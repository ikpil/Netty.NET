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

using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Redis;

/**
 * Constant values for Redis encoder/decoder.
 */
// CLR: the package-private final class is an internal sealed class with the private constructor; the compile-time
// constants are const and the values computed at class initialization are static readonly.
internal sealed class RedisConstants
{

    private RedisConstants()
    {
    }

    internal const int TYPE_LENGTH = 1;

    internal const int EOL_LENGTH = 2;

    internal const int NULL_LENGTH = 2;

    internal const int NULL_VALUE = -1;

    internal const int REDIS_MESSAGE_MAX_LENGTH = 512 * 1024 * 1024; // 512MB

    // 64KB is max inline length of current Redis server implementation.
    internal const int REDIS_INLINE_MESSAGE_MAX_LENGTH = 64 * 1024;

    internal const int POSITIVE_LONG_MAX_LENGTH = 19; // length of Long.MAX_VALUE

    internal const int LONG_MAX_LENGTH = POSITIVE_LONG_MAX_LENGTH + 1; // +1 is sign

    internal static readonly short NULL_SHORT = RedisCodecUtil.MakeShort('-', '1');

    internal static readonly short EOL_SHORT = RedisCodecUtil.MakeShort('\r', '\n');

    internal const string PROP_REDIS_MAX_ARRAY_LENGTH = "io.netty.handler.codec.redis.maxArrayLength";
    internal static readonly int REDIS_MAX_ARRAY_LENGTH = SystemPropertyUtil.GetInt(PROP_REDIS_MAX_ARRAY_LENGTH, 1_000_000);
}
