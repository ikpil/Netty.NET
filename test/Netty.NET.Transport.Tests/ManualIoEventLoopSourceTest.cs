/*
 * Copyright 2025 The Netty Project
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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1031 // The source drives the loop synchronously on one physical thread.
public class ManualIoEventLoopSourceTest
{
    [Fact]
    public void TestRunNow()
    {
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        try
        {
            Assert.Equal(0, loop.RunNow());
            bool done = false;
            loop.Execute(() => done = true);
            Assert.False(done);
            Assert.Equal(1, loop.RunNow());
            Assert.True(done);
        }
        finally { Terminate(loop); }
    }

    [Fact]
    public void TestRun()
    {
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        try
        {
            var clock = Stopwatch.StartNew();
            Assert.Equal(0, loop.Run(200_000_000));
            Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(200));

            bool done = false;
            loop.Execute(() => done = true);
            Assert.False(done);
            clock.Restart();
            Assert.Equal(1, loop.Run(1_000_000_000));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1));
            Assert.True(done);
        }
        finally { Terminate(loop); }
    }

    [Fact(Timeout = 5000)]
    public async Task TestShutdownOutSideOfOwningThread()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var returned = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        ManualIoEventLoop loop = null;
        var owner = new Thread(() =>
        {
            try { returned.SetResult(loop.Run(0)); }
            catch (Exception error) { returned.SetException(error); }
        }) { IsBackground = true };
        loop = new ManualIoEventLoop(owner);
        owner.Start();
        try
        {
            Assert.True(SpinWait.SpinUntil(() => (owner.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0,
                TimeSpan.FromSeconds(2)));
            var termination = loop.StopAsync();
            Assert.True(loop.IsShuttingDown());
            // we expect wakeup to be called!
            Assert.Equal(0, await returned.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellation));
            await termination.WaitAsync(cancellation);
        }
        finally { _ = loop.StopAsync(); }
    }

    [Fact]
    public void TestCallFromWrongThread()
    {
        var loop = new ManualIoEventLoop(new Thread(() => { }));
        Assert.Throws<InvalidOperationException>(() => loop.RunNow());
        Assert.Throws<InvalidOperationException>(() => loop.Run(10));
    }

    [Fact]
    public void TestThreadEventExecutorMap()
    {
        var loop = new ManualIoEventLoop(Thread.CurrentThread);
        try
        {
            IEventExecutor seen = null;
            Assert.Null(ThreadExecutorMap.CurrentExecutor());
            loop.Execute(() => seen = ThreadExecutorMap.CurrentExecutor());
            Assert.Equal(1, loop.RunNow());
            Assert.Same(loop, seen);
            Assert.Null(ThreadExecutorMap.CurrentExecutor());
        }
        finally { Terminate(loop); }
    }

    [Fact(Timeout = 10000)]
    public async Task TestDelayOwningThread()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var loop = new ManualIoEventLoop(null);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownerBound = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new Thread(() =>
        {
            try
            {
                loop.SetOwningThread(Thread.CurrentThread);
                ownerBound.SetResult(loop.InEventLoop());
                while (!loop.IsTerminated()) loop.Run(0);
            }
            catch (Exception error)
            {
                ownerBound.TrySetException(error);
                completion.TrySetException(error);
            }
        }) { IsBackground = true };

        Assert.False(loop.InEventLoop());
        loop.Execute(() => completion.SetResult(loop.InEventLoop()));
        owner.Start();
        try
        {
            Assert.True(await ownerBound.Task.WaitAsync(cancellation));
            Assert.True(await completion.Task.WaitAsync(cancellation));
            await loop.ShutdownGracefullyAsync().WaitAsync(cancellation);
        }
        finally
        {
            _ = loop.StopAsync();
            Assert.True(owner.Join(TimeSpan.FromSeconds(3)));
        }
    }

    [Fact]
    public void TestRunWithoutOwner()
    {
        var loop = new ManualIoEventLoop(null);
        // prior to setOwningThread, runNow is forbidden
        Assert.Throws<InvalidOperationException>(() => loop.RunNow());
        loop.SetOwningThread(Thread.CurrentThread);
        try { loop.RunNow(); } // runs fine
        finally { Terminate(loop); }
    }

    [Fact]
    public void TestRunNonIoTasksSetupEventExecutor()
    {
        var loop = new ManualIoEventLoop(Thread.CurrentThread, ticker: Ticker.NewMockTicker());
        try
        {
            Assert.Null(ThreadExecutorMap.CurrentExecutor());
            bool executed = false;
            IEventExecutor seen = null;
            var task = loop.SubmitAsync(() =>
            {
                Assert.False(executed);
                executed = true;
                seen = ThreadExecutorMap.CurrentExecutor();
            }, TestContext.Current.CancellationToken);
            Assert.Equal(1, loop.RunNonBlockingTasks(0));
            task.GetAwaiter().GetResult();
            Assert.True(executed);
            Assert.Same(loop, seen);
            Assert.Null(ThreadExecutorMap.CurrentExecutor());
        }
        finally { Terminate(loop); }
    }

    public enum RunMode { Now, Wait, NonIoNow }

    [Theory]
    [InlineData(RunMode.Now)]
    [InlineData(RunMode.Wait)]
    [InlineData(RunMode.NonIoNow)]
    public void TestTasksWhileExpiringTimeout(RunMode mode)
    {
        var ticker = Ticker.NewMockTicker();
        var loop = new ManualIoEventLoop(Thread.CurrentThread, ticker: ticker);
        try
        {
            bool executedFirst = false;
            var first = loop.SubmitAsync(() =>
            {
                Assert.False(executedFirst);
                executedFirst = true;
                ticker.Advance(1);
            }, TestContext.Current.CancellationToken);
            bool canExecute = false;
            bool executedSecond = false;
            var second = loop.SubmitAsync(() =>
            {
                Assert.True(canExecute, "Should not be executed");
                Assert.False(executedSecond);
                executedSecond = true;
            }, TestContext.Current.CancellationToken);

            Assert.Equal(1, Run());
            first.GetAwaiter().GetResult();
            Assert.True(executedFirst);
            Assert.False(executedSecond);
            canExecute = true;
            Assert.Equal(1, Run());
            second.GetAwaiter().GetResult();

            int Run() => mode switch
            {
                RunMode.Now => loop.RunNow(1),
                RunMode.Wait => loop.Run(3_600_000_000_000, 1),
                RunMode.NonIoNow => loop.RunNonBlockingTasks(1),
                _ => throw new InvalidOperationException($"Unknown run mode: {mode}")
            };
        }
        finally { Terminate(loop); }
    }

    [Fact]
    public void TestSetOwnerMultipleTimes()
    {
        var loop = new ManualIoEventLoop(null);
        loop.SetOwningThread(Thread.CurrentThread);
        try { Assert.Throws<InvalidOperationException>(() => loop.SetOwningThread(Thread.CurrentThread)); }
        finally { Terminate(loop); }
    }

    [Fact]
    public void TestTicker()
    {
        var ticker = Ticker.NewMockTicker();
        var loop = new ManualIoEventLoop(Thread.CurrentThread, ticker: ticker);
        try
        {
            int counter = 0;
            var scheduled = loop.ScheduleAsync(() => counter++, TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
            loop.RunNow();
            Assert.Equal(0, counter);
            ticker.Advance(50_000_000_000);
            loop.RunNow();
            Assert.Equal(0, counter);
            ticker.Advance(20_000_000_000);
            loop.RunNow();
            Assert.Equal(1, counter);
            Assert.True(scheduled.IsCompletedSuccessfully);
        }
        finally { Terminate(loop); }
    }

    internal static void Terminate(ManualIoEventLoop loop)
    {
        var termination = loop.StopAsync();
        loop.RunNow();
        Assert.True(loop.IsTerminated());
        termination.GetAwaiter().GetResult();
    }
}
