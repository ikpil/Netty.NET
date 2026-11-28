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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http.HttpUtil;
using static Netty.NET.Handler.Codec.Http.Tests.HttpHeadersTestUtils;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests;

// CLR: The CharSequence results of getCharsetAsSequence/getMimeType are compared as strings (ToString()).
// NumberFormatException is FormatException and IllegalArgumentException is ArgumentException. InetSocketAddress is
// EndPoint: createUnresolved(host, port) is DnsEndPoint and new InetSocketAddress(InetAddress.getByName(literal),
// port) is IPEndPoint(IPAddress.Parse(literal)). The package-private normalizeAndGetContentLength/validateToken
// are reached through InternalsVisibleTo. String.format "%04X" is the invariant "X4" format.
// testFormatHostnameForHttpFromResolvedAddressWithHostname is CLR-inapplicable: an InetSocketAddress built from
// the InetAddress of getByName("localhost") keeps the host name "localhost" for getHostString(), while the CLR
// counterpart of a resolved address, IPEndPoint, keeps only the IPAddress, and NetUtil.GetHostname returns "the
// numeric IPEndPoint address without DNS lookup" (src/Netty.NET.Common/NetUtil.cs); DnsEndPoint, the unresolved
// counterpart, is covered by testFormatHostnameForHttpFromUnesolvedAddressWithHostname.
public class HttpUtilTest
{

    [Fact]
    public void TestRecognizesOriginForm()
    {
        // Origin form: https://tools.ietf.org/html/rfc7230#section-5.3.1
        Assert.True(HttpUtil.IsOriginForm(new Uri("/where?q=now", UriKind.RelativeOrAbsolute)));
        // Absolute form: https://tools.ietf.org/html/rfc7230#section-5.3.2
        Assert.False(HttpUtil.IsOriginForm(new Uri("http://www.example.org/pub/WWW/TheProject.html",
                UriKind.RelativeOrAbsolute)));
        // Authority form: https://tools.ietf.org/html/rfc7230#section-5.3.3
        Assert.False(HttpUtil.IsOriginForm(new Uri("www.example.com:80", UriKind.RelativeOrAbsolute)));
        // Asterisk form: https://tools.ietf.org/html/rfc7230#section-5.3.4
        Assert.False(HttpUtil.IsOriginForm(new Uri("*", UriKind.RelativeOrAbsolute)));
    }

    [Fact]
    public void TestRecognizesAsteriskForm()
    {
        // Asterisk form: https://tools.ietf.org/html/rfc7230#section-5.3.4
        Assert.True(HttpUtil.IsAsteriskForm(new Uri("*", UriKind.RelativeOrAbsolute)));
        // Origin form: https://tools.ietf.org/html/rfc7230#section-5.3.1
        Assert.False(HttpUtil.IsAsteriskForm(new Uri("/where?q=now", UriKind.RelativeOrAbsolute)));
        // Absolute form: https://tools.ietf.org/html/rfc7230#section-5.3.2
        Assert.False(HttpUtil.IsAsteriskForm(new Uri("http://www.example.org/pub/WWW/TheProject.html",
                UriKind.RelativeOrAbsolute)));
        // Authority form: https://tools.ietf.org/html/rfc7230#section-5.3.3
        Assert.False(HttpUtil.IsAsteriskForm(new Uri("www.example.com:80", UriKind.RelativeOrAbsolute)));
    }

    [Theory]
    [InlineData("http://localhost/\r\n")]
    [InlineData("/r\r\n?q=1")]
    [InlineData("http://localhost/\r\n?q=1")]
    [InlineData("/r\r\n/?q=1")]
    [InlineData("http://localhost/\r\n/?q=1")]
    [InlineData("/r\r\n")]
    [InlineData("http://localhost/ HTTP/1.1\r\n\r\nPOST /p HTTP/1.1\r\n\r\n")]
    [InlineData("/r HTTP/1.1\r\n\r\nPOST /p HTTP/1.1\r\n\r\n")]
    [InlineData("GET ")]
    [InlineData(" GET")]
    [InlineData("HTTP/ 1.1")]
    [InlineData("HTTP/\r0.9")]
    [InlineData("HTTP/\n1.1")]
    public void RequestLineTokenValidationMustRejectInvalidTokens(string token)
    {
        Assert.False(HttpUtil.IsEncodingSafeStartLineToken(new StringCharSequence(token)));
    }

