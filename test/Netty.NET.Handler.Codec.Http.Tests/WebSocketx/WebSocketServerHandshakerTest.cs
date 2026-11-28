/*
 * Copyright 2020 The Netty Project
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

using System.Text;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx;

// CLR: the protected newHandshakeResponse(...) that the Java test calls through package access is reached through the
// internal forwarder InvokeNewHandshakeResponse(...), and the package-private WebSocketVersion.toAsciiString() is
// internal. handshake(...) is HandshakeAsync(...), whose Task replaces the ChannelFuture (isDone() is IsCompleted); the
// null ChannelPromise argument is a null TaskCompletionSource. HttpHeaders is an IEnumerable of its entries, so the
// header equality assertion calls Equals, as Java's assertEquals does (core's DefaultHttpHeadersTest). A String value
// that the Java test passes to containsValue(CharSequence, CharSequence, boolean) is wrapped in the Common
// StringCharSequence. The anonymous Executable is a lambda for Assert.ThrowsAny (JUnit's assertThrows accepts
// subtypes), getMessage() is Message, CharsetUtil.US_ASCII is Encoding.ASCII, and readInbound()/readOutbound() are
// ReadInbound<T>()/ReadOutbound<T>(). The three tests that WebSocketServerHandshaker00Test overrides with empty bodies
// run their body from a protected virtual method with the suffix 0, which the derived class overrides.
public abstract class WebSocketServerHandshakerTest
{

    protected abstract WebSocketServerHandshaker NewHandshaker(string webSocketURL, string subprotocols,
                                                               WebSocketDecoderConfig decoderConfig);

    protected abstract WebSocketVersion WebSocketVersion();

    [Fact]
    public void TestDuplicateHandshakeResponseHeaders()
    {
        WebSocketServerHandshaker serverHandshaker = NewHandshaker("ws://example.com/chat",
                                                                   "chat", WebSocketDecoderConfig.DEFAULT);
        IFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/chat");
        request.Headers()
               .Set(HttpHeaderNames.HOST, "example.com")
               .Set(HttpHeaderNames.ORIGIN, "example.com")
               .Set(HttpHeaderNames.UPGRADE, HttpHeaderValues.WEBSOCKET)
               .Set(HttpHeaderNames.CONNECTION, HttpHeaderValues.UPGRADE)
               .Set(HttpHeaderNames.SEC_WEBSOCKET_KEY, "dGhlIHNhbXBsZSBub25jZQ==")
               .Set(HttpHeaderNames.SEC_WEBSOCKET_ORIGIN, "http://example.com")
               .Set(HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL, "chat, superchat")
               .Set(HttpHeaderNames.WEBSOCKET_PROTOCOL, "chat, superchat")
               .Set(HttpHeaderNames.SEC_WEBSOCKET_VERSION, WebSocketVersion().ToAsciiString());
        HttpHeaders customResponseHeaders = new DefaultHttpHeaders();
        // set duplicate required headers and one custom
        customResponseHeaders
                .Set(HttpHeaderNames.CONNECTION, HttpHeaderValues.UPGRADE)
                .Set(HttpHeaderNames.UPGRADE, HttpHeaderValues.WEBSOCKET)
                .Set(HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL, "superchat")
                .Set(HttpHeaderNames.WEBSOCKET_PROTOCOL, "superchat")
                .Set("custom", "header");

        if (WebSocketVersion() != Http.WebSocketx.WebSocketVersion.V00)
        {
            customResponseHeaders.Set(HttpHeaderNames.SEC_WEBSOCKET_ACCEPT, "12345");
        }

        IFullHttpResponse response = null;

        try
        {
            response = serverHandshaker.InvokeNewHandshakeResponse(request, customResponseHeaders);
            HttpHeaders responseHeaders = response.Headers();

            Assert.Single(responseHeaders.GetAll(HttpHeaderNames.CONNECTION));
            Assert.Single(responseHeaders.GetAll(HttpHeaderNames.UPGRADE));
            Assert.True(responseHeaders.ContainsValue(new StringCharSequence("custom"),
                                                      new StringCharSequence("header"), true));

            if (WebSocketVersion() != Http.WebSocketx.WebSocketVersion.V00)
            {
                Assert.False(responseHeaders.ContainsValue(HttpHeaderNames.SEC_WEBSOCKET_ACCEPT,
                                                           new StringCharSequence("12345"), false));
                Assert.Single(responseHeaders.GetAll(HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL));
                Assert.Equal("chat", responseHeaders.Get(HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL));
            }
            else
            {
                Assert.Single(responseHeaders.GetAll(HttpHeaderNames.WEBSOCKET_PROTOCOL));
                Assert.Equal("chat", responseHeaders.Get(HttpHeaderNames.WEBSOCKET_PROTOCOL));
            }
        }
        finally
        {
            request.Release();

            if (response != null)
            {
                response.Release();
            }
        }
    }

    [Fact]
    public void TestWebSocketServerHandshakeException()
    {
        WebSocketServerHandshaker serverHandshaker = NewHandshaker("ws://example.com/chat",
                                                                   "chat", WebSocketDecoderConfig.DEFAULT);

        IFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET,
                                                             "ws://example.com/chat");
        request.Headers().Set("x-client-header", "value");

        try
        {
            _ = serverHandshaker.HandshakeAsync((IChannel) null, request, null, null);
        }
        catch (WebSocketServerHandshakeException exception)
        {
            Assert.NotNull(exception.Message);
            Assert.True(request.Headers().Equals(exception.Request().Headers()));
            Assert.Equal(HttpMethod.GET, exception.Request().Method());
        }
        finally
        {
            request.Release();
        }
    }

    [Fact]
    public void TestHandshakeForHttpRequestWithoutAggregator()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestDecoder(), new HttpResponseEncoder());
        WebSocketServerHandshaker serverHandshaker = NewHandshaker("ws://example.com/chat",
                                                                   "chat", WebSocketDecoderConfig.DEFAULT);

        IHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/chat");
        request.Headers()
               .Set(HttpHeaderNames.HOST, "example.com")
               .Set(HttpHeaderNames.ORIGIN, "example.com")
               .Set(HttpHeaderNames.UPGRADE, HttpHeaderValues.WEBSOCKET)
               .Set(HttpHeaderNames.CONNECTION, HttpHeaderValues.UPGRADE)
               .Set(HttpHeaderNames.SEC_WEBSOCKET_KEY, "dGhlIHNhbXBsZSBub25jZQ==")
               .Set(HttpHeaderNames.SEC_WEBSOCKET_ORIGIN, "http://example.com")
               .Set(HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL, "chat, superchat")
               .Set(HttpHeaderNames.SEC_WEBSOCKET_KEY1, "4 @1  46546xW%0l 1 5")
               .Set(HttpHeaderNames.SEC_WEBSOCKET_KEY2, "12998 5 Y3 1  .P00")
               .Set(HttpHeaderNames.WEBSOCKET_PROTOCOL, "chat, superchat")
               .Set(HttpHeaderNames.SEC_WEBSOCKET_VERSION, WebSocketVersion().ToAsciiString());

        Task future = serverHandshaker.HandshakeAsync(channel, request);
        Assert.False(future.IsCompleted);
        Assert.NotNull(channel.Pipeline.Get("handshaker"));

        if (WebSocketVersion() != Http.WebSocketx.WebSocketVersion.V00)
        {
            Assert.Null(channel.Pipeline.Get("httpAggregator"));
            channel.WriteInbound(ILastHttpContent.EMPTY_LAST_CONTENT);
        }
        else
        {
            Assert.NotNull(channel.Pipeline.Get("httpAggregator"));
            channel.WriteInbound(new DefaultLastHttpContent(
                    Unpooled.CopiedBuffer("^n:ds[4U", Encoding.ASCII)));
        }

        Assert.True(future.IsCompleted);
        Assert.Null(channel.Pipeline.Get("handshaker"));

        ByteBuf byteBuf = channel.ReadOutbound<ByteBuf>();
        Assert.False(channel.Finish());

        channel = new EmbeddedChannel(new HttpResponseDecoder());
        Assert.True(channel.WriteInbound(byteBuf));

        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.Equal(SWITCHING_PROTOCOLS, response.Status());
        Assert.True(response.Headers().ContainsValue(HttpHeaderNames.UPGRADE, HttpHeaderValues.WEBSOCKET, true));

        ILastHttpContent lastHttpContent = channel.ReadInbound<ILastHttpContent>();

        if (WebSocketVersion() != Http.WebSocketx.WebSocketVersion.V00)
        {
            Assert.Equal(ILastHttpContent.EMPTY_LAST_CONTENT, lastHttpContent);
        }
        else
        {
            Assert.Equal("8jKS'y:G*Co,Wxa-", lastHttpContent.Content.ToString(Encoding.ASCII));
            Assert.True(lastHttpContent.Release());
        }

        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestHandshakeExceptionWhenConnectionHeaderIsAbsent()
    {
        TestHandshakeExceptionWhenConnectionHeaderIsAbsent0();
    }

    // CLR: the body of testHandshakeExceptionWhenConnectionHeaderIsAbsent, which WebSocketServerHandshaker00Test
    // overrides.
    protected virtual void TestHandshakeExceptionWhenConnectionHeaderIsAbsent0()
    {
        WebSocketServerHandshaker serverHandshaker = NewHandshaker("ws://example.com/chat",
                "chat", WebSocketDecoderConfig.DEFAULT);
        IFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET,
                "ws://example.com/chat");
        request.Headers()
                .Set(HttpHeaderNames.HOST, "server.example.com")
                .Set(HttpHeaderNames.UPGRADE, HttpHeaderValues.WEBSOCKET)
                .Set(HttpHeaderNames.SEC_WEBSOCKET_KEY, "dGhlIHNhbXBsZSBub25jZQ==")
                .Set(HttpHeaderNames.SEC_WEBSOCKET_ORIGIN, "http://example.com")
                .Set(HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL, "chat, superchat")
                .Set(HttpHeaderNames.SEC_WEBSOCKET_VERSION, "13");
        WebSocketServerHandshakeException exception = Assert.ThrowsAny<WebSocketServerHandshakeException>(() =>
        {
            _ = serverHandshaker.HandshakeAsync((IChannel) null, request, null, null);
        });

        Assert.Equal("not a WebSocket request: a |Connection| header must include a token 'Upgrade'",
                exception.Message);
        Assert.True(request.Release());
    }

    [Fact]
    public void TestHandshakeExceptionWhenInvalidConnectionHeader()
    {
        TestHandshakeExceptionWhenInvalidConnectionHeader0();
    }

    // CLR: the body of testHandshakeExceptionWhenInvalidConnectionHeader, which WebSocketServerHandshaker00Test
    // overrides.
    protected virtual void TestHandshakeExceptionWhenInvalidConnectionHeader0()
    {
        WebSocketServerHandshaker serverHandshaker = NewHandshaker("ws://example.com/chat",
                "chat", WebSocketDecoderConfig.DEFAULT);
        IFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET,
                "ws://example.com/chat");
        request.Headers()
                .Set(HttpHeaderNames.HOST, "server.example.com")
                .Set(HttpHeaderNames.CONNECTION, "close")
                .Set(HttpHeaderNames.UPGRADE, HttpHeaderValues.WEBSOCKET)
                .Set(HttpHeaderNames.SEC_WEBSOCKET_KEY, "dGhlIHNhbXBsZSBub25jZQ==")
                .Set(HttpHeaderNames.SEC_WEBSOCKET_ORIGIN, "http://example.com")
                .Set(HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL, "chat, superchat")
                .Set(HttpHeaderNames.SEC_WEBSOCKET_VERSION, "13");
        WebSocketServerHandshakeException exception = Assert.ThrowsAny<WebSocketServerHandshakeException>(() =>
        {
            _ = serverHandshaker.HandshakeAsync((IChannel) null, request, null, null);
        });

        Assert.Equal("not a WebSocket request: a |Connection| header must include a token 'Upgrade'",
                exception.Message);
        Assert.True(request.Release());
    }

    [Fact]
    public void TestHandshakeExceptionWhenInvalidUpgradeHeader()
    {
        TestHandshakeExceptionWhenInvalidUpgradeHeader0();
    }

    // CLR: the body of testHandshakeExceptionWhenInvalidUpgradeHeader, which WebSocketServerHandshaker00Test overrides.
    protected virtual void TestHandshakeExceptionWhenInvalidUpgradeHeader0()
    {
        WebSocketServerHandshaker serverHandshaker = NewHandshaker("ws://example.com/chat",
                "chat", WebSocketDecoderConfig.DEFAULT);
        IFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET,
                "ws://example.com/chat");
        request.Headers()
                .Set(HttpHeaderNames.HOST, "server.example.com")
                .Set(HttpHeaderNames.CONNECTION, HttpHeaderValues.UPGRADE)
                .Set(HttpHeaderNames.UPGRADE, "my_websocket")
                .Set(HttpHeaderNames.SEC_WEBSOCKET_KEY, "dGhlIHNhbXBsZSBub25jZQ==")
                .Set(HttpHeaderNames.SEC_WEBSOCKET_ORIGIN, "http://example.com")
                .Set(HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL, "chat, superchat")
                .Set(HttpHeaderNames.SEC_WEBSOCKET_VERSION, "13");
        WebSocketServerHandshakeException exception = Assert.ThrowsAny<WebSocketServerHandshakeException>(() =>
        {
            _ = serverHandshaker.HandshakeAsync((IChannel) null, request, null, null);
        });

        Assert.Equal("not a WebSocket request: an |Upgrade| header must containing the value 'websocket'",
                exception.Message);
        Assert.True(request.Release());
    }
}
