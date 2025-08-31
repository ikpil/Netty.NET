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
using System.Threading;

namespace Netty.NET.Common.Concurrent;

/**
 * The {@link IEventExecutor} is a special {@link IEventExecutorGroup} which comes
 * with some handy methods to see if a {@link Thread} is executed in a event loop.
 * Besides this, it also extends the {@link IEventExecutorGroup} to allow for a generic
 * way to access methods.
 */
public interface IEventExecutor : IEventExecutorGroup, IThreadAwareExecutor
{
    /**
     * Return the {@link IEventExecutorGroup} which is the parent of this {@link IEventExecutor},
     */
    IEventExecutorGroup parent();

    bool isExecutorThread(Thread thread)
    {
        return inEventLoop(thread);
    }

    /**
     * Calls {@link #inEventLoop(Thread)} with {@link Thread#currentThread()} as argument
     */
    bool inEventLoop();

    /**
     * Return {@code true} if the given {@link Thread} is executed in the event loop,
     * {@code false} otherwise.
     */
    bool inEventLoop(Thread thread);

    /**
     * Return a new {@link Promise}.
     */
    Promise<V> newPromise<V>()
    {
        return new DefaultPromise<V>(this);
    }

    /**
     * Create a new {@link ProgressivePromise}.
     */
    ProgressivePromise<V> newProgressivePromise<V>()
    {
        return new DefaultProgressivePromise<V>(this);
    }

    /**
     * Create a new {@link Future} which is marked as succeeded already. So {@link Future#isSuccess()}
     * will return {@code true}. All {@link IFutureListener} added to it will be notified directly. Also
     * every call of blocking methods will just return without blocking.
     */
    IFuture<V> newSucceededFuture<V>(V result)
    {
        return new SucceededFuture<V>(this, result);
    }

    /**
     * Create a new {@link Future} which is marked as failed already. So {@link Future#isSuccess()}
     * will return {@code false}. All {@link IFutureListener} added to it will be notified directly. Also
     * every call of blocking methods will just return without blocking.
     */
    IFuture<V> newFailedFuture<V>(Exception cause)
    {
        return new FailedFuture<V>(this, cause);
    }

    /**
     * Returns {@code true} if the {@link IEventExecutor} is considered suspended.
     *
     * @return {@code true} if suspended, {@code false} otherwise.
     */
    bool isSuspended()
    {
        return false;
    }

    /**
     * Try to suspend this {@link IEventExecutor} and return {@code true} if suspension was successful.
     * Suspending an {@link IEventExecutor} will allow it to free up resources, like for example a {@link Thread} that
     * is backing the {@link IEventExecutor}. Once an {@link IEventExecutor} was suspended it will be started again
     * by submitting work to it via one of the following methods:
     * <ul>
     *   <li>{@link #execute(IRunnable)}</li>
     *   <li>{@link #schedule(IRunnable, long, TimeSpan)}</li>
     *   <li>{@link #schedule(Callable, long, TimeSpan)}</li>
     *   <li>{@link #scheduleAtFixedRate(IRunnable, long, long, TimeSpan)}</li>
     *   <li>{@link #scheduleWithFixedDelay(IRunnable, long, long, TimeSpan)}</li>
     * </ul>
     *
     * Even if this method returns {@code true} it might take some time for the {@link IEventExecutor} to fully suspend
     * itself.
     *
     * @return {@code true} if suspension was successful, otherwise {@code false}.
     */
    bool trySuspend()
    {
        return false;
    }
}