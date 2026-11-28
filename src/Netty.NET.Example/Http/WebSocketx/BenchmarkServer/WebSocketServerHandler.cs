/*
 * Copyright 2014 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http.HttpMethod;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;

namespace Netty.NET.Example.Http.WebSocketx.BenchmarkServer;

/**
 * Handles handshakes and messages
 */
// CLR: FullHttpRequest/FullHttpResponse are IFullHttpRequest/IFullHttpResponse, content() is the Content property and
// ctx.alloc() is ctx.Alloc(). The handshaker methods are HandshakeAsync/CloseAsync and sendUnsupportedVersionResponse
// is SendUnsupportedVersionResponseAsync; their Tasks, and those of ctx.write(...), are discarded as Java discards the
// futures. ChannelFutureListener.CLOSE on the write future is a synchronous Task continuation that closes the channel on
// its event loop (Common's EventExecutorTaskScheduler), as codec-http's CorsHandler maps it; a Task has no channel, so
// the channel is ctx.Channel. String.equals is the ordinal string.Equals. Java string concatenation writes a missing
// Host header as "null", which getWebSocketLocation keeps. printStackTrace() writes the exception to Console.Error and
// ctx.close() is CloseAsync().
public class WebSocketServerHandler : SimpleChannelInboundHandler<object>
{
    private const string WEBSOCKET_PATH = "/websocket";

    private WebSocketServerHandshaker handshaker;

    protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
    {
        if (msg is IFullHttpRequest)
        {
            HandleHttpRequest(ctx, (IFullHttpRequest) msg);
        }
        else if (msg is WebSocketFrame)
        {
            HandleWebSocketFrame(ctx, (WebSocketFrame) msg);
        }
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        ctx.Flush();
    }

    private void HandleHttpRequest(IChannelHandlerContext ctx, IFullHttpRequest req)
    {
        // Handle a bad request.
        if (!req.DecoderResult().IsSuccess())
        {
            SendHttpResponse(ctx, req, new DefaultFullHttpResponse(req.ProtocolVersion(), BAD_REQUEST,
                                                                   ctx.Alloc().Buffer(0)));
            return;
        }

        // Allow only GET methods.
        if (!GET.Equals(req.Method()))
        {
            SendHttpResponse(ctx, req, new DefaultFullHttpResponse(req.ProtocolVersion(), FORBIDDEN,
                                                                   ctx.Alloc().Buffer(0)));
            return;
        }

        // Send the demo page and favicon.ico
        if ("/".Equals(req.Uri()))
        {
            ByteBuf content = WebSocketServerBenchmarkPage.GetContent(GetWebSocketLocation(req));
            IFullHttpResponse res = new DefaultFullHttpResponse(req.ProtocolVersion(), OK, content);

            res.Headers().Set(HttpHeaderNames.CONTENT_TYPE, "text/html; charset=UTF-8");
            HttpUtil.SetContentLength(res, content.ReadableBytes);

            SendHttpResponse(ctx, req, res);
            return;
        }

        if ("/favicon.ico".Equals(req.Uri()))
        {
            IFullHttpResponse res = new DefaultFullHttpResponse(req.ProtocolVersion(), NOT_FOUND,
                                                               ctx.Alloc().Buffer(0));
            SendHttpResponse(ctx, req, res);
            return;
        }

        // Handshake
        WebSocketServerHandshakerFactory wsFactory = new WebSocketServerHandshakerFactory(
                GetWebSocketLocation(req), null, true, 5 * 1024 * 1024);
        handshaker = wsFactory.NewHandshaker(req);
        if (handshaker == null)
        {
            _ = WebSocketServerHandshakerFactory.SendUnsupportedVersionResponseAsync(ctx.Channel);
        }
        else
        {
            _ = handshaker.HandshakeAsync(ctx, req);
        }
    }

    private void HandleWebSocketFrame(IChannelHandlerContext ctx, WebSocketFrame frame)
    {

        // Check for closing frame
        if (frame is CloseWebSocketFrame)
        {
            _ = handshaker.CloseAsync(ctx, (CloseWebSocketFrame) frame.Retain());
            return;
        }

        if (frame is PingWebSocketFrame)
        {
            _ = ctx.WriteAsync(new PongWebSocketFrame(frame.Content.Retain()));
            return;
        }

        if (frame is TextWebSocketFrame)
        {
            // Echo the frame
            _ = ctx.WriteAsync(frame.Retain());
            return;
        }

        if (frame is BinaryWebSocketFrame)
        {
            // Echo the frame
            _ = ctx.WriteAsync(frame.Retain());
        }
    }

    private static void SendHttpResponse(IChannelHandlerContext ctx, IFullHttpRequest req, IFullHttpResponse res)
    {
        // Generate an error page if response getStatus code is not OK (200).
        HttpResponseStatus responseStatus = res.Status();
        if (responseStatus.Code() != 200)
        {
            ByteBufUtil.WriteUtf8(res.Content, responseStatus.ToString());
            HttpUtil.SetContentLength(res, res.Content.ReadableBytes);
        }

        // Send the response and close the connection if necessary.
        bool keepAlive = HttpUtil.IsKeepAlive(req) && responseStatus.Code() == 200;
        HttpUtil.SetKeepAlive(res, keepAlive);
        Task future = ctx.WriteAsync(res); // Flushed in channelReadComplete()

        if (!keepAlive)
        {
            IChannel channel = ctx.Channel;
            _ = future.ContinueWith(_ => channel.CloseAsync(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, new EventExecutorTaskScheduler(() => channel.EventLoop));
        }
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }

    private static string GetWebSocketLocation(IFullHttpRequest req)
    {
        string location =  (req.Headers().Get(HttpHeaderNames.HOST) ?? "null") + WEBSOCKET_PATH;

        if (WebSocketServer.SSL)
        {
            return "wss://" + location;
        }
        else
        {
            return "ws://" + location;
        }
    }
}
