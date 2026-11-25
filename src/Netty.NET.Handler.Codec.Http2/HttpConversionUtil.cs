/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;
using static Netty.NET.Common.AsciiString;
using static Netty.NET.Common.ByteProcessor;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Provides utility methods and constants for the HTTP/2 to HTTP conversion
 */
// CLR: the final class with a private constructor is a static class; the static initializers are the static
// constructors. The codec-http interfaces take the I prefix (IHttpMessage, IHttpRequest, IFullHttpRequest, ...) and
// their getters are methods (docs/codec-http-porting.md, Messages). The @Deprecated message constructors with a
// validateHeaders flag are not ported in codec-http (named replacements: the HttpHeadersFactory constructors), so the
// conversions call the HttpHeadersFactory constructors with headersFactory().withValidation(validate) and
// trailersFactory().withValidation(validate), which is what the deprecated constructors do in Java. The @Deprecated
// HttpHeaderNames.KEEP_ALIVE and PROXY_CONNECTION are not ported in codec-http (docs/codec-http-porting.md, Core not
// ported), so the blacklist uses AsciiString constants with the same names. HttpHeaders.get returns a string, which is
// wrapped in the Common StringCharSequence where Java uses the String as a CharSequence; StringUtil.unescapeCsvFields
// takes and returns strings in the Common port (docs/codec-http-porting.md, Headers). InternalThreadLocalMap's
// stringBuilder() is a new StringBuilder (retired; docs/codec-http-porting.md, Shared CLR adaptations). The ByteProcessor
// constants are Func<byte, bool>. The catch (Http2Exception) { throw e; } clauses rethrow with throw;. new
// IllegalStateException(e) keeps Java's message cause.toString() through ThrowableUtil.ThrowableToString. java.net.URI is
// the private CreateUri/UriScheme/UriPort/UriAuthority helpers (see CreateUri). IllegalArgumentException is
// ArgumentException; the package-private static members are internal.
public static class HttpConversionUtil
{
    // Parsing logic adapted from Vert.x HttpUtils.parsePath/parseQuery:
    // https://github.com/eclipse-vertx/vert.x/blob/98a8ef6c8b408009ff86eb8277fd0bbb2b866857/
    // vertx-core/src/main/java/io/vertx/core/http/impl/HttpUtils.java#L279-L319
    /**
     * The set of headers that should not be directly copied when converting headers from HTTP to HTTP/2.
     */
    private static readonly CharSequenceMap<AsciiString> HTTP_TO_HTTP2_HEADER_BLACKLIST =
            new CharSequenceMap<AsciiString>();

    private static readonly CharSequenceMap<AsciiString> HTTP2_TO_HTTP_HEADER_BLACKLIST =
            new CharSequenceMap<AsciiString>(false);

    static HttpConversionUtil()
    {
        HTTP_TO_HTTP2_HEADER_BLACKLIST.Add(CONNECTION, EMPTY_STRING);
        // CLR: HttpHeaderNames.KEEP_ALIVE (deprecated, not ported in codec-http).
        AsciiString keepAlive = AsciiString.Cached("keep-alive");
        HTTP_TO_HTTP2_HEADER_BLACKLIST.Add(keepAlive, EMPTY_STRING);
        // CLR: HttpHeaderNames.PROXY_CONNECTION (deprecated, not ported in codec-http).
        AsciiString proxyConnection = AsciiString.Cached("proxy-connection");
        HTTP_TO_HTTP2_HEADER_BLACKLIST.Add(proxyConnection, EMPTY_STRING);
        HTTP_TO_HTTP2_HEADER_BLACKLIST.Add(HttpHeaderNames.TRANSFER_ENCODING, EMPTY_STRING);
        HTTP_TO_HTTP2_HEADER_BLACKLIST.Add(HttpHeaderNames.HOST, EMPTY_STRING);
        HTTP_TO_HTTP2_HEADER_BLACKLIST.Add(HttpHeaderNames.UPGRADE, EMPTY_STRING);
        HTTP_TO_HTTP2_HEADER_BLACKLIST.Add(ExtensionHeaderNames.STREAM_ID.Text(), EMPTY_STRING);
        HTTP_TO_HTTP2_HEADER_BLACKLIST.Add(ExtensionHeaderNames.SCHEME.Text(), EMPTY_STRING);
        HTTP_TO_HTTP2_HEADER_BLACKLIST.Add(ExtensionHeaderNames.PATH.Text(), EMPTY_STRING);
        HTTP_TO_HTTP2_HEADER_BLACKLIST.Add(ExtensionHeaderNames.PROTOCOL.Text(), EMPTY_STRING);

        foreach (ExtensionHeaderNames name in ExtensionHeaderNames.Values())
        {
            HTTP2_TO_HTTP_HEADER_BLACKLIST.Add(name.Text(), EMPTY_STRING);
        }
    }

    /**
     * This will be the method used for {@link HttpRequest} objects generated out of the HTTP message flow defined in <a
     * href="https://tools.ietf.org/html/rfc7540#section-8.1">[RFC 7540], Section 8.1</a>
     */
    public static readonly HttpMethod OUT_OF_MESSAGE_SEQUENCE_METHOD = HttpMethod.OPTIONS;

    /**
     * This will be the path used for {@link HttpRequest} objects generated out of the HTTP message flow defined in <a
     * href="https://tools.ietf.org/html/rfc7540#section-8.1">[RFC 7540], Section 8.1</a>
     */
    public const string OUT_OF_MESSAGE_SEQUENCE_PATH = "";

    /**
     * This will be the status code used for {@link HttpResponse} objects generated out of the HTTP message flow defined
     * in <a href="https://tools.ietf.org/html/rfc7540#section-8.1">[RFC 7540], Section 8.1</a>
     */
    public static readonly HttpResponseStatus OUT_OF_MESSAGE_SEQUENCE_RETURN_CODE = HttpResponseStatus.OK;

    /**
     * <a href="https://tools.ietf.org/html/rfc7540#section-8.1.2.3">[RFC 7540], 8.1.2.3</a> states the path must not
     * be empty, and instead should be {@code /}.
     */
    private static readonly AsciiString EMPTY_REQUEST_PATH = AsciiString.Cached("/");

