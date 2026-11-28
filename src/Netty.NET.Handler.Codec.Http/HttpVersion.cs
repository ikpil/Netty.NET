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
using System.Globalization;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http;

/**
 * The version of HTTP or its derived protocols, such as
 * <a href="https://en.wikipedia.org/wiki/Real_Time_Streaming_Protocol">RTSP</a> and
 * <a href="https://en.wikipedia.org/wiki/Internet_Content_Adaptation_Protocol">ICAP</a>.
 */
// CLR: Comparable<HttpVersion> is IComparable<HttpVersion> (CompareTo keeps its BCL name); String.compareTo is the
// ordinal UTF-16 comparison string.CompareOrdinal. The reference comparisons text == HTTP_1_1_STRING are
// ReferenceEquals (both languages intern string literals). toUpperCase(Locale.US) is ToUpperInvariant(),
// "converted to uppercase using the casing rules of the invariant culture", so the Turkish dotted I is never
// produced (https://learn.microsoft.com/en-us/dotnet/api/system.string.toupperinvariant?view=net-10.0).
// Character.isISOControl ('\u0000' through '\u001F' or '\u007F' through '\u009F') is char.IsControl (the Unicode
// control category, the same code points:
// https://learn.microsoft.com/en-us/dotnet/api/system.char.iscontrol?view=net-10.0), and Character.isWhitespace
// keeps the Java rule in IsJavaWhitespace, because char.IsWhiteSpace also accepts the no-break spaces that Java
// excludes (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Character.html).
// IllegalArgumentException is ArgumentException.
public class HttpVersion : IComparable<HttpVersion>
{

    internal const string HTTP_1_0_STRING = "HTTP/1.0";
    internal const string HTTP_1_1_STRING = "HTTP/1.1";

    /**
     * HTTP/1.0
     */
    public static readonly HttpVersion HTTP_1_0 = new HttpVersion("HTTP", 1, 0, false, true);

    /**
     * HTTP/1.1
     */
    public static readonly HttpVersion HTTP_1_1 = new HttpVersion("HTTP", 1, 1, true, true);

    /**
     * Returns an existing or new {@link HttpVersion} instance which matches to
     * the specified protocol version string.  If the specified {@code text} is
     * equal to {@code "HTTP/1.0"}, {@link #HTTP_1_0} will be returned.  If the
     * specified {@code text} is equal to {@code "HTTP/1.1"}, {@link #HTTP_1_1}
     * will be returned.  Otherwise, a new {@link HttpVersion} instance will be
     * returned.
     */
    public static HttpVersion ValueOf(string text)
    {
        return ValueOf(text, false);
    }

    internal static HttpVersion ValueOf(string text, bool strict)
    {
        ObjectUtil.CheckNotNull(text, "text");

        // super fast-path
        if (ReferenceEquals(text, HTTP_1_1_STRING))
        {
            return HTTP_1_1;
        }
        if (ReferenceEquals(text, HTTP_1_0_STRING))
        {
            return HTTP_1_0;
        }

        if (text.Length == 0)
        {
            throw new ArgumentException("text is empty (possibly HTTP/0.9)");
        }

        // Try to match without convert to uppercase first as this is what 99% of all clients
        // will send anyway. Also there is a change to the RFC to make it clear that it is
        // expected to be case-sensitive
        //
        // See:
        // * https://trac.tools.ietf.org/wg/httpbis/trac/ticket/1
        // * https://trac.tools.ietf.org/wg/httpbis/trac/wiki
        //
        HttpVersion version = Version0(text);
        if (version == null)
        {
            version = new HttpVersion(text, strict, true);
        }

        return version;
    }

    private static HttpVersion Version0(string text)
    {
        if (HTTP_1_1_STRING.Equals(text))
        {
            return HTTP_1_1;
        }
        if (HTTP_1_0_STRING.Equals(text))
        {
            return HTTP_1_0;
        }

        return null;
    }

    private readonly string protocolName;
    private readonly int majorVersion;
    private readonly int minorVersion;
    private readonly string text;
    private readonly bool keepAliveDefault;
    private readonly byte[] bytes;

    /**
     * Creates a new HTTP version with the specified version string.  You will
     * not need to create a new instance unless you are implementing a protocol
     * derived from HTTP, such as
     * <a href="https://en.wikipedia.org/wiki/Real_Time_Streaming_Protocol">RTSP</a> and
     * <a href="https://en.wikipedia.org/wiki/Internet_Content_Adaptation_Protocol">ICAP</a>.
     *
     * @param keepAliveDefault
     *        {@code true} if and only if the connection is kept alive unless
     *        the {@code "Connection"} header is set to {@code "close"} explicitly.
     */
    public HttpVersion(string text, bool keepAliveDefault) : this(text, false, keepAliveDefault)
    {
    }

    internal HttpVersion(string text, bool strict, bool keepAliveDefault)
    {
        // toUpperCase() without an explicit Locale uses the JVM default. In Turkish locale
        // (tr_TR) 'i' uppercases to 'İ' (U+0130), which would corrupt protocol strings such
        // as "icap/1.0" or any custom HTTP-derived scheme that contains a lowercase 'i'.
        // Control characters or whitespace at the token boundary must fail the checks below.
        ObjectUtil.CheckNotNull(text, "text");
        if (text.Length == 0)
        {
            throw new ArgumentException("text must not be empty");
        }

        text = text.ToUpperInvariant();

        if (strict)
        {
            // Only single digit major / minor version is allowed.
            // See
            //  - https://datatracker.ietf.org/doc/html/rfc7230#section-2.6
            //  - https://datatracker.ietf.org/doc/html/rfc9110#name-protocol-version
            if (text.Length != 8 || !text.StartsWith("HTTP/", StringComparison.Ordinal) || text[6] != '.')
            {
                throw new ArgumentException("invalid version format: " + text);
            }

            protocolName = "HTTP";
            majorVersion = ToDecimal(text[5]);
            minorVersion = ToDecimal(text[7]);
        }
        else
        {
            int slashIndex = text.IndexOf('/');
            int dotIndex = text.IndexOf('.', slashIndex + 1);

            if (slashIndex <= 0 || dotIndex <= slashIndex + 1
                    || dotIndex >= text.Length - 1 || HasControlOrWhitespace(text, slashIndex))
            {
                throw new ArgumentException("invalid version format: " + text);
            }

            protocolName = text.Substring(0, slashIndex);
            majorVersion = ParseInt(text, slashIndex + 1, dotIndex);
            minorVersion = ParseInt(text, dotIndex + 1, text.Length);
        }

        this.text = protocolName + '/' + majorVersion.ToString(CultureInfo.InvariantCulture) + '.' +
                minorVersion.ToString(CultureInfo.InvariantCulture);
        this.keepAliveDefault = keepAliveDefault;
        bytes = null;
    }

    private static bool HasControlOrWhitespace(string s, int end)
    {
        for (int i = 0; i < end; i++)
        {
            char c = s[i];
            if (char.IsControl(c) || IsJavaWhitespace(c))
            {
                return true;
            }
        }

        return false;
    }

    // CLR: Character.isWhitespace(char): a space, line or paragraph separator other than the no-break spaces
    // ' ', ' ' and ' ', or one of '\t', '\n', '\u000B', '\f', '\r', '\u001C'-'\u001F'.
    internal static bool IsJavaWhitespace(char c)
    {
        switch (c)
        {
            case '\t':
            case '\n':
            case '\u000B':
            case '\f':
            case '\r':
            case '\u001C':
            case '\u001D':
            case '\u001E':
            case '\u001F':
                return true;
            case ' ':
            case ' ':
            case ' ':
                return false;
        }

        UnicodeCategory category = char.GetUnicodeCategory(c);
        return category == UnicodeCategory.SpaceSeparator || category == UnicodeCategory.LineSeparator ||
               category == UnicodeCategory.ParagraphSeparator;
    }

    private static int ParseInt(string text, int start, int end)
    {
        int result = 0;

        for (int i = start; i < end; i++)
        {
            char ch = text[i];
            result = result * 10 + ToDecimal(ch);
        }

        return result;
    }

    private static int ToDecimal(int value)
    {
        if (value < '0' || value > '9')
        {
            throw new ArgumentException("Invalid version number, only 0-9 (0x30-0x39) allowed," +
                    " but received a '" + (char) value + "' (0x" + value.ToString("x", CultureInfo.InvariantCulture) + ")");
        }

        return value - '0';
    }

