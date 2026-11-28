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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Timeout;

/**
 * Raises a {@link WriteTimeoutException} when a write operation cannot finish in a certain period of time.
 *
 * <pre>
 * // The connection is closed when a write operation cannot finish in 30 seconds.
 *
 * public class MyChannelInitializer extends {@link ChannelInitializer}&lt;{@link Channel}&gt; {
 *     public void initChannel({@link Channel} channel) {
 *         channel.pipeline().addLast("writeTimeoutHandler", new {@link WriteTimeoutHandler}(30);
 *         channel.pipeline().addLast("myHandler", new MyHandler());
 *     }
 * }
 *
 * // Handler should handle the {@link WriteTimeoutException}.
 * public class MyHandler extends {@link ChannelDuplexHandler} {
 *     {@code @Override}
 *     public void exceptionCaught({@link ChannelHandlerContext} ctx, {@link Throwable} cause)
 *             throws {@link Exception} {
 *         if (cause instanceof {@link WriteTimeoutException}) {
 *             // do something
 *         } else {
 *             super.exceptionCaught(ctx, cause);
 *         }
 *     }
 * }
 *
 * {@link ServerBootstrap} bootstrap = ...;
 * ...
 * bootstrap.childHandler(new MyChannelInitializer());
 * ...
 * </pre>
 * @see ReadTimeoutHandler
 * @see IdleStateHandler
 */
// CLR: a TimeSpan replaces the (long, TimeUnit) pair and the timeout is scheduled in 100 ns TimeSpan ticks, rounded up
// (as IdleStateHandler). The ChannelPromise of write(ctx, msg, promise) is the Task that ctx.WriteAsync returns
// (docs/codec-base-porting.md, "Outbound operations"), so the timeout is scheduled right after the write is handed to the
// next handler instead of right before it; a write that has already completed then cancels the timeout through its
// listener at once, as a listener added to a completed promise runs at once. Future<?> is the CancellationTokenSource of
// the scheduled work (cancel(false) is Cancel()). The listener is a Task continuation on Common's
// EventExecutorTaskScheduler over ctx.executor() (src/Netty.NET.Common/Concurrent/EventExecutorTaskScheduler.cs), which
// notifies it as DefaultPromise.notifyListeners() does (at once on the event loop, otherwise through execute);
// operationComplete() keeps the Java hand-off to the executor for a listener that runs elsewhere
// (https://github.com/netty/netty/issues/11053).
public class WriteTimeoutHandler : ChannelOutboundHandlerAdapter
{
    // CLR: TimeUnit.MILLISECONDS.toNanos(1).
    private static readonly long MIN_TIMEOUT_NANOS = TimeSpan.TicksPerMillisecond * 100;

    private readonly long timeoutNanos;

    /**
     * A doubly-linked list to track all WriteTimeoutTasks
     */
    private WriteTimeoutTask lastTask;

    private bool closed;

    // CLR: the TaskScheduler of the write listeners, created with the first scheduled timeout.
    private TaskScheduler listenerScheduler;

    /**
     * Creates a new instance.
     *
     * @param timeoutSeconds
     *        write timeout in seconds
     */
    public WriteTimeoutHandler(int timeoutSeconds) : this(TimeSpan.FromSeconds(timeoutSeconds))
    {
    }

