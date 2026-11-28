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
using System.Collections.Immutable;
using System.Text;
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Handler.Codec.Http.Cookie.CookieUtil;

namespace Netty.NET.Handler.Codec.Http.Cookie;

/**
 * A <a href="https://tools.ietf.org/html/rfc6265">RFC6265</a> compliant cookie encoder to be used server side,
 * so some fields are sent (Version is typically ignored).
 *
 * As Netty's Cookie merges Expires and MaxAge into one single field, only Max-Age field is sent.
 *
 * Note that multiple cookies must be sent as separate "Set-Cookie" headers.
 *
 * <pre>
 * // Example
 * {@link HttpResponse} res = ...;
 * res.setHeader("Set-Cookie", {@link ServerCookieEncoder}.encode("JSESSIONID", "1234"));
 * </pre>
 *
 * @see ServerCookieDecoder
 */
// CLR: List<String> is IList<string> and Collections.emptyList() is the immutable ImmutableList<string>.Empty (the core
// maps emptyMap/singletonList to the immutable collections the same way); Map<String, Integer> is Dictionary<string,
// int>, and Map.put(...) != null (a previous mapping existed) is the private Put helper. Cookie... is params ICookie[],
// Collection<? extends Cookie> is the covariant IReadOnlyCollection<ICookie> and Iterable<? extends Cookie> is
// IEnumerable<ICookie> (see ClientCookieEncoder); Iterator.hasNext()/next() is IEnumerator.MoveNext()/Current.
// java.util.Date is DateTimeOffset (codec-base DateFormatter), and new Date(millis) is
// DateTimeOffset.FromUnixTimeMilliseconds, whose range ends at -62,135,596,800,000 and 253,402,300,799,999 ms
// (https://learn.microsoft.com/en-us/dotnet/api/system.datetimeoffset.fromunixtimemilliseconds?view=net-10.0, years 1 to
// 9999); a java.util.Date has no such bound, so an Expires instant beyond it (a Max-Age above about 7,975 years) is
// clamped to the range, where Java prints a year above 9999. System.currentTimeMillis() is
// DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() (see ClientCookieDecoder).
public sealed class ServerCookieEncoder : CookieEncoder
{

    /**
     * Strict encoder that validates that name and value chars are in the valid scope
     * defined in RFC6265, and (for methods that accept multiple cookies) that only
     * one cookie is encoded with any given name. (If multiple cookies have the same
     * name, the last one is the one that is encoded.)
     */
    public static readonly ServerCookieEncoder STRICT = new ServerCookieEncoder(true);

    /**
     * Lax instance that doesn't validate name and value, and that allows multiple
     * cookies with the same name.
     */
    public static readonly ServerCookieEncoder LAX = new ServerCookieEncoder(false);

    private ServerCookieEncoder(bool strict) : base(strict)
    {
    }

    /**
     * Encodes the specified cookie name-value pair into a Set-Cookie header value.
     *
     * @param name the cookie name
     * @param value the cookie value
     * @return a single Set-Cookie header value
     */
    public string Encode(string name, string value)
    {
        return Encode(new DefaultCookie(name, value));
    }

    /**
     * Encodes the specified cookie into a Set-Cookie header value.
     *
     * @param cookie the cookie
     * @return a single Set-Cookie header value
     */
    public string Encode(ICookie cookie)
    {
        string name = CheckNotNull(cookie, "cookie").Name();
        string value = cookie.Value() != null ? cookie.Value() : "";

        ValidateCookie(name, value);

        StringBuilder buf = CookieUtil.StringBuilder();

        if (cookie.Wrap())
        {
            AddQuoted(buf, name, value);
        }
        else
        {
            Add(buf, name, value);
        }

        if (cookie.MaxAge() != long.MinValue)
        {
            Add(buf, CookieHeaderNames.MAX_AGE, cookie.MaxAge());
            DateTimeOffset expires = NewDate(cookie.MaxAge() * 1000 + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            buf.Append(CookieHeaderNames.EXPIRES);
            buf.Append('=');
            DateFormatter.Append(expires, buf);
            buf.Append(';');
            buf.Append(HttpConstants.SP_CHAR);
        }

        if (cookie.Path() != null)
        {
            Add(buf, CookieHeaderNames.PATH, cookie.Path());
        }

        if (cookie.Domain() != null)
        {
            Add(buf, CookieHeaderNames.DOMAIN, cookie.Domain());
        }

        if (cookie.IsSecure())
        {
            Add(buf, CookieHeaderNames.SECURE);
        }

        if (cookie.IsHttpOnly())
        {
            Add(buf, CookieHeaderNames.HTTPONLY);
        }

        if (cookie is DefaultCookie)
        {
            DefaultCookie c = (DefaultCookie) cookie;
            if (c.SameSite() != null)
            {
                Add(buf, CookieHeaderNames.SAMESITE, c.SameSite().Name());
            }

            if (c.IsPartitioned())
            {
                Add(buf, CookieHeaderNames.PARTITIONED);
            }
        }

        return StripTrailingSeparator(buf);
    }

    // CLR: new Date(millis), clamped to the DateTimeOffset range (see the class comment).
    private static DateTimeOffset NewDate(long millis)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(Math.Clamp(millis, -62135596800000L, 253402300799999L));
    }

