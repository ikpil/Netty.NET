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
using System.IO;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Ssl;
using Netty.NET.PkiTesting;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: the SSLEngine a context creates per connection is the SslClientAuthenticationOptions/SslServerAuthenticationOptions
// of the SslHandler that newHandler(...) builds (owner decision 2026-11-24, docs/handler-ssl-design.md: no SSLEngine
// facade; SslContext.newEngine is not ported), so each context.newEngine(alloc) is context.NewHandler(alloc) and the
// engine checks read that handler's options (see GetWantClientAuth). engine.closeInbound()/closeOutbound() and
// ReferenceCountUtil.release(engine) release an engine that never handshook; a handler that did not join a pipeline
// releases its SslStream with Engine().Dispose(), as SniHandler.replaceHandler releases one
// (src/Netty.NET.Handler/Ssl/SniHandler.cs). The JSSE/JCA types are their BCL counterparts (ssl design decision 3,
// src/Netty.NET.Handler/Ssl/SslContextBuilder.cs): PrivateKey is AsymmetricAlgorithm, X509Certificate X509Certificate2,
// the KeyManager the ServerCertificateSelectionCallback of a server or the LocalCertificateSelectionCallback of a client,
// the TrustManager a RemoteCertificateValidationCallback and SecureRandom a RandomNumberGenerator.
// CachedSelfSignedCertificate.getCachedCertificate() caches the @Deprecated SelfSignedCertificate (deprecated-not-ported,
// "@deprecated Use the {@code CertificateBuilder} from {@code netty-pkitesting} instead."); the tests use one cached
// Netty.NET.PkiTesting bundle of a self-signed "CN=localhost" RSA 2048 certificate, the defaults of
// SelfSignedCertificate(): certificate()/privateKey() are its PEM files (ToTempCertChainPem/ToTempPrivateKeyPem),
// cert() its certificate and key() its key pair (an RSA key, src/Netty.NET.PkiTesting/X509Bundle.cs). CharsetUtil.US_ASCII
// is Encoding.ASCII, ByteArrayInputStream is MemoryStream and Collections.singleton is a one-element array.
// IllegalArgumentException is ArgumentException.
//
// Not ported (recorded as CLR-inapplicable in docs/handler-porting-manifest.json): the tests that need OpenSSL
// (netty-tcnative), which is library-not-ported (owner decision 2026-11-24) so that SslProvider.OPENSSL cannot be built
// (src/Netty.NET.Handler/Ssl/SslContext.cs): testClientContextFromFileOpenssl, testClientContextOpenssl,
// testCombinedPemFileClientContextOpenssl, testKeyStoreTypeOpenssl, testServerContextFromFileOpenssl,
// testServerContextOpenssl, testContextFromManagersOpenssl, testInvalidCipherOpenSSL, testUnsupportedPrivateKeyFailsFastForServer
// and testUnsupportedPrivateKeyFailsFastForClient (with their helper testUnsupportedPrivateKeyFailsFast, which assume a
// BoringSSL or AWS-LC OpenSsl), and openSslCredentialApiIsNotAvailableWithJdkProvider, whose OpenSslCredential and
// SslContextBuilder.addCredential are library-not-ported. The original comments of testInvalidCipherOpenSSL:
// This may fail or not depending on the OpenSSL version used
// See https://github.com/openssl/openssl/issues/7196
// ok
public class SslContextBuilderTest
{

    [Fact]
    public void TestClientContextFromFileJdk()
    {
        TestClientContextFromFile(SslProvider.JDK);
    }

    [Fact]
    public void TestClientContextJdk()
    {
        TestClientContext(SslProvider.JDK);
    }

    [Fact]
    public void TestCombinedPemFileClientContextJdk()
    {
        TestServerContextWithCombinedCertAndKeyInPem(SslProvider.JDK);
    }

    [Fact]
    public void TestKeyStoreTypeJdk()
    {
        TestKeyStoreType(SslProvider.JDK);
    }

    [Fact]
    public void TestServerContextFromFileJdk()
    {
        TestServerContextFromFile(SslProvider.JDK);
    }

    [Fact]
    public void TestServerContextJdk()
    {
        TestServerContext(SslProvider.JDK);
    }

    [Fact]
    public void TestContextFromManagersJdk()
    {
        TestContextFromManagers(SslProvider.JDK);
    }

