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
using System.Threading.Tasks;
using static Netty.NET.Buffer.AdaptivePoolingAllocator;

namespace Netty.NET.Buffer.Tests;

// CLR: shared-cache supplement with real 4096-byte reservations in 128-KiB chunks and a reduced per-instance
// configuration (reuse queue 2, 4 polls per purge, threshold 1). The original fixture is SizeClassedChunkCacheTest.
public class SharedSizeClassedChunkCacheTest
{
    private sealed class Fixture : IDisposable
    {
        internal readonly SharedSizeClassedChunkCache Cache = new(2, 4, 1);
        internal readonly ChunkRegistry Registry = new();
        internal readonly List<SizeClassedChunk> Chunks = new();
        internal readonly List<AdaptiveChunkAllocation> Claims = new();
        internal SizeClassedChunk Chunk(int allocated)
        {
            var chunk = new SizeClassedChunk(new UnpooledHeapByteBuf(131072, 131072), Registry, 4096);
            Chunks.Add(chunk);
            for (int i = 0; i < allocated; i++) Claims.Add(chunk.TryAllocate(4096));
            return chunk;
        }
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
        using var f = new Fixture(); var noCapacity = f.Chunk(32); var capacity = f.Chunk(1);
        f.Cache.OfferChunk(noCapacity); f.Cache.OfferChunk(capacity);
        Assert.Same(capacity, f.Cache.ForcePurge()); Assert.Equal(1, f.Cache.Count);
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
    [Fact]
    public void FullChunkAgesAndIsEventuallyEvictedShared()
    {
        using var f = new Fixture();
        f.Cache.OfferChunk(f.Chunk(32)); f.Cache.OfferChunk(f.Chunk(32));
        var working = f.Chunk(1); var idle = f.Chunk(0);
        f.Cache.OfferChunk(working); f.Cache.OfferChunk(idle);
        for (int i = 0; i < 6; i++)
        { var selected = f.Cache.ForcePurge(); if (selected != null) f.Cache.OfferChunk(selected); }
        Assert.True(idle.IsDeallocated); Assert.False(working.IsDeallocated);
    }
    [Fact]
    public void NonFullChunkDoesNotAge()
    {
        using var f = new Fixture(); var chunk = f.Chunk(1); f.Cache.OfferChunk(chunk);
        Assert.Same(chunk, f.Cache.ForcePurge()); Assert.Equal(0, chunk.PurgeEpoch);
    }
    [Fact]
    public void SelectedFullChunkHasEpochReset()
    {
        using var f = new Fixture();
        // All segments available → purge ages it.
        var chunk = f.Chunk(0); f.Cache.OfferChunk(chunk);
        Assert.Same(chunk, f.Cache.ForcePurge()); Assert.Equal(0, chunk.PurgeEpoch);
    }
    // --- scanForCapacity: fallback (both caches) ---
    [Fact]
    public void ScanForCapacityFallbackFindsChunkThatGainedCapacity()
    {
        using var f = new Fixture(); var chunk = f.Chunk(32); f.Cache.OfferChunk(chunk);
        Assert.Null(f.Cache.PollChunk(4096)); f.Claims[0].Release();
        Assert.Same(chunk, f.Cache.PollChunk(4096)); Assert.Equal(4096, chunk.RemainingCapacity);
    }
    [Fact]
    public void CacheSettlesAfterBurstShared()
    {
        using var f = new Fixture(); var working = f.Chunk(1); f.Cache.OfferChunk(working);
        f.Cache.OfferChunk(f.Chunk(32)); var excess = Enumerable.Range(0, 10).Select(_ => f.Chunk(0)).ToArray();
        foreach (var chunk in excess) Assert.True(f.Cache.OfferChunk(chunk));
        for (int i = 0; i < 6; i++)
        { var selected = f.Cache.ForcePurge(); if (selected != null) f.Cache.OfferChunk(selected); }
        Assert.All(excess, chunk => Assert.True(chunk.IsDeallocated));
    }
    [Fact]
    public void ActiveChunkWithShortLivedBuffersShouldNotBeEvicted()
    {
        using var f = new Fixture(); f.Cache.OfferChunk(f.Chunk(32)); f.Cache.OfferChunk(f.Chunk(32));
        // Full chunk: remaining==capacity>0, has capacity.
        // Simulates short-lived buffers: chunk polled, used, all segments return before next purge.
        var active = f.Chunk(0); f.Cache.OfferChunk(active);
        for (int i = 0; i < 4; i++)
        {
            Assert.Same(active, f.Cache.ForcePurge()); Assert.Equal(0, active.PurgeEpoch);
            var allocation = active.TryAllocate(4096); allocation.Release(); f.Cache.OfferChunk(active);
        }
        Assert.False(active.IsDeallocated);
    }
    // --- shared cache: concurrent scanForCapacity must not livelock ---
    [Fact]
    public async Task ConcurrentScansTerminateWhenNoCapacity()
    {
        using var f = new Fixture();
        // Fill with no-capacity chunks — no scan can find anything
        for (int i = 0; i < 10; i++) f.Cache.OfferChunk(f.Chunk(32));
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 1000; i++) Assert.Null(f.Cache.PollChunk(256));
        }, TestContext.Current.CancellationToken))).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        // With the == sentinel check, threads could livelock here.
        // With >= ordering, all scans terminate promptly.
        Assert.Equal(10, f.Cache.Count);
    }
    [Fact]
    public void FreeDrainsAllChunksShared()
    {
        using var f = new Fixture(); var busy = f.Chunk(32); var idle = f.Chunk(0);
        f.Cache.OfferChunk(busy); f.Cache.OfferChunk(idle); f.Cache.Dispose();
        Assert.True(f.Cache.IsEmpty); Assert.True(idle.IsDeallocated); Assert.False(busy.IsDeallocated);
        Assert.False(f.Cache.OfferChunk(f.Chunk(0))); Assert.Null(f.Cache.PollChunk(0));
    }
}
