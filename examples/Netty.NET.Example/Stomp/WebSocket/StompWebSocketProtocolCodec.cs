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
using System.Collections.Generic;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Handler.Codec.Stomp;
using Netty.NET.Transport.Channels;
using HandshakeComplete = Netty.NET.Handler.Codec.Http.WebSocketx.WebSocketServerProtocolHandler.HandshakeComplete;

namespace Netty.NET.Example.Stomp.WebSocket;

// CLR: @Sharable is the [Sharable] attribute. The import of the nested WebSocketServerProtocolHandler.HandshakeComplete
// is a using alias. MessageToMessageCodec<WebSocketFrame, StompSubframe> takes the Java interface StompSubframe as
// IStompSubframe, and List<Object> is IList<object>. ctx.channel().attr(key).set is Attr(key).Set, the pipeline's
// addLast(...).remove(handler) chain is AddLast and Remove, which return the pipeline, and close() discards its Task as
// Java discards the future.
[Sharable]
public class StompWebSocketProtocolCodec : MessageToMessageCodec<WebSocketFrame, IStompSubframe>
{

    private readonly StompChatHandler stompChatHandler = new StompChatHandler();
    private readonly StompWebSocketFrameEncoder stompWebSocketFrameEncoder = new StompWebSocketFrameEncoder();

    public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        if (evt is WebSocketServerProtocolHandler.HandshakeComplete)
        {
            StompVersion stompVersion = StompVersion.FindBySubProtocol(((HandshakeComplete) evt).SelectedSubprotocol());
            ctx.Channel.Attr(StompVersion.CHANNEL_ATTRIBUTE_KEY).Set(stompVersion);
            ctx.Pipeline
               .AddLast(new WebSocketFrameAggregator(65536))
               .AddLast(new StompSubframeDecoder())
               .AddLast(new StompSubframeAggregator(65536))
               .AddLast(stompChatHandler)
               .Remove(StompWebSocketClientPageHandler.INSTANCE);
        }
        else
        {
            base.UserEventTriggered(ctx, evt);
        }
    }

    protected override void Encode(IChannelHandlerContext ctx, IStompSubframe stompFrame, IList<object> @out)
    {
        stompWebSocketFrameEncoder.Encode(ctx, stompFrame, @out);
    }

    protected override void Decode(IChannelHandlerContext ctx, WebSocketFrame webSocketFrame, IList<object> @out)
    {
        if (webSocketFrame is TextWebSocketFrame || webSocketFrame is BinaryWebSocketFrame)
        {
            @out.Add(webSocketFrame.Content.Retain());
        }
        else
        {
            _ = ctx.CloseAsync();
        }
    }
}
