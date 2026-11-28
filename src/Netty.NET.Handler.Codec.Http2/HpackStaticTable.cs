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

/*
 * Copyright 2014 Twitter, Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using static Netty.NET.Handler.Codec.Http2.IHttp2Headers;
using static Netty.NET.Handler.Codec.Http2.HpackUtil;

namespace Netty.NET.Handler.Codec.Http2;

// CLR: the package-private final class is internal sealed and its package-private members are internal.
// Arrays.asList(...) is an array behind IReadOnlyList (size() is Count, get(i) the indexer). The two static
// initializer blocks are the private InitHeaderNames() and InitHeadersWithNonEmptyValues(), which the static
// constructor runs in the Java order after every field initializer: "A class or struct can only have one static
// constructor", and the static field initializers "run in the textual order in which they appear in the class
// declaration. The initializers run immediately before the static constructor"
// (https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/static-constructors).
// The shifts depend on PlatformDependent.BIG_ENDIAN_NATIVE_ORDER, a static readonly value, so they are static
// readonly fields. AsciiString.hashCode(CharSequence) is the Common AsciiString.HashCode, which keeps Java's
// hashing (PlatformDependent.HashCodeAscii), so each bucket holds one entry as in Java (the static constructor
// throws on a collision). IllegalStateException is InvalidOperationException. The private nested classes'
// fields are internal readonly so the outer class reads them.
internal sealed class HpackStaticTable
{

    internal const int NOT_FOUND = -1;

    // Appendix A: Static Table
    // https://tools.ietf.org/html/rfc7541#appendix-A
    private static readonly IReadOnlyList<HpackHeaderField> STATIC_TABLE = new HpackHeaderField[] {
    /*  1 */ NewEmptyPseudoHeaderField(PseudoHeaderName.AUTHORITY),
    /*  2 */ NewPseudoHeaderMethodField(HttpMethod.GET),
    /*  3 */ NewPseudoHeaderMethodField(HttpMethod.POST),
    /*  4 */ NewPseudoHeaderField(PseudoHeaderName.PATH, "/"),
    /*  5 */ NewPseudoHeaderField(PseudoHeaderName.PATH, "/index.html"),
    /*  6 */ NewPseudoHeaderField(PseudoHeaderName.SCHEME, "http"),
    /*  7 */ NewPseudoHeaderField(PseudoHeaderName.SCHEME, "https"),
    /*  8 */ NewPseudoHeaderField(PseudoHeaderName.STATUS, HttpResponseStatus.OK.CodeAsText()),
    /*  9 */ NewPseudoHeaderField(PseudoHeaderName.STATUS, HttpResponseStatus.NO_CONTENT.CodeAsText()),
    /* 10 */ NewPseudoHeaderField(PseudoHeaderName.STATUS, HttpResponseStatus.PARTIAL_CONTENT.CodeAsText()),
    /* 11 */ NewPseudoHeaderField(PseudoHeaderName.STATUS, HttpResponseStatus.NOT_MODIFIED.CodeAsText()),
    /* 12 */ NewPseudoHeaderField(PseudoHeaderName.STATUS, HttpResponseStatus.BAD_REQUEST.CodeAsText()),
    /* 13 */ NewPseudoHeaderField(PseudoHeaderName.STATUS, HttpResponseStatus.NOT_FOUND.CodeAsText()),
    /* 14 */ NewPseudoHeaderField(PseudoHeaderName.STATUS, HttpResponseStatus.INTERNAL_SERVER_ERROR.CodeAsText()),
    /* 15 */ NewEmptyHeaderField(HttpHeaderNames.ACCEPT_CHARSET),
    /* 16 */ NewHeaderField(HttpHeaderNames.ACCEPT_ENCODING, "gzip, deflate"),
    /* 17 */ NewEmptyHeaderField(HttpHeaderNames.ACCEPT_LANGUAGE),
    /* 18 */ NewEmptyHeaderField(HttpHeaderNames.ACCEPT_RANGES),
    /* 19 */ NewEmptyHeaderField(HttpHeaderNames.ACCEPT),
    /* 20 */ NewEmptyHeaderField(HttpHeaderNames.ACCESS_CONTROL_ALLOW_ORIGIN),
    /* 21 */ NewEmptyHeaderField(HttpHeaderNames.AGE),
    /* 22 */ NewEmptyHeaderField(HttpHeaderNames.ALLOW),
    /* 23 */ NewEmptyHeaderField(HttpHeaderNames.AUTHORIZATION),
    /* 24 */ NewEmptyHeaderField(HttpHeaderNames.CACHE_CONTROL),
    /* 25 */ NewEmptyHeaderField(HttpHeaderNames.CONTENT_DISPOSITION),
    /* 26 */ NewEmptyHeaderField(HttpHeaderNames.CONTENT_ENCODING),
    /* 27 */ NewEmptyHeaderField(HttpHeaderNames.CONTENT_LANGUAGE),
    /* 28 */ NewEmptyHeaderField(HttpHeaderNames.CONTENT_LENGTH),
    /* 29 */ NewEmptyHeaderField(HttpHeaderNames.CONTENT_LOCATION),
    /* 30 */ NewEmptyHeaderField(HttpHeaderNames.CONTENT_RANGE),
    /* 31 */ NewEmptyHeaderField(HttpHeaderNames.CONTENT_TYPE),
    /* 32 */ NewEmptyHeaderField(HttpHeaderNames.COOKIE),
    /* 33 */ NewEmptyHeaderField(HttpHeaderNames.DATE),
    /* 34 */ NewEmptyHeaderField(HttpHeaderNames.ETAG),
    /* 35 */ NewEmptyHeaderField(HttpHeaderNames.EXPECT),
    /* 36 */ NewEmptyHeaderField(HttpHeaderNames.EXPIRES),
    /* 37 */ NewEmptyHeaderField(HttpHeaderNames.FROM),
    /* 38 */ NewEmptyHeaderField(HttpHeaderNames.HOST),
    /* 39 */ NewEmptyHeaderField(HttpHeaderNames.IF_MATCH),
    /* 40 */ NewEmptyHeaderField(HttpHeaderNames.IF_MODIFIED_SINCE),
    /* 41 */ NewEmptyHeaderField(HttpHeaderNames.IF_NONE_MATCH),
    /* 42 */ NewEmptyHeaderField(HttpHeaderNames.IF_RANGE),
    /* 43 */ NewEmptyHeaderField(HttpHeaderNames.IF_UNMODIFIED_SINCE),
    /* 44 */ NewEmptyHeaderField(HttpHeaderNames.LAST_MODIFIED),
    /* 45 */ NewEmptyHeaderField("link"),
    /* 46 */ NewEmptyHeaderField(HttpHeaderNames.LOCATION),
    /* 47 */ NewEmptyHeaderField(HttpHeaderNames.MAX_FORWARDS),
    /* 48 */ NewEmptyHeaderField(HttpHeaderNames.PROXY_AUTHENTICATE),
    /* 49 */ NewEmptyHeaderField(HttpHeaderNames.PROXY_AUTHORIZATION),
    /* 50 */ NewEmptyHeaderField(HttpHeaderNames.RANGE),
    /* 51 */ NewEmptyHeaderField(HttpHeaderNames.REFERER),
    /* 52 */ NewEmptyHeaderField("refresh"),
    /* 53 */ NewEmptyHeaderField(HttpHeaderNames.RETRY_AFTER),
    /* 54 */ NewEmptyHeaderField(HttpHeaderNames.SERVER),
    /* 55 */ NewEmptyHeaderField(HttpHeaderNames.SET_COOKIE),
    /* 56 */ NewEmptyHeaderField("strict-transport-security"),
    /* 57 */ NewEmptyHeaderField(HttpHeaderNames.TRANSFER_ENCODING),
    /* 58 */ NewEmptyHeaderField(HttpHeaderNames.USER_AGENT),
    /* 59 */ NewEmptyHeaderField(HttpHeaderNames.VARY),
    /* 60 */ NewEmptyHeaderField(HttpHeaderNames.VIA),
    /* 61 */ NewEmptyHeaderField(HttpHeaderNames.WWW_AUTHENTICATE)
    };

    private static HpackHeaderField NewEmptyHeaderField(AsciiString name)
    {
        return new HpackHeaderField(name, AsciiString.EMPTY_STRING);
    }

    private static HpackHeaderField NewEmptyHeaderField(string name)
    {
        return new HpackHeaderField(AsciiString.Cached(name), AsciiString.EMPTY_STRING);
    }

    private static HpackHeaderField NewHeaderField(AsciiString name, string value)
    {
        return new HpackHeaderField(name, AsciiString.Cached(value));
    }

    private static HpackHeaderField NewPseudoHeaderMethodField(HttpMethod method)
    {
        return new HpackHeaderField(PseudoHeaderName.METHOD.Value(), method.AsciiName());
    }

    private static HpackHeaderField NewPseudoHeaderField(PseudoHeaderName name, AsciiString value)
    {
        return new HpackHeaderField(name.Value(), value);
    }

    private static HpackHeaderField NewPseudoHeaderField(PseudoHeaderName name, string value)
    {
        return new HpackHeaderField(name.Value(), AsciiString.Cached(value));
    }

    private static HpackHeaderField NewEmptyPseudoHeaderField(PseudoHeaderName name)
    {
        return new HpackHeaderField(name.Value(), AsciiString.EMPTY_STRING);
    }

    // The table size and bit shift are chosen so that each hash bucket contains a single header name.
    private const int HEADER_NAMES_TABLE_SIZE = 1 << 9;

    private static readonly int HEADER_NAMES_TABLE_SHIFT = PlatformDependent.BIG_ENDIAN_NATIVE_ORDER ? 22 : 18;

    // A table mapping header names to their associated indexes.
    private static readonly HeaderNameIndex[] HEADER_NAMES = new HeaderNameIndex[HEADER_NAMES_TABLE_SIZE];

    static HpackStaticTable()
    {
        InitHeaderNames();
        InitHeadersWithNonEmptyValues();
    }

    private static void InitHeaderNames()
    {
        // Iterate through the static table in reverse order to
        // save the smallest index for a given name in the table.
        for (int index = STATIC_TABLE.Count; index > 0; index--)
        {
            HpackHeaderField entry = GetEntry(index);
            int bucket = HeaderNameBucket(entry.name);
            HeaderNameIndex tableEntry = HEADER_NAMES[bucket];
            if (tableEntry != null && !EqualsVariableTime(tableEntry.name, entry.name))
            {
                // Can happen if AsciiString.hashCode changes
                throw new InvalidOperationException("Hash bucket collision between " +
                  tableEntry.name + " and " + entry.name);
            }

            HEADER_NAMES[bucket] = new HeaderNameIndex(entry.name, index, entry.value.Length() == 0);
        }
    }

    // The table size and bit shift are chosen so that each hash bucket contains a single header.
    private const int HEADERS_WITH_NON_EMPTY_VALUES_TABLE_SIZE = 1 << 6;

    private static readonly int HEADERS_WITH_NON_EMPTY_VALUES_TABLE_SHIFT =
      PlatformDependent.BIG_ENDIAN_NATIVE_ORDER ? 0 : 6;

    // A table mapping headers with non-empty values to their associated indexes.
    private static readonly HeaderIndex[] HEADERS_WITH_NON_EMPTY_VALUES =
      new HeaderIndex[HEADERS_WITH_NON_EMPTY_VALUES_TABLE_SIZE];

    private static void InitHeadersWithNonEmptyValues()
    {
        for (int index = STATIC_TABLE.Count; index > 0; index--)
        {
            HpackHeaderField entry = GetEntry(index);
            if (entry.value.Length() > 0)
            {
                int bucket = HeaderBucket(entry.value);
                HeaderIndex tableEntry = HEADERS_WITH_NON_EMPTY_VALUES[bucket];
                if (tableEntry != null)
                {
                    // Can happen if AsciiString.hashCode changes
                    throw new InvalidOperationException("Hash bucket collision between " +
                      tableEntry.value + " and " + entry.value);
                }

                HEADERS_WITH_NON_EMPTY_VALUES[bucket] = new HeaderIndex(entry.name, entry.value, index);
            }
        }
    }

    /**
     * The number of header fields in the static table.
     */
    internal static readonly int length = STATIC_TABLE.Count;

    /**
     * Return the header field at the given index value.
     */
    internal static HpackHeaderField GetEntry(int index)
    {
        return STATIC_TABLE[index - 1];
    }

    /**
     * Returns the lowest index value for the given header field name in the static table. Returns
     * -1 if the header field name is not in the static table.
     */
    internal static int GetIndex(ICharSequence name)
    {
        HeaderNameIndex entry = GetEntry(name);
        return entry == null ? NOT_FOUND : entry.index;
    }

    /**
     * Returns the index value for the given header field in the static table. Returns -1 if the
     * header field is not in the static table.
     */
    internal static int GetIndexInsensitive(ICharSequence name, ICharSequence value)
    {
        if (value.Length() == 0)
        {
            HeaderNameIndex entry = GetEntry(name);
            return entry == null || !entry.emptyValue ? NOT_FOUND : entry.index;
        }

        int bucket = HeaderBucket(value);
        HeaderIndex header = HEADERS_WITH_NON_EMPTY_VALUES[bucket];
        if (header == null)
        {
            return NOT_FOUND;
        }
        if (EqualsVariableTime(header.name, name) && EqualsVariableTime(header.value, value))
        {
            return header.index;
        }

        return NOT_FOUND;
    }

    private static HeaderNameIndex GetEntry(ICharSequence name)
    {
        int bucket = HeaderNameBucket(name);
        HeaderNameIndex entry = HEADER_NAMES[bucket];
        if (entry == null)
        {
            return null;
        }

        return EqualsVariableTime(entry.name, name) ? entry : null;
    }

    private static int HeaderNameBucket(ICharSequence name)
    {
        return Bucket(name, HEADER_NAMES_TABLE_SHIFT, HEADER_NAMES_TABLE_SIZE - 1);
    }

    private static int HeaderBucket(ICharSequence value)
    {
        return Bucket(value, HEADERS_WITH_NON_EMPTY_VALUES_TABLE_SHIFT, HEADERS_WITH_NON_EMPTY_VALUES_TABLE_SIZE - 1);
    }

    private static int Bucket(ICharSequence s, int shift, int mask)
    {
        return (AsciiString.HashCode(s) >> shift) & mask;
    }

    private sealed class HeaderNameIndex
    {
        internal readonly ICharSequence name;
        internal readonly int index;
        internal readonly bool emptyValue;

        internal HeaderNameIndex(ICharSequence name, int index, bool emptyValue)
        {
            this.name = name;
            this.index = index;
            this.emptyValue = emptyValue;
        }
    }

    private sealed class HeaderIndex
    {
        internal readonly ICharSequence name;
        internal readonly ICharSequence value;
        internal readonly int index;

        internal HeaderIndex(ICharSequence name, ICharSequence value, int index)
        {
            this.name = name;
            this.value = value;
            this.index = index;
        }
    }

    // singleton
    private HpackStaticTable()
    {
    }
}
