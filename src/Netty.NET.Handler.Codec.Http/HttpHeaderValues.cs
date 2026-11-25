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
 * Standard HTTP header values.
 */
// CLR: the final class with a private constructor is a static class.
public static class HttpHeaderValues
{
    /**
     * {@code "application/json"}
     */
    public static readonly AsciiString APPLICATION_JSON = AsciiString.Cached("application/json");
    /**
     * {@code "application/manifest+json"}
     */
    public static readonly AsciiString APPLICATION_MANIFEST_JSON = AsciiString.Cached("application/manifest+json");
    /**
     * {@code "application/octet-stream"}
     */
    public static readonly AsciiString APPLICATION_OCTET_STREAM = AsciiString.Cached("application/octet-stream");
    /**
     * {@code "application/ogg"}
     */
    public static readonly AsciiString APPLICATION_OGG = AsciiString.Cached("application/ogg");
    /**
     * {@code "application/pdf"}
     */
    public static readonly AsciiString APPLICATION_PDF = AsciiString.Cached("application/pdf");
    /**
     * {@code "application/rtf"}
     */
    public static readonly AsciiString APPLICATION_RTF = AsciiString.Cached("application/rtf");
    /**
     * {@code "application/wasm"}
     */
    public static readonly AsciiString APPLICATION_WASM = AsciiString.Cached("application/wasm");
    /**
     * {@code "application/x-www-form-urlencoded"}
     */
    public static readonly AsciiString APPLICATION_X_WWW_FORM_URLENCODED =
            AsciiString.Cached("application/x-www-form-urlencoded");
    /**
     * {@code "application/xhtml+xml"}
     */
    public static readonly AsciiString APPLICATION_XHTML = AsciiString.Cached("application/xhtml+xml");
    /**
     * {@code "application/xml"}
     */
    public static readonly AsciiString APPLICATION_XML = AsciiString.Cached("application/xml");
    /**
     * {@code "application/zstd"}
     */
    public static readonly AsciiString APPLICATION_ZSTD = AsciiString.Cached("application/zstd");
    /**
     * {@code "attachment"}
     * See {@link HttpHeaderNames#CONTENT_DISPOSITION}
     */
    public static readonly AsciiString ATTACHMENT = AsciiString.Cached("attachment");
    /**
     * {@code "audio/aac"}
     */
    public static readonly AsciiString AUDIO_AAC = AsciiString.Cached("audio/aac");
    /**
     * {@code "audio/midi"}
     */
    public static readonly AsciiString AUDIO_MIDI = AsciiString.Cached("audio/midi");
    /**
     * {@code "audio/x-midi"}
     */
    public static readonly AsciiString AUDIO_X_MIDI = AsciiString.Cached("audio/x-midi");
    /**
     * {@code "audio/mpeg"}
     */
    public static readonly AsciiString AUDIO_MPEG = AsciiString.Cached("audio/mpeg");
    /**
     * {@code "audio/ogg"}
     */
    public static readonly AsciiString AUDIO_OGG = AsciiString.Cached("audio/ogg");
    /**
     * {@code "audio/wav"}
     */
    public static readonly AsciiString AUDIO_WAV = AsciiString.Cached("audio/wav");
    /**
     * {@code "audio/webm"}
     */
    public static readonly AsciiString AUDIO_WEBM = AsciiString.Cached("audio/webm");
    /**
     * {@code "base64"}
     */
    public static readonly AsciiString BASE64 = AsciiString.Cached("base64");
    /**
     * {@code "binary"}
     */
    public static readonly AsciiString BINARY = AsciiString.Cached("binary");
    /**
     * {@code "boundary"}
     */
    public static readonly AsciiString BOUNDARY = AsciiString.Cached("boundary");
    /**
     * {@code "bytes"}
     */
    public static readonly AsciiString BYTES = AsciiString.Cached("bytes");
    /**
     * {@code "charset"}
     */
    public static readonly AsciiString CHARSET = AsciiString.Cached("charset");
    /**
     * {@code "chunked"}
     */
    public static readonly AsciiString CHUNKED = AsciiString.Cached("chunked");
    /**
     * {@code "close"}
     */
    public static readonly AsciiString CLOSE = AsciiString.Cached("close");
    /**
     * {@code "compress"}
     */
    public static readonly AsciiString COMPRESS = AsciiString.Cached("compress");
    /**
     * {@code "100-continue"}
     */
    public static readonly AsciiString CONTINUE = AsciiString.Cached("100-continue");
    /**
     * {@code "deflate"}
     */
    public static readonly AsciiString DEFLATE = AsciiString.Cached("deflate");
    /**
     * {@code "x-deflate"}
     */
    public static readonly AsciiString X_DEFLATE = AsciiString.Cached("x-deflate");
    /**
     * {@code "file"}
     * See {@link HttpHeaderNames#CONTENT_DISPOSITION}
     */
    public static readonly AsciiString FILE = AsciiString.Cached("file");
    /**
     * {@code "filename"}
     * See {@link HttpHeaderNames#CONTENT_DISPOSITION}
     */
    public static readonly AsciiString FILENAME = AsciiString.Cached("filename");
    /**
     * {@code "font/otf"}
     */
    public static readonly AsciiString FONT_OTF = AsciiString.Cached("font/otf");
    /**
     * {@code "font/ttf"}
     */
    public static readonly AsciiString FONT_TTF = AsciiString.Cached("font/ttf");
    /**
     * {@code "font/woff"}
     */
    public static readonly AsciiString FONT_WOFF = AsciiString.Cached("font/woff");
    /**
     * {@code "font/woff2"}
     */
    public static readonly AsciiString FONT_WOFF2 = AsciiString.Cached("font/woff2");
    /**
     * {@code "form-data"}
     * See {@link HttpHeaderNames#CONTENT_DISPOSITION}
     */
    public static readonly AsciiString FORM_DATA = AsciiString.Cached("form-data");
    /**
     * {@code "gzip"}
     */
    public static readonly AsciiString GZIP = AsciiString.Cached("gzip");
    /**
     * {@code "br"}
     */
    public static readonly AsciiString BR = AsciiString.Cached("br");

