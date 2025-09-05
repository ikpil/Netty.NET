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
using System.Collections.Concurrent;
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
 * Abstract base class for {@link IOrderedEventExecutor}'s that execute all its submitted tasks in a single thread.
 *
 */
public abstract class SingleThreadEventExecutor : AbstractScheduledEventExecutor, IOrderedEventExecutor
{
    private static readonly int DEFAULT_MAX_PENDING_EXECUTOR_TASKS = Math.Max(16,
        SystemPropertyUtil.getInt("io.netty.eventexecutor.maxPendingTasks", int.MaxValue));

    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(SingleThreadEventExecutor));

    private const int ST_NOT_STARTED = 1;
    private const int ST_SUSPENDING = 2;
    private const int ST_SUSPENDED = 3;
    private const int ST_STARTED = 4;
    private const int ST_SHUTTING_DOWN = 5;
    private const int ST_SHUTDOWN = 6;
    private const int ST_TERMINATED = 7;

    private static readonly IRunnable NOOP_TASK = EmptyRunnable.Shared;

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

    private long lastExecutionTime;

    private readonly AtomicInteger _state = new AtomicInteger(ST_NOT_STARTED);

    private readonly AtomicLong _gracefulShutdownQuietPeriod = new AtomicLong();
    private readonly AtomicLong _gracefulShutdownTimeout = new AtomicLong();
    private long gracefulShutdownStartTime;

    //private readonly TaskCompletionSource<Void> _terminationFuture = new DefaultPromise<Void>(GlobalEventExecutor.INSTANCE);
    private readonly TaskCompletionSource<Void> _terminationFuture = new DefaultPromise<Void>(GlobalEventExecutor.INSTANCE);

    /**
     * Create a new instance
     *
     * @param parent            the {@link IEventExecutorGroup} which is the parent of this instance and belongs to it
     * @param threadFactory     the {@link IThreadFactory} which will be used for the used {@link Thread}
     * @param addTaskWakesUp    {@code true} if and only if invocation of {@link #addTask(IRunnable)} will wake up the
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
     * @param parent            the {@link IEventExecutorGroup} which is the parent of this instance and belongs to it
     * @param threadFactory     the {@link IThreadFactory} which will be used for the used {@link Thread}
     * @param addTaskWakesUp    {@code true} if and only if invocation of {@link #addTask(IRunnable)} will wake up the
     *                          executor thread
     * @param maxPendingTasks   the maximum number of pending tasks before new tasks will be rejected.
     * @param rejectedHandler   the {@link IRejectedExecutionHandler} to use.
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
     * @param parent            the {@link IEventExecutorGroup} which is the parent of this instance and belongs to it
     * @param threadFactory     the {@link IThreadFactory} which will be used for the used {@link Thread}
     * @param addTaskWakesUp    {@code true} if and only if invocation of {@link #addTask(IRunnable)} will wake up the
     *                          executor thread
     * @param supportSuspension {@code true} if suspension of this {@link SingleThreadEventExecutor} is supported.
     * @param maxPendingTasks   the maximum number of pending tasks before new tasks will be rejected.
     * @param rejectedHandler   the {@link IRejectedExecutionHandler} to use.
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
     * @param parent            the {@link IEventExecutorGroup} which is the parent of this instance and belongs to it
     * @param executor          the {@link IExecutor} which will be used for executing
     * @param addTaskWakesUp    {@code true} if and only if invocation of {@link #addTask(IRunnable)} will wake up the
     *                          executor thread
     */
    protected SingleThreadEventExecutor(IEventExecutorGroup parent, IExecutor executor, bool addTaskWakesUp)
        : this(parent, executor, addTaskWakesUp, DEFAULT_MAX_PENDING_EXECUTOR_TASKS, RejectedExecutionHandlers.reject())
    {
    }

    /**
     * Create a new instance
     *
     * @param parent            the {@link IEventExecutorGroup} which is the parent of this instance and belongs to it
     * @param executor          the {@link IExecutor} which will be used for executing
     * @param addTaskWakesUp    {@code true} if and only if invocation of {@link #addTask(IRunnable)} will wake up the
     *                          executor thread
     * @param maxPendingTasks   the maximum number of pending tasks before new tasks will be rejected.
     * @param rejectedHandler   the {@link IRejectedExecutionHandler} to use.
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
     * @param parent            the {@link IEventExecutorGroup} which is the parent of this instance and belongs to it
     * @param executor          the {@link IExecutor} which will be used for executing
     * @param addTaskWakesUp    {@code true} if and only if invocation of {@link #addTask(IRunnable)} will wake up the
     *                          executor thread
     * @param supportSuspension {@code true} if suspension of this {@link SingleThreadEventExecutor} is supported.
     * @param maxPendingTasks   the maximum number of pending tasks before new tasks will be rejected.
     * @param rejectedHandler   the {@link IRejectedExecutionHandler} to use.
     */
    protected SingleThreadEventExecutor(IEventExecutorGroup parent, IExecutor executor,
        bool addTaskWakesUp, bool supportSuspension,
        int maxPendingTasks, IRejectedExecutionHandler rejectedHandler)
        : base(parent)
    {
        _addTaskWakesUp = addTaskWakesUp;
        _supportSuspension = supportSuspension;
        _maxPendingTasks = Math.Max(16, maxPendingTasks);
        _executor = ThreadExecutorMap.apply(executor, this);
        _taskQueue = newTaskQueue(_maxPendingTasks);
        _rejectedExecutionHandler = ObjectUtil.checkNotNull(rejectedHandler, "rejectedHandler");
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
        _executor = ThreadExecutorMap.apply(executor, this);
        _taskQueue = ObjectUtil.checkNotNull(taskQueue, "taskQueue");
        _rejectedExecutionHandler = ObjectUtil.checkNotNull(rejectedHandler, "rejectedHandler");
    }

    /**
     * @deprecated Please use and override {@link #newTaskQueue(int)}.
     */
    [Obsolete]
    protected IQueue<IRunnable> newTaskQueue()
    {
        return newTaskQueue(_maxPendingTasks);
    }

    /**
     * Create a new {@link Queue} which will holds the tasks to execute. This default implementation will return a
     * {@link LinkedBlockingQueue} but if your sub-class of {@link SingleThreadEventExecutor} will not do any blocking
     * calls on the this {@link Queue} it may make sense to {@code @Override} this and return some more performant
     * implementation that does not support blocking operations at all.
     */
    protected IQueue<IRunnable> newTaskQueue(int maxPendingTasks)
    {
        return new LinkedBlockingQueue<IRunnable>(maxPendingTasks);
    }

    /**
     * Interrupt the current running {@link Thread}.
     */
    protected void interruptThread()
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
    protected IRunnable pollTask()
    {
        Debug.Assert(inEventLoop());
        return pollTaskFrom(_taskQueue);
    }

    protected static IRunnable pollTaskFrom(IQueue<IRunnable> taskQueue)
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
     * Take the next {@link IRunnable} from the task queue and so will block if no task is currently present.
     * <p>
     * Be aware that this method will throw an {@link NotSupportedException} if the task queue, which was
     * created via {@link #newTaskQueue()}, does not implement {@link BlockingQueue}.
     * </p>
     *
     * @return {@code null} if the executor thread has been interrupted or waken up.
     */
    protected IRunnable takeTask()
    {
        Debug.Assert(inEventLoop());
        if (!(_taskQueue is BlockingQueue<IRunnable>))
        {
            throw new NotSupportedException();
        }

        BlockingQueue<IRunnable> taskQueue = (BlockingQueue<IRunnable>)_taskQueue;
        for (;;)
        {
            IScheduledTask scheduledTask = peekScheduledTask();
            if (scheduledTask == null)
            {
                IRunnable task = null;
                try
                {
                    taskQueue.TryDequeue(out task);
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
                long delayNanos = scheduledTask.delayNanos();
                IRunnable task = null;
                if (delayNanos > 0)
                {
                    try
                    {
                        task = taskQueue.poll(delayNanos, System.TimeSpan.NANOSECONDS);
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
                    fetchFromScheduledTaskQueue();
                    task = taskQueue.poll();
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

    private bool fetchFromScheduledTaskQueue()
    {
        return fetchFromScheduledTaskQueue(_taskQueue);
    }

    /**
     * @return {@code true} if at least one scheduled task was executed.
     */
    private bool executeExpiredScheduledTasks()
    {
        if (_scheduledTaskQueue == null || _scheduledTaskQueue.isEmpty())
        {
            return false;
        }

        long nanoTime = getCurrentTimeNanos();
        IRunnable scheduledTask = pollScheduledTask(nanoTime);
        if (scheduledTask == null)
        {
            return false;
        }

        do
        {
            safeExecute(scheduledTask);
        } while ((scheduledTask = pollScheduledTask(nanoTime)) != null);

        return true;
    }

    /**
     * @see Queue#peek()
     */
    protected IRunnable peekTask()
    {
        Debug.Assert(inEventLoop());
        return _taskQueue.TryPeek(out var task) ? task : null;
    }

    /**
     * @see Queue#isEmpty()
     */
    protected bool hasTasks()
    {
        Debug.Assert(inEventLoop());
        return !_taskQueue.IsEmpty();
    }

    /**
     * Return the number of tasks that are pending for processing.
     */
    public int pendingTasks()
    {
        return _taskQueue.Count;
    }

    /**
     * Add a task to the task queue, or throws a {@link RejectedExecutionException} if this instance was shutdown
     * before.
     */
    protected void addTask(IRunnable task)
    {
        ObjectUtil.checkNotNull(task, "task");
        if (!offerTask(task))
        {
            reject(task);
        }
    }

    public bool offerTask(IRunnable task)
    {
        if (isShutdown())
        {
            reject();
        }

        return _taskQueue.TryEnqueue(task);
    }

    /**
     * @see Queue#remove(object)
     */
    protected bool removeTask(IRunnable task)
    {
        return _taskQueue.TryRemove(ObjectUtil.checkNotNull(task, "task"));
    }

    /**
     * Poll all tasks from the task queue and run them via {@link IRunnable#run()} method.
     *
     * @return {@code true} if and only if at least one task was run
     */
    protected bool runAllTasks()
    {
        Debug.Assert(inEventLoop());
        bool fetchedAll;
        bool ranAtLeastOne = false;

        do
        {
            fetchedAll = fetchFromScheduledTaskQueue(_taskQueue);
            if (runAllTasksFrom(_taskQueue))
            {
                ranAtLeastOne = true;
            }
        } while (!fetchedAll); // keep on processing until we fetched all scheduled tasks.

        if (ranAtLeastOne)
        {
            lastExecutionTime = getCurrentTimeNanos();
        }

        afterRunningAllTasks();
        return ranAtLeastOne;
    }

    /**
     * Execute all expired scheduled tasks and all current tasks in the executor queue until both queues are empty,
     * or {@code maxDrainAttempts} has been exceeded.
     * @param maxDrainAttempts The maximum amount of times this method attempts to drain from queues. This is to prevent
     *                         continuous task execution and scheduling from preventing the IEventExecutor thread to
     *                         make progress and return to the selector mechanism to process inbound I/O events.
     * @return {@code true} if at least one task was run.
     */
    protected bool runScheduledAndExecutorTasks(int maxDrainAttempts)
    {
        Debug.Assert(inEventLoop());
        bool ranAtLeastOneTask;
        int drainAttempt = 0;
        do
        {
            // We must run the taskQueue tasks first, because the scheduled tasks from outside the EventLoop are queued
            // here because the taskQueue is thread safe and the scheduledTaskQueue is not thread safe.
            ranAtLeastOneTask = runExistingTasksFrom(_taskQueue) | executeExpiredScheduledTasks();
        } while (ranAtLeastOneTask && ++drainAttempt < maxDrainAttempts);

        if (drainAttempt > 0)
        {
            lastExecutionTime = getCurrentTimeNanos();
        }

        afterRunningAllTasks();

        return drainAttempt > 0;
    }

    /**
     * Runs all tasks from the passed {@code taskQueue}.
     *
     * @param taskQueue To poll and execute all tasks.
     *
     * @return {@code true} if at least one task was executed.
     */
    protected bool runAllTasksFrom(IQueue<IRunnable> taskQueue)
    {
        IRunnable task = pollTaskFrom(taskQueue);
        if (task == null)
        {
            return false;
        }

        for (;;)
        {
            safeExecute(task);
            task = pollTaskFrom(taskQueue);
            if (task == null)
            {
                return true;
            }
        }
    }

    /**
     * What ever tasks are present in {@code taskQueue} when this method is invoked will be {@link IRunnable#run()}.
     * @param taskQueue the task queue to drain.
     * @return {@code true} if at least {@link IRunnable#run()} was called.
     */
    private bool runExistingTasksFrom(IQueue<IRunnable> taskQueue)
    {
        IRunnable task = pollTaskFrom(taskQueue);
        if (task == null)
        {
            return false;
        }

        int remaining = Math.Min(_maxPendingTasks, taskQueue.Count);
        safeExecute(task);
        // Use taskQueue.poll() directly rather than pollTaskFrom() since the latter may
        // silently consume more than one item from the queue (skips over WAKEUP_TASK instances)
        while (remaining-- > 0 && taskQueue.TryDequeue(out task))
        {
            safeExecute(task);
        }

        return true;
    }

    /**
     * Poll all tasks from the task queue and run them via {@link IRunnable#run()} method.  This method stops running
     * the tasks in the task queue and returns if it ran longer than {@code timeoutNanos}.
     */
    protected bool runAllTasks(long timeoutNanos)
    {
        fetchFromScheduledTaskQueue(_taskQueue);
        IRunnable task = pollTask();
        if (task == null)
        {
            afterRunningAllTasks();
            return false;
        }

        long deadline = timeoutNanos > 0 ? getCurrentTimeNanos() + timeoutNanos : 0;
        long runTasks = 0;
        long lastExecutionTime;
        for (;;)
        {
            safeExecute(task);

            runTasks++;

            // Check timeout every 64 tasks because nanoTime() is relatively expensive.
            // XXX: Hard-coded value - will make it configurable if it is really a problem.
            if ((runTasks & 0x3F) == 0)
            {
                lastExecutionTime = getCurrentTimeNanos();
                if (lastExecutionTime >= deadline)
                {
                    break;
                }
            }

            task = pollTask();
            if (task == null)
            {
                lastExecutionTime = getCurrentTimeNanos();
                break;
            }
        }

        afterRunningAllTasks();
        this.lastExecutionTime = lastExecutionTime;
        return true;
    }

    /**
     * Invoked before returning from {@link #runAllTasks()} and {@link #runAllTasks(long)}.
     */
    protected void afterRunningAllTasks() { }

    /**
     * Returns the amount of time left until the scheduled task with the closest dead line is executed.
     */
    protected long delayNanos(long currentTimeNanos)
    {
        currentTimeNanos -= ticker().initialNanoTime();

        var scheduledTask = peekScheduledTask();
        if (scheduledTask == null)
        {
            return SCHEDULE_PURGE_INTERVAL;
        }

        return scheduledTask.delayNanos(currentTimeNanos);
    }

    /**
     * Returns the absolute point in time (relative to {@link #getCurrentTimeNanos()}) at which the next
     * closest scheduled task should run.
     */
    protected long deadlineNanos()
    {
        IScheduledTask scheduledTask = peekScheduledTask();
        if (scheduledTask == null)
        {
            return getCurrentTimeNanos() + SCHEDULE_PURGE_INTERVAL;
        }

        return scheduledTask.deadlineNanos();
    }

    /**
     * Updates the internal timestamp that tells when a submitted task was executed most recently.
     * {@link #runAllTasks()} and {@link #runAllTasks(long)} updates this timestamp automatically, and thus there's
     * usually no need to call this method.  However, if you take the tasks manually using {@link #takeTask()} or
     * {@link #pollTask()}, you have to call this method at the end of task execution loop for accurate quiet period
     * checks.
     */
    protected void updateLastExecutionTime()
    {
        lastExecutionTime = getCurrentTimeNanos();
    }

    /**
     * Run the tasks in the {@link #taskQueue}
     */
    protected abstract void run();

    /**
     * Do nothing, sub-classes may override
     */
    protected void cleanup()
    {
        // NOOP
    }

    protected void wakeup(bool inEventLoop)
    {
        if (!inEventLoop)
        {
            // Use offer as we actually only need this to unblock the thread and if offer fails we do not care as there
            // is already something in the queue.
            _taskQueue.TryEnqueue(WAKEUP_TASK);
        }
    }

    public bool inEventLoop()
    {
        return inEventLoop(Thread.CurrentThread);
    }

    public override bool inEventLoop(Thread thread)
    {
        return thread == _thread;
    }

    /**
     * Add a {@link IRunnable} which will be executed on shutdown of this instance
     */
    public void addShutdownHook(IRunnable task)
    {
        if (inEventLoop())
        {
            _shutdownHooks.Add(task);
        }
        else
        {
            execute(new AnonymousRunnable(() => _shutdownHooks.Add(task)));
        }
    }

    /**
     * Remove a previous added {@link IRunnable} as a shutdown hook
     */
    public void removeShutdownHook(IRunnable task)
    {
        if (inEventLoop())
        {
            _shutdownHooks.Remove(task);
        }
        else
        {
            execute(new AnonymousRunnable(() => _shutdownHooks.Remove(task)));
        }
    }

    private bool runShutdownHooks()
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
                    runTask(task);
                }
                catch (Exception t)
                {
                    logger.warn("Shutdown hook raised an exception.", t);
                }
                finally
                {
                    ran = true;
                }
            }
        }

        if (ran)
        {
            lastExecutionTime = getCurrentTimeNanos();
        }

        return ran;
    }

    private void shutdown0(long quietPeriod, long timeout, int shutdownState)
    {
        if (isShuttingDown())
        {
            return;
        }

        bool inEventLoop = this.inEventLoop();
        bool wakeup;
        int oldState;
        for (;;)
        {
            if (isShuttingDown())
            {
                return;
            }

            int newState;
            wakeup = true;
            oldState = _state.get();
            if (inEventLoop)
            {
                newState = shutdownState;
            }
            else
            {
                switch (oldState)
                {
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

            if (_state.compareAndSet(oldState, newState))
            {
                break;
            }
        }

        if (quietPeriod != -1)
        {
            _gracefulShutdownQuietPeriod.set(quietPeriod);
        }

        if (timeout != -1)
        {
            _gracefulShutdownTimeout.set(timeout);
        }

        if (ensureThreadStarted(oldState))
        {
            return;
        }

        if (wakeup)
        {
            _taskQueue.TryEnqueue(WAKEUP_TASK);
            if (!_addTaskWakesUp)
            {
                this.wakeup(inEventLoop);
            }
        }
    }

    public override Task shutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
    {
        ObjectUtil.checkPositiveOrZero(quietPeriod, "quietPeriod");
        if (timeout < quietPeriod)
        {
            throw new ArgumentException(
                "timeout: " + timeout + " (expected >= quietPeriod (" + quietPeriod + "))");
        }
        //ObjectUtil.checkNotNull(timeout, "timeout");

        shutdown0((long)quietPeriod.TotalNanoseconds, (long)timeout.TotalNanoseconds, ST_SHUTTING_DOWN);
        return terminationFuture();
    }

    public Task terminationFuture()
    {
        return _terminationFuture.Task;
    }

    [Obsolete]
    public override void shutdown()
    {
        shutdown0(-1, -1, ST_SHUTDOWN);
    }

    public override bool isShuttingDown()
    {
        return _state.get() >= ST_SHUTTING_DOWN;
    }

    public override bool isShutdown()
    {
        return _state.get() >= ST_SHUTDOWN;
    }

    public override bool isTerminated()
    {
        return _state.get() == ST_TERMINATED;
    }

    public override bool isSuspended()
    {
        int currentState = _state.get();
        return currentState == ST_SUSPENDED || currentState == ST_SUSPENDING;
    }

    public override bool trySuspend()
    {
        if (_supportSuspension)
        {
            if (_state.compareAndSet(ST_STARTED, ST_SUSPENDING))
            {
                wakeup(inEventLoop());
                return true;
            }

            int currentState = _state.get();
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
    protected bool canSuspend()
    {
        return canSuspend(_state.get());
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
    protected bool canSuspend(int state)
    {
        Debug.Assert(inEventLoop());
        return _supportSuspension && (state == ST_SUSPENDED || state == ST_SUSPENDING)
                                  && !hasTasks() && nextScheduledTaskDeadlineNanos() == -1;
    }

    /**
     * Confirm that the shutdown if the instance should be done now!
     */
    protected bool confirmShutdown()
    {
        if (!isShuttingDown())
        {
            return false;
        }

        if (!inEventLoop())
        {
            throw new InvalidOperationException("must be invoked from an event loop");
        }

        cancelScheduledTasks();

        if (gracefulShutdownStartTime == 0)
        {
            gracefulShutdownStartTime = getCurrentTimeNanos();
        }

        if (runAllTasks() || runShutdownHooks())
        {
            if (isShutdown())
            {
                // IExecutor shut down - no new tasks anymore.
                return true;
            }

            // There were tasks in the queue. Wait a little bit more until no tasks are queued for the quiet period or
            // terminate if the quiet period is 0.
            // See https://github.com/netty/netty/issues/4241
            if (_gracefulShutdownQuietPeriod.get() == 0)
            {
                return true;
            }

            _taskQueue.TryEnqueue(WAKEUP_TASK);
            return false;
        }

        long nanoTime = getCurrentTimeNanos();

        if (isShutdown() || nanoTime - gracefulShutdownStartTime > _gracefulShutdownTimeout.get())
        {
            return true;
        }

        if (nanoTime - lastExecutionTime <= _gracefulShutdownQuietPeriod.get())
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

    public override bool awaitTermination(TimeSpan timeout)
    {
        if (inEventLoop())
        {
            throw new InvalidOperationException("cannot await termination of the current thread");
        }

        _threadLock.Wait(timeout);

        return isTerminated();
    }

    public override void execute(IRunnable task)
    {
        execute0(task);
    }

    public override void lazyExecute(IRunnable task)
    {
        lazyExecute0(task);
    }

    private void execute0(IRunnable task)
    {
        ObjectUtil.checkNotNull(task, "task");
        execute(task, wakesUpForTask(task));
    }

    private void lazyExecute0(IRunnable task)
    {
        execute(ObjectUtil.checkNotNull(task, "task"), false);
    }

    protected override void scheduleRemoveScheduled(IScheduledTask task)
    {
        ObjectUtil.checkNotNull(task, "task");
        int currentState = _state.get();
        if (_supportSuspension && currentState == ST_SUSPENDED)
        {
            // In the case of scheduling for removal we need to also ensure we will recover the "suspend" state
            // after it if it was set before. Otherwise we will always end up "unsuspending" things on cancellation
            // which is not optimal.
            execute(new AnonymousRunnable(() =>
            {
                task.run();
                if (canSuspend(ST_SUSPENDED))
                {
                    // Try suspending again to recover the state before we submitted the new task that will
                    // handle cancellation itself.
                    trySuspend();
                }
            }), true);
        }
        else
        {
            // task will remove itself from scheduled task queue when it runs
            execute(task, false);
        }
    }

    private void execute(IRunnable task, bool immediate)
    {
        bool inEventLoop = this.inEventLoop();
        addTask(task);
        if (!inEventLoop)
        {
            startThread();
            if (isShutdown())
            {
                bool reject = false;
                try
                {
                    if (removeTask(task))
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
                    SingleThreadEventExecutor.reject();
                }
            }
        }

        if (!_addTaskWakesUp && immediate)
        {
            wakeup(inEventLoop);
        }
    }

    public override T invokeAny<T>(ICollection<T> tasks)
    {
        throwIfInEventLoop("invokeAny");
        return base.invokeAny(tasks);
    }

    public override T invokeAny<T>(ICollection<T> tasks, TimeSpan timeout)
    {
        throwIfInEventLoop("invokeAny");
        return base.invokeAny(tasks, timeout);
    }

    public override List<Task<T>> invokeAll<T>(ICollection<T> tasks)
    {
        throwIfInEventLoop("invokeAll");
        return base.invokeAll(tasks);
    }

    public override List<Task<T>> invokeAll<T>(ICollection<T> tasks, TimeSpan timeout)
    {
        throwIfInEventLoop("invokeAll");
        return base.invokeAll(tasks, timeout);
    }

    private void throwIfInEventLoop(string method)
    {
        if (inEventLoop())
        {
            throw new RejectedExecutionException("Calling " + method + " from within the EventLoop is not allowed");
        }
    }

    /**
     * Returns the {@link IThreadProperties} of the {@link Thread} that powers the {@link SingleThreadEventExecutor}.
     * If the {@link SingleThreadEventExecutor} is not started yet, this operation will start it and block until
     * it is fully started.
     */
    public IThreadProperties threadProperties()
    {
        IThreadProperties threadProperties = _threadProperties.get();
        if (threadProperties == null)
        {
            Thread thread = _thread;
            if (thread == null)
            {
                Debug.Assert(!inEventLoop());
                submit(NOOP_TASK).Wait();
                thread = _thread;
                Debug.Assert(thread != null);
            }

            threadProperties = new DefaultThreadProperties(thread);
            if (!_threadProperties.compareAndSet(null, threadProperties))
            {
                threadProperties = _threadProperties.get();
            }
        }

        return threadProperties;
    }


    /**
     * Can be overridden to control which tasks require waking the {@link IEventExecutor} thread
     * if it is waiting so that they can be run immediately.
     */
    protected bool wakesUpForTask(IRunnable task)
    {
        return true;
    }

    protected static void reject()
    {
        throw new RejectedExecutionException("event executor terminated");
    }

    /**
     * Offers the task to the associated {@link IRejectedExecutionHandler}.
     *
     * @param task to reject.
     */
    protected void reject(IRunnable task)
    {
        _rejectedExecutionHandler.rejected(task, this);
    }

    // ScheduledExecutorService implementation
    private static readonly long SCHEDULE_PURGE_INTERVAL = (long)TimeSpan.FromSeconds(1).TotalNanoseconds;

    private void startThread()
    {
        int currentState = _state.get();
        if (currentState == ST_NOT_STARTED || currentState == ST_SUSPENDED)
        {
            if (_state.compareAndSet(currentState, ST_STARTED))
            {
                bool success = false;
                try
                {
                    doStartThread();
                    success = true;
                }
                finally
                {
                    if (!success)
                    {
                        _state.compareAndSet(ST_STARTED, ST_NOT_STARTED);
                    }
                }
            }
        }
    }

    private bool ensureThreadStarted(int oldState)
    {
        if (oldState == ST_NOT_STARTED || oldState == ST_SUSPENDED)
        {
            try
            {
                doStartThread();
            }
            catch (Exception cause)
            {
                _state.set(ST_TERMINATED);
                _terminationFuture.SetException(cause);

                if (!(cause is OutOfMemoryException || cause is StackOverflowException || cause is ThreadAbortException))
                {
                    // Also rethrow as it may be an OOME for example
                    PlatformDependent.throwException(cause);
                }

                return true;
            }
        }

        return false;
    }

    private void doStartThread()
    {
        _executor.execute(new AnonymousRunnable(doStartThreadInternal));
    }

    private void doStartThreadInternal()
    {
        lock (_processingLock)
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
            updateLastExecutionTime();
            bool suspend = false;
            try
            {
                for (;;)
                {
                    run();
                    success = true;

                    int currentState = _state.get();
                    if (canSuspend(currentState))
                    {
                        if (!_state.compareAndSet(ST_SUSPENDING, ST_SUSPENDED))
                        {
                            // Try again as the CAS failed.
                            continue;
                        }

                        if (!canSuspend(ST_SUSPENDED) && _state.compareAndSet(ST_SUSPENDED, ST_STARTED))
                        {
                            // Seems like there was something added to the task queue again in the meantime but we
                            // were able to re-engage this thread as the event loop thread.
                            continue;
                        }

                        suspend = true;
                    }

                    break;
                }
            }
            catch (Exception t)
            {
                unexpectedException = t;
                logger.warn("Unexpected exception from an event executor: ", t);
            }
            finally
            {
                bool shutdown = !suspend;
                if (shutdown)
                {
                    for (;;)
                    {
                        // We are re-fetching the state as it might have been shutdown in the meantime.
                        int oldState = _state.get();
                        if (oldState >= ST_SHUTTING_DOWN || _state.compareAndSet(oldState, ST_SHUTTING_DOWN))
                        {
                            break;
                        }
                    }

                    if (success && gracefulShutdownStartTime == 0)
                    {
                        // Check if confirmShutdown() was called at the end of the loop.
                        if (logger.isErrorEnabled())
                        {
                            logger.error("Buggy " + nameof(IEventExecutor) + " implementation; " +
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
                            if (confirmShutdown())
                            {
                                break;
                            }
                        }

                        // Now we want to make sure no more tasks can be added from this point. This is
                        // achieved by switching the state. Any new tasks beyond this point will be rejected.
                        for (;;)
                        {
                            int currentState = _state.get();
                            if (currentState >= ST_SHUTDOWN || _state.compareAndSet(currentState, ST_SHUTDOWN))
                            {
                                break;
                            }
                        }

                        // We have the final set of tasks in the queue now, no more can be added, run all remaining.
                        // No need to loop here, this is the final pass.
                        confirmShutdown();
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
                                cleanup();
                            }
                            finally
                            {
                                // Lets remove all FastThreadLocals for the Thread as we are about to terminate and
                                // notify the future. The user may block on the future and once it unblocks the JVM
                                // may terminate and start unloading classes.
                                // See https://github.com/netty/netty/issues/6596.
                                FastThreadLocal.removeAll();

                                _state.set(ST_TERMINATED);
                                _threadLock.Signal();
                                int numUserTasks = drainTasks();
                                if (numUserTasks > 0 && logger.isWarnEnabled())
                                {
                                    logger.warn("An event executor terminated with " +
                                                "non-empty task queue (" + numUserTasks + ')');
                                }

                                if (unexpectedException == null)
                                {
                                    _terminationFuture.SetResult(null);
                                }
                                else
                                {
                                    _terminationFuture.SetException(unexpectedException);
                                }
                            }
                        }
                        else
                        {
                            // Lets remove all FastThreadLocals for the Thread as we are about to terminate it.
                            FastThreadLocal.removeAll();

                            // Reset the stored threadProperties in case of suspension.
                            _threadProperties.set(null);
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

    private int drainTasks()
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