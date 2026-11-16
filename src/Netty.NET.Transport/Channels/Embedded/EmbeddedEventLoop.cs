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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Transport.Channels.Embedded;

/// <summary>A manually driven event loop. Embedded calls are owned by the test thread, as in Netty.</summary>
// CLR: public so tests can construct it directly (package-private final in Java); the default ticker is the
// FreezableTicker that EmbeddedChannel passes in Java.
public sealed class EmbeddedEventLoop : AbstractScheduledEventExecutor, IEventLoop
{
    private readonly Ticker ticker;

    private readonly ConcurrentQueue<Action> tasks = new();

    public EmbeddedEventLoop(Ticker ticker = null)
    {
        this.ticker = ticker ?? new FreezableTicker();
    }

    public override void Execute(Action command)
    {
        tasks.Enqueue(ObjectUtil.CheckNotNull(command, "command"));
    }

    public void RunTasks()
    {
        for (; ; )
        {
            if (!tasks.TryDequeue(out Action task))
            {
                break;
            }

            task();
        }
    }

    public bool HasPendingNormalTasks()
    {
        return !tasks.IsEmpty;
    }

    public long RunScheduledTasks()
    {
        // CLR: getCurrentTimeNanos() is [Obsolete] in favor of ticker() and returns ticker().nanoTime(); read the
        // ticker directly instead of overriding the obsolete member.
        long time = ticker.NanoTime();
        for (; ; )
        {
            Action task = PollScheduledTask(time);
            if (task == null)
            {
                return NextScheduledTaskNano();
            }

            task();
        }
    }

    public long NextScheduledTask()
    {
        return NextScheduledTaskNano();
    }

    public override Ticker Ticker()
    {
        return ticker;
    }

    // CLR: an override cannot widen protected to internal access, so the package-private override is this wrapper.
    internal void CancelTasks()
    {
        CancelScheduledTasks();
    }

    public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
    {
        throw new NotSupportedException();
    }

    // CLR: terminationFuture() throws UnsupportedOperationException; the property returns a faulted Task so that
    // generic executor plumbing reading the property does not throw.
    private readonly Task _unsupported = Task.FromException(new NotSupportedException("Embedded loops are manually driven."));

    public override Task TerminationFuture => _unsupported;

    [Obsolete]
    public override void Shutdown()
    {
        throw new NotSupportedException();
    }

    public override bool IsShuttingDown()
    {
        return false;
    }

    public override bool IsShutdown()
    {
        return false;
    }

    public override bool IsTerminated()
    {
        return false;
    }

    public override bool AwaitTermination(TimeSpan timeout)
    {
        return false;
    }

    public Task RegisterAsync(IChannel channel, CancellationToken cancellationToken = default) => channel.RegisterAsync(this, cancellationToken);

    public override bool InEventLoop(Thread thread)
    {
        return true;
    }

    /**
     * Ticker that implements the old {@link EmbeddedChannel} time freezing mechanics.
     */
    internal sealed class FreezableTicker : MockTicker
    {
        private readonly Ticker unfrozen = global::Netty.NET.Common.Concurrent.Ticker.SystemTicker();
        /**
         * When time is not {@link #timeFrozen frozen}, the base time to subtract from {@link System#nanoTime()}. When
         * time is frozen, this variable is unused.
         */
        private long startTime;
        /**
         * When time is frozen, the timestamp returned by {@link #getCurrentTimeNanos()}. When unfrozen, this is unused.
         */
        private long frozenTimestamp;
        /**
         * Whether time is currently frozen.
         */
        private bool timeFrozen;

        public override void Advance(long nanos)
        {
            if (timeFrozen)
            {
                frozenTimestamp = unchecked(frozenTimestamp + nanos);
            }
            else
            {
                // startTime is subtracted from nanoTime, so increasing the startTime will advance
                // getCurrentTimeNanos
                startTime = unchecked(startTime - nanos);
            }
        }

        public override long NanoTime()
        {
            if (timeFrozen)
            {
                return frozenTimestamp;
            }
            return unchecked(unfrozen.NanoTime() - startTime);
        }

        public override void Sleep(long delayNanos)
        {
            throw new NotSupportedException("Sleeping is not supported by the default ticker for " +
                    "EmbeddedEventLoop. Please use a different ticker implementation if you require sleep support.");
        }

        public void FreezeTime()
        {
            if (!timeFrozen)
            {
                frozenTimestamp = NanoTime();
                timeFrozen = true;
            }
        }

        public void UnfreezeTime()
        {
            if (timeFrozen)
            {
                // we want getCurrentTimeNanos to continue right where frozenTimestamp left off:
                // nanoTime = unfrozen.nanoTime - startTime = frozenTimestamp
                // then solve for startTime
                startTime = unchecked(unfrozen.NanoTime() - frozenTimestamp);
                timeFrozen = false;
            }
        }
    }
}
