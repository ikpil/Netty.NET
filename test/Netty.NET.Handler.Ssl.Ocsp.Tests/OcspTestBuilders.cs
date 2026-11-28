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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Netty.NET.Handler.Ssl.Ocsp.Tests;

// CLR-only test support: the Java tests build OCSP responses with Bouncy Castle (BasicOCSPRespBuilder, RespID,
// CertificateStatus, RevokedStatus, OCSPRespBuilder, OCSPResponse/ResponseBytes, AuthorityInformationAccess and
// JcaContentSignerBuilder). These builders write the same RFC 6960 structures with the BCL's AsnWriter, the
// counterpart of the module's CLR-only OcspAsn1.cs, and the AIA extension with the BCL's
// X509AuthorityInformationAccessExtension.

// CLR-only: OCSPRespBuilder/OCSPResponseStatus constants (RFC 6960: "successful (0) ... unauthorized (6)").
internal enum OcspResponseStatus
{
    Successful = 0,
    Unauthorized = 6,
}

// CLR-only: CertificateStatus.GOOD, which Bouncy Castle represents as null (SingleResp.getCertStatus() returns null for
// a good certificate).
internal static class CertificateStatus
{
    internal static readonly object GOOD = null;
}

// CLR-only: RespID(X500Name), the byName ResponderID.
internal sealed class RespId
{
    internal RespId(X500DistinguishedName name)
    {
        Name = name;
    }

    internal X500DistinguishedName Name { get; }
}

// CLR-only: JcaContentSignerBuilder(algorithm).build(privateKey) for the two algorithms the tests use.
internal sealed class ContentSigner
{
    private readonly AsymmetricAlgorithm key;
    private readonly string algorithm;

    internal ContentSigner(string algorithm, AsymmetricAlgorithm key)
    {
        if (algorithm != "SHA256withRSA" && algorithm != "SHA256withECDSA")
        {
            throw new NotSupportedException(algorithm);
        }

        this.algorithm = algorithm;
        this.key = key;
    }

    internal void WriteAlgorithmIdentifier(AsnWriter writer)
    {
        writer.PushSequence();

        if (algorithm == "SHA256withRSA")
        {
            writer.WriteObjectIdentifier("1.2.840.113549.1.1.11");
            writer.WriteNull();
        }
        else
        {
            writer.WriteObjectIdentifier("1.2.840.10045.4.3.2");
        }

        writer.PopSequence();
    }

    internal byte[] Sign(byte[] data)
    {
        return algorithm == "SHA256withRSA"
                ? ((RSA) key).SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                : ((ECDsa) key).SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    }
}

// CLR-only: BasicOCSPRespBuilder(RespID) with addResponse(certId, status[, thisUpdate, nextUpdate]) and
// build(signer, chain, producedAt). A null status is CertificateStatus.GOOD; addResponse(certId, status) uses the
// current time as thisUpdate and no nextUpdate, as Bouncy Castle does.
internal sealed class BasicOcspRespBuilder
{
    private readonly RespId respId;
    private readonly List<(CertificateId Id, object Status, DateTimeOffset ThisUpdate, DateTimeOffset? NextUpdate)>
            responses = new List<(CertificateId, object, DateTimeOffset, DateTimeOffset?)>();

    internal BasicOcspRespBuilder(RespId respId)
    {
        this.respId = respId;
    }

    internal BasicOcspRespBuilder AddResponse(CertificateId certId, object status)
    {
        return AddResponse(certId, status, DateTimeOffset.UtcNow, null);
    }

    internal BasicOcspRespBuilder AddResponse(CertificateId certId, object status, DateTimeOffset thisUpdate,
                                              DateTimeOffset? nextUpdate)
    {
        responses.Add((certId, status, thisUpdate, nextUpdate));
        return this;
    }

