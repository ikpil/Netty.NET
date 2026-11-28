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
using System.Collections.ObjectModel;
using System.Formats.Asn1;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Netty.NET.Common.Internal;

namespace Netty.NET.PkiTesting;

/**
 * The {@link CertificateBuilder} produce {@link X509Bundle} instances, where the keys use the specified
 * algorithm, and the certificate have the specified data.
 * <p>
 * The builder can make self-signed bundles, or can make bundles that are signed by other bundles to build a verified
 * certificate path.
 * <p>
 * The builder can also make certificate that are invalid in various ways, for testing purpose.
 * The most typical application is to make a certificate that has already expired or is not yet valid.
 * <p>
 * See RFC 5280 for the details of X.509 certificate contents.
 * <p>
 * Here is an example where a leaf certificate is created and signed by a self-signed issuer certificate:
 * <pre>{@code
 * Instant now = Instant.now();
 * CertificateBuilder template = new CertificateBuilder()
 *             .notBefore(now.minus(1, DAYS))
 *             .notAfter(now.plus(1, DAYS));
 * X509Bundle issuer = template.copy()
 *             .subject("CN=testca, OU=dept, O=your-org")
 *             .setKeyUsage(true, KeyUsage.digitalSignature, KeyUsage.keyCertSign)
 *             .setIsCertificateAuthority(true)
 *             .buildSelfSigned();
 * X509Bundle leaf = template.copy()
 *             .subject("CN=leaf, OU=dept, O=your-org")
 *             .setKeyUsage(true, KeyUsage.digitalSignature)
 *             .addExtendedKeyUsage(ExtendedKeyUsage.PKIX_KP_SERVER_AUTH)
 *             .addSanDnsName("san-1.leaf.dept.your-org.com")
 *             .buildIssuedBy(issuer);
 * }</pre>
 */
// CLR: Bouncy Castle's V3TBSCertificateGenerator, ExtensionsGenerator and the Signed/CertificateFactory round trip are
// the BCL CertificateRequest: it holds the subject name, the public key and the extensions, and
// Create(issuerName, generator, notBefore, notAfter, serialNumber) encodes the TBSCertificate, signs it with the
// X509SignatureGenerator (Signed) and returns the X509Certificate2
// (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.certificaterequest.create?view=net-10.0).
// The extensions are the BCL X509Extension types (X509BasicConstraintsExtension, X509KeyUsageExtension,
// X509EnhancedKeyUsageExtension, X509SubjectAlternativeNameExtension) and X509Extension for the CRL distribution points
// and the custom extensions, whose DER the BCL does not build and AsnWriter writes
// (https://learn.microsoft.com/en-us/dotnet/api/system.formats.asn1.asnwriter?view=net-10.0). Owner decision
// 2026-11-24: pkitesting is ported on the BCL. Type mappings: X500Principal is X500DistinguishedName, whose string
// constructor encodes "CN=netty.io, O=Netty" with O first as X500Principal's RFC 2253 parser does (measured on this
// host); Instant is DateTimeOffset; BigInteger is System.Numerics.BigInteger, a struct, so the optional serial is
// BigInteger?; OptionalInt is int?; URI is Uri; InetAddress is IPAddress; the key pairs are the BCL key objects (see
// Algorithms); and the Bouncy Castle GeneralName and DistributionPoint objects are their DER encodings (byte[]), so the
// empty arrays for their toArray calls are not needed. provider(Provider), secureRandom(SecureRandom), the provider and
// random fields and SecureRandomHolder are not ported: the BCL generates keys without a provider or a caller's random
// source (see Algorithms.KeyPairGenerator), and the random serial number comes from RandomNumberGenerator
// (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.randomnumbergenerator.getbytes?view=net-10.0).
// requireNonNull is ObjectUtil.CheckNotNull (ArgumentNullException, docs/codec-dns-porting.md); for the value types
// Instant (DateTimeOffset) and OptionalInt (int?) it has no counterpart. The package-private fields stay internal.
public sealed class CertificateBuilder
{

    internal const string OID_X509_NAME_CONSTRAINTS = "2.5.29.30";
    internal const string OID_PKIX_KP = "1.3.6.1.5.5.7.3";
    internal const string OID_PKIX_KP_SERVER_AUTH = OID_PKIX_KP + ".1";
    internal const string OID_PKIX_KP_CLIENT_AUTH = OID_PKIX_KP + ".2";
    internal const string OID_PKIX_KP_CODE_SIGNING = OID_PKIX_KP + ".3";
    internal const string OID_PKIX_KP_EMAIL_PROTECTION = OID_PKIX_KP + ".4";
    internal const string OID_PKIX_KP_TIME_STAMPING = OID_PKIX_KP + ".8";
    internal const string OID_PKIX_KP_OCSP_SIGNING = OID_PKIX_KP + ".9";
    internal const string OID_PKIX_PE_ACME_IDENTIFIER = "1.3.6.1.5.5.7.1.31";
    internal const string OID_KERBEROS_KEY_PURPOSE_CLIENT_AUTH = "1.3.6.1.5.2.3.4";
    internal const string OID_MICROSOFT_SMARTCARD_LOGIN = "1.3.6.1.4.1.311.20.2.2";
    private static readonly object UNSUPPORTED_SPEC = new object();
    private const string UNSUPPORTED_SIGN = "UNSUPPORTED_SIGN";

    internal Algorithm algorithm = Algorithm.ecp256;
    internal DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddDays(-1);
    internal DateTimeOffset notAfter = DateTimeOffset.UtcNow.AddDays(1);
    internal List<BuilderCallback> modifierCallbacks = new List<BuilderCallback>();
    internal List<byte[]> subjectAlternativeNames = new List<byte[]>();
    internal List<byte[]> crlDistributionPoints = new List<byte[]>();
    internal BigInteger? serial;
    internal X500DistinguishedName subject;
    internal bool isCertificateAuthority;
    internal int? pathLengthConstraint = null;
    internal object keyPair;
    // CLR: TreeSet<String> orders by String.compareTo, which compares UTF-16 code units as StringComparer.Ordinal does.
    internal SortedSet<string> extendedKeyUsage = new SortedSet<string>(StringComparer.Ordinal);
    internal X509Extension keyUsage;

    /**
     * Create a new certificate builder with a default configuration.
     * Unless specified otherwise, the builder will produce bundles that use the
     * {@linkplain Algorithm#ecp256 NIST EC-P 256} key algorithm,
     * and the certificates will be valid as of yesterday and expire tomorrow.
     */
    public CertificateBuilder()
    {
    }

    /**
     * Produce a copy of the current state in this certificate builder.
     * @return A copy of this certificate builder.
     */
    public CertificateBuilder Copy()
    {
        CertificateBuilder copy = new CertificateBuilder();
        copy.algorithm = algorithm;
        copy.notBefore = notBefore;
        copy.notAfter = notAfter;
        copy.modifierCallbacks = new List<BuilderCallback>(modifierCallbacks);
        copy.subjectAlternativeNames = new List<byte[]>(subjectAlternativeNames);
        copy.crlDistributionPoints = new List<byte[]>(crlDistributionPoints);
        copy.serial = serial;
        copy.subject = subject;
        copy.isCertificateAuthority = isCertificateAuthority;
        copy.pathLengthConstraint = pathLengthConstraint;
        copy.keyPair = keyPair;
        copy.keyUsage = keyUsage;
        copy.extendedKeyUsage = new SortedSet<string>(extendedKeyUsage, StringComparer.Ordinal);
        return copy;
    }

    /**
     * Set the not-before field of the certificate. The certificate will not be valid before this time.
     * @param instant The not-before time.
     * @return This certificate builder.
     */
    public CertificateBuilder NotBefore(DateTimeOffset instant)
    {
        notBefore = instant;
        return this;
    }

    /**
     * Set the not-after field of the certificate. The certificate will not be valid after this time.
     * @param instant The not-after time.
     * @return This certificate builder.
     */
    public CertificateBuilder NotAfter(DateTimeOffset instant)
    {
        notAfter = instant;
        return this;
    }

    /**
     * Set the specific serial number to use in the certificate.
     * One will be generated randomly, if none is specified.
     * @param serial The serial number to use, or {@code null}.
     * @return This certificate builder.
     */
    public CertificateBuilder Serial(BigInteger? serial)
    {
        this.serial = serial;
        return this;
    }

    /**
     * Set the fully-qualified domain name (an X.500 name) as the subject of the certificate.
     * @param fqdn The subject name to use.
     * @return This certificate builder.
     */
    public CertificateBuilder Subject(string fqdn)
    {
        subject = new X500DistinguishedName(ObjectUtil.CheckNotNull(fqdn, "fqdn"));
        return this;
    }

    /**
     * Set the subject name of the certificate to the given {@link X500Principal}.
     * @param name The subject name to use.
     * @return This certificate builder.
     */
    public CertificateBuilder Subject(X500DistinguishedName name)
    {
        subject = ObjectUtil.CheckNotNull(name, "name");
        return this;
    }

    /**
     * Add an Other Name to the Subject Alternative Names, of the given OID type, and with the given encoded value.
     * The type and value will be wrapped in a SEQUENCE.
     * @param typeOid The OID type of the Other Name value.
     * @param encodedValue The encoded Other Name value.
     * @return This certificate builder.
     */
    public CertificateBuilder AddSanOtherName(string typeOid, byte[] encodedValue)
    {
        subjectAlternativeNames.Add(GeneralNameUtils.OtherName(typeOid, encodedValue));
        return this;
    }

