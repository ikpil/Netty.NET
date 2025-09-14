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
 * Abstract base class for {@link IEventExecutor} implementations.
 */
public abstract class AbstractEventExecutor : AbstractExecutorService, IEventExecutor
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(AbstractEventExecutor));

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

    public virtual IEventExecutorGroup parent()
    {
        return _parent;
    }

    public virtual Ticker ticker()
    {
        return Ticker.systemTicker();
    }

    public virtual bool isExecutorThread(Thread thread)
    {
        return inEventLoop(thread);
    }

    public virtual bool inEventLoop()
    {
        return inEventLoop(Thread.CurrentThread);
    }

    public abstract bool inEventLoop(Thread thread);

    public virtual TaskCompletionSource<V> newPromise<V>()
    {
        throw new NotImplementedException();
    }

    public virtual TaskCompletionSource<V> newProgressivePromise<V>()
    {
        throw new NotImplementedException();
    }

    public virtual Task<V> newSucceededFuture<V>(V result)
    {
        throw new NotImplementedException();
    }

    public virtual Task<V> newFailedFuture<V>(Exception cause)
    {
        throw new NotImplementedException();
    }

    public abstract Task terminationTask();

    public virtual IEventExecutor next()
    {
        return this;
    }

    public virtual IEnumerable<IEventExecutor> iterator()
    {
        return _selfCollection;
    }

    public virtual Task shutdownGracefullyAsync()
    {
        return shutdownGracefullyAsync(DEFAULT_SHUTDOWN_QUIET_PERIOD, DEFAULT_SHUTDOWN_TIMEOUT);
    }

    public abstract Task shutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout);


    /**
     * @deprecated {@link #shutdownGracefullyAsync(long, long, TimeSpan)} or {@link #shutdownGracefullyAsync()} instead.
     */
    [Obsolete]
    public override List<IRunnable> shutdownNow()
    {
        shutdown();
        return new List<IRunnable>();
    }

    public abstract bool isShuttingDown();

    public virtual bool isSuspended()
    {
        return false;
    }

    public virtual bool trySuspend()
    {
        return false;
    }

    public virtual IScheduledTask schedule(IRunnable command, TimeSpan delay)
    {
        throw new NotSupportedException();
    }

    public virtual IScheduledTask<V> schedule<V>(ICallable<V> callable, TimeSpan delay)
    {
        throw new NotSupportedException();
    }

    public virtual IScheduledTask scheduleAtFixedRate(IRunnable command, TimeSpan initialDelay, TimeSpan period)
    {
        throw new NotSupportedException();
    }

    public virtual IScheduledTask scheduleWithFixedDelay(IRunnable command, TimeSpan initialDelay, TimeSpan delay)
    {
        throw new NotSupportedException();
    }

    /**
     * Try to execute the given {@link IRunnable} and just log if it throws a {@link Exception}.
     */
    protected static void safeExecute(IRunnable task)
    {
        try
        {
            runTask(task);
        }
        catch (Exception t)
        {
            logger.warn("A task raised an exception. Task: {}", task, t);
        }
    }

    protected static void runTask(IRunnable task)
    {
        task.run();
    }

    /**
     * Like {@link #execute(IRunnable)} but does not guarantee the task will be run until either
     * a non-lazy task is executed or the executor is shut down.
     * <p>
     * The default implementation just delegates to {@link #execute(IRunnable)}.
     * </p>
     */
    [UnstableApi]
    public virtual void lazyExecute(IRunnable task)
    {
        execute(task);
    }
}