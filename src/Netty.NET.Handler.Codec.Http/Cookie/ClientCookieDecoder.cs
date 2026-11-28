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
using Netty.NET.Common;
using static Netty.NET.Common.Internal.ObjectUtil;
using SameSite = Netty.NET.Handler.Codec.Http.Cookie.CookieHeaderNames.SameSite;

namespace Netty.NET.Handler.Codec.Http.Cookie;

/**
 * A <a href="https://tools.ietf.org/html/rfc6265">RFC6265</a> compliant cookie decoder to be used client side.
 *
 * It will store the way the raw value was wrapped in {@link Cookie#setWrap(boolean)} so it can be
 * eventually sent back to the Origin server as is.
 *
 * @see ClientCookieEncoder
 */
// CLR: the labeled "break loop" is a goto to a label after the loop (as the core QueryStringDecoder). String.indexOf(ch,
// from) is IndexOf(ch, from) (from is always inside the header here). String.regionMatches(true, ...) is string.Compare
// with StringComparison.OrdinalIgnoreCase over the length (the number of characters compared is the lesser of the two
// substring lengths and the length, https://learn.microsoft.com/en-us/dotnet/api/system.string.compare?view=net-10.0;
// Java's regionMatches folds case without the locale,
// https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html); the attribute name always has the
// compared length. java.util.Date is DateTimeOffset (codec-base DateFormatter, which parses the header region through a
// StringCharSequence view), Date.getTime() is ToUnixTimeMilliseconds() and System.currentTimeMillis() is
// DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
// (https://learn.microsoft.com/en-us/dotnet/api/system.datetimeoffset.tounixtimemilliseconds?view=net-10.0, as handler's
// PcapWriter). Long.parseLong is the core HttpUtil.ParseLong (NumberFormatException is FormatException); Java also throws
// NumberFormatException for a null string ("The first argument is null", parseLong(String, int),
// https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Long.html), where long.Parse throws
// ArgumentNullException (https://learn.microsoft.com/en-us/dotnet/api/system.int64.parse?view=net-10.0), so setMaxAge
// ignores both. The private nested CookieBuilder keeps its Java members (its package-private methods are internal).
public sealed class ClientCookieDecoder : CookieDecoder
{

    /**
     * Strict encoder that validates that name and value chars are in the valid scope
     * defined in RFC6265
     */
    public static readonly ClientCookieDecoder STRICT = new ClientCookieDecoder(true);

    /**
     * Lax instance that doesn't validate name and value
     */
    public static readonly ClientCookieDecoder LAX = new ClientCookieDecoder(false);

    private ClientCookieDecoder(bool strict) : base(strict)
    {
    }

    /**
     * Decodes the specified Set-Cookie HTTP header value into a {@link Cookie}.
     *
     * @return the decoded {@link Cookie}
     */
    public ICookie Decode(string header)
    {
        int headerLen = CheckNotNull(header, "header").Length;

        if (headerLen == 0)
        {
            return null;
        }

        CookieBuilder cookieBuilder = null;

        for (int i = 0;;)
        {

            // Skip spaces and separators.
            for (;;)
            {
                if (i == headerLen)
                {
                    goto loop;
                }

                char c = header[i];
                if (c == ',')
                {
                    // Having multiple cookies in a single Set-Cookie header is
                    // deprecated, modern browsers only parse the first one
                    goto loop;

                }
                else if (c == '\t' || c == '\n' || c == 0x0b || c == '\f'
                        || c == '\r' || c == ' ' || c == ';')
                {
                    i++;
                    continue;
                }

                break;
            }

            int nameBegin = i;
            int nameEnd;
            int valueBegin;
            int valueEnd;

            for (;;)
            {
                char curChar = header[i];
                if (curChar == ';')
                {
                    // NAME; (no value till ';')
                    nameEnd = i;
                    valueBegin = valueEnd = -1;
                    break;

                }
                else if (curChar == '=')
                {
                    // NAME=VALUE
                    nameEnd = i;
                    i++;

                    if (i == headerLen)
                    {
                        // NAME= (empty value, i.e. nothing after '=')
                        valueBegin = valueEnd = 0;
                        break;
                    }

                    valueBegin = i;
                    // NAME=VALUE;
                    int semiPos = header.IndexOf(';', i);
                    valueEnd = i = semiPos > 0 ? semiPos : headerLen;
                    break;
                }
                else
                {
                    i++;
                }

                if (i == headerLen)
                {
                    // NAME (no value till the end of string)
                    nameEnd = headerLen;
                    valueBegin = valueEnd = -1;
                    break;
                }
            }

            if (valueEnd > 0 && header[valueEnd - 1] == ',')
            {
                // old multiple cookies separator, skipping it
                valueEnd--;
            }

            if (cookieBuilder == null)
            {
                // cookie name-value pair
                DefaultCookie cookie = InitCookie(header, nameBegin, nameEnd, valueBegin, valueEnd);

                if (cookie == null)
                {
                    return null;
                }

                cookieBuilder = new CookieBuilder(cookie, header);
            }
            else
            {
                // cookie attribute
                cookieBuilder.AppendAttribute(nameBegin, nameEnd, valueBegin, valueEnd);
            }
        }
        loop:
        return cookieBuilder != null ? cookieBuilder.Cookie() : null;
    }

    private class CookieBuilder
    {

        private readonly string header;
        private readonly DefaultCookie cookie;
        private string domain;
        private string path;
        private long maxAge = long.MinValue;
        private int expiresStart;
        private int expiresEnd;
        private bool secure;
        private bool httpOnly;
        private SameSite sameSite;
        private bool partitioned;

        internal CookieBuilder(DefaultCookie cookie, string header)
        {
            this.cookie = cookie;
            this.header = header;
        }

        private long MergeMaxAgeAndExpires()
        {
            // max age has precedence over expires
            if (maxAge != long.MinValue)
            {
                return maxAge;
            }
            else if (IsValueDefined(expiresStart, expiresEnd))
            {
                DateTimeOffset? expiresDate = DateFormatter.ParseHttpDate(new StringCharSequence(header), expiresStart,
                        expiresEnd);
                if (expiresDate != null)
                {
                    long maxAgeMillis = expiresDate.Value.ToUnixTimeMilliseconds() -
                            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    return maxAgeMillis / 1000 + (maxAgeMillis % 1000 != 0 ? 1 : 0);
                }
            }

            return long.MinValue;
        }

        internal ICookie Cookie()
        {
            cookie.SetDomain(domain);
            cookie.SetPath(path);
            cookie.SetMaxAge(MergeMaxAgeAndExpires());
            cookie.SetSecure(secure);
            cookie.SetHttpOnly(httpOnly);
            cookie.SetSameSite(sameSite);
            cookie.SetPartitioned(partitioned);
            return cookie;
        }

        /**
         * Parse and store a key-value pair. First one is considered to be the
         * cookie name/value. Unknown attribute names are silently discarded.
         *
         * @param keyStart
         *            where the key starts in the header
         * @param keyEnd
         *            where the key ends in the header
         * @param valueStart
         *            where the value starts in the header
         * @param valueEnd
         *            where the value ends in the header
         */
        internal void AppendAttribute(int keyStart, int keyEnd, int valueStart, int valueEnd)
        {
            int length = keyEnd - keyStart;

            if (length == 4)
            {
                Parse4(keyStart, valueStart, valueEnd);
            }
            else if (length == 6)
            {
                Parse6(keyStart, valueStart, valueEnd);
            }
            else if (length == 7)
            {
                Parse7(keyStart, valueStart, valueEnd);
            }
            else if (length == 8)
            {
                Parse8(keyStart, valueStart, valueEnd);
            }
            else if (length == 11)
            {
                Parse11(keyStart);
            }
        }

        private void Parse4(int nameStart, int valueStart, int valueEnd)
        {
            if (RegionMatchesIgnoreCase(header, nameStart, CookieHeaderNames.PATH, 4))
            {
                path = ComputeValue(valueStart, valueEnd);
            }
        }

        private void Parse6(int nameStart, int valueStart, int valueEnd)
        {
            if (RegionMatchesIgnoreCase(header, nameStart, CookieHeaderNames.DOMAIN, 5))
            {
                domain = ComputeValue(valueStart, valueEnd);
            }
            else if (RegionMatchesIgnoreCase(header, nameStart, CookieHeaderNames.SECURE, 5))
            {
                secure = true;
            }
        }

        private void SetMaxAge(string value)
        {
            try
            {
                maxAge = Math.Max(HttpUtil.ParseLong(value), 0L);
            }
            catch (Exception e1) when (e1 is FormatException || e1 is ArgumentNullException)
            {
                // ignore failure to parse -> treat as session cookie
            }
        }

        private void Parse7(int nameStart, int valueStart, int valueEnd)
        {
            if (RegionMatchesIgnoreCase(header, nameStart, CookieHeaderNames.EXPIRES, 7))
            {
                expiresStart = valueStart;
                expiresEnd = valueEnd;
            }
            else if (RegionMatchesIgnoreCase(header, nameStart, CookieHeaderNames.MAX_AGE, 7))
            {
                SetMaxAge(ComputeValue(valueStart, valueEnd));
            }
        }

        private void Parse8(int nameStart, int valueStart, int valueEnd)
        {
            if (RegionMatchesIgnoreCase(header, nameStart, CookieHeaderNames.HTTPONLY, 8))
            {
                httpOnly = true;
            }
            else if (RegionMatchesIgnoreCase(header, nameStart, CookieHeaderNames.SAMESITE, 8))
            {
                sameSite = SameSite.Of(ComputeValue(valueStart, valueEnd));
            }
        }

        private void Parse11(int nameStart)
        {
            if (RegionMatchesIgnoreCase(header, nameStart, CookieHeaderNames.PARTITIONED, 11))
            {
                partitioned = true;
            }
        }

        private static bool IsValueDefined(int valueStart, int valueEnd)
        {
            return valueStart != -1 && valueStart != valueEnd;
        }

        private string ComputeValue(int valueStart, int valueEnd)
        {
            return IsValueDefined(valueStart, valueEnd) ? header.Substring(valueStart, valueEnd - valueStart) : null;
        }

        // CLR: header.regionMatches(true, nameStart, name, 0, length).
        private static bool RegionMatchesIgnoreCase(string header, int nameStart, string name, int length)
        {
            return string.Compare(header, nameStart, name, 0, length, StringComparison.OrdinalIgnoreCase) == 0;
        }
    }
}
