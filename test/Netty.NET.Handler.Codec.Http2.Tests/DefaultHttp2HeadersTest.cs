/*
 * Copyright 2014 The Netty Project
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
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;
using PseudoHeaderName = Netty.NET.Handler.Codec.Http2.IHttp2Headers.PseudoHeaderName;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: AsciiString.of(String) (static import of AsciiString.*) is the private Of(string), which returns new
// AsciiString(s) as AsciiString.of does for a String; a Java String passed directly as a CharSequence is a
// StringCharSequence built by the private Cs(string) (a C# string is not an ICharSequence;
// docs/codec-http-porting.md, Shared CLR adaptations), and CharSequence results that Java compares with a String
// (assertEquals("value1", headers.get(...))) are compared as strings. assertEquals of two CharSequences calls equals,
// which AssertCsEquals keeps (xUnit would compare two ICharSequence, an IReadOnlyList<char>, element by element).
// @ParameterizedTest with @ValueSource/@CsvSource is a [Theory] with InlineData rows, with @MethodSource a [Theory]
// with MemberData; the Stream<Arguments> sources are TheoryData. assertThrows(T.class) is Assert.ThrowsAny<T>, and
// assertThrows(expectedType, ...) with a runtime Class is Record.Exception plus Assert.IsAssignableFrom (JUnit checks
// expectedType.isInstance). Map.Entry is the codec-base IEntry and fail is Assert.Fail. verifyPseudoHeadersFirst is
// internal for ReadOnlyHttp2HeadersTest (the Java static import of this class).
public class DefaultHttp2HeadersTest
{

    [Fact]
    public void NullHeaderNameNotAllowed()
    {
        Assert.ThrowsAny<Http2Exception>(() => new DefaultHttp2Headers().Add(null, Cs("foo")));
    }

    [Fact]
    public void EmptyHeaderNameNotAllowed()
    {
        Assert.ThrowsAny<Http2Exception>(() => new DefaultHttp2Headers().Add(Cs(StringUtil.EMPTY_STRING), Cs("foo")));
    }

    [Theory]
    [InlineData('\0')]
    [InlineData('\n')]
    [InlineData('\r')]
    public void HeaderValueValidationIsEnabledByDefault(char illegalChar)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => new DefaultHttp2Headers().Add(Cs("headername"), Cs("fo" + illegalChar + "o")));
    }

    [Fact]
    public void TestPseudoHeadersMustComeFirstWhenIterating()
    {
        IHttp2Headers headers = NewHeaders();

        VerifyPseudoHeadersFirst(headers);
        VerifyAllPseudoHeadersPresent(headers);
    }

    [Fact]
    public void TestPseudoHeadersWithRemovePreservesPseudoIterationOrder()
    {
        IHttp2Headers headers = NewHeaders();

        IHttp2Headers nonPseudoHeaders = new DefaultHttp2Headers();

        foreach (IEntry<ICharSequence, ICharSequence> entry in headers)
        {
            if (entry.GetKey().Length() == 0 || entry.GetKey().CharAt(0) != ':' &&
                !nonPseudoHeaders.Contains(entry.GetKey()))
            {
                nonPseudoHeaders.Add(entry.GetKey(), entry.GetValue());
            }
        }

        Assert.False(nonPseudoHeaders.IsEmpty());

        // Remove all the non-pseudo headers and verify
        foreach (IEntry<ICharSequence, ICharSequence> nonPseudoHeaderEntry in nonPseudoHeaders)
        {
            Assert.True(headers.Remove(nonPseudoHeaderEntry.GetKey()));
            VerifyPseudoHeadersFirst(headers);
            VerifyAllPseudoHeadersPresent(headers);
        }

        // Add back all non-pseudo headers
        foreach (IEntry<ICharSequence, ICharSequence> nonPseudoHeaderEntry in nonPseudoHeaders)
        {
            headers.Add(nonPseudoHeaderEntry.GetKey(), Of("goo"));
            VerifyPseudoHeadersFirst(headers);
            VerifyAllPseudoHeadersPresent(headers);
        }
    }

    [Fact]
    public void TestPseudoHeadersWithClearDoesNotLeak()
    {
        IHttp2Headers headers = NewHeaders();

        Assert.False(headers.IsEmpty());
        headers.Clear();
        Assert.True(headers.IsEmpty());

        // Combine 2 headers together, make sure pseudo headers stay up front.
        headers.Add(Cs("name1"), Cs("value1")).Scheme(Cs("nothing"));
        VerifyPseudoHeadersFirst(headers);

        IHttp2Headers other = new DefaultHttp2Headers().Add(Cs("name2"), Cs("value2")).Authority(Cs("foo"));
        VerifyPseudoHeadersFirst(other);

        headers.Add(other);
        VerifyPseudoHeadersFirst(headers);

        // Make sure the headers are what we expect them to be, and no leaking behind the scenes.
        Assert.Equal(4, headers.Size());
        Assert.Equal("value1", headers.Get(Cs("name1")).ToString());
        Assert.Equal("value2", headers.Get(Cs("name2")).ToString());
        Assert.Equal("nothing", headers.Scheme().ToString());
        Assert.Equal("foo", headers.Authority().ToString());
    }

    [Fact]
    public void TestSetHeadersOrdersPseudoHeadersCorrectly()
    {
        IHttp2Headers headers = NewHeaders();
        IHttp2Headers other = new DefaultHttp2Headers().Add(Cs("name2"), Cs("value2")).Authority(Cs("foo"));

        headers.Set(other);
        VerifyPseudoHeadersFirst(headers);
        Assert.Equal(other.Size(), headers.Size());
        Assert.Equal("foo", headers.Authority().ToString());
        Assert.Equal("value2", headers.Get(Cs("name2")).ToString());
    }

    [Fact]
    public void TestSetAllOrdersPseudoHeadersCorrectly()
    {
        IHttp2Headers headers = NewHeaders();
        IHttp2Headers other = new DefaultHttp2Headers().Add(Cs("name2"), Cs("value2")).Authority(Cs("foo"));

        int headersSizeBefore = headers.Size();
        headers.SetAll(other);
        VerifyPseudoHeadersFirst(headers);
        VerifyAllPseudoHeadersPresent(headers);
        Assert.Equal(headersSizeBefore + 1, headers.Size());
        Assert.Equal("foo", headers.Authority().ToString());
        Assert.Equal("value2", headers.Get(Cs("name2")).ToString());
    }

    [Fact]
    public void TestHeaderNameValidation()
    {
        IHttp2Headers headers = NewHeaders();

        Assert.ThrowsAny<Http2Exception>(() => headers.Add(Of("Foo"), Of("foo")));
    }

    [Fact]
    public void RejectNonAsciiHeaderNameAsciiString()
    {
        // U+1F631 ("😱") encoded as F0 9F 98 B1 — bytes outside the token grammar that previously
        // slipped past the upper-case-only validator. See issue #11975.
        byte[] buf = { 0xF0, 0x9F, 0x98, 0xB1 };
        IHttp2Headers headers = new DefaultHttp2Headers();
        Assert.ThrowsAny<Http2Exception>(() => headers.Add(new AsciiString(buf), Of("test")));
    }

    [Fact]
    public void RejectNonAsciiHeaderNameCharSequence()
    {
        IHttp2Headers headers = new DefaultHttp2Headers();
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            // Non-ASCII char via the CharSequence path.
            headers.Add(Cs("naÿme"), Cs("test"));
        });
    }

    [Fact]
    public void RejectHighBitHeaderNameCharSequence()
    {
        IHttp2Headers headers = new DefaultHttp2Headers();
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            // U+0100 — above 0xFF, must be rejected by the CharSequence path.
            headers.Add(Cs("naĀme"), Cs("test"));
        });
    }

    [Theory]
    [MemberData(nameof(NonTokenBytesInHeaderName))]
    public void RejectNonTokenCharactersInHeaderName(int illegalByte)
    {
        string name = "n" + (char) illegalByte + "ame";
        IHttp2Headers headers = new DefaultHttp2Headers();
        Assert.ThrowsAny<Http2Exception>(() => headers.Add(Cs(name), Cs("test")));
    }

    public static TheoryData<int> NonTokenBytesInHeaderName()
    {
        return new TheoryData<int>
        {
                // Control characters: every byte in the C0 range that the previous validator silently let through.
                0x00, // NUL
                0x01, // SOH
                0x07, // BEL
                0x08, // BS
                0x09, // HTAB
                0x0A, // LF
                0x0B, // VT
                0x0C, // FF
                0x0D, // CR
                0x1F, // US
                0x7F, // DEL
                // Whitespace and RFC 7230 separators that are not valid token characters.
                0x20, // SP
                ',',
                ';',
                ':',
                '/',
                '=',
                '?',
                '@',
                '(',
                ')',
                '[',
                ']',
                '{',
                '}',
                '<',
                '>',
                '\\',
                '"'
        };
    }

    [Fact]
    public void AcceptValidLowercaseTokenHeaderName()
    {
        // Regression: valid RFC 7230 token (lower-case ALPHA, DIGIT, "!#$%&'*+-.^_`|~") must be accepted.
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Add(Of("x-custom-header"), Of("v"));
        headers.Add(Of("2name"), Of("v"));
        headers.Add(Of("a!#$%&'*+-.^_`|~b"), Of("v"));
        Assert.True(headers.Contains(Cs("x-custom-header")));
        Assert.True(headers.Contains(Cs("2name")));
        Assert.True(headers.Contains(Cs("a!#$%&'*+-.^_`|~b")));
    }

    [Fact]
    public void AcceptPseudoHeaderName()
    {
        // Regression: leading-colon pseudo-header names must remain accepted even though ":" is not a token char.
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Method(Of("GET"));
        headers.Path(Of("/"));
        headers.Scheme(Of("https"));
        headers.Authority(Of("example.com"));
        AssertCsEquals(Of("GET"), headers.Method());
        AssertCsEquals(Of("/"), headers.Path());
    }

    [Fact]
    public void TestClearResetsPseudoHeaderDivision()
    {
        DefaultHttp2Headers http2Headers = new DefaultHttp2Headers();
        http2Headers.Method(Cs("POST"));
        http2Headers.Set(Cs("some"), Cs("value"));
        http2Headers.Clear();
        http2Headers.Method(Cs("GET"));
        Assert.Single(http2Headers.Names());
    }

    [Fact]
    public void TestContainsNameAndValue()
    {
        IHttp2Headers headers = NewHeaders();
        Assert.True(headers.Contains(Cs("name1"), Cs("value2")));
        Assert.False(headers.Contains(Cs("name1"), Cs("Value2")));
        Assert.True(headers.Contains(Cs("2name"), Cs("Value3"), true));
        Assert.False(headers.Contains(Cs("2name"), Cs("Value3"), false));
    }

    [Fact]
    public void TestContainsName()
    {
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Add(CONTENT_LENGTH, Cs("36"));
        Assert.False(headers.Contains(Cs("Content-Length")));
        Assert.True(headers.Contains(Cs("content-length")));
        Assert.True(headers.Contains(CONTENT_LENGTH));
        headers.Remove(CONTENT_LENGTH);
        Assert.False(headers.Contains(Cs("Content-Length")));
        Assert.False(headers.Contains(Cs("content-length")));
        Assert.False(headers.Contains(CONTENT_LENGTH));

        Assert.False(headers.Contains(Cs("non-existent-name")));
        Assert.False(headers.Contains(new AsciiString("non-existent-name")));
    }

    [Fact]
    public void SetMustOverwritePseudoHeaders()
    {
        IHttp2Headers headers = NewHeaders();
        // The headers are already populated with pseudo headers.
        headers.Method(Of("GET"));
        headers.Path(Of("/index2.html"));
        headers.Status(Of("101"));
        headers.Authority(Of("github.com"));
        headers.Scheme(Of("http"));
        headers.Set(Of(":protocol"), Of("http"));
        AssertCsEquals(Of("GET"), headers.Method());
        AssertCsEquals(Of("/index2.html"), headers.Path());
        AssertCsEquals(Of("101"), headers.Status());
        AssertCsEquals(Of("github.com"), headers.Authority());
        AssertCsEquals(Of("http"), headers.Scheme());
    }

    [Theory]
    [InlineData("upgrade", "protocol1")]
    [InlineData("connection", "close")]
    [InlineData("keep-alive", "timeout=5")]
    [InlineData("proxy-connection", "close")]
    [InlineData("transfer-encoding", "chunked")]
    [InlineData("te", "something-else")]
    public void PossibleToAddConnectionHeaders(string name, string value)
    {
        IHttp2Headers headers = NewHeaders();
        headers.Add(Cs(name), Cs(value));
        Assert.True(headers.Contains(Cs(name), Cs(value)));
    }

    [Theory]
    [MemberData(nameof(InvalidPseudoHeaders))]
    public void HeaderValueValidation(string name, string value)
    {
        // The second `true` parameter enables header value validation:
        IHttp2Headers headers = new DefaultHttp2Headers(true, true, 10);
        Type expectedType =
                value.Length == 0 ? typeof(Http2Exception) : typeof(ArgumentException);
        Exception thrown = Record.Exception(() => headers.Add(Cs(name), Cs(value)));
        Assert.NotNull(thrown);
        Assert.IsAssignableFrom(expectedType, thrown);
    }

    public static TheoryData<string, string> InvalidPseudoHeaders()
    {
        return new TheoryData<string, string>
        {
                // NUL character (0x00)
                { ":method", "GET\0" },
                { ":method", "\0GET" },
                { ":scheme", "http\0" },
                { ":path", "/path\0with\0nulls" },
                { ":authority", "example.com\0" },

                // CR character (0x0D)
                { ":method", "GET\r" },
                { ":scheme", "http\r" },
                { ":path", "/path\r" },
                { ":authority", "example.com\r" },

                // LF character (0x0A)
                { ":method", "GET\n" },
                { ":scheme", "http\n" },
                { ":path", "/path\n" },
                { ":authority", "example.com\n" },

                // CR LF sequence
                { ":method", "GET\r\n" },
                { ":scheme", "http\r\n" },
                { ":path", "/path\r\n" },
                { ":authority", "example.com\r\n" },

                // DEL character (0x7F)
                { ":method", "GET\u007F" },
                { ":scheme", "http\u007F" },
                { ":path", "/path\u007F" },
                { ":authority", "example.com\u007F" },

                // Control characters 0x01-0x08
                { ":method", "GET\u0001" },
                { ":method", "GET\u0002" },
                { ":method", "GET\u0003" },
                { ":method", "GET\u0004" },
                { ":method", "GET\u0005" },
                { ":method", "GET\u0006" },
                { ":method", "GET\u0007" },
                { ":method", "GET\u0008" },

                // Control characters 0x0B, 0x0C
                { ":method", "GET\u000B" },
                { ":method", "GET\u000C" },

                // Control characters 0x0E-0x1F
                { ":method", "GET\u000E" },
                { ":method", "GET\u000F" },
                { ":method", "GET\u0010" },
                { ":method", "GET\u0011" },
                { ":method", "GET\u0012" },
                { ":method", "GET\u0013" },
                { ":method", "GET\u0014" },
                { ":method", "GET\u0015" },
                { ":method", "GET\u0016" },
                { ":method", "GET\u0017" },
                { ":method", "GET\u0018" },
                { ":method", "GET\u0019" },
                { ":method", "GET\u001A" },
                { ":method", "GET\u001B" },
                { ":method", "GET\u001C" },
                { ":method", "GET\u001D" },
                { ":method", "GET\u001E" },
                { ":method", "GET\u001F" },

                // Multiple illegal characters
                { ":path", "/\0\r\n\u007F" },
                { ":authority", "\u0001\u001F\u007F" },

                // Embedded in middle of value
                { ":path", "/path/with\u0000embedded/nul" },
                { ":authority", "exam\u000Bple.com" },
                { ":scheme", "ht\u001Ftp" },
                { ":method", "GE\u007FT" }
        };
    }

    private static void VerifyAllPseudoHeadersPresent(IHttp2Headers headers)
    {
        foreach (PseudoHeaderName pseudoName in PseudoHeaderName.Values())
        {
            Assert.NotNull(headers.Get(pseudoName.Value()));
        }
    }

    internal static void VerifyPseudoHeadersFirst(IHttp2Headers headers)
    {
        ICharSequence lastNonPseudoName = null;

        foreach (IEntry<ICharSequence, ICharSequence> entry in headers)
        {
            if (entry.GetKey().Length() == 0 || entry.GetKey().CharAt(0) != ':')
            {
                lastNonPseudoName = entry.GetKey();
            }
            else if (lastNonPseudoName != null)
            {
                Assert.Fail("All pseudo headers must be first in iteration. Pseudo header " + entry.GetKey() +
                        " is after a non pseudo header " + lastNonPseudoName);
            }
        }
    }

    private static IHttp2Headers NewHeaders()
    {
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Add(Of("name1"), Of("value1"), Of("value2"));
        headers.Method(Of("POST"));
        headers.Add(Of("2name"), Of("value3"));
        headers.Path(Of("/index.html"));
        headers.Status(Of("200"));
        headers.Authority(Of("netty.io"));
        headers.Add(Of("name3"), Of("value4"));
        headers.Scheme(Of("https"));
        headers.Add(Of(":protocol"), Of("websocket"));
        return headers;
    }

    // CLR: AsciiString.of(String) (see the class comment).
    private static AsciiString Of(string s)
    {
        return new AsciiString(s);
    }

    // CLR: a Java String passed as a CharSequence (see the class comment).
    private static ICharSequence Cs(string s)
    {
        return new StringCharSequence(s);
    }

    // CLR: assertEquals(Object, Object) of two CharSequences, which calls equals (see the class comment).
    private static void AssertCsEquals(ICharSequence expected, ICharSequence actual)
    {
        Assert.True(Equals(expected, actual), "expected: <" + expected + "> but was: <" + actual + ">");
    }
}
