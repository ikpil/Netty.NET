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
using Netty.NET.Handler.Codec.Http;

namespace Netty.NET.Handler.Codec.Rtsp;

/**
 * Standard RTSP header names.
 * <p>
 * These are all defined as lowercase to support HTTP/2 requirements while also not
 * violating RTSP/1.x requirements.  New header names should always be lowercase.
 */
// CLR: the final class with a private constructor is a static class of static readonly AsciiString constants
// (AsciiString.cached is the Common AsciiString.Cached), as core HttpHeaderNames.
public static class RtspHeaderNames
{
    /**
     * {@code "accept"}
     */
    public static readonly AsciiString ACCEPT = HttpHeaderNames.ACCEPT;
    /**
     * {@code "accept-encoding"}
     */
    public static readonly AsciiString ACCEPT_ENCODING = HttpHeaderNames.ACCEPT_ENCODING;
    /**
     * {@code "accept-language"}
     */
    public static readonly AsciiString ACCEPT_LANGUAGE = HttpHeaderNames.ACCEPT_LANGUAGE;
    /**
     * {@code "allow"}
     */
    public static readonly AsciiString ALLOW = AsciiString.Cached("allow");
    /**
     * {@code "authorization"}
     */
    public static readonly AsciiString AUTHORIZATION = HttpHeaderNames.AUTHORIZATION;
    /**
     * {@code "bandwidth"}
     */
    public static readonly AsciiString BANDWIDTH = AsciiString.Cached("bandwidth");
    /**
     * {@code "blocksize"}
     */
    public static readonly AsciiString BLOCKSIZE = AsciiString.Cached("blocksize");
    /**
     * {@code "cache-control"}
     */
    public static readonly AsciiString CACHE_CONTROL = HttpHeaderNames.CACHE_CONTROL;
    /**
     * {@code "conference"}
     */
    public static readonly AsciiString CONFERENCE = AsciiString.Cached("conference");
    /**
     * {@code "connection"}
     */
    public static readonly AsciiString CONNECTION = HttpHeaderNames.CONNECTION;
    /**
     * {@code "content-base"}
     */
    public static readonly AsciiString CONTENT_BASE = HttpHeaderNames.CONTENT_BASE;
    /**
     * {@code "content-encoding"}
     */
    public static readonly AsciiString CONTENT_ENCODING = HttpHeaderNames.CONTENT_ENCODING;
    /**
     * {@code "content-language"}
     */
    public static readonly AsciiString CONTENT_LANGUAGE = HttpHeaderNames.CONTENT_LANGUAGE;
    /**
     * {@code "content-length"}
     */
    public static readonly AsciiString CONTENT_LENGTH = HttpHeaderNames.CONTENT_LENGTH;
    /**
     * {@code "content-location"}
     */
    public static readonly AsciiString CONTENT_LOCATION = HttpHeaderNames.CONTENT_LOCATION;
    /**
     * {@code "content-type"}
     */
    public static readonly AsciiString CONTENT_TYPE = HttpHeaderNames.CONTENT_TYPE;
    /**
     * {@code "cseq"}
     */
    public static readonly AsciiString CSEQ = AsciiString.Cached("cseq");
    /**
     * {@code "date"}
     */
    public static readonly AsciiString DATE = HttpHeaderNames.DATE;
    /**
     * {@code "expires"}
     */
    public static readonly AsciiString EXPIRES = HttpHeaderNames.EXPIRES;
    /**
     * {@code "from"}
     */
    public static readonly AsciiString FROM = HttpHeaderNames.FROM;
    /**
     * {@code "host"}
     */
    public static readonly AsciiString HOST = HttpHeaderNames.HOST;
    /**
     * {@code "if-match"}
     */
    public static readonly AsciiString IF_MATCH = HttpHeaderNames.IF_MATCH;
    /**
     * {@code "if-modified-since"}
     */
    public static readonly AsciiString IF_MODIFIED_SINCE = HttpHeaderNames.IF_MODIFIED_SINCE;
    /**
     * {@code "keymgmt"}
     */
    public static readonly AsciiString KEYMGMT = AsciiString.Cached("keymgmt");
    /**
     * {@code "last-modified"}
     */
    public static readonly AsciiString LAST_MODIFIED = HttpHeaderNames.LAST_MODIFIED;
    /**
     * {@code "proxy-authenticate"}
     */
    public static readonly AsciiString PROXY_AUTHENTICATE = HttpHeaderNames.PROXY_AUTHENTICATE;
    /**
     * {@code "proxy-require"}
     */
    public static readonly AsciiString PROXY_REQUIRE = AsciiString.Cached("proxy-require");
    /**
     * {@code "public"}
     */
    public static readonly AsciiString PUBLIC = AsciiString.Cached("public");
    /**
     * {@code "range"}
     */
    public static readonly AsciiString RANGE = HttpHeaderNames.RANGE;
    /**
     * {@code "referer"}
     */
    public static readonly AsciiString REFERER = HttpHeaderNames.REFERER;
    /**
     * {@code "require"}
     */
    public static readonly AsciiString REQUIRE = AsciiString.Cached("require");
    /**
     * {@code "retry-after"}
     */
    public static readonly AsciiString RETRT_AFTER = HttpHeaderNames.RETRY_AFTER;
    /**
     * {@code "rtp-info"}
     */
    public static readonly AsciiString RTP_INFO = AsciiString.Cached("rtp-info");
    /**
     * {@code "scale"}
     */
    public static readonly AsciiString SCALE = AsciiString.Cached("scale");
    /**
     * {@code "session"}
     */
    public static readonly AsciiString SESSION = AsciiString.Cached("session");
    /**
     * {@code "server"}
     */
    public static readonly AsciiString SERVER = HttpHeaderNames.SERVER;
    /**
     * {@code "speed"}
     */
    public static readonly AsciiString SPEED = AsciiString.Cached("speed");
    /**
     * {@code "timestamp"}
     */
    public static readonly AsciiString TIMESTAMP = AsciiString.Cached("timestamp");
    /**
     * {@code "transport"}
     */
    public static readonly AsciiString TRANSPORT = AsciiString.Cached("transport");
    /**
     * {@code "unsupported"}
     */
    public static readonly AsciiString UNSUPPORTED = AsciiString.Cached("unsupported");
    /**
     * {@code "user-agent"}
     */
    public static readonly AsciiString USER_AGENT = HttpHeaderNames.USER_AGENT;
    /**
     * {@code "vary"}
     */
    public static readonly AsciiString VARY = HttpHeaderNames.VARY;
    /**
     * {@code "via"}
     */
    public static readonly AsciiString VIA = HttpHeaderNames.VIA;
    /**
     * {@code "www-authenticate"}
     */
    public static readonly AsciiString WWW_AUTHENTICATE = HttpHeaderNames.WWW_AUTHENTICATE;
}
