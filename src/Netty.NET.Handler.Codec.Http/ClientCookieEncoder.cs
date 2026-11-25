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
using System.Collections.Generic;

namespace Netty.NET.Handler.Codec.Http;

/**
 * A <a href="https://tools.ietf.org/html/rfc6265">RFC6265</a> compliant cookie encoder to be used client side,
 * so only name=value pairs are sent.
 *
 * User-Agents are not supposed to interpret cookies, so, if present, {@link Cookie#rawValue()} will be used.
 * Otherwise, {@link Cookie#value()} will be used unquoted.
 *
 * Note that multiple cookies are supposed to be sent at once in a single "Cookie" header.
 *
 * <pre>
 * // Example
 * {@link HttpRequest} req = ...;
 * res.setHeader("Cookie", {@link ClientCookieEncoder}.encode("JSESSIONID", "1234"));
 * </pre>
 *
 * @see ClientCookieDecoder
 */
// CLR: the class and its methods are @Deprecated without @deprecated text (no replacement named), so they are ported
// with [Obsolete] (owner decision 2026-11-16). The final class with a private constructor is a static class. Its
// parameters of the @Deprecated io.netty.handler.codec.http.Cookie, which is not ported because its @deprecated text
// names io.netty.handler.codec.http.cookie.Cookie, are that replacement, Netty.NET.Handler.Codec.Http.Cookie.ICookie;
// every Java call passes a Cookie that is such a cookie.Cookie, and the methods delegate to the cookie package's
// ClientCookieEncoder.LAX as in Java. Iterable<Cookie> is IEnumerable<ICookie>.
[Obsolete]
public static class ClientCookieEncoder
{

    /**
     * Encodes the specified cookie into a Cookie header value.
     *
     * @param name the cookie name
     * @param value the cookie value
     * @return a Rfc6265 style Cookie header value
     */
    [Obsolete]
    public static string Encode(string name, string value)
    {
        return Cookie.ClientCookieEncoder.LAX.Encode(name, value);
    }

    /**
     * Encodes the specified cookie into a Cookie header value.
     *
     * @param cookie the specified cookie
     * @return a Rfc6265 style Cookie header value
     */
    [Obsolete]
    public static string Encode(Cookie.ICookie cookie)
    {
        return Cookie.ClientCookieEncoder.LAX.Encode(cookie);
    }

    /**
     * Encodes the specified cookies into a single Cookie header value.
     *
     * @param cookies some cookies
     * @return a Rfc6265 style Cookie header value, null if no cookies are passed.
     */
    [Obsolete]
    public static string Encode(params Cookie.ICookie[] cookies)
    {
        return Cookie.ClientCookieEncoder.LAX.Encode(cookies);
    }

    /**
     * Encodes the specified cookies into a single Cookie header value.
     *
     * @param cookies some cookies
     * @return a Rfc6265 style Cookie header value, null if no cookies are passed.
     */
    [Obsolete]
    public static string Encode(IEnumerable<Cookie.ICookie> cookies)
    {
        return Cookie.ClientCookieEncoder.LAX.Encode(cookies);
    }

    // CLR: the private constructor and its comment; a static class has no constructor.
    // unused
}
