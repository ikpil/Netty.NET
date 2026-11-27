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
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Redis;

/**
 * A last chunk of Bulk Strings.
 */
// CLR: copy(), duplicate(), retainedDuplicate(), replace(ByteBuf), retain(), retain(int), touch() and
// touch(Object) are re-declared only to narrow the return type, so they are inherited from IByteBufHolder, not
// repeated (owner decision 2026-11-19; docs/transport-porting.md, Decisions).
[UnstableApi]
public interface ILastBulkStringRedisContent : IBulkStringRedisContent
{

    /**
     * The 'end of content' marker in chunked encoding.
     */
    // CLR: the interface constant is a static readonly field of the interface and the anonymous class is the named
    // nested class EmptyLastBulkStringRedisContent; C# interfaces may declare static fields and nested types
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface). The IByteBufHolder and
    // IReferenceCounted members, which those shared interfaces re-declare with other return types, are explicit
    // implementations that forward to the public members, as Transport DatagramPacket does
    // (https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/interfaces/explicit-interface-implementation).
    public static readonly ILastBulkStringRedisContent EMPTY_LAST_CONTENT = new EmptyLastBulkStringRedisContent();

    private sealed class EmptyLastBulkStringRedisContent : ILastBulkStringRedisContent
    {

        public ByteBuf Content => Unpooled.EmptyBuffer;

        public ILastBulkStringRedisContent Copy()
        {
            return this;
        }

        public ILastBulkStringRedisContent Duplicate()
        {
            return this;
        }

        public ILastBulkStringRedisContent RetainedDuplicate()
        {
            return this;
        }

        public ILastBulkStringRedisContent Replace(ByteBuf content)
        {
            return new DefaultLastBulkStringRedisContent(content);
        }

        public ILastBulkStringRedisContent Retain(int increment)
        {
            return this;
        }

        public ILastBulkStringRedisContent Retain()
        {
            return this;
        }

        public int RefCnt => 1;

        public ILastBulkStringRedisContent Touch()
        {
            return this;
        }

        public ILastBulkStringRedisContent Touch(object hint)
        {
            return this;
        }

        public bool Release()
        {
            return false;
        }

        public bool Release(int decrement)
        {
            return false;
        }

        IByteBufHolder IByteBufHolder.Copy()
        {
            return Copy();
        }
        IByteBufHolder IByteBufHolder.Duplicate()
        {
            return Duplicate();
        }
        IByteBufHolder IByteBufHolder.RetainedDuplicate()
        {
            return RetainedDuplicate();
        }
        IByteBufHolder IByteBufHolder.Replace(ByteBuf content)
        {
            return Replace(content);
        }
        IByteBufHolder IByteBufHolder.Retain()
        {
            return Retain();
        }
        IByteBufHolder IByteBufHolder.Retain(int increment)
        {
            return Retain(increment);
        }
        IByteBufHolder IByteBufHolder.Touch()
        {
            return Touch();
        }
        IByteBufHolder IByteBufHolder.Touch(object hint)
        {
            return Touch(hint);
        }
        IReferenceCounted IReferenceCounted.Retain()
        {
            return Retain();
        }
        IReferenceCounted IReferenceCounted.Retain(int increment)
        {
            return Retain(increment);
        }
        IReferenceCounted IReferenceCounted.Touch()
        {
            return Touch();
        }
        IReferenceCounted IReferenceCounted.Touch(object hint)
        {
            return Touch(hint);
        }
    }
}
