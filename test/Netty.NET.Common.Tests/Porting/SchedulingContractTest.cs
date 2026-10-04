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
        executor.Execute(Runnables.Create(() => first.Signal()));
        Assert.True(first.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(executor.AwaitInactivity(TimeSpan.FromSeconds(5)));
        using var second = new CountdownEvent(1);
        executor.Execute(Runnables.Create(() => second.Signal()));
        Assert.True(second.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(executor.IsShuttingDown());
        Assert.False(executor.IsShutdown());
    }
    [Fact]
    public void OneShotCallableCompletesWithValueOnTheExecutor()
    {
        var executor = new DefaultEventExecutor();
        try
        {
            bool ranOnExecutor = false;
            Task<object> task = executor.ScheduleAsync<object>(() =>
            {
                ranOnExecutor = executor.InEventLoop();
                return "result";
            }, TimeSpan.FromMilliseconds(15));
            Assert.Equal("result", task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.True(ranOnExecutor);
            Assert.True(task.IsCompletedSuccessfully);
        }
        finally { Shutdown(executor); }
    }
    [Fact]
    public void FailureRetainsTheCallableException()
    {
        var executor = new DefaultEventExecutor();
        try
        {
            var cause = new InvalidOperationException("scheduled failure");
            var task = executor.ScheduleAsync(object () => throw cause, TimeSpan.Zero);
            var error = Assert.Throws<InvalidOperationException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.Same(cause, error);
        }
        finally { Shutdown(executor); }
    }
    [Fact]
    public void CancellingBeforeDeadlinePreventsExecution()
    {
        var executor = new DefaultEventExecutor();
        try
        {
            int calls = 0;
            using var cancellation = new CancellationTokenSource();
            var task = executor.ScheduleAsync(() => { ++calls; }, TimeSpan.FromSeconds(10), cancellation.Token);
            cancellation.Cancel();
            Assert.True(task.IsCanceled);
            using var drained = new CountdownEvent(1);
            executor.Execute(Runnables.Create(() => drained.Signal()));
            Assert.True(drained.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, calls);
        }
        finally { Shutdown(executor); }
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
            using var cancellation = new CancellationTokenSource();
            Action action = () => { if (Interlocked.Increment(ref calls) == 3) repeated.Signal(); };
            var task = fixedRate ? executor.ScheduleAtFixedRateAsync(action, TimeSpan.Zero, TimeSpan.FromMilliseconds(2), cancellation.Token) :
                executor.ScheduleWithFixedDelayAsync(action, TimeSpan.Zero, TimeSpan.FromMilliseconds(2), cancellation.Token);
            Assert.True(repeated.Wait(TimeSpan.FromSeconds(5)));
            cancellation.Cancel();
            using var drained = new CountdownEvent(1);
            executor.Execute(Runnables.Create(() => drained.Signal()));
            Assert.True(drained.Wait(TimeSpan.FromSeconds(5)));
            int completedCalls = Volatile.Read(ref calls);
            Thread.Sleep(20);
            Assert.Equal(completedCalls, Volatile.Read(ref calls));
            Assert.True(task.IsCanceled);
        }
        finally { Shutdown(executor); }
    }
    [Fact]
    public void DifferentGenericResultTypesCanShareTheDeadlineQueue()
    {
        var executor = new DefaultEventExecutor();
        try
        {
            var first = executor.ScheduleAsync(() => "text", TimeSpan.FromMilliseconds(10));
            var second = executor.ScheduleAsync(() => 42, TimeSpan.FromMilliseconds(10));
            Assert.Equal("text", first.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            Assert.Equal(42, second.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
        }
        finally { Shutdown(executor); }
    }
    private class ManualExecutor : AbstractScheduledEventExecutor
    {
        private readonly MockTicker clock = global::Netty.NET.Common.Concurrent.Ticker.NewMockTicker();
        public ManualExecutor() : base(null) { }
        public override Ticker Ticker() => clock;
        public void Advance(long nanos) => clock.Advance(nanos);
        internal IScheduledWork Head => PeekScheduledTask();
        internal bool holdRemoval;
        internal readonly List<IRunnable> removals = new();
        protected override void ScheduleRemoveScheduled(IScheduledWork task)
        {
            if (holdRemoval) removals.Add(task);
            else base.ScheduleRemoveScheduled(task);
        }
        public IRunnable PollDue() => PollScheduledTask(GetCurrentTimeNanos());
        public bool TransferDue(IQueue<IRunnable> queue) => FetchFromScheduledTaskQueue(queue);
        public override bool InEventLoop(Thread thread) => !holdRemoval;
        public override void Execute(Action task)
        {
            IRunnable queuedTask = ExecutorWork.Unwrap(task, nameof(task));
            queuedTask.Run();
        }
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => Task.CompletedTask;
        public override Task Termination => Task.CompletedTask;
        public override bool IsShuttingDown() => false;
        public override bool IsShutdown() => false;
        public override bool IsTerminated() => false;
        public override bool AwaitTermination(TimeSpan timeout) => false;
        public override void Shutdown() { }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationTokenRemainsRegisteredWhileWaitingForTheDeadline(bool cancel)
    {
        var executor = new ManualExecutor();
        using var token = new CancellationTokenSource();
        int calls = 0;
        Task task = executor.ScheduleAsync(() => { ++calls; }, TimeSpan.FromTicks(1), token.Token);
        Assert.False(task.IsCompleted);
        if (cancel)
        {
            token.Cancel();
            Assert.True(task.IsCanceled);
        }
        executor.Advance(100);
        executor.PollDue()?.Run();
        Assert.Equal(cancel ? 0 : 1, calls);
        Assert.True(task.IsCompleted);
        if (!cancel) Assert.True(task.IsCompletedSuccessfully);
    }
    private static void Shutdown(IEventExecutor executor) =>
        Assert.True(executor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).Wait(TimeSpan.FromSeconds(5)));

    [Fact]
    public void SaturatingNanosecondConversionPreservesLargeDeadlines()
    {
        var executor = new ManualExecutor();
        var task = executor.ScheduleAsync(() => { }, TimeSpan.MaxValue);
        Assert.Equal(long.MaxValue, executor.Head.DeadlineNanos());
        Assert.Null(executor.PollDue());
        Assert.Equal(long.MaxValue, AbstractScheduledEventExecutor.ToNanos(TimeSpan.MaxValue));
        Assert.Equal(long.MinValue, AbstractScheduledEventExecutor.ToNanos(TimeSpan.MinValue));
        Assert.Equal(9007199254740900L, AbstractScheduledEventExecutor.ToNanos(TimeSpan.FromTicks(90071992547409L)));
        var periodic = executor.ScheduleAtFixedRateAsync(() => { }, TimeSpan.Zero, TimeSpan.MaxValue);
        Assert.False(periodic.IsCompleted);
    }

    [Fact]
    public void CancelledScheduledTasksDoNotConsumeTransferQueueCapacity()
    {
        var executor = new ManualExecutor();
        using var cancellation = new CancellationTokenSource();
        var cancelled = executor.ScheduleAsync(() => { }, TimeSpan.FromTicks(1), cancellation.Token);
        var canceledWork = executor.Head;
        var ready = executor.ScheduleAsync(() => { }, TimeSpan.FromTicks(1));
        executor.holdRemoval = true;
        cancellation.Cancel();
        Assert.True(cancelled.IsCanceled);
        Assert.Single(executor.removals);
        executor.holdRemoval = false;
        executor.Advance(100);
        var queue = new LinkedBlockingQueue<IRunnable>(1);
        Assert.True(executor.TransferDue(queue));
        Assert.Equal(1, queue.Count);
        Assert.True(queue.TryDequeue(out var task));
        Assert.NotSame(canceledWork, task);
        task.Run();
        Assert.True(ready.IsCompletedSuccessfully);
        Assert.Null(executor.PollDue());
    }

    [Fact]
    public void FullTransferQueueReturnsTheTaskToTheDeadlineQueue()
    {
        var executor = new ManualExecutor();
        var ready = executor.ScheduleAsync(() => { }, TimeSpan.FromTicks(1));
        var readyWork = executor.Head;
        executor.Advance(100);
        var queue = new LinkedBlockingQueue<IRunnable>(1);
        Assert.True(queue.TryEnqueue(Runnables.Empty));
        Assert.False(executor.TransferDue(queue));
        Assert.Same(readyWork, executor.PollDue());
    }

    [Fact]
    public void PeriodicReinsertionDoesNotConsumeANewTaskId()
    {
        var executor = new ManualExecutor();
        using var cancellation = new CancellationTokenSource();
        var periodic = executor.ScheduleAtFixedRateAsync(() => { }, TimeSpan.Zero, TimeSpan.FromTicks(1), cancellation.Token);
        var periodicWork = executor.Head;
        long id = periodicWork.GetId();
        executor.PollDue().Run();
        Assert.Equal(id, periodicWork.GetId());
        var next = executor.ScheduleAsync(() => { }, TimeSpan.FromTicks(1));
        executor.Advance(100);
        Assert.Same(periodicWork, executor.PollDue());
        var nextWork = Assert.IsAssignableFrom<IScheduledWork>(executor.PollDue());
        Assert.Equal(id + 1, nextWork.GetId());
        nextWork.Run();
        Assert.True(next.IsCompletedSuccessfully);
        cancellation.Cancel();
        Assert.True(periodic.IsCanceled);
    }

    private sealed class HookExecutor : ManualExecutor
    {
        internal bool before;
        internal bool after;
        internal int afterCalls;
        internal int lazyCalls;
        internal readonly List<IRunnable> submissions = new();
        public override bool InEventLoop(Thread thread) => false;
        public override void Execute(Action command)
        {
            IRunnable queuedTask = ExecutorWork.Unwrap(command, nameof(command));
            submissions.Add(queuedTask);
        }
        public override void LazyExecute(Action command)
        {
            IRunnable queuedTask = ExecutorWork.Unwrap(command, nameof(command));
            ++lazyCalls;
            submissions.Add(queuedTask);
        }
        protected override bool BeforeScheduledTaskSubmitted(long deadline) => before;
        protected override bool AfterScheduledTaskSubmitted(long deadline) { ++afterCalls; return after; }
        protected override void ValidateScheduled(TimeSpan amount)
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
        var task = executor.ScheduleAsync(() => { }, TimeSpan.FromTicks(1));
        var work = Assert.IsAssignableFrom<IScheduledWork>(executor.submissions[0]);
        Assert.False((object)work is System.Threading.Tasks.Task);
        Assert.False(task.IsCompleted);
        Assert.Equal(before ? 0 : 1, executor.lazyCalls);
        Assert.Equal(before ? 0 : 1, executor.afterCalls);
        Assert.Equal(!before && after ? 2 : 1, executor.submissions.Count);
        if (!before && after) Assert.NotSame(work, executor.submissions[1]);
    }

    [Fact]
    public void SubclassSchedulingValidationRunsBeforeAnyTaskIsSubmitted()
    {
        var executor = new HookExecutor();
        Assert.Throws<ArgumentException>(() => executor.ScheduleAsync(() => { }, TimeSpan.FromTicks(2)));
        Assert.Throws<ArgumentException>(() => executor.ScheduleAsync(() => 1, TimeSpan.FromTicks(2)));
        Assert.Throws<ArgumentException>(() => executor.ScheduleAtFixedRateAsync(() => { }, TimeSpan.Zero, TimeSpan.FromTicks(2)));
        Assert.Throws<ArgumentException>(() => executor.ScheduleWithFixedDelayAsync(() => { }, TimeSpan.Zero, TimeSpan.FromTicks(2)));
        Assert.Empty(executor.submissions);
    }
}
