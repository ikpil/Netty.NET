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
using System.Collections;
using System.Globalization;
using System.Text;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http.Cookie;

// CLR: BitSet.get(int) is false for a bit that was never set, also past the set's size (it "grows as needed" and
// all bits are initially false,
// https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/BitSet.html), whereas BitArray.Get(int)
// throws ArgumentOutOfRangeException when the index is not less than the number of elements
// (https://learn.microsoft.com/en-us/dotnet/api/system.collections.bitarray.get?view=net-10.0), so
// firstInvalidOctet checks the length first (as codec-base DateFormatter.IsDelim does). IllegalArgumentException
// is ArgumentException. StringBuilder.append(long) is the invariant-culture text of the number (Java text
// conversions keep Java results, docs/codec-compression-porting.md).
internal static class CookieUtil
{

    private static readonly BitArray VALID_COOKIE_NAME_OCTETS = ValidCookieNameOctets();

    private static readonly BitArray VALID_COOKIE_VALUE_OCTETS = ValidCookieValueOctets();

    private static readonly BitArray VALID_COOKIE_ATTRIBUTE_VALUE_OCTETS = ValidCookieAttributeValueOctets();

    // token = 1*<any CHAR except CTLs or separators>
    // separators = "(" | ")" | "<" | ">" | "@"
    // | "," | ";" | ":" | "\" | <">
    // | "/" | "[" | "]" | "?" | "="
    // | "{" | "}" | SP | HT
    private static BitArray ValidCookieNameOctets()
    {
        BitArray bits = new BitArray(0x80);

        for (int i = 32; i < 127; i++)
        {
            bits.Set(i, true);
        }

        int[] separators = new int[]
                { '(', ')', '<', '>', '@', ',', ';', ':', '\\', '"', '/', '[', ']', '?', '=', '{', '}', ' ', '\t' };

        foreach (int separator in separators)
        {
            bits.Set(separator, false);
        }

        return bits;
    }

    // cookie-octet = %x21 / %x23-2B / %x2D-3A / %x3C-5B / %x5D-7E
    // US-ASCII characters excluding CTLs, whitespace, DQUOTE, comma, semicolon, and backslash
    private static BitArray ValidCookieValueOctets()
    {
        BitArray bits = new BitArray(0x80);
        bits.Set(0x21, true);

        for (int i = 0x23; i <= 0x2B; i++)
        {
            bits.Set(i, true);
        }

        for (int i = 0x2D; i <= 0x3A; i++)
        {
            bits.Set(i, true);
        }

        for (int i = 0x3C; i <= 0x5B; i++)
        {
            bits.Set(i, true);
        }

        for (int i = 0x5D; i <= 0x7E; i++)
        {
            bits.Set(i, true);
        }

        return bits;
    }

    // path-value        = <any CHAR except CTLs or ";">
    private static BitArray ValidCookieAttributeValueOctets()
    {
        BitArray bits = new BitArray(0x80);

        for (int i = 32; i < 127; i++)
        {
            bits.Set(i, true);
        }

        bits.Set(';', false);
        return bits;
    }

    // CLR: InternalThreadLocalMap.get().stringBuilder() is a JVM allocation cache that the Common port retired;
    // its cookie users take an operation-owned StringBuilder. The callers in this namespace name the method
    // CookieUtil.StringBuilder(), because a using static import of it would be ambiguous with the
    // System.Text.StringBuilder type.
    internal static StringBuilder StringBuilder()
    {
        return new StringBuilder();
    }

    /**
     * @param buf a buffer where some cookies were maybe encoded
     * @return the buffer String without the trailing separator, or null if no cookie was appended.
     */
    internal static string StripTrailingSeparatorOrNull(StringBuilder buf)
    {
        return buf.Length == 0 ? null : StripTrailingSeparator(buf);
    }

    internal static string StripTrailingSeparator(StringBuilder buf)
    {
        if (buf.Length > 0)
        {
            buf.Length = buf.Length - 2;
        }

        return buf.ToString();
    }

    internal static void Add(StringBuilder sb, string name, long val)
    {
        sb.Append(name);
        sb.Append('=');
        sb.Append(val.ToString(CultureInfo.InvariantCulture));
        sb.Append(';');
        sb.Append(HttpConstants.SP_CHAR);
    }

    internal static void Add(StringBuilder sb, string name, string val)
    {
        sb.Append(name);
        sb.Append('=');
        sb.Append(val);
        sb.Append(';');
        sb.Append(HttpConstants.SP_CHAR);
    }

    internal static void Add(StringBuilder sb, string name)
    {
        sb.Append(name);
        sb.Append(';');
        sb.Append(HttpConstants.SP_CHAR);
    }

    internal static void AddQuoted(StringBuilder sb, string name, string val)
    {
        if (val == null)
        {
            val = "";
        }

        sb.Append(name);
        sb.Append('=');
        sb.Append('"');
        sb.Append(val);
        sb.Append('"');
        sb.Append(';');
        sb.Append(HttpConstants.SP_CHAR);
    }

    internal static int FirstInvalidCookieNameOctet(ICharSequence cs)
    {
        return FirstInvalidOctet(cs, VALID_COOKIE_NAME_OCTETS);
    }

    internal static int FirstInvalidCookieValueOctet(ICharSequence cs)
    {
        return FirstInvalidOctet(cs, VALID_COOKIE_VALUE_OCTETS);
    }

    internal static int FirstInvalidOctet(ICharSequence cs, BitArray bits)
    {
        for (int i = 0; i < cs.Length(); i++)
        {
            char c = cs.CharAt(i);
            if (!(c < bits.Length && bits.Get(c)))
            {
                return i;
            }
        }

        return -1;
    }

    internal static ICharSequence UnwrapValue(ICharSequence cs)
    {
        int len = cs.Length();
        if (len > 0 && cs.CharAt(0) == '"')
        {
            if (len >= 2 && cs.CharAt(len - 1) == '"')
            {
                // properly balanced
                return len == 2 ? StringCharSequence.Empty : cs.SubSequence(1, len - 1);
            }
            else
            {
                return null;
            }
        }

        return cs;
    }

    internal static string ValidateAttributeValue(string name, string value)
    {
        if (value == null)
        {
            return null;
        }

        value = HttpUtil.TrimJava(value);
        if (value.Length == 0)
        {
            return null;
        }

        int i = FirstInvalidOctet(new StringCharSequence(value), VALID_COOKIE_ATTRIBUTE_VALUE_OCTETS);
        if (i != -1)
        {
            throw new ArgumentException(name + " contains the prohibited characters: " + value[i]);
        }

        return value;
    }

    // CLR: the private constructor and its comment; a static class has no constructor.
    // Unused
}
