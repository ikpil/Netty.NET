/*
 * Copyright 2017 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: @Sharable is [Sharable]. testUpgradeToHttp2MultiplexCodec only exercises the deprecated, not ported
// Http2MultiplexCodecBuilder; its replacement (Http2FrameCodecBuilder with Http2MultiplexHandler) is
// TestUpgradeToHttp2FrameCodecWithMultiplexer, so it has no C# method (recorded as CLR-inapplicable in the
// manifest).
public class Http2ClientUpgradeCodecTest
{

    [Fact]
    public void TestUpgradeToHttp2ConnectionHandler()
    {
        TestUpgrade(new Http2ConnectionHandlerBuilder().Server(false).FrameListener(
            new Http2FrameAdapter()).Build(), null);
    }

    [Fact]
    public void TestUpgradeToHttp2FrameCodec()
    {
        TestUpgrade(Http2FrameCodecBuilder.ForClient().Build(), null);
    }

    [Fact]
    public void TestUpgradeToHttp2FrameCodecWithMultiplexer()
    {
        TestUpgrade(Http2FrameCodecBuilder.ForClient().Build(),
            new Http2MultiplexHandler(new HttpInboundHandler(), new HttpInboundHandler()));
    }

    private static void TestUpgrade(Http2ConnectionHandler handler, Http2MultiplexHandler multiplexer)
    {
        IFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.OPTIONS, "*");

        EmbeddedChannel channel = new EmbeddedChannel(new ChannelInboundHandlerAdapter());
        IChannelHandlerContext ctx = channel.Pipeline.FirstContext;

        Http2ClientUpgradeCodec codec;

        if (multiplexer == null)
        {
            codec = new Http2ClientUpgradeCodec("connectionHandler", handler);
        }
        else
        {
            codec = new Http2ClientUpgradeCodec("connectionHandler", handler, multiplexer);
        }

        codec.SetUpgradeHeaders(ctx, request);
        // Flush the channel to ensure we write out all buffered data
        channel.Flush();

        codec.UpgradeTo(ctx, null);
        Assert.NotNull(channel.Pipeline.Get("connectionHandler"));

        if (multiplexer != null)
        {
            Assert.NotNull(channel.Pipeline.Get<Http2MultiplexHandler>());
        }

        Assert.True(channel.FinishAndReleaseAll());
    }

    [Sharable]
    private sealed class HttpInboundHandler : ChannelInboundHandlerAdapter { }
}
