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
using System.Threading;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common;

internal sealed class HashedWheelWorker : IRunnable
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(HashedWheelTimer));

    private readonly HashSet<ITimeout> _unprocessedTimeouts = new HashSet<ITimeout>();

    private readonly HashedWheelTimer _timer;
    private long _tick;

    internal HashedWheelWorker(HashedWheelTimer timer)
    {
        _timer = timer;
    }

    public void Run()
    {
        // Initialize the startTime.
        Volatile.Write(ref _timer._startTime, SystemTimer.NanoTime());
        if (Volatile.Read(ref _timer._startTime) == 0)
        {
            // We use 0 as an indicator for the uninitialized value here, so make sure it's not 0 when initialized.
            Volatile.Write(ref _timer._startTime, 1);
        }

        // Notify the other threads waiting for the initialization at start().
        _timer._startTimeInitialized.Signal();

        do
        {
            long deadline = WaitForNextTick();
            if (deadline > 0)
            {
                int idx = (int)(_tick & _timer._mask);
                ProcessCancelledTasks();
                HashedWheelBucket bucket =
                    _timer._wheel[idx];
                TransferTimeoutsToBuckets();
                bucket.ExpireTimeouts(deadline);
                _tick++;
            }
        } while (Volatile.Read(ref _timer._workerState) == HashedWheelTimer.WORKER_STATE_STARTED);

        // Fill the unprocessedTimeouts so we can return them from stop() method.
        foreach (HashedWheelBucket bucket in _timer._wheel)
        {
            bucket.ClearTimeouts(_unprocessedTimeouts);
        }

        for (;;)
        {
            _timer._timeouts.TryDequeue(out var timeout);
            if (timeout == null)
            {
                break;
            }

            if (!timeout.IsCancelled())
            {
                _unprocessedTimeouts.Add(timeout);
            }
        }

        ProcessCancelledTasks();
    }

    private void TransferTimeoutsToBuckets()
    {
        // transfer only max. 100000 timeouts per tick to prevent a thread to stale the workerThread when it just
        // adds new timeouts in a loop.
        for (int i = 0; i < 100000; i++)
        {
            _timer._timeouts.TryDequeue(out var timeout);
            if (timeout == null)
            {
                // all processed
                break;
            }

            if (timeout.State() == HashedWheelTimeout.ST_CANCELLED)
            {
                // Was cancelled in the meantime.
                continue;
            }

            long calculated = timeout._deadline / _timer._tickDuration;
            timeout._remainingRounds = (calculated - _tick) / _timer._wheel.Length;

            long ticks = Math.Max(calculated, _tick); // Ensure we don't schedule for past.
            int stopIndex = (int)(ticks & _timer._mask);

            HashedWheelBucket bucket = _timer._wheel[stopIndex];
            bucket.AddTimeout(timeout);
        }
    }

    private void ProcessCancelledTasks()
    {
        for (;;)
        {
            _timer._cancelledTimeouts.TryDequeue(out var timeout);
            if (timeout == null)
            {
                // all processed
                break;
            }

            try
            {
                timeout.RemoveAfterCancellation();
            }
            catch (Exception t)
            {
                if (logger.IsWarnEnabled())
                {
                    logger.Warn("An exception was thrown while process a cancellation task", t);
                }
            }
        }
    }

    /**
         * calculate goal nanoTime from startTime and current tick number,
         * then wait until that goal has been reached.
         * @return Long.MIN_VALUE if received a shutdown request,
         * current time otherwise (with Long.MIN_VALUE changed by +1)
         */
    private long WaitForNextTick()
    {
        long deadline = _timer._tickDuration * (_tick + 1);

        for (;;)
        {
            long currentTime = SystemTimer.NanoTime() - Volatile.Read(ref _timer._startTime);
            long sleepTimeMs = (deadline - currentTime + 999999) / 1000000;

            if (sleepTimeMs <= 0)
            {
                if (currentTime == long.MinValue)
                {
                    return -long.MaxValue;
                }
                else
                {
                    return currentTime;
                }
            }

            // Check if we run on windows, as if thats the case we will need
            // to round the sleepTime as workaround for a bug that only affect
            // the JVM if it runs on windows.
            //
            // See https://github.com/netty/netty/issues/356
            // The JVM-specific Windows workaround above does not apply to
            // CLR Thread.Sleep. Keep the native rounded-up millisecond wait.

            try
            {
                // Sleep(Int32) is bounded on CLR; recheck the monotonic deadline
                // after every chunk and remain interruptible for stop().
                Thread.Sleep((int)Math.Min(sleepTimeMs, int.MaxValue));
            }
            catch (ThreadInterruptedException ignored)
            {
                if (Volatile.Read(ref _timer._workerState) == HashedWheelTimer.WORKER_STATE_SHUTDOWN)
                {
                    return long.MinValue;
                }
            }
        }
    }

    public IReadOnlyCollection<ITimeout> UnprocessedTimeouts()
    {
        return Array.AsReadOnly<ITimeout>([.. _unprocessedTimeouts]);
    }
}
