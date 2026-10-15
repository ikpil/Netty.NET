/*
 * Copyright 2026 The Netty Project
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
using System.Diagnostics.Tracing;
using System.Linq;
using System.Reflection;
using System.Threading;
using Netty.NET.Common;
using static Netty.NET.Buffer.Tests.PooledBufferEventTest;
using Record = Netty.NET.Buffer.Tests.PooledBufferEventTest.Record;

namespace Netty.NET.Buffer.Tests;

[Collection(nameof(PooledBufferEventCollection))]
public class PooledChunkEventTest
{
    private static PooledByteBufAllocator NewAllocator(NativeMemoryAllocator native, int order = 1, int cache = 0, int alignment = 0)
        => new(maxOrder: order, nativeAllocator: native, smallCacheSize: cache, normalCacheSize: cache, directMemoryCacheAlignment: alignment);
    private static ByteBuf Allocate(PooledByteBufAllocator allocator, bool direct, int size)
        => direct ? allocator.DirectBuffer(size, 40000) : allocator.HeapBuffer(size, 40000);
    private static Listener Listen(EventKeywords keywords = (EventKeywords)31) => new(keywords);
    private static void Check(Record record, int id, int capacity, bool direct, bool pooled)
    {
        Assert.Equal(id, record.Id); Assert.Equal(typeof(PooledByteBufAllocator).FullName, record.Fields["allocatorType"]);
        Assert.Equal(capacity, record.Number("capacity")); Assert.Equal(direct, record.Flag("direct"));
        Assert.Equal(pooled, record.Flag("pooled"));
        Assert.True(direct ? record.Number("address") != 0 : record.Number("address") == 0);
        if (id == 4) Assert.False(record.Flag("threadLocal"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FullChunkRetiresAfterItsLastRegionWithOriginalCombinedEventOrder(bool direct)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native, order: 4);
        using var listener = Listen(); ByteBuf[] buffers = Enumerable.Range(0, 4).Select(_ => Allocate(allocator, direct, 32768)).ToArray();
        Assert.Equal(new[] { 4, 1, 1, 1, 1 }, listener.Events.Select(e => e.Id)); Check(listener.Events[0], 4, 131072, direct, true);
        // release all buffers
        foreach (ByteBuf buffer in buffers) buffer.Release();
        Assert.Equal(new[] { 4, 1, 1, 1, 1, 2, 2, 2, 2, 5 }, listener.Events.Select(e => e.Id));
        Check(listener.Events[^1], 5, 131072, direct, true); allocator.Dispose(); Assert.Equal(10, listener.Events.Length);
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalTwoRemainingSegmentsReuseScenarioKeepsOneRegisteredChunk(bool direct)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native, order: 4);
        using var listener = Listen((EventKeywords)24); const int size = 16896;
        ByteBuf initial = Allocate(allocator, direct, size);
        long pinned = direct ? allocator.PinnedDirectMemory : allocator.PinnedHeapMemory;
        initial.Release(); int buffersPerChunk = (int)(131072 / pinned);
        var buffers = Enumerable.Range(0, buffersPerChunk - 2).Select(_ => Allocate(allocator, direct, size)).ToArray();
        // we still have 2 available segments in the chunk, so we should not allocate a new one
        for (int i = 0; i < 128; i++) Allocate(allocator, direct, size).Release();
        Assert.Single(listener.Events, e => e.Id == 4);
        // release all buffers
        foreach (ByteBuf buffer in buffers) buffer.Release();
        allocator.Dispose(); Assert.Single(listener.Events, e => e.Id == 5); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalTwoChunkAndReuseScenariosExposeLogicalChunkLifetime(bool direct)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native, order: 4);
        using var listener = Listen(); ByteBuf[] buffers = Enumerable.Range(0, 5).Select(_ => Allocate(allocator, direct, 32768)).ToArray();
        Assert.Equal(2, listener.Events.Count(e => e.Id == 4));
        foreach (ByteBuf buffer in buffers) buffer.Release();
        Assert.Equal(2, listener.Events.Count(e => e.Id == 5));
        allocator.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalRepeatedSmallAllocationsReuseOneChunkUntilShutdown(bool direct)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native);
        using var listener = Listen((EventKeywords)24);
        for (int i = 0; i < 128; i++) Allocate(allocator, direct, 128).Release();
        Check(Assert.Single(listener.Events), 4, 16384, direct, true);
        allocator.Dispose(); Check(listener.Events[1], 5, 16384, direct, true); Assert.Equal(2, listener.Events.Length);
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RetiredHeapChunkReuseStillEmitsAnotherLogicalAllocation(bool direct)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native, order: 4);
        using var listener = Listen((EventKeywords)24);
        ByteBuf first = Allocate(allocator, direct, 32768);
        PoolChunk oldChunk = ((PooledByteBuf)AllocatorLeakDetectionTest.UnwrapLeak(first)).Allocation.Chunk; first.Release();
        ByteBuf second = Allocate(allocator, direct, 32768);
        PoolChunk newChunk = ((PooledByteBuf)AllocatorLeakDetectionTest.UnwrapLeak(second)).Allocation.Chunk; second.Release();
        if (direct) Assert.NotSame(oldChunk, newChunk); else Assert.Same(oldChunk, newChunk);
        Assert.Equal(new[] { 4, 5, 4, 5 }, listener.Events.Select(e => e.Id));
        allocator.Dispose(); Assert.Equal(4, listener.Events.Length); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false, 0)] [InlineData(true, 0)] [InlineData(true, 64)]
    public unsafe void HugeChunkReportsUsableNormalizedStorageAndCurrentAlignedAddress(bool direct, int alignment)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native, alignment: alignment);
        using var listener = Listen(); ByteBuf buffer = Allocate(allocator, direct, 20000);
        int capacity = alignment == 0 ? 20000 : 20480;
        Check(listener.Events[0], 4, capacity, direct, false);
        using (var pin = buffer.AsReadOnlyMemory(0, buffer.Capacity).Pin())
            Assert.True(direct ? listener.Events[0].Number("address") == unchecked((long)(nint)pin.Pointer) : listener.Events[0].Number("address") == 0);
        if (alignment != 0) Assert.True(listener.Events[0].Number("address") % alignment == 0);
        buffer.Release(); Assert.Equal(new[] { 4, 1, 2, 5 }, listener.Events.Select(e => e.Id));
        Check(listener.Events[3], 5, capacity, direct, false);
        Assert.True(listener.Events[0].Number("address") == listener.Events[3].Number("address"));
        allocator.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false, 0)] [InlineData(true, 0)] [InlineData(false, 1)] [InlineData(true, 1)] [InlineData(false, 2)] [InlineData(true, 2)]
    [InlineData(false, 3)] [InlineData(true, 3)]
    public void CacheTrimBindingRemovalAndAllocatorShutdownDrainChunkEventsAfterAllLocks(bool direct, int operation)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native, order: 4, cache: 4);
        using var listener = Listen(); ByteBuf buffer = Allocate(allocator, direct, 32768);
        var allocation = ((PooledByteBuf)AllocatorLeakDetectionTest.UnwrapLeak(buffer)).Allocation;
        object allocatorLock = typeof(PooledByteBufAllocator).GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(allocator);
        object cacheLock = typeof(PoolThreadCache).GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(allocation.Cache);
        bool held = false;
        listener.Callback = _ => held |= Monitor.IsEntered(allocatorLock) || Monitor.IsEntered(cacheLock) || Monitor.IsEntered(allocation.Arena.SyncRoot);
        buffer.Release(); Assert.Equal(new[] { 4, 1, 2 }, listener.Events.Select(e => e.Id));
        if (operation == 0) Assert.True(allocator.TrimCurrentThreadCache());
        else if (operation == 1) Assert.True(allocator.ReleaseCurrentThreadBinding());
        else if (operation == 2) allocator.Dispose();
        // The scheduled trimming callback targets this cache method directly.
        else allocation.Cache.Trim();
        Assert.False(held); Assert.Equal(new[] { 4, 1, 2, 5 }, listener.Events.Select(e => e.Id));
        allocator.Dispose(); Assert.Equal(4, listener.Events.Length); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LastOperationLeaseDefersChunkRetirementPastLogicalReleaseAndShutdown(bool direct)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native);
        using var listener = Listen(); ByteBuf buffer = Allocate(allocator, direct, 128);
        var lease = AllocatorLeakDetectionTest.UnwrapLeak(buffer).AcquireReadLease();
        buffer.Release(); allocator.Dispose(); Assert.Equal(new[] { 4, 1, 2 }, listener.Events.Select(e => e.Id));
        lease.Dispose(); Assert.Equal(new[] { 4, 1, 2, 5 }, listener.Events.Select(e => e.Id)); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void NativePinCanOutliveTheLogicalChunkFreeWithoutDuplicateEvents()
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native);
        using var listener = Listen((EventKeywords)24); ByteBuf buffer = Allocate(allocator, true, 8192);
        var pin = buffer.AsReadOnlyMemory(0, 8192).Pin();
        buffer.Release(); allocator.Dispose(); Assert.Equal(new[] { 4, 5 }, listener.Events.Select(e => e.Id));
        Assert.True(native.ReservedBytes > 0); pin.Dispose(); Assert.Equal(0, native.ReservedBytes); Assert.Equal(2, listener.Events.Length);
    }

    [Fact]
    public void FailedReservationHasNoChunkEventAndFailedResizeFlushesOnlyItsAttempt()
    {
        var native = new NativeMemoryAllocator(16384); using var allocator = NewAllocator(native);
        using var listener = Listen(); ByteBuf buffer = Allocate(allocator, true, 16).WriteInt(0x12345678);
        Assert.Throws<OutOfMemoryException>(() => buffer.Capacity = 20000);
        Assert.Throws<OutOfMemoryException>(() => Allocate(allocator, true, 20000));
        Assert.Equal(new[] { 4, 1, 3 }, listener.Events.Select(e => e.Id));
        Assert.Equal(16, buffer.Capacity); Assert.Equal(0x12345678, buffer.GetInt(0));
        buffer.Release(); allocator.Dispose(); Assert.Equal(new[] { 4, 1, 3, 2, 5 }, listener.Events.Select(e => e.Id));
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(8)] [InlineData(16)]
    public void ChunkKeywordsAreIndependentOfEachOtherAndOfBufferEvents(long keyword)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native);
        using var listener = Listen((EventKeywords)keyword); Allocate(allocator, true, 20000).Release();
        Check(Assert.Single(listener.Events), keyword == 8 ? 4 : 5, 20000, true, false);
        allocator.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void CrossThreadCachedReturnDefersRetirementUntilTheOwningBindingIsDrained()
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native, order: 4, cache: 4);
        using var listener = Listen(); ByteBuf buffer = Allocate(allocator, true, 32768);
        // Keep the owning physical thread's binding while another thread returns the region.
        var worker = new Thread(() => buffer.Release()); worker.Start(); worker.Join();
        Assert.Equal(new[] { 4, 1, 2 }, listener.Events.Select(e => e.Id));
        allocator.ReleaseCurrentThreadBinding(); Assert.Equal(new[] { 4, 1, 2, 5 }, listener.Events.Select(e => e.Id));
        allocator.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void ReentrantListenerGetsAnIndependentBatchAfterTheOuterMutationHasFinished()
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native);
        using var listener = Listen(); bool nested = false;
        listener.Callback = e =>
        {
            if (e.Id != 4 || nested) return;
            nested = true; Allocate(allocator, true, 64).Release();
        };
        ByteBuf buffer = Allocate(allocator, true, 128); buffer.Release(); allocator.Dispose();
        Assert.True(nested); Assert.Equal(new[] { 4, 1, 2, 1, 2, 5 }, listener.Events.Select(e => e.Id)); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void TracingEnabledWhileAnAllocationWaitsForTheArenaStillPublishesOutsideLocks()
    {
        Assert.False(BufferAllocatorEventSource.Log.IsEnabled());
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native);
        var arena = (PoolArena)allocator.Metric.DirectArenas[0];
        object allocatorLock = typeof(PooledByteBufAllocator).GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(allocator);
        bool held = false; Exception failure = null;
        var worker = new Thread(() =>
        {
            try { Allocate(allocator, true, 128).Release(); }
            catch (Exception error) { failure = error; }
        });
        Listener listener = null;
        Monitor.Enter(arena.SyncRoot);
        try
        {
            worker.Start();
            Assert.True(SpinWait.SpinUntil(() =>
            {
                if (!Monitor.TryEnter(allocatorLock)) return true;
                Monitor.Exit(allocatorLock); return false;
            }, TimeSpan.FromSeconds(5)));
            listener = Listen();
            listener.Callback = _ => held |= Monitor.IsEntered(allocatorLock) || Monitor.IsEntered(arena.SyncRoot);
        }
        finally { Monitor.Exit(arena.SyncRoot); }
        worker.Join();
        using (listener)
        {
            Assert.Null(failure); allocator.Dispose(); Assert.False(held);
            Assert.Equal(new[] { 4, 1, 2, 5 }, listener.Events.Select(e => e.Id)); Assert.Equal(0, native.ReservedBytes);
        }
    }

    [Fact]
    public void ThrowingChunkObserverDoesNotKeepAnAmbientBatchOrNativeReservationAlive()
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native);
        using var listener = Listen((EventKeywords)24); listener.Callback = _ => throw new InvalidOperationException("observer failure");
        for (int i = 0; i < 2; i++) Allocate(allocator, true, 20000).Release();
        Assert.Equal(new[] { 4, 5, 4, 5 }, listener.Events.Where(e => e.Id > 0).Select(e => e.Id));
        allocator.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }
}
