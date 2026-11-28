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
 * Default implementation of {@link FullHttpRequest}.
 */
// CLR: the two @Deprecated constructors with a validateHeaders flag, whose @deprecated texts name the
// (HttpVersion, HttpMethod, String, ByteBuf, HttpHeadersFactory, HttpHeadersFactory) constructor, are not ported.
// setProtocolVersion/setMethod/setUri narrow DefaultHttpRequest's virtual members with covariant override returns;
// copy/duplicate/retainedDuplicate/replace/retain/touch are virtual members returning IFullHttpRequest, and the
// IByteBufHolder and IReferenceCounted members are implemented explicitly and call them (see DefaultHttpContent).
// content() is the Content property and refCnt() is RefCnt.
public class DefaultFullHttpRequest : DefaultHttpRequest, IFullHttpRequest
{
    private readonly ByteBuf content;
    private readonly HttpHeaders trailingHeader;

    /**
     * Used to cache the value of the hash code and avoid {@link IllegalReferenceCountException}.
     */
    private int hash;

    /**
     * Create a full HTTP response with the given HTTP version, method, and URI.
     */
    public DefaultFullHttpRequest(HttpVersion httpVersion, HttpMethod method, string uri)
        : this(httpVersion, method, uri, Unpooled.Buffer(0), HeadersFactory(), TrailersFactory())
    {
    }

    /**
     * Create a full HTTP response with the given HTTP version, method, URI, and contents.
     */
    public DefaultFullHttpRequest(HttpVersion httpVersion, HttpMethod method, string uri, ByteBuf content)
        : this(httpVersion, method, uri, content, HeadersFactory(), TrailersFactory())
    {
    }

    /**
     * Create a full HTTP response with the given HTTP version, method, URI, contents,
     * and factories for creating headers and trailers.
     * <p>
     * The recommended default header factory is {@link DefaultHttpHeadersFactory#headersFactory()},
     * and the recommended default trailer factory is {@link DefaultHttpHeadersFactory#trailersFactory()}.
     */
    public DefaultFullHttpRequest(HttpVersion httpVersion, HttpMethod method, string uri,
            ByteBuf content, IHttpHeadersFactory headersFactory, IHttpHeadersFactory trailersFactory)
        : this(httpVersion, method, uri, content, headersFactory.NewHeaders(), trailersFactory.NewHeaders())
    {
    }

    /**
     * Create a full HTTP response with the given HTTP version, method, URI, contents, and header and trailer objects.
     */
    public DefaultFullHttpRequest(HttpVersion httpVersion, HttpMethod method, string uri,
            ByteBuf content, HttpHeaders headers, HttpHeaders trailingHeader)
        : this(httpVersion, method, uri, content, headers, trailingHeader, true)
    {
    }

    /**
     * Create a full HTTP response with the given HTTP version, method, URI, contents, and header and trailer objects.
     */
    public DefaultFullHttpRequest(HttpVersion httpVersion, HttpMethod method, string uri,
            ByteBuf content, HttpHeaders headers, HttpHeaders trailingHeader, bool validateRequestLine)
        : base(httpVersion, method, uri, headers, validateRequestLine)
    {
        this.content = CheckNotNull(content, "content");
        this.trailingHeader = CheckNotNull(trailingHeader, "trailingHeader");
    }

    public virtual HttpHeaders TrailingHeaders()
    {
        return trailingHeader;
    }

    public virtual ByteBuf Content => content;

    public virtual int RefCnt => content.RefCnt;

    public virtual IFullHttpRequest Retain()
    {
        content.Retain();
        return this;
    }

    public virtual IFullHttpRequest Retain(int increment)
    {
        content.Retain(increment);
        return this;
    }

    public virtual IFullHttpRequest Touch()
    {
        content.Touch();
        return this;
    }

    public virtual IFullHttpRequest Touch(object hint)
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

    public override IFullHttpRequest SetProtocolVersion(HttpVersion version)
    {
        base.SetProtocolVersion(version);
        return this;
    }

    public override IFullHttpRequest SetMethod(HttpMethod method)
    {
        base.SetMethod(method);
        return this;
    }

    public override IFullHttpRequest SetUri(string uri)
    {
        base.SetUri(uri);
        return this;
    }

    public virtual IFullHttpRequest Copy()
    {
        return Replace(Content.Copy());
    }

    public virtual IFullHttpRequest Duplicate()
    {
        return Replace(Content.Duplicate());
    }

    public virtual IFullHttpRequest RetainedDuplicate()
    {
        return Replace(Content.RetainedDuplicate());
    }

    public virtual IFullHttpRequest Replace(ByteBuf content)
    {
        IFullHttpRequest request = new DefaultFullHttpRequest(ProtocolVersion(), Method(), Uri(), content,
                Headers().Copy(), TrailingHeaders().Copy());
        request.SetDecoderResult(DecoderResult());
        return request;
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
        if (!(o is DefaultFullHttpRequest))
        {
            return false;
        }

        DefaultFullHttpRequest other = (DefaultFullHttpRequest) o;

        return base.Equals(other) &&
               Content.Equals(other.Content) &&
               TrailingHeaders().Equals(other.TrailingHeaders());
    }

    public override string ToString()
    {
        return HttpMessageUtil.AppendFullRequest(new StringBuilder(256), this).ToString();
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
