/*
 * Copyright 2024 The Netty Project
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
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.PkiTesting;

/**
 * A certificate bundle is a private key and a full certificate path, all the way to the root certificate.
 * The bundle offers ways of accessing these, and converting them into various representations.
 */
// CLR: X509Certificate is X509Certificate2 and the KeyPair is the BCL key object (object; a PublicKey when the bundle
// has no private key, see Algorithms). Certificate.equals compares encoded forms
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/security/cert/Certificate.html), while
// X509Certificate.Equals compares issuer and serial number
// (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificate.equals?view=net-10.0),
// so certificates are compared by RawData; X500Principal.equals compares canonical names
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/javax/security/auth/x500/X500Principal.html) and
// X500DistinguishedName has no equality, so names are compared by their DER encoding, which CertificateRequest copies
// from the issuer's subject. The JSSE/JCA conversions are replaced by the SslStream-ready BCL objects: KeyStore is the
// PKCS #12 encoding of an X509Certificate2Collection, TrustManagerFactory is an X509ChainPolicy (the remote certificate
// policy of SslClientAuthenticationOptions/SslServerAuthenticationOptions.CertificateChainPolicy,
// https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslclientauthenticationoptions.certificatechainpolicy?view=net-10.0),
// the X509TrustManager is an X509Chain with that policy, and KeyManagerFactory is an SslStreamCertificateContext
// (https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstreamcertificatecontext.create?view=net-10.0).
// The members whose only purpose is a JCA algorithm or store type (getTrustAnchor(), toTrustManagerFactory(String),
// toKeyStore(String, char[]), toKeyManagerFactory(String)) and toTempKeyStoreFile(char[], char[]), whose separate key
// entry password X509Certificate2Collection.Export cannot write, are not ported (owner decision 2026-11-16: JVM-only API
// families are replaced without facades); docs/pkitesting-porting.md lists them. File is FileInfo.
public sealed class X509Bundle
{
    // CLR: File.deleteOnExit has no BCL counterpart; the temporary files are deleted when AppDomain.ProcessExit is raised,
    // which "Occurs when the default application domain's parent process exits"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.appdomain.processexit?view=net-10.0).
    private static readonly List<string> DELETE_ON_EXIT = new List<string>();

    private readonly X509Certificate2[] certPath;
    private readonly X509Certificate2 root;
    private readonly object keyPair;