    /**
     * Add an RFC 822 name to the Subject Alternative Names.
     * The RFC 822 standard is the obsolete specification for email, so these SANs are email addresses.
     * @param name The email address to add to the SANs.
     * @return This certificate builder.
     */
    public CertificateBuilder AddSanRfc822Name(string name)
    {
        subjectAlternativeNames.Add(GeneralNameUtils.Rfc822Name(name));
        return this;
    }

    /**
     * Add a DNS name to the Subject Alternate Names.
     * @param dns The DNS name to add.
     * @return This certificate builder.
     */
    public CertificateBuilder AddSanDnsName(string dns)
    {
        if (dns.Trim().Length == 0)
        {
            throw new ArgumentException("Blank DNS SANs are forbidden by RFC 5280, Section 4.2.1.6.");
        }

        subjectAlternativeNames.Add(GeneralNameUtils.DnsName(dns));
        return this;
    }

    // x400Address support intentionally omitted; not in common use.

    /**
     * Add a Directory Name to the Subject Alternative Names.
     * These are LDAP directory paths.
     * @param dirName The directory name to add to the SANs.
     * @return This certificate builder.
     */
    public CertificateBuilder AddSanDirectoryName(string dirName)
    {
        subjectAlternativeNames.Add(GeneralNameUtils.DirectoryName(dirName));
        return this;
    }

    // ediPartyName support intentionally omitted; not in common use.

    /**
     * Add a URI name to the Subject Alternative Names.
     * @param uri The URI to add to the SANs.
     * @return This certificate builder.
     */
    // CLR: the URISyntaxException of new URI(uri) is the UriFormatException of new Uri(uri); C# has no throws clause.
    public CertificateBuilder AddSanUriName(string uri)
    {
        subjectAlternativeNames.Add(GeneralNameUtils.UriName(uri));
        return this;
    }

    /**
     * Add a URI name to the Subject Alternative Names.
     * @param uri The URI to add to the SANs.
     * @return This certificate builder.
     */
    public CertificateBuilder AddSanUriName(Uri uri)
    {
        subjectAlternativeNames.Add(GeneralNameUtils.UriName(uri));
        return this;
    }

    /**
     * Add an IP address to the Subject Alternative Names.
     * IPv4 and IPv6 addresses are both supported and converted to their correct encoding.
     * @param ipAddress The IP address to add to the SANs.
     * @return This certificate builder.
     */
    public CertificateBuilder AddSanIpAddress(string ipAddress)
    {
        subjectAlternativeNames.Add(GeneralNameUtils.IpAddress(ipAddress));
        return this;
    }

    /**
     * Add an IP address to the Subject Alternative Names.
     * IPv4 and IPv6 addresses are both supported and converted to their correct encoding.
     * @param ipAddress The IP address to add to the SANs.
     * @return This certificate builder.
     */
    public CertificateBuilder AddSanIpAddress(IPAddress ipAddress)
    {
        subjectAlternativeNames.Add(GeneralNameUtils.IpAddress(ipAddress.ToString()));
        return this;
    }

    /**
     * Add a registeredID to the Subject Alternative Names.
     * A registeredID is an OBJECT IDENTIFIER, or OID, in ASN.1 speak.
     * @param oid The OID to add to the SANs.
     * @return This certificate builder.
     */
    public CertificateBuilder AddSanRegisteredId(string oid)
    {
        subjectAlternativeNames.Add(GeneralNameUtils.RegisteredId(oid));
        return this;
    }

    /**
     * Add a URI distribution point for a certificate revocation list.
     * <p>
     * If you are testing certificate revocation using the {@link RevocationServer},
     * you would obtain this URI from {@link RevocationServer#getCrlUri(X509Bundle)} with your intended issuer
     * certificate bundle.
     *
     * @param uri The URI for the CRL file.
     * @return This certificate builder.
     */
    // CLR: CertificateRevocationListBuilder.BuildCrlDistributionPointExtension builds the DistributionPoint with
    // the URI as its fullName and no reasons or cRLIssuer, as the Java code does
    // (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.certificaterevocationlistbuilder.buildcrldistributionpointextension?view=net-10.0);
    // its one DistributionPoint is taken out of the CRLDistributionPoints SEQUENCE, so that every call adds one
    // point as in Java.
    public CertificateBuilder AddCrlDistributionPoint(Uri uri)
    {
        X509Extension fullName = CertificateRevocationListBuilder.BuildCrlDistributionPointExtension(
                new[] { uri.AbsoluteUri }, false);
        AsnReader distributionPoints = new AsnReader(fullName.RawData, AsnEncodingRules.DER).ReadSequence();
        crlDistributionPoints.Add(distributionPoints.ReadEncodedValue().ToArray());
        return this;
    }

    /**
     * Add a URI distribution point for a certificate revocation list.
     * <p>
     * If you are testing certificate revocation using the {@link RevocationServer},
     * you would obtain this URI from {@link RevocationServer#getCrlUri(X509Bundle)} with your intended issuer
     * certificate bundle.
     *
     * @param uri The URI for the CRL file.
     * @param issuer The issuer that signs the CRL file.
     * This MUST be {@code null} if the CRL issuer is also the issuer of the certificate being built.
     * Otherwise, if this certificate and the CRL will be signed by different issuers, then this MUST be the subject
     * name of the CRL signing certificate.
     * @return This certificate builder.
     */
    // CLR: BuildCrlDistributionPointExtension cannot write a cRLIssuer, so AsnWriter writes the DistributionPoint
    // SEQUENCE { [0] { [0] GeneralNames { fullName } }, [2] GeneralNames { issuerName } } (RFC 5280 4.2.1.13,
    // https://www.rfc-editor.org/rfc/rfc5280) that Bouncy Castle's DistributionPoint encodes.
    public CertificateBuilder AddCrlDistributionPoint(Uri uri, X500DistinguishedName issuer)
    {
        byte[] fullName = GeneralNameUtils.UriName(uri);
        byte[] issuerName = GeneralNameUtils.DirectoryName(issuer);
        AsnWriter distributionPoint = new AsnWriter(AsnEncodingRules.DER);

        using (distributionPoint.PushSequence())
        {
            using (distributionPoint.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
            {
                using (distributionPoint.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
                {
                    distributionPoint.WriteEncodedValue(fullName);
                }
            }

            using (distributionPoint.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 2)))
            {
                distributionPoint.WriteEncodedValue(issuerName);
            }
        }

        crlDistributionPoints.Add(distributionPoint.Encode());
        return this;
    }

    /**
     * Set the certificate authority field.
     * If this is set to {@code true}, then this builder can build self-signed certificates, and those certifiactes
     * can be used to sign other certificates.
     * @param isCA {@code true} if this builder should make CA certificates.
     * @return This certificate builder.
     */
    public CertificateBuilder SetIsCertificateAuthority(bool isCA)
    {
        isCertificateAuthority = isCA;
        return this;
    }

    /**
     * Certificate Authority certificates may impose a limit to the length of the verified certificate path they permit.
     * @param pathLengthConstraint The maximum verified path length, if any.
     * @return This certificate builder.
     */
    public CertificateBuilder SetPathLengthConstraint(int? pathLengthConstraint)
    {
        this.pathLengthConstraint = pathLengthConstraint;
        return this;
    }

    /**
     * Set the key algorithm to use. This also determines how certificates are signed.
     * @param algorithm The algorithm to use when generating the private key.
     * @return This certificate builder.
     */
    // CLR: algorithm(Algorithm) is Algorithm_(Algorithm): a method cannot share the name of the nested Algorithm
    // class (CS0102, https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0102), as codec-haproxy's type() is
    // Type_(). UnsupportedOperationException is NotSupportedException.
    public CertificateBuilder Algorithm_(Algorithm algorithm)
    {
        ObjectUtil.CheckNotNull(algorithm, "algorithm");
        if (algorithm.parameterSpec == UNSUPPORTED_SPEC)
        {
            throw new NotSupportedException("This algorithm is not supported: " + algorithm);
        }

        this.algorithm = algorithm;
        return this;
    }

    /**
     * Make this certificate builder use the {@linkplain Algorithm#ecp256 NIST EC-P 256} elliptic curve key algorithm.
     * This algorithm provides a good balance between security, compatibility, performance, and key & signature sizes.
     * @return This certificate builder.
     * @see Algorithm#ecp256
     */
    public CertificateBuilder Ecp256()
    {
        return Algorithm_(Algorithm.ecp256);
    }

    /**
     * Make this certificate builder use the {@linkplain Algorithm#rsa2048 2048-bit RSA} encryption and signing
     * algorithm. This algorithm provides maximum compatibility, but keys are large and slow to generate.
     * @return This certificate builder.
     * @see Algorithm#rsa2048
     */
    public CertificateBuilder Rsa2048()
    {
        return Algorithm_(Algorithm.rsa2048);
    }

