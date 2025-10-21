/*
 * Copyright 2015 The Netty Project
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
using System.Diagnostics;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;

/**
 * Abstract base class for {@link IEventExecutor}s that want to support scheduling.
 */
public abstract class AbstractScheduledEventExecutor : AbstractEventExecutor
{
    private static readonly IComparer<IScheduledTask> SCHEDULED_FUTURE_TASK_COMPARATOR =
        Comparer<IScheduledTask>.Create((o1, o2) => o1.CompareTo(o2));

    protected static readonly IRunnable WAKEUP_TASK = EmptyRunnable.Shared;

    protected IPriorityQueue<IScheduledTask> _scheduledTaskQueue;

    private long nextTaskId;

    protected AbstractScheduledEventExecutor(IEventExecutorGroup parent)
        : base(parent)
    {
    }

    /**
     * Get the current time in nanoseconds by this executor's clock. This is not the same as {@link System#nanoTime()}
     * for two reasons:
     *
     * <ul>
     *     <li>We apply a fixed offset to the {@link System#nanoTime() nanoTime}</li>
     *     <li>Implementations (in particular EmbeddedEventLoop) may use their own time source so they can control time
     *     for testing purposes.</li>
     * </ul>
     *
     * @deprecated Please use (or override) {@link #ticker()} instead. This method delegates to {@link #ticker()}. Old
     * code may still call this method for compatibility.
     */
    public long getCurrentTimeNanos()
    {
        return ticker().nanoTime();
    }

    /**
     * @deprecated Use the non-static {@link #ticker()} instead.
     */
    protected static long nanoTime()
    {
        return Ticker.systemTicker().nanoTime();
    }

    /**
     * @deprecated Use the non-static {@link #ticker()} instead.
     */
    [Obsolete]
    static long defaultCurrentTimeNanos()
    {
        return Ticker.systemTicker().nanoTime();
    }

    internal static long deadlineNanos(long nanoTime, long delay)
    {
        long deadlineNanos = nanoTime + delay;
        // Guard against overflow
        return deadlineNanos < 0 ? long.MaxValue : deadlineNanos;
    }

    /**
     * Given an arbitrary deadline {@code deadlineNanos}, calculate the number of nano seconds from now
     * {@code deadlineNanos} would expire.
     * @param deadlineNanos An arbitrary deadline in nano seconds.
     * @return the number of nano seconds from now {@code deadlineNanos} would expire.
     * @deprecated Use {@link #ticker()} instead
     */
    [Obsolete]
    protected static long deadlineToDelayNanos(long deadlineNanos)
    {
        return ScheduledTask.deadlineToDelayNanos(defaultCurrentTimeNanos(), deadlineNanos);
    }

    /**
     * Returns the amount of time left until the scheduled task with the closest dead line is executed.
     */
    protected long delayNanos(long currentTimeNanos, long scheduledPurgeInterval)
    {
        currentTimeNanos -= ticker().initialNanoTime();

        IScheduledTask scheduledTask = peekScheduledTask();
        if (scheduledTask == null)
        {
            return scheduledPurgeInterval;
        }

        return scheduledTask.delayNanos(currentTimeNanos);
    }

    /**
     * The initial value used for delay and computations based upon a monatomic time source.
     * @return initial value used for delay and computations based upon a monatomic time source.
     * @deprecated Use {@link #ticker()} instead
     */
    [Obsolete]
    protected static long initialNanoTime()
    {
        return Ticker.systemTicker().initialNanoTime();
    }

    internal IPriorityQueue<IScheduledTask> scheduledTaskQueue()
    {
        if (_scheduledTaskQueue == null)
        {
            _scheduledTaskQueue = new DefaultPriorityQueue<IScheduledTask>(
                SCHEDULED_FUTURE_TASK_COMPARATOR,
                // Use same initial capacity as java.util.PriorityQueue
                11);
        }

        return _scheduledTaskQueue;
    }

    private static bool isNullOrEmpty(IQueue<IScheduledTask> queue)
    {
        return queue == null || queue.isEmpty();
    }

    /**
     * Cancel all scheduled tasks.
     *
     * This method MUST be called only when {@link #inEventLoop()} is {@code true}.
     */
    protected void cancelScheduledTasks()
    {
        Debug.Assert(inEventLoop());
        var scheduledTaskQueue = _scheduledTaskQueue;
        if (isNullOrEmpty(scheduledTaskQueue))
        {
            return;
        }

        IScheduledTask[] scheduledTasks = scheduledTaskQueue.toArray();

        foreach (IScheduledTask task in scheduledTasks)
        {
            task.cancelWithoutRemove(false);
        }

        scheduledTaskQueue.clearIgnoringIndexes();
    }

