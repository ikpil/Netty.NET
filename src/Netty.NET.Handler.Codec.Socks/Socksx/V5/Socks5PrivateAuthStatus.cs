/*
 * Copyright 2025 The Netty Project
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
 * The status of a SOCKS5 private authentication response.
 * <p>
 * RFC 1928 reserves method codes 0x80-0xFE for private authentication methods but does not
 * specify the format of their subnegotiation. This class provides standard status codes
 * for the private authentication response that follow the pattern established by the
 * username/password authentication method in RFC 1929.
 * </p>
 *
 * @see <a href="https://www.ietf.org/rfc/rfc1928.txt">RFC 1928 Section 3</a>
 * @see <a href="https://www.ietf.org/rfc/rfc1929.txt">RFC 1929</a>
 */
// CLR: Comparable<T> is IComparable<T>, and GetHashCode/CompareTo use (sbyte) byteValue to return the signed Java
// results over the unsigned System.Byte (see Netty.NET.Handler.Codec.Socksx.V4.Socks4CommandStatus for the evidence):
// FAILURE (0xFF) hashes as -1 and orders before SUCCESS, as in the source. The byte switch label (byte) 0xFF is the
// unsigned constant 0xFF; ToString appends byteValue & 0xFF with the invariant culture.
public sealed class Socks5PrivateAuthStatus : IComparable<Socks5PrivateAuthStatus>
{

    public static readonly Socks5PrivateAuthStatus SUCCESS = new Socks5PrivateAuthStatus(0x00, "SUCCESS");
    public static readonly Socks5PrivateAuthStatus FAILURE = new Socks5PrivateAuthStatus(0xFF, "FAILURE");

    /**
     * Returns the {@link Socks5PrivateAuthStatus} instance that corresponds to the specified byte value.
     * <p>
     * This method returns a singleton instance for standard status codes:
     * <ul>
     *   <li>0x00: {@link #SUCCESS}</li>
     *   <li>0xFF: {@link #FAILURE}</li>
     * </ul>
     * For any other values, a new instance is created.
     *
     * @param b The byte value of the SOCKS5 private authentication status
     * @return The corresponding {@link Socks5PrivateAuthStatus} instance
     */
    public static Socks5PrivateAuthStatus ValueOf(byte b)
    {
        switch (b)
        {
            case 0x00:
                return SUCCESS;
            case 0xFF:
                return FAILURE;
        }

        return new Socks5PrivateAuthStatus(b);
    }

    private readonly byte byteValue;
    private readonly string name;
    private string text;

    private Socks5PrivateAuthStatus(int byteValue) : this(byteValue, "UNKNOWN")
    {
    }

    /**
     * Creates a new SOCKS5 private authentication status.
     *
     * @param byteValue The byte value representing the authentication status
     *                  (0x00 for success, 0xFF for failure, or custom values)
     * @param name      The descriptive name of this status, must not be null
     * @throws NullPointerException if the name is null
     */
    public Socks5PrivateAuthStatus(int byteValue, string name)
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
        if (!(obj is Socks5PrivateAuthStatus))
        {
            return false;
        }

        return byteValue == ((Socks5PrivateAuthStatus) obj).byteValue;
    }

    public int CompareTo(Socks5PrivateAuthStatus o)
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
