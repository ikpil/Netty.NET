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
 * Header of Redis Array Message.
 */
// CLR: the numbers in the exception message and in toString() use the invariant culture, because Java's decimal text
// does not depend on a culture while Int32/Int64.ToString() and StringBuilder.Append(Int64) format with the current
// culture (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring,
// https://learn.microsoft.com/en-us/dotnet/api/system.text.stringbuilder.append).
[UnstableApi]
public class ArrayHeaderRedisMessage : IRedisMessage
{

    private readonly long length;

    /**
     * Creates a {@link ArrayHeaderRedisMessage} for the given {@code length}.
     */
    public ArrayHeaderRedisMessage(long length)
    {
        if (length < RedisConstants.NULL_VALUE)
        {
            throw new RedisCodecException("length: " + length.ToString(CultureInfo.InvariantCulture) +
                    " (expected: >= " + RedisConstants.NULL_VALUE.ToString(CultureInfo.InvariantCulture) + ")");
        }
        this.length = length;
    }

    /**
     * Get length of this array object.
     */
    public long Length()
    {
        return length;
    }

    /**
     * Returns whether the content of this message is {@code null}.
     *
     * @return indicates whether the content of this message is {@code null}.
     */
    public bool IsNull()
    {
        return length == RedisConstants.NULL_VALUE;
    }

    public override string ToString()
    {
        return new StringBuilder(StringUtil.SimpleClassName(this))
                .Append('[')
                .Append("length=")
                .Append(length.ToString(CultureInfo.InvariantCulture))
                .Append(']').ToString();
    }
}
