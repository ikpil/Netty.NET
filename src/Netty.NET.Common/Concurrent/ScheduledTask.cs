/*
 * Copyright 2013 The Netty Project
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
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;

// CLR compatibility name for the static delay helper.
public static class ScheduledTask
{
    public static long deadlineToDelayNanos(long currentTimeNanos, long deadlineNanos) =>
        deadlineNanos == 0L ? 0L : Math.Max(0L, deadlineNanos - currentTimeNanos);
}

// CLR adaptation: ScheduledFutureTask<V> is exposed through the existing IScheduledTask wrappers.
public class ScheduledTask<V> : PromiseTask<V>, IScheduledTask<V>, IPriorityQueueNode<IScheduledTask>
{
    // set once when added to priority queue
    private long _id;
    private long _deadlineNanos;
    /* 0 - no repeat, >0 - repeat at fixed rate, <0 - repeat with fixed delay */
    private readonly long _periodNanos;
    private int _queueIndex = IPriorityQueueNode<IScheduledTask>.INDEX_NOT_IN_QUEUE;
    public Task<V> Completion => Task;
    public V Result => get();

    protected ScheduledTask(AbstractScheduledEventExecutor executor, IRunnable runnable, long deadlineNanos)
        : base(executor, runnable)
    {
        _deadlineNanos = deadlineNanos;
        _periodNanos = 0;
    }
    protected ScheduledTask(AbstractScheduledEventExecutor executor, IRunnable runnable, long deadlineNanos, long periodNanos)
        : base(executor, runnable)
    {
        _deadlineNanos = deadlineNanos;
        _periodNanos = validatePeriod(periodNanos);
    }
    protected ScheduledTask(AbstractScheduledEventExecutor executor, ICallable<V> callable, long deadlineNanos)
        : base(executor, callable)
    {
        _deadlineNanos = deadlineNanos;
        _periodNanos = 0;
    }
    protected ScheduledTask(AbstractScheduledEventExecutor executor, ICallable<V> callable, long deadlineNanos, long periodNanos)
        : base(executor, callable)
    {
        _deadlineNanos = deadlineNanos;
        _periodNanos = validatePeriod(periodNanos);
    }
    private static long validatePeriod(long period)
    {
        if (period == 0) throw new ArgumentException("period: 0 (expected: != 0)");
        return period;
    }
    public IScheduledTask setId(long id)
    {
        if (_id == 0L) _id = id;
        return this;
    }
    public long getId() => _id;
    public long deadlineNanos() => _deadlineNanos;
    public void setConsumed()
    {
        // Optimization to avoid checking system clock again
        // after deadline has passed and task has been dequeued
        if (_periodNanos == 0)
        {
            Debug.Assert(scheduledExecutor().getCurrentTimeNanos() >= _deadlineNanos);
            _deadlineNanos = 0L;
        }
    }
    public long delayNanos() => _deadlineNanos == 0L ? 0L : delayNanos(scheduledExecutor().getCurrentTimeNanos());
    public long delayNanos(long currentTimeNanos) => ScheduledTask.deadlineToDelayNanos(currentTimeNanos, _deadlineNanos);
    public long getDelay() => delayNanos();
    public TimeSpan getDelayTimeSpan() => TimeSpan.FromTicks(delayNanos() / 100);
    public int CompareTo(IScheduledTask other)
    {
        if (ReferenceEquals(this, other)) return 0;
        long difference = deadlineNanos() - other.deadlineNanos();
        if (difference < 0) return -1;
        if (difference > 0) return 1;
        if (_id < other.getId()) return -1;
        Debug.Assert(_id != other.getId());
        return 1;
    }
    public override void run()
    {
        Debug.Assert(executor().inEventLoop());
        try
        {
            if (delayNanos() > 0L)
            {
                // Not yet expired, need to add or remove from queue
                if (isCancelled()) scheduledExecutor().scheduledTaskQueue().remove(this);
                else scheduledExecutor().scheduleFromEventLoop(this);
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
                    if (!executor().isShutdown())
                    {
                        if (_periodNanos > 0) _deadlineNanos += _periodNanos;
                        else _deadlineNanos = scheduledExecutor().getCurrentTimeNanos() - _periodNanos;
                        if (!isCancelled()) scheduledExecutor().scheduleFromEventLoop(this);
                    }
                }
            }
        }
        catch (Exception cause) { setFailureInternal(cause); }
    }
    private AbstractScheduledEventExecutor scheduledExecutor() => (AbstractScheduledEventExecutor)executor();
    /**
     * {@inheritDoc}
     *
     * @param mayInterruptIfRunning this value has no effect in this implementation.
     */
    public override bool cancel(bool mayInterruptIfRunning)
    {
        bool cancelled = base.cancel(mayInterruptIfRunning);
        if (cancelled) scheduledExecutor().removeScheduled(this);
        return cancelled;
    }
    public bool cancel() => cancel(false);
    public virtual bool cancelWithoutRemove(bool mayInterruptIfRunning) => base.cancel(mayInterruptIfRunning);
    public TaskAwaiter<V> GetAwaiter() => Task.GetAwaiter();
    protected override StringBuilder toStringBuilder()
    {
        StringBuilder buf = base.toStringBuilder();
        buf[buf.Length - 1] = ',';
        return buf.Append(" deadline: ").Append(_deadlineNanos).Append(", period: ").Append(_periodNanos).Append(')');
    }
    public int priorityQueueIndex(DefaultPriorityQueue<IScheduledTask> queue) => _queueIndex;
    public void priorityQueueIndex(DefaultPriorityQueue<IScheduledTask> queue, int index) => _queueIndex = index;
}
