using System;
using System.Collections.Generic;
using System.Threading;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common;

public class HashedWheelWorker : IRunnable
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(HashedWheelTimer));

    private readonly ISet<ITimeout> _unprocessedTimeouts = new HashSet<ITimeout>();

    private readonly HashedWheelTimer _timer;
    private long _tick;

    internal HashedWheelWorker(HashedWheelTimer timer)
    {
        _timer = timer;
    }

    public void run()
    {
        // Initialize the startTime.
        _timer._startTime.set(PreciseTimer.nanoTime());
        if (_timer._startTime.read() == 0)
        {
            // We use 0 as an indicator for the uninitialized value here, so make sure it's not 0 when initialized.
            _timer._startTime.set(1);
        }

        // Notify the other threads waiting for the initialization at start().
        _timer._startTimeInitialized.Signal();

        do
        {
            long deadline = waitForNextTick();
            if (deadline > 0)
            {
                int idx = (int)(_tick & _timer._mask);
                processCancelledTasks();
                HashedWheelBucket bucket =
                    _timer._wheel[idx];
                transferTimeoutsToBuckets();
                bucket.expireTimeouts(deadline);
                _tick++;
            }
        } while (_timer._workerState.read() == HashedWheelTimer.WORKER_STATE_STARTED);

        // Fill the unprocessedTimeouts so we can return them from stop() method.
        foreach (HashedWheelBucket bucket in _timer._wheel)
        {
            bucket.clearTimeouts(_unprocessedTimeouts);
        }

        for (;;)
        {
            _timer._timeouts.TryDequeue(out var timeout);
            if (timeout == null)
            {
                break;
            }

            if (!timeout.isCancelled())
            {
                _unprocessedTimeouts.Add(timeout);
            }
        }

        processCancelledTasks();
    }

    private void transferTimeoutsToBuckets()
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

            if (timeout.state() == HashedWheelTimeout.ST_CANCELLED)
            {
                // Was cancelled in the meantime.
                continue;
            }

            long calculated = timeout._deadline / _timer._tickDuration;
            timeout._remainingRounds = (calculated - _tick) / _timer._wheel.Length;

            long ticks = Math.Max(calculated, _tick); // Ensure we don't schedule for past.
            int stopIndex = (int)(ticks & _timer._mask);

            HashedWheelBucket bucket = _timer._wheel[stopIndex];
            bucket.addTimeout(timeout);
        }
    }

    private void processCancelledTasks()
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
                timeout.removeAfterCancellation();
            }
            catch (Exception t)
            {
                if (logger.isWarnEnabled())
                {
                    logger.warn("An exception was thrown while process a cancellation task", t);
                }
            }
        }
    }

    /**
     * calculate goal nanoTime from startTime and current tick number,
     * then wait until that goal has been reached.
     * @return long.MinValue if received a shutdown request,
     * current time otherwise (with long.MinValue changed by +1)
     */
    private long waitForNextTick()
    {
        long deadline = _timer._tickDuration * (_tick + 1);

        for (;;)
        {
            long currentTime = PreciseTimer.nanoTime() - _timer._startTime.read();
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
            if (PlatformDependent.isWindows())
            {
                sleepTimeMs = sleepTimeMs / 10 * 10;
                if (sleepTimeMs == 0)
                {
                    sleepTimeMs = 1;
                }
            }

            try
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(sleepTimeMs));
            }
            catch (ThreadInterruptedException ignored)
            {
                if (_timer._workerState.read() == HashedWheelTimer.WORKER_STATE_SHUTDOWN)
                {
                    return long.MinValue;
                }
            }
        }
    }

    public ISet<ITimeout> unprocessedTimeouts()
    {
        return Collections.unmodifiableSet(_unprocessedTimeouts);
    }
}