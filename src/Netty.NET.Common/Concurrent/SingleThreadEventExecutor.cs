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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

/**
 * Abstract base class for {@link OrderedEventExecutor}'s that execute all its submitted tasks in a single thread.
 *
 */
public abstract class SingleThreadEventExecutor : AbstractScheduledEventExecutor, IOrderedEventExecutor
{
    public static readonly int DEFAULT_MAX_PENDING_EXECUTOR_TASKS = Math.Max(16,
        SystemPropertyUtil.GetInt("io.netty.eventexecutor.maxPendingTasks", int.MaxValue));

    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(SingleThreadEventExecutor));

    private const int ST_NOT_STARTED = 1;
    private const int ST_SUSPENDING = 2;
    private const int ST_SUSPENDED = 3;
    private const int ST_STARTED = 4;
    private const int ST_SHUTTING_DOWN = 5;
    private const int ST_SHUTDOWN = 6;
    private const int ST_TERMINATED = 7;

    private static readonly IRunnable NOOP_TASK = Runnables.Empty; // Do nothing.

    private readonly IQueue<IRunnable> _taskQueue;

    private volatile Thread _thread;
    private readonly AtomicReference<IThreadProperties> _threadProperties = new AtomicReference<IThreadProperties>();
    private readonly IExecutor _executor;
    private volatile bool interrupted;

    private readonly object _processingLock = new object();
    private readonly CountdownEvent _threadLock = new CountdownEvent(1);
    private readonly LinkedHashSet<IRunnable> _shutdownHooks = new LinkedHashSet<IRunnable>();
    private readonly bool _addTaskWakesUp;
    private readonly int _maxPendingTasks;
    private readonly IRejectedExecutionHandler _rejectedExecutionHandler;
    private readonly bool _supportSuspension;

    // A running total of nanoseconds this executor has spent in an "active" state.
    private long accumulatedActiveTimeNanos;
    // Timestamp of the last recorded activity (tasks + I/O).
    private long lastActivityTimeNanos;
    /**
     * Tracks the number of consecutive monitor cycles this executor's
     * utilization has been below the scale-down threshold.
     */
    private int consecutiveIdleCycles;

    /**
     * Tracks the number of consecutive monitor cycles this executor's
     * utilization has been above the scale-up threshold.
     */
    private int consecutiveBusyCycles;

    private long lastExecutionTime;

    private readonly AtomicInteger _state = new AtomicInteger(ST_NOT_STARTED);

    private readonly AtomicLong _gracefulShutdownQuietPeriod = new AtomicLong();
    private readonly AtomicLong _gracefulShutdownTimeout = new AtomicLong();
    private long gracefulShutdownStartTime;

    private readonly TaskCompletionSource _terminationSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /**
     * Create a new instance
     *
     * @param parent            the {@link EventExecutorGroup} which is the parent of this instance and belongs to it
     * @param threadFactory     the {@link ThreadFactory} which will be used for the used {@link Thread}
     * @param addTaskWakesUp    {@code true} if and only if invocation of {@link #addTask(Runnable)} will wake up the
     *                          executor thread
     */
    protected SingleThreadEventExecutor(
        IEventExecutorGroup parent, IThreadFactory threadFactory, bool addTaskWakesUp)
        : this(parent, new ThreadPerTaskExecutor(threadFactory), addTaskWakesUp)
    {
    }

    /**
     * Create a new instance
     *
     * @param parent            the {@link EventExecutorGroup} which is the parent of this instance and belongs to it
     * @param threadFactory     the {@link ThreadFactory} which will be used for the used {@link Thread}
     * @param addTaskWakesUp    {@code true} if and only if invocation of {@link #addTask(Runnable)} will wake up the
     *                          executor thread
     * @param maxPendingTasks   the maximum number of pending tasks before new tasks will be rejected.
     * @param rejectedHandler   the {@link RejectedExecutionHandler} to use.
     */
    protected SingleThreadEventExecutor(
        IEventExecutorGroup parent, IThreadFactory threadFactory,
        bool addTaskWakesUp, int maxPendingTasks, IRejectedExecutionHandler rejectedHandler)
        : this(parent, new ThreadPerTaskExecutor(threadFactory), addTaskWakesUp, maxPendingTasks, rejectedHandler)
    {
    }

    /**
     * Create a new instance
     *
     * @param parent            the {@link EventExecutorGroup} which is the parent of this instance and belongs to it
     * @param threadFactory     the {@link ThreadFactory} which will be used for the used {@link Thread}
     * @param addTaskWakesUp    {@code true} if and only if invocation of {@link #addTask(Runnable)} will wake up the
     *                          executor thread
     * @param supportSuspension {@code true} if suspension of this {@link SingleThreadEventExecutor} is supported.
     * @param maxPendingTasks   the maximum number of pending tasks before new tasks will be rejected.
     * @param rejectedHandler   the {@link RejectedExecutionHandler} to use.
     */
    protected SingleThreadEventExecutor(
        IEventExecutorGroup parent, IThreadFactory threadFactory,
        bool addTaskWakesUp, bool supportSuspension,
        int maxPendingTasks, IRejectedExecutionHandler rejectedHandler)
        : this(parent, new ThreadPerTaskExecutor(threadFactory), addTaskWakesUp, supportSuspension,
            maxPendingTasks, rejectedHandler)
    {
    }

    /**
     * Create a new instance
     *
     * @param parent            the {@link EventExecutorGroup} which is the parent of this instance and belongs to it
     * @param executor          the {@link Executor} which will be used for executing
     * @param addTaskWakesUp    {@code true} if and only if invocation of {@link #addTask(Runnable)} will wake up the
     *                          executor thread
     */
    protected SingleThreadEventExecutor(IEventExecutorGroup parent, IExecutor executor, bool addTaskWakesUp)
        : this(parent, executor, addTaskWakesUp, DEFAULT_MAX_PENDING_EXECUTOR_TASKS, RejectedExecutionHandlers.Reject())
    {
    }

    /**
     * Create a new instance
     *
     * @param parent            the {@link EventExecutorGroup} which is the parent of this instance and belongs to it
     * @param executor          the {@link Executor} which will be used for executing
     * @param addTaskWakesUp    {@code true} if and only if invocation of {@link #addTask(Runnable)} will wake up the
     *                          executor thread
     * @param maxPendingTasks   the maximum number of pending tasks before new tasks will be rejected.
     * @param rejectedHandler   the {@link RejectedExecutionHandler} to use.
     */
    protected SingleThreadEventExecutor(IEventExecutorGroup parent, IExecutor executor,
        bool addTaskWakesUp, int maxPendingTasks,
        IRejectedExecutionHandler rejectedHandler)
        : this(parent, executor, addTaskWakesUp, false, maxPendingTasks, rejectedHandler)
    {
    }

    /**
     * Create a new instance
     *
     * @param parent            the {@link EventExecutorGroup} which is the parent of this instance and belongs to it
     * @param executor          the {@link Executor} which will be used for executing
     * @param addTaskWakesUp    {@code true} if and only if invocation of {@link #addTask(Runnable)} will wake up the
     *                          executor thread
     * @param supportSuspension {@code true} if suspension of this {@link SingleThreadEventExecutor} is supported.
     * @param maxPendingTasks   the maximum number of pending tasks before new tasks will be rejected.
     * @param rejectedHandler   the {@link RejectedExecutionHandler} to use.
     */
    protected SingleThreadEventExecutor(IEventExecutorGroup parent, IExecutor executor,
        bool addTaskWakesUp, bool supportSuspension,
        int maxPendingTasks, IRejectedExecutionHandler rejectedHandler)
        : this(parent, executor, addTaskWakesUp, supportSuspension, maxPendingTasks, rejectedHandler, TimeProvider.System)
    {
    }

    protected SingleThreadEventExecutor(IEventExecutorGroup parent, IExecutor executor,
        bool addTaskWakesUp, bool supportSuspension,
        int maxPendingTasks, IRejectedExecutionHandler rejectedHandler, TimeProvider timeProvider)
        : base(parent, timeProvider)
    {
        _addTaskWakesUp = addTaskWakesUp;
        _supportSuspension = supportSuspension;
        _maxPendingTasks = Math.Max(16, maxPendingTasks);
        _executor = ThreadExecutorMap.Apply(executor, this);
        _taskQueue = NewTaskQueue(_maxPendingTasks);
        _rejectedExecutionHandler = ObjectUtil.CheckNotNull(rejectedHandler, "rejectedHandler");
        lastActivityTimeNanos = Ticker().NanoTime();
    }

    protected SingleThreadEventExecutor(IEventExecutorGroup parent, IExecutor executor,
        bool addTaskWakesUp, IQueue<IRunnable> taskQueue,
        IRejectedExecutionHandler rejectedHandler)
        : this(parent, executor, addTaskWakesUp, false, taskQueue, rejectedHandler)
    {
    }

    protected SingleThreadEventExecutor(IEventExecutorGroup parent, IExecutor executor,
        bool addTaskWakesUp, bool supportSuspension,
        IQueue<IRunnable> taskQueue, IRejectedExecutionHandler rejectedHandler)
        : base(parent)
    {
        _addTaskWakesUp = addTaskWakesUp;
        _supportSuspension = supportSuspension;
        _maxPendingTasks = DEFAULT_MAX_PENDING_EXECUTOR_TASKS;
        _executor = ThreadExecutorMap.Apply(executor, this);
        _taskQueue = ObjectUtil.CheckNotNull(taskQueue, "taskQueue");
        _rejectedExecutionHandler = ObjectUtil.CheckNotNull(rejectedHandler, "rejectedHandler");
    }

    /**
     * @deprecated Please use and override {@link #newTaskQueue(int)}.
     */
    [Obsolete]
    protected virtual IQueue<IRunnable> NewTaskQueue()
    {
        return NewTaskQueue(_maxPendingTasks);
    }

    /**
     * Create a new {@link Queue} which will holds the tasks to execute. This default implementation will return a
     * {@link LinkedBlockingQueue} but if your sub-class of {@link SingleThreadEventExecutor} will not do any blocking
     * calls on the this {@link Queue} it may make sense to {@code @Override} this and return some more performant
     * implementation that does not support blocking operations at all.
     */
    protected virtual IQueue<IRunnable> NewTaskQueue(int maxPendingTasks)
    {
        return new LinkedBlockingQueue<IRunnable>(maxPendingTasks);
    }

    /**
     * Interrupt the current running {@link Thread}.
     */
    protected virtual void InterruptThread()
    {
        Thread currentThread = _thread;
        if (currentThread == null)
        {
            interrupted = true;
        }
        else
        {
            currentThread.Interrupt();
        }
    }

    /**
     * @see Queue#poll()
     */
    protected virtual IRunnable PollTask()
    {
        Debug.Assert(InEventLoop());
        return PollTaskFrom(_taskQueue);
    }

    protected static IRunnable PollTaskFrom(IQueue<IRunnable> taskQueue)
    {
        for (;;)
        {
            taskQueue.TryDequeue(out var task);
            if (task != WAKEUP_TASK)
            {
                return task;
            }
        }
    }

    /**
     * Take the next {@link Runnable} from the task queue and so will block if no task is currently present.
     * <p>
     * Be aware that this method will throw an {@link UnsupportedOperationException} if the task queue, which was
     * created via {@link #newTaskQueue()}, does not implement {@link BlockingQueue}.
     * </p>
     *
     * @return {@code null} if the executor thread has been interrupted or waken up.
     */
    protected virtual IRunnable TakeTask()
    {
        Debug.Assert(InEventLoop());
        if (!(_taskQueue is IBlockingQueue<IRunnable>))
        {
            throw new NotSupportedException();
        }

        IBlockingQueue<IRunnable> taskQueue = (IBlockingQueue<IRunnable>)_taskQueue;
        for (;;)
        {
            IScheduledWork scheduledTask = PeekScheduledTask();
            if (scheduledTask == null)
            {
                IRunnable task = null;
                try
                {
                    task = taskQueue.Take();
                    if (task == WAKEUP_TASK)
                    {
                        task = null;
                    }
                }
                catch (ThreadInterruptedException e)
                {
                    // Ignore
                }

                return task;
            }
            else
            {
                long delayNanos = scheduledTask.DelayNanos();
                IRunnable task = null;
                if (delayNanos > 0)
                {
                    try
                    {
                        var delayTs = TimeSpan.FromTicks(delayNanos / 100);
                        taskQueue.TryTake(out task, delayTs);
                    }
                    catch (ThreadInterruptedException e)
                    {
                        // Waken up.
                        return null;
                    }
                }

                if (task == null)
                {
                    // We need to fetch the scheduled tasks now as otherwise there may be a chance that
                    // scheduled tasks are never executed if there is always one task in the taskQueue.
                    // This is for example true for the read task of OIO Transport
                    // See https://github.com/netty/netty/issues/1614
                    FetchFromScheduledTaskQueue();
                    taskQueue.TryTake(out task);
                }

                if (task != null)
                {
                    if (task == WAKEUP_TASK)
                    {
                        return null;
                    }

                    return task;
                }
            }
        }
    }

    private bool FetchFromScheduledTaskQueue()
    {
        return FetchFromScheduledTaskQueue(_taskQueue);
    }

    /**
     * @return {@code true} if at least one scheduled task was executed.
     */
    private bool ExecuteExpiredScheduledTasks()
    {
        if (_scheduledTaskQueue == null || _scheduledTaskQueue.IsEmpty())
        {
            return false;
        }

        long nanoTime = GetCurrentTimeNanos();
        IRunnable scheduledTask = PollScheduledTask(nanoTime);
        if (scheduledTask == null)
        {
            return false;
        }

        do
        {
            SafeExecute(scheduledTask);
        } while ((scheduledTask = PollScheduledTask(nanoTime)) != null);

        return true;
    }

    /**
     * @see Queue#peek()
     */
    protected virtual IRunnable PeekTask()
    {
        Debug.Assert(InEventLoop());
        return _taskQueue.TryPeek(out var task) ? task : null;
    }

    /**
     * @see Queue#isEmpty()
     */
    protected virtual bool HasTasks()
    {
        Debug.Assert(InEventLoop());
        return !_taskQueue.IsEmpty();
    }

    /**
     * Return the number of tasks that are pending for processing.
     */
    public int PendingTasks()
    {
        return _taskQueue.Count;
    }

    /**
     * Add a task to the task queue, or throws a {@link RejectedExecutionException} if this instance was shutdown
     * before.
     */
    protected virtual void AddTask(IRunnable task)
    {
        ObjectUtil.CheckNotNull(task, "task");
        if (!OfferTask(task))
        {
            Reject(task);
        }
    }

    public bool OfferTask(IRunnable task)
    {
        if (IsShutdown())
        {
            Reject();
        }

        return _taskQueue.TryEnqueue(task);
    }

    /**
     * @see Queue#remove(Object)
     */
    protected virtual bool RemoveTask(IRunnable task)
    {
        return _taskQueue.TryRemove(ObjectUtil.CheckNotNull(task, "task"));
    }

    /**
     * Poll all tasks from the task queue and run them via {@link Runnable#run()} method.
     *
     * @return {@code true} if and only if at least one task was run
     */
    protected virtual bool RunAllTasks()
    {
        Debug.Assert(InEventLoop());
        bool fetchedAll;
        bool ranAtLeastOne = false;

        do
        {
            fetchedAll = FetchFromScheduledTaskQueue(_taskQueue);
            if (RunAllTasksFrom(_taskQueue))
            {
                ranAtLeastOne = true;
            }
        } while (!fetchedAll); // keep on processing until we fetched all scheduled tasks.

        if (ranAtLeastOne)
        {
            lastExecutionTime = GetCurrentTimeNanos();
        }

        AfterRunningAllTasks();
        return ranAtLeastOne;
    }

    /**
     * Execute all expired scheduled tasks and all current tasks in the executor queue until both queues are empty,
     * or {@code maxDrainAttempts} has been exceeded.
     * @param maxDrainAttempts The maximum amount of times this method attempts to drain from queues. This is to prevent
     *                         continuous task execution and scheduling from preventing the EventExecutor thread to
     *                         make progress and return to the selector mechanism to process inbound I/O events.
     * @return {@code true} if at least one task was run.
     */
    protected bool RunScheduledAndExecutorTasks(int maxDrainAttempts)
    {
        Debug.Assert(InEventLoop());
        bool ranAtLeastOneTask;
        int drainAttempt = 0;
        do
        {
            // We must run the taskQueue tasks first, because the scheduled tasks from outside the EventLoop are queued
            // here because the taskQueue is thread safe and the scheduledTaskQueue is not thread safe.
            ranAtLeastOneTask = RunExistingTasksFrom(_taskQueue) | ExecuteExpiredScheduledTasks();
        } while (ranAtLeastOneTask && ++drainAttempt < maxDrainAttempts);

        if (drainAttempt > 0)
        {
            lastExecutionTime = GetCurrentTimeNanos();
        }

        AfterRunningAllTasks();

        return drainAttempt > 0;
    }

    /**
     * Runs all tasks from the passed {@code taskQueue}.
     *
     * @param taskQueue To poll and execute all tasks.
     *
     * @return {@code true} if at least one task was executed.
     */
    protected bool RunAllTasksFrom(IQueue<IRunnable> taskQueue)
    {
        IRunnable task = PollTaskFrom(taskQueue);
        if (task == null)
        {
            return false;
        }

        for (;;)
        {
            SafeExecute(task);
            task = PollTaskFrom(taskQueue);
            if (task == null)
            {
                return true;
            }
        }
    }

    /**
     * What ever tasks are present in {@code taskQueue} when this method is invoked will be {@link Runnable#run()}.
     * @param taskQueue the task queue to drain.
     * @return {@code true} if at least {@link Runnable#run()} was called.
     */
    private bool RunExistingTasksFrom(IQueue<IRunnable> taskQueue)
    {
        IRunnable task = PollTaskFrom(taskQueue);
        if (task == null)
        {
            return false;
        }

        int remaining = Math.Min(_maxPendingTasks, taskQueue.Count);
        SafeExecute(task);
        // Use taskQueue.poll() directly rather than pollTaskFrom() since the latter may
        // silently consume more than one item from the queue (skips over WAKEUP_TASK instances)
        while (remaining-- > 0 && taskQueue.TryDequeue(out task))
        {
            SafeExecute(task);
        }

        return true;
    }

    /**
     * Poll all tasks from the task queue and run them via {@link Runnable#run()} method.  This method stops running
     * the tasks in the task queue and returns if it ran longer than {@code timeoutNanos}.
     */
    protected virtual bool RunAllTasks(long timeoutNanos)
    {
        FetchFromScheduledTaskQueue(_taskQueue);
        IRunnable task = PollTask();
        if (task == null)
        {
            AfterRunningAllTasks();
            return false;
        }

        long deadline = timeoutNanos > 0 ? GetCurrentTimeNanos() + timeoutNanos : 0;
        long runTasks = 0;
        long lastExecutionTime;
        long workStartTime = Ticker().NanoTime();
        for (;;)
        {
            SafeExecute(task);

            runTasks++;

            // Check timeout every 64 tasks because nanoTime() is relatively expensive.
            // XXX: Hard-coded value - will make it configurable if it is really a problem.
            if ((runTasks & 0x3F) == 0)
            {
                lastExecutionTime = GetCurrentTimeNanos();
                if (lastExecutionTime >= deadline)
                {
                    break;
                }
            }

            task = PollTask();
            if (task == null)
            {
                lastExecutionTime = GetCurrentTimeNanos();
                break;
            }
        }

        long workEndTime = Ticker().NanoTime();
        // CLR: the monitor atomically consumes this counter on another thread.
        // A read/add/write can restore an already-consumed window or lose new work.
        Interlocked.Add(ref accumulatedActiveTimeNanos, workEndTime - workStartTime);
        Volatile.Write(ref lastActivityTimeNanos, workEndTime);
        AfterRunningAllTasks();
        this.lastExecutionTime = lastExecutionTime;
        return true;
    }

    /**
     * Invoked before returning from {@link #runAllTasks()} and {@link #runAllTasks(long)}.
     */
    protected virtual void AfterRunningAllTasks() { }

    /**
     * Returns the amount of time left until the scheduled task with the closest dead line is executed.
     */
    protected virtual long DelayNanos(long currentTimeNanos)
    {
        currentTimeNanos -= Ticker().InitialNanoTime();

        var scheduledTask = PeekScheduledTask();
        if (scheduledTask == null)
        {
            return SCHEDULE_PURGE_INTERVAL;
        }

        return scheduledTask.DelayNanos(currentTimeNanos);
    }

    /**
     * Returns the absolute point in time (relative to {@link #getCurrentTimeNanos()}) at which the next
     * closest scheduled task should run.
     */
    protected virtual long DeadlineNanos()
    {
        IScheduledWork scheduledTask = PeekScheduledTask();
        if (scheduledTask == null)
        {
            return GetCurrentTimeNanos() + SCHEDULE_PURGE_INTERVAL;
        }

        return scheduledTask.DeadlineNanos();
    }

    /**
     * Updates the internal timestamp that tells when a submitted task was executed most recently.
     * {@link #runAllTasks()} and {@link #runAllTasks(long)} updates this timestamp automatically, and thus there's
     * usually no need to call this method.  However, if you take the tasks manually using {@link #takeTask()} or
     * {@link #pollTask()}, you have to call this method at the end of task execution loop for accurate quiet period
     * checks.
     */
    protected virtual void UpdateLastExecutionTime()
    {
        long now = GetCurrentTimeNanos();
        lastExecutionTime = now;
        Volatile.Write(ref lastActivityTimeNanos, now);
    }

    /**
     * Returns the number of registered channels for auto-scaling related decisions.
     * This is intended to be used by {@link MultithreadEventExecutorGroup} for dynamic scaling.
     *
     * @return The number of registered channels, or {@code -1} if not applicable.
     */
    protected internal virtual int GetNumOfRegisteredChannels() => -1;

    /**
     * Adds the given duration to the total active time for the current measurement window.
     * <p>
     * <strong>Note:</strong> This method is not thread-safe and must only be called from the
     * {@link #inEventLoop() event loop thread}.
     *
     * @param nanos The active time in nanoseconds to add.
     */
    protected virtual void ReportActiveIoTime(long nanos)
    {
        Debug.Assert(InEventLoop());
        if (nanos > 0)
        {
            // The event loop remains the reporting owner; atomic addition coordinates
            // that writer with the monitor's concurrent exchange-to-zero.
            Interlocked.Add(ref accumulatedActiveTimeNanos, nanos);
            Volatile.Write(ref lastActivityTimeNanos, Ticker().NanoTime());
        }
    }

    /**
     * Returns the accumulated active time since the last call and resets the counter.
     */
    protected internal virtual long GetAndResetAccumulatedActiveTimeNanos() => Interlocked.Exchange(ref accumulatedActiveTimeNanos, 0);

    /**
     * Returns the timestamp of the last known activity (tasks + I/O).
     */
    protected internal virtual long GetLastActivityTimeNanos() => Volatile.Read(ref lastActivityTimeNanos);

    /**
     * Atomically increments the counter for consecutive monitor cycles where utilization was below the
     * scale-down threshold. This is used by the auto-scaling monitor to track sustained idleness.
     *
     * @return The number of consecutive idle cycles before the increment.
     */
    protected internal virtual int GetAndIncrementIdleCycles() => unchecked(Interlocked.Increment(ref consecutiveIdleCycles) - 1);

    /**
     * Resets the counter for consecutive idle cycles to zero. This is typically called when the
     * executor's utilization is no longer considered idle, breaking the streak.
     */
    protected internal virtual void ResetIdleCycles() => Volatile.Write(ref consecutiveIdleCycles, 0);

    /**
     * Atomically increments the counter for consecutive monitor cycles where utilization was above the
     * scale-up threshold. This is used by the auto-scaling monitor to track a sustained high load.
     *
     * @return The number of consecutive busy cycles before the increment.
     */
    protected internal virtual int GetAndIncrementBusyCycles() => unchecked(Interlocked.Increment(ref consecutiveBusyCycles) - 1);

    /**
     * Resets the counter for consecutive busy cycles to zero. This is typically called when the
     * executor's utilization is no longer considered busy, breaking the streak.
     */
    protected internal virtual void ResetBusyCycles() => Volatile.Write(ref consecutiveBusyCycles, 0);

    /**
     * Returns {@code true} if this {@link SingleThreadEventExecutor} supports suspension.
     */
    protected virtual bool IsSuspensionSupported() => _supportSuspension;

    /**
     * Runs the task-processing loop until {@link #confirmShutdown()} returns {@code true}.
     *
     * <p>Implementations <strong>must not let a {@link Throwable} thrown by a task escape this
     * method</strong>: any uncaught {@link Throwable} terminates the executor (logged at {@code WARN}
     * and surfaced via {@link #terminationFuture()}), at which point every {@code Channel}
     * registered with this executor stops processing I/O and new task submissions are rejected.
     * The supplied helpers - {@link #runAllTasks()}, {@link #runAllTasks(long)}, and
     * {@link #safeExecute(Runnable)} - catch {@code Throwable} for you; custom loops built on
     * {@link #pollTask()} or {@link #takeTask()} are responsible for wrapping each task
     * invocation accordingly.
     */
    protected abstract void Run();

    /**
     * Do nothing, sub-classes may override
     */
    protected virtual void Cleanup()
    {
        // NOOP
    }

    public virtual void Wakeup(bool inEventLoop)
    {
        if (!inEventLoop)
        {
            // Use offer as we actually only need this to unblock the thread and if offer fails we do not care as there
            // is already something in the queue.
            _taskQueue.TryEnqueue(WAKEUP_TASK);
        }
    }

    public override bool InEventLoop()
    {
        return InEventLoop(Thread.CurrentThread);
    }

    public override bool InEventLoop(Thread thread)
    {
        return thread == _thread;
    }

    /**
     * Add a {@link Runnable} which will be executed on shutdown of this instance
     */
    public void AddShutdownHook(IRunnable task)
    {
        if (InEventLoop())
        {
            _shutdownHooks.Add(task);
        }
        else
        {
            Execute(Runnables.Create(() => _shutdownHooks.Add(task)));
        }
    }

    /**
     * Remove a previous added {@link Runnable} as a shutdown hook
     */
    public void RemoveShutdownHook(IRunnable task)
    {
        if (InEventLoop())
        {
            _shutdownHooks.Remove(task);
        }
        else
        {
            Execute(Runnables.Create(() => _shutdownHooks.Remove(task)));
        }
    }

    private bool RunShutdownHooks()
    {
        bool ran = false;
        // Note shutdown hooks can add / remove shutdown hooks.
        while (!_shutdownHooks.IsEmpty())
        {
            List<IRunnable> copy = new List<IRunnable>(_shutdownHooks);
            _shutdownHooks.Clear();
            foreach (IRunnable task in copy)
            {
                try
                {
                    RunTask(task);
                }
                catch (Exception t)
                {
                    logger.Warn("Shutdown hook raised an exception.", t);
                }
                finally
                {
                    ran = true;
                }
            }
        }

        if (ran)
        {
            lastExecutionTime = GetCurrentTimeNanos();
        }

        return ran;
    }

    private void Shutdown0(long quietPeriod, long timeout, int shutdownState, bool escalate = false)
    {
        if (escalate ? IsShutdown() : IsShuttingDown())
        {
            return;
        }

        bool inEventLoop = this.InEventLoop();
        bool wakeup;
        int oldState;
        for (;;)
        {
            oldState = _state.Get();
            // Check the same state snapshot used by CAS. A concurrent native
            // stop must not be downgraded back into graceful admission.
            if (oldState >= (escalate ? ST_SHUTDOWN : ST_SHUTTING_DOWN))
            {
                return;
            }

            int newState;
            wakeup = true;
            if (inEventLoop)
            {
                newState = shutdownState;
            }
            else
            {
                switch (oldState)
                {
                    case ST_SHUTTING_DOWN when escalate:
                    case ST_NOT_STARTED:
                    case ST_STARTED:
                    case ST_SUSPENDING:
                    case ST_SUSPENDED:
                        newState = shutdownState;
                        break;
                    default:
                        newState = oldState;
                        wakeup = false;
                        break;
                }
            }

            if (_state.CompareAndSet(oldState, newState))
            {
                break;
            }
        }

        if (quietPeriod != -1)
        {
            _gracefulShutdownQuietPeriod.Set(quietPeriod);
        }

        if (timeout != -1)
        {
            _gracefulShutdownTimeout.Set(timeout);
        }

        if (EnsureThreadStarted(oldState))
        {
            return;
        }

        if (wakeup)
        {
            _taskQueue.TryEnqueue(WAKEUP_TASK);
            if (!_addTaskWakesUp)
            {
                this.Wakeup(inEventLoop);
            }
        }
    }

    public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
    {
        ObjectUtil.CheckPositiveOrZero(quietPeriod, "quietPeriod");
        if (timeout < quietPeriod)
        {
            throw new ArgumentException(
                "timeout: " + timeout + " (expected >= quietPeriod (" + quietPeriod + "))");
        }
        //ObjectUtil.checkNotNull(timeout, "timeout");

        Shutdown0(ToNanos(quietPeriod), ToNanos(timeout), ST_SHUTTING_DOWN);
        return Termination;
    }

    public override Task Termination => _terminationSource.Task;

    public override Task StopAsync()
    {
        // Native stop closes admission even during an existing graceful quiet
        // period. Preserve ordered accepted-work drain and scheduled cancellation;
        // legacy shutdown entry points retain their pinned non-escalating behavior.
        Shutdown0(-1, -1, ST_SHUTDOWN, true);
        return Termination;
    }

    [Obsolete]
    public override void Shutdown()
    {
        Shutdown0(-1, -1, ST_SHUTDOWN);
    }

    public override bool IsShuttingDown()
    {
        return _state.Get() >= ST_SHUTTING_DOWN;
    }

    public override bool IsShutdown()
    {
        return _state.Get() >= ST_SHUTDOWN;
    }

    public override bool IsTerminated()
    {
        return _state.Get() == ST_TERMINATED;
    }

    public override bool IsSuspended()
    {
        int currentState = _state.Get();
        return currentState == ST_SUSPENDED || currentState == ST_SUSPENDING;
    }

    public override bool TrySuspend()
    {
        if (_supportSuspension)
        {
            if (_state.CompareAndSet(ST_STARTED, ST_SUSPENDING))
            {
                Wakeup(InEventLoop());
                return true;
            }

            if (_state.CompareAndSet(ST_NOT_STARTED, ST_SUSPENDED))
            {
                return true;
            }

            int currentState = _state.Get();
            return currentState == ST_SUSPENDED || currentState == ST_SUSPENDING;
        }

        return false;
    }

    /**
     * Returns {@code true} if this {@link SingleThreadEventExecutor} can be suspended at the moment, {@code false}
     * otherwise.
     *
     * @return  if suspension is possible at the moment.
     */
    protected virtual bool CanSuspend()
    {
        return CanSuspend(_state.Get());
    }

    /**
     * Returns {@code true} if this {@link SingleThreadEventExecutor} can be suspended at the moment, {@code false}
     * otherwise.
     *
     * Subclasses might override this method to add extra checks.
     *
     * @param   state   the current internal state of the {@link SingleThreadEventExecutor}.
     * @return          if suspension is possible at the moment.
     */
    protected virtual bool CanSuspend(int state)
    {
        Debug.Assert(InEventLoop());
        return _supportSuspension && (state == ST_SUSPENDED || state == ST_SUSPENDING)
                                  && !HasTasks() && NextScheduledTaskDeadlineNanos() == -1;
    }

    /**
     * Confirm that the shutdown if the instance should be done now!
     */
    protected virtual bool ConfirmShutdown()
    {
        if (!IsShuttingDown())
        {
            return false;
        }

        if (!InEventLoop())
        {
            throw new InvalidOperationException("must be invoked from an event loop");
        }

        CancelScheduledTasks();

        if (gracefulShutdownStartTime == 0)
        {
            gracefulShutdownStartTime = GetCurrentTimeNanos();
        }

        if (RunAllTasks() || RunShutdownHooks())
        {
            if (IsShutdown())
            {
                // Executor shut down - no new tasks anymore.
                return true;
            }

            // There were tasks in the queue. Wait a little bit more until no tasks are queued for the quiet period or
            // terminate if the quiet period is 0.
            // See https://github.com/netty/netty/issues/4241
            if (_gracefulShutdownQuietPeriod.Get() == 0)
            {
                return true;
            }

            _taskQueue.TryEnqueue(WAKEUP_TASK);
            return false;
        }

        long nanoTime = GetCurrentTimeNanos();

        if (IsShutdown() || nanoTime - gracefulShutdownStartTime > _gracefulShutdownTimeout.Get())
        {
            return true;
        }

        if (nanoTime - lastExecutionTime <= _gracefulShutdownQuietPeriod.Get())
        {
            // Check if any tasks were added to the queue every 100ms.
            // TODO: Change the behavior of takeTask() so that it returns on timeout.
            _taskQueue.TryEnqueue(WAKEUP_TASK);
            try
            {
                Thread.Sleep(100);
            }
            catch (ThreadInterruptedException e)
            {
                // Ignore
            }

            return false;
        }

        // No tasks were added for last quiet period - hopefully safe to shut down.
        // (Hopefully because we really cannot make a guarantee that there will be no execute() calls by a user.)
        return true;
    }

    public override bool AwaitTermination(TimeSpan timeout)
    {
        if (InEventLoop())
        {
            throw new InvalidOperationException("cannot await termination of the current thread");
        }

        // CLR CountdownEvent waits accept at most Int32 milliseconds. Keep the
        // original interruptible latch semantics and wait larger durations in chunks.
        Thread.Sleep(0);
        long started = Stopwatch.GetTimestamp();
        long ticks = Math.Max(0, timeout.Ticks);
        for (;;)
        {
            long elapsed = Stopwatch.GetElapsedTime(started).Ticks;
            long left = elapsed >= ticks ? 0 : ticks - elapsed;
            int millis = left == 0 ? 0 : (int)Math.Min(int.MaxValue, 1 + (left - 1) / TimeSpan.TicksPerMillisecond);
            if (_threadLock.Wait(millis) || Stopwatch.GetElapsedTime(started).Ticks >= ticks) break;
        }

        return IsTerminated();
    }

    public override void Execute(IRunnable task)
    {
        Execute0(task);
    }

    public override void LazyExecute(IRunnable task)
    {
        LazyExecute0(task);
    }

    private void Execute0(IRunnable task)
    {
        ObjectUtil.CheckNotNull(task, "task");
        Execute(task, WakesUpForTask(task));
    }

    private void LazyExecute0(IRunnable task)
    {
        Execute(ObjectUtil.CheckNotNull(task, "task"), false);
    }

    protected override void ScheduleRemoveScheduled(IScheduledWork task)
    {
        ObjectUtil.CheckNotNull(task, "task");
        int currentState = _state.Get();
        if (_supportSuspension && (currentState == ST_SUSPENDED || currentState == ST_SUSPENDING))
        {
            // In the case of scheduling for removal we need to also ensure we will recover the "suspend" state
            // after it if it was set before. Otherwise we will always end up "unsuspending" things on cancellation
            // which is not optimal. This also covers the case where the executor is still ST_SUSPENDING (suspend
            // was requested but not confirmed yet): if the removal task races with doStartThread() re-engaging
            // the thread as ST_STARTED (see the ST_SUSPENDED/ST_STARTED CAS dance below), nobody would otherwise
            // ever re-request suspension and the thread would keep running forever waiting for new tasks.
            Execute(Runnables.Create(() =>
            {
                task.Run();
                if (CanSuspend(ST_SUSPENDED))
                {
                    // Try suspending again to recover the state before we submitted the new task that will
                    // handle cancellation itself.
                    TrySuspend();
                }
            }), true);
        }
        else
        {
            // task will remove itself from scheduled task queue when it runs
            Execute(task, false);
        }
    }

    private void Execute(IRunnable task, bool immediate)
    {
        bool inEventLoop = this.InEventLoop();
        AddTask(task);
        if (!inEventLoop)
        {
            StartThread();
            if (IsShutdown())
            {
                bool reject = false;
                try
                {
                    if (RemoveTask(task))
                    {
                        reject = true;
                    }
                }
                catch (NotSupportedException e)
                {
                    // The task queue does not support removal so the best thing we can do is to just move on and
                    // hope we will be able to pick-up the task before its completely terminated.
                    // In worst case we will log on termination.
                }

                if (reject)
                {
                    SingleThreadEventExecutor.Reject();
                }
            }
        }

        if (!_addTaskWakesUp && immediate)
        {
            Wakeup(inEventLoop);
        }
    }

    /**
     * Returns the {@link ThreadProperties} of the {@link Thread} that powers the {@link SingleThreadEventExecutor}.
     * If the {@link SingleThreadEventExecutor} is not started yet, this operation will start it and block until
     * it is fully started.
     */
    public IThreadProperties ThreadProperties()
    {
        IThreadProperties threadProperties = _threadProperties.Get();
        if (threadProperties == null)
        {
            Thread thread = _thread;
            if (thread == null)
            {
                Debug.Assert(!InEventLoop());
                WaitForBootstrap(this.SubmitAsync(NOOP_TASK.Run));
                thread = _thread;
                Debug.Assert(thread != null);
            }

            threadProperties = new DefaultThreadProperties(thread);
            if (!_threadProperties.CompareAndSet(null, threadProperties))
            {
                threadProperties = _threadProperties.Get();
            }
        }

        return threadProperties;
    }

    // The synchronous properties getter must finish starting its owner before
    // returning. Preserve an interrupt consumed by a blocking CLR wait without
    // introducing a Java Future into native submission or a public Task facade.
    private static void WaitForBootstrap(Task started)
    {
        bool interrupted = false;
        try
        {
            for (;;)
            {
                try { started.GetAwaiter().GetResult(); return; }
                catch (ThreadInterruptedException error)
                    when (!started.IsFaulted || !ReferenceEquals(started.Exception.InnerException, error))
                { interrupted = true; }
            }
        }
        finally { if (interrupted) Thread.CurrentThread.Interrupt(); }
    }

    /**
     * @deprecated override {@link SingleThreadEventExecutor#wakesUpForTask} to re-create this behaviour
     */
    [Obsolete]
    protected interface NonWakeupRunnable : ILazyRunnable { }

    /**
     * Can be overridden to control which tasks require waking the {@link EventExecutor} thread
     * if it is waiting so that they can be run immediately.
     */
    protected virtual bool WakesUpForTask(IRunnable task)
    {
        return true;
    }

    protected static void Reject()
    {
        throw new RejectedExecutionException("event executor terminated");
    }

    /**
     * Offers the task to the associated {@link RejectedExecutionHandler}.
     *
     * @param task to reject.
     */
    protected void Reject(IRunnable task)
    {
        _rejectedExecutionHandler.Rejected(task, this);
    }

    // ScheduledExecutorService implementation
    private static readonly long SCHEDULE_PURGE_INTERVAL = (long)TimeSpan.FromSeconds(1).TotalNanoseconds;

    private void StartThread()
    {
        int currentState = _state.Get();
        while (currentState == ST_NOT_STARTED || currentState == ST_SUSPENDED)
        {
            if (_state.CompareAndSet(currentState, ST_STARTED))
            {
                ResetIdleCycles();
                ResetBusyCycles();
                bool success = false;
                try
                {
                    DoStartThread();
                    success = true;
                }
                finally
                {
                    if (!success)
                    {
                        _state.CompareAndSet(ST_STARTED, ST_NOT_STARTED);
                    }
                }
                break;
            }
            // The state changed after we read it. If trySuspend() moved it from ST_NOT_STARTED to ST_SUSPENDED no
            // thread was started, so we need to try again. Otherwise the task that was just added might never run.
            // This may deliberately un-suspend an executor that trySuspend() has just suspended, which is the same
            // thing execute() does for an executor that is already suspended. We only loop again if another thread
            // changed the state in between, so this can't spin.
            currentState = _state.Get();
        }
    }

    private bool EnsureThreadStarted(int oldState)
    {
        if (oldState == ST_NOT_STARTED || oldState == ST_SUSPENDED)
        {
            try
            {
                DoStartThread();
            }
            catch (Exception cause)
            {
                _state.Set(ST_TERMINATED);
                _terminationSource.TrySetException(cause);

                if (cause is OutOfMemoryException || cause is StackOverflowException || cause is ThreadAbortException)
                {
                    // Also rethrow as it may be an OOME for example
                    PlatformDependent.ThrowException(cause);
                }

                return true;
            }
        }

        return false;
    }

    private void DoStartThread()
    {
        _executor.Execute(Runnables.Create(DoStartThreadInternal));
    }

    private void DoStartThreadInternal()
    {
        using (UninterruptibleMonitor.Enter(_processingLock))
        {
            Debug.Assert(_thread == null);
            _thread = Thread.CurrentThread;
            if (interrupted)
            {
                _thread.Interrupt();
                interrupted = false;
            }

            bool success = false;
            Exception unexpectedException = null;
            UpdateLastExecutionTime();
            bool suspend = false;
            try
            {
                for (;;)
                {
                    Run();
                    success = true;

                    int currentState = _state.Get();
                    if (CanSuspend(currentState))
                    {
                        // currentState might already be ST_SUSPENDED here (we can loop back around with the
                        // state still ST_SUSPENDED via the ST_SUSPENDING/ST_SUSPENDED branch below), so we must
                        // CAS from currentState and not hardcode ST_SUSPENDING as the expected value, or the CAS
                        // would spuriously "fail" forever and livelock this thread instead of finishing suspend.
                        if (!_state.CompareAndSet(currentState, ST_SUSPENDED))
                        {
                            // Try again as the CAS failed.
                            continue;
                        }

                        if (!CanSuspend(ST_SUSPENDED) && _state.CompareAndSet(ST_SUSPENDED, ST_STARTED))
                        {
                            // Seems like there was something added to the task queue again in the meantime but we
                            // were able to re-engage this thread as the event loop thread.
                            continue;
                        }

                        suspend = true;
                    }
                    else if (currentState == ST_SUSPENDING || currentState == ST_SUSPENDED)
                    {
                        // We were trying to suspend (or just did) but something raced in - e.g. a scheduled
                        // task being cancelled and re-submitted itself for removal via scheduleRemoveScheduled()
                        // - so we can no longer suspend right now. No shutdown was requested, so this must not
                        // be treated as if run() returned without confirming shutdown; just go around the loop
                        // again so run() can pick up whatever raced in.
                        continue;
                    }

                    break;
                }
            }
            catch (Exception t)
            {
                unexpectedException = t;
                logger.Warn("Unexpected exception from an event executor: ", t);
            }
            finally
            {
                bool shutdown = !suspend;
                if (shutdown)
                {
                    for (;;)
                    {
                        // We are re-fetching the state as it might have been shutdown in the meantime.
                        int oldState = _state.Get();
                        if (oldState >= ST_SHUTTING_DOWN || _state.CompareAndSet(oldState, ST_SHUTTING_DOWN))
                        {
                            break;
                        }
                    }

                    if (success && gracefulShutdownStartTime == 0)
                    {
                        // Check if confirmShutdown() was called at the end of the loop.
                        if (logger.IsErrorEnabled())
                        {
                            logger.Error("Buggy " + nameof(IEventExecutor) + " implementation; " +
                                         nameof(SingleThreadEventExecutor) + ".confirmShutdown() must " +
                                         "be called before run() implementation terminates.");
                        }
                    }
                }

                try
                {
                    if (shutdown)
                    {
                        // Run all remaining tasks and shutdown hooks. At this point the event loop
                        // is in ST_SHUTTING_DOWN state still accepting tasks which is needed for
                        // graceful shutdown with quietPeriod.
                        for (;;)
                        {
                            if (ConfirmShutdown())
                            {
                                break;
                            }
                        }

                        // Now we want to make sure no more tasks can be added from this point. This is
                        // achieved by switching the state. Any new tasks beyond this point will be rejected.
                        for (;;)
                        {
                            int currentState = _state.Get();
                            if (currentState >= ST_SHUTDOWN || _state.CompareAndSet(currentState, ST_SHUTDOWN))
                            {
                                break;
                            }
                        }

                        // We have the final set of tasks in the queue now, no more can be added, run all remaining.
                        // No need to loop here, this is the final pass.
                        ConfirmShutdown();
                    }
                }
                finally
                {
                    try
                    {
                        if (shutdown)
                        {
                            try
                            {
                                Cleanup();
                            }
                            finally
                            {
                                // Lets remove all FastThreadLocals for the Thread as we are about to terminate and
                                // notify the future. The user may block on the future and once it unblocks the JVM
                                // may terminate and start unloading classes.
                                // See https://github.com/netty/netty/issues/6596.
                                FastThreadLocal.RemoveAll();

                                _state.Set(ST_TERMINATED);
                                _threadLock.Signal();
                                int numUserTasks = DrainTasks();
                                if (numUserTasks > 0 && logger.IsWarnEnabled())
                                {
                                    logger.Warn("An event executor terminated with " +
                                                "non-empty task queue (" + numUserTasks + ')');
                                }

                                if (unexpectedException == null)
                                {
                                    _terminationSource.SetResult();
                                }
                                else
                                {
                                    _terminationSource.SetException(unexpectedException);
                                }
                            }
                        }
                        else
                        {
                            // Lets remove all FastThreadLocals for the Thread as we are about to terminate it.
                            FastThreadLocal.RemoveAll();

                            // Reset the stored threadProperties in case of suspension.
                            _threadProperties.Set(null);
                        }
                    }
                    finally
                    {
                        _thread = null;
                        // Let the next thread take over if needed.
                        //processingLock.unlock();
                    }
                }
            }
        }
    }

    internal int DrainTasks()
    {
        int numTasks = 0;
        for (;;)
        {
            _taskQueue.TryDequeue(out var runnable);
            if (runnable == null)
            {
                break;
            }

            // WAKEUP_TASK should be just discarded as these are added internally.
            // The important bit is that we not have any user tasks left.
            if (WAKEUP_TASK != runnable)
            {
                numTasks++;
            }
        }

        return numTasks;
    }
}
