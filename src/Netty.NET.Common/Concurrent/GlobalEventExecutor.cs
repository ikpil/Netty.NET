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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common.Concurrent;

/**
 * Single-thread singleton {@link EventExecutor}.  It starts the thread automatically and stops it when there is no
 * task pending in the task queue for {@code io.netty.globalEventExecutor.quietPeriodSeconds} second
 * (default is 1 second).  Please note it is not scalable to schedule large number of tasks to this executor;
 * use a dedicated executor.
 */
public sealed class GlobalEventExecutor : AbstractScheduledEventExecutor, IOrderedEventExecutor
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(GlobalEventExecutor));

    private static readonly long SCHEDULE_QUIET_PERIOD_INTERVAL;

    public static readonly GlobalEventExecutor INSTANCE;

    private readonly LinkedBlockingQueue<Action> _taskQueue = new(int.MaxValue, ReferenceEqualityComparer.Instance);

    private readonly IScheduledWork _quietPeriodTask;
    private readonly Action _quietPeriodCallback;

    // because the GlobalEventExecutor is a singleton, tasks submitted to it can come from arbitrary threads and this
    // can trigger the creation of a thread from arbitrary thread groups; for this reason, the thread factory must not
    // be sticky about its thread group
    // visible for testing
    internal readonly IThreadFactory _threadFactory;
    private readonly TaskRunner _taskRunner;
    private readonly AtomicBoolean _started = new AtomicBoolean();
    internal volatile Thread _thread;

    private readonly Task _terminationTask;

    static GlobalEventExecutor()
    {
        int quietPeriod = SystemPropertyUtil.GetInt("io.netty.globalEventExecutor.quietPeriodSeconds", 1);
        if (quietPeriod <= 0)
        {
            quietPeriod = 1;
        }

        logger.Debug("-Dio.netty.globalEventExecutor.quietPeriodSeconds: {}", quietPeriod);

        SCHEDULE_QUIET_PERIOD_INTERVAL = quietPeriod * SystemTimer.NanosecondsPerSecond;
        // CLR static field initializers precede the static constructor body.
        INSTANCE = new GlobalEventExecutor();
    }

    private GlobalEventExecutor() : base(null)
    {
        // note: the getCurrentTimeNanos() call here only works because this is a final class, otherwise the method
        // could be overridden leading to unsafe initialization here!
        // NOOP
        _quietPeriodTask = new NativeScheduledWork<object>(_ => null, default,
            DeadlineNanos(GetCurrentTimeNanos(),
                SCHEDULE_QUIET_PERIOD_INTERVAL),
            -SCHEDULE_QUIET_PERIOD_INTERVAL, GetCurrentTimeNanos, () => true,
            task => ScheduleFromEventLoop(task), task => RemoveScheduled(task), captureContext: false
        );
        _quietPeriodCallback = _quietPeriodTask.QueueCallback;
        ScheduledTaskQueue().TryEnqueue(_quietPeriodTask);
        _threadFactory = ThreadExecutorMap.Apply(new DefaultThreadFactory(
            GetType(), false, ThreadPriority.Normal), this);

        NotSupportedException terminationFailure = ThrowableUtil.UnknownStackTrace(new StacklessUnsupportedOperationException(),
            typeof(GlobalEventExecutor), "terminationFuture");
        _terminationTask = Task.FromException(terminationFailure);
        _taskRunner = new TaskRunner(this);
    }

    /**
     * Take the next {@link Runnable} from the task queue and so will block if no task is currently present.
     *
     * @return {@code null} if the executor thread has been interrupted or waken up.
     */
    internal Action TakeTask()
    {
        LinkedBlockingQueue<Action> taskQueue = _taskQueue;
        for (;;)
        {
            var scheduledTask = PeekScheduledTask();
            if (scheduledTask == null)
            {
                Action task = null;
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
                long delayNanos = scheduledTask.DelayNanos();
                Action task = null;
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
                    return task;
                }
            }
        }
    }

    private void FetchFromScheduledTaskQueue()
    {
        long nanoTime = GetCurrentTimeNanos();
        Action scheduledTask = PollScheduledTask(nanoTime);
        while (scheduledTask != null)
        {
            _taskQueue.Add(scheduledTask);
            scheduledTask = PollScheduledTask(nanoTime);
        }
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
    private void AddTask(Action task)
    {
        _taskQueue.Add(ObjectUtil.CheckNotNull(task, "task"));
    }

    public override bool InEventLoop(Thread thread)
    {
        return thread == _thread;
    }

    public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
    {
        return Termination;
    }

    public override bool IsShuttingDown()
    {
        return false;
    }

    public override Task Termination => _terminationTask;
    public override Task StopAsync() => Termination;

    [Obsolete]
    public override void Shutdown()
    {
        throw new NotSupportedException();
    }

    public override bool IsShutdown()
    {
        return false;
    }

    public override bool IsTerminated()
    {
        return false;
    }

    public override bool AwaitTermination(TimeSpan timeout)
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
    /// <remarks>
    /// Zero polls the current worker; <see cref="Timeout.InfiniteTimeSpan"/> waits without a deadline.
    /// Other negative durations are invalid. Positive durations share one bounded wait budget,
    /// including durations beyond the CLR Join millisecond range. Thread interruption propagates.
    /// This observes the captured worker and does not prevent another worker from starting.
    /// </remarks>
    public bool AwaitInactivity(TimeSpan timeout)
    {
        Thread thread = _thread;
        if (thread == null)
        {
            throw new InvalidOperationException("thread was not started");
        }

        return ThreadJoin.Join(thread, timeout);
    }

    public override void Execute(Action task)
    {
        AddTask(ObjectUtil.CheckNotNull(task, "task"));
        if (!InEventLoop())
        {
            StartThread();
        }
    }

    private void StartThread()
    {
        if (_started.CompareAndSet(false, true))
        {
            // CLR counterpart of clearing inherited JVM loader/security context.
            // Suppress only during creation/start and restore the submitting thread.
            if (ExecutionContext.IsFlowSuppressed())
            {
                StartThreadWithoutContext();
            }
            else
            {
                using (ExecutionContext.SuppressFlow())
                {
                    StartThreadWithoutContext();
                }
            }
        }
    }

    private void StartThreadWithoutContext()
    {
        Thread t = _threadFactory.NewThread(_taskRunner.Run);
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
        // Avoid calling classloader leaking through Thread.inheritedAccessControlContext.
        // CLR execution-context flow is suppressed by startThread().
        t.Start();
    }

    private sealed class TaskRunner
    {
        private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(TaskRunner));

        private readonly GlobalEventExecutor _this;

        public TaskRunner(GlobalEventExecutor executor)
        {
            _this = executor;
        }

        public void Run()
        {
            for (;;)
            {
                Action task = _this.TakeTask();
                if (task != null)
                {
                    try
                    {
                        RunTask(task);
                    }
                    catch (Exception t)
                    {
                        logger.Warn("Unexpected exception from the global event executor: ", t);
                    }

                    if (!ReferenceEquals(task, _this._quietPeriodCallback))
                    {
                        continue;
                    }
                }

                IQueue<IScheduledWork> scheduledTaskQueue = _this._scheduledTaskQueue;
                // Terminate if there is no task in the queue (except the noop task).
                if (_this._taskQueue.IsEmpty() && (scheduledTaskQueue == null || scheduledTaskQueue.Count == 1))
                {
                    // Mark the current thread as stopped.
                    // The following CAS must always success and must be uncontended,
                    // because only one thread should be running at the same time.
                    bool stopped = _this._started.CompareAndSet(true, false);
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
                    if (!_this._started.CompareAndSet(false, true))
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

    private sealed class StacklessUnsupportedOperationException : NotSupportedException
    {
        // Override fillInStackTrace() so we not populate the backtrace via a native call and so leak the
        // Classloader. As the GlobalEventExecutor.INSTANCE is a singleton and holds on to this exception via its
        // terminationFuture, a populated backtrace would pin the Classloader of whatever thread happened to trigger
        // the lazy initialization of INSTANCE (see https://github.com/netty/netty/issues/17128).
        // CLR synthetic text does not retain captured caller frames.
        public override string StackTrace => "at GlobalEventExecutor.terminationFuture(...)";
    }
}