    static X509Bundle()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            lock (DELETE_ON_EXIT)
            {
                foreach (string path in DELETE_ON_EXIT)
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception)
                    {
                        // CLR: deleteOnExit is best effort; a file that cannot be deleted stays.
                    }
                }
            }
        };
    }

    /**
     * Construct a bundle from a given certificate path, root certificate, and {@link KeyPair}.
     * @param certPath The certificate path, starting with the leaf certificate.The path can end either with the
     * root certificate, or the intermediate certificate signed by the root certificate.
     * @param root The self-signed root certificate.
     * @param keyPair The key pair.
     */
    private X509Bundle(X509Certificate2[] certPath, X509Certificate2 root, object keyPair)
    {
        ObjectUtil.CheckNotNull(root, "root");
        ObjectUtil.CheckNotNull(keyPair, "keyPair");

        if (certPath.Length > 1 && CertificateEquals(certPath[certPath.Length - 1], root))
        {
            this.certPath = certPath[..^1];
        }
        else
        {
            this.certPath = (X509Certificate2[]) certPath.Clone();
        }

        this.root = root;
        this.keyPair = keyPair;
    }

    /**
     * Construct a bundle for a certificate authority.
     * @param root The self-signed root certificate.
     * @param keyPair The key pair.
     * @return The new bundle.
     */
    public static X509Bundle FromRootCertificateAuthority(X509Certificate2 root, object keyPair)
    {
        ObjectUtil.CheckNotNull(root, "root");
        ObjectUtil.CheckNotNull(keyPair, "keyPair");

        X509Bundle bundle = new X509Bundle(new X509Certificate2[] { root }, root, keyPair);
        if (!bundle.IsCertificateAuthority() || !bundle.IsSelfSigned())
        {
            throw new ArgumentException("Given certificate is not a root CA certificate: " +
                                        root.SubjectName.Name + ", issued by " + root.IssuerName.Name);
        }

        return bundle;
    }

    /**
     * Construct a bundle from a given certificate path, root certificate, and {@link KeyPair}.
     * @param certPath The certificate path, starting with the leaf certificate.The path can end either with the
     * root certificate, or the intermediate certificate signed by the root certificate.
     * @param root The self-signed root certificate.
     * @param keyPair The key pair.
     */
    public static X509Bundle FromCertificatePath(
            X509Certificate2[] certPath, X509Certificate2 root, object keyPair)
    {
        return new X509Bundle(certPath, root, keyPair);
    }

    /**
     * Get the leaf certificate of the bundle.
     * If this bundle is for a certificate authority, then this return the same as {@link #getRootCertificate()}.
     * @return The leaf certificate.
     */
    public X509Certificate2 GetCertificate()
    {
        return certPath[0];
    }

    /**
     * Get the PEM encoded string of the {@linkplain #getCertificate() leaf certificate}.
     * @return The certificate PEM string.
     */
    public string GetCertificatePEM()
    {
        return ToCertPem(certPath[0]);
    }

    /**
     * Get the certificate path, starting with the leaf certificate up to but excluding the root certificate.
     * @return The certificate path.
     */
    public X509Certificate2[] GetCertificatePath()
    {
        return (X509Certificate2[]) certPath.Clone();
    }

    /**
     * Get the certificate path, starting with the leaf certificate up to and including the root certificate.
     * @return The certificate path, including the root certificate.
     */
    public X509Certificate2[] GetCertificatePathWithRoot()
    {
        X509Certificate2[] path = new X509Certificate2[certPath.Length + 1];
        Array.Copy(certPath, path, certPath.Length);
        path[path.Length - 1] = root;
        return path;
    }

    /**
     * Get the certificate path as a list, starting with the leaf certificate up to but excluding the root certificate.
     * @return The certificate path list.
     */
    // CLR: Arrays.asList is a fixed-size list backed by the array; a C# array is an IList<T> with the same behaviour
    // (set allowed, add/remove rejected).
    public IList<X509Certificate2> GetCertificatePathList()
    {
        return certPath;
    }

    /**
     * Get the {@linkplain #getCertificatePath() certificate path} as a PEM encoded string.
     * @return The PEM encoded certificate path.
     */
    public string GetCertificatePathPEM()
    {
        return ToCertPem(certPath);
    }

    /**
     * Get the key pair.
     * @return The key pair.
     */
    public object GetKeyPair()
    {
        return keyPair;
    }

    /**
     * Get the root certificate that anchors the certificate path.
     * @return The root certificate.
     */
    public X509Certificate2 GetRootCertificate()
    {
        return root;
    }

    /**
     * Get the {@linkplain #getRootCertificate() root certificate} as a PEM encoded string.
     * @return The PEM encoded root certificate.
     */
    public string GetRootCertificatePEM()
    {
        return ToCertPem(root);
    }

    // CLR: X509Certificate.getEncoded() is RawData; the CertificateEncodingException wrapped in IllegalStateException
    // has no counterpart, because the BCL certificate is its encoding.
    private static string ToCertPem(params X509Certificate2[] certs)
    {
        Func<byte[], string> encoder = GetMimeEncoder();
        StringBuilder sb = new StringBuilder();

        foreach (X509Certificate2 cert in certs)
        {
            sb.Append("-----BEGIN CERTIFICATE-----\r\n");
            sb.Append(encoder(cert.RawData));
            sb.Append("\r\n-----END CERTIFICATE-----\r\n");
        }

        return sb.ToString();
    }

    /**
     * Get the private key as a PEM encoded PKCS#8 string.
     * @return The private key in PKCS#8 and PEM encoded string.
     */
    public string GetPrivateKeyPEM()
    {
        Func<byte[], string> encoder = GetMimeEncoder();
        StringBuilder sb = new StringBuilder();
        sb.Append("-----BEGIN PRIVATE KEY-----\r\n");
        object privateKey = Algorithms.GetPrivate(keyPair);
        sb.Append(encoder(Algorithms.GetEncoded(privateKey)));
        sb.Append("\r\n-----END PRIVATE KEY-----\r\n");

        return sb.ToString();
    }

    // CLR: Base64.getMimeEncoder(64, CRLF) writes lines of 64 characters separated by CRLF, with no separator after the
    // last line; Convert.ToBase64String inserts line breaks only every 76 characters, so the lines are cut here.
    private static Func<byte[], string> GetMimeEncoder()
    {
        return bytes =>
        {
            string base64 = Convert.ToBase64String(bytes);
            StringBuilder sb = new StringBuilder(base64.Length + base64.Length / 64 * 2);

            for (int i = 0; i < base64.Length; i += 64)
            {
                if (i > 0)
                {
                    sb.Append("\r\n");
                }

                sb.Append(base64, i, Math.Min(64, base64.Length - i));
            }

            return sb.ToString();
        };
    }

    /**
     * Query if this bundle is for a certificate authority root certificate.
     * @return {@code true} if the {@linkplain #getCertificate() leaf certificate} is a certificate authority,
     * otherwise {@code false}.
     */
    public bool IsCertificateAuthority()
    {
        return Algorithms.GetBasicConstraints(certPath[0]) != -1;
    }

    /**
     * Query if this bundle is for a self-signed certificate.
     * @return {@code true} if the {@linkplain #getCertificate() leaf certificate} is self-signed.
     */
    public bool IsSelfSigned()
    {
        X509Certificate2 leaf = certPath[0];
        return certPath.Length == 1 &&
               leaf.SubjectName.RawData.AsSpan().SequenceEqual(leaf.IssuerName.RawData) &&
               ArraysEquals(Algorithms.GetUniqueId(leaf, 2), Algorithms.GetUniqueId(leaf, 1));
    }

    /**
     * Create a {@link TrustManager} instance that trusts the root certificate in this bundle.
     * @return The new {@link TrustManager}.
     */
    // CLR: the X509TrustManager is an X509Chain whose ChainPolicy is ToTrustManagerFactory(); X509Chain.Build is the
    // check that checkClientTrusted/checkServerTrusted perform.
    public X509Chain ToTrustManager()
    {
        X509ChainPolicy tmf = ToTrustManagerFactory();
        X509Chain trustManager = new X509Chain();
        trustManager.ChainPolicy = tmf;
        return trustManager;
    }

    /**
     * Create {@link TrustManagerFactory} instance that trusts the root certificate in this bundle.
     * <p>
     * The trust manager factory will use the {@linkplain TrustManagerFactory#getDefaultAlgorithm() default algorithm}.
     *
     * @return The new {@link TrustManagerFactory}.
     */
    // CLR: an X509ChainPolicy whose CustomTrustStore, which "Represents a collection of certificates replacing the default
    // certificate trust", holds the root certificate (TrustMode CustomRootTrust,
    // https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509chainpolicy?view=net-10.0).
    // "If the TrustManagerFactory.init(KeyStore) method is used, then default PKIX parameters are used with the exception
    // that revocation checking is disabled" (JSSE Reference Guide,
    // https://docs.oracle.com/en/java/javase/17/security/java-secure-socket-extension-jsse-reference-guide.html), while
    // "Online is the default value" of X509ChainPolicy.RevocationMode
    // (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509chainpolicy.revocationmode?view=net-10.0),
    // so RevocationMode is NoCheck.
    public X509ChainPolicy ToTrustManagerFactory()
    {
        X509ChainPolicy tmf = new X509ChainPolicy();
        tmf.TrustMode = X509ChainTrustMode.CustomRootTrust;
        tmf.CustomTrustStore.Add(root);
        tmf.RevocationMode = X509RevocationMode.NoCheck;
        return tmf;
    }

    /**
     * Create a {@link KeyStore} with the contents of this bundle.
     * The root certificate will be a trusted root in the key store.
     * If this bundle has a {@linkplain #getPrivateKeyPEM() private key},
     * then the private key and certificate path will also be added to the key store.
     * <p>
     * The key store will use the PKCS#12 format.
     *
     * @param keyEntryPassword The password used to encrypt the private key entry in the key store.
     * @return The key store.
     * @throws KeyStoreException If an error occurred when adding entries to the key store.
     */
    // CLR: the key store is the PKCS #12 encoding of an X509Certificate2Collection that holds the root certificate and,
    // when the bundle has a private key, the leaf certificate carrying that key (the key entry) followed by the rest of
    // the certificate path; X509Certificate2Collection.Export protects it with keyEntryPassword ("A string used to
    // protect the byte array",
    // https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificate2collection.export?view=net-10.0),
    // and X509CertificateLoader.LoadPkcs12Collection reads it back. KeyStoreException is CryptographicException. On this
    // Windows host the BCL cannot attach an ML-DSA or ML-KEM private key to a certificate (CopyWithPrivateKey fails in
    // the CNG key import, docs/pkitesting-porting.md), so such a bundle cannot be exported.
    public byte[] ToKeyStore(char[] keyEntryPassword)
    {
        X509Certificate2Collection keyStore = new X509Certificate2Collection();
        keyStore.Add(root);
        object privateKey = Algorithms.GetPrivate(keyPair);
        if (privateKey != null)
        {
            keyStore.Add(Algorithms.CopyWithPrivateKey(certPath[0], privateKey));

            for (int i = 1; i < certPath.Length; i++)
            {
                keyStore.Add(certPath[i]);
            }
        }

        return keyStore.Export(X509ContentType.Pkcs12, new string(keyEntryPassword));
    }

    /**
     * Create a temporary PKCS#12 file with the {@linkplain #toKeyStore(char[]) key store} of this bundle.
     * The temporary file is automatically deleted when the JVM terminates normally.
     * @param password The password used both to encrypt the private key in the key store,
     * and to protect the key store itself.
     * @return The {@link File} object with the path to the PKCS#12 key store.
     * @throws Exception If something went wrong with creating the key store file.
     */
    public FileInfo ToTempKeyStoreFile(char[] password)
    {
        byte[] keyStore = ToKeyStore(password);
        return CreateTempFile(keyStore, "ks", ".p12");
    }

    /**
     * Create a temporary PEM file with the {@linkplain #getRootCertificate() root certificate} of this bundle.
     * The temporary file is automatically deleted when the JVM terminates normally.
     * @return The {@link File} object with the path to the trust root PEM file.
     * @throws IOException If an IO error occurred when creating the trust root file.
     */
    public FileInfo ToTempRootCertPem()
    {
        return CreateTempPemFile(GetRootCertificatePEM(), "ca");
    }

    /**
     * Create a temporary PEM file with the {@linkplain #getCertificatePath() certificate chain} of this bundle.
     * The temporary file is automatically deleted when the JVM terminates normally.
     * @return The {@link File} object with the path to the certificate chain PEM file.
     * @throws IOException If an IO error occurred when creating the certificate chain file.
     */
    public FileInfo ToTempCertChainPem()
    {
        return CreateTempPemFile(GetCertificatePathPEM(), "chain");
    }

    /**
     * Create a temporary PEM file with the {@linkplain #getPrivateKeyPEM() private key} of this bundle.
     * The temporary file is automatically deleted when the JVM terminates normally.
     * @return The {@link File} object with the path to the private key PEM file.
     * @throws IOException If an IO error occurred when creating the private key file.
     */
    public FileInfo ToTempPrivateKeyPem()
    {
        return CreateTempPemFile(GetPrivateKeyPEM(), "key");
    }

    // CLR: StandardCharsets.ISO_8859_1 is Encoding.Latin1.
    private static FileInfo CreateTempPemFile(string pem, string filePrefix)
    {
        return CreateTempFile(Encoding.Latin1.GetBytes(pem), filePrefix, ".pem");
    }

    // CLR: Files.createTempFile(prefix, suffix) creates a new file with a random name between the prefix and the suffix
    // in the temporary directory; Path.GetTempFileName takes neither, so the name is made here and the file is created
    // with FileMode.CreateNew. deleteOnExit is the ProcessExit list above.
    private static FileInfo CreateTempFile(byte[] content, string filePrefix, string fileSuffix)
    {
        string tempFile = Path.Combine(Path.GetTempPath(), filePrefix + Guid.NewGuid().ToString("N") + fileSuffix);

        using (FileStream @out = new FileStream(tempFile, FileMode.CreateNew, FileAccess.Write))
        {
            @out.Write(content);
        }

        lock (DELETE_ON_EXIT)
        {
            DELETE_ON_EXIT.Add(tempFile);
        }

        return new FileInfo(tempFile);
    }

    /**
     * Create a {@link KeyManagerFactory} from this bundle.
     * <p>
     * The {@link KeyManagerFactory} will use the
     * {@linkplain KeyManagerFactory#getDefaultAlgorithm() default algorithm}.
     *
     * @return The new {@link KeyManagerFactory}.
     * @throws KeyStoreException If there was a problem creating or initializing the key store.
     * @throws UnrecoverableKeyException If the private key could not be recovered,
     * for instance if this bundle is a {@linkplain #isCertificateAuthority() certificate authority}.
     * @throws NoSuchAlgorithmException If the key manager factory algorithm is not supported by the current
     * security provider.
     */
    // CLR: the SslStream counterpart of a KeyManagerFactory is the SslStreamCertificateContext of
    // SslServerAuthenticationOptions.ServerCertificateContext and SslClientAuthenticationOptions.ClientCertificateContext:
    // the leaf certificate with its private key and the intermediate certificates (Create(target,
    // additionalCertificates, offline); "target: The server certificate", "Supplementary certificates used to build the
    // certificate chain"). As the KeyManagerFactory reads the key store of toKeyStore(EMPTY_CHARS), the leaf certificate
    // is the key entry loaded back from ToKeyStore(EMPTY_CHARS): with the ephemeral key of CopyWithPrivateKey the Windows
    // SslStream server fails with "Authentication failed because the platform does not support ephemeral keys", while
    // the PKCS #12-loaded key completes a TLS 1.3 handshake (both measured on this host, docs/pkitesting-porting.md).
    // offline is true, so no certificate is downloaded. A bundle without
    // a private key has no key entry, and Create would throw "NotSupportedException if target has no private key", so it
    // throws CryptographicException (KeyStoreException/UnrecoverableKeyException).
    public SslStreamCertificateContext ToKeyManagerFactory()
    {
        X509Certificate2Collection keyStore =
            X509CertificateLoader.LoadPkcs12Collection(ToKeyStore(EmptyArrays.EMPTY_CHARS), string.Empty);
        X509Certificate2 target = null;

        foreach (X509Certificate2 cert in keyStore)
        {
            if (cert.HasPrivateKey)
            {
                target = cert;
            }
        }

        if (target == null)
        {
            throw new CryptographicException("The bundle has no private key entry.");
        }

        X509Certificate2Collection intermediates = new X509Certificate2Collection();

        for (int i = 1; i < certPath.Length; i++)
        {
            intermediates.Add(certPath[i]);
        }

        SslStreamCertificateContext context = SslStreamCertificateContext.Create(target, intermediates, offline: true);
        // CLR: a self-signed bundle puts the same certificate into the key store twice (root and key entry), and both
        // loaded entries carry the private key in one key container; when the entry that is not the target is finalized,
        // the key of the target goes with it and the SslStream server fails with SEC_E_UNKNOWN_CREDENTIALS (0x8009030D;
        // measured: 6 of 6 with a forced GC.Collect + WaitForPendingFinalizers, about 1 in 70 test runs without). The
        // loaded entries are therefore kept as long as the context: ConditionalWeakTable "automatically removes the
        // key/value entry as soon as no other references to a key exist outside the table"
        // (https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.conditionalweaktable-2?view=net-10.0).
        KEY_STORE_ENTRIES.AddOrUpdate(context, keyStore);
        return context;
    }

    // CLR-only: the PKCS #12 entries loaded by ToKeyManagerFactory, alive while their SslStreamCertificateContext is.
    private static readonly ConditionalWeakTable<SslStreamCertificateContext, X509Certificate2Collection> KEY_STORE_ENTRIES =
        new ConditionalWeakTable<SslStreamCertificateContext, X509Certificate2Collection>();

    /**
     * Create a new {@link X509Bundle} that has the leaf and root certificates of <em>this</em> bundle,
     * but a certificate path that is the combination all the intermediate certificates of both this and the given
     * bundle.
     * <p>
     * This is useful when building a bundle with a cross-signed certificate,
     * or when you just want to have additional unrelated intermediate certificates in the path.
     *
     * @param other The other bundle.
     * @return A new {@link X509Bundle} that has the leaf and root of this bundle,
     * but the combined intermediates of both bundles.
     */
    // CLR: LinkedHashSet keeps insertion order and drops certificates whose encoded forms are equal (Certificate.equals);
    // the list skips certificates with equal RawData.
    public X509Bundle MergeIntermediates(X509Bundle other)
    {
        List<X509Certificate2> certs = new List<X509Certificate2>();

        foreach (X509Certificate2 cert in certPath)
        {
            AddIfAbsent(certs, cert);
        }

        X509Certificate2[] otherPath = other.certPath;

        for (int i = 1; i < otherPath.Length; i++)
        {
            X509Certificate2 intermediate = otherPath[i];
            AddIfAbsent(certs, intermediate);
        }

        return FromCertificatePath(certs.ToArray(), root, keyPair);
    }

    private static void AddIfAbsent(List<X509Certificate2> certs, X509Certificate2 cert)
    {
        foreach (X509Certificate2 existing in certs)
        {
            if (CertificateEquals(existing, cert))
            {
                return;
            }
        }

        certs.Add(cert);
    }

    private static bool CertificateEquals(X509Certificate2 a, X509Certificate2 b)
    {
        return a.RawDataMemory.Span.SequenceEqual(b.RawDataMemory.Span);
    }

    // CLR: Arrays.equals(boolean[], boolean[]) of the unique identifiers, true when both are absent (null).
    private static bool ArraysEquals(byte[] a, byte[] b)
    {
        if (a == null || b == null)
        {
            return a == b;
        }

        return a.AsSpan().SequenceEqual(b);
    }
}
