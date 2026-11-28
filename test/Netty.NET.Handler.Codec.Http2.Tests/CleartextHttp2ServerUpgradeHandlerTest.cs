/*
 * Copyright 2017 The Netty Project
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
using System.Text;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.HttpServerUpgradeHandler;
using static Netty.NET.Handler.Codec.Http2.CleartextHttp2ServerUpgradeHandler;
using State = Netty.NET.Handler.Codec.Http2.IHttp2Stream.State;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Tests for {@link CleartextHttp2ServerUpgradeHandler}
 */
// CLR: Mockito mock/verify(...)/any/eq are a loose Moq mock, Verify(..., Times.Once()), It.IsAny and the expected
// value compared with Equals. Http2Stream.State is the sealed class IHttp2Stream.State and state() is State_()
// (CS0102 owner rule). assertInstanceOf is Assert.IsAssignableFrom, the request getters
// method()/uri()/protocolVersion() are Method()/Uri()/ ProtocolVersion(), and headers equality calls Equals
// directly, as Java's assertEquals calls equals.
public class CleartextHttp2ServerUpgradeHandlerTest : IDisposable
{
    private EmbeddedChannel channel;

    private Mock<IHttp2FrameListener> frameListener;

    private Http2ConnectionHandler http2ConnectionHandler;

    private List<object> userEvents;

    private void SetUpServerChannel()
    {
        frameListener = new Mock<IHttp2FrameListener>();

        http2ConnectionHandler = new Http2ConnectionHandlerBuilder()
                .FrameListener(frameListener.Object).Build();

        IUpgradeCodecFactory upgradeCodecFactory = new ConnectionHandlerUpgradeCodecFactory(this);

        userEvents = new List<object>();

        HttpServerCodec httpServerCodec = new HttpServerCodec();
        HttpServerUpgradeHandler upgradeHandler = new HttpServerUpgradeHandler(httpServerCodec, upgradeCodecFactory);

        CleartextHttp2ServerUpgradeHandler handler = new CleartextHttp2ServerUpgradeHandler(
                httpServerCodec, upgradeHandler, http2ConnectionHandler);
        channel = new EmbeddedChannel(handler, new UserEventCollector(userEvents));
    }

    private sealed class ConnectionHandlerUpgradeCodecFactory : IUpgradeCodecFactory
    {
        private readonly CleartextHttp2ServerUpgradeHandlerTest test;

        internal ConnectionHandlerUpgradeCodecFactory(CleartextHttp2ServerUpgradeHandlerTest test)
        {
            this.test = test;
        }

        public IUpgradeCodec NewUpgradeCodec(ICharSequence protocol)
        {
            return new Http2ServerUpgradeCodec(test.http2ConnectionHandler);
        }
    }

    private sealed class UserEventCollector : ChannelInboundHandlerAdapter
    {
        private readonly List<object> userEvents;

        internal UserEventCollector(List<object> userEvents)
        {
            this.userEvents = userEvents;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            userEvents.Add(evt);
        }
    }

    public void Dispose()
    {
        channel.FinishAndReleaseAll();
    }

    [Fact]
    public void PriorKnowledge()
    {
        SetUpServerChannel();

        channel.WriteInbound(Http2CodecUtil.ConnectionPrefaceBuf());

        ByteBuf settingsFrame = SettingsFrameBuf();

        Assert.False(channel.WriteInbound(settingsFrame));

        Assert.Single(userEvents);
        Assert.IsAssignableFrom<PriorKnowledgeUpgradeEvent>(userEvents[0]);

        Assert.Equal(100, http2ConnectionHandler.Connection().Local().MaxActiveStreams());
        Assert.Equal(65535, http2ConnectionHandler.Connection().Local().FlowController().InitialWindowSize());

        frameListener.Verify(l => l.OnSettingsRead(
                It.IsAny<IChannelHandlerContext>(), ExpectedSettings()), Times.Once());
    }

    [Fact]
    public void Upgrade()
    {
        string upgradeString = "GET / HTTP/1.1\r\n" +
                "Host: example.com\r\n" +
                "Connection: Upgrade, HTTP2-Settings\r\n" +
                "Upgrade: h2c\r\n" +
                "HTTP2-Settings: AAMAAABkAAQAAP__\r\n\r\n";
        ValidateClearTextUpgrade(upgradeString);
    }

    [Fact]
    public void UpgradeWithMultipleConnectionHeaders()
    {
        string upgradeString = "GET / HTTP/1.1\r\n" +
                "Host: example.com\r\n" +
                "Connection: keep-alive\r\n" +
                "Connection: Upgrade, HTTP2-Settings\r\n" +
                "Upgrade: h2c\r\n" +
                "HTTP2-Settings: AAMAAABkAAQAAP__\r\n\r\n";
        ValidateClearTextUpgrade(upgradeString);
    }

