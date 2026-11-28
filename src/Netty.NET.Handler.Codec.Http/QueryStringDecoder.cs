/*
 * Copyright 2012 The Netty Project
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
using System.Globalization;
using System.Text;
using Netty.NET.Common.Collections;
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Common.Internal.StringUtil;

namespace Netty.NET.Handler.Codec.Http;

/**
 * Splits an HTTP query string into a path string and key-value parameter pairs.
 * This decoder is for one time use only.  Create a new instance for each URI:
 * <pre>
 * {@link QueryStringDecoder} decoder = new {@link QueryStringDecoder}("/hello?recipient=world&x=1;y=2");
 * assert decoder.path().equals("/hello");
 * assert decoder.parameters().get("recipient").get(0).equals("world");
 * assert decoder.parameters().get("x").get(0).equals("1");
 * assert decoder.parameters().get("y").get(0).equals("2");
 * </pre>
 *
 * This decoder can also decode the content of an HTTP POST request whose
 * content type is <tt>application/x-www-form-urlencoded</tt>:
 * <pre>
 * {@link QueryStringDecoder} decoder = new {@link QueryStringDecoder}("recipient=world&x=1;y=2", false);
 * ...
 * </pre>
 *
 * <h3>HashDOS vulnerability fix</h3>
 *
 * As a workaround to the <a href="https://netty.io/s/hashdos">HashDOS</a> vulnerability, the decoder
 * limits the maximum number of decoded key-value parameter pairs, up to {@literal 1024} by
 * default, and you can configure it when you construct the decoder by passing an additional
 * integer parameter.
 *
 * @see QueryStringEncoder
 */
// CLR: java.nio.charset.Charset is System.Text.Encoding and new String(bytes, 0, n, charset) is
// Encoding.GetString, whose default replacement fallback replaces a malformed sequence as the JDK decoder does.
// java.net.URI is System.Uri: URI.getRawPath()/getRawQuery() return the components exactly as they appear in the
// source string, while Uri.AbsolutePath normalizes them (for example "http://localhost:8080?a=b" gets the path
// "/"), so RawPathAndQuery reads the path and query of Uri.OriginalString with the RFC 3986 generic syntax (scheme
// ":" ["//" authority] path ["?" query] ["#" fragment]); an opaque URI (a scheme not followed by '/') has neither,
// as java.net.URI reports null for both
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/URI.html). Map<String, List<String>> is
// IDictionary<string, IList<string>>: LinkedHashMap is the Common LinkedHashMap (insertion order),
// Collections.emptyMap() is ImmutableDictionary.Empty and Collections.singletonList(value) is a one-element
// ImmutableList (both read-only, as the Java collections are); "values instanceof ArrayList" is "values is
// List<string>". The labeled "break loop" is a goto to a label after the loop, and the fall-through from ';' to
// '&' is goto case. PlatformDependent.allocateUninitializedArray is GC.AllocateUninitializedArray. The static
// builder() clashes with the nested Builder type (CS0102) and takes a trailing underscore, Builder_() (owner
// rule). The private constructor and fields of the nested Builder that the decoder uses are internal.
// IllegalArgumentException is ArgumentException and numbers in messages use the invariant culture.
public class QueryStringDecoder
{

    private const int DEFAULT_MAX_PARAMS = 1024;

    private readonly Encoding charset;
    private readonly string uri;
    private readonly int maxParams;
    private readonly bool semicolonIsNormalChar;
    private readonly bool htmlQueryDecoding;
    private int pathEndIdx;
    private string path;
    private IDictionary<string, IList<string>> @params;

    /**
     * Creates a new decoder that decodes the specified URI. The decoder will
     * assume that the query string is encoded in UTF-8.
     */
    public QueryStringDecoder(string uri) : this(Builder_(), uri)
    {
    }

    /**
     * Creates a new decoder that decodes the specified URI encoded in the
     * specified charset.
     */
    public QueryStringDecoder(string uri, bool hasPath) : this(Builder_().HasPath(hasPath), uri)
    {
    }

