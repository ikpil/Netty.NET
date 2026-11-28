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
using System.Formats.Asn1;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using static Netty.NET.PkiTesting.CertificateBuilder;

namespace Netty.NET.PkiTesting.Tests;

// CLR: the JCA/JSSE checks of the Java tests use the BCL: a certificate signature is verified with Algorithms.Verify
// over the tbsCertificate and signature read from the DER certificate (Signature.initVerify/update/verify), and the
// PKIX X509TrustManager is an X509Chain (TrustMode CustomRootTrust with the root in CustomTrustStore, revocation
// X509RevocationMode.Online, https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509chainpolicy?view=net-10.0),
// whose Build is checkClientTrusted; CheckClientTrusted adds the intermediates to ExtraStore, requires the client
// authentication extended key usage through ApplicationPolicy, as the PKIX trust manager does for a client chain, and
// throws a CryptographicException naming the chain status flags (CertificateException). The JUnit assumptions
// (assumeTrue, @EnabledForJreRange) are Assert.SkipUnless/SkipWhen, so an algorithm the BCL does not support on the
// running platform skips the test (Ed25519 and Ed448 always, SLH-DSA where SlhDsa.IsSupported is false). @EnumSource is
// MemberData over Algorithm.Values(), the names= form AlgorithmsNamed. The X509Certificate accessors are the BCL
// properties or the Algorithms helpers (getSubjectX500Principal is SubjectName, compared by its DER encoding because
// X500DistinguishedName has no equality; getSerialNumber, getBasicConstraints, getSigAlgName, getTBSCertificate and
// getSignature are Algorithms members), and the AssertJ assertions are xUnit assertions. Instant is DateTimeOffset and
// the unused SecureRandom RNG field is not ported (the BCL key generation takes no random source).
public class CertificateBuilderTest
{
    private static readonly DateTimeOffset NOW = DateTimeOffset.UtcNow;
    private const string SUBJECT = "CN=netty.io, O=Netty";
    private static readonly CertificateBuilder BASE = new CertificateBuilder()
            .NotBefore(NOW.AddDays(-1))
            .NotAfter(NOW.AddDays(1))
            .Subject(SUBJECT);

    public static TheoryData<Algorithm> AllAlgorithms()
    {
        return new TheoryData<Algorithm>(Algorithm.Values());
    }

    public static TheoryData<Algorithm> AlgorithmsNamed(string names)
    {
        string[] selected = names.Split(',');
        return new TheoryData<Algorithm>(Algorithm.Values().Where(a => selected.Contains(a.Name())));
    }

    [Theory]
    [MemberData(nameof(AllAlgorithms))]
    public void CreateCertOfEveryKeyType(Algorithm algorithm)
    {
        // Assume that RSA 4096 and RSA 8192 work if the other RSA bit-widths work.
        // These big keys just take too long to test with.
        Assert.SkipUnless(algorithm != Algorithm.rsa4096 && algorithm != Algorithm.rsa8192, "RSA 4096/8192 not tested");
        Assert.SkipUnless(algorithm.IsSupported(), algorithm + " is not supported by the BCL on this platform");
        Assert.SkipUnless(algorithm.SupportSigning(), algorithm + " does not support signing");

        X509Bundle bundle = BASE.Copy()
                .Algorithm_(algorithm)
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();
        X509Certificate2 cert = bundle.GetCertificate();
        Assert.True(bundle.IsCertificateAuthority());
        Assert.True(bundle.IsSelfSigned());
        AssertPrincipal(SUBJECT, cert.SubjectName);
    }

