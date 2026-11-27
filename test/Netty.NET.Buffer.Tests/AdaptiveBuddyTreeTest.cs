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
using static Netty.NET.Buffer.AdaptivePoolingAllocator;

namespace Netty.NET.Buffer.Tests;

// The buddy tree lives in BuddyChunk, as in the source. These cases drive it through real
// reservations: a return goes through releaseSegment's packing, the free list and accept().
public class AdaptiveBuddyTreeTest
{
    private sealed class Tree : IDisposable
    {
        private readonly Dictionary<int, AdaptiveChunkAllocation> _live = new();
        internal readonly ChunkRegistry Registry = new();
        internal readonly BuddyChunk Owner;
        internal Tree(int capacity)
        {
            Owner = new BuddyChunk(new UnpooledHeapByteBuf(capacity, capacity), Registry);
        }
        internal int RemainingCapacity => Owner.RemainingCapacity;
        internal int TryAllocate(int size)
        {
            AdaptiveChunkAllocation allocation = Owner.TryAllocate(size);
            if (allocation == null)
                return -1;
            Assert.Equal(size, allocation.Length);
            _live.Add(allocation.Offset, allocation);
            return allocation.Offset;
        }

        internal void Free(int offset, int size)
        {
            Assert.True(_live.Remove(offset, out var allocation));
            Assert.Equal(size, allocation.Length);
            allocation.Release();
            Assert.True(Owner.HasUnprocessedFreelistEntries);
            Owner.ProcessFreelistEntries();
            Assert.False(Owner.HasUnprocessedFreelistEntries);
        }

        public void Dispose()
        {
            foreach (var allocation in _live.Values)
                allocation.Release();
            Owner.Dispose();
            Assert.True(Owner.IsDeallocated);
            Assert.Equal(0, Registry.TotalCapacity);
        }
    }

    [Theory]
    [InlineData(32768)] [InlineData(65536)] [InlineData(131072)]
    [InlineData(262144)] [InlineData(1048576)] [InlineData(8388608)]
    public void ExhaustionFragmentationAndWholeChunkCoalescence(int capacity)
    {
        using var tree = new Tree(capacity);
        int unit = BuddyChunk.MIN_BUDDY_SIZE;
        for (int offset = 0; offset < capacity; offset += unit)
            Assert.Equal(offset, tree.TryAllocate(unit));
        Assert.Equal(-1, tree.TryAllocate(unit));
        Assert.Equal(0, tree.RemainingCapacity);
        for (int offset = 0; offset < capacity; offset += 2 * unit)
            tree.Free(offset, unit);
        Assert.Equal(-1, tree.TryAllocate(2 * unit));
        for (int offset = unit; offset < capacity; offset += 2 * unit)
            tree.Free(offset, unit);
        Assert.Equal(capacity, tree.RemainingCapacity);
        Assert.Equal(0, tree.TryAllocate(capacity));
        Assert.Equal(-1, tree.TryAllocate(unit));
        tree.Free(0, capacity);
        Assert.Equal(capacity, tree.RemainingCapacity);
    }

    [Fact]
    public void MixedSizeClaimsKeepUnrelatedSiblingsReserved()
    {
        using var t = new Tree(262144);
        Assert.Equal(0, t.TryAllocate(131072));
        Assert.Equal(131072, t.TryAllocate(65536));
        Assert.Equal(196608, t.TryAllocate(65536));
        t.Free(0, 131072);
        Assert.Equal(-1, t.TryAllocate(262144));
        Assert.Equal(0, t.TryAllocate(65536));
        Assert.Equal(65536, t.TryAllocate(65536));
        t.Free(131072, 65536);
        t.Free(196608, 65536);
        Assert.Equal(131072, t.TryAllocate(131072));
        t.Free(0, 65536);
        t.Free(65536, 65536);
        t.Free(131072, 131072);
        Assert.Equal(0, t.TryAllocate(262144));
        t.Free(0, 262144);
        Assert.Equal(262144, t.RemainingCapacity);
    }

