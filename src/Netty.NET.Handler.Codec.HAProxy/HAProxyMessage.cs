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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.HAProxy.HAProxyProxiedProtocol;

namespace Netty.NET.Handler.Codec.HAProxy;

/**
 * Message container for decoded HAProxy proxy protocol parameters
 */
// CLR: java.util.List is IList<T>; Collections.unmodifiableList is a ReadOnlyCollection<T> view and
// Collections.emptyList() an empty array (https://learn.microsoft.com/dotnet/api/system.collections.generic.list-1.asreadonly).
// List<? extends HAProxyTLV> is IList<HAProxyTLV>, because a C# IList<T> is invariant
// (https://learn.microsoft.com/dotnet/standard/generics/covariance-and-contravariance). A switch over the AddressFamily
// constants uses case guards, because a C# case label is a pattern
// (https://learn.microsoft.com/dotnet/csharp/language-reference/statements/selection-statements). RuntimeException and
// Throwable are Exception, IllegalArgumentException is ArgumentException, CharsetUtil.US_ASCII is Encoding.ASCII
// (docs/codec-base-porting.md), and numbers in text use the invariant culture.
public sealed class HAProxyMessage : AbstractReferenceCounted
{

    // Let's pick some conservative limit here.
    private const int MAX_NESTING_LEVEL = 128;
    private static readonly ResourceLeakDetector<HAProxyMessage> leakDetector =
            ResourceLeakDetectorFactory.Instance().NewResourceLeakDetector<HAProxyMessage>(typeof(HAProxyMessage));

    private readonly IResourceLeakTracker<HAProxyMessage> leak;
    private readonly HAProxyProtocolVersion protocolVersion;
    private readonly HAProxyCommand command;
    private readonly HAProxyProxiedProtocol proxiedProtocol;
    private readonly string sourceAddress;
    private readonly string destinationAddress;
    private readonly int sourcePort;
    private readonly int destinationPort;
    private readonly IList<HAProxyTLV> tlvs;

    /**
     * Creates a new instance
     */
    private HAProxyMessage(
            HAProxyProtocolVersion protocolVersion, HAProxyCommand command, HAProxyProxiedProtocol proxiedProtocol,
            string sourceAddress, string destinationAddress, string sourcePort, string destinationPort)
        : this(
                protocolVersion, command, proxiedProtocol,
                sourceAddress, destinationAddress, PortStringToInt(sourcePort), PortStringToInt(destinationPort))
    {
    }

    /**
     * Creates a new instance of HAProxyMessage.
     * @param protocolVersion the protocol version.
     * @param command the command.
     * @param proxiedProtocol the protocol containing the address family and transport protocol.
     * @param sourceAddress the source address.
     * @param destinationAddress the destination address.
     * @param sourcePort the source port. This value must be 0 for unix, unspec addresses.
     * @param destinationPort the destination port. This value must be 0 for unix, unspec addresses.
     */
    public HAProxyMessage(
            HAProxyProtocolVersion protocolVersion, HAProxyCommand command, HAProxyProxiedProtocol proxiedProtocol,
            string sourceAddress, string destinationAddress, int sourcePort, int destinationPort)
        : this(protocolVersion, command, proxiedProtocol,
             sourceAddress, destinationAddress, sourcePort, destinationPort, Array.Empty<HAProxyTLV>())
    {
    }

    /**
     * Creates a new instance of HAProxyMessage.
     * @param protocolVersion the protocol version.
     * @param command the command.
     * @param proxiedProtocol the protocol containing the address family and transport protocol.
     * @param sourceAddress the source address.
     * @param destinationAddress the destination address.
     * @param sourcePort the source port. This value must be 0 for unix, unspec addresses.
     * @param destinationPort the destination port. This value must be 0 for unix, unspec addresses.
     * @param tlvs the list of tlvs.
     */
    public HAProxyMessage(
            HAProxyProtocolVersion protocolVersion, HAProxyCommand command, HAProxyProxiedProtocol proxiedProtocol,
            string sourceAddress, string destinationAddress, int sourcePort, int destinationPort,
            IList<HAProxyTLV> tlvs)
    {

        ObjectUtil.CheckNotNull(protocolVersion, "protocolVersion");
        ObjectUtil.CheckNotNull(proxiedProtocol, "proxiedProtocol");
        ObjectUtil.CheckNotNull(tlvs, "tlvs");

        AddressFamily addrFamily = proxiedProtocol.AddressFamily_();

        CheckAddress(sourceAddress, addrFamily, protocolVersion);
        CheckAddress(destinationAddress, addrFamily, protocolVersion);
        CheckPort(sourcePort, addrFamily);
        CheckPort(destinationPort, addrFamily);

        this.protocolVersion = protocolVersion;
        this.command = command;
        this.proxiedProtocol = proxiedProtocol;
        this.sourceAddress = sourceAddress;
        this.destinationAddress = destinationAddress;
        this.sourcePort = sourcePort;
        this.destinationPort = destinationPort;
        this.tlvs = new ReadOnlyCollection<HAProxyTLV>(tlvs);

        leak = leakDetector.Track(this);
    }

