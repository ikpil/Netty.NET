/*
 * Copyright 2014 The Netty Project
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

using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx;

// CLR: readOutbound() is ReadOutbound<T>() of the Transport EmbeddedChannel. ReadByte() returns the unsigned
// System.Byte (owner decision), so the expected octets (byte) 0xFF and (byte) 0x00 are the unsigned values 0xFF and
// 0x00.
public class WebSocket00FrameEncoderTest
{

    // Test for https://github.com/netty/netty/issues/2768
    [Fact]
    public void TestMultipleWebSocketCloseFrames()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new WebSocket00FrameEncoder());
        Assert.True(channel.WriteOutbound(new CloseWebSocketFrame()));
        Assert.True(channel.WriteOutbound(new CloseWebSocketFrame()));
        Assert.True(channel.Finish());
        AssertCloseWebSocketFrame(channel);
        AssertCloseWebSocketFrame(channel);
        Assert.Null(channel.ReadOutbound<object>());
    }

    private static void AssertCloseWebSocketFrame(EmbeddedChannel channel)
    {
        ByteBuf buf = channel.ReadOutbound<ByteBuf>();
        Assert.Equal(2, buf.ReadableBytes);
        Assert.Equal((byte) 0xFF, buf.ReadByte());
        Assert.Equal((byte) 0x00, buf.ReadByte());
        buf.Release();
    }
}