    [Theory]
    [MemberData(nameof(AllAlgorithms))]
    public void CreateKeyPairOfEveryKeyType(Algorithm algorithm)
    {
        // Assume that RSA 4096 and RSA 8192 work if the other RSA bit-widths work.
        // These big keys just take too long to test with.
        Assert.SkipUnless(algorithm != Algorithm.rsa4096 && algorithm != Algorithm.rsa8192, "RSA 4096/8192 not tested");
        Assert.SkipUnless(algorithm.IsSupported(), algorithm + " is not supported by the BCL on this platform");

        object keyPair = algorithm.GenerateKeyPair();
        Assert.NotNull(keyPair);
        Assert.NotNull(Algorithms.GetPrivate(keyPair));
        Assert.NotNull(Algorithms.GetPublic(keyPair));
    }

    // CLR: generateKeyPair(RNG, new BouncyCastleProvider()) asks the Bouncy Castle provider for the key generator; the BCL
    // has no provider to choose, so the test generates the keys of these algorithms with the platform implementation.
    [Theory]
    [MemberData(nameof(AlgorithmsNamed), "ecp256,ecp384,rsa2048,rsa3072")]
    public void CreateKeyPairWithProvider(Algorithm algorithm)
    {
        Assert.SkipUnless(algorithm.IsSupported(), algorithm + " is not supported by the BCL on this platform");

        object keyPair = algorithm.GenerateKeyPair();
        Assert.NotNull(keyPair);
        Assert.NotNull(Algorithms.GetPrivate(keyPair));
        Assert.NotNull(Algorithms.GetPublic(keyPair));
    }

    // CLR: @EnabledForJreRange(min = JAVA_24) enables the test where ML-KEM and ML-DSA exist; here, where MLKem and MLDsa
    // are supported on the running platform.
    [Theory]
    [MemberData(nameof(AlgorithmsNamed), "mlKem512,mlKem768,mlKem1024")]
    public void CreateMlKemCerts(Algorithm algorithm)
    {
        Assert.SkipUnless(algorithm.IsSupported() && Algorithm.mlDsa44.IsSupported(),
                          "ML-KEM is only supported in Java 24 onwards");
        CertificateBuilder mlKemBuilder = BASE.Copy()
                .Algorithm_(algorithm);

        // ML-KEM cannot be used to sign itself
        Assert.Throws<InvalidOperationException>(() =>
        {
            mlKemBuilder.Copy().SetIsCertificateAuthority(true).BuildSelfSigned();
        });

        CertificateBuilder mlDsaBuilder = BASE.Copy()
                .Algorithm_(Algorithm.mlDsa44);
        X509Bundle issuer = mlDsaBuilder
                .Subject("CN=issuer.netty.io, O=Netty")
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();

        // ML-KEM can be signed by others
        X509Bundle mlKemBundle = mlKemBuilder.BuildIssuedBy(issuer);

        X509Certificate2 cert = mlKemBundle.GetCertificate();
        Assert.False(mlKemBundle.IsCertificateAuthority());
        Assert.False(mlKemBundle.IsSelfSigned());
        AssertPrincipal(SUBJECT, cert.SubjectName);

        // ML-KEM cannot sign others
        ArgumentException e = Assert.Throws<ArgumentException>(() =>
        {
            mlDsaBuilder.BuildIssuedBy(mlKemBundle);
        });
        Assert.Contains("cannot be used for signing", e.Message);
    }

    // CLR: @EnabledForJreRange(max = JAVA_11) runs the test only on JVMs without EdDSA of their own, where the
    // BouncyCastleJsseProvider has no EdDSA key generator and buildIssuedBy throws NoSuchAlgorithmException. The BCL has
    // no EdDSA, so the test runs while ed25519/ed448 report unsupported (Assert.SkipWhen as the JRE range), and the
    // builder rejects the algorithm with NotSupportedException when it is set, before a key is generated; there is no
    // provider to pass, so the builder is created inside the assertion.
    [Theory]
    [MemberData(nameof(AlgorithmsNamed), "ed25519,ed448")]
    public void CreateEdCertsWithProvider(Algorithm algorithm)
    {
        Assert.SkipWhen(algorithm.IsSupported(), "EdXxx are supported in recent Java version");

        CertificateBuilder mlDsaBuilder = BASE.Copy()
                .Algorithm_(Algorithm.rsa2048);
        X509Bundle issuer = mlDsaBuilder
                .Subject("CN=issuer.netty.io, O=Netty")
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();

        // edXxx are not supported by BouncyCastleJsseProvider
        Assert.Throws<NotSupportedException>(
            () => BASE.Copy().Algorithm_(algorithm).BuildIssuedBy(issuer));
    }

