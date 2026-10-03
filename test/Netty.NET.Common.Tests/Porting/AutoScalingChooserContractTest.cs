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
        private readonly MockTicker clock = Ticker.newMockTicker();
        private Thread reportingThread;
        internal AccountingExecutor()
            : base(null, new AnonymousExecutor(_ => throw new Exception("must not start")), true, true,
                int.MaxValue, RejectedExecutionHandlers.reject()) { }
        public override Ticker ticker() => clock ?? Ticker.systemTicker();
        public override bool inEventLoop(Thread thread) => thread != null && thread == Volatile.Read(ref reportingThread);
        protected override void run() => throw new Exception("must not run");
        internal void Report(long nanos)
        {
            Volatile.Write(ref reportingThread, Thread.CurrentThread);
            reportActiveIoTime(nanos);
        }
        internal void ReportTask(long nanos)
        {
            Volatile.Write(ref reportingThread, Thread.CurrentThread);
            addTask(Runnables.Create(() => clock.advance(nanos)));
            runAllTasks(1_000_000L);
        }
        internal long Sample() => getAndResetAccumulatedActiveTimeNanos();
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
            : base(null, new AnonymousExecutor(_ => throw new Exception("must not start")), true, true,
                int.MaxValue, RejectedExecutionHandlers.reject()) => this.clock = clock;
        public override Ticker ticker() => clock ?? Ticker.systemTicker();
        public override bool isSuspended() => suspended;
        public override bool isShuttingDown() => shuttingDown;
        public override Task Termination => termination.Task;
        public override bool trySuspend()
        {
            ++suspensionAttempts;
            if (!suspendAllowed) return false;
            return suspended = true;
        }
        public override void execute(IRunnable command) { suspended = false; ++wakes; }
        protected override void run() => throw new Exception("must not run");
        protected internal override int getNumOfRegisteredChannels() => channels;
        protected internal override long getAndResetAccumulatedActiveTimeNanos()
        {
            ++metricReads;
            long result = activeTime;
            activeTime = 0;
            return result;
        }
        protected internal override long getLastActivityTimeNanos() => lastActivity;
        internal int idleCycles() => getAndIncrementIdleCycles();
        internal int busyCycles() => getAndIncrementBusyCycles();
    }

    private sealed class Harness : IDisposable
    {
        internal readonly MockTicker clock = Ticker.newMockTicker();
        internal readonly ManualExecutor[] children;
        internal readonly IObservableEventExecutorChooser chooser;
        private readonly IRunnable monitor;
        internal Harness(int min, int max, int rampUp = 1, int rampDown = 1, int patience = 0)
        {
            children = Enumerable.Range(0, max).Select(_ => new ManualExecutor(clock)).ToArray();
            var factory = new AutoScalingEventExecutorChooserFactory(min, max, TimeSpan.FromHours(1),
                0.4, 0.6, rampUp, rampDown, patience);
            chooser = (IObservableEventExecutorChooser)factory.newChooser(children);
            Type monitorType = chooser.GetType().GetNestedType("UtilizationMonitor", BindingFlags.NonPublic);
            monitor = (IRunnable)Activator.CreateInstance(monitorType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                new object[] { chooser }, null);
        }
        internal void tick(long delta = Period) { clock.advance(delta); monitor.run(); }
        public void Dispose() => children[0].termination.TrySetResult();
    }

    [Fact]
    public void PatienceUsesTheCounterBeforeIncrementAndRampDownRespectsTheMinimum()
    {
        using var h = new Harness(1, 4, rampDown: 1, patience: 2);
        h.tick(); Assert.Equal(4, h.chooser.activeExecutorCount());
        h.tick(); Assert.Equal(4, h.chooser.activeExecutorCount());
        h.tick(); Assert.Equal(3, h.chooser.activeExecutorCount());
        h.tick(); Assert.Equal(2, h.chooser.activeExecutorCount());
        h.tick(); Assert.Equal(1, h.chooser.activeExecutorCount());
        h.tick(); Assert.Equal(1, h.chooser.activeExecutorCount());
        Assert.Equal(3, h.children.Count(c => c.suspended));
        Assert.Equal(3, h.children.Sum(c => c.suspensionAttempts));
    }

    [Fact]
    public void BusyScalingUsesRampLimitAndRotatingWakeIndexAndResetsCounters()
    {
        using var h = new Harness(1, 4, rampUp: 1, rampDown: 4);
        h.tick(); Assert.Equal(1, h.chooser.activeExecutorCount());
        ManualExecutor active = h.children.Single(c => !c.suspended);
        active.activeTime = Period;
        h.tick(); Assert.Equal(2, h.chooser.activeExecutorCount());
        Assert.Equal(1, h.children[0].wakes);
        Assert.Equal(0, active.busyCycles());
        Assert.Equal(0, active.idleCycles());
        foreach (ManualExecutor child in h.children.Where(c => !c.suspended)) child.activeTime = Period;
        h.tick(); Assert.Equal(3, h.chooser.activeExecutorCount());
        Assert.Equal(1, h.children[1].wakes);
        foreach (ManualExecutor child in h.children.Where(c => !c.suspended)) child.activeTime = Period;
        h.tick(); Assert.Equal(4, h.chooser.activeExecutorCount());
        Assert.Equal(1, h.children[2].wakes);
        Assert.Equal(0, active.wakes);
    }

    [Fact]
    public void ZeroMinimumCanSuspendAllAndNextWakesOneUsingAllExecutorsChooser()
    {
        using var h = new Harness(0, 3, rampDown: 3);
        h.tick(); Assert.Equal(0, h.chooser.activeExecutorCount());
        Assert.All(h.children, child => Assert.True(child.suspended));
        Assert.Same(h.children[0], h.chooser.next());
        Assert.Equal(1, h.chooser.activeExecutorCount());
        Assert.False(h.children[0].suspended);
        Assert.Equal(1, h.children[0].wakes);
        Assert.Same(h.children[0], h.chooser.next());
        Assert.Equal(1, h.children.Sum(c => c.wakes));
    }

    [Fact]
    public void RegisteredChannelsAndFailedSuspensionLeaveTheActiveSnapshotIntact()
    {
        using var h = new Harness(0, 2, rampDown: 2);
        h.children[0].channels = 1;
        h.children[1].suspendAllowed = false;
        h.tick(); Assert.Equal(2, h.chooser.activeExecutorCount());
        Assert.Equal(0, h.children[0].suspensionAttempts);
        Assert.Equal(1, h.children[1].suspensionAttempts);
        h.children[0].channels = 0;
        h.children[1].suspendAllowed = true;
        h.tick(); Assert.Equal(0, h.chooser.activeExecutorCount());
        Assert.All(h.children, child => Assert.Equal(0, child.busyCycles()));
        Assert.All(h.children, child => Assert.Equal(0, child.idleCycles()));
    }

    [Fact]
    public void ThresholdEqualityResetsBothCountersAndMetricsRetainAnImmutableView()
    {
        using var h = new Harness(0, 2);
        IReadOnlyList<AutoScalingUtilizationMetric> metrics = h.chooser.executorUtilizations();
        h.children[0].activeTime = Period * 4 / 10;
        h.children[1].activeTime = Period * 6 / 10;
        h.tick(); Assert.Equal(2, h.chooser.activeExecutorCount());
        Assert.Equal(0.4, metrics[0].utilization());
        Assert.Equal(0.6, metrics[1].utilization());
        Assert.All(h.children, child => Assert.Equal(0, child.idleCycles()));
        Assert.All(h.children, child => Assert.Equal(0, child.busyCycles()));
        Assert.Same(metrics, h.chooser.executorUtilizations());
        Assert.Throws<NotSupportedException>(() => ((IList<AutoScalingUtilizationMetric>)metrics).Clear());
        Assert.Same(h.children[0], metrics[0].executor());
        Assert.Same(h.children[1], metrics[1].executor());
    }

    [Fact]
    public void MetricsUseActualElapsedTimeFallbackActivityAndUpperClamp()
    {
        using var h = new Harness(2, 2);
        h.children[0].lastActivity = Period / 2;
        h.children[1].activeTime = Period * 2;
        h.tick();
        Assert.Equal(0.5, h.chooser.executorUtilizations()[0].utilization());
        Assert.Equal(1.0, h.chooser.executorUtilizations()[1].utilization());
        h.children[0].activeTime = Period;
        h.children[1].lastActivity = Period * 2;
        h.tick(Period * 2);
        Assert.Equal(0.5, h.chooser.executorUtilizations()[0].utilization());
        Assert.Equal(0.5, h.chooser.executorUtilizations()[1].utilization());
    }

    [Fact]
    public void ShortCatchUpCallbacksDoNotInventIdleMonitoringWindows()
    {
        using var h = new Harness(1, 2, patience: 2);
        foreach (ManualExecutor child in h.children) child.activeTime = Period;
        h.tick();
        for (int callback = 0; callback < 8; ++callback) h.tick(1);
        Assert.Equal(2, h.chooser.activeExecutorCount());
        Assert.All(h.children, child => Assert.Equal(1, child.metricReads));
        Assert.All(h.chooser.executorUtilizations(), metric => Assert.Equal(1.0, metric.utilization()));

        // Sustained idle time still reaches the original pre-increment patience boundary.
        h.tick(Period - 8); Assert.Equal(2, h.chooser.activeExecutorCount());
        h.tick(); Assert.Equal(2, h.chooser.activeExecutorCount());
        h.tick(); Assert.Equal(1, h.chooser.activeExecutorCount());
    }

    [Fact]
    public void PartialWindowCallbacksPreserveTheReportedActiveTimeBudget()
    {
        using var h = new Harness(2, 2);
        foreach (ManualExecutor child in h.children) child.activeTime = Period;
        h.tick();
        for (int quarter = 1; quarter <= 4; ++quarter)
        {
            h.children[0].activeTime += Period / 8;
            h.children[1].activeTime += Period / 4;
            h.tick(Period / 4);
            if (quarter < 4)
            {
                Assert.All(h.children, child => Assert.Equal(1, child.metricReads));
                Assert.All(h.chooser.executorUtilizations(), metric => Assert.Equal(1.0, metric.utilization()));
            }
        }
        Assert.All(h.children, child => Assert.Equal(2, child.metricReads));
        Assert.Equal(0.5, h.chooser.executorUtilizations()[0].utilization());
        Assert.Equal(1.0, h.chooser.executorUtilizations()[1].utilization());
        Assert.All(h.children, child => Assert.Equal(0, child.activeTime));
    }

    [Fact]
    public void ADelayedSampleDoesNotMoveTheNextScheduledWindowBoundary()
    {
        using var h = new Harness(2, 2);
        foreach (ManualExecutor child in h.children) child.activeTime = Period;
        h.tick(Period + Period / 100);
        foreach (ManualExecutor child in h.children) child.activeTime = Period / 2;
        h.tick(Period - Period / 100);
        Assert.All(h.children, child => Assert.Equal(2, child.metricReads));
        Assert.All(h.chooser.executorUtilizations(), metric => Assert.Equal(50.0 / 99.0, metric.utilization()));
    }

    [Fact]
    public void ADelayedWindowAndItsCatchUpCallbacksCountAsOneIdleSample()
    {
        using var h = new Harness(1, 2, patience: 2);
        h.tick();
        h.tick(Period * 4);
        for (int callback = 0; callback < 8; ++callback) h.tick(1);
        Assert.Equal(2, h.chooser.activeExecutorCount());
        Assert.All(h.children, child => Assert.Equal(2, child.metricReads));
        h.tick(Period - 8);
        Assert.Equal(1, h.chooser.activeExecutorCount());
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(long.MaxValue - Period / 2)]
    public void AcceptedWindowBoundaryCanBeZeroOrCrossTheSignedClockRange(long initialTime)
    {
        using var h = new Harness(2, 2);
        h.clock.advance(initialTime);
        foreach (ManualExecutor child in h.children) child.activeTime = Period;
        h.tick(0);
        h.tick(1);
        Assert.All(h.children, child => Assert.Equal(1, child.metricReads));
        foreach (ManualExecutor child in h.children) child.activeTime = Period / 2;
        h.tick(Period - 1);
        Assert.All(h.children, child => Assert.Equal(2, child.metricReads));
        Assert.All(h.chooser.executorUtilizations(), metric => Assert.Equal(0.5, metric.utilization()));
    }

    [Fact]
    public void InvalidElapsedWindowAndShutdownSkipMetricReadsAndScaling()
    {
        using var h = new Harness(1, 2);
        h.children[0].activeTime = Period / 2;
        h.children[1].activeTime = Period / 2;
        h.tick();
        h.tick(0);
        Assert.All(h.children, child => Assert.Equal(1, child.metricReads));
        h.children[0].shuttingDown = true;
        h.tick();
        Assert.All(h.children, child => Assert.Equal(1, child.metricReads));
        Assert.Equal(2, h.chooser.activeExecutorCount());
        Assert.All(h.chooser.executorUtilizations(), metric => Assert.Equal(0.5, metric.utilization()));
    }

    [Fact]
    public void ConstructorChecksPinnedRangesAndKeepsJavaNaNComparisonSemantics()
    {
        AutoScalingEventExecutorChooserFactory create(int min = 0, int max = 1, double down = 0.4,
            double up = 0.6, int rampUp = 1, int rampDown = 1, int patience = 0, TimeSpan? window = null) =>
            new(min, max, window ?? TimeSpan.FromHours(1), down, up, rampUp, rampDown, patience);
        Assert.Throws<ArgumentException>(() => create(min: -1));
        Assert.Throws<ArgumentException>(() => create(max: 0));
        Assert.Throws<ArgumentException>(() => create(min: 2));
        Assert.Throws<ArgumentException>(() => create(window: TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => create(down: -0.1));
        Assert.Throws<ArgumentException>(() => create(up: 1.1));
        Assert.Throws<ArgumentException>(() => create(down: 0.6));
        Assert.Throws<ArgumentException>(() => create(rampUp: 0));
        Assert.Throws<ArgumentException>(() => create(rampDown: 0));
        Assert.Throws<ArgumentException>(() => create(patience: -1));
        Assert.NotNull(create(down: double.NaN, up: double.NaN));
        Assert.NotNull(create(window: TimeSpan.MaxValue));
        Assert.NotNull(create(down: 0, up: 1));
    }
}