    /**
     * Provides the HTTP header extensions used to carry HTTP/2 information in HTTP objects
     */
    // CLR: the Java enum with a constructor, a field and a method is a sealed class with static readonly instances, as
    // codec-base Base64Dialect and codec-http HttpStatusClass (docs/codec-base-porting.md, docs/codec-http-porting.md);
    // the implicit values() is Values() and ToString() returns the constant name.
    public sealed class ExtensionHeaderNames
    {
        /**
         * HTTP extension header which will identify the stream id from the HTTP/2 event(s) responsible for
         * generating an {@code HttpObject}
         * <p>
         * {@code "x-http2-stream-id"}
         */
        public static readonly ExtensionHeaderNames STREAM_ID =
            new ExtensionHeaderNames(nameof(STREAM_ID), "x-http2-stream-id");
        /**
         * HTTP extension header which will identify the scheme pseudo header from the HTTP/2 event(s) responsible for
         * generating an {@code HttpObject}
         * <p>
         * {@code "x-http2-scheme"}
         */
        public static readonly ExtensionHeaderNames SCHEME = new ExtensionHeaderNames(nameof(SCHEME), "x-http2-scheme");
        /**
         * HTTP extension header which will identify the path pseudo header from the HTTP/2 event(s) responsible for
         * generating an {@code HttpObject}
         * <p>
         * {@code "x-http2-path"}
         */
        public static readonly ExtensionHeaderNames PATH = new ExtensionHeaderNames(nameof(PATH), "x-http2-path");
        /**
         * HTTP extension header which will identify the stream id used to create this stream in an HTTP/2 push promise
         * frame
         * <p>
         * {@code "x-http2-stream-promise-id"}
         */
        public static readonly ExtensionHeaderNames STREAM_PROMISE_ID =
            new ExtensionHeaderNames(nameof(STREAM_PROMISE_ID), "x-http2-stream-promise-id");
        /**
         * HTTP extension header which will identify the stream id which this stream is dependent on. This stream will
         * be a child node of the stream id associated with this header value.
         * <p>
         * {@code "x-http2-stream-dependency-id"}
         */
        public static readonly ExtensionHeaderNames STREAM_DEPENDENCY_ID =
            new ExtensionHeaderNames(nameof(STREAM_DEPENDENCY_ID), "x-http2-stream-dependency-id");
        /**
         * HTTP extension header which will identify the weight (if non-default and the priority is not on the default
         * stream) of the associated HTTP/2 stream responsible responsible for generating an {@code HttpObject}
         * <p>
         * {@code "x-http2-stream-weight"}
         */
        public static readonly ExtensionHeaderNames STREAM_WEIGHT =
            new ExtensionHeaderNames(nameof(STREAM_WEIGHT), "x-http2-stream-weight");
        /**
         * HTTP extension header which will identify the protocol pseudo header from an Extended CONNECT
         * (<a href="https://tools.ietf.org/html/rfc8441">RFC 8441</a>) HTTP/2 event responsible for generating an
         * {@code HttpObject}
         * <p>
         * {@code "x-http2-protocol"}
         */
        public static readonly ExtensionHeaderNames PROTOCOL =
            new ExtensionHeaderNames(nameof(PROTOCOL), "x-http2-protocol");

        private readonly string name;
        private readonly AsciiString text;

        private ExtensionHeaderNames(string name, string text)
        {
            this.name = name;
            this.text = AsciiString.Cached(text);
        }

        // CLR: Java's implicit values().
        public static ExtensionHeaderNames[] Values()
        {
            return new[]
            {
                STREAM_ID, SCHEME, PATH, STREAM_PROMISE_ID, STREAM_DEPENDENCY_ID, STREAM_WEIGHT, PROTOCOL
            };
        }

        public AsciiString Text()
        {
            return text;
        }

        public override string ToString()
        {
            return name;
        }
    }

    /**
     * Apply HTTP/2 rules while translating status code to {@link HttpResponseStatus}
     *
     * @param status The status from an HTTP/2 frame
     * @return The HTTP/1.x status
     * @throws Http2Exception If there is a problem translating from HTTP/2 to HTTP/1.x
     */
    public static HttpResponseStatus ParseStatus(ICharSequence status)
    {
        HttpResponseStatus result;
        try
        {
            result = HttpResponseStatus.ParseLine(status);
            if (ReferenceEquals(result, HttpResponseStatus.SWITCHING_PROTOCOLS))
            {
                throw ConnectionError(PROTOCOL_ERROR, "Invalid HTTP/2 status code '%d'", result.Code());
            }
        }
        catch (Http2Exception)
        {
            throw;
        }
        catch (Exception t)
        {
            throw ConnectionError(PROTOCOL_ERROR, t,
                            "Unrecognized HTTP status code '%s' encountered in translation to HTTP/1.x", status);
        }
        return result;
    }

    /**
     * Create a new object to contain the response data
     *
     * @param streamId The stream associated with the response
     * @param http2Headers The initial set of HTTP/2 headers to create the response with
     * @param alloc The {@link ByteBufAllocator} to use to generate the content of the message
     * @param validateHttpHeaders <ul>
     *        <li>{@code true} to validate HTTP headers in the http-codec</li>
     *        <li>{@code false} not to validate HTTP headers in the http-codec</li>
     *        </ul>
     * @return A new response object which represents headers/data
     * @throws Http2Exception see {@link #addHttp2ToHttpHeaders(int, Http2Headers, FullHttpMessage, boolean)}
     */
    public static IFullHttpResponse ToFullHttpResponse(int streamId, IHttp2Headers http2Headers, IByteBufAllocator alloc,
                                                      bool validateHttpHeaders)
    {
        return ToFullHttpResponse(streamId, http2Headers, alloc.Buffer(), validateHttpHeaders);
    }

