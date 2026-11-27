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
using System.Text;

namespace Netty.NET.Common;

/**
 * A utility class that provides various common operations and constants
 * related with {@link Charset} and its relevant classes.
 */
// CLR: the charset constants and the cached encoders/decoders of the Java class are System.Text.Encoding with
// caller-selected fallbacks (docs/common-clr-design.md, Native encoding and codec ownership), so the class holds only
// the CLR-only ForName (owner decision 2026-11-24), the counterpart of java.nio.charset.Charset.forName(String) that
// the codec modules share.
public static class CharsetUtil
{
    // CLR: Charset.forName(String), which "Returns a charset object for the named charset", given by its canonical
    // name or an alias
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/charset/Charset.html#forName(java.lang.String)).
    // "Charset names are not case-sensitive", and "Every implementation of the Java platform is required to support"
    // US-ASCII, ISO-8859-1, UTF-8, UTF-16BE, UTF-16LE and UTF-16 (same page), under the canonical names and aliases of
    // the java.base table (https://docs.oracle.com/en/java/javase/17/intl/supported-encodings.html), for example "UTF8"
    // and "ISO8859_1". Encoding.GetEncoding accepts "Any value returned by the WebName property" and the names .NET
    // knows (https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding.getencoding?view=net-10.0); it rejects
    // "UTF8" and "ISO8859_1" with ArgumentException and maps "UTF-16" to the little-endian Encoding.Unicode (measured
    // on .NET 10), so these names are looked up here first, ignoring case. UTF-16 "defaults to big-endian if there is
    // no byte-order mark" when decoding and "writes a big-endian byte-order mark" when encoding (Charset page); the
    // big-endian UnicodeEncoding used for it neither interprets nor writes a byte-order mark in GetString/GetBytes
    // (docs/common-clr-design.md, UTF-16). The exceptions follow the OpenJDK lookup: a null name throws
    // IllegalArgumentException("Null charset name"), which is ArgumentException (not ArgumentNullException, which
    // stands for NullPointerException); checkName throws IllegalCharsetNameException for the empty string or a
    // character other than the ASCII letters, digits and "-+:_." (those five not first; Charset names section of the
    // Charset page). OpenJDK checks the name only when no charset matches it, but the names and aliases of the table
    // are legal and the Charset constructor checks those of the other charsets, so checking it first gives the same
    // result and keeps the case-insensitive table lookup to ASCII names. Any other name goes to Encoding.GetEncoding(string), whose ArgumentException ("name is not a valid code
    // page name", "not supported by the underlying platform") stands for UnsupportedCharsetException. Both Java
    // exceptions are IllegalArgumentExceptions, so a catch of either one, or of both, is catch (ArgumentException).
    public static Encoding ForName(string charsetName)
    {
        if (charsetName == null)
        {
            throw new ArgumentException("Null charset name");
        }
        CheckName(charsetName);
        if (STANDARD_CHARSETS.TryGetValue(charsetName, out Encoding charset))
        {
            return charset;
        }
        return Encoding.GetEncoding(charsetName);
    }

    // CLR: Charset.checkName(String) of OpenJDK, which throws IllegalCharsetNameException(name); the generated exception
    // passes String.valueOf(name) to super as its detail message (make/scripts/genExceptions.sh).
    private static void CheckName(string s)
    {
        int n = s.Length;
        if (n == 0)
        {
            throw new ArgumentException(s);
        }
        for (int i = 0; i < n; i++)
        {
            char c = s[i];
            if (c >= 'A' && c <= 'Z')
            {
                continue;
            }
            if (c >= 'a' && c <= 'z')
            {
                continue;
            }
            if (c >= '0' && c <= '9')
            {
                continue;
            }
            if (c == '-' && i != 0)
            {
                continue;
            }
            if (c == '+' && i != 0)
            {
                continue;
            }
            if (c == ':' && i != 0)
            {
                continue;
            }
            if (c == '_' && i != 0)
            {
                continue;
            }
            if (c == '.' && i != 0)
            {
                continue;
            }
            throw new ArgumentException(s);
        }
    }

    private static readonly Dictionary<string, Encoding> STANDARD_CHARSETS = NewStandardCharsets();

    // CLR: the canonical java.nio names and the aliases of the java.base table; the java.io/java.lang canonical names
    // (ASCII, ISO8859_1, UTF8, UnicodeBigUnmarked, UnicodeLittleUnmarked) are among them.
    private static Dictionary<string, Encoding> NewStandardCharsets()
    {
        Dictionary<string, Encoding> map = new Dictionary<string, Encoding>(StringComparer.OrdinalIgnoreCase);
        Add(map, Encoding.UTF8, "UTF-8", "UTF8", "unicode-1-1-utf-8");
        Add(map, Encoding.ASCII, "US-ASCII", "iso-ir-6", "ANSI_X3.4-1986", "ISO_646.irv:1991", "ASCII", "ISO646-US",
            "us", "IBM367", "cp367", "csASCII", "default", "646", "iso_646.irv:1983", "ANSI_X3.4-1968", "ascii7");
        Add(map, Encoding.Latin1, "ISO-8859-1", "iso-ir-100", "ISO_8859-1", "latin1", "l1", "IBM819", "cp819",
            "csISOLatin1", "819", "IBM-819", "ISO8859_1", "ISO_8859-1:1987", "ISO_8859_1", "8859_1", "ISO8859-1");
        Add(map, new UnicodeEncoding(true, false), "UTF-16BE", "UTF_16BE", "ISO-10646-UCS-2", "X-UTF-16BE",
            "UnicodeBigUnmarked");
        Add(map, new UnicodeEncoding(false, false), "UTF-16LE", "UTF_16LE", "X-UTF-16LE", "UnicodeLittleUnmarked");
        Add(map, new UnicodeEncoding(true, false), "UTF-16", "UTF_16", "utf16", "unicode", "UnicodeBig");
        return map;
    }

    private static void Add(Dictionary<string, Encoding> map, Encoding charset, params string[] names)
    {
        foreach (string name in names)
        {
            map[name] = charset;
        }
    }
}
