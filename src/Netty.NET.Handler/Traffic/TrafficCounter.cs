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
using System.Globalization;
using System.Text;
using System.Threading;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Handler.Traffic;

/**
 * Counts the number of read and written bytes for rate-limiting traffic.
 * <p>
 * It computes the statistics for both inbound and outbound traffic periodically at the given
 * {@code checkInterval}, and calls the {@link AbstractTrafficShapingHandler#doAccounting(TrafficCounter)} method back.
 * If the {@code checkInterval} is {@code 0}, no accounting will be done and statistics will only be computed at each
 * receive or write operation.
 * </p>
 */
// CLR: ScheduledExecutorService is IEventExecutorGroup, the executor family whose ScheduleAtFixedRateAsync
// extension schedules on the executor's own queue
// (src/Netty.NET.Common/Concurrent/EventExecutorSchedulingExtensions.cs); the Java EventExecutorGroup extends
// ScheduledExecutorService. ScheduledFuture<?> is the CancellationTokenSource of the periodic work (as
// AbstractNioChannel's connectTimeoutFuture); cancel(true) is Cancel(), which stops further runs but does not
// interrupt a running one ("No worker is interrupted", EventExecutorSchedulingExtensions). AtomicLong is the
// Common AtomicLong (getAndSet is Set, which returns the previous value). A volatile long field is a long read and
// written through Volatile.Read/Volatile.Write, because C# volatile fields cannot be long
// (https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/volatile). synchronized methods lock
// this, the monitor a Java synchronized method uses, so that GlobalChannelTrafficCounter and other subclasses
// share it (https://learn.microsoft.com/dotnet/csharp/language-reference/statements/lock). Numbers in toString()
// use the invariant culture, because Java decimal text does not depend on a culture.
public class TrafficCounter
{

    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(TrafficCounter));

    /**
     * @return the time in ms using nanoTime, so not real EPOCH time but elapsed time in ms.
     */
    public static long MilliSecondFromNano()
    {
        return SystemTimer.NanoTime() / 1000000;
    }

    /**
     * Current written bytes
     */
    private readonly AtomicLong currentWrittenBytes = new AtomicLong();

    /**
     * Current read bytes
     */
    private readonly AtomicLong currentReadBytes = new AtomicLong();

    /**
     * Last writing time during current check interval
     */
    private long writingTime;

    /**
     * Last reading delay during current check interval
     */
    private long readingTime;

    /**
     * Long life written bytes
     */
    private readonly AtomicLong cumulativeWrittenBytes = new AtomicLong();

    /**
     * Long life read bytes
     */
    private readonly AtomicLong cumulativeReadBytes = new AtomicLong();

    /**
     * Last Time where cumulative bytes where reset to zero: this time is a real EPOC time (informative only)
     */
    private long lastCumulativeTime;

    /**
     * Last writing bandwidth
     */
    private long lastWriteThroughput;

    /**
     * Last reading bandwidth
     */
    private long lastReadThroughput;

    /**
     * Last Time Check taken
     */
    internal readonly AtomicLong lastTime = new AtomicLong();

    /**
     * Last written bytes number during last check interval
     */
    private long lastWrittenBytes;

    /**
     * Last read bytes number during last check interval
     */
    private long lastReadBytes;

    /**
     * Last future writing time during last check interval
     */
    private long lastWritingTime;

    /**
     * Last reading time during last check interval
     */
    private long lastReadingTime;

    /**
     * Real written bytes
     */
    private readonly AtomicLong realWrittenBytes = new AtomicLong();

    /**
     * Real writing bandwidth
     */
    private long realWriteThroughput;

    /**
     * Delay between two captures
     */
    // CLR: the Common AtomicLong constructor takes an int initial value.
    internal readonly AtomicLong checkInterval = new AtomicLong(
            (int)AbstractTrafficShapingHandler.DEFAULT_CHECK_INTERVAL);

    // default 1 s

    /**
     * Name of this Monitor
     */
    internal readonly string name;

    /**
     * The associated TrafficShapingHandler
     */
    internal readonly AbstractTrafficShapingHandler trafficShapingHandler;

    /**
     * Executor that will run the monitor
     */
    internal readonly IEventExecutorGroup executor;
    /**
     * Monitor created once in start()
     */
    internal Action monitor;
    /**
     * used in stop() to cancel the timer
     */
    internal volatile CancellationTokenSource scheduledFuture;

    /**
     * Is Monitor active
     */
    internal volatile bool monitorActive;

    /**
     * Class to implement monitoring at fix delay
     *
     */
    // CLR: the inner Runnable is a class with Run() that reaches the counter through an explicit reference
    // (TrafficCounter.this); it is scheduled as the Run method group.
    private sealed class TrafficMonitoringTask
    {
        private readonly TrafficCounter counter;

        internal TrafficMonitoringTask(TrafficCounter counter)
        {
            this.counter = counter;
        }

        public void Run()
        {
            if (!counter.monitorActive)
            {
                return;
            }

            counter.ResetAccounting(MilliSecondFromNano());

            if (counter.trafficShapingHandler != null)
            {
                counter.trafficShapingHandler.DoAccounting(counter);
            }
        }
    }

    /**
     * Start the monitoring process.
     */
    public virtual void Start()
    {
        lock (this)
        {
            if (monitorActive)
            {
                return;
            }

            lastTime.Set(MilliSecondFromNano());
            long localCheckInterval = checkInterval.Get();
            // if executor is null, it means it is piloted by a GlobalChannelTrafficCounter, so no executor
            if (localCheckInterval > 0 && executor != null)
            {
                monitorActive = true;
                monitor = new TrafficMonitoringTask(this).Run;
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
    public virtual void Stop()
    {
        lock (this)
        {
            if (!monitorActive)
            {
                return;
            }

            monitorActive = false;
            ResetAccounting(MilliSecondFromNano());

            if (trafficShapingHandler != null)
            {
                trafficShapingHandler.DoAccounting(this);
            }

            if (scheduledFuture != null)
            {
                scheduledFuture.Cancel();
            }
        }
    }

    /**
     * Reset the accounting on Read and Write.
     *
     * @param newLastTime the milliseconds unix timestamp that we should be considered up-to-date for.
     */
    internal void ResetAccounting(long newLastTime)
    {
        lock (this)
        {
            long interval = newLastTime - lastTime.Set(newLastTime);
            if (interval == 0)
            {
                // nothing to do
                return;
            }

            if (logger.IsDebugEnabled() && interval > CheckInterval() << 1)
            {
                logger.Debug("Acct schedule not ok: " + interval + " > 2*" + CheckInterval() + " from " + name);
            }

            Volatile.Write(ref lastReadBytes, currentReadBytes.Set(0));
            Volatile.Write(ref lastWrittenBytes, currentWrittenBytes.Set(0));
            lastReadThroughput = Volatile.Read(ref lastReadBytes) * 1000 / interval;
            // nb byte / checkInterval in ms * 1000 (1s)
            lastWriteThroughput = Volatile.Read(ref lastWrittenBytes) * 1000 / interval;
            // nb byte / checkInterval in ms * 1000 (1s)
            realWriteThroughput = realWrittenBytes.Set(0) * 1000 / interval;
            Volatile.Write(ref lastWritingTime, Math.Max(Volatile.Read(ref lastWritingTime), writingTime));
            Volatile.Write(ref lastReadingTime, Math.Max(Volatile.Read(ref lastReadingTime), readingTime));
        }
    }

    /**
     * Constructor with the {@link AbstractTrafficShapingHandler} that hosts it, the {@link ScheduledExecutorService}
     * to use, its name, the checkInterval between two computations in milliseconds.
     *
     * @param executor
     *            the underlying executor service for scheduling checks, might be null when used
     * from {@link GlobalChannelTrafficCounter}.
     * @param name
     *            the name given to this monitor.
     * @param checkInterval
     *            the checkInterval in millisecond between two computations.
     */
    public TrafficCounter(IEventExecutorGroup executor, string name, long checkInterval)
    {

        this.name = ObjectUtil.CheckNotNull(name, "name");
        trafficShapingHandler = null;
        this.executor = executor;

        Init(checkInterval);
    }

    /**
     * Constructor with the {@link AbstractTrafficShapingHandler} that hosts it, the Timer to use, its
     * name, the checkInterval between two computations in millisecond.
     *
     * @param trafficShapingHandler
     *            the associated AbstractTrafficShapingHandler.
     * @param executor
     *            the underlying executor service for scheduling checks, might be null when used
     * from {@link GlobalChannelTrafficCounter}.
     * @param name
     *            the name given to this monitor.
     * @param checkInterval
     *            the checkInterval in millisecond between two computations.
     */
    public TrafficCounter(
            AbstractTrafficShapingHandler trafficShapingHandler, IEventExecutorGroup executor,
            string name, long checkInterval)
    {
        this.name = ObjectUtil.CheckNotNull(name, "name");
        this.trafficShapingHandler = ObjectUtil.CheckNotNullWithIAE(trafficShapingHandler, "trafficShapingHandler");
        this.executor = executor;

        Init(checkInterval);
    }

    private void Init(long checkInterval)
    {
        // absolute time: informative only
        lastCumulativeTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        writingTime = MilliSecondFromNano();
        readingTime = writingTime;
        Volatile.Write(ref lastWritingTime, writingTime);
        Volatile.Write(ref lastReadingTime, writingTime);
        Configure(checkInterval);
    }

    /**
     * Change checkInterval between two computations in millisecond.
     *
     * @param newCheckInterval The new check interval (in milliseconds)
     */
    public void Configure(long newCheckInterval)
    {
        long newInterval = newCheckInterval / 10 * 10;
        if (checkInterval.Set(newInterval) != newInterval)
        {
            if (newInterval <= 0)
            {
                Stop();
                // No more active monitoring
                lastTime.Set(MilliSecondFromNano());
            }
            else
            {
                // Restart
                Stop();
                Start();
            }
        }
    }

    /**
     * Computes counters for Read.
     *
     * @param recv
     *            the size in bytes to read
     */
    internal void BytesRecvFlowControl(long recv)
    {
        currentReadBytes.AddAndGet(recv);
        cumulativeReadBytes.AddAndGet(recv);
    }

    /**
     * Computes counters for Write.
     *
     * @param write
     *            the size in bytes to write
     */
    internal void BytesWriteFlowControl(long write)
    {
        currentWrittenBytes.AddAndGet(write);
        cumulativeWrittenBytes.AddAndGet(write);
    }

    /**
     * Computes counters for Real Write.
     *
     * @param write
     *            the size in bytes to write
     */
    internal void BytesRealWriteFlowControl(long write)
    {
        realWrittenBytes.AddAndGet(write);
    }

    /**
     * @return the current checkInterval between two computations of traffic counter
     *         in millisecond.
     */
    public long CheckInterval()
    {
        return checkInterval.Get();
    }

    /**
     * @return the Read Throughput in bytes/s computes in the last check interval.
     */
    public long LastReadThroughput()
    {
        return lastReadThroughput;
    }

    /**
     * @return the Write Throughput in bytes/s computes in the last check interval.
     */
    public long LastWriteThroughput()
    {
        return lastWriteThroughput;
    }

    /**
     * @return the number of bytes read during the last check Interval.
     */
    public long LastReadBytes()
    {
        return Volatile.Read(ref lastReadBytes);
    }

    /**
     * @return the number of bytes written during the last check Interval.
     */
    public long LastWrittenBytes()
    {
        return Volatile.Read(ref lastWrittenBytes);
    }

    /**
     * @return the current number of bytes read since the last checkInterval.
     */
    public long CurrentReadBytes()
    {
        return currentReadBytes.Get();
    }

    /**
     * @return the current number of bytes written since the last check Interval.
     */
    public long CurrentWrittenBytes()
    {
        return currentWrittenBytes.Get();
    }

    /**
     * @return the Time in millisecond of the last check as of System.currentTimeMillis().
     */
    public long LastTime()
    {
        return lastTime.Get();
    }

    /**
     * @return the cumulativeWrittenBytes
     */
    public long CumulativeWrittenBytes()
    {
        return cumulativeWrittenBytes.Get();
    }

    /**
     * @return the cumulativeReadBytes
     */
    public long CumulativeReadBytes()
    {
        return cumulativeReadBytes.Get();
    }

    /**
     * @return the lastCumulativeTime in millisecond as of System.currentTimeMillis()
     * when the cumulative counters were reset to 0.
     */
    public long LastCumulativeTime()
    {
        return lastCumulativeTime;
    }

    /**
     * @return the realWrittenBytes
     */
    public AtomicLong GetRealWrittenBytes()
    {
        return realWrittenBytes;
    }

    /**
     * @return the realWriteThroughput
     */
    public long GetRealWriteThroughput()
    {
        return realWriteThroughput;
    }

    /**
     * Reset both read and written cumulative bytes counters and the associated absolute time
     * from System.currentTimeMillis().
     */
    public virtual void ResetCumulativeTime()
    {
        lastCumulativeTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        cumulativeReadBytes.Set(0);
        cumulativeWrittenBytes.Set(0);
    }

    /**
     * @return the name of this TrafficCounter.
     */
    public string Name()
    {
        return name;
    }

    /**
     * Returns the time to wait (if any) for the given length message, using the given limitTraffic and the max wait
     * time.
     *
     * @param size
     *            the recv size
     * @param limitTraffic
     *            the traffic limit in bytes per second.
     * @param maxTime
     *            the max time in ms to wait in case of excess of traffic.
     * @return the current time to wait (in ms) if needed for Read operation.
     */
    // CLR: @Deprecated without a @deprecated tag names no replacement, so the method is ported with [Obsolete].
    [Obsolete]
    public long ReadTimeToWait(long size, long limitTraffic, long maxTime)
    {
        return ReadTimeToWait(size, limitTraffic, maxTime, MilliSecondFromNano());
    }

    /**
     * Returns the time to wait (if any) for the given length message, using the given limitTraffic and the max wait
     * time.
     *
     * @param size
     *            the recv size
     * @param limitTraffic
     *            the traffic limit in bytes per second
     * @param maxTime
     *            the max time in ms to wait in case of excess of traffic.
     * @param now the current time
     * @return the current time to wait (in ms) if needed for Read operation.
     */
    // CLR: the second local time is time2: a C# local is in scope in its whole block, so it cannot share the name of the
    // local declared in the nested if block (CS0136).
    public long ReadTimeToWait(long size, long limitTraffic, long maxTime, long now)
    {
        BytesRecvFlowControl(size);
        if (size == 0 || limitTraffic == 0)
        {
            return 0;
        }

        long lastTimeCheck = lastTime.Get();
        long sum = currentReadBytes.Get();
        long localReadingTime = readingTime;
        long lastRB = Volatile.Read(ref lastReadBytes);
        long interval = now - lastTimeCheck;
        long pastDelay = Math.Max(Volatile.Read(ref lastReadingTime) - lastTimeCheck, 0);

        if (interval > AbstractTrafficShapingHandler.MINIMAL_WAIT)
        {
            // Enough interval time to compute shaping
            long time = sum * 1000 / limitTraffic - interval + pastDelay;
            if (time > AbstractTrafficShapingHandler.MINIMAL_WAIT)
            {
                if (logger.IsDebugEnabled())
                {
                    logger.Debug("Time: " + time + ':' + sum + ':' + interval + ':' + pastDelay);
                }

                if (time > maxTime && now + time - localReadingTime > maxTime)
                {
                    time = maxTime;
                }

                readingTime = Math.Max(localReadingTime, now + time);
                return time;
            }

            readingTime = Math.Max(localReadingTime, now);
            return 0;
        }

        // take the last read interval check to get enough interval time
        long lastsum = sum + lastRB;
        long lastinterval = interval + checkInterval.Get();
        long time2 = lastsum * 1000 / limitTraffic - lastinterval + pastDelay;
        if (time2 > AbstractTrafficShapingHandler.MINIMAL_WAIT)
        {
            if (logger.IsDebugEnabled())
            {
                logger.Debug("Time: " + time2 + ':' + lastsum + ':' + lastinterval + ':' + pastDelay);
            }

            if (time2 > maxTime && now + time2 - localReadingTime > maxTime)
            {
                time2 = maxTime;
            }

            readingTime = Math.Max(localReadingTime, now + time2);
            return time2;
        }

        readingTime = Math.Max(localReadingTime, now);
        return 0;
    }

    /**
     * Returns the time to wait (if any) for the given length message, using the given limitTraffic and
     * the max wait time.
     *
     * @param size
     *            the write size
     * @param limitTraffic
     *            the traffic limit in bytes per second.
     * @param maxTime
     *            the max time in ms to wait in case of excess of traffic.
     * @return the current time to wait (in ms) if needed for Write operation.
     */
    // CLR: @Deprecated without a @deprecated tag names no replacement, so the method is ported with [Obsolete].
    [Obsolete]
    public long WriteTimeToWait(long size, long limitTraffic, long maxTime)
    {
        return WriteTimeToWait(size, limitTraffic, maxTime, MilliSecondFromNano());
    }

    /**
     * Returns the time to wait (if any) for the given length message, using the given limitTraffic and
     * the max wait time.
     *
     * @param size
     *            the write size
     * @param limitTraffic
     *            the traffic limit in bytes per second.
     * @param maxTime
     *            the max time in ms to wait in case of excess of traffic.
     * @param now the current time
     * @return the current time to wait (in ms) if needed for Write operation.
     */
    // CLR: the second local time is time2: a C# local is in scope in its whole block, so it cannot share the name of the
    // local declared in the nested if block (CS0136).
    public long WriteTimeToWait(long size, long limitTraffic, long maxTime, long now)
    {
        BytesWriteFlowControl(size);
        if (size == 0 || limitTraffic == 0)
        {
            return 0;
        }

        long lastTimeCheck = lastTime.Get();
        long sum = currentWrittenBytes.Get();
        long lastWB = Volatile.Read(ref lastWrittenBytes);
        long localWritingTime = writingTime;
        long pastDelay = Math.Max(Volatile.Read(ref lastWritingTime) - lastTimeCheck, 0);
        long interval = now - lastTimeCheck;
        if (interval > AbstractTrafficShapingHandler.MINIMAL_WAIT)
        {
            // Enough interval time to compute shaping
            long time = sum * 1000 / limitTraffic - interval + pastDelay;
            if (time > AbstractTrafficShapingHandler.MINIMAL_WAIT)
            {
                if (logger.IsDebugEnabled())
                {
                    logger.Debug("Time: " + time + ':' + sum + ':' + interval + ':' + pastDelay);
                }

                if (time > maxTime && now + time - localWritingTime > maxTime)
                {
                    time = maxTime;
                }

                writingTime = Math.Max(localWritingTime, now + time);
                return time;
            }

            writingTime = Math.Max(localWritingTime, now);
            return 0;
        }

        // take the last write interval check to get enough interval time
        long lastsum = sum + lastWB;
        long lastinterval = interval + checkInterval.Get();
        long time2 = lastsum * 1000 / limitTraffic - lastinterval + pastDelay;
        if (time2 > AbstractTrafficShapingHandler.MINIMAL_WAIT)
        {
            if (logger.IsDebugEnabled())
            {
                logger.Debug("Time: " + time2 + ':' + lastsum + ':' + lastinterval + ':' + pastDelay);
            }

            if (time2 > maxTime && now + time2 - localWritingTime > maxTime)
            {
                time2 = maxTime;
            }

            writingTime = Math.Max(localWritingTime, now + time2);
            return time2;
        }

        writingTime = Math.Max(localWritingTime, now);
        return 0;
    }

    public override string ToString()
    {
        return new StringBuilder(165).Append("Monitor ").Append(name)
                .Append(" Current Speed Read: ").Append((lastReadThroughput >> 10).ToString(CultureInfo.InvariantCulture)).Append(" KB/s, ")
                .Append("Asked Write: ").Append((lastWriteThroughput >> 10).ToString(CultureInfo.InvariantCulture)).Append(" KB/s, ")
                .Append("Real Write: ").Append((realWriteThroughput >> 10).ToString(CultureInfo.InvariantCulture)).Append(" KB/s, ")
                .Append("Current Read: ").Append((currentReadBytes.Get() >> 10).ToString(CultureInfo.InvariantCulture)).Append(" KB, ")
                .Append("Current asked Write: ").Append((currentWrittenBytes.Get() >> 10).ToString(CultureInfo.InvariantCulture)).Append(" KB, ")
                .Append("Current real Write: ").Append((realWrittenBytes.Get() >> 10).ToString(CultureInfo.InvariantCulture)).Append(" KB").ToString();
    }
}
