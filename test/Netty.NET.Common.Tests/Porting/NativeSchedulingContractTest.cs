using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Porting;

public class NativeSchedulingContractTest
{
    private sealed class ManualExecutor : AbstractScheduledEventExecutor
    {
        private readonly MockTicker _clock = global::Netty.NET.Common.Concurrent.Ticker.NewMockTicker();
        private bool _shutdown;
        internal IScheduledWork Head => PeekScheduledTask();
        internal void Advance(long nanos) => _clock.Advance(TimeSpan.FromTicks(nanos / 100));
        internal Action Poll() => PollScheduledTask();
        public override Ticker Ticker() => _clock;
        public override bool InEventLoop(Thread thread) => true;
        public override void Execute(Action task)
        {
            task();
        }
        public override Task Termination => Task.CompletedTask;
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) { Shutdown(); return Termination; }
        public override void Shutdown() { _shutdown = true; CancelScheduledTasks(); }
        public override bool IsShuttingDown() => _shutdown;
        public override bool IsShutdown() => _shutdown;
        public override bool IsTerminated() => _shutdown;
        public override bool AwaitTermination(TimeSpan timeout) => _shutdown;
    }

    [Fact]
    public async Task NativeResultTypesShareDeadlineOrderWithoutSharingFutureResults()
    {
        var executor = new ManualExecutor();
        var order = new List<int>();
        Task<int> first = executor.ScheduleAsync(() => { order.Add(1); return 42; }, TimeSpan.FromTicks(1));
        Assert.False((object)executor.Head is System.Threading.Tasks.Task);
        Task<string> second = executor.ScheduleAsync(() => { order.Add(2); return "text"; }, TimeSpan.FromTicks(1));
        Task third = executor.ScheduleAsync(() => { order.Add(3); }, TimeSpan.FromTicks(1));
        Assert.Null(executor.Poll());
        executor.Advance(100);
        executor.Poll().Invoke(); executor.Poll().Invoke(); executor.Poll().Invoke();
        Assert.Equal(new[] { 1, 2, 3 }, order);
        Assert.Equal(42, await first);
        Assert.Equal("text", await second);
        await third;
        Assert.Null(executor.Poll());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FixedRateAndFixedDelayUseDifferentClockOrigins(bool fixedDelay)
    {
        var executor = new ManualExecutor();
        using var cancellation = new CancellationTokenSource();
        int calls = 0;
        Action callback = () => { ++calls; executor.Advance(100); };
        Task task = fixedDelay
            ? executor.ScheduleWithFixedDelayAsync(callback, TimeSpan.Zero, TimeSpan.FromTicks(2), cancellation.Token)
            : executor.ScheduleAtFixedRateAsync(callback, TimeSpan.Zero, TimeSpan.FromTicks(2), cancellation.Token);
        long sequence = executor.Head.GetId();
        executor.Poll().Invoke();
        Assert.Equal(fixedDelay ? 300 : 200, executor.Head.DeadlineNanos());
        Assert.Equal(sequence, executor.Head.GetId());
        Assert.False(task.IsCompleted);
        executor.Advance(fixedDelay ? 200 : 100);
        executor.Poll().Invoke();
        Assert.Equal(2, calls);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
        Assert.Null(executor.Head);
    }

    [Fact]
    public async Task CancellationAfterDueTransferStillPreventsInvocation()
    {
        var executor = new ManualExecutor();
        using var cancellation = new CancellationTokenSource();
        int calls = 0;
        Task task = executor.ScheduleAsync(() => { ++calls; }, TimeSpan.Zero, cancellation.Token);
        Action due = executor.Poll();
        cancellation.Cancel();
        due();
        Assert.Equal(0, calls);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task NonpositiveOneShotDelayIsImmediatelyEligible(long ticks)
    {
        var executor = new ManualExecutor();
        Task<int> task = executor.ScheduleAsync(() => 7, TimeSpan.FromTicks(ticks));
        Assert.False(task.IsCompleted);
        executor.Poll().Invoke();
        Assert.Equal(7, await task);
    }

    [Fact]
    public async Task LargeDeadlinesAndShutdownUseTheQueueRatherThanTimerLimits()
    {
        var executor = new ManualExecutor();
        Task task = executor.ScheduleAsync(() => { }, TimeSpan.MaxValue);
        Assert.Equal(long.MaxValue, executor.Head.DeadlineNanos());
        Assert.Null(executor.Poll());
        executor.Shutdown();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }

    [Fact]
    public void PeriodicArgumentErrorsAreSynchronousAndDoNotEnqueue()
    {
        var executor = new ManualExecutor();
        Assert.Throws<ArgumentOutOfRangeException>(() => executor.ScheduleAtFixedRateAsync(() => { }, TimeSpan.FromTicks(-1), TimeSpan.FromTicks(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => executor.ScheduleAtFixedRateAsync(() => { }, TimeSpan.Zero, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => executor.ScheduleWithFixedDelayAsync(() => { }, TimeSpan.Zero, TimeSpan.FromTicks(-1)));
        Assert.Null(executor.Head);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerContextIsIsolatedAcrossPeriodicInvocations(bool suppressFlow)
    {
        var executor = new ManualExecutor();
        var ambient = new AsyncLocal<string>();
        using var cancellation = new CancellationTokenSource();
        var values = new List<string>();
        ambient.Value = "caller";
        Action action = () => { values.Add(ambient.Value); ambient.Value = "changed"; };
        Task task;
        if (suppressFlow)
        {
            using (ExecutionContext.SuppressFlow())
                task = executor.ScheduleAtFixedRateAsync(action, TimeSpan.Zero, TimeSpan.FromTicks(1), cancellation.Token);
        }
        else task = executor.ScheduleAtFixedRateAsync(action, TimeSpan.Zero, TimeSpan.FromTicks(1), cancellation.Token);
        ambient.Value = "executor";
        executor.Poll().Invoke();
        Assert.Equal("executor", ambient.Value);
        executor.Advance(100);
        executor.Poll().Invoke();
        Assert.Equal(new[] { suppressFlow ? "executor" : "caller", suppressFlow ? "executor" : "caller" }, values);
        Assert.Equal("executor", ambient.Value);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }

    private static IEventExecutor Create(bool unordered) => unordered
        ? new UnorderedThreadPoolEventExecutor(1) : new DefaultEventExecutor();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrecanceledWorkAndRejectedWorkDoNotInvokeDelegates(bool unordered)
    {
        IEventExecutor executor = Create(unordered);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        int calls = 0;
        Task canceled = executor.ScheduleAsync(() => { ++calls; }, TimeSpan.Zero, cancellation.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceled);
        await Stop(executor);
        Task rejected = executor.ScheduleAsync(() => { ++calls; }, TimeSpan.Zero);
        await Assert.ThrowsAsync<RejectedExecutionException>(async () => await rejected);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObserverCancellationDoesNotCancelAQueuedTimeout(bool unordered)
    {
        IEventExecutor executor = Create(unordered);
        using var cancellation = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task blocker = executor.SubmitAsync(() => { started.Set(); release.Wait(); });
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            // Keep the producer queued until observer cancellation has been verified.
            // WaitAsync returns an already-completed result even with a canceled token;
            // a fixed wall-clock delay alone does not establish this test's precondition.
            Task<int> producer = executor.ScheduleAsync(() => 42, TimeSpan.FromMilliseconds(100));
            cancellation.Cancel();
            Assert.False(producer.IsCompleted);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await producer.WaitAsync(cancellation.Token));
            release.Set();
            Assert.Equal(42, await producer.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            release.Set();
            try { await blocker.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { await Stop(executor); }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TokenAwareDelegatesAndTaskDelegatesUseTheSameInvocationBoundary(bool unordered)
    {
        IEventExecutor executor = Create(unordered);
        using var cancellation = new CancellationTokenSource();
        try
        {
            CancellationToken observed = default;
            await executor.ScheduleAsync(token => { observed = token; }, TimeSpan.Zero, cancellation.Token);
            Assert.Equal(cancellation.Token, observed);
            Assert.Equal(17, await executor.ScheduleAsync(token => token == cancellation.Token ? 17 : 0, TimeSpan.Zero, cancellation.Token));
            await executor.ScheduleAsync((Func<Task>)(() => Task.CompletedTask), TimeSpan.Zero);
            await executor.ScheduleAsync((Func<CancellationToken, Task>)(token =>
            {
                Assert.Equal(cancellation.Token, token);
                return Task.CompletedTask;
            }), TimeSpan.Zero, cancellation.Token);
            Assert.Equal(23, await executor.ScheduleAsync((Func<CancellationToken, Task<int>>)(token => Task.FromResult(token == cancellation.Token ? 23 : 0)), TimeSpan.Zero, cancellation.Token));
        }
        finally { await Stop(executor); }
    }

    private sealed class FailingFactory(Exception error) : IThreadFactory
    {
        public Thread NewThread(Action runnable) => throw error;
    }

    [Fact]
    public async Task WorkerStartFailureFaultsAndRemovesTheNativeReservation()
    {
        var error = new InvalidOperationException("worker start");
        var executor = new UnorderedThreadPoolEventExecutor(1, new FailingFactory(error));
        Task task = executor.ScheduleAsync(() => { }, TimeSpan.FromDays(1));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(async () => await task));
        Assert.Equal(0, executor.PendingTaskCount);
        await Stop(executor);
    }

    [Fact]
    public async Task ShutdownPolicyDuringRunningNativePeriodicWorkPublishesCancellation()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Task task = executor.ScheduleWithFixedDelayAsync(() =>
            {
                started.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            }, TimeSpan.Zero, TimeSpan.FromDays(1));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            executor.Shutdown();
            release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(task.IsCanceled);
        }
        finally { release.Set(); await Stop(executor); }
    }

    private sealed class InspectableExecutor : SingleThreadEventExecutor
    {
        internal InspectableExecutor() : base(null, new DefaultThreadFactory(typeof(InspectableExecutor)), true) { }
        internal int ScheduledCount => _scheduledTaskQueue?.Count ?? 0;
        internal IScheduledWork ScheduledHead => PeekScheduledTask();
        protected override void Run()
        {
            while (!ConfirmShutdown())
            {
                Action task = TakeTask();
                if (task != null) { RunTask(task); UpdateLastExecutionTime(); }
            }
        }
    }

    [Fact]
    public async Task CancellationRacingWithSubmissionDoesNotLoseRegistrationsOrQueueRemoval()
    {
        var executor = new InspectableExecutor();
        int calls = 0;
        try
        {
            for (int i = 0; i < 256; ++i)
            {
                using var cancellation = new CancellationTokenSource();
                Task cancel = Task.Run(() => cancellation.Cancel());
                Task work = executor.ScheduleAsync(() => { Interlocked.Increment(ref calls); }, TimeSpan.FromDays(1), cancellation.Token);
                await cancel;
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await work);
            }
            Assert.Equal(0, await executor.SubmitAsync(() => executor.ScheduledCount));
            Assert.Equal(0, calls);
        }
        finally { await Stop(executor); }
    }

    private static async Task Stop(IEventExecutor executor)
    {
        await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5));
        if (executor is UnorderedThreadPoolEventExecutor)
            Assert.True(await Task.Run(() => executor.AwaitTermination(TimeSpan.FromSeconds(5))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActionFailureAndPeriodicFailureFaultTheNativeTask(bool unordered)
    {
        IEventExecutor executor = Create(unordered);
        try
        {
            var failure = new InvalidOperationException("original failure");
            Task once = executor.ScheduleAsync((Action)(() => throw failure), TimeSpan.Zero);
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await once.WaitAsync(TimeSpan.FromSeconds(5))));
            int calls = 0;
            Task periodic = executor.ScheduleAtFixedRateAsync(() => { Interlocked.Increment(ref calls); throw failure; }, TimeSpan.Zero, TimeSpan.FromTicks(1));
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await periodic.WaitAsync(TimeSpan.FromSeconds(5))));
            await executor.ScheduleAsync(() => { }, TimeSpan.Zero);
            Assert.Equal(1, calls);
        }
        finally { await Stop(executor); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CancellationAfterOneShotStartsIsCooperative(bool unordered, bool cooperate)
    {
        IEventExecutor executor = Create(unordered);
        using var cancellation = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Task<int> task = executor.ScheduleAsync(token =>
            {
                started.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                if (cooperate) token.ThrowIfCancellationRequested();
                return 42;
            }, TimeSpan.Zero, cancellation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            Assert.False(task.IsCompleted);
            release.Set();
            if (cooperate)
            {
                var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Equal(cancellation.Token, error.CancellationToken);
                Assert.True(task.IsCanceled);
            }
            else Assert.Equal(42, await task.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { release.Set(); await Stop(executor); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsyncDelegateCompletionIsUnwrappedAndDoesNotBlockTheExecutor(bool unordered)
    {
        IEventExecutor executor = Create(unordered);
        var inner = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Task<int> task = executor.ScheduleAsync(async () => { started.SetResult(); return await inner.Task.ConfigureAwait(false); }, TimeSpan.Zero);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await executor.ScheduleAsync(() => { }, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(task.IsCompleted);
            inner.SetResult(73);
            Assert.Equal(73, await task.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { inner.TrySetResult(0); await Stop(executor); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnrelatedCancellationExceptionIsFailureAndNullTaskIsFailure(bool unordered)
    {
        IEventExecutor executor = Create(unordered);
        try
        {
            var error = new OperationCanceledException();
            Task task = executor.ScheduleAsync((Action)(() => throw error), TimeSpan.Zero);
            Assert.Same(error, await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task));
            Assert.True(task.IsFaulted);
            Task nullTask = executor.ScheduleAsync((Func<Task>)(() => null), TimeSpan.Zero);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await nullTask);
        }
        finally { await Stop(executor); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PeriodicCancellationDuringCallbackStopsFutureInvocations(bool unordered)
    {
        IEventExecutor executor = Create(unordered);
        using var cancellation = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        try
        {
            Task task = executor.ScheduleAtFixedRateAsync(() =>
            {
                Interlocked.Increment(ref calls);
                started.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            }, TimeSpan.Zero, TimeSpan.FromTicks(1), cancellation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
            release.Set();
            await executor.ScheduleAsync(() => { }, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, calls);
        }
        finally { release.Set(); await Stop(executor); }
    }

    [Fact]
    public async Task PoolStopCancelsNativeWorkRemovedFromItsQueue()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        Task task = executor.ScheduleAsync(() => { }, TimeSpan.FromDays(1));
        _ = executor.StopAsync();
        Assert.True(task.IsCanceled);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
        await Stop(executor);
    }

    [Fact]
    public async Task NonStickyGroupForwardsSchedulingAndItsOrderedChildRemainsUnsupported()
    {
        var pool = new UnorderedThreadPoolEventExecutor(1);
        var group = new NonStickyEventExecutorGroup(pool);
        try
        {
            Assert.Equal(29, await group.ScheduleAsync(() => 29, TimeSpan.Zero));
            await Assert.ThrowsAsync<NotSupportedException>(async () => await group.Next().ScheduleAsync(() => { }, TimeSpan.Zero));
        }
        finally { await Stop(pool); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CancelCapturedPayload(ManualExecutor executor)
    {
        var payload = new object();
        var weak = new WeakReference(payload);
        using var cancellation = new CancellationTokenSource();
        _ = executor.ScheduleAsync(() => GC.KeepAlive(payload), TimeSpan.FromDays(1), cancellation.Token);
        cancellation.Cancel();
        return weak;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Work, Task Result) ScheduleRetainedWork(InspectableExecutor executor)
    {
        Task result = executor.ScheduleAsync(() => Assert.Fail("Stopped schedule ran"), TimeSpan.FromDays(1));
        return (new WeakReference(executor.ScheduledHead), result);
    }

    [Fact]
    public async Task ShutdownReleasesScheduledWorkWhileTheExecutorRemainsReferenced()
    {
        var executor = new InspectableExecutor();
        try
        {
            var scheduled = await executor.SubmitAsync(() => ScheduleRetainedWork(executor)).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(scheduled.Work.IsAlive);
            await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(scheduled.Result.IsCanceled);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Assert.False(scheduled.Work.IsAlive);
            GC.KeepAlive(executor);
        }
        finally { await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public void CancellationReleasesCapturedDelegateBeforeAFarFutureDeadline()
    {
        var executor = new ManualExecutor();
        WeakReference weak = CancelCapturedPayload(executor);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(weak.IsAlive);
        Assert.Null(executor.Head);
        GC.KeepAlive(executor);
    }
}