    /**
     * Instruct the certificate builder to not generate its own key pair, but to instead create a certificate that
     * uses the given public key.
     * <p>
     * This method is useful if you want to use an existing key-pair, e.g. to emulate a certificate authority
     * responding to a Certificate Signing Request (CSR).
     * <p>
     * If the given public key is {@code null} (the default) then a new key-pair will be generated instead.
     *
     * @param publicKey The public key to wrap in a certificate.
     * @return This certificate builder.
     */
    // CLR: new KeyPair(publicKey, null) is the BCL PublicKey itself: a key pair without a private key (see Algorithms).
    public CertificateBuilder PublicKey(PublicKey publicKey)
    {
        if (publicKey == null)
        {
            keyPair = null;
        }
        else
        {
            keyPair = publicKey;
        }

        return this;
    }

    /**
     * Instruct the certificate builder to not generate its own key pair, but to instead create a certificate that
     * uses the given key pair.
     * <p>
     * This method is useful if you want to use an existing key-pair, e.g. to emulate a certificate authority
     * responding to a Certificate Signing Request (CSR), or when creating cross-signed certificates.
     * <p>
     * Cross-signing is when two certificates have the same subject and public key, but are signed by different keys.
     * In effect, it's the same logical certificate, but manifest as two different "concrete" certificate objects, i.e.
     * two different {@link X509Bundle} objects. Cross-signing can be done to both leaf certificates and to issuers,
     * and can create complicated certificate graphs. The technique is used for introducing new roots and issuers
     * to a PKI system, in a backwards compatible way where certificates can be trusted by peers that aren't familiar
     * with the new roots or issuers.
     * <p>
     * If the given key pair is {@code null} (the default) then a new key-pair will be generated instead.
     *
     * @param keyPair The key pair to use when creating a certificate.
     * @return This certificate builder.
     */
    // CLR: every BCL key object carries its public key, so getPublic() is null only for an object that is not a BCL key
    // object (Algorithms.GetPublic).
    public CertificateBuilder KeyPair(object keyPair)
    {
        if (keyPair != null && Algorithms.GetPublic(keyPair) == null)
        {
            throw new ArgumentException("The given key pair must have a public key");
        }

        this.keyPair = keyPair;
        return this;
    }

    // CLR: new Extension(oid, critical, value) is X509Extension(oid, rawData, critical); value is the extnValue content.
    private CertificateBuilder AddExtension(string identifierOid, bool critical, byte[] value)
    {
        ObjectUtil.CheckNotNull(identifierOid, "identifierOid");
        ObjectUtil.CheckNotNull(value, "value");

        modifierCallbacks.Add(builder =>
        {
            builder.Add(new X509Extension(
                    identifierOid,
                    value,
                    critical));
        });
        return this;
    }

    /**
     * Add a custom extension to the certificate, with the given OID, criticality flag, and DER-encoded contents.
     * @param identifierOID The OID identifying the extension.
     * @param critical {@code true} if the extension is critical, otherwise {@code false}.
     * Certificate systems MUST reject certificates with critical extensions they don't recognize.
     * @param contents The DER-encoded extension contents.
     * @return This certificate builder.
     */
    public CertificateBuilder AddExtensionOctetString(string identifierOID, bool critical, byte[] contents)
    {
        ObjectUtil.CheckNotNull(identifierOID, "identifierOID");
        ObjectUtil.CheckNotNull(contents, "contents");

        modifierCallbacks.Add(builder =>
        {
            builder.Add(new X509Extension(
                    identifierOID,
                    contents,
                    critical));
        });
        return this;
    }

    /**
     * Add a custom DER-encoded ASN.1 UTF-8 string extension to the certificate, with the given OID, criticality,
     * and string value.
     * The string will be converted to its proper binary encoding by this method.
     * @param identifierOID The OID identifying the extension.
     * @param critical {@code true} if the extension is critical, otherwise {@code false}.
     * Certificate systems MUST reject certificates with critical extensions they don't recognize.
     * @param value The string value.
     * @return This certificate builder.
     */
    // CLR: new DERUTF8String(value).getEncoded("DER") is AsnWriter.WriteCharacterString(UTF8String); the IOException
    // wrapped in UncheckedIOException has no counterpart.
    public CertificateBuilder AddExtensionUtf8String(string identifierOID, bool critical, string value)
    {
        AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);
        writer.WriteCharacterString(UniversalTagNumber.UTF8String, value);

