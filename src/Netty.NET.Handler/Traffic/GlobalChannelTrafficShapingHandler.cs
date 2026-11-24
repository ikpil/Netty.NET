/*
 * Copyright 2014 The Netty Project
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
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Traffic;

/**
 * This implementation of the {@link AbstractTrafficShapingHandler} is for global
 * and per channel traffic shaping, that is to say a global limitation of the bandwidth, whatever
 * the number of opened channels and a per channel limitation of the bandwidth.<br><br>
 * This version shall not be in the same pipeline than other TrafficShapingHandler.<br><br>
 *
 * The general use should be as follow:<br>
 * <ul>
 * <li>Create your unique GlobalChannelTrafficShapingHandler like:<br><br>
 * <tt>GlobalChannelTrafficShapingHandler myHandler = new GlobalChannelTrafficShapingHandler(executor);</tt><br><br>
 * The executor could be the underlying IO worker pool<br>
 * <tt>pipeline.addLast(myHandler);</tt><br><br>
 *
 * <b>Note that this handler has a Pipeline Coverage of "all" which means only one such handler must be created
 * and shared among all channels as the counter must be shared among all channels.</b><br><br>
 *
 * Other arguments can be passed like write or read limitation (in bytes/s where 0 means no limitation)
 * or the check interval (in millisecond) that represents the delay between two computations of the
 * bandwidth and so the call back of the doAccounting method (0 means no accounting at all).<br>
 * Note that as this is a fusion of both Global and Channel Traffic Shaping, limits are in 2 sets,
 * respectively Global and Channel.<br><br>
 *
 * A value of 0 means no accounting for checkInterval. If you need traffic shaping but no such accounting,
 * it is recommended to set a positive value, even if it is high since the precision of the
 * Traffic Shaping depends on the period where the traffic is computed. The highest the interval,
 * the less precise the traffic shaping will be. It is suggested as higher value something close
 * to 5 or 10 minutes.<br><br>
 *
 * maxTimeToWait, by default set to 15s, allows to specify an upper bound of time shaping.<br><br>
 * </li>
 * <li>In your handler, you should consider to use the {@code channel.isWritable()} and
 * {@code channelWritabilityChanged(ctx)} to handle writability, or through
 * {@code future.addListener(new GenericFutureListener())} on the future returned by
 * {@code ctx.write()}.</li>
 * <li>You shall also consider to have object size in read or write operations relatively adapted to
 * the bandwidth you required: for instance having 10 MB objects for 10KB/s will lead to burst effect,
 * while having 100 KB objects for 1 MB/s should be smoothly handle by this TrafficShaping handler.<br><br></li>
 * <li>Some configuration methods will be taken as best effort, meaning
 * that all already scheduled traffics will not be
 * changed, but only applied to new traffics.<br>
 * So the expected usage of those methods are to be used not too often,
 * accordingly to the traffic shaping configuration.</li>
 * </ul><br>
 *
 * Be sure to call {@link #release()} once this handler is not needed anymore to release all internal resources.
 * This will not shutdown the {@link EventExecutor} as it may be shared, so you need to do this by your own.
 */
// CLR: as GlobalTrafficShapingHandler (ConcurrentDictionary<int, PerChannel>, LinkedList<ToSend>, Common AtomicLong,
// lock (perChannel), IEventExecutorGroup/IEventExecutor, the Transport ClosedChannelException, the queued promise of
// AbstractTrafficShapingHandler). channelQueues.values() is the ConcurrentDictionary Values copy (see
// GlobalChannelTrafficCounter). IllegalArgumentException is ArgumentException and its float text is the invariant
// float.ToString(), which prints MAX_DEVIATION as Java's Float.toString does ("0.4"). Collection<TrafficCounter> is
// ICollection<TrafficCounter>. The protected checkWaitReadTime, informReadOperation and submitWrite overrides are
// protected internal (see AbstractTrafficShapingHandler).
[Sharable]
public class GlobalChannelTrafficShapingHandler : AbstractTrafficShapingHandler
{
    private static readonly IInternalLogger logger =
            InternalLoggerFactory.GetInstance(typeof(GlobalChannelTrafficShapingHandler));
    /**
     * All queues per channel
     */
    internal readonly ConcurrentDictionary<int, PerChannel> channelQueues = new ConcurrentDictionary<int, PerChannel>();

