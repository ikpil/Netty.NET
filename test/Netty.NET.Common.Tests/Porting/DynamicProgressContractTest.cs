using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Tests.Porting;

public class DynamicProgressContractTest
{
    private sealed class QueuedExecutor : AbstractEventExecutor
    {
        private readonly ConcurrentQueue<IRunnable> _queue = new();
        private Thread _running;
        internal Exception Rejection;
        internal void RunAll()
        {
            _running = Thread.CurrentThread;
            try { while (_queue.TryDequeue(out var work)) work.Run(); }
            finally { _running = null; }
        }
        public override bool InEventLoop(Thread thread) => thread != null && thread == _running;
        public override void Execute(Action task)
        {
            IRunnable queuedTask = ExecutorWork.Unwrap(task, nameof(task));
            if (Rejection != null)
                throw Rejection;
            _queue.Enqueue(queuedTask);
        }
        public override Task Termination => Task.CompletedTask;
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => Task.CompletedTask;
        public override void Shutdown() { }
        public override bool IsShuttingDown() => false;
        public override bool IsShutdown() => false;
        public override bool IsTerminated() => false;
        public override bool AwaitTermination(TimeSpan timeout) => false;
    }

    private static async Task Drain(QueuedExecutor executor, Task completion)
    {
        var elapsed = Stopwatch.StartNew();
        while (!completion.IsCompleted && elapsed.Elapsed < TimeSpan.FromSeconds(5))
        {
            executor.RunAll();
            if (!completion.IsCompleted) await Task.Delay(1);
        }
        await completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RegistrationAdmissionDoesNotReplayPreviouslyQueuedProgress()
    {
        var executor = new QueuedExecutor();
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reporter = new ExecutorProgress(executor, source.Task);
        var events = new List<string>();
        using var first = reporter.Register(value => events.Add("first:" + value.Completed), _ => events.Add("first:done"));
        reporter.Report(new TransferProgress(1, 3));
        using var second = reporter.Register(value => events.Add("second:" + value.Completed), _ => events.Add("second:done"));
        reporter.Report(new TransferProgress(2, 3));
        source.SetResult();
        await Drain(executor, reporter.NotificationsCompleted);
        await Task.WhenAll(first.NotificationsCompleted, second.NotificationsCompleted);
        Assert.Equal(new[] { "first:1", "first:2", "second:2", "first:done", "second:done" }, events);
    }

    [Fact]
    public async Task DuplicateDelegatesHaveIndependentRemovalHandles()
    {
        var executor = new QueuedExecutor();
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reporter = new ExecutorProgress(executor, source.Task);
        int progress = 0, terminal = 0;
        Action<TransferProgress> callback = _ => ++progress;
        Action<Task> completed = _ => ++terminal;
        var first = reporter.Register(callback, completed);
        using var second = reporter.Register(callback, completed);
        reporter.Report(new TransferProgress(1));
        first.Dispose(); first.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await first.NotificationsCompleted);
        executor.RunAll();
        Assert.Equal(1, progress);
        Assert.False(source.Task.IsCompleted);
        source.SetResult();
        await Drain(executor, reporter.NotificationsCompleted);
        await second.NotificationsCompleted;
        Assert.Equal(1, terminal);
    }

