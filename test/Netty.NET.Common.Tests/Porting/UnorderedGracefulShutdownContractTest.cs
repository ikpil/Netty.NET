using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

public class UnorderedGracefulShutdownContractTest
{
    private sealed class WorkerlessFactory : IThreadFactory
    {
        public Thread NewThread(Action task) => null;
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, 0)]
    [InlineData(0, -1)]
    public void InvalidPeriodsDoNotStartShutdown(int quietMilliseconds, int timeoutMilliseconds)
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => executor.ShutdownGracefullyAsync(
                TimeSpan.FromMilliseconds(quietMilliseconds), TimeSpan.FromMilliseconds(timeoutMilliseconds)));
            Assert.False(executor.IsShuttingDown());
            Assert.False(executor.Termination.IsCompleted);
        }
        finally { _ = executor.StopAsync(); }
    }

    [Fact]
    public async Task ActiveWorkKeepsAdmissionOpenAndCompletionStartsANewQuietPeriod()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var lastEntered = new ManualResetEventSlim();
        using var lastRelease = new ManualResetEventSlim();
        Task running = executor.SubmitAsync(() => { entered.Set(); release.Wait(); });
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            TimeSpan quiet = TimeSpan.FromMilliseconds(250);
            Task termination = executor.ShutdownGracefullyAsync(quiet, TimeSpan.FromSeconds(10));
            Assert.True(executor.IsShuttingDown());
            Assert.False(executor.IsShutdown());
            await Task.Delay(quiet + TimeSpan.FromMilliseconds(100));
            Assert.False(executor.IsShutdown());
            int calls = 0;
            executor.Execute(Runnables.Create(() => Interlocked.Increment(ref calls)));
            Task submitted = executor.SubmitAsync(() => Interlocked.Increment(ref calls));
            Task scheduled = executor.ScheduleAsync(() =>
            {
                Interlocked.Increment(ref calls);
                lastEntered.Set();
                lastRelease.Wait();
            }, TimeSpan.Zero);
            release.Set();
            Assert.True(lastEntered.Wait(TimeSpan.FromSeconds(5)));
            var elapsed = Stopwatch.StartNew();
            Assert.False(termination.IsCompleted);
            lastRelease.Set();
            await termination.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(running, submitted, scheduled).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(150));
            Assert.Equal(3, calls);
            Assert.True(executor.IsTerminated());
        }
        finally
        {
            release.Set();
            lastRelease.Set();
            _ = executor.StopAsync();
            await executor.Termination.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task TimeoutClosesAdmissionWithoutInterruptingOrCompletingActiveWork()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task<int> running = executor.SubmitAsync(() => { entered.Set(); release.Wait(); return 17; });
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Task termination = executor.ShutdownGracefullyAsync(
                TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(300));
            Assert.False(executor.IsShutdown());
            // A second request cannot extend the first request's timeout.
            Assert.Same(termination, executor.ShutdownGracefullyAsync(TimeSpan.MaxValue, TimeSpan.MaxValue));
            Assert.True(SpinWait.SpinUntil(executor.IsShutdown, TimeSpan.FromSeconds(5)));
            Assert.False(running.IsCompleted);
            Assert.False(termination.IsCompleted);
            Assert.False(executor.AwaitTermination(TimeSpan.Zero));
            Assert.Throws<RejectedExecutionException>(() => executor.Execute(Runnables.Empty));
            await Assert.ThrowsAsync<RejectedExecutionException>(() => executor.SubmitAsync(() => 1));
            await Assert.ThrowsAsync<RejectedExecutionException>(() => executor.ScheduleAsync(() => 1, TimeSpan.Zero));
            release.Set();
            Assert.Equal(17, await running.WaitAsync(TimeSpan.FromSeconds(5)));
            await termination.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            release.Set();
            _ = executor.StopAsync();
            await executor.Termination.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task WorkerlessPoolClosesAfterQuietPeriodAndRetainsDeadlinesUntilOwnerCancellation()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1, new WorkerlessFactory());
        using var cancellation = new CancellationTokenSource();
        Task scheduled = executor.ScheduleAsync(() => Assert.Fail("Canceled deadline ran"), TimeSpan.FromDays(1), cancellation.Token);
        try
        {
            Task termination = executor.ShutdownGracefullyAsync(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5));
            Assert.False(executor.IsShutdown());
            Assert.False(scheduled.IsCompleted);
            Assert.True(SpinWait.SpinUntil(executor.IsShutdown, TimeSpan.FromSeconds(5)));
            Assert.False(termination.IsCompleted);
            Assert.False(scheduled.IsCompleted);
            cancellation.Cancel();
            await termination.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(scheduled.IsCanceled);
            Assert.True(executor.IsTerminated());
        }
        finally { _ = executor.StopAsync(); }
    }

    [Fact]
    public async Task WorkerlessAdmissionRestartsQuietPeriodButDoesNotFakeQueueDrain()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1, new WorkerlessFactory());
        using var cancellation = new CancellationTokenSource();
        try
        {
            Task termination = executor.ShutdownGracefullyAsync(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(5));
            Thread.Sleep(100);
            Task submitted = executor.SubmitAsync(() => 7, cancellation.Token);
            Task scheduled = executor.ScheduleAsync(() => 8, TimeSpan.FromDays(1), cancellation.Token);
            var elapsed = Stopwatch.StartNew();
            Assert.False(executor.IsShutdown());
            Assert.True(SpinWait.SpinUntil(executor.IsShutdown, TimeSpan.FromSeconds(5)));
            Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(400));
            Assert.False(termination.IsCompleted);
            Assert.False(submitted.IsCompleted);
            Assert.False(scheduled.IsCompleted);
            cancellation.Cancel();
            await termination.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(submitted.IsCanceled);
            Assert.True(scheduled.IsCanceled);
        }
        finally { _ = executor.StopAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitShutdownOverridesThePendingQuietPeriod(bool immediate)
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        Task termination = executor.ShutdownGracefullyAsync(TimeSpan.MaxValue, TimeSpan.MaxValue);
        try
        {
            Assert.Same(termination, executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero));
            Assert.True(executor.IsShuttingDown());
            Assert.False(executor.IsShutdown());
            if (immediate) _ = executor.StopAsync();
            else executor.Shutdown();
            await termination.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(executor.IsTerminated());
        }
        finally { _ = executor.StopAsync(); }
    }

    [Fact]
    public async Task ClosedAdmissionRejectsReplacementWorkWhileOwnedCancellationDrains()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1, new WorkerlessFactory());
        using var cancellation = new CancellationTokenSource();
        Task retained = executor.ScheduleAsync(() => 1, TimeSpan.FromDays(1), cancellation.Token);
        try
        {
            Task termination = executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            Assert.False(termination.IsCompleted);
            await Assert.ThrowsAsync<RejectedExecutionException>(() => executor.ScheduleAsync(() => 2, TimeSpan.Zero));
            Assert.Equal(1, executor.PendingTaskCount);
            cancellation.Cancel();
            await termination.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(retained.IsCanceled);
        }
        finally { _ = executor.StopAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LifecycleTimerDoesNotRetainAmbientExecutionContext(bool alreadySuppressed)
    {
        var executor = new UnorderedThreadPoolEventExecutor(1, new WorkerlessFactory());
        try
        {
            WeakReference value = RequestWithAmbientValue(executor, alreadySuppressed);
            for (int attempt = 0; attempt < 3 && value.IsAlive; ++attempt)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Assert.False(value.IsAlive);
            Assert.True(executor.IsShuttingDown());
            Assert.False(executor.IsShutdown());
            Assert.False(executor.Termination.IsCompleted);
        }
        finally { _ = executor.StopAsync(); }
    }

    [Fact]
    public async Task RepeatedStopCannotRestartATerminatedPeriodicPool()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1, new WorkerlessFactory());
        Task repeating = executor.ScheduleAtFixedRateAsync(() => { }, TimeSpan.Zero, TimeSpan.FromDays(1));
        Assert.Same(executor.Termination, executor.StopAsync());
        try
        {
            Task termination = executor.Termination;
            await termination.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(executor.Termination, executor.StopAsync());
            Assert.True(executor.IsTerminated());
            Assert.Equal(0, executor.PendingTaskCount);
            Assert.True(repeating.IsCanceled);
            Assert.Same(termination, executor.Termination);
        }
        finally { _ = executor.StopAsync(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RequestWithAmbientValue(UnorderedThreadPoolEventExecutor executor, bool suppress)
    {
        var ambient = new AsyncLocal<object> { Value = new object() };
        var value = new WeakReference(ambient.Value);
        try
        {
            using (suppress ? ExecutionContext.SuppressFlow() : default)
            {
                executor.ShutdownGracefullyAsync(TimeSpan.MaxValue, TimeSpan.MaxValue);
                Assert.Equal(suppress, ExecutionContext.IsFlowSuppressed());
            }
        }
        finally { ambient.Value = null; }
        return value;
    }
}
