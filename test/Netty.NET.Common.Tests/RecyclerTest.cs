/*
* Copyright 2014 The Netty Project
*
* The Netty Project licenses this file to you under the Apache License,
* version 2.0 (the "License"); you may not use this file except in compliance
* with the License. You may obtain a copy of the License at:
*
*   https://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
* WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
* License for the specific language governing permissions and limitations
* under the License.
*/
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests;

public class RecyclerTest
{
    public enum OwnerType { NONE, PINNED, FAST_THREAD_LOCAL }
    protected static bool isPooling(OwnerType type) => type != OwnerType.FAST_THREAD_LOCAL ||
        FastThreadLocalThread.currentThreadWillCleanupFastThreadLocals();
    protected static void assumeIsPooling(OwnerType type) => Xunit.Assert.SkipUnless(isPooling(type), "Owner has no automatic thread-local cleanup.");
    public static IEnumerable<object[]> ownerTypeAndUnguarded()
        => Enum.GetValues<OwnerType>().SelectMany(owner => new[] { true, false }.Select(unguarded => new object[] { owner, unguarded }));
    public static IEnumerable<object[]> notNoneOwnerAndUnguarded()
        => ownerTypeAndUnguarded().Where(row => (OwnerType)row[0] != OwnerType.NONE);
    public static IEnumerable<object[]> owners() => Enum.GetValues<OwnerType>().Select(owner => new object[] { owner });
    private readonly ConcurrentQueue<Exception> workerFailures = new();
    protected virtual void runTest(Action invocation) => invocation();
    protected virtual Thread newThread(Action invocation) => new(invocation.Invoke) { IsBackground = true };
    protected Thread worker(Action invocation) => newThread(() =>
    {
        try { invocation(); }
        catch (Exception error) { workerFailures.Enqueue(error); }
    });
    protected void join(Thread thread)
    {
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Empty(workerFailures);
    }
    protected sealed class HandledObject
    {
        internal readonly IRecyclerHandle<HandledObject> handle;
        internal HandledObject(IRecyclerHandle<HandledObject> handle) => this.handle = handle;
        internal void recycle() => handle.recycle(this);
    }
    private sealed class Pool : Recycler<HandledObject>
    {
        private readonly Action<HandledObject> onNewObject;
        internal Pool(int capacity, bool unguarded, Action<HandledObject> onNewObject) : base(capacity, unguarded) => this.onNewObject = onNewObject;
        internal Pool(Thread owner, int capacity, int ratio, int chunk, bool unguarded, Action<HandledObject> onNewObject)
            : base(capacity, ratio, chunk, owner, unguarded) => this.onNewObject = onNewObject;
        internal Pool(int capacity, int ratio, int chunk, bool unguarded, Action<HandledObject> onNewObject)
            : base(capacity, ratio, chunk, unguarded) => this.onNewObject = onNewObject;
        protected override HandledObject newObject(IRecyclerHandle<HandledObject> handle)
        {
            var value = new HandledObject(handle);
            onNewObject?.Invoke(value);
            return value;
        }
    }
    protected static Recycler<HandledObject> newRecycler(OwnerType owner, bool unguarded, int capacity, Action<HandledObject> onNewObject = null)
        => newRecycler(owner, unguarded, capacity, Recycler.RATIO, capacity >> 1, onNewObject);
    protected static Recycler<HandledObject> newRecycler(bool unguarded, int capacity)
        => newRecycler(OwnerType.FAST_THREAD_LOCAL, unguarded, capacity);
    protected static Recycler<HandledObject> newRecycler(int capacity)
        => newRecycler(OwnerType.FAST_THREAD_LOCAL, false, capacity, 8, capacity >> 1);
    protected static Recycler<HandledObject> newRecycler(OwnerType owner, bool unguarded, int capacity, int ratio, int chunk, Action<HandledObject> onNewObject = null)
    {
        // NOTE: ratio and chunk size will be ignored for NONE owner type!
        return owner switch
        {
            OwnerType.NONE => new Pool(capacity, unguarded, onNewObject),
            OwnerType.PINNED => new Pool(Thread.CurrentThread, capacity, ratio, chunk, unguarded, onNewObject),
            OwnerType.FAST_THREAD_LOCAL => new Pool(capacity, ratio, chunk, unguarded, onNewObject),
            _ => throw new ArgumentOutOfRangeException(nameof(owner))
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference<Thread> startCollectibleThread(OwnerType owner, bool unguarded, Action<HandledObject> retain)
    {
        Thread thread = worker(() =>
        {
            Recycler<HandledObject> recycler = newRecycler(owner, unguarded, 1024);
            HandledObject value = recycler.get();
            // Store a reference to the HandledObject to ensure it is not collected when the run method finish.
            retain(value);
            Recycler.unpinOwner(recycler);
        });
        var weak = new WeakReference<Thread>(thread);
        thread.Start();
        join(thread);
        // Null out so it can be collected.
        return weak;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool collected(WeakReference<Thread> thread) => !thread.TryGetTarget(out _);

    [Theory]
    [MemberData(nameof(ownerTypeAndUnguarded))]
    public virtual void testThreadCanBeCollectedEvenIfHandledObjectIsReferenced(OwnerType ownerType, bool unguarded) => runTest(() =>
    {
        HandledObject retained = null;
        WeakReference<Thread> thread = startCollectibleThread(ownerType, unguarded, value => retained = value);
        // Loop until the Thread was collected. If we can not collect it the Test will fail due of a timeout.
        var deadline = Stopwatch.StartNew();
        while (!collected(thread) && deadline.Elapsed < TimeSpan.FromSeconds(5))
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Thread.Sleep(50);
        }
        Assert.True(collected(thread));
        // Now call recycle after the Thread was collected to ensure this still works...
        retained?.recycle();
        GC.KeepAlive(retained);
    });

    [Theory]
    [MemberData(nameof(ownerTypeAndUnguarded))]
    public void verySmallRecycer(OwnerType ownerType, bool unguarded) => runTest(() => newRecycler(ownerType, unguarded, 2, 0, 1).get());

    [Theory]
    [MemberData(nameof(owners))]
    public void testMultipleRecycle(OwnerType ownerType) => runTest(() =>
    {
        // This test makes only sense for guarded recyclers
        HandledObject value = newRecycler(ownerType, false, 1024).get();
        value.recycle();
        if (isPooling(ownerType)) Assert.Throws<InvalidOperationException>(value.recycle);
        else value.recycle(); // No-op because not pooling.
    });

    [Fact]
    public void testUnguardedMultipleRecycle() => runTest(() =>
    {
        HandledObject value = newRecycler(true, 1024).get();
        value.recycle();
        value.recycle();
    });

    [Theory]
    [MemberData(nameof(owners))]
    public void testMultipleRecycleAtDifferentThread(OwnerType ownerType) => runTest(() =>
    {
        // This test makes only sense for guarded recyclers
        Recycler<HandledObject> recycler = newRecycler(ownerType, false, 1024);
        HandledObject value = recycler.get();
        Exception failure = null;
        Thread first = worker(value.recycle);
        first.Start(); join(first);
        Thread second = worker(() => { try { value.recycle(); } catch (InvalidOperationException e) { failure = e; } });
        second.Start(); join(second);
        Assert.NotSame(recycler.get(), recycler.get());
        if (isPooling(ownerType)) Assert.NotNull(failure);
        else Assert.Null(failure);
    });

    [Theory]
    [MemberData(nameof(owners))]
    public void testMultipleRecycleAtDifferentThreadRacing(OwnerType ownerType) => runTest(() =>
    {
        // This test makes only sense for guarded recyclers
        Recycler<HandledObject> recycler = newRecycler(ownerType, false, 1024);
        HandledObject value = recycler.get();
        Exception failure = null;
        int failures = 0;
        using var done = new CountdownEvent(2);
        Action recycle = () =>
        {
            try { value.recycle(); }
            catch (InvalidOperationException e) { Interlocked.Exchange(ref failure, e); Interlocked.Increment(ref failures); }
            finally { done.Signal(); }
        };
        Thread first = worker(recycle), second = worker(recycle);
        first.Start(); second.Start();
        try
        {
            Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
            Assert.NotSame(recycler.get(), recycler.get());
            if (failure != null)
            {
                Assert.Contains("recycled already", failure.Message);
                Assert.Equal(1, failures);
            }
        }
        finally { join(first); join(second); }
    });

    [Theory]
    [MemberData(nameof(owners))]
    public void testMultipleRecycleRacing(OwnerType ownerType) => runTest(() =>
    {
        // This test makes only sense for guarded recyclers
        Recycler<HandledObject> recycler = newRecycler(ownerType, false, 1024);
        HandledObject value = recycler.get();
        Exception failure = null;
        using var done = new CountdownEvent(1);
        Thread first = worker(() =>
        {
            try { value.recycle(); }
            catch (InvalidOperationException e) { Interlocked.Exchange(ref failure, e); }
            finally { done.Signal(); }
        });
        first.Start();
        try { value.recycle(); }
        catch (InvalidOperationException e) { Interlocked.Exchange(ref failure, e); }
        try
        {
            Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
            Assert.NotSame(recycler.get(), recycler.get());
            if (isPooling(ownerType)) Assert.NotNull(failure); // Object got recycled twice, so at least one of the calls must throw.
            else Assert.Null(failure);
        }
        finally { join(first); }
    });

    [Theory]
    [MemberData(nameof(ownerTypeAndUnguarded))]
    public void testRecycle(OwnerType ownerType, bool unguarded) => runTest(() =>
    {
        Recycler<HandledObject> recycler = newRecycler(ownerType, unguarded, 1024);
        HandledObject value = recycler.get();
        value.recycle();
        HandledObject other = recycler.get();
        if (isPooling(ownerType)) Assert.Same(value, other);
        else Assert.NotSame(value, other);
        other.recycle();
    });
    [Theory]
    [MemberData(nameof(ownerTypeAndUnguarded))]
    public void testRecycleDisable(OwnerType ownerType, bool unguarded) => runTest(() =>
    {
        Recycler<HandledObject> recycler = newRecycler(ownerType, unguarded, -1);
        HandledObject value = recycler.get();
        value.recycle();
        HandledObject other = recycler.get();
        Assert.NotSame(value, other);
        other.recycle();
    });
    [Theory]
    [MemberData(nameof(ownerTypeAndUnguarded))]
    public void testRecycleDisableDrop(OwnerType ownerType, bool unguarded) => runTest(() =>
    {
        assumeIsPooling(ownerType);
        Recycler<HandledObject> recycler = newRecycler(ownerType, unguarded, 1024, 0, 16);
        HandledObject value = recycler.get();
        value.recycle();
        HandledObject other = recycler.get();
        Assert.Same(value, other);
        other.recycle();
        HandledObject third = recycler.get();
        Assert.Same(value, third);
        third.recycle();
    });

    /**
     * Test to make sure bug #2848 never happens again
     * https://github.com/netty/netty/issues/2848
     */
    [Theory]
    [MemberData(nameof(ownerTypeAndUnguarded))]
    public void testMaxCapacity(OwnerType ownerType, bool unguarded) => runTest(() =>
    {
        checkMaxCapacity(ownerType, unguarded, 300);
        var rand = new Random(2848);
        for (int i = 0; i < 50; i++) checkMaxCapacity(ownerType, unguarded, rand.Next(1000) + 256); // 256 - 1256
    });
    private static void checkMaxCapacity(OwnerType owner, bool unguarded, int capacity)
    {
        Recycler<HandledObject> recycler = newRecycler(owner, unguarded, capacity);
        var values = new HandledObject[capacity * 3];
        for (int i = 0; i < values.Length; i++) values[i] = recycler.get();
        for (int i = 0; i < values.Length; i++) { values[i].recycle(); values[i] = null; }
        Assert.True(MathUtil.findNextPositivePowerOfTwo(capacity) >= recycler.threadLocalSize(),
            "The threadLocalSize (" + recycler.threadLocalSize() + ") must be <= maxCapacity ("
                + capacity + ") as we not pool all new handles internally");
    }

    [Theory]
    [MemberData(nameof(notNoneOwnerAndUnguarded))]
    public void testRecycleAtDifferentThread(OwnerType ownerType, bool unguarded) => runTest(() =>
    {
        assumeIsPooling(ownerType);
        Recycler<HandledObject> recycler = newRecycler(ownerType, unguarded, 256, 2, 16);
        HandledObject first = recycler.get(), second = recycler.get();
        Thread thread = worker(() => { first.recycle(); second.recycle(); });
        thread.Start(); join(thread);
        Assert.Same(recycler.get(), first);
        Assert.NotSame(recycler.get(), second);
    });

    [Theory]
    [MemberData(nameof(ownerTypeAndUnguarded))]
    public void testRecycleAtTwoThreadsMulti(OwnerType ownerType, bool unguarded) => runTest(() =>
    {
        assumeIsPooling(ownerType);
        Recycler<HandledObject> recycler = newRecycler(ownerType, unguarded, 256);
        HandledObject first = recycler.get(), second = null;
        using var work = new BlockingCollection<Action>();
        Thread single = worker(() => { foreach (Action action in work.GetConsumingEnumerable()) action(); });
        single.Start();
        try
        {
            using var latch1 = new CountdownEvent(1);
            work.Add(() => { first.recycle(); latch1.Signal(); });
            Assert.True(latch1.Wait(TimeSpan.FromMilliseconds(100)));
            second = recycler.get();
            // Always recycler the first object, that is Ok
            Assert.Same(second, first);
            using var latch2 = new CountdownEvent(1);
            work.Add(() =>
            {
                //The object should be recycled
                second.recycle(); latch2.Signal();
            });
            Assert.True(latch2.Wait(TimeSpan.FromMilliseconds(100)));
            // It should be the same object, right?
            Assert.Same(recycler.get(), first);
        }
        finally { work.CompleteAdding(); join(single); }
    });

    [Theory]
    [MemberData(nameof(notNoneOwnerAndUnguarded))]
    public void testMaxCapacityWithRecycleAtDifferentThread(OwnerType ownerType, bool unguarded) => runTest(() =>
    {
        assumeIsPooling(ownerType);
        const int maxCapacity = 4;
        Recycler<HandledObject> recycler = newRecycler(ownerType, unguarded, maxCapacity, 4, 4);
        // Borrow 2 * maxCapacity objects.
        // Return the half from the same thread.
        // Return the other half from the different thread.
        var values = new HandledObject[maxCapacity * 3];
        for (int i = 0; i < values.Length; i++) values[i] = recycler.get();
        for (int i = 0; i < maxCapacity; i++) values[i].recycle();
        Thread thread = worker(() => { for (int i = maxCapacity; i < values.Length; i++) values[i].recycle(); });
        thread.Start(); join(thread);
        Assert.Equal(maxCapacity * 3 / 4, recycler.threadLocalSize());
        for (int i = 0; i < values.Length; i++) recycler.get();
        Assert.Equal(0, recycler.threadLocalSize());
    });

    [Theory]
    [MemberData(nameof(notNoneOwnerAndUnguarded))]
    public void testDiscardingExceedingElementsWithRecycleAtDifferentThread(OwnerType ownerType, bool unguarded) => runTest(() =>
    {
        const int maxCapacity = 32;
        int instances = 0;
        Recycler<HandledObject> recycler = newRecycler(ownerType, unguarded, maxCapacity, _ => Interlocked.Increment(ref instances));
        // Borrow 2 * maxCapacity objects.
        var values = new HandledObject[maxCapacity * 2];
        for (int i = 0; i < values.Length; i++) values[i] = recycler.get();
        Assert.Equal(values.Length, instances);
        // Reset counter.
        instances = 0;
        // Recycle from other thread.
        Thread thread = worker(() => { foreach (HandledObject value in values) value.recycle(); });
        thread.Start(); join(thread);
        Assert.Equal(0, instances);
        // Borrow 2 * maxCapacity objects. Half of them should come from
        // the recycler queue, the other half should be freshly allocated.
        for (int i = 0; i < values.Length; i++) recycler.get();
        // The implementation uses maxCapacity / 2 as limit per WeakOrderQueue
        Assert.True(values.Length - maxCapacity / 2 <= instances,
            "The instances count (" + instances + ") must be <= array.length (" + values.Length
                + ") - maxCapacity (" + maxCapacity + ") / 2 as we not pool all new handles internally");
    });
}