    /**
     * Decodes a version 2, binary proxy protocol header.
     *
     * @param header                     a version 2 proxy protocol header
     * @return                           {@link HAProxyMessage} instance
     * @throws HAProxyProtocolException  if any portion of the header is invalid
     */
    // CLR: Integer.toHexString prints the sign-extended Java byte (a negative byte prints ffffffxx); the C# byte is
    // unsigned, so the text widens it through sbyte to int and uses the lower-case "x" format
    // (https://learn.microsoft.com/dotnet/standard/base-types/standard-numeric-format-strings), which prints a negative
    // int in two's complement as toHexString does (-16325 prints FFFFC03B with "X",
    // https://learn.microsoft.com/dotnet/api/system.int32.tostring).
    // readUnsignedShort() returns ushort, indexOf(int, int, byte) is IndexOf and readerIndex(int) is the ReaderIndex
    // setter of the Buffer port (docs/buffer-porting.md).
    internal static HAProxyMessage DecodeHeader(ByteBuf header)
    {
        ObjectUtil.CheckNotNull(header, "header");

        if (header.ReadableBytes < 16)
        {
            throw new HAProxyProtocolException(
                    "incomplete header: " + header.ReadableBytes.ToString(CultureInfo.InvariantCulture) +
                    " bytes (expected: 16+ bytes)");
        }

        // Per spec, the 13th byte is the protocol version and command byte
        header.SkipBytes(12);
        byte verCmdByte = header.ReadByte();

        HAProxyProtocolVersion ver;

        try
        {
            ver = HAProxyProtocolVersion.ValueOf(verCmdByte);
        }
        catch (ArgumentException e)
        {
            throw new HAProxyProtocolException(e);
        }

        if (ver != HAProxyProtocolVersion.V2)
        {
            throw new HAProxyProtocolException("version 1 unsupported: 0x" +
                    ((int) (sbyte) verCmdByte).ToString("x", CultureInfo.InvariantCulture));
        }

        HAProxyCommand cmd;

        try
        {
            cmd = HAProxyCommand.ValueOf(verCmdByte);
        }
        catch (ArgumentException e)
        {
            throw new HAProxyProtocolException(e);
        }

        if (cmd == HAProxyCommand.LOCAL)
        {
            return UnknownMsg(HAProxyProtocolVersion.V2, HAProxyCommand.LOCAL);
        }

        // Per spec, the 14th byte is the protocol and address family byte
        HAProxyProxiedProtocol protAndFam;

        try
        {
            protAndFam = HAProxyProxiedProtocol.ValueOf(header.ReadByte());
        }
        catch (ArgumentException e)
        {
            throw new HAProxyProtocolException(e);
        }

        if (protAndFam == HAProxyProxiedProtocol.UNKNOWN)
        {
            return UnknownMsg(HAProxyProtocolVersion.V2, HAProxyCommand.PROXY);
        }

        int addressInfoLen = header.ReadUnsignedShort();

        string srcAddress;
        string dstAddress;
        int addressLen;
        int srcPort = 0;
        int dstPort = 0;

        AddressFamily addressFamily = protAndFam.AddressFamily_();

        if (addressFamily == AddressFamily.AF_UNIX)
        {
            // unix sockets require 216 bytes for address information
            if (addressInfoLen < 216 || header.ReadableBytes < 216)
            {
                throw new HAProxyProtocolException(
                    "incomplete UNIX socket address information: " +
                            Math.Min(addressInfoLen, header.ReadableBytes).ToString(CultureInfo.InvariantCulture) +
                            " bytes (expected: 216+ bytes)");
            }

            int startIdx = header.ReaderIndex;
            int addressEnd = header.IndexOf(startIdx, startIdx + 108, (byte) 0); // FIND_NUL
            if (addressEnd == -1)
            {
                addressLen = 108;
            }
            else
            {
                addressLen = addressEnd - startIdx;
            }

            srcAddress = header.ToString(startIdx, addressLen, Encoding.ASCII);

            startIdx += 108;

            addressEnd = header.IndexOf(startIdx, startIdx + 108, (byte) 0); // FIND_NUL
            if (addressEnd == -1)
            {
                addressLen = 108;
            }
            else
            {
                addressLen = addressEnd - startIdx;
            }

            dstAddress = header.ToString(startIdx, addressLen, Encoding.ASCII);
            // AF_UNIX defines that exactly 108 bytes are reserved for the address. The previous methods
            // did not increase the reader index although we already consumed the information.
            header.ReaderIndex = startIdx + 108;
        }
        else
        {
            if (addressFamily == AddressFamily.AF_IPv4)
            {
                // IPv4 requires 12 bytes for address information
                if (addressInfoLen < 12 || header.ReadableBytes < 12)
                {
                    throw new HAProxyProtocolException(
                        "incomplete IPv4 address information: " +
                                Math.Min(addressInfoLen, header.ReadableBytes).ToString(CultureInfo.InvariantCulture) +
                                " bytes (expected: 12+ bytes)");
                }

                addressLen = 4;
            }
            else if (addressFamily == AddressFamily.AF_IPv6)
            {
                // IPv6 requires 36 bytes for address information
                if (addressInfoLen < 36 || header.ReadableBytes < 36)
                {
                    throw new HAProxyProtocolException(
                        "incomplete IPv6 address information: " +
                                Math.Min(addressInfoLen, header.ReadableBytes).ToString(CultureInfo.InvariantCulture) +
                                " bytes (expected: 36+ bytes)");
                }

                addressLen = 16;
            }
            else
            {
                throw new HAProxyProtocolException(
                    "unable to parse address information (unknown address family: " + addressFamily + ')');
            }

            // Per spec, the src address begins at the 17th byte
            srcAddress = IpBytesToString(header, addressLen);
            dstAddress = IpBytesToString(header, addressLen);
            srcPort = header.ReadUnsignedShort();
            dstPort = header.ReadUnsignedShort();
        }

        IList<HAProxyTLV> tlvs = ReadTlvs(header);

        return new HAProxyMessage(ver, cmd, protAndFam, srcAddress, dstAddress, srcPort, dstPort, tlvs);
    }

