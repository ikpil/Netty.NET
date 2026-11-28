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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;
using static Netty.NET.Handler.Codec.Http.HttpMethod;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;

namespace Netty.NET.Example.Http.WebSocketx.Server;

/**
 * Outputs index page content.
 */
// CLR: FullHttpRequest/FullHttpResponse/HttpRequest are the I-prefixed codec-http interfaces, ChannelPipeline is
// IChannelPipeline, content() is the Content property and ctx.alloc() is ctx.Alloc(); ChannelFutureListener.CLOSE
// is a synchronous Task continuation that closes the channel on its event loop (Common's
// EventExecutorTaskScheduler), as codec-http's CorsHandler maps it; a Task has no channel, so the channel is
// ctx.Channel. Java string concatenation writes a missing Host header as "null", which getWebSocketLocation keeps.
// printStackTrace() writes the exception to Console.Error and ctx.close() is CloseAsync(), whose Task is
// discarded.
public class WebSocketIndexPageHandler : SimpleChannelInboundHandler<IFullHttpRequest>
{
    private readonly string websocketPath;

    public WebSocketIndexPageHandler(string websocketPath)
    {
        this.websocketPath = websocketPath;
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, IFullHttpRequest req)
    {
        // Handle a bad request.
        if (!req.DecoderResult().IsSuccess())
        {
            SendHttpResponse(ctx, req, new DefaultFullHttpResponse(req.ProtocolVersion(), BAD_REQUEST,
                                                                   ctx.Alloc().Buffer(0)));
            return;
        }

        // Handle websocket upgrade request.
        if (req.Headers().Contains(HttpHeaderNames.UPGRADE, HttpHeaderValues.WEBSOCKET, true))
        {
            ctx.FireChannelRead(req.Retain());
            return;
        }

        // Allow only GET methods.
        if (!GET.Equals(req.Method()))
        {
            SendHttpResponse(ctx, req, new DefaultFullHttpResponse(req.ProtocolVersion(), FORBIDDEN,
                                                                   ctx.Alloc().Buffer(0)));
            return;
        }

        // Send the index page
        if ("/".Equals(req.Uri()) || "/index.html".Equals(req.Uri()))
        {
            string webSocketLocation = GetWebSocketLocation(ctx.Pipeline, req, websocketPath);
            ByteBuf content = WebSocketServerIndexPage.GetContent(webSocketLocation);
            IFullHttpResponse res = new DefaultFullHttpResponse(req.ProtocolVersion(), OK, content);

            res.Headers().Set(CONTENT_TYPE, "text/html; charset=UTF-8");
            HttpUtil.SetContentLength(res, content.ReadableBytes);

            SendHttpResponse(ctx, req, res);
        }
        else
        {
            SendHttpResponse(ctx, req, new DefaultFullHttpResponse(req.ProtocolVersion(), NOT_FOUND,
                                                                   ctx.Alloc().Buffer(0)));
        }
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
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
        Task future = ctx.WriteAndFlushAsync(res);

        if (!keepAlive)
        {
            IChannel channel = ctx.Channel;
            _ = future.ContinueWith(_ => channel.CloseAsync(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, new EventExecutorTaskScheduler(() => channel.EventLoop));
        }
    }

    private static string GetWebSocketLocation(IChannelPipeline cp, IHttpRequest req, string path)
    {
        string protocol = "ws";

        if (cp.Get<SslHandler>() != null)
        {
            // SSL in use so use Secure WebSockets
            protocol = "wss";
        }

        return protocol + "://" + (req.Headers().Get(HttpHeaderNames.HOST) ?? "null") + path;
    }
}
