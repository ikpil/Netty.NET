/*
 * Copyright 2012 The Netty Project
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class PoolThreadCacheTest
{
    private static PooledByteBufAllocator NewAllocator(int size = 4, int max = 32768, int trim = 8192,
        int arenas = 1, NativeMemoryAllocator native = null, int alignment = 0)
        => new(false, arenas, arenas, maxOrder: 4, directMemoryCacheAlignment: alignment, nativeAllocator: native,
            smallCacheSize: size, normalCacheSize: size, maxCachedBufferCapacity: max, cacheTrimInterval: trim);
    private static PooledByteBuf Allocate(PooledByteBufAllocator a, bool direct, int size)
        => Assert.IsAssignableFrom<PooledByteBuf>(direct ? a.DirectBuffer(size) : a.HeapBuffer(size));
    private static IPoolArenaMetric Arena(PooledByteBufAllocator a, bool direct)
        => (direct ? a.Metric.DirectArenas : a.Metric.HeapArenas)[0];

    // Adapted from PooledByteBufAllocatorTest.testCacheWorksForNormalAllocations.
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalNormalCacheCapacityBoundaryDoesNotCountHitsAsArenaAllocations(bool direct)
    {
        using var a = NewAllocator();
        var m = Arena(a, direct);
        Allocate(a, direct, 32768).Release();
        Assert.Equal(1, m.NumNormalAllocations); Assert.Equal(0, m.NumNormalDeallocations);
        // Should come out of the cache so the count should not be incremented
        Allocate(a, direct, 32768).Release();
        Assert.Equal(1, m.NumNormalAllocations);
        // Should be allocated without cache and also not put back in a cache.
        Allocate(a, direct, 32769).Release(); Allocate(a, direct, 32769).Release();
        Assert.Equal(3, m.NumNormalAllocations); Assert.Equal(2, m.NumNormalDeallocations);
        Assert.Equal(1, m.NumActiveAllocations); Assert.Equal(0, ((PoolArena)m).NumPinnedBytes);
        Assert.True(a.ReleaseCurrentThreadBinding());
        Assert.Equal(m.NumAllocations, m.NumDeallocations);
    }

    [Theory]
    [InlineData(false, 48)] [InlineData(true, 48)]
    [InlineData(false, 32768)] [InlineData(true, 32768)]
    public void CacheIsBoundedPerClassRoundedToPowerOfTwoAndReusesHandlesInFifoOrder(bool direct, int size)
    {
        using var a = NewAllocator(size: 3);
        var m = Arena(a, direct);
        Assert.Equal(3, a.Metric.SmallCacheSize); Assert.Equal(3, a.Metric.NormalCacheSize);
        var buffers = Enumerable.Range(0, 5).Select(_ => Allocate(a, direct, size)).ToArray();
        var originals = buffers.Select(b => b.Allocation).ToArray();
        foreach (var b in buffers) b.Release();
        Assert.Equal(5, m.NumAllocations); Assert.Equal(1, m.NumDeallocations);
        Assert.Equal(0, ((PoolArena)m).NumPinnedBytes);
        var reused = Enumerable.Range(0, 4).Select(_ => Allocate(a, direct, size)).ToArray();
        for (int i = 0; i < 4; ++i)
        {
            Assert.Same(originals[i].Storage, reused[i].Allocation.Storage);
            Assert.Equal(originals[i].Handle, reused[i].Allocation.Handle);
            Assert.NotSame(originals[i], reused[i].Allocation);
        }
        Assert.Equal(5, m.NumAllocations);
        foreach (var b in reused) b.Release();
        a.ReleaseCurrentThreadBinding();
        Assert.Equal(5, m.NumDeallocations);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SmallCacheIgnoresNormalCapacityLimitAndClassesRemainIndependent(bool direct)
    {
        using var a = NewAllocator(size: 1, max: 0);
        var m = Arena(a, direct);
        var x = Allocate(a, direct, 48); var y = Allocate(a, direct, 64); x.Release(); y.Release();
        Allocate(a, direct, 48).Release(); Allocate(a, direct, 64).Release();
        Assert.Equal(2, m.NumSmallAllocations); Assert.Equal(0, m.NumSmallDeallocations);
        Allocate(a, direct, 32768).Release(); Allocate(a, direct, 32768).Release();
        Assert.Equal(2, m.NumNormalAllocations); Assert.Equal(2, m.NumNormalDeallocations);
    }

    [Theory]
    [InlineData(false, 48)] [InlineData(true, 48)]
    [InlineData(false, 32768)] [InlineData(true, 32768)]
    public void TrimUsesSuccessfulHitsSinceLastTrimAndDoesNotCreateBindings(bool direct, int size)
    {
        using var a = NewAllocator(); var m = Arena(a, direct);
        Assert.False(a.TrimCurrentThreadCache()); Assert.Equal(0, a.Metric.NumThreadLocalCaches);
        var buffers = Enumerable.Range(0, 4).Select(_ => Allocate(a, direct, size)).ToArray();
        foreach (var b in buffers) b.Release();
        for (int i = 0; i < 4; ++i) Allocate(a, direct, size).Release();
        Assert.True(a.TrimCurrentThreadCache()); Assert.Equal(0, m.NumDeallocations);
        // No hits after the previous trim: the next trim returns all four handles.
        Assert.True(a.TrimCurrentThreadCache()); Assert.Equal(4, m.NumDeallocations);
        Assert.Equal(0, m.NumActiveAllocations);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AutomaticSweepCountsEligibleMissesAndHitsButExcludesHugeRequests(bool direct)
    {
        using var a = NewAllocator(trim: 4); var m = Arena(a, direct);
        Allocate(a, direct, 48).Release(); // eligible miss, attempt 1
        Allocate(a, direct, 200000).Release(); // huge: not an attempt
        Allocate(a, direct, 32768).Release(); // eligible miss, attempt 2
        Allocate(a, direct, 32768).Release(); // hit, attempt 3
        Assert.Equal(0, m.NumSmallDeallocations);
        var live = Allocate(a, direct, 32768); // hit, attempt 4: trim unused small class
        Assert.Equal(1, m.NumSmallDeallocations); Assert.Equal(0, m.NumNormalDeallocations);
        Assert.Equal(1, m.NumHugeAllocations); Assert.Equal(1, m.NumHugeDeallocations);
        Assert.Equal(32768, ((PoolArena)m).NumPinnedBytes);
        live.Release(); a.ReleaseCurrentThreadBinding(); Assert.Equal(0, m.NumActiveAllocations);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OperationLeaseDefersReturnAndLateReleaseBypassesDetachedCache(bool direct)
    {
        var native = new NativeMemoryAllocator(); using var a = NewAllocator(native: native);
        var m = Arena(a, direct); var b = Allocate(a, direct, 48);
        var lease = b.AcquireReadLease(); b.Release();
        Assert.Equal(48, ((PoolArena)m).NumPinnedBytes);
        a.ReleaseCurrentThreadBinding();
        Assert.Equal(0, m.NumDeallocations);
        a.Dispose(); Assert.Equal(1, m.NumActiveAllocations);
        lease.Dispose();
        Assert.Equal(0, m.NumActiveAllocations); Assert.Equal(0, m.NumActiveChunks);
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void NativePinHoldsPhysicalStorageWhileCachedHandleCanBeReused()
    {
        var native = new NativeMemoryAllocator(); using var a = NewAllocator(native: native, alignment: 64);
        var b = Allocate(a, true, 48); var original = b.Allocation;
        using (var pin = b.AsMemory(0, 1).Pin())
        {
            b.Release(); Assert.Equal(0, a.PinnedDirectMemory);
            var next = Allocate(a, true, 48);
            Assert.Same(original.Storage, next.Allocation.Storage); Assert.Equal(original.Handle, next.Allocation.Handle);
            next.Release(); a.Dispose();
            Assert.Equal(0, a.Metric.UsedDirectMemory); Assert.True(native.ReservedBytes > 0);
        }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CrossThreadReturnUsesOriginalCacheAndClosedCacheNeverAcceptsLateReturns(bool direct)
    {
        using var a = NewAllocator(); using var worker = new Worker(); using var owner = new Worker();
        PooledByteBuf b = null;
        await owner.InvokeAsync(() => b = Allocate(a, direct, 48));
        var original = b.Allocation; var m = Arena(a, direct);
        await worker.InvokeAsync(() => b.Release());
        PooledByteBuf next = null;
        await owner.InvokeAsync(() =>
        {
            next = Allocate(a, direct, 48);
            Assert.Same(original.Storage, next.Allocation.Storage); Assert.Equal(original.Handle, next.Allocation.Handle);
            Assert.Equal(1, m.NumAllocations);
            a.ReleaseCurrentThreadBinding();
        });
        await worker.InvokeAsync(() => next.Release());
        Assert.Equal(1, m.NumDeallocations); Assert.Equal(0, a.Metric.NumThreadLocalCaches);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ResizeCanHitCallersCacheFromAnotherArenaThenReturnToActualOwner(bool direct)
    {
        using var a = NewAllocator(arenas: 2); using var worker = new Worker(); using var owner = new Worker();
        PooledByteBuf b = null;
        await owner.InvokeAsync(() => { b = Allocate(a, direct, 48); b.WriteInt(0x12345678); });
        var oldArena = b.Allocation.Arena; PoolArena callerArena = null;
        await worker.InvokeAsync(() =>
        {
            var cached = Allocate(a, direct, 32768); callerArena = cached.Allocation.Arena; cached.Release();
            Assert.NotSame(oldArena, callerArena);
            b.Capacity = 32768;
            Assert.Same(callerArena, b.Allocation.Arena); Assert.Equal(0x12345678, b.GetInt(0));
            Assert.Equal(1, callerArena.NumNormalAllocations);
            b.Release(); a.ReleaseCurrentThreadBinding();
        });
        Assert.Equal(0, callerArena.NumActiveAllocations);
        Assert.Equal(1, oldArena.NumActiveAllocations); Assert.Equal(0, oldArena.NumPinnedBytes);
        await owner.InvokeAsync(() => a.ReleaseCurrentThreadBinding());
        Assert.Equal(0, oldArena.NumActiveAllocations);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TerminatedThreadCleanupDrainsCachedHandlesWithoutReleasingLiveBuffer(bool direct)
    {
        using var a = NewAllocator(); PooledByteBuf live = null;
        using (var worker = new Worker())
            await worker.InvokeAsync(() => { live = Allocate(a, direct, 48); Allocate(a, direct, 64).Release(); });
        var m = Arena(a, direct);
        Assert.Equal(2, m.NumActiveAllocations); Assert.Equal(1, a.ReleaseTerminatedThreadBindings());
        Assert.Equal(1, m.NumActiveAllocations); live.WriteInt(42); Assert.Equal(42, live.ReadInt());
        live.Release(); Assert.Equal(0, m.NumActiveAllocations);
    }

    [Fact]
    public async Task ConcurrentReturnsTrimAndCloseLeaveNoHandlesOrNativeStorage()
    {
        var native = new NativeMemoryAllocator(); using var a = NewAllocator(size: 16, native: native);
        var buffers = Enumerable.Range(0, 256).Select(i => Allocate(a, i % 2 == 0, i % 3 == 0 ? 32768 : 48)).ToArray();
        using var start = new ManualResetEventSlim();
        var returns = Enumerable.Range(0, 8).Select(w => Task.Run(() =>
        {
            Assert.True(start.Wait(TimeSpan.FromSeconds(10)));
            for (int i = w; i < buffers.Length; i += 8) buffers[i].Release();
        })).ToArray();
        start.Set();
        for (int i = 0; i < 8; ++i) a.TrimCurrentThreadCache();
        a.Dispose();
        await Task.WhenAll(returns).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        foreach (var m in a.Metric.HeapArenas.Concat(a.Metric.DirectArenas))
        {
            Assert.Equal(0, m.NumActiveAllocations); Assert.Equal(0, m.NumActiveChunks);
            Assert.Equal(m.NumAllocations, m.NumDeallocations);
        }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void FailedNativeMissAndResizeLeaveCachedAndLiveReservationsUsable()
    {
        const int chunk = 8192 << 4;
        var native = new NativeMemoryAllocator(chunk); using var a = NewAllocator(native: native);
        var cached = Allocate(a, true, 32768); var cachedAllocation = cached.Allocation; cached.Release();
        var live = Allocate(a, true, 48); var original = live.Allocation; live.WriteInt(42);
        Assert.Throws<OutOfMemoryException>(() => a.DirectBuffer(chunk + 1));
        Assert.Throws<OutOfMemoryException>(() => live.Capacity = chunk + 1);
        Assert.Same(original, live.Allocation); Assert.Equal(48, live.Capacity); Assert.Equal(42, live.GetInt(0));
        var reused = Allocate(a, true, 32768);
        Assert.Same(cachedAllocation.Storage, reused.Allocation.Storage); Assert.Equal(cachedAllocation.Handle, reused.Allocation.Handle);
        Assert.Equal(1, a.Metric.DirectArenas[0].NumNormalAllocations);
        live.Release(); reused.Release(); a.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void HeapAndNativeQueuesOfSameSizeRemainIndependent()
    {
        using var a = NewAllocator();
        var heap = Allocate(a, false, 48); var direct = Allocate(a, true, 48);
        var h = heap.Allocation; var d = direct.Allocation; heap.Release(); direct.Release();
        heap = Allocate(a, false, 48); direct = Allocate(a, true, 48);
        Assert.Same(h.Storage, heap.Allocation.Storage); Assert.Same(d.Storage, direct.Allocation.Storage);
        Assert.NotSame(heap.Allocation.Storage, direct.Allocation.Storage);
        heap.Release(); direct.Release();
    }

    [Fact]
    public void MaximumConfiguredCacheSizeIsSaturatedAndQueueStorageRemainsLazy()
    {
        using var a = NewAllocator(size: int.MaxValue);
        Assert.Equal(int.MaxValue, a.Metric.SmallCacheSize);
        var x = Allocate(a, false, 48); var y = Allocate(a, false, 48); x.Release(); y.Release();
        Assert.True(a.TrimCurrentThreadCache()); Assert.Equal(0, a.Metric.HeapArenas[0].NumActiveAllocations);
    }

    [Fact]
    public void ZeroSizedCacheBindingCanBeTrimmedAndDoesNotRetainRegions()
    {
        using var a = NewAllocator(size: 0);
        Assert.False(a.TrimCurrentThreadCache()); Allocate(a, false, 48).Release();
        Assert.True(a.TrimCurrentThreadCache()); Assert.Equal(1, a.Metric.NumThreadLocalCaches);
        Assert.Equal(0, a.Metric.HeapArenas[0].NumActiveAllocations);
    }

    [Fact]
    public void InvalidCacheConfigurationFailsBeforeNativeReservation()
    {
        var native = new NativeMemoryAllocator();
        Assert.Throws<ArgumentOutOfRangeException>(() => new PooledByteBufAllocator(nativeAllocator: native, smallCacheSize: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PooledByteBufAllocator(nativeAllocator: native, normalCacheSize: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PooledByteBufAllocator(nativeAllocator: native, maxCachedBufferCapacity: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PooledByteBufAllocator(nativeAllocator: native, cacheTrimInterval: 0));
        Assert.Equal(0, native.ReservedBytes);
    }

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
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add((action, done));
            return done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        public void Dispose()
        {
            _queue.CompleteAdding(); Assert.True(_thread.Join(TimeSpan.FromSeconds(10))); _queue.Dispose();
        }
    }
}
