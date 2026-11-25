/*
 * Copyright 2013 The Netty Project
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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;
using static Netty.NET.Handler.Codec.Http.Tests.HttpHeadersTestUtils;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests;

// CLR: @ParameterizedTest @ValueSource(strings) is a [Theory] with one [InlineData] per value; the display-name pattern
// of @ParameterizedTest(name = ...) has no xUnit counterpart. Package-private JUnit methods are public xUnit methods.
// CharsetUtil.US_ASCII is Encoding.ASCII (docs/codec-smtp-porting.md). IllegalArgumentException is ArgumentException and
// NumberFormatException is FormatException (docs/codec-base-porting.md; HttpObjectDecoder/HttpUtil throw
// FormatException where Java throws NumberFormatException); assertInstanceOf and AssertJ's isInstanceOf are
// Assert.IsAssignableFrom, hasMessageContaining is Assert.Contains on Message, and the asserts with messages use
// XunitAssertMessages.AssertM. Java passes Strings as CharSequence (HttpHeadersTestUtils.of and
// AsciiString.contentEqualsIgnoreCase(String, String)); C# string is not an ICharSequence, so they are wrapped with Of(...)
// (StringCharSequence). The anonymous ChannelInboundHandlerAdapter of reentrantClose is the named nested
// ReentrantCloseHandler, ctx.close() is ctx.CloseAsync(); new Exception(cause) keeps the cause as InnerException and
// printStackTrace() writes the exception to Console.Error. Integer.toHexString is ToString("x") with the invariant
// culture. The deprecated-free constructors HttpRequestDecoder(int, int, int) and HttpRequestDecoder(HttpDecoderConfig)
// are used as in Java.
public class HttpRequestDecoderTest
{
    private static readonly byte[] CONTENT_CRLF_DELIMITERS = CreateContent("\r\n");
    private static readonly byte[] CONTENT_LF_DELIMITERS = CreateContent("\n");
    private static readonly byte[] CONTENT_MIXED_DELIMITERS = CreateContent("\r\n", "\n");
    private const int CONTENT_LENGTH = 8;

    private static byte[] CreateContent(params string[] lineDelimiters)
    {
        string lineDelimiter;
        string lineDelimiter2;
        if (lineDelimiters.Length == 2)
        {
            lineDelimiter = lineDelimiters[0];
            lineDelimiter2 = lineDelimiters[1];
        }
        else
        {
            lineDelimiter = lineDelimiters[0];
            lineDelimiter2 = lineDelimiters[0];
        }
        return Encoding.ASCII.GetBytes("GET /some/path?foo=bar&wibble=eek HTTP/1.1" + "\r\n" +
                "Upgrade: WebSocket" + lineDelimiter2 +
                "Connection: Upgrade" + lineDelimiter +
                "Host: localhost" + lineDelimiter2 +
                "Accept: */*" + lineDelimiter +
                "Origin: http://localhost:8080" + lineDelimiter +
                "Sec-WebSocket-Key1: 10  28 8V7 8 48     0" + lineDelimiter2 +
                "Sec-WebSocket-Key2: 8 Xt754O3Q3QW 0   _60" + lineDelimiter +
                "Content-Length: " + CONTENT_LENGTH + lineDelimiter2 +
                "\r\n"  +
                "12345678");
    }

    [Fact]
    public void TestDecodeWholeRequestAtOnceCRLFDelimiters()
    {
        TestDecodeWholeRequestAtOnce(CONTENT_CRLF_DELIMITERS);
    }

    [Fact]
    public void TestDecodeWholeRequestAtOnceLFDelimiters()
    {
        TestDecodeWholeRequestAtOnce(CONTENT_LF_DELIMITERS);
    }

    [Fact]
    public void TestDecodeWholeRequestAtOnceMixedDelimiters()
    {
        TestDecodeWholeRequestAtOnce(CONTENT_MIXED_DELIMITERS);
    }

    [Fact]
    public void TestDecodeWholeRequestAtOnceFailesWithLFDelimiters()
    {
        TestDecodeWholeRequestAtOnce(CONTENT_LF_DELIMITERS, HttpObjectDecoder.DEFAULT_MAX_HEADER_SIZE, true, true);
    }

    [Fact]
    public void TestDecodeWholeRequestAtOnceFailsWithMixedDelimiters()
    {
        TestDecodeWholeRequestAtOnce(CONTENT_MIXED_DELIMITERS, HttpObjectDecoder.DEFAULT_MAX_HEADER_SIZE, true, true);
    }

    [Fact]
    public void TestDecodeWholeRequestAtOnceMixedDelimitersWithIntegerOverflowOnMaxBodySize()
    {
        TestDecodeWholeRequestAtOnce(CONTENT_MIXED_DELIMITERS, int.MaxValue, false, false);
        TestDecodeWholeRequestAtOnce(CONTENT_MIXED_DELIMITERS, int.MaxValue - 1, false, false);
    }

    private static void TestDecodeWholeRequestAtOnce(byte[] content)
    {
        TestDecodeWholeRequestAtOnce(content, HttpObjectDecoder.DEFAULT_MAX_HEADER_SIZE, false, false);
    }

    private static void TestDecodeWholeRequestAtOnce(byte[] content, int maxHeaderSize, bool strictLineParsing,
                                                     bool expectFailure)
    {
        HttpDecoderConfig config = new HttpDecoderConfig()
                .SetMaxHeaderSize(maxHeaderSize)
                .SetStrictLineParsing(strictLineParsing);
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder(config));
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(content)));
        IHttpRequest req = channel.ReadInbound<IHttpRequest>();
        Assert.NotNull(req);
        if (expectFailure)
        {
            Assert.True(req.DecoderResult().IsFailure());
            Assert.IsAssignableFrom<InvalidLineSeparatorException>(req.DecoderResult().Cause());
        }
        else
        {
            Assert.False(req.DecoderResult().IsFailure());
            CheckHeaders(req.Headers());
            ILastHttpContent c = channel.ReadInbound<ILastHttpContent>();
            Assert.Equal(CONTENT_LENGTH, c.Content.ReadableBytes);
            Assert.Equal(
                    Unpooled.WrappedBuffer(content, content.Length - CONTENT_LENGTH, CONTENT_LENGTH),
                    c.Content.ReadSlice(CONTENT_LENGTH));
            c.Release();
        }

        Assert.False(channel.Finish());
        Assert.Null(channel.ReadInbound());
    }

    private static void CheckHeaders(HttpHeaders headers)
    {
        Assert.Equal(8, headers.Names().Count);
        CheckHeader(headers, "Upgrade", "WebSocket");
        CheckHeader(headers, "Connection", "Upgrade");
        CheckHeader(headers, "Host", "localhost");
        CheckHeader(headers, "Accept", "*/*");
        CheckHeader(headers, "Origin", "http://localhost:8080");
        CheckHeader(headers, "Sec-WebSocket-Key1", "10  28 8V7 8 48     0");
        CheckHeader(headers, "Sec-WebSocket-Key2", "8 Xt754O3Q3QW 0   _60");
        CheckHeader(headers, "Content-Length", CONTENT_LENGTH.ToString(CultureInfo.InvariantCulture));
    }

    private static void CheckHeader(HttpHeaders headers, string name, string value)
    {
        IList<string> header1 = headers.GetAll(Of(name));
        Assert.Equal(1, header1.Count);
        Assert.Equal(value, header1[0]);
    }

    [Fact]
    public void TestDecodeWholeRequestInMultipleStepsCRLFDelimiters()
    {
        TestDecodeWholeRequestInMultipleSteps(CONTENT_CRLF_DELIMITERS, true, false);
    }

    [Fact]
    public void TestDecodeWholeRequestInMultipleStepsLFDelimiters()
    {
        TestDecodeWholeRequestInMultipleSteps(CONTENT_LF_DELIMITERS, false, false);
    }

    [Fact]
    public void TestDecodeWholeRequestInMultipleStepsMixedDelimiters()
    {
        TestDecodeWholeRequestInMultipleSteps(CONTENT_MIXED_DELIMITERS, false, false);
    }

    [Fact]
    public void TestDecodeWholeRequestInMultipleStepsFailsWithLFDelimiters()
    {
        TestDecodeWholeRequestInMultipleSteps(CONTENT_LF_DELIMITERS, true, true);
    }

    [Fact]
    public void TestDecodeWholeRequestInMultipleStepsFailsWithMixedDelimiters()
    {
        TestDecodeWholeRequestInMultipleSteps(CONTENT_MIXED_DELIMITERS, true, true);
    }

    private static void TestDecodeWholeRequestInMultipleSteps(
            byte[] content, bool strictLineParsing, bool expectFailure)
    {
        for (int i = 1; i < content.Length; i++)
        {
            TestDecodeWholeRequestInMultipleSteps(content, i, strictLineParsing, expectFailure);
        }
    }

    private static void TestDecodeWholeRequestInMultipleSteps(
            byte[] content, int fragmentSize, bool strictLineParsing, bool expectFailure)
    {
        HttpDecoderConfig config = new HttpDecoderConfig()
                .SetStrictLineParsing(strictLineParsing);
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder(config));
        int headerLength = content.Length - CONTENT_LENGTH;

        // split up the header
        for (int a = 0; a < headerLength;)
        {
            int amount = fragmentSize;
            if (a + amount > headerLength)
            {
                amount = headerLength -  a;
            }

            // if header is done it should produce an HttpRequest
            channel.WriteInbound(Unpooled.CopiedBuffer(content, a, amount));
            a += amount;
        }

        for (int i = CONTENT_LENGTH; i > 0; i --)
        {
            // Should produce HttpContent
            channel.WriteInbound(Unpooled.CopiedBuffer(content, content.Length - i, 1));
        }

        IHttpRequest req = channel.ReadInbound<IHttpRequest>();
        Assert.NotNull(req);
        if (expectFailure)
        {
            Assert.True(req.DecoderResult().IsFailure());
            Assert.IsAssignableFrom<InvalidLineSeparatorException>(req.DecoderResult().Cause());
            return; // No more messages will be produced.
        }
        Assert.False(req.DecoderResult().IsFailure());
        CheckHeaders(req.Headers());

        for (int i = CONTENT_LENGTH; i > 1; i --)
        {
            IHttpContent c = channel.ReadInbound<IHttpContent>();
            Assert.Equal(1, c.Content.ReadableBytes);
            Assert.Equal(content[content.Length - i], c.Content.ReadByte());
            c.Release();
        }

        ILastHttpContent last = channel.ReadInbound<ILastHttpContent>();
        Assert.Equal(1, last.Content.ReadableBytes);
        Assert.Equal(content[content.Length - 1], last.Content.ReadByte());
        last.Release();

        Assert.False(channel.Finish());
        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestMultiLineHeader()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        string crlf = "\r\n";
        string request =  "GET /some/path HTTP/1.1" + crlf +
                "Host: localhost" + crlf +
                "MyTestHeader: part1" + crlf +
                "              newLinePart2" + crlf +
                "MyTestHeader2: part21" + crlf +
                "\t            newLinePart22"
                + crlf + crlf;
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(request, Encoding.ASCII)));
        IHttpRequest req = channel.ReadInbound<IHttpRequest>();
        Assert.Equal("part1 newLinePart2", req.Headers().Get(Of("MyTestHeader")));
        Assert.Equal("part21 newLinePart22", req.Headers().Get(Of("MyTestHeader2")));

        ILastHttpContent c = channel.ReadInbound<ILastHttpContent>();
        c.Release();

        Assert.False(channel.Finish());
        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestEmptyHeaderValue()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        string crlf = "\r\n";
        string request =  "GET /some/path HTTP/1.1" + crlf +
                "Host: localhost" + crlf +
                "EmptyHeader:" + crlf + crlf;
        channel.WriteInbound(Unpooled.CopiedBuffer(request, Encoding.ASCII));
        IHttpRequest req = channel.ReadInbound<IHttpRequest>();
        Assert.Equal("", req.Headers().Get(Of("EmptyHeader")));
    }

    [Fact]
    public void TestSingleTrailingHeader()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        string request = "POST / HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n" +
                "5\r\n" +
                "hello\r\n" +
                "0\r\n" +
                "X-Checksum: abc123\r\n" +
                "\r\n";
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(request, Encoding.ASCII)));
        IHttpRequest req = channel.ReadInbound<IHttpRequest>();
        Assert.False(req.DecoderResult().IsFailure());
        IHttpContent body = channel.ReadInbound<IHttpContent>();
        body.Release();
        ILastHttpContent last = channel.ReadInbound<ILastHttpContent>();
        Assert.False(last.DecoderResult().IsFailure());
        Assert.Equal("abc123", last.TrailingHeaders().Get(Of("X-Checksum")));
        last.Release();
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestMultiLineTrailingHeader()
    {
        // Regression: folded trailer values previously threw UnsupportedOperationException
        // because trailingHeaders().getAll() returns an AbstractList that does not implement set().
        // Note: obs-fold in trailers is permitted as trailers are field-lines per
        // https://www.rfc-editor.org/rfc/rfc9112#section-5.2
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        string request = "POST / HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n" +
                "5\r\n" +
                "hello\r\n" +
                "0\r\n" +
                "X-Long: part1\r\n" +
                "        part2\r\n" +
                "\t\t\t  part3\r\n" +
                "X-Short: value\r\n" +
                "\r\n";
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(request, Encoding.ASCII)));
        IHttpRequest req = channel.ReadInbound<IHttpRequest>();
        Assert.False(req.DecoderResult().IsFailure());
        IHttpContent body = channel.ReadInbound<IHttpContent>();
        body.Release();
        ILastHttpContent last = channel.ReadInbound<ILastHttpContent>();
        Assert.False(last.DecoderResult().IsFailure());
        Assert.Equal("part1 part2 part3", last.TrailingHeaders().Get(Of("X-Long")));
        Assert.Equal("value", last.TrailingHeaders().Get(Of("X-Short")));
        last.Release();
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestForbiddenTrailingHeadersAreDropped()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        string request = "POST / HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n" +
                "5\r\n" +
                "hello\r\n" +
                "0\r\n" +
                HttpHeaderNames.CONTENT_LENGTH + ": 5\r\n" +
                HttpHeaderNames.TRANSFER_ENCODING + ": chunked\r\n" +
                "X-Custom: keep\r\n" +
                HttpHeaderNames.TRAILER + ": X-Checksum\r\n" + // covering post-loop flush path
                "\r\n";
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(request, Encoding.ASCII)));
        IHttpRequest req = channel.ReadInbound<IHttpRequest>();
        Assert.False(req.DecoderResult().IsFailure());
        IHttpContent body = channel.ReadInbound<IHttpContent>();
        body.Release();
        ILastHttpContent last = channel.ReadInbound<ILastHttpContent>();
        Assert.False(last.DecoderResult().IsFailure());
        Assert.Null(last.TrailingHeaders().Get(HttpHeaderNames.CONTENT_LENGTH));
        Assert.Null(last.TrailingHeaders().Get(HttpHeaderNames.TRANSFER_ENCODING));
        Assert.Null(last.TrailingHeaders().Get(HttpHeaderNames.TRAILER));
        Assert.Equal("keep", last.TrailingHeaders().Get(Of("X-Custom")));
        last.Release();
        Assert.False(channel.Finish());
    }

    [Fact]
    public void Test100Continue()
    {
        HttpRequestDecoder decoder = new HttpRequestDecoder();
        EmbeddedChannel channel = new EmbeddedChannel(decoder);
        string oversized =
                "PUT /file HTTP/1.1\r\n" +
                "Expect: 100-continue\r\n" +
                "Content-Length: 1048576000\r\n\r\n";

        channel.WriteInbound(Unpooled.CopiedBuffer(oversized, Encoding.ASCII));
        Assert.IsAssignableFrom<IHttpRequest>(channel.ReadInbound());

        // At this point, we assume that we sent '413 Entity Too Large' to the peer without closing the connection
        // so that the client can try again.
        decoder.Reset();

        string query = "GET /max-file-size HTTP/1.1\r\n\r\n";
        channel.WriteInbound(Unpooled.CopiedBuffer(query, Encoding.ASCII));
        Assert.IsAssignableFrom<IHttpRequest>(channel.ReadInbound());
        Assert.IsAssignableFrom<ILastHttpContent>(channel.ReadInbound());

        Assert.False(channel.Finish());
    }

    [Fact]
    public void Test100ContinueWithBadClient()
    {
        HttpRequestDecoder decoder = new HttpRequestDecoder();
        EmbeddedChannel channel = new EmbeddedChannel(decoder);
        string oversized =
                "PUT /file HTTP/1.1\r\n" +
                "Expect: 100-continue\r\n" +
                "Content-Length: 1048576000\r\n\r\n" +
                "WAY_TOO_LARGE_DATA_BEGINS";

        channel.WriteInbound(Unpooled.CopiedBuffer(oversized, Encoding.ASCII));
        Assert.IsAssignableFrom<IHttpRequest>(channel.ReadInbound());

        IHttpContent prematureData = channel.ReadInbound<IHttpContent>();
        prematureData.Release();

        Assert.Null(channel.ReadInbound());

        // At this point, we assume that we sent '413 Entity Too Large' to the peer without closing the connection
        // so that the client can try again.
        decoder.Reset();

        string query = "GET /max-file-size HTTP/1.1\r\n\r\n";
        channel.WriteInbound(Unpooled.CopiedBuffer(query, Encoding.ASCII));
        Assert.IsAssignableFrom<IHttpRequest>(channel.ReadInbound());
        Assert.IsAssignableFrom<ILastHttpContent>(channel.ReadInbound());

        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestMessagesSplitBetweenMultipleBuffers()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        string crlf = "\r\n";
        string str1 = "GET /some/path HTTP/1.1" + crlf +
                "Host: localhost1" + crlf + crlf +
                "GET /some/other/path HTTP/1.0" + crlf +
                "Hos";
        string str2 = "t: localhost2" + crlf +
                "content-length: 0" + crlf + crlf;
        channel.WriteInbound(Unpooled.CopiedBuffer(str1, Encoding.ASCII));
        IHttpRequest req = channel.ReadInbound<IHttpRequest>();
        Assert.Equal(HttpVersion.HTTP_1_1, req.ProtocolVersion());
        Assert.Equal("/some/path", req.Uri());
        Assert.Equal(1, req.Headers().Size());
        Assert.True(AsciiString.ContentEqualsIgnoreCase(Of("localhost1"), Of(req.Headers().Get(HOST))));
        ILastHttpContent cnt = channel.ReadInbound<ILastHttpContent>();
        cnt.Release();

        channel.WriteInbound(Unpooled.CopiedBuffer(str2, Encoding.ASCII));
        req = channel.ReadInbound<IHttpRequest>();
        Assert.Equal(HttpVersion.HTTP_1_0, req.ProtocolVersion());
        Assert.Equal("/some/other/path", req.Uri());
        Assert.Equal(2, req.Headers().Size());
        Assert.True(AsciiString.ContentEqualsIgnoreCase(Of("localhost2"), Of(req.Headers().Get(HOST))));
        Assert.True(AsciiString.ContentEqualsIgnoreCase(Of("0"), Of(req.Headers().Get(HttpHeaderNames.CONTENT_LENGTH))));
        cnt = channel.ReadInbound<ILastHttpContent>();
        cnt.Release();
        Assert.False(channel.FinishAndReleaseAll());
    }

    [Fact]
    public void TestTooLargeInitialLine()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder(10, 1024, 1024));
        string requestStr = "GET /some/path HTTP/1.1\r\n" +
                "Host: localhost1\r\n\r\n";

        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.True(request.DecoderResult().IsFailure());
        Assert.IsAssignableFrom<TooLongHttpLineException>(request.DecoderResult().Cause());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestTooLargeInitialLineWithWSOnly()
    {
        TestTooLargeInitialLineWithControlCharsOnly("                    ");
    }

    [Fact]
    public void TestTooLargeInitialLineWithCRLFOnly()
    {
        TestTooLargeInitialLineWithControlCharsOnly("\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n");
    }

    private static void TestTooLargeInitialLineWithControlCharsOnly(string controlChars)
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder(15, 1024, 1024));
        string requestStr = controlChars + "GET / HTTP/1.1\r\n" +
                "Host: localhost1\r\n\r\n";

        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.True(request.DecoderResult().IsFailure());
        Assert.IsAssignableFrom<TooLongHttpLineException>(request.DecoderResult().Cause());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestInitialLineWithLeadingControlChars()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        string crlf = "\r\n";
        string request =  crlf + "GET /some/path HTTP/1.1" + crlf +
                "Host: localhost" + crlf + crlf;
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(request, Encoding.ASCII)));
        IHttpRequest req = channel.ReadInbound<IHttpRequest>();
        Assert.Equal(HttpMethod.GET, req.Method());
        Assert.Equal("/some/path", req.Uri());
        Assert.Equal(HttpVersion.HTTP_1_1, req.ProtocolVersion());
        Assert.True(channel.FinishAndReleaseAll());
    }

    [Fact]
    public void TestNonCrlfControlBytesPrecedingRequestLineAreRejected()
    {
        // RFC 9112 §2.2: servers SHOULD ignore "at least one empty line (CRLF)" before the
        // request-line.  Non-CRLF control bytes are not part of this robustness allowance
        // and must not be silently swallowed.
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());

        ByteBuf buf = Unpooled.Buffer();
        buf.WriteByte(0x00);   // NUL  — not an empty CRLF line
        buf.WriteByte(0x01);   // SOH  — not an empty CRLF line
        buf.WriteCharSequence(
                "GET / HTTP/1.1\r\nHost: example.com\r\n\r\n",
                Encoding.ASCII);

        channel.WriteInbound(buf);
        IHttpRequest req = channel.ReadInbound<IHttpRequest>();

        DecoderResult decoderResult = req.DecoderResult();
        Assert.True(decoderResult.IsFailure(),
                "Non-CRLF control bytes before the request-line must not be silently skipped");
        Assert.IsAssignableFrom<InvalidLineSeparatorException>(decoderResult.Cause());

        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestTooLargeHeaders()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder(1024, 10, 1024));
        string requestStr = "GET /some/path HTTP/1.1\r\n" +
                "Host: localhost1\r\n\r\n";

        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.True(request.DecoderResult().IsFailure());
        Assert.IsAssignableFrom<TooLongHttpHeaderException>(request.DecoderResult().Cause());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestTotalHeaderLimit()
    {
        string requestStr = "GET /some/path HTTP/1.1\r\n" +
                "Host: a.b\r\n" + // 9 content bytes
                "a1: b\r\n" +     // + 5 = 14 bytes,
                "a2: b\r\n\r\n";  // + 5 = 19 bytes

        // Decoding with a max header size of 18 bytes must fail:
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder(1024, 18, 1024));
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.True(request.DecoderResult().IsFailure());
        Assert.IsAssignableFrom<TooLongHttpHeaderException>(request.DecoderResult().Cause());
        Assert.False(channel.Finish());

        // Decoding with a max header size of 19 must pass:
        channel = new EmbeddedChannel(new HttpRequestDecoder(1024, 19, 1024));
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        request = channel.ReadInbound<IHttpRequest>();
        Assert.True(request.DecoderResult().IsSuccess());
        Assert.Equal("a.b", request.Headers().Get("Host"));
        Assert.Equal("b", request.Headers().Get("a1"));
        Assert.Equal("b", request.Headers().Get("a2"));
        Assert.Equal(ILastHttpContent.EMPTY_LAST_CONTENT, channel.ReadInbound());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestHeaderNameStartsWithControlChar1c()
    {
        TestHeaderNameStartsWithControlChar(0x1c);
    }

    [Fact]
    public void TestHeaderNameStartsWithControlChar1d()
    {
        TestHeaderNameStartsWithControlChar(0x1d);
    }

    [Fact]
    public void TestHeaderNameStartsWithControlChar1e()
    {
        TestHeaderNameStartsWithControlChar(0x1e);
    }

    [Fact]
    public void TestHeaderNameStartsWithControlChar1f()
    {
        TestHeaderNameStartsWithControlChar(0x1f);
    }

    [Fact]
    public void TestHeaderNameStartsWithControlChar0c()
    {
        TestHeaderNameStartsWithControlChar(0x0c);
    }

    private void TestHeaderNameStartsWithControlChar(int controlChar)
    {
        ByteBuf requestBuffer = Unpooled.Buffer();
        requestBuffer.WriteCharSequence("GET /some/path HTTP/1.1\r\n" +
                "Host: netty.io\r\n", Encoding.ASCII);
        requestBuffer.WriteByte(controlChar);
        requestBuffer.WriteCharSequence("Transfer-Encoding: chunked\r\n\r\n", Encoding.ASCII);
        TestInvalidHeaders0(requestBuffer);
    }

    [Fact]
    public void TestHeaderNameEndsWithControlChar1c()
    {
        TestHeaderNameEndsWithControlChar(0x1c);
    }

    [Fact]
    public void TestHeaderNameEndsWithControlChar1d()
    {
        TestHeaderNameEndsWithControlChar(0x1d);
    }

    [Fact]
    public void TestHeaderNameEndsWithControlChar1e()
    {
        TestHeaderNameEndsWithControlChar(0x1e);
    }

    [Fact]
    public void TestHeaderNameEndsWithControlChar1f()
    {
        TestHeaderNameEndsWithControlChar(0x1f);
    }

    [Fact]
    public void TestHeaderNameEndsWithControlChar0c()
    {
        TestHeaderNameEndsWithControlChar(0x0c);
    }

    private void TestHeaderNameEndsWithControlChar(int controlChar)
    {
        ByteBuf requestBuffer = Unpooled.Buffer();
        requestBuffer.WriteCharSequence("GET /some/path HTTP/1.1\r\n" +
                "Host: netty.io\r\n", Encoding.ASCII);
        requestBuffer.WriteCharSequence("Transfer-Encoding", Encoding.ASCII);
        requestBuffer.WriteByte(controlChar);
        requestBuffer.WriteCharSequence(": chunked\r\n\r\n", Encoding.ASCII);
        TestInvalidHeaders0(requestBuffer);
    }

    [Fact]
    public void TestWhitespace()
    {
        string requestStr = "GET /some/path HTTP/1.1\r\n" +
                "Transfer-Encoding : chunked\r\n" +
                "Host: netty.io\r\n\r\n";
        TestInvalidHeaders0(requestStr);
    }

    [Fact]
    public void TestWhitespaceInTransferEncoding01()
    {
        string requestStr = "GET /some/path HTTP/1.1\r\n" +
                "Transfer-Encoding : chunked\r\n" +
                "Content-Length: 1\r\n" +
                "Host: netty.io\r\n\r\n" +
                "a";
        TestInvalidHeaders0(requestStr);
    }

    [Fact]
    public void TestWhitespaceInTransferEncoding02()
    {
        string requestStr = "POST / HTTP/1.1" +
                "Transfer-Encoding : chunked\r\n" +
                "Host: target.com" +
                "Content-Length: 65\r\n\r\n" +
                "0\r\n\r\n" +
                "GET /maliciousRequest HTTP/1.1\r\n" +
                "Host: evilServer.com\r\n" +
                "Foo: x";
        TestInvalidHeaders0(requestStr);
    }

    [Fact]
    public void TestHeaderWithNoValueAndMissingColon()
    {
        string requestStr = "GET /some/path HTTP/1.1\r\n" +
                "Content-Length: 0\r\n" +
                "Host:\r\n" +
                "netty.io\r\n\r\n";
        TestInvalidHeaders0(requestStr);
    }

    [Fact]
    public void TestMultipleContentLengthHeaders()
    {
        string requestStr = "GET /some/path HTTP/1.1\r\n" +
                "Content-Length: 1\r\n" +
                "Content-Length: 0\r\n\r\n" +
                "b";
        TestInvalidHeaders0(requestStr);
    }

    [Fact]
    public void TestMultipleContentLengthHeaders2()
    {
        string requestStr = "GET /some/path HTTP/1.1\r\n" +
                "Content-Length: 1\r\n" +
                "Connection: close\r\n" +
                "Content-Length: 0\r\n\r\n" +
                "b";
        TestInvalidHeaders0(requestStr);
    }

    [Fact]
    public void TestContentLengthHeaderWithCommaValue()
    {
        string requestStr = "GET /some/path HTTP/1.1\r\n" +
                "Content-Length: 1,1\r\n\r\n" +
                "b";
        TestInvalidHeaders0(requestStr);
    }

    [Fact]
    public void TestMultipleContentLengthHeadersWithFolding()
    {
        string requestStr = "POST / HTTP/1.1\r\n" +
                "Host: example.com\r\n" +
                "Connection: close\r\n" +
                "Content-Length: 5\r\n" +
                "Content-Length:\r\n" +
                "\t6\r\n\r\n" +
                "123456";
        TestInvalidHeaders0(requestStr);
    }

    [Theory]
    [InlineData("Transfer-Encoding: chunked, identity\r\nContent-Length: 1\r\n")]
    [InlineData("Transfer-Encoding: chunked\r\nTransfer-Encoding: gzip\r\n")]
    [InlineData("Transfer-Encoding: chunked, gzip,\r\n")]
    [InlineData("Transfer-Encoding: chunked, xchunked\r\n")]
    [InlineData("Transfer-Encoding: gzip\r\n")]
    [InlineData("Transfer-Encoding: chunked gzip\r\nContent-Length: 1\r\n")]
    [InlineData("Transfer-Encoding: chunked, chunked\r\n")]
    [InlineData("Transfer-Encoding: chunked, gzip, chunked\r\n")]
    [InlineData("Transfer-Encoding: chunked\r\nTransfer-Encoding: chunked\r\n")]
    [InlineData("Transfer-Encoding: Chunked, , CHUNKED,\r\n")]
    [InlineData("Transfer-Encoding: chunked;foo=bar\r\n")]
    public void TestInvalidTransferEncodingFraming(string headers)
    {
        TestInvalidHeaders0("GET /some/path HTTP/1.1\r\n" + headers + "Host: netty.io\r\n\r\n");
    }

    [Theory]
    [InlineData("Transfer-Encoding: gzip\r\nTransfer-Encoding: chunked\r\n")]
    [InlineData("Transfer-Encoding: gzip\r\nTransfer-Encoding: chunked,\r\n")]
    [InlineData("Transfer-Encoding: chunked\r\nTransfer-Encoding: , \t,\r\n")]
    public void TestChunkedLastInMultiLineTransferEncoding(string transferEncodingHeaders)
    {
        string requestStr = "GET /some/path HTTP/1.1\r\n" +
                transferEncodingHeaders +
                "Host: netty.io\r\n\r\n" +
                "0\r\n\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));

        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.True(request.DecoderResult().IsSuccess());
        Assert.Equal(2, request.Headers().GetAll(HttpHeaderNames.TRANSFER_ENCODING).Count);
        ILastHttpContent content = channel.ReadInbound<ILastHttpContent>();
        Assert.True(content.DecoderResult().IsSuccess());
        content.Release();
        Assert.False(channel.Finish());
    }

    // Regression: the chunked-must-be-last check was nested inside a protocolVersion() ==
    // HTTP_1_1 condition, so it was silently skipped for HTTP/1.0 when
    // useRfc9112TransferEncoding is disabled. See HttpObjectDecoder#readHeaders.
    [Fact]
    public void TestChunkedNotLastInTransferEncodingHttp10WithRfc9112Disabled()
    {
        string requestStr = "GET /some/path HTTP/1.0\r\n" +
                "Transfer-Encoding: chunked, identity\r\n" +
                "Content-Length: 1\r\n" +
                "Host: netty.io\r\n\r\n" +
                "a";
        HttpDecoderConfig config = new HttpDecoderConfig().SetUseRfc9112TransferEncoding(false);
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder(config));
        Assert.True(channel.WriteInbound(
                Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.IsAssignableFrom<ArgumentException>(request.DecoderResult().Cause());
        Assert.True(request.DecoderResult().IsFailure(),
                "expected 'chunked must be last' to be enforced regardless of HTTP version "
                        + "when Transfer-Encoding is legitimately present (useRfc9112TransferEncoding=false)");
        channel.FinishAndReleaseAll();
    }

    // Companion to the test above: confirms the fix isn't tied to identity being a no-op
    // by using a real trailing encoding (gzip) instead.
    [Fact]
    public void TestChunkedNotLastInTransferEncodingHttp10WithRfc9112DisabledNonNoOpEncoding()
    {
        string requestStr = "GET /some/path HTTP/1.0\r\n" +
                "Transfer-Encoding: chunked, gzip\r\n" +
                "Content-Length: 1\r\n" +
                "Host: netty.io\r\n\r\n" +
                "a";
        HttpDecoderConfig config = new HttpDecoderConfig().SetUseRfc9112TransferEncoding(false);
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder(config));
        Assert.True(channel.WriteInbound(
                Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.IsAssignableFrom<ArgumentException>(request.DecoderResult().Cause());
        Assert.True(request.DecoderResult().IsFailure(),
                "expected 'chunked must be last' to be enforced regardless of the specific "
                        + "trailing encoding value, matching the identity-trailing case above");
        channel.FinishAndReleaseAll();
    }

    [Fact]
    public void TestContentLengthAndTransferEncodingHeadersWithVerticalTab()
    {
        TestContentLengthAndTransferEncodingHeadersWithInvalidSeparator((char) 0x0b, false);
        TestContentLengthAndTransferEncodingHeadersWithInvalidSeparator((char) 0x0b, true);
    }

    [Fact]
    public void TestContentLengthAndTransferEncodingHeadersWithCR()
    {
        TestContentLengthAndTransferEncodingHeadersWithInvalidSeparator((char) 0x0d, false);
        TestContentLengthAndTransferEncodingHeadersWithInvalidSeparator((char) 0x0d, true);
    }

    private static void TestContentLengthAndTransferEncodingHeadersWithInvalidSeparator(
            char separator, bool extraLine)
    {
        string requestStr = "POST / HTTP/1.1\r\n" +
                "Host: example.com\r\n" +
                "Connection: close\r\n" +
                "Content-Length: 9\r\n" +
                "Transfer-Encoding:" + separator + "chunked\r\n\r\n" +
                (extraLine ? "0\r\n\r\n" : "") +
                "something\r\n\r\n";
        TestInvalidHeaders0(requestStr);
    }

    [Fact]
    public void TestContentLengthHeaderAndChunkedHttp11()
    {
        string requestStr = "POST / HTTP/1.1\r\n" +
                "Host: example.com\r\n" +
                "Connection: close\r\n" +
                "Content-Length: 5\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "0\r\n\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.True(request.DecoderResult().IsFailure());
        Assert.IsAssignableFrom<ContentLengthNotAllowedException>(request.DecoderResult().Cause());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestContentLengthHeaderAndChunkedHttp11RFC7230()
    {
        string requestStr = "POST / HTTP/1.1\r\n" +
                "Host: example.com\r\n" +
                "Content-Length: 5\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "0\r\n\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder(
                new HttpDecoderConfig().SetUseRfc9112TransferEncoding(false)));
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.False(request.DecoderResult().IsFailure());
        Assert.True(request.Headers().Names().Contains("Transfer-Encoding"));
        Assert.True(request.Headers().Contains("Transfer-Encoding", "chunked", false));
        Assert.False(request.Headers().Contains("Content-Length"));
        Assert.Equal("close", request.Headers().Get("Connection"));
        ILastHttpContent c = channel.ReadInbound<ILastHttpContent>();
        c.Release();
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestContentLengthHeaderAndChunkedHttp10()
    {
        string requestStr = "POST / HTTP/1.0\r\n" +
                "Host: example.com\r\n" +
                "Connection: close\r\n" +
                "Content-Length: 5\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "0\r\n\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.True(request.DecoderResult().IsFailure());
        Assert.IsAssignableFrom<TransferEncodingNotAllowedException>(request.DecoderResult().Cause());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void MustRejectImproperlyTerminatedChunkExtensions()
    {
        // See full explanation: https://w4ke.info/2025/06/18/funky-chunks.html
        string requestStr = "GET /one HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "2;\n" + // Chunk size followed by illegal single newline (not preceded by carraige return)
                "xx\r\n" +
                "45\r\n" +
                "0\r\n\r\n" +
                "GET /two HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "0\r\n\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.False(request.DecoderResult().IsFailure()); // We parse the headers just fine.
        Assert.True(request.Headers().Names().Contains("Transfer-Encoding"));
        Assert.True(request.Headers().Contains("Transfer-Encoding", "chunked", false));
        IHttpContent content = channel.ReadInbound<IHttpContent>();
        DecoderResult decoderResult = content.DecoderResult();
        Assert.True(decoderResult.IsFailure()); // But parsing the chunk must fail.
        Assert.IsAssignableFrom<InvalidChunkExtensionException>(decoderResult.Cause());
        content.Release();
        Assert.False(channel.Finish());
    }

    [Fact]
    public void MustRejectImproperlyTerminatedChunkBodies()
    {
        // See full explanation: https://w4ke.info/2025/06/18/funky-chunks.html
        string requestStr = "GET /one HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "5\r\n" +
                "AAAAAXX" + // Chunk body contains extra (XX) bytes, and no CRLF terminator.
                "45\r\n" +
                "0\r\n" +
                "GET /two HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "0\r\n\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.False(request.DecoderResult().IsFailure()); // We parse the headers just fine.
        Assert.True(request.Headers().Names().Contains("Transfer-Encoding"));
        Assert.True(request.Headers().Contains("Transfer-Encoding", "chunked", false));
        IHttpContent content = channel.ReadInbound<IHttpContent>();
        Assert.False(content.DecoderResult().IsFailure()); // We parse the content promised by the chunk length.
        content.Release();

        content = channel.ReadInbound<IHttpContent>();
        DecoderResult decoderResult = content.DecoderResult();
        Assert.True(decoderResult.IsFailure()); // But then parsing the chunk delimiter must fail.
        Assert.IsAssignableFrom<InvalidChunkTerminationException>(decoderResult.Cause());
        content.Release();
        Assert.False(channel.Finish());
    }

    [Fact]
    public void MustParsedChunkExtensionsWithQuotedStrings()
    {
        // See full explanation: https://w4ke.info/2025/10/29/funky-chunks-2.html
        string requestStr = "GET /one HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "1;a=\" ;\t\"\r\n" + // chunk extension quote end
                "Y\r\n" +
                "0\r\n" +
                "\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.False(request.DecoderResult().IsFailure()); // We parse the headers just fine.
        Assert.True(request.Headers().Names().Contains("Transfer-Encoding"));
        Assert.True(request.Headers().Contains("Transfer-Encoding", "chunked", false));
        IHttpContent content = channel.ReadInbound<IHttpContent>();
        DecoderResult decoderResult = content.DecoderResult();
        Assert.False(decoderResult.IsFailure()); // And we parse the chunk.
        content.Release();
        ILastHttpContent last = channel.ReadInbound<ILastHttpContent>();
        Assert.Equal(0, last.Content.ReadableBytes);
        last.Release();
        Assert.False(channel.Finish()); // And there are no other chunks parsed.
    }

    [Fact]
    public void MustRejectChunkExtensionsWithLineBreaksInQuotedStrings()
    {
        // See full explanation: https://w4ke.info/2025/10/29/funky-chunks-2.html
        string requestStr = "GET /one HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "1;a=\"\r\n" + // chunk extension quote start
                "X\r\n" +
                "0\r\n\r\n" +
                "GET /two HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "\"\r\n" + // chunk extension quote end
                "Y\r\n" +
                "0\r\n" +
                "\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.False(request.DecoderResult().IsFailure()); // We parse the headers just fine.
        Assert.True(request.Headers().Names().Contains("Transfer-Encoding"));
        Assert.True(request.Headers().Contains("Transfer-Encoding", "chunked", false));
        IHttpContent content = channel.ReadInbound<IHttpContent>();
        DecoderResult decoderResult = content.DecoderResult();
        Assert.True(decoderResult.IsFailure()); // Chunk extension is not allowed to contain line breaks.
        Assert.IsAssignableFrom<InvalidChunkExtensionException>(decoderResult.Cause());
        content.Release();
        Assert.False(channel.Finish()); // And there are no other chunks parsed.
    }

    [Fact]
    public void MustParseChunkExtensionsWithQuotedStringsAndEscapes()
    {
        // See full explanation: https://w4ke.info/2025/10/29/funky-chunks-2.html
        string requestStr = "GET /one HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "1;a=\" \\\";\t\"\r\n" +
                "Y\r\n" +
                "0\r\n" +
                "\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.False(request.DecoderResult().IsFailure()); // We parse the headers just fine.
        Assert.True(request.Headers().Names().Contains("Transfer-Encoding"));
        Assert.True(request.Headers().Contains("Transfer-Encoding", "chunked", false));
        IHttpContent content = channel.ReadInbound<IHttpContent>();
        DecoderResult decoderResult = content.DecoderResult();
        Assert.False(decoderResult.IsFailure()); // And we parse the chunk.
        content.Release();
        ILastHttpContent last = channel.ReadInbound<ILastHttpContent>();
        Assert.Equal(0, last.Content.ReadableBytes);
        last.Release();
        Assert.False(channel.Finish()); // And there are no other chunks parsed.
    }

    [Fact]
    public void MustParseMultipleChunkExtensionsWithTokenValues()
    {
        // Regression: the old Match-based state machine had ';' (0x3B) missing from the
        // exclusion set in ChunkExtValToken, so ';' was treated as a token character
        // instead of starting a new extension.  This caused valid multi-extension lines
        // like ";name1=val1;name2=val2" to be rejected with InvalidChunkExtensionException.
        string requestStr = "GET / HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "1;name1=val1;name2=val2\r\n" +
                "Y\r\n" +
                "0\r\n" +
                "\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.False(request.DecoderResult().IsFailure());
        IHttpContent content = channel.ReadInbound<IHttpContent>();
        if (content.DecoderResult().IsFailure())
        {
            Console.Error.WriteLine(content.DecoderResult().Cause());
        }
        Assert.False(content.DecoderResult().IsFailure()); // Must accept valid multi-extension token values.
        content.Release();
        ILastHttpContent last = channel.ReadInbound<ILastHttpContent>();
        Assert.Equal(0, last.Content.ReadableBytes);
        last.Release();
        Assert.False(channel.Finish());
    }

    [Fact]
    public void MustRejectChunkExtensionsWithEscapedLineBreakInQuotedStrings()
    {
        // See full explanation: https://w4ke.info/2025/10/29/funky-chunks-2.html
        string requestStr = "GET /one HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "1;a=\" \\\n;\t\"\r\n" +
                "Y\r\n" +
                "0\r\n" +
                "\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.False(request.DecoderResult().IsFailure()); // We parse the headers just fine.
        Assert.True(request.Headers().Names().Contains("Transfer-Encoding"));
        Assert.True(request.Headers().Contains("Transfer-Encoding", "chunked", false));
        IHttpContent content = channel.ReadInbound<IHttpContent>();
        DecoderResult decoderResult = content.DecoderResult();
        Assert.True(decoderResult.IsFailure()); // Chunk extension is not allowed to contain line breaks.
        Assert.IsAssignableFrom<InvalidChunkExtensionException>(decoderResult.Cause());
        content.Release();
        Assert.False(channel.Finish()); // And there are no other chunks parsed.
    }

    [Fact]
    public void MustRejectChunkExtensionsWithEscapedCarriageReturnInQuotedStrings()
    {
        // See full explanation: https://w4ke.info/2025/10/29/funky-chunks-2.html
        string requestStr = "GET /one HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "1;a=\" \\\r;\t\"\r\n" +
                "Y\r\n" +
                "0\r\n" +
                "\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.False(request.DecoderResult().IsFailure()); // We parse the headers just fine.
        Assert.True(request.Headers().Names().Contains("Transfer-Encoding"));
        Assert.True(request.Headers().Contains("Transfer-Encoding", "chunked", false));
        IHttpContent content = channel.ReadInbound<IHttpContent>();
        DecoderResult decoderResult = content.DecoderResult();
        Assert.True(decoderResult.IsFailure()); // Chunk extension is not allowed to contain carraige return.
        Assert.IsAssignableFrom<InvalidChunkExtensionException>(decoderResult.Cause());
        content.Release();
        Assert.False(channel.Finish()); // And there are no other chunks parsed.
    }

    [Fact]
    public void LineLengthRestrictionMustNotApplyToChunkContents()
    {
        char[] chars = new char[10000];
        Array.Fill(chars, 'a');
        string requestContent = new string(chars);
        string requestStr = "POST /one HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                chars.Length.ToString("x", CultureInfo.InvariantCulture) + "\r\n" +
                requestContent + "\r\n" +
                "0\r\n\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.False(request.DecoderResult().IsFailure()); // We parse the headers just fine.
        Assert.True(request.Headers().Names().Contains("Transfer-Encoding"));
        Assert.True(request.Headers().Contains("Transfer-Encoding", "chunked", false));
        int contentLength = 0;
        IHttpContent content;
        do
        {
            content = channel.ReadInbound<IHttpContent>();
            DecoderResult decoderResult = content.DecoderResult();
            if (decoderResult.Cause() != null)
            {
                throw new Exception(decoderResult.Cause().Message, decoderResult.Cause());
            }
            Assert.False(decoderResult.IsFailure()); // And we parse the chunk.
            contentLength += content.Content.ReadableBytes;
            content.Release();
        } while (!(content is ILastHttpContent));
        Assert.Equal(chars.Length, contentLength);
        Assert.False(channel.Finish()); // And there are no other chunks parsed.
    }

    [Fact]
    public void MustRejectChunkSizeWithNonHexadecimalCharacters()
    {
        string requestStr = "POST /one HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n" +
                "test\r\n\r\n" + // chunk extension quote start
                "\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.False(request.DecoderResult().IsFailure()); // We parse the headers
        IHttpContent content = channel.ReadInbound<IHttpContent>();
        Assert.True(content.DecoderResult().IsFailure());
        Assert.IsAssignableFrom<FormatException>(content.DecoderResult().Cause());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void MustRejectChunkSizeThatWouldCauseOverflow()
    {
        string requestStr = "POST / HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "100000004\r\n" +
                "test\r\n" +
                "0\r\n" +
                "\r\n" +
                "GET /smuggled HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "Content-Length: 0\r\n" +
                "\r\n";

        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));

        // Request 1
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.True(request.DecoderResult().IsSuccess());
        IHttpContent content = channel.ReadInbound<IHttpContent>();
        Assert.False(content.DecoderResult().IsSuccess());
        Assert.Contains("Chunk size overflow", content.DecoderResult().Cause().Message);
        content.Release();
        Assert.False(channel.Finish());
    }

    [Theory]
    [InlineData("")]
    [InlineData("5 ")]
    [InlineData(" 5")]
    [InlineData("5 c")]
    [InlineData("5 x")]
    [InlineData("5 c;foo=bar")]
    [InlineData("  5 c")]
    [InlineData("  5 x")]
    [InlineData("  5 c;foo=bar")]
    public void MustRejectChunkSizeWithIllegalWhitespaceOrExtraTokens(string chunkSizeLine)
    {
        string requestStr = "POST / HTTP/1.1\r\n" +
            "Host: example\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "\r\n" +
            chunkSizeLine + "\r\n" +
            "GPOST\r\n" +
            "0\r\n" +
            "\r\n" +
            "GET /smuggled HTTP/1.1\r\n" +
            "Host: example\r\n" +
            "\r\n";

        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));

        // The request headers parse fine.
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.True(request.DecoderResult().IsSuccess());
        Assert.Equal(HttpVersion.HTTP_1_1, request.ProtocolVersion());
        Assert.Equal(HttpMethod.POST, request.Method());

        // But the malformed chunk-size line must be rejected, not truncated at the whitespace.
        IHttpContent content = channel.ReadInbound<IHttpContent>();
        Assert.True(content.DecoderResult().IsFailure());
        Assert.Contains("chunk size", content.DecoderResult().Cause().Message);
        content.Release();

        // Nothing after the failed chunk may be surfaced as a second, smuggled request.
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestOrderOfHeadersWithContentLength()
    {
        string requestStr = "GET /some/path HTTP/1.1\r\n" +
                "Host: example.com\r\n" +
                "Content-Length: 5\r\n" +
                "Connection: close\r\n\r\n" +
                "hello";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        List<string> headers = new List<string>();
        foreach (IEntry<string, string> header in request.Headers())
        {
            headers.Add(header.GetKey());
        }
        Assert.Equal(new List<string> { "Host", "Content-Length", "Connection" }, headers, "ordered headers");
    }

    [Fact]
    public void TestHttpMessageDecoderResult()
    {
        string requestStr = "PUT /some/path HTTP/1.1\r\n" +
                "Content-Length: 11\r\n" +
                "Connection: close\r\n\r\n" +
                "Lorem ipsum";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.True(request.DecoderResult().IsSuccess());
        Assert.IsAssignableFrom<HttpMessageDecoderResult>(request.DecoderResult());
        HttpMessageDecoderResult decoderResult = (HttpMessageDecoderResult) request.DecoderResult();
        Assert.Equal(23, decoderResult.InitialLineLength());
        Assert.Equal(35, decoderResult.HeaderSize());
        Assert.Equal(58, decoderResult.TotalSize());
        IHttpContent c = channel.ReadInbound<IHttpContent>();
        c.Release();
        Assert.False(channel.Finish());
    }

    /**
     * <a href="https://datatracker.ietf.org/doc/html/rfc9112#name-field-syntax">RFC 9112</a> define the header field
     * syntax thusly, where the field value is bracketed by optional whitespace:
     * <pre>
     *     field-line   = field-name ":" OWS field-value OWS
     * </pre>
     * Meanwhile, <a href="https://datatracker.ietf.org/doc/html/rfc9110#name-whitespace">RFC 9110</a> says that
     * "optional whitespace" (OWS) is defined as "zero or more linear whitespace octets".
     * And a "linear whitespace octet" is defined in the ABNF as either a space or a tab character.
     */
    [Fact]
    public void HeaderValuesMayBeBracketedByZeroOrMoreWhitespace()
    {
        string requestStr = "GET / HTTP/1.1\r\n" +
                "Host:example.com\r\n" + // zero whitespace
                "X-0-Header:  x0\r\n" + // two whitespace
                "X-1-Header:\tx1\r\n" + // tab whitespace
                "X-2-Header: \t x2\r\n" + // mixed whitespace
                "X-3-Header:x3\t \r\n" + // whitespace after the value
                "\r\n";
        HttpRequestDecoder decoder = new HttpRequestDecoder();
        EmbeddedChannel channel = new EmbeddedChannel(decoder);

        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.True(request.DecoderResult().IsSuccess());
        HttpHeaders headers = request.Headers();
        Assert.Equal("example.com", headers.Get("Host"));
        Assert.Equal("x0", headers.Get("X-0-Header"));
        Assert.Equal("x1", headers.Get("X-1-Header"));
        Assert.Equal("x2", headers.Get("X-2-Header"));
        Assert.Equal("x3", headers.Get("X-3-Header"));
        ILastHttpContent last = channel.ReadInbound<ILastHttpContent>();
        Assert.Equal(ILastHttpContent.EMPTY_LAST_CONTENT, last);
        last.Release();
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestChunkSizeOverflow()
    {
        string requestStr = "PUT /some/path HTTP/1.1\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "8ccccccc\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.True(request.DecoderResult().IsSuccess());
        IHttpContent c = channel.ReadInbound<IHttpContent>();
        c.Release();
        Assert.True(c.DecoderResult().IsFailure());
        Assert.IsAssignableFrom<FormatException>(c.DecoderResult().Cause());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestChunkSizeOverflow2()
    {
        string requestStr = "PUT /some/path HTTP/1.1\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "bbbbbbbe;\r\n\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.True(request.DecoderResult().IsSuccess());
        IHttpContent c = channel.ReadInbound<IHttpContent>();
        c.Release();
        Assert.True(c.DecoderResult().IsFailure());
        Assert.IsAssignableFrom<FormatException>(c.DecoderResult().Cause());
        Assert.False(channel.Finish());
    }

    [Theory]
    [InlineData("HTP/1.1")]
    [InlineData("HTTP")]
    [InlineData("HTTP/1x")]
    [InlineData("Something/1.1")]
    [InlineData("HTTP/1")]
    [InlineData("HTTP/1.11")]
    [InlineData("HTTP/11.1")]
    [InlineData("HTTP/A.1")]
    [InlineData("HTTP/1.B")]
    public void TestInvalidVersion(string version)
    {
        TestInvalidHeaders0("GET / " + version + "\r\nHost: whatever\r\n\r\n");
    }

    [Theory]
    // See https://www.unicode.org/charts/nameslist/n_0000.html
    [InlineData("\r")]
    [InlineData("\u000b")]
    [InlineData("\u000c")]
    public void TestHeaderValueWithInvalidSuffix(string suffix)
    {
        TestInvalidHeaders0("GET / HTTP/1.1\r\nHost: whatever\r\nTest-Key: test-value" + suffix + "\r\n\r\n");
    }

    [Fact]
    public void TestLeadingWhitespaceInFirstHeaderName()
    {
        TestInvalidHeaders0("POST / HTTP/1.1\r\n\tContent-Length: 1\r\n\r\nX");
    }

   [Fact]
    public void TestNulInInitialLine()
    {
        TestInvalidHeaders0("GET / HTTP/1.1\u0000\r\nHost: whatever\r\n\r\n");
    }

    [Fact]
    public void ReentrantClose()
    {
        string requestStr = "GET / HTTP/1.1\r\n" +
                "Host: example.com\r\n" +
                "Content-Length: 0\r\n" +
                "\r\n" +
                "GET / HTTP/1.1\r\n" +
                "Host: example.com\r\n" +
                "Content-Length: 0\r\n" +
                "\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder(), new ReentrantCloseHandler());

        Assert.False(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        Assert.False(channel.Finish());
    }

    private sealed class ReentrantCloseHandler : ChannelInboundHandlerAdapter
    {
        private int i;

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (i == 0)
            {
                Assert.IsAssignableFrom<IHttpRequest>(msg);
            }
            else if (i == 1)
            {
                Assert.IsAssignableFrom<ILastHttpContent>(msg);
            }
            else if (i == 2)
            {
                Assert.IsAssignableFrom<IHttpRequest>(msg);
            }
            else if (i == 3)
            {
                Assert.IsAssignableFrom<ILastHttpContent>(msg);
            }
            ReferenceCountUtil.Release(msg);

            if (++i == 1)
            {
                // first request
                ctx.CloseAsync();
            }
        }
    }

    private static void TestInvalidHeaders0(string requestStr)
    {
        TestInvalidHeaders0(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII));
    }

    private static void TestInvalidHeaders0(ByteBuf requestBuffer)
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        Assert.True(channel.WriteInbound(requestBuffer));
        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.IsAssignableFrom<ArgumentException>(request.DecoderResult().Cause());
        Assert.True(request.DecoderResult().IsFailure());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestNulInVersionTokenIsRejected()
    {
        // A NUL right before the version token must be rejected.
        TestInvalidHeaders0("GET / " + (char) 0 + "HTTP/1.1\r\nHost: whatever\r\n\r\n");
    }

    [Fact]
    public void TestNulInMethodTokenIsRejected()
    {
        // Control case: a NUL inside the method token is also rejected.
        TestInvalidHeaders0("GET" + (char) 0 + " / HTTP/1.1\r\nHost: whatever\r\n\r\n");
    }

    [Fact]
    public void TestNormalRequestStillDecodes()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer("GET / HTTP/1.1\r\nHost: example.com\r\n\r\n",
                Encoding.ASCII)));
        IHttpRequest req = channel.ReadInbound<IHttpRequest>();
        Assert.NotNull(req);
        Assert.True(req.DecoderResult().IsSuccess());
        Assert.Equal(HttpVersion.HTTP_1_1, req.ProtocolVersion());
        ILastHttpContent last = channel.ReadInbound<ILastHttpContent>();
        Assert.NotNull(last);
        last.Release();
        Assert.False(channel.Finish());
    }
}
