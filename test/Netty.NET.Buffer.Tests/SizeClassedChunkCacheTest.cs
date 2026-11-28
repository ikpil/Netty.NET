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
using System.Globalization;
using System.Reflection;
using System.Threading;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Buffer.AdaptivePoolingAllocator;

namespace Netty.NET.Buffer.Tests;

// CLR: The class is sealed and its capacity queries are not virtual. Holding 7, 8 or 0 segment claims reproduces
// each stub exactly: remainingCapacity 512/0/4096 of capacity 4096 with the stubbed
// hasRemainingCapacity/hasFullCapacity values. A stubbed capacity change becomes a real external segment return,
// and verify(chunk[, mode]).markToDeallocate() becomes the chunk's retirement flag, which only
// Chunk.MarkToDeallocate sets (src/Netty.NET.Buffer/AdaptivePoolingAllocator.cs). Mockito:
// https://javadoc.io/doc/org.mockito/mockito-core/latest/org/mockito/Mockito.html
public class SizeClassedChunkCacheTest : IDisposable
{
    // CLR: the source reads these AdaptivePoolingAllocator class-initializer statics; the CLR pool carries them per
    // instance and AdaptiveByteBufAllocator supplies the same defaults from the same system properties
    // (src/Netty.NET.Buffer/AdaptiveByteBufAllocator.cs). SizeClassedChunkCache.Create takes that pool.
    private static readonly int CHUNK_REUSE_QUEUE = Math.Max(2, SystemPropertyUtil.GetInt(
            "io.netty.allocator.chunkReuseQueueCapacity", (int)Math.Min(int.MaxValue, 2L * NettyRuntime.AvailableProcessors())));
    private static readonly long CHUNK_PURGE_POLLS_THREAD_LOCAL = Math.Max(1, SystemPropertyUtil.GetLong(
            "io.netty.allocator.chunkPurgePollsThreadLocal", 16L));
    private static readonly int CHUNK_PURGE_THRESHOLD = Math.Max(1, SystemPropertyUtil.GetInt(
            "io.netty.allocator.chunkPurgeThreshold", 3));
    private static readonly int THREAD_LOCAL_CACHE_MAX_BYTES = Math.Max(1, SystemPropertyUtil.GetInt(
            "io.netty.allocator.threadLocalChunkCacheMaxBytes", 8 * 1024 * 1024));
    private static readonly int THREAD_LOCAL_CACHE_MIN_BYTES = Math.Min(THREAD_LOCAL_CACHE_MAX_BYTES,
            Math.Max(1, SystemPropertyUtil.GetInt(
                    "io.netty.allocator.threadLocalChunkCacheMinBytes",
                    THREAD_LOCAL_CACHE_MAX_BYTES / 2)));
    private static readonly AdaptivePoolingAllocator Pool = new AdaptiveByteBufAllocator(false).HeapPool;

    private static readonly int TL_CHUNK_SIZE = THREAD_LOCAL_CACHE_MAX_BYTES / 8;

    private readonly ChunkRegistry _registry = new();
    private readonly List<SizeClassedChunk> _chunks = new();
    private readonly Dictionary<SizeClassedChunk, Queue<AdaptiveChunkAllocation>> _claims = new();

    private static int ThreadLocalPurgeFloor()
    {
        ThreadLocalSizeClassedChunkCache cache = NewThreadLocalSizeClassedChunkCache(TL_CHUNK_SIZE);
        return cache.PurgeRetentionFloor;
    }

    private SizeClassedChunk ChunkWithCapacity()
    {
        SizeClassedChunk chunk = Mock(7);
        Assert.Equal(512, chunk.RemainingCapacity);
        Assert.Equal(4096, chunk.Capacity);
        Assert.True(chunk.HasRemainingCapacity);
        Assert.False(chunk.HasFullCapacity);
        return chunk;
    }

    private SizeClassedChunk ChunkWithoutCapacity()
    {
        SizeClassedChunk chunk = Mock(8);
        Assert.Equal(0, chunk.RemainingCapacity);
        Assert.Equal(4096, chunk.Capacity);
        Assert.False(chunk.HasRemainingCapacity);
        Assert.False(chunk.HasFullCapacity);
        return chunk;
    }

