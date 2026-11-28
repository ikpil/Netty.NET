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
using System.Collections.Generic;
using System.Text;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.Tests.HttpHeadersTestUtils;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests;

#pragma warning disable xUnit1051 // The source's channel.close() observes no test cancellation.

// CLR: IllegalArgumentException/IllegalStateException are ArgumentException/ InvalidOperationException, and
// java.nio.channels.ClosedChannelException is the Transport ClosedChannelException. Boolean.TRUE.toString() is
// "true", the Java text of a boolean header value. HttpUtil.getContentLength returns a long, so the expected int
// lengths are widened. CompositeByteBuf.numComponents() and decompose(...) are NumComponents and Decompose(...) of
// the Buffer port. FullHttpRequest/FullHttpResponse.replace(ByteBuf) narrow the return type in Java; the C#
// interfaces inherit IByteBufHolder.Replace, so the result is cast. channel.close() discards the CloseAsync Task.
public class HttpObjectAggregatorTest
{

    [Fact]
    public void TestAggregate()
    {
        HttpObjectAggregator aggr = new HttpObjectAggregator(1024 * 1024);
        EmbeddedChannel embedder = new EmbeddedChannel(aggr);

        IHttpRequest message = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "http://localhost");
        message.Headers().Set(Of("X-Test"), true);
        IHttpContent chunk1 = new DefaultHttpContent(Unpooled.CopiedBuffer("test", Encoding.ASCII));
        IHttpContent chunk2 = new DefaultHttpContent(Unpooled.CopiedBuffer("test2", Encoding.ASCII));
        IHttpContent chunk3 = new DefaultLastHttpContent(Unpooled.EmptyBuffer);
        Assert.False(embedder.WriteInbound(message));
        Assert.False(embedder.WriteInbound(chunk1));
        Assert.False(embedder.WriteInbound(chunk2));

        // this should trigger a channelRead event so return true
        Assert.True(embedder.WriteInbound(chunk3));
        Assert.True(embedder.Finish());
        IFullHttpRequest aggregatedMessage = embedder.ReadInbound<IFullHttpRequest>();
        Assert.NotNull(aggregatedMessage);

        Assert.Equal((long) (chunk1.Content.ReadableBytes + chunk2.Content.ReadableBytes),
                HttpUtil.GetContentLength(aggregatedMessage));
        Assert.Equal("true", aggregatedMessage.Headers().Get(Of("X-Test")));
        CheckContentBuffer(aggregatedMessage);
        Assert.Null(embedder.ReadInbound());
    }

    private static void CheckContentBuffer(IFullHttpRequest aggregatedMessage)
    {
        CompositeByteBuf buffer = (CompositeByteBuf) aggregatedMessage.Content;
        Assert.Equal(2, buffer.NumComponents);
        IReadOnlyList<ByteBuf> buffers = buffer.Decompose(0, buffer.Capacity);
        Assert.Equal(2, buffers.Count);

        foreach (ByteBuf buf in buffers)
        {
            // This should be false as we decompose the buffer before to not have deep hierarchy
            Assert.False(buf is CompositeByteBuf);
        }

        aggregatedMessage.Release();
    }

    [Fact]
    public void TestAggregateWithTrailer()
    {
        HttpObjectAggregator aggr = new HttpObjectAggregator(1024 * 1024);
        EmbeddedChannel embedder = new EmbeddedChannel(aggr);
        IHttpRequest message = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "http://localhost");
        message.Headers().Set(Of("X-Test"), true);
        HttpUtil.SetTransferEncodingChunked(message, true);
        IHttpContent chunk1 = new DefaultHttpContent(Unpooled.CopiedBuffer("test", Encoding.ASCII));
        IHttpContent chunk2 = new DefaultHttpContent(Unpooled.CopiedBuffer("test2", Encoding.ASCII));
        ILastHttpContent trailer = new DefaultLastHttpContent();
        trailer.TrailingHeaders().Set(Of("X-Trailer"), true);

        Assert.False(embedder.WriteInbound(message));
        Assert.False(embedder.WriteInbound(chunk1));
        Assert.False(embedder.WriteInbound(chunk2));

        // this should trigger a channelRead event so return true
        Assert.True(embedder.WriteInbound(trailer));
        Assert.True(embedder.Finish());
        IFullHttpRequest aggregatedMessage = embedder.ReadInbound<IFullHttpRequest>();
        Assert.NotNull(aggregatedMessage);

        Assert.Equal((long) (chunk1.Content.ReadableBytes + chunk2.Content.ReadableBytes),
                HttpUtil.GetContentLength(aggregatedMessage));
        Assert.Equal("true", aggregatedMessage.Headers().Get(Of("X-Test")));
        Assert.Equal("true", aggregatedMessage.TrailingHeaders().Get(Of("X-Trailer")));
        CheckContentBuffer(aggregatedMessage);
        Assert.Null(embedder.ReadInbound());
    }

    [Fact]
    public void TestOversizedRequest()
    {
        EmbeddedChannel embedder = new EmbeddedChannel(new HttpObjectAggregator(4));
        IHttpRequest message = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.PUT, "http://localhost");
        IHttpContent chunk1 = new DefaultHttpContent(Unpooled.CopiedBuffer("test", Encoding.ASCII));
        IHttpContent chunk2 = new DefaultHttpContent(Unpooled.CopiedBuffer("test2", Encoding.ASCII));
        IHttpContent chunk3 = ILastHttpContent.EMPTY_LAST_CONTENT;

        Assert.False(embedder.WriteInbound(message));
        Assert.False(embedder.WriteInbound(chunk1));
        Assert.False(embedder.WriteInbound(chunk2));

        IFullHttpResponse response = embedder.ReadOutbound<IFullHttpResponse>();
        Assert.Equal(HttpResponseStatus.REQUEST_ENTITY_TOO_LARGE, response.Status());
        Assert.Equal("0", response.Headers().Get(HttpHeaderNames.CONTENT_LENGTH));
        Assert.False(embedder.IsOpen);

        Assert.ThrowsAny<ClosedChannelException>(() => embedder.WriteInbound(chunk3));

        Assert.False(embedder.Finish());
    }

    [Fact]
    public void TestOversizedRequestWithContentLengthAndDecoder()
    {
        EmbeddedChannel embedder = new EmbeddedChannel(new HttpRequestDecoder(), new HttpObjectAggregator(4, false));
        Assert.False(embedder.WriteInbound(Unpooled.CopiedBuffer(
                "PUT /upload HTTP/1.1\r\n" +
                        "Content-Length: 5\r\n\r\n", Encoding.ASCII)));

        Assert.Null(embedder.ReadInbound());

        IFullHttpResponse response = embedder.ReadOutbound<IFullHttpResponse>();
        Assert.Equal(HttpResponseStatus.REQUEST_ENTITY_TOO_LARGE, response.Status());
        Assert.Equal("0", response.Headers().Get(HttpHeaderNames.CONTENT_LENGTH));

        Assert.True(embedder.IsOpen);

        Assert.False(embedder.WriteInbound(Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4 })));
        Assert.False(embedder.WriteInbound(Unpooled.WrappedBuffer(new byte[] { 5 })));

        Assert.Null(embedder.ReadOutbound());

        Assert.False(embedder.WriteInbound(Unpooled.CopiedBuffer(
                "PUT /upload HTTP/1.1\r\n" +
                        "Content-Length: 2\r\n\r\n", Encoding.ASCII)));

        Assert.Equal(HttpResponseStatus.REQUEST_ENTITY_TOO_LARGE, response.Status());
        Assert.Equal("0", response.Headers().Get(HttpHeaderNames.CONTENT_LENGTH));

        Assert.IsAssignableFrom<ILastHttpContent>(response);
        ReferenceCountUtil.Release(response);

        Assert.True(embedder.IsOpen);

        Assert.False(embedder.WriteInbound(Unpooled.CopiedBuffer(new byte[] { 1 })));
        Assert.Null(embedder.ReadOutbound());
        Assert.True(embedder.WriteInbound(Unpooled.CopiedBuffer(new byte[] { 2 })));
        Assert.Null(embedder.ReadOutbound());

        IFullHttpRequest request = embedder.ReadInbound<IFullHttpRequest>();
        Assert.Equal(HttpVersion.HTTP_1_1, request.ProtocolVersion());
        Assert.Equal(HttpMethod.PUT, request.Method());
        Assert.Equal("/upload", request.Uri());
        Assert.Equal(2L, HttpUtil.GetContentLength(request));

        byte[] actual = new byte[request.Content.ReadableBytes];
        request.Content.ReadBytes(actual);
        Assert.Equal(new byte[] { 1, 2 }, actual);
        request.Release();

        Assert.False(embedder.Finish());
    }

    [Fact]
    public void TestOversizedRequestWithoutKeepAlive()
    {
        // send an HTTP/1.0 request with no keep-alive header
        IHttpRequest message = new DefaultHttpRequest(HttpVersion.HTTP_1_0, HttpMethod.PUT, "http://localhost");
        HttpUtil.SetContentLength(message, 5);
        CheckOversizedRequest(message);
    }

    [Fact]
    public void TestOversizedRequestWithContentLength()
    {
        IHttpRequest message = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.PUT, "http://localhost");
        HttpUtil.SetContentLength(message, 5);
        CheckOversizedRequest(message);
    }

    private static void CheckOversizedRequest(IHttpRequest message)
    {
        EmbeddedChannel embedder = new EmbeddedChannel(new HttpObjectAggregator(4));

        Assert.False(embedder.WriteInbound(message));
        IHttpResponse response = embedder.ReadOutbound<IHttpResponse>();
        Assert.Equal(HttpResponseStatus.REQUEST_ENTITY_TOO_LARGE, response.Status());
        Assert.Equal("0", response.Headers().Get(HttpHeaderNames.CONTENT_LENGTH));

        Assert.IsAssignableFrom<ILastHttpContent>(response);
        ReferenceCountUtil.Release(response);

        if (ServerShouldCloseConnection(message, response))
        {
            Assert.False(embedder.IsOpen);

            Assert.ThrowsAny<ClosedChannelException>(
                    () => embedder.WriteInbound(new DefaultHttpContent(Unpooled.EmptyBuffer)));

            Assert.False(embedder.Finish());
        }
        else
        {
            Assert.True(embedder.IsOpen);
            Assert.False(embedder.WriteInbound(new DefaultHttpContent(Unpooled.CopiedBuffer(new byte[8]))));
            Assert.False(embedder.WriteInbound(new DefaultHttpContent(Unpooled.CopiedBuffer(new byte[8]))));

            // Now start a new message and ensure we will not reject it again.
            IHttpRequest message2 = new DefaultHttpRequest(HttpVersion.HTTP_1_0, HttpMethod.PUT, "http://localhost");
            HttpUtil.SetContentLength(message, 2);

            Assert.False(embedder.WriteInbound(message2));
            Assert.Null(embedder.ReadOutbound());
            Assert.False(embedder.WriteInbound(new DefaultHttpContent(Unpooled.CopiedBuffer(new byte[] { 1 }))));
            Assert.Null(embedder.ReadOutbound());
            Assert.True(embedder.WriteInbound(new DefaultLastHttpContent(Unpooled.CopiedBuffer(new byte[] { 2 }))));
            Assert.Null(embedder.ReadOutbound());

            IFullHttpRequest request = embedder.ReadInbound<IFullHttpRequest>();
            Assert.Equal(message2.ProtocolVersion(), request.ProtocolVersion());
            Assert.Equal(message2.Method(), request.Method());
            Assert.Equal(message2.Uri(), request.Uri());
            Assert.Equal(2L, HttpUtil.GetContentLength(request));

            byte[] actual = new byte[request.Content.ReadableBytes];
            request.Content.ReadBytes(actual);
            Assert.Equal(new byte[] { 1, 2 }, actual);
            request.Release();

            Assert.False(embedder.Finish());
        }
    }

    private static bool ServerShouldCloseConnection(IHttpRequest message, IHttpResponse response)
    {
        // If the response wasn't keep-alive, the server should close the connection.
        if (!HttpUtil.IsKeepAlive(response))
        {
            return true;
        }
        // The connection should only be kept open if Expect: 100-continue is set,
        // or if keep-alive is on.
        if (HttpUtil.Is100ContinueExpected(message))
        {
            return false;
        }
        if (HttpUtil.IsKeepAlive(message))
        {
            return false;
        }

        return true;
    }

    [Fact]
    public void TestOversizedResponse()
    {
        EmbeddedChannel embedder = new EmbeddedChannel(new HttpObjectAggregator(4));
        IHttpResponse message = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        IHttpContent chunk1 = new DefaultHttpContent(Unpooled.CopiedBuffer("test", Encoding.ASCII));
        IHttpContent chunk2 = new DefaultHttpContent(Unpooled.CopiedBuffer("test2", Encoding.ASCII));

        Assert.False(embedder.WriteInbound(message));
        Assert.False(embedder.WriteInbound(chunk1));

        Assert.ThrowsAny<TooLongHttpContentException>(() => embedder.WriteInbound(chunk2));

        Assert.False(embedder.IsOpen);
        Assert.False(embedder.Finish());
    }

    [Fact]
    public void TestInvalidConstructorUsage()
    {
        Assert.ThrowsAny<ArgumentException>(() => new HttpObjectAggregator(-1));
    }

    [Fact]
    public void TestInvalidMaxCumulationBufferComponents()
    {
        HttpObjectAggregator aggr = new HttpObjectAggregator(int.MaxValue);
        Assert.ThrowsAny<ArgumentException>(() => aggr.SetMaxCumulationBufferComponents(1));
    }

    [Fact]
    public void TestSetMaxCumulationBufferComponentsAfterInit()
    {
        HttpObjectAggregator aggr = new HttpObjectAggregator(int.MaxValue);
        Mock<IChannelHandlerContext> ctx = new Mock<IChannelHandlerContext>();
        aggr.HandlerAdded(ctx.Object);
        ctx.VerifyNoOtherCalls();
        Assert.ThrowsAny<InvalidOperationException>(() => aggr.SetMaxCumulationBufferComponents(10));
    }

    [Fact]
    public void TestAggregateTransferEncodingChunked()
    {
        HttpObjectAggregator aggr = new HttpObjectAggregator(1024 * 1024);
        EmbeddedChannel embedder = new EmbeddedChannel(aggr);

        IHttpRequest message = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.PUT, "http://localhost");
        message.Headers().Set(Of("X-Test"), true);
        message.Headers().Set(Of("Transfer-Encoding"), Of("Chunked"));
        IHttpContent chunk1 = new DefaultHttpContent(Unpooled.CopiedBuffer("test", Encoding.ASCII));
        IHttpContent chunk2 = new DefaultHttpContent(Unpooled.CopiedBuffer("test2", Encoding.ASCII));
        IHttpContent chunk3 = ILastHttpContent.EMPTY_LAST_CONTENT;
        Assert.False(embedder.WriteInbound(message));
        Assert.False(embedder.WriteInbound(chunk1));
        Assert.False(embedder.WriteInbound(chunk2));

        // this should trigger a channelRead event so return true
        Assert.True(embedder.WriteInbound(chunk3));
        Assert.True(embedder.Finish());
        IFullHttpRequest aggregatedMessage = embedder.ReadInbound<IFullHttpRequest>();
        Assert.NotNull(aggregatedMessage);

        Assert.Equal((long) (chunk1.Content.ReadableBytes + chunk2.Content.ReadableBytes),
                HttpUtil.GetContentLength(aggregatedMessage));
        Assert.Equal("true", aggregatedMessage.Headers().Get(Of("X-Test")));
        CheckContentBuffer(aggregatedMessage);
        Assert.Null(embedder.ReadInbound());
    }

    [Fact]
    public void TestBadRequest()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpRequestDecoder(), new HttpObjectAggregator(1024 * 1024));
        ch.WriteInbound(Unpooled.CopiedBuffer("GET / HTTP/1.0 with extra\r\n", Encoding.UTF8));
        object inbound = ch.ReadInbound();
        Assert.IsAssignableFrom<IFullHttpRequest>(inbound);
        Assert.True(((IDecoderResultProvider) inbound).DecoderResult().IsFailure());
        Assert.Null(ch.ReadInbound());
        ch.Finish();
    }

    [Fact]
    public void TestBadResponse()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder(), new HttpObjectAggregator(1024 * 1024));
        ch.WriteInbound(Unpooled.CopiedBuffer("HTTP/1.0 BAD_CODE Bad Server\r\n", Encoding.UTF8));
        object inbound = ch.ReadInbound();
        Assert.IsAssignableFrom<IFullHttpResponse>(inbound);
        Assert.True(((IDecoderResultProvider) inbound).DecoderResult().IsFailure());
        Assert.Null(ch.ReadInbound());
        ch.Finish();
    }

    [Fact]
    public void TestOversizedRequestWith100Continue()
    {
        EmbeddedChannel embedder = new EmbeddedChannel(new HttpObjectAggregator(8));

        // Send an oversized request with 100 continue.
        IHttpRequest message = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.PUT, "http://localhost");
        HttpUtil.Set100ContinueExpected(message, true);
        HttpUtil.SetContentLength(message, 16);

        IHttpContent chunk1 = new DefaultHttpContent(Unpooled.CopiedBuffer("some", Encoding.ASCII));
        IHttpContent chunk2 = new DefaultHttpContent(Unpooled.CopiedBuffer("test", Encoding.ASCII));
        IHttpContent chunk3 = ILastHttpContent.EMPTY_LAST_CONTENT;

        // Send a request with 100-continue + large Content-Length header value.
        Assert.False(embedder.WriteInbound(message));

        // The aggregator should respond with '413.'
        IFullHttpResponse response = embedder.ReadOutbound<IFullHttpResponse>();
        Assert.Equal(HttpResponseStatus.REQUEST_ENTITY_TOO_LARGE, response.Status());
        Assert.Equal("0", response.Headers().Get(HttpHeaderNames.CONTENT_LENGTH));

        // An ill-behaving client could continue to send data without a respect, and such data should be discarded.
        Assert.False(embedder.WriteInbound(chunk1));

        // The aggregator should not close the connection because keep-alive is on.
        Assert.True(embedder.IsOpen);

        // Now send a valid request.
        IHttpRequest message2 = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.PUT, "http://localhost");

        Assert.False(embedder.WriteInbound(message2));
        Assert.False(embedder.WriteInbound(chunk2));
        Assert.True(embedder.WriteInbound(chunk3));

        IFullHttpRequest fullMsg = embedder.ReadInbound<IFullHttpRequest>();
        Assert.NotNull(fullMsg);

        Assert.Equal(
                (long) (chunk2.Content.ReadableBytes + chunk3.Content.ReadableBytes),
                HttpUtil.GetContentLength(fullMsg));

        Assert.Equal(HttpUtil.GetContentLength(fullMsg), fullMsg.Content.ReadableBytes);

        fullMsg.Release();
        Assert.False(embedder.Finish());
    }

    [Fact]
    public void TestUnsupportedExpectHeaderExpectation()
    {
        RunUnsupportedExceptHeaderExceptionTest(true);
        RunUnsupportedExceptHeaderExceptionTest(false);
    }

    private static void RunUnsupportedExceptHeaderExceptionTest(bool close)
    {
        HttpObjectAggregator aggregator;
        const int maxContentLength = 4;

        if (close)
        {
            aggregator = new HttpObjectAggregator(maxContentLength, true);
        }
        else
        {
            aggregator = new HttpObjectAggregator(maxContentLength);
        }

        EmbeddedChannel embedder = new EmbeddedChannel(new HttpRequestDecoder(), aggregator);

        Assert.False(embedder.WriteInbound(Unpooled.CopiedBuffer(
                "GET / HTTP/1.1\r\n" +
                        "Expect: chocolate=yummy\r\n" +
                        "Content-Length: 100\r\n\r\n", Encoding.ASCII)));
        Assert.Null(embedder.ReadInbound());

        IFullHttpResponse response = embedder.ReadOutbound<IFullHttpResponse>();
        Assert.Equal(HttpResponseStatus.EXPECTATION_FAILED, response.Status());
        Assert.Equal("0", response.Headers().Get(HttpHeaderNames.CONTENT_LENGTH));
        response.Release();

        if (close)
        {
            Assert.False(embedder.IsOpen);
        }
        else
        {
            // keep-alive is on by default in HTTP/1.1, so the connection should be still alive
            Assert.True(embedder.IsOpen);

            // the decoder should be reset by the aggregator at this point and be able to decode the next request
            Assert.True(embedder.WriteInbound(Unpooled.CopiedBuffer("GET / HTTP/1.1\r\n\r\n", Encoding.ASCII)));

            IFullHttpRequest request = embedder.ReadInbound<IFullHttpRequest>();
            Assert.Equal(HttpMethod.GET, request.Method());
            Assert.Equal("/", request.Uri());
            Assert.Equal(0, request.Content.ReadableBytes);
            request.Release();
        }

        Assert.False(embedder.Finish());
    }

    [Fact]
    public void TestValidRequestWith100ContinueAndDecoder()
    {
        EmbeddedChannel embedder = new EmbeddedChannel(new HttpRequestDecoder(), new HttpObjectAggregator(100));
        embedder.WriteInbound(Unpooled.CopiedBuffer(
            "GET /upload HTTP/1.1\r\n" +
                "Expect: 100-continue\r\n" +
                "Content-Length: 0\r\n\r\n", Encoding.ASCII));

        IFullHttpResponse response = embedder.ReadOutbound<IFullHttpResponse>();
        Assert.Equal(HttpResponseStatus.CONTINUE, response.Status());
        IFullHttpRequest request = embedder.ReadInbound<IFullHttpRequest>();
        Assert.False(request.Headers().Contains(HttpHeaderNames.EXPECT));
        request.Release();
        response.Release();
        Assert.False(embedder.Finish());
    }

    [Fact]
    public void TestOversizedRequestWith100ContinueAndDecoder()
    {
        EmbeddedChannel embedder = new EmbeddedChannel(new HttpRequestDecoder(), new HttpObjectAggregator(4));
        embedder.WriteInbound(Unpooled.CopiedBuffer(
                "PUT /upload HTTP/1.1\r\n" +
                        "Expect: 100-continue\r\n" +
                        "Content-Length: 100\r\n\r\n", Encoding.ASCII));

        Assert.Null(embedder.ReadInbound());

        IFullHttpResponse response = embedder.ReadOutbound<IFullHttpResponse>();
        Assert.Equal(HttpResponseStatus.REQUEST_ENTITY_TOO_LARGE, response.Status());
        Assert.Equal("0", response.Headers().Get(HttpHeaderNames.CONTENT_LENGTH));

        // Keep-alive is on by default in HTTP/1.1, so the connection should be still alive.
        Assert.True(embedder.IsOpen);

        // The decoder should be reset by the aggregator at this point and be able to decode the next request.
        embedder.WriteInbound(Unpooled.CopiedBuffer("GET /max-upload-size HTTP/1.1\r\n\r\n", Encoding.ASCII));

        IFullHttpRequest request = embedder.ReadInbound<IFullHttpRequest>();
        Assert.Equal(HttpMethod.GET, request.Method());
        Assert.Equal("/max-upload-size", request.Uri());
        Assert.Equal(0, request.Content.ReadableBytes);
        request.Release();

        Assert.False(embedder.Finish());
    }

    [Fact]
    public void TestOversizedRequestWith100ContinueAndDecoderCloseConnection()
    {
        EmbeddedChannel embedder = new EmbeddedChannel(new HttpRequestDecoder(), new HttpObjectAggregator(4, true));
        embedder.WriteInbound(Unpooled.CopiedBuffer(
                "PUT /upload HTTP/1.1\r\n" +
                        "Expect: 100-continue\r\n" +
                        "Content-Length: 100\r\n\r\n", Encoding.ASCII));

        Assert.Null(embedder.ReadInbound());

        IFullHttpResponse response = embedder.ReadOutbound<IFullHttpResponse>();
        Assert.Equal(HttpResponseStatus.REQUEST_ENTITY_TOO_LARGE, response.Status());
        Assert.Equal("0", response.Headers().Get(HttpHeaderNames.CONTENT_LENGTH));

        // We are forcing the connection closed if an expectation is exceeded.
        Assert.False(embedder.IsOpen);
        Assert.False(embedder.Finish());
    }

    [Fact]
    public void TestRequestAfterOversized100ContinueAndDecoder()
    {
        EmbeddedChannel embedder = new EmbeddedChannel(new HttpRequestDecoder(), new HttpObjectAggregator(15));

        // Write first request with Expect: 100-continue.
        IHttpRequest message = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.PUT, "http://localhost");
        HttpUtil.Set100ContinueExpected(message, true);
        HttpUtil.SetContentLength(message, 16);

        IHttpContent chunk1 = new DefaultHttpContent(Unpooled.CopiedBuffer("some", Encoding.ASCII));
        IHttpContent chunk2 = new DefaultHttpContent(Unpooled.CopiedBuffer("test", Encoding.ASCII));
        IHttpContent chunk3 = ILastHttpContent.EMPTY_LAST_CONTENT;

        // Send a request with 100-continue + large Content-Length header value.
        Assert.False(embedder.WriteInbound(message));

        // The aggregator should respond with '413'.
        IFullHttpResponse response = embedder.ReadOutbound<IFullHttpResponse>();
        Assert.Equal(HttpResponseStatus.REQUEST_ENTITY_TOO_LARGE, response.Status());
        Assert.Equal("0", response.Headers().Get(HttpHeaderNames.CONTENT_LENGTH));

        // An ill-behaving client could continue to send data without a respect, and such data should be discarded.
        Assert.False(embedder.WriteInbound(chunk1));

        // The aggregator should not close the connection because keep-alive is on.
        Assert.True(embedder.IsOpen);

        // Now send a valid request.
        IHttpRequest message2 = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.PUT, "http://localhost");

        Assert.False(embedder.WriteInbound(message2));
        Assert.False(embedder.WriteInbound(chunk2));
        Assert.True(embedder.WriteInbound(chunk3));

        IFullHttpRequest fullMsg = embedder.ReadInbound<IFullHttpRequest>();
        Assert.NotNull(fullMsg);

        Assert.Equal(
                (long) (chunk2.Content.ReadableBytes + chunk3.Content.ReadableBytes),
                HttpUtil.GetContentLength(fullMsg));

        Assert.Equal(HttpUtil.GetContentLength(fullMsg), fullMsg.Content.ReadableBytes);

        fullMsg.Release();
        Assert.False(embedder.Finish());
    }

    [Fact]
    public void TestReplaceAggregatedRequest()
    {
        EmbeddedChannel embedder = new EmbeddedChannel(new HttpObjectAggregator(1024 * 1024));

        Exception boom = new Exception("boom");
        IHttpRequest req = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "http://localhost");
        req.SetDecoderResult(DecoderResult.Failure(boom));

        Assert.True(embedder.WriteInbound(req) && embedder.Finish());

        IFullHttpRequest aggregatedReq = embedder.ReadInbound<IFullHttpRequest>();
        IFullHttpRequest replacedReq = (IFullHttpRequest) aggregatedReq.Replace(Unpooled.EmptyBuffer);

        Assert.Equal(replacedReq.DecoderResult(), aggregatedReq.DecoderResult());
        aggregatedReq.Release();
        replacedReq.Release();
    }

    [Fact]
    public void TestReplaceAggregatedResponse()
    {
        EmbeddedChannel embedder = new EmbeddedChannel(new HttpObjectAggregator(1024 * 1024));

        Exception boom = new Exception("boom");
        IHttpResponse rep = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        rep.SetDecoderResult(DecoderResult.Failure(boom));

        Assert.True(embedder.WriteInbound(rep) && embedder.Finish());

        IFullHttpResponse aggregatedRep = embedder.ReadInbound<IFullHttpResponse>();
        IFullHttpResponse replacedRep = (IFullHttpResponse) aggregatedRep.Replace(Unpooled.EmptyBuffer);

        Assert.Equal(replacedRep.DecoderResult(), aggregatedRep.DecoderResult());
        aggregatedRep.Release();
        replacedRep.Release();
    }

    private sealed class PostAggregator : HttpObjectAggregator
    {
        internal PostAggregator(int maxContentLength) : base(maxContentLength)
        {
        }

        protected override bool IsStartMessage(IHttpObject msg)
        {
            if (msg is IHttpRequest)
            {
                IHttpRequest request = (IHttpRequest) msg;
                HttpMethod method = request.Method();

                if (method.Equals(HttpMethod.POST))
                {
                    return true;
                }
            }

            return false;
        }
    }

    [Fact]
    public void TestSelectiveRequestAggregation()
    {
        HttpObjectAggregator myPostAggregator = new PostAggregator(1024 * 1024);

        EmbeddedChannel channel = new EmbeddedChannel(myPostAggregator);

        try
        {
            // Aggregate: POST
            IHttpRequest request1 = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/");
            IHttpContent content1 = new DefaultHttpContent(Unpooled.CopiedBuffer("Hello, World!", Encoding.UTF8));
            request1.Headers().Set(HttpHeaderNames.CONTENT_TYPE, HttpHeaderValues.TEXT_PLAIN);

            Assert.True(channel.WriteInbound(request1, content1, ILastHttpContent.EMPTY_LAST_CONTENT));

            // Getting an aggregated response out
            object msg1 = channel.ReadInbound();

            try
            {
                Assert.IsAssignableFrom<IFullHttpRequest>(msg1);
            }
            finally
            {
                ReferenceCountUtil.Release(msg1);
            }

            // Don't aggregate: non-POST
            IHttpRequest request2 = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.PUT, "/");
            IHttpContent content2 = new DefaultHttpContent(Unpooled.CopiedBuffer("Hello, World!", Encoding.UTF8));
            request2.Headers().Set(HttpHeaderNames.CONTENT_TYPE, HttpHeaderValues.TEXT_PLAIN);

            try
            {
                Assert.True(channel.WriteInbound(request2, content2, ILastHttpContent.EMPTY_LAST_CONTENT));

                // Getting the same response objects out
                Assert.Same(request2, channel.ReadInbound());
                Assert.Same(content2, channel.ReadInbound());
                Assert.Same(ILastHttpContent.EMPTY_LAST_CONTENT, channel.ReadInbound());
            }
            finally
            {
              ReferenceCountUtil.Release(request2);
              ReferenceCountUtil.Release(content2);
            }

            Assert.False(channel.Finish());
        }
        finally
        {
          _ = channel.CloseAsync();
        }
    }

    private sealed class TextAggregator : HttpObjectAggregator
    {
        internal TextAggregator(int maxContentLength) : base(maxContentLength)
        {
        }

        protected override bool IsStartMessage(IHttpObject msg)
        {
            if (msg is IHttpResponse)
            {
                IHttpResponse response = (IHttpResponse) msg;
                HttpHeaders headers = response.Headers();

                string contentType = headers.Get(HttpHeaderNames.CONTENT_TYPE);
                if (AsciiString.ContentEqualsIgnoreCase(
                        contentType == null ? null : new StringCharSequence(contentType), HttpHeaderValues.TEXT_PLAIN))
                {
                    return true;
                }
            }

            return false;
        }
    }

    [Fact]
    public void TestSelectiveResponseAggregation()
    {
        HttpObjectAggregator myTextAggregator = new TextAggregator(1024 * 1024);

        EmbeddedChannel channel = new EmbeddedChannel(myTextAggregator);

        try
        {
            // Aggregate: text/plain
            IHttpResponse response1 = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
            IHttpContent content1 = new DefaultHttpContent(Unpooled.CopiedBuffer("Hello, World!", Encoding.UTF8));
            response1.Headers().Set(HttpHeaderNames.CONTENT_TYPE, HttpHeaderValues.TEXT_PLAIN);

            Assert.True(channel.WriteInbound(response1, content1, ILastHttpContent.EMPTY_LAST_CONTENT));

            // Getting an aggregated response out
            object msg1 = channel.ReadInbound();

            try
            {
                Assert.IsAssignableFrom<IFullHttpResponse>(msg1);
            }
            finally
            {
                ReferenceCountUtil.Release(msg1);
            }

            // Don't aggregate: application/json
            IHttpResponse response2 = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
            IHttpContent content2 = new DefaultHttpContent(Unpooled.CopiedBuffer("{key: 'value'}", Encoding.UTF8));
            response2.Headers().Set(HttpHeaderNames.CONTENT_TYPE, HttpHeaderValues.APPLICATION_JSON);

            try
            {
                Assert.True(channel.WriteInbound(response2, content2, ILastHttpContent.EMPTY_LAST_CONTENT));

                // Getting the same response objects out
                Assert.Same(response2, channel.ReadInbound());
                Assert.Same(content2, channel.ReadInbound());
                Assert.Same(ILastHttpContent.EMPTY_LAST_CONTENT, channel.ReadInbound());
            }
            finally
            {
                ReferenceCountUtil.Release(response2);
                ReferenceCountUtil.Release(content2);
            }

            Assert.False(channel.Finish());
        }
        finally
        {
          _ = channel.CloseAsync();
        }
    }

    [Fact]
    public void TestPrematureClosureWithChunkedEncodingAndAggregator()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpResponseDecoder(), new HttpObjectAggregator(1024));

        // Write the partial response.
        Assert.False(ch.WriteInbound(Unpooled.CopiedBuffer(
                "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n8\r\n12345678", Encoding.ASCII)));
        Assert.ThrowsAny<PrematureChannelClosureException>(() => ch.Finish());
    }

    [Fact]
    public void InvalidContinueLength()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpServerCodec(), new HttpObjectAggregator(1024));

        channel.WriteInbound(Unpooled.CopiedBuffer("POST / HTTP/1.1\r\n" +
                "Expect: 100-continue\r\n" +
                "Content-Length:\r\n" +
                "\r\n\r\n", Encoding.ASCII));
        Assert.True(channel.FinishAndReleaseAll());
    }

    [Fact]
    public void TestOversizedRequestWithAutoReadFalse()
    {
        EmbeddedChannel embedder = new EmbeddedChannel(new HttpRequestDecoder(), new HttpObjectAggregator(4));
        embedder.Config.SetAutoRead(false);
        Assert.False(embedder.WriteInbound(Unpooled.CopiedBuffer(
                "PUT /upload HTTP/1.1\r\n"
                + "Content-Length: 5\r\n\r\n", Encoding.ASCII)));

        Assert.Null(embedder.ReadInbound());

        IFullHttpResponse response = embedder.ReadOutbound<IFullHttpResponse>();
        Assert.Equal(HttpResponseStatus.REQUEST_ENTITY_TOO_LARGE, response.Status());
        Assert.Equal("0", response.Headers().Get(HttpHeaderNames.CONTENT_LENGTH));
        ReferenceCountUtil.Release(response);

        Assert.False(embedder.IsOpen);
        Assert.False(embedder.Finish());
    }
}
