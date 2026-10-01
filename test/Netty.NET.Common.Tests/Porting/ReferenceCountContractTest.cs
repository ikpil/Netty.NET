using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Common.Tests.Porting;

public class ReferenceCountContractTest
{
    private sealed class Counted : AbstractReferenceCounted
    {
        public int Deallocations;
        protected override void deallocate() => Interlocked.Increment(ref Deallocations);
        public override IReferenceCounted touch(object hint) => this;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void InvalidIncrementsAndDecrementsPreserveCount(int value)
    {
        var counted = new Counted();
        Assert.Throws<ArgumentException>(() => counted.retain(value));
        Assert.Throws<ArgumentException>(() => counted.release(value));
        Assert.Equal(1, counted.refCnt());
        Assert.Equal(0, counted.Deallocations);
    }

    [Fact]
    public void RetainOverflowPreservesCount()
    {
        var counted = new Counted();
        counted.retain(int.MaxValue - 1);
        Assert.Throws<IllegalReferenceCountException>(() => counted.retain());
        Assert.Equal(int.MaxValue, counted.refCnt());
        Assert.True(counted.release(int.MaxValue));
        Assert.Equal(1, counted.Deallocations);
    }

    [Fact]
    public async Task ConcurrentReleasesAreAllAppliedAndDeallocateOnce()
    {
        for (int iteration = 0; iteration < 1000; iteration++)
        {
            var counted = new Counted();
            counted.retain(3);
            using var start = new ManualResetEventSlim();
            var releases = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                start.Wait();
                return counted.release();
            })).ToArray();
            start.Set();
            var results = await Task.WhenAll(releases);
            Assert.Equal(1, results.Count(result => result));
            Assert.Equal(0, counted.refCnt());
            Assert.Equal(1, counted.Deallocations);
        }
    }
}
