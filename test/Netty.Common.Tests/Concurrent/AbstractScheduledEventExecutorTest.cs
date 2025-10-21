/*
 * Copyright 2017 The Netty Project
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

namespace Netty.Common.Tests.Concurrent;

public class AbstractScheduledEventExecutorTest
{
    private static readonly IRunnable TEST_RUNNABLE = EmptyRunnable.Shared;

    private static final Callable<?> TEST_CALLABLE = Executors.callable(TEST_RUNNABLE);

    [Fact]
    public void testScheduleRunnableZero() {
        TestScheduledEventExecutor executor = new TestScheduledEventExecutor();
        ScheduledFuture<?> future = executor.schedule(TEST_RUNNABLE, 0, TimeUnit.NANOSECONDS);
        Assert.Equal(0, future.getDelay(TimeUnit.NANOSECONDS));
        Assert.NotNull(executor.pollScheduledTask());
        Assert.Null(executor.pollScheduledTask());
    }

    [Fact]
    public void testScheduleRunnableNegative() {
        TestScheduledEventExecutor executor = new TestScheduledEventExecutor();
        ScheduledFuture<?> future = executor.schedule(TEST_RUNNABLE, -1, TimeUnit.NANOSECONDS);
        Assert.Equal(0, future.getDelay(TimeUnit.NANOSECONDS));
        Assert.NotNull(executor.pollScheduledTask());
        Assert.Null(executor.pollScheduledTask());
    }

    [Fact]
    public void testScheduleCallableZero() {
        TestScheduledEventExecutor executor = new TestScheduledEventExecutor();
        ScheduledFuture<?> future = executor.schedule(TEST_CALLABLE, 0, TimeUnit.NANOSECONDS);
        Assert.Equal(0, future.getDelay(TimeUnit.NANOSECONDS));
        Assert.NotNull(executor.pollScheduledTask());
        Assert.Null(executor.pollScheduledTask());
    }

    [Fact]
    public void testScheduleCallableNegative() {
        TestScheduledEventExecutor executor = new TestScheduledEventExecutor();
        ScheduledFuture<?> future = executor.schedule(TEST_CALLABLE, -1, TimeUnit.NANOSECONDS);
        Assert.Equal(0, future.getDelay(TimeUnit.NANOSECONDS));
        Assert.NotNull(executor.pollScheduledTask());
        Assert.Null(executor.pollScheduledTask());
    }

    [Fact]
    public void testScheduleAtFixedRateRunnableZero() {
        final TestScheduledEventExecutor executor = new TestScheduledEventExecutor();
        Assert.Throws<ArgumentException>(new Executable() {
            @Override
            public void execute() {
                executor.scheduleAtFixedRate(TEST_RUNNABLE, 0, 0, TimeUnit.DAYS);
            }
        });
    }

    [Fact]
    public void testScheduleAtFixedRateRunnableNegative() {
        final TestScheduledEventExecutor executor = new TestScheduledEventExecutor();
        Assert.Throws<ArgumentException>(new Executable() {
            @Override
            public void execute() {
                executor.scheduleAtFixedRate(TEST_RUNNABLE, 0, -1, TimeUnit.DAYS);
            }
        });
    }

    [Fact]
    public void testScheduleWithFixedDelayZero() {
        final TestScheduledEventExecutor executor = new TestScheduledEventExecutor();
        Assert.Throws<ArgumentException>(new Executable() {
            @Override
            public void execute() {
                executor.scheduleWithFixedDelay(TEST_RUNNABLE, 0, -1, TimeUnit.DAYS);
            }
        });
    }

    [Fact]
    public void testScheduleWithFixedDelayNegative() {
        final TestScheduledEventExecutor executor = new TestScheduledEventExecutor();
        Assert.Throws<ArgumentException>(new Executable() {
            @Override
            public void execute() {
                executor.scheduleWithFixedDelay(TEST_RUNNABLE, 0, -1, TimeUnit.DAYS);
            }
        });
    }

    [Fact]
    public void testDeadlineNanosNotOverflow() {
        Assertions.Assert.Equal(long.MaxValue, AbstractScheduledEventExecutor.deadlineNanos(
                Ticker.systemTicker().nanoTime(), long.MaxValue));
    }

    private static final class TestScheduledEventExecutor extends AbstractScheduledEventExecutor {
        @Override
        public bool isShuttingDown() {
            return false;
        }

        @Override
        public bool inEventLoop(Thread thread) {
            return true;
        }

        @Override
        public void shutdown() {
            // NOOP
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
        public bool awaitTermination(long timeout, TimeUnit unit) {
            return false;
        }

        @Override
        public void execute(IRunnable command) {
            throw new NotSupportedException();
        }
    }
}
