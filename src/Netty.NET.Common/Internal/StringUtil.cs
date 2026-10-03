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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Netty.NET.Common.Internal;

/**
 * String utility class.
 */
public static class StringUtil
{
    public static readonly string EMPTY_STRING = "";
    public static readonly string NEWLINE = SystemPropertyUtil.Get("line.separator", "\n");

    public const char DOUBLE_QUOTE = '\"';
    public const char COMMA = ',';
    public const char LINE_FEED = '\n';
    public const char CARRIAGE_RETURN = '\r';
    public const char TAB = '\t';
    public const char SPACE = (char)0x20;

    private static readonly string[] BYTE2HEX_PAD = new string[256];
    private static readonly string[] BYTE2HEX_NOPAD = new string[256];
    private static readonly byte[] HEX2B;

    /**
     * 2 - Quote character at beginning and end.
     * 5 - Extra allowance for anticipated escape characters that may be added.
     */
    private static readonly int CSV_NUMBER_ESCAPE_CHARACTERS = 2 + 5;

    private static readonly char PACKAGE_SEPARATOR_CHAR = '.';

    // Unused.
    // CLR adaptation: the private Java constructor is replaced by a static class.
    static StringUtil()
    {
        // Generate the lookup table that converts a byte into a 2-digit hexadecimal integer.
        for (int i = 0; i < BYTE2HEX_PAD.Length; i++)
        {
            string str = i.ToString("x");
            BYTE2HEX_PAD[i] = i > 0xf ? str : ('0' + str);
            BYTE2HEX_NOPAD[i] = str;
        }

        // Generate the lookup table that converts an hex char into its decimal value:
        // the size of the table is such that the JVM is capable of save any bounds-check
        // if a char type is used as an index.
        HEX2B = new byte[char.MaxValue + 1];
        Arrays.Fill(HEX2B, byte.MaxValue);
        HEX2B['0'] = 0;
        HEX2B['1'] = 1;
        HEX2B['2'] = 2;
        HEX2B['3'] = 3;
        HEX2B['4'] = 4;
        HEX2B['5'] = 5;
        HEX2B['6'] = 6;
        HEX2B['7'] = 7;
        HEX2B['8'] = 8;
        HEX2B['9'] = 9;
        HEX2B['A'] = 10;
        HEX2B['B'] = 11;
        HEX2B['C'] = 12;
        HEX2B['D'] = 13;
        HEX2B['E'] = 14;
        HEX2B['F'] = 15;
        HEX2B['a'] = 10;
        HEX2B['b'] = 11;
        HEX2B['c'] = 12;
        HEX2B['d'] = 13;
        HEX2B['e'] = 14;
        HEX2B['f'] = 15;
    }

    /**
     * Get the item after one char delim if the delim is found (else null).
     * This operation is a simplified and optimized
     * version of {@link String#split(String, int)}.
     */
    public static string SubstringAfter(string value, char delim)
    {
        int pos = value.IndexOf(delim);
        if (pos >= 0)
        {
            return value[(pos + 1)..];
        }

        return null;
    }

    /**
     * Get the item before one char delim if the delim is found (else null).
     * This operation is a simplified and optimized
     * version of {@link String#split(String, int)}.
     */
    public static string SubstringBefore(string value, char delim)
    {
        int pos = value.IndexOf(delim);
        if (pos >= 0)
        {
            return value[0..pos];
        }

        return null;
    }

    /**
     * Checks if two strings have the same suffix of specified length
     *
     * @param s   string
     * @param p   string
     * @param len length of the common suffix
     * @return true if both s and p are not null and both have the same suffix. Otherwise - false
     */
    public static bool CommonSuffixOfLength(string s, string p, int len)
    {
        return s != null && p != null && len >= 0 && len <= s.Length && len <= p.Length &&
               s.AsSpan(s.Length - len).SequenceEqual(p.AsSpan(p.Length - len));
    }

    /**
     * Converts the specified byte value into a 2-digit hexadecimal integer.
     */
    public static string ByteToHexStringPadded(int value)
    {
        return BYTE2HEX_PAD[value & 0xff];
    }

