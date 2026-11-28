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

using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Handler.Codec.Http.DefaultHttpHeadersFactory;

namespace Netty.NET.Handler.Codec.Http;

/**
 * Default implementation of a {@link FullHttpResponse}.
 */
// CLR: the four @Deprecated constructors with validateHeaders/singleFieldHeaders flags, whose @deprecated texts
// name the (HttpVersion, HttpResponseStatus, ByteBuf, HttpHeadersFactory, HttpHeadersFactory) constructor, are not
// ported. setProtocolVersion/setStatus narrow DefaultHttpResponse's virtual members with covariant override
// returns; copy/duplicate/retainedDuplicate/replace/retain/touch are virtual members returning IFullHttpResponse,
// and the IByteBufHolder and IReferenceCounted members are implemented explicitly and call them (see
// DefaultHttpContent). content() is the Content property and refCnt() is RefCnt.
public class DefaultFullHttpResponse : DefaultHttpResponse, IFullHttpResponse
{

    private readonly ByteBuf content;
    private readonly HttpHeaders trailingHeaders;

    /**
     * Used to cache the value of the hash code and avoid {@link IllegalReferenceCountException}.
     */
    private int hash;

    /**
     * Create an empty HTTP response with the given HTTP version and status.
     */
    public DefaultFullHttpResponse(HttpVersion version, HttpResponseStatus status)
        : this(version, status, Unpooled.Buffer(0), HeadersFactory(), TrailersFactory())
    {
    }

    /**
     * Create an HTTP response with the given HTTP version, status, and contents.
     */
    public DefaultFullHttpResponse(HttpVersion version, HttpResponseStatus status, ByteBuf content)
        : this(version, status, content, HeadersFactory(), TrailersFactory())
    {
    }

    /**
     * Create an HTTP response with the given HTTP version, status, contents,
     * and with headers and trailers created by the given header factories.
     * <p>
     * The recommended header factory is {@link DefaultHttpHeadersFactory#headersFactory()},
     * and the recommended trailer factory is {@link DefaultHttpHeadersFactory#trailersFactory()}.
     */
    public DefaultFullHttpResponse(HttpVersion version, HttpResponseStatus status, ByteBuf content,
                                   IHttpHeadersFactory headersFactory, IHttpHeadersFactory trailersFactory)
        : this(version, status, content, headersFactory.NewHeaders(), trailersFactory.NewHeaders())
    {
    }

    /**
     * Create an HTTP response with the given HTTP version, status, contents, headers and trailers.
     */
    public DefaultFullHttpResponse(HttpVersion version, HttpResponseStatus status,
            ByteBuf content, HttpHeaders headers, HttpHeaders trailingHeaders)
        : base(version, status, headers)
    {
        this.content = CheckNotNull(content, "content");
        this.trailingHeaders = CheckNotNull(trailingHeaders, "trailingHeaders");
    }

    public virtual HttpHeaders TrailingHeaders()
    {
        return trailingHeaders;
    }

    public virtual ByteBuf Content => content;

    public virtual int RefCnt => content.RefCnt;

    public virtual IFullHttpResponse Retain()
    {
        content.Retain();
        return this;
    }

    public virtual IFullHttpResponse Retain(int increment)
    {
        content.Retain(increment);
        return this;
    }

    public virtual IFullHttpResponse Touch()
    {
        content.Touch();
        return this;
    }

    public virtual IFullHttpResponse Touch(object hint)
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

    public override IFullHttpResponse SetProtocolVersion(HttpVersion version)
    {
        base.SetProtocolVersion(version);
        return this;
    }

    public override IFullHttpResponse SetStatus(HttpResponseStatus status)
    {
        base.SetStatus(status);
        return this;
    }

    public virtual IFullHttpResponse Copy()
    {
        return Replace(Content.Copy());
    }

    public virtual IFullHttpResponse Duplicate()
    {
        return Replace(Content.Duplicate());
    }

    public virtual IFullHttpResponse RetainedDuplicate()
    {
        return Replace(Content.RetainedDuplicate());
    }

    public virtual IFullHttpResponse Replace(ByteBuf content)
    {
        IFullHttpResponse response = new DefaultFullHttpResponse(ProtocolVersion(), Status(), content,
                Headers().Copy(), TrailingHeaders().Copy());
        response.SetDecoderResult(DecoderResult());
        return response;
    }

    public override int GetHashCode()
    {
        int hash = this.hash;
        if (hash == 0)
        {
            if (ByteBufUtil.IsAccessible(Content))
            {
                try
                {
                    hash = 31 + Content.GetHashCode();
                }
                catch (IllegalReferenceCountException)
                {
                    // Handle race condition between checking refCnt() == 0 and using the object.
                    hash = 31;
                }
            }
            else
            {
                hash = 31;
            }

            hash = 31 * hash + TrailingHeaders().GetHashCode();
            hash = 31 * hash + base.GetHashCode();
            this.hash = hash;
        }

        return hash;
    }

    public override bool Equals(object o)
    {
        if (!(o is DefaultFullHttpResponse))
        {
            return false;
        }

        DefaultFullHttpResponse other = (DefaultFullHttpResponse) o;

        return base.Equals(other) &&
               Content.Equals(other.Content) &&
               TrailingHeaders().Equals(other.TrailingHeaders());
    }

    public override string ToString()
    {
        return HttpMessageUtil.AppendFullResponse(new StringBuilder(256), this).ToString();
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
