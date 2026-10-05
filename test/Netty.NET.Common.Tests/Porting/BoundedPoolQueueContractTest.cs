using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Common.Tests.Porting;

public class BoundedPoolQueueContractTest
{
    private sealed class Item(IRecyclerHandle<Item> handle)
    {
        internal readonly IRecyclerHandle<Item> Handle = handle;
    }

    private sealed class Pool(int capacity, bool unguarded = false) : Recycler<Item>(capacity, unguarded)
    {
        protected override Item NewObject(IRecyclerHandle<Item> handle) => new(handle);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InterruptedClrSegmentTransitionsPreserveReturnOrClaimAndPendingInterrupt(bool enqueue)
    {
        var pool = new Pool(128);
        var items = new Item[enqueue ? 33 : 65];
        for (int index = 0; index < items.Length; index++) items[index] = pool.Get();
        for (int index = 0; index < (enqueue ? 32 : 65); index++) items[index].Handle.Recycle(items[index]);
        if (!enqueue)
            for (int index = 0; index < 32; index++) Assert.Same(items[index], pool.Get());

        // Hold the actual net10 ConcurrentQueue segment-transition lock to force the CLR-specific wait.
        // Reflection controls the schedule only; recycle/borrow use the ordinary public API.
        object localPool = Field(pool, "localPool");
        object returnQueue = Field(localPool, "pooledHandles");
        // Blocking mode synchronizes on the return queue itself, as in the
        // pinned Java implementation. Force that monitor wait instead of a
        // ConcurrentQueue segment transition; retain the same interrupt/FIFO assertions.
        object segmentGate = Recycler.BLOCKING_POOL
            ? returnQueue
            : Field(Field(returnQueue, "queue"), "_crossSegmentLock");
        Exception failure = null;
        Item claimed = null;
        bool pendingInterrupt = false;
        var worker = new Thread(() =>
        {
            try
            {
                if (enqueue) items[32].Handle.Recycle(items[32]);
                else claimed = pool.Get();
                try { Thread.Sleep(0); }
                catch (ThreadInterruptedException) { pendingInterrupt = true; }
            }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true };
        bool started = false;
        bool waited = false;
        Monitor.Enter(segmentGate);
        try
        {
            worker.Start();
            started = true;
            waited = SpinWait.SpinUntil(() => (worker.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5));
            if (waited) worker.Interrupt();
        }
        finally
        {
            Monitor.Exit(segmentGate);
            if (started) Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
        }
        Assert.True(waited);
        Assert.Null(failure);
        Assert.True(pendingInterrupt);
        Assert.Equal(enqueue ? 33 : 32, pool.ThreadLocalSize());
        if (enqueue)
        {
            for (int index = 0; index < 33; index++) Assert.Same(items[index], pool.Get());
        }
        else Assert.Same(items[32], claimed);
    }

    [Theory]
    [InlineData(3, 4, false)]
    [InlineData(3, 4, true)]
    [InlineData(17, 32, false)]
    [InlineData(17, 32, true)]
    public void FullPoolPreservesAcceptedFifoReturnsAndCanReuseFreedCapacity(int requested, int effective, bool unguarded)
    {
        // The original 'effective' theory value is the default rounded bound.
        // Blocking mode preserves the requested capacity after the pool's minimum of four.
        int expectedCapacity = Recycler.BLOCKING_POOL ? Math.Max(4, requested) : effective;
        var pool = new Pool(requested, unguarded);
        var items = new Item[expectedCapacity + 2];
        for (int index = 0; index < items.Length; index++) items[index] = pool.Get();
        foreach (Item item in items) item.Handle.Recycle(item);
        Assert.Equal(expectedCapacity, pool.ThreadLocalSize());
        for (int index = 0; index < expectedCapacity; index++) Assert.Same(items[index], pool.Get());
        Assert.Equal(0, pool.ThreadLocalSize());
        Item fresh = pool.Get();
        foreach (Item item in items) Assert.NotSame(item, fresh);
        items[0].Handle.Recycle(items[0]);
        Assert.Equal(1, pool.ThreadLocalSize());
        Assert.Same(items[0], pool.Get());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentReturnsRespectTheBoundAndDoNotEvictPreviouslyAcceptedObjects(bool unguarded)
    {
        int expectedCapacity = Recycler.BLOCKING_POOL ? 17 : 32;
        var pool = new Pool(17, unguarded);
        var items = new Item[96];
        for (int index = 0; index < items.Length; index++) items[index] = pool.Get();
        for (int index = 0; index < 4; index++) items[index].Handle.Recycle(items[index]);
        var workers = new Task[4];
        for (int producer = 0; producer < workers.Length; producer++)
        {
            int first = 4 + producer;
            workers[producer] = Task.Run(() =>
            {
                for (int index = first; index < items.Length; index += 4)
                    items[index].Handle.Recycle(items[index]);
            }, TestContext.Current.CancellationToken);
        }
        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(expectedCapacity, pool.ThreadLocalSize());
        for (int index = 0; index < 4; index++) Assert.Same(items[index], pool.Get());
        var accepted = new HashSet<Item>(ReferenceEqualityComparer.Instance);
        var borrowed = new Item[expectedCapacity - 4];
        for (int index = 0; index < borrowed.Length; index++)
        {
            Item item = borrowed[index] = pool.Get();
            Assert.True(accepted.Add(item));
            Assert.Contains(item, items);
        }
        Assert.Equal(0, pool.ThreadLocalSize());
        Item fresh = pool.Get();
        foreach (Item item in items) Assert.NotSame(item, fresh);
        foreach (Item item in borrowed) item.Handle.Recycle(item);
        Assert.Equal(borrowed.Length, pool.ThreadLocalSize());
        foreach (Item item in borrowed) Assert.Same(item, pool.Get());
    }

    private static object Field(object instance, string name)
    {
        for (Type type = instance.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) return field.GetValue(instance);
        }
        throw new InvalidOperationException($"Missing schedule-control field {name} on {instance.GetType()}.");
    }
}
