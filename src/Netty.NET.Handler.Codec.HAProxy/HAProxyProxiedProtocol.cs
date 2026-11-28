/*
 * Copyright 2014 The Netty Project
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
using System.Globalization;
using static Netty.NET.Handler.Codec.HAProxy.HAProxyConstants;

namespace Netty.NET.Handler.Codec.HAProxy;

/**
 * A protocol proxied by HAProxy which is represented by its transport protocol and address family.
 */
// CLR: this enum and its nested AddressFamily and TransportProtocol enums carry fields and methods, so they are sealed
// classes with static readonly constants; Name(), ToString() and Values() stand for the java.lang.Enum members (see
// HAProxyCommand). The byte values stay bytes (owner decision). The nested constants are created on first use of their
// class, before the outer constants read them.
public sealed class HAProxyProxiedProtocol
{
    /**
     * The UNKNOWN represents a connection which was forwarded for an unknown protocol and an unknown address family.
     */
    public static readonly HAProxyProxiedProtocol UNKNOWN =
            new HAProxyProxiedProtocol(nameof(UNKNOWN), TPAF_UNKNOWN_BYTE, AddressFamily.AF_UNSPEC, TransportProtocol.UNSPEC);
    /**
     * The TCP4 represents a connection which was forwarded for an IPv4 client over TCP.
     */
    public static readonly HAProxyProxiedProtocol TCP4 =
            new HAProxyProxiedProtocol(nameof(TCP4), TPAF_TCP4_BYTE, AddressFamily.AF_IPv4, TransportProtocol.STREAM);
    /**
     * The TCP6 represents a connection which was forwarded for an IPv6 client over TCP.
     */
    public static readonly HAProxyProxiedProtocol TCP6 =
            new HAProxyProxiedProtocol(nameof(TCP6), TPAF_TCP6_BYTE, AddressFamily.AF_IPv6, TransportProtocol.STREAM);
    /**
     * The UDP4 represents a connection which was forwarded for an IPv4 client over UDP.
     */
    public static readonly HAProxyProxiedProtocol UDP4 =
            new HAProxyProxiedProtocol(nameof(UDP4), TPAF_UDP4_BYTE, AddressFamily.AF_IPv4, TransportProtocol.DGRAM);
    /**
     * The UDP6 represents a connection which was forwarded for an IPv6 client over UDP.
     */
    public static readonly HAProxyProxiedProtocol UDP6 =
            new HAProxyProxiedProtocol(nameof(UDP6), TPAF_UDP6_BYTE, AddressFamily.AF_IPv6, TransportProtocol.DGRAM);
    /**
     * The UNIX_STREAM represents a connection which was forwarded for a UNIX stream socket.
     */
    public static readonly HAProxyProxiedProtocol UNIX_STREAM =
            new HAProxyProxiedProtocol(nameof(UNIX_STREAM), TPAF_UNIX_STREAM_BYTE, AddressFamily.AF_UNIX,
                    TransportProtocol.STREAM);
    /**
     * The UNIX_DGRAM represents a connection which was forwarded for a UNIX datagram socket.
     */
    public static readonly HAProxyProxiedProtocol UNIX_DGRAM =
            new HAProxyProxiedProtocol(nameof(UNIX_DGRAM), TPAF_UNIX_DGRAM_BYTE, AddressFamily.AF_UNIX,
                    TransportProtocol.DGRAM);

    private readonly byte byteValue;
    private readonly AddressFamily addressFamily;
    private readonly TransportProtocol transportProtocol;
    private readonly string name;

    /**
     * Creates a new instance.
     */
    private HAProxyProxiedProtocol(
            string name,
            byte byteValue,
            AddressFamily addressFamily,
            TransportProtocol transportProtocol)
    {

        this.name = name;
        this.byteValue = byteValue;
        this.addressFamily = addressFamily;
        this.transportProtocol = transportProtocol;
    }

