/*
 * Copyright 2015 The Netty Project
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
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.PkiTesting;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: @Timeout(value = 30000, unit = MILLISECONDS) is the Timeout of the [Fact], which xUnit applies to async
// tests; the abstract class's test runs in each concrete subclass (JdkSslRenegotiateTest), as JUnit runs inherited
// @Test methods. Throwable is Exception, CountDownLatch is CountdownEvent, AtomicReference the Common type, the
// anonymous handlers are named nested classes and the Future listener of renegotiate() is a continuation of its
// Task. The bootstrap API is asynchronous (syncUninterruptibly() is an await). SslHandler renegotiates on
// SslStream as a server only, through SslStream.NegotiateClientCertificateAsync ("Negotiates the client
// certificate on the authenticated connection",
// https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.negotiateclientcertificateasync?view=net-10.0),
// which is the side this test renegotiates. That renegotiation always asks the client for a certificate; the
// server context without client authentication accepts the client of this test, which has none, as a JSSE server
// checks no client certificate then (JdkSslContext.VerifyRemoteCertificate).
public abstract class RenegotiateTest
{

    [Fact(Timeout = 30000)]
    public async Task TestRenegotiateServer()
    {
        AtomicReference<Exception> error = new AtomicReference<Exception>();
        CountdownEvent latch = new CountdownEvent(2);
        SelfSignedCertificateBundle cert = CachedSelfSignedCertificate();
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(LocalIoHandler.NewFactory());
        SslProvider sslProvider = ServerSslProvider();
        SslContext context = SslContextBuilder.ForServer(cert.Key(), cert.Cert())
                .SslProvider(sslProvider)
                .Protocols(Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_2)
                .Build();

        try
        {
            ServerBootstrap sb = new ServerBootstrap();
            sb.Group(group).Channel<LocalServerChannel>()
                    .ChildHandler(new ServerInitializer(context, error, latch));
            IChannel channel = await sb.BindAsync(new LocalAddress(GetType()), TestContext.Current.CancellationToken);

            SslContext clientContext = SslContextBuilder.ForClient()
                    .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                    .SslProvider(SslProvider.JDK)
                    .Protocols(Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_2)
                    .Build();

            Bootstrap bootstrap = new Bootstrap();
            bootstrap.Group(group).Channel<LocalChannel>()
                    .Handler(new ClientInitializer(clientContext, error, latch));

            IChannel clientChannel = await bootstrap.ConnectAsync(channel.LocalAddress,
                    TestContext.Current.CancellationToken);
            latch.Wait(TestContext.Current.CancellationToken);
            await clientChannel.CloseAsync(TestContext.Current.CancellationToken);
            await channel.CloseAsync(TestContext.Current.CancellationToken);
            VerifyResult(error);
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
            ReferenceCountUtil.Release(context);
        }
    }

    private sealed class ServerInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslContext context;
        private readonly AtomicReference<Exception> error;
        private readonly CountdownEvent latch;

        internal ServerInitializer(SslContext context, AtomicReference<Exception> error, CountdownEvent latch)
        {
            this.context = context;
            this.error = error;
            this.latch = latch;
        }

        protected override void InitChannel(IChannel ch)
        {
            SslHandler handler = context.NewHandler(ch.Alloc());
            handler.SetHandshakeTimeoutMillis(0);
            ch.Pipeline.AddLast(handler);
            ch.Pipeline.AddLast(new RenegotiatingHandler(error, latch));
        }
    }

    private sealed class RenegotiatingHandler : ChannelInboundHandlerAdapter
    {
        private readonly AtomicReference<Exception> error;
        private readonly CountdownEvent latch;
        private bool renegotiate;

        internal RenegotiatingHandler(AtomicReference<Exception> error, CountdownEvent latch)
        {
            this.error = error;
            this.latch = latch;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ReferenceCountUtil.Release(msg);
        }

        public override void UserEventTriggered(
                IChannelHandlerContext ctx, object evt)
        {
            if (!renegotiate && evt is SslHandshakeCompletionEvent)
            {
                SslHandshakeCompletionEvent @event = (SslHandshakeCompletionEvent) evt;

                if (@event.IsSuccess())
                {
                    SslHandler handler = ctx.Pipeline.Get<SslHandler>();

                    renegotiate = true;
                    _ = handler.Renegotiate().ContinueWith(future =>
                    {
                        if (!future.IsCompletedSuccessfully)
                        {
                            error.CompareAndSet(null, CauseOf(future));
                            _ = ctx.CloseAsync();
                        }

                        CountDown(latch);
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
                else
                {
                    error.CompareAndSet(null, @event.Cause());
                    CountDown(latch);

                    _ = ctx.CloseAsync();
                }
            }
        }
    }

    private sealed class ClientInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslContext clientContext;
        private readonly AtomicReference<Exception> error;
        private readonly CountdownEvent latch;

        internal ClientInitializer(SslContext clientContext, AtomicReference<Exception> error, CountdownEvent latch)
        {
            this.clientContext = clientContext;
            this.error = error;
            this.latch = latch;
        }

        protected override void InitChannel(IChannel ch)
        {
            SslHandler handler = clientContext.NewHandler(ch.Alloc());
            handler.SetHandshakeTimeoutMillis(0);
            ch.Pipeline.AddLast(handler);
            ch.Pipeline.AddLast(new ClientHandshakeHandler(error, latch));
        }
    }

    private sealed class ClientHandshakeHandler : ChannelInboundHandlerAdapter
    {
        private readonly AtomicReference<Exception> error;
        private readonly CountdownEvent latch;

        internal ClientHandshakeHandler(AtomicReference<Exception> error, CountdownEvent latch)
        {
            this.error = error;
            this.latch = latch;
        }

        public override void UserEventTriggered(
                IChannelHandlerContext ctx, object evt)
        {
            if (evt is SslHandshakeCompletionEvent)
            {
                SslHandshakeCompletionEvent @event = (SslHandshakeCompletionEvent) evt;
                if (!@event.IsSuccess())
                {
                    error.CompareAndSet(null, @event.Cause());
                    _ = ctx.CloseAsync();
                }

                CountDown(latch);
            }
        }
    }

    protected abstract SslProvider ServerSslProvider();

    // CLR: throw cause rethrows the Throwable with its own stack trace; ExceptionDispatchInfo.Throw does the same for an
    // Exception (https://learn.microsoft.com/dotnet/api/system.runtime.exceptionservices.exceptiondispatchinfo.throw), as
    // SslHandler rethrows.
    protected virtual void VerifyResult(AtomicReference<Exception> error)
    {
        Exception cause = error.Get();
        if (cause != null)
        {
            ExceptionDispatchInfo.Throw(cause);
        }
    }

    // ---- CLR-only helpers -----------------------------------------------------------------------------------------

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

    // CLR: CachedSelfSignedCertificate.getCachedCertificate() returns a cached SelfSignedCertificate; both are
    // @Deprecated test units that are not ported ("@deprecated Use the {@code CertificateBuilder} from {@code
    // netty-pkitesting} instead", recorded by the lead, docs/handler-ssl-design.md), so the nested
    // SelfSignedCertificateBundle caches the X509Bundle that SelfSignedCertificate's
    // CertificateBuilderCertGenerator builds by default: a self-signed certificate authority "CN=localhost" with
    // an RSA 2048 key. Its Cert() is the certificate and Key() the key, which is the BCL key object of the
    // bundle's key pair.
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

        internal SelfSignedCertificateBundle(X509Bundle bundle)
        {
            this.bundle = bundle;
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
