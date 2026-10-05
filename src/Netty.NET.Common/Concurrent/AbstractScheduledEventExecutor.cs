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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;

/**
 * Abstract base class for {@link EventExecutor}s that want to support scheduling.
 */
public abstract class AbstractScheduledEventExecutor : AbstractEventExecutor
{
    private static readonly IComparer<IScheduledWork> SCHEDULED_FUTURE_TASK_COMPARATOR =
        Comparer<IScheduledWork>.Create((o1, o2) =>
        {
            if (ReferenceEquals(o1, o2)) return 0;
            long delta = unchecked(o1.DeadlineNanos() - o2.DeadlineNanos());
            return delta == 0 ? o1.GetId().CompareTo(o2.GetId()) : delta < 0 ? -1 : 1;
        });

    protected static readonly IRunnable WAKEUP_TASK = Runnables.Empty; // Do nothing

    protected IPriorityQueue<IScheduledWork> _scheduledTaskQueue;

    private long nextTaskId;
    private readonly Ticker selectedTicker;
    // Bind these to the owner once; calls still observe virtual clock and shutdown
    // state at invocation time. Every native scheduled membership shares them.
    private readonly Func<long> nativeClock;
    private readonly Func<bool> nativeCanRun;
    private readonly Action<ITaskScheduledWork> nativeEnqueue;
    private readonly Action<ITaskScheduledWork> nativeRemove;

    protected AbstractScheduledEventExecutor() : this(null)
    {
    }

    protected AbstractScheduledEventExecutor(IEventExecutorGroup parent)
        : this(parent, TimeProvider.System)
    {
    }

    // CLR: native timestamp injection retains this executor's dispatch/queue policy.
    protected AbstractScheduledEventExecutor(IEventExecutorGroup parent, TimeProvider timeProvider)
        : base(parent)
    {
        selectedTicker = global::Netty.NET.Common.Concurrent.Ticker.FromTimeProvider(timeProvider);
        nativeClock = GetCurrentTimeNanos;
        nativeCanRun = () => !IsShutdown();
        nativeEnqueue = EnqueueNative;
        nativeRemove = RemoveNative;
    }

    public override Ticker Ticker() => selectedTicker;

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
    [Obsolete]
    public virtual long GetCurrentTimeNanos()
    {
        return Ticker().NanoTime();
    }

    /**
     * @deprecated Use the non-static {@link #ticker()} instead.
     */
    [Obsolete]
    protected static long NanoTime()
    {
        return global::Netty.NET.Common.Concurrent.Ticker.SystemTicker().NanoTime();
    }

    /**
     * @deprecated Use the non-static {@link #ticker()} instead.
     */
    [Obsolete]
    internal static long DefaultCurrentTimeNanos()
    {
        return global::Netty.NET.Common.Concurrent.Ticker.SystemTicker().NanoTime();
    }

