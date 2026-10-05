using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Concurrent;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

public class SingleThreadMetricsContractTest
{
    private sealed class ActivityClockExecutor : SingleThreadEventExecutor
    {
        internal static readonly MockTicker clock = CreateClock();
        private static MockTicker CreateClock()
        {
            MockTicker clock = global::Netty.NET.Common.Concurrent.Ticker.NewMockTicker();
            clock.Advance(1000);
            return clock;
        }
        internal ActivityClockExecutor()
            : base(null, _ => throw new Exception("must not start"), false, true,
                int.MaxValue, RejectedExecutionHandlers.Reject()) { }
        internal ActivityClockExecutor(IQueue<Action> queue)
            : base(null, _ => throw new Exception("must not start"), false, true,
                queue, RejectedExecutionHandlers.Reject()) { }
        public override Ticker Ticker() => clock;
        protected override void Run() => throw new Exception("must not run");
        internal long LastActivity() => GetLastActivityTimeNanos();
        internal void UpdateActivity() => UpdateLastExecutionTime();
    }

    [Fact]
    public void AllocatedQueueConstructorInitializesActivityAndExplicitUpdateRefreshesIt()
    {
        long now = ActivityClockExecutor.clock.NanoTime();
        var executor = new ActivityClockExecutor();
        Assert.Equal(now, executor.LastActivity());
        ActivityClockExecutor.clock.Advance(123);
        executor.UpdateActivity();
        Assert.Equal(now + 123, executor.LastActivity());
        // The pinned constructor accepting an explicit queue does not initialize this field.
        var explicitQueue = new ActivityClockExecutor(new LinkedBlockingQueue<Action>(int.MaxValue));
        Assert.Equal(0, explicitQueue.LastActivity());
        explicitQueue.UpdateActivity();
        Assert.Equal(now + 123, explicitQueue.LastActivity());
    }

    private sealed class ManualExecutor : SingleThreadEventExecutor
    {
        internal readonly MockTicker clock = global::Netty.NET.Common.Concurrent.Ticker.NewMockTicker();
        internal ManualExecutor(bool support = false)
            : base(null, _ => throw new Exception("must not start"), false, support,
                int.MaxValue, RejectedExecutionHandlers.Reject()) { }
        public override bool InEventLoop(Thread thread) => thread == Thread.CurrentThread;
        public override Ticker Ticker() => clock;
        protected override void Run() => throw new Exception("must not run");
        internal bool Drain(long nanos) => RunAllTasks(nanos);
        internal long ResetActive() => GetAndResetAccumulatedActiveTimeNanos();
        internal long LastActivity() => GetLastActivityTimeNanos();
        internal void Io(long nanos) => ReportActiveIoTime(nanos);
        internal int Idle() => GetAndIncrementIdleCycles();
        internal int Busy() => GetAndIncrementBusyCycles();
        internal void ResetIdle() => ResetIdleCycles();
        internal void ResetBusy() => ResetBusyCycles();
        internal int Channels() => GetNumOfRegisteredChannels();
        internal bool SupportsSuspension() => IsSuspensionSupported();
    }

    [Fact]
    public void TimedDrainAccountsWorkAndChecksTheBudgetEvery64Tasks()
    {
        var executor = new ManualExecutor();
        int calls = 0;
        for (int i = 0; i < 100; i++) executor.Execute(() => { ++calls; executor.clock.Advance(5); });
        Assert.True(executor.Drain(0));
        Assert.Equal(64, calls);
        Assert.Equal(320, executor.ResetActive());
        Assert.Equal(320, executor.LastActivity());
        Assert.Equal(0, executor.ResetActive());
        Assert.True(executor.Drain(long.MaxValue));
        Assert.Equal(100, calls);
        Assert.Equal(180, executor.ResetActive());
        Assert.Equal(500, executor.LastActivity());
        Assert.False(executor.Drain(0));
        Assert.Equal(0, executor.ResetActive());
        Assert.Equal(500, executor.LastActivity());
    }

