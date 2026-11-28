/*
 * Copyright 2016 The Netty Project
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
using System.Net.Security;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Ssl;
using Netty.NET.PkiTesting;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: SslProvider.values() minus the OpenSSL providers when OpenSsl is unavailable: OpenSSL (netty-tcnative) is
// library-not-ported (owner decision 2026-11-24, docs/handler-ssl-design.md), so OpenSsl.isAvailable() is always false
// and parameters() keeps the one (JDK, JDK) pair. @ParameterizedTest with @MethodSource is [Theory] with [MemberData];
// @Timeout(30000 ms) is the Timeout of the [Theory], which xUnit applies to async tests, so the test returns a Task (as
// CloseNotifyTest does). The local transport is Netty.NET.Transport.Channels.Local; bind(...)/connect(...)/close()
// .syncUninterruptibly() await BindAsync/ConnectAsync/CloseAsync (as FlowControlHandlerTest waits for them), and
// shutdownGracefully() is ShutdownGracefullyAsync(). Promise<String> from group.next().newPromise() is a
// TaskCompletionSource<string> (docs/transport-porting.md) and getNow() its Result once completed. The anonymous
// ChannelInitializer and Mapping are a named nested class and a Func<string, SslContext> (SniHandler's Mapping,
// src/Netty.NET.Handler/Ssl/SniHandler.cs). CachedSelfSignedCertificate (a cache of the @Deprecated SelfSignedCertificate,
// not ported) is a pkitesting bundle of a self-signed "CN=localhost" RSA 2048 certificate (see SslContextBuilderTest).
// new SslHandler(sslClientContext.newEngine(alloc, sniHostName, -1)) is sslClientContext.newHandler(alloc, sniHostName,
// -1) (owner decision 2: SslContext.newEngine is not ported; the handler builds its own SslStream with that host as the
// SNI name, src/Netty.NET.Handler/Ssl/JdkSslContext.cs ConfigureSSLParameters).
//
// testSniSNIMatcherMatchesClient and testSniSNIMatcherDoesNotMatchClient are not ported: they run
// SniClientJava8TestUtil.testSniClient, whose subject is an SNIMatcher in the server engine's SSLParameters, which SslStream
// has no counterpart for (see SniClientJava8TestUtil); they are recorded as CLR-inapplicable in
// docs/handler-porting-manifest.json.
public class SniClientTest
{
    public static IEnumerable<object[]> Parameters()
    {
        List<SslProvider> providers = new List<SslProvider>
        {
            SslProvider.JDK, SslProvider.OPENSSL, SslProvider.OPENSSL_REFCNT,
        };
        providers.Remove(SslProvider.OPENSSL);
        providers.Remove(SslProvider.OPENSSL_REFCNT);

        List<object[]> @params = new List<object[]>();

        foreach (SslProvider sp in providers)
        {
            foreach (SslProvider cp in providers)
            {
                @params.Add(new object[] { sp, cp });
            }
        }

        return @params;
    }

    // CLR: the OPENSSL branch (a key file context when OpenSSL cannot use a KeyManagerFactory) is unreachable, as
    // OPENSSL is not a parameter (see the class comment); the KeyManagerFactory branch is the JDK one. The
    // KeyManagerFactory of newSniX509KeyManagerFactory is the server KeyManager callback (see SniClientJava8TestUtil), so
    // forServer(kmf) is ForServer(ServerCertificateSelectionCallback). getHandshakeSession() is the SSLSession "being
    // constructed during a SSL/TLS/DTLS handshake" and null when the engine "is not currently handshaking"
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/javax/net/ssl/SSLEngine.html); SslStream has no
    // handshake session, so "done with handshaking" is SslStream.IsAuthenticated. getUseClientMode() is !IsServer and
    // getSession() the SslStream itself (SniClientJava8TestUtil.AssertSSLSession).
    [Theory(Timeout = 30000)]
    [MemberData(nameof(Parameters))]
    public async Task TestSniClient(SslProvider sslServerProvider, SslProvider sslClientProvider)
    {
        string sniHostName = "sni.netty.io";
        LocalAddress address = new LocalAddress(typeof(SniClientTest));
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());
        X509Bundle cert = CachedSelfSignedCertificate();
        SslContext sslServerContext = null;
        SslContext sslClientContext = null;

        IChannel sc = null;
        IChannel cc = null;

        try
        {
            if ((sslServerProvider == SslProvider.OPENSSL || sslServerProvider == SslProvider.OPENSSL_REFCNT))
            {
                sslServerContext = SslContextBuilder.ForServer(cert.ToTempCertChainPem(), cert.ToTempPrivateKeyPem())
                                                    .SslProvider(sslServerProvider)
                                                    .Build();
            }
            else
            {
                // The used OpenSSL version does support a KeyManagerFactory, so use it.
                ServerCertificateSelectionCallback kmf =
                        SniClientJava8TestUtil.NewSniX509KeyManagerFactory(cert, sniHostName);

               sslServerContext = SslContextBuilder.ForServer(kmf)
                                                   .SslProvider(sslServerProvider)
                                                   .Build();
            }

            SslContext finalContext = sslServerContext;
            TaskCompletionSource<string> promise =
                    new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            ServerBootstrap sb = new ServerBootstrap();
            sc = await sb.Group(group).Channel<LocalServerChannel>().ChildHandler(new ServerInitializer(input =>
            {
                promise.SetResult(input);
                return finalContext;
            })).BindAsync(address, TestContext.Current.CancellationToken);

            RemoteCertificateValidationCallback tmf = SniClientJava8TestUtil.NewSniX509TrustmanagerFactory(sniHostName);
            sslClientContext = SslContextBuilder.ForClient().TrustManager(tmf)
                                                     .SslProvider(sslClientProvider).Build();
            Bootstrap cb = new Bootstrap();

            SslHandler handler = sslClientContext.NewHandler(UnpooledByteBufAllocator.Default, sniHostName, -1);
            cc = await cb.Group(group).Channel<LocalChannel>().Handler(handler)
                    .ConnectAsync(address, TestContext.Current.CancellationToken);
            Assert.Equal(sniHostName, await promise.Task);

            // After we are done with handshaking getHandshakeSession() should return null.
            await handler.HandshakeFuture();
            Assert.True(handler.Engine().IsAuthenticated);

            SniClientJava8TestUtil.AssertSSLSession(
                    !handler.Engine().IsServer, handler.Engine(), sniHostName);
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

            ReferenceCountUtil.Release(sslServerContext);
            ReferenceCountUtil.Release(sslClientContext);

            _ = group.ShutdownGracefullyAsync();
        }
    }

    private sealed class ServerInitializer : ChannelInitializer<IChannel>
    {
        private readonly Func<string, SslContext> mapping;

        internal ServerInitializer(Func<string, SslContext> mapping)
        {
            this.mapping = mapping;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Pipeline.AddFirst(new SniHandler(mapping));
        }
    }

    private static readonly Lazy<X509Bundle> CACHED_CERTIFICATE = new Lazy<X509Bundle>(
            () => new CertificateBuilder()
                    .Subject("CN=localhost")
                    .Rsa2048()
                    .SetIsCertificateAuthority(true)
                    .BuildSelfSigned());

    private static X509Bundle CachedSelfSignedCertificate()
    {
        return CACHED_CERTIFICATE.Value;
    }
}
