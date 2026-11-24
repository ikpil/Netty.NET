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
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Netty.NET.Common;

namespace Netty.NET.PkiTesting;

/**
 * Encodes a limited set of GeneralName types, no decoding is supported.
 * See ITU-T X.509 (10/2019) Section 9.3.2.1, or RFC 5280 Section 4.2.1.6.
 */
// CLR: the BCL has no GeneralName type, so each method returns the DER encoding of one GeneralName (RFC 5280 4.2.1.6,
// https://www.rfc-editor.org/rfc/rfc5280), which CertificateBuilder joins into a GeneralNames SEQUENCE in the order the
// names were added. SubjectAlternativeNameBuilder encodes the dNSName, rfc822Name, uniformResourceIdentifier and
// iPAddress names; on .NET 10 it has no method for directory names, registered IDs or arbitrary other names (its Add
// methods are AddDnsName, AddEmailAddress, AddIpAddress, AddUri and AddUserPrincipalName,
// https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.subjectalternativenamebuilder?view=net-10.0,
// measured on .NET 10.0.8), so AsnWriter writes those
// (https://learn.microsoft.com/en-us/dotnet/api/system.formats.asn1.asnwriter?view=net-10.0). The Bouncy Castle
// IOException wrapped in UncheckedIOException has no counterpart: AsnWriter reports a value that is not a single valid
// DER element as an ArgumentException.
internal static class GeneralNameUtils
{
    internal static byte[] OtherName(string oid, byte[] value)
    {
        // CLR: new GeneralName(otherName, SEQUENCE { oid, [0] EXPLICIT value }) is the [0] IMPLICIT OtherName SEQUENCE.
        AsnWriter wrappedValue = new AsnWriter(AsnEncodingRules.DER);
        using (wrappedValue.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
        {
            wrappedValue.WriteObjectIdentifier(oid);
            using (wrappedValue.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
            {
                wrappedValue.WriteEncodedValue(value);
            }
        }
        return wrappedValue.Encode();
    }

    internal static byte[] Rfc822Name(string emailAddress)
    {
        SubjectAlternativeNameBuilder builder = new SubjectAlternativeNameBuilder();
        builder.AddEmailAddress(emailAddress);
        return SingleName(builder);
    }

    internal static byte[] DnsName(string dnsName)
    {
        // CLR: URI.create("ip://" + dnsName).getHost() is new Uri("ip://" + dnsName).Host; System.Uri lower-cases the
        // host (DNS names compare case-insensitively) and rejects a host that java.net.URI parses as a registry-based
        // authority (getHost() null), such as a wildcard, with a UriFormatException.
        Uri uri = new Uri("ip://" + dnsName);
        string host = uri.Host;
        SubjectAlternativeNameBuilder builder = new SubjectAlternativeNameBuilder();
        builder.AddDnsName(host);
        return SingleName(builder);
    }

    internal static byte[] DirectoryName(string x500Name)
    {
        return DirectoryName(new X500DistinguishedName(x500Name));
    }

    internal static byte[] DirectoryName(X500DistinguishedName name)
    {
        // CLR: directoryName is [4] EXPLICIT Name, because Name is a CHOICE.
        AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 4)))
        {
            writer.WriteEncodedValue(name.RawData);
        }
        return writer.Encode();
    }

    // CLR: new URI(uri) throws URISyntaxException; new Uri(uri) throws UriFormatException.
    internal static byte[] UriName(string uri)
    {
        return UriName(new Uri(uri));
    }

    // CLR: uri.toASCIIString() is the URI text SubjectAlternativeNameBuilder.AddUri writes, the escaped Uri.AbsoluteUri
    // (measured: "spiffe://netty.io/ex ample/sän" is written as spiffe://netty.io/ex%20ample/s%C3%A4n, as
    // toASCIIString() quotes it).
    internal static byte[] UriName(Uri uri)
    {
        SubjectAlternativeNameBuilder builder = new SubjectAlternativeNameBuilder();
        builder.AddUri(uri);
        return SingleName(builder);
    }

    internal static byte[] IpAddress(string ipAddress)
    {
        if (!NetUtil.IsValidIpV4Address(ipAddress) && !NetUtil.IsValidIpV6Address(ipAddress))
        {
            throw new ArgumentException("Not a valid IP address: " + ipAddress);
        }
        // CLR: Bouncy Castle parses the text into the address octets; NetUtil.CreateByteArrayFromIpAddressString does so
        // with the rules of the validation above.
        SubjectAlternativeNameBuilder builder = new SubjectAlternativeNameBuilder();
        builder.AddIpAddress(new IPAddress(NetUtil.CreateByteArrayFromIpAddressString(ipAddress)));
        return SingleName(builder);
    }

    internal static byte[] RegisteredId(string oid)
    {
        // CLR: registeredID is [8] IMPLICIT OBJECT IDENTIFIER.
        AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);
        writer.WriteObjectIdentifier(oid, new Asn1Tag(TagClass.ContextSpecific, 8));
        return writer.Encode();
    }

    // CLR: the single GeneralName of a one-name SubjectAlternativeNameBuilder extension (GeneralNames SEQUENCE).
    private static byte[] SingleName(SubjectAlternativeNameBuilder builder)
    {
        AsnReader names = new AsnReader(builder.Build().RawData, AsnEncodingRules.DER).ReadSequence();
        return names.ReadEncodedValue().ToArray();
    }
}
