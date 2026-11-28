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
using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Netty.NET.PkiTesting;

// CLR: the JCA provider machinery of this class (KeyPairGenerator/Signature engines looked up by name in the installed
// providers, with Bouncy Castle as the fallback provider) has no BCL counterpart: each BCL key type generates its keys
// with a static factory (ECDsa.Create(ECCurve), RSA.Create(int), MLDsa.GenerateKey, MLKem.GenerateKey,
// SlhDsa.GenerateKey) and signs and verifies with its own SignData/VerifyData
// (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.ecdsa.signdata?view=net-10.0), so the
// bouncyCastle() provider, its DefaultProvider holder and every Provider/SecureRandom parameter are not ported
// (owner decision 2026-11-24: pkitesting is ported on the BCL). The Java key objects (PublicKey, PrivateKey, KeyPair) are
// the BCL key objects; their only common base type is Object, because MLDsa, MLKem and SlhDsa derive from Object and
// not from AsymmetricAlgorithm (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.mldsa?view=net-10.0),
// so a key pair is typed object and holds an RSA or ECDsa (AsymmetricAlgorithm), an MLDsa, an MLKem or an SlhDsa, or,
// for a key pair without a private key (new KeyPair(publicKey, null) in Java), the BCL PublicKey, "a certificate's
// public key information" that has a constructor for each of these key types
// (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.publickey?view=net-10.0).
// The members below GetPublic(object) stand for the JCA accessors (KeyPair.getPublic/getPrivate, Key.getEncoded,
// X509Certificate.getSigAlgName/getSerialNumber/getBasicConstraints/getTBSCertificate/getSignature/
// getIssuerUniqueID/getSubjectUniqueID, https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/security/cert/X509Certificate.html)
// that the BCL key and certificate types do not have. The experimental BCL PQC members (SYSLIB5006, "Types for
// post-quantum cryptography (PQC) are experimental",
// https://learn.microsoft.com/en-us/dotnet/fundamentals/syslib-diagnostics/experimental-overview) are used inside
// #pragma warning disable SYSLIB5006 regions: pkitesting builds ML-DSA, ML-KEM and SLH-DSA keys and certificates as the
// Java module does, and the BCL offers them only through these members.
internal static class Algorithms
{
    // CLR: the Java standard names of the signature algorithms that oidForAlgorithmName knows, in its order, so that
    // GetSigAlgName can map a certificate's signature algorithm OID back to the name.
    private static readonly string[] SIGNATURE_ALGORITHM_NAMES =
    {
        "SHA256withECDSA", "SHA384withECDSA", "SHA256withRSA", "SHA384withRSA", "Ed25519", "Ed448",
        "ML-DSA-44", "ML-DSA-65", "ML-DSA-87",
        "SLH-DSA-SHA2-128s", "SLH-DSA-SHA2-128f", "SLH-DSA-SHAKE-128s", "SLH-DSA-SHAKE-128f",
        "SLH-DSA-SHA2-192s", "SLH-DSA-SHA2-192f", "SLH-DSA-SHAKE-192s", "SLH-DSA-SHAKE-192f",
        "SLH-DSA-SHA2-256s", "SLH-DSA-SHA2-256f", "SLH-DSA-SHAKE-256s", "SLH-DSA-SHAKE-256f",
    };

