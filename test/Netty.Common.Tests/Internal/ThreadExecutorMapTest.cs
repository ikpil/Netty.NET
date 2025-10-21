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
namespace Netty.Common.Tests.Internal;
public class ThreadExecutorMapTest {
    private static final EventExecutor EVENT_EXECUTOR = new AbstractEventExecutor() {
        @Override
        public void shutdown() {
            throw new NotSupportedException();
        }

        @Override
        public bool inEventLoop(Thread thread) {
            return false;
        }

        @Override
        public bool isShuttingDown() {
            return false;
        }

        @Override
        public Future<?> shutdownGracefully(long quietPeriod, long timeout, TimeUnit unit) {
            throw new NotSupportedException();
        }

        @Override
        public Task terminationTask() {
            throw new NotSupportedException();
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
        public bool awaitTermination(long timeout, @NotNull TimeUnit unit) {
            return false;
        }

        @Override
        public void execute(@NotNull IRunnable command) {
            throw new NotSupportedException();
        }
    };

    [Fact]
    public void testOldExecutorIsRestored() {
        IExecutor executor = ThreadExecutorMap.apply(ImmediateExecutor.INSTANCE, ImmediateEventExecutor.INSTANCE);
        IExecutor executor2 = ThreadExecutorMap.apply(ImmediateExecutor.INSTANCE, EVENT_EXECUTOR);
        executor.execute(new IRunnable() {
            @Override
            public void run() {
                executor2.execute(new IRunnable() {
                    @Override
                    public void run() {
                        Assert.Same(EVENT_EXECUTOR, ThreadExecutorMap.currentExecutor());
                    }
                });
                Assert.Same(ImmediateEventExecutor.INSTANCE, ThreadExecutorMap.currentExecutor());
            }
        });
    }

    [Fact]
    public void testDecorateExecutor() {
        IExecutor executor = ThreadExecutorMap.apply(ImmediateExecutor.INSTANCE, ImmediateEventExecutor.INSTANCE);
        executor.execute(new IRunnable() {
            @Override
            public void run() {
                Assert.Same(ImmediateEventExecutor.INSTANCE, ThreadExecutorMap.currentExecutor());
            }
        });
    }

    [Fact]
    public void testDecorateRunnable() {
        ThreadExecutorMap.apply(new IRunnable() {
            @Override
            public void run() {
                Assert.Same(ImmediateEventExecutor.INSTANCE,
                        ThreadExecutorMap.currentExecutor());
            }
        }, ImmediateEventExecutor.INSTANCE).run();
    }

    [Fact]
    public void testDecorateThreadFactory() {
        IThreadFactory threadFactory =
                ThreadExecutorMap.apply(Executors.defaultThreadFactory(), ImmediateEventExecutor.INSTANCE);
        Thread thread = threadFactory.newThread(new IRunnable() {
            @Override
            public void run() {
                Assert.Same(ImmediateEventExecutor.INSTANCE, ThreadExecutorMap.currentExecutor());
            }
        });
        thread.start();
        thread.join();
    }
}