    /**
     * @see #pollScheduledTask(long)
     */
    protected IRunnable pollScheduledTask()
    {
        return pollScheduledTask(getCurrentTimeNanos());
    }

    /**
     * Fetch scheduled tasks from the internal queue and add these to the given {@link Queue}.
     *
     * @param taskQueue the task queue into which the fetched scheduled tasks should be transferred.
     * @return {@code true} if we were able to transfer everything, {@code false} if we need to call this method again
     *         as soon as there is space again in {@code taskQueue}.
     */
    protected bool fetchFromScheduledTaskQueue(IQueue<IRunnable> taskQueue)
    {
        Debug.Assert(inEventLoop());
        ObjectUtil.requireNonNull(taskQueue, "taskQueue");
        if (_scheduledTaskQueue == null || _scheduledTaskQueue.isEmpty())
        {
            return true;
        }

        long nanoTime = getCurrentTimeNanos();
        for (;;)
        {
            IRunnable scheduledTask = pollScheduledTask(nanoTime);
            if (scheduledTask == null)
            {
                return true;
            }

            if (!taskQueue.tryEnqueue(scheduledTask))
            {
                // No space left in the task queue add it back to the scheduledTaskQueue so we pick it up again.
                _scheduledTaskQueue.tryEnqueue((IScheduledTask)scheduledTask);
                return false;
            }
        }
    }

    /**
     * Return the {@link IRunnable} which is ready to be executed with the given {@code nanoTime}.
     * You should use {@link #getCurrentTimeNanos()} to retrieve the correct {@code nanoTime}.
     */
    protected IRunnable pollScheduledTask(long nanoTime)
    {
        Debug.Assert(inEventLoop());

        IScheduledTask scheduledTask = peekScheduledTask();
        if (scheduledTask == null || scheduledTask.deadlineNanos() - nanoTime > 0)
        {
            return null;
        }

        _scheduledTaskQueue.tryDequeue(out _);
        scheduledTask.setConsumed();
        return scheduledTask;
    }

    /**
     * Return the nanoseconds until the next scheduled task is ready to be run or {@code -1} if no task is scheduled.
     */
    protected long nextScheduledTaskNano()
    {
        IScheduledTask scheduledTask = peekScheduledTask();
        return scheduledTask != null ? scheduledTask.delayNanos() : -1;
    }

    /**
     * Return the deadline (in nanoseconds) when the next scheduled task is ready to be run or {@code -1}
     * if no task is scheduled.
     */
    protected long nextScheduledTaskDeadlineNanos()
    {
        IScheduledTask scheduledTask = peekScheduledTask();
        return scheduledTask != null ? scheduledTask.deadlineNanos() : -1;
    }

    protected IScheduledTask peekScheduledTask()
    {
        var scheduledTaskQueue = _scheduledTaskQueue;
        IScheduledTask task = null;
        var peek = scheduledTaskQueue?.tryPeek(out task) ?? false;
        return peek ? task : null;
    }

    /**
     * Returns {@code true} if a scheduled task is ready for processing.
     */
    protected bool hasScheduledTasks()
    {
        var scheduledTask = peekScheduledTask();
        return scheduledTask != null && scheduledTask.deadlineNanos() <= getCurrentTimeNanos();
    }

    public override IScheduledTask schedule(IRunnable command, TimeSpan delay)
    {
        ObjectUtil.checkNotNull(command, "command");
        //ObjectUtil.checkNotNull(unit, "unit");
        if (delay.Ticks < 0)
        {
            delay = TimeSpan.Zero;
        }

        validateScheduled0(delay);

        return schedule(new ScheduledRunnableTask(
            this,
            command,
            deadlineNanos(getCurrentTimeNanos(), (long)delay.TotalNanoseconds))
        );
    }

    public override IScheduledTask<V> schedule<V>(ICallable<V> callable, TimeSpan delay)
    {
        ObjectUtil.checkNotNull(callable, "callable");
        //ObjectUtil.checkNotNull(unit, "unit");
        if (delay.Ticks < 0)
        {
            delay = TimeSpan.Zero;
            ;
        }

        validateScheduled0(delay);

        return schedule(new ScheduledCallableTask<V>(
            this, callable, deadlineNanos(getCurrentTimeNanos(), (long)delay.TotalNanoseconds)));
    }

    public override IScheduledTask scheduleAtFixedRate(IRunnable command, TimeSpan initialDelay, TimeSpan period)
    {
        ObjectUtil.checkNotNull(command, "command");
        //ObjectUtil.checkNotNull(unit, "unit");
        var initialDelayNanos = (long)initialDelay.TotalNanoseconds;
        var periodNanos = (long)period.TotalNanoseconds;
        if (initialDelayNanos < 0)
        {
            throw new ArgumentException($"initialDelay: {initialDelayNanos} (expected: >= 0)");
        }

        if (periodNanos <= 0)
        {
            throw new ArgumentException($"period: {periodNanos} (expected: > 0)");
        }

        validateScheduled0(initialDelay);
        validateScheduled0(period);

        return schedule(new ScheduledRunnableTask(
            this, command, deadlineNanos(getCurrentTimeNanos(), initialDelayNanos), periodNanos));
    }

    public override IScheduledTask scheduleWithFixedDelay(IRunnable command, TimeSpan initialDelay, TimeSpan delay)
    {
        ObjectUtil.checkNotNull(command, "command");
        //ObjectUtil.checkNotNull(unit, "unit");
        var initialDelayNanos = (long)initialDelay.TotalNanoseconds;
        var delayNanos = (long)delay.TotalNanoseconds;
        if (initialDelayNanos < 0)
        {
            throw new ArgumentException($"initialDelay: {initialDelay} (expected: >= 0)");
        }

        if (delayNanos <= 0)
        {
            throw new ArgumentException($"delay: {delayNanos} (expected: > 0)");
        }

        validateScheduled0(initialDelay);
        validateScheduled0(delay);

        return schedule(new ScheduledRunnableTask(
            this, command, deadlineNanos(getCurrentTimeNanos(), initialDelayNanos), -delayNanos));
    }

    //@SuppressWarnings("deprecation")
    private void validateScheduled0(TimeSpan amount)
    {
        validateScheduled(amount);
    }

    /**
     * Sub-classes may override this to restrict the maximal amount of time someone can use to schedule a task.
     *
     * @deprecated will be removed in the future.
     */
    [Obsolete]
    protected void validateScheduled(TimeSpan amount)
    {
        // NOOP
    }

    internal void scheduleFromEventLoop(IScheduledTask task)
    {
        // nextTaskId a long and so there is no chance it will overflow back to 0
        scheduledTaskQueue().tryEnqueue(task.setId(++nextTaskId));
    }

    private IScheduledTask<T> schedule<T>(IScheduledTask<T> task)
    {
        if (inEventLoop())
        {
            scheduleFromEventLoop(task);
        }
        else
        {
            long deadlineNanos = task.deadlineNanos();
            // task will add itself to scheduled task queue when run if not expired
            if (beforeScheduledTaskSubmitted(deadlineNanos))
            {
                execute(task);
            }
            else
            {
                lazyExecute(task);
                // Second hook after scheduling to facilitate race-avoidance
                if (afterScheduledTaskSubmitted(deadlineNanos))
                {
                    execute(WAKEUP_TASK);
                }
            }
        }

        return task;
    }

    public void removeScheduled(IScheduledTask task)
    {
        Debug.Assert(task.isCancelled());
        if (inEventLoop())
        {
            scheduledTaskQueue().tryRemove(task);
        }
        else
        {
            // task will remove itself from scheduled task queue when it runs
            scheduleRemoveScheduled(task);
        }
    }

    protected virtual void scheduleRemoveScheduled(IScheduledTask task)
    {
        // task will remove itself from scheduled task queue when it runs
        lazyExecute(task);
    }

    /**
     * Called from arbitrary non-{@link IEventExecutor} threads prior to scheduled task submission.
     * Returns {@code true} if the {@link IEventExecutor} thread should be woken immediately to
     * process the scheduled task (if not already awake).
     * <p>
     * If {@code false} is returned, {@link #afterScheduledTaskSubmitted(long)} will be called with
     * the same value <i>after</i> the scheduled task is enqueued, providing another opportunity
     * to wake the {@link IEventExecutor} thread if required.
     *
     * @param deadlineNanos deadline of the to-be-scheduled task
     *     relative to {@link AbstractScheduledEventExecutor#getCurrentTimeNanos()}
     * @return {@code true} if the {@link IEventExecutor} thread should be woken, {@code false} otherwise
     */
    protected bool beforeScheduledTaskSubmitted(long deadlineNanos)
    {
        return true;
    }

    /**
     * See {@link #beforeScheduledTaskSubmitted(long)}. Called only after that method returns false.
     *
     * @param deadlineNanos relative to {@link AbstractScheduledEventExecutor#getCurrentTimeNanos()}
     * @return  {@code true} if the {@link IEventExecutor} thread should be woken, {@code false} otherwise
     */
    protected bool afterScheduledTaskSubmitted(long deadlineNanos)
    {
        return true;
    }
}