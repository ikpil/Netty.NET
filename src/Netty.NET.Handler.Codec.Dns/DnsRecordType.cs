/*
 * Copyright 2015 The Netty Project
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
using System.Text;

namespace Netty.NET.Handler.Codec.Dns;

/**
 * Represents a DNS record type.
 */
// CLR: Comparable<DnsRecordType> is IComparable<DnsRecordType>; hashCode()/equals(Object)/toString() are the BCL
// GetHashCode()/Equals(object)/ToString(), and name()/intValue() stay methods. The static initializer is the static
// constructor, which runs after the static field initializers, as the Java static initializer runs after the
// preceding field initializers. Numbers in text use the invariant culture (Int32.ToString() otherwise uses "the
// NumberFormatInfo object of the current culture", https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring).
public class DnsRecordType : IComparable<DnsRecordType>
{

    /**
     * Address record RFC 1035 Returns a 32-bit IPv4 address, most commonly used
     * to map hostnames to an IP address of the host, but also used for DNSBLs,
     * storing subnet masks in RFC 1101, etc.
     */
    public static readonly DnsRecordType A = new DnsRecordType(0x0001, "A");

    /**
     * Name server record RFC 1035 Delegates a DNS zone to use the given
     * authoritative name servers
     */
    public static readonly DnsRecordType NS = new DnsRecordType(0x0002, "NS");

    /**
     * Canonical name record RFC 1035 Alias of one name to another: the DNS
     * lookup will continue by retrying the lookup with the new name.
     */
    public static readonly DnsRecordType CNAME = new DnsRecordType(0x0005, "CNAME");

    /**
     * Start of [a zone of] authority record RFC 1035 and RFC 2308 Specifies
     * authoritative information about a DNS zone, including the primary name
     * server, the email of the domain administrator, the domain serial number,
     * and several timers relating to refreshing the zone.
     */
    public static readonly DnsRecordType SOA = new DnsRecordType(0x0006, "SOA");

    /**
     * Pointer record RFC 1035 Pointer to a canonical name. Unlike a CNAME, DNS
     * processing does NOT proceed, just the name is returned. The most common
     * use is for implementing reverse DNS lookups, but other uses include such
     * things as DNS-SD.
     */
    public static readonly DnsRecordType PTR = new DnsRecordType(0x000c, "PTR");

    /**
     * Mail exchange record RFC 1035 Maps a domain name to a list of message
     * transfer agents for that domain.
     */
    public static readonly DnsRecordType MX = new DnsRecordType(0x000f, "MX");

    /**
     * Text record RFC 1035 Originally for arbitrary human-readable text in a
     * DNS record. Since the early 1990s, however, this record more often
     * carries machine-readable data, such as specified by RFC 1464,
     * opportunistic encryption, Sender Policy Framework, DKIM, DMARC DNS-SD,
     * etc.
     */
    public static readonly DnsRecordType TXT = new DnsRecordType(0x0010, "TXT");

    /**
     * Responsible person record RFC 1183 Information about the responsible
     * person(s) for the domain. Usually an email address with the @ replaced by
     * a .
     */
    public static readonly DnsRecordType RP = new DnsRecordType(0x0011, "RP");

    /**
     * AFS database record RFC 1183 Location of database servers of an AFS cell.
     * This record is commonly used by AFS clients to contact AFS cells outside
     * their local domain. A subtype of this record is used by the obsolete
     * DCE/DFS file system.
     */
    public static readonly DnsRecordType AFSDB = new DnsRecordType(0x0012, "AFSDB");

    /**
     * Signature record RFC 2535 Signature record used in SIG(0) (RFC 2931) and
     * TKEY (RFC 2930). RFC 3755 designated RRSIG as the replacement for SIG for
     * use within DNSSEC.
     */
    public static readonly DnsRecordType SIG = new DnsRecordType(0x0018, "SIG");