    /**
     * Converts the specified byte value into a 2-digit hexadecimal integer and appends it to the specified buffer.
     */
    public static StringBuilder ByteToHexStringPadded(StringBuilder buf, int value)
    {
        try
        {
            buf.Append(ByteToHexStringPadded(value));
        }
        catch (IOException e)
        {
            PlatformDependent.ThrowException(e);
        }

        return buf;
    }

    /**
     * Converts the specified byte array into a hexadecimal value.
     */
    public static string ToHexStringPadded(byte[] src)
    {
        return ToHexStringPadded(src, 0, src.Length);
    }

    /**
     * Converts the specified byte array into a hexadecimal value.
     */
    public static string ToHexStringPadded(byte[] src, int offset, int length)
    {
        return ToHexStringPadded(new StringBuilder(length << 1), src, offset, length).ToString();
    }

    /**
     * Converts the specified byte array into a hexadecimal value and appends it to the specified buffer.
     */
    public static StringBuilder ToHexStringPadded(StringBuilder dst, byte[] src)
    {
        return ToHexStringPadded(dst, src, 0, src.Length);
    }

    /**
     * Converts the specified byte array into a hexadecimal value and appends it to the specified buffer.
     */
    public static StringBuilder ToHexStringPadded(StringBuilder dst, byte[] src, int offset, int length)
    {
        int end = offset + length;
        for (int i = offset; i < end; i++)
        {
            ByteToHexStringPadded(dst, src[i]);
        }

        return dst;
    }

    /**
     * Converts the specified byte value into a hexadecimal integer.
     */
    public static string ByteToHexString(int value)
    {
        return BYTE2HEX_NOPAD[value & 0xff];
    }

    /**
     * Converts the specified byte value into a hexadecimal integer and appends it to the specified buffer.
     */
    public static StringBuilder ByteToHexString(StringBuilder buf, int value)
    {
        try
        {
            buf.Append(ByteToHexString(value));
        }
        catch (IOException e)
        {
            PlatformDependent.ThrowException(e);
        }

        return buf;
    }

    /**
     * Converts the specified byte array into a hexadecimal value.
     */
    public static string ToHexString(byte[] src)
    {
        return ToHexString(src, 0, src.Length);
    }

    /**
     * Converts the specified byte array into a hexadecimal value.
     */
    public static string ToHexString(byte[] src, int offset, int length)
    {
        return ToHexString(new StringBuilder(length << 1), src, offset, length).ToString();
    }

    /**
     * Converts the specified byte array into a hexadecimal value and appends it to the specified buffer.
     */
    public static StringBuilder ToHexString(StringBuilder dst, byte[] src)
    {
        return ToHexString(dst, src, 0, src.Length);
    }

    /**
     * Converts the specified byte array into a hexadecimal value and appends it to the specified buffer.
     */
    public static StringBuilder ToHexString(StringBuilder dst, byte[] src, int offset, int length)
    {
        Debug.Assert(length >= 0);
        if (length == 0)
        {
            return dst;
        }

        int end = offset + length;
        int endMinusOne = end - 1;
        int i;

        // Skip preceding zeroes.
        for (i = offset; i < endMinusOne; i++)
        {
            if (src[i] != 0)
            {
                break;
            }
        }

        ByteToHexString(dst, src[i++]);
        int remaining = end - i;
        ToHexStringPadded(dst, src, i, remaining);

        return dst;
    }

    /**
     * Helper to decode half of a hexadecimal number from a string.
     * @param c The ASCII character of the hexadecimal number to decode.
     * Must be in the range {@code [0-9a-fA-F]}.
     * @return The hexadecimal value represented in the ASCII character
     * given, or {@code -1} if the character is invalid.
     */
    public static int DecodeHexNibble(char c)
    {
        // Character.digit() is not used here, as it addresses a larger
        // set of characters (both ASCII and full-width latin letters).
        return HEX2B[c] == byte.MaxValue ? -1 : HEX2B[c];
    }

