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
// @deprecated text names valueOf(byte) as the replacement, is not ported (owner decision 2026-11-16). 0xff is the
// unsigned System.Byte 255 (owner decision, docs/common-clr-design.md); valueOf(byte) only compares for equality.
public sealed class SocksAuthStatus
{
    public static readonly SocksAuthStatus SUCCESS = new SocksAuthStatus(nameof(SUCCESS), (byte) 0x00);
    public static readonly SocksAuthStatus FAILURE = new SocksAuthStatus(nameof(FAILURE), (byte) 0xff);

    // CLR: the constants in declaration order, returned as a new array by Values() as values() does.
    private static readonly SocksAuthStatus[] VALUES = { SUCCESS, FAILURE };

    private readonly byte b;
    // CLR: the constant name that java.lang.Enum keeps for name() and toString().
    private readonly string name;

    private SocksAuthStatus(string name, byte b)
    {
        this.name = name;
        this.b = b;
    }

    public static SocksAuthStatus[] Values()
    {
        return (SocksAuthStatus[]) VALUES.Clone();
    }

    public static SocksAuthStatus ValueOf(byte b)
    {
        foreach (SocksAuthStatus code in Values())
        {
            if (code.b == b)
            {
                return code;
            }
        }
        return FAILURE;
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
