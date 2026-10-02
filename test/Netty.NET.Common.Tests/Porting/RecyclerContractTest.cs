using System;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

public class RecyclerContractTest
{
    private sealed class Item(IRecyclerHandle<Item> handle)
    {
        internal readonly IRecyclerHandle<Item> Handle = handle;
        internal int Active;
        public override bool Equals(object obj) => obj is Item;
        public override int GetHashCode() => 0;
    }
    private sealed class Pool : Recycler<Item>
    {
        internal Pool(int capacity, bool unguarded) : base(capacity, unguarded) { }
        internal Pool(Thread owner, bool unguarded) : base(4, 0, 2, owner, unguarded) { }
        internal Pool(bool unguarded) : base(4, 0, 2, unguarded) { }
        protected override Item newObject(IRecyclerHandle<Item> handle) => new(handle);
    }

    [Fact]
    public void GuardedHandlesRejectEqualButDistinctObjectsWithoutChangingState()
    {
        var pool = new Pool(4, false);
        Item item = pool.get();
        var other = new Item(item.Handle);
        Assert.True(item.Equals(other));
        Assert.Throws<ArgumentException>(() => item.Handle.recycle(other));
        var enhanced = Assert.IsAssignableFrom<RecyclerEnhancedHandle<Item>>(item.Handle);
        Assert.Throws<ArgumentException>(() => enhanced.unguardedRecycle(other));
        item.Handle.recycle(item);
        Assert.Throws<InvalidOperationException>(() => item.Handle.recycle(item));
        Assert.Same(item, pool.get());
        enhanced.unguardedRecycle(item);
        Assert.Throws<InvalidOperationException>(() => enhanced.unguardedRecycle(item));
        Assert.Same(item, pool.get());
    }

    [Fact]
    public void UnguardedPoolUsesSharedHandleWithoutIdentityOrDuplicateChecks()
    {
        var pool = new Pool(4, true);
        Item item = pool.get();
        var replacement = new Item(item.Handle);
        item.Handle.recycle(replacement);
        Assert.Same(replacement, pool.get());
        item.Handle.recycle(item);
        item.Handle.recycle(item);
        Assert.Same(item, pool.get());
        Assert.Same(item, pool.get());
    }

    [Fact]
    public void PinnedOwnerUsesLifoBatchThenExternalFifoAndNullOwnerIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new Pool(null, false));
        var pool = new Pool(Thread.CurrentThread, false);
        Item a = pool.get(), b = pool.get(), c = pool.get(), d = pool.get();
        a.Handle.recycle(a); b.Handle.recycle(b);
        c.Handle.recycle(c); d.Handle.recycle(d);
        Assert.Equal(4, pool.threadLocalSize());
        Assert.Same(b, pool.get());
        Assert.Same(a, pool.get());
        Assert.Same(c, pool.get());
        Assert.Same(d, pool.get());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedPoolSupportsConcurrentBorrowAndReturnWithoutDuplicateClaims(bool unguarded)
    {
        var pool = new Pool(32, unguarded);
        var tasks = new Task[8];
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        for (int worker = 0; worker < tasks.Length; worker++)
            tasks[worker] = Task.Run(() =>
            {
                for (int i = 0; i < 5000; i++)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    Item item = pool.get();
                    Assert.Equal(0, Interlocked.CompareExchange(ref item.Active, 1, 0));
                    Thread.Yield();
                    Assert.Equal(1, Interlocked.Exchange(ref item.Active, 0));
                    item.Handle.recycle(item);
                }
            });
        Assert.True(Task.WaitAll(tasks, TimeSpan.FromSeconds(15)));
        Assert.InRange(pool.threadLocalSize(), 1, 32);
    }

    [Fact]
    public void ReturningAfterOwnerTerminationDiscardsItsExternalQueue()
    {
        Pool pool = null;
        Item item = null;
        var owner = new Thread(() => { pool = new Pool(Thread.CurrentThread, false); item = pool.get(); });
        owner.Start();
        Assert.True(owner.Join(TimeSpan.FromSeconds(5)));
        item.Handle.recycle(item);
        Assert.Equal(0, pool.threadLocalSize());
        Assert.NotSame(item, pool.get());
    }

    [Fact]
    public void UnpinPreservesPoolButStopsRetainingItsOwner()
    {
        Pool pool = null;
        Item item = null;
        var owner = new Thread(() =>
        {
            pool = new Pool(Thread.CurrentThread, false);
            item = pool.get();
            Recycler.unpinOwner(pool);
        });
        owner.Start();
        Assert.True(owner.Join(TimeSpan.FromSeconds(5)));
        item.Handle.recycle(item);
        Assert.Same(item, pool.get());
    }

    private sealed class NoCleanupThread(Action action) : FastThreadLocalThread
    {
        public override void run() => action();
    }

    [Fact]
    public void IndexedMapWithoutAutomaticCleanupDoesNotEnableRecyclerPooling()
    {
        Exception failure = null;
        var thread = new NoCleanupThread(() =>
        {
            try
            {
                Assert.True(FastThreadLocalThread.currentThreadHasFastThreadLocal());
                Assert.False(FastThreadLocalThread.currentThreadWillCleanupFastThreadLocals());
                var pool = new Pool(false);
                Item first = pool.get();
                first.Handle.recycle(first);
                Assert.NotSame(first, pool.get());
                Assert.Equal(0, pool.threadLocalSize());
            }
            catch (Exception error) { failure = error; }
            finally { FastThreadLocal.removeAll(); }
        });
        thread.start();
        Assert.True(thread.join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
    }
}
