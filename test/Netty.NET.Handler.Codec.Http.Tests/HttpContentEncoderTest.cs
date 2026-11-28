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
using System.Globalization;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.Tests.HttpHeadersTestUtils;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests;

// CLR: the anonymous MessageToByteEncoder<ByteBuf>, HttpContentEncoder and ChannelInboundHandlerAdapter classes
// are the named nested classes LengthEncoder, CleanupThrowsEncoder, InactiveThrowsHandler and
// InactiveCountingHandler. assertInstanceOf(X.class, o) is Assert.IsAssignableFrom<IX>(o) and AssertJ's
// isNotInstanceOf is Assert.False(o is IX); assertThrows(CodecException.class,...) accepts subclasses, so it is
// Assert.ThrowsAny<CodecException> (the EncoderException of the inner channel), while the other assertThrows keep
// the exact types. EmbeddedChannel's readOutbound()/readInbound() are ReadOutbound<T>()/ReadInbound<T>(), refCnt()
// is RefCnt and isOpen() is IsOpen. String.valueOf(int).getBytes(US_ASCII) is Encoding.ASCII.GetBytes of the
// invariant decimal text; CharsetUtil.US_ASCII/UTF_8 are Encoding.ASCII/UTF8. UnsupportedOperationException is
// NotSupportedException and AtomicBoolean is the Common AtomicBoolean.
public class HttpContentEncoderTest
{

    private sealed class TestEncoder : HttpContentEncoder
    {

        private EmbeddedChannel contentEncoder;

        internal TestEncoder()
        {
        }

        internal TestEncoder(int maxPipelineDepth) : base(maxPipelineDepth)
        {
        }

        protected override Result BeginEncode(IHttpResponse httpResponse, string acceptEncoding)
        {
            contentEncoder = new EmbeddedChannel(new LengthEncoder());
            return new Result("test", contentEncoder);
        }

        internal EmbeddedChannel ContentEncoder()
        {
            return contentEncoder;
        }
    }

    private sealed class LengthEncoder : MessageToByteEncoder<ByteBuf>
    {
        protected override void Encode(IChannelHandlerContext ctx, ByteBuf @in, ByteBuf @out)
        {
            @out.WriteBytes(Encoding.ASCII.GetBytes(@in.ReadableBytes.ToString(CultureInfo.InvariantCulture)));
            @in.SkipBytes(@in.ReadableBytes);
        }
    }

