/*
 * Copyright 2025 The Netty Project
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
using Netty.NET.Common;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests;

// CLR: assertThrows(IllegalArgumentException.class, executable[, message]) is Assert.Throws<ArgumentException>
// (XunitAssertMessages.AssertM for the message overloads); Integer.toHexString is the invariant lower-case "x" format.
// @ParameterizedTest @ValueSource(strings) is a [Theory] with MemberData rows in the source order; the source repeats
// some tokens (" HTTP\r", "HTTP \n", "\nHTTP ", " HTTP\n", "HTT\nTP", " HTT\rP", " HTT\nP"), which are kept, and xUnit
// runs a repeated row once. The package-private test methods are public, as xUnit requires. HttpVersion's
// package-private constructor and valueOf(String, boolean) are internal and reached through InternalsVisibleTo, as the
// Java test reaches them through package access. isEncodingSafeStartLineToken(String) receives the text as a
// StringCharSequence because a C# string is not an ICharSequence.
public class HttpVersionParsingTest
{

    [Fact]
    public void TestStandardVersions()
    {
        HttpVersion v10 = HttpVersion.ValueOf("HTTP/1.0");
        HttpVersion v11 = HttpVersion.ValueOf("HTTP/1.1");

        Assert.Same(HttpVersion.HTTP_1_0, v10);
        Assert.Same(HttpVersion.HTTP_1_1, v11);

        Assert.Equal("HTTP", v10.ProtocolName());
        Assert.Equal(1, v10.MajorVersion());
        Assert.Equal(0, v10.MinorVersion());

        Assert.Equal("HTTP", v11.ProtocolName());
        Assert.Equal(1, v11.MajorVersion());
        Assert.Equal(1, v11.MinorVersion());
    }

    [Fact]
    public void TestLowerCaseProtocolNameNonStrict()
    {
        HttpVersion version = HttpVersion.ValueOf("http/1.1");
        Assert.Equal("HTTP", version.ProtocolName());
        Assert.Equal(1, version.MajorVersion());
        Assert.Equal(1, version.MinorVersion());
        Assert.Equal("HTTP/1.1", version.Text());
    }

    [Fact]
    public void TestMixedCaseProtocolNameNonStrict()
    {
        HttpVersion version = HttpVersion.ValueOf("hTtP/1.0");
        Assert.Equal("HTTP", version.ProtocolName());
        Assert.Equal(1, version.MajorVersion());
        Assert.Equal(0, version.MinorVersion());
        Assert.Equal("HTTP/1.0", version.Text());
    }

    [Fact]
    public void TestCustomLowerCaseProtocolNonStrict()
    {
        HttpVersion version = HttpVersion.ValueOf("mqtt/5.0");
        Assert.Equal("MQTT", version.ProtocolName());
        Assert.Equal(5, version.MajorVersion());
        Assert.Equal(0, version.MinorVersion());
        Assert.Equal("MQTT/5.0", version.Text());
    }

    [Fact]
    public void TestCustomVersionNonStrict()
    {
        HttpVersion version = HttpVersion.ValueOf("MyProto/2.3");
        Assert.Equal("MYPROTO", version.ProtocolName()); // uppercased
        Assert.Equal(2, version.MajorVersion());
        Assert.Equal(3, version.MinorVersion());
        Assert.Equal("MYPROTO/2.3", version.Text());
    }

    [Fact]
    public void TestCustomVersionStrict()
    {
        HttpVersion version = new HttpVersion("HTTP/1.1", true, true);
        Assert.Equal("HTTP", version.ProtocolName());
        Assert.Equal(1, version.MajorVersion());
        Assert.Equal(1, version.MinorVersion());
    }

    [Fact]
    public void TestCustomVersionStrictFailsOnLongVersion()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
                new HttpVersion("HTTP/10.1", true, true)
        );
        Assert.True(ex.Message.Contains("invalid version format"));
    }

    [Fact]
    public void TestInvalidFormatMissingSlash()
    {
        Assert.Throws<ArgumentException>(() =>
                HttpVersion.ValueOf("HTTP1.1")
        );
    }

    [Fact]
    public void TestInvalidFormatWhitespaceInProtocol()
    {
        Assert.Throws<ArgumentException>(() =>
                HttpVersion.ValueOf("HT TP/1.1")
        );
    }

    [Fact]
    public void TestLeadingNulInStrictValueOfIsRejected()
    {
        string text = (char) 0x00 + "HTTP/1.1";
        Assert.Throws<ArgumentException>(() => HttpVersion.ValueOf(text, true),
                "leading NUL must be rejected");
    }

    [Fact]
    public void TestTrailingNulInStrictValueOfIsRejected()
    {
        string text = "HTTP/1.1" + (char) 0x00;
        Assert.Throws<ArgumentException>(() => HttpVersion.ValueOf(text, true),
                "trailing NUL must be rejected");
    }

    [Fact]
    public void TestLeadingControlCharInStrictValueOfIsRejected()
    {
        // CR, LF, VT and FF must all be rejected in the version token.
        foreach (int control in new int[] {0x0D, 0x0A, 0x0B, 0x0C})
        {
            string text = (char) control + "HTTP/1.1";
            Assert.Throws<ArgumentException>(() => HttpVersion.ValueOf(text, true),
                    "control char 0x" + control.ToString("x", CultureInfo.InvariantCulture) + " must be rejected");
        }
    }

    [Fact]
    public void TestLeadingSpaceInStrictValueOfIsRejected()
    {
        // A leading space in the version token must be rejected.
        Assert.Throws<ArgumentException>(() => HttpVersion.ValueOf(" HTTP/1.1", true),
                "leading space must be rejected");
    }

    [Fact]
    public void TestLeadingNulInNonStrictValueOfIsRejected()
    {
        string text = (char) 0x00 + "HTTP/1.1";
        Assert.Throws<ArgumentException>(() => HttpVersion.ValueOf(text),
                "leading NUL must be rejected in non-strict mode too");
    }

    [Fact]
    public void TestValidVersionsResolveCorrectly()
    {
        Assert.Same(HttpVersion.HTTP_1_1, HttpVersion.ValueOf("HTTP/1.1", true));
        Assert.Same(HttpVersion.HTTP_1_0, HttpVersion.ValueOf("HTTP/1.0"));
        Assert.Equal("ICAP", HttpVersion.ValueOf("icap/1.0").ProtocolName());
    }

    public static readonly TheoryData<string> IllegalTokens = new TheoryData<string>
    {
            "HTTP ",
            " HTTP",
            "H TTP",
            " HTTP ",
            "HTTP\r",
            "HTTP\n",
            "HTTP\r\n",
            "HTT\rP",
            "HTT\nP",
            "HTT\r\nP",
            "\rHTTP",
            "\nHTTP",
            "\r\nHTTP",
            " \r\nHTTP",
            "\r \nHTTP",
            "\r\n HTTP",
            "\r\nHTTP ",
            "\nHTTP ",
            "\rHTTP ",
            "\r HTTP",
            " \rHTTP",
            "\nHTTP ",
            "\n HTTP",
            " \nHTTP",
            "HTTP \n",
            "HTTP \r",
            " HTTP\r",
            " HTTP\r",
            "HTTP \n",
            " HTTP\n",
            " HTTP\n",
            "HTT\nTP",
            "HTT\rTP",
            " HTT\rP",
            " HTT\rP",
            "HTT\nTP",
            " HTT\nP",
            " HTT\nP",
    };

    [Theory]
    [MemberData(nameof(IllegalTokens))]
    public void HttpVersionMustRejectIllegalTokens(string protocol)
    {
        try
        {
            HttpVersion httpVersion = new HttpVersion(protocol, 1, 0, true);
            // If no exception is thrown, then the version must have been sanitized and made safe.
            Assert.True(HttpUtil.IsEncodingSafeStartLineToken(new StringCharSequence(httpVersion.Text())));
        }
        catch (ArgumentException)
        {
            // Throwing is good.
        }
    }
}
