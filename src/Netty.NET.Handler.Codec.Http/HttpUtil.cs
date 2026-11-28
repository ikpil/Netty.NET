/*
 * Copyright 2015 The Netty Project
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
using System.Globalization;
using System.Net;
using System.Text;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Common.Internal.StringUtil;

namespace Netty.NET.Handler.Codec.Http;

/**
 * Utility methods useful in the HTTP context.
 */
// CLR: the final class with a private constructor is a static class; CharSequence is ICharSequence and the String
// arguments that Java passes as CharSequence are wrapped in StringCharSequence. java.net.URI is System.Uri, and
// URI.toString() (the original string of a parsed URI) is Uri.OriginalString. java.nio.charset.Charset is
// System.Text.Encoding and CharsetUtil.ISO_8859_1 is Encoding.Latin1; Charset.forName is Common's CharsetUtil.ForName,
// which resolves the Java charset names and aliases (such as "UTF8") as Java does and throws ArgumentException for
// both IllegalCharsetNameException and UnsupportedCharsetException, so their catch is catch (ArgumentException).
// Long.parseLong is ParseLong (long.Parse with an optional sign; NumberFormatException is FormatException, also for an
// overflow, which long.Parse reports as OverflowException). InetSocketAddress is EndPoint: an unresolved address is a
// DnsEndPoint and a resolved one an IPEndPoint, whose getHostString() is the Common NetUtil.GetHostname (the numeric
// address, as an IPEndPoint keeps no host name; docs/handler-porting.md, Addresses). method.getClass() !=
// HttpMethod.class is GetType() != typeof(HttpMethod). The @Deprecated getCharsetAsString(HttpMessage), whose
// @deprecated text names getCharsetAsSequence(HttpMessage), is not ported (owner decision 2026-11-16). Java byte
// octets of AsciiString.array() are unsigned in C#; isValidTokenChar keeps the Java signed test with (sbyte), so the
// octets 0x80-0xFF are still invalid. String.trim() (chars up to U+0020) is TrimJava; ParseLong and TrimJava are internal
// CLR helpers that HttpObjectDecoder shares. IllegalArgumentException is ArgumentException; the package-private
// members are internal.
public static class HttpUtil
{

    private static readonly AsciiString CHARSET_EQUALS = new AsciiString(HttpHeaderValues.CHARSET + "=");
    private static readonly AsciiString SEMICOLON = AsciiString.Cached(";");
    private static readonly string COMMA_STRING = COMMA.ToString();

    /**
     * Determine if a uri is in origin-form according to
     * <a href="https://datatracker.ietf.org/doc/html/rfc9112#section-3.2.1">RFC 9112, 3.2.1</a>.
     */
    public static bool IsOriginForm(Uri uri)
    {
        return IsOriginForm(uri.OriginalString);
    }

    /**
     * Determine if a string uri is in origin-form according to
     * <a href="https://datatracker.ietf.org/doc/html/rfc9112#section-3.2.1">RFC 9112, 3.2.1</a>.
     */
    public static bool IsOriginForm(string uri)
    {
        return uri.StartsWith("/", StringComparison.Ordinal);
    }

    /**
     * Determine if a uri is in asterisk-form according to
     * <a href="https://datatracker.ietf.org/doc/html/rfc9112#section-3.2.4">RFC 9112, 3.2.4</a>.
     */
    public static bool IsAsteriskForm(Uri uri)
    {
        return IsAsteriskForm(uri.OriginalString);
    }

    /**
     * Determine if a string uri is in asterisk-form according to
     * <a href="https://datatracker.ietf.org/doc/html/rfc9112#section-3.2.4">RFC 9112, 3.2.4</a>.
     */
    public static bool IsAsteriskForm(string uri)
    {
        return "*".Equals(uri);
    }

    internal static void ValidateRequestLineTokens(HttpMethod method, string uri)
    {
        // The HttpVersion class does its own validation, and it's not possible for subclasses to circumvent it.
        // The HttpMethod class does its own validation, but subclasses might circumvent it.
        if (method.GetType() != typeof(HttpMethod))
        {
            if (!IsEncodingSafeStartLineToken(method.AsciiName()))
            {
                throw new ArgumentException(
                        "The HTTP method name contain illegal characters: " + method.AsciiName());
            }
        }

        if (!IsEncodingSafeStartLineToken(new StringCharSequence(uri)))
        {
            throw new ArgumentException("The URI contain illegal characters: " + uri);
        }
    }

