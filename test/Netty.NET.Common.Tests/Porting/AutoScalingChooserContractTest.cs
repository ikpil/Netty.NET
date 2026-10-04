using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

[Collection("Global executor")]
public class AutoScalingChooserContractTest
{
    private const long Period = 3_600_000_000_000L;

    private sealed class AccountingExecutor : SingleThreadEventExecutor
    {
        private readonly MockTicker clock = global::Netty.NET.Common.Concurrent.Ticker.NewMockTicker();
        private Thread reportingThread;
        internal AccountingExecutor()
            : base(null, _ => throw new Exception("must not start"), true, true,
                int.MaxValue, RejectedExecutionHandlers.Reject()) { }
        public override Ticker Ticker() => clock ?? global::Netty.NET.Common.Concurrent.Ticker.SystemTicker();
        public override bool InEventLoop(Thread thread) => thread != null && thread == Volatile.Read(ref reportingThread);
        protected override void Run() => throw new Exception("must not run");
        internal void Report(long nanos)
        {
            Volatile.Write(ref reportingThread, Thread.CurrentThread);
            ReportActiveIoTime(nanos);
        }
        internal void ReportTask(long nanos)
        {
            Volatile.Write(ref reportingThread, Thread.CurrentThread);
            AddTask(Runnables.Create(() => clock.Advance(nanos)));
            RunAllTasks(1_000_000L);
        }
        internal long Sample() => GetAndResetAccumulatedActiveTimeNanos();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcurrentUtilizationSamplingPreservesTheReportedActiveTimeBudget(bool taskTime)
    {
        // One event-loop writer reports work while a separate monitor consumes windows.
        // The independently known total must be preserved across every reset boundary.
        for (int epoch = 0; epoch < 3; ++epoch)
        {
            var executor = new AccountingExecutor();
            using var start = new Barrier(2);
            const int reports = 200_000;
            int done = 0;
            Exception reportingFailure = null;
            var reporter = new Thread(() =>
            {
                try
                {
                    start.SignalAndWait();
                    for (int i = 0; i < reports; ++i)
                        if (taskTime) executor.ReportTask(1); else executor.Report(1);
                }
                catch (Exception error) { reportingFailure = error; }
                finally { Volatile.Write(ref done, 1); }
            }) { IsBackground = true };
            reporter.Start();
            start.SignalAndWait();
            long sampled = 0;
            long deadline = Environment.TickCount64 + 5_000;
            while (Volatile.Read(ref done) == 0 && Environment.TickCount64 < deadline) sampled += executor.Sample();
            Assert.True(reporter.Join(TimeSpan.FromSeconds(5)));
            Assert.Null(reportingFailure);
            sampled += executor.Sample();
            Assert.Equal(reports, sampled);
        }
    }

    // Invoke the private monitor with an independent mock clock. Its normal scheduled
    // instance remains dormant for an hour and is canceled by the real lifecycle listener.
    private sealed class ManualExecutor : SingleThreadEventExecutor
    {
        private readonly MockTicker clock;
        internal readonly TaskCompletionSource termination = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool suspended;
        internal bool shuttingDown;
        internal bool suspendAllowed = true;
        internal long activeTime;
        internal long lastActivity;
        internal int channels = -1;
        internal int wakes;
        internal int suspensionAttempts;
        internal int metricReads;

        internal ManualExecutor(MockTicker clock)
            : base(null, _ => throw new Exception("must not start"), true, true,
                int.MaxValue, RejectedExecutionHandlers.Reject()) => this.clock = clock;
        public override Ticker Ticker() => clock ?? global::Netty.NET.Common.Concurrent.Ticker.SystemTicker();
        public override bool IsSuspended() => suspended;
        public override bool IsShuttingDown() => shuttingDown;
        public override Task Termination => termination.Task;
        public override bool TrySuspend()
        {
            ++suspensionAttempts;
            if (!suspendAllowed) return false;
            return suspended = true;
        }
        public override void Execute(Action command)
        {
            IRunnable queuedTask = ExecutorWork.Unwrap(command, nameof(command));
            suspended = false;
            ++wakes;
        }
        protected override void Run() => throw new Exception("must not run");
        protected internal override int GetNumOfRegisteredChannels() => channels;
        protected internal override long GetAndResetAccumulatedActiveTimeNanos()
        {
            ++metricReads;
            long result = activeTime;
            activeTime = 0;
            return result;
        }
        protected internal override long GetLastActivityTimeNanos() => lastActivity;
        internal int IdleCycles() => GetAndIncrementIdleCycles();
        internal int BusyCycles() => GetAndIncrementBusyCycles();
    }

