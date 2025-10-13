using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Common.Concurrent;

public static class ScheduledTask
{
    public static long deadlineToDelayNanos(long currentTimeNanos, long deadlineNanos)
    {
        return deadlineNanos == 0L ? 0L : Math.Max(0L, deadlineNanos - currentTimeNanos);
    }
}

public class ScheduledTask<T> : IScheduledTask<T>
{
    public const int CancellationProhibited = 1;
    public const int CancellationRequested = 1 << 1;

    private const int INDEX_NOT_IN_QUEUE = -1;

    protected readonly AbstractScheduledEventExecutor Executor;
    protected readonly TaskCompletionSource<T> Promise;

    private long _id;
    private long _deadlineNanos;

    /* 0 - no repeat, >0 - repeat at fixed rate, <0 - repeat with fixed delay */
    private readonly long _periodNanos;
    private int _volatileCancellationState;

    private int _queueIndex = INDEX_NOT_IN_QUEUE;
    public Task<T> Completion => Promise.Task;
    public T Result => Promise.Task.Result;

    protected ScheduledTask(AbstractScheduledEventExecutor executor, TaskCompletionSource<T> promise, long deadlineNanos)
    {
        Executor = executor;
        Promise = promise;
        _deadlineNanos = deadlineNanos;
        _periodNanos = 0;
    }

    protected ScheduledTask(AbstractScheduledEventExecutor executor, TaskCompletionSource<T> promise, long deadlineNanos, long periodNanos)
    {
        Executor = executor;
        Promise = promise;
        _deadlineNanos = deadlineNanos;
        _periodNanos = validatePeriod(periodNanos);
    }

    private static long validatePeriod(long period)
    {
        if (period == 0)
        {
            throw new ArgumentException("period: 0 (expected: != 0)");
        }

        return period;
    }

    public IScheduledTask setId(long id)
    {
        if (_id == 0L)
        {
            _id = id;
        }

        return this;
    }

    public int CompareTo(IScheduledTask o)
    {
        if (this == o)
        {
            return 0;
        }

        var that = (ScheduledTask<T>)o;
        long d = deadlineNanos() - o.deadlineNanos();
        if (d < 0)
        {
            return -1;
        }
        else if (d > 0)
        {
            return 1;
        }
        else if (_id < that._id)
        {
            return -1;
        }
        else
        {
            Debug.Assert(_id != that._id);
            return 1;
        }
    }

    public virtual void run()
    {
        Debug.Assert(Executor.inEventLoop());
        try
        {
            if (delayNanos() > 0L)
            {
                // Not yet expired, need to add or remove from queue
                if (isCancelled())
                {
                    Executor.scheduledTaskQueue().remove(this);
                }
                else
                {
                    Executor.scheduleFromEventLoop(this);
                }

                return;
            }

            if (_periodNanos == 0)
            {
                if (setUncancellableInternal())
                {
                    V result = runTask();
                    setSuccessInternal(result);
                }
            }
            else
            {
                // check if is done as it may was cancelled
                if (!isCancelled())
                {
                    runTask();
                    if (!Executor.isShutdown())
                    {
                        if (_periodNanos > 0)
                        {
                            _deadlineNanos += _periodNanos;
                        }
                        else
                        {
                            _deadlineNanos = Executor.getCurrentTimeNanos() - _periodNanos;
                        }

                        if (!isCancelled())
                        {
                            Executor.scheduledTaskQueue().tryEnqueue(this);
                        }
                    }
                }
            }
        }
        catch (Exception cause)
        {
            setFailureInternal(cause);
        }
    }

    public bool cancel()
    {
        if (!AtomicCancellationStateUpdate(CancellationRequested, CancellationProhibited))
        {
            return false;
        }

        bool canceled = Promise.TrySetCanceled();
        if (canceled)
        {
            Executor.removeScheduled(this);
        }

        return canceled;
    }

    public bool cancelWithoutRemove(bool mayInterruptIfRunning)
    {
        if (!AtomicCancellationStateUpdate(CancellationRequested, CancellationProhibited))
        {
            return false;
        }

        return Promise.TrySetCanceled();
    }

    public long deadlineNanos()
    {
        return _deadlineNanos;
    }

    public long delayNanos(long currentTimeNanos)
    {
        return ScheduledTask.deadlineToDelayNanos(currentTimeNanos, _deadlineNanos);
    }

    public long delayNanos()
    {
        if (_deadlineNanos == 0L)
        {
            return 0L;
        }

        return delayNanos(Executor.getCurrentTimeNanos());
    }


    public TaskAwaiter GetAwaiter()
    {
        return Completion.GetAwaiter();
    }

    public void setConsumed()
    {
        throw new NotImplementedException();
    }

    private bool TrySetUncancelable()
    {
        return AtomicCancellationStateUpdate(CancellationProhibited, CancellationRequested);
    }

    private bool AtomicCancellationStateUpdate(int newBits, int illegalBits)
    {
        int cancellationState = Volatile.Read(ref _volatileCancellationState);
        int oldCancellationState;
        do
        {
            oldCancellationState = cancellationState;
            if ((cancellationState & illegalBits) != 0)
            {
                return false;
            }

            cancellationState = Interlocked.CompareExchange(ref _volatileCancellationState, cancellationState | newBits, cancellationState);
        } while (cancellationState != oldCancellationState);

        return true;
    }
}