using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class ReferenceCountFieldContractTest
{
    private sealed class Counted : AbstractReferenceCounted
    {
        public int Deallocations;
        public Exception DeallocationError;
        public void SetCount(int count) => SetRefCnt(count);
        protected override void Deallocate()
        {
            Interlocked.Increment(ref Deallocations);
            if (DeallocationError != null) throw DeallocationError;
        }
        public override IReferenceCounted Touch(object hint) => this;
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(0)]
    public void NonpositiveDirectCountsBecomeReleasedAndCannotBeRetained(int count)
    {
        var counted = new Counted();
        counted.SetCount(count);
        Assert.Equal(0, counted.RefCnt());
        Assert.Throws<IllegalReferenceCountException>(() => counted.Retain());
        Assert.Throws<IllegalReferenceCountException>(() => counted.Release());
        Assert.Equal(0, counted.RefCnt());
        // Setting a count is a quiescent operation, not a deallocation callback.
        Assert.Equal(0, counted.Deallocations);
    }

    private sealed class Fields
    {
        public int First = 1;
        public int Second = 1;
        public int Payload;
    }

    [Fact]
    public void ComposedFieldsRemainIndependentAndResetOnlyAfterPriorUsersStop()
    {
        var fields = new Fields();
        ReferenceCountUpdater.Retain(ref fields.First, 2);
        Assert.False(ReferenceCountUpdater.Release(ref fields.First, 2));
        Assert.True(ReferenceCountUpdater.Release(ref fields.First));
        Assert.False(ReferenceCountUpdater.IsLive(ref fields.First));
        Assert.Equal(1, ReferenceCountUpdater.GetCount(ref fields.Second));
        Assert.Throws<IllegalReferenceCountException>(() => ReferenceCountUpdater.Retain(ref fields.First));
        ReferenceCountUpdater.Reset(ref fields.First);
        Assert.True(ReferenceCountUpdater.IsLive(ref fields.First));
        Assert.Equal(1, ReferenceCountUpdater.GetCount(ref fields.First));
        Assert.True(ReferenceCountUpdater.Release(ref fields.Second));
        Assert.True(ReferenceCountUpdater.Release(ref fields.First));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void InvalidChangesLeaveTheSameFieldUnchanged(int change)
    {
        var fields = new Fields();
        Assert.Throws<ArgumentException>(() => ReferenceCountUpdater.Retain(ref fields.First, change));
        Assert.Throws<ArgumentException>(() => ReferenceCountUpdater.Release(ref fields.First, change));
        Assert.Equal(1, ReferenceCountUpdater.GetCount(ref fields.First));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1073741824)]
    [InlineData(int.MaxValue)]
    public void FullPositiveInt32RangeHasStableFailureAndFinalRelease(int count)
    {
        var fields = new Fields();
        ReferenceCountUpdater.SetCount(ref fields.First, count);
        var overflow = Assert.Throws<IllegalReferenceCountException>(
            () => ReferenceCountUpdater.Retain(ref fields.First, int.MaxValue));
        Assert.Equal($"refCnt: {count}, increment: {int.MaxValue}", overflow.Message);
        Assert.Equal(count, ReferenceCountUpdater.GetCount(ref fields.First));
        if (count < int.MaxValue)
        {
            var excessive = Assert.Throws<IllegalReferenceCountException>(
                () => ReferenceCountUpdater.Release(ref fields.First, int.MaxValue));
            Assert.Equal($"refCnt: {count}, decrement: {int.MaxValue}", excessive.Message);
            Assert.Equal(count, ReferenceCountUpdater.GetCount(ref fields.First));
        }
        Assert.True(ReferenceCountUpdater.Release(ref fields.First, count));
        Assert.Equal(0, ReferenceCountUpdater.GetCount(ref fields.First));
        Assert.Throws<IllegalReferenceCountException>(() => ReferenceCountUpdater.Retain(ref fields.First));
    }

    [Fact]
    public void SharedNativeStorageIsDisposedOnlyByTheLastReference()
    {
        var allocator = new NativeMemoryAllocator();
        using var owner = allocator.Allocate(16, clear: true);
        var fields = new Fields();
        Memory<byte> borrowed = owner.Memory.Slice(3, 4);
        ReferenceCountUpdater.Retain(ref fields.First);
        Assert.False(ReferenceCountUpdater.Release(ref fields.First));
        borrowed.Span[0] = 0xa5;
        Assert.Equal(0xa5, owner.Memory.Span[3]);
        Assert.Equal(16, allocator.ReservedBytes);
        if (ReferenceCountUpdater.Release(ref fields.First)) owner.Dispose();
        Assert.Equal(0, allocator.ReservedBytes);
        Assert.Throws<ObjectDisposedException>(() => _ = borrowed.Span.Length);
        Assert.Throws<IllegalReferenceCountException>(() => ReferenceCountUpdater.Retain(ref fields.First));
    }

    [Fact]
    public void ThrowingDeallocatorStillLeavesTheCountReleasedExactlyOnce()
    {
        var error = new InvalidOperationException("deallocation failed");
        var counted = new Counted { DeallocationError = error };
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => counted.Release()));
        Assert.Equal(0, counted.RefCnt());
        Assert.Equal(1, counted.Deallocations);
        Assert.Throws<IllegalReferenceCountException>(() => counted.Release());
        Assert.Throws<IllegalReferenceCountException>(() => counted.Retain());
        Assert.Equal(1, counted.Deallocations);
    }

    [Fact(Timeout = 30000)]
    public async Task RetainAndFinalReleaseRaceCannotResurrectStorage()
    {
        for (int iteration = 0; iteration < 1000; iteration++)
        {
            var counted = new Counted();
            using var start = new ManualResetEventSlim();
            Task<bool> retaining = Task.Run(() =>
            {
                start.Wait();
                try { counted.Retain(); return true; }
                catch (IllegalReferenceCountException) { return false; }
            });
            Task<bool> releasing = Task.Run(() => { start.Wait(); return counted.Release(); });
            start.Set();
            await Task.WhenAll(retaining, releasing);
            bool retained = await retaining;
            bool released = await releasing;
            Assert.Equal(!retained, released);
            Assert.Equal(retained ? 1 : 0, counted.RefCnt());
            if (retained) Assert.True(counted.Release());
            Assert.Equal(0, counted.RefCnt());
            Assert.Equal(1, counted.Deallocations);
        }
    }

    [Fact(Timeout = 30000)]
    public async Task ContendedRetainAndReleaseKeepTheOriginalOwnerAlive()
    {
        var fields = new Fields();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (int iteration = 0; iteration < 10000; iteration++)
            {
                ReferenceCountUpdater.Retain(ref fields.First);
                Assert.False(ReferenceCountUpdater.Release(ref fields.First));
            }
        })));
        Assert.Equal(1, ReferenceCountUpdater.GetCount(ref fields.First));
        Assert.True(ReferenceCountUpdater.Release(ref fields.First));
    }

    [Fact(Timeout = 30000)]
    public async Task CompetingRetainsAtTheLimitPublishOnlyOneIncrement()
    {
        for (int iteration = 0; iteration < 500; iteration++)
        {
            var fields = new Fields();
            ReferenceCountUpdater.SetCount(ref fields.First, int.MaxValue - 1);
            using var start = new ManualResetEventSlim();
            Task<bool>[] attempts = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
            {
                start.Wait();
                try { ReferenceCountUpdater.Retain(ref fields.First); return true; }
                catch (IllegalReferenceCountException) { return false; }
            })).ToArray();
            start.Set();
            bool[] results = await Task.WhenAll(attempts);
            Assert.Equal(1, results.Count(success => success));
            Assert.Equal(int.MaxValue, ReferenceCountUpdater.GetCount(ref fields.First));
            Assert.True(ReferenceCountUpdater.Release(ref fields.First, int.MaxValue));
        }
    }

    [Fact(Timeout = 30000)]
    public async Task CountPublicationMakesEarlierPayloadWritesVisible()
    {
        for (int iteration = 0; iteration < 1000; iteration++)
        {
            var fields = new Fields();
            Task publisher = Task.Run(() =>
            {
                fields.Payload = 0x13579bdf;
                ReferenceCountUpdater.Retain(ref fields.First);
            }, TestContext.Current.CancellationToken);
            Assert.True(SpinWait.SpinUntil(() => ReferenceCountUpdater.GetCount(ref fields.First) == 2,
                TimeSpan.FromSeconds(5)));
            Assert.Equal(0x13579bdf, fields.Payload);
            Assert.False(ReferenceCountUpdater.Release(ref fields.First));
            await publisher;
            Assert.True(ReferenceCountUpdater.Release(ref fields.First));
        }
    }
}
