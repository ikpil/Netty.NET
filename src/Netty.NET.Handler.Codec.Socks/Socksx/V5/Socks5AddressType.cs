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
 * The type of address in {@link Socks5CommandRequest} and {@link Socks5CommandResponse}.
 */
// CLR: Comparable<T> is IComparable<T>, and GetHashCode/CompareTo use (sbyte) byteValue to return the signed Java
// results over the unsigned System.Byte (see Netty.NET.Handler.Codec.Socksx.V4.Socks4CommandStatus for the evidence);
// ToString appends byteValue & 0xFF with the invariant culture.
public class Socks5AddressType : IComparable<Socks5AddressType>
{

    public static readonly Socks5AddressType IPv4 = new Socks5AddressType(0x01, "IPv4");
    public static readonly Socks5AddressType DOMAIN = new Socks5AddressType(0x03, "DOMAIN");
    public static readonly Socks5AddressType IPv6 = new Socks5AddressType(0x04, "IPv6");

    public static Socks5AddressType ValueOf(byte b)
    {
        switch (b)
        {
        case 0x01:
            return IPv4;
        case 0x03:
            return DOMAIN;
        case 0x04:
            return IPv6;
        }

        return new Socks5AddressType(b);
    }

    private readonly byte byteValue;
    private readonly string name;
    private string text;

    public Socks5AddressType(int byteValue) : this(byteValue, "UNKNOWN")
    {
    }

    public Socks5AddressType(int byteValue, string name)
    {
        this.name = ObjectUtil.CheckNotNull(name, "name");
        this.byteValue = (byte) byteValue;
    }

    public byte ByteValue()
    {
        return byteValue;
    }

    public override int GetHashCode()
    {
        return (sbyte) byteValue;
    }

    public override bool Equals(object obj)
    {
        if (!(obj is Socks5AddressType))
        {
            return false;
        }

        return byteValue == ((Socks5AddressType) obj).byteValue;
    }

    public int CompareTo(Socks5AddressType o)
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
