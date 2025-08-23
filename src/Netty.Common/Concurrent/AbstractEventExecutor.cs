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
    
    protected AbstractEventExecutor() : this(null)
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

    public IFuture shutdownGracefully()
    {
        return shutdownGracefully(DEFAULT_SHUTDOWN_QUIET_PERIOD, DEFAULT_SHUTDOWN_TIMEOUT);
    }

    /**
     * @deprecated {@link #shutdownGracefullyAsync(long, long, TimeSpan)} or {@link #shutdownGracefullyAsync()} instead.
     */
    @Override
    [Obsolete]
    public abstract void shutdown();

    /**
     * @deprecated {@link #shutdownGracefullyAsync(long, long, TimeSpan)} or {@link #shutdownGracefullyAsync()} instead.
     */
    @Override
    [Obsolete]
    public List<IRunnable> shutdownNow() {
        shutdown();
        return Collections.emptyList();
    }

    @Override
    public Future<?> submit(IRunnable task) {
        return (Future<?>) super.submit(task);
    }

    @Override
    public Task<T> submit(IRunnable task, T result) {
        return (Future<T>) super.submit(task, result);
    }

    @Override
    public Task<T> submit(Func<T> task) {
        return (Future<T>) super.submit(task);
    }

    @Override
    protected final <T> RunnableFuture<T> newTaskFor(IRunnable runnable, T value) {
        return new PromiseTask<T>(this, runnable, value);
    }

    @Override
    protected final <T> RunnableFuture<T> newTaskFor(Func<T> callable) {
        return new PromiseTask<T>(this, callable);
    }

    @Override
    public IScheduledTask<?> schedule(IRunnable command, long delay,
                                       TimeSpan unit) {
        throw new NotSupportedException();
    }

    @Override
    public <V> IScheduledTask<V> schedule(Func<V> callable, long delay, TimeSpan unit) {
        throw new NotSupportedException();
    }

    @Override
    public IScheduledTask<?> scheduleAtFixedRate(IRunnable command, long initialDelay, long period, TimeSpan unit) {
        throw new NotSupportedException();
    }

    @Override
    public IScheduledTask<?> scheduleWithFixedDelay(IRunnable command, long initialDelay, long delay, TimeSpan unit) {
        throw new NotSupportedException();
    }

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

    protected static void runTask(@Execute IRunnable task) {
        task.run();
    }

    /**
     * Like {@link #execute(IRunnable)} but does not guarantee the task will be run until either
     * a non-lazy task is executed or the executor is shut down.
     * <p>
     * The default implementation just delegates to {@link #execute(IRunnable)}.
     * </p>
     */
    @UnstableApi
    public void lazyExecute(IRunnable task) {
        execute(task);
    }

    /**
     *  @deprecated override {@link SingleThreadEventExecutor#wakesUpForTask} to re-create this behaviour
     *
     */
    [Obsolete]
    public interface LazyRunnable extends IRunnable { }
}
