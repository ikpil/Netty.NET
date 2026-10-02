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
        internal readonly Queue<IRunnable> tasks = new();
        internal bool queued;
        internal int executions;
        internal IScheduledWork scheduledSubmission;
        internal int rejectExecution = -1;
        internal bool shuttingDown;
        internal bool stopped;
        internal bool completeOnShutdown;
        internal TimeSpan quietPeriod;
        internal TimeSpan timeout;
        internal readonly List<TimeSpan> waits = new();
        private bool running;
        internal Child(IEventExecutorGroup parent = null) : base(parent) { }
        internal IScheduledWork firstScheduled() => peekScheduledTask();
        public override bool inEventLoop(Thread thread) => running && thread == Thread.CurrentThread;
        public override void execute(IRunnable command)
        {
            if (command is IScheduledWork scheduled) scheduledSubmission = scheduled;
            if (++executions == rejectExecution) throw new RejectedExecutionException("Simulated queue full");
            if (queued) tasks.Enqueue(command);
            else run(command);
        }
        internal void run(IRunnable command)
        {
            running = true;
            try { command.run(); }
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
        public override void shutdown() { shuttingDown = stopped = true; }
        public override bool isShuttingDown() => shuttingDown;
        public override bool isShutdown() => stopped;
        public override bool isTerminated() => termination.Task.IsCompleted;
        public override bool awaitTermination(TimeSpan timeout)
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
        public override IEventExecutor next() { ++selections; return child; }
        public override IEnumerable<IEventExecutor> iterator() => new[] { child };
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => child.ShutdownGracefullyAsync(quietPeriod, timeout);
        public override Task Termination => child.Termination;
        public override void shutdown() => child.shutdown();
        public override bool isShuttingDown() => child.isShuttingDown();
        public override bool isShutdown() => child.isShutdown();
        public override bool isTerminated() => child.isTerminated();
        public override bool awaitTermination(TimeSpan timeout) => child.awaitTermination(timeout);
    }

    private sealed class ManualGroup : MultithreadEventExecutorGroup
    {
        internal ManualGroup(params Child[] children) : this(DefaultEventExecutorChooserFactory.INSTANCE, children) { }
        internal ManualGroup(IEventExecutorChooserFactory factory, params Child[] children)
            : base(children.Length, ImmediateExecutor.INSTANCE, factory, (object)children) { }
        protected override IEventExecutor newChild(IExecutor executor, params object[] args) =>
            ((Child[])args[0])[iteratorIndex++];
        private int iteratorIndex;
    }

    private sealed class ObservableChooser : IEventExecutorChooserFactory, IObservableEventExecutorChooser
    {
        internal IEventExecutor[] children;
        internal IReadOnlyList<AutoScalingUtilizationMetric> metrics;
        public IEventExecutorChooser newChooser(IEventExecutor[] executors)
        {
            children = executors;
            metrics = Array.AsReadOnly(executors.Select(executor => new AutoScalingUtilizationMetric(executor)).ToArray());
            return this;
        }
        public IEventExecutor next() => children[0];
        public int activeExecutorCount() => 1;
        public IReadOnlyList<AutoScalingUtilizationMetric> executorUtilizations() => metrics;
    }

    private sealed class FailingGroup : MultithreadEventExecutorGroup
    {
        internal FailingGroup(List<Child> created, Exception failure)
            : base(3, ImmediateExecutor.INSTANCE, created, failure) { }
        protected override IEventExecutor newChild(IExecutor executor, params object[] args)
        {
            var created = (List<Child>)args[0];
            if (created.Count == 2) throw (Exception)args[1];
            var child = new Child(this) { completeOnShutdown = true };
            created.Add(child);
            return child;
        }
    }

    private sealed class RejectingExecutor : IExecutor
    {
        internal readonly RejectedExecutionException failure = new("worker rejected");
        public void execute(IRunnable command) => throw failure;
    }

    private sealed class FailingWorker : SingleThreadEventExecutor
    {
        private readonly Exception failure;
        internal FailingWorker(Exception failure)
            : base(null, new DefaultThreadFactory("lifecycle-failure", true), true) => this.failure = failure;
        protected override void run() => throw failure;
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
        Assert.True(executor.isTerminated());
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
            Assert.True(executor.isShuttingDown());
            Assert.Same(termination, executor.Termination);
            release.Set();
            await work.WaitAsync(TimeSpan.FromSeconds(5));
            await ordinaryWait;
            Assert.True(termination.IsCompletedSuccessfully);
            Assert.True(executor.isTerminated());
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
            Assert.False(executor.isShuttingDown());
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
                Assert.True(executor.inEventLoop());
                Task termination = executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
                Assert.False(termination.IsCompleted);
                // Yielding releases the physical event-loop thread so it can finish shutdown.
                await termination.ConfigureAwait(false);
                Assert.False(executor.inEventLoop());
                Assert.True(executor.isTerminated());
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
        Task continuation = executor.Termination.ContinueWith(_ => Assert.False(executor.inEventLoop()),
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
        Assert.True(group.isTerminated());
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
        Assert.True(group.isTerminated());
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
            Assert.False(executor.isShuttingDown());
            Assert.False(executor.Termination.IsCompleted);
        }
        finally { await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task WorkerStartFailureRetainsCauseAndAllowsExplicitExecutorNotification()
    {
        var backing = new RejectingExecutor();
        var executor = new DefaultEventExecutor(backing);
        Task termination = executor.Termination;
        Task failure = executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
        Assert.Same(termination, failure);
        Assert.Same(backing.failure, await Assert.ThrowsAsync<RejectedExecutionException>(() => failure));
        Assert.Same(backing.failure, failure.Exception.InnerException);
        // Native Task continuations do not choose a Netty executor. Consumers
        // that require affinity dispatch explicitly after observing completion.
        await GlobalEventExecutor.INSTANCE.SubmitAsync(() =>
        {
            Assert.True(GlobalEventExecutor.INSTANCE.inEventLoop());
            Assert.Same(termination, executor.Termination);
        }).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(executor.isTerminated());
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
            ? new DefaultEventExecutor(new AnonymousExecutor(_ => throw failure))
            : new FailingWorker(failure);
        Task termination = executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
        Assert.Same(failure, await Assert.ThrowsAsync<OperationCanceledException>(() =>
            termination.WaitAsync(TimeSpan.FromSeconds(5))));
        Assert.True(termination.IsFaulted);
        Assert.False(termination.IsCanceled);
        Assert.Same(failure, termination.Exception.InnerException);
        Assert.Same(termination, executor.Termination);
        Assert.True(executor.isTerminated());
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
            Assert.False(executor.isShuttingDown());
            Assert.False(executor.isShutdown());
            Assert.False(executor.isTerminated());
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
        Assert.True(group.isShuttingDown());
        Assert.False(group.isShutdown());
        Assert.Equal(TimeSpan.FromMilliseconds(20), first.quietPeriod);
        Assert.Equal(TimeSpan.FromSeconds(1), second.timeout);
        first.termination.SetException(new InvalidOperationException("child failure"));
        Assert.False(termination.IsCompleted);
        second.termination.SetResult();
        await termination.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(termination.IsCompletedSuccessfully);
        Assert.True(group.isTerminated());
        Assert.Same(termination, group.Termination);
    }

    [Fact]
    public async Task ChildEnumerationCannotMutateGroupAndShutdownNowUsesInheritedBehavior()
    {
        var children = new[] { new Child(), new Child(), new Child() };
        var group = new ManualGroup(children);
        Assert.False(group.iterator() is ICollection<IEventExecutor>);
        Assert.Equal(children, group.iterator());
        Assert.Equal(3, group.executorCount());
        for (int i = 0; i < 12; ++i) Assert.Same(children[i % 3], group.next());
        Assert.Empty(group.shutdownNow());
        Assert.True(group.isShutdown());
        foreach (var child in children) child.termination.SetResult();
        await group.Termination.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GroupLargeTimeoutDoesNotBecomeNegativeDuringNanosecondConversion()
    {
        var child = new Child();
        child.termination.SetResult();
        var group = new ManualGroup(child);
        Assert.True(group.awaitTermination(TimeSpan.MaxValue));
        Assert.Single(child.waits);
        Assert.True(child.waits[0] > TimeSpan.FromDays(1000));
        Assert.True(group.awaitTermination(TimeSpan.Zero));
        Assert.Single(child.waits);
    }

    [Fact]
    public async Task GroupExposesObservableMetricsAndDefaultCounts()
    {
        var children = new[] { new Child(), new Child(), new Child() };
        var ordinary = new ManualGroup(children);
        Assert.Equal(3, ordinary.activeExecutorCount());
        Assert.Empty(ordinary.executorUtilizations());
        var chooser = new ObservableChooser();
        var observable = new ManualGroup(chooser, children);
        Assert.Equal(1, observable.activeExecutorCount());
        Assert.Same(chooser.metrics, observable.executorUtilizations());
        Assert.Same(children[0], observable.next());
        var metric = observable.executorUtilizations()[0];
        Assert.Same(children[0], metric.executor());
        Assert.Equal(0.0, metric.utilization());
        metric.setUtilization(0.75);
        Assert.Equal(0.75, observable.executorUtilizations()[0].utilization());
        long payload = unchecked((long)0x7ff8000000001234UL);
        metric.setUtilization(BitConverter.Int64BitsToDouble(payload));
        Assert.Equal(payload, BitConverter.DoubleToInt64Bits(metric.utilization()));
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
            Assert.True(child.isShuttingDown());
            Assert.True(child.isTerminated());
            Assert.Equal(TimeSpan.FromSeconds(2), child.quietPeriod);
            Assert.Equal(TimeSpan.FromSeconds(15), child.timeout);
        });
    }

    [Fact]
    public void BaseGroupSelectsOneExecutorPerSubmissionAndExplicitBatchSelection()
    {
        var group = new SingleChildGroup(new Child());
        int executed = 0;
        group.execute(Runnables.Create(() => ++executed));
        group.SubmitAsync(() => { ++executed; }).GetAwaiter().GetResult();
        Assert.Equal(42, group.SubmitAsync(() => 42).GetAwaiter().GetResult());
        Assert.Equal(7, group.SubmitAsync(() => 7).GetAwaiter().GetResult());
        IEventExecutor selected = group.next();
        Task<int>[] operations = { selected.SubmitAsync(() => 1), selected.SubmitAsync(() => 2) };
        Assert.Equal(new[] { 1, 2 }, Task.WhenAll(operations).GetAwaiter().GetResult());
        Assert.Equal(5, group.selections);
        Assert.Equal(2, executed);
        Assert.Same(Ticker.systemTicker(), group.ticker());
        Assert.Same(group.Termination, group.ShutdownGracefullyAsync());
        Assert.Equal(TimeSpan.FromSeconds(2), group.child.quietPeriod);
        Assert.Equal(TimeSpan.FromSeconds(15), group.child.timeout);
    }

    [Fact]
    public async Task DefaultGroupCreatesParentedExecutorsAndCanUseShutdownNow()
    {
        var group = new DefaultEventExecutorGroup(2, new DefaultThreadFactory("group-contract", true),
            16, RejectedExecutionHandlers.reject());
        try
        {
            var children = group.iterator().ToArray();
            Assert.Equal(2, children.Length);
            Assert.All(children, child => { Assert.IsType<DefaultEventExecutor>(child); Assert.Same(group, child.parent()); });
            var first = group.SubmitAsync<Thread>(() => Thread.CurrentThread);
            var second = group.SubmitAsync<Thread>(() => Thread.CurrentThread);
            Assert.NotSame(first.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(), second.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.Empty(group.shutdownNow());
            Assert.True(group.awaitTermination(TimeSpan.FromSeconds(5)));
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
        Assert.Same(firstWork, child.firstScheduled());
        Assert.False((object)firstWork is System.Threading.Tasks.Task);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, new[] { firstWork.getId(), secondWork.getId(), rateWork.getId(), delayWork.getId() });
        Assert.True(firstWork.deadlineNanos() < secondWork.deadlineNanos());
        Assert.True(secondWork.deadlineNanos() < rateWork.deadlineNanos());
        Assert.True(rateWork.deadlineNanos() < delayWork.deadlineNanos());
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
        Assert.IsAssignableFrom<INativeSubmission>(child.tasks.Peek());
        child.run(child.tasks.Dequeue());
        direct.GetAwaiter().GetResult();
        var first = group.next();
        var second = group.next();
        Assert.NotSame(first, second);
        Assert.IsAssignableFrom<IOrderedEventExecutor>(first);
        Assert.Same(child, first.parent());
        Assert.Same(child.termination.Task, first.Termination);
        Assert.Same(child.termination.Task, first.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero));
        Assert.IsAssignableFrom<IOrderedEventExecutor>(Assert.Single(group.iterator()));
    }

    [Fact]
    public void NonStickyRunnerReturnsAfterReschedulingAndDoesNotExecuteTheNextBatch()
    {
        var child = new Child { queued = true };
        var executor = new NonStickyEventExecutorGroup(new SingleChildGroup(child), 1).next();
        var values = new List<int>();
        for (int i = 0; i < 3; ++i)
        {
            int value = i;
            executor.execute(Runnables.Create(() => { Assert.True(executor.inEventLoop()); values.Add(value); }));
        }
        Assert.Single(child.tasks);
        for (int i = 0; i < 3; ++i)
        {
            child.run(child.tasks.Dequeue());
            Assert.Equal(Enumerable.Range(0, i + 1), values);
            Assert.False(executor.inEventLoop());
            Assert.Single(child.tasks);
        }
        child.run(child.tasks.Dequeue());
        Assert.Empty(child.tasks);
        Assert.False(executor.inEventLoop());
    }

    [Fact]
    public void NonStickyReschedulingFailureRetainsExecutorThreadUntilRetry()
    {
        var child = new Child { queued = true, rejectExecution = 2 };
        var executor = new NonStickyEventExecutorGroup(new SingleChildGroup(child), 1).next();
        int count = 0;
        executor.execute(Runnables.Create(() => ++count));
        executor.execute(Runnables.Create(() => { Assert.True(executor.inEventLoop()); ++count; }));
        child.run(child.tasks.Dequeue());
        Assert.Equal(2, count);
        Assert.Equal(3, child.executions);
        Assert.False(executor.inEventLoop());
        child.run(child.tasks.Dequeue());
        Assert.Empty(child.tasks);
    }

    [Fact]
    public void ImmediateExecutorDrainsReentrantTasksInOrderAfterAnException()
    {
        var executor = ImmediateEventExecutor.INSTANCE;
        var order = new List<int>();
        executor.execute(Runnables.Create(() =>
        {
            order.Add(1);
            executor.execute(Runnables.Create(() => { order.Add(3); throw new InvalidOperationException("queued"); }));
            executor.execute(Runnables.Create(() => order.Add(4)));
            order.Add(2);
            throw new InvalidOperationException("outer");
        }));
        executor.execute(Runnables.Create(() => order.Add(5)));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, order);
        Assert.True(executor.inEventLoop());
        Assert.Same(executor, executor.next());
        Assert.Same(executor, Assert.Single(executor.iterator()));
        Assert.Null(executor.parent());
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
    public void GlobalInactivityUsesUnboundedJoinForZeroWholeMilliseconds(long ticks)
    {
        var executor = GlobalEventExecutor.INSTANCE;
        using var entered = new CountdownEvent(1);
        using var release = new CountdownEvent(1);
        executor.execute(Runnables.Create(() => { entered.Signal(); release.Wait(); }));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Exception failure = null;
        bool inactive = false;
        var waiter = new Thread(() =>
        {
            try { inactive = executor.awaitInactivity(TimeSpan.FromTicks(ticks)); }
            catch (Exception e) { failure = e; }
        }) { IsBackground = true };
        waiter.Start();
        try { Assert.False(waiter.Join(TimeSpan.FromMilliseconds(50))); }
        finally { release.Signal(); }
        Assert.True(waiter.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
        Assert.True(inactive);
    }

    [Fact]
    public void GlobalLargeInactivityWaitRemainsInterruptible()
    {
        var executor = GlobalEventExecutor.INSTANCE;
        using var entered = new CountdownEvent(1);
        using var release = new CountdownEvent(1);
        executor.execute(Runnables.Create(() => { entered.Signal(); release.Wait(); }));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Exception failure = null;
        var waiter = new Thread(() =>
        {
            try { executor.awaitInactivity(TimeSpan.MaxValue); }
            catch (Exception e) { failure = e; }
        }) { IsBackground = true };
        waiter.Start();
        try
        {
            Assert.False(waiter.Join(TimeSpan.FromMilliseconds(50)));
            waiter.Interrupt();
            Assert.True(waiter.Join(TimeSpan.FromSeconds(5)));
            Assert.IsType<ThreadInterruptedException>(failure);
            Assert.Throws<ArgumentOutOfRangeException>(() => executor.awaitInactivity(TimeSpan.FromMilliseconds(-1)));
        }
        finally { release.Signal(); }
        Assert.True(executor.awaitInactivity(TimeSpan.FromSeconds(5)));
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
                earlyAffinity = GlobalEventExecutor.INSTANCE.inEventLoop();
                ranOnOwnedWorker = executor.inEventLoop();
                observedEarly = task;
            });
            await executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(await Task.Run(() => executor.awaitTermination(TimeSpan.FromSeconds(5))));
            await early.NotificationCompleted.WaitAsync(TimeSpan.FromSeconds(5));
            using var late = observation.Register(task =>
            {
                lateAffinity = GlobalEventExecutor.INSTANCE.inEventLoop();
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
