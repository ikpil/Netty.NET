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
using System.Text;
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Handler.Codec.Http.DefaultHttpHeadersFactory;

namespace Netty.NET.Handler.Codec.Http;

/**
 * The default {@link HttpRequest} implementation.
 */
// CLR: the @Deprecated DefaultHttpRequest(HttpVersion, HttpMethod, String, boolean), whose @deprecated text names
// DefaultHttpRequest(HttpVersion, HttpMethod, String), and the @Deprecated getMethod()/getUri() are not ported.
// setProtocolVersion narrows its return type with a covariant override return, and setMethod/ setUri are virtual
// so that DefaultFullHttpRequest narrows them the same way
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override). String.equalsIgnoreCase
// is string.Equals with StringComparison.OrdinalIgnoreCase.
public class DefaultHttpRequest : DefaultHttpMessage, IHttpRequest
{
    private const int HASH_CODE_PRIME = 31;
    private HttpMethod method;
    private string uri;
    private readonly bool validateRequestLine;

    /**
     * Creates a new instance.
     *
     * @param httpVersion the HTTP version of the request
     * @param method      the HTTP method of the request
     * @param uri         the URI or path of the request
     */
    public DefaultHttpRequest(HttpVersion httpVersion, HttpMethod method, string uri)
        : this(httpVersion, method, uri, HeadersFactory().NewHeaders())
    {
    }

    /**
     * Creates a new instance.
     *
     * @param httpVersion       the HTTP version of the request
     * @param method            the HTTP method of the request
     * @param uri               the URI or path of the request
     * @param headersFactory    the {@link HttpHeadersFactory} used to create the headers for this Request.
     * The recommended default is {@link DefaultHttpHeadersFactory#headersFactory()}.
     */
    public DefaultHttpRequest(HttpVersion httpVersion, HttpMethod method, string uri,
                              IHttpHeadersFactory headersFactory)
        : this(httpVersion, method, uri, headersFactory.NewHeaders())
    {
    }

    /**
     * Creates a new instance.
     *
     * @param httpVersion       the HTTP version of the request
     * @param method            the HTTP method of the request
     * @param uri               the URI or path of the request
     * @param headers           the Headers for this Request
     */
    public DefaultHttpRequest(HttpVersion httpVersion, HttpMethod method, string uri, HttpHeaders headers)
        : this(httpVersion, method, uri, headers, true)
    {
    }

    /**
     * Creates a new instance.
     *
     * @param httpVersion       the HTTP version of the request
     * @param method            the HTTP method of the request
     * @param uri               the URI or path of the request
     * @param headers           the Headers for this Request
     */
    public DefaultHttpRequest(HttpVersion httpVersion, HttpMethod method, string uri, HttpHeaders headers,
                              bool validateRequestLine)
        : base(httpVersion, headers)
    {
        this.method = CheckNotNull(method, "method");
        this.uri = CheckNotNull(uri, "uri");
        this.validateRequestLine = validateRequestLine;

        if (validateRequestLine)
        {
            HttpUtil.ValidateRequestLineTokens(method, uri);
        }
    }

    public virtual HttpMethod Method()
    {
        return method;
    }

    public virtual string Uri()
    {
        return uri;
    }

    public virtual IHttpRequest SetMethod(HttpMethod method)
    {
        CheckNotNull(method, "method");

        if (validateRequestLine)
        {
            HttpUtil.ValidateRequestLineTokens(method, uri);
        }

        this.method = method;
        return this;
    }

    public virtual IHttpRequest SetUri(string uri)
    {
        CheckNotNull(uri, "uri");

        if (validateRequestLine)
        {
            HttpUtil.ValidateRequestLineTokens(method, uri);
        }

        this.uri = uri;
        return this;
    }

    public override IHttpRequest SetProtocolVersion(HttpVersion version)
    {
        base.SetProtocolVersion(version);
        return this;
    }

    public override int GetHashCode()
    {
        int result = 1;
        result = HASH_CODE_PRIME * result + method.GetHashCode();
        result = HASH_CODE_PRIME * result + uri.GetHashCode();
        result = HASH_CODE_PRIME * result + base.GetHashCode();
        return result;
    }

    public override bool Equals(object o)
    {
        if (!(o is DefaultHttpRequest))
        {
            return false;
        }

        DefaultHttpRequest other = (DefaultHttpRequest) o;

        return Method().Equals(other.Method()) &&
               string.Equals(Uri(), other.Uri(), StringComparison.OrdinalIgnoreCase) &&
               base.Equals(o);
    }

    public override string ToString()
    {
        return HttpMessageUtil.AppendRequest(new StringBuilder(256), this).ToString();
    }
}