    /**
     * key record RFC 2535 and RFC 2930 Used only for SIG(0) (RFC 2931) and TKEY
     * (RFC 2930). RFC 3445 eliminated their use for application keys and
     * limited their use to DNSSEC. RFC 3755 designates DNSKEY as the
     * replacement within DNSSEC. RFC 4025 designates IPSECKEY as the
     * replacement for use with IPsec.
     */
    public static readonly DnsRecordType KEY = new DnsRecordType(0x0019, "KEY");

    /**
     * IPv6 address record RFC 3596 Returns a 128-bit IPv6 address, most
     * commonly used to map hostnames to an IP address of the host.
     */
    public static readonly DnsRecordType AAAA = new DnsRecordType(0x001c, "AAAA");

    /**
     * Location record RFC 1876 Specifies a geographical location associated
     * with a domain name.
     */
    public static readonly DnsRecordType LOC = new DnsRecordType(0x001d, "LOC");

    /**
     * Service locator RFC 2782 Generalized service location record, used for
     * newer protocols instead of creating protocol-specific records such as MX.
     */
    public static readonly DnsRecordType SRV = new DnsRecordType(0x0021, "SRV");

    /**
     * Naming Authority Pointer record RFC 3403 Allows regular expression based
     * rewriting of domain names which can then be used as URIs, further domain
     * names to lookups, etc.
     */
    public static readonly DnsRecordType NAPTR = new DnsRecordType(0x0023, "NAPTR");

    /**
     * Key eXchanger record RFC 2230 Used with some cryptographic systems (not
     * including DNSSEC) to identify a key management agent for the associated
     * domain-name. Note that this has nothing to do with DNS Security. It is
     * Informational status, rather than being on the IETF standards-track. It
     * has always had limited deployment, but is still in use.
     */
    public static readonly DnsRecordType KX = new DnsRecordType(0x0024, "KX");

    /**
     * Certificate record RFC 4398 Stores PKIX, SPKI, PGP, etc.
     */
    public static readonly DnsRecordType CERT = new DnsRecordType(0x0025, "CERT");

    /**
     * Delegation name record RFC 2672 DNAME creates an alias for a name and all
     * its subnames, unlike CNAME, which aliases only the exact name in its
     * label. Like the CNAME record, the DNS lookup will continue by retrying
     * the lookup with the new name.
     */
    public static readonly DnsRecordType DNAME = new DnsRecordType(0x0027, "DNAME");

    /**
     * Option record RFC 2671 This is a pseudo DNS record type needed to support
     * EDNS.
     */
    public static readonly DnsRecordType OPT = new DnsRecordType(0x0029, "OPT");

    /**
     * Address Prefix List record RFC 3123 Specify lists of address ranges, e.g.
     * in CIDR format, for various address families. Experimental.
     */
    public static readonly DnsRecordType APL = new DnsRecordType(0x002a, "APL");

    /**
     * Delegation signer record RFC 4034 The record used to identify the DNSSEC
     * signing key of a delegated zone.
     */
    public static readonly DnsRecordType DS = new DnsRecordType(0x002b, "DS");

    /**
     * SSH Public Key Fingerprint record RFC 4255 Resource record for publishing
     * SSH public host key fingerprints in the DNS System, in order to aid in
     * verifying the authenticity of the host. RFC 6594 defines ECC SSH keys and
     * SHA-256 hashes. See the IANA SSHFP RR parameters registry for details.
     */
    public static readonly DnsRecordType SSHFP = new DnsRecordType(0x002c, "SSHFP");

    /**
     * IPsec Key record RFC 4025 Key record that can be used with IPsec.
     */
    public static readonly DnsRecordType IPSECKEY = new DnsRecordType(0x002d, "IPSECKEY");

    /**
     * DNSSEC signature record RFC 4034 Signature for a DNSSEC-secured record
     * set. Uses the same format as the SIG record.
     */
    public static readonly DnsRecordType RRSIG = new DnsRecordType(0x002e, "RRSIG");

    /**
     * Next-Secure record RFC 4034 Part of DNSSEC, used to prove a name does not
     * exist. Uses the same format as the (obsolete) NXT record.
     */
    public static readonly DnsRecordType NSEC = new DnsRecordType(0x002f, "NSEC");