    /**
     * Validate that the given request line token is safe for verbatim encoding to the network.
     * This does not fully check that the token – HTTP method, version, or URI – is valid and formatted correctly.
     * Only that the token does not contain characters that would break or
     * desynchronize HTTP message parsing of the start line wherein the token would be included.
     * <p>
     * See <a href="https://datatracker.ietf.org/doc/html/rfc9112#name-request-line">RFC 9112, 3.</a>
     *
     * @param token The token to check.
     * @return {@code true} if the token is safe to encode verbatim into the HTTP message output stream,
     * otherwise {@code false}.
     */
    public static bool IsEncodingSafeStartLineToken(ICharSequence token)
    {
        int lenBytes = token.Length();

        for (int i = 0; i < lenBytes; i++)
        {
            char ch = token.CharAt(i);
            // this is to help AOT compiled code which cannot profile the switch
            if (ch <= ' ')
            {
                switch (ch)
                {
                    case '\n':
                    case '\r':
                    case ' ':
                        return false;
                }
            }
        }

        return true;
    }

    /**
     * Returns {@code true} if and only if the connection can remain open and
     * thus 'kept alive'. This method respects the value of the
     * {@code "Connection"} header first and then the return value of
     * {@link HttpVersion#isKeepAliveDefault()}.
     */
    public static bool IsKeepAlive(IHttpMessage message)
    {
        return !message.Headers().ContainsValue(HttpHeaderNames.CONNECTION, HttpHeaderValues.CLOSE, true) &&
               (message.ProtocolVersion().IsKeepAliveDefault() ||
                message.Headers().ContainsValue(HttpHeaderNames.CONNECTION, HttpHeaderValues.KEEP_ALIVE, true));
    }

    /**
     * Sets the value of the {@code "Connection"} header depending on the
     * protocol version of the specified message. This getMethod sets or removes
     * the {@code "Connection"} header depending on what the default keep alive
     * mode of the message's protocol version is, as specified by
     * {@link HttpVersion#isKeepAliveDefault()}.
     * <ul>
     * <li>If the connection is kept alive by default:
     *     <ul>
     *     <li>set to {@code "close"} if {@code keepAlive} is {@code false}.</li>
     *     <li>remove otherwise.</li>
     *     </ul></li>
     * <li>If the connection is closed by default:
     *     <ul>
     *     <li>set to {@code "keep-alive"} if {@code keepAlive} is {@code true}.</li>
     *     <li>remove otherwise.</li>
     *     </ul></li>
     * </ul>
     * @see #setKeepAlive(HttpHeaders, HttpVersion, boolean)
     */
    public static void SetKeepAlive(IHttpMessage message, bool keepAlive)
    {
        SetKeepAlive(message.Headers(), message.ProtocolVersion(), keepAlive);
    }

    /**
     * Sets the value of the {@code "Connection"} header depending on the
     * protocol version of the specified message. This getMethod sets or removes
     * the {@code "Connection"} header depending on what the default keep alive
     * mode of the message's protocol version is, as specified by
     * {@link HttpVersion#isKeepAliveDefault()}.
     * <ul>
     * <li>If the connection is kept alive by default:
     *     <ul>
     *     <li>set to {@code "close"} if {@code keepAlive} is {@code false}.</li>
     *     <li>remove otherwise.</li>
     *     </ul></li>
     * <li>If the connection is closed by default:
     *     <ul>
     *     <li>set to {@code "keep-alive"} if {@code keepAlive} is {@code true}.</li>
     *     <li>remove otherwise.</li>
     *     </ul></li>
     * </ul>
     */
    public static void SetKeepAlive(HttpHeaders h, HttpVersion httpVersion, bool keepAlive)
    {
        if (httpVersion.IsKeepAliveDefault())
        {
            if (keepAlive)
            {
                h.Remove(HttpHeaderNames.CONNECTION);
            }
            else
            {
                h.Set(HttpHeaderNames.CONNECTION, HttpHeaderValues.CLOSE);
            }
        }
        else
        {
            if (keepAlive)
            {
                h.Set(HttpHeaderNames.CONNECTION, HttpHeaderValues.KEEP_ALIVE);
            }
            else
            {
                h.Remove(HttpHeaderNames.CONNECTION);
            }
        }
    }