    [Fact]
    public void CreateCertIssuedBySameAlgorithm()
    {
        CertificateBuilder builder = BASE.Copy().Ecp256();
        X509Bundle root = builder.Copy()
                .SetKeyUsage(true, KeyUsage.digitalSignature, KeyUsage.keyCertSign)
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();
        X509Bundle leaf = builder.Copy()
                .Subject("CN=leaf.netty.io, O=Netty")
                .BuildIssuedBy(root);
        AssertPrincipal("CN=leaf.netty.io, O=Netty", leaf.GetCertificate().SubjectName);
        AssertPrincipal(SUBJECT, leaf.GetCertificate().IssuerName);

        AssertSignature(root, leaf);
    }

    [Fact]
    public void CreateCertIssuedByDifferentAlgorithmEcp384vsEcp256()
    {
        X509Bundle root = BASE.Copy()
                .Algorithm_(Algorithm.ecp384)
                .SetKeyUsage(true, KeyUsage.digitalSignature, KeyUsage.keyCertSign)
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();
        X509Bundle leaf = BASE.Copy()
                .Ecp256()
                .Subject("CN=leaf.netty.io, O=Netty")
                .BuildIssuedBy(root);
        AssertPrincipal("CN=leaf.netty.io, O=Netty", leaf.GetCertificate().SubjectName);
        AssertPrincipal(SUBJECT, leaf.GetCertificate().IssuerName);

        AssertSignature(root, leaf);
    }

    [Fact]
    public void CreateCertIssuedByDifferentAlgorithmEcp256vsRsa2048()
    {
        X509Bundle root = BASE.Copy()
                .Ecp256()
                .SetKeyUsage(true, KeyUsage.digitalSignature, KeyUsage.keyCertSign)
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();
        X509Bundle leaf = BASE.Copy()
                .Rsa2048()
                .Subject("CN=leaf.netty.io, O=Netty")
                .BuildIssuedBy(root);
        AssertPrincipal("CN=leaf.netty.io, O=Netty", leaf.GetCertificate().SubjectName);
        AssertPrincipal(SUBJECT, leaf.GetCertificate().IssuerName);

        AssertSignature(root, leaf);
    }

    // CLR: Java runs this test without an assumption; Algorithm.ed25519 is unsupported in the BCL, so it is skipped as the
    // other unsupported algorithms are.
    [Fact]
    public void CreateCertIssuedByDifferentAlgorithmEd25519vEcp256()
    {
        Assert.SkipUnless(Algorithm.ed25519.IsSupported(), "ed25519 is not supported by the BCL on this platform");
        X509Bundle root = BASE.Copy()
                .Algorithm_(Algorithm.ed25519)
                .SetKeyUsage(true, KeyUsage.digitalSignature, KeyUsage.keyCertSign)
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();
        X509Bundle leaf = BASE.Copy()
                .Ecp256()
                .Subject("CN=leaf.netty.io, O=Netty")
                .BuildIssuedBy(root);
        AssertPrincipal("CN=leaf.netty.io, O=Netty", leaf.GetCertificate().SubjectName);
        AssertPrincipal(SUBJECT, leaf.GetCertificate().IssuerName);

        AssertSignature(root, leaf);
    }