    [Fact]
    public async Task RegistrationFromAProgressCallbackStartsWithTheNextReport()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reporter = new ExecutorProgress(ImmediateEventExecutor.INSTANCE, source.Task);
        var events = new List<string>();
        ProgressRegistration added = null;
        using var first = reporter.Register(value =>
        {
            events.Add("first:" + value.Completed);
            if (value.Completed == 1)
            {
                added = reporter.Register(next => events.Add("added:" + next.Completed));
                reporter.Report(new TransferProgress(2));
            }
        });
        using var second = reporter.Register(value => events.Add("second:" + value.Completed));
        reporter.Report(new TransferProgress(1));
        source.SetResult();
        await reporter.NotificationsCompleted.WaitAsync(TimeSpan.FromSeconds(5));
        await added.NotificationsCompleted;
        added.Dispose();
        Assert.Equal(new[] { "first:1", "second:1", "first:2", "second:2", "added:2" }, events);
    }

    [Fact]
    public async Task TerminalDisposalCannotLeaveAnotherClaimedSubscriptionPending()
    {
        var executor = new QueuedExecutor();
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reporter = new ExecutorProgress(executor, source.Task);
        ProgressRegistration second = null;
        int calls = 0;
        using var first = reporter.Register(null, _ => { second.Dispose(); reporter.Dispose(); });
        second = reporter.Register(null, _ => ++calls);
        source.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await Drain(executor, reporter.NotificationsCompleted));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await second.NotificationsCompleted.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(0, calls);
        Assert.True(source.Task.IsCompletedSuccessfully);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalAfterBatchClaimAllowsThatBatchButRemovesLaterNotifications(bool terminal)
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reporter = new ExecutorProgress(ImmediateEventExecutor.INSTANCE, source.Task);
        ProgressRegistration second = null;
        int calls = 0;
        using var first = reporter.Register(_ => { if (!terminal) second.Dispose(); }, _ => { if (terminal) second.Dispose(); });
        second = reporter.Register(_ => ++calls, _ => ++calls);
        if (terminal) source.SetResult();
        else
        {
            reporter.Report(new TransferProgress(1));
            reporter.Report(new TransferProgress(2));
            source.SetResult();
        }
        await reporter.NotificationsCompleted.WaitAsync(TimeSpan.FromSeconds(5));
        if (terminal) await second.NotificationsCompleted;
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await second.NotificationsCompleted);
        Assert.Equal(1, calls);
        second.Dispose();
    }

    [Fact]
    public async Task RejectionFaultsEverySubscriptionAndLeavesTheOperationPending()
    {
        var error = new RejectedExecutionException("closed executor");
        var executor = new QueuedExecutor { Rejection = error };
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reporter = new ExecutorProgress(executor, source.Task);
        using var first = reporter.Register(_ => { });
        using var second = reporter.Register(null, _ => { });
        reporter.Report(new TransferProgress(1));
        foreach (var task in new[] { reporter.NotificationsCompleted, first.NotificationsCompleted, second.NotificationsCompleted })
            Assert.Same(error, await Assert.ThrowsAsync<RejectedExecutionException>(async () => await task));
        Assert.False(source.Task.IsCompleted);
        source.SetResult();
    }

    [Fact]
    public async Task CompletionClosesProgressRegistrationAndLateCompletionUsesTheSourceTask()
    {
        var executor = new QueuedExecutor();
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reporter = new ExecutorProgress(executor, source.Task);
        Assert.Throws<ArgumentNullException>(() => reporter.Register(null));
        using var early = reporter.Register(_ => { });
        source.SetResult();
        Assert.Throws<InvalidOperationException>(() => reporter.Register(_ => { }));
        await Drain(executor, reporter.NotificationsCompleted);
        await early.NotificationsCompleted;
        using var completion = new ExecutorCompletion(executor, source.Task);
        Task observed = null;
        using var late = completion.Register(task => observed = task);
        await Drain(executor, late.NotificationCompleted);
        Assert.Same(source.Task, observed);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RemovedCallback(ExecutorProgress reporter)
    {
        var target = new object();
        var registration = reporter.Register(_ => GC.KeepAlive(target));
        reporter.Report(new TransferProgress(1));
        registration.Dispose();
        return new WeakReference(target);
    }

    [Fact]
    public async Task DetachingReleasesCallbackTargetsEvenWithTheirReportStillQueued()
    {
        var executor = new QueuedExecutor();
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reporter = new ExecutorProgress(executor, source.Task);
        WeakReference target = RemovedCallback(reporter);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(target.IsAlive);
        source.SetResult();
        await Drain(executor, reporter.NotificationsCompleted);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ShutdownRemovalSettlesEverySubscriptionWithoutChangingTheSource(bool orderedChild, bool terminal)
    {
        var pool = new UnorderedThreadPoolEventExecutor(1);
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var active = pool.SubmitAsync(() =>
        {
            started.Set();
            try { release.Wait(); } catch (ThreadInterruptedException) { }
        });
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            IEventExecutor executor = orderedChild ? new NonStickyEventExecutorGroup(pool, 1).Next() : pool;
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var reporter = new ExecutorProgress(executor, source.Task);
            int callbacks = 0;
            using var first = reporter.Register(_ => ++callbacks, _ => ++callbacks);
            using var second = reporter.Register(null, _ => ++callbacks);
            if (terminal)
            {
                source.SetResult();
                Assert.True(SpinWait.SpinUntil(() => pool.PendingTaskCount == 1, TimeSpan.FromSeconds(5)));
            }
            else reporter.Report(new TransferProgress(1));
            Assert.Equal(1, pool.PendingTaskCount);
            _ = pool.StopAsync();
            foreach (var task in new[] { reporter.NotificationsCompleted, first.NotificationsCompleted, second.NotificationsCompleted })
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, callbacks);
            Assert.Equal(terminal, source.Task.IsCompleted);
            if (!terminal) source.SetResult();
        }
        finally
        {
            release.Set();
            _ = pool.StopAsync();
            await active.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(await Task.Run(() => pool.AwaitTermination(TimeSpan.FromSeconds(5))));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task KeepSubscription(QueuedExecutor executor, Task operation, Action<Task> completed)
        => new ExecutorProgress(executor, operation).Register(null, completed).NotificationsCompleted;

    [Fact]
    public async Task KeepingOnlyTheSubscriptionTaskKeepsItsPendingObservationAlive()
    {
        var executor = new QueuedExecutor();
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Task notification = KeepSubscription(executor, source.Task, _ => ++calls);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        source.SetResult();
        await Drain(executor, notification);
        Assert.Equal(1, calls);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Task Notification, WeakReference Payload, WeakReference Callback) CompletedSubscription(QueuedExecutor executor)
    {
        var payload = new object();
        var target = new object();
        var source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reporter = new ExecutorProgress(executor, source.Task);
        var registration = reporter.Register(_ => GC.KeepAlive(target), _ => GC.KeepAlive(target));
        source.SetResult(payload);
        return (registration.NotificationsCompleted, new WeakReference(payload), new WeakReference(target));
    }

    [Fact]
    public async Task RetainedSubscriptionTaskReleasesCompletedResultAndCallbackTargets()
    {
        var executor = new QueuedExecutor();
        var completed = CompletedSubscription(executor);
        await Drain(executor, completed.Notification);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(completed.Payload.IsAlive);
        Assert.False(completed.Callback.IsAlive);
        GC.KeepAlive(completed.Notification);
    }

    [Fact]
    public async Task DynamicObserversUseExecutorContextAndIsolateMutationsAndFailures()
    {
        var executor = new QueuedExecutor();
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ambient = new AsyncLocal<string> { Value = "constructor" };
        using var reporter = new ExecutorProgress(executor, source.Task);
        ambient.Value = "registration";
        var observed = new List<string>();
        using var first = reporter.Register(_ =>
        {
            observed.Add(ambient.Value);
            ambient.Value = "changed";
            throw new InvalidOperationException("observer failure");
        }, _ => { observed.Add(ambient.Value); ambient.Value = "changed terminal"; });
        using var second = reporter.Register(_ => observed.Add(ambient.Value), _ => observed.Add(ambient.Value));
        ambient.Value = "producer";
        reporter.Report(new TransferProgress(1));
        source.SetResult();
        ambient.Value = "executor";
        await Drain(executor, reporter.NotificationsCompleted);
        await Task.WhenAll(first.NotificationsCompleted, second.NotificationsCompleted);
        Assert.Equal(new[] { "executor", "executor", "executor", "executor" }, observed);
        Assert.Equal("executor", ambient.Value);
        Assert.True(source.Task.IsCompletedSuccessfully);
    }
}