    /**
     * Returns the length of the content. Please note that this value is
     * not retrieved from {@link HttpContent#content()} but from the
     * {@code "Content-Length"} header, and thus they are independent from each
     * other.
     *
     * @return the content length
     *
     * @throws NumberFormatException
     *         if the message does not have the {@code "Content-Length"} header
     *         or its value is not a number
     */
    public static long GetContentLength(IHttpMessage message)
    {
        string value = message.Headers().Get(HttpHeaderNames.CONTENT_LENGTH);
        if (value != null)
        {
            return ParseLong(value);
        }

        // We know the content length if it's a Web Socket message even if
        // Content-Length header is missing.
        long webSocketContentLength = GetWebSocketContentLength(message);
        if (webSocketContentLength >= 0)
        {
            return webSocketContentLength;
        }

        // Otherwise we don't.
        throw new FormatException("header not found: " + HttpHeaderNames.CONTENT_LENGTH);
    }

    // CLR: java.lang.Long.parseLong(String), which accepts an optional sign and decimal digits only and throws
    // NumberFormatException for anything else, including an overflow.
    internal static long ParseLong(string s)
    {
        try
        {
            return long.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }
        catch (OverflowException e)
        {
            throw new FormatException("For input string: \"" + s + "\"", e);
        }
    }

    /**
     * Returns the length of the content or the specified default value if the message does not have the {@code
     * "Content-Length" header}. Please note that this value is not retrieved from {@link HttpContent#content()} but
     * from the {@code "Content-Length"} header, and thus they are independent from each other.
     *
     * @param message      the message
     * @param defaultValue the default value
     * @return the content length or the specified default value
     * @throws NumberFormatException if the {@code "Content-Length"} header does not parse as a long
     */
    public static long GetContentLength(IHttpMessage message, long defaultValue)
    {
        string value = message.Headers().Get(HttpHeaderNames.CONTENT_LENGTH);
        if (value != null)
        {
            return ParseLong(value);
        }

        // We know the content length if it's a Web Socket message even if
        // Content-Length header is missing.
        long webSocketContentLength = GetWebSocketContentLength(message);
        if (webSocketContentLength >= 0)
        {
            return webSocketContentLength;
        }

        // Otherwise we don't.
        return defaultValue;
    }

    /**
     * Get an {@code int} representation of {@link #getContentLength(HttpMessage, long)}.
     *
     * @return the content length or {@code defaultValue} if this message does
     *         not have the {@code "Content-Length"} header.
     *
     * @throws NumberFormatException if the {@code "Content-Length"} header does not parse as an int
     */
    public static int GetContentLength(IHttpMessage message, int defaultValue)
    {
        return (int) Math.Min(int.MaxValue, GetContentLength(message, (long) defaultValue));
    }

    /**
     * Returns the content length of the specified web socket message. If the
     * specified message is not a web socket message, {@code -1} is returned.
     */
    internal static int GetWebSocketContentLength(IHttpMessage message)
    {
        // WebSocket messages have constant content-lengths.
        HttpHeaders h = message.Headers();

        if (message is IHttpRequest)
        {
            IHttpRequest req = (IHttpRequest) message;
            if (HttpMethod.GET.Equals(req.Method()) &&
                    h.Contains(HttpHeaderNames.SEC_WEBSOCKET_KEY1) &&
                    h.Contains(HttpHeaderNames.SEC_WEBSOCKET_KEY2))
            {
                return 8;
            }
        }
        else if (message is IHttpResponse)
        {
            IHttpResponse res = (IHttpResponse) message;
            if (res.Status().Code() == 101 &&
                    h.Contains(HttpHeaderNames.SEC_WEBSOCKET_ORIGIN) &&
                    h.Contains(HttpHeaderNames.SEC_WEBSOCKET_LOCATION))
            {
                return 16;
            }
        }

        // Not a web socket message
        return -1;
    }

    /**
     * Sets the {@code "Content-Length"} header.
     */
    public static void SetContentLength(IHttpMessage message, long length)
    {
        message.Headers().Set(HttpHeaderNames.CONTENT_LENGTH, length);
    }

    public static bool IsContentLengthSet(IHttpMessage m)
    {
        return m.Headers().Contains(HttpHeaderNames.CONTENT_LENGTH);
    }

    /**
     * Returns {@code true} if and only if the specified message contains an expect header and the only expectation
     * present is the 100-continue expectation. Note that this method returns {@code false} if the expect header is
     * not valid for the message (e.g., the message is a response, or the version on the message is HTTP/1.0).
     *
     * @param message the message
     * @return {@code true} if and only if the expectation 100-continue is present and it is the only expectation
     * present
     */
    public static bool Is100ContinueExpected(IHttpMessage message)
    {
        return IsExpectHeaderValid(message)
          // unquoted tokens in the expect header are case-insensitive, thus 100-continue is case insensitive
          && message.Headers().Contains(HttpHeaderNames.EXPECT, HttpHeaderValues.CONTINUE, true);
    }

