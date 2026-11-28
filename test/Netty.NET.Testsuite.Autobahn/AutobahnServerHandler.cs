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
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http.HttpMethod;
using static Netty.NET.Handler.Codec.Http.HttpResponseStatus;
using static Netty.NET.Handler.Codec.Http.HttpUtil;
using static Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Testsuite.Autobahn;

/**
 * Handles handshakes and messages
 */
// CLR: java.util.logging (Logger.getLogger, isLoggable(Level.FINE), fine) has no CLR counterpart; the logger is Common's
// InternalLoggerFactory logger and Level.FINE is its debug level (Netty's JdkLogger maps isDebugEnabled() and debug()
// to Level.FINE). String.format("%s", ...) is string.Format with the invariant culture; ctx.channel().hashCode() is
// GetHashCode() and frame.getClass().getName() the type's FullName. HttpRequest/FullHttpResponse are
// IHttpRequest/IFullHttpResponse and ctx.alloc() is ctx.Alloc(), as in codec-http. The handshaker methods are
// HandshakeAsync/CloseAsync and sendUnsupportedVersionResponse is SendUnsupportedVersionResponseAsync; their Tasks, and
// the Tasks of ctx.write(...), are discarded as Java discards the futures. ChannelFutureListener.CLOSE on the
// writeAndFlush future of ctx.channel() is a Task continuation that closes that channel on its event loop (Common's
// EventExecutorTaskScheduler), as codec-http's CorsHandler maps it. IllegalStateException is InvalidOperationException
// and UnsupportedOperationException NotSupportedException (docs/codec-base-porting.md). CharsetUtil.UTF_8 is
// Encoding.UTF8. Java string concatenation writes a missing Host header as "null", which getWebSocketLocation keeps.
public class AutobahnServerHandler : ChannelInboundHandlerAdapter
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(AutobahnServerHandler));

    private WebSocketServerHandshaker handshaker;

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        if (msg is IHttpRequest)
        {
            HandleHttpRequest(ctx, (IHttpRequest) msg);
        }
        else if (msg is WebSocketFrame)
        {
            HandleWebSocketFrame(ctx, (WebSocketFrame) msg);
        }
        else
        {
            throw new InvalidOperationException("unknown message: " + msg);
        }
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        ctx.Flush();
    }

    private void HandleHttpRequest(IChannelHandlerContext ctx, IHttpRequest req)
    {
        // Handle a bad request.
        if (!req.DecoderResult().IsSuccess())
        {
            SendHttpResponse(ctx, req, new DefaultFullHttpResponse(HTTP_1_1, BAD_REQUEST, ctx.Alloc().Buffer(0)));
            return;
        }

        // Allow only GET methods.
        if (!GET.Equals(req.Method()))
        {
            SendHttpResponse(ctx, req, new DefaultFullHttpResponse(HTTP_1_1, FORBIDDEN, ctx.Alloc().Buffer(0)));
            return;
        }

        // Handshake
        WebSocketServerHandshakerFactory wsFactory = new WebSocketServerHandshakerFactory(
                GetWebSocketLocation(req), null, false, int.MaxValue);
        handshaker = wsFactory.NewHandshaker(req);
        if (handshaker == null)
        {
            _ = WebSocketServerHandshakerFactory.SendUnsupportedVersionResponseAsync(ctx.Channel);
        }
        else
        {
            _ = handshaker.HandshakeAsync(ctx.Channel, req);
        }
    }

    private void HandleWebSocketFrame(IChannelHandlerContext ctx, WebSocketFrame frame)
    {
        if (logger.IsDebugEnabled())
        {
            logger.Debug(string.Format(CultureInfo.InvariantCulture,
                    "Channel {0} received {1}", ctx.Channel.GetHashCode(), StringUtil.SimpleClassName(frame)));
        }

        if (frame is CloseWebSocketFrame)
        {
            _ = handshaker.CloseAsync(ctx, (CloseWebSocketFrame) frame);
        }
        else if (frame is PingWebSocketFrame)
        {
            _ = ctx.WriteAsync(new PongWebSocketFrame(frame.IsFinalFragment(), frame.Rsv(), frame.Content));
        }
        else if (frame is TextWebSocketFrame ||
                frame is BinaryWebSocketFrame ||
                frame is ContinuationWebSocketFrame)
        {
            _ = ctx.WriteAsync(frame);
        }
        else if (frame is PongWebSocketFrame)
        {
            frame.Release();
            // Ignore
        }
        else
        {
            throw new NotSupportedException(string.Format(CultureInfo.InvariantCulture, "{0} frame types not supported",
                    frame.GetType().FullName));
        }
    }

    private static void SendHttpResponse(
            IChannelHandlerContext ctx, IHttpRequest req, IFullHttpResponse res)
    {
        // Generate an error page if response status code is not OK (200).
        if (res.Status().Code() != 200)
        {
            ByteBuf buf = Unpooled.CopiedBuffer(res.Status().ToString(), Encoding.UTF8);
            res.Content.WriteBytes(buf);
            buf.Release();
            SetContentLength(res, res.Content.ReadableBytes);
        }

        // Send the response and close the connection if necessary.
        IChannel channel = ctx.Channel;
        Task f = channel.WriteAndFlushAsync(res);

        if (!IsKeepAlive(req) || res.Status().Code() != 200)
        {
            _ = f.ContinueWith(_ => channel.CloseAsync(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, new EventExecutorTaskScheduler(() => channel.EventLoop));
        }
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        _ = ctx.CloseAsync();
    }

    private static string GetWebSocketLocation(IHttpRequest req)
    {
        return "ws://" + (req.Headers().Get(HttpHeaderNames.HOST) ?? "null");
    }
}
