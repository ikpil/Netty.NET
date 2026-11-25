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
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;
using static Netty.NET.Handler.Codec.Http.WebSocketx.WebSocketServerHandshaker13;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx;

// CLR: java.net.URI is System.Uri (see WebSocketClientHandshaker): URI.create(s) is the private helper Create, new
// Uri(s, UriKind.RelativeOrAbsolute), which accepts the relative references of the source. CharSequence is
// ICharSequence (the HttpHeaderNames constants are AsciiString). The protected newHandshakeRequest(),
// newWebsocketDecoder() and newWebSocketEncoder() that the Java tests call through package access are reached through
// the internal forwarders InvokeNewHandshakeRequest(), InvokeNewWebsocketDecoder() and InvokeNewWebSocketEncoder(); the
// anonymous WebSocketClientHandshaker and SimpleChannelInboundHandler subclasses are the named nested classes
// DelegatingHandshaker and FinishHandshakeHandler. ChannelFuture is Task: isSuccess() is IsCompletedSuccessfully,
// isDone() is IsCompleted, cause() is the InnerException of Task.Exception, and syncUninterruptibly() is
// GetAwaiter().GetResult(). The deprecated getUri() used by testUpgradeUrl is not ported (core, getUri() names uri()),
// so the test reads Uri(). @Timeout(3000 ms) is the xUnit Timeout of [Fact], which applies to async tests only, so the
// two timed tests run the body with Task.Run and return that Task. CompositeByteBuf addComponent(true, buf) is
// AddComponent(buf, true) (Buffer port parameter order). assertThrows is Assert.ThrowsAny, assertInstanceOf is
// Assert.IsAssignableFrom, getMessage() is Message, String.getBytes(US_ASCII) is Encoding.ASCII.GetBytes and
// readInbound()/readOutbound() are ReadInbound<T>()/ReadOutbound<T>(). xUnit refuses a test method with overloads
// (https://xunit.net/xunit.analyzers/rules/xUnit1024), so the private testHttpResponseAndFrameInSameBuffer(boolean)
// helper takes the suffix 0, as core's HttpResponseDecoderTest does, and assertEquals(1, list.size()) is Assert.Single
// (https://xunit.net/xunit.analyzers/rules/xUnit2013).
public abstract class WebSocketClientHandshakerTest
{
    protected abstract WebSocketClientHandshaker NewHandshaker(Uri uri, string subprotocol, HttpHeaders headers,
                                                               bool absoluteUpgradeUrl,
                                                               bool generateOriginHeader);

    protected WebSocketClientHandshaker NewHandshaker(Uri uri)
    {
        return NewHandshaker(uri, null, null, false, true);
    }

    protected abstract ICharSequence GetOriginHeaderName();

    protected abstract ICharSequence GetProtocolHeaderName();

    protected abstract ICharSequence[] GetHandshakeRequiredHeaderNames();

    private static Uri Create(string uri)
    {
        return new Uri(uri, UriKind.RelativeOrAbsolute);
    }

    [Fact]
    public void HostHeaderWs()
    {
        foreach (string scheme in new string[]{"ws://", "http://"})
        {
            foreach (string host in new string[]{"localhost", "127.0.0.1", "[::1]", "Netty.io"})
            {
                string enter = scheme + host;

                TestHostHeader(enter, host);
                TestHostHeader(enter + '/', host);
                TestHostHeader(enter + ":80", host);
                TestHostHeader(enter + ":443", host + ":443");
                TestHostHeader(enter + ":9999", host + ":9999");
                TestHostHeader(enter + "/path", host);
                TestHostHeader(enter + ":80/path", host);
                TestHostHeader(enter + ":443/path", host + ":443");
                TestHostHeader(enter + ":9999/path", host + ":9999");
            }
        }
    }

