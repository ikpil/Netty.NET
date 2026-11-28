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
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Handler.Ssl;

/**
 * A secure socket protocol implementation which acts as a factory for {@link SSLEngine} and {@link SslHandler}.
 * Internally, it is implemented via JDK's {@link SSLContext} or OpenSSL's {@code SSL_CTX}.
 *
 * <h3>Making your server support SSL/TLS</h3>
 * <pre>
 * // In your {@link ChannelInitializer}:
 * {@link ChannelPipeline} p = channel.pipeline();
 * {@link SslContext} sslCtx = {@link SslContextBuilder#forServer(File, File) SslContextBuilder.forServer(...)}.build();
 * p.addLast("ssl", {@link #newHandler(ByteBufAllocator) sslCtx.newHandler(channel.alloc())});
 * ...
 * </pre>
 *
 * <h3>Making your client support SSL/TLS</h3>
 * <pre>
 * // In your {@link ChannelInitializer}:
 * {@link ChannelPipeline} p = channel.pipeline();
 * {@link SslContext} sslCtx = {@link SslContextBuilder#forClient() SslContextBuilder.forClient()}.build();
 * p.addLast("ssl", {@link #newHandler(ByteBufAllocator, String, int) sslCtx.newHandler(channel.alloc(), host, port)});
 * ...
 * </pre>
 */