    [Fact]
    public void TestRemoveTransferEncodingIgnoreCase()
    {
        IHttpMessage message = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        message.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, "Chunked");
        Assert.False(message.Headers().IsEmpty());
        HttpUtil.SetTransferEncodingChunked(message, false);
        Assert.True(message.Headers().IsEmpty());
    }

    // Test for https://github.com/netty/netty/issues/1690
    [Fact]
    public void TestGetOperations()
    {
        HttpHeaders headers = new DefaultHttpHeaders();
        headers.Add(Of("Foo"), Of("1"));
        headers.Add(Of("Foo"), Of("2"));

        Assert.Equal("1", headers.Get(Of("Foo")));

        IList<string> values = headers.GetAll(Of("Foo"));
        Assert.Equal(2, values.Count);
        Assert.Equal("1", values[0]);
        Assert.Equal("2", values[1]);
    }

    [Fact]
    public void TestGetCharsetAsRawCharSequence()
    {
        string QUOTES_CHARSET_CONTENT_TYPE = "text/html; charset=\"utf8\"";
        string SIMPLE_CONTENT_TYPE = "text/html";

        IHttpMessage message = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        message.Headers().Set(HttpHeaderNames.CONTENT_TYPE, QUOTES_CHARSET_CONTENT_TYPE);
        Assert.Equal("\"utf8\"", HttpUtil.GetCharsetAsSequence(message).ToString());
        Assert.Equal("\"utf8\"", HttpUtil.GetCharsetAsSequence(Cs(QUOTES_CHARSET_CONTENT_TYPE)).ToString());

        message.Headers().Set(HttpHeaderNames.CONTENT_TYPE, "text/html");
        Assert.Null(HttpUtil.GetCharsetAsSequence(message));
        Assert.Null(HttpUtil.GetCharsetAsSequence(Cs(SIMPLE_CONTENT_TYPE)));
    }

    // CLR: a String passed as a CharSequence argument.
    private static ICharSequence Cs(string s)
    {
        return new StringCharSequence(s);
    }

    [Fact]
    public void TestGetCharset()
    {
        TestGetCharsetUtf8("text/html; charset=utf-8");
    }

    [Fact]
    public void TestGetCharsetNoSpace()
    {
        TestGetCharsetUtf8("text/html;charset=utf-8");
    }

    [Fact]
    public void TestGetCharsetQuoted()
    {
        TestGetCharsetUtf8("text/html; charset=\"utf-8\"");
    }

    [Fact]
    public void TestGetCharsetNoSpaceQuoted()
    {
        TestGetCharsetUtf8("text/html;charset=\"utf-8\"");
    }

    private void TestGetCharsetUtf8(string contentType)
    {
        string UPPER_CASE_NORMAL_CONTENT_TYPE = contentType.ToUpperInvariant();

        IHttpMessage message = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        message.Headers().Set(HttpHeaderNames.CONTENT_TYPE, contentType);
        Assert.Equal(Encoding.UTF8, HttpUtil.GetCharset(message));
        Assert.Equal(Encoding.UTF8, HttpUtil.GetCharset(Cs(contentType)));

        message.Headers().Set(HttpHeaderNames.CONTENT_TYPE, UPPER_CASE_NORMAL_CONTENT_TYPE);
        Assert.Equal(Encoding.UTF8, HttpUtil.GetCharset(message));
        Assert.Equal(Encoding.UTF8, HttpUtil.GetCharset(Cs(UPPER_CASE_NORMAL_CONTENT_TYPE)));
    }

    [Fact]
    public void TestGetCharsetWithWhitespaceBeforeNextParameter()
    {
        // RFC 9110 5.6.6: parameters = *( OWS ";" OWS [ parameter ] ), so optional
        // whitespace is allowed before the semicolon starting the next parameter
        TestGetCharsetUtf8("text/html; charset=utf-8 ; action=\"foo\"");
    }

    [Fact]
    public void TestGetCharsetQuotedWithWhitespaceBeforeNextParameter()
    {
        TestGetCharsetUtf8("text/html; charset=\"utf-8\" ; action=\"foo\"");
    }

    [Fact]
    public void TestGetCharsetWithWhitespaceBeforeNextParameterNonStringCharSequence()
    {
        // must behave identically for a general CharSequence implementation
        ICharSequence contentType = Wrap("text/html; charset=utf-8 ; action=\"foo\"");
        Assert.Equal(Encoding.UTF8, HttpUtil.GetCharset(contentType, Encoding.Latin1));

        ICharSequence emptyCharset = Wrap("text/html; charset= ; action=\"foo\"");
        Assert.Equal(Encoding.Latin1, HttpUtil.GetCharset(emptyCharset, Encoding.Latin1));
    }

    private static ICharSequence Wrap(string s)
    {
        return new AppendableCharSequence(s.Length).Append(new StringCharSequence(s));
    }

    [Fact]
    public void TestGetCharsetWithLeadingWhitespaceIsNotAccepted()
    {
        // Only trailing OWS is grammar-legal per RFC 9110 5.6.6; whitespace after '='
        // is not, so such values keep falling back to the default charset.
        Assert.Equal(Encoding.Latin1,
                HttpUtil.GetCharset(Cs("text/html; charset= utf-8 ; action=\"foo\""), Encoding.Latin1));
        Assert.Equal(Encoding.Latin1,
                HttpUtil.GetCharset(Cs("text/html; charset= ; action=\"foo\""), Encoding.Latin1));
    }

    [Fact]
    public void TestGetCharsetNoLeadingQuotes()
    {
        TestGetCharsetInvalidQuotes("text/html;charset=utf-8\"");
    }

    [Fact]
    public void TestGetCharsetNoTrailingQuotes()
    {
        TestGetCharsetInvalidQuotes("text/html;charset=\"utf-8");
    }

    [Fact]
    public void TestGetCharsetOnlyQuotes()
    {
        TestGetCharsetInvalidQuotes("text/html;charset=\"\"");
    }

    private static void TestGetCharsetInvalidQuotes(string contentType)
    {
        string UPPER_CASE_NORMAL_CONTENT_TYPE = contentType.ToUpperInvariant();

        IHttpMessage message = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        message.Headers().Set(HttpHeaderNames.CONTENT_TYPE, contentType);
        Assert.Equal(Encoding.Latin1, HttpUtil.GetCharset(message, Encoding.Latin1));
        Assert.Equal(Encoding.Latin1, HttpUtil.GetCharset(Cs(contentType), Encoding.Latin1));

        message.Headers().Set(HttpHeaderNames.CONTENT_TYPE, UPPER_CASE_NORMAL_CONTENT_TYPE);
        Assert.Equal(Encoding.Latin1, HttpUtil.GetCharset(message, Encoding.Latin1));
        Assert.Equal(Encoding.Latin1, HttpUtil.GetCharset(Cs(UPPER_CASE_NORMAL_CONTENT_TYPE),
                Encoding.Latin1));
    }

    [Fact]
    public void TestGetCharsetIfNotLastParameter()
    {
        string NORMAL_CONTENT_TYPE_WITH_PARAMETERS = "application/soap-xml; charset=utf-8; "
            + "action=\"http://www.soap-service.by/foo/add\"";

        IHttpMessage message = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST,
            "http://localhost:7788/foo");
        message.Headers().Set(HttpHeaderNames.CONTENT_TYPE, NORMAL_CONTENT_TYPE_WITH_PARAMETERS);

        Assert.Equal(Encoding.UTF8, HttpUtil.GetCharset(message));
        Assert.Equal(Encoding.UTF8, HttpUtil.GetCharset(Cs(NORMAL_CONTENT_TYPE_WITH_PARAMETERS)));

        Assert.Equal("utf-8", HttpUtil.GetCharsetAsSequence(message).ToString());
        Assert.Equal("utf-8", HttpUtil.GetCharsetAsSequence(Cs(NORMAL_CONTENT_TYPE_WITH_PARAMETERS)).ToString());
    }

    [Fact]
    public void TestGetCharset_defaultValue()
    {
        string SIMPLE_CONTENT_TYPE = "text/html";
        string CONTENT_TYPE_WITH_INCORRECT_CHARSET = "text/html; charset=UTFFF";
        string CONTENT_TYPE_WITH_ILLEGAL_CHARSET_NAME = "text/html; charset=!illegal!";

        IHttpMessage message = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        message.Headers().Set(HttpHeaderNames.CONTENT_TYPE, SIMPLE_CONTENT_TYPE);
        Assert.Equal(Encoding.Latin1, HttpUtil.GetCharset(message));
        Assert.Equal(Encoding.Latin1, HttpUtil.GetCharset(Cs(SIMPLE_CONTENT_TYPE)));

        message.Headers().Set(HttpHeaderNames.CONTENT_TYPE, SIMPLE_CONTENT_TYPE);
        Assert.Equal(Encoding.UTF8, HttpUtil.GetCharset(message, Encoding.UTF8));
        Assert.Equal(Encoding.UTF8, HttpUtil.GetCharset(Cs(SIMPLE_CONTENT_TYPE), Encoding.UTF8));

        message.Headers().Set(HttpHeaderNames.CONTENT_TYPE, CONTENT_TYPE_WITH_INCORRECT_CHARSET);
        Assert.Equal(Encoding.Latin1, HttpUtil.GetCharset(message));
        Assert.Equal(Encoding.Latin1, HttpUtil.GetCharset(Cs(CONTENT_TYPE_WITH_INCORRECT_CHARSET)));

        message.Headers().Set(HttpHeaderNames.CONTENT_TYPE, CONTENT_TYPE_WITH_INCORRECT_CHARSET);
        Assert.Equal(Encoding.UTF8, HttpUtil.GetCharset(message, Encoding.UTF8));
        Assert.Equal(Encoding.UTF8,
                     HttpUtil.GetCharset(Cs(CONTENT_TYPE_WITH_INCORRECT_CHARSET), Encoding.UTF8));

        message.Headers().Set(HttpHeaderNames.CONTENT_TYPE, CONTENT_TYPE_WITH_ILLEGAL_CHARSET_NAME);
        Assert.Equal(Encoding.Latin1, HttpUtil.GetCharset(message));
        Assert.Equal(Encoding.Latin1, HttpUtil.GetCharset(Cs(CONTENT_TYPE_WITH_ILLEGAL_CHARSET_NAME)));

        message.Headers().Set(HttpHeaderNames.CONTENT_TYPE, CONTENT_TYPE_WITH_ILLEGAL_CHARSET_NAME);
        Assert.Equal(Encoding.UTF8, HttpUtil.GetCharset(message, Encoding.UTF8));
        Assert.Equal(Encoding.UTF8,
                HttpUtil.GetCharset(Cs(CONTENT_TYPE_WITH_ILLEGAL_CHARSET_NAME), Encoding.UTF8));
    }

    [Fact]
    public void TestGetMimeType()
    {
        string SIMPLE_CONTENT_TYPE = "text/html";
        string NORMAL_CONTENT_TYPE = "text/html; charset=utf-8";

        IHttpMessage message = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        Assert.Null(HttpUtil.GetMimeType(message));
        message.Headers().Set(HttpHeaderNames.CONTENT_TYPE, "");
        Assert.Null(HttpUtil.GetMimeType(message));
        Assert.Null(HttpUtil.GetMimeType(Cs("")));
        message.Headers().Set(HttpHeaderNames.CONTENT_TYPE, SIMPLE_CONTENT_TYPE);
        Assert.Equal("text/html", HttpUtil.GetMimeType(message).ToString());
        Assert.Equal("text/html", HttpUtil.GetMimeType(Cs(SIMPLE_CONTENT_TYPE)).ToString());

        message.Headers().Set(HttpHeaderNames.CONTENT_TYPE, NORMAL_CONTENT_TYPE);
        Assert.Equal("text/html", HttpUtil.GetMimeType(message).ToString());
        Assert.Equal("text/html", HttpUtil.GetMimeType(Cs(NORMAL_CONTENT_TYPE)).ToString());
    }

    [Fact]
    public void TestGetMimeTypeWithWhitespaceBeforeParameter()
    {
        // RFC 9110 8.3.1: parameters = *( OWS ";" OWS [ parameter ] ), so optional
        // whitespace is allowed before the semicolon starting the first parameter
        Assert.Equal("text/html", HttpUtil.GetMimeType(Cs("text/html ; charset=utf-8")).ToString());
        Assert.Equal("text/html", HttpUtil.GetMimeType(Cs("text/html\t; charset=utf-8")).ToString());
        Assert.Equal("text/html", HttpUtil.GetMimeType(Cs("text/html \t ;charset=utf-8")).ToString());
        Assert.Equal("text/html", HttpUtil.GetMimeType(Cs("text/html ;")).ToString());

        IHttpMessage message = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        message.Headers().Set(HttpHeaderNames.CONTENT_TYPE, "application/json ; charset=UTF-8");
        Assert.Equal("application/json", HttpUtil.GetMimeType(message).ToString());
    }

    [Fact]
    public void TestGetContentLengthThrowsNumberFormatException()
    {
        IHttpMessage message = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        message.Headers().Set(HttpHeaderNames.CONTENT_LENGTH, "bar");
        Assert.Throws<FormatException>(() => HttpUtil.GetContentLength(message));
    }

    [Fact]
    public void TestGetContentLengthIntDefaultValueThrowsNumberFormatException()
    {
        IHttpMessage message = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        message.Headers().Set(HttpHeaderNames.CONTENT_LENGTH, "bar");
        Assert.Throws<FormatException>(() => HttpUtil.GetContentLength(message, 1));
    }

    [Fact]
    public void TestGetContentLengthLongDefaultValueThrowsNumberFormatException()
    {
        IHttpMessage message = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        message.Headers().Set(HttpHeaderNames.CONTENT_LENGTH, "bar");
        Assert.Throws<FormatException>(() => HttpUtil.GetContentLength(message, 1L));
    }

    [Fact]
    public void TestDoubleChunkedHeader()
    {
        IHttpMessage message = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        message.Headers().Add(HttpHeaderNames.TRANSFER_ENCODING, "chunked");
        HttpUtil.SetTransferEncodingChunked(message, true);
        IList<string> expected = new List<string> { "chunked" };
        Assert.Equal(expected, message.Headers().GetAll(HttpHeaderNames.TRANSFER_ENCODING));
    }

    private static IList<string> AllPossibleCasesOfContinue()
    {
        IList<string> cases = new List<string>();
        string c = "continue";

        for (int i = 0; i < Math.Pow(2, c.Length); i++)
        {
            StringBuilder sb = new StringBuilder(c.Length);
            int j = i;
            int k = 0;

            while (j > 0)
            {
                if ((j & 1) == 1)
                {
                    sb.Append(char.ToUpperInvariant(c[k++]));
                }
                else
                {
                    sb.Append(c[k++]);
                }

                j >>= 1;
            }

            for (; k < c.Length; k++)
            {
                sb.Append(c[k]);
            }

            cases.Add(sb.ToString());
        }

        return cases;
    }

    [Fact]
    public void TestIs100Continue()
    {
        // test all possible cases of 100-continue
        foreach (string continueCase in AllPossibleCasesOfContinue())
        {
            Run100ContinueTest(HttpVersion.HTTP_1_1, "100-" + continueCase, true);
        }

        Run100ContinueTest(HttpVersion.HTTP_1_1, null, false);
        Run100ContinueTest(HttpVersion.HTTP_1_1, "chocolate=yummy", false);
        Run100ContinueTest(HttpVersion.HTTP_1_0, "100-continue", false);
        IHttpMessage message = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        message.Headers().Set(HttpHeaderNames.EXPECT, "100-continue");
        Run100ContinueTest(message, false);
    }

    private static void Run100ContinueTest(HttpVersion version, string expectations, bool expect)
    {
        IHttpMessage message = new DefaultFullHttpRequest(version, HttpMethod.GET, "/");

        if (expectations != null)
        {
            message.Headers().Set(HttpHeaderNames.EXPECT, expectations);
        }

        Run100ContinueTest(message, expect);
    }

    private static void Run100ContinueTest(IHttpMessage message, bool expected)
    {
        Assert.Equal(expected, HttpUtil.Is100ContinueExpected(message));
        ReferenceCountUtil.Release(message);
    }

    [Fact]
    public void TestContainsUnsupportedExpectation()
    {
        // test all possible cases of 100-continue
        foreach (string continueCase in AllPossibleCasesOfContinue())
        {
            RunUnsupportedExpectationTest(HttpVersion.HTTP_1_1, "100-" + continueCase, false);
        }

        RunUnsupportedExpectationTest(HttpVersion.HTTP_1_1, null, false);
        RunUnsupportedExpectationTest(HttpVersion.HTTP_1_1, "chocolate=yummy", true);
        RunUnsupportedExpectationTest(HttpVersion.HTTP_1_0, "100-continue", false);
        IHttpMessage message = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        message.Headers().Set("Expect", "100-continue");
        RunUnsupportedExpectationTest(message, false);
    }

    private static void RunUnsupportedExpectationTest(HttpVersion version,
                                                      string expectations, bool expect)
    {
        IHttpMessage message = new DefaultFullHttpRequest(version, HttpMethod.GET, "/");

        if (expectations != null)
        {
            message.Headers().Set("Expect", expectations);
        }

        RunUnsupportedExpectationTest(message, expect);
    }

    private static void RunUnsupportedExpectationTest(IHttpMessage message, bool expected)
    {
        Assert.Equal(expected, HttpUtil.IsUnsupportedExpectation(message));
        ReferenceCountUtil.Release(message);
    }

    [Fact]
    public void TestFormatHostnameForHttpFromUnesolvedAddressWithHostname()
    {
        EndPoint socketAddress = new DnsEndPoint("localhost", 80);
        Assert.Equal("localhost", HttpUtil.FormatHostnameForHttp(socketAddress));
    }

    [Fact]
    public void TestIpv6()
    {
        EndPoint socketAddress = new IPEndPoint(IPAddress.Parse("::1"), 8080);
        Assert.Equal("[::1]", HttpUtil.FormatHostnameForHttp(socketAddress));
    }

    [Fact]
    public void TestIpv6Unresolved()
    {
        EndPoint socketAddress = new DnsEndPoint("::1", 8080);
        Assert.Equal("[::1]", HttpUtil.FormatHostnameForHttp(socketAddress));
    }

    [Fact]
    public void TestIpv6UnresolvedExplicitAddress()
    {
        EndPoint socketAddress = new DnsEndPoint("[2001:4860:4860:0:0:0:0:8888]", 8080);
        Assert.Equal("[2001:4860:4860:0:0:0:0:8888]", HttpUtil.FormatHostnameForHttp(socketAddress));
    }

    [Fact]
    public void TestIpv4()
    {
        EndPoint socketAddress = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 8080);
        Assert.Equal("10.0.0.1", HttpUtil.FormatHostnameForHttp(socketAddress));
    }

    [Fact]
    public void TestIpv4Unresolved()
    {
        EndPoint socketAddress = new DnsEndPoint("10.0.0.1", 8080);
        Assert.Equal("10.0.0.1", HttpUtil.FormatHostnameForHttp(socketAddress));
    }

    [Fact]
    public void TestKeepAliveIfConnectionHeaderAbsent()
    {
        IHttpMessage http11Message = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET,
            "http:localhost/http_1_1");
        Assert.True(HttpUtil.IsKeepAlive(http11Message));

        IHttpMessage http10Message = new DefaultHttpRequest(HttpVersion.HTTP_1_0, HttpMethod.GET,
            "http:localhost/http_1_0");
        Assert.False(HttpUtil.IsKeepAlive(http10Message));
    }

    [Fact]
    public void TestKeepAliveIfConnectionHeaderMultipleValues()
    {
        IHttpMessage http11Message = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET,
            "http:localhost/http_1_1");
        http11Message.Headers().Set(
                HttpHeaderNames.CONNECTION, HttpHeaderValues.UPGRADE + ", " + HttpHeaderValues.CLOSE);
        Assert.False(HttpUtil.IsKeepAlive(http11Message));

        http11Message.Headers().Set(
                HttpHeaderNames.CONNECTION, HttpHeaderValues.UPGRADE + ", Close");
        Assert.False(HttpUtil.IsKeepAlive(http11Message));

        http11Message.Headers().Set(
                HttpHeaderNames.CONNECTION, HttpHeaderValues.CLOSE + ", " + HttpHeaderValues.UPGRADE);
        Assert.False(HttpUtil.IsKeepAlive(http11Message));

        http11Message.Headers().Set(
                HttpHeaderNames.CONNECTION, HttpHeaderValues.UPGRADE + ", " + HttpHeaderValues.KEEP_ALIVE);
        Assert.True(HttpUtil.IsKeepAlive(http11Message));
    }

    [Fact]
    public void NormalizeAndGetContentLengthEmpty()
    {
        TestNormalizeAndGetContentLengthInvalidContentLength("");
    }

    [Fact]
    public void NormalizeAndGetContentLengthNotANumber()
    {
        TestNormalizeAndGetContentLengthInvalidContentLength("foo");
    }

    [Fact]
    public void NormalizeAndGetContentLengthNegative()
    {
        TestNormalizeAndGetContentLengthInvalidContentLength("-1");
    }

    private static void TestNormalizeAndGetContentLengthInvalidContentLength(string contentLengthField)
    {
        Assert.Throws<ArgumentException>(
                () => NormalizeAndGetContentLength(new List<string> { contentLengthField }, false, false));
    }

    public static IEnumerable<object[]> ValidTokenChars()
    {
        List<char> list = new List<char>();

        for (char c = '0'; c <= '9'; c++)
        {
            list.Add(c);
        }

        for (char c = 'a'; c <= 'z'; c++)
        {
            list.Add(c);
        }

        for (char c = 'A'; c <= 'Z'; c++)
        {
            list.Add(c);
        }

        // Unreserved characters:
        list.Add('-');
        list.Add('.');
        list.Add('_');
        list.Add('~');

        // Token special characters:
        list.Add('!');
        list.Add('#');
        list.Add('$');
        list.Add('%');
        list.Add('&');
        list.Add('\'');
        list.Add('*');
        list.Add('+');
        list.Add('^');
        list.Add('`');
        list.Add('|');

        return list.Select(c => new object[] { c });
    }

    [Theory]
    [MemberData(nameof(ValidTokenChars))]
    public void TestValidTokenChars(char validChar)
    {
        AsciiString asciiStringToken =
                new AsciiString(new byte[] { (byte) 'G', (byte) 'E', (byte) validChar, (byte) 'T' });
        string token = "GE" + validChar + 'T';
        Assert.Equal(-1, ValidateToken(asciiStringToken));
        Assert.Equal(-1, ValidateToken(new StringCharSequence(token)));
    }

    [Theory]
    [InlineData('(')]
    [InlineData(')')]
    [InlineData(',')]
    [InlineData('/')]
    [InlineData(':')]
    [InlineData(';')]
    [InlineData('<')]
    [InlineData('=')]
    [InlineData('>')]
    [InlineData('?')]
    [InlineData('@')]
    [InlineData('\"')]
    [InlineData('[')]
    [InlineData('\\')]
    [InlineData(']')]
    [InlineData('{')]
    [InlineData('}')]
    [InlineData('\u0000')]
    [InlineData(' ')]
    [InlineData('\u007f')]
    [InlineData('ÿ')]
    public void TestInvalidTokenChars(char invalidChar)
    {
        AsciiString asciiStringToken =
                new AsciiString(new byte[] { (byte) 'G', (byte) 'E', (byte) invalidChar, (byte) 'T' });
        string token = "GE" + invalidChar + 'T';
        Assert.Equal(2, ValidateToken(asciiStringToken));
        Assert.Equal(2, ValidateToken(new StringCharSequence(token)));
    }

    [Theory]
    // High-bit Truncation Candidates (verifying > 0xFF check)
    // These characters are chosen because their lower 8 bits
    // alias to valid US-ASCII 'tchar' values.
    [InlineData('š')] // 0x0161 truncates to 0x61 ('a')
    [InlineData('ġ')] // 0x0121 truncates to 0x21 ('!')
    [InlineData('ȱ')] // 0x0231 truncates to 0x31 ('1')
    [InlineData('͡')]  // 0x0361 truncates to 0x61 ('a')
    public void TestInvalidTokenCharsOutsideAsciiRange(char invalidChar)
    {
        // We use a String here because AsciiString would truncate
        // the char to a byte during construction.
        string token = "GE" + invalidChar + 'T';
        Assert.Equal(2, ValidateToken(new StringCharSequence(token)),
            string.Format(CultureInfo.InvariantCulture, "Character U+{0:X4} should be invalid", (int) invalidChar));
    }
}
