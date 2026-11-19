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
 * The authentication method of SOCKS5.
 */
// CLR: Comparable<T> is IComparable<T>, and GetHashCode/CompareTo use (sbyte) byteValue to return the signed Java
// results over the unsigned System.Byte (see Netty.NET.Handler.Codec.Socksx.V4.Socks4CommandStatus for the evidence):
// UNACCEPTED (0xFF) and the private methods (0x80-0xFE) hash and compare as -1 and -128..-2, below NO_AUTH, as in the
// source. The byte switch label (byte) 0xFF is the unsigned constant 0xFF, and b & 0xFF is the same 0-255 value in
// both; numbers in names and ToString use the invariant culture.
public class Socks5AuthMethod : IComparable<Socks5AuthMethod>
{

    public static readonly Socks5AuthMethod NO_AUTH = new Socks5AuthMethod(0x00, "NO_AUTH");
    public static readonly Socks5AuthMethod GSSAPI = new Socks5AuthMethod(0x01, "GSSAPI");
    public static readonly Socks5AuthMethod PASSWORD = new Socks5AuthMethod(0x02, "PASSWORD");

    /**
     * Indicates that the server does not accept any authentication methods the client proposed.
     */
    public static readonly Socks5AuthMethod UNACCEPTED = new Socks5AuthMethod(0xff, "UNACCEPTED");

    /**
     * Returns whether the authentication method code is in the private methods range (0x80-0xFE)
     * as defined by <a href="https://www.ietf.org/rfc/rfc1928.txt">RFC 1928 section 3</a>.
     *
     * @param b The authentication method code
     * @return true if the code is in the private methods range
     */
    public static bool IsPrivateMethod(byte b)
    {
        int ubyte = b & 0xFF;
        return ubyte >= 0x80 && ubyte <= 0xFE;
    }

    public static Socks5AuthMethod ValueOf(byte b)
    {
        switch (b)
        {
        case 0x00:
            return NO_AUTH;
        case 0x01:
            return GSSAPI;
        case 0x02:
            return PASSWORD;
        case 0xFF:
            return UNACCEPTED;
        }

        // Handle all private methods (0x80-0xFE)
        if (IsPrivateMethod(b))
        {
            return new Socks5AuthMethod(b, "PRIVATE_" + (b & 0xFF).ToString(CultureInfo.InvariantCulture));
        }

        return new Socks5AuthMethod(b);
    }

    private readonly byte byteValue;
    private readonly string name;
    private string text;

    public Socks5AuthMethod(int byteValue) : this(byteValue, "UNKNOWN")
    {
    }

    public Socks5AuthMethod(int byteValue, string name)
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
        if (!(obj is Socks5AuthMethod))
        {
            return false;
        }

        return byteValue == ((Socks5AuthMethod) obj).byteValue;
    }

    public int CompareTo(Socks5AuthMethod o)
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
