using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Collections;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

public class SchedulingContractTest
{
    [Fact]
    public void GlobalExecutorStopsWhenIdleAndRestartsForANewTask()
    {
        var executor = GlobalEventExecutor.INSTANCE;
        using var first = new CountdownEvent(1);
        executor.execute(Runnables.Create(() => first.Signal()));
        Assert.True(first.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(executor.awaitInactivity(TimeSpan.FromSeconds(5)));
        using var second = new CountdownEvent(1);
        executor.execute(Runnables.Create(() => second.Signal()));
        Assert.True(second.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(executor.isShuttingDown());
        Assert.False(executor.isShutdown());
    }
    [Fact]
    public void OneShotCallableCompletesWithValueOnTheExecutor()
    {
        var executor = new DefaultEventExecutor();
        try
        {
            bool ranOnExecutor = false;
            IScheduledTask<object> task = executor.schedule(new AnonymousCallable<object>(() =>
            {
                ranOnExecutor = executor.inEventLoop();
                return "result";
            }), TimeSpan.FromMilliseconds(15));
            Assert.Equal("result", task.Completion.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.True(ranOnExecutor);
            Assert.False(task.cancel());
            Assert.Equal(0, task.delayNanos());
        }
        finally { shutdown(executor); }
    }
    [Fact]
    public void FailureRetainsTheCallableException()
    {
        var executor = new DefaultEventExecutor();
        try
        {
            var cause = new InvalidOperationException("scheduled failure");
            var task = executor.schedule(new AnonymousCallable<object>(() => throw cause), TimeSpan.Zero);
            var error = Assert.Throws<InvalidOperationException>(() => task.Completion.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.Same(cause, error);
        }
        finally { shutdown(executor); }
    }
    [Fact]
    public void CancellingBeforeDeadlinePreventsExecution()
    {
        var executor = new DefaultEventExecutor();
        try
        {
            int calls = 0;
            var task = executor.schedule(Runnables.Create(() => ++calls), TimeSpan.FromSeconds(10));
            Assert.True(task.cancel());
            Assert.True(task.isCancelled());
            using var drained = new CountdownEvent(1);
            executor.execute(Runnables.Create(() => drained.Signal()));
            Assert.True(drained.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, calls);
        }
        finally { shutdown(executor); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PeriodicTasksRunRepeatedlyAndCancellationStopsThem(bool fixedRate)
    {
        var executor = new DefaultEventExecutor();
        try
        {
            int calls = 0;
            using var repeated = new CountdownEvent(1);
            var action = Runnables.Create(() => { if (Interlocked.Increment(ref calls) == 3) repeated.Signal(); });
            var task = fixedRate ? executor.scheduleAtFixedRate(action, TimeSpan.Zero, TimeSpan.FromMilliseconds(2)) :
                executor.scheduleWithFixedDelay(action, TimeSpan.Zero, TimeSpan.FromMilliseconds(2));
            Assert.True(repeated.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(task.cancel());
            using var drained = new CountdownEvent(1);
            executor.execute(Runnables.Create(() => drained.Signal()));
            Assert.True(drained.Wait(TimeSpan.FromSeconds(5)));
            int completedCalls = Volatile.Read(ref calls);
            Thread.Sleep(20);
            Assert.Equal(completedCalls, Volatile.Read(ref calls));
            Assert.True(task.isCancelled());
        }
        finally { shutdown(executor); }
    }
    [Fact]
    public void DifferentGenericResultTypesCanShareTheDeadlineQueue()
    {
        var executor = new DefaultEventExecutor();
        try
        {
            var first = executor.schedule(new AnonymousCallable<string>(() => "text"), TimeSpan.FromMilliseconds(10));
            var second = executor.schedule(new AnonymousCallable<int>(() => 42), TimeSpan.FromMilliseconds(10));
            Assert.Equal("text", first.Completion.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.Equal(42, second.Completion.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
        }
        finally { shutdown(executor); }
    }
    private class ManualExecutor : AbstractScheduledEventExecutor
    {
        private readonly MockTicker clock = Ticker.newMockTicker();
        public ManualExecutor() : base(null) { }
        public override Ticker ticker() => clock;
        public void advance(long nanos) => clock.advance(nanos);
        public IRunnable pollDue() => pollScheduledTask(getCurrentTimeNanos());
        public bool transferDue(IQueue<IRunnable> queue) => fetchFromScheduledTaskQueue(queue);
        public override bool inEventLoop(Thread thread) => true;
        public override void execute(IRunnable task) => task.run();
        public override IFuture<Netty.NET.Common.Concurrent.Void> shutdownGracefully(TimeSpan quietPeriod, TimeSpan timeout) => ImmediateEventExecutor.INSTANCE.newSucceededFuture<Netty.NET.Common.Concurrent.Void>(null);
        public override IFuture<Netty.NET.Common.Concurrent.Void> terminationFuture() => ImmediateEventExecutor.INSTANCE.newSucceededFuture<Netty.NET.Common.Concurrent.Void>(null);
        public override bool isShuttingDown() => false;
        public override bool isShutdown() => false;
        public override bool isTerminated() => false;
        public override bool awaitTermination(TimeSpan timeout) => false;
        public override void shutdown() { }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationTokenRemainsRegisteredWhileWaitingForTheDeadline(bool cancel)
    {
        var executor = new ManualExecutor();
        using var token = new CancellationTokenSource();
        int calls = 0;
        var task = new ScheduledActionAsyncTask(executor, () => ++calls, 100, token.Token);
        task.run();
        Assert.False(task.isDone());
        if (cancel)
        {
            token.Cancel();
            Assert.True(task.isCancelled());
        }
        executor.advance(100);
        executor.pollDue()?.run();
        Assert.Equal(cancel ? 0 : 1, calls);
        Assert.True(task.isDone());
        if (!cancel) Assert.True(task.Completion.IsCompletedSuccessfully);
    }
    private static void shutdown(IEventExecutor executor) =>
        Assert.True(executor.shutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).Wait(TimeSpan.FromSeconds(5)));

    [Fact]
    public void SaturatingNanosecondConversionPreservesLargeDeadlines()
    {
        var executor = new ManualExecutor();
        var task = executor.schedule(Runnables.Empty, TimeSpan.MaxValue);
        Assert.Equal(long.MaxValue, task.deadlineNanos());
        Assert.Null(executor.pollDue());
        Assert.Equal(long.MaxValue, AbstractScheduledEventExecutor.toNanos(TimeSpan.MaxValue));
        Assert.Equal(long.MinValue, AbstractScheduledEventExecutor.toNanos(TimeSpan.MinValue));
        Assert.Equal(9007199254740900L, AbstractScheduledEventExecutor.toNanos(TimeSpan.FromTicks(90071992547409L)));
        var periodic = executor.scheduleAtFixedRate(Runnables.Empty, TimeSpan.Zero, TimeSpan.MaxValue);
        Assert.False(periodic.isDone());
    }

    [Fact]
    public void CancelledScheduledTasksDoNotConsumeTransferQueueCapacity()
    {
        var executor = new ManualExecutor();
        var cancelled = executor.schedule(Runnables.Empty, TimeSpan.FromTicks(1));
        var ready = executor.schedule(Runnables.Empty, TimeSpan.FromTicks(1));
        Assert.True(cancelled.cancelWithoutRemove(false));
        executor.advance(100);
        var queue = new LinkedBlockingQueue<IRunnable>(1);
        Assert.True(executor.transferDue(queue));
        Assert.Equal(1, queue.Count);
        Assert.True(queue.tryDequeue(out var task));
        Assert.Same(ready, task);
        Assert.Null(executor.pollDue());
    }

    [Fact]
    public void FullTransferQueueReturnsTheTaskToTheDeadlineQueue()
    {
        var executor = new ManualExecutor();
        var ready = executor.schedule(Runnables.Empty, TimeSpan.FromTicks(1));
        executor.advance(100);
        var queue = new LinkedBlockingQueue<IRunnable>(1);
        Assert.True(queue.tryEnqueue(Runnables.Empty));
        Assert.False(executor.transferDue(queue));
        Assert.Same(ready, executor.pollDue());
    }

    [Fact]
    public void PeriodicReinsertionDoesNotConsumeANewTaskId()
    {
        var executor = new ManualExecutor();
        var periodic = executor.scheduleAtFixedRate(Runnables.Empty, TimeSpan.Zero, TimeSpan.FromTicks(1));
        long id = periodic.getId();
        executor.pollDue().run();
        Assert.Equal(id, periodic.getId());
        var next = executor.schedule(Runnables.Empty, TimeSpan.FromTicks(1));
        Assert.Equal(id + 1, next.getId());
        executor.advance(100);
        Assert.Same(periodic, executor.pollDue());
        Assert.Same(next, executor.pollDue());
    }

    private sealed class HookExecutor : ManualExecutor
    {
        internal bool before;
        internal bool after;
        internal int afterCalls;
        internal int lazyCalls;
        internal readonly List<IRunnable> submissions = new();
        public override bool inEventLoop(Thread thread) => false;
        public override void execute(IRunnable command) => submissions.Add(command);
        public override void lazyExecute(IRunnable command) { ++lazyCalls; submissions.Add(command); }
        protected override bool beforeScheduledTaskSubmitted(long deadline) => before;
        protected override bool afterScheduledTaskSubmitted(long deadline) { ++afterCalls; return after; }
        protected override void validateScheduled(TimeSpan amount)
        {
            if (amount > TimeSpan.FromTicks(1)) throw new ArgumentException("test scheduling limit");
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void SchedulingHooksSelectImmediateOrLazySubmissionAndOptionalWakeup(bool before, bool after)
    {
        var executor = new HookExecutor { before = before, after = after };
        var task = executor.schedule(Runnables.Empty, TimeSpan.FromTicks(1));
        Assert.Same(task, executor.submissions[0]);
        Assert.Equal(before ? 0 : 1, executor.lazyCalls);
        Assert.Equal(before ? 0 : 1, executor.afterCalls);
        Assert.Equal(!before && after ? 2 : 1, executor.submissions.Count);
        if (!before && after) Assert.NotSame(task, executor.submissions[1]);
    }

    [Fact]
    public void SubclassSchedulingValidationRunsBeforeAnyTaskIsSubmitted()
    {
        var executor = new HookExecutor();
        Assert.Throws<ArgumentException>(() => executor.schedule(Runnables.Empty, TimeSpan.FromTicks(2)));
        Assert.Throws<ArgumentException>(() => executor.schedule(new AnonymousCallable<int>(() => 1), TimeSpan.FromTicks(2)));
        Assert.Throws<ArgumentException>(() => executor.scheduleAtFixedRate(Runnables.Empty, TimeSpan.Zero, TimeSpan.FromTicks(2)));
        Assert.Throws<ArgumentException>(() => executor.scheduleWithFixedDelay(Runnables.Empty, TimeSpan.Zero, TimeSpan.FromTicks(2)));
        Assert.Empty(executor.submissions);
    }
}
