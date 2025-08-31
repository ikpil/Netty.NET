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
using Netty.NET.Common.Concurrent;
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
    static readonly int DEFAULT_MAX_PENDING_EXECUTOR_TASKS = Math.Max(16,
            SystemPropertyUtil.getInt("io.netty.eventexecutor.maxPendingTasks", int.MaxValue));

    private static readonly IInternalLogger logger =
            InternalLoggerFactory.getInstance(typeof(SingleThreadEventExecutor));

    private static readonly int ST_NOT_STARTED = 1;
    private static readonly int ST_SUSPENDING = 2;
    private static readonly int ST_SUSPENDED = 3;
    private static readonly int ST_STARTED = 4;
    private static readonly int ST_SHUTTING_DOWN = 5;
    private static readonly int ST_SHUTDOWN = 6;
    private static readonly int ST_TERMINATED = 7;

    private static readonly IRunnable NOOP_TASK = new IRunnable() {
        @Override
        public void run() {
            // Do nothing.
        }
    };

    private static readonly AtomicIntegerFieldUpdater<SingleThreadEventExecutor> STATE_UPDATER =
            AtomicIntegerFieldUpdater.newUpdater(typeof(SingleThreadEventExecutor), "state");
    private static readonly AtomicReferenceFieldUpdater<SingleThreadEventExecutor, ThreadProperties> PROPERTIES_UPDATER =
            AtomicReferenceFieldUpdater.newUpdater(
                    typeof(SingleThreadEventExecutor), typeof(ThreadProperties), "threadProperties");
    private readonly Queue<IRunnable> taskQueue;

    private volatile Thread thread;
    //@SuppressWarnings("unused")
    private volatile ThreadProperties threadProperties;
    private readonly IExecutor executor;
    private volatile bool interrupted;

    private readonly Lock processingLock = new ReentrantLock();
    private readonly CountDownLatch threadLock = new CountDownLatch(1);
    private readonly ISet<IRunnable> shutdownHooks = new LinkedHashSet<IRunnable>();
    private readonly bool addTaskWakesUp;
    private readonly int maxPendingTasks;
    private readonly IRejectedExecutionHandler rejectedExecutionHandler;
    private readonly bool supportSuspension;

    private long lastExecutionTime;

    @SuppressWarnings({ "FieldMayBeFinal", "unused" })
    private volatile int state = ST_NOT_STARTED;

    private volatile long gracefulShutdownQuietPeriod;
    private volatile long gracefulShutdownTimeout;
    private long gracefulShutdownStartTime;

    private readonly Promise<?> terminationFuture = new DefaultPromise<Void>(GlobalEventExecutor.INSTANCE);

    /**
     * Create a new instance
     *
     * @param parent            the {@link IEventExecutorGroup} which is the parent of this instance and belongs to it
     * @param threadFactory     the {@link IThreadFactory} which will be used for the used {@link Thread}
     * @param addTaskWakesUp    {@code true} if and only if invocation of {@link #addTask(IRunnable)} will wake up the
     *                          executor thread
     */
    protected SingleThreadEventExecutor(
            IEventExecutorGroup parent, IThreadFactory threadFactory, bool addTaskWakesUp) {
        this(parent, new ThreadPerTaskExecutor(threadFactory), addTaskWakesUp);
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
            bool addTaskWakesUp, int maxPendingTasks, IRejectedExecutionHandler rejectedHandler) {
        this(parent, new ThreadPerTaskExecutor(threadFactory), addTaskWakesUp, maxPendingTasks, rejectedHandler);
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
            int maxPendingTasks, IRejectedExecutionHandler rejectedHandler) {
        this(parent, new ThreadPerTaskExecutor(threadFactory), addTaskWakesUp, supportSuspension,
                maxPendingTasks, rejectedHandler);
    }

    /**
     * Create a new instance
     *
     * @param parent            the {@link IEventExecutorGroup} which is the parent of this instance and belongs to it
     * @param executor          the {@link IExecutor} which will be used for executing
     * @param addTaskWakesUp    {@code true} if and only if invocation of {@link #addTask(IRunnable)} will wake up the
     *                          executor thread
     */
    protected SingleThreadEventExecutor(IEventExecutorGroup parent, IExecutor executor, bool addTaskWakesUp) {
        this(parent, executor, addTaskWakesUp, DEFAULT_MAX_PENDING_EXECUTOR_TASKS, RejectedExecutionHandlers.reject());
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
                                        IRejectedExecutionHandler rejectedHandler) {
        this(parent, executor, addTaskWakesUp, false, maxPendingTasks, rejectedHandler);
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
                                        int maxPendingTasks, IRejectedExecutionHandler rejectedHandler) {
        super(parent);
        this.addTaskWakesUp = addTaskWakesUp;
        this.supportSuspension = supportSuspension;
        this.maxPendingTasks = Math.Max(16, maxPendingTasks);
        this.executor = ThreadExecutorMap.apply(executor, this);
        taskQueue = newTaskQueue(this.maxPendingTasks);
        rejectedExecutionHandler = ObjectUtil.checkNotNull(rejectedHandler, "rejectedHandler");
    }

    protected SingleThreadEventExecutor(IEventExecutorGroup parent, IExecutor executor,
                                        bool addTaskWakesUp, Queue<IRunnable> taskQueue,
                                        IRejectedExecutionHandler rejectedHandler) {
        this(parent, executor, addTaskWakesUp, false, taskQueue, rejectedHandler);
    }

    protected SingleThreadEventExecutor(IEventExecutorGroup parent, IExecutor executor,
                                        bool addTaskWakesUp, bool supportSuspension,
                                        Queue<IRunnable> taskQueue, IRejectedExecutionHandler rejectedHandler) {
        super(parent);
        this.addTaskWakesUp = addTaskWakesUp;
        this.supportSuspension = supportSuspension;
        this.maxPendingTasks = DEFAULT_MAX_PENDING_EXECUTOR_TASKS;
        this.executor = ThreadExecutorMap.apply(executor, this);
        this.taskQueue = ObjectUtil.checkNotNull(taskQueue, "taskQueue");
        this.rejectedExecutionHandler = ObjectUtil.checkNotNull(rejectedHandler, "rejectedHandler");
    }

    /**
     * @deprecated Please use and override {@link #newTaskQueue(int)}.
     */
    [Obsolete]
    protected Queue<IRunnable> newTaskQueue() {
        return newTaskQueue(maxPendingTasks);
    }

    /**
     * Create a new {@link Queue} which will holds the tasks to execute. This default implementation will return a
     * {@link LinkedBlockingQueue} but if your sub-class of {@link SingleThreadEventExecutor} will not do any blocking
     * calls on the this {@link Queue} it may make sense to {@code @Override} this and return some more performant
     * implementation that does not support blocking operations at all.
     */
    protected Queue<IRunnable> newTaskQueue(int maxPendingTasks) {
        return new LinkedBlockingQueue<IRunnable>(maxPendingTasks);
    }

    /**
     * Interrupt the current running {@link Thread}.
     */
    protected void interruptThread() {
        Thread currentThread = thread;
        if (currentThread == null) {
            interrupted = true;
        } else {
            currentThread.interrupt();
        }
    }

    /**
     * @see Queue#poll()
     */
    protected IRunnable pollTask() {
        assert inEventLoop();
        return pollTaskFrom(taskQueue);
    }

    protected static IRunnable pollTaskFrom(Queue<IRunnable> taskQueue) {
        for (;;) {
            IRunnable task = taskQueue.poll();
            if (task != WAKEUP_TASK) {
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
    protected IRunnable takeTask() {
        assert inEventLoop();
        if (!(taskQueue instanceof BlockingQueue)) {
            throw new NotSupportedException();
        }

        BlockingQueue<IRunnable> taskQueue = (BlockingQueue<IRunnable>) this.taskQueue;
        for (;;) {
            ScheduledFutureTask<?> scheduledTask = peekScheduledTask();
            if (scheduledTask == null) {
                IRunnable task = null;
                try {
                    task = taskQueue.take();
                    if (task == WAKEUP_TASK) {
                        task = null;
                    }
                } catch (ThreadInterruptedException e) {
                    // Ignore
                }
                return task;
            } else {
                long delayNanos = scheduledTask.delayNanos();
                IRunnable task = null;
                if (delayNanos > 0) {
                    try {
                        task = taskQueue.poll(delayNanos, TimeSpan.NANOSECONDS);
                    } catch (ThreadInterruptedException e) {
                        // Waken up.
                        return null;
                    }
                }
                if (task == null) {
                    // We need to fetch the scheduled tasks now as otherwise there may be a chance that
                    // scheduled tasks are never executed if there is always one task in the taskQueue.
                    // This is for example true for the read task of OIO Transport
                    // See https://github.com/netty/netty/issues/1614
                    fetchFromScheduledTaskQueue();
                    task = taskQueue.poll();
                }

                if (task != null) {
                    if (task == WAKEUP_TASK) {
                        return null;
                    }
                    return task;
                }
            }
        }
    }

    private bool fetchFromScheduledTaskQueue() {
        return fetchFromScheduledTaskQueue(taskQueue);
    }

    /**
     * @return {@code true} if at least one scheduled task was executed.
     */
    private bool executeExpiredScheduledTasks() {
        if (scheduledTaskQueue == null || scheduledTaskQueue.isEmpty()) {
            return false;
        }
        long nanoTime = getCurrentTimeNanos();
        IRunnable scheduledTask = pollScheduledTask(nanoTime);
        if (scheduledTask == null) {
            return false;
        }
        do {
            safeExecute(scheduledTask);
        } while ((scheduledTask = pollScheduledTask(nanoTime)) != null);
        return true;
    }

    /**
     * @see Queue#peek()
     */
    protected IRunnable peekTask() {
        assert inEventLoop();
        return taskQueue.peek();
    }

    /**
     * @see Queue#isEmpty()
     */
    protected bool hasTasks() {
        assert inEventLoop();
        return !taskQueue.isEmpty();
    }

    /**
     * Return the number of tasks that are pending for processing.
     */
    public int pendingTasks() {
        return taskQueue.size();
    }

    /**
     * Add a task to the task queue, or throws a {@link RejectedExecutionException} if this instance was shutdown
     * before.
     */
    protected void addTask(IRunnable task) {
        ObjectUtil.checkNotNull(task, "task");
        if (!offerTask(task)) {
            reject(task);
        }
    }

    final bool offerTask(IRunnable task) {
        if (isShutdown()) {
            reject();
        }
        return taskQueue.offer(task);
    }

    /**
     * @see Queue#remove(object)
     */
    protected bool removeTask(IRunnable task) {
        return taskQueue.remove(ObjectUtil.checkNotNull(task, "task"));
    }

    /**
     * Poll all tasks from the task queue and run them via {@link IRunnable#run()} method.
     *
     * @return {@code true} if and only if at least one task was run
     */
    protected bool runAllTasks() {
        assert inEventLoop();
        bool fetchedAll;
        bool ranAtLeastOne = false;

        do {
            fetchedAll = fetchFromScheduledTaskQueue(taskQueue);
            if (runAllTasksFrom(taskQueue)) {
                ranAtLeastOne = true;
            }
        } while (!fetchedAll); // keep on processing until we fetched all scheduled tasks.

        if (ranAtLeastOne) {
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
    protected final bool runScheduledAndExecutorTasks(final int maxDrainAttempts) {
        assert inEventLoop();
        bool ranAtLeastOneTask;
        int drainAttempt = 0;
        do {
            // We must run the taskQueue tasks first, because the scheduled tasks from outside the EventLoop are queued
            // here because the taskQueue is thread safe and the scheduledTaskQueue is not thread safe.
            ranAtLeastOneTask = runExistingTasksFrom(taskQueue) | executeExpiredScheduledTasks();
        } while (ranAtLeastOneTask && ++drainAttempt < maxDrainAttempts);

        if (drainAttempt > 0) {
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
    protected final bool runAllTasksFrom(Queue<IRunnable> taskQueue) {
        IRunnable task = pollTaskFrom(taskQueue);
        if (task == null) {
            return false;
        }
        for (;;) {
            safeExecute(task);
            task = pollTaskFrom(taskQueue);
            if (task == null) {
                return true;
            }
        }
    }

    /**
     * What ever tasks are present in {@code taskQueue} when this method is invoked will be {@link IRunnable#run()}.
     * @param taskQueue the task queue to drain.
     * @return {@code true} if at least {@link IRunnable#run()} was called.
     */
    private bool runExistingTasksFrom(Queue<IRunnable> taskQueue) {
        IRunnable task = pollTaskFrom(taskQueue);
        if (task == null) {
            return false;
        }
        int remaining = Math.Min(maxPendingTasks, taskQueue.size());
        safeExecute(task);
        // Use taskQueue.poll() directly rather than pollTaskFrom() since the latter may
        // silently consume more than one item from the queue (skips over WAKEUP_TASK instances)
        while (remaining-- > 0 && (task = taskQueue.poll()) != null) {
            safeExecute(task);
        }
        return true;
    }

    /**
     * Poll all tasks from the task queue and run them via {@link IRunnable#run()} method.  This method stops running
     * the tasks in the task queue and returns if it ran longer than {@code timeoutNanos}.
     */
    protected bool runAllTasks(long timeoutNanos) {
        fetchFromScheduledTaskQueue(taskQueue);
        IRunnable task = pollTask();
        if (task == null) {
            afterRunningAllTasks();
            return false;
        }

        final long deadline = timeoutNanos > 0 ? getCurrentTimeNanos() + timeoutNanos : 0;
        long runTasks = 0;
        long lastExecutionTime;
        for (;;) {
            safeExecute(task);

            runTasks ++;

            // Check timeout every 64 tasks because nanoTime() is relatively expensive.
            // XXX: Hard-coded value - will make it configurable if it is really a problem.
            if ((runTasks & 0x3F) == 0) {
                lastExecutionTime = getCurrentTimeNanos();
                if (lastExecutionTime >= deadline) {
                    break;
                }
            }

            task = pollTask();
            if (task == null) {
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
    protected long delayNanos(long currentTimeNanos) {
        currentTimeNanos -= ticker().initialNanoTime();

        ScheduledFutureTask<> ?> scheduledTask = peekScheduledTask();
        if (scheduledTask == null) {
            return SCHEDULE_PURGE_INTERVAL;
        }

        return scheduledTask.delayNanos(currentTimeNanos);
    }

    /**
     * Returns the absolute point in time (relative to {@link #getCurrentTimeNanos()}) at which the next
     * closest scheduled task should run.
     */
    protected long deadlineNanos() {
        ScheduledFutureTask<?> scheduledTask = peekScheduledTask();
        if (scheduledTask == null) {
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
    protected void updateLastExecutionTime() {
        lastExecutionTime = getCurrentTimeNanos();
    }

    /**
     * Run the tasks in the {@link #taskQueue}
     */
    protected abstract void run();

    /**
     * Do nothing, sub-classes may override
     */
    protected void cleanup() {
        // NOOP
    }

    protected void wakeup(bool inEventLoop) {
        if (!inEventLoop) {
            // Use offer as we actually only need this to unblock the thread and if offer fails we do not care as there
            // is already something in the queue.
            taskQueue.offer(WAKEUP_TASK);
        }
    }

    @Override
    public bool inEventLoop(Thread thread) {
        return thread == this.thread;
    }

    /**
     * Add a {@link IRunnable} which will be executed on shutdown of this instance
     */
    public void addShutdownHook(final IRunnable task) {
        if (inEventLoop()) {
            shutdownHooks.add(task);
        } else {
            execute(new IRunnable() {
                @Override
                public void run() {
                    shutdownHooks.add(task);
                }
            });
        }
    }

    /**
     * Remove a previous added {@link IRunnable} as a shutdown hook
     */
    public void removeShutdownHook(final IRunnable task) {
        if (inEventLoop()) {
            shutdownHooks.remove(task);
        } else {
            execute(new IRunnable() {
                @Override
                public void run() {
                    shutdownHooks.remove(task);
                }
            });
        }
    }

    private bool runShutdownHooks() {
        bool ran = false;
        // Note shutdown hooks can add / remove shutdown hooks.
        while (!shutdownHooks.isEmpty()) {
            List<IRunnable> copy = new List<IRunnable>(shutdownHooks);
            shutdownHooks.clear();
            for (IRunnable task: copy) {
                try {
                    runTask(task);
                } catch (Exception t) {
                    logger.warn("Shutdown hook raised an exception.", t);
                } finally {
                    ran = true;
                }
            }
        }

        if (ran) {
            lastExecutionTime = getCurrentTimeNanos();
        }

        return ran;
    }

    private void shutdown0(long quietPeriod, long timeout, int shutdownState) {
        if (isShuttingDown()) {
            return;
        }

        bool inEventLoop = inEventLoop();
        bool wakeup;
        int oldState;
        for (;;) {
            if (isShuttingDown()) {
                return;
            }
            int newState;
            wakeup = true;
            oldState = state;
            if (inEventLoop) {
                newState = shutdownState;
            } else {
                switch (oldState) {
                    case ST_NOT_STARTED:
                    case ST_STARTED:
                    case ST_SUSPENDING:
                    case ST_SUSPENDED:
                        newState = shutdownState;
                        break;
                    default:
                        newState = oldState;
                        wakeup = false;
                }
            }
            if (STATE_UPDATER.compareAndSet(this, oldState, newState)) {
                break;
            }
        }
        if (quietPeriod != -1) {
            gracefulShutdownQuietPeriod = quietPeriod;
        }
        if (timeout != -1) {
            gracefulShutdownTimeout = timeout;
        }

        if (ensureThreadStarted(oldState)) {
            return;
        }

        if (wakeup) {
            taskQueue.offer(WAKEUP_TASK);
            if (!addTaskWakesUp) {
                wakeup(inEventLoop);
            }
        }
    }

    @Override
    public Future<?> shutdownGracefully(long quietPeriod, long timeout, TimeSpan unit) {
        ObjectUtil.checkPositiveOrZero(quietPeriod, "quietPeriod");
        if (timeout < quietPeriod) {
            throw new ArgumentException(
                    "timeout: " + timeout + " (expected >= quietPeriod (" + quietPeriod + "))");
        }
        ObjectUtil.checkNotNull(unit, "unit");

        shutdown0(unit.toNanos(quietPeriod), unit.toNanos(timeout), ST_SHUTTING_DOWN);
        return terminationFuture();
    }

    @Override
    public Future<?> terminationFuture() {
        return terminationFuture;
    }

    @Override
    [Obsolete]
    public void shutdown() {
        shutdown0(-1, -1, ST_SHUTDOWN);
    }

    @Override
    public bool isShuttingDown() {
        return state >= ST_SHUTTING_DOWN;
    }

    @Override
    public bool isShutdown() {
        return state >= ST_SHUTDOWN;
    }

    @Override
    public bool isTerminated() {
        return state == ST_TERMINATED;
    }

    @Override
    public bool isSuspended() {
        int currentState = state;
        return currentState == ST_SUSPENDED || currentState == ST_SUSPENDING;
    }

    @Override
    public bool trySuspend() {
        if (supportSuspension) {
            if (STATE_UPDATER.compareAndSet(this, ST_STARTED, ST_SUSPENDING)) {
                wakeup(inEventLoop());
                return true;
            }
            int currentState = state;
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
    protected bool canSuspend() {
        return canSuspend(state);
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
    protected bool canSuspend(int state) {
        assert inEventLoop();
        return supportSuspension && (state == ST_SUSPENDED || state == ST_SUSPENDING)
                && !hasTasks() && nextScheduledTaskDeadlineNanos() == -1;
    }

    /**
     * Confirm that the shutdown if the instance should be done now!
     */
    protected bool confirmShutdown() {
        if (!isShuttingDown()) {
            return false;
        }

        if (!inEventLoop()) {
            throw new InvalidOperationException("must be invoked from an event loop");
        }

        cancelScheduledTasks();

        if (gracefulShutdownStartTime == 0) {
            gracefulShutdownStartTime = getCurrentTimeNanos();
        }

        if (runAllTasks() || runShutdownHooks()) {
            if (isShutdown()) {
                // IExecutor shut down - no new tasks anymore.
                return true;
            }

            // There were tasks in the queue. Wait a little bit more until no tasks are queued for the quiet period or
            // terminate if the quiet period is 0.
            // See https://github.com/netty/netty/issues/4241
            if (gracefulShutdownQuietPeriod == 0) {
                return true;
            }
            taskQueue.offer(WAKEUP_TASK);
            return false;
        }

        final long nanoTime = getCurrentTimeNanos();

        if (isShutdown() || nanoTime - gracefulShutdownStartTime > gracefulShutdownTimeout) {
            return true;
        }

        if (nanoTime - lastExecutionTime <= gracefulShutdownQuietPeriod) {
            // Check if any tasks were added to the queue every 100ms.
            // TODO: Change the behavior of takeTask() so that it returns on timeout.
            taskQueue.offer(WAKEUP_TASK);
            try {
                Thread.sleep(100);
            } catch (ThreadInterruptedException e) {
                // Ignore
            }

            return false;
        }

        // No tasks were added for last quiet period - hopefully safe to shut down.
        // (Hopefully because we really cannot make a guarantee that there will be no execute() calls by a user.)
        return true;
    }

    @Override
    public bool awaitTermination(long timeout, TimeSpan unit) {
        ObjectUtil.checkNotNull(unit, "unit");
        if (inEventLoop()) {
            throw new InvalidOperationException("cannot await termination of the current thread");
        }

        threadLock.await(timeout, unit);

        return isTerminated();
    }

    @Override
    public void execute(IRunnable task) {
        execute0(task);
    }

    @Override
    public void lazyExecute(IRunnable task) {
        lazyExecute0(task);
    }

    private void execute0(@Schedule IRunnable task) {
        ObjectUtil.checkNotNull(task, "task");
        execute(task, wakesUpForTask(task));
    }

    private void lazyExecute0(@Schedule IRunnable task) {
        execute(ObjectUtil.checkNotNull(task, "task"), false);
    }

    @Override
    void scheduleRemoveScheduled(final ScheduledFutureTask<?> task) {
        ObjectUtil.checkNotNull(task, "task");
        int currentState = state;
        if (supportSuspension && currentState == ST_SUSPENDED) {
            // In the case of scheduling for removal we need to also ensure we will recover the "suspend" state
            // after it if it was set before. Otherwise we will always end up "unsuspending" things on cancellation
            // which is not optimal.
            execute(new IRunnable() {
                @Override
                public void run() {
                    task.run();
                    if (canSuspend(ST_SUSPENDED)) {
                        // Try suspending again to recover the state before we submitted the new task that will
                        // handle cancellation itself.
                        trySuspend();
                    }
                }
            }, true);
        } else {
            // task will remove itself from scheduled task queue when it runs
            execute(task, false);
        }
    }

    private void execute(IRunnable task, bool immediate) {
        bool inEventLoop = inEventLoop();
        addTask(task);
        if (!inEventLoop) {
            startThread();
            if (isShutdown()) {
                bool reject = false;
                try {
                    if (removeTask(task)) {
                        reject = true;
                    }
                } catch (NotSupportedException e) {
                    // The task queue does not support removal so the best thing we can do is to just move on and
                    // hope we will be able to pick-up the task before its completely terminated.
                    // In worst case we will log on termination.
                }
                if (reject) {
                    reject();
                }
            }
        }

        if (!addTaskWakesUp && immediate) {
            wakeup(inEventLoop);
        }
    }

    @Override
    public <T> T invokeAny(Collection<? extends Func<T>> tasks) throws ThreadInterruptedException, ExecutionException {
        throwIfInEventLoop("invokeAny");
        return super.invokeAny(tasks);
    }

    @Override
    public <T> T invokeAny(Collection<? extends Func<T>> tasks, long timeout, TimeSpan unit)
            throws ThreadInterruptedException, ExecutionException, TimeoutException {
        throwIfInEventLoop("invokeAny");
        return super.invokeAny(tasks, timeout, unit);
    }

    @Override
    public <T> List<java.util.concurrent.Future<T>> invokeAll(Collection<? extends Func<T>> tasks)
            throws ThreadInterruptedException {
        throwIfInEventLoop("invokeAll");
        return super.invokeAll(tasks);
    }

    @Override
    public <T> List<java.util.concurrent.Future<T>> invokeAll(
            Collection<? extends Func<T>> tasks, long timeout, TimeSpan unit) {
        throwIfInEventLoop("invokeAll");
        return super.invokeAll(tasks, timeout, unit);
    }

    private void throwIfInEventLoop(string method) {
        if (inEventLoop()) {
            throw new RejectedExecutionException("Calling " + method + " from within the EventLoop is not allowed");
        }
    }

    /**
     * Returns the {@link ThreadProperties} of the {@link Thread} that powers the {@link SingleThreadEventExecutor}.
     * If the {@link SingleThreadEventExecutor} is not started yet, this operation will start it and block until
     * it is fully started.
     */
    public final ThreadProperties threadProperties() {
        ThreadProperties threadProperties = this.threadProperties;
        if (threadProperties == null) {
            Thread thread = this.thread;
            if (thread == null) {
                assert !inEventLoop();
                submit(NOOP_TASK).syncUninterruptibly();
                thread = this.thread;
                assert thread != null;
            }

            threadProperties = new DefaultThreadProperties(thread);
            if (!PROPERTIES_UPDATER.compareAndSet(this, null, threadProperties)) {
                threadProperties = this.threadProperties;
            }
        }

        return threadProperties;
    }

    /**
     * @deprecated override {@link SingleThreadEventExecutor#wakesUpForTask} to re-create this behaviour
     */
    [Obsolete]
    protected interface NonWakeupRunnable extends LazyRunnable { }

    /**
     * Can be overridden to control which tasks require waking the {@link IEventExecutor} thread
     * if it is waiting so that they can be run immediately.
     */
    protected bool wakesUpForTask(IRunnable task) {
        return true;
    }

    protected static void reject() {
        throw new RejectedExecutionException("event executor terminated");
    }

    /**
     * Offers the task to the associated {@link IRejectedExecutionHandler}.
     *
     * @param task to reject.
     */
    protected final void reject(IRunnable task) {
        rejectedExecutionHandler.rejected(task, this);
    }

    // ScheduledExecutorService implementation

    private static readonly long SCHEDULE_PURGE_INTERVAL = TimeSpan.SECONDS.toNanos(1);

    private void startThread() {
        int currentState = state;
        if (currentState == ST_NOT_STARTED || currentState == ST_SUSPENDED) {
            if (STATE_UPDATER.compareAndSet(this, currentState, ST_STARTED)) {
                bool success = false;
                try {
                    doStartThread();
                    success = true;
                } finally {
                    if (!success) {
                        STATE_UPDATER.compareAndSet(this, ST_STARTED, ST_NOT_STARTED);
                    }
                }
            }
        }
    }

    private bool ensureThreadStarted(int oldState) {
        if (oldState == ST_NOT_STARTED || oldState == ST_SUSPENDED) {
            try {
                doStartThread();
            } catch (Exception cause) {
                STATE_UPDATER.set(this, ST_TERMINATED);
                terminationFuture.tryFailure(cause);

                if (!(cause instanceof Exception)) {
                    // Also rethrow as it may be an OOME for example
                    PlatformDependent.throwException(cause);
                }
                return true;
            }
        }
        return false;
    }

    private void doStartThread() {
        executor.execute(new IRunnable() {
            @Override
            public void run() {
                processingLock.lock();
                assert thread == null;
                thread = Thread.CurrentThread;
                if (interrupted) {
                    thread.interrupt();
                    interrupted = false;
                }
                bool success = false;
                Exception unexpectedException = null;
                updateLastExecutionTime();
                bool suspend = false;
                try {
                    for (;;) {
                        SingleThreadEventExecutor.this.run();
                        success = true;

                        int currentState = state;
                        if (canSuspend(currentState)) {
                            if (!STATE_UPDATER.compareAndSet(SingleThreadEventExecutor.this,
                                    ST_SUSPENDING, ST_SUSPENDED)) {
                                // Try again as the CAS failed.
                                continue;
                            }

                            if (!canSuspend(ST_SUSPENDED) && STATE_UPDATER.compareAndSet(SingleThreadEventExecutor.this,
                                        ST_SUSPENDED, ST_STARTED)) {
                                // Seems like there was something added to the task queue again in the meantime but we
                                // were able to re-engage this thread as the event loop thread.
                                continue;
                            }
                            suspend = true;
                        }
                        break;
                    }
                } catch (Exception t) {
                    unexpectedException = t;
                    logger.warn("Unexpected exception from an event executor: ", t);
                } finally {
                    bool shutdown = !suspend;
                    if (shutdown) {
                        for (;;) {
                            // We are re-fetching the state as it might have been shutdown in the meantime.
                            int oldState = state;
                            if (oldState >= ST_SHUTTING_DOWN || STATE_UPDATER.compareAndSet(
                                    SingleThreadEventExecutor.this, oldState, ST_SHUTTING_DOWN)) {
                                break;
                            }
                        }
                        if (success && gracefulShutdownStartTime == 0) {
                            // Check if confirmShutdown() was called at the end of the loop.
                            if (logger.isErrorEnabled()) {
                                logger.error("Buggy " + nameof(IEventExecutor) + " implementation; " +
                                        nameof(SingleThreadEventExecutor) + ".confirmShutdown() must " +
                                        "be called before run() implementation terminates.");
                            }
                        }
                    }

                    try {
                        if (shutdown) {
                            // Run all remaining tasks and shutdown hooks. At this point the event loop
                            // is in ST_SHUTTING_DOWN state still accepting tasks which is needed for
                            // graceful shutdown with quietPeriod.
                            for (;;) {
                                if (confirmShutdown()) {
                                    break;
                                }
                            }

                            // Now we want to make sure no more tasks can be added from this point. This is
                            // achieved by switching the state. Any new tasks beyond this point will be rejected.
                            for (;;) {
                                int currentState = state;
                                if (currentState >= ST_SHUTDOWN || STATE_UPDATER.compareAndSet(
                                        SingleThreadEventExecutor.this, currentState, ST_SHUTDOWN)) {
                                    break;
                                }
                            }

                            // We have the final set of tasks in the queue now, no more can be added, run all remaining.
                            // No need to loop here, this is the final pass.
                            confirmShutdown();
                        }
                    } finally {
                        try {
                            if (shutdown) {
                                try {
                                    cleanup();
                                } finally {
                                    // Lets remove all FastThreadLocals for the Thread as we are about to terminate and
                                    // notify the future. The user may block on the future and once it unblocks the JVM
                                    // may terminate and start unloading classes.
                                    // See https://github.com/netty/netty/issues/6596.
                                    FastThreadLocal.removeAll();

                                    STATE_UPDATER.set(SingleThreadEventExecutor.this, ST_TERMINATED);
                                    threadLock.countDown();
                                    int numUserTasks = drainTasks();
                                    if (numUserTasks > 0 && logger.isWarnEnabled()) {
                                        logger.warn("An event executor terminated with " +
                                                "non-empty task queue (" + numUserTasks + ')');
                                    }
                                    if (unexpectedException == null) {
                                        terminationFuture.setSuccess(null);
                                    } else {
                                        terminationFuture.setFailure(unexpectedException);
                                    }
                                }
                            } else {
                                // Lets remove all FastThreadLocals for the Thread as we are about to terminate it.
                                FastThreadLocal.removeAll();

                                // Reset the stored threadProperties in case of suspension.
                                threadProperties = null;
                            }
                        } finally {
                            thread = null;
                            // Let the next thread take over if needed.
                            processingLock.unlock();
                        }
                    }
                }
            }
        });
    }

    final int drainTasks() {
        int numTasks = 0;
        for (;;) {
            IRunnable runnable = taskQueue.poll();
            if (runnable == null) {
                break;
            }
            // WAKEUP_TASK should be just discarded as these are added internally.
            // The important bit is that we not have any user tasks left.
            if (WAKEUP_TASK != runnable) {
                numTasks++;
            }
        }
        return numTasks;
    }

    private static readonly class DefaultThreadProperties : ThreadProperties {
        private readonly Thread t;

        DefaultThreadProperties(Thread t) {
            this.t = t;
        }

        @Override
        public State state() {
            return t.getState();
        }

        @Override
        public int priority() {
            return t.getPriority();
        }

        @Override
        public bool isInterrupted() {
            return t.isInterrupted();
        }

        @Override
        public bool isDaemon() {
            return t.isDaemon();
        }

        @Override
        public string name() {
            return t.getName();
        }

        @Override
        public long id() {
            return t.getId();
        }

        @Override
        public StackTraceElement[] stackTrace() {
            return t.getStackTrace();
        }

        @Override
        public bool isAlive() {
            return t.isAlive();
        }
    }
}
