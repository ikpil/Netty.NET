/*
 * Copyright 2025 The Netty Project
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
using System.Threading;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Common.Concurrent;

/**
 * The default {@link MockTicker} implementation.
 */
public sealed class DefaultMockTicker : MockTicker
{
    // The lock is fair, so waiters get to process condition signals in the order they (the waiters) queued up.
    // CLR: Monitor alone is not fair. The explicit tick queue lets existing
    // sleepers observe each advance in registration order before a new phase,
    // observer or advance can pass them. This is clock policy, not a fair mutex.
    private readonly object _lock = new object();
    private long _nanoTime;
    private readonly HashSet<Thread> sleepers = new HashSet<Thread>(ReferenceEqualityComparer.Instance);
    private readonly LinkedList<Sleeper> registered = new LinkedList<Sleeper>();
    private readonly LinkedList<Sleeper> pendingTicks = new LinkedList<Sleeper>();

    private sealed class Sleeper
    {
        internal readonly LinkedListNode<Sleeper> Tick;
        internal Sleeper() => Tick = new LinkedListNode<Sleeper>(this);
    }

    public DefaultMockTicker()
    {
    }

    public override long NanoTime()
    {
        return Interlocked.Read(ref _nanoTime);
    }

    // nano time
    public override void Sleep(long delayNanos)
    {
        CheckPositiveOrZero(delayNanos, "delayNanos");

        if (delayNanos == 0)
        {
            return;
        }

        lock (_lock)
        {
            // A signaled old phase must leave its registration before an
            // observer can mistake it for this newly admitted sleep.
            while (pendingTicks.Count != 0) Monitor.Wait(_lock);
            var sleeper = new Sleeper();
            var registration = registered.AddLast(sleeper);
            try
            {
                long startTimeNanos = NanoTime();
                sleepers.Add(Thread.CurrentThread);
                Monitor.PulseAll(_lock);
                while (true)
                {
                    while (pendingTicks.First != sleeper.Tick) Monitor.Wait(_lock);
                    pendingTicks.RemoveFirst();
                    if (unchecked(NanoTime() - startTimeNanos) >= delayNanos) return;
                    Monitor.PulseAll(_lock);
                }
            }
            finally
            {
                sleepers.Remove(Thread.CurrentThread);
                registered.Remove(registration);
                if (sleeper.Tick.List != null) pendingTicks.Remove(sleeper.Tick);
                Monitor.PulseAll(_lock);
            }
        }
    }

    /**
     * Wait for the given thread to enter the {@link #sleep(long, TimeUnit)} method, and block.
     */
    public void AwaitSleepingThread(Thread thread)
    {
        lock (_lock)
        {
            while (pendingTicks.Count != 0 || !sleepers.Contains(thread))
            {
                Monitor.Wait(_lock);
            }
        }
    }

    public override void Advance(long amountNanos)
    {
        CheckPositiveOrZero(amountNanos, "amountNanos");

        if (amountNanos == 0)
        {
            return;
        }

        bool interrupted = false;
        try
        {
            // Java lock.lock is noninterruptible, while sleep/observation entry
            // is interruptible. Preserve that distinction with native monitors.
            using (UninterruptibleMonitor.Enter(_lock))
            {
                while (pendingTicks.Count != 0)
                {
                    try { Monitor.Wait(_lock); }
                    catch (ThreadInterruptedException) { interrupted = true; }
                }
                Interlocked.Add(ref _nanoTime, amountNanos);
                foreach (var sleeper in registered) pendingTicks.AddLast(sleeper.Tick);
                Monitor.PulseAll(_lock);
            }
        }
        finally
        {
            if (interrupted) Thread.CurrentThread.Interrupt();
        }
    }
}
