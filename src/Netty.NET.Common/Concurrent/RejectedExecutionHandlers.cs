/*
 * Copyright 2016 The Netty Project
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
using System.Diagnostics;
using System.Threading;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;

/**
 * Expose helper methods which create different {@link RejectedExecutionHandler}s.
 */
public static class RejectedExecutionHandlers
{
    private static readonly Action<Action, SingleThreadEventExecutor> REJECT =
        static (_, _) => throw new RejectedExecutionException();

    /**
     * Returns a {@link RejectedExecutionHandler} that will always just throw a {@link RejectedExecutionException}.
     */
    public static Action<Action, SingleThreadEventExecutor> Reject()
    {
        return REJECT;
    }

    /**
     * Tries to backoff when the task can not be added due restrictions for an configured amount of time. This
     * is only done if the task was added from outside of the event loop which means
     * {@link EventExecutor#inEventLoop()} returns {@code false}.
     */
    public static Action<Action, SingleThreadEventExecutor> Backoff(int retries, TimeSpan backoffAmount)
    {
        ObjectUtil.CheckPositive(retries, "retries");
        return (task, executor) =>
        {
            if (!executor.InEventLoop())
            {
                for (int i = 0; i < retries; i++)
                {
                    // Try to wake up the executor so it will empty its task queue.
                    executor.Wakeup(false);

                    WaitBackoff(backoffAmount);
                    if (executor.OfferTask(task))
                    {
                        return;
                    }
                }
            }
            // Either we tried to add the task from within the EventLoop or we was not able to add it even with
            // backoff.
            throw new RejectedExecutionException();
        };
    }

    private static void WaitBackoff(TimeSpan delay)
    {
        // Java parkNanos does not wait for non-positive amounts. CLR Sleep throws
        // for negative durations and consumes interrupts; restore an interrupt
        // and return early, preserving the original retry policy and pending flag.
        if (delay <= TimeSpan.Zero) return;
        long started = Stopwatch.GetTimestamp();
        TimeSpan remaining = delay;
        while (remaining > TimeSpan.Zero)
        {
            // Round up without overflowing TimeSpan.MaxValue, and chunk waits
            // to CLR Sleep's millisecond limit. No nanosecond floating conversion.
            int milliseconds = (int)Math.Min(int.MaxValue, 1 + (remaining.Ticks - 1) / TimeSpan.TicksPerMillisecond);
            try { Thread.Sleep(milliseconds); }
            catch (ThreadInterruptedException)
            {
                Thread.CurrentThread.Interrupt();
                return;
            }
            remaining = delay - Stopwatch.GetElapsedTime(started);
        }
    }
}
