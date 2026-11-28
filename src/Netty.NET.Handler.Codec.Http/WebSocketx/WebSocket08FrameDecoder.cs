/*
 * Copyright 2019 The Netty Project
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


using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * Decodes a web socket frame from wire protocol version 8 format. This code was forked from <a
 * href="https://github.com/joewalnes/webbit">webbit</a> and modified.
 */
// CLR: decode overrides the codec-base ByteToMessageDecoder.Decode (List<Object> out is IList<object> @out); the
// package-private nested enum State is an internal nested C# enum. C# switch sections cannot fall through, so each
// state that continues with the next one ends with goto case (https://learn.microsoft.com/en-us/dotnet/csharp/language-
// reference/statements/selection-statements), and the unreachable default's throw new Error(...) is
// UnreachableException, as AssertionError is in the handler port (docs/handler-porting.md, Exceptions). ByteBuf bytes
// are unsigned System.Byte (owner decision): the masks of the first and second byte ((b & 0x80) != 0, (b & 0x70) >> 4,
// b & 0x0F, b & 0x7F) give the same values as for the signed Java byte. getShort returns the signed short in the Buffer
// port as in Java, so checkCloseFrameBody sees the same (possibly negative) status code. The Buffer port has no
// ByteBuf.order(): buffers are always big-endian and byte order is chosen by explicit BE/LE accessors
// (docs/codec-base-porting.md, Byte order), so unmask's ByteOrder lookup and its LITTLE_ENDIAN branch, which Java takes
// only for a deprecated swapped buffer, have no counterpart. ByteBufUtil.readBytes is the Buffer port's
// ByteBufUtil.ReadBytes. ctx.writeAndFlush(closeMessage).addListener(ChannelFutureListener.CLOSE) closes ctx.Channel
// from a synchronous continuation of the write Task on TaskScheduler.Default, as codec-base MessageAggregator does
// (docs/codec-base-porting.md). UnsupportedOperationException is NotSupportedException, and numbers in messages use the
// invariant culture. checkCloseFrameBody is protected virtual because the Java method is overridable.
public class WebSocket08FrameDecoder : ByteToMessageDecoder, IWebSocketFrameDecoder
{

