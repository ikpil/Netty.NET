using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Xunit;
using Void = Netty.NET.Common.Concurrent.Void;

namespace Netty.NET.Common.Tests.Porting;

[Collection("Global executor")]
public class ExecutorLifecycleContractTest
{
    private sealed class Child : AbstractScheduledEventExecutor
    {
        internal readonly TaskCompletionSource termination = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly Queue<Action> tasks = new();
        internal bool queued;
        internal int executions;
        internal IScheduledWork scheduledSubmission;
        internal int rejectExecution = -1;
        internal bool shuttingDown;
        internal bool stopped;
        internal bool completeOnShutdown;
        internal TimeSpan quietPeriod;
        internal TimeSpan timeout;
        internal int stopRequests;
        internal Exception stopFailure;
        internal readonly List<TimeSpan> waits = new();
        private bool running;
        internal Child(IEventExecutorGroup parent = null) : base(parent) { }
        internal IScheduledWork FirstScheduled() => PeekScheduledTask();
        public override bool InEventLoop(Thread thread) => running && thread == Thread.CurrentThread;
        public override void Execute(Action command)
        {
            if (++executions == rejectExecution)
                throw new RejectedExecutionException("Simulated queue full");
            if (queued)
                tasks.Enqueue(command);
            else
            {
                Run(command);
                scheduledSubmission = _scheduledTaskQueue?.ToArray()
                    .FirstOrDefault(work => ReferenceEquals(work.QueueCallback, command)) ?? scheduledSubmission;
            }
        }
        internal void Run(Action command)
        {
            running = true;
            try { command(); }
            finally { running = false; }
        }
        public override Task Termination => termination.Task;
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
        {
            shuttingDown = true;
            this.quietPeriod = quietPeriod;
            this.timeout = timeout;
            if (completeOnShutdown) termination.TrySetResult();
            return termination.Task;
        }
        public override void Shutdown() { shuttingDown = stopped = true; }
        public override Task StopAsync()
        {
            ++stopRequests;
            Shutdown();
            if (stopFailure != null) throw stopFailure;
            return Termination;
        }
        public override bool IsShuttingDown() => shuttingDown;
        public override bool IsShutdown() => stopped;
        public override bool IsTerminated() => termination.Task.IsCompleted;
        public override bool AwaitTermination(TimeSpan timeout)
        {
            waits.Add(timeout);
            return termination.Task.IsCompleted;
        }
    }

    private sealed class SingleChildGroup : AbstractEventExecutorGroup
    {
        internal readonly Child child;
        internal int selections;
        internal SingleChildGroup(Child child) => this.child = child;
        public override IEventExecutor Next() { ++selections; return child; }
        public override IEnumerable<IEventExecutor> Iterator() => new[] { child };
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => child.ShutdownGracefullyAsync(quietPeriod, timeout);
        public override Task Termination => child.Termination;
        public override void Shutdown() => child.Shutdown();
        public override bool IsShuttingDown() => child.IsShuttingDown();
        public override bool IsShutdown() => child.IsShutdown();
        public override bool IsTerminated() => child.IsTerminated();
        public override bool AwaitTermination(TimeSpan timeout) => child.AwaitTermination(timeout);
    }

    private sealed class ManualGroup : MultithreadEventExecutorGroup
    {
        internal ManualGroup(params Child[] children) : this(DefaultEventExecutorChooserFactory.INSTANCE, children) { }
        internal ManualGroup(IEventExecutorChooserFactory factory, params Child[] children)
            : base(children.Length, command => command(), factory, (object)children) { }
        protected override IEventExecutor NewChild(Action<Action> executor, params object[] args) =>
            ((Child[])args[0])[iteratorIndex++];
        private int iteratorIndex;
    }

    private sealed class ObservableChooser : IEventExecutorChooserFactory, IObservableEventExecutorChooser
    {
        internal IEventExecutor[] children;
        internal IReadOnlyList<AutoScalingUtilizationMetric> metrics;
        public IEventExecutorChooser NewChooser(IEventExecutor[] executors)
        {
            children = executors;
            metrics = Array.AsReadOnly(executors.Select(executor => new AutoScalingUtilizationMetric(executor)).ToArray());
            return this;
        }
        public IEventExecutor Next() => children[0];
        public int ActiveExecutorCount() => 1;
        public IReadOnlyList<AutoScalingUtilizationMetric> ExecutorUtilizations() => metrics;
    }

