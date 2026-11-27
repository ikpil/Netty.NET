/*
 * Copyright 2026 The Netty Project
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
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Netty.NET.Handler.Ssl.Ocsp;

// CLR-only: the Bouncy Castle OCSP types that handler-ssl-ocsp uses (org.bouncycastle.cert.ocsp CertificateID,
// OCSPReqBuilder, OCSPResp, BasicOCSPResp, SingleResp, RevokedStatus, UnknownStatus and org.bouncycastle.asn1.x509
// Extension) have no BCL counterpart, so this file implements the parts the module needs on the BCL's ASN.1 reader and
// writer (System.Formats.Asn1 AsnReader/AsnWriter, "Provides a stateful, forward-only reader/writer for BER-, CER-, or
// DER-encoded ASN.1 data", https://learn.microsoft.com/en-us/dotnet/api/system.formats.asn1?view=net-10.0), as
// pkitesting replaces the Bouncy Castle certificate building (docs/pkitesting-porting.md). The structures are those of
// RFC 6960 section 4 (https://www.rfc-editor.org/rfc/rfc6960.txt), whose ASN.1 module uses EXPLICIT tags except for
// CertStatus ("good [0] IMPLICIT NULL, revoked [1] IMPLICIT RevokedInfo, unknown [2] IMPLICIT UnknownInfo"). The
// types are internal: Bouncy Castle types appear only in package-private signatures of the Java module. OCSPException,
// a checked Bouncy Castle exception, is CryptographicException, as pkitesting and handler map the JCA's
// CertificateException (docs/pkitesting-porting.md); an encoding that cannot be decoded is an IOException
// ("malformed response") when the OCSPResponse is read, as OCSPResp(byte[]) throws it, and a CryptographicException
// when the BasicOCSPResponse is read (getResponseObject throws OCSPException).
internal static class OcspObjectIdentifiers
{
    // id-pkix-ocsp-nonce, OCSPObjectIdentifiers.id_pkix_ocsp_nonce (RFC 6960 section 4.4.1).
    internal const string IdPkixOcspNonce = "1.3.6.1.5.5.7.48.1.2";

    // id-pkix-ocsp-basic, OCSPObjectIdentifiers.id_pkix_ocsp_basic (RFC 6960 section 4.2.1).
    internal const string IdPkixOcspBasic = "1.3.6.1.5.5.7.48.1.1";

    // id-sha1 (OIW), the AlgorithmIdentifier of CertificateID.HASH_SHA1.
    internal const string IdSha1 = "1.3.14.3.2.26";

    internal const string IdSha256 = "2.16.840.1.101.3.4.2.1";
    internal const string IdSha384 = "2.16.840.1.101.3.4.2.2";
    internal const string IdSha512 = "2.16.840.1.101.3.4.2.3";
}

// CLR-only: org.bouncycastle.asn1.x509.Extension: extnID, critical (DEFAULT FALSE, so written only when true) and the
// contents of the extnValue OCTET STRING.
internal sealed class OcspExtension
{
    internal OcspExtension(string extnId, bool critical, byte[] extnValue)
    {
        ExtnId = extnId;
        Critical = critical;
        ExtnValue = extnValue;
    }

    internal string ExtnId { get; }

    internal bool Critical { get; }

    internal byte[] ExtnValue { get; }

    internal void Write(AsnWriter writer)
    {
        writer.PushSequence();
        writer.WriteObjectIdentifier(ExtnId);
        if (Critical)
        {
            writer.WriteBoolean(true);
        }
        writer.WriteOctetString(ExtnValue);
        writer.PopSequence();
    }

    internal static OcspExtension Read(AsnReader reader)
    {
        AsnReader sequence = reader.ReadSequence();
        string extnId = sequence.ReadObjectIdentifier();
        bool critical = false;
        if (sequence.PeekTag().HasSameClassAndValue(Asn1Tag.Boolean))
        {
            critical = sequence.ReadBoolean();
        }
        byte[] extnValue = sequence.ReadOctetString();
        sequence.ThrowIfNotEmpty();
        return new OcspExtension(extnId, critical, extnValue);
    }

    internal static void WriteAll(AsnWriter writer, IReadOnlyList<OcspExtension> extensions)
    {
        writer.PushSequence();
        foreach (OcspExtension extension in extensions)
        {
            extension.Write(writer);
        }
        writer.PopSequence();
    }

    internal static List<OcspExtension> ReadAll(AsnReader reader)
    {
        AsnReader sequence = reader.ReadSequence();
        List<OcspExtension> extensions = new List<OcspExtension>();
        while (sequence.HasData)
        {
            extensions.Add(Read(sequence));
        }
        return extensions;
    }
}

// CLR-only: org.bouncycastle.cert.ocsp.CertificateID, the CertID of RFC 6960 section 4.1.1: "hashAlgorithm
// AlgorithmIdentifier, issuerNameHash OCTET STRING, -- Hash of issuer's DN; issuerKeyHash OCTET STRING, -- Hash of
// issuer's public key; serialNumber CertificateSerialNumber". The issuer name hash covers the DER encoding of the
// issuer's subject name and the key hash "the value of the BIT STRING field subjectPublicKey (excluding the tag, length,
// and number of unused bits)". HASH_SHA1 writes the algorithm with a NULL parameter, as Bouncy Castle's
// AlgorithmIdentifier(OIWObjectIdentifiers.idSHA1, DERNull.INSTANCE) does.
internal sealed class CertificateId
{
    internal CertificateId(string hashAlgorithm, byte[] issuerNameHash, byte[] issuerKeyHash, BigInteger serialNumber)
    {
        HashAlgorithm = hashAlgorithm;
        IssuerNameHash = issuerNameHash;
        IssuerKeyHash = issuerKeyHash;
        SerialNumber = serialNumber;
    }

    internal string HashAlgorithm { get; }

    internal byte[] IssuerNameHash { get; }

    internal byte[] IssuerKeyHash { get; }

    internal BigInteger SerialNumber { get; }

    // CLR: new CertificateID(digestCalculatorProvider.get(HASH_SHA1), new JcaX509CertificateHolder(issuer), serial).
    internal static CertificateId Create(string hashAlgorithm, X509Certificate2 issuer, BigInteger serialNumber)
    {
        return new CertificateId(hashAlgorithm, Hash(hashAlgorithm, issuer.SubjectName.RawData),
                Hash(hashAlgorithm, SubjectPublicKey(issuer)), serialNumber);
    }

    // CLR: X509Certificate.getSerialNumber(), a BigInteger of the DER INTEGER; SerialNumberBytes is "the serial number
    // of the certificate as a big-endian byte array"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificate2.serialnumberbytes?view=net-10.0).
    internal static BigInteger SerialNumberOf(X509Certificate2 certificate)
    {
        return new BigInteger(certificate.SerialNumberBytes.Span, false, true);
    }

    // CLR: CertificateID.matchesIssuer(issuerHolder, digestCalculatorProvider): the issuer hashes computed with this
    // ID's hash algorithm equal this ID's.
    internal bool MatchesIssuer(X509Certificate2 issuer)
    {
        CertificateId other = Create(HashAlgorithm, issuer, SerialNumber);
        return IssuerNameHash.AsSpan().SequenceEqual(other.IssuerNameHash)
               && IssuerKeyHash.AsSpan().SequenceEqual(other.IssuerKeyHash);
    }

    internal void Write(AsnWriter writer)
    {
        writer.PushSequence();
        writer.PushSequence();
        writer.WriteObjectIdentifier(HashAlgorithm);
        writer.WriteNull();
        writer.PopSequence();
        writer.WriteOctetString(IssuerNameHash);
        writer.WriteOctetString(IssuerKeyHash);
        writer.WriteInteger(SerialNumber);
        writer.PopSequence();
    }

    internal static CertificateId Read(AsnReader reader)
    {
        AsnReader sequence = reader.ReadSequence();
        AsnReader algorithm = sequence.ReadSequence();
        string hashAlgorithm = algorithm.ReadObjectIdentifier();
        if (algorithm.HasData)
        {
            algorithm.ReadEncodedValue();
        }
        algorithm.ThrowIfNotEmpty();
        byte[] issuerNameHash = sequence.ReadOctetString();
        byte[] issuerKeyHash = sequence.ReadOctetString();
        BigInteger serialNumber = sequence.ReadInteger();
        sequence.ThrowIfNotEmpty();
        return new CertificateId(hashAlgorithm, issuerNameHash, issuerKeyHash, serialNumber);
    }

    private static byte[] SubjectPublicKey(X509Certificate2 certificate)
    {
        AsnReader spki = new AsnReader(certificate.PublicKey.ExportSubjectPublicKeyInfo(), AsnEncodingRules.DER)
                .ReadSequence();
        spki.ReadSequence();
        return spki.ReadBitString(out _);
    }

    // CLR: the DigestCalculator of the hash algorithm; an algorithm the BCL has no hash for is the OCSPException Bouncy
    // Castle throws for it ("problem creating ID").
    private static byte[] Hash(string hashAlgorithm, byte[] data)
    {
        switch (hashAlgorithm)
        {
            case OcspObjectIdentifiers.IdSha1:
                return SHA1.HashData(data);
            case OcspObjectIdentifiers.IdSha256:
                return SHA256.HashData(data);
            case OcspObjectIdentifiers.IdSha384:
                return SHA384.HashData(data);
            case OcspObjectIdentifiers.IdSha512:
                return SHA512.HashData(data);
            default:
                throw new CryptographicException("problem creating ID: unsupported hash algorithm " + hashAlgorithm);
        }
    }
}

// CLR-only: org.bouncycastle.cert.ocsp.OCSPReqBuilder for an unsigned request (RFC 6960 section 4.1.1): "OCSPRequest
// ::= SEQUENCE { tbsRequest TBSRequest, optionalSignature [0] EXPLICIT Signature OPTIONAL }", "TBSRequest ::= SEQUENCE {
// version [0] EXPLICIT Version DEFAULT v1, requestorName [1] EXPLICIT GeneralName OPTIONAL, requestList SEQUENCE OF
// Request, requestExtensions [2] EXPLICIT Extensions OPTIONAL }", "Request ::= SEQUENCE { reqCert CertID,
// singleRequestExtensions [0] EXPLICIT Extensions OPTIONAL }". build().getEncoded() is Build(): DER omits the default
// version, as Bouncy Castle does.
internal sealed class OcspReqBuilder
{
    private readonly List<CertificateId> requests = new List<CertificateId>();
    private IReadOnlyList<OcspExtension> requestExtensions;

    internal OcspReqBuilder AddRequest(CertificateId certificateId)
    {
        requests.Add(certificateId);
        return this;
    }

    internal OcspReqBuilder SetRequestExtensions(IReadOnlyList<OcspExtension> extensions)
    {
        requestExtensions = extensions;
        return this;
    }

    internal byte[] Build()
    {
        AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.PushSequence();
        writer.PushSequence();
        foreach (CertificateId request in requests)
        {
            writer.PushSequence();
            request.Write(writer);
            writer.PopSequence();
        }
        writer.PopSequence();
        if (requestExtensions != null)
        {
            Asn1Tag tag = new Asn1Tag(TagClass.ContextSpecific, 2, true);
            writer.PushSequence(tag);
            OcspExtension.WriteAll(writer, requestExtensions);
            writer.PopSequence(tag);
        }
        writer.PopSequence();
        writer.PopSequence();
        return writer.Encode();
    }
}

// CLR-only: org.bouncycastle.cert.ocsp.OCSPResp (RFC 6960 section 4.2.1): "OCSPResponse ::= SEQUENCE { responseStatus
// OCSPResponseStatus, responseBytes [0] EXPLICIT ResponseBytes OPTIONAL }", "ResponseBytes ::= SEQUENCE { responseType
// OBJECT IDENTIFIER, response OCTET STRING }". GetResponseObject() is getResponseObject(): null without responseBytes,
// a BasicOcspResp for id-pkix-ocsp-basic, and otherwise the undecoded response (Bouncy Castle returns the ASN.1
// primitive of the OCTET STRING), here its bytes.
internal sealed class OcspResp
{
    private readonly string responseType;
    private readonly byte[] response;

    // CLR: new OCSPResp(byte[]), which throws IOException ("malformed response: ...") for an encoding it cannot read.
    internal OcspResp(byte[] encoded)
    {
        try
        {
            AsnReader reader = new AsnReader(encoded, AsnEncodingRules.DER);
            AsnReader sequence = reader.ReadSequence();
            reader.ThrowIfNotEmpty();
            ReadOnlyMemory<byte> status = sequence.ReadEnumeratedBytes();
            Status = (int) new BigInteger(status.Span, false, true);
            if (sequence.HasData)
            {
                AsnReader explicitTag = sequence.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true));
                AsnReader responseBytes = explicitTag.ReadSequence();
                explicitTag.ThrowIfNotEmpty();
                responseType = responseBytes.ReadObjectIdentifier();
                response = responseBytes.ReadOctetString();
                responseBytes.ThrowIfNotEmpty();
            }
            sequence.ThrowIfNotEmpty();
        }
        catch (AsnContentException e)
        {
            throw new IOException("malformed response: " + e.Message, e);
        }
    }

    // CLR: getStatus(), the OCSPResponseStatus (successful (0), malformedRequest (1), internalError (2), tryLater (3),
    // sigRequired (5), unauthorized (6)).
    internal int Status { get; }

    internal object GetResponseObject()
    {
        if (responseType == null)
        {
            return null;
        }
        if (responseType == OcspObjectIdentifiers.IdPkixOcspBasic)
        {
            return new BasicOcspResp(response);
        }
        return response;
    }
}

// CLR-only: org.bouncycastle.cert.ocsp.BasicOCSPResp (RFC 6960 section 4.2.1): "BasicOCSPResponse ::= SEQUENCE {
// tbsResponseData ResponseData, signatureAlgorithm AlgorithmIdentifier, signature BIT STRING, certs [0] EXPLICIT
// SEQUENCE OF Certificate OPTIONAL }", "ResponseData ::= SEQUENCE { version [0] EXPLICIT Version DEFAULT v1, responderID
// ResponderID, producedAt GeneralizedTime, responses SEQUENCE OF SingleResponse, responseExtensions [1] EXPLICIT
// Extensions OPTIONAL }", "ResponderID ::= CHOICE { byName [1] Name, byKey [2] KeyHash }". The signature is checked over
// the DER encoding of tbsResponseData as it was received.
internal sealed class BasicOcspResp
{
    private readonly byte[] encoded;
    private readonly byte[] tbsResponseData;
    private readonly string signatureAlgorithm;
    private readonly byte[] signature;
    private readonly byte[][] certs;
    private readonly SingleResp[] responses;
    private readonly List<OcspExtension> responseExtensions;

    // CLR: getResponseObject() throws OCSPException ("problem decoding object: ...") when the BasicOCSPResponse cannot be
    // decoded.
    internal BasicOcspResp(byte[] encoded)
    {
        this.encoded = encoded;
        try
        {
            AsnReader reader = new AsnReader(encoded, AsnEncodingRules.DER);
            AsnReader sequence = reader.ReadSequence();
            reader.ThrowIfNotEmpty();
            tbsResponseData = sequence.ReadEncodedValue().ToArray();
            AsnReader algorithm = sequence.ReadSequence();
            signatureAlgorithm = algorithm.ReadObjectIdentifier();
            signature = sequence.ReadBitString(out _);
            List<byte[]> certificates = new List<byte[]>();
            if (sequence.HasData)
            {
                AsnReader explicitTag = sequence.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true));
                AsnReader certSequence = explicitTag.ReadSequence();
                explicitTag.ThrowIfNotEmpty();
                while (certSequence.HasData)
                {
                    certificates.Add(certSequence.ReadEncodedValue().ToArray());
                }
            }
            sequence.ThrowIfNotEmpty();
            certs = certificates.ToArray();

            AsnReader data = new AsnReader(tbsResponseData, AsnEncodingRules.DER).ReadSequence();
            Asn1Tag versionTag = new Asn1Tag(TagClass.ContextSpecific, 0, true);
            if (data.PeekTag().HasSameClassAndValue(versionTag))
            {
                data.ReadSequence(versionTag);
            }
            Asn1Tag responderId = data.PeekTag();
            if (responderId.TagClass != TagClass.ContextSpecific || responderId.TagValue is not (1 or 2))
            {
                throw new AsnContentException("Unexpected ResponderID tag: " + responderId);
            }
            data.ReadEncodedValue();
            data.ReadGeneralizedTime();
            AsnReader responseSequence = data.ReadSequence();
            List<SingleResp> singleResponses = new List<SingleResp>();
            while (responseSequence.HasData)
            {
                singleResponses.Add(SingleResp.Read(responseSequence));
            }
            responses = singleResponses.ToArray();
            Asn1Tag extensionsTag = new Asn1Tag(TagClass.ContextSpecific, 1, true);
            responseExtensions = new List<OcspExtension>();
            if (data.HasData && data.PeekTag().HasSameClassAndValue(extensionsTag))
            {
                AsnReader explicitTag = data.ReadSequence(extensionsTag);
                responseExtensions = OcspExtension.ReadAll(explicitTag);
                explicitTag.ThrowIfNotEmpty();
            }
            data.ThrowIfNotEmpty();
        }
        catch (AsnContentException e)
        {
            throw new CryptographicException("problem decoding object: " + e.Message, e);
        }
    }

    internal SingleResp[] GetResponses()
    {
        return (SingleResp[]) responses.Clone();
    }

    // CLR: getEncoded(), the BasicOCSPResponse as it was decoded.
    internal byte[] GetEncoded()
    {
        return (byte[]) encoded.Clone();
    }

    // CLR: getCerts(), the X509CertificateHolder of each certificate of the response (an empty array without certs).
    // A holder is the DER encoding; the JcaX509CertificateConverter step of the caller loads it.
    internal byte[][] GetCerts()
    {
        return (byte[][]) certs.Clone();
    }

    // CLR: getExtension(oid), null when the response has no such extension.
    internal OcspExtension GetExtension(string oid)
    {
        foreach (OcspExtension extension in responseExtensions)
        {
            if (extension.ExtnId == oid)
            {
                return extension;
            }
        }
        return null;
    }

    // CLR: isSignatureValid(new JcaContentVerifierProviderBuilder().build(certificate)): the signature algorithms of the
    // BCL's RSA (PKCS #1 v1.5) and ECDSA (DER-encoded "Ecdsa-Sig-Value", DSASignatureFormat.Rfc3279DerSequence) keys.
    // An algorithm or key the BCL cannot verify with is the OperatorCreationException of the verifier provider, which
    // OcspClient.validateSignature turns into "Error validating OCSP-Signature"; here it is a NotSupportedException.
    // RSASSA-PSS and EdDSA signatures are not supported (pkitesting records EdDSA as unsupported, see
    // docs/pkitesting-porting.md).
    internal bool IsSignatureValid(X509Certificate2 certificate)
    {
        switch (signatureAlgorithm)
        {
            case "1.2.840.113549.1.1.5":
                return VerifyRsa(certificate, HashAlgorithmName.SHA1);
            case "1.2.840.113549.1.1.11":
                return VerifyRsa(certificate, HashAlgorithmName.SHA256);
            case "1.2.840.113549.1.1.12":
                return VerifyRsa(certificate, HashAlgorithmName.SHA384);
            case "1.2.840.113549.1.1.13":
                return VerifyRsa(certificate, HashAlgorithmName.SHA512);
            case "1.2.840.10045.4.1":
                return VerifyEcdsa(certificate, HashAlgorithmName.SHA1);
            case "1.2.840.10045.4.3.2":
                return VerifyEcdsa(certificate, HashAlgorithmName.SHA256);
            case "1.2.840.10045.4.3.3":
                return VerifyEcdsa(certificate, HashAlgorithmName.SHA384);
            case "1.2.840.10045.4.3.4":
                return VerifyEcdsa(certificate, HashAlgorithmName.SHA512);
            default:
                throw new NotSupportedException("Unsupported OCSP signature algorithm: " + signatureAlgorithm);
        }
    }

    private bool VerifyRsa(X509Certificate2 certificate, HashAlgorithmName hash)
    {
        using RSA key = certificate.GetRSAPublicKey()
                ?? throw new NotSupportedException("The certificate has no RSA key for " + signatureAlgorithm);
        return key.VerifyData(tbsResponseData, signature, hash, RSASignaturePadding.Pkcs1);
    }

    private bool VerifyEcdsa(X509Certificate2 certificate, HashAlgorithmName hash)
    {
        using ECDsa key = certificate.GetECDsaPublicKey()
                ?? throw new NotSupportedException("The certificate has no EC key for " + signatureAlgorithm);
        return key.VerifyData(tbsResponseData, signature, hash, DSASignatureFormat.Rfc3279DerSequence);
    }
}

// CLR-only: org.bouncycastle.cert.ocsp.SingleResp (RFC 6960 section 4.2.1): "SingleResponse ::= SEQUENCE { certID
// CertID, certStatus CertStatus, thisUpdate GeneralizedTime, nextUpdate [0] EXPLICIT GeneralizedTime OPTIONAL,
// singleExtensions [1] EXPLICIT Extensions OPTIONAL }". GetCertStatus() is getCertStatus(): null for good (Bouncy
// Castle's CertificateStatus.GOOD is null), a RevokedStatus or an UnknownStatus. java.util.Date is DateTimeOffset
// (docs/codec-http-porting.md).
internal sealed class SingleResp
{
    private SingleResp(CertificateId certId, object certStatus, DateTimeOffset thisUpdate, DateTimeOffset? nextUpdate)
    {
        CertId = certId;
        CertStatus = certStatus;
        ThisUpdate = thisUpdate;
        NextUpdate = nextUpdate;
    }

    internal CertificateId CertId { get; }

    internal object CertStatus { get; }

    internal DateTimeOffset ThisUpdate { get; }

    internal DateTimeOffset? NextUpdate { get; }

    internal CertificateId GetCertID() => CertId;

    internal object GetCertStatus() => CertStatus;

    internal DateTimeOffset GetThisUpdate() => ThisUpdate;

    internal DateTimeOffset? GetNextUpdate() => NextUpdate;

    internal static SingleResp Read(AsnReader reader)
    {
        AsnReader sequence = reader.ReadSequence();
        CertificateId certId = CertificateId.Read(sequence);
        object certStatus;
        Asn1Tag statusTag = sequence.PeekTag();
        if (statusTag.TagClass != TagClass.ContextSpecific)
        {
            throw new AsnContentException("Unexpected CertStatus tag: " + statusTag);
        }
        switch (statusTag.TagValue)
        {
            case 0:
                sequence.ReadNull(new Asn1Tag(TagClass.ContextSpecific, 0));
                certStatus = null;
                break;
            case 1:
                AsnReader revokedInfo = sequence.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 1, true));
                DateTimeOffset revocationTime = revokedInfo.ReadGeneralizedTime();
                int? reason = null;
                if (revokedInfo.HasData)
                {
                    AsnReader explicitTag = revokedInfo.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true));
                    reason = (int) new BigInteger(explicitTag.ReadEnumeratedBytes().Span, false, true);
                    explicitTag.ThrowIfNotEmpty();
                }
                revokedInfo.ThrowIfNotEmpty();
                certStatus = new RevokedStatus(revocationTime, reason);
                break;
            case 2:
                sequence.ReadNull(new Asn1Tag(TagClass.ContextSpecific, 2));
                certStatus = new UnknownStatus();
                break;
            default:
                throw new AsnContentException("Unexpected CertStatus tag: " + statusTag);
        }
        DateTimeOffset thisUpdate = sequence.ReadGeneralizedTime();
        DateTimeOffset? nextUpdate = null;
        Asn1Tag nextUpdateTag = new Asn1Tag(TagClass.ContextSpecific, 0, true);
        if (sequence.HasData && sequence.PeekTag().HasSameClassAndValue(nextUpdateTag))
        {
            AsnReader explicitTag = sequence.ReadSequence(nextUpdateTag);
            nextUpdate = explicitTag.ReadGeneralizedTime();
            explicitTag.ThrowIfNotEmpty();
        }
        Asn1Tag extensionsTag = new Asn1Tag(TagClass.ContextSpecific, 1, true);
        if (sequence.HasData && sequence.PeekTag().HasSameClassAndValue(extensionsTag))
        {
            sequence.ReadEncodedValue();
        }
        sequence.ThrowIfNotEmpty();
        return new SingleResp(certId, certStatus, thisUpdate, nextUpdate);
    }
}

// CLR-only: org.bouncycastle.cert.ocsp.RevokedStatus (RFC 6960: "RevokedInfo ::= SEQUENCE { revocationTime
// GeneralizedTime, revocationReason [0] EXPLICIT CRLReason OPTIONAL }").
internal sealed class RevokedStatus
{
    internal RevokedStatus(DateTimeOffset revocationTime, int? revocationReason)
    {
        RevocationTime = revocationTime;
        RevocationReason = revocationReason;
    }

    internal DateTimeOffset RevocationTime { get; }

    internal int? RevocationReason { get; }
}

// CLR-only: org.bouncycastle.cert.ocsp.UnknownStatus (RFC 6960: "UnknownInfo ::= NULL").
internal sealed class UnknownStatus
{
}
