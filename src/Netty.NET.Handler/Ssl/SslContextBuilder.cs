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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Ssl;

/**
 * Builder for configuring a new SslContext for creation.
 */
// CLR: the fluent API is kept with the JSSE/JCA parameter types mapped to the BCL types the SslStream options take
// (owner decision 2026-11-24, docs/handler-ssl-design.md; as pkitesting X509Bundle maps them,
// src/Netty.NET.PkiTesting/X509Bundle.cs): PrivateKey is AsymmetricAlgorithm, X509Certificate is X509Certificate2,
// File/InputStream are FileInfo/System.IO.Stream, Iterable is IEnumerable, TrustManagerFactory is X509ChainPolicy (the
// options' CertificateChainPolicy, "an optional customized policy for remote certificate validation",
// https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslclientauthenticationoptions?view=net-10.0),
// TrustManager is a RemoteCertificateValidationCallback, KeyManagerFactory is SslStreamCertificateContext (the options'
// ServerCertificateContext/ClientCertificateContext) and KeyManager is the ServerCertificateSelectionCallback of a server
// or the LocalCertificateSelectionCallback of a client (the two BCL delegates that pick the local certificate per
// connection, https://learn.microsoft.com/en-us/dotnet/api/system.net.security.servercertificateselectioncallback?view=net-10.0,
// https://learn.microsoft.com/en-us/dotnet/api/system.net.security.localcertificateselectioncallback?view=net-10.0). A
// callback is kept in its own field next to the factory field (the JCA TrustManagerFactoryWrapper/KeyManagerFactoryWrapper
// that wrap them are jvm-only-replaced) and setting either clears the other, as setting a factory clears the
// certificates. SecureRandom is RandomNumberGenerator. The JCA java.security.Provider of sslContextProvider(Provider)
// has no counterpart and OpenSslCredential (addCredential/addCredentials) is library-not-ported; their Javadoc is
// recorded. SNIServerName is the host name string: only SNIHostName is supported, and SslStream sends the
// SslClientAuthenticationOptions.TargetHost as the SNI name. KeyStore.getDefaultType() is "PKCS12" (the SunJSSE KeyStore,
// JSSE Reference Guide table 8-1; the BCL writes PKCS #12, see SslContext.BuildKeyStore). Map.Entry[] of the options is
// KeyValuePair<SslContextOption, object>[] and HashMap is Dictionary. Inside this class the method names SslProvider,
// ClientAuth and ApplicationProtocolConfig hide the types of the same names in expressions, so those are qualified.
public sealed class SslContextBuilder
{

    /**
     * Creates a builder for new client-side {@link SslContext}.
     */
    public static SslContextBuilder ForClient()
    {
        return new SslContextBuilder(false);
    }

    /**
     * Creates a builder for new server-side {@link SslContext}.
     *
     * @param keyCertChainFile an X.509 certificate chain file in PEM format
     * @param keyFile a PKCS#8 private key file in PEM format
     * @see #keyManager(File, File)
     */
    public static SslContextBuilder ForServer(FileInfo keyCertChainFile, FileInfo keyFile)
    {
        return new SslContextBuilder(true).KeyManager(keyCertChainFile, keyFile);
    }

    /**
     * Creates a builder for new server-side {@link SslContext}.
     *
     * @param keyCertChainInputStream   an input stream for an X.509 certificate chain in PEM format. The caller is
     *                                  responsible for calling {@link InputStream#close()} after {@link #build()}
     *                                  has been called.
     * @param keyInputStream            an input stream for a PKCS#8 private key in PEM format. The caller is
     *                                  responsible for calling {@link InputStream#close()} after {@link #build()}
     *                                  has been called.
     *
     * @see #keyManager(InputStream, InputStream)
     */
    public static SslContextBuilder ForServer(System.IO.Stream keyCertChainInputStream, System.IO.Stream keyInputStream)
    {
        return new SslContextBuilder(true).KeyManager(keyCertChainInputStream, keyInputStream);
    }

    /**
     * Creates a builder for new server-side {@link SslContext}.
     *
     * @param key a PKCS#8 private key
     * @param keyCertChain the X.509 certificate chain
     * @see #keyManager(PrivateKey, X509Certificate[])
     */
    public static SslContextBuilder ForServer(AsymmetricAlgorithm key, params X509Certificate2[] keyCertChain)
    {
        return new SslContextBuilder(true).KeyManager(key, keyCertChain);
    }

    /**
     * Creates a builder for new server-side {@link SslContext}.
     *
     * @param key a PKCS#8 private key
     * @param keyCertChain the X.509 certificate chain
     * @see #keyManager(PrivateKey, X509Certificate[])
     */
    public static SslContextBuilder ForServer(AsymmetricAlgorithm key, IEnumerable<X509Certificate2> keyCertChain)
    {
        return ForServer(key, ToArray(keyCertChain));
    }

    /**
     * Creates a builder for new server-side {@link SslContext}.
     *
     * @param keyCertChainFile an X.509 certificate chain file in PEM format
     * @param keyFile a PKCS#8 private key file in PEM format
     * @param keyPassword the password of the {@code keyFile}, or {@code null} if it's not
     *     password-protected
     * @see #keyManager(File, File, String)
     */
    public static SslContextBuilder ForServer(
            FileInfo keyCertChainFile, FileInfo keyFile, string keyPassword)
    {
        return new SslContextBuilder(true).KeyManager(keyCertChainFile, keyFile, keyPassword);
    }

    /**
     * Creates a builder for new server-side {@link SslContext}.
     *
     * @param keyCertChainInputStream   an input stream for an X.509 certificate chain in PEM format. The caller is
     *                                  responsible for calling {@link InputStream#close()} after {@link #build()}
     *                                  has been called.
     * @param keyInputStream            an input stream for a PKCS#8 private key in PEM format. The caller is
     *                                  responsible for calling {@link InputStream#close()} after {@link #build()}
     *                                  has been called.
     * @param keyPassword the password of the {@code keyFile}, or {@code null} if it's not
     *     password-protected
     * @see #keyManager(InputStream, InputStream, String)
     */
    public static SslContextBuilder ForServer(
            System.IO.Stream keyCertChainInputStream, System.IO.Stream keyInputStream, string keyPassword)
    {
        return new SslContextBuilder(true).KeyManager(keyCertChainInputStream, keyInputStream, keyPassword);
    }

    /**
     * Creates a builder for new server-side {@link SslContext}.
     *
     * @param key a PKCS#8 private key
     * @param keyCertChain the X.509 certificate chain
     * @param keyPassword the password of the {@code keyFile}, or {@code null} if it's not
     *     password-protected
     * @see #keyManager(File, File, String)
     */
    public static SslContextBuilder ForServer(
            AsymmetricAlgorithm key, string keyPassword, params X509Certificate2[] keyCertChain)
    {
        return new SslContextBuilder(true).KeyManager(key, keyPassword, keyCertChain);
    }

    /**
     * Creates a builder for new server-side {@link SslContext}.
     *
     * @param key a PKCS#8 private key
     * @param keyCertChain the X.509 certificate chain
     * @param keyPassword the password of the {@code keyFile}, or {@code null} if it's not
     *     password-protected
     * @see #keyManager(File, File, String)
     */
    public static SslContextBuilder ForServer(
            AsymmetricAlgorithm key, string keyPassword, IEnumerable<X509Certificate2> keyCertChain)
    {
        return ForServer(key, keyPassword, ToArray(keyCertChain));
    }

    /**
     * Creates a builder for new server-side {@link SslContext}.
     * <p>
     * If you use {@link SslProvider#OPENSSL} or {@link SslProvider#OPENSSL_REFCNT} consider using
     * {@link OpenSslX509KeyManagerFactory} or {@link OpenSslCachingX509KeyManagerFactory}.
     *
     * @param keyManagerFactory non-{@code null} factory for server's private key
     * @see #keyManager(KeyManagerFactory)
     */
    public static SslContextBuilder ForServer(SslStreamCertificateContext keyManagerFactory)
    {
        return new SslContextBuilder(true).KeyManager(keyManagerFactory);
    }