    private static IList<HAProxyTLV> ReadTlvs(ByteBuf header)
    {
        HAProxyTLV haProxyTLV = ReadNextTLV(header, 0);
        if (haProxyTLV == null)
        {
            return Array.Empty<HAProxyTLV>();
        }

        // In most cases there are less than 4 TLVs available
        List<HAProxyTLV> haProxyTLVs = new List<HAProxyTLV>(4);

        try
        {
            do
            {
                haProxyTLVs.Add(haProxyTLV);

                if (haProxyTLV is HAProxySSLTLV)
                {
                    haProxyTLVs.AddRange(((HAProxySSLTLV) haProxyTLV).EncapsulatedTLVs());
                }
            } while ((haProxyTLV = ReadNextTLV(header, 0)) != null);
        }
        catch (Exception)
        {
            // Release all previously read TLVs before rethrowing as otherwise we would leak.
            ReleaseTlvs(haProxyTLVs);
            throw;
        }

        return haProxyTLVs;
    }

    private static void ReleaseDeep(IList<HAProxyTLV> children)
    {
        foreach (HAProxyTLV child in children)
        {
            child.Release();

            if (child is HAProxySSLTLV)
            {
                ReleaseDeep(((HAProxySSLTLV) child).EncapsulatedTLVs());
            }
        }
    }

    private static void ReleaseTlvs(IList<HAProxyTLV> tlvs)
    {
        int skip = 0;

        foreach (HAProxyTLV tlv in tlvs)
        {
            if (skip > 0)
            {
                skip--;

                // This TLV is a flattened depth-1 child. If it encapsulates anything (depth-2+),
                // those deeper children were NOT flattened, so we must release them recursively.
                if (tlv is HAProxySSLTLV)
                {
                    ReleaseDeep(((HAProxySSLTLV) tlv).EncapsulatedTLVs());
                }
            }
            else if (tlv is HAProxySSLTLV)
            {
                // This is a top-level (depth-0) SSL TLV.
                // Its immediate children (depth-1) were flattened into this list,
                // so we must skip them in the outer loop to avoid treating them as top-level TLVs.
                skip = ((HAProxySSLTLV) tlv).EncapsulatedTLVs().Count;
            }

            tlv.Release();
        }
    }