    /**
     * Creates a new decoder that decodes the specified URI encoded in the
     * specified charset.
     */
    public QueryStringDecoder(string uri, Encoding charset) : this(Builder_().Charset(charset), uri)
    {
    }

    /**
     * Creates a new decoder that decodes the specified URI encoded in the
     * specified charset.
     */
    public QueryStringDecoder(string uri, Encoding charset, bool hasPath)
        : this(Builder_().HasPath(hasPath).Charset(charset), uri)
    {
    }

    /**
     * Creates a new decoder that decodes the specified URI encoded in the
     * specified charset.
     */
    public QueryStringDecoder(string uri, Encoding charset, bool hasPath, int maxParams)
        : this(Builder_().HasPath(hasPath).Charset(charset).MaxParams(maxParams), uri)
    {
    }

    /**
     * Creates a new decoder that decodes the specified URI encoded in the
     * specified charset.
     */
    public QueryStringDecoder(string uri, Encoding charset, bool hasPath,
                              int maxParams, bool semicolonIsNormalChar)
        : this(
                Builder_()
                        .HasPath(hasPath)
                        .Charset(charset)
                        .MaxParams(maxParams)
                        .SemicolonIsNormalChar(semicolonIsNormalChar),
                uri)
    {
    }

    /**
     * Creates a new decoder that decodes the specified URI. The decoder will
     * assume that the query string is encoded in UTF-8.
     */
    public QueryStringDecoder(Uri uri) : this(Builder_(), uri)
    {
    }

    /**
     * Creates a new decoder that decodes the specified URI encoded in the
     * specified charset.
     */
    public QueryStringDecoder(Uri uri, Encoding charset) : this(Builder_().Charset(charset), uri)
    {
    }

    /**
     * Creates a new decoder that decodes the specified URI encoded in the
     * specified charset.
     */
    public QueryStringDecoder(Uri uri, Encoding charset, int maxParams)
        : this(Builder_().Charset(charset).MaxParams(maxParams), uri)
    {
    }

    /**
     * Creates a new decoder that decodes the specified URI encoded in the
     * specified charset.
     */
    public QueryStringDecoder(Uri uri, Encoding charset, int maxParams, bool semicolonIsNormalChar)
        : this(Builder_().Charset(charset).MaxParams(maxParams).SemicolonIsNormalChar(semicolonIsNormalChar), uri)
    {
    }

    private QueryStringDecoder(Builder builder, string uri)
    {
        this.uri = CheckNotNull(uri, "uri");
        this.charset = CheckNotNull(builder.charset, "charset");
        this.maxParams = CheckPositive(builder.maxParams, "maxParams");
        this.semicolonIsNormalChar = builder.semicolonIsNormalChar;
        this.htmlQueryDecoding = builder.htmlQueryDecoding;

        // `-1` means that path end index will be initialized lazily
        pathEndIdx = builder.hasPath ? -1 : 0;
    }

    private QueryStringDecoder(Builder builder, Uri uri)
    {
        RawPathAndQuery(uri, out string rawPath, out string rawQuery);

        if (rawPath == null)
        {
            rawPath = EMPTY_STRING;
        }

        // Also take care of cut of things like "http://localhost"
        this.uri = rawQuery == null? rawPath : rawPath + '?' + rawQuery;
        this.charset = CheckNotNull(builder.charset, "charset");
        this.maxParams = CheckPositive(builder.maxParams, "maxParams");
        this.semicolonIsNormalChar = builder.semicolonIsNormalChar;
        this.htmlQueryDecoding = builder.htmlQueryDecoding;
        pathEndIdx = rawPath.Length;
    }

