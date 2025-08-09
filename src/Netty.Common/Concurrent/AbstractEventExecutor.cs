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
using System.Collections.ObjectModel;
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

    private readonly IEventExecutorGroup parent;
    private readonly Collection<IEventExecutor> selfCollection = Collections.<IEventExecutor>singleton(this);

    protected AbstractEventExecutor() {
        this(null);
    }

    protected AbstractEventExecutor(IEventExecutorGroup parent) {
        this.parent = parent;
    }

    @Override
    public IEventExecutorGroup parent() {
        return parent;
    }

    @Override
    public IEventExecutor next() {
        return this;
    }

    @Override
    public Iterator<IEventExecutor> iterator() {
        return selfCollection.iterator();
    }

    @Override
    public Future<?> shutdownGracefully() {
        return shutdownGracefully(DEFAULT_SHUTDOWN_QUIET_PERIOD, DEFAULT_SHUTDOWN_TIMEOUT, TimeSpan.SECONDS);
    }

    /**
     * @deprecated {@link #shutdownGracefullyAsync(long, long, TimeSpan)} or {@link #shutdownGracefullyAsync()} instead.
     */
    @Override
    @Deprecated
    public abstract void shutdown();

    /**
     * @deprecated {@link #shutdownGracefullyAsync(long, long, TimeSpan)} or {@link #shutdownGracefullyAsync()} instead.
     */
    @Override
    @Deprecated
    public List<Runnable> shutdownNow() {
        shutdown();
        return Collections.emptyList();
    }

    @Override
    public Future<?> submit(Runnable task) {
        return (Future<?>) super.submit(task);
    }

    @Override
    public Task<T> submit(Runnable task, T result) {
        return (Future<T>) super.submit(task, result);
    }

    @Override
    public Task<T> submit(Func<T> task) {
        return (Future<T>) super.submit(task);
    }

    @Override
    protected final <T> RunnableFuture<T> newTaskFor(Runnable runnable, T value) {
        return new PromiseTask<T>(this, runnable, value);
    }

    @Override
    protected final <T> RunnableFuture<T> newTaskFor(Func<T> callable) {
        return new PromiseTask<T>(this, callable);
    }

    @Override
    public IScheduledTask<?> schedule(Runnable command, long delay,
                                       TimeSpan unit) {
        throw new UnsupportedOperationException();
    }

    @Override
    public <V> IScheduledTask<V> schedule(Func<V> callable, long delay, TimeSpan unit) {
        throw new UnsupportedOperationException();
    }

    @Override
    public IScheduledTask<?> scheduleAtFixedRate(Runnable command, long initialDelay, long period, TimeSpan unit) {
        throw new UnsupportedOperationException();
    }

    @Override
    public IScheduledTask<?> scheduleWithFixedDelay(Runnable command, long initialDelay, long delay, TimeSpan unit) {
        throw new UnsupportedOperationException();
    }

    /**
     * Try to execute the given {@link Runnable} and just log if it throws a {@link Exception}.
     */
    protected static void safeExecute(Runnable task) {
        try {
            runTask(task);
        } catch (Exception t) {
            logger.warn("A task raised an exception. Task: {}", task, t);
        }
    }

    protected static void runTask(@Execute Runnable task) {
        task.run();
    }

    /**
     * Like {@link #execute(Runnable)} but does not guarantee the task will be run until either
     * a non-lazy task is executed or the executor is shut down.
     * <p>
     * The default implementation just delegates to {@link #execute(Runnable)}.
     * </p>
     */
    @UnstableApi
    public void lazyExecute(Runnable task) {
        execute(task);
    }

    /**
     *  @deprecated override {@link SingleThreadEventExecutor#wakesUpForTask} to re-create this behaviour
     *
     */
    @Deprecated
    public interface LazyRunnable extends Runnable { }
}