    private void TestServerContextWithCombinedCertAndKeyInPem(SslProvider provider)
    {
        string pem = "-----BEGIN CERTIFICATE-----\n" +
                     "MIIB1jCCAX0CCQDq4PSOirh7MDAJBgcqhkjOPQQBMHIxCzAJBgNVBAYTAlVTMQsw\n" +
                     "CQYDVQQIDAJDQTEMMAoGA1UEBwwDRm9vMQwwCgYDVQQKDANCYXIxDDAKBgNVBAsM\n" +
                     "A0JhejEQMA4GA1UEAwwHQmFyLmNvbTEaMBgGCSqGSIb3DQEJARYLZm9vQGJhci5j\n" +
                     "b20wHhcNMjIxMDAyMTYzODAyWhcNMjIxMjAxMTYzODAyWjB2MQswCQYDVQQGEwJV\n" +
                     "UzELMAkGA1UECAwCQ0ExDDAKBgNVBAcMA0ZvbzEMMAoGA1UECgwDQmFyMQwwCgYD\n" +
                     "VQQLDANiYXoxFDASBgNVBAMMC2Jhci5iYXIuYmF6MRowGAYJKoZIhvcNAQkBFgtm\n" +
                     "b29AYmFyLmNvbTBZMBMGByqGSM49AgEGCCqGSM49AwEHA0IABHiEmjPEqQbqXYMB\n" +
                     "nAPOv24rJf6MhTwHB0QC1suZ9q9XFUkalnqGryqf/emHs81RsXWKz4sCsbIJkmHz\n" +
                     "H8HYhmkwCQYHKoZIzj0EAQNIADBFAiBCgzxZ5qviemPdejt2WazSgwNJTbirzoQa\n" +
                     "FMv2XFTTCwIhANS3fZ8BulbYkdRWVEFwm2FGotqLfC60JA/gg/brlWSP\n" +
                     "-----END CERTIFICATE-----\n" +
                     "-----BEGIN EC PRIVATE KEY-----\n" +
                     "MHcCAQEEIF8RlaD0JX8u2Lryq1+AbYfDaTBPJnPSA8+N2L12YuuUoAoGCCqGSM49\n" +
                     "AwEHoUQDQgAEeISaM8SpBupdgwGcA86/bisl/oyFPAcHRALWy5n2r1cVSRqWeoav\n" +
                     "Kp/96YezzVGxdYrPiwKxsgmSYfMfwdiGaQ==\n" +
                     "-----END EC PRIVATE KEY-----";

        MemoryStream certStream = new MemoryStream(Encoding.ASCII.GetBytes(pem));
        MemoryStream keyStream = new MemoryStream(Encoding.ASCII.GetBytes(pem));

        SslContext context = SslContextBuilder.ForServer(certStream, keyStream, null)
                                              .SslProvider(provider)
                                              .ClientAuth(ClientAuth.OPTIONAL)
                                              .Build();

        SslHandler engine = context.NewHandler(UnpooledByteBufAllocator.Default);
        Assert.True(GetWantClientAuth(engine));
        Assert.False(GetNeedClientAuth(engine));
        engine.Engine().Dispose();
        ReferenceCountUtil.Release(context);
    }

