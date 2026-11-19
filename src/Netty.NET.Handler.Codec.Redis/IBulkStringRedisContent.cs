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
 * A chunk of bulk strings which is used for Redis chunked transfer-encoding.
 * {@link RedisDecoder} generates {@link BulkStringRedisContent} after
 * {@link BulkStringHeaderRedisMessage} when the content is large or the encoding of the content is chunked.
 * If you prefer not to receive {@link BulkStringRedisContent} in your handler,
 * place {@link RedisBulkStringAggregator} after {@link RedisDecoder} in the {@link ChannelPipeline}.
 */
// CLR: ByteBufHolder is IByteBufHolder, whose content() is the read-only Content property (docs/buffer-porting.md,
// Buffer holders and conversion).
// CLR: copy(), duplicate(), retainedDuplicate(), replace(ByteBuf), retain(), retain(int), touch() and
// touch(Object) are re-declared only to narrow the return type, so they are inherited from IByteBufHolder, not
// repeated (owner decision 2026-11-19; docs/transport-porting.md, Decisions).
[UnstableApi]
public interface IBulkStringRedisContent : IRedisMessage, IByteBufHolder
{
}
