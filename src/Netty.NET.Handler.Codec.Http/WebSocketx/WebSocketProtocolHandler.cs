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
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

// CLR: the package-private abstract class is public because the public WebSocketServerProtocolHandler and
// WebSocketClientProtocolHandler derive from it and a C# base class must be at least as accessible as the derived class
// (CS0060, https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0060); its constructors stay internal, so only this
// assembly (and the friend test assembly) can derive from it. MessageToMessageDecoder<WebSocketFrame> is the codec-base
// generic and super(WebSocketFrame.class) is base(typeof(WebSocketFrame)) (docs/codec-base-porting.md, Type matching);
// ChannelOutboundHandler is the Transport IChannelOutboundHandler, whose bind/connect/disconnect/close/deregister/write
// operations return the Task that replaces the promise (docs/codec-base-porting.md, Outbound operations). The
// ChannelPromise closeSent is a TaskCompletionSource that this handler completes: write(CloseWebSocketFrame) returns
// its Task where Java uses the caller's promise.unvoid() (there is no void promise), a write after the close frame
// returns a faulted Task where Java fails the promise, and PromiseNotifier(false, ...) is a synchronous continuation
// that copies the outcome with TrySetFromTask (docs/common-task-composition.md, PromiseNotifier). close(ctx, promise)
// returns the Task of ctx.close() started once closeSent completes. The listeners on the close-frame write and on
// closeSent run on Common's EventExecutorTaskScheduler over ctx.Executor, the DefaultPromise.notifyListeners() timing
// that handler's IdleStateHandler uses too (docs/handler-porting.md, Listener timing): the Transport completes the
// write Task with RunContinuationsAsynchronously when the later flush writes it, so a continuation on
// TaskScheduler.Default closed the channel later on a thread-pool thread, and WebSocketServerProtocolHandlerTest saw
// the channel still open after close() (measured: 5 of 6 runs had one or two such failures).
// ctx.executor().schedule(...) is ScheduleAsync, and the Future<?> forceCloseTimeoutTask is the CancellationTokenSource
// whose Cancel() stands for cancel(false) (docs/handler-porting.md, Executors and scheduling). The anonymous Runnable
// is a lambda.
// ctx.channel().config().isAutoRead() is ctx.Channel.Config.IsAutoRead(), and SocketAddress is EndPoint.
// closeSent(ChannelPromise) and applyCloseSentTimeout(ChannelHandlerContext) are internal because the subclasses call
// them (package access in Java).
public abstract class WebSocketProtocolHandler : MessageToMessageDecoder<WebSocketFrame>, IChannelOutboundHandler
{

    private readonly bool dropPongFrames;
    private readonly WebSocketCloseStatus closeStatus;
    private readonly long forceCloseTimeoutMillis;
    private TaskCompletionSource closeSent;
    private CancellationTokenSource forceCloseTimeoutTask;

    /**
     * Creates a new {@link WebSocketProtocolHandler} that will <i>drop</i> {@link PongWebSocketFrame}s.
     */
    internal WebSocketProtocolHandler() : this(true)
    {
    }

    /**
     * Creates a new {@link WebSocketProtocolHandler}, given a parameter that determines whether or not to drop {@link
     * PongWebSocketFrame}s.
     *
     * @param dropPongFrames
     *            {@code true} if {@link PongWebSocketFrame}s should be dropped
     */
    internal WebSocketProtocolHandler(bool dropPongFrames)
        : this(dropPongFrames, null, WebSocketServerProtocolConfig.DEFAULT_FORCE_CLOSE_TIMEOUT_MILLIS)
    {
    }

    internal WebSocketProtocolHandler(bool dropPongFrames,
                                      WebSocketCloseStatus closeStatus,
                                      long forceCloseTimeoutMillis) : base(typeof(WebSocketFrame))
    {
        this.dropPongFrames = dropPongFrames;
        this.closeStatus = closeStatus;
        this.forceCloseTimeoutMillis = forceCloseTimeoutMillis;
    }

    protected override void Decode(IChannelHandlerContext ctx, WebSocketFrame frame, IList<object> @out)
    {
        if (frame is PingWebSocketFrame)
        {
            frame.Content.Retain();
            _ = ctx.WriteAndFlushAsync(new PongWebSocketFrame(frame.Content));
            ReadIfNeeded(ctx);
            return;
        }

        if (frame is PongWebSocketFrame && dropPongFrames)
        {
            ReadIfNeeded(ctx);
            return;
        }

        @out.Add(frame.Retain());
    }

    private static void ReadIfNeeded(IChannelHandlerContext ctx)
    {
        if (!ctx.Channel.Config.IsAutoRead())
        {
            ctx.Read();
        }
    }