    /**
     * Returns the {@link HAProxyProxiedProtocol} represented by the specified byte.
     *
     * @param tpafByte transport protocol and address family byte
     */
    public static HAProxyProxiedProtocol ValueOf(byte tpafByte)
    {
        switch (tpafByte)
        {
            case TPAF_TCP4_BYTE:
                return TCP4;
            case TPAF_TCP6_BYTE:
                return TCP6;
            case TPAF_UNKNOWN_BYTE:
                return UNKNOWN;
            case TPAF_UDP4_BYTE:
                return UDP4;
            case TPAF_UDP6_BYTE:
                return UDP6;
            case TPAF_UNIX_STREAM_BYTE:
                return UNIX_STREAM;
            case TPAF_UNIX_DGRAM_BYTE:
                return UNIX_DGRAM;
            default:
                throw new ArgumentException(
                        "unknown transport protocol + address family: " +
                        (tpafByte & 0xFF).ToString(CultureInfo.InvariantCulture));
        }
    }

    // CLR: the implicit valueOf(String) of a Java enum class (JLS 8.9.3), which HAProxyMessage.decodeHeader(String)
    // calls. Enum.valueOf "Returns the enum constant ... with the specified name", throws IllegalArgumentException
    // (ArgumentException) when there is none and NullPointerException for a null name
    // (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/lang/Enum.html#valueOf(java.lang.Class,java.lang.String));
    // the message names the C# type where the JDK names the Java class.
    public static HAProxyProxiedProtocol ValueOf(string name)
    {
        if (name == null)
        {
            throw new ArgumentNullException(nameof(name), "Name is null");
        }

        foreach (HAProxyProxiedProtocol value in Values())
        {
            if (value.name == name)
            {
                return value;
            }
        }

        throw new ArgumentException("No enum constant " + typeof(HAProxyProxiedProtocol).FullName + "." + name);
    }

    /**
     * Returns the byte value of this protocol and address family.
     */
    public byte ByteValue()
    {
        return byteValue;
    }

    /**
     * Returns the {@link AddressFamily} of this protocol and address family.
     */
    // CLR: addressFamily() and transportProtocol() would be the methods AddressFamily() and TransportProtocol(), which
    // cannot be declared beside the nested types of the same names ("The type 'type name' already contains a definition
    // for 'identifier'", https://learn.microsoft.com/dotnet/csharp/misc/cs0102; the Transport port meets the same clash
    // for EmbeddedChannel.builder()). The nested types keep the Java names and the methods take a trailing underscore,
    // the only spelling difference besides case that the naming rule allows.
    public AddressFamily AddressFamily_()
    {
        return addressFamily;
    }

    /**
     * Returns the {@link TransportProtocol} of this protocol and address family.
     */
    public TransportProtocol TransportProtocol_()
    {
        return transportProtocol;
    }

    public string Name()
    {
        return name;
    }

    public static HAProxyProxiedProtocol[] Values()
    {
        return new[] { UNKNOWN, TCP4, TCP6, UDP4, UDP6, UNIX_STREAM, UNIX_DGRAM };
    }

    public override string ToString()
    {
        return name;
    }

    /**
     * The address family of an HAProxy proxy protocol header.
     */
    public sealed class AddressFamily
    {
        /**
         * The UNSPECIFIED address family represents a connection which was forwarded for an unknown protocol.
         */
        public static readonly AddressFamily AF_UNSPEC = new AddressFamily(nameof(AF_UNSPEC), AF_UNSPEC_BYTE);
        /**
         * The IPV4 address family represents a connection which was forwarded for an IPV4 client.
         */
        public static readonly AddressFamily AF_IPv4 = new AddressFamily(nameof(AF_IPv4), AF_IPV4_BYTE);
        /**
         * The IPV6 address family represents a connection which was forwarded for an IPV6 client.
         */
        public static readonly AddressFamily AF_IPv6 = new AddressFamily(nameof(AF_IPv6), AF_IPV6_BYTE);
        /**
         * The UNIX address family represents a connection which was forwarded for a unix socket.
         */
        public static readonly AddressFamily AF_UNIX = new AddressFamily(nameof(AF_UNIX), AF_UNIX_BYTE);

        /**
         * The highest 4 bits of the transport protocol and address family byte contain the address family
         */
        private const byte FAMILY_MASK = (byte) 0xf0;

