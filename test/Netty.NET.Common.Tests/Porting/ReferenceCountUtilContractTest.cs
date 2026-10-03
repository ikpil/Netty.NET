using System;
using System.Threading;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

[Collection("Thread death watcher globals")]
public class ReferenceCountUtilContractTest : IDisposable
{
    public void Dispose() => Assert.True(ThreadDeathWatcher.AwaitInactivity(TimeSpan.FromSeconds(5)));

    [Fact]
    public void NonCountedValuesAndNullPassThroughWithoutCreatingOwnership()
    {
        object value = new();
        Assert.Same(value, ReferenceCountUtil.Retain(value));
        Assert.Same(value, ReferenceCountUtil.Retain(value, 2));
        Assert.Same(value, ReferenceCountUtil.Touch(value));
        Assert.Same(value, ReferenceCountUtil.Touch(value, new object()));
        Assert.Same(value, ReferenceCountUtil.ReleaseLater(value));
        Assert.Same(value, ReferenceCountUtil.ReleaseLater(value, 2));
        Assert.False(ReferenceCountUtil.Release(value));
        Assert.False(ReferenceCountUtil.Release(value, 2));
        Assert.Equal(-1, ReferenceCountUtil.RefCnt(value));
        Assert.Null(ReferenceCountUtil.Retain<object>(null));
        Assert.Null(ReferenceCountUtil.Touch<object>(null, null));
        Assert.Null(ReferenceCountUtil.ReleaseLater<object>(null));
        Assert.False(ReferenceCountUtil.Release(null));
        Assert.Equal(-1, ReferenceCountUtil.RefCnt(null));
        Assert.Equal(3, ReferenceCountUtil.Retain(3));
        ReferenceCountUtil.SafeRelease(value); ReferenceCountUtil.SafeRelease(null);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ExplicitCountsAreValidatedBeforeTestingTheObjectType(int count)
    {
        Assert.Throws<ArgumentException>(() => ReferenceCountUtil.Retain<object>(null, count));
        Assert.Throws<ArgumentException>(() => ReferenceCountUtil.Release(null, count));
        Assert.Throws<ArgumentException>(() => ReferenceCountUtil.ReleaseLater<object>(null, count));
        ReferenceCountUtil.SafeRelease(null, count);
    }

    private sealed class Stub : IReferenceCounted
    {
        internal IReferenceCounted Result;
        internal int Increment;
        internal int Decrement;
        internal object Hint;
        internal Exception Failure;
        internal int Count = 4;
        public int RefCnt() => Count;
        public IReferenceCounted Retain() { Increment = 1; return Result ?? this; }
        public IReferenceCounted Retain(int increment) { Increment = increment; return Result ?? this; }
        public IReferenceCounted Touch() { Hint = null; return Result ?? this; }
        public IReferenceCounted Touch(object hint) { Hint = hint; return Result ?? this; }
        public bool Release() => Release(1);
        public bool Release(int decrement)
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
        Assert.Same(result, ReferenceCountUtil.Retain(value));
        Assert.Equal(1, value.Increment);
        Assert.Same(result, ReferenceCountUtil.Retain(value, 3));
        Assert.Equal(3, value.Increment);
        Assert.Same(result, ReferenceCountUtil.Touch(value));
        Assert.Null(value.Hint);
        object hint = new();
        Assert.Same(result, ReferenceCountUtil.Touch(value, hint));
        Assert.Same(hint, value.Hint);
        Assert.Equal(4, ReferenceCountUtil.RefCnt(value));
        Assert.False(ReferenceCountUtil.Release(value));
        Assert.Equal(1, value.Decrement);
        Assert.True(ReferenceCountUtil.Release(value, 3));
        Assert.Equal(3, value.Decrement);
    }

    [Fact]
    public void OrdinaryReleasePropagatesTheFailureAndSafeReleaseSwallowsIt()
    {
        var failure = new InvalidOperationException("release failure");
        var value = new Stub { Failure = failure };
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => ReferenceCountUtil.Release(value)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => ReferenceCountUtil.Release(value, 2)));
        ReferenceCountUtil.SafeRelease(value);
        ReferenceCountUtil.SafeRelease(value, 2);
        Assert.Equal(2, value.Decrement);
        ReferenceCountUtil.SafeRelease(value, 0);
        Assert.Equal(2, value.Decrement);
    }

    private sealed class Counted : AbstractReferenceCounted
    {
        internal int Deallocations;
        internal Thread Deallocator;
        public override IReferenceCounted Touch(object hint) => this;
        protected override void Deallocate() { Deallocator = Thread.CurrentThread; Interlocked.Increment(ref Deallocations); }
    }
    [Fact]
    public void ReleaseLaterRunsOnlyAfterCallerTerminatesAndPreservesRequestedDecrements()
    {
        var first = new Counted();
        var second = new Counted();
        second.Retain(2);
        using var queued = new ManualResetEventSlim();
        using var stop = new ManualResetEventSlim();
        Exception error = null;
        var caller = new Thread(() =>
        {
            try
            {
                Assert.Same(first, ReferenceCountUtil.ReleaseLater(first));
                Assert.Same(second, ReferenceCountUtil.ReleaseLater(second, 2));
                queued.Set(); stop.Wait();
            }
            catch (Exception failure) { error = failure; queued.Set(); }
        }) { IsBackground = true };
        caller.Start();
        try
        {
            Assert.True(queued.Wait(TimeSpan.FromSeconds(5)));
            Assert.Null(error);
            Assert.Equal(1, first.RefCnt());
            Assert.Equal(3, second.RefCnt());
            Assert.Equal(0, first.Deallocations);
        }
        finally { stop.Set(); Assert.True(caller.Join(TimeSpan.FromSeconds(5))); }
        Assert.True(ThreadDeathWatcher.AwaitInactivity(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, first.RefCnt());
        Assert.Equal(1, first.Deallocations);
        Assert.NotSame(caller, first.Deallocator);
        Assert.Contains("threadDeathWatcher", first.Deallocator.Name);
        Assert.Equal(1, second.RefCnt());
        Assert.Equal(0, second.Deallocations);
        Assert.True(second.Release());
    }

    [Fact]
    public void DeferredReleaseFailureDoesNotPreventOtherDeferredReleases()
    {
        var bad = new Stub { Failure = new InvalidOperationException("deferred failure") };
        var good = new Counted();
        var caller = new Thread(() => { ReferenceCountUtil.ReleaseLater(bad); ReferenceCountUtil.ReleaseLater(good); }) { IsBackground = true };
        caller.Start(); Assert.True(caller.Join(TimeSpan.FromSeconds(5)));
        Assert.True(ThreadDeathWatcher.AwaitInactivity(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, bad.Decrement);
        Assert.Equal(1, good.Deallocations);
    }
}
