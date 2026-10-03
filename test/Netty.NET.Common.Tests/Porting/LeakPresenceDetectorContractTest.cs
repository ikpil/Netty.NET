using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Xunit;
using ResourceScope = Netty.NET.Common.LeakPresenceDetector.ResourceScope;

namespace Netty.NET.Common.Tests.Porting;

[Collection("Leak detector globals")]
public class LeakPresenceDetectorContractTest
{
    private sealed class Scoped<T>(ResourceScope scope) : LeakPresenceDetector<T>(typeof(T)) where T : class
    {
        protected override ResourceScope CurrentScope() => scope;
    }
    private sealed class ScopedFactory(ResourceScope scope) : ResourceLeakDetectorFactory
    {
        public override ResourceLeakDetector<T> NewResourceLeakDetector<T>(Type resource, int samplingInterval, long maxActive)
            => new Scoped<T>(scope);
    }
    private static void WithFactory(ResourceLeakDetectorFactory factory, Action action)
    {
        var previous = ResourceLeakDetectorFactory.Instance();
        try { ResourceLeakDetectorFactory.SetResourceLeakDetectorFactory(factory); action(); }
        finally { ResourceLeakDetectorFactory.SetResourceLeakDetectorFactory(previous); }
    }
    private sealed class ThrowingHint : IResourceLeakHint
    {
        public string ToHintString() => throw new InvalidOperationException("record should be a no-op");
    }

    [Fact]
    public void GlobalScopeIsSharedAcrossClrGenericResourceTypes()
    {
        var objectDetector = new LeakPresenceDetector<object>(typeof(object));
        var stringDetector = new LeakPresenceDetector<string>(typeof(string));
        Assert.Same(objectDetector.ScopeForCheck(), stringDetector.ScopeForCheck());
        object resource = new();
        var tracker = objectDetector.Track(resource);
        Assert.True(stringDetector.ScopeForCheck().HasOpenResources());
        Assert.True(tracker.Close(resource));
        stringDetector.ScopeForCheck().Check();
    }

    [Fact]
    public void DisabledLevelAndSamplingDoNotDisablePresenceCountingOrEnableRecording()
    {
        var previous = ResourceLeakDetector.GetLevel();
        using var scope = new ResourceScope("count-all");
        var detector = new Scoped<object>(scope);
        try
        {
            ResourceLeakDetector.SetLevel(ResourceLeakDetectorLevel.DISABLED);
            object resource = new();
            var tracker = detector.Track(resource);
            Assert.NotNull(tracker);
            Assert.True(scope.HasOpenResources());
            Assert.False(detector.IsRecordEnabled());
            tracker.Record(); tracker.Record(new ThrowingHint());
            Assert.Null(tracker.GetCloseStackTraceIfAny());
            Assert.True(tracker.Close(null));
            Assert.False(tracker.Close(resource));
            // This implementation counts trackers; it need not dereference the object.
            var nullTracker = detector.TrackForcibly(null);
            Assert.True(nullTracker.Close(null));
            scope.Check();
        }
        finally { ResourceLeakDetector.SetLevel(previous); }
    }

    [Fact]
    public void CheckReportsLiveResourcesImmediatelyAndLateReleaseProducesNegativeCount()
    {
        using var scope = new ResourceScope("live-resource");
        var detector = new Scoped<object>(scope);
        object resource = new();
        var tracker = detector.Track(resource);
        var leak = Assert.Throws<InvalidOperationException>(scope.Check);
        Assert.Contains("live-resource", leak.Message);
        Assert.False(scope.HasOpenResources());
        Assert.True(tracker.Close(resource));
        var late = Assert.Throws<InvalidOperationException>(scope.Check);
        Assert.Contains("Resource count was negative", late.Message);
        scope.Check();
        GC.KeepAlive(resource);
    }

    [Fact]
    public void DisposeIsIdempotentAndNeverReopensAScope()
    {
        var scope = new ResourceScope("closed");
        scope.Dispose(); scope.Close(); scope.Dispose();
        var detector = new Scoped<object>(scope);
        var failure = Assert.Throws<LeakPresenceDetector.AllocationProhibitedException>(() => detector.Track(new object()));
        Assert.Contains("already closed", failure.Message);
        Assert.False(scope.HasOpenResources());
    }

    [Fact]
    public void LeakingScopeClosesEvenWhenItsFirstCloseThrowsAndLateReleaseOnlySucceedsOnce()
    {
        var scope = new ResourceScope("closing-leak");
        var detector = new Scoped<object>(scope);
        object resource = new();
        var tracker = detector.Track(resource);
        Assert.Throws<InvalidOperationException>(scope.Close);
        scope.Dispose();
        Assert.Throws<LeakPresenceDetector.AllocationProhibitedException>(() => detector.Track(resource));
        Assert.Throws<LeakPresenceDetector.AllocationProhibitedException>(() => tracker.Close(resource));
        Assert.False(tracker.Close(resource));
        Assert.Throws<InvalidOperationException>(scope.Check);
    }

    [Fact]
    public void DifferentScopesAreIndependentAndStaticCheckSelectsCurrentFactoryScope()
    {
        using var first = new ResourceScope("first");
        using var second = new ResourceScope("second");
        var tracker = new Scoped<object>(first).Track(new object());
        WithFactory(new ScopedFactory(second), LeakPresenceDetector.Check);
        WithFactory(new ScopedFactory(first), () => Assert.Throws<InvalidOperationException>(LeakPresenceDetector.Check));
        Assert.True(tracker.Close(null));
        Assert.Throws<InvalidOperationException>(first.Check);
    }

    [Fact]
    public void StaticCheckRequiresPresenceProviderAndGenericFactoryCanLoadIt()
    {
        string key = "io.netty.customResourceLeakDetector";
        string previous = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, null);
            WithFactory(new DefaultResourceLeakDetectorFactory(), () => Assert.Throws<InvalidOperationException>(LeakPresenceDetector.Check));
            Environment.SetEnvironmentVariable(key, typeof(LeakPresenceDetector<>).AssemblyQualifiedName);
            var factory = new DefaultResourceLeakDetectorFactory();
            Assert.IsType<LeakPresenceDetector<object>>(factory.NewResourceLeakDetector<object>(typeof(object), -1));
            Assert.IsType<LeakPresenceDetector<string>>(factory.NewResourceLeakDetector<string>(typeof(string), 0, -1));
            WithFactory(factory, LeakPresenceDetector.Check);
        }
        finally { Environment.SetEnvironmentVariable(key, previous); }
    }

    [Fact]
    public void ConcurrentCountingAndCloseRetainExactlyOnceSemantics()
    {
        using var scope = new ResourceScope("concurrent");
        var detector = new Scoped<object>(scope);
        Parallel.For(0, 8, _ =>
        {
            for (int i = 0; i < 10000; i++)
            {
                object resource = new();
                Assert.True(detector.Track(resource).Close(resource));
            }
        });
        object shared = new();
        var tracker = detector.Track(shared);
        int successes = 0;
        Parallel.For(0, 32, _ => { if (tracker.Close(shared)) Interlocked.Increment(ref successes); });
        Assert.Equal(1, successes);
        scope.Check();
        Assert.False(scope.HasOpenResources());
    }

    [Fact]
    public void StaticInitializerWrapperRejectsOrdinaryCallers()
        => Assert.Throws<InvalidOperationException>(() => LeakPresenceDetector.StaticInitializer(() => new object()));

    private static ResourceScope initializerScope;
    private static IResourceLeakTracker<object> concurrentTracker;
    private static class InitializerProbe
    {
        internal static readonly bool Skipped;
        internal static readonly bool Forced;
        static InitializerProbe()
        {
            var detector = new Scoped<object>(initializerScope);
            Skipped = LeakPresenceDetector.StaticInitializer(() =>
            {
                Assert.Null(detector.Track(new object()));
                var forced = detector.TrackForcibly(new object());
                Assert.True(forced.Close(null));
                var other = new Thread(() => concurrentTracker = detector.Track(new object()));
                other.Start(); Assert.True(other.Join(TimeSpan.FromSeconds(5)));
                return true;
            });
            Forced = LeakPresenceDetector.StaticInitializer(() => LeakPresenceDetector.StaticInitializer(() => true));
            Assert.Throws<InvalidOperationException>(() => LeakPresenceDetector.StaticInitializer<object>(() => throw new InvalidOperationException("supplier")));
        }
    }
    [Fact]
    public void WrapperSkipsOnlyStaticInitializationAndRestoresItsCountAfterNestedAndThrowingSuppliers()
    {
        using var scope = new ResourceScope("initializer");
        initializerScope = scope;
        Assert.True(InitializerProbe.Skipped);
        Assert.True(InitializerProbe.Forced);
        Assert.NotNull(concurrentTracker);
        Assert.True(concurrentTracker.Close(null));
        var tracker = new Scoped<object>(scope).Track(new object());
        Assert.NotNull(tracker);
        Assert.True(tracker.Close(null));
        scope.Check();
        Assert.False(LeakPresenceDetector.InStaticInitializerFast());
        initializerScope = null; concurrentTracker = null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IResourceLeakTracker<object> CreateDiagnostic(Scoped<object> detector) => detector.Track(new object());

    private static ResourceScope diagnosticInitializerScope;
    private static class UnwrappedInitializer
    {
        internal static readonly IResourceLeakTracker<object> Tracker;
        static UnwrappedInitializer() => Tracker = new Scoped<object>(diagnosticInitializerScope).Track(new object());
    }
    [Fact]
    public void OptionalDiagnosticsIdentifyAnUnwrappedStaticInitializer()
    {
        using var scope = new ResourceScope("unwrapped-initializer");
        diagnosticInitializerScope = scope;
        var tracker = UnwrappedInitializer.Tracker;
        Assert.NotNull(tracker);
        var failure = Assert.Throws<InvalidOperationException>(scope.Check);
        Exception[] creation = ThrowableUtil.GetSuppressed(failure);
        if (LeakPresenceDetector.TRACK_CREATION_STACK)
        {
            Assert.Single(creation);
            Assert.Contains("Resource created in static initializer", creation[0].Message);
            Assert.Contains("LeakPresenceDetector.staticInitializer", creation[0].Message);
        }
        else Assert.Empty(creation);
        Assert.True(tracker.Close(null));
        Assert.Throws<InvalidOperationException>(scope.Check);
        diagnosticInitializerScope = null;
    }

    [Fact]
    public void OptionalCreationDiagnosticsRetainCallerAndCapSuppressedStacksAtSeven()
    {
        using var scope = new ResourceScope("diagnostic");
        var detector = new Scoped<object>(scope);
        var trackers = new List<IResourceLeakTracker<object>>();
        for (int i = 0; i < 10; i++) trackers.Add(CreateDiagnostic(detector));
        var leak = Assert.Throws<InvalidOperationException>(scope.Check);
        Exception[] creation = ThrowableUtil.GetSuppressed(leak);
        if (LeakPresenceDetector.TRACK_CREATION_STACK)
        {
            Assert.Equal(7, creation.Length);
            Assert.All(creation, entry =>
            {
                Assert.Contains(nameof(CreateDiagnostic), entry.StackTrace);
                Assert.Contains("Resource created outside static initializer", entry.Message);
            });
        }
        else
        {
            Assert.Empty(creation);
            Assert.Contains("trackCreationStack=true", leak.Message);
        }
        foreach (var tracker in trackers) Assert.True(tracker.Close(null));
        Assert.Throws<InvalidOperationException>(scope.Check);
    }
}
