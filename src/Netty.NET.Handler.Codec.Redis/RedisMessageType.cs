/*
 * Copyright 2016 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Redis;

/**
 * Type of <a href="https://redis.io/topics/protocol">RESP (REdis Serialization Protocol)</a>.
 */
// CLR: a C# enum cannot carry the constructor, fields and methods ("You can't define a method inside the definition of
// an enumeration type", https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/enum). A Java
// enum constant is created with the constructor arguments that follow it and the class has no other instances
// (JLS 8.9: https://docs.oracle.com/javase/specs/jls/se17/html/jls-8.html#jls-8.9), so the constants are static readonly
// instances of a sealed class with a private constructor, and ToString() returns the constant name as Enum.toString()
// does. The boxed Byte value is byte? (null for INLINE_COMMAND) and byteValue() is its Value; the prefixes are ASCII,
// so their System.Byte values equal the Java bytes. The implicit values()/valueOf(String) members are not added.
[UnstableApi]
public sealed class RedisMessageType
{

    public static readonly RedisMessageType INLINE_COMMAND = new RedisMessageType(nameof(INLINE_COMMAND), null, true);
    public static readonly RedisMessageType SIMPLE_STRING = new RedisMessageType(nameof(SIMPLE_STRING), (byte) '+', true);
    public static readonly RedisMessageType ERROR = new RedisMessageType(nameof(ERROR), (byte) '-', true);
    public static readonly RedisMessageType INTEGER = new RedisMessageType(nameof(INTEGER), (byte) ':', true);
    public static readonly RedisMessageType BULK_STRING = new RedisMessageType(nameof(BULK_STRING), (byte) '$', false);
    public static readonly RedisMessageType ARRAY_HEADER = new RedisMessageType(nameof(ARRAY_HEADER), (byte) '*', false);

    private readonly string name;
    private readonly byte? value;
    private readonly bool inline;

    private RedisMessageType(string name, byte? value, bool inline)
    {
        this.name = name;
        this.value = value;
        this.inline = inline;
    }

    /**
     * Returns length of this type.
     */
    public int Length()
    {
        return value != null ? RedisConstants.TYPE_LENGTH : 0;
    }

    /**
     * Returns {@code true} if this type is inline type, or returns {@code false}. If this is {@code true},
     * this type doesn't have length field.
     */
    public bool IsInline()
    {
        return inline;
    }

    /**
     * Determine {@link RedisMessageType} based on the type prefix {@code byte} read from given the buffer.
     */
    public static RedisMessageType ReadFrom(ByteBuf @in, bool decodeInlineCommands)
    {
        int initialIndex = @in.ReaderIndex;
        RedisMessageType type = ValueOf(@in.ReadByte());
        if (type == INLINE_COMMAND)
        {
            if (!decodeInlineCommands)
            {
                throw new RedisCodecException("Decoding of inline commands is disabled");
            }

            // reset index to make content readable again
            @in.ReaderIndex = initialIndex;
        }

        return type;
    }

    /**
     * Write the message type's prefix to the given buffer.
     */
    public void WriteTo(ByteBuf @out)
    {
        if (value == null)
        {
            return;
        }

        @out.WriteByte(value.Value);
    }

    private static RedisMessageType ValueOf(byte value)
    {
        switch (value)
        {
        case (byte) '+':
            return SIMPLE_STRING;
        case (byte) '-':
            return ERROR;
        case (byte) ':':
            return INTEGER;
        case (byte) '$':
            return BULK_STRING;
        case (byte) '*':
            return ARRAY_HEADER;
        default:
            return INLINE_COMMAND;
        }
    }

    public override string ToString()
    {
        return name;
    }
}
