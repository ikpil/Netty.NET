// CLR: test-only stand-in for org.apache.directory.server.dns.messages.RecordType of apacheds-protocol-dns 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/messages/RecordType.java
// The source is a Java enum with a short value that implements EnumConverter<Short> (util/EnumConverter.java) and
// converts back through ReverseEnumMap (util/ReverseEnumMap.java); a Java enum with fields and methods is a sealed
// class with static readonly instances (owner porting rule 1, as codec-base Base64Dialect), so a switch over it is
// "case var _ when x == RecordType.A" as in codec-haproxy HAProxyMessage, and == stays the reference comparison of
// enum constants. convert() is Convert(), the static convert(value) is Convert(short) over a Dictionary built in
// declaration order (ReverseEnumMap.get throws IllegalArgumentException for an unknown value, ArgumentException
// here), and name(), ordinal(), values() and toString() keep the java.lang.Enum results. Two constants share a value
// in the source (ATMA(34) and CERT(34)); ReverseEnumMap puts them in declaration order into a HashMap, so
// convert((short) 34) returns CERT, as the map here does.

using System;
using System.Collections.Generic;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

public sealed class RecordType
{
    /** Host address */
    public static readonly RecordType A = new RecordType("A", 0, 1);

    /** Authoritative name server */
    public static readonly RecordType NS = new RecordType("NS", 1, 2);

    /** Mail destination */
    public static readonly RecordType MD = new RecordType("MD", 2, 3);

    /** Mail forwarder */
    public static readonly RecordType MF = new RecordType("MF", 3, 4);

    /** Canonical name for an alias */
    public static readonly RecordType CNAME = new RecordType("CNAME", 4, 5);

    /** Start of a zone of authority */
    public static readonly RecordType SOA = new RecordType("SOA", 5, 6);

    /** Mailbox domain name */
    public static readonly RecordType MB = new RecordType("MB", 6, 7);

    /** Mail group member */
    public static readonly RecordType MG = new RecordType("MG", 7, 8);

    /** Mail rename domain name */
    public static readonly RecordType MR = new RecordType("MR", 8, 9);

    /** Null resource record */
    public static readonly RecordType NULL = new RecordType("NULL", 9, 10);

    /** Well know service description */
    public static readonly RecordType WKS = new RecordType("WKS", 10, 11);

    /** Domain name pointer */
    public static readonly RecordType PTR = new RecordType("PTR", 11, 12);

    /** Host information */
    public static readonly RecordType HINFO = new RecordType("HINFO", 12, 13);

    /** Mailbox or mail list information */
    public static readonly RecordType MINFO = new RecordType("MINFO", 13, 14);

    /** Mail exchange */
    public static readonly RecordType MX = new RecordType("MX", 14, 15);

    /** Text strings */
    public static readonly RecordType TXT = new RecordType("TXT", 15, 16);

    /** Responsible person */
    public static readonly RecordType RP = new RecordType("RP", 16, 17);

    /** AFS cell database */
    public static readonly RecordType AFSDB = new RecordType("AFSDB", 17, 18);

    /** X.25 calling address */
    public static readonly RecordType X25 = new RecordType("X25", 18, 19);

    /** ISDN calling address */
    public static readonly RecordType ISDN = new RecordType("ISDN", 19, 20);

    /** Router */
    public static readonly RecordType RT = new RecordType("RT", 20, 21);

    /** NSAP address */
    public static readonly RecordType NSAP = new RecordType("NSAP", 21, 22);

    /** Reverse NSAP address (deprecated) */
    public static readonly RecordType NSAP_PTR = new RecordType("NSAP_PTR", 22, 23);

    /** Signature */
    public static readonly RecordType SIG = new RecordType("SIG", 23, 24);

    /** Key */
    public static readonly RecordType KEY = new RecordType("KEY", 24, 25);

    /** X.400 mail mapping */
    public static readonly RecordType PX = new RecordType("PX", 25, 26);

    /** Geographical position (withdrawn) */
    public static readonly RecordType GPOS = new RecordType("GPOS", 26, 27);

    /** IPv6 address */
    public static readonly RecordType AAAA = new RecordType("AAAA", 27, 28);

    /** Location */
    public static readonly RecordType LOC = new RecordType("LOC", 28, 29);

    /** Next valid name in zone */
    public static readonly RecordType NXT = new RecordType("NXT", 29, 30);

    /** Endpoint identifier */
    public static readonly RecordType EID = new RecordType("EID", 30, 31);

    /** Nimrod locator */
    public static readonly RecordType NIMLOC = new RecordType("NIMLOC", 31, 32);

    /** Server selection */
    public static readonly RecordType SRV = new RecordType("SRV", 32, 33);

    /** ATM address */
    public static readonly RecordType ATMA = new RecordType("ATMA", 33, 34);