    internal static long DeadlineNanos(long nanoTime, long delay)
    {
        // Java long addition wraps before the existing saturation check.
        long deadlineNanos = unchecked(nanoTime + delay);
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
    protected static long DeadlineToDelayNanos(long deadlineNanos)
    {
        return DeadlineToDelayNanos(DefaultCurrentTimeNanos(), deadlineNanos);
    }

    /**
     * Returns the amount of time left until the scheduled task with the closest dead line is executed.
     */
    protected long DelayNanos(long currentTimeNanos, long scheduledPurgeInterval)
    {
        currentTimeNanos -= Ticker().InitialNanoTime();

        IScheduledWork scheduledTask = PeekScheduledTask();
        if (scheduledTask == null)
        {
            return scheduledPurgeInterval;
        }

        return scheduledTask.DelayNanos(currentTimeNanos);
    }

    /**
     * The initial value used for delay and computations based upon a monatomic time source.
     * @return initial value used for delay and computations based upon a monatomic time source.
     * @deprecated Use {@link #ticker()} instead
     */
    [Obsolete]
    protected static long InitialNanoTime()
    {
        return global::Netty.NET.Common.Concurrent.Ticker.SystemTicker().InitialNanoTime();
    }

    internal IPriorityQueue<IScheduledWork> ScheduledTaskQueue()
    {
        if (_scheduledTaskQueue == null)
        {
            _scheduledTaskQueue = new DefaultPriorityQueue<IScheduledWork>(
                SCHEDULED_FUTURE_TASK_COMPARATOR,
                // Use same initial capacity as java.util.PriorityQueue
                11);
        }

        return _scheduledTaskQueue;
    }

    private static bool IsNullOrEmpty(IQueue<IScheduledWork> queue)
    {
        return queue == null || queue.IsEmpty();
    }

    /**
     * Cancel all scheduled tasks.
     *
     * This method MUST be called only when {@link #inEventLoop()} is {@code true}.
     */
    protected virtual void CancelScheduledTasks()
    {
        Debug.Assert(InEventLoop());
        var scheduledTaskQueue = _scheduledTaskQueue;
        if (IsNullOrEmpty(scheduledTaskQueue))
        {
            return;
        }

        IScheduledWork[] scheduledTasks = scheduledTaskQueue.ToArray();

        foreach (IScheduledWork task in scheduledTasks)
        {
            task.CancelForShutdown();
        }

        // CLR: A terminated executor may remain reachable. Release queued work
        // and its membership instead of assuming this queue is about to be GC'd.
        scheduledTaskQueue.Clear();
    }

    /**
     * @see #pollScheduledTask(long)
     */
    protected internal IRunnable PollScheduledTask()
    {
        return PollScheduledTask(GetCurrentTimeNanos());
    }

    /**
     * Fetch scheduled tasks from the internal queue and add these to the given {@link Queue}.
     *
     * @param taskQueue the task queue into which the fetched scheduled tasks should be transferred.
     * @return {@code true} if we were able to transfer everything, {@code false} if we need to call this method again
     *         as soon as there is space again in {@code taskQueue}.
     */
    protected virtual bool FetchFromScheduledTaskQueue(IQueue<Action> taskQueue)
    {
        Debug.Assert(InEventLoop());
        ObjectUtil.RequireNonNull(taskQueue, "taskQueue");
        if (_scheduledTaskQueue == null || _scheduledTaskQueue.IsEmpty())
        {
            return true;
        }

        long nanoTime = GetCurrentTimeNanos();
        for (;;)
        {
            IScheduledWork scheduledTask = PollScheduledTask(nanoTime);
            if (scheduledTask == null)
            {
                return true;
            }

            if (scheduledTask.IsCanceled)
            {
                continue;
            }

            if (!taskQueue.TryEnqueue(ExecutorWork.Wrap(scheduledTask)))
            {
                // No space left in the task queue add it back to the scheduledTaskQueue so we pick it up again.
                _scheduledTaskQueue.TryEnqueue(scheduledTask);
                return false;
            }
        }
    }

    /**
     * Return the {@link Runnable} which is ready to be executed with the given {@code nanoTime}.
     * You should use {@link #getCurrentTimeNanos()} to retrieve the correct {@code nanoTime}.
     */
    protected IScheduledWork PollScheduledTask(long nanoTime)
    {
        Debug.Assert(InEventLoop());

        IScheduledWork scheduledTask = PeekScheduledTask();
        if (scheduledTask == null || scheduledTask.DeadlineNanos() - nanoTime > 0)
        {
            return null;
        }

        _scheduledTaskQueue.TryDequeue(out _);
        scheduledTask.SetConsumed();
        return scheduledTask;
    }

    /**
     * Return the nanoseconds until the next scheduled task is ready to be run or {@code -1} if no task is scheduled.
     */
    internal static long DeadlineToDelayNanos(long now, long deadline) =>
        deadline == 0L ? 0L : Math.Max(0L, deadline - now);

    protected long NextScheduledTaskNano()
    {
        IScheduledWork scheduledTask = PeekScheduledTask();
        return scheduledTask != null ? scheduledTask.DelayNanos() : -1;
    }

    /**
     * Return the deadline (in nanoseconds) when the next scheduled task is ready to be run or {@code -1}
     * if no task is scheduled.
     */
    protected long NextScheduledTaskDeadlineNanos()
    {
        IScheduledWork scheduledTask = PeekScheduledTask();
        return scheduledTask != null ? scheduledTask.DeadlineNanos() : -1;
    }

    protected IScheduledWork PeekScheduledTask()
    {
        var scheduledTaskQueue = _scheduledTaskQueue;
        IScheduledWork task = null;
        var peek = scheduledTaskQueue?.TryPeek(out task) ?? false;
        return peek ? task : null;
    }

    /**
     * Returns {@code true} if a scheduled task is ready for processing.
     */
    protected bool HasScheduledTasks()
    {
        var scheduledTask = PeekScheduledTask();
        return scheduledTask != null && scheduledTask.DeadlineNanos() <= GetCurrentTimeNanos();
    }

    //@SuppressWarnings("deprecation")
    private void ValidateScheduled0(TimeSpan amount)
    {
        ValidateScheduled(amount);
    }

    // CLR: TimeSpan stores 100 ns ticks. Saturate like Java TimeUnit.toNanos,
    // retaining integer precision and avoiding a floating-point overflow cast.
    internal static long ToNanos(TimeSpan amount) => TimeUtil.ToNanoseconds(amount);

    /**
     * Sub-classes may override this to restrict the maximal amount of time someone can use to schedule a task.
     *
     * @deprecated will be removed in the future.
     */
    [Obsolete]
    protected virtual void ValidateScheduled(TimeSpan amount)
    {
        // NOOP
    }

    internal void ScheduleFromEventLoop(IScheduledWork task)
    {
        // nextTaskId a long and so there is no chance it will overflow back to 0
        if (task.GetId() == 0L)
        {
            task.AssignId(++nextTaskId);
        }
        ScheduledTaskQueue().TryEnqueue(task);
    }

    internal Task<T> ScheduleNative<T>(Func<CancellationToken, T> function, TimeSpan delay, long period,
        CancellationToken token, bool captureContext = true)
    {
        ValidateScheduled0(delay);
        if (period != 0) ValidateScheduled0(TimeSpan.FromTicks(Math.Abs(period) / 100));
        if (token.IsCancellationRequested) return Task.FromCanceled<T>(token);
        var task = new NativeScheduledWork<T>(function, token,
            DeadlineNanos(GetCurrentTimeNanos(), ToNanos(delay)), period, nativeClock,
            nativeCanRun, nativeEnqueue, nativeRemove, captureContext);
        try { if (!task.Completion.IsCompleted) SubmitScheduled(task); }
        catch (Exception error) { task.Reject(error); }
        task.Publish();
        return task.ResultTask;
    }

    private void EnqueueNative(ITaskScheduledWork task)
    {
        if (task.Completion.IsCompleted) return;
        if (IsShutdown()) task.CancelForShutdown();
        else ScheduleFromEventLoop(task);
    }

    private void RemoveNative(ITaskScheduledWork task)
    {
        if (IsTerminated()) return;
        try { RemoveScheduled(task); }
        catch (RejectedExecutionException) when (IsShuttingDown()) { }
    }

    private void SubmitScheduled(IScheduledWork task)
    {
        if (InEventLoop())
        {
            ScheduleFromEventLoop(task);
        }
        else
        {
            long deadlineNanos = task.DeadlineNanos();
            // task will add itself to scheduled task queue when run if not expired
            if (BeforeScheduledTaskSubmitted(deadlineNanos))
            {
                Execute(task);
            }
            else
            {
                LazyExecute(task);
                // Second hook after scheduling to facilitate race-avoidance
                if (AfterScheduledTaskSubmitted(deadlineNanos))
                {
                    Execute(WAKEUP_TASK);
                }
            }
        }

    }

    public void RemoveScheduled(IScheduledWork task)
    {
        Debug.Assert(task.IsCanceled);
        if (InEventLoop())
        {
            ScheduledTaskQueue().TryRemove(task);
        }
        else
        {
            // task will remove itself from scheduled task queue when it runs
            ScheduleRemoveScheduled(task);
        }
    }

    protected virtual void ScheduleRemoveScheduled(IScheduledWork task)
    {
        // task will remove itself from scheduled task queue when it runs
        LazyExecute(task);
    }

    /**
     * Called from arbitrary non-{@link EventExecutor} threads prior to scheduled task submission.
     * Returns {@code true} if the {@link EventExecutor} thread should be woken immediately to
     * process the scheduled task (if not already awake).
     * <p>
     * If {@code false} is returned, {@link #afterScheduledTaskSubmitted(long)} will be called with
     * the same value <i>after</i> the scheduled task is enqueued, providing another opportunity
     * to wake the {@link EventExecutor} thread if required.
     *
     * @param deadlineNanos deadline of the to-be-scheduled task
     *     relative to {@link AbstractScheduledEventExecutor#getCurrentTimeNanos()}
     * @return {@code true} if the {@link EventExecutor} thread should be woken, {@code false} otherwise
     */
    protected virtual bool BeforeScheduledTaskSubmitted(long deadlineNanos)
    {
        return true;
    }

    /**
     * See {@link #beforeScheduledTaskSubmitted(long)}. Called only after that method returns false.
     *
     * @param deadlineNanos relative to {@link AbstractScheduledEventExecutor#getCurrentTimeNanos()}
     * @return  {@code true} if the {@link EventExecutor} thread should be woken, {@code false} otherwise
     */
    protected virtual bool AfterScheduledTaskSubmitted(long deadlineNanos)
    {
        return true;
    }
}
