/*
 * Copyright 2013 The Netty Project
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
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Socksx.V5;

/**
 * The status of {@link Socks5CommandResponse}.
 */
// CLR: Comparable<T> is IComparable<T>, and GetHashCode/CompareTo use (sbyte) byteValue to return the signed Java
// results over the unsigned System.Byte (see Netty.NET.Handler.Codec.Socksx.V4.Socks4CommandStatus for the evidence);
// ToString appends byteValue & 0xFF with the invariant culture.
public class Socks5CommandStatus : IComparable<Socks5CommandStatus>
{

    public static readonly Socks5CommandStatus SUCCESS = new Socks5CommandStatus(0x00, "SUCCESS");
    public static readonly Socks5CommandStatus FAILURE = new Socks5CommandStatus(0x01, "FAILURE");
    public static readonly Socks5CommandStatus FORBIDDEN = new Socks5CommandStatus(0x02, "FORBIDDEN");
    public static readonly Socks5CommandStatus NETWORK_UNREACHABLE = new Socks5CommandStatus(0x03, "NETWORK_UNREACHABLE");
    public static readonly Socks5CommandStatus HOST_UNREACHABLE = new Socks5CommandStatus(0x04, "HOST_UNREACHABLE");
    public static readonly Socks5CommandStatus CONNECTION_REFUSED = new Socks5CommandStatus(0x05, "CONNECTION_REFUSED");
    public static readonly Socks5CommandStatus TTL_EXPIRED = new Socks5CommandStatus(0x06, "TTL_EXPIRED");
    public static readonly Socks5CommandStatus COMMAND_UNSUPPORTED = new Socks5CommandStatus(0x07, "COMMAND_UNSUPPORTED");
    public static readonly Socks5CommandStatus ADDRESS_UNSUPPORTED = new Socks5CommandStatus(0x08, "ADDRESS_UNSUPPORTED");

    public static Socks5CommandStatus ValueOf(byte b)
    {
        switch (b)
        {
        case 0x00:
            return SUCCESS;
        case 0x01:
            return FAILURE;
        case 0x02:
            return FORBIDDEN;
        case 0x03:
            return NETWORK_UNREACHABLE;
        case 0x04:
            return HOST_UNREACHABLE;
        case 0x05:
            return CONNECTION_REFUSED;
        case 0x06:
            return TTL_EXPIRED;
        case 0x07:
            return COMMAND_UNSUPPORTED;
        case 0x08:
            return ADDRESS_UNSUPPORTED;
        }

        return new Socks5CommandStatus(b);
    }

    private readonly byte byteValue;
    private readonly string name;
    private string text;

    public Socks5CommandStatus(int byteValue) : this(byteValue, "UNKNOWN")
    {
    }

    public Socks5CommandStatus(int byteValue, string name)
    {
        this.name = ObjectUtil.CheckNotNull(name, "name");
        this.byteValue = (byte) byteValue;
    }

    public byte ByteValue()
    {
        return byteValue;
    }

    public bool IsSuccess()
    {
        return byteValue == 0;
    }

    public override int GetHashCode()
    {
        return (sbyte) byteValue;
    }

    public override bool Equals(object obj)
    {
        if (!(obj is Socks5CommandStatus))
        {
            return false;
        }

        return byteValue == ((Socks5CommandStatus) obj).byteValue;
    }

    public int CompareTo(Socks5CommandStatus o)
    {
        return (sbyte) byteValue - (sbyte) o.byteValue;
    }

    public override string ToString()
    {
        string text = this.text;
        if (text == null)
        {
            this.text = text = name + '(' + (byteValue & 0xFF).ToString(CultureInfo.InvariantCulture) + ')';
        }
        return text;
    }
}
