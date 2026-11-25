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
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http;

/**
 * Creates a URL-encoded URI from a path string and key-value parameter pairs.
 * This encoder is for one time use only.  Create a new instance for each URI.
 *
 * <pre>
 * {@link QueryStringEncoder} encoder = new {@link QueryStringEncoder}("/hello");
 * encoder.addParam("recipient", "world");
 * assert encoder.toString().equals("/hello?recipient=world");
 * </pre>
 *
 * @see QueryStringDecoder
 */
// CLR: java.nio.charset.Charset is System.Text.Encoding. CharsetUtil.UTF_8.equals(charset) compares the canonical
// charset names, so any UTF-8 charset takes the built-in UTF-8 path; its .NET counterpart is the code page of the
// Encoding (65001 for every UTF8Encoding). String.getBytes(charset) is Encoding.GetBytes, which writes no preamble
// (the preamble "can be prefixed to the sequence of bytes resulting from the encoding process",
// https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding.getpreamble?view=net-10.0), while the JDK "UTF-16"
// charset "writes a big-endian byte-order mark" when encoding
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/charset/Charset.html); no .NET Encoding emits a
// byte-order mark from GetBytes. The private encode methods take the String arguments of addParam as string (Java
// passes them as CharSequence). java.net.URI is System.Uri: new URI(String) parses a relative or an absolute reference,
// so toUri() is new Uri(ToString(), UriKind.RelativeOrAbsolute), and URISyntaxException is the UriFormatException that
// the constructor throws. Character.isHighSurrogate/isLowSurrogate/toCodePoint are char.IsHighSurrogate/IsLowSurrogate/
// ConvertToUtf32. The encoded octets are masked with & 0xF in forDigit, so the Java signed byte and the C# byte give
// the same hex digits.
public class QueryStringEncoder
{

    private readonly Encoding charset;
    private readonly StringBuilder uriBuilder;
    private bool hasParams;
    private const byte WRITE_UTF_UNKNOWN = (byte) '?';
    private static readonly char[] CHAR_MAP = "0123456789ABCDEF".ToCharArray();

    /**
     * Creates a new encoder that encodes a URI that starts with the specified
     * path string.  The encoder will encode the URI in UTF-8.
     */
    public QueryStringEncoder(string uri) : this(uri, HttpConstants.DEFAULT_CHARSET)
    {
    }

    /**
     * Creates a new encoder that encodes a URI that starts with the specified
     * path string in the specified charset.
     */
    public QueryStringEncoder(string uri, Encoding charset)
    {
        ObjectUtil.CheckNotNull(charset, "charset");
        uriBuilder = new StringBuilder(uri);
        this.charset = Encoding.UTF8.CodePage == charset.CodePage ? null : charset;
    }

    /**
     * Adds a parameter with the specified name and value to this encoder.
     */
    public void AddParam(string name, string value)
    {
        ObjectUtil.CheckNotNull(name, "name");
        if (hasParams)
        {
            uriBuilder.Append('&');
        }
        else
        {
            uriBuilder.Append('?');
            hasParams = true;
        }

        EncodeComponent(name);
        if (value != null)
        {
            uriBuilder.Append('=');
            EncodeComponent(value);
        }
    }

    private void EncodeComponent(string s)
    {
        if (charset == null)
        {
            EncodeUtf8Component(s);
        }
        else
        {
            EncodeNonUtf8Component(s);
        }
    }

    /**
     * Returns the URL-encoded URI object which was created from the path string
     * specified in the constructor and the parameters added by
     * {@link #addParam(String, String)} method.
     */
    public Uri ToUri()
    {
        return new Uri(ToString(), UriKind.RelativeOrAbsolute);
    }

    /**
     * Returns the URL-encoded URI which was created from the path string
     * specified in the constructor and the parameters added by
     * {@link #addParam(String, String)} method.
     */
    public override string ToString()
    {
        return uriBuilder.ToString();
    }

    /**
     * Encode the String as per RFC 3986, Section 2.
     * <p>
     * There is a little different between the JDK's encode method : {@link URLEncoder#encode(String, String)}.
     * The JDK's encoder encode the space to {@code +} and this method directly encode the blank to {@code %20}
     * beyond that , this method reuse the {@link #uriBuilder} in this class rather then create a new one,
     * thus generates less garbage for the GC.
     *
     * @param s The String to encode
     */
    private void EncodeNonUtf8Component(string s)
    {
        //Don't allocate memory until needed
        char[] buf = null;

        for (int i = 0, len = s.Length; i < len;)
        {
            char c = s[i];
            if (DontNeedEncoding(c))
            {
                uriBuilder.Append(c);
                i++;
            }
            else
            {
                int index = 0;
                if (buf == null)
                {
                    buf = new char[s.Length - i];
                }

                do
                {
                    buf[index] = c;
                    index++;
                    i++;
                } while (i < s.Length && !DontNeedEncoding(c = s[i]));

                byte[] bytes = charset.GetBytes(buf, 0, index);

                foreach (byte b in bytes)
                {
                    AppendEncoded(b);
                }
            }
        }
    }

