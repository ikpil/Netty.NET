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
public sealed class SocksCmdStatus
{
    public static readonly SocksCmdStatus SUCCESS = new SocksCmdStatus(nameof(SUCCESS), (byte) 0x00);
    public static readonly SocksCmdStatus FAILURE = new SocksCmdStatus(nameof(FAILURE), (byte) 0x01);
    public static readonly SocksCmdStatus FORBIDDEN = new SocksCmdStatus(nameof(FORBIDDEN), (byte) 0x02);
    public static readonly SocksCmdStatus NETWORK_UNREACHABLE =
            new SocksCmdStatus(nameof(NETWORK_UNREACHABLE), (byte) 0x03);
    public static readonly SocksCmdStatus HOST_UNREACHABLE = new SocksCmdStatus(nameof(HOST_UNREACHABLE), (byte) 0x04);
    public static readonly SocksCmdStatus REFUSED = new SocksCmdStatus(nameof(REFUSED), (byte) 0x05);
    public static readonly SocksCmdStatus TTL_EXPIRED = new SocksCmdStatus(nameof(TTL_EXPIRED), (byte) 0x06);
    public static readonly SocksCmdStatus COMMAND_NOT_SUPPORTED =
            new SocksCmdStatus(nameof(COMMAND_NOT_SUPPORTED), (byte) 0x07);
    public static readonly SocksCmdStatus ADDRESS_NOT_SUPPORTED =
            new SocksCmdStatus(nameof(ADDRESS_NOT_SUPPORTED), (byte) 0x08);
    public static readonly SocksCmdStatus UNASSIGNED = new SocksCmdStatus(nameof(UNASSIGNED), (byte) 0xff);

    // CLR: the constants in declaration order, returned as a new array by Values() as values() does.
    private static readonly SocksCmdStatus[] VALUES = {
            SUCCESS, FAILURE, FORBIDDEN, NETWORK_UNREACHABLE, HOST_UNREACHABLE, REFUSED, TTL_EXPIRED,
            COMMAND_NOT_SUPPORTED, ADDRESS_NOT_SUPPORTED, UNASSIGNED };

    private readonly byte b;
    // CLR: the constant name that java.lang.Enum keeps for name() and toString().
    private readonly string name;

    private SocksCmdStatus(string name, byte b)
    {
        this.name = name;
        this.b = b;
    }

    public static SocksCmdStatus[] Values()
    {
        return (SocksCmdStatus[]) VALUES.Clone();
    }

    public static SocksCmdStatus ValueOf(byte b)
    {
        foreach (SocksCmdStatus code in Values())
        {
            if (code.b == b)
            {
                return code;
            }
        }

        return UNASSIGNED;
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