    // CLR: the switch over the HAProxyTLV.Type constants uses case guards (see the class comment); the type byte stays
    // a byte and ByteBuf.retain() returns the buffer, as in the source.
    private static HAProxyTLV ReadNextTLV(ByteBuf header, int nestingLevel)
    {
        if (nestingLevel > MAX_NESTING_LEVEL)
        {
            throw new HAProxyProtocolException(
                    "Maximum TLV nesting level reached: " + nestingLevel.ToString(CultureInfo.InvariantCulture) +
                    " (expected: < " + MAX_NESTING_LEVEL.ToString(CultureInfo.InvariantCulture) + ')');
        }
        // We need at least 4 bytes for a TLV
        if (header.ReadableBytes < 4)
        {
            return null;
        }

        byte typeAsByte = header.ReadByte();
        HAProxyTLV.Type type = HAProxyTLV.Type.TypeForByteValue(typeAsByte);

        int length = header.ReadUnsignedShort();

        switch (type)
        {
        case var _ when type == HAProxyTLV.Type.PP2_TYPE_SSL:
            if (length < 5)
            {
                throw new HAProxyProtocolException("TLV length must be at least 5 but was: " +
                        length.ToString(CultureInfo.InvariantCulture));
            }
            if (length > header.ReadableBytes)
            {
                throw new HAProxyProtocolException("TLV length must be smaller or equal the readable bytes (" +
                        header.ReadableBytes.ToString(CultureInfo.InvariantCulture) + ") but was: " +
                        length.ToString(CultureInfo.InvariantCulture));
            }

            // Slice the rawContent but only retain it if we didn't see an error as otherwise we might
            // leak.
            ByteBuf rawContent = header.Slice(header.ReaderIndex, length);
            ByteBuf byteBuf = header.ReadSlice(length);
            byte client = byteBuf.ReadByte();
            int verify = byteBuf.ReadInt();

            if (byteBuf.ReadableBytes >= 4)
            {

                List<HAProxyTLV> encapsulatedTlvs = new List<HAProxyTLV>(4);

                try
                {
                    do
                    {
                        HAProxyTLV haProxyTLV = ReadNextTLV(byteBuf, nestingLevel + 1);
                        if (haProxyTLV == null)
                        {
                            break;
                        }

                        encapsulatedTlvs.Add(haProxyTLV);
                    } while (byteBuf.ReadableBytes >= 4);
                }
                    catch (Exception)
                {
                    // Release all previously read TLVs before rethrowing as otherwise we would leak.
                    ReleaseDeep(encapsulatedTlvs);
                    throw;
                }

                return new HAProxySSLTLV(verify, client, encapsulatedTlvs, rawContent.Retain());
            }

            return new HAProxySSLTLV(verify, client, Array.Empty<HAProxyTLV>(), rawContent.Retain());
        // If we're not dealing with an SSL Type, we can use the same mechanism
        case var _ when type == HAProxyTLV.Type.PP2_TYPE_ALPN:
        case var _ when type == HAProxyTLV.Type.PP2_TYPE_AUTHORITY:
        case var _ when type == HAProxyTLV.Type.PP2_TYPE_SSL_VERSION:
        case var _ when type == HAProxyTLV.Type.PP2_TYPE_SSL_CN:
        case var _ when type == HAProxyTLV.Type.PP2_TYPE_NETNS:
        case var _ when type == HAProxyTLV.Type.OTHER:
            return new HAProxyTLV(type, typeAsByte, header.ReadRetainedSlice(length));
        default:
            return null;
        }
    }

