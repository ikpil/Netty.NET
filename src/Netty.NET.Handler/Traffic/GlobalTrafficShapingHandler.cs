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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Traffic;

/**
 * <p>This implementation of the {@link AbstractTrafficShapingHandler} is for global
 * traffic shaping, that is to say a global limitation of the bandwidth, whatever
 * the number of opened channels.</p>
 * <p>Note the index used in {@code OutboundBuffer.setUserDefinedWritability(index, boolean)} is <b>2</b>.</p>
 *
 * <p>The general use should be as follow:</p>
 * <ul>
 * <li><p>Create your unique GlobalTrafficShapingHandler like:</p>
 * <p><tt>GlobalTrafficShapingHandler myHandler = new GlobalTrafficShapingHandler(executor);</tt></p>
 * <p>The executor could be the underlying IO worker pool</p>
 * <p><tt>pipeline.addLast(myHandler);</tt></p>
 *
 * <p><b>Note that this handler has a Pipeline Coverage of "all" which means only one such handler must be created
 * and shared among all channels as the counter must be shared among all channels.</b></p>
 *
 * <p>Other arguments can be passed like write or read limitation (in bytes/s where 0 means no limitation)
 * or the check interval (in millisecond) that represents the delay between two computations of the
 * bandwidth and so the call back of the doAccounting method (0 means no accounting at all).</p>
 *
 * <p>A value of 0 means no accounting for checkInterval. If you need traffic shaping but no such accounting,
 * it is recommended to set a positive value, even if it is high since the precision of the
 * Traffic Shaping depends on the period where the traffic is computed. The highest the interval,
 * the less precise the traffic shaping will be. It is suggested as higher value something close
 * to 5 or 10 minutes.</p>
 *
 * <p>maxTimeToWait, by default set to 15s, allows to specify an upper bound of time shaping.</p>
 * </li>
 * <li>In your handler, you should consider to use the {@code channel.isWritable()} and
 * {@code channelWritabilityChanged(ctx)} to handle writability, or through
 * {@code future.addListener(new GenericFutureListener())} on the future returned by
 * {@code ctx.write()}.</li>
 * <li><p>You shall also consider to have object size in read or write operations relatively adapted to
 * the bandwidth you required: for instance having 10 MB objects for 10KB/s will lead to burst effect,
 * while having 100 KB objects for 1 MB/s should be smoothly handle by this TrafficShaping handler.</p></li>
 * <li><p>Some configuration methods will be taken as best effort, meaning
 * that all already scheduled traffics will not be
 * changed, but only applied to new traffics.</p>
 * So the expected usage of those methods are to be used not too often,
 * accordingly to the traffic shaping configuration.</li>
 * </ul>
 *
 * Be sure to call {@link #release()} once this handler is not needed anymore to release all internal resources.
 * This will not shutdown the {@link EventExecutor} as it may be shared, so you need to do this by your own.
 */
// CLR: ConcurrentMap<Integer, PerChannel>/ConcurrentHashMap is ConcurrentDictionary<int, PerChannel> (get is
// TryGetValue, put the indexer, remove TryRemove) and channel.hashCode() is GetHashCode(); ArrayDeque<ToSend> is
// LinkedList<ToSend>; AtomicLong is the Common AtomicLong; synchronized (perChannel) is lock (perChannel);
// ScheduledExecutorService is IEventExecutorGroup and EventExecutor is IEventExecutor (see TrafficCounter);
// java.nio.channels.ClosedChannelException is the Transport ClosedChannelException. See AbstractTrafficShapingHandler
// for the queued promise and the protected maxTime read through Volatile.Read.
[Sharable]
public class GlobalTrafficShapingHandler : AbstractTrafficShapingHandler
{
    /**
     * All queues per channel
     */
    private readonly ConcurrentDictionary<int, PerChannel> channelQueues = new ConcurrentDictionary<int, PerChannel>();

    /**
     * Global queues size
     */
    private readonly AtomicLong queuesSize = new AtomicLong();

    /**
     * Max size in the list before proposing to stop writing new objects from next handlers
     * for all channel (global)
     */
    internal long maxGlobalWriteSize = DEFAULT_MAX_SIZE * 100; // default 400MB

    private sealed class PerChannel
    {
        internal LinkedList<ToSend> messagesQueue;
        internal long queueSize;
        internal long lastWriteTimestamp;
        internal long lastReadTimestamp;
    }

    /**
     * Create the global TrafficCounter.
     */
    internal void CreateGlobalTrafficCounter(IEventExecutorGroup executor)
    {
        TrafficCounter tc = new TrafficCounter(this,
                ObjectUtil.CheckNotNull(executor, "executor"),
                "GlobalTC",
                Volatile.Read(ref checkInterval));

        SetTrafficCounter(tc);
        tc.Start();
    }

    protected override int UserDefinedWritabilityIndex()
    {
        return AbstractTrafficShapingHandler.GLOBAL_DEFAULT_USER_DEFINED_WRITABILITY_INDEX;
    }

    /**
     * Create a new instance.
     *
     * @param executor
     *            the {@link ScheduledExecutorService} to use for the {@link TrafficCounter}.
     * @param writeLimit
     *            0 or a limit in bytes/s
     * @param readLimit
     *            0 or a limit in bytes/s
     * @param checkInterval
     *            The delay between two computations of performances for
     *            channels or 0 if no stats are to be computed.
     * @param maxTime
     *            The maximum delay to wait in case of traffic excess.
     */
    public GlobalTrafficShapingHandler(IEventExecutorGroup executor, long writeLimit, long readLimit,
            long checkInterval, long maxTime) : base(writeLimit, readLimit, checkInterval, maxTime)
    {
        CreateGlobalTrafficCounter(executor);
    }

    /**
     * Create a new instance using
     * default max time as delay allowed value of 15000 ms.
     *
     * @param executor
     *          the {@link ScheduledExecutorService} to use for the {@link TrafficCounter}.
     * @param writeLimit
     *          0 or a limit in bytes/s
     * @param readLimit
     *          0 or a limit in bytes/s
     * @param checkInterval
     *          The delay between two computations of performances for
     *            channels or 0 if no stats are to be computed.
     */
    public GlobalTrafficShapingHandler(IEventExecutorGroup executor, long writeLimit,
            long readLimit, long checkInterval) : base(writeLimit, readLimit, checkInterval)
    {
        CreateGlobalTrafficCounter(executor);
    }

    /**
     * Create a new instance using default Check Interval value of 1000 ms and
     * default max time as delay allowed value of 15000 ms.
     *
     * @param executor
     *          the {@link ScheduledExecutorService} to use for the {@link TrafficCounter}.
     * @param writeLimit
     *          0 or a limit in bytes/s
     * @param readLimit
     *          0 or a limit in bytes/s
     */
    public GlobalTrafficShapingHandler(IEventExecutorGroup executor, long writeLimit,
            long readLimit) : base(writeLimit, readLimit)
    {
        CreateGlobalTrafficCounter(executor);
    }

    /**
     * Create a new instance using
     * default max time as delay allowed value of 15000 ms and no limit.
     *
     * @param executor
     *          the {@link ScheduledExecutorService} to use for the {@link TrafficCounter}.
     * @param checkInterval
     *          The delay between two computations of performances for
     *            channels or 0 if no stats are to be computed.
     */
    public GlobalTrafficShapingHandler(IEventExecutorGroup executor, long checkInterval) : base(checkInterval)
    {
        CreateGlobalTrafficCounter(executor);
    }

    /**
     * Create a new instance using default Check Interval value of 1000 ms and
     * default max time as delay allowed value of 15000 ms and no limit.
     *
     * @param executor
     *          the {@link EventExecutor} to use for the {@link TrafficCounter}.
     */
    public GlobalTrafficShapingHandler(IEventExecutor executor)
    {
        CreateGlobalTrafficCounter(executor);
    }

    /**
     * @return the maxGlobalWriteSize default value being 400 MB.
     */
    public long GetMaxGlobalWriteSize()
    {
        return maxGlobalWriteSize;
    }

    /**
     * Note the change will be taken as best effort, meaning
     * that all already scheduled traffics will not be
     * changed, but only applied to new traffics.<br>
     * So the expected usage of this method is to be used not too often,
     * accordingly to the traffic shaping configuration.
     *
     * @param maxGlobalWriteSize the maximum Global Write Size allowed in the buffer
     *            globally for all channels before write suspended is set,
     *            default value being 400 MB.
     */
    public void SetMaxGlobalWriteSize(long maxGlobalWriteSize)
    {
        this.maxGlobalWriteSize = maxGlobalWriteSize;
    }

    /**
     * @return the global size of the buffers for all queues.
     */
    public long QueuesSize()
    {
        return queuesSize.Get();
    }

    /**
     * Release all internal resources of this instance.
     */
    public void Release()
    {
        trafficCounter.Stop();
    }

    private PerChannel GetOrSetPerChannel(IChannelHandlerContext ctx)
    {
        // ensure creation is limited to one thread per channel
        IChannel channel = ctx.Channel;
        int key = channel.GetHashCode();
        channelQueues.TryGetValue(key, out PerChannel perChannel);
        if (perChannel == null)
        {
            perChannel = new PerChannel();
            perChannel.messagesQueue = new LinkedList<ToSend>();
            perChannel.queueSize = 0L;
            perChannel.lastReadTimestamp = Traffic.TrafficCounter.MilliSecondFromNano();
            perChannel.lastWriteTimestamp = perChannel.lastReadTimestamp;
            channelQueues[key] = perChannel;
        }
        return perChannel;
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        GetOrSetPerChannel(ctx);
        base.HandlerAdded(ctx);
    }

    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        IChannel channel = ctx.Channel;
        int key = channel.GetHashCode();
        channelQueues.TryRemove(key, out PerChannel perChannel);
        if (perChannel != null)
        {
            // write operations need synchronization
            lock (perChannel)
            {
                if (channel.IsActive)
                {
                    foreach (ToSend toSend in perChannel.messagesQueue)
                    {
                        long size = CalculateSize(toSend.toSend);
                        trafficCounter.BytesRealWriteFlowControl(size);
                        perChannel.queueSize -= size;
                        queuesSize.AddAndGet(-size);
                        Write(ctx, toSend.toSend, toSend.promise, toSend.cancellationToken);
                    }
                }
                else if (perChannel.messagesQueue.Count != 0)
                {
                    queuesSize.AddAndGet(-perChannel.queueSize);
                    ClosedChannelException cause = new ClosedChannelException();
                    foreach (ToSend toSend in perChannel.messagesQueue)
                    {
                        ReleaseAndFailQueuedWrite(toSend.toSend, toSend.promise, cause);
                    }
                    perChannel.queueSize = 0;
                }
                perChannel.messagesQueue.Clear();
            }
        }
        ReleaseWriteSuspended(ctx);
        ReleaseReadSuspended(ctx);
        base.HandlerRemoved(ctx);
    }

    protected internal override long CheckWaitReadTime(IChannelHandlerContext ctx, long wait, long now)
    {
        int key = ctx.Channel.GetHashCode();
        channelQueues.TryGetValue(key, out PerChannel perChannel);
        if (perChannel != null)
        {
            long maxTime = Volatile.Read(ref this.maxTime);
            if (wait > maxTime && now + wait - perChannel.lastReadTimestamp > maxTime)
            {
                wait = maxTime;
            }
        }
        return wait;
    }

    protected internal override void InformReadOperation(IChannelHandlerContext ctx, long now)
    {
        int key = ctx.Channel.GetHashCode();
        channelQueues.TryGetValue(key, out PerChannel perChannel);
        if (perChannel != null)
        {
            perChannel.lastReadTimestamp = now;
        }
    }

    // CLR: ChannelPromise promise is the TaskCompletionSource and CancellationToken of the queued write.
    private sealed class ToSend
    {
        internal readonly long relativeTimeAction;
        internal readonly object toSend;
        internal readonly long size;
        internal readonly TaskCompletionSource promise;
        internal readonly CancellationToken cancellationToken;

        internal ToSend(long delay, object toSend, long size, TaskCompletionSource promise,
                CancellationToken cancellationToken)
        {
            relativeTimeAction = delay;
            this.toSend = toSend;
            this.size = size;
            this.promise = promise;
            this.cancellationToken = cancellationToken;
        }
    }

    protected internal override Task SubmitWrite(IChannelHandlerContext ctx, object msg,
            long size, long writedelay, long now,
            CancellationToken cancellationToken)
    {
        IChannel channel = ctx.Channel;
        int key = channel.GetHashCode();
        channelQueues.TryGetValue(key, out PerChannel perChannel);
        if (perChannel == null)
        {
            // in case write occurs before handlerAdded is raised for this handler
            // imply a synchronized only if needed
            perChannel = GetOrSetPerChannel(ctx);
        }
        ToSend newToSend;
        long delay = writedelay;
        bool globalSizeExceeded = false;
        // write operations need synchronization
        lock (perChannel)
        {
            if (writedelay == 0 && perChannel.messagesQueue.Count == 0)
            {
                trafficCounter.BytesRealWriteFlowControl(size);
                Task future = ctx.WriteAsync(msg, cancellationToken);
                perChannel.lastWriteTimestamp = now;
                return future;
            }
            long maxTime = Volatile.Read(ref this.maxTime);
            if (delay > maxTime && now + delay - perChannel.lastWriteTimestamp > maxTime)
            {
                delay = maxTime;
            }
            newToSend = new ToSend(delay + now, msg, size, NewPromise(cancellationToken), cancellationToken);
            perChannel.messagesQueue.AddLast(newToSend);
            perChannel.queueSize += size;
            queuesSize.AddAndGet(size);
            CheckWriteSuspend(ctx, delay, perChannel.queueSize);
            if (queuesSize.Get() > maxGlobalWriteSize)
            {
                globalSizeExceeded = true;
            }
        }
        if (globalSizeExceeded)
        {
            SetUserDefinedWritability(ctx, false);
        }
        long futureNow = newToSend.relativeTimeAction;
        PerChannel forSchedule = perChannel;
        _ = ctx.Executor.ScheduleAsync(() => SendAllValid(ctx, forSchedule, futureNow), TimeSpan.FromMilliseconds(delay));
        return newToSend.promise.Task;
    }

    private void SendAllValid(IChannelHandlerContext ctx, PerChannel perChannel, long now)
    {
        // write operations need synchronization
        lock (perChannel)
        {
            ToSend newToSend = PollFirst(perChannel.messagesQueue);
            for (; newToSend != null; newToSend = PollFirst(perChannel.messagesQueue))
            {
                if (newToSend.relativeTimeAction <= now)
                {
                    long size = newToSend.size;
                    trafficCounter.BytesRealWriteFlowControl(size);
                    perChannel.queueSize -= size;
                    queuesSize.AddAndGet(-size);
                    Write(ctx, newToSend.toSend, newToSend.promise, newToSend.cancellationToken);
                    perChannel.lastWriteTimestamp = now;
                }
                else
                {
                    perChannel.messagesQueue.AddFirst(newToSend);
                    break;
                }
            }
            if (perChannel.messagesQueue.Count == 0)
            {
                ReleaseWriteSuspended(ctx);
            }
        }
        ctx.Flush();
    }
}
