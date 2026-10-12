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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class PooledByteBufAllocatorTest
{
    private static PooledByteBufAllocator NewAllocator(bool direct = false, int alignment = 0,
        NativeMemoryAllocator native = null) => new(direct, pageSize: 8192, maxOrder: 4,
            directMemoryCacheAlignment: alignment, nativeAllocator: native);
    private static IPoolArenaMetric Arena(PooledByteBufAllocator a, bool direct)
        => (direct ? a.Metric.DirectArenas : a.Metric.HeapArenas).Single();
    private static long Pinned(PooledByteBufAllocator a, bool direct)
        => direct ? a.PinnedDirectMemory : a.PinnedHeapMemory;

    [Theory]
    [InlineData(false, false, false)] [InlineData(false, false, true)]
    [InlineData(false, true, false)] [InlineData(false, true, true)]
    [InlineData(true, false, false)] [InlineData(true, false, true)]
    [InlineData(true, true, false)] [InlineData(true, true, true)]
    public void PublicFactoriesPreserveStoragePolicyProvenanceAndDisabledPoolFallback(bool preferDirect, bool poolHeap, bool poolDirect)
    {
        var native = new NativeMemoryAllocator();
        using var a = new PooledByteBufAllocator(preferDirect, poolHeap, poolDirect, maxOrder: 4, nativeAllocator: native);
        Assert.Same(a.Metric, ((IByteBufAllocatorMetricProvider)a).Metric);
        Assert.Equal(poolDirect, a.IsDirectBufferPooled);
        Assert.Equal(poolHeap ? 1 : 0, a.Metric.NumHeapArenas);
        Assert.Equal(poolDirect ? 1 : 0, a.Metric.NumDirectArenas);
        var factories = new Func<ByteBuf>[] { () => a.Buffer(), () => a.HeapBuffer(), () => a.DirectBuffer(), () => a.IoBuffer() };
        for (int i = 0; i < factories.Length; ++i)
        {
            ByteBuf b = factories[i]();
            bool direct = i == 0 ? preferDirect : i >= 2;
            try
            {
                Assert.Equal(direct, b.IsDirect); Assert.Same(a, b.Allocator);
                Assert.Equal(256, b.Capacity); Assert.Equal(int.MaxValue, b.MaxCapacity);
                Assert.Equal(0, b.ReaderIndex); Assert.Equal(0, b.WriterIndex);
                Assert.Equal(1, b.ReferenceCount); Assert.True(b.IsContiguous);
                Assert.Equal(direct ? poolDirect : poolHeap, b is PooledByteBuf);
                b.WriteLong(0x0102030405060708); Assert.Equal(0x0102030405060708, b.ReadLong());
            }
            finally { b.Release(); }
        }
        Assert.Equal(poolHeap ? 131072 : -1, a.Metric.UsedHeapMemory);
        Assert.Equal(poolDirect ? 131072 : -1, a.Metric.UsedDirectMemory);
        Assert.Equal(poolHeap ? 0 : -1, a.PinnedHeapMemory);
        Assert.Equal(poolDirect ? 0 : -1, a.PinnedDirectMemory);
        a.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(0)] [InlineData(64)]
    public void OriginalArenaMetricsWithoutCacheCountEveryReservationAndReturn(int alignment)
    {
        using var a = NewAllocator(alignment: alignment);
        for (int i = 0; i < 100; ++i) { Assert.True(a.HeapBuffer().Release()); Assert.True(a.DirectBuffer().Release()); }
        foreach (var arena in a.Metric.HeapArenas.Concat(a.Metric.DirectArenas))
        {
            Assert.Equal(100, arena.NumAllocations); Assert.Equal(100, arena.NumDeallocations);
            Assert.Equal(0, arena.NumActiveAllocations);
            Assert.Equal(100, arena.NumSmallAllocations); Assert.Equal(100, arena.NumSmallDeallocations);
            Assert.Equal(0, arena.NumNormalAllocations); Assert.Equal(0, arena.NumHugeAllocations);
            Assert.Equal(1, arena.NumChunkAllocations); Assert.Equal(0, arena.NumChunkDeallocations);
            Assert.Equal(1, arena.NumActiveChunks); Assert.Equal(a.Metric.ChunkSize, arena.NumActiveBytes);
            Assert.Equal(1, arena.NumThreadCaches);
        }
        Assert.Equal(0, a.Metric.SmallCacheSize); Assert.Equal(0, a.Metric.NormalCacheSize);
        Assert.Equal(1, a.Metric.NumThreadLocalCaches);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalSmallSubpageAndChunkListMetricsArePublicReadonlySnapshots(bool direct)
    {
        using var a = NewAllocator(direct);
        IPoolArenaMetric arena = Arena(a, direct);
        Assert.Equal(6, arena.NumChunkLists);
        Assert.Equal(new[] { 1, 1, 25, 50, 75, 100 }, arena.ChunkLists.Select(l => l.MinUsage));
        Assert.Equal(new[] { 25, 50, 75, 100, 100, 100 }, arena.ChunkLists.Select(l => l.MaxUsage));
        Assert.Empty(arena.SmallSubpages);
        ByteBuf b = a.Buffer(500);
        var snapshot = arena.SmallSubpages;
        try
        {
            IPoolSubpageMetric subpage = Assert.Single(snapshot);
            Assert.Equal(1, subpage.MaxNumElements - subpage.NumAvailable);
            Assert.Equal(512, subpage.ElementSize);
            Assert.Throws<NotSupportedException>(() => ((IList<IPoolSubpageMetric>)snapshot).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<IPoolArenaMetric>)a.Metric.HeapArenas).Clear());
            Assert.Equal(arena.SizeIdx2Size(arena.Size2SizeIdx(500)), arena.NormalizeSize(500));
            Assert.Equal(arena.SizeIdx2Size(0), arena.SizeIdx2SizeCompute(0));
            Assert.Equal(arena.PageIdx2Size(3), arena.PageIdx2SizeCompute(3));
            Assert.Equal(arena.Pages2PageIdx(4), arena.Pages2PageIdxFloor(4));
        }
        finally { b.Release(); }
        Assert.Equal(snapshot[0].MaxNumElements, snapshot[0].NumAvailable);
        a.Dispose(); Assert.Empty(arena.SmallSubpages);
#pragma warning disable CS0618
        Assert.Equal(0, arena.NumTinySubpages); Assert.Empty(arena.TinySubpages);
        Assert.Equal(0, arena.NumTinyAllocations); Assert.Equal(0, arena.NumTinyDeallocations);
        Assert.Equal(0, arena.NumActiveTinyAllocations); Assert.Equal(0, a.Metric.TinyCacheSize);
#pragma warning restore CS0618
    }

    [Theory]
    [InlineData(false, 1024)] [InlineData(true, 1024)]
    [InlineData(false, 2048)] [InlineData(true, 2048)]
    [InlineData(false, 4096)] [InlineData(true, 4096)]
    [InlineData(false, 8192)] [InlineData(true, 8192)]
    [InlineData(false, 16384)] [InlineData(true, 16384)]
    [InlineData(false, 32768)] [InlineData(true, 32768)]
    [InlineData(false, 65536)] [InlineData(true, 65536)]
    [InlineData(false, 131072)] [InlineData(true, 131072)]
    public void OriginalUsedAndPinnedMemoryAcrossResizeAndFinalRelease(bool direct, int initial)
    {
        using var a = new PooledByteBufAllocator(direct);
        IPoolArenaMetric arena = Arena(a, direct);
        Assert.Equal(0, arena.NumActiveBytes); Assert.Equal(0, Pinned(a, direct));
        ByteBuf b = a.Buffer(initial, initial * 4);
        try
        {
            Assert.Equal(a.Metric.ChunkSize, arena.NumActiveBytes);
            Assert.InRange(Pinned(a, direct), initial, arena.NumActiveBytes);
            // Double the size of the buffer
            b.Capacity = initial * 2;
            Assert.Equal(a.Metric.ChunkSize, arena.NumActiveBytes);
            Assert.InRange(Pinned(a, direct), initial * 2, arena.NumActiveBytes);
            b.Retain(); Assert.Equal(1, arena.NumActiveAllocations); Assert.False(b.Release());
        }
        finally { b.Release(); }
        // This is the case as allocations will start in qInit and chunks in qInit will never be released until
        // these are moved to q000.
        // See https://www.bsdcan.org/2006/papers/jemalloc.pdf
        Assert.Equal(a.Metric.ChunkSize, arena.NumActiveBytes);
        Assert.Equal(0, Pinned(a, direct)); Assert.Equal(0, arena.NumActiveAllocations);
        Assert.Equal(arena.NumAllocations, arena.NumDeallocations);
        a.Dispose(); Assert.Equal(0, arena.NumActiveBytes); Assert.Equal(0, arena.NumActiveChunks);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalWholeChunkReleaseRetiresStorageAndPreservesCreator(bool direct)
    {
        using var a = NewAllocator(direct);
        IPoolArenaMetric arena = Arena(a, direct);
        ByteBuf b = a.Buffer(a.Metric.ChunkSize);
        Assert.Same(a, b.Allocator);
        // Must end up in the 6th PoolChunkList
        Assert.Single(arena.ChunkLists[5]);
        Assert.Equal(1, arena.NumNormalAllocations); Assert.Equal(1, arena.NumActiveChunks);
        b.Release(); Assert.Same(a, b.Allocator);
        // Should be completely removed and so all PoolChunkLists must be empty
        Assert.All(arena.ChunkLists, list => Assert.Empty(list));
        Assert.Equal(1, arena.NumChunkDeallocations); Assert.Equal(0, arena.NumActiveChunks);
        Assert.Equal(0, arena.NumActiveBytes); Assert.Equal(0, Pinned(a, direct));
        var retry = (PooledByteBuf)a.Buffer(a.Metric.ChunkSize);
        try { Assert.Equal(2, arena.NumChunkAllocations); Assert.Equal(1, arena.NumActiveChunks); }
        finally { retry.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(64)] [InlineData(8192)]
    public void AlignedHugeMetricsCountNormalizedReservationWithoutHeadroom(int alignment)
    {
        var native = new NativeMemoryAllocator();
        using var a = NewAllocator(true, alignment, native);
        IPoolArenaMetric arena = Arena(a, true);
        int requested = a.Metric.ChunkSize + 1;
        ByteBuf b = a.DirectBuffer(requested, requested);
        int reserved = alignment == 0 ? requested : arena.NormalizeSize(requested);
        try
        {
            Assert.Equal(requested, b.Capacity); Assert.Equal(requested, b.MaxCapacity);
            Assert.Equal(reserved, arena.NumActiveBytes); Assert.Equal(reserved, a.PinnedDirectMemory);
            Assert.Equal(reserved + alignment, native.ReservedBytes);
            Assert.Equal(1, arena.NumHugeAllocations); Assert.Equal(1, arena.NumActiveHugeAllocations);
            Assert.Equal(0, arena.NumChunkAllocations); Assert.Equal(0, arena.NumActiveChunks);
        }
        finally { b.Release(); }
        Assert.Equal(1, arena.NumHugeDeallocations); Assert.Equal(0, arena.NumActiveBytes);
        Assert.Equal(0, native.ReservedBytes); Assert.Equal(0, a.PinnedDirectMemory);
    }

    [Theory]
    [InlineData(0)] [InlineData(64)]
    public void FailedNativeGrowthAndRetryDoNotChargeUnpublishedReservations(int alignment)
    {
        const int chunk = 131072;
        var native = new NativeMemoryAllocator(chunk + alignment);
        using var a = NewAllocator(true, alignment, native);
        IPoolArenaMetric arena = Arena(a, true);
        ByteBuf b = a.DirectBuffer(chunk, chunk * 2).WriteInt(42);
        Assert.Throws<OutOfMemoryException>(() => b.Capacity = chunk + 1);
        Assert.Equal(42, b.GetInt(0)); Assert.Equal(1, arena.NumAllocations);
        Assert.Equal(0, arena.NumDeallocations); Assert.Equal(chunk, arena.NumActiveBytes);
        Assert.Equal(chunk, a.PinnedDirectMemory); Assert.Equal(1, arena.NumChunkAllocations);
        b.Release(); Assert.Equal(0, native.ReservedBytes);
        ByteBuf retry = a.DirectBuffer(48);
        try { Assert.Equal(2, arena.NumAllocations); Assert.Equal(2, arena.NumChunkAllocations); }
        finally { retry.Release(); }
    }

    [Theory]
    [InlineData(false, 48)] [InlineData(true, 48)]
    [InlineData(false, 131072)] [InlineData(true, 131072)]
    [InlineData(false, 131073)] [InlineData(true, 131073)]
    public void ShutdownMetricsKeepActiveReservationsUntilFinalInternalLease(bool direct, int size)
    {
        var native = new NativeMemoryAllocator();
        using var a = NewAllocator(direct, native: native);
        IPoolArenaMetric arena = Arena(a, direct);
        ByteBuf b = a.Buffer(size).WriteInt(42);
        long pinned = Pinned(a, direct);
        Memory<byte> memory = b.AsMemory(0, 4);
        using (var lease = b.AcquireReadLease())
        {
            a.Dispose(); a.Dispose();
            Assert.Throws<ObjectDisposedException>(() => a.Buffer(1));
            Assert.Equal(1, arena.NumActiveAllocations); Assert.True(arena.NumActiveBytes >= size);
            Assert.All(arena.ChunkLists, list => Assert.Empty(list));
            b.Release(); Assert.Equal(pinned, Pinned(a, direct));
            Assert.Equal(1, arena.NumActiveAllocations);
            Assert.Equal(new byte[] { 0, 0, 0, 42 }, memory.Span.ToArray());
        }
        Assert.Equal(0, arena.NumActiveBytes); Assert.Equal(0, Pinned(a, direct));
        Assert.Equal(0, arena.NumActiveAllocations); Assert.Equal(0, arena.NumActiveChunks);
        Assert.Equal(arena.NumAllocations, arena.NumDeallocations);
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public unsafe void RawNativePinIsPhysicalMemoryRatherThanLogicalPinnedMetric()
    {
        var native = new NativeMemoryAllocator();
        using var a = NewAllocator(true, native: native);
        ByteBuf b = a.DirectBuffer(131072).WriteInt(42);
        using (var pin = b.AsMemory(0, 4).Pin())
        {
            b.Release(); Assert.Equal(0, a.PinnedDirectMemory); Assert.Equal(0, a.Metric.UsedDirectMemory);
            Assert.Equal(131072, native.ReservedBytes); Assert.Equal(42, ((byte*)pin.Pointer)[3]);
        }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CopiesViewsAndCompositePaddingUsePublicAllocatorAndCountOnlyReservations(bool direct)
    {
        using var a = NewAllocator(direct);
        var arena = Arena(a, direct);
        ByteBuf root = a.Buffer(48).WriteLong(42);
        ByteBuf view = root.RetainedSlice(0, 8), copy = view.Copy();
        try
        {
            Assert.Same(a, copy.Allocator); Assert.Equal(direct, copy.IsDirect);
            Assert.IsAssignableFrom<PooledByteBuf>(copy); Assert.Equal(2, arena.NumActiveAllocations);
            using var compositeLease = root.AcquireReadLease();
            Assert.Equal(2, arena.NumActiveAllocations);
            Assert.False(root.Release()); Assert.True(view.Release());
        }
        finally { copy.Release(); if (root.ReferenceCount != 0) root.Release(root.ReferenceCount); }
        CompositeByteBuf composite = a.CompositeBuffer(1);
        try
        {
            composite.Capacity = 100; composite.WriteLong(99);
            Assert.Equal(direct, composite.IsDirect); Assert.Same(a, composite.Component(0).Allocator);
            Assert.Equal(1, arena.NumActiveAllocations);
        }
        finally { composite.Release(); }
        Assert.Equal(0, arena.NumActiveAllocations); Assert.Equal(0, Pinned(a, direct));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UnpooledFallbackRemainsIndependentlyOwnedAfterFactoryShutdown(bool direct)
    {
        var native = new NativeMemoryAllocator();
        using var a = new PooledByteBufAllocator(direct, false, false, nativeAllocator: native);
        ByteBuf b = a.Buffer(8, 32).WriteInt(42);
        a.Dispose();
        try
        {
            Assert.Throws<ObjectDisposedException>(() => a.Buffer(1));
            b.Capacity = 16; Assert.Equal(42, b.GetInt(0));
            Assert.Equal(-1, a.Metric.UsedHeapMemory); Assert.Equal(-1, a.Metric.UsedDirectMemory);
            Assert.Same(a.HeapBuffer(0, 0), a.DirectBuffer(0, 0));
            CompositeByteBuf empty = a.CompositeBuffer(); Assert.Equal(0, empty.Capacity); empty.Release();
        }
        finally { b.Release(); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(0, 4, 0)] [InlineData(1024, 4, 0)] [InlineData(4097, 4, 0)]
    [InlineData(8192, -1, 0)] [InlineData(8192, 15, 0)] [InlineData(131072, 14, 0)]
    [InlineData(8192, 4, -1)] [InlineData(8192, 4, 3)] [InlineData(int.MaxValue, 4, 64)]
    public void InvalidGeometryAndAlignmentFailBeforeNativeReservation(int pageSize, int maxOrder, int alignment)
    {
        var native = new NativeMemoryAllocator();
        Assert.ThrowsAny<ArgumentException>(() => new PooledByteBufAllocator(pageSize: pageSize, maxOrder: maxOrder,
            directMemoryCacheAlignment: alignment, nativeAllocator: native));
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void OriginalPageAlignmentPromotionAndClrDefaultChoices()
    {
        var native = new NativeMemoryAllocator();
        using var a = new PooledByteBufAllocator(pageSize: 4096, maxOrder: 4, directMemoryCacheAlignment: 8192, nativeAllocator: native);
        Assert.Equal(131072, a.Metric.ChunkSize); Assert.True(PooledByteBufAllocator.IsDirectMemoryCacheAlignmentSupported);
        ByteBuf b = a.Buffer(0, 1); Assert.False(b.IsDirect); b.Release();
        ByteBuf d = PooledByteBufAllocator.Default.Buffer(1); Assert.True(d.IsDirect); d.Release();
        Assert.Same(PooledByteBufAllocator.Default, PooledByteBufAllocator.Default);
        Assert.Same(UnpooledByteBufAllocator.Default, IByteBufAllocator.Default);
        Assert.Equal(0, native.ReservedBytes);
        Assert.Contains("smallCacheSize: 0; normalCacheSize: 0; numThreadLocalCaches: 1; chunkSize: 131072", a.Metric.ToString());
    }

    [Fact]
    public void MaximumGeometryReservesNoBackingBeforeAllocation()
    {
        var native = new NativeMemoryAllocator(0);
        using var a = new PooledByteBufAllocator(pageSize: 65536, maxOrder: 14, nativeAllocator: native);
        Assert.Equal(1 << 30, a.Metric.ChunkSize);
        Assert.Equal(0, a.Metric.UsedHeapMemory); Assert.Equal(0, a.Metric.UsedDirectMemory);
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ConcurrentReservationsKeepExactCountersThroughShutdown(bool direct)
    {
        var native = new NativeMemoryAllocator();
        using var a = NewAllocator(direct, native: native);
        var arena = Arena(a, direct);
        var token = TestContext.Current.CancellationToken;
        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (int i = 0; i < 128; ++i)
            {
                ByteBuf b = a.Buffer(new[] { 48, 32768, 131073 }[i % 3]);
                try { b.WriteInt(worker); Assert.Equal(worker, b.GetInt(0)); }
                finally { b.Release(); }
            }
        }, token)));
        Assert.Equal(1024, arena.NumAllocations); Assert.Equal(1024, arena.NumDeallocations);
        Assert.Equal(0, arena.NumActiveAllocations); Assert.Equal(0, Pinned(a, direct));
        a.Dispose(); Assert.Equal(0, arena.NumActiveBytes); Assert.Equal(0, arena.NumActiveChunks);
        Assert.Equal(arena.NumChunkAllocations, arena.NumChunkDeallocations); Assert.Equal(0, native.ReservedBytes);
    }
}
