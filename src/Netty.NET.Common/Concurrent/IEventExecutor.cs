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
using System.Threading.Tasks;

namespace Netty.NET.Common.Concurrent;

/**
 * The {@link EventExecutor} is a special {@link EventExecutorGroup} which comes
 * with some handy methods to see if a {@link Thread} is executed in a event loop.
 * Besides this, it also extends the {@link EventExecutorGroup} to allow for a generic
 * way to access methods.
 */
public interface IEventExecutor : IEventExecutorGroup, IThreadAwareExecutor
{
    /**
     * Return the {@link EventExecutorGroup} which is the parent of this {@link EventExecutor},
     */
    IEventExecutorGroup parent();

    bool isExecutorThread(Thread thread);

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
     * Returns {@code true} if the {@link EventExecutor} is considered suspended.
     *
     * @return {@code true} if suspended, {@code false} otherwise.
     */
    bool isSuspended();

    /**
     * Try to suspend this {@link EventExecutor} and return {@code true} if suspension was successful.
     * Suspending an {@link EventExecutor} will allow it to free up resources, like for example a {@link Thread} that
     * is backing the {@link EventExecutor}. Once an {@link EventExecutor} was suspended it will be started again
     * by submitting work to it via one of the following methods:
     * <ul>
     *   <li>{@link #execute(Runnable)}</li>
     *   <li>{@link #schedule(Runnable, long, TimeUnit)}</li>
     *   <li>{@link #schedule(Callable, long, TimeUnit)}</li>
     *   <li>{@link #scheduleAtFixedRate(Runnable, long, long, TimeUnit)}</li>
     *   <li>{@link #scheduleWithFixedDelay(Runnable, long, long, TimeUnit)}</li>
     * </ul>
     *
     * Even if this method returns {@code true} it might take some time for the {@link EventExecutor} to fully suspend
     * itself.
     *
     * @return {@code true} if suspension was successful, otherwise {@code false}.
     */
    bool trySuspend();
}
