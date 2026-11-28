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
using System.Globalization;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Ssl.Util;

/**
 * An {@link TrustManagerFactory} that trusts an X.509 certificate whose hash matches.
 * <p>
 * <strong>NOTE:</strong> It is recommended to verify certificates and their chain to prevent
 * <a href="https://en.wikipedia.org/wiki/Man-in-the-middle_attack">Man-in-the-middle attacks</a>.
 * This {@link TrustManagerFactory} will <strong>only</strong> verify that the fingerprint of certificates match one
 * of the given fingerprints. This procedure is called
 * <a href="https://en.wikipedia.org/wiki/Transport_Layer_Security#Certificate_pinning">certificate pinning</a> and
 * is an effective protection. For maximum security one should verify that the whole certificate chain is as expected.
 * It is worth mentioning that certain firewalls, proxies or other appliances found in corporate environments,
 * actually perform Man-in-the-middle attacks and thus present a different certificate fingerprint.
 * </p>
 * <p>
 * The hash of an X.509 certificate is calculated from its DER encoded format.  You can get the fingerprint of
 * an X.509 certificate using the {@code openssl} command.  For example:
 *
 * <pre>
 * $ openssl x509 -fingerprint -sha256 -in my_certificate.crt
 * SHA256 Fingerprint=1C:53:0E:6B:FF:93:F0:DE:C2:E6:E7:9D:10:53:58:FF:DD:8E:68:CD:82:D9:C9:36:9B:43:EE:B3:DC:13:68:FB
 * -----BEGIN CERTIFICATE-----
 * MIIC/jCCAeagAwIBAgIIIMONxElm0AIwDQYJKoZIhvcNAQELBQAwPjE8MDoGA1UE
 * AwwzZThhYzAyZmEwZDY1YTg0MjE5MDE2MDQ1ZGI4YjA1YzQ4NWI0ZWNkZi5uZXR0
 * eS50ZXN0MCAXDTEzMDgwMjA3NTEzNloYDzk5OTkxMjMxMjM1OTU5WjA+MTwwOgYD
 * VQQDDDNlOGFjMDJmYTBkNjVhODQyMTkwMTYwNDVkYjhiMDVjNDg1YjRlY2RmLm5l
 * dHR5LnRlc3QwggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIBAQDb+HBO3C0U
 * RBKvDUgJHbhIlBye8X/cbNH3lDq3XOOFBz7L4XZKLDIXS+FeQqSAUMo2otmU+Vkj
 * 0KorshMjbUXfE1KkTijTMJlaga2M2xVVt21fRIkJNWbIL0dWFLWyRq7OXdygyFkI
 * iW9b2/LYaePBgET22kbtHSCAEj+BlSf265+1rNxyAXBGGGccCKzEbcqASBKHOgVp
 * 6pLqlQAfuSy6g/OzGzces3zXRrGu1N3pBIzAIwCW429n52ZlYfYR0nr+REKDnRrP
 * IIDsWASmEHhBezTD+v0qCJRyLz2usFgWY+7agUJE2yHHI2mTu2RAFngBilJXlMCt
 * VwT0xGuQxkbHAgMBAAEwDQYJKoZIhvcNAQELBQADggEBAEv8N7Xm8qaY2FgrOc6P
 * a1GTgA+AOb3aU33TGwAR86f+nLf6BSPaohcQfOeJid7FkFuYInuXl+oqs+RqM/j8
 * R0E5BuGYY2wOKpL/PbFi1yf/Kyvft7KVh8e1IUUec/i1DdYTDB0lNWvXXxjfMKGL
 * ct3GMbEHKvLfHx42Iwz/+fva6LUrO4u2TDfv0ycHuR7UZEuC1DJ4xtFhbpq/QRAj
 * CyfNx3cDc7L2EtJWnCmivTFA9l8MF1ZPMDSVd4ecQ7B0xZIFQ5cSSFt7WGaJCsGM
 * zYkU4Fp4IykQcWxdlNX7wJZRwQ2TZJFFglpTiFZdeq6I6Ad9An1Encpz5W8UJ4tv
 * hmw=
 * -----END CERTIFICATE-----
 * </pre>
 * </p>
 */
