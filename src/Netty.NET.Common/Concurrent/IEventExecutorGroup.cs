/*
 * Copyright 2012 The Netty Project
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

namespace Netty.NET.Common.Concurrent;

/**
 * The {@link EventExecutorGroup} is responsible for providing the {@link EventExecutor}'s to use
 * via its {@link #next()} method. Besides this, it is also responsible for handling their
 * life-cycle and allows shutting them down in a global fashion.
 *
 */
public interface IEventExecutorGroup : IScheduledExecutorService
{
    /**
     * Returns {@code true} if and only if all {@link EventExecutor}s managed by this {@link EventExecutorGroup}
     * are being {@linkplain #shutdownGracefully() shut down gracefully} or was {@linkplain #isShutdown() shut down}.
     */
    bool isShuttingDown();

    /**
     * Shortcut method for {@link #shutdownGracefully(long, long, TimeUnit)} with sensible default values.
     *
     * @return the {@link #terminationFuture()}
     */
    IFuture<Void> shutdownGracefully();

    /**
     * Signals this executor that the caller wants the executor to be shut down.  Once this method is called,
     * {@link #isShuttingDown()} starts to return {@code true}, and the executor prepares to shut itself down.
     * Unlike {@link #shutdown()}, graceful shutdown ensures that no tasks are submitted for <i>'the quiet period'</i>
     * (usually a couple seconds) before it shuts itself down.  If a task is submitted during the quiet period,
     * it is guaranteed to be accepted and the quiet period will start over.
     *
     * @param quietPeriod the quiet period as described in the documentation
     * @param timeout     the maximum amount of time to wait until the executor is {@linkplain #shutdown()}
     *                    regardless if a task was submitted during the quiet period
     * @param unit        the unit of {@code quietPeriod} and {@code timeout}
     *
     * @return the {@link #terminationFuture()}
     */
    IFuture<Void> shutdownGracefully(TimeSpan quietPeriod, TimeSpan timeout);

    /**
     * Returns the {@link Future} which is notified when all {@link EventExecutor}s managed by this
     * {@link EventExecutorGroup} have been terminated.
     */
    IFuture<Void> terminationFuture();

    /**
     * @deprecated {@link #shutdownGracefully(long, long, TimeUnit)} or {@link #shutdownGracefully()} instead.
     */
    [Obsolete]
    new void shutdown();

    /**
     * @deprecated {@link #shutdownGracefully(long, long, TimeUnit)} or {@link #shutdownGracefully()} instead.
     */
    [Obsolete]
    new List<Functional.IRunnable> shutdownNow();

    /**
     * Returns one of the {@link EventExecutor}s managed by this {@link EventExecutorGroup}.
     */
    IEventExecutor next();

    IEnumerable<IEventExecutor> iterator();

    /**
     * The ticker for this executor. Usually the {@link #schedule} methods will follow the
     * {@link Ticker#systemTicker() system ticker} (i.e. {@link System#nanoTime()}), but especially for testing it is
     * sometimes useful to have more control over the ticker. In that case, this method will be overridden. Code that
     * schedules tasks on this executor should use this ticker in order to stay consistent with the executor (e.g. not
     * be surprised by scheduled tasks running "early").
     *
     * @return The ticker for this scheduler
     */
    Ticker ticker() => Ticker.systemTicker();

    // CLR convenience: all Task views use the same Netty Future completion.
    Task shutdownGracefullyAsync() => shutdownGracefully().Task;
    Task shutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => shutdownGracefully(quietPeriod, timeout).Task;
    Task terminationTask() => terminationFuture().Task;
}
