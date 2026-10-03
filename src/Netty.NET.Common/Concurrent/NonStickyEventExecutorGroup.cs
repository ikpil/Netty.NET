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
    internal IEventExecutorGroup DelegatedGroup => _group;
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

    public Task ShutdownGracefullyAsync()
    {
        return _group.ShutdownGracefullyAsync();
    }

    public Ticker ticker()
    {
        return Ticker.systemTicker();
    }

    public Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
    {
        return _group.ShutdownGracefullyAsync(quietPeriod, timeout);
    }

    public Task Termination => _group.Termination;
    public Task StopAsync() => _group.StopAsync();

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

    public void execute(IRunnable command)
    {
        _group.execute(command);
    }
}
