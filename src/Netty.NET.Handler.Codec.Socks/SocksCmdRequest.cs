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
 * An socks cmd request.
 *
 * @see SocksCmdResponse
 * @see SocksCmdRequestDecoder
 */
// CLR: java.net.IDN is the shared Netty.NET.Common.Internal.IDN over IdnMapping with the IDN results; the switches over
// SocksAddressType are if/else chains (see SocksAddressType.cs); IllegalArgumentException is ArgumentException,
// CharsetUtil.US_ASCII is Encoding.ASCII (docs/codec-base-porting.md), and numbers in messages use the invariant
// culture (Int32.ToString() otherwise uses "the NumberFormatInfo object of the current culture",
// https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring?view=net-10.0).
public sealed class SocksCmdRequest : SocksRequest
{
    private readonly SocksCmdType cmdType;
    private readonly SocksAddressType addressType;
    private readonly string host;
    private readonly int port;

    public SocksCmdRequest(SocksCmdType cmdType, SocksAddressType addressType, string host, int port)
        : base(SocksRequestType.CMD)
    {
        ObjectUtil.CheckNotNull(cmdType, "cmdType");
        ObjectUtil.CheckNotNull(addressType, "addressType");
        ObjectUtil.CheckNotNull(host, "host");

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
        if (port <= 0 || port >= 65536)
        {
            throw new ArgumentException(port.ToString(CultureInfo.InvariantCulture) + " is not in bounds 0 < x < 65536");
        }
        this.cmdType = cmdType;
        this.addressType = addressType;
        this.host = host;
        this.port = port;
    }

    /**
     * Returns the {@link SocksCmdType} of this {@link SocksCmdRequest}
     *
     * @return The {@link SocksCmdType} of this {@link SocksCmdRequest}
     */
    public SocksCmdType CmdType()
    {
        return cmdType;
    }

    /**
     * Returns the {@link SocksAddressType} of this {@link SocksCmdRequest}
     *
     * @return The {@link SocksAddressType} of this {@link SocksCmdRequest}
     */
    public SocksAddressType AddressType()
    {
        return addressType;
    }

    /**
     * Returns host that is used as a parameter in {@link SocksCmdType}
     *
     * @return host that is used as a parameter in {@link SocksCmdType}
     */
    public string Host()
    {
        return addressType == SocksAddressType.DOMAIN ? IDN.ToUnicode(host) : host;
    }

    /**
     * Returns port that is used as a parameter in {@link SocksCmdType}
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
        byteBuf.WriteByte(cmdType.ByteValue());
        byteBuf.WriteByte(0x00);
        byteBuf.WriteByte(addressType.ByteValue());
        if (addressType == SocksAddressType.IPv4)
        {
            byteBuf.WriteBytes(NetUtil.CreateByteArrayFromIpAddressString(host));
            ByteBufUtil.WriteShortBE(byteBuf, port);
        }
        else if (addressType == SocksAddressType.DOMAIN)
        {
            byteBuf.WriteByte(host.Length);
            byteBuf.WriteCharSequence(host, Encoding.ASCII);
            ByteBufUtil.WriteShortBE(byteBuf, port);
        }
        else if (addressType == SocksAddressType.IPv6)
        {
            byteBuf.WriteBytes(NetUtil.CreateByteArrayFromIpAddressString(host));
            ByteBufUtil.WriteShortBE(byteBuf, port);
        }
    }
}
