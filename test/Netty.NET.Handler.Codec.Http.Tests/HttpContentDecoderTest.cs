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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Compression;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests;

// CLR: the Java byte literals of GZ_HELLO_WORLD, SNAPPY_HELLO_WORLD and SAMPLE_BZ_BYTES are signed; they are sbyte
// arrays that Bytes() reinterprets as System.Byte values with the same bit patterns, as the codec-compression and
// codec-dns tests do (test/Netty.NET.Handler.Codec.Compression.Tests/SnappyFrameEncoderTest.cs). Decoding zstd needs
// zstd-jni, which is library-not-ported (owner decision 2026-11-23; docs/codec-compression-porting.md):
// HttpContentDecompressor passes a "zstd" Content-Encoding through, as Java does when Zstd.isAvailable() is false, so
// testResponseZstdDecompression and testResponseChunksZstdDecompression are CLR-inapplicable (recorded in the manifest)
// and SAMPLE_ZSTD_BYTES, which only they read, is left out. @DisabledIf("isNotSupported") skips the brotli tests where
// brotli4j has no native library (macOS on aarch_64); the codec-compression Brotli codecs are the BCL
// BrotliEncoder/BrotliDecoder and Brotli.IsAvailable() is always true (docs/codec-compression-porting.md), so the
// brotli tests always run and isNotSupported() is not ported. The anonymous HttpContentDecoder and
// ChannelInboundHandlerAdapter classes are the named nested CleanupThrowsDecoder, InactiveThrowsHandler,
// InactiveCountingHandler and SecondRequestHandler; AtomicBoolean/AtomicReference are the Common ones. assertInstanceOf
// is Assert.IsAssignableFrom, and assertThrows(CodecException.class, ...) accepts subclasses, so it is
// Assert.ThrowsAny<CodecException> (the DecoderException of the inner channel). EmbeddedChannel's
// readInbound()/readOutbound() are ReadInbound<T>()/ReadOutbound<T>(), inboundMessages() is the Queue<object>
// InboundMessages() (peek() is Peek(), after the size check), content() is Content and refCnt() is RefCnt
// (docs/transport-porting.md); new EmbeddedChannel(decoder, decompressor, null) stops at the null handler as in Java
// (src/Netty.NET.Transport/Channels/Embedded/EmbeddedChannel.cs). CharsetUtil.US_ASCII/UTF_8 are
// Encoding.ASCII/Encoding.UTF8 and String.getBytes() (the default charset) is Encoding.Default
// (docs/codec-smtp-porting.md; the bytes are ASCII). Integer.toHexString of the non-negative length is ToString("x")
// with the invariant culture, Long.parseLong is long.Parse with the invariant culture (the header values are decimal
// digits), and ByteBuf.readBytes(byte[], int, int) is ReadBytes(Span<byte>) over the array slice.
public class HttpContentDecoderTest
{
    private const string HELLO_WORLD = "hello, world";
    private static readonly byte[] GZ_HELLO_WORLD = Bytes(new sbyte[] {
            31, -117, 8, 8, 12, 3, -74, 84, 0, 3, 50, 0, -53, 72, -51, -55, -55,
            -41, 81, 40, -49, 47, -54, 73, 1, 0, 58, 114, -85, -1, 12, 0, 0, 0
    });
    private static readonly byte[] SNAPPY_HELLO_WORLD = Bytes(new sbyte[] {
            -1, 6, 0, 0, 115, 78, 97, 80, 112, 89, 1, 16, 0, 0, 11, -66, -63,
            -22, 104, 101, 108, 108, 111, 44, 32, 119, 111, 114, 108, 100
    });
    private const string SAMPLE_STRING = "Hello, I am Meow!. A small kitten. :)" +
            "I sleep all day, and meow all night.";
    private static readonly byte[] SAMPLE_BZ_BYTES = Bytes(new sbyte[]{27, 72, 0, 0, -60, -102, 91, -86, 103, 20,
            -28, -23, 54, -101, 11, -106, -16, -32, -95, -61, -37, 94, -16, 97, -40, -93, -56, 18, 21, 86,
            -110, 82, -41, 102, -89, 20, 11, 10, -68, -31, 96, -116, -55, -80, -31, -91, 96, -64, 83, 51,
            -39, 13, -21, 92, -16, -119, 124, -31, 18, 78, -1, 91, 82, 105, -116, -95, -22, -11, -70, -45, 0});

    // CLR: reinterprets the signed Java byte literals as System.Byte values.
    private static byte[] Bytes(sbyte[] p)
    {
        return Array.ConvertAll(p, b => unchecked((byte) b));
    }

    [Fact]
    public void TestBinaryDecompression()
    {
        // baseline test: zlib library and test helpers work correctly.
        byte[] helloWorld = GzDecompress(GZ_HELLO_WORLD);
        Assert.Equal(HELLO_WORLD.Length, helloWorld.Length);
        Assert.Equal(HELLO_WORLD, Encoding.ASCII.GetString(helloWorld));

        string fullCycleTest = "full cycle test";
        byte[] compressed = GzCompress(Encoding.ASCII.GetBytes(fullCycleTest));
        byte[] decompressed = GzDecompress(compressed);
        Assert.Equal(decompressed.Length, fullCycleTest.Length);
        Assert.Equal(fullCycleTest, Encoding.ASCII.GetString(decompressed));
    }