    // CLR: as CreateCertIssuedByDifferentAlgorithmEd25519vEcp256, for Algorithm.ed448.
    [Fact]
    public void CreateCertIssuedByDifferentAlgorithmEd448vEcp256()
    {
        Assert.SkipUnless(Algorithm.ed448.IsSupported(), "ed448 is not supported by the BCL on this platform");
        X509Bundle root = BASE.Copy()
                .Algorithm_(Algorithm.ed448)
                .SetKeyUsage(true, KeyUsage.digitalSignature, KeyUsage.keyCertSign)
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();
        X509Bundle leaf = BASE.Copy()
                .Ecp256()
                .Subject("CN=leaf.netty.io, O=Netty")
                .BuildIssuedBy(root);
        AssertPrincipal("CN=leaf.netty.io, O=Netty", leaf.GetCertificate().SubjectName);
        AssertPrincipal(SUBJECT, leaf.GetCertificate().IssuerName);

        AssertSignature(root, leaf);
    }

    // CLR: getSubjectAlternativeNames() is GetSubjectAlternativeNames, which decodes the extension into the same
    // [type, value] lists (RFC 2253 directory name, dotted IPv4 text, DER OtherName and, as newer JDKs add, its OID and
    // value). It decodes the OtherName as Java 19 and later do, so the branch for older Java versions has no counterpart;
    // its comment is kept below. The negative Java byte literals are written with unchecked byte casts (same bits).
    [Fact]
    public void CreateCertificateWithSans()
    {
        X509Bundle root = BASE.Copy()
                .SetIsCertificateAuthority(true)
                .SetKeyUsage(true, KeyUsage.digitalSignature, KeyUsage.keyCertSign)
                .BuildSelfSigned();
        X509Bundle leaf = BASE.Copy()
                .Subject("CN=leaf.netty.io")
                .AddSanDirectoryName("CN=san.leaf.netty.io")
                .AddSanDnsName("san-1.leaf.netty.io")
                .AddSanDnsName("san-2.leaf.netty.io")
                .AddSanIpAddress("192.0.2.1") // RFC 5737 example IP
                .AddSanRfc822Name("san@netty.io")
                .AddSanRegisteredId("1.2.840.113635.100.1.2.42")
                .AddSanUriName("spiffe://netty.io/example/san")
                .AddSanOtherName("1.2.840.113635.100.1.2.42", new byte[] { 0x01, 0x01, 0x00 })
                .BuildIssuedBy(root);
        X509Certificate2 cert = leaf.GetCertificate();
        AssertPrincipal("CN=leaf.netty.io", cert.SubjectName);
        List<List<object>> sans = new List<List<object>>(GetSubjectAlternativeNames(cert));
        Assert.Equal(8, sans.Count);
        Assert.Equal(new List<object> { 4, "CN=san.leaf.netty.io" }, sans[0]);
        Assert.Equal(new List<object> { 2, "san-1.leaf.netty.io" }, sans[1]);
        Assert.Equal(new List<object> { 2, "san-2.leaf.netty.io" }, sans[2]);
        Assert.Equal(new List<object> { 7, "192.0.2.1" }, sans[3]);
        Assert.Equal(new List<object> { 1, "san@netty.io" }, sans[4]);
        Assert.Equal(new List<object> { 8, "1.2.840.113635.100.1.2.42" }, sans[5]);
        Assert.Equal(new List<object> { 6, "spiffe://netty.io/example/san" }, sans[6]);
        Assert.Equal(0, sans[7][0]);
        Assert.Equal(Bytes(
                48, 17, 6, 10, 42, -122, 72, -122, -9, 99, 100, 1, 2, 42, -96, 3, 1, 1, 0), sans[7][1]);

        // Java versions older than 19 re-encode the value in an extra context-constructed tag, the {-96, 5} bit.
        if (sans[7].Count > 2)
        {
            Assert.Equal("1.2.840.113635.100.1.2.42", sans[7][2]);
            Assert.Equal(new byte[] { 0x01, 0x01, 0x00 }, sans[7][3]);
        }
    }

