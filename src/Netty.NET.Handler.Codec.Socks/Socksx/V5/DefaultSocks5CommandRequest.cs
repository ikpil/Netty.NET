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
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Socks;

namespace Netty.NET.Handler.Codec.Socksx.V5;

/**
 * The default {@link Socks5CommandRequest}.
 */
// CLR: java.net.IDN is the shared Netty.NET.Common.Internal.IDN over IdnMapping with the IDN results;
// IllegalArgumentException is ArgumentException and numbers in messages use the invariant culture (see
// Netty.NET.Handler.Codec.Socks.SocksCmdRequest); StringUtil.simpleClassName is StringUtil.SimpleClassName and
// StringBuilder.append(int) appends a non-negative port.
public sealed class DefaultSocks5CommandRequest : AbstractSocks5Message, ISocks5CommandRequest
{

    private readonly Socks5CommandType type;
    private readonly Socks5AddressType dstAddrType;
    private readonly string dstAddr;
    private readonly int dstPort;

    public DefaultSocks5CommandRequest(
            Socks5CommandType type, Socks5AddressType dstAddrType, string dstAddr, int dstPort)
    {

        this.type = ObjectUtil.CheckNotNull(type, "type");
        ObjectUtil.CheckNotNull(dstAddrType, "dstAddrType");
        ObjectUtil.CheckNotNull(dstAddr, "dstAddr");

        if (dstAddrType == Socks5AddressType.IPv4)
        {
            if (!NetUtil.IsValidIpV4Address(dstAddr))
            {
                throw new ArgumentException("dstAddr: " + dstAddr + " (expected: a valid IPv4 address)");
            }
        }
        else if (dstAddrType == Socks5AddressType.DOMAIN)
        {
            dstAddr = IDN.ToASCII(dstAddr);
            if (dstAddr.Length > 255)
            {
                throw new ArgumentException("dstAddr: " + dstAddr + " (expected: less than 256 chars)");
            }
        }
        else if (dstAddrType == Socks5AddressType.IPv6)
        {
            if (!NetUtil.IsValidIpV6Address(dstAddr))
            {
                throw new ArgumentException("dstAddr: " + dstAddr + " (expected: a valid IPv6 address");
            }
        }

        if (dstPort < 0 || dstPort > 65535)
        {
            throw new ArgumentException("dstPort: " + dstPort.ToString(CultureInfo.InvariantCulture) + " (expected: 0~65535)");
        }

        this.dstAddrType = dstAddrType;
        this.dstAddr = dstAddr;
        this.dstPort = dstPort;
    }

    public Socks5CommandType Type()
    {
        return type;
    }

    public Socks5AddressType DstAddrType()
    {
        return dstAddrType;
    }

    public string DstAddr()
    {
        return dstAddr;
    }

    public int DstPort()
    {
        return dstPort;
    }

    public override string ToString()
    {
        StringBuilder buf = new StringBuilder(128);
        buf.Append(StringUtil.SimpleClassName(this));

        DecoderResult decoderResult = DecoderResult();
        if (!decoderResult.IsSuccess())
        {
            buf.Append("(decoderResult: ");
            buf.Append(decoderResult);
            buf.Append(", type: ");
        }
        else
        {
            buf.Append("(type: ");
        }

        buf.Append(Type());
        buf.Append(", dstAddrType: ");
        buf.Append(DstAddrType());
        buf.Append(", dstAddr: ");
        buf.Append(DstAddr());
        buf.Append(", dstPort: ");
        buf.Append(DstPort());
        buf.Append(')');

        return buf.ToString();
    }
}