    /**
     * Decodes a version 1, human-readable proxy protocol header.
     *
     * @param header                     a version 1 proxy protocol header
     * @return                           {@link HAProxyMessage} instance
     * @throws HAProxyProtocolException  if any portion of the header is invalid
     */
    // CLR: String.split(" ") returns the whole string when the separator does not occur and otherwise drops the
    // trailing empty strings ("Trailing empty strings are therefore not included in the resulting array",
    // https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html#split(java.lang.String));
    // string.Split(' ') keeps them, so they are removed after a split. HAProxyProxiedProtocol.valueOf(String) is the
    // implicit enum method, ported as HAProxyProxiedProtocol.ValueOf(string).
    internal static HAProxyMessage DecodeHeader(string header)
    {
        if (header == null)
        {
            throw new HAProxyProtocolException("header");
        }

        string[] parts = header.Split(' ');
        if (parts.Length > 1)
        {
            int count = parts.Length;

            while (count > 0 && parts[count - 1].Length == 0)
            {
                count--;
            }

            Array.Resize(ref parts, count);
        }

        int numParts = parts.Length;

        if (numParts < 2)
        {
            throw new HAProxyProtocolException(
                    "invalid header: " + header + " (expected: 'PROXY' and proxied protocol values)");
        }

        if (!"PROXY".Equals(parts[0]))
        {
            throw new HAProxyProtocolException("unknown identifier: " + parts[0]);
        }

        HAProxyProxiedProtocol protAndFam;

        try
        {
            protAndFam = HAProxyProxiedProtocol.ValueOf(parts[1]);
        }
        catch (ArgumentException e)
        {
            throw new HAProxyProtocolException(e);
        }

        if (protAndFam != HAProxyProxiedProtocol.TCP4 &&
                protAndFam != HAProxyProxiedProtocol.TCP6 &&
                protAndFam != HAProxyProxiedProtocol.UNKNOWN)
        {
            throw new HAProxyProtocolException("unsupported v1 proxied protocol: " + parts[1]);
        }

        if (protAndFam == HAProxyProxiedProtocol.UNKNOWN)
        {
            return UnknownMsg(HAProxyProtocolVersion.V1, HAProxyCommand.PROXY);
        }

        if (numParts != 6)
        {
            throw new HAProxyProtocolException("invalid TCP4/6 header: " + header + " (expected: 6 parts)");
        }

        try
        {
            return new HAProxyMessage(
                    HAProxyProtocolVersion.V1, HAProxyCommand.PROXY,
                    protAndFam, parts[2], parts[3], parts[4], parts[5]);
        }
        catch (Exception e)
        {
            throw new HAProxyProtocolException("invalid HAProxy message", e);
        }
    }

    /**
     * Proxy protocol message for 'UNKNOWN' proxied protocols. Per spec, when the proxied protocol is
     * 'UNKNOWN' we must discard all other header values.
     */
    private static HAProxyMessage UnknownMsg(HAProxyProtocolVersion version, HAProxyCommand command)
    {
        return new HAProxyMessage(version, command, HAProxyProxiedProtocol.UNKNOWN, null, null, 0, 0);
    }

    /**
     * Convert ip address bytes to string representation
     *
     * @param header     buffer containing ip address bytes
     * @param addressLen number of bytes to read (4 bytes for IPv4, 16 bytes for IPv6)
     * @return           string representation of the ip address
     */
    // CLR: Integer.toHexString of the unsigned short is the lower-case "x" format; numbers use the invariant culture.
    private static string IpBytesToString(ByteBuf header, int addressLen)
    {
        StringBuilder sb = new StringBuilder();
        const int ipv4Len = 4;
        const int ipv6Len = 8;

        if (addressLen == ipv4Len)
        {
            for (int i = 0; i < ipv4Len; i++)
            {
                sb.Append((header.ReadByte() & 0xff).ToString(CultureInfo.InvariantCulture));
                sb.Append('.');
            }
        }
        else
        {
            for (int i = 0; i < ipv6Len; i++)
            {
                sb.Append(header.ReadUnsignedShort().ToString("x", CultureInfo.InvariantCulture));
                sb.Append(':');
            }
        }

        sb.Length = sb.Length - 1;
        return sb.ToString();
    }

    /**
     * Convert port to integer
     *
     * @param value                      the port
     * @return                           port as an integer
     * @throws IllegalArgumentException  if port is not a valid integer
     */
    // CLR: Integer.parseInt accepts an optional sign and decimal digits and throws NumberFormatException for null,
    // malformed or out-of-range text; int.Parse with NumberStyles.AllowLeadingSign and the invariant culture
    // (https://learn.microsoft.com/dotnet/api/system.int32.parse) throws ArgumentNullException, FormatException or
    // OverflowException for those cases, which are all caught here.
    private static int PortStringToInt(string value)
    {
        int port;

        try
        {
            port = int.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is FormatException || e is OverflowException || e is ArgumentNullException)
        {
            throw new ArgumentException("invalid port: " + (value ?? "null"), e);
        }

        if (port <= 0 || port > 65535)
        {
            throw new ArgumentException("invalid port: " + value + " (expected: 1 ~ 65535)");
        }

        return port;
    }

