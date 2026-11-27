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
using Netty.NET.Common;

namespace Netty.NET.Handler.Codec.Http;

// CLR: the package-private final class is internal sealed. Its getDecoderResult() overrides the not-ported
// @Deprecated HttpObject.getDecoderResult() and has no counterpart (see IHttpObject). The IByteBufHolder and
// IReferenceCounted members, which those interfaces declare with their own return types, are implemented explicitly,
// as in ILastHttpContent.EMPTY_LAST_CONTENT.
internal sealed class ComposedLastHttpContent : ILastHttpContent
{
    private readonly HttpHeaders trailingHeaders;
    private DecoderResult result;

    internal ComposedLastHttpContent(HttpHeaders trailingHeaders)
    {
        this.trailingHeaders = trailingHeaders;
    }

    internal ComposedLastHttpContent(HttpHeaders trailingHeaders, DecoderResult result) : this(trailingHeaders)
    {
        this.result = result;
    }

    public HttpHeaders TrailingHeaders()
    {
        return trailingHeaders;
    }

    public ILastHttpContent Copy()
    {
        ILastHttpContent content = new DefaultLastHttpContent(Unpooled.EmptyBuffer);
        content.TrailingHeaders().Set(TrailingHeaders());
        return content;
    }

    public ILastHttpContent Duplicate()
    {
        return Copy();
    }

    public ILastHttpContent RetainedDuplicate()
    {
        return Copy();
    }

    public ILastHttpContent Replace(ByteBuf content)
    {
        ILastHttpContent dup = new DefaultLastHttpContent(content);
        dup.TrailingHeaders().SetAll(TrailingHeaders());
        return dup;
    }

    public ILastHttpContent Retain(int increment)
    {
        return this;
    }

    public ILastHttpContent Retain()
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

    public ByteBuf Content => Unpooled.EmptyBuffer;

    public DecoderResult DecoderResult()
    {
        return result;
    }

    public void SetDecoderResult(DecoderResult result)
    {
        this.result = result;
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