    [Fact]
    public void TestRequestDecompression()
    {
        // baseline test: request decoder, content decompressor && request aggregator work as expected
        HttpRequestDecoder decoder = new HttpRequestDecoder();
        HttpContentDecoder decompressor = new HttpContentDecompressor(0);
        HttpObjectAggregator aggregator = new HttpObjectAggregator(1024);
        EmbeddedChannel channel = new EmbeddedChannel(decoder, decompressor, aggregator);

        string headers = "POST / HTTP/1.1\r\n" +
                         "Content-Length: " + GZ_HELLO_WORLD.Length + "\r\n" +
                         "Content-Encoding: gzip\r\n" +
                         "\r\n";
        ByteBuf buf = Unpooled.CopiedBuffer(Encoding.ASCII.GetBytes(headers), GZ_HELLO_WORLD);
        Assert.True(channel.WriteInbound(buf));

        object o = channel.ReadInbound();
        Assert.IsAssignableFrom<IFullHttpRequest>(o);
        IFullHttpRequest req = (IFullHttpRequest) o;
        Assert.Equal(HELLO_WORLD.Length, req.Headers().GetInt(HttpHeaderNames.CONTENT_LENGTH).Value);
        Assert.Equal(HELLO_WORLD, req.Content.ToString(Encoding.ASCII));
        req.Release();

        AssertHasInboundMessages(channel, false);
        AssertHasOutboundMessages(channel, false);
        Assert.False(channel.Finish()); // assert that no messages are left in channel
    }

