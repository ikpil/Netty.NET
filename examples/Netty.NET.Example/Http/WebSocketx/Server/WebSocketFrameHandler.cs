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
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Http.WebSocketx.Server;

/**
 * Echoes uppercase content of text frames.
 */
// CLR: toUpperCase(Locale.US) is ToUpperInvariant (the invariant culture's casing, as Locale.US cases ASCII and
// other letters without locale-specific rules); getClass().getName() is GetType().FullName and
// UnsupportedOperationException is NotSupportedException. writeAndFlush returns a Task, discarded as Java discards
// the future. pipeline().remove(WebSocketIndexPageHandler.class) is Remove<WebSocketIndexPageHandler>().
public class WebSocketFrameHandler : SimpleChannelInboundHandler<WebSocketFrame>
{
    protected override void ChannelRead0(IChannelHandlerContext ctx, WebSocketFrame frame)
    {
        // ping and pong frames already handled

        if (frame is TextWebSocketFrame)
        {
            // Send the uppercase string back.
            string request = ((TextWebSocketFrame) frame).Text();
            _ = ctx.Channel.WriteAndFlushAsync(new TextWebSocketFrame(request.ToUpperInvariant()));
        }
        else
        {
            string message = "unsupported frame type: " + frame.GetType().FullName;
            throw new NotSupportedException(message);
        }
    }

    public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        if (evt is WebSocketServerProtocolHandler.HandshakeComplete)
        {
            //Channel upgrade to websocket, remove WebSocketIndexPageHandler.
            ctx.Pipeline.Remove<WebSocketIndexPageHandler>();
        }
        else
        {
            base.UserEventTriggered(ctx, evt);
        }
    }
}
