/*
 * Copyright 2014 The Netty Project
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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using AuthSslProtocols = System.Security.Authentication.SslProtocols;

namespace Netty.NET.Handler.Ssl;

/**
 * An {@link SslContext} which uses JDK's SSL/TLS implementation.
 */
// CLR: the SSL/TLS implementation is SslStream (owner decision 2026-11-24, docs/handler-ssl-design.md). The JSSE
// SSLContext this class wraps is replaced by the trust and key material SslStream takes per connection: the
// TrustManagerFactory (X509ChainPolicy, the options' CertificateChainPolicy) or TrustManager
// (RemoteCertificateValidationCallback) and the KeyManagerFactory (SslStreamCertificateContext, built once per context,
// which "also enables extra features such as TLS session resumption on Linux servers",
// https://learn.microsoft.com/en-us/dotnet/core/extensions/sslstream-best-practices) or KeyManager
// (ServerCertificateSelectionCallback/LocalCertificateSelectionCallback). So the public constructors that take a
// pre-configured SSLContext and context() are not ported (owner decision 2: JSSE-only members; the two @Deprecated ones
// name the SSLContext constructor as replacement), and SslContextBuilder uses the internal factories
// NewJdkSslServerContext/NewJdkSslClientContext, which do what the constructors of the @Deprecated JdkSslServerContext/
// JdkSslClientContext (deprecated-not-ported) did, and the internal constructor. sessionContext() (SSLSessionContext) is
// not ported, and newEngine(alloc[, peerHost, peerPort]) with configureAndWrapEngine becomes the newHandler overrides
// that build a fresh SslClientAuthenticationOptions/SslServerAuthenticationOptions per handler (see SslContext). The
// JdkApplicationProtocolNegotiator types are @Deprecated and not ported (lead decision), so the negotiator is the private
// ProtocolNegotiator holding the ALPN protocol list. The JCA Provider of the source (DEFAULT_PROVIDER, the
// non-default-provider branch of the constructor) has no counterpart: the engine is always SslStream. PROTOCOL ("TLS") only
// served SSLContext.getInstance in the deprecated subclasses and is not ported. SSLException is AuthenticationException,
// IllegalArgumentException ArgumentException, UnsupportedOperationException NotSupportedException and java.lang.Error
// UnreachableException (codec-base mappings).
public class JdkSslContext : SslContext
{

    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(JdkSslContext));

    private static readonly string[] DEFAULT_PROTOCOLS;
    private static readonly IList<string> DEFAULT_CIPHERS;
    private static readonly IList<string> DEFAULT_CIPHERS_NON_TLSV13;
    private static readonly ISet<string> SUPPORTED_CIPHERS;
    private static readonly ISet<string> SUPPORTED_CIPHERS_NON_TLSV13;

    // CLR: the TlsCipherSuite field names, whose "field names and values are based on the TLS Cipher Suites list from the
    // Internet Assigned Numbers Authority (IANA)"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.security.tlsciphersuite?view=net-10.0), are the JSSE
    // standard cipher suite names; this map turns a name into the value CipherSuitesPolicy takes (Enum.TryParse would also
    // accept numbers).
    private static readonly Dictionary<string, TlsCipherSuite> TLS_CIPHER_SUITES = NewTlsCipherSuites();

    // CLR: an explicit static constructor, so the defaults are computed when the class is first used, as the Java class
    // initializer computes them. Arrays.asList(...) and the List print as AbstractCollection.toString() does.
    static JdkSslContext()
    {
        Defaults defaults = new Defaults();
        defaults.Init();

        DEFAULT_PROTOCOLS = defaults.defaultProtocols;
        SUPPORTED_CIPHERS = defaults.supportedCiphers;
        DEFAULT_CIPHERS = defaults.defaultCiphers;
        DEFAULT_CIPHERS_NON_TLSV13 = defaults.defaultCiphersNonTLSv13;
        SUPPORTED_CIPHERS_NON_TLSV13 = defaults.supportedCiphersNonTLSv13;

        if (logger.IsDebugEnabled())
        {
            logger.Debug("Default protocols (JDK): {} ", "[" + string.Join(", ", DEFAULT_PROTOCOLS) + "]");
            logger.Debug("Default cipher suites (JDK): {}", "[" + string.Join(", ", DEFAULT_CIPHERS) + "]");
        }
    }

    // CLR: SSLContext.getInstance(PROTOCOL).init(null, null, null), its createSSLEngine() and getProvider() have no
    // counterpart; the defaults are computed from SslStream's protocol and cipher suite names (see DefaultProtocols and
    // SupportedCiphers). Collections.unmodifiableSet/unmodifiableList are ReadOnlySet/ReadOnlyCollection and
    // LinkedHashSet is HashSet (the BCL has no insertion-ordered set; the order of a set is not used for the defaults).
    private sealed class Defaults
    {
        internal string[] defaultProtocols;
        internal IList<string> defaultCiphers;
        internal IList<string> defaultCiphersNonTLSv13;
        internal ISet<string> supportedCiphers;
        internal ISet<string> supportedCiphersNonTLSv13;

        internal void Init()
        {
            defaultProtocols = DefaultProtocols();

            supportedCiphers = new ReadOnlySet<string>(SupportedCiphers());
            defaultCiphers = new ReadOnlyCollection<string>(DefaultCiphers(supportedCiphers));

            List<string> ciphersNonTLSv13 = new List<string>(defaultCiphers);
            ciphersNonTLSv13.RemoveAll(c => Array.IndexOf(SslUtils.DEFAULT_TLSV13_CIPHER_SUITES, c) >= 0);
            defaultCiphersNonTLSv13 = new ReadOnlyCollection<string>(ciphersNonTLSv13);

            ISet<string> suppertedCiphersNonTLSv13 = new HashSet<string>(supportedCiphers);
            suppertedCiphersNonTLSv13.ExceptWith(SslUtils.DEFAULT_TLSV13_CIPHER_SUITES);
            supportedCiphersNonTLSv13 = new ReadOnlySet<string>(suppertedCiphersNonTLSv13);
        }
    }

    // CLR: context.getDefaultSSLParameters().getProtocols(), the protocols enabled by default, has no SslStream query:
    // SslStream defers the choice to the operating system with SslProtocols.None, "it is recommended to defer to the
    // operating system settings by using None value (this is the default)"
    // (https://learn.microsoft.com/en-us/dotnet/core/extensions/sslstream-best-practices). The set holds TLSv1.3 when the
    // wave-1 probe SslUtils.IsTLSv13EnabledByJDK() negotiated it with None, and TLSv1.2, the other version that page and
    // the .NET Framework TLS page cover ("This document covers TLS 1.2 and TLS 1.3 only"); the engine.getEnabledProtocols()
    // fallback is not needed, as the list always holds TLSv1.2. The deprecated SslProtocols constants are not ported, so
    // TLSv1.1 and TLSv1 are their names. This list is reported and decides the default cipher suites; an SslStream of a
    // context built without protocols uses SslProtocols.None (see ConfigureAndWrapEngine).
    private static string[] DefaultProtocols()
    {
        // Choose the sensible default list of protocols that respects JDK flags, eg. jdk.tls.client.protocols
        ISet<string> supportedProtocolsSet = new HashSet<string>();
        if (SslUtils.IsTLSv13EnabledByJDK())
        {
            supportedProtocolsSet.Add(SslProtocols.TLS_v1_3);
        }
        supportedProtocolsSet.Add(SslProtocols.TLS_v1_2);
        List<string> protocols = new List<string>();
        SslUtils.AddIfSupported(
                supportedProtocolsSet, protocols,
                SslProtocols.TLS_v1_3, SslProtocols.TLS_v1_2,
                "TLSv1.1", "TLSv1");

        return protocols.ToArray();
    }

    // CLR: engine.getSupportedCipherSuites() is the set of TlsCipherSuite names (see TLS_CIPHER_SUITES), the cipher suites
    // SslStream can be restricted to. The source's addition of the "TLS_" form of IBM J9's "SSL_" names tests each with
    // engine.setEnabledCipherSuites and has no counterpart (no TlsCipherSuite name starts with "SSL_"); its comments are
    // recorded.
    private static ISet<string> SupportedCiphers()
    {
        // Choose the sensible default list of cipher suites.
        string[] supportedCiphers = Enum.GetNames<TlsCipherSuite>();
        ISet<string> supportedCiphersSet = new HashSet<string>(supportedCiphers.Length);
        for (int i = 0; i < supportedCiphers.Length; ++i)
        {
            string supportedCipher = supportedCiphers[i];
            supportedCiphersSet.Add(supportedCipher);
        }
        return supportedCiphersSet;
    }

    // CLR: engine.getEnabledCipherSuites(), the fallback when no default cipher suite is supported, is the operating
    // system's choice, which SslStream does not list; the supported names are the fallback.
    private static List<string> DefaultCiphers(ISet<string> supportedCiphers)
    {
        List<string> ciphers = new List<string>();
        SslUtils.AddIfSupported(supportedCiphers, ciphers, SslUtils.DEFAULT_CIPHER_SUITES);
        SslUtils.UseFallbackCiphersIfDefaultIsEmpty(ciphers, supportedCiphers);
        return ciphers;
    }

    private static bool IsTlsV13Supported(string[] protocols)
    {
        foreach (string protocol in protocols)
        {
            if (SslProtocols.TLS_v1_3.Equals(protocol))
            {
                return true;
            }
        }
        return false;
    }

    private static Dictionary<string, TlsCipherSuite> NewTlsCipherSuites()
    {
        Dictionary<string, TlsCipherSuite> suites = new Dictionary<string, TlsCipherSuite>(StringComparer.Ordinal);
        foreach (string name in Enum.GetNames<TlsCipherSuite>())
        {
            suites[name] = Enum.Parse<TlsCipherSuite>(name);
        }
        return suites;
    }

    private readonly string[] protocols;
    private readonly string[] cipherSuites;
    private readonly IList<string> unmodifiableCipherSuites;
    private readonly IApplicationProtocolNegotiator apn;
    private readonly ClientAuth clientAuth;
    // CLR: the trust and key material of the source's SSLContext (see the class comment).
    private readonly X509ChainPolicy trustManagerFactory;
    private readonly RemoteCertificateValidationCallback trustManager;
    private readonly SslStreamCertificateContext keyManagerFactory;
    private readonly ServerCertificateSelectionCallback serverKeyManager;
    private readonly LocalCertificateSelectionCallback clientKeyManager;
    private readonly bool isClient;
    private readonly string endpointIdentificationAlgorithm;
    private readonly IList<string> serverNames;
    // CLR: whether the protocols and cipher suites were configured (non-null); otherwise the SslStream options keep the
    // operating system's choice (see ConfigureAndWrapEngine).
    private readonly bool defaultProtocols;
    private readonly bool restrictCipherSuites;

    // CLR: the package-private constructor (sslContext, isClient, ciphers, cipherFilter, apn, clientAuth, protocols,
    // startTls, endpointIdentificationAlgorithm, serverNames, resumptionController) with the SSLContext replaced by its
    // trust and key material and without the ResumptionController (not ported). The source compares the SSLContext
    // provider with the JDK default; there is no other provider, so the default branch is the only one, and the other
    // branch, which probes a non-default provider's engine, has no counterpart (its comments are recorded).
    // ClientAuth is an enum (a value type), so its checkNotNull has no counterpart. Arrays.asList(cipherSuites) wrapped
    // unmodifiable is a ReadOnlyCollection over the array.
    internal JdkSslContext(X509ChainPolicy trustManagerFactory, RemoteCertificateValidationCallback trustManager,
                           SslStreamCertificateContext keyManagerFactory,
                           ServerCertificateSelectionCallback serverKeyManager,
                           LocalCertificateSelectionCallback clientKeyManager,
                           bool isClient, IEnumerable<string> ciphers, ICipherSuiteFilter cipherFilter,
                           IApplicationProtocolNegotiator apn, ClientAuth clientAuth, string[] protocols, bool startTls,
                           string endpointIdentificationAlgorithm, IList<string> serverNames)
        : base(startTls)
    {
        this.apn = ObjectUtil.CheckNotNull(apn, "apn");
        this.clientAuth = clientAuth;
        this.trustManagerFactory = trustManagerFactory;
        this.trustManager = trustManager;
        this.keyManagerFactory = keyManagerFactory;
        this.serverKeyManager = serverKeyManager;
        this.clientKeyManager = clientKeyManager;
        this.endpointIdentificationAlgorithm = endpointIdentificationAlgorithm;
        this.serverNames = serverNames;

        IList<string> defaultCiphers;
        ISet<string> supportedCiphers;
        defaultProtocols = protocols == null;
        this.protocols = protocols == null? DEFAULT_PROTOCOLS : protocols;
        if (IsTlsV13Supported(this.protocols))
        {
            supportedCiphers = SUPPORTED_CIPHERS;
            defaultCiphers = DEFAULT_CIPHERS;
        }
        else
        {
            // TLSv1.3 is not supported, ensure we do not include any TLSv1.3 ciphersuite.
            supportedCiphers = SUPPORTED_CIPHERS_NON_TLSV13;
            defaultCiphers = DEFAULT_CIPHERS_NON_TLSV13;
        }

        restrictCipherSuites = ciphers != null;
        cipherSuites = ObjectUtil.CheckNotNull(cipherFilter, "cipherFilter").FilterCipherSuites(
                ciphers, defaultCiphers, supportedCiphers);

        unmodifiableCipherSuites = new ReadOnlyCollection<string>(cipherSuites);
        this.isClient = isClient;
    }

    // CLR-only factory for what the constructor of the @Deprecated JdkSslServerContext (deprecated-not-ported) built for
    // SslContext.newServerContextInternal: newSSLContext(...) turns the certificates into the TrustManagerFactory
    // (BuildTrustManagerFactory) and the key into the KeyManagerFactory (BuildKeyManagerFactory); "Mimic the way
    // SSLContext.getInstance(KeyManager[], null, null) works" (the default TrustManagerFactory) is the system trust of a
    // null CertificateChainPolicy. The session cache size and timeout have no SslStream setting (docs/handler-ssl-design.md,
    // Known differences) and the SecureRandom no option, so both are not applied; the resumption-controller and
    // EnhancingX509ExtendedTrustManager wrapping of the trust managers is not ported (see the decision record). A missing
    // key is the NullPointerException("key, keyManagerFactory"), an ArgumentNullException here; other failures become
    // the SSLException (AuthenticationException) "failed to initialize the server-side SSL context".
    internal static JdkSslContext NewJdkSslServerContext(
            X509Certificate2[] trustCertCollection, X509ChainPolicy trustManagerFactory,
            RemoteCertificateValidationCallback trustManager,
            X509Certificate2[] keyCertChain, AsymmetricAlgorithm key, string keyPassword,
            SslStreamCertificateContext keyManagerFactory, ServerCertificateSelectionCallback keyManager,
            IEnumerable<string> ciphers, ICipherSuiteFilter cipherFilter, ApplicationProtocolConfig apn,
            long sessionCacheSize, long sessionTimeout, ClientAuth clientAuth, string[] protocols, bool startTls,
            RandomNumberGenerator secureRandom, string keyStore)
    {
        if (key == null && keyManagerFactory == null && keyManager == null)
        {
            throw new ArgumentNullException("key, keyManagerFactory");
        }

        try
        {
            if (trustCertCollection != null)
            {
                trustManagerFactory = BuildTrustManagerFactory(trustCertCollection, trustManagerFactory, keyStore);
            }

            if (key != null)
            {
                keyManagerFactory = BuildKeyManagerFactory(keyCertChain, null,
                        key, keyPassword, keyManagerFactory, null);
            }
        }
        catch (Exception e) when (e is not AuthenticationException)
        {
            throw new AuthenticationException("failed to initialize the server-side SSL context", e);
        }
        return new JdkSslContext(trustManagerFactory, trustManager, keyManagerFactory, keyManager, null, false,
                ciphers, cipherFilter, ToNegotiator(apn, true), clientAuth, protocols, startTls, null, null);
    }

    // CLR-only factory for what the constructor of the @Deprecated JdkSslClientContext (deprecated-not-ported) built for
    // SslContext.newClientContextInternal; see NewJdkSslServerContext. As in the source, the client context has
    // ClientAuth.NONE and startTls false.
    internal static JdkSslContext NewJdkSslClientContext(
            X509Certificate2[] trustCertCollection, X509ChainPolicy trustManagerFactory,
            RemoteCertificateValidationCallback trustManager,
            X509Certificate2[] keyCertChain, AsymmetricAlgorithm key, string keyPassword,
            SslStreamCertificateContext keyManagerFactory, LocalCertificateSelectionCallback keyManager,
            IEnumerable<string> ciphers, ICipherSuiteFilter cipherFilter, ApplicationProtocolConfig apn,
            string[] protocols, long sessionCacheSize, long sessionTimeout, RandomNumberGenerator secureRandom,
            string keyStore, string endpointIdentificationAlgorithm, IList<string> serverNames)
    {
        try
        {
            if (trustCertCollection != null)
            {
                trustManagerFactory = BuildTrustManagerFactory(trustCertCollection, trustManagerFactory, keyStore);
            }
            if (keyCertChain != null)
            {
                keyManagerFactory = BuildKeyManagerFactory(keyCertChain, null,
                        key, keyPassword, keyManagerFactory, keyStore);
            }
        }
        catch (Exception e) when (e is not AuthenticationException)
        {
            throw new AuthenticationException("failed to initialize the client-side SSL context", e);
        }
        return new JdkSslContext(trustManagerFactory, trustManager, keyManagerFactory, null, keyManager, true,
                ciphers, cipherFilter, ToNegotiator(apn, false), ClientAuth.NONE,
                protocols, false, endpointIdentificationAlgorithm, serverNames);
    }

    public sealed override bool IsClient()
    {
        return isClient;
    }

    public sealed override IList<string> CipherSuites()
    {
        return unmodifiableCipherSuites;
    }

    // CLR: newEngine(alloc) is createSSLEngine() without peer information, which newHandler(alloc, startTls) wraps in an
    // SslHandler; see NewHandler(alloc, peerHost, peerPort, startTls).
    protected internal override SslHandler NewHandler(IByteBufAllocator alloc, bool startTls)
    {
        return NewHandler(alloc, null, -1, startTls);
    }

    // CLR: newEngine(alloc, peerHost, peerPort) + configureAndWrapEngine, wrapped in an SslHandler as SslContext's
    // newHandler does: the engine is a fresh SslClientAuthenticationOptions/SslServerAuthenticationOptions that the
    // handler owns (SslHandler(options, startTls)). The allocator is not used by SslStream (the JDK engine ignores it
    // too) and the peer port has no option (JSSE uses it as a session cache hint).
    protected internal override SslHandler NewHandler(IByteBufAllocator alloc, string peerHost, int peerPort,
                                                      bool startTls)
    {
        if (IsServer())
        {
            return new SslHandler(ConfigureAndWrapEngine(new SslServerAuthenticationOptions()), startTls);
        }
        return new SslHandler(ConfigureAndWrapEngine(new SslClientAuthenticationOptions(), peerHost), startTls);
    }

    // CLR: configureAndWrapEngine for a client engine. setEnabledCipherSuites is the CipherSuitesPolicy of the cipher
    // suites when they were configured (NewCipherSuitesPolicy), setEnabledProtocols the EnabledSslProtocols
    // (ToSslProtocols; SslProtocols.None, the operating system's choice, when no protocols were configured),
    // setUseClientMode is the options type, configureSSLParameters sets the host name and the endpoint identification,
    // and the ALPN engine wrapper of the negotiator is the ApplicationProtocols list. The SSLContext's trust and key
    // managers are the CertificateChainPolicy (a clone per handler, as the policy is mutable), the validation callback
    // (VerifyRemoteCertificate), ClientCertificateContext and LocalCertificateSelectionCallback.
    private SslClientAuthenticationOptions ConfigureAndWrapEngine(SslClientAuthenticationOptions engine, string peerHost)
    {
        if (restrictCipherSuites)
        {
            SetCipherSuitesPolicy(engine);
        }
        engine.EnabledSslProtocols = defaultProtocols ? AuthSslProtocols.None : ToSslProtocols(protocols);
        ConfigureSSLParameters(engine, peerHost);
        engine.ApplicationProtocols = ApplicationProtocols();
        engine.CertificateChainPolicy = trustManagerFactory?.Clone();
        if (trustManager != null || endpointIdentificationAlgorithm == null || trustManagerFactory != null)
        {
            engine.RemoteCertificateValidationCallback = VerifyRemoteCertificate;
        }
        engine.ClientCertificateContext = keyManagerFactory;
        engine.LocalCertificateSelectionCallback = clientKeyManager;
        return engine;
    }

    // CLR: configureAndWrapEngine for a server engine; see the client overload. setWantClientAuth(true) (OPTIONAL) and
    // setNeedClientAuth(true) (REQUIRE) are ClientCertificateRequired, "whether the client is asked for a certificate
    // for authentication. Note that this is only a request -- if no certificate is provided, the server still accepts
    // the connection request" (https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslserverauthenticationoptions?view=net-10.0),
    // while SslStream's default validation fails a requested but missing client certificate with
    // RemoteCertificateNotAvailable (measured); so REQUIRE keeps that validation and OPTIONAL accepts a missing certificate
    // in VerifyRemoteCertificate. The key material is ServerCertificateContext or ServerCertificateSelectionCallback.
    private SslServerAuthenticationOptions ConfigureAndWrapEngine(SslServerAuthenticationOptions engine)
    {
        if (restrictCipherSuites)
        {
            SetCipherSuitesPolicy(engine);
        }
        engine.EnabledSslProtocols = defaultProtocols ? AuthSslProtocols.None : ToSslProtocols(protocols);
        switch (clientAuth)
        {
            case ClientAuth.OPTIONAL:
                engine.ClientCertificateRequired = true;
                break;
            case ClientAuth.REQUIRE:
                engine.ClientCertificateRequired = true;
                break;
            case ClientAuth.NONE:
                break; // exhaustive cases
            default:
                throw new UnreachableException("Unexpected auth " + clientAuth);
        }
        engine.ApplicationProtocols = ApplicationProtocols();
        engine.CertificateChainPolicy = trustManagerFactory?.Clone();
        // CLR: installed for every server, ClientAuth.NONE included: a server renegotiates on SslStream only through
        // NegotiateClientCertificateAsync, which asks the client for a certificate, and SslStream's default validation then
        // rejects a client without one (RemoteCertificateNotAvailable, measured with RenegotiateTest), where a JSSE server
        // without client authentication neither requests nor checks a client certificate.
        engine.RemoteCertificateValidationCallback = VerifyRemoteCertificate;
        engine.ServerCertificateContext = keyManagerFactory;
        engine.ServerCertificateSelectionCallback = serverKeyManager;
        return engine;
    }

    // CLR: SSLParameters.setEndpointIdentificationAlgorithm(endpointIdentificationAlgorithm) is applied by
    // VerifyRemoteCertificate. createSSLEngine(peerHost, peerPort) gives the engine the peer host, which JSSE checks the
    // server certificate against and sends as the SNI name unless setServerNames(serverNames) names one;
    // SslClientAuthenticationOptions.TargetHost is both ("The name is used for server certificate validation and can be
    // a DNS name or an IP address", and SslStream sends it as the SNI host name, measured), so it is the first configured
    // server name, else the peer host (null: no SNI and a name check that fails, as JSSE fails it without a peer host).
    private void ConfigureSSLParameters(SslClientAuthenticationOptions engine, string peerHost)
    {
        engine.TargetHost = peerHost;
        if (serverNames != null && serverNames.Count != 0)
        {
            engine.TargetHost = serverNames[0];
        }
    }

    // CLR-only helper: the remote certificate check that the SSLContext's trust managers perform. JSSE calls no trust
    // manager when the peer sent no certificate (SslStream reports a null certificate with
    // RemoteCertificateNotAvailable): a client fails, and a server accepts unless it needs client authentication
    // (REQUIRE). Without endpoint identification (a null algorithm) the host name check is not part of the decision, so
    // RemoteCertificateNameMismatch is cleared. A configured TrustManager callback then decides, as an
    // X509ExtendedTrustManager decides with the SSLParameters; otherwise the certificate is trusted when SslStream found
    // no error with the system trust or the CertificateChainPolicy ("The Boolean value returned by the method invoked by
    // this delegate determines whether the authentication is allowed to succeed",
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.security.remotecertificatevalidationcallback?view=net-10.0).
    private bool VerifyRemoteCertificate(object sender, X509Certificate certificate, X509Chain chain,
                                         SslPolicyErrors sslPolicyErrors)
    {
        if (!isClient && clientAuth == ClientAuth.NONE)
        {
            // A JSSE server without client authentication calls no trust manager for the client (see the server
            // ConfigureAndWrapEngine): whatever a renegotiation brings is accepted.
            return true;
        }
        if (certificate == null)
        {
            return !isClient && clientAuth != ClientAuth.REQUIRE;
        }
        if (endpointIdentificationAlgorithm == null)
        {
            sslPolicyErrors &= ~SslPolicyErrors.RemoteCertificateNameMismatch;
        }
        if (trustManager != null)
        {
            return trustManager(sender, certificate, chain, sslPolicyErrors);
        }
        if ((sslPolicyErrors & SslPolicyErrors.RemoteCertificateChainErrors) != 0 && OnlyTrustAnchorsExpired(chain))
        {
            sslPolicyErrors &= ~SslPolicyErrors.RemoteCertificateChainErrors;
        }
        return sslPolicyErrors == SslPolicyErrors.None;
    }

    // CLR-only helper: a PKIX trust manager over the configured certificates does not check the validity period of a
    // trust anchor. RFC 5280 6.1: "When the trust anchor is provided in the form of a self-signed certificate, this
    // self-signed certificate is not included as part of the prospective certification path", and 6.1.3 (a)(2) "The
    // certificate validity period includes the current time" applies to "certificate i (for all i in [1..n])" of that
    // path (https://www.rfc-editor.org/rfc/rfc5280.txt). The Windows chain engine reports NotTimeValid for an expired
    // certificate of the CustomTrustStore (measured with the expired test resources mutual_auth_ca.pem and test2.crt,
    // which JSSE accepts). So a chain whose only errors are NotTimeValid on elements of the configured trust store is
    // trusted; any other error, or an expired element that is not an anchor, still fails. X509ChainElement
    // .ChainElementStatus "Gets the error status of the current X.509 certificate in a chain"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509chainelement.chainelementstatus?view=net-10.0).
    private bool OnlyTrustAnchorsExpired(X509Chain chain)
    {
        if (chain == null || trustManagerFactory == null || trustManagerFactory.TrustMode != X509ChainTrustMode.CustomRootTrust)
        {
            return false;
        }
        foreach (X509ChainStatus status in chain.ChainStatus)
        {
            if (status.Status != X509ChainStatusFlags.NotTimeValid)
            {
                return false;
            }
        }
        bool anchorExpired = false;
        foreach (X509ChainElement element in chain.ChainElements)
        {
            X509ChainStatusFlags flags = X509ChainStatusFlags.NoError;
            foreach (X509ChainStatus status in element.ChainElementStatus)
            {
                flags |= status.Status;
            }
            if (flags == X509ChainStatusFlags.NoError)
            {
                continue;
            }
            if (flags != X509ChainStatusFlags.NotTimeValid || !IsTrustAnchor(element.Certificate))
            {
                return false;
            }
            anchorExpired = true;
        }
        return anchorExpired;
    }

    private bool IsTrustAnchor(X509Certificate2 certificate)
    {
        foreach (X509Certificate2 anchor in trustManagerFactory.CustomTrustStore)
        {
            if (anchor.RawDataMemory.Span.SequenceEqual(certificate.RawDataMemory.Span))
            {
                return true;
            }
        }
        return false;
    }

    // CLR: the ALPN protocols of the negotiator as SslApplicationProtocol values; null (no ALPN) for an empty list.
    private List<SslApplicationProtocol> ApplicationProtocols()
    {
        IList<string> names = apn.Protocols();
        if (names.Count == 0)
        {
            return null;
        }
        List<SslApplicationProtocol> protocols = new List<SslApplicationProtocol>(names.Count);
        foreach (string name in names)
        {
            protocols.Add(new SslApplicationProtocol(name));
        }
        return protocols;
    }

    // CLR: SSLEngine.setEnabledProtocols with the JSSE protocol names; an unknown name fails as setEnabledProtocols fails
    // ("Unsupported protocol"). SSLv2Hello is a ClientHello format, not a protocol version, and has no SslProtocols value.
    // The obsolete SslProtocols values are the protocols the configured names ask for.
    private static AuthSslProtocols ToSslProtocols(string[] protocols)
    {
        AuthSslProtocols enabled = AuthSslProtocols.None;
#pragma warning disable SYSLIB0039, CS0618 // the configured legacy protocol names map to the obsolete values.
        foreach (string protocol in protocols)
        {
            switch (protocol)
            {
                case SslProtocols.TLS_v1_3:
                    enabled |= AuthSslProtocols.Tls13;
                    break;
                case SslProtocols.TLS_v1_2:
                    enabled |= AuthSslProtocols.Tls12;
                    break;
                case "TLSv1.1":
                    enabled |= AuthSslProtocols.Tls11;
                    break;
                case "TLSv1":
                    enabled |= AuthSslProtocols.Tls;
                    break;
                case "SSLv3":
                    enabled |= AuthSslProtocols.Ssl3;
                    break;
                case "SSLv2":
                    enabled |= AuthSslProtocols.Ssl2;
                    break;
                case "SSLv2Hello":
                    break;
                default:
                    throw new ArgumentException("Unsupported protocol: " + protocol);
            }
        }
#pragma warning restore SYSLIB0039, CS0618
        return enabled;
    }

    // CLR: SSLEngine.setEnabledCipherSuites(cipherSuites) with configured cipher suites is the CipherSuitesPolicy of
    // their TlsCipherSuite values; an unknown name fails as setEnabledCipherSuites fails ("Unsupported CipherSuite"). A
    // CipherSuitesPolicy can only be created "if you're using Linux systems with OpenSSL 1.1.1 or higher or macOS", else
    // its constructor throws PlatformNotSupportedException
    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.security.ciphersuitespolicy.-ctor?view=net-10.0; the type is
    // unsupported on Windows and Android), so on those platforms configured cipher suites fail here, in newHandler, with
    // that exception (measured on Windows) instead of being ignored; "the order of the TlsCipherSuite elements doesn't
    // guarantee their priority".
    private void SetCipherSuitesPolicy(SslClientAuthenticationOptions engine)
    {
        List<TlsCipherSuite> suites = ToTlsCipherSuites(cipherSuites);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsAndroid())
        {
            throw new PlatformNotSupportedException("CipherSuitesPolicy is not supported on this platform.");
        }
        engine.CipherSuitesPolicy = new CipherSuitesPolicy(suites);
    }

    // CLR: see SetCipherSuitesPolicy(SslClientAuthenticationOptions).
    private void SetCipherSuitesPolicy(SslServerAuthenticationOptions engine)
    {
        List<TlsCipherSuite> suites = ToTlsCipherSuites(cipherSuites);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsAndroid())
        {
            throw new PlatformNotSupportedException("CipherSuitesPolicy is not supported on this platform.");
        }
        engine.CipherSuitesPolicy = new CipherSuitesPolicy(suites);
    }

    private static List<TlsCipherSuite> ToTlsCipherSuites(string[] cipherSuites)
    {
        List<TlsCipherSuite> suites = new List<TlsCipherSuite>(cipherSuites.Length);
        foreach (string cipherSuite in cipherSuites)
        {
            if (!TLS_CIPHER_SUITES.TryGetValue(cipherSuite, out TlsCipherSuite suite))
            {
                throw new ArgumentException("Unsupported CipherSuite: " + cipherSuite);
            }
            suites.Add(suite);
        }
        return suites;
    }

    public sealed override IApplicationProtocolNegotiator ApplicationProtocolNegotiator()
    {
        return apn;
    }

    /**
     * Translate a {@link ApplicationProtocolConfig} object to a {@link JdkApplicationProtocolNegotiator} object.
     * @param config The configuration which defines the translation
     * @param isServer {@code true} if a server {@code false} otherwise.
     * @return The results of the translation
     */
    // CLR: JdkDefaultApplicationProtocolNegotiator.INSTANCE and JdkAlpnApplicationProtocolNegotiator(failIfNoCommon,
    // protocols) are the ProtocolNegotiator below (the JDK negotiator types are @Deprecated and not ported). StringBuilder
    // appends the enum constant name, as C# enum ToString() does.
    internal static IApplicationProtocolNegotiator ToNegotiator(ApplicationProtocolConfig config, bool isServer)
    {
        if (config == null)
        {
            return ProtocolNegotiator.DEFAULT;
        }

        switch(config.Protocol_())
        {
        case ApplicationProtocolConfig.Protocol.NONE:
            return ProtocolNegotiator.DEFAULT;
        case ApplicationProtocolConfig.Protocol.ALPN:
            if (isServer)
            {
                switch(config.SelectorFailureBehavior_())
                {
                case ApplicationProtocolConfig.SelectorFailureBehavior.FATAL_ALERT:
                    return new ProtocolNegotiator(true, config.SupportedProtocols());
                case ApplicationProtocolConfig.SelectorFailureBehavior.NO_ADVERTISE:
                    return new ProtocolNegotiator(false, config.SupportedProtocols());
                default:
                    throw new NotSupportedException(new StringBuilder("JDK provider does not support ")
                    .Append(config.SelectorFailureBehavior_()).Append(" failure behavior").ToString());
                }
            }
            else
            {
                switch(config.SelectedListenerFailureBehavior_())
                {
                case ApplicationProtocolConfig.SelectedListenerFailureBehavior.ACCEPT:
                    return new ProtocolNegotiator(false, config.SupportedProtocols());
                case ApplicationProtocolConfig.SelectedListenerFailureBehavior.FATAL_ALERT:
                    return new ProtocolNegotiator(true, config.SupportedProtocols());
                default:
                    throw new NotSupportedException(new StringBuilder("JDK provider does not support ")
                    .Append(config.SelectedListenerFailureBehavior_()).Append(" failure behavior").ToString());
                }
            }
        default:
            throw new NotSupportedException(new StringBuilder("JDK provider does not support ")
            .Append(config.Protocol_()).Append(" protocol").ToString());
        }
    }

    // CLR-only negotiator replacing JdkDefaultApplicationProtocolNegotiator (no protocols) and
    // JdkAlpnApplicationProtocolNegotiator (the ALPN protocol list). failIfNoCommonProtocols (FATAL_ALERT) has no
    // SslStream option (the options only take the protocol list), so a failed negotiation behaves as SslStream decides;
    // it is kept as the negotiator's state.
    private sealed class ProtocolNegotiator : IApplicationProtocolNegotiator
    {
        internal static readonly ProtocolNegotiator DEFAULT =
                new ProtocolNegotiator(false, new ReadOnlyCollection<string>(Array.Empty<string>()));

        internal readonly bool failIfNoCommonProtocols;
        private readonly IList<string> protocols;

        internal ProtocolNegotiator(bool failIfNoCommonProtocols, IList<string> protocols)
        {
            this.failIfNoCommonProtocols = failIfNoCommonProtocols;
            this.protocols = protocols;
        }

        public IList<string> Protocols()
        {
            return protocols;
        }
    }

    /**
     * Build a {@link KeyManagerFactory} based upon a key file, key file password, and a certificate chain.
     * @param certChainFile an X.509 certificate chain file in PEM format
     * @param keyFile a PKCS#8 private key file in PEM format
     * @param keyPassword the password of the {@code keyFile}.
     *                    {@code null} if it's not password-protected.
     * @param kmf The existing {@link KeyManagerFactory} that will be used if not {@code null}
     * @param keyStore the {@link KeyStore} that should be used in the {@link KeyManagerFactory}
     * @return A {@link KeyManagerFactory} based upon a key file, key file password, and a certificate chain.
     */
    // CLR: the KeyManagerFactory is an SslStreamCertificateContext (see SslContext.BuildKeyManagerFactory).
    // Security.getProperty("ssl.KeyManagerFactory.algorithm") has no counterpart (no JCA security properties), so the
    // algorithm is the source's fallback "SunX509", which the SslStreamCertificateContext does not use.
    internal static SslStreamCertificateContext BuildKeyManagerFactory(FileInfo certChainFile, FileInfo keyFile,
            string keyPassword, SslStreamCertificateContext kmf, string keyStore)
    {
        string algorithm = "SunX509";
        return BuildKeyManagerFactory(certChainFile, algorithm, keyFile, keyPassword, kmf, keyStore);
    }

    /**
     * Build a {@link KeyManagerFactory} based upon a key file, key file password, and a certificate chain.
     * @param certChainFile an X.509 certificate chain file in PEM format
     * @param keyFile a PKCS#8 private key file in PEM format
     * @param keyPassword the password of the {@code keyFile}.
     *                    {@code null} if it's not password-protected.
     * @param kmf The existing {@link KeyManagerFactory} that will be used if not {@code null}
     * @return A {@link KeyManagerFactory} based upon a key file, key file password, and a certificate chain.
     * @deprecated will be removed.
     */
    // CLR: @deprecated names no replacement, so it is ported with [Obsolete] (owner decision 2026-11-16).
    // KeyStore.getDefaultType() is "PKCS12" (see SslContextBuilder).
    [Obsolete("will be removed.")]
    protected internal static SslStreamCertificateContext BuildKeyManagerFactory(FileInfo certChainFile,
            FileInfo keyFile, string keyPassword, SslStreamCertificateContext kmf)
    {
        return BuildKeyManagerFactory(certChainFile, keyFile, keyPassword, kmf, "PKCS12");
    }

    /**
     * Build a {@link KeyManagerFactory} based upon a key algorithm, key file, key file password,
     * and a certificate chain.
     * @param certChainFile an X.509 certificate chain file in PEM format
     * @param keyAlgorithm the standard name of the requested algorithm. See the Java Secure Socket Extension
     *                    Reference Guide for information about standard algorithm names.
     * @param keyFile a PKCS#8 private key file in PEM format
     * @param keyPassword the password of the {@code keyFile}.
     *                    {@code null} if it's not password-protected.
     * @param kmf The existing {@link KeyManagerFactory} that will be used if not {@code null}
     * @param keyStore the {@link KeyStore} that should be used in the {@link KeyManagerFactory}
     * @return A {@link KeyManagerFactory} based upon a key algorithm, key file, key file password,
     * and a certificate chain.
     */
    internal static SslStreamCertificateContext BuildKeyManagerFactory(FileInfo certChainFile,
            string keyAlgorithm, FileInfo keyFile, string keyPassword, SslStreamCertificateContext kmf,
            string keyStore)
    {
        return BuildKeyManagerFactory(ToX509Certificates(certChainFile), keyAlgorithm,
                                      ToPrivateKey(keyFile, keyPassword), keyPassword, kmf, keyStore);
    }

    /**
     * Build a {@link KeyManagerFactory} based upon a key algorithm, key file, key file password,
     * and a certificate chain.
     * @param certChainFile an buildKeyManagerFactory X.509 certificate chain file in PEM format
     * @param keyAlgorithm the standard name of the requested algorithm. See the Java Secure Socket Extension
     *                    Reference Guide for information about standard algorithm names.
     * @param keyFile a PKCS#8 private key file in PEM format
     * @param keyPassword the password of the {@code keyFile}.
     *                    {@code null} if it's not password-protected.
     * @param kmf The existing {@link KeyManagerFactory} that will be used if not {@code null}
     * @return A {@link KeyManagerFactory} based upon a key algorithm, key file, key file password,
     * and a certificate chain.
     * @deprecated will be removed.
     */
    // CLR: @deprecated names no replacement, so it is ported with [Obsolete] (owner decision 2026-11-16).
    [Obsolete("will be removed.")]
    protected internal static SslStreamCertificateContext BuildKeyManagerFactory(FileInfo certChainFile,
                                                              string keyAlgorithm, FileInfo keyFile,
                                                              string keyPassword, SslStreamCertificateContext kmf)
    {
        return BuildKeyManagerFactory(ToX509Certificates(certChainFile), keyAlgorithm,
                ToPrivateKey(keyFile, keyPassword), keyPassword, kmf, "PKCS12");
    }
}
