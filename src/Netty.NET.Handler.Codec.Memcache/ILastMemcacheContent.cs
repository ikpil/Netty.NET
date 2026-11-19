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

using System;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;

namespace Netty.NET.Handler.Codec.Memcache;

/**
 * The {@link MemcacheContent} which signals the end of the content batch.
 * <p/>
 * Note that by design, even when no content is emitted by the protocol, an
 * empty {@link LastMemcacheContent} is issued to make the upstream parsing
 * easier.
 */
// CLR: copy(), duplicate(), retainedDuplicate(), replace(ByteBuf), retain(), retain(int), touch() and
// touch(Object) are re-declared only to narrow the return type, so they are inherited from IByteBufHolder, not
// repeated (owner decision 2026-11-19; docs/transport-porting.md, Decisions).
[UnstableApi]
public interface ILastMemcacheContent : IMemcacheContent
{

    // CLR: the interface constant is a static readonly field of the interface and the anonymous class is the named
    // nested class EmptyLastMemcacheContent; C# interfaces may declare static fields and nested types
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface).
    public static readonly ILastMemcacheContent EMPTY_LAST_CONTENT = new EmptyLastMemcacheContent();

    // CLR: the IByteBufHolder and IReferenceCounted members, which those shared interfaces re-declare with other
    // return types, are explicit implementations that forward to the public members, as Transport DatagramPacket
    // does (https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/interfaces/explicit-interface-implementation).
    // UnsupportedOperationException is NotSupportedException (docs/codec-base-porting.md, Shared CLR adaptations).
    private sealed class EmptyLastMemcacheContent : ILastMemcacheContent
    {
        public ILastMemcacheContent Copy()
        {
            return EMPTY_LAST_CONTENT;
        }

        public ILastMemcacheContent Duplicate()
        {
            return this;
        }

        public ILastMemcacheContent RetainedDuplicate()
        {
            return this;
        }

        public ILastMemcacheContent Replace(ByteBuf content)
        {
            return new DefaultLastMemcacheContent(content);
        }

        public ILastMemcacheContent Retain(int increment)
        {
            return this;
        }

        public ILastMemcacheContent Retain()
        {
            return this;
        }

        public ILastMemcacheContent Touch()
        {
            return this;
        }

        public ILastMemcacheContent Touch(object hint)
        {
            return this;
        }

        public ByteBuf Content => Unpooled.EmptyBuffer;

        // CLR: inside this class the simple name DecoderResult is this method in an expression, so the DecoderResult
        // type is reached through global:: (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/namespace-alias-qualifier).
        public DecoderResult DecoderResult()
        {
            return global::Netty.NET.Handler.Codec.DecoderResult.SUCCESS;
        }

        public void SetDecoderResult(DecoderResult result)
        {
            throw new NotSupportedException("read only");
        }

        public int RefCnt => 1;

        public bool Release()
        {
            return false;
        }

        public bool Release(int decrement)
        {
            return false;
        }

        IByteBufHolder IByteBufHolder.Copy() => Copy();
        IByteBufHolder IByteBufHolder.Duplicate() => Duplicate();
        IByteBufHolder IByteBufHolder.RetainedDuplicate() => RetainedDuplicate();
        IByteBufHolder IByteBufHolder.Replace(ByteBuf content) => Replace(content);
        IByteBufHolder IByteBufHolder.Retain() => Retain();
        IByteBufHolder IByteBufHolder.Retain(int increment) => Retain(increment);
        IByteBufHolder IByteBufHolder.Touch() => Touch();
        IByteBufHolder IByteBufHolder.Touch(object hint) => Touch(hint);
        IReferenceCounted IReferenceCounted.Retain() => Retain();
        IReferenceCounted IReferenceCounted.Retain(int increment) => Retain(increment);
        IReferenceCounted IReferenceCounted.Touch() => Touch();
        IReferenceCounted IReferenceCounted.Touch(object hint) => Touch(hint);
    }
}
