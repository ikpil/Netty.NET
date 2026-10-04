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
        protected override Item NewObject(IRecyclerHandle<Item> handle) => new(handle);
    }

    [Fact]
    public void GuardedHandlesRejectEqualButDistinctObjectsWithoutChangingState()
    {
        var pool = new Pool(4, false);
        Item item = pool.Get();
        var other = new Item(item.Handle);
        Assert.True(item.Equals(other));
        Assert.Throws<ArgumentException>(() => item.Handle.Recycle(other));
        var enhanced = Assert.IsAssignableFrom<RecyclerEnhancedHandle<Item>>(item.Handle);
        Assert.Throws<ArgumentException>(() => enhanced.UnguardedRecycle(other));
        item.Handle.Recycle(item);
        Assert.Throws<InvalidOperationException>(() => item.Handle.Recycle(item));
        Assert.Same(item, pool.Get());
        enhanced.UnguardedRecycle(item);
        Assert.Throws<InvalidOperationException>(() => enhanced.UnguardedRecycle(item));
        Assert.Same(item, pool.Get());
    }

    [Fact]
    public void UnguardedPoolUsesSharedHandleWithoutIdentityOrDuplicateChecks()
    {
        var pool = new Pool(4, true);
        Item item = pool.Get();
        var replacement = new Item(item.Handle);
        item.Handle.Recycle(replacement);
        Assert.Same(replacement, pool.Get());
        item.Handle.Recycle(item);
        item.Handle.Recycle(item);
        Assert.Same(item, pool.Get());
        Assert.Same(item, pool.Get());
    }

    [Fact]
    public void PinnedOwnerUsesLifoBatchThenExternalFifoAndNullOwnerIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new Pool(null, false));
        var pool = new Pool(Thread.CurrentThread, false);
        Item a = pool.Get(), b = pool.Get(), c = pool.Get(), d = pool.Get();
        a.Handle.Recycle(a); b.Handle.Recycle(b);
        c.Handle.Recycle(c); d.Handle.Recycle(d);
        Assert.Equal(4, pool.ThreadLocalSize());
        Assert.Same(b, pool.Get());
        Assert.Same(a, pool.Get());
        Assert.Same(c, pool.Get());
        Assert.Same(d, pool.Get());
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
                    Item item = pool.Get();
                    Assert.Equal(0, Interlocked.CompareExchange(ref item.Active, 1, 0));
                    Thread.Yield();
                    Assert.Equal(1, Interlocked.Exchange(ref item.Active, 0));
                    item.Handle.Recycle(item);
                }
            });
        Assert.True(Task.WaitAll(tasks, TimeSpan.FromSeconds(15)));
        Assert.InRange(pool.ThreadLocalSize(), 1, 32);
    }

    [Fact]
    public void ReturningAfterOwnerTerminationDiscardsItsExternalQueue()
    {
        Pool pool = null;
        Item item = null;
        var owner = new Thread(() => { pool = new Pool(Thread.CurrentThread, false); item = pool.Get(); });
        owner.Start();
        Assert.True(owner.Join(TimeSpan.FromSeconds(5)));
        item.Handle.Recycle(item);
        Assert.Equal(0, pool.ThreadLocalSize());
        Assert.NotSame(item, pool.Get());
    }

    [Fact]
    public void UnpinPreservesPoolButStopsRetainingItsOwner()
    {
        Pool pool = null;
        Item item = null;
        var owner = new Thread(() =>
        {
            pool = new Pool(Thread.CurrentThread, false);
            item = pool.Get();
            Recycler.UnpinOwner(pool);
        });
        owner.Start();
        Assert.True(owner.Join(TimeSpan.FromSeconds(5)));
        item.Handle.Recycle(item);
        Assert.Same(item, pool.Get());
    }

    private sealed class NoCleanupThread(Action action) : FastThreadLocalThread
    {
        public override void Run() => action();
    }

    [Fact]
    public void IndexedMapWithoutAutomaticCleanupDoesNotEnableRecyclerPooling()
    {
        Exception failure = null;
        var thread = new NoCleanupThread(() =>
        {
            try
            {
                Assert.True(FastThreadLocalThread.CurrentThreadHasFastThreadLocal());
                Assert.False(FastThreadLocalThread.CurrentThreadWillCleanupFastThreadLocals());
                var pool = new Pool(false);
                Item first = pool.Get();
                first.Handle.Recycle(first);
                Assert.NotSame(first, pool.Get());
                Assert.Equal(0, pool.ThreadLocalSize());
            }
            catch (Exception error) { failure = error; }
            finally { FastThreadLocal.RemoveAll(); }
        });
        thread.Thread.Start();
        Assert.True(thread.Thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
    }
}
