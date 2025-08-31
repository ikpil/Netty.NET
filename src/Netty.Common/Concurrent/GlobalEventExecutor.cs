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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common.Concurrent;

/**
 * Single-thread singleton {@link IEventExecutor}.  It starts the thread automatically and stops it when there is no
 * task pending in the task queue for {@code io.netty.globalEventExecutor.quietPeriodSeconds} second
 * (default is 1 second).  Please note it is not scalable to schedule large number of tasks to this executor;
 * use a dedicated executor.
 */
public class GlobalEventExecutor : AbstractScheduledEventExecutor, IOrderedEventExecutor 
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(GlobalEventExecutor));

    private static readonly long SCHEDULE_QUIET_PERIOD_INTERVAL;

    static GlobalEventExecutor()
    {
        int quietPeriod = SystemPropertyUtil.getInt("io.netty.globalEventExecutor.quietPeriodSeconds", 1);
        if (quietPeriod <= 0) {
            quietPeriod = 1;
        }
        logger.debug("-Dio.netty.globalEventExecutor.quietPeriodSeconds: {}", quietPeriod);

        SCHEDULE_QUIET_PERIOD_INTERVAL = quietPeriod * PreciseTimer.NanosecondsPerSecond;
    }

    public static readonly GlobalEventExecutor INSTANCE = new GlobalEventExecutor();

    private readonly BlockingCollection<IRunnable> taskQueue = new BlockingCollection<IRunnable>();
    final ScheduledFutureTask<Void> quietPeriodTask = new ScheduledFutureTask<Void>(
            this, Executors.<Void>callable(new IRunnable() {
        @Override
        public void run() {
            // NOOP
        }
    }, null),
            // note: the getCurrentTimeNanos() call here only works because this is a final class, otherwise the method
            // could be overridden leading to unsafe initialization here!
            deadlineNanos(getCurrentTimeNanos(), SCHEDULE_QUIET_PERIOD_INTERVAL),
            -SCHEDULE_QUIET_PERIOD_INTERVAL
    );

    // because the GlobalEventExecutor is a singleton, tasks submitted to it can come from arbitrary threads and this
    // can trigger the creation of a thread from arbitrary thread groups; for this reason, the thread factory must not
    // be sticky about its thread group
    // visible for testing
    private IThreadFactory threadFactory;
    private readonly TaskRunner taskRunner = new TaskRunner();
    private readonly AtomicBoolean started = new AtomicBoolean();
    volatile Thread thread;

    private readonly Future<object> terminationFuture;

    private GlobalEventExecutor() 
    {
        scheduledTaskQueue().add(quietPeriodTask);
        threadFactory = ThreadExecutorMap.apply(new DefaultThreadFactory(
                DefaultThreadFactory.toPoolName(GetType()), false, ThreadPriority.Normal), this);
        

        NotSupportedException terminationFailure = new NotSupportedException();
        ThrowableUtil.unknownStackTrace(msg => new NotSupportedException(msg), typeof(GlobalEventExecutor), "terminationAsync");
        terminationFuture = new FailedFuture<object>(this, terminationFailure);
    }

    /**
     * Take the next {@link IRunnable} from the task queue and so will block if no task is currently present.
     *
     * @return {@code null} if the executor thread has been interrupted or waken up.
     */
    IRunnable takeTask() {
        BlockingCollection<IRunnable> taskQueue = this.taskQueue;
        for (;;) {
            Concurrent.ScheduledFutureTask<> ?> scheduledTask = peekScheduledTask();
            if (scheduledTask == null) {
                IRunnable task = null;
                try {
                    task = taskQueue.take();
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
                    return task;
                }
            }
        }
    }

    private void fetchFromScheduledTaskQueue() {
        long nanoTime = getCurrentTimeNanos();
        IRunnable scheduledTask = pollScheduledTask(nanoTime);
        while (scheduledTask != null) {
            taskQueue.add(scheduledTask);
            scheduledTask = pollScheduledTask(nanoTime);
        }
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
    private void addTask(IRunnable task) {
        taskQueue.add(ObjectUtil.checkNotNull(task, "task"));
    }

    @Override
    public bool inEventLoop(Thread thread) {
        return thread == this.thread;
    }

    @Override
    public Task shutdownGracefullyAsync(long quietPeriod, long timeout, TimeSpan unit) {
        return terminationFuture();
    }

    @Override
    public Future<?> terminationFuture() {
        return terminationFuture;
    }

    @Override
    [Obsolete]
    public void shutdown() {
        throw new NotSupportedException();
    }

    @Override
    public bool isShuttingDown() {
        return false;
    }

    @Override
    public bool isShutdown() {
        return false;
    }

    @Override
    public bool isTerminated() {
        return false;
    }

    @Override
    public bool awaitTermination(long timeout, TimeSpan unit) {
        return false;
    }

    /**
     * Waits until the worker thread of this executor has no tasks left in its task queue and terminates itself.
     * Because a new worker thread will be started again when a new task is submitted, this operation is only useful
     * when you want to ensure that the worker thread is terminated <strong>after</strong> your application is shut
     * down and there's no chance of submitting a new task afterwards.
     *
     * @return {@code true} if and only if the worker thread has been terminated
     */
    public bool awaitInactivity(long timeout, TimeSpan unit) {
        ObjectUtil.checkNotNull(unit, "unit");

        final Thread thread = this.thread;
        if (thread == null) {
            throw new InvalidOperationException("thread was not started");
        }
        thread.join(unit.toMillis(timeout));
        return !thread.isAlive();
    }

    @Override
    public void execute(IRunnable task) {
        execute0(task);
    }

    private void execute0(@Schedule IRunnable task) {
        addTask(ObjectUtil.checkNotNull(task, "task"));
        if (!inEventLoop()) {
            startThread();
        }
    }

    private void startThread() {
        if (started.compareAndSet(false, true)) {
            final Thread callingThread = Thread.CurrentThread;
            ClassLoader parentCCL = AccessController.doPrivileged(new PrivilegedAction<ClassLoader>() {
                @Override
                public ClassLoader run() {
                    return callingThread.getContextClassLoader();
                }
            });
            // Avoid calling classloader leaking through Thread.inheritedAccessControlContext.
            setContextClassLoader(callingThread, null);
            try {
                final Thread t = threadFactory.newThread(taskRunner);
                // Set to null to ensure we not create classloader leaks by holds a strong reference to the inherited
                // classloader.
                // See:
                // - https://github.com/netty/netty/issues/7290
                // - https://bugs.openjdk.java.net/browse/JDK-7008595
                setContextClassLoader(t, null);

                // Set the thread before starting it as otherwise inEventLoop() may return false and so produce
                // an assert error.
                // See https://github.com/netty/netty/issues/4357
                thread = t;
                t.start();
            } finally {
                setContextClassLoader(callingThread, parentCCL);
            }
        }
    }

    private static void setContextClassLoader(final Thread t, final ClassLoader cl) {
        AccessController.doPrivileged(new PrivilegedAction<Void>() {
            @Override
            public Void run() {
                t.setContextClassLoader(cl);
                return null;
            }
        });
    }

    final class TaskRunner : IRunnable {
        @Override
        public void run() {
            for (;;) {
                IRunnable task = takeTask();
                if (task != null) {
                    try {
                        runTask(task);
                    } catch (Exception t) {
                        logger.warn("Unexpected exception from the global event executor: ", t);
                    }

                    if (task != quietPeriodTask) {
                        continue;
                    }
                }

                Queue< Concurrent.ScheduledFutureTask<> ?>> scheduledTaskQueue = GlobalEventExecutor.this.scheduledTaskQueue;
                // Terminate if there is no task in the queue (except the noop task).
                if (taskQueue.isEmpty() && (scheduledTaskQueue == null || scheduledTaskQueue.size() == 1)) {
                    // Mark the current thread as stopped.
                    // The following CAS must always success and must be uncontended,
                    // because only one thread should be running at the same time.
                    bool stopped = started.compareAndSet(true, false);
                    assert stopped;

                    // Check if there are pending entries added by execute() or schedule*() while we do CAS above.
                    // Do not check scheduledTaskQueue because it is not thread-safe and can only be mutated from a
                    // TaskRunner actively running tasks.
                    if (taskQueue.isEmpty()) {
                        // A) No new task was added and thus there's nothing to handle
                        //    -> safe to terminate because there's nothing left to do
                        // B) A new thread started and handled all the new tasks.
                        //    -> safe to terminate the new thread will take care the rest
                        break;
                    }

                    // There are pending tasks added again.
                    if (!started.compareAndSet(false, true)) {
                        // startThread() started a new thread and set 'started' to true.
                        // -> terminate this thread so that the new thread reads from taskQueue exclusively.
                        break;
                    }

                    // New tasks were added, but this worker was faster to set 'started' to true.
                    // i.e. a new worker thread was not started by startThread().
                    // -> keep this thread alive to handle the newly added entries.
                }
            }
        }
    }
}
