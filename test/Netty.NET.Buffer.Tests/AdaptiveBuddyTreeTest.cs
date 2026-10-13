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

namespace Netty.NET.Buffer.Tests;

public class AdaptiveBuddyTreeTest
{
    [Theory]
    [InlineData(32768)] [InlineData(65536)] [InlineData(131072)]
    [InlineData(262144)] [InlineData(1048576)] [InlineData(8388608)]
    public void ExhaustionFragmentationAndWholeChunkCoalescence(int capacity)
    {
        var tree = new AdaptiveBuddyTree(capacity); int unit = AdaptiveBuddyTree.MinBuddySize;
        for (int offset = 0; offset < capacity; offset += unit) Assert.Equal(offset, tree.TryAllocate(unit));
        Assert.Equal(-1, tree.TryAllocate(unit)); Assert.Equal(0, tree.RemainingCapacity);
        for (int offset = 0; offset < capacity; offset += 2 * unit) tree.Free(offset, unit);
        Assert.Equal(-1, tree.TryAllocate(2 * unit));
        for (int offset = unit; offset < capacity; offset += 2 * unit) tree.Free(offset, unit);
        Assert.Equal(capacity, tree.RemainingCapacity); Assert.Equal(0, tree.TryAllocate(capacity));
        Assert.Equal(-1, tree.TryAllocate(unit)); tree.Free(0, capacity); Assert.Equal(capacity, tree.RemainingCapacity);
    }
    [Fact]
    public void MixedSizeClaimsKeepUnrelatedSiblingsReserved()
    {
        var t = new AdaptiveBuddyTree(262144);
        Assert.Equal(0, t.TryAllocate(131072)); Assert.Equal(131072, t.TryAllocate(65536)); Assert.Equal(196608, t.TryAllocate(65536));
        t.Free(0, 131072); Assert.Equal(-1, t.TryAllocate(262144));
        Assert.Equal(0, t.TryAllocate(65536)); Assert.Equal(65536, t.TryAllocate(65536));
        t.Free(131072, 65536); t.Free(196608, 65536); Assert.Equal(131072, t.TryAllocate(131072));
        t.Free(0, 65536); t.Free(65536, 65536); t.Free(131072, 131072); Assert.Equal(0, t.TryAllocate(262144));
        t.Free(0, 262144); Assert.Equal(262144, t.RemainingCapacity);
    }
    [Theory]
    [InlineData(131072, 0)] [InlineData(131072, 941)]
    [InlineData(1048576, 0)] [InlineData(1048576, 941)]
    [InlineData(8388608, 0)] [InlineData(8388608, 941)]
    public void SeededAllocationsMatchAnIndependentAlignedIntervalModel(int capacity, int seed)
    {
        var t = new AdaptiveBuddyTree(capacity); var random = new Random(seed);
        bool[] occupied = new bool[capacity / AdaptiveBuddyTree.MinBuddySize]; var live = new List<(int Offset, int Size)>();
        for (int step = 0; step < 4096; step++)
        {
            if (live.Count > 0 && random.Next(3) == 0)
            {
                int index = random.Next(live.Count); var item = live[index]; live.RemoveAt(index);
                t.Free(item.Offset, item.Size); Array.Fill(occupied, false, item.Offset / 32768, item.Size / 32768);
            }
            else
            {
                int size = 32768 << random.Next(10); int units = size / 32768, expected = -1;
                for (int start = 0; start <= occupied.Length - units; start += units)
                {
                    bool free = true; for (int i = start; i < start + units; i++) if (occupied[i]) { free = false; break; }
                    if (free) { expected = start * 32768; break; }
                }
                Assert.Equal(expected, t.TryAllocate(size));
                if (expected >= 0) { live.Add((expected, size)); Array.Fill(occupied, true, expected / 32768, units); }
            }
            int used = 0; foreach (var item in live) used += item.Size;
            Assert.Equal(capacity - used, t.RemainingCapacity);
        }
        foreach (var item in live) t.Free(item.Offset, item.Size);
        Assert.Equal(capacity, t.RemainingCapacity); Assert.Equal(0, t.TryAllocate(capacity)); t.Free(0, capacity);
    }
    [Theory]
    [InlineData(32768)] [InlineData(65536)] [InlineData(131072)] [InlineData(262144)] [InlineData(524288)]
    [InlineData(1048576)] [InlineData(2097152)] [InlineData(4194304)] [InlineData(8388608)]
    public void PackedFreeSegmentsRoundTripAtEverySupportedTreeLevel(int size)
    {
        for (int offset = 0; offset <= 8388608 - size; offset += size)
        {
            int packed = AdaptiveBuddyTree.PackFreeSegment(offset, size);
            Assert.Equal(size, AdaptiveBuddyTree.UnpackSize(packed)); Assert.Equal(offset, AdaptiveBuddyTree.UnpackOffset(packed));
        }
    }
    [Fact]
    public void InvalidAndDuplicateReturnsPreserveLiveReservations()
    {
        var t = new AdaptiveBuddyTree(131072); Assert.Equal(0, t.TryAllocate(65536));
        Assert.Throws<InvalidOperationException>(() => t.Free(0, 131072));
        Assert.Throws<InvalidOperationException>(() => t.Free(0, 32768));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Free(1, 65536));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Free(131072, 65536));
        Assert.Equal(65536, t.RemainingCapacity); Assert.Equal(65536, t.TryAllocate(65536));
        t.Free(0, 65536); Assert.Throws<InvalidOperationException>(() => t.Free(0, 65536));
        Assert.Equal(65536, t.RemainingCapacity); t.Free(65536, 65536); Assert.Equal(0, t.TryAllocate(131072));
        t.Free(0, 131072);
    }
    [Fact]
    public void ShapeAndPackingValidationKeepTheByteFlagRepresentationBounded()
    {
        foreach (int size in new[] { -1, 0, 32767, 32769, int.MaxValue })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AdaptiveBuddyTree(size));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AdaptiveBuddyTree(131072).TryAllocate(size));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => new AdaptiveBuddyTree(16777216));
        Assert.Throws<ArgumentOutOfRangeException>(() => AdaptiveBuddyTree.PackFreeSegment(1, 32768));
        Assert.Throws<ArgumentOutOfRangeException>(() => AdaptiveBuddyTree.PackFreeSegment(8388608, 32768));
        Assert.Throws<ArgumentOutOfRangeException>(() => AdaptiveBuddyTree.PackFreeSegment(0, 16777216));
    }
}
