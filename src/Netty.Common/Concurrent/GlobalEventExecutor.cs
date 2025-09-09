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
using System.Threading;
using System.Threading.Tasks;
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

    private readonly BlockingCollection<IRunnable> taskQueue = new BlockingCollection<IRunnable>();

    private IScheduledTask<Void> quietPeriodTask = null;
    // private ScheduledFutureTask<Void> quietPeriodTask = new ScheduledFutureTask<Void>(
    //         this, Executors.<Void>callable(new IRunnable() {
    //     @Override
    //     public void run() {
    //         // NOOP
    //     }
    // }, null),
    //         // note: the getCurrentTimeNanos() call here only works because this is a final class, otherwise the method
    //         // could be overridden leading to unsafe initialization here!
    //         deadlineNanos(getCurrentTimeNanos(), SCHEDULE_QUIET_PERIOD_INTERVAL),
    //         -SCHEDULE_QUIET_PERIOD_INTERVAL
    // );

    // because the GlobalEventExecutor is a singleton, tasks submitted to it can come from arbitrary threads and this
    // can trigger the creation of a thread from arbitrary thread groups; for this reason, the thread factory must not
    // be sticky about its thread group
    // visible for testing
    private IThreadFactory threadFactory;
    private readonly TaskRunner taskRunner = new TaskRunner();
    private readonly AtomicBoolean started = new AtomicBoolean();
    volatile Thread thread;

    private readonly TaskCompletionSource<object> _terminationFuture;

    static GlobalEventExecutor()
    {
        int quietPeriod = SystemPropertyUtil.getInt("io.netty.globalEventExecutor.quietPeriodSeconds", 1);
        if (quietPeriod <= 0)
        {
            quietPeriod = 1;
        }

        logger.debug("-Dio.netty.globalEventExecutor.quietPeriodSeconds: {}", quietPeriod);

        SCHEDULE_QUIET_PERIOD_INTERVAL = quietPeriod * PreciseTimer.NanosecondsPerSecond;
    }

    private GlobalEventExecutor()
    {
        scheduledTaskQueue().TryEnqueue(quietPeriodTask);
        threadFactory = ThreadExecutorMap.apply(new DefaultThreadFactory(
            DefaultThreadFactory.toPoolName(GetType()), false, ThreadPriority.Normal), this);


        NotSupportedException terminationFailure = new NotSupportedException();
        ThrowableUtil.unknownStackTrace(msg => new NotSupportedException(msg), typeof(GlobalEventExecutor), "terminationAsync");
        _terminationFuture = new FailedFuture<object>(this, terminationFailure);
    }

    /**
     * Take the next {@link IRunnable} from the task queue and so will block if no task is currently present.
     *
     * @return {@code null} if the executor thread has been interrupted or waken up.
     */
    public IRunnable takeTask()
    {
        BlockingCollection<IRunnable> taskQueue = this.taskQueue;
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
            taskQueue.Add(scheduledTask);
            scheduledTask = pollScheduledTask(nanoTime);
        }
    }

    /**
     * Return the number of tasks that are pending for processing.
     */
    public int pendingTasks()
    {
        return taskQueue.Count;
    }

    /**
     * Add a task to the task queue, or throws a {@link RejectedExecutionException} if this instance was shutdown
     * before.
     */
    private void addTask(IRunnable task)
    {
        taskQueue.Add(ObjectUtil.checkNotNull(task, "task"));
    }

    public override bool inEventLoop(Thread thread)
    {
        return thread == this.thread;
    }

    public override Task shutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
    {
        return terminationAsync();
    }

    public override Task terminationAsync()
    {
        return _terminationFuture.Task;
    }

    [Obsolete]
    public override void shutdown()
    {
        throw new NotSupportedException();
    }

    public override bool isShuttingDown()
    {
        return false;
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
        Thread thread = this.thread;
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
        if (started.compareAndSet(false, true))
        {
            Thread callingThread = Thread.CurrentThread;
            ClassLoader parentCCL = AccessController.doPrivileged(new PrivilegedAction<ClassLoader>()
            {
                @Override
                public ClassLoader run() {
                return callingThread.getContextClassLoader();
            }
            });
            // Avoid calling classloader leaking through Thread.inheritedAccessControlContext.
            setContextClassLoader(callingThread, null);
            try
            {
                Thread t = threadFactory.newThread(taskRunner);
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
            }
            finally
            {
                setContextClassLoader(callingThread, parentCCL);
            }
        }
    }

    private static void setContextClassLoader(Thread t, ClassLoader cl)
    {
        AccessController.doPrivileged(new PrivilegedAction<Void>()
        {
            @Override
            public Void run() {
            t.setContextClassLoader(cl);
            return null;
        }
        });
    }
}