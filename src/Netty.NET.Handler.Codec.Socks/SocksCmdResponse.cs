/*
 * Copyright 2012 The Netty Project
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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Socks;

/**
 * A socks cmd response.
 *
 * @see SocksCmdRequest
 * @see SocksCmdResponseDecoder
 */
// CLR: java.net.IDN is the shared Netty.NET.Common.Internal.IDN over IdnMapping with the IDN results; the switches over
// SocksAddressType are if/else chains (see SocksAddressType.cs); IllegalArgumentException is ArgumentException,
// CharsetUtil.US_ASCII is Encoding.ASCII (docs/codec-base-porting.md), and numbers in messages use the invariant
// culture (see SocksCmdRequest).
public sealed class SocksCmdResponse : SocksResponse
{
    private readonly SocksCmdStatus cmdStatus;

    private readonly SocksAddressType addressType;
    private readonly string host;
    private readonly int port;

    // All arrays are initialized on construction time to 0/false/null remove array Initialization
    private static readonly byte[] DOMAIN_ZEROED = { 0x00 };
    private static readonly byte[] IPv4_HOSTNAME_ZEROED = { 0x00, 0x00, 0x00, 0x00 };
    private static readonly byte[] IPv6_HOSTNAME_ZEROED = { 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00 };

    public SocksCmdResponse(SocksCmdStatus cmdStatus, SocksAddressType addressType) : this(cmdStatus, addressType, null, 0)
    {
    }

    /**
     * Constructs new response and includes provided host and port as part of it.
     *
     * @param cmdStatus status of the response
     * @param addressType type of host parameter
     * @param host host (BND.ADDR field) is address that server used when connecting to the target host.
     *             When null a value of 4/8 0x00 octets will be used for IPv4/IPv6 and a single 0x00 byte will be
     *             used for domain addressType. Value is converted to ASCII using {@link IDN#toASCII(String)}.
     * @param port port (BND.PORT field) that the server assigned to connect to the target host
     * @throws NullPointerException in case cmdStatus or addressType are missing
     * @throws IllegalArgumentException in case host or port cannot be validated
     * @see IDN#toASCII(String)
     */
    public SocksCmdResponse(SocksCmdStatus cmdStatus, SocksAddressType addressType, string host, int port)
        : base(SocksResponseType.CMD)
    {
        ObjectUtil.CheckNotNull(cmdStatus, "cmdStatus");
        ObjectUtil.CheckNotNull(addressType, "addressType");
        if (host != null)
        {
            if (addressType == SocksAddressType.IPv4)
            {
                if (!NetUtil.IsValidIpV4Address(host))
                {
                    throw new ArgumentException(host + " is not a valid IPv4 address");
                }
            }
            else if (addressType == SocksAddressType.DOMAIN)
            {
                string asciiHost = IDN.ToASCII(host);
                if (asciiHost.Length > 255)
                {
                    throw new ArgumentException(host + " IDN: " + asciiHost + " exceeds 255 char limit");
                }
                host = asciiHost;
            }
            else if (addressType == SocksAddressType.IPv6)
            {
                if (!NetUtil.IsValidIpV6Address(host))
                {
                    throw new ArgumentException(host + " is not a valid IPv6 address");
                }
            }
            else if (addressType == SocksAddressType.UNKNOWN)
            {
            }
        }
        if (port < 0 || port > 65535)
        {
            throw new ArgumentException(port.ToString(CultureInfo.InvariantCulture) + " is not in bounds 0 <= x <= 65535");
        }
        this.cmdStatus = cmdStatus;
        this.addressType = addressType;
        this.host = host;
        this.port = port;
    }

    /**
     * Returns the {@link SocksCmdStatus} of this {@link SocksCmdResponse}
     *
     * @return The {@link SocksCmdStatus} of this {@link SocksCmdResponse}
     */
    public SocksCmdStatus CmdStatus()
    {
        return cmdStatus;
    }

    /**
     * Returns the {@link SocksAddressType} of this {@link SocksCmdResponse}
     *
     * @return The {@link SocksAddressType} of this {@link SocksCmdResponse}
     */
    public SocksAddressType AddressType()
    {
        return addressType;
    }

    /**
     * Returns host that is used as a parameter in {@link SocksCmdType}.
     * Host (BND.ADDR field in response) is address that server used when connecting to the target host.
     * This is typically different from address which client uses to connect to the SOCKS server.
     *
     * @return host that is used as a parameter in {@link SocksCmdType}
     *         or null when there was no host specified during response construction
     */
    public string Host()
    {
        return host != null && addressType == SocksAddressType.DOMAIN ? IDN.ToUnicode(host) : host;
    }

    /**
     * Returns port that is used as a parameter in {@link SocksCmdType}.
     * Port (BND.PORT field in response) is port that the server assigned to connect to the target host.
     *
     * @return port that is used as a parameter in {@link SocksCmdType}
     */
    public int Port()
    {
        return port;
    }

    // CLR: the override of the deprecated encodeAsByteBuf is not marked [Obsolete] (see UnknownSocksRequest).
#pragma warning disable CS0672
    public override void EncodeAsByteBuf(ByteBuf byteBuf)
#pragma warning restore CS0672
    {
        byteBuf.WriteByte(ProtocolVersion().ByteValue());
        byteBuf.WriteByte(cmdStatus.ByteValue());
        byteBuf.WriteByte(0x00);
        byteBuf.WriteByte(addressType.ByteValue());
        if (addressType == SocksAddressType.IPv4)
        {
            byte[] hostContent = host == null ?
                    IPv4_HOSTNAME_ZEROED : NetUtil.CreateByteArrayFromIpAddressString(host);
            byteBuf.WriteBytes(hostContent);
            ByteBufUtil.WriteShortBE(byteBuf, port);
        }
        else if (addressType == SocksAddressType.DOMAIN)
        {
            if (host != null)
            {
                byteBuf.WriteByte(host.Length);
                byteBuf.WriteCharSequence(host, Encoding.ASCII);
            }
            else
            {
                byteBuf.WriteByte(DOMAIN_ZEROED.Length);
                byteBuf.WriteBytes(DOMAIN_ZEROED);
            }
            ByteBufUtil.WriteShortBE(byteBuf, port);
        }
        else if (addressType == SocksAddressType.IPv6)
        {
            byte[] hostContent = host == null
                    ? IPv6_HOSTNAME_ZEROED : NetUtil.CreateByteArrayFromIpAddressString(host);
            byteBuf.WriteBytes(hostContent);
            ByteBufUtil.WriteShortBE(byteBuf, port);
        }
    }
}