    /**
     * Returns {@code true} if the specified message contains an expect header specifying an expectation that is not
     * supported. Note that this method returns {@code false} if the expect header is not valid for the message
     * (e.g., the message is a response, or the version on the message is HTTP/1.0).
     *
     * @param message the message
     * @return {@code true} if and only if an expectation is present that is not supported
     */
    internal static bool IsUnsupportedExpectation(IHttpMessage message)
    {
        if (!IsExpectHeaderValid(message))
        {
            return false;
        }

        string expectValue = message.Headers().Get(HttpHeaderNames.EXPECT);
        return expectValue != null && !string.Equals(HttpHeaderValues.CONTINUE.ToString(), expectValue,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExpectHeaderValid(IHttpMessage message)
    {
        /*
         * Expect: 100-continue is for requests only and it works only on HTTP/1.1 or later. Note further that RFC 7231
         * section 5.1.1 says "A server that receives a 100-continue expectation in an HTTP/1.0 request MUST ignore
         * that expectation."
         */
        return message is IHttpRequest &&
                message.ProtocolVersion().CompareTo(HttpVersion.HTTP_1_1) >= 0;
    }

    /**
     * Sets or removes the {@code "Expect: 100-continue"} header to / from the
     * specified message. If {@code expected} is {@code true},
     * the {@code "Expect: 100-continue"} header is set and all other previous
     * {@code "Expect"} headers are removed.  Otherwise, all {@code "Expect"}
     * headers are removed completely.
     */
    public static void Set100ContinueExpected(IHttpMessage message, bool expected)
    {
        if (expected)
        {
            message.Headers().Set(HttpHeaderNames.EXPECT, HttpHeaderValues.CONTINUE);
        }
        else
        {
            message.Headers().Remove(HttpHeaderNames.EXPECT);
        }
    }

    /**
     * Checks to see if the transfer encoding in a specified {@link HttpMessage} is chunked
     *
     * @param message The message to check
     * @return True if transfer encoding is chunked, otherwise false
     */
    public static bool IsTransferEncodingChunked(IHttpMessage message)
    {
        return message.Headers().ContainsValue(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED, true);
    }

    /**
     * Set the {@link HttpHeaderNames#TRANSFER_ENCODING} to either include {@link HttpHeaderValues#CHUNKED} if
     * {@code chunked} is {@code true}, or remove {@link HttpHeaderValues#CHUNKED} if {@code chunked} is {@code false}.
     *
     * @param m The message which contains the headers to modify.
     * @param chunked if {@code true} then include {@link HttpHeaderValues#CHUNKED} in the headers. otherwise remove
     * {@link HttpHeaderValues#CHUNKED} from the headers.
     */
    public static void SetTransferEncodingChunked(IHttpMessage m, bool chunked)
    {
        if (chunked)
        {
            m.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);
            m.Headers().Remove(HttpHeaderNames.CONTENT_LENGTH);
        }
        else
        {
            IList<string> encodings = m.Headers().GetAll(HttpHeaderNames.TRANSFER_ENCODING);
            if (encodings.Count == 0)
            {
                return;
            }

            // CLR: the ArrayList<CharSequence> copy of the String values and its Iterator.remove() loop are a List of
            // StringCharSequence values filtered in place.
            List<object> values = new List<object>(encodings.Count);

            foreach (string encoding in encodings)
            {
                values.Add(new StringCharSequence(encoding));
            }

            values.RemoveAll(value => HttpHeaderValues.CHUNKED.ContentEqualsIgnoreCase((ICharSequence) value));

            if (values.Count == 0)
            {
                m.Headers().Remove(HttpHeaderNames.TRANSFER_ENCODING);
            }
            else
            {
                m.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, values);
            }
        }
    }

    /**
     * Fetch charset from message's Content-Type header.
     *
     * @param message entity to fetch Content-Type header from
     * @return the charset from message's Content-Type header or {@link CharsetUtil#ISO_8859_1}
     * if charset is not presented or unparsable
     */
    public static Encoding GetCharset(IHttpMessage message)
    {
        return GetCharset(message, Encoding.Latin1);
    }

    /**
     * Fetch charset from Content-Type header value.
     *
     * @param contentTypeValue Content-Type header value to parse
     * @return the charset from message's Content-Type header or {@link CharsetUtil#ISO_8859_1}
     * if charset is not presented or unparsable
     */
    public static Encoding GetCharset(ICharSequence contentTypeValue)
    {
        if (contentTypeValue != null)
        {
            return GetCharset(contentTypeValue, Encoding.Latin1);
        }
        else
        {
            return Encoding.Latin1;
        }
    }

