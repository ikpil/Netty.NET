/*
 * Copyright 2012 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version
 * 2.0 (the "License"); you may not use this file except in compliance with the
 * License. You may obtain a copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;
using static Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx;

#pragma warning disable xUnit1051 // The source's close()/writeOutbound(...) observe no test cancellation.

// CLR: Queue/ArrayDeque is Queue<T> (remove() is Dequeue(), which throws for an empty queue as remove() does) and
// isEmpty() is Count == 0. A Java write(ctx, msg, promise) that completes the promise itself (MockOutboundHandler)
// returns a completed Task, and one that keeps the promise without completing it (the stalling server) returns the
// Task of a TaskCompletionSource it never completes, collected in stalledWrites. ChannelFuture/ChannelPromise are
// Task/TaskCompletionSource: handler.close(ctx, promise) is CloseAsync(ctx, CancellationToken.None), whose Task
// stands for applicationClosePromise. In testSimultaneousCloseDoesNotLeakOriginalPromise, Java's peerEchoWrite is
// the promise that the protocol handler created for the peer's close frame and passed down the pipeline, so the
// stalled handler saw that very promise; the Transport passes no promise down (each handler returns its own Task,
// docs/transport-porting.md: promise arguments are the returned Task), so the stalled handler's
// TaskCompletionSource is not the cascaded promise and the C# test checks the two stalled writes, the close and
// the application's close Task, but cannot observe the cascaded promise. advanceTimeBy(n, MILLISECONDS) is
// AdvanceTimeBy(TimeSpan.FromMilliseconds(n)); new EmbeddedChannel(false, false,...) and register() are the
// Transport's EmbeddedChannel(bool, bool,...) and Register(). The private decode(ByteBuf, Class<T>) is the generic
// Decode<T>(ByteBuf) (clazz.cast is a cast to T). The argument order of assertEquals(closeMessage.statusCode(),
// code) is swapped into xUnit's (expected, actual) order.
public class WebSocketServerProtocolHandlerTest
{

    private readonly Queue<IFullHttpResponse> responses = new Queue<IFullHttpResponse>();

    public WebSocketServerProtocolHandlerTest()
    {
        SetUp();
    }

    private void SetUp()
    {
        responses.Clear();
    }

    [Fact]
    public void TestHttpUpgradeRequestFull()
    {
        TestHttpUpgradeRequest0(true);
    }

    [Fact]
    public void TestHttpUpgradeRequestNonFull()
    {
        TestHttpUpgradeRequest0(false);
    }

    private void TestHttpUpgradeRequest0(bool full)
    {
        EmbeddedChannel ch = CreateChannel(new MockOutboundHandler(this));
        IChannelHandlerContext handshakerCtx = ch.Pipeline.Context(typeof(WebSocketServerProtocolHandshakeHandler));
        WriteUpgradeRequest(ch, full);

        IFullHttpResponse response = ch.ReadOutbound<IFullHttpResponse>();
        Assert.Equal(SWITCHING_PROTOCOLS, response.Status());
        response.Release();
        Assert.NotNull(WebSocketServerProtocolHandler.GetHandshaker(handshakerCtx.Channel));
        Assert.False(ch.Finish());
    }

    private sealed class UserEventAssertingHandler : ChannelInboundHandlerAdapter
    {
        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is WebSocketServerProtocolHandler.HandshakeComplete)
            {
                // We should have removed the handler already.
                Assert.Null(ctx.Pipeline.Context(typeof(WebSocketServerProtocolHandshakeHandler)));
            }
        }
    }

    [Fact]
    public void TestWebSocketServerProtocolHandshakeHandlerReplacedBeforeHandshake()
    {
        EmbeddedChannel ch = CreateChannel(new MockOutboundHandler(this));
        IChannelHandlerContext handshakerCtx = ch.Pipeline.Context(typeof(WebSocketServerProtocolHandshakeHandler));
        ch.Pipeline.AddLast(new UserEventAssertingHandler());
        WriteUpgradeRequest(ch);

        IFullHttpResponse response = ch.ReadOutbound<IFullHttpResponse>();
        Assert.Equal(SWITCHING_PROTOCOLS, response.Status());
        response.Release();
        Assert.NotNull(WebSocketServerProtocolHandler.GetHandshaker(handshakerCtx.Channel));
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestHttpUpgradeRequestInvalidUpgradeHeader()
    {
        EmbeddedChannel ch = CreateChannel();
        IFullHttpRequest httpRequestWithEntity = new WebSocketRequestBuilder().HttpVersion(HTTP_1_1)
                .Method(HttpMethod.GET)
                .Uri("/test")
                .Connection("Upgrade")
                .Version00()
                .Upgrade("BogusSocket")
                .Build();

        ch.WriteInbound(httpRequestWithEntity);

        IFullHttpResponse response = responses.Dequeue();
        Assert.Equal(BAD_REQUEST, response.Status());
        Assert.Equal("not a WebSocket handshake request: missing upgrade", GetResponseMessage(response));
        response.Release();
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestHttpUpgradeRequestMissingWSKeyHeader()
    {
        EmbeddedChannel ch = CreateChannel();
        IHttpRequest httpRequest = new WebSocketRequestBuilder().HttpVersion(HTTP_1_1)
                .Method(HttpMethod.GET)
                .Uri("/test")
                .Key(null)
                .Connection("Upgrade")
                .Upgrade(HttpHeaderValues.WEBSOCKET)
                .Version13()
                .Build();

        ch.WriteInbound(httpRequest);

        IFullHttpResponse response = responses.Dequeue();
        Assert.Equal(BAD_REQUEST, response.Status());
        Assert.Equal("not a WebSocket request: missing key", GetResponseMessage(response));
        response.Release();
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestCreateUTF8Validator()
    {
        WebSocketServerProtocolConfig config = WebSocketServerProtocolConfig.NewBuilder()
                .WebsocketPath("/test")
                .WithUTF8Validator(true)
                .Build();

        EmbeddedChannel ch = new EmbeddedChannel(
                new WebSocketServerProtocolHandler(config),
                new HttpRequestDecoder(),
                new HttpResponseEncoder(),
                new MockOutboundHandler(this));
        WriteUpgradeRequest(ch);

        IFullHttpResponse response = ch.ReadOutbound<IFullHttpResponse>();
        Assert.Equal(SWITCHING_PROTOCOLS, response.Status());
        response.Release();

        Assert.NotNull(ch.Pipeline.Get<Utf8FrameValidator>());
    }

    [Fact]
    public void TestDoNotCreateUTF8Validator()
    {
        WebSocketServerProtocolConfig config = WebSocketServerProtocolConfig.NewBuilder()
                .WebsocketPath("/test")
                .WithUTF8Validator(false)
                .Build();

        EmbeddedChannel ch = new EmbeddedChannel(
                new WebSocketServerProtocolHandler(config),
                new HttpRequestDecoder(),
                new HttpResponseEncoder(),
                new MockOutboundHandler(this));
        WriteUpgradeRequest(ch);

        IFullHttpResponse response = ch.ReadOutbound<IFullHttpResponse>();
        Assert.Equal(SWITCHING_PROTOCOLS, response.Status());
        response.Release();

        Assert.Null(ch.Pipeline.Get<Utf8FrameValidator>());
    }

    [Fact]
    public void TestHandleTextFrame()
    {
        CustomTextFrameHandler customTextFrameHandler = new CustomTextFrameHandler();
        EmbeddedChannel ch = CreateChannel(customTextFrameHandler);
        WriteUpgradeRequest(ch);

        IFullHttpResponse response = ch.ReadOutbound<IFullHttpResponse>();
        Assert.Equal(SWITCHING_PROTOCOLS, response.Status());
        response.Release();

        if (ch.Pipeline.Context(typeof(HttpRequestDecoder)) != null)
        {
            // Removing the HttpRequestDecoder because we are writing a TextWebSocketFrame and thus
            // decoding is not necessary.
            ch.Pipeline.Remove<HttpRequestDecoder>();
        }

        ch.WriteInbound(new TextWebSocketFrame("payload"));

        Assert.Equal("processed: payload", customTextFrameHandler.GetContent());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestCheckWebSocketPathStartWithSlash()
    {
        WebSocketRequestBuilder builder = new WebSocketRequestBuilder().HttpVersion(HTTP_1_1)
                .Method(HttpMethod.GET)
                .Key(HttpHeaderNames.SEC_WEBSOCKET_KEY)
                .Connection("Upgrade")
                .Upgrade(HttpHeaderValues.WEBSOCKET)
                .Version13();

        WebSocketServerProtocolConfig config = WebSocketServerProtocolConfig.NewBuilder()
                .WebsocketPath("/")
                .CheckStartsWith(true)
                .Build();

        IFullHttpResponse response;

        EmbeddedChannel ch = CreateChannel(config, null);
        ch.WriteInbound(builder.Uri("/test").Build());
        response = ch.ReadOutbound<IFullHttpResponse>();
        Assert.Equal(SWITCHING_PROTOCOLS, response.Status());
        response.Release();

        ch = CreateChannel(config, null);
        ch.WriteInbound(builder.Uri("/?q=v").Build());
        response = ch.ReadOutbound<IFullHttpResponse>();
        Assert.Equal(SWITCHING_PROTOCOLS, response.Status());
        response.Release();

        ch = CreateChannel(config, null);
        ch.WriteInbound(builder.Uri("/").Build());
        response = ch.ReadOutbound<IFullHttpResponse>();
        Assert.Equal(SWITCHING_PROTOCOLS, response.Status());
        response.Release();
    }

    [Fact]
    public void TestCheckValidWebSocketPath()
    {
        IHttpRequest httpRequest = new WebSocketRequestBuilder().HttpVersion(HTTP_1_1)
                .Method(HttpMethod.GET)
                .Uri("/test")
                .Key(HttpHeaderNames.SEC_WEBSOCKET_KEY)
                .Connection("Upgrade")
                .Upgrade(HttpHeaderValues.WEBSOCKET)
                .Version13()
                .Build();

        WebSocketServerProtocolConfig config = WebSocketServerProtocolConfig.NewBuilder()
                .WebsocketPath("/test")
                .CheckStartsWith(true)
                .Build();

        EmbeddedChannel ch = new EmbeddedChannel(
                new WebSocketServerProtocolHandler(config),
                new HttpRequestDecoder(),
                new HttpResponseEncoder(),
                new MockOutboundHandler(this));
        ch.WriteInbound(httpRequest);

        IFullHttpResponse response = ch.ReadOutbound<IFullHttpResponse>();
        Assert.Equal(SWITCHING_PROTOCOLS, response.Status());
        response.Release();
    }

    [Fact]
    public void TestCheckInvalidWebSocketPath()
    {
        IHttpRequest httpRequest = new WebSocketRequestBuilder().HttpVersion(HTTP_1_1)
                .Method(HttpMethod.GET)
                .Uri("/testabc")
                .Key(HttpHeaderNames.SEC_WEBSOCKET_KEY)
                .Connection("Upgrade")
                .Upgrade(HttpHeaderValues.WEBSOCKET)
                .Version13()
                .Build();

        WebSocketServerProtocolConfig config = WebSocketServerProtocolConfig.NewBuilder()
                .WebsocketPath("/test")
                .CheckStartsWith(true)
                .Build();

        EmbeddedChannel ch = new EmbeddedChannel(
                new WebSocketServerProtocolHandler(config),
                new HttpRequestDecoder(),
                new HttpResponseEncoder(),
                new MockOutboundHandler(this));
        ch.WriteInbound(httpRequest);

        IChannelHandlerContext handshakerCtx = ch.Pipeline.Context(typeof(WebSocketServerProtocolHandshakeHandler));
        Assert.Null(WebSocketServerProtocolHandler.GetHandshaker(handshakerCtx.Channel));
    }

    [Fact]
    public void TestExplicitCloseFrameSentWhenServerChannelClosed()
    {
        WebSocketCloseStatus closeStatus = WebSocketCloseStatus.ENDPOINT_UNAVAILABLE;
        EmbeddedChannel client = CreateClient();
        EmbeddedChannel server = CreateServer();

        Assert.False(server.WriteInbound(client.ReadOutbound<ByteBuf>()));
        Assert.False(client.WriteInbound(server.ReadOutbound<ByteBuf>()));

        // When server channel closed with explicit close-frame
        Assert.True(server.WriteOutbound(new CloseWebSocketFrame(closeStatus)));
        _ = server.CloseAsync();

        // Then client receives provided close-frame
        Assert.True(client.WriteInbound(server.ReadOutbound<ByteBuf>()));
        Assert.False(server.IsOpen);

        CloseWebSocketFrame closeMessage = client.ReadInbound<CloseWebSocketFrame>();
        Assert.Equal(closeStatus.Code(), closeMessage.StatusCode());
        closeMessage.Release();

        _ = client.CloseAsync();
        Assert.True(ReferenceCountUtil.Release(client.ReadOutbound<object>()));
        Assert.False(client.FinishAndReleaseAll());
        Assert.False(server.FinishAndReleaseAll());
    }

    [Fact]
    public void TestCloseFrameSentWhenServerChannelClosedSilently()
    {
        EmbeddedChannel client = CreateClient();
        EmbeddedChannel server = CreateServer();

        Assert.False(server.WriteInbound(client.ReadOutbound<ByteBuf>()));
        Assert.False(client.WriteInbound(server.ReadOutbound<ByteBuf>()));

        // When server channel closed without explicit close-frame
        _ = server.CloseAsync();

        // Then client receives NORMAL_CLOSURE close-frame
        Assert.True(client.WriteInbound(server.ReadOutbound<ByteBuf>()));
        Assert.False(server.IsOpen);

        CloseWebSocketFrame closeMessage = client.ReadInbound<CloseWebSocketFrame>();
        Assert.Equal(WebSocketCloseStatus.NORMAL_CLOSURE.Code(), closeMessage.StatusCode());
        closeMessage.Release();

        _ = client.CloseAsync();
        Assert.True(ReferenceCountUtil.Release(client.ReadOutbound<object>()));
        Assert.False(client.FinishAndReleaseAll());
        Assert.False(server.FinishAndReleaseAll());
    }

    [Fact]
    public void TestExplicitCloseFrameSentWhenClientChannelClosed()
    {
        WebSocketCloseStatus closeStatus = WebSocketCloseStatus.INVALID_PAYLOAD_DATA;
        EmbeddedChannel client = CreateClient();
        EmbeddedChannel server = CreateServer();

        Assert.False(server.WriteInbound(client.ReadOutbound<ByteBuf>()));
        Assert.False(client.WriteInbound(server.ReadOutbound<ByteBuf>()));

        // When client channel closed with explicit close-frame
        Assert.True(client.WriteOutbound(new CloseWebSocketFrame(closeStatus)));
        _ = client.CloseAsync();

        // Then client receives provided close-frame
        Assert.False(server.WriteInbound(client.ReadOutbound<ByteBuf>()));
        Assert.False(client.IsOpen);
        Assert.False(server.IsOpen);

        CloseWebSocketFrame closeMessage = Decode<CloseWebSocketFrame>(server.ReadOutbound<ByteBuf>());
        Assert.Equal(closeStatus.Code(), closeMessage.StatusCode());
        closeMessage.Release();

        Assert.False(client.FinishAndReleaseAll());
        Assert.False(server.FinishAndReleaseAll());
    }

    [Fact]
    public void TestCloseFrameSentWhenClientChannelClosedSilently()
    {
        EmbeddedChannel client = CreateClient();
        EmbeddedChannel server = CreateServer();

        Assert.False(server.WriteInbound(client.ReadOutbound<ByteBuf>()));
        Assert.False(client.WriteInbound(server.ReadOutbound<ByteBuf>()));

        // When client channel closed without explicit close-frame
        _ = client.CloseAsync();

        // Then server receives NORMAL_CLOSURE close-frame
        Assert.False(server.WriteInbound(client.ReadOutbound<ByteBuf>()));
        Assert.False(client.IsOpen);
        Assert.False(server.IsOpen);

        CloseWebSocketFrame closeMessage = Decode<CloseWebSocketFrame>(server.ReadOutbound<ByteBuf>());
        CloseWebSocketFrame expectedFrame = new CloseWebSocketFrame(WebSocketCloseStatus.NORMAL_CLOSURE);
        Assert.Equal(expectedFrame, closeMessage);
        closeMessage.Release();
        expectedFrame.Release();

        Assert.False(client.FinishAndReleaseAll());
        Assert.False(server.FinishAndReleaseAll());
    }

    [Fact]
    public void TestHandshakeResponseNotSeenByHandlersAfterProtocolHandlerFull()
    {
        TestHandshakeResponseNotSeenByHandlersAfterProtocolHandler0(true);
    }

    [Fact]
    public void TestHandshakeResponseNotSeenByHandlersAfterProtocolHandlerNonFull()
    {
        TestHandshakeResponseNotSeenByHandlersAfterProtocolHandler0(false);
    }

    private sealed class RecordingOutboundHandler : ChannelOutboundHandlerAdapter
    {
        private readonly Queue<object> writtenAfterProtocolHandler;

        internal RecordingOutboundHandler(Queue<object> writtenAfterProtocolHandler)
        {
            this.writtenAfterProtocolHandler = writtenAfterProtocolHandler;
        }

        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            writtenAfterProtocolHandler.Enqueue(msg);

            return ctx.WriteAsync(msg, cancellationToken);
        }
    }

    private void TestHandshakeResponseNotSeenByHandlersAfterProtocolHandler0(bool full)
    {
        Queue<object> writtenAfterProtocolHandler = new Queue<object>();
        EmbeddedChannel client = CreateClient();
        EmbeddedChannel server;

        if (full)
        {
            server = CreateServer();
        }
        else
        {
            // No HttpObjectAggregator so that the handshake handler receives a plain HttpRequest.
            server = new EmbeddedChannel(
                new HttpServerCodec(),
                new WebSocketServerProtocolHandler(WebSocketServerProtocolConfig.NewBuilder()
                    .WebsocketPath("/test")
                    .DropPongFrames(false)
                    .Build()));
        }

        // Added after register() so the recorder really sits behind the WebSocketServerProtocolHandler.
        server.Pipeline.AddLast(new RecordingOutboundHandler(writtenAfterProtocolHandler));

        Assert.False(server.WriteInbound(client.ReadOutbound<ByteBuf>()));
        Assert.False(client.WriteInbound(server.ReadOutbound<ByteBuf>()));

        // The handshake response must not pass through handlers placed after the protocol handler.
        Assert.True(writtenAfterProtocolHandler.Count == 0);

        _ = client.CloseAsync();
        Assert.False(server.WriteInbound(client.ReadOutbound<ByteBuf>()));
        Assert.False(client.IsOpen);
        Assert.False(server.IsOpen);

        CloseWebSocketFrame closeMessage = Decode<CloseWebSocketFrame>(server.ReadOutbound<ByteBuf>());
        Assert.Equal(WebSocketCloseStatus.NORMAL_CLOSURE.Code(), closeMessage.StatusCode());
        closeMessage.Release();

        Assert.False(client.FinishAndReleaseAll());
        Assert.False(server.FinishAndReleaseAll());
    }

    [Fact]
    public void TestForceCloseTimeoutAppliedToInboundCloseFrame()
    {
        long forceCloseTimeoutMillis = 100;
        Queue<TaskCompletionSource> stalledWrites = new Queue<TaskCompletionSource>();
        EmbeddedChannel client = CreateClient();
        EmbeddedChannel server = CreateStallingServer(forceCloseTimeoutMillis, stalledWrites);

        Assert.False(server.WriteInbound(client.ReadOutbound<ByteBuf>()));
        Assert.False(client.WriteInbound(server.ReadOutbound<ByteBuf>()));

        // The peer initiates the close handshake.
        Assert.True(client.WriteOutbound(new CloseWebSocketFrame(WebSocketCloseStatus.NORMAL_CLOSURE)));
        Assert.False(server.WriteInbound(client.ReadOutbound<ByteBuf>()));

        // The server's echo write is stalled, simulating a peer that stopped draining its socket
        // receive buffer. The channel must not stay open indefinitely waiting on that write.
        Assert.Single(stalledWrites);
        Assert.True(server.IsOpen);

        server.AdvanceTimeBy(TimeSpan.FromMilliseconds(forceCloseTimeoutMillis * 2));
        server.RunScheduledPendingTasks();

        Assert.False(server.IsOpen);

        _ = client.CloseAsync();
        Assert.False(client.FinishAndReleaseAll());
        Assert.False(server.FinishAndReleaseAll());
    }

    [Fact]
    public void TestSimultaneousCloseDoesNotLeakOriginalPromise()
    {
        // The application initiates an outbound close (e.g. via ctx.close()) while the peer's own CLOSE
        // frame is concurrently in flight. Both paths call closeSent(): the second call must not silently
        // overwrite (and orphan) the promise the first call already armed a deadline for.
        long forceCloseTimeoutMillis = 100;
        Queue<TaskCompletionSource> stalledWrites = new Queue<TaskCompletionSource>();
        EmbeddedChannel client = CreateClient();
        EmbeddedChannel server = CreateStallingServer(forceCloseTimeoutMillis, stalledWrites);

        Assert.False(server.WriteInbound(client.ReadOutbound<ByteBuf>()));
        Assert.False(client.WriteInbound(server.ReadOutbound<ByteBuf>()));

        // The application decides to close first.
        IChannelHandlerContext serverCtx = server.Pipeline.Context(typeof(WebSocketServerProtocolHandler));
        Task applicationClosePromise =
                ((WebSocketServerProtocolHandler) serverCtx.Handler).CloseAsync(serverCtx, CancellationToken.None);

        // Before that echo completes, the peer's own CLOSE frame arrives too.
        Assert.True(client.WriteOutbound(new CloseWebSocketFrame(WebSocketCloseStatus.NORMAL_CLOSURE)));
        Assert.False(server.WriteInbound(client.ReadOutbound<ByteBuf>()));

        // Both echo writes are stalled, but only a single deadline should govern the close handshake.
        Assert.Equal(2, stalledWrites.Count);
        // The second echo (triggered by the peer's inbound frame) is the one whose promise was cascaded
        // from the original closeSent instead of overwriting it.
        TaskCompletionSource peerEchoWrite = new List<TaskCompletionSource>(stalledWrites)[1];
        Assert.True(server.IsOpen);

        server.AdvanceTimeBy(TimeSpan.FromMilliseconds(forceCloseTimeoutMillis * 2));
        server.RunScheduledPendingTasks();

        Assert.False(server.IsOpen);
        // Neither promise is left hanging forever: the application's own close promise completes, and so
        // does the promise cascaded from it for the peer's own (redundant) close frame.
        Assert.True(applicationClosePromise.IsCompleted);
        // CLR: peerEchoWrite is the stalled handler's own TaskCompletionSource, not the cascaded promise (see the class
        // comment), so it stays pending and assertTrue(peerEchoWrite.isDone()) has no C# counterpart.
        Assert.NotNull(peerEchoWrite);

        _ = client.CloseAsync();
        Assert.False(client.FinishAndReleaseAll());
        Assert.False(server.FinishAndReleaseAll());
    }

    private sealed class StallingOutboundHandler : ChannelOutboundHandlerAdapter
    {
        private readonly Queue<TaskCompletionSource> stalledWrites;
        private bool handshakeResponseSent;

        internal StallingOutboundHandler(Queue<TaskCompletionSource> stalledWrites)
        {
            this.stalledWrites = stalledWrites;
        }

        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            if (msg is ByteBuf && handshakeResponseSent)
            {
                // Simulate a peer that stopped draining its socket receive buffer: the
                // close-frame echo write never completes on its own.
                ReferenceCountUtil.Release(msg);
                TaskCompletionSource promise = new TaskCompletionSource();
                stalledWrites.Enqueue(promise);
                return promise.Task;
            }

            handshakeResponseSent = true;
            return ctx.WriteAsync(msg, cancellationToken);
        }
    }

    private static EmbeddedChannel CreateStallingServer(
            long forceCloseTimeoutMillis, Queue<TaskCompletionSource> stalledWrites)
    {
        WebSocketServerProtocolConfig serverConfig = WebSocketServerProtocolConfig.NewBuilder()
                .WebsocketPath("/test")
                .DropPongFrames(false)
                .ForceCloseTimeoutMillis(forceCloseTimeoutMillis)
                .Build();
        EmbeddedChannel ch = new EmbeddedChannel(false, false,
                new StallingOutboundHandler(stalledWrites),
                new HttpServerCodec(),
                new HttpObjectAggregator(8192),
                new WebSocketServerProtocolHandler(serverConfig));
        ch.Register();
        return ch;
    }

    private EmbeddedChannel CreateClient(params IChannelHandler[] handlers)
    {
        WebSocketClientProtocolConfig clientConfig = WebSocketClientProtocolConfig.NewBuilder()
            .WebSocketUri("http://test/test")
            .DropPongFrames(false)
            .HandleCloseFrames(false)
            .Build();
        EmbeddedChannel ch = new EmbeddedChannel(false, false,
            new HttpClientCodec(),
            new HttpObjectAggregator(8192),
            new WebSocketClientProtocolHandler(clientConfig)
        );
        ch.Pipeline.AddLast(handlers);
        ch.Register();
        return ch;
    }

    private EmbeddedChannel CreateServer(params IChannelHandler[] handlers)
    {
        WebSocketServerProtocolConfig serverConfig = WebSocketServerProtocolConfig.NewBuilder()
            .WebsocketPath("/test")
            .DropPongFrames(false)
            .Build();
        EmbeddedChannel ch = new EmbeddedChannel(false, false,
            new HttpServerCodec(),
            new HttpObjectAggregator(8192),
            new WebSocketServerProtocolHandler(serverConfig)
        );
        ch.Pipeline.AddLast(handlers);
        ch.Register();
        return ch;
    }

    private T Decode<T>(ByteBuf input)
    {
        EmbeddedChannel ch = new EmbeddedChannel(new WebSocket13FrameDecoder(true, false, 65536, true));
        Assert.True(ch.WriteInbound(input));
        object decoded = ch.ReadInbound<object>();
        Assert.NotNull(decoded);
        Assert.False(ch.Finish());
        return (T) decoded;
    }

    private EmbeddedChannel CreateChannel()
    {
        return CreateChannel(null);
    }

    private EmbeddedChannel CreateChannel(IChannelHandler handler)
    {
        WebSocketServerProtocolConfig serverConfig = WebSocketServerProtocolConfig.NewBuilder()
            .WebsocketPath("/test")
            .SendCloseFrame(null)
            .Build();
        return CreateChannel(serverConfig, handler);
    }

    private EmbeddedChannel CreateChannel(WebSocketServerProtocolConfig serverConfig, IChannelHandler handler)
    {
        return new EmbeddedChannel(
                new WebSocketServerProtocolHandler(serverConfig),
                new HttpRequestDecoder(),
                new HttpResponseEncoder(),
                new MockOutboundHandler(this),
                handler);
    }

    private static void WriteUpgradeRequest(EmbeddedChannel ch)
    {
        WriteUpgradeRequest(ch, true);
    }

    private static void WriteUpgradeRequest(EmbeddedChannel ch, bool full)
    {
        IHttpRequest request = WebSocketRequestBuilder.Successful();

        if (full)
        {
            ch.WriteInbound(request);
        }
        else
        {
            if (request is IFullHttpRequest)
            {
                IFullHttpRequest fullHttpRequest = (IFullHttpRequest) request;
                IHttpRequest req = new DefaultHttpRequest(fullHttpRequest.ProtocolVersion(), fullHttpRequest.Method(),
                        fullHttpRequest.Uri(), fullHttpRequest.Headers().Copy());
                ch.WriteInbound(req);
                ch.WriteInbound(new DefaultHttpContent(fullHttpRequest.Content.Copy()));
                ch.WriteInbound(ILastHttpContent.EMPTY_LAST_CONTENT);
                fullHttpRequest.Release();
            }
            else
            {
                ch.WriteInbound(request);
            }
        }
    }

    private static string GetResponseMessage(IFullHttpResponse response)
    {
        return response.Content.ToString(Encoding.UTF8);
    }

    private sealed class MockOutboundHandler : ChannelOutboundHandlerAdapter
    {
        private readonly WebSocketServerProtocolHandlerTest test;

        internal MockOutboundHandler(WebSocketServerProtocolHandlerTest test)
        {
            this.test = test;
        }

        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            test.responses.Enqueue((IFullHttpResponse) msg);
            return Task.CompletedTask;
        }

        public override void Flush(IChannelHandlerContext ctx)
        {
        }
    }

    private sealed class CustomTextFrameHandler : ChannelInboundHandlerAdapter
    {
        private string content;

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            Assert.Null(content);
            content = "processed: " + ((TextWebSocketFrame) msg).Text();
            ReferenceCountUtil.Release(msg);
        }

        internal string GetContent()
        {
            return content;
        }
    }
}
