/*
 * Copyright 2014 The Netty Project
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
using static Netty.NET.Handler.Codec.HAProxy.HAProxyConstants;

namespace Netty.NET.Handler.Codec.HAProxy;

/**
 * The HAProxy proxy protocol specification version.
 */
// CLR: an enum with a field and methods is a sealed class with static readonly constants; Name(), ToString() and
// Values() stand for the java.lang.Enum members (see HAProxyCommand). byteValue stays a byte (owner decision).
public sealed class HAProxyProtocolVersion
{
    /**
     * The ONE proxy protocol version represents a version 1 (human-readable) header.
     */
    public static readonly HAProxyProtocolVersion V1 = new HAProxyProtocolVersion(nameof(V1), VERSION_ONE_BYTE);
    /**
     * The TWO proxy protocol version represents a version 2 (binary) header.
     */
    public static readonly HAProxyProtocolVersion V2 = new HAProxyProtocolVersion(nameof(V2), VERSION_TWO_BYTE);

    /**
     * The highest 4 bits of the protocol version and command byte contain the version
     */
    private const byte VERSION_MASK = (byte) 0xf0;

    private readonly byte byteValue;
    private readonly string name;

    /**
     * Creates a new instance
     */
    private HAProxyProtocolVersion(string name, byte byteValue)
    {
        this.name = name;
        this.byteValue = byteValue;
    }

    /**
     * Returns the {@link HAProxyProtocolVersion} represented by the highest 4 bits of the specified byte.
     *
     * @param verCmdByte protocol version and command byte
     */
    // CLR: the Java byte and the (byte) 0xf0 mask are signed (-128..127,
    // https://docs.oracle.com/javase/specs/jls/se17/html/jls-4.html#jls-4.2.1), so verCmdByte & VERSION_MASK widens both
    // with sign and an unknown high version prints a negative number (0xE1 prints -32); the C# bytes are unsigned, so
    // both are widened through sbyte to keep that value and its text. The case labels compare the low 8 bits, as the
    // source's (byte) cast does.
    public static HAProxyProtocolVersion ValueOf(byte verCmdByte)
    {
        int version = unchecked((sbyte) verCmdByte & (sbyte) VERSION_MASK);

        switch ((byte) version)
        {
            case VERSION_TWO_BYTE:
                return V2;
            case VERSION_ONE_BYTE:
                return V1;
            default:
                throw new ArgumentException("unknown version: " + version.ToString(CultureInfo.InvariantCulture));
        }
    }

    /**
     * Returns the byte value of this version.
     */
    public byte ByteValue()
    {
        return byteValue;
    }

    public string Name()
    {
        return name;
    }

    public static HAProxyProtocolVersion[] Values()
    {
        return new[] { V1, V2 };
    }

    public override string ToString()
    {
        return name;
    }
}