    internal static string OidForAlgorithmName(string algorithmIdentifier)
    {
        // See the Java Security Standard Algorithm Names documentation for names and links to RFCs.
        // https://docs.oracle.com/en/java/javase/22/docs/specs/security/standard-names.html#signature-algorithms
        // CLR: toLowerCase(Locale.ROOT) is ToLowerInvariant(); UnsupportedOperationException is
        // NotSupportedException.
        switch (algorithmIdentifier.ToLowerInvariant())
        {
            case "sha256withecdsa":
                return "1.2.840.10045.4.3.2";
            case "sha384withecdsa":
                return "1.2.840.10045.4.3.3";
            case "sha256withrsa":
                return "1.2.840.113549.1.1.11";
            case "sha384withrsa":
                return "1.2.840.113549.1.1.12";
            case "ed25519":
                return "1.3.101.112";
            case "ed448":
                return "1.3.101.113";
            case "ml-dsa-44":
                return "2.16.840.1.101.3.4.3.17";
            case "ml-dsa-65":
                return "2.16.840.1.101.3.4.3.18";
            case "ml-dsa-87":
                return "2.16.840.1.101.3.4.3.19";
            case "slh-dsa-sha2-128s":
                return "2.16.840.1.101.3.4.3.20";
            case "slh-dsa-sha2-128f":
                return "2.16.840.1.101.3.4.3.21";
            case "slh-dsa-shake-128s":
                return "2.16.840.1.101.3.4.3.22";
            case "slh-dsa-shake-128f":
                return "2.16.840.1.101.3.4.3.23";
            case "slh-dsa-sha2-192s":
                return "2.16.840.1.101.3.4.3.24";
            case "slh-dsa-sha2-192f":
                return "2.16.840.1.101.3.4.3.25";
            case "slh-dsa-shake-192s":
                return "2.16.840.1.101.3.4.3.26";
            case "slh-dsa-shake-192f":
                return "2.16.840.1.101.3.4.3.27";
            case "slh-dsa-sha2-256s":
                return "2.16.840.1.101.3.4.3.28";
            case "slh-dsa-sha2-256f":
                return "2.16.840.1.101.3.4.3.29";
            case "slh-dsa-shake-256s":
                return "2.16.840.1.101.3.4.3.30";
            case "slh-dsa-shake-256f":
                return "2.16.840.1.101.3.4.3.31";
            default:
                throw new NotSupportedException("Algorithm not supported: " + algorithmIdentifier);
        }
    }

    // CLR: keyPairGenerator(keyType, spec, rng, provider) returns a JCA KeyPairGenerator initialized with the spec and the
    // SecureRandom, looked up in the installed providers or in Bouncy Castle. The BCL key generator of each key type is
    // a static factory that draws from the operating system's random number generator and takes no provider, so the
    // generator is a delegate over that factory and the spec is the BCL parameter object (an ECCurve, the RSA key size,
    // or an MLDsaAlgorithm/MLKemAlgorithm/SlhDsaAlgorithm, see CertificateBuilder.Algorithm). NoSuchAlgorithmException
    // (a GeneralSecurityException) is CryptographicException, the BCL's cryptographic error.
    internal static Func<object> KeyPairGenerator(string keyType, object spec)
    {
        switch (keyType)
        {
            case "EC":
                ECCurve curve = (ECCurve) spec;
                return () => ECDsa.Create(curve);
            case "RSA":
                int keySize = (int) spec;
                return () => RSA.Create(keySize);
            case "ML-DSA":
                MLDsaAlgorithm mlDsa = (MLDsaAlgorithm) spec;
                return () => MLDsa.GenerateKey(mlDsa);
            case "ML-KEM":
                MLKemAlgorithm mlKem = (MLKemAlgorithm) spec;
                return () => MLKem.GenerateKey(mlKem);
#pragma warning disable SYSLIB5006 // SlhDsa is experimental; see the class comment.
            case "SLH-DSA":
                SlhDsaAlgorithm slhDsa = (SlhDsaAlgorithm) spec;
                return () => SlhDsa.GenerateKey(slhDsa);
#pragma warning restore SYSLIB5006
            default:
                throw new CryptographicException(keyType + " KeyPairGenerator not available");
        }
    }

