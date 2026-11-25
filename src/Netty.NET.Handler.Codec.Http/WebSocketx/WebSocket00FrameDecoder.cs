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

using System;
using System.Collections.Generic;
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using static Netty.NET.Buffer.ByteBufUtil;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * Decodes {@link ByteBuf}s into {@link WebSocketFrame}s.
 * <p>
 * For the detailed instruction on adding add Web Socket support to your HTTP server, take a look into the
 * <tt>WebSocketServer</tt> example located in the {@code io.netty.example.http.websocket} package.
 */
// CLR: ReplayingDecoder<Void> is ReplayingDecoder<object> (C# needs a type argument;
// test/Netty.NET.Handler.Codec.Tests/ReplayingDecoderTest.cs records the same mapping), and decode overrides the
// codec-base Decode (List<Object> out is IList<object> @out). The package-private DEFAULT_MAX_FRAME_SIZE is an internal
// const. ByteBuf bytes are unsigned System.Byte (owner decision): type == (byte) 0xFF is type == 0xFF, and the masks
// (type & 0x80, b & 0x7f, b & 0x80) give the same values as for the signed Java byte. The seven length bits are widened
// as uint before the OR (CS0675; the value is never negative). The static import ByteBufUtil.readBytes is using static
// ByteBufUtil, whose ReadBytes(alloc, buffer, length) reads through buffer.ReadBytes(dst), which the replaying buffer
// supports, when the allocator returns exactly length writable bytes (src/Netty.NET.Buffer/ByteBufUtil.Allocation.cs);
// IllegalArgumentException is ArgumentException, and numbers in messages use the invariant culture.
public class WebSocket00FrameDecoder : ReplayingDecoder<object>, IWebSocketFrameDecoder
{

    internal const int DEFAULT_MAX_FRAME_SIZE = 16384;

    private readonly long maxFrameSize;
    private bool receivedClosingHandshake;

    public WebSocket00FrameDecoder() : this(DEFAULT_MAX_FRAME_SIZE)
    {
    }

    /**
     * Creates a new instance of {@code WebSocketFrameDecoder} with the specified {@code maxFrameSize}. If the client
     * sends a frame size larger than {@code maxFrameSize}, the channel will be closed.
     *
     * @param maxFrameSize
     *            the maximum frame size to decode
     */
    public WebSocket00FrameDecoder(int maxFrameSize)
    {
        this.maxFrameSize = maxFrameSize;
    }

    /**
     * Creates a new instance of {@code WebSocketFrameDecoder} with the specified {@code maxFrameSize}. If the client
     * sends a frame size larger than {@code maxFrameSize}, the channel will be closed.
     *
     * @param decoderConfig
     *            Frames decoder configuration.
     */
    public WebSocket00FrameDecoder(WebSocketDecoderConfig decoderConfig)
    {
        this.maxFrameSize = ObjectUtil.CheckNotNull(decoderConfig, "decoderConfig").MaxFramePayloadLength();
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        // Discard all data received if closing handshake was received before.
        if (receivedClosingHandshake)
        {
            @in.SkipBytes(ActualReadableBytes());
            return;
        }

        // Decode a frame otherwise.
        byte type = @in.ReadByte();
        WebSocketFrame frame;
        if ((type & 0x80) == 0x80)
        {
            // If the MSB on type is set, decode the frame length
            frame = DecodeBinaryFrame(ctx, type, @in);
        }
        else
        {
            // Decode a 0xff terminated UTF-8 string
            frame = DecodeTextFrame(ctx, @in);
        }

        if (frame != null)
        {
            @out.Add(frame);
        }
    }

    private WebSocketFrame DecodeBinaryFrame(IChannelHandlerContext ctx, byte type, ByteBuf buffer)
    {
        long frameSize = 0;
        int lengthFieldSize = 0;
        byte b;
        do
        {
            b = buffer.ReadByte();
            frameSize <<= 7;
            frameSize |= (uint) (b & 0x7f);
            if (frameSize > maxFrameSize)
            {
                throw new TooLongFrameException("frame length exceeds " +
                        maxFrameSize.ToString(CultureInfo.InvariantCulture) + ": " +
                        frameSize.ToString(CultureInfo.InvariantCulture));
            }
            lengthFieldSize++;
            if (lengthFieldSize > 8)
            {
                // Perhaps a malicious peer?
                throw new TooLongFrameException("frame length field size exceeds 8: " +
                                                lengthFieldSize.ToString(CultureInfo.InvariantCulture));
            }
        } while ((b & 0x80) == 0x80);

        if (type == 0xFF && frameSize == 0)
        {
            receivedClosingHandshake = true;
            return new CloseWebSocketFrame(true, 0, ctx.Alloc().Buffer(0));
        }
        ByteBuf payload = ReadBytes(ctx.Alloc(), buffer, (int) frameSize);
        return new BinaryWebSocketFrame(payload);
    }

    private WebSocketFrame DecodeTextFrame(IChannelHandlerContext ctx, ByteBuf buffer)
    {
        int ridx = buffer.ReaderIndex;
        int rbytes = ActualReadableBytes();
        int delimPos = buffer.IndexOf(ridx, ridx + rbytes, (byte) 0xFF);
        if (delimPos == -1)
        {
            // Frame delimiter (0xFF) not found
            if (rbytes > maxFrameSize)
            {
                // Frame length exceeded the maximum
                throw new TooLongFrameException("frame length exceeds " +
                        maxFrameSize.ToString(CultureInfo.InvariantCulture) + ": " +
                        rbytes.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                // Wait until more data is received
                return null;
            }
        }

        int frameSize = delimPos - ridx;
        if (frameSize > maxFrameSize)
        {
            throw new TooLongFrameException("frame length exceeds " +
                    maxFrameSize.ToString(CultureInfo.InvariantCulture) + ": " +
                    frameSize.ToString(CultureInfo.InvariantCulture));
        }

        ByteBuf binaryData = ReadBytes(ctx.Alloc(), buffer, frameSize);
        buffer.SkipBytes(1);

        int ffDelimPos = binaryData.IndexOf(binaryData.ReaderIndex, binaryData.WriterIndex, (byte) 0xFF);
        if (ffDelimPos >= 0)
        {
            binaryData.Release();
            throw new ArgumentException("a text frame should not contain 0xFF.");
        }

        return new TextWebSocketFrame(binaryData);
    }
}