    /**
     * Helper to decode half of a hexadecimal number from a string.
     * @param b The ASCII character of the hexadecimal number to decode.
     * Must be in the range {@code [0-9a-fA-F]}.
     * @return The hexadecimal value represented in the ASCII character
     * given, or {@code -1} if the character is invalid.
     */
    public static int DecodeHexNibble(byte b)
    {
        // Character.digit() is not used here, as it addresses a larger
        // set of characters (both ASCII and full-width latin letters).
        return HEX2B[b] == byte.MaxValue ? -1 : HEX2B[b];
    }

    /**
     * Decode a 2-digit hex byte from within a string.
     */
    public static byte DecodeHexByte(ICharSequence s, int pos)
    {
        int hi = DecodeHexNibble(s.CharAt(pos));
        int lo = DecodeHexNibble(s.CharAt(pos + 1));
        if (hi == -1 || lo == -1)
        {
            throw new ArgumentException($"invalid hex byte '{s.SubSequence(pos, pos + 2)}' at index {pos} of '{s}'");
        }

        return (byte)((hi << 4) + lo);
    }

    public static byte DecodeHexByte(string str, int index)
    {
        if (index + 1 >= str.Length)
        {
            throw new ArgumentException($"Cannot decode hex byte at index {index}, string too short.");
        }

        char c1 = str[index];
        char c2 = str[index + 1];

        try
        {
            return byte.Parse($"{c1}{c2}", NumberStyles.HexNumber);
        }
        catch (FormatException)
        {
            throw new ArgumentException($"Invalid hex characters at index {index}: '{c1}{c2}'");
        }
    }

    /**
     * Decodes part of a string with <a href="https://en.wikipedia.org/wiki/Hex_dump">hex dump</a>
     *
     * @param hexDump a {@link CharSequence} which contains the hex dump
     * @param fromIndex start of hex dump in {@code hexDump}
     * @param length hex string length
     */
    public static byte[] DecodeHexDump(ICharSequence hexDump, int fromIndex, int length)
    {
        if (length < 0 || (length & 1) != 0)
        {
            throw new ArgumentException("length: " + length);
        }

        if (length == 0)
        {
            return EmptyArrays.EMPTY_BYTES;
        }

        byte[] bytes = new byte[length >>> 1];
        for (int i = 0; i < length; i += 2)
        {
            bytes[i >>> 1] = DecodeHexByte(hexDump, fromIndex + i);
        }

        return bytes;
    }

    /**
     * Decodes a <a href="https://en.wikipedia.org/wiki/Hex_dump">hex dump</a>
     */
    public static byte[] DecodeHexDump(ICharSequence hexDump)
    {
        return DecodeHexDump(hexDump, 0, hexDump.Length());
    }
    public static byte[] DecodeHexDump(string hexDump)
    {
        var str = new StringCharSequence(hexDump);
        return DecodeHexDump(str, 0, str.Length());
    }

    /**
     * Generates a class name from a {@link Class}. Similar to {@link Class#getName()}, but null-safe.
     */
    public static string ClassName(object o)
    {
        return o == null ? "null_object" : o.GetType().FullName;
    }
    public static string SimpleClassName<T>()
    {
        return SimpleClassName(typeof(T));
    }
    
    /**
     * The shortcut to {@link #simpleClassName(Class) simpleClassName(o.getClass())}.
     */
    public static string SimpleClassName(object o)
    {
        if (o == null)
        {
            return "null_object";
        }
        else
        {
            return SimpleClassName(o.GetType());
        }
    }

    /**
     * Generates a simplified name from a {@link Class}.  Similar to {@link Class#getSimpleName()}, but it works fine
     * with anonymous classes.
     */
    public static string SimpleClassName(Type t)
    {
        ObjectUtil.CheckNotNull(t, nameof(t));
        if (t.IsGenericType) t = t.GetGenericTypeDefinition();
        string name = t.FullName ?? t.Name;
        int namespaceEnd = name.LastIndexOf('.');
        if (namespaceEnd >= 0) name = name.Substring(namespaceEnd + 1);
        return System.Text.RegularExpressions.Regex.Replace(name, @"`\d+", "");
    }

