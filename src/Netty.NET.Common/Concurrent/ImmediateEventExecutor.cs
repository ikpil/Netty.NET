/*
 * Copyright 2013 The Netty Project
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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common.Concurrent;

/**
 * Executes {@link Runnable} objects in the caller's thread. If the {@link #execute(Runnable)} is reentrant it will be
 * queued until the original {@link Runnable} finishes execution.
 * <p>
 * All {@link Throwable} objects thrown from {@link #execute(Runnable)} will be swallowed and logged. This is to ensure
 * that all queued {@link Runnable} objects have the chance to be run.
 */
public sealed class ImmediateEventExecutor : AbstractEventExecutor
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(ImmediateEventExecutor));
    public static readonly ImmediateEventExecutor INSTANCE = new ImmediateEventExecutor();

    /**
     * A Runnable will be queued if we are executing a Runnable. This is to prevent a {@link StackOverflowError}.
     */
    private static readonly FastThreadLocal<Queue<IRunnable>> DELAYED_RUNNABLES =
        new FastThreadLocalFunc<Queue<IRunnable>>(() => new Queue<IRunnable>());

    /**
     * Set to {@code true} if we are executing a runnable.
     */
    private static readonly StrongBox<bool> StrongFalse = new StrongBox<bool>(false);

    private static readonly StrongBox<bool> StrongTrue = new StrongBox<bool>(true);
    private static readonly FastThreadLocal<StrongBox<bool>> RUNNING = new FastThreadLocalFunc<StrongBox<bool>>(() => StrongFalse);

    private readonly Task _terminationTask = Task.FromException(new NotSupportedException());

    private ImmediateEventExecutor() { }

    public override bool inEventLoop()
    {
        return true;
    }

    public override bool inEventLoop(Thread thread)
    {
        return true;
    }

    public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
    {
        return Termination;
    }

    public override Task Termination => _terminationTask;

    [Obsolete]
    public override void shutdown()
    {
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

    public override void execute(IRunnable command)
    {
        ObjectUtil.checkNotNull(command, "command");
        if (StrongFalse == RUNNING.get())
        {
            RUNNING.set(StrongTrue);
            try
            {
                command.run();
            }
            catch (Exception cause)
            {
                logger.info("Throwable caught while executing Runnable {}", command, cause);
            }
            finally
            {
                var delayedRunnables = DELAYED_RUNNABLES.get();
                IRunnable runnable;
                while (delayedRunnables.TryDequeue(out runnable) && null != runnable)
                {
                    try
                    {
                        runnable.run();
                    }
                    catch (Exception cause)
                    {
                        logger.info("Throwable caught while executing Runnable {}", runnable, cause);
                    }
                }

                RUNNING.set(StrongFalse);
            }
        }
        else
        {
            DELAYED_RUNNABLES.get().Enqueue(command);
        }
    }

}