    internal BasicOcspResp Build(ContentSigner signer, X509Certificate2[] chain, DateTimeOffset producedAt)
    {
        AsnWriter tbs = new AsnWriter(AsnEncodingRules.DER);
        tbs.PushSequence();
        Asn1Tag byName = new Asn1Tag(TagClass.ContextSpecific, 1, true);
        tbs.PushSequence(byName);
        tbs.WriteEncodedValue(respId.Name.RawData);
        tbs.PopSequence(byName);
        tbs.WriteGeneralizedTime(producedAt, true);
        tbs.PushSequence();

        foreach ((CertificateId id, object status, DateTimeOffset thisUpdate, DateTimeOffset? nextUpdate) in responses)
        {
            tbs.PushSequence();
            id.Write(tbs);

            if (status == null)
            {
                tbs.WriteNull(new Asn1Tag(TagClass.ContextSpecific, 0));
            }
            else if (status is RevokedStatus revoked)
            {
                Asn1Tag revokedTag = new Asn1Tag(TagClass.ContextSpecific, 1, true);
                tbs.PushSequence(revokedTag);
                tbs.WriteGeneralizedTime(revoked.RevocationTime, true);
                tbs.PopSequence(revokedTag);
            }
            else
            {
                tbs.WriteNull(new Asn1Tag(TagClass.ContextSpecific, 2));
            }

            tbs.WriteGeneralizedTime(thisUpdate, true);

            if (nextUpdate != null)
            {
                Asn1Tag nextUpdateTag = new Asn1Tag(TagClass.ContextSpecific, 0, true);
                tbs.PushSequence(nextUpdateTag);
                tbs.WriteGeneralizedTime(nextUpdate.Value, true);
                tbs.PopSequence(nextUpdateTag);
            }

            tbs.PopSequence();
        }

        tbs.PopSequence();
        tbs.PopSequence();
        byte[] tbsResponseData = tbs.Encode();

        AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.WriteEncodedValue(tbsResponseData);
        signer.WriteAlgorithmIdentifier(writer);
        writer.WriteBitString(signer.Sign(tbsResponseData));

        if (chain != null)
        {
            Asn1Tag certsTag = new Asn1Tag(TagClass.ContextSpecific, 0, true);
            writer.PushSequence(certsTag);
            writer.PushSequence();

            foreach (X509Certificate2 certificate in chain)
            {
                writer.WriteEncodedValue(certificate.RawData);
            }

            writer.PopSequence();
            writer.PopSequence(certsTag);
        }

        writer.PopSequence();

        return new BasicOcspResp(writer.Encode());
    }
}

// CLR-only: OCSPRespBuilder.build(status, basicResponse).getEncoded() and new OCSPResponse(status, responseBytes)
// .getEncoded(), with ResponseBytes(responseType, new DEROctetString(response)); a null responseType leaves the
// responseBytes out.
internal static class OcspRespBuilder
{
    internal static byte[] Build(OcspResponseStatus status, BasicOcspResp basicResponse)
    {
        return Encode(status, OcspObjectIdentifiers.IdPkixOcspBasic, basicResponse.GetEncoded());
    }

    internal static byte[] Encode(OcspResponseStatus status, string responseType, byte[] response)
    {
        AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.WriteEnumeratedValue(status);

        if (responseType != null)
        {
            Asn1Tag responseBytesTag = new Asn1Tag(TagClass.ContextSpecific, 0, true);
            writer.PushSequence(responseBytesTag);
            writer.PushSequence();
            writer.WriteObjectIdentifier(responseType);
            writer.WriteOctetString(response);
            writer.PopSequence();
            writer.PopSequence(responseBytesTag);
        }

        writer.PopSequence();

        return writer.Encode();
    }
}

// CLR-only: new AuthorityInformationAccess(new AccessDescription(id_ad_ocsp, new GeneralName(uniformResourceIdentifier,
// uri))).getEncoded(), the DER of the extension value that CertificateBuilder.AddExtensionOctetString takes.
internal static class AuthorityInformationAccess
{
    internal static byte[] OcspUri(string uri)
    {
        return new X509AuthorityInformationAccessExtension(new[] { uri }, null).RawData;
    }
}
