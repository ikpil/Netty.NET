/*
 * Copyright 2013 The Netty Project
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.PkiTesting;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Local;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: SslHandler drives System.Net.Security.SslStream (owner decision 2026-11-24, docs/handler-ssl-design.md), so an
// SSLEngine from SSLContext.getDefault().createSSLEngine() is the SslClientAuthenticationOptions or
// SslServerAuthenticationOptions that SslHandler(options) takes for a client-mode or server-mode engine (a JDK engine
// without setUseClientMode is in server mode: its TransportContext is built with "new SSLConfiguration(sslContext,
// false)", https://raw.githubusercontent.com/openjdk/jdk17u/master/src/java.base/share/classes/sun/security/ssl/TransportContext.java),
// and the session facts of engine() come from the internal SslHandler.Engine() accessor. SSLException,
// SSLHandshakeException and SSLProtocolException are AuthenticationException (ssl design decision 3);
// CertificateException and SignatureException are CryptographicException (ssl wave 1); ClosedChannelException is the
// Transport's ClosedChannelException; IllegalReferenceCountException and CodecException are the Common/codec types.
// The tests that used the @Deprecated CachedSelfSignedCertificate use CachedSelfSignedCertificate() below. ChannelFuture/
// ChannelPromise/Promise/Future are Task/TaskCompletionSource (docs/handler-porting.md, "Task promises"):
// sync()/syncUninterruptibly() are awaits, await()/awaitUninterruptibly() awaits that suppress the exception
// (ConfigureAwaitOptions.SuppressThrowing | ContinueOnCapturedContext, xUnit1030), future.cause() is CauseOf and
// isSuccess() is IsCompletedSuccessfully. The bootstrap API is asynchronous (BindAsync/ConnectAsync), new
// InetSocketAddress(0) is the wildcard IPEndPoint and ConnectAddress(...) connects to it through the loopback address as
// the JDK does (docs/handler-porting.md, "Socket tests"). CountDownLatch is CountdownEvent with CountDown(...),
// LinkedBlockingQueue is BlockingCollection (take() is Take, poll(timeout) TryTake), ConcurrentLinkedQueue is
// ConcurrentQueue, AtomicBoolean/AtomicReference are the Common types, Thread.sleep is Task.Delay and (long, TimeUnit)
// is TimeSpan. @Timeout(value, MILLISECONDS) is the Timeout of the [Fact], which xUnit applies to async tests. The
// anonymous handlers are named nested classes. DIRECT_EXECUTOR is not ported: only the CLR-inapplicable executor tests
// use it (see their notes below).
public class SslHandlerTest
{

    [Fact(Timeout = 5000)]
    public async Task TestNonApplicationDataFailureFailsQueuedWrites()
    {
        CountdownEvent writeLatch = new CountdownEvent(1);
        ConcurrentQueue<TaskCompletionSource> writesToFail = new ConcurrentQueue<TaskCompletionSource>();
        SslClientAuthenticationOptions engine = NewClientModeSSLEngine();
        SslHandler handler = new WriteLatchSslHandler(engine, writeLatch);
        EmbeddedChannel ch = new EmbeddedChannel(new FailWritesHandler(writesToFail), handler);

        try
        {
            CountdownEvent writeCauseLatch = new CountdownEvent(1);
            AtomicReference<Exception> failureRef = new AtomicReference<Exception>();
            _ = ch.WriteAsync(Unpooled.WrappedBuffer(new byte[]{1}), TestContext.Current.CancellationToken).ContinueWith(future =>
            {
                failureRef.CompareAndSet(null, CauseOf(future));
                CountDown(writeCauseLatch);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            writeLatch.Wait(TestContext.Current.CancellationToken);

            // Simulate failing the SslHandler non-application writes after there are applications writes queued.
            TaskCompletionSource promiseToFail;
            while (writesToFail.TryDequeue(out promiseToFail))
            {
                promiseToFail.TrySetException(new Exception("fake exception"));
            }

            writeCauseLatch.Wait(TestContext.Current.CancellationToken);
            Exception writeCause = failureRef.Get();
            Assert.NotNull(writeCause);
            Assert.IsAssignableFrom<AuthenticationException>(writeCause);
            Exception cause = CauseOf(handler.HandshakeFuture());
            Assert.NotNull(cause);
            Assert.IsAssignableFrom<AuthenticationException>(cause);
        }
        finally
        {
            Assert.False(ch.FinishAndReleaseAll());
        }
        await Task.CompletedTask;
    }

    private sealed class WriteLatchSslHandler : SslHandler
    {
        private readonly CountdownEvent writeLatch;

        internal WriteLatchSslHandler(SslClientAuthenticationOptions engine, CountdownEvent writeLatch) : base(engine)
        {
            this.writeLatch = writeLatch;
        }

        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            Task future = base.WriteAsync(ctx, msg, cancellationToken);
            CountDown(writeLatch);
            return future;
        }
    }

    // CLR: the ChannelPromise of write(...) is the returned Task: a readable buffer's write returns the Task of a
    // TaskCompletionSource that the test fails later, an empty buffer's write a completed Task, and any other message's
    // write a Task that never completes, as the Java handler leaves that promise alone.
    private sealed class FailWritesHandler : ChannelDuplexHandler
    {
        private readonly ConcurrentQueue<TaskCompletionSource> writesToFail;

        internal FailWritesHandler(ConcurrentQueue<TaskCompletionSource> writesToFail)
        {
            this.writesToFail = writesToFail;
        }

        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            Task future = new TaskCompletionSource().Task;
            if (msg is ByteBuf)
            {
                if (((ByteBuf) msg).IsReadable())
                {
                    TaskCompletionSource promise = new TaskCompletionSource();
                    writesToFail.Enqueue(promise);
                    future = promise.Task;
                }
                else
                {
                    future = Task.CompletedTask;
                }
            }
            ReferenceCountUtil.Release(msg);
            return future;
        }
    }

    [Fact]
    public void TestNoSslHandshakeEventWhenNoHandshake()
    {
        AtomicBoolean inActive = new AtomicBoolean(false);

        SslServerAuthenticationOptions engine = NewServerModeSSLEngine();
        EmbeddedChannel ch = new InActiveEmbeddedChannel(inActive, DefaultChannelId.NewInstance(),
                new CloseOnActiveHandler(), new InActiveSslHandler(engine, inActive), new ThrowOnHandshakeEventHandler());

        ch.Register();
        Assert.False(ch.FinishAndReleaseAll());
    }

    private sealed class CloseOnActiveHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            // Not forward the event to the SslHandler but just close the Channel.
            _ = ctx.CloseAsync();
        }
    }

    private sealed class InActiveSslHandler : SslHandler
    {
        private readonly AtomicBoolean inActive;

        internal InActiveSslHandler(SslServerAuthenticationOptions engine, AtomicBoolean inActive) : base(engine)
        {
            this.inActive = inActive;
        }

        public override void HandlerAdded(IChannelHandlerContext ctx)
        {
            // We want to override what Channel.isActive() will return as otherwise it will
            // return true and so trigger an handshake.
            inActive.Set(true);
            base.HandlerAdded(ctx);
            inActive.Set(false);
        }
    }

    private sealed class ThrowOnHandshakeEventHandler : ChannelInboundHandlerAdapter
    {
        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is SslHandshakeCompletionEvent)
            {
                throw ((SslHandshakeCompletionEvent) evt).Cause();
            }
        }
    }

    // CLR: the anonymous EmbeddedChannel subclass reads the captured flag in isActive(); the field is set from the
    // primary-constructor parameter in its initializer, which runs before the base constructor
    // (docs/handler-porting.md, "Embedded channels whose remote address is captured").
    private sealed class InActiveEmbeddedChannel(AtomicBoolean inActive, IChannelId channelId,
                                                 params IChannelHandler[] handlers)
        : EmbeddedChannel(channelId, false, false, handlers)
    {
        private readonly AtomicBoolean inActive = inActive;

        public override bool IsActive => !inActive.Get() && base.IsActive;
    }

#pragma warning disable xUnit1069 // The helpers wait with TestContext.Current.CancellationToken themselves.
    [Fact(Timeout = 3000)]
    public async Task TestClientHandshakeTimeout()
    {
        await Assert.ThrowsAsync<SslHandshakeTimeoutException>(() => TestHandshakeTimeout(true));
    }

    [Fact(Timeout = 3000)]
    public async Task TestServerHandshakeTimeout()
    {
        await Assert.ThrowsAsync<SslHandshakeTimeoutException>(() => TestHandshakeTimeout(false));
    }
#pragma warning restore xUnit1069

    // CLR: a server-mode SSLEngine of the default SSLContext has no key material and fails only when it must answer a
    // ClientHello, while SslStream checks that a server has a certificate when its authentication starts
    // (NotSupportedException "The server mode SSL must use a certificate with the associated private key", measured), so
    // the server options carry the certificate of CachedSelfSignedCertificate() as their ServerCertificateContext.
    private static SslServerAuthenticationOptions NewServerModeSSLEngine()
    {
        SslServerAuthenticationOptions engine = new SslServerAuthenticationOptions();
        // Set the mode before we try to do the handshake as otherwise it may throw an IllegalStateException.
        // See:
        //  - https://docs.oracle.com/javase/10/docs/api/javax/net/ssl/SSLEngine.html#beginHandshake()
        //  - https://mail.openjdk.java.net/pipermail/security-dev/2018-July/017715.html
        engine.ServerCertificateContext = CachedSelfSignedCertificate().KeyManagerFactory();
        return engine;
    }

    private static SslClientAuthenticationOptions NewClientModeSSLEngine()
    {
        SslClientAuthenticationOptions engine = new SslClientAuthenticationOptions();
        // Set the mode before we try to do the handshake as otherwise it may throw an IllegalStateException.
        // See:
        //  - https://docs.oracle.com/javase/10/docs/api/javax/net/ssl/SSLEngine.html#beginHandshake()
        //  - https://mail.openjdk.java.net/pipermail/security-dev/2018-July/017715.html
        return engine;
    }

    private static async Task TestHandshakeTimeout(bool client)
    {
        SslHandler handler = client ? new SslHandler(NewClientModeSSLEngine()) : new SslHandler(NewServerModeSSLEngine());
        handler.SetHandshakeTimeoutMillis(1);

        EmbeddedChannel ch = new EmbeddedChannel(handler);
        try
        {
            while (!handler.HandshakeFuture().IsCompleted)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
                // We need to run all pending tasks as the handshake timeout is scheduled on the EventLoop.
                ch.RunPendingTasks();
            }

            await handler.HandshakeFuture();
        }
        finally
        {
            ch.FinishAndReleaseAll();
        }
    }

    [Fact(Timeout = 5000)]
    public async Task TestHandshakeAndClosePromiseFailedOnRemoval()
    {
        SslClientAuthenticationOptions engine = NewClientModeSSLEngine();
        SslHandler handler = new SslHandler(engine);
        AtomicReference<Exception> handshakeRef = new AtomicReference<Exception>();
        AtomicReference<Exception> closeRef = new AtomicReference<Exception>();
        EmbeddedChannel ch = new EmbeddedChannel(handler, new CompletionCauseHandler(handshakeRef, closeRef));
        Assert.False(handler.HandshakeFuture().IsCompleted);
        Assert.False(handler.SslCloseFuture().IsCompleted);

        ch.Pipeline.Remove(handler);

        try
        {
            while (!handler.HandshakeFuture().IsCompleted || handshakeRef.Get() == null
                    || !handler.SslCloseFuture().IsCompleted || closeRef.Get() == null)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
                // Continue running all pending tasks until we notified for everything.
                ch.RunPendingTasks();
            }

            Assert.Same(CauseOf(handler.HandshakeFuture()), handshakeRef.Get());
            Assert.Same(CauseOf(handler.SslCloseFuture()), closeRef.Get());
        }
        finally
        {
            ch.FinishAndReleaseAll();
        }
    }

    private sealed class CompletionCauseHandler : ChannelInboundHandlerAdapter
    {
        private readonly AtomicReference<Exception> handshakeRef;
        private readonly AtomicReference<Exception> closeRef;

        internal CompletionCauseHandler(AtomicReference<Exception> handshakeRef, AtomicReference<Exception> closeRef)
        {
            this.handshakeRef = handshakeRef;
            this.closeRef = closeRef;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is SslHandshakeCompletionEvent)
            {
                handshakeRef.Set(((SslHandshakeCompletionEvent) evt).Cause());
            }
            else if (evt is SslCloseCompletionEvent)
            {
                closeRef.Set(((SslCloseCompletionEvent) evt).Cause());
            }
        }
    }

    [Fact]
    public void TestTruncatedPacket()
    {
        SslServerAuthenticationOptions engine = NewServerModeSSLEngine();
        EmbeddedChannel ch = new EmbeddedChannel(new SslHandler(engine));

        // Push the first part of a 5-byte handshake message.
        ch.WriteInbound(Unpooled.WrappedBuffer(new byte[]{22, 3, 1, 0, 5}));

        // Should decode nothing yet.
        Assert.Null(ch.ReadInbound());

        DecoderException e = Assert.Throws<DecoderException>(() =>
        {
            // Push the second part of the 5-byte handshake message.
            ch.WriteInbound(Unpooled.WrappedBuffer(new byte[]{2, 0, 0, 1, 0}));
        });
        // Be sure we cleanup the channel and release any pending messages that may have been generated because
        // of an alert.
        // See https://github.com/netty/netty/issues/6057.
        ch.FinishAndReleaseAll();

        // The pushed message is invalid, so it should raise an exception if it decoded the message correctly.
        Assert.IsAssignableFrom<AuthenticationException>(e.InnerException);
    }

    // CLR: ch.write(msg).get() is the awaited Task of WriteAsync, which rethrows the UnsupportedMessageTypeException
    // itself where Future.get() wraps the cause in an ExecutionException.
    [Fact]
    public async Task TestNonByteBufWriteIsReleased()
    {
        SslServerAuthenticationOptions engine = NewServerModeSSLEngine();
        EmbeddedChannel ch = new EmbeddedChannel(new SslHandler(engine));

        AbstractReferenceCounted referenceCounted = new NoopReferenceCounted();

        Exception e = await Assert.ThrowsAnyAsync<Exception>(() => ch.WriteAsync(referenceCounted, TestContext.Current.CancellationToken));
        Assert.IsAssignableFrom<UnsupportedMessageTypeException>(e);
        Assert.Equal(0, referenceCounted.RefCnt);
        Assert.True(ch.FinishAndReleaseAll());
    }

    private sealed class NoopReferenceCounted : AbstractReferenceCounted
    {
        public override IReferenceCounted Touch(object hint)
        {
            return this;
        }

        protected override void Deallocate()
        {
        }
    }

    [Fact]
    public void TestNonByteBufNotPassThrough()
    {
        SslServerAuthenticationOptions engine = NewServerModeSSLEngine();
        EmbeddedChannel ch = new EmbeddedChannel(new SslHandler(engine));

        Assert.Throws<UnsupportedMessageTypeException>(() => ch.WriteOutbound(new object()));
        ch.FinishAndReleaseAll();
    }

    // CLR: the ChannelPromise passed to writeAndFlush(buf, promise) is the Task that WriteAndFlushAsync returns. Closing
    // the channel during the handshake leaves no outbound message: the JDK engine wrote its user_canceled and close_notify
    // alerts there, while SslStream writes close_notify only on an authenticated stream (SslHandler.EngineCloseOutbound
    // calls ShutdownAsync once the handshake succeeded; measured: the outbound queue stays empty), so
    // FinishAndReleaseAll() returns false where Java asserts true.
    [Fact]
    public void TestIncompleteWriteDoesNotCompletePromisePrematurely()
    {
        SslServerAuthenticationOptions engine = NewServerModeSSLEngine();
        EmbeddedChannel ch = new EmbeddedChannel(new SslHandler(engine));

        ByteBuf buf = Unpooled.Buffer(10).WriteZero(10);
        Task promise = ch.WriteAndFlushAsync(buf, TestContext.Current.CancellationToken);
        Assert.False(promise.IsCompleted);
        Assert.False(ch.FinishAndReleaseAll());
        Assert.True(promise.IsCompleted);
        Assert.IsAssignableFrom<AuthenticationException>(CauseOf(promise));
    }

    // CLR: testReleaseSslEngine is CLR-inapplicable (OpenSsl.ensureAvailability() and the reference count of the OpenSSL
    // SSLEngine; netty-tcnative is library-not-ported).

    private sealed class TlsReadTest : ChannelOutboundHandlerAdapter
    {
        private volatile bool readIssued;

        public override void Read(IChannelHandlerContext ctx)
        {
            readIssued = true;
            base.Read(ctx);
        }

        public void Test(bool dropChannelActive)
        {
            SslClientAuthenticationOptions engine = NewClientModeSSLEngine();

            EmbeddedChannel ch = new EmbeddedChannel(false, false,
                    this,
                    new SslHandler(engine),
                    new DropChannelActiveHandler(dropChannelActive)
            );
            ch.Config.SetAutoRead(false);
            Assert.False(ch.Config.IsAutoRead());

            ch.Register();

            Assert.True(readIssued);
            readIssued = false;

            Assert.True(ch.WriteOutbound(Unpooled.EmptyBuffer));
            Assert.True(readIssued);
            Assert.True(ch.FinishAndReleaseAll());
       }
    }

    private sealed class DropChannelActiveHandler : ChannelInboundHandlerAdapter
    {
        private readonly bool dropChannelActive;

        internal DropChannelActiveHandler(bool dropChannelActive)
        {
            this.dropChannelActive = dropChannelActive;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            if (!dropChannelActive)
            {
                ctx.FireChannelActive();
            }
        }
    }

    [Fact]
    public void TestIssueReadAfterActiveWriteFlush()
    {
        // the handshake is initiated by channelActive
        new TlsReadTest().Test(false);
    }

    [Fact]
    public void TestIssueReadAfterWriteFlushActive()
    {
        // the handshake is initiated by flush
        new TlsReadTest().Test(true);
    }

    [Fact(Timeout = 30000)]
    public async Task TestRemoval()
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
        IChannel sc = null;
        IChannel cc = null;
        try
        {
            TaskCompletionSource clientPromise = NewPromise();
            Bootstrap bootstrap = new Bootstrap()
                    .Group(group)
                    .Channel<NioSocketChannel>()
                    .Handler(NewHandler(SslContextBuilder.ForClient().TrustManager(
                            InsecureTrustManagerFactory.INSTANCE).Build(), clientPromise));

            SelfSignedCertificateBundle ssc = CachedSelfSignedCertificate();
            TaskCompletionSource serverPromise = NewPromise();
            ServerBootstrap serverBootstrap = new ServerBootstrap()
                    .Group(group)
                    .Channel<NioServerSocketChannel>()
                    .ChildHandler(NewHandler(SslContextBuilder.ForServer(ssc.Certificate(), ssc.PrivateKey()).Build(),
                            serverPromise));
            sc = await serverBootstrap.BindAsync(new IPEndPoint(IPAddress.Any, 0), TestContext.Current.CancellationToken);
            cc = await bootstrap.ConnectAsync(ConnectAddress(sc.LocalAddress), TestContext.Current.CancellationToken);

            await serverPromise.Task;
            await clientPromise.Task;
        }
        finally
        {
            if (cc != null)
            {
                await cc.CloseAsync(TestContext.Current.CancellationToken);
            }
            if (sc != null)
            {
                await sc.CloseAsync(TestContext.Current.CancellationToken);
            }
            _ = group.ShutdownGracefullyAsync();
        }
    }

    private static IChannelHandler NewHandler(SslContext sslCtx, TaskCompletionSource promise)
    {
        return new RemovalInitializer(sslCtx, promise);
    }

    private sealed class RemovalInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslContext sslCtx;
        private readonly TaskCompletionSource promise;

        internal RemovalInitializer(SslContext sslCtx, TaskCompletionSource promise)
        {
            this.sslCtx = sslCtx;
            this.promise = promise;
        }

        protected override void InitChannel(IChannel ch)
        {
            SslHandler sslHandler = sslCtx.NewHandler(ch.Alloc());
            sslHandler.SetHandshakeTimeoutMillis(1000);
            ch.Pipeline.AddFirst(sslHandler);
            AddListener(sslHandler.HandshakeFuture(), ch.EventLoop, future =>
            {
                ch.Pipeline.Remove(sslHandler);

                // Schedule the close so removal has time to propagate exception if any.
                ch.EventLoop.Execute(() => _ = ch.CloseAsync());
            });

            ch.Pipeline.AddLast(new RemovalHandler(sslCtx, promise));
        }
    }

    private sealed class RemovalHandler : ChannelInboundHandlerAdapter
    {
        private readonly SslContext sslCtx;
        private readonly TaskCompletionSource promise;

        internal RemovalHandler(SslContext sslCtx, TaskCompletionSource promise)
        {
            this.sslCtx = sslCtx;
            this.promise = promise;
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (cause is CodecException)
            {
                cause = cause.InnerException;
            }
            if (cause is IllegalReferenceCountException)
            {
                promise.SetException(cause);
            }
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            promise.TrySetResult();
            ReferenceCountUtil.Release(sslCtx);
        }
    }

    [Fact]
    public void TestCloseFutureNotified()
    {
        SslServerAuthenticationOptions engine = NewServerModeSSLEngine();
        SslHandler handler = new SslHandler(engine);
        EmbeddedChannel ch = new EmbeddedChannel(handler);

        _ = ch.CloseAsync(TestContext.Current.CancellationToken);

        // When the channel is closed the SslHandler will write an empty buffer to the channel.
        ByteBuf buf = ch.ReadOutbound<ByteBuf>();
        Assert.False(buf.IsReadable());
        buf.Release();

        Assert.False(ch.FinishAndReleaseAll());

        Assert.IsAssignableFrom<ClosedChannelException>(CauseOf(handler.HandshakeFuture()));
        Assert.IsAssignableFrom<ClosedChannelException>(CauseOf(handler.SslCloseFuture()));
    }

    [Fact(Timeout = 5000)]
    public async Task TestEventsFired()
    {
        SslServerAuthenticationOptions engine = NewServerModeSSLEngine();
        BlockingCollection<SslCompletionEvent> events = new BlockingCollection<SslCompletionEvent>();
        EmbeddedChannel channel = new EmbeddedChannel(new SslHandler(engine), new CompletionEventCollector(events));
        Assert.True(events.Count == 0);
        Assert.True(channel.FinishAndReleaseAll());

        SslCompletionEvent evt = events.Take(TestContext.Current.CancellationToken);
        Assert.IsAssignableFrom<SslHandshakeCompletionEvent>(evt);
        Assert.IsAssignableFrom<ClosedChannelException>(evt.Cause());

        evt = events.Take(TestContext.Current.CancellationToken);
        Assert.IsAssignableFrom<SslCloseCompletionEvent>(evt);
        Assert.IsAssignableFrom<ClosedChannelException>(evt.Cause());
        Assert.True(events.Count == 0);
        await Task.CompletedTask;
    }

    private sealed class CompletionEventCollector : ChannelInboundHandlerAdapter
    {
        private readonly BlockingCollection<SslCompletionEvent> events;

        internal CompletionEventCollector(BlockingCollection<SslCompletionEvent> events)
        {
            this.events = events;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is SslCompletionEvent)
            {
                events.Add((SslCompletionEvent) evt);
            }
        }
    }

    [Fact(Timeout = 5000)]
    public async Task TestHandshakeFailBeforeWritePromise()
    {
        SelfSignedCertificateBundle ssc = CachedSelfSignedCertificate();
        SslContext sslServerCtx = SslContextBuilder.ForServer(ssc.Certificate(), ssc.PrivateKey()).Build();
        CountdownEvent latch = new CountdownEvent(2);
        CountdownEvent latch2 = new CountdownEvent(2);
        BlockingCollection<object> events = new BlockingCollection<object>();
        IChannel serverChannel = null;
        IChannel clientChannel = null;
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(LocalIoHandler.NewFactory());
        try
        {
            ServerBootstrap sb = new ServerBootstrap();
            sb.Group(group)
                .Channel<LocalServerChannel>()
                .ChildHandler(new FailBeforeWriteServerInitializer(sslServerCtx, events, latch, latch2));

            Bootstrap cb = new Bootstrap();
            cb.Group(group)
                .Channel<LocalChannel>()
                .Handler(new PlainWriteClientInitializer());

            serverChannel = await sb.BindAsync(new LocalAddress(typeof(SslHandlerTest)),
                    TestContext.Current.CancellationToken);
            clientChannel = await cb.ConnectAsync(serverChannel.LocalAddress, TestContext.Current.CancellationToken);
            latch.Wait(TestContext.Current.CancellationToken);

            SslCompletionEvent evt = (SslCompletionEvent) events.Take(TestContext.Current.CancellationToken);
            Assert.IsAssignableFrom<SslHandshakeCompletionEvent>(evt);
            Assert.IsAssignableFrom<AuthenticationException>(evt.Cause());

            Task future = (Task) events.Take(TestContext.Current.CancellationToken);
            Assert.IsAssignableFrom<AuthenticationException>(CauseOf(future));

            await serverChannel.CloseAsync(TestContext.Current.CancellationToken);
            serverChannel = null;
            await clientChannel.CloseAsync(TestContext.Current.CancellationToken);
            clientChannel = null;

            latch2.Wait(TestContext.Current.CancellationToken);
            evt = (SslCompletionEvent) events.Take(TestContext.Current.CancellationToken);
            Assert.IsAssignableFrom<SslCloseCompletionEvent>(evt);
            Assert.IsAssignableFrom<ClosedChannelException>(evt.Cause());
            Assert.True(events.Count == 0);
        }
        finally
        {
            if (serverChannel != null)
            {
                _ = serverChannel.CloseAsync(TestContext.Current.CancellationToken);
            }
            if (clientChannel != null)
            {
                _ = clientChannel.CloseAsync(TestContext.Current.CancellationToken);
            }
            _ = group.ShutdownGracefullyAsync();
            ReferenceCountUtil.Release(sslServerCtx);
        }
    }

    private sealed class FailBeforeWriteServerInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslContext sslServerCtx;
        private readonly BlockingCollection<object> events;
        private readonly CountdownEvent latch;
        private readonly CountdownEvent latch2;

        internal FailBeforeWriteServerInitializer(SslContext sslServerCtx, BlockingCollection<object> events,
                                                  CountdownEvent latch, CountdownEvent latch2)
        {
            this.sslServerCtx = sslServerCtx;
            this.events = events;
            this.latch = latch;
            this.latch2 = latch2;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Pipeline.AddLast(sslServerCtx.NewHandler(ch.Alloc()));
            ch.Pipeline.AddLast(new FailBeforeWriteServerHandler(events, latch, latch2));
            ch.Pipeline.AddLast(new SilenceExceptionHandler()
                    .RootCauseInstanceOf(typeof(NotSslRecordException)));
        }
    }

    private sealed class FailBeforeWriteServerHandler : ChannelInboundHandlerAdapter
    {
        private readonly BlockingCollection<object> events;
        private readonly CountdownEvent latch;
        private readonly CountdownEvent latch2;

        internal FailBeforeWriteServerHandler(BlockingCollection<object> events, CountdownEvent latch,
                                              CountdownEvent latch2)
        {
            this.events = events;
            this.latch = latch;
            this.latch2 = latch2;
        }

        // CLR: the ChannelFutureListener runs on Common's EventExecutorTaskScheduler over ctx.Executor, which runs it
        // at once on the event loop as DefaultPromise.notifyListeners() does (docs/handler-porting.md, "Listener
        // timing"), so the failed write is queued before the SslCloseCompletionEvent of the later channelInactive, as
        // in Java; a thread-pool continuation raced with that event (measured).
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            ByteBuf buf = ctx.Alloc().Buffer(10);
            buf.WriteZero(buf.Capacity);
            _ = ctx.WriteAndFlushAsync(buf).ContinueWith(future =>
            {
                events.Add(future);
                CountDown(latch);
            }, CancellationToken.None, TaskContinuationOptions.None,
                    new EventExecutorTaskScheduler(() => ctx.Executor));
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is SslCompletionEvent)
            {
                events.Add(evt);
                CountDown(latch);
                CountDown(latch2);
            }
        }
    }

    private sealed class PlainWriteClientInitializer : ChannelInitializer<IChannel>
    {
        protected override void InitChannel(IChannel ch)
        {
            ch.Pipeline.AddFirst(new PlainWriteOnActiveHandler());
        }
    }

    private sealed class PlainWriteOnActiveHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            ByteBuf buf = ctx.Alloc().Buffer(1000);
            buf.WriteZero(buf.Capacity);
            _ = ctx.WriteAndFlushAsync(buf);
        }
    }

    [Fact]
    public async Task WritingReadOnlyBufferDoesNotBreakAggregation()
    {
        SelfSignedCertificateBundle ssc = CachedSelfSignedCertificate();

        SslContext sslServerCtx = SslContextBuilder.ForServer(ssc.Certificate(), ssc.PrivateKey()).Build();

        SslContext sslClientCtx = SslContextBuilder.ForClient()
                .TrustManager(InsecureTrustManagerFactory.INSTANCE).Build();

        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
        IChannel sc = null;
        IChannel cc = null;
        CountdownEvent serverReceiveLatch = new CountdownEvent(1);
        try
        {
            int expectedBytes = 11;
            sc = await new ServerBootstrap()
                    .Group(group)
                    .Channel<NioServerSocketChannel>()
                    .ChildHandler(new ReadOnlyServerInitializer(sslServerCtx, expectedBytes, serverReceiveLatch))
                    .BindAsync(new IPEndPoint(IPAddress.Any, 0), TestContext.Current.CancellationToken);

            cc = await new Bootstrap()
                    .Group(group)
                    .Channel<NioSocketChannel>()
                    .Handler(new SslOnlyInitializer(sslClientCtx))
                    .ConnectAsync(ConnectAddress(sc.LocalAddress), TestContext.Current.CancellationToken);

            // We first write a ReadOnlyBuffer because SslHandler will attempt to take the first buffer and append to it
            // until there is no room, or the aggregation size threshold is exceeded. We want to verify that we don't
            // throw when a ReadOnlyBuffer is used and just verify that we don't aggregate in this case.
            ByteBuf firstBuffer = Unpooled.Buffer(10);
            firstBuffer.WriteByte(0);
            firstBuffer = firstBuffer.AsReadOnly();
            ByteBuf secondBuffer = Unpooled.Buffer(10);
            secondBuffer.WriteZero(secondBuffer.Capacity);
            _ = cc.WriteAsync(firstBuffer, TestContext.Current.CancellationToken);
            await cc.WriteAndFlushAsync(secondBuffer, TestContext.Current.CancellationToken);
            CountDown(serverReceiveLatch);
        }
        finally
        {
            if (cc != null)
            {
                await cc.CloseAsync(TestContext.Current.CancellationToken);
            }
            if (sc != null)
            {
                await sc.CloseAsync(TestContext.Current.CancellationToken);
            }
            _ = group.ShutdownGracefullyAsync();

            ReferenceCountUtil.Release(sslServerCtx);
            ReferenceCountUtil.Release(sslClientCtx);
        }
    }

    private sealed class ReadOnlyServerInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslContext sslServerCtx;
        private readonly int expectedBytes;
        private readonly CountdownEvent serverReceiveLatch;

        internal ReadOnlyServerInitializer(SslContext sslServerCtx, int expectedBytes,
                                           CountdownEvent serverReceiveLatch)
        {
            this.sslServerCtx = sslServerCtx;
            this.expectedBytes = expectedBytes;
            this.serverReceiveLatch = serverReceiveLatch;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Pipeline.AddLast(sslServerCtx.NewHandler(ch.Alloc()));
            ch.Pipeline.AddLast(new ReadBytesCounter(expectedBytes, serverReceiveLatch));
        }
    }

    private sealed class ReadBytesCounter : SimpleChannelInboundHandler<ByteBuf>
    {
        private readonly int expectedBytes;
        private readonly CountdownEvent serverReceiveLatch;
        private int readBytes;

        internal ReadBytesCounter(int expectedBytes, CountdownEvent serverReceiveLatch)
        {
            this.expectedBytes = expectedBytes;
            this.serverReceiveLatch = serverReceiveLatch;
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf msg)
        {
            readBytes += msg.ReadableBytes;
            if (readBytes >= expectedBytes)
            {
                CountDown(serverReceiveLatch);
            }
        }
    }

    private sealed class SslOnlyInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslContext sslCtx;

        internal SslOnlyInitializer(SslContext sslCtx)
        {
            this.sslCtx = sslCtx;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Pipeline.AddLast(sslCtx.NewHandler(ch.Alloc()));
        }
    }

    // CLR: cert.getKeyPair().getPrivate() is the bundle's key object (docs/pkitesting-porting.md: "A generated or given
    // key pair is the BCL key object") and getCertificatePath() the X509Certificate2 path; the trust manager factory of
    // the other self-signed bundle is its X509ChainPolicy (ToTrustManagerFactory).
    [Fact(Timeout = 10000)]
    public async Task TestCloseOnHandshakeFailure()
    {
        CertificateBuilder ca = new CertificateBuilder()
                .Subject("cn=localhost")
                .SetIsCertificateAuthority(true);

        X509Bundle cert = ca.BuildSelfSigned();
        SslContext sslServerCtx = SslContextBuilder.ForServer(
                (AsymmetricAlgorithm) cert.GetKeyPair(), cert.GetCertificatePath()).Build();
        SslContext sslClientCtx = SslContextBuilder.ForClient()
                .TrustManager(ca.BuildSelfSigned().ToTrustManagerFactory())
                .Build();

        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());
        IChannel sc = null;
        IChannel cc = null;
        try
        {
            LocalAddress address = new LocalAddress(GetType().Name + ".testCloseOnHandshakeFailure");
            ServerBootstrap sb = new ServerBootstrap()
                    .Group(group)
                    .Channel<LocalServerChannel>()
                    .ChildHandler(new CloseOnFailureServerInitializer(sslServerCtx));
            sc = await sb.BindAsync(address, TestContext.Current.CancellationToken);

            AtomicReference<SslHandler> sslHandlerRef = new AtomicReference<SslHandler>();
            Bootstrap b = new Bootstrap()
                    .Group(group)
                    .Channel<LocalChannel>()
                    .Handler(new CloseOnFailureClientInitializer(sslClientCtx, sslHandlerRef));
            cc = await b.ConnectAsync(sc.LocalAddress, TestContext.Current.CancellationToken);
            SslHandler handler = sslHandlerRef.Get();
            await ((Task) handler.HandshakeFuture()).ConfigureAwait(
                    ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            Assert.False(handler.HandshakeFuture().IsCompletedSuccessfully);

            await cc.CloseFuture;
        }
        finally
        {
            if (cc != null)
            {
                await cc.CloseAsync(TestContext.Current.CancellationToken);
            }
            if (sc != null)
            {
                await sc.CloseAsync(TestContext.Current.CancellationToken);
            }
            _ = group.ShutdownGracefullyAsync();

            ReferenceCountUtil.Release(sslServerCtx);
            ReferenceCountUtil.Release(sslClientCtx);
        }
    }

    private sealed class CloseOnFailureServerInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslContext sslServerCtx;

        internal CloseOnFailureServerInitializer(SslContext sslServerCtx)
        {
            this.sslServerCtx = sslServerCtx;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Pipeline.AddLast(sslServerCtx.NewHandler(ch.Alloc()));
            ch.Pipeline.AddLast(new SilenceExceptionHandler()
                    .RootCauseInstanceOf(typeof(AuthenticationException)));
        }
    }

    private sealed class CloseOnFailureClientInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslContext sslClientCtx;
        private readonly AtomicReference<SslHandler> sslHandlerRef;

        internal CloseOnFailureClientInitializer(SslContext sslClientCtx, AtomicReference<SslHandler> sslHandlerRef)
        {
            this.sslClientCtx = sslClientCtx;
            this.sslHandlerRef = sslHandlerRef;
        }

        protected override void InitChannel(IChannel ch)
        {
            SslHandler handler = sslClientCtx.NewHandler(ch.Alloc());

            // We propagate the SslHandler via an AtomicReference to the outer-scope as using
            // pipeline.get(...) may return null if the pipeline was teared down by the time we call it.
            // This will happen if the channel was closed in the meantime.
            sslHandlerRef.Set(handler);
            ch.Pipeline.AddLast(handler);
            ch.Pipeline.AddLast(new SilenceExceptionHandler()
                    .RootCauseInstanceOf(typeof(CryptographicException)));
        }
    }

    // CLR: testOutboundClosedAfterChannelInactive is CLR-inapplicable: it asserts SSLEngine.isOutboundDone() of an engine
    // from SslContext.newEngine(...), and neither exists on SslStream (no outbound-done member; newEngine is not ported,
    // owner decision 2026-11-24).

    // CLR: SslProtocols.SSL_v3 is a @Deprecated constant that is not ported (its @deprecated text names TLS_v1_2/TLS_v1_3),
    // so its text "SSLv3" is passed. The connect listener that writes "before fireChannelActive is called" runs at once
    // on the event loop in Java, because DefaultPromise notifies on its executor and fulfillConnectPromise completes the
    // promise before it fires channelActive; the Transport's connect Task runs its continuations asynchronously
    // (docs/handler-porting.md, "Listener timing"), so a first handler (WriteBeforeActiveHandler) writes from
    // channelActive before it passes the event on, which also hands the write to SslHandler before its channelActive.
    // AssertionError is UnreachableException (docs/handler-porting.md, Exceptions).
    [Fact(Timeout = 10000)]
    public async Task TestHandshakeFailedByWriteBeforeChannelActive()
    {
        SslContext sslClientCtx = SslContextBuilder.ForClient()
                                                         .Protocols("SSLv3")
                                                         .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                                                         .SslProvider(SslProvider.JDK).Build();

        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
        IChannel sc = null;
        IChannel cc = null;
        CountdownEvent activeLatch = new CountdownEvent(1);
        AtomicReference<UnreachableException> errorRef = new AtomicReference<UnreachableException>();
        SslHandler sslHandler = sslClientCtx.NewHandler(UnpooledByteBufAllocator.Default);
        try
        {
            sc = await new ServerBootstrap()
                    .Group(group)
                    .Channel<NioServerSocketChannel>()
                    .ChildHandler(new ChannelInboundHandlerAdapter())
                    .BindAsync(new IPEndPoint(IPAddress.Any, 0), TestContext.Current.CancellationToken);

            cc = await new Bootstrap()
                    .Group(group)
                    .Channel<NioSocketChannel>()
                    .Handler(new WriteBeforeActiveInitializer(sslHandler, errorRef, activeLatch))
                    .ConnectAsync(ConnectAddress(sc.LocalAddress), TestContext.Current.CancellationToken);

            // Ensure there is no AssertionError thrown by having the handshake failed by the writeAndFlush(...) before
            // channelActive(...) was called. Let's first wait for the activeLatch countdown to happen and after this
            // check if we saw and AssertionError (even if we timed out waiting).
            activeLatch.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            UnreachableException error = errorRef.Get();
            if (error != null)
            {
                throw error;
            }
            await ((Task) sslHandler.HandshakeFuture()).ConfigureAwait(
                    ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            Assert.IsAssignableFrom<AuthenticationException>(CauseOf(sslHandler.HandshakeFuture()));
        }
        finally
        {
            if (cc != null)
            {
                await cc.CloseAsync(TestContext.Current.CancellationToken);
            }
            if (sc != null)
            {
                await sc.CloseAsync(TestContext.Current.CancellationToken);
            }
            _ = group.ShutdownGracefullyAsync();

            ReferenceCountUtil.Release(sslClientCtx);
        }
    }

    private sealed class WriteBeforeActiveInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslHandler sslHandler;
        private readonly AtomicReference<UnreachableException> errorRef;
        private readonly CountdownEvent activeLatch;

        internal WriteBeforeActiveInitializer(SslHandler sslHandler, AtomicReference<UnreachableException> errorRef,
                                              CountdownEvent activeLatch)
        {
            this.sslHandler = sslHandler;
            this.errorRef = errorRef;
            this.activeLatch = activeLatch;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Pipeline.AddLast(sslHandler);
            ch.Pipeline.AddLast(new ActiveLatchHandler(errorRef, activeLatch));
            ch.Pipeline.AddFirst(new WriteBeforeActiveHandler());
        }
    }

    private sealed class ActiveLatchHandler : ChannelInboundHandlerAdapter
    {
        private readonly AtomicReference<UnreachableException> errorRef;
        private readonly CountdownEvent activeLatch;

        internal ActiveLatchHandler(AtomicReference<UnreachableException> errorRef, CountdownEvent activeLatch)
        {
            this.errorRef = errorRef;
            this.activeLatch = activeLatch;
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (cause is UnreachableException)
            {
                errorRef.Set((UnreachableException) cause);
            }
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            CountDown(activeLatch);
        }
    }

    // CLR-only helper: the connect listener of the source (see TestHandshakeFailedByWriteBeforeChannelActive).
    private sealed class WriteBeforeActiveHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            // Write something to trigger the handshake before fireChannelActive is called.
            _ = ctx.Channel.WriteAndFlushAsync(Unpooled.WrappedBuffer(new byte [] { 1, 2, 3, 4 }));
            ctx.FireChannelActive();
        }
    }

#pragma warning disable xUnit1069 // The helper waits with TestContext.Current.CancellationToken itself.
    [Fact(Timeout = 10000)]
    public async Task TestHandshakeTimeoutFlushStartsHandshake()
    {
        await TestHandshakeTimeout0(false);
    }

    [Fact(Timeout = 10000)]
    public async Task TestHandshakeTimeoutStartTLS()
    {
        await TestHandshakeTimeout0(true);
    }
#pragma warning restore xUnit1069

    // CLR: the connect listener of the !startTls case is the first handler WriteBeforeActiveHandler, as in
    // TestHandshakeFailedByWriteBeforeChannelActive.
    private static async Task TestHandshakeTimeout0(bool startTls)
    {
        SslContext sslClientCtx = SslContextBuilder.ForClient()
                                                         .StartTls(true)
                                                         .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                                                         .SslProvider(SslProvider.JDK).Build();

        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
        IChannel sc = null;
        IChannel cc = null;
        SslHandler sslHandler = sslClientCtx.NewHandler(UnpooledByteBufAllocator.Default);
        sslHandler.SetHandshakeTimeout(TimeSpan.FromMilliseconds(500));

        try
        {
            sc = await new ServerBootstrap()
                    .Group(group)
                    .Channel<NioServerSocketChannel>()
                    .ChildHandler(new ChannelInboundHandlerAdapter())
                    .BindAsync(new IPEndPoint(IPAddress.Any, 0), TestContext.Current.CancellationToken);

            Task<IChannel> future = new Bootstrap()
                    .Group(group)
                    .Channel<NioSocketChannel>()
                    .Handler(new StartTlsInitializer(sslHandler, startTls))
                    .ConnectAsync(ConnectAddress(sc.LocalAddress), TestContext.Current.CancellationToken);
            cc = await future;

            await ((Task) sslHandler.HandshakeFuture()).ConfigureAwait(
                    ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            Exception cause = CauseOf(sslHandler.HandshakeFuture());
            Assert.IsAssignableFrom<AuthenticationException>(cause);
            Assert.Contains("timed out", cause.Message);
        }
        finally
        {
            if (cc != null)
            {
                await cc.CloseAsync(TestContext.Current.CancellationToken);
            }
            if (sc != null)
            {
                await sc.CloseAsync(TestContext.Current.CancellationToken);
            }
            _ = group.ShutdownGracefullyAsync();
            ReferenceCountUtil.Release(sslClientCtx);
        }
    }

    private sealed class StartTlsInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslHandler sslHandler;
        private readonly bool startTls;

        internal StartTlsInitializer(SslHandler sslHandler, bool startTls)
        {
            this.sslHandler = sslHandler;
            this.startTls = startTls;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Pipeline.AddLast(sslHandler);
            if (startTls)
            {
                ch.Pipeline.AddLast(new WriteOnActiveHandler());
            }
            else
            {
                ch.Pipeline.AddFirst(new WriteBeforeActiveHandler());
            }
        }
    }

    private sealed class WriteOnActiveHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            _ = ctx.WriteAndFlushAsync(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4 }));
        }
    }

    // CLR: the tests of SslHandler(SSLEngine, Executor) and SslContext.newHandler(alloc, Executor) are CLR-inapplicable:
    // SslStream has no delegated tasks (SSLEngine.getDelegatedTask), so neither constructor nor newHandler overload is
    // ported (decisions of the ssl-handler and ssl-context waves), and the OpenSSL variants also need netty-tcnative
    // (library-not-ported): testHandshakeWithExecutorThatExecuteDirectlyJDK, testHandshakeWithImmediateExecutorJDK,
    // testHandshakeWithImmediateEventExecutorJDK, testHandshakeWithExecutorJDK, their OpenSsl and MTLS variants (16
    // tests, with the DIRECT_EXECUTOR field and the DelayingExecutor helper), testClientHandshakeTimeoutBecauseExecutorNotExecute
    // and testServerHandshakeTimeoutBecauseExecutorNotExecute. The original comment of the executors of the last two:
    // Do nothing to simulate slow execution.

    // CLR: testSessionTicketsWithTLSv12, testSessionTicketsWithTLSv13, testSessionTicketsWithTLSv12AndNoKey and
    // testSessionTicketsWithTLSv13AndNoKey are CLR-inapplicable: they configure the OpenSSL session context and ticket
    // keys (OpenSslSessionContext, OpenSslSessionTicketKey, ReferenceCountedOpenSslEngine.isSessionReused) of
    // netty-tcnative, which is library-not-ported, and SslStream has no session cache settings or resumption facts
    // (docs/handler-ssl-design.md). The original comments of their helpers:
    // Explicit enable session cache as it's disabled by default atm.
    // This test only works for non TLSv1.3 as TLSv1.3 will establish sessions after
    // the handshake is done.
    // See https://www.openssl.org/docs/man1.1.1/man3/SSL_CTX_sess_set_get_cb.html
    // First should not re-use the session

    // CLR: the X509ExtendedTrustManager is a RemoteCertificateValidationCallback (ssl design decision 3): its six
    // checkClientTrusted/checkServerTrusted overloads, which all call failVerification(), are the one FailVerification
    // callback, and getAcceptedIssuers() has no counterpart. A trust manager rejects a certificate by throwing
    // CertificateException, which JSSE answers with a fatal alert; a validation callback rejects it by returning false,
    // after which SslStream creates the fatal alert (CreateFatalHandshakeAlertToken), while an exception thrown by the
    // callback propagates without one ("No try/catch wraps this call",
    // https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Net.Security/src/System/Net/Security/SslStream.Protocol.cs),
    // so FailVerification returns false. The client's root cause is then SslStream's AuthenticationException instead of
    // the CertificateException.
    [Fact(Timeout = 10000)]
    public async Task TestHandshakeFailureOnlyFireExceptionOnce()
    {
        SslContext sslClientCtx = SslContextBuilder.ForClient()
                .TrustManager(FailVerification)
                .SslProvider(SslProvider.JDK).Build();

        SelfSignedCertificateBundle cert = CachedSelfSignedCertificate();
        SslContext sslServerCtx = SslContextBuilder.ForServer(cert.Key(), cert.Cert())
                .SslProvider(SslProvider.JDK).Build();

        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
        IChannel sc = null;
        SslHandler clientSslHandler = sslClientCtx.NewHandler(UnpooledByteBufAllocator.Default);
        SslHandler serverSslHandler = sslServerCtx.NewHandler(UnpooledByteBufAllocator.Default);

        try
        {
            object terminalEvent = new object();
            BlockingCollection<object> errorQueue = new BlockingCollection<object>();
            sc = await new ServerBootstrap()
                    .Group(group)
                    .Channel<NioServerSocketChannel>()
                    .ChildHandler(new ErrorQueueServerInitializer(serverSslHandler, errorQueue, terminalEvent))
                    .BindAsync(new IPEndPoint(IPAddress.Any, 0), TestContext.Current.CancellationToken);
            Task<IChannel> future = new Bootstrap()
                    .Group(group)
                    .Channel<NioSocketChannel>()
                    .Handler(new SilencedClientInitializer(clientSslHandler, typeof(AuthenticationException)))
                    .ConnectAsync(ConnectAddress(sc.LocalAddress), TestContext.Current.CancellationToken);
            IChannel cc = await future;
            _ = clientSslHandler.HandshakeFuture().ContinueWith(f => cc.CloseAsync(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            await ((Task) clientSslHandler.HandshakeFuture()).ConfigureAwait(
                    ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            Assert.False(clientSslHandler.HandshakeFuture().IsCompletedSuccessfully);
            await ((Task) serverSslHandler.HandshakeFuture()).ConfigureAwait(
                    ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            // CLR: SslStream validates the server certificate after its TLS handshake completed, so the server's
            // handshake has succeeded when the client rejects the certificate (measured; the ssl-handler wave measured
            // the same for SslStream's default validation), where the JSSE client fails the handshake before the
            // server's completes. The client's fatal alert then fails the server's first read, which is the single
            // exception below.
            Assert.True(serverSslHandler.HandshakeFuture().IsCompletedSuccessfully);

            object error = errorQueue.Take(TestContext.Current.CancellationToken);
            Assert.IsAssignableFrom<DecoderException>(error);
            Assert.IsAssignableFrom<AuthenticationException>(((Exception) error).InnerException);
            object terminal = errorQueue.Take(TestContext.Current.CancellationToken);
            Assert.Same(terminalEvent, terminal);

            Assert.False(errorQueue.TryTake(out _, 1, TestContext.Current.CancellationToken));
        }
        finally
        {
            if (sc != null)
            {
                await sc.CloseAsync(TestContext.Current.CancellationToken);
            }
            _ = group.ShutdownGracefullyAsync();
        }
    }

    private static bool FailVerification(object sender, X509Certificate certificate, X509Chain chain,
                                         SslPolicyErrors sslPolicyErrors)
    {
        return false;
    }

    private sealed class ErrorQueueServerInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslHandler serverSslHandler;
        private readonly BlockingCollection<object> errorQueue;
        private readonly object terminalEvent;

        internal ErrorQueueServerInitializer(SslHandler serverSslHandler, BlockingCollection<object> errorQueue,
                                             object terminalEvent)
        {
            this.serverSslHandler = serverSslHandler;
            this.errorQueue = errorQueue;
            this.terminalEvent = terminalEvent;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Pipeline.AddLast(serverSslHandler);
            ch.Pipeline.AddLast(new ErrorQueueHandler(errorQueue, terminalEvent));
        }
    }

    private sealed class ErrorQueueHandler : ChannelInboundHandlerAdapter
    {
        private readonly BlockingCollection<object> errorQueue;
        private readonly object terminalEvent;

        internal ErrorQueueHandler(BlockingCollection<object> errorQueue, object terminalEvent)
        {
            this.errorQueue = errorQueue;
            this.terminalEvent = terminalEvent;
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            errorQueue.Add(cause);
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            errorQueue.Add(terminalEvent);
        }
    }

    private sealed class SilencedClientInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslHandler clientSslHandler;
        private readonly Type rootCause;

        internal SilencedClientInitializer(SslHandler clientSslHandler, Type rootCause)
        {
            this.clientSslHandler = clientSslHandler;
            this.rootCause = rootCause;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Pipeline.AddLast(clientSslHandler);
            ch.Pipeline.AddLast(new SilenceExceptionHandler()
                    .RootCauseInstanceOf(rootCause));
        }
    }

    [Fact]
    public async Task TestHandshakeFailureCipherMissmatchTLSv12Jdk()
    {
        await TestHandshakeFailureCipherMissmatch(SslProvider.JDK, false);
    }

    [Fact]
    public async Task TestHandshakeFailureCipherMissmatchTLSv13Jdk()
    {
        Assert.SkipUnless(SslProvider.IsTlsv13Supported(SslProvider.JDK), "TLSv1.3 is not supported");
        await TestHandshakeFailureCipherMissmatch(SslProvider.JDK, true);
    }

    // CLR: testHandshakeFailureCipherMissmatchTLSv12OpenSsl and testHandshakeFailureCipherMissmatchTLSv13OpenSsl are
    // CLR-inapplicable: they need OpenSsl (netty-tcnative), which is library-not-ported.

    // CLR: the cipher suites become a CipherSuitesPolicy, which SslStream does not support on Windows: NewHandler throws
    // PlatformNotSupportedException there (docs/handler-ssl-design.md, "No cipher-suite selection on Windows"), so the
    // test is skipped on Windows and runs where CipherSuitesPolicy exists (Linux, macOS). Collections.singleton(x) is a
    // one-element array; getCause() is InnerException.
    private static async Task TestHandshakeFailureCipherMissmatch(SslProvider provider, bool tls13)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(),
                "CipherSuitesPolicy is not supported on Windows (SslContextBuilder.ciphers cannot be applied)");
        string clientCipher;
        string serverCipher;
        string protocol;

        if (tls13)
        {
            clientCipher = "TLS_AES_128_GCM_SHA256";
            serverCipher = "TLS_AES_256_GCM_SHA384";
            protocol = Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_3;
        }
        else
        {
            clientCipher = "TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256";
            serverCipher = "TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384";
            protocol = Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_2;
        }
        SslContext sslClientCtx = SslContextBuilder.ForClient()
                .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                .Protocols(protocol)
                .Ciphers(new[] { clientCipher })
                .SslProvider(provider).Build();

        SelfSignedCertificateBundle cert = CachedSelfSignedCertificate();
        SslContext sslServerCtx = SslContextBuilder.ForServer(cert.Key(), cert.Cert())
                .Protocols(protocol)
                .Ciphers(new[] { serverCipher })
                .SslProvider(provider).Build();

        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
        IChannel sc = null;
        IChannel cc = null;
        SslHandler clientSslHandler = sslClientCtx.NewHandler(UnpooledByteBufAllocator.Default);
        SslHandler serverSslHandler = sslServerCtx.NewHandler(UnpooledByteBufAllocator.Default);

        AtomicReference<SslHandshakeCompletionEvent> clientEvent =
                new AtomicReference<SslHandshakeCompletionEvent>();
        AtomicReference<SslHandshakeCompletionEvent> serverEvent =
                new AtomicReference<SslHandshakeCompletionEvent>();
        try
        {
            sc = await new ServerBootstrap()
                    .Group(group)
                    .Channel<NioServerSocketChannel>()
                    .ChildHandler(new SslEventInitializer(serverSslHandler, serverEvent))
                    .BindAsync(new IPEndPoint(IPAddress.Any, 0), TestContext.Current.CancellationToken);

            Task<IChannel> future = new Bootstrap()
                    .Group(group)
                    .Channel<NioSocketChannel>()
                    .Handler(new SslEventInitializer(clientSslHandler, clientEvent))
                    .ConnectAsync(ConnectAddress(sc.LocalAddress), TestContext.Current.CancellationToken);
            cc = await future;

            await ((Task) clientSslHandler.HandshakeFuture()).ConfigureAwait(
                    ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            Exception clientCause = CauseOf(clientSslHandler.HandshakeFuture());
            Assert.IsAssignableFrom<AuthenticationException>(clientCause);
            Assert.Null(clientCause.InnerException);
            await ((Task) serverSslHandler.HandshakeFuture()).ConfigureAwait(
                    ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            Exception serverCause = CauseOf(serverSslHandler.HandshakeFuture());
            Assert.IsAssignableFrom<AuthenticationException>(serverCause);
            Assert.Null(serverCause.InnerException);
            await cc.CloseAsync(TestContext.Current.CancellationToken);
            await sc.CloseAsync(TestContext.Current.CancellationToken);

            Exception eventClientCause = clientEvent.Get().Cause();
            Assert.IsAssignableFrom<AuthenticationException>(eventClientCause);
            Assert.Null(eventClientCause.InnerException);
            Exception serverEventCause = serverEvent.Get().Cause();

            Assert.IsAssignableFrom<AuthenticationException>(serverEventCause);
            Assert.Null(serverEventCause.InnerException);
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
            ReferenceCountUtil.Release(sslServerCtx);
            ReferenceCountUtil.Release(sslClientCtx);
        }
    }

    // CLR: the local class SslEventHandler of testHandshakeFailureCipherMissmatch is a nested class.
    private sealed class SslEventHandler : ChannelInboundHandlerAdapter
    {
        private readonly AtomicReference<SslHandshakeCompletionEvent> @ref;

        internal SslEventHandler(AtomicReference<SslHandshakeCompletionEvent> @ref)
        {
            this.@ref = @ref;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is SslHandshakeCompletionEvent)
            {
                @ref.Set((SslHandshakeCompletionEvent) evt);
            }
            base.UserEventTriggered(ctx, evt);
        }
    }

    private sealed class SslEventInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslHandler sslHandler;
        private readonly AtomicReference<SslHandshakeCompletionEvent> @ref;

        internal SslEventInitializer(SslHandler sslHandler, AtomicReference<SslHandshakeCompletionEvent> @ref)
        {
            this.sslHandler = sslHandler;
            this.@ref = @ref;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Pipeline.AddLast(sslHandler);
            ch.Pipeline.AddLast(new SslEventHandler(@ref));
            ch.Pipeline.AddLast(new SilenceExceptionHandler()
                    .RootCauseInstanceOf(typeof(AuthenticationException)));
        }
    }

    [Fact]
    public void TestIncorrectLength()
    {
        SelfSignedCertificateBundle cert = CachedSelfSignedCertificate();
        EmbeddedChannel channel = new EmbeddedChannel();
        channel.Pipeline.AddLast(
                SslContextBuilder.ForServer(cert.Key(), cert.Cert())
                        .SslProvider(SslProvider.JDK)
                        .Build()
                        .NewHandler(channel.Alloc()));
        ByteBuf buf = channel.Alloc().Buffer(5);
        buf.WriteByte(0x0);
        buf.WriteByte(0x1);
        buf.WriteByte(0xfe);
        buf.WriteByte(0x87);
        buf.WriteByte(0x2);
        DecoderException e = Assert.Throws<DecoderException>(() =>
        {
            channel.WriteInbound(buf);
        });
        Assert.IsAssignableFrom<NotSslRecordException>(e.InnerException);
        Assert.True(channel.FinishAndReleaseAll());
    }

    [Fact]
    public async Task TestSslCompletionEventsTls12JDK()
    {
        await TestSslCompletionEvents(SslProvider.JDK, Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_2, true);
        await TestSslCompletionEvents(SslProvider.JDK, Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_2, false);
    }

    // CLR: testSslCompletionEventsTls12Openssl and testSslCompletionEventsTls13Openssl are CLR-inapplicable: they need
    // OpenSsl (netty-tcnative), which is library-not-ported.

    [Fact]
    public async Task TestSslCompletionEventsTls13JDK()
    {
        Assert.SkipUnless(SslProvider.IsTlsv13Supported(SslProvider.JDK), "TLSv1.3 is not supported");
        await TestSslCompletionEvents(SslProvider.JDK, Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_3, true);
        await TestSslCompletionEvents(SslProvider.JDK, Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_3, false);
    }

    private async Task TestSslCompletionEvents(SslProvider provider, string protocol, bool clientClose)
    {
        SslContext sslClientCtx = SslContextBuilder.ForClient()
                .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                .Protocols(protocol)
                .SslProvider(provider).Build();

        SelfSignedCertificateBundle cert = CachedSelfSignedCertificate();
        SslContext sslServerCtx = SslContextBuilder.ForServer(cert.Key(), cert.Cert())
                .Protocols(protocol)
                .SslProvider(provider).Build();

        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        BlockingCollection<IChannel> acceptedChannels =
                new BlockingCollection<IChannel>();

        BlockingCollection<SslHandshakeCompletionEvent> serverHandshakeCompletionEvents =
                new BlockingCollection<SslHandshakeCompletionEvent>();

        BlockingCollection<SslHandshakeCompletionEvent> clientHandshakeCompletionEvents =
                new BlockingCollection<SslHandshakeCompletionEvent>();

        BlockingCollection<SslCloseCompletionEvent> serverCloseCompletionEvents =
                new BlockingCollection<SslCloseCompletionEvent>();

        BlockingCollection<SslCloseCompletionEvent> clientCloseCompletionEvents =
                new BlockingCollection<SslCloseCompletionEvent>();
        try
        {
            IChannel sc = await new ServerBootstrap()
                    .Group(group)
                    .Channel<NioServerSocketChannel>()
                    .ChildHandler(new CompletionEventsServerInitializer(sslServerCtx, protocol, acceptedChannels,
                            serverHandshakeCompletionEvents, serverCloseCompletionEvents))
                    .BindAsync(new IPEndPoint(IPAddress.Any, 0), TestContext.Current.CancellationToken);

            Bootstrap bs = new Bootstrap()
                    .Group(group)
                    .Channel<NioSocketChannel>()
                    .Handler(new CompletionEventsClientInitializer(sslClientCtx, protocol,
                            clientHandshakeCompletionEvents, clientCloseCompletionEvents))
                    .RemoteAddress(ConnectAddress(sc.LocalAddress));

            IChannel cc1 = await bs.ConnectAsync(TestContext.Current.CancellationToken);
            IChannel cc2 = await bs.ConnectAsync(TestContext.Current.CancellationToken);

            // We expect 4 events as we have 2 connections and for each connection there should be one event
            // on the server-side and one on the client-side.
            for (int i = 0; i < 2; i++)
            {
                SslHandshakeCompletionEvent @event = clientHandshakeCompletionEvents.Take(
                        TestContext.Current.CancellationToken);
                Assert.True(@event.IsSuccess());
            }
            for (int i = 0; i < 2; i++)
            {
                SslHandshakeCompletionEvent @event = serverHandshakeCompletionEvents.Take(
                        TestContext.Current.CancellationToken);
                Assert.True(@event.IsSuccess());
            }

            Assert.Empty(clientCloseCompletionEvents);
            Assert.Empty(serverCloseCompletionEvents);

            if (clientClose)
            {
                await cc1.CloseAsync(TestContext.Current.CancellationToken);
                await cc2.CloseAsync(TestContext.Current.CancellationToken);

                await acceptedChannels.Take(TestContext.Current.CancellationToken).CloseFuture;
                await acceptedChannels.Take(TestContext.Current.CancellationToken).CloseFuture;
            }
            else
            {
                await acceptedChannels.Take(TestContext.Current.CancellationToken)
                        .CloseAsync(TestContext.Current.CancellationToken);
                await acceptedChannels.Take(TestContext.Current.CancellationToken)
                        .CloseAsync(TestContext.Current.CancellationToken);

                await cc1.CloseFuture;
                await cc2.CloseFuture;
            }

            // We expect 4 events as we have 2 connections and for each connection there should be one event
            // on the server-side and one on the client-side.
            for (int i = 0; i < 2; i++)
            {
                SslCloseCompletionEvent @event = clientCloseCompletionEvents.Take(
                        TestContext.Current.CancellationToken);
                if (clientClose)
                {
                    // When we use TLSv1.3 the remote peer is not required to send a close_notify as response.
                    // See:
                    //  - https://datatracker.ietf.org/doc/html/rfc8446#section-6.1
                    //  - https://bugs.openjdk.org/browse/JDK-8208526
                    if (Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_3.Equals(protocol))
                    {
                        Assert.NotNull(@event);
                    }
                    else
                    {
                        Assert.True(@event.IsSuccess());
                    }
                }
                else
                {
                    Assert.True(@event.IsSuccess());
                }
            }
            for (int i = 0; i < 2; i++)
            {
                SslCloseCompletionEvent @event = serverCloseCompletionEvents.Take(
                        TestContext.Current.CancellationToken);

                if (clientClose)
                {
                    Assert.True(@event.IsSuccess());
                }
                else
                {
                    // When we use TLSv1.3 the remote peer is not required to send a close_notify as response.
                    // See:
                    //  - https://datatracker.ietf.org/doc/html/rfc8446#section-6.1
                    //  - https://bugs.openjdk.org/browse/JDK-8208526
                    if (Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_3.Equals(protocol))
                    {
                        Assert.NotNull(@event);
                    }
                    else
                    {
                        Assert.True(@event.IsSuccess());
                    }
                }
            }

            await sc.CloseAsync(TestContext.Current.CancellationToken);
            Assert.Empty(clientHandshakeCompletionEvents);
            Assert.Empty(serverHandshakeCompletionEvents);
            Assert.Empty(clientCloseCompletionEvents);
            Assert.Empty(serverCloseCompletionEvents);
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
            ReferenceCountUtil.Release(sslClientCtx);
            ReferenceCountUtil.Release(sslServerCtx);
        }
    }

    private sealed class CompletionEventsServerInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslContext sslServerCtx;
        private readonly string protocol;
        private readonly BlockingCollection<IChannel> acceptedChannels;
        private readonly BlockingCollection<SslHandshakeCompletionEvent> serverHandshakeCompletionEvents;
        private readonly BlockingCollection<SslCloseCompletionEvent> serverCloseCompletionEvents;

        internal CompletionEventsServerInitializer(
                SslContext sslServerCtx, string protocol, BlockingCollection<IChannel> acceptedChannels,
                BlockingCollection<SslHandshakeCompletionEvent> serverHandshakeCompletionEvents,
                BlockingCollection<SslCloseCompletionEvent> serverCloseCompletionEvents)
        {
            this.sslServerCtx = sslServerCtx;
            this.protocol = protocol;
            this.acceptedChannels = acceptedChannels;
            this.serverHandshakeCompletionEvents = serverHandshakeCompletionEvents;
            this.serverCloseCompletionEvents = serverCloseCompletionEvents;
        }

        protected override void InitChannel(IChannel ch)
        {
            acceptedChannels.Add(ch);
            SslHandler handler = sslServerCtx.NewHandler(ch.Alloc());
            if (!Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_3.Equals(protocol))
            {
                handler.SetCloseNotifyReadTimeout(TimeSpan.FromSeconds(5));
            }
            ch.Pipeline.AddLast(handler);
            ch.Pipeline.AddLast(new SslCompletionEventHandler(
                    serverHandshakeCompletionEvents, serverCloseCompletionEvents));
        }
    }

    private sealed class CompletionEventsClientInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslContext sslClientCtx;
        private readonly string protocol;
        private readonly BlockingCollection<SslHandshakeCompletionEvent> clientHandshakeCompletionEvents;
        private readonly BlockingCollection<SslCloseCompletionEvent> clientCloseCompletionEvents;

        internal CompletionEventsClientInitializer(
                SslContext sslClientCtx, string protocol,
                BlockingCollection<SslHandshakeCompletionEvent> clientHandshakeCompletionEvents,
                BlockingCollection<SslCloseCompletionEvent> clientCloseCompletionEvents)
        {
            this.sslClientCtx = sslClientCtx;
            this.protocol = protocol;
            this.clientHandshakeCompletionEvents = clientHandshakeCompletionEvents;
            this.clientCloseCompletionEvents = clientCloseCompletionEvents;
        }

        protected override void InitChannel(IChannel ch)
        {
            SslHandler handler = sslClientCtx.NewHandler(
                    ch.Alloc(), "netty.io", 9999);
            if (!Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_3.Equals(protocol))
            {
                handler.SetCloseNotifyReadTimeout(TimeSpan.FromSeconds(5));
            }
            ch.Pipeline.AddLast(handler);
            ch.Pipeline.AddLast(
                    new SslCompletionEventHandler(
                            clientHandshakeCompletionEvents, clientCloseCompletionEvents));
        }
    }

    // CLR: Queue<SslHandshakeCompletionEvent>/Queue<SslCloseCompletionEvent> are the BlockingCollections of the test
    // (add is Add).
    private class SslCompletionEventHandler : ChannelInboundHandlerAdapter
    {
        private readonly BlockingCollection<SslHandshakeCompletionEvent> handshakeCompletionEvents;
        private readonly BlockingCollection<SslCloseCompletionEvent> closeCompletionEvents;

        internal SslCompletionEventHandler(BlockingCollection<SslHandshakeCompletionEvent> handshakeCompletionEvents,
                                           BlockingCollection<SslCloseCompletionEvent> closeCompletionEvents)
        {
            this.handshakeCompletionEvents = handshakeCompletionEvents;
            this.closeCompletionEvents = closeCompletionEvents;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is SslHandshakeCompletionEvent)
            {
                handshakeCompletionEvents.Add((SslHandshakeCompletionEvent) evt);
            }
            else if (evt is SslCloseCompletionEvent)
            {
                closeCompletionEvents.Add((SslCloseCompletionEvent) evt);
            }
        }

        public override bool IsSharable => true;
    }

    // ---- CLR-only helpers -----------------------------------------------------------------------------------------

    // CLR: group.next().newPromise(): a TaskCompletionSource whose continuations do not run inside the completing call.
    private static TaskCompletionSource NewPromise()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // CLR: Future.cause(): null on success or while pending; a cancelled Task's cause is a TaskCanceledException.
    private static Exception CauseOf(Task future)
    {
        if (future.IsFaulted)
        {
            return future.Exception.InnerException;
        }
        return future.IsCanceled ? new TaskCanceledException(future) : null;
    }

    // CLR: CountDownLatch.countDown() does nothing once the count is zero, while CountdownEvent.Signal() throws
    // InvalidOperationException when "The current instance is already set"
    // (https://learn.microsoft.com/dotnet/api/system.threading.countdownevent.signal), as FlowControlHandlerTest notes.
    private static void CountDown(CountdownEvent latch)
    {
        lock (latch)
        {
            if (latch.CurrentCount > 0)
            {
                latch.Signal();
            }
        }
    }

    // CLR: future.addListener(listener) on a promise of the channel's event loop: DefaultPromise notifies the listener on
    // that executor (docs/handler-porting.md, "Listener timing"), and the handler's Tasks run their continuations
    // asynchronously, so the continuation hands the listener to the executor.
    private static void AddListener(Task future, IEventExecutor executor, Action<Task> listener)
    {
        _ = future.ContinueWith(f =>
        {
            if (executor.InEventLoop())
            {
                listener(f);
            }
            else
            {
                executor.Execute(() => listener(f));
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    // CLR: the server is bound to the wildcard address, which the JDK connects to through the loopback address
    // (SocketChannelImpl.checkRemote), while a CLR connect to 0.0.0.0 fails on Windows (docs/handler-porting.md,
    // "Socket tests"), so the client connects to the loopback address of the bound port.
    private static EndPoint ConnectAddress(EndPoint server)
    {
        IPEndPoint address = (IPEndPoint) server;
        if (address.Address.Equals(IPAddress.Any))
        {
            return new IPEndPoint(IPAddress.Loopback, address.Port);
        }
        if (address.Address.Equals(IPAddress.IPv6Any))
        {
            return new IPEndPoint(IPAddress.IPv6Loopback, address.Port);
        }
        return server;
    }

    // CLR: CachedSelfSignedCertificate.getCachedCertificate() returns a cached SelfSignedCertificate; both are @Deprecated
    // test units that are not ported ("@deprecated Use the {@code CertificateBuilder} from {@code netty-pkitesting}
    // instead", recorded by the lead, docs/handler-ssl-design.md), so the nested SelfSignedCertificateBundle caches the
    // X509Bundle that SelfSignedCertificate's CertificateBuilderCertGenerator builds by default: a self-signed
    // certificate authority "CN=localhost" with an RSA 2048 key. Its Certificate()/PrivateKey() are the PEM files of the
    // certificate path and of the PKCS #8 key (X509Bundle.ToTempCertChainPem/ToTempPrivateKeyPem, deleted at process exit
    // as SelfSignedCertificate's deleteOnExit() files are), Cert() the certificate and Key() the key, which is the BCL key
    // object of the bundle's key pair (docs/pkitesting-porting.md: "A generated or given key pair is the BCL key object");
    // KeyManagerFactory() is the bundle's SslStreamCertificateContext (X509Bundle.ToKeyManagerFactory, a persisted key).
    private static readonly Lazy<SelfSignedCertificateBundle> CACHED_CERTIFICATE =
            new Lazy<SelfSignedCertificateBundle>(() => new SelfSignedCertificateBundle(new CertificateBuilder()
                    .SetIsCertificateAuthority(true)
                    .Subject("CN=localhost")
                    .Rsa2048()
                    .BuildSelfSigned()));

    private static SelfSignedCertificateBundle CachedSelfSignedCertificate()
    {
        return CACHED_CERTIFICATE.Value;
    }

    private sealed class SelfSignedCertificateBundle
    {
        private readonly X509Bundle bundle;
        private readonly Lazy<FileInfo> certificate;
        private readonly Lazy<FileInfo> privateKey;
        private readonly Lazy<SslStreamCertificateContext> keyManagerFactory;

        internal SelfSignedCertificateBundle(X509Bundle bundle)
        {
            this.bundle = bundle;
            certificate = new Lazy<FileInfo>(bundle.ToTempCertChainPem);
            privateKey = new Lazy<FileInfo>(bundle.ToTempPrivateKeyPem);
            keyManagerFactory = new Lazy<SslStreamCertificateContext>(bundle.ToKeyManagerFactory);
        }

        internal FileInfo Certificate() => certificate.Value;

        internal FileInfo PrivateKey() => privateKey.Value;

        internal X509Certificate2 Cert() => bundle.GetCertificate();

        internal AsymmetricAlgorithm Key() => (AsymmetricAlgorithm) bundle.GetKeyPair();

        internal SslStreamCertificateContext KeyManagerFactory() => keyManagerFactory.Value;
    }
}
