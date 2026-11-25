/*
 * Copyright 2019 The Netty Project
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
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http.WebSocketx.WebSocketServerProtocolHandler;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * Handles the HTTP handshake (the HTTP Upgrade request) for {@link WebSocketServerProtocolHandler}.
 */
// CLR: the package-private class is internal. The ChannelPromise handshakePromise is a TaskCompletionSource (see
// WebSocketClientHandshaker); trySuccess/tryFailure are TrySetResult/TrySetException and isDone() is Task.IsCompleted.
// The handshake listener is a synchronous continuation on TaskScheduler.Default (docs/codec-base-porting.md,
// MessageAggregator) and future.cause() is the Task's InnerException. The deprecated
// ServerHandshakeStateEvent.HANDSHAKE_COMPLETE is not ported (see WebSocketServerProtocolHandler), so only the
// HandshakeComplete event is fired after the "Kept for compatibility" comment. ctx.executor().schedule(...) is
// ScheduleAsync with a CancellationTokenSource for the Future and its cancel(false) (docs/handler-porting.md, Executors
// and scheduling); ctx.flush().fireUserEventTriggered(...).close() are three calls, because the Transport's Flush() and
// FireUserEventTriggered() return the outbound/inbound invoker interfaces, not the context. String.startsWith is an
// ordinal StartsWith, charAt is the indexer, and a null Host header prints "null" as in Java string concatenation.
// checkNotNull is the Common ObjectUtil.CheckNotNull.
internal class WebSocketServerProtocolHandshakeHandler : ChannelInboundHandlerAdapter
{

    private readonly WebSocketServerProtocolConfig serverConfig;
    private IChannelHandlerContext ctx;
    private TaskCompletionSource handshakePromise;
    private bool isWebSocketPath;

    internal WebSocketServerProtocolHandshakeHandler(WebSocketServerProtocolConfig serverConfig)
    {
        this.serverConfig = ObjectUtil.CheckNotNull(serverConfig, "serverConfig");
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        this.ctx = ctx;
        handshakePromise = new TaskCompletionSource();
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        IHttpObject httpObject = (IHttpObject) msg;

        if (httpObject is IHttpRequest)
        {
            IHttpRequest req = (IHttpRequest) httpObject;
            isWebSocketPath = IsWebSocketPath(req);
            if (!isWebSocketPath)
            {
                ctx.FireChannelRead(msg);
                return;
            }

            try
            {
                WebSocketServerHandshaker handshaker = WebSocketServerHandshakerFactory.ResolveHandshaker(
                        req,
                        GetWebSocketLocation(ctx.Pipeline, req, serverConfig.WebsocketPath()),
                        serverConfig.Subprotocols(), serverConfig.DecoderConfig());
                TaskCompletionSource localHandshakePromise = handshakePromise;
                if (handshaker == null)
                {
                    _ = WebSocketServerHandshakerFactory.SendUnsupportedVersionResponseAsync(ctx.Channel);
                }
                else
                {
                    // Ensure we set the handshaker and replace this handler before we
                    // trigger the actual handshake. Otherwise we may receive websocket bytes in this handler
                    // before we had a chance to replace it.
                    //
                    // See https://github.com/netty/netty/issues/9471.
                    WebSocketServerProtocolHandler.SetHandshaker(ctx.Channel, handshaker);
                    ctx.Pipeline.Remove(this);

                    // The write via the removed handler's ctx still starts at this handler's former position.
                    Task handshakeFuture = handshaker.HandshakeAsync(ctx, req);
                    _ = handshakeFuture.ContinueWith(future =>
                    {
                        if (!future.IsCompletedSuccessfully)
                        {
                            Exception cause = Cause(future);
                            localHandshakePromise.TrySetException(cause);
                            ctx.FireExceptionCaught(cause);
                        }
                        else
                        {
                            localHandshakePromise.TrySetResult();
                            // Kept for compatibility
                            ctx.FireUserEventTriggered(
                                    new WebSocketServerProtocolHandler.HandshakeComplete(
                                            req.Uri(), req.Headers(), handshaker.SelectedSubprotocol()));
                        }
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    ApplyHandshakeTimeout();
                }
            }
            finally
            {
                ReferenceCountUtil.Release(req);
            }
        }
        else if (!isWebSocketPath)
        {
            ctx.FireChannelRead(msg);
        }
        else
        {
            ReferenceCountUtil.Release(msg);
        }
    }

    // CLR: future.cause() of a faulted or canceled Task.
    private static Exception Cause(Task future)
    {
        return future.IsFaulted ? future.Exception.InnerException : new TaskCanceledException(future);
    }

    private bool IsWebSocketPath(IHttpRequest req)
    {
        string websocketPath = serverConfig.WebsocketPath();
        string uri = req.Uri();
        return serverConfig.CheckStartsWith()
                ? uri.StartsWith(websocketPath, StringComparison.Ordinal) &&
                  ("/".Equals(websocketPath) || CheckNextUri(uri, websocketPath))
                : uri.Equals(websocketPath);
    }

    private bool CheckNextUri(string uri, string websocketPath)
    {
        int len = websocketPath.Length;
        if (uri.Length > len)
        {
            char nextUri = uri[len];
            return nextUri == '/' || nextUri == '?';
        }
        return true;
    }

    private static string GetWebSocketLocation(IChannelPipeline cp, IHttpRequest req, string path)
    {
        string protocol = "ws";
        if (cp.Get<SslHandler>() != null)
        {
            // SSL in use so use Secure WebSockets
            protocol = "wss";
        }
        string host = req.Headers().Get(HttpHeaderNames.HOST);
        return protocol + "://" + (host ?? "null") + path;
    }

    private void ApplyHandshakeTimeout()
    {
        TaskCompletionSource localHandshakePromise = handshakePromise;
        long handshakeTimeoutMillis = serverConfig.HandshakeTimeoutMillis();
        if (handshakeTimeoutMillis <= 0 || localHandshakePromise.Task.IsCompleted)
        {
            return;
        }

        CancellationTokenSource timeoutFuture = new CancellationTokenSource();
        _ = ctx.Executor.ScheduleAsync(() =>
        {
            if (!localHandshakePromise.Task.IsCompleted &&
                localHandshakePromise.TrySetException(new WebSocketServerHandshakeException("handshake timed out")))
            {
                ctx.Flush();
                ctx.FireUserEventTriggered(ServerHandshakeStateEvent.HANDSHAKE_TIMEOUT);
                _ = ctx.CloseAsync();
            }
        }, TimeSpan.FromMilliseconds(handshakeTimeoutMillis), timeoutFuture.Token);

        // Cancel the handshake timeout when handshake is finished.
        _ = localHandshakePromise.Task.ContinueWith(f => timeoutFuture.Cancel(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
