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
namespace Netty.NET.Common.Internal;


/**
 * Allow to retrieve the {@link IEventExecutor} for the calling {@link Thread}.
 */
public final class ThreadExecutorMap {

    private static readonly FastThreadLocal<IEventExecutor> mappings = new FastThreadLocal<IEventExecutor>();

    private ThreadExecutorMap() { }

    /**
     * Returns the current {@link IEventExecutor} that uses the {@link Thread}, or {@code null} if none / unknown.
     */
    public static IEventExecutor currentExecutor() {
        return mappings.get();
    }

    /**
     * Set the current {@link IEventExecutor} that is used by the {@link Thread}.
     */
    public static IEventExecutor setCurrentExecutor(IEventExecutor executor) {
        return mappings.getAndSet(executor);
    }

    /**
     * Decorate the given {@link Executor} and ensure {@link #currentExecutor()} will return {@code eventExecutor}
     * when called from within the {@link Runnable} during execution.
     */
    public static Executor apply(final Executor executor, final IEventExecutor eventExecutor) {
        ObjectUtil.checkNotNull(executor, "executor");
        ObjectUtil.checkNotNull(eventExecutor, "eventExecutor");
        return new Executor() {
            @Override
            public void execute(final Runnable command) {
                executor.execute(apply(command, eventExecutor));
            }
        };
    }

    /**
     * Decorate the given {@link Runnable} and ensure {@link #currentExecutor()} will return {@code eventExecutor}
     * when called from within the {@link Runnable} during execution.
     */
    public static Runnable apply(final Runnable command, final IEventExecutor eventExecutor) {
        ObjectUtil.checkNotNull(command, "command");
        ObjectUtil.checkNotNull(eventExecutor, "eventExecutor");
        return new Runnable() {
            @Override
            public void run() {
                IEventExecutor old = setCurrentExecutor(eventExecutor);
                try {
                    command.run();
                } finally {
                    setCurrentExecutor(old);
                }
            }
        };
    }

    /**
     * Decorate the given {@link IThreadFactory} and ensure {@link #currentExecutor()} will return {@code eventExecutor}
     * when called from within the {@link Runnable} during execution.
     */
    public static IThreadFactory apply(final IThreadFactory threadFactory, final IEventExecutor eventExecutor) {
        ObjectUtil.checkNotNull(threadFactory, "threadFactory");
        ObjectUtil.checkNotNull(eventExecutor, "eventExecutor");
        return new IThreadFactory() {
            @Override
            public Thread newThread(Runnable r) {
                return threadFactory.newThread(apply(r, eventExecutor));
            }
        };
    }
}
