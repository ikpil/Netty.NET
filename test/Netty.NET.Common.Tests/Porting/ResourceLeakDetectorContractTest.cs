using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

[Collection("Leak detector globals")]
public class ResourceLeakDetectorContractTest : IDisposable
{
    private readonly ResourceLeakDetectorLevel previous = ResourceLeakDetector.getLevel();
    public ResourceLeakDetectorContractTest() => ResourceLeakDetector.setLevel(ResourceLeakDetectorLevel.PARANOID);
    public void Dispose() => ResourceLeakDetector.setLevel(previous);
    private sealed class Detector : ResourceLeakDetector<object>, ResourceLeakDetector<object>.LeakListener
    {
        internal readonly List<string> Reports = new();
        internal object Hint;
        internal bool Reporting = true;
        internal int ListenerCalls;
        internal int ReportThread;
        internal Detector(int sampling = 1) : base(typeof(object), sampling) { }
        protected override bool needReport() => Reporting;
        protected override object getInitialHint(string type) => Hint;
        protected override void reportTracedLeak(string type, string records) { Reports.Add(records); ReportThread = Environment.CurrentManagedThreadId; }
        protected override void reportUntracedLeak(string type) { Reports.Add(""); ReportThread = Environment.CurrentManagedThreadId; }
        public void onLeak(string type, string records) { Assert.Equal("Object", type); ListenerCalls++; }
        internal void flush() { object resource = new(); trackForcibly(resource).close(resource); }
    }
    private sealed class MutableHint : IResourceLeakHint
    {
        internal string Text;
        public string toHintString() => Text;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IResourceLeakTracker<object> createLeak(Detector detector, out WeakReference<object> resource)
    {
        object value = new();
        resource = new WeakReference<object>(value);
        return detector.trackForcibly(value);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool isCollected(WeakReference<object> resource) => !resource.TryGetTarget(out _);
    private static void collectUntil(Action visit, Func<bool> done)
    {
        var elapsed = Stopwatch.StartNew();
        do
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); visit();
            if (done()) return;
            Thread.Sleep(10);
        } while (elapsed.Elapsed < TimeSpan.FromSeconds(5));
        Assert.True(done());
    }

    [Theory]
    [InlineData(" disabled ", ResourceLeakDetectorLevel.DISABLED)]
    [InlineData("aDvAnCeD", ResourceLeakDetectorLevel.ADVANCED)]
    [InlineData("3", ResourceLeakDetectorLevel.PARANOID)]
    [InlineData("0", ResourceLeakDetectorLevel.DISABLED)]
    [InlineData("999", ResourceLeakDetectorLevel.SIMPLE)]
    [InlineData("-1", ResourceLeakDetectorLevel.SIMPLE)]
    [InlineData("+2", ResourceLeakDetectorLevel.SIMPLE)]
    [InlineData("02", ResourceLeakDetectorLevel.SIMPLE)]
    [InlineData("SIMPLE,ADVANCED", ResourceLeakDetectorLevel.SIMPLE)]
    public void LevelParsingAcceptsOnlyNamedLevelsAndExactOrdinals(string text, ResourceLeakDetectorLevel expected)
        => Assert.Equal(expected, ResourceLeakDetector.parseLevel(text));

    [Fact]
    public void ForceIgnoresDisabledLevelAndSamplingWhileRecordCapabilityFollowsLevel()
    {
        var detector = new Detector(0);
        object resource = new();
        ResourceLeakDetector.setLevel(ResourceLeakDetectorLevel.DISABLED);
        Assert.Null(detector.track(resource));
        Assert.False(detector.isRecordEnabled());
        Assert.True(detector.trackForcibly(resource).close(resource));
        ResourceLeakDetector.setLevel(ResourceLeakDetectorLevel.SIMPLE);
        Assert.Throws<ArgumentOutOfRangeException>(() => detector.track(resource));
        ResourceLeakDetector.setLevel(ResourceLeakDetectorLevel.PARANOID);
        Assert.True(detector.track(resource).close(resource));
        Assert.Equal(ResourceLeakDetector.TARGET_RECORDS > 0, detector.isRecordEnabled());
        Assert.Throws<ArgumentOutOfRangeException>(() => ResourceLeakDetector.setLevel((ResourceLeakDetectorLevel)999));
    }

    [Fact]
    public void RetainedTrackerDoesNotRootResourceAndReportsOnNextTrackingThread()
    {
        var detector = new Detector { Hint = "retained-tracker" };
        detector.setLeakListener(detector);
        IResourceLeakTracker<object> tracker = createLeak(detector, out var resource);
        collectUntil(detector.flush, () => detector.Reports.Count == 1);
        Assert.True(isCollected(resource));
        Assert.Contains("retained-tracker", detector.Reports[0]);
        Assert.Contains(nameof(createLeak), detector.Reports[0]);
        Assert.Equal(Environment.CurrentManagedThreadId, detector.ReportThread);
        Assert.Equal(1, detector.ListenerCalls);
        Assert.Equal("", tracker.ToString());
        detector.flush();
        Assert.Single(detector.Reports);
        GC.KeepAlive(tracker);
    }

    [Fact]
    public void LiveResourceAndExplicitCloseSuppressReportsAndPreserveFirstCloseStack()
    {
        var detector = new Detector();
        object resource = new();
        var tracker = detector.trackForcibly(resource);
        GC.Collect(); GC.WaitForPendingFinalizers(); detector.flush();
        Assert.Empty(detector.Reports);
        Assert.Null(tracker.getCloseStackTraceIfAny());
        Assert.True(tracker.close(resource));
        Exception close = tracker.getCloseStackTraceIfAny();
        if (ResourceLeakDetector.TRACK_CLOSE)
        {
            Assert.NotNull(close);
            Assert.Contains(nameof(LiveResourceAndExplicitCloseSuppressReportsAndPreserveFirstCloseStack), close.StackTrace);
        }
        else Assert.Null(close);
        Assert.False(tracker.close(resource));
        Assert.Same(close, tracker.getCloseStackTraceIfAny());
        tracker.record(new ThrowingHint());
        Assert.Same(close, tracker.getCloseStackTraceIfAny());
        GC.KeepAlive(resource);
    }
    private sealed class ThrowingHint : IResourceLeakHint { public string toHintString() => throw new InvalidOperationException(); }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void recordFromCaller(IResourceLeakTracker<object> tracker, object hint) => tracker.record(hint);
    [Fact]
    public void RecordsSnapshotHintAndAccessCallerRatherThanReportGenerationStack()
    {
        var detector = new Detector();
        object resource = new();
        var tracker = detector.trackForcibly(resource);
        var hint = new MutableHint { Text = "original-hint" };
        recordFromCaller(tracker, hint);
        hint.Text = "mutated-hint";
        string report = tracker.ToString();
        Assert.Contains("original-hint", report);
        Assert.DoesNotContain("mutated-hint", report);
        Assert.Contains(nameof(recordFromCaller), report);
        Assert.DoesNotContain("generateReport", report);
        tracker.close(resource);
    }

    [Fact]
    public void ClosingDoesNotAcquireTheTrackedObjectsMonitor()
    {
        var detector = new Detector();
        object resource = new();
        var tracker = detector.trackForcibly(resource);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() => { lock (resource) { entered.Set(); release.Wait(); } }) { IsBackground = true };
        holder.Start();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Task<bool> close = Task.Run(() => tracker.close(resource));
            Assert.True(close.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(close.Result);
        }
        finally { release.Set(); Assert.True(holder.Join(TimeSpan.FromSeconds(5))); }
    }

    [Fact]
    public void ConcurrentCloseSucceedsExactlyOnce()
    {
        var detector = new Detector();
        object resource = new();
        var tracker = detector.trackForcibly(resource);
        int successes = 0;
        Parallel.For(0, 32, _ => { if (tracker.close(resource)) Interlocked.Increment(ref successes); });
        Assert.Equal(1, successes);
        Assert.Equal(ResourceLeakDetector.TRACK_CLOSE, tracker.getCloseStackTraceIfAny() != null);
    }

    private sealed class BlockingHint(ManualResetEventSlim entered, ManualResetEventSlim release) : IResourceLeakHint
    {
        public string toHintString() { entered.Set(); release.Wait(); return "concurrent record"; }
    }
    [Fact]
    public void RecordThatStartedBeforeCloseCannotOverwriteCloseMarker()
    {
        var detector = new Detector();
        object resource = new();
        var tracker = detector.trackForcibly(resource);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task record = Task.Run(() => tracker.record(new BlockingHint(entered, release)));
        Exception close = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            tracker.close(resource);
            close = tracker.getCloseStackTraceIfAny();
        }
        finally { release.Set(); Assert.True(record.Wait(TimeSpan.FromSeconds(5))); }
        Assert.Same(close, tracker.getCloseStackTraceIfAny());
    }

    [Fact]
    public void DuplicateCreationReportsAreSuppressedAndDisabledReportingDrainsNotifications()
    {
        var detector = new Detector();
        detector.setLeakListener(detector);
        IResourceLeakTracker<object> first = null;
        for (int i = 0; i < 2; i++) first = createLeak(detector, out _);
        collectUntil(detector.flush, () => detector.Reports.Count == 1);
        GC.Collect(); GC.WaitForPendingFinalizers(); detector.flush();
        Assert.Single(detector.Reports);
        Assert.Equal(1, detector.ListenerCalls);
        detector.Reporting = false;
        var discarded = createLeak(detector, out var resource);
        collectUntil(detector.flush, () => isCollected(resource));
        GC.Collect(); GC.WaitForPendingFinalizers(); detector.flush();
        Assert.False(((IResourceLeak)discarded).close());
        Assert.NotEmpty(discarded.ToString());
        detector.Reporting = true;
        detector.flush();
        Assert.Single(detector.Reports);
        GC.KeepAlive(first); GC.KeepAlive(discarded);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void createTwoTrackers(Detector detector)
    {
        object resource = new();
        detector.Hint = "first-tracker";
        detector.trackForcibly(resource);
        detector.Hint = "second-tracker";
        detector.trackForcibly(resource);
    }
    [Fact]
    public void OneResourceCanNotifyMultipleTrackersAfterTheirReferentIsCollected()
    {
        var detector = new Detector();
        createTwoTrackers(detector);
        collectUntil(detector.flush, () => detector.Reports.Count == 2);
        Assert.Contains(detector.Reports, report => report.Contains("first-tracker"));
        Assert.Contains(detector.Reports, report => report.Contains("second-tracker"));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IResourceLeakTracker<object> closeAndRetrack(Detector detector, out WeakReference<object> weak)
    {
        object resource = new();
        weak = new WeakReference<object>(resource);
        detector.Hint = "closed-registration";
        Assert.True(detector.trackForcibly(resource).close(resource));
        detector.Hint = "live-registration";
        var live = detector.trackForcibly(resource);
        detector.Hint = "cancelled-sibling";
        Assert.True(detector.trackForcibly(resource).close(resource));
        return live;
    }

    [Fact]
    public void RetrackingAfterCloseRearmsCollectionAndCancellingOneSiblingKeepsTheOther()
    {
        var detector = new Detector();
        var tracker = closeAndRetrack(detector, out var resource);
        collectUntil(detector.flush, () => detector.Reports.Count == 1);
        Assert.True(isCollected(resource));
        Assert.Contains("live-registration", detector.Reports[0]);
        Assert.DoesNotContain("closed-registration", detector.Reports[0]);
        Assert.DoesNotContain("cancelled-sibling", detector.Reports[0]);
        Assert.False(((IResourceLeak)tracker).close());
        GC.KeepAlive(tracker);
    }

    private sealed class ExcludedAccessHelper
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void record(IResourceLeakTracker<object> tracker) => tracker.record();
    }

    private sealed class GenericExcludedAccessHelper<T>
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void record(IResourceLeakTracker<object> tracker) => tracker.record();
    }

    [Fact]
    public void ExclusionsUseDeclaredMethodsAndFullyQualifiedClrNamesIncludingNestedTypes()
    {
        var detector = new Detector();
        object resource = new();
        var tracker = detector.trackForcibly(resource);
        ExcludedAccessHelper.record(tracker);
        string displayedType = typeof(ExcludedAccessHelper).FullName.Replace('+', '.');
        Assert.Contains(displayedType, tracker.ToString());
        Assert.Throws<ArgumentException>(() => ResourceLeakDetector.addExclusions(typeof(ExcludedAccessHelper), "missing"));
        Assert.Throws<ArgumentException>(() => ResourceLeakDetector.addExclusions(typeof(ExcludedAccessHelper), nameof(ToString)));
        ResourceLeakDetector.addExclusions(typeof(ExcludedAccessHelper), nameof(ExcludedAccessHelper.record));
        Assert.DoesNotContain(displayedType, tracker.ToString());
        Assert.Contains(nameof(ExclusionsUseDeclaredMethodsAndFullyQualifiedClrNamesIncludingNestedTypes), tracker.ToString());
        GenericExcludedAccessHelper<object>.record(tracker);
        GenericExcludedAccessHelper<string>.record(tracker);
        Assert.Contains("GenericExcludedAccessHelper", tracker.ToString());
        ResourceLeakDetector.addExclusions(typeof(GenericExcludedAccessHelper<object>), nameof(GenericExcludedAccessHelper<object>.record));
        Assert.DoesNotContain("GenericExcludedAccessHelper", tracker.ToString());
        Assert.True(tracker.close(resource));
    }
}
