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

using Moq;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: Mockito.mock(ServerChannel.class) is a loose Moq mock of IServerChannel (docs/codec-http-porting.md, Shared CLR
// adaptations); DefaultChannelId.newInstance() is NewInstance(), channel.pipeline().firstContext() is the
// Pipeline.FirstContext property and channel.flush() is Flush() (docs/transport-porting.md, Decisions).
// pipeline().remove(handler.getClass())/get(handler.getClass()) are Pipeline.Remove<T>()/Get<T>() with T inferred from
// the handler argument, which is the handler's runtime class at every call (Http2ConnectionHandlerBuilder builds an
// Http2ConnectionHandler, Http2FrameCodecBuilder an Http2FrameCodec): a Class<T> argument becomes a generic type
// argument (src/Netty.NET.Transport/Channels/IChannelPipeline.cs). readOutbound() is ReadOutbound<ByteBuf>(); the
// Strings that headers().set(...) takes are the String overloads (docs/codec-http-porting.md, Shared CLR adaptations).
// @Sharable is [Sharable]. testUpgradeToHttp2MultiplexCodec only exercises the deprecated, not ported
// Http2MultiplexCodecBuilder; its replacement (Http2FrameCodecBuilder with Http2MultiplexHandler) is
// TestUpgradeToHttp2FrameCodecWithMultiplexer, so it has no C# method (recorded as CLR-inapplicable in the manifest).
public class Http2ServerUpgradeCodecTest
{

    [Fact]
    public void TestUpgradeToHttp2ConnectionHandler()
    {
        TestUpgrade(new Http2ConnectionHandlerBuilder().FrameListener(new Http2FrameAdapter()).Build(), null);
    }

    [Fact]
    public void TestUpgradeToHttp2FrameCodec()
    {
        TestUpgrade(new Http2FrameCodecBuilder(true).Build(), null);
    }

    [Fact]
    public void TestUpgradeToHttp2FrameCodecWithMultiplexer()
    {
        TestUpgrade(new Http2FrameCodecBuilder(true).Build(),
                new Http2MultiplexHandler(new HttpInboundHandler()));
    }

    private static void TestUpgrade<T>(T handler, IChannelHandler multiplexer) where T : Http2ConnectionHandler
    {
        IFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.OPTIONS, "*");
        request.Headers().Set(HttpHeaderNames.HOST, "netty.io");
        request.Headers().Set(HttpHeaderNames.CONNECTION, "Upgrade, HTTP2-Settings");
        request.Headers().Set(HttpHeaderNames.UPGRADE, "h2c");
        request.Headers().Set("HTTP2-Settings", "AAMAAABkAAQAAP__");

        IServerChannel parent = new Mock<IServerChannel>().Object;
        EmbeddedChannel channel = new EmbeddedChannel(parent, DefaultChannelId.NewInstance(), true, false,
                new ChannelInboundHandlerAdapter());
        IChannelHandlerContext ctx = channel.Pipeline.FirstContext;
        Http2ServerUpgradeCodec codec;

        if (multiplexer == null)
        {
            codec = new Http2ServerUpgradeCodec(handler);
        }
        else
        {
            codec = new Http2ServerUpgradeCodec((Http2FrameCodec) (Http2ConnectionHandler) handler, multiplexer);
        }

        Assert.True(codec.PrepareUpgradeResponse(ctx, request, new DefaultHttpHeaders()));
        codec.UpgradeTo(ctx, request);
        // Flush the channel to ensure we write out all buffered data
        channel.Flush();

        channel.WriteInbound(Http2CodecUtil.ConnectionPrefaceBuf());
        Http2FrameInboundWriter writer = new Http2FrameInboundWriter(channel);
        writer.WriteInboundSettings(new Http2Settings());
        writer.WriteInboundRstStream(Http2CodecUtil.HTTP_UPGRADE_STREAM_ID, Http2Error.CANCEL.Code());

        Assert.Same(handler, channel.Pipeline.Remove<T>());
        Assert.Null(channel.Pipeline.Get<T>());
        Assert.True(channel.Finish());

        // Check that the preface was send (a.k.a the settings frame)
        ByteBuf settingsBuffer = channel.ReadOutbound<ByteBuf>();
        Assert.NotNull(settingsBuffer);
        settingsBuffer.Release();

        ByteBuf buf = channel.ReadOutbound<ByteBuf>();
        Assert.NotNull(buf);
        buf.Release();

        Assert.Null(channel.ReadOutbound());
    }

    [Sharable]
    private sealed class HttpInboundHandler : ChannelInboundHandlerAdapter { }
}