    // CLR: OpenSsl.ensureAvailability() has no counterpart (OpenSsl is library-not-ported, owner decision 2026-11-24); it
    // only guards the OpenSSL environment of the CI and the asserted behaviour is the JDK provider's. setEnabledCipherSuites
    // fails an unknown name with IllegalArgumentException; JdkSslContext reports "Unsupported CipherSuite:
    // SOME_INVALID_CIPHER" as ArgumentException when newHandler configures the cipher suites, before it would refuse to
    // create a CipherSuitesPolicy on Windows (src/Netty.NET.Handler/Ssl/JdkSslContext.cs ToTlsCipherSuites).
    [Fact]
    public void TestInvalidCipherJdk()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            TestInvalidCipher(SslProvider.JDK);
        });
    }

    // CLR: SslStream takes no random source (no SslClientAuthenticationOptions/SslServerAuthenticationOptions property, see
    // SslContextBuilder.SecureRandom), so the SpySecureRandom handed to secureRandom(...) is never asked for bytes; the
    // assertion secureRandom.getCount() > 0 is adapted to the measured count 0, the client authentication checks are kept.
    [Fact]
    public void TestServerContextWithSecureRandom()
    {
        TestServerContextWithSecureRandom0(SslProvider.JDK, new SpySecureRandom());
    }

    // CLR: see TestServerContextWithSecureRandom.
    [Fact]
    public void TestClientContextWithSecureRandom()
    {
        TestClientContextWithSecureRandom0(SslProvider.JDK, new SpySecureRandom());
    }

    private static void TestKeyStoreType(SslProvider provider)
    {
        X509Bundle cert = CachedSelfSignedCertificate();
        SslContextBuilder builder = SslContextBuilder.ForServer(cert.ToTempCertChainPem(), cert.ToTempPrivateKeyPem())
                .SslProvider(provider)
                .KeyStoreType("PKCS12");
        SslContext context = builder.Build();
        SslHandler engine = context.NewHandler(UnpooledByteBufAllocator.Default);
        engine.Engine().Dispose();
        ReferenceCountUtil.Release(context);
    }

    private static void TestInvalidCipher(SslProvider provider)
    {
        X509Bundle cert = CachedSelfSignedCertificate();
        SslContextBuilder builder = SslContextBuilder.ForClient()
                .SslProvider(provider)
                .Ciphers(new[] { "SOME_INVALID_CIPHER" })
                .KeyManager(cert.ToTempCertChainPem(),
                        cert.ToTempPrivateKeyPem())
                .TrustManager(cert.ToTempCertChainPem());
        SslContext context = builder.Build();
        SslHandler engine = context.NewHandler(UnpooledByteBufAllocator.Default);
        engine.Engine().Dispose();
        ReferenceCountUtil.Release(context);
    }

    private static void TestClientContextFromFile(SslProvider provider)
    {
        X509Bundle cert = CachedSelfSignedCertificate();
        SslContextBuilder builder = SslContextBuilder.ForClient()
                                                     .SslProvider(provider)
                                                     .KeyManager(cert.ToTempCertChainPem(),
                                                             cert.ToTempPrivateKeyPem())
                                                     .TrustManager(cert.ToTempCertChainPem())
                                                     .ClientAuth(ClientAuth.OPTIONAL);
        SslContext context = builder.Build();
        SslHandler engine = context.NewHandler(UnpooledByteBufAllocator.Default);
        Assert.False(GetWantClientAuth(engine));
        Assert.False(GetNeedClientAuth(engine));
        engine.Engine().Dispose();
        ReferenceCountUtil.Release(context);
    }

    private static void TestClientContext(SslProvider provider)
    {
        X509Bundle cert = CachedSelfSignedCertificate();
        SslContextBuilder builder = SslContextBuilder.ForClient()
                                                     .SslProvider(provider)
                                                     .KeyManager(Key(cert), cert.GetCertificate())
                                                     .TrustManager(cert.GetCertificate())
                                                     .ClientAuth(ClientAuth.OPTIONAL);
        SslContext context = builder.Build();
        SslHandler engine = context.NewHandler(UnpooledByteBufAllocator.Default);
        Assert.False(GetWantClientAuth(engine));
        Assert.False(GetNeedClientAuth(engine));
        engine.Engine().Dispose();
        ReferenceCountUtil.Release(context);
    }

    private static void TestServerContextFromFile(SslProvider provider)
    {
        X509Bundle cert = CachedSelfSignedCertificate();
        SslContextBuilder builder = SslContextBuilder.ForServer(cert.ToTempCertChainPem(), cert.ToTempPrivateKeyPem())
                                                     .SslProvider(provider)
                                                     .TrustManager(cert.ToTempCertChainPem())
                                                     .ClientAuth(ClientAuth.OPTIONAL);
        SslContext context = builder.Build();
        SslHandler engine = context.NewHandler(UnpooledByteBufAllocator.Default);
        Assert.True(GetWantClientAuth(engine));
        Assert.False(GetNeedClientAuth(engine));
        engine.Engine().Dispose();
        ReferenceCountUtil.Release(context);
    }

    private static void TestServerContext(SslProvider provider)
    {
        X509Bundle cert = CachedSelfSignedCertificate();
        SslContextBuilder builder = SslContextBuilder.ForServer(Key(cert), cert.GetCertificate())
                                                     .SslProvider(provider)
                                                     .TrustManager(cert.GetCertificate())
                                                     .ClientAuth(ClientAuth.REQUIRE);
        SslContext context = builder.Build();
        SslHandler engine = context.NewHandler(UnpooledByteBufAllocator.Default);
        Assert.False(GetWantClientAuth(engine));
        Assert.True(GetNeedClientAuth(engine));
        engine.Engine().Dispose();
        ReferenceCountUtil.Release(context);
    }

    // CLR: xUnit1024 forbids a test method name that another method of the class overloads, so the helpers
    // testServerContextWithSecureRandom(SslProvider, SpySecureRandom) and testClientContextWithSecureRandom(SslProvider,
    // SpySecureRandom) end in 0, as CipherSuiteConverterTest's helpers do.
    private static void TestServerContextWithSecureRandom0(SslProvider provider,
                                                          SpySecureRandom secureRandom)
    {
        X509Bundle cert = CachedSelfSignedCertificate();
        SslContextBuilder builder = SslContextBuilder.ForServer(Key(cert), cert.GetCertificate())
                .SslProvider(provider)
                .SecureRandom(secureRandom)
                .TrustManager(cert.GetCertificate())
                .ClientAuth(ClientAuth.REQUIRE);
        SslContext context = builder.Build();
        SslHandler engine = context.NewHandler(UnpooledByteBufAllocator.Default);
        Assert.False(GetWantClientAuth(engine));
        Assert.True(GetNeedClientAuth(engine));
        Assert.Equal(0, secureRandom.GetCount());
        engine.Engine().Dispose();
        ReferenceCountUtil.Release(context);
    }

    private static void TestClientContextWithSecureRandom0(SslProvider provider,
                                                          SpySecureRandom secureRandom)
    {
        X509Bundle cert = CachedSelfSignedCertificate();
        SslContextBuilder builder = SslContextBuilder.ForClient()
                .SslProvider(provider)
                .SecureRandom(secureRandom)
                .KeyManager(Key(cert), cert.GetCertificate())
                .TrustManager(cert.GetCertificate())
                .ClientAuth(ClientAuth.OPTIONAL);
        SslContext context = builder.Build();
        SslHandler engine = context.NewHandler(UnpooledByteBufAllocator.Default);
        Assert.False(GetWantClientAuth(engine));
        Assert.False(GetNeedClientAuth(engine));
        Assert.Equal(0, secureRandom.GetCount());
        engine.Engine().Dispose();
        ReferenceCountUtil.Release(context);
    }

    // CLR: the anonymous X509ExtendedKeyManager serves the client and the server builder; a KeyManager is the
    // LocalCertificateSelectionCallback of a client and the ServerCertificateSelectionCallback of a server (two BCL
    // delegates, see SslContextBuilder.KeyManager(ServerCertificateSelectionCallback)), so its two alias choices are the
    // nested CustomKeyManager's ChooseClientCertificate (chooseClientAlias "cert_sent_to_server", whose
    // getCertificateChain returns an empty chain, so no certificate is sent) and ChooseServerCertificate (chooseServerAlias
    // null: no certificate). The anonymous X509ExtendedTrustManager accepts every certificate: the callback returns true.
    private static void TestContextFromManagers(SslProvider provider)
    {
        X509Bundle cert = CachedSelfSignedCertificate();
        CustomKeyManager customKeyManager = new CustomKeyManager(cert);
        RemoteCertificateValidationCallback customTrustManager = (sender, certificate, chain, errors) => true;
        SslContextBuilder client_builder = SslContextBuilder.ForClient()
                                                     .SslProvider(provider)
                                                     .KeyManager(
                                                             (LocalCertificateSelectionCallback)
                                                             customKeyManager.ChooseClientCertificate)
                                                     .TrustManager(customTrustManager)
                                                     .ClientAuth(ClientAuth.OPTIONAL);
        SslContext client_context = client_builder.Build();
        SslHandler client_engine = client_context.NewHandler(UnpooledByteBufAllocator.Default);
        Assert.False(GetWantClientAuth(client_engine));
        Assert.False(GetNeedClientAuth(client_engine));
        client_engine.Engine().Dispose();
        SslContextBuilder server_builder = SslContextBuilder.ForServer(
                                                             (ServerCertificateSelectionCallback)
                                                             customKeyManager.ChooseServerCertificate)
                                                     .SslProvider(provider)
                                                     .TrustManager(customTrustManager)
                                                     .ClientAuth(ClientAuth.REQUIRE);
        SslContext server_context = server_builder.Build();
        SslHandler server_engine = server_context.NewHandler(UnpooledByteBufAllocator.Default);
        Assert.False(GetWantClientAuth(server_engine));
        Assert.True(GetNeedClientAuth(server_engine));
        server_engine.Engine().Dispose();
        ReferenceCountUtil.Release(client_context);
        ReferenceCountUtil.Release(server_context);
    }

    private sealed class CustomKeyManager
    {
        private readonly X509Bundle cert;

        internal CustomKeyManager(X509Bundle cert)
        {
            this.cert = cert;
        }

        internal X509Certificate ChooseClientCertificate(object sender, string targetHost,
                X509CertificateCollection localCertificates, X509Certificate remoteCertificate,
                string[] acceptableIssuers)
        {
            X509Certificate2[] certificates = GetCertificateChain("cert_sent_to_server");
            return certificates.Length == 0 ? null : certificates[0];
        }

        internal X509Certificate ChooseServerCertificate(object sender, string hostName)
        {
            return null;
        }

        private X509Certificate2[] GetCertificateChain(string s)
        {
            X509Certificate2[] certificates = new X509Certificate2[1];
            certificates[0] = cert.GetCertificate();
            return new X509Certificate2[0];
        }
    }

    // CLR: SecureRandom's next* methods are drawn from RandomNumberGenerator's byte producers, which SpySecureRandom
    // counts: GetBytes(byte[]) is abstract, GetBytes(byte[], int, int), GetBytes(Span<byte>) and the GetNonZeroBytes
    // overloads are virtual ("When overridden in a derived class, fills an array of bytes with a cryptographically strong
    // random sequence of values", https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.randomnumbergenerator?view=net-10.0);
    // each fills from the static RandomNumberGenerator.Fill (or the default generator of Create() for non-zero bytes), as