    // CLR: signature(algorithmIdentifier, provider) returns a JCA Signature engine that the caller initializes with a
    // key, updates with the data and asks to sign or verify. The BCL has no signature engine type: each key type signs
    // with its own SignData, so the engine's sign path is Sign and its verify path Verify. The Java names keep their JCA
    // meaning: SHA*withECDSA is ECDSA over the named digest with the DER SEQUENCE {r, s} signature of X.509
    // (DSASignatureFormat.Rfc3279DerSequence, https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.dsasignatureformat?view=net-10.0),
    // SHA*withRSA is PKCS #1 v1.5, and ML-DSA-*/SLH-DSA-* are the pure algorithms with an empty context. A key of another
    // type fails as Signature.initSign/initVerify does with an InvalidKeyException; the BCL error is CryptographicException.
    internal static byte[] Sign(string algorithmIdentifier, object privateKey, byte[] data)
    {
        string name = algorithmIdentifier.ToLowerInvariant();
        if (name.EndsWith("withecdsa", StringComparison.Ordinal) && privateKey is ECDsa ecdsa)
        {
            return ecdsa.SignData(data, SignatureHash(algorithmIdentifier), DSASignatureFormat.Rfc3279DerSequence);
        }
        if (name.EndsWith("withrsa", StringComparison.Ordinal) && privateKey is RSA rsa)
        {
            return rsa.SignData(data, SignatureHash(algorithmIdentifier), RSASignaturePadding.Pkcs1);
        }
        if (privateKey is MLDsa mlDsa && mlDsa.Algorithm.Name.Equals(algorithmIdentifier, StringComparison.OrdinalIgnoreCase))
        {
            return mlDsa.SignData(data);
        }
#pragma warning disable SYSLIB5006 // SlhDsa is experimental; see the class comment.
        if (privateKey is SlhDsa slhDsa && slhDsa.Algorithm.Name.Equals(algorithmIdentifier, StringComparison.OrdinalIgnoreCase))
        {
            return slhDsa.SignData(data);
        }
#pragma warning restore SYSLIB5006
        throw new CryptographicException("Cannot sign with " + algorithmIdentifier + " using " +
                                         (privateKey == null ? "no private key" : privateKey.GetType().Name));
    }

    // CLR: the verify path of the JCA Signature engine (initVerify(certificate), update(data), verify(signature)); see
    // Sign. The tests use it where the Java tests verify a certificate signature with a Signature engine.
    internal static bool Verify(string algorithmIdentifier, PublicKey publicKey, byte[] data, byte[] signature)
    {
        string name = algorithmIdentifier.ToLowerInvariant();
        if (name.EndsWith("withecdsa", StringComparison.Ordinal))
        {
            using ECDsa ecdsa = publicKey.GetECDsaPublicKey() ?? throw new CryptographicException("Not an EC key");
            return ecdsa.VerifyData(data, signature, SignatureHash(algorithmIdentifier),
                                    DSASignatureFormat.Rfc3279DerSequence);
        }

        if (name.EndsWith("withrsa", StringComparison.Ordinal))
        {
            using RSA rsa = publicKey.GetRSAPublicKey() ?? throw new CryptographicException("Not an RSA key");
            return rsa.VerifyData(data, signature, SignatureHash(algorithmIdentifier), RSASignaturePadding.Pkcs1);
        }
#pragma warning disable SYSLIB5006 // PublicKey.GetMLDsaPublicKey and SlhDsa are experimental; see the class comment.
        if (name.StartsWith("ml-dsa-", StringComparison.Ordinal))
        {
            using MLDsa mlDsa = publicKey.GetMLDsaPublicKey() ?? throw new CryptographicException("Not an ML-DSA key");
            return mlDsa.VerifyData(data, signature);
        }

        if (name.StartsWith("slh-dsa-", StringComparison.Ordinal))
        {
            using SlhDsa slhDsa = publicKey.GetSlhDsaPublicKey() ?? throw new CryptographicException("Not an SLH-DSA key");
            return slhDsa.VerifyData(data, signature);
        }
#pragma warning restore SYSLIB5006
        throw new CryptographicException("Algorithm not supported: " + algorithmIdentifier);
    }