    [Fact]
    public void HostHeaderWss()
    {
        foreach (string scheme in new string[]{"wss://", "https://"})
        {
            foreach (string host in new string[]{"localhost", "127.0.0.1", "[::1]", "Netty.io"})
            {
                string enter = scheme + host;

                TestHostHeader(enter, host);
                TestHostHeader(enter + '/', host);
                TestHostHeader(enter + ":80", host + ":80");
                TestHostHeader(enter + ":443", host);
                TestHostHeader(enter + ":9999", host + ":9999");
                TestHostHeader(enter + "/path", host);
                TestHostHeader(enter + ":80/path", host + ":80");
                TestHostHeader(enter + ":443/path", host);
                TestHostHeader(enter + ":9999/path", host + ":9999");
            }
        }
    }

    [Fact]
    public void HostHeaderWithoutScheme()
    {
        TestHostHeader("//localhost/", "localhost");
        TestHostHeader("//localhost/path", "localhost");
        TestHostHeader("//localhost:80/", "localhost:80");
        TestHostHeader("//localhost:443/", "localhost:443");
        TestHostHeader("//localhost:9999/", "localhost:9999");
    }

    [Fact]
    public void OriginHeaderWs()
    {
        foreach (string scheme in new string[]{"ws://", "http://"})
        {
            foreach (string host in new string[]{"localhost", "127.0.0.1", "[::1]", "NETTY.IO"})
            {
                string enter = scheme + host;
                string expect = "http://" + host.ToLowerInvariant();

                TestOriginHeader(enter, expect);
                TestOriginHeader(enter + '/', expect);
                TestOriginHeader(enter + ":80", expect);
                TestOriginHeader(enter + ":443", expect + ":443");
                TestOriginHeader(enter + ":9999", expect + ":9999");
                TestOriginHeader(enter + "/path%20with%20ws", expect);
                TestOriginHeader(enter + ":80/path%20with%20ws", expect);
                TestOriginHeader(enter + ":443/path%20with%20ws", expect + ":443");
                TestOriginHeader(enter + ":9999/path%20with%20ws", expect + ":9999");
            }
        }
    }

    [Fact]
    public void OriginHeaderWss()
    {
        foreach (string scheme in new string[]{"wss://", "https://"})
        {
            foreach (string host in new string[]{"localhost", "127.0.0.1", "[::1]", "NETTY.IO"})
            {
                string enter = scheme + host;
                string expect = "https://" + host.ToLowerInvariant();

                TestOriginHeader(enter, expect);
                TestOriginHeader(enter + '/', expect);
                TestOriginHeader(enter + ":80", expect + ":80");
                TestOriginHeader(enter + ":443", expect);
                TestOriginHeader(enter + ":9999", expect + ":9999");
                TestOriginHeader(enter + "/path%20with%20ws", expect);
                TestOriginHeader(enter + ":80/path%20with%20ws", expect + ":80");
                TestOriginHeader(enter + ":443/path%20with%20ws", expect);
                TestOriginHeader(enter + ":9999/path%20with%20ws", expect + ":9999");
            }
        }
    }

    [Fact]
    public void OriginHeaderWithoutScheme()
    {
        TestOriginHeader("//localhost/", "http://localhost");
        TestOriginHeader("//localhost/path", "http://localhost");

        // http scheme by port
        TestOriginHeader("//localhost:80/", "http://localhost");
        TestOriginHeader("//localhost:80/path", "http://localhost");

        // https scheme by port
        TestOriginHeader("//localhost:443/", "https://localhost");
        TestOriginHeader("//localhost:443/path", "https://localhost");

        // http scheme for non standard port
        TestOriginHeader("//localhost:9999/", "http://localhost:9999");
        TestOriginHeader("//localhost:9999/path", "http://localhost:9999");

        // convert host to lower case
        TestOriginHeader("//LOCALHOST/", "http://localhost");
    }

    [Fact]
    public void TestSetOriginFromCustomHeaders()
    {
        HttpHeaders customHeaders = new DefaultHttpHeaders().Set(GetOriginHeaderName(), "http://example.com");
        WebSocketClientHandshaker handshaker = NewHandshaker(Create("ws://server.example.com/chat"), null,
                                                             customHeaders, false, true);
        IFullHttpRequest request = handshaker.InvokeNewHandshakeRequest();
        try
        {
            Assert.Equal("http://example.com", request.Headers().Get(GetOriginHeaderName()));
        }
        finally
        {
            request.Release();
        }
    }

    [Fact]
    public void TestOriginHeaderIsAbsentWhenGeneratingDisable()
    {
        Uri uri = Create("http://example.com/ws");
        WebSocketClientHandshaker handshaker = NewHandshaker(uri, null, null, false, false);
        IFullHttpRequest request = handshaker.InvokeNewHandshakeRequest();
        try
        {
            Assert.False(request.Headers().Contains(GetOriginHeaderName()));
            Assert.Equal("/ws", request.Uri());
        }
        finally
        {
            request.Release();
        }
    }

    [Fact]
    public void TestInvalidHostWhenIncorrectWebSocketURI()
    {
        Uri uri = Create("/ws");
        EmbeddedChannel channel = new EmbeddedChannel(new HttpClientCodec());
        WebSocketClientHandshaker handshaker = NewHandshaker(uri, null, null, false, true);
        Task handshakeFuture = handshaker.HandshakeAsync(channel);

        Assert.False(handshakeFuture.IsCompletedSuccessfully);
        Assert.IsAssignableFrom<ArgumentException>(handshakeFuture.Exception.InnerException);
        Assert.Equal("Cannot generate the 'host' header value, webSocketURI should contain host" +
                     " or passed through customHeaders", handshakeFuture.Exception.InnerException.Message);
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestInvalidOriginWhenIncorrectWebSocketURI()
    {
        Uri uri = Create("/ws");
        EmbeddedChannel channel = new EmbeddedChannel(new HttpClientCodec());
        HttpHeaders headers = new DefaultHttpHeaders();
        headers.Set(HttpHeaderNames.HOST, "localhost:80");
        WebSocketClientHandshaker handshaker = NewHandshaker(uri, null, headers, false, true);
        Task handshakeFuture = handshaker.HandshakeAsync(channel);

        Assert.False(handshakeFuture.IsCompletedSuccessfully);
        Assert.IsAssignableFrom<ArgumentException>(handshakeFuture.Exception.InnerException);
        Assert.Equal("Cannot generate the '" + GetOriginHeaderName() + "' header value," +
                     " webSocketURI should contain host or disable generateOriginHeader" +
                     " or pass value through customHeaders", handshakeFuture.Exception.InnerException.Message);
        Assert.False(channel.Finish());
    }

    private void TestHostHeader(string uri, string expected)
    {
        TestHeaderDefaultHttp(uri, HttpHeaderNames.HOST, expected);
    }

    private void TestOriginHeader(string uri, string expected)
    {
        TestHeaderDefaultHttp(uri, GetOriginHeaderName(), expected);
    }

    protected void TestHeaderDefaultHttp(string uri, ICharSequence header, string expectedValue)
    {
        WebSocketClientHandshaker handshaker = NewHandshaker(Create(uri));
        IFullHttpRequest request = handshaker.InvokeNewHandshakeRequest();
        try
        {
            Assert.Equal(expectedValue, request.Headers().Get(header));
        }
        finally
        {
            request.Release();
        }
    }

    [Fact]
    public void TestUpgradeUrl()
    {
        Uri uri = Create("ws://localhost:9999/path%20with%20ws");
        WebSocketClientHandshaker handshaker = NewHandshaker(uri);
        IFullHttpRequest request = handshaker.InvokeNewHandshakeRequest();
        try
        {
            Assert.Equal("/path%20with%20ws", request.Uri());
        }
        finally
        {
            request.Release();
        }
    }

    [Fact]
    public void TestUpgradeUrlWithQuery()
    {
        Uri uri = Create("ws://localhost:9999/path%20with%20ws?a=b%20c");
        WebSocketClientHandshaker handshaker = NewHandshaker(uri);
        IFullHttpRequest request = handshaker.InvokeNewHandshakeRequest();
        try
        {
            Assert.Equal("/path%20with%20ws?a=b%20c", request.Uri());
        }
        finally
        {
            request.Release();
        }
    }

    [Fact]
    public void TestUpgradeUrlWithoutPath()
    {
        Uri uri = Create("ws://localhost:9999");
        WebSocketClientHandshaker handshaker = NewHandshaker(uri);
        IFullHttpRequest request = handshaker.InvokeNewHandshakeRequest();
        try
        {
            Assert.Equal("/", request.Uri());
        }
        finally
        {
            request.Release();
        }
    }

    [Fact]
    public void TestUpgradeUrlWithoutPathWithQuery()
    {
        Uri uri = Create("ws://localhost:9999?a=b%20c");
        WebSocketClientHandshaker handshaker = NewHandshaker(uri);
        IFullHttpRequest request = handshaker.InvokeNewHandshakeRequest();
        try
        {
            Assert.Equal("/?a=b%20c", request.Uri());
        }
        finally
        {
            request.Release();
        }
    }

    [Fact]
    public void TestAbsoluteUpgradeUrlWithQuery()
    {
        Uri uri = Create("ws://localhost:9999/path%20with%20ws?a=b%20c");
        WebSocketClientHandshaker handshaker = NewHandshaker(uri, null, null, true, true);
        IFullHttpRequest request = handshaker.InvokeNewHandshakeRequest();
        try
        {
            Assert.Equal("ws://localhost:9999/path%20with%20ws?a=b%20c", request.Uri());
        }
        finally
        {
            request.Release();
        }
    }

    [Fact(Timeout = 3000)]
    public Task TestHttpResponseAndFrameInSameBuffer()
    {
        return Task.Run(() => TestHttpResponseAndFrameInSameBuffer0(false), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 3000)]
    public Task TestHttpResponseAndFrameInSameBufferCodec()
    {
        return Task.Run(() => TestHttpResponseAndFrameInSameBuffer0(true), TestContext.Current.CancellationToken);
    }

    private sealed class DelegatingHandshaker : WebSocketClientHandshaker
    {
        private readonly WebSocketClientHandshaker shaker;

        internal DelegatingHandshaker(WebSocketClientHandshaker shaker)
            : base(shaker.Uri(), shaker.Version(), null, EmptyHttpHeaders.INSTANCE, int.MaxValue, -1)
        {
            this.shaker = shaker;
        }

        protected override IFullHttpRequest NewHandshakeRequest()
        {
            return shaker.InvokeNewHandshakeRequest();
        }

        protected override void Verify(IFullHttpResponse response)
        {
            // Not do any verification, so we not need to care sending the correct headers etc in the test,
            // which would just make things more complicated.
        }

        protected override IWebSocketFrameDecoder NewWebsocketDecoder()
        {
            return shaker.InvokeNewWebsocketDecoder();
        }

        protected override IWebSocketFrameEncoder NewWebSocketEncoder()
        {
            return shaker.InvokeNewWebSocketEncoder();
        }
    }

    private sealed class FinishHandshakeHandler : SimpleChannelInboundHandler<IFullHttpResponse>
    {
        private readonly WebSocketClientHandshaker handshaker;

        internal FinishHandshakeHandler(WebSocketClientHandshaker handshaker)
        {
            this.handshaker = handshaker;
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, IFullHttpResponse msg)
        {
            handshaker.FinishHandshake(ctx.Channel, msg);
            ctx.Pipeline.Remove(this);
        }
    }

    private void TestHttpResponseAndFrameInSameBuffer0(bool codec)
    {
        string url = "ws://localhost:9999/ws";
        WebSocketClientHandshaker shaker = NewHandshaker(Create(url));
        WebSocketClientHandshaker handshaker = new DelegatingHandshaker(shaker);

        // use randomBytes helper from utils to check that it functions properly
        byte[] data = WebSocketUtil.RandomBytes(24);

        // Create a EmbeddedChannel which we will use to encode a BinaryWebsocketFrame to bytes and so use these
        // to test the actual handshaker.
        WebSocketServerHandshakerFactory factory = new WebSocketServerHandshakerFactory(url, null, false);
        IFullHttpRequest request = shaker.InvokeNewHandshakeRequest();
        WebSocketServerHandshaker socketServerHandshaker = factory.NewHandshaker(request);
        request.Release();
        EmbeddedChannel websocketChannel = new EmbeddedChannel(socketServerHandshaker.InvokeNewWebSocketEncoder(),
                socketServerHandshaker.InvokeNewWebsocketDecoder());
        Assert.True(websocketChannel.WriteOutbound(new BinaryWebSocketFrame(Unpooled.WrappedBuffer(data))));

        byte[] bytes = Encoding.ASCII.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\nSec-Websocket-Accept: not-verify\r\n" +
                "Upgrade: websocket\r\n\r\n");

        CompositeByteBuf compositeByteBuf = Unpooled.CompositeBuffer();
        compositeByteBuf.AddComponent(Unpooled.WrappedBuffer(bytes), true);
        for (;;)
        {
            ByteBuf frameBytes = websocketChannel.ReadOutbound<ByteBuf>();
            if (frameBytes == null)
            {
                break;
            }
            compositeByteBuf.AddComponent(frameBytes, true);
        }

        EmbeddedChannel ch = new EmbeddedChannel(new HttpObjectAggregator(int.MaxValue),
                new FinishHandshakeHandler(handshaker));
        if (codec)
        {
            ch.Pipeline.AddFirst(new HttpClientCodec());
        }
        else
        {
            ch.Pipeline.AddFirst(new HttpRequestEncoder(), new HttpResponseDecoder());
        }
        // We need to first write the request as HttpClientCodec will fail if we receive a response before a request
        // was written.
        shaker.HandshakeAsync(ch).GetAwaiter().GetResult();
        for (;;)
        {
            // Just consume the bytes, we are not interested in these.
            ByteBuf buf = ch.ReadOutbound<ByteBuf>();
            if (buf == null)
            {
                break;
            }
            buf.Release();
        }
        Assert.True(ch.WriteInbound(compositeByteBuf));
        Assert.True(ch.Finish());

        BinaryWebSocketFrame frame = ch.ReadInbound<BinaryWebSocketFrame>();
        ByteBuf expect = Unpooled.WrappedBuffer(data);
        try
        {
            Assert.Equal(expect, frame.Content);
            Assert.True(frame.IsFinalFragment());
            Assert.Equal(0, frame.Rsv());
        }
        finally
        {
            expect.Release();
            frame.Release();
        }
    }

    [Fact]
    public void TestDuplicateWebsocketHandshakeHeaders()
    {
        Uri uri = Create("ws://localhost:9999/foo");

        HttpHeaders inputHeaders = new DefaultHttpHeaders();
        string bogusSubProtocol = "bogusSubProtocol";
        string bogusHeaderValue = "bogusHeaderValue";

        // add values for the headers that are reserved for use in the websockets handshake
        foreach (ICharSequence header in GetHandshakeRequiredHeaderNames())
        {
            if (!HttpHeaderNames.HOST.Equals(header))
            {
                inputHeaders.Add(header, bogusHeaderValue);
            }
        }
        inputHeaders.Add(GetProtocolHeaderName(), bogusSubProtocol);

        string realSubProtocol = "realSubProtocol";
        WebSocketClientHandshaker handshaker = NewHandshaker(uri, realSubProtocol, inputHeaders, false, true);
        IFullHttpRequest request = handshaker.InvokeNewHandshakeRequest();
        HttpHeaders outputHeaders = request.Headers();

        // the header values passed in originally have been replaced with values generated by the Handshaker
        foreach (ICharSequence header in GetHandshakeRequiredHeaderNames())
        {
            Assert.Single(outputHeaders.GetAll(header));
            Assert.NotEqual(bogusHeaderValue, outputHeaders.Get(header));
        }

        // the subprotocol header value is that of the subprotocol string passed into the Handshaker
        Assert.Single(outputHeaders.GetAll(GetProtocolHeaderName()));
        Assert.Equal(realSubProtocol, outputHeaders.Get(GetProtocolHeaderName()));

        request.Release();
    }

    [Fact]
    public void TestWebSocketClientHandshakeException()
    {
        Uri uri = Create("ws://localhost:9999/exception");
        WebSocketClientHandshaker handshaker = NewHandshaker(uri, null, null, false, true);
        IFullHttpResponse response = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.UNAUTHORIZED);
        response.Headers().Set(HttpHeaderNames.WWW_AUTHENTICATE, "realm = access token required");

        try
        {
            WebSocketClientHandshakeException exception = Assert.ThrowsAny<WebSocketClientHandshakeException>(() =>
                    handshaker.FinishHandshake(null, response));
            Assert.Equal("Invalid handshake response getStatus: 401 Unauthorized", exception.Message);
            Assert.Equal(HttpResponseStatus.UNAUTHORIZED, exception.Response().Status());
            Assert.True(exception.Response().Headers().Contains(HttpHeaderNames.WWW_AUTHENTICATE,
                                                               new StringCharSequence("realm = access token required"),
                                                               false));
        }
        finally
        {
            response.Release();
        }
    }

    [Fact]
    public void TestHandshakeForHttpResponseWithoutAggregator()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestEncoder(), new HttpResponseDecoder());
        Uri uri = Create("ws://localhost:9999/chat");
        WebSocketClientHandshaker clientHandshaker = NewHandshaker(uri);
        IFullHttpRequest handshakeRequest = clientHandshaker.InvokeNewHandshakeRequest();
        handshakeRequest.Release();

        string accept = "";
        if (clientHandshaker.Version() != WebSocketVersion.V00)
        {
            string acceptSeed = handshakeRequest.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_KEY)
                                + WEBSOCKET_13_ACCEPT_GUID;
            byte[] sha1 = WebSocketUtil.Sha1(Encoding.ASCII.GetBytes(acceptSeed));
            accept = WebSocketUtil.Base64(sha1);
        }

        IHttpResponse response = new DefaultHttpResponse(HttpVersion.HTTP_1_1, SWITCHING_PROTOCOLS);
        response.Headers()
                .Set(HttpHeaderNames.UPGRADE, HttpHeaderValues.WEBSOCKET)
                .Set(HttpHeaderNames.CONNECTION, HttpHeaderValues.UPGRADE)
                .Set(HttpHeaderNames.SEC_WEBSOCKET_ACCEPT, accept);

        Task handshakeFuture = clientHandshaker.ProcessHandshakeAsync(channel, response);
        Assert.False(handshakeFuture.IsCompleted);
        Assert.NotNull(channel.Pipeline.Get("handshaker"));

        if (clientHandshaker.Version() != WebSocketVersion.V00)
        {
            Assert.Null(channel.Pipeline.Get("httpAggregator"));
            channel.WriteInbound(ILastHttpContent.EMPTY_LAST_CONTENT);
        }
        else
        {
            Assert.NotNull(channel.Pipeline.Get("httpAggregator"));
            channel.WriteInbound(new DefaultLastHttpContent(
                    Unpooled.CopiedBuffer("8jKS'y:G*Co,Wxa-", Encoding.ASCII)));
        }

        Assert.True(handshakeFuture.IsCompleted);
        Assert.Null(channel.Pipeline.Get("handshaker"));
        Assert.False(channel.Finish());
    }
}
