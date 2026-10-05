using System;
using System.Threading;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Porting;

public class RecyclerFactoryContractTest
{
    private sealed class Item(IRecyclerHandle<Item> handle)
    {
        internal readonly IRecyclerHandle<Item> Handle = handle;
        public override bool Equals(object obj) => obj is Item;
        public override int GetHashCode() => 0;
    }

    private sealed class Other(IRecyclerHandle<Other> handle)
    {
        internal readonly IRecyclerHandle<Other> Handle = handle;
    }

    [Fact]
    public void NullFactoriesAreRejectedBeforeBorrowing()
    {
        Assert.Equal("factory", Assert.Throws<ArgumentNullException>(() => Recycler.Create<Item>(null)).ParamName);
    }

    [Fact]
    public void NativeFactoriesAreLazyAndOrdinaryThreadsKeepNoopReturns()
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Assert.False(FastThreadLocalThread.CurrentThreadWillCleanupFastThreadLocals());
                int creations = 0;
                var pool = Recycler.Create<Item>(handle => { Assert.NotNull(handle); creations++; return new Item(handle); });
                Assert.Equal(0, creations);
                Item first = pool.Get(), second = pool.Get();
                Assert.NotSame(first, second);
                Assert.Equal(2, creations);
                first.Handle.Recycle(first);
                first.Handle.Recycle(first);
                second.Handle.Recycle(second);
                Assert.Equal(0, pool.ThreadLocalSize());
            }
            catch (Exception error) { failure = error; }
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeFactoriesPreserveGuardedIdentityAndReturnAcrossThreads(bool externalReturn)
    {
        RunInFastThreadLocalThreadExtension.Run(() =>
        {
            int creations = 0;
            var pool = Recycler.Create<Item>(handle => { creations++; return new Item(handle); });
            Item first = pool.Get();
            var impostor = new Item(first.Handle);
            Assert.True(first.Equals(impostor));
            Assert.Throws<ArgumentException>(() => first.Handle.Recycle(impostor));
            if (externalReturn)
            {
                Exception failure = null;
                var thread = new Thread(() => { try { first.Handle.Recycle(first); } catch (Exception error) { failure = error; } });
                thread.Start();
                Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
                Assert.Null(failure);
            }
            else first.Handle.Recycle(first);
            Assert.Throws<InvalidOperationException>(() => first.Handle.Recycle(first));
            Assert.Equal(1, pool.ThreadLocalSize());
            Item next = pool.Get();
            Assert.Same(first, next);
            Assert.Equal(1, creations);
            Assert.Equal(0, pool.ThreadLocalSize());
            next.Handle.Recycle(next);
        });
    }

    [Fact]
    public void FactoryExceptionsPropagateWithoutPublishingAnIncompleteValue()
    {
        RunInFastThreadLocalThreadExtension.Run(() =>
        {
            var cause = new InvalidOperationException("creation failed");
            int creations = 0;
            var pool = Recycler.Create<Item>(handle => ++creations == 1 ? throw cause : new Item(handle));
            Assert.Same(cause, Assert.Throws<InvalidOperationException>(() => pool.Get()));
            Assert.Equal(0, pool.ThreadLocalSize());
            Item next = pool.Get();
            Assert.NotNull(next);
            Assert.NotNull(next.Handle);
            Assert.Equal(2, creations);
            next.Handle.Recycle(next);
        });
    }

    [Fact]
    public void ClosedGenericTypesKeepTheirFactoriesAndHandlesSeparate()
    {
        RunInFastThreadLocalThreadExtension.Run(() =>
        {
            int firstCreations = 0, otherCreations = 0;
            var firstPool = Recycler.Create<Item>(handle => { firstCreations++; return new Item(handle); });
            var otherPool = Recycler.Create<Other>(handle => { otherCreations++; return new Other(handle); });
            Item first = firstPool.Get();
            Other other = otherPool.Get();
            first.Handle.Recycle(first);
            other.Handle.Recycle(other);
            Item firstAgain = firstPool.Get();
            Other otherAgain = otherPool.Get();
            Assert.Same(first, firstAgain);
            Assert.Same(other, otherAgain);
            Assert.Equal(1, firstCreations);
            Assert.Equal(1, otherCreations);
            firstAgain.Handle.Recycle(firstAgain);
            otherAgain.Handle.Recycle(otherAgain);
        });
    }

    [Fact]
    public void PoolsOfTheSameTypeKeepCapturedFactoriesAndBorrowedValuesSeparate()
    {
        RunInFastThreadLocalThreadExtension.Run(() =>
        {
            int firstCreations = 0, secondCreations = 0;
            var firstPool = Recycler.Create<Item>(handle => { firstCreations++; return new Item(handle); });
            var secondPool = Recycler.Create<Item>(handle => { secondCreations++; return new Item(handle); });
            Item first = firstPool.Get(), second = secondPool.Get();
            Assert.NotSame(first, second);
            first.Handle.Recycle(first);
            second.Handle.Recycle(second);
            Item firstAgain = firstPool.Get(), secondAgain = secondPool.Get();
            Assert.Same(first, firstAgain);
            Assert.Same(second, secondAgain);
            Assert.Equal(1, firstCreations);
            Assert.Equal(1, secondCreations);
            firstAgain.Handle.Recycle(firstAgain);
            secondAgain.Handle.Recycle(secondAgain);
        });
    }
}
