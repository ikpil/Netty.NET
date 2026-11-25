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
 */
// CLR: the final class with a private constructor is a static class of static readonly AsciiString constants
// (AsciiString.cached is the Common AsciiString.Cached), as core HttpHeaderValues.
public static class RtspHeaderValues
{
    /**
     * {@code "append"}
     */
    public static readonly AsciiString APPEND = AsciiString.Cached("append");
    /**
     * {@code "AVP"}
     */
    public static readonly AsciiString AVP = AsciiString.Cached("AVP");
    /**
     * {@code "bytes"}
     */
    public static readonly AsciiString BYTES = HttpHeaderValues.BYTES;
    /**
     * {@code "charset"}
     */
    public static readonly AsciiString CHARSET = HttpHeaderValues.CHARSET;
    /**
     * {@code "client_port"}
     */
    public static readonly AsciiString CLIENT_PORT = AsciiString.Cached("client_port");
    /**
     * {@code "clock"}
     */
    public static readonly AsciiString CLOCK = AsciiString.Cached("clock");
    /**
     * {@code "close"}
     */
    public static readonly AsciiString CLOSE = HttpHeaderValues.CLOSE;
    /**
     * {@code "compress"}
     */
    public static readonly AsciiString COMPRESS = HttpHeaderValues.COMPRESS;
    /**
     * {@code "100-continue"}
     */
    public static readonly AsciiString CONTINUE =  HttpHeaderValues.CONTINUE;
    /**
     * {@code "deflate"}
     */
    public static readonly AsciiString DEFLATE = HttpHeaderValues.DEFLATE;
    /**
     * {@code "destination"}
     */
    public static readonly AsciiString DESTINATION = AsciiString.Cached("destination");
    /**
     * {@code "gzip"}
     */
    public static readonly AsciiString GZIP = HttpHeaderValues.GZIP;
    /**
     * {@code "identity"}
     */
    public static readonly AsciiString IDENTITY = HttpHeaderValues.IDENTITY;
    /**
     * {@code "interleaved"}
     */
    public static readonly AsciiString INTERLEAVED = AsciiString.Cached("interleaved");
    /**
     * {@code "keep-alive"}
     */
    public static readonly AsciiString KEEP_ALIVE = HttpHeaderValues.KEEP_ALIVE;
    /**
     * {@code "layers"}
     */
    public static readonly AsciiString LAYERS = AsciiString.Cached("layers");
    /**
     * {@code "max-age"}
     */
    public static readonly AsciiString MAX_AGE = HttpHeaderValues.MAX_AGE;
    /**
     * {@code "max-stale"}
     */
    public static readonly AsciiString MAX_STALE = HttpHeaderValues.MAX_STALE;
    /**
     * {@code "min-fresh"}
     */
    public static readonly AsciiString MIN_FRESH = HttpHeaderValues.MIN_FRESH;
    /**
     * {@code "mode"}
     */
    public static readonly AsciiString MODE = AsciiString.Cached("mode");
    /**
     * {@code "multicast"}
     */
    public static readonly AsciiString MULTICAST = AsciiString.Cached("multicast");
    /**
     * {@code "must-revalidate"}
     */
    public static readonly AsciiString MUST_REVALIDATE = HttpHeaderValues.MUST_REVALIDATE;
    /**
     * {@code "none"}
     */
    public static readonly AsciiString NONE = HttpHeaderValues.NONE;
    /**
     * {@code "no-cache"}
     */
    public static readonly AsciiString NO_CACHE = HttpHeaderValues.NO_CACHE;
    /**
     * {@code "no-transform"}
     */
    public static readonly AsciiString NO_TRANSFORM = HttpHeaderValues.NO_TRANSFORM;
    /**
     * {@code "only-if-cached"}
     */
    public static readonly AsciiString ONLY_IF_CACHED = HttpHeaderValues.ONLY_IF_CACHED;
    /**
     * {@code "port"}
     */
    public static readonly AsciiString PORT = AsciiString.Cached("port");
    /**
     * {@code "private"}
     */
    public static readonly AsciiString PRIVATE = HttpHeaderValues.PRIVATE;
    /**
     * {@code "proxy-revalidate"}
     */
    public static readonly AsciiString PROXY_REVALIDATE = HttpHeaderValues.PROXY_REVALIDATE;
    /**
     * {@code "public"}
     */
    public static readonly AsciiString PUBLIC = HttpHeaderValues.PUBLIC;
    /**
     * {@code "RTP"}
     */
    public static readonly AsciiString RTP = AsciiString.Cached("RTP");
    /**
     * {@code "rtptime"}
     */
    public static readonly AsciiString RTPTIME = AsciiString.Cached("rtptime");
    /**
     * {@code "seq"}
     */
    public static readonly AsciiString SEQ = AsciiString.Cached("seq");
    /**
     * {@code "server_port"}
     */
    public static readonly AsciiString SERVER_PORT = AsciiString.Cached("server_port");
    /**
     * {@code "ssrc"}
     */
    public static readonly AsciiString SSRC = AsciiString.Cached("ssrc");
    /**
     * {@code "TCP"}
     */
    public static readonly AsciiString TCP = AsciiString.Cached("TCP");
    /**
     * {@code "time"}
     */
    public static readonly AsciiString TIME = AsciiString.Cached("time");
    /**
     * {@code "timeout"}
     */
    public static readonly AsciiString TIMEOUT = AsciiString.Cached("timeout");
    /**
     * {@code "ttl"}
     */
    public static readonly AsciiString TTL = AsciiString.Cached("ttl");
    /**
     * {@code "UDP"}
     */
    public static readonly AsciiString UDP = AsciiString.Cached("UDP");
    /**
     * {@code "unicast"}
     */
    public static readonly AsciiString UNICAST = AsciiString.Cached("unicast");
    /**
     * {@code "url"}
     */
    public static readonly AsciiString URL = AsciiString.Cached("url");
}
