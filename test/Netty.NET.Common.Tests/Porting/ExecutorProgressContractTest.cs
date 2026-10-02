using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Tests.Porting;

public class ExecutorProgressContractTest
{
    private sealed class QueuedExecutor : AbstractEventExecutor
    {
        private readonly ConcurrentQueue<IRunnable> _queue = new();
        private Thread _running;
        internal Exception rejection;
        internal void RunAll()
        {
            _running = Thread.CurrentThread;
            try { while (_queue.TryDequeue(out var work)) work.run(); }
            finally { _running = null; }
        }
        public override bool inEventLoop(Thread thread) => thread == _running;
        public override void execute(IRunnable work) { if (rejection != null) throw rejection; _queue.Enqueue(work); }
        public override Task Termination => Task.CompletedTask;
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => Task.CompletedTask;
        public override void shutdown() { }
        public override bool isShuttingDown() => false;
        public override bool isShutdown() => false;
        public override bool isTerminated() => false;
        public override bool awaitTermination(TimeSpan timeout) => false;
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
    public async Task NativeTaskAndIProgressModelChunkedTransferWithFinalProgressBeforeCompletion()
    {
        var executor = new QueuedExecutor();
        var operation = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<string>();
        var reporter = new ExecutorProgress(executor, operation.Task,
            value => { Assert.True(executor.inEventLoop()); events.Add($"p:{value.Completed}/{value.Total}"); },
            task => { Assert.Same(operation.Task, task); events.Add("done"); });
        IProgress<TransferProgress> progress = reporter;
        progress.Report(new TransferProgress(3, 10));
        progress.Report(new TransferProgress(7, 10));
        progress.Report(new TransferProgress(10, 10));
        operation.SetResult(10);
        Assert.Equal(10, await operation.Task);
        await Drain(executor, reporter.NotificationsCompleted);
        Assert.Equal(new[] { "p:3/10", "p:7/10", "p:10/10", "done" }, events);
        Assert.False(reporter.TryReport(10, 10));
        Assert.Throws<InvalidOperationException>(() => reporter.Report(new TransferProgress(10, 10)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    [InlineData(7, -100)]
    [InlineData(long.MaxValue, long.MaxValue)]
    public async Task CountsRetainLongPrecisionAndNormalizeUnknownTotals(long completed, long total)
    {
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TransferProgress observed = default;
        var reporter = new ExecutorProgress(ImmediateEventExecutor.INSTANCE, operation.Task, value => observed = value);
        Assert.True(reporter.TryReport(completed, total));
        Assert.Equal(completed, observed.Completed);
        Assert.Equal(total < 0 ? (long?)null : total, observed.Total);
        operation.SetResult();
        await reporter.NotificationsCompleted.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(-1, -1)]
    [InlineData(-1, 10)]
    [InlineData(11, 10)]
    public async Task InvalidCountsAreRejectedWithoutNotifyingObservers(long completed, long total)
    {
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var reporter = new ExecutorProgress(ImmediateEventExecutor.INSTANCE, operation.Task, _ => ++calls);
        Assert.False(reporter.TryReport(completed, total));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransferProgress(completed, total));
        Assert.Equal(0, calls);
        operation.SetResult();
        await reporter.NotificationsCompleted.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CompletedFailedAndCanceledOperationsCloseAdmissionButRetainTheirOwnResult(int outcome)
    {
        var executor = new QueuedExecutor();
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = new InvalidOperationException("transfer failed");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        if (outcome == 0) operation.SetResult();
        else if (outcome == 1) operation.SetException(error);
        else operation.SetCanceled(cancellation.Token);
        int completions = 0;
        var reporter = new ExecutorProgress(executor, operation.Task, _ => throw new InvalidOperationException("unexpected progress"),
            task => { Assert.Same(operation.Task, task); ++completions; });
        Assert.False(reporter.TryReport(1, 2));
        await Drain(executor, reporter.NotificationsCompleted);
        Assert.Equal(1, completions);
        if (outcome == 1) Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(async () => await operation.Task));
        else if (outcome == 2)
        {
            var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await operation.Task);
            Assert.Equal(cancellation.Token, canceled.CancellationToken);
        }
        else await operation.Task;
    }

    [Fact]
    public async Task MulticastObserverFailureDoesNotSkipOtherObserversOrFaultTheOperation()
    {
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<string>();
        Action<TransferProgress> progress = _ => { events.Add("bad-progress"); throw new InvalidOperationException(); };
        progress += _ => events.Add("good-progress");
        Action<Task> completed = _ => { events.Add("bad-complete"); throw new InvalidOperationException(); };
        completed += _ => events.Add("good-complete");
        var reporter = new ExecutorProgress(ImmediateEventExecutor.INSTANCE, operation.Task, progress, completed);
        reporter.Report(new TransferProgress(1, 2));
        operation.SetResult();
        await reporter.NotificationsCompleted.WaitAsync(TimeSpan.FromSeconds(5));
        await operation.Task;
        Assert.Equal(new[] { "bad-progress", "good-progress", "bad-complete", "good-complete" }, events);
    }

    [Fact]
    public async Task RecursiveReportsHaveBoundedStackAndFinishTheCurrentObserversFirst()
    {
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ExecutorProgress reporter = null;
        var events = new List<long>();
        int depth = 0, maxDepth = 0;
        Action<TransferProgress> progress = value =>
        {
            ++depth; maxDepth = Math.Max(maxDepth, depth);
            events.Add(value.Completed * 2);
            if (value.Completed < 10_000) reporter.Report(new TransferProgress(value.Completed + 1));
            --depth;
        };
        progress += value => events.Add(value.Completed * 2 + 1);
        reporter = new ExecutorProgress(ImmediateEventExecutor.INSTANCE, operation.Task, progress);
        reporter.Report(new TransferProgress(0));
        Assert.Equal(1, maxDepth);
        Assert.Equal(Enumerable.Range(0, 20_002).Select(value => (long)value), events);
        operation.SetResult();
        await reporter.NotificationsCompleted.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ObserverContextUsesExecutorValuesAndDoesNotLeakMutations()
    {
        var executor = new QueuedExecutor();
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ambient = new AsyncLocal<string>();
        var observed = new List<string>();
        ambient.Value = "constructor";
        Action<TransferProgress> progress = _ => { observed.Add(ambient.Value); ambient.Value = "changed"; };
        progress += _ => observed.Add(ambient.Value);
        var reporter = new ExecutorProgress(executor, operation.Task, progress,
            _ => { observed.Add(ambient.Value); ambient.Value = "complete-change"; });
        ambient.Value = "producer";
        reporter.Report(new TransferProgress(1));
        ambient.Value = "executor";
        executor.RunAll();
        Assert.Equal("executor", ambient.Value);
        operation.SetResult();
        await Drain(executor, reporter.NotificationsCompleted);
        Assert.Equal(new[] { "executor", "executor", "executor" }, observed);
        Assert.Equal("executor", ambient.Value);
    }

    [Fact]
    public async Task DisposalStopsPendingObservationWithoutCancelingItsProducer()
    {
        var executor = new QueuedExecutor();
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var reporter = new ExecutorProgress(executor, operation.Task, _ => ++calls, _ => ++calls);
        reporter.Report(new TransferProgress(1));
        reporter.Dispose(); reporter.Dispose();
        executor.RunAll();
        Assert.Equal(0, calls);
        Assert.False(operation.Task.IsCompleted);
        Assert.False(reporter.TryReport(2, 3));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reporter.NotificationsCompleted);
        operation.SetResult();
        await operation.Task;
    }

    [Fact]
    public async Task ExecutorRejectionFaultsNotificationCompletionWithoutCompletingTheProducer()
    {
        var failure = new RejectedExecutionException("notification rejected");
        var executor = new QueuedExecutor { rejection = failure };
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reporter = new ExecutorProgress(executor, operation.Task, _ => { });
        Assert.True(reporter.TryReport(1, 2));
        Assert.Same(failure, await Assert.ThrowsAsync<RejectedExecutionException>(async () => await reporter.NotificationsCompleted));
        Assert.False(operation.Task.IsCompleted);
        Assert.False(reporter.TryReport(2, 2));
        operation.SetResult();
        await operation.Task;
    }

    [Fact]
    public async Task BoundedObservationRejectsExcessReportsButStillDeliversItsTerminalMarker()
    {
        var executor = new QueuedExecutor();
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new List<long>();
        var reporter = new ExecutorProgress(executor, operation.Task, value => observed.Add(value.Completed),
            _ => observed.Add(-1), maxPendingReports: 2);
        Assert.True(reporter.TryReport(0, 10));
        Assert.True(reporter.TryReport(1, 10));
        Assert.False(reporter.TryReport(2, 10));
        Assert.Throws<InvalidOperationException>(() => reporter.Report(new TransferProgress(3, 10)));
        executor.RunAll();
        Assert.True(reporter.TryReport(4, 10));
        operation.SetResult();
        await Drain(executor, reporter.NotificationsCompleted);
        Assert.Equal(new long[] { 0, 1, 4, -1 }, observed);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Task Notifications, WeakReference Result) CompletedResult(QueuedExecutor executor)
    {
        var payload = new object();
        var operation = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reporter = new ExecutorProgress(executor, operation.Task, _ => { });
        operation.SetResult(payload);
        return (reporter.NotificationsCompleted, new WeakReference(payload));
    }

    [Fact]
    public async Task RetainedNotificationTaskDoesNotRetainTheCompletedOperationResult()
    {
        var executor = new QueuedExecutor();
        var completed = CompletedResult(executor);
        await Drain(executor, completed.Notifications);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(completed.Result.IsAlive);
        GC.KeepAlive(completed.Notifications);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentProducersKeepPerProducerOrderAndSerializeObservers(bool unordered)
    {
        IEventExecutor executor = unordered ? new UnorderedThreadPoolEventExecutor(2) : new DefaultEventExecutor();
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int active = 0, overlap = 0, callbacks = 0;
        var last = new int[4];
        Array.Fill(last, -1);
        var reporter = new ExecutorProgress(executor, operation.Task, value =>
        {
            if (Interlocked.Increment(ref active) != 1) Interlocked.Increment(ref overlap);
            int producer = (int)(value.Completed / 1000);
            int index = (int)(value.Completed % 1000);
            Assert.Equal(last[producer] + 1, index);
            last[producer] = index;
            Interlocked.Increment(ref callbacks);
            Interlocked.Decrement(ref active);
        });
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 4).Select(producer => Task.Run(() =>
            {
                for (int index = 0; index < 200; ++index)
                    Assert.True(reporter.TryReport(producer * 1000 + index, -1));
            })));
            operation.SetResult();
            await reporter.NotificationsCompleted.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(800, callbacks);
            Assert.Equal(0, overlap);
        }
        finally
        {
            reporter.Dispose();
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            if (unordered) Assert.True(await Task.Run(() => executor.awaitTermination(TimeSpan.FromSeconds(5))));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AbandonReporter(IEventExecutor executor, Task operation)
        => new(new ExecutorProgress(executor, operation, _ => { }));

    [Fact]
    public void PendingOperationDoesNotRetainAnAbandonedReporter()
    {
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WeakReference weak = AbandonReporter(new QueuedExecutor(), operation.Task);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(weak.IsAlive);
        GC.KeepAlive(operation);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task KeepNotificationTask(QueuedExecutor executor, Task operation, Action<Task> completed)
        => new ExecutorProgress(executor, operation, _ => { }, completed).NotificationsCompleted;

    [Fact]
    public async Task KeepingOnlyTheNotificationTaskKeepsThePendingDispatcherAlive()
    {
        var executor = new QueuedExecutor();
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Task notification = KeepNotificationTask(executor, operation.Task, _ => ++calls);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        operation.SetResult();
        await Drain(executor, notification);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ImmediateShutdownSettlesRemovedProgressNotifications(bool orderedChild, bool completedSource)
    {
        var pool = new UnorderedThreadPoolEventExecutor(1);
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var active = pool.SubmitAsync(() =>
        {
            started.Set();
            try { release.Wait(); } catch (ThreadInterruptedException) { }
        });
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        IEventExecutor executor = orderedChild ? new NonStickyEventExecutorGroup(pool, 1).next() : pool;
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (completedSource) source.SetResult();
        int callbacks = 0;
        using var reporter = new ExecutorProgress(executor, source.Task, _ => ++callbacks, _ => ++callbacks);
        if (!completedSource) Assert.True(reporter.TryReport(1, 10));
        try
        {
            Assert.Equal(1, pool.getQueue().Count);
            pool.shutdownNow();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await reporter.NotificationsCompleted.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(0, callbacks);
            Assert.Equal(completedSource, source.Task.IsCompleted);
            if (!completedSource) source.SetResult();
        }
        finally
        {
            release.Set();
            await active.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(await Task.Run(() => pool.awaitTermination(TimeSpan.FromSeconds(5))));
        }
    }
}
