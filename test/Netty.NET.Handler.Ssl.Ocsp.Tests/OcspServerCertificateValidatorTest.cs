/*
 * Copyright 2022 The Netty Project
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
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.PkiTesting;
using Netty.NET.Resolver.Dns;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;
using HttpVersion = Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Handler.Ssl.Ocsp.Tests;

// CLR: the Bouncy Castle builders are the CLR-only builders of OcspTestBuilders.cs (see OcspClientTest), and OCSPException
// is CryptographicException. ChannelFuture.sync() awaits the Task; channel() is the Task's result, closeFuture() the
// channel's CloseFuture Task and awaitUninterruptibly(timeout) Task.Wait(timeout). CountDownLatch is CountdownEvent and
// await(timeout) Wait(timeout). The anonymous ChannelInitializer classes are Transport's ActionChannelInitializer, and
// the anonymous handlers are nested classes. NetUtil.LOCALHOST4 is IPAddress.Loopback; InetSocketAddress is IPEndPoint.
// The checked "throws Exception" clauses have no counterpart. @Timeout(value = 60, unit = SECONDS) is the [Fact]
// Timeout in milliseconds. assertInstanceOf(OCSPException.class, x) is Assert.IsAssignableFrom<CryptographicException>,
// which also accepts a derived type.
public class OcspServerCertificateValidatorTest : AbstractOcspTest
{
    private const string OID_AUTHORITY_INFORMATION_ACCESS = "1.3.6.1.5.5.7.1.1";

    /**
     * Well within the 15 minutes of clock skew that the validator tolerates by default.
     */
    private const int SERVER_PAYLOAD_LENGTH = 8;
    private static readonly long FRESH_THIS_UPDATE_AGE_MILLIS = (long) TimeSpan.FromMinutes(1).TotalMilliseconds;

    /**
     * Far outside the 15 minutes of clock skew that the validator tolerates by default.
     */
    private static readonly long STALE_THIS_UPDATE_AGE_MILLIS = (long) TimeSpan.FromHours(2).TotalMilliseconds;

    /**
     * A response age that is unremarkable for a real CA, but far outside the clock skew tolerance.
     */
    private static readonly long OLD_THIS_UPDATE_AGE_MILLIS = (long) TimeSpan.FromDays(7).TotalMilliseconds;

    private static readonly long NEXT_UPDATE_AHEAD_MILLIS = (long) TimeSpan.FromHours(1).TotalMilliseconds;

    // CLR: the test needs the internet, as the Java test does. The anonymous SimpleChannelInboundHandler<Object> is the
    // nested ValidationEventHandler; AtomicBoolean is Common's. eventLoopGroup.shutdownGracefully() discards the Task.
    [Fact]
    public async Task ConnectUsingHttpAndValidateCertificateUsingOcspTest()
    {
        AtomicBoolean ocspStatus = new AtomicBoolean();
        IEventLoopGroup eventLoopGroup = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
        SslContext sslContext = SslContextBuilder.ForClient()
                .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                .Build();

        try
        {
            CountdownEvent latch = new CountdownEvent(1);

            Bootstrap bootstrap = new Bootstrap()
                    .Group(eventLoopGroup)
                    .Channel<NioSocketChannel>()
                    .Option(ChannelOption.TCP_NODELAY, true)
                    .Handler(new ActionChannelInitializer<ISocketChannel>(ch =>
                    {
                        IChannelPipeline pipeline = ch.Pipeline;
                        pipeline.AddLast(sslContext.NewHandler(ch.Alloc(), "apple.com", 443));
                        pipeline.AddLast(new OcspServerCertificateValidator(false, CreateDefaultTransport()));
                        pipeline.AddLast(new ValidationEventHandler(ocspStatus, latch));
                    }));

            IChannel channelFuture = await bootstrap.ConnectAsync("apple.com", 443, TestContext.Current.CancellationToken);

            // Wait for maximum of 1 minute for Ocsp validation to happen
            latch.Wait(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
            Assert.True(ocspStatus.Get());

            // Wait for Channel to be closed
            await channelFuture.CloseFuture.WaitAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            _ = eventLoopGroup.ShutdownGracefullyAsync();
            ReferenceCountUtil.Release(sslContext);
        }
    }

    private sealed class ValidationEventHandler : SimpleChannelInboundHandler<object>
    {
        private readonly AtomicBoolean ocspStatus;
        private readonly CountdownEvent latch;

        internal ValidationEventHandler(AtomicBoolean ocspStatus, CountdownEvent latch)
        {
            this.ocspStatus = ocspStatus;
            this.latch = latch;
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
        {
            // NOOP
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is OcspValidationEvent)
            {
                OcspValidationEvent @event = (OcspValidationEvent) evt;

                ocspStatus.Set(@event.Response().Status_() == OcspResponse.Status.VALID);
                _ = ctx.Channel.CloseAsync();
                latch.Signal();
            }
        }
    }

    /**
     * A revoked certificate is revoked regardless of its nextUpdate field.
     */
    [Fact(Timeout = 60000)]
    public async Task RevokedCertificateIsReportedWhenResponseHasNextUpdate()
    {
        ResponseSpec responseSpec = new ResponseSpec(
            true /*revoked*/, FRESH_THIS_UPDATE_AGE_MILLIS, NEXT_UPDATE_AHEAD_MILLIS);
        await ValidateCertificate(responseSpec, TestContext.Current.CancellationToken, outcome =>
        {
            Assert.True(1 == outcome.ocspRequests, "OCSP responder must be queried");
            Assert.True(outcome.verdictReceived, "Must deliver verdict to the application");
            Assert.Equal((object) OcspResponse.Status.REVOKED, outcome.status);
            Assert.True(outcome.channelClosedFuture.Wait(TimeSpan.FromSeconds(10)),
                "Channel must be closed for a REVOKED certificate");
            Assert.IsAssignableFrom<CryptographicException>(outcome.failure);
            Assert.Equal(0, outcome.bytesReceived);
        });
    }

    /**
     * With {@code closeAndThrowIfNotValid} disabled the application decides what to do with a revoked certificate,
     * so it must receive the verdict and all the bytes the peer sent, unmodified.
     */
    [Fact(Timeout = 60000)]
    public async Task RevokedCertificateDeliversAllBytesWhenNotClosing()
    {
        ResponseSpec responseSpec = new ResponseSpec(
            true /*revoked*/, FRESH_THIS_UPDATE_AGE_MILLIS, NEXT_UPDATE_AHEAD_MILLIS);
        await ValidateCertificate(responseSpec, false, TestContext.Current.CancellationToken, outcome =>
        {
            Assert.True(1 == outcome.ocspRequests, "OCSP responder must be queried");
            Assert.True(outcome.verdictReceived, "Must deliver verdict to the application");
            Assert.Equal((object) OcspResponse.Status.REVOKED, outcome.status);
            Assert.Null(outcome.failure);
            Assert.False(outcome.channelClosedFuture.IsCompleted, "Channel must not be closed");
            Assert.Equal(SERVER_PAYLOAD_LENGTH, outcome.bytesReceived);
        });
    }

    /**
     * The nextUpdate field is OPTIONAL (RFC 6960 section 4.2.1).
     * When it is omitted the validator must still tell the application what happened, either by firing an
     * {@link OcspValidationEvent} or by firing an exception.
     */
    [Fact(Timeout = 60000)]
    public async Task RevokedCertificateIsReportedWhenResponseHasNoNextUpdate()
    {
        await ValidateCertificate(new ResponseSpec(true, FRESH_THIS_UPDATE_AGE_MILLIS, null), TestContext.Current.CancellationToken, outcome =>
        {
            Assert.True(1 == outcome.ocspRequests, "OCSP responder must be queried");
            Assert.True(outcome.verdictReceived,
                "OCSP validation result was silently dropped: neither an OcspValidationEvent nor an exception " +
                    "was delivered for an OCSP response without 'nextUpdate'");
            // Whatever the verdict is, the revoked certificate must never be reported as valid.
            Assert.NotEqual((object) OcspResponse.Status.VALID, outcome.status);
            Assert.Equal(0, outcome.bytesReceived);
        });
    }

    /**
     * RFC 6960 section 4.2.2.1: an absent {@code nextUpdate} means newer information is available all the time,
     * so such a response is current rather than unusable.
     */
    [Fact(Timeout = 60000)]
    public async Task RecentResponseWithoutNextUpdateIsAccepted()
    {
        await ValidateCertificate(new ResponseSpec(false, FRESH_THIS_UPDATE_AGE_MILLIS, null), TestContext.Current.CancellationToken, outcome =>
        {
            Assert.True(1 == outcome.ocspRequests, "OCSP responder must be queried");
            Assert.True(outcome.verdictReceived, "Must deliver verdict to the application");
            Assert.Equal((object) OcspResponse.Status.VALID, outcome.status);
            Assert.Null(outcome.failure);
            Assert.Equal(SERVER_PAYLOAD_LENGTH, outcome.bytesReceived);
        });
    }

    /**
     * RFC 6960 section 3.2 (5): {@code thisUpdate} must be "sufficiently recent". With {@code nextUpdate} absent
     * it is the only bound on the age of the response, and so the only thing preventing replay of an old but
     * genuinely signed response.
     */
    [Fact(Timeout = 60000)]
    public async Task StaleResponseWithoutNextUpdateIsRejected()
    {
        await ValidateCertificate(new ResponseSpec(false, STALE_THIS_UPDATE_AGE_MILLIS, null), TestContext.Current.CancellationToken, outcome =>
        {
            Assert.True(1 == outcome.ocspRequests, "OCSP responder must be queried");
            Assert.True(!Equals(OcspResponse.Status.VALID, outcome.status),
                "Must not accept an out-of-date OCSP response without 'nextUpdate' as valid");
            Assert.True(outcome.failure != null, "No exception was delivered for an out-of-date OCSP response");
            Assert.True(outcome.channelClosedFuture.Wait(TimeSpan.FromSeconds(10)),
                "Channel was not closed for an out-of-date OCSP response");
            Assert.Equal(0, outcome.bytesReceived);
        });
    }

    /**
     * A present {@code nextUpdate} extends the validity interval past {@code thisUpdate}, which is what keeps the
     * age bound from rejecting the long-lived responses that real CAs serve.
     */
    [Fact(Timeout = 60000)]
    public async Task OldResponseWithValidNextUpdateIsAccepted()
    {
        ResponseSpec responseSpec = new ResponseSpec(
            false, OLD_THIS_UPDATE_AGE_MILLIS, NEXT_UPDATE_AHEAD_MILLIS);
        await ValidateCertificate(responseSpec, TestContext.Current.CancellationToken, outcome =>
        {
            Assert.True(1 == outcome.ocspRequests, "OCSP responder must be queried");
            Assert.True(outcome.verdictReceived, "Must deliver verdict to the application");
            Assert.True(Equals(OcspResponse.Status.VALID, outcome.status),
                "A response that is still within its nextUpdate window was not accepted");
            Assert.Null(outcome.failure);
            Assert.Equal(SERVER_PAYLOAD_LENGTH, outcome.bytesReceived);
        });
    }

    /**
     * Performs a TLS handshake against a local server whose certificate status is served by a local OCSP
     * responder, using {@link OcspServerCertificateValidator} with {@code closeAndThrowIfNotValid} enabled,
     * and collects what the application was told.
     *
     * @param spec describes the OCSP response the responder will serve
     * @param assertions the assertions to verify on the {@link ValidationOutcome}
     */
    private Task ValidateCertificate(ResponseSpec spec, CancellationToken token, Action<ValidationOutcome> assertions)
    {
        return ValidateCertificate(spec, true, token, assertions);
    }

    // CLR: Consumer<ValidationOutcome> is Action<ValidationOutcome>, and the CancellationToken parameter carries the
    // test's TestContext.Current.CancellationToken to the binds, the connect and the waits. The busy wait sleeps with
    // Task.Delay, and System.nanoTime() deadlines are a Stopwatch. AtomicReference/AtomicInteger are Common's. sync()
    // and syncUninterruptibly() await the Tasks; the SslContexts are released with ReferenceCountUtil, as in handler's
    // tests.
    private async Task ValidateCertificate(ResponseSpec spec, bool closeAndThrowIfNotValid, CancellationToken token,
                                           Action<ValidationOutcome> assertions)
    {
        X509Bundle issuer = new CertificateBuilder()
                .Algorithm_(CertificateBuilder.Algorithm.ecp256)
                .Subject("CN=OcspIssuerCA")
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();

        IoTransport ioTransport = CreateDefaultTransport();
        DnsNameResolver dnsNameResolver = OcspServerCertificateValidator.InvokeCreateDefaultResolver(ioTransport);
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(2, NioIoHandler.NewFactory());
        SslContext serverSslContext = null;
        SslContext clientSslContext = null;
        IChannel ocspResponder = null;
        IChannel tlsServer = null;
        IChannel client = null;
        AtomicReference<IChannel> acceptedServerChannel = new AtomicReference<IChannel>();
        try
        {
            AtomicReference<byte[]> ocspResponseBytes = new AtomicReference<byte[]>();
            AtomicInteger ocspRequests = new AtomicInteger();

            // Local OCSP responder. It always answers with the response bytes prepared below.
            ocspResponder = await new ServerBootstrap()
                    .Group(group)
                    .Channel<NioServerSocketChannel>()
                    .ChildHandler(new ActionChannelInitializer<ISocketChannel>(ch =>
                    {
                        IChannelPipeline pipeline = ch.Pipeline;
                        pipeline.AddLast(new HttpServerCodec());
                        pipeline.AddLast(new HttpObjectAggregator(64 * 1024));
                        pipeline.AddLast(new OcspResponderHandler(ocspRequests, ocspResponseBytes));
                    }))
                    .BindAsync(IPAddress.Loopback, 0, token);
            int ocspPort = ((IPEndPoint) ocspResponder.LocalAddress).Port;

            // The server certificate points at our local OCSP responder using the AIA extension.
            byte[] aia = AuthorityInformationAccess.OcspUri(
                    "http://" + IPAddress.Loopback + ':' + ocspPort + '/');
            X509Bundle serverBundle = new CertificateBuilder()
                    .Algorithm_(CertificateBuilder.Algorithm.ecp256)
                    .Subject("CN=localhost")
                    .AddSanDnsName("localhost")
                    .AddExtendedKeyUsageServerAuth()
                    .AddExtensionOctetString(OID_AUTHORITY_INFORMATION_ACCESS, false, aia)
                    .BuildIssuedBy(issuer);

            ocspResponseBytes.Set(OcspResponse_(issuer, serverBundle.GetCertificate(), spec));

            serverSslContext = SslContextBuilder.ForServer((AsymmetricAlgorithm) serverBundle.GetKeyPair(),
                    serverBundle.GetCertificatePathWithRoot()).Build();
            SslContext serverSslCtx = serverSslContext;
            tlsServer = await new ServerBootstrap()
                    .Group(group)
                    .Channel<NioServerSocketChannel>()
                    .ChildHandler(new ActionChannelInitializer<ISocketChannel>(ch =>
                    {
                        acceptedServerChannel.Set(ch);
                        ch.Pipeline.AddLast(serverSslCtx.NewHandler(ch.Alloc()));
                        // Write back some bytes so we can validate if these are discarded or not.
                        _ = ch.WriteAndFlushAsync(ch.Alloc().Buffer(SERVER_PAYLOAD_LENGTH)
                                .WriteZero(SERVER_PAYLOAD_LENGTH));
                    }))
                    .BindAsync(IPAddress.Loopback, 0, token);
            int tlsPort = ((IPEndPoint) tlsServer.LocalAddress).Port;

            clientSslContext = SslContextBuilder.ForClient()
                    .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                    .Build();
            SslContext clientSslCtx = clientSslContext;

            ValidationOutcome outcome = new ValidationOutcome();
            CountdownEvent verdictLatch = new CountdownEvent(1);
            CountdownEvent sslEventLatch = new CountdownEvent(1);
            client = await new Bootstrap()
                    .Group(group)
                    .Channel<NioSocketChannel>()
                    .Option(ChannelOption.TCP_NODELAY, true)
                    .Handler(new ActionChannelInitializer<ISocketChannel>(ch =>
                    {
                        IChannelPipeline pipeline = ch.Pipeline;
                        pipeline.AddLast(clientSslCtx.NewHandler(ch.Alloc(), "localhost", tlsPort));
                        pipeline.AddLast(new OcspServerCertificateValidator(
                                closeAndThrowIfNotValid, false, ioTransport, dnsNameResolver));
                        pipeline.AddLast(new OutcomeHandler(outcome, verdictLatch, sslEventLatch));
                    }))
                    .ConnectAsync(IPAddress.Loopback, tlsPort, token);
            outcome.verdictReceived = verdictLatch.Wait(TimeSpan.FromSeconds(20));
            outcome.channelClosedFuture = client.CloseFuture;
            // Wait until the channel was closed or the payload of the server was delivered, so assertions on the
            // received bytes are not racy.
            Stopwatch deadline = Stopwatch.StartNew();
            while (!outcome.channelClosedFuture.IsCompleted && outcome.bytesReceived < SERVER_PAYLOAD_LENGTH &&
                    deadline.Elapsed < TimeSpan.FromSeconds(10))
            {
                await Task.Delay(10, token);
            }
            outcome.ocspRequests = ocspRequests.Get();
            Assert.True(sslEventLatch.Wait(TimeSpan.FromSeconds(10)));
            assertions(outcome);
        }
        finally
        {
            if (client != null)
            {
                await client.CloseAsync();
            }
            // The server-side child channel accepted for the TLS connection is independent of the listening
            // socket, so closing 'tlsServer' below does not close it. It must be closed explicitly here so that
            // its SSLEngine (and the key material cached by 'serverSslContext') is released deterministically,
            // instead of racing the tight timeout on the 'group' shutdown further down.
            IChannel acceptedChannel = acceptedServerChannel.Get();
            if (acceptedChannel != null)
            {
                await acceptedChannel.CloseAsync();
            }
            if (tlsServer != null)
            {
                await tlsServer.CloseAsync();
            }
            if (ocspResponder != null)
            {
                await ocspResponder.CloseAsync();
            }
            dnsNameResolver.Dispose();
            ReferenceCountUtil.Release(clientSslContext);
            ReferenceCountUtil.Release(serverSslContext);
            await group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(1));
            RemoveFromCurrentUserCaStore(issuer.GetCertificate());
        }
    }

    // CLR: on Windows, SslStreamCertificateContext.Create adds the certificates passed with the server certificate to
    // the current user's intermediate certification authority store (CurrentUser\CA), with or without offline, so that
    // SChannel can send them, and nothing removes them. Every run left its throwaway "CN=OcspIssuerCA" there; once
    // dozens with that subject and different keys had accumulated, X509Chain.Build of the next server certificate
    // (which, as in the source, has no authority key identifier) threw "An unknown chain building error occurred" in
    // the server context (measured 2026-11-27: 71 such certificates, the 6 tests of this class that build a server
    // context failing; a unique issuer subject built). The source's JSSE context writes to no store, so the test
    // removes the issuer it created; its random key makes the thumbprint unique to this run.
    private static void RemoveFromCurrentUserCaStore(X509Certificate2 certificate)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        using X509Store store = new X509Store(StoreName.CertificateAuthority, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);
        foreach (X509Certificate2 found in store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint,
                                                                    false))
        {
            store.Remove(found);
            found.Dispose();
        }
    }

    // CLR: the anonymous SimpleChannelInboundHandler<FullHttpRequest> of the responder. ChannelFutureListener.CLOSE on
    // the writeAndFlush Task closes the channel when the write completes.
    private sealed class OcspResponderHandler : SimpleChannelInboundHandler<IFullHttpRequest>
    {
        private readonly AtomicInteger ocspRequests;
        private readonly AtomicReference<byte[]> ocspResponseBytes;

        internal OcspResponderHandler(AtomicInteger ocspRequests, AtomicReference<byte[]> ocspResponseBytes)
        {
            this.ocspRequests = ocspRequests;
            this.ocspResponseBytes = ocspResponseBytes;
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, IFullHttpRequest msg)
        {
            ocspRequests.IncrementAndGet();
            IFullHttpResponse response = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1,
                    HttpResponseStatus.OK,
                    Unpooled.WrappedBuffer(ocspResponseBytes.Get()));
            response.Headers()
                    .Set(HttpHeaderNames.CONTENT_TYPE, "application/ocsp-response")
                    .Set(HttpHeaderNames.CONTENT_LENGTH,
                            response.Content.ReadableBytes);
            IChannel channel = ctx.Channel;
            _ = ctx.WriteAndFlushAsync(response).ContinueWith(_ => channel.CloseAsync(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of the client pipeline.
    private sealed class OutcomeHandler : ChannelInboundHandlerAdapter
    {
        private readonly ValidationOutcome outcome;
        private readonly CountdownEvent verdictLatch;
        private readonly CountdownEvent sslEventLatch;

        internal OutcomeHandler(ValidationOutcome outcome, CountdownEvent verdictLatch, CountdownEvent sslEventLatch)
        {
            this.outcome = outcome;
            this.verdictLatch = verdictLatch;
            this.sslEventLatch = sslEventLatch;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is OcspValidationEvent)
            {
                outcome.status = ((OcspValidationEvent) evt).Response().Status_();
                CountDown(verdictLatch);
            }
            if (evt is SslHandshakeCompletionEvent)
            {
                CountDown(sslEventLatch);
            }
            ctx.FireUserEventTriggered(evt);
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (outcome.failure == null)
            {
                outcome.failure = cause;
            }
            CountDown(verdictLatch);
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is ByteBuf)
            {
                outcome.bytesReceived += ((ByteBuf) msg).ReadableBytes;
            }
            ReferenceCountUtil.Release(msg);
        }

        // CLR: CountDownLatch.countDown() does nothing once the count is zero, while CountdownEvent.Signal() throws.
        private static void CountDown(CountdownEvent latch)
        {
            if (!latch.IsSet)
            {
                try
                {
                    latch.Signal();
                }
                catch (InvalidOperationException)
                {
                    // Another signal reached zero first.
                }
            }
        }
    }

    /**
     * Build a signed OCSP response for the given certificate, as described by the given {@link ResponseSpec}.
     */
    // CLR: ocspResponse is OcspResponse_: a method named OcspResponse would hide the OcspResponse type in this class, as
    // pkitesting's algorithm(Algorithm) is Algorithm_(Algorithm) (docs/pkitesting-porting.md).
    private static byte[] OcspResponse_(X509Bundle issuer, X509Certificate2 certificate,
                                        ResponseSpec spec)
    {
        X509Certificate2 issuerHolder = issuer.GetCertificate();
        CertificateId certificateId = CertificateId.Create(
                OcspObjectIdentifiers.IdSha1,
                issuerHolder, CertificateId.SerialNumberOf(certificate));

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset thisUpdate = now - TimeSpan.FromMilliseconds(spec.thisUpdateAgeMillis);
        DateTimeOffset? nextUpdate = spec.nextUpdateOffsetMillis == null ? null :
                now + TimeSpan.FromMilliseconds(spec.nextUpdateOffsetMillis.Value);
        object status = spec.revoked ?
                new RevokedStatus(now - TimeSpan.FromDays(1), null) : CertificateStatus.GOOD;

        BasicOcspRespBuilder builder = new BasicOcspRespBuilder(new RespId(issuerHolder.SubjectName));
        builder.AddResponse(certificateId, status, thisUpdate, nextUpdate);

        ContentSigner signer = new ContentSigner("SHA256withECDSA",
                (AsymmetricAlgorithm) issuer.GetKeyPair());
        BasicOcspResp basicResponse = builder.Build(signer, null, now);
        return OcspRespBuilder.Build(OcspResponseStatus.Successful, basicResponse);
    }

    /**
     * Describes the OCSP response the test responder should serve.
     */
    private sealed class ResponseSpec
    {
        internal readonly bool revoked;
        internal readonly long thisUpdateAgeMillis;
        internal readonly long? nextUpdateOffsetMillis;

        /**
         * @param revoked                if {@code true} the certificate is reported as {@code REVOKED},
         *                               otherwise as {@code GOOD}
         * @param thisUpdateAgeMillis    how long ago {@code thisUpdate} is
         * @param nextUpdateOffsetMillis how far ahead of now {@code nextUpdate} is; a negative value puts it in
         *                               the past, and {@code null} omits the OPTIONAL field entirely
         */
        internal ResponseSpec(bool revoked, long thisUpdateAgeMillis, long? nextUpdateOffsetMillis)
        {
            this.revoked = revoked;
            this.thisUpdateAgeMillis = thisUpdateAgeMillis;
            this.nextUpdateOffsetMillis = nextUpdateOffsetMillis;
        }
    }

    // CLR: a Java enum field starts as null, while a C# enum field starts as its zero value, which is
    // OcspResponse.Status.VALID; so status holds the boxed OcspResponse.Status, null until a verdict arrives, and the
    // assertions compare boxed values (assertEquals/assertNotEquals use equals). Every field keeps its volatile
    // modifier. ChannelFuture is the channel's CloseFuture Task and Throwable is Exception.
    private sealed class ValidationOutcome
    {
        internal volatile object status;
        internal volatile Exception failure;
        internal volatile bool verdictReceived;
        internal volatile Task channelClosedFuture;
        internal volatile int ocspRequests;
        internal volatile int bytesReceived;
    }
}
