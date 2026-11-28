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

using System.Text;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Handler.Codec.Http.DefaultHttpHeadersFactory;

namespace Netty.NET.Handler.Codec.Http;

/**
 * The default {@link HttpResponse} implementation.
 */
// CLR: the two @Deprecated constructors with boolean flags, whose @deprecated texts name
// DefaultHttpResponse(HttpVersion, HttpResponseStatus, HttpHeadersFactory), and the @Deprecated getStatus() are
// not ported. setProtocolVersion narrows its return type with a covariant override return, and setStatus is
// virtual so that DefaultFullHttpResponse narrows it
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override).
public class DefaultHttpResponse : DefaultHttpMessage, IHttpResponse
{

    private HttpResponseStatus status;

    /**
     * Creates a new instance.
     *
     * @param version the HTTP version of this response
     * @param status  the status of this response
     */
    public DefaultHttpResponse(HttpVersion version, HttpResponseStatus status)
        : this(version, status, HeadersFactory())
    {
    }

    /**
     * Creates a new instance.
     *
     * @param version           the HTTP version of this response
     * @param status            the status of this response
     * @param headersFactory    the {@link HttpHeadersFactory} used to create the headers for this HTTP Response.
     * The recommended default is {@link DefaultHttpHeadersFactory#headersFactory()}.
     */
    public DefaultHttpResponse(HttpVersion version, HttpResponseStatus status, IHttpHeadersFactory headersFactory)
        : this(version, status, headersFactory.NewHeaders())
    {
    }

    /**
     * Creates a new instance.
     *
     * @param version           the HTTP version of this response
     * @param status            the status of this response
     * @param headers           the headers for this HTTP Response
     */
    public DefaultHttpResponse(HttpVersion version, HttpResponseStatus status, HttpHeaders headers)
        : base(version, headers)
    {
        this.status = CheckNotNull(status, "status");
    }

    public virtual HttpResponseStatus Status()
    {
        return status;
    }

    public virtual IHttpResponse SetStatus(HttpResponseStatus status)
    {
        this.status = ObjectUtil.CheckNotNull(status, "status");
        return this;
    }

    public override IHttpResponse SetProtocolVersion(HttpVersion version)
    {
        base.SetProtocolVersion(version);
        return this;
    }

    public override string ToString()
    {
        return HttpMessageUtil.AppendResponse(new StringBuilder(256), this).ToString();
    }

    public override int GetHashCode()
    {
        int result = 1;
        result = 31 * result + status.GetHashCode();
        result = 31 * result + base.GetHashCode();
        return result;
    }

    public override bool Equals(object o)
    {
        if (!(o is DefaultHttpResponse))
        {
            return false;
        }

        DefaultHttpResponse other = (DefaultHttpResponse) o;

        return status.Equals(other.Status()) && base.Equals(o);
    }
}
