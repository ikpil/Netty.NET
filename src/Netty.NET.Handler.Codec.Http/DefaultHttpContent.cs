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

using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http;

/**
 * The default {@link HttpContent} implementation.
 */
// CLR: content() is the virtual read-only Content property of IByteBufHolder and refCnt() is RefCnt
// (docs/buffer-porting.md, Buffer holders and conversion). The narrowed copy/duplicate/retainedDuplicate/replace/
// retain/touch members are virtual members returning IHttpContent, which subclasses narrow further with covariant
// override returns (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override); the
// IByteBufHolder and IReferenceCounted members, which those interfaces declare with their own return types, are
// implemented explicitly and call them, as codec-stomp's DefaultStompFrame does.
public class DefaultHttpContent : DefaultHttpObject, IHttpContent
{

    private readonly ByteBuf content;

    /**
     * Creates a new instance with the specified chunk content.
     */
    public DefaultHttpContent(ByteBuf content)
    {
        this.content = ObjectUtil.CheckNotNull(content, "content");
    }

    public virtual ByteBuf Content => content;

    public virtual IHttpContent Copy()
    {
        return Replace(content.Copy());
    }

    public virtual IHttpContent Duplicate()
    {
        return Replace(content.Duplicate());
    }

    public virtual IHttpContent RetainedDuplicate()
    {
        return Replace(content.RetainedDuplicate());
    }

    public virtual IHttpContent Replace(ByteBuf content)
    {
        return new DefaultHttpContent(content);
    }

    public virtual int RefCnt => content.RefCnt;

    public virtual IHttpContent Retain()
    {
        content.Retain();
        return this;
    }

    public virtual IHttpContent Retain(int increment)
    {
        content.Retain(increment);
        return this;
    }

    public virtual IHttpContent Touch()
    {
        content.Touch();
        return this;
    }

    public virtual IHttpContent Touch(object hint)
    {
        content.Touch(hint);
        return this;
    }

    public virtual bool Release()
    {
        return content.Release();
    }

    public virtual bool Release(int decrement)
    {
        return content.Release(decrement);
    }

    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) +
               "(data: " + Content + ", decoderResult: " + DecoderResult() + ')';
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
