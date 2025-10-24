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
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Common.Concurrent;

/**
 * The default {@link MockTicker} implementation.
 */
public sealed class DefaultMockTicker : MockTicker
{
    // The lock is fair, so waiters get to process condition signals in the order they (the waiters) queued up.
    private readonly object _lock = new object();
    private readonly AtomicLong _nanoTime = new AtomicLong();
    private readonly Dictionary<Thread, bool> sleepers = new Dictionary<Thread, bool>();

    public DefaultMockTicker()
    {
    }

    public override long nanoTime()
    {
        return _nanoTime.get();
    }

    // nano time
    public override void sleep(long delayNanos)
    {
        checkPositiveOrZero(delayNanos, "delayNanos");

        if (delayNanos == 0)
        {
            return;
        }

        lock (_lock)
        {
            try
            {
                long startTimeNanos = nanoTime();
                sleepers.Add(Thread.CurrentThread, true);
                Monitor.PulseAll(_lock);
                do
                {
                    Monitor.Wait(_lock);
                } while (nanoTime() - startTimeNanos < delayNanos);
            }
            finally
            {
                sleepers.Remove(Thread.CurrentThread);
            }
        }
    }

    /**
     * Wait for the given thread to enter the {@link #sleep(long, TimeSpan)} method, and block.
     */
    public void awaitSleepingThread(Thread thread)
    {
        lock (_lock)
        {
            while (!sleepers.ContainsKey(thread))
            {
                Monitor.Wait(_lock);
            }
        }
    }

    public override void advance(long amountNanos)
    {
        checkPositiveOrZero(amountNanos, "amountNanos");

        if (amountNanos == 0)
        {
            return;
        }

        lock (_lock)
        {
            _nanoTime.addAndGet(amountNanos);
            Monitor.PulseAll(_lock);
        }
    }
}