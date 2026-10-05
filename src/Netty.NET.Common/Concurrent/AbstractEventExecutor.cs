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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common.Concurrent;

/**
 * Abstract base class for {@link EventExecutor} implementations.
 */
public abstract class AbstractEventExecutor : IEventExecutor
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(AbstractEventExecutor));

    public static readonly TimeSpan DEFAULT_SHUTDOWN_QUIET_PERIOD = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan DEFAULT_SHUTDOWN_TIMEOUT = TimeSpan.FromSeconds(15);

    private readonly IEventExecutorGroup _parent;
    private readonly IReadOnlyCollection<IEventExecutor> _selfCollection;

    protected AbstractEventExecutor() : this(ObjectUtil.Null<IEventExecutorGroup>())
    {
    }

    protected AbstractEventExecutor(IEventExecutorGroup parent)
    {
        _selfCollection = new List<IEventExecutor> { this }.AsReadOnly();
        _parent = parent;
    }

    public virtual IEventExecutorGroup Parent()
    {
        return _parent;
    }

    public virtual Ticker Ticker()
    {
        return global::Netty.NET.Common.Concurrent.Ticker.SystemTicker();
    }

    public virtual bool IsExecutorThread(Thread thread)
    {
        return InEventLoop(thread);
    }

    public virtual bool InEventLoop()
    {
        return InEventLoop(Thread.CurrentThread);
    }

    public abstract bool InEventLoop(Thread thread);

    public abstract void Execute(Action task);

    // Stateful common work must still pass the public virtual Action hook.
    internal void Execute(IRunnable task) => Execute(ExecutorWork.Wrap(task));
    internal void LazyExecute(IRunnable task) => LazyExecute(ExecutorWork.Wrap(task));
    public abstract bool IsShutdown();
    public abstract bool IsTerminated();
    public abstract bool AwaitTermination(TimeSpan timeout);
    public abstract Task Termination { get; }

    public virtual IEventExecutor Next()
    {
        return this;
    }

    public virtual IEnumerable<IEventExecutor> Iterator()
    {
        return _selfCollection;
    }

    public virtual Task ShutdownGracefullyAsync()
    {
        return ShutdownGracefullyAsync(DEFAULT_SHUTDOWN_QUIET_PERIOD, DEFAULT_SHUTDOWN_TIMEOUT);
    }

    public abstract Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout);

    // Default for custom backends; concrete workers and wrappers select their
    // native shutdown policy directly. No additional lifecycle result is created.
    public virtual Task StopAsync()
    {
        Shutdown();
        return Termination;
    }

    /**
     * @deprecated {@link #shutdownGracefully(long, long, TimeUnit)} or {@link #shutdownGracefully()} instead.
     */
    [Obsolete]
    public abstract void Shutdown();

    public abstract bool IsShuttingDown();

    public virtual bool IsSuspended()
    {
        return false;
    }

    public virtual bool TrySuspend()
    {
        return false;
    }

    /**
     * Try to execute the given {@link Runnable} and just log if it throws a {@link Throwable}.
     */
    protected static void SafeExecute(IRunnable task)
    {
        try
        {
            RunTask(task);
        }
        catch (Exception t)
        {
            logger.Warn("A task raised an exception. Task: {}", task, t);
        }
    }

    protected static void RunTask(IRunnable task)
    {
        task.Run();
    }

    /**
     * Like {@link #execute(Runnable)} but does not guarantee the task will be run until either
     * a non-lazy task is executed or the executor is shut down.
     * <p>
     * The default implementation just delegates to {@link #execute(Runnable)}.
     * </p>
     */
    [UnstableApi]
    public virtual void LazyExecute(Action task)
    {
        Execute(task);
    }

    /**
     *  @deprecated override {@link SingleThreadEventExecutor#wakesUpForTask} to re-create this behaviour
     *
     */
    [Obsolete]
    public interface LazyRunnable : ILazyRunnable { }
}
