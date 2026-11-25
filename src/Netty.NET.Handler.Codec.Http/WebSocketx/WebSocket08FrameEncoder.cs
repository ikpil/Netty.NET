/*
 * Copyright 2012 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */
// (BSD License: https://www.opensource.org/licenses/bsd-license)
//
// Copyright (c) 2011, Joe Walnes and contributors
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or
// without modification, are permitted provided that the
// following conditions are met:
//
// * Redistributions of source code must retain the above
// copyright notice, this list of conditions and the
// following disclaimer.
//
// * Redistributions in binary form must reproduce the above
// copyright notice, this list of conditions and the
// following disclaimer in the documentation and/or other
// materials provided with the distribution.
//
// * Neither the name of the Webbit nor the names of
// its contributors may be used to endorse or promote products
// derived from this software without specific prior written
// permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND
// CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES,
// INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF
// MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR
// CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT,
// INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
// (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE
// GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR
// BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
// LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT
// OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
// POSSIBILITY OF SUCH DAMAGE.

using System;
using System.Collections.Generic;
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * <p>
 * Encodes a web socket frame into wire protocol version 8 format. This code was forked from <a
 * href="https://github.com/joewalnes/webbit">webbit</a> and modified.
 * </p>
 */
// CLR: encode overrides the codec-base MessageToMessageEncoder<WebSocketFrame>.Encode (List<Object> out is
// IList<object> @out) and super(WebSocketFrame.class) is base(typeof(WebSocketFrame)) (docs/codec-base-porting.md, Type
// matching). WebSocketFrameEncoder and WebSocketFrameMaskGenerator are IWebSocketFrameEncoder and
// IWebSocketFrameMaskGenerator. The Buffer port has no ByteBuf.order(): buffers are always big-endian and byte order is
// chosen by explicit BE/LE accessors (docs/codec-base-porting.md, Byte order), so the source and destination orders of
// the masking loop always match and the LITTLE_ENDIAN mask reversal, which Java needs only for deprecated swapped
// buffers, has no counterpart. The masked byte loop XORs the unsigned octet with the mask byte; WriteByte keeps the low
// eight bits, as Java does with the sign-extended byte. getClass().getName() is GetType().FullName,
// UnsupportedOperationException is NotSupportedException, the int constants are const, and numbers in messages use the
// invariant culture.
public class WebSocket08FrameEncoder : MessageToMessageEncoder<WebSocketFrame>, IWebSocketFrameEncoder
{

    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(WebSocket08FrameEncoder));

    private const byte OPCODE_CONT = 0x0;
    private const byte OPCODE_TEXT = 0x1;
    private const byte OPCODE_BINARY = 0x2;
    private const byte OPCODE_CLOSE = 0x8;
    private const byte OPCODE_PING = 0x9;
    private const byte OPCODE_PONG = 0xA;

    /**
     * The size threshold for gathering writes. Non-Masked messages bigger than this size will be sent fragmented as
     * a header and a content ByteBuf whereas messages smaller than the size will be merged into a single buffer and
     * sent at once.<br>
     * Masked messages will always be sent at once.
     */
    private const int GATHERING_WRITE_THRESHOLD = 1024;

    private readonly IWebSocketFrameMaskGenerator maskGenerator;

    /**
     * Constructor
     *
     * @param maskPayload
     *            Web socket clients must set this to true to mask payload. Server implementations must set this to
     *            false.
     */
    public WebSocket08FrameEncoder(bool maskPayload)
        : this(maskPayload ? RandomWebSocketFrameMaskGenerator.INSTANCE : null)
    {
    }

    /**
     * Constructor
     *
     * @param maskGenerator
     *            Web socket clients must set this to {@code non null} to mask payload.
     *            Server implementations must set this to {@code null}.
     */
    public WebSocket08FrameEncoder(IWebSocketFrameMaskGenerator maskGenerator) : base(typeof(WebSocketFrame))
    {
        this.maskGenerator = maskGenerator;
    }

    protected override void Encode(IChannelHandlerContext ctx, WebSocketFrame msg, IList<object> @out)
    {
        ByteBuf data = msg.Content;

        byte opcode = GetOpCode(msg);

        int length = data.ReadableBytes;

        if (logger.IsTraceEnabled())
        {
            logger.Trace("Encoding WebSocket Frame opCode={} length={}", opcode, length);
        }

        int b0 = 0;
        if (msg.IsFinalFragment())
        {
            b0 |= 1 << 7;
        }
        b0 |= (msg.Rsv() & 0x07) << 4;
        b0 |= opcode & 0x7F;

        if (opcode == OPCODE_PING && length > 125)
        {
            throw new TooLongFrameException("invalid payload for PING (payload length must be <= 125, was " +
                                            length.ToString(CultureInfo.InvariantCulture));
        }

        bool release = true;
        ByteBuf buf = null;
        try
        {
            int maskLength = maskGenerator != null ? 4 : 0;
            if (length <= 125)
            {
                int size = 2 + maskLength + length;
                buf = ctx.Alloc().Buffer(size);
                buf.WriteByte(b0);
                byte b = (byte) (maskGenerator != null ? 0x80 | length : length);
                buf.WriteByte(b);
            }
            else if (length <= 0xFFFF)
            {
                int size = 4 + maskLength;
                if (maskGenerator != null || length <= GATHERING_WRITE_THRESHOLD)
                {
                    size += length;
                }
                buf = ctx.Alloc().Buffer(size);
                buf.WriteByte(b0);
                buf.WriteByte(maskGenerator != null ? 0xFE : 126);
                buf.WriteByte(length >>> 8 & 0xFF);
                buf.WriteByte(length & 0xFF);
            }
            else
            {
                int size = 10 + maskLength;
                if (maskGenerator != null)
                {
                    size += length;
                }
                buf = ctx.Alloc().Buffer(size);
                buf.WriteByte(b0);
                buf.WriteByte(maskGenerator != null ? 0xFF : 127);
                buf.WriteLong(length);
            }

            // Write payload
            if (maskGenerator != null)
            {
                int mask = maskGenerator.NextMask();
                buf.WriteInt(mask);

                // If the mask is 0 we can skip all the XOR operations.
                if (mask != 0)
                {
                    if (length > 0)
                    {
                        int i = data.ReaderIndex;
                        int end = data.WriterIndex;

                        // CLR: the Buffer port has no order(); both buffers are big-endian, so their orders always
                        // match and srcOrder == dstOrder holds (see the class comment).
                        {
                            // Use the optimized path only when byte orders match.
                            // Avoid sign extension on widening primitive conversion
                            long longMask = mask & 0xFFFFFFFFL;
                            longMask |= longMask << 32;

                            // If the byte order of our buffers it little endian we have to bring our mask
                            // into the same format, because getInt() and writeInt() will use a reversed byte order
                            // CLR: never the case in the Buffer port (big-endian buffers only).

                            for (int lim = end - 7; i < lim; i += 8)
                            {
                                buf.WriteLong(data.GetLong(i) ^ longMask);
                            }

                            if (i < end - 3)
                            {
                                buf.WriteInt(data.GetInt(i) ^ (int) longMask);
                                i += 4;
                            }
                        }
                        int maskOffset = 0;
                        for (; i < end; i++)
                        {
                            byte byteData = data.GetByte(i);
                            buf.WriteByte(byteData ^ WebSocketUtil.ByteAtIndex(mask, maskOffset++ & 3));
                        }
                    }
                    @out.Add(buf);
                }
                else
                {
                    AddBuffers(buf, data, @out);
                }
            }
            else
            {
                AddBuffers(buf, data, @out);
            }
            release = false;
        }
        finally
        {
            if (release && buf != null)
            {
                buf.Release();
            }
        }
    }

    private static byte GetOpCode(WebSocketFrame msg)
    {
        if (msg is TextWebSocketFrame)
        {
            return OPCODE_TEXT;
        }
        if (msg is BinaryWebSocketFrame)
        {
            return OPCODE_BINARY;
        }
        if (msg is PingWebSocketFrame)
        {
            return OPCODE_PING;
        }
        if (msg is PongWebSocketFrame)
        {
            return OPCODE_PONG;
        }
        if (msg is CloseWebSocketFrame)
        {
            return OPCODE_CLOSE;
        }
        if (msg is ContinuationWebSocketFrame)
        {
            return OPCODE_CONT;
        }
        throw new NotSupportedException("Cannot encode frame of type: " + msg.GetType().FullName);
    }

    private static void AddBuffers(ByteBuf buf, ByteBuf data, IList<object> @out)
    {
        int readableBytes = data.ReadableBytes;
        if (buf.WritableBytes >= readableBytes)
        {
            // merge buffers as this is cheaper then a gathering write if the payload is small enough
            buf.WriteBytes(data);
            @out.Add(buf);
        }
        else
        {
            @out.Add(buf);
            if (readableBytes > 0)
            {
                @out.Add(data.Retain());
            }
        }
    }
}
