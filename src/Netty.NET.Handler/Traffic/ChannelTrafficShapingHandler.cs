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
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Traffic;

/**
 * <p>This implementation of the {@link AbstractTrafficShapingHandler} is for channel
 * traffic shaping, that is to say a per channel limitation of the bandwidth.</p>
 * <p>Note the index used in {@code OutboundBuffer.setUserDefinedWritability(index, boolean)} is <b>1</b>.</p>
 *
 * <p>The general use should be as follow:</p>
 * <ul>
 * <li><p>Add in your pipeline a new ChannelTrafficShapingHandler.</p>
 * <p><tt>ChannelTrafficShapingHandler myHandler = new ChannelTrafficShapingHandler();</tt></p>
 * <p><tt>pipeline.addLast(myHandler);</tt></p>
 *
 * <p><b>Note that this handler has a Pipeline Coverage of "one" which means a new handler must be created
 * for each new channel as the counter cannot be shared among all channels.</b>.</p>
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
 * <p>So the expected usage of those methods are to be used not too often,
 * accordingly to the traffic shaping configuration.</p></li>
 * </ul>
 */
// CLR: ArrayDeque<ToSend> is LinkedList<ToSend> (pollFirst is PollFirst, addFirst/addLast are AddFirst/AddLast);
// synchronized (this) is lock (this), the monitor Java uses; java.nio.channels.ClosedChannelException is the Transport
// ClosedChannelException. The anonymous Runnable that calls sendAllValid is a lambda scheduled with ScheduleAsync and
// TimeSpan.FromMilliseconds. See AbstractTrafficShapingHandler for the queued promise.
public class ChannelTrafficShapingHandler : AbstractTrafficShapingHandler
{
    private readonly LinkedList<ToSend> messagesQueue = new LinkedList<ToSend>();
    private long queueSize;

    /**
     * Create a new instance.
     *
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
    public ChannelTrafficShapingHandler(long writeLimit, long readLimit,
            long checkInterval, long maxTime) : base(writeLimit, readLimit, checkInterval, maxTime)
    {
    }

    /**
     * Create a new instance using default
     * max time as delay allowed value of 15000 ms.
     *
     * @param writeLimit
     *          0 or a limit in bytes/s
     * @param readLimit
     *          0 or a limit in bytes/s
     * @param checkInterval
     *          The delay between two computations of performances for
     *            channels or 0 if no stats are to be computed.
     */
    public ChannelTrafficShapingHandler(long writeLimit,
            long readLimit, long checkInterval) : base(writeLimit, readLimit, checkInterval)
    {
    }

    /**
     * Create a new instance using default Check Interval value of 1000 ms and
     * max time as delay allowed value of 15000 ms.
     *
     * @param writeLimit
     *          0 or a limit in bytes/s
     * @param readLimit
     *          0 or a limit in bytes/s
     */
    public ChannelTrafficShapingHandler(long writeLimit,
            long readLimit) : base(writeLimit, readLimit)
    {
    }

    /**
     * Create a new instance using
     * default max time as delay allowed value of 15000 ms and no limit.
     *
     * @param checkInterval
     *          The delay between two computations of performances for
     *            channels or 0 if no stats are to be computed.
     */
    public ChannelTrafficShapingHandler(long checkInterval) : base(checkInterval)
    {
    }

    // CLR: the hash code in the counter name uses the invariant culture.
    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        TrafficCounter trafficCounter = new TrafficCounter(this, ctx.Executor, "ChannelTC" +
                ctx.Channel.GetHashCode().ToString(CultureInfo.InvariantCulture), Volatile.Read(ref checkInterval));
        SetTrafficCounter(trafficCounter);
        trafficCounter.Start();
        base.HandlerAdded(ctx);
    }

    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {
        trafficCounter.Stop();

        // write order control
        lock (this)
        {
            if (ctx.Channel.IsActive)
            {
                foreach (ToSend toSend in messagesQueue)
                {
                    long size = CalculateSize(toSend.toSend);
                    trafficCounter.BytesRealWriteFlowControl(size);
                    queueSize -= size;
                    Write(ctx, toSend.toSend, toSend.promise, toSend.cancellationToken);
                }
            }
            else if (messagesQueue.Count != 0)
            {
                ClosedChannelException cause = new ClosedChannelException();

                foreach (ToSend toSend in messagesQueue)
                {
                    ReleaseAndFailQueuedWrite(toSend.toSend, toSend.promise, cause);
                }

                queueSize = 0;
            }

            messagesQueue.Clear();
        }

        ReleaseWriteSuspended(ctx);
        ReleaseReadSuspended(ctx);
        base.HandlerRemoved(ctx);
    }

    // CLR: ChannelPromise promise is the TaskCompletionSource and CancellationToken of the queued write.
    private sealed class ToSend
    {
        internal readonly long relativeTimeAction;
        internal readonly object toSend;
        internal readonly TaskCompletionSource promise;
        internal readonly CancellationToken cancellationToken;

        internal ToSend(long delay, object toSend, TaskCompletionSource promise,
                CancellationToken cancellationToken)
        {
            relativeTimeAction = delay;
            this.toSend = toSend;
            this.promise = promise;
            this.cancellationToken = cancellationToken;
        }
    }

    protected internal override Task SubmitWrite(IChannelHandlerContext ctx, object msg,
            long size, long delay, long now,
            CancellationToken cancellationToken)
    {
        ToSend newToSend;

        // write order control
        lock (this)
        {
            if (delay == 0 && messagesQueue.Count == 0)
            {
                trafficCounter.BytesRealWriteFlowControl(size);

                return ctx.WriteAsync(msg, cancellationToken);
            }

            newToSend = new ToSend(delay + now, msg, NewPromise(cancellationToken), cancellationToken);
            messagesQueue.AddLast(newToSend);
            queueSize += size;
            CheckWriteSuspend(ctx, delay, queueSize);
        }

        long futureNow = newToSend.relativeTimeAction;
        _ = ctx.Executor.ScheduleAsync(() => SendAllValid(ctx, futureNow), TimeSpan.FromMilliseconds(delay));
        return newToSend.promise.Task;
    }

    private void SendAllValid(IChannelHandlerContext ctx, long now)
    {
        // write order control
        lock (this)
        {
            ToSend newToSend = PollFirst(messagesQueue);

            for (; newToSend != null; newToSend = PollFirst(messagesQueue))
            {
                if (newToSend.relativeTimeAction <= now)
                {
                    long size = CalculateSize(newToSend.toSend);
                    trafficCounter.BytesRealWriteFlowControl(size);
                    queueSize -= size;
                    Write(ctx, newToSend.toSend, newToSend.promise, newToSend.cancellationToken);
                }
                else
                {
                    messagesQueue.AddFirst(newToSend);
                    break;
                }
            }

            if (messagesQueue.Count == 0)
            {
                ReleaseWriteSuspended(ctx);
            }
        }

        ctx.Flush();
    }

    /**
    * @return current size in bytes of the write buffer.
    */
   public long QueueSize()
   {
       return queueSize;
   }
}