    /**
     * Escapes the specified value, if necessary according to
     * <a href="https://tools.ietf.org/html/rfc4180#section-2">RFC-4180</a>.
     *
     * @param value The value which will be escaped according to
     *              <a href="https://tools.ietf.org/html/rfc4180#section-2">RFC-4180</a>
     * @return {@link CharSequence} the escaped value if necessary, or the value unchanged
     */
    public static string EscapeCsv(string value)
    {
        return EscapeCsv(value, false);
    }

    /**
     * Escapes the specified value, if necessary according to
     * <a href="https://tools.ietf.org/html/rfc4180#section-2">RFC-4180</a>.
     *
     * @param value          The value which will be escaped according to
     *                       <a href="https://tools.ietf.org/html/rfc4180#section-2">RFC-4180</a>
     * @param trimWhiteSpace The value will first be trimmed of its optional white-space characters,
     *                       according to <a href="https://tools.ietf.org/html/rfc7230#section-7">RFC-7230</a>
     * @return {@link CharSequence} the escaped value if necessary, or the value unchanged
     */
    public static string EscapeCsv(string value, bool trimWhiteSpace)
    {
        int length = ObjectUtil.CheckNotNull(value, "value").Length;
        int start;
        int last;
        if (trimWhiteSpace)
        {
            start = IndexOfFirstNonOwsChar(value, length);
            last = IndexOfLastNonOwsChar(value, start, length);
        }
        else
        {
            start = 0;
            last = length - 1;
        }

        if (start > last)
        {
            return EMPTY_STRING;
        }

        int firstUnescapedSpecial = -1;
        bool quoted = false;
        if (IsDoubleQuote(value[start]))
        {
            quoted = IsDoubleQuote(value[last]) && last > start;
            if (quoted)
            {
                start++;
                last--;
            }
            else
            {
                firstUnescapedSpecial = start;
            }
        }

        if (firstUnescapedSpecial < 0)
        {
            if (quoted)
            {
                for (int i = start; i <= last; i++)
                {
                    if (IsDoubleQuote(value[i]))
                    {
                        if (i == last || !IsDoubleQuote(value[i + 1]))
                        {
                            firstUnescapedSpecial = i;
                            break;
                        }

                        i++;
                    }
                }
            }
            else
            {
                for (int i = start; i <= last; i++)
                {
                    char c = value[i];
                    if (c == LINE_FEED || c == CARRIAGE_RETURN || c == COMMA)
                    {
                        firstUnescapedSpecial = i;
                        break;
                    }

                    if (IsDoubleQuote(c))
                    {
                        if (i == last || !IsDoubleQuote(value[i + 1]))
                        {
                            firstUnescapedSpecial = i;
                            break;
                        }

                        i++;
                    }
                }
            }

            if (firstUnescapedSpecial < 0)
            {
                // Special characters is not found or all of them already escaped.
                // In the most cases returns a same string. New string will be instantiated (via StringBuilder)
                // only if it really needed. It's important to prevent GC extra load.
                return quoted ? value[(start - 1)..(last + 2)] : value[start..(last + 1)];
            }
        }

        StringBuilder result = new StringBuilder(last - start + 1 + CSV_NUMBER_ESCAPE_CHARACTERS);
        result.Append(DOUBLE_QUOTE).Append(value, start, firstUnescapedSpecial - start);
        for (int i = firstUnescapedSpecial; i <= last; i++)
        {
            char c = value[i];
            if (IsDoubleQuote(c))
            {
                result.Append(DOUBLE_QUOTE);
                if (i < last && IsDoubleQuote(value[i + 1]))
                {
                    i++;
                }
            }

            result.Append(c);
        }

        return result.Append(DOUBLE_QUOTE).ToString();
    }