    /**
     * {@code "snappy"}
     */
    public static readonly AsciiString SNAPPY = AsciiString.Cached("snappy");

    /**
     * {@code "zstd"}
     */
    public static readonly AsciiString ZSTD = AsciiString.Cached("zstd");
    /**
     * {@code "gzip,deflate"}
     */
    public static readonly AsciiString GZIP_DEFLATE = AsciiString.Cached("gzip,deflate");
    /**
     * {@code "x-gzip"}
     */
    public static readonly AsciiString X_GZIP = AsciiString.Cached("x-gzip");
    /**
     * {@code "identity"}
     */
    public static readonly AsciiString IDENTITY = AsciiString.Cached("identity");
    /**
     * {@code "image/avif"}
     */
    public static readonly AsciiString IMAGE_AVIF = AsciiString.Cached("image/avif");
    /**
     * {@code "image/bmp"}
     */
    public static readonly AsciiString IMAGE_BMP = AsciiString.Cached("image/bmp");
    /**
     * {@code "image/jpeg"}
     */
    public static readonly AsciiString IMAGE_JPEG = AsciiString.Cached("image/jpeg");
    /**
     * {@code "image/png"}
     */
    public static readonly AsciiString IMAGE_PNG = AsciiString.Cached("image/png");
    /**
     * {@code "image/svg+xml"}
     */
    public static readonly AsciiString IMAGE_SVG_XML = AsciiString.Cached("image/svg+xml");
    /**
     * {@code "image/tiff"}
     */
    public static readonly AsciiString IMAGE_TIFF = AsciiString.Cached("image/tiff");
    /**
     * {@code "image/webp"}
     */
    public static readonly AsciiString IMAGE_WEBP = AsciiString.Cached("image/webp");
    /**
     * {@code "keep-alive"}
     */
    public static readonly AsciiString KEEP_ALIVE = AsciiString.Cached("keep-alive");
    /**
     * {@code "max-age"}
     */
    public static readonly AsciiString MAX_AGE = AsciiString.Cached("max-age");
    /**
     * {@code "max-stale"}
     */
    public static readonly AsciiString MAX_STALE = AsciiString.Cached("max-stale");
    /**
     * {@code "min-fresh"}
     */
    public static readonly AsciiString MIN_FRESH = AsciiString.Cached("min-fresh");
    /**
     * {@code "multipart/form-data"}
     */
    public static readonly AsciiString MULTIPART_FORM_DATA = AsciiString.Cached("multipart/form-data");
    /**
     * {@code "multipart/mixed"}
     */
    public static readonly AsciiString MULTIPART_MIXED = AsciiString.Cached("multipart/mixed");
    /**
     * {@code "must-revalidate"}
     */
    public static readonly AsciiString MUST_REVALIDATE = AsciiString.Cached("must-revalidate");
    /**
     * {@code "name"}
     * See {@link HttpHeaderNames#CONTENT_DISPOSITION}
     */
    public static readonly AsciiString NAME = AsciiString.Cached("name");
    /**
     * {@code "no-cache"}
     */
    public static readonly AsciiString NO_CACHE = AsciiString.Cached("no-cache");
    /**
     * {@code "no-store"}
     */
    public static readonly AsciiString NO_STORE = AsciiString.Cached("no-store");
    /**
     * {@code "no-transform"}
     */
    public static readonly AsciiString NO_TRANSFORM = AsciiString.Cached("no-transform");
    /**
     * {@code "none"}
     */
    public static readonly AsciiString NONE = AsciiString.Cached("none");
    /**
     * {@code "0"}
     */
    public static readonly AsciiString ZERO = AsciiString.Cached("0");
    /**
     * {@code "only-if-cached"}
     */
    public static readonly AsciiString ONLY_IF_CACHED = AsciiString.Cached("only-if-cached");
    /**
     * {@code "private"}
     */
    public static readonly AsciiString PRIVATE = AsciiString.Cached("private");
    /**
     * {@code "proxy-revalidate"}
     */
    public static readonly AsciiString PROXY_REVALIDATE = AsciiString.Cached("proxy-revalidate");
    /**
     * {@code "public"}
     */
    public static readonly AsciiString PUBLIC = AsciiString.Cached("public");
    /**
     * {@code "quoted-printable"}
     */
    public static readonly AsciiString QUOTED_PRINTABLE = AsciiString.Cached("quoted-printable");
    /**
     * {@code "s-maxage"}
     */
    public static readonly AsciiString S_MAXAGE = AsciiString.Cached("s-maxage");
    /**
     * {@code "text/css"}
     */
    public static readonly AsciiString TEXT_CSS = AsciiString.Cached("text/css");
    /**
     * {@code "text/csv"}
     */
    public static readonly AsciiString TEXT_CSV = AsciiString.Cached("text/csv");
    /**
     * {@code "text/html"}
     */
    public static readonly AsciiString TEXT_HTML = AsciiString.Cached("text/html");
    /**
     * {@code "text/javascript"}
     */
    public static readonly AsciiString TEXT_JAVASCRIPT = AsciiString.Cached("text/javascript");
    /**
     * {@code "text/markdown"}
     */
    public static readonly AsciiString TEXT_MARKDOWN = AsciiString.Cached("text/markdown");
    /**
     * {@code "text/event-stream"}
     */
    public static readonly AsciiString TEXT_EVENT_STREAM = AsciiString.Cached("text/event-stream");
    /**
     * {@code "text/plain"}
     */
    public static readonly AsciiString TEXT_PLAIN = AsciiString.Cached("text/plain");
    /**
     * {@code "trailers"}
     */
    public static readonly AsciiString TRAILERS = AsciiString.Cached("trailers");
    /**
     * {@code "upgrade"}
     */
    public static readonly AsciiString UPGRADE = AsciiString.Cached("upgrade");
    /**
     * {@code "video/mp4"}
     */
    public static readonly AsciiString VIDEO_MP4 = AsciiString.Cached("video/mp4");
    /**
     * {@code "video/mpeg"}
     */
    public static readonly AsciiString VIDEO_MPEG = AsciiString.Cached("video/mpeg");
    /**
     * {@code "video/ogg"}
     */
    public static readonly AsciiString VIDEO_OGG = AsciiString.Cached("video/ogg");
    /**
     * {@code "video/webm"}
     */
    public static readonly AsciiString VIDEO_WEBM = AsciiString.Cached("video/webm");
    /**
     * {@code "websocket"}
     */
    public static readonly AsciiString WEBSOCKET = AsciiString.Cached("websocket");
    /**
     * {@code "XmlHttpRequest"}
     */
    public static readonly AsciiString XML_HTTP_REQUEST = AsciiString.Cached("XMLHttpRequest");

}
