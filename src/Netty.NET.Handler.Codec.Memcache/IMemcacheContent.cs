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

using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Memcache;

/**
 * An Memcache content chunk.
 * <p/>
 * A implementation of a {@link AbstractMemcacheObjectDecoder} generates {@link MemcacheContent} after
 * {@link MemcacheMessage} when the content is large. If you prefer not to receive {@link MemcacheContent}
 * in your handler, place a aggregator after an implementation of the {@link AbstractMemcacheObjectDecoder}
 * in the {@link ChannelPipeline}.
 */
// CLR: ByteBufHolder is IByteBufHolder, whose content() is the read-only Content property.
// CLR: copy(), duplicate(), retainedDuplicate(), replace(ByteBuf), retain(), retain(int), touch() and
// touch(Object) are re-declared only to narrow the return type, so they are inherited from IByteBufHolder, not
// repeated.
[UnstableApi]
public interface IMemcacheContent : IMemcacheObject, IByteBufHolder
{
}
