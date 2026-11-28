// CLR: test-only stand-in for org.apache.directory.server.dns.messages.RecordClass of apacheds-protocol-dns 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/messages/RecordClass.java
// The source is a Java enum with a short value that implements EnumConverter<Short> (util/EnumConverter.java) and
// converts back through ReverseEnumMap (util/ReverseEnumMap.java); a Java enum with fields and methods is a sealed
// class with static readonly instances (owner porting rule 1, as codec-base Base64Dialect), so a switch over it is
// "case var _ when x == RecordClass.IN" as in codec-haproxy HAProxyMessage, and == stays the reference comparison of
// enum constants. convert() is Convert(), the static convert(value) is Convert(short) over a Dictionary built in
// declaration order (ReverseEnumMap.get throws IllegalArgumentException for an unknown value, ArgumentException
// here), and name(), ordinal(), values() and toString() keep the java.lang.Enum results.

using System;
using System.Collections.Generic;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

public sealed class RecordClass
{
    /** Internet */
    public static readonly RecordClass IN = new RecordClass("IN", 0, 1);

    /** CSNET class */
    public static readonly RecordClass CS = new RecordClass("CS", 1, 2);

    /** CHAOS class */
    public static readonly RecordClass CH = new RecordClass("CH", 2, 3);

    /** Hesiod [Dyer 87] */
    public static readonly RecordClass HS = new RecordClass("HS", 3, 4);

    /** Special value used in dynamic update messages */
    public static readonly RecordClass NONE = new RecordClass("NONE", 4, 254);

    /** Any class */
    public static readonly RecordClass ANY = new RecordClass("ANY", 5, 255);

    private static readonly RecordClass[] VALUES =
    {
        IN, CS, CH, HS, NONE, ANY
    };

    private static readonly Dictionary<short, RecordClass> map = CreateMap();

    private readonly string name;
    private readonly int ordinal;
    private readonly short value;

    private RecordClass(string name, int ordinal, int value)
    {
        this.name = name;
        this.ordinal = ordinal;
        this.value = (short)value;
    }

    private static Dictionary<short, RecordClass> CreateMap()
    {
        Dictionary<short, RecordClass> reverseMap = new Dictionary<short, RecordClass>();

        foreach (RecordClass e in VALUES)
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
     * Converts an ordinal value into a {@link RecordClass}.
     *
     * @param value
     * @return The {@link RecordClass}.
     */
    public static RecordClass Convert(short value)
    {
        if (!map.TryGetValue(value, out RecordClass e))
        {
            throw new ArgumentException("Unknown RecordClass value: " + value);
        }

        return e;
    }

    public static RecordClass[] Values()
    {
        return (RecordClass[])VALUES.Clone();
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