    private sealed class Harness : IDisposable
    {
        internal readonly MockTicker clock = Ticker.NewMockTicker();
        internal readonly ManualExecutor[] children;
        internal readonly IObservableEventExecutorChooser chooser;
        private readonly IRunnable monitor;
        internal Harness(int min, int max, int rampUp = 1, int rampDown = 1, int patience = 0, long initialTime = 0)
        {
            if (initialTime > 0) clock.Advance(initialTime);
            else if (initialTime < 0)
            {
                clock.Advance(long.MaxValue);
                clock.Advance(unchecked(initialTime - long.MaxValue));
            }
            children = Enumerable.Range(0, max).Select(_ => new ManualExecutor(clock)).ToArray();
            if (initialTime != 0)
                foreach (ManualExecutor child in children) child.lastActivity = clock.NanoTime();
            var factory = new AutoScalingEventExecutorChooserFactory(min, max, TimeSpan.FromHours(1),
                0.4, 0.6, rampUp, rampDown, patience);
            chooser = (IObservableEventExecutorChooser)factory.NewChooser(children);
            Type monitorType = chooser.GetType().GetNestedType("UtilizationMonitor", BindingFlags.NonPublic);
            monitor = (IRunnable)Activator.CreateInstance(monitorType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                new object[] { chooser }, null);
        }
        internal void Tick(long delta = Period) { clock.Advance(delta); monitor.Run(); }
        public void Dispose() => children[0].termination.TrySetResult();
    }

    [Fact]
    public void PatienceUsesTheCounterBeforeIncrementAndRampDownRespectsTheMinimum()
    {
        using var h = new Harness(1, 4, rampDown: 1, patience: 2);
        h.Tick(); Assert.Equal(4, h.chooser.ActiveExecutorCount());
        h.Tick(); Assert.Equal(4, h.chooser.ActiveExecutorCount());
        h.Tick(); Assert.Equal(3, h.chooser.ActiveExecutorCount());
        h.Tick(); Assert.Equal(2, h.chooser.ActiveExecutorCount());
        h.Tick(); Assert.Equal(1, h.chooser.ActiveExecutorCount());
        h.Tick(); Assert.Equal(1, h.chooser.ActiveExecutorCount());
        Assert.Equal(3, h.children.Count(c => c.suspended));
        Assert.Equal(3, h.children.Sum(c => c.suspensionAttempts));
    }

    [Fact]
    public void BusyScalingUsesRampLimitAndRotatingWakeIndexAndResetsCounters()
    {
        using var h = new Harness(1, 4, rampUp: 1, rampDown: 4);
        h.Tick(); Assert.Equal(1, h.chooser.ActiveExecutorCount());
        ManualExecutor active = h.children.Single(c => !c.suspended);
        active.activeTime = Period;
        h.Tick(); Assert.Equal(2, h.chooser.ActiveExecutorCount());
        Assert.Equal(1, h.children[0].wakes);
        Assert.Equal(0, active.BusyCycles());
        Assert.Equal(0, active.IdleCycles());
        foreach (ManualExecutor child in h.children.Where(c => !c.suspended)) child.activeTime = Period;
        h.Tick(); Assert.Equal(3, h.chooser.ActiveExecutorCount());
        Assert.Equal(1, h.children[1].wakes);
        foreach (ManualExecutor child in h.children.Where(c => !c.suspended)) child.activeTime = Period;
        h.Tick(); Assert.Equal(4, h.chooser.ActiveExecutorCount());
        Assert.Equal(1, h.children[2].wakes);
        Assert.Equal(0, active.wakes);
    }

    [Fact]
    public void ZeroMinimumCanSuspendAllAndNextWakesOneUsingAllExecutorsChooser()
    {
        using var h = new Harness(0, 3, rampDown: 3);
        h.Tick(); Assert.Equal(0, h.chooser.ActiveExecutorCount());
        Assert.All(h.children, child => Assert.True(child.suspended));
        Assert.Same(h.children[0], h.chooser.Next());
        Assert.Equal(1, h.chooser.ActiveExecutorCount());
        Assert.False(h.children[0].suspended);
        Assert.Equal(1, h.children[0].wakes);
        Assert.Same(h.children[0], h.chooser.Next());
        Assert.Equal(1, h.children.Sum(c => c.wakes));
    }