    // CLR: URI.getRawPath() and URI.getRawQuery() of the source string (see the class comment).
    private static void RawPathAndQuery(Uri uri, out string rawPath, out string rawQuery)
    {
        string s = uri.OriginalString;
        int fragment = s.IndexOf('#');
        if (fragment != -1)
        {
            s = s.Substring(0, fragment);
        }

        int start = 0;
        int i = 0;

        while (i < s.Length && (char.IsAsciiLetterOrDigit(s[i]) || s[i] == '+' || s[i] == '-' || s[i] == '.'))
        {
            i++;
        }

        if (i > 0 && i < s.Length && s[i] == ':' && char.IsAsciiLetter(s[0]))
        {
            // scheme ":" hier-part
            start = i + 1;
            if (start >= s.Length || s[start] != '/')
            {
                // An opaque URI has no path and no query.
                rawPath = null;
                rawQuery = null;
                return;
            }

            if (start + 1 < s.Length && s[start + 1] == '/')
            {
                // "//" authority
                start += 2;

                while (start < s.Length && s[start] != '/' && s[start] != '?')
                {
                    start++;
                }
            }
        }
        else if (s.StartsWith("//", StringComparison.Ordinal))
        {
            // A network-path reference: "//" authority path.
            start = 2;

            while (start < s.Length && s[start] != '/' && s[start] != '?')
            {
                start++;
            }
        }

        int query = s.IndexOf('?', start);
        if (query == -1)
        {
            rawPath = s.Substring(start);
            rawQuery = null;
        }
        else
        {
            rawPath = s.Substring(start, query - start);
            rawQuery = s.Substring(query + 1);
        }
    }

    public override string ToString()
    {
        return Uri();
    }

    /**
     * Returns the uri used to initialize this {@link QueryStringDecoder}.
     */
    public string Uri()
    {
        return uri;
    }

    /**
     * Returns the decoded path string of the URI.
     */
    public string Path()
    {
        if (path == null)
        {
            path = DecodeComponent(uri, 0, PathEndIdx(), charset, false);
        }

        return path;
    }

    /**
     * Returns the decoded key-value parameter pairs of the URI.
     */
    public IDictionary<string, IList<string>> Parameters()
    {
        if (@params == null)
        {
            @params = DecodeParams(uri, PathEndIdx(), charset, maxParams);
        }

        return @params;
    }

    /**
     * Returns the raw path string of the URI.
     */
    public string RawPath()
    {
        return uri.Substring(0, PathEndIdx());
    }

    /**
     * Returns raw query string of the URI.
     */
    public string RawQuery()
    {
        int start = PathEndIdx() + 1;
        return start < uri.Length ? uri.Substring(start) : EMPTY_STRING;
    }

    private int PathEndIdx()
    {
        if (pathEndIdx == -1)
        {
            pathEndIdx = FindPathEndIndex(uri);
        }

        return pathEndIdx;
    }

    private IDictionary<string, IList<string>> DecodeParams(string s, int from, Encoding charset, int paramsLimit)
    {
        int len = s.Length;
        if (from >= len)
        {
            return ImmutableDictionary<string, IList<string>>.Empty;
        }

        if (s[from] == '?')
        {
            from++;
        }

        IDictionary<string, IList<string>> @params = new LinkedHashMap<string, IList<string>>();
        int nameStart = from;
        int valueStart = -1;
        int i;

        for (i = from; i < len; i++)
        {
            switch (s[i])
            {
            case '=':
                if (nameStart == i)
                {
                    nameStart = i + 1;
                }
                else if (valueStart < nameStart)
                {
                    valueStart = i + 1;
                }

                break;
            case ';':
                if (semicolonIsNormalChar)
                {
                    continue;
                }

                // fall-through
                goto case '&';
            case '&':
                if (AddParam(s, nameStart, valueStart, i, @params, charset))
                {
                    paramsLimit--;
                    if (paramsLimit == 0)
                    {
                        return @params;
                    }
                }

                nameStart = i + 1;
                break;
            case '#':
                goto loop;
            default:
                // continue
                break;
            }
        }
        loop:
        AddParam(s, nameStart, valueStart, i, @params, charset);
        return @params;
    }

