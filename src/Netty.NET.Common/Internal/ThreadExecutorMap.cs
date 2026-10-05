/*
 * Copyright 2019 The Netty Project
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

using Netty.NET.Common.Concurrent;
using System;

namespace Netty.NET.Common.Internal;

/**
 * Allow to retrieve the {@link EventExecutor} for the calling {@link Thread}.
 */
public static class ThreadExecutorMap
{
    private static readonly FastThreadLocal<IEventExecutor> _mappings = new FastThreadLocal<IEventExecutor>();

    /**
     * Returns the current {@link EventExecutor} that uses the {@link Thread}, or {@code null} if none / unknown.
     */
    public static IEventExecutor CurrentExecutor()
    {
        return _mappings.Get();
    }

    /**
     * Set the current {@link EventExecutor} that is used by the {@link Thread}.
     */
    public static IEventExecutor SetCurrentExecutor(IEventExecutor executor)
    {
        return _mappings.GetAndSet(executor);
    }

    /**
     * Decorate the given {@link Executor} and ensure {@link #currentExecutor()} will return {@code eventExecutor}
     * when called from within the {@link Runnable} during execution.
     */
    public static IExecutor Apply(IExecutor executor, IEventExecutor eventExecutor)
    {
        ObjectUtil.CheckNotNull(executor, "executor");
        ObjectUtil.CheckNotNull(eventExecutor, "eventExecutor");
        return new AnonymousExecutor(command =>
            executor.Execute(Apply(command, eventExecutor))
        );
    }

    // Native worker-entry dispatcher: preserve physical executor mapping without a Runnable adapter.
    public static Action<Action> Apply(Action<Action> executor, IEventExecutor eventExecutor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(eventExecutor);
        return command =>
        {
            ArgumentNullException.ThrowIfNull(command);
            executor(() => RunWithExecutor(command, eventExecutor));
        };
    }

    /**
     * Decorate the given {@link Runnable} and ensure {@link #currentExecutor()} will return {@code eventExecutor}
     * when called from within the {@link Runnable} during execution.
     */
    // CLR callback decoration uses native Action with physical mapping/finally restoration.
    public static Action Apply(Action command, IEventExecutor eventExecutor)
    {
        ObjectUtil.CheckNotNull(command, "command");
        ObjectUtil.CheckNotNull(eventExecutor, "eventExecutor");
        return () => RunWithExecutor(command, eventExecutor);
    }

    /**
     * Decorate the given {@link ThreadFactory} and ensure {@link #currentExecutor()} will return {@code eventExecutor}
     * when called from within the {@link Runnable} during execution.
     */
    public static IThreadFactory Apply(IThreadFactory threadFactory, IEventExecutor eventExecutor)
    {
        ObjectUtil.CheckNotNull(threadFactory, "threadFactory");
        ObjectUtil.CheckNotNull(eventExecutor, "eventExecutor");
        return new AnonymousThreadFactory(r =>
            threadFactory.NewThread(() => RunWithExecutor(r, eventExecutor))
        );
    }

    private static void RunWithExecutor(Action command, IEventExecutor eventExecutor)
    {
        IEventExecutor old = SetCurrentExecutor(eventExecutor);
        try { command(); }
        finally { SetCurrentExecutor(old); }
    }
}
