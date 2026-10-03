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
using System.Threading;

namespace Netty.NET.Common.Concurrent;

sealed class SystemTicker : Ticker
{
    public static readonly SystemTicker INSTANCE = new SystemTicker();
    private static readonly long START_TIME = SystemTimer.nanoTime();

    public override long initialNanoTime()
    {
        return START_TIME;
    }

    public override long nanoTime()
    {
        return SystemTimer.nanoTime() - START_TIME;
    }

    public override void sleep(long delayNanos)
    {
        if (delayNanos <= 0) return;
        // Thread.Sleep has millisecond resolution. Round a positive remainder up;
        // adding 999999 before division would overflow for long.MaxValue.
        SleepMilliseconds(delayNanos / 1_000_000 + (delayNanos % 1_000_000 == 0 ? 0 : 1));
    }

    public override void sleepMillis(long delayMillis) => SleepMilliseconds(delayMillis);

    public override void sleep(TimeSpan delay)
    {
        if (delay.Ticks <= 0) return;
        // Keep the complete CLR duration rather than saturating it to 292 years.
        SleepMilliseconds(delay.Ticks / TimeSpan.TicksPerMillisecond +
            (delay.Ticks % TimeSpan.TicksPerMillisecond == 0 ? 0 : 1));
    }

    private static void SleepMilliseconds(long remaining)
    {
        // Positive waits retain Thread.Interrupt semantics, including long waits.
        // Zero and negative TimeUnit.sleep calls do not consume pending interrupts.
        while (remaining > 0)
        {
            int chunk = (int)Math.Min(remaining, int.MaxValue);
            Thread.Sleep(chunk);
            remaining -= chunk;
        }
    }
}
