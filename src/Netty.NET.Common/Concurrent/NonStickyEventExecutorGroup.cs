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
        _group = Verify(group);
        _maxTaskExecutePerRun = ObjectUtil.CheckPositive(maxTaskExecutePerRun, "maxTaskExecutePerRun");
    }

    private static IEventExecutorGroup Verify(IEventExecutorGroup group)
    {
        IEnumerable<IEventExecutor> executors = ObjectUtil.CheckNotNull(group, "group").Iterator();
        foreach (var executor in executors)
        {
            if (executor is IOrderedEventExecutor)
            {
                throw new ArgumentException("IEventExecutorGroup " + group + " contains OrderedEventExecutors: " + executor);
            }
        }

        return group;
    }

    private NonStickyOrderedEventExecutor NewExecutor(IEventExecutor executor)
    {
        return new NonStickyOrderedEventExecutor(executor, _maxTaskExecutePerRun);
    }

    public bool IsShuttingDown()
    {
        return _group.IsShuttingDown();
    }

    public Task ShutdownGracefullyAsync()
    {
        return _group.ShutdownGracefullyAsync();
    }

    public Ticker Ticker()
    {
        return global::Netty.NET.Common.Concurrent.Ticker.SystemTicker();
    }

    public Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
    {
        return _group.ShutdownGracefullyAsync(quietPeriod, timeout);
    }

    public Task Termination => _group.Termination;
    public Task StopAsync() => _group.StopAsync();

    //@SuppressWarnings("deprecation")
    public void Shutdown()
    {
        _group.Shutdown();
    }

    //@SuppressWarnings("deprecation")
    public List<IRunnable> ShutdownNow()
    {
        return _group.ShutdownNow();
    }

    public IEventExecutor Next()
    {
        return NewExecutor(_group.Next());
    }

    public IEnumerable<IEventExecutor> Iterator()
    {
        IEnumerable<IEventExecutor> itr = _group.Iterator();
        foreach (var it in itr)
        {
            yield return NewExecutor(it);
        }
    }

    public bool IsShutdown()
    {
        return _group.IsShutdown();
    }

    public bool IsTerminated()
    {
        return _group.IsTerminated();
    }

    public bool AwaitTermination(TimeSpan timeout)
    {
        return _group.AwaitTermination(timeout);
    }

    public void Execute(Action command)
    {
        _group.Execute(command);
    }
}
