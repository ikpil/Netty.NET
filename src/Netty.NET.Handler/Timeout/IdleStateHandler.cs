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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Timeout;

/**
 * Triggers an {@link IdleStateEvent} when a {@link Channel} has not performed
 * read, write, or both operation for a while.
 *
 * <h3>Supported idle states</h3>
 * <table border="1">
 * <tr>
 * <th>Property</th><th>Meaning</th>
 * </tr>
 * <tr>
 * <td>{@code readerIdleTime}</td>
 * <td>an {@link IdleStateEvent} whose state is {@link IdleState#READER_IDLE}
 *     will be triggered when no read was performed for the specified period of
 *     time.  Specify {@code 0} to disable.</td>
 * </tr>
 * <tr>
 * <td>{@code writerIdleTime}</td>
 * <td>an {@link IdleStateEvent} whose state is {@link IdleState#WRITER_IDLE}
 *     will be triggered when no write was performed for the specified period of
 *     time.  Specify {@code 0} to disable.</td>
 * </tr>
 * <tr>
 * <td>{@code allIdleTime}</td>
 * <td>an {@link IdleStateEvent} whose state is {@link IdleState#ALL_IDLE}
 *     will be triggered when neither read nor write was performed for the
 *     specified period of time.  Specify {@code 0} to disable.</td>
 * </tr>
 * </table>
 *
 * <pre>
 * // An example that sends a ping message when there is no outbound traffic
 * // for 30 seconds.  The connection is closed when there is no inbound traffic
 * // for 60 seconds.
 *
 * public class MyChannelInitializer extends {@link ChannelInitializer}&lt;{@link Channel}&gt; {
 *     {@code @Override}
 *     public void initChannel({@link Channel} channel) {
 *         channel.pipeline().addLast("idleStateHandler", new {@link IdleStateHandler}(60, 30, 0));
 *         channel.pipeline().addLast("myHandler", new MyHandler());
 *     }
 * }
 *
 * // Handler should handle the {@link IdleStateEvent} triggered by {@link IdleStateHandler}.
 * public class MyHandler extends {@link ChannelDuplexHandler} {
 *     {@code @Override}
 *     public void userEventTriggered({@link ChannelHandlerContext} ctx, {@link Object} evt) throws {@link Exception} {
 *         if (evt instanceof {@link IdleStateEvent}) {
 *             {@link IdleStateEvent} e = ({@link IdleStateEvent}) evt;
 *             if (e.state() == {@link IdleState}.READER_IDLE) {
 *                 ctx.close();
 *             } else if (e.state() == {@link IdleState}.WRITER_IDLE) {
 *                 ctx.writeAndFlush(new PingMessage());
 *             }
 *         }
 *     }
 * }
 *
 * {@link ServerBootstrap} bootstrap = ...;
 * ...
 * bootstrap.childHandler(new MyChannelInitializer());
 * ...
 * </pre>
 *
 * @see ReadTimeoutHandler
 * @see WriteTimeoutHandler
 */
// CLR: a TimeSpan replaces each (long duration, TimeUnit unit) pair (docs/common-clr-design.md, "Monotonic time and
// duration conversion"); a positive TimeSpan converts to nanoseconds with saturation, as TimeUnit.toNanos does, and the
// nanosecond delays passed to the executor round up to the 100 ns TimeSpan tick ("The smallest unit of time is the tick,
// which is equal to 100 nanoseconds", https://learn.microsoft.com/dotnet/api/system.timespan.ticks), so an idle task
// runs at most 99 ns after its Java deadline and never before it. Future<?> is the CancellationTokenSource of the
// scheduled work, as AbstractNioChannel's connectTimeoutFuture (src/Netty.NET.Transport/Channels/Nio/AbstractNioChannel.cs).
public class IdleStateHandler : ChannelDuplexHandler
{
    // CLR: TimeUnit.MILLISECONDS.toNanos(1).
    private static readonly long MIN_TIMEOUT_NANOS = TimeSpan.TicksPerMillisecond * 100;

    private readonly bool observeOutput;
    private readonly long readerIdleTimeNanos;
    private readonly long writerIdleTimeNanos;
    private readonly long allIdleTimeNanos;

    private Ticker ticker = Ticker.SystemTicker();

    private CancellationTokenSource readerIdleTimeout;
    private long lastReadTime;
    private bool firstReaderIdleEvent = true;

    private CancellationTokenSource writerIdleTimeout;
    private long lastWriteTime;
    private bool firstWriterIdleEvent = true;

    private CancellationTokenSource allIdleTimeout;
    private bool firstAllIdleEvent = true;

    private byte state;
    private const byte ST_INITIALIZED = 1;
    private const byte ST_DESTROYED = 2;

    private bool reading;

    private long lastChangeCheckTimeStamp;
    private int lastMessageHashCode;
    private long lastPendingWriteBytes;
    private long lastFlushProgress;
    // Not create a new ChannelFutureListener per write operation to reduce GC pressure.
    // CLR: the ChannelFutureListener is an Action<Task> continuation, assigned in the constructor because a field
    // initializer cannot reference the instance (CS0236).
    private readonly Action<Task> writeListener;
    // CLR: the TaskScheduler of writeListener, created in handlerAdded with the ticker: Common's
    // EventExecutorTaskScheduler over ctx.Executor, which notifies it as DefaultPromise.notifyListeners() does
    // (src/Netty.NET.Common/Concurrent/EventExecutorTaskScheduler.cs).
    private TaskScheduler writeListenerScheduler;

    /**
     * Creates a new instance firing {@link IdleStateEvent}s.
     *
     * @param readerIdleTimeSeconds
     *        an {@link IdleStateEvent} whose state is {@link IdleState#READER_IDLE}
     *        will be triggered when no read was performed for the specified
     *        period of time.  Specify {@code 0} to disable.
     * @param writerIdleTimeSeconds
     *        an {@link IdleStateEvent} whose state is {@link IdleState#WRITER_IDLE}
     *        will be triggered when no write was performed for the specified
     *        period of time.  Specify {@code 0} to disable.
     * @param allIdleTimeSeconds
     *        an {@link IdleStateEvent} whose state is {@link IdleState#ALL_IDLE}
     *        will be triggered when neither read nor write was performed for
     *        the specified period of time.  Specify {@code 0} to disable.
     */
    public IdleStateHandler(
            int readerIdleTimeSeconds,
            int writerIdleTimeSeconds,
            int allIdleTimeSeconds)
        : this(TimeSpan.FromSeconds(readerIdleTimeSeconds), TimeSpan.FromSeconds(writerIdleTimeSeconds),
             TimeSpan.FromSeconds(allIdleTimeSeconds))
    {
    }

    /**
     * @see #IdleStateHandler(boolean, long, long, long, TimeUnit)
     */
    public IdleStateHandler(
            TimeSpan readerIdleTime, TimeSpan writerIdleTime, TimeSpan allIdleTime)
        : this(false, readerIdleTime, writerIdleTime, allIdleTime)
    {
    }

    /**
     * Creates a new instance firing {@link IdleStateEvent}s.
     *
     * @param observeOutput
     *        whether or not the consumption of {@code bytes} should be taken into
     *        consideration when assessing write idleness. The default is {@code false}.
     * @param readerIdleTime
     *        an {@link IdleStateEvent} whose state is {@link IdleState#READER_IDLE}
     *        will be triggered when no read was performed for the specified
     *        period of time.  Specify {@code 0} to disable.
     * @param writerIdleTime
     *        an {@link IdleStateEvent} whose state is {@link IdleState#WRITER_IDLE}
     *        will be triggered when no write was performed for the specified
     *        period of time.  Specify {@code 0} to disable.
     * @param allIdleTime
     *        an {@link IdleStateEvent} whose state is {@link IdleState#ALL_IDLE}
     *        will be triggered when neither read nor write was performed for
     *        the specified period of time.  Specify {@code 0} to disable.
     * @param unit
     *        the {@link TimeUnit} of {@code readerIdleTime},
     *        {@code writeIdleTime}, and {@code allIdleTime}
     */
    // CLR: the TimeSpan arguments carry their unit, so the TimeUnit parameter and its checkNotNull have no counterpart.
    public IdleStateHandler(bool observeOutput,
            TimeSpan readerIdleTime, TimeSpan writerIdleTime, TimeSpan allIdleTime)
    {
        this.observeOutput = observeOutput;

        if (readerIdleTime <= TimeSpan.Zero)
        {
            readerIdleTimeNanos = 0;
        }
        else
        {
            readerIdleTimeNanos = Math.Max(ToNanos(readerIdleTime), MIN_TIMEOUT_NANOS);
        }
        if (writerIdleTime <= TimeSpan.Zero)
        {
            writerIdleTimeNanos = 0;
        }
        else
        {
            writerIdleTimeNanos = Math.Max(ToNanos(writerIdleTime), MIN_TIMEOUT_NANOS);
        }
        if (allIdleTime <= TimeSpan.Zero)
        {
            allIdleTimeNanos = 0;
        }
        else
        {
            allIdleTimeNanos = Math.Max(ToNanos(allIdleTime), MIN_TIMEOUT_NANOS);
        }

        writeListener = future =>
        {
            lastWriteTime = ticker.NanoTime();
            firstWriterIdleEvent = firstAllIdleEvent = true;
        };
    }

    /**
     * Return the readerIdleTime that was given when instance this class in milliseconds.
     *
     */
    public long GetReaderIdleTimeInMillis()
    {
        return readerIdleTimeNanos / 1_000_000;
    }

    /**
     * Return the writerIdleTime that was given when instance this class in milliseconds.
     *
     */
    public long GetWriterIdleTimeInMillis()
    {
        return writerIdleTimeNanos / 1_000_000;
    }

    /**
     * Return the allIdleTime that was given when instance this class in milliseconds.
     *
     */
    public long GetAllIdleTimeInMillis()
    {
        return allIdleTimeNanos / 1_000_000;
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        this.ticker = ctx.Executor.Ticker();
        writeListenerScheduler = new EventExecutorTaskScheduler(() => ctx.Executor);
        if (ctx.Channel.IsActive && ctx.Channel.IsRegistered)
        {
            // channelActive() event has been fired already, which means this.channelActive() will
            // not be invoked. We have to initialize here instead.
            Initialize(ctx);
        }
        else
        {
            // channelActive() event has not been fired yet.  this.channelActive() will be invoked
            // and initialization will occur there.
        }
    }

    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        Destroy();
    }

    public override void ChannelRegistered(IChannelHandlerContext ctx)
    {
        // Initialize early if channel is active already.
        if (ctx.Channel.IsActive)
        {
            Initialize(ctx);
        }
        base.ChannelRegistered(ctx);
    }

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        // This method will be invoked only if this handler was added
        // before channelActive() event is fired.  If a user adds this handler
        // after the channelActive() event, initialize() will be called by beforeAdd().
        Initialize(ctx);
        base.ChannelActive(ctx);
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        Destroy();
        base.ChannelInactive(ctx);
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        if (readerIdleTimeNanos > 0 || allIdleTimeNanos > 0)
        {
            reading = true;
            firstReaderIdleEvent = firstAllIdleEvent = true;
        }
        ctx.FireChannelRead(msg);
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        if ((readerIdleTimeNanos > 0 || allIdleTimeNanos > 0) && reading)
        {
            lastReadTime = ticker.NanoTime();
            reading = false;
        }
        ctx.FireChannelReadComplete();
    }

    // CLR: write(ctx, msg, promise) is WriteAsync, whose returned Task replaces the promise (docs/codec-base-porting.md,
    // "Outbound operations"); there is no void promise to unvoid(). addListener(writeListener) is a continuation on
    // writeListenerScheduler, which runs it when the write completes on the event loop, as Java notifies the listener.
    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        // Allow writing with void promise if handler is only configured for read timeout events.
        if (writerIdleTimeNanos > 0 || allIdleTimeNanos > 0)
        {
            Task future = ctx.WriteAsync(msg, cancellationToken);
            _ = future.ContinueWith(writeListener, CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, writeListenerScheduler);
            return future;
        }
        else
        {
            return ctx.WriteAsync(msg, cancellationToken);
        }
    }

    /**
     * Reset the read timeout. As this handler is not thread-safe, this method <b>must</b> be called on the event loop.
     */
    public void ResetReadTimeout()
    {
        if (readerIdleTimeNanos > 0 || allIdleTimeNanos > 0)
        {
            lastReadTime = ticker.NanoTime();
            reading = false;
            firstReaderIdleEvent = firstAllIdleEvent = true;
        }
    }

    /**
     * Reset the write timeout. As this handler is not thread-safe, this method <b>must</b> be called on the event loop.
     */
    public void ResetWriteTimeout()
    {
        if (writerIdleTimeNanos > 0 || allIdleTimeNanos > 0)
        {
            lastWriteTime = ticker.NanoTime();
            firstWriterIdleEvent = firstAllIdleEvent = true;
        }
    }

    private void Initialize(IChannelHandlerContext ctx)
    {
        // Avoid the case where destroy() is called before scheduling timeouts.
        // See: https://github.com/netty/netty/issues/143
        switch (state)
        {
            case 1:
            case 2:
                return;
            default:
                break;
        }

        state = ST_INITIALIZED;
        InitOutputChanged(ctx);

        lastReadTime = lastWriteTime = ticker.NanoTime();
        if (readerIdleTimeNanos > 0)
        {
            readerIdleTimeout = Schedule(ctx, new ReaderIdleTimeoutTask(this, ctx).Run,
                    NanosToTimeSpan(readerIdleTimeNanos));
        }
        if (writerIdleTimeNanos > 0)
        {
            writerIdleTimeout = Schedule(ctx, new WriterIdleTimeoutTask(this, ctx).Run,
                    NanosToTimeSpan(writerIdleTimeNanos));
        }
        if (allIdleTimeNanos > 0)
        {
            allIdleTimeout = Schedule(ctx, new AllIdleTimeoutTask(this, ctx).Run,
                    NanosToTimeSpan(allIdleTimeNanos));
        }
    }

    /**
     * This method is visible for testing!
     */
    // CLR: ctx.executor().schedule(task, delay, unit) is ScheduleAsync(task, delay, token) on the executor; the returned
    // CancellationTokenSource stands for the Future<?> (its cancel(false) is Cancel()), and the scheduled Task is not
    // observed, as nothing observes the Java future's result.
    internal virtual CancellationTokenSource Schedule(IChannelHandlerContext ctx, Action task, TimeSpan delay)
    {
        CancellationTokenSource future = new CancellationTokenSource();
        _ = ctx.Executor.ScheduleAsync(task, delay, future.Token);
        return future;
    }

    private void Destroy()
    {
        state = ST_DESTROYED;

        if (readerIdleTimeout != null)
        {
            readerIdleTimeout.Cancel();
            readerIdleTimeout = null;
        }
        if (writerIdleTimeout != null)
        {
            writerIdleTimeout.Cancel();
            writerIdleTimeout = null;
        }
        if (allIdleTimeout != null)
        {
            allIdleTimeout.Cancel();
            allIdleTimeout = null;
        }
    }

    /**
     * Is called when an {@link IdleStateEvent} should be fired. This implementation calls
     * {@link ChannelHandlerContext#fireUserEventTriggered(Object)}.
     */
    protected virtual void ChannelIdle(IChannelHandlerContext ctx, IdleStateEvent evt)
    {
        ctx.FireUserEventTriggered(evt);
    }

    /**
     * Returns a {@link IdleStateEvent}.
     */
    // CLR: the message prints first as Java does ("true"/"false"); bool.ToString() gives "True"/"False"
    // (https://learn.microsoft.com/dotnet/api/system.boolean.tostring).
    protected virtual IdleStateEvent NewIdleStateEvent(IdleState state, bool first)
    {
        switch (state)
        {
            case IdleState.ALL_IDLE:
                return first ? IdleStateEvent.FIRST_ALL_IDLE_STATE_EVENT : IdleStateEvent.ALL_IDLE_STATE_EVENT;
            case IdleState.READER_IDLE:
                return first ? IdleStateEvent.FIRST_READER_IDLE_STATE_EVENT : IdleStateEvent.READER_IDLE_STATE_EVENT;
            case IdleState.WRITER_IDLE:
                return first ? IdleStateEvent.FIRST_WRITER_IDLE_STATE_EVENT : IdleStateEvent.WRITER_IDLE_STATE_EVENT;
            default:
                throw new ArgumentException("Unhandled: state=" + state + ", first=" + (first ? "true" : "false"));
        }
    }

    /**
     * @see #hasOutputChanged(ChannelHandlerContext, boolean)
     */
    // CLR: Channel.Unsafe is folded into AbstractChannel, whose OutboundBuffer is unsafe().outboundBuffer()
    // (src/Netty.NET.Transport/Channels/AbstractChannel.cs); a channel that is not an AbstractChannel has no buffer and
    // stands for the null buffer.
    private void InitOutputChanged(IChannelHandlerContext ctx)
    {
        if (observeOutput)
        {
            IChannel channel = ctx.Channel;
            ChannelOutboundBuffer buf = (channel as AbstractChannel)?.OutboundBuffer;

            if (buf != null)
            {
                lastMessageHashCode = RuntimeHelpers.GetHashCode(buf.Current);
                lastPendingWriteBytes = buf.TotalPendingWriteBytes;
                lastFlushProgress = buf.CurrentProgress;
            }
        }
    }

    /**
     * Returns {@code true} if and only if the {@link IdleStateHandler} was constructed
     * with {@link #observeOutput} enabled and there has been an observed change in the
     * {@link ChannelOutboundBuffer} between two consecutive calls of this method.
     *
     * https://github.com/netty/netty/issues/6150
     */
    // CLR: System.identityHashCode is RuntimeHelpers.GetHashCode, the identity hash that ignores overrides (0 for null,
    // as identityHashCode(null)) (https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.runtimehelpers.gethashcode).
    // The Transport port keeps a closed ChannelOutboundBuffer instead of a null one; the idle tasks return before this
    // method once the channel is closed, so that buffer is not inspected.
    private bool HasOutputChanged(IChannelHandlerContext ctx, bool first)
    {
        if (observeOutput)
        {

            // We can take this shortcut if the ChannelPromises that got passed into write()
            // appear to complete. It indicates "change" on message level and we simply assume
            // that there's change happening on byte level. If the user doesn't observe channel
            // writability events then they'll eventually OOME and there's clearly a different
            // problem and idleness is least of their concerns.
            if (lastChangeCheckTimeStamp != lastWriteTime)
            {
                lastChangeCheckTimeStamp = lastWriteTime;

                // But this applies only if it's the non-first call.
                if (!first)
                {
                    return true;
                }
            }

            IChannel channel = ctx.Channel;
            ChannelOutboundBuffer buf = (channel as AbstractChannel)?.OutboundBuffer;

            if (buf != null)
            {
                int messageHashCode = RuntimeHelpers.GetHashCode(buf.Current);
                long pendingWriteBytes = buf.TotalPendingWriteBytes;

                if (messageHashCode != lastMessageHashCode || pendingWriteBytes != lastPendingWriteBytes)
                {
                    lastMessageHashCode = messageHashCode;
                    lastPendingWriteBytes = pendingWriteBytes;

                    if (!first)
                    {
                        return true;
                    }
                }

                long flushProgress = buf.CurrentProgress;
                if (flushProgress != lastFlushProgress)
                {
                    lastFlushProgress = flushProgress;
                    return !first;
                }
            }
        }

        return false;
    }

    // CLR: a positive TimeSpan as saturated nanoseconds (TimeUnit.toNanos saturates at Long.MAX_VALUE).
    private static long ToNanos(TimeSpan duration)
    {
        return duration.Ticks > long.MaxValue / 100 ? long.MaxValue : duration.Ticks * 100;
    }

    // CLR: a nanosecond delay as a TimeSpan, rounded up to the next 100 ns tick (see the class comment).
    private static TimeSpan NanosToTimeSpan(long nanos)
    {
        return nanos <= 0 ? TimeSpan.Zero : TimeSpan.FromTicks(nanos / 100 + (nanos % 100 == 0 ? 0 : 1));
    }

    // CLR: the abstract Runnable is a class with Run(); its instances are scheduled as the Run method group.
    private abstract class AbstractIdleTask
    {

        private readonly IChannelHandlerContext ctx;

        internal AbstractIdleTask(IChannelHandlerContext ctx)
        {
            this.ctx = ctx;
        }

        public void Run()
        {
            if (!ctx.Channel.IsOpen)
            {
                return;
            }

            Run(ctx);
        }

        protected abstract void Run(IChannelHandlerContext ctx);
    }

    // CLR: the inner classes reach the handler through an explicit reference (IdleStateHandler.this).
    private sealed class ReaderIdleTimeoutTask : AbstractIdleTask
    {
        private readonly IdleStateHandler handler;

        internal ReaderIdleTimeoutTask(IdleStateHandler handler, IChannelHandlerContext ctx) : base(ctx)
        {
            this.handler = handler;
        }

        // CLR: catch (Throwable t) is catch (Exception t).
        protected override void Run(IChannelHandlerContext ctx)
        {
            long nextDelay = handler.readerIdleTimeNanos;
            if (!handler.reading)
            {
                nextDelay -= handler.ticker.NanoTime() - handler.lastReadTime;
            }

            if (nextDelay <= 0)
            {
                // Reader is idle - set a new timeout and notify the callback.
                handler.readerIdleTimeout = handler.Schedule(ctx, Run, NanosToTimeSpan(handler.readerIdleTimeNanos));

                bool first = handler.firstReaderIdleEvent;
                handler.firstReaderIdleEvent = false;

                try
                {
                    IdleStateEvent @event = handler.NewIdleStateEvent(IdleState.READER_IDLE, first);
                    handler.ChannelIdle(ctx, @event);
                }
                catch (Exception t)
                {
                    ctx.FireExceptionCaught(t);
                }
            }
            else
            {
                // Read occurred before the timeout - set a new timeout with shorter delay.
                handler.readerIdleTimeout = handler.Schedule(ctx, Run, NanosToTimeSpan(nextDelay));
            }
        }
    }

    private sealed class WriterIdleTimeoutTask : AbstractIdleTask
    {
        private readonly IdleStateHandler handler;

        internal WriterIdleTimeoutTask(IdleStateHandler handler, IChannelHandlerContext ctx) : base(ctx)
        {
            this.handler = handler;
        }

        protected override void Run(IChannelHandlerContext ctx)
        {

            long lastWriteTime = handler.lastWriteTime;
            long nextDelay = handler.writerIdleTimeNanos - (handler.ticker.NanoTime() - lastWriteTime);
            if (nextDelay <= 0)
            {
                // Writer is idle - set a new timeout and notify the callback.
                handler.writerIdleTimeout = handler.Schedule(ctx, Run, NanosToTimeSpan(handler.writerIdleTimeNanos));

                bool first = handler.firstWriterIdleEvent;
                handler.firstWriterIdleEvent = false;

                try
                {
                    if (handler.HasOutputChanged(ctx, first))
                    {
                        return;
                    }

                    IdleStateEvent @event = handler.NewIdleStateEvent(IdleState.WRITER_IDLE, first);
                    handler.ChannelIdle(ctx, @event);
                }
                catch (Exception t)
                {
                    ctx.FireExceptionCaught(t);
                }
            }
            else
            {
                // Write occurred before the timeout - set a new timeout with shorter delay.
                handler.writerIdleTimeout = handler.Schedule(ctx, Run, NanosToTimeSpan(nextDelay));
            }
        }
    }

    private sealed class AllIdleTimeoutTask : AbstractIdleTask
    {
        private readonly IdleStateHandler handler;

        internal AllIdleTimeoutTask(IdleStateHandler handler, IChannelHandlerContext ctx) : base(ctx)
        {
            this.handler = handler;
        }

        protected override void Run(IChannelHandlerContext ctx)
        {

            long nextDelay = handler.allIdleTimeNanos;
            if (!handler.reading)
            {
                nextDelay -= handler.ticker.NanoTime() - Math.Max(handler.lastReadTime, handler.lastWriteTime);
            }
            if (nextDelay <= 0)
            {
                // Both reader and writer are idle - set a new timeout and
                // notify the callback.
                handler.allIdleTimeout = handler.Schedule(ctx, Run, NanosToTimeSpan(handler.allIdleTimeNanos));

                bool first = handler.firstAllIdleEvent;
                handler.firstAllIdleEvent = false;

                try
                {
                    if (handler.HasOutputChanged(ctx, first))
                    {
                        return;
                    }

                    IdleStateEvent @event = handler.NewIdleStateEvent(IdleState.ALL_IDLE, first);
                    handler.ChannelIdle(ctx, @event);
                }
                catch (Exception t)
                {
                    ctx.FireExceptionCaught(t);
                }
            }
            else
            {
                // Either read or write occurred before the timeout - set a new
                // timeout with shorter delay.
                handler.allIdleTimeout = handler.Schedule(ctx, Run, NanosToTimeSpan(nextDelay));
            }
        }
    }
}
