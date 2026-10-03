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
    protected static bool IsPooling(OwnerType type) => type != OwnerType.FAST_THREAD_LOCAL ||
        FastThreadLocalThread.CurrentThreadWillCleanupFastThreadLocals();
    protected static void AssumeIsPooling(OwnerType type) => Xunit.Assert.SkipUnless(IsPooling(type), "Owner has no automatic thread-local cleanup.");
    public static IEnumerable<object[]> OwnerTypeAndUnguarded()
        => Enum.GetValues<OwnerType>().SelectMany(owner => new[] { true, false }.Select(unguarded => new object[] { owner, unguarded }));
    public static IEnumerable<object[]> NotNoneOwnerAndUnguarded()
        => OwnerTypeAndUnguarded().Where(row => (OwnerType)row[0] != OwnerType.NONE);
    public static IEnumerable<object[]> Owners() => Enum.GetValues<OwnerType>().Select(owner => new object[] { owner });
    private readonly ConcurrentQueue<Exception> workerFailures = new();
    protected virtual void RunTest(Action invocation) => invocation();
    protected virtual Thread NewThread(Action invocation) => new(invocation.Invoke) { IsBackground = true };
    protected Thread Worker(Action invocation) => NewThread(() =>
    {
        try { invocation(); }
        catch (Exception error) { workerFailures.Enqueue(error); }
    });
    protected void Join(Thread thread)
    {
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Empty(workerFailures);
    }
    protected sealed class HandledObject
    {
        internal readonly IRecyclerHandle<HandledObject> handle;
        internal HandledObject(IRecyclerHandle<HandledObject> handle) => this.handle = handle;
        internal void Recycle() => handle.Recycle(this);
    }
    private sealed class Pool : Recycler<HandledObject>
    {
        private readonly Action<HandledObject> onNewObject;
        internal Pool(int capacity, bool unguarded, Action<HandledObject> onNewObject) : base(capacity, unguarded) => this.onNewObject = onNewObject;
        internal Pool(Thread owner, int capacity, int ratio, int chunk, bool unguarded, Action<HandledObject> onNewObject)
            : base(capacity, ratio, chunk, owner, unguarded) => this.onNewObject = onNewObject;
        internal Pool(int capacity, int ratio, int chunk, bool unguarded, Action<HandledObject> onNewObject)
            : base(capacity, ratio, chunk, unguarded) => this.onNewObject = onNewObject;
        protected override HandledObject NewObject(IRecyclerHandle<HandledObject> handle)
        {
            var value = new HandledObject(handle);
            onNewObject?.Invoke(value);
            return value;
        }
    }
    protected static Recycler<HandledObject> NewRecycler(OwnerType owner, bool unguarded, int capacity, Action<HandledObject> onNewObject = null)
        => NewRecycler(owner, unguarded, capacity, Recycler.RATIO, capacity >> 1, onNewObject);
    protected static Recycler<HandledObject> NewRecycler(bool unguarded, int capacity)
        => NewRecycler(OwnerType.FAST_THREAD_LOCAL, unguarded, capacity);
    protected static Recycler<HandledObject> NewRecycler(int capacity)
        => NewRecycler(OwnerType.FAST_THREAD_LOCAL, false, capacity, 8, capacity >> 1);
    protected static Recycler<HandledObject> NewRecycler(OwnerType owner, bool unguarded, int capacity, int ratio, int chunk, Action<HandledObject> onNewObject = null)
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
    private WeakReference<Thread> StartCollectibleThread(OwnerType owner, bool unguarded, Action<HandledObject> retain)
    {
        Thread thread = Worker(() =>
        {
            Recycler<HandledObject> recycler = NewRecycler(owner, unguarded, 1024);
            HandledObject value = recycler.Get();
            // Store a reference to the HandledObject to ensure it is not collected when the run method finish.
            retain(value);
            Recycler.UnpinOwner(recycler);
        });
        var weak = new WeakReference<Thread>(thread);
        thread.Start();
        Join(thread);
        // Null out so it can be collected.
        return weak;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Collected(WeakReference<Thread> thread) => !thread.TryGetTarget(out _);

    [Theory]
    [MemberData(nameof(OwnerTypeAndUnguarded))]
    public virtual void TestThreadCanBeCollectedEvenIfHandledObjectIsReferenced(OwnerType ownerType, bool unguarded) => RunTest(() =>
    {
        HandledObject retained = null;
        WeakReference<Thread> thread = StartCollectibleThread(ownerType, unguarded, value => retained = value);
        // Loop until the Thread was collected. If we can not collect it the Test will fail due of a timeout.
        var deadline = Stopwatch.StartNew();
        while (!Collected(thread) && deadline.Elapsed < TimeSpan.FromSeconds(5))
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Thread.Sleep(50);
        }
        Assert.True(Collected(thread));
        // Now call recycle after the Thread was collected to ensure this still works...
        retained?.Recycle();
        GC.KeepAlive(retained);
    });

    [Theory]
    [MemberData(nameof(OwnerTypeAndUnguarded))]
    public void VerySmallRecycer(OwnerType ownerType, bool unguarded) => RunTest(() => NewRecycler(ownerType, unguarded, 2, 0, 1).Get());

    [Theory]
    [MemberData(nameof(Owners))]
    public void TestMultipleRecycle(OwnerType ownerType) => RunTest(() =>
    {
        // This test makes only sense for guarded recyclers
        HandledObject value = NewRecycler(ownerType, false, 1024).Get();
        value.Recycle();
        if (IsPooling(ownerType)) Assert.Throws<InvalidOperationException>(value.Recycle);
        else value.Recycle(); // No-op because not pooling.
    });

    [Fact]
    public void TestUnguardedMultipleRecycle() => RunTest(() =>
    {
        HandledObject value = NewRecycler(true, 1024).Get();
        value.Recycle();
        value.Recycle();
    });

    [Theory]
    [MemberData(nameof(Owners))]
    public void TestMultipleRecycleAtDifferentThread(OwnerType ownerType) => RunTest(() =>
    {
        // This test makes only sense for guarded recyclers
        Recycler<HandledObject> recycler = NewRecycler(ownerType, false, 1024);
        HandledObject value = recycler.Get();
        Exception failure = null;
        Thread first = Worker(value.Recycle);
        first.Start(); Join(first);
        Thread second = Worker(() => { try { value.Recycle(); } catch (InvalidOperationException e) { failure = e; } });
        second.Start(); Join(second);
        Assert.NotSame(recycler.Get(), recycler.Get());
        if (IsPooling(ownerType)) Assert.NotNull(failure);
        else Assert.Null(failure);
    });

    [Theory]
    [MemberData(nameof(Owners))]
    public void TestMultipleRecycleAtDifferentThreadRacing(OwnerType ownerType) => RunTest(() =>
    {
        // This test makes only sense for guarded recyclers
        Recycler<HandledObject> recycler = NewRecycler(ownerType, false, 1024);
        HandledObject value = recycler.Get();
        Exception failure = null;
        int failures = 0;
        using var done = new CountdownEvent(2);
        Action recycle = () =>
        {
            try { value.Recycle(); }
            catch (InvalidOperationException e) { Interlocked.Exchange(ref failure, e); Interlocked.Increment(ref failures); }
            finally { done.Signal(); }
        };
        Thread first = Worker(recycle), second = Worker(recycle);
        first.Start(); second.Start();
        try
        {
            Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
            Assert.NotSame(recycler.Get(), recycler.Get());
            if (failure != null)
            {
                Assert.Contains("recycled already", failure.Message);
                Assert.Equal(1, failures);
            }
        }
        finally { Join(first); Join(second); }
    });

    [Theory]
    [MemberData(nameof(Owners))]
    public void TestMultipleRecycleRacing(OwnerType ownerType) => RunTest(() =>
    {
        // This test makes only sense for guarded recyclers
        Recycler<HandledObject> recycler = NewRecycler(ownerType, false, 1024);
        HandledObject value = recycler.Get();
        Exception failure = null;
        using var done = new CountdownEvent(1);
        Thread first = Worker(() =>
        {
            try { value.Recycle(); }
            catch (InvalidOperationException e) { Interlocked.Exchange(ref failure, e); }
            finally { done.Signal(); }
        });
        first.Start();
        try { value.Recycle(); }
        catch (InvalidOperationException e) { Interlocked.Exchange(ref failure, e); }
        try
        {
            Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
            Assert.NotSame(recycler.Get(), recycler.Get());
            if (IsPooling(ownerType)) Assert.NotNull(failure); // Object got recycled twice, so at least one of the calls must throw.
            else Assert.Null(failure);
        }
        finally { Join(first); }
    });

    [Theory]
    [MemberData(nameof(OwnerTypeAndUnguarded))]
    public void TestRecycle(OwnerType ownerType, bool unguarded) => RunTest(() =>
    {
        Recycler<HandledObject> recycler = NewRecycler(ownerType, unguarded, 1024);
        HandledObject value = recycler.Get();
        value.Recycle();
        HandledObject other = recycler.Get();
        if (IsPooling(ownerType)) Assert.Same(value, other);
        else Assert.NotSame(value, other);
        other.Recycle();
    });
    [Theory]
    [MemberData(nameof(OwnerTypeAndUnguarded))]
    public void TestRecycleDisable(OwnerType ownerType, bool unguarded) => RunTest(() =>
    {
        Recycler<HandledObject> recycler = NewRecycler(ownerType, unguarded, -1);
        HandledObject value = recycler.Get();
        value.Recycle();
        HandledObject other = recycler.Get();
        Assert.NotSame(value, other);
        other.Recycle();
    });
    [Theory]
    [MemberData(nameof(OwnerTypeAndUnguarded))]
    public void TestRecycleDisableDrop(OwnerType ownerType, bool unguarded) => RunTest(() =>
    {
        AssumeIsPooling(ownerType);
        Recycler<HandledObject> recycler = NewRecycler(ownerType, unguarded, 1024, 0, 16);
        HandledObject value = recycler.Get();
        value.Recycle();
        HandledObject other = recycler.Get();
        Assert.Same(value, other);
        other.Recycle();
        HandledObject third = recycler.Get();
        Assert.Same(value, third);
        third.Recycle();
    });

    /**
     * Test to make sure bug #2848 never happens again
     * https://github.com/netty/netty/issues/2848
     */
    [Theory]
    [MemberData(nameof(OwnerTypeAndUnguarded))]
    public void TestMaxCapacity(OwnerType ownerType, bool unguarded) => RunTest(() =>
    {
        CheckMaxCapacity(ownerType, unguarded, 300);
        var rand = new Random(2848);
        for (int i = 0; i < 50; i++) CheckMaxCapacity(ownerType, unguarded, rand.Next(1000) + 256); // 256 - 1256
    });
    private static void CheckMaxCapacity(OwnerType owner, bool unguarded, int capacity)
    {
        Recycler<HandledObject> recycler = NewRecycler(owner, unguarded, capacity);
        var values = new HandledObject[capacity * 3];
        for (int i = 0; i < values.Length; i++) values[i] = recycler.Get();
        for (int i = 0; i < values.Length; i++) { values[i].Recycle(); values[i] = null; }
        Assert.True(MathUtil.FindNextPositivePowerOfTwo(capacity) >= recycler.ThreadLocalSize(),
            "The threadLocalSize (" + recycler.ThreadLocalSize() + ") must be <= maxCapacity ("
                + capacity + ") as we not pool all new handles internally");
    }

    [Theory]
    [MemberData(nameof(NotNoneOwnerAndUnguarded))]
    public void TestRecycleAtDifferentThread(OwnerType ownerType, bool unguarded) => RunTest(() =>
    {
        AssumeIsPooling(ownerType);
        Recycler<HandledObject> recycler = NewRecycler(ownerType, unguarded, 256, 2, 16);
        HandledObject first = recycler.Get(), second = recycler.Get();
        Thread thread = Worker(() => { first.Recycle(); second.Recycle(); });
        thread.Start(); Join(thread);
        Assert.Same(recycler.Get(), first);
        Assert.NotSame(recycler.Get(), second);
    });

    [Theory]
    [MemberData(nameof(OwnerTypeAndUnguarded))]
    public void TestRecycleAtTwoThreadsMulti(OwnerType ownerType, bool unguarded) => RunTest(() =>
    {
        AssumeIsPooling(ownerType);
        Recycler<HandledObject> recycler = NewRecycler(ownerType, unguarded, 256);
        HandledObject first = recycler.Get(), second = null;
        using var work = new BlockingCollection<Action>();
        Thread single = Worker(() => { foreach (Action action in work.GetConsumingEnumerable()) action(); });
        single.Start();
        try
        {
            using var latch1 = new CountdownEvent(1);
            work.Add(() => { first.Recycle(); latch1.Signal(); });
            Assert.True(latch1.Wait(TimeSpan.FromMilliseconds(100)));
            second = recycler.Get();
            // Always recycler the first object, that is Ok
            Assert.Same(second, first);
            using var latch2 = new CountdownEvent(1);
            work.Add(() =>
            {
                //The object should be recycled
                second.Recycle(); latch2.Signal();
            });
            Assert.True(latch2.Wait(TimeSpan.FromMilliseconds(100)));
            // It should be the same object, right?
            Assert.Same(recycler.Get(), first);
        }
        finally { work.CompleteAdding(); Join(single); }
    });

    [Theory]
    [MemberData(nameof(NotNoneOwnerAndUnguarded))]
    public void TestMaxCapacityWithRecycleAtDifferentThread(OwnerType ownerType, bool unguarded) => RunTest(() =>
    {
        AssumeIsPooling(ownerType);
        const int maxCapacity = 4;
        Recycler<HandledObject> recycler = NewRecycler(ownerType, unguarded, maxCapacity, 4, 4);
        // Borrow 2 * maxCapacity objects.
        // Return the half from the same thread.
        // Return the other half from the different thread.
        var values = new HandledObject[maxCapacity * 3];
        for (int i = 0; i < values.Length; i++) values[i] = recycler.Get();
        for (int i = 0; i < maxCapacity; i++) values[i].Recycle();
        Thread thread = Worker(() => { for (int i = maxCapacity; i < values.Length; i++) values[i].Recycle(); });
        thread.Start(); Join(thread);
        Assert.Equal(maxCapacity * 3 / 4, recycler.ThreadLocalSize());
        for (int i = 0; i < values.Length; i++) recycler.Get();
        Assert.Equal(0, recycler.ThreadLocalSize());
    });

    [Theory]
    [MemberData(nameof(NotNoneOwnerAndUnguarded))]
    public void TestDiscardingExceedingElementsWithRecycleAtDifferentThread(OwnerType ownerType, bool unguarded) => RunTest(() =>
    {
        const int maxCapacity = 32;
        int instances = 0;
        Recycler<HandledObject> recycler = NewRecycler(ownerType, unguarded, maxCapacity, _ => Interlocked.Increment(ref instances));
        // Borrow 2 * maxCapacity objects.
        var values = new HandledObject[maxCapacity * 2];
        for (int i = 0; i < values.Length; i++) values[i] = recycler.Get();
        Assert.Equal(values.Length, instances);
        // Reset counter.
        instances = 0;
        // Recycle from other thread.
        Thread thread = Worker(() => { foreach (HandledObject value in values) value.Recycle(); });
        thread.Start(); Join(thread);
        Assert.Equal(0, instances);
        // Borrow 2 * maxCapacity objects. Half of them should come from
        // the recycler queue, the other half should be freshly allocated.
        for (int i = 0; i < values.Length; i++) recycler.Get();
        // The implementation uses maxCapacity / 2 as limit per WeakOrderQueue
        Assert.True(values.Length - maxCapacity / 2 <= instances,
            "The instances count (" + instances + ") must be <= array.length (" + values.Length
                + ") - maxCapacity (" + maxCapacity + ") / 2 as we not pool all new handles internally");
    });
}
