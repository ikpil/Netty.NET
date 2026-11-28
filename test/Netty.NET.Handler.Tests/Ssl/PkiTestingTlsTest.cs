/*
 * Copyright 2025 The Netty Project
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
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Handler.Ssl;
using Netty.NET.PkiTesting;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: SslProvider.OPENSSL is library-not-ported (netty-tcnative; owner decision 2026-11-24, docs/handler-ssl-design.md),
// so classicalAlgorithms() keeps the JDK provider (SslStream) and drops the OpenSsl rows. @ParameterizedTest with
// @MethodSource is [Theory] with [MemberData], and @EnabledForJreRange/@EnabledIf conditions are Assert.SkipUnless, as
// the pkitesting tests skip an algorithm the BCL does not support (docs/pkitesting-porting.md). X509Bundle's
// toKeyManagerFactory()/toTrustManagerFactory() are its SslStreamCertificateContext/X509ChainPolicy (pkitesting), which
// SslContextBuilder.ForServer(SslStreamCertificateContext) and TrustManager(X509ChainPolicy) take (ssl context wave);
// serverName(new SNIHostName("localhost")) is ServerName("localhost"), the SNI host name that SslStream sends as
// TargetHost. Promise<SslHandshakeCompletionEvent> is a TaskCompletionSource (setSuccess/setFailure/tryFailure are
// TrySet*), sync() an await, and the bootstrap API is asynchronous; the anonymous handlers are named nested classes.
public class PkiTestingTlsTest
{

    public static IEnumerable<object[]> ClassicalAlgorithms()
    {
        List<SslProvider> providers = new List<SslProvider>();

        if (SslProvider.IsTlsv13Supported(SslProvider.JDK))
        {
            providers.Add(SslProvider.JDK);
        }

        List<object[]> args = new List<object[]>();

        foreach (SslProvider provider in providers)
        {
            List<CertificateBuilder.Algorithm> algs =  new List<CertificateBuilder.Algorithm>();
            algs.Add(CertificateBuilder.Algorithm.rsa2048);
            algs.Add(CertificateBuilder.Algorithm.ecp256);

            foreach (CertificateBuilder.Algorithm alg in algs)
            {
                args.Add(new object[] { provider, alg });
            }
        }

        return args;
    }

    // CLR: interoperabilityParams() only feeds the CLR-inapplicable x25519MLKEM768Interoperability (see below), so it is
    // not ported.

    /**
     * A TLS connection with just classical algorithms.
     */
    [Theory]
    [MemberData(nameof(ClassicalAlgorithms))]
    public async Task ConnectWithClassicalAlgorithms(SslProvider provider, CertificateBuilder.Algorithm algorithm)
    {
        X509Bundle cert = new CertificateBuilder()
                .Algorithm_(algorithm)
                .SetIsCertificateAuthority(true)
                .Subject("CN=localhost")
                .BuildSelfSigned();

        SslContext serverContext = SslContextBuilder.ForServer(cert.ToKeyManagerFactory())
                .SslProvider(provider)
                .Build();

        SslContext clientContext = SslContextBuilder.ForClient()
                .TrustManager(cert.ToTrustManagerFactory())
                .SslProvider(provider)
                .ServerName("localhost")
                .Protocols("TLSv1.3")
                .Build();

        await TestTlsConnection(serverContext, clientContext);
    }

    // CLR: @EnabledForJreRange(min = JAVA_15) enables the test where the JDK has EdDSA; the BCL has no Ed25519 key type
    // (CertificateBuilder.Algorithm.ed25519.IsSupported() is false, docs/pkitesting-porting.md), so the test is skipped
    // while that is so.
    [Fact]
    public async Task ConnectWithEd25519()
    {
        Assert.SkipUnless(CertificateBuilder.Algorithm.ed25519.IsSupported(),
                "ed25519 is not supported by the BCL on this platform");
        X509Bundle cert = new CertificateBuilder()
                .Algorithm_(CertificateBuilder.Algorithm.ed25519)
                .SetIsCertificateAuthority(true)
                .Subject("CN=localhost")
                .BuildSelfSigned();

        // We currently don't support Ed25519 or Ed448 with the OPENSSL provider,
        // so to use those algorithms we have to use the JDK provider.
        SslProvider provider = SslProvider.JDK;

        SslContext serverContext = SslContextBuilder.ForServer(cert.ToKeyManagerFactory())
                .SslProvider(provider)
                .Build();

        SslContext clientContext = SslContextBuilder.ForClient()
                .TrustManager(cert.ToTrustManagerFactory())
                .SslProvider(provider)
                .ServerName("localhost")
                .Protocols("TLSv1.3")
                .Build();

        await TestTlsConnection(serverContext, clientContext);
    }

    // CLR: peerSupportedSignatureAlgorithmsContainEd25519, connectWithX25519MLKEM768 and x25519MLKEM768Interoperability
    // are CLR-inapplicable: @EnabledIf("isBoringSSLAvailable") runs them only with the BoringSSL flavour of netty-tcnative
    // (SslProvider.OPENSSL servers, OpenSslContextOption.GROUPS, OpenSslParametersUtil.setNamesGroups), which is
    // library-not-ported, and the first also reads the JSSE ExtendedSSLSession of an X509ExtendedKeyManager, which
    // SslStream does not expose. isBoringSSLAvailable() is not ported with them. Their original comments:
    //
    // peerSupportedSignatureAlgorithmsContainEd25519:
    /**
     * The OPENSSL provider should report Ed25519 in the peer's supported signature algorithms when the client
     * offers it, so key managers can see what the client actually supports.
     * <p>
     * This is currently limited to BoringSSL, because stock OpenSSL has no combined signature-and-hash NID for them.
     */
    // The JDK client offers ed25519 in its signature_algorithms extension by default.
    //
    // connectWithX25519MLKEM768:
    /**
     * A TLS connection using the X25519MLKEM768 hybrid classical-and-quantum-safe key exchange.
     * This protects the ephemeral TLS session key from harvest-now-decrypt-later attacks.
     * <p>
     * The ephemeral session key is used for the symmetric encryption algorithm.
     * To make that quantum safe, we just need to double the bit-width, from AES-128 to AES-256.
     */
    //
    // x25519MLKEM768Interoperability:
    /**
     * It is possible to enable the X25519MLKEM768 hybrid classical-and-quantum-safe key exchange in an interoperable
     * manner.
     * This way, we can allow the classical algorithms to be used as fallback if the peer doesn't support the hybrid
     * quantum safe key exchange.
     * This allows us to gradually introduce quantum safety into complex systems, without forcing all clients and
     * servers to upgrade at the same time.
     */

    // CLR: the String[] groups parameter and its branches set the named groups through the SSLParameters of
    // handler.engine() and OpenSslParametersUtil.setNamesGroups (library-not-ported); only the CLR-inapplicable
    // connectWithX25519MLKEM768 passes groups, so the helper has no groups parameter. The OpenSslSession named-group check
    // of the client handler has no counterpart either (no OpenSslSession). group.shutdownGracefully(10, 1000,
    // MILLISECONDS) takes TimeSpans.
    private async Task TestTlsConnection(SslContext serverContext, SslContext clientContext)
    {
        MultiThreadIoEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());
        LocalAddress serverAddress = new LocalAddress(GetType());

        IChannel serverChannel = null;
        IChannel clientChannel = null;

        try
        {
            serverChannel = await new ServerBootstrap()
                    .Channel<LocalServerChannel>()
                    .ChildHandler(new ServerInitializer(serverContext))
                    .Group(group)
                    .BindAsync(serverAddress, TestContext.Current.CancellationToken);

            TaskCompletionSource<SslHandshakeCompletionEvent> promise =
                    new TaskCompletionSource<SslHandshakeCompletionEvent>(
                            TaskCreationOptions.RunContinuationsAsynchronously);

            clientChannel = await new Bootstrap()
                    .Channel<LocalChannel>()
                    .Group(group)
                    .Handler(new ClientInitializer(clientContext, promise))
                    .ConnectAsync(serverAddress, TestContext.Current.CancellationToken);

            await promise.Task;
        }
        finally
        {
            if (clientChannel != null)
            {
                _ = clientChannel.CloseAsync(TestContext.Current.CancellationToken);
            }

            if (serverChannel != null)
            {
                _ = serverChannel.CloseAsync(TestContext.Current.CancellationToken);
            }

            if (clientChannel != null)
            {
                await clientChannel.CloseFuture;
            }

            if (serverChannel != null)
            {
                await serverChannel.CloseFuture;
            }

            await group.ShutdownGracefullyAsync(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(1000));
            // Release contexts created for this test to avoid leak failures with OPENSSL_REFCNT.
            ReferenceCountUtil.Release(clientContext);
            ReferenceCountUtil.Release(serverContext);
        }
    }

    private sealed class ServerInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslContext serverContext;

        internal ServerInitializer(SslContext serverContext)
        {
            this.serverContext = serverContext;
        }

        protected override void InitChannel(IChannel ch)
        {
            SslHandler handler = serverContext.NewHandler(ch.Alloc());
            ch.Pipeline.AddLast(handler);
        }
    }

    private sealed class ClientInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslContext clientContext;
        private readonly TaskCompletionSource<SslHandshakeCompletionEvent> promise;

        internal ClientInitializer(SslContext clientContext, TaskCompletionSource<SslHandshakeCompletionEvent> promise)
        {
            this.clientContext = clientContext;
            this.promise = promise;
        }

        protected override void InitChannel(IChannel ch)
        {
            SslHandler handler = clientContext.NewHandler(ch.Alloc(), "localhost", 0);
            ch.Pipeline
                    .AddLast(handler)
                    .AddLast(new HandshakeResultHandler(promise));
        }
    }

    private sealed class HandshakeResultHandler : ChannelInboundHandlerAdapter
    {
        private readonly TaskCompletionSource<SslHandshakeCompletionEvent> promise;

        internal HandshakeResultHandler(TaskCompletionSource<SslHandshakeCompletionEvent> promise)
        {
            this.promise = promise;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is SslHandshakeCompletionEvent)
            {
                SslHandshakeCompletionEvent shce = (SslHandshakeCompletionEvent) evt;
                if (shce.IsSuccess())
                {
                    promise.SetResult(shce);
                }
                else
                {
                    promise.SetException(shce.Cause());
                }

                return;
            }

            base.UserEventTriggered(ctx, evt);
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (!promise.TrySetException(cause))
            {
                ctx.FireExceptionCaught(cause);
            }
        }
    }
}