    private sealed class FailingGroup : MultithreadEventExecutorGroup
    {
        internal FailingGroup(List<Child> created, Exception failure)
            : base(3, command => command(), created, failure) { }
        protected override IEventExecutor NewChild(Action<Action> executor, params object[] args)
        {
            var created = (List<Child>)args[0];
            if (created.Count == 2) throw (Exception)args[1];
            var child = new Child(this) { completeOnShutdown = true };
            created.Add(child);
            return child;
        }
    }

    private sealed class RejectingExecutor
    {
        internal readonly RejectedExecutionException failure = new("worker rejected");
        public void Execute(Action command) => throw failure;
    }

    private sealed class FailingWorker : SingleThreadEventExecutor
    {
        private readonly Exception failure;
        internal FailingWorker(Exception failure)
            : base(null, new DefaultThreadFactory("lifecycle-failure", true), true) => this.failure = failure;
        protected override void Run() => throw failure;
    }

    [Fact]
    public async Task ShutdownReturnsThePersistentTerminationTask()
    {
        var executor = new DefaultEventExecutor();
        var termination = executor.Termination;
        Assert.False(termination.IsCompleted);
        Assert.Same(termination, executor.Termination);
        Assert.Same(termination, executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero));
        await termination.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(termination.IsCompletedSuccessfully);
        Assert.Same(termination, executor.ShutdownGracefullyAsync());
        Assert.True(executor.IsTerminated());
    }

    [Fact]
    public async Task CancelingOneShutdownWaitLeavesTheProducerAndOtherWaitersRunning()
    {
        var executor = new DefaultEventExecutor();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        Task work = executor.SubmitAsync(() => { entered.Set(); release.Wait(); });
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Task termination = executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            Task canceledWait = termination.WaitAsync(cancellation.Token);
            Task ordinaryWait = termination.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWait);
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.False(termination.IsCompleted);
            Assert.False(ordinaryWait.IsCompleted);
            Assert.True(executor.IsShuttingDown());
            Assert.Same(termination, executor.Termination);
            release.Set();
            await work.WaitAsync(TimeSpan.FromSeconds(5));
            await ordinaryWait;
            Assert.True(termination.IsCompletedSuccessfully);
            Assert.True(executor.IsTerminated());
        }
        finally
        {
            release.Set();
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task TimingOutAnObservationDoesNotRequestShutdown()
    {
        var executor = new DefaultEventExecutor();
        Task termination = executor.Termination;
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => termination.WaitAsync(TimeSpan.Zero));
            Assert.False(termination.IsCompleted);
            Assert.False(executor.IsShuttingDown());
            Assert.Equal(42, await executor.SubmitAsync(() => 42).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(termination, executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero));
            await termination.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task AnAsyncDelegateCanRequestAndAwaitItsOwnExecutorShutdown()
    {
        var executor = new DefaultEventExecutor();
        try
        {
            Task operation = executor.SubmitAsync(async () =>
            {
                Assert.True(executor.InEventLoop());
                Task termination = executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
                Assert.False(termination.IsCompleted);
                // Yielding releases the physical event-loop thread so it can finish shutdown.
                await termination.ConfigureAwait(false);
                Assert.False(executor.InEventLoop());
                Assert.True(executor.IsTerminated());
            });
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task TerminationDoesNotInlineUserContinuationsOnTheEventLoop()
    {
        var executor = new DefaultEventExecutor();
        Task continuation = executor.Termination.ContinueWith(_ => Assert.False(executor.InEventLoop()),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try
        {
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5));
            await continuation.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class DormantSynchronizationContext : SynchronizationContext
    {
        internal int posts;
        public override void Post(SendOrPostCallback callback, object state) => Interlocked.Increment(ref posts);
    }

    [Fact]
    public async Task GroupCompletionDoesNotDependOnPumpingTheConstructorsSynchronizationContext()
    {
        var first = new Child();
        var second = new Child();
        var context = new DormantSynchronizationContext();
        SynchronizationContext previous = SynchronizationContext.Current;
        ManualGroup group;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            group = new ManualGroup(first, second);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        first.termination.SetResult();
        second.termination.SetResult();
        await group.Termination.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, Volatile.Read(ref context.posts));
    }

    [Fact]
    public async Task AlreadyCompletedFailedAndCanceledChildrenStillCompleteTheGroup()
    {
        var success = new Child();
        var failure = new Child();
        var canceled = new Child();
        success.termination.SetResult();
        failure.termination.SetException(new InvalidOperationException("child failed before registration"));
        canceled.termination.SetCanceled();
        var group = new ManualGroup(success, failure, canceled);
        await group.Termination.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(group.Termination.IsCompletedSuccessfully);
        Assert.True(group.IsTerminated());
        Assert.True(failure.Termination.IsFaulted);
        Assert.True(canceled.Termination.IsCanceled);
    }

    [Fact]
    public async Task ConcurrentChildCompletionsPublishTheGroupSignalExactlyOnce()
    {
        var children = Enumerable.Range(0, 32).Select(_ => new Child()).ToArray();
        var group = new ManualGroup(children);
        using var start = new ManualResetEventSlim();
        Task[] completions = children.Select(child => Task.Run(() =>
        {
            start.Wait();
            child.termination.SetResult();
        })).ToArray();
        Assert.False(group.Termination.IsCompleted);
        start.Set();
        await Task.WhenAll(completions).WaitAsync(TimeSpan.FromSeconds(5));
        await group.Termination.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(group.Termination.IsCompletedSuccessfully);
        Assert.True(group.IsTerminated());
        Assert.Same(group.Termination, group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero));
    }

    [Fact]
    public async Task InvalidShutdownArgumentsDoNotStartShutdown()
    {
        var executor = new DefaultEventExecutor();
        try
        {
            Assert.Throws<ArgumentException>(() => executor.ShutdownGracefullyAsync(TimeSpan.FromTicks(-1), TimeSpan.Zero));
            Assert.Throws<ArgumentException>(() => executor.ShutdownGracefullyAsync(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)));
            Assert.False(executor.IsShuttingDown());
            Assert.False(executor.Termination.IsCompleted);
        }
        finally { await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task WorkerStartFailureRetainsCauseAndAllowsExplicitExecutorNotification()
    {
        var backing = new RejectingExecutor();
        var executor = new DefaultEventExecutor(backing.Execute);
        Task termination = executor.Termination;
        Task failure = executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
        Assert.Same(termination, failure);
        Assert.Same(backing.failure, await Assert.ThrowsAsync<RejectedExecutionException>(() => failure));
        Assert.Same(backing.failure, failure.Exception.InnerException);
        // Native Task continuations do not choose a Netty executor. Consumers
        // that require affinity dispatch explicitly after observing completion.
        await GlobalEventExecutor.INSTANCE.SubmitAsync(() =>
        {
            Assert.True(GlobalEventExecutor.INSTANCE.InEventLoop());
            Assert.Same(termination, executor.Termination);
        }).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(executor.IsTerminated());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AWorkerCancellationExceptionIsALifecycleFailureRatherThanObserverCancellation(bool atStartup)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var failure = new OperationCanceledException("unexpected worker failure", cancellation.Token);
        SingleThreadEventExecutor executor = atStartup
            ? new DefaultEventExecutor(_ => throw failure)
            : new FailingWorker(failure);
        Task termination = executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
        Assert.Same(failure, await Assert.ThrowsAsync<OperationCanceledException>(() =>
            termination.WaitAsync(TimeSpan.FromSeconds(5))));
        Assert.True(termination.IsFaulted);
        Assert.False(termination.IsCanceled);
        Assert.Same(failure, termination.Exception.InnerException);
        Assert.Same(termination, executor.Termination);
        Assert.True(executor.IsTerminated());
    }

    [Fact]
    public void NonTerminableExecutorsExposeTheirPersistentFailedTask()
    {
        foreach (var executor in new IEventExecutor[] { GlobalEventExecutor.INSTANCE, ImmediateEventExecutor.INSTANCE })
        {
            var future = executor.Termination;
            Assert.True(future.IsCompleted);
            Assert.True(future.IsFaulted);
            Assert.IsAssignableFrom<NotSupportedException>(future.Exception.InnerException);
            Assert.Same(future, executor.ShutdownGracefullyAsync());
            Assert.Same(future, executor.ShutdownGracefullyAsync(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(-2)));
            Assert.Same(future, executor.Termination);
            Assert.Same(future, executor.StopAsync());
            Assert.False(executor.IsShuttingDown());
            Assert.False(executor.IsShutdown());
            Assert.False(executor.IsTerminated());
        }
    }

    [Fact]
    public async Task MultithreadGroupCompletesOnlyAfterEveryChildEvenWhenOneFails()
    {
        var first = new Child();
        var second = new Child();
        var group = new ManualGroup(first, second);
        var termination = group.Termination;
        Assert.Same(termination, group.ShutdownGracefullyAsync(TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(1)));
        Assert.True(group.IsShuttingDown());
        Assert.False(group.IsShutdown());
        Assert.Equal(TimeSpan.FromMilliseconds(20), first.quietPeriod);
        Assert.Equal(TimeSpan.FromSeconds(1), second.timeout);
        first.termination.SetException(new InvalidOperationException("child failure"));
        Assert.False(termination.IsCompleted);
        second.termination.SetResult();
        await termination.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(termination.IsCompletedSuccessfully);
        Assert.True(group.IsTerminated());
        Assert.Same(termination, group.Termination);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeOrderedStopClosesAdmissionAndDrainsAcceptedInvocations(bool gracefulFirst)
    {
        IEventExecutorGroup executor = new DefaultEventExecutor();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task<int> running = executor.SubmitAsync(() => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); return 7; });
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Task<int> queued = executor.SubmitAsync(() => 8);
            Task delayed = executor.ScheduleAsync(() => Assert.Fail("Stopped schedule ran"), TimeSpan.FromDays(1));
            if (gracefulFirst)
            {
                executor.ShutdownGracefullyAsync(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
                Assert.False(executor.IsShutdown());
            }
            Task stopping = executor.StopAsync();
            Assert.Same(executor.Termination, stopping);
            Assert.Same(stopping, executor.StopAsync());
            Assert.True(executor.IsShutdown());
            Assert.Same(stopping, executor.ShutdownGracefullyAsync(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
            Assert.True(executor.IsShutdown());
            Assert.False(stopping.IsCompleted);
            await Assert.ThrowsAsync<RejectedExecutionException>(() => executor.SubmitAsync(() => 9));
            using var observer = new CancellationTokenSource();
            observer.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopping.WaitAsync(observer.Token));
            Assert.False(stopping.IsCompleted);
            release.Set();
            Assert.Equal(7, await running.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(8, await queued.WaitAsync(TimeSpan.FromSeconds(5)));
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(delayed.IsCanceled);
        }
        finally { release.Set(); await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeNonStickyStopPreservesUnderlyingWithdrawalAndLifecycle(bool selectedChild)
    {
        var underlying = new UnorderedThreadPoolEventExecutor(1);
        IEventExecutorGroup group = new NonStickyEventExecutorGroup(underlying);
        IEventExecutorGroup surface = selectedChild ? group.Next() : group;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task running = underlying.SubmitAsync(() => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); });
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Task queued = surface.SubmitAsync(() => Assert.Fail("Withdrawn nonsticky work ran"));
            Task stopping = surface.StopAsync();
            Assert.Same(underlying.Termination, stopping);
            Assert.Same(stopping, group.StopAsync());
            Assert.True(underlying.StopToken.IsCancellationRequested);
            Assert.True(queued.IsCanceled);
            Assert.False(stopping.IsCompleted);
            release.Set();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { release.Set(); await group.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task NativeGroupStopRequestsEveryChildAndPreservesCompletionCounting()
    {
        var first = new Child();
        var second = new Child();
        IEventExecutorGroup group = new ManualGroup(first, second);
        Task stopping = group.StopAsync();
        Assert.Same(group.Termination, stopping);
        Assert.Equal(1, first.stopRequests);
        Assert.Equal(1, second.stopRequests);
        Assert.True(group.IsShutdown());
        first.termination.SetException(new InvalidOperationException("child failed"));
        Assert.False(stopping.IsCompleted);
        second.termination.SetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(stopping.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task NativeGroupStopRequestFailuresStillReachEveryOtherChild()
    {
        var first = new Child { stopFailure = new InvalidOperationException("first stop failed") };
        var second = new Child();
        var third = new Child { stopFailure = new ArgumentException("third stop failed") };
        var group = new ManualGroup(first, second, third);
        AggregateException failure = Assert.Throws<AggregateException>(() => group.StopAsync());
        Assert.Equal(new[] { first.stopFailure, third.stopFailure }, failure.InnerExceptions);
        Assert.All(new[] { first, second, third }, child => Assert.Equal(1, child.stopRequests));
        Assert.False(group.Termination.IsCompleted);
        foreach (Child child in new[] { first, second, third }) child.termination.SetResult();
        await group.Termination.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task NativeStopCanBeAwaitedFromItsYieldedOrderedInvocation()
    {
        IEventExecutorGroup executor = new DefaultEventExecutor();
        try
        {
            await executor.SubmitAsync(async () => await executor.StopAsync()).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(executor.Termination.IsCompletedSuccessfully);
        }
        finally { await executor.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task ChildEnumerationCannotMutateGroupAndNativeStopRequestsEveryChild()
    {
        var children = new[] { new Child(), new Child(), new Child() };
        var group = new ManualGroup(children);
        Assert.False(group.Iterator() is ICollection<IEventExecutor>);
        Assert.Equal(children, group.Iterator());
        Assert.Equal(3, group.ExecutorCount());
        for (int i = 0; i < 12; ++i) Assert.Same(children[i % 3], group.Next());
        Assert.Same(group.Termination, group.StopAsync());
        Assert.True(group.IsShutdown());
        Assert.All(children, child => Assert.Equal(1, child.stopRequests));
        foreach (var child in children) child.termination.SetResult();
        await group.Termination.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GroupLargeTimeoutDoesNotBecomeNegativeDuringNanosecondConversion()
    {
        var child = new Child();
        child.termination.SetResult();
        var group = new ManualGroup(child);
        Assert.True(group.AwaitTermination(TimeSpan.MaxValue));
        Assert.Single(child.waits);
        Assert.True(child.waits[0] > TimeSpan.FromDays(1000));
        Assert.True(group.AwaitTermination(TimeSpan.Zero));
        Assert.Single(child.waits);
    }

    [Fact]
    public async Task GroupExposesObservableMetricsAndDefaultCounts()
    {
        var children = new[] { new Child(), new Child(), new Child() };
        var ordinary = new ManualGroup(children);
        Assert.Equal(3, ordinary.ActiveExecutorCount());
        Assert.Empty(ordinary.ExecutorUtilizations());
        var chooser = new ObservableChooser();
        var observable = new ManualGroup(chooser, children);
        Assert.Equal(1, observable.ActiveExecutorCount());
        Assert.Same(chooser.metrics, observable.ExecutorUtilizations());
        Assert.Same(children[0], observable.Next());
        var metric = observable.ExecutorUtilizations()[0];
        Assert.Same(children[0], metric.Executor());
        Assert.Equal(0.0, metric.Utilization());
        metric.SetUtilization(0.75);
        Assert.Equal(0.75, observable.ExecutorUtilizations()[0].Utilization());
        long payload = unchecked((long)0x7ff8000000001234UL);
        metric.SetUtilization(BitConverter.Int64BitsToDouble(payload));
        Assert.Equal(payload, BitConverter.DoubleToInt64Bits(metric.Utilization()));
        foreach (var child in children) child.termination.SetResult();
        await ordinary.Termination.WaitAsync(TimeSpan.FromSeconds(5));
        await observable.Termination.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ChildCreationFailureShutsDownAlreadyCreatedExecutors()
    {
        var created = new List<Child>();
        var cause = new InvalidOperationException("third child failed");
        var failure = Assert.Throws<InvalidOperationException>(() => new FailingGroup(created, cause));
        Assert.Same(cause, failure.InnerException);
        Assert.Equal(2, created.Count);
        Assert.All(created, child =>
        {
            Assert.True(child.IsShuttingDown());
            Assert.True(child.IsTerminated());
            Assert.Equal(TimeSpan.FromSeconds(2), child.quietPeriod);
            Assert.Equal(TimeSpan.FromSeconds(15), child.timeout);
        });
    }

    [Fact]
    public void BaseGroupSelectsOneExecutorPerSubmissionAndExplicitBatchSelection()
    {
        var group = new SingleChildGroup(new Child());
        int executed = 0;
        group.Execute(Runnables.Create(() => ++executed));
        group.SubmitAsync(() => { ++executed; }).GetAwaiter().GetResult();
        Assert.Equal(42, group.SubmitAsync(() => 42).GetAwaiter().GetResult());
        Assert.Equal(7, group.SubmitAsync(() => 7).GetAwaiter().GetResult());
        IEventExecutor selected = group.Next();
        Task<int>[] operations = { selected.SubmitAsync(() => 1), selected.SubmitAsync(() => 2) };
        Assert.Equal(new[] { 1, 2 }, Task.WhenAll(operations).GetAwaiter().GetResult());
        Assert.Equal(5, group.selections);
        Assert.Equal(2, executed);
        Assert.Same(Ticker.SystemTicker(), group.Ticker());
        Assert.Same(group.Termination, group.ShutdownGracefullyAsync());
        Assert.Equal(TimeSpan.FromSeconds(2), group.child.quietPeriod);
        Assert.Equal(TimeSpan.FromSeconds(15), group.child.timeout);
    }

    [Fact]
    public async Task DefaultGroupCreatesParentedExecutorsAndCanUseNativeStop()
    {
        var group = new DefaultEventExecutorGroup(2, new DefaultThreadFactory("group-contract", true),
            16, RejectedExecutionHandlers.Reject());
        try
        {
            var children = group.Iterator().ToArray();
            Assert.Equal(2, children.Length);
            Assert.All(children, child => { Assert.IsType<DefaultEventExecutor>(child); Assert.Same(group, child.Parent()); });
            var first = group.SubmitAsync<Thread>(() => Thread.CurrentThread);
            var second = group.SubmitAsync<Thread>(() => Thread.CurrentThread);
            Assert.NotSame(first.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(), second.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.Same(group.Termination, group.StopAsync());
            Assert.True(group.AwaitTermination(TimeSpan.FromSeconds(5)));
            await group.Termination.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { await group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public void BaseGroupForwardsEveryScheduleFormToTheChosenChild()
    {
        var child = new Child();
        var group = new SingleChildGroup(child);
        using var cancellation = new CancellationTokenSource();
        var runnable = group.ScheduleAsync(() => { }, TimeSpan.FromDays(1), cancellation.Token);
        var firstWork = child.scheduledSubmission;
        var callable = group.ScheduleAsync(() => 123, TimeSpan.FromDays(2), cancellation.Token);
        var secondWork = child.scheduledSubmission;
        var rate = group.ScheduleAtFixedRateAsync(() => { }, TimeSpan.FromDays(3), TimeSpan.FromDays(1), cancellation.Token);
        var rateWork = child.scheduledSubmission;
        var delay = group.ScheduleWithFixedDelayAsync(() => { }, TimeSpan.FromDays(4), TimeSpan.FromDays(1), cancellation.Token);
        var delayWork = child.scheduledSubmission;
        Assert.Equal(4, group.selections);
        Assert.Same(firstWork, child.FirstScheduled());
        Assert.False((object)firstWork is System.Threading.Tasks.Task);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, new[] { firstWork.GetId(), secondWork.GetId(), rateWork.GetId(), delayWork.GetId() });
        Assert.True(firstWork.DeadlineNanos() < secondWork.DeadlineNanos());
        Assert.True(secondWork.DeadlineNanos() < rateWork.DeadlineNanos());
        Assert.True(rateWork.DeadlineNanos() < delayWork.DeadlineNanos());
        cancellation.Cancel();
        foreach (Task operation in new Task[] { runnable, callable, rate, delay }) Assert.True(operation.IsCanceled);
    }

    [Fact]
    public void NonStickyGroupForwardsDirectCallsAndCreatesIndependentOrderedWrappers()
    {
        var child = new Child { queued = true };
        var underlying = new SingleChildGroup(child);
        var group = new NonStickyEventExecutorGroup(underlying, 1);
        Assert.Same(child.termination.Task, group.Termination);
        Assert.Same(child.termination.Task, group.ShutdownGracefullyAsync());
        Assert.Same(child.termination.Task, group.Termination);
        var direct = group.SubmitAsync(() => { });
        Assert.IsAssignableFrom<INativeSubmission>(ExecutorWork.GetNativeSubmission(child.tasks.Peek()));
        child.Run(child.tasks.Dequeue());
        direct.GetAwaiter().GetResult();
        var first = group.Next();
        var second = group.Next();
        Assert.NotSame(first, second);
        Assert.IsAssignableFrom<IOrderedEventExecutor>(first);
        Assert.Same(child, first.Parent());
        Assert.Same(child.termination.Task, first.Termination);
        Assert.Same(child.termination.Task, first.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero));
        Assert.IsAssignableFrom<IOrderedEventExecutor>(Assert.Single(group.Iterator()));
    }

    [Fact]
    public void NonStickyRunnerReturnsAfterReschedulingAndDoesNotExecuteTheNextBatch()
    {
        var child = new Child { queued = true };
        var executor = new NonStickyEventExecutorGroup(new SingleChildGroup(child), 1).Next();
        var values = new List<int>();
        for (int i = 0; i < 3; ++i)
        {
            int value = i;
            executor.Execute(Runnables.Create(() => { Assert.True(executor.InEventLoop()); values.Add(value); }));
        }
        Assert.Single(child.tasks);
        for (int i = 0; i < 3; ++i)
        {
            child.Run(child.tasks.Dequeue());
            Assert.Equal(Enumerable.Range(0, i + 1), values);
            Assert.False(executor.InEventLoop());
            Assert.Single(child.tasks);
        }
        child.Run(child.tasks.Dequeue());
        Assert.Empty(child.tasks);
        Assert.False(executor.InEventLoop());
    }

    [Fact]
    public void NonStickyReschedulingFailureRetainsExecutorThreadUntilRetry()
    {
        var child = new Child { queued = true, rejectExecution = 2 };
        var executor = new NonStickyEventExecutorGroup(new SingleChildGroup(child), 1).Next();
        int count = 0;
        executor.Execute(Runnables.Create(() => ++count));
        executor.Execute(Runnables.Create(() => { Assert.True(executor.InEventLoop()); ++count; }));
        child.Run(child.tasks.Dequeue());
        Assert.Equal(2, count);
        Assert.Equal(3, child.executions);
        Assert.False(executor.InEventLoop());
        child.Run(child.tasks.Dequeue());
        Assert.Empty(child.tasks);
    }

    [Fact]
    public void ImmediateExecutorDrainsReentrantTasksInOrderAfterAnException()
    {
        var executor = ImmediateEventExecutor.INSTANCE;
        var order = new List<int>();
        executor.Execute(Runnables.Create(() =>
        {
            order.Add(1);
            executor.Execute(Runnables.Create(() => { order.Add(3); throw new InvalidOperationException("queued"); }));
            executor.Execute(Runnables.Create(() => order.Add(4)));
            order.Add(2);
            throw new InvalidOperationException("outer");
        }));
        executor.Execute(Runnables.Create(() => order.Add(5)));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, order);
        Assert.True(executor.InEventLoop());
        Assert.Same(executor, executor.Next());
        Assert.Same(executor, Assert.Single(executor.Iterator()));
        Assert.Null(executor.Parent());
        var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        long reported = -1;
        using var progress = new ExecutorProgress(executor, source.Task, value => reported = value.Completed);
        progress.Report(new TransferProgress(1, 2));
        Assert.Equal(1, reported);
        Assert.False(source.Task.IsCompleted);
        Assert.Throws<TimeoutException>(() => source.Task.WaitAsync(TimeSpan.FromMilliseconds(1)).GetAwaiter().GetResult());
        Assert.False(source.Task.IsCompleted);
        source.SetResult(2);
        Assert.Equal(2, source.Task.GetAwaiter().GetResult());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void GlobalInactivityPollsOrBoundsSubmillisecondWaits(long ticks)
    {
        var executor = GlobalEventExecutor.INSTANCE;
        using var entered = new CountdownEvent(1);
        using var release = new CountdownEvent(1);
        executor.Execute(Runnables.Create(() => { entered.Signal(); release.Wait(); }));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Exception failure = null;
        bool inactive = false;
        var waiter = new Thread(() =>
        {
            try { inactive = executor.AwaitInactivity(TimeSpan.FromTicks(ticks)); }
            catch (Exception e) { failure = e; }
        }) { IsBackground = true };
        waiter.Start();
        try
        {
            Assert.True(waiter.Join(TimeSpan.FromSeconds(5)));
            Assert.Null(failure);
            Assert.False(inactive);
        }
        finally { release.Signal(); }
        Assert.True(waiter.Join(TimeSpan.FromSeconds(5)));
        Assert.True(executor.AwaitInactivity(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void GlobalLargeInactivityWaitRemainsInterruptible()
    {
        var executor = GlobalEventExecutor.INSTANCE;
        using var entered = new CountdownEvent(1);
        using var release = new CountdownEvent(1);
        executor.Execute(Runnables.Create(() => { entered.Signal(); release.Wait(); }));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Exception failure = null;
        var waiter = new Thread(() =>
        {
            try { executor.AwaitInactivity(TimeSpan.MaxValue); }
            catch (Exception e) { failure = e; }
        }) { IsBackground = true };
        waiter.Start();
        try
        {
            Assert.False(waiter.Join(TimeSpan.FromMilliseconds(50)));
            waiter.Interrupt();
            Assert.True(waiter.Join(TimeSpan.FromSeconds(5)));
            Assert.IsType<ThreadInterruptedException>(failure);
            Assert.Throws<ArgumentOutOfRangeException>(() => executor.AwaitInactivity(TimeSpan.FromMilliseconds(-2)));
            Assert.Throws<ArgumentOutOfRangeException>(() => executor.AwaitInactivity(TimeSpan.FromTicks(-1)));
        }
        finally { release.Signal(); }
        Assert.True(executor.AwaitInactivity(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlobalInfiniteInactivityWaitCompletesOrCanBeInterrupted(bool interrupt)
    {
        var executor = GlobalEventExecutor.INSTANCE;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        executor.Execute(() => { entered.Set(); release.Wait(); });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Exception failure = null;
        bool inactive = false;
        var waiter = new Thread(() =>
        {
            try { inactive = executor.AwaitInactivity(Timeout.InfiniteTimeSpan); }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true };
        waiter.Start();
        try
        {
            Assert.False(waiter.Join(TimeSpan.FromMilliseconds(50)));
            if (interrupt)
            {
                waiter.Interrupt();
                Assert.True(waiter.Join(TimeSpan.FromSeconds(5)));
            }
        }
        finally
        {
            release.Set();
            Assert.True(waiter.Join(TimeSpan.FromSeconds(5)));
            Assert.True(executor.AwaitInactivity(TimeSpan.FromSeconds(5)));
        }
        if (interrupt) Assert.IsType<ThreadInterruptedException>(failure);
        else { Assert.Null(failure); Assert.True(inactive); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeTerminationObserversUseGlobalExecutorAfterTheOwnedWorkerStops(bool unordered)
    {
        IEventExecutor executor = unordered ? new UnorderedThreadPoolEventExecutor(1) : new DefaultEventExecutor();
        try
        {
            await executor.SubmitAsync(() => { }).WaitAsync(TimeSpan.FromSeconds(5));
            // The pinned termination promises bind their listeners to GlobalEventExecutor,
            // which remains available after the observed executor has stopped accepting work.
            using var observation = new ExecutorCompletion(GlobalEventExecutor.INSTANCE, executor.Termination);
            bool earlyAffinity = false, lateAffinity = false, ranOnOwnedWorker = false;
            Task observedEarly = null, observedLate = null;
            using var early = observation.Register(task =>
            {
                earlyAffinity = GlobalEventExecutor.INSTANCE.InEventLoop();
                ranOnOwnedWorker = executor.InEventLoop();
                observedEarly = task;
            });
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(await Task.Run(() => executor.AwaitTermination(TimeSpan.FromSeconds(5))));
            await early.NotificationCompleted.WaitAsync(TimeSpan.FromSeconds(5));
            using var late = observation.Register(task =>
            {
                lateAffinity = GlobalEventExecutor.INSTANCE.InEventLoop();
                observedLate = task;
            });
            await late.NotificationCompleted.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(earlyAffinity);
            Assert.True(lateAffinity);
            Assert.False(ranOnOwnedWorker);
            Assert.Same(executor.Termination, observedEarly);
            Assert.Same(executor.Termination, observedLate);
        }
        finally { await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5)); }
    }
}
