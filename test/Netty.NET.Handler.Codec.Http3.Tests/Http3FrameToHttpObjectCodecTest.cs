/*
 * Copyright 2021 The Netty Project
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;
using Assert = XunitAssertMessages.AssertM;
using static Netty.NET.Handler.Codec.Http3.Tests.Http3TestUtils;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: The QUIC endpoint starts listening at bind time, so each loopback endpoint binds with its codec already
// installed.
// The write tests complete controlled tasks to exercise when the aggregate write becomes observable. The FIN test
// uses a bounded queue wait for the decoded content.
public class Http3FrameToHttpObjectCodecTest
{

    [Fact]
    public void TestUpgradeEmptyFullResponse()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(true));
        Assert.True(ch.WriteOutbound(new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK)));

        IHttp3HeadersFrame headersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        Assert.Equal("200", headersFrame.Headers().Status().ToString());
        Assert.True(ch.IsOutputShutdown);

        Assert.False(ch.Finish());
    }

    [Fact]
    public void Encode100ContinueAsHttp2HeadersFrameThatIsNotEndStream()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(true));
        Assert.True(ch.WriteOutbound(new DefaultFullHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.CONTINUE)));

        IHttp3HeadersFrame headersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        Assert.Equal("100", headersFrame.Headers().Status().ToString());
        Assert.False(ch.IsOutputShutdown);

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void EncodeNonFullHttpResponse100ContinueIsRejected()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(true));
        Assert.ThrowsAny<EncoderException>(() => ch.WriteOutbound(new DefaultHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.CONTINUE)));
        ch.FinishAndReleaseAll();
    }

    [Fact]
    public void TestUpgradeNonEmptyFullResponse()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(true));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        Assert.True(ch.WriteOutbound(new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK, hello)));

        IHttp3HeadersFrame headersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        Assert.Equal("200", headersFrame.Headers().Status().ToString());

        IHttp3DataFrame dataFrame = ch.ReadOutbound<IHttp3DataFrame>();

        try
        {
            Assert.Equal("hello world", dataFrame.Content.ToString(Encoding.UTF8));
        }
        finally
        {
            dataFrame.Release();
        }

        Assert.True(ch.IsOutputShutdown);
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestUpgradeEmptyFullResponseWithTrailers()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(true));
        IFullHttpResponse response = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        HttpHeaders trailers = response.TrailingHeaders();
        trailers.Set("key", "value");
        Assert.True(ch.WriteOutbound(response));

        IHttp3HeadersFrame headersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        Assert.Equal("200", headersFrame.Headers().Status().ToString());

        IHttp3HeadersFrame trailersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        Assert.Equal("value", trailersFrame.Headers().Get(Cs("key")).ToString());
        Assert.True(ch.IsOutputShutdown);

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestUpgradeNonEmptyFullResponseWithTrailers()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(true));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        IFullHttpResponse response = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK, hello);
        HttpHeaders trailers = response.TrailingHeaders();
        trailers.Set("key", "value");
        Assert.True(ch.WriteOutbound(response));

        IHttp3HeadersFrame headersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        Assert.Equal("200", headersFrame.Headers().Status().ToString());

        IHttp3DataFrame dataFrame = ch.ReadOutbound<IHttp3DataFrame>();

        try
        {
            Assert.Equal("hello world", dataFrame.Content.ToString(Encoding.UTF8));
        }
        finally
        {
            dataFrame.Release();
        }

        IHttp3HeadersFrame trailersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        Assert.Equal("value", trailersFrame.Headers().Get(Cs("key")).ToString());
        Assert.True(ch.IsOutputShutdown);

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestUpgradeHeaders()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(true));
        IHttpResponse response = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        Assert.True(ch.WriteOutbound(response));

        IHttp3HeadersFrame headersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        Assert.Equal("200", headersFrame.Headers().Status().ToString());
        Assert.False(ch.IsOutputShutdown);

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestUpgradeChunk()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(true));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        IHttpContent content = new DefaultHttpContent(hello);
        Assert.True(ch.WriteOutbound(content));

        IHttp3DataFrame dataFrame = ch.ReadOutbound<IHttp3DataFrame>();

        try
        {
            Assert.Equal("hello world", dataFrame.Content.ToString(Encoding.UTF8));
            Assert.False(ch.IsOutputShutdown);
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
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(true));
        ch.WriteOutbound(ILastHttpContent.EMPTY_LAST_CONTENT);

        Assert.True(ch.IsOutputShutdown);
        IHttp3DataFrame dataFrame = ch.ReadOutbound<IHttp3DataFrame>();

        try
        {
            Assert.Equal(0, dataFrame.Content.ReadableBytes);
        }
        finally
        {
            dataFrame.Release();
        }

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestUpgradeDataEnd()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(true));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        // CLR: new DefaultLastHttpContent(hello, true) (deprecated, see the class comment).
        ILastHttpContent end = new DefaultLastHttpContent(hello);
        Assert.True(ch.WriteOutbound(end));

        IHttp3DataFrame dataFrame = ch.ReadOutbound<IHttp3DataFrame>();

        try
        {
            Assert.Equal("hello world", dataFrame.Content.ToString(Encoding.UTF8));
        }
        finally
        {
            dataFrame.Release();
        }

        Assert.True(ch.IsOutputShutdown);
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestUpgradeDataEndWithTrailers()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(true));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        // CLR: new DefaultLastHttpContent(hello, true) (deprecated, see the class comment).
        ILastHttpContent trailers = new DefaultLastHttpContent(hello);
        HttpHeaders headers = trailers.TrailingHeaders();
        headers.Set("key", "value");
        Assert.True(ch.WriteOutbound(trailers));

        IHttp3DataFrame dataFrame = ch.ReadOutbound<IHttp3DataFrame>();

        try
        {
            Assert.Equal("hello world", dataFrame.Content.ToString(Encoding.UTF8));
        }
        finally
        {
            dataFrame.Release();
        }

        IHttp3HeadersFrame headerFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        Assert.Equal("value", headerFrame.Headers().Get(Cs("key")).ToString());
        Assert.True(ch.IsOutputShutdown);

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestDowngradeExtendedConnectHeaders()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(true));
        IHttp3Headers headers = new DefaultHttp3Headers();
        headers.Method(HttpMethod.CONNECT.AsciiName());
        headers.Authority(Cs("ws.example:443"));
        headers.Scheme(Cs("https"));
        headers.Path(Cs("/admin/ws"));
        headers.Protocol(Cs("websocket"));

        Assert.True(ch.WriteInbound(new DefaultHttp3HeadersFrame(headers)));

        IHttpRequest request = ch.ReadInbound<IHttpRequest>();
        // The request-target/URI stays the authority, matching regular CONNECT: this fix does not
        // change wire-level CONNECT behavior for downstream code that only understands regular CONNECT.
        Assert.Equal("ws.example:443", request.Uri());
        Assert.Equal(HttpMethod.CONNECT, request.Method());
        Assert.Equal("ws.example:443", request.Headers().Get("host"));

        // But the Extended CONNECT (RFC 9220) state is preserved via extension headers, so a
        // protocol-aware policy can distinguish this from a regular CONNECT tunnel request.
        Assert.Equal("websocket", request.Headers().Get(HttpConversionUtil.ExtensionHeaderNames.PROTOCOL.Text()));
        Assert.Equal("/admin/ws", request.Headers().Get(HttpConversionUtil.ExtensionHeaderNames.PATH.Text()));

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestDowngradeRegularConnectHeadersHaveNoExtendedConnectState()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(true));
        IHttp3Headers headers = new DefaultHttp3Headers();
        headers.Method(HttpMethod.CONNECT.AsciiName());
        headers.Authority(Cs("ws.example:443"));

        Assert.True(ch.WriteInbound(new DefaultHttp3HeadersFrame(headers)));

        IHttpRequest request = ch.ReadInbound<IHttpRequest>();
        Assert.Equal("ws.example:443", request.Uri());
        Assert.Equal(HttpMethod.CONNECT, request.Method());
        Assert.False(request.Headers().Contains(HttpConversionUtil.ExtensionHeaderNames.PROTOCOL.Text()));
        Assert.False(request.Headers().Contains(HttpConversionUtil.ExtensionHeaderNames.PATH.Text()));

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestDowngradeHeaders()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(true));
        IHttp3Headers headers = new DefaultHttp3Headers();
        headers.Path(Cs("/"));
        headers.Method(Cs("GET"));

        Assert.True(ch.WriteInbound(new DefaultHttp3HeadersFrame(headers)));

        IHttpRequest request = ch.ReadInbound<IHttpRequest>();
        Assert.Equal("/", request.Uri());
        Assert.Equal(HttpMethod.GET, request.Method());
        Assert.Equal(HttpVersion.HTTP_1_1, request.ProtocolVersion());
        Assert.False(request is IFullHttpRequest);
        Assert.True(HttpUtil.IsTransferEncodingChunked(request));

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestDowngradeHeadersRejectsConflictingAuthorityAndHost()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(true));
        IHttp3Headers headers = new DefaultHttp3Headers();
        headers.Path(Cs("/"));
        headers.Method(Cs("GET"));
        headers.Scheme(Cs("https"));
        headers.Authority(Cs("public.example.com"));
        headers.Add(HttpHeaderNames.HOST, Cs("internal-admin.local"));

        // Http3RequestStreamInboundHandler#exceptionCaught consumes the Http3Exception, so the frame is
        // dropped rather than rethrown here.
        Assert.False(ch.WriteInbound(new DefaultHttp3HeadersFrame(headers)));

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestDowngradeHeadersWithContentLength()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(true));
        IHttp3Headers headers = new DefaultHttp3Headers();
        headers.Path(Cs("/"));
        headers.Method(Cs("GET"));
        headers.SetInt(Cs("content-length"), 0);

        Assert.True(ch.WriteInbound(new DefaultHttp3HeadersFrame(headers)));

        IHttpRequest request = ch.ReadInbound<IHttpRequest>();
        Assert.Equal("/", request.Uri());
        Assert.Equal(HttpMethod.GET, request.Method());
        Assert.Equal(HttpVersion.HTTP_1_1, request.ProtocolVersion());
        Assert.False(request is IFullHttpRequest);
        Assert.False(HttpUtil.IsTransferEncodingChunked(request));

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestDowngradeTrailers()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(true));
        IHttp3Headers headers = new DefaultHttp3Headers();
        headers.Set(Cs("key"), Cs("value"));

        Assert.True(ch.WriteInboundWithFin(new DefaultHttp3HeadersFrame(headers)));

        ILastHttpContent trailers = ch.ReadInbound<ILastHttpContent>();

        try
        {
            Assert.Equal(0, trailers.Content.ReadableBytes);
            Assert.Equal("value", trailers.TrailingHeaders().Get("key"));
            Assert.False(trailers is IFullHttpRequest);
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
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(true));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        Assert.True(ch.WriteInbound(new DefaultHttp3DataFrame(hello)));

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
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(true));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        Assert.True(ch.WriteInboundWithFin(new DefaultHttp3DataFrame(hello)));

        IHttpContent content = ch.ReadInbound<IHttpContent>();

        try
        {
            Assert.Equal("hello world", content.Content.ToString(Encoding.UTF8));
        }
        finally
        {
            content.Release();
        }

        ILastHttpContent last = ch.ReadInbound<ILastHttpContent>();

        try
        {
            Assert.False(last.Content.IsReadable());
            Assert.True(last.TrailingHeaders().IsEmpty());
        }
        finally
        {
            last.Release();
        }

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    // client-specific tests
    [Fact]
    public void TestEncodeEmptyFullRequest()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(false));
        Assert.True(ch.WriteOutbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/hello/world")));

        IHttp3HeadersFrame headersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        IHttp3Headers headers = headersFrame.Headers();

        Assert.Equal("https", headers.Scheme().ToString());
        Assert.Equal("GET", headers.Method().ToString());
        Assert.Equal("/hello/world", headers.Path().ToString());
        Assert.True(ch.IsOutputShutdown);

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncodeNonEmptyFullRequest()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(false));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        Assert.True(ch.WriteOutbound(new DefaultFullHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.PUT, "/hello/world", hello)));

        IHttp3HeadersFrame headersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        IHttp3Headers headers = headersFrame.Headers();

        Assert.Equal("https", headers.Scheme().ToString());
        Assert.Equal("PUT", headers.Method().ToString());
        Assert.Equal("/hello/world", headers.Path().ToString());

        IHttp3DataFrame dataFrame = ch.ReadOutbound<IHttp3DataFrame>();

        try
        {
            Assert.Equal("hello world", dataFrame.Content.ToString(Encoding.UTF8));
        }
        finally
        {
            dataFrame.Release();
        }

        Assert.True(ch.IsOutputShutdown);
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncodeEmptyFullRequestWithTrailers()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(false));
        IFullHttpRequest request = new DefaultFullHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.PUT, "/hello/world");

        HttpHeaders trailers = request.TrailingHeaders();
        trailers.Set("key", "value");
        Assert.True(ch.WriteOutbound(request));

        IHttp3HeadersFrame headersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        IHttp3Headers headers = headersFrame.Headers();

        Assert.Equal("https", headers.Scheme().ToString());
        Assert.Equal("PUT", headers.Method().ToString());
        Assert.Equal("/hello/world", headers.Path().ToString());

        IHttp3HeadersFrame trailersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        Assert.Equal("value", trailersFrame.Headers().Get(Cs("key")).ToString());

        Assert.True(ch.IsOutputShutdown);
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncodeNonEmptyFullRequestWithTrailers()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(false));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        IFullHttpRequest request = new DefaultFullHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.PUT, "/hello/world", hello);

        HttpHeaders trailers = request.TrailingHeaders();
        trailers.Set("key", "value");
        Assert.True(ch.WriteOutbound(request));

        IHttp3HeadersFrame headersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        IHttp3Headers headers = headersFrame.Headers();

        Assert.Equal("https", headers.Scheme().ToString());
        Assert.Equal("PUT", headers.Method().ToString());
        Assert.Equal("/hello/world", headers.Path().ToString());

        IHttp3DataFrame dataFrame = ch.ReadOutbound<IHttp3DataFrame>();

        try
        {
            Assert.Equal("hello world", dataFrame.Content.ToString(Encoding.UTF8));
        }
        finally
        {
            dataFrame.Release();
        }

        IHttp3HeadersFrame trailersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        Assert.Equal("value", trailersFrame.Headers().Get(Cs("key")).ToString());

        Assert.True(ch.IsOutputShutdown);
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncodeRequestHeaders()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(false));
        IHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/hello/world");
        Assert.True(ch.WriteOutbound(request));

        IHttp3HeadersFrame headersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        IHttp3Headers headers = headersFrame.Headers();

        Assert.Equal("https", headers.Scheme().ToString());
        Assert.Equal("GET", headers.Method().ToString());
        Assert.Equal("/hello/world", headers.Path().ToString());
        Assert.False(ch.IsOutputShutdown);

        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncodeChunkAsClient()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(false));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        IHttpContent content = new DefaultHttpContent(hello);
        Assert.True(ch.WriteOutbound(content));

        IHttp3DataFrame dataFrame = ch.ReadOutbound<IHttp3DataFrame>();

        try
        {
            Assert.Equal("hello world", dataFrame.Content.ToString(Encoding.UTF8));
        }
        finally
        {
            dataFrame.Release();
        }

        Assert.False(ch.IsOutputShutdown);
        Assert.Null(ch.ReadOutbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncodeEmptyEndAsClient()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(false));
        ch.WriteOutbound(ILastHttpContent.EMPTY_LAST_CONTENT);

        Assert.True(ch.IsOutputShutdown);
        IHttp3DataFrame dataFrame = ch.ReadOutbound<IHttp3DataFrame>();

        try
        {
            Assert.Equal(0, dataFrame.Content.ReadableBytes);
        }
        finally
        {
            dataFrame.Release();
        }

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncodeDataEndAsClient()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(false));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        // CLR: new DefaultLastHttpContent(hello, true) (deprecated, see the class comment).
        ILastHttpContent end = new DefaultLastHttpContent(hello);
        Assert.True(ch.WriteOutbound(end));

        IHttp3DataFrame dataFrame = ch.ReadOutbound<IHttp3DataFrame>();

        try
        {
            Assert.Equal("hello world", dataFrame.Content.ToString(Encoding.UTF8));
        }
        finally
        {
            dataFrame.Release();
        }

        Assert.True(ch.IsOutputShutdown);
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncodeTrailersAsClient()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(false));
        // CLR: new DefaultLastHttpContent(Unpooled.EMPTY_BUFFER, true) (deprecated, see the class comment).
        ILastHttpContent trailers = new DefaultLastHttpContent(Unpooled.EmptyBuffer);
        HttpHeaders headers = trailers.TrailingHeaders();
        headers.Set("key", "value");
        Assert.True(ch.WriteOutbound(trailers));

        IHttp3HeadersFrame headerFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        Assert.Equal("value", headerFrame.Headers().Get(Cs("key")).ToString());

        Assert.True(ch.IsOutputShutdown);
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncodeDataEndWithTrailersAsClient()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(false));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        // CLR: new DefaultLastHttpContent(hello, true) (deprecated, see the class comment).
        ILastHttpContent trailers = new DefaultLastHttpContent(hello);
        HttpHeaders headers = trailers.TrailingHeaders();
        headers.Set("key", "value");
        Assert.True(ch.WriteOutbound(trailers));

        IHttp3DataFrame dataFrame = ch.ReadOutbound<IHttp3DataFrame>();

        try
        {
            Assert.Equal("hello world", dataFrame.Content.ToString(Encoding.UTF8));
        }
        finally
        {
            dataFrame.Release();
        }

        IHttp3HeadersFrame headerFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        Assert.Equal("value", headerFrame.Headers().Get(Cs("key")).ToString());

        Assert.True(ch.IsOutputShutdown);
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncodeFullPromiseCompletes()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(false));
        Task writeFuture = ch.WriteOneOutboundAsync(new DefaultFullHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.GET, "/hello/world"));
        ch.FlushOutbound();
        Assert.True(writeFuture.IsCompletedSuccessfully);

        IHttp3HeadersFrame headersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        IHttp3Headers headers = headersFrame.Headers();

        Assert.Equal("https", headers.Scheme().ToString());
        Assert.Equal("GET", headers.Method().ToString());
        Assert.Equal("/hello/world", headers.Path().ToString());
        Assert.True(ch.IsOutputShutdown);

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncodeEmptyLastPromiseCompletes()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(false));
        Task f1 = ch.WriteOneOutboundAsync(new DefaultHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.GET, "/hello/world"));
        Task f2 = ch.WriteOneOutboundAsync(new DefaultLastHttpContent());
        ch.FlushOutbound();
        Assert.True(f1.IsCompletedSuccessfully);
        Assert.True(f2.IsCompletedSuccessfully);

        IHttp3HeadersFrame headersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        IHttp3Headers headers = headersFrame.Headers();

        Assert.Equal("https", headers.Scheme().ToString());
        Assert.Equal("GET", headers.Method().ToString());
        Assert.Equal("/hello/world", headers.Path().ToString());
        Assert.True(ch.IsOutputShutdown);

        IHttp3DataFrame dataFrame = ch.ReadOutbound<IHttp3DataFrame>();

        try
        {
            Assert.Equal(0, dataFrame.Content.ReadableBytes);
        }
        finally
        {
            dataFrame.Release();
        }

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncodeMultiplePromiseCompletes()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(false));
        Task f1 = ch.WriteOneOutboundAsync(new DefaultHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.GET, "/hello/world"));
        Task f2 = ch.WriteOneOutboundAsync(new DefaultLastHttpContent(
                Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes("foo"))));
        ch.FlushOutbound();
        Assert.True(f1.IsCompletedSuccessfully);
        Assert.True(f2.IsCompletedSuccessfully);

        IHttp3HeadersFrame headersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        IHttp3Headers headers = headersFrame.Headers();

        Assert.Equal("https", headers.Scheme().ToString());
        Assert.Equal("GET", headers.Method().ToString());
        Assert.Equal("/hello/world", headers.Path().ToString());
        Assert.True(ch.IsOutputShutdown);

        IHttp3DataFrame dataFrame = ch.ReadOutbound<IHttp3DataFrame>();
        Assert.Equal("foo", dataFrame.Content.ToString(Encoding.UTF8));

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncodeTrailersCompletes()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(false));
        Task f1 = ch.WriteOneOutboundAsync(new DefaultHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.GET, "/hello/world"));
        ILastHttpContent last = new DefaultLastHttpContent(
                Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes("foo")));
        last.TrailingHeaders().Add("foo", "bar");
        Task f2 = ch.WriteOneOutboundAsync(last);
        ch.FlushOutbound();
        Assert.True(f1.IsCompletedSuccessfully);
        Assert.True(f2.IsCompletedSuccessfully);

        IHttp3HeadersFrame headersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        IHttp3Headers headers = headersFrame.Headers();

        Assert.Equal("https", headers.Scheme().ToString());
        Assert.Equal("GET", headers.Method().ToString());
        Assert.Equal("/hello/world", headers.Path().ToString());
        Assert.True(ch.IsOutputShutdown);

        IHttp3DataFrame dataFrame = ch.ReadOutbound<IHttp3DataFrame>();
        Assert.Equal("foo", dataFrame.Content.ToString(Encoding.UTF8));

        IHttp3HeadersFrame trailingHeadersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        Assert.Equal("bar", trailingHeadersFrame.Headers().Get(Cs("foo")).ToString());

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestEncodeVoidPromise()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(false));
        _ = ch.WriteOneOutboundAsync(new DefaultFullHttpRequest(
                HttpVersion.HTTP_1_1, HttpMethod.POST, "/hello/world", Unpooled.WrappedBuffer(new byte[1])));
        ch.FlushOutbound();

        IHttp3HeadersFrame headersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        IHttp3Headers headers = headersFrame.Headers();
        IHttp3DataFrame data = ch.ReadOutbound<IHttp3DataFrame>();
        data.Release();
        Assert.Equal("https", headers.Scheme().ToString());
        Assert.Equal("POST", headers.Method().ToString());
        Assert.Equal("/hello/world", headers.Path().ToString());
        Assert.True(ch.IsOutputShutdown);

        Assert.False(ch.Finish());
    }

    private sealed class EncodeCombinationsArgumentsProvider : TheoryData<bool, bool, bool, bool, bool>
    {
        public EncodeCombinationsArgumentsProvider()
        {
            foreach (bool headers in new bool[] { false, true })
            {
                foreach (bool last in new bool[] { false, true })
                {
                    foreach (bool nonEmptyContent in new bool[] { false, true })
                    {
                        foreach (bool hasTrailers in new bool[] { false, true })
                        {
                            foreach (bool voidPromise in new bool[] { false, true })
                            {
                                // this test goes through all the branches of Http3FrameToHttpObjectCodec
                                // and ensures right functionality
                                Add(headers, last, nonEmptyContent, hasTrailers, voidPromise);
                            }
                        }
                    }
                }
            }
        }
    }

    [Theory]
    [ClassData(typeof(EncodeCombinationsArgumentsProvider))]
    public void TestEncodeCombination(
            bool headers,
            bool last,
            bool nonEmptyContent,
            bool hasTrailers,
            bool voidPromise
    )
    {
        ByteBuf content = nonEmptyContent ? Unpooled.WrappedBuffer(new byte[1]) : Unpooled.EmptyBuffer;
        HttpHeaders trailers = new DefaultHttpHeaders();

        if (hasTrailers)
        {
            trailers.Add("foo", "bar");
        }

        IHttpObject msg;

        if (headers)
        {
            if (last)
            {
                msg = new DefaultFullHttpRequest(
                        HttpVersion.HTTP_1_1, HttpMethod.POST, "/foo", content, new DefaultHttpHeaders(), trailers);
            }
            else
            {
                if (hasTrailers || nonEmptyContent)
                {
                    // not supported by the netty HTTP/1 model
                    content.Release();
                    return;
                }

                msg = new DefaultHttpRequest(
                        HttpVersion.HTTP_1_1, HttpMethod.POST, "/foo", new DefaultHttpHeaders());
            }
        }
        else
        {
            if (last)
            {
                msg = new DefaultLastHttpContent(content, trailers);
            }
            else
            {
                if (hasTrailers)
                {
                    // makes no sense
                    content.Release();
                    return;
                }

                msg = new DefaultHttpContent(content);
            }
        }

        List<TaskCompletionSource> framePromises = new List<TaskCompletionSource>();
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(
                new FramePromiseHandler(framePromises),
                new Http3FrameToHttpObjectCodec(false)
        );

        // CLR: ch.voidPromise() and ch.newPromise() are the Task of the write (see the class comment).
        Task fullPromise = ch.WriteOneOutboundAsync(msg);
        ch.FlushOutbound();

        if (headers)
        {
            IHttp3HeadersFrame headersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
            Assert.Equal("https", headersFrame.Headers().Scheme().ToString());
            Assert.Equal("POST", headersFrame.Headers().Method().ToString());
            Assert.Equal("/foo", headersFrame.Headers().Path().ToString());
        }

        if (nonEmptyContent)
        {
            IHttp3DataFrame dataFrame = ch.ReadOutbound<IHttp3DataFrame>();
            Assert.Equal(1, dataFrame.Content.ReadableBytes);
            dataFrame.Release();
        }

        if (hasTrailers)
        {
            IHttp3HeadersFrame trailersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
            Assert.Equal<ICharSequence>(Cs("bar"), trailersFrame.Headers().Get(Cs("foo")));
        }
        else if (!nonEmptyContent && !headers)
        {
            IHttp3DataFrame dataFrame = ch.ReadOutbound<IHttp3DataFrame>();
            Assert.Equal(0, dataFrame.Content.ReadableBytes);
            dataFrame.Release();
        }

        if (!voidPromise)
        {
            Assert.False(fullPromise.IsCompleted);
        }

        Assert.False(ch.IsOutputShutdown);

        foreach (TaskCompletionSource framePromise in framePromises)
        {
            framePromise.TrySetResult();
        }

        if (last)
        {
            Assert.True(ch.IsOutputShutdown);
        }
        else
        {
            Assert.False(ch.IsOutputShutdown);
        }

        if (!voidPromise)
        {
            Assert.True(fullPromise.IsCompleted);
        }

        Assert.False(ch.Finish());
    }

    private sealed class FramePromiseHandler : ChannelOutboundHandlerAdapter
    {
        private readonly List<TaskCompletionSource> framePromises;

        internal FramePromiseHandler(List<TaskCompletionSource> framePromises)
        {
            this.framePromises = framePromises;
        }

        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            TaskCompletionSource promise = new TaskCompletionSource();
            framePromises.Add(promise);
            _ = ctx.WriteAsync(msg, cancellationToken);
            return promise.Task;
        }
    }

    [Fact]
    public void Decode100ContinueHttp2HeadersAsFullHttpResponse()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(false));
        IHttp3Headers headers = new DefaultHttp3Headers();
        headers.Scheme(HttpScheme.HTTP.Name());
        headers.Status(HttpResponseStatus.CONTINUE.CodeAsText());

        Assert.True(ch.WriteInbound(new DefaultHttp3HeadersFrame(headers)));

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

    [Fact]
    public void TestDecodeResponseHeaders()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(false));
        IHttp3Headers headers = new DefaultHttp3Headers();
        headers.Scheme(HttpScheme.HTTP.Name());
        headers.Status(HttpResponseStatus.OK.CodeAsText());

        Assert.True(ch.WriteInbound(new DefaultHttp3HeadersFrame(headers)));

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
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(false));
        IHttp3Headers headers = new DefaultHttp3Headers();
        headers.Scheme(HttpScheme.HTTP.Name());
        headers.Status(HttpResponseStatus.OK.CodeAsText());
        headers.SetInt(Cs("content-length"), 0);

        Assert.True(ch.WriteInbound(new DefaultHttp3HeadersFrame(headers)));

        IHttpResponse response = ch.ReadInbound<IHttpResponse>();
        Assert.Equal(HttpResponseStatus.OK, response.Status());
        Assert.Equal(HttpVersion.HTTP_1_1, response.ProtocolVersion());
        Assert.False(response is IFullHttpResponse);
        Assert.False(HttpUtil.IsTransferEncodingChunked(response));

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestDecodeResponseTrailersAsClient()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(false));
        IHttp3Headers headers = new DefaultHttp3Headers();
        headers.Set(Cs("key"), Cs("value"));
        Assert.True(ch.WriteInboundWithFin(new DefaultHttp3HeadersFrame(headers)));

        ILastHttpContent trailers = ch.ReadInbound<ILastHttpContent>();

        try
        {
            Assert.Equal(0, trailers.Content.ReadableBytes);
            Assert.Equal("value", trailers.TrailingHeaders().Get("key"));
            Assert.False(trailers is IFullHttpRequest);
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
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(false));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        Assert.True(ch.WriteInbound(new DefaultHttp3DataFrame(hello)));

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
    public void TestDecodeEndDataAsClient()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(false));
        ByteBuf hello = Unpooled.CopiedBuffer("hello world", Encoding.UTF8);
        Assert.True(ch.WriteInboundWithFin(new DefaultHttp3DataFrame(hello)));

        IHttpContent content = ch.ReadInbound<IHttpContent>();

        try
        {
            Assert.Equal("hello world", content.Content.ToString(Encoding.UTF8));
        }
        finally
        {
            content.Release();
        }

        ILastHttpContent last = ch.ReadInbound<ILastHttpContent>();

        try
        {
            Assert.False(last.Content.IsReadable());
            Assert.True(last.TrailingHeaders().IsEmpty());
        }
        finally
        {
            last.Release();
        }

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestHostTranslated()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(false));
        IFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/hello/world");
        request.Headers().Add(HttpHeaderNames.HOST, "example.com");
        Assert.True(ch.WriteOutbound(request));

        IHttp3HeadersFrame headersFrame = ch.ReadOutbound<IHttp3HeadersFrame>();
        IHttp3Headers headers = headersFrame.Headers();

        Assert.Equal("https", headers.Scheme().ToString());
        Assert.Equal("example.com", headers.Authority().ToString());
        Assert.True(ch.IsOutputShutdown);

        Assert.False(ch.Finish());
    }

    [Fact]
    public async Task MultipleFramesInFin()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());

        try
        {
            // CLR: the QuicEndpointChannel takes the place of NioDatagramChannel (see the class comment).
            Bootstrap bootstrap = new Bootstrap()
                    .Channel<QuicEndpointChannel>()
                    .Handler(new NoopChannelInitializer())
                    .Group(group);

            SelfSignedCertificate cert = new SelfSignedCertificate();

            // CLR: the endpoint binds with its codec as the handler (see the class comment).
            IChannelHandler serverCodec = Http3.NewQuicServerCodecBuilder()
                    .InitialMaxData(10000000)
                    .InitialMaxStreamDataBidirectionalLocal(1000000)
                    .InitialMaxStreamDataBidirectionalRemote(1000000)
                    .InitialMaxStreamsBidirectional(100)
                    .SslContext(QuicSslContextBuilder.ForServer(cert.Key(), null, cert.Cert())
                            .ApplicationProtocols(Http3.SupportedApplicationProtocols()).Build())
                    .TokenHandler(InsecureQuicTokenHandler.INSTANCE)
                    .Handler(new ServerConnectionInitializer())
                    .Build();
            IChannel server = await bootstrap.Clone().Handler(serverCodec).BindAsync("127.0.0.1", 0, cancellationToken);

            IChannelHandler clientCodec = Http3.NewQuicClientCodecBuilder()
                    .InitialMaxData(10000000)
                    .InitialMaxStreamDataBidirectionalLocal(1000000)
                    .SslContext(QuicSslContextBuilder.ForClient()
                            .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                            .ApplicationProtocols(Http3.SupportedApplicationProtocols())
                            .Build())
                    .Build();
            IChannel client = await bootstrap.Clone().Handler(clientCodec).BindAsync("127.0.0.1", 0, cancellationToken);
            client.Config.SetAutoRead(true);

            IQuicChannel quicChannel = await IQuicChannel.NewBootstrap(client)
                    .Handler(new ClientConnectionInitializer())
                    .RemoteAddress(server.LocalAddress)
                    .LocalAddress(client.LocalAddress)
                    .ConnectAsync(cancellationToken);

            BlockingCollection<object> received = new BlockingCollection<object>();
            IQuicStreamChannel stream = await Http3.NewRequestStreamAsync(quicChannel,
                    new ReceivingRequestStreamInitializer(received));
            DefaultFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/");
            request.Headers().Add(HttpHeaderNames.HOST, "localhost");
            _ = stream.WriteAndFlushAsync(request, cancellationToken);

            IHttpResponse respHeaders = (IHttpResponse) Poll(received, TimeSpan.FromSeconds(20));
            Assert.NotNull(respHeaders);
            Assert.Equal(HttpResponseStatus.OK, respHeaders.Status());
            Assert.IsNotAssignableFrom<ILastHttpContent>(respHeaders);
            IHttpContent respBody = (IHttpContent) Poll(received, TimeSpan.FromSeconds(20));
            Assert.NotNull(respBody);
            Assert.Equal("foo", respBody.Content.ToString(Encoding.UTF8));
            respBody.Release();

            ILastHttpContent last = (ILastHttpContent) Poll(received, TimeSpan.FromSeconds(20));
            Assert.NotNull(last);
            last.Release();
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    // CLR: BlockingQueue.poll(timeout, unit) (see the class comment).
    private static object Poll(BlockingCollection<object> queue, TimeSpan timeout)
    {
        return queue.TryTake(out object item, timeout) ? item : null;
    }

    private sealed class NoopChannelInitializer : ChannelInitializer<IChannel>
    {
        protected override void InitChannel(IChannel ch)
        {
            // initialized below
        }
    }

    private sealed class ServerConnectionInitializer : ChannelInitializer<IChannel>
    {
        protected override void InitChannel(IChannel ch)
        {
            ch.Pipeline.AddLast(new Http3ServerConnectionHandler(new ResponseHandler()));
        }
    }

    private sealed class ResponseHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is IHttp3HeadersFrame)
            {
                DefaultHttp3HeadersFrame responseHeaders = new DefaultHttp3HeadersFrame();
                responseHeaders.Headers().Status(HttpResponseStatus.OK.CodeAsText());
                _ = ctx.WriteAsync(responseHeaders);
                _ = ctx.WriteAsync(new DefaultHttp3DataFrame(ByteBufUtil.EncodeString(
                        ctx.Alloc(), "foo".AsSpan(), Encoding.UTF8)));
                // send a fin, this also flushes
                _ = ((IDuplexChannel) ctx.Channel).ShutdownOutputAsync();
            }
            else
            {
                base.ChannelRead(ctx, msg);
            }
        }
    }

    private sealed class ClientConnectionInitializer : ChannelInitializer<IQuicChannel>
    {
        protected override void InitChannel(IQuicChannel ch)
        {
            ch.Pipeline.AddLast(new Http3ClientConnectionHandler());
        }
    }

    private sealed class ReceivingRequestStreamInitializer : Http3RequestStreamInitializer
    {
        private readonly BlockingCollection<object> received;

        internal ReceivingRequestStreamInitializer(BlockingCollection<object> received)
        {
            this.received = received;
        }

        protected override void InitRequestStream(IQuicStreamChannel ch)
        {
            ch.Pipeline
                    .AddLast(new Http3FrameToHttpObjectCodec(false))
                    .AddLast(new ReceivingHandler(received));
        }
    }

    private sealed class ReceivingHandler : ChannelInboundHandlerAdapter
    {
        private readonly BlockingCollection<object> received;

        internal ReceivingHandler(BlockingCollection<object> received)
        {
            this.received = received;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            received.Add(msg);
        }
    }

    // CLR: io.netty.handler.ssl.util.SelfSignedCertificate, reduced to key() and cert() (see the class comment).
    private sealed class SelfSignedCertificate
    {
        private readonly ECDsa key;
        private readonly X509Certificate2 cert;

        internal SelfSignedCertificate()
        {
            key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            CertificateRequest request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
            SubjectAlternativeNameBuilder san = new SubjectAlternativeNameBuilder();
            san.AddDnsName("localhost");
            request.CertificateExtensions.Add(san.Build());
            using X509Certificate2 selfSigned = request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            cert = X509CertificateLoader.LoadCertificate(selfSigned.RawData);
        }

        internal AsymmetricAlgorithm Key()
        {
            return key;
        }

        internal X509Certificate2 Cert()
        {
            return cert;
        }
    }

    [Fact]
    public void TestUnsupportedIncludeSomeDetails()
    {
        EmbeddedQuicStreamChannel ch = new EmbeddedQuicStreamChannel(new Http3FrameToHttpObjectCodec(false));
        UnsupportedMessageTypeException ex = Assert.ThrowsAny<UnsupportedMessageTypeException>(
                () => ch.WriteOutbound("unsupported"));
        Assert.NotNull(ex.Message);
        Assert.False(ch.Finish());
    }
}