// CLR: TrustManagerFactory/TrustManager map to X509ChainPolicy or a RemoteCertificateValidationCallback; a
// fingerprint check cannot be expressed as a chain policy, so the trust manager is a callback (the.NET TLS
// best-practices page implements certificate pinning as a validation callback,
// https://learn.microsoft.com/en-us/dotnet/core/extensions/sslstream-best-practices). The class does not derive
// from the JCA SPI helper SimpleTrustManagerFactory (jvm-only-replaced): engineGetTrustManagers() is internal
// (protected members of a sealed class have no subclass) and returns that callback, and an implicit conversion to
// RemoteCertificateValidationCallback keeps the Java call site SslContextBuilder.trustManager(factory) working
// (SslContextBuilder.TrustManager(RemoteCertificateValidationCallback)). engineInit(KeyStore) and
// engineInit(ManagerFactoryParameters), which do nothing, have no counterpart. java.util.regex.Pattern is Regex:
// Matcher.matches() matches the whole input, so the pattern is wrapped in \A(?:...)\z (the.NET $ also matches
// before a final newline, which Java's matches() rejects); replaceAll is Regex.Replace. The three @Deprecated
// SHA-1 constructors, whose @deprecated text names builder(String), are not ported.
public sealed class FingerprintTrustManagerFactory
{

    private static readonly Regex FINGERPRINT_PATTERN = new Regex(@"\A(?:^[0-9a-fA-F:]+$)\z");
    private static readonly Regex FINGERPRINT_STRIP_PATTERN = new Regex(":");

    /**
     * Creates a builder for {@link FingerprintTrustManagerFactory}.
     *
     * @param algorithm a hash algorithm
     * @return a builder
     */
    public static FingerprintTrustManagerFactoryBuilder Builder(string algorithm)
    {
        return new FingerprintTrustManagerFactoryBuilder(algorithm);
    }

    // CLR: FastThreadLocal<MessageDigest> exists because a MessageDigest instance is stateful and not thread safe; the
    // one-shot CryptographicOperations.HashData(HashAlgorithmName, byte[]) ("Computes the hash of data",
    // https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptographicoperations.hashdata?view=net-10.0)
    // keeps no state, so the field holds the algorithm name instead of a per-thread digest.
    private readonly HashAlgorithmName tlmd;

    private readonly TrustManager tm;

    private readonly byte[][] fingerprints;

