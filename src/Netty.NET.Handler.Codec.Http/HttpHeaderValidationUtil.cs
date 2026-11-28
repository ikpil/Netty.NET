/*
 * Copyright 2022 The Netty Project
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
using Netty.NET.Common.Internal;
using static Netty.NET.Common.AsciiString;

namespace Netty.NET.Handler.Codec.Http;

/**
 * Functions used to perform various validations of HTTP header names and values.
 */
// CLR: the final class with a private constructor is a static class. CharSequence is ICharSequence. The
// @Deprecated HttpHeaderNames.KEEP_ALIVE and PROXY_CONNECTION are not ported (their @deprecated text names
// CONNECTION, owner decision 2026-11-16), so their header names are the private constants KEEP_ALIVE and
// PROXY_CONNECTION here, with the same AsciiString.cached values. The Java byte[] of AsciiString.array() is the
// unsigned byte[] of the Common AsciiString, and "& 0xFF" yields the same value for both.
public static class HttpHeaderValidationUtil
{
    private static readonly AsciiString KEEP_ALIVE = AsciiString.Cached("keep-alive");
    private static readonly AsciiString PROXY_CONNECTION = AsciiString.Cached("proxy-connection");

    /**
     * Check if a header name is "connection related".
     * <p>
     * The <a href="https://datatracker.ietf.org/doc/html/rfc9110#section-7.6.1">RFC9110</a> only specify an incomplete
     * list of the following headers:
     *
     * <ul>
     *     <li><tt>Connection</tt></li>
     *     <li><tt>Proxy-Connection</tt></li>
     *     <li><tt>Keep-Alive</tt></li>
     *     <li><tt>TE</tt></li>
     *     <li><tt>Transfer-Encoding</tt></li>
     *     <li><tt>Upgrade</tt></li>
     * </ul>
     *
     * @param name the name of the header to check. The check is case-insensitive.
     * @param ignoreTeHeader {@code true} if the <tt>TE</tt> header should be ignored by this check.
     * This is relevant for HTTP/2 header validation, where the <tt>TE</tt> header has special rules.
     * @return {@code true} if the given header name is one of the specified connection-related headers.
     */
    // We need to check for deprecated headers as well.
    public static bool IsConnectionHeader(ICharSequence name, bool ignoreTeHeader)
    {
        // These are the known standard and non-standard connection related headers:
        // - upgrade (7 chars)
        // - connection (10 chars)
        // - keep-alive (10 chars)
        // - proxy-connection (16 chars)
        // - transfer-encoding (17 chars)
        //
        // See https://datatracker.ietf.org/doc/html/rfc9113#section-8.2.2
        // and https://datatracker.ietf.org/doc/html/rfc9110#section-7.6.1
        // for the list of connection related headers.
        //
        // We scan for these based on the length, then double-check any matching name.
        int len = name.Length();

        switch (len)
        {
            case 2:
                return ignoreTeHeader? false : ContentEqualsIgnoreCase(name, HttpHeaderNames.TE);
            case 7:
                return ContentEqualsIgnoreCase(name, HttpHeaderNames.UPGRADE);
            case 10:
                return ContentEqualsIgnoreCase(name, HttpHeaderNames.CONNECTION) ||
                    ContentEqualsIgnoreCase(name, KEEP_ALIVE);
            case 16:
                return ContentEqualsIgnoreCase(name, PROXY_CONNECTION);
            case 17:
                return ContentEqualsIgnoreCase(name, HttpHeaderNames.TRANSFER_ENCODING);
            default:
                return false;
        }
    }

    /**
     * If the given header is {@link HttpHeaderNames#TE} and the given header value is <em>not</em>
     * {@link HttpHeaderValues#TRAILERS}, then return {@code true}. Otherwie, {@code false}.
     * <p>
     * The string comparisons are case-insensitive.
     * <p>
     * This check is important for HTTP/2 header validation.
     *
     * @param name the header name to check if it is <tt>TE</tt> or not.
     * @param value the header value to check if it is something other than <tt>TRAILERS</tt>.
     * @return {@code true} only if the header name is <tt>TE</tt>, and the header value is <em>not</em>
     * <tt>TRAILERS</tt>. Otherwise, {@code false}.
     */
    public static bool IsTeNotTrailers(ICharSequence name, ICharSequence value)
    {
        if (name.Length() == 2)
        {
            return ContentEqualsIgnoreCase(name, HttpHeaderNames.TE) &&
                    !ContentEqualsIgnoreCase(value, HttpHeaderValues.TRAILERS);
        }

        return false;
    }

    /**
     * Validate the given HTTP header value by searching for any illegal characters.
     *
     * @param value the HTTP header value to validate.
     * @return the index of the first illegal character found, or {@code -1} if there are none and the header value is
     * valid.
     */
    public static int ValidateValidHeaderValue(ICharSequence value)
    {
        int length = value.Length();
        if (length == 0)
        {
            return -1;
        }
        if (value is AsciiString)
        {
            return VerifyValidHeaderValueAsciiString((AsciiString) value);
        }

        return VerifyValidHeaderValueCharSequence(value);
    }

    private static int VerifyValidHeaderValueAsciiString(AsciiString value)
    {
        // Validate value to field-content rule.
        //  field-content  = field-vchar [ 1*( SP / HTAB ) field-vchar ]
        //  field-vchar    = VCHAR / obs-text
        //  VCHAR          = %x21-7E ; visible (printing) characters
        //  obs-text       = %x80-FF
        //  SP             = %x20
        //  HTAB           = %x09 ; horizontal tab
        //  See: https://datatracker.ietf.org/doc/html/rfc7230#section-3.2
        //  And: https://datatracker.ietf.org/doc/html/rfc5234#appendix-B.1
        byte[] array = value.Array();
        int start = value.ArrayOffset();
        int b = array[start] & 0xFF;
        if (b < 0x21 || b == 0x7F)
        {
            return 0;
        }

        int end = start + value.Length();

        for (int i = start + 1; i < end; i++)
        {
            b = array[i] & 0xFF;
            if (b < 0x20 && b != 0x09 || b == 0x7F)
            {
                return i - start;
            }
        }

        return -1;
    }

    private static int VerifyValidHeaderValueCharSequence(ICharSequence value)
    {
        // Validate value to field-content rule.
        //  field-content  = field-vchar [ 1*( SP / HTAB ) field-vchar ]
        //  field-vchar    = VCHAR / obs-text
        //  VCHAR          = %x21-7E ; visible (printing) characters
        //  obs-text       = %x80-FF
        //  SP             = %x20
        //  HTAB           = %x09 ; horizontal tab
        //  See: https://datatracker.ietf.org/doc/html/rfc7230#section-3.2
        //  And: https://datatracker.ietf.org/doc/html/rfc5234#appendix-B.1
        int b = value.CharAt(0);
        if (b < 0x21 || b == 0x7F)
        {
            return 0;
        }

        int length = value.Length();

        for (int i = 1; i < length; i++)
        {
            b = value.CharAt(i);
            if (b < 0x20 && b != 0x09 || b == 0x7F)
            {
                return i;
            }
        }

        return -1;
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
    public static int ValidateToken(ICharSequence token)
    {
        return HttpUtil.ValidateToken(token);
    }
}
