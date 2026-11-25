/*
 * Copyright 2014 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.WebSocketx.WebSocketClientProtocolHandler;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx;

#pragma warning disable xUnit1051 // The source's close(...)/writeAndFlush(...) observe no test cancellation.
#pragma warning disable xUnit1031 // The source blocks with syncUninterruptibly() on the handshake future.

// CLR: @BeforeEach setUp() runs from the constructor, since xUnit creates a new instance of the test class for every
// test (https://xunit.net/docs/shared-context), and the inner class CloseNoOpServerProtocolHandler and the anonymous
// SimpleChannelInboundHandler subclasses are nested classes that receive the test instance whose fields they set. The
// deprecated WebSocketServerProtocolHandler.ServerHandshakeStateEvent.HANDSHAKE_COMPLETE is not ported (its @deprecated
// text names the HandshakeComplete class, owner decision 2026-11-16), so the server handlers of testHandover and
// testClientHandshakeTimeout react to the HandshakeComplete event that the handshake handler fires right after it in
// Java: they set serverReceivedHandshake, write the message and keep the event, as the two Java branches did together.
// java.net.URI is System.Uri (new URI(s) is new Uri(s, UriKind.RelativeOrAbsolute)). ChannelFuture is Task: isDone() is
// IsCompleted and syncUninterruptibly() is GetAwaiter().GetResult(), which throws the faulted Task's exception itself;
// ctx.channel().closeFuture().addListener(...) is a continuation of CloseFuture, and close(ctx, ctx.newPromise()) is
// CloseAsync(ctx, CancellationToken.None). Class.getName() of the pipeline names is Type.FullName, the package-private
// getHandshakeFuture() and isForceCloseComplete() (the latter through the internal forwarder
// InvokeIsForceCloseComplete()) are reached through InternalsVisibleTo, and the anonymous Executable is a lambda for
// Assert.ThrowsAny. @Timeout(10000 ms) is the xUnit Timeout of [Fact], which applies to async tests only, so
// testClientHandshakerForceClose runs its body with Task.Run. Thread.sleep is Thread.Sleep, and readOutbound() is
// ReadOutbound<T>().
public class WebSocketHandshakeHandOverTest
{

    private bool serverReceivedHandshake;
    private WebSocketServerProtocolHandler.HandshakeComplete serverHandshakeComplete;
    private bool clientReceivedHandshake;
    private bool clientReceivedMessage;
    private bool serverReceivedCloseHandshake;
    private volatile bool clientForceClosed;
    private bool clientHandshakeTimeout;

    private sealed class CloseNoOpServerProtocolHandler : WebSocketServerProtocolHandler
    {
        private readonly WebSocketHandshakeHandOverTest test;

        internal CloseNoOpServerProtocolHandler(WebSocketHandshakeHandOverTest test, string websocketPath)
            : base(WebSocketServerProtocolConfig.NewBuilder()
                .WebsocketPath(websocketPath)
                .AllowExtensions(false)
                .SendCloseFrame(null)
                .Build())
        {
            this.test = test;
        }

        protected override void Decode(IChannelHandlerContext ctx, WebSocketFrame frame, IList<object> @out)
        {
            if (frame is CloseWebSocketFrame)
            {
                test.serverReceivedCloseHandshake = true;
                return;
            }
            base.Decode(ctx, frame, @out);
        }
    }

    public WebSocketHandshakeHandOverTest()
    {
        SetUp();
    }

    private void SetUp()
    {
        serverReceivedHandshake = false;
        serverHandshakeComplete = null;
        clientReceivedHandshake = false;
        clientReceivedMessage = false;
        serverReceivedCloseHandshake = false;
        clientForceClosed = false;
        clientHandshakeTimeout = false;
    }

    private sealed class ServerHandler : SimpleChannelInboundHandler<object>
    {
        private readonly WebSocketHandshakeHandOverTest test;

        internal ServerHandler(WebSocketHandshakeHandOverTest test)
        {
            this.test = test;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is WebSocketServerProtocolHandler.HandshakeComplete)
            {
                test.serverReceivedHandshake = true;
                // immediately send a message to the client on connect
                _ = ctx.WriteAndFlushAsync(new TextWebSocketFrame("abc"));
                test.serverHandshakeComplete = (WebSocketServerProtocolHandler.HandshakeComplete) evt;
            }
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
        {
        }
    }

    private sealed class ClientHandler : SimpleChannelInboundHandler<object>
    {
        private readonly WebSocketHandshakeHandOverTest test;

        internal ClientHandler(WebSocketHandshakeHandOverTest test)
        {
            this.test = test;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (Equals(evt, ClientHandshakeStateEvent.HANDSHAKE_COMPLETE))
            {
                test.clientReceivedHandshake = true;
            }
            else if (Equals(evt, ClientHandshakeStateEvent.HANDSHAKE_TIMEOUT))
            {
                test.clientHandshakeTimeout = true;
            }
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
        {
            if (msg is TextWebSocketFrame)
            {
                test.clientReceivedMessage = true;
            }
        }
    }

    [Fact]
    public void TestHandover()
    {
        EmbeddedChannel serverChannel = CreateServerChannel(new ServerHandler(this));

        EmbeddedChannel clientChannel = CreateClientChannel(new ClientHandler(this));

        // Transfer the handshake from the client to the server
        TransferAllDataWithMerge(clientChannel, serverChannel);
        Assert.True(serverReceivedHandshake);
        Assert.NotNull(serverHandshakeComplete);
        Assert.Equal("/test", serverHandshakeComplete.RequestUri());
        Assert.Equal(8, serverHandshakeComplete.RequestHeaders().Size());
        Assert.Equal("test-proto-2", serverHandshakeComplete.SelectedSubprotocol());

        // Transfer the handshake response and the websocket message to the client
        TransferAllDataWithMerge(serverChannel, clientChannel);
        Assert.True(clientReceivedHandshake);
        Assert.True(clientReceivedMessage);
    }

    [Fact]
    public void TestClientHandshakeTimeout()
    {
        EmbeddedChannel serverChannel = CreateServerChannel(new ServerHandler(this));

        EmbeddedChannel clientChannel = CreateClientChannel(new ClientHandler(this), 100);
        // Client send the handshake request to server
        TransferAllDataWithMerge(clientChannel, serverChannel);
        // Server do not send the response back
        // transferAllDataWithMerge(serverChannel, clientChannel);
        WebSocketClientProtocolHandshakeHandler handshakeHandler =
                (WebSocketClientProtocolHandshakeHandler) clientChannel
                        .Pipeline.Get(typeof(WebSocketClientProtocolHandshakeHandler).FullName);

        while (!handshakeHandler.GetHandshakeFuture().IsCompleted)
        {
            Thread.Sleep(10);
            // We need to run all pending tasks as the handshake timeout is scheduled on the EventLoop.
            clientChannel.RunScheduledPendingTasks();
        }
        Assert.True(clientHandshakeTimeout);
        Assert.False(clientReceivedHandshake);
        Assert.False(clientReceivedMessage);
        // Should throw WebSocketHandshakeException
        try
        {
            Assert.ThrowsAny<WebSocketHandshakeException>(() =>
                    handshakeHandler.GetHandshakeFuture().GetAwaiter().GetResult());
        }
        finally
        {
            serverChannel.FinishAndReleaseAll();
        }
    }

    /**
     * Tests a scenario when channel is closed while the handshake is in progress. Validates that the handshake
     * future is notified in such cases.
     */
    [Fact]
    public void TestHandshakeFutureIsNotifiedOnChannelClose()
    {
        EmbeddedChannel clientChannel = CreateClientChannel(null);
        EmbeddedChannel serverChannel = CreateServerChannel(null);

        try
        {
            // Start handshake from client to server but don't complete the handshake for the purpose of this test.
            TransferAllDataWithMerge(clientChannel, serverChannel);

            WebSocketClientProtocolHandler clientWsHandler =
                    clientChannel.Pipeline.Get<WebSocketClientProtocolHandler>();
            WebSocketClientProtocolHandshakeHandler clientWsHandshakeHandler =
                    clientChannel.Pipeline.Get<WebSocketClientProtocolHandshakeHandler>();

            IChannelHandlerContext ctx = clientChannel.Pipeline.Context(typeof(WebSocketClientProtocolHandler));

            // Close the channel while the handshake is in progress. The channel could be closed before the handshake is
            // complete due to a number of varied reasons. To reproduce the test scenario for this test case,
            // we would manually close the channel.
            _ = clientWsHandler.CloseAsync(ctx, CancellationToken.None);

            // At this stage handshake is incomplete but the handshake future should be completed exceptionally since
            // channel is closed.
            Assert.True(clientWsHandshakeHandler.GetHandshakeFuture().IsCompleted);
        }
        finally
        {
            serverChannel.FinishAndReleaseAll();
            clientChannel.FinishAndReleaseAll();
        }
    }

    private sealed class ForceCloseClientHandler : SimpleChannelInboundHandler<object>
    {
        private readonly WebSocketHandshakeHandOverTest test;
        private readonly WebSocketClientHandshaker handshaker;

        internal ForceCloseClientHandler(WebSocketHandshakeHandOverTest test, WebSocketClientHandshaker handshaker)
        {
            this.test = test;
            this.handshaker = handshaker;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (Equals(evt, ClientHandshakeStateEvent.HANDSHAKE_COMPLETE))
            {
                _ = ctx.Channel.CloseFuture.ContinueWith(future => test.clientForceClosed = true,
                        CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                _ = handshaker.CloseAsync(ctx.Channel, new CloseWebSocketFrame());
            }
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
        {
        }
    }

    private sealed class NoOpHandler : SimpleChannelInboundHandler<object>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
        {
        }
    }

    [Fact(Timeout = 10000)]
    public Task TestClientHandshakerForceClose()
    {
        return Task.Run(TestClientHandshakerForceClose0, TestContext.Current.CancellationToken);
    }

    // CLR: the body of testClientHandshakerForceClose, run by the [Fact(Timeout)] test above.
    private void TestClientHandshakerForceClose0()
    {
        WebSocketClientHandshaker handshaker = WebSocketClientHandshakerFactory.NewHandshaker(
                new Uri("ws://localhost:1234/test", UriKind.RelativeOrAbsolute), WebSocketVersion.V13, null, true,
                EmptyHttpHeaders.INSTANCE, int.MaxValue, true, false, 20);

        EmbeddedChannel serverChannel = CreateServerChannel(
                new CloseNoOpServerProtocolHandler(this, "/test"),
                new NoOpHandler());

        EmbeddedChannel clientChannel = CreateClientChannel(handshaker, new ForceCloseClientHandler(this, handshaker));

        // Transfer the handshake from the client to the server
        TransferAllDataWithMerge(clientChannel, serverChannel);
        // Transfer the handshake from the server to client
        TransferAllDataWithMerge(serverChannel, clientChannel);

        // Transfer closing handshake
        TransferAllDataWithMerge(clientChannel, serverChannel);
        Assert.True(serverReceivedCloseHandshake);
        // Should not be closed yet as we disabled closing the connection on the server
        Assert.False(clientForceClosed);

        while (!clientForceClosed)
        {
            Thread.Sleep(10);
            // We need to run all pending tasks as the force close timeout is scheduled on the EventLoop.
            clientChannel.RunPendingTasks();
        }

        // clientForceClosed would be set to TRUE after any close,
        // so check here that force close timeout was actually fired
        Assert.True(handshaker.InvokeIsForceCloseComplete());

        // Both should be empty
        Assert.False(serverChannel.FinishAndReleaseAll());
        Assert.False(clientChannel.FinishAndReleaseAll());
    }

    /**
     * Transfers all pending data from the source channel into the destination channel.<br>
     * Merges all data into a single buffer before transmission into the destination.
     * @param srcChannel The source channel
     * @param dstChannel The destination channel
     */
    private static void TransferAllDataWithMerge(EmbeddedChannel srcChannel, EmbeddedChannel dstChannel)
    {
        ByteBuf mergedBuffer = null;
        for (;;)
        {
            object srcData = srcChannel.ReadOutbound<object>();

            if (srcData != null)
            {
                Assert.IsAssignableFrom<ByteBuf>(srcData);
                ByteBuf srcBuf = (ByteBuf) srcData;
                try
                {
                    if (mergedBuffer == null)
                    {
                        mergedBuffer = Unpooled.Buffer();
                    }
                    mergedBuffer.WriteBytes(srcBuf);
                }
                finally
                {
                    srcBuf.Release();
                }
            }
            else
            {
                break;
            }
        }

        if (mergedBuffer != null)
        {
            dstChannel.WriteInbound(mergedBuffer);
        }
    }

    private static EmbeddedChannel CreateClientChannel(IChannelHandler handler)
    {
        return CreateClientChannel(handler, WebSocketClientProtocolConfig.NewBuilder()
            .WebSocketUri("ws://localhost:1234/test")
            .Subprotocol("test-proto-2")
            .Build());
    }

    private static EmbeddedChannel CreateClientChannel(IChannelHandler handler, long timeoutMillis)
    {
        return CreateClientChannel(handler, WebSocketClientProtocolConfig.NewBuilder()
            .WebSocketUri("ws://localhost:1234/test")
            .Subprotocol("test-proto-2")
            .HandshakeTimeoutMillis(timeoutMillis)
            .Build());
    }

    private static EmbeddedChannel CreateClientChannel(IChannelHandler handler, WebSocketClientProtocolConfig config)
    {
        return new EmbeddedChannel(
                new HttpClientCodec(),
                new HttpObjectAggregator(8192),
                new WebSocketClientProtocolHandler(config),
                handler);
    }

    private static EmbeddedChannel CreateClientChannel(WebSocketClientHandshaker handshaker,
                                                       IChannelHandler handler)
    {
        return new EmbeddedChannel(
                new HttpClientCodec(),
                new HttpObjectAggregator(8192),
                // Note that we're switching off close frames handling on purpose to test forced close on timeout.
                new WebSocketClientProtocolHandler(handshaker, false, false),
                handler);
    }

    private static EmbeddedChannel CreateServerChannel(IChannelHandler handler)
    {
        return CreateServerChannel(
                new WebSocketServerProtocolHandler("/test", "test-proto-1, test-proto-2", false),
                handler);
    }

    private static EmbeddedChannel CreateServerChannel(WebSocketServerProtocolHandler webSocketHandler,
                                                       IChannelHandler handler)
    {
        return new EmbeddedChannel(
                new HttpServerCodec(),
                new HttpObjectAggregator(8192),
                webSocketHandler,
                handler);
    }
}