    // CLR: the digest that a SHA*with* signature name names. CertificateRequest and CertificateRevocationListBuilder
    // require a named HashAlgorithmName even when the X509SignatureGenerator (Signed) chooses the digest itself, so the
    // names without a separate digest (Ed25519, Ed448, ML-DSA-*, SLH-DSA-*) give SHA-256, which Signed ignores.
    internal static HashAlgorithmName SignatureHash(string algorithmIdentifier)
    {
        string name = algorithmIdentifier.ToLowerInvariant();
        if (name.StartsWith("sha384with", StringComparison.Ordinal))
        {
            return HashAlgorithmName.SHA384;
        }
        if (name.StartsWith("sha512with", StringComparison.Ordinal))
        {
            return HashAlgorithmName.SHA512;
        }

        return HashAlgorithmName.SHA256;
    }

    // CLR: KeyPair.getPublic(): the BCL PublicKey of a key object, the PublicKey itself for a key pair without a private
    // key, or null for an object that is not a BCL key (the Java key pair "must have a public key").
    internal static PublicKey GetPublic(object keyPair)
    {
        switch (keyPair)
        {
            case PublicKey publicKey:
                return publicKey;
            case AsymmetricAlgorithm asymmetric:
                return new PublicKey(asymmetric);
#pragma warning disable SYSLIB5006 // the PQC PublicKey constructors are experimental; see the class comment.
            case MLDsa mlDsa:
                return new PublicKey(mlDsa);
            case MLKem mlKem:
                return new PublicKey(mlKem);
            case SlhDsa slhDsa:
                return new PublicKey(slhDsa);
#pragma warning restore SYSLIB5006
            default:
                return null;
        }
    }

    // CLR: KeyPair.getPrivate(): the key object itself, or null for a key pair that only holds a PublicKey.
    internal static object GetPrivate(object keyPair)
    {
        return keyPair is PublicKey ? null : keyPair;
    }

    // CLR: PrivateKey.getEncoded(), the PKCS #8 PrivateKeyInfo of the key. Java dereferences a null private key with a
    // NullPointerException; a C# type switch does not dereference null, so the same exception type is thrown for it.
    internal static byte[] GetEncoded(object privateKey)
    {
        switch (privateKey)
        {
            case null:
                throw new NullReferenceException("The key pair has no private key.");
            case AsymmetricAlgorithm asymmetric:
                return asymmetric.ExportPkcs8PrivateKey();
#pragma warning disable SYSLIB5006 // the PQC PKCS #8 exports are experimental; see the class comment.
            case MLDsa mlDsa:
                return mlDsa.ExportPkcs8PrivateKey();
            case MLKem mlKem:
                return mlKem.ExportPkcs8PrivateKey();
            case SlhDsa slhDsa:
                return slhDsa.ExportPkcs8PrivateKey();
#pragma warning restore SYSLIB5006
            default:
                throw new ArgumentException("Not a private key: " + privateKey.GetType().Name);
        }
    }

    // CLR: a KeyStore key entry (KeyStore.setKeyEntry(alias, privateKey, password, chain)) is a certificate that carries
    // its private key in the BCL, X509Certificate2.CopyWithPrivateKey for the key's type.
    internal static X509Certificate2 CopyWithPrivateKey(X509Certificate2 certificate, object privateKey)
    {
        switch (privateKey)
        {
            case RSA rsa:
                return certificate.CopyWithPrivateKey(rsa);
            case ECDsa ecdsa:
                return certificate.CopyWithPrivateKey(ecdsa);
#pragma warning disable SYSLIB5006 // the PQC CopyWithPrivateKey overloads are experimental; see the class comment.
            case MLDsa mlDsa:
                return certificate.CopyWithPrivateKey(mlDsa);
            case MLKem mlKem:
                return certificate.CopyWithPrivateKey(mlKem);
            case SlhDsa slhDsa:
                return certificate.CopyWithPrivateKey(slhDsa);
#pragma warning restore SYSLIB5006
            default:
                throw new ArgumentException("Not a private key: " + (privateKey == null ? "null" : privateKey.GetType().Name));
        }
    }

