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

namespace Netty.NET.Handler.Codec.Socksx.V4;

/**
 * The default {@link Socks4CommandResponse}.
 */
// CLR: IllegalArgumentException is ArgumentException and numbers in messages use the invariant culture (see
// Netty.NET.Handler.Codec.Socks.SocksCmdRequest); StringUtil.simpleClassName is StringUtil.SimpleClassName and
// StringBuilder.append(int) appends a non-negative port; a null dstAddr is appended as "null".
// CLR: a Java instance method that is not final can be overridden (JLS 8.4.3.3: "A method can be declared final to
// prevent subclasses from overriding or hiding it", https://docs.oracle.com/javase/specs/jls/se21/html/jls-8.html), while
// in C# "By default, methods are non-virtual. You can't override a non-virtual method"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/virtual), so the accessors of this
// non-final class are virtual (Socks5ClientEncoderTest overrides username()/password() of
// DefaultSocks5PasswordAuthRequest).
public class DefaultSocks4CommandResponse : AbstractSocks4Message, ISocks4CommandResponse
{

    private readonly Socks4CommandStatus status;
    private readonly string dstAddr;
    private readonly int dstPort;

    /**
     * Creates a new instance.
     *
     * @param status the status of the response
     */
    public DefaultSocks4CommandResponse(Socks4CommandStatus status) : this(status, null, 0)
    {
    }

    /**
     * Creates a new instance.
     *
     * @param status the status of the response
     * @param dstAddr the {@code DSTIP} field of the response
     * @param dstPort the {@code DSTPORT} field of the response
     */
    public DefaultSocks4CommandResponse(Socks4CommandStatus status, string dstAddr, int dstPort)
    {
        if (dstAddr != null)
        {
            if (!NetUtil.IsValidIpV4Address(dstAddr))
            {
                throw new ArgumentException(
                        "dstAddr: " + dstAddr + " (expected: a valid IPv4 address)");
            }
        }

        if (dstPort < 0 || dstPort > 65535)
        {
            throw new ArgumentException("dstPort: " + dstPort.ToString(CultureInfo.InvariantCulture) + " (expected: 0~65535)");
        }

        this.status = ObjectUtil.CheckNotNull(status, "cmdStatus");
        this.dstAddr = dstAddr;
        this.dstPort = dstPort;
    }

    public virtual Socks4CommandStatus Status()
    {
        return status;
    }

    public virtual string DstAddr()
    {
        return dstAddr;
    }

    public virtual int DstPort()
    {
        return dstPort;
    }

    public override string ToString()
    {
        StringBuilder buf = new StringBuilder(96);
        buf.Append(StringUtil.SimpleClassName(this));

        DecoderResult decoderResult = DecoderResult();
        if (!decoderResult.IsSuccess())
        {
            buf.Append("(decoderResult: ");
            buf.Append(decoderResult);
            buf.Append(", dstAddr: ");
        }
        else
        {
            buf.Append("(dstAddr: ");
        }

        // CLR: StringBuilder.append(String) appends "the four characters "null"" for null
        // (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/lang/StringBuilder.html), while
        // StringBuilder.Append(string) makes no change ("If value is null, no changes are made",
        // https://learn.microsoft.com/en-us/dotnet/api/system.text.stringbuilder.append?view=net-10.0).
        buf.Append(DstAddr() ?? "null");
        buf.Append(", dstPort: ");
        buf.Append(DstPort());
        buf.Append(')');

        return buf.ToString();
    }
}
