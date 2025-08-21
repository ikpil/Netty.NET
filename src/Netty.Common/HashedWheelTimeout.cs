using System;
using System.Text;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common;

public class HashedWheelTimeout : ITimeout, IRunnable
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(HashedWheelTimer));

    private const int ST_INIT = 0;
    private const int ST_CANCELLED = 1;
    private const int ST_EXPIRED = 2;

    private static readonly AtomicIntegerFieldUpdater<HashedWheelTimeout> STATE_UPDATER =
        AtomicIntegerFieldUpdater.newUpdater(typeof(HashedWheelTimeout), "state");

    private readonly HashedWheelTimer _timer;
    private readonly ITimerTask _task;
    private readonly long _deadline;

    //@SuppressWarnings({"unused", "FieldMayBeFinal", "RedundantFieldInitialization" })
    private volatile int _state = ST_INIT;

    // remainingRounds will be calculated and set by Worker.transferTimeoutsToBuckets() before the
    // HashedWheelTimeout will be added to the correct HashedWheelBucket.
    long remainingRounds;

    // This will be used to chain timeouts in HashedWheelTimerBucket via a double-linked-list.
    // As only the workerThread will act on it there is no need for synchronization / volatile.
    private HashedWheelTimeout next;
    private HashedWheelTimeout prev;

    // The bucket to which the timeout was added
    private HashedWheelBucket bucket;

    public HashedWheelTimeout(HashedWheelTimer timer, ITimerTask task, long deadline)
    {
        _timer = timer;
        _task = task;
        _deadline = deadline;
    }

    public ITimer timer()
    {
        return _timer;
    }

    public ITimerTask task()
    {
        return _task;
    }

    public bool cancel()
    {
        // only update the state it will be removed from HashedWheelBucket on next tick.
        if (!compareAndSetState(ST_INIT, ST_CANCELLED))
        {
            return false;
        }

        // If a task should be canceled we put this to another queue which will be processed on each tick.
        // So this means that we will have a GC latency of max. 1 tick duration which is good enough. This way
        // we can make again use of our MpscLinkedQueue and so minimize the locking / overhead as much as possible.
        _timer._cancelledTimeouts.add(this);
        return true;
    }

    private void remove()
    {
        HashedWheelBucket bucket = this.bucket;
        if (bucket != null)
        {
            bucket.remove(this);
        }

        _timer._pendingTimeouts.decrementAndGet();
    }

    void removeAfterCancellation()
    {
        remove();
        _task.cancelled(this);
    }

    public bool compareAndSetState(int expected, int state)
    {
        return STATE_UPDATER.compareAndSet(this, expected, state);
    }

    public int state()
    {
        return _state;
    }

    public bool isCancelled()
    {
        return state() == ST_CANCELLED;
    }

    public bool isExpired()
    {
        return state() == ST_EXPIRED;
    }

    public void expire()
    {
        if (!compareAndSetState(ST_INIT, ST_EXPIRED))
        {
            return;
        }

        try
        {
            remove();
            _timer._taskExecutor.execute(this);
        }
        catch (Exception t)
        {
            if (logger.isWarnEnabled())
            {
                logger.warn("An exception was thrown while submit " + nameof(ITimerTask)
                                                                    + " for execution.", t);
            }
        }
    }

    public void run()
    {
        try
        {
            _task.run(this);
        }
        catch (Exception t)
        {
            if (logger.isWarnEnabled())
            {
                logger.warn("An exception was thrown by " + nameof(ITimerTask) + '.', t);
            }
        }
    }

    public override string ToString()
    {
        long currentTime = PreciseTimer.nanoTime();
        long remaining = _deadline - currentTime + _timer.startTime;

        StringBuilder buf = new StringBuilder(192)
            .Append(simpleClassName(this))
            .Append('(')
            .Append("deadline: ");
        if (remaining > 0)
        {
            buf.Append(remaining)
                .Append(" ns later");
        }
        else if (remaining < 0)
        {
            buf.Append(-remaining)
                .Append(" ns ago");
        }
        else
        {
            buf.Append("now");
        }

        if (isCancelled())
        {
            buf.Append(", cancelled");
        }

        return buf.Append(", task: ")
            .Append(task())
            .Append(')')
            .ToString();
    }
}