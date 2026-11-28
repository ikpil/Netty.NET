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
using System.Collections.ObjectModel;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http.Cookie;

/**
 * A <a href="https://tools.ietf.org/html/rfc6265">RFC6265</a> compliant cookie decoder to be used server side.
 *
 * Only name and value fields are expected, so old fields are not populated (path, domain, etc).
 *
 * Old <a href="https://tools.ietf.org/html/rfc2965">RFC2965</a> cookies are still supported,
 * old fields will simply be ignored.
 *
 * @see ServerCookieEncoder
 */
// CLR: Set<Cookie> is ISet<ICookie> and List<Cookie> is IList<ICookie>. TreeSet with the natural ordering is SortedSet
// with the default comparer (Comparer<ICookie>.Default, which calls IComparable<ICookie>.CompareTo): the TreeSet
// "performs all element comparisons using its compareTo" and add leaves the set unchanged when an equal element is
// present (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/TreeSet.html), and SortedSet.Add
// "returns false and does not throw" for an element already in the set
// (https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.sortedset-1.add?view=net-10.0), so the first
// cookie of a name wins in both. Collections.unmodifiableList is a ReadOnlyCollection<T> view (as codec-socks
// SocksInitRequest). Collection<? super Cookie> is ICollection<ICookie>. The labeled "break loop" is a goto to a label
// after the loop. String.regionMatches(true, 0, ...) and regionMatches(nameBegin, ...) are string.Compare with
// StringComparison.OrdinalIgnoreCase and Ordinal over the length, where a shorter header does not match (see
// ClientCookieDecoder).
public sealed class ServerCookieDecoder : CookieDecoder
{

    private const string RFC2965_VERSION = "$Version";

    private const string RFC2965_PATH = "$" + CookieHeaderNames.PATH;

    private const string RFC2965_DOMAIN = "$" + CookieHeaderNames.DOMAIN;

    private const string RFC2965_PORT = "$Port";

    /**
     * Strict decoder that validates that name and value chars are in the valid scope
     * defined in RFC6265
     */
    public static readonly ServerCookieDecoder STRICT = new ServerCookieDecoder(true);

    /**
     * Lax instance that doesn't validate name and value
     */
    public static readonly ServerCookieDecoder LAX = new ServerCookieDecoder(false);

    private ServerCookieDecoder(bool strict) : base(strict)
    {
    }

    /**
     * Decodes the specified {@code Cookie} HTTP header value into a {@link Cookie}. Unlike {@link #decode(String)},
     * this includes all cookie values present, even if they have the same name.
     *
     * @return the decoded {@link Cookie}
     */
    public IList<ICookie> DecodeAll(string header)
    {
        List<ICookie> cookies = new List<ICookie>();
        Decode(cookies, header);

        return new ReadOnlyCollection<ICookie>(cookies);
    }

    /**
     * Decodes the specified {@code Cookie} HTTP header value into a {@link Cookie}.
     *
     * @return the decoded {@link Cookie}
     */
    public ISet<ICookie> Decode(string header)
    {
        ISet<ICookie> cookies = new SortedSet<ICookie>();
        Decode(cookies, header);
        return cookies;
    }

    /**
     * Decodes the specified {@code Cookie} HTTP header value into a {@link Cookie}.
     */
    private void Decode(ICollection<ICookie> cookies, string header)
    {
        int headerLen = CheckNotNull(header, "header").Length;

        if (headerLen == 0)
        {
            return;
        }

        int i = 0;

        bool rfc2965Style = false;

        if (string.Compare(header, 0, RFC2965_VERSION, 0, RFC2965_VERSION.Length,
                StringComparison.OrdinalIgnoreCase) == 0)
        {
            // RFC 2965 style cookie, move to after version value
            i = header.IndexOf(';') + 1;
            rfc2965Style = true;
        }

        for (;;)
        {

            // Skip spaces and separators.
            for (;;)
            {
                if (i == headerLen)
                {
                    goto loop;
                }

                char c = header[i];
                if (c == '\t' || c == '\n' || c == 0x0b || c == '\f'
                        || c == '\r' || c == ' ' || c == ',' || c == ';')
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

            if (rfc2965Style && (RegionMatches(header, nameBegin, RFC2965_PATH) ||
                    RegionMatches(header, nameBegin, RFC2965_DOMAIN) ||
                    RegionMatches(header, nameBegin, RFC2965_PORT)))
            {

                // skip obsolete RFC2965 fields
                continue;
            }

            DefaultCookie cookie = InitCookie(header, nameBegin, nameEnd, valueBegin, valueEnd);
            if (cookie != null)
            {
                cookies.Add(cookie);
            }
        }
        loop:
        ;
    }

    // CLR: header.regionMatches(toffset, other, 0, other.length()).
    private static bool RegionMatches(string header, int toffset, string other)
    {
        return string.Compare(header, toffset, other, 0, other.Length, StringComparison.Ordinal) == 0;
    }
}
