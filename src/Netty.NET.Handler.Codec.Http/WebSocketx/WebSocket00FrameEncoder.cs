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

using System.Collections.Generic;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * Encodes a {@link WebSocketFrame} into a {@link ByteBuf}.
 * <p>
 * For the detailed instruction on adding add Web Socket support to your HTTP server, take a look into the
 * <tt>WebSocketServer</tt> example located in the {@code io.netty.example.http.websocket} package.
 */
[Sharable]
// CLR: @Sharable is [Sharable]; encode overrides the codec-base MessageToMessageEncoder<WebSocketFrame>.Encode and
// super(WebSocketFrame.class) is base(typeof(WebSocketFrame)) (docs/codec-base-porting.md, Type matching). The static
// buffers keep LeakPresenceDetector.StaticInitializer, Unpooled.UnreleasableBuffer, DirectBuffer and AsReadOnly of the
// Common and Buffer ports, as core's HttpObjectEncoder does. ByteBuf.WriteByte takes an int and writes its low eight
// bits, so (byte) 0xFF and (byte) 0x80 are written as 0xFF and 0x80.
public class WebSocket00FrameEncoder : MessageToMessageEncoder<WebSocketFrame>, IWebSocketFrameEncoder
{
    private static readonly ByteBuf _0X00 = LeakPresenceDetector.StaticInitializer(() => Unpooled.UnreleasableBuffer(
            Unpooled.DirectBuffer(1, 1).WriteByte(0x00)).AsReadOnly());
    private static readonly ByteBuf _0XFF = LeakPresenceDetector.StaticInitializer(() => Unpooled.UnreleasableBuffer(
            Unpooled.DirectBuffer(1, 1).WriteByte(0xFF)).AsReadOnly());
    private static readonly ByteBuf _0XFF_0X00 = LeakPresenceDetector.StaticInitializer(() =>
        Unpooled.UnreleasableBuffer(
            Unpooled.DirectBuffer(2, 2).WriteByte(0xFF).WriteByte(0x00)).AsReadOnly());

    public WebSocket00FrameEncoder() : base(typeof(WebSocketFrame))
    {
    }

    protected override void Encode(IChannelHandlerContext ctx, WebSocketFrame msg, IList<object> @out)
    {
        if (msg is TextWebSocketFrame)
        {
            // Text frame
            ByteBuf data = msg.Content;

            @out.Add(_0X00.Duplicate());
            @out.Add(data.Retain());
            @out.Add(_0XFF.Duplicate());
        }
        else if (msg is CloseWebSocketFrame)
        {
            // Close frame, needs to call duplicate to allow multiple writes.
            // See https://github.com/netty/netty/issues/2768
            @out.Add(_0XFF_0X00.Duplicate());
        }
        else
        {
            // Binary frame
            ByteBuf data = msg.Content;
            int dataLen = data.ReadableBytes;

            ByteBuf buf = ctx.Alloc().Buffer(5);
            bool release = true;
            try
            {
                // Encode type.
                buf.WriteByte(0x80);

                // Encode length.
                int b1 = dataLen >>> 28 & 0x7F;
                int b2 = dataLen >>> 14 & 0x7F;
                int b3 = dataLen >>> 7 & 0x7F;
                int b4 = dataLen & 0x7F;
                if (b1 == 0)
                {
                    if (b2 == 0)
                    {
                        if (b3 != 0)
                        {
                            buf.WriteByte(b3 | 0x80);
                        }
                        buf.WriteByte(b4);
                    }
                    else
                    {
                        buf.WriteByte(b2 | 0x80);
                        buf.WriteByte(b3 | 0x80);
                        buf.WriteByte(b4);
                    }
                }
                else
                {
                    buf.WriteByte(b1 | 0x80);
                    buf.WriteByte(b2 | 0x80);
                    buf.WriteByte(b3 | 0x80);
                    buf.WriteByte(b4);
                }

                // Encode binary data.
                @out.Add(buf);
                @out.Add(data.Retain());
                release = false;
            }
            finally
            {
                if (release)
                {
                    buf.Release();
                }
            }
        }
    }
}
