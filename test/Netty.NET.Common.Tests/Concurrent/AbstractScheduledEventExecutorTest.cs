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

using System;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Void = Netty.NET.Common.Concurrent.Void;

namespace Netty.NET.Common.Tests.Concurrent;

public class AbstractScheduledEventExecutorTest
{
    private static readonly IRunnable TEST_RUNNABLE = Runnables.Empty;

    private static ICallable<Void> TEST_CALLABLE = Executors.callable(TEST_RUNNABLE);

    [Fact]
    public void testScheduleRunnableZero()
    {
        TestScheduledEventExecutor executor = new TestScheduledEventExecutor();
        IScheduledTask future = executor.schedule(TEST_RUNNABLE, TimeSpan.FromTicks(0));
        Assert.Equal(0, future.getDelay(TimeUnit.NANOSECONDS));
        Assert.NotNull(executor.pollScheduledTask());
        Assert.Null(executor.pollScheduledTask());
    }

    [Fact]
    public void testScheduleRunnableNegative()
    {
        TestScheduledEventExecutor executor = new TestScheduledEventExecutor();
        IScheduledTask future = executor.schedule(TEST_RUNNABLE, TimeSpan.FromTicks(-1));
        Assert.Equal(0, future.getDelay(TimeUnit.NANOSECONDS));
        Assert.NotNull(executor.pollScheduledTask());
        Assert.Null(executor.pollScheduledTask());
    }

    [Fact]
    public void testScheduleCallableZero()
    {
        TestScheduledEventExecutor executor = new TestScheduledEventExecutor();
        IScheduledTask future = executor.schedule(TEST_CALLABLE, TimeSpan.FromTicks(0));
        Assert.Equal(0, future.getDelay(TimeUnit.NANOSECONDS));
        Assert.NotNull(executor.pollScheduledTask());
        Assert.Null(executor.pollScheduledTask());
    }

    [Fact]
    public void testScheduleCallableNegative()
    {
        TestScheduledEventExecutor executor = new TestScheduledEventExecutor();
        IScheduledTask future = executor.schedule(TEST_CALLABLE, TimeSpan.FromTicks(-1));
        Assert.Equal(0, future.getDelay(TimeUnit.NANOSECONDS));
        Assert.NotNull(executor.pollScheduledTask());
        Assert.Null(executor.pollScheduledTask());
    }

    [Fact]
    public void testScheduleAtFixedRateRunnableZero()
    {
        TestScheduledEventExecutor executor = new TestScheduledEventExecutor();
        Assert.Throws<ArgumentException>(() =>
        {
            executor.scheduleAtFixedRate(TEST_RUNNABLE, TimeSpan.FromDays(0), TimeSpan.FromDays(0));
        });
    }

    [Fact]
    public void testScheduleAtFixedRateRunnableNegative()
    {
        TestScheduledEventExecutor executor = new TestScheduledEventExecutor();
        Assert.Throws<ArgumentException>(() =>
        {
            executor.scheduleAtFixedRate(TEST_RUNNABLE, TimeSpan.FromDays(0), TimeSpan.FromDays(-1));
        });
    }

    [Fact]
    public void testScheduleWithFixedDelayZero()
    {
        TestScheduledEventExecutor executor = new TestScheduledEventExecutor();
        Assert.Throws<ArgumentException>(() =>
        {
            executor.scheduleWithFixedDelay(TEST_RUNNABLE, TimeSpan.FromDays(0), TimeSpan.FromDays(-1));
        });
    }

    [Fact]
    public void testScheduleWithFixedDelayNegative()
    {
        TestScheduledEventExecutor executor = new TestScheduledEventExecutor();
        Assert.Throws<ArgumentException>(() =>
        {
            executor.scheduleWithFixedDelay(TEST_RUNNABLE, TimeSpan.FromDays(0), TimeSpan.FromDays(-1));
        });
    }

    [Fact]
    public void testDeadlineNanosNotOverflow()
    {
        Assert.Equal(long.MaxValue, AbstractScheduledEventExecutor.deadlineNanos(
            Ticker.systemTicker().nanoTime(), long.MaxValue));
    }

    private sealed class TestScheduledEventExecutor : AbstractScheduledEventExecutor
    {
        public TestScheduledEventExecutor() : base(null)
        {
        }

        public override bool isShuttingDown()
        {
            return false;
        }

        public override bool inEventLoop(Thread thread)
        {
            return true;
        }

        public override void shutdown()
        {
            // NOOP
        }

        public override Task shutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
        {
            throw new NotSupportedException();
        }

        public override Task terminationTask()
        {
            throw new NotSupportedException();
        }

        public override bool isShutdown()
        {
            return false;
        }

        public override bool isTerminated()
        {
            return false;
        }

        public override bool awaitTermination(TimeSpan timeout)
        {
            return false;
        }

        public override void execute(IRunnable command)
        {
            throw new NotSupportedException();
        }
    }
}