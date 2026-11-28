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

using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx;

// CLR: handshake(ch, req) is HandshakeAsync(ch, req) with the Task discarded, as the source discards the ChannelFuture;
// the static import of HttpVersion.* is using static, Integer.MAX_VALUE is int.MaxValue and
// readInbound()/readOutbound() are ReadInbound<T>()/ReadOutbound<T>(). Inside the class, the method WebSocketVersion()
// hides the type in expressions, so the constant is written Http.WebSocketx.WebSocketVersion.V08.
public class WebSocketServerHandshaker08Test : WebSocketServerHandshakerTest
{

    protected override WebSocketServerHandshaker NewHandshaker(string webSocketURL, string subprotocols,
            WebSocketDecoderConfig decoderConfig)
    {
        return new WebSocketServerHandshaker08(webSocketURL, subprotocols, decoderConfig);
    }

    protected override WebSocketVersion WebSocketVersion()
    {
        return Http.WebSocketx.WebSocketVersion.V08;
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

    private static void TestPerformOpeningHandshake0(bool subProtocol)
    {
        EmbeddedChannel ch = new EmbeddedChannel(
                new HttpObjectAggregator(42), new HttpRequestDecoder(), new HttpResponseEncoder());

        IFullHttpRequest req = new DefaultFullHttpRequest(HTTP_1_1, HttpMethod.GET, "/chat");
        req.Headers().Set(HttpHeaderNames.HOST, "server.example.com");
        req.Headers().Set(HttpHeaderNames.UPGRADE, HttpHeaderValues.WEBSOCKET);
        req.Headers().Set(HttpHeaderNames.CONNECTION, "Upgrade");
        req.Headers().Set(HttpHeaderNames.SEC_WEBSOCKET_KEY, "dGhlIHNhbXBsZSBub25jZQ==");
        req.Headers().Set(HttpHeaderNames.SEC_WEBSOCKET_ORIGIN, "http://example.com");
        req.Headers().Set(HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL, "chat, superchat");
        req.Headers().Set(HttpHeaderNames.SEC_WEBSOCKET_VERSION, "8");

        if (subProtocol)
        {
            _ = new WebSocketServerHandshaker08(
                    "ws://example.com/chat", "chat", false, int.MaxValue, false).HandshakeAsync(ch, req);
        }
        else
        {
            _ = new WebSocketServerHandshaker08(
                    "ws://example.com/chat", null, false, int.MaxValue, false).HandshakeAsync(ch, req);
        }

        ByteBuf resBuf = ch.ReadOutbound<ByteBuf>();

        EmbeddedChannel ch2 = new EmbeddedChannel(new HttpResponseDecoder());
        ch2.WriteInbound(resBuf);
        IHttpResponse res = ch2.ReadInbound<IHttpResponse>();

        Assert.Equal(
                "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", res.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_ACCEPT));

        if (subProtocol)
        {
            Assert.Equal("chat", res.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL));
        }
        else
        {
            Assert.Null(res.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL));
        }

        ReferenceCountUtil.Release(res);
        req.Release();
    }
}