    /**
     * Creates a builder for new server-side {@link SslContext} with {@link KeyManager}.
     *
     * @param keyManager non-{@code null} KeyManager for server's private key
     */
    public static SslContextBuilder ForServer(ServerCertificateSelectionCallback keyManager)
    {
        return new SslContextBuilder(true).KeyManager(keyManager);
    }

    private readonly bool forServer;
    private SslProvider provider;
    private X509Certificate2[] trustCertCollection;
    private X509ChainPolicy trustManagerFactory;
    // CLR: the TrustManager of trustManager(TrustManager), kept as the callback (see the class comment).
    private RemoteCertificateValidationCallback trustManager;
    private X509Certificate2[] keyCertChain;
    private AsymmetricAlgorithm key;
    private string keyPassword;
    private SslStreamCertificateContext keyManagerFactory;
    // CLR: the KeyManager of keyManager(KeyManager), kept as the callback of its side (see the class comment).
    private ServerCertificateSelectionCallback serverKeyManager;
    private LocalCertificateSelectionCallback clientKeyManager;
    private IEnumerable<string> ciphers;
    private ICipherSuiteFilter cipherFilter = IdentityCipherSuiteFilter.INSTANCE;
    private ApplicationProtocolConfig apn;
    private long sessionCacheSize;
    private long sessionTimeout;
    private ClientAuth clientAuth = global::Netty.NET.Handler.Ssl.ClientAuth.NONE;
    private string[] protocols;
    private bool startTls;
    private bool enableOcsp;
    private RandomNumberGenerator secureRandom;
    private string keyStoreType = "PKCS12";
    private string endpointIdentificationAlgorithm;
    private readonly Dictionary<SslContextOption, object> options = new Dictionary<SslContextOption, object>();
    private readonly List<string> serverNames;

    private SslContextBuilder(bool forServer)
    {
        this.forServer = forServer;

        if (!forServer)
        {
            endpointIdentificationAlgorithm = SslContext.defaultEndpointVerificationAlgorithm;
        }

        serverNames = forServer ? null : new List<string>(2); // Only for clients.
    }

    /**
     * Configure a {@link SslContextOption}.
     */
    public SslContextBuilder Option<T>(SslContextOption<T> option, T value)
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
     * The {@link SslContext} implementation to use. {@code null} uses the default one.
     */
    public SslContextBuilder SslProvider(SslProvider provider)
    {
        this.provider = provider;
        return this;
    }

    /**
     * Sets the {@link KeyStore} type that should be used. {@code null} uses the default one.
     */
    public SslContextBuilder KeyStoreType(string keyStoreType)
    {
        this.keyStoreType = keyStoreType;
        return this;
    }

    /**
     * Trusted certificates for verifying the remote endpoint's certificate. The file should
     * contain an X.509 certificate collection in PEM format. {@code null} uses the system default.
     */
    // CLR: IllegalArgumentException is ArgumentException; the file is printed as File.toString() prints its path
    // (FileInfo.ToString()).
    public SslContextBuilder TrustManager(FileInfo trustCertCollectionFile)
    {
        try
        {
            return TrustManager(SslContext.ToX509Certificates(trustCertCollectionFile));
        }
        catch (Exception e)
        {
            throw new ArgumentException("File does not contain valid certificates: "
                    + trustCertCollectionFile, e);
        }
    }

    /**
     * Trusted certificates for verifying the remote endpoint's certificate. The input stream should
     * contain an X.509 certificate collection in PEM format. {@code null} uses the system default.
     * <p>
     * The caller is responsible for calling {@link InputStream#close()} after {@link #build()} has been called.
     */
    public SslContextBuilder TrustManager(System.IO.Stream trustCertCollectionInputStream)
    {
        try
        {
            return TrustManager(SslContext.ToX509Certificates(trustCertCollectionInputStream));
        }
        catch (Exception e)
        {
            throw new ArgumentException("Input stream does not contain valid certificates.", e);
        }
    }