    /**
     * @see ByteBufUtil#writeUtf8(io.netty.buffer.ByteBuf, CharSequence, int, int)
     */
    private void EncodeUtf8Component(string s)
    {
        for (int i = 0, len = s.Length; i < len; i++)
        {
            char c = s[i];
            if (!DontNeedEncoding(c))
            {
                EncodeUtf8Component(s, i, len);
                return;
            }
        }
        uriBuilder.Append(s);
    }

    private void EncodeUtf8Component(string s, int encodingStart, int len)
    {
        if (encodingStart > 0)
        {
            // Append non-encoded characters directly first.
            uriBuilder.Append(s, 0, encodingStart);
        }
        EncodeUtf8ComponentSlow(s, encodingStart, len);
    }

    private void EncodeUtf8ComponentSlow(string s, int start, int len)
    {
        for (int i = start; i < len; i++)
        {
            char c = s[i];
            if (c < 0x80)
            {
                if (DontNeedEncoding(c))
                {
                    uriBuilder.Append(c);
                }
                else
                {
                    AppendEncoded(c);
                }
            }
            else if (c < 0x800)
            {
                AppendEncoded(0xc0 | (c >> 6));
                AppendEncoded(0x80 | (c & 0x3f));
            }
            else if (StringUtil.IsSurrogate(c))
            {
                if (!char.IsHighSurrogate(c))
                {
                    AppendEncoded(WRITE_UTF_UNKNOWN);
                    continue;
                }
                // Surrogate Pair consumes 2 characters.
                if (++i == s.Length)
                {
                    AppendEncoded(WRITE_UTF_UNKNOWN);
                    break;
                }
                // Extra method to allow inlining the rest of writeUtf8 which is the most likely code path.
                WriteUtf8Surrogate(c, s[i]);
            }
            else
            {
                AppendEncoded(0xe0 | (c >> 12));
                AppendEncoded(0x80 | ((c >> 6) & 0x3f));
                AppendEncoded(0x80 | (c & 0x3f));
            }
        }
    }

    private void WriteUtf8Surrogate(char c, char c2)
    {
        if (!char.IsLowSurrogate(c2))
        {
            AppendEncoded(WRITE_UTF_UNKNOWN);
            AppendEncoded(char.IsHighSurrogate(c2) ? WRITE_UTF_UNKNOWN : c2);
            return;
        }
        int codePoint = char.ConvertToUtf32(c, c2);
        // See https://www.unicode.org/versions/Unicode7.0.0/ch03.pdf#G2630.
        AppendEncoded(0xf0 | (codePoint >> 18));
        AppendEncoded(0x80 | ((codePoint >> 12) & 0x3f));
        AppendEncoded(0x80 | ((codePoint >> 6) & 0x3f));
        AppendEncoded(0x80 | (codePoint & 0x3f));
    }

    private void AppendEncoded(int b)
    {
        uriBuilder.Append('%').Append(ForDigit(b >> 4)).Append(ForDigit(b));
    }

    /**
     * Convert the given digit to a upper hexadecimal char.
     *
     * @param digit the number to convert to a character.
     * @return the {@code char} representation of the specified digit
     * in hexadecimal.
     */
    private static char ForDigit(int digit)
    {
        return CHAR_MAP[digit & 0xF];
    }

    /**
     * Determines whether the given character is a unreserved character.
     * <p>
     * unreserved characters do not need to be encoded, and include uppercase and lowercase
     * letters, decimal digits, hyphen, period, underscore, and tilde.
     * <p>
     * unreserved  = ALPHA / DIGIT / "-" / "." / "_" / "~" / "*"
     *
     * @param ch the char to be judged whether it need to be encode
     * @return true or false
     */
    private static bool DontNeedEncoding(char ch)
    {
        return ch >= 'a' && ch <= 'z' || ch >= 'A' && ch <= 'Z' || ch >= '0' && ch <= '9'
                || ch == '-' || ch == '_' || ch == '.' || ch == '*' || ch == '~';
    }
}