    public virtual Task CloseAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        if (closeStatus == null || !ctx.Channel.IsActive)
        {
            return ctx.CloseAsync(cancellationToken);
        }
        else
        {
            if (closeSent == null)
            {
                _ = WriteAsync(ctx, new CloseWebSocketFrame(closeStatus, ctx.Alloc()), CancellationToken.None);
            }

            Flush(ctx);

            return closeSent.Task.ContinueWith(future => ctx.CloseAsync(cancellationToken), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    new EventExecutorTaskScheduler(() => ctx.Executor)).Unwrap();
        }
    }

    public virtual Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        if (closeSent != null)
        {
            ReferenceCountUtil.Release(msg);

            return Task.FromException(new ClosedChannelException());
        }
        else if (msg is CloseWebSocketFrame)
        {
            TaskCompletionSource promise = new TaskCompletionSource();
            CloseSent(promise);
            TaskCompletionSource notified = closeSent;
            // CLR: the scheduler's lambda captures this branch's copy of ctx, so the other writes allocate no closure
            // (see EventExecutorTaskScheduler).
            IChannelHandlerContext listenerCtx = ctx;
            _ = ctx.WriteAsync(msg).ContinueWith(future => notified.TrySetFromTask(future), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    new EventExecutorTaskScheduler(() => listenerCtx.Executor));
            return promise.Task;
        }
        else
        {
            return ctx.WriteAsync(msg, cancellationToken);
        }
    }

    public virtual void Flush(IChannelHandlerContext ctx)
    {
        ctx.Flush();

        // Give the flush a chance to complete synchronously before arming the deadline, so a write that
        // finishes immediately (e.g. EmbeddedChannel, or a fast socket) never races the force-close timer.
        if (closeSent != null)
        {
            ApplyCloseSentTimeout(ctx);
        }
    }

    /**
     * Records the {@link ChannelPromise} used to write the outgoing close frame. Every code path that initiates
     * the close handshake must call this, and must subsequently give
     * {@link #applyCloseSentTimeout(ChannelHandlerContext)} a chance to run (directly, or indirectly via
     * {@link #flush(ChannelHandlerContext)}) so the channel is guaranteed to close even if the outbound write
     * never completes (e.g. the peer stops reading and the socket send buffer fills up).
     */
    internal void CloseSent(TaskCompletionSource promise)
    {
        if (closeSent != null)
        {
            // Already sending (or already sent) a close frame, e.g. a peer that sends more than one CLOSE
            // frame. Keep the original promise (and the deadline already armed for it) authoritative, and
            // just cascade its outcome onto the new one instead of losing track of the original.
            _ = closeSent.Task.ContinueWith(future => promise.TrySetFromTask(future), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return;
        }

        closeSent = promise;
    }

    internal void ApplyCloseSentTimeout(IChannelHandlerContext ctx)
    {
        if (forceCloseTimeoutTask != null || closeSent.Task.IsCompleted || forceCloseTimeoutMillis < 0)
        {
            return;
        }

        CancellationTokenSource timeoutTask = new CancellationTokenSource();
        _ = ctx.Executor.ScheduleAsync(() =>
        {
            if (!closeSent.Task.IsCompleted)
            {
                closeSent.TrySetException(BuildHandshakeException("send close frame timed out"));
                // Do not rely on some other listener eventually closing the channel once closeSent
                // completes (e.g. write(CloseWebSocketFrame) without a subsequent close() call attaches
                // none): close it here so the deadline is enforced unconditionally.
                _ = ctx.CloseAsync();
            }
        }, TimeSpan.FromMilliseconds(forceCloseTimeoutMillis), timeoutTask.Token);
        forceCloseTimeoutTask = timeoutTask;

        _ = closeSent.Task.ContinueWith(future => timeoutTask.Cancel(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, new EventExecutorTaskScheduler(() => ctx.Executor));
    }

    /**
     * Returns a {@link WebSocketHandshakeException} that depends on which client or server pipeline
     * this handler belongs. Should be overridden in implementation otherwise a default exception is used.
     */
    protected virtual WebSocketHandshakeException BuildHandshakeException(string message)
    {
        return new WebSocketHandshakeException(message);
    }

    public virtual Task BindAsync(IChannelHandlerContext ctx, EndPoint localAddress,
                                  CancellationToken cancellationToken)
    {
        return ctx.BindAsync(localAddress, cancellationToken);
    }

    public virtual Task ConnectAsync(IChannelHandlerContext ctx, EndPoint remoteAddress,
                                     EndPoint localAddress, CancellationToken cancellationToken)
    {
        return ctx.ConnectAsync(remoteAddress, localAddress, cancellationToken);
    }

    public virtual Task DisconnectAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return ctx.DisconnectAsync(cancellationToken);
    }

    public virtual Task DeregisterAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        return ctx.DeregisterAsync(cancellationToken);
    }

    public virtual void Read(IChannelHandlerContext ctx)
    {
        ctx.Read();
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        ctx.FireExceptionCaught(cause);
        _ = ctx.CloseAsync();
    }
}
