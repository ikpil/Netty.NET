using System;
using System.Threading;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

[Collection("Thread death watcher globals")]
public class ReferenceCountUtilContractTest : IDisposable
{
    public void Dispose() => Assert.True(ThreadDeathWatcher.awaitInactivity(TimeSpan.FromSeconds(5)));

    [Fact]
    public void NonCountedValuesAndNullPassThroughWithoutCreatingOwnership()
    {
        object value = new();
        Assert.Same(value, ReferenceCountUtil.retain(value));
        Assert.Same(value, ReferenceCountUtil.retain(value, 2));
        Assert.Same(value, ReferenceCountUtil.touch(value));
        Assert.Same(value, ReferenceCountUtil.touch(value, new object()));
        Assert.Same(value, ReferenceCountUtil.releaseLater(value));
        Assert.Same(value, ReferenceCountUtil.releaseLater(value, 2));
        Assert.False(ReferenceCountUtil.release(value));
        Assert.False(ReferenceCountUtil.release(value, 2));
        Assert.Equal(-1, ReferenceCountUtil.refCnt(value));
        Assert.Null(ReferenceCountUtil.retain<object>(null));
        Assert.Null(ReferenceCountUtil.touch<object>(null, null));
        Assert.Null(ReferenceCountUtil.releaseLater<object>(null));
        Assert.False(ReferenceCountUtil.release(null));
        Assert.Equal(-1, ReferenceCountUtil.refCnt(null));
        Assert.Equal(3, ReferenceCountUtil.retain(3));
        ReferenceCountUtil.safeRelease(value); ReferenceCountUtil.safeRelease(null);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ExplicitCountsAreValidatedBeforeTestingTheObjectType(int count)
    {
        Assert.Throws<ArgumentException>(() => ReferenceCountUtil.retain<object>(null, count));
        Assert.Throws<ArgumentException>(() => ReferenceCountUtil.release(null, count));
        Assert.Throws<ArgumentException>(() => ReferenceCountUtil.releaseLater<object>(null, count));
        ReferenceCountUtil.safeRelease(null, count);
    }

    private sealed class Stub : IReferenceCounted
    {
        internal IReferenceCounted Result;
        internal int Increment;
        internal int Decrement;
        internal object Hint;
        internal Exception Failure;
        internal int Count = 4;
        public int refCnt() => Count;
        public IReferenceCounted retain() { Increment = 1; return Result ?? this; }
        public IReferenceCounted retain(int increment) { Increment = increment; return Result ?? this; }
        public IReferenceCounted touch() { Hint = null; return Result ?? this; }
        public IReferenceCounted touch(object hint) { Hint = hint; return Result ?? this; }
        public bool release() => release(1);
        public bool release(int decrement)
        {
            Decrement = decrement;
            if (Failure != null) throw Failure;
            Count -= decrement;
            return Count == 0;
        }
    }
    [Fact]
    public void RetainAndTouchForwardReturnValuesAndHintIdentityWhileReleaseForwardsCounts()
    {
        var result = new Stub();
        var value = new Stub { Result = result };
        Assert.Same(result, ReferenceCountUtil.retain(value));
        Assert.Equal(1, value.Increment);
        Assert.Same(result, ReferenceCountUtil.retain(value, 3));
        Assert.Equal(3, value.Increment);
        Assert.Same(result, ReferenceCountUtil.touch(value));
        Assert.Null(value.Hint);
        object hint = new();
        Assert.Same(result, ReferenceCountUtil.touch(value, hint));
        Assert.Same(hint, value.Hint);
        Assert.Equal(4, ReferenceCountUtil.refCnt(value));
        Assert.False(ReferenceCountUtil.release(value));
        Assert.Equal(1, value.Decrement);
        Assert.True(ReferenceCountUtil.release(value, 3));
        Assert.Equal(3, value.Decrement);
    }

    [Fact]
    public void OrdinaryReleasePropagatesTheFailureAndSafeReleaseSwallowsIt()
    {
        var failure = new InvalidOperationException("release failure");
        var value = new Stub { Failure = failure };
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => ReferenceCountUtil.release(value)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => ReferenceCountUtil.release(value, 2)));
        ReferenceCountUtil.safeRelease(value);
        ReferenceCountUtil.safeRelease(value, 2);
        Assert.Equal(2, value.Decrement);
        ReferenceCountUtil.safeRelease(value, 0);
        Assert.Equal(2, value.Decrement);
    }

    private sealed class Counted : AbstractReferenceCounted
    {
        internal int Deallocations;
        internal Thread Deallocator;
        public override IReferenceCounted touch(object hint) => this;
        protected override void deallocate() { Deallocator = Thread.CurrentThread; Interlocked.Increment(ref Deallocations); }
    }
    [Fact]
    public void ReleaseLaterRunsOnlyAfterCallerTerminatesAndPreservesRequestedDecrements()
    {
        var first = new Counted();
        var second = new Counted();
        second.retain(2);
        using var queued = new ManualResetEventSlim();
        using var stop = new ManualResetEventSlim();
        Exception error = null;
        var caller = new Thread(() =>
        {
            try
            {
                Assert.Same(first, ReferenceCountUtil.releaseLater(first));
                Assert.Same(second, ReferenceCountUtil.releaseLater(second, 2));
                queued.Set(); stop.Wait();
            }
            catch (Exception failure) { error = failure; queued.Set(); }
        }) { IsBackground = true };
        caller.Start();
        try
        {
            Assert.True(queued.Wait(TimeSpan.FromSeconds(5)));
            Assert.Null(error);
            Assert.Equal(1, first.refCnt());
            Assert.Equal(3, second.refCnt());
            Assert.Equal(0, first.Deallocations);
        }
        finally { stop.Set(); Assert.True(caller.Join(TimeSpan.FromSeconds(5))); }
        Assert.True(ThreadDeathWatcher.awaitInactivity(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, first.refCnt());
        Assert.Equal(1, first.Deallocations);
        Assert.NotSame(caller, first.Deallocator);
        Assert.Contains("threadDeathWatcher", first.Deallocator.Name);
        Assert.Equal(1, second.refCnt());
        Assert.Equal(0, second.Deallocations);
        Assert.True(second.release());
    }

    [Fact]
    public void DeferredReleaseFailureDoesNotPreventOtherDeferredReleases()
    {
        var bad = new Stub { Failure = new InvalidOperationException("deferred failure") };
        var good = new Counted();
        var caller = new Thread(() => { ReferenceCountUtil.releaseLater(bad); ReferenceCountUtil.releaseLater(good); }) { IsBackground = true };
        caller.Start(); Assert.True(caller.Join(TimeSpan.FromSeconds(5)));
        Assert.True(ThreadDeathWatcher.awaitInactivity(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, bad.Decrement);
        Assert.Equal(1, good.Deallocations);
    }
}
