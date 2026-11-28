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

using Netty.NET.Common;

namespace Netty.NET.Handler.Codec.Http;

/**
 * Standard HTTP header names.
 * <p>
 * These are all defined as lowercase to support HTTP/2 requirements while also not
 * violating HTTP/1.x requirements.  New header names should always be lowercase.
 */
// CLR: the final class with a private constructor is a static class. The @Deprecated KEEP_ALIVE and
// PROXY_CONNECTION, whose @deprecated text names CONNECTION, are not ported; HttpHeaderValidationUtil keeps their
// header names as private constants.
public static class HttpHeaderNames
{
    /**
     * {@code "accept"}
     */
    public static readonly AsciiString ACCEPT = AsciiString.Cached("accept");
    /**
     * {@code "accept-charset"}
     */
    public static readonly AsciiString ACCEPT_CHARSET = AsciiString.Cached("accept-charset");
    /**
     * {@code "accept-encoding"}
     */
    public static readonly AsciiString ACCEPT_ENCODING = AsciiString.Cached("accept-encoding");
    /**
     * {@code "accept-language"}
     */
    public static readonly AsciiString ACCEPT_LANGUAGE = AsciiString.Cached("accept-language");
    /**
     * {@code "accept-ranges"}
     */
    public static readonly AsciiString ACCEPT_RANGES = AsciiString.Cached("accept-ranges");
    /**
     * {@code "accept-patch"}
     */
    public static readonly AsciiString ACCEPT_PATCH = AsciiString.Cached("accept-patch");
    /**
     * {@code "accept-query"}
     */
    public static readonly AsciiString ACCEPT_QUERY = AsciiString.Cached("accept-query");
    /**
     * {@code "access-control-allow-credentials"}
     */
    public static readonly AsciiString ACCESS_CONTROL_ALLOW_CREDENTIALS =
            AsciiString.Cached("access-control-allow-credentials");
    /**
     * {@code "access-control-allow-headers"}
     */
    public static readonly AsciiString ACCESS_CONTROL_ALLOW_HEADERS =
            AsciiString.Cached("access-control-allow-headers");
    /**
     * {@code "access-control-allow-methods"}
     */
    public static readonly AsciiString ACCESS_CONTROL_ALLOW_METHODS =
            AsciiString.Cached("access-control-allow-methods");
    /**
     * {@code "access-control-allow-origin"}
     */
    public static readonly AsciiString ACCESS_CONTROL_ALLOW_ORIGIN =
            AsciiString.Cached("access-control-allow-origin");
    /**
     * {@code "access-control-allow-origin"}
     */
    public static readonly AsciiString ACCESS_CONTROL_ALLOW_PRIVATE_NETWORK =
            AsciiString.Cached("access-control-allow-private-network");
    /**
     * {@code "access-control-expose-headers"}
     */
    public static readonly AsciiString ACCESS_CONTROL_EXPOSE_HEADERS =
            AsciiString.Cached("access-control-expose-headers");
    /**
     * {@code "access-control-max-age"}
     */
    public static readonly AsciiString ACCESS_CONTROL_MAX_AGE = AsciiString.Cached("access-control-max-age");
    /**
     * {@code "access-control-request-headers"}
     */
    public static readonly AsciiString ACCESS_CONTROL_REQUEST_HEADERS =
            AsciiString.Cached("access-control-request-headers");
    /**
     * {@code "access-control-request-method"}
     */
    public static readonly AsciiString ACCESS_CONTROL_REQUEST_METHOD =
            AsciiString.Cached("access-control-request-method");
    /**
     * {@code "access-control-request-private-network"}
     */
    public static readonly AsciiString ACCESS_CONTROL_REQUEST_PRIVATE_NETWORK =
            AsciiString.Cached("access-control-request-private-network");
    /**
     * {@code "age"}
     */
    public static readonly AsciiString AGE = AsciiString.Cached("age");
    /**
     * {@code "allow"}
     */
    public static readonly AsciiString ALLOW = AsciiString.Cached("allow");
    /**
     * {@code "authorization"}
     */
    public static readonly AsciiString AUTHORIZATION = AsciiString.Cached("authorization");
    /**
     * {@code "cache-control"}
     */
    public static readonly AsciiString CACHE_CONTROL = AsciiString.Cached("cache-control");
    /**
     * {@code "connection"}
     */
    public static readonly AsciiString CONNECTION = AsciiString.Cached("connection");
    /**
     * {@code "content-base"}
     */
    public static readonly AsciiString CONTENT_BASE = AsciiString.Cached("content-base");
    /**
     * {@code "content-encoding"}
     */
    public static readonly AsciiString CONTENT_ENCODING = AsciiString.Cached("content-encoding");
    /**
     * {@code "content-language"}
     */
    public static readonly AsciiString CONTENT_LANGUAGE = AsciiString.Cached("content-language");
    /**
     * {@code "content-length"}
     */
    public static readonly AsciiString CONTENT_LENGTH = AsciiString.Cached("content-length");
    /**
     * {@code "content-location"}
     */
    public static readonly AsciiString CONTENT_LOCATION = AsciiString.Cached("content-location");
    /**
     * {@code "content-transfer-encoding"}
     */
    public static readonly AsciiString CONTENT_TRANSFER_ENCODING = AsciiString.Cached("content-transfer-encoding");
    /**
     * {@code "content-disposition"}
     */
    public static readonly AsciiString CONTENT_DISPOSITION = AsciiString.Cached("content-disposition");
    /**
     * {@code "content-md5"}
     */
    public static readonly AsciiString CONTENT_MD5 = AsciiString.Cached("content-md5");
    /**
     * {@code "content-range"}
     */
    public static readonly AsciiString CONTENT_RANGE = AsciiString.Cached("content-range");
    /**
     * {@code "content-security-policy"}
     */
    public static readonly AsciiString CONTENT_SECURITY_POLICY = AsciiString.Cached("content-security-policy");
    /**
     * {@code "content-type"}
     */
    public static readonly AsciiString CONTENT_TYPE = AsciiString.Cached("content-type");
    /**
     * {@code "cookie"}
     */
    public static readonly AsciiString COOKIE = AsciiString.Cached("cookie");
    /**
     * {@code "date"}
     */
    public static readonly AsciiString DATE = AsciiString.Cached("date");
    /**
     * {@code "dnt"}
     */
    public static readonly AsciiString DNT = AsciiString.Cached("dnt");
    /**
     * {@code "etag"}
     */
    public static readonly AsciiString ETAG = AsciiString.Cached("etag");
    /**
     * {@code "expect"}
     */
    public static readonly AsciiString EXPECT = AsciiString.Cached("expect");
    /**
     * {@code "expires"}
     */
    public static readonly AsciiString EXPIRES = AsciiString.Cached("expires");
    /**
     * {@code "from"}
     */
    public static readonly AsciiString FROM = AsciiString.Cached("from");
    /**
     * {@code "host"}
     */
    public static readonly AsciiString HOST = AsciiString.Cached("host");
    /**
     * {@code "if-match"}
     */
    public static readonly AsciiString IF_MATCH = AsciiString.Cached("if-match");
    /**
     * {@code "if-modified-since"}
     */
    public static readonly AsciiString IF_MODIFIED_SINCE = AsciiString.Cached("if-modified-since");
    /**
     * {@code "if-none-match"}
     */
    public static readonly AsciiString IF_NONE_MATCH = AsciiString.Cached("if-none-match");
    /**
     * {@code "if-range"}
     */
    public static readonly AsciiString IF_RANGE = AsciiString.Cached("if-range");
    /**
     * {@code "if-unmodified-since"}
     */
    public static readonly AsciiString IF_UNMODIFIED_SINCE = AsciiString.Cached("if-unmodified-since");
    /**
     * {@code "last-modified"}
     */
    public static readonly AsciiString LAST_MODIFIED = AsciiString.Cached("last-modified");
    /**
     * {@code "location"}
     */
    public static readonly AsciiString LOCATION = AsciiString.Cached("location");
    /**
     * {@code "max-forwards"}
     */
    public static readonly AsciiString MAX_FORWARDS = AsciiString.Cached("max-forwards");
    /**
     * {@code "origin"}
     */
    public static readonly AsciiString ORIGIN = AsciiString.Cached("origin");
    /**
     * {@code "pragma"}
     */
    public static readonly AsciiString PRAGMA = AsciiString.Cached("pragma");
    /**
     * {@code "proxy-authenticate"}
     */
    public static readonly AsciiString PROXY_AUTHENTICATE = AsciiString.Cached("proxy-authenticate");
    /**
     * {@code "proxy-authorization"}
     */
    public static readonly AsciiString PROXY_AUTHORIZATION = AsciiString.Cached("proxy-authorization");
    /**
     * {@code "range"}
     */
    public static readonly AsciiString RANGE = AsciiString.Cached("range");
    /**
     * {@code "referer"}
     */
    public static readonly AsciiString REFERER = AsciiString.Cached("referer");
    /**
     * {@code "retry-after"}
     */
    public static readonly AsciiString RETRY_AFTER = AsciiString.Cached("retry-after");
    /**
     * {@code "sec-websocket-key1"}
     */
    public static readonly AsciiString SEC_WEBSOCKET_KEY1 = AsciiString.Cached("sec-websocket-key1");
    /**
     * {@code "sec-websocket-key2"}
     */
    public static readonly AsciiString SEC_WEBSOCKET_KEY2 = AsciiString.Cached("sec-websocket-key2");
    /**
     * {@code "sec-websocket-location"}
     */
    public static readonly AsciiString SEC_WEBSOCKET_LOCATION = AsciiString.Cached("sec-websocket-location");
    /**
     * {@code "sec-websocket-origin"}
     */
    public static readonly AsciiString SEC_WEBSOCKET_ORIGIN = AsciiString.Cached("sec-websocket-origin");
    /**
     * {@code "sec-websocket-protocol"}
     */
    public static readonly AsciiString SEC_WEBSOCKET_PROTOCOL = AsciiString.Cached("sec-websocket-protocol");
    /**
     * {@code "sec-websocket-version"}
     */
    public static readonly AsciiString SEC_WEBSOCKET_VERSION = AsciiString.Cached("sec-websocket-version");
    /**
     * {@code "sec-websocket-key"}
     */
    public static readonly AsciiString SEC_WEBSOCKET_KEY = AsciiString.Cached("sec-websocket-key");
    /**
     * {@code "sec-websocket-accept"}
     */
    public static readonly AsciiString SEC_WEBSOCKET_ACCEPT = AsciiString.Cached("sec-websocket-accept");
    /**
     * {@code "sec-websocket-protocol"}
     */
    public static readonly AsciiString SEC_WEBSOCKET_EXTENSIONS = AsciiString.Cached("sec-websocket-extensions");
    /**
     * {@code "server"}
     */
    public static readonly AsciiString SERVER = AsciiString.Cached("server");
    /**
     * {@code "set-cookie"}
     */
    public static readonly AsciiString SET_COOKIE = AsciiString.Cached("set-cookie");
    /**
     * {@code "set-cookie2"}
     */
    public static readonly AsciiString SET_COOKIE2 = AsciiString.Cached("set-cookie2");
    /**
     * {@code "te"}
     */
    public static readonly AsciiString TE = AsciiString.Cached("te");
    /**
     * {@code "trailer"}
     */
    public static readonly AsciiString TRAILER = AsciiString.Cached("trailer");
    /**
     * {@code "transfer-encoding"}
     */
    public static readonly AsciiString TRANSFER_ENCODING = AsciiString.Cached("transfer-encoding");
    /**
     * {@code "upgrade"}
     */
    public static readonly AsciiString UPGRADE = AsciiString.Cached("upgrade");
    /**
     * {@code "upgrade-insecure-requests"}
     */
    public static readonly AsciiString UPGRADE_INSECURE_REQUESTS = AsciiString.Cached("upgrade-insecure-requests");
    /**
     * {@code "user-agent"}
     */
    public static readonly AsciiString USER_AGENT = AsciiString.Cached("user-agent");
    /**
     * {@code "vary"}
     */
    public static readonly AsciiString VARY = AsciiString.Cached("vary");
    /**
     * {@code "via"}
     */
    public static readonly AsciiString VIA = AsciiString.Cached("via");
    /**
     * {@code "warning"}
     */
    public static readonly AsciiString WARNING = AsciiString.Cached("warning");
    /**
     * {@code "websocket-location"}
     */
    public static readonly AsciiString WEBSOCKET_LOCATION = AsciiString.Cached("websocket-location");
    /**
     * {@code "websocket-origin"}
     */
    public static readonly AsciiString WEBSOCKET_ORIGIN = AsciiString.Cached("websocket-origin");
    /**
     * {@code "websocket-protocol"}
     */
    public static readonly AsciiString WEBSOCKET_PROTOCOL = AsciiString.Cached("websocket-protocol");
    /**
     * {@code "www-authenticate"}
     */
    public static readonly AsciiString WWW_AUTHENTICATE = AsciiString.Cached("www-authenticate");
    /**
     * {@code "x-frame-options"}
     */
    public static readonly AsciiString X_FRAME_OPTIONS = AsciiString.Cached("x-frame-options");
    /**
     * {@code "x-requested-with"}
     */
    public static readonly AsciiString X_REQUESTED_WITH = AsciiString.Cached("x-requested-with");

    /**
     * {@code "alt-svc"}
     */
    public static readonly AsciiString ALT_SVC = AsciiString.Cached("alt-svc");

}
