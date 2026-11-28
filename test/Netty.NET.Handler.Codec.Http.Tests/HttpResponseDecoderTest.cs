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
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.Tests.HttpHeadersTestUtils;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests;

#pragma warning disable xUnit1051 // The source's channel.close() observes no test cancellation.

// CLR: @ParameterizedTest @ValueSource(strings) is a [Theory] with one [InlineData] per value; the display-name pattern
// of @ParameterizedTest(name = ...) has no xUnit counterpart. Package-private JUnit methods are public xUnit methods.
// CharsetUtil.US_ASCII is Encoding.ASCII and String.getBytes() (the default charset) is Encoding.Default
// (docs/codec-smtp-porting.md; the bytes are ASCII). IllegalArgumentException is ArgumentException and
// NumberFormatException FormatException (docs/codec-base-porting.md); assertInstanceOf and AssertJ's isInstanceOf are
// Assert.IsAssignableFrom, isNotInstanceOf is Assert.False(x is T), hasMessageContaining is Assert.Contains on Message,
// assertArrayEquals is Assert.Equal on the arrays, and the asserts with messages use XunitAssertMessages.AssertM.
// ByteBuf.readBytes(byte[]) and readBytes(byte[], int, int) are ReadBytes(Span<byte>) over the array or its slice.
// testResponseDisallowPartialChunks uses the @Deprecated seven-argument HttpResponseDecoder constructor, whose
// @deprecated text names HttpResponseDecoder(HttpDecoderConfig); that constructor is not ported (owner decision
// 2026-11-16), so the test passes the same values through HttpDecoderConfig (setValidateHeaders(DEFAULT_VALIDATE_HEADERS)
// selects the default factories, as HttpObjectDecoder's deprecated constructor did). ThreadLocalRandom.current() is
// Random.Shared (the chunk bytes are arbitrary). Java passes Strings as CharSequence header names
// (HttpHeadersTestUtils.of); C# string is not an ICharSequence, so they are wrapped with Of(...). In
// testResponseChunked/testResponseChunkedExceedMaxChunkSize the Java byte data[i] = (byte) i only takes 0..63. The
// private helpers that overload the test methods testLastResponseWithTrailingHeaderFragmented and
// testResponseWithContentLengthFragmented take the suffix 0, because xUnit does not run a test method that has
// overloads ("Test methods cannot have overloads", https://xunit.net/xunit.analyzers/rules/xUnit1024).
public class HttpResponseDecoderTest
{

    /**
     * The size of headers should be calculated correctly even if a single header is split into multiple fragments.
     * @see <a href="https://github.com/netty/netty/issues/3445">#3445</a>
     */
    [Fact]
    public void TestMaxHeaderSize1()
    {
        int maxHeaderSize = 8192;

        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder(4096, maxHeaderSize, 8192));
        char[] bytes = new char[maxHeaderSize / 2 - 4];
        Array.Fill(bytes, 'a');

        ch.WriteInbound(Unpooled.CopiedBuffer("HTTP/1.1 200 OK\r\n", Encoding.ASCII));

        // Write two 4096-byte headers (= 8192 bytes)
        ch.WriteInbound(Unpooled.CopiedBuffer("A:", Encoding.ASCII));
        ch.WriteInbound(Unpooled.CopiedBuffer(bytes, Encoding.ASCII));
        ch.WriteInbound(Unpooled.CopiedBuffer("\r\n", Encoding.ASCII));
        Assert.Null(ch.ReadInbound());
        ch.WriteInbound(Unpooled.CopiedBuffer("B:", Encoding.ASCII));
        ch.WriteInbound(Unpooled.CopiedBuffer(bytes, Encoding.ASCII));
        ch.WriteInbound(Unpooled.CopiedBuffer("\r\n", Encoding.ASCII));
        ch.WriteInbound(Unpooled.CopiedBuffer("\r\n", Encoding.ASCII));

        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.Null(res.DecoderResult().Cause());
        Assert.True(res.DecoderResult().IsSuccess());

