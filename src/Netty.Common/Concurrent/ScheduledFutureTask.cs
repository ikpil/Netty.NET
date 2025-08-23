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
using System.Text;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;


//@SuppressWarnings("ComparableImplementedButEqualsNotOverridden")
public class ScheduledFutureTask<V> : PromiseTask<V> , IScheduledTask<V>, IPriorityQueueNode<V> 
{
    // set once when added to priority queue
    private long id;

    private long _deadlineNanos;
    /* 0 - no repeat, >0 - repeat at fixed rate, <0 - repeat with fixed delay */
    private readonly long _periodNanos;

    private int queueIndex = IPriorityQueueNode<V>.INDEX_NOT_IN_QUEUE;

    public ScheduledFutureTask(AbstractScheduledEventExecutor executor, IRunnable runnable, long nanoTime) 
        : base(executor, runnable)
    {
        _deadlineNanos = nanoTime;
        _periodNanos = 0;
    }

    public ScheduledFutureTask(AbstractScheduledEventExecutor executor,
            IRunnable runnable, long nanoTime, long period) 
    : base(executor, runnable)
    {

        _deadlineNanos = nanoTime;
        _periodNanos = validatePeriod(period);
    }

    public ScheduledFutureTask(AbstractScheduledEventExecutor executor,
            Func<V> callable, long nanoTime, long period) 
        : base(executor, callable)
    {

        _deadlineNanos = nanoTime;
        _periodNanos = validatePeriod(period);
    }

    public ScheduledFutureTask(AbstractScheduledEventExecutor executor,
            Func<V> callable, long nanoTime) 
    : base(executor, callable)
    {
        _deadlineNanos = nanoTime;
        _periodNanos = 0;
    }

    private static long validatePeriod(long period) {
        if (period == 0) {
            throw new ArgumentException("period: 0 (expected: != 0)");
        }
        return period;
    }

    internal ScheduledFutureTask<V> setId(long id) {
        if (this.id == 0L) {
            this.id = id;
        }
        return this;
    }

    protected IEventExecutor executor() {
        return base.executor();
    }

    public long deadlineNanos() {
        return _deadlineNanos;
    }

    internal void setConsumed() {
        // Optimization to avoid checking system clock again
        // after deadline has passed and task has been dequeued
        if (_periodNanos == 0)
        {
            Debug.Assert(scheduledExecutor().getCurrentTimeNanos() >= deadlineNanos);
            _deadlineNanos = 0L;
        }
    }

    public long delayNanos() {
        if (_deadlineNanos == 0L) {
            return 0L;
        }
        return delayNanos(scheduledExecutor().getCurrentTimeNanos());
    }

    static long deadlineToDelayNanos(long currentTimeNanos, long deadlineNanos) {
        return deadlineNanos == 0L ? 0L : Math.Max(0L, deadlineNanos - currentTimeNanos);
    }

    public long delayNanos(long currentTimeNanos) {
        return deadlineToDelayNanos(currentTimeNanos, _deadlineNanos);
    }

    public long getDelay(TimeSpan unit) {
        return unit.convert(delayNanos(), TimeSpan.NANOSECONDS);
    }

    public override int CompareTo(Delayed o) {
        if (this == o) {
            return 0;
        }

        ScheduledFutureTask<V> that = (ScheduledFutureTask<V>) o;
        long d = deadlineNanos() - that.deadlineNanos();
        if (d < 0) {
            return -1;
        } else if (d > 0) {
            return 1;
        } else if (id < that.id) {
            return -1;
        } else
        {
            Debug.Assert(id != that.id);
            return 1;
        }
    }

    public override void run()
    {
        Debug.Assert(executor().inEventLoop());
        try {
            if (delayNanos() > 0L) {
                // Not yet expired, need to add or remove from queue
                if (isCancelled()) {
                    scheduledExecutor().scheduledTaskQueue().removeTyped(this);
                } else {
                    scheduledExecutor().scheduleFromEventLoop(this);
                }
                return;
            }
            if (_periodNanos == 0) {
                if (setUncancellableInternal()) {
                    V result = runTask();
                    setSuccessInternal(result);
                }
            } else {
                // check if is done as it may was cancelled
                if (!isCancelled()) {
                    runTask();
                    if (!executor().isShutdown()) {
                        if (_periodNanos > 0) {
                            deadlineNanos += _periodNanos;
                        } else {
                            deadlineNanos = scheduledExecutor().getCurrentTimeNanos() - _periodNanos;
                        }
                        if (!isCancelled()) {
                            scheduledExecutor().scheduledTaskQueue().add(this);
                        }
                    }
                }
            }
        } catch (Exception cause) {
            setFailureInternal(cause);
        }
    }

    private AbstractScheduledEventExecutor scheduledExecutor()
    {
        return executor() as AbstractScheduledEventExecutor;
    }

    /**
     * {@inheritDoc}
     *
     * @param mayInterruptIfRunning this value has no effect in this implementation.
     */
    @Override
    public bool cancel(bool mayInterruptIfRunning) {
        bool canceled = super.cancel(mayInterruptIfRunning);
        if (canceled) {
            scheduledExecutor().removeScheduled(this);
        }
        return canceled;
    }

    bool cancelWithoutRemove(bool mayInterruptIfRunning) {
        return super.cancel(mayInterruptIfRunning);
    }

    @Override
    protected StringBuilder toStringBuilder() {
        StringBuilder buf = super.toStringBuilder();
        buf.setCharAt(buf.length() - 1, ',');

        return buf.append(" deadline: ")
                  .append(deadlineNanos)
                  .append(", period: ")
                  .append(_periodNanos)
                  .append(')');
    }

    @Override
    public int priorityQueueIndex(DefaultPriorityQueue<?> queue) {
        return queueIndex;
    }

    @Override
    public void priorityQueueIndex(DefaultPriorityQueue<?> queue, int i) {
        queueIndex = i;
    }
}
