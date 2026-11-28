/*
 * Copyright 2011 The Netty Project
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
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Traffic;

/**
 * <p>AbstractTrafficShapingHandler allows to limit the global bandwidth
 * (see {@link GlobalTrafficShapingHandler}) or per session
 * bandwidth (see {@link ChannelTrafficShapingHandler}), as traffic shaping.
 * It allows you to implement an almost real time monitoring of the bandwidth using
 * the monitors from {@link TrafficCounter} that will call back every checkInterval
 * the method doAccounting of this handler.</p>
 *
 * <p>If you want for any particular reasons to stop the monitoring (accounting) or to change
 * the read/write limit or the check interval, several methods allow that for you:</p>
 * <ul>
 * <li><tt>configure</tt> allows you to change read or write limits, or the checkInterval</li>
 * <li><tt>getTrafficCounter</tt> allows you to have access to the TrafficCounter and so to stop
 * or start the monitoring, to change the checkInterval directly, or to have access to its values.</li>
 * </ul>
 */
// CLR: shared adaptations of the traffic package:
// - A volatile long field is a long read and written through Volatile.Read/Volatile.Write, because C# volatile fields
//   cannot be long (https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/volatile).
// - write(ctx, msg, promise) is WriteAsync and submitWrite returns the Task of the write (docs/codec-base-porting.md,
//   "Outbound operations"). A write that is written at once returns ctx.WriteAsync's Task; a queued write keeps the
//   promise as a TaskCompletionSource whose Task WriteAsync returned (NewPromise) and the caller's CancellationToken,
//   which is handed to ctx.WriteAsync when the message is written (Write) and which cancels the queued promise at once,
//   as promise.cancel() completes a Java promise (CancellationToken.UnsafeRegister,
//   https://learn.microsoft.com/dotnet/api/system.threading.cancellationtoken.unsaferegister). The written Task's
//   outcome completes the promise with TaskCompletionSource.TrySetFromTask (docs/common-task-composition.md).
// - Package-private members that GlobalChannelTrafficShapingHandler overrides as protected (submitWrite,
//   checkWaitReadTime, informReadOperation) are protected internal, because a C# override keeps the accessibility of
//   the member it overrides (CS0507); a subclass in another assembly can therefore override submitWrite, which Java
//   only allows inside the package.
// - The method trafficCounter() hides the type TrafficCounter in expressions inside these classes, so static calls
//   name the type as Traffic.TrafficCounter.
// - Debug log messages keep the Java text of booleans ("true"/"false"; bool.ToString() gives "True",
//   https://learn.microsoft.com/dotnet/api/system.boolean.tostring); numbers in toString() use the invariant culture.
public abstract class AbstractTrafficShapingHandler : ChannelDuplexHandler
{
    private static readonly IInternalLogger logger =
            InternalLoggerFactory.GetInstance(typeof(AbstractTrafficShapingHandler));
    /**
     * Default delay between two checks: 1s
     */
    public const long DEFAULT_CHECK_INTERVAL = 1000;

   /**
    * Default max delay in case of traffic shaping
    * (during which no communication will occur).
    * Shall be less than TIMEOUT. Here half of "standard" 30s
    */
    public const long DEFAULT_MAX_TIME = 15000;

    /**
     * Default max size to not exceed in buffer (write only).
     */
    internal const long DEFAULT_MAX_SIZE = 4 * 1024 * 1024L;

    /**
     * Default minimal time to wait: 10ms
     */
    internal const long MINIMAL_WAIT = 10;

    /**
     * Traffic Counter
     */
    protected TrafficCounter trafficCounter;

    /**
     * Limit in B/s to apply to write
     */
    private long writeLimit;

    /**
     * Limit in B/s to apply to read
     */
    private long readLimit;

    /**
     * Max delay in wait
     */
    protected long maxTime = DEFAULT_MAX_TIME; // default 15 s

    /**
     * Delay between two performance snapshots
     */
    protected long checkInterval = DEFAULT_CHECK_INTERVAL; // default 1 s

