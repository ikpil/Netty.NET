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

public class ExecutorCompletionContractTest
{
    private sealed class QueuedExecutor : AbstractEventExecutor
    {
        private readonly ConcurrentQueue<IRunnable> _queue = new();
        private Thread _running;
        internal Exception Rejection;
        internal IRunnable LastWork;
        internal int Pending => _queue.Count;
        internal void RunAll()
        {
            _running = Thread.CurrentThread;
            try { while (_queue.TryDequeue(out var work)) work.run(); }
            finally { _running = null; }
        }
        public override bool inEventLoop(Thread thread) => thread != null && thread == _running;
        public override void execute(IRunnable work)
        {
            if (Rejection != null) throw Rejection;
            LastWork = work;
            _queue.Enqueue(work);
        }
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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SourceTaskAloneOwnsSuccessFailureAndCancellation(int outcome)
    {
        var executor = new QueuedExecutor();
        var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var observation = new ExecutorCompletion(executor, source.Task);
        Task observed = null;
        bool affinity = false;
        using var registration = observation.Register(task => { observed = task; affinity = executor.inEventLoop(); });
        var failure = new InvalidOperationException("source failure");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        if (outcome == 0) source.SetResult(42);
        else if (outcome == 1) source.SetException(failure);
        else source.SetCanceled(cancellation.Token);
        await Drain(executor, registration.NotificationCompleted);
        Assert.Same(source.Task, observed);
        Assert.True(affinity);
        Assert.False((object)executor.LastWork is System.Threading.Tasks.Task);
        if (outcome == 0) Assert.Equal(42, await source.Task);
        else if (outcome == 1) Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await source.Task));
        else
        {
            var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await source.Task);
            Assert.Equal(cancellation.Token, canceled.CancellationToken);
        }
    }

    [Fact]
    public async Task EarlyQueuedAndLateRegistrationsPreserveOrderAndAreDistinctFromOperationCompletion()
    {
        var executor = new QueuedExecutor();
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var observation = new ExecutorCompletion(executor, source.Task);
        var seen = new List<int>();
        using var first = observation.Register(_ => seen.Add(1));
        using var second = observation.Register(_ => seen.Add(2));
        source.SetResult();
        await source.Task;
        using var third = observation.Register(_ => seen.Add(3));
        Assert.False(first.NotificationCompleted.IsCompleted);
        Assert.Empty(seen);
        await Drain(executor, Task.WhenAll(first.NotificationCompleted, second.NotificationCompleted, third.NotificationCompleted));
        using var late = observation.Register(_ => seen.Add(4));
        await Drain(executor, late.NotificationCompleted);
        Assert.Equal(new[] { 1, 2, 3, 4 }, seen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovingPendingRegistrationDoesNotCancelTheSource(bool completed)
    {
        var executor = new QueuedExecutor();
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (completed) source.SetResult();
        using var observation = new ExecutorCompletion(executor, source.Task);
        int calls = 0;
        var registration = observation.Register(_ => ++calls);
        registration.Dispose();
        registration.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await registration.NotificationCompleted);
        Assert.Equal(completed, source.Task.IsCompleted);
        if (!completed) source.SetResult();
        executor.RunAll();
        using var barrier = observation.Register(_ => { });
        await Drain(executor, barrier.NotificationCompleted);
        Assert.Equal(0, calls);
        await source.Task;
    }

    [Fact]
    public async Task RemovingOneDuplicateDelegateRegistrationLeavesTheOther()
    {
        var executor = new QueuedExecutor();
        using var observation = new ExecutorCompletion(executor, Task.CompletedTask);
        int calls = 0;
        Action<Task> callback = _ => ++calls;
        var first = observation.Register(callback);
        using var second = observation.Register(callback);
        first.Dispose();
        await Drain(executor, second.NotificationCompleted);
        Assert.True(first.NotificationCompleted.IsCanceled);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ClaimedSnapshotSurvivesRemovalAndReentrantRegistrationsFollowIt()
    {
        var executor = new QueuedExecutor();
        using var observation = new ExecutorCompletion(executor, Task.CompletedTask);
        var seen = new List<int>();
        CompletionRegistration second = null, late = null;
        using var first = observation.Register(_ =>
        {
            seen.Add(1);
            second.Dispose();
            late = observation.Register(_ => seen.Add(3));
        });
        second = observation.Register(_ => seen.Add(2));
        executor.RunAll();
        await Task.WhenAll(first.NotificationCompleted, second.NotificationCompleted, late.NotificationCompleted);
        Assert.Equal(new[] { 1, 2, 3 }, seen);
    }

    [Fact]
    public async Task ObserverFailureIsIsolatedAcrossMulticastEntriesAndRegistrations()
    {
        var executor = new QueuedExecutor();
        using var observation = new ExecutorCompletion(executor, Task.CompletedTask);
        var seen = new List<int>();
        Action<Task> callback = _ => { seen.Add(1); throw new InvalidOperationException("observer failure"); };
        callback += _ => seen.Add(2);
        using var first = observation.Register(callback);
        using var second = observation.Register(_ => seen.Add(3));
        await Drain(executor, Task.WhenAll(first.NotificationCompleted, second.NotificationCompleted));
        Assert.Equal(new[] { 1, 2, 3 }, seen);
    }

    [Fact]
    public void CompletedTaskCanInvokeInlineOnItsExecutor()
    {
        using var observation = new ExecutorCompletion(ImmediateEventExecutor.INSTANCE, Task.CompletedTask);
        bool called = false;
        using var registration = observation.Register(_ => called = true);
        Assert.True(called);
        Assert.True(registration.NotificationCompleted.IsCompletedSuccessfully);
    }

    [Fact]
    public void ReentrantRegistrationUsesABoundedStack()
    {
        using var observation = new ExecutorCompletion(ImmediateEventExecutor.INSTANCE, Task.CompletedTask);
        int calls = 0, depth = 0, maximum = 0;
        Action<Task> callback = null;
        callback = _ =>
        {
            maximum = Math.Max(maximum, ++depth);
            if (++calls < 10_000) observation.Register(callback);
            --depth;
        };
        using var registration = observation.Register(callback);
        Assert.Equal(10_000, calls);
        Assert.Equal(1, maximum);
        Assert.True(registration.NotificationCompleted.IsCompletedSuccessfully);
    }

    [Fact]
    public void ChainedObservationsBoundCrossInstanceRecursion()
    {
        var observations = Enumerable.Range(0, 10_000)
            .Select(_ => new ExecutorCompletion(ImmediateEventExecutor.INSTANCE, Task.CompletedTask)).ToArray();
        int calls = 0, depth = 0, maximum = 0;
        void Invoke(int index)
        {
            maximum = Math.Max(maximum, ++depth);
            ++calls;
            if (index + 1 < observations.Length) observations[index + 1].Register(_ => Invoke(index + 1));
            --depth;
        }
        try
        {
            using var registration = observations[0].Register(_ => Invoke(0));
            Assert.Equal(observations.Length, calls);
            Assert.InRange(maximum, 1, 16);
        }
        finally { foreach (var observation in observations) observation.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallbackContextUsesExecutorValuesAndIsolatesMutations(bool suppressFlow)
    {
        var executor = new QueuedExecutor();
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ambient = new AsyncLocal<string>();
        ambient.Value = "constructor";
        using var observation = new ExecutorCompletion(executor, source.Task);
        var seen = new List<string>();
        ambient.Value = "registration";
        Action<Task> callback = _ => { seen.Add(ambient.Value); ambient.Value = "callback mutation"; };
        callback += _ => seen.Add(ambient.Value);
        using var first = observation.Register(callback);
        using var second = observation.Register(_ => seen.Add(ambient.Value));
        ambient.Value = "producer";
        source.SetResult();
        // Registration against an already-terminal source guarantees a queued drain without timing assumptions.
        using var barrier = observation.Register(_ => { });
        ambient.Value = "executor";
        if (suppressFlow) { using (ExecutionContext.SuppressFlow()) executor.RunAll(); }
        else executor.RunAll();
        Assert.Equal("executor", ambient.Value);
        await Task.WhenAll(first.NotificationCompleted, second.NotificationCompleted, barrier.NotificationCompleted);
        Assert.Equal(new[] { "executor", "executor", "executor" }, seen);
    }

    [Fact]
    public async Task DisposingObservationCancelsPendingNotificationsAndClosesRegistration()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observation = new ExecutorCompletion(new QueuedExecutor(), source.Task);
        using var registration = observation.Register(_ => { });
        observation.Dispose();
        observation.Dispose();
        Assert.False(source.Task.IsCompleted);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await registration.NotificationCompleted);
        Assert.Throws<ObjectDisposedException>(() => observation.Register(_ => { }));
        source.SetResult();
    }

    [Fact]
    public async Task RejectionFaultsNotificationTasksAndClosesTheObservation()
    {
        var error = new InvalidOperationException("executor admission failed");
        var executor = new QueuedExecutor { Rejection = error };
        using var observation = new ExecutorCompletion(executor, Task.CompletedTask);
        int calls = 0;
        using var registration = observation.Register(_ => ++calls);
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(async () => await registration.NotificationCompleted));
        Assert.Equal(0, calls);
        Assert.Throws<ObjectDisposedException>(() => observation.Register(_ => { }));
    }

    [Fact]
    public async Task DisposingFromAClaimedCallbackAllowsItsSnapshotToFinishAndCancelsLaterAdditions()
    {
        var executor = new QueuedExecutor();
        var observation = new ExecutorCompletion(executor, Task.CompletedTask);
        var seen = new List<int>();
        CompletionRegistration late = null;
        using var first = observation.Register(_ =>
        {
            seen.Add(1);
            late = observation.Register(_ => seen.Add(3));
            observation.Dispose();
        });
        using var second = observation.Register(_ => seen.Add(2));
        executor.RunAll();
        await Task.WhenAll(first.NotificationCompleted, second.NotificationCompleted);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await late.NotificationCompleted);
        Assert.Equal(new[] { 1, 2 }, seen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedNativeNotificationsCannotBeSilentlyDiscarded(bool orderedChild)
    {
        var pool = new UnorderedThreadPoolEventExecutor(1, (_, _) => { });
        IEventExecutor executor = orderedChild ? new NonStickyEventExecutorGroup(pool, 1).next() : pool;
        pool.shutdownNow();
        using var observation = new ExecutorCompletion(executor, Task.CompletedTask);
        using var registration = observation.Register(_ => { });
        await Assert.ThrowsAsync<RejectedExecutionException>(async () =>
            await registration.NotificationCompleted.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(pool.awaitTermination(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task UnorderedExecutorSerializesConcurrentRegistrationsInPerProducerOrder()
    {
        var executor = new UnorderedThreadPoolEventExecutor(4);
        using var observation = new ExecutorCompletion(executor, Task.CompletedTask);
        var tasks = new ConcurrentBag<Task>();
        var last = new int[4];
        Array.Fill(last, -1);
        int active = 0, overlaps = 0, outOfOrder = 0, calls = 0;
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 4).Select(producer => Task.Run(() =>
            {
                for (int index = 0; index < 200; ++index)
                {
                    int captured = index;
                    tasks.Add(observation.Register(_ =>
                    {
                        if (Interlocked.Increment(ref active) != 1) Interlocked.Increment(ref overlaps);
                        if (last[producer] + 1 != captured) Interlocked.Increment(ref outOfOrder);
                        last[producer] = captured;
                        Interlocked.Increment(ref calls);
                        Interlocked.Decrement(ref active);
                    }).NotificationCompleted);
                }
            })));
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(800, calls);
            Assert.Equal(0, overlaps);
            Assert.Equal(0, outOfOrder);
        }
        finally
        {
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            Assert.True(await Task.Run(() => executor.awaitTermination(TimeSpan.FromSeconds(5))));
        }
    }

    [Fact]
    public async Task CompletionRemovalRaceAlwaysSettlesEachNotificationAtMostOnce()
    {
        for (int iteration = 0; iteration < 200; ++iteration)
        {
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var observation = new ExecutorCompletion(ImmediateEventExecutor.INSTANCE, source.Task);
            int calls = 0;
            using var registration = observation.Register(_ => Interlocked.Increment(ref calls));
            await Task.WhenAll(Task.Run(() => source.SetResult()), Task.Run(registration.Dispose));
            try { await registration.NotificationCompleted.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
            Assert.Equal(registration.NotificationCompleted.IsCanceled ? 0 : 1, calls);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownNowCancelsAQueuedNativeNotificationWithoutAFutureWrapper(bool orderedChild)
    {
        var executor = new UnorderedThreadPoolEventExecutor(1);
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var active = executor.SubmitAsync(() =>
        {
            started.Set();
            try { release.Wait(); } catch (ThreadInterruptedException) { }
        });
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        IEventExecutor target = orderedChild ? new NonStickyEventExecutorGroup(executor, 1).next() : executor;
        using var observation = new ExecutorCompletion(target, Task.CompletedTask);
        int calls = 0;
        using var registration = observation.Register(_ => ++calls);
        try
        {
            Assert.Equal(1, executor.PendingTaskCount);
            IRunnable queued = Assert.Single(executor.shutdownNow());
            Assert.False((object)queued is System.Threading.Tasks.Task);
            queued.run();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await registration.NotificationCompleted.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, calls);
        }
        finally
        {
            release.Set();
            await active.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(await Task.Run(() => executor.awaitTermination(TimeSpan.FromSeconds(5))));
        }
    }

    [Fact]
    public async Task ResolverStyleRemovalReleasesCapturedOwnerWhileTerminationRemainsPending()
    {
        var termination = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var observation = new ExecutorCompletion(new QueuedExecutor(), termination.Task);
        var retained = RegisterCapturedOwner(observation);
        retained.Registration.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await retained.Registration.NotificationCompleted);
        Collect();
        Assert.False(retained.Owner.IsAlive);
        Assert.False(termination.Task.IsCompleted);
        GC.KeepAlive(retained.Registration);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (CompletionRegistration Registration, WeakReference Owner) RegisterCapturedOwner(ExecutorCompletion observation)
    {
        var owner = new object();
        return (observation.Register(_ => GC.KeepAlive(owner)), new WeakReference(owner));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AbandonObservation(Task operation)
    {
        var observation = new ExecutorCompletion(new QueuedExecutor(), operation);
        observation.Register(_ => { });
        return new WeakReference(observation);
    }

    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }

    [Fact]
    public void PendingSourceTaskDoesNotRetainAnAbandonedObservation()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var abandoned = AbandonObservation(source.Task);
        Collect();
        Assert.False(abandoned.IsAlive);
        GC.KeepAlive(source);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task KeepNotificationTask(QueuedExecutor executor, Task operation, Action<Task> callback)
        => new ExecutorCompletion(executor, operation).Register(callback).NotificationCompleted;

    [Fact]
    public async Task RetainingOnlyNotificationTaskKeepsItsPendingObservationAlive()
    {
        var executor = new QueuedExecutor();
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Task notification = KeepNotificationTask(executor, source.Task, _ => ++calls);
        Collect();
        source.SetResult();
        await Drain(executor, notification);
        Assert.Equal(1, calls);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Task Notification, WeakReference Result) CompletedResult(QueuedExecutor executor)
    {
        var result = new object();
        var source = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task notification = KeepNotificationTask(executor, source.Task, _ => { });
        source.SetResult(result);
        return (notification, new WeakReference(result));
    }

    [Fact]
    public async Task RetainedCompletedNotificationDoesNotRetainSourceResult()
    {
        var executor = new QueuedExecutor();
        var retained = CompletedResult(executor);
        await Drain(executor, retained.Notification);
        Collect();
        Assert.False(retained.Result.IsAlive);
        GC.KeepAlive(retained.Notification);
    }

    [Fact]
    public async Task HandshakeStyleCompletionCancelsExecutorTimeout()
    {
        var executor = new DefaultEventExecutor();
        using var timeoutCancellation = new CancellationTokenSource();
        var handshake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var observation = new ExecutorCompletion(executor, handshake.Task);
        int timeouts = 0;
        bool affinity = false;
        Task timeout = executor.ScheduleAsync(() => Interlocked.Increment(ref timeouts), TimeSpan.FromHours(1), timeoutCancellation.Token);
        using var registration = observation.Register(_ =>
        {
            affinity = executor.inEventLoop();
            timeoutCancellation.Cancel();
        });
        try
        {
            handshake.SetResult();
            await registration.NotificationCompleted.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await timeout);
            Assert.True(affinity);
            Assert.Equal(0, timeouts);
        }
        finally { await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero); }
    }

    [Fact]
    public void InvalidArgumentsFailSynchronously()
    {
        Assert.Throws<ArgumentNullException>(() => new ExecutorCompletion(null, Task.CompletedTask));
        Assert.Throws<ArgumentNullException>(() => new ExecutorCompletion(ImmediateEventExecutor.INSTANCE, null));
        using var observation = new ExecutorCompletion(ImmediateEventExecutor.INSTANCE, Task.CompletedTask);
        Assert.Throws<ArgumentNullException>(() => observation.Register(null));
    }
}
