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
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Socksx.V4;

/**
 * The type of {@link Socks4CommandRequest}.
 */
// CLR: Comparable<T> is IComparable<T>, and GetHashCode/CompareTo use (sbyte) byteValue to return the signed Java
// results over the unsigned System.Byte (see Socks4CommandStatus for the evidence); ToString appends byteValue & 0xFF
// with the invariant culture.
public class Socks4CommandType : IComparable<Socks4CommandType>
{

    public static readonly Socks4CommandType CONNECT = new Socks4CommandType(0x01, "CONNECT");
    public static readonly Socks4CommandType BIND = new Socks4CommandType(0x02, "BIND");

    public static Socks4CommandType ValueOf(byte b)
    {
        switch (b)
        {
        case 0x01:
            return CONNECT;
        case 0x02:
            return BIND;
        }

        return new Socks4CommandType(b);
    }

    private readonly byte byteValue;
    private readonly string name;
    private string text;

    public Socks4CommandType(int byteValue) : this(byteValue, "UNKNOWN")
    {
    }

    public Socks4CommandType(int byteValue, string name)
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
        if (!(obj is Socks4CommandType))
        {
            return false;
        }

        return byteValue == ((Socks4CommandType) obj).byteValue;
    }

    public int CompareTo(Socks4CommandType o)
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
