// CLR: test-only stand-in for org.apache.directory.server.dns.messages.MessageType of apacheds-protocol-dns 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/messages/MessageType.java
// The source is a Java enum with a byte value that implements EnumConverter<Byte> (util/EnumConverter.java) and
// converts back through ReverseEnumMap (util/ReverseEnumMap.java); a Java enum with fields and methods is a sealed
// class with static readonly instances (owner porting rule 1, as codec-base Base64Dialect), so a switch over it is
// "case var _ when x == MessageType.QUERY" as in codec-haproxy HAProxyMessage, and == stays the reference comparison
// of enum constants. convert() is Convert(), the static convert(value) is Convert(byte) over a Dictionary built in
// declaration order (ReverseEnumMap.get throws IllegalArgumentException for an unknown value, ArgumentException
// here), and name(), ordinal(), values() and toString() keep the java.lang.Enum results. The Java byte value is the
// C# byte (owner decision for Java byte); every constant is between 0 and 5, so no value depends on the sign.

using System;
using System.Collections.Generic;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

public sealed class MessageType
{
    /** A query message. */
    public static readonly MessageType QUERY = new MessageType("QUERY", 0, 0);

    /** A response message. */
    public static readonly MessageType RESPONSE = new MessageType("RESPONSE", 1, 1);

    private static readonly MessageType[] VALUES =
    {
        QUERY, RESPONSE
    };

    private static readonly Dictionary<byte, MessageType> map = CreateMap();

    private readonly string name;
    private readonly int ordinal;
    private readonly byte value;

    private MessageType(string name, int ordinal, int value)
    {
        this.name = name;
        this.ordinal = ordinal;
        this.value = (byte)value;
    }

    private static Dictionary<byte, MessageType> CreateMap()
    {
        Dictionary<byte, MessageType> reverseMap = new Dictionary<byte, MessageType>();
        foreach (MessageType e in VALUES)
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
     * Converts an ordinal value into a {@link MessageType}.
     *
     * @param value
     * @return The {@link MessageType}.
     */
    public static MessageType Convert(byte value)
    {
        if (!map.TryGetValue(value, out MessageType e))
        {
            throw new ArgumentException("Unknown MessageType value: " + value);
        }

        return e;
    }

    public static MessageType[] Values()
    {
        return (MessageType[])VALUES.Clone();
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