    /**
     * Fetch charset from message's Content-Type header.
     *
     * @param message        entity to fetch Content-Type header from
     * @param defaultCharset result to use in case of empty, incorrect or doesn't contain required part header value
     * @return the charset from message's Content-Type header or {@code defaultCharset}
     * if charset is not presented or unparsable
     */
    public static Encoding GetCharset(IHttpMessage message, Encoding defaultCharset)
    {
        string contentTypeValue = message.Headers().Get(HttpHeaderNames.CONTENT_TYPE);
        if (contentTypeValue != null)
        {
            return GetCharset(new StringCharSequence(contentTypeValue), defaultCharset);
        }
        else
        {
            return defaultCharset;
        }
    }

    /**
     * Fetch charset from Content-Type header value.
     *
     * @param contentTypeValue Content-Type header value to parse
     * @param defaultCharset   result to use in case of empty, incorrect or doesn't contain required part header value
     * @return the charset from message's Content-Type header or {@code defaultCharset}
     * if charset is not presented or unparsable
     */
    public static Encoding GetCharset(ICharSequence contentTypeValue, Encoding defaultCharset)
    {
        if (contentTypeValue != null)
        {
            ICharSequence charsetRaw = GetCharsetAsSequence(contentTypeValue);
            if (charsetRaw != null)
            {
                // Remove only the trailing optional whitespace (OWS) that RFC 9110 allows before
                // the semicolon starting the next parameter, see
                // https://www.rfc-editor.org/rfc/rfc9110#section-5.6.6
                // Leading whitespace after '=' is not grammar-legal and is deliberately kept, so
                // such values keep failing the charset lookup below.
                int end = charsetRaw.Length();

                while (end > 0 && IsOws(charsetRaw.CharAt(end - 1)))
                {
                    end--;
                }

                if (end < charsetRaw.Length())
                {
                    charsetRaw = charsetRaw.SubSequence(0, end);
                }

                if (charsetRaw.Length() > 2)
                { // at least contains 2 quotes(")
                    if (charsetRaw.CharAt(0) == '"' && charsetRaw.CharAt(charsetRaw.Length() - 1) == '"')
                {
                        charsetRaw = charsetRaw.SubSequence(1, charsetRaw.Length() - 1);
                    }
                }

                try
                {
                    return CharsetUtil.ForName(charsetRaw.ToString());
                }
                catch (ArgumentException)
                {
                    // just return the default charset
                }
            }
        }

        return defaultCharset;
    }

    /**
     * Whether the given character is optional whitespace (OWS) as defined by
     * <a href="https://www.rfc-editor.org/rfc/rfc9110#section-5.6.3">RFC 9110 section 5.6.3</a>.
     */
    private static bool IsOws(char c)
    {
        return c == ' ' || c == '\t';
    }

    /**
     * Fetch charset from message's Content-Type header as a char sequence.
     *
     * A lot of sites/possibly clients have charset="CHARSET", for example charset="utf-8". Or "utf8" instead of "utf-8"
     * This is not according to standard, but this method provide an ability to catch desired mistakes manually in code
     *
     * @return the {@code CharSequence} with charset from message's Content-Type header
     * or {@code null} if charset is not presented
     */
    public static ICharSequence GetCharsetAsSequence(IHttpMessage message)
    {
        string contentTypeValue = message.Headers().Get(HttpHeaderNames.CONTENT_TYPE);
        if (contentTypeValue != null)
        {
            return GetCharsetAsSequence(new StringCharSequence(contentTypeValue));
        }
        else
        {
            return null;
        }
    }

    /**
     * Fetch charset from Content-Type header value as a char sequence.
     *
     * A lot of sites/possibly clients have charset="CHARSET", for example charset="utf-8". Or "utf8" instead of "utf-8"
     * This is not according to standard, but this method provide an ability to catch desired mistakes manually in code
     *
     * @param contentTypeValue Content-Type header value to parse
     * @return the {@code CharSequence} with charset from message's Content-Type header
     * or {@code null} if charset is not presented
     * @throws NullPointerException in case if {@code contentTypeValue == null}
     */
    public static ICharSequence GetCharsetAsSequence(ICharSequence contentTypeValue)
    {
        ObjectUtil.CheckNotNull(contentTypeValue, "contentTypeValue");

        int indexOfCharset = AsciiString.IndexOfIgnoreCaseAscii(contentTypeValue, CHARSET_EQUALS, 0);
        if (indexOfCharset == AsciiString.INDEX_NOT_FOUND)
        {
            return null;
        }

        int indexOfEncoding = indexOfCharset + CHARSET_EQUALS.Length();
        if (indexOfEncoding < contentTypeValue.Length())
        {
            ICharSequence charsetCandidate = contentTypeValue.SubSequence(indexOfEncoding, contentTypeValue.Length());
            int indexOfSemicolon = AsciiString.IndexOfIgnoreCaseAscii(charsetCandidate, SEMICOLON, 0);
            if (indexOfSemicolon == AsciiString.INDEX_NOT_FOUND)
            {
                return charsetCandidate;
            }

            return charsetCandidate.SubSequence(0, indexOfSemicolon);
        }

        return null;
    }