    // CLR: ExtendedKeyUsage.values() is Values(), the TreeSet a SortedSet with ordinal order, getExtendedKeyUsage() the
    // OIDs of the X509EnhancedKeyUsageExtension, and containsExactlyInAnyOrderElementsOf compares the sorted sequences.
    [Fact]
    public void CreateCertificteWithExtendedKeyUsage()
    {
        CertificateBuilder builder = BASE.Copy()
                .SetIsCertificateAuthority(true)
                .SetKeyUsage(true, KeyUsage.digitalSignature, KeyUsage.keyCertSign)
                .AddExtendedKeyUsage("1.2.840.113635.100.1.2.42");

        foreach (CertificateBuilder.ExtendedKeyUsage extendedKeyUsage in CertificateBuilder.ExtendedKeyUsage.Values())
        {
            builder.AddExtendedKeyUsage(extendedKeyUsage);
        }

        X509Bundle bundle = builder.BuildSelfSigned();
        SortedSet<string> expectedExtendedKeyUsage = new SortedSet<string>(StringComparer.Ordinal);
        expectedExtendedKeyUsage.Add("1.2.840.113635.100.1.2.42");

        foreach (CertificateBuilder.ExtendedKeyUsage extendedKeyUsage in CertificateBuilder.ExtendedKeyUsage.Values())
        {
            expectedExtendedKeyUsage.Add(extendedKeyUsage.GetOid());
        }

        List<string> actualExtendedKeyUsage = GetExtendedKeyUsage(bundle.GetCertificate());
        Assert.Equal(expectedExtendedKeyUsage, actualExtendedKeyUsage.OrderBy(oid => oid, StringComparer.Ordinal));
    }

    // CLR: hasUnsupportedCriticalExtension() is the absence of X509ChainStatusFlags.HasNotSupportedCriticalExtension when
    // the chain engine evaluates the certificate as its own trusted root; getKeyUsage() is GetKeyUsage, the nine named
    // bits of the X509KeyUsageExtension; checkValidity() checks that the current time is within NotBefore/NotAfter, and
    // getNotBefore().getTime() is the Unix time in milliseconds of NotBefore (a local DateTime,
    // https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificate2.notbefore?view=net-10.0).
    [Fact]
    public void CreateCertificateWithOtherFields()
    {
        X509Bundle root = BASE.Copy()
                .SetIsCertificateAuthority(true)
                .SetKeyUsage(true, KeyUsage.digitalSignature, KeyUsage.keyCertSign)
                .SetPathLengthConstraint(42)
                .Serial(new BigInteger(10))
                .BuildSelfSigned();
        X509Certificate2 cert = root.GetCertificate();
        Assert.Equal(42, Algorithms.GetBasicConstraints(cert));
        Assert.Equal(new BigInteger(10), Algorithms.GetSerialNumber(cert));
        Assert.Equal(3, cert.Version);
        Assert.False(HasUnsupportedCriticalExtension(cert));
        Assert.Equal(
                new bool[] { true, false, false, false, false, true, false, false, false }, GetKeyUsage(cert));
        DateTime now = DateTime.Now;
        Assert.InRange(now, cert.NotBefore, cert.NotAfter);
        Assert.Equal(TruncatedToSeconds(NOW.AddDays(-1)).ToUnixTimeMilliseconds(),
                     new DateTimeOffset(cert.NotBefore).ToUnixTimeMilliseconds());
        Assert.Equal(TruncatedToSeconds(NOW.AddDays(1)).ToUnixTimeMilliseconds(),
                     new DateTimeOffset(cert.NotAfter).ToUnixTimeMilliseconds());
    }