    /**
     * Creates a new instance.
     *
     * @param timeout
     *        write timeout
     * @param unit
     *        the {@link TimeUnit} of {@code timeout}
     */
    // CLR: the TimeSpan carries its unit, so checkNotNull(unit, "unit") has no counterpart; a positive TimeSpan converts
    // to nanoseconds with saturation, as TimeUnit.toNanos does.
    public WriteTimeoutHandler(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            timeoutNanos = 0;
        }
        else
        {
            long nanos = timeout.Ticks > long.MaxValue / 100 ? long.MaxValue : timeout.Ticks * 100;
            timeoutNanos = Math.Max(nanos, MIN_TIMEOUT_NANOS);
        }
    }

    // CLR: there is no void promise to unvoid(); see the class comment for the order of write and scheduleTimeout.
    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        Task promise = ctx.WriteAsync(msg, cancellationToken);

        if (timeoutNanos > 0)
        {
            ScheduleTimeout(ctx, promise);
        }

        return promise;
    }

    // CLR: the Java asserts are Debug.Assert.
    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        Debug.Assert(ctx.Executor.InEventLoop());
        WriteTimeoutTask task = lastTask;
        lastTask = null;

        while (task != null)
        {
            Debug.Assert(task.ctx.Executor.InEventLoop());
            task.scheduledFuture.Cancel();
            WriteTimeoutTask prev = task.prev;
            task.prev = null;
            task.next = null;
            task = prev;
        }
    }

    // CLR: the scheduled Task answers scheduledFuture.isDone() (a rejected schedule completes it at once).
    private void ScheduleTimeout(IChannelHandlerContext ctx, Task promise)
    {
        // Schedule a timeout.
        WriteTimeoutTask task = new WriteTimeoutTask(this, ctx, promise);
        task.scheduledFuture = new CancellationTokenSource();
        Task scheduled = ctx.Executor.ScheduleAsync(task.Run,
                TimeSpan.FromTicks(timeoutNanos / 100 + (timeoutNanos % 100 == 0 ? 0 : 1)), task.scheduledFuture.Token);

        if (!scheduled.IsCompleted)
        {
            AddWriteTimeoutTask(task);

            // CLR: the listener scheduler is created once, and its lambda captures a copy of ctx declared in that block,
            // so the later writes allocate no closure (see EventExecutorTaskScheduler).
            if (listenerScheduler == null)
            {
                IChannelHandlerContext listenerCtx = ctx;
                listenerScheduler = new EventExecutorTaskScheduler(() => listenerCtx.Executor);
            }

            // Cancel the scheduled timeout if the flush promise is complete.
            _ = promise.ContinueWith(task.OperationComplete, CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, listenerScheduler);
        }
    }

    private void AddWriteTimeoutTask(WriteTimeoutTask task)
    {
        Debug.Assert(task.ctx.Executor.InEventLoop());

        if (lastTask != null)
        {
            lastTask.next = task;
            task.prev = lastTask;
        }

        lastTask = task;
    }

    private void RemoveWriteTimeoutTask(WriteTimeoutTask task)
    {
        Debug.Assert(task.ctx.Executor.InEventLoop());

        if (task == lastTask)
        {
            // task is the tail of list
            Debug.Assert(task.next == null);
            lastTask = lastTask.prev;
            if (lastTask != null)
            {
                lastTask.next = null;
            }
        }
        else if (task.prev == null && task.next == null)
        {
            // Since task is not lastTask, then it has been removed or not been added.
            return;
        }
        else if (task.prev == null)
        {
            // task is the head of list and the list has at least 2 nodes
            task.next.prev = null;
        }
        else
        {
            task.prev.next = task.next;
            task.next.prev = task.prev;
        }

        task.prev = null;
        task.next = null;
    }

    /**
     * Is called when a write timeout was detected
     */
    protected virtual void WriteTimedOut(IChannelHandlerContext ctx)
    {
        if (!closed)
        {
            ctx.FireExceptionCaught(WriteTimeoutException.INSTANCE);
            _ = ctx.CloseAsync();
            closed = true;
        }
    }

    // CLR: Runnable and ChannelFutureListener are the Run() and OperationComplete(Task) methods; the inner class reaches
    // the handler through an explicit reference (WriteTimeoutHandler.this).
    private sealed class WriteTimeoutTask
    {
        private readonly WriteTimeoutHandler handler;
        internal readonly IChannelHandlerContext ctx;
        private readonly Task promise;

        // WriteTimeoutTask is also a node of a doubly-linked list
        internal WriteTimeoutTask prev;
        internal WriteTimeoutTask next;

        internal CancellationTokenSource scheduledFuture;

        internal WriteTimeoutTask(WriteTimeoutHandler handler, IChannelHandlerContext ctx, Task promise)
        {
            this.handler = handler;
            this.ctx = ctx;
            this.promise = promise;
        }

        // CLR: catch (Throwable t) is catch (Exception t).
        public void Run()
        {
            // Was not written yet so issue a write timeout
            // The promise itself will be failed with a ClosedChannelException once the close() was issued
            // See https://github.com/netty/netty/issues/2159
            if (!promise.IsCompleted)
            {
                try
                {
                    handler.WriteTimedOut(ctx);
                }
                catch (Exception t)
                {
                    ctx.FireExceptionCaught(t);
                }
            }

            handler.RemoveWriteTimeoutTask(this);
        }

        public void OperationComplete(Task future)
        {
            // scheduledFuture has already be set when reaching here
            scheduledFuture.Cancel();

            // Check if its safe to modify the "doubly-linked-list" that we maintain. If its not we will schedule the
            // modification so its picked up by the executor..
            if (ctx.Executor.InEventLoop())
            {
                handler.RemoveWriteTimeoutTask(this);
            }
            else
            {
                // So let's just pass outself to the executor which will then take care of remove this task
                // from the doubly-linked list. Schedule ourself is fine as the promise itself is done.
                //
                // This fixes https://github.com/netty/netty/issues/11053
                Debug.Assert(promise.IsCompleted);
                ctx.Executor.Execute(Run);
            }
        }
    }
}