        private readonly byte byteValue;
        private readonly string name;

        /**
         * Creates a new instance
         */
        private AddressFamily(string name, byte byteValue)
        {
            this.name = name;
            this.byteValue = byteValue;
        }

        /**
         * Returns the {@link AddressFamily} represented by the highest 4 bits of the specified byte.
         *
         * @param tpafByte transport protocol and address family byte
         */
        // CLR: the Java byte and the (byte) 0xf0 mask are signed (-128..127,
        // https://docs.oracle.com/javase/specs/jls/se17/html/jls-4.html#jls-4.2.1), so an unknown high family prints a
        // negative number (0xF1 prints -16); both are widened through sbyte to keep that value and its text.
        public static AddressFamily ValueOf(byte tpafByte)
        {
            int addressFamily = unchecked((sbyte) tpafByte & (sbyte) FAMILY_MASK);

            switch ((byte) addressFamily)
            {
                case AF_IPV4_BYTE:
                    return AF_IPv4;
                case AF_IPV6_BYTE:
                    return AF_IPv6;
                case AF_UNSPEC_BYTE:
                    return AF_UNSPEC;
                case AF_UNIX_BYTE:
                    return AF_UNIX;
                default:
                    throw new ArgumentException("unknown address family: " +
                            addressFamily.ToString(CultureInfo.InvariantCulture));
            }
        }

        /**
         * Returns the byte value of this address family.
         */
        public byte ByteValue()
        {
            return byteValue;
        }

        public string Name()
        {
            return name;
        }

        public static AddressFamily[] Values()
        {
            return new[] { AF_UNSPEC, AF_IPv4, AF_IPv6, AF_UNIX };
        }

        public override string ToString()
        {
            return name;
        }
    }

    /**
     * The transport protocol of an HAProxy proxy protocol header
     */
    public sealed class TransportProtocol
    {
        /**
         * The UNSPEC transport protocol represents a connection which was forwarded for an unknown protocol.
         */
        public static readonly TransportProtocol UNSPEC = new TransportProtocol(nameof(UNSPEC), TRANSPORT_UNSPEC_BYTE);
        /**
         * The STREAM transport protocol represents a connection which was forwarded for a TCP connection.
         */
        public static readonly TransportProtocol STREAM = new TransportProtocol(nameof(STREAM), TRANSPORT_STREAM_BYTE);
        /**
         * The DGRAM transport protocol represents a connection which was forwarded for a UDP connection.
         */
        public static readonly TransportProtocol DGRAM = new TransportProtocol(nameof(DGRAM), TRANSPORT_DGRAM_BYTE);

        /**
         * The transport protocol is specified in the lowest 4 bits of the transport protocol and address family byte
         */
        private const byte TRANSPORT_MASK = 0x0f;

        private readonly byte transportByte;
        private readonly string name;

        /**
         * Creates a new instance.
         */
        private TransportProtocol(string name, byte transportByte)
        {
            this.name = name;
            this.transportByte = transportByte;
        }

        /**
         * Returns the {@link TransportProtocol} represented by the lowest 4 bits of the specified byte.
         *
         * @param tpafByte transport protocol and address family byte
         */
        public static TransportProtocol ValueOf(byte tpafByte)
        {
            int transportProtocol = tpafByte & TRANSPORT_MASK;

            switch ((byte) transportProtocol)
            {
                case TRANSPORT_STREAM_BYTE:
                    return STREAM;
                case TRANSPORT_UNSPEC_BYTE:
                    return UNSPEC;
                case TRANSPORT_DGRAM_BYTE:
                    return DGRAM;
                default:
                    throw new ArgumentException("unknown transport protocol: " +
                            transportProtocol.ToString(CultureInfo.InvariantCulture));
            }
        }

        /**
         * Returns the byte value of this transport protocol.
         */
        public byte ByteValue()
        {
            return transportByte;
        }

        public string Name()
        {
            return name;
        }

        public static TransportProtocol[] Values()
        {
            return new[] { UNSPEC, STREAM, DGRAM };
        }

        public override string ToString()
        {
            return name;
        }
    }
}
