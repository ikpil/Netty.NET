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
 * The default {@link Socks5CommandResponse}.
 */
// CLR: java.net.IDN is the shared Netty.NET.Common.Internal.IDN over IdnMapping with the IDN results;
// IllegalArgumentException is ArgumentException and numbers in messages use the invariant culture (see
// Netty.NET.Handler.Codec.Socks.SocksCmdRequest); StringUtil.simpleClassName is StringUtil.SimpleClassName,
// StringBuilder.append(int) appends a non-negative port and a null bndAddr is appended as "null" (see
// Netty.NET.Handler.Codec.Socksx.V4.DefaultSocks4CommandResponse).
public sealed class DefaultSocks5CommandResponse : AbstractSocks5Message, ISocks5CommandResponse
{

    private readonly Socks5CommandStatus status;
    private readonly Socks5AddressType bndAddrType;
    private readonly string bndAddr;
    private readonly int bndPort;

    public DefaultSocks5CommandResponse(Socks5CommandStatus status, Socks5AddressType bndAddrType)
        : this(status, bndAddrType, null, 0)
    {
    }

    public DefaultSocks5CommandResponse(
            Socks5CommandStatus status, Socks5AddressType bndAddrType, string bndAddr, int bndPort)
    {

        ObjectUtil.CheckNotNull(status, "status");
        ObjectUtil.CheckNotNull(bndAddrType, "bndAddrType");

        if (bndAddr != null)
        {
            if (bndAddrType == Socks5AddressType.IPv4)
            {
                if (!NetUtil.IsValidIpV4Address(bndAddr))
                {
                    throw new ArgumentException("bndAddr: " + bndAddr + " (expected: a valid IPv4 address)");
                }
            }
            else if (bndAddrType == Socks5AddressType.DOMAIN)
            {
                bndAddr = IDN.ToASCII(bndAddr);
                if (bndAddr.Length > 255)
                {
                    throw new ArgumentException("bndAddr: " + bndAddr + " (expected: less than 256 chars)");
                }
            }
            else if (bndAddrType == Socks5AddressType.IPv6)
            {
                if (!NetUtil.IsValidIpV6Address(bndAddr))
                {
                    throw new ArgumentException("bndAddr: " + bndAddr + " (expected: a valid IPv6 address)");
                }
            }
        }

        if (bndPort < 0 || bndPort > 65535)
        {
            throw new ArgumentException("bndPort: " + bndPort.ToString(CultureInfo.InvariantCulture) + " (expected: 0~65535)");
        }

        this.status = status;
        this.bndAddrType = bndAddrType;
        this.bndAddr = bndAddr;
        this.bndPort = bndPort;
    }

    public Socks5CommandStatus Status()
    {
        return status;
    }

    public Socks5AddressType BndAddrType()
    {
        return bndAddrType;
    }

    public string BndAddr()
    {
        return bndAddr;
    }

    public int BndPort()
    {
        return bndPort;
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
            buf.Append(", status: ");
        }
        else
        {
            buf.Append("(status: ");
        }

        buf.Append(Status());
        buf.Append(", bndAddrType: ");
        buf.Append(BndAddrType());
        buf.Append(", bndAddr: ");
        buf.Append(BndAddr() ?? "null");
        buf.Append(", bndPort: ");
        buf.Append(BndPort());
        buf.Append(')');

        return buf.ToString();
    }
}
