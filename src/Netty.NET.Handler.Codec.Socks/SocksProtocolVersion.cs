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

namespace Netty.NET.Handler.Codec.Socks;

// CLR: Java enum with a field and methods as a sealed class with static readonly constants, Values() and the
// constant-name ToString(), with the evidence given in SocksAddressType.cs. The @Deprecated fromByte(byte), whose
// @deprecated text names valueOf(byte) as the replacement, is not ported. 0xff is the unsigned System.Byte 255
// (owner decision, docs/common-clr-design.md); valueOf(byte) only compares for equality.
public sealed class SocksProtocolVersion
{
    public static readonly SocksProtocolVersion SOCKS4a = new SocksProtocolVersion(nameof(SOCKS4a), (byte) 0x04);
    public static readonly SocksProtocolVersion SOCKS5 = new SocksProtocolVersion(nameof(SOCKS5), (byte) 0x05);
    public static readonly SocksProtocolVersion UNKNOWN = new SocksProtocolVersion(nameof(UNKNOWN), (byte) 0xff);

    // CLR: the constants in declaration order, returned as a new array by Values() as values() does.
    private static readonly SocksProtocolVersion[] VALUES = { SOCKS4a, SOCKS5, UNKNOWN };

    private readonly byte b;
    // CLR: the constant name that java.lang.Enum keeps for name() and toString().
    private readonly string name;

    private SocksProtocolVersion(string name, byte b)
    {
        this.name = name;
        this.b = b;
    }

    public static SocksProtocolVersion[] Values()
    {
        return (SocksProtocolVersion[]) VALUES.Clone();
    }

    public static SocksProtocolVersion ValueOf(byte b)
    {
        foreach (SocksProtocolVersion code in Values())
        {
            if (code.b == b)
            {
                return code;
            }
        }

        return UNKNOWN;
    }

    public byte ByteValue()
    {
        return b;
    }

    public override string ToString()
    {
        return name;
    }
}