    internal enum State
    {
        READING_FIRST,
        READING_SECOND,
        READING_SIZE,
        MASKING_KEY,
        PAYLOAD,
        CORRUPT
    }

    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(WebSocket08FrameDecoder));

    private const byte OPCODE_CONT = 0x0;
    private const byte OPCODE_TEXT = 0x1;
    private const byte OPCODE_BINARY = 0x2;
    private const byte OPCODE_CLOSE = 0x8;
    private const byte OPCODE_PING = 0x9;
    private const byte OPCODE_PONG = 0xA;

    private readonly WebSocketDecoderConfig config;

    private int fragmentedFramesCount;
    private bool frameFinalFlag;
    private bool frameMasked;
    private int frameRsv;
    private int frameOpcode;
    private long framePayloadLength;
    private int mask;
    private int framePayloadLen1;
    private bool receivedClosingHandshake;
    private State state = State.READING_FIRST;

    /**
     * Constructor
     *
     * @param expectMaskedFrames
     *            Web socket servers must set this to true processed incoming masked payload. Client implementations
     *            must set this to false.
     * @param allowExtensions
     *            Flag to allow reserved extension bits to be used or not
     * @param maxFramePayloadLength
     *            Maximum length of a frame's payload. Setting this to an appropriate value for you application
     *            helps check for denial of services attacks.
     */
    public WebSocket08FrameDecoder(bool expectMaskedFrames, bool allowExtensions, int maxFramePayloadLength)
        : this(expectMaskedFrames, allowExtensions, maxFramePayloadLength, false)
    {
    }

    /**
     * Constructor
     *
     * @param expectMaskedFrames
     *            Web socket servers must set this to true processed incoming masked payload. Client implementations
     *            must set this to false.
     * @param allowExtensions
     *            Flag to allow reserved extension bits to be used or not
     * @param maxFramePayloadLength
     *            Maximum length of a frame's payload. Setting this to an appropriate value for you application
     *            helps check for denial of services attacks.
     * @param allowMaskMismatch
     *            When set to true, frames which are not masked properly according to the standard will still be
     *            accepted.
     */
    public WebSocket08FrameDecoder(bool expectMaskedFrames, bool allowExtensions, int maxFramePayloadLength,
                                   bool allowMaskMismatch)
        : this(WebSocketDecoderConfig.NewBuilder()
            .ExpectMaskedFrames(expectMaskedFrames)
            .AllowExtensions(allowExtensions)
            .MaxFramePayloadLength(maxFramePayloadLength)
            .AllowMaskMismatch(allowMaskMismatch)
            .Build())
    {
    }

    /**
     * Constructor
     *
     * @param decoderConfig
     *            Frames decoder configuration.
     */
    public WebSocket08FrameDecoder(WebSocketDecoderConfig decoderConfig)
    {
        this.config = ObjectUtil.CheckNotNull(decoderConfig, "decoderConfig");
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        // Discard all data received if closing handshake was received before.
        if (receivedClosingHandshake)
        {
            @in.SkipBytes(ActualReadableBytes());
            return;
        }

        byte b;

        switch (state)
        {
        case State.READING_FIRST:
            if (!@in.IsReadable())
            {
                return;
            }

            framePayloadLength = 0;

            // FIN, RSV, OPCODE
            b = @in.ReadByte();
            frameFinalFlag = (b & 0x80) != 0;
            frameRsv = (b & 0x70) >> 4;
            frameOpcode = b & 0x0F;

            if (logger.IsTraceEnabled())
            {
                logger.Trace("Decoding WebSocket Frame opCode={}", frameOpcode);
            }

            state = State.READING_SECOND;
            goto case State.READING_SECOND;
        case State.READING_SECOND:
            if (!@in.IsReadable())
            {
                return;
            }

            // MASK, PAYLOAD LEN 1
            b = @in.ReadByte();
            frameMasked = (b & 0x80) != 0;
            framePayloadLen1 = b & 0x7F;

            if (frameRsv != 0 && !config.AllowExtensions())
            {
                ProtocolViolation(ctx, @in, "RSV != 0 and no extension negotiated, RSV:" +
                                            frameRsv.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (!config.AllowMaskMismatch() && config.ExpectMaskedFrames() != frameMasked)
            {
                ProtocolViolation(ctx, @in, "received a frame that is not masked as expected");
                return;
            }

            if (frameOpcode > 7)
            { // control frame (have MSB in opcode set)

                // control frames MUST NOT be fragmented
                if (!frameFinalFlag)
                {
                    ProtocolViolation(ctx, @in, "fragmented control frame");
                    return;
                }

                // control frames MUST have payload 125 octets or less
                if (framePayloadLen1 > 125)
                {
                    ProtocolViolation(ctx, @in, "control frame with payload length > 125 octets");
                    return;
                }

                // check for reserved control frame opcodes
                if (!(frameOpcode == OPCODE_CLOSE || frameOpcode == OPCODE_PING
                      || frameOpcode == OPCODE_PONG))
                {
                    ProtocolViolation(ctx, @in, "control frame using reserved opcode " +
                                                frameOpcode.ToString(CultureInfo.InvariantCulture));
                    return;
                }

                // close frame : if there is a body, the first two bytes of the
                // body MUST be a 2-byte unsigned integer (in network byte
                // order) representing a getStatus code
                if (frameOpcode == 8 && framePayloadLen1 == 1)
                {
                    ProtocolViolation(ctx, @in, "received close control frame with payload len 1");
                    return;
                }
            }
            else
            { // data frame
                // check for reserved data frame opcodes
                if (!(frameOpcode == OPCODE_CONT || frameOpcode == OPCODE_TEXT
                      || frameOpcode == OPCODE_BINARY))
                {
                    ProtocolViolation(ctx, @in, "data frame using reserved opcode " +
                                                frameOpcode.ToString(CultureInfo.InvariantCulture));
                    return;
                }

                // check opcode vs message fragmentation state 1/2
                if (fragmentedFramesCount == 0 && frameOpcode == OPCODE_CONT)
                {
                    ProtocolViolation(ctx, @in, "received continuation data frame outside fragmented message");
                    return;
                }

                // check opcode vs message fragmentation state 2/2
                if (fragmentedFramesCount != 0 && frameOpcode != OPCODE_CONT)
                {
                    ProtocolViolation(ctx, @in,
                                      "received non-continuation data frame while inside fragmented message");
                    return;
                }
            }

            state = State.READING_SIZE;
            goto case State.READING_SIZE;
        case State.READING_SIZE:

            // Read frame payload length
            if (framePayloadLen1 == 126)
            {
                if (@in.ReadableBytes < 2)
                {
                    return;
                }

                framePayloadLength = @in.ReadUnsignedShort();
                if (framePayloadLength < 126)
                {
                    ProtocolViolation(ctx, @in, "invalid data frame length (not using minimal length encoding)");
                    return;
                }
            }
            else if (framePayloadLen1 == 127)
            {
                if (@in.ReadableBytes < 8)
                {
                    return;
                }

                framePayloadLength = @in.ReadLong();
                if (framePayloadLength < 0)
                {
                    ProtocolViolation(ctx, @in, "invalid data frame length (negative length)");
                    return;
                }

                if (framePayloadLength < 65536)
                {
                    ProtocolViolation(ctx, @in, "invalid data frame length (not using minimal length encoding)");
                    return;
                }
            }
            else
            {
                framePayloadLength = framePayloadLen1;
            }

            if (framePayloadLength > config.MaxFramePayloadLength())
            {
                ProtocolViolation(ctx, @in, WebSocketCloseStatus.MESSAGE_TOO_BIG,
                    "Max frame length of " + config.MaxFramePayloadLength().ToString(CultureInfo.InvariantCulture) +
                    " has been exceeded.");
                return;
            }

            if (logger.IsTraceEnabled())
            {
                logger.Trace("Decoding WebSocket Frame length={}", framePayloadLength);
            }

            state = State.MASKING_KEY;
            goto case State.MASKING_KEY;
        case State.MASKING_KEY:
            if (frameMasked)
            {
                if (@in.ReadableBytes < 4)
                {
                    return;
                }

                mask = @in.ReadInt();
            }

            state = State.PAYLOAD;
            goto case State.PAYLOAD;
        case State.PAYLOAD:
            if (@in.ReadableBytes < framePayloadLength)
            {
                return;
            }

            ByteBuf payloadBuffer = Unpooled.EmptyBuffer;

            try
            {
                if (framePayloadLength > 0)
                {
                    payloadBuffer = ByteBufUtil.ReadBytes(ctx.Alloc(), @in, ToFrameLength(framePayloadLength));
                }

                // Now we have all the data, the next checkpoint must be the next
                // frame
                state = State.READING_FIRST;

                // Unmask data if needed
                if (frameMasked & framePayloadLength > 0)
                {
                    Unmask(payloadBuffer);
                }

                // Processing ping/pong/close frames because they cannot be
                // fragmented
                if (frameOpcode == OPCODE_PING)
                {
                    @out.Add(new PingWebSocketFrame(frameFinalFlag, frameRsv, payloadBuffer));
                    payloadBuffer = null;
                    return;
                }

                if (frameOpcode == OPCODE_PONG)
                {
                    @out.Add(new PongWebSocketFrame(frameFinalFlag, frameRsv, payloadBuffer));
                    payloadBuffer = null;
                    return;
                }

                if (frameOpcode == OPCODE_CLOSE)
                {
                    receivedClosingHandshake = true;
                    CheckCloseFrameBody(ctx, payloadBuffer);
                    @out.Add(new CloseWebSocketFrame(frameFinalFlag, frameRsv, payloadBuffer));
                    payloadBuffer = null;
                    return;
                }

                // Processing for possible fragmented messages for text and binary
                // frames
                if (frameFinalFlag)
                {
                    // Final frame of the sequence. Apparently ping frames are
                    // allowed in the middle of a fragmented message
                    fragmentedFramesCount = 0;
                }
                else
                {
                    // Increment counter
                    fragmentedFramesCount++;
                }

                // Return the frame
                if (frameOpcode == OPCODE_TEXT)
                {
                    @out.Add(new TextWebSocketFrame(frameFinalFlag, frameRsv, payloadBuffer));
                    payloadBuffer = null;
                    return;
                }
                else if (frameOpcode == OPCODE_BINARY)
                {
                    @out.Add(new BinaryWebSocketFrame(frameFinalFlag, frameRsv, payloadBuffer));
                    payloadBuffer = null;
                    return;
                }
                else if (frameOpcode == OPCODE_CONT)
                {
                    @out.Add(new ContinuationWebSocketFrame(frameFinalFlag, frameRsv,
                                                           payloadBuffer));
                    payloadBuffer = null;
                    return;
                }
                else
                {
                    throw new System.NotSupportedException("Cannot decode web socket frame with opcode: "
                                                           + frameOpcode.ToString(CultureInfo.InvariantCulture));
                }
            }
            finally
            {
                if (payloadBuffer != null)
                {
                    payloadBuffer.Release();
                }
            }
        case State.CORRUPT:
            if (@in.IsReadable())
            {
                // If we don't keep reading Netty will throw an exception saying
                // we can't return null if no bytes read and state not changed.
                @in.ReadByte();
            }

            return;
        default:
            throw new UnreachableException("Shouldn't reach here (state: " + state + ")");
        }
    }

    private void Unmask(ByteBuf frame)
    {
        int i = frame.ReaderIndex;
        int end = frame.WriterIndex;

        int intMask = mask;
        if (intMask == 0)
        {
            // If the mask is 0 we can just return directly as the XOR operations will just produce the same value.
            return;
        }

        // Avoid sign extension on widening primitive conversion
        long longMask = intMask & 0xFFFFFFFFL;
        longMask |= longMask << 32;

        for (int lim = end - 7; i < lim; i += 8)
        {
            frame.SetLong(i, frame.GetLong(i) ^ longMask);
        }

        if (i < end - 3)
        {
            frame.SetInt(i, frame.GetInt(i) ^ (int) longMask);
            i += 4;
        }

        int maskOffset = 0;

        for (; i < end; i++)
        {
            frame.SetByte(i, frame.GetByte(i) ^ WebSocketUtil.ByteAtIndex(intMask, maskOffset++ & 3));
        }
    }

    private void ProtocolViolation(IChannelHandlerContext ctx, ByteBuf @in, string reason)
    {
        ProtocolViolation(ctx, @in, WebSocketCloseStatus.PROTOCOL_ERROR, reason);
    }

    private void ProtocolViolation(IChannelHandlerContext ctx, ByteBuf @in, WebSocketCloseStatus status, string reason)
    {
        ProtocolViolation(ctx, @in, new CorruptedWebSocketFrameException(status, reason));
    }

    private void ProtocolViolation(IChannelHandlerContext ctx, ByteBuf @in, CorruptedWebSocketFrameException ex)
    {
        state = State.CORRUPT;
        int readableBytes = @in.ReadableBytes;
        if (readableBytes > 0)
        {
            // Fix for memory leak, caused by ByteToMessageDecoder#channelRead:
            // buffer 'cumulation' is released ONLY when no more readable bytes available.
            @in.SkipBytes(readableBytes);
        }

        if (ctx.Channel.IsActive && config.CloseOnProtocolViolation())
        {
            object closeMessage;

            if (receivedClosingHandshake)
            {
                closeMessage = Unpooled.EmptyBuffer;
            }
            else
            {
                WebSocketCloseStatus closeStatus = ex.CloseStatus();
                string reasonText = ex.Message;
                if (reasonText == null)
                {
                    reasonText = closeStatus.ReasonText();
                }

                closeMessage = new CloseWebSocketFrame(closeStatus, reasonText);
            }

            _ = ctx.WriteAndFlushAsync(closeMessage).ContinueWith(_ => ctx.Channel.CloseAsync(),
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        throw ex;
    }

    private static int ToFrameLength(long l)
    {
        if (l > int.MaxValue)
        {
            throw new TooLongFrameException("frame length exceeds " +
                                            int.MaxValue.ToString(CultureInfo.InvariantCulture) + ": " +
                                            l.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            return (int) l;
        }
    }

    /** */
    protected virtual void CheckCloseFrameBody(
            IChannelHandlerContext ctx, ByteBuf buffer)
    {
        if (buffer == null || !buffer.IsReadable())
        {
            return;
        }

        if (buffer.ReadableBytes < 2)
        {
            ProtocolViolation(ctx, buffer, WebSocketCloseStatus.INVALID_PAYLOAD_DATA, "Invalid close frame body");
        }

        // Must have 2 byte integer within the valid range
        int statusCode = buffer.GetShort(buffer.ReaderIndex);
        if (!WebSocketCloseStatus.IsValidStatusCode(statusCode))
        {
            ProtocolViolation(ctx, buffer, "Invalid close frame getStatus code: " +
                                           statusCode.ToString(CultureInfo.InvariantCulture));
        }

        // May have UTF-8 message
        if (buffer.ReadableBytes > 2)
        {
            try
            {
                new Utf8Validator().Check(buffer, buffer.ReaderIndex + 2, buffer.ReadableBytes - 2);
            }
            catch (CorruptedWebSocketFrameException ex)
            {
                ProtocolViolation(ctx, buffer, ex);
            }
        }
    }
}