    private SizeClassedChunk FullChunk()
    {
        // All segments available → purge ages it.
        SizeClassedChunk chunk = Mock(0);
        Assert.Equal(4096, chunk.RemainingCapacity);
        Assert.Equal(4096, chunk.Capacity);
        Assert.True(chunk.HasRemainingCapacity);
        Assert.True(chunk.HasFullCapacity);
        return chunk;
    }

    // --- purge: selection (both caches) ---

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PurgeSelectsFirstChunkWithCapacity(bool threadLocal)
    {
        SizeClassedChunkCache cache = SizeClassedChunkCache.Create(threadLocal, 1024, Pool);

        SizeClassedChunk noCap = ChunkWithoutCapacity();
        SizeClassedChunk cap = ChunkWithCapacity();
        cache.OfferChunk(noCap);
        cache.OfferChunk(cap);

        Assert.Same(cap, cache.ForcePurge());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PurgeReturnsNullWhenCacheIsEmpty(bool threadLocal)
    {
        SizeClassedChunkCache cache = SizeClassedChunkCache.Create(threadLocal, 1024, Pool);
        Assert.Null(cache.ForcePurge());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PurgeReturnsNullWhenNoChunkHasCapacity(bool threadLocal)
    {
        SizeClassedChunkCache cache = SizeClassedChunkCache.Create(threadLocal, 1024, Pool);
        cache.OfferChunk(ChunkWithoutCapacity());
        cache.OfferChunk(ChunkWithoutCapacity());

        Assert.Null(cache.ForcePurge());
    }

    // --- purge: epoch aging and eviction (both caches) ---

    [Fact]
    public void FullChunkAgesEachPurgeAndIsEvictedPastThresholdThreadLocal()
    {
        SizeClassedChunkCache cache = SizeClassedChunkCache.Create(true, TL_CHUNK_SIZE, Pool);

        for (int i = 0; i < ThreadLocalPurgeFloor(); i++)
        {
            cache.OfferChunk(ChunkWithoutCapacity());
        }

        SizeClassedChunk workingSet = ChunkWithCapacity();
        cache.OfferChunk(workingSet);
        SizeClassedChunk idle = FullChunk();
        cache.OfferChunk(idle);

        for (int i = 0; i < CHUNK_PURGE_THRESHOLD; i++)
        {
            SizeClassedChunk polled = cache.ForcePurge();
            Assert.Same(workingSet, polled);
            cache.OfferChunk(workingSet);
            Assert.Equal(i + 1, idle.PurgeEpoch);
            Assert.False(MarkedToDeallocate(idle));
        }

        {
            // CLR: braces scope the source's second `polled` local, which C# rejects in the enclosing method scope (CS0136).
            SizeClassedChunk polled = cache.ForcePurge();
            Assert.Same(workingSet, polled);
        }
        Assert.True(MarkedToDeallocate(idle));
    }

    [Fact]
    public void FullChunkAgesAndIsEventuallyEvictedShared()
    {
        SizeClassedChunkCache cache = SizeClassedChunkCache.Create(false, 1024, Pool);

        for (int i = 0; i < CHUNK_REUSE_QUEUE; i++)
        {
            cache.OfferChunk(ChunkWithoutCapacity());
        }

        SizeClassedChunk workingSet = ChunkWithCapacity();
        cache.OfferChunk(workingSet);
        SizeClassedChunk idle = FullChunk();
        cache.OfferChunk(idle);

        int maxCycles = (CHUNK_PURGE_THRESHOLD + 1) * 3;

        for (int i = 0; i < maxCycles; i++)
        {
            SizeClassedChunk polled = cache.ForcePurge();
            if (polled != null)
            {
                cache.OfferChunk(polled);
            }
        }

        Assert.True(MarkedToDeallocate(idle));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NonFullChunkDoesNotAge(bool threadLocal)
    {
        SizeClassedChunkCache cache = SizeClassedChunkCache.Create(threadLocal, 1024, Pool);

        SizeClassedChunk chunk = ChunkWithCapacity();
        cache.OfferChunk(chunk);

        cache.ForcePurge();
        Assert.Equal(0, chunk.PurgeEpoch);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SelectedFullChunkHasEpochReset(bool threadLocal)
    {
        SizeClassedChunkCache cache = SizeClassedChunkCache.Create(threadLocal, 1024, Pool);

        SizeClassedChunk chunk = FullChunk();
        cache.OfferChunk(chunk);

        SizeClassedChunk selected = cache.ForcePurge();
        Assert.Same(chunk, selected);
        Assert.Equal(0, selected.PurgeEpoch);
    }

    // --- scanForCapacity: fallback (both caches) ---

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ScanForCapacityFallbackFindsChunkThatGainedCapacity(bool threadLocal)
    {
        SizeClassedChunkCache cache = SizeClassedChunkCache.Create(threadLocal, 1024, Pool);

        SizeClassedChunk chunk = ChunkWithoutCapacity();
        cache.OfferChunk(chunk);

        // Purge: no capacity, nothing selected
        Assert.Null(cache.ForcePurge());

        // External segment return gives the chunk capacity
        ReturnSegment(chunk);

        Assert.Same(chunk, cache.PollChunk(256));
    }

    // --- thread-local only: capacity-first ordering ---

    [Fact]
    public void PurgeMovesCapacityChunksBeforeNoCapacityChunks()
    {
        SizeClassedChunkCache cache = SizeClassedChunkCache.Create(true, 1024, Pool);

        cache.OfferChunk(ChunkWithoutCapacity());
        cache.OfferChunk(ChunkWithCapacity());
        cache.OfferChunk(ChunkWithoutCapacity());
        cache.OfferChunk(ChunkWithCapacity());
        cache.OfferChunk(ChunkWithCapacity());

        // Purge selects one capacity chunk, partitions the rest: [cap, cap | noCap, noCap]
        SizeClassedChunk selected = cache.ForcePurge();
        Assert.NotNull(selected);
        Assert.True(selected.HasRemainingCapacity);

        // Both remaining capacity chunks come out before any no-capacity chunk
        Assert.True(cache.PollChunk(256).HasRemainingCapacity);
        Assert.True(cache.PollChunk(256).HasRemainingCapacity);
        Assert.Null(cache.PollChunk(256));
    }

    [Fact]
    public void ScanForCapacityUsesO1FastPathAfterPurge()
    {
        SizeClassedChunkCache cache = SizeClassedChunkCache.Create(true, 1024, Pool);

        cache.OfferChunk(ChunkWithCapacity());
        cache.OfferChunk(ChunkWithCapacity());
        cache.OfferChunk(ChunkWithoutCapacity());

        // Purge partitions: [cap | noCap], selects one cap
        Assert.NotNull(cache.ForcePurge());

        // Next poll hits the O(1) fast path — capacity chunk is at head
        SizeClassedChunk fast = cache.PollChunk(256);
        Assert.NotNull(fast);
        Assert.True(fast.HasRemainingCapacity);
    }

    // --- thread-local only: ring buffer mechanics ---

    [Fact]
    public void OfferGrowsRingWhenFull()
    {
        SizeClassedChunkCache cache = SizeClassedChunkCache.Create(true, 1024, Pool);

        // Initial ring size is 8 — offer 9 to trigger growth
        for (int i = 0; i < 9; i++)
        {
            cache.OfferChunk(ChunkWithCapacity());
        }

        // Purge selects one, 8 remain — all should be retrievable
        Assert.NotNull(cache.ForcePurge());

        for (int i = 0; i < 8; i++)
        {
            Assert.NotNull(cache.PollChunk(256));
        }

        Assert.Null(cache.PollChunk(256));
    }

    [Fact]
    public void PurgeHandlesWrappedRingCorrectly()
    {
        SizeClassedChunkCache cache = SizeClassedChunkCache.Create(true, 1024, Pool);

        // Fill with 4, purge (linearizes to head=0), consume 3 to advance head
        for (int i = 0; i < 4; i++)
        {
            cache.OfferChunk(ChunkWithCapacity());
        }

        cache.ForcePurge();
        cache.PollChunk(256);
        cache.PollChunk(256);
        cache.PollChunk(256);

        // Offer more — tail wraps around past the array end
        cache.OfferChunk(ChunkWithoutCapacity());
        cache.OfferChunk(ChunkWithCapacity());
        cache.OfferChunk(ChunkWithoutCapacity());
        cache.OfferChunk(ChunkWithCapacity());

        // Purge with wrapped ring should still partition correctly
        SizeClassedChunk selected = cache.ForcePurge();
        Assert.NotNull(selected);
        Assert.True(selected.HasRemainingCapacity);

        // Remaining capacity chunk at head
        SizeClassedChunk next = cache.PollChunk(256);
        if (next != null)
        {
            Assert.True(next.HasRemainingCapacity);
        }
    }

    [Fact]
    public void WrappedRingCompactionLeavesNoStaleReferences()
    {
        ThreadLocalSizeClassedChunkCache cache =
                (ThreadLocalSizeClassedChunkCache)
                        SizeClassedChunkCache.Create(true, 1024, Pool);

        // Fill 6 slots of the initial ring (size=8), purge, drain to advance head
        for (int i = 0; i < 6; i++)
        {
            cache.OfferChunk(ChunkWithCapacity());
        }

        cache.ForcePurge();

        while (cache.PollChunk(256) != null)
        {
            // drain
        }

        Assert.True(Head(cache) > 0, "head should have advanced past 0");

        // Offer 4 chunks — wraps past the array boundary
        cache.OfferChunk(ChunkWithCapacity());
        cache.OfferChunk(ChunkWithCapacity());
        cache.OfferChunk(FullChunk());
        cache.OfferChunk(FullChunk());
        Assert.True(Tail(cache) < Head(cache),
                Text($"ring should wrap: tail={Tail(cache)} < head={Head(cache)}"));

        // Purge partitions on the wrapped ring
        SizeClassedChunk polled = cache.ForcePurge();
        Assert.NotNull(polled);

        // Verify the backing array: exactly count non-null entries, no stale refs
        int nonNull = 0;

        for (int i = 0; i < Chunks(cache).Length; i++)
        {
            if (Chunks(cache)[i] != null)
            {
                nonNull++;
            }
        }

        Assert.True(cache.Count == nonNull,
                Text($"backing array should have exactly count={cache.Count} non-null entries, but found {nonNull}"));
    }

    // --- bursty traffic: idle chunks are eventually evicted ---

    [Fact]
    public void CacheSettlesAtRetentionFloorAfterBurstThreadLocal()
    {
        SizeClassedChunkCache cache = SizeClassedChunkCache.Create(true, TL_CHUNK_SIZE, Pool);

        int floor = ThreadLocalPurgeFloor();
        int cap = Math.Max(2, THREAD_LOCAL_CACHE_MAX_BYTES / TL_CHUNK_SIZE);
        int excess = cap - floor;
        Assert.True(excess > 0, "excess must be positive for the test to be meaningful");

        SizeClassedChunk workingSet = ChunkWithCapacity();
        cache.OfferChunk(workingSet);

        for (int i = 0; i < floor - 1; i++)
        {
            cache.OfferChunk(ChunkWithoutCapacity());
        }

        SizeClassedChunk[] excessChunks = new SizeClassedChunk[excess];

        for (int i = 0; i < excess; i++)
        {
            excessChunks[i] = FullChunk();
            cache.OfferChunk(excessChunks[i]);
        }

        for (int i = 0; i < CHUNK_PURGE_THRESHOLD + 1; i++)
        {
            SizeClassedChunk polled = cache.ForcePurge();
            Assert.Same(workingSet, polled);
            cache.OfferChunk(workingSet);
        }

        foreach (SizeClassedChunk chunk in excessChunks)
        {
            Assert.True(MarkedToDeallocate(chunk));
        }

        Assert.False(MarkedToDeallocate(workingSet));
    }

    [Fact]
    public void CacheSettlesAfterBurstShared()
    {
        SizeClassedChunkCache cache = SizeClassedChunkCache.Create(false, 1024, Pool);

        int crq = CHUNK_REUSE_QUEUE;
        int capacity = Math.Max(16, crq * 2);
        int excess = Math.Min(10, capacity - crq);
        Assert.True(excess > 0, "excess must be positive for the test to be meaningful");
        SizeClassedChunk workingSet = ChunkWithCapacity();
        cache.OfferChunk(workingSet);

        for (int i = 0; i < crq - 1; i++)
        {
            cache.OfferChunk(ChunkWithoutCapacity());
        }

        SizeClassedChunk[] excessChunks = new SizeClassedChunk[excess];

        for (int i = 0; i < excess; i++)
        {
            excessChunks[i] = FullChunk();
            Assert.True(cache.OfferChunk(excessChunks[i]), "all excess chunks must be accepted by the queue");
        }

        int maxCycles = (CHUNK_PURGE_THRESHOLD + 1) * 3;

        for (int i = 0; i < maxCycles; i++)
        {
            SizeClassedChunk polled = cache.ForcePurge();
            if (polled != null)
            {
                cache.OfferChunk(polled);
            }
        }

        foreach (SizeClassedChunk chunk in excessChunks)
        {
            Assert.True(MarkedToDeallocate(chunk));
        }
    }

    // --- epoch aging with working set ---
    // Scan resets epoch on pick (the chunk is being used). Partition sub-ordering puts
    // epoch=0 (recently used) at head, epoch>0 (idle) behind. Scan prefers head, so
    // idle chunks age undisturbed behind the working set.

    [Fact]
    public void ExcessFullChunksAgeWhileWorkingSetIsPreferredThreadLocal()
    {
        SizeClassedChunkCache cache = SizeClassedChunkCache.Create(true, TL_CHUNK_SIZE, Pool);

        for (int i = 0; i < ThreadLocalPurgeFloor(); i++)
        {
            cache.OfferChunk(ChunkWithoutCapacity());
        }

        SizeClassedChunk workingSet = ChunkWithCapacity();
        cache.OfferChunk(workingSet);
        int excess = 3;
        SizeClassedChunk[] idleChunks = new SizeClassedChunk[excess];

        for (int i = 0; i < excess; i++)
        {
            idleChunks[i] = FullChunk();
            cache.OfferChunk(idleChunks[i]);
        }

        for (int i = 0; i < CHUNK_PURGE_THRESHOLD + 1; i++)
        {
            SizeClassedChunk polled = cache.ForcePurge();
            Assert.True(ReferenceEquals(workingSet, polled), Text($"cycle {i}: scan should prefer working-set chunk"));
            cache.OfferChunk(workingSet);
        }

        foreach (SizeClassedChunk idle in idleChunks)
        {
            Assert.True(MarkedToDeallocate(idle));
        }

        Assert.False(MarkedToDeallocate(workingSet));
    }

    [Fact]
    public void ExcessFullChunksEventuallyEvictedShared()
    {
        SizeClassedChunkCache cache = SizeClassedChunkCache.Create(false, 1024, Pool);

        for (int i = 0; i < CHUNK_REUSE_QUEUE; i++)
        {
            cache.OfferChunk(ChunkWithoutCapacity());
        }

        SizeClassedChunk workingSet = ChunkWithCapacity();
        cache.OfferChunk(workingSet);
        int excess = 3;
        SizeClassedChunk[] idleChunks = new SizeClassedChunk[excess];

        for (int i = 0; i < excess; i++)
        {
            idleChunks[i] = FullChunk();
            cache.OfferChunk(idleChunks[i]);
        }

        int maxCycles = (CHUNK_PURGE_THRESHOLD + 1) * 3;

        for (int i = 0; i < maxCycles; i++)
        {
            SizeClassedChunk polled = cache.ForcePurge();
            if (polled != null)
            {
                cache.OfferChunk(polled);
            }
        }

        foreach (SizeClassedChunk idle in idleChunks)
        {
            Assert.True(MarkedToDeallocate(idle));
        }
    }

    // --- full-but-active chunk must not be prematurely evicted ---
    // A chunk that is polled every purge cycle but whose buffers are short-lived
    // (all segments return before next purge) looks "full" (remaining == capacity)
    // at purge time. Purge must not treat it as idle.

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ActiveChunkWithShortLivedBuffersShouldNotBeEvicted(bool threadLocal)
    {
        int chunkSize = threadLocal ? TL_CHUNK_SIZE : 1024;
        SizeClassedChunkCache cache = SizeClassedChunkCache.Create(threadLocal, chunkSize, Pool);

        int floor = threadLocal ? ThreadLocalPurgeFloor() : CHUNK_REUSE_QUEUE;

        for (int i = 0; i < floor; i++)
        {
            cache.OfferChunk(ChunkWithoutCapacity());
        }

        // Full chunk: remaining==capacity>0, has capacity.
        // Simulates short-lived buffers: chunk polled, used, all segments return before next purge.
        SizeClassedChunk active = FullChunk();
        cache.OfferChunk(active);

        int cycles = CHUNK_PURGE_THRESHOLD + 2;

        for (int cycle = 0; cycle < cycles; cycle++)
        {
            SizeClassedChunk polled = cache.ForcePurge();
            Assert.True(ReferenceEquals(active, polled), Text($"cycle {cycle}: chunk should be polled, not evicted"));
            Assert.True(0 == polled.PurgeEpoch,
                    Text($"cycle {cycle}: actively-used chunk epoch should be reset"));
            cache.OfferChunk(active);
        }

        Assert.False(MarkedToDeallocate(active));
    }

    // --- shared cache: concurrent scanForCapacity must not livelock ---

    [Fact]
    public void ConcurrentScansTerminateWhenNoCapacity()
    {
        SizeClassedChunkCache cache = SizeClassedChunkCache.Create(false, 1024, Pool);

        // Fill with no-capacity chunks — no scan can find anything
        for (int i = 0; i < 10; i++)
        {
            cache.OfferChunk(ChunkWithoutCapacity());
        }

        int threadCount = 4;
        // CLR: CountdownEvent replaces java.util.concurrent.CountDownLatch and Interlocked.CompareExchange replaces
        // AtomicReference.compareAndSet (https://learn.microsoft.com/dotnet/api/system.threading.countdownevent).
        using CountdownEvent startLatch = new CountdownEvent(1);
        using CountdownEvent doneLatch = new CountdownEvent(threadCount);
        Exception error = null;

        for (int t = 0; t < threadCount; t++)
        {
            new Thread(() =>
            {
                try
                {
                    startLatch.Wait();

                    for (int i = 0; i < 1000; i++)
                    {
                        Assert.Null(cache.PollChunk(256));
                    }
                }
                catch (Exception e)
                {
                    Interlocked.CompareExchange(ref error, e, null);
                }
                finally
                {
                    doneLatch.Signal();
                }
            }).Start();
        }

        startLatch.Signal();
        // With the == sentinel check, threads could livelock here.
        // With >= ordering, all scans terminate promptly.
        bool finished = doneLatch.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.True(finished, "Concurrent scans should terminate within 30 seconds, not livelock");
        Assert.Null(Volatile.Read(ref error));
    }

    // --- free: draining all chunks (thread-local) ---

    [Fact]
    public void PollChunkCannotDrainNoCapChunksThreadLocal()
    {
        ThreadLocalSizeClassedChunkCache cache =
                NewThreadLocalSizeClassedChunkCache(1024);

        cache.OfferChunk(ChunkWithCapacity());
        cache.OfferChunk(ChunkWithoutCapacity());
        cache.OfferChunk(ChunkWithCapacity());
        cache.OfferChunk(ChunkWithoutCapacity());

        int drained = 0;

        while (cache.PollChunk(0) != null)
        {
            drained++;
            if (drained > 100)
            {
                break;
            }
        }

        // pollChunk uses scanForCapacity which skips noCap chunks — they're stuck.
        // This is why free() is needed instead of a pollChunk drain loop.
        Assert.Equal(2, cache.Count);
        Assert.False(MarkedToDeallocate(Chunks(cache)[Head(cache)]));
    }

    [Fact]
    public void FreeDrainsAllChunksIncludingNoCapThreadLocal()
    {
        ThreadLocalSizeClassedChunkCache cache =
                NewThreadLocalSizeClassedChunkCache(1024);

        SizeClassedChunk cap1 = ChunkWithCapacity();
        SizeClassedChunk cap2 = ChunkWithCapacity();
        SizeClassedChunk noCap1 = ChunkWithoutCapacity();
        SizeClassedChunk noCap2 = ChunkWithoutCapacity();

        cache.OfferChunk(cap1);
        cache.OfferChunk(noCap1);
        cache.OfferChunk(cap2);
        cache.OfferChunk(noCap2);

        cache.Dispose();

        Assert.True(cache.IsEmpty);
        Assert.True(MarkedToDeallocate(cap1));
        Assert.True(MarkedToDeallocate(cap2));
        Assert.True(MarkedToDeallocate(noCap1));
        Assert.True(MarkedToDeallocate(noCap2));
    }

    [Fact]
    public void FreeDrainsAllChunksShared()
    {
        SizeClassedChunkCache cache = SizeClassedChunkCache.Create(false, 1024, Pool);

        SizeClassedChunk cap = ChunkWithCapacity();
        SizeClassedChunk noCap = ChunkWithoutCapacity();

        cache.OfferChunk(cap);
        cache.OfferChunk(noCap);

        cache.Dispose();

        Assert.True(cache.IsEmpty);
        Assert.True(MarkedToDeallocate(cap));
        Assert.True(MarkedToDeallocate(noCap));
    }

    [Fact]
    public void ThreadLocalCacheRejectsChunksWhenCapReached()
    {
        int chunkSize = TL_CHUNK_SIZE;
        int maxCached = Math.Max(1,
                THREAD_LOCAL_CACHE_MAX_BYTES / chunkSize);

        ThreadLocalSizeClassedChunkCache cache =
                NewThreadLocalSizeClassedChunkCache(chunkSize);

        // Fill to capacity
        for (int i = 0; i < maxCached; i++)
        {
            Assert.True(cache.OfferChunk(ChunkWithoutCapacity()),
                    Text($"offer should succeed for chunk {i}"));
        }

        // Next offer should be rejected
        SizeClassedChunk excess = ChunkWithoutCapacity();
        bool accepted = cache.OfferChunk(excess);
        Assert.False(accepted, "offer should be rejected when cache is at capacity");
    }

    // CLR: new AdaptivePoolingAllocator.ThreadLocalSizeClassedChunkCache(chunkSize) of the source; the CLR
    // constructor also takes the per-instance settings the source reads from statics.
    private static ThreadLocalSizeClassedChunkCache NewThreadLocalSizeClassedChunkCache(int chunkSize)
    {
        return new(chunkSize, CHUNK_PURGE_POLLS_THREAD_LOCAL, CHUNK_PURGE_THRESHOLD,
            THREAD_LOCAL_CACHE_MAX_BYTES, THREAD_LOCAL_CACHE_MIN_BYTES);
    }

    // CLR: a real chunk standing in for a Mockito stub; `allocated` of its eight 512-byte segments stay claimed.
    private SizeClassedChunk Mock(int allocated)
    {
        var chunk = new SizeClassedChunk(new UnpooledHeapByteBuf(4096, 4096), _registry, 512);
        _chunks.Add(chunk);
        var claims = new Queue<AdaptiveChunkAllocation>();
        _claims.Add(chunk, claims);

        for (int i = 0; i < allocated; i++)
        {
            claims.Enqueue(chunk.TryAllocate(512));
        }

        return chunk;
    }

    // CLR: an external thread's segment return is what gives a chunk capacity again.
    private void ReturnSegment(SizeClassedChunk chunk)
    {
        AdaptiveChunkAllocation claim = _claims[chunk].Dequeue();
        var returner = new Thread(claim.Release);
        returner.Start();
        returner.Join();
    }

    private static readonly FieldInfo RetiredField = typeof(Chunk).GetField("_retired", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo ChunksField = typeof(ThreadLocalSizeClassedChunkCache).GetField("_chunks", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo HeadField = typeof(ThreadLocalSizeClassedChunkCache).GetField("_head", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo TailField = typeof(ThreadLocalSizeClassedChunkCache).GetField("_tail", BindingFlags.Instance | BindingFlags.NonPublic);

    // CLR: verify(chunk).markToDeallocate(); only Chunk.MarkToDeallocate sets the retirement flag.
    private static bool MarkedToDeallocate(Chunk chunk)
    {
        return (int)RetiredField.GetValue(chunk) != 0;
    }

    private static SizeClassedChunk[] Chunks(ThreadLocalSizeClassedChunkCache cache)
    {
        return (SizeClassedChunk[])ChunksField.GetValue(cache);
    }

    private static int Head(ThreadLocalSizeClassedChunkCache cache)
    {
        return (int)HeadField.GetValue(cache);
    }

    private static int Tail(ThreadLocalSizeClassedChunkCache cache)
    {
        return (int)TailField.GetValue(cache);
    }

    // CLR: Java string concatenation renders ints culture-independently.
    private static string Text(FormattableString message)
    {
        return message.ToString(CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        foreach (SizeClassedChunk chunk in _chunks)
        {
            foreach (AdaptiveChunkAllocation claim in _claims[chunk])
            {
                claim.Release();
            }

            chunk.MarkToDeallocate();
        }

        Assert.Equal(0, _registry.TotalCapacity);
    }
}