    /**
     * Global queues size
     */
    private readonly AtomicLong queuesSize = new AtomicLong();

    /**
     * Maximum cumulative writing bytes for one channel among all (as long as channels stay the same)
     */
    private readonly AtomicLong cumulativeWrittenBytes = new AtomicLong();

    /**
     * Maximum cumulative read bytes for one channel among all (as long as channels stay the same)
     */
    private readonly AtomicLong cumulativeReadBytes = new AtomicLong();

    /**
     * Max size in the list before proposing to stop writing new objects from next handlers
     * for all channel (global)
     */
    internal long maxGlobalWriteSize = DEFAULT_MAX_SIZE * 100; // default 400MB

    /**
     * Limit in B/s to apply to write
     */
    private long writeChannelLimit;

    /**
     * Limit in B/s to apply to read
     */
    private long readChannelLimit;

    private const float DEFAULT_DEVIATION = 0.1F;
    private const float MAX_DEVIATION = 0.4F;
    private const float DEFAULT_SLOWDOWN = 0.4F;
    private const float DEFAULT_ACCELERATION = -0.1F;
    private volatile float maxDeviation;
    private volatile float accelerationFactor;
    private volatile float slowDownFactor;
    private volatile bool readDeviationActive;
    private volatile bool writeDeviationActive;

    internal sealed class PerChannel
    {
        internal LinkedList<ToSend> messagesQueue;
        internal TrafficCounter channelTrafficCounter;
        internal long queueSize;
        internal long lastWriteTimestamp;
        internal long lastReadTimestamp;
    }

    /**
     * Create the global TrafficCounter
     */
    internal void CreateGlobalTrafficCounter(IEventExecutorGroup executor)
    {
        // Default
        SetMaxDeviation(DEFAULT_DEVIATION, DEFAULT_SLOWDOWN, DEFAULT_ACCELERATION);
        ObjectUtil.CheckNotNullWithIAE(executor, "executor");
        TrafficCounter tc = new GlobalChannelTrafficCounter(this, executor, "GlobalChannelTC",
                Volatile.Read(ref checkInterval));
        SetTrafficCounter(tc);
        tc.Start();
    }

    protected override int UserDefinedWritabilityIndex()
    {
        return AbstractTrafficShapingHandler.GLOBALCHANNEL_DEFAULT_USER_DEFINED_WRITABILITY_INDEX;
    }

    /**
     * Create a new instance.
     *
     * @param executor
     *            the {@link ScheduledExecutorService} to use for the {@link TrafficCounter}.
     * @param writeGlobalLimit
     *            0 or a limit in bytes/s
     * @param readGlobalLimit
     *            0 or a limit in bytes/s
     * @param writeChannelLimit
     *            0 or a limit in bytes/s
     * @param readChannelLimit
     *            0 or a limit in bytes/s
     * @param checkInterval
     *            The delay between two computations of performances for
     *            channels or 0 if no stats are to be computed.
     * @param maxTime
     *            The maximum delay to wait in case of traffic excess.
     */
    public GlobalChannelTrafficShapingHandler(IEventExecutorGroup executor,
            long writeGlobalLimit, long readGlobalLimit,
            long writeChannelLimit, long readChannelLimit,
            long checkInterval, long maxTime) : base(writeGlobalLimit, readGlobalLimit, checkInterval, maxTime)
    {
        CreateGlobalTrafficCounter(executor);
        Volatile.Write(ref this.writeChannelLimit, writeChannelLimit);
        Volatile.Write(ref this.readChannelLimit, readChannelLimit);
    }

