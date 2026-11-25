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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Handler.Codec.Http.WebSocketx.Extensions;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.Tests.WebSocketx.Extensions.WebSocketExtensionTestUtil;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx.Extensions;

// CLR: Mockito mocks are Moq mocks of the I-prefixed interfaces (see WebSocketClientExtensionHandlerTest: verify(mock)
// is Verify(..., Times.Once()), times(n) is Times.Exactly(n) and never() is Times.Never()). The anonymous handler of
// testExtensionNegotiationDisabledForResponse is the named NegotiationDisabledHandler. assertThrows(T.class) is
// Assert.ThrowsAny<T> and IllegalStateException is InvalidOperationException. LastHttpContent.EMPTY_LAST_CONTENT is
// ILastHttpContent.EMPTY_LAST_CONTENT. testExtensionHandlerNotRemovedByFailureWritePromise: the Transport has no
// ChannelPromise to pass to writeOneOutbound(msg, promise) and fail later (docs/transport-porting.md, ChannelFuture and
// ChannelPromise are Task), so FailingWritePromiseHandler, added before the extension handler on the outbound path,
// passes the response on to the outbound buffer and returns the Task of a TaskCompletionSource that stands for that
// promise; the test then fails it with the same IOException (failurePromise.setFailure is TrySetException). As in Java,
// the response stays unflushed, the handler is not removed and finish() finds the message.
#pragma warning disable xUnit1051 // The source's writes observe no test cancellation.
public class WebSocketServerExtensionHandlerTest
{

    Mock<IWebSocketServerExtensionHandshaker> mainHandshakerMock =
            new Mock<IWebSocketServerExtensionHandshaker>();
    Mock<IWebSocketServerExtensionHandshaker> fallbackHandshakerMock =
            new Mock<IWebSocketServerExtensionHandshaker>();

    Mock<IWebSocketServerExtensionHandshaker> main2HandshakerMock =
            new Mock<IWebSocketServerExtensionHandshaker>();
    Mock<IWebSocketServerExtension> mainExtensionMock =
            new Mock<IWebSocketServerExtension>();

    Mock<IWebSocketServerExtension> fallbackExtensionMock =
            new Mock<IWebSocketServerExtension>();

    Mock<IWebSocketServerExtension> main2ExtensionMock =
            new Mock<IWebSocketServerExtension>();

