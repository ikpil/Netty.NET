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
using static Netty.NET.Buffer.AdaptivePoolingAllocator;

namespace Netty.NET.Buffer.Tests;

public class AdaptiveBuddyChunkCacheTest
{
    [Fact]
    public void EqualCapacityUsesNewestEntryAndCeilingUsesSmallestKey()
    {
        var registry = new ChunkRegistry();
        using var cache = new ConcurrentSkipListChunkCache(8);
        using var first = new BuddyChunk(new UnpooledHeapByteBuf(131072, 131072), registry);
        using var second = new BuddyChunk(new UnpooledHeapByteBuf(131072, 131072), registry);
        using var large = new BuddyChunk(new UnpooledHeapByteBuf(262144, 262144), registry);
        cache.OfferChunk(first);
        cache.OfferChunk(second);
        cache.OfferChunk(large);
        Assert.Same(second, cache.PollChunk(32768));
        Assert.Same(first, cache.PollChunk(131072));
        Assert.Same(large, cache.PollChunk(131073));
        Assert.True(cache.IsEmpty);
    }

    [Fact]
    public void PendingReturnsReindexCapacityAndChooseLargestRecoveredCandidate()
    {
        var registry = new ChunkRegistry();
        using var cache = new ConcurrentSkipListChunkCache(8);
        using var small = new BuddyChunk(new UnpooledHeapByteBuf(131072, 131072), registry);
        using var large = new BuddyChunk(new UnpooledHeapByteBuf(262144, 262144), registry);
        var a = small.TryAllocate(131072);
        var b = large.TryAllocate(262144);
        cache.OfferChunk(small);
        cache.OfferChunk(large);
        a.Release();
        b.Release();
        Assert.Same(large, cache.PollChunk(65536));
        Assert.False(large.HasUnprocessedFreelistEntries);
        Assert.Same(small, cache.PollChunk(131072));
        Assert.True(cache.IsEmpty);
    }

    [Fact]
    public void RetentionPressureNeverEvictsChunksWithLiveRegionClaims()
    {
        var registry = new ChunkRegistry();
        using var cache = new ConcurrentSkipListChunkCache(2);
        var chunks = new List<BuddyChunk>();
        var allocations = new List<AdaptiveChunkAllocation>();

        try
        {
            for (int i = 0; i < 4; i++)
            {
                var chunk = new BuddyChunk(new UnpooledHeapByteBuf(131072, 131072), registry);
                chunks.Add(chunk);
                allocations.Add(chunk.TryAllocate(32768));
                cache.OfferChunk(chunk);
            }

            Assert.Equal(4, cache.Count);
            Assert.Equal(524288, registry.TotalCapacity);
            allocations[0].Release();
            allocations[1].Release();
            var idle = new BuddyChunk(new UnpooledHeapByteBuf(131072, 131072), registry);
            chunks.Add(idle);
            cache.OfferChunk(idle);
            Assert.Equal(2, cache.Count);
            Assert.True(chunks[0].IsDeallocated);
            Assert.True(chunks[1].IsDeallocated);
            Assert.True(idle.IsDeallocated);
        }
        finally
        {
            cache.Dispose();

            foreach (var a in allocations)
                a.Release();

            foreach (var c in chunks)
                c.Dispose();
        }

        Assert.Equal(0, registry.TotalCapacity);
    }

    [Fact]
    public void NonPowerOfTwoResidualReservationFallsBackWithoutChangingLiveBuddies()
    {
        var registry = new ChunkRegistry();
        using var chunk = new BuddyChunk(new UnpooledHeapByteBuf(131072, 131072), registry);
        var a = chunk.TryAllocate(32768);
        Assert.Equal(98304, chunk.RemainingCapacity);
        Assert.Null(chunk.TryAllocate(98304));
        Assert.Equal(98304, chunk.RemainingCapacity);
        var b = chunk.TryAllocate(65536);
        Assert.Equal(65536, b.Offset);
        a.Release();
        b.Release();
        chunk.Dispose();
        Assert.Equal(0, registry.TotalCapacity);
    }
}