    [Fact]
    public void RequiredHeadersInSeparateConnectionHeaders()
    {
        string upgradeString = "GET / HTTP/1.1\r\n" +
                "Host: example.com\r\n" +
                "Connection: keep-alive\r\n" +
                "Connection: HTTP2-Settings\r\n" +
                "Connection: Upgrade\r\n" +
                "Upgrade: h2c\r\n" +
                "HTTP2-Settings: AAMAAABkAAQAAP__\r\n\r\n";
        ValidateClearTextUpgrade(upgradeString);
    }

    [Fact]
    public void PriorKnowledgeInFragments()
    {
        SetUpServerChannel();

        ByteBuf connectionPreface = Http2CodecUtil.ConnectionPrefaceBuf();
        Assert.False(channel.WriteInbound(connectionPreface.ReadBytes(5), connectionPreface));

        ByteBuf settingsFrame = SettingsFrameBuf();
        Assert.False(channel.WriteInbound(settingsFrame));

        Assert.Single(userEvents);
        Assert.IsAssignableFrom<PriorKnowledgeUpgradeEvent>(userEvents[0]);

        Assert.Equal(100, http2ConnectionHandler.Connection().Local().MaxActiveStreams());
        Assert.Equal(65535, http2ConnectionHandler.Connection().Local().FlowController().InitialWindowSize());

        frameListener.Verify(l => l.OnSettingsRead(
                It.IsAny<IChannelHandlerContext>(), ExpectedSettings()), Times.Once());
    }

