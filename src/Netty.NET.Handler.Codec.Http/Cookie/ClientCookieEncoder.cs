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

using System.Collections.Generic;
using System.Linq;
using System.Text;
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Handler.Codec.Http.Cookie.CookieUtil;

namespace Netty.NET.Handler.Codec.Http.Cookie;

/**
 * A <a href="https://tools.ietf.org/html/rfc6265">RFC6265</a> compliant cookie encoder to be used client side, so
 * only name=value pairs are sent.
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
// CLR: Cookie... is params ICookie[]; Collection<? extends Cookie> is IReadOnlyCollection<ICookie> and Iterable<?
// extends Cookie> is IEnumerable<ICookie>, both covariant ("This type parameter is covariant",
// https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.ireadonlycollection-1?view=net-10.0), so
// a List<DefaultCookie> binds as Java's wildcard allows; a collection type without IReadOnlyCollection<T>
// (ISet<T>) takes the Iterable overload, which encodes the same text. Comparator<Cookie> is IComparer<ICookie>,
// and the anonymous comparator is the private nested CookieComparator. Arrays.sort(T[], Comparator) "is guaranteed
// to be stable" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Arrays.html), while
// Array.Sort "performs an unstable sort"
// (https://learn.microsoft.com/en-us/dotnet/api/system.array.sort?view=net-10.0), so the copies are sorted with
// Enumerable.OrderBy, which "performs a stable sort"
// (https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.orderby?view=net-10.0); the creation order
// of cookies with paths of the same length is kept as the Java comment requires. The private encode(StringBuilder,
// Cookie) is Encode(StringBuilder, ICookie).
public sealed class ClientCookieEncoder : CookieEncoder
{

    /**
     * Strict encoder that validates that name and value chars are in the valid scope and (for methods that accept
     * multiple cookies) sorts cookies into order of decreasing path length, as specified in RFC6265.
     */
    public static readonly ClientCookieEncoder STRICT = new ClientCookieEncoder(true);

    /**
     * Lax instance that doesn't validate name and value, and (for methods that accept multiple cookies) keeps
     * cookies in the order in which they were given.
     */
    public static readonly ClientCookieEncoder LAX = new ClientCookieEncoder(false);

    private ClientCookieEncoder(bool strict) : base(strict)
    {
    }

    /**
     * Encodes the specified cookie into a Cookie header value.
     *
     * @param name
     *            the cookie name
     * @param value
     *            the cookie value
     * @return a Rfc6265 style Cookie header value
     */
    public string Encode(string name, string value)
    {
        return Encode(new DefaultCookie(name, value));
    }

    /**
     * Encodes the specified cookie into a Cookie header value.
     *
     * @param cookie the specified cookie
     * @return a Rfc6265 style Cookie header value
     */
    public string Encode(ICookie cookie)
    {
        StringBuilder buf = CookieUtil.StringBuilder();
        Encode(buf, CheckNotNull(cookie, "cookie"));

        return StripTrailingSeparator(buf);
    }

    /**
     * Sort cookies into decreasing order of path length, breaking ties by sorting into increasing chronological
     * order of creation time, as recommended by RFC 6265.
     */
    // package-private for testing only.
    internal static readonly IComparer<ICookie> COOKIE_COMPARATOR = new CookieComparator();

    private sealed class CookieComparator : IComparer<ICookie>
    {
        public int Compare(ICookie c1, ICookie c2)
        {
            string path1 = c1.Path();
            string path2 = c2.Path();
            // Cookies with unspecified path default to the path of the request. We don't
            // know the request path here, but we assume that the length of an unspecified
            // path is longer than any specified path (i.e. pathless cookies come first),
            // because setting cookies with a path longer than the request path is of
            // limited use.
            int len1 = path1 == null ? int.MaxValue : path1.Length;
            int len2 = path2 == null ? int.MaxValue : path2.Length;

            // Rely on Arrays.sort's stability to retain creation order in cases where
            // cookies have same path length.
            return len2 - len1;
        }
    }

    /**
     * Encodes the specified cookies into a single Cookie header value.
     *
     * @param cookies
     *            some cookies
     * @return a Rfc6265 style Cookie header value, null if no cookies are passed.
     */
    public string Encode(params ICookie[] cookies)
    {
        if (CheckNotNull(cookies, "cookies").Length == 0)
        {
            return null;
        }

        StringBuilder buf = CookieUtil.StringBuilder();

        if (strict)
        {
            if (cookies.Length == 1)
            {
                Encode(buf, cookies[0]);
            }
            else
            {
                ICookie[] cookiesSorted = cookies.OrderBy(c => c, COOKIE_COMPARATOR).ToArray();

                foreach (ICookie c in cookiesSorted)
                {
                    Encode(buf, c);
                }
            }
        }
        else
        {
            foreach (ICookie c in cookies)
            {
                Encode(buf, c);
            }
        }

        return StripTrailingSeparatorOrNull(buf);
    }

    /**
     * Encodes the specified cookies into a single Cookie header value.
     *
     * @param cookies
     *            some cookies
     * @return a Rfc6265 style Cookie header value, null if no cookies are passed.
     */
    public string Encode(IReadOnlyCollection<ICookie> cookies)
    {
        if (CheckNotNull(cookies, "cookies").Count == 0)
        {
            return null;
        }

        StringBuilder buf = CookieUtil.StringBuilder();

        if (strict)
        {
            if (cookies.Count == 1)
            {
                Encode(buf, cookies.First());
            }
            else
            {
                ICookie[] cookiesSorted = cookies.OrderBy(c => c, COOKIE_COMPARATOR).ToArray();

                foreach (ICookie c in cookiesSorted)
                {
                    Encode(buf, c);
                }
            }
        }
        else
        {
            foreach (ICookie c in cookies)
            {
                Encode(buf, c);
            }
        }

        return StripTrailingSeparatorOrNull(buf);
    }

    /**
     * Encodes the specified cookies into a single Cookie header value.
     *
     * @param cookies some cookies
     * @return a Rfc6265 style Cookie header value, null if no cookies are passed.
     */
    public string Encode(IEnumerable<ICookie> cookies)
    {
        using IEnumerator<ICookie> cookiesIt = CheckNotNull(cookies, "cookies").GetEnumerator();
        if (!cookiesIt.MoveNext())
        {
            return null;
        }

        StringBuilder buf = CookieUtil.StringBuilder();

        if (strict)
        {
            ICookie firstCookie = cookiesIt.Current;

            if (!cookiesIt.MoveNext())
            {
                Encode(buf, firstCookie);
            }
            else
            {
                List<ICookie> cookiesList = new List<ICookie>();
                cookiesList.Add(firstCookie);

                do
                {
                    cookiesList.Add(cookiesIt.Current);
                }
                while (cookiesIt.MoveNext());

                ICookie[] cookiesSorted = cookiesList.OrderBy(c => c, COOKIE_COMPARATOR).ToArray();

                foreach (ICookie c in cookiesSorted)
                {
                    Encode(buf, c);
                }
            }
        }
        else
        {
            do
            {
                Encode(buf, cookiesIt.Current);
            }
            while (cookiesIt.MoveNext());
        }

        return StripTrailingSeparatorOrNull(buf);
    }

    private void Encode(StringBuilder buf, ICookie c)
    {
        string name = c.Name();
        string value = c.Value() != null ? c.Value() : "";

        ValidateCookie(name, value);

        if (c.Wrap())
        {
            AddQuoted(buf, name, value);
        }
        else
        {
            Add(buf, name, value);
        }
    }
}
