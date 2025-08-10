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
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;


namespace Netty.NET.Common.Concurrent;

/**
 * Executes {@link Runnable} objects in the caller's thread. If the {@link #execute(Runnable)} is reentrant it will be
 * queued until the original {@link Runnable} finishes execution.
 * <p>
 * All {@link Exception} objects thrown from {@link #execute(Runnable)} will be swallowed and logged. This is to ensure
 * that all queued {@link Runnable} objects have the chance to be run.
 */
public class ImmediateEventExecutor : AbstractEventExecutor 
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(ImmediateEventExecutor));
    public static readonly ImmediateEventExecutor INSTANCE = new ImmediateEventExecutor();
    /**
     * A Runnable will be queued if we are executing a Runnable. This is to prevent a {@link StackOverflowError}.
     */
    private static readonly FastThreadLocal<Queue<IRunnable>> DELAYED_RUNNABLES = new FastThreadLocal<Queue<IRunnable>>() {
        @Override
        protected Queue<Runnable> initialValue() {
            return new ArrayDeque<Runnable>();
        }
    };
    /**
     * Set to {@code true} if we are executing a runnable.
     */
    private static readonly FastThreadLocal<bool> RUNNING = new FastThreadLocal<bool>() {
        @Override
        protected bool initialValue() {
            return false;
        }
    };

    private readonly Future<?> terminationFuture = new FailedFuture<object>(
            GlobalEventExecutor.INSTANCE, new NotSupportedException());

    private ImmediateEventExecutor() { }

    @Override
    public bool inEventLoop() {
        return true;
    }

    @Override
    public bool inEventLoop(Thread thread) {
        return true;
    }

    @Override
    public Future<?> shutdownGracefully(long quietPeriod, long timeout, TimeSpan unit) {
        return terminationFuture();
    }

    @Override
    public Future<?> terminationFuture() {
        return terminationFuture;
    }

    @Override
    @Deprecated
    public void shutdown() { }

    @Override
    public bool isShuttingDown() {
        return false;
    }

    @Override
    public bool isShutdown() {
        return false;
    }

    @Override
    public bool isTerminated() {
        return false;
    }

    @Override
    public bool awaitTermination(long timeout, TimeSpan unit) {
        return false;
    }

    @Override
    public void execute(Runnable command) {
        ObjectUtil.checkNotNull(command, "command");
        if (!RUNNING.get()) {
            RUNNING.set(true);
            try {
                command.run();
            } catch (Exception cause) {
                logger.info("Exception caught while executing Runnable {}", command, cause);
            } finally {
                Queue<Runnable> delayedRunnables = DELAYED_RUNNABLES.get();
                Runnable runnable;
                while ((runnable = delayedRunnables.poll()) != null) {
                    try {
                        runnable.run();
                    } catch (Exception cause) {
                        logger.info("Exception caught while executing Runnable {}", runnable, cause);
                    }
                }
                RUNNING.set(false);
            }
        } else {
            DELAYED_RUNNABLES.get().add(command);
        }
    }

    @Override
    public <V> Promise<V> newPromise() {
        return new ImmediatePromise<V>(this);
    }

    @Override
    public <V> ProgressivePromise<V> newProgressivePromise() {
        return new ImmediateProgressivePromise<V>(this);
    }

    static class ImmediatePromise<V> extends DefaultPromise<V> {
        ImmediatePromise(IEventExecutor executor) {
            super(executor);
        }

        @Override
        protected void checkDeadLock() {
            // No check
        }
    }

    static class ImmediateProgressivePromise<V> extends DefaultProgressivePromise<V> {
        ImmediateProgressivePromise(IEventExecutor executor) {
            super(executor);
        }

        @Override
        protected void checkDeadLock() {
            // No check
        }
    }
}