    /**
     * Trusted certificates for verifying the remote endpoint's certificate, {@code null} uses the system default.
     */
    public SslContextBuilder TrustManager(params X509Certificate2[] trustCertCollection)
    {
        this.trustCertCollection = trustCertCollection != null ? (X509Certificate2[]) trustCertCollection.Clone() : null;
        trustManagerFactory = null;
        trustManager = null;
        return this;
    }

    /**
     * Trusted certificates for verifying the remote endpoint's certificate, {@code null} uses the system default.
     */
    public SslContextBuilder TrustManager(IEnumerable<X509Certificate2> trustCertCollection)
    {
        return TrustManager(ToArray(trustCertCollection));
    }

    /**
     * Trusted manager for verifying the remote endpoint's certificate. {@code null} uses the system default.
     */
    public SslContextBuilder TrustManager(X509ChainPolicy trustManagerFactory)
    {
        trustCertCollection = null;
        this.trustManagerFactory = trustManagerFactory;
        trustManager = null;
        return this;
    }

    /**
     * A single trusted manager for verifying the remote endpoint's certificate.
     * This is helpful when custom implementation of {@link TrustManager} is needed.
     * Internally, a simple wrapper of {@link TrustManagerFactory} that only produces this
     * specified {@link TrustManager} will be created, thus all the requirements specified in
     * {@link #trustManager(TrustManagerFactory trustManagerFactory)} also apply here.
     */
    // CLR: the TrustManagerFactoryWrapper (jvm-only-replaced) is not needed: the callback is kept as it is (see the
    // class comment). The callback receives SslStream's verdict as SslPolicyErrors, with RemoteCertificateNameMismatch
    // cleared when endpoint identification is disabled, and decides like an X509ExtendedTrustManager.
    public SslContextBuilder TrustManager(RemoteCertificateValidationCallback trustManager)
    {
        trustManagerFactory = null;
        this.trustManager = trustManager;
        trustCertCollection = null;
        return this;
    }

    /**
     * Identifying certificate for this host. {@code keyCertChainFile} and {@code keyFile} may
     * be {@code null} for client contexts, which disables mutual authentication.
     *
     * @param keyCertChainFile an X.509 certificate chain file in PEM format
     * @param keyFile a PKCS#8 private key file in PEM format
     */
    public SslContextBuilder KeyManager(FileInfo keyCertChainFile, FileInfo keyFile)
    {
        return KeyManager(keyCertChainFile, keyFile, null);
    }

    /**
     * Identifying certificate for this host. {@code keyCertChainInputStream} and {@code keyInputStream} may
     * be {@code null} for client contexts, which disables mutual authentication.
     *
     * @param keyCertChainInputStream   an input stream for an X.509 certificate chain in PEM format. The caller is
     *                                  responsible for calling {@link InputStream#close()} after {@link #build()}
     *                                  has been called.
     * @param keyInputStream            an input stream for a PKCS#8 private key in PEM format. The caller is
     *                                  responsible for calling {@link InputStream#close()} after {@link #build()}
     *                                  has been called.
     */
    public SslContextBuilder KeyManager(System.IO.Stream keyCertChainInputStream, System.IO.Stream keyInputStream)
    {
        return KeyManager(keyCertChainInputStream, keyInputStream, null);
    }

    /**
     * Identifying certificate for this host. {@code keyCertChain} and {@code key} may
     * be {@code null} for client contexts, which disables mutual authentication.
     *
     * @param key a PKCS#8 private key
     * @param keyCertChain an X.509 certificate chain
     */
    public SslContextBuilder KeyManager(AsymmetricAlgorithm key, params X509Certificate2[] keyCertChain)
    {
        return KeyManager(key, null, keyCertChain);
    }

    /**
     * Identifying certificate for this host. {@code keyCertChain} and {@code key} may
     * be {@code null} for client contexts, which disables mutual authentication.
     *
     * @param key a PKCS#8 private key
     * @param keyCertChain an X.509 certificate chain
     */
    public SslContextBuilder KeyManager(AsymmetricAlgorithm key, IEnumerable<X509Certificate2> keyCertChain)
    {
        return KeyManager(key, ToArray(keyCertChain));
    }

    /**
     * Identifying certificate for this host. {@code keyCertChainFile} and {@code keyFile} may
     * be {@code null} for client contexts, which disables mutual authentication.
     *
     * @param keyCertChainFile an X.509 certificate chain file in PEM format
     * @param keyFile a PKCS#8 private key file in PEM format
     * @param keyPassword the password of the {@code keyFile}, or {@code null} if it's not
     *     password-protected
     */
    public SslContextBuilder KeyManager(FileInfo keyCertChainFile, FileInfo keyFile, string keyPassword)
    {
        X509Certificate2[] keyCertChain;
        AsymmetricAlgorithm key;

        try
        {
            keyCertChain = SslContext.ToX509Certificates(keyCertChainFile);
        }
        catch (Exception e)
        {
            throw new ArgumentException("File does not contain valid certificates: " + keyCertChainFile, e);
        }

        try
        {
            key = SslContext.ToPrivateKey(keyFile, keyPassword);
        }
        catch (Exception e)
        {
            throw new ArgumentException("File does not contain valid private key: " + keyFile, e);
        }

        return KeyManager(key, keyPassword, keyCertChain);
    }