    /**
     * DNS Key record RFC 4034 The key record used in DNSSEC. Uses the same
     * format as the KEY record.
     */
    public static readonly DnsRecordType DNSKEY = new DnsRecordType(0x0030, "DNSKEY");

    /**
     * DHCP identifier record RFC 4701 Used in conjunction with the FQDN option
     * to DHCP.
     */
    public static readonly DnsRecordType DHCID = new DnsRecordType(0x0031, "DHCID");

    /**
     * NSEC record version 3 RFC 5155 An extension to DNSSEC that allows proof
     * of nonexistence for a name without permitting zonewalking.
     */
    public static readonly DnsRecordType NSEC3 = new DnsRecordType(0x0032, "NSEC3");

    /**
     * NSEC3 parameters record RFC 5155 Parameter record for use with NSEC3.
     */
    public static readonly DnsRecordType NSEC3PARAM = new DnsRecordType(0x0033, "NSEC3PARAM");

    /**
     * TLSA certificate association record RFC 6698 A record for DNS-based
     * Authentication of Named Entities (DANE). RFC 6698 defines The TLSA DNS
     * resource record is used to associate a TLS server certificate or public
     * key with the domain name where the record is found, thus forming a 'TLSA
     * certificate association'.
     */
    public static readonly DnsRecordType TLSA = new DnsRecordType(0x0034, "TLSA");

    /**
     * S/MIME certificate association record. Uses the same RDATA format as {@link #TLSA}, but
     * associates an S/MIME certificate with an email address. RFC 8162 is Experimental rather than
     * standards-track, but the code point is assigned and the record is seen on the wire.
     * See <a href="https://www.rfc-editor.org/rfc/rfc8162.html">RFC 8162</a>.
     */
    public static readonly DnsRecordType SMIMEA = new DnsRecordType(0x0035, "SMIMEA");

    /**
     * Host Identity Protocol record RFC 5205 Method of separating the end-point
     * identifier and locator roles of IP addresses.
     */
    public static readonly DnsRecordType HIP = new DnsRecordType(0x0037, "HIP");

    /**
     * Child DS record. Published in a child zone to signal the {@link #DS} record the child wishes
     * its parent to publish, so that a delegation can be maintained without an out-of-band
     * provisioning channel.
     * See <a href="https://www.rfc-editor.org/rfc/rfc7344.html">RFC 7344</a>.
     */
    public static readonly DnsRecordType CDS = new DnsRecordType(0x003b, "CDS");

    /**
     * Child DNSKEY record. The child-published counterpart of {@link #DNSKEY}, from which the
     * parent may derive the delegation's {@link #DS} record.
     * See <a href="https://www.rfc-editor.org/rfc/rfc7344.html">RFC 7344</a>.
     */
    public static readonly DnsRecordType CDNSKEY = new DnsRecordType(0x003c, "CDNSKEY");

    /**
     * OpenPGP public key record. Associates an OpenPGP public key with an email address. RFC 7929 is
     * Experimental rather than standards-track, but the code point is assigned and the record is seen
     * on the wire.
     * See <a href="https://www.rfc-editor.org/rfc/rfc7929.html">RFC 7929</a>.
     */
    public static readonly DnsRecordType OPENPGPKEY = new DnsRecordType(0x003d, "OPENPGPKEY");

    /**
     * Child-to-parent synchronization record. Signals to a parent zone which records a child wishes
     * the parent to keep in sync.
     * See <a href="https://www.rfc-editor.org/rfc/rfc7477.html">RFC 7477</a>.
     */
    public static readonly DnsRecordType CSYNC = new DnsRecordType(0x003e, "CSYNC");

    /**
     * Message digest for DNS zones. Carries a cryptographic digest over the zone's contents, so that
     * a whole zone can be verified independently of the transport that delivered it.
     * See <a href="https://www.rfc-editor.org/rfc/rfc8976.html">RFC 8976</a>.
     */
    public static readonly DnsRecordType ZONEMD = new DnsRecordType(0x003f, "ZONEMD");

