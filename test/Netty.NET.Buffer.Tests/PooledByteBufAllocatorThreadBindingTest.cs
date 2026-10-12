/*
 * Copyright 2015 The Netty Project
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using static Netty.NET.Buffer.Tests.AllocatorLeakDetectionTest;

namespace Netty.NET.Buffer.Tests;

public class PooledByteBufAllocatorThreadBindingTest
{
    private static PooledByteBufAllocator NewAllocator(int heap, int direct, int alignment = 0,
        NativeMemoryAllocator native = null) => new(false, heap, direct, maxOrder: 4,
            directMemoryCacheAlignment: alignment, nativeAllocator: native);

    private static void AllocateAndRelease(PooledByteBufAllocator a, bool direct)
    {
        ByteBuf b = direct ? a.DirectBuffer(48) : a.HeapBuffer(48);
        try { b.WriteInt(42); Assert.Equal(42, b.ReadInt()); Assert.Same(a, b.Allocator); }
        finally { b.Release(); }
    }

    private static IReadOnlyList<IPoolArenaMetric> Arenas(PooledByteBufAllocator a, bool direct)
        => direct ? a.Metric.DirectArenas : a.Metric.HeapArenas;

    // Uses chunk-list identity only for test inspection, without adding public arena handles.
    private static int ArenaIndex(PooledByteBufAllocator a, ByteBuf b)
    {
        PoolChunk chunk = PooledStorage(b).Allocation.Chunk;
        return Arenas(a, b.IsDirect).Select((m, i) => (m, i))
            .Single(x => ((PoolArena)x.m).ChunkLists.Any(l => ReferenceEquals(l, chunk.Parent))).i;
    }

    [Theory]
    [InlineData(2, 0)] [InlineData(0, 2)] [InlineData(2, 2)]
    public async Task OriginalThreadToArenaMappingsAndBindingCountsRecoverReleasedArena(int heap, int direct)
    {
        using var a = NewAllocator(heap, direct);
        using var w0 = new Worker(); using var w1 = new Worker(); using var w2 = new Worker();
        bool useDirect = heap == 0;
        await w0.InvokeAsync(() => AllocateAndRelease(a, useDirect));
        Assert.Equal(1, a.Metric.NumThreadLocalCaches);
        foreach (var arenas in new[] { a.Metric.HeapArenas, a.Metric.DirectArenas })
            if (arenas.Count != 0) Assert.Equal(new[] { 1, 0 }, arenas.Select(m => m.NumThreadCaches));
        await w1.InvokeAsync(() => AllocateAndRelease(a, useDirect));
        Assert.Equal(2, a.Metric.NumThreadLocalCaches);
        foreach (var m in a.Metric.HeapArenas.Concat(a.Metric.DirectArenas)) Assert.Equal(1, m.NumThreadCaches);
        await w1.InvokeAsync(() => Assert.True(a.ReleaseCurrentThreadBinding()));
        Assert.Equal(1, a.Metric.NumThreadLocalCaches);
        foreach (var arenas in new[] { a.Metric.HeapArenas, a.Metric.DirectArenas })
            if (arenas.Count != 0) Assert.Equal(new[] { 1, 0 }, arenas.Select(m => m.NumThreadCaches));
        await w2.InvokeAsync(() => AllocateAndRelease(a, useDirect));
        Assert.Equal(2, a.Metric.NumThreadLocalCaches);
        foreach (var m in a.Metric.HeapArenas.Concat(a.Metric.DirectArenas)) Assert.Equal(1, m.NumThreadCaches);
        await w0.InvokeAsync(() => Assert.True(a.ReleaseCurrentThreadBinding()));
        await w2.InvokeAsync(() => Assert.True(a.ReleaseCurrentThreadBinding()));
        Assert.Equal(0, a.Metric.NumThreadLocalCaches);
        foreach (var m in a.Metric.HeapArenas.Concat(a.Metric.DirectArenas)) Assert.Equal(0, m.NumThreadCaches);
    }

    [Theory]
    [InlineData(1, 3, 0)] [InlineData(3, 1, 0)] [InlineData(2, 3, 0)] [InlineData(3, 2, 0)]
    [InlineData(1, 3, 64)] [InlineData(3, 1, 64)] [InlineData(2, 3, 64)] [InlineData(3, 2, 64)]
    public async Task UnequalArenaCountsPairBothKindsAndUseStableFirstMinimum(int heap, int direct, int alignment)
    {
        var native = new NativeMemoryAllocator();
        using var a = NewAllocator(heap, direct, alignment, native);
        var workers = Enumerable.Range(0, 7).Select(_ => new Worker()).ToArray();
        try
        {
            for (int i = 0; i < workers.Length; ++i)
            {
                int expected = i;
                await workers[i].InvokeAsync(() =>
                {
                    ByteBuf h = a.HeapBuffer(48), d = a.DirectBuffer(48);
                    try
                    {
                        Assert.Equal(expected % heap, ArenaIndex(a, h));
                        Assert.Equal(expected % direct, ArenaIndex(a, d));
                    }
                    finally { h.Release(); d.Release(); }
                });
                Assert.Equal(i + 1, a.Metric.NumThreadLocalCaches);
            }
            foreach (var arenas in new[] { a.Metric.HeapArenas, a.Metric.DirectArenas })
            {
                Assert.Equal(7, arenas.Sum(m => m.NumThreadCaches));
                Assert.InRange(arenas.Max(m => m.NumThreadCaches) - arenas.Min(m => m.NumThreadCaches), 0, 1);
            }
            Assert.Equal(a.Metric.HeapArenas.Sum(m => m.NumActiveBytes), a.Metric.UsedHeapMemory);
            Assert.Equal(a.Metric.DirectArenas.Sum(m => m.NumActiveBytes), a.Metric.UsedDirectMemory);
            Assert.Equal(0, a.PinnedHeapMemory); Assert.Equal(0, a.PinnedDirectMemory);
        }
        finally { foreach (Worker w in workers) w.Dispose(); }
        Assert.Equal(7, a.ReleaseTerminatedThreadBindings());
        Assert.Equal(0, a.ReleaseTerminatedThreadBindings());
        Assert.Equal(0, a.Metric.NumThreadLocalCaches);
        a.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RebindingChangesNewBufferArenaWhileResizeKeepsTheLiveBufferOwner(bool direct)
    {
        using var a = NewAllocator(direct ? 0 : 2, direct ? 2 : 0);
        using var w0 = new Worker(); using var w1 = new Worker(); using var w2 = new Worker();
        ByteBuf original = null;
        try
        {
            await w0.InvokeAsync(() =>
            {
                original = (direct ? a.DirectBuffer(48) : a.HeapBuffer(48)).WriteInt(42);
                Assert.Equal(0, ArenaIndex(a, original));
            });
            await w1.InvokeAsync(() => AllocateAndRelease(a, direct));
            await w2.InvokeAsync(() => AllocateAndRelease(a, direct));
            await w1.InvokeAsync(() => Assert.True(a.ReleaseCurrentThreadBinding()));
            await w0.InvokeAsync(() =>
            {
                Assert.True(a.ReleaseCurrentThreadBinding()); Assert.False(a.ReleaseCurrentThreadBinding());
                original.Capacity = 65536; Assert.Equal(42, original.GetInt(0));
                Assert.Equal(0, ArenaIndex(a, original));
                ByteBuf copy = original.Copy();
                try { Assert.Equal(1, ArenaIndex(a, copy)); Assert.Equal(42, copy.GetInt(0)); }
                finally { copy.Release(); }
            });
            Assert.Equal(new[] { 1, 1 }, Arenas(a, direct).Select(m => m.NumThreadCaches));
        }
        finally { original?.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task TerminatedBindingsAreReapedByExplicitCleanupMetricReadOrNewAssignment(int trigger)
    {
        using var a = NewAllocator(2, 2);
        using (var worker = new Worker()) await worker.InvokeAsync(() => AllocateAndRelease(a, false));
        Assert.Equal(1, a.Metric.HeapArenas[0].NumThreadCaches);
        Assert.Equal(1, a.Metric.DirectArenas[0].NumThreadCaches);
        using var live = new Worker();
        if (trigger == 0) Assert.Equal(1, a.ReleaseTerminatedThreadBindings());
        else if (trigger == 1) Assert.Equal(0, a.Metric.NumThreadLocalCaches);
        else await live.InvokeAsync(() => AllocateAndRelease(a, true));
        Assert.Equal(trigger == 2 ? 1 : 0, a.Metric.NumThreadLocalCaches);
        Assert.Equal(0, a.Metric.HeapArenas[1].NumThreadCaches);
        Assert.Equal(0, a.Metric.DirectArenas[1].NumThreadCaches);
        Assert.Equal(0, a.ReleaseTerminatedThreadBindings());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FastResizeDoesNotCreateCallerBindingOrMoveReservation(bool direct)
    {
        using var a = NewAllocator(2, 2);
        ByteBuf b = null;
        try
        {
            using (var exiting = new Worker()) await exiting.InvokeAsync(() =>
                b = (direct ? a.DirectBuffer(500) : a.HeapBuffer(500)).WriteInt(42));
            Assert.Equal(1, a.ReleaseTerminatedThreadBindings());
            b.Capacity = 510; Assert.Equal(42, b.GetInt(0));
            Assert.Equal(0, ArenaIndex(a, b));
            Assert.Equal(0, a.Metric.NumThreadLocalCaches);
            Assert.Equal(1, Arenas(a, direct)[0].NumAllocations);
            b.Capacity = 509; Assert.Equal(0, a.Metric.NumThreadLocalCaches);
            Assert.Equal(1, Arenas(a, direct)[0].NumAllocations);
        }
        finally { b?.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ThreadExitDoesNotReturnBuffersAndGrowthRetainsOwningArena(bool direct)
    {
        var native = new NativeMemoryAllocator();
        using var a = NewAllocator(2, 2, native: native);
        using var live = new Worker();
        await live.InvokeAsync(() => AllocateAndRelease(a, direct));
        ByteBuf orphan = null;
        try
        {
            using (var exiting = new Worker()) await exiting.InvokeAsync(() =>
            {
                orphan = (direct ? a.DirectBuffer(48) : a.HeapBuffer(48)).WriteInt(42);
                Assert.Equal(1, ArenaIndex(a, orphan));
            });
            Assert.Equal(1, a.ReleaseTerminatedThreadBindings());
            Assert.Equal(1, a.Metric.NumThreadLocalCaches);
            orphan.Capacity = 65536;
            Assert.Equal(1, ArenaIndex(a, orphan)); Assert.Equal(42, orphan.GetInt(0));
            Assert.Equal(2, a.Metric.NumThreadLocalCaches);
            Assert.Equal(1, Arenas(a, direct)[1].NumActiveAllocations);
            Assert.Equal(65536, direct ? a.PinnedDirectMemory : a.PinnedHeapMemory);
        }
        finally { orphan?.Release(); }
        a.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public async Task ExecutionContextFlowDoesNotSharePhysicalThreadArenaBinding()
    {
        using var a = NewAllocator(2, 2);
        using var w0 = new Worker(); using var w1 = new Worker();
        var marker = new AsyncLocal<string>(); ExecutionContext context = null;
        await w0.InvokeAsync(() =>
        {
            marker.Value = "flowed"; AllocateAndRelease(a, false); context = ExecutionContext.Capture();
        });
        Assert.NotNull(context);
        await w1.InvokeAsync(() => ExecutionContext.Run(context, _ =>
        {
            Assert.Equal("flowed", marker.Value);
            ByteBuf b = a.HeapBuffer(48);
            try { Assert.Equal(1, ArenaIndex(a, b)); }
            finally { b.Release(); }
        }, null));
        Assert.Equal(2, a.Metric.NumThreadLocalCaches);
        Assert.Equal(new[] { 1, 1 }, a.Metric.HeapArenas.Select(m => m.NumThreadCaches));
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        await w0.InvokeAsync(() => AllocateAndRelease(a, false));
        await w1.InvokeAsync(() => AllocateAndRelease(a, false));
        Assert.Equal(new long[] { 2, 2 }, a.Metric.HeapArenas.Select(m => m.NumAllocations));
    }

    [Theory]
    [InlineData(0)] [InlineData(64)]
    public async Task FailedNativeAllocationKeepsChosenBindingForRetry(int alignment)
    {
        var native = new NativeMemoryAllocator(131072 + alignment);
        using var a = NewAllocator(2, 2, alignment, native);
        using var w0 = new Worker(); using var w1 = new Worker();
        ByteBuf held = null;
        try
        {
            await w0.InvokeAsync(() => held = a.DirectBuffer(131072).WriteInt(42));
            await w1.InvokeAsync(() => Assert.Throws<OutOfMemoryException>(() => a.DirectBuffer(48)));
            Assert.Equal(2, a.Metric.NumThreadLocalCaches);
            Assert.Equal(0, a.Metric.DirectArenas[1].NumAllocations);
            Assert.Equal(0, a.Metric.DirectArenas[1].NumActiveBytes);
            Assert.Equal(1, a.Metric.DirectArenas[1].NumThreadCaches);
            Assert.Equal(42, held.GetInt(0)); held.Release(); held = null;
            await w1.InvokeAsync(() =>
            {
                ByteBuf b = a.DirectBuffer(48);
                try { Assert.Equal(1, ArenaIndex(a, b)); }
                finally { b.Release(); }
            });
            Assert.Equal(1, a.Metric.DirectArenas[1].NumAllocations);
        }
        finally { held?.Release(); }
        a.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(0)] [InlineData(64)]
    public async Task MultiArenaShutdownDetachesLiveThreadsAndDefersStorageUntilLastLease(int alignment)
    {
        var native = new NativeMemoryAllocator();
        using var a = NewAllocator(3, 3, alignment, native);
        var workers = Enumerable.Range(0, 3).Select(_ => new Worker()).ToArray();
        var buffers = new ByteBuf[3]; var leases = new BufferMemoryLease[3];
        try
        {
            for (int i = 0; i < 3; ++i)
            {
                int k = i;
                await workers[i].InvokeAsync(() =>
                {
                    buffers[k] = a.DirectBuffer(new[] { 48, 131072, 131073 }[k]).WriteInt(k + 1);
                    leases[k] = buffers[k].AcquireReadLease();
                });
            }
            Assert.Equal(3, a.Metric.NumThreadLocalCaches);
            Assert.Equal(a.Metric.DirectArenas.Sum(m => m.NumActiveBytes), a.Metric.UsedDirectMemory);
            Assert.Equal(a.Metric.DirectArenas.Cast<PoolArena>().Sum(m => m.NumPinnedBytes), a.PinnedDirectMemory);
            long pinned = a.PinnedDirectMemory;
            a.Dispose(); a.Dispose(); Assert.Equal(0, a.Metric.NumThreadLocalCaches);
            Assert.All(a.Metric.HeapArenas.Concat(a.Metric.DirectArenas), m => Assert.Equal(0, m.NumThreadCaches));
            for (int i = 0; i < 3; ++i)
            {
                Assert.Equal(i + 1, buffers[i].GetInt(0)); buffers[i].Release(); buffers[i] = null;
                await workers[i].InvokeAsync(() =>
                {
                    Assert.False(a.ReleaseCurrentThreadBinding());
                    Assert.Throws<ObjectDisposedException>(() => a.DirectBuffer(48));
                });
            }
            Assert.Equal(pinned, a.PinnedDirectMemory);
            Assert.Equal(3, a.Metric.DirectArenas.Sum(m => m.NumActiveAllocations));
        }
        finally
        {
            foreach (ByteBuf b in buffers) b?.Release();
            foreach (BufferMemoryLease lease in leases) lease.Dispose();
            foreach (Worker w in workers) w.Dispose();
        }
        Assert.Equal(0, native.ReservedBytes); Assert.Equal(0, a.PinnedDirectMemory);
        Assert.Equal(0, a.Metric.UsedDirectMemory);
        Assert.All(a.Metric.DirectArenas, m => Assert.Equal(m.NumAllocations, m.NumDeallocations));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ConcurrentAssignmentReleaseAndShutdownKeepExactLifetimes(bool direct)
    {
        var native = new NativeMemoryAllocator();
        using var a = NewAllocator(3, 2, native: native);
        var workers = Enumerable.Range(0, 8).Select(_ => new Worker()).ToArray();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int startedCount = 0;
        try
        {
            Task[] actions = workers.Select((w, k) => w.InvokeAsync(() =>
            {
                for (int i = 0; i < 512; ++i)
                {
                    ByteBuf b;
                    try { b = direct ? a.DirectBuffer(48) : a.HeapBuffer(48); }
                    catch (ObjectDisposedException) { break; }
                    try
                    {
                        b.WriteInt(k); Assert.Equal(k, b.GetInt(0));
                        if (i == 0 && Interlocked.Increment(ref startedCount) == 8) started.SetResult();
                    }
                    finally { b.Release(); }
                    if (i % 3 == 0) a.ReleaseCurrentThreadBinding();
                }
            })).ToArray();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            a.Dispose(); await Task.WhenAll(actions);
            Assert.Equal(0, a.Metric.NumThreadLocalCaches);
            foreach (var m in a.Metric.HeapArenas.Concat(a.Metric.DirectArenas))
            {
                Assert.Equal(0, m.NumThreadCaches); Assert.Equal(0, m.NumActiveAllocations);
                Assert.Equal(m.NumAllocations, m.NumDeallocations); Assert.Equal(0, m.NumActiveBytes);
            }
        }
        finally { foreach (Worker w in workers) w.Dispose(); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(-1, 0)] [InlineData(0, -1)]
    public void NegativeArenaCountsFailBeforeBackingReservation(int heap, int direct)
    {
        var native = new NativeMemoryAllocator(0);
        Assert.Throws<ArgumentOutOfRangeException>(() => NewAllocator(heap, direct, native: native));
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void FullyUnpooledConfigurationNeedsNoThreadBinding()
    {
        using var a = NewAllocator(0, 0);
        AllocateAndRelease(a, false); AllocateAndRelease(a, true);
        Assert.Equal(0, a.Metric.NumThreadLocalCaches);
        Assert.False(a.ReleaseCurrentThreadBinding()); Assert.Equal(0, a.ReleaseTerminatedThreadBindings());
        Assert.Equal(-1, a.Metric.UsedHeapMemory); Assert.Equal(-1, a.Metric.UsedDirectMemory);
    }

    // A dedicated live thread makes affinity and termination deterministic; no sleeps/GC
    // polling are used to infer thread exit. Every operation and join has a finite timeout.
    private sealed class Worker : IDisposable
    {
        private readonly BlockingCollection<(Action Action, TaskCompletionSource Completion)> _queue = new();
        private readonly Thread _thread;
        internal Worker()
        {
            _thread = new Thread(() =>
            {
                foreach (var command in _queue.GetConsumingEnumerable())
                {
                    try { command.Action(); command.Completion.SetResult(); }
                    catch (Exception error) { command.Completion.SetException(error); }
                }
            }) { IsBackground = true };
            _thread.Start();
        }
        internal Task InvokeAsync(Action action)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add((action, completion));
            return completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        public void Dispose()
        {
            _queue.CompleteAdding();
            Assert.True(_thread.Join(TimeSpan.FromSeconds(10)), "Dedicated allocator test thread did not finish.");
            _queue.Dispose();
        }
    }
}
