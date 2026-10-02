using System;
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
    private sealed class Listener : IGenericFutureListener<IFuture<Void>>
    {
        private readonly Action<IFuture<Void>> action;
        internal Listener(Action<IFuture<Void>> action) => this.action = action;
        public void operationComplete(IFuture<Void> future) => action(future);
    }

    private sealed class Child : AbstractScheduledEventExecutor
    {
        internal readonly IPromise<Void> termination = new DefaultPromise<Void>(ImmediateEventExecutor.INSTANCE);
        internal readonly Queue<IRunnable> tasks = new();
        internal bool queued;
        internal int executions;
        internal int rejectExecution = -1;
        internal bool shuttingDown;
        internal bool stopped;
        internal bool completeOnShutdown;
        internal TimeSpan quietPeriod;
        internal TimeSpan timeout;
        internal readonly List<TimeSpan> waits = new();
        private bool running;
        internal Child(IEventExecutorGroup parent = null) : base(parent) { }
        internal IScheduledTask firstScheduled() => peekScheduledTask();
        public override bool inEventLoop(Thread thread) => running && thread == Thread.CurrentThread;
        public override void execute(IRunnable command)
        {
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
        public override IFuture<Void> terminationFuture() => termination;
        public override IFuture<Void> shutdownGracefully(TimeSpan quietPeriod, TimeSpan timeout)
        {
            shuttingDown = true;
            this.quietPeriod = quietPeriod;
            this.timeout = timeout;
            if (completeOnShutdown) termination.trySuccess(null);
            return termination;
        }
        public override void shutdown() { shuttingDown = stopped = true; }
        public override bool isShuttingDown() => shuttingDown;
        public override bool isShutdown() => stopped;
        public override bool isTerminated() => termination.isDone();
        public override bool awaitTermination(TimeSpan timeout)
        {
            waits.Add(timeout);
            return termination.isDone();
        }
    }

    private sealed class SingleChildGroup : AbstractEventExecutorGroup
    {
        internal readonly Child child;
        internal int selections;
        internal SingleChildGroup(Child child) => this.child = child;
        public override IEventExecutor next() { ++selections; return child; }
        public override IEnumerable<IEventExecutor> iterator() => new[] { child };
        public override IFuture<Void> shutdownGracefully(TimeSpan quietPeriod, TimeSpan timeout) => child.shutdownGracefully(quietPeriod, timeout);
        public override IFuture<Void> terminationFuture() => child.terminationFuture();
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

    [Fact]
    public void ShutdownReturnsThePersistentTerminationFutureAndTaskView()
    {
        var executor = new DefaultEventExecutor();
        var termination = executor.terminationFuture();
        Assert.False(termination.isDone());
        Assert.Same(termination.Task, executor.terminationTask());
        Assert.Same(termination, executor.shutdownGracefully(TimeSpan.Zero, TimeSpan.Zero));
        Assert.Null(termination.get(TimeSpan.FromSeconds(5)));
        Assert.True(termination.isSuccess());
        Assert.Same(termination, executor.shutdownGracefully());
        Assert.Same(termination.Task, executor.shutdownGracefullyAsync());
        Assert.True(executor.isTerminated());
    }

    [Fact]
    public void InvalidShutdownArgumentsDoNotStartShutdown()
    {
        var executor = new DefaultEventExecutor();
        try
        {
            Assert.Throws<ArgumentException>(() => executor.shutdownGracefully(TimeSpan.FromTicks(-1), TimeSpan.Zero));
            Assert.Throws<ArgumentException>(() => executor.shutdownGracefully(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)));
            Assert.False(executor.isShuttingDown());
            Assert.False(executor.terminationFuture().isDone());
        }
        finally { executor.shutdownGracefully(TimeSpan.Zero, TimeSpan.Zero).get(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public void WorkerStartFailureRetainsCauseAndNotifiesTerminationListenerOnGlobalExecutor()
    {
        var backing = new RejectingExecutor();
        var executor = new DefaultEventExecutor(backing);
        using var notified = new CountdownEvent(1);
        IFuture<Void> observed = null;
        bool global = false;
        executor.terminationFuture().addListener(new Listener(f =>
        {
            observed = f;
            global = GlobalEventExecutor.INSTANCE.inEventLoop();
            notified.Signal();
        }));
        var future = executor.shutdownGracefully(TimeSpan.Zero, TimeSpan.Zero);
        Assert.True(notified.Wait(TimeSpan.FromSeconds(5)));
        Assert.Same(future, observed);
        Assert.True(global);
        Assert.Same(backing.failure, future.cause());
        Assert.Same(backing.failure, Assert.Throws<AggregateException>(() => future.get()).InnerException);
        Assert.Same(backing.failure, future.Task.Exception.InnerException);
        Assert.True(executor.isTerminated());
    }

    [Fact]
    public void NonTerminableExecutorsExposeTheirPersistentFailedFuture()
    {
        foreach (var executor in new IEventExecutor[] { GlobalEventExecutor.INSTANCE, ImmediateEventExecutor.INSTANCE })
        {
            var future = executor.terminationFuture();
            Assert.True(future.isDone());
            Assert.False(future.isSuccess());
            Assert.IsAssignableFrom<NotSupportedException>(future.cause());
            Assert.Same(future, executor.shutdownGracefully());
            Assert.Same(future, executor.shutdownGracefully(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(-2)));
            Assert.Same(future.Task, executor.terminationTask());
            Assert.False(executor.isShuttingDown());
            Assert.False(executor.isShutdown());
            Assert.False(executor.isTerminated());
        }
    }

    [Fact]
    public void MultithreadGroupCompletesOnlyAfterEveryChildEvenWhenOneFails()
    {
        var first = new Child();
        var second = new Child();
        var group = new ManualGroup(first, second);
        using var notified = new CountdownEvent(1);
        bool global = false;
        var termination = group.terminationFuture();
        termination.addListener(new Listener(_ => { global = GlobalEventExecutor.INSTANCE.inEventLoop(); notified.Signal(); }));
        Assert.Same(termination, group.shutdownGracefully(TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(1)));
        Assert.True(group.isShuttingDown());
        Assert.False(group.isShutdown());
        Assert.Equal(TimeSpan.FromMilliseconds(20), first.quietPeriod);
        Assert.Equal(TimeSpan.FromSeconds(1), second.timeout);
        first.termination.setFailure(new InvalidOperationException("child failure"));
        Assert.False(termination.isDone());
        second.termination.setSuccess(null);
        Assert.Null(termination.get(TimeSpan.FromSeconds(5)));
        Assert.True(notified.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(global);
        Assert.True(termination.isSuccess());
        Assert.True(group.isTerminated());
        Assert.Same(termination.Task, group.terminationTask());
    }

    [Fact]
    public void ChildEnumerationCannotMutateGroupAndShutdownNowUsesInheritedBehavior()
    {
        var children = new[] { new Child(), new Child(), new Child() };
        var group = new ManualGroup(children);
        Assert.False(group.iterator() is ICollection<IEventExecutor>);
        Assert.Equal(children, group.iterator());
        Assert.Equal(3, group.executorCount());
        for (int i = 0; i < 12; ++i) Assert.Same(children[i % 3], group.next());
        Assert.Empty(group.shutdownNow());
        Assert.True(group.isShutdown());
        foreach (var child in children) child.termination.setSuccess(null);
        group.terminationFuture().get(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GroupLargeTimeoutDoesNotBecomeNegativeDuringNanosecondConversion()
    {
        var child = new Child();
        child.termination.setSuccess(null);
        var group = new ManualGroup(child);
        Assert.True(group.awaitTermination(TimeSpan.MaxValue));
        Assert.Single(child.waits);
        Assert.True(child.waits[0] > TimeSpan.FromDays(1000));
        Assert.True(group.awaitTermination(TimeSpan.Zero));
        Assert.Single(child.waits);
    }

    [Fact]
    public void GroupExposesObservableMetricsAndDefaultCounts()
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
        foreach (var child in children) child.termination.setSuccess(null);
        ordinary.terminationFuture().get(TimeSpan.FromSeconds(5));
        observable.terminationFuture().get(TimeSpan.FromSeconds(5));
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
    public void BaseGroupSelectsExactlyOneExecutorPerSubmissionAndBulkCall()
    {
        var group = new SingleChildGroup(new Child());
        int executed = 0;
        group.execute(Runnables.Create(() => ++executed));
        Assert.Null(group.submit(Runnables.Create(() => ++executed)).get());
        Assert.Equal(42, group.submit(Runnables.Empty, 42).get());
        Assert.Equal(7, group.submit(new AnonymousCallable<int>(() => 7)).get());
        var tasks = new ICallable<int>[] { new AnonymousCallable<int>(() => 1), new AnonymousCallable<int>(() => 2) };
        Assert.Equal(new[] { 1, 2 }, group.invokeAll(tasks).Select(f => f.get()));
        Assert.Equal(new[] { 1, 2 }, group.invokeAll(tasks, TimeSpan.FromSeconds(1)).Select(f => f.get()));
        Assert.Equal(1, group.invokeAny(tasks));
        Assert.Equal(1, group.invokeAny(tasks, TimeSpan.FromSeconds(1)));
        Assert.Equal(8, group.selections);
        Assert.Equal(2, executed);
        Assert.Same(Ticker.systemTicker(), group.ticker());
        Assert.Same(group.terminationFuture(), group.shutdownGracefully());
        Assert.Equal(TimeSpan.FromSeconds(2), group.child.quietPeriod);
        Assert.Equal(TimeSpan.FromSeconds(15), group.child.timeout);
    }

    [Fact]
    public void DefaultGroupCreatesParentedExecutorsAndCanUseShutdownNow()
    {
        var group = new DefaultEventExecutorGroup(2, new DefaultThreadFactory("group-contract", true),
            16, RejectedExecutionHandlers.reject());
        try
        {
            var children = group.iterator().ToArray();
            Assert.Equal(2, children.Length);
            Assert.All(children, child => { Assert.IsType<DefaultEventExecutor>(child); Assert.Same(group, child.parent()); });
            var first = group.submit(new AnonymousCallable<Thread>(() => Thread.CurrentThread));
            var second = group.submit(new AnonymousCallable<Thread>(() => Thread.CurrentThread));
            Assert.NotSame(first.get(TimeSpan.FromSeconds(5)), second.get(TimeSpan.FromSeconds(5)));
            Assert.Empty(group.shutdownNow());
            Assert.True(group.awaitTermination(TimeSpan.FromSeconds(5)));
            Assert.Null(group.terminationFuture().get(TimeSpan.FromSeconds(5)));
        }
        finally { group.shutdownGracefully(TimeSpan.Zero, TimeSpan.Zero).get(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public void BaseGroupForwardsEveryScheduleFormToTheChosenChild()
    {
        var child = new Child();
        var group = new SingleChildGroup(child);
        var runnable = group.schedule(Runnables.Empty, TimeSpan.FromDays(1));
        var callable = group.schedule(new AnonymousCallable<int>(() => 123), TimeSpan.FromDays(2));
        var rate = group.scheduleAtFixedRate(Runnables.Empty, TimeSpan.FromDays(3), TimeSpan.FromDays(1));
        var delay = group.scheduleWithFixedDelay(Runnables.Empty, TimeSpan.FromDays(4), TimeSpan.FromDays(1));
        Assert.Equal(4, group.selections);
        Assert.Same(runnable, child.firstScheduled());
        Assert.Equal(new long[] { 1, 2, 3, 4 }, new[] { runnable.getId(), callable.getId(), rate.getId(), delay.getId() });
        Assert.True(runnable.deadlineNanos() < callable.deadlineNanos());
        Assert.True(callable.deadlineNanos() < rate.deadlineNanos());
        Assert.True(rate.deadlineNanos() < delay.deadlineNanos());
        foreach (var future in new IScheduledTask[] { runnable, callable, rate, delay }) Assert.True(future.cancel(false));
    }

    [Fact]
    public void NonStickyGroupForwardsDirectCallsAndCreatesIndependentOrderedWrappers()
    {
        var child = new Child { queued = true };
        var underlying = new SingleChildGroup(child);
        var group = new NonStickyEventExecutorGroup(underlying, 1);
        Assert.Same(child.termination, group.terminationFuture());
        Assert.Same(child.termination, group.shutdownGracefully());
        Assert.Same(child.termination.Task, group.terminationTask());
        var direct = group.submit(Runnables.Empty);
        Assert.IsType<PromiseTask<Void>>(child.tasks.Peek());
        child.run(child.tasks.Dequeue());
        Assert.Null(direct.get());
        var first = group.next();
        var second = group.next();
        Assert.NotSame(first, second);
        Assert.IsAssignableFrom<IOrderedEventExecutor>(first);
        Assert.Same(child, first.parent());
        Assert.Same(child.termination, first.terminationFuture());
        Assert.Same(child.termination, first.shutdownGracefully(TimeSpan.Zero, TimeSpan.Zero));
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
        Assert.False(executor.newPromise<int>().await(TimeSpan.FromMilliseconds(1)));
        Assert.False(executor.newProgressivePromise<int>().await(TimeSpan.FromMilliseconds(1)));
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
}