    [Fact]
    public void RegisteredChannelsAndFailedSuspensionLeaveTheActiveSnapshotIntact()
    {
        using var h = new Harness(0, 2, rampDown: 2);
        h.children[0].channels = 1;
        h.children[1].suspendAllowed = false;
        h.Tick(); Assert.Equal(2, h.chooser.ActiveExecutorCount());
        Assert.Equal(0, h.children[0].suspensionAttempts);
        Assert.Equal(1, h.children[1].suspensionAttempts);
        h.children[0].channels = 0;
        h.children[1].suspendAllowed = true;
        h.Tick(); Assert.Equal(0, h.chooser.ActiveExecutorCount());
        Assert.All(h.children, child => Assert.Equal(0, child.BusyCycles()));
        Assert.All(h.children, child => Assert.Equal(0, child.IdleCycles()));
    }

    [Fact]
    public void ThresholdEqualityResetsBothCountersAndMetricsRetainAnImmutableView()
    {
        using var h = new Harness(0, 2);
        IReadOnlyList<AutoScalingUtilizationMetric> metrics = h.chooser.ExecutorUtilizations();
        h.children[0].activeTime = Period * 4 / 10;
        h.children[1].activeTime = Period * 6 / 10;
        h.Tick(); Assert.Equal(2, h.chooser.ActiveExecutorCount());
        Assert.Equal(0.4, metrics[0].Utilization());
        Assert.Equal(0.6, metrics[1].Utilization());
        Assert.All(h.children, child => Assert.Equal(0, child.IdleCycles()));
        Assert.All(h.children, child => Assert.Equal(0, child.BusyCycles()));
        Assert.Same(metrics, h.chooser.ExecutorUtilizations());
        Assert.Throws<NotSupportedException>(() => ((IList<AutoScalingUtilizationMetric>)metrics).Clear());
        Assert.Same(h.children[0], metrics[0].Executor());
        Assert.Same(h.children[1], metrics[1].Executor());
    }

    [Fact]
    public void MetricsUseActualElapsedTimeFallbackActivityAndUpperClamp()
    {
        using var h = new Harness(2, 2);
        h.children[0].lastActivity = Period / 2;
        h.children[1].activeTime = Period * 2;
        h.Tick();
        Assert.Equal(0.5, h.chooser.ExecutorUtilizations()[0].Utilization());
        Assert.Equal(1.0, h.chooser.ExecutorUtilizations()[1].Utilization());
        h.children[0].activeTime = Period;
        h.children[1].lastActivity = Period * 2;
        h.Tick(Period * 2);
        Assert.Equal(0.5, h.chooser.ExecutorUtilizations()[0].Utilization());
        Assert.Equal(0.5, h.chooser.ExecutorUtilizations()[1].Utilization());
    }

    [Fact]
    public void ShortCatchUpCallbacksDoNotInventIdleMonitoringWindows()
    {
        using var h = new Harness(1, 2, patience: 2);
        foreach (ManualExecutor child in h.children) child.activeTime = Period;
        h.Tick();
        for (int callback = 0; callback < 8; ++callback) h.Tick(1);
        Assert.Equal(2, h.chooser.ActiveExecutorCount());
        Assert.All(h.children, child => Assert.Equal(1, child.metricReads));
        Assert.All(h.chooser.ExecutorUtilizations(), metric => Assert.Equal(1.0, metric.Utilization()));

        // Sustained idle time still reaches the original pre-increment patience boundary.
        h.Tick(Period - 8); Assert.Equal(2, h.chooser.ActiveExecutorCount());
        h.Tick(); Assert.Equal(2, h.chooser.ActiveExecutorCount());
        h.Tick(); Assert.Equal(1, h.chooser.ActiveExecutorCount());
    }

    [Fact]
    public void PartialWindowCallbacksPreserveTheReportedActiveTimeBudget()
    {
        using var h = new Harness(2, 2);
        foreach (ManualExecutor child in h.children) child.activeTime = Period;
        h.Tick();
        for (int quarter = 1; quarter <= 4; ++quarter)
        {
            h.children[0].activeTime += Period / 8;
            h.children[1].activeTime += Period / 4;
            h.Tick(Period / 4);
            if (quarter < 4)
            {
                Assert.All(h.children, child => Assert.Equal(1, child.metricReads));
                Assert.All(h.chooser.ExecutorUtilizations(), metric => Assert.Equal(1.0, metric.Utilization()));
            }
        }
        Assert.All(h.children, child => Assert.Equal(2, child.metricReads));
        Assert.Equal(0.5, h.chooser.ExecutorUtilizations()[0].Utilization());
        Assert.Equal(1.0, h.chooser.ExecutorUtilizations()[1].Utilization());
        Assert.All(h.children, child => Assert.Equal(0, child.activeTime));
    }

