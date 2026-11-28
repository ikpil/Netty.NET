// CLR: test-only stand-in for org.apache.directory.server.dns.messages.ResponseCode of apacheds-protocol-dns
// 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/messages/ResponseCode.java
// The source is a Java enum with a byte value that implements EnumConverter<Byte> (util/EnumConverter.java) and
// converts back through ReverseEnumMap (util/ReverseEnumMap.java); a Java enum with fields and methods is a sealed
// class with static readonly instances (owner porting rule 1, as codec-base Base64Dialect), so a switch over it is
// "case var _ when x == ResponseCode.NO_ERROR" as in codec-haproxy HAProxyMessage, and == stays the reference
// comparison of enum constants. convert() is Convert(), the static convert(value) is Convert(byte) over a
// Dictionary built in declaration order (ReverseEnumMap.get throws IllegalArgumentException for an unknown value,
// ArgumentException here), and name(), ordinal(), values() and toString() keep the java.lang.Enum results. The
// Java byte value is the C# byte; every constant is between 0 and 5, so no value depends on the sign.

using System;
using System.Collections.Generic;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

public sealed class ResponseCode
{
    /** No error condition. */
    public static readonly ResponseCode NO_ERROR = new ResponseCode("NO_ERROR", 0, 0);

    /** The name server was unable to interpret the query. */
    public static readonly ResponseCode FORMAT_ERROR = new ResponseCode("FORMAT_ERROR", 1, 1);

    /** The name server was unable to process this query due to a problem with the name server. */
    public static readonly ResponseCode SERVER_FAILURE = new ResponseCode("SERVER_FAILURE", 2, 2);

    /** The domain name referenced in the query does not exist. */
    public static readonly ResponseCode NAME_ERROR = new ResponseCode("NAME_ERROR", 3, 3);

    /** The name server does not support the requested kind of query. */
    public static readonly ResponseCode NOT_IMPLEMENTED = new ResponseCode("NOT_IMPLEMENTED", 4, 4);

    /** The name server refuses to perform the specified operation for policy reasons. */
    public static readonly ResponseCode REFUSED = new ResponseCode("REFUSED", 5, 5);

    private static readonly ResponseCode[] VALUES =
    {
        NO_ERROR, FORMAT_ERROR, SERVER_FAILURE, NAME_ERROR, NOT_IMPLEMENTED, REFUSED
    };

    private static readonly Dictionary<byte, ResponseCode> map = CreateMap();

    private readonly string name;
    private readonly int ordinal;
    private readonly byte value;

    private ResponseCode(string name, int ordinal, int value)
    {
        this.name = name;
        this.ordinal = ordinal;
        this.value = (byte)value;
    }

    private static Dictionary<byte, ResponseCode> CreateMap()
    {
        Dictionary<byte, ResponseCode> reverseMap = new Dictionary<byte, ResponseCode>();

        foreach (ResponseCode e in VALUES)
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
     * Converts an ordinal value into a {@link ResponseCode}.
     *
     * @param value
     * @return The {@link ResponseCode}.
     */
    public static ResponseCode Convert(byte value)
    {
        if (!map.TryGetValue(value, out ResponseCode e))
        {
            throw new ArgumentException("Unknown ResponseCode value: " + value);
        }

        return e;
    }

    public static ResponseCode[] Values()
    {
        return (ResponseCode[])VALUES.Clone();
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
