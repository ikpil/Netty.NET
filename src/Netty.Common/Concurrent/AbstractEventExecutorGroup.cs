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
 * Abstract base class for {@link IEventExecutorGroup} implementations.
 */
public abstract class AbstractEventExecutorGroup : IEventExecutorGroup
{
    public abstract void shutdown();

    public abstract List<IRunnable> shutdownNow();
    public abstract bool isShutdown();
    public abstract bool isShuttingDown();
    public abstract bool isTerminated();
    public abstract bool awaitTermination(TimeSpan timeout);
    public abstract Task terminationAsync();
    public abstract Task shutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout);
    public abstract IEventExecutor next();


    public Task<T> submit<T>(ICallable<T> task)
    {
        return next().submit(task);
    }

    public Task<T> submit<T>(IRunnable task, T result)
    {
        return next().submit(task, result);
    }

    public Task submit(IRunnable task)
    {
        return next().submit(task);
    }

    public IScheduledTask schedule(IRunnable command, TimeSpan delay)
    {
        return next().schedule(command, delay);
    }

    public IScheduledTask<V> schedule<V>(Func<V> callable, TimeSpan delay)
    {
        return next().schedule(callable, delay);
    }

    public IScheduledTask scheduleAtFixedRate(IRunnable command, long initialDelay, TimeSpan period)
    {
        return next().scheduleAtFixedRate(command, initialDelay, period);
    }

    public IScheduledTask scheduleWithFixedDelay(IRunnable command, long initialDelay, TimeSpan delay)
    {
        return next().scheduleWithFixedDelay(command, initialDelay, delay);
    }

    public Task shutdownGracefullyAsync()
    {
        return shutdownGracefullyAsync(AbstractEventExecutor.DEFAULT_SHUTDOWN_QUIET_PERIOD, AbstractEventExecutor.DEFAULT_SHUTDOWN_TIMEOUT);
    }

    public List<Task<T>> invokeAll<T>(ICollection<ICallable<T>> tasks)
    {
        return next().invokeAll(tasks);
    }

    public List<Task<T>> invokeAll<T>(ICollection<ICallable<T>> tasks, TimeSpan timeout)
    {
        return next().invokeAll(tasks, timeout);
    }

    public T invokeAny<T>(ICollection<ICallable<T>> tasks)
    {
        return next().invokeAny(tasks);
    }

    public T invokeAny<T>(ICollection<ICallable<T>> tasks, TimeSpan timeout)
    {
        return next().invokeAny(tasks, timeout);
    }

    public void execute(IRunnable command)
    {
        next().execute(command);
    }
}