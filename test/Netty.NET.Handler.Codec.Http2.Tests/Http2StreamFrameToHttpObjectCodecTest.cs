/*
 * Copyright 2017 The Netty Project
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

using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http2.Tests.Http2TestUtil;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: readOutbound()/readInbound() are ReadOutbound<T>()/ReadInbound<T>() and content() is the Content property; the
// codec-http interfaces take the I prefix and their getters are methods (docs/codec-http-porting.md, Shared CLR
// adaptations). A Java String passed as a CharSequence (Http2Headers.get/path/method/authority/status/setInt/add) is a
// StringCharSequence (Http2TestUtil.Of), because a C# string is not an ICharSequence (docs/codec-http-porting.md, Shared
// CLR adaptations). CharsetUtil.UTF_8 is Encoding.UTF8. The @Deprecated DefaultLastHttpContent(ByteBuf, boolean) is
// not ported in codec-http (its @deprecated text names DefaultLastHttpContent(ByteBuf), "to always have header
// validation enabled"; src/Netty.NET.Handler.Codec.Http/DefaultLastHttpContent.cs); every call here passes true, so it
// is that constructor. assertThrows(T.class, executable) is Assert.ThrowsAny<T> with a lambda, AssertJ's
// assertThat(x).isNotInstanceOf(T.class) is Assert.IsNotAssignableFrom<T> (as test/Netty.NET.Handler.Codec.Http.Tests/
// HttpClientCodecTest.cs), and the @ValueSource(strings) test is a [Theory] with InlineData rows. The private helper
// testDecodeFullResponseHeaders(boolean), which overloads a test name, is TestDecodeFullResponseHeaders0 (xUnit1024;
// docs/codec-http-porting.md, Shared CLR adaptations). The anonymous Http2FrameStream and ChannelOutboundHandlerAdapter
// classes are named nested classes; their write(ctx, msg, promise) is WriteAsync(ctx, msg, cancellationToken), where
// ctx.write(msg, promise) returns ctx.WriteAsync(msg, cancellationToken) and promise.setSuccess() returns a completed
// Task (docs/codec-base-porting.md, Outbound operations). ConcurrentLinkedQueue is ConcurrentQueue
// (docs/codec-http-porting.md, Shared CLR adaptations) and poll() is TryDequeue (Poll). SslContextBuilder.forClient()
// .sslProvider(SslProvider.JDK).build().newHandler(ByteBufAllocator.DEFAULT) is the handler port's SslContextBuilder and
// SslHandler on SslStream (docs/handler-porting.md, SSL) with IByteBufAllocator.Default.
public class Http2StreamFrameToHttpObjectCodecTest
{

    [Fact]
    public void TestUpgradeEmptyFullResponse()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(true));
        Assert.True(ch.WriteOutbound(new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK)));

        IHttp2HeadersFrame headersFrame = ch.ReadOutbound<IHttp2HeadersFrame>();
        Assert.Equal("200", headersFrame.Headers().Status().ToString());
        Assert.True(headersFrame.IsEndStream());

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void Encode100ContinueAsHttp2HeadersFrameThatIsNotEndStream()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(true));
        Assert.True(ch.WriteOutbound(new DefaultFullHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.CONTINUE)));

        IHttp2HeadersFrame headersFrame = ch.ReadOutbound<IHttp2HeadersFrame>();
        Assert.Equal("100", headersFrame.Headers().Status().ToString());
        Assert.False(headersFrame.IsEndStream());

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void EncodeNonFullHttpResponse100ContinueIsRejected()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(true));
        Assert.ThrowsAny<EncoderException>(() =>
        {
            ch.WriteOutbound(new DefaultHttpResponse(
                    HttpVersion.HTTP_1_1, HttpResponseStatus.CONTINUE));
        });
        ch.FinishAndReleaseAll();
    }

    [Fact]
    public void TestUpgradeNonEmptyFullResponse()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(true));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        Assert.True(ch.WriteOutbound(new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK, hello)));

        IHttp2HeadersFrame headersFrame = ch.ReadOutbound<IHttp2HeadersFrame>();
        Assert.Equal("200", headersFrame.Headers().Status().ToString());
        Assert.False(headersFrame.IsEndStream());

        IHttp2DataFrame dataFrame = ch.ReadOutbound<IHttp2DataFrame>();
        try
        {
            Assert.Equal("hello world", dataFrame.Content.ToString(Encoding.UTF8));
            Assert.True(dataFrame.IsEndStream());
        }
        finally
        {
            dataFrame.Release();
        }

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestUpgradeEmptyFullResponseWithTrailers()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(true));
        IFullHttpResponse response = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        HttpHeaders trailers = response.TrailingHeaders();
        trailers.Set("key", "value");
        Assert.True(ch.WriteOutbound(response));

        IHttp2HeadersFrame headersFrame = ch.ReadOutbound<IHttp2HeadersFrame>();
        Assert.Equal("200", headersFrame.Headers().Status().ToString());
        Assert.False(headersFrame.IsEndStream());

        IHttp2HeadersFrame trailersFrame = ch.ReadOutbound<IHttp2HeadersFrame>();
        Assert.Equal("value", trailersFrame.Headers().Get(Of("key")).ToString());
        Assert.True(trailersFrame.IsEndStream());

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestUpgradeNonEmptyFullResponseWithTrailers()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(true));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        IFullHttpResponse response = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK, hello);
        HttpHeaders trailers = response.TrailingHeaders();
        trailers.Set("key", "value");
        Assert.True(ch.WriteOutbound(response));

        IHttp2HeadersFrame headersFrame = ch.ReadOutbound<IHttp2HeadersFrame>();
        Assert.Equal("200", headersFrame.Headers().Status().ToString());
        Assert.False(headersFrame.IsEndStream());

        IHttp2DataFrame dataFrame = ch.ReadOutbound<IHttp2DataFrame>();
        try
        {
            Assert.Equal("hello world", dataFrame.Content.ToString(Encoding.UTF8));
            Assert.False(dataFrame.IsEndStream());
        }
        finally
        {
            dataFrame.Release();
        }

        IHttp2HeadersFrame trailersFrame = ch.ReadOutbound<IHttp2HeadersFrame>();
        Assert.Equal("value", trailersFrame.Headers().Get(Of("key")).ToString());
        Assert.True(trailersFrame.IsEndStream());

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestUpgradeHeaders()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(true));
        IHttpResponse response = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        Assert.True(ch.WriteOutbound(response));

        IHttp2HeadersFrame headersFrame = ch.ReadOutbound<IHttp2HeadersFrame>();
        Assert.Equal("200", headersFrame.Headers().Status().ToString());
        Assert.False(headersFrame.IsEndStream());

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestUpgradeChunk()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(true));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        IHttpContent content = new DefaultHttpContent(hello);
        Assert.True(ch.WriteOutbound(content));

        IHttp2DataFrame dataFrame = ch.ReadOutbound<IHttp2DataFrame>();
        try
        {
            Assert.Equal("hello world", dataFrame.Content.ToString(Encoding.UTF8));
            Assert.False(dataFrame.IsEndStream());
        }
        finally
        {
            dataFrame.Release();
        }

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestUpgradeEmptyEnd()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(true));
        ILastHttpContent end = ILastHttpContent.EMPTY_LAST_CONTENT;
        Assert.True(ch.WriteOutbound(end));

        IHttp2DataFrame emptyFrame = ch.ReadOutbound<IHttp2DataFrame>();
        try
        {
            Assert.Equal(0, emptyFrame.Content.ReadableBytes);
            Assert.True(emptyFrame.IsEndStream());
        }
        finally
        {
            emptyFrame.Release();
        }

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestUpgradeDataEnd()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(true));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        // CLR: new DefaultLastHttpContent(hello, true) (deprecated, see the class comment).
        ILastHttpContent end = new DefaultLastHttpContent(hello);
        Assert.True(ch.WriteOutbound(end));

        IHttp2DataFrame dataFrame = ch.ReadOutbound<IHttp2DataFrame>();
        try
        {
            Assert.Equal("hello world", dataFrame.Content.ToString(Encoding.UTF8));
            Assert.True(dataFrame.IsEndStream());
        }
        finally
        {
            dataFrame.Release();
        }

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestUpgradeTrailers()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(true));
        // CLR: new DefaultLastHttpContent(Unpooled.EMPTY_BUFFER, true) (deprecated, see the class comment).
        ILastHttpContent trailers = new DefaultLastHttpContent(Unpooled.EmptyBuffer);
        HttpHeaders headers = trailers.TrailingHeaders();
        headers.Set("key", "value");
        Assert.True(ch.WriteOutbound(trailers));

        IHttp2HeadersFrame headerFrame = ch.ReadOutbound<IHttp2HeadersFrame>();
        Assert.Equal("value", headerFrame.Headers().Get(Of("key")).ToString());
        Assert.True(headerFrame.IsEndStream());

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestUpgradeDataEndWithTrailers()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(true));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        // CLR: new DefaultLastHttpContent(hello, true) (deprecated, see the class comment).
        ILastHttpContent trailers = new DefaultLastHttpContent(hello);
        HttpHeaders headers = trailers.TrailingHeaders();
        headers.Set("key", "value");
        Assert.True(ch.WriteOutbound(trailers));

        IHttp2DataFrame dataFrame = ch.ReadOutbound<IHttp2DataFrame>();
        try
        {
            Assert.Equal("hello world", dataFrame.Content.ToString(Encoding.UTF8));
            Assert.False(dataFrame.IsEndStream());
        }
        finally
        {
            dataFrame.Release();
        }

        IHttp2HeadersFrame headerFrame = ch.ReadOutbound<IHttp2HeadersFrame>();
        Assert.Equal("value", headerFrame.Headers().Get(Of("key")).ToString());
        Assert.True(headerFrame.IsEndStream());

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestDowngradeExtendedConnectHeaders()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(true));
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Method(HttpMethod.CONNECT.AsciiName());
        headers.Authority(Of("ws.example:443"));
        headers.Scheme(Of("https"));
        headers.Path(Of("/admin/ws"));
        headers.Add(IHttp2Headers.PseudoHeaderName.PROTOCOL.Value(), Of("websocket"));

        Assert.True(ch.WriteInbound(new DefaultHttp2HeadersFrame(headers, true)));

        IFullHttpRequest request = ch.ReadInbound<IFullHttpRequest>();
        try
        {
            // The request-target/URI stays the authority, matching regular CONNECT: this fix does not
            // change wire-level CONNECT behavior for downstream code that only understands regular CONNECT.
            Assert.Equal("ws.example:443", request.Uri());
            Assert.Equal(HttpMethod.CONNECT, request.Method());
            Assert.Equal("ws.example:443", request.Headers().Get("host"));

            // But the Extended CONNECT (RFC 8441) state is preserved via extension headers, so a
            // protocol-aware policy can distinguish this from a regular CONNECT tunnel request.
            Assert.Equal("websocket",
                    request.Headers().Get(HttpConversionUtil.ExtensionHeaderNames.PROTOCOL.Text()));
            Assert.Equal("/admin/ws",
                    request.Headers().Get(HttpConversionUtil.ExtensionHeaderNames.PATH.Text()));
        }
        finally
        {
            request.Release();
        }

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestDowngradeRegularConnectHeadersHaveNoExtendedConnectState()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(true));
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Method(HttpMethod.CONNECT.AsciiName());
        headers.Authority(Of("ws.example:443"));

        Assert.True(ch.WriteInbound(new DefaultHttp2HeadersFrame(headers, true)));

        IFullHttpRequest request = ch.ReadInbound<IFullHttpRequest>();
        try
        {
            Assert.Equal("ws.example:443", request.Uri());
            Assert.Equal(HttpMethod.CONNECT, request.Method());
            Assert.False(request.Headers().Contains(HttpConversionUtil.ExtensionHeaderNames.PROTOCOL.Text()));
            Assert.False(request.Headers().Contains(HttpConversionUtil.ExtensionHeaderNames.PATH.Text()));
        }
        finally
        {
            request.Release();
        }

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestDowngradeHeaders()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(true));
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Path(Of("/"));
        headers.Method(Of("GET"));

        Assert.True(ch.WriteInbound(new DefaultHttp2HeadersFrame(headers)));

        IHttpRequest request = ch.ReadInbound<IHttpRequest>();
        Assert.Equal("/", request.Uri());
        Assert.Equal(HttpMethod.GET, request.Method());
        Assert.Equal(HttpVersion.HTTP_1_1, request.ProtocolVersion());
        Assert.IsNotAssignableFrom<IFullHttpRequest>(request);
        Assert.True(HttpUtil.IsTransferEncodingChunked(request));

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestDowngradeHeadersWithContentLength()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(true));
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Path(Of("/"));
        headers.Method(Of("GET"));
        headers.SetInt(Of("content-length"), 0);

        Assert.True(ch.WriteInbound(new DefaultHttp2HeadersFrame(headers)));

        IHttpRequest request = ch.ReadInbound<IHttpRequest>();
        Assert.Equal("/", request.Uri());
        Assert.Equal(HttpMethod.GET, request.Method());
        Assert.Equal(HttpVersion.HTTP_1_1, request.ProtocolVersion());
        Assert.IsNotAssignableFrom<IFullHttpRequest>(request);
        Assert.False(HttpUtil.IsTransferEncodingChunked(request));

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestDowngradeFullHeaders()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(true));
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Path(Of("/"));
        headers.Method(Of("GET"));

        Assert.True(ch.WriteInbound(new DefaultHttp2HeadersFrame(headers, true)));

        IFullHttpRequest request = ch.ReadInbound<IFullHttpRequest>();
        try
        {
            Assert.Equal("/", request.Uri());
            Assert.Equal(HttpMethod.GET, request.Method());
            Assert.Equal(HttpVersion.HTTP_1_1, request.ProtocolVersion());
            Assert.Equal(0, request.Content.ReadableBytes);
            Assert.True(request.TrailingHeaders().IsEmpty());
            Assert.False(HttpUtil.IsTransferEncodingChunked(request));
        }
        finally
        {
            request.Release();
        }

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestDowngradeTrailers()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(true));
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Set(Of("key"), Of("value"));
        Assert.True(ch.WriteInbound(new DefaultHttp2HeadersFrame(headers, true)));

        ILastHttpContent trailers = ch.ReadInbound<ILastHttpContent>();
        try
        {
            Assert.Equal(0, trailers.Content.ReadableBytes);
            Assert.Equal("value", trailers.TrailingHeaders().Get("key"));
            Assert.IsNotAssignableFrom<IFullHttpRequest>(trailers);
        }
        finally
        {
            trailers.Release();
        }

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestDowngradeData()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(true));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        Assert.True(ch.WriteInbound(new DefaultHttp2DataFrame(hello)));

        IHttpContent content = ch.ReadInbound<IHttpContent>();
        try
        {
            Assert.Equal("hello world", content.Content.ToString(Encoding.UTF8));
            Assert.False(content is ILastHttpContent);
        }
        finally
        {
            content.Release();
        }

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestDowngradeEndData()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(true));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        Assert.True(ch.WriteInbound(new DefaultHttp2DataFrame(hello, true)));

        ILastHttpContent content = ch.ReadInbound<ILastHttpContent>();
        try
        {
            Assert.Equal("hello world", content.Content.ToString(Encoding.UTF8));
            Assert.True(content.TrailingHeaders().IsEmpty());
        }
        finally
        {
            content.Release();
        }

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestPassThroughOther()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(true));
        IHttp2ResetFrame reset = new DefaultHttp2ResetFrame(0);
        IHttp2GoAwayFrame goaway = new DefaultHttp2GoAwayFrame(0);
        Assert.True(ch.WriteInbound(reset));
        Assert.True(ch.WriteInbound(goaway.Retain()));

        Assert.Equal<object>(reset, ch.ReadInbound());

        IHttp2GoAwayFrame frame = ch.ReadInbound<IHttp2GoAwayFrame>();
        try
        {
            Assert.Equal(goaway, frame);
            Assert.Null(ch.ReadInbound());
            Assert.False(ch.Finish());
        }
        finally
        {
            goaway.Release();
            frame.Release();
        }
    }

    // client-specific tests
    [Fact]
    public void TestEncodeEmptyFullRequest()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(false));
        Assert.True(ch.WriteOutbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/hello/world")));

        IHttp2HeadersFrame headersFrame = ch.ReadOutbound<IHttp2HeadersFrame>();
        IHttp2Headers headers = headersFrame.Headers();

        Assert.Equal("http", headers.Scheme().ToString());
        Assert.Equal("GET", headers.Method().ToString());
        Assert.Equal("/hello/world", headers.Path().ToString());
        Assert.True(headersFrame.IsEndStream());

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncodeHttpsSchemeWhenSslHandlerExists()
    {
        ConcurrentQueue<IHttp2StreamFrame> frames = new ConcurrentQueue<IHttp2StreamFrame>();

        SslContext ctx = SslContextBuilder.ForClient().SslProvider(SslProvider.JDK).Build();
        EmbeddedChannel ch = new EmbeddedChannel(ctx.NewHandler(IByteBufAllocator.Default),
                new EmptyBufferFrameWriter(frames), new Http2StreamFrameToHttpObjectCodec(false));

        try
        {
            IFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/hello/world");
            Assert.True(ch.WriteOutbound(req));

            ch.FinishAndReleaseAll();

            IHttp2HeadersFrame headersFrame = (IHttp2HeadersFrame) Poll(frames);
            IHttp2Headers headers = headersFrame.Headers();

            Assert.Equal("https", headers.Scheme().ToString());
            Assert.Equal("GET", headers.Method().ToString());
            Assert.Equal("/hello/world", headers.Path().ToString());
            Assert.True(headersFrame.IsEndStream());
            Assert.Null(Poll(frames));
        }
        finally
        {
            ch.FinishAndReleaseAll();
        }
    }

    private sealed class EmptyBufferFrameWriter : ChannelOutboundHandlerAdapter
    {
        private readonly ConcurrentQueue<IHttp2StreamFrame> frames;

        internal EmptyBufferFrameWriter(ConcurrentQueue<IHttp2StreamFrame> frames)
        {
            this.frames = frames;
        }

        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            if (msg is IHttp2StreamFrame)
            {
                frames.Enqueue((IHttp2StreamFrame) msg);
                return ctx.WriteAsync(Unpooled.EmptyBuffer, cancellationToken);
            }
            else
            {
                return ctx.WriteAsync(msg, cancellationToken);
            }
        }
    }

    [Fact]
    public void TestEncodeNonEmptyFullRequest()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(false));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        Assert.True(ch.WriteOutbound(new DefaultFullHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.PUT, "/hello/world", hello)));

        IHttp2HeadersFrame headersFrame = ch.ReadOutbound<IHttp2HeadersFrame>();
        IHttp2Headers headers = headersFrame.Headers();

        Assert.Equal("http", headers.Scheme().ToString());
        Assert.Equal("PUT", headers.Method().ToString());
        Assert.Equal("/hello/world", headers.Path().ToString());
        Assert.False(headersFrame.IsEndStream());

        IHttp2DataFrame dataFrame = ch.ReadOutbound<IHttp2DataFrame>();
        try
        {
            Assert.Equal("hello world", dataFrame.Content.ToString(Encoding.UTF8));
            Assert.True(dataFrame.IsEndStream());
        }
        finally
        {
            dataFrame.Release();
        }

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncodeEmptyFullRequestWithTrailers()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(false));
        IFullHttpRequest request = new DefaultFullHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.PUT, "/hello/world");

        HttpHeaders trailers = request.TrailingHeaders();
        trailers.Set("key", "value");
        Assert.True(ch.WriteOutbound(request));

        IHttp2HeadersFrame headersFrame = ch.ReadOutbound<IHttp2HeadersFrame>();
        IHttp2Headers headers = headersFrame.Headers();

        Assert.Equal("http", headers.Scheme().ToString());
        Assert.Equal("PUT", headers.Method().ToString());
        Assert.Equal("/hello/world", headers.Path().ToString());
        Assert.False(headersFrame.IsEndStream());

        IHttp2HeadersFrame trailersFrame = ch.ReadOutbound<IHttp2HeadersFrame>();
        Assert.Equal("value", trailersFrame.Headers().Get(Of("key")).ToString());
        Assert.True(trailersFrame.IsEndStream());

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncodeNonEmptyFullRequestWithTrailers()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(false));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        IFullHttpRequest request = new DefaultFullHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.PUT, "/hello/world", hello);

        HttpHeaders trailers = request.TrailingHeaders();
        trailers.Set("key", "value");
        Assert.True(ch.WriteOutbound(request));

        IHttp2HeadersFrame headersFrame = ch.ReadOutbound<IHttp2HeadersFrame>();
        IHttp2Headers headers = headersFrame.Headers();

        Assert.Equal("http", headers.Scheme().ToString());
        Assert.Equal("PUT", headers.Method().ToString());
        Assert.Equal("/hello/world", headers.Path().ToString());
        Assert.False(headersFrame.IsEndStream());

        IHttp2DataFrame dataFrame = ch.ReadOutbound<IHttp2DataFrame>();
        try
        {
            Assert.Equal("hello world", dataFrame.Content.ToString(Encoding.UTF8));
            Assert.False(dataFrame.IsEndStream());
        }
        finally
        {
            dataFrame.Release();
        }

        IHttp2HeadersFrame trailersFrame = ch.ReadOutbound<IHttp2HeadersFrame>();
        Assert.Equal("value", trailersFrame.Headers().Get(Of("key")).ToString());
        Assert.True(trailersFrame.IsEndStream());

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncodeRequestHeaders()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(false));
        IHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/hello/world");
        Assert.True(ch.WriteOutbound(request));

        IHttp2HeadersFrame headersFrame = ch.ReadOutbound<IHttp2HeadersFrame>();
        IHttp2Headers headers = headersFrame.Headers();

        Assert.Equal("http", headers.Scheme().ToString());
        Assert.Equal("GET", headers.Method().ToString());
        Assert.Equal("/hello/world", headers.Path().ToString());
        Assert.False(headersFrame.IsEndStream());

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncodeChunkAsClient()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(false));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        IHttpContent content = new DefaultHttpContent(hello);
        Assert.True(ch.WriteOutbound(content));

        IHttp2DataFrame dataFrame = ch.ReadOutbound<IHttp2DataFrame>();
        try
        {
            Assert.Equal("hello world", dataFrame.Content.ToString(Encoding.UTF8));
            Assert.False(dataFrame.IsEndStream());
        }
        finally
        {
            dataFrame.Release();
        }

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncodeEmptyEndAsClient()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(false));
        ILastHttpContent end = ILastHttpContent.EMPTY_LAST_CONTENT;
        Assert.True(ch.WriteOutbound(end));

        IHttp2DataFrame emptyFrame = ch.ReadOutbound<IHttp2DataFrame>();
        try
        {
            Assert.Equal(0, emptyFrame.Content.ReadableBytes);
            Assert.True(emptyFrame.IsEndStream());
        }
        finally
        {
            emptyFrame.Release();
        }

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncodeDataEndAsClient()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(false));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        // CLR: new DefaultLastHttpContent(hello, true) (deprecated, see the class comment).
        ILastHttpContent end = new DefaultLastHttpContent(hello);
        Assert.True(ch.WriteOutbound(end));

        IHttp2DataFrame dataFrame = ch.ReadOutbound<IHttp2DataFrame>();
        try
        {
            Assert.Equal("hello world", dataFrame.Content.ToString(Encoding.UTF8));
            Assert.True(dataFrame.IsEndStream());
        }
        finally
        {
            dataFrame.Release();
        }

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncodeTrailersAsClient()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(false));
        // CLR: new DefaultLastHttpContent(Unpooled.EMPTY_BUFFER, true) (deprecated, see the class comment).
        ILastHttpContent trailers = new DefaultLastHttpContent(Unpooled.EmptyBuffer);
        HttpHeaders headers = trailers.TrailingHeaders();
        headers.Set("key", "value");
        Assert.True(ch.WriteOutbound(trailers));

        IHttp2HeadersFrame headerFrame = ch.ReadOutbound<IHttp2HeadersFrame>();
        Assert.Equal("value", headerFrame.Headers().Get(Of("key")).ToString());
        Assert.True(headerFrame.IsEndStream());

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncodeDataEndWithTrailersAsClient()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(false));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        // CLR: new DefaultLastHttpContent(hello, true) (deprecated, see the class comment).
        ILastHttpContent trailers = new DefaultLastHttpContent(hello);
        HttpHeaders headers = trailers.TrailingHeaders();
        headers.Set("key", "value");
        Assert.True(ch.WriteOutbound(trailers));

        IHttp2DataFrame dataFrame = ch.ReadOutbound<IHttp2DataFrame>();
        try
        {
            Assert.Equal("hello world", dataFrame.Content.ToString(Encoding.UTF8));
            Assert.False(dataFrame.IsEndStream());
        }
        finally
        {
            dataFrame.Release();
        }

        IHttp2HeadersFrame headerFrame = ch.ReadOutbound<IHttp2HeadersFrame>();
        Assert.Equal("value", headerFrame.Headers().Get(Of("key")).ToString());
        Assert.True(headerFrame.IsEndStream());

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void Decode100ContinueHttp2HeadersAsFullHttpResponse()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(false));
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Scheme(HttpScheme.HTTP.Name());
        headers.Status(HttpResponseStatus.CONTINUE.CodeAsText());

        Assert.True(ch.WriteInbound(new DefaultHttp2HeadersFrame(headers, false)));

        IFullHttpResponse response = ch.ReadInbound<IFullHttpResponse>();
        try
        {
            Assert.Equal(HttpResponseStatus.CONTINUE, response.Status());
            Assert.Equal(HttpVersion.HTTP_1_1, response.ProtocolVersion());
        }
        finally
        {
            response.Release();
        }

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    /**
     *    An informational response using a 1xx status code other than 101 is
     *    transmitted as a HEADERS frame, followed by zero or more CONTINUATION
     *    frames.
     *    Trailing header fields are sent as a header block after both the
     *    request or response header block and all the DATA frames have been
     *    sent.  The HEADERS frame starting the trailers header block has the
     *    END_STREAM flag set.
     */
    [Fact]
    public void Decode103EarlyHintsHttp2HeadersAsFullHttpResponse()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(false));
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Scheme(HttpScheme.HTTP.Name());
        headers.Status(HttpResponseStatus.EARLY_HINTS.CodeAsText());
        headers.Set(Of("key"), Of("value"));

        Assert.True(ch.WriteInbound(new DefaultHttp2HeadersFrame(headers, false)));

        IFullHttpResponse response = ch.ReadInbound<IFullHttpResponse>();
        try
        {
            Assert.Equal(HttpResponseStatus.EARLY_HINTS, response.Status());
            Assert.Equal(HttpVersion.HTTP_1_1, response.ProtocolVersion());
            Assert.Equal("value", response.Headers().Get("key"));
        }
        finally
        {
            response.Release();
        }

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestDecodeResponseHeaders()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(false));
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Scheme(HttpScheme.HTTP.Name());
        headers.Status(HttpResponseStatus.OK.CodeAsText());

        Assert.True(ch.WriteInbound(new DefaultHttp2HeadersFrame(headers)));

        IHttpResponse response = ch.ReadInbound<IHttpResponse>();
        Assert.Equal(HttpResponseStatus.OK, response.Status());
        Assert.Equal(HttpVersion.HTTP_1_1, response.ProtocolVersion());
        Assert.False(response is IFullHttpResponse);
        Assert.True(HttpUtil.IsTransferEncodingChunked(response));

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestDecodeResponseHeadersWithContentLength()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(false));
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Scheme(HttpScheme.HTTP.Name());
        headers.Status(HttpResponseStatus.OK.CodeAsText());
        headers.SetInt(Of("content-length"), 0);

        Assert.True(ch.WriteInbound(new DefaultHttp2HeadersFrame(headers)));

        IHttpResponse response = ch.ReadInbound<IHttpResponse>();
        Assert.Equal(HttpResponseStatus.OK, response.Status());
        Assert.Equal(HttpVersion.HTTP_1_1, response.ProtocolVersion());
        Assert.IsNotAssignableFrom<IFullHttpResponse>(response);
        Assert.False(HttpUtil.IsTransferEncodingChunked(response));

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Theory]
    [InlineData("204")]
    [InlineData("304")]
    public void TestDecodeResponseHeadersContentAlwaysEmpty(string statusCode)
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(false));
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Scheme(HttpScheme.HTTP.Name());
        headers.Status(Of(statusCode));

        Assert.True(ch.WriteInbound(new DefaultHttp2HeadersFrame(headers)));

        IHttpResponse request = ch.ReadInbound<IHttpResponse>();
        Assert.Equal(statusCode, request.Status().CodeAsText().ToString());
        Assert.Equal(HttpVersion.HTTP_1_1, request.ProtocolVersion());
        Assert.IsNotAssignableFrom<IFullHttpResponse>(request);
        Assert.False(HttpUtil.IsTransferEncodingChunked(request));

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestDecodeFullResponseHeaders()
    {
        TestDecodeFullResponseHeaders0(false);
    }

    [Fact]
    public void TestDecodeFullResponseHeadersWithStreamID()
    {
        TestDecodeFullResponseHeaders0(true);
    }

    private void TestDecodeFullResponseHeaders0(bool withStreamId)
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(false));
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Scheme(HttpScheme.HTTP.Name());
        headers.Status(HttpResponseStatus.OK.CodeAsText());

        IHttp2HeadersFrame frame = new DefaultHttp2HeadersFrame(headers, true);
        if (withStreamId)
        {
            frame.Stream(new OpenStream());
        }

        Assert.True(ch.WriteInbound(frame));

        IFullHttpResponse response = ch.ReadInbound<IFullHttpResponse>();
        try
        {
            Assert.Equal(HttpResponseStatus.OK, response.Status());
            Assert.Equal(HttpVersion.HTTP_1_1, response.ProtocolVersion());
            Assert.Equal(0, response.Content.ReadableBytes);
            Assert.True(response.TrailingHeaders().IsEmpty());
            Assert.False(HttpUtil.IsTransferEncodingChunked(response));
            if (withStreamId)
            {
                Assert.Equal(1,
                        (int) response.Headers().GetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text()));
            }
        }
        finally
        {
            response.Release();
        }

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    private sealed class OpenStream : IHttp2FrameStream
    {
        public int Id()
        {
            return 1;
        }

        public IHttp2Stream.State State()
        {
            return IHttp2Stream.State.OPEN;
        }
    }

    [Fact]
    public void TestDecodeResponseTrailersAsClient()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(false));
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Set(Of("key"), Of("value"));
        Assert.True(ch.WriteInbound(new DefaultHttp2HeadersFrame(headers, true)));

        ILastHttpContent trailers = ch.ReadInbound<ILastHttpContent>();
        try
        {
            Assert.Equal(0, trailers.Content.ReadableBytes);
            Assert.Equal("value", trailers.TrailingHeaders().Get("key"));
            Assert.IsNotAssignableFrom<IFullHttpRequest>(trailers);
        }
        finally
        {
            trailers.Release();
        }

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestDecodeDataAsClient()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(false));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        Assert.True(ch.WriteInbound(new DefaultHttp2DataFrame(hello)));

        IHttpContent content = ch.ReadInbound<IHttpContent>();
        try
        {
            Assert.Equal("hello world", content.Content.ToString(Encoding.UTF8));
            Assert.IsNotAssignableFrom<ILastHttpContent>(content);
        }
        finally
        {
            content.Release();
        }

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestDecodeEndDataAsClient()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(false));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        Assert.True(ch.WriteInbound(new DefaultHttp2DataFrame(hello, true)));

        ILastHttpContent content = ch.ReadInbound<ILastHttpContent>();
        try
        {
            Assert.Equal("hello world", content.Content.ToString(Encoding.UTF8));
            Assert.True(content.TrailingHeaders().IsEmpty());
        }
        finally
        {
            content.Release();
        }

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestPassThroughOtherAsClient()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new Http2StreamFrameToHttpObjectCodec(false));
        IHttp2ResetFrame reset = new DefaultHttp2ResetFrame(0);
        IHttp2GoAwayFrame goaway = new DefaultHttp2GoAwayFrame(0);
        Assert.True(ch.WriteInbound(reset));
        Assert.True(ch.WriteInbound(goaway.Retain()));

        Assert.Equal<object>(reset, ch.ReadInbound());

        IHttp2GoAwayFrame frame = ch.ReadInbound<IHttp2GoAwayFrame>();
        try
        {
            Assert.Equal(goaway, frame);
            Assert.Null(ch.ReadInbound());
            Assert.False(ch.Finish());
        }
        finally
        {
            goaway.Release();
            frame.Release();
        }
    }

    [Fact]
    public void TestIsSharableBetweenChannels()
    {
        ConcurrentQueue<IHttp2StreamFrame> frames = new ConcurrentQueue<IHttp2StreamFrame>();
        IChannelHandler sharedHandler = new Http2StreamFrameToHttpObjectCodec(false);

        SslContext ctx = SslContextBuilder.ForClient().SslProvider(SslProvider.JDK).Build();
        EmbeddedChannel tlsCh = new EmbeddedChannel(ctx.NewHandler(IByteBufAllocator.Default),
            new FrameSink(frames), sharedHandler);

        EmbeddedChannel plaintextCh = new EmbeddedChannel(
            new FrameSink(frames), sharedHandler);

        IFullHttpRequest req = new DefaultFullHttpRequest(
            HttpVersion.HTTP_1_1, HttpMethod.GET, "/hello/world");
        Assert.True(tlsCh.WriteOutbound(req));
        Assert.True(tlsCh.FinishAndReleaseAll());

        IHttp2HeadersFrame headersFrame = (IHttp2HeadersFrame) Poll(frames);
        IHttp2Headers headers = headersFrame.Headers();

        Assert.Equal("https", headers.Scheme().ToString());
        Assert.Equal("GET", headers.Method().ToString());
        Assert.Equal("/hello/world", headers.Path().ToString());
        Assert.True(headersFrame.IsEndStream());
        Assert.Null(Poll(frames));

        // Run the plaintext channel
        req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/hello/world");
        Assert.False(plaintextCh.WriteOutbound(req));
        Assert.False(plaintextCh.FinishAndReleaseAll());

        headersFrame = (IHttp2HeadersFrame) Poll(frames);
        headers = headersFrame.Headers();

        Assert.Equal("http", headers.Scheme().ToString());
        Assert.Equal("GET", headers.Method().ToString());
        Assert.Equal("/hello/world", headers.Path().ToString());
        Assert.True(headersFrame.IsEndStream());
        Assert.Null(Poll(frames));
    }

    private sealed class FrameSink : ChannelOutboundHandlerAdapter
    {
        private readonly ConcurrentQueue<IHttp2StreamFrame> frames;

        internal FrameSink(ConcurrentQueue<IHttp2StreamFrame> frames)
        {
            this.frames = frames;
        }

        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            if (msg is IHttp2StreamFrame)
            {
                frames.Enqueue((IHttp2StreamFrame) msg);
                return Task.CompletedTask;
            }
            else
            {
                return ctx.WriteAsync(msg, cancellationToken);
            }
        }
    }

    // CLR: Queue.poll(), null when the queue is empty.
    private static IHttp2StreamFrame Poll(ConcurrentQueue<IHttp2StreamFrame> frames)
    {
        return frames.TryDequeue(out IHttp2StreamFrame frame) ? frame : null;
    }
}
