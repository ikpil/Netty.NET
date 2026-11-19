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
 * A strategy interface for caching {@link RedisMessage}s.
 */
[UnstableApi]
public interface IRedisMessagePool
{

    /**
     * Returns {@link SimpleStringRedisMessage} for given {@code content}. Returns {@code null} it does not exist.
     */
    SimpleStringRedisMessage GetSimpleString(string content);

    /**
     * Returns {@link SimpleStringRedisMessage} for given {@code content}. Returns {@code null} it does not exist.
     */
    SimpleStringRedisMessage GetSimpleString(ByteBuf content);

    /**
     * Returns {@link ErrorRedisMessage} for given {@code content}. Returns {@code null} it does not exist.
     */
    ErrorRedisMessage GetError(string content);

    /**
     * Returns {@link ErrorRedisMessage} for given {@code content}. Returns {@code null} it does not exist.
     */
    ErrorRedisMessage GetError(ByteBuf content);

    /**
     * Returns {@link IntegerRedisMessage} for given {@code value}. Returns {@code null} it does not exist.
     */
    IntegerRedisMessage GetInteger(long value);

    /**
     * Returns {@link IntegerRedisMessage} for given {@code content}. Returns {@code null} it does not exist.
     */
    IntegerRedisMessage GetInteger(ByteBuf content);

    /**
     * Returns {@code byte[]} for given {@code msg}. Returns {@code null} it does not exist.
     */
    byte[] GetByteBufOfInteger(long value);
}
