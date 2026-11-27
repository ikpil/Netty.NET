using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Porting;

public class NativeLazySchedulingContractTest
{
    private sealed class ManualLoop : SingleThreadEventExecutor
    {
        internal ManualLoop() : base(null, _ => throw new Exception("unexpected worker start"), true) { }
        public override bool InEventLoop(Thread thread)
        {
            return true;
        }
        protected override void Run()
        {
            throw new Exception("unexpected worker loop");
        }
        internal void EnqueueWakeup()
        {
            AddTask(WAKEUP_TASK);
        }
        internal Action CopyWakeup()
        {
            return (Action)WAKEUP_TASK.Clone();
        }
        internal void Enqueue(Action task)
        {
            AddTask(task);
        }
        internal Action Poll()
        {
            return PollTask();
        }
        internal Action Take()
        {
            return TakeTask();
        }
        internal bool Drain()
        {
            return RunAllTasks();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleThreadQueuesConsumeTheSharedWakeupButRetainCopiedNativeActions(bool blocking)
    {
        var executor = new ManualLoop();
        Action copy = executor.CopyWakeup();
        int calls = 0;
        Action command = () => calls++;
        executor.EnqueueWakeup();
        executor.Enqueue(copy);
        executor.Enqueue(command);
        if (blocking) Assert.Null(executor.Take());
        Assert.Same(copy, blocking ? executor.Take() : executor.Poll());
        Assert.Same(command, blocking ? executor.Take() : executor.Poll());
        command();
        Assert.Equal(1, calls);
        executor.EnqueueWakeup();
        Assert.False(executor.Drain());
    }

    private sealed class ManualExecutor(bool before, bool after) : AbstractScheduledEventExecutor
    {
        private readonly MockTicker _clock = global::Netty.NET.Common.Concurrent.Ticker.NewMockTicker();
        private bool _inLoop;
        internal readonly List<Action> Ready = new();
        internal readonly List<string> Hooks = new();
        internal IScheduledWork Head => PeekScheduledTask();
        public override Ticker Ticker()
        {
            return _clock;
        }
        public override bool InEventLoop(Thread thread)
        {
            return _inLoop;
        }
        public override void Execute(Action task) { Hooks.Add("execute"); Ready.Add(task); }
        public override void LazyExecute(Action task) { Hooks.Add("lazy"); Ready.Add(task); }
        protected override bool BeforeScheduledTaskSubmitted(long deadline) { Hooks.Add("before:" + deadline); return before; }
        protected override bool AfterScheduledTaskSubmitted(long deadline) { Hooks.Add("after:" + deadline); return after; }
        internal void Drain()
        {
            _inLoop = true;
            try { foreach (Action task in Ready) task(); Ready.Clear(); }
            finally { _inLoop = false; }
        }
        internal void RunDue()
        {
            _inLoop = true;
            try
            {
                _clock.Advance(TimeSpan.FromTicks(1));
                PollScheduledTask().Invoke();
            }
            finally { _inLoop = false; }
        }
        public override Task TerminationFuture => Task.CompletedTask;
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
        {
            return TerminationFuture;
        }
        [Obsolete]
        public override void Shutdown()
        {
            CancelScheduledTasks();
        }
        public override bool IsShuttingDown()
        {
            return false;
        }
        public override bool IsShutdown()
        {
            return false;
        }
        public override bool IsTerminated()
        {
            return false;
        }
        public override bool AwaitTermination(TimeSpan timeout)
        {
            return false;
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task NativeSubmissionHooksKeepCancellationAndContextAcrossTheWakeupRace(bool before, bool after)
    {
        var executor = new ManualExecutor(before, after);
        var ambient = new AsyncLocal<string>();
        using var cancellation = new CancellationTokenSource();
        int canceledRuns = 0;
        ambient.Value = "producer";
        Task<string> first = executor.ScheduleAsync(() => ambient.Value, TimeSpan.FromTicks(1), TestContext.Current.CancellationToken);
        Task canceled = executor.ScheduleAsync(() => { canceledRuns++; }, TimeSpan.FromTicks(1), cancellation.Token);
        bool wakeup = !before && after;
        string[] expected = before ? ["before:100", "execute"]
            : wakeup ? ["before:100", "lazy", "after:100", "execute"]
            : ["before:100", "lazy", "after:100"];
        var hooks = new List<string>(expected);
        hooks.AddRange(expected);
        Assert.Equal(hooks, executor.Hooks);
        Assert.Equal(wakeup ? 4 : 2, executor.Ready.Count);
        if (wakeup) Assert.Same(executor.Ready[1], executor.Ready[3]);
        Assert.False(first.IsCompleted);
        Assert.Null(executor.Head);

        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceled);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal("lazy", executor.Hooks[^1]);
        ambient.Value = "executor";
        executor.Drain();
        Assert.Empty(executor.Ready);
        Assert.Equal(100, executor.Head.DeadlineNanos());
        Assert.False(first.IsCompleted);
        executor.RunDue();
        Assert.Equal("producer", await first);
        Assert.Equal("executor", ambient.Value);
        Assert.Equal(0, canceledRuns);
        Assert.Null(executor.Head);
    }
}