        Assert.Null(ch.ReadInbound());
        Assert.True(ch.Finish());
        Assert.IsAssignableFrom<ILastHttpContent>(ch.ReadInbound());
    }

    /**
     * Complementary test case of {@link #testMaxHeaderSize1()} When it actually exceeds the maximum, it should fail.
     */
    [Fact]
    public void TestMaxHeaderSize2()
    {
        int maxHeaderSize = 8192;

        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder(4096, maxHeaderSize, 8192));
        char[] bytes = new char[maxHeaderSize / 2 - 2];
        Array.Fill(bytes, 'a');

        ch.WriteInbound(Unpooled.CopiedBuffer("HTTP/1.1 200 OK\r\n", Encoding.ASCII));

        // Write a 4096-byte header and a 4097-byte header to test an off-by-one case (= 8193 bytes)
        ch.WriteInbound(Unpooled.CopiedBuffer("A:", Encoding.ASCII));
        ch.WriteInbound(Unpooled.CopiedBuffer(bytes, Encoding.ASCII));
        ch.WriteInbound(Unpooled.CopiedBuffer("\r\n", Encoding.ASCII));
        Assert.Null(ch.ReadInbound());
        ch.WriteInbound(Unpooled.CopiedBuffer("B: ", Encoding.ASCII)); // Note an extra space.
        ch.WriteInbound(Unpooled.CopiedBuffer(bytes, Encoding.ASCII));
        ch.WriteInbound(Unpooled.CopiedBuffer("\r\n", Encoding.ASCII));
        ch.WriteInbound(Unpooled.CopiedBuffer("\r\n", Encoding.ASCII));

        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.IsAssignableFrom<TooLongHttpHeaderException>(res.DecoderResult().Cause());

        Assert.False(ch.Finish());
        Assert.Null(ch.ReadInbound());
    }

    [Fact]
    public void TestTotalHeaderLimit()
    {
        string requestStr = "HTTP/1.1 200 OK\r\n" +
                "Server: X\r\n" + // 9 content bytes
                "a1: b\r\n" +     // + 5 = 14 bytes,
                "a2: b\r\n\r\n";  // + 5 = 19 bytes

        // Decoding with a max header size of 18 bytes must fail:
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder(1024, 18, 1024));
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.True(response.DecoderResult().IsFailure());
        Assert.IsAssignableFrom<TooLongHttpHeaderException>(response.DecoderResult().Cause());
        Assert.False(channel.Finish());

        // Decoding with a max header size of 19 must pass:
        channel = new EmbeddedChannel(new HttpResponseDecoder(1024, 19, 1024));
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        response = channel.ReadInbound<IHttpResponse>();
        Assert.True(response.DecoderResult().IsSuccess());
        Assert.Equal("X", response.Headers().Get("Server"));
        Assert.Equal("b", response.Headers().Get("a1"));
        Assert.Equal("b", response.Headers().Get("a2"));
        channel.CloseAsync();
        Assert.Equal(ILastHttpContent.EMPTY_LAST_CONTENT, channel.ReadInbound());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestResponseChunked()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder());
        ch.WriteInbound(Unpooled.CopiedBuffer("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n",
                Encoding.ASCII));

        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.OK, res.Status());

        byte[] data = new byte[64];

        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte) i;
        }

        for (int i = 0; i < 10; i++)
        {
            Assert.False(ch.WriteInbound(Unpooled.CopiedBuffer(data.Length.ToString("x", CultureInfo.InvariantCulture) +
                    "\r\n", Encoding.ASCII)));
            Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(data)));
            IHttpContent content = ch.ReadInbound<IHttpContent>();
            Assert.Equal(data.Length, content.Content.ReadableBytes);

            byte[] decodedData = new byte[data.Length];
            content.Content.ReadBytes(decodedData);
            Assert.Equal(data, decodedData);
            content.Release();

            Assert.False(ch.WriteInbound(Unpooled.CopiedBuffer("\r\n", Encoding.ASCII)));
        }

        // Write the last chunk.
        ch.WriteInbound(Unpooled.CopiedBuffer("0\r\n\r\n", Encoding.ASCII));

        // Ensure the last chunk was decoded.
        ILastHttpContent lastContent = ch.ReadInbound<ILastHttpContent>();
        Assert.False(lastContent.Content.IsReadable());
        lastContent.Release();

        ch.Finish();
        Assert.Null(ch.ReadInbound());
    }

    [Fact]
    public void TestResponseDisallowPartialChunks()
    {
        HttpResponseDecoder decoder = new HttpResponseDecoder(new HttpDecoderConfig()
            .SetMaxInitialLineLength(HttpObjectDecoder.DEFAULT_MAX_INITIAL_LINE_LENGTH)
            .SetMaxHeaderSize(HttpObjectDecoder.DEFAULT_MAX_HEADER_SIZE)
            .SetMaxChunkSize(HttpObjectDecoder.DEFAULT_MAX_CHUNK_SIZE)
            .SetChunkedSupported(HttpObjectDecoder.DEFAULT_CHUNKED_SUPPORTED)
            .SetValidateHeaders(HttpObjectDecoder.DEFAULT_VALIDATE_HEADERS)
            .SetInitialBufferSize(HttpObjectDecoder.DEFAULT_INITIAL_BUFFER_SIZE)
            .SetAllowDuplicateContentLengths(HttpObjectDecoder.DEFAULT_ALLOW_DUPLICATE_CONTENT_LENGTHS)
            .SetAllowPartialChunks(false));
        EmbeddedChannel ch = new EmbeddedChannel(decoder);

        string headers = "HTTP/1.1 200 OK\r\n"
            + "Transfer-Encoding: chunked\r\n"
            + "\r\n";
       Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(headers, Encoding.ASCII)));

        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.OK, res.Status());

        byte[] chunkBytes = new byte[10];
        Random.Shared.NextBytes(chunkBytes);
        ByteBuf chunk = ch.Alloc().Buffer().WriteBytes(chunkBytes);
        int chunkSize = chunk.ReadableBytes;
        ByteBuf partialChunk1 = chunk.RetainedSlice(0, 5);
        ByteBuf partialChunk2 = chunk.RetainedSlice(5, 5);

        Assert.False(ch.WriteInbound(Unpooled.CopiedBuffer(chunkSize.ToString("x", CultureInfo.InvariantCulture)
                                                          + "\r\n", Encoding.ASCII)));
        Assert.False(ch.WriteInbound(partialChunk1));
        Assert.True(ch.WriteInbound(partialChunk2));

        IHttpContent content = ch.ReadInbound<IHttpContent>();
        Assert.Equal(chunk, content.Content);
        content.Release();
        chunk.Release();

        Assert.False(ch.WriteInbound(Unpooled.CopiedBuffer("\r\n", Encoding.ASCII)));

        // Write the last chunk.
        Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer("0\r\n\r\n", Encoding.ASCII)));

        // Ensure the last chunk was decoded.
        IHttpContent lastContent = ch.ReadInbound<IHttpContent>();
        Assert.False(lastContent.Content.IsReadable());
        lastContent.Release();

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestResponseChunkedExceedMaxChunkSize()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder(4096, 8192, 32));
        ch.WriteInbound(
                Unpooled.CopiedBuffer("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n", Encoding.ASCII));

        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.OK, res.Status());

        byte[] data = new byte[64];

        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte) i;
        }

        for (int i = 0; i < 10; i++)
        {
            Assert.False(ch.WriteInbound(Unpooled.CopiedBuffer(data.Length.ToString("x", CultureInfo.InvariantCulture) +
                    "\r\n", Encoding.ASCII)));
            Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(data)));

            byte[] decodedData = new byte[data.Length];
            IHttpContent content = ch.ReadInbound<IHttpContent>();
            Assert.Equal(32, content.Content.ReadableBytes);
            content.Content.ReadBytes(decodedData.AsSpan(0, 32));
            content.Release();

            content = ch.ReadInbound<IHttpContent>();
            Assert.Equal(32, content.Content.ReadableBytes);

            content.Content.ReadBytes(decodedData.AsSpan(32, 32));

            Assert.Equal(data, decodedData);
            content.Release();

            Assert.False(ch.WriteInbound(Unpooled.CopiedBuffer("\r\n", Encoding.ASCII)));
        }

        // Write the last chunk.
        ch.WriteInbound(Unpooled.CopiedBuffer("0\r\n\r\n", Encoding.ASCII));

        // Ensure the last chunk was decoded.
        ILastHttpContent lastContent = ch.ReadInbound<ILastHttpContent>();
        Assert.False(lastContent.Content.IsReadable());
        lastContent.Release();

        ch.Finish();
        Assert.Null(ch.ReadInbound());
    }

    [Fact]
    public void TestClosureWithoutContentLength1()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder());
        ch.WriteInbound(Unpooled.CopiedBuffer("HTTP/1.1 200 OK\r\n\r\n", Encoding.ASCII));

        // Read the response headers.
        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.OK, res.Status());
        Assert.Null(ch.ReadInbound());

        // Close the connection without sending anything.
        Assert.True(ch.Finish());

        // The decoder should still produce the last content.
        ILastHttpContent content = ch.ReadInbound<ILastHttpContent>();
        Assert.False(content.Content.IsReadable());
        content.Release();

        // But nothing more.
        Assert.Null(ch.ReadInbound());
    }

    [Fact]
    public void TestClosureWithoutContentLength2()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder());

        // Write the partial response.
        ch.WriteInbound(Unpooled.CopiedBuffer("HTTP/1.1 200 OK\r\n\r\n12345678", Encoding.ASCII));

        // Read the response headers.
        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.OK, res.Status());

        // Read the partial content.
        IHttpContent content = ch.ReadInbound<IHttpContent>();
        Assert.Equal("12345678", content.Content.ToString(Encoding.ASCII));
        Assert.False(content is ILastHttpContent);
        content.Release();

        Assert.Null(ch.ReadInbound());

        // Close the connection.
        Assert.True(ch.Finish());

        // The decoder should still produce the last content.
        ILastHttpContent lastContent = ch.ReadInbound<ILastHttpContent>();
        Assert.False(lastContent.Content.IsReadable());
        lastContent.Release();

        // But nothing more.
        Assert.Null(ch.ReadInbound());
    }

    [Fact]
    public void TestPrematureClosureWithChunkedEncoding1()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder());
        ch.WriteInbound(
                Unpooled.CopiedBuffer("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n", Encoding.ASCII));

        // Read the response headers.
        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.OK, res.Status());
        Assert.Equal("chunked", res.Headers().Get(HttpHeaderNames.TRANSFER_ENCODING));
        Assert.Null(ch.ReadInbound());

        // Close the connection without sending anything.
        ch.Finish();
        // The decoder should not generate the last chunk because it's closed prematurely.
        Assert.Null(ch.ReadInbound());
    }

    [Fact]
    public void TestPrematureClosureWithChunkedEncoding2()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder());

        // Write the partial response.
        ch.WriteInbound(Unpooled.CopiedBuffer(
                "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n8\r\n12345678", Encoding.ASCII));

        // Read the response headers.
        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.OK, res.Status());
        Assert.Equal("chunked", res.Headers().Get(HttpHeaderNames.TRANSFER_ENCODING));

        // Read the partial content.
        IHttpContent content = ch.ReadInbound<IHttpContent>();
        Assert.Equal("12345678", content.Content.ToString(Encoding.ASCII));
        Assert.False(content is ILastHttpContent);
        content.Release();

        Assert.Null(ch.ReadInbound());

        // Close the connection.
        ch.Finish();

        // The decoder should not generate the last chunk because it's closed prematurely.
        Assert.Null(ch.ReadInbound());
    }

    [Fact]
    public void TestLastResponseWithEmptyHeaderAndEmptyContent()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder());
        ch.WriteInbound(Unpooled.CopiedBuffer("HTTP/1.1 200 OK\r\n\r\n", Encoding.ASCII));

        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.OK, res.Status());
        Assert.Null(ch.ReadInbound());

        Assert.True(ch.Finish());

        ILastHttpContent content = ch.ReadInbound<ILastHttpContent>();
        Assert.False(content.Content.IsReadable());
        content.Release();

        Assert.Null(ch.ReadInbound());
    }

    [Fact]
    public void TestLastResponseWithoutContentLengthHeader()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder());
        ch.WriteInbound(Unpooled.CopiedBuffer("HTTP/1.1 200 OK\r\n\r\n", Encoding.ASCII));

        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.OK, res.Status());
        Assert.Null(ch.ReadInbound());

        ch.WriteInbound(Unpooled.WrappedBuffer(new byte[1024], 0, 1024));
        IHttpContent content = ch.ReadInbound<IHttpContent>();
        Assert.Equal(1024, content.Content.ReadableBytes);
        content.Release();

        Assert.True(ch.Finish());

        ILastHttpContent lastContent = ch.ReadInbound<ILastHttpContent>();
        Assert.False(lastContent.Content.IsReadable());
        lastContent.Release();

        Assert.Null(ch.ReadInbound());
    }

    [Fact]
    public void TestLastResponseWithHeaderRemoveTrailingSpaces()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder());
        ch.WriteInbound(Unpooled.CopiedBuffer(
                "HTTP/1.1 200 OK\r\nX-Header: h2=h2v2; Expires=Wed, 09-Jun-2021 10:18:14 GMT       \r\n\r\n",
                Encoding.ASCII));

        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.OK, res.Status());
        Assert.Equal("h2=h2v2; Expires=Wed, 09-Jun-2021 10:18:14 GMT", res.Headers().Get(Of("X-Header")));
        Assert.Null(ch.ReadInbound());

        ch.WriteInbound(Unpooled.WrappedBuffer(new byte[1024], 0, 1024));
        IHttpContent content = ch.ReadInbound<IHttpContent>();
        Assert.Equal(1024, content.Content.ReadableBytes);
        content.Release();

        Assert.True(ch.Finish());

        ILastHttpContent lastContent = ch.ReadInbound<ILastHttpContent>();
        Assert.False(lastContent.Content.IsReadable());
        lastContent.Release();

        Assert.Null(ch.ReadInbound());
    }

    [Fact]
    public void TestResetContentResponseWithTransferEncoding()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder());
        Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(
                "HTTP/1.1 205 Reset Content\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n" +
                "0\r\n" +
                "\r\n",
                Encoding.ASCII)));

        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.RESET_CONTENT, res.Status());

        ILastHttpContent lastContent = ch.ReadInbound<ILastHttpContent>();
        Assert.False(lastContent.Content.IsReadable());
        lastContent.Release();

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestLastResponseWithTrailingHeader()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder());
        ch.WriteInbound(Unpooled.CopiedBuffer(
                "HTTP/1.1 200 OK\r\n" +
                        "Transfer-Encoding: chunked\r\n" +
                        "\r\n" +
                        "0\r\n" +
                        "Set-Cookie: t1=t1v1\r\n" +
                        "Set-Cookie: t2=t2v2; Expires=Wed, 09-Jun-2021 10:18:14 GMT\r\n" +
                        "\r\n",
                Encoding.ASCII));

        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.OK, res.Status());

        ILastHttpContent lastContent = ch.ReadInbound<ILastHttpContent>();
        Assert.False(lastContent.Content.IsReadable());
        HttpHeaders headers = lastContent.TrailingHeaders();
        Assert.Equal(1, headers.Names().Count);
        IList<string> values = headers.GetAll(Of("Set-Cookie"));
        Assert.Equal(2, values.Count);
        Assert.True(values.Contains("t1=t1v1"));
        Assert.True(values.Contains("t2=t2v2; Expires=Wed, 09-Jun-2021 10:18:14 GMT"));
        lastContent.Release();

        Assert.False(ch.Finish());
        Assert.Null(ch.ReadInbound());
    }

    [Fact]
    public void TestLastResponseWithTrailingHeaderFragmented()
    {
        byte[] data = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n" +
                "0\r\n" +
                "Set-Cookie: t1=t1v1\r\n" +
                "Set-Cookie: t2=t2v2; Expires=Wed, 09-Jun-2021 10:18:14 GMT\r\n" +
                "\r\n");

        for (int i = 1; i < data.Length; i++)
        {
            TestLastResponseWithTrailingHeaderFragmented0(data, i);
        }
    }

    private static void TestLastResponseWithTrailingHeaderFragmented0(byte[] content, int fragmentSize)
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder());
        int headerLength = 47;

        // split up the header
        for (int a = 0; a < headerLength;)
        {
            int amount = fragmentSize;
            if (a + amount > headerLength)
            {
                amount = headerLength -  a;
            }

            // if header is done it should produce an HttpRequest
            bool headerDone = a + amount == headerLength;
            Assert.Equal(headerDone, ch.WriteInbound(Unpooled.CopiedBuffer(content, a, amount)));
            a += amount;
        }

        ch.WriteInbound(Unpooled.CopiedBuffer(content, headerLength, content.Length - headerLength));
        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.OK, res.Status());

        ILastHttpContent lastContent = ch.ReadInbound<ILastHttpContent>();
        Assert.False(lastContent.Content.IsReadable());
        HttpHeaders headers = lastContent.TrailingHeaders();
        Assert.Equal(1, headers.Names().Count);
        IList<string> values = headers.GetAll(Of("Set-Cookie"));
        Assert.Equal(2, values.Count);
        Assert.True(values.Contains("t1=t1v1"));
        Assert.True(values.Contains("t2=t2v2; Expires=Wed, 09-Jun-2021 10:18:14 GMT"));
        lastContent.Release();

        Assert.False(ch.Finish());
        Assert.Null(ch.ReadInbound());
    }

    [Fact]
    public void TestMultiLineTrailingHeader()
    {
        // Regression: folded trailer values previously threw UnsupportedOperationException
        // because trailingHeaders().getAll() returns an AbstractList that does not implement set().
        // Note: obs-fold in trailers is permitted as trailers are field-lines per
        // https://www.rfc-editor.org/rfc/rfc9112#section-5.2
        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder());
        string response = "HTTP/1.1 200 OK\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n" +
                "0\r\n" +
                "X-Long: part1\r\n" +
                "        part2\r\n" +
                "\t\t\t  part3\r\n" +
                "X-Short: value\r\n" +
                "\r\n";
        Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(response, Encoding.ASCII)));
        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.False(res.DecoderResult().IsFailure());
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.OK, res.Status());

        ILastHttpContent last = ch.ReadInbound<ILastHttpContent>();
        Assert.False(last.DecoderResult().IsFailure());
        Assert.Equal("part1 part2 part3", last.TrailingHeaders().Get(Of("X-Long")));
        Assert.Equal("value", last.TrailingHeaders().Get(Of("X-Short")));
        last.Release();
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestForbiddenTrailingHeadersAreDropped()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder());
        string response = "HTTP/1.1 200 OK\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n" +
                "0\r\n" +
                HttpHeaderNames.CONTENT_LENGTH + ": 5\r\n" +
                HttpHeaderNames.TRANSFER_ENCODING + ": chunked\r\n" +
                "X-Custom: keep\r\n" +
                HttpHeaderNames.TRAILER + ": X-Checksum\r\n" + // covering post-loop flush path
                "\r\n";
        Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(response, Encoding.ASCII)));
        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.False(res.DecoderResult().IsFailure());
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.OK, res.Status());

        ILastHttpContent last = ch.ReadInbound<ILastHttpContent>();
        Assert.False(last.DecoderResult().IsFailure());
        Assert.Null(last.TrailingHeaders().Get(HttpHeaderNames.CONTENT_LENGTH));
        Assert.Null(last.TrailingHeaders().Get(HttpHeaderNames.TRANSFER_ENCODING));
        Assert.Null(last.TrailingHeaders().Get(HttpHeaderNames.TRAILER));
        Assert.Equal("keep", last.TrailingHeaders().Get(Of("X-Custom")));
        last.Release();
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestResponseWithContentLength()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder());
        ch.WriteInbound(Unpooled.CopiedBuffer(
                "HTTP/1.1 200 OK\r\n" +
                        "Content-Length: 10\r\n" +
                        "\r\n", Encoding.ASCII));

        byte[] data = new byte[10];

        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte) i;
        }

        ch.WriteInbound(Unpooled.CopiedBuffer(data, 0, data.Length / 2));
        ch.WriteInbound(Unpooled.CopiedBuffer(data, 5, data.Length / 2));

        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.OK, res.Status());

        IHttpContent firstContent = ch.ReadInbound<IHttpContent>();
        Assert.Equal(5, firstContent.Content.ReadableBytes);
        Assert.Equal(Unpooled.CopiedBuffer(data, 0, 5), firstContent.Content);
        firstContent.Release();

        ILastHttpContent lastContent = ch.ReadInbound<ILastHttpContent>();
        Assert.Equal(5, lastContent.Content.ReadableBytes);
        Assert.Equal(Unpooled.CopiedBuffer(data, 5, 5), lastContent.Content);
        lastContent.Release();

        Assert.False(ch.Finish());
        Assert.Null(ch.ReadInbound());
    }

    [Fact]
    public void TestContentLengthHeaderAndChunkedHttp11()
    {
        string responseStr = "HTTP/1.1 200 OK\r\n" +
                "Connection: close\r\n" +
                "Content-Length: 5\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "0\r\n\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(responseStr, Encoding.ASCII)));
        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.True(response.DecoderResult().IsFailure());
        Assert.IsAssignableFrom<ContentLengthNotAllowedException>(response.DecoderResult().Cause());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestContentLengthHeaderAndChunkedHttp11RFC7230()
    {
        string responseStr = "HTTP/1.1 200 OK\r\n" +
                "Content-Length: 5\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "0\r\n\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder(
                new HttpDecoderConfig().SetUseRfc9112TransferEncoding(false)));
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(responseStr, Encoding.ASCII)));
        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.False(response.DecoderResult().IsFailure());
        Assert.True(response.Headers().Names().Contains("Transfer-Encoding"));
        Assert.True(response.Headers().Contains("Transfer-Encoding", "chunked", false));
        Assert.False(response.Headers().Contains("Content-Length"));
        ILastHttpContent c = channel.ReadInbound<ILastHttpContent>();
        c.Release();
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestContentLengthHeaderAndChunkedHttp10()
    {
        string responseStr = "HTTP/1.0 200 OK\r\n" +
                "Content-Length: 5\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "0\r\n\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(responseStr, Encoding.ASCII)));
        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.True(response.DecoderResult().IsFailure());
        Assert.IsAssignableFrom<TransferEncodingNotAllowedException>(response.DecoderResult().Cause());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestResponseWithContentLengthFragmented()
    {
        byte[] data = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\n" +
                "Content-Length: 10\r\n" +
                "\r\n");

        for (int i = 1; i < data.Length; i++)
        {
            TestResponseWithContentLengthFragmented0(data, i);
        }
    }

    private static void TestResponseWithContentLengthFragmented0(byte[] header, int fragmentSize)
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder());

        // split up the header
        for (int a = 0; a < header.Length;)
        {
            int amount = fragmentSize;
            if (a + amount > header.Length)
            {
                amount = header.Length -  a;
            }

            ch.WriteInbound(Unpooled.CopiedBuffer(header, a, amount));
            a += amount;
        }

        byte[] data = new byte[10];

        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte) i;
        }

        ch.WriteInbound(Unpooled.CopiedBuffer(data, 0, data.Length / 2));
        ch.WriteInbound(Unpooled.CopiedBuffer(data, 5, data.Length / 2));

        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.OK, res.Status());

        IHttpContent firstContent = ch.ReadInbound<IHttpContent>();
        Assert.Equal(5, firstContent.Content.ReadableBytes);
        Assert.Equal(Unpooled.WrappedBuffer(data, 0, 5), firstContent.Content);
        firstContent.Release();

        ILastHttpContent lastContent = ch.ReadInbound<ILastHttpContent>();
        Assert.Equal(5, lastContent.Content.ReadableBytes);
        Assert.Equal(Unpooled.WrappedBuffer(data, 5, 5), lastContent.Content);
        lastContent.Release();

        Assert.False(ch.Finish());
        Assert.Null(ch.ReadInbound());
    }

    [Fact]
    public void TestOrderOfHeadersWithContentLength()
    {
        string requestStr = "HTTP/1.1 200 OK\r\n" +
                "Content-Type: text/plain; charset=UTF-8\r\n" +
                "Content-Length: 5\r\n" +
                "Connection: close\r\n\r\n" +
                "hello";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        List<string> headers = new List<string>();

        foreach (IEntry<string, string> header in response.Headers())
        {
            headers.Add(header.GetKey());
        }

        Assert.Equal(new List<string> { "Content-Type", "Content-Length", "Connection" }, headers, "ordered headers");
    }

    [Fact]
    public void TestWebSocketResponse()
    {
        byte[] data = Encoding.Default.GetBytes("HTTP/1.1 101 WebSocket Protocol Handshake\r\n" +
                "Upgrade: WebSocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Origin: http://localhost:8080\r\n" +
                "Sec-WebSocket-Location: ws://localhost/some/path\r\n" +
                "\r\n" +
                "1234567812345678");
        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder());
        ch.WriteInbound(Unpooled.WrappedBuffer(data, 0, data.Length));

        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.SWITCHING_PROTOCOLS, res.Status());
        IHttpContent content = ch.ReadInbound<IHttpContent>();
        Assert.Equal(16, content.Content.ReadableBytes);
        content.Release();

        Assert.False(ch.Finish());

        Assert.Null(ch.ReadInbound());
    }

    // See https://github.com/netty/netty/issues/2173
    [Fact]
    public void TestWebSocketResponseWithDataFollowing()
    {
        byte[] data = Encoding.Default.GetBytes("HTTP/1.1 101 WebSocket Protocol Handshake\r\n" +
                "Upgrade: WebSocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Origin: http://localhost:8080\r\n" +
                "Sec-WebSocket-Location: ws://localhost/some/path\r\n" +
                "\r\n" +
                "1234567812345678");
        byte[] otherData = {1, 2, 3, 4};

        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder());
        ch.WriteInbound(Unpooled.CopiedBuffer(data, otherData));

        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.SWITCHING_PROTOCOLS, res.Status());
        IHttpContent content = ch.ReadInbound<IHttpContent>();
        Assert.Equal(16, content.Content.ReadableBytes);
        content.Release();

        Assert.True(ch.Finish());

        ByteBuf expected = Unpooled.WrappedBuffer(otherData, 0, otherData.Length);
        ByteBuf buffer = ch.ReadInbound<ByteBuf>();

        try
        {
            Assert.Equal(expected, buffer);
        }
        finally
        {
            expected.Release();

            if (buffer != null)
            {
                buffer.Release();
            }
        }
    }

    [Fact]
    public void TestGarbageHeaders()
    {
        // A response without headers - from https://github.com/netty/netty/issues/2103
        byte[] data = Encoding.Default.GetBytes("<html>\r\n" +
                "<head><title>400 Bad Request</title></head>\r\n" +
                "<body bgcolor=\"white\">\r\n" +
                "<center><h1>400 Bad Request</h1></center>\r\n" +
                "<hr><center>nginx/1.1.19</center>\r\n" +
                "</body>\r\n" +
                "</html>\r\n");

        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder());

        ch.WriteInbound(Unpooled.CopiedBuffer(data));

        // Garbage input should generate the 999 Unknown response.
        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.Same(HttpVersion.HTTP_1_0, res.ProtocolVersion());
        Assert.Equal(999, res.Status().Code());
        Assert.True(res.DecoderResult().IsFailure());
        Assert.True(res.DecoderResult().IsFinished());
        Assert.Null(ch.ReadInbound());

        // More garbage should not generate anything (i.e. the decoder discards anything beyond this point.)
        ch.WriteInbound(Unpooled.CopiedBuffer(data));
        Assert.Null(ch.ReadInbound());

        // Closing the connection should not generate anything since the protocol has been violated.
        ch.Finish();
        Assert.Null(ch.ReadInbound());
    }

    /**
     * Tests if the decoder produces one and only {@link LastHttpContent} when an invalid chunk is received and
     * the connection is closed.
     */
    [Fact]
    public void TestGarbageChunk()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        string responseWithIllegalChunk =
                "HTTP/1.1 200 OK\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "NOT_A_CHUNK_LENGTH\r\n";

        channel.WriteInbound(Unpooled.CopiedBuffer(responseWithIllegalChunk, Encoding.ASCII));
        Assert.IsAssignableFrom<IHttpResponse>(channel.ReadInbound());

        // Ensure that the decoder generates the last chunk with correct decoder result.
        ILastHttpContent invalidChunk = channel.ReadInbound<ILastHttpContent>();
        Assert.True(invalidChunk.DecoderResult().IsFailure());
        invalidChunk.Release();

        // And no more messages should be produced by the decoder.
        Assert.Null(channel.ReadInbound());

        // .. even after the connection is closed.
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestWhiteSpaceGarbageChunk()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        string responseWithIllegalChunk =
                "HTTP/1.1 200 OK\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                " \r\n";

        channel.WriteInbound(Unpooled.CopiedBuffer(responseWithIllegalChunk, Encoding.ASCII));
        Assert.IsAssignableFrom<IHttpResponse>(channel.ReadInbound());

        // Ensure that the decoder generates the last chunk with correct decoder result.
        ILastHttpContent invalidChunk = channel.ReadInbound<ILastHttpContent>();
        Assert.True(invalidChunk.DecoderResult().IsFailure());
        invalidChunk.Release();

        // And no more messages should be produced by the decoder.
        Assert.Null(channel.ReadInbound());

        // .. even after the connection is closed.
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestLeadingWhiteSpacesSemiColonGarbageChunk()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        string responseWithIllegalChunk =
                "HTTP/1.1 200 OK\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "  ;\r\n";

        channel.WriteInbound(Unpooled.CopiedBuffer(responseWithIllegalChunk, Encoding.ASCII));
        Assert.IsAssignableFrom<IHttpResponse>(channel.ReadInbound());

        // Ensure that the decoder generates the last chunk with correct decoder result.
        ILastHttpContent invalidChunk = channel.ReadInbound<ILastHttpContent>();
        Assert.True(invalidChunk.DecoderResult().IsFailure());
        invalidChunk.Release();

        // And no more messages should be produced by the decoder.
        Assert.Null(channel.ReadInbound());

        // .. even after the connection is closed.
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestControlCharGarbageChunk()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        string responseWithIllegalChunk =
                "HTTP/1.1 200 OK\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "\0\r\n";

        channel.WriteInbound(Unpooled.CopiedBuffer(responseWithIllegalChunk, Encoding.ASCII));
        Assert.IsAssignableFrom<IHttpResponse>(channel.ReadInbound());

        // Ensure that the decoder generates the last chunk with correct decoder result.
        ILastHttpContent invalidChunk = channel.ReadInbound<ILastHttpContent>();
        Assert.True(invalidChunk.DecoderResult().IsFailure());
        invalidChunk.Release();

        // And no more messages should be produced by the decoder.
        Assert.Null(channel.ReadInbound());

        // .. even after the connection is closed.
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestLeadingWhiteSpacesControlCharGarbageChunk()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        string responseWithIllegalChunk =
                "HTTP/1.1 200 OK\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "  \0\r\n";

        channel.WriteInbound(Unpooled.CopiedBuffer(responseWithIllegalChunk, Encoding.ASCII));
        Assert.IsAssignableFrom<IHttpResponse>(channel.ReadInbound());

        // Ensure that the decoder generates the last chunk with correct decoder result.
        ILastHttpContent invalidChunk = channel.ReadInbound<ILastHttpContent>();
        Assert.True(invalidChunk.DecoderResult().IsFailure());
        invalidChunk.Release();

        // And no more messages should be produced by the decoder.
        Assert.Null(channel.ReadInbound());

        // .. even after the connection is closed.
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestGarbageChunkAfterWhiteSpaces()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        string responseWithIllegalChunk =
                "HTTP/1.1 200 OK\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "  12345N1 ;\r\n";

        channel.WriteInbound(Unpooled.CopiedBuffer(responseWithIllegalChunk, Encoding.ASCII));
        Assert.IsAssignableFrom<IHttpResponse>(channel.ReadInbound());

        // Ensure that the decoder generates the last chunk with correct decoder result.
        ILastHttpContent invalidChunk = channel.ReadInbound<ILastHttpContent>();
        Assert.True(invalidChunk.DecoderResult().IsFailure());
        invalidChunk.Release();

        // And no more messages should be produced by the decoder.
        Assert.Null(channel.ReadInbound());

        // .. even after the connection is closed.
        Assert.False(channel.Finish());
    }

    [Fact]
    public void MustRejectImproperlyTerminatedChunkExtensions()
    {
        // See full explanation: https://w4ke.info/2025/06/18/funky-chunks.html
        string responseStr = "HTTP/1.1 200 OK\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n" +
                "2;\n" + // Chunk size followed by illegal single newline (not preceded by carraige return)
                "xx\r\n" +
                "1D\r\n" +
                "0\r\n\r\n" +
                "HTTP/1.1 200 OK\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "0\r\n\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(responseStr, Encoding.ASCII)));
        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.False(response.DecoderResult().IsFailure()); // We parse the headers just fine.
        Assert.True(response.Headers().Names().Contains("Transfer-Encoding"));
        Assert.True(response.Headers().Contains("Transfer-Encoding", "chunked", false));
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
        string responseStr = "HTTP/1.1 200 OK\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "5\r\n" +
                "AAAAXX" + // Chunk body contains extra (XX) bytes, and no CRLF terminator.
                "1D\r\n" +
                "0\r\n" +
                "HTTP/1.1 200 OK\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "0\r\n\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(responseStr, Encoding.ASCII)));
        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.False(response.DecoderResult().IsFailure()); // We parse the headers just fine.
        Assert.True(response.Headers().Names().Contains("Transfer-Encoding"));
        Assert.True(response.Headers().Contains("Transfer-Encoding", "chunked", false));
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
        string responseStr = "HTTP/1.1 200 OK\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n" +
                "1;a=\" ;\t\"\r\n" +
                "Y\r\n" +
                "0\r\n" +
                "\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(responseStr, Encoding.ASCII)));
        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.False(response.DecoderResult().IsFailure()); // We parse the headers just fine.
        Assert.True(response.Headers().Names().Contains("Transfer-Encoding"));
        Assert.True(response.Headers().Contains("Transfer-Encoding", "chunked", false));
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
        string responseStr = "HTTP/1.1 200 OK\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n" +
                "1;a=\"\r\n" + // chunk extension quote start
                "X\r\n" +
                "0\r\n\r\n" +
                "HTTP/1.1 200 OK\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "\"\r\n" + // chunk extension quote end
                "Y\r\n" +
                "0\r\n" +
                "\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(responseStr, Encoding.ASCII)));
        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.False(response.DecoderResult().IsFailure()); // We parse the headers just fine.
        Assert.True(response.Headers().Names().Contains("Transfer-Encoding"));
        Assert.True(response.Headers().Contains("Transfer-Encoding", "chunked", false));
        IHttpContent content = channel.ReadInbound<IHttpContent>();
        DecoderResult decoderResult = content.DecoderResult();
        Assert.True(decoderResult.IsFailure()); // Chunk extension is not allowed to contain line breaks.
        Assert.IsAssignableFrom<InvalidChunkExtensionException>(decoderResult.Cause());
        content.Release();
        Assert.False(channel.Finish()); // And there are no other chunks parsed.
    }

    [Fact]
    public void MustParsedChunkExtensionsWithQuotedStringsAndEscapes()
    {
        // See full explanation: https://w4ke.info/2025/10/29/funky-chunks-2.html
        string responseStr = "HTTP/1.1 200 OK\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n" +
                "1;a=\" \\\";\t\"\r\n" +
                "Y\r\n" +
                "0\r\n" +
                "\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(responseStr, Encoding.ASCII)));
        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.False(response.DecoderResult().IsFailure()); // We parse the headers just fine.
        Assert.True(response.Headers().Names().Contains("Transfer-Encoding"));
        Assert.True(response.Headers().Contains("Transfer-Encoding", "chunked", false));
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
        string responseStr = "HTTP/1.1 200 OK\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n" +
                "1;name1=val1;name2=val2\r\n" +
                "Y\r\n" +
                "0\r\n" +
                "\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(responseStr, Encoding.ASCII)));
        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.False(response.DecoderResult().IsFailure());
        IHttpContent content = channel.ReadInbound<IHttpContent>();
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
        string responseStr = "HTTP/1.1 200 OK\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n" +
                "1;a=\" \\\n;\t\"\r\n" +
                "Y\r\n" +
                "0\r\n" +
                "\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(responseStr, Encoding.ASCII)));
        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.False(response.DecoderResult().IsFailure()); // We parse the headers just fine.
        Assert.True(response.Headers().Names().Contains("Transfer-Encoding"));
        Assert.True(response.Headers().Contains("Transfer-Encoding", "chunked", false));
        IHttpContent content = channel.ReadInbound<IHttpContent>();
        DecoderResult decoderResult = content.DecoderResult();
        Assert.True(decoderResult.IsFailure()); // Chunk extension is not allowed to contain line breaks.
        Assert.IsAssignableFrom<InvalidChunkExtensionException>(decoderResult.Cause());
        content.Release();
        Assert.False(channel.Finish()); // And there are no other chunks parsed.
    }

    [Fact]
    public void MustRejectChunkExtensionsWithEscapedCarraigeReturnInQuotedStrings()
    {
        // See full explanation: https://w4ke.info/2025/10/29/funky-chunks-2.html
        string responseStr = "HTTP/1.1 200 OK\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n" +
                "1;a=\" \\\r;\t\"\r\n" +
                "Y\r\n" +
                "0\r\n" +
                "\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(responseStr, Encoding.ASCII)));
        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.False(response.DecoderResult().IsFailure()); // We parse the headers just fine.
        Assert.True(response.Headers().Names().Contains("Transfer-Encoding"));
        Assert.True(response.Headers().Contains("Transfer-Encoding", "chunked", false));
        IHttpContent content = channel.ReadInbound<IHttpContent>();
        DecoderResult decoderResult = content.DecoderResult();
        Assert.True(decoderResult.IsFailure()); // Chunk extension is not allowed to contain carriage returns.
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
        string responseStr = "HTTP/1.1 200 OK\r\n" +
                "Host: localhost\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                chars.Length.ToString("x", CultureInfo.InvariantCulture) + "\r\n" +
                requestContent + "\r\n" +
                "0\r\n\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(responseStr, Encoding.ASCII)));
        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.False(response.DecoderResult().IsFailure()); // We parse the headers just fine.
        Assert.True(response.Headers().Names().Contains("Transfer-Encoding"));
        Assert.True(response.Headers().Contains("Transfer-Encoding", "chunked", false));
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
        string responseStr = "HTTP/1.1 200 OK\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "\r\n" +
                "test\r\n\r\n" + // chunk extension quote start
                "\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(responseStr, Encoding.ASCII)));
        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.False(response.DecoderResult().IsFailure()); // We parse the headers
        IHttpContent content = channel.ReadInbound<IHttpContent>();
        Assert.True(content.DecoderResult().IsFailure());
        Assert.IsAssignableFrom<FormatException>(content.DecoderResult().Cause());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void MustRejectChunkSizeThatWouldCauseOverflow()
    {
        string requestStr = "HTTP/1.1 200 OK\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "100000004\r\n" +
                "test\r\n" +
                "0\r\n" +
                "\r\n" +
                "GET /smuggled HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "Content-Length: 0\r\n" +
                "\r\n";

        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));

        // Request 1
        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.True(response.DecoderResult().IsSuccess());
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
        string responseStr = "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "\r\n" +
            chunkSizeLine + "\r\n" + // malformed chunk size
            "GPOST\r\n" +
            "0\r\n" +
            "\r\n" +
            "HTTP/1.1 200 smuggled\r\n" +
            "Host: example\r\n" +
            "\r\n";

        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(responseStr, Encoding.ASCII)));

        // The response headers parse fine.
        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.True(response.DecoderResult().IsSuccess());
        Assert.Equal(HttpVersion.HTTP_1_1, response.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.OK, response.Status());

        // But the malformed chunk-size line must be rejected, not truncated at the whitespace.
        IHttpContent content = channel.ReadInbound<IHttpContent>();
        Assert.True(content.DecoderResult().IsFailure());
        Assert.Contains("chunk size", content.DecoderResult().Cause().Message);
        content.Release();

        // Nothing after the failed chunk may be surfaced as a second, smuggled response.
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestConnectionClosedBeforeHeadersReceived()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        string responseInitialLine =
                "HTTP/1.1 200 OK\r\n";
        Assert.False(channel.WriteInbound(Unpooled.CopiedBuffer(responseInitialLine, Encoding.ASCII)));
        Assert.True(channel.Finish());
        IHttpMessage message = channel.ReadInbound<IHttpMessage>();
        Assert.True(message.DecoderResult().IsFailure());
        Assert.IsAssignableFrom<PrematureChannelClosureException>(message.DecoderResult().Cause());
        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestTrailerWithEmptyLineInSeparateBuffer()
    {
        HttpResponseDecoder decoder = new HttpResponseDecoder();
        EmbeddedChannel channel = new EmbeddedChannel(decoder);

        string headers = "HTTP/1.1 200 OK\r\n"
                + "Transfer-Encoding: chunked\r\n"
                + "Trailer: My-Trailer\r\n";
        Assert.False(channel.WriteInbound(Unpooled.CopiedBuffer(Encoding.ASCII.GetBytes(headers))));
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(Encoding.ASCII.GetBytes("\r\n"))));

        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer("0\r\n", Encoding.ASCII)));
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer("My-Trailer: 42\r\n", Encoding.ASCII)));
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer("\r\n", Encoding.ASCII)));

        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.Equal(2, response.Headers().Size());
        Assert.Equal("chunked", response.Headers().Get(HttpHeaderNames.TRANSFER_ENCODING));
        Assert.Equal("My-Trailer", response.Headers().Get(HttpHeaderNames.TRAILER));

        ILastHttpContent lastContent = channel.ReadInbound<ILastHttpContent>();
        Assert.Equal(1, lastContent.TrailingHeaders().Size());
        Assert.Equal("42", lastContent.TrailingHeaders().Get("My-Trailer"));
        Assert.Equal(0, lastContent.Content.ReadableBytes);
        lastContent.Release();

        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestWhitespace()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        string requestStr = "HTTP/1.1 200 OK\r\n" +
                "Transfer-Encoding : chunked\r\n" +
                "Host: netty.io\r\n\r\n";

        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));
        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.False(response.DecoderResult().IsFailure());
        Assert.Equal(HttpHeaderValues.CHUNKED.ToString(), response.Headers().Get(HttpHeaderNames.TRANSFER_ENCODING));
        Assert.Equal("netty.io", response.Headers().Get(HttpHeaderNames.HOST));
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestHttpMessageDecoderResult()
    {
        string responseStr = "HTTP/1.1 200 OK\r\n" +
                "Content-Length: 11\r\n" +
                "Connection: close\r\n\r\n" +
                "Lorem ipsum";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(responseStr, Encoding.ASCII)));
        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.True(response.DecoderResult().IsSuccess());
        Assert.IsAssignableFrom<HttpMessageDecoderResult>(response.DecoderResult());
        HttpMessageDecoderResult decoderResult = (HttpMessageDecoderResult) response.DecoderResult();
        Assert.Equal(15, decoderResult.InitialLineLength());
        Assert.Equal(35, decoderResult.HeaderSize());
        Assert.Equal(50, decoderResult.TotalSize());
        IHttpContent c = channel.ReadInbound<IHttpContent>();
        c.Release();
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestStatusWithoutReasonPhrase()
    {
        string responseStr = "HTTP/1.1 200 \r\n" +
                "Content-Length: 0\r\n\r\n";
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(responseStr, Encoding.ASCII)));
        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.True(response.DecoderResult().IsSuccess());
        Assert.Equal(HttpResponseStatus.OK, response.Status());
        IHttpContent c = channel.ReadInbound<IHttpContent>();
        c.Release();
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
        ByteBuf responseBuffer = Unpooled.Buffer();
        responseBuffer.WriteCharSequence("HTTP/1.1 200 OK\r\n" +
                "Host: netty.io\r\n", Encoding.ASCII);
        responseBuffer.WriteByte(controlChar);
        responseBuffer.WriteCharSequence("Transfer-Encoding: chunked\r\n\r\n", Encoding.ASCII);
        TestInvalidHeaders0(responseBuffer);
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

    private static void TestHeaderNameEndsWithControlChar(int controlChar)
    {
        ByteBuf responseBuffer = Unpooled.Buffer();
        responseBuffer.WriteCharSequence("HTTP/1.1 200 OK\r\n" +
                "Host: netty.io\r\n", Encoding.ASCII);
        responseBuffer.WriteCharSequence("Transfer-Encoding", Encoding.ASCII);
        responseBuffer.WriteByte(controlChar);
        responseBuffer.WriteCharSequence(": chunked\r\n\r\n", Encoding.ASCII);
        TestInvalidHeaders0(responseBuffer);
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
        TestInvalidHeaders0(Unpooled.CopiedBuffer(
                version + " 200 OK\r\nHost: whatever\r\n\r\n", Encoding.ASCII));
    }

    private static void TestInvalidHeaders0(ByteBuf responseBuffer)
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseDecoder());
        Assert.True(channel.WriteInbound(responseBuffer));
        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.IsAssignableFrom<ArgumentException>(response.DecoderResult().Cause());
        Assert.True(response.DecoderResult().IsFailure());
        ReferenceCountUtil.Release(response);
        Assert.False(channel.Finish());
    }
}