    // CLR: register(root, provider) is Register(root): the JCA Provider that signs the CRL has no BCL counterpart, so
    // Providers() keeps only the default row and the parameter is unused (xUnit1026 is suppressed for that reason).
#pragma warning disable xUnit1026 // the provider row is kept for the 1:1 port; the BCL signs the CRL without a provider.
    [Theory]
    [MemberData(nameof(Providers))]
    public void ValidCertificatesWithCrlMustPassValidation(object provider)
    {
        X509Bundle root = BASE.Copy()
                .SetIsCertificateAuthority(true)
                .SetKeyUsage(true, KeyUsage.digitalSignature, KeyUsage.keyCertSign, KeyUsage.cRLSign)
                .BuildSelfSigned();
        RevocationServer server = RevocationServer.GetInstance();
        server.Register(root);
        X509Bundle cert = BASE.Copy()
                .Subject("CN=leaf.netty.io")
                .AddCrlDistributionPoint(server.GetCrlUri(root))
                .AddExtendedKeyUsageClientAuth()
                .BuildIssuedBy(root);

        X509Chain tm = GetX509TrustManager(root);

        // Assert that this does not throw:
        CheckClientTrusted(tm, cert.GetCertificatePath());
    }

    // CLR: the CertificateException "Certificate has been revoked" is the CryptographicException of CheckClientTrusted,
    // whose message names X509ChainStatusFlags.Revoked.
    [Theory]
    [MemberData(nameof(Providers))]
    public void RevokedCertificatesWithCrlMustFailValidation(object provider)
    {
        X509Bundle root = BASE.Copy()
                .SetIsCertificateAuthority(true)
                .SetKeyUsage(true, KeyUsage.digitalSignature, KeyUsage.keyCertSign, KeyUsage.cRLSign)
                .BuildSelfSigned();
        RevocationServer server = RevocationServer.GetInstance();
        server.Register(root);
        X509Bundle cert = BASE.Copy()
                .Subject("CN=leaf.netty.io")
                .AddCrlDistributionPoint(server.GetCrlUri(root))
                .AddExtendedKeyUsageClientAuth()
                .BuildIssuedBy(root);
        server.Revoke(cert, NOW);

        X509Chain tm = GetX509TrustManager(root);

        // Assert that this does not throw:
        CryptographicException ce = Assert.Throws<CryptographicException>(
                () => CheckClientTrusted(tm, cert.GetCertificatePath()));
        Assert.Contains(nameof(X509ChainStatusFlags.Revoked), ce.Message);
    }
#pragma warning restore xUnit1026

    // CLR: keyPair.getPublic() is the BCL PublicKey of the key object; Algorithms.GetPublic makes a new PublicKey for
    // each call, so the test keeps the one it passes and checks that the bundle holds that object (isSameAs). The
    // NullPointerException of getPrivateKeyPEM() is NullReferenceException.
    [Fact]
    public void GenerateCertificateWithGivenPublicKey()
    {
        X509Bundle root = BASE.Copy()
                .SetIsCertificateAuthority(true)
                .SetKeyUsage(true, KeyUsage.digitalSignature, KeyUsage.keyCertSign, KeyUsage.cRLSign)
                .BuildSelfSigned();
        object keyPair = Algorithm.ecp256.GenerateKeyPair();
        PublicKey publicKey = Algorithms.GetPublic(keyPair);
        X509Bundle bundle = BASE.Copy()
                .Subject("CN=leaf.netty.io")
                .PublicKey(publicKey)
                .BuildIssuedBy(root);
        Assert.Same(publicKey, Algorithms.GetPublic(bundle.GetKeyPair()));
        Assert.Null(Algorithms.GetPrivate(bundle.GetKeyPair()));
        Assert.Throws<NullReferenceException>(() => bundle.GetPrivateKeyPEM());
    }

    // CLR: the key pair is one BCL key object, so getPublic() and getPrivate() are both checked by its identity.
    [Fact]
    public void GenerateCertificateWithGivenKeyPair()
    {
        X509Bundle root = BASE.Copy()
                .SetIsCertificateAuthority(true)
                .SetKeyUsage(true, KeyUsage.digitalSignature, KeyUsage.keyCertSign, KeyUsage.cRLSign)
                .BuildSelfSigned();
        object keyPair = Algorithm.ecp256.GenerateKeyPair();
        X509Bundle bundle = BASE.Copy()
                .Subject("CN=leaf.netty.io")
                .KeyPair(keyPair)
                .BuildIssuedBy(root);
        Assert.Same(keyPair, bundle.GetKeyPair());
        Assert.Same(Algorithms.GetPrivate(keyPair), Algorithms.GetPrivate(bundle.GetKeyPair()));
        Assert.NotNull(bundle.GetPrivateKeyPEM());
    }