    /**
     * Unescapes the specified escaped CSV field, if necessary according to
     * <a href="https://tools.ietf.org/html/rfc4180#section-2">RFC-4180</a>.
     *
     * @param value The escaped CSV field which will be unescaped according to
     *              <a href="https://tools.ietf.org/html/rfc4180#section-2">RFC-4180</a>
     * @return {@link CharSequence} the unescaped value if necessary, or the value unchanged
     */
    public static string UnescapeCsv(string value)
    {
        int length = ObjectUtil.CheckNotNull(value, "value").Length;
        if (length == 0)
        {
            return value;
        }

        int last = length - 1;
        bool quoted = IsDoubleQuote(value[0]) && IsDoubleQuote(value[last]) && length != 1;
        if (!quoted)
        {
            ValidateCsvFormat(value);
            return value;
        }

        StringBuilder unescaped = InternalThreadLocalMap.Get().StringBuilder();
        for (int i = 1; i < last; i++)
        {
            char current = value[i];
            if (current == DOUBLE_QUOTE)
            {
                if (IsDoubleQuote(value[i + 1]) && (i + 1) != last)
                {
                    // Followed by a double-quote but not the last character
                    // Just skip the next double-quote
                    i++;
                }
                else
                {
                    // Not followed by a double-quote or the following double-quote is the last character
                    throw NewInvalidEscapedCsvFieldException(value, i);
                }
            }

            unescaped.Append(current);
        }

        return unescaped.ToString();
    }

    /**
     * Unescapes the specified escaped CSV fields according to
     * <a href="https://tools.ietf.org/html/rfc4180#section-2">RFC-4180</a>.
     *
     * @param value A string with multiple CSV escaped fields which will be unescaped according to
     *              <a href="https://tools.ietf.org/html/rfc4180#section-2">RFC-4180</a>
     * @return {@link List} the list of unescaped fields
     */
    public static List<string> UnescapeCsvFields(string value)
    {
        List<string> unescaped = new List<string>(2);
        StringBuilder current = InternalThreadLocalMap.Get().StringBuilder();
        bool quoted = false;
        int last = value.Length - 1;
        for (int i = 0; i <= last; i++)
        {
            char c = value[i];
            if (quoted)
            {
                switch (c)
                {
                    case DOUBLE_QUOTE:
                        if (i == last)
                        {
                            // Add the last field and return
                            unescaped.Add(current.ToString());
                            return unescaped;
                        }

                        char next = value[++i];
                        if (next == DOUBLE_QUOTE)
                        {
                            // 2 double-quotes should be unescaped to one
                            current.Append(DOUBLE_QUOTE);
                            break;
                        }

                        if (next == COMMA)
                        {
                            // This is the end of a field. Let's start to parse the next field.
                            quoted = false;
                            unescaped.Add(current.ToString());
                            current.Length = 0;
                            break;
                        }

                        // double-quote followed by other character is invalid
                        throw NewInvalidEscapedCsvFieldException(value, i - 1);
                    default:
                        current.Append(c);
                        break;
                }
            }
            else
            {
                switch (c)
                {
                    case COMMA:
                        // Start to parse the next field
                        unescaped.Add(current.ToString());
                        current.Length = 0;
                        break;
                    case DOUBLE_QUOTE:
                        if (current.Length == 0)
                        {
                            quoted = true;
                            break;
                        }
                        throw NewInvalidEscapedCsvFieldException(value, i);
                    // double-quote appears without being enclosed with double-quotes
                    // fall through
                    case LINE_FEED:
                    // fall through
                    case CARRIAGE_RETURN:
                        // special characters appears without being enclosed with double-quotes
                        throw NewInvalidEscapedCsvFieldException(value, i);
                    default:
                        current.Append(c);
                        break;
                }
            }
        }

        if (quoted)
        {
            throw NewInvalidEscapedCsvFieldException(value, last);
        }

        unescaped.Add(current.ToString());
        return unescaped;
    }

    /**
     * Validate if {@code value} is a valid csv field without double-quotes.
     *
     * @throws IllegalArgumentException if {@code value} needs to be encoded with double-quotes.
     */
    private static void ValidateCsvFormat(string value)
    {
        int length = value.Length;
        for (int i = 0; i < length; i++)
        {
            switch (value[i])
            {
                case DOUBLE_QUOTE:
                case LINE_FEED:
                case CARRIAGE_RETURN:
                case COMMA:
                    // If value contains any special character, it should be enclosed with double-quotes
                    throw NewInvalidEscapedCsvFieldException(value, i);
                default:
                    break;
            }
        }
    }

