/*
 * Copyright 2022 The Netty Project
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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// Supplementary tests for actual adaptive storage ownership; the original
// classification fixture is kept separately in AdaptivePoolingAllocatorTest.
public class AdaptiveChunkTest
{
    public static IEnumerable<object[]> FixedClasses()
    {
        foreach (int segment in AdaptiveAllocationPolicy.GetSizeClasses())
            foreach (bool local in new[] { false, true })
                foreach (bool direct in new[] { false, true }) yield return [segment, local, direct];
    }

    private static ByteBuf Storage(bool direct, int capacity, NativeMemoryAllocator native)
        => direct ? new UnpooledDirectByteBuf(UnpooledByteBufAllocator.Default, capacity, capacity, native)
            : new UnpooledHeapByteBuf(capacity, capacity);

    [Theory]
    [MemberData(nameof(FixedClasses))]
    public void FixedSlotsExhaustReuseAndRetireWithOriginalOffsetOrder(int segment, bool local, bool direct)
    {
        int capacity = new AdaptiveAllocationPolicy(false).ComputeChunkSize(segment);
        int count = capacity / segment, remainder = capacity % segment;
        var native = new NativeMemoryAllocator(); var registry = new AdaptiveChunkRegistry();
        ByteBuf storage = Storage(direct, capacity, native);
        using var chunk = new AdaptiveSizeClassedChunk(storage, registry, segment, local);
        var live = new List<AdaptiveChunkAllocation>();
        try
        {
            Assert.Equal(capacity, registry.TotalCapacity); Assert.Equal(direct, chunk.IsDirect);
            Assert.True(chunk.IsPooled); Assert.True(chunk.HasFullCapacity);
            for (int i = 0; i < count; i++)
            {
                var allocation = chunk.TryAllocate(segment);
                Assert.NotNull(allocation); live.Add(allocation);
                Assert.Equal(i * segment + (local ? remainder : 0), allocation.Offset);
                allocation.Memory.Span[0] = (byte)(i % 251);
            }
            Assert.Null(chunk.TryAllocate(segment)); Assert.Equal(0, chunk.RemainingCapacity);
            Assert.False(chunk.HasFullCapacity); Assert.False(chunk.HasRemainingCapacity);
            for (int i = 0; i < count; i++) Assert.Equal((byte)(i % 251), live[i].Memory.Span[0]);
            Assert.Equal(!direct, MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)live[0].Memory, out var array));
            if (!direct) Assert.Equal(live[0].Offset, array.Offset);
            for (int i = count - 1; i >= 0; i--) live[i].Release();
            Assert.True(chunk.HasFullCapacity); Assert.Equal(count * segment, chunk.RemainingCapacity);
            var reused = chunk.TryAllocate(Math.Min(7, segment)); live.Add(reused);
            Assert.Equal(local ? remainder : (count - 1) * segment, reused.Offset);
            Assert.Equal(Math.Min(7, segment), reused.Length);
            chunk.Dispose(); chunk.Dispose();
            Assert.Equal(capacity, registry.TotalCapacity); Assert.Equal(1, storage.ReferenceCount);
            Assert.Throws<ObjectDisposedException>(() => chunk.TryAllocate(segment));
            reused.Release(); reused.Release();
            Assert.True(chunk.IsDeallocated); Assert.Equal(0, storage.ReferenceCount);
            Assert.Equal(0, registry.TotalCapacity); Assert.Equal(0, native.ReservedBytes);
        }
        finally { foreach (var allocation in live) allocation.Release(); }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void OperationAndCopiedPinClaimsDelayFixedSlotReuse(bool direct, bool local)
    {
        var native = new NativeMemoryAllocator(); var registry = new AdaptiveChunkRegistry();
        using var chunk = new AdaptiveSizeClassedChunk(Storage(direct, 131072, native), registry, 16896, local);
        var held = chunk.TryAllocate(13); Memory<byte> borrowed = held.Memory;
        borrowed.Span[0] = 47;
        var lease = held.AcquireLease(); var leaseCopy = lease;
        using var pin = borrowed.Pin(); var copiedPin = pin;
        held.Release(); held.Release();
        Assert.Throws<ObjectDisposedException>(() => borrowed.Span.Clear());
        Assert.Throws<ObjectDisposedException>(() => borrowed.Pin());
        Assert.Throws<ObjectDisposedException>(() => held.AcquireLease());
        var live = new List<AdaptiveChunkAllocation>();
        try
        {
            AdaptiveChunkAllocation next;
            while ((next = chunk.TryAllocate(16896)) != null)
            {
                Assert.NotEqual(held.Offset, next.Offset); live.Add(next); next.Memory.Span.Fill(99);
            }
            lease.Dispose(); leaseCopy.Dispose();
            Assert.Null(chunk.TryAllocate(16896)); Assert.Equal(47, Marshal.ReadByte((nint)PinPointer(pin)));
            copiedPin.Dispose();
            var reused = chunk.TryAllocate(16896); Assert.NotNull(reused); live.Add(reused);
            Assert.Equal(held.Offset, reused.Offset);
        }
        finally { foreach (var allocation in live) allocation.Release(); }
        chunk.Dispose(); Assert.Equal(0, registry.TotalCapacity); Assert.Equal(0, native.ReservedBytes);
    }

    private static unsafe nint PinPointer(System.Buffers.MemoryHandle pin) => (nint)pin.Pointer;

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BuddyReturnsStayQueuedUntilOwnerAllocatesOrDrains(bool direct)
    {
        var native = new NativeMemoryAllocator(); var registry = new AdaptiveChunkRegistry();
        ByteBuf storage = Storage(direct, 131072, native);
        using var chunk = new AdaptiveBuddyChunk(storage, registry);
        var first = chunk.TryAllocate(65536); var second = chunk.TryAllocate(65536);
        Assert.Null(chunk.TryAllocate(32768));
        Assert.Equal(0, first.Offset); Assert.Equal(65536, second.Offset);
        first.Memory.Span.Fill(17); second.Memory.Span.Fill(31);
        var lease = first.AcquireLease(); first.Release();
        Assert.False(chunk.HasUnprocessedFreelistEntries); Assert.Equal(0, chunk.RemainingCapacity);
        lease.Dispose(); Assert.True(chunk.HasUnprocessedFreelistEntries); Assert.Equal(65536, chunk.RemainingCapacity);
        Assert.Contains("remaining: 0, free list: 1", chunk.ToString());
        var replacement = chunk.TryAllocate(32768);
        Assert.Equal(0, replacement.Offset); Assert.False(chunk.HasUnprocessedFreelistEntries);
        Assert.Equal(32768, chunk.RemainingCapacity); Assert.Equal(31, second.Memory.Span[0]);
        chunk.Dispose(); Assert.False(chunk.IsDeallocated); Assert.Equal(131072, registry.TotalCapacity);
        replacement.Release(); second.Release();
        Assert.True(chunk.IsDeallocated); Assert.Equal(0, storage.ReferenceCount);
        Assert.Equal(0, registry.TotalCapacity); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DedicatedChunkOwnerAndPinsHaveIndependentLifetimes(bool direct)
    {
        var native = new NativeMemoryAllocator(); var registry = new AdaptiveChunkRegistry();
        using var chunk = new AdaptiveChunk(Storage(direct, 37, native), registry);
        var allocation = chunk.TryAllocate(37);
        Assert.False(chunk.IsPooled); Assert.Null(chunk.TryAllocate(1));
        Memory<byte> memory = allocation.Memory; memory.Span[36] = 121;
        var pin = memory.Slice(36).Pin(); var copied = pin;
        chunk.Dispose(); allocation.Release();
        Assert.False(chunk.IsDeallocated); Assert.Equal(37, registry.TotalCapacity);
        Assert.Equal(121, Marshal.ReadByte(PinPointer(pin)));
        pin.Dispose(); copied.Dispose();
        Assert.True(chunk.IsDeallocated); Assert.Equal(0, registry.TotalCapacity); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void InitializationFailureRestoresReservationsAndOwnership(int kind)
    {
        var storage = new FaultingHeap(131072); var registry = new AdaptiveChunkRegistry();
        using AdaptiveChunk chunk = kind switch
        {
            0 => new AdaptiveChunk(storage, registry),
            1 => new AdaptiveSizeClassedChunk(storage, registry, 16896),
            _ => new AdaptiveBuddyChunk(storage, registry)
        };
        int size = kind == 1 ? 16896 : 32768;
        Assert.Throws<InvalidOperationException>(() => chunk.TryAllocate(size));
        Assert.Equal(131072, chunk.RemainingCapacity); Assert.Equal(1, storage.ReferenceCount);
        storage.Fail = false;
        var live = new List<AdaptiveChunkAllocation>();
        try { AdaptiveChunkAllocation a; while ((a = chunk.TryAllocate(size)) != null) live.Add(a); }
        finally { foreach (var a in live) a.Release(); }
        Assert.Equal(kind == 1 ? 7 : 4, live.Count);
        chunk.Dispose(); Assert.Equal(0, registry.TotalCapacity); Assert.Equal(0, storage.ReferenceCount);
    }

    private sealed class FaultingHeap(int capacity) : UnpooledHeapByteBuf(capacity, capacity)
    {
        internal bool Fail = true;
        protected override Memory<byte> GetMemoryCore(int index, int length)
            => Fail ? throw new InvalidOperationException("injected initialization failure") : base.GetMemoryCore(index, length);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LocalReturnsPreferTheStackAndExternalReturnsUseTheQueue(bool direct)
    {
        var registry = new AdaptiveChunkRegistry(); var native = new NativeMemoryAllocator();
        using var chunk = new AdaptiveSizeClassedChunk(Storage(direct, 131072, native), registry, 16896, true);
        var live = new List<AdaptiveChunkAllocation>();
        for (int i = 0; i < 7; i++) live.Add(chunk.TryAllocate(16896));
        Exception error = null;
        var thread = new Thread(() =>
        {
            try
            {
                Assert.Throws<InvalidOperationException>(() => chunk.TryAllocate(1));
                Assert.Throws<InvalidOperationException>(() => chunk.Dispose());
                live[0].Release();
            }
            catch (Exception e) { error = e; }
        });
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(10))); Assert.Null(error);
        live[1].Release();
        var local = chunk.TryAllocate(1); var external = chunk.TryAllocate(1);
        Assert.Equal(live[1].Offset, local.Offset); Assert.Equal(live[0].Offset, external.Offset);
        local.Release(); external.Release(); foreach (var a in live) a.Release();
        chunk.Dispose(); Assert.Equal(0, registry.TotalCapacity); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(2, false)] [InlineData(2, true)]
    public void ConcurrentExternalReturnsRaceRetirementWithoutLeakingOrDoubleFreeing(int kind, bool direct)
    {
        var registry = new AdaptiveChunkRegistry(); var native = new NativeMemoryAllocator();
        for (int iteration = 0; iteration < 100; iteration++)
        {
            ByteBuf storage = Storage(direct, 131072, native);
            using AdaptiveChunk chunk = kind == 2 ? new AdaptiveBuddyChunk(storage, registry)
                : new AdaptiveSizeClassedChunk(storage, registry, 16896, kind == 1);
            var live = new List<AdaptiveChunkAllocation>(); int size = kind == 2 ? 32768 : 16896;
            AdaptiveChunkAllocation a; while ((a = chunk.TryAllocate(size)) != null) live.Add(a);
            using var start = new ManualResetEventSlim(); Exception error = null;
            var threads = new Thread[3];
            for (int i = 0; i < threads.Length; i++)
            {
                int worker = i;
                threads[i] = new Thread(() =>
                {
                    try { start.Wait(); for (int j = worker; j < live.Count; j += threads.Length) live[j].Release(); }
                    catch (Exception e) { Interlocked.CompareExchange(ref error, e, null); }
                });
                threads[i].Start();
            }
            start.Set(); chunk.Dispose();
            foreach (var t in threads) Assert.True(t.Join(TimeSpan.FromSeconds(10)));
            Assert.Null(error); Assert.True(chunk.IsDeallocated); Assert.Equal(0, storage.ReferenceCount);
            Assert.Equal(0, registry.TotalCapacity); Assert.Equal(0, native.ReservedBytes);
        }
    }

    [Fact]
    public void InvalidConstructionLeavesRawDelegateAndRegistryOwnedByCaller()
    {
        ByteBuf storage = new UnpooledHeapByteBuf(131073, 131073); var registry = new AdaptiveChunkRegistry();
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AdaptiveSizeClassedChunk(storage, registry, 7));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AdaptiveBuddyChunk(storage, registry));
            Assert.Equal(0, registry.TotalCapacity); Assert.Equal(1, storage.ReferenceCount);
        }
        finally { storage.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AbandonedPinClaimEventuallyReleasesRetiredChunk(bool direct)
    {
        var registry = new AdaptiveChunkRegistry(); var native = new NativeMemoryAllocator();
        using var chunk = new AdaptiveSizeClassedChunk(Storage(direct, 131072, native), registry, 16896);
        AbandonPin(chunk);
        chunk.Dispose();
        for (int i = 0; i < 10 && !chunk.IsDeallocated; i++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers();
        }
        Assert.True(chunk.IsDeallocated); Assert.Equal(0, registry.TotalCapacity); Assert.Equal(0, native.ReservedBytes);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AbandonPin(AdaptiveChunk chunk)
    {
        var allocation = chunk.TryAllocate(13);
        var pin = allocation.Memory.Pin(); allocation.Release();
        GC.KeepAlive(pin);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EmptyDedicatedStorageAndOnePastEndPinsHaveBalancedOwnership(bool direct)
    {
        var registry = new AdaptiveChunkRegistry(); var native = new NativeMemoryAllocator();
        using var chunk = new AdaptiveChunk(Storage(direct, 0, native), registry);
        var allocation = chunk.TryAllocate(0);
        Assert.Equal(0, allocation.Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => allocation.Pin(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => allocation.Pin(1));
        var pin = allocation.Pin(0);
        chunk.Dispose(); allocation.Release(); Assert.False(chunk.IsDeallocated);
        Assert.Equal(direct ? 1 : 0, native.ReservedBytes);
        pin.Dispose(); Assert.True(chunk.IsDeallocated); Assert.Equal(0, native.ReservedBytes);
    }
}