    /**
     * Sender Policy Framework record RFC 4408 Specified as part of the SPF
     * protocol as an alternative to of storing SPF data in TXT records. Uses
     * the same format as the earlier TXT record.
     */
    public static readonly DnsRecordType SPF = new DnsRecordType(0x0063, "SPF");

    /**
     * Secret key record RFC 2930 A method of providing keying material to be
     * used with TSIG that is encrypted under the public key in an accompanying
     * KEY RR..
     */
    public static readonly DnsRecordType TKEY = new DnsRecordType(0x00f9, "TKEY");

    /**
     * Transaction Signature record RFC 2845 Can be used to authenticate dynamic
     * updates as coming from an approved client, or to authenticate responses
     * as coming from an approved recursive name server similar to DNSSEC.
     */
    public static readonly DnsRecordType TSIG = new DnsRecordType(0x00fa, "TSIG");

    /**
     * Incremental Zone Transfer record RFC 1996 Requests a zone transfer of the
     * given zone but only differences from a previous serial number. This
     * request may be ignored and a full (AXFR) sent in response if the
     * authoritative server is unable to fulfill the request due to
     * configuration or lack of required deltas.
     */
    public static readonly DnsRecordType IXFR = new DnsRecordType(0x00fb, "IXFR");

    /**
     * Authoritative Zone Transfer record RFC 1035 Transfer entire zone file
     * from the master name server to secondary name servers.
     */
    public static readonly DnsRecordType AXFR = new DnsRecordType(0x00fc, "AXFR");

    /**
     * All cached records RFC 1035 Returns all records of all types known to the
     * name server. If the name server does not have any information on the
     * name, the request will be forwarded on. The records returned may not be
     * complete. For example, if there is both an A and an MX for a name, but
     * the name server has only the A record cached, only the A record will be
     * returned. Sometimes referred to as ANY, for example in Windows nslookup
     * and Wireshark.
     */
    public static readonly DnsRecordType ANY = new DnsRecordType(0x00ff, "ANY");

    /**
     * Certification Authority Authorization record RFC 6844 CA pinning,
     * constraining acceptable CAs for a host/domain.
     */
    public static readonly DnsRecordType CAA = new DnsRecordType(0x0101, "CAA");

    /**
     * DNSSEC Trust Authorities record N/A Part of a deployment proposal for
     * DNSSEC without a signed DNS root. See the IANA database and Weiler Spec
     * for details. Uses the same format as the DS record.
     */
    public static readonly DnsRecordType TA = new DnsRecordType(0x8000, "TA");

    /**
     * DNSSEC Lookaside Validation record RFC 4431 For publishing DNSSEC trust
     * anchors outside of the DNS delegation chain. Uses the same format as the
     * DS record. RFC 5074 describes a way of using these records.
     */
    public static readonly DnsRecordType DLV = new DnsRecordType(0x8001, "DLV");

    /**
     * Service Binding and Parameter Specification via the DNS (SVCB and HTTPS Resource Records).
     * See <a href="https://www.rfc-editor.org/rfc/rfc9460.html#section-14.1">RFC9460 SVCB RR Type</a>.
     */
    public static readonly DnsRecordType SVCB = new DnsRecordType(0x0040, "SVCB");

    /**
     * Service Binding and Parameter Specification via the DNS (SVCB and HTTPS Resource Records).
     * See <a href="https://www.rfc-editor.org/rfc/rfc9460.html#section-14.2">RFC9460 HTTPS RR Type</a>.
     */
    public static readonly DnsRecordType HTTPS = new DnsRecordType(0x0041, "HTTPS");

