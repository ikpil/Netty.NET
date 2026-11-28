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

using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Netty.NET.Common.Internal;

namespace Netty.NET.PkiTesting;

// CLR: Java's Signed signs a DER to-be-signed structure (a TBSCertificate or a TBSCertList) with the named JCA signature
// algorithm and wraps it as SEQUENCE { toBeSigned, AlgorithmIdentifier, BIT STRING signature }. In the BCL that wrapping
// is done by CertificateRequest.Create and CertificateRevocationListBuilder.Build, which take an X509SignatureGenerator,
// the "Base class for building encoded signatures as needed for X.509 certificates"
// (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509signaturegenerator?view=net-10.0,
// https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.certificaterequest.create?view=net-10.0,
// https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.certificaterevocationlistbuilder.build?view=net-10.0).
// Signed is therefore that generator: the builders pass the to-be-signed bytes to SignData, so the toBeSigned
// constructor argument and getEncoded(Provider)/toInputStream(Provider) (the wrapping and the CertificateFactory input)
// have no counterpart, and the JCA Provider is not ported (see Algorithms). The AlgorithmIdentifier keeps the Java
// encoding, SEQUENCE { OID } without parameters, and the signature algorithm is the Java name, not the BCL builders'
// HashAlgorithmName, which SignData and GetSignatureAlgorithmIdentifier ignore.
internal sealed class Signed : X509SignatureGenerator
{
    private readonly string algorithmIdentifier;
    private readonly object privateKey;

    internal Signed(X509Bundle signer)
        : this(Algorithms.GetSigAlgName(signer.GetCertificate()), Algorithms.GetPrivate(signer.GetKeyPair()))
    {
    }

    internal Signed(string algorithmIdentifier, object privateKey)
    {
        this.algorithmIdentifier = ObjectUtil.CheckNotNull(algorithmIdentifier, "algorithmIdentifier");
        this.privateKey = privateKey;
    }

    public override byte[] GetSignatureAlgorithmIdentifier(HashAlgorithmName hashAlgorithm)
    {
        AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);

        using (writer.PushSequence())
        {
            writer.WriteObjectIdentifier(Algorithms.OidForAlgorithmName(algorithmIdentifier));
        }

        return writer.Encode();
    }

    public override byte[] SignData(byte[] data, HashAlgorithmName hashAlgorithm)
    {
        return Algorithms.Sign(algorithmIdentifier, privateKey, data);
    }

    protected override PublicKey BuildPublicKey()
    {
        return Algorithms.GetPublic(privateKey);
    }
}