    /**
     * Create a new instance.
     *
     * @param executor
     *          the {@link ScheduledExecutorService} to use for the {@link TrafficCounter}.
     * @param writeGlobalLimit
     *            0 or a limit in bytes/s
     * @param readGlobalLimit
     *            0 or a limit in bytes/s
     * @param writeChannelLimit
     *            0 or a limit in bytes/s
     * @param readChannelLimit
     *            0 or a limit in bytes/s
     * @param checkInterval
     *          The delay between two computations of performances for
     *            channels or 0 if no stats are to be computed.
     */
    public GlobalChannelTrafficShapingHandler(IEventExecutorGroup executor,
            long writeGlobalLimit, long readGlobalLimit,
            long writeChannelLimit, long readChannelLimit,
            long checkInterval) : base(writeGlobalLimit, readGlobalLimit, checkInterval)
    {
        Volatile.Write(ref this.writeChannelLimit, writeChannelLimit);
        Volatile.Write(ref this.readChannelLimit, readChannelLimit);
        CreateGlobalTrafficCounter(executor);
    }

    /**
     * Create a new instance.
     *
     * @param executor
     *          the {@link ScheduledExecutorService} to use for the {@link TrafficCounter}.
     * @param writeGlobalLimit
     *            0 or a limit in bytes/s
     * @param readGlobalLimit
     *            0 or a limit in bytes/s
     * @param writeChannelLimit
     *            0 or a limit in bytes/s
     * @param readChannelLimit
     *            0 or a limit in bytes/s
     */
    public GlobalChannelTrafficShapingHandler(IEventExecutorGroup executor,
            long writeGlobalLimit, long readGlobalLimit,
            long writeChannelLimit, long readChannelLimit) : base(writeGlobalLimit, readGlobalLimit)
    {
        Volatile.Write(ref this.writeChannelLimit, writeChannelLimit);
        Volatile.Write(ref this.readChannelLimit, readChannelLimit);
        CreateGlobalTrafficCounter(executor);
    }

    /**
     * Create a new instance.
     *
     * @param executor
     *          the {@link ScheduledExecutorService} to use for the {@link TrafficCounter}.
     * @param checkInterval
     *          The delay between two computations of performances for
     *            channels or 0 if no stats are to be computed.
     */
    public GlobalChannelTrafficShapingHandler(IEventExecutorGroup executor, long checkInterval) : base(checkInterval)
    {
        CreateGlobalTrafficCounter(executor);
    }

    /**
     * Create a new instance.
     *
     * @param executor
     *          the {@link ScheduledExecutorService} to use for the {@link TrafficCounter}.
     */
    public GlobalChannelTrafficShapingHandler(IEventExecutorGroup executor)
    {
        CreateGlobalTrafficCounter(executor);
    }

    /**
     * @return the current max deviation
     */
    public float MaxDeviation()
    {
        return maxDeviation;
    }

    /**
     * @return the current acceleration factor
     */
    public float AccelerationFactor()
    {
        return accelerationFactor;
    }

    /**
     * @return the current slow down factor
     */
    public float SlowDownFactor()
    {
        return slowDownFactor;
    }

    /**
     * @param maxDeviation
     *            the maximum deviation to allow during computation of average, default deviation
     *            being 0.1, so +/-10% of the desired bandwidth. Maximum being 0.4.
     * @param slowDownFactor
     *            the factor set as +x% to the too fast client (minimal value being 0, meaning no
     *            slow down factor), default being 40% (0.4).
     * @param accelerationFactor
     *            the factor set as -x% to the too slow client (maximal value being 0, meaning no
     *            acceleration factor), default being -10% (-0.1).
     */
    public void SetMaxDeviation(float maxDeviation, float slowDownFactor, float accelerationFactor)
    {
        if (maxDeviation > MAX_DEVIATION)
        {
            throw new ArgumentException("maxDeviation must be <= " + MAX_DEVIATION.ToString(CultureInfo.InvariantCulture));
        }
        ObjectUtil.CheckPositiveOrZero(slowDownFactor, "slowDownFactor");
        if (accelerationFactor > 0)
        {
            throw new ArgumentException("accelerationFactor must be <= 0");
        }
        this.maxDeviation = maxDeviation;
        this.accelerationFactor = 1 + accelerationFactor;
        this.slowDownFactor = 1 + slowDownFactor;
    }

    private void ComputeDeviationCumulativeBytes()
    {
        // compute the maximum cumulativeXxxxBytes among still connected Channels
        long maxWrittenBytes = 0;
        long maxReadBytes = 0;
        long minWrittenBytes = long.MaxValue;
        long minReadBytes = long.MaxValue;
        foreach (PerChannel perChannel in channelQueues.Values)
        {
            long value = perChannel.channelTrafficCounter.CumulativeWrittenBytes();
            if (maxWrittenBytes < value)
            {
                maxWrittenBytes = value;
            }
            if (minWrittenBytes > value)
            {
                minWrittenBytes = value;
            }
            value = perChannel.channelTrafficCounter.CumulativeReadBytes();
            if (maxReadBytes < value)
            {
                maxReadBytes = value;
            }
            if (minReadBytes > value)
            {
                minReadBytes = value;
            }
        }
        bool multiple = channelQueues.Count > 1;
        readDeviationActive = multiple && minReadBytes < maxReadBytes / 2;
        writeDeviationActive = multiple && minWrittenBytes < maxWrittenBytes / 2;
        cumulativeWrittenBytes.Set(maxWrittenBytes);
        cumulativeReadBytes.Set(maxReadBytes);
    }

    protected internal override void DoAccounting(TrafficCounter counter)
    {
        ComputeDeviationCumulativeBytes();
        base.DoAccounting(counter);
    }

    private long ComputeBalancedWait(float maxLocal, float maxGlobal, long wait)
    {
        if (maxGlobal == 0)
        {
            // no change
            return wait;
        }
        float ratio = maxLocal / maxGlobal;
        // if in the boundaries, same value
        if (ratio > maxDeviation)
        {
            if (ratio < 1 - maxDeviation)
            {
                return wait;
            }
            else
            {
                ratio = slowDownFactor;
                if (wait < MINIMAL_WAIT)
                {
                    wait = MINIMAL_WAIT;
                }
            }
        }
        else
        {
            ratio = accelerationFactor;
        }
        return (long)(wait * ratio);
    }

    /**
     * @return the maxGlobalWriteSize
     */
    public long GetMaxGlobalWriteSize()
    {
        return Volatile.Read(ref maxGlobalWriteSize);
    }

    /**
     * Note the change will be taken as best effort, meaning
     * that all already scheduled traffics will not be
     * changed, but only applied to new traffics.<br>
     * So the expected usage of this method is to be used not too often,
     * accordingly to the traffic shaping configuration.
     *
     * @param maxGlobalWriteSize the maximum Global Write Size allowed in the buffer
     *            globally for all channels before write suspended is set.
     */
    public void SetMaxGlobalWriteSize(long maxGlobalWriteSize)
    {
        Volatile.Write(ref this.maxGlobalWriteSize, ObjectUtil.CheckPositive(maxGlobalWriteSize, "maxGlobalWriteSize"));
    }

    /**
     * @return the global size of the buffers for all queues.
     */
    public long QueuesSize()
    {
        return queuesSize.Get();
    }

    /**
     * @param newWriteLimit Channel write limit
     * @param newReadLimit Channel read limit
     */
    public void ConfigureChannel(long newWriteLimit, long newReadLimit)
    {
        Volatile.Write(ref writeChannelLimit, newWriteLimit);
        Volatile.Write(ref readChannelLimit, newReadLimit);
        long now = Traffic.TrafficCounter.MilliSecondFromNano();
        foreach (PerChannel perChannel in channelQueues.Values)
        {
            perChannel.channelTrafficCounter.ResetAccounting(now);
        }
    }

    /**
     * @return Channel write limit
     */
    public long GetWriteChannelLimit()
    {
        return Volatile.Read(ref writeChannelLimit);
    }

    /**
     * @param writeLimit Channel write limit
     */
    public void SetWriteChannelLimit(long writeLimit)
    {
        Volatile.Write(ref writeChannelLimit, writeLimit);
        long now = Traffic.TrafficCounter.MilliSecondFromNano();
        foreach (PerChannel perChannel in channelQueues.Values)
        {
            perChannel.channelTrafficCounter.ResetAccounting(now);
        }
    }

    /**
     * @return Channel read limit
     */
    public long GetReadChannelLimit()
    {
        return Volatile.Read(ref readChannelLimit);
    }

    /**
     * @param readLimit Channel read limit
     */
    public void SetReadChannelLimit(long readLimit)
    {
        Volatile.Write(ref readChannelLimit, readLimit);
        long now = Traffic.TrafficCounter.MilliSecondFromNano();
        foreach (PerChannel perChannel in channelQueues.Values)
        {
            perChannel.channelTrafficCounter.ResetAccounting(now);
        }
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
            // Don't start it since managed through the Global one
            perChannel.channelTrafficCounter = new TrafficCounter(this, null, "ChannelTC" +
                    ctx.Channel.GetHashCode().ToString(CultureInfo.InvariantCulture), Volatile.Read(ref checkInterval));
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
        trafficCounter.ResetCumulativeTime();
        base.HandlerAdded(ctx);
    }

    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        trafficCounter.ResetCumulativeTime();
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
                        perChannel.channelTrafficCounter.BytesRealWriteFlowControl(size);
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

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        long size = CalculateSize(msg);
        long now = Traffic.TrafficCounter.MilliSecondFromNano();
        if (size > 0)
        {
            // compute the number of ms to wait before reopening the channel
            long waitGlobal = trafficCounter.ReadTimeToWait(size, GetReadLimit(), Volatile.Read(ref maxTime), now);
            int key = ctx.Channel.GetHashCode();
            channelQueues.TryGetValue(key, out PerChannel perChannel);
            long wait = 0;
            if (perChannel != null)
            {
                wait = perChannel.channelTrafficCounter.ReadTimeToWait(size, Volatile.Read(ref readChannelLimit),
                        Volatile.Read(ref maxTime), now);
                if (readDeviationActive)
                {
                    // now try to balance between the channels
                    long maxLocalRead;
                    maxLocalRead = perChannel.channelTrafficCounter.CumulativeReadBytes();
                    long maxGlobalRead = cumulativeReadBytes.Get();
                    if (maxLocalRead <= 0)
                    {
                        maxLocalRead = 0;
                    }
                    if (maxGlobalRead < maxLocalRead)
                    {
                        maxGlobalRead = maxLocalRead;
                    }
                    wait = ComputeBalancedWait(maxLocalRead, maxGlobalRead, wait);
                }
            }
            if (wait < waitGlobal)
            {
                wait = waitGlobal;
            }
            wait = CheckWaitReadTime(ctx, wait, now);
            if (wait >= MINIMAL_WAIT)
            { // At least 10ms seems a minimal
                // time in order to try to limit the traffic
                // Only AutoRead AND HandlerActive True means Context Active
                IChannel channel = ctx.Channel;
                IChannelConfig config = channel.Config;
                if (logger.IsDebugEnabled())
                {
                    logger.Debug("Read Suspend: " + wait + ':' + Text(config.IsAutoRead()) + ':'
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

    // CLR: ChannelPromise promise is the TaskCompletionSource and CancellationToken of the queued write. The class is
    // internal because the internal PerChannel declares a LinkedList<ToSend> (CS0052).
    internal sealed class ToSend
    {
        internal readonly long relativeTimeAction;
        internal readonly object toSend;
        internal readonly TaskCompletionSource promise;
        internal readonly long size;
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

    protected long MaximumCumulativeWrittenBytes()
    {
        return cumulativeWrittenBytes.Get();
    }

    protected long MaximumCumulativeReadBytes()
    {
        return cumulativeReadBytes.Get();
    }

    /**
     * To allow for instance doAccounting to use the TrafficCounter per channel.
     * @return the list of TrafficCounters that exists at the time of the call.
     */
    public ICollection<TrafficCounter> ChannelTrafficCounters()
    {
        return new TrafficCounterCollection(this);
    }

    // CLR: the anonymous AbstractCollection is this named class over ICollection<TrafficCounter>; as in
    // AbstractCollection, contains() and toArray() iterate, while add(), remove() and clear() are unsupported
    // (NotSupportedException, IsReadOnly), because the iterator's remove() throws UnsupportedOperationException.
    private sealed class TrafficCounterCollection : ICollection<TrafficCounter>
    {
        private readonly GlobalChannelTrafficShapingHandler handler;

        internal TrafficCounterCollection(GlobalChannelTrafficShapingHandler handler)
        {
            this.handler = handler;
        }

        public IEnumerator<TrafficCounter> GetEnumerator()
        {
            return new TrafficCounterIterator(handler.channelQueues.GetEnumerator());
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int Count => handler.channelQueues.Count;

        public bool IsReadOnly => true;

        public void Add(TrafficCounter item)
        {
            throw new NotSupportedException();
        }

        public void Clear()
        {
            using IEnumerator<TrafficCounter> it = GetEnumerator();
            if (it.MoveNext())
            {
                throw new NotSupportedException();
            }
        }

        public bool Contains(TrafficCounter item)
        {
            foreach (TrafficCounter counter in this)
            {
                if (Equals(item, counter))
                {
                    return true;
                }
            }
            return false;
        }

        public void CopyTo(TrafficCounter[] array, int arrayIndex)
        {
            foreach (TrafficCounter counter in this)
            {
                array[arrayIndex++] = counter;
            }
        }

        public bool Remove(TrafficCounter item)
        {
            if (Contains(item))
            {
                throw new NotSupportedException();
            }
            return false;
        }
    }

    // CLR: the anonymous Iterator is this named IEnumerator over the ConcurrentDictionary enumerator, which, like
    // channelQueues.values().iterator(), is weakly consistent; IEnumerator has no remove(), whose Java body throws
    // UnsupportedOperationException, and Reset() is not supported either.
    private sealed class TrafficCounterIterator : IEnumerator<TrafficCounter>
    {
        private readonly IEnumerator<KeyValuePair<int, PerChannel>> iter;

        internal TrafficCounterIterator(IEnumerator<KeyValuePair<int, PerChannel>> iter)
        {
            this.iter = iter;
        }

        public bool MoveNext()
        {
            return iter.MoveNext();
        }

        public TrafficCounter Current => iter.Current.Value.channelTrafficCounter;

        object IEnumerator.Current => Current;

        public void Reset()
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
            iter.Dispose();
        }
    }

    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        long size = CalculateSize(msg);
        long now = Traffic.TrafficCounter.MilliSecondFromNano();
        if (size > 0)
        {
            // compute the number of ms to wait before continue with the channel
            long waitGlobal = trafficCounter.WriteTimeToWait(size, GetWriteLimit(), Volatile.Read(ref maxTime), now);
            int key = ctx.Channel.GetHashCode();
            channelQueues.TryGetValue(key, out PerChannel perChannel);
            long wait = 0;
            if (perChannel != null)
            {
                wait = perChannel.channelTrafficCounter.WriteTimeToWait(size, Volatile.Read(ref writeChannelLimit),
                        Volatile.Read(ref maxTime), now);
                if (writeDeviationActive)
                {
                    // now try to balance between the channels
                    long maxLocalWrite;
                    maxLocalWrite = perChannel.channelTrafficCounter.CumulativeWrittenBytes();
                    long maxGlobalWrite = cumulativeWrittenBytes.Get();
                    if (maxLocalWrite <= 0)
                    {
                        maxLocalWrite = 0;
                    }
                    if (maxGlobalWrite < maxLocalWrite)
                    {
                        maxGlobalWrite = maxLocalWrite;
                    }
                    wait = ComputeBalancedWait(maxLocalWrite, maxGlobalWrite, wait);
                }
            }
            if (wait < waitGlobal)
            {
                wait = waitGlobal;
            }
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
                perChannel.channelTrafficCounter.BytesRealWriteFlowControl(size);
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
            if (queuesSize.Get() > Volatile.Read(ref maxGlobalWriteSize))
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
                    perChannel.channelTrafficCounter.BytesRealWriteFlowControl(size);
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

    public override string ToString()
    {
        return new StringBuilder(340).Append(base.ToString())
            .Append(" Write Channel Limit: ").Append(Volatile.Read(ref writeChannelLimit).ToString(CultureInfo.InvariantCulture))
            .Append(" Read Channel Limit: ").Append(Volatile.Read(ref readChannelLimit).ToString(CultureInfo.InvariantCulture)).ToString();
    }
}
