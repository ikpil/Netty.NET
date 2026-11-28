/*
 * Copyright 2020 The Netty Project
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
using Netty.NET.Common;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Sockets;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: the typed channelRead overloads of Http3RequestStreamInboundHandler are protected abstract members in the
// port, so the public Java overrides are protected overrides; channelInputClosed stays protected. readInbound() is
// ReadInbound<T>() (ReadInbound() for an Object), and assertEquals(true, readInbound()) compares the boxed bool
// with Equals, as Java compares Boolean.TRUE with equals.
public class Http3RequestStreamInboundHandlerTest
{

    [Fact]
    public void TestDetectLastViaUserEvent()
    {
        EmbeddedQuicStreamChannel channel = new EmbeddedQuicStreamChannel(new TestHttp3RequestStreamInboundHandler());
        Assert.True(channel.WriteInbound(new DefaultHttp3HeadersFrame()));
        Assert.True(channel.WriteInbound(new DefaultHttp3DataFrame(Unpooled.Buffer())));
        Assert.True(channel.WriteInbound(new DefaultHttp3DataFrame(Unpooled.Buffer())));
        channel.Pipeline.FireUserEventTriggered(ChannelInputShutdownEvent.INSTANCE);
        AssertFrame(channel);
        AssertFrame(channel);
        AssertFrame(channel);
        Assert.Equal<object>(true, channel.ReadInbound());
        Assert.False(channel.Finish());
    }

    private void AssertFrame(EmbeddedChannel channel)
    {
        IHttp3Frame frame = channel.ReadInbound<IHttp3Frame>();
        Assert.NotNull(frame);
        ReferenceCountUtil.Release(frame);
    }

    private sealed class TestHttp3RequestStreamInboundHandler : Http3RequestStreamInboundHandler
    {

        protected override void ChannelRead(IChannelHandlerContext ctx, IHttp3HeadersFrame frame)
        {
            ctx.FireChannelRead(frame);
        }

        protected override void ChannelRead(IChannelHandlerContext ctx, IHttp3DataFrame frame)
        {
            ctx.FireChannelRead(frame);
        }

        protected override void ChannelInputClosed(IChannelHandlerContext ctx)
        {
            ctx.FireChannelRead(true);
        }
    }
}