    /**
     * Fetch MIME type part from message's Content-Type header as a char sequence.
     *
     * @param message entity to fetch Content-Type header from
     * @return the MIME type as a {@code CharSequence} from message's Content-Type header
     * or {@code null} if content-type header or MIME type part of this header are not presented
     * <p/>
     * "content-type: text/html; charset=utf-8" - "text/html" will be returned <br/>
     * "content-type: text/html" - "text/html" will be returned <br/>
     * "content-type: " or no header - {@code null} we be returned
     */
    public static ICharSequence GetMimeType(IHttpMessage message)
    {
        string contentTypeValue = message.Headers().Get(HttpHeaderNames.CONTENT_TYPE);
        if (contentTypeValue != null)
        {
            return GetMimeType(new StringCharSequence(contentTypeValue));
        }
        else
        {
            return null;
        }
    }

    /**
     * Fetch MIME type part from Content-Type header value as a char sequence.
     *
     * @param contentTypeValue Content-Type header value to parse
     * @return the MIME type as a {@code CharSequence} from message's Content-Type header
     * or {@code null} if content-type header or MIME type part of this header are not presented
     * <p/>
     * "content-type: text/html; charset=utf-8" - "text/html" will be returned <br/>
     * "content-type: text/html" - "text/html" will be returned <br/>
     * "content-type: empty header - {@code null} we be returned
     * @throws NullPointerException in case if {@code contentTypeValue == null}
     */
    public static ICharSequence GetMimeType(ICharSequence contentTypeValue)
    {
        ObjectUtil.CheckNotNull(contentTypeValue, "contentTypeValue");

        int indexOfSemicolon = AsciiString.IndexOfIgnoreCaseAscii(contentTypeValue, SEMICOLON, 0);
        if (indexOfSemicolon != AsciiString.INDEX_NOT_FOUND)
        {
            // Remove the trailing optional whitespace (OWS) that RFC 9110 allows before the semicolon
            // starting the next parameter, see https://www.rfc-editor.org/rfc/rfc9110#section-8.3.1
            int end = indexOfSemicolon;

            while (end > 0 && IsOws(contentTypeValue.CharAt(end - 1)))
            {
                end--;
            }

            return contentTypeValue.SubSequence(0, end);
        }
        else
        {
            return contentTypeValue.Length() > 0 ? contentTypeValue : null;
        }
    }

    /**
     * Formats the host string of an address so it can be used for computing an HTTP component
     * such as a URL or a Host header
     *
     * @param addr the address
     * @return the formatted String
     */
    public static string FormatHostnameForHttp(EndPoint addr)
    {
        string hostString = NetUtil.GetHostname(addr);
        if (NetUtil.IsValidIpV6Address(hostString))
        {
            if (!(addr is DnsEndPoint))
            {
                hostString = NetUtil.ToAddressString(((IPEndPoint) addr).Address);
            }
            else if (hostString[0] == '[' && hostString[hostString.Length - 1] == ']')
            {
                // If IPv6 address already contains brackets, let's return as is.
                return hostString;
            }

            return '[' + hostString + ']';
        }

        return hostString;
    }