    // CLR: AttributeKey<T> requires a reference type, so the Boolean attribute holds a boxed bool as object; the
    // Runnable attribute holds an Action.
    internal static readonly AttributeKey<object> READ_SUSPENDED = AttributeKey<object>
            .ValueOf(typeof(AbstractTrafficShapingHandler).FullName + ".READ_SUSPENDED");
    internal static readonly AttributeKey<Action> REOPEN_TASK = AttributeKey<Action>.ValueOf(typeof(AbstractTrafficShapingHandler)
            .FullName + ".REOPEN_TASK");

    /**
     * Max time to delay before proposing to stop writing new objects from next handlers
     */
    internal long maxWriteDelay = 4 * DEFAULT_CHECK_INTERVAL; // default 4 s
    /**
     * Max size in the list before proposing to stop writing new objects from next handlers
     */
    internal long maxWriteSize = DEFAULT_MAX_SIZE; // default 4MB

    /**
     * Rank in UserDefinedWritability (1 for Channel, 2 for Global TrafficShapingHandler).
     * Set in final constructor. Must be between 1 and 31
     */
    internal readonly int userDefinedWritabilityIndex;

    /**
     * Default value for Channel UserDefinedWritability index
     */
    internal const int CHANNEL_DEFAULT_USER_DEFINED_WRITABILITY_INDEX = 1;

    /**
     * Default value for Global UserDefinedWritability index
     */
    internal const int GLOBAL_DEFAULT_USER_DEFINED_WRITABILITY_INDEX = 2;

    /**
     * Default value for GlobalChannel UserDefinedWritability index
     */
    internal const int GLOBALCHANNEL_DEFAULT_USER_DEFINED_WRITABILITY_INDEX = 3;

    /**
     * @param newTrafficCounter
     *            the TrafficCounter to set
     */
    internal void SetTrafficCounter(TrafficCounter newTrafficCounter)
    {
        trafficCounter = newTrafficCounter;
    }

    /**
     * @return the index to be used by the TrafficShapingHandler to manage the user defined writability.
     *              For Channel TSH it is defined as {@value #CHANNEL_DEFAULT_USER_DEFINED_WRITABILITY_INDEX},
     *              for Global TSH it is defined as {@value #GLOBAL_DEFAULT_USER_DEFINED_WRITABILITY_INDEX},
     *              for GlobalChannel TSH it is defined as
     *              {@value #GLOBALCHANNEL_DEFAULT_USER_DEFINED_WRITABILITY_INDEX}.
     */
    protected virtual int UserDefinedWritabilityIndex()
    {
        return CHANNEL_DEFAULT_USER_DEFINED_WRITABILITY_INDEX;
    }

    /**
     * @param writeLimit
     *          0 or a limit in bytes/s
     * @param readLimit
     *          0 or a limit in bytes/s
     * @param checkInterval
     *            The delay between two computations of performances for
     *            channels or 0 if no stats are to be computed.
     * @param maxTime
     *            The maximum delay to wait in case of traffic excess.
     *            Must be positive.
     */
    // CLR: as in Java, the constructor calls the virtual userDefinedWritabilityIndex() of the subclass.
    protected AbstractTrafficShapingHandler(long writeLimit, long readLimit, long checkInterval, long maxTime)
    {
        Volatile.Write(ref this.maxTime, ObjectUtil.CheckPositive(maxTime, "maxTime"));

        userDefinedWritabilityIndex = UserDefinedWritabilityIndex();
        Volatile.Write(ref this.writeLimit, writeLimit);
        Volatile.Write(ref this.readLimit, readLimit);
        Volatile.Write(ref this.checkInterval, checkInterval);
    }

    /**
     * Constructor using default max time as delay allowed value of {@value #DEFAULT_MAX_TIME} ms.
     * @param writeLimit
     *            0 or a limit in bytes/s
     * @param readLimit
     *            0 or a limit in bytes/s
     * @param checkInterval
     *            The delay between two computations of performances for
     *            channels or 0 if no stats are to be computed.
     */
    protected AbstractTrafficShapingHandler(long writeLimit, long readLimit, long checkInterval)
        : this(writeLimit, readLimit, checkInterval, DEFAULT_MAX_TIME)
    {
    }

    /**
     * Constructor using default Check Interval value of {@value #DEFAULT_CHECK_INTERVAL} ms and
     * default max time as delay allowed value of {@value #DEFAULT_MAX_TIME} ms.
     *
     * @param writeLimit
     *          0 or a limit in bytes/s
     * @param readLimit
     *          0 or a limit in bytes/s
     */
    protected AbstractTrafficShapingHandler(long writeLimit, long readLimit)
        : this(writeLimit, readLimit, DEFAULT_CHECK_INTERVAL, DEFAULT_MAX_TIME)
    {
    }

    /**
     * Constructor using NO LIMIT, default Check Interval value of {@value #DEFAULT_CHECK_INTERVAL} ms and
     * default max time as delay allowed value of {@value #DEFAULT_MAX_TIME} ms.
     */
    protected AbstractTrafficShapingHandler() : this(0, 0, DEFAULT_CHECK_INTERVAL, DEFAULT_MAX_TIME)
    {
    }

    /**
     * Constructor using NO LIMIT and
     * default max time as delay allowed value of {@value #DEFAULT_MAX_TIME} ms.
     *
     * @param checkInterval
     *            The delay between two computations of performances for
     *            channels or 0 if no stats are to be computed.
     */
    protected AbstractTrafficShapingHandler(long checkInterval) : this(0, 0, checkInterval, DEFAULT_MAX_TIME)
    {
    }

    /**
     * Change the underlying limitations and check interval.
     * <p>Note the change will be taken as best effort, meaning
     * that all already scheduled traffics will not be
     * changed, but only applied to new traffics.</p>
     * <p>So the expected usage of this method is to be used not too often,
     * accordingly to the traffic shaping configuration.</p>
     *
     * @param newWriteLimit The new write limit (in bytes)
     * @param newReadLimit The new read limit (in bytes)
     * @param newCheckInterval The new check interval (in milliseconds)
     */
    public void Configure(long newWriteLimit, long newReadLimit,
            long newCheckInterval)
    {
        Configure(newWriteLimit, newReadLimit);
        Configure(newCheckInterval);
    }

    /**
     * Change the underlying limitations.
     * <p>Note the change will be taken as best effort, meaning
     * that all already scheduled traffics will not be
     * changed, but only applied to new traffics.</p>
     * <p>So the expected usage of this method is to be used not too often,
     * accordingly to the traffic shaping configuration.</p>
     *
     * @param newWriteLimit The new write limit (in bytes)
     * @param newReadLimit The new read limit (in bytes)
     */
    public void Configure(long newWriteLimit, long newReadLimit)
    {
        Volatile.Write(ref writeLimit, newWriteLimit);
        Volatile.Write(ref readLimit, newReadLimit);

        if (trafficCounter != null)
        {
            trafficCounter.ResetAccounting(Traffic.TrafficCounter.MilliSecondFromNano());
        }
    }

    /**
     * Change the check interval.
     *
     * @param newCheckInterval The new check interval (in milliseconds)
     */
    public void Configure(long newCheckInterval)
    {
        Volatile.Write(ref checkInterval, newCheckInterval);

        if (trafficCounter != null)
        {
            trafficCounter.Configure(Volatile.Read(ref checkInterval));
        }
    }

    /**
     * @return the writeLimit
     */
    public long GetWriteLimit()
    {
        return Volatile.Read(ref writeLimit);
    }

    /**
     * <p>Note the change will be taken as best effort, meaning
     * that all already scheduled traffics will not be
     * changed, but only applied to new traffics.</p>
     * <p>So the expected usage of this method is to be used not too often,
     * accordingly to the traffic shaping configuration.</p>
     *
     * @param writeLimit the writeLimit to set
     */
    public void SetWriteLimit(long writeLimit)
    {
        Volatile.Write(ref this.writeLimit, writeLimit);

        if (trafficCounter != null)
        {
            trafficCounter.ResetAccounting(Traffic.TrafficCounter.MilliSecondFromNano());
        }
    }

    /**
     * @return the readLimit
     */
    public long GetReadLimit()
    {
        return Volatile.Read(ref readLimit);
    }

    /**
     * <p>Note the change will be taken as best effort, meaning
     * that all already scheduled traffics will not be
     * changed, but only applied to new traffics.</p>
     * <p>So the expected usage of this method is to be used not too often,
     * accordingly to the traffic shaping configuration.</p>
     *
     * @param readLimit the readLimit to set
     */
    public void SetReadLimit(long readLimit)
    {
        Volatile.Write(ref this.readLimit, readLimit);

        if (trafficCounter != null)
        {
            trafficCounter.ResetAccounting(Traffic.TrafficCounter.MilliSecondFromNano());
        }
    }

    /**
     * @return the checkInterval
     */
    public long GetCheckInterval()
    {
        return Volatile.Read(ref checkInterval);
    }

    /**
     * @param checkInterval the interval in ms between each step check to set, default value being 1000 ms.
     */
    public void SetCheckInterval(long checkInterval)
    {
        Volatile.Write(ref this.checkInterval, checkInterval);

        if (trafficCounter != null)
        {
            trafficCounter.Configure(checkInterval);
        }
    }

    /**
     * <p>Note the change will be taken as best effort, meaning
     * that all already scheduled traffics will not be
     * changed, but only applied to new traffics.</p>
     * <p>So the expected usage of this method is to be used not too often,
     * accordingly to the traffic shaping configuration.</p>
     *
     * @param maxTime
     *            Max delay in wait, shall be less than TIME OUT in related protocol.
     *            Must be positive.
     */
    public void SetMaxTimeWait(long maxTime)
    {
        Volatile.Write(ref this.maxTime, ObjectUtil.CheckPositive(maxTime, "maxTime"));
    }

    /**
     * @return the max delay in wait to prevent TIME OUT
     */
    public long GetMaxTimeWait()
    {
        return Volatile.Read(ref maxTime);
    }

    /**
     * @return the maxWriteDelay
     */
    public long GetMaxWriteDelay()
    {
        return Volatile.Read(ref maxWriteDelay);
    }

    /**
     * <p>Note the change will be taken as best effort, meaning
     * that all already scheduled traffics will not be
     * changed, but only applied to new traffics.</p>
     * <p>So the expected usage of this method is to be used not too often,
     * accordingly to the traffic shaping configuration.</p>
     *
     * @param maxWriteDelay the maximum Write Delay in ms in the buffer allowed before write suspension is set.
     *              Must be positive.
     */
    public void SetMaxWriteDelay(long maxWriteDelay)
    {
        Volatile.Write(ref this.maxWriteDelay, ObjectUtil.CheckPositive(maxWriteDelay, "maxWriteDelay"));
    }

    /**
     * @return the maxWriteSize default being {@value #DEFAULT_MAX_SIZE} bytes.
     */
    public long GetMaxWriteSize()
    {
        return Volatile.Read(ref maxWriteSize);
    }

    /**
     * <p>Note that this limit is a best effort on memory limitation to prevent Out Of
     * Memory Exception. To ensure it works, the handler generating the write should
     * use one of the way provided by Netty to handle the capacity:</p>
     * <p>- the {@code Channel.isWritable()} property and the corresponding
     * {@code channelWritabilityChanged()}</p>
     * <p>- the {@code ChannelFuture.addListener(new GenericFutureListener())}</p>
     *
     * @param maxWriteSize the maximum Write Size allowed in the buffer
     *            per channel before write suspended is set,
     *            default being {@value #DEFAULT_MAX_SIZE} bytes.
     */
    public void SetMaxWriteSize(long maxWriteSize)
    {
        Volatile.Write(ref this.maxWriteSize, maxWriteSize);
    }

    /**
     * Called each time the accounting is computed from the TrafficCounters.
     * This method could be used for instance to implement almost real time accounting.
     *
     * @param counter
     *            the TrafficCounter that computes its performance
     */
    // CLR: protected internal, because TrafficCounter (another class of the package) calls it, as Java protected
    // admits the package.
    protected internal virtual void DoAccounting(TrafficCounter counter)
    {
        // NOOP by default
    }

    /**
     * Class to implement setReadable at fix time
     */
    // CLR: the Runnable is a class with Run(); it is stored in the REOPEN_TASK attribute and scheduled as the Run method
    // group.
    internal sealed class ReopenReadTimerTask
    {
        internal readonly IChannelHandlerContext ctx;
        internal ReopenReadTimerTask(IChannelHandlerContext ctx)
        {
            this.ctx = ctx;
        }

        public void Run()
        {
            IChannel channel = ctx.Channel;
            IChannelConfig config = channel.Config;
            if (!config.IsAutoRead() && IsHandlerActive(ctx))
            {
                // If AutoRead is False and Active is True, user make a direct setAutoRead(false)
                // Then Just reset the status
                if (logger.IsDebugEnabled())
                {
                    logger.Debug("Not unsuspend: " + Text(config.IsAutoRead()) + ':' +
                            Text(IsHandlerActive(ctx)));
                }

                channel.Attr(READ_SUSPENDED).Set(false);
            }
            else
            {
                // Anything else allows the handler to reset the AutoRead
                if (logger.IsDebugEnabled())
                {
                    if (config.IsAutoRead() && !IsHandlerActive(ctx))
                    {
                        if (logger.IsDebugEnabled())
                        {
                            logger.Debug("Unsuspend: " + Text(config.IsAutoRead()) + ':' +
                                    Text(IsHandlerActive(ctx)));
                        }
                    }
                    else
                    {
                        if (logger.IsDebugEnabled())
                        {
                            logger.Debug("Normal unsuspend: " + Text(config.IsAutoRead()) + ':'
                                    + Text(IsHandlerActive(ctx)));
                        }
                    }
                }

                channel.Attr(READ_SUSPENDED).Set(false);
                config.SetAutoRead(true);
                channel.Read();
            }

            if (logger.IsDebugEnabled())
            {
                logger.Debug("Unsuspend final status => " + Text(config.IsAutoRead()) + ':'
                        + Text(IsHandlerActive(ctx)));
            }
        }
    }

    /**
     * Release the Read suspension
     */
    internal void ReleaseReadSuspended(IChannelHandlerContext ctx)
    {
        IChannel channel = ctx.Channel;
        channel.Attr(READ_SUSPENDED).Set(false);
        channel.Config.SetAutoRead(true);
    }

    // CLR: ctx.executor().schedule(reopenTask, wait, MILLISECONDS) is ScheduleAsync with TimeSpan.FromMilliseconds; the
    // scheduled Task is not observed, as nothing observes the Java future.
    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        long size = CalculateSize(msg);
        long now = Traffic.TrafficCounter.MilliSecondFromNano();

        if (size > 0)
        {
            // compute the number of ms to wait before reopening the channel
            long wait = trafficCounter.ReadTimeToWait(size, Volatile.Read(ref readLimit), Volatile.Read(ref maxTime), now);
            wait = CheckWaitReadTime(ctx, wait, now);
            if (wait >= MINIMAL_WAIT)
            { // At least 10ms seems a minimal
                // time in order to try to limit the traffic
                // Only AutoRead AND HandlerActive True means Context Active
                IChannel channel = ctx.Channel;
                IChannelConfig config = channel.Config;

                if (logger.IsDebugEnabled())
                {
                    logger.Debug("Read suspend: " + wait + ':' + Text(config.IsAutoRead()) + ':'
                            + Text(IsHandlerActive(ctx)));
                }

                if (config.IsAutoRead() && IsHandlerActive(ctx))
                {
                    config.SetAutoRead(false);
                    channel.Attr(READ_SUSPENDED).Set(true);
                    // Create a Runnable to reactive the read if needed. If one was create before it will just be
                    // reused to limit object creation
                    IAttribute<Action> attr = channel.Attr(REOPEN_TASK);
                    Action reopenTask = attr.Get();
                    if (reopenTask == null)
                    {
                        reopenTask = new ReopenReadTimerTask(ctx).Run;
                        attr.Set(reopenTask);
                    }

                    _ = ctx.Executor.ScheduleAsync(reopenTask, TimeSpan.FromMilliseconds(wait));

                    if (logger.IsDebugEnabled())
                    {
                        logger.Debug("Suspend final status => " + Text(config.IsAutoRead()) + ':'
                                + Text(IsHandlerActive(ctx)) + " will reopened at: " + wait);
                    }
                }
            }
        }

        InformReadOperation(ctx, now);
        ctx.FireChannelRead(msg);
    }

    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        IChannel channel = ctx.Channel;
        if (channel.HasAttr(REOPEN_TASK))
        {
            //release the reopen task
            channel.Attr(REOPEN_TASK).Set(null);
        }

        base.HandlerRemoved(ctx);
    }

    /**
     * Method overridden in GTSH to take into account specific timer for the channel.
     * @param wait the wait delay computed in ms
     * @param now the relative now time in ms
     * @return the wait to use according to the context
     */
    protected internal virtual long CheckWaitReadTime(IChannelHandlerContext ctx, long wait, long now)
    {
        // no change by default
        return wait;
    }

    /**
     * Method overridden in GTSH to take into account specific timer for the channel.
     * @param now the relative now time in ms
     */
    protected internal virtual void InformReadOperation(IChannelHandlerContext ctx, long now)
    {
        // default noop
    }

    protected static bool IsHandlerActive(IChannelHandlerContext ctx)
    {
        object suspended = ctx.Channel.Attr(READ_SUSPENDED).Get();
        return suspended == null || false.Equals(suspended);
    }

    public override void Read(IChannelHandlerContext ctx)
    {
        if (IsHandlerActive(ctx))
        {
            // For Global Traffic (and Read when using EventLoop in pipeline) : check if READ_SUSPENDED is False
            ctx.Read();
        }
    }

    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        long size = CalculateSize(msg);
        long now = Traffic.TrafficCounter.MilliSecondFromNano();

        if (size > 0)
        {
            // compute the number of ms to wait before continue with the channel
            long wait = trafficCounter.WriteTimeToWait(size, Volatile.Read(ref writeLimit), Volatile.Read(ref maxTime), now);
            if (wait >= MINIMAL_WAIT)
            {
                if (logger.IsDebugEnabled())
                {
                    logger.Debug("Write suspend: " + wait + ':' + Text(ctx.Channel.Config.IsAutoRead()) + ':'
                            + Text(IsHandlerActive(ctx)));
                }

                return SubmitWrite(ctx, msg, size, wait, now, cancellationToken);
            }
        }

        // to maintain order of write
        return SubmitWrite(ctx, msg, size, 0, now, cancellationToken);
    }

    // CLR: @Deprecated without a @deprecated tag names no replacement, so the method is ported with [Obsolete].
    [Obsolete]
    protected Task SubmitWrite(IChannelHandlerContext ctx, object msg,
            long delay, CancellationToken cancellationToken)
    {
        return SubmitWrite(ctx, msg, CalculateSize(msg),
                delay, Traffic.TrafficCounter.MilliSecondFromNano(), cancellationToken);
    }

    protected internal abstract Task SubmitWrite(
            IChannelHandlerContext ctx, object msg, long size, long delay, long now, CancellationToken cancellationToken);

    /**
     * Releases the given {@code msg} and fails the given {@code promise} with the supplied {@code cause}.
     */
    internal static void ReleaseAndFailQueuedWrite(object msg, TaskCompletionSource promise, Exception cause)
    {
        ReferenceCountUtil.SafeRelease(msg);
        promise.TrySetException(cause);
    }

    // CLR: the promise of a queued write (see the class comment); a token that is already cancelled cancels it at once.
    internal static TaskCompletionSource NewPromise(CancellationToken cancellationToken)
    {
        TaskCompletionSource promise = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        if (cancellationToken.CanBeCanceled)
        {
            CancellationTokenRegistration registration = cancellationToken.UnsafeRegister(
                    static (state, token) => ((TaskCompletionSource)state).TrySetCanceled(token), promise);
            _ = promise.Task.ContinueWith(static (_, state) => ((CancellationTokenRegistration)state).Dispose(),
                    registration, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
        }

        return promise;
    }

    internal static void Write(IChannelHandlerContext ctx, object msg, TaskCompletionSource promise,
            CancellationToken cancellationToken)
    {
        Task future = ctx.WriteAsync(msg, cancellationToken);
        if (future.IsCompleted)
        {
            promise.TrySetFromTask(future);
        }
        else
        {
            _ = future.ContinueWith(static (f, state) => ((TaskCompletionSource)state).TrySetFromTask(f), promise,
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    // CLR: ArrayDeque.pollFirst() of the LinkedList<T> that stands for the ArrayDeque of queued writes (the BCL has no
    // double-ended array queue; LinkedList<T> adds and removes at both ends in O(1),
    // https://learn.microsoft.com/dotnet/api/system.collections.generic.linkedlist-1).
    internal static T PollFirst<T>(LinkedList<T> deque) where T : class
    {
        LinkedListNode<T> first = deque.First;
        if (first == null)
        {
            return null;
        }

        deque.RemoveFirst();
        return first.Value;
    }

    // CLR: Java string conversion of a boolean ("true"/"false"); see the class comment.
    internal static string Text(bool value)
    {
        return value ? "true" : "false";
    }

    public override void ChannelRegistered(IChannelHandlerContext ctx)
    {
        SetUserDefinedWritability(ctx, true);
        base.ChannelRegistered(ctx);
    }

    // CLR: Channel.Unsafe is folded into AbstractChannel, whose OutboundBuffer is unsafe().outboundBuffer()
    // (src/Netty.NET.Transport/Channels/AbstractChannel.cs). Java's outboundBuffer() is null once close() has started;
    // the Transport port keeps a closed buffer instead, so a channel that is no longer open stands for the null buffer,
    // as PendingBytesTracker does (src/Netty.NET.Transport/Channels/PendingBytesTracker.cs).
    internal void SetUserDefinedWritability(IChannelHandlerContext ctx, bool writable)
    {
        ChannelOutboundBuffer cob = ctx.Channel.IsOpen ? (ctx.Channel as AbstractChannel)?.OutboundBuffer : null;
        if (cob != null)
        {
            cob.SetUserDefinedWritability(userDefinedWritabilityIndex, writable);
        }
    }

    /**
     * Check the writability according to delay and size for the channel.
     * Set if necessary setUserDefinedWritability status.
     * @param delay the computed delay
     * @param queueSize the current queueSize
     */
    internal void CheckWriteSuspend(IChannelHandlerContext ctx, long delay, long queueSize)
    {
        if (queueSize > Volatile.Read(ref maxWriteSize) || delay > Volatile.Read(ref maxWriteDelay))
        {
            SetUserDefinedWritability(ctx, false);
        }
    }

    /**
     * Explicitly release the Write suspended status.
     */
    internal void ReleaseWriteSuspended(IChannelHandlerContext ctx)
    {
        SetUserDefinedWritability(ctx, true);
    }

    /**
     * @return the current TrafficCounter (if
     *         channel is still connected)
     */
    public TrafficCounter TrafficCounter()
    {
        return trafficCounter;
    }

    public override string ToString()
    {
        StringBuilder builder = new StringBuilder(290)
            .Append("TrafficShaping with Write Limit: ").Append(Volatile.Read(ref writeLimit).ToString(CultureInfo.InvariantCulture))
            .Append(" Read Limit: ").Append(Volatile.Read(ref readLimit).ToString(CultureInfo.InvariantCulture))
            .Append(" CheckInterval: ").Append(Volatile.Read(ref checkInterval).ToString(CultureInfo.InvariantCulture))
            .Append(" maxDelay: ").Append(Volatile.Read(ref maxWriteDelay).ToString(CultureInfo.InvariantCulture))
            .Append(" maxSize: ").Append(Volatile.Read(ref maxWriteSize).ToString(CultureInfo.InvariantCulture))
            .Append(" and Counter: ");

        if (trafficCounter != null)
        {
            builder.Append(trafficCounter);
        }
        else
        {
            builder.Append("none");
        }

        return builder.ToString();
    }

    /**
     * Calculate the size of the given {@link Object}.
     *
     * This implementation supports {@link ByteBuf}, {@link ByteBufHolder} and {@link FileRegion}.
     * Sub-classes may override this.
     * @param msg the msg for which the size should be calculated.
     * @return size the size of the msg or {@code -1} if unknown.
     */
    // CLR: ByteBufHolder is IByteBufHolder (content() is the Content property) and FileRegion is IFileRegion.
    protected virtual long CalculateSize(object msg)
    {
        if (msg is ByteBuf)
        {
            return ((ByteBuf)msg).ReadableBytes;
        }
        if (msg is IByteBufHolder)
        {
            return ((IByteBufHolder)msg).Content.ReadableBytes;
        }
        if (msg is IFileRegion)
        {
            return ((IFileRegion)msg).Count();
        }

        return -1;
    }
}
