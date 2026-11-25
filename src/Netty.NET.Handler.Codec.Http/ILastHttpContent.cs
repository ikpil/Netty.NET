/*
 * Copyright 2012 The Netty Project
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

namespace Netty.NET.Handler.Codec.Http;

/**
 * The last {@link HttpContent} which has trailing headers.
 */
// CLR: the Java interface takes the I prefix (owner rule). The narrowing re-declarations of copy(), duplicate(),
// retainedDuplicate(), replace(ByteBuf), retain(int), retain(), touch() and touch(Object) are inherited from
// IByteBufHolder (see IHttpContent). The interface constant EMPTY_LAST_CONTENT is a static readonly field of the
// interface and its anonymous class is the named nested class EmptyLastHttpContent
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface), which implements the
// IByteBufHolder and IReferenceCounted members, which those shared interfaces re-declare with other return types,
// explicitly, as codec-stomp's ILastStompContentSubframe does. Its @Deprecated getDecoderResult() override is not
// ported (see IHttpObject). UnsupportedOperationException is NotSupportedException, and DecoderResult.SUCCESS is reached
// through global:: because the simple name DecoderResult is the method inside the nested class
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/namespace-alias-qualifier).
public interface ILastHttpContent : IHttpContent
{

    /**
     * The 'end of content' marker in chunked encoding.
     */
    public static readonly ILastHttpContent EMPTY_LAST_CONTENT = new EmptyLastHttpContent();

    private sealed class EmptyLastHttpContent : ILastHttpContent
    {

        public ByteBuf Content => Unpooled.EmptyBuffer;

        public ILastHttpContent Copy()
        {
            return EMPTY_LAST_CONTENT;
        }

        public ILastHttpContent Duplicate()
        {
            return this;
        }

        public ILastHttpContent Replace(ByteBuf content)
        {
            return new DefaultLastHttpContent(content);
        }

        public ILastHttpContent RetainedDuplicate()
        {
            return this;
        }

        public HttpHeaders TrailingHeaders()
        {
            return EmptyHttpHeaders.INSTANCE;
        }

        public DecoderResult DecoderResult()
        {
            return global::Netty.NET.Handler.Codec.DecoderResult.SUCCESS;
        }

        public void SetDecoderResult(DecoderResult result)
        {
            throw new NotSupportedException("read only");
        }

        public int RefCnt => 1;

        public ILastHttpContent Retain()
        {
            return this;
        }

        public ILastHttpContent Retain(int increment)
        {
            return this;
        }

        public ILastHttpContent Touch()
        {
            return this;
        }

        public ILastHttpContent Touch(object hint)
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

        public override string ToString()
        {
            return "EmptyLastHttpContent";
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

    HttpHeaders TrailingHeaders();
}
