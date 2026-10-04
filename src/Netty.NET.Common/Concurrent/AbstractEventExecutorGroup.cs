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
    public abstract void Shutdown();

    /**
     * @deprecated {@link #shutdownGracefully(long, long, TimeUnit)} or {@link #shutdownGracefully()} instead.
     */
    [Obsolete]
    public virtual List<IRunnable> ShutdownNow()
    {
        Shutdown();
        return new List<IRunnable>();
    }
    public abstract bool IsShutdown();
    public abstract bool IsShuttingDown();
    public abstract bool IsTerminated();
    public abstract bool AwaitTermination(TimeSpan timeout);
    public abstract Task Termination { get; }
    public abstract IEnumerable<IEventExecutor> Iterator();

    public virtual Ticker Ticker()
    {
        return global::Netty.NET.Common.Concurrent.Ticker.SystemTicker();
    }

    public abstract Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout);
    public virtual Task StopAsync()
    {
        Shutdown();
        return Termination;
    }
    public abstract IEventExecutor Next();

    public virtual Task ShutdownGracefullyAsync()
    {
        return ShutdownGracefullyAsync(AbstractEventExecutor.DEFAULT_SHUTDOWN_QUIET_PERIOD, AbstractEventExecutor.DEFAULT_SHUTDOWN_TIMEOUT);
    }

    public virtual void Execute(Action command)
    {
        Next().Execute(command);
    }
}