    [Fact]
    public void TestMainSuccess()
    {
        // initialize
        mainHandshakerMock.Setup(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("main"))).
                Returns(mainExtensionMock.Object);
        mainHandshakerMock.Setup(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("fallback"))).
                Returns((IWebSocketServerExtension) null);

        fallbackHandshakerMock.Setup(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("fallback"))).
                Returns(fallbackExtensionMock.Object);
        fallbackHandshakerMock.Setup(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("main"))).
                Returns((IWebSocketServerExtension) null);

        mainExtensionMock.Setup(m => m.Rsv()).Returns(IWebSocketExtension.RSV1);
        mainExtensionMock.Setup(m => m.NewReponseData()).Returns(
                new WebSocketExtensionData("main", ImmutableDictionary<string, string>.Empty));
        mainExtensionMock.Setup(m => m.NewExtensionEncoder()).Returns(new DummyEncoder());
        mainExtensionMock.Setup(m => m.NewExtensionDecoder()).Returns(new DummyDecoder());

        fallbackExtensionMock.Setup(m => m.Rsv()).Returns(IWebSocketExtension.RSV1);

        // execute
        WebSocketServerExtensionHandler extensionHandler =
                new WebSocketServerExtensionHandler(mainHandshakerMock.Object, fallbackHandshakerMock.Object);
        EmbeddedChannel ch = new EmbeddedChannel(extensionHandler);

        IHttpRequest req = NewUpgradeRequest("main, fallback");
        ch.WriteInbound(req);

        IHttpResponse res = NewUpgradeResponse(null);
        ch.WriteOutbound(res);

        IHttpResponse res2 = ch.ReadOutbound<IHttpResponse>();
        IList<WebSocketExtensionData> resExts = WebSocketExtensionUtil.ExtractExtensions(
                res2.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS));

        // test
        Assert.Null(ch.Pipeline.Context(extensionHandler));
        Assert.Single(resExts);
        Assert.Equal("main", resExts[0].Name());
        Assert.True(resExts[0].Parameters().Count == 0);
        Assert.NotNull(ch.Pipeline.Get<DummyDecoder>());
        Assert.NotNull(ch.Pipeline.Get<DummyEncoder>());

        mainHandshakerMock.Verify(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("main")),
                Times.AtLeastOnce());
        mainHandshakerMock.Verify(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("fallback")),
                Times.AtLeastOnce());
        fallbackHandshakerMock.Verify(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("fallback")),
                Times.AtLeastOnce());

        mainExtensionMock.Verify(m => m.Rsv(), Times.AtLeastOnce());
        mainExtensionMock.Verify(m => m.NewReponseData(), Times.Once());
        mainExtensionMock.Verify(m => m.NewExtensionEncoder(), Times.Once());
        mainExtensionMock.Verify(m => m.NewExtensionDecoder(), Times.Once());
        fallbackExtensionMock.Verify(m => m.Rsv(), Times.AtLeastOnce());
    }

    [Fact]
    public void TestCompatibleExtensionTogetherSuccess()
    {
        // initialize
        mainHandshakerMock.Setup(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("main"))).
                Returns(mainExtensionMock.Object);
        mainHandshakerMock.Setup(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("fallback"))).
                Returns((IWebSocketServerExtension) null);

        fallbackHandshakerMock.Setup(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("fallback"))).
                Returns(fallbackExtensionMock.Object);
        fallbackHandshakerMock.Setup(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("main"))).
                Returns((IWebSocketServerExtension) null);

        mainExtensionMock.Setup(m => m.Rsv()).Returns(IWebSocketExtension.RSV1);
        mainExtensionMock.Setup(m => m.NewReponseData()).Returns(
                new WebSocketExtensionData("main", ImmutableDictionary<string, string>.Empty));
        mainExtensionMock.Setup(m => m.NewExtensionEncoder()).Returns(new DummyEncoder());
        mainExtensionMock.Setup(m => m.NewExtensionDecoder()).Returns(new DummyDecoder());

        fallbackExtensionMock.Setup(m => m.Rsv()).Returns(IWebSocketExtension.RSV2);
        fallbackExtensionMock.Setup(m => m.NewReponseData()).Returns(
                new WebSocketExtensionData("fallback", ImmutableDictionary<string, string>.Empty));
        fallbackExtensionMock.Setup(m => m.NewExtensionEncoder()).Returns(new Dummy2Encoder());
        fallbackExtensionMock.Setup(m => m.NewExtensionDecoder()).Returns(new Dummy2Decoder());

        // execute
        WebSocketServerExtensionHandler extensionHandler =
                new WebSocketServerExtensionHandler(mainHandshakerMock.Object, fallbackHandshakerMock.Object);
        EmbeddedChannel ch = new EmbeddedChannel(extensionHandler);

        IHttpRequest req = NewUpgradeRequest("main, fallback");
        ch.WriteInbound(req);

        IHttpResponse res = NewUpgradeResponse(null);
        ch.WriteOutbound(res);

        IHttpResponse res2 = ch.ReadOutbound<IHttpResponse>();
        IList<WebSocketExtensionData> resExts = WebSocketExtensionUtil.ExtractExtensions(
                res2.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS));

        // test
        Assert.Null(ch.Pipeline.Context(extensionHandler));
        Assert.Equal(2, resExts.Count);
        Assert.Equal("main", resExts[0].Name());
        Assert.Equal("fallback", resExts[1].Name());
        Assert.NotNull(ch.Pipeline.Get<DummyDecoder>());
        Assert.NotNull(ch.Pipeline.Get<DummyEncoder>());
        Assert.NotNull(ch.Pipeline.Get<Dummy2Decoder>());
        Assert.NotNull(ch.Pipeline.Get<Dummy2Encoder>());

        mainHandshakerMock.Verify(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("main")), Times.Once());
        mainHandshakerMock.Verify(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("fallback")), Times.Once());
        fallbackHandshakerMock.Verify(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("fallback")),
                Times.Once());
        mainExtensionMock.Verify(m => m.Rsv(), Times.Exactly(2));
        mainExtensionMock.Verify(m => m.NewReponseData(), Times.Once());
        mainExtensionMock.Verify(m => m.NewExtensionEncoder(), Times.Once());
        mainExtensionMock.Verify(m => m.NewExtensionDecoder(), Times.Once());

        fallbackExtensionMock.Verify(m => m.Rsv(), Times.Exactly(2));

        fallbackExtensionMock.Verify(m => m.NewReponseData(), Times.Once());
        fallbackExtensionMock.Verify(m => m.NewExtensionEncoder(), Times.Once());
        fallbackExtensionMock.Verify(m => m.NewExtensionDecoder(), Times.Once());
    }

    [Fact]
    public void TestNoneExtensionMatchingSuccess()
    {
        // initialize
        mainHandshakerMock.Setup(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("unknown"))).
                Returns((IWebSocketServerExtension) null);
        mainHandshakerMock.Setup(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("unknown2"))).
                Returns((IWebSocketServerExtension) null);

        fallbackHandshakerMock.Setup(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("unknown"))).
                Returns((IWebSocketServerExtension) null);
        fallbackHandshakerMock.Setup(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("unknown2"))).
                Returns((IWebSocketServerExtension) null);

        // execute
        WebSocketServerExtensionHandler extensionHandler =
                new WebSocketServerExtensionHandler(mainHandshakerMock.Object, fallbackHandshakerMock.Object);
        EmbeddedChannel ch = new EmbeddedChannel(extensionHandler);

        IHttpRequest req = NewUpgradeRequest("unknown, unknown2");
        ch.WriteInbound(req);

        IHttpResponse res = NewUpgradeResponse(null);
        ch.WriteOutbound(res);

        IHttpResponse res2 = ch.ReadOutbound<IHttpResponse>();

        // test
        Assert.Null(ch.Pipeline.Context(extensionHandler));
        Assert.False(res2.Headers().Contains(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS));

        mainHandshakerMock.Verify(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("unknown")), Times.Once());
        mainHandshakerMock.Verify(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("unknown2")), Times.Once());

        fallbackHandshakerMock.Verify(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("unknown")),
                Times.Once());
        fallbackHandshakerMock.Verify(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("unknown2")),
                Times.Once());
    }

    private sealed class NegotiationDisabledHandler : WebSocketServerExtensionHandler
    {
        internal NegotiationDisabledHandler(params IWebSocketServerExtensionHandshaker[] extensionHandshakers)
            : base(extensionHandshakers)
        {
        }

        protected override bool IsExtensionNegotiationEnabled(IChannelHandlerContext ctx, IHttpResponse response)
        {
            return false;
        }
    }

    [Fact]
    public void TestExtensionNegotiationDisabledForResponse()
    {
        // initialize
        mainHandshakerMock.Setup(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("main"))).
                Returns(mainExtensionMock.Object);

        mainExtensionMock.Setup(m => m.Rsv()).Returns(IWebSocketExtension.RSV1);

        // execute
        WebSocketServerExtensionHandler extensionHandler =
                new NegotiationDisabledHandler(mainHandshakerMock.Object);
        EmbeddedChannel ch = new EmbeddedChannel(extensionHandler);

        IHttpRequest req = NewUpgradeRequest("main");
        ch.WriteInbound(req);

        IHttpResponse res = NewUpgradeResponse(null);
        ch.WriteOutbound(res);

        IHttpResponse res2 = ch.ReadOutbound<IHttpResponse>();

        // test
        Assert.Null(ch.Pipeline.Context(extensionHandler));
        Assert.False(res2.Headers().Contains(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS));
        Assert.Null(ch.Pipeline.Get<DummyDecoder>());
        Assert.Null(ch.Pipeline.Get<DummyEncoder>());

        mainHandshakerMock.Verify(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("main")), Times.Once());
        mainExtensionMock.Verify(m => m.Rsv(), Times.Exactly(2));
        mainExtensionMock.Verify(m => m.NewReponseData(), Times.Never());
        mainExtensionMock.Verify(m => m.NewExtensionEncoder(), Times.Never());
        mainExtensionMock.Verify(m => m.NewExtensionDecoder(), Times.Never());
    }

    private sealed class FailingWritePromiseHandler : ChannelOutboundHandlerAdapter
    {
        internal readonly TaskCompletionSource failurePromise = new TaskCompletionSource();

        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            _ = ctx.WriteAsync(msg, cancellationToken);
            return failurePromise.Task;
        }
    }

    [Fact]
    public void TestExtensionHandlerNotRemovedByFailureWritePromise()
    {
        // initialize
        mainHandshakerMock.Setup(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("main")))
                .Returns(mainExtensionMock.Object);
        mainExtensionMock.Setup(m => m.NewReponseData()).Returns(
                new WebSocketExtensionData("main", ImmutableDictionary<string, string>.Empty));

        // execute
        WebSocketServerExtensionHandler extensionHandler =
                new WebSocketServerExtensionHandler(mainHandshakerMock.Object);
        EmbeddedChannel ch = new EmbeddedChannel(extensionHandler);
        FailingWritePromiseHandler failurePromiseHandler = new FailingWritePromiseHandler();
        ch.Pipeline.AddFirst(failurePromiseHandler);

        IHttpRequest req = NewUpgradeRequest("main");
        ch.WriteInbound(req);

        IHttpResponse res = NewUpgradeResponse(null);
        TaskCompletionSource failurePromise = failurePromiseHandler.failurePromise;
        _ = ch.WriteOneOutboundAsync(res);
        failurePromise.TrySetException(new IOException("Cannot write response"));

        // test
        Assert.Null(ch.ReadOutbound());
        Assert.NotNull(ch.Pipeline.Context(extensionHandler));
        Assert.True(ch.Finish());
    }

    [Fact]
    public void TestExtensionMultipleRequests()
    {
        // initialize
        mainHandshakerMock.Setup(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("main")))
                .Returns(mainExtensionMock.Object);

        mainExtensionMock.Setup(m => m.Rsv()).Returns(IWebSocketExtension.RSV1);
        mainExtensionMock.Setup(m => m.NewReponseData()).Returns(
                new WebSocketExtensionData("main", ImmutableDictionary<string, string>.Empty));
        mainExtensionMock.Setup(m => m.NewExtensionEncoder()).Returns(new DummyEncoder());
        mainExtensionMock.Setup(m => m.NewExtensionDecoder()).Returns(new DummyDecoder());

        main2HandshakerMock.Setup(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("main2")))
                .Returns(main2ExtensionMock.Object);

        main2ExtensionMock.Setup(m => m.Rsv()).Returns(IWebSocketExtension.RSV1);
        main2ExtensionMock.Setup(m => m.NewReponseData()).Returns(
                new WebSocketExtensionData("main2", ImmutableDictionary<string, string>.Empty));
        main2ExtensionMock.Setup(m => m.NewExtensionEncoder()).Returns(new DummyEncoder());
        main2ExtensionMock.Setup(m => m.NewExtensionDecoder()).Returns(new DummyDecoder());

        // execute
        WebSocketServerExtensionHandler extensionHandler =
                new WebSocketServerExtensionHandler(mainHandshakerMock.Object, main2HandshakerMock.Object);
        EmbeddedChannel ch = new EmbeddedChannel(extensionHandler);

        IHttpRequest req = NewUpgradeRequest("main");
        Assert.True(ch.WriteInbound(req));
        Assert.True(ch.WriteInbound(ILastHttpContent.EMPTY_LAST_CONTENT));

        IHttpRequest req2 = NewUpgradeRequest("main2");
        Assert.True(ch.WriteInbound(req2));
        Assert.True(ch.WriteInbound(ILastHttpContent.EMPTY_LAST_CONTENT));

        IHttpResponse res = NewUpgradeResponse(null);
        Assert.True(ch.WriteOutbound(res));
        Assert.True(ch.WriteOutbound(ILastHttpContent.EMPTY_LAST_CONTENT));

        res = ch.ReadOutbound<IHttpResponse>();
        Assert.Equal("main", res.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_EXTENSIONS));
        ILastHttpContent content = ch.ReadOutbound<ILastHttpContent>();
        content.Release();

        Assert.Null(ch.Pipeline.Context(extensionHandler));
        Assert.True(ch.FinishAndReleaseAll());
    }

    [Fact]
    public void TestPipelineLimit()
    {
        TestPipelineLimit0(NewUpgradeRequest("main2"));
    }

    [Fact]
    public void TestPipelineLimitWithFullHttpRequest()
    {
        IFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/chat");
        WebSocketExtensionTestUtil.AddUpgradeHeaders(req, "main2");
        TestPipelineLimit0(req);
        Assert.Equal(0, req.RefCnt);
    }

    private void TestPipelineLimit0(IHttpRequest req2)
    {
        // initialize
        mainHandshakerMock.Setup(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("main")))
            .Returns(mainExtensionMock.Object);

        mainExtensionMock.Setup(m => m.Rsv()).Returns(IWebSocketExtension.RSV1);
        mainExtensionMock.Setup(m => m.NewReponseData()).Returns(
            new WebSocketExtensionData("main", ImmutableDictionary<string, string>.Empty));
        mainExtensionMock.Setup(m => m.NewExtensionEncoder()).Returns(new DummyEncoder());
        mainExtensionMock.Setup(m => m.NewExtensionDecoder()).Returns(new DummyDecoder());

        main2HandshakerMock.Setup(m => m.HandshakeExtension(WebSocketExtensionDataMatcher_("main2")))
            .Returns(main2ExtensionMock.Object);

        main2ExtensionMock.Setup(m => m.Rsv()).Returns(IWebSocketExtension.RSV1);
        main2ExtensionMock.Setup(m => m.NewReponseData()).Returns(
            new WebSocketExtensionData("main2", ImmutableDictionary<string, string>.Empty));
        main2ExtensionMock.Setup(m => m.NewExtensionEncoder()).Returns(new DummyEncoder());
        main2ExtensionMock.Setup(m => m.NewExtensionDecoder()).Returns(new DummyDecoder());

        // execute
        WebSocketServerExtensionHandler extensionHandler =
            new WebSocketServerExtensionHandler(1, mainHandshakerMock.Object, main2HandshakerMock.Object);
        EmbeddedChannel ch = new EmbeddedChannel(extensionHandler);

        IHttpRequest req = NewUpgradeRequest("main");
        Assert.True(ch.WriteInbound(req));
        Assert.True(ch.WriteInbound(ILastHttpContent.EMPTY_LAST_CONTENT));

        Assert.ThrowsAny<InvalidOperationException>(() => ch.WriteInbound(req2));

        // Should also close the channel.
        Assert.False(ch.IsActive);

        ch.FinishAndReleaseAll();
    }
}