    // CLR: toTrustManager() is the X509Chain of ToTrustManager(); checkClientTrusted(chain, "UNKNOWN") is
    // CheckClientTrusted, which throws when no path to the trusted root is found.
    [Fact]
    public void AuthenticatingCrossSignedCertificate()
    {
        CertificateBuilder caBuilder = BASE.Copy()
                .SetIsCertificateAuthority(true)
                .SetKeyUsage(true, KeyUsage.digitalSignature, KeyUsage.keyCertSign, KeyUsage.cRLSign);

        // Roots. Different subjects.
        X509Bundle oldRoot = caBuilder.Subject("CN=root-a").BuildSelfSigned();
        X509Bundle newRoot = caBuilder.Subject("CN=root-b").BuildSelfSigned();

        // Intermediates. Same subject. Will be cross-signed.
        CertificateBuilder intermediateBuilder = caBuilder.Copy().Subject("CN=issuer.netty.io");
        X509Bundle oldIssuer = intermediateBuilder.BuildIssuedBy(oldRoot);
        X509Bundle crossIssuer = intermediateBuilder.KeyPair(oldIssuer.GetKeyPair()).BuildIssuedBy(newRoot);

        // Leaf. Cross-signed by cross-signed issuer.
        CertificateBuilder leafBuilder = BASE.Copy().Subject("CN=leaf.netty.io").AddExtendedKeyUsageClientAuth();
        X509Bundle leaf = leafBuilder.BuildIssuedBy(crossIssuer)
                .MergeIntermediates(leafBuilder.BuildIssuedBy(oldIssuer));

        // This trust manager only knows about the old root. It will throw if it can't find a path to it.
        X509Chain oldRootTrustManager = oldRoot.ToTrustManager();
        CheckClientTrusted(oldRootTrustManager, leaf.GetCertificatePath());
    }

    private static X509Chain GetX509TrustManager(X509Bundle root)
    {
        X509Chain tmf = new X509Chain();
        tmf.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        tmf.ChainPolicy.CustomTrustStore.Add(root.GetCertificate());

        // Explicitly add the revocation checker. We cannot use params.setRevocationEnabled(true) because
        // it will not do any online revocation checking, i.e. it will not do any network access.
        // CLR: X509RevocationMode.Online fetches the CRLs of the distribution points over the network; ExcludeRoot checks
        // every certificate but the trust anchor, as the PKIXRevocationChecker does.
        tmf.ChainPolicy.RevocationMode = X509RevocationMode.Online;
        tmf.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
        return tmf;
    }

    // CLR: BouncyCastleJsseProvider has no BCL counterpart; the BCL is the only provider, so only the default row remains.
    public static TheoryData<object> Providers()
    {
        return new TheoryData<object>
        {
            null /* default */,
        };
    }

    // CLR: X509TrustManager.checkClientTrusted(chain, authType); see the class comment.
    private static void CheckClientTrusted(X509Chain trustManager, X509Certificate2[] chain)
    {
        for (int i = 1; i < chain.Length; i++)
        {
            trustManager.ChainPolicy.ExtraStore.Add(chain[i]);
        }

        trustManager.ChainPolicy.ApplicationPolicy.Add(new Oid(OID_PKIX_KP_CLIENT_AUTH));
        if (!trustManager.Build(chain[0]))
        {
            throw new CryptographicException("Certificate path validation failed: " +
                    string.Join(", ", trustManager.ChainStatus.Select(s => s.Status + " (" + s.StatusInformation.Trim() + ")")));
        }
    }