// CLR: the TLS engine is SslStream (owner decision 2026-11-24, docs/handler-ssl-design.md). The SSLEngine a context
// creates per connection is the SslClientAuthenticationOptions/SslServerAuthenticationOptions handed to the SslHandler
// constructors, so newEngine(alloc[, peerHost, peerPort]) and sessionContext() (SSLSessionContext) are not ported (owner
// decision 2: no SSLEngine facade), and the engine factory moves into the protected newHandler(alloc, startTls) and
// newHandler(alloc, peerHost, peerPort, startTls), which are abstract here (their source bodies only wrapped newEngine).
// sessionCacheSize()/sessionTimeout() only read the SSLSessionContext; SslStream has no session cache size or timeout
// setting (docs/handler-ssl-design.md, Known differences) and they are not ported. The Executor overloads of newHandler
// pass the executor for SSLEngine.getDelegatedTask(); SslStream runs certificate callbacks and crypto inline and has no
// delegated tasks (members of SslStream,
// https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream?view=net-10.0), so they are not ported.
// The @Deprecated newServerContext/newClientContext overloads and nextProtocols(), whose @deprecated text names their
// replacements, are not ported (owner decision 2026-11-16); the package-private newServerContext(..., keyStore),
// toApplicationProtocolConfig, toPrivateKeyInternal and toX509CertificatesInternal only serve them, the @Deprecated
// JdkSslClientContext/JdkSslServerContext or the library-not-ported OpenSSL contexts, and are not ported either. The
// JCA types map to the BCL types the SslStream options take (owner decision 3, as pkitesting X509Bundle maps them,
// src/Netty.NET.PkiTesting/X509Bundle.cs): X509Certificate is X509Certificate2, PrivateKey is AsymmetricAlgorithm,
// TrustManagerFactory is X509ChainPolicy (TrustManager a RemoteCertificateValidationCallback), KeyManagerFactory is
// SslStreamCertificateContext (KeyManager a ServerCertificateSelectionCallback or LocalCertificateSelectionCallback), the
// KeyStore is its PKCS #12 encoding, File is FileInfo and InputStream System.IO.Stream; CertificateException, KeyException
// and the other GeneralSecurityExceptions are CryptographicException; SSLException is AuthenticationException.
// java.security.Provider/SecureRandom parameters of the internal factories follow the builder. ResumptionController is
// not ported (see the builder), so there is no resumption controller field. CertificateFactory (X509_CERT_FACTORY, only
// used by the OpenSSL code) is the static X509CertificateLoader and ALIAS has no counterpart (a PKCS #12 export of an
// X509Certificate2Collection carries no alias). OpenSsl (netty-tcnative) is library-not-ported, so OpenSsl.isAvailable()
// is false and OPENSSL/OPENSSL_REFCNT cannot be built. java.lang.Error for an unexpected provider is
// UnreachableException, as codec-base LengthFieldPrepender maps it.
public abstract class SslContext
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(SslContext));

    private const string DEFAULT_ENDPOINT_VERIFICATION_ALGORITHM_PROPERTY =
            "io.netty.handler.ssl.defaultEndpointVerificationAlgorithm";

    /**
     * Endpoint verification is enabled by default from Netty 4.2 onward, but it wasn't in Netty 4.1 and earlier.
     * The {@value #DEFAULT_ENDPOINT_VERIFICATION_ALGORITHM_PROPERTY} can be set to one of the following
     * values to control this behavior:
     * <ul>
     *     <li>{@code "HTTPS"} — verify subject by DNS hostnames; this is the Netty 4.2 default.</li>
     *     <li>{@code "LDAP"} — verify subject by LDAP identity.</li>
     *     <li>{@code "NONE"} — don't enable endpoint verification by default; this is the Netty 4.1 behavior.</li>
     * </ul>
     */
    // CLR: protected internal, as Java's protected also opens it to the package (SslContextBuilder reads it).
    protected internal static readonly string defaultEndpointVerificationAlgorithm;

    // CLR: an explicit static constructor, so the property is read when the class is first used, as the Java class
    // initializer reads it. equalsIgnoreCase is an OrdinalIgnoreCase comparison.
    static SslContext()
    {
        string defaultEndpointVerification = SystemPropertyUtil.Get(DEFAULT_ENDPOINT_VERIFICATION_ALGORITHM_PROPERTY);
        if ("LDAP".Equals(defaultEndpointVerification, StringComparison.OrdinalIgnoreCase))
        {
            defaultEndpointVerificationAlgorithm = "LDAP";
        }
        else if ("NONE".Equals(defaultEndpointVerification, StringComparison.OrdinalIgnoreCase))
        {
            logger.Info("Default SSL endpoint verification has been disabled:  -D{}=\"{}\"",
                    DEFAULT_ENDPOINT_VERIFICATION_ALGORITHM_PROPERTY, defaultEndpointVerification);
            defaultEndpointVerificationAlgorithm = null;
        }
        else
        {
            if (defaultEndpointVerification != null &&
                !"HTTPS".Equals(defaultEndpointVerification, StringComparison.OrdinalIgnoreCase))
            {
                logger.Warn("Unknown default SSL endpoint verification algorithm: -D{}=\"{}\", " +
                                "will use \"HTTPS\" instead.",
                        DEFAULT_ENDPOINT_VERIFICATION_ALGORITHM_PROPERTY, defaultEndpointVerification);
            }

            defaultEndpointVerificationAlgorithm = "HTTPS";
        }
    }

    private readonly bool startTls;
    private readonly IAttributeMap attributes = new DefaultAttributeMap();

    /**
     * Returns the default server-side implementation provider currently in use.
     *
     * @return {@link SslProvider#OPENSSL} if OpenSSL is available. {@link SslProvider#JDK} otherwise.
     */
    public static SslProvider DefaultServerProvider()
    {
        return DefaultProvider();
    }

    /**
     * Returns the default client-side implementation provider currently in use.
     *
     * @return {@link SslProvider#OPENSSL} if OpenSSL is available. {@link SslProvider#JDK} otherwise.
     */
    public static SslProvider DefaultClientProvider()
    {
        return DefaultProvider();
    }

    // CLR: OpenSsl.isAvailable() is always false (OpenSsl is library-not-ported, owner decision 2026-11-24), so the
    // default provider is JDK, the SslStream-based JdkSslContext.
    private static SslProvider DefaultProvider()
    {
        return SslProvider.JDK;
    }

    // CLR: the JCA java.security.Provider (sslContextProvider) is not taken (no counterpart), the OpenSslCredential list
    // is library-not-ported, and the trust and key material take their BCL types (see the class comment), with the
    // TrustManager/KeyManager callbacks next to the factories. The JDK case builds what the @Deprecated
    // JdkSslServerContext constructor built (JdkSslContext.NewJdkSslServerContext). OPENSSL/OPENSSL_REFCNT, whose
    // OpenSslServerContext/ReferenceCountedOpenSslServerContext need netty-tcnative, throw NotSupportedException.
    internal static SslContext NewServerContextInternal(
            SslProvider provider,
            X509Certificate2[] trustCertCollection, X509ChainPolicy trustManagerFactory,
            RemoteCertificateValidationCallback trustManager,
            X509Certificate2[] keyCertChain, AsymmetricAlgorithm key, string keyPassword,
            SslStreamCertificateContext keyManagerFactory, ServerCertificateSelectionCallback keyManager,
            IEnumerable<string> ciphers, ICipherSuiteFilter cipherFilter, ApplicationProtocolConfig apn,
            long sessionCacheSize, long sessionTimeout, ClientAuth clientAuth, string[] protocols, bool startTls,
            bool enableOcsp, RandomNumberGenerator secureRandom, string keyStoreType,
            KeyValuePair<SslContextOption, object>[] ctxOptions)
    {

        if (provider == null)
        {
            provider = DefaultServerProvider();
        }

        if (provider == SslProvider.JDK)
        {
            if (enableOcsp)
            {
                throw new ArgumentException("OCSP is not supported with this SslProvider: " + provider);
            }

            return JdkSslContext.NewJdkSslServerContext(
                    trustCertCollection, trustManagerFactory, trustManager, keyCertChain, key, keyPassword,
                    keyManagerFactory, keyManager, ciphers, cipherFilter, apn, sessionCacheSize, sessionTimeout,
                    clientAuth, protocols, startTls, secureRandom, keyStoreType);
        }

        if (provider == SslProvider.OPENSSL || provider == SslProvider.OPENSSL_REFCNT)
        {
            throw new NotSupportedException("OpenSSL is not available (netty-tcnative is not ported): " + provider);
        }

        throw new UnreachableException("Unexpected provider: " + provider);
    }

    // CLR: see NewServerContextInternal.
    internal static SslContext NewClientContextInternal(
            SslProvider provider,
            X509Certificate2[] trustCert, X509ChainPolicy trustManagerFactory,
            RemoteCertificateValidationCallback trustManager,
            X509Certificate2[] keyCertChain, AsymmetricAlgorithm key, string keyPassword,
            SslStreamCertificateContext keyManagerFactory, LocalCertificateSelectionCallback keyManager,
            IEnumerable<string> ciphers, ICipherSuiteFilter cipherFilter, ApplicationProtocolConfig apn,
            string[] protocols, long sessionCacheSize, long sessionTimeout, bool enableOcsp,
            RandomNumberGenerator secureRandom, string keyStoreType, string endpointIdentificationAlgorithm,
            IList<string> serverNames,
            KeyValuePair<SslContextOption, object>[] options)
    {
        return NewClientContextInternal(provider, trustCert, trustManagerFactory, trustManager, keyCertChain, key,
                keyPassword, keyManagerFactory, keyManager, ciphers, cipherFilter, apn, protocols, sessionCacheSize,
                sessionTimeout, false, enableOcsp, secureRandom, keyStoreType, endpointIdentificationAlgorithm,
                serverNames, options);
    }

    // CLR: see NewServerContextInternal; the JDK case builds what the @Deprecated JdkSslClientContext constructor built
    // (JdkSslContext.NewJdkSslClientContext), which, as in the source, does not pass startTls on.
    internal static SslContext NewClientContextInternal(
            SslProvider provider,
            X509Certificate2[] trustCert, X509ChainPolicy trustManagerFactory,
            RemoteCertificateValidationCallback trustManager,
            X509Certificate2[] keyCertChain, AsymmetricAlgorithm key, string keyPassword,
            SslStreamCertificateContext keyManagerFactory, LocalCertificateSelectionCallback keyManager,
            IEnumerable<string> ciphers, ICipherSuiteFilter cipherFilter, ApplicationProtocolConfig apn,
            string[] protocols, long sessionCacheSize, long sessionTimeout, bool startTls, bool enableOcsp,
            RandomNumberGenerator secureRandom, string keyStoreType, string endpointIdentificationAlgorithm,
            IList<string> serverNames,
            KeyValuePair<SslContextOption, object>[] options)
    {
        if (provider == null)
        {
            provider = DefaultClientProvider();
        }

        if (provider == SslProvider.JDK)
        {
            if (enableOcsp)
            {
                throw new ArgumentException("OCSP is not supported with this SslProvider: " + provider);
            }

            return JdkSslContext.NewJdkSslClientContext(
                    trustCert, trustManagerFactory, trustManager, keyCertChain, key, keyPassword,
                    keyManagerFactory, keyManager, ciphers, cipherFilter, apn, protocols, sessionCacheSize,
                    sessionTimeout, secureRandom, keyStoreType, endpointIdentificationAlgorithm,
                    serverNames);
        }

        if (provider == SslProvider.OPENSSL || provider == SslProvider.OPENSSL_REFCNT)
        {
            throw new NotSupportedException("OpenSSL is not available (netty-tcnative is not ported): " + provider);
        }

        throw new UnreachableException("Unexpected provider: " + provider);
    }

    /**
     * Creates a new instance (startTls set to {@code false}).
     */
    protected SslContext() : this(false)
    {
    }

    /**
     * Creates a new instance.
     */
    // CLR: the source delegates to the package-private SslContext(startTls, ResumptionController); the resumption
    // controller is not ported, so the field is set here.
    protected SslContext(bool startTls)
    {
        this.startTls = startTls;
    }

    /**
     * Returns the {@link AttributeMap} that belongs to this {@link SslContext} .
     */
    public IAttributeMap Attributes()
    {
        return attributes;
    }

    /**
     * Returns {@code true} if and only if this context is for server-side.
     */
    public bool IsServer()
    {
        return !IsClient();
    }

    /**
     * Returns the {@code true} if and only if this context is for client-side.
     */
    public abstract bool IsClient();

    /**
     * Returns the list of enabled cipher suites, in the order of preference.
     */
    public abstract IList<string> CipherSuites();

    /**
     * Returns the object responsible for negotiating application layer protocols for the TLS NPN/ALPN extensions.
     */
    public abstract IApplicationProtocolNegotiator ApplicationProtocolNegotiator();

    /**
     * Create a new SslHandler.
     * @see #newHandler(ByteBufAllocator, Executor)
     */
    public SslHandler NewHandler(IByteBufAllocator alloc)
    {
        return NewHandler(alloc, startTls);
    }

    /**
     * Create a new SslHandler.
     * @see #newHandler(ByteBufAllocator)
     */
    // CLR: abstract: the source body is new SslHandler(newEngine(alloc), startTls, ImmediateExecutor.INSTANCE,
    // resumptionController), and newEngine, the abstract engine factory, is not ported (see the class comment), so the
    // subclass that knows how to configure the engine (JdkSslContext) builds the handler here. protected internal, as
    // Java's protected admits the package (DelegatingSslContext calls it on another context).
    protected internal abstract SslHandler NewHandler(IByteBufAllocator alloc, bool startTls);

    /**
     * Creates a new {@link SslHandler}
     *
     * @see #newHandler(ByteBufAllocator, String, int, Executor)
     */
    public SslHandler NewHandler(IByteBufAllocator alloc, string peerHost, int peerPort)
    {
        return NewHandler(alloc, peerHost, peerPort, startTls);
    }

    /**
     * Create a new SslHandler.
     * @see #newHandler(ByteBufAllocator, String, int, boolean, Executor)
     */
    // CLR: abstract for the reason given at NewHandler(alloc, startTls); the source body wraps newEngine(alloc, peerHost,
    // peerPort).
    protected internal abstract SslHandler NewHandler(IByteBufAllocator alloc, string peerHost, int peerPort,
                                                      bool startTls);

    /**
     * Generates a key specification for an (encrypted) private key.
     *
     * @param password characters, if {@code null} an unencrypted key is assumed
     * @param key bytes of the DER encoded private key
     *
     * @return a key specification
     *
     * @throws IOException if parsing {@code key} fails
     * @throws NoSuchAlgorithmException if the algorithm used to encrypt {@code key} is unknown
     * @throws NoSuchPaddingException if the padding scheme specified in the decryption algorithm is unknown
     * @throws InvalidKeySpecException if the decryption key based on {@code password} cannot be generated
     * @throws InvalidKeyException if the decryption key based on {@code password} cannot be used to decrypt
     *                             {@code key}
     * @throws InvalidAlgorithmParameterException if decryption algorithm parameters are somehow faulty
     */
    // CLR: @Deprecated without @deprecated text (no replacement named), so it is ported with [Obsolete] (owner decision
    // 2026-11-16). PKCS8EncodedKeySpec "represents the ASN.1 encoding of a private key" (PKCS #8 PrivateKeyInfo), which is
    // the returned byte[]. The BCL decrypts a PKCS #8 EncryptedPrivateKeyInfo only while importing it into a key of its
    // algorithm (AsymmetricAlgorithm.ImportEncryptedPkcs8PrivateKey, "imports the public/private keypair from a PKCS#8
    // EncryptedPrivateKeyInfo structure after decrypting with a char-based password",
    // https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.asymmetricalgorithm.importencryptedpkcs8privatekey?view=net-10.0),
    // so EncryptedPrivateKeyInfo, getPBEAlgorithm (its comments are recorded), SecretKeyFactory/PBEKeySpec and Cipher are
    // replaced by importing into RSA, DSA and then ECDsa and exporting the decrypted PKCS #8 (ExportPkcs8PrivateKey);
    // the last import failure (CryptographicException, the BCL report of every GeneralSecurityException above and of a
    // malformed structure) is rethrown. Measured on this host: the PBES2 (AES/3DES), PKCS #12 PBE-SHA1-3DES and
    // PBES1-MD5-DES keys of the handler test resources all decrypt.
    [Obsolete]
    protected internal static byte[] GenerateKeySpec(char[] password, byte[] key)
    {

        if (password == null)
        {
            return key;
        }

        CryptographicException failure = null;

        foreach (Func<AsymmetricAlgorithm> factory in KEY_FACTORIES)
        {
            using (AsymmetricAlgorithm candidate = factory())
            {
                try
                {
                    candidate.ImportEncryptedPkcs8PrivateKey(password, key, out _);

                    return candidate.ExportPkcs8PrivateKey();
                }
                catch (CryptographicException e)
                {
                    failure = e;
                }
            }
        }

        throw failure;
    }

    // CLR: the KeyFactory algorithms of getPrivateKeyFromByteBuffer and GenerateKeySpec, in the source's order (RSA, DSA,
    // EC); an EC private key is an ECDsa, the BCL type that signs with it (as an SslStream certificate key does).
    private static readonly Func<AsymmetricAlgorithm>[] KEY_FACTORIES =
    {
        () => RSA.Create(), () => DSA.Create(), () => ECDsa.Create(),
    };

    /**
     * Generates a new {@link KeyStore}.
     *
     * @param certChain an X.509 certificate chain
     * @param key a PKCS#8 private key
     * @param keyPasswordChars the password of the {@code keyFile}.
     *                    {@code null} if it's not password-protected.
     * @param keyStoreType The KeyStore Type you want to use
     * @return generated {@link KeyStore}.
     */
    // CLR: the KeyStore is the PKCS #12 encoding of an X509Certificate2Collection, as pkitesting X509Bundle.ToKeyStore
    // builds it (src/Netty.NET.PkiTesting/X509Bundle.cs): setKeyEntry(ALIAS, key, password, certChain) is the leaf
    // certificate combined with the key (CopyWithPrivateKey, "Combines a private key with a certificate containing the
    // associated public key into a new instance that can access the private key",
    // https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificate2.copywithprivatekey?view=net-10.0,
    // and the RSA/DSA/ECDsa certificate extensions; the copy starts from the leaf's DER so a leaf that already has a key
    // is accepted) followed by the rest of the chain, protected with the password. The keystore type only selects the
    // JCA KeyStore implementation of the in-memory store (KeyStore.getDefaultType() is the keystore.type property,
    // https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/security/KeyStore.html; SunJSSE's is PKCS12,
    // JSSE Reference Guide table 8-1); the BCL carries the entry as PKCS #12 whatever the type, so keyStoreType is not
    // used. KeyStoreException is CryptographicException.
    protected internal static byte[] BuildKeyStore(X509Certificate2[] certChain, AsymmetricAlgorithm key,
                                  char[] keyPasswordChars, string keyStoreType)
    {
        X509Certificate2Collection ks = new X509Certificate2Collection();
        ks.Add(CopyWithPrivateKey(X509CertificateLoader.LoadCertificate(certChain[0].RawData), key));

        for (int i = 1; i < certChain.Length; i++)
        {
            ks.Add(certChain[i]);
        }

        return ks.Export(X509ContentType.Pkcs12, keyPasswordChars == null ? null : new string(keyPasswordChars));
    }

    // CLR: see BuildKeyStore.
    private static X509Certificate2 CopyWithPrivateKey(X509Certificate2 certificate, AsymmetricAlgorithm key)
    {
        switch (key)
        {
            case RSA rsa:
                return certificate.CopyWithPrivateKey(rsa);
            case DSA dsa:
                return certificate.CopyWithPrivateKey(dsa);
            case ECDsa ecdsa:
                return certificate.CopyWithPrivateKey(ecdsa);
            case ECDiffieHellman ecdh:
                return certificate.CopyWithPrivateKey(ecdh);
            default:
                throw new CryptographicException("Unsupported private key: " +
                        (key == null ? "null" : key.GetType().FullName));
        }
    }

    protected internal static AsymmetricAlgorithm ToPrivateKey(FileInfo keyFile, string keyPassword)
    {
        return ToPrivateKey(keyFile, keyPassword, true);
    }

    // CLR: Bouncy Castle (BouncyCastleUtil, BouncyCastlePemReader) is library-not-ported; the BCL has its own PEM key
    // readers, so the first attempt reads the PEM text with them (ReadPemPrivateKey) where the source asks Bouncy Castle,
    // and tryBouncyCastle selects that attempt.
    internal static AsymmetricAlgorithm ToPrivateKey(FileInfo keyFile, string keyPassword, bool tryBouncyCastle)
    {
        if (keyFile == null)
        {
            return null;
        }

        // try BC first, if this fail fallback to original key extraction process
        if (tryBouncyCastle)
        {
            AsymmetricAlgorithm pk = ReadPemPrivateKey(keyFile, keyPassword);
            if (pk != null)
            {
                return pk;
            }
        }

        return GetPrivateKeyFromByteBuffer(PemReader.ReadPrivateKey(keyFile), keyPassword);
    }

    // CLR: see ToPrivateKey(FileInfo, string, bool). markSupported/mark/reset are CanSeek and Position: a stream that
    // cannot seek is copied into a MemoryStream, the BufferedInputStream counterpart, which can return to any position.
    protected internal static AsymmetricAlgorithm ToPrivateKey(System.IO.Stream keyInputStream, string keyPassword)
    {
        if (keyInputStream == null)
        {
            return null;
        }

        // try BC first, if this fail fallback to original key extraction process
        if (!keyInputStream.CanSeek)
        {
            // We need an input stream that supports resetting, in case BouncyCastle fails to read.
            MemoryStream buffered = new MemoryStream();
            keyInputStream.CopyTo(buffered);
            buffered.Position = 0;
            keyInputStream = buffered;
        }

        long mark = keyInputStream.Position; // Be able to reset up to 1 MiB of data.
        AsymmetricAlgorithm pk = ReadPemPrivateKey(keyInputStream, keyPassword);
        if (pk != null)
        {
            return pk;
        }

        // BouncyCastle could not read the key. Reset the input stream in case the input position changed.
        keyInputStream.Position = mark;

        return GetPrivateKeyFromByteBuffer(PemReader.ReadPrivateKey(keyInputStream), keyPassword);
    }

    // CLR-only helper replacing BouncyCastlePemReader.getPrivateKey(File, String), which returns null when it cannot read
    // the key; see ReadPemPrivateKey(string, string).
    private static AsymmetricAlgorithm ReadPemPrivateKey(FileInfo keyFile, string keyPassword)
    {
        string pem;

        try
        {
            pem = File.ReadAllText(keyFile.FullName, Encoding.ASCII);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return null;
        }

        return ReadPemPrivateKey(pem, keyPassword);
    }

    // CLR-only helper replacing BouncyCastlePemReader.getPrivateKey(InputStream, String); see
    // ReadPemPrivateKey(string, string).
    private static AsymmetricAlgorithm ReadPemPrivateKey(System.IO.Stream keyInputStream, string keyPassword)
    {
        string pem;

        try
        {
            using (StreamReader reader = new StreamReader(keyInputStream, Encoding.ASCII, false, 4096, true))
            {
                pem = reader.ReadToEnd();
            }
        }
        catch (IOException)
        {
            return null;
        }

        return ReadPemPrivateKey(pem, keyPassword);
    }

    // CLR-only helper: the BCL PEM readers of RSA, DSA and ECDsa, ImportFromPem (PKCS #8 "PRIVATE KEY", PKCS #1 "RSA
    // PRIVATE KEY", SEC 1 "EC PRIVATE KEY") and ImportFromEncryptedPem ("ENCRYPTED PRIVATE KEY" with the password), read
    // the PEM text in the source's KeyFactory order and the first that succeeds is the key; null (fall back to the PKCS #8
    // path) when the text has no private key label or no reader accepts it, as BouncyCastlePemReader returns null. Measured
    // on this host: unencrypted PKCS #1 RSA keys are read, the legacy OpenSSL "Proc-Type: 4,ENCRYPTED" PKCS #1 keys and
    // PKCS #1 DSA keys are not (Bouncy Castle reads them).
    private static AsymmetricAlgorithm ReadPemPrivateKey(string pem, string keyPassword)
    {
        if (pem.IndexOf("PRIVATE KEY-----", StringComparison.Ordinal) < 0)
        {
            return null;
        }

        foreach (Func<AsymmetricAlgorithm> factory in KEY_FACTORIES)
        {
            AsymmetricAlgorithm candidate = factory();

            try
            {
                if (keyPassword == null)
                {
                    candidate.ImportFromPem(pem);
                }
                else
                {
                    candidate.ImportFromEncryptedPem(pem, keyPassword);
                }

                return candidate;
            }
            catch (Exception e) when (e is CryptographicException || e is ArgumentException)
            {
                candidate.Dispose();
            }
        }

        return null;
    }

    // CLR: KeyFactory.getInstance("RSA"/"DSA"/"EC").generatePrivate(PKCS8EncodedKeySpec) is ImportPkcs8PrivateKey
    // ("imports the public/private keypair from a PKCS#8 PrivateKeyInfo structure after decryption",
    // https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.asymmetricalgorithm.importpkcs8privatekey?view=net-10.0)
    // on RSA, DSA and ECDsa; InvalidKeySpecException is its CryptographicException. The [Obsolete] GenerateKeySpec is
    // called as the source calls the @Deprecated generateKeySpec.
    private static AsymmetricAlgorithm GetPrivateKeyFromByteBuffer(ByteBuf encodedKeyBuf, string keyPassword)
    {

        byte[] encodedKey = new byte[encodedKeyBuf.ReadableBytes];
        encodedKeyBuf.ReadBytes(encodedKey).Release();

#pragma warning disable CS0612 // @Deprecated generateKeySpec is called by the source too.
        byte[] encodedKeySpec = GenerateKeySpec(
                keyPassword == null ? null : keyPassword.ToCharArray(), encodedKey);
#pragma warning restore CS0612
        AsymmetricAlgorithm rsa = RSA.Create();

        try
        {
            rsa.ImportPkcs8PrivateKey(encodedKeySpec, out _);
            return rsa;
        }
        catch (CryptographicException)
        {
            rsa.Dispose();
            AsymmetricAlgorithm dsa = DSA.Create();

            try
            {
                dsa.ImportPkcs8PrivateKey(encodedKeySpec, out _);
                return dsa;
            }
            catch (CryptographicException)
            {
                dsa.Dispose();
                AsymmetricAlgorithm ec = ECDsa.Create();

                try
                {
                    ec.ImportPkcs8PrivateKey(encodedKeySpec, out _);
                    return ec;
                }
                catch (CryptographicException e)
                {
                    ec.Dispose();
                    throw new CryptographicException("Neither RSA, DSA nor EC worked", e);
                }
            }
        }
    }

    /**
     * Build a {@link TrustManagerFactory} from a certificate chain file.
     * @param certChainFile The certificate file to build from.
     * @param trustManagerFactory The existing {@link TrustManagerFactory} that will be used if not {@code null}.
     * @return A {@link TrustManagerFactory} which contains the certificates in {@code certChainFile}
     */
    // CLR: @Deprecated without @deprecated text (no replacement named), so it is ported with [Obsolete] (owner decision
    // 2026-11-16).
    [Obsolete]
    protected internal static X509ChainPolicy BuildTrustManagerFactory(
            FileInfo certChainFile, X509ChainPolicy trustManagerFactory)
    {
        return BuildTrustManagerFactory(certChainFile, trustManagerFactory, null);
    }

    /**
     * Build a {@link TrustManagerFactory} from a certificate chain file.
     * @param certChainFile The certificate file to build from.
     * @param trustManagerFactory The existing {@link TrustManagerFactory} that will be used if not {@code null}.
     * @param keyType The KeyStore Type you want to use
     * @return A {@link TrustManagerFactory} which contains the certificates in {@code certChainFile}
     */
    protected internal static X509ChainPolicy BuildTrustManagerFactory(
            FileInfo certChainFile, X509ChainPolicy trustManagerFactory, string keyType)
    {
        X509Certificate2[] x509Certs = ToX509Certificates(certChainFile);

        return BuildTrustManagerFactory(x509Certs, trustManagerFactory, keyType);
    }

    protected internal static X509Certificate2[] ToX509Certificates(FileInfo file)
    {
        if (file == null)
        {
            return null;
        }

        return GetCertificatesFromBuffers(PemReader.ReadCertificates(file));
    }

    protected internal static X509Certificate2[] ToX509Certificates(System.IO.Stream @in)
    {
        if (@in == null)
        {
            return null;
        }

        return GetCertificatesFromBuffers(PemReader.ReadCertificates(@in));
    }

    // CLR: CertificateFactory.generateCertificate is X509CertificateLoader.LoadCertificate, which throws
    // CryptographicException (CertificateException) for data that is not a certificate
    // (src/Netty.NET.Handler/Ssl/Util/LazyX509Certificate.cs uses it the same way); the DER bytes are read through the
    // ByteBufInputStream as in the source. RuntimeException is Exception with Throwable.toString() as its message
