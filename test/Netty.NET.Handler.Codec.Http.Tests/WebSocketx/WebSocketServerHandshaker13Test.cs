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

using System.Collections.Generic;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx;

// CLR: handshake(ch, req) is HandshakeAsync(ch, req) with the Task discarded, as the source discards the ChannelFuture;
// ChannelHandler... is params IChannelHandler[], ReferenceCounted is IReferenceCounted, Iterator<String> of
// subprotocols() is the IEnumerator of the ISet (hasNext()/next() are MoveNext()/Current), assertThrows is
// Assert.ThrowsAny and assertInstanceOf is Assert.IsAssignableFrom. The static import of HttpVersion.HTTP_1_1 is using
// static, Integer.MAX_VALUE is int.MaxValue and readInbound()/readOutbound() are ReadInbound<T>()/ReadOutbound<T>().
// Inside the class, the method WebSocketVersion() hides the type in expressions, so the constant is written
// Http.WebSocketx.WebSocketVersion.V13.
public class WebSocketServerHandshaker13Test : WebSocketServerHandshakerTest
{

    protected override WebSocketServerHandshaker NewHandshaker(string webSocketURL, string subprotocols,
            WebSocketDecoderConfig decoderConfig)
    {
        return new WebSocketServerHandshaker13(webSocketURL, subprotocols, decoderConfig);
    }

    protected override WebSocketVersion WebSocketVersion()
    {
        return Http.WebSocketx.WebSocketVersion.V13;
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
                new HttpObjectAggregator(42), new HttpResponseEncoder(), new HttpRequestDecoder());

        if (subProtocol)
        {
            TestUpgrade0(ch, new WebSocketServerHandshaker13(
                    "ws://example.com/chat", "chat", false, int.MaxValue, false));
        }
        else
        {
            TestUpgrade0(ch, new WebSocketServerHandshaker13(
                    "ws://example.com/chat", null, false, int.MaxValue, false));
        }

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestCloseReasonWithEncoderAndDecoder()
    {
        TestCloseReason0(new HttpResponseEncoder(), new HttpRequestDecoder());
    }

    [Fact]
    public void TestCloseReasonWithCodec()
    {
        TestCloseReason0(new HttpServerCodec());
    }

    private static void TestCloseReason0(params IChannelHandler[] handlers)
    {
        EmbeddedChannel ch = new EmbeddedChannel(
                new HttpObjectAggregator(42));
        ch.Pipeline.AddLast(handlers);
        TestUpgrade0(ch, new WebSocketServerHandshaker13("ws://example.com/chat", "chat",
                WebSocketDecoderConfig.NewBuilder().MaxFramePayloadLength(4).CloseOnProtocolViolation(true).Build()));

        ch.WriteOutbound(new BinaryWebSocketFrame(Unpooled.WrappedBuffer(new byte[8])));
        ByteBuf buffer = ch.ReadOutbound<ByteBuf>();
        Assert.ThrowsAny<CorruptedWebSocketFrameException>(() => ch.WriteInbound(buffer));
        IReferenceCounted closeMessage = ch.ReadOutbound<IReferenceCounted>();
        Assert.IsAssignableFrom<ByteBuf>(closeMessage);
        closeMessage.Release();
        Assert.False(ch.Finish());
    }

    private static void TestUpgrade0(EmbeddedChannel ch, WebSocketServerHandshaker13 handshaker)
    {
        IFullHttpRequest req = new DefaultFullHttpRequest(HTTP_1_1, HttpMethod.GET, "/chat");
        req.Headers().Set(HttpHeaderNames.HOST, "server.example.com");
        req.Headers().Set(HttpHeaderNames.UPGRADE, HttpHeaderValues.WEBSOCKET);
        req.Headers().Set(HttpHeaderNames.CONNECTION, "Upgrade");
        req.Headers().Set(HttpHeaderNames.SEC_WEBSOCKET_KEY, "dGhlIHNhbXBsZSBub25jZQ==");
        req.Headers().Set(HttpHeaderNames.SEC_WEBSOCKET_ORIGIN, "http://example.com");
        req.Headers().Set(HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL, "chat, superchat");
        req.Headers().Set(HttpHeaderNames.SEC_WEBSOCKET_VERSION, "13");

        _ = handshaker.HandshakeAsync(ch, req);

        ByteBuf resBuf = ch.ReadOutbound<ByteBuf>();

        EmbeddedChannel ch2 = new EmbeddedChannel(new HttpResponseDecoder());
        ch2.WriteInbound(resBuf);
        IHttpResponse res = ch2.ReadInbound<IHttpResponse>();

        Assert.Equal(
                "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", res.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_ACCEPT));
        IEnumerator<string> subProtocols = handshaker.Subprotocols().GetEnumerator();
        if (subProtocols.MoveNext())
        {
            Assert.Equal(subProtocols.Current,
                    res.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL));
        }
        else
        {
            Assert.Null(res.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_PROTOCOL));
        }

        ReferenceCountUtil.Release(res);
        req.Release();
    }
}