    [Fact]
    public void ADelayedSampleDoesNotMoveTheNextScheduledWindowBoundary()
    {
        using var h = new Harness(2, 2);
        foreach (ManualExecutor child in h.children) child.activeTime = Period;
        h.Tick(Period + Period / 100);
        foreach (ManualExecutor child in h.children) child.activeTime = Period / 2;
        h.Tick(Period - Period / 100);
        Assert.All(h.children, child => Assert.Equal(2, child.metricReads));
        Assert.All(h.chooser.ExecutorUtilizations(), metric => Assert.Equal(50.0 / 99.0, metric.Utilization()));
    }

    [Fact]
    public void ADelayedWindowAndItsCatchUpCallbacksCountAsOneIdleSample()
    {
        using var h = new Harness(1, 2, patience: 2);
        h.Tick();
        h.Tick(Period * 4);
        for (int callback = 0; callback < 8; ++callback) h.Tick(1);
        Assert.Equal(2, h.chooser.ActiveExecutorCount());
        Assert.All(h.children, child => Assert.Equal(2, child.metricReads));
        h.Tick(Period - 8);
        Assert.Equal(1, h.chooser.ActiveExecutorCount());
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(long.MaxValue - Period / 2)]
    public void AcceptedWindowBoundaryCanBeZeroOrCrossTheSignedClockRange(long initialTime)
    {
        using var h = new Harness(2, 2);
        h.clock.Advance(initialTime);
        foreach (ManualExecutor child in h.children) child.activeTime = Period;
        h.Tick(0);
        h.Tick(1);
        Assert.All(h.children, child => Assert.Equal(1, child.metricReads));
        foreach (ManualExecutor child in h.children) child.activeTime = Period / 2;
        h.Tick(Period - 1);
        Assert.All(h.children, child => Assert.Equal(2, child.metricReads));
        Assert.All(h.chooser.ExecutorUtilizations(), metric => Assert.Equal(0.5, metric.Utilization()));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(long.MaxValue - 6 * Period)]
    [InlineData(-6 * Period - Period / 10)]
    public void NewlyResumedChildGetsACompleteWindowBeforeCountingIdleCycles(long initialTime)
    {
        using var h = new Harness(1, 2, patience: 2, initialTime: initialTime);
        ManualExecutor resumed = ResumeOneChild(h);
        ManualExecutor busy = h.children.Single(c => c != resumed);
        // Exercise a short post-resume interval followed by two idle windows, as in the retained trace.
        // The resumed child has not published its first batched I/O report yet.
        foreach (long delta in new[] { Period * 94 / 100, Period * 96 / 100, Period * 128 / 100 })
        {
            busy.activeTime = Period;
            h.Tick(delta);
            Assert.Equal(2, h.chooser.ActiveExecutorCount());
            Assert.False(resumed.suspended);
        }
        resumed.activeTime = Period * 7 / 10;
        busy.activeTime = Period;
        h.Tick(Period * 96 / 100);
        Assert.Equal(2, h.chooser.ActiveExecutorCount());
        Assert.False(resumed.suspended);
    }

    [Fact]
    public void ResumedLowUtilizationStillSuspendsAfterTheFullPatienceSequence()
    {
        using var h = new Harness(1, 2, patience: 2);
        ManualExecutor resumed = ResumeOneChild(h);
        ManualExecutor busy = h.children.Single(c => c != resumed);
        foreach (long delta in new[] { Period * 94 / 100, Period * 96 / 100, Period * 128 / 100 })
        {
            // Brief tasks remain low utilization; their recent timestamp does not imply a busy window.
            resumed.activeTime = Period / 100;
            resumed.lastActivity = unchecked(h.clock.NanoTime() + delta - 1);
            busy.activeTime = Period;
            h.Tick(delta);
            Assert.False(resumed.suspended);
        }
        resumed.activeTime = Period / 100;
        busy.activeTime = Period;
        h.Tick(Period * 96 / 100);
        Assert.True(resumed.suspended);
        Assert.Equal(1, h.chooser.ActiveExecutorCount());
    }

