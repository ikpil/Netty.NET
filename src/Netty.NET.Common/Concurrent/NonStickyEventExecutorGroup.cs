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
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;

/**
 * {@link EventExecutorGroup} which will preserve {@link Runnable} execution order but makes no guarantees about what
 * {@link EventExecutor} (and therefore {@link Thread}) will be used to execute the {@link Runnable}s.
 *
 * <p>The {@link EventExecutorGroup#next()} for the wrapped {@link EventExecutorGroup} must <strong>NOT</strong> return
 * executors of type {@link OrderedEventExecutor}.
 */
[UnstableApi]
public sealed class NonStickyEventExecutorGroup : IEventExecutorGroup
{
    private readonly IEventExecutorGroup _group;
    private readonly int _maxTaskExecutePerRun;

    /**
     * Creates a new instance. Be aware that the given {@link EventExecutorGroup} <strong>MUST NOT</strong> contain
     * any {@link OrderedEventExecutor}s.
     */
    public NonStickyEventExecutorGroup(IEventExecutorGroup group) : this(group, 1024) { }

    /**
     * Creates a new instance. Be aware that the given {@link EventExecutorGroup} <strong>MUST NOT</strong> contain
     * any {@link OrderedEventExecutor}s.
     */
    public NonStickyEventExecutorGroup(IEventExecutorGroup group, int maxTaskExecutePerRun)
    {
        _group = verify(group);
        _maxTaskExecutePerRun = ObjectUtil.checkPositive(maxTaskExecutePerRun, "maxTaskExecutePerRun");
    }

    private static IEventExecutorGroup verify(IEventExecutorGroup group)
    {
        IEnumerable<IEventExecutor> executors = ObjectUtil.checkNotNull(group, "group").iterator();
        foreach (var executor in executors)
        {
            if (executor is IOrderedEventExecutor)
            {
                throw new ArgumentException("IEventExecutorGroup " + group + " contains OrderedEventExecutors: " + executor);
            }
        }

        return group;
    }

    private NonStickyOrderedEventExecutor newExecutor(IEventExecutor executor)
    {
        return new NonStickyOrderedEventExecutor(executor, _maxTaskExecutePerRun);
    }

    public bool isShuttingDown()
    {
        return _group.isShuttingDown();
    }

    public IFuture<Void> shutdownGracefully()
    {
        return _group.shutdownGracefully();
    }

    public Ticker ticker()
    {
        return Ticker.systemTicker();
    }

    public IFuture<Void> shutdownGracefully(TimeSpan quietPeriod, TimeSpan timeout)
    {
        return _group.shutdownGracefully(quietPeriod, timeout);
    }

    public IFuture<Void> terminationFuture()
    {
        return _group.terminationFuture();
    }

    public Task shutdownGracefullyAsync() => shutdownGracefully().Task;
    public Task shutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => shutdownGracefully(quietPeriod, timeout).Task;
    public Task terminationTask() => terminationFuture().Task;

    //@SuppressWarnings("deprecation")
    public void shutdown()
    {
        _group.shutdown();
    }

    //@SuppressWarnings("deprecation")
    public List<IRunnable> shutdownNow()
    {
        return _group.shutdownNow();
    }

    public IEventExecutor next()
    {
        return newExecutor(_group.next());
    }

    public IEnumerable<IEventExecutor> iterator()
    {
        IEnumerable<IEventExecutor> itr = _group.iterator();
        foreach (var it in itr)
        {
            yield return newExecutor(it);
        }
    }

    public IFuture<Void> submit(IRunnable task)
    {
        return _group.submit(task);
    }

    public IFuture<T> submit<T>(IRunnable task, T result)
    {
        return _group.submit(task, result);
    }

    public IFuture<T> submit<T>(ICallable<T> task)
    {
        return _group.submit(task);
    }

    public IScheduledTask schedule(IRunnable command, TimeSpan delay)
    {
        return _group.schedule(command, delay);
    }

    public IScheduledTask<V> schedule<V>(ICallable<V> callable, TimeSpan delay)
    {
        return _group.schedule(callable, delay);
    }

    public IScheduledTask scheduleAtFixedRate(IRunnable command, TimeSpan initialDelay, TimeSpan period)
    {
        return _group.scheduleAtFixedRate(command, initialDelay, period);
    }

    public IScheduledTask scheduleWithFixedDelay(IRunnable command, TimeSpan initialDelay, TimeSpan delay)
    {
        return _group.scheduleWithFixedDelay(command, initialDelay, delay);
    }

    public bool isShutdown()
    {
        return _group.isShutdown();
    }

    public bool isTerminated()
    {
        return _group.isTerminated();
    }

    public bool awaitTermination(TimeSpan timeout)
    {
        return _group.awaitTermination(timeout);
    }

    public List<IFuture<T>> invokeAll<T>(ICollection<ICallable<T>> tasks)
    {
        return _group.invokeAll(tasks);
    }

    public List<IFuture<T>> invokeAll<T>(ICollection<ICallable<T>> tasks, TimeSpan timeout)
    {
        return _group.invokeAll<T>(tasks, timeout);
    }

    public T invokeAny<T>(ICollection<ICallable<T>> tasks)
    {
        return _group.invokeAny(tasks);
    }

    public T invokeAny<T>(ICollection<ICallable<T>> tasks, TimeSpan timeout)
    {
        return _group.invokeAny(tasks, timeout);
    }

    public void execute(IRunnable command)
    {
        _group.execute(command);
    }
}
