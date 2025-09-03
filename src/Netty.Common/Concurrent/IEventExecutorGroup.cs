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
using System.Threading.Tasks;

namespace Netty.NET.Common.Concurrent;

/**
 * The {@link IEventExecutorGroup} is responsible for providing the {@link IEventExecutor}'s to use
 * via its {@link #next()} method. Besides this, it is also responsible for handling their
 * life-cycle and allows shutting them down in a global fashion.
 *
 */
public interface IEventExecutorGroup : IScheduledExecutorService
{
    /**
     * Returns {@code true} if and only if all {@link IEventExecutor}s managed by this {@link IEventExecutorGroup}
     * are being {@linkplain #shutdownGracefullyAsync() shut down gracefully} or was {@linkplain #isShutdown() shut down}.
     */
    bool isShuttingDown();

    /**
     * Shortcut method for {@link #shutdownGracefullyAsync(long, long, TimeSpan)} with sensible default values.
     *
     * @return the {@link #terminationAsync()}
     */
    Task shutdownGracefullyAsync();

    Ticker ticker();

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
     * @return the {@link #terminationAsync()}
     */
    Task shutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout);

    /**
     * Returns the {@link Future} which is notified when all {@link IEventExecutor}s managed by this
     * {@link IEventExecutorGroup} have been terminated.
     */
    Task terminationAsync();

    /**
     * Returns one of the {@link IEventExecutor}s managed by this {@link IEventExecutorGroup}.
     */
    IEventExecutor next();
}
