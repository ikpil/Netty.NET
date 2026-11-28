/*
 * Copyright 2026 The Netty Project
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
using System.Globalization;
using System.Text;

namespace Netty.NET.Common.Internal;

// CLR: java.net.IDN.toASCII(String)/toUnicode(String) (flag 0: no ALLOW_UNASSIGNED, no USE_STD3_ASCII_RULES) for the
// codec modules that call them (codec-dns AbstractDnsRecord; codec-socks SocksCmdRequest, SocksCmdResponse,
// DefaultSocks4CommandRequest, DefaultSocks5CommandRequest, DefaultSocks5CommandResponse). Owner decision 2026-11-19:
// one shared helper, public in the Internal namespace like JdkDecimal, instead of a copy per module.
//
// IDN "can handle both label and entire domain name, by assuming that labels in a domain name are always separated by
// dots" (U+002E, U+3002, U+FF0E, U+FF61) and joins the converted labels with U+002E
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/IDN.html). This class keeps the OpenJDK label
// loop (https://raw.githubusercontent.com/openjdk/jdk17u/master/src/java.base/share/classes/java/net/IDN.java): a lone
// separator (the root label) gives ".", each converted label is followed by the dot that followed it (so "" stays ""
// and a trailing dot stays), an all-ASCII label skips Nameprep and Punycode (RFC 3490 section 4.1 steps 1 and 4,
// https://www.rfc-editor.org/rfc/rfc3490#section-4.1) and is only checked for length (step 8), an empty label is
// rejected ("Empty label is not a legal name") and so is a converted label longer than 63 ("The label in the input is
// too long").
//
// Only a label with non-ASCII characters goes through System.Globalization.IdnMapping, whose GetAscii input "cannot
// begin with a label separator" and whose labels "cannot contain" control characters
// (https://learn.microsoft.com/en-us/dotnet/api/system.globalization.idnmapping.getascii?view=net-10.0), unlike
// IDN.toASCII("") (returns "") or an ASCII label holding NUL (codec-socks Socks4ClientEncoderTest passes
// "unix://uds\0.sock" through unchanged). IdnMapping follows IDNA2008/UTS #46 where IDN uses IDNA2003 (Unicode 3.2
// Nameprep), so non-ASCII labels such as "faß" can convert differently; the host names of the ported tests convert
// identically. "ToUnicode never fails. In case of any error, the input string is returned unmodified", so a label
// IdnMapping.GetUnicode rejects is kept as it is. IllegalArgumentException is ArgumentException, which GetAscii also
// throws for invalid input.
public static class IDN
{
    private const int MAX_LABEL_LENGTH = 63;

    private static readonly IdnMapping MAPPING = new IdnMapping();

    public static string ToASCII(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (IsRootLabel(input))
        {
            return ".";
        }

        StringBuilder @out = new StringBuilder(input.Length);
        int p = 0;

        while (p < input.Length)
        {
            int q = SearchDots(input, p);
            @out.Append(ToASCIIInternal(input.Substring(p, q - p)));

            if (q != input.Length)
            {
                @out.Append('.');
            }

            p = q + 1;
        }

        return @out.ToString();
    }

    public static string ToUnicode(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (IsRootLabel(input))
        {
            return ".";
        }

        StringBuilder @out = new StringBuilder(input.Length);
        int p = 0;

        while (p < input.Length)
        {
            int q = SearchDots(input, p);
            @out.Append(ToUnicodeInternal(input.Substring(p, q - p)));

            if (q != input.Length)
            {
                @out.Append('.');
            }

            p = q + 1;
        }

        return @out.ToString();
    }

    private static string ToASCIIInternal(string label)
    {
        string dest = Ascii.IsValid(label) ? label : MAPPING.GetAscii(label);
        if (dest.Length == 0)
        {
            throw new ArgumentException("Empty label is not a legal name");
        }
        if (dest.Length > MAX_LABEL_LENGTH)
        {
            throw new ArgumentException("The label in the input is too long");
        }

        return dest;
    }

    private static string ToUnicodeInternal(string label)
    {
        if (label.Length == 0)
        {
            return label;
        }

        try
        {
            return MAPPING.GetUnicode(label);
        }
        catch (ArgumentException)
        {
            return label;
        }
    }

    private static bool IsRootLabel(string s)
    {
        return s.Length == 1 && IsLabelSeparator(s[0]);
    }

    private static int SearchDots(string s, int start)
    {
        int i;

        for (i = start; i < s.Length; i++)
        {
            if (IsLabelSeparator(s[i]))
            {
                break;
            }
        }

        return i;
    }

    private static bool IsLabelSeparator(char c)
    {
        return c == '.' || c == '。' || c == '．' || c == '｡';
    }
}
