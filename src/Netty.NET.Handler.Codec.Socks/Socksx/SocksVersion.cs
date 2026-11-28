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

namespace Netty.NET.Handler.Codec.Socksx;

/**
 * The version of SOCKS protocol.
 */
// CLR: Java enum with a field and methods as a sealed class with static readonly constants and the constant-name
// ToString(), with the evidence given in Netty.NET.Handler.Codec.Socks.SocksAddressType (SocksAddressType.cs); the
// implicit values()/valueOf(String) are not used and not added. A switch over the constants is an if/else chain.
// 0xff is the unsigned System.Byte 255 (owner decision, docs/common-clr-design.md); valueOf(byte) only compares for
// equality.
public sealed class SocksVersion
{
    /**
     * SOCKS protocol version 4a (or 4)
     */
    public static readonly SocksVersion SOCKS4a = new SocksVersion(nameof(SOCKS4a), (byte) 0x04);
    /**
     * SOCKS protocol version 5
     */
    public static readonly SocksVersion SOCKS5 = new SocksVersion(nameof(SOCKS5), (byte) 0x05);
    /**
     * Unknown protocol version
     */
    public static readonly SocksVersion UNKNOWN = new SocksVersion(nameof(UNKNOWN), (byte) 0xff);

    /**
     * Returns the {@link SocksVersion} that corresponds to the specified version field value,
     * as defined in the protocol specification.
     *
     * @return {@link #UNKNOWN} if the specified value does not represent a known SOCKS protocol version
     */
    public static SocksVersion ValueOf(byte b)
    {
        if (b == SOCKS4a.ByteValue())
        {
            return SOCKS4a;
        }
        if (b == SOCKS5.ByteValue())
        {
            return SOCKS5;
        }

        return UNKNOWN;
    }

    private readonly byte b;
    // CLR: the constant name that java.lang.Enum keeps for name() and toString().
    private readonly string name;

    private SocksVersion(string name, byte b)
    {
        this.name = name;
        this.b = b;
    }

    /**
     * Returns the value of the version field, as defined in the protocol specification.
     */
    public byte ByteValue()
    {
        return b;
    }

    public override string ToString()
    {
        return name;
    }
}
