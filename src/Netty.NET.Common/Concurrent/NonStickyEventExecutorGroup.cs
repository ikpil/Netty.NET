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
 * {@link IEventExecutorGroup} which will preserve {@link IRunnable} execution order but makes no guarantees about what
 * {@link IEventExecutor} (and therefore {@link Thread}) will be used to execute the {@link IRunnable}s.
 *
 * <p>The {@link IEventExecutorGroup#next()} for the wrapped {@link IEventExecutorGroup} must <strong>NOT</strong> return
 * executors of type {@link IOrderedEventExecutor}.
 */
[UnstableApi]
public class NonStickyEventExecutorGroup : IEventExecutorGroup
{
    private readonly IEventExecutorGroup _group;
    private readonly int _maxTaskExecutePerRun;

    /**
     * Creates a new instance. Be aware that the given {@link IEventExecutorGroup} <strong>MUST NOT</strong> contain
     * any {@link IOrderedEventExecutor}s.
     */
    public NonStickyEventExecutorGroup(IEventExecutorGroup group, int maxTaskExecutePerRun = 1024)
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

    public Task shutdownGracefullyAsync()
    {
        return _group.shutdownGracefullyAsync();
    }

    public Ticker ticker()
    {
        return Ticker.systemTicker();
    }

    public Task shutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
    {
        return _group.shutdownGracefullyAsync(quietPeriod, timeout);
    }

    public Task terminationTask()
    {
        return _group.terminationTask();
    }

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

    public Task submit(IRunnable task)
    {
        return _group.submit(task);
    }

    public Task<T> submit<T>(IRunnable task, T result)
    {
        return _group.submit(task, result);
    }

    public Task<T> submit<T>(ICallable<T> task)
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

    public List<QueueingTaskNode<T>> invokeAll<T>(ICollection<T> tasks) where T : ICallable<T>
    {
        return _group.invokeAll(tasks);
    }

    public List<QueueingTaskNode<T>> invokeAll<T>(ICollection<T> tasks, TimeSpan timeout) where T : ICallable<T>
    {
        return _group.invokeAll<T>(tasks, timeout);
    }

    public T invokeAny<T>(ICollection<T> tasks) where T : ICallable<T>
    {
        return _group.invokeAny(tasks);
    }

    public T invokeAny<T>(ICollection<T> tasks, TimeSpan timeout) where T : ICallable<T>
    {
        return _group.invokeAny(tasks, timeout);
    }

    public void execute(IRunnable command)
    {
        _group.execute(command);
    }
}