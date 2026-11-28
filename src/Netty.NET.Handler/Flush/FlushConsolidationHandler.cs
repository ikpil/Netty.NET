/*
 * Copyright 2016 The Netty Project
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

namespace Netty.NET.Handler.Flush;

/**
 * {@link ChannelDuplexHandler} which consolidates {@link Channel#flush()} / {@link ChannelHandlerContext#flush()}
 * operations (which also includes
 * {@link Channel#writeAndFlush(Object)} / {@link Channel#writeAndFlush(Object, ChannelPromise)} and
 * {@link ChannelOutboundInvoker#writeAndFlush(Object)} /
 * {@link ChannelOutboundInvoker#writeAndFlush(Object, ChannelPromise)}).
 * <p>
 * Flush operations are generally speaking expensive as these may trigger a syscall on the transport level. Thus it is
 * in most cases (where write latency can be traded with throughput) a good idea to try to minimize flush operations
 * as much as possible.
 * <p>
 * If a read loop is currently ongoing, {@link #flush(ChannelHandlerContext)} will not be passed on to the next
 * {@link ChannelOutboundHandler} in the {@link ChannelPipeline}, as it will pick up any pending flushes when
 * {@link #channelReadComplete(ChannelHandlerContext)} is triggered.
 * If no read loop is ongoing, the behavior depends on the {@code consolidateWhenNoReadInProgress} constructor argument:
 * <ul>
 *     <li>if {@code false}, flushes are passed on to the next handler directly;</li>
 *     <li>if {@code true}, the invocation of the next handler is submitted as a separate task on the event loop. Under
 *     high throughput, this gives the opportunity to process other flushes before the task gets executed, thus
 *     batching multiple flushes into one.</li>
 * </ul>
 * If {@code explicitFlushAfterFlushes} is reached the flush will be forwarded as well (whether while in a read loop, or
 * while batching outside of a read loop).
 * <p>
 * If the {@link Channel} becomes non-writable it will also try to execute any pending flush operations.
 * <p>
 * The {@link FlushConsolidationHandler} should be put as first {@link ChannelHandler} in the
 * {@link ChannelPipeline} to have the best effect.
 */
public class FlushConsolidationHandler : ChannelDuplexHandler
{
    private readonly int explicitFlushAfterFlushes;
    private readonly bool consolidateWhenNoReadInProgress;
    // CLR: the Runnable is an Action (the anonymous class is a lambda over the handler's fields).
    private readonly Action flushTask;
    private int flushPendingCount;
    private bool readInProgress;
    private IChannelHandlerContext ctx;
    // CLR: java.util.concurrent.Future<?> is only kept to cancel(false) the submitted task, so it is the
    // CancellationTokenSource whose token is passed to EventExecutorExtensions.SubmitAsync: "Cancellation before
    // execution prevents the delegate from running" (src/Netty.NET.Common/Concurrent/EventExecutorExtensions.cs), as a
    // task that "has not started when cancel is called ... should never run", and with mayInterruptIfRunning false
    // "in-progress tasks are allowed to complete"
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/concurrent/Future.html#cancel(boolean)).
    private CancellationTokenSource nextScheduledFlush;

    /**
     * The default number of flushes after which a flush will be forwarded to downstream handlers (whether while in a
     * read loop, or while batching outside of a read loop).
     */
    public const int DEFAULT_EXPLICIT_FLUSH_AFTER_FLUSHES = 256;

    /**
     * Create new instance which explicit flush after {@value DEFAULT_EXPLICIT_FLUSH_AFTER_FLUSHES} pending flush
     * operations at the latest.
     */
    public FlushConsolidationHandler() : this(DEFAULT_EXPLICIT_FLUSH_AFTER_FLUSHES, false)
    {
    }

    /**
     * Create new instance which doesn't consolidate flushes when no read is in progress.
     *
     * @param explicitFlushAfterFlushes the number of flushes after which an explicit flush will be done.
     */
    public FlushConsolidationHandler(int explicitFlushAfterFlushes) : this(explicitFlushAfterFlushes, false)
    {
    }

    /**
     * Create new instance.
     *
     * @param explicitFlushAfterFlushes the number of flushes after which an explicit flush will be done.
     * @param consolidateWhenNoReadInProgress whether to consolidate flushes even when no read loop is currently
     *                                        ongoing.
     */
    public FlushConsolidationHandler(int explicitFlushAfterFlushes, bool consolidateWhenNoReadInProgress)
    {
        this.explicitFlushAfterFlushes =
                ObjectUtil.CheckPositive(explicitFlushAfterFlushes, "explicitFlushAfterFlushes");
        this.consolidateWhenNoReadInProgress = consolidateWhenNoReadInProgress;
        this.flushTask = consolidateWhenNoReadInProgress ?
                () =>
                {
                    if (flushPendingCount > 0 && !readInProgress)
                    {
                        flushPendingCount = 0;
                        nextScheduledFlush = null;
                        ctx.Flush();
                    } // else we'll flush when the read completes
                }
                : null;
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        this.ctx = ctx;
    }

    public override void Flush(IChannelHandlerContext ctx)
    {
        if (readInProgress)
        {
            // If there is still a read in progress we are sure we will see a channelReadComplete(...) call. Thus
            // we only need to flush if we reach the explicitFlushAfterFlushes limit.
            if (++flushPendingCount == explicitFlushAfterFlushes)
            {
                FlushNow(ctx);
            }
        }
        else if (consolidateWhenNoReadInProgress)
        {
            // Flush immediately if we reach the threshold, otherwise schedule
            if (++flushPendingCount == explicitFlushAfterFlushes)
            {
                FlushNow(ctx);
            }
            else
            {
                ScheduleFlush(ctx);
            }
        }
        else
        {
            // Always flush directly
            FlushNow(ctx);
        }
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        // This may be the last event in the read loop, so flush now!
        ResetReadAndFlushIfNeeded(ctx);
        ctx.FireChannelReadComplete();
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        readInProgress = true;
        ctx.FireChannelRead(msg);
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        // To ensure we not miss to flush anything, do it now.
        ResetReadAndFlushIfNeeded(ctx);
        ctx.FireExceptionCaught(cause);
    }

    // CLR: disconnect(ctx, promise) and close(ctx, promise) are the Task-returning DisconnectAsync/CloseAsync; the
    // returned Task replaces the promise (docs/codec-base-porting.md, Outbound operations).
    public override Task DisconnectAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        // Try to flush one last time if flushes are pending before disconnect the channel.
        ResetReadAndFlushIfNeeded(ctx);

        return ctx.DisconnectAsync(cancellationToken);
    }

    public override Task CloseAsync(IChannelHandlerContext ctx, CancellationToken cancellationToken)
    {
        // Try to flush one last time if flushes are pending before close the channel.
        ResetReadAndFlushIfNeeded(ctx);

        return ctx.CloseAsync(cancellationToken);
    }

    public override void ChannelWritabilityChanged(IChannelHandlerContext ctx)
    {
        if (!ctx.Channel.IsWritable)
        {
            // The writability of the channel changed to false, so flush all consolidated flushes now to free up memory.
            FlushIfNeeded(ctx);
        }

        ctx.FireChannelWritabilityChanged();
    }

    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        FlushIfNeeded(ctx);
    }

    private void ResetReadAndFlushIfNeeded(IChannelHandlerContext ctx)
    {
        readInProgress = false;
        FlushIfNeeded(ctx);
    }

    private void FlushIfNeeded(IChannelHandlerContext ctx)
    {
        if (flushPendingCount > 0)
        {
            FlushNow(ctx);
        }
    }

    private void FlushNow(IChannelHandlerContext ctx)
    {
        CancelScheduledFlush();
        flushPendingCount = 0;
        ctx.Flush();
    }

    // CLR: eventLoop().submit(flushTask) is SubmitAsync(flushTask, token); the returned Task is not observed, as the
    // source keeps the Future only to cancel it.
    private void ScheduleFlush(IChannelHandlerContext ctx)
    {
        if (nextScheduledFlush == null)
        {
            // Run as soon as possible, but still yield to give a chance for additional writes to enqueue.
            nextScheduledFlush = new CancellationTokenSource();
            _ = ctx.Channel.EventLoop.SubmitAsync(flushTask, nextScheduledFlush.Token);
        }
    }

    private void CancelScheduledFlush()
    {
        if (nextScheduledFlush != null)
        {
            nextScheduledFlush.Cancel();
            nextScheduledFlush = null;
        }
    }
}