    private static void AssertSignature(X509Bundle root, X509Bundle leaf)
    {
        string sigAlgName = Algorithms.GetSigAlgName(leaf.GetCertificate());
        Assert.True(Algorithms.Verify(sigAlgName, root.GetCertificate().PublicKey,
                Algorithms.GetTBSCertificate(leaf.GetCertificate()), Algorithms.GetSignature(leaf.GetCertificate())));
    }

    private static void AssertPrincipal(string expected, X500DistinguishedName actual)
    {
        Assert.Equal(new X500DistinguishedName(expected).RawData, actual.RawData);
    }

    private static DateTimeOffset TruncatedToSeconds(DateTimeOffset instant)
    {
        return DateTimeOffset.FromUnixTimeSeconds(instant.ToUnixTimeSeconds());
    }

    private static byte[] Bytes(params int[] values)
    {
        return values.Select(v => unchecked((byte) v)).ToArray();
    }

    private static List<string> GetExtendedKeyUsage(X509Certificate2 cert)
    {
        List<string> usages = new List<string>();

        foreach (X509Extension extension in cert.Extensions)
        {
            if (extension is X509EnhancedKeyUsageExtension eku)
            {
                foreach (Oid oid in eku.EnhancedKeyUsages)
                {
                    usages.Add(oid.Value);
                }
            }
        }

        return usages;
    }

    private static bool[] GetKeyUsage(X509Certificate2 cert)
    {
        foreach (X509Extension extension in cert.Extensions)
        {
            if (extension is X509KeyUsageExtension keyUsage)
            {
                int flags = (int) keyUsage.KeyUsages;
                bool[] bits = new bool[9];

                for (int i = 0; i < 8; i++)
                {
                    bits[i] = (flags & (0x80 >>> i)) != 0;
                }

                bits[8] = (flags & 0x8000) != 0;
                return bits;
            }
        }

        return null;
    }

    private static bool HasUnsupportedCriticalExtension(X509Certificate2 cert)
    {
        using X509Chain chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(cert);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.Build(cert);

        return chain.ChainStatus.Any(s => s.Status.HasFlag(X509ChainStatusFlags.HasNotSupportedCriticalExtension));
    }

    private static IEnumerable<List<object>> GetSubjectAlternativeNames(X509Certificate2 cert)
    {
        X509Extension extension = cert.Extensions["2.5.29.17"];
        AsnReader names = new AsnReader(extension.RawData, AsnEncodingRules.DER).ReadSequence();

        while (names.HasData)
        {
            Asn1Tag tag = names.PeekTag();

            switch (tag.TagValue)
            {
                case 0:
                {
                    byte[] otherName = names.ReadEncodedValue().ToArray();
                    otherName[0] = 0x30; // the OtherName as a universal SEQUENCE
                    AsnReader otherNameReader = new AsnReader(otherName, AsnEncodingRules.DER).ReadSequence();
                    string typeId = otherNameReader.ReadObjectIdentifier();
                    byte[] value = otherNameReader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0))
                            .ReadEncodedValue().ToArray();
                    yield return new List<object> { 0, otherName, typeId, value };
                    break;
                }
                case 1:
                case 2:
                case 6:
                    yield return new List<object>
                    {
                        tag.TagValue, names.ReadCharacterString(UniversalTagNumber.IA5String, tag)
                    };
                    break;
                case 4:
                    yield return new List<object>
                    {
                        4, new X500DistinguishedName(names.ReadSequence(tag).ReadEncodedValue().Span).Name
                    };
                    break;
                case 7:
                    yield return new List<object> { 7, new IPAddress(names.ReadOctetString(tag)).ToString() };
                    break;
                case 8:
                    yield return new List<object> { 8, names.ReadObjectIdentifier(tag) };
                    break;
                default:
                    names.ReadEncodedValue();
                    break;
            }
        }
    }
}