    [Theory]
    [InlineData(0L, 64)]
    [InlineData(-1L, 64)]
    [InlineData(long.MaxValue, 100)]
    [InlineData(long.MaxValue - 320, 100)]
    public void TimedDrainUsesTheElapsedBudgetWithoutOverflowingALargeDeadline(long budget, int expectedCalls)
    {
        var executor = new ManualExecutor();
        executor.clock.Advance(320);
        int calls = 0;
        for (int i = 0; i < 100; i++)
            executor.Execute(() => { calls++; executor.clock.Advance(5); });
        Assert.True(executor.Drain(budget));
        Assert.Equal(expectedCalls, calls);
        Assert.Equal(expectedCalls * 5, executor.ResetActive());
        Assert.Equal(320 + expectedCalls * 5, executor.LastActivity());
    }

    [Fact]
    public void PositiveIoReportsUpdateActiveTimeAndIgnoreNonpositiveDurations()
    {
        var executor = new ManualExecutor(true);
        executor.clock.Advance(100);
        executor.Io(12);
        Assert.Equal(12, executor.ResetActive());
        Assert.Equal(100, executor.LastActivity());
        executor.clock.Advance(100);
        executor.Io(0);
        executor.Io(-1);
        Assert.Equal(0, executor.ResetActive());
        Assert.Equal(100, executor.LastActivity());
        executor.Io(10);
        executor.Io(20);
        Assert.Equal(30, executor.ResetActive());
        Assert.Equal(200, executor.LastActivity());
        Assert.Equal(-1, executor.Channels());
        Assert.True(executor.SupportsSuspension());
        Assert.False(new ManualExecutor().SupportsSuspension());
    }

    [Fact]
    public void MonitorCycleCountersIncrementAtomicallyAndResetIndependently()
    {
        var executor = new ManualExecutor();
        var idle = new ConcurrentBag<int>();
        var busy = new ConcurrentBag<int>();
        Parallel.For(0, 256, _ => { idle.Add(executor.Idle()); busy.Add(executor.Busy()); });
        Assert.Equal(Enumerable.Range(0, 256), idle.Order());
        Assert.Equal(Enumerable.Range(0, 256), busy.Order());
        executor.ResetIdle();
        Assert.Equal(0, executor.Idle());
        Assert.Equal(256, executor.Busy());
        executor.ResetBusy();
        Assert.Equal(0, executor.Busy());
        Assert.Equal(1, executor.Idle());
    }

    [Fact]
    public void AThrowingTaskDoesNotPreventDrainOrLoseItsMeasuredWork()
    {
        var executor = new ManualExecutor();
        int calls = 0;
        executor.Execute(() => { executor.clock.Advance(12); throw new InvalidOperationException(); });
        executor.Execute(() => { executor.clock.Advance(5); ++calls; });
        Assert.True(executor.Drain(long.MaxValue));
        Assert.Equal(1, calls);
        Assert.Equal(17, executor.ResetActive());
        Assert.Equal(17, executor.LastActivity());
    }

    private sealed class CountingStartExecutor : SingleThreadEventExecutor
    {
        internal CountingStartExecutor(Action<Action> executor)
            : base(null, executor, false, true, int.MaxValue, RejectedExecutionHandlers.Reject()) { }
        protected override void Run() => throw new Exception("must not run");
        public override bool InEventLoop(Thread thread) => false;
        internal int Idle() => GetAndIncrementIdleCycles();
        internal int Busy() => GetAndIncrementBusyCycles();
    }

    [Fact]
    public void StartingANeverStartedSuspendedExecutorResetsBothMonitorStreaks()
    {
        int starts = 0;
        var executor = new CountingStartExecutor(_ => ++starts);
        Assert.True(executor.TrySuspend());
        Assert.True(executor.IsSuspended());
        Assert.Equal(0, executor.Idle());
        Assert.Equal(0, executor.Busy());
        executor.Execute(static () => { });
        Assert.Equal(1, starts);
        Assert.False(executor.IsSuspended());
        Assert.Equal(0, executor.Idle());
        Assert.Equal(0, executor.Busy());
    }

    [Fact]
    public void ThreadPropertiesRemainReadableAfterTheNativeThreadStops()
    {
        var thread = new Thread(() => { }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "properties" };
        var properties = new DefaultThreadProperties(thread);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.False(properties.IsAlive());
        Assert.Equal(ThreadPriority.BelowNormal, properties.Priority());
        Assert.True(properties.IsDaemon());
        Assert.Equal("properties", properties.Name());
        Assert.Equal(thread.ManagedThreadId, properties.Id());
        Assert.Throws<NotSupportedException>(() => properties.IsInterrupted());
    }
}