    /**
     * Validate an address (IPv4, IPv6, Unix Socket)
     *
     * @param address    human-readable address
     * @param addrFamily the {@link AddressFamily} to check the address against
     * @param version    the protocol version
     * @throws IllegalArgumentException if the address is invalid
     */
    // CLR: a null address in the messages is written as "null", as Java string conversion does
    // (https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11).
    private static void CheckAddress(string address, AddressFamily addrFamily, HAProxyProtocolVersion version)
    {
        ObjectUtil.CheckNotNull(addrFamily, "addrFamily");

        switch (addrFamily)
        {
            case var _ when addrFamily == AddressFamily.AF_UNSPEC:
                if (address != null)
                {
                    throw new ArgumentException("unable to validate an AF_UNSPEC address: " + address);
                }

                return;
            case var _ when addrFamily == AddressFamily.AF_UNIX:
                ObjectUtil.CheckNotNull(address, "address");
                if (Encoding.ASCII.GetBytes(address).Length > 108)
                {
                    throw new ArgumentException("invalid AF_UNIX address: " + address);
                }

                if (version == HAProxyProtocolVersion.V1)
                {
                    // V1 is text-based and uses CR LF as header delimiters, and space as field delimiter.
                    for (int i = 0, len = address.Length; i < len; i++)
                    {
                        char c = address[i];
                        if (c == '\r' || c == '\n' || c == ' ')
                        {
                            throw new ArgumentException("invalid AF_UNIX address: " + address);
                        }
                    }
                }

                return;
        }

        ObjectUtil.CheckNotNull(address, "address");

        switch (addrFamily)
        {
            case var _ when addrFamily == AddressFamily.AF_IPv4:
                if (!NetUtil.IsValidIpV4Address(address))
                {
                    throw new ArgumentException("invalid IPv4 address: " + address);
                }

                break;
            case var _ when addrFamily == AddressFamily.AF_IPv6:
                if (!NetUtil.IsValidIpV6Address(address))
                {
                    throw new ArgumentException("invalid IPv6 address: " + address);
                }

                break;
            default:
                throw new ArgumentException("unexpected addrFamily: " + addrFamily);
        }
    }

    /**
     * Validate the port depending on the addrFamily.
     *
     * @param port                       the UDP/TCP port
     * @throws IllegalArgumentException  if the port is out of range (0-65535 inclusive)
     */
    private static void CheckPort(int port, AddressFamily addrFamily)
    {
        switch (addrFamily)
        {
        case var _ when addrFamily == AddressFamily.AF_IPv6:
        case var _ when addrFamily == AddressFamily.AF_IPv4:
            if (port < 0 || port > 65535)
            {
                throw new ArgumentException("invalid port: " + port.ToString(CultureInfo.InvariantCulture) +
                        " (expected: 0 ~ 65535)");
            }

            break;
        case var _ when addrFamily == AddressFamily.AF_UNIX:
        case var _ when addrFamily == AddressFamily.AF_UNSPEC:
            if (port != 0)
            {
                throw new ArgumentException("port cannot be specified with addrFamily: " + addrFamily);
            }

            break;
        default:
            throw new ArgumentException("unexpected addrFamily: " + (addrFamily?.ToString() ?? "null"));
        }
    }

    /**
     * Returns the {@link HAProxyProtocolVersion} of this {@link HAProxyMessage}.
     */
    public HAProxyProtocolVersion ProtocolVersion()
    {
        return protocolVersion;
    }

    /**
     * Returns the {@link HAProxyCommand} of this {@link HAProxyMessage}.
     */
    public HAProxyCommand Command()
    {
        return command;
    }

    /**
     * Returns the {@link HAProxyProxiedProtocol} of this {@link HAProxyMessage}.
     */
    public HAProxyProxiedProtocol ProxiedProtocol()
    {
        return proxiedProtocol;
    }