    private bool AddParam(string s, int nameStart, int valueStart, int valueEnd,
                                    IDictionary<string, IList<string>> @params, Encoding charset)
    {
        if (nameStart >= valueEnd)
        {
            return false;
        }

        if (valueStart <= nameStart)
        {
            valueStart = valueEnd + 1;
        }

        string name = DecodeComponent(s, nameStart, valueStart - 1, charset, htmlQueryDecoding);
        string value = DecodeComponent(s, valueStart, valueEnd, charset, htmlQueryDecoding);
        @params.TryGetValue(name, out IList<string> values);

        if (values == null)
        {
            @params[name] = ImmutableList.Create(value);
        }
        else if (values is List<string>)
        {
            values.Add(value);
        }
        else
        {
            IList<string> newValues = new List<string>(2);
            newValues.Add(values[0]);
            newValues.Add(value);
            @params[name] = newValues;
        }

        return true;
    }

    /**
     * Decodes a bit of a URL encoded by a browser.
     * <p>
     * This is equivalent to calling {@link #decodeComponent(String, Charset)}
     * with the UTF-8 charset (recommended to comply with RFC 3986, Section 2).
     * @param s The string to decode (can be empty).
     * @return The decoded string, or {@code s} if there's nothing to decode.
     * If the string to decode is {@code null}, returns an empty string.
     * @throws IllegalArgumentException if the string contains a malformed
     * escape sequence.
     */
    public static string DecodeComponent(string s)
    {
        return DecodeComponent(s, HttpConstants.DEFAULT_CHARSET);
    }

    /**
     * Decodes a bit of a URL encoded by a browser.
     * <p>
     * The string is expected to be encoded as per RFC 3986, Section 2.
     * This is the encoding used by JavaScript functions {@code encodeURI}
     * and {@code encodeURIComponent}, but not {@code escape}.  For example
     * in this encoding, &eacute; (in Unicode {@code U+00E9} or in UTF-8
     * {@code 0xC3 0xA9}) is encoded as {@code %C3%A9} or {@code %c3%a9}.
     * <p>
     * This is essentially equivalent to calling
     *   {@link URLDecoder#decode(String, String)}
     * except that it's over 2x faster and generates less garbage for the GC.
     * Actually this function doesn't allocate any memory if there's nothing
     * to decode, the argument itself is returned.
     * @param s The string to decode (can be empty).
     * @param charset The charset to use to decode the string (should really
     * be {@link CharsetUtil#UTF_8}.
     * @return The decoded string, or {@code s} if there's nothing to decode.
     * If the string to decode is {@code null}, returns an empty string.
     * @throws IllegalArgumentException if the string contains a malformed
     * escape sequence.
     */
    public static string DecodeComponent(string s, Encoding charset)
    {
        if (s == null)
        {
            return EMPTY_STRING;
        }

        return DecodeComponent(s, 0, s.Length, charset, true);
    }

    private static string DecodeComponent(string s, int from, int toExcluded, Encoding charset, bool plusToSpace)
    {
        int len = toExcluded - from;
        if (len <= 0)
        {
            return EMPTY_STRING;
        }

        int firstEscaped = -1;

        for (int i = from; i < toExcluded; i++)
        {
            char c = s[i];
            if (c == '%' || (c == '+' && plusToSpace))
            {
                firstEscaped = i;
                break;
            }
        }

        if (firstEscaped == -1)
        {
            return s.Substring(from, toExcluded - from);
        }

        // Each encoded byte takes 3 characters (e.g. "%20")
        int decodedCapacity = (toExcluded - firstEscaped) / 3;
        byte[] buf = GC.AllocateUninitializedArray<byte>(decodedCapacity);
        int bufIdx;

        StringBuilder strBuf = new StringBuilder(len);
        strBuf.Append(s, from, firstEscaped - from);

        for (int i = firstEscaped; i < toExcluded; i++)
        {
            char c = s[i];
            if (c != '%')
            {
                strBuf.Append(c != '+' || !plusToSpace ? c : SPACE);
                continue;
            }

            bufIdx = 0;

            do
            {
                if (i + 3 > toExcluded)
                {
                    throw new ArgumentException("unterminated escape sequence at index " +
                            i.ToString(CultureInfo.InvariantCulture) + " of: " + s);
                }

                buf[bufIdx++] = DecodeHexByte(s, i + 1);
                i += 3;
            } while (i < toExcluded && s[i] == '%');

            i--;

            strBuf.Append(charset.GetString(buf, 0, bufIdx));
        }

        return strBuf.ToString();
    }

