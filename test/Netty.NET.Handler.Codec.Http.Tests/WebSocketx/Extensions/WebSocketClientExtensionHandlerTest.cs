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

using System.Collections.Generic;
using System.Collections.Immutable;
using Moq;
using Netty.NET.Handler.Codec.Http.WebSocketx.Extensions;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.Tests.WebSocketx.Extensions.WebSocketExtensionTestUtil;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx.Extensions;

// CLR: Mockito mocks are Moq mocks of the I-prefixed interfaces (the mock names have no counterpart);
// when(...).thenReturn(...) is Setup(...).Returns(...), and an unconfigured member of a loose mock returns the default
// value as in Mockito (https://github.com/devlooped/moq/wiki/Quickstart). verify(mock) defaults to times(1), so it is
// Verify(..., Times.Once()); atLeastOnce() is Times.AtLeastOnce(). any(WebSocketExtensionData.class) is
// It.IsAny<WebSocketExtensionData>() and webSocketExtensionDataMatcher(name) is WebSocketExtensionDataMatcher_(name)
// (see WebSocketExtensionTestUtil). Collections.emptyMap() is ImmutableDictionary<string, string>.Empty, List is IList,
// pipeline().get(Class)/context(handler) are Pipeline.Get<T>()/Context(handler), and fail(...) is Assert.Fail. The test
// class fields are created per test, as xUnit creates a new instance of the test class for every test.
public class WebSocketClientExtensionHandlerTest
{

    Mock<IWebSocketClientExtensionHandshaker> mainHandshakerMock =
            new Mock<IWebSocketClientExtensionHandshaker>();
    Mock<IWebSocketClientExtensionHandshaker> fallbackHandshakerMock =
            new Mock<IWebSocketClientExtensionHandshaker>();
    Mock<IWebSocketClientExtension> mainExtensionMock =
            new Mock<IWebSocketClientExtension>();
    Mock<IWebSocketClientExtension> fallbackExtensionMock =
            new Mock<IWebSocketClientExtension>();

    [Fact]
    public void TestMainSuccess()
    {
        // initialize
        mainHandshakerMock.Setup(m => m.NewRequestData()).
                Returns(new WebSocketExtensionData("main", ImmutableDictionary<string, string>.Empty));
        mainHandshakerMock.Setup(m => m.HandshakeExtension(It.IsAny<WebSocketExtensionData>()))
                .Returns(mainExtensionMock.Object);
        fallbackHandshakerMock.Setup(m => m.NewRequestData()).
                Returns(new WebSocketExtensionData("fallback", ImmutableDictionary<string, string>.Empty));
        mainExtensionMock.Setup(m => m.Rsv()).Returns(IWebSocketExtension.RSV1);
        mainExtensionMock.Setup(m => m.NewExtensionEncoder()).Returns(new DummyEncoder());
        mainExtensionMock.Setup(m => m.NewExtensionDecoder()).Returns(new DummyDecoder());

        // execute
        EmbeddedChannel ch = new EmbeddedChannel(new WebSocketClientExtensionHandler(
                mainHandshakerMock.Object, fallbackHandshakerMock.Object));

        IHttpRequest req = NewUpgradeRequest(null);
        ch.WriteOutbound(req);

        IHttpRequest req2 = ch.ReadOutbound<IHttpRequest>();
        IList<WebSocketExtensionData> reqExts = WebSocketExtensionUtil.ExtractExtensions(
                req2.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS));

        IHttpResponse res = NewUpgradeResponse("main");
        ch.WriteInbound(res);

