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
using Netty.NET.Common;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels.Embedded;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests;

// CLR: CharsetUtil.UTF_8/US_ASCII are Encoding.UTF8/Encoding.ASCII (docs/codec-smtp-porting.md); String.getBytes(UTF_8)
// is Encoding.UTF8.GetBytes. assertThat(x).isInstanceOf(T.class) and assertInstanceOf are Assert.IsAssignableFrom
// (https://raw.githubusercontent.com/xunit/assert.xunit/main/TypeAsserts.cs); assertDoesNotThrow(executable) records the
// exception with Record.Exception and asserts that none was thrown
// (https://raw.githubusercontent.com/xunit/assert.xunit/main/Record.cs). The Java messages of assertTrue/assertFalse use
// XunitAssertMessages.AssertM. Numbers concatenated into request text use the invariant culture. A C# local is in
// scope in its whole block, so the method-level buf/encoded of the Java HEAD checks, declared after loop-scoped locals
// of the same names, are headBuf/headEncoded (CS0136: https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0136).
public class HttpServerCodecTest
{

    /**
     * Testcase for https://github.com/netty/netty/issues/433
     */
    [Fact]
    public void TestUnfinishedChunkedHttpRequestIsLastFlag()
    {

        int maxChunkSize = 2000;
        HttpServerCodec httpServerCodec = new HttpServerCodec(1000, 1000, maxChunkSize);
        EmbeddedChannel decoderEmbedder = new EmbeddedChannel(httpServerCodec);

        int totalContentLength = maxChunkSize * 5;
        decoderEmbedder.WriteInbound(Unpooled.CopiedBuffer(
                "PUT /test HTTP/1.1\r\n" +
                "Content-Length: " + totalContentLength.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                "\r\n", Encoding.UTF8));

        int offeredContentLength = (int) (maxChunkSize * 2.5);
        decoderEmbedder.WriteInbound(PrepareDataChunk(offeredContentLength));
        decoderEmbedder.Finish();

        IHttpMessage httpMessage = decoderEmbedder.ReadInbound<IHttpMessage>();
        Assert.NotNull(httpMessage);

        bool empty = true;
        int totalBytesPolled = 0;
        for (;;)
        {
            IHttpContent httpChunk = decoderEmbedder.ReadInbound<IHttpContent>();
            if (httpChunk == null)
            {
                break;
            }
            empty = false;
            totalBytesPolled += httpChunk.Content.ReadableBytes;
            Assert.False(httpChunk is ILastHttpContent);
            httpChunk.Release();
        }
        Assert.False(empty);
        Assert.Equal(offeredContentLength, totalBytesPolled);
    }

    [Fact]
    public void Test100Continue()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpServerCodec(), new HttpObjectAggregator(1024));

        // Send the request headers.
        ch.WriteInbound(Unpooled.CopiedBuffer(
                "PUT /upload-large HTTP/1.1\r\n" +
                "Expect: 100-continue\r\n" +
                "Content-Length: 1\r\n\r\n", Encoding.UTF8));

        // Ensure the aggregator generates nothing.
        Assert.Null(ch.ReadInbound());

        // Ensure the aggregator writes a 100 Continue response.
        ByteBuf continueResponse = ch.ReadOutbound<ByteBuf>();
        Assert.Equal("HTTP/1.1 100 Continue\r\n\r\n", continueResponse.ToString(Encoding.UTF8));
        continueResponse.Release();

        // But nothing more.
        Assert.Null(ch.ReadOutbound());

        // Send the content of the request.
        ch.WriteInbound(Unpooled.WrappedBuffer(new byte[] { 42 }));

        // Ensure the aggregator generates a full request.
        IFullHttpRequest req = ch.ReadInbound<IFullHttpRequest>();
        Assert.Equal("1", req.Headers().Get(HttpHeaderNames.CONTENT_LENGTH));
        Assert.Equal(1, req.Content.ReadableBytes);
        Assert.Equal((byte) 42, req.Content.ReadByte());
        req.Release();

        // But nothing more.
        Assert.Null(ch.ReadInbound());

        // Send the actual response.
        IFullHttpResponse res = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.CREATED);
        res.Content.WriteBytes(Encoding.UTF8.GetBytes("OK"));
        res.Headers().SetInt(HttpHeaderNames.CONTENT_LENGTH, 2);
        ch.WriteOutbound(res);

        // Ensure the encoder handles the response after handling 100 Continue.
        ByteBuf encodedRes = ch.ReadOutbound<ByteBuf>();
        Assert.Equal("HTTP/1.1 201 Created\r\n" + HttpHeaderNames.CONTENT_LENGTH + ": 2\r\n\r\nOK",
                encodedRes.ToString(Encoding.UTF8));
        encodedRes.Release();

        ch.Finish();
    }

    [Fact]
    public void TestChunkedHeadResponse()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpServerCodec());

        // Send the request headers.
        Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(
                "HEAD / HTTP/1.1\r\n\r\n", Encoding.UTF8)));

        IHttpRequest request = ch.ReadInbound<IHttpRequest>();
        Assert.Equal(HttpMethod.HEAD, request.Method());
        ILastHttpContent content = ch.ReadInbound<ILastHttpContent>();
        Assert.False(content.Content.IsReadable());
        content.Release();

        IHttpResponse response = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        HttpUtil.SetTransferEncodingChunked(response, true);
        Assert.True(ch.WriteOutbound(response));
        Assert.True(ch.WriteOutbound(ILastHttpContent.EMPTY_LAST_CONTENT));
        Assert.True(ch.Finish());

        ByteBuf buf = ch.ReadOutbound<ByteBuf>();
        Assert.Equal("HTTP/1.1 200 OK\r\ntransfer-encoding: chunked\r\n\r\n", buf.ToString(Encoding.ASCII));
        buf.Release();

        buf = ch.ReadOutbound<ByteBuf>();
        Assert.False(buf.IsReadable());
        buf.Release();

        Assert.False(ch.FinishAndReleaseAll());
    }

    [Fact]
    public void TestChunkedHeadFullHttpResponse()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpServerCodec());

        // Send the request headers.
        Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(
                "HEAD / HTTP/1.1\r\n\r\n", Encoding.UTF8)));

        IHttpRequest request = ch.ReadInbound<IHttpRequest>();
        Assert.Equal(HttpMethod.HEAD, request.Method());
        ILastHttpContent content = ch.ReadInbound<ILastHttpContent>();
        Assert.False(content.Content.IsReadable());
        content.Release();

        IFullHttpResponse response = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        HttpUtil.SetTransferEncodingChunked(response, true);
        Assert.True(ch.WriteOutbound(response));
        Assert.True(ch.Finish());

        ByteBuf buf = ch.ReadOutbound<ByteBuf>();
        Assert.Equal("HTTP/1.1 200 OK\r\ntransfer-encoding: chunked\r\n\r\n", buf.ToString(Encoding.ASCII));
        buf.Release();

        Assert.False(ch.FinishAndReleaseAll());
    }

    [Fact]
    public void TestConnectionClosedAfterResponseWhenBothTransferEncodingAndContentLengthRfc9112()
    {
        // We reject these requests by default.
        EmbeddedChannel ch = new EmbeddedChannel(new HttpServerCodec());

        string requestStr = "POST / HTTP/1.1\r\n" +
                "Host: example.com\r\n" +
                "Content-Length: 5\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "0\r\n\r\n";

        Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));

        IHttpRequest request = ch.ReadInbound<IHttpRequest>();
        Assert.True(request.DecoderResult().IsFailure());
        Assert.IsAssignableFrom<ContentLengthNotAllowedException>(request.DecoderResult().Cause());
        Assert.False(ch.FinishAndReleaseAll());
    }

    [Fact]
    public void TestConnectionClosedAfterResponseWhenBothTransferEncodingAndContentLengthRfc7230()
    {
        // Leniency, or "RFC 7230" mode, can be configured but the connection is then closed after.
        EmbeddedChannel ch = new EmbeddedChannel(new HttpServerCodec(
                new HttpDecoderConfig().SetUseRfc9112TransferEncoding(false)));

        string requestStr = "POST / HTTP/1.1\r\n" +
                "Host: example.com\r\n" +
                "Content-Length: 5\r\n" +
                "Transfer-Encoding: chunked\r\n\r\n" +
                "0\r\n\r\n";

        Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(requestStr, Encoding.ASCII)));

        IHttpRequest request = ch.ReadInbound<IHttpRequest>();
        Assert.False(request.DecoderResult().IsFailure());
        Assert.False(HttpUtil.IsKeepAlive(request));
        ILastHttpContent content = ch.ReadInbound<ILastHttpContent>();
        ReferenceCountUtil.Release(content);

        IFullHttpResponse response = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        HttpUtil.SetContentLength(response, 0);

        Assert.True(ch.WriteOutbound(response));
        // Channel should be closed after the response is written
        Assert.False(ch.IsOpen);

        ReferenceCountUtil.Release(ch.ReadOutbound());
        Assert.False(ch.FinishAndReleaseAll());
    }

    [Fact]
    public void TestInterleavedRequestResponseAcrossOverflow()
    {
        // Test interleaved enqueue/dequeue that crosses the inline-to-overflow boundary.
        // Send some requests, process some responses, then send more requests to trigger overflow.
        EmbeddedChannel ch = new EmbeddedChannel(new HttpServerCodec());

        // Send 30 GET requests (not yet filling the 32-slot inline queue).
        StringBuilder requests = new StringBuilder();
        for (int i = 0; i < 30; i++)
        {
            requests.Append("GET /").Append(i.ToString(CultureInfo.InvariantCulture)).Append(" HTTP/1.1\r\nHost: a\r\n\r\n");
        }
        Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(requests.ToString(), Encoding.UTF8)));

        // Drain inbound.
        for (;;)
        {
            object msg = ch.ReadInbound();
            if (msg == null)
            {
                break;
            }
            if (msg is IHttpContent)
            {
                ((IHttpContent) msg).Release();
            }
        }

        // Respond to 10 of them (draining 10 from the queue, leaving 20).
        for (int i = 0; i < 10; i++)
        {
            IFullHttpResponse resp = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
                    Unpooled.CopiedBuffer("ok", Encoding.UTF8));
            resp.Headers().SetInt(HttpHeaderNames.CONTENT_LENGTH, 2);
            Assert.True(ch.WriteOutbound(resp));
            ByteBuf buf = ch.ReadOutbound<ByteBuf>();
            buf.Release();
        }

        // Now send 15 more requests (20 remaining + 15 = 35 total, exceeding inline capacity of 32).
        // Put a HEAD request as the last one to verify overflow ordering.
        requests = new StringBuilder();
        for (int i = 30; i < 44; i++)
        {
            requests.Append("GET /").Append(i.ToString(CultureInfo.InvariantCulture)).Append(" HTTP/1.1\r\nHost: a\r\n\r\n");
        }
        requests.Append("HEAD /44 HTTP/1.1\r\nHost: a\r\n\r\n");
        Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(requests.ToString(), Encoding.UTF8)));

        // Drain inbound.
        for (;;)
        {
            object msg = ch.ReadInbound();
            if (msg == null)
            {
                break;
            }
            if (msg is IHttpContent)
            {
                ((IHttpContent) msg).Release();
            }
        }

        // Respond to remaining 20 GET requests (inline queue).
        for (int i = 10; i < 30; i++)
        {
            IFullHttpResponse resp = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
                    Unpooled.CopiedBuffer("ok", Encoding.UTF8));
            resp.Headers().SetInt(HttpHeaderNames.CONTENT_LENGTH, 2);
            Assert.True(ch.WriteOutbound(resp));
            ByteBuf buf = ch.ReadOutbound<ByteBuf>();
            buf.Release();
        }

        // Respond to the 14 GET requests that were added.
        for (int i = 30; i < 44; i++)
        {
            IFullHttpResponse resp = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
                    Unpooled.CopiedBuffer("ok", Encoding.UTF8));
            resp.Headers().SetInt(HttpHeaderNames.CONTENT_LENGTH, 2);
            Assert.True(ch.WriteOutbound(resp));
            ByteBuf buf = ch.ReadOutbound<ByteBuf>();
            string encoded = buf.ToString(Encoding.ASCII);
            Assert.True(encoded.Contains("ok", StringComparison.Ordinal),
                    "GET response at position " + i.ToString(CultureInfo.InvariantCulture) + " should contain body");
            buf.Release();
        }

        // Respond to the HEAD request at position 44 — must be content-always-empty.
        IHttpResponse headResp = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        headResp.Headers().SetInt(HttpHeaderNames.CONTENT_LENGTH, 999);
        Assert.True(ch.WriteOutbound(headResp));
        Assert.True(ch.WriteOutbound(ILastHttpContent.EMPTY_LAST_CONTENT));

        ByteBuf headBuf = ch.ReadOutbound<ByteBuf>();
        string headEncoded = headBuf.ToString(Encoding.ASCII);
        Assert.True(headEncoded.Contains("HTTP/1.1 200 OK", StringComparison.Ordinal), "HEAD response should be 200 OK");
        headBuf.Release();

        headBuf = ch.ReadOutbound<ByteBuf>();
        Assert.False(headBuf.IsReadable(), "HEAD response body should be empty in overflow scenario");
        headBuf.Release();

        Assert.False(ch.FinishAndReleaseAll());
    }

    [Fact]
    public void TestGetMethodHasNormalBody()
    {
        // Verify a simple GET request is treated as METHOD_FLAG_NONE and body is included.
        EmbeddedChannel ch = new EmbeddedChannel(new HttpServerCodec());

        Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(
                "GET / HTTP/1.1\r\nHost: a\r\n\r\n", Encoding.UTF8)));

        IHttpRequest request = ch.ReadInbound<IHttpRequest>();
        Assert.Equal(HttpMethod.GET, request.Method());
        ILastHttpContent content = ch.ReadInbound<ILastHttpContent>();
        content.Release();

        IFullHttpResponse resp = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
                Unpooled.CopiedBuffer("hello", Encoding.UTF8));
        resp.Headers().SetInt(HttpHeaderNames.CONTENT_LENGTH, 5);
        Assert.True(ch.WriteOutbound(resp));

        ByteBuf buf = ch.ReadOutbound<ByteBuf>();
        string encoded = buf.ToString(Encoding.ASCII);
        Assert.True(encoded.Contains("hello", StringComparison.Ordinal), "GET response should contain body");
        Assert.True(encoded.Contains("content-length: 5", StringComparison.Ordinal));
        buf.Release();

        Assert.False(ch.FinishAndReleaseAll());
    }

    [Fact]
    public void TestPostMethodHasNormalBody()
    {
        // POST should also be METHOD_FLAG_NONE.
        EmbeddedChannel ch = new EmbeddedChannel(new HttpServerCodec());

        Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(
                "POST / HTTP/1.1\r\nHost: a\r\nContent-Length: 0\r\n\r\n", Encoding.UTF8)));

        for (;;)
        {
            object msg = ch.ReadInbound();
            if (msg == null)
            {
                break;
            }
            if (msg is IHttpContent)
            {
                ((IHttpContent) msg).Release();
            }
        }

        IFullHttpResponse resp = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
                Unpooled.CopiedBuffer("result", Encoding.UTF8));
        resp.Headers().SetInt(HttpHeaderNames.CONTENT_LENGTH, 6);
        Assert.True(ch.WriteOutbound(resp));

        ByteBuf buf = ch.ReadOutbound<ByteBuf>();
        string encoded = buf.ToString(Encoding.ASCII);
        Assert.True(encoded.Contains("result", StringComparison.Ordinal), "POST response should contain body");
        buf.Release();

        Assert.False(ch.FinishAndReleaseAll());
    }

    [Fact]
    public void TestOverflowDrainsBeforeInlineRefill()
    {
        // Verify that once overflow is activated, subsequent enqueues also go to overflow
        // until the overflow drains, keeping FIFO order correct.
        EmbeddedChannel ch = new EmbeddedChannel(new HttpServerCodec());

        int totalRequests = 34; // 32 inline + 2 overflow

        // Send 34 pipelined requests: position 33 (overflow) is HEAD.
        StringBuilder requests = new StringBuilder();
        for (int i = 0; i < totalRequests; i++)
        {
            if (i == 33)
            {
                requests.Append("HEAD /").Append(i.ToString(CultureInfo.InvariantCulture)).Append(" HTTP/1.1\r\nHost: a\r\n\r\n");
            }
            else
            {
                requests.Append("GET /").Append(i.ToString(CultureInfo.InvariantCulture)).Append(" HTTP/1.1\r\nHost: a\r\n\r\n");
            }
        }
        Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(requests.ToString(), Encoding.UTF8)));

        // Drain inbound.
        for (;;)
        {
            object msg = ch.ReadInbound();
            if (msg == null)
            {
                break;
            }
            if (msg is IHttpContent)
            {
                ((IHttpContent) msg).Release();
            }
        }

        // Send responses for first 33 (all GET).
        for (int i = 0; i < 33; i++)
        {
            IFullHttpResponse resp = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
                    Unpooled.CopiedBuffer("ok", Encoding.UTF8));
            resp.Headers().SetInt(HttpHeaderNames.CONTENT_LENGTH, 2);
            Assert.True(ch.WriteOutbound(resp));
            ByteBuf buf = ch.ReadOutbound<ByteBuf>();
            string encoded = buf.ToString(Encoding.ASCII);
            Assert.True(encoded.Contains("ok", StringComparison.Ordinal),
                    "GET response at position " + i.ToString(CultureInfo.InvariantCulture) + " should contain body");
            buf.Release();
        }

        // Response for position 33 — HEAD from overflow queue.
        IHttpResponse headResp = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        headResp.Headers().SetInt(HttpHeaderNames.CONTENT_LENGTH, 42);
        Assert.True(ch.WriteOutbound(headResp));
        Assert.True(ch.WriteOutbound(ILastHttpContent.EMPTY_LAST_CONTENT));

        ByteBuf headBuf = ch.ReadOutbound<ByteBuf>();
        headBuf.Release();

        headBuf = ch.ReadOutbound<ByteBuf>();
        Assert.False(headBuf.IsReadable(), "HEAD response from overflow queue body should be empty");
        headBuf.Release();

        Assert.False(ch.FinishAndReleaseAll());
    }

    [Fact]
    public void TestPollFromEmptyQueueReturnsNone()
    {
        // If a response is written without a matching request (unusual but defensive),
        // pollMethod should return METHOD_FLAG_NONE, so body is treated normally.
        EmbeddedChannel ch = new EmbeddedChannel(new HttpServerCodec());

        // Write a response without any prior request.
        IFullHttpResponse resp = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
                Unpooled.CopiedBuffer("data", Encoding.UTF8));
        resp.Headers().SetInt(HttpHeaderNames.CONTENT_LENGTH, 4);
        Assert.Null(Record.Exception(() => ch.WriteOutbound(resp)));

        ByteBuf buf = ch.ReadOutbound<ByteBuf>();
        Assert.NotNull(buf);
        string encoded = buf.ToString(Encoding.ASCII);
        Assert.True(encoded.Contains("data", StringComparison.Ordinal),
                "Response body should be present even without prior request");
        buf.Release();

        ch.FinishAndReleaseAll();
    }

    [Fact]
    public void TestHeadResponseHasNoContentAfterInterimResponse()
    {
        // A 1xx interim response must not consume an entry of the method queue, as it is not the
        // final response for the queued request.
        EmbeddedChannel ch = new EmbeddedChannel(new HttpServerCodec());

        // Send a single HEAD request. Method queue = [HEAD].
        Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(
                "HEAD /a HTTP/1.1\r\nHost: a\r\n\r\n", Encoding.UTF8)));
        IHttpRequest request = ch.ReadInbound<IHttpRequest>();
        Assert.Equal(HttpMethod.HEAD, request.Method());
        ILastHttpContent requestContent = ch.ReadInbound<ILastHttpContent>();
        Assert.False(requestContent.Content.IsReadable());
        requestContent.Release();

        // Write a 103 Early Hints interim response. The HEAD entry must stay in the queue.
        IFullHttpResponse earlyHints = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.EARLY_HINTS);
        Assert.True(ch.WriteOutbound(earlyHints));
        ByteBuf earlyHintsBuf = ch.ReadOutbound<ByteBuf>();
        Assert.Equal("HTTP/1.1 103 Early Hints\r\n\r\n", earlyHintsBuf.ToString(Encoding.ASCII));
        earlyHintsBuf.Release();

        // Now write the final response for the HEAD request, with content attached.
        IFullHttpResponse finalResponse = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
                Unpooled.CopiedBuffer("body", Encoding.UTF8));
        finalResponse.Headers().SetInt(HttpHeaderNames.CONTENT_LENGTH, 4);
        Assert.True(ch.WriteOutbound(finalResponse));

        // A HEAD response must never carry a body per RFC 9110 section 9.3.2, regardless of
        // any interim responses sent before it.
        ByteBuf buf = ch.ReadOutbound<ByteBuf>();
        Assert.Equal("HTTP/1.1 200 OK\r\ncontent-length: 4\r\n\r\n", buf.ToString(Encoding.ASCII));
        buf.Release();

        Assert.False(ch.FinishAndReleaseAll());
    }

    [Fact]
    public void TestPipelinedResponseContentPreservedAfterInterimResponse()
    {
        // A 1xx interim response for one pipelined request must not consume a method queue entry,
        // so the queue order is preserved for the remaining pipelined requests.
        EmbeddedChannel ch = new EmbeddedChannel(new HttpServerCodec());

        // Pipeline two requests: GET /a then HEAD /b. Method queue = [OTHER, HEAD].
        Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(
                "GET /a HTTP/1.1\r\nHost: a\r\n\r\n" +
                "HEAD /b HTTP/1.1\r\nHost: a\r\n\r\n", Encoding.UTF8)));

        IHttpRequest requestA = ch.ReadInbound<IHttpRequest>();
        Assert.Equal(HttpMethod.GET, requestA.Method());
        ILastHttpContent requestAContent = ch.ReadInbound<ILastHttpContent>();
        Assert.False(requestAContent.Content.IsReadable());
        requestAContent.Release();

        IHttpRequest requestB = ch.ReadInbound<IHttpRequest>();
        Assert.Equal(HttpMethod.HEAD, requestB.Method());
        ILastHttpContent requestBContent = ch.ReadInbound<ILastHttpContent>();
        Assert.False(requestBContent.Content.IsReadable());
        requestBContent.Release();

        // Send a 1xx interim response for /a. The queue must stay at [OTHER, HEAD].
        IFullHttpResponse earlyHints = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.EARLY_HINTS);
        Assert.True(ch.WriteOutbound(earlyHints));
        ByteBuf earlyHintsBuf = ch.ReadOutbound<ByteBuf>();
        Assert.Equal("HTTP/1.1 103 Early Hints\r\n\r\n", earlyHintsBuf.ToString(Encoding.ASCII));
        earlyHintsBuf.Release();

        // Now send the final response to /a, with content attached.
        IFullHttpResponse finalResponseA = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
                Unpooled.CopiedBuffer("body-a", Encoding.UTF8));
        finalResponseA.Headers().SetInt(HttpHeaderNames.CONTENT_LENGTH, 6);
        Assert.True(ch.WriteOutbound(finalResponseA));

        // The GET /a response body must be preserved.
        ByteBuf buf = ch.ReadOutbound<ByteBuf>();
        Assert.Equal("HTTP/1.1 200 OK\r\ncontent-length: 6\r\n\r\nbody-a", buf.ToString(Encoding.ASCII));
        buf.Release();

        Assert.False(ch.FinishAndReleaseAll());
    }

    [Fact]
    public void TestSwitchingProtocolsResponseHasNoContent()
    {
        // A 101 Switching Protocols response must never carry a body.
        EmbeddedChannel ch = new EmbeddedChannel(new HttpServerCodec());

        // Send a HEAD request that asks to switch protocols. Method queue = [HEAD].
        Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(
                "HEAD / HTTP/1.1\r\n" +
                "Host: a\r\n" +
                "Connection: upgrade\r\n" +
                "Upgrade: websocket\r\n\r\n", Encoding.UTF8)));
        IHttpRequest request = ch.ReadInbound<IHttpRequest>();
        Assert.Equal(HttpMethod.HEAD, request.Method());
        ILastHttpContent requestContent = ch.ReadInbound<ILastHttpContent>();
        Assert.False(requestContent.Content.IsReadable());
        requestContent.Release();

        IHttpResponse response = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.SWITCHING_PROTOCOLS);
        response.Headers().Set(HttpHeaderNames.CONNECTION, "upgrade");
        response.Headers().Set(HttpHeaderNames.UPGRADE, "websocket");
        Assert.True(ch.WriteOutbound(response));
        Assert.True(ch.WriteOutbound(ILastHttpContent.EMPTY_LAST_CONTENT));

        ByteBuf buf = ch.ReadOutbound<ByteBuf>();
        Assert.Equal("HTTP/1.1 101 Switching Protocols\r\n" +
                "connection: upgrade\r\n" +
                "upgrade: websocket\r\n\r\n", buf.ToString(Encoding.ASCII));
        buf.Release();

        buf = ch.ReadOutbound<ByteBuf>();
        Assert.False(buf.IsReadable());
        buf.Release();

        Assert.False(ch.FinishAndReleaseAll());
    }

    [Fact]
    public void TestMaxPipelineDepthLimited()
    {
        HttpServerCodec codec = new HttpServerCodec(new HttpDecoderConfig(), 2);
        EmbeddedChannel ch = new EmbeddedChannel(codec);

        ByteBuf request = Unpooled.CopiedBuffer(
                "GET / HTTP/1.1\r\nHost: netty.io\r\nContent-Length: 0\r\n\r\n", Encoding.ASCII);
        try
        {
            Assert.True(ch.WriteInbound(request.RetainedDuplicate()));
            IHttpRequest req = ch.ReadInbound<IHttpRequest>();
            ILastHttpContent content = ch.ReadInbound<ILastHttpContent>();
            content.Release();
            Assert.True(ch.WriteInbound(request.RetainedDuplicate()));
            req = ch.ReadInbound<IHttpRequest>();
            content = ch.ReadInbound<ILastHttpContent>();
            content.Release();

            Assert.Throws<DecoderException>(() => ch.WriteInbound(request.RetainedDuplicate()));

            // This one will be just discarded
            Assert.False(ch.WriteInbound(
                request.RetainedDuplicate()));

            // Writing the response will close the channel.
            Assert.True(ch.WriteOutbound(new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK)));
            ReferenceCountUtil.Release(ch.ReadOutbound());

            Assert.False(ch.IsActive);
        }
        finally
        {
            request.Release();
            ch.FinishAndReleaseAll();
        }
    }

    private static ByteBuf PrepareDataChunk(int size)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < size; ++i)
        {
            sb.Append('a');
        }
        return Unpooled.CopiedBuffer(sb.ToString(), Encoding.UTF8);
    }
}