    private static ArgumentException NewInvalidEscapedCsvFieldException(string value, int index)
    {
        return new ArgumentException("invalid escaped CSV field: " + value + " index: " + index);
    }

    /**
     * Get the length of a string, {@code null} input is considered {@code 0} length.
     */
    public static int Length(string s)
    {
        return s == null ? 0 : s.Length;
    }

    /**
     * Determine if a string is {@code null} or {@link String#isEmpty()} returns {@code true}.
     */
    public static bool IsNullOrEmpty(string s)
    {
        return string.IsNullOrEmpty(s);
    }

    /**
     * Find the index of the first non-white space character in {@code s} starting at {@code offset}.
     *
     * @param seq    The string to search.
     * @param offset The offset to start searching at.
     * @return the index of the first non-white space character or &lt;{@code -1} if none was found.
     */
    public static int IndexOfNonWhiteSpace(string seq, int offset)
    {
        for (; offset < seq.Length; ++offset)
        {
            if (!char.IsWhiteSpace(seq[offset]))
            {
                return offset;
            }
        }

        return -1;
    }

    /**
     * Find the index of the first white space character in {@code s} starting at {@code offset}.
     *
     * @param seq    The string to search.
     * @param offset The offset to start searching at.
     * @return the index of the first white space character or &lt;{@code -1} if none was found.
     */
    public static int IndexOfWhiteSpace(string seq, int offset)
    {
        for (; offset < seq.Length; ++offset)
        {
            if (char.IsWhiteSpace(seq[offset]))
            {
                return offset;
            }
        }

        return -1;
    }

    /**
     * Determine if {@code c} lies within the range of values defined for
     * <a href="https://unicode.org/glossary/#surrogate_code_point">Surrogate Code Point</a>.
     *
     * @param c the character to check.
     * @return {@code true} if {@code c} lies within the range of values defined for
     * <a href="https://unicode.org/glossary/#surrogate_code_point">Surrogate Code Point</a>. {@code false} otherwise.
     */
    public static bool IsSurrogate(char c)
    {
        return c >= '\uD800' && c <= '\uDFFF';
    }

    private static bool IsDoubleQuote(char c)
    {
        return c == DOUBLE_QUOTE;
    }

    /**
     * Determine if the string {@code s} ends with the char {@code c}.
     *
     * @param s the string to test
     * @param c the tested char
     * @return true if {@code s} ends with the char {@code c}
     */
    public static bool EndsWith(string s, char c)
    {
        int len = s.Length;
        return len > 0 && s[len - 1] == c;
    }

    /**
     * Trim optional white-space characters from the specified value,
     * according to <a href="https://tools.ietf.org/html/rfc7230#section-7">RFC-7230</a>.
     *
     * @param value the value to trim
     * @return {@link CharSequence} the trimmed value if necessary, or the value unchanged
     */
    public static string TrimOws(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ReadOnlySpan<char> trimmed = value.AsSpan().Trim(" \t".AsSpan());
        return trimmed.Length == value.Length ? value : trimmed.ToString();
    }

    /**
     * Returns a char sequence that contains all {@code elements} joined by a given separator.
     *
     * @param separator for each element
     * @param elements to join together
     *
     * @return a char sequence joined by a given separator.
     */
    public static string Join(string separator, IEnumerable<string> elements)
    {
        ObjectUtil.CheckNotNull(separator, nameof(separator));
        ObjectUtil.CheckNotNull(elements, nameof(elements));
        return string.Join(separator, elements.Select(element => element ?? "null"));
    }

    /**
     * @return {@code length} if no OWS is found.
     */
    private static int IndexOfFirstNonOwsChar(string value, int length)
    {
        int index = value.AsSpan(0, length).IndexOfAnyExcept(SPACE, TAB);
        return index < 0 ? length : index;
    }

    /**
     * @return {@code start} if no OWS is found.
     */
    private static int IndexOfLastNonOwsChar(string value, int start, int length)
    {
        if (start == length)
        {
            return length - 1;
        }

        int index = value.AsSpan(start, length - start).LastIndexOfAnyExcept(SPACE, TAB);
        return index < 0 ? start : start + index;
    }
}
