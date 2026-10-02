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
        protected override ResourceScope currentScope() => scope;
    }
    private sealed class ScopedFactory(ResourceScope scope) : ResourceLeakDetectorFactory
    {
        public override ResourceLeakDetector<T> newResourceLeakDetector<T>(Type resource, int samplingInterval, long maxActive)
            => new Scoped<T>(scope);
    }
    private static void withFactory(ResourceLeakDetectorFactory factory, Action action)
    {
        var previous = ResourceLeakDetectorFactory.instance();
        try { ResourceLeakDetectorFactory.setResourceLeakDetectorFactory(factory); action(); }
        finally { ResourceLeakDetectorFactory.setResourceLeakDetectorFactory(previous); }
    }
    private sealed class ThrowingHint : IResourceLeakHint
    {
        public string toHintString() => throw new InvalidOperationException("record should be a no-op");
    }

    [Fact]
    public void GlobalScopeIsSharedAcrossClrGenericResourceTypes()
    {
        var objectDetector = new LeakPresenceDetector<object>(typeof(object));
        var stringDetector = new LeakPresenceDetector<string>(typeof(string));
        Assert.Same(objectDetector.scopeForCheck(), stringDetector.scopeForCheck());
        object resource = new();
        var tracker = objectDetector.track(resource);
        Assert.True(stringDetector.scopeForCheck().hasOpenResources());
        Assert.True(tracker.close(resource));
        stringDetector.scopeForCheck().check();
    }

    [Fact]
    public void DisabledLevelAndSamplingDoNotDisablePresenceCountingOrEnableRecording()
    {
        var previous = ResourceLeakDetector.getLevel();
        using var scope = new ResourceScope("count-all");
        var detector = new Scoped<object>(scope);
        try
        {
            ResourceLeakDetector.setLevel(ResourceLeakDetectorLevel.DISABLED);
            object resource = new();
            var tracker = detector.track(resource);
            Assert.NotNull(tracker);
            Assert.True(scope.hasOpenResources());
            Assert.False(detector.isRecordEnabled());
            tracker.record(); tracker.record(new ThrowingHint());
            Assert.Null(tracker.getCloseStackTraceIfAny());
            Assert.True(tracker.close(null));
            Assert.False(tracker.close(resource));
            // This implementation counts trackers; it need not dereference the object.
            var nullTracker = detector.trackForcibly(null);
            Assert.True(nullTracker.close(null));
            scope.check();
        }
        finally { ResourceLeakDetector.setLevel(previous); }
    }

    [Fact]
    public void CheckReportsLiveResourcesImmediatelyAndLateReleaseProducesNegativeCount()
    {
        using var scope = new ResourceScope("live-resource");
        var detector = new Scoped<object>(scope);
        object resource = new();
        var tracker = detector.track(resource);
        var leak = Assert.Throws<InvalidOperationException>(scope.check);
        Assert.Contains("live-resource", leak.Message);
        Assert.False(scope.hasOpenResources());
        Assert.True(tracker.close(resource));
        var late = Assert.Throws<InvalidOperationException>(scope.check);
        Assert.Contains("Resource count was negative", late.Message);
        scope.check();
        GC.KeepAlive(resource);
    }

    [Fact]
    public void DisposeIsIdempotentAndNeverReopensAScope()
    {
        var scope = new ResourceScope("closed");
        scope.Dispose(); scope.close(); scope.Dispose();
        var detector = new Scoped<object>(scope);
        var failure = Assert.Throws<LeakPresenceDetector.AllocationProhibitedException>(() => detector.track(new object()));
        Assert.Contains("already closed", failure.Message);
        Assert.False(scope.hasOpenResources());
    }

    [Fact]
    public void LeakingScopeClosesEvenWhenItsFirstCloseThrowsAndLateReleaseOnlySucceedsOnce()
    {
        var scope = new ResourceScope("closing-leak");
        var detector = new Scoped<object>(scope);
        object resource = new();
        var tracker = detector.track(resource);
        Assert.Throws<InvalidOperationException>(scope.close);
        scope.Dispose();
        Assert.Throws<LeakPresenceDetector.AllocationProhibitedException>(() => detector.track(resource));
        Assert.Throws<LeakPresenceDetector.AllocationProhibitedException>(() => tracker.close(resource));
        Assert.False(tracker.close(resource));
        Assert.Throws<InvalidOperationException>(scope.check);
    }

    [Fact]
    public void DifferentScopesAreIndependentAndStaticCheckSelectsCurrentFactoryScope()
    {
        using var first = new ResourceScope("first");
        using var second = new ResourceScope("second");
        var tracker = new Scoped<object>(first).track(new object());
        withFactory(new ScopedFactory(second), LeakPresenceDetector.check);
        withFactory(new ScopedFactory(first), () => Assert.Throws<InvalidOperationException>(LeakPresenceDetector.check));
        Assert.True(tracker.close(null));
        Assert.Throws<InvalidOperationException>(first.check);
    }

    [Fact]
    public void StaticCheckRequiresPresenceProviderAndGenericFactoryCanLoadIt()
    {
        string key = "io.netty.customResourceLeakDetector";
        string previous = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, null);
            withFactory(new DefaultResourceLeakDetectorFactory(), () => Assert.Throws<InvalidOperationException>(LeakPresenceDetector.check));
            Environment.SetEnvironmentVariable(key, typeof(LeakPresenceDetector<>).AssemblyQualifiedName);
            var factory = new DefaultResourceLeakDetectorFactory();
            Assert.IsType<LeakPresenceDetector<object>>(factory.newResourceLeakDetector<object>(typeof(object), -1));
            Assert.IsType<LeakPresenceDetector<string>>(factory.newResourceLeakDetector<string>(typeof(string), 0, -1));
            withFactory(factory, LeakPresenceDetector.check);
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
                Assert.True(detector.track(resource).close(resource));
            }
        });
        object shared = new();
        var tracker = detector.track(shared);
        int successes = 0;
        Parallel.For(0, 32, _ => { if (tracker.close(shared)) Interlocked.Increment(ref successes); });
        Assert.Equal(1, successes);
        scope.check();
        Assert.False(scope.hasOpenResources());
    }

    [Fact]
    public void StaticInitializerWrapperRejectsOrdinaryCallers()
        => Assert.Throws<InvalidOperationException>(() => LeakPresenceDetector.staticInitializer(() => new object()));

    private static ResourceScope initializerScope;
    private static IResourceLeakTracker<object> concurrentTracker;
    private static class InitializerProbe
    {
        internal static readonly bool Skipped;
        internal static readonly bool Forced;
        static InitializerProbe()
        {
            var detector = new Scoped<object>(initializerScope);
            Skipped = LeakPresenceDetector.staticInitializer(() =>
            {
                Assert.Null(detector.track(new object()));
                var forced = detector.trackForcibly(new object());
                Assert.True(forced.close(null));
                var other = new Thread(() => concurrentTracker = detector.track(new object()));
                other.Start(); Assert.True(other.Join(TimeSpan.FromSeconds(5)));
                return true;
            });
            Forced = LeakPresenceDetector.staticInitializer(() => LeakPresenceDetector.staticInitializer(() => true));
            Assert.Throws<InvalidOperationException>(() => LeakPresenceDetector.staticInitializer<object>(() => throw new InvalidOperationException("supplier")));
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
        Assert.True(concurrentTracker.close(null));
        var tracker = new Scoped<object>(scope).track(new object());
        Assert.NotNull(tracker);
        Assert.True(tracker.close(null));
        scope.check();
        Assert.False(LeakPresenceDetector.inStaticInitializerFast());
        initializerScope = null; concurrentTracker = null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IResourceLeakTracker<object> createDiagnostic(Scoped<object> detector) => detector.track(new object());

    private static ResourceScope diagnosticInitializerScope;
    private static class UnwrappedInitializer
    {
        internal static readonly IResourceLeakTracker<object> Tracker;
        static UnwrappedInitializer() => Tracker = new Scoped<object>(diagnosticInitializerScope).track(new object());
    }
    [Fact]
    public void OptionalDiagnosticsIdentifyAnUnwrappedStaticInitializer()
    {
        using var scope = new ResourceScope("unwrapped-initializer");
        diagnosticInitializerScope = scope;
        var tracker = UnwrappedInitializer.Tracker;
        Assert.NotNull(tracker);
        var failure = Assert.Throws<InvalidOperationException>(scope.check);
        Exception[] creation = ThrowableUtil.getSuppressed(failure);
        if (LeakPresenceDetector.TRACK_CREATION_STACK)
        {
            Assert.Single(creation);
            Assert.Contains("Resource created in static initializer", creation[0].Message);
            Assert.Contains("LeakPresenceDetector.staticInitializer", creation[0].Message);
        }
        else Assert.Empty(creation);
        Assert.True(tracker.close(null));
        Assert.Throws<InvalidOperationException>(scope.check);
        diagnosticInitializerScope = null;
    }

    [Fact]
    public void OptionalCreationDiagnosticsRetainCallerAndCapSuppressedStacksAtSeven()
    {
        using var scope = new ResourceScope("diagnostic");
        var detector = new Scoped<object>(scope);
        var trackers = new List<IResourceLeakTracker<object>>();
        for (int i = 0; i < 10; i++) trackers.Add(createDiagnostic(detector));
        var leak = Assert.Throws<InvalidOperationException>(scope.check);
        Exception[] creation = ThrowableUtil.getSuppressed(leak);
        if (LeakPresenceDetector.TRACK_CREATION_STACK)
        {
            Assert.Equal(7, creation.Length);
            Assert.All(creation, entry =>
            {
                Assert.Contains(nameof(createDiagnostic), entry.StackTrace);
                Assert.Contains("Resource created outside static initializer", entry.Message);
            });
        }
        else
        {
            Assert.Empty(creation);
            Assert.Contains("trackCreationStack=true", leak.Message);
        }
        foreach (var tracker in trackers) Assert.True(tracker.close(null));
        Assert.Throws<InvalidOperationException>(scope.check);
    }
}