    /** Naming authority pointer */
    public static readonly RecordType NAPTR = new RecordType("NAPTR", 34, 35);

    /** Key exchange */
    public static readonly RecordType KX = new RecordType("KX", 35, 36);

    /** Certificate */
    public static readonly RecordType CERT = new RecordType("CERT", 36, 34);

    /** IPv6 address (experimental) */
    public static readonly RecordType A6 = new RecordType("A6", 37, 38);

    /** Non-terminal name redirection */
    public static readonly RecordType DNAME = new RecordType("DNAME", 38, 39);

    /** Options - contains EDNS metadata */
    public static readonly RecordType OPT = new RecordType("OPT", 39, 41);

    /** Address Prefix List */
    public static readonly RecordType APL = new RecordType("APL", 40, 42);

    /** Delegation Signer */
    public static readonly RecordType DS = new RecordType("DS", 41, 43);

    /** SSH Key Fingerprint */
    public static readonly RecordType SSHFP = new RecordType("SSHFP", 42, 44);

    /** Resource Record Signature */
    public static readonly RecordType RRSIG = new RecordType("RRSIG", 43, 46);

    /** Next Secure Name */
    public static readonly RecordType NSEC = new RecordType("NSEC", 44, 47);

    /** DNSSEC Key */
    public static readonly RecordType DNSKEY = new RecordType("DNSKEY", 45, 48);

    /** Transaction key - used to compute a shared secret or exchange a key */
    public static readonly RecordType TKEY = new RecordType("TKEY", 46, 249);

    /** Transaction signature */
    public static readonly RecordType TSIG = new RecordType("TSIG", 47, 250);

    /** Incremental zone transfer */
    public static readonly RecordType IXFR = new RecordType("IXFR", 48, 251);

    /** Request for transfer of an entire zone */
    public static readonly RecordType AXFR = new RecordType("AXFR", 49, 252);

    /** Request for mailbox-related records */
    public static readonly RecordType MAILB = new RecordType("MAILB", 50, 253);

    /** Request for mail agent resource records */
    public static readonly RecordType MAILA = new RecordType("MAILA", 51, 254);

    /** Request for all records */
    public static readonly RecordType ANY = new RecordType("ANY", 52, 255);

    private static readonly RecordType[] VALUES =
    {
        A, NS, MD, MF, CNAME, SOA, MB, MG, MR, NULL, WKS, PTR, HINFO, MINFO, MX, TXT, RP, AFSDB, X25, ISDN, RT, NSAP,
        NSAP_PTR, SIG, KEY, PX, GPOS, AAAA, LOC, NXT, EID, NIMLOC, SRV, ATMA, NAPTR, KX, CERT, A6, DNAME, OPT, APL,
        DS, SSHFP, RRSIG, NSEC, DNSKEY, TKEY, TSIG, IXFR, AXFR, MAILB, MAILA, ANY
    };

    private static readonly Dictionary<short, RecordType> map = CreateMap();

    private readonly string name;
    private readonly int ordinal;
    private readonly short value;

    private RecordType(string name, int ordinal, int value)
    {
        this.name = name;
        this.ordinal = ordinal;
        this.value = (short)value;
    }

    private static Dictionary<short, RecordType> CreateMap()
    {
        Dictionary<short, RecordType> reverseMap = new Dictionary<short, RecordType>();

        foreach (RecordType e in VALUES)
        {
            reverseMap[e.Convert()] = e;
        }

        return reverseMap;
    }

    public short Convert()
    {
        return value;
    }

    /**
     * Converts an ordinal value into a {@link RecordType}.
     *
     * @param value
     * @return The {@link RecordType}.
     */
    public static RecordType Convert(short value)
    {
        if (!map.TryGetValue(value, out RecordType e))
        {
            throw new ArgumentException("Unknown RecordType value: " + value);
        }

        return e;
    }

    /**
     * Returns whether a given {@link RecordType} is a {@link ResourceRecord}.
     *
     * @param resourceType
     * @return true of the {@link RecordType} is a {@link ResourceRecord}.
     */
    public static bool IsResourceRecord(RecordType resourceType)
    {
        // CLR: the source switch over OPT, TKEY, TSIG, IXFR, AXFR, MAILB, MAILA and ANY compares the instances.
        return !(resourceType == OPT || resourceType == TKEY || resourceType == TSIG || resourceType == IXFR ||
                 resourceType == AXFR || resourceType == MAILB || resourceType == MAILA || resourceType == ANY);
    }

    public static RecordType[] Values()
    {
        return (RecordType[])VALUES.Clone();
    }

    public string Name()
    {
        return name;
    }

    public int Ordinal()
    {
        return ordinal;
    }

    public override string ToString()
    {
        return name;
    }
}
