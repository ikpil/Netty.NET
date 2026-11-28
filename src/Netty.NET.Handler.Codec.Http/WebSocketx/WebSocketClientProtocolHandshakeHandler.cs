/*
 * Copyright 2013 The Netty Project
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
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http.WebSocketx.WebSocketClientProtocolHandler;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

// CLR: the package-private class is internal. The ChannelPromise handshakePromise is a TaskCompletionSource (see
// WebSocketClientHandshaker); trySuccess/tryFailure are TrySetResult/TrySetException and isDone() is Task.IsCompleted.
// The handshake listener is a synchronous continuation on TaskScheduler.Default (docs/codec-base-porting.md,
// MessageAggregator) and future.cause() is the Task's InnerException. ctx.executor().schedule(...) is ScheduleAsync
// with a CancellationTokenSource for the Future and its cancel(false) (docs/handler-porting.md, Executors and
// scheduling); ctx.flush().fireUserEventTriggered(...).close() are three calls, because the Transport's Flush() and
// FireUserEventTriggered() return the outbound/inbound invoker interfaces, not the context. checkPositive is the Common
// ObjectUtil.CheckPositive, and the package-private getHandshakeFuture() is internal. IllegalStateException is
// InvalidOperationException.
internal class WebSocketClientProtocolHandshakeHandler : ChannelInboundHandlerAdapter
{
    private const long DEFAULT_HANDSHAKE_TIMEOUT_MS = 10000L;

    private readonly WebSocketClientHandshaker handshaker;
    private readonly long handshakeTimeoutMillis;
    private IChannelHandlerContext ctx;
    private TaskCompletionSource handshakePromise;

    internal WebSocketClientProtocolHandshakeHandler(WebSocketClientHandshaker handshaker)
        : this(handshaker, DEFAULT_HANDSHAKE_TIMEOUT_MS)
    {
    }

    internal WebSocketClientProtocolHandshakeHandler(WebSocketClientHandshaker handshaker, long handshakeTimeoutMillis)
    {
        this.handshaker = handshaker;
        this.handshakeTimeoutMillis = ObjectUtil.CheckPositive(handshakeTimeoutMillis, "handshakeTimeoutMillis");
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        this.ctx = ctx;
        handshakePromise = new TaskCompletionSource();
    }

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        ctx.FireChannelActive();
        _ = handshaker.HandshakeAsync(ctx.Channel).ContinueWith(future =>
        {
            if (!future.IsCompletedSuccessfully)
            {
                Exception cause = future.IsFaulted
                        ? future.Exception.InnerException : new TaskCanceledException(future);
                handshakePromise.TrySetException(cause);
                ctx.FireExceptionCaught(cause);
            }
            else
            {
                ctx.FireUserEventTriggered(
                        ClientHandshakeStateEvent.HANDSHAKE_ISSUED);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        ApplyHandshakeTimeout();
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        if (!handshakePromise.Task.IsCompleted)
        {
            handshakePromise.TrySetException(new WebSocketClientHandshakeException("channel closed with handshake " +
                                                                              "in progress"));
        }

        base.ChannelInactive(ctx);
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        if (!(msg is IFullHttpResponse))
        {
            ctx.FireChannelRead(msg);
            return;
        }

        IFullHttpResponse response = (IFullHttpResponse) msg;

        try
        {
            if (!handshaker.IsHandshakeComplete())
            {
                handshaker.FinishHandshake(ctx.Channel, response);
                handshakePromise.TrySetResult();
                ctx.FireUserEventTriggered(
                        WebSocketClientProtocolHandler.ClientHandshakeStateEvent.HANDSHAKE_COMPLETE);
                ctx.Pipeline.Remove(this);
                return;
            }

            throw new InvalidOperationException("WebSocketClientHandshaker should have been non finished yet");
        }
        finally
        {
            response.Release();
        }
    }

    private void ApplyHandshakeTimeout()
    {
        TaskCompletionSource localHandshakePromise = handshakePromise;
        if (handshakeTimeoutMillis <= 0 || localHandshakePromise.Task.IsCompleted)
        {
            return;
        }

        CancellationTokenSource timeoutFuture = new CancellationTokenSource();
        _ = ctx.Executor.ScheduleAsync(() =>
        {
            if (localHandshakePromise.Task.IsCompleted)
            {
                return;
            }

            if (localHandshakePromise.TrySetException(new WebSocketClientHandshakeException("handshake timed out")))
            {
                ctx.Flush();
                ctx.FireUserEventTriggered(ClientHandshakeStateEvent.HANDSHAKE_TIMEOUT);
                _ = ctx.CloseAsync();
            }
        }, TimeSpan.FromMilliseconds(handshakeTimeoutMillis), timeoutFuture.Token);

        // Cancel the handshake timeout when handshake is finished.
        _ = localHandshakePromise.Task.ContinueWith(f => timeoutFuture.Cancel(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /**
     * This method is visible for testing.
     *
     * @return current handshake future
     */
    internal Task GetHandshakeFuture()
    {
        return handshakePromise.Task;
    }
}