    [Fact]
    public void ResumedPartialWindowPublishesMetricsWithoutCountingBusyPatience()
    {
        using var h = new Harness(1, 2, patience: 2);
        ManualExecutor resumed = ResumeOneChild(h);
        resumed.activeTime = Period * 7 / 10;
        int priorReads = resumed.metricReads;
        h.Tick(Period * 94 / 100);
        Assert.Equal(priorReads + 1, resumed.metricReads);
        Assert.Equal(0, resumed.activeTime);
        Assert.Equal((Period * 7 / 10) / (double)(Period * 94 / 100),
            h.chooser.ExecutorUtilizations().Single(m => m.Executor() == resumed).Utilization());
        Assert.Equal(0, resumed.BusyCycles());
        Assert.Equal(0, resumed.IdleCycles());
    }

    [Fact]
    public void ASecondResumeStartsANewEligibilityWindowAfterSnapshotRebuild()
    {
        using var h = new Harness(1, 2, patience: 2);
        ManualExecutor resumed = ResumeOneChild(h);
        ManualExecutor busy = h.children.Single(c => c != resumed);
        foreach (long delta in new[] { Period * 94 / 100, Period * 96 / 100, Period * 128 / 100, Period * 96 / 100 })
        {
            busy.activeTime = Period;
            h.Tick(delta);
        }
        Assert.True(resumed.suspended);
        busy.activeTime = Period;
        h.Tick(Period + Period / 10);
        Assert.False(resumed.suspended);
        Assert.Equal(2, resumed.wakes);
        resumed.lastActivity = h.clock.NanoTime();
        busy.activeTime = Period;
        h.Tick(Period * 94 / 100);
        Assert.False(resumed.suspended);
        Assert.Equal(0, resumed.IdleCycles());
    }

    private static ManualExecutor ResumeOneChild(Harness h)
    {
        h.Tick(); h.Tick(); h.Tick();
        Assert.Equal(1, h.chooser.ActiveExecutorCount());
        ManualExecutor busy = h.children.Single(c => !c.suspended);
        busy.activeTime = Period; h.Tick();
        busy.activeTime = Period; h.Tick();
        busy.activeTime = Period; h.Tick(Period + Period / 10);
        Assert.Equal(2, h.chooser.ActiveExecutorCount());
        ManualExecutor resumed = h.children.Single(c => c.wakes != 0);
        resumed.lastActivity = h.clock.NanoTime(); // The wake-up task has completed, as in the retained trace.
        return resumed;
    }

    [Fact]
    public void InvalidElapsedWindowAndShutdownSkipMetricReadsAndScaling()
    {
        using var h = new Harness(1, 2);
        h.children[0].activeTime = Period / 2;
        h.children[1].activeTime = Period / 2;
        h.Tick();
        h.Tick(0);
        Assert.All(h.children, child => Assert.Equal(1, child.metricReads));
        h.children[0].shuttingDown = true;
        h.Tick();
        Assert.All(h.children, child => Assert.Equal(1, child.metricReads));
        Assert.Equal(2, h.chooser.ActiveExecutorCount());
        Assert.All(h.chooser.ExecutorUtilizations(), metric => Assert.Equal(0.5, metric.Utilization()));
    }

    [Fact]
    public void ConstructorChecksPinnedRangesAndKeepsJavaNaNComparisonSemantics()
    {
        AutoScalingEventExecutorChooserFactory Create(int min = 0, int max = 1, double down = 0.4,
            double up = 0.6, int rampUp = 1, int rampDown = 1, int patience = 0, TimeSpan? window = null) =>
            new(min, max, window ?? TimeSpan.FromHours(1), down, up, rampUp, rampDown, patience);
        Assert.Throws<ArgumentException>(() => Create(min: -1));
        Assert.Throws<ArgumentException>(() => Create(max: 0));
        Assert.Throws<ArgumentException>(() => Create(min: 2));
        Assert.Throws<ArgumentException>(() => Create(window: TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => Create(down: -0.1));
        Assert.Throws<ArgumentException>(() => Create(up: 1.1));
        Assert.Throws<ArgumentException>(() => Create(down: 0.6));
        Assert.Throws<ArgumentException>(() => Create(rampUp: 0));
        Assert.Throws<ArgumentException>(() => Create(rampDown: 0));
        Assert.Throws<ArgumentException>(() => Create(patience: -1));
        Assert.NotNull(Create(down: double.NaN, up: double.NaN));
        Assert.NotNull(Create(window: TimeSpan.MaxValue));
        Assert.NotNull(Create(down: 0, up: 1));
    }
}