    /**
     * Validates, and optionally extracts the content length from headers. This method is not intended for
     * general use, but is here to be shared between HTTP/1 and HTTP/2 parsing.
     *
     * @param contentLengthFields the content-length header fields.
     * @param isHttp10OrEarlier unused
     * @param allowDuplicateContentLengths {@code true}  if multiple, identical-value content lengths should be allowed.
     * @return the normalized content length from the headers or {@code -1} if the fields were empty.
     * @throws IllegalArgumentException if the content-length fields are not valid
     */
    // CLR: List<? extends CharSequence> is the generic IList<T> (HttpObjectDecoder passes the String values of
    // headers().getAll(...), and a C# string is not an ICharSequence); each field is read through ToString(). The
    // list prints as Java's AbstractCollection.toString() ("[a, b]") in the exception messages.
    public static long NormalizeAndGetContentLength<T>(
            IList<T> contentLengthFields, bool isHttp10OrEarlier,
            bool allowDuplicateContentLengths)
    {
        if (contentLengthFields.Count == 0)
        {
            return -1;
        }

        // Guard against multiple Content-Length headers as stated in
        // https://tools.ietf.org/html/rfc7230#section-3.3.2:
        //
        // If a message is received that has multiple Content-Length header
        //   fields with field-values consisting of the same decimal value, or a
        //   single Content-Length header field with a field value containing a
        //   list of identical decimal values (e.g., "Content-Length: 42, 42"),
        //   indicating that duplicate Content-Length header fields have been
        //   generated or combined by an upstream message processor, then the
        //   recipient MUST either reject the message as invalid or replace the
        //   duplicated field-values with a single valid Content-Length field
        //   containing that decimal value prior to determining the message body
        //   length or forwarding the message.
        string firstField = contentLengthFields[0].ToString();
        bool multipleContentLengths =
                contentLengthFields.Count > 1 || firstField.IndexOf(COMMA) >= 0;

        if (multipleContentLengths)
        {
            if (allowDuplicateContentLengths)
            {
                // Find and enforce that all Content-Length values are the same
                string firstValue = null;

                foreach (T field in contentLengthFields)
                {
                    string[] tokens = field.ToString().Split(COMMA_STRING);

                    foreach (string token in tokens)
                    {
                        string trimmed = TrimJava(token);

                        if (firstValue == null)
                        {
                            firstValue = trimmed;
                        }
                        else if (!trimmed.Equals(firstValue))
                        {
                            throw new ArgumentException(
                                    "Multiple Content-Length values found: " + ToJavaString(contentLengthFields));
                        }
                    }
                }

                // Replace the duplicated field-values with a single valid Content-Length field
                firstField = firstValue;
            }
            else
            {
                // Reject the message as invalid
                throw new ArgumentException(
                        "Multiple Content-Length values found: " + ToJavaString(contentLengthFields));
            }
        }

        // Ensure we not allow sign as part of the content-length:
        // See https://github.com/squid-cache/squid/security/advisories/GHSA-qf3v-rc95-96j5
        if (firstField.Length == 0 || !char.IsDigit(firstField[0]))
        {
            // Reject the message as invalid
            throw new ArgumentException(
                    "Content-Length value is not a number: " + firstField);
        }

        try
        {
            long value = ParseLong(firstField);
            return CheckPositiveOrZero(value, "Content-Length value");
        }
        catch (FormatException e)
        {
            // Reject the message as invalid
            throw new ArgumentException(
                    "Content-Length value is not a number: " + firstField, e);
        }
    }

    // CLR: String.trim(), which removes the chars up to U+0020 from both ends.
    internal static string TrimJava(string value)
    {
        int start = 0;
        int end = value.Length;

        while (start < end && value[start] <= ' ')
        {
            start++;
        }

        while (end > start && value[end - 1] <= ' ')
        {
            end--;
        }

        return value.Substring(start, end - start);
    }

    // CLR: AbstractCollection.toString() of the fields.
    private static string ToJavaString<T>(IList<T> list)
    {
        StringBuilder sb = new StringBuilder("[");

        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(list[i]);
        }

        return sb.Append(']').ToString();
    }

    /**
     * Validate a <a href="https://tools.ietf.org/html/rfc7230#section-3.2.6">token</a> contains only allowed
     * characters.
     * <p>
     * The <a href="https://tools.ietf.org/html/rfc2616#section-2.2">token</a> format is used for variety of HTTP
     * components, like  <a href="https://tools.ietf.org/html/rfc6265#section-4.1.1">cookie-name</a>,
     * <a href="https://tools.ietf.org/html/rfc7230#section-3.2.6">field-name</a> of a
     * <a href="https://tools.ietf.org/html/rfc7230#section-3.2">header-field</a>, or
     * <a href="https://tools.ietf.org/html/rfc7231#section-4">request method</a>.
     *
     * @param token the token to validate.
     * @return the index of the first invalid token character found, or {@code -1} if there are none.
     */
    internal static int ValidateToken(ICharSequence token)
    {
        if (token is AsciiString)
        {
            return ValidateAsciiStringToken((AsciiString) token);
        }

        return ValidateCharSequenceToken(token);
    }

    /**
     * Validate that an {@link AsciiString} contain onlu valid
     * <a href="https://tools.ietf.org/html/rfc7230#section-3.2.6">token</a> characters.
     *
     * @param token the ascii string to validate.
     */
    private static int ValidateAsciiStringToken(AsciiString token)
    {
        byte[] array = token.Array();

        for (int i = token.ArrayOffset(), len = token.ArrayOffset() + token.Length(); i < len; i++)
        {
            if (!IsValidTokenChar(array[i]))
            {
                return i - token.ArrayOffset();
            }
        }

        return -1;
    }

    /**
     * Validate that a {@link CharSequence} contain onlu valid
     * <a href="https://tools.ietf.org/html/rfc7230#section-3.2.6">token</a> characters.
     *
     * @param token the character sequence to validate.
     */
    private static int ValidateCharSequenceToken(ICharSequence token)
    {
        for (int i = 0, len = token.Length(); i < len; i++)
        {
            int value = token.CharAt(i);
            // 1. Check for truncation (anything above 255)
            // 2. Check against the BitSet (isValidTokenChar handles 128-255 via bit < 0)
            if (value > 0xFF || !IsValidTokenChar((byte) value))
            {
                return i;
            }
        }

        return -1;
    }

    // HEADER
    // header-field   = field-name ":" OWS field-value OWS
    //
    // field-name     = token
    // token          = 1*tchar
    //
    // tchar          = "!" / "#" / "$" / "%" / "&" / "'" / "*"
    //                    / "+" / "-" / "." / "^" / "_" / "`" / "|" / "~"
    //                    / DIGIT / ALPHA
    //                    ; any VCHAR, except delimiters.
    //  Delimiters are chosen
    //   from the set of US-ASCII visual characters not allowed in a token
    //   (DQUOTE and "(),/:;<=>?@[\]{}")
    //
    // COOKIE
    // cookie-pair       = cookie-name "=" cookie-value
    // cookie-name       = token
    // token          = 1*<any CHAR except CTLs or separators>
    // CTL = <any US-ASCII control character
    //       (octets 0 - 31) and DEL (127)>
    // separators     = "(" | ")" | "<" | ">" | "@"
    //                      | "," | ";" | ":" | "\" | <">
    //                      | "/" | "[" | "]" | "?" | "="
    //                      | "{" | "}" | SP | HT
    //
    // field-name's token is equivalent to cookie-name's token, we can reuse the tchar mask for both:

    //    private static final class BitSet128 {
    //        private long high;
    //        private long low;
    //
    //        BitSet128 range(char fromInc, char toInc) {
    //            for (int bit = fromInc; bit <= toInc; bit++) {
    //                if (bit < 64) {
    //                    low |= 1L << bit;
    //                } else {
    //                    high |= 1L << bit - 64;
    //                }
    //            }
    //            return this;
    //        }
    //
    //        BitSet128 bits(char... bits) {
    //            for (char bit : bits) {
    //                if (bit < 64) {
    //                    low |= 1L << bit;
    //                } else {
    //                    high |= 1L << bit - 64;
    //                }
    //            }
    //            return this;
    //        }
    //
    //        long high() {
    //            return high;
    //        }
    //
    //        long low() {
    //            return low;
    //        }
    //
    //        static boolean contains(byte bit, long high, long low) {
    //            if (bit < 0) {
    //                return false;
    //            }
    //            if (bit < 64) {
    //                return 0 != (low & 1L << bit);
    //            }
    //            return 0 != (high & 1L << bit - 64);
    //        }
    //    }

    // BitSet128 tokenChars = new BitSet128()
    //        .range('0', '9').range('a', 'z').range('A', 'Z') // Alphanumeric.
    //        .bits('-', '.', '_', '~') // Unreserved characters.
    //        .bits('!', '#', '$', '%', '&', '\'', '*', '+', '^', '`', '|'); // Token special characters.

    // This constants calculated by the above code
    private const long TOKEN_CHARS_HIGH = 0x57ffffffc7fffffeL;
    private const long TOKEN_CHARS_LOW = 0x3ff6cfa00000000L;

    // CLR: the Java byte is signed and the octets 0x80-0xFF are negative ("octet < 0"); the C# byte is unsigned, so
    // the octet is read as sbyte to keep the Java test (Java byte sign rule).
    internal static bool IsValidTokenChar(byte octet)
    {
        sbyte value = unchecked((sbyte) octet);
        if (value < 0)
        {
            return false;
        }
        if (value < 64)
        {
            return 0 != (TOKEN_CHARS_LOW & 1L << value);
        }

        return 0 != (TOKEN_CHARS_HIGH & 1L << value - 64);
    }
}