    [Fact]
    public void TestSplitContent()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new TestEncoder());
        ch.WriteInbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/"));

        ch.WriteOutbound(new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK));
        ch.WriteOutbound(new DefaultHttpContent(Unpooled.WrappedBuffer(new byte[3])));
        ch.WriteOutbound(new DefaultHttpContent(Unpooled.WrappedBuffer(new byte[2])));
        ch.WriteOutbound(new DefaultLastHttpContent(Unpooled.WrappedBuffer(new byte[1])));

        AssertEncodedResponse(ch);

        IHttpContent chunk;
        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("3", chunk.Content.ToString(Encoding.ASCII));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("2", chunk.Content.ToString(Encoding.ASCII));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("1", chunk.Content.ToString(Encoding.ASCII));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.False(chunk.Content.IsReadable());
        Assert.IsAssignableFrom<ILastHttpContent>(chunk);
        chunk.Release();

        Assert.Null(ch.ReadOutbound());
    }

    [Fact]
    public void TestChunkedContent()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new TestEncoder());
        ch.WriteInbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/"));

        IHttpResponse res = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        res.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);
        ch.WriteOutbound(res);

        AssertEncodedResponse(ch);

        ch.WriteOutbound(new DefaultHttpContent(Unpooled.WrappedBuffer(new byte[3])));
        ch.WriteOutbound(new DefaultHttpContent(Unpooled.WrappedBuffer(new byte[2])));
        ch.WriteOutbound(new DefaultLastHttpContent(Unpooled.WrappedBuffer(new byte[1])));

        IHttpContent chunk;
        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("3", chunk.Content.ToString(Encoding.ASCII));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("2", chunk.Content.ToString(Encoding.ASCII));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("1", chunk.Content.ToString(Encoding.ASCII));
        Assert.IsAssignableFrom<IHttpContent>(chunk);
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.False(chunk.Content.IsReadable());
        Assert.IsAssignableFrom<ILastHttpContent>(chunk);
        chunk.Release();

        Assert.Null(ch.ReadOutbound());
    }

    [Fact]
    public void TestChunkedContentWithTrailingHeader()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new TestEncoder());
        ch.WriteInbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/"));

        IHttpResponse res = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        res.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);
        ch.WriteOutbound(res);

        AssertEncodedResponse(ch);

        ch.WriteOutbound(new DefaultHttpContent(Unpooled.WrappedBuffer(new byte[3])));
        ch.WriteOutbound(new DefaultHttpContent(Unpooled.WrappedBuffer(new byte[2])));
        ILastHttpContent content = new DefaultLastHttpContent(Unpooled.WrappedBuffer(new byte[1]));
        content.TrailingHeaders().Set(Of("X-Test"), Of("Netty"));
        ch.WriteOutbound(content);

        IHttpContent chunk;
        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("3", chunk.Content.ToString(Encoding.ASCII));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("2", chunk.Content.ToString(Encoding.ASCII));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("1", chunk.Content.ToString(Encoding.ASCII));
        Assert.IsAssignableFrom<IHttpContent>(chunk);
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.False(chunk.Content.IsReadable());
        Assert.IsAssignableFrom<ILastHttpContent>(chunk);
        Assert.Equal("Netty", ((ILastHttpContent) chunk).TrailingHeaders().Get(Of("X-Test")));
        Assert.Equal(DecoderResult.SUCCESS, res.DecoderResult());
        chunk.Release();

        Assert.Null(ch.ReadOutbound());
    }

    [Fact]
    public void TestFullContentWithContentLength()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new TestEncoder());
        ch.WriteInbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/"));

        IFullHttpResponse fullRes = new DefaultFullHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.OK, Unpooled.WrappedBuffer(new byte[42]));
        fullRes.Headers().Set(HttpHeaderNames.CONTENT_LENGTH, 42);
        ch.WriteOutbound(fullRes);

        IHttpResponse res = ch.ReadOutbound<IHttpResponse>();
        Assert.False(res is IHttpContent);
        Assert.Null(res.Headers().Get(HttpHeaderNames.TRANSFER_ENCODING));
        Assert.Equal("2", res.Headers().Get(HttpHeaderNames.CONTENT_LENGTH));
        Assert.Equal("test", res.Headers().Get(HttpHeaderNames.CONTENT_ENCODING));

        IHttpContent c = ch.ReadOutbound<IHttpContent>();
        Assert.Equal(2, c.Content.ReadableBytes);
        Assert.Equal("42", c.Content.ToString(Encoding.ASCII));
        c.Release();

        ILastHttpContent last = ch.ReadOutbound<ILastHttpContent>();
        Assert.Equal(0, last.Content.ReadableBytes);
        last.Release();

        Assert.Null(ch.ReadOutbound());
    }

    [Fact]
    public void TestFullContent()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new TestEncoder());
        ch.WriteInbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/"));

        IFullHttpResponse res = new DefaultFullHttpResponse(
            HttpVersion.HTTP_1_1, HttpResponseStatus.OK, Unpooled.WrappedBuffer(new byte[42]));
        ch.WriteOutbound(res);

        AssertEncodedResponse(ch);
        IHttpContent c = ch.ReadOutbound<IHttpContent>();
        Assert.Equal(2, c.Content.ReadableBytes);
        Assert.Equal("42", c.Content.ToString(Encoding.ASCII));
        c.Release();

        ILastHttpContent last = ch.ReadOutbound<ILastHttpContent>();
        Assert.Equal(0, last.Content.ReadableBytes);
        last.Release();

        Assert.Null(ch.ReadOutbound());
    }

    /**
     * If the length of the content is unknown, {@link HttpContentEncoder} should not skip encoding the content
     * even if the actual length is turned out to be 0.
     */
    [Fact]
    public void TestEmptySplitContent()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new TestEncoder());
        ch.WriteInbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/"));

        ch.WriteOutbound(new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK));
        AssertEncodedResponse(ch);

        ch.WriteOutbound(ILastHttpContent.EMPTY_LAST_CONTENT);
        IHttpContent chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("0", chunk.Content.ToString(Encoding.ASCII));
        Assert.IsAssignableFrom<IHttpContent>(chunk);
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.False(chunk.Content.IsReadable());
        Assert.IsAssignableFrom<ILastHttpContent>(chunk);
        chunk.Release();

        Assert.Null(ch.ReadOutbound());
    }

    /**
     * If the length of the content is 0 for sure, {@link HttpContentEncoder} should skip encoding.
     */
    [Fact]
    public void TestEmptyFullContent()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new TestEncoder());
        ch.WriteInbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/"));

        IFullHttpResponse res = new DefaultFullHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.OK, Unpooled.EmptyBuffer);
        ch.WriteOutbound(res);

        object o = ch.ReadOutbound();
        Assert.IsAssignableFrom<IFullHttpResponse>(o);

        res = (IFullHttpResponse) o;
        Assert.Null(res.Headers().Get(HttpHeaderNames.TRANSFER_ENCODING));

        // Content encoding shouldn't be modified.
        Assert.Null(res.Headers().Get(HttpHeaderNames.CONTENT_ENCODING));
        Assert.Equal(0, res.Content.ReadableBytes);
        Assert.Equal("", res.Content.ToString(Encoding.ASCII));
        res.Release();

        Assert.Null(ch.ReadOutbound());
    }

    [Fact]
    public void TestEmptyFullContentWithTrailer()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new TestEncoder());
        ch.WriteInbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/"));

        IFullHttpResponse res = new DefaultFullHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.OK, Unpooled.EmptyBuffer);
        res.TrailingHeaders().Set(Of("X-Test"), Of("Netty"));
        ch.WriteOutbound(res);

        object o = ch.ReadOutbound();
        Assert.IsAssignableFrom<IFullHttpResponse>(o);

        res = (IFullHttpResponse) o;
        Assert.Null(res.Headers().Get(HttpHeaderNames.TRANSFER_ENCODING));

        // Content encoding shouldn't be modified.
        Assert.Null(res.Headers().Get(HttpHeaderNames.CONTENT_ENCODING));
        Assert.Equal(0, res.Content.ReadableBytes);
        Assert.Equal("", res.Content.ToString(Encoding.ASCII));
        Assert.Equal("Netty", res.TrailingHeaders().Get(Of("X-Test")));
        Assert.Equal(DecoderResult.SUCCESS, res.DecoderResult());
        Assert.Null(ch.ReadOutbound());
    }

    [Fact]
    public void TestEmptyHeadResponse()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new TestEncoder());
        IHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.HEAD, "/");
        ch.WriteInbound(req);

        IHttpResponse res = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        res.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);
        ch.WriteOutbound(res);
        ch.WriteOutbound(ILastHttpContent.EMPTY_LAST_CONTENT);

        AssertEmptyResponse(ch);
    }

    [Fact]
    public void TestHttp304Response()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new TestEncoder());
        IHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/");
        req.Headers().Set(HttpHeaderNames.ACCEPT_ENCODING, HttpHeaderValues.GZIP);
        ch.WriteInbound(req);

        IHttpResponse res = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.NOT_MODIFIED);
        res.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);
        ch.WriteOutbound(res);
        ch.WriteOutbound(ILastHttpContent.EMPTY_LAST_CONTENT);

        AssertEmptyResponse(ch);
    }

    [Fact]
    public void TestConnect200Response()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new TestEncoder());
        IHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.CONNECT, "google.com:80");
        ch.WriteInbound(req);

        IHttpResponse res = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        res.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);
        ch.WriteOutbound(res);
        ch.WriteOutbound(ILastHttpContent.EMPTY_LAST_CONTENT);

        AssertEmptyResponse(ch);
    }

    [Fact]
    public void TestConnectFailureResponse()
    {
        string content = "Not allowed by configuration";

        EmbeddedChannel ch = new EmbeddedChannel(new TestEncoder());
        IHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.CONNECT, "google.com:80");
        ch.WriteInbound(req);

        IHttpResponse res = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.METHOD_NOT_ALLOWED);
        res.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);
        ch.WriteOutbound(res);
        ch.WriteOutbound(new DefaultHttpContent(Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(content))));
        ch.WriteOutbound(ILastHttpContent.EMPTY_LAST_CONTENT);

        AssertEncodedResponse(ch);
        object o = ch.ReadOutbound();
        Assert.IsAssignableFrom<IHttpContent>(o);
        IHttpContent chunk = (IHttpContent) o;
        Assert.Equal("28", chunk.Content.ToString(Encoding.ASCII));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.True(chunk.Content.IsReadable());
        Assert.Equal("0", chunk.Content.ToString(Encoding.ASCII));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.IsAssignableFrom<ILastHttpContent>(chunk);
        chunk.Release();
        Assert.Null(ch.ReadOutbound());
    }

    [Fact]
    public void TestHttp1_0()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new TestEncoder());
        IFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_0, HttpMethod.GET, "/");
        Assert.True(ch.WriteInbound(req));

        IHttpResponse res = new DefaultHttpResponse(HttpVersion.HTTP_1_0, HttpResponseStatus.OK);
        res.Headers().Set(HttpHeaderNames.CONTENT_LENGTH, HttpHeaderValues.ZERO);
        Assert.True(ch.WriteOutbound(res));
        Assert.True(ch.WriteOutbound(ILastHttpContent.EMPTY_LAST_CONTENT));
        Assert.True(ch.Finish());

        IFullHttpRequest request = ch.ReadInbound<IFullHttpRequest>();
        Assert.True(request.Release());
        Assert.Null(ch.ReadInbound());

        IHttpResponse response = ch.ReadOutbound<IHttpResponse>();
        Assert.Same(res, response);

        ILastHttpContent content = ch.ReadOutbound<ILastHttpContent>();
        Assert.Same(ILastHttpContent.EMPTY_LAST_CONTENT, content);
        content.Release();
        Assert.Null(ch.ReadOutbound());
    }

    private sealed class CleanupThrowsEncoder : HttpContentEncoder
    {
        protected override Result BeginEncode(IHttpResponse httpResponse, string acceptEncoding)
        {
            return new Result("myencoding", new EmbeddedChannel(
                    new InactiveThrowsHandler()));
        }
    }

    private sealed class InactiveThrowsHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            ctx.FireExceptionCaught(new EncoderException());
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
        HttpContentEncoder encoder = new CleanupThrowsEncoder();

        AtomicBoolean channelInactiveCalled = new AtomicBoolean();
        EmbeddedChannel channel = new EmbeddedChannel(encoder, new InactiveCountingHandler(channelInactiveCalled));
        Assert.True(channel.WriteInbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/")));
        Assert.True(channel.WriteOutbound(new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK)));
        IHttpContent content = new DefaultHttpContent(Unpooled.Buffer().WriteZero(10));
        Assert.True(channel.WriteOutbound(content));
        Assert.Equal(1, content.RefCnt);
        Assert.ThrowsAny<CodecException>(() => channel.FinishAndReleaseAll());

        Assert.True(channelInactiveCalled.Get());
        Assert.Equal(0, content.RefCnt);
    }

    [Fact]
    public void TestPipelineDepthLimited()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new TestEncoder(2));
        ch.WriteInbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/"));
        ch.WriteInbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/"));

        Assert.Throws<DecoderException>(() => ch.WriteInbound(
                new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/")));

        ch.FinishAndReleaseAll();
    }

    [Fact]
    public void TestHeaderMutationFailureReleasesContentEncoder()
    {
        TestEncoder encoder = new TestEncoder();
        EmbeddedChannel channel = new EmbeddedChannel(encoder);

        try
        {
            Assert.True(channel.WriteInbound(new DefaultFullHttpRequest(
                    HttpVersion.HTTP_1_1, HttpMethod.GET, "/first")));

            EncoderException exception = Assert.Throws<EncoderException>(() => channel.WriteOutbound(
                    new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
                            new ReadOnlyHttpHeaders(false))));
            Assert.IsAssignableFrom<NotSupportedException>(exception.InnerException);
            Assert.False(encoder.ContentEncoder().IsOpen);

            Assert.True(channel.WriteInbound(new DefaultFullHttpRequest(
                    HttpVersion.HTTP_1_1, HttpMethod.GET, "/second")));
            Assert.True(channel.WriteOutbound(new DefaultFullHttpResponse(
                    HttpVersion.HTTP_1_1, HttpResponseStatus.OK, Unpooled.WrappedBuffer(new byte[1]))));
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    private static void AssertEmptyResponse(EmbeddedChannel ch)
    {
        object o = ch.ReadOutbound();
        Assert.IsAssignableFrom<IHttpResponse>(o);

        IHttpResponse res = (IHttpResponse) o;
        Assert.False(res is IHttpContent);
        Assert.Equal("chunked", res.Headers().Get(HttpHeaderNames.TRANSFER_ENCODING));
        Assert.Null(res.Headers().Get(HttpHeaderNames.CONTENT_LENGTH));

        IHttpContent chunk = ch.ReadOutbound<IHttpContent>();
        Assert.IsAssignableFrom<ILastHttpContent>(chunk);
        chunk.Release();
        Assert.Null(ch.ReadOutbound());
    }

    private static void AssertEncodedResponse(EmbeddedChannel ch)
    {
        object o = ch.ReadOutbound();
        Assert.IsAssignableFrom<IHttpResponse>(o);

        IHttpResponse res = (IHttpResponse) o;
        Assert.False(res is IHttpContent);
        Assert.Equal("chunked", res.Headers().Get(HttpHeaderNames.TRANSFER_ENCODING));
        Assert.Null(res.Headers().Get(HttpHeaderNames.CONTENT_LENGTH));
        Assert.Equal("test", res.Headers().Get(HttpHeaderNames.CONTENT_ENCODING));
    }
}