    // CLR: X509Certificate.getSigAlgName(): X509Certificate2.SignatureAlgorithm holds the OID, which is mapped back to the
    // Java standard name of oidForAlgorithmName; an unknown OID is returned as is.
    internal static string GetSigAlgName(X509Certificate2 certificate)
    {
        string oid = certificate.SignatureAlgorithm.Value;

        foreach (string name in SIGNATURE_ALGORITHM_NAMES)
        {
            if (OidForAlgorithmName(name) == oid)
            {
                return name;
            }
        }

        return oid;
    }

    // CLR: X509Certificate.getSerialNumber(): X509Certificate2.SerialNumberBytes is the big-endian two's complement content
    // of the DER INTEGER.
    internal static BigInteger GetSerialNumber(X509Certificate2 certificate)
    {
        return new BigInteger(certificate.SerialNumberBytes.Span, isUnsigned: false, isBigEndian: true);
    }

    // CLR: X509Certificate.getBasicConstraints(): -1 unless the Basic Constraints extension marks a CA, then the path
    // length constraint, or int.MaxValue when it has none.
    internal static int GetBasicConstraints(X509Certificate2 certificate)
    {
        foreach (X509Extension extension in certificate.Extensions)
        {
            if (extension is X509BasicConstraintsExtension basicConstraints)
            {
                if (!basicConstraints.CertificateAuthority)
                {
                    return -1;
                }

                return basicConstraints.HasPathLengthConstraint ? basicConstraints.PathLengthConstraint : int.MaxValue;
            }
        }

        return -1;
    }

    // CLR: X509Certificate.getTBSCertificate(): the DER tbsCertificate, the first element of the Certificate SEQUENCE
    // (RFC 5280 4.1, https://www.rfc-editor.org/rfc/rfc5280).
    internal static byte[] GetTBSCertificate(X509Certificate2 certificate)
    {
        AsnReader certificateSequence = new AsnReader(certificate.RawData, AsnEncodingRules.DER).ReadSequence();
        return certificateSequence.ReadEncodedValue().ToArray();
    }

    // CLR: X509Certificate.getSignature(): the signatureValue BIT STRING, the third element of the Certificate SEQUENCE.
    internal static byte[] GetSignature(X509Certificate2 certificate)
    {
        AsnReader certificateSequence = new AsnReader(certificate.RawData, AsnEncodingRules.DER).ReadSequence();
        certificateSequence.ReadEncodedValue();
        certificateSequence.ReadEncodedValue();

        return certificateSequence.ReadBitString(out _);
    }

    // CLR: X509Certificate.getIssuerUniqueID()/getSubjectUniqueID(): the bits of the [1]/[2] IMPLICIT BIT STRING after
    // the subjectPublicKeyInfo of the tbsCertificate, or null when absent (RFC 5280 4.1.2.8); the unused-bit count is
    // returned after the content bytes, so equal values give equal arrays as the boolean[] of Java do.
    internal static byte[] GetUniqueId(X509Certificate2 certificate, int tagNumber)
    {
        AsnReader tbs = new AsnReader(GetTBSCertificate(certificate), AsnEncodingRules.DER).ReadSequence();
        if (tbs.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0, true)))
        {
            tbs.ReadEncodedValue(); // version
        }

        for (int i = 0; i < 6; i++)
        {
            tbs.ReadEncodedValue(); // serialNumber, signature, issuer, validity, subject, subjectPublicKeyInfo
        }

        Asn1Tag uniqueIdTag = new Asn1Tag(TagClass.ContextSpecific, tagNumber);

        while (tbs.HasData)
        {
            if (tbs.PeekTag().HasSameClassAndValue(uniqueIdTag))
            {
                byte[] bits = tbs.ReadBitString(out int unusedBitCount, uniqueIdTag);
                byte[] value = new byte[bits.Length + 1];
                bits.CopyTo(value, 0);
                value[bits.Length] = (byte) unusedBitCount;
                return value;
            }

            tbs.ReadEncodedValue();
        }

        return null;
    }
}