    /**
     * Creates a new instance.
     *
     * @param algorithm a hash algorithm
     * @param fingerprints a list of fingerprints
     */
    // CLR: MessageDigest.getInstance(algorithm) with a JCA standard name ("SHA-256", "SHA-1", "SHA3-256", "MD5",
    // https://docs.oracle.com/en/java/javase/17/docs/specs/security/standard-names.html; the source's deprecated
    // constructors use the alias "SHA1") is the HashAlgorithmName of the upper-cased name without the hyphen after "SHA"
    // (ToHashAlgorithmName): the BCL names are "SHA1", "SHA256", "SHA3-256", "MD5" and are case-sensitive
    // (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.hashalgorithmname?view=net-10.0).
    // HashData throws CryptographicException for
    // an unknown algorithm and PlatformNotSupportedException for one the platform lacks, which become the
    // IllegalArgumentException (ArgumentException) of NoSuchAlgorithmException. getDigestLength() is the length of the
    // hash of no data. String.format("%d") is the invariant-culture integer text.
    internal FingerprintTrustManagerFactory(string algorithm, byte[][] fingerprints)
    {
        ObjectUtil.CheckNotNull(algorithm, "algorithm");
        ObjectUtil.CheckNotNull(fingerprints, "fingerprints");

        if (fingerprints.Length == 0)
        {
            throw new ArgumentException("No fingerprints provided");
        }

        // check early if the hash algorithm is available
        HashAlgorithmName md = ToHashAlgorithmName(algorithm);
        int hashLength;

        try
        {
            hashLength = CryptographicOperations.HashData(md, Array.Empty<byte>()).Length;
        }
        catch (Exception e) when (e is CryptographicException || e is PlatformNotSupportedException ||
                                  e is ArgumentException)
        {
            throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture, "Unsupported hash algorithm: {0}", algorithm), e);
        }

        List<byte[]> list = new List<byte[]>(fingerprints.Length);

        foreach (byte[] f in fingerprints)
        {
            if (f == null)
            {
                break;
            }
            if (f.Length != hashLength)
            {
                throw new ArgumentException(
                        string.Format(CultureInfo.InvariantCulture,
                                "malformed fingerprint (length is {0} but expected {1}): {2}",
                                f.Length, hashLength, ByteBufUtil.HexDump(Unpooled.WrappedBuffer(f))));
            }

            list.Add((byte[]) f.Clone());
        }

        tlmd = md;

        this.fingerprints = list.ToArray();
        tm = new TrustManager(this);
    }

    private static HashAlgorithmName ToHashAlgorithmName(string algorithm)
    {
        string name = algorithm.ToUpperInvariant();
        if (name.StartsWith("SHA-", StringComparison.Ordinal))
        {
            name = "SHA" + name.Substring(4);
        }

        return new HashAlgorithmName(name);
    }

    internal static byte[][] ToFingerprintArray(IEnumerable<string> fingerprints)
    {
        ObjectUtil.CheckNotNull(fingerprints, "fingerprints");

        List<byte[]> list = new List<byte[]>();

        foreach (string fingerprint in fingerprints)
        {
            string f = fingerprint;
            if (f == null)
            {
                break;
            }

            if (!FINGERPRINT_PATTERN.IsMatch(f))
            {
                throw new ArgumentException("malformed fingerprint: " + f);
            }

            f = FINGERPRINT_STRIP_PATTERN.Replace(f, "");

            list.Add(StringUtil.DecodeHexDump(f));
        }

        return list.ToArray();
    }

    // CLR: engineGetTrustManagers() returns the one trust manager.
    internal RemoteCertificateValidationCallback[] EngineGetTrustManagers()
    {
        return new RemoteCertificateValidationCallback[] { tm.Verify };
    }

    // CLR: the trust manager of this factory, so that SslContextBuilder.TrustManager(RemoteCertificateValidationCallback)
    // accepts the factory where Java passes it to trustManager(TrustManagerFactory) (see the class comment).
    public static implicit operator RemoteCertificateValidationCallback(FingerprintTrustManagerFactory factory)
    {
        return factory == null ? null : factory.tm.Verify;
    }

    // CLR: CertificateException (and CertificateEncodingException) is CryptographicException and getSubjectDN() is
    // X509Certificate.Subject. getAcceptedIssuers() has no callback counterpart. Verify is the
    // RemoteCertificateValidationCallback: the sender is the authenticating SslStream (measured), whose IsServer
    // tells whether the peer is a client (checkClientTrusted) or a server (checkServerTrusted; also for a sender
    // that is not an SslStream), chain[0] is the remote certificate, and a check that fails throws its
    // CryptographicException, which SslStream passes on to the authenticating caller (measured:
    // AuthenticateAsClientAsync throws the callback's exception), as the JSSE handshake fails with the trust
    // manager's CertificateException as cause. This trust manager is a plain X509TrustManager, which JSSE wraps
    // "with AbstractTrustManagerWrapper and add hostname verification by itself" (InsecureTrustManagerFactory); so
    // a remote certificate that SslStream reports with RemoteCertificateNameMismatch is rejected (SslContext
    // clears that flag when endpoint identification is disabled). A missing remote certificate cannot be
    // fingerprinted and is rejected.
    private sealed class TrustManager
    {
        private readonly FingerprintTrustManagerFactory parent;

        internal TrustManager(FingerprintTrustManagerFactory parent)
        {
            this.parent = parent;
        }

        internal bool Verify(object sender, X509Certificate certificate, X509Chain chain,
                             SslPolicyErrors sslPolicyErrors)
        {
            if (certificate == null)
            {
                return false;
            }

            X509Certificate2[] peerChain = { certificate as X509Certificate2 ?? new X509Certificate2(certificate) };

            if (sender is SslStream stream && stream.IsServer)
            {
                CheckClientTrusted(peerChain, null);
            }
            else
            {
                CheckServerTrusted(peerChain, null);
            }

            return (sslPolicyErrors & SslPolicyErrors.RemoteCertificateNameMismatch) == 0;
        }

        internal void CheckClientTrusted(X509Certificate2[] chain, string s)
        {
            CheckTrusted("client", chain);
        }

        internal void CheckServerTrusted(X509Certificate2[] chain, string s)
        {
            CheckTrusted("server", chain);
        }

        private void CheckTrusted(string type, X509Certificate2[] chain)
        {
            X509Certificate2 cert = chain[0];
            byte[] fingerprint = Fingerprint(cert);
            bool found = false;

            foreach (byte[] allowedFingerprint in parent.fingerprints)
            {
                if (fingerprint.AsSpan().SequenceEqual(allowedFingerprint))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new CryptographicException(
                        type + " certificate with unknown fingerprint: " + cert.Subject);
            }
        }

        // CLR: MessageDigest.digest(cert.getEncoded()) is the one-shot hash of RawData, the DER encoding.
        private byte[] Fingerprint(X509Certificate2 cert)
        {
            return CryptographicOperations.HashData(parent.tlmd, cert.RawData);
        }
    }
}
