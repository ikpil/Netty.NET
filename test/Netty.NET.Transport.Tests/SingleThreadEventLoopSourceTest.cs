/*
 * Copyright 2012 The Netty Project
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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

#pragma warning disable CS0612, CS0618, xUnit1031, xUnit1051 // Original legacy shutdown and blocking scheduling semantics.
public class SingleThreadEventLoopSourceTest : IAsyncLifetime
{
    private static readonly Action Noop = () => { };
    private readonly LoopA _a = new();
    private readonly LoopB _b = new();
    private readonly LoopC _c = new();
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    public async ValueTask DisposeAsync()
    {
        foreach (var loop in new SingleThreadEventLoop[] { _a, _b, _c })
            if (!loop.IsShuttingDown()) _ = loop.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
        foreach (var loop in new SingleThreadEventLoop[] { _a, _b, _c })
            await loop.Termination.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, _a.CleanedUp);
    }

    [Fact(Timeout = 10000)]
    public async Task ShutdownBeforeStart()
    {
        _a.Shutdown();
        AssertRejection(_a);
        await _a.Termination.WaitAsync(TestContext.Current.CancellationToken);
    }
    [Fact(Timeout = 10000)]
    public async Task ShutdownAfterStart()
    {
        var latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _a.Execute(() => latch.SetResult());
        // Wait for the event loop thread to start.
        await latch.Task.WaitAsync(TestContext.Current.CancellationToken);
        // Request the event loop thread to stop.
        _a.Shutdown();
        AssertRejection(_a);
        Assert.True(_a.IsShutdown());
        // Wait until the event loop is terminated.
        await _a.Termination.WaitAsync(TestContext.Current.CancellationToken);
    }
    private static void AssertRejection(IEventExecutor loop) => Assert.Throws<RejectedExecutionException>(() => loop.Execute(Noop));

    [Fact(Timeout = 10000)] public Task ScheduleTaskA() => ScheduleTask(_a).WaitAsync(TestContext.Current.CancellationToken);
    [Fact(Timeout = 10000)] public Task ScheduleTaskB() => ScheduleTask(_b).WaitAsync(TestContext.Current.CancellationToken);
    [Fact(Timeout = 10000)] public Task ScheduleTaskC() => ScheduleTask(_c).WaitAsync(TestContext.Current.CancellationToken);
    private static async Task ScheduleTask(IEventLoop loop)
    {
        long start = Stopwatch.GetTimestamp(), end = 0;
        await loop.ScheduleAsync(() => { end = Stopwatch.GetTimestamp(); }, TimeSpan.FromMilliseconds(500));
        Assert.True(Stopwatch.GetElapsedTime(start, end) >= TimeSpan.FromMilliseconds(500));
    }

    [Fact(Timeout = 5000)] public Task ScheduleTaskAtFixedRateA() => FixedRate(_a, false).WaitAsync(TestContext.Current.CancellationToken);
    [Fact(Timeout = 5000)] public Task ScheduleTaskAtFixedRateB() => FixedRate(_b, false).WaitAsync(TestContext.Current.CancellationToken);
    [Fact(Timeout = 5000)] public Task ScheduleLaggyTaskAtFixedRateA() => FixedRate(_a, true).WaitAsync(TestContext.Current.CancellationToken);
    [Fact(Timeout = 5000)] public Task ScheduleLaggyTaskAtFixedRateB() => FixedRate(_b, true).WaitAsync(TestContext.Current.CancellationToken);
    private static async Task FixedRate(IEventLoop loop, bool laggy)
    {
        var timestamps = new ConcurrentQueue<long>();
        const int expectedTimeStamps = 5;
        using var latch = new CountdownEvent(expectedTimeStamps);
        using var cancel = new CancellationTokenSource();
        var periodic = loop.ScheduleAtFixedRateAsync(() =>
        {
            bool empty = timestamps.IsEmpty;
            timestamps.Enqueue(Stopwatch.GetTimestamp());
            if (!laggy || empty) SleepIgnoringInterrupt(laggy ? 401 : 50);
            latch.Signal();
        }, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), cancel.Token);
        await Task.Run(() => latch.Wait(TestContext.Current.CancellationToken));
        cancel.Cancel();
        Assert.True(periodic.IsCanceled);
        await Task.Delay(300);
        Assert.Equal(expectedTimeStamps, timestamps.Count);
        var values = timestamps.ToArray();
        if (laggy)
        {
            // Check if the task was run with lag.
            for (int i = 1; i < values.Length; i++)
            {
                var diff = Stopwatch.GetElapsedTime(values[i - 1], values[i]);
                if (i == 1) Assert.True(diff >= TimeSpan.FromMilliseconds(400), diff.ToString());
                else Assert.True(diff <= TimeSpan.FromMilliseconds(10), diff.ToString());
            }
        }
        else
        {
            // Check if the task was run without a lag.
            for (int i = 1; i < values.Length; i++)
            {
                var timepoint = Stopwatch.GetElapsedTime(values[0], values[i]);
                Assert.True(timepoint >= TimeSpan.FromMilliseconds(100 * (i - 1) + 80), timepoint.ToString());
                Assert.True(timepoint < TimeSpan.FromMilliseconds(100 * i + 20), timepoint.ToString());
            }
        }
    }
    private static void SleepIgnoringInterrupt(int milliseconds)
    {
        try { Thread.Sleep(milliseconds); }
        catch (ThreadInterruptedException) { /* Ignore */ }
    }

    [Fact(Timeout = 5000)] public Task ScheduleTaskWithFixedDelayA() => FixedDelay(_a).WaitAsync(TestContext.Current.CancellationToken);
    [Fact(Timeout = 5000)] public Task ScheduleTaskWithFixedDelayB() => FixedDelay(_b).WaitAsync(TestContext.Current.CancellationToken);
    private static async Task FixedDelay(IEventLoop loop)
    {
        var timestamps = new ConcurrentQueue<long>();
        const int expectedTimeStamps = 3;
        using var latch = new CountdownEvent(expectedTimeStamps);
        using var cancel = new CancellationTokenSource();
        var periodic = loop.ScheduleWithFixedDelayAsync(() =>
        {
            timestamps.Enqueue(Stopwatch.GetTimestamp()); SleepIgnoringInterrupt(51); latch.Signal();
        }, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), cancel.Token);
        await Task.Run(() => latch.Wait(TestContext.Current.CancellationToken));
        cancel.Cancel(); Assert.True(periodic.IsCanceled);
        await Task.Delay(300);
        Assert.Equal(expectedTimeStamps, timestamps.Count);
        // Check if the task was run without a lag.
        var values = timestamps.ToArray();
        for (int i = 1; i < values.Length; i++)
            Assert.True(Stopwatch.GetElapsedTime(values[i - 1], values[i]) >= TimeSpan.FromMilliseconds(150));
    }

    [Fact(Timeout = 10000)]
    public async Task ShutdownWithPendingTasks()
    {
        const int numTasks = 3;
        int ranTasks = 0;
        using var latch = new ManualResetEventSlim();
        Action task = () =>
        {
            Interlocked.Increment(ref ranTasks);
            while (!latch.IsSet)
            {
                try { latch.Wait(TestContext.Current.CancellationToken); }
                catch (ThreadInterruptedException) { /* Ignored */ }
            }
        };
        try
        {
            for (int i = 0; i < numTasks; i++) _a.Execute(task);
            // At this point, the first task should be running and stuck at latch.await().
            while (Volatile.Read(ref ranTasks) == 0) { TestContext.Current.CancellationToken.ThrowIfCancellationRequested(); Thread.Yield(); }
            Assert.Equal(1, Volatile.Read(ref ranTasks));
            // Shut down the event loop to test if the other tasks are run before termination.
            _a.Shutdown();
            // Let the other tasks run.
            latch.Set();
            // Wait until the event loop is terminated.
            await _a.Termination.WaitAsync(TestContext.Current.CancellationToken);
            // Make sure loop.shutdown() above triggered wakeup().
            Assert.Equal(numTasks, Volatile.Read(ref ranTasks));
        }
        finally { latch.Set(); }
    }

    [Fact(Timeout = 10000)]
    public async Task TestRegistrationAfterShutdown()
    {
        _a.Shutdown();
        // Disable logging temporarily.
        // CLR registration exposes the rejection through Task; no process-wide Java Logback appenders are detached.
        var channel = new LocalChannel();
        var registration = _a.RegisterAsync(channel);
        await Assert.ThrowsAsync<RejectedExecutionException>(() => registration.WaitAsync(TestContext.Current.CancellationToken));
        Assert.True(registration.IsFaulted); Assert.False(channel.IsOpen);
    }

    [Fact(Timeout = 5000)]
    public async Task TestGracefulShutdownQuietPeriod()
    {
        var shutdown = _a.ShutdownGracefullyAsync(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(int.MaxValue));
        // Keep Scheduling tasks for another 2 seconds.
        for (int i = 0; i < 20; i++) { Thread.Sleep(100); _a.Execute(Noop); }
        long start = Stopwatch.GetTimestamp();
        Assert.True(_a.IsShuttingDown()); Assert.False(_a.IsShutdown());
        await shutdown.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(Stopwatch.GetElapsedTime(start) >= TimeSpan.FromSeconds(1));
    }
    [Fact(Timeout = 5000)]
    public void TestGracefulShutdownTimeout()
    {
        _ = _a.ShutdownGracefullyAsync(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        // Keep Scheduling tasks for another 3 seconds.
        // Submitted tasks must be rejected after 2 second timeout.
        for (int i = 0; i < 10; i++) { Thread.Sleep(100); _a.Execute(Noop); }
        Assert.Throws<RejectedExecutionException>(() =>
        {
            for (int i = 0; i < 20; i++) { TestContext.Current.CancellationToken.ThrowIfCancellationRequested(); Thread.Sleep(100); _a.Execute(Noop); }
        });
        Assert.True(_a.IsShuttingDown()); Assert.True(_a.IsShutdown());
    }

    [Fact(Timeout = 10000)]
    public async Task TestOnEventLoopIteration()
    {
        var onIteration = new CountingRunnable();
        _c.ExecuteAfterEventLoopIteration(onIteration.Work);
        var noop = new CountingRunnable();
        await _c.SubmitAsync(noop.Work);
        await _c.IterationEnd.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, noop.Count); Assert.Equal(1, onIteration.Count);
    }
    [Fact(Timeout = 10000)]
    public async Task TestRemoveOnEventLoopIteration()
    {
        var onIteration1 = new CountingRunnable(); _c.ExecuteAfterEventLoopIteration(onIteration1.Work);
        var onIteration2 = new CountingRunnable(); _c.ExecuteAfterEventLoopIteration(onIteration2.Work);
        _c.RemoveAfterEventLoopIterationTask(onIteration1.Work);
        var noop = new CountingRunnable();
        await _c.SubmitAsync(noop.Work);
        await _c.IterationEnd.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, noop.Count); Assert.Equal(1, onIteration2.Count); Assert.Equal(0, onIteration1.Count);
    }
    private sealed class CountingRunnable
    {
        private int _count;
        internal readonly Action Work;
        internal CountingRunnable() => Work = () => Interlocked.Increment(ref _count);
        internal int Count => Volatile.Read(ref _count);
    }
    internal sealed class LoopA : SingleThreadEventLoop
    {
        internal int CleanedUp;
        internal LoopA() : base(null, new DefaultThreadFactory(typeof(LoopA)), true) { }
        protected override void Run()
        {
            for (;;)
            {
                var task = TakeTask();
                if (task != null) { task(); UpdateLastExecutionTime(); }
                if (ConfirmShutdown()) break;
            }
        }
        protected override void Cleanup() => Interlocked.Increment(ref CleanedUp);
    }
    internal class LoopB : SingleThreadEventLoop
    {
        private readonly AutoResetEvent _wakeup = new(false);
        private readonly object _wakeupGate = new();
        private bool _stopped;
        internal LoopB() : base(null, new DefaultThreadFactory(typeof(LoopB)), false) { }
        protected override void Run()
        {
            for (;;)
            {
                // Source delayNanos consumes absolute System.nanoTime, while the native ticker reports elapsed time.
                // Waken up by interruptThread()
                // Java interrupt only signals the source sleep here. A CLR signal avoids interrupting Monitor/Trace locks in callbacks.
                _wakeup.WaitOne(TimeSpan.FromMilliseconds(DelayNanos(unchecked(GetCurrentTimeNanos() + Ticker().InitialNanoTime())) / 1000000));
                RunTasks0();
                if (ConfirmShutdown()) break;
            }
        }
        protected virtual void RunTasks0() => RunAllTasks();
        public override void Wakeup(bool inEventLoop)
        {
            lock (_wakeupGate)
            {
                if (!_stopped) _wakeup.Set();
            }
        }
        protected override void Cleanup()
        {
            lock (_wakeupGate)
            {
                _stopped = true;
                _wakeup.Dispose();
            }
        }
    }
    private sealed class LoopC : LoopB
    {
        internal readonly System.Threading.Channels.Channel<bool> IterationEnd = System.Threading.Channels.Channel.CreateBounded<bool>(1);
        protected override void AfterRunningAllTasks() { base.AfterRunningAllTasks(); IterationEnd.Writer.TryWrite(true); }
        protected override void RunTasks0() => RunAllTasks(60000000000L);
    }
}
