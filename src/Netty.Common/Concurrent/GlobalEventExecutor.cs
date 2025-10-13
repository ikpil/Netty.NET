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
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Functional;
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

    public static readonly GlobalEventExecutor INSTANCE = new GlobalEventExecutor();

    private readonly BlockingCollection<IRunnable> _taskQueue = new BlockingCollection<IRunnable>();

    private readonly IScheduledTask _quietPeriodTask;

    // because the GlobalEventExecutor is a singleton, tasks submitted to it can come from arbitrary threads and this
    // can trigger the creation of a thread from arbitrary thread groups; for this reason, the thread factory must not
    // be sticky about its thread group
    // visible for testing
    private readonly IThreadFactory _threadFactory;
    private readonly TaskRunner _taskRunner;
    private readonly AtomicBoolean _started = new AtomicBoolean();
    private volatile Thread _thread;

    private readonly TaskCompletionSource<Void> _terminationSource;

    static GlobalEventExecutor()
    {
        int quietPeriod = SystemPropertyUtil.getInt("io.netty.globalEventExecutor.quietPeriodSeconds", 1);
        if (quietPeriod <= 0)
        {
            quietPeriod = 1;
        }

        logger.debug("-Dio.netty.globalEventExecutor.quietPeriodSeconds: {}", quietPeriod);

        SCHEDULE_QUIET_PERIOD_INTERVAL = quietPeriod * SystemTimer.NanosecondsPerSecond;
    }

    private GlobalEventExecutor() : base(null)
    {
        scheduledTaskQueue().tryEnqueue(_quietPeriodTask);

        // // note: the getCurrentTimeNanos() call here only works because this is a final class, otherwise the method
        // // could be overridden leading to unsafe initialization here!
        _quietPeriodTask = new ScheduledRunnableTask(this, EmptyRunnable.Shared,
            deadlineNanos(getCurrentTimeNanos(),
                SCHEDULE_QUIET_PERIOD_INTERVAL),
            -SCHEDULE_QUIET_PERIOD_INTERVAL
        );
        _threadFactory = ThreadExecutorMap.apply(new DefaultThreadFactory(
            DefaultThreadFactory.toPoolName(GetType()), false, ThreadPriority.Normal), this);


        NotSupportedException terminationFailure = new NotSupportedException();
        ThrowableUtil.unknownStackTrace(msg => new NotSupportedException(msg), typeof(GlobalEventExecutor), "terminationAsync");
        _terminationSource = FailedFuture.Create<Void>(this, terminationFailure);
        _taskRunner = new TaskRunner(this);
    }

    /**
     * Take the next {@link IRunnable} from the task queue and so will block if no task is currently present.
     *
     * @return {@code null} if the executor thread has been interrupted or waken up.
     */
    public IRunnable takeTask()
    {
        BlockingCollection<IRunnable> taskQueue = _taskQueue;
        for (;;)
        {
            var scheduledTask = peekScheduledTask();
            if (scheduledTask == null)
            {
                IRunnable task = null;
                try
                {
                    task = taskQueue.Take();
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
                    fetchFromScheduledTaskQueue();
                    taskQueue.TryTake(out task);
                }

                if (task != null)
                {
                    return task;
                }
            }
        }
    }

    private void fetchFromScheduledTaskQueue()
    {
        long nanoTime = getCurrentTimeNanos();
        IRunnable scheduledTask = pollScheduledTask(nanoTime);
        while (scheduledTask != null)
        {
            _taskQueue.Add(scheduledTask);
            scheduledTask = pollScheduledTask(nanoTime);
        }
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
    private void addTask(IRunnable task)
    {
        _taskQueue.Add(ObjectUtil.checkNotNull(task, "task"));
    }

    public override bool inEventLoop(Thread thread)
    {
        return thread == _thread;
    }

    public override Task shutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
    {
        return terminationTask();
    }

    public override bool isShuttingDown()
    {
        throw new NotImplementedException();
    }

    public override Task terminationTask()
    {
        return _terminationSource.Task;
    }

    [Obsolete]
    public override void shutdown()
    {
        throw new NotSupportedException();
    }

    public override bool isShutdown()
    {
        return false;
    }

    public override bool isTerminated()
    {
        return false;
    }

    public override bool awaitTermination(TimeSpan timeout)
    {
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
    public bool awaitInactivity(TimeSpan timeout)
    {
        Thread thread = _thread;
        if (thread == null)
        {
            throw new InvalidOperationException("thread was not started");
        }

        thread.Join(timeout);
        return !thread.IsAlive;
    }

    public override void execute(IRunnable task)
    {
        execute0(task);
    }

    private void execute0(IRunnable task)
    {
        addTask(ObjectUtil.checkNotNull(task, "task"));
        if (!inEventLoop())
        {
            startThread();
        }
    }

    private void startThread()
    {
        if (_started.compareAndSet(false, true))
        {
            Thread t = _threadFactory.newThread(_taskRunner);
            // Set to null to ensure we not create classloader leaks by holds a strong reference to the inherited
            // classloader.
            // See:
            // - https://github.com/netty/netty/issues/7290
            // - https://bugs.openjdk.java.net/browse/JDK-7008595
            //setContextClassLoader(t, null);

            // Set the thread before starting it as otherwise inEventLoop() may return false and so produce
            // an assert error.
            // See https://github.com/netty/netty/issues/4357
            _thread = t;
            t.Start();
        }
    }


    private class TaskRunner : IRunnable
    {
        private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(TaskRunner));

        private readonly GlobalEventExecutor _this;

        public TaskRunner(GlobalEventExecutor executor)
        {
            _this = executor;
        }

        public void run()
        {
            for (;;)
            {
                IRunnable task = _this.takeTask();
                if (task != null)
                {
                    try
                    {
                        runTask(task);
                    }
                    catch (Exception t)
                    {
                        logger.warn("Unexpected exception from the global event executor: ", t);
                    }

                    if (task != _this._quietPeriodTask)
                    {
                        continue;
                    }
                }

                IQueue<IScheduledTask> scheduledTaskQueue = _this._scheduledTaskQueue;
                // Terminate if there is no task in the queue (except the noop task).
                if (_this._taskQueue.IsEmpty() && (scheduledTaskQueue == null || scheduledTaskQueue.Count == 1))
                {
                    // Mark the current thread as stopped.
                    // The following CAS must always success and must be uncontended,
                    // because only one thread should be running at the same time.
                    bool stopped = _this._started.compareAndSet(true, false);
                    Debug.Assert(stopped);

                    // Check if there are pending entries added by execute() or schedule*() while we do CAS above.
                    // Do not check scheduledTaskQueue because it is not thread-safe and can only be mutated from a
                    // TaskRunner actively running tasks.
                    if (_this._taskQueue.IsEmpty())
                    {
                        // A) No new task was added and thus there's nothing to handle
                        //    -> safe to terminate because there's nothing left to do
                        // B) A new thread started and handled all the new tasks.
                        //    -> safe to terminate the new thread will take care the rest
                        break;
                    }

                    // There are pending tasks added again.
                    if (!_this._started.compareAndSet(false, true))
                    {
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