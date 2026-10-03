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
    private readonly ResourceLeakDetectorLevel previous = ResourceLeakDetector.GetLevel();
    public ResourceLeakDetectorContractTest() => ResourceLeakDetector.SetLevel(ResourceLeakDetectorLevel.PARANOID);
    public void Dispose() => ResourceLeakDetector.SetLevel(previous);
    private sealed class Detector : ResourceLeakDetector<object>, ResourceLeakDetector<object>.LeakListener
    {
        internal readonly List<string> Reports = new();
        internal object Hint;
        internal bool Reporting = true;
        internal int ListenerCalls;
        internal int ReportThread;
        internal Detector(int sampling = 1) : base(typeof(object), sampling) { }
        protected override bool NeedReport() => Reporting;
        protected override object GetInitialHint(string type) => Hint;
        protected override void ReportTracedLeak(string type, string records) { Reports.Add(records); ReportThread = Environment.CurrentManagedThreadId; }
        protected override void ReportUntracedLeak(string type) { Reports.Add(""); ReportThread = Environment.CurrentManagedThreadId; }
        public void OnLeak(string type, string records) { Assert.Equal("Object", type); ListenerCalls++; }
        internal void Flush() { object resource = new(); TrackForcibly(resource).Close(resource); }
    }
    private sealed class MutableHint : IResourceLeakHint
    {
        internal string Text;
        public string ToHintString() => Text;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IResourceLeakTracker<object> CreateLeak(Detector detector, out WeakReference<object> resource)
    {
        object value = new();
        resource = new WeakReference<object>(value);
        return detector.TrackForcibly(value);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsCollected(WeakReference<object> resource) => !resource.TryGetTarget(out _);
    private static void CollectUntil(Action visit, Func<bool> done)
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
        => Assert.Equal(expected, ResourceLeakDetector.ParseLevel(text));

    [Fact]
    public void ForceIgnoresDisabledLevelAndSamplingWhileRecordCapabilityFollowsLevel()
    {
        var detector = new Detector(0);
        object resource = new();
        ResourceLeakDetector.SetLevel(ResourceLeakDetectorLevel.DISABLED);
        Assert.Null(detector.Track(resource));
        Assert.False(detector.IsRecordEnabled());
        Assert.True(detector.TrackForcibly(resource).Close(resource));
        ResourceLeakDetector.SetLevel(ResourceLeakDetectorLevel.SIMPLE);
        Assert.Throws<ArgumentOutOfRangeException>(() => detector.Track(resource));
        ResourceLeakDetector.SetLevel(ResourceLeakDetectorLevel.PARANOID);
        Assert.True(detector.Track(resource).Close(resource));
        Assert.Equal(ResourceLeakDetector.TARGET_RECORDS > 0, detector.IsRecordEnabled());
        Assert.Throws<ArgumentOutOfRangeException>(() => ResourceLeakDetector.SetLevel((ResourceLeakDetectorLevel)999));
    }

    [Fact]
    public void RetainedTrackerDoesNotRootResourceAndReportsOnNextTrackingThread()
    {
        var detector = new Detector { Hint = "retained-tracker" };
        detector.SetLeakListener(detector);
        IResourceLeakTracker<object> tracker = CreateLeak(detector, out var resource);
        CollectUntil(detector.Flush, () => detector.Reports.Count == 1);
        Assert.True(IsCollected(resource));
        Assert.Contains("retained-tracker", detector.Reports[0]);
        Assert.Contains(nameof(CreateLeak), detector.Reports[0]);
        Assert.Equal(Environment.CurrentManagedThreadId, detector.ReportThread);
        Assert.Equal(1, detector.ListenerCalls);
        Assert.Equal("", tracker.ToString());
        detector.Flush();
        Assert.Single(detector.Reports);
        GC.KeepAlive(tracker);
    }

    [Fact]
    public void LiveResourceAndExplicitCloseSuppressReportsAndPreserveFirstCloseStack()
    {
        var detector = new Detector();
        object resource = new();
        var tracker = detector.TrackForcibly(resource);
        GC.Collect(); GC.WaitForPendingFinalizers(); detector.Flush();
        Assert.Empty(detector.Reports);
        Assert.Null(tracker.GetCloseStackTraceIfAny());
        Assert.True(tracker.Close(resource));
        Exception close = tracker.GetCloseStackTraceIfAny();
        if (ResourceLeakDetector.TRACK_CLOSE)
        {
            Assert.NotNull(close);
            Assert.Contains(nameof(LiveResourceAndExplicitCloseSuppressReportsAndPreserveFirstCloseStack), close.StackTrace);
        }
        else Assert.Null(close);
        Assert.False(tracker.Close(resource));
        Assert.Same(close, tracker.GetCloseStackTraceIfAny());
        tracker.Record(new ThrowingHint());
        Assert.Same(close, tracker.GetCloseStackTraceIfAny());
        GC.KeepAlive(resource);
    }
    private sealed class ThrowingHint : IResourceLeakHint { public string ToHintString() => throw new InvalidOperationException(); }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RecordFromCaller(IResourceLeakTracker<object> tracker, object hint) => tracker.Record(hint);
    [Fact]
    public void RecordsSnapshotHintAndAccessCallerRatherThanReportGenerationStack()
    {
        var detector = new Detector();
        object resource = new();
        var tracker = detector.TrackForcibly(resource);
        var hint = new MutableHint { Text = "original-hint" };
        RecordFromCaller(tracker, hint);
        hint.Text = "mutated-hint";
        string report = tracker.ToString();
        Assert.Contains("original-hint", report);
        Assert.DoesNotContain("mutated-hint", report);
        Assert.Contains(nameof(RecordFromCaller), report);
        Assert.DoesNotContain("GenerateReport", report);
        tracker.Close(resource);
    }

    [Fact]
    public void ClosingDoesNotAcquireTheTrackedObjectsMonitor()
    {
        var detector = new Detector();
        object resource = new();
        var tracker = detector.TrackForcibly(resource);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() => { lock (resource) { entered.Set(); release.Wait(); } }) { IsBackground = true };
        holder.Start();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Task<bool> close = Task.Run(() => tracker.Close(resource));
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
        var tracker = detector.TrackForcibly(resource);
        int successes = 0;
        Parallel.For(0, 32, _ => { if (tracker.Close(resource)) Interlocked.Increment(ref successes); });
        Assert.Equal(1, successes);
        Assert.Equal(ResourceLeakDetector.TRACK_CLOSE, tracker.GetCloseStackTraceIfAny() != null);
    }

    private sealed class BlockingHint(ManualResetEventSlim entered, ManualResetEventSlim release) : IResourceLeakHint
    {
        public string ToHintString() { entered.Set(); release.Wait(); return "concurrent record"; }
    }
    [Fact]
    public void RecordThatStartedBeforeCloseCannotOverwriteCloseMarker()
    {
        var detector = new Detector();
        object resource = new();
        var tracker = detector.TrackForcibly(resource);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task record = Task.Run(() => tracker.Record(new BlockingHint(entered, release)));
        Exception close = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            tracker.Close(resource);
            close = tracker.GetCloseStackTraceIfAny();
        }
        finally { release.Set(); Assert.True(record.Wait(TimeSpan.FromSeconds(5))); }
        Assert.Same(close, tracker.GetCloseStackTraceIfAny());
    }

    [Fact]
    public void DuplicateCreationReportsAreSuppressedAndDisabledReportingDrainsNotifications()
    {
        var detector = new Detector();
        detector.SetLeakListener(detector);
        IResourceLeakTracker<object> first = null;
        for (int i = 0; i < 2; i++) first = CreateLeak(detector, out _);
        CollectUntil(detector.Flush, () => detector.Reports.Count == 1);
        GC.Collect(); GC.WaitForPendingFinalizers(); detector.Flush();
        Assert.Single(detector.Reports);
        Assert.Equal(1, detector.ListenerCalls);
        detector.Reporting = false;
        var discarded = CreateLeak(detector, out var resource);
        CollectUntil(detector.Flush, () => IsCollected(resource));
        GC.Collect(); GC.WaitForPendingFinalizers(); detector.Flush();
        Assert.False(((IResourceLeak)discarded).Close());
        Assert.NotEmpty(discarded.ToString());
        detector.Reporting = true;
        detector.Flush();
        Assert.Single(detector.Reports);
        GC.KeepAlive(first); GC.KeepAlive(discarded);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateTwoTrackers(Detector detector)
    {
        object resource = new();
        detector.Hint = "first-tracker";
        detector.TrackForcibly(resource);
        detector.Hint = "second-tracker";
        detector.TrackForcibly(resource);
    }
    [Fact]
    public void OneResourceCanNotifyMultipleTrackersAfterTheirReferentIsCollected()
    {
        var detector = new Detector();
        CreateTwoTrackers(detector);
        CollectUntil(detector.Flush, () => detector.Reports.Count == 2);
        Assert.Contains(detector.Reports, report => report.Contains("first-tracker"));
        Assert.Contains(detector.Reports, report => report.Contains("second-tracker"));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IResourceLeakTracker<object> CloseAndRetrack(Detector detector, out WeakReference<object> weak)
    {
        object resource = new();
        weak = new WeakReference<object>(resource);
        detector.Hint = "closed-registration";
        Assert.True(detector.TrackForcibly(resource).Close(resource));
        detector.Hint = "live-registration";
        var live = detector.TrackForcibly(resource);
        detector.Hint = "cancelled-sibling";
        Assert.True(detector.TrackForcibly(resource).Close(resource));
        return live;
    }

    [Fact]
    public void RetrackingAfterCloseRearmsCollectionAndCancellingOneSiblingKeepsTheOther()
    {
        var detector = new Detector();
        var tracker = CloseAndRetrack(detector, out var resource);
        CollectUntil(detector.Flush, () => detector.Reports.Count == 1);
        Assert.True(IsCollected(resource));
        Assert.Contains("live-registration", detector.Reports[0]);
        Assert.DoesNotContain("closed-registration", detector.Reports[0]);
        Assert.DoesNotContain("cancelled-sibling", detector.Reports[0]);
        Assert.False(((IResourceLeak)tracker).Close());
        GC.KeepAlive(tracker);
    }

    private sealed class ExcludedAccessHelper
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Record(IResourceLeakTracker<object> tracker) => tracker.Record();
    }

    private sealed class GenericExcludedAccessHelper<T>
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Record(IResourceLeakTracker<object> tracker) => tracker.Record();
    }

    [Fact]
    public void ExclusionsUseDeclaredMethodsAndFullyQualifiedClrNamesIncludingNestedTypes()
    {
        var detector = new Detector();
        object resource = new();
        var tracker = detector.TrackForcibly(resource);
        ExcludedAccessHelper.Record(tracker);
        string displayedType = typeof(ExcludedAccessHelper).FullName.Replace('+', '.');
        Assert.Contains(displayedType, tracker.ToString());
        Assert.Throws<ArgumentException>(() => ResourceLeakDetector.AddExclusions(typeof(ExcludedAccessHelper), "missing"));
        Assert.Throws<ArgumentException>(() => ResourceLeakDetector.AddExclusions(typeof(ExcludedAccessHelper), nameof(ToString)));
        ResourceLeakDetector.AddExclusions(typeof(ExcludedAccessHelper), nameof(ExcludedAccessHelper.Record));
        Assert.DoesNotContain(displayedType, tracker.ToString());
        Assert.Contains(nameof(ExclusionsUseDeclaredMethodsAndFullyQualifiedClrNamesIncludingNestedTypes), tracker.ToString());
        GenericExcludedAccessHelper<object>.Record(tracker);
        GenericExcludedAccessHelper<string>.Record(tracker);
        Assert.Contains("GenericExcludedAccessHelper", tracker.ToString());
        ResourceLeakDetector.AddExclusions(typeof(GenericExcludedAccessHelper<object>), nameof(GenericExcludedAccessHelper<object>.Record));
        Assert.DoesNotContain("GenericExcludedAccessHelper", tracker.ToString());
        Assert.True(tracker.Close(resource));
    }
}
