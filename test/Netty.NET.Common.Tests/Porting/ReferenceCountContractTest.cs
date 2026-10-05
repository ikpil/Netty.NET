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
        protected override void Deallocate() => Interlocked.Increment(ref Deallocations);
        public override IReferenceCounted Touch(object hint) => this;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void InvalidIncrementsAndDecrementsPreserveCount(int value)
    {
        var counted = new Counted();
        Assert.Throws<ArgumentException>(() => counted.Retain(value));
        Assert.Throws<ArgumentException>(() => counted.Release(value));
        Assert.Equal(1, counted.ReferenceCount);
        Assert.Equal(0, counted.Deallocations);
    }

    [Fact]
    public void RetainOverflowPreservesCount()
    {
        var counted = new Counted();
        counted.Retain(int.MaxValue - 1);
        Assert.Throws<IllegalReferenceCountException>(() => counted.Retain());
        Assert.Equal(int.MaxValue, counted.ReferenceCount);
        Assert.True(counted.Release(int.MaxValue));
        Assert.Equal(1, counted.Deallocations);
    }

    [Fact]
    public async Task ConcurrentReleasesAreAllAppliedAndDeallocateOnce()
    {
        for (int iteration = 0; iteration < 1000; iteration++)
        {
            var counted = new Counted();
            counted.Retain(3);
            using var start = new ManualResetEventSlim();
            var releases = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                start.Wait();
                return counted.Release();
            })).ToArray();
            start.Set();
            var results = await Task.WhenAll(releases);
            Assert.Equal(1, results.Count(result => result));
            Assert.Equal(0, counted.ReferenceCount);
            Assert.Equal(1, counted.Deallocations);
        }
    }
}
