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
 * An HTTP response.
 *
 * <h3>Accessing Cookies</h3>
 * <p>
 * Unlike the Servlet API, {@link io.netty.handler.codec.http.cookie.Cookie} support is provided
 * separately via {@link io.netty.handler.codec.http.cookie.ServerCookieDecoder},
 * {@link io.netty.handler.codec.http.cookie.ClientCookieDecoder},
 * {@link io.netty.handler.codec.http.cookie.ServerCookieEncoder},
 * and {@link io.netty.handler.codec.http.cookie.ClientCookieEncoder}.
 *
 * @see HttpRequest
 * @see io.netty.handler.codec.http.cookie.ServerCookieDecoder
 * @see io.netty.handler.codec.http.cookie.ClientCookieDecoder
 * @see io.netty.handler.codec.http.cookie.ServerCookieEncoder
 * @see io.netty.handler.codec.http.cookie.ClientCookieEncoder
 */
// CLR: the Java interface takes the I prefix (owner rule). The @Deprecated getStatus(), whose @deprecated text
// names status(), is not ported; the narrowed setProtocolVersion is inherited (see IHttpRequest).
public interface IHttpResponse : IHttpMessage
{

    /**
     * Returns the status of this {@link HttpResponse}.
     *
     * @return The {@link HttpResponseStatus} of this {@link HttpResponse}
     */
    HttpResponseStatus Status();

    /**
     * Set the status of this {@link HttpResponse}.
     */
    IHttpResponse SetStatus(HttpResponseStatus status);
}
