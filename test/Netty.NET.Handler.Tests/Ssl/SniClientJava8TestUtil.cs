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

using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Netty.NET.Handler.Ssl;
using Netty.NET.PkiTesting;

namespace Netty.NET.Handler.Tests.Ssl;

/**
 * In extra class to be able to run tests with java7 without trying to load classes that not exists in java7.
 */
// CLR: a final class with a private constructor and only static members is a static class. The JSSE/JCA types are their
// BCL counterparts (ssl design decision 3, src/Netty.NET.Handler/Ssl/SslContextBuilder.cs): the SSLSession of a handshake
// is the SslStream (SslHandler.Engine(), internal), SNIServerName/SNIHostName is the host name string (as
// SslContextBuilder.serverName maps it, so the two assertSSLSession overloads collapse into one), the TrustManagerFactory
// handing out one X509ExtendedTrustManager is that trust manager as a RemoteCertificateValidationCallback, and the
// KeyManagerFactory handing out wrapping X509ExtendedKeyManagers is the server KeyManager, a
// ServerCertificateSelectionCallback (an SslStreamCertificateContext cannot wrap a key manager). SelfSignedCertificate
// (@Deprecated, not ported) is the pkitesting X509Bundle the tests build (see SniClientTest).
//
// testSniClient(SslProvider, SslProvider, boolean) is not ported: it installs an SNIMatcher on the server engine's
// SSLParameters (setSNIMatchers is "only useful to SSLSockets or SSLEngines operating in server mode",
// https://docs.oracle.com/en/java/javase/17/docs/api/java.base/javax/net/ssl/SSLParameters.html) and expects the handshake
// to fail when it does not match. SslStream has no SSLParameters and SslServerAuthenticationOptions has no SNI matcher
// (its properties, https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslserverauthenticationoptions?view=net-10.0);
// its two callers, SniClientTest.testSniSNIMatcherMatchesClient and testSniSNIMatcherDoesNotMatchClient, are recorded as
// CLR-inapplicable in docs/handler-porting-manifest.json.
public static class SniClientJava8TestUtil
{

    // CLR: ExtendedSSLSession.getRequestedServerNames() "Obtains a List containing all SNIServerNames of the requested
    // Server Name Indication (SNI) extension"; getLocalSupportedSignatureAlgorithms()/getPeerSupportedSignatureAlgorithms()
    // list the TLS 1.2 signature algorithms (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/javax/net/ssl/ExtendedSSLSession.html).
    // SslStream is always "extended": its one requested server name is TargetHostName, the SNI host name a client sends and
    // a server received (measured on this host by SniClientTest.TestSniClient: the server SslStream reports the client's
    // SNI name in its ServerCertificateSelectionCallback), so the list of one name is that string. SslStream exposes no
    // signature algorithm lists, so those three checks have no counterpart.
    internal static void AssertSSLSession(bool clientSide, SslStream session, string name)
    {
        Assert.NotNull(session);
        Assert.Equal(name, session.TargetHostName);
    }

    internal static RemoteCertificateValidationCallback NewSniX509TrustmanagerFactory(string name)
    {
        return new SniX509TrustmanagerFactory(name).EngineGetTrustManagers()[0];
    }

    // CLR: SimpleTrustManagerFactory is jvm-only-replaced (JCA SPI plumbing); engineInit(KeyStore) and
    // engineInit(ManagerFactoryParameters), whose bodies are
    // NOOP
    // have no counterpart. The anonymous X509ExtendedTrustManager is the Verify callback: its sender is the authenticating
    // SslStream, whose IsServer tells checkClientTrusted (the peer is a client) from checkServerTrusted, as
    // FingerprintTrustManagerFactory decides (src/Netty.NET.Handler/Ssl/Util/FingerprintTrustManagerFactory.cs); the engine
    // overloads are the ones SslStream reaches, the Socket and engine-less overloads (which call fail()) have no callback
    // counterpart, nor has getAcceptedIssuers(). checkServerTrusted(..., SSLEngine) asserts the handshake session and
    // returns normally, which is a callback that returns true; fail() is Assert.Fail().
    private sealed class SniX509TrustmanagerFactory
    {

        private readonly string name;

        internal SniX509TrustmanagerFactory(string name)
        {
            this.name = name;
        }

        internal RemoteCertificateValidationCallback[] EngineGetTrustManagers()
        {
            return new RemoteCertificateValidationCallback[] { Verify };
        }

        private bool Verify(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            SslStream sslEngine = (SslStream) sender;
            if (sslEngine.IsServer)
            {
                Assert.Fail();
            }
            AssertSSLSession(!sslEngine.IsServer, sslEngine, name);
            return true;
        }
    }

    // CLR: SslContext.buildKeyManagerFactory(X509Certificate[], String, PrivateKey, String, KeyManagerFactory, String) is
    // the SslStreamCertificateContext of the certificate and its PKCS #12 round-tripped key (SslContext.cs); see
    // SniX509KeyManagerFactory for the KeyManagerFactory around it.
    internal static ServerCertificateSelectionCallback NewSniX509KeyManagerFactory(X509Bundle cert, string hostname)
    {
        return new SniX509KeyManagerFactory(
                hostname, SslContext.BuildKeyManagerFactory(
                new X509Certificate2[] { cert.GetCertificate() }, null, (AsymmetricAlgorithm) cert.GetKeyPair(), null,
                null, null)).ChooseEngineServerAlias;
    }

    // CLR: the KeyManagerFactory subclass with an anonymous KeyManagerFactorySpi wraps every X509ExtendedKeyManager of the
    // given factory in one that forwards each call and, in chooseEngineServerAlias, asserts the handshake session first.
    // SslStream asks a server KeyManager only for the server certificate, through the ServerCertificateSelectionCallback
    // ("Selects the server Secure Sockets Layer (SSL) certificate"; sender "A SslStream object", hostName "The host name
    // requested by the client",
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.security.servercertificateselectioncallback?view=net-10.0), so
    // the wrapper is ChooseEngineServerAlias, which asserts the SslStream's session and returns the certificate (with its
    // key) of the wrapped SslStreamCertificateContext, the alias the wrapped key manager chooses. The forwarding client
    // methods, getServerAliases, getCertificateChain and getPrivateKey, engineInit and the provider/algorithm have no
    // callback counterpart.
    private sealed class SniX509KeyManagerFactory
    {
        private readonly string name;
        private readonly SslStreamCertificateContext factory;

        internal SniX509KeyManagerFactory(string name, SslStreamCertificateContext factory)
        {
            this.name = name;
            this.factory = factory;
        }

        internal X509Certificate ChooseEngineServerAlias(object sender, string hostName)
        {
            SslStream sslEngine = (SslStream) sender;
            AssertSSLSession(!sslEngine.IsServer, sslEngine, name);
            return factory.TargetCertificate;
        }
    }
}
