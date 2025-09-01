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
    private readonly IReadOnlyCollection<IEventExecutor> selfCollection;
    
    protected AbstractEventExecutor() : this(ObjectUtil.Null<IEventExecutorGroup>())
    {
    }

    protected AbstractEventExecutor(IEventExecutorGroup parent) {
        selfCollection = new List<IEventExecutor> { this }.AsReadOnly(); 
        _parent = parent;
    }

    public virtual IEventExecutorGroup parent() {
        return _parent;
    }

    public virtual IEventExecutor next() {
        return this;
    }

    public virtual IEnumerable<IEventExecutor> iterator()
    {
        return selfCollection;
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
    public abstract void shutdown();

    
    /**
     * @deprecated {@link #shutdownGracefullyAsync(long, long, TimeSpan)} or {@link #shutdownGracefullyAsync()} instead.
     */
    [Obsolete]
    public List<IRunnable> shutdownNow() {
        shutdown();
        return new List<IRunnable>();
    }
    
    public abstract bool isShuttingDown();
    public abstract bool isShutdown();
    public abstract bool isTerminated();
    public abstract bool isSuspended();
    public abstract bool trySuspend();

    public Task submit(IRunnable task) {
        return (Future<?>) super.submit(task);
    }

    @Override
    public Task<T> submit<T>(IRunnable task, T result) {
        return (Future<T>) super.submit(task, result);
    }

    @Override
    public Task<T> submit<T>(Func<T> task) {
        return (Future<T>) super.submit(task);
    }

    @Override
    protected RunnableFuture<T> newTaskFor<T>(IRunnable runnable, T value) {
        return new PromiseTask<T>(this, runnable, value);
    }

    @Override
    protected RunnableFuture<T> newTaskFor<T>(Func<T> callable) {
        return new PromiseTask<T>(this, callable);
    }

    @Override
    public IScheduledTask schedule(IRunnable command, TimeSpan delay) {
        throw new NotSupportedException();
    }

    @Override
    public IScheduledTask<V> schedule<V>(Func<V> callable, TimeSpan delay) {
        throw new NotSupportedException();
    }

    @Override
    public IScheduledTask scheduleAtFixedRate(IRunnable command, TimeSpan initialDelay, TimeSpan period) {
        throw new NotSupportedException();
    }

    @Override
    public IScheduledTask scheduleWithFixedDelay(IRunnable command, TimeSpan initialDelay, TimeSpan delay) {
        throw new NotSupportedException();
    }

    public abstract void execute(IRunnable task);
    
    /**
     * Try to execute the given {@link IRunnable} and just log if it throws a {@link Exception}.
     */
    protected static void safeExecute(IRunnable task) {
        try {
            runTask(task);
        } catch (Exception t) {
            logger.warn("A task raised an exception. Task: {}", task, t);
        }
    }

    protected static void runTask(IRunnable task) {
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
