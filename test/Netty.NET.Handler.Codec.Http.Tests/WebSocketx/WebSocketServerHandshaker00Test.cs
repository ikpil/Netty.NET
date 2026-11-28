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

using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx;

// CLR: The three inherited tests that the Java class overrides with an empty body ("// ignore") override the
// protected virtual bodies (suffix 0) that the base tests call, because the manifest checker requires the [Fact]
// declaration as public void in the base. Inside the class, the method WebSocketVersion() hides the type in
// expressions, so the constant is written Http.WebSocketx.WebSocketVersion.V00.
public class WebSocketServerHandshaker00Test : WebSocketServerHandshakerTest
{

    protected override WebSocketServerHandshaker NewHandshaker(string webSocketURL, string subprotocols,
            WebSocketDecoderConfig decoderConfig)
    {
        return new WebSocketServerHandshaker00(webSocketURL, subprotocols, decoderConfig);
    }

    protected override WebSocketVersion WebSocketVersion()
    {
        return Http.WebSocketx.WebSocketVersion.V00;
    }

    [Fact]
    public void TestPerformOpeningHandshake()
    {
        TestPerformOpeningHandshake0(true);
    }

    [Fact]
    public void TestPerformOpeningHandshakeSubProtocolNotSupported()
    {
        TestPerformOpeningHandshake0(false);
    }

    [Fact]
    public void TestPerformHandshakeWithoutOriginHeader()
    {
        EmbeddedChannel ch = new EmbeddedChannel(
            new HttpObjectAggregator(42), new HttpRequestDecoder(), new HttpResponseEncoder());

        IFullHttpRequest req = new DefaultFullHttpRequest(
            HTTP_1_1, HttpMethod.GET, "/chat", Unpooled.CopiedBuffer("^n:ds[4U", Encoding.ASCII));

        req.Headers().Set(HttpHeaderNames.HOST, "server.example.com");
        req.Headers().Set(HttpHeaderNames.UPGRADE, HttpHeaderValues.WEBSOCKET);
        req.Headers().Set(HttpHeaderNames.CONNECTION, "Upgrade");
        req.Headers().Set(HttpHeaderNames.SEC_WEBSOCKET_KEY1, "4 @1  46546xW%0l 1 5");
        req.Headers().Set(HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL, "chat, superchat");

        WebSocketServerHandshaker00 handshaker00 = new WebSocketServerHandshaker00(
            "ws://example.com/chat", "chat", int.MaxValue);

        try
        {
            WebSocketHandshakeException e = Assert.ThrowsAny<WebSocketHandshakeException>(() =>
            {
                _ = handshaker00.HandshakeAsync(ch, req);
            });
            Assert.Equal("Missing origin header, got only "
                    + "[host, upgrade, connection, sec-websocket-key1, sec-websocket-protocol]",
                e.Message);
        }
        finally
        {
            req.Release();
        }
    }

    private static void TestPerformOpeningHandshake0(bool subProtocol)
    {
        EmbeddedChannel ch = new EmbeddedChannel(
                new HttpObjectAggregator(42), new HttpRequestDecoder(), new HttpResponseEncoder());

        IFullHttpRequest req = new DefaultFullHttpRequest(
                HTTP_1_1, HttpMethod.GET, "/chat", Unpooled.CopiedBuffer("^n:ds[4U", Encoding.ASCII));

        req.Headers().Set(HttpHeaderNames.HOST, "server.example.com");
        req.Headers().Set(HttpHeaderNames.UPGRADE, HttpHeaderValues.WEBSOCKET);
        req.Headers().Set(HttpHeaderNames.CONNECTION, "Upgrade");
        req.Headers().Set(HttpHeaderNames.ORIGIN, "http://example.com");
        req.Headers().Set(HttpHeaderNames.SEC_WEBSOCKET_KEY1, "4 @1  46546xW%0l 1 5");
        req.Headers().Set(HttpHeaderNames.SEC_WEBSOCKET_KEY2, "12998 5 Y3 1  .P00");
        req.Headers().Set(HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL, "chat, superchat");

        if (subProtocol)
        {
            _ = new WebSocketServerHandshaker00(
                    "ws://example.com/chat", "chat", int.MaxValue).HandshakeAsync(ch, req);
        }
        else
        {
            _ = new WebSocketServerHandshaker00(
                    "ws://example.com/chat", null, int.MaxValue).HandshakeAsync(ch, req);
        }

        EmbeddedChannel ch2 = new EmbeddedChannel(new HttpResponseDecoder());
        ch2.WriteInbound(ch.ReadOutbound<ByteBuf>());
        IHttpResponse res = ch2.ReadInbound<IHttpResponse>();

        Assert.Equal("ws://example.com/chat", res.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_LOCATION));

        if (subProtocol)
        {
            Assert.Equal("chat", res.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL));
        }
        else
        {
            Assert.Null(res.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL));
        }

        ILastHttpContent content = ch2.ReadInbound<ILastHttpContent>();

        Assert.Equal("8jKS'y:G*Co,Wxa-", content.Content.ToString(Encoding.ASCII));
        content.Release();
        req.Release();
    }

    protected override void TestHandshakeExceptionWhenConnectionHeaderIsAbsent0()
    {
        // ignore
    }

    protected override void TestHandshakeExceptionWhenInvalidConnectionHeader0()
    {
        // ignore
    }

    protected override void TestHandshakeExceptionWhenInvalidUpgradeHeader0()
    {
        // ignore
    }
}