    private static int FindPathEndIndex(string uri)
    {
        int len = uri.Length;

        for (int i = 0; i < len; i++)
        {
            char c = uri[i];
            if (c == '?' || c == '#')
            {
                return i;
            }
        }

        return len;
    }

    public static Builder Builder_()
    {
        return new Builder();
    }

    public sealed class Builder
    {
        internal bool hasPath = true;
        internal int maxParams = DEFAULT_MAX_PARAMS;
        internal bool semicolonIsNormalChar;
        internal Encoding charset = HttpConstants.DEFAULT_CHARSET;
        internal bool htmlQueryDecoding = true;

        internal Builder()
        {
        }

        /**
         * {@code true} by default. When set to {@code false}, the input string only contains the query component of
         * the URI.
         *
         * @param hasPath Whether the URI contains a path
         * @return This builder
         */
        public Builder HasPath(bool hasPath)
        {
            this.hasPath = hasPath;
            return this;
        }

        /**
         * Maximum number of query parameters allowed, to mitigate HashDOS. {@value DEFAULT_MAX_PARAMS} by default.
         *
         * @param maxParams The maximum number of query parameters
         * @return This builder
         */
        public Builder MaxParams(int maxParams)
        {
            this.maxParams = maxParams;
            return this;
        }

        /**
         * {@code false} by default. If set to {@code true}, instead of allowing query parameters to be separated by
         * semicolons, treat the semicolon as a normal character in a query value.
         *
         * @param semicolonIsNormalChar Whether to treat semicolons as a normal character
         * @return This builder
         */
        public Builder SemicolonIsNormalChar(bool semicolonIsNormalChar)
        {
            this.semicolonIsNormalChar = semicolonIsNormalChar;
            return this;
        }

        /**
         * The charset to use for decoding percent escape sequences. {@link HttpConstants#DEFAULT_CHARSET} by default.
         *
         * @param charset The charset
         * @return This builder
         */
        public Builder Charset(Encoding charset)
        {
            this.charset = charset;
            return this;
        }

        /**
         * RFC 3986 (the URI standard) makes no mention of using '+' to encode a space in a URI query component. The
         * whatwg HTML standard, however, defines the query to be encoded with the
         * {@code application/x-www-form-urlencoded} serializer defined in the whatwg URL standard, which does use '+'
         * to encode a space instead of {@code %20}.
         * <p>This flag controls whether the decoding should happen according to HTML rules, which decodes the '+' to a
         * space. The default is {@code true}.
         *
         * @param htmlQueryDecoding Whether to decode '+' to space
         * @return This builder
         */
        public Builder HtmlQueryDecoding(bool htmlQueryDecoding)
        {
            this.htmlQueryDecoding = htmlQueryDecoding;
            return this;
        }

        /**
         * Create a decoder that will lazily decode the given URI with the settings configured in this builder.
         *
         * @param uri The URI in String form
         * @return The decoder
         */
        public QueryStringDecoder Build(string uri)
        {
            return new QueryStringDecoder(this, uri);
        }

        /**
         * Create a decoder that will lazily decode the given URI with the settings configured in this builder. Note
         * that {@link #hasPath(boolean)} has no effect when using this method.
         *
         * @param uri The already parsed URI
         * @return The decoder
         */
        public QueryStringDecoder Build(Uri uri)
        {
            return new QueryStringDecoder(this, uri);
        }
    }
}