    /**
     * Returns the human-readable source address of this {@link HAProxyMessage} or {@code null}
     * if HAProxy performs health check with {@code send-proxy-v2}.
     */
    public string SourceAddress()
    {
        return sourceAddress;
    }

    /**
     * Returns the human-readable destination address of this {@link HAProxyMessage}.
     */
    public string DestinationAddress()
    {
        return destinationAddress;
    }

    /**
     * Returns the UDP/TCP source port of this {@link HAProxyMessage}.
     */
    public int SourcePort()
    {
        return sourcePort;
    }

    /**
     * Returns the UDP/TCP destination port of this {@link HAProxyMessage}.
     */
    public int DestinationPort()
    {
        return destinationPort;
    }

    /**
     * Returns a list of {@link HAProxyTLV} or an empty list if no TLVs are present.
     * <p>
     * TLVs are only available for the Proxy Protocol V2
     */
    public IList<HAProxyTLV> Tlvs()
    {
        return tlvs;
    }

    internal int TlvNumBytes()
    {
        int tlvNumBytes = 0;

        for (int i = 0; i < tlvs.Count; i += 1 + FlattenedChildren(tlvs, i))
        {
            tlvNumBytes += tlvs[i].TotalNumBytes();
        }

        return tlvNumBytes;
    }

    /**
     * Returns how many entries directly following {@link HAProxySSLTLV} at {@code index}
     * are its encapsulated TLVs.
     */
    internal static int FlattenedChildren(IList<HAProxyTLV> tlvs, int index)
    {
        HAProxyTLV tlv = tlvs[index];
        if (!(tlv is HAProxySSLTLV))
        {
            return 0;
        }

        IList<HAProxyTLV> children = ((HAProxySSLTLV) tlv).EncapsulatedTLVs();
        int n = 0;

        while (n < children.Count && (index + 1 + n) < tlvs.Count &&
                children[n] == tlvs[index + 1 + n])
        {
            n++;
        }

        return n;
    }

    // CLR: the retain()/touch() overrides narrow the IReferenceCounted results of AbstractReferenceCounted with covariant
    // returns (https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/override).
    public override HAProxyMessage Touch()
    {
        TryRecord();
        return (HAProxyMessage) base.Touch();
    }

    public override HAProxyMessage Touch(object hint)
    {
        if (leak != null)
        {
            leak.Record(hint);
        }

        return this;
    }

    public override HAProxyMessage Retain()
    {
        TryRecord();
        return (HAProxyMessage) base.Retain();
    }

    public override HAProxyMessage Retain(int increment)
    {
        TryRecord();
        return (HAProxyMessage) base.Retain(increment);
    }

    public override bool Release()
    {
        TryRecord();

        return base.Release();
    }

    public override bool Release(int decrement)
    {
        TryRecord();

        return base.Release(decrement);
    }

    private void TryRecord()
    {
        if (leak != null)
        {
            leak.Record();
        }
    }

    // CLR: assert is Debug.Assert, as in codec-base (DatagramPacketEncoder).
    protected override void Deallocate()
    {
        try
        {
            ReleaseTlvs(tlvs);
        }
        finally
        {
            IResourceLeakTracker<HAProxyMessage> leak = this.leak;
            if (leak != null)
            {
                bool closed = leak.Close(this);
                Debug.Assert(closed);
            }
        }
    }

    // CLR: Java string conversion writes null as "null" (https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11);
    // numbers use the invariant culture.
    public override string ToString()
    {
        StringBuilder sb = new StringBuilder(256)
                .Append(StringUtil.SimpleClassName(this))
                .Append("(protocolVersion: ").Append(protocolVersion)
                .Append(", command: ").Append(command?.ToString() ?? "null")
                .Append(", proxiedProtocol: ").Append(proxiedProtocol)
                .Append(", sourceAddress: ").Append(sourceAddress ?? "null")
                .Append(", destinationAddress: ").Append(destinationAddress ?? "null")
                .Append(", sourcePort: ").Append(sourcePort.ToString(CultureInfo.InvariantCulture))
                .Append(", destinationPort: ").Append(destinationPort.ToString(CultureInfo.InvariantCulture))
                .Append(", tlvs: [");

        if (tlvs.Count != 0)
        {
            foreach (HAProxyTLV tlv in tlvs)
            {
                sb.Append(tlv).Append(", ");
            }

            sb.Length = sb.Length - 2;
        }

        sb.Append("])");

        return sb.ToString();
    }
}
