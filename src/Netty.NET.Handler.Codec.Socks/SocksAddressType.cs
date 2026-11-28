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

// CLR: a Java enum constant is an instance of its enum class, created with the arguments that follow it ("An enum
// constant may be followed by arguments, which are passed to the constructor"), and "An enum class has no
// instances other than those defined by its enum constants" (JLS 8.9, 8.9.1:
// https://docs.oracle.com/javase/specs/jls/se21/html/jls-8.html#jls-8.9). A C# enum is a value type of named
// integral constants and "You can't define a method inside the definition of an enumeration type"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/enum), so the constants are
// the static readonly instances of a sealed class with a private constructor, as Base64Dialect in codec-base.
// Values() is the implicit values(), "which returns an array containing the enum constants of E, in the same order
// as they appear" (JLS 8.9.3), used by valueOf(byte) and the tests; ToString() returns the constant name, as
// Enum.toString() does (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/lang/Enum.html). Code
// that switches over the constants uses an if/else chain, because a case label needs a constant pattern
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/selection-statements). The
// @Deprecated fromByte(byte), whose @deprecated text names valueOf(byte) as the replacement, is not ported. The
// byte values are unsigned System.Byte (owner decision, docs/common-clr-design.md): 0xff is 255 where the Java
// byte is -1; valueOf(byte) only compares for equality, which does not depend on the sign.
public sealed class SocksAddressType
{
    public static readonly SocksAddressType IPv4 = new SocksAddressType(nameof(IPv4), (byte) 0x01);
    public static readonly SocksAddressType DOMAIN = new SocksAddressType(nameof(DOMAIN), (byte) 0x03);
    public static readonly SocksAddressType IPv6 = new SocksAddressType(nameof(IPv6), (byte) 0x04);
    public static readonly SocksAddressType UNKNOWN = new SocksAddressType(nameof(UNKNOWN), (byte) 0xff);

    // CLR: the constants in declaration order, returned as a new array by Values() as values() does.
    private static readonly SocksAddressType[] VALUES = { IPv4, DOMAIN, IPv6, UNKNOWN };

    private readonly byte b;
    // CLR: the constant name that java.lang.Enum keeps for name() and toString().
    private readonly string name;

    private SocksAddressType(string name, byte b)
    {
        this.name = name;
        this.b = b;
    }

    public static SocksAddressType[] Values()
    {
        return (SocksAddressType[]) VALUES.Clone();
    }

    public static SocksAddressType ValueOf(byte b)
    {
        foreach (SocksAddressType code in Values())
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
