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
using Xunit;
using Void = Netty.NET.Common.Concurrent.Void;

namespace Netty.NET.Common.Tests.Concurrent;

public class AbstractScheduledEventExecutorTest
{
    private static readonly Action TEST_RUNNABLE = () => { };
    private static readonly Func<object> TEST_CALLABLE = () => { TEST_RUNNABLE(); return null; };

    [Fact]
    public void testScheduleRunnableZero()
    {
        var executor = new TestScheduledEventExecutor();
        var future = executor.ScheduleAsync(TEST_RUNNABLE, TimeSpan.Zero);
        Assert.Equal(0, executor.Head.delayNanos());
        var ready = executor.pollScheduledTask();
        Assert.NotNull(ready);
        Assert.False((object)ready is System.Threading.Tasks.Task);
        ready.run();
        Assert.True(future.IsCompletedSuccessfully);
        Assert.Null(executor.pollScheduledTask());
    }

    [Fact]
    public void testScheduleRunnableNegative()
    {
        var executor = new TestScheduledEventExecutor();
        // CLR: the smallest negative TimeSpan is -100 ns; both inputs clamp to zero.
        var future = executor.ScheduleAsync(TEST_RUNNABLE, TimeSpan.FromTicks(-1));
        Assert.Equal(0, executor.Head.delayNanos());
        var ready = executor.pollScheduledTask();
        Assert.NotNull(ready);
        Assert.False((object)ready is System.Threading.Tasks.Task);
        ready.run();
        Assert.True(future.IsCompletedSuccessfully);
        Assert.Null(executor.pollScheduledTask());
    }

    [Fact]
    public void testScheduleCallableZero()
    {
        var executor = new TestScheduledEventExecutor();
        var future = executor.ScheduleAsync(TEST_CALLABLE, TimeSpan.Zero);
        Assert.Equal(0, executor.Head.delayNanos());
        var ready = executor.pollScheduledTask();
        Assert.NotNull(ready);
        Assert.False((object)ready is System.Threading.Tasks.Task);
        ready.run();
        Assert.True(future.IsCompletedSuccessfully);
        Assert.Null(executor.pollScheduledTask());
    }

    [Fact]
    public void testScheduleCallableNegative()
    {
        var executor = new TestScheduledEventExecutor();
        var future = executor.ScheduleAsync(TEST_CALLABLE, TimeSpan.FromTicks(-1));
        Assert.Equal(0, executor.Head.delayNanos());
        var ready = executor.pollScheduledTask();
        Assert.NotNull(ready);
        Assert.False((object)ready is System.Threading.Tasks.Task);
        ready.run();
        Assert.True(future.IsCompletedSuccessfully);
        Assert.Null(executor.pollScheduledTask());
    }

    [Fact]
    public void testScheduleAtFixedRateRunnableZero()
    {
        var executor = new TestScheduledEventExecutor();
        Assert.Throws<ArgumentOutOfRangeException>(() => executor.ScheduleAtFixedRateAsync(TEST_RUNNABLE, TimeSpan.Zero, TimeSpan.Zero));
    }

    [Fact]
    public void testScheduleAtFixedRateRunnableNegative()
    {
        var executor = new TestScheduledEventExecutor();
        Assert.Throws<ArgumentOutOfRangeException>(() => executor.ScheduleAtFixedRateAsync(TEST_RUNNABLE, TimeSpan.Zero, TimeSpan.FromDays(-1)));
    }

    [Fact]
    public void testScheduleWithFixedDelayZero()
    {
        var executor = new TestScheduledEventExecutor();
        // CLR: retain the pinned upstream's -1 operand despite the method name.
        Assert.Throws<ArgumentOutOfRangeException>(() => executor.ScheduleWithFixedDelayAsync(TEST_RUNNABLE, TimeSpan.Zero, TimeSpan.FromDays(-1)));
    }

    [Fact]
    public void testScheduleWithFixedDelayNegative()
    {
        var executor = new TestScheduledEventExecutor();
        Assert.Throws<ArgumentOutOfRangeException>(() => executor.ScheduleWithFixedDelayAsync(TEST_RUNNABLE, TimeSpan.Zero, TimeSpan.FromDays(-1)));
    }

    [Fact]
    public void testDeadlineNanosNotOverflow() =>
        Assert.Equal(long.MaxValue, AbstractScheduledEventExecutor.deadlineNanos(Ticker.systemTicker().nanoTime(), long.MaxValue));

    private sealed class TestScheduledEventExecutor : AbstractScheduledEventExecutor
    {
        internal IScheduledWork Head => peekScheduledTask();
        public override bool isShuttingDown() => false;
        public override bool inEventLoop(Thread thread) => true;
        public override void shutdown()
        {
            // NOOP
        }
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => throw new NotSupportedException();
        public override Task Termination => throw new NotSupportedException();
        public override bool isShutdown() => false;
        public override bool isTerminated() => false;
        public override bool awaitTermination(TimeSpan timeout) => false;
        public override void execute(IRunnable command) => throw new NotSupportedException();
    }
}
