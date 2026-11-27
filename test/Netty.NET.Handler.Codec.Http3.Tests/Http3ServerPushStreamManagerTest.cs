/*
 * Copyright 2021 The Netty Project
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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;
using Assert = XunitAssertMessages.AssertM;
using static Netty.NET.Handler.Codec.Http3.Tests.Http3TestUtils;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: @BeforeEach setUp() runs in the constructor and @AfterEach tearDown() is Dispose() (xUnit creates a test class
// instance per test and disposes it after the test). The project has no Moq (docs/codec-http3-porting.md), so
// mock(ChannelHandlerContext.class) with when(controlStreamHandlerCtx.channel()).thenReturn(localControlStream) is the
// private nested class MockChannelHandlerContext with Mockito's default answers: Channel returns the stubbed stream and
// every other member does nothing and returns null, false or the default value (the frames reach the handlers only
// through it, as in Java). The anonymous Http3RequestStreamInboundHandler is the private nested class
// ReleasingRequestStreamHandler. Http3ServerPushStreamManager.newPushStream(pushId, handler) is NewPushStreamAsync
// returning Task<IQuicStreamChannel> (get() is GetAwaiter().GetResult()); newPushStream(pushId, handler,
// bootstrapConfigurator, promise) with channel.eventLoop().newPromise() is the Task overload
// NewPushStreamAsync(pushId, handler, bootstrapConfigurator), into which the Promise overload folds
// (docs/codec-http3-porting.md, Shared CLR adaptations), and UnaryOperator.identity() is the lambda b => b.
// Callable<EmbeddedQuicStreamChannel> is Func<EmbeddedQuicStreamChannel>, and the @FunctionalInterface PushStreamFactory
// is a private delegate. PushStreamListener's write(ctx, msg, promise) is WriteAsync(ctx, msg, cancellationToken)
// returning base.WriteAsync (docs/codec-base-porting.md, Outbound operations). assertThrows(IllegalStateException.class,
// ...) is Assert.ThrowsAny<InvalidOperationException>, assertInstanceOf(T.class, x) Assert.IsAssignableFrom<T>, and
// assertEquals(int, long) a long literal. Http3.getLocalControlStream is Http3.GetLocalControlStream; the
// connection handler's localControlStreamHandler is the internal field. The throws clauses and @Nullable
// (org.jetbrains.annotations) have no counterpart.
public class Http3ServerPushStreamManagerTest : IDisposable
{

    private EmbeddedQuicChannel channel;
    private Http3ServerPushStreamManager manager;
    private Http3ServerConnectionHandler connectionHandler;
    private IChannelHandlerContext controlStreamHandlerCtx;
    private EmbeddedQuicStreamChannel localControlStream;

    public Http3ServerPushStreamManagerTest()
    {
        SetUp();
    }

    private void SetUp()
    {
        connectionHandler = new Http3ServerConnectionHandler(new ReleasingRequestStreamHandler(), null, null, null,
                true);
        channel = new EmbeddedQuicChannel(true, connectionHandler);
        localControlStream = (EmbeddedQuicStreamChannel) Http3.GetLocalControlStream(channel);
        Assert.NotNull(localControlStream);
        Assert.True(localControlStream.ReleaseOutbound()); // settings

        controlStreamHandlerCtx = new MockChannelHandlerContext(localControlStream);
        connectionHandler.localControlStreamHandler.ChannelRead(controlStreamHandlerCtx,
                new DefaultHttp3SettingsFrame());
        manager = new Http3ServerPushStreamManager(channel);
    }

    // CLR: the anonymous Http3RequestStreamInboundHandler of setUp().
    private sealed class ReleasingRequestStreamHandler : Http3RequestStreamInboundHandler
    {
        protected override void ChannelRead(IChannelHandlerContext ctx, IHttp3HeadersFrame frame)
        {
            ReferenceCountUtil.Release(frame);
        }

        protected override void ChannelRead(IChannelHandlerContext ctx, IHttp3DataFrame frame)
        {
            ReferenceCountUtil.Release(frame);
        }

        protected override void ChannelInputClosed(IChannelHandlerContext ctx)
        {
            // NOOP
        }
    }

    public void Dispose()
    {
        TearDown();
    }

    private void TearDown()
    {
        Assert.False(localControlStream.Finish());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void PushAllowed()
    {
        Assert.False(manager.IsPushAllowed());
        SendMaxPushId(1);
        Assert.True(manager.IsPushAllowed());
    }

    [Fact]
    public void ReserveWhenPushNotAllowed()
    {
        Assert.ThrowsAny<InvalidOperationException>(() => manager.ReserveNextPushId());
    }

    [Fact]
    public void ReserveWhenPushAllowed()
    {
        SendMaxPushId(2);
        Assert.Equal(0L, manager.ReserveNextPushId());
    }

    [Fact]
    public void ReservesAfterRefreshMaxId()
    {
        SendMaxPushId(0);
        Assert.Equal(0L, manager.ReserveNextPushId());
        Assert.False(manager.IsPushAllowed());
        SendMaxPushId(1);
        Assert.Equal(1L, manager.ReserveNextPushId());
    }

    [Fact]
    public void PushStreamNoHandler()
    {
        PushStreamCreateAndClose(pushId => NewPushStream(null, pushId));
    }

    [Fact]
    public void PushStreamWithHandler()
    {
        PushStreamListener pushStreamHandler = new PushStreamListener();
        PushStreamCreateAndClose(pushId => NewPushStream(pushStreamHandler, pushId));
        Assert.Equal(1, pushStreamHandler.framesWritten.Count);
        Assert.IsAssignableFrom<IHttp3HeadersFrame>(pushStreamHandler.framesWritten[0]);
    }

    [Fact]
    public void PushStreamWithBootstrapNoHandler()
    {
        PushStreamWithBootstrapCreateAndClose(null);
    }

    [Fact]
    public void PushStreamWithBootstrapWithHandler()
    {
        PushStreamListener pushStreamHandler = new PushStreamListener();
        PushStreamWithBootstrapCreateAndClose(pushStreamHandler);
        Assert.Equal(1, pushStreamHandler.framesWritten.Count);
        Assert.IsAssignableFrom<IHttp3HeadersFrame>(pushStreamHandler.framesWritten[0]);
    }

    private void PushStreamWithBootstrapCreateAndClose(IChannelHandler pushStreamHandler)
    {
        PushStreamCreateAndClose(pushId => NewPushStreamWithBootstrap(pushStreamHandler, pushId));
    }

    private void PushStreamCreateAndClose(PushStreamFactory pushStreamFactory)
    {
        SendMaxPushId(1);
        long pushId = manager.ReserveNextPushId();
        EmbeddedQuicStreamChannel pushStream = pushStreamFactory(pushId);

        DefaultHttp3HeadersFrame headerFrame = NewHeadersFrameWithPseudoHeaders();
        Assert.True(pushStream.WriteOutbound(headerFrame));
        ByteBuf encodedHeaders = pushStream.ReadOutbound<ByteBuf>();
        Assert.NotNull(encodedHeaders);
        encodedHeaders.Release();

        IChannelInboundHandler controlStreamListener = manager.ControlStreamListener();
        controlStreamListener.ChannelRead(controlStreamHandlerCtx, new DefaultHttp3CancelPushFrame(pushId));
        Assert.False(pushStream.IsActive);
    }

    private EmbeddedQuicStreamChannel NewPushStream(IChannelHandler pushStreamHandler,
                                                    long pushId)
    {
        return NewPushStream(() => (EmbeddedQuicStreamChannel) manager.NewPushStreamAsync(pushId, pushStreamHandler)
                .GetAwaiter().GetResult());
    }

    private EmbeddedQuicStreamChannel NewPushStreamWithBootstrap(IChannelHandler pushStreamHandler,
                                                                 long pushId)
    {
        return NewPushStream(() =>
        {
            // CLR: the Promise of channel.eventLoop().newPromise() is the returned Task (see the class comment).
            Task<IQuicStreamChannel> promise = manager.NewPushStreamAsync(pushId, pushStreamHandler, b => b);
            return (EmbeddedQuicStreamChannel) promise.GetAwaiter().GetResult();
        });
    }

    private EmbeddedQuicStreamChannel NewPushStream(Func<EmbeddedQuicStreamChannel> pushStreamFactory)
    {
        EmbeddedQuicStreamChannel pushStream = pushStreamFactory();
        Assert.True(pushStream.IsActive);
        pushStream.FlushOutbound(); // flush the stream header
        ByteBuf streamHeader = pushStream.ReadOutbound<ByteBuf>();
        Assert.NotNull(streamHeader);
        streamHeader.Release();
        return pushStream;
    }

    private void SendMaxPushId(int maxPushId)
    {
        DefaultHttp3MaxPushIdFrame maxPushIdFrame = new DefaultHttp3MaxPushIdFrame(maxPushId);
        connectionHandler.localControlStreamHandler.ChannelRead(controlStreamHandlerCtx, maxPushIdFrame);
        Assert.True(channel.IsActive);
    }

    private sealed class PushStreamListener : ChannelOutboundHandlerAdapter
    {
        internal readonly List<IHttp3PushStreamFrame> framesWritten = new List<IHttp3PushStreamFrame>();

        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            if (msg is IHttp3PushStreamFrame)
            {
                framesWritten.Add((IHttp3PushStreamFrame) msg);
            }
            return base.WriteAsync(ctx, msg, cancellationToken);
        }
    }

    private delegate EmbeddedQuicStreamChannel PushStreamFactory(long pushId);

    // CLR: mock(ChannelHandlerContext.class) with its stubbing (see the class comment).
    private sealed class MockChannelHandlerContext : IChannelHandlerContext
    {
        private readonly IChannel channel;

        internal MockChannelHandlerContext(IChannel channel)
        {
            this.channel = channel;
        }

        public IChannel Channel => channel;

        public IEventExecutor Executor => null;

        public string Name => null;

        public IChannelHandler Handler => null;

        public bool IsRemoved => false;

        public IChannelPipeline Pipeline => null;

        public IByteBufAllocator Alloc() => null;

        public IAttribute<T> Attr<T>(AttributeKey<T> key) where T : class => null;

        public bool HasAttr<T>(AttributeKey<T> key) where T : class => false;

        public IChannelInboundInvoker FireChannelRegistered() => null;

        public IChannelInboundInvoker FireChannelUnregistered() => null;

        public IChannelInboundInvoker FireChannelActive() => null;

        public IChannelInboundInvoker FireChannelInactive() => null;

        public IChannelInboundInvoker FireExceptionCaught(Exception cause) => null;

        public IChannelInboundInvoker FireUserEventTriggered(object @event) => null;

        public IChannelInboundInvoker FireChannelRead(object msg) => null;

        public IChannelInboundInvoker FireChannelReadComplete() => null;

        public IChannelInboundInvoker FireChannelWritabilityChanged() => null;

        public Task BindAsync(EndPoint localAddress, CancellationToken cancellationToken = default) => null;

        public Task ConnectAsync(EndPoint remoteAddress, EndPoint localAddress = null,
                                 CancellationToken cancellationToken = default) => null;

        public Task DisconnectAsync(CancellationToken cancellationToken = default) => null;

        public Task CloseAsync(CancellationToken cancellationToken = default) => null;

        public Task DeregisterAsync(CancellationToken cancellationToken = default) => null;

        public Task WriteAsync(object msg, CancellationToken cancellationToken = default) => null;

        public Task WriteAndFlushAsync(object msg, CancellationToken cancellationToken = default) => null;

        public IChannelOutboundInvoker Read() => null;

        public IChannelOutboundInvoker Flush() => null;
    }
}