    [Fact]
    public void Downgrade()
    {
        SetUpServerChannel();

        string requestString = "GET / HTTP/1.1\r\n" +
                         "Host: example.com\r\n\r\n";
        ByteBuf inbound = Unpooled.Buffer().WriteBytes(Encoding.ASCII.GetBytes(requestString));

        Assert.True(channel.WriteInbound(inbound));

        object firstInbound = channel.ReadInbound();
        Assert.IsAssignableFrom<IHttpRequest>(firstInbound);
        IHttpRequest request = (IHttpRequest) firstInbound;
        Assert.Equal(HttpMethod.GET, request.Method());
        Assert.Equal("/", request.Uri());
        Assert.Equal(HttpVersion.HTTP_1_1, request.ProtocolVersion());
        Assert.True(new DefaultHttpHeaders().Add("Host", "example.com").Equals(request.Headers()));

        channel.ReadInbound<ILastHttpContent>().Release();

        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void UsedHttp2MultiplexCodec()
    {
        // CLR: Http2MultiplexCodec/Http2MultiplexCodecBuilder are @Deprecated with the replacement named
        // (Http2FrameCodecBuilder together with Http2MultiplexHandler) and are not ported. The test also checks
        // CleartextHttp2ServerUpgradeHandler's prior-knowledge path with a frame codec that hands the frames on,
        // so it runs with the replacement: new Http2MultiplexCodecBuilder(true, childHandler).build() is new
        // Http2FrameCodecBuilder(true).Build() with new Http2MultiplexHandler(childHandler); the upgrade codec
        // takes the pair through Http2ServerUpgradeCodec(Http2FrameCodec, params IChannelHandler[]), and the
        // single http2ServerHandler is the nested Http2ServerHandlerInitializer, a ChannelInitializer that
        // installs the pair where Java adds the codec (right after the cleartext handler) and removes itself.
        Http2FrameCodec http2Codec = new Http2FrameCodecBuilder(true).Build();
        Http2MultiplexHandler http2MultiplexHandler = new Http2MultiplexHandler(new EmptyChannelInitializer());
        IUpgradeCodecFactory upgradeCodecFactory = new FrameCodecUpgradeCodecFactory(http2Codec, http2MultiplexHandler);
        http2ConnectionHandler = http2Codec;

        userEvents = new List<object>();

        HttpServerCodec httpServerCodec = new HttpServerCodec();
        HttpServerUpgradeHandler upgradeHandler = new HttpServerUpgradeHandler(httpServerCodec, upgradeCodecFactory);

        CleartextHttp2ServerUpgradeHandler handler = new CleartextHttp2ServerUpgradeHandler(
                httpServerCodec, upgradeHandler, new Http2ServerHandlerInitializer(http2Codec, http2MultiplexHandler));
        channel = new EmbeddedChannel(handler, new UserEventCollector(userEvents));

        Assert.False(channel.WriteInbound(Http2CodecUtil.ConnectionPrefaceBuf()));

        ByteBuf settingsFrame = SettingsFrameBuf();

        Assert.True(channel.WriteInbound(settingsFrame));

        Assert.Single(userEvents);
        Assert.IsAssignableFrom<PriorKnowledgeUpgradeEvent>(userEvents[0]);
    }

    private sealed class EmptyChannelInitializer : ChannelInitializer<IChannel>
    {
        protected override void InitChannel(IChannel ch)
        {
        }
    }

    private sealed class FrameCodecUpgradeCodecFactory : IUpgradeCodecFactory
    {
        private readonly Http2FrameCodec http2Codec;
        private readonly IChannelHandler http2MultiplexHandler;

        internal FrameCodecUpgradeCodecFactory(Http2FrameCodec http2Codec, IChannelHandler http2MultiplexHandler)
        {
            this.http2Codec = http2Codec;
            this.http2MultiplexHandler = http2MultiplexHandler;
        }

        public IUpgradeCodec NewUpgradeCodec(ICharSequence protocol)
        {
            return new Http2ServerUpgradeCodec(http2Codec, http2MultiplexHandler);
        }
    }

    private sealed class Http2ServerHandlerInitializer : ChannelInitializer<IChannel>
    {
        private readonly Http2FrameCodec http2Codec;
        private readonly IChannelHandler http2MultiplexHandler;

        internal Http2ServerHandlerInitializer(Http2FrameCodec http2Codec, IChannelHandler http2MultiplexHandler)
        {
            this.http2Codec = http2Codec;
            this.http2MultiplexHandler = http2MultiplexHandler;
        }

        protected override void InitChannel(IChannel ch)
        {
            // CLR: Http2MultiplexHandler (an Http2ChannelDuplexHandler) looks up the Http2FrameCodec in the pipeline
            // when it is added (requireHttp2FrameCodec,
            // src/Netty.NET.Handler.Codec.Http2/Http2ChannelDuplexHandler.cs), so the codec is added first and the
            // multiplexer right after it, as Http2ServerUpgradeCodec.upgradeTo does.
            ch.Pipeline.AddAfter(ch.Pipeline.Context(this).Name, null, http2Codec);
            ch.Pipeline.AddAfter(ch.Pipeline.Context(http2Codec).Name, null, http2MultiplexHandler);
        }
    }

    private static ByteBuf SettingsFrameBuf()
    {
        ByteBuf settingsFrame = Unpooled.Buffer();
        settingsFrame.WriteMedium(12); // Payload length
        settingsFrame.WriteByte(0x4); // Frame type
        settingsFrame.WriteByte(0x0); // Flags
        settingsFrame.WriteInt(0x0); // StreamId
        settingsFrame.WriteShort(0x3);
        settingsFrame.WriteInt(100);
        settingsFrame.WriteShort(0x4);
        settingsFrame.WriteInt(65535);

        return settingsFrame;
    }

    private static Http2Settings ExpectedSettings()
    {
        return new Http2Settings().MaxConcurrentStreams(100).InitialWindowSize(65535);
    }

    private void ValidateClearTextUpgrade(string upgradeString)
    {
        SetUpServerChannel();

        ByteBuf upgrade = Unpooled.CopiedBuffer(upgradeString, Encoding.ASCII);

        Assert.False(channel.WriteInbound(upgrade));

        Assert.Single(userEvents);

        object userEvent = userEvents[0];
        Assert.IsAssignableFrom<UpgradeEvent>(userEvent);
        // CLR: Java compares the String "h2c" with the protocol, which HttpServerUpgradeHandler.splitHeader creates as
        // a String; the port creates it as a StringCharSequence (src/Netty.NET.Handler.Codec.Http/
        // HttpServerUpgradeHandler.cs), so its text is compared.
        Assert.Equal("h2c", ((UpgradeEvent) userEvent).Protocol().ToString());
        ReferenceCountUtil.Release(userEvent);

        Assert.Equal(100, http2ConnectionHandler.Connection().Local().MaxActiveStreams());
        Assert.Equal(65535, http2ConnectionHandler.Connection().Local().FlowController().InitialWindowSize());

        Assert.Equal(1, http2ConnectionHandler.Connection().NumActiveStreams());
        Assert.NotNull(http2ConnectionHandler.Connection().Stream(1));

        IHttp2Stream stream = http2ConnectionHandler.Connection().Stream(1);
        Assert.Equal(State.HALF_CLOSED_REMOTE, stream.State_());
        Assert.False(stream.IsHeadersSent());

        string expectedHttpResponse = "HTTP/1.1 101 Switching Protocols\r\n" +
                "connection: upgrade\r\n" +
                "upgrade: h2c\r\n\r\n";
        ByteBuf responseBuffer = channel.ReadOutbound<ByteBuf>();
        Assert.Equal(expectedHttpResponse, responseBuffer.ToString(Encoding.UTF8));
        responseBuffer.Release();

        // Check that the preface was send (a.k.a the settings frame)
        ByteBuf settingsBuffer = channel.ReadOutbound<ByteBuf>();
        Assert.NotNull(settingsBuffer);
        settingsBuffer.Release();

        Assert.Null(channel.ReadOutbound());
    }
}
