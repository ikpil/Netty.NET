// CLR: test-only stand-in for org.apache.directory.server.dns.messages.OpCode of apacheds-protocol-dns 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/messages/OpCode.java
// The source is a Java enum with a byte value that implements EnumConverter<Byte> (util/EnumConverter.java) and
// converts back through ReverseEnumMap (util/ReverseEnumMap.java); a Java enum with fields and methods is a sealed
// class with static readonly instances (owner porting rule 1, as codec-base Base64Dialect), so a switch over it is
// "case var _ when x == OpCode.QUERY" as in codec-haproxy HAProxyMessage, and == stays the reference comparison of
// enum constants. convert() is Convert(), the static convert(value) is Convert(byte) over a Dictionary built in
// declaration order (ReverseEnumMap.get throws IllegalArgumentException for an unknown value, ArgumentException
// here), and name(), ordinal(), values() and toString() keep the java.lang.Enum results. The Java byte value is
// the C# byte; every constant is between 0 and 5, so no value depends on the sign.

using System;
using System.Collections.Generic;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

public sealed class OpCode
{
    /** Standard query */
    public static readonly OpCode QUERY = new OpCode("QUERY", 0, 0);

    /** Inverse query */
    public static readonly OpCode IQUERY = new OpCode("IQUERY", 1, 1);

    /** Server status request */
    public static readonly OpCode STATUS = new OpCode("STATUS", 2, 2);

    /** Zone transfer notification */
    public static readonly OpCode NOTIFY = new OpCode("NOTIFY", 3, 4);

    /** Dynamic update message */
    public static readonly OpCode UPDATE = new OpCode("UPDATE", 4, 5);

    private static readonly OpCode[] VALUES =
    {
        QUERY, IQUERY, STATUS, NOTIFY, UPDATE
    };

    private static readonly Dictionary<byte, OpCode> map = CreateMap();

    private readonly string name;
    private readonly int ordinal;
    private readonly byte value;

    private OpCode(string name, int ordinal, int value)
    {
        this.name = name;
        this.ordinal = ordinal;
        this.value = (byte)value;
    }

    private static Dictionary<byte, OpCode> CreateMap()
    {
        Dictionary<byte, OpCode> reverseMap = new Dictionary<byte, OpCode>();

        foreach (OpCode e in VALUES)
        {
            reverseMap[e.Convert()] = e;
        }

        return reverseMap;
    }

    public byte Convert()
    {
        return value;
    }

    /**
     * Converts an ordinal value into an {@link OpCode}.
     *
     * @param value
     * @return The {@link OpCode}.
     */
    public static OpCode Convert(byte value)
    {
        if (!map.TryGetValue(value, out OpCode e))
        {
            throw new ArgumentException("Unknown OpCode value: " + value);
        }

        return e;
    }

    public static OpCode[] Values()
    {
        return (OpCode[])VALUES.Clone();
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