    [Theory]
    [InlineData(131072, 0)] [InlineData(131072, 941)]
    [InlineData(1048576, 0)] [InlineData(1048576, 941)]
    [InlineData(8388608, 0)] [InlineData(8388608, 941)]
    public void SeededAllocationsMatchAnIndependentAlignedIntervalModel(int capacity, int seed)
    {
        using var t = new Tree(capacity);
        var random = new Random(seed);
        bool[] occupied = new bool[capacity / BuddyChunk.MIN_BUDDY_SIZE];
        var live = new List<(int Offset, int Size)>();
        for (int step = 0; step < 4096; step++)
        {
            if (live.Count > 0 && random.Next(3) == 0)
            {
                int index = random.Next(live.Count);
                var item = live[index];
                live.RemoveAt(index);
                t.Free(item.Offset, item.Size);
                Array.Fill(occupied, false, item.Offset / 32768, item.Size / 32768);
            }
            else
            {
                int size = 32768 << random.Next(10);
                int units = size / 32768, expected = -1;
                for (int start = 0; start <= occupied.Length - units; start += units)
                {
                    bool free = true;
                    for (int i = start; i < start + units; i++)
                    if (occupied[i])
                    {
                        free = false;
                        break;
                    }
                    if (free)
                    {
                        expected = start * 32768;
                        break;
                    }
                }
                Assert.Equal(expected, t.TryAllocate(size));
                if (expected >= 0)
                {
                    live.Add((expected, size));
                    Array.Fill(occupied, true, expected / 32768, units);
                }
            }
            int used = 0;
            foreach (var item in live)
                used += item.Size;
            Assert.Equal(capacity - used, t.RemainingCapacity);
        }
        foreach (var item in live)
            t.Free(item.Offset, item.Size);
        Assert.Equal(capacity, t.RemainingCapacity);
        Assert.Equal(0, t.TryAllocate(capacity));
        t.Free(0, capacity);
    }

    [Theory]
    [InlineData(32768)] [InlineData(65536)] [InlineData(131072)] [InlineData(262144)] [InlineData(524288)]
    [InlineData(1048576)] [InlineData(2097152)] [InlineData(4194304)] [InlineData(8388608)]
    public void PackedFreeSegmentsRoundTripAtEverySupportedTreeLevel(int size)
    {
        // releaseSegment packs every offset of this level; remainingCapacity unpacks the sizes and
        // accept() unpacks the offsets before the whole 8 MiB chunk can be claimed again.
        using var t = new Tree(8388608);
        var live = new List<AdaptiveChunkAllocation>();
        for (int offset = 0; offset <= 8388608 - size; offset += size)
        {
            AdaptiveChunkAllocation allocation = t.Owner.TryAllocate(size);
            Assert.Equal(offset, allocation.Offset);
            live.Add(allocation);
        }
        Assert.Equal(0, t.RemainingCapacity);
        foreach (var allocation in live)
            allocation.Release();
        Assert.True(t.Owner.HasUnprocessedFreelistEntries);
        Assert.Equal(8388608, t.RemainingCapacity);
        Assert.Contains("free list: " + live.Count, t.Owner.ToString());
        t.Owner.ProcessFreelistEntries();
        Assert.False(t.Owner.HasUnprocessedFreelistEntries);
        Assert.Equal(8388608, t.RemainingCapacity);
        Assert.Equal(0, t.TryAllocate(8388608));
        t.Free(0, 8388608);
    }

    [Fact]
    public void InvalidAndDuplicateReturnsPreserveLiveReservations()
    {
        // Real returns always describe a live reservation, so mismatched returns go straight to
        // unreserveMatchingBuddy; it rejects them before clearing any claim.
        using var t = new Tree(131072);
        Assert.Equal(0, t.TryAllocate(65536));
        Assert.Throws<InvalidOperationException>(() => t.Owner.UnreserveMatchingBuddy(1, 131072, 0, 0));
        Assert.Throws<InvalidOperationException>(() => t.Owner.UnreserveMatchingBuddy(1, 32768, 0, 0));
        Assert.Throws<InvalidOperationException>(() => t.Owner.UnreserveMatchingBuddy(1, 65536, 1, 0));
        Assert.Throws<InvalidOperationException>(() => t.Owner.UnreserveMatchingBuddy(1, 65536, 131072, 0));
        Assert.Equal(65536, t.RemainingCapacity);
        Assert.Equal(65536, t.TryAllocate(65536));
        t.Free(0, 65536);
        Assert.Throws<InvalidOperationException>(() => t.Owner.UnreserveMatchingBuddy(1, 65536, 0, 0));
        Assert.Equal(65536, t.RemainingCapacity);
        t.Free(65536, 65536);
        Assert.Equal(0, t.TryAllocate(131072));
        t.Free(0, 131072);
    }

    [Fact]
    public void ShapeValidationKeepsTheByteFlagRepresentationBounded()
    {
        foreach (int capacity in new[] { 0, 32767, 32769, 131073, 16777216 })
        {
            var storage = new UnpooledHeapByteBuf(capacity, capacity);
            var registry = new ChunkRegistry();
            try
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new BuddyChunk(storage, registry));
                Assert.Equal(1, storage.RefCnt);
                Assert.Equal(0, registry.TotalCapacity);
            }
            finally
            {
                storage.Release();
            }
        }
        using var t = new Tree(131072);
        // Sizes that are not a claimable power of two find no buddy, as the source's search does.
        foreach (int size in new[] { -1, 0, 32767, 32769, 98304, int.MaxValue })
            Assert.Equal(-1, t.TryAllocate(size));
        Assert.Equal(131072, t.RemainingCapacity);
        Assert.Equal(0, t.TryAllocate(131072));
        t.Free(0, 131072);
    }
}