    /**
     * Create a new object to contain the response data
     *
     * @param streamId The stream associated with the response
     * @param http2Headers The initial set of HTTP/2 headers to create the response with
     * @param content {@link ByteBuf} content to put in {@link FullHttpResponse}
     * @param validateHttpHeaders <ul>
     *        <li>{@code true} to validate HTTP headers in the http-codec</li>
     *        <li>{@code false} not to validate HTTP headers in the http-codec</li>
     *        </ul>
     * @return A new response object which represents headers/data
     * @throws Http2Exception see {@link #addHttp2ToHttpHeaders(int, Http2Headers, FullHttpMessage, boolean)}
     */
    public static IFullHttpResponse ToFullHttpResponse(int streamId, IHttp2Headers http2Headers, ByteBuf content,
                                                      bool validateHttpHeaders)
    {
        HttpResponseStatus status = ParseStatus(http2Headers.Status());
        // HTTP/2 does not define a way to carry the version or reason phrase that is included in an
        // HTTP/1.1 status line.
        IFullHttpResponse msg = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, status, content,
                DefaultHttpHeadersFactory.HeadersFactory().WithValidation(validateHttpHeaders),
                DefaultHttpHeadersFactory.TrailersFactory().WithValidation(validateHttpHeaders));
        try
        {
            AddHttp2ToHttpHeaders(streamId, http2Headers, msg, false);
        }
        catch (Http2Exception)
        {
            msg.Release();
            throw;
        }
        catch (Exception t)
        {
            msg.Release();
            throw StreamError(streamId, PROTOCOL_ERROR, t, "HTTP/2 to HTTP/1.x headers conversion error");
        }
        return msg;
    }

    /**
     * Create a new object to contain the request data
     *
     * @param streamId The stream associated with the request
     * @param http2Headers The initial set of HTTP/2 headers to create the request with
     * @param alloc The {@link ByteBufAllocator} to use to generate the content of the message
     * @param validateHttpHeaders <ul>
     *        <li>{@code true} to validate HTTP headers in the http-codec</li>
     *        <li>{@code false} not to validate HTTP headers in the http-codec</li>
     *        </ul>
     * @return A new request object which represents headers/data
     * @throws Http2Exception see {@link #addHttp2ToHttpHeaders(int, Http2Headers, FullHttpMessage, boolean)}
     */
    public static IFullHttpRequest ToFullHttpRequest(int streamId, IHttp2Headers http2Headers, IByteBufAllocator alloc,
                                                    bool validateHttpHeaders)
    {
        return ToFullHttpRequest(streamId, http2Headers, alloc.Buffer(), validateHttpHeaders);
    }

    private static string ExtractPath(ICharSequence method, IHttp2Headers headers)
    {
        if (HttpMethod.CONNECT.AsciiName().ContentEqualsIgnoreCase(method))
        {
            // See https://tools.ietf.org/html/rfc7231#section-4.3.6
            return CheckNotNull(headers.Authority(),
                    "authority header cannot be null in the conversion to HTTP/1.x").ToString();
        }
        else
        {
            return CheckNotNull(headers.Path(),
                    "path header cannot be null in conversion to HTTP/1.x").ToString();
        }
    }

    /**
     * Create a new object to contain the request data
     *
     * @param streamId The stream associated with the request
     * @param http2Headers The initial set of HTTP/2 headers to create the request with
     * @param content {@link ByteBuf} content to put in {@link FullHttpRequest}
     * @param validateHttpHeaders <ul>
     *        <li>{@code true} to validate HTTP headers in the http-codec</li>
     *        <li>{@code false} not to validate HTTP headers in the http-codec</li>
     *        </ul>
     * @return A new request object which represents headers/data
     * @throws Http2Exception see {@link #addHttp2ToHttpHeaders(int, Http2Headers, FullHttpMessage, boolean)}
     */
    public static IFullHttpRequest ToFullHttpRequest(int streamId, IHttp2Headers http2Headers, ByteBuf content,
                                                bool validateHttpHeaders)
    {
        // HTTP/2 does not define a way to carry the version identifier that is included in the HTTP/1.1 request line.
        ICharSequence method = CheckNotNull(http2Headers.Method(),
                "method header cannot be null in conversion to HTTP/1.x");
        ICharSequence path = new StringCharSequence(ExtractPath(method, http2Headers));
        IFullHttpRequest msg = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.ValueOf(method
                        .ToString()), path.ToString(), content,
                DefaultHttpHeadersFactory.HeadersFactory().WithValidation(validateHttpHeaders),
                DefaultHttpHeadersFactory.TrailersFactory().WithValidation(validateHttpHeaders));
        try
        {
            AddHttp2ToHttpHeaders(streamId, http2Headers, msg, false);
        }
        catch (Http2Exception)
        {
            msg.Release();
            throw;
        }
        catch (Exception t)
        {
            msg.Release();
            throw StreamError(streamId, PROTOCOL_ERROR, t, "HTTP/2 to HTTP/1.x headers conversion error");
        }
        return msg;
    }

    /**
     * Create a new object to contain the request data.
     *
     * @param streamId The stream associated with the request
     * @param http2Headers The initial set of HTTP/2 headers to create the request with
     * @param validateHttpHeaders <ul>
     *        <li>{@code true} to validate HTTP headers in the http-codec</li>
     *        <li>{@code false} not to validate HTTP headers in the http-codec</li>
     *        </ul>
     * @return A new request object which represents headers for a chunked request
     * @throws Http2Exception see {@link #addHttp2ToHttpHeaders(int, Http2Headers, FullHttpMessage, boolean)}
     */
    public static IHttpRequest ToHttpRequest(int streamId, IHttp2Headers http2Headers, bool validateHttpHeaders)
    {
        // HTTP/2 does not define a way to carry the version identifier that is included in the HTTP/1.1 request line.
        ICharSequence method = CheckNotNull(http2Headers.Method(),
                "method header cannot be null in conversion to HTTP/1.x");
        ICharSequence path = new StringCharSequence(ExtractPath(method, http2Headers));
        IHttpRequest msg = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.ValueOf(method.ToString()),
                path.ToString(), DefaultHttpHeadersFactory.HeadersFactory().WithValidation(validateHttpHeaders));
        try
        {
            AddHttp2ToHttpHeaders(streamId, http2Headers, msg.Headers(), msg.ProtocolVersion(), false, true);
        }
        catch (Http2Exception)
        {
            throw;
        }
        catch (Exception t)
        {
            throw StreamError(streamId, PROTOCOL_ERROR, t, "HTTP/2 to HTTP/1.x headers conversion error");
        }
        return msg;
    }

    /**
     * Create a new object to contain the response data.
     *
     * @param streamId The stream associated with the response
     * @param http2Headers The initial set of HTTP/2 headers to create the response with
     * @param validateHttpHeaders <ul>
     *        <li>{@code true} to validate HTTP headers in the http-codec</li>
     *        <li>{@code false} not to validate HTTP headers in the http-codec</li>
     *        </ul>
     * @return A new response object which represents headers for a chunked response
     * @throws Http2Exception see {@link #addHttp2ToHttpHeaders(int, Http2Headers,
     *         HttpHeaders, HttpVersion, boolean, boolean)}
     */
    public static IHttpResponse ToHttpResponse(int streamId,
                                              IHttp2Headers http2Headers,
                                              bool validateHttpHeaders)
    {
        HttpResponseStatus status = ParseStatus(http2Headers.Status());
        // HTTP/2 does not define a way to carry the version or reason phrase that is included in an
        // HTTP/1.1 status line.
        IHttpResponse msg = new DefaultHttpResponse(HttpVersion.HTTP_1_1, status,
                DefaultHttpHeadersFactory.HeadersFactory().WithValidation(validateHttpHeaders));
        try
        {
            AddHttp2ToHttpHeaders(streamId, http2Headers, msg.Headers(), msg.ProtocolVersion(), false, false);
        }
        catch (Http2Exception)
        {
            throw;
        }
        catch (Exception t)
        {
            throw StreamError(streamId, PROTOCOL_ERROR, t, "HTTP/2 to HTTP/1.x headers conversion error");
        }
        return msg;
    }

    /**
     * Translate and add HTTP/2 headers to HTTP/1.x headers.
     *
     * @param streamId The stream associated with {@code sourceHeaders}.
     * @param inputHeaders The HTTP/2 headers to convert.
     * @param destinationMessage The object which will contain the resulting HTTP/1.x headers.
     * @param addToTrailer {@code true} to add to trailing headers. {@code false} to add to initial headers.
     * @throws Http2Exception If not all HTTP/2 headers can be translated to HTTP/1.x.
     * @see #addHttp2ToHttpHeaders(int, Http2Headers, HttpHeaders, HttpVersion, boolean, boolean)
     */
    public static void AddHttp2ToHttpHeaders(int streamId, IHttp2Headers inputHeaders,
                    IFullHttpMessage destinationMessage, bool addToTrailer)
    {
        AddHttp2ToHttpHeaders(streamId, inputHeaders,
                addToTrailer ? destinationMessage.TrailingHeaders() : destinationMessage.Headers(),
                destinationMessage.ProtocolVersion(), addToTrailer, destinationMessage is IHttpRequest);
    }

    /**
     * Translate and add HTTP/2 headers to HTTP/1.x headers.
     *
     * @param streamId The stream associated with {@code sourceHeaders}.
     * @param inputHeaders The HTTP/2 headers to convert.
     * @param outputHeaders The object which will contain the resulting HTTP/1.x headers..
     * @param httpVersion What HTTP/1.x version {@code outputHeaders} should be treated as when doing the conversion.
     * @param isTrailer {@code true} if {@code outputHeaders} should be treated as trailing headers.
     * {@code false} otherwise.
     * @param isRequest {@code true} if the {@code outputHeaders} will be used in a request message.
     * {@code false} for response message.
     * @throws Http2Exception If not all HTTP/2 headers can be translated to HTTP/1.x.
     */
    public static void AddHttp2ToHttpHeaders(int streamId, IHttp2Headers inputHeaders, HttpHeaders outputHeaders,
            HttpVersion httpVersion, bool isTrailer, bool isRequest)
    {
        // Extended CONNECT (RFC 8441) changes the semantics of a CONNECT request: the server must not treat
        // ':authority' as an ordinary tunnel target the way it would for a regular CONNECT request. Preserve the
        // ':protocol' and ':path' pseudo-headers as extension headers so that code operating on the converted
        // HTTP/1.x object can still distinguish an Extended CONNECT request from a regular CONNECT request.
        bool isConnect = isRequest && HttpMethod.CONNECT.AsciiName().ContentEqualsIgnoreCase(inputHeaders.Method());
        Http2ToHttpHeaderTranslator translator =
                new Http2ToHttpHeaderTranslator(streamId, outputHeaders, isRequest, isConnect);
        try
        {
            translator.TranslateHeaders(inputHeaders);
        }
        catch (Http2Exception)
        {
            throw;
        }
        catch (Exception t)
        {
            throw StreamError(streamId, PROTOCOL_ERROR, t, "HTTP/2 to HTTP/1.x headers conversion error");
        }

        outputHeaders.Remove(HttpHeaderNames.TRANSFER_ENCODING);
        outputHeaders.Remove(HttpHeaderNames.TRAILER);
        if (!isTrailer)
        {
            outputHeaders.SetInt(ExtensionHeaderNames.STREAM_ID.Text(), streamId);
            HttpUtil.SetKeepAlive(outputHeaders, httpVersion, true);
        }
    }

    /**
     * Converts the given HTTP/1.x headers into HTTP/2 headers.
     * The following headers are only used if they can not be found in from the {@code HOST} header or the
     * {@code Request-Line} as defined by <a href="https://tools.ietf.org/html/rfc7230">rfc7230</a>
     * <ul>
     * <li>{@link ExtensionHeaderNames#SCHEME}</li>
     * </ul>
     * {@link ExtensionHeaderNames#PATH} is ignored and instead extracted from the {@code Request-Line}.
     */
    public static IHttp2Headers ToHttp2Headers(IHttpMessage @in, bool validateHeaders)
    {
        HttpHeaders inHeaders = @in.Headers();
        IHttp2Headers @out = new DefaultHttp2Headers(validateHeaders, inHeaders.Size());
        if (@in is IHttpRequest)
        {
            IHttpRequest request = (IHttpRequest) @in;
            if (request.Method().Equals(HttpMethod.CONNECT))
            {
                // https://datatracker.ietf.org/doc/html/rfc9112#section-3.2.3 defines the HTTP/1 CONNECT
                // request-target as authority-form (host:port), which is the only valid request-target for
                // CONNECT. Use it directly for :authority, ignoring any (potentially conflicting) Host header,
                // and per https://datatracker.ietf.org/doc/html/rfc9113#section-8.5 omit :scheme and :path.

                string authorityForm = request.Uri();
                if (authorityForm != null)
                {
                    // CONNECT uses a special form of request target, unique to this method, consisting of only the
                    // host and port number of the tunnel destination, separated by a colon per
                    // https://www.rfc-editor.org/info/rfc9110/#name-connect
                    if (authorityForm.Length == 0 || authorityForm.IndexOf('@') >= 0 || authorityForm.IndexOf('/') >= 0)
                    {
                        throw new ArgumentException("Invalid CONNECT request target: " + authorityForm);
                    }
                    @out.Authority(new AsciiString(authorityForm));
                }
            }
            else
            {
                string host = inHeaders.GetAsString(HttpHeaderNames.HOST);
                if (HttpUtil.IsOriginForm(request.Uri()) || HttpUtil.IsAsteriskForm(request.Uri()))
                {
                    @out.Path(new AsciiString(request.Uri()));
                    SetHttp2Scheme(inHeaders, @out);
                }
                else
                {
                    string requestTarget = request.Uri();
                    @out.Path(ToHttp2Path(requestTarget));
                    if (HasSchemeAndAuthority(requestTarget))
                    {
                        string requestTargetUri = CreateUri(Http2PathlessRequestTarget(requestTarget));
                        // The absolute-form request-target authority is authoritative and takes precedence over
                        // a (potentially conflicting) HOST header, per RFC 9112 section 3.2 and RFC 9113 section 8.3.1.
                        string requestTargetAuthority = UriAuthority(requestTargetUri);
                        host = StringUtil.IsNullOrEmpty(requestTargetAuthority) ? host : requestTargetAuthority;
                        SetHttp2Scheme(inHeaders, requestTargetUri, @out);
                    }
                    else
                    {
                        int schemeEnd = SchemeEnd(requestTarget);
                        if (schemeEnd != -1)
                        {
                            SetHttp2Scheme(inHeaders, requestTarget.Substring(0, schemeEnd), -1, @out);
                        }
                        else
                        {
                            SetHttp2Scheme(inHeaders, @out);
                        }
                    }
                }
                SetHttp2Authority(host, @out);
            }
            @out.Method(request.Method().AsciiName());
        }
        else if (@in is IHttpResponse)
        {
            IHttpResponse response = (IHttpResponse) @in;
            @out.Status(response.Status().CodeAsText());
        }

        // Add the HTTP headers which have not been consumed above
        ToHttp2Headers(inHeaders, @out);
        return @out;
    }

    public static IHttp2Headers ToHttp2Headers(HttpHeaders inHeaders, bool validateHeaders)
    {
        if (inHeaders.IsEmpty())
        {
            return EmptyHttp2Headers.INSTANCE;
        }

        IHttp2Headers @out = new DefaultHttp2Headers(validateHeaders, inHeaders.Size());
        ToHttp2Headers(inHeaders, @out);
        return @out;
    }

    private static CharSequenceMap<AsciiString> ToLowercaseMap(IIterator<ICharSequence> valuesIter,
                                                               int arraySizeHint)
    {
        UnsupportedValueConverter<AsciiString> valueConverter = UnsupportedValueConverter<AsciiString>.Instance();
        CharSequenceMap<AsciiString> result = new CharSequenceMap<AsciiString>(true, valueConverter, arraySizeHint);

        while (valuesIter.HasNext())
        {
            AsciiString lowerCased = AsciiString.Of(valuesIter.Next()).ToLowerCase();
            try
            {
                int index = lowerCased.ForEachByte(FIND_COMMA);
                if (index != -1)
                {
                    int start = 0;
                    do
                    {
                        result.Add(lowerCased.SubSequence(start, index, false).Trim(), EMPTY_STRING);
                        start = index + 1;
                    } while (start < lowerCased.Length() &&
                             (index = lowerCased.ForEachByte(start, lowerCased.Length() - start, FIND_COMMA)) != -1);
                    result.Add(lowerCased.SubSequence(start, lowerCased.Length(), false).Trim(), EMPTY_STRING);
                }
                else
                {
                    result.Add(lowerCased.Trim(), EMPTY_STRING);
                }
            }
            catch (Exception e)
            {
                // This is not expect to happen because FIND_COMMA never throws but must be caught
                // because of the ByteProcessor interface.
                throw new InvalidOperationException(ThrowableUtil.ThrowableToString(e), e);
            }
        }
        return result;
    }

    /**
     * Filter the {@link HttpHeaderNames#TE} header according to the
     * <a href="https://tools.ietf.org/html/rfc7540#section-8.1.2.2">special rules in the HTTP/2 RFC</a>.
     * @param entry An entry whose name is {@link HttpHeaderNames#TE}.
     * @param out the resulting HTTP/2 headers.
     */
    private static void ToHttp2HeadersFilterTE(IEntry<ICharSequence, ICharSequence> entry,
                                               IHttp2Headers @out)
    {
        if (AsciiString.IndexOf(entry.GetValue(), ',', 0) == -1)
        {
            if (ContentEqualsIgnoreCase(AsciiString.Trim(entry.GetValue()), HttpHeaderValues.TRAILERS))
            {
                @out.Add(TE, HttpHeaderValues.TRAILERS);
            }
        }
        else
        {
            List<string> teValues = StringUtil.UnescapeCsvFields(entry.GetValue().ToString());
            foreach (string teValue in teValues)
            {
                if (ContentEqualsIgnoreCase(AsciiString.Trim(new StringCharSequence(teValue)), HttpHeaderValues.TRAILERS))
                {
                    @out.Add(TE, HttpHeaderValues.TRAILERS);
                    break;
                }
            }
        }
    }

    public static void ToHttp2Headers(HttpHeaders inHeaders, IHttp2Headers @out)
    {
        IIterator<IEntry<ICharSequence, ICharSequence>> iter = inHeaders.IteratorCharSequence();
        // Choose 8 as a default size because it is unlikely we will see more than 4 Connection headers values, but
        // still allowing for "enough" space in the map to reduce the chance of hash code collision.
        CharSequenceMap<AsciiString> connectionBlacklist =
            ToLowercaseMap(inHeaders.ValueCharSequenceIterator(CONNECTION), 8);
        while (iter.HasNext())
        {
            IEntry<ICharSequence, ICharSequence> entry = iter.Next();
            AsciiString aName = AsciiString.Of(entry.GetKey()).ToLowerCase();
            if (!HTTP_TO_HTTP2_HEADER_BLACKLIST.Contains(aName) && !connectionBlacklist.Contains(aName))
            {
                // https://tools.ietf.org/html/rfc7540#section-8.1.2.2 makes a special exception for TE
                if (aName.ContentEqualsIgnoreCase(TE))
                {
                    ToHttp2HeadersFilterTE(entry, @out);
                }
                else if (aName.ContentEqualsIgnoreCase(COOKIE))
                {
                    ICharSequence valueCs = entry.GetValue();
                    // validate
                    bool invalid = false;
                    for (int i = 0; i < valueCs.Length(); i++)
                    {
                        char c = valueCs.CharAt(i);
                        if (c == ';')
                        {
                            if (i + 1 >= valueCs.Length() || valueCs.CharAt(i + 1) != ' ')
                            {
                                // semicolon not followed by space. invalid, don't split
                                invalid = true;
                                break;
                            }
                            i++; // skip space
                        }
                        else if (c > 255)
                        {
                            // not ascii, don't split
                            invalid = true;
                            break;
                        }
                    }

                    if (invalid)
                    {
                        @out.Add(COOKIE, valueCs);
                    }
                    else
                    {
                        SplitValidCookieHeader(@out, valueCs);
                    }
                }
                else
                {
                    @out.Add(aName, entry.GetValue());
                }
            }
        }
    }

    private static void SplitValidCookieHeader(IHttp2Headers @out, ICharSequence valueCs)
    {
        try
        {
            AsciiString value = AsciiString.Of(valueCs);
            // split up cookies to allow for better compression
            // https://tools.ietf.org/html/rfc7540#section-8.1.2.5
            int index = value.ForEachByte(FIND_SEMI_COLON);
            if (index != -1)
            {
                int start = 0;
                do
                {
                    @out.Add(COOKIE, value.SubSequence(start, index, false));
                    System.Diagnostics.Debug.Assert(index + 1 < value.Length());
                    System.Diagnostics.Debug.Assert(value.CharAt(index + 1) == ' ');
                    // skip 2 characters "; " (see https://tools.ietf.org/html/rfc6265#section-4.2.1)
                    start = index + 2;
                } while (start < value.Length() &&
                        (index = value.ForEachByte(start, value.Length() - start, FIND_SEMI_COLON)) != -1);
                System.Diagnostics.Debug.Assert(start < value.Length());
                @out.Add(COOKIE, value.SubSequence(start, value.Length(), false));
            }
            else
            {
                @out.Add(COOKIE, value);
            }
        }
        catch (Exception e)
        {
            // This is not expect to happen because FIND_SEMI_COLON never throws but must be caught
            // because of the ByteProcessor interface.
            throw new InvalidOperationException(ThrowableUtil.ThrowableToString(e), e);
        }
    }

    /**
     * Generate an HTTP/2 {code :path} from a request-target in accordance with
     * <a href="https://tools.ietf.org/html/rfc7230#section-5.3">rfc7230, 5.3</a>.
     */
    private static AsciiString ToHttp2Path(string uri)
    {
        string path = DropEmptyFragment(ParsePath(uri));
        string query = ParseQuery(uri);
        if (StringUtil.IsNullOrEmpty(query))
        {
            return path.Length == 0 ? EMPTY_REQUEST_PATH : new AsciiString(path);
        }
        StringBuilder pathBuilder = new StringBuilder(path.Length + query.Length + 1);
        pathBuilder.Append(path);
        AppendQuery(pathBuilder, query);
        return new AsciiString(pathBuilder.ToString());
    }

    /**
     * Extract the path out of the request-target. Based on Vert.x' HttpUtils.parsePath logic.
     */
    private static string ParsePath(string uri)
    {
        if (uri.Length == 0)
        {
            return StringUtil.EMPTY_STRING;
        }
        int i;
        if (uri[0] == '/')
        {
            i = 0;
        }
        else
        {
            i = uri.IndexOf("://", StringComparison.Ordinal);
            // Netty change: validate the scheme before treating :// as authority syntax.
            if (!IsValidScheme(uri, i))
            {
                i = 0;
            }
            else
            {
                int authorityStart = i + 3;
                // Netty change: only accept '/' before query/fragment as path start.
                int queryOrFragmentStart = QueryOrFragmentStart(uri, authorityStart);
                i = uri.IndexOf('/', authorityStart);
                if (i == -1 || (queryOrFragmentStart != -1 && queryOrFragmentStart < i))
                {
                    // contains no /
                    return "/";
                }
            }
        }

        int queryStart = uri.IndexOf('?', i);
        if (queryStart == -1)
        {
            queryStart = uri.Length;
            if (i == 0)
            {
                return uri;
            }
        }
        return uri.Substring(i, queryStart - i);
    }

    /**
     * Extract the query out of a request-target or returns {@code null} if no query was found.
     */
    private static string ParseQuery(string uri)
    {
        int i = uri.IndexOf('?');
        if (i == -1)
        {
            return null;
        }
        else
        {
            return uri.Substring(i + 1);
        }
    }

    private static string DropEmptyFragment(string path)
    {
        // Netty change: old URI-based conversion dropped an empty fragment delimiter.
        return path.EndsWith('#') ? path.Substring(0, path.Length - 1) : path;
    }

    private static void AppendQuery(StringBuilder pathBuilder, string query)
    {
        int fragmentStart = query.IndexOf('#');
        if (fragmentStart == 0)
        {
            // Netty change: old URI-based conversion skipped an empty query before a fragment.
            pathBuilder.Append(query);
        }
        else if (fragmentStart == query.Length - 1)
        {
            // Netty change: old URI-based conversion dropped an empty fragment delimiter after a query.
            pathBuilder.Append('?').Append(query, 0, fragmentStart);
        }
        else
        {
            pathBuilder.Append('?').Append(query);
        }
    }

    internal static int QueryOrFragmentStart(string uri, int searchStart)
    {
        int queryStart = uri.IndexOf('?', searchStart);
        int fragmentStart = uri.IndexOf('#', searchStart);
        return queryStart == -1 ? fragmentStart :
                fragmentStart == -1 ? queryStart : Math.Min(queryStart, fragmentStart);
    }

    // Netty addition: detect authority for HTTP/2 :scheme/:authority extraction.
    internal static bool HasSchemeAndAuthority(string requestTarget)
    {
        int schemeEnd = requestTarget.IndexOf("://", StringComparison.Ordinal);
        return IsValidScheme(requestTarget, schemeEnd);
    }

    private static int SchemeEnd(string requestTarget)
    {
        int schemeEnd = requestTarget.IndexOf(':');
        return IsValidScheme(requestTarget, schemeEnd) ? schemeEnd : -1;
    }

    // Netty addition: prepare only scheme://authority for URI validation.
    private static string Http2PathlessRequestTarget(string requestTarget)
    {
        int schemeEnd = requestTarget.IndexOf("://", StringComparison.Ordinal);
        int authorityStart = schemeEnd + 3;
        // Netty addition: strip before path/query/fragment; Vert.x parsePath does not validate authority.
        int pathStart = requestTarget.IndexOf('/', authorityStart);
        int delimiter = QueryOrFragmentStart(requestTarget, authorityStart);
        if (pathStart != -1 && (delimiter == -1 || pathStart < delimiter))
        {
            delimiter = pathStart;
        }
        if (delimiter == -1)
        {
            return requestTarget;
        }
        return delimiter == authorityStart ? requestTarget.Substring(0, delimiter + 1) :
                requestTarget.Substring(0, delimiter);
    }

    // Netty addition: validate the text before :// as a scheme.
    internal static bool IsValidScheme(string uri, int schemeEnd)
    {
        if (schemeEnd <= 0)
        {
            return false;
        }
        char first = uri[0];
        if (!IsAlpha(first))
        {
            return false;
        }
        for (int i = 1; i < schemeEnd; ++i)
        {
            char c = uri[i];
            if (!IsAlpha(c) && (c < '0' || c > '9') && c != '+' && c != '-' && c != '.')
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsAlpha(char c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    }

    // package-private for testing only
    internal static void SetHttp2Authority(string authority, IHttp2Headers @out)
    {
        // The authority MUST NOT include the deprecated "userinfo" subcomponent
        if (authority != null)
        {
            if (authority.Length == 0)
            {
                @out.Authority(EMPTY_STRING);
            }
            else
            {
                int start = authority.IndexOf('@') + 1;
                int length = authority.Length - start;
                if (length == 0)
                {
                    throw new ArgumentException("authority: " + authority);
                }
                @out.Authority(new AsciiString(authority, start, length));
            }
        }
    }

    private static void SetHttp2Scheme(HttpHeaders @in, IHttp2Headers @out)
    {
        SetHttp2Scheme(@in, CreateUri(""), @out);
    }

    private static void SetHttp2Scheme(HttpHeaders @in, string uri, IHttp2Headers @out)
    {
        SetHttp2Scheme(@in, UriScheme(uri), UriPort(uri), @out);
    }

    private static void SetHttp2Scheme(HttpHeaders @in, string scheme, int port, IHttp2Headers @out)
    {
        if (!StringUtil.IsNullOrEmpty(scheme))
        {
            @out.Scheme(new AsciiString(scheme));
            return;
        }

        // Consume the Scheme extension header if present
        string cValue = @in.Get(ExtensionHeaderNames.SCHEME.Text());
        if (cValue != null)
        {
            @out.Scheme(AsciiString.Of(new StringCharSequence(cValue)));
            return;
        }

        if (port == HttpScheme.HTTPS.Port())
        {
            @out.Scheme(HttpScheme.HTTPS.Name());
        }
        else if (port == HttpScheme.HTTP.Port())
        {
            @out.Scheme(HttpScheme.HTTP.Name());
        }
        else
        {
            throw new ArgumentException(":scheme must be specified. " +
                    "see https://tools.ietf.org/html/rfc7540#section-8.1.2.3");
        }
    }

    // CLR: java.net.URI has no BCL counterpart with the same results: System.Uri canonicalizes (it lower-cases the host
    // and leaves the user info and a default port out of Authority) and validates with other rules
    // (docs/codec-http-porting.md, Shared CLR adaptations: "System.Uri canonicalizes"). This class passes URI.create
    // only "" and the "scheme://authority" text of Http2PathlessRequestTarget (optionally followed by one '/', '?' or
    // '#'), and reads getScheme(), getPort() and getAuthority(), so the private helpers below keep the parts of the
    // OpenJDK parser that apply to these forms
    // (https://raw.githubusercontent.com/openjdk/jdk17u/master/src/java.base/share/classes/java/net/URI.java):
    // parseHierarchical allows an empty authority before a path, query or fragment (getAuthority() is then null) and
    // fails at the end of the input; parseAuthority accepts an authority whose characters are all reg_name characters
    // (unreserved, %-escapes, visible non-ASCII characters and "$,;:@&=+") as registry-based when it does not parse as
    // a server ([userinfo@]host[:port]); an authority with '[' or ']' must parse as a server with an IPv6 literal
    // (System.Net.IPAddress.TryParse checks the literal), and any other character, a malformed %-escape or a failed
    // server parse of such an authority throws (URISyntaxException, which URI.create wraps in IllegalArgumentException,
    // here ArgumentException). getPort() is the port of a server-based authority, -1 if undefined. getAuthority()
    // decodes %-escapes as UTF-8 outside square brackets (decode(s) with ignorePercentInBrackets).
    private static string CreateUri(string str)
    {
        if (str.Length == 0)
        {
            return str;
        }
        int schemeEnd = str.IndexOf("://", StringComparison.Ordinal);
        if (!IsValidScheme(str, schemeEnd))
        {
            throw new ArgumentException("Illegal character in scheme name at index 0: " + str);
        }
        int p = schemeEnd + 3;
        int q = p;
        while (q < str.Length && str[q] != '/' && str[q] != '?' && str[q] != '#')
        {
            q++;
        }
        if (q > p)
        {
            ParseUriAuthority(str, str.Substring(p, q - p), p, out _);
        }
        else if (q >= str.Length)
        {
            throw new ArgumentException("Expected authority at index " +
                    p.ToString(CultureInfo.InvariantCulture) + ": " + str);
        }
        return str;
    }

    // CLR: URI.getScheme() of a URI created by CreateUri.
    private static string UriScheme(string uri)
    {
        int schemeEnd = uri.IndexOf("://", StringComparison.Ordinal);
        return schemeEnd > 0 ? uri.Substring(0, schemeEnd) : null;
    }

    // CLR: URI.getPort() of a URI created by CreateUri.
    private static int UriPort(string uri)
    {
        string authority = RawUriAuthority(uri);
        if (authority == null)
        {
            return -1;
        }
        ParseUriAuthority(uri, authority, 0, out int port);
        return port;
    }

    // CLR: URI.getAuthority() of a URI created by CreateUri.
    private static string UriAuthority(string uri)
    {
        string authority = RawUriAuthority(uri);
        return authority == null ? null : DecodeUri(authority);
    }

    private static string RawUriAuthority(string uri)
    {
        int schemeEnd = uri.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd <= 0)
        {
            return null;
        }
        int p = schemeEnd + 3;
        int q = p;
        while (q < uri.Length && uri[q] != '/' && uri[q] != '?' && uri[q] != '#')
        {
            q++;
        }
        return q > p ? uri.Substring(p, q - p) : null;
    }

    // CLR: Parser.parseAuthority/parseServer of java.net.URI (see CreateUri); returns the port of a server-based
    // authority or -1.
    private static void ParseUriAuthority(string input, string authority, int offset, out int port)
    {
        port = -1;
        bool serverChars;
        if (authority.IndexOf(']') >= 0)
        {
            serverChars = ScanUri(input, authority, offset, true, true) == authority.Length;
        }
        else
        {
            serverChars = ScanUri(input, authority, offset, true, false) == authority.Length;
        }
        bool regChars = ScanUri(input, authority, offset, false, false) == authority.Length;
        if (regChars && !serverChars)
        {
            return;
        }
        if (serverChars)
        {
            int serverPort = ParseUriServer(authority);
            if (serverPort != -2)
            {
                port = serverPort;
                return;
            }
            if (regChars)
            {
                return;
            }
            throw new ArgumentException("Malformed authority: " + input);
        }
        if (!regChars)
        {
            throw new ArgumentException("Illegal character in authority: " + input);
        }
    }

    // CLR: scan(p, n, L_SERVER[_PERCENT] or L_REG_NAME): the index of the first character outside the set; a
    // malformed %-escape throws (scanEscape), a visible non-ASCII character is accepted.
    private static int ScanUri(string input, string s, int offset, bool server, bool serverPercent)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c < 128)
            {
                bool allowed = c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' ||
                               "-_.!~*'()$,;:@&=+".IndexOf(c) >= 0 || server && (c == '[' || c == ']');
                if (allowed)
                {
                    continue;
                }
                if (c == '%')
                {
                    if (serverPercent)
                    {
                        continue;
                    }
                    if (i + 2 < s.Length && Uri.IsHexDigit(s[i + 1]) && Uri.IsHexDigit(s[i + 2]))
                    {
                        i += 2;
                        continue;
                    }
                    throw new ArgumentException("Malformed escape pair at index " +
                            (offset + i).ToString(CultureInfo.InvariantCulture) + ": " + input);
                }
                return i;
            }
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                return i;
            }
        }
        return s.Length;
    }

    // CLR: parseServer: [userinfo "@"] host [":" port]; returns the port (-1 if undefined) or -2 if the authority does
    // not parse as a server.
    private static int ParseUriServer(string s)
    {
        int p = 0;
        int at = s.IndexOf('@');
        if (at >= 0)
        {
            if (s.IndexOf('[', 0, at) >= 0 || s.IndexOf(']', 0, at) >= 0)
            {
                return -2;
            }
            p = at + 1;
        }
        if (p < s.Length && s[p] == '[')
        {
            int close = s.IndexOf(']', p + 1);
            if (close <= p + 1 || !System.Net.IPAddress.TryParse(s.Substring(p + 1, close - p - 1), out System.Net.IPAddress address) ||
                address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                return -2;
            }
            p = close + 1;
        }
        else
        {
            int q = ParseUriIPv4Address(s, p);
            if (q <= p)
            {
                q = ParseUriHostname(s, p);
            }
            if (q < 0)
            {
                return -2;
            }
            p = q;
        }
        int port = -1;
        if (p < s.Length && s[p] == ':')
        {
            p++;
            int q = p;
            while (q < s.Length && s[q] >= '0' && s[q] <= '9')
            {
                q++;
            }
            if (q > p)
            {
                if (!int.TryParse(s.AsSpan(p, q - p), NumberStyles.None, CultureInfo.InvariantCulture, out port))
                {
                    return -2;
                }
                p = q;
            }
        }
        return p < s.Length ? -2 : port;
    }

    // CLR: parseIPv4Address: the end of a dotted-quad IPv4 address followed by the end or ':', otherwise -1.
    private static int ParseUriIPv4Address(string s, int start)
    {
        int p = start;
        for (int octet = 0; octet < 4; octet++)
        {
            if (octet > 0)
            {
                if (p >= s.Length || s[p] != '.')
                {
                    return -1;
                }
                p++;
            }
            int q = p;
            while (q < s.Length && q - p < 3 && s[q] >= '0' && s[q] <= '9')
            {
                q++;
            }
            if (q == p || int.Parse(s.AsSpan(p, q - p), NumberStyles.None, CultureInfo.InvariantCulture) > 255)
            {
                return -1;
            }
            p = q;
        }
        return p == s.Length || s[p] == ':' ? p : -1;
    }

    // CLR: parseHostname: labels of alphanumerics and inner dashes separated by dots, the last label starting with a
    // letter when there is more than one; the end of the host name, or -1 where Java fails.
    private static int ParseUriHostname(string s, int start)
    {
        int p = start;
        int l = -1;
        while (p < s.Length)
        {
            int q = p;
            while (q < s.Length && char.IsAsciiLetterOrDigit(s[q]))
            {
                q++;
            }
            if (q <= p)
            {
                break;
            }
            l = p;
            p = q;
            while (q < s.Length && (char.IsAsciiLetterOrDigit(s[q]) || s[q] == '-'))
            {
                q++;
            }
            if (q > p)
            {
                if (s[q - 1] == '-')
                {
                    return -1;
                }
                p = q;
            }
            if (p < s.Length && s[p] == '.')
            {
                p++;
            }
            else
            {
                break;
            }
        }
        if (p < s.Length && s[p] != ':')
        {
            return -1;
        }
        if (l < 0)
        {
            return -1;
        }
        if (l > start && !char.IsAsciiLetter(s[l]))
        {
            return -1;
        }
        return p;
    }

    // CLR: decode(s, true): %-escapes outside square brackets are decoded as UTF-8 (malformed input is replaced, as
    // Java's decoder does with CodingErrorAction.REPLACE).
    private static string DecodeUri(string s)
    {
        if (s.IndexOf('%') < 0)
        {
            return s;
        }
        StringBuilder sb = new StringBuilder(s.Length);
        List<byte> bytes = new List<byte>();
        bool betweenBrackets = false;
        for (int i = 0; i < s.Length;)
        {
            char c = s[i];
            if (c == '[')
            {
                betweenBrackets = true;
            }
            else if (betweenBrackets && c == ']')
            {
                betweenBrackets = false;
            }
            if (c != '%' || betweenBrackets)
            {
                sb.Append(c);
                i++;
                continue;
            }
            bytes.Clear();
            while (i < s.Length && s[i] == '%')
            {
                bytes.Add((byte) int.Parse(s.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                i += 3;
            }
            sb.Append(Encoding.UTF8.GetString(bytes.ToArray()));
        }
        return sb.ToString();
    }

    /**
     * Utility which translates HTTP/2 headers to HTTP/1 headers.
     */
    // CLR: the static initializer is the static constructor; Iterable<Entry<CharSequence, CharSequence>> is
    // IEnumerable<IEntry<ICharSequence, ICharSequence>>, which IHttp2Headers implements.
    private sealed class Http2ToHttpHeaderTranslator
    {
        /**
         * Translations from HTTP/2 header name to the HTTP/1.x equivalent.
         */
        private static readonly CharSequenceMap<AsciiString>
            REQUEST_HEADER_TRANSLATIONS = new CharSequenceMap<AsciiString>();
        private static readonly CharSequenceMap<AsciiString>
            RESPONSE_HEADER_TRANSLATIONS = new CharSequenceMap<AsciiString>();
        /**
         * Translations used for Extended CONNECT (RFC 8441) requests. In addition to the regular request
         * translations, the ':path' and ':protocol' pseudo-headers are preserved as extension headers so that
         * an Extended CONNECT request cannot be mistaken for a regular CONNECT request once converted to an
         * HTTP/1.x object.
         */
        private static readonly CharSequenceMap<AsciiString>
            CONNECT_REQUEST_HEADER_TRANSLATIONS = new CharSequenceMap<AsciiString>();
        static Http2ToHttpHeaderTranslator()
        {
            RESPONSE_HEADER_TRANSLATIONS.Add(IHttp2Headers.PseudoHeaderName.AUTHORITY.Value(),
                            HttpHeaderNames.HOST);
            RESPONSE_HEADER_TRANSLATIONS.Add(IHttp2Headers.PseudoHeaderName.SCHEME.Value(),
                            ExtensionHeaderNames.SCHEME.Text());
            REQUEST_HEADER_TRANSLATIONS.Add(RESPONSE_HEADER_TRANSLATIONS);
            RESPONSE_HEADER_TRANSLATIONS.Add(IHttp2Headers.PseudoHeaderName.PATH.Value(),
                            ExtensionHeaderNames.PATH.Text());
            CONNECT_REQUEST_HEADER_TRANSLATIONS.Add(REQUEST_HEADER_TRANSLATIONS);
            CONNECT_REQUEST_HEADER_TRANSLATIONS.Add(IHttp2Headers.PseudoHeaderName.PATH.Value(),
                            ExtensionHeaderNames.PATH.Text());
            CONNECT_REQUEST_HEADER_TRANSLATIONS.Add(IHttp2Headers.PseudoHeaderName.PROTOCOL.Value(),
                            ExtensionHeaderNames.PROTOCOL.Text());
        }

        private readonly int streamId;
        private readonly HttpHeaders output;
        private readonly CharSequenceMap<AsciiString> translations;

        /**
         * Create a new instance
         *
         * @param output The HTTP/1.x headers object to store the results of the translation
         * @param request if {@code true}, translates headers using the request translation map. Otherwise uses the
         *        response translation map.
         * @param connect if {@code true}, translates headers using the CONNECT request translation map, which
         *        additionally preserves the ':path' and ':protocol' pseudo-headers of an Extended CONNECT
         *        (RFC 8441) request as extension headers. Ignored unless {@code request} is {@code true}.
         */
        internal Http2ToHttpHeaderTranslator(int streamId, HttpHeaders output, bool request, bool connect)
        {
            this.streamId = streamId;
            this.output = output;
            if (request)
            {
                translations = connect ? CONNECT_REQUEST_HEADER_TRANSLATIONS : REQUEST_HEADER_TRANSLATIONS;
            }
            else
            {
                translations = RESPONSE_HEADER_TRANSLATIONS;
            }
        }

        internal void TranslateHeaders(IEnumerable<IEntry<ICharSequence, ICharSequence>> inputHeaders)
        {
            // lazily created as needed
            StringBuilder cookies = null;
            bool hostHeaderFound = false;

            foreach (IEntry<ICharSequence, ICharSequence> entry in inputHeaders)
            {
                ICharSequence name = entry.GetKey();
                ICharSequence value = entry.GetValue();
                AsciiString translatedName = translations.Get(name);
                if (translatedName != null)
                {
                    if (translatedName.ContentEqualsIgnoreCase(HttpHeaderNames.HOST))
                    {
                        hostHeaderFound = true;
                    }
                    output.Add(translatedName, AsciiString.Of(value));
                }
                else if (!IHttp2Headers.PseudoHeaderName.IsPseudoHeader(name))
                {
                    // https://tools.ietf.org/html/rfc7540#section-8.1.2.3
                    // All headers that start with ':' are only valid in HTTP/2 context
                    if (name.Length() == 0 || name.CharAt(0) == ':')
                    {
                        throw StreamError(streamId, PROTOCOL_ERROR,
                                "Invalid HTTP/2 header '%s' encountered in translation to HTTP/1.x", name);
                    }
                    if (HTTP2_TO_HTTP_HEADER_BLACKLIST.Contains(name))
                    {
                        continue;
                    }
                    if (COOKIE.Equals(name))
                    {
                        // combine the cookie values into 1 header entry.
                        // https://tools.ietf.org/html/rfc7540#section-8.1.2.5
                        if (cookies == null)
                        {
                            cookies = new StringBuilder();
                        }
                        else if (cookies.Length > 0)
                        {
                            cookies.Append("; ");
                        }
                        cookies.Append(value);
                    }
                    else if (ContentEqualsIgnoreCase(HttpHeaderNames.HOST, name))
                    {
                        // https://www.rfc-editor.org/rfc/rfc9113#section-8.3.1 requires that intermediaries
                        // translating to HTTP/1.x treat a literal 'host' header that conflicts with ':authority'
                        // as malformed, and RFC 9110 section 7.2 requires 'Host' be sent as a single field-value.
                        // Reject the request rather than emitting an HTTP/1.x message with duplicate Host headers.
                        if (hostHeaderFound)
                        {
                            string hostValue = output.Get(HttpHeaderNames.HOST);
                            if (!ContentEqualsIgnoreCase(hostValue == null ? null : new StringCharSequence(hostValue),
                                    value))
                            {
                                throw StreamError(streamId, PROTOCOL_ERROR,
                                        "Conflicting ':authority' and 'host' headers found");
                            }
                        }
                        else
                        {
                            hostHeaderFound = true;
                            output.Add(name, value);
                        }
                    }
                    else
                    {
                        output.Add(name, value);
                    }
                }
            }
            if (cookies != null)
            {
                output.Add(COOKIE, cookies.ToString());
            }
        }
    }
}