        return AddExtension(identifierOID, critical, writer.Encode());
    }

    /**
     * Add a custom DER-encoded ASN.1 IA5String (an ASCII string) extension to the certificate, with the given OID,
     * criticality, and string value.
     * The string will be converted to its proper binary encoding by this method.
     * @param identifierOID The OID identifying the extension.
     * @param critical {@code true} if the extension is critical, otherwise {@code false}.
     * Certificate systems MUST reject certificates with critical extensions they don't recognize.
     * @param value The string value.
     * @return This certificate builder.
     */
    // CLR: new DERIA5String(value).getEncoded("DER") is AsnWriter.WriteCharacterString(IA5String), which rejects a
    // character outside IA5 where the unvalidated Bouncy Castle constructor writes its low byte.
    public CertificateBuilder AddExtensionAsciiString(string identifierOID, bool critical, string value)
    {
        AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);
        writer.WriteCharacterString(UniversalTagNumber.IA5String, value);

        return AddExtension(identifierOID, critical, writer.Encode());
    }

    /**
     * Add an {@code acmeIdentifier} extension marked {@code critical}, with the given authorization SHA-256 value.
     * <p>
     * The {@code acmeIdentifier} extension is specified by
     * <a href="https://datatracker.ietf.org/doc/html/rfc8737">RFC 8737</a>,
     * which specify the TLS-ALPN-01 challenge type.
     * See the <a href="https://letsencrypt.org/docs/challenge-types/#tls-alpn-01">Let's Encrypt documentation</a>
     * for a short summary of this ACME challenge method.
     * <p>
     * To use this challenge method, the server must accept TLS connections with an ALPN protocol name of
     * {@code acme-tls/1} and an SNI extension for the hostname being challenged.
     * The server must then offer a self-signed certificate that include both this extension,
     * and a {@linkplain #addSanDnsName(String) dNSName SAN} with the hostname that is being challenged.
     * <p>
     * The extension value must be the SHA-256 of the key authorization challenge.
     * See <a href="https://datatracker.ietf.org/doc/html/rfc8555#section-8.1">RFC 8555</a> for details on key
     * authorizations.
     *
     * @param sha256 The SHA-256 of the key authorization.
     * @return This certificate builder.
     */
    // CLR: as in Java, the given bytes are the extnValue content; RFC 8737 section 3 defines that content as "the ASN.1
    // DER encoding [X.690] of the Authorization structure", Authorization ::= OCTET STRING (SIZE (32))
    // (https://www.rfc-editor.org/rfc/rfc8737), so a caller passes the encoded OCTET STRING, not the bare digest, for a
    // conforming extension. The Java encoding is kept.
    public CertificateBuilder AddAcmeIdentifierExtension(byte[] sha256)
    {
        return AddExtension(OID_PKIX_PE_ACME_IDENTIFIER, true, ObjectUtil.CheckNotNull(sha256, "sha256"));
    }

    /**
     * The key usage specify the intended usages for which the certificate has been issued.
     * Some are overlapping, some are deprecated, and some are implied by usage.
     * <p>
     * For Certificate Authority usage, the important ones are {@link KeyUsage#keyCertSign}
     * and {@link KeyUsage#cRLSign}.
     * <p>
     * Any certificate that has {@link KeyUsage#keyCertSign} must also have {@link #setIsCertificateAuthority(boolean)}
     * set to {@code true}.
     *
     * @param critical {@code true} if certificate recipients are required to understand all the set bits,
     * otherwise {@code false}.
     * @param keyUsages The key usages to set.
     * @return This certificate builder.
     */
    // CLR: the DER BIT STRING computed as in Java (AsnWriter.WriteBitString with the same padding) becomes an
    // X509KeyUsageExtension through its (AsnEncodedData, Boolean) constructor, "if the information to create the
    // extension is already in an AsnEncodedData object"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509keyusageextension.-ctor?view=net-10.0),
    // so the extension keeps the Java bytes. >>> is the C# logical shift.
    public CertificateBuilder SetKeyUsage(bool critical, params KeyUsage[] keyUsages)
    {
        int maxBit = 0;

        foreach (KeyUsage usage in keyUsages)
        {
            maxBit = Math.Max(usage.bitId, maxBit);
        }

        bool[] bits = new bool[maxBit + 1];

        foreach (KeyUsage usage in keyUsages)
        {
            bits[usage.bitId] = true;
        }

        int padding = 8 - bits.Length % 8;
        int lenBytes = bits.Length / 8 + 1;

        if (padding == 8)
        {
            padding = 0;
            lenBytes--;
        }

        byte[] bytes = new byte[lenBytes];

        for (int i = 0; i < bits.Length; i++)
        {
            if (bits[i])
            {
                int byteIndex = i / 8;
                int bitIndex = i % 8;
                bytes[byteIndex] |= (byte) (0x80 >>> bitIndex);
            }
        }

        AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);
        writer.WriteBitString(bytes, padding);
        keyUsage = new X509KeyUsageExtension(
                new AsnEncodedData(writer.Encode()),
                critical);
        return this;
    }

    /**
     * Add the given OID to the list of extended key usages.
     * @param oid The OID to add.
     * @return This certificate builder.
     * @see ExtendedKeyUsage
     * @see #addExtendedKeyUsage(ExtendedKeyUsage)
     */
    public CertificateBuilder AddExtendedKeyUsage(string oid)
    {
        extendedKeyUsage.Add(oid);
        return this;
    }

    /**
     * Add the given {@link ExtendedKeyUsage} to the list of extended key usages.
     * @param keyUsage The extended key usage to add.
     * @return This certificate builder.
     */
    public CertificateBuilder AddExtendedKeyUsage(ExtendedKeyUsage keyUsage)
    {
        extendedKeyUsage.Add(keyUsage.GetOid());
        return this;
    }

    /**
     * Add server-authentication to the list of extended key usages.
     * @return This certificate builder.
     */
    public CertificateBuilder AddExtendedKeyUsageServerAuth()
    {
        return AddExtendedKeyUsage(ExtendedKeyUsage.PKIX_KP_SERVER_AUTH);
    }

    /**
     * Add client-authentication to the list of extended key usages.
     * @return This certificate builder.
     */
    public CertificateBuilder AddExtendedKeyUsageClientAuth()
    {
        return AddExtendedKeyUsage(ExtendedKeyUsage.PKIX_KP_CLIENT_AUTH);
    }

    /**
     * Add code signing to the list of extended key usages.
     * @return This certificate builder.
     */
    public CertificateBuilder AddExtendedKeyUsageCodeSigning()
    {
        return AddExtendedKeyUsage(ExtendedKeyUsage.PKIX_KP_CODE_SIGNING);
    }

    /**
     * Add email protection to the list of extended key usages.
     * @return This certificate builder.
     */
    public CertificateBuilder AddExtendedKeyUsageEmailProtection()
    {
        return AddExtendedKeyUsage(ExtendedKeyUsage.PKIX_KP_EMAIL_PROTECTION);
    }

    /**
     * Add time-stamping to the list of extended key usages.
     * @return This certificate builder.
     */
    public CertificateBuilder AddExtendedKeyUsageTimeStamping()
    {
        return AddExtendedKeyUsage(ExtendedKeyUsage.PKIX_KP_TIME_STAMPING);
    }

    /**
     * Add OCSP signing to the list of extended key usages.
     * @return This certificate builder.
     */
    public CertificateBuilder AddExtendedKeyUsageOcspSigning()
    {
        return AddExtendedKeyUsage(ExtendedKeyUsage.PKIX_KP_OCSP_SIGNING);
    }

    /**
     * Add Kerberos client authentication to the list of extended key usages.
     * @return This certificate builder.
     */
    public CertificateBuilder AddExtendedKeyUsageKerberosClientAuth()
    {
        return AddExtendedKeyUsage(ExtendedKeyUsage.KERBEROS_KEY_PURPOSE_CLIENT_AUTH);
    }

    /**
     * Add Microsoft smartcard login to the list of extended key usages.
     * @return This certificate builder.
     */
    public CertificateBuilder AddExtendedKeyUsageMicrosoftSmartcardLogin()
    {
        return AddExtendedKeyUsage(ExtendedKeyUsage.MICROSOFT_SMARTCARD_LOGIN);
    }

    /**
     * Build a {@link X509Bundle} with a self-signed certificate.
     * @return The newly created bundle.
     * @throws Exception If something went wrong in the process.
     */
    // CLR: IllegalStateException is InvalidOperationException. As in Java, a new key pair is generated even when
    // one was given. The private key is not attached to the X509Certificate2 (the bundle keeps the key pair next
    // to it, as the Java bundle does); CertificateRequest.CreateSelfSigned would attach it with
    // CopyWithPrivateKey, which fails for ML-DSA keys on this Windows host, so the certificate is signed with
    // Create(subject, Signed,...).
    public X509Bundle BuildSelfSigned()
    {
        if (this.keyPair != null &&
            (Algorithms.GetPublic(this.keyPair) == null || Algorithms.GetPrivate(this.keyPair) == null))
        {
            throw new InvalidOperationException("Cannot create a self-signed certificate with an incomplete key pair.");
        }
        if (!algorithm.SupportSigning())
        {
            throw new InvalidOperationException("Cannot create a self-signed certificate with a " +
                    "key algorithm that does not support signing: " + algorithm);
        }

        object keyPair = GenerateKeyPair();

        CertificateRequest generator = CreateCertBuilder(subject, keyPair, algorithm.signatureType);

        AddExtensions(generator);

        Signed signed = new Signed(algorithm.signatureType, Algorithms.GetPrivate(keyPair));
        X509Certificate2 cert = CreateCertificate(generator, subject, signed);
        return X509Bundle.FromRootCertificateAuthority(cert, keyPair);
    }

    /**
     * Build a {@link X509Bundle} with a certificate signed by the given issuer bundle.
     * The signing algorithm used will be derived from the issuers public key.
     * @return The newly created bundle.
     * @throws Exception If something went wrong in the process.
     */
    public X509Bundle BuildIssuedBy(X509Bundle issuerBundle)
    {
        string issuerSignAlgorithm = PreferredSignatureAlgorithm(issuerBundle.GetCertificate().PublicKey);
        return BuildIssuedBy(issuerBundle, issuerSignAlgorithm);
    }

    /**
     * Build a {@link X509Bundle} with a certificate signed by the given issuer bundle, using the specified
     * signing algorithm.
     * @return The newly created bundle.
     * @throws Exception If something went wrong in the process.
     */
    public X509Bundle BuildIssuedBy(X509Bundle issuerBundle, string signAlg)
    {
        object keyPair;
        if (this.keyPair == null)
        {
            keyPair = GenerateKeyPair();
        }
        else
        {
            keyPair = this.keyPair;
        }

        X500DistinguishedName issuerPrincipal = issuerBundle.GetCertificate().SubjectName;
        CertificateRequest generator = CreateCertBuilder(subject, keyPair, signAlg);

        AddExtensions(generator);

        object issuerPrivateKey = Algorithms.GetPrivate(issuerBundle.GetKeyPair());
        if (issuerPrivateKey == null)
        {
            throw new ArgumentException(
                    "Cannot sign certificate with issuer bundle that does not have a private key.");
        }

        Signed signed = new Signed(signAlg, issuerPrivateKey);
        X509Certificate2 cert = CreateCertificate(generator, issuerPrincipal, signed);
        X509Certificate2[] issuerPath = issuerBundle.GetCertificatePath();
        X509Certificate2[] path = new X509Certificate2[issuerPath.Length + 1];
        path[0] = cert;
        Array.Copy(issuerPath, 0, path, 1, issuerPath.Length);

        return X509Bundle.FromCertificatePath(path, issuerBundle.GetRootCertificate(), keyPair);
    }

    // CLR: the JCA key interfaces are read from the BCL PublicKey: RSAPublicKey.getModulus().bitLength() is the RSA key
    // size, ECPublicKey.getW().getAffineX().bitLength() is BigInteger.GetBitLength() of the unsigned X coordinate
    // (https://learn.microsoft.com/en-us/dotnet/api/system.numerics.biginteger.getbitlength?view=net-10.0), and a DSA key
    // is one that GetDSAPublicKey() returns. key.getAlgorithm() is the algorithm OID of PublicKey.Oid
    // (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.publickey?view=net-10.0):
    // the JCA names never equal it, the OIDs 1.3.101.112/113 (id-Ed25519/id-Ed448, https://www.rfc-editor.org/rfc/rfc8410)
    // keep their results, and the Bouncy Castle "EdDSA" name, for which Java tells the curves apart by the length of the
    // encoded key, never occurs, because a BCL PublicKey always names the curve's OID; the branch is kept with the
    // SubjectPublicKeyInfo length as getEncoded(). The ML-DSA parameter set name comes from MLDsa.Algorithm.Name instead
    // of the reflective AsymmetricKey.getParams().getName(), and ML-DSA/ML-KEM keys are recognized by their OIDs
    // (2.16.840.1.101.3.4.3.17-19 and 2.16.840.1.101.3.4.4.1-3). The message of the last exception names the key's OID,
    // because PublicKey.ToString() is the type name.
    private static string PreferredSignatureAlgorithm(PublicKey key)
    {
        using (RSA rsa = key.GetRSAPublicKey())
        {
            if (rsa != null)
            {
                if (rsa.KeySize < 4096)
                {
                    return "SHA256withRSA";
                }

                return "SHA384withRSA";
            }
        }

        using (ECDsa ec = key.GetECDsaPublicKey())
        {
            if (ec != null)
            {
                long size = new BigInteger(ec.ExportParameters(false).Q.X, isUnsigned: true, isBigEndian: true)
                        .GetBitLength();
                // Note: the coords are not guaranteed to use up all available bits, hence less-than-or-equal checks.
                if (size <= 256)
                {
                    return "SHA256withECDSA";
                }
                if (size <= 384)
                {
                    return "SHA384withECDSA";
                }

                return "SHA512withECDSA";
            }
        }

        if (IsDsaKey(key))
        {
            throw new ArgumentException("DSA keys are not supported because they are obsolete");
        }

        string keyAlgorithm = key.Oid.Value;
        if ("Ed25519".Equals(keyAlgorithm) || "1.3.101.112".Equals(keyAlgorithm))
        {
            return "Ed25519";
        }
        if ("Ed448".Equals(keyAlgorithm) || "1.3.101.113".Equals(keyAlgorithm))
        {
            return "Ed448";
        }

        if ("EdDSA".Equals(keyAlgorithm))
        {
            byte[] encoded = key.ExportSubjectPublicKeyInfo();
            if (encoded.Length <= 44)
            {
                return "Ed25519";
            }
            if (encoded.Length <= 69)
            {
                return "Ed448";
            }
        }

        if ("2.16.840.1.101.3.4.3.17".Equals(keyAlgorithm) || "2.16.840.1.101.3.4.3.18".Equals(keyAlgorithm) ||
            "2.16.840.1.101.3.4.3.19".Equals(keyAlgorithm))
        {
            try
            {
#pragma warning disable SYSLIB5006 // PublicKey.GetMLDsaPublicKey is experimental; see Algorithms.
                using MLDsa mlDsa = key.GetMLDsaPublicKey();
#pragma warning restore SYSLIB5006
                return mlDsa.Algorithm.Name;
            }
            catch (Exception e)
            {
                throw new ArgumentException("Cannot get algorithm name for ML-DSA key", e);
            }
        }

        if ("2.16.840.1.101.3.4.4.1".Equals(keyAlgorithm) || "2.16.840.1.101.3.4.4.2".Equals(keyAlgorithm) ||
            "2.16.840.1.101.3.4.4.3".Equals(keyAlgorithm))
        {
            throw new ArgumentException("ML-KEM keys cannot be used for signing");
        }

        throw new ArgumentException("Don't know what signature algorithm is best for " + keyAlgorithm);
    }

    // CLR: key instanceof DSAPublicKey; GetDSAPublicKey() returns null for another key type.
    private static bool IsDsaKey(PublicKey key)
    {
        try
        {
            using DSA dsa = key.GetDSAPublicKey();
            return dsa != null;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    private object GenerateKeyPair()
    {
        return algorithm.GenerateKeyPair();
    }

    // CLR: createCertBuilder sets the issuer, subject, serial number, signature algorithm, validity and public key of a
    // V3TBSCertificateGenerator. A CertificateRequest takes the subject and the public key (the
    // (X500DistinguishedName, PublicKey, HashAlgorithmName) constructor, which accepts every BCL key type); the issuer
    // name, validity, serial number and signature are the arguments of Create in CreateCertificate. A null subject throws
    // ArgumentNullException here, where Java fails later in Bouncy Castle.
    private CertificateRequest CreateCertBuilder(X500DistinguishedName subject, object keyPair, string signAlg)
    {
        PublicKey pubKey = Algorithms.GetPublic(keyPair);

        CertificateRequest generator = new CertificateRequest(subject, pubKey, Algorithms.SignatureHash(signAlg));
        return generator;
    }

    // CLR: tbsCertToBytes, new Signed(...) and CertificateFactory.generateCertificate(signed.toInputStream(provider)) are
    // CertificateRequest.Create(issuer, signed, notBefore, notAfter, serialNumber), which encodes the TBSCertificate,
    // signs it with the generator and returns the certificate. new BigInteger(159, random) is a random value in
    // [0, 2^159 - 1]: 20 random bytes with the top bit cleared. Create interprets the serial number "as an unsigned
    // integer of arbitrary size in big-endian byte ordering", so a negative serial, which RFC 5280 4.1.2.2 forbids ("The
    // serial number MUST be a positive integer"), throws the OverflowException of BigInteger.ToByteArray(isUnsigned: true)
    // (https://learn.microsoft.com/en-us/dotnet/api/system.numerics.biginteger.tobytearray?view=net-10.0), where Bouncy
    // Castle writes it. new Time(Date.from(instant)) keeps whole seconds; Create writes the validity in whole seconds too.
    private X509Certificate2 CreateCertificate(CertificateRequest generator, X500DistinguishedName issuer, Signed signed)
    {
        BigInteger serial;
        if (this.serial.HasValue)
        {
            serial = this.serial.Value;
        }
        else
        {
            byte[] random = RandomNumberGenerator.GetBytes(20);
            random[0] &= 0x7F;
            serial = new BigInteger(random, isUnsigned: true, isBigEndian: true);
        }

        return generator.Create(issuer, signed, notBefore, notAfter,
                                serial.ToByteArray(isUnsigned: true, isBigEndian: true));
    }

    // CLR: ExtensionsGenerator is the request's CertificateExtensions collection, which Create writes in order; the
    // basic constraints extension is X509BasicConstraintsExtension(true, hasPathLengthConstraint, pathLengthConstraint,
    // critical: true), the extended key usages X509EnhancedKeyUsageExtension over the OIDs in TreeSet order, the
    // subject alternative names an X509SubjectAlternativeNameExtension over the GeneralNames SEQUENCE ("from an encoded
    // representation of the extension and an optional critical marker",
    // https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509subjectalternativenameextension.-ctor?view=net-10.0),
    // and the CRL distribution points an X509Extension over the CRLDistributionPoints SEQUENCE (2.5.29.31). The
    // UncheckedIOException wrapping and setExtensions(generator.generate()) have no counterpart.
    private void AddExtensions(CertificateRequest tbsCert)
    {
        Collection<X509Extension> generator = tbsCert.CertificateExtensions;

        if (isCertificateAuthority)
        {
            X509BasicConstraintsExtension basicConstraints;

            if (pathLengthConstraint.HasValue)
            {
                basicConstraints = new X509BasicConstraintsExtension(true, true, pathLengthConstraint.Value, true);
            }
            else
            {
                basicConstraints = new X509BasicConstraintsExtension(true, false, 0, true);
            }

            generator.Add(basicConstraints);
        }

        if (keyUsage != null)
        {
            generator.Add(keyUsage);
        }

        if (extendedKeyUsage.Count != 0)
        {
            OidCollection usages = new OidCollection();

            foreach (string usage in extendedKeyUsage)
            {
                usages.Add(new Oid(usage));
            }

            generator.Add(new X509EnhancedKeyUsageExtension(usages, false));
        }

        if (subjectAlternativeNames.Count != 0)
        {
            // SAN is critical extension if subject is empty sequence:
            bool critical = subject.Name.Length == 0;
            AsnWriter generalNames = new AsnWriter(AsnEncodingRules.DER);

            using (generalNames.PushSequence())
            {
                foreach (byte[] name in subjectAlternativeNames)
                {
                    generalNames.WriteEncodedValue(name);
                }
            }

            generator.Add(new X509SubjectAlternativeNameExtension(generalNames.Encode(), critical));
        }

        if (crlDistributionPoints.Count != 0)
        {
            AsnWriter distPoints = new AsnWriter(AsnEncodingRules.DER);

            using (distPoints.PushSequence())
            {
                foreach (byte[] distributionPoint in crlDistributionPoints)
                {
                    distPoints.WriteEncodedValue(distributionPoint);
                }
            }

            generator.Add(new X509Extension("2.5.29.31", distPoints.Encode(), false));
        }

        foreach (BuilderCallback callback in modifierCallbacks)
        {
            callback(generator);
        }
    }

    /**
     * The {@link Algorithm} enum encapsulates both the key type, key generation parameters, and the signature
     * algorithm to use.
     */
    // CLR: a Java enum with fields, a constructor and methods is a sealed class whose constants are static
    // readonly instances, as codec-base's Base64Dialect. The members java.lang.Enum supplies and the tests use are
    // kept: Name() and ToString() return the constant name, and Values() returns a new array in declaration order
    // (JLS 8.9.3, https://docs.oracle.com/javase/specs/jls/se17/html/jls-8.html#jls-8.9.3). The
    // AlgorithmParameterSpec is the BCL parameter object of the key type:
    // ECGenParameterSpec("secp256r1"/"secp384r1") is ECCurve.NamedCurves.nistP256/nistP384 (RFC 5480 2.1.1.1:
    // "[FIPS186-3] refers to... secp256r1 as P-256, secp384r1 as P-384", https://www.rfc-editor.org/rfc/rfc5480),
    // RSAKeyGenParameterSpec(bits, F4) is the key size, because RSA.Create(int) generates keys with the public
    // exponent 65537 that F4 names (measured on this host,
    // https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/security/spec/RSAKeyGenParameterSpec.html),
    // and the named parameter specs are the MLDsaAlgorithm, MLKemAlgorithm and SlhDsaAlgorithm values (see
    // NamedParameterSpec). The fields are internal because the enclosing CertificateBuilder reads them: a C#
    // private member is limited to its containing type ("Access is limited to the containing type",
    // https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/accessibility-levels), while
    // Java lets the enclosing class read it.
    public sealed class Algorithm
    {
        /**
         * The NIST P-256 elliptic curve algorithm, offer fast key generation, signing, and verification,
         * with small keys and signatures, at 128-bits of security strength.
         * <p>
         * This algorithm is older than the Edwards curves, and are more widely supported.
         */
        public static readonly Algorithm ecp256 = new Algorithm(nameof(ecp256), "EC", ECCurve.NamedCurves.nistP256, "SHA256withECDSA");
        /**
         * The NIST P-384 elliptic curve algorithm, offer fast key generation, signing, and verification,
         * with small keys and signatures, at 192-bits of security strength.
         * <p>
         * This algorithm is older than the Edwards curves, and are more widely supported.
         */
        public static readonly Algorithm ecp384 = new Algorithm(nameof(ecp384), "EC", ECCurve.NamedCurves.nistP384, "SHA384withECDSA");
        /**
         * The 2048-bit RSA algorithm offer roughly 112-bits of security strength, at the cost of large keys
         * and slightly expensive key generation.
         * <p>
         * This algorithm enjoy the widest support and compatibility, though.
         */
        public static readonly Algorithm rsa2048 = new Algorithm(nameof(rsa2048), "RSA", 2048, "SHA256withRSA");
        /**
         * The 3072-bit RSA algorithm offer roughly 128-bits of security strength, at the cost of large keys
         * and fairly expensive key generation.
         * <p>
         * RSA enjoy pretty wide compatibility, though not all systems support keys this large.
         */
        public static readonly Algorithm rsa3072 = new Algorithm(nameof(rsa3072), "RSA", 3072, "SHA256withRSA");
        /**
         * The 4096-bit RSA algorithm offer roughly greater than 128-bits of security strength,
         * at the cost of large keys and very expensive key generation.
         * <p>
         * RSA enjoy pretty wide compatibility, though not all systems support keys this large.
         */
        public static readonly Algorithm rsa4096 = new Algorithm(nameof(rsa4096), "RSA", 4096, "SHA384withRSA");
        /**
         * The 8192-bit RSA algorithm offer roughly greater than 192-bits of security strength,
         * at the cost of very large keys and extremely expensive key generation.
         * <p>
         * RSA enjoy pretty wide compatibility, though not all systems support keys this large.
         */
        public static readonly Algorithm rsa8192 = new Algorithm(nameof(rsa8192), "RSA", 8192, "SHA384withRSA");
        /**
         * The Ed25519 algorithm offer fast key generation, signing, and verification,
         * with very small keys and signatures, at 128-bits of security strength.
         * <p>
         * This algorithm was added in Java 15, and may not be supported everywhere.
         */
        public static readonly Algorithm ed25519 = new Algorithm(nameof(ed25519), "Ed25519", NamedParameterSpec("Ed25519"), "Ed25519");
        /**
         * The Ed448 algorithm offer fast key generation, signing, and verification,
         * with small keys and signatures, at 224-bits of security strength.
         * <p>
         * This algorithm was added in Java 15, and may not be supported everywhere.
         */
        public static readonly Algorithm ed448 = new Algorithm(nameof(ed448), "Ed448", NamedParameterSpec("Ed448"), "Ed448");
        /**
         * The ML-DSA-44 algorithm is the NIST FIPS 204 version of the post-quantum Dilithium algorithm.
         * It has 128-bits of classical security strength, and is claimed to meet NIST Level 2
         * quantum security strength (equivalent to finding a SHA-256 collision).
         * <p>
         * This algorithm was added in Java 24, and may not be supported everywhere.
         */
        public static readonly Algorithm mlDsa44 = new Algorithm(nameof(mlDsa44), "ML-DSA", NamedParameterSpec("ML-DSA-44"), "ML-DSA-44");
        /**
         * The ML-DSA-65 algorithm is the NIST FIPS 204 version of the post-quantum Dilithium algorithm.
         * It has 192-bits of classical security strength, and is claimed to meet NIST Level 3
         * quantum security strength (equivalent to finding the key for an AES-192 block).
         * <p>
         * This algorithm was added in Java 24, and may not be supported everywhere.
         */
        public static readonly Algorithm mlDsa65 = new Algorithm(nameof(mlDsa65), "ML-DSA", NamedParameterSpec("ML-DSA-65"), "ML-DSA-65");
        /**
         * The ML-DSA-87 algorithm is the NIST FIPS 204 version of the post-quantum Dilithium algorithm.
         * It has 256-bits of classical security strength, and is claimed to meet NIST Level 5
         * quantum security strength (equivalent to finding the key for an AES-256 block).
         * <p>
         * This algorithm was added in Java 24, and may not be supported everywhere.
         */
        public static readonly Algorithm mlDsa87 = new Algorithm(nameof(mlDsa87), "ML-DSA", NamedParameterSpec("ML-DSA-87"), "ML-DSA-87");
        /**
         * The ML-KEM-512 algorithm is the NIST FIPS 203 version of the post-quantum Kyber algorithm.
         * It has 128-bits of classical security strength, and is claimed to meet NIST Level 1
         * quantum security strength (equivalent to finding the key for an AES-128 block).
         * <p>
         * This algorithm was added in Java 24, and may not be supported everywhere.
         */
        public static readonly Algorithm mlKem512 = new Algorithm(nameof(mlKem512), "ML-KEM", NamedParameterSpec("ML-KEM-512"), UNSUPPORTED_SIGN);
        /**
         * The ML-KEM-768 algorithm is the NIST FIPS 203 version of the post-quantum Kyber algorithm.
         * It has 192-bits of classical security strength, and is claimed to meet NIST Level 3
         * quantum security strength (equivalent to finding the key for an AES-192 block).
         * <p>
         * This algorithm was added in Java 24, and may not be supported everywhere.
         */
        public static readonly Algorithm mlKem768 = new Algorithm(nameof(mlKem768), "ML-KEM", NamedParameterSpec("ML-KEM-768"), UNSUPPORTED_SIGN);
        /**
         * The ML-KEM-1024 algorithm is the NIST FIPS 203 version of the post-quantum Kyber algorithm.
         * It has 256-bits of classical security strength, and is claimed to meet NIST Level 5
         * quantum security strength (equivalent to finding the key for an AES-256 block).
         * <p>
         * This algorithm was added in Java 24, and may not be supported everywhere.
         */
        public static readonly Algorithm mlKem1024 = new Algorithm(nameof(mlKem1024), "ML-KEM", NamedParameterSpec("ML-KEM-1024"), UNSUPPORTED_SIGN);
        /**
         * The SLH-DSA-SHA2-128s algorithm is the NIST FIPS 205 of the post-quantum SPHINCS+ algorithm.
         * It has 128-bits of classical security strength, and is claimed to meet NIST Level 1
         * quantum security strength (equivalent to finding the key for an AES-128 block).
         * <p>
         * SLH-DSA algorithms with the 's' suffix have relatively smaller signatures but are much slower.
         */
        public static readonly Algorithm slhDsaSha2_128s = new Algorithm(nameof(slhDsaSha2_128s), "SLH-DSA", NamedParameterSpec("SLH-DSA-SHA2-128s"), "SLH-DSA-SHA2-128s");
        /**
         * The SLH-DSA-SHA2-128f algorithm is the NIST FIPS 205 of the post-quantum SPHINCS+ algorithm.
         * It has 128-bits of classical security strength, and is claimed to meet NIST Level 1
         * quantum security strength (equivalent to finding the key for an AES-128 block).
         * <p>
         * SLH-DSA algorithms with the 'f' suffix have larger signatures but are much faster.
         */
        public static readonly Algorithm slhDsaSha2_128f = new Algorithm(nameof(slhDsaSha2_128f), "SLH-DSA", NamedParameterSpec("SLH-DSA-SHA2-128f"), "SLH-DSA-SHA2-128f");
        /**
         * The SLH-DSA-SHAKE-128s algorithm is the NIST FIPS 205 of the post-quantum SPHINCS+ algorithm.
         * It has 128-bits of classical security strength, and is claimed to meet NIST Level 1
         * quantum security strength (equivalent to finding the key for an AES-128 block).
         * <p>
         * SLH-DSA algorithms with the 's' suffix have relatively smaller signatures but are much slower.
         */
        public static readonly Algorithm slhDsaShake_128s = new Algorithm(nameof(slhDsaShake_128s), "SLH-DSA", NamedParameterSpec("SLH-DSA-SHAKE-128s"), "SLH-DSA-SHAKE-128s");
        /**
         * The SLH-DSA-SHAKE-128f algorithm is the NIST FIPS 205 of the post-quantum SPHINCS+ algorithm.
         * It has 128-bits of classical security strength, and is claimed to meet NIST Level 1
         * quantum security strength (equivalent to finding the key for an AES-128 block).
         * <p>
         * SLH-DSA algorithms with the 'f' suffix have larger signatures but are much faster.
         */
        public static readonly Algorithm slhDsaShake_128f = new Algorithm(nameof(slhDsaShake_128f), "SLH-DSA", NamedParameterSpec("SLH-DSA-SHAKE-128f"), "SLH-DSA-SHAKE-128f");
        /**
         * The SLH-DSA-SHA2-192 algorithm is the NIST FIPS 205 of the post-quantum SPHINCS+ algorithm.
         * It has 192-bits of classical security strength, and is claimed to meet NIST Level 3
         * quantum security strength (equivalent to finding the key for an AES-192 block).
         * <p>
         * SLH-DSA algorithms with the 's' suffix have relatively smaller signatures but are much slower.
         */
        public static readonly Algorithm slhDsaSha2_192s = new Algorithm(nameof(slhDsaSha2_192s), "SLH-DSA", NamedParameterSpec("SLH-DSA-SHA2-192s"), "SLH-DSA-SHA2-192s");
        /**
         * The SLH-DSA-SHA2-192f algorithm is the NIST FIPS 205 of the post-quantum SPHINCS+ algorithm.
         * It has 192-bits of classical security strength, and is claimed to meet NIST Level 3
         * quantum security strength (equivalent to finding the key for an AES-192 block).
         * <p>
         * SLH-DSA algorithms with the 'f' suffix have larger signatures but are much faster.
         */
        public static readonly Algorithm slhDsaSha2_192f = new Algorithm(nameof(slhDsaSha2_192f), "SLH-DSA", NamedParameterSpec("SLH-DSA-SHA2-192f"), "SLH-DSA-SHA2-192f");
        /**
         * The SLH-DSA-SHAKE-192s algorithm is the NIST FIPS 205 of the post-quantum SPHINCS+ algorithm.
         * It has 192-bits of classical security strength, and is claimed to meet NIST Level 3
         * quantum security strength (equivalent to finding the key for an AES-192 block).
         * <p>
         * SLH-DSA algorithms with the 's' suffix have relatively smaller signatures but are much slower.
         */
        public static readonly Algorithm slhDsaShake_192s = new Algorithm(nameof(slhDsaShake_192s), "SLH-DSA", NamedParameterSpec("SLH-DSA-SHAKE-192s"), "SLH-DSA-SHAKE-192s");
        /**
         * The SLH-DSA-SHAKE-192f algorithm is the NIST FIPS 205 of the post-quantum SPHINCS+ algorithm.
         * It has 192-bits of classical security strength, and is claimed to meet NIST Level 3
         * quantum security strength (equivalent to finding the key for an AES-192 block).
         * <p>
         * SLH-DSA algorithms with the 'f' suffix have larger signatures but are much faster.
         */
        public static readonly Algorithm slhDsaShake_192f = new Algorithm(nameof(slhDsaShake_192f), "SLH-DSA", NamedParameterSpec("SLH-DSA-SHAKE-192f"), "SLH-DSA-SHAKE-192f");
        /**
         * The SLH-DSA-SHA2-256s algorithm is the NIST FIPS 205 of the post-quantum SPHINCS+ algorithm.
         * It has 256-bits of classical security strength, and is claimed to meet NIST Level 5
         * quantum security strength (equivalent to finding the key for an AES-256 block).
         * <p>
         * SLH-DSA algorithms with the 's' suffix have relatively smaller signatures but are much slower.
         */
        public static readonly Algorithm slhDsaSha2_256s = new Algorithm(nameof(slhDsaSha2_256s), "SLH-DSA", NamedParameterSpec("SLH-DSA-SHA2-256s"), "SLH-DSA-SHA2-256s");
        /**
         * The SLH-DSA-SHA2-256f algorithm is the NIST FIPS 205 of the post-quantum SPHINCS+ algorithm.
         * It has 256-bits of classical security strength, and is claimed to meet NIST Level 5
         * quantum security strength (equivalent to finding the key for an AES-256 block).
         * <p>
         * SLH-DSA algorithms with the 'f' suffix have larger signatures but are much faster.
         */
        public static readonly Algorithm slhDsaSha2_256f = new Algorithm(nameof(slhDsaSha2_256f), "SLH-DSA", NamedParameterSpec("SLH-DSA-SHA2-256f"), "SLH-DSA-SHA2-256f");
        /**
         * The SLH-DSA-SHAKE-256s algorithm is the NIST FIPS 205 of the post-quantum SPHINCS+ algorithm.
         * It has 256-bits of classical security strength, and is claimed to meet NIST Level 5
         * quantum security strength (equivalent to finding the key for an AES-256 block).
         * <p>
         * SLH-DSA algorithms with the 's' suffix have relatively smaller signatures but are much slower.
         */
        public static readonly Algorithm slhDsaShake_256s = new Algorithm(nameof(slhDsaShake_256s), "SLH-DSA", NamedParameterSpec("SLH-DSA-SHAKE-256s"), "SLH-DSA-SHAKE-256s");
        /**
         * The SLH-DSA-SHAKE-256f algorithm is the NIST FIPS 205 of the post-quantum SPHINCS+ algorithm.
         * It has 256-bits of classical security strength, and is claimed to meet NIST Level 5
         * quantum security strength (equivalent to finding the key for an AES-256 block).
         * <p>
         * SLH-DSA algorithms with the 'f' suffix have larger signatures but are much faster.
         */
        public static readonly Algorithm slhDsaShake_256f = new Algorithm(nameof(slhDsaShake_256f), "SLH-DSA", NamedParameterSpec("SLH-DSA-SHAKE-256f"), "SLH-DSA-SHAKE-256f");

        private static readonly Algorithm[] VALUES =
        {
            ecp256,
            ecp384,
            rsa2048,
            rsa3072,
            rsa4096,
            rsa8192,
            ed25519,
            ed448,
            mlDsa44,
            mlDsa65,
            mlDsa87,
            mlKem512,
            mlKem768,
            mlKem1024,
            slhDsaSha2_128s,
            slhDsaSha2_128f,
            slhDsaShake_128s,
            slhDsaShake_128f,
            slhDsaSha2_192s,
            slhDsaSha2_192f,
            slhDsaShake_192s,
            slhDsaShake_192f,
            slhDsaSha2_256s,
            slhDsaSha2_256f,
            slhDsaShake_256s,
            slhDsaShake_256f,
        };

        internal readonly string keyType;
        internal readonly object parameterSpec;
        internal readonly string signatureType;
        private readonly string name;

        private Algorithm(string name, string keyType, object parameterSpec, string signatureType)
        {
            this.name = name;
            this.keyType = keyType;
            this.parameterSpec = parameterSpec;
            this.signatureType = signatureType;
        }

        // CLR: java.security.spec.NamedParameterSpec (looked up reflectively, Java 11+) is the BCL algorithm object of the
        // name when the running platform supports the algorithm (MLDsa.IsSupported, MLKem.IsSupported,
        // SlhDsa.IsSupported: "Gets a value indicating whether the current platform supports ML-DSA",
        // https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.mldsa?view=net-10.0), and
        // UNSUPPORTED_SPEC otherwise. The BCL has no Ed25519 or Ed448 type, and the Bouncy Castle EdDSAParameterSpec
        // fallback is Bouncy Castle, so ed25519 and ed448 are always UNSUPPORTED_SPEC: isSupported() reports false and
        // algorithm(...) rejects them, as Java does for a spec it cannot create.
        private static object NamedParameterSpec(string name)
        {
            if (MLDsa.IsSupported)
            {
                foreach (MLDsaAlgorithm mlDsa in new[] { MLDsaAlgorithm.MLDsa44, MLDsaAlgorithm.MLDsa65, MLDsaAlgorithm.MLDsa87 })
                {
                    if (mlDsa.Name == name)
                    {
                        return mlDsa;
                    }
                }
            }

            if (MLKem.IsSupported)
            {
                foreach (MLKemAlgorithm mlKem in new[] { MLKemAlgorithm.MLKem512, MLKemAlgorithm.MLKem768, MLKemAlgorithm.MLKem1024 })
                {
                    if (mlKem.Name == name)
                    {
                        return mlKem;
                    }
                }
            }
#pragma warning disable SYSLIB5006 // SlhDsa is experimental; see Algorithms.
            if (SlhDsa.IsSupported)
            {
                foreach (SlhDsaAlgorithm slhDsa in new[]
                         {
                             SlhDsaAlgorithm.SlhDsaSha2_128s, SlhDsaAlgorithm.SlhDsaSha2_128f,
                             SlhDsaAlgorithm.SlhDsaShake128s, SlhDsaAlgorithm.SlhDsaShake128f,
                             SlhDsaAlgorithm.SlhDsaSha2_192s, SlhDsaAlgorithm.SlhDsaSha2_192f,
                             SlhDsaAlgorithm.SlhDsaShake192s, SlhDsaAlgorithm.SlhDsaShake192f,
                             SlhDsaAlgorithm.SlhDsaSha2_256s, SlhDsaAlgorithm.SlhDsaSha2_256f,
                             SlhDsaAlgorithm.SlhDsaShake256s, SlhDsaAlgorithm.SlhDsaShake256f,
                         })
                {
                    if (slhDsa.Name == name)
                    {
                        return slhDsa;
                    }
                }
            }
#pragma warning restore SYSLIB5006
            return UNSUPPORTED_SPEC;
        }

        /**
         * Generate a new {@link KeyPair} using this algorithm, and the given {@link SecureRandom} generator.
         * @param secureRandom The {@link SecureRandom} generator to use, not {@code null}.
         * @return The generated {@link KeyPair}.
         * @throws GeneralSecurityException if the key pair cannot be generated using this algorithm for some reason.
         * @throws UnsupportedOperationException if this algorithm is not support in the current JVM.
         */
        // CLR: the BCL key generators (RSA.Create(int), ECDsa.Create(ECCurve), MLDsa/MLKem/SlhDsa.GenerateKey) take no
        // random source and no provider (see Algorithms.KeyPairGenerator), so the
        // SecureRandom parameter, its requireNonNull check and the generateKeyPair(SecureRandom, Provider) overload are
        // not ported. The key pair is the BCL key object (see Algorithms); GeneralSecurityException is
        // CryptographicException and UnsupportedOperationException NotSupportedException.
        public object GenerateKeyPair()
        {
            if (parameterSpec == UNSUPPORTED_SPEC)
            {
                throw new NotSupportedException("This algorithm is not supported: " + this);
            }

            Func<object> keyGen = Algorithms.KeyPairGenerator(keyType, parameterSpec);
            return keyGen();
        }

        /**
         * Tell whether this algorithm is supported in the current JVM.
         * @return {@code true} if this algorithm is supported.
         */
        public bool IsSupported()
        {
            return parameterSpec != UNSUPPORTED_SPEC;
        }

        /**
         * Discern if this algorithm can be used for signing.
         * Algorithms need to support signing in order to create self-signed certificates,
         * or to be used as signing issuers of other certificates.
         * <p>
         * Note that this method only inspects a property of the algorithm, and does not check if the algorithm
         * {@linkplain #isSupported() is supported} in your environment.
         *
         * @return {@code true} if this algorithm can be used for signing, otherwise {@code false}.
         */
        public bool SupportSigning()
        {
            return !Equals(signatureType, UNSUPPORTED_SIGN);
        }

        public string Name()
        {
            return name;
        }

        public override string ToString()
        {
            return name;
        }

        public static Algorithm[] Values()
        {
            return (Algorithm[]) VALUES.Clone();
        }
    }

    /**
     * The key usage field specify what the certificate and key is allowed to be used for.
     * <p>
     * These key usages are specified by the X.509 standard, and some of them are deprecated.
     * <p>
     * See the {@link ExtendedKeyUsage} for other commonly used key usage extensions.
     * <p>
     * See ITU-T X.509 (10/2019) section 9.2.2.3 for the precise meaning of these usages.
     */
    // CLR: a Java enum with a field and a constructor is a sealed class with static readonly instances (see Algorithm);
    // bitId is internal for SetKeyUsage, as Algorithm's fields are.
    public sealed class KeyUsage
    {
        /**
         * For verifying digital signatures, for entity authentication,
         * for entity authentication, or for integrity verification.
         */
        public static readonly KeyUsage digitalSignature = new KeyUsage(nameof(digitalSignature), 0);
        /**
         * This key usage is deprecated by X.509, and commitment may instead be derived from the actual use of the keys.
         * <p>
         * For verifying digital signatures that imply the signer has "committed" to the
         * content being signed. This does not imply any specific policy or review on part of the signer, however.
         */
        public static readonly KeyUsage contentCommitment = new KeyUsage(nameof(contentCommitment), 1);
        /**
         * For enciphering keys or other security information.
         */
        public static readonly KeyUsage keyEncipherment = new KeyUsage(nameof(keyEncipherment), 2);
        /**
         * For enciphering user data, but not keys or security information.
         */
        public static readonly KeyUsage dataEncipherment = new KeyUsage(nameof(dataEncipherment), 3);
        /**
         * For use in public key agreement.
         */
        public static readonly KeyUsage keyAgreement = new KeyUsage(nameof(keyAgreement), 4);
        /**
         * For verifying the Certificate Authority's signature on a public-key certificate.
         * <p>
         * This implies {@link #digitalSignature} and {@link #contentCommitment}, so they do not need to be specified
         * separately.
         */
        public static readonly KeyUsage keyCertSign = new KeyUsage(nameof(keyCertSign), 5);
        /**
         * For verifying the Certificate Authority's signature on a Certificate Revocation List.
         * <p>
         * This implies {@link #digitalSignature} and {@link #contentCommitment}, so they do not need to be specified
         * separately.
         */
        public static readonly KeyUsage cRLSign = new KeyUsage(nameof(cRLSign), 6);
        /**
         * For use with {@link #keyAgreement} to limit the key to enciphering only.
         * <p>
         * The meaning of this without the {@link #keyAgreement} bit set is unspecified.
         */
        public static readonly KeyUsage encipherOnly = new KeyUsage(nameof(encipherOnly), 7);
        /**
         * For use with {@link #keyAgreement} to limit the key to deciphering only.
         * <p>
         * The meaning of this without the {@link #keyAgreement} bit set is unspecified.
         */
        public static readonly KeyUsage decipherOnly = new KeyUsage(nameof(decipherOnly), 8);

        private static readonly KeyUsage[] VALUES =
        {
            digitalSignature,
            contentCommitment,
            keyEncipherment,
            dataEncipherment,
            keyAgreement,
            keyCertSign,
            cRLSign,
            encipherOnly,
            decipherOnly,
        };

        internal readonly int bitId;
        private readonly string name;

        private KeyUsage(string name, int bitId)
        {
            this.name = name;
            this.bitId = bitId;
        }

        public string Name()
        {
            return name;
        }

        public override string ToString()
        {
            return name;
        }

        public static KeyUsage[] Values()
        {
            return (KeyUsage[]) VALUES.Clone();
        }
    }

    /**
     * The extended key usage field specify what the certificate and key is allowed to be used for.
     * <p>
     * A certificate can have many key usages. For instance, some certificates support both client and server usage
     * for TLS connections.
     * <p>
     * The key usage must be checked by the opposing peer receiving the certificate, and reject certificates that do
     * not permit the given usage.
     * <p>
     * For instance, if a TLS client connects to a server that presents a certificate without the
     * {@linkplain #PKIX_KP_SERVER_AUTH server-authentication} usage, then the client must reject the server
     * certificate as invalid.
     */
    // CLR: a Java enum with a field, a constructor and a method is a sealed class with static readonly instances (see
    // Algorithm).
    public sealed class ExtendedKeyUsage
    {
        /**
         * The certificate can be used on the server-side of a TLS connection.
         */
        public static readonly ExtendedKeyUsage PKIX_KP_SERVER_AUTH = new ExtendedKeyUsage(nameof(PKIX_KP_SERVER_AUTH), OID_PKIX_KP_SERVER_AUTH);
        /**
         * The certificate can be used on the client-side of a TLS connection.
         */
        public static readonly ExtendedKeyUsage PKIX_KP_CLIENT_AUTH = new ExtendedKeyUsage(nameof(PKIX_KP_CLIENT_AUTH), OID_PKIX_KP_CLIENT_AUTH);
        /**
         * The certificate can be used for code signing.
         */
        public static readonly ExtendedKeyUsage PKIX_KP_CODE_SIGNING = new ExtendedKeyUsage(nameof(PKIX_KP_CODE_SIGNING), OID_PKIX_KP_CODE_SIGNING);
        /**
         * The certificate can be used for protecting email.
         */
        public static readonly ExtendedKeyUsage PKIX_KP_EMAIL_PROTECTION = new ExtendedKeyUsage(nameof(PKIX_KP_EMAIL_PROTECTION), OID_PKIX_KP_EMAIL_PROTECTION);
        /**
         * The certificate can be used for time-stamping.
         */
        public static readonly ExtendedKeyUsage PKIX_KP_TIME_STAMPING = new ExtendedKeyUsage(nameof(PKIX_KP_TIME_STAMPING), OID_PKIX_KP_TIME_STAMPING);
        /**
         * The certificate can be used to sign OCSP replies.
         */
        public static readonly ExtendedKeyUsage PKIX_KP_OCSP_SIGNING = new ExtendedKeyUsage(nameof(PKIX_KP_OCSP_SIGNING), OID_PKIX_KP_OCSP_SIGNING);
        /**
         * The certificate can be used for Kerberos client authentication.
         */
        public static readonly ExtendedKeyUsage KERBEROS_KEY_PURPOSE_CLIENT_AUTH = new ExtendedKeyUsage(nameof(KERBEROS_KEY_PURPOSE_CLIENT_AUTH), OID_KERBEROS_KEY_PURPOSE_CLIENT_AUTH);
        /**
         * The certificate can be used for Microsoft smartcard logins.
         */
        public static readonly ExtendedKeyUsage MICROSOFT_SMARTCARD_LOGIN = new ExtendedKeyUsage(nameof(MICROSOFT_SMARTCARD_LOGIN), OID_MICROSOFT_SMARTCARD_LOGIN);

        private static readonly ExtendedKeyUsage[] VALUES =
        {
            PKIX_KP_SERVER_AUTH,
            PKIX_KP_CLIENT_AUTH,
            PKIX_KP_CODE_SIGNING,
            PKIX_KP_EMAIL_PROTECTION,
            PKIX_KP_TIME_STAMPING,
            PKIX_KP_OCSP_SIGNING,
            KERBEROS_KEY_PURPOSE_CLIENT_AUTH,
            MICROSOFT_SMARTCARD_LOGIN,
        };

        private readonly string oid;
        private readonly string name;

        private ExtendedKeyUsage(string name, string oid)
        {
            this.name = name;
            this.oid = oid;
        }

        public string GetOid()
        {
            return oid;
        }

        public string Name()
        {
            return name;
        }

        public override string ToString()
        {
            return name;
        }

        public static ExtendedKeyUsage[] Values()
        {
            return (ExtendedKeyUsage[]) VALUES.Clone();
        }
    }

    // CLR: the private @FunctionalInterface is a delegate, as codec-base turns functional interfaces into
    // delegates; it is internal because the package-private modifierCallbacks field holds it (CS0052), and it
    // modifies the request's extension collection. C# has no throws clause.
    internal delegate void BuilderCallback(Collection<X509Extension> builder);
}