        IHttpResponse res2 = ch.ReadInbound<IHttpResponse>();
        IList<WebSocketExtensionData> resExts = WebSocketExtensionUtil.ExtractExtensions(
                res2.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS));

        // test
        Assert.Equal(2, reqExts.Count);
        Assert.Equal("main", reqExts[0].Name());
        Assert.Equal("fallback", reqExts[1].Name());

        Assert.Single(resExts);
        Assert.Equal("main", resExts[0].Name());
        Assert.True(resExts[0].Parameters().Count == 0);
        Assert.NotNull(ch.Pipeline.Get<DummyDecoder>());
        Assert.NotNull(ch.Pipeline.Get<DummyEncoder>());

        mainHandshakerMock.Verify(m => m.NewRequestData(), Times.Once());
        mainHandshakerMock.Verify(m => m.HandshakeExtension(It.IsAny<WebSocketExtensionData>()), Times.Once());
        fallbackHandshakerMock.Verify(m => m.NewRequestData(), Times.Once());
        mainExtensionMock.Verify(m => m.Rsv(), Times.AtLeastOnce());
        mainExtensionMock.Verify(m => m.NewExtensionEncoder(), Times.Once());
        mainExtensionMock.Verify(m => m.NewExtensionDecoder(), Times.Once());
    }

    [Fact]
    public void TestFallbackSuccess()
    {
        // initialize
        mainHandshakerMock.Setup(m => m.NewRequestData()).
                Returns(new WebSocketExtensionData("main", ImmutableDictionary<string, string>.Empty));
        mainHandshakerMock.Setup(m => m.HandshakeExtension(It.IsAny<WebSocketExtensionData>()))
                .Returns((IWebSocketClientExtension) null);
        fallbackHandshakerMock.Setup(m => m.NewRequestData()).
                Returns(new WebSocketExtensionData("fallback", ImmutableDictionary<string, string>.Empty));
        fallbackHandshakerMock.Setup(m => m.HandshakeExtension(
                It.IsAny<WebSocketExtensionData>())).Returns(fallbackExtensionMock.Object);
        fallbackExtensionMock.Setup(m => m.Rsv()).Returns(IWebSocketExtension.RSV1);
        fallbackExtensionMock.Setup(m => m.NewExtensionEncoder()).Returns(new DummyEncoder());
        fallbackExtensionMock.Setup(m => m.NewExtensionDecoder()).Returns(new DummyDecoder());

        // execute
        EmbeddedChannel ch = new EmbeddedChannel(new WebSocketClientExtensionHandler(
                mainHandshakerMock.Object, fallbackHandshakerMock.Object));

        IHttpRequest req = NewUpgradeRequest(null);
        ch.WriteOutbound(req);

        IHttpRequest req2 = ch.ReadOutbound<IHttpRequest>();
        IList<WebSocketExtensionData> reqExts = WebSocketExtensionUtil.ExtractExtensions(
                req2.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS));

        IHttpResponse res = NewUpgradeResponse("fallback");
        ch.WriteInbound(res);

        IHttpResponse res2 = ch.ReadInbound<IHttpResponse>();
        IList<WebSocketExtensionData> resExts = WebSocketExtensionUtil.ExtractExtensions(
                res2.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS));

        // test
        Assert.Equal(2, reqExts.Count);
        Assert.Equal("main", reqExts[0].Name());
        Assert.Equal("fallback", reqExts[1].Name());

        Assert.Single(resExts);
        Assert.Equal("fallback", resExts[0].Name());
        Assert.True(resExts[0].Parameters().Count == 0);
        Assert.NotNull(ch.Pipeline.Get<DummyDecoder>());
        Assert.NotNull(ch.Pipeline.Get<DummyEncoder>());

        mainHandshakerMock.Verify(m => m.NewRequestData(), Times.Once());
        mainHandshakerMock.Verify(m => m.HandshakeExtension(It.IsAny<WebSocketExtensionData>()), Times.Once());
        fallbackHandshakerMock.Verify(m => m.NewRequestData(), Times.Once());
        fallbackHandshakerMock.Verify(m => m.HandshakeExtension(It.IsAny<WebSocketExtensionData>()), Times.Once());
        fallbackExtensionMock.Verify(m => m.Rsv(), Times.AtLeastOnce());
        fallbackExtensionMock.Verify(m => m.NewExtensionEncoder(), Times.Once());
        fallbackExtensionMock.Verify(m => m.NewExtensionDecoder(), Times.Once());
    }

    [Fact]
    public void TestAllSuccess()
    {
        // initialize
        mainHandshakerMock.Setup(m => m.NewRequestData()).
                Returns(new WebSocketExtensionData("main", ImmutableDictionary<string, string>.Empty));
        mainHandshakerMock.Setup(m => m.HandshakeExtension(
                WebSocketExtensionDataMatcher_("main"))).Returns(mainExtensionMock.Object);
        mainHandshakerMock.Setup(m => m.HandshakeExtension(
                WebSocketExtensionDataMatcher_("fallback"))).Returns((IWebSocketClientExtension) null);
        fallbackHandshakerMock.Setup(m => m.NewRequestData()).
                Returns(new WebSocketExtensionData("fallback", ImmutableDictionary<string, string>.Empty));
        fallbackHandshakerMock.Setup(m => m.HandshakeExtension(
                WebSocketExtensionDataMatcher_("main"))).Returns((IWebSocketClientExtension) null);
        fallbackHandshakerMock.Setup(m => m.HandshakeExtension(
                WebSocketExtensionDataMatcher_("fallback"))).Returns(fallbackExtensionMock.Object);

        DummyEncoder mainEncoder = new DummyEncoder();
        DummyDecoder mainDecoder = new DummyDecoder();
        mainExtensionMock.Setup(m => m.Rsv()).Returns(IWebSocketExtension.RSV1);
        mainExtensionMock.Setup(m => m.NewExtensionEncoder()).Returns(mainEncoder);
        mainExtensionMock.Setup(m => m.NewExtensionDecoder()).Returns(mainDecoder);

        Dummy2Encoder fallbackEncoder = new Dummy2Encoder();
        Dummy2Decoder fallbackDecoder = new Dummy2Decoder();
        fallbackExtensionMock.Setup(m => m.Rsv()).Returns(IWebSocketExtension.RSV2);
        fallbackExtensionMock.Setup(m => m.NewExtensionEncoder()).Returns(fallbackEncoder);
        fallbackExtensionMock.Setup(m => m.NewExtensionDecoder()).Returns(fallbackDecoder);

        // execute
        EmbeddedChannel ch = new EmbeddedChannel(new WebSocketClientExtensionHandler(
                mainHandshakerMock.Object, fallbackHandshakerMock.Object));

        IHttpRequest req = NewUpgradeRequest(null);
        ch.WriteOutbound(req);

        IHttpRequest req2 = ch.ReadOutbound<IHttpRequest>();
        IList<WebSocketExtensionData> reqExts = WebSocketExtensionUtil.ExtractExtensions(
                req2.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS));

        IHttpResponse res = NewUpgradeResponse("main, fallback");
        ch.WriteInbound(res);

        IHttpResponse res2 = ch.ReadInbound<IHttpResponse>();
        IList<WebSocketExtensionData> resExts = WebSocketExtensionUtil.ExtractExtensions(
                res2.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS));

        // test
        Assert.Equal(2, reqExts.Count);
        Assert.Equal("main", reqExts[0].Name());
        Assert.Equal("fallback", reqExts[1].Name());

        Assert.Equal(2, resExts.Count);
        Assert.Equal("main", resExts[0].Name());
        Assert.Equal("fallback", resExts[1].Name());
        Assert.NotNull(ch.Pipeline.Context(mainEncoder));
        Assert.NotNull(ch.Pipeline.Context(mainDecoder));
        Assert.NotNull(ch.Pipeline.Context(fallbackEncoder));
        Assert.NotNull(ch.Pipeline.Context(fallbackDecoder));

        mainHandshakerMock.Verify(m => m.NewRequestData(), Times.Once());
        mainHandshakerMock.Verify(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("main")), Times.Once());
        mainHandshakerMock.Verify(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("fallback")), Times.Once());
        fallbackHandshakerMock.Verify(m => m.NewRequestData(), Times.Once());
        fallbackHandshakerMock.Verify(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("fallback")),
                Times.Once());
        mainExtensionMock.Verify(m => m.Rsv(), Times.AtLeastOnce());
        mainExtensionMock.Verify(m => m.NewExtensionEncoder(), Times.Once());
        mainExtensionMock.Verify(m => m.NewExtensionDecoder(), Times.Once());
        fallbackExtensionMock.Verify(m => m.Rsv(), Times.AtLeastOnce());
        fallbackExtensionMock.Verify(m => m.NewExtensionEncoder(), Times.Once());
        fallbackExtensionMock.Verify(m => m.NewExtensionDecoder(), Times.Once());
    }

    [Fact]
    public void TestIfMainAndFallbackUseRSV1WillFail()
    {
        // initialize
        mainHandshakerMock.Setup(m => m.NewRequestData()).
                Returns(new WebSocketExtensionData("main", ImmutableDictionary<string, string>.Empty));
        mainHandshakerMock.Setup(m => m.HandshakeExtension(
                WebSocketExtensionDataMatcher_("main"))).Returns(mainExtensionMock.Object);
        mainHandshakerMock.Setup(m => m.HandshakeExtension(
                WebSocketExtensionDataMatcher_("fallback"))).Returns((IWebSocketClientExtension) null);
        fallbackHandshakerMock.Setup(m => m.NewRequestData()).
                Returns(new WebSocketExtensionData("fallback", ImmutableDictionary<string, string>.Empty));
        fallbackHandshakerMock.Setup(m => m.HandshakeExtension(
                WebSocketExtensionDataMatcher_("main"))).Returns((IWebSocketClientExtension) null);
        fallbackHandshakerMock.Setup(m => m.HandshakeExtension(
                WebSocketExtensionDataMatcher_("fallback"))).Returns(fallbackExtensionMock.Object);
        mainExtensionMock.Setup(m => m.Rsv()).Returns(IWebSocketExtension.RSV1);
        fallbackExtensionMock.Setup(m => m.Rsv()).Returns(IWebSocketExtension.RSV1);

        // execute
        EmbeddedChannel ch = new EmbeddedChannel(new WebSocketClientExtensionHandler(
                mainHandshakerMock.Object, fallbackHandshakerMock.Object));

        IHttpRequest req = NewUpgradeRequest(null);
        ch.WriteOutbound(req);

        IHttpRequest req2 = ch.ReadOutbound<IHttpRequest>();
        IList<WebSocketExtensionData> reqExts = WebSocketExtensionUtil.ExtractExtensions(
                req2.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS));

        IHttpResponse res = NewUpgradeResponse("main, fallback");

        try
        {
            ch.WriteInbound(res);
        }
        catch (CodecException)
        {
            return;
        }

        Assert.Fail("Expected to encounter a CodecException");

        // test
        Assert.Equal(2, reqExts.Count);
        Assert.Equal("main", reqExts[0].Name());
        Assert.Equal("fallback", reqExts[1].Name());

        mainHandshakerMock.Verify(m => m.NewRequestData(), Times.Once());
        mainHandshakerMock.Verify(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("main")),
                Times.AtLeastOnce());
        mainHandshakerMock.Verify(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("fallback")),
                Times.AtLeastOnce());

        fallbackHandshakerMock.Verify(m => m.NewRequestData(), Times.Once());
        fallbackHandshakerMock.Verify(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("main")),
                Times.AtLeastOnce());
        fallbackHandshakerMock.Verify(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("fallback")),
                Times.AtLeastOnce());

        mainExtensionMock.Verify(m => m.Rsv(), Times.AtLeastOnce());
        fallbackExtensionMock.Verify(m => m.Rsv(), Times.AtLeastOnce());
    }
}
