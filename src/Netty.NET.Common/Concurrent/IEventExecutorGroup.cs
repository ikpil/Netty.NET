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
namespace Netty.NET.Common.Concurrent;

/**
 * The {@link IEventExecutorGroup} is responsible for providing the {@link IEventExecutor}'s to use
 * via its {@link #next()} method. Besides this, it is also responsible for handling their
 * life-cycle and allows shutting them down in a global fashion.
 *
 */
public interface IEventExecutorGroup : ScheduledExecutorService, Iterable<IEventExecutor> {

    /**
     * Returns {@code true} if and only if all {@link IEventExecutor}s managed by this {@link IEventExecutorGroup}
     * are being {@linkplain #shutdownGracefully() shut down gracefully} or was {@linkplain #isShutdown() shut down}.
     */
    bool isShuttingDown();

    /**
     * Shortcut method for {@link #shutdownGracefully(long, long, TimeSpan)} with sensible default values.
     *
     * @return the {@link #terminationFuture()}
     */
    Future<?> shutdownGracefully();

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
    Future<?> shutdownGracefully(long quietPeriod, long timeout, TimeSpan unit);

    /**
     * Returns the {@link Future} which is notified when all {@link IEventExecutor}s managed by this
     * {@link IEventExecutorGroup} have been terminated.
     */
    Future<?> terminationFuture();

    /**
     * @deprecated {@link #shutdownGracefully(long, long, TimeSpan)} or {@link #shutdownGracefully()} instead.
     */
    @Override
    @Deprecated
    void shutdown();

    /**
     * @deprecated {@link #shutdownGracefully(long, long, TimeSpan)} or {@link #shutdownGracefully()} instead.
     */
    @Override
    @Deprecated
    List<Runnable> shutdownNow();

    /**
     * Returns one of the {@link IEventExecutor}s managed by this {@link IEventExecutorGroup}.
     */
    IEventExecutor next();

    @Override
    Iterator<IEventExecutor> iterator();

    @Override
    Future<?> submit(Runnable task);

    @Override
    <T> Future<T> submit(Runnable task, T result);

    @Override
    <T> Future<T> submit(Callable<T> task);

    /**
     * The ticker for this executor. Usually the {@link #schedule} methods will follow the
     * {@link Ticker#systemTicker() system ticker} (i.e. {@link System#nanoTime()}), but especially for testing it is
     * sometimes useful to have more control over the ticker. In that case, this method will be overridden. Code that
     * schedules tasks on this executor should use this ticker in order to stay consistent with the executor (e.g. not
     * be surprised by scheduled tasks running "early").
     *
     * @return The ticker for this scheduler
     */
    default Ticker ticker() {
        return Ticker.systemTicker();
    }

    @Override
    ScheduledFuture<?> schedule(Runnable command, long delay, TimeSpan unit);

    @Override
    <V> ScheduledFuture<V> schedule(Callable<V> callable, long delay, TimeSpan unit);

    @Override
    ScheduledFuture<?> scheduleAtFixedRate(Runnable command, long initialDelay, long period, TimeSpan unit);

    @Override
    ScheduledFuture<?> scheduleWithFixedDelay(Runnable command, long initialDelay, long delay, TimeSpan unit);
}
