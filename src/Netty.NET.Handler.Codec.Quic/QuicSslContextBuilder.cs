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
using System.IO;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Ssl;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * Builder for configuring a new SslContext for creation.
 */
// CLR: the JSSE/JCA types map to the BCL types the SslStream options take, as the handler's SslContextBuilder maps them
// (docs/handler-ssl-design.md, owner decision 3): X509Certificate is X509Certificate2, PrivateKey AsymmetricAlgorithm,
// File FileInfo, TrustManagerFactory X509ChainPolicy, TrustManager RemoteCertificateValidationCallback (so
// InsecureTrustManagerFactory.INSTANCE is the handler's callback), KeyManagerFactory SslStreamCertificateContext and
// KeyManager ServerCertificateSelectionCallback (server) or LocalCertificateSelectionCallback (client); the
// TrustManagerFactoryWrapper/KeyManagerFactoryWrapper of the single-manager overloads are not needed, the callback is kept
// as it is. Mapping<? super String, ? extends QuicSslContext> is Func<string, QuicSslContext> (the SniHandler mapping).
// keyManager(key, password, chain) builds the key store and the factory with SslContext's helpers (the PKCS #12 round
// trip gives SChannel the persisted key it needs). build() creates the MsQuicSslContext of System.Net.Quic (the
// counterpart of QuicheQuicSslContext). BoringSSL-only settings keep their API and report NotSupportedException from
// Build() (owner decision 2026-11-27): early data (0-RTT) and key logging (BoringSSLKeylog, whose overload has no
// counterpart because the BoringSSL types are not ported); the SslContextOptions (BoringSSLContextOption GROUPS,
// SIGNATURE_ALGORITHMS, CLIENT_KEY_TYPES, SERVER_KEY_TYPES) are skipped by the context with a debug log, as quiche skips
// an option it does not know. The session cache size and timeout are kept for the API; msquic caches sessions itself
// (QuicClientSessionCache is jvm-only-replaced). SSLParameters.setEndpointIdentificationAlgorithm is the host name check
// of the context's validation callback (a null or empty algorithm ignores RemoteCertificateNameMismatch).
// UnsupportedOperationException, IllegalArgumentException and NullPointerException are NotSupportedException,
// ArgumentException and ArgumentNullException; @Nullable has no counterpart.
public sealed class QuicSslContextBuilder
{

    /**
     * Special {@link X509ExtendedKeyManager} implementation which will just fail the certificate selection.
     * This is used as a "dummy" implementation when SNI is used as we should always select an other
     * {@link QuicSslContext} based on the provided hostname.
     */
    // CLR: the KeyManager is a ServerCertificateSelectionCallback that selects no certificate (null), which fails the
    // handshake as an empty chain does.
    private static readonly ServerCertificateSelectionCallback SNI_KEYMANAGER = (sender, hostName) => null;

    /**
     * Creates a builder for new client-side {@link QuicSslContext} that can be used for {@code QUIC}.
     */
    public static QuicSslContextBuilder ForClient()
    {
        return new QuicSslContextBuilder(false);
    }

    /**
     * Creates a builder for new server-side {@link QuicSslContext} that can be used for {@code QUIC}.
     *
     * @param keyFile a PKCS#8 private key file in PEM format
     * @param keyPassword the password of the {@code keyFile}, or {@code null} if it's not
     *     password-protected
     * @param certChainFile an X.509 certificate chain file in PEM format
     * @see #keyManager(File, String, File)
     */
    public static QuicSslContextBuilder ForServer(
            FileInfo keyFile, string keyPassword, FileInfo certChainFile)
    {
        return new QuicSslContextBuilder(true).KeyManager(keyFile, keyPassword, certChainFile);
    }

    /**
     * Creates a builder for new server-side {@link QuicSslContext} that can be used for {@code QUIC}.
     *
     * @param key a PKCS#8 private key
     * @param keyPassword the password of the {@code keyFile}, or {@code null} if it's not
     *     password-protected
     * @param certChain the X.509 certificate chain
     * @see #keyManager(File, String, File)
     */
    public static QuicSslContextBuilder ForServer(
            AsymmetricAlgorithm key, string keyPassword, params X509Certificate2[] certChain)
    {
        return new QuicSslContextBuilder(true).KeyManager(key, keyPassword, certChain);
    }

    /**
     * Creates a builder for new server-side {@link QuicSslContext} that can be used for {@code QUIC}.
     *
     * @param keyManagerFactory non-{@code null} factory for server's private key
     * @see #keyManager(KeyManagerFactory, String)
     */
    public static QuicSslContextBuilder ForServer(SslStreamCertificateContext keyManagerFactory, string password)
    {
        return new QuicSslContextBuilder(true).KeyManager(keyManagerFactory, password);
    }

    /**
     * Creates a builder for new server-side {@link QuicSslContext} with {@link KeyManager} that can be used for
     * {@code QUIC}.
     *
     * @param keyManager non-{@code null} KeyManager for server's private key
     * @param keyPassword the password of the {@code keyFile}, or {@code null} if it's not
     *     password-protected
     */
    public static QuicSslContextBuilder ForServer(ServerCertificateSelectionCallback keyManager, string keyPassword)
    {
        return new QuicSslContextBuilder(true).KeyManager(keyManager, keyPassword);
    }

    /**
     * Enables support for
     * <a href="https://quicwg.org/ops-drafts/draft-ietf-quic-manageability.html#name-server-name-indication-sni">
     *     SNI</a> on the server side.
     *
     * @param mapping   the {@link Mapping} that is used to map names to the {@link QuicSslContext} to use.
     *                  Usually using {@link io.netty.util.DomainWildcardMappingBuilder} should be used
     *                  to create the {@link Mapping}.
     */
    public static QuicSslContext BuildForServerWithSni(Func<string, QuicSslContext> mapping)
    {
        return ForServer(SNI_KEYMANAGER, null).Sni(mapping).Build();
    }

    private readonly bool forServer;
    private readonly Dictionary<SslContextOption, object> options = new Dictionary<SslContextOption, object>();
    private X509ChainPolicy trustManagerFactory;
    private RemoteCertificateValidationCallback trustManager;
    private string keyPassword;
    private SslStreamCertificateContext keyManagerFactory;
    private ServerCertificateSelectionCallback serverKeyManager;
    private LocalCertificateSelectionCallback clientKeyManager;
    private long sessionCacheSize = 20480;
    private long sessionTimeout = 300;
    private ClientAuth clientAuth = Ssl.ClientAuth.NONE;
    private string[] applicationProtocols;
    private bool? earlyData;
    private bool keylog;
    private Func<string, QuicSslContext> mapping;
    private string endpointIdentificationAlgorithm;

    private QuicSslContextBuilder(bool forServer)
    {
        this.forServer = forServer;
        if (!forServer)
        {
             this.endpointIdentificationAlgorithm = MsQuicSslContext.defaultEndpointVerificationAlgorithm;
        }
    }

    /**
     * Enables
     * <a href="https://quicwg.org/ops-drafts/draft-ietf-quic-manageability.html#name-server-name-indication-sni">
     *     SNI</a> support on the server side.
     * <p>
     * The provided {@link Mapping} receives the hostname from the client and returns the
     * {@link QuicSslContext} to use for that connection. The returned context's settings
     * (such as {@link #clientAuth(ClientAuth)}, {@link #trustManager(TrustManagerFactory)}, etc.)
     * will be applied to the connection.
     * <p>
     * Use {@link io.netty.util.DomainWildcardMappingBuilder} to create the {@link Mapping} when
     * matching against domain patterns is needed.
     *
     * @param mapping the {@link Mapping} that maps hostnames to {@link QuicSslContext} instances
     * @return this builder
     * @throws NullPointerException if {@code mapping} is {@code null}
     */
    // CLR: the mapping selects the context of a connection from SslClientHelloInfo.ServerName when msquic asks for its
    // options (MsQuicServerCodec); SniCompletionEvent has no counterpart there and is not fired.
    public QuicSslContextBuilder Sni(Func<string, QuicSslContext> mapping)
    {
        if (!forServer)
        {
            throw new NotSupportedException("Only supported for server");
        }
        this.mapping = ObjectUtil.CheckNotNull(mapping, "mapping");
        return this;
    }

    /**
     * Configure a {@link SslContextOption}.
     */
    public QuicSslContextBuilder Option<T>(SslContextOption<T> option, T value)
    {
        if (value == null)
        {
            options.Remove(option);
        }
        else
        {
            options[option] = value;
        }
        return this;
    }

    /**
     * Enable / disable the usage of early data.
     */
    // CLR: 0-RTT early data is not exposed by System.Net.Quic; EarlyData(true) reports NotSupportedException from Build().
    public QuicSslContextBuilder EarlyData(bool enabled)
    {
        this.earlyData = enabled;
        return this;
    }

    /**
     * Enable / disable keylog. When enabled, TLS keys are logged to an internal logger named
     * "io.netty.handler.codec.quic.BoringSSLLogginKeylog" with DEBUG level, see
     * {@link BoringSSLKeylog} for detail, logging keys are following
     * <a href="https://developer.mozilla.org/en-US/docs/Mozilla/Projects/NSS/Key_Log_Format">
     *     NSS Key Log Format</a>. This is intended for debugging use with tools like Wireshark.
     */
    // CLR: key logging is BoringSSL's; Keylog(true) reports NotSupportedException from Build().
    public QuicSslContextBuilder Keylog(bool enabled)
    {
        keylog = enabled;
        return this;
    }

    /**
     * Enable / disable keylog. When enabled, TLS keys are logged to {@link BoringSSLKeylog#logKey(SSLEngine, String)}
     * logging keys are following
     * <a href="https://developer.mozilla.org/en-US/docs/Mozilla/Projects/NSS/Key_Log_Format">
     *     NSS Key Log Format</a>. This is intended for debugging use with tools like Wireshark.
     */
    // CLR: keylog(BoringSSLKeylog) has no counterpart: BoringSSLKeylog is a JNI unit (jvm-only-replaced).

    /**
     * Trusted certificates for verifying the remote endpoint's certificate. The file should
     * contain an X.509 certificate collection in PEM format. {@code null} uses the system default
     * which only works with Java 8u261 and later as these versions support TLS1.3,
     * see <a href="https://www.oracle.com/java/technologies/javase/8u261-relnotes.html">
     *     JDK 8u261 Update Release Notes</a>
     */
    public QuicSslContextBuilder TrustManager(FileInfo trustCertCollectionFile)
    {
        try
        {
            return TrustManager(MsQuicSslContext.ToX509Certificates0(trustCertCollectionFile));
        }
        catch (Exception e)
        {
            throw new ArgumentException("File does not contain valid certificates: "
                    + trustCertCollectionFile, e);
        }
    }

    /**
     * Trusted certificates for verifying the remote endpoint's certificate. {@code null} uses the system default
     * which only works with Java 8u261 and later as these versions support TLS1.3,
     * see <a href="https://www.oracle.com/java/technologies/javase/8u261-relnotes.html">
     *     JDK 8u261 Update Release Notes</a>
     */
    public QuicSslContextBuilder TrustManager(params X509Certificate2[] trustCertCollection)
    {
        try
        {
            return TrustManager(trustCertCollection == null
                    ? (X509ChainPolicy) null
                    : MsQuicSslContext.BuildTrustManagerFactory0(trustCertCollection));
        }
        catch (Exception e)
        {
            throw new ArgumentException(ThrowableUtil.ThrowableToString(e), e);
        }
    }

    /**
     * Trusted manager for verifying the remote endpoint's certificate. {@code null} uses the system default
     * which only works with Java 8u261 and later as these versions support TLS1.3,
     * see <a href="https://www.oracle.com/java/technologies/javase/8u261-relnotes.html">
     *     JDK 8u261 Update Release Notes</a>
     */
    public QuicSslContextBuilder TrustManager(X509ChainPolicy trustManagerFactory)
    {
        this.trustManagerFactory = trustManagerFactory;
        this.trustManager = null;
        return this;
    }

    /**
     * A single trusted manager for verifying the remote endpoint's certificate.
     * This is helpful when custom implementation of {@link TrustManager} is needed.
     * Internally, a simple wrapper of {@link TrustManagerFactory} that only produces this
     * specified {@link TrustManager} will be created, thus all the requirements specified in
     * {@link #trustManager(TrustManagerFactory trustManagerFactory)} also apply here.
     */
    public QuicSslContextBuilder TrustManager(RemoteCertificateValidationCallback trustManager)
    {
        this.trustManagerFactory = null;
        this.trustManager = trustManager;
        return this;
    }

    /**
     * Identifying certificate for this host. {@code keyCertChainFile} and {@code keyFile} may
     * be {@code null} for client contexts, which disables mutual authentication.
     *
     * @param keyFile a PKCS#8 private key file in PEM format
     * @param keyPassword the password of the {@code keyFile}, or {@code null} if it's not
     *     password-protected
     * @param keyCertChainFile an X.509 certificate chain file in PEM format
     */
    public QuicSslContextBuilder KeyManager(
            FileInfo keyFile, string keyPassword, FileInfo keyCertChainFile)
    {
        X509Certificate2[] keyCertChain;
        AsymmetricAlgorithm key;
        try
        {
            keyCertChain = MsQuicSslContext.ToX509Certificates0(keyCertChainFile);
        }
        catch (Exception e)
        {
            throw new ArgumentException("File does not contain valid certificates: " + keyCertChainFile, e);
        }
        try
        {
            key = MsQuicSslContext.ToPrivateKey0(keyFile, keyPassword);
        }
        catch (Exception e)
        {
            throw new ArgumentException("File does not contain valid private key: " + keyFile, e);
        }
        return KeyManager(key, keyPassword, keyCertChain);
    }

    /**
     * Identifying certificate for this host. {@code keyCertChain} and {@code key} may
     * be {@code null} for client contexts, which disables mutual authentication.
     *
     * @param key a PKCS#8 private key file
     * @param keyPassword the password of the {@code key}, or {@code null} if it's not
     *     password-protected
     * @param certChain an X.509 certificate chain
     */
    // CLR: the KeyStore with its key entry and the KeyManagerFactory initialized from it are the SslStreamCertificateContext
    // of SslContext.buildKeyManagerFactory (see the class comment); a null key or chain disables the key manager.
    public QuicSslContextBuilder KeyManager(
            AsymmetricAlgorithm key, string keyPassword, params X509Certificate2[] certChain)
    {
        if (key == null || certChain == null || certChain.Length == 0)
        {
            return KeyManager((SslStreamCertificateContext) null, keyPassword);
        }
        try
        {
            SslStreamCertificateContext keyManagerFactory =
                    MsQuicSslContext.BuildKeyManagerFactory0(certChain, key, keyPassword);
            return KeyManager(keyManagerFactory, keyPassword);
        }
        catch (Exception e)
        {
            throw new ArgumentException(ThrowableUtil.ThrowableToString(e), e);
        }
    }

    /**
     * Identifying manager for this host. {@code keyManagerFactory} may be {@code null} for
     * client contexts, which disables mutual authentication.
     */
    public QuicSslContextBuilder KeyManager(
            SslStreamCertificateContext keyManagerFactory, string keyPassword)
    {
        this.keyPassword = keyPassword;
        this.keyManagerFactory = keyManagerFactory;
        this.serverKeyManager = null;
        this.clientKeyManager = null;
        return this;
    }

    /**
     * A single key manager managing the identity information of this host.
     * This is helpful when custom implementation of {@link KeyManager} is needed.
     * Internally, a wrapper of {@link KeyManagerFactory} that only produces this specified
     * {@link KeyManager} will be created, thus all the requirements specified in
     * {@link #keyManager(KeyManagerFactory, String)} also apply here.
     */
    // CLR: the server's KeyManager (see the class comment).
    public QuicSslContextBuilder KeyManager(ServerCertificateSelectionCallback keyManager, string password)
    {
        KeyManager((SslStreamCertificateContext) null, password);
        this.serverKeyManager = ObjectUtil.CheckNotNull(keyManager, "keyManager");
        return this;
    }

    // CLR-only: the client's KeyManager (keyManager(KeyManager, String) of a client builder), as the handler's
    // SslContextBuilder splits it.
    public QuicSslContextBuilder KeyManager(LocalCertificateSelectionCallback keyManager, string password)
    {
        KeyManager((SslStreamCertificateContext) null, password);
        this.clientKeyManager = ObjectUtil.CheckNotNull(keyManager, "keyManager");
        return this;
    }

    /**
     * Application protocol negotiation configuration. {@code null} disables support.
     */
    public QuicSslContextBuilder ApplicationProtocols(params string[] applicationProtocols)
    {
        this.applicationProtocols = applicationProtocols;
        return this;
    }

    /**
     * Set the size of the cache used for storing SSL session objects. {@code 0} to use the
     * default value.
     */
    public QuicSslContextBuilder SessionCacheSize(long sessionCacheSize)
    {
        this.sessionCacheSize = sessionCacheSize;
        return this;
    }

    /**
     * Set the timeout for the cached SSL session objects, in seconds. {@code 0} to use the
     * default value.
     */
    public QuicSslContextBuilder SessionTimeout(long sessionTimeout)
    {
        this.sessionTimeout = sessionTimeout;
        return this;
    }

    /**
     * Sets the client authentication mode.
     */
    // CLR: ClientAuth is an enum (a value type), so checkNotNull has no counterpart.
    public QuicSslContextBuilder ClientAuth(ClientAuth clientAuth)
    {
        if (!forServer)
        {
            throw new NotSupportedException("Only supported for server");
        }
        this.clientAuth = clientAuth;
        return this;
    }

    /**
     * Specify the endpoint identification algorithm (aka. hostname verification algorithm) that clients will use as
     * part of authenticating servers.
     * <p>
     * See <a href="https://docs.oracle.com/javase/8/docs/technotes/guides/security/StandardNames.html#jssenames">
     *     Java Security Standard Names</a> for a list of supported algorithms.
     *
     * @param algorithm either {@code "HTTPS"}, {@code "LDAPS"}, or {@code null} (disables hostname verification).
     * @see SSLParameters#setEndpointIdentificationAlgorithm(String)
     */
    public QuicSslContextBuilder EndpointIdentificationAlgorithm(string algorithm)
    {
        endpointIdentificationAlgorithm = algorithm;
        return this;
    }

    /**
     * Create new {@link QuicSslContext} instance with configured settings that can be used for {@code QUIC}.
     *
     */
    public QuicSslContext Build()
    {
        // CLR: the BoringSSL-only settings (see the class comment).
        if (earlyData == true)
        {
            throw new NotSupportedException("earlyData(true) is not supported by System.Net.Quic (msquic)");
        }
        if (keylog)
        {
            throw new NotSupportedException("keylog(true) is not supported by System.Net.Quic (msquic)");
        }
        if (forServer)
        {
            return new MsQuicSslContext(true, sessionTimeout, sessionCacheSize, clientAuth, trustManagerFactory,
                    trustManager, keyManagerFactory, serverKeyManager, clientKeyManager, mapping,
                    applicationProtocols, null, ToArray(options));
        }
        else
        {
            return new MsQuicSslContext(false, sessionTimeout, sessionCacheSize, clientAuth, trustManagerFactory,
                    trustManager, keyManagerFactory, serverKeyManager, clientKeyManager, mapping,
                    applicationProtocols, endpointIdentificationAlgorithm, ToArray(options));
        }
    }

    // CLR: toArray(Iterable, T[]) of the option entries; the prototype only gave the element type.
    private static KeyValuePair<SslContextOption, object>[] ToArray(
            IEnumerable<KeyValuePair<SslContextOption, object>> iterable)
    {
        if (iterable == null)
        {
            return null;
        }
        List<KeyValuePair<SslContextOption, object>> list = new List<KeyValuePair<SslContextOption, object>>();
        foreach (KeyValuePair<SslContextOption, object> element in iterable)
        {
            list.Add(element);
        }
        return list.ToArray();
    }
}
