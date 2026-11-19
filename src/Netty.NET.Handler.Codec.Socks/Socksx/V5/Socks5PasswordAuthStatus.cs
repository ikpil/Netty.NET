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
 * The status of {@link Socks5PasswordAuthResponse}.
 */
// CLR: Comparable<T> is IComparable<T>, and GetHashCode/CompareTo use (sbyte) byteValue to return the signed Java
// results over the unsigned System.Byte (see Netty.NET.Handler.Codec.Socksx.V4.Socks4CommandStatus for the evidence):
// FAILURE (0xFF) hashes as -1 and orders before SUCCESS, as in the source. The byte switch label (byte) 0xFF is the
// unsigned constant 0xFF; ToString appends byteValue & 0xFF with the invariant culture.
public class Socks5PasswordAuthStatus : IComparable<Socks5PasswordAuthStatus>
{

    public static readonly Socks5PasswordAuthStatus SUCCESS = new Socks5PasswordAuthStatus(0x00, "SUCCESS");
    public static readonly Socks5PasswordAuthStatus FAILURE = new Socks5PasswordAuthStatus(0xFF, "FAILURE");

    public static Socks5PasswordAuthStatus ValueOf(byte b)
    {
        switch (b)
        {
        case 0x00:
            return SUCCESS;
        case 0xFF:
            return FAILURE;
        }

        return new Socks5PasswordAuthStatus(b);
    }

    private readonly byte byteValue;
    private readonly string name;
    private string text;

    public Socks5PasswordAuthStatus(int byteValue) : this(byteValue, "UNKNOWN")
    {
    }

    public Socks5PasswordAuthStatus(int byteValue, string name)
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
        if (!(obj is Socks5PasswordAuthStatus))
        {
            return false;
        }

        return byteValue == ((Socks5PasswordAuthStatus) obj).byteValue;
    }

    public int CompareTo(Socks5PasswordAuthStatus o)
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
