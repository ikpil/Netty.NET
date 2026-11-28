/*
 * Copyright 2013 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the "License");
 * you may not use this file except in compliance with the License. You may obtain a copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License is
 * distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and limitations under the License.
 */

using System;
using System.Collections.Generic;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx;

// CLR: Mockito's mock(ChannelHandlerContext.class) and verify(ctx).fireChannelInactive() are a Moq
// Mock<IChannelHandlerContext> and ctx.Verify(c => c.FireChannelInactive()). Set<Integer>/HashSet is HashSet<int>
// (removeAll is ExceptWith). The anonymous Executable is a lambda for Assert.ThrowsAny (JUnit's assertThrows accepts
// subtypes too), getMessage() is Message, and readInbound()/readOutbound() are ReadInbound<T>()/ReadOutbound<T>().
public class WebSocket08FrameDecoderTest
{

    [Fact]
    public void ChannelInactive()
    {
        WebSocket08FrameDecoder decoder = new WebSocket08FrameDecoder(true, true, 65535, false);
        Mock<IChannelHandlerContext> ctx = new Mock<IChannelHandlerContext>();
        decoder.ChannelInactive(ctx.Object);
        ctx.Verify(c => c.FireChannelInactive());
    }

    [Fact]
    public void SupportIanaStatusCodes()
    {
        ISet<int> forbiddenIanaCodes = new HashSet<int>();
        forbiddenIanaCodes.Add(1004);
        forbiddenIanaCodes.Add(1005);
        forbiddenIanaCodes.Add(1006);
        ISet<int> validIanaCodes = new HashSet<int>();

        for (int i = 1000; i < 1015; i++)
        {
            validIanaCodes.Add(i);
        }

        validIanaCodes.ExceptWith(forbiddenIanaCodes);

        foreach (int statusCode in validIanaCodes)
        {
            EmbeddedChannel encoderChannel = new EmbeddedChannel(new WebSocket08FrameEncoder(true));
            EmbeddedChannel decoderChannel = new EmbeddedChannel(new WebSocket08FrameDecoder(true, true, 65535, false));

            Assert.True(encoderChannel.WriteOutbound(new CloseWebSocketFrame(statusCode, "Bye")));
            Assert.True(encoderChannel.Finish());
            ByteBuf serializedCloseFrame = encoderChannel.ReadOutbound<ByteBuf>();
            Assert.Null(encoderChannel.ReadOutbound<object>());

            Assert.True(decoderChannel.WriteInbound(serializedCloseFrame));
            Assert.True(decoderChannel.Finish());

            CloseWebSocketFrame outputFrame = decoderChannel.ReadInbound<CloseWebSocketFrame>();
            Assert.Null(decoderChannel.ReadOutbound<object>());

            try
            {
                Assert.Equal(statusCode, outputFrame.StatusCode());
            }
            finally
            {
                outputFrame.Release();
            }
        }
    }

    [Fact]
    public void ProtocolViolationWhenNegativeFrameLength()
    {
        WebSocket08FrameDecoder decoder = new WebSocket08FrameDecoder(true, true, 65535, false);
        EmbeddedChannel channel = new EmbeddedChannel(decoder);
        ByteBuf invalidFrame = Unpooled.Buffer(10).WriteByte(0x81)
                                             .WriteByte(0xFF).WriteLong(-1L);

        Exception exception = Assert.ThrowsAny<CorruptedWebSocketFrameException>(() =>
                channel.WriteInbound(invalidFrame));
        Assert.Equal("invalid data frame length (negative length)", exception.Message);

        CloseWebSocketFrame closeFrame = channel.ReadOutbound<CloseWebSocketFrame>();
        Assert.Equal("invalid data frame length (negative length)", closeFrame.ReasonText());
        Assert.True(closeFrame.Release());
        Assert.False(channel.IsActive);
    }
}