    /**
     * Identifying certificate for this host. {@code keyCertChainInputStream} and {@code keyInputStream} may
     * be {@code null} for client contexts, which disables mutual authentication.
     *
     * @param keyCertChainInputStream   an input stream for an X.509 certificate chain in PEM format. The caller is
     *                                  responsible for calling {@link InputStream#close()} after {@link #build()}
     *                                  has been called.
     * @param keyInputStream            an input stream for a PKCS#8 private key in PEM format. The caller is
     *                                  responsible for calling {@link InputStream#close()} after {@link #build()}
     *                                  has been called.
     * @param keyPassword the password of the {@code keyInputStream}, or {@code null} if it's not
     *     password-protected
     */
    public SslContextBuilder KeyManager(System.IO.Stream keyCertChainInputStream, System.IO.Stream keyInputStream,
            string keyPassword)
    {
        X509Certificate2[] keyCertChain;
        AsymmetricAlgorithm key;

        try
        {
            keyCertChain = SslContext.ToX509Certificates(keyCertChainInputStream);
        }
        catch (Exception e)
        {
            throw new ArgumentException("Input stream not contain valid certificates.", e);
        }

        try
        {
            key = SslContext.ToPrivateKey(keyInputStream, keyPassword);
        }
        catch (Exception e)
        {
            throw new ArgumentException("Input stream does not contain valid private key.", e);
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
     * @param keyCertChain an X.509 certificate chain
     */
    public SslContextBuilder KeyManager(AsymmetricAlgorithm key, string keyPassword,
            params X509Certificate2[] keyCertChain)
    {
        if (forServer)
        {
            ObjectUtil.CheckNonEmpty(keyCertChain, "keyCertChain");
            ObjectUtil.CheckNotNull(key, "key required for servers");
        }

        if (keyCertChain == null || keyCertChain.Length == 0)
        {
            this.keyCertChain = null;
        }
        else
        {
            foreach (X509Certificate2 cert in keyCertChain)
            {
                ObjectUtil.CheckNotNullWithIAE(cert, "cert");
            }

            this.keyCertChain = (X509Certificate2[]) keyCertChain.Clone();
        }

        this.key = key;
        this.keyPassword = keyPassword;
        keyManagerFactory = null;
        serverKeyManager = null;
        clientKeyManager = null;
        return this;
    }

    /**
     * Identifying certificate for this host. {@code keyCertChain} and {@code key} may
     * be {@code null} for client contexts, which disables mutual authentication.
     *
     * @param key a PKCS#8 private key file
     * @param keyPassword the password of the {@code key}, or {@code null} if it's not
     *     password-protected
     * @param keyCertChain an X.509 certificate chain
     */
    public SslContextBuilder KeyManager(AsymmetricAlgorithm key, string keyPassword,
                                        IEnumerable<X509Certificate2> keyCertChain)
    {
        return KeyManager(key, keyPassword, ToArray(keyCertChain));
    }

    /**
     * Identifying manager for this host. {@code keyManagerFactory} may be {@code null} for
     * client contexts, which disables mutual authentication. Using a {@link KeyManagerFactory}
     * is only supported for {@link SslProvider#JDK} or {@link SslProvider#OPENSSL} / {@link SslProvider#OPENSSL_REFCNT}
     * if the used openssl version is 1.0.1+. You can check if your openssl version supports using a
     * {@link KeyManagerFactory} by calling {@link OpenSsl#supportsKeyManagerFactory()}. If this is not the case
     * you must use {@link #keyManager(File, File)} or {@link #keyManager(File, File, String)}.
     * <p>
     * If you use {@link SslProvider#OPENSSL} or {@link SslProvider#OPENSSL_REFCNT} consider using
     * {@link OpenSslX509KeyManagerFactory} or {@link OpenSslCachingX509KeyManagerFactory}.
     */
    public SslContextBuilder KeyManager(SslStreamCertificateContext keyManagerFactory)
    {
        if (forServer)
        {
            ObjectUtil.CheckNotNull(keyManagerFactory, "keyManagerFactory required for servers");
        }

        keyCertChain = null;
        key = null;
        keyPassword = null;
        serverKeyManager = null;
        clientKeyManager = null;
        this.keyManagerFactory = keyManagerFactory;
        return this;
    }

    /**
     * A single key manager managing the identity information of this host.
     * This is helpful when custom implementation of {@link KeyManager} is needed.
     * Internally, a wrapper of {@link KeyManagerFactory} that only produces this specified
     * {@link KeyManager} will be created, thus all the requirements specified in
     * {@link #keyManager(KeyManagerFactory keyManagerFactory)} also apply here.
     */
    // CLR: a KeyManager chooses the server alias for a server and the client alias for a client; SslStream takes the
    // first as SslServerAuthenticationOptions.ServerCertificateSelectionCallback and the second as
    // SslClientAuthenticationOptions.LocalCertificateSelectionCallback, two delegate types, so keyManager(KeyManager) has
    // this overload and KeyManager(LocalCertificateSelectionCallback). The KeyManagerFactoryWrapper (jvm-only-replaced) is
    // not needed; the callback is kept as it is.
    public SslContextBuilder KeyManager(ServerCertificateSelectionCallback keyManager)
    {
        if (forServer)
        {
            ObjectUtil.CheckNotNull(keyManager, "keyManager required for servers");
        }
        else if (keyManager != null)
        {
            throw new ArgumentException("keyManager of a client is a LocalCertificateSelectionCallback");
        }

        keyManagerFactory = null;
        serverKeyManager = keyManager;
        clientKeyManager = null;
        keyCertChain = null;
        key = null;
        keyPassword = null;
        return this;
    }

    // CLR: the client-side KeyManager; see KeyManager(ServerCertificateSelectionCallback). A server builder requires a
    // ServerCertificateSelectionCallback, as a server KeyManager must choose a server alias, and a client builder
    // rejects one.
    public SslContextBuilder KeyManager(LocalCertificateSelectionCallback keyManager)
    {
        if (forServer)
        {
            throw new ArgumentException("keyManager required for servers is a ServerCertificateSelectionCallback");
        }

        keyManagerFactory = null;
        serverKeyManager = null;
        clientKeyManager = keyManager;
        keyCertChain = null;
        key = null;
        keyPassword = null;
        return this;
    }

    /**
     * The cipher suites to enable, in the order of preference. {@code null} to use default
     * cipher suites.
     */
    public SslContextBuilder Ciphers(IEnumerable<string> ciphers)
    {
        return Ciphers(ciphers, IdentityCipherSuiteFilter.INSTANCE);
    }

    /**
     * The cipher suites to enable, in the order of preference. {@code cipherFilter} will be
     * applied to the ciphers before use. If {@code ciphers} is {@code null}, then the default
     * cipher suites will be used.
     */
    // CLR: SslStream restricts cipher suites with a CipherSuitesPolicy, which can only be created "if you're using Linux
    // systems with OpenSSL 1.1.1 or higher or macOS" (PlatformNotSupportedException elsewhere,
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.security.ciphersuitespolicy.-ctor?view=net-10.0); on Windows
    // a context built with non-null ciphers therefore fails in newHandler (see JdkSslContext), while null ciphers keep
    // the operating system's choice.
    public SslContextBuilder Ciphers(IEnumerable<string> ciphers, ICipherSuiteFilter cipherFilter)
    {
        this.cipherFilter = ObjectUtil.CheckNotNull(cipherFilter, "cipherFilter");
        this.ciphers = ciphers;
        return this;
    }

    /**
     * Application protocol negotiation configuration. {@code null} disables support.
     */
    public SslContextBuilder ApplicationProtocolConfig(ApplicationProtocolConfig apn)
    {
        this.apn = apn;
        return this;
    }

    /**
     * Set the size of the cache used for storing SSL session objects. {@code 0} to use the
     * default value.
     */
    // CLR: kept for the API; SslStream has no session cache size setting (docs/handler-ssl-design.md, Known
    // differences), so JdkSslContext does not apply it.
    public SslContextBuilder SessionCacheSize(long sessionCacheSize)
    {
        this.sessionCacheSize = sessionCacheSize;
        return this;
    }

    /**
     * Set the timeout for the cached SSL session objects, in seconds. {@code 0} to use the
     * default value.
     */
    // CLR: kept for the API; SslStream has no session timeout setting (see SessionCacheSize).
    public SslContextBuilder SessionTimeout(long sessionTimeout)
    {
        this.sessionTimeout = sessionTimeout;
        return this;
    }

    /**
     * Sets the client authentication mode.
     */
    // CLR: ClientAuth is an enum (a value type), so checkNotNull has no counterpart.
    public SslContextBuilder ClientAuth(ClientAuth clientAuth)
    {
        this.clientAuth = clientAuth;
        return this;
    }

    /**
     * The TLS protocol versions to enable.
     * @param protocols The protocols to enable, or {@code null} to enable the default protocols.
     * @see SSLEngine#setEnabledCipherSuites(String[])
     */
    public SslContextBuilder Protocols(params string[] protocols)
    {
        this.protocols = protocols == null ? null : (string[]) protocols.Clone();
        return this;
    }

    /**
     * The TLS protocol versions to enable.
     * @param protocols The protocols to enable, or {@code null} to enable the default protocols.
     * @see SSLEngine#setEnabledCipherSuites(String[])
     */
    public SslContextBuilder Protocols(IEnumerable<string> protocols)
    {
        return Protocols(ToArray(protocols));
    }

    /**
     * {@code true} if the first write request shouldn't be encrypted.
     */
    public SslContextBuilder StartTls(bool startTls)
    {
        this.startTls = startTls;
        return this;
    }

    /**
     * Enables OCSP stapling. Please note that not all {@link SslProvider} implementations support OCSP
     * stapling and an exception will be thrown upon {@link #build()}.
     *
     * @see OpenSsl#isOcspSupported()
     */
    [UnstableApi]
    public SslContextBuilder EnableOcsp(bool enableOcsp)
    {
        this.enableOcsp = enableOcsp;
        return this;
    }

    /**
     * Specify a non-default source of randomness for the {@link JdkSslContext}
     * <p>
     * In general, the best practice is to leave this unspecified, or to assign a new random source using the
     * default {@code new SecureRandom()} constructor.
     * Only assign this something when you have a good reason to.
     *
     * @param secureRandom the source of randomness for {@link JdkSslContext}
     *
     */
    // CLR: kept for the API; no SslStream option takes a random source (SslClientAuthenticationOptions and
    // SslServerAuthenticationOptions properties), so JdkSslContext does not use it.
    public SslContextBuilder SecureRandom(RandomNumberGenerator secureRandom)
    {
        this.secureRandom = secureRandom;
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
    // CLR: SslStream checks the server certificate against SslClientAuthenticationOptions.TargetHost ("The name is used
    // for server certificate validation") and reports a mismatch as SslPolicyErrors.RemoteCertificateNameMismatch; a
    // non-null algorithm keeps that check (one host name check for "HTTPS" and "LDAPS" alike), null clears the flag (see
    // JdkSslContext).
    public SslContextBuilder EndpointIdentificationAlgorithm(string algorithm)
    {
        endpointIdentificationAlgorithm = algorithm;
        return this;
    }

    /**
     * Add the given server name indication to this client context. This will cause the client to include a
     * Server Name Indication extension with its {@code ClientHello} message, as per
     * <a href="https://datatracker.ietf.org/doc/html/rfc6066#section-3">RFC 6066 section 3</a>.
     * <p>
     * Note that only one name per name type can be included in the message.
     * Currently, only the {@link SNIHostName} type is supported.
     * @param serverName The server name to include in the SNI extension.
     */
    // CLR: SNIServerName/SNIHostName have no BCL type; the host name is a string (the only supported type, so the
    // instanceof check has no counterpart) and the client sends it as TargetHost (see the class comment), which is also
    // the name SslStream checks the server certificate against. UnsupportedOperationException is NotSupportedException.
    public SslContextBuilder ServerName(string serverName)
    {
        if (forServer)
        {
            throw new NotSupportedException("Cannot add Server Name Indication extension, " +
                    "because this is a server context builder.");
        }
        ObjectUtil.CheckNotNull(serverName, "serverName");

        serverNames.Add(serverName);
        return this;
    }

    /**
     * Create new {@code SslContext} instance with configured settings.
     * <p>If {@link #sslProvider(SslProvider)} is set to {@link SslProvider#OPENSSL_REFCNT} then the caller is
     * responsible for releasing this object, or else native memory may leak.
     */
    // CLR: SSLException is AuthenticationException (thrown by the internal factories).
    public SslContext Build()
    {
        if (forServer)
        {
            return SslContext.NewServerContextInternal(provider, trustCertCollection,
                trustManagerFactory, trustManager, keyCertChain, key, keyPassword, keyManagerFactory, serverKeyManager,
                ciphers, cipherFilter, apn, sessionCacheSize, sessionTimeout, clientAuth, protocols, startTls,
                enableOcsp, secureRandom, keyStoreType, options.ToArray());
        }
        else
        {
            return SslContext.NewClientContextInternal(provider, trustCertCollection,
                trustManagerFactory, trustManager, keyCertChain, key, keyPassword, keyManagerFactory, clientKeyManager,
                ciphers, cipherFilter, apn, protocols, sessionCacheSize,
                sessionTimeout, startTls, enableOcsp, secureRandom, keyStoreType, endpointIdentificationAlgorithm,
                    serverNames, options.ToArray());
        }
    }

    // CLR: the prototype array of toArray(Iterable, T[]) only gives the element type, which the generic method knows.
    private static T[] ToArray<T>(IEnumerable<T> iterable)
    {
        if (iterable == null)
        {
            return null;
        }

        List<T> list = new List<T>();

        foreach (T element in iterable)
        {
            list.Add(element);
        }

        return list.ToArray();
    }
}
