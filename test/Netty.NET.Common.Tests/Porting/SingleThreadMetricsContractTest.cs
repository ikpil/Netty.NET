using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

public class SingleThreadMetricsContractTest
{
    private sealed class ActivityClockExecutor : SingleThreadEventExecutor
    {
        internal static readonly MockTicker clock = createClock();
        private static MockTicker createClock()
        {
            MockTicker clock = Ticker.newMockTicker();
            clock.advance(1000);
            return clock;
        }
        internal ActivityClockExecutor()
            : base(null, new AnonymousExecutor(_ => throw new Exception("must not start")), false, true,
                int.MaxValue, RejectedExecutionHandlers.reject()) { }
        internal ActivityClockExecutor(IQueue<IRunnable> queue)
            : base(null, new AnonymousExecutor(_ => throw new Exception("must not start")), false, true,
                queue, RejectedExecutionHandlers.reject()) { }
        public override Ticker ticker() => clock;
        protected override void run() => throw new Exception("must not run");
        internal long lastActivity() => getLastActivityTimeNanos();
        internal void updateActivity() => updateLastExecutionTime();
    }

    [Fact]
    public void AllocatedQueueConstructorInitializesActivityAndExplicitUpdateRefreshesIt()
    {
        long now = ActivityClockExecutor.clock.nanoTime();
        var executor = new ActivityClockExecutor();
        Assert.Equal(now, executor.lastActivity());
        ActivityClockExecutor.clock.advance(123);
        executor.updateActivity();
        Assert.Equal(now + 123, executor.lastActivity());
        // The pinned constructor accepting an explicit queue does not initialize this field.
        var explicitQueue = new ActivityClockExecutor(new LinkedBlockingQueue<IRunnable>(int.MaxValue));
        Assert.Equal(0, explicitQueue.lastActivity());
        explicitQueue.updateActivity();
        Assert.Equal(now + 123, explicitQueue.lastActivity());
    }

    private sealed class ManualExecutor : SingleThreadEventExecutor
    {
        internal readonly MockTicker clock = Ticker.newMockTicker();
        internal ManualExecutor(bool support = false)
            : base(null, new AnonymousExecutor(_ => throw new Exception("must not start")), false, support,
                int.MaxValue, RejectedExecutionHandlers.reject()) { }
        public override bool inEventLoop(Thread thread) => thread == Thread.CurrentThread;
        public override Ticker ticker() => clock;
        protected override void run() => throw new Exception("must not run");
        internal bool drain(long nanos) => runAllTasks(nanos);
        internal long resetActive() => getAndResetAccumulatedActiveTimeNanos();
        internal long lastActivity() => getLastActivityTimeNanos();
        internal void io(long nanos) => reportActiveIoTime(nanos);
        internal int idle() => getAndIncrementIdleCycles();
        internal int busy() => getAndIncrementBusyCycles();
        internal void resetIdle() => resetIdleCycles();
        internal void resetBusy() => resetBusyCycles();
        internal int channels() => getNumOfRegisteredChannels();
        internal bool supportsSuspension() => isSuspensionSupported();
    }

    [Fact]
    public void TimedDrainAccountsWorkAndChecksTheBudgetEvery64Tasks()
    {
        var executor = new ManualExecutor();
        int calls = 0;
        for (int i = 0; i < 100; i++) executor.execute(Runnables.Create(() => { ++calls; executor.clock.advance(5); }));
        Assert.True(executor.drain(0));
        Assert.Equal(64, calls);
        Assert.Equal(320, executor.resetActive());
        Assert.Equal(320, executor.lastActivity());
        Assert.Equal(0, executor.resetActive());
        Assert.True(executor.drain(long.MaxValue));
        Assert.Equal(100, calls);
        Assert.Equal(180, executor.resetActive());
        Assert.Equal(500, executor.lastActivity());
        Assert.False(executor.drain(0));
        Assert.Equal(0, executor.resetActive());
        Assert.Equal(500, executor.lastActivity());
    }

