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
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Internal;

/**
 * Allow to retrieve the {@link IEventExecutor} for the calling {@link Thread}.
 */
public static class ThreadExecutorMap
{
    private static readonly FastThreadLocal<IEventExecutor> _mappings = new FastThreadLocal<IEventExecutor>();

    /**
     * Returns the current {@link IEventExecutor} that uses the {@link Thread}, or {@code null} if none / unknown.
     */
    public static IEventExecutor currentExecutor()
    {
        return _mappings.get();
    }

    /**
     * Set the current {@link IEventExecutor} that is used by the {@link Thread}.
     */
    public static IEventExecutor setCurrentExecutor(IEventExecutor executor)
    {
        return _mappings.getAndSet(executor);
    }

    /**
     * Decorate the given {@link IExecutor} and ensure {@link #currentExecutor()} will return {@code eventExecutor}
     * when called from within the {@link IRunnable} during execution.
     */
    public static IExecutor apply(IExecutor executor, IEventExecutor eventExecutor)
    {
        ObjectUtil.checkNotNull(executor, "executor");
        ObjectUtil.checkNotNull(eventExecutor, "eventExecutor");
        return new AnonymousExecutor(command =>
            executor.execute(apply(command, eventExecutor))
        );
    }

    /**
     * Decorate the given {@link IRunnable} and ensure {@link #currentExecutor()} will return {@code eventExecutor}
     * when called from within the {@link IRunnable} during execution.
     */
    public static IRunnable apply(IRunnable command, IEventExecutor eventExecutor)
    {
        ObjectUtil.checkNotNull(command, "command");
        ObjectUtil.checkNotNull(eventExecutor, "eventExecutor");
        return AnonymousRunnable.Create(() =>
        {
            IEventExecutor old = setCurrentExecutor(eventExecutor);
            try
            {
                command.run();
            }
            finally
            {
                setCurrentExecutor(old);
            }
        });
    }

    /**
     * Decorate the given {@link IThreadFactory} and ensure {@link #currentExecutor()} will return {@code eventExecutor}
     * when called from within the {@link IRunnable} during execution.
     */
    public static IThreadFactory apply(IThreadFactory threadFactory, IEventExecutor eventExecutor)
    {
        ObjectUtil.checkNotNull(threadFactory, "threadFactory");
        ObjectUtil.checkNotNull(eventExecutor, "eventExecutor");
        return new AnonymousThreadFactory(r =>
            threadFactory.newThread(apply(r, eventExecutor))
        );
    }
}