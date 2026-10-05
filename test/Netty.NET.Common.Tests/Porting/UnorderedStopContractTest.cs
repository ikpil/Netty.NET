using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Porting;

public class UnorderedStopContractTest
{
    private static readonly AsyncLocal<string> Ambient = new();
    private sealed class Factory(Func<Action, Thread> create) : IThreadFactory
    {
        public Thread NewThread(Action task) => create(task);
    }

    // Exercise the native cancellation boundary with synchronous membership
    // removal and lifecycle reentry; this reservation never runs caller work.
    private sealed class ReentrantCancellation(Action observe) : ICancelableNativeSubmission
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Action _remove;
        internal int Cancellations;
        internal Task Result => _completion.Task;
        public bool IsCanceled => Result.IsCanceled;
        public void SetCancellationRemoval(Action remove) => _remove = remove;
        public void CancelForShutdown()
        {
            if (!_completion.TrySetCanceled()) return;
            ++Cancellations;
            Interlocked.Exchange(ref _remove, null)?.Invoke();
            observe();
        }
        public void Reject(Exception error) => _completion.TrySetException(error);
        public void Run() => Assert.Fail("Removed reservation ran");
    }

    [Fact]
    public async Task SnapshotWithdrawalPrecedesCancellationReentryAndTerminationPublication()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1, new Factory(_ => null));
        Task[] results = null;
        int pendingInsideCancellation = -1;
        bool completedInsideCancellation = true, terminatedInsideCancellation = true, settledInsideCancellation = true;
        Task reentrantStop = null;
        var reservation = new ReentrantCancellation(() =>
        {
            pendingInsideCancellation = executor.PendingTaskCount;
            completedInsideCancellation = executor.Termination.IsCompleted;
            terminatedInsideCancellation = executor.AwaitTermination(TimeSpan.Zero);
            settledInsideCancellation = results.All(task => task.IsCompleted);
            reentrantStop = executor.StopAsync();
        });
        executor.ExecuteNativeSubmission(reservation);
        executor.Execute(() => Assert.Fail("Removed raw work ran"));
        results = [executor.SubmitAsync(() => Assert.Fail("Removed submission ran"), TestContext.Current.CancellationToken),
            executor.ScheduleAsync(() => Assert.Fail("Removed deadline ran"), TimeSpan.FromDays(1), TestContext.Current.CancellationToken),
            executor.ScheduleAtFixedRateAsync(() => Assert.Fail("Removed periodic work ran"), TimeSpan.FromDays(1), TimeSpan.FromDays(1), TestContext.Current.CancellationToken)];
        Task stopping = executor.StopAsync();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(0, pendingInsideCancellation);
        Assert.False(completedInsideCancellation);
        Assert.False(terminatedInsideCancellation);
        Assert.False(settledInsideCancellation);
        Assert.Same(stopping, reentrantStop);
        Assert.Same(executor.Termination, stopping);
        Assert.Equal(1, reservation.Cancellations);
        Assert.True(reservation.Result.IsCanceled);
        Assert.All(results, task => Assert.True(task.IsCanceled));
        Assert.True(executor.IsTerminated());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedSnapshotSettlesResultsBeforeStopCallbacksFinishAndWorkersDrain(bool cancelOwner)
    {
        const int count = 128;
        var executor = new UnorderedThreadPoolEventExecutor(1);
        IEventExecutor child = new NonStickyEventExecutorGroup(executor).Next();
        using var owner = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var callbackEntered = new ManualResetEventSlim();
        using var callbackRelease = new ManualResetEventSlim();
        int rawCalls = 0;
        var results = new Task[count * 4];
        using var registration = executor.StopToken.UnsafeRegister(_ =>
        {
            Assert.Same(executor.Termination, executor.StopAsync());
            Assert.Equal(0, executor.PendingTaskCount);
            Assert.All(results, task => Assert.True(task.IsCanceled));
            callbackEntered.Set();
            Assert.True(callbackRelease.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }, null);
        Task<int> running = executor.SubmitAsync(() => { entered.Set(); release.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); return 7; }, TestContext.Current.CancellationToken);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            for (int i = 0; i < count; ++i)
            {
                executor.Execute(() => Interlocked.Increment(ref rawCalls));
                results[i * 4] = executor.SubmitAsync(() => Assert.Fail("Removed submission ran"), owner.Token);
                results[i * 4 + 1] = executor.ScheduleAsync(() => Assert.Fail("Removed deadline ran"), TimeSpan.FromDays(1), owner.Token);
                results[i * 4 + 2] = executor.ScheduleWithFixedDelayAsync(() => Assert.Fail("Removed periodic work ran"), TimeSpan.FromDays(1), TimeSpan.FromDays(1), owner.Token);
                results[i * 4 + 3] = child.SubmitAsync(() => Assert.Fail("Removed child submission ran"), owner.Token);
            }
            // The ordered child shares one runner reservation for its pending results.
            Assert.Equal(count * 4 + 1, executor.PendingTaskCount);
            Task canceling = cancelOwner ? Task.Run(owner.Cancel, TestContext.Current.CancellationToken) : Task.CompletedTask;
            Task stopping = executor.StopAsync();
            await canceling.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.False(stopping.IsCompleted);
            Assert.All(results, task => Assert.True(task.IsCanceled));
            callbackRelease.Set();
            Assert.False(stopping.IsCompleted);
            release.Set();
            Assert.Equal(7, await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            await stopping.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(cancelOwner, owner.IsCancellationRequested);
            Assert.Equal(0, rawCalls);
            Assert.Equal(0, executor.PendingTaskCount);
            Assert.Equal(0, executor.WorkerCount);
            Assert.Same(stopping, executor.StopAsync());
        }
        finally
        {
            callbackRelease.Set();
            release.Set();
            await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImmediateStopRequestsTheOptedInTokenAndCancelsWaitingWork(bool repeatRequest)
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int notifications = 0;
        using var registration = executor.StopToken.UnsafeRegister(_ => Interlocked.Increment(ref notifications), null);
        Task running = executor.SubmitAsync(token => { entered.Set(); release.Wait(TimeSpan.FromSeconds(5), token); }, executor.StopToken);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Task queued = executor.SubmitAsync(() => Assert.Fail("Removed work ran"));
            Task scheduled = executor.ScheduleAsync(() => Assert.Fail("Removed deadline ran"), TimeSpan.FromDays(1));
            Assert.Equal(2, executor.PendingTaskCount);
            if (repeatRequest) Assert.Same(executor.Termination, executor.StopAsync());
            Task stopping = executor.StopAsync();
            Assert.Same(executor.Termination, stopping);
            Assert.Same(stopping, executor.StopAsync());
            Assert.True(executor.StopToken.IsCancellationRequested);
            Assert.True(queued.IsCanceled);
            Assert.True(scheduled.IsCanceled);
            OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(executor.StopToken, error.CancellationToken);
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, notifications);
            Assert.True(executor.IsTerminated());
        }
        finally { release.Set(); await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task StopDoesNotInterruptOrOverwriteAnInvocationThatDefersCancellation()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var owner = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task<int> running = executor.SubmitAsync(() => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); return 7; }, owner.Token);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Task stopping = executor.StopAsync();
            Assert.False(owner.IsCancellationRequested);
            Assert.False(stopping.IsCompleted);
            Assert.False(running.IsCompleted);
            using var observer = new CancellationTokenSource();
            observer.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopping.WaitAsync(observer.Token));
            Assert.False(stopping.IsCompleted);
            release.Set();
            Assert.Equal(7, await running.WaitAsync(TimeSpan.FromSeconds(5)));
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { release.Set(); await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task TerminationWaitsForAsynchronousStopCallbacksAndAllowsReentry()
    {
        var executor = new UnorderedThreadPoolEventExecutor(0);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int caller = Environment.CurrentManagedThreadId, callback = 0;
        using var registration = executor.StopToken.UnsafeRegister(_ =>
        {
            callback = Environment.CurrentManagedThreadId;
            Assert.Same(executor.Termination, executor.StopAsync());
            Assert.Equal(0, executor.PendingTaskCount);
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
        }, null);
        try
        {
            Task stopping = executor.StopAsync();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.NotEqual(caller, callback);
            Assert.False(stopping.IsCompleted);
            Assert.False(executor.IsTerminated());
            Assert.False(executor.AwaitTermination(TimeSpan.FromMilliseconds(1)));
            release.Set();
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(executor.IsTerminated());
            Assert.Equal(executor.StopToken, executor.StopToken);
        }
        finally { release.Set(); await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task AllStopCallbackFailuresRemainObservable()
    {
        var executor = new UnorderedThreadPoolEventExecutor(0);
        var first = new InvalidOperationException("first callback");
        var second = new ArgumentException("second callback");
        using var one = executor.StopToken.UnsafeRegister(_ => throw first, null);
        using var two = executor.StopToken.UnsafeRegister(_ => throw second, null);
        Task stopping = executor.StopAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => stopping.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(stopping.IsFaulted);
        Assert.Contains(first, stopping.Exception.Flatten().InnerExceptions);
        Assert.Contains(second, stopping.Exception.Flatten().InnerExceptions);
        Assert.True(executor.IsTerminated());
        Assert.True(executor.AwaitTermination(TimeSpan.FromSeconds(5)));
        Assert.Same(stopping, executor.StopAsync());
    }

    [Fact]
    public async Task ConsumersCanLinkOperationOwnershipWithImmediateStop()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var owner = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(owner.Token, executor.StopToken);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task running = executor.SubmitAsync(token => { entered.Set(); release.Wait(TimeSpan.FromSeconds(5), token); }, linked.Token);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
            Assert.Equal(linked.Token, error.CancellationToken);
            Assert.False(owner.IsCancellationRequested);
        }
        finally { release.Set(); await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task GracefulDrainDoesNotRequestImmediateStop()
    {
        var executor = new UnorderedThreadPoolEventExecutor(0);
        CancellationToken token = executor.StopToken;
        int callbacks = 0;
        using var registration = token.UnsafeRegister(_ => ++callbacks, null);
        await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(token.IsCancellationRequested);
        Assert.Equal(token, executor.StopToken);
        Assert.Same(executor.Termination, executor.StopAsync());
        Assert.Equal(0, callbacks);
    }

    [Fact]
    public async Task ImmediateStopDoesNotLeakInterruptIntoCustomFactorySuffix()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Thread worker = null;
        bool suffixInterrupted = false;
        var executor = new UnorderedThreadPoolEventExecutor(1, new Factory(task => worker = new Thread(() =>
        {
            task.Invoke();
            try { Thread.Sleep(1); }
            catch (ThreadInterruptedException) { suffixInterrupted = true; }
        }) { IsBackground = true }));
        Task running = executor.SubmitAsync(() => { entered.Set(); while (!release.IsSet) Thread.SpinWait(64); });
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Task stopping = executor.StopAsync();
            release.Set();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
            Assert.False(suffixInterrupted);
        }
        finally { release.Set(); await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task FactoryReentrantStopDoesNotRunCancellationRemovalUnderItsGate()
    {
        UnorderedThreadPoolEventExecutor executor = null;
        executor = new UnorderedThreadPoolEventExecutor(1, new Factory(_ => { executor.StopAsync(); return null; }));
        int callbacks = 0;
        using var registration = executor.StopToken.UnsafeRegister(_ => { Assert.Equal(0, executor.PendingTaskCount); ++callbacks; }, null);
        Task queued = executor.SubmitAsync(() => Assert.Fail("Work admitted after reentrant stop"), executor.StopToken);
        await executor.Termination.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(queued.IsCanceled);
        Assert.Equal(1, callbacks);
        Assert.Equal(0, executor.WorkerCount);
    }

    [Fact]
    public async Task YieldedAsyncBodyRetainsItsOwnCompletionAfterWorkerDrain()
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task running = executor.SubmitAsync(async token => { entered.SetResult(); await release.Task; token.ThrowIfCancellationRequested(); }, executor.StopToken);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(running.IsCompleted);
            release.SetResult();
            OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(executor.StopToken, error.CancellationToken);
        }
        finally { release.TrySetResult(); await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunningSchedulesObserveTheExplicitStopToken(bool periodic)
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Action<CancellationToken> body = token => { entered.Set(); release.Wait(TimeSpan.FromSeconds(5), token); };
        Task work = periodic
            ? executor.ScheduleAtFixedRateAsync(body, TimeSpan.Zero, TimeSpan.FromDays(1), executor.StopToken)
            : executor.ScheduleAsync(body, TimeSpan.Zero, executor.StopToken);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
            Assert.Equal(executor.StopToken, error.CancellationToken);
            Assert.Equal(0, executor.ActiveWorkerCount);
        }
        finally { release.Set(); await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task UnsafeCallbacksDoNotCaptureTheStoppingCallersExecutionContext()
    {
        var executor = new UnorderedThreadPoolEventExecutor(0);
        string previous = Ambient.Value, seen = "not invoked";
        try
        {
            Ambient.Value = "registration";
            using var registration = executor.StopToken.UnsafeRegister(_ => seen = Ambient.Value, null);
            Ambient.Value = "stop caller";
            await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(seen);
            Assert.Equal("stop caller", Ambient.Value);
        }
        finally { Ambient.Value = previous; await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task ConcurrentStopRequestsPublishOneCancellationAndOneLifecycle()
    {
        var executor = new UnorderedThreadPoolEventExecutor(0);
        using var barrier = new Barrier(3);
        int callbacks = 0;
        using var registration = executor.StopToken.UnsafeRegister(_ => Interlocked.Increment(ref callbacks), null);
        Task first = null, second = null;
        Task Invoke(Action<Task> save) => Task.Run(() =>
        {
            Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(5)));
            save(executor.StopAsync());
        });
        Task one = Invoke(task => first = task), two = Invoke(task => second = task);
        Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(5)));
        await Task.WhenAll(one, two).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(first, second);
        Assert.Same(executor.Termination, first);
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, callbacks);
    }
}
