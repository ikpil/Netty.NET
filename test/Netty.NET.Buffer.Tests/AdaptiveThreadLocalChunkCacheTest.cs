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
using System.Linq;
using System.Reflection;

namespace Netty.NET.Buffer.Tests;

// Real local chunk reservations replace the original Mockito capacity stubs.
public class AdaptiveThreadLocalChunkCacheTest
{
    private sealed class Fixture(int maxChunks = 64, int floor = 2, long polls = 16, int threshold = 3) : IDisposable
    {
        internal readonly AdaptiveThreadLocalChunkCache Cache = new(131072, polls, threshold, maxChunks * 131072, floor * 131072);
        internal readonly AdaptiveChunkRegistry Registry = new();
        internal readonly List<AdaptiveSizeClassedChunk> Chunks = new();
        internal readonly List<AdaptiveChunkAllocation> Claims = new();
        internal AdaptiveSizeClassedChunk Chunk(int allocated = 1)
        {
            var chunk = new AdaptiveSizeClassedChunk(new UnpooledHeapByteBuf(131072, 131072), Registry, 4096, true);
            Chunks.Add(chunk);
            for (int i = 0; i < allocated; i++) Claims.Add(chunk.TryAllocate(4096));
            return chunk;
        }
        internal AdaptiveSizeClassedChunk[] Slots => (AdaptiveSizeClassedChunk[])typeof(AdaptiveThreadLocalChunkCache)
            .GetField("_chunks", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Cache);
        public void Dispose()
        {
            Cache.Dispose(); foreach (var c in Chunks) c.Dispose(); foreach (var a in Claims) a.Release();
            Assert.Equal(0, Registry.TotalCapacity);
        }
    }

    // --- purge: selection (both caches) ---
    [Fact]
    public void PurgeSelectsFirstChunkWithCapacity()
    {
        using var f = new Fixture(); var empty = f.Chunk(32); var working = f.Chunk();
        f.Cache.OfferChunk(empty); f.Cache.OfferChunk(working);
        Assert.Same(working, f.Cache.ForcePurge()); Assert.Equal(1, f.Cache.Count);
    }
    [Fact]
    public void PurgeReturnsNullWhenCacheIsEmpty()
    { using var f = new Fixture(); Assert.Null(f.Cache.ForcePurge()); }
    [Fact]
    public void PurgeReturnsNullWhenNoChunkHasCapacity()
    {
        using var f = new Fixture(); f.Cache.OfferChunk(f.Chunk(32)); f.Cache.OfferChunk(f.Chunk(32));
        Assert.Null(f.Cache.ForcePurge()); Assert.Equal(2, f.Cache.Count);
    }
    // --- purge: epoch aging and eviction (both caches) ---
    [Fact]
    public void FullChunkAgesEachPurgeAndIsEvictedPastThresholdThreadLocal()
    {
        using var f = new Fixture(); var working = f.Chunk(); var idle = f.Chunk(0);
        f.Cache.OfferChunk(working); f.Cache.OfferChunk(f.Chunk(32)); f.Cache.OfferChunk(idle);
        for (int epoch = 1; epoch <= 3; epoch++)
        {
            Assert.Same(working, f.Cache.ForcePurge()); Assert.Equal(epoch, idle.PurgeEpoch);
            Assert.False(idle.IsDeallocated); f.Cache.OfferChunk(working);
        }
        Assert.Same(working, f.Cache.ForcePurge()); Assert.True(idle.IsDeallocated);
    }
    [Fact]
    public void NonFullChunkDoesNotAge()
    {
        using var f = new Fixture(); var chunk = f.Chunk(); f.Cache.OfferChunk(chunk);
        Assert.Same(chunk, f.Cache.ForcePurge()); Assert.Equal(0, chunk.PurgeEpoch);
    }
    [Fact]
    public void SelectedFullChunkHasEpochReset()
    {
        using var f = new Fixture(); var chunk = f.Chunk(0); f.Cache.OfferChunk(chunk);
        Assert.Same(chunk, f.Cache.ForcePurge()); Assert.Equal(0, chunk.PurgeEpoch);
    }
    // --- scanForCapacity: fallback (both caches) ---
    [Fact]
    public void ScanForCapacityFallbackFindsChunkThatGainedCapacity()
    {
        using var f = new Fixture(); var chunk = f.Chunk(32); f.Cache.OfferChunk(chunk);
        // Purge: no capacity, nothing selected
        Assert.Null(f.Cache.ForcePurge());
        // External segment return gives the chunk capacity
        // CLR: a local return makes HasRemainingCapacity visible immediately; actual external
        // returns are tested separately with the pinned lazy-capacity rule.
        f.Claims[0].Release(); Assert.Same(chunk, f.Cache.PollChunk(0));
    }
    // --- thread-local only: capacity-first ordering ---
    [Fact]
    public void PurgeMovesCapacityChunksBeforeNoCapacityChunks()
    {
        using var f = new Fixture();
        f.Cache.OfferChunk(f.Chunk(32)); f.Cache.OfferChunk(f.Chunk()); f.Cache.OfferChunk(f.Chunk(32));
        f.Cache.OfferChunk(f.Chunk()); f.Cache.OfferChunk(f.Chunk());
        // Purge selects one capacity chunk, partitions the rest: [cap, cap | noCap, noCap]
        Assert.NotNull(f.Cache.ForcePurge());
        // Both remaining capacity chunks come out before any no-capacity chunk
        Assert.NotNull(f.Cache.PollChunk(0)); Assert.NotNull(f.Cache.PollChunk(0));
        Assert.Null(f.Cache.PollChunk(0)); Assert.Equal(2, f.Cache.Count);
    }
    [Fact]
    public void ScanForCapacityUsesO1FastPathAfterPurge()
    {
        using var f = new Fixture(); f.Cache.OfferChunk(f.Chunk(32)); f.Cache.OfferChunk(f.Chunk()); f.Cache.OfferChunk(f.Chunk());
        // Purge partitions: [cap | noCap], selects one cap
        Assert.NotNull(f.Cache.ForcePurge());
        // Next poll hits the O(1) fast path — capacity chunk is at head
        Assert.NotNull(f.Cache.PollChunk(0)); Assert.Null(f.Cache.PollChunk(0));
    }
    // --- thread-local only: ring buffer mechanics ---
    [Fact]
    public void OfferGrowsRingWhenFull()
    {
        using var f = new Fixture();
        // Initial ring size is 8 — offer 9 to trigger growth
        for (int i = 0; i < 9; i++) Assert.True(f.Cache.OfferChunk(f.Chunk()));
        Assert.Equal(16, f.Cache.RingLength);
        // Purge selects one, 8 remain — all should be retrievable
        Assert.NotNull(f.Cache.ForcePurge());
        var selected = new HashSet<AdaptiveChunk>();
        for (int i = 0; i < 8; i++) Assert.True(selected.Add(f.Cache.PollChunk(0)));
        Assert.Null(f.Cache.PollChunk(0)); Assert.Equal(0, f.Cache.Count);
    }
    [Theory]
    [InlineData(4)] [InlineData(6)] [InlineData(8)] [InlineData(15)]
    public void PurgeHandlesWrappedRingCorrectlyAndLeavesNoStaleReferences(int advance)
    {
        using var f = new Fixture();
        // Fill 6 slots of the initial ring (size=8), purge, drain to advance head
        // CLR: parameterize the head advance across small and expanded rings.
        for (int i = 0; i < advance; i++) f.Cache.OfferChunk(f.Chunk());
        Assert.NotNull(f.Cache.ForcePurge());
        while (f.Cache.PollChunk(0) != null) { }
        // Offer 4 chunks — wraps past the array boundary
        f.Cache.OfferChunk(f.Chunk(32)); f.Cache.OfferChunk(f.Chunk());
        f.Cache.OfferChunk(f.Chunk(32)); f.Cache.OfferChunk(f.Chunk());
        // Purge partitions on the wrapped ring
        Assert.NotNull(f.Cache.ForcePurge()); Assert.NotNull(f.Cache.PollChunk(0)); Assert.Null(f.Cache.PollChunk(0));
        // Verify the backing array: exactly count non-null entries, no stale refs
        Assert.Equal(f.Cache.Count, f.Slots.Count(c => c != null)); Assert.Equal(2, f.Cache.Count);
    }
    // --- bursty traffic: idle chunks are eventually evicted ---
    [Fact]
    public void CacheSettlesAtRetentionFloorAfterBurstThreadLocal()
    {
        using var f = new Fixture(maxChunks: 12); var working = f.Chunk(); f.Cache.OfferChunk(working);
        f.Cache.OfferChunk(f.Chunk(32)); var idle = Enumerable.Range(0, 10).Select(_ => f.Chunk(0)).ToArray();
        foreach (var c in idle) Assert.True(f.Cache.OfferChunk(c));
        for (int i = 0; i < 4; i++) { Assert.Same(working, f.Cache.ForcePurge()); f.Cache.OfferChunk(working); }
        Assert.All(idle, c => Assert.True(c.IsDeallocated)); Assert.Equal(2, f.Cache.Count);
    }
    // --- full-but-active chunk must not be prematurely evicted ---
    // A chunk that is polled every purge cycle but whose buffers are short-lived
    // (all segments return before next purge) looks "full" (remaining == capacity)
    // at purge time. Purge must not treat it as idle.
    [Fact]
    public void ActiveChunkWithShortLivedBuffersShouldNotBeEvicted()
    {
        using var f = new Fixture(); f.Cache.OfferChunk(f.Chunk(32)); f.Cache.OfferChunk(f.Chunk(32));
        var active = f.Chunk(0); f.Cache.OfferChunk(active);
        for (int i = 0; i < 20; i++)
        {
            Assert.Same(active, f.Cache.ForcePurge()); var claim = active.TryAllocate(4096); claim.Release();
            Assert.Equal(0, active.PurgeEpoch); f.Cache.OfferChunk(active);
        }
        Assert.False(active.IsDeallocated);
    }
    // --- free: draining all chunks (thread-local) ---
    [Fact]
    public void FreeDrainsAllChunksIncludingNoCapThreadLocal()
    {
        using var f = new Fixture(); var chunk = f.Chunk(32); f.Cache.OfferChunk(chunk);
        // pollChunk uses scanForCapacity which skips noCap chunks — they're stuck.
        // This is why free() is needed instead of a pollChunk drain loop.
        Assert.Null(f.Cache.PollChunk(0)); Assert.Equal(1, f.Cache.Count);
        f.Cache.Dispose(); Assert.Equal(0, f.Cache.Count); Assert.False(chunk.IsDeallocated);
        Assert.All(f.Slots, c => Assert.Null(c)); Assert.Null(f.Cache.PollChunk(0)); Assert.False(f.Cache.OfferChunk(chunk));
        foreach (var claim in f.Claims) claim.Release(); Assert.True(chunk.IsDeallocated);
    }
    [Theory]
    [InlineData(1, 1)] [InlineData(3, 1)] [InlineData(8, 8)] [InlineData(9, 20)]
    public void ThreadLocalCacheRejectsChunksWhenCapReached(int max, int min)
    {
        using var f = new Fixture(max, min); Assert.Equal(max, f.Cache.MaxCachedChunks);
        Assert.Equal(Math.Min(max, min), f.Cache.PurgeRetentionFloor);
        // Fill to capacity
        for (int i = 0; i < max; i++) Assert.True(f.Cache.OfferChunk(f.Chunk()));
        // Next offer should be rejected
        Assert.False(f.Cache.OfferChunk(f.Chunk())); Assert.Equal(max, f.Cache.Count);
    }
}
