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
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Socks;

namespace Netty.NET.Handler.Codec.Socksx.V4;

/**
 * The default {@link Socks4CommandRequest}.
 */
// CLR: java.net.IDN is the shared Netty.NET.Common.Internal.IDN over IdnMapping with the IDN results;
// IllegalArgumentException is ArgumentException and numbers in messages use the invariant culture (see
// SocksCmdRequest); StringUtil.simpleClassName is StringUtil.SimpleClassName and StringBuilder.append(int) appends a
// non-negative port.
// CLR: a Java instance method that is not final can be overridden (JLS 8.4.3.3: "A method can be declared final to
// prevent subclasses from overriding or hiding it", https://docs.oracle.com/javase/specs/jls/se21/html/jls-8.html), while
// in C# "By default, methods are non-virtual. You can't override a non-virtual method"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/virtual), so the accessors of this
// non-final class are virtual (Socks5ClientEncoderTest overrides username()/password() of
// DefaultSocks5PasswordAuthRequest).
public class DefaultSocks4CommandRequest : AbstractSocks4Message, ISocks4CommandRequest
{

    private readonly Socks4CommandType type;
    private readonly string dstAddr;
    private readonly int dstPort;
    private readonly string userId;

    /**
     * Creates a new instance.
     *
     * @param type the type of the request
     * @param dstAddr the {@code DSTIP} field of the request
     * @param dstPort the {@code DSTPORT} field of the request
     */
    public DefaultSocks4CommandRequest(Socks4CommandType type, string dstAddr, int dstPort)
        : this(type, dstAddr, dstPort, "")
    {
    }

    /**
     * Creates a new instance.
     *
     * @param type the type of the request
     * @param dstAddr the {@code DSTIP} field of the request
     * @param dstPort the {@code DSTPORT} field of the request
     * @param userId the {@code USERID} field of the request
     */
    public DefaultSocks4CommandRequest(Socks4CommandType type, string dstAddr, int dstPort, string userId)
    {
        if (dstPort <= 0 || dstPort >= 65536)
        {
            throw new ArgumentException(
                    "dstPort: " + dstPort.ToString(CultureInfo.InvariantCulture) + " (expected: 1~65535)");
        }

        this.type = ObjectUtil.CheckNotNull(type, "type");
        this.dstAddr = IDN.ToASCII(
                ObjectUtil.CheckNotNull(dstAddr, "dstAddr"));
        this.userId = ObjectUtil.CheckNotNull(userId, "userId");
        this.dstPort = dstPort;
    }

    public virtual Socks4CommandType Type()
    {
        return type;
    }

    public virtual string DstAddr()
    {
        return dstAddr;
    }

    public virtual int DstPort()
    {
        return dstPort;
    }

    public virtual string UserId()
    {
        return userId;
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
        buf.Append(", dstAddr: ");
        buf.Append(DstAddr());
        buf.Append(", dstPort: ");
        buf.Append(DstPort());
        buf.Append(", userId: ");
        buf.Append(UserId());
        buf.Append(')');

        return buf.ToString();
    }
}
