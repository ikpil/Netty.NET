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
using System.Threading.Tasks;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

/**
 * Abstract base class for {@link EventExecutorGroup} implementations.
 */
public abstract class AbstractEventExecutorGroup : IEventExecutorGroup
{
    /**
     * @deprecated {@link #shutdownGracefully(long, long, TimeUnit)} or {@link #shutdownGracefully()} instead.
     */
    [Obsolete]
    public abstract void shutdown();

    /**
     * @deprecated {@link #shutdownGracefully(long, long, TimeUnit)} or {@link #shutdownGracefully()} instead.
     */
    [Obsolete]
    public virtual List<IRunnable> shutdownNow()
    {
        shutdown();
        return new List<IRunnable>();
    }
    public abstract bool isShutdown();
    public abstract bool isShuttingDown();
    public abstract bool isTerminated();
    public abstract bool awaitTermination(TimeSpan timeout);
    public abstract IFuture<Void> terminationFuture();
    public Task terminationTask() => terminationFuture().Task;
    public abstract IEnumerable<IEventExecutor> iterator();

    public virtual Ticker ticker()
    {
        return Ticker.systemTicker();
    }

    public abstract IFuture<Void> shutdownGracefully(TimeSpan quietPeriod, TimeSpan timeout);
    public Task shutdownGracefullyAsync() => shutdownGracefully().Task;
    public Task shutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => shutdownGracefully(quietPeriod, timeout).Task;
    public abstract IEventExecutor next();


    public virtual IFuture<T> submit<T>(ICallable<T> task)
    {
        return next().submit(task);
    }

    public virtual IFuture<T> submit<T>(IRunnable task, T result)
    {
        return next().submit(task, result);
    }

    public virtual IFuture<Void> submit(IRunnable task)
    {
        return next().submit(task);
    }

    public virtual IScheduledTask schedule(IRunnable command, TimeSpan delay)
    {
        return next().schedule(command, delay);
    }

    public virtual IScheduledTask<V> schedule<V>(ICallable<V> callable, TimeSpan delay)
    {
        return next().schedule(callable, delay);
    }

    public virtual IScheduledTask scheduleAtFixedRate(IRunnable command, TimeSpan initialDelay, TimeSpan period)
    {
        return next().scheduleAtFixedRate(command, initialDelay, period);
    }

    public virtual IScheduledTask scheduleWithFixedDelay(IRunnable command, TimeSpan initialDelay, TimeSpan delay)
    {
        return next().scheduleWithFixedDelay(command, initialDelay, delay);
    }

    public virtual IFuture<Void> shutdownGracefully()
    {
        return shutdownGracefully(AbstractEventExecutor.DEFAULT_SHUTDOWN_QUIET_PERIOD, AbstractEventExecutor.DEFAULT_SHUTDOWN_TIMEOUT);
    }

    public virtual List<IFuture<T>> invokeAll<T>(ICollection<ICallable<T>> tasks)
    {
        return next().invokeAll(tasks);
    }

    public virtual List<IFuture<T>> invokeAll<T>(ICollection<ICallable<T>> tasks, TimeSpan timeout)
    {
        return next().invokeAll(tasks, timeout);
    }

    public virtual T invokeAny<T>(ICollection<ICallable<T>> tasks)
    {
        return next().invokeAny(tasks);
    }

    public virtual T invokeAny<T>(ICollection<ICallable<T>> tasks, TimeSpan timeout)
    {
        return next().invokeAny(tasks, timeout);
    }

    public virtual void execute(IRunnable command)
    {
        next().execute(command);
    }
}
