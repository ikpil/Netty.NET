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
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Reflection;
using System.Threading;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using static Netty.NET.Buffer.Tests.PooledBufferEventTest;
using Record = Netty.NET.Buffer.Tests.PooledBufferEventTest.Record;

namespace Netty.NET.Buffer.Tests;

[Collection(nameof(PooledBufferEventCollection))]
public class AdaptiveAllocatorEventTest
{
    private static AdaptiveByteBufAllocator NewAllocator(NativeMemoryAllocator native, bool low = false)
        => new(false, low, native, 2, 128, 3, 2, true);
    private static ByteBuf Allocate(AdaptiveByteBufAllocator allocator, bool direct, int size, int max = 2097152)
        => direct ? allocator.DirectBuffer(size, max) : allocator.HeapBuffer(size, max);
    private static Listener Listen(EventKeywords keywords = (EventKeywords)31) => new(keywords);
    private static void OnThread(bool fast, Action action)
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try { if (fast) FastThreadLocalThread.RunWithFastThreadLocal(action); else action(); }
            catch (Exception error) { failure = error; }
        });
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(15))); Assert.Null(failure);
    }
    private static void Check(Record record, int id, bool direct)
    {
        Assert.Equal(id, record.Id); Assert.Equal(typeof(AdaptiveByteBufAllocator).FullName, record.Fields["allocatorType"]);
        Assert.Equal(direct, record.Flag("direct")); Assert.True(direct ? record.Number("address") != 0 : record.Number("address") == 0);
    }

    [Theory]
    [InlineData(false, false, false)] [InlineData(true, false, false)] [InlineData(false, true, false)] [InlineData(true, true, false)]
    [InlineData(false, false, true)] [InlineData(true, false, true)] [InlineData(false, true, true)] [InlineData(true, true, true)]
    public void OriginalSharedLocalAndLowMemoryAllocationMetadataSurvivesFinalRelease(bool direct, bool fast, bool low)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native, low);
        using var listener = Listen();
        OnThread(fast, () =>
        {
            ByteBuf buffer = Allocate(allocator, direct, 128, 200); buffer.SetIndex(7, 99);
            Record chunk = listener.Events[0], allocation = listener.Events[1];
            Check(chunk, 4, direct); Assert.Equal(131072, chunk.Number("capacity")); Assert.True(chunk.Flag("pooled"));
            Assert.Equal(fast && !low, chunk.Flag("threadLocal")); Check(allocation, 1, direct);
            Assert.Equal(fast && !low, allocation.Flag("chunkThreadLocal")); Assert.True(allocation.Flag("chunkPooled"));
            Assert.Equal(128, allocation.Number("size")); Assert.Equal(128, allocation.Number("maxFastCapacity")); Assert.Equal(200, allocation.Number("maxCapacity"));
            buffer.Retain(); Assert.False(buffer.Release()); Assert.Equal(2, listener.Events.Length); buffer.Release();
            Check(listener.Events[2], 2, direct); Assert.True(allocation.Number("address") == listener.Events[2].Number("address"));
        });
        allocator.Dispose(); Assert.Equal(new[] { 4, 1, 2, 5 }, listener.Events.Select(e => e.Id));
        Check(listener.Events[^1], 5, direct); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void OriginalFixedClassTwoChunkAndTwoRemainingSegmentReuseScenarios(bool direct, bool twoChunks)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native); using var listener = Listen((EventKeywords)24);
        int size = 16896;
        int minSegmentsPerChunk = 32; // See AdaptivePoolingAllocator.SizeClassChunkController.
        var buffers = new List<ByteBuf>();
        for (int i = 0; i < (twoChunks ? minSegmentsPerChunk + 1 : minSegmentsPerChunk - 2); i++)
            buffers.Add(Allocate(allocator, direct, size, size));
        if (!twoChunks)
        {
            // we still have 2 available segments in the chunk, so we should not allocate a new one
            for (int i = 0; i < 128; i++) Allocate(allocator, direct, size, size).Release();
        }
        Assert.Equal(twoChunks ? 2 : 1, listener.Events.Count(e => e.Id == 4));
        // release all buffers
        foreach (ByteBuf buffer in buffers) buffer.Release();
        allocator.Dispose(); Assert.Equal(twoChunks ? 2 : 1, listener.Events.Count(e => e.Id == 5)); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalPrimedLocalMagazineReportsItsReusedBufferAllocation(bool direct)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native);
        OnThread(true, () =>
        {
            // Prime the cache.
            Allocate(allocator, direct, 128).Release();
            using var listener = Listen((EventKeywords)7);
            // Allocate out of the cache.
            Allocate(allocator, direct, 128).Release();
            Assert.Equal(new[] { 1, 2 }, listener.Events.Select(e => e.Id));
            Assert.True(listener.Events[0].Flag("chunkThreadLocal")); Assert.True(listener.Events[0].Flag("chunkPooled"));
            Assert.Equal(128, listener.Events[0].Number("maxFastCapacity"));
        });
        allocator.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LastLocalSlotKeepsTheMagazineFlagCapturedBeforeDetachment(bool direct)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native); using var listener = Listen();
        OnThread(true, () =>
        {
            var buffers = new List<ByteBuf>();
            for (int i = 0; i < 128; i++) buffers.Add(Allocate(allocator, direct, 1024));
            Assert.All(listener.Events.Where(e => e.Id == 1), e => Assert.True(e.Flag("chunkThreadLocal")));
            Assert.Equal(128, listener.Events.Count(e => e.Id == 1)); Assert.Single(listener.Events, e => e.Id == 4);
            Assert.False(AdaptiveByteBufAllocatorTest.Root(buffers[^1]).Allocation.CurrentThreadLocalAssociation);
            // release all buffers
            foreach (ByteBuf buffer in buffers) buffer.Release();
        });
        allocator.Dispose(); Assert.Single(listener.Events, e => e.Id == 5); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void ResizePreservesLocalAssociationForDedicatedFallbackAndSubsequentFallback(bool direct, bool fast)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native); using var listener = Listen();
        OnThread(fast, () =>
        {
            ByteBuf buffer = Allocate(allocator, direct, 7); buffer.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7 }).SetIndex(2, 7);
            buffer.Capacity = 64; int count = listener.Events.Length; buffer.Capacity = 16; Assert.Equal(count, listener.Events.Length);
            buffer.Capacity = 1048577; buffer.Capacity = 1048578;
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7 }, ByteBufUtil.GetBytes(buffer, 0, 7)); Assert.Equal(2, buffer.ReaderIndex); Assert.Equal(7, buffer.WriterIndex);
            Record[] allocated = listener.Events.Where(e => e.Id == 1).ToArray(); Assert.Equal(4, allocated.Length);
            Assert.All(allocated, e => Assert.Equal(fast, e.Flag("chunkThreadLocal")));
            Assert.Equal(new[] { true, true, false, false }, allocated.Select(e => e.Flag("chunkPooled")));
            Assert.Equal(new long[] { 64, 1048577, 1048578 }, listener.Events.Where(e => e.Id == 3).Select(e => e.Number("newCapacity")));
            Assert.All(listener.Events.Where(e => e.Id == 4), e => Assert.Equal(fast, e.Flag("threadLocal")));
            buffer.Release();
        });
        allocator.Dispose(); Assert.Equal(4, listener.Events.Count(e => e.Id == 4)); Assert.Equal(4, listener.Events.Count(e => e.Id == 5)); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void BuddyAndLowMemoryDedicatedPathsReportTheirActualChunkAndFastCapacity(bool direct, bool low)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native, low); using var listener = Listen();
        ByteBuf buffer = Allocate(allocator, direct, 20000);
        Check(listener.Events[0], 4, direct); Assert.Equal(!low, listener.Events[0].Flag("pooled"));
        Assert.Equal(low ? 20000 : 262144, listener.Events[0].Number("capacity")); Assert.Equal(low ? 20000 : 32768, listener.Events[1].Number("maxFastCapacity"));
        buffer.Release(); allocator.Dispose(); Assert.Equal(new[] { 4, 1, 2, 5 }, listener.Events.Select(e => e.Id)); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void FailedReservationEmitsOnlyResizeAttemptAndRetainsTheExistingBuffer()
    {
        var native = new NativeMemoryAllocator(131072); using var allocator = NewAllocator(native); using var listener = Listen();
        ByteBuf buffer = Allocate(allocator, true, 7).WriteInt(0x12345678);
        Assert.Throws<OutOfMemoryException>(() => buffer.Capacity = 64); Assert.Throws<OutOfMemoryException>(() => Allocate(allocator, true, 64));
        Assert.Equal(new[] { 4, 1, 3 }, listener.Events.Select(e => e.Id));
        Assert.Equal(7, buffer.Capacity); Assert.Equal(4, buffer.WriterIndex); Assert.Equal(0x12345678, buffer.GetInt(0));
        buffer.Capacity = 8; buffer.Release(); allocator.Dispose(); Assert.Equal(new[] { 4, 1, 3, 2, 5 }, listener.Events.Select(e => e.Id)); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void OperationAndPinClaimsDelayChunkFreeBeyondLogicalReleaseAndAllocatorClose(bool direct, bool pin)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native); using var listener = Listen();
        ByteBuf buffer = Allocate(allocator, direct, 128);
        IDisposable lease = pin ? buffer.AsReadOnlyMemory(0, 128).Pin() : AdaptiveByteBufAllocatorTest.Root(buffer).AcquireReadLease();
        allocator.Dispose(); buffer.Release(); Assert.Equal(new[] { 4, 1, 2 }, listener.Events.Select(e => e.Id));
        lease.Dispose(); Assert.Equal(new[] { 4, 1, 2, 5 }, listener.Events.Select(e => e.Id)); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(1, 1, 2)] [InlineData(2, 2, 1)] [InlineData(4, 3, 1)] [InlineData(8, 4, 2)] [InlineData(16, 5, 2)]
    public void IndependentKeywordsKeepTheExistingEventSchema(long keyword, int id, int count)
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native); using var listener = Listen((EventKeywords)keyword);
        ByteBuf buffer = Allocate(allocator, true, 128); buffer.Capacity = 256; buffer.Release(); allocator.Dispose();
        Assert.Equal(count, listener.Events.Length); Assert.All(listener.Events, e => Check(e, id, true)); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void LateEnablePublishesAfterLifetimeAndMagazineLocksAreReleased()
    {
        Assert.False(BufferAllocatorEventSource.Log.IsEnabled()); var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native);
        var gate = (ReaderWriterLockSlim)typeof(AdaptivePoolingAllocator).GetField("_lifetime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(allocator.DirectPool);
        object dispose = typeof(AdaptiveByteBufAllocator).GetField("_disposeLock", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(allocator);
        bool held = false; Exception failure = null; Listener listener = null;
        var worker = new Thread(() => { try { Allocate(allocator, true, 128).Release(); } catch (Exception e) { failure = e; } });
        gate.EnterWriteLock();
        try
        {
            worker.Start(); Assert.True(SpinWait.SpinUntil(() => gate.WaitingReadCount > 0, TimeSpan.FromSeconds(5)));
            listener = Listen(); listener.Callback = _ => held |= gate.IsReadLockHeld || gate.IsWriteLockHeld || Monitor.IsEntered(dispose)
                || allocator.DirectPool.Group(2).Magazines.Any(m => Monitor.IsEntered(m.AllocationLock));
        }
        finally { gate.ExitWriteLock(); }
        worker.Join();
        using (listener) { Assert.Null(failure); allocator.Dispose(); Assert.False(held); Assert.Equal(new[] { 4, 1, 2, 5 }, listener.Events.Select(e => e.Id)); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void ReentrantAndThrowingObserversDoNotHoldAdaptiveStorageOrAmbientBatches()
    {
        var native = new NativeMemoryAllocator(); using var allocator = NewAllocator(native); using var listener = Listen(); bool entered = false;
        listener.Callback = e => { if (e.Id == 4 && !entered) { entered = true; Allocate(allocator, true, 7).Release(); } };
        Allocate(allocator, true, 128).Release(); Assert.True(entered);
        listener.Callback = _ => throw new InvalidOperationException("observer failure");
        Allocate(allocator, true, 256).Release(); allocator.Dispose();
        Assert.Equal(3, listener.Events.Count(e => e.Id == 4)); Assert.Equal(3, listener.Events.Count(e => e.Id == 5)); Assert.Equal(0, native.ReservedBytes);
    }
}
