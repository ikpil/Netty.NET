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
using System.Numerics;
using System.Security.Cryptography.X509Certificates;

namespace Netty.NET.PkiTesting;

// CLR: Java assembles the TBSCertList with Bouncy Castle and RevocationServer signs it with Signed. The BCL's
// CertificateRevocationListBuilder encodes and signs the CRL in one call with an X509SignatureGenerator
// (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.certificaterevocationlistbuilder.build?view=net-10.0),
// so getEncoded takes the Signed generator and returns the signed CRL. Differences of the BCL encoding, all within
// RFC 5280 5.1 (https://www.rfc-editor.org/rfc/rfc5280): the builder always writes nextUpdate ("Conforming CRL issuers
// MUST include the nextUpdate field in all CRLs"), so nextUpdate is not optional; it omits revokedCertificates when
// no certificate is revoked, where Java writes an empty SEQUENCE; and it adds the CRL Number and Authority Key
// Identifier extensions, which Java does not write. The CRL number is the thisUpdate time in Unix milliseconds,
// "a monotonically increasing sequence number" (RFC 5280 5.2.3), and the Authority Key Identifier names the issuer
// certificate by its issuer and serial number, because CertificateBuilder writes no Subject Key Identifier.
// Instant is DateTimeOffset, and the Map.Entry<BigInteger, Instant> pairs are KeyValuePair<BigInteger, DateTimeOffset>.
internal sealed class CertificateList
{
    private readonly X509Bundle issuer;
    private readonly DateTimeOffset thisUpdate;
    private readonly DateTimeOffset nextUpdate;
    private readonly IEnumerable<KeyValuePair<BigInteger, DateTimeOffset>> revokedCerts;

    internal CertificateList(X509Bundle issuer, DateTimeOffset thisUpdate, DateTimeOffset nextUpdate,
                             IEnumerable<KeyValuePair<BigInteger, DateTimeOffset>> revokedCerts)
    {
        this.issuer = issuer;
        this.thisUpdate = thisUpdate;
        this.nextUpdate = nextUpdate;
        this.revokedCerts = revokedCerts;
    }

    internal byte[] GetEncoded(Signed signed)
    {
        CertificateRevocationListBuilder vec = new CertificateRevocationListBuilder();
        X509Certificate2 cert = issuer.GetCertificate();

        // Version 2
        // CLR: the builder writes the version (v2, INTEGER 1), the signature AlgorithmIdentifier from the generator, the
        // issuer name and the times; Signed(X509Bundle) uses the algorithm of cert.getSigAlgOID() as Java does.
        foreach (KeyValuePair<BigInteger, DateTimeOffset> revokedCert in revokedCerts)
        {
            vec.AddEntry(revokedCert.Key.ToByteArray(isUnsigned: false, isBigEndian: true), revokedCert.Value);
        }

        X509AuthorityKeyIdentifierExtension authorityKeyIdentifier =
            X509AuthorityKeyIdentifierExtension.CreateFromIssuerNameAndSerialNumber(cert.IssuerName,
                                                                                   cert.SerialNumberBytes.Span);
        return vec.Build(cert.SubjectName, signed, new BigInteger(thisUpdate.ToUnixTimeMilliseconds()), nextUpdate,
                         Algorithms.SignatureHash(Algorithms.GetSigAlgName(cert)), authorityKeyIdentifier, thisUpdate);
    }
}