    [Fact]
    public void PositiveIoReportsUpdateActiveTimeAndIgnoreNonpositiveDurations()
    {
        var executor = new ManualExecutor(true);
        executor.clock.advance(100);
        executor.io(12);
        Assert.Equal(12, executor.resetActive());
        Assert.Equal(100, executor.lastActivity());
        executor.clock.advance(100);
        executor.io(0);
        executor.io(-1);
        Assert.Equal(0, executor.resetActive());
        Assert.Equal(100, executor.lastActivity());
        executor.io(10);
        executor.io(20);
        Assert.Equal(30, executor.resetActive());
        Assert.Equal(200, executor.lastActivity());
        Assert.Equal(-1, executor.channels());
        Assert.True(executor.supportsSuspension());
        Assert.False(new ManualExecutor().supportsSuspension());
    }

    [Fact]
    public void MonitorCycleCountersIncrementAtomicallyAndResetIndependently()
    {
        var executor = new ManualExecutor();
        var idle = new ConcurrentBag<int>();
        var busy = new ConcurrentBag<int>();
        Parallel.For(0, 256, _ => { idle.Add(executor.idle()); busy.Add(executor.busy()); });
        Assert.Equal(Enumerable.Range(0, 256), idle.Order());
        Assert.Equal(Enumerable.Range(0, 256), busy.Order());
        executor.resetIdle();
        Assert.Equal(0, executor.idle());
        Assert.Equal(256, executor.busy());
        executor.resetBusy();
        Assert.Equal(0, executor.busy());
        Assert.Equal(1, executor.idle());
    }

    [Fact]
    public void AThrowingTaskDoesNotPreventDrainOrLoseItsMeasuredWork()
    {
        var executor = new ManualExecutor();
        int calls = 0;
        executor.execute(Runnables.Create(() => { executor.clock.advance(12); throw new InvalidOperationException(); }));
        executor.execute(Runnables.Create(() => { executor.clock.advance(5); ++calls; }));
        Assert.True(executor.drain(long.MaxValue));
        Assert.Equal(1, calls);
        Assert.Equal(17, executor.resetActive());
        Assert.Equal(17, executor.lastActivity());
    }

    private sealed class CountingStartExecutor : SingleThreadEventExecutor
    {
        internal CountingStartExecutor(IExecutor executor)
            : base(null, executor, false, true, int.MaxValue, RejectedExecutionHandlers.reject()) { }
        protected override void run() => throw new Exception("must not run");
        public override bool inEventLoop(Thread thread) => false;
        internal int idle() => getAndIncrementIdleCycles();
        internal int busy() => getAndIncrementBusyCycles();
    }

    [Fact]
    public void StartingANeverStartedSuspendedExecutorResetsBothMonitorStreaks()
    {
        int starts = 0;
        var executor = new CountingStartExecutor(new AnonymousExecutor(_ => ++starts));
        Assert.True(executor.trySuspend());
        Assert.True(executor.isSuspended());
        Assert.Equal(0, executor.idle());
        Assert.Equal(0, executor.busy());
        executor.execute(Runnables.Empty);
        Assert.Equal(1, starts);
        Assert.False(executor.isSuspended());
        Assert.Equal(0, executor.idle());
        Assert.Equal(0, executor.busy());
    }

    [Fact]
    public void ThreadPropertiesRemainReadableAfterTheNativeThreadStops()
    {
        var thread = new Thread(() => { }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "properties" };
        var properties = new DefaultThreadProperties(thread);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.False(properties.isAlive());
        Assert.Equal(ThreadPriority.BelowNormal, properties.priority());
        Assert.True(properties.isDaemon());
        Assert.Equal("properties", properties.name());
        Assert.Equal(thread.ManagedThreadId, properties.id());
        Assert.Throws<NotSupportedException>(() => properties.isInterrupted());
    }
}
