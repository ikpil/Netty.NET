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

namespace Netty.NET.Handler.Codec.HAProxy;

/**
 * The command of an HAProxy proxy protocol header
 */
// CLR: a Java enum with a field, a constructor and methods is a sealed class whose constants are static readonly
// instances, as Base64Dialect in codec-base: a C# enum is a set of named integral constants and "You can't define a
// method inside the definition of an enumeration type" (https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/enum),
// while a Java enum class "has no instances other than those defined by its enum constants"
// (https://docs.oracle.com/javase/specs/jls/se21/html/jls-8.html#jls-8.9). Name(), ToString() and Values() stand for
// the java.lang.Enum members (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/lang/Enum.html) and
// equality stays reference based. byteValue stays a byte (owner decision for Java byte protocol values); the masked
// command is 0..15, so no sign-dependent text remains. Numbers in messages use the invariant culture
// (https://learn.microsoft.com/dotnet/api/system.int32.tostring).
public sealed class HAProxyCommand
{
    /**
     * The LOCAL command represents a connection that was established on purpose by the proxy
     * without being relayed.
     */
    public static readonly HAProxyCommand LOCAL = new HAProxyCommand(nameof(LOCAL), HAProxyConstants.COMMAND_LOCAL_BYTE);
    /**
     * The PROXY command represents a connection that was established on behalf of another node,
     * and reflects the original connection endpoints.
     */
    public static readonly HAProxyCommand PROXY = new HAProxyCommand(nameof(PROXY), HAProxyConstants.COMMAND_PROXY_BYTE);

    /**
     * The command is specified in the lowest 4 bits of the protocol version and command byte
     */
    private const byte COMMAND_MASK = 0x0f;

    private readonly byte byteValue;
    private readonly string name;

    /**
     * Creates a new instance
     */
    private HAProxyCommand(string name, byte byteValue)
    {
        this.name = name;
        this.byteValue = byteValue;
    }

    /**
     * Returns the {@link HAProxyCommand} represented by the lowest 4 bits of the specified byte.
     *
     * @param verCmdByte protocol version and command byte
     */
    public static HAProxyCommand ValueOf(byte verCmdByte)
    {
        int cmd = verCmdByte & COMMAND_MASK;
        switch ((byte) cmd)
        {
            case HAProxyConstants.COMMAND_PROXY_BYTE:
                return PROXY;
            case HAProxyConstants.COMMAND_LOCAL_BYTE:
                return LOCAL;
            default:
                throw new ArgumentException("unknown command: " + cmd.ToString(CultureInfo.InvariantCulture));
        }
    }

    /**
     * Returns the byte value of this command.
     */
    public byte ByteValue()
    {
        return byteValue;
    }

    public string Name()
    {
        return name;
    }

    public static HAProxyCommand[] Values()
    {
        return new[] { LOCAL, PROXY };
    }

    public override string ToString()
    {
        return name;
    }
}