    /**
     * Creates a new HTTP version with the specified protocol name and version
     * numbers.  You will not need to create a new instance unless you are
     * implementing a protocol derived from HTTP, such as
     * <a href="https://en.wikipedia.org/wiki/Real_Time_Streaming_Protocol">RTSP</a> and
     * <a href="https://en.wikipedia.org/wiki/Internet_Content_Adaptation_Protocol">ICAP</a>
     *
     * @param keepAliveDefault
     *        {@code true} if and only if the connection is kept alive unless
     *        the {@code "Connection"} header is set to {@code "close"} explicitly.
     */
    public HttpVersion(
            string protocolName, int majorVersion, int minorVersion,
            bool keepAliveDefault) : this(protocolName, majorVersion, minorVersion, keepAliveDefault, false)
    {
    }

    private HttpVersion(
            string protocolName, int majorVersion, int minorVersion,
            bool keepAliveDefault, bool bytes)
    {
        // See the comment in the (text, strict, keepAliveDefault) constructor for why this needs
        // an explicit Locale.US: avoids the Turkish-locale 'i' -> 'İ' corruption.
        ObjectUtil.CheckNotNull(protocolName, "protocolName");
        if (protocolName.Length == 0)
        {
            throw new ArgumentException("protocolName must not be empty");
        }

        protocolName = protocolName.ToUpperInvariant();

        if (HasControlOrWhitespace(protocolName, protocolName.Length))
        {
            throw new ArgumentException("invalid character in protocolName");
        }

        CheckPositiveOrZero(majorVersion, "majorVersion");
        CheckPositiveOrZero(minorVersion, "minorVersion");

        this.protocolName = protocolName;
        this.majorVersion = majorVersion;
        this.minorVersion = minorVersion;
        text = protocolName + '/' + majorVersion.ToString(CultureInfo.InvariantCulture) + '.' +
               minorVersion.ToString(CultureInfo.InvariantCulture);
        this.keepAliveDefault = keepAliveDefault;

        if (bytes)
        {
            this.bytes = Encoding.ASCII.GetBytes(text);
        }
        else
        {
            this.bytes = null;
        }
    }

    /**
     * Returns the name of the protocol such as {@code "HTTP"} in {@code "HTTP/1.0"}.
     */
    public string ProtocolName()
    {
        return protocolName;
    }

    /**
     * Returns the name of the protocol such as {@code 1} in {@code "HTTP/1.0"}.
     */
    public int MajorVersion()
    {
        return majorVersion;
    }

    /**
     * Returns the name of the protocol such as {@code 0} in {@code "HTTP/1.0"}.
     */
    public int MinorVersion()
    {
        return minorVersion;
    }

    /**
     * Returns the full protocol version text such as {@code "HTTP/1.0"}.
     */
    public string Text()
    {
        return text;
    }

    /**
     * Returns {@code true} if and only if the connection is kept alive unless
     * the {@code "Connection"} header is set to {@code "close"} explicitly.
     */
    public bool IsKeepAliveDefault()
    {
        return keepAliveDefault;
    }

    /**
     * Returns the full protocol version text such as {@code "HTTP/1.0"}.
     */
    public override string ToString()
    {
        return Text();
    }

    public override int GetHashCode()
    {
        return (ProtocolName().GetHashCode() * 31 + MajorVersion()) * 31 +
               MinorVersion();
    }

    public override bool Equals(object o)
    {
        if (!(o is HttpVersion))
        {
            return false;
        }

        HttpVersion that = (HttpVersion) o;
        return MinorVersion() == that.MinorVersion() &&
               MajorVersion() == that.MajorVersion() &&
               ProtocolName().Equals(that.ProtocolName());
    }

    public int CompareTo(HttpVersion o)
    {
        int v = string.CompareOrdinal(ProtocolName(), o.ProtocolName());
        if (v != 0)
        {
            return v;
        }

        v = MajorVersion() - o.MajorVersion();
        if (v != 0)
        {
            return v;
        }

        return MinorVersion() - o.MinorVersion();
    }

    internal void Encode(ByteBuf buf)
    {
        if (bytes == null)
        {
            ByteBufUtil.WriteAscii(buf, text);
        }
        else
        {
            buf.WriteBytes(bytes);
        }
    }
}
