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
using Xunit;

namespace Netty.NET.Common.Tests.Concurrent;

public class AbstractScheduledEventExecutorTest
{
    private static readonly Action TEST_RUNNABLE = () => { };
    private static readonly Func<object> TEST_CALLABLE = () => { TEST_RUNNABLE(); return null; };

    [Fact]
    public void TestScheduleRunnableZero()
    {
        var executor = new TestScheduledEventExecutor();
        var future = executor.ScheduleAsync(TEST_RUNNABLE, TimeSpan.Zero);
        Assert.Equal(0, executor.Head.DelayNanos());
        var ready = executor.PollScheduledTask();
        Assert.NotNull(ready);
        Assert.False((object)ready is System.Threading.Tasks.Task);
        ready();
        Assert.True(future.IsCompletedSuccessfully);
        Assert.Null(executor.PollScheduledTask());
    }

    [Fact]
    public void TestScheduleRunnableNegative()
    {
        var executor = new TestScheduledEventExecutor();
        // CLR: the smallest negative TimeSpan is -100 ns; both inputs clamp to zero.
        var future = executor.ScheduleAsync(TEST_RUNNABLE, TimeSpan.FromTicks(-1));
        Assert.Equal(0, executor.Head.DelayNanos());
        var ready = executor.PollScheduledTask();
        Assert.NotNull(ready);
        Assert.False((object)ready is System.Threading.Tasks.Task);
        ready();
        Assert.True(future.IsCompletedSuccessfully);
        Assert.Null(executor.PollScheduledTask());
    }

    [Fact]
    public void TestScheduleCallableZero()
    {
        var executor = new TestScheduledEventExecutor();
        var future = executor.ScheduleAsync(TEST_CALLABLE, TimeSpan.Zero);
        Assert.Equal(0, executor.Head.DelayNanos());
        var ready = executor.PollScheduledTask();
        Assert.NotNull(ready);
        Assert.False((object)ready is System.Threading.Tasks.Task);
        ready();
        Assert.True(future.IsCompletedSuccessfully);
        Assert.Null(executor.PollScheduledTask());
    }

    [Fact]
    public void TestScheduleCallableNegative()
    {
        var executor = new TestScheduledEventExecutor();
        var future = executor.ScheduleAsync(TEST_CALLABLE, TimeSpan.FromTicks(-1));
        Assert.Equal(0, executor.Head.DelayNanos());
        var ready = executor.PollScheduledTask();
        Assert.NotNull(ready);
        Assert.False((object)ready is System.Threading.Tasks.Task);
        ready();
        Assert.True(future.IsCompletedSuccessfully);
        Assert.Null(executor.PollScheduledTask());
    }

    [Fact]
    public void TestScheduleAtFixedRateRunnableZero()
    {
        var executor = new TestScheduledEventExecutor();
        Assert.Throws<ArgumentOutOfRangeException>(() => executor.ScheduleAtFixedRateAsync(TEST_RUNNABLE, TimeSpan.Zero, TimeSpan.Zero));
    }

    [Fact]
    public void TestScheduleAtFixedRateRunnableNegative()
    {
        var executor = new TestScheduledEventExecutor();
        Assert.Throws<ArgumentOutOfRangeException>(() => executor.ScheduleAtFixedRateAsync(TEST_RUNNABLE, TimeSpan.Zero, TimeSpan.FromDays(-1)));
    }

    [Fact]
    public void TestScheduleWithFixedDelayZero()
    {
        var executor = new TestScheduledEventExecutor();
        // CLR: retain the pinned upstream's -1 operand despite the method name.
        Assert.Throws<ArgumentOutOfRangeException>(() => executor.ScheduleWithFixedDelayAsync(TEST_RUNNABLE, TimeSpan.Zero, TimeSpan.FromDays(-1)));
    }

    [Fact]
    public void TestScheduleWithFixedDelayNegative()
    {
        var executor = new TestScheduledEventExecutor();
        Assert.Throws<ArgumentOutOfRangeException>(() => executor.ScheduleWithFixedDelayAsync(TEST_RUNNABLE, TimeSpan.Zero, TimeSpan.FromDays(-1)));
    }

    [Fact]
    public void TestDeadlineNanosNotOverflow() =>
        Assert.Equal(long.MaxValue, AbstractScheduledEventExecutor.DeadlineNanos(Ticker.SystemTicker().NanoTime(), long.MaxValue));

    private sealed class TestScheduledEventExecutor : AbstractScheduledEventExecutor
    {
        internal IScheduledWork Head => PeekScheduledTask();
        public override bool IsShuttingDown() => false;
        public override bool InEventLoop(Thread thread) => true;
        public override void Shutdown()
        {
            // NOOP
        }
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => throw new NotSupportedException();
        public override Task Termination => throw new NotSupportedException();
        public override bool IsShutdown() => false;
        public override bool IsTerminated() => false;
        public override bool AwaitTermination(TimeSpan timeout) => false;
        public override void Execute(Action command)
        {
            ArgumentNullException.ThrowIfNull(command);
            throw new NotSupportedException();
        }
    }
}
