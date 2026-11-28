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

namespace Netty.NET.Handler.Codec.Http;

/**
 * An HTTP request.
 *
 * <h3>Accessing Query Parameters and Cookie</h3>
 * <p>
 * Unlike the Servlet API, a query string is constructed and decomposed by
 * {@link QueryStringEncoder} and {@link QueryStringDecoder}.
 *
 * {@link io.netty.handler.codec.http.cookie.Cookie} support is also provided
 * separately via {@link io.netty.handler.codec.http.cookie.ServerCookieDecoder},
 * {@link io.netty.handler.codec.http.cookie.ClientCookieDecoder},
 * {@link io.netty.handler.codec.http.cookie.ServerCookieEncoder},
 * and {@link io.netty.handler.codec.http.cookie.ClientCookieEncoder}.
 *
 * @see HttpResponse
 * @see io.netty.handler.codec.http.cookie.ServerCookieDecoder
 * @see io.netty.handler.codec.http.cookie.ClientCookieDecoder
 * @see io.netty.handler.codec.http.cookie.ServerCookieEncoder
 * @see io.netty.handler.codec.http.cookie.ClientCookieEncoder
 */
// CLR: the Java interface takes the I prefix (owner rule). The @Deprecated getMethod() and getUri(), whose
// @deprecated texts name method() and uri(), are not ported. The setProtocolVersion re-declaration that only
// narrows the return type is inherited from IHttpMessage, not repeated: C# 9 covariant returns cover class
// overrides only, so the implementations narrow it.
public interface IHttpRequest : IHttpMessage
{

    /**
     * Returns the {@link HttpMethod} of this {@link HttpRequest}.
     *
     * @return The {@link HttpMethod} of this {@link HttpRequest}
     */
    HttpMethod Method();

    /**
     * Set the {@link HttpMethod} of this {@link HttpRequest}.
     */
    IHttpRequest SetMethod(HttpMethod method);

    /**
     * Returns the requested URI (or alternatively, path)
     *
     * @return The URI being requested
     */
    string Uri();

    /**
     *  Set the requested URI (or alternatively, path)
     */
    IHttpRequest SetUri(string uri);
}
