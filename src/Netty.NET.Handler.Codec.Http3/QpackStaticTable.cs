/*
 * Copyright 2020 The Netty Project
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
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: List<Integer> is List<int> (add is Add, get(0) the indexer). CharSequenceMap<List<Integer>> is the ported
// CharSequenceMap<List<int>>, whose get/set are the codec-base DefaultHeaders Get/Set;
// UnsupportedValueConverter.<List<Integer>>instance() is UnsupportedValueConverter<List<int>>.Instance(), so the
// unchecked cast needs no counterpart. The static field initializers run in their textual order, as Java's do, so
// STATIC_INDEX_BY_NAME is created after STATIC_TABLE and length.
internal sealed class QpackStaticTable
{

    internal const int NOT_FOUND = -1;

    /**
     * Special mask used to disambiguate exact pair index from
     * name only index and avoid executing lookup twice. Supposed
     * to be used internally. The value should be large enough
     * not to override bits from static table index (current size
     * of the table is 99 elements).
     */
    internal const int MASK_NAME_REF = 1 << 10;

    /**
     * <a href="https://www.rfc-editor.org/rfc/rfc9204.html#name-static-table-2>Appendix A: Static Table</a>
     */
    private static readonly IReadOnlyList<QpackHeaderField> STATIC_TABLE = new QpackHeaderField[] {
        NewEmptyHeaderField(":authority"),
        NewHeaderField(":path", "/"),
        NewHeaderField("age", "0"),
        NewEmptyHeaderField("content-disposition"),
        NewHeaderField("content-length", "0"),
        NewEmptyHeaderField("cookie"),
        NewEmptyHeaderField("date"),
        NewEmptyHeaderField("etag"),
        NewEmptyHeaderField("if-modified-since"),
        NewEmptyHeaderField("if-none-match"),
        NewEmptyHeaderField("last-modified"),
        NewEmptyHeaderField("link"),
        NewEmptyHeaderField("location"),
        NewEmptyHeaderField("referer"),
        NewEmptyHeaderField("set-cookie"),
        NewHeaderField(":method", "CONNECT"),
        NewHeaderField(":method", "DELETE"),
        NewHeaderField(":method", "GET"),
        NewHeaderField(":method", "HEAD"),
        NewHeaderField(":method", "OPTIONS"),
        NewHeaderField(":method", "POST"),
        NewHeaderField(":method", "PUT"),
        NewHeaderField(":scheme", "http"),
        NewHeaderField(":scheme", "https"),
        NewHeaderField(":status", "103"),
        NewHeaderField(":status", "200"),
        NewHeaderField(":status", "304"),
        NewHeaderField(":status", "404"),
        NewHeaderField(":status", "503"),
        NewHeaderField("accept", "*/*"),
        NewHeaderField("accept", "application/dns-message"),
        NewHeaderField("accept-encoding", "gzip, deflate, br"),
        NewHeaderField("accept-ranges", "bytes"),
        NewHeaderField("access-control-allow-headers", "cache-control"),
        NewHeaderField("access-control-allow-headers", "content-type"),
        NewHeaderField("access-control-allow-origin", "*"),
        NewHeaderField("cache-control", "max-age=0"),
        NewHeaderField("cache-control", "max-age=2592000"),
        NewHeaderField("cache-control", "max-age=604800"),
        NewHeaderField("cache-control", "no-cache"),
        NewHeaderField("cache-control", "no-store"),
        NewHeaderField("cache-control", "public, max-age=31536000"),
        NewHeaderField("content-encoding", "br"),
        NewHeaderField("content-encoding", "gzip"),
        NewHeaderField("content-type", "application/dns-message"),
        NewHeaderField("content-type", "application/javascript"),
        NewHeaderField("content-type", "application/json"),
        NewHeaderField("content-type", "application/x-www-form-urlencoded"),
        NewHeaderField("content-type", "image/gif"),
        NewHeaderField("content-type", "image/jpeg"),
        NewHeaderField("content-type", "image/png"),
        NewHeaderField("content-type", "text/css"),
        NewHeaderField("content-type", "text/html;charset=utf-8"),
        NewHeaderField("content-type", "text/plain"),
        NewHeaderField("content-type", "text/plain;charset=utf-8"),
        NewHeaderField("range", "bytes=0-"),
        NewHeaderField("strict-transport-security", "max-age=31536000"),
        NewHeaderField("strict-transport-security", "max-age=31536000;includesubdomains"),
        NewHeaderField("strict-transport-security", "max-age=31536000;includesubdomains;preload"),
        NewHeaderField("vary", "accept-encoding"),
        NewHeaderField("vary", "origin"),
        NewHeaderField("x-content-type-options", "nosniff"),
        NewHeaderField("x-xss-protection", "1; mode=block"),
        NewHeaderField(":status", "100"),
        NewHeaderField(":status", "204"),
        NewHeaderField(":status", "206"),
        NewHeaderField(":status", "302"),
        NewHeaderField(":status", "400"),
        NewHeaderField(":status", "403"),
        NewHeaderField(":status", "421"),
        NewHeaderField(":status", "425"),
        NewHeaderField(":status", "500"),
        NewEmptyHeaderField("accept-language"),
        NewHeaderField("access-control-allow-credentials", "FALSE"),
        NewHeaderField("access-control-allow-credentials", "TRUE"),
        NewHeaderField("access-control-allow-headers", "*"),
        NewHeaderField("access-control-allow-methods", "get"),
        NewHeaderField("access-control-allow-methods", "get, post, options"),
        NewHeaderField("access-control-allow-methods", "options"),
        NewHeaderField("access-control-expose-headers", "content-length"),
        NewHeaderField("access-control-request-headers", "content-type"),
        NewHeaderField("access-control-request-method", "get"),
        NewHeaderField("access-control-request-method", "post"),
        NewHeaderField("alt-svc", "clear"),
        NewEmptyHeaderField("authorization"),
        NewHeaderField("content-security-policy", "script-src 'none';object-src 'none';base-uri 'none'"),
        NewHeaderField("early-data", "1"),
        NewEmptyHeaderField("expect-ct"),
        NewEmptyHeaderField("forwarded"),
        NewEmptyHeaderField("if-range"),
        NewEmptyHeaderField("origin"),
        NewHeaderField("purpose", "prefetch"),
        NewEmptyHeaderField("server"),
        NewHeaderField("timing-allow-origin", "*"),
        NewHeaderField("upgrade-insecure-requests", "1"),
        NewEmptyHeaderField("user-agent"),
        NewEmptyHeaderField("x-forwarded-for"),
        NewHeaderField("x-frame-options", "deny"),
        NewHeaderField("x-frame-options", "sameorigin")
    };

    /**
     * The number of header fields in the static table.
     */
    internal static readonly int length = STATIC_TABLE.Count;

    private static readonly CharSequenceMap<List<int>> STATIC_INDEX_BY_NAME = CreateMap(length);

    private static QpackHeaderField NewEmptyHeaderField(string name)
    {
        return new QpackHeaderField(AsciiString.Cached(name), AsciiString.EMPTY_STRING);
    }

    private static QpackHeaderField NewHeaderField(string name, string value)
    {
        return new QpackHeaderField(AsciiString.Cached(name), AsciiString.Cached(value));
    }

    /**
     * Return the header field at the given index value.
     * Note that QPACK uses 0-based indexing when HPACK is using 1-based.
     */
    internal static QpackHeaderField GetField(int index)
    {
        return STATIC_TABLE[index];
    }

    /**
     * Returns the lowest index value for the given header field name in the static
     * table. Returns -1 if the header field name is not in the static table.
     */
    internal static int GetIndex(ICharSequence name)
    {
        List<int> index = STATIC_INDEX_BY_NAME.Get(name);
        if (index == null)
        {
            return NOT_FOUND;
        }

        return index[0];
    }

    /**
     * Returns:
     *    a) the index value for the given header field in the static table (when found);
     *    b) the index value for a given name with a single bit masked (no exact match);
     *    c) -1 if name was not found in the static table.
     */
    internal static int FindFieldIndex(ICharSequence name, ICharSequence value)
    {
        List<int> nameIndex = STATIC_INDEX_BY_NAME.Get(name);

        // Early return if name not found in the table.
        if (nameIndex == null)
        {
            return NOT_FOUND;
        }

        // If name was found, check all subsequence elements of the table for exact match.
        foreach (int index in nameIndex)
        {
            QpackHeaderField field = STATIC_TABLE[index];
            if (QpackUtil.EqualsVariableTime(value, field.value))
            {
                return index;
            }
        }

        // No exact match was found but we still can reference the name.
        return nameIndex[0] | MASK_NAME_REF;
    }

    /**
     * Creates a map CharSequenceMap header name to index value to allow quick lookup.
     */
    private static CharSequenceMap<List<int>> CreateMap(int length)
    {
        CharSequenceMap<List<int>> mapping =
            new CharSequenceMap<List<int>>(true, UnsupportedValueConverter<List<int>>.Instance(), length);

        for (int index = 0; index < length; index++)
        {
            QpackHeaderField field = GetField(index);
            List<int> cursor = mapping.Get(field.name);
            if (cursor == null)
            {
                List<int> holder = new List<int>(16);
                holder.Add(index);
                mapping.Set(field.name, holder);
            }
            else
            {
                cursor.Add(index);
            }
        }

        return mapping;
    }

    private QpackStaticTable()
    {
    }
}