// super.nextInt() draws from the default SecureRandom. The
    // counter is updated with Interlocked, as a context may draw from several threads.
    private sealed class SpySecureRandom : RandomNumberGenerator
    {
        private int count;

        public override void GetBytes(byte[] data)
        {
            Interlocked.Increment(ref count);
            Fill(data);
        }

        public override void GetBytes(byte[] data, int offset, int count)
        {
            Interlocked.Increment(ref this.count);
            Fill(data.AsSpan(offset, count));
        }

        public override void GetBytes(Span<byte> data)
        {
            Interlocked.Increment(ref count);
            Fill(data);
        }

        public override void GetNonZeroBytes(byte[] data)
        {
            Interlocked.Increment(ref count);
            FillNonZero(data);
        }

        public override void GetNonZeroBytes(Span<byte> data)
        {
            Interlocked.Increment(ref count);
            FillNonZero(data);
        }

        private static void FillNonZero(Span<byte> data)
        {
            using (RandomNumberGenerator random = Create())
            {
                random.GetNonZeroBytes(data);
            }
        }

        public int GetCount()
        {
            return Volatile.Read(ref count);
        }
    }

    // CLR: CachedSelfSignedCertificate.getCachedCertificate() (see the class comment); Lazy<T> creates the bundle once for
    // all tests, as the lazily initialized holder class does.
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

    // CLR: SelfSignedCertificate.key(), the private key of the bundle's key pair (an RSA AsymmetricAlgorithm).
    private static AsymmetricAlgorithm Key(X509Bundle cert)
    {
        return (AsymmetricAlgorithm) cert.GetKeyPair();
    }

    // CLR: SSLEngine.getWantClientAuth() "Returns true if the engine will request client authentication" and
    // getNeedClientAuth() "Returns true if the engine will require client authentication"; both are "only useful to
    // engines in the server mode" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/javax/net/ssl/SSLEngine.html).
    // A client handler's SslClientAuthenticationOptions carry no client authentication setting, so both are false. A server
    // handler requests the certificate with SslServerAuthenticationOptions.ClientCertificateRequired ("whether the client is
    // asked for a certificate for authentication",
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslserverauthenticationoptions?view=net-10.0), and
    // whether a missing certificate is accepted is decided by its RemoteCertificateValidationCallback: without one SslStream
    // rejects a requested but missing certificate, and JdkSslContext installs one that accepts it for ClientAuth.OPTIONAL
    // (src/Netty.NET.Handler/Ssl/JdkSslContext.cs ConfigureAndWrapEngine/VerifyRemoteCertificate). So "want" is a request
    // whose callback accepts a missing certificate when called as SslStream calls it (no certificate,
    // SslPolicyErrors.RemoteCertificateNotAvailable) and "need" a request that rejects it. The options are read through the
    // internal SslHandler.ServerOptions().
    private static bool GetWantClientAuth(SslHandler engine)
    {
        SslServerAuthenticationOptions options = ServerOptions(engine);
        return options != null && options.ClientCertificateRequired && AcceptsMissingClientCertificate(options);
    }

    // CLR: see GetWantClientAuth.
    private static bool GetNeedClientAuth(SslHandler engine)
    {
        SslServerAuthenticationOptions options = ServerOptions(engine);
        return options != null && options.ClientCertificateRequired && !AcceptsMissingClientCertificate(options);
    }

    private static bool AcceptsMissingClientCertificate(SslServerAuthenticationOptions options)
    {
        RemoteCertificateValidationCallback callback = options.RemoteCertificateValidationCallback;
        return callback != null && callback(null, null, null, SslPolicyErrors.RemoteCertificateNotAvailable);
    }

    private static SslServerAuthenticationOptions ServerOptions(SslHandler handler)
    {
        return handler.ServerOptions();
    }
}