    private static readonly Dictionary<string, DnsRecordType> BY_NAME = new Dictionary<string, DnsRecordType>();
    // CLR: io.netty.util.collection.IntObjectHashMap exists because a Java type argument cannot be a primitive type
    // ("Type arguments may be either reference types or wildcards", JLS 4.5.1,
    // https://docs.oracle.com/javase/specs/jls/se17/html/jls-4.html#jls-4.5.1); the owner kept Dictionary instead of
    // porting it (2026-11-19, docs/common-porting-manifest.json), and Dictionary<int, DnsRecordType> stores the int
    // keys unboxed: "the runtime creates a specialized generic type with
    // the supplied parameter or parameters substituted in the appropriate locations"
    // (https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/generics/generics-in-the-run-time).
    private static readonly Dictionary<int, DnsRecordType> BY_TYPE = new Dictionary<int, DnsRecordType>();
    private static readonly string EXPECTED;

    static DnsRecordType()
    {
        DnsRecordType[] all = {
                A, NS, CNAME, SOA, PTR, MX, TXT, RP, AFSDB, SIG, KEY, AAAA, LOC, SRV, NAPTR, KX, CERT, DNAME, OPT, APL,
                DS, SSHFP, IPSECKEY, RRSIG, NSEC, DNSKEY, DHCID, NSEC3, NSEC3PARAM, TLSA, SMIMEA, HIP, CDS, CDNSKEY,
                OPENPGPKEY, CSYNC, ZONEMD, SPF, TKEY, TSIG, IXFR, AXFR, ANY, CAA, TA, DLV, SVCB, HTTPS
        };

        StringBuilder expected = new StringBuilder(1024);

        expected.Append(" (expected: ");

        foreach (DnsRecordType type in all)
        {
            BY_NAME[type.Name()] = type;
            BY_TYPE[type.IntValue()] = type;

            expected.Append(type.Name())
                    .Append('(')
                    .Append(type.IntValue().ToString(CultureInfo.InvariantCulture))
                    .Append("), ");
        }

        expected.Length = expected.Length - 2;
        expected.Append(')');
        EXPECTED = expected.ToString();
    }

    public static DnsRecordType ValueOf(int intValue)
    {
        DnsRecordType result = BY_TYPE.GetValueOrDefault(intValue);
        if (result == null)
        {
            return new DnsRecordType(intValue);
        }

        return result;
    }

    // CLR: HashMap.get(null) returns null, while a Dictionary rejects a null key, so a null name skips the lookup;
    // Java string concatenation renders null as "null" and C# as an empty string, so the message spells it out.
    // IllegalArgumentException is ArgumentException (docs/codec-base-porting.md, Shared CLR adaptations).
    public static DnsRecordType ValueOf(string name)
    {
        DnsRecordType result = name == null ? null : BY_NAME.GetValueOrDefault(name);
        if (result == null)
        {
            throw new ArgumentException("name: " + (name ?? "null") + EXPECTED);
        }

        return result;
    }

    private readonly int intValue;
    private readonly string name;
    private string text;

    private DnsRecordType(int intValue) : this(intValue, "UNKNOWN")
    {
    }

    // CLR: IllegalArgumentException is ArgumentException (docs/codec-base-porting.md, Shared CLR adaptations).
    public DnsRecordType(int intValue, string name)
    {
        if ((intValue & 0xffff) != intValue)
        {
            throw new ArgumentException("intValue: " + intValue.ToString(CultureInfo.InvariantCulture) +
                                        " (expected: 0 ~ 65535)");
        }

        this.intValue = intValue;
        this.name = name;
    }

    /**
     * Returns the name of this type, as seen in bind config files
     */
    public string Name()
    {
        return name;
    }

    /**
     * Returns the value of this DnsType as it appears in DNS protocol
     */
    public int IntValue()
    {
        return intValue;
    }

    public override int GetHashCode()
    {
        return intValue;
    }

    public override bool Equals(object o)
    {
        return o is DnsRecordType && ((DnsRecordType) o).intValue == intValue;
    }

    public int CompareTo(DnsRecordType o)
    {
        return IntValue() - o.IntValue();
    }

    public override string ToString()
    {
        string text = this.text;
        if (text == null)
        {
            this.text = text = name + '(' + IntValue().ToString(CultureInfo.InvariantCulture) + ')';
        }

        return text;
    }
}