    [Fact]
    public void TestChunkedRequestDecompression()
    {
        HttpResponseDecoder decoder = new HttpResponseDecoder();
        HttpContentDecoder decompressor = new HttpContentDecompressor(0);

        EmbeddedChannel channel = new EmbeddedChannel(decoder, decompressor, null);

        string headers = "HTTP/1.1 200 OK\r\n"
                + "Transfer-Encoding: chunked\r\n"
                + "Trailer: My-Trailer\r\n"
                + "Content-Encoding: gzip\r\n\r\n";

        channel.WriteInbound(Unpooled.CopiedBuffer(Encoding.ASCII.GetBytes(headers)));

        string chunkLength = GZ_HELLO_WORLD.Length.ToString("x", CultureInfo.InvariantCulture);
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(chunkLength + "\r\n", Encoding.ASCII)));
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(GZ_HELLO_WORLD)));
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(Encoding.ASCII.GetBytes("\r\n"))));
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer("0\r\n", Encoding.ASCII)));
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer("My-Trailer: 42\r\n\r\n\r\n", Encoding.ASCII)));

        object ob1 = channel.ReadInbound();
        Assert.IsAssignableFrom<DefaultHttpResponse>(ob1);

        object ob2 = channel.ReadInbound();
        Assert.IsAssignableFrom<IHttpContent>(ob2);
        IHttpContent content = (IHttpContent) ob2;
        Assert.Equal(HELLO_WORLD, content.Content.ToString(Encoding.ASCII));
        content.Release();

        object ob3 = channel.ReadInbound();
        Assert.IsAssignableFrom<ILastHttpContent>(ob3);
        ILastHttpContent lastContent = (ILastHttpContent) ob3;
        Assert.NotNull(lastContent.DecoderResult());
        Assert.True(lastContent.DecoderResult().IsSuccess());
        Assert.False(lastContent.TrailingHeaders().IsEmpty());
        Assert.Equal("42", lastContent.TrailingHeaders().Get("My-Trailer"));
        AssertHasInboundMessages(channel, false);
        AssertHasOutboundMessages(channel, false);
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestSnappyResponseDecompression()
    {
        // baseline test: response decoder, content decompressor && request aggregator work as expected
        HttpResponseDecoder decoder = new HttpResponseDecoder();
        HttpContentDecoder decompressor = new HttpContentDecompressor(0);
        HttpObjectAggregator aggregator = new HttpObjectAggregator(1024);
        EmbeddedChannel channel = new EmbeddedChannel(decoder, decompressor, aggregator);

        string headers = "HTTP/1.1 200 OK\r\n" +
                         "Content-Length: " + SNAPPY_HELLO_WORLD.Length + "\r\n" +
                         "Content-Encoding: snappy\r\n" +
                         "\r\n";
        ByteBuf buf = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes(headers), SNAPPY_HELLO_WORLD);
        Assert.True(channel.WriteInbound(buf));

        object o = channel.ReadInbound();
        Assert.IsAssignableFrom<IFullHttpResponse>(o);
        IFullHttpResponse resp = (IFullHttpResponse) o;
        Assert.Equal(HELLO_WORLD.Length, resp.Headers().GetInt(HttpHeaderNames.CONTENT_LENGTH).Value);
        Assert.Equal(HELLO_WORLD, resp.Content.ToString(Encoding.UTF8));
        resp.Release();

        AssertHasInboundMessages(channel, false);
        AssertHasOutboundMessages(channel, false);
        Assert.False(channel.Finish()); // assert that no messages are left in channel
    }

    [Fact]
    public void TestResponseDecompression()
    {
        // baseline test: response decoder, content decompressor && request aggregator work as expected
        HttpResponseDecoder decoder = new HttpResponseDecoder();
        HttpContentDecoder decompressor = new HttpContentDecompressor(0);
        HttpObjectAggregator aggregator = new HttpObjectAggregator(1024);

        EmbeddedChannel channel = new EmbeddedChannel(decoder, decompressor, aggregator);

        string headers = "HTTP/1.1 200 OK\r\n" +
                "Content-Length: " + GZ_HELLO_WORLD.Length + "\r\n" +
                "Content-Encoding: gzip\r\n" +
                "\r\n";
        ByteBuf buf = Unpooled.CopiedBuffer(Encoding.ASCII.GetBytes(headers), GZ_HELLO_WORLD);
        Assert.True(channel.WriteInbound(buf));

        object o = channel.ReadInbound();
        Assert.IsAssignableFrom<IFullHttpResponse>(o);
        IFullHttpResponse resp = (IFullHttpResponse) o;
        Assert.Equal(HELLO_WORLD.Length, resp.Headers().GetInt(HttpHeaderNames.CONTENT_LENGTH).Value);
        Assert.Equal(HELLO_WORLD, resp.Content.ToString(Encoding.ASCII));
        resp.Release();

        AssertHasInboundMessages(channel, false);
        AssertHasOutboundMessages(channel, false);
        Assert.False(channel.Finish()); // assert that no messages are left in channel
    }

    [Fact]
    public void TestResponseBrotliDecompression()
    {
        Brotli.EnsureAvailability();

        HttpResponseDecoder decoder = new HttpResponseDecoder();
        HttpContentDecoder decompressor = new HttpContentDecompressor(0);
        HttpObjectAggregator aggregator = new HttpObjectAggregator(int.MaxValue);
        EmbeddedChannel channel = new EmbeddedChannel(decoder, decompressor, aggregator);

        string headers = "HTTP/1.1 200 OK\r\n" +
          "Content-Length: " + SAMPLE_BZ_BYTES.Length + "\r\n" +
          "Content-Encoding: br\r\n" +
          "\r\n";
        ByteBuf buf = Unpooled.WrappedBuffer(Encoding.ASCII.GetBytes(headers), SAMPLE_BZ_BYTES);
        Assert.True(channel.WriteInbound(buf));

        object o = channel.ReadInbound();
        Assert.IsAssignableFrom<IFullHttpResponse>(o);
        IFullHttpResponse resp = (IFullHttpResponse) o;
        Assert.Null(resp.Headers().Get(HttpHeaderNames.CONTENT_ENCODING), "Content-Encoding header should be removed");
        Assert.Equal(SAMPLE_STRING, resp.Content.ToString(Encoding.UTF8),
                "Response body should match uncompressed string");
        resp.Release();

        AssertHasInboundMessages(channel, false);
        AssertHasOutboundMessages(channel, false);
        Assert.False(channel.Finish()); // assert that no messages are left in channel
    }

    [Fact]
    public void TestResponseChunksBrotliDecompression()
    {
        Brotli.EnsureAvailability();

        HttpResponseDecoder decoder = new HttpResponseDecoder();
        HttpContentDecoder decompressor = new HttpContentDecompressor(0);
        HttpObjectAggregator aggregator = new HttpObjectAggregator(int.MaxValue);
        EmbeddedChannel channel = new EmbeddedChannel(decoder, decompressor, aggregator);

        string headers = "HTTP/1.1 200 OK\r\n" +
          "Content-Length: " + SAMPLE_BZ_BYTES.Length + "\r\n" +
          "Content-Encoding: br\r\n" +
          "\r\n";

        Assert.False(channel.WriteInbound(Unpooled.WrappedBuffer(Encoding.ASCII.GetBytes(headers))));

        int offset = 0;

        while (offset < SAMPLE_BZ_BYTES.Length)
        {
            int len = Math.Min(1500, SAMPLE_BZ_BYTES.Length - offset);
            bool available = channel.WriteInbound(Unpooled.WrappedBuffer(SAMPLE_BZ_BYTES, offset, len));
            offset += 1500;
            if (offset < SAMPLE_BZ_BYTES.Length)
            {
                Assert.False(available);
            }
            else
            {
                Assert.True(available);
            }
        }

        object o = channel.ReadInbound();
        Assert.IsAssignableFrom<IFullHttpResponse>(o);
        IFullHttpResponse resp = (IFullHttpResponse) o;
        Assert.Equal(SAMPLE_STRING, resp.Content.ToString(Encoding.UTF8),
                "Response body should match uncompressed string");
        resp.Release();

        AssertHasInboundMessages(channel, false);
        AssertHasOutboundMessages(channel, false);
        Assert.False(channel.Finish()); // assert that no messages are left in channel
    }

    [Fact]
    public void TestExpectContinueResponse1()
    {
        // request with header "Expect: 100-continue" must be replied with one "100 Continue" response
        // case 1: no ContentDecoder in chain at all (baseline test)
        HttpRequestDecoder decoder = new HttpRequestDecoder();
        HttpObjectAggregator aggregator = new HttpObjectAggregator(1024);
        EmbeddedChannel channel = new EmbeddedChannel(decoder, aggregator);
        string req = "POST / HTTP/1.1\r\n" +
                     "Content-Length: " + GZ_HELLO_WORLD.Length + "\r\n" +
                     "Expect: 100-continue\r\n" +
                     "\r\n";
        // note: the following writeInbound() returns false as there is no message is inbound buffer
        // until HttpObjectAggregator caches composes a complete message.
        // however, http response "100 continue" must be sent as soon as headers are received
        Assert.False(channel.WriteInbound(Unpooled.WrappedBuffer(Encoding.Default.GetBytes(req))));

        object o = channel.ReadOutbound();
        Assert.IsAssignableFrom<IFullHttpResponse>(o);
        IFullHttpResponse r = (IFullHttpResponse) o;
        Assert.Equal(100, r.Status().Code());
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(GZ_HELLO_WORLD)));
        r.Release();

        AssertHasInboundMessages(channel, true);
        AssertHasOutboundMessages(channel, false);
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestExpectContinueResponse2()
    {
        // request with header "Expect: 100-continue" must be replied with one "100 Continue" response
        // case 2: contentDecoder is in chain, but the content is not encoded, should be no-op
        HttpRequestDecoder decoder = new HttpRequestDecoder();
        HttpContentDecoder decompressor = new HttpContentDecompressor(0);
        HttpObjectAggregator aggregator = new HttpObjectAggregator(1024);
        EmbeddedChannel channel = new EmbeddedChannel(decoder, decompressor, aggregator);
        string req = "POST / HTTP/1.1\r\n" +
                     "Content-Length: " + GZ_HELLO_WORLD.Length + "\r\n" +
                     "Expect: 100-continue\r\n" +
                     "\r\n";
        Assert.False(channel.WriteInbound(Unpooled.WrappedBuffer(Encoding.Default.GetBytes(req))));

        object o = channel.ReadOutbound();
        Assert.IsAssignableFrom<IFullHttpResponse>(o);
        IFullHttpResponse r = (IFullHttpResponse) o;
        Assert.Equal(100, r.Status().Code());
        r.Release();
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(GZ_HELLO_WORLD)));

        AssertHasInboundMessages(channel, true);
        AssertHasOutboundMessages(channel, false);
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestExpectContinueResponse3()
    {
        // request with header "Expect: 100-continue" must be replied with one "100 Continue" response
        // case 3: ContentDecoder is in chain and content is encoded
        HttpRequestDecoder decoder = new HttpRequestDecoder();
        HttpContentDecoder decompressor = new HttpContentDecompressor(0);
        HttpObjectAggregator aggregator = new HttpObjectAggregator(1024);
        EmbeddedChannel channel = new EmbeddedChannel(decoder, decompressor, aggregator);
        string req = "POST / HTTP/1.1\r\n" +
                     "Content-Length: " + GZ_HELLO_WORLD.Length + "\r\n" +
                     "Expect: 100-continue\r\n" +
                     "Content-Encoding: gzip\r\n" +
                     "\r\n";
        Assert.False(channel.WriteInbound(Unpooled.WrappedBuffer(Encoding.Default.GetBytes(req))));

        object o = channel.ReadOutbound();
        Assert.IsAssignableFrom<IFullHttpResponse>(o);
        IFullHttpResponse r = (IFullHttpResponse) o;
        Assert.Equal(100, r.Status().Code());
        r.Release();
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(GZ_HELLO_WORLD)));

        AssertHasInboundMessages(channel, true);
        AssertHasOutboundMessages(channel, false);
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestExpectContinueResponse4()
    {
        // request with header "Expect: 100-continue" must be replied with one "100 Continue" response
        // case 4: ObjectAggregator is up in chain
        HttpRequestDecoder decoder = new HttpRequestDecoder();
        HttpObjectAggregator aggregator = new HttpObjectAggregator(1024);
        HttpContentDecoder decompressor = new HttpContentDecompressor(0);
        EmbeddedChannel channel = new EmbeddedChannel(decoder, aggregator, decompressor);
        string req = "POST / HTTP/1.1\r\n" +
                     "Content-Length: " + GZ_HELLO_WORLD.Length + "\r\n" +
                     "Expect: 100-continue\r\n" +
                     "Content-Encoding: gzip\r\n" +
                     "\r\n";
        Assert.False(channel.WriteInbound(Unpooled.WrappedBuffer(Encoding.Default.GetBytes(req))));

        object o = channel.ReadOutbound();
        Assert.IsAssignableFrom<IFullHttpResponse>(o);
        IFullHttpResponse r = (IFullHttpResponse) o;
        Assert.Equal(100, r.Status().Code());
        r.Release();
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(GZ_HELLO_WORLD)));

        AssertHasInboundMessages(channel, true);
        AssertHasOutboundMessages(channel, false);
        Assert.False(channel.Finish());
    }

    private sealed class SecondRequestHandler : ChannelInboundHandlerAdapter
    {
        private readonly AtomicReference<IFullHttpRequest> secondRequestRef;

        internal SecondRequestHandler(AtomicReference<IFullHttpRequest> secondRequestRef)
        {
            this.secondRequestRef = secondRequestRef;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is IFullHttpRequest)
            {
                if (!secondRequestRef.CompareAndSet(null, (IFullHttpRequest) msg))
                {
                    ((IFullHttpRequest) msg).Release();
                }
            }
            else
            {
                ReferenceCountUtil.Release(msg);
            }
        }
    }

    [Fact]
    public void TestExpectContinueResetHttpObjectDecoder()
    {
        // request with header "Expect: 100-continue" must be replied with one "100 Continue" response
        // case 5: Test that HttpObjectDecoder correctly resets its internal state after a failed expectation.
        HttpRequestDecoder decoder = new HttpRequestDecoder();
        const int maxBytes = 10;
        HttpObjectAggregator aggregator = new HttpObjectAggregator(maxBytes);
        AtomicReference<IFullHttpRequest> secondRequestRef = new AtomicReference<IFullHttpRequest>();
        EmbeddedChannel channel = new EmbeddedChannel(decoder, aggregator, new SecondRequestHandler(secondRequestRef));
        string req1 = "POST /1 HTTP/1.1\r\n" +
                "Content-Length: " + (maxBytes + 1) + "\r\n" +
                "Expect: 100-continue\r\n" +
                "\r\n";
        Assert.False(channel.WriteInbound(Unpooled.WrappedBuffer(Encoding.ASCII.GetBytes(req1))));

        IFullHttpResponse resp = channel.ReadOutbound<IFullHttpResponse>();
        Assert.Equal(HttpStatusClass.CLIENT_ERROR, resp.Status().CodeClass());
        resp.Release();

        string req2 = "POST /2 HTTP/1.1\r\n" +
                "Content-Length: " + maxBytes + "\r\n" +
                "Expect: 100-continue\r\n" +
                "\r\n";
        Assert.False(channel.WriteInbound(Unpooled.WrappedBuffer(Encoding.ASCII.GetBytes(req2))));

        resp = channel.ReadOutbound<IFullHttpResponse>();
        Assert.Equal(100, resp.Status().Code());
        resp.Release();

        byte[] content = new byte[maxBytes];
        Assert.False(channel.WriteInbound(Unpooled.WrappedBuffer(content)));

        IFullHttpRequest req = secondRequestRef.Get();
        Assert.NotNull(req);
        Assert.Equal("/2", req.Uri());
        Assert.Equal(10, req.Content.ReadableBytes);
        req.Release();

        AssertHasInboundMessages(channel, false);
        AssertHasOutboundMessages(channel, false);
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestRequestContentLength1()
    {
        // case 1: test that ContentDecompressor either sets the correct Content-Length header
        // or removes it completely (handlers down the chain must rely on LastHttpContent object)

        // force content to be in more than one chunk (5 bytes/chunk)
        HttpRequestDecoder decoder = new HttpRequestDecoder(4096, 4096, 5);
        HttpContentDecoder decompressor = new HttpContentDecompressor(0);
        EmbeddedChannel channel = new EmbeddedChannel(decoder, decompressor);
        string headers = "POST / HTTP/1.1\r\n" +
                         "Content-Length: " + GZ_HELLO_WORLD.Length + "\r\n" +
                         "Content-Encoding: gzip\r\n" +
                         "\r\n";
        ByteBuf buf = Unpooled.CopiedBuffer(Encoding.ASCII.GetBytes(headers), GZ_HELLO_WORLD);
        Assert.True(channel.WriteInbound(buf));

        Queue<object> req = channel.InboundMessages();
        Assert.True(req.Count >= 1);
        object o = req.Peek();
        Assert.IsAssignableFrom<IHttpRequest>(o);
        IHttpRequest r = (IHttpRequest) o;
        string v = r.Headers().Get(HttpHeaderNames.CONTENT_LENGTH);
        long? value = v == null ? null : long.Parse(v, CultureInfo.InvariantCulture);
        Assert.True(value == null || value.Value == HELLO_WORLD.Length);

        AssertHasInboundMessages(channel, true);
        AssertHasOutboundMessages(channel, false);
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestRequestContentLength2()
    {
        // case 2: if HttpObjectAggregator is down the chain, then correct Content-Length header must be set

        // force content to be in more than one chunk (5 bytes/chunk)
        HttpRequestDecoder decoder = new HttpRequestDecoder(4096, 4096, 5);
        HttpContentDecoder decompressor = new HttpContentDecompressor(0);
        HttpObjectAggregator aggregator = new HttpObjectAggregator(1024);
        EmbeddedChannel channel = new EmbeddedChannel(decoder, decompressor, aggregator);
        string headers = "POST / HTTP/1.1\r\n" +
                         "Content-Length: " + GZ_HELLO_WORLD.Length + "\r\n" +
                         "Content-Encoding: gzip\r\n" +
                         "\r\n";
        ByteBuf buf = Unpooled.CopiedBuffer(Encoding.ASCII.GetBytes(headers), GZ_HELLO_WORLD);
        Assert.True(channel.WriteInbound(buf));

        object o = channel.ReadInbound();
        Assert.IsAssignableFrom<IFullHttpRequest>(o);
        IFullHttpRequest r = (IFullHttpRequest) o;
        string v = r.Headers().Get(HttpHeaderNames.CONTENT_LENGTH);
        long? value = v == null ? null : long.Parse(v, CultureInfo.InvariantCulture);

        r.Release();
        Assert.NotNull(value);
        Assert.Equal((long) HELLO_WORLD.Length, value.Value);

        AssertHasInboundMessages(channel, false);
        AssertHasOutboundMessages(channel, false);
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestResponseContentLength1()
    {
        // case 1: test that ContentDecompressor either sets the correct Content-Length header
        // or removes it completely (handlers down the chain must rely on LastHttpContent object)

        // force content to be in more than one chunk (5 bytes/chunk)
        HttpResponseDecoder decoder = new HttpResponseDecoder(4096, 4096, 5);
        HttpContentDecoder decompressor = new HttpContentDecompressor(0);
        EmbeddedChannel channel = new EmbeddedChannel(decoder, decompressor);
        string headers = "HTTP/1.1 200 OK\r\n" +
                         "Content-Length: " + GZ_HELLO_WORLD.Length + "\r\n" +
                         "Content-Encoding: gzip\r\n" +
                         "\r\n";
        ByteBuf buf = Unpooled.CopiedBuffer(Encoding.ASCII.GetBytes(headers), GZ_HELLO_WORLD);
        Assert.True(channel.WriteInbound(buf));

        Queue<object> resp = channel.InboundMessages();
        Assert.True(resp.Count >= 1);
        object o = resp.Peek();
        Assert.IsAssignableFrom<IHttpResponse>(o);
        IHttpResponse r = (IHttpResponse) o;

        Assert.False(r.Headers().Contains(HttpHeaderNames.CONTENT_LENGTH), "Content-Length header not removed.");

        string transferEncoding = r.Headers().Get(HttpHeaderNames.TRANSFER_ENCODING);
        Assert.NotNull(transferEncoding, "Content-length as well as transfer-encoding not set.");
        Assert.Equal(HttpHeaderValues.CHUNKED.ToString(), transferEncoding, "Unexpected transfer-encoding value.");

        AssertHasInboundMessages(channel, true);
        AssertHasOutboundMessages(channel, false);
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestResponseContentLength2()
    {
        // case 2: if HttpObjectAggregator is down the chain, then correct Content-Length header must be set

        // force content to be in more than one chunk (5 bytes/chunk)
        HttpResponseDecoder decoder = new HttpResponseDecoder(4096, 4096, 5);
        HttpContentDecoder decompressor = new HttpContentDecompressor(0);
        HttpObjectAggregator aggregator = new HttpObjectAggregator(1024);
        EmbeddedChannel channel = new EmbeddedChannel(decoder, decompressor, aggregator);
        string headers = "HTTP/1.1 200 OK\r\n" +
                         "Content-Length: " + GZ_HELLO_WORLD.Length + "\r\n" +
                         "Content-Encoding: gzip\r\n" +
                         "\r\n";
        ByteBuf buf = Unpooled.CopiedBuffer(Encoding.ASCII.GetBytes(headers), GZ_HELLO_WORLD);
        Assert.True(channel.WriteInbound(buf));

        object o = channel.ReadInbound();
        Assert.IsAssignableFrom<IFullHttpResponse>(o);
        IFullHttpResponse r = (IFullHttpResponse) o;
        string v = r.Headers().Get(HttpHeaderNames.CONTENT_LENGTH);
        long? value = v == null ? null : long.Parse(v, CultureInfo.InvariantCulture);
        Assert.NotNull(value);
        Assert.Equal((long) HELLO_WORLD.Length, value.Value);
        r.Release();

        AssertHasInboundMessages(channel, false);
        AssertHasOutboundMessages(channel, false);
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestFullHttpRequest()
    {
        // test that ContentDecoder can be used after the ObjectAggregator
        HttpRequestDecoder decoder = new HttpRequestDecoder(4096, 4096, 5);
        HttpObjectAggregator aggregator = new HttpObjectAggregator(1024);
        HttpContentDecoder decompressor = new HttpContentDecompressor(0);
        EmbeddedChannel channel = new EmbeddedChannel(decoder, aggregator, decompressor);
        string headers = "POST / HTTP/1.1\r\n" +
                         "Content-Length: " + GZ_HELLO_WORLD.Length + "\r\n" +
                         "Content-Encoding: gzip\r\n" +
                         "\r\n";
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(Encoding.Default.GetBytes(headers), GZ_HELLO_WORLD)));

        Queue<object> req = channel.InboundMessages();
        Assert.True(req.Count > 1);
        int contentLength = 0;
        contentLength = CalculateContentLength(req, contentLength);

        byte[] receivedContent = ReadContent(req, contentLength, true);

        Assert.Equal(HELLO_WORLD, Encoding.ASCII.GetString(receivedContent));

        AssertHasInboundMessages(channel, true);
        AssertHasOutboundMessages(channel, false);
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestFullHttpResponse()
    {
        // test that ContentDecoder can be used after the ObjectAggregator
        HttpResponseDecoder decoder = new HttpResponseDecoder(4096, 4096, 5);
        HttpObjectAggregator aggregator = new HttpObjectAggregator(1024);
        HttpContentDecoder decompressor = new HttpContentDecompressor(0);
        EmbeddedChannel channel = new EmbeddedChannel(decoder, aggregator, decompressor);
        string headers = "HTTP/1.1 200 OK\r\n" +
                         "Content-Length: " + GZ_HELLO_WORLD.Length + "\r\n" +
                         "Content-Encoding: gzip\r\n" +
                         "\r\n";
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(Encoding.Default.GetBytes(headers), GZ_HELLO_WORLD)));

        Queue<object> resp = channel.InboundMessages();
        Assert.True(resp.Count > 1);
        int contentLength = 0;
        contentLength = CalculateContentLength(resp, contentLength);

        byte[] receivedContent = ReadContent(resp, contentLength, true);

        Assert.Equal(HELLO_WORLD, Encoding.ASCII.GetString(receivedContent));

        AssertHasInboundMessages(channel, true);
        AssertHasOutboundMessages(channel, false);
        Assert.False(channel.Finish());
    }

    // See https://github.com/netty/netty/issues/5892
    [Fact]
    public void TestFullHttpResponseEOF()
    {
        // test that ContentDecoder can be used after the ObjectAggregator
        HttpResponseDecoder decoder = new HttpResponseDecoder(4096, 4096, 5);
        HttpContentDecoder decompressor = new HttpContentDecompressor(0);
        EmbeddedChannel channel = new EmbeddedChannel(decoder, decompressor);
        string headers = "HTTP/1.1 200 OK\r\n" +
                "Content-Encoding: gzip\r\n" +
                "\r\n";
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(Encoding.Default.GetBytes(headers), GZ_HELLO_WORLD)));
        // This should terminate it.
        Assert.True(channel.Finish());

        Queue<object> resp = channel.InboundMessages();
        Assert.True(resp.Count > 1);
        int contentLength = 0;
        contentLength = CalculateContentLength(resp, contentLength);

        byte[] receivedContent = ReadContent(resp, contentLength, false);

        Assert.Equal(HELLO_WORLD, Encoding.ASCII.GetString(receivedContent));

        AssertHasInboundMessages(channel, true);
        AssertHasOutboundMessages(channel, false);
        Assert.False(channel.Finish());
    }

    private sealed class CleanupThrowsDecoder : HttpContentDecoder
    {
        protected override EmbeddedChannel NewContentDecoder(string contentEncoding)
        {
            return new EmbeddedChannel(new InactiveThrowsHandler());
        }
    }

    private sealed class InactiveThrowsHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            ctx.FireExceptionCaught(new DecoderException());
            ctx.FireChannelInactive();
        }
    }

    private sealed class InactiveCountingHandler : ChannelInboundHandlerAdapter
    {
        private readonly AtomicBoolean channelInactiveCalled;

        internal InactiveCountingHandler(AtomicBoolean channelInactiveCalled)
        {
            this.channelInactiveCalled = channelInactiveCalled;
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            Assert.True(channelInactiveCalled.CompareAndSet(false, true));
            base.ChannelInactive(ctx);
        }
    }

    [Fact]
    public void TestCleanupThrows()
    {
        HttpContentDecoder decoder = new CleanupThrowsDecoder();

        AtomicBoolean channelInactiveCalled = new AtomicBoolean();
        EmbeddedChannel channel = new EmbeddedChannel(decoder, new InactiveCountingHandler(channelInactiveCalled));
        Assert.True(channel.WriteInbound(new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/")));
        IHttpContent content = new DefaultHttpContent(Unpooled.Buffer().WriteZero(10));
        Assert.True(channel.WriteInbound(content));
        Assert.Equal(1, content.RefCnt);
        Assert.ThrowsAny<CodecException>(() => channel.FinishAndReleaseAll());
        Assert.True(channelInactiveCalled.Get());
        Assert.Equal(0, content.RefCnt);
    }

    [Fact]
    public void TestTransferCodingGZIP()
    {
        string responseStr = "HTTP/1.1 200 OK\r\n" +
                "Connection: close\r\n" +
                "Transfer-Encoding: gzip\r\n" +
                "\r\n";
        HttpResponseDecoder decoder = new HttpResponseDecoder();
        HttpContentDecoder decompressor = new HttpContentDecompressor(0);
        EmbeddedChannel channel = new EmbeddedChannel(decoder, decompressor);

        channel.WriteInbound(Unpooled.CopiedBuffer(Encoding.Default.GetBytes(responseStr)));
        channel.WriteInbound(Unpooled.CopiedBuffer(GZ_HELLO_WORLD));

        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.True(response.DecoderResult().IsSuccess());
        Assert.False(response.Headers().Contains(HttpHeaderNames.CONTENT_LENGTH));

        IHttpContent content = channel.ReadInbound<IHttpContent>();
        Assert.True(content.DecoderResult().IsSuccess());
        Assert.Equal(HELLO_WORLD, content.Content.ToString(Encoding.ASCII));
        content.Release();

        Assert.True(channel.Finish());
        ILastHttpContent lastHttpContent = channel.ReadInbound<ILastHttpContent>();
        Assert.True(lastHttpContent.DecoderResult().IsSuccess());
        lastHttpContent.Release();

        AssertHasInboundMessages(channel, false);
        AssertHasOutboundMessages(channel, false);
        channel.ReleaseInbound();
    }

    [Fact]
    public void TestTransferCodingGZIPAndChunked()
    {
        string requestStr = "POST / HTTP/1.1\r\n" +
                "Host: example.com\r\n" +
                "Content-Type: application/x-www-form-urlencoded\r\n" +
                "Trailer: My-Trailer\r\n" +
                "Transfer-Encoding: gzip, chunked\r\n" +
                "\r\n";
        HttpRequestDecoder decoder = new HttpRequestDecoder();
        HttpContentDecoder decompressor = new HttpContentDecompressor(0);
        EmbeddedChannel channel = new EmbeddedChannel(decoder, decompressor);

        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));

        string chunkLength = GZ_HELLO_WORLD.Length.ToString("x", CultureInfo.InvariantCulture);
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(chunkLength + "\r\n", Encoding.ASCII)));
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(GZ_HELLO_WORLD)));
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(Encoding.ASCII.GetBytes("\r\n"))));
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer("0\r\n", Encoding.ASCII)));
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer("My-Trailer: 42\r\n\r\n", Encoding.ASCII)));

        IHttpRequest request = channel.ReadInbound<IHttpRequest>();
        Assert.True(request.DecoderResult().IsSuccess());
        Assert.True(request.Headers().ContainsValue(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED, true));
        Assert.False(request.Headers().Contains(HttpHeaderNames.CONTENT_LENGTH));

        IHttpContent chunk1 = channel.ReadInbound<IHttpContent>();
        Assert.True(chunk1.DecoderResult().IsSuccess());
        Assert.Equal(HELLO_WORLD, chunk1.Content.ToString(Encoding.ASCII));
        chunk1.Release();

        ILastHttpContent chunk2 = channel.ReadInbound<ILastHttpContent>();
        Assert.True(chunk2.DecoderResult().IsSuccess());
        Assert.Equal("42", chunk2.TrailingHeaders().Get("My-Trailer"));
        chunk2.Release();

        Assert.False(channel.Finish());
        channel.ReleaseInbound();
    }

    private static byte[] GzDecompress(byte[] input)
    {
        ZlibDecoder decoder = ZlibCodecFactory.NewZlibDecoder(ZlibWrapper.GZIP, 0);
        EmbeddedChannel channel = new EmbeddedChannel(decoder);
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(input)));
        Assert.True(channel.Finish()); // close the channel to indicate end-of-data

        int outputSize = 0;
        ByteBuf o;
        List<ByteBuf> inbound = new List<ByteBuf>();

        while ((o = channel.ReadInbound<ByteBuf>()) != null)
        {
            inbound.Add(o);
            outputSize += o.ReadableBytes;
        }

        byte[] output = new byte[outputSize];
        int readCount = 0;

        foreach (ByteBuf b in inbound)
        {
            int readableBytes = b.ReadableBytes;
            b.ReadBytes(output.AsSpan(readCount, readableBytes));
            b.Release();
            readCount += readableBytes;
        }

        Assert.True(channel.InboundMessages().Count == 0 && channel.OutboundMessages().Count == 0);
        return output;
    }

    private static byte[] ReadContent(Queue<object> req, int contentLength, bool hasTransferEncoding)
    {
        byte[] receivedContent = new byte[contentLength];
        int readCount = 0;

        foreach (object o in req)
        {
            if (o is IHttpContent)
            {
                ByteBuf b = ((IHttpContent) o).Content;
                int readableBytes = b.ReadableBytes;
                b.ReadBytes(receivedContent.AsSpan(readCount, readableBytes));
                readCount += readableBytes;
            }

            if (o is IHttpMessage)
            {
                Assert.Equal(hasTransferEncoding,
                        ((IHttpMessage) o).Headers().Contains(HttpHeaderNames.TRANSFER_ENCODING));
            }
        }

        return receivedContent;
    }

    private static int CalculateContentLength(Queue<object> req, int contentLength)
    {
        foreach (object o in req)
        {
            if (o is IHttpContent)
            {
                Assert.True(((IHttpContent) o).RefCnt > 0);
                ByteBuf b = ((IHttpContent) o).Content;
                contentLength += b.ReadableBytes;
            }
        }

        return contentLength;
    }

    private static byte[] GzCompress(byte[] input)
    {
        ZlibEncoder encoder = ZlibCodecFactory.NewZlibEncoder(ZlibWrapper.GZIP);
        EmbeddedChannel channel = new EmbeddedChannel(encoder);
        Assert.True(channel.WriteOutbound(Unpooled.WrappedBuffer(input)));
        Assert.True(channel.Finish());  // close the channel to indicate end-of-data

        int outputSize = 0;
        ByteBuf o;
        List<ByteBuf> outbound = new List<ByteBuf>();

        while ((o = channel.ReadOutbound<ByteBuf>()) != null)
        {
            outbound.Add(o);
            outputSize += o.ReadableBytes;
        }

        byte[] output = new byte[outputSize];
        int readCount = 0;

        foreach (ByteBuf b in outbound)
        {
            int readableBytes = b.ReadableBytes;
            b.ReadBytes(output.AsSpan(readCount, readableBytes));
            b.Release();
            readCount += readableBytes;
        }

        Assert.True(channel.InboundMessages().Count == 0 && channel.OutboundMessages().Count == 0);
        return output;
    }

    private static void AssertHasInboundMessages(EmbeddedChannel channel, bool hasMessages)
    {
        object o;

        if (hasMessages)
        {
            while (true)
            {
                o = channel.ReadInbound();
                Assert.NotNull(o);
                ReferenceCountUtil.Release(o);
                if (o is ILastHttpContent)
                {
                    break;
                }
            }
        }
        else
        {
            o = channel.ReadInbound();
            Assert.Null(o);
        }
    }

    private static void AssertHasOutboundMessages(EmbeddedChannel channel, bool hasMessages)
    {
        object o;

        if (hasMessages)
        {
            while (true)
            {
                o = channel.ReadOutbound();
                Assert.NotNull(o);
                ReferenceCountUtil.Release(o);
                if (o is ILastHttpContent)
                {
                    break;
                }
            }
        }
        else
        {
            o = channel.ReadOutbound();
            Assert.Null(o);
        }
    }
}
