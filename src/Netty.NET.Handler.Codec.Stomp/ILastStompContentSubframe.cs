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
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec;

namespace Netty.NET.Handler.Codec.Stomp;

/**
 * The last {@link StompContentSubframe} which signals the end of the content batch
 * <p/>
 * Note, even when no content is emitted by the protocol, an
 * empty {@link LastStompContentSubframe} is issued to make the upstream parsing
 * easier.
 */
// CLR: copy(), duplicate(), retainedDuplicate(), replace(ByteBuf), retain(), retain(int), touch() and
// touch(Object) are re-declared only to narrow the return type, so they are inherited from IByteBufHolder, not
// repeated (owner decision 2026-11-19; docs/transport-porting.md, Decisions).
// The interface constant
// EMPTY_LAST_CONTENT is a static readonly field of the interface and its anonymous class is the named nested class
// EmptyLastStompContentSubframe (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface),
// which implements the IByteBufHolder and IReferenceCounted members, which those shared interfaces re-declare with other
// return types, explicitly, as Transport DatagramPacket does
// (https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/interfaces/explicit-interface-implementation).
// UnsupportedOperationException is NotSupportedException (docs/codec-base-porting.md, Shared CLR adaptations), and
// DecoderResult.SUCCESS is reached through global:: because the simple name DecoderResult is the method inside the
// nested class (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/namespace-alias-qualifier).
public interface ILastStompContentSubframe : IStompContentSubframe
{

    public static readonly ILastStompContentSubframe EMPTY_LAST_CONTENT = new EmptyLastStompContentSubframe();

    private sealed class EmptyLastStompContentSubframe : ILastStompContentSubframe
    {
        public ByteBuf Content => Unpooled.EmptyBuffer;

        public ILastStompContentSubframe Copy()
        {
            return EMPTY_LAST_CONTENT;
        }

        public ILastStompContentSubframe Duplicate()
        {
            return this;
        }

        public ILastStompContentSubframe RetainedDuplicate()
        {
            return this;
        }

        public ILastStompContentSubframe Replace(ByteBuf content)
        {
            return new DefaultLastStompContentSubframe(content);
        }

        public ILastStompContentSubframe Retain()
        {
            return this;
        }

        public ILastStompContentSubframe Retain(int increment)
        {
            return this;
        }

        public ILastStompContentSubframe Touch()
        {
            return this;
        }

        public ILastStompContentSubframe Touch(object hint)
        {
            return this;
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

        public DecoderResult DecoderResult()
        {
            return global::Netty.NET.Handler.Codec.DecoderResult.SUCCESS;
        }

        public void SetDecoderResult(DecoderResult result)
        {
            throw new NotSupportedException("read only");
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