    /** Deduplicate a list of encoded cookies by keeping only the last instance with a given name.
     *
     * @param encoded The list of encoded cookies.
     * @param nameToLastIndex A map from cookie name to index of last cookie instance.
     * @return The encoded list with all but the last instance of a named cookie.
     */
    private static IList<string> Dedup(IList<string> encoded, Dictionary<string, int> nameToLastIndex)
    {
        bool[] isLastInstance = new bool[encoded.Count];

        foreach (int idx in nameToLastIndex.Values)
        {
            isLastInstance[idx] = true;
        }

        List<string> dedupd = new List<string>(nameToLastIndex.Count);

        for (int i = 0, n = encoded.Count; i < n; i++)
        {
            if (isLastInstance[i])
            {
                dedupd.Add(encoded[i]);
            }
        }

        return dedupd;
    }

    // CLR: Map.put(key, value) != null: true when the map already held a value for the key, which is replaced.
    private static bool Put(Dictionary<string, int> map, string key, int value)
    {
        bool hadPrevious = map.ContainsKey(key);
        map[key] = value;
        return hadPrevious;
    }

    /**
     * Batch encodes cookies into Set-Cookie header values.
     *
     * @param cookies a bunch of cookies
     * @return the corresponding bunch of Set-Cookie headers
     */
    public IList<string> Encode(params ICookie[] cookies)
    {
        if (CheckNotNull(cookies, "cookies").Length == 0)
        {
            return ImmutableList<string>.Empty;
        }

        List<string> encoded = new List<string>(cookies.Length);
        Dictionary<string, int> nameToIndex = strict && cookies.Length > 1 ? new Dictionary<string, int>() : null;
        bool hasDupdName = false;

        for (int i = 0; i < cookies.Length; i++)
        {
            ICookie c = cookies[i];
            encoded.Add(Encode(c));

            if (nameToIndex != null)
            {
                hasDupdName |= Put(nameToIndex, c.Name(), i);
            }
        }

        return hasDupdName ? Dedup(encoded, nameToIndex) : encoded;
    }

    /**
     * Batch encodes cookies into Set-Cookie header values.
     *
     * @param cookies a bunch of cookies
     * @return the corresponding bunch of Set-Cookie headers
     */
    public IList<string> Encode(IReadOnlyCollection<ICookie> cookies)
    {
        if (CheckNotNull(cookies, "cookies").Count == 0)
        {
            return ImmutableList<string>.Empty;
        }

        List<string> encoded = new List<string>(cookies.Count);
        Dictionary<string, int> nameToIndex = strict && cookies.Count > 1 ? new Dictionary<string, int>() : null;
        int i = 0;
        bool hasDupdName = false;

        foreach (ICookie c in cookies)
        {
            encoded.Add(Encode(c));

            if (nameToIndex != null)
            {
                hasDupdName |= Put(nameToIndex, c.Name(), i++);
            }
        }

        return hasDupdName ? Dedup(encoded, nameToIndex) : encoded;
    }

    /**
     * Batch encodes cookies into Set-Cookie header values.
     *
     * @param cookies a bunch of cookies
     * @return the corresponding bunch of Set-Cookie headers
     */
    public IList<string> Encode(IEnumerable<ICookie> cookies)
    {
        using IEnumerator<ICookie> cookiesIt = CheckNotNull(cookies, "cookies").GetEnumerator();
        if (!cookiesIt.MoveNext())
        {
            return ImmutableList<string>.Empty;
        }

        List<string> encoded = new List<string>();
        ICookie firstCookie = cookiesIt.Current;
        bool hasNext = cookiesIt.MoveNext();
        Dictionary<string, int> nameToIndex = strict && hasNext ? new Dictionary<string, int>() : null;
        int i = 0;
        encoded.Add(Encode(firstCookie));
        bool hasDupdName = nameToIndex != null && Put(nameToIndex, firstCookie.Name(), i++);

        while (hasNext)
        {
            ICookie c = cookiesIt.Current;
            encoded.Add(Encode(c));

            if (nameToIndex != null)
            {
                hasDupdName |= Put(nameToIndex, c.Name(), i++);
            }

            hasNext = cookiesIt.MoveNext();
        }

        return hasDupdName ? Dedup(encoded, nameToIndex) : encoded;
    }
}
