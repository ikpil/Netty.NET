/*
 * Copyright 2016 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;

internal sealed class NonStickyOrderedEventExecutor : AbstractEventExecutor, IRunnable, IOrderedEventExecutor
{
    private readonly IEventExecutor _executor;
    private readonly IQueue<IRunnable> tasks = PlatformDependent.newMpscQueue<IRunnable>();

    private const int NONE = 0;
    private const int SUBMITTED = 1;
    private const int RUNNING = 2;

    private readonly AtomicInteger _state = new AtomicInteger();
    private readonly int _maxTaskExecutePerRun;

    private readonly AtomicReference<Thread> _executingThread = new AtomicReference<Thread>();

    public NonStickyOrderedEventExecutor(IEventExecutor executor, int maxTaskExecutePerRun)
        : base(executor)
    {
        _executor = executor;
        _maxTaskExecutePerRun = maxTaskExecutePerRun;
    }

    public void run()
    {
        if (!_state.compareAndSet(SUBMITTED, RUNNING))
        {
            return;
        }

        Thread current = Thread.CurrentThread;
        _executingThread.set(current);
        for (;;)
        {
            int i = 0;
            // CLR cannot return from a finally block. Defer the original returns until it exits.
            bool done = false;
            try
            {
                for (; i < _maxTaskExecutePerRun; i++)
                {
                    tasks.tryDequeue(out var task);
                    if (task == null)
                    {
                        break;
                    }

                    safeExecute(task);
                }
            }
            finally
            {
                if (i == _maxTaskExecutePerRun)
                {
                    try
                    {
                        _state.set(SUBMITTED);
                        // Only set executingThread to null if no other thread did update it yet.
                        _executingThread.compareAndSet(current, null);
                        _executor.execute(this);
                        done = true; // done
                    }
                    catch (Exception ignore)
                    {
                        // Restore executingThread since we're continuing to execute tasks.
                        _executingThread.set(current);
                        // Reset the state back to running as we will keep on executing tasks.
                        _state.set(RUNNING);
                        // if an error happened we should just ignore it and let the loop run again as there is not
                        // much else we can do. Most likely this was triggered by a full task queue. In this case
                        // we just will run more tasks and try again later.
                    }
                }
                else
                {
                    _state.set(NONE);
                    // After setting the state to NONE, look at the tasks queue one more time.
                    // If it is empty, then we can return from this method.
                    // Otherwise, it means the producer thread has called execute(Runnable)
                    // and enqueued a task in between the tasks.poll() above and the state.set(NONE) here.
                    // There are two possible scenarios when this happens
                    //
                    // 1. The producer thread sees state == NONE, hence the compareAndSet(NONE, SUBMITTED)
                    //    is successfully setting the state to SUBMITTED. This mean the producer
                    //    will call / has called executor.execute(this). In this case, we can just return.
                    // 2. The producer thread don't see the state change, hence the compareAndSet(NONE, SUBMITTED)
                    //    returns false. In this case, the producer thread won't call executor.execute.
                    //    In this case, we need to change the state to RUNNING and keeps running.
                    //
                    // The above cases can be distinguished by performing a
                    // compareAndSet(NONE, RUNNING). If it returns "false", it is case 1; otherwise it is case 2.
                    if (tasks.isEmpty() || !_state.compareAndSet(NONE, RUNNING))
                    {
                        // Only set executingThread to null if no other thread did update it yet.
                        _executingThread.compareAndSet(current, null);
                        done = true; // done
                    }
                }
            }
            if (done)
            {
                return;
            }
        }
    }

    public override bool inEventLoop(Thread thread)
    {
        return _executingThread.get() == thread;
    }

    public override bool isShuttingDown()
    {
        return _executor.isShutdown();
    }

    public override IFuture<Netty.NET.Common.Concurrent.Void> shutdownGracefully(TimeSpan quietPeriod, TimeSpan timeout)
    {
        return _executor.shutdownGracefully(quietPeriod, timeout);
    }

    public override IFuture<Netty.NET.Common.Concurrent.Void> terminationFuture()
    {
        return _executor.terminationFuture();
    }

    public override void shutdown()
    {
        _executor.shutdown();
    }

    public override bool isShutdown()
    {
        return _executor.isShutdown();
    }

    public override bool isTerminated()
    {
        return _executor.isTerminated();
    }

    public override bool awaitTermination(TimeSpan timeout)
    {
        return _executor.awaitTermination(timeout);
    }

    public override void execute(IRunnable command)
    {
        if (!tasks.tryEnqueue(command))
        {
            throw new RejectedExecutionException();
        }

        if (_state.compareAndSet(NONE, SUBMITTED))
        {
            // Actually it could happen that the runnable was picked up in between but we not care to much and just
            // execute ourself. At worst this will be a NOOP when run() is called.
            _executor.execute(this);
        }
    }
}
