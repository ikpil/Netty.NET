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
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using PerChannel = Netty.NET.Handler.Traffic.GlobalChannelTrafficShapingHandler.PerChannel;

namespace Netty.NET.Handler.Traffic;

/**
 * Version for {@link GlobalChannelTrafficShapingHandler}.
 * This TrafficCounter is the Global one, and its special property is to directly handle
 * other channel's TrafficCounters. In particular, there are no scheduler for those
 * channel's TrafficCounters because it is managed by this one.
 */
// CLR: ScheduledExecutorService is IEventExecutorGroup and ScheduledFuture<?> the CancellationTokenSource of the
// periodic work, as in TrafficCounter; the synchronized overrides lock this, the monitor of TrafficCounter's
// synchronized methods. channelQueues.values() is the ConcurrentDictionary Values copy ("The property returns a copy of
// all the values", https://learn.microsoft.com/dotnet/api/system.collections.concurrent.concurrentdictionary-2.values),
// where Java iterates a weakly consistent view; both visit every channel present when the iteration starts.
public class GlobalChannelTrafficCounter : TrafficCounter
{
    /**
     * @param trafficShapingHandler the associated {@link GlobalChannelTrafficShapingHandler}.
     * @param executor the underlying executor service for scheduling checks (both Global and per Channel).
     * @param name the name given to this monitor.
     * @param checkInterval the checkInterval in millisecond between two computations.
     */
    public GlobalChannelTrafficCounter(GlobalChannelTrafficShapingHandler trafficShapingHandler,
            IEventExecutorGroup executor, string name, long checkInterval)
        : base(trafficShapingHandler, executor, name, checkInterval)
    {
        ObjectUtil.CheckNotNullWithIAE(executor, "executor");
    }

    /**
     * Class to implement monitoring at fix delay.
     * This version is Mixed in the way it mixes Global and Channel counters.
     */
    // CLR: the Runnable is a class with Run(), scheduled as the Run method group.
    private class MixedTrafficMonitoringTask
    {
        /**
         * The associated TrafficShapingHandler
         */
        private readonly GlobalChannelTrafficShapingHandler trafficShapingHandler1;

        /**
         * The associated TrafficCounter
         */
        private readonly TrafficCounter counter;

        /**
         * @param trafficShapingHandler The parent handler to which this task needs to callback to for accounting.
         * @param counter The parent TrafficCounter that we need to reset the statistics for.
         */
        internal MixedTrafficMonitoringTask(
                GlobalChannelTrafficShapingHandler trafficShapingHandler,
                TrafficCounter counter)
        {
            trafficShapingHandler1 = trafficShapingHandler;
            this.counter = counter;
        }

        public void Run()
        {
            if (!counter.monitorActive)
            {
                return;
            }

            long newLastTime = MilliSecondFromNano();
            counter.ResetAccounting(newLastTime);

            foreach (PerChannel perChannel in trafficShapingHandler1.channelQueues.Values)
            {
                perChannel.channelTrafficCounter.ResetAccounting(newLastTime);
            }

            trafficShapingHandler1.DoAccounting(counter);
        }
    }

    /**
     * Start the monitoring process.
     */
    public override void Start()
    {
        lock (this)
        {
            if (monitorActive)
            {
                return;
            }

            lastTime.Set(MilliSecondFromNano());
            long localCheckInterval = checkInterval.Get();
            if (localCheckInterval > 0)
            {
                monitorActive = true;
                monitor = new MixedTrafficMonitoringTask((GlobalChannelTrafficShapingHandler)trafficShapingHandler, this).Run;
                CancellationTokenSource future = new CancellationTokenSource();
                _ = executor.ScheduleAtFixedRateAsync(monitor, TimeSpan.Zero,
                        TimeSpan.FromMilliseconds(localCheckInterval), future.Token);
                scheduledFuture = future;
            }
        }
    }

    /**
     * Stop the monitoring process.
     */
    public override void Stop()
    {
        lock (this)
        {
            if (!monitorActive)
            {
                return;
            }

            monitorActive = false;
            ResetAccounting(MilliSecondFromNano());
            trafficShapingHandler.DoAccounting(this);

            if (scheduledFuture != null)
            {
                scheduledFuture.Cancel();
            }
        }
    }

    public override void ResetCumulativeTime()
    {
        foreach (PerChannel perChannel in
            ((GlobalChannelTrafficShapingHandler)trafficShapingHandler).channelQueues.Values)
        {
            perChannel.channelTrafficCounter.ResetCumulativeTime();
        }

        base.ResetCumulativeTime();
    }

}
