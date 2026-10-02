using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Xunit;
using Void = Netty.NET.Common.Concurrent.Void;

namespace Netty.NET.Common.Tests.Porting;

[Collection("Global executor")]
public class AutoScalingChooserContractTest
{
    private const long Period = 3_600_000_000_000L;

    // Invoke the private monitor with an independent mock clock. Its normal scheduled
    // instance remains dormant for an hour and is canceled by the real lifecycle listener.
    private sealed class ManualExecutor : SingleThreadEventExecutor
    {
        private readonly MockTicker clock;
        internal readonly IPromise<Void> termination = new DefaultPromise<Void>(ImmediateEventExecutor.INSTANCE);
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
        public override IFuture<Void> terminationFuture() => termination;
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
        public void Dispose() => children[0].termination.trySuccess(null);
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
