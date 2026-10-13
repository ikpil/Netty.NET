/*
 * Copyright 2024 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class AdaptiveByteBufAllocatorTest
{
    private static AdaptiveByteBufAllocator Allocator(bool lowMemory = false, NativeMemoryAllocator native = null,
        int reuseCapacity = 2, long purgePolls = 128, int threshold = 3, int stripes = 4)
        => new(false, lowMemory, native, reuseCapacity, purgePolls, threshold, stripes);
    internal static AdaptiveByteBuf Root(ByteBuf buffer)
    {
        while (buffer is not AdaptiveByteBuf) buffer = buffer.Unwrap();
        return (AdaptiveByteBuf)buffer;
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void PublicFactoriesPreservePreferenceAllocatorAndMetrics(bool preferDirect, bool lowMemory)
    {
        var native = new NativeMemoryAllocator();
        using var allocator = new AdaptiveByteBufAllocator(preferDirect, lowMemory, native);
        ByteBuf normal = allocator.Buffer(7, 100), heap = allocator.HeapBuffer(7, 100), direct = allocator.IoBuffer(7, 100);
        try
        {
            Assert.Equal(preferDirect, normal.IsDirect); Assert.False(heap.IsDirect); Assert.True(direct.IsDirect);
            Assert.Same(allocator, normal.Allocator); Assert.Same(allocator, allocator.Metric);
            Assert.Equal(131072, allocator.UsedHeapMemory); Assert.Equal(131072, allocator.UsedDirectMemory);
            Assert.Equal(131072, native.ReservedBytes); Assert.True(allocator.IsDirectBufferPooled);
            Assert.Null(Root(normal).Unwrap()); Assert.Equal(7, normal.Capacity);
        }
        finally { normal.Release(); heap.Release(); direct.Release(); }
        allocator.Dispose(); Assert.Equal(0, allocator.UsedHeapMemory + allocator.UsedDirectMemory); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TestUsedMemoryFollowsSizeClassChangesAndRetainedChunks(bool direct)
    {
        using var allocator = Allocator();
        ByteBuf buffer = direct ? allocator.DirectBuffer(1024, 4096) : allocator.HeapBuffer(1024, 4096);
        try
        {
            Assert.Equal(131072, direct ? allocator.UsedDirectMemory : allocator.UsedHeapMemory);
            // Double the size of the buffer
            buffer.Capacity = 2048;
            // This is a new size class, and a new magazine with a new chunk
            Assert.Equal(262144, direct ? allocator.UsedDirectMemory : allocator.UsedHeapMemory);
        }
        finally { buffer.Release(); }
        // Memory is still held by the magazines
        Assert.Equal(262144, direct ? allocator.UsedDirectMemory : allocator.UsedHeapMemory);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AdaptiveChunkMustDeallocateOrReuseWthBufferRelease(bool direct)
    {
        using var allocator = Allocator(); var live = new List<ByteBuf>();
        ByteBuf NewBuffer() => direct ? allocator.DirectBuffer(256) : allocator.HeapBuffer(256);
        try
        {
            for (int i = 0; i < 513; i++) live.Add(NewBuffer());
            Assert.Equal(262144, direct ? allocator.UsedDirectMemory : allocator.UsedHeapMemory);
            foreach (var b in live) b.Release(); live.Clear();
            for (int i = 0; i < 1024; i++) live.Add(NewBuffer());
            Assert.Equal(262144, direct ? allocator.UsedDirectMemory : allocator.UsedHeapMemory);
        }
        finally { foreach (var b in live) b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PurgeScanShouldEvictIdleChunks(bool direct)
    {
        using var allocator = Allocator(reuseCapacity: 2, purgePolls: 4, threshold: 1);
        var live = new List<ByteBuf>();
        ByteBuf NewBuffer() => direct ? allocator.DirectBuffer(256, 256) : allocator.HeapBuffer(256, 256);
        try
        {
            for (int i = 0; i < 10 * 512; i++) live.Add(NewBuffer());
            foreach (var b in live) b.Release(); live.Clear();
            long before = direct ? allocator.UsedDirectMemory : allocator.UsedHeapMemory;
            for (int poll = 0; poll < 128; poll++)
            {
                for (int i = 0; i < 512; i++) live.Add(NewBuffer());
                foreach (var b in live) b.Release(); live.Clear();
            }
            long after = direct ? allocator.UsedDirectMemory : allocator.UsedHeapMemory;
            Assert.True(after < before, $"before={before}, after={after}");
            Assert.True(after >= 2 * 131072);
        }
        finally { foreach (var b in live) b.Release(); }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void DedicatedCutoffsAndLowMemoryDoNotRetainFallbackChunks(bool direct, bool lowMemory)
    {
        var native = new NativeMemoryAllocator(); using var allocator = Allocator(lowMemory, native);
        foreach (int size in new[] { 16897, 32769, 262145, 1048576, 1048577 })
        {
            long before = direct ? allocator.UsedDirectMemory : allocator.UsedHeapMemory;
            ByteBuf b = direct ? allocator.DirectBuffer(size, size + 3) : allocator.HeapBuffer(size, size + 3);
            AdaptiveChunkAllocation reservation = Root(b).Allocation;
            bool pooled = new AdaptiveAllocationPolicy(lowMemory).IsPooled(size);
            b.SetInt(0, 12345); Assert.Equal(12345, b.GetInt(0)); b.Release();
            long after = direct ? allocator.UsedDirectMemory : allocator.UsedHeapMemory;
            if (!pooled) Assert.Equal(before, after);
            Assert.Equal(pooled ? new AdaptiveAllocationPolicy(lowMemory).ComputeBufferCapacity(size, size + 3) : size, reservation.Length);
        }
        allocator.Dispose(); Assert.Equal(0, allocator.UsedHeapMemory + allocator.UsedDirectMemory); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BuddyAllocationConsistency(bool direct)
    {
        using var allocator = Allocator(); var random = new Random(91);
        for (int iteration = 0; iteration < 100; iteration++)
        {
            int[] sizes = [32768,32768,32768,32768,32768,32768,32768,32768,65536,65536,65536,65536,131072,131072];
            random.Shuffle(sizes); ByteBuf[] buffers = new ByteBuf[sizes.Length];
            try
            {
                for (int i = 0; i < sizes.Length; i++) buffers[i] = direct ? allocator.DirectBuffer(sizes[i], sizes[i]) : allocator.HeapBuffer(sizes[i], sizes[i]);
                random.Shuffle(buffers);
                for (int i = 0; i < buffers.Length / 2; i++)
                {
                    int size = buffers[i].Capacity; buffers[i].Release(); buffers[i] = null;
                    buffers[i] = direct ? allocator.DirectBuffer(size, size) : allocator.HeapBuffer(size, size);
                }
                for (int i = 0; i < buffers.Length; i++) buffers[i].AsSpan(0, buffers[i].Capacity).Fill((byte)(i + 1));
                for (int i = 0; i < buffers.Length; i++) Assert.True(buffers[i].AsSpan(0, buffers[i].Capacity).IndexOfAnyExcept((byte)(i + 1)) < 0);
            }
            finally { foreach (var buffer in buffers) buffer?.Release(); }
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TestAllocateWithoutLock(bool lowMemory)
    {
        var native = new NativeMemoryAllocator(); using var allocator = Allocator(lowMemory, native);
        await Task.WhenAll(Enumerable.Range(0, 16).Select(worker => Task.Run(() =>
        {
            var random = new Random(worker);
            for (int i = 0; i < 256; i++)
            {
                ByteBuf b = allocator.DirectBuffer(128, 65536);
                try
                {
                    b.WriteInt(worker); b.EnsureWritable(random.Next(512, 32769)); Assert.Equal(worker, b.GetInt(0));
                    ByteBuf retained = b.RetainedSlice(0, 4); Assert.Equal(2, b.ReferenceCount); retained.Release();
                }
                finally { b.Release(); }
            }
        }, TestContext.Current.CancellationToken)));
        allocator.Dispose(); Assert.Equal(0, allocator.UsedDirectMemory); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(1)] [InlineData(4)]
    public async Task ForcedContentionExpandsStripesOrUsesDedicatedFallback(int stripes)
    {
        using var allocator = Allocator(stripes: stripes);
        AdaptiveMagazine magazine = allocator.HeapPool.Group(0).Magazines[0];
        using var locked = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var holder = new Thread(() => { lock (magazine.AllocationLock) { locked.Set(); release.Wait(); } });
        holder.Start(); locked.Wait(TestContext.Current.CancellationToken);
        try
        {
            Task<ByteBuf> allocation = Task.Run(() => allocator.HeapBuffer(7, 100), TestContext.Current.CancellationToken);
            if (stripes > 1)
            {
                Assert.True(SpinWait.SpinUntil(() => allocator.HeapPool.Group(0).Magazines.Length > 1, TimeSpan.FromSeconds(10)));
                release.Set();
            }
            ByteBuf b = await allocation;
            try
            {
                Assert.Equal(stripes == 1 ? 7 : 32, Root(b).Allocation.Length);
                Assert.Equal(stripes == 1 ? 7 : 131072, allocator.UsedHeapMemory);
            }
            finally { b.Release(); }
        }
        finally { release.Set(); Assert.True(holder.Join(TimeSpan.FromSeconds(10))); }
        allocator.Dispose();
        Assert.Equal(0, allocator.UsedHeapMemory);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DisposePreservesLiveViewsAndPinsButClosesReallocation(bool direct)
    {
        var native = new NativeMemoryAllocator(); using var allocator = Allocator(native: native);
        ByteBuf b = direct ? allocator.DirectBuffer(7, 100) : allocator.HeapBuffer(7, 100); b.SetInt(0, 12345);
        ByteBuf child = b.RetainedSlice(0, 4); var pin = child.AsMemory(0, 4).Pin();
        b.Release(); allocator.Dispose(); Assert.Equal(12345, child.GetInt(0));
        AdaptiveByteBuf root = Root(child); root.Capacity = 32;
        Assert.Throws<ObjectDisposedException>(() => root.Capacity = 33);
        Assert.Throws<ObjectDisposedException>(() => allocator.HeapBuffer(1));
        Assert.Throws<ObjectDisposedException>(() => allocator.DirectBuffer(1));
        child.Release(); Assert.Equal(131072, allocator.UsedHeapMemory + allocator.UsedDirectMemory);
        pin.Dispose(); Assert.Equal(0, allocator.UsedHeapMemory + allocator.UsedDirectMemory); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void NativeAllocationFailureLeavesExistingBuffersUsableAndCountersBalanced()
    {
        var native = new NativeMemoryAllocator(131072); using var allocator = Allocator(native: native);
        ByteBuf b = allocator.DirectBuffer(7, 100); b.SetInt(0, 12345);
        try
        {
            Assert.Throws<OutOfMemoryException>(() => b.Capacity = 33);
            Assert.Equal(7, b.Capacity); Assert.Equal(12345, b.GetInt(0)); Assert.Equal(131072, allocator.UsedDirectMemory);
        }
        finally { b.Release(); }
        allocator.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CompositeConsolidationUsesTheSameAllocatorAndNativeReservationDomain(bool direct)
    {
        var native = new NativeMemoryAllocator(); using var allocator = Allocator(native: native);
        CompositeByteBuf composite = direct ? allocator.CompositeDirectBuffer(1) : allocator.CompositeHeapBuffer(1);
        try
        {
            ByteBuf first = direct ? allocator.DirectBuffer(4, 100) : allocator.HeapBuffer(4, 100);
            ByteBuf second = direct ? allocator.DirectBuffer(4, 100) : allocator.HeapBuffer(4, 100);
            first.WriteInt(12345); second.WriteInt(67890);
            composite.AddComponent(first, true).AddComponent(second, true);
            Assert.Equal(12345, composite.GetInt(0)); Assert.Equal(67890, composite.GetInt(4));
            Assert.Same(allocator, composite.Allocator); Assert.Equal(direct, composite.IsDirect);
        }
        finally { composite.Release(); }
        allocator.Dispose(); Assert.Equal(0, allocator.UsedHeapMemory + allocator.UsedDirectMemory); Assert.Equal(0, native.ReservedBytes);
    }
}
