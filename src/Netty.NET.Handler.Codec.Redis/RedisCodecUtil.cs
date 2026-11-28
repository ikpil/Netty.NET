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

using System.Globalization;
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Redis;

/**
 * Utilities for codec-redis.
 */
// CLR: the package-private final class is an internal sealed class with the private constructor;
// Long.toString(long) is the invariant-culture decimal text, because Int64.ToString() formats with the current
// culture (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring), and CharsetUtil.US_ASCII is
// Encoding.ASCII.
internal sealed class RedisCodecUtil
{

    private RedisCodecUtil()
    {
    }

    internal static byte[] LongToAsciiBytes(long value)
    {
        return Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture));
    }

    /**
     * Returns a {@code short} value using endian order.
     */
    internal static short MakeShort(char first, char second)
    {
        return PlatformDependent.BIG_ENDIAN_NATIVE_ORDER ?
                (short) ((second << 8) | first) : (short) ((first << 8) | second);
    }

    /**
     * Returns a {@code byte[]} of {@code short} value. This is opposite of {@code makeShort()}.
     */
    internal static byte[] ShortToBytes(short value)
    {
        byte[] bytes = new byte[2];

        if (PlatformDependent.BIG_ENDIAN_NATIVE_ORDER)
        {
            bytes[1] = (byte) ((value >> 8) & 0xff);
            bytes[0] = (byte) (value & 0xff);
        }
        else
        {
            bytes[0] = (byte) ((value >> 8) & 0xff);
            bytes[1] = (byte) (value & 0xff);
        }

        return bytes;
    }
}
