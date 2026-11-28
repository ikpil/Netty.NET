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
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.PkiTesting;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: SslProvider.OPENSSL and OPENSSL_REFCNT are library-not-ported (netty-tcnative; owner decision 2026-11-24,
// docs/handler-ssl-design.md) and OpenSsl.isAvailable() has no counterpart, so data() holds the JDK pair only.
// @ParameterizedTest with @MethodSource is [Theory] with [MemberData], and @Timeout(value, MILLISECONDS) the Timeout of
// the [Theory], which xUnit applies to async tests. The tests that used the @Deprecated CachedSelfSignedCertificate use
// CachedSelfSignedCertificate() below (see there). Promise/Future/ChannelFuture are TaskCompletionSource/Task
// (docs/handler-porting.md, "Task promises"): get() and syncUninterruptibly() are awaits, awaitUninterruptibly() an
// await that suppresses the failure (ConfigureAwaitOptions.SuppressThrowing | ContinueOnCapturedContext, xUnit1030),
// isSuccess() is IsCompletedSuccessfully and PromiseNotifier.cascade(future, promise) is a continuation that copies the
// outcome with TrySetFromTask (docs/common-task-composition.md). The bootstrap API is asynchronous, new
// InetSocketAddress(0) is the wildcard IPEndPoint and ConnectAddress(...) connects to it through the loopback address
// (docs/handler-porting.md, "Socket tests"); Class<? extends Channel> is a Type for Bootstrap.Channel(Type).
// SSLException is AuthenticationException (ssl design decision 3), IllegalStateException InvalidOperationException,
// CountDownLatch CountdownEvent, AtomicBoolean the Common type and ThreadLocalRandom.current() Random.Shared
// (docs/common-clr-design.md). The anonymous handlers are named nested classes.
#pragma warning disable xUnit1069 // The tests' helpers wait with TestContext.Current.CancellationToken themselves.
public class ParameterizedSslHandlerTest
{

    // CLR: PARAMETERIZED_NAME is the JUnit display-name pattern of the @ParameterizedTest annotations; xUnit names theory
    // cases by their arguments, so the constant only documents the original names.
    private const string PARAMETERIZED_NAME = "{index}: clientProvider={0}, {index}: serverProvider={1}";

    public static IEnumerable<object[]> Data()
    {
        List<SslProvider> providers = new List<SslProvider>(3);
        providers.Add(SslProvider.JDK);

        List<object[]> @params = new List<object[]>();

        foreach (SslProvider cp in providers)
        {
            foreach (SslProvider sp in providers)
            {
                @params.Add(new object[] { cp, sp });
            }
        }

        return @params;
    }

    [Theory(Timeout = 48000)]
    [MemberData(nameof(Data))]
    public async Task TestCompositeBufSizeEstimationGuaranteesSynchronousWrite(
            SslProvider clientProvider, SslProvider serverProvider)
    {
        await CompositeBufSizeEstimationGuaranteesSynchronousWrite(serverProvider, clientProvider,
                true, true, true);
        await CompositeBufSizeEstimationGuaranteesSynchronousWrite(serverProvider, clientProvider,
                true, true, false);
        await CompositeBufSizeEstimationGuaranteesSynchronousWrite(serverProvider, clientProvider,
                true, false, true);
        await CompositeBufSizeEstimationGuaranteesSynchronousWrite(serverProvider, clientProvider,
                true, false, false);
        await CompositeBufSizeEstimationGuaranteesSynchronousWrite(serverProvider, clientProvider,
                false, true, true);
        await CompositeBufSizeEstimationGuaranteesSynchronousWrite(serverProvider, clientProvider,
                false, true, false);
        await CompositeBufSizeEstimationGuaranteesSynchronousWrite(serverProvider, clientProvider,
                false, false, true);
        await CompositeBufSizeEstimationGuaranteesSynchronousWrite(serverProvider, clientProvider,
                false, false, false);
    }

    // CLR: new SslHandler(sslCtx.newEngine(alloc)) wraps an engine of the context without the context's startTls;
    // newEngine is not ported (owner decision 2026-11-24: no SSLEngine facade), and the context's
    // NewHandler(alloc, startTls: false) builds that SslHandler for the same fresh engine options, so the
    // letHandlerCreate*Engine == false paths call it.
    private static async Task CompositeBufSizeEstimationGuaranteesSynchronousWrite(
            SslProvider serverProvider, SslProvider clientProvider,
            bool serverDisableWrapSize,
            bool letHandlerCreateServerEngine, bool letHandlerCreateClientEngine)
    {
        SelfSignedCertificateBundle ssc = CachedSelfSignedCertificate();

        SslContext sslServerCtx = SslContextBuilder.ForServer(ssc.Certificate(), ssc.PrivateKey())
                .SslProvider(serverProvider)
                .Build();

        SslContext sslClientCtx = SslContextBuilder.ForClient()
                .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                .SslProvider(clientProvider).Build();

        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
        IChannel sc = null;
        IChannel cc = null;

        try
        {
            TaskCompletionSource donePromise = NewPromise();
            // The goal is to provide the SSLEngine with many ByteBuf components to ensure that the overhead for wrap
            // is correctly accounted for on each component.
            int numComponents = 150;
            // This is the TLS packet size. The goal is to divide the maximum amount of application data that can fit
            // into a single TLS packet into many components to ensure the overhead is correctly taken into account.
            int desiredBytes = 16384;
            int singleComponentSize = desiredBytes / numComponents;
            int expectedBytes = numComponents * singleComponentSize;

            sc = await new ServerBootstrap()
                    .Group(group)
                    .Channel<NioServerSocketChannel>()
                    .ChildHandler(new CompositeServerInitializer(sslServerCtx, letHandlerCreateServerEngine,
                            serverDisableWrapSize, donePromise, numComponents, singleComponentSize))
                    .BindAsync(new IPEndPoint(IPAddress.Any, 0), TestContext.Current.CancellationToken);

            cc = await new Bootstrap()
                    .Group(group)
                    .Channel<NioSocketChannel>()
                    .Handler(new CompositeClientInitializer(sslClientCtx, letHandlerCreateClientEngine, donePromise,
                            expectedBytes))
                    .ConnectAsync(ConnectAddress(sc.LocalAddress), TestContext.Current.CancellationToken);

            await donePromise.Task;
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

    private sealed class CompositeServerInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslContext sslServerCtx;
        private readonly bool letHandlerCreateServerEngine;
        private readonly bool serverDisableWrapSize;
        private readonly TaskCompletionSource donePromise;
        private readonly int numComponents;
        private readonly int singleComponentSize;

        internal CompositeServerInitializer(SslContext sslServerCtx, bool letHandlerCreateServerEngine,
                                            bool serverDisableWrapSize, TaskCompletionSource donePromise,
                                            int numComponents, int singleComponentSize)
        {
            this.sslServerCtx = sslServerCtx;
            this.letHandlerCreateServerEngine = letHandlerCreateServerEngine;
            this.serverDisableWrapSize = serverDisableWrapSize;
            this.donePromise = donePromise;
            this.numComponents = numComponents;
            this.singleComponentSize = singleComponentSize;
        }

        protected override void InitChannel(IChannel ch)
        {
            SslHandler handler = letHandlerCreateServerEngine
                    ? sslServerCtx.NewHandler(ch.Alloc())
                    : sslServerCtx.NewHandler(ch.Alloc(), false);

            if (serverDisableWrapSize)
            {
                handler.SetWrapDataSize(-1);
            }

            ch.Pipeline.AddLast(handler);
            ch.Pipeline.AddLast(new CompositeServerHandler(donePromise, numComponents, singleComponentSize));
        }
    }

    private sealed class CompositeServerHandler : ChannelInboundHandlerAdapter
    {
        private readonly TaskCompletionSource donePromise;
        private readonly int numComponents;
        private readonly int singleComponentSize;
        private volatile bool sentData;
        private volatile Exception writeCause;

        internal CompositeServerHandler(TaskCompletionSource donePromise, int numComponents, int singleComponentSize)
        {
            this.donePromise = donePromise;
            this.numComponents = numComponents;
            this.singleComponentSize = singleComponentSize;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is SslHandshakeCompletionEvent)
            {
                SslHandshakeCompletionEvent sslEvt = (SslHandshakeCompletionEvent) evt;
                if (sslEvt.IsSuccess())
                {
                    CompositeByteBuf content = ctx.Alloc().CompositeDirectBuffer(numComponents);

                    for (int i = 0; i < numComponents; ++i)
                    {
                        ByteBuf buf = ctx.Alloc().DirectBuffer(singleComponentSize);
                        buf.WriterIndex = buf.WriterIndex + singleComponentSize;
                        content.AddComponent(buf, true);
                    }

                    _ = ctx.WriteAndFlushAsync(content).ContinueWith(future =>
                    {
                        writeCause = CauseOf(future);
                        if (writeCause == null)
                        {
                            sentData = true;
                        }
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
                else
                {
                    donePromise.TrySetException(sslEvt.Cause());
                }
            }

            ctx.FireUserEventTriggered(evt);
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            donePromise.TrySetException(new InvalidOperationException("server exception sentData: " +
                    BoolText(sentData) + " writeCause: " + writeCause, cause));
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            donePromise.TrySetException(new InvalidOperationException("server closed sentData: " +
                    BoolText(sentData) + " writeCause: " + writeCause));
        }
    }

    private sealed class CompositeClientInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslContext sslClientCtx;
        private readonly bool letHandlerCreateClientEngine;
        private readonly TaskCompletionSource donePromise;
        private readonly int expectedBytes;

        internal CompositeClientInitializer(SslContext sslClientCtx, bool letHandlerCreateClientEngine,
                                            TaskCompletionSource donePromise, int expectedBytes)
        {
            this.sslClientCtx = sslClientCtx;
            this.letHandlerCreateClientEngine = letHandlerCreateClientEngine;
            this.donePromise = donePromise;
            this.expectedBytes = expectedBytes;
        }

        protected override void InitChannel(IChannel ch)
        {
            if (letHandlerCreateClientEngine)
            {
                ch.Pipeline.AddLast(sslClientCtx.NewHandler(ch.Alloc()));
            }
            else
            {
                ch.Pipeline.AddLast(sslClientCtx.NewHandler(ch.Alloc(), false));
            }

            ch.Pipeline.AddLast(new CompositeClientHandler(donePromise, expectedBytes));
        }
    }

    private sealed class CompositeClientHandler : ChannelInboundHandlerAdapter
    {
        private readonly TaskCompletionSource donePromise;
        private readonly int expectedBytes;
        private int bytesSeen;

        internal CompositeClientHandler(TaskCompletionSource donePromise, int expectedBytes)
        {
            this.donePromise = donePromise;
            this.expectedBytes = expectedBytes;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is ByteBuf)
            {
                bytesSeen += ((ByteBuf) msg).ReadableBytes;
                if (bytesSeen == expectedBytes)
                {
                    donePromise.TrySetResult();
                }
            }

            ReferenceCountUtil.Release(msg);
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is SslHandshakeCompletionEvent)
            {
                SslHandshakeCompletionEvent sslEvt = (SslHandshakeCompletionEvent) evt;
                if (!sslEvt.IsSuccess())
                {
                    donePromise.TrySetException(sslEvt.Cause());
                }
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            donePromise.TrySetException(new InvalidOperationException("client exception. bytesSeen: " +
                    bytesSeen, cause));
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            donePromise.TrySetException(new InvalidOperationException("client closed. bytesSeen: " +
                    bytesSeen));
        }
    }

    // CLR: the anonymous SimpleTrustManagerFactory (a JCA SPI class that is jvm-only-replaced) hands out one
    // X509TrustManager, which is a RemoteCertificateValidationCallback (ssl design decision 3, as
    // InsecureTrustManagerFactory): the server's callback only sees client certificates, so it is checkClientTrusted,
    // and a client-mode sender would be checkServerTrusted (decided by SslStream.IsServer, as InsecureTrustManagerFactory
    // does). engineInit(KeyStore), engineInit(ManagerFactoryParameters) and getAcceptedIssuers() have no callback
    // counterpart. A trust manager fails verification by throwing CertificateException, which JSSE turns into the alert;
    // a validation callback fails it by returning false, after which SslStream creates the fatal alert
    // (CreateFatalHandshakeAlertToken), while an exception thrown by the callback ends the handshake without one
    // (https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Net.Security/src/System/Net/Security/SslStream.Protocol.cs),
    // so the failed check returns false. ResourcesUtil.getFile(getClass(), name) is the resource deployed next to the
    // test assembly.
    [Theory(Timeout = 30000)]
    [MemberData(nameof(Data))]
    public async Task TestAlertProducedAndSend(SslProvider clientProvider, SslProvider serverProvider)
    {
        SelfSignedCertificateBundle ssc = CachedSelfSignedCertificate();

        SslContext sslServerCtx = SslContextBuilder.ForServer(ssc.Certificate(), ssc.PrivateKey())
                .SslProvider(serverProvider)
                .TrustManager(FailingClientTrustManager)
                .ClientAuth(ClientAuth.REQUIRE).Build();

        SslContext sslClientCtx = SslContextBuilder.ForClient()
                .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                .KeyManager(ResourcesUtil.GetFile(GetType(),  "test.crt"),
                        ResourcesUtil.GetFile(GetType(), "test_unencrypted.pem"))
                .SslProvider(clientProvider).Build();

        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
        IChannel sc = null;
        IChannel cc = null;

        try
        {
            TaskCompletionSource promise = NewPromise();
            sc = await new ServerBootstrap()
                    .Group(group)
                    .Channel<NioServerSocketChannel>()
                    .ChildHandler(new AlertServerInitializer(sslServerCtx))
                    .BindAsync(new IPEndPoint(IPAddress.Any, 0), TestContext.Current.CancellationToken);

            cc = await new Bootstrap()
                    .Group(group)
                    .Channel<NioSocketChannel>()
                    .Handler(new AlertClientInitializer(sslClientCtx, promise))
                    .ConnectAsync(ConnectAddress(sc.LocalAddress), TestContext.Current.CancellationToken);

            await promise.Task;
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

    private static bool FailingClientTrustManager(object sender, X509Certificate certificate, X509Chain chain,
                                                  SslPolicyErrors sslPolicyErrors)
    {
        if (sender is SslStream stream && stream.IsServer)
        {
            // CLR: checkClientTrusted(x509Certificates, s).
            // Fail verification which should produce an alert that is send back to the client.
            return false;
        }

        // CLR: checkServerTrusted(x509Certificates, s).
        // NOOP
        return true;
    }

    private sealed class AlertServerInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslContext sslServerCtx;

        internal AlertServerInitializer(SslContext sslServerCtx)
        {
            this.sslServerCtx = sslServerCtx;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Pipeline.AddLast(sslServerCtx.NewHandler(ch.Alloc()));
            ch.Pipeline.AddLast(new CloseOnExceptionHandler());
        }
    }

    private sealed class CloseOnExceptionHandler : ChannelInboundHandlerAdapter
    {
        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            // Just trigger a close
            _ = ctx.CloseAsync();
        }
    }

    private sealed class AlertClientInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslContext sslClientCtx;
        private readonly TaskCompletionSource promise;

        internal AlertClientInitializer(SslContext sslClientCtx, TaskCompletionSource promise)
        {
            this.sslClientCtx = sslClientCtx;
            this.promise = promise;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Pipeline.AddLast(sslClientCtx.NewHandler(ch.Alloc()));
            ch.Pipeline.AddLast(new AlertReceivedHandler(promise));
        }
    }

    private sealed class AlertReceivedHandler : ChannelInboundHandlerAdapter
    {
        private readonly TaskCompletionSource promise;

        internal AlertReceivedHandler(TaskCompletionSource promise)
        {
            this.promise = promise;
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (cause.InnerException is AuthenticationException)
            {
                // We received the alert and so produce an SSLException.
                promise.TrySetResult();
            }
        }
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(Data))]
    public async Task TestCloseNotify(SslProvider clientProvider, SslProvider serverProvider)
    {
        await TestCloseNotify0(clientProvider, serverProvider, 5000, false);
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(Data))]
    public async Task TestCloseNotifyReceivedTimeout(SslProvider clientProvider, SslProvider serverProvider)
    {
        await TestCloseNotify0(clientProvider, serverProvider, 100, true);
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(Data))]
    public async Task TestCloseNotifyNotWaitForResponse(SslProvider clientProvider, SslProvider serverProvider)
    {
        await TestCloseNotify0(clientProvider, serverProvider, 0, false);
    }

    // CLR: xUnit1024 forbids overloading a test name, so the helper testCloseNotify(...) ends in 0, as
    // CipherSuiteConverterTest's helpers do. SignatureException is CryptographicException (ssl wave 1).
    private async Task TestCloseNotify0(SslProvider clientProvider, SslProvider serverProvider,
                                 long closeNotifyReadTimeout, bool timeout)
    {
        SelfSignedCertificateBundle ssc = CachedSelfSignedCertificate();

        SslContext sslServerCtx = SslContextBuilder.ForServer(ssc.Certificate(), ssc.PrivateKey())
                                                         .SslProvider(serverProvider)
                                                         // Use TLSv1.2 as we depend on the fact that the handshake
                                                         // is done in an extra round trip in the test which
                                                         // is not true in TLSv1.3
                                                         .Protocols(Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_2)
                                                         .Build();

        SslContext sslClientCtx = SslContextBuilder.ForClient()
                                                         .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                                                         .SslProvider(clientProvider)
                                                         // Use TLSv1.2 as we depend on the fact that the handshake
                                                         // is done in an extra round trip in the test which
                                                         // is not true in TLSv1.3
                                                         .Protocols(Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_2)
                                                         .Build();

        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
        IChannel sc = null;
        IChannel cc = null;

        try
        {
            TaskCompletionSource<IChannel> clientPromise = NewChannelPromise();
            TaskCompletionSource<IChannel> serverPromise = NewChannelPromise();

            sc = await new ServerBootstrap()
                    .Group(group)
                    .Channel<NioServerSocketChannel>()
                    .ChildHandler(new CloseNotifyServerInitializer(sslServerCtx, closeNotifyReadTimeout, serverPromise))
                    .BindAsync(new IPEndPoint(IPAddress.Any, 0), TestContext.Current.CancellationToken);

            cc = await new Bootstrap()
                    .Group(group)
                    .Channel<NioSocketChannel>()
                    .Handler(new CloseNotifyClientInitializer(sslClientCtx, closeNotifyReadTimeout, timeout,
                            clientPromise))
                    .ConnectAsync(ConnectAddress(sc.LocalAddress), TestContext.Current.CancellationToken);

            await ((Task) serverPromise.Task).ConfigureAwait(
                    ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            await ((Task) clientPromise.Task).ConfigureAwait(
                    ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);

            // Server always received the close_notify as the client triggers the close sequence.
            Assert.True(serverPromise.Task.IsCompletedSuccessfully);

            // Depending on if we wait for the response or not the promise will be failed or not.
            if (closeNotifyReadTimeout > 0 && !timeout)
            {
                Assert.True(clientPromise.Task.IsCompletedSuccessfully);
            }
            else
            {
                Assert.False(clientPromise.Task.IsCompletedSuccessfully);
            }
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

    private sealed class CloseNotifyServerInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslContext sslServerCtx;
        private readonly long closeNotifyReadTimeout;
        private readonly TaskCompletionSource<IChannel> serverPromise;

        internal CloseNotifyServerInitializer(SslContext sslServerCtx, long closeNotifyReadTimeout,
                                              TaskCompletionSource<IChannel> serverPromise)
        {
            this.sslServerCtx = sslServerCtx;
            this.closeNotifyReadTimeout = closeNotifyReadTimeout;
            this.serverPromise = serverPromise;
        }

        protected override void InitChannel(IChannel ch)
        {
            SslHandler handler = sslServerCtx.NewHandler(ch.Alloc());
            handler.SetCloseNotifyReadTimeoutMillis(closeNotifyReadTimeout);
            Cascade(handler.SslCloseFuture(), serverPromise);
            _ = handler.HandshakeFuture().ContinueWith(future =>
            {
                if (!future.IsCompletedSuccessfully)
                {
                    // Something bad happened during handshake fail the promise!
                    serverPromise.TrySetException(CauseOf(future));
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            ch.Pipeline.AddLast(handler);
            ch.Pipeline.AddLast(new SilenceExceptionHandler()
                    .RootCauseInstanceOf(typeof(CryptographicException)));
        }
    }

    private sealed class CloseNotifyClientInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslContext sslClientCtx;
        private readonly long closeNotifyReadTimeout;
        private readonly bool timeout;
        private readonly TaskCompletionSource<IChannel> clientPromise;

        internal CloseNotifyClientInitializer(SslContext sslClientCtx, long closeNotifyReadTimeout, bool timeout,
                                              TaskCompletionSource<IChannel> clientPromise)
        {
            this.sslClientCtx = sslClientCtx;
            this.closeNotifyReadTimeout = closeNotifyReadTimeout;
            this.timeout = timeout;
            this.clientPromise = clientPromise;
        }

        protected override void InitChannel(IChannel ch)
        {
            AtomicBoolean closeSent = new AtomicBoolean();

            if (timeout)
            {
                ch.Pipeline.AddFirst(new DropAfterCloseHandler(closeSent));
            }

            SslHandler handler = sslClientCtx.NewHandler(ch.Alloc());
            handler.SetCloseNotifyReadTimeoutMillis(closeNotifyReadTimeout);
            Cascade(handler.SslCloseFuture(), clientPromise);
            _ = handler.HandshakeFuture().ContinueWith(future =>
            {
                if (future.IsCompletedSuccessfully)
                {
                    closeSent.CompareAndSet(false, true);
                    _ = future.Result.CloseAsync();
                }
                else
                {
                    // Something bad happened during handshake fail the promise!
                    clientPromise.TrySetException(CauseOf(future));
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            ch.Pipeline.AddLast(handler);
        }
    }

    private sealed class DropAfterCloseHandler : ChannelInboundHandlerAdapter
    {
        private readonly AtomicBoolean closeSent;

        internal DropAfterCloseHandler(AtomicBoolean closeSent)
        {
            this.closeSent = closeSent;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (closeSent.Get())
            {
                // Drop data on the floor so we will get a timeout while waiting for the
                // close_notify.
                ReferenceCountUtil.Release(msg);
            }
            else
            {
                base.ChannelRead(ctx, msg);
            }
        }
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(Data))]
    public async Task ReentryOnHandshakeCompleteNioChannel(SslProvider clientProvider, SslProvider serverProvider)
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        try
        {
            Type serverClass = typeof(NioServerSocketChannel);
            Type clientClass = typeof(NioSocketChannel);
            EndPoint bindAddress = new IPEndPoint(IPAddress.Any, 0);
            await ReentryOnHandshakeComplete(clientProvider, serverProvider, group, bindAddress,
                    serverClass, clientClass, false, false);
            await ReentryOnHandshakeComplete(clientProvider, serverProvider, group, bindAddress,
                    serverClass, clientClass, false, true);
            await ReentryOnHandshakeComplete(clientProvider, serverProvider, group, bindAddress,
                    serverClass, clientClass, true, false);
            await ReentryOnHandshakeComplete(clientProvider, serverProvider, group, bindAddress,
                    serverClass, clientClass, true, true);
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(Data))]
    public async Task ReentryOnHandshakeCompleteLocalChannel(SslProvider clientProvider, SslProvider serverProvider)
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(LocalIoHandler.NewFactory());

        try
        {
            Type serverClass = typeof(LocalServerChannel);
            Type clientClass = typeof(LocalChannel);
            EndPoint bindAddress = new LocalAddress(
                    Random.Shared.NextInt64().ToString(CultureInfo.InvariantCulture));
            await ReentryOnHandshakeComplete(clientProvider, serverProvider, group, bindAddress,
                    serverClass, clientClass, false, false);
            await ReentryOnHandshakeComplete(clientProvider, serverProvider, group, bindAddress,
                    serverClass, clientClass, false, true);
            await ReentryOnHandshakeComplete(clientProvider, serverProvider, group, bindAddress,
                    serverClass, clientClass, true, false);
            await ReentryOnHandshakeComplete(clientProvider, serverProvider, group, bindAddress,
                    serverClass, clientClass, true, true);
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    private async Task ReentryOnHandshakeComplete(SslProvider clientProvider, SslProvider serverProvider,
                                            IEventLoopGroup group, EndPoint bindAddress,
                                            Type serverClass,
                                            Type clientClass, bool serverAutoRead,
                                            bool clientAutoRead)
    {
        SelfSignedCertificateBundle ssc = CachedSelfSignedCertificate();
        SslContext sslServerCtx = SslContextBuilder.ForServer(ssc.Certificate(), ssc.PrivateKey())
                .SslProvider(serverProvider)
                .Build();

        SslContext sslClientCtx = SslContextBuilder.ForClient()
                .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                .SslProvider(clientProvider)
                .Build();

        IChannel sc = null;
        IChannel cc = null;

        try
        {
            string expectedContent = "HelloWorld";
            CountdownEvent serverLatch = new CountdownEvent(1);
            CountdownEvent clientLatch = new CountdownEvent(1);
            StringBuilder serverQueue = new StringBuilder(expectedContent.Length);
            StringBuilder clientQueue = new StringBuilder(expectedContent.Length);

            sc = await new ServerBootstrap()
                    .Group(group)
                    .Channel(serverClass)
                    .ChildOption(ChannelOption.AUTO_READ, serverAutoRead)
                    .ChildHandler(new ReentryInitializer(sslServerCtx, expectedContent, serverQueue, serverLatch))
                    .BindAsync(bindAddress, TestContext.Current.CancellationToken);

            cc = await new Bootstrap()
                    .Group(group)
                    .Channel(clientClass)
                    .Option(ChannelOption.AUTO_READ, clientAutoRead)
                    .Handler(new ReentryInitializer(sslClientCtx, expectedContent, clientQueue, clientLatch))
                    .ConnectAsync(ConnectAddress(sc.LocalAddress), TestContext.Current.CancellationToken);

            serverLatch.Wait(TestContext.Current.CancellationToken);
            Assert.Equal(expectedContent, serverQueue.ToString());
            clientLatch.Wait(TestContext.Current.CancellationToken);
            Assert.Equal(expectedContent, clientQueue.ToString());
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

            ReferenceCountUtil.Release(sslServerCtx);
            ReferenceCountUtil.Release(sslClientCtx);
        }
    }

    private sealed class ReentryInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslContext sslCtx;
        private readonly string expectedContent;
        private readonly StringBuilder queue;
        private readonly CountdownEvent latch;

        internal ReentryInitializer(SslContext sslCtx, string expectedContent, StringBuilder queue,
                                    CountdownEvent latch)
        {
            this.sslCtx = sslCtx;
            this.expectedContent = expectedContent;
            this.queue = queue;
            this.latch = latch;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Pipeline.AddLast(DisableHandshakeTimeout(sslCtx.NewHandler(ch.Alloc())));
            ch.Pipeline.AddLast(new ReentryWriteSslHandshakeHandler(expectedContent, queue, latch));
        }
    }

    private static SslHandler DisableHandshakeTimeout(SslHandler handler)
    {
        handler.SetHandshakeTimeoutMillis(0);
        return handler;
    }

    // CLR: cause.printStackTrace(PrintStream) writes Throwable.toString() and the stack trace, which Exception.ToString()
    // returns ("the name of the class that threw the current exception, the message, ... and the stack trace",
    // https://learn.microsoft.com/en-us/dotnet/api/system.exception.tostring?view=net-10.0); the
    // ByteArrayOutputStream/PrintStream pair is a MemoryStream written through a StreamWriter, out.toString(charsetName)
    // decodes it with Encoding.GetEncoding(name), which throws ArgumentException for an unknown name as toString(String)
    // throws UnsupportedEncodingException.
    private sealed class ReentryWriteSslHandshakeHandler : SimpleChannelInboundHandler<ByteBuf>
    {
        private static readonly IInternalLogger LOGGER =
                InternalLoggerFactory.GetInstance(typeof(ReentryWriteSslHandshakeHandler));
        private readonly string toWrite;
        private readonly StringBuilder readQueue;
        private readonly CountdownEvent doneLatch;

        internal ReentryWriteSslHandshakeHandler(string toWrite, StringBuilder readQueue, CountdownEvent doneLatch)
        {
            this.toWrite = toWrite;
            this.readQueue = readQueue;
            this.doneLatch = doneLatch;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            // Write toWrite in two chunks, first here then we get SslHandshakeCompletionEvent (which is re-entry).
            _ = ctx.WriteAndFlushAsync(ByteBufUtil.WriteAscii(ctx.Alloc(), toWrite.Substring(0, toWrite.Length / 2)));
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf msg)
        {
            readQueue.Append(msg.ToString(Encoding.ASCII));

            if (readQueue.Length >= toWrite.Length)
            {
                CountDown(doneLatch);
            }
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is SslHandshakeCompletionEvent)
            {
                SslHandshakeCompletionEvent sslEvt = (SslHandshakeCompletionEvent) evt;
                if (sslEvt.IsSuccess())
                {
                    // this is the re-entry write, it should be ordered after the subsequent write.
                    _ = ctx.WriteAndFlushAsync(ByteBufUtil.WriteAscii(ctx.Alloc(), toWrite.Substring(toWrite.Length / 2)));
                }
                else
                {
                    AppendError(sslEvt.Cause());
                }
            }

            ctx.FireUserEventTriggered(evt);
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            AppendError(cause);
            ctx.FireExceptionCaught(cause);
        }

        private void AppendError(Exception cause)
        {
            LOGGER.Error(new Exception("Caught possible write failure in ParameterizedSslHandlerTest.", cause));
            readQueue.Append("failed to write '").Append(toWrite).Append("': ");

            MemoryStream @out = new MemoryStream();

            try
            {
                using (StreamWriter printStream = new StreamWriter(@out, Encoding.ASCII, -1, true))
                {
                    printStream.Write(cause.ToString());
                }

                readQueue.Append(Encoding.GetEncoding("US-ASCII").GetString(@out.ToArray()));
            }
            catch (ArgumentException)
            {
                // Let's just fallback to using toString().
                readQueue.Append(cause);
            }
            finally
            {
                CountDown(doneLatch);
            }
        }
    }

    // ---- CLR-only helpers -----------------------------------------------------------------------------------------

    // CLR: group.next().newPromise(): a TaskCompletionSource whose continuations do not run inside the completing call.
    private static TaskCompletionSource NewPromise()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static TaskCompletionSource<IChannel> NewChannelPromise()
    {
        return new TaskCompletionSource<IChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // CLR: PromiseNotifier.cascade(future, promise).
    private static void Cascade(Task<IChannel> future, TaskCompletionSource<IChannel> promise)
    {
        _ = future.ContinueWith(f => promise.TrySetFromTask(f), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
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

    // CLR: a boolean concatenated into a Java string is "true"/"false" (docs/handler-porting.md, Text).
    private static string BoolText(bool value)
    {
        return value ? "true" : "false";
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

    // CLR: the server is bound to the wildcard address, which the JDK connects to through the loopback address
    // (SocketChannelImpl.checkRemote), while a CLR connect to 0.0.0.0 fails on Windows (docs/handler-porting.md,
    // "Socket tests"), so the client connects to the loopback address of the bound port; other addresses are kept.
    private static EndPoint ConnectAddress(EndPoint server)
    {
        if (server is IPEndPoint address)
        {
            if (address.Address.Equals(IPAddress.Any))
            {
                return new IPEndPoint(IPAddress.Loopback, address.Port);
            }
            if (address.Address.Equals(IPAddress.IPv6Any))
            {
                return new IPEndPoint(IPAddress.IPv6Loopback, address.Port);
            }
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
    // object of the bundle's key pair (docs/pkitesting-porting.md: "A generated or given key pair is the BCL key object").
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

        internal SelfSignedCertificateBundle(X509Bundle bundle)
        {
            this.bundle = bundle;
            certificate = new Lazy<FileInfo>(bundle.ToTempCertChainPem);
            privateKey = new Lazy<FileInfo>(bundle.ToTempPrivateKeyPem);
        }

        internal FileInfo Certificate()
        {
            return certificate.Value;
        }

        internal FileInfo PrivateKey()
        {
            return privateKey.Value;
        }

        internal X509Certificate2 Cert()
        {
            return bundle.GetCertificate();
        }

        internal AsymmetricAlgorithm Key()
        {
            return (AsymmetricAlgorithm) bundle.GetKeyPair();
        }
    }
}
#pragma warning restore xUnit1069