// (Common's ThrowableUtil.ThrowableToString).
    private static X509Certificate2[] GetCertificatesFromBuffers(ByteBuf[] certs)
    {
        X509Certificate2[] x509Certs = new X509Certificate2[certs.Length];

        try
        {
            for (int i = 0; i < certs.Length; i++)
            {
                try
                {
                    using (System.IO.Stream @is = new ByteBufInputStream(certs[i], false))
                    {
                        MemoryStream der = new MemoryStream();
                        @is.CopyTo(der);
                        x509Certs[i] = X509CertificateLoader.LoadCertificate(der.ToArray());
                    }
                }
                catch (IOException e)
                {
                    // This is not expected to happen, but re-throw in case it does.
                    throw new Exception(ThrowableUtil.ThrowableToString(e), e);
                }
            }
        }
        finally
        {
            foreach (ByteBuf buf in certs)
            {
                buf.Release();
            }
        }

        return x509Certs;
    }

    // CLR: the TrustManagerFactory is an X509ChainPolicy whose CustomTrustStore, which "Represents a collection of
    // certificates replacing the default certificate trust", holds the certificates, with TrustMode CustomRootTrust and
    // RevocationMode NoCheck (TrustManagerFactory.init(KeyStore) disables revocation checking), as pkitesting
    // X509Bundle.ToTrustManagerFactory builds it (src/Netty.NET.PkiTesting/X509Bundle.cs;
    // https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509chainpolicy?view=net-10.0).
    // An existing factory is re-initialized as init(ks) re-initializes it: its trust store is replaced. The trusted
    // certificate entries are the CustomTrustStore (the aliases have no counterpart), and the key store type, which only
    // selects the JCA KeyStore implementation, is not used (see BuildKeyStore).
    protected internal static X509ChainPolicy BuildTrustManagerFactory(
            X509Certificate2[] certCollection, X509ChainPolicy trustManagerFactory, string keyStoreType)
    {
        X509Certificate2Collection ks = new X509Certificate2Collection();

        foreach (X509Certificate2 cert in certCollection)
        {
            ks.Add(cert);
        }

        // Set up trust manager factory to use our key store.
        if (trustManagerFactory == null)
        {
            trustManagerFactory = new X509ChainPolicy();
        }

        trustManagerFactory.TrustMode = X509ChainTrustMode.CustomRootTrust;
        trustManagerFactory.CustomTrustStore.Clear();
        trustManagerFactory.CustomTrustStore.AddRange(ks);
        trustManagerFactory.RevocationMode = X509RevocationMode.NoCheck;

        return trustManagerFactory;
    }

    // CLR: the KeyManagerFactory is an SslStreamCertificateContext (owner decision 3, as pkitesting
    // X509Bundle.ToKeyManagerFactory builds it). keyAlgorithm names the JCA KeyManagerFactory algorithm and kmf only
    // selects the algorithm/provider of the factory that init(KeyStore, char[]) fills; SslStreamCertificateContext has
    // neither and is created from the key store each time, so both are not used.
    protected internal static SslStreamCertificateContext BuildKeyManagerFactory(X509Certificate2[] certChainFile,
                                                    string keyAlgorithm, AsymmetricAlgorithm key,
                                                    string keyPassword, SslStreamCertificateContext kmf,
                                                    string keyStore)
    {
        char[] keyPasswordChars = KeyStorePassword(keyPassword);
        byte[] ks = BuildKeyStore(certChainFile, key, keyPasswordChars, keyStore);
        return BuildKeyManagerFactory(ks, keyAlgorithm, keyPasswordChars, kmf);
    }

    // CLR: kmf.init(ks, password) loads the key entry back from the PKCS #12 key store (X509CertificateLoader.
    // LoadPkcs12Collection) and creates the context with the leaf certificate that carries the key and the other
    // certificates as the chain (SslStreamCertificateContext.Create(target, additionalCertificates, offline: true):
    // "target: The server certificate", "Supplementary certificates used to build the certificate chain", and offline
    // means "only available X509Certificate stores should be searched for missing certificates",
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstreamcertificatecontext.create?view=net-10.0).
    // The round trip gives the persisted key an SslStream server certificate needs (docs/handler-ssl-design.md; an
    // ephemeral CopyWithPrivateKey key fails, docs/pkitesting-porting.md). A key store without a key entry is the
    // UnrecoverableKeyException, a CryptographicException.
    internal static SslStreamCertificateContext BuildKeyManagerFactory(byte[] ks,
                                                    string keyAlgorithm,
                                                    char[] keyPasswordChars, SslStreamCertificateContext kmf)
    {
        // Set up key manager factory to use our key store
        X509Certificate2Collection entries =
                X509CertificateLoader.LoadPkcs12Collection(ks, new string(keyPasswordChars));
        X509Certificate2 target = null;
        X509Certificate2Collection additionalCertificates = new X509Certificate2Collection();

        foreach (X509Certificate2 cert in entries)
        {
            if (target == null && cert.HasPrivateKey)
            {
                target = cert;
            }
            else
            {
                additionalCertificates.Add(cert);
            }
        }

        if (target == null)
        {
            throw new CryptographicException("The key store has no private key entry.");
        }

        SslStreamCertificateContext context = SslStreamCertificateContext.Create(target, additionalCertificates, true);
        // CLR: entries of one PKCS #12 load that carry the same private key share one key container, and finalizing any of
        // them deletes it. A key store can hold the key entry's certificate twice: the handler test resource
        // rsaValidations-server-keystore.p12 does (two "CN=west.int" entries, both with the key), and so does pkitesting's
        // key store of a self-signed bundle. When the entry that is not the target was finalized, the SslStream server
        // failed with SEC_E_UNKNOWN_CREDENTIALS (0x8009030D) in AcquireCredentialsHandle (measured 2026-11-26: 18 of 18
        // JdkSslEngineTest.TestRSASSAPSS cases with a forced GC.Collect + WaitForPendingFinalizers after loading the key
        // stores; intermittently in full test runs without, where the handshake raced the finalizer). The loaded entries are
        // therefore kept as long as the context, as pkitesting's X509Bundle.ToKeyManagerFactory keeps them:
        // ConditionalWeakTable "automatically removes the key/value entry as soon as no other references to a key exist
        // outside the table"
        // (https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.conditionalweaktable-2?view=net-10.0).
        KEY_STORE_ENTRIES.AddOrUpdate(context, entries);
        return context;
    }

    // CLR-only: the PKCS #12 entries loaded by BuildKeyManagerFactory, alive while their SslStreamCertificateContext is.
    private static readonly ConditionalWeakTable<SslStreamCertificateContext, X509Certificate2Collection> KEY_STORE_ENTRIES =
        new ConditionalWeakTable<SslStreamCertificateContext, X509Certificate2Collection>();

    internal static char[] KeyStorePassword(string keyPassword)
    {
        return keyPassword == null ? EmptyArrays.EMPTY_CHARS : keyPassword.ToCharArray();
    }
}
