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
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer;

internal interface IAdaptiveChunkCache : IDisposable
{
    AdaptiveChunk PollChunk(int size);
    bool OfferChunk(AdaptiveChunk chunk);
    bool IsEmpty { get; }
}

    // Cached chunks are detached from magazines: no readInitInto can happen, so segment count
    // can only grow (external releaseSegment returns) and never shrink. Once a chunk reaches
    // full capacity (hasFullCapacity), it stays idle while in the cache.
    //
    // Epoch-based aging invariants (both caches):
    //
    // 1. CLASSIFICATION: purge scans all chunks. Idle (hasFullCapacity) → epoch++.
    //    Non-idle → epoch = 0. Only idle chunks can accumulate epoch.
    //
    // 2. EVICTION: idle chunks with epoch > CHUNK_PURGE_THRESHOLD are evicted (markToDeallocate).
    //    Eviction is immediate — all segments are in, no outstanding references.
    //    Non-idle chunks are never evicted (deallocation would be deferred, not immediate).
    //    A retention floor prevents over-eviction: CHUNK_REUSE_QUEUE for the shared cache,
    //    purgeRetentionFloor (from THREAD_LOCAL_CACHE_MIN_BYTES) for the thread-local cache.
    //
    // 3. SCAN RESET: scanForCapacity resets purgeEpoch = 0 on the chunk it picks. The scan
    //    knows the chunk is being used. The chunk gets allocated from, becomes non-idle, and
    //    the next purge resets its epoch anyway (non-idle → 0). The scan reset covers the case
    //    where all segments return before the next purge (short-lived buffers).
    //
    // 4. CONVERGENCE: idle chunks that are never picked by scan age undisturbed across
    //    purge cycles. After CHUNK_PURGE_THRESHOLD + 1 consecutive cycles of being idle and
    //    unpolled, they are evicted. Chunks picked by scan get epoch reset — aging interrupted.
    //    Thread-local: partition orders [epoch=0 | 0<epoch<T | epoch>=T | noCap]. Scan takes
    //    from head (epoch=0 first). Chunks with epoch>=threshold are placed at the back of
    //    the hasCap zone so scan doesn't reach them — they age to threshold+1 and get evicted.
    //    Shared: approximate, converges over multiple cycles (FIFO queue ordering,
    //    LRU preference in scan, retained counter in purge).
/**
 * MPMC queue cache for shared (cross-thread) chunk reuse.
 *
 * <p><b>scanForCapacity</b> — LRU preference with fallback:
 * <pre>
 *   fast path: head chunk has purgeEpoch == 0 and capacity → return O(1)
 *
 *   slow path: scan for epoch=0 chunk, hold first idle (epoch &gt; 0) as fallback
 *     queue: [E&gt;0, E&gt;0, E=0, E&gt;0, ...]
 *             skip   skip  ↑ return (put fallback back)
 *
 *   no epoch=0 found → use fallback, reset its epoch to 0
 * </pre>
 *
 * <p>The LRU preference creates a natural separation: recently-used chunks (epoch=0,
 * returned via {@link #offerChunk} after magazine use) cycle at the front. Idle chunks
 * (epoch &gt; 0, aged by purge) are scanned past but never returned — they age undisturbed.
 * When no recently-used chunks exist, idle ones are reused (fallback) rather than
 * allocating new chunks.
 *
 * <p>All re-offered chunks are stamped with {@code lastScanGeneration} for cycle detection.
 * The {@code >=} check terminates the scan when encountering any chunk already processed
 * by this or a later scan, preventing livelock under concurrent access.
 *
 * <p><b>runPurgeScan</b> (every {@link #CHUNK_PURGE_POLLS_SHARED} polls):
 * drains the queue, ages full chunks (epoch++), resets non-full (epoch=0).
 * Non-candidate capacity chunks are re-offered inline. Eviction candidates (full,
 * epoch past threshold) and no-capacity chunks are deferred to a buffer. After the drain,
 * the buffer is walked with the known total: candidates are evicted while above
 * {@link #CHUNK_REUSE_QUEUE}, remainder re-offered. No selection — that is
 * {@code scanForCapacity}'s job (called after purge via {@code pollChunk}).
 */
// CLR: the cache gate serializes BCL FIFO operations, selection and purge. Allocation
// is still striped across magazine locks; external segment returns remain concurrent.
internal sealed class AdaptiveSharedChunkCache : IAdaptiveChunkCache
{
    private readonly object _sync = new();
    private readonly Queue<AdaptiveSizeClassedChunk> _queue = new();
    private readonly List<AdaptiveSizeClassedChunk> _deferred = new();
    private readonly int _capacity, _retentionFloor, _purgeThreshold;
    private readonly long _purgePolls;
    private long _budget, _purgeGeneration, _scanGeneration;
    private bool _disposed;

    internal AdaptiveSharedChunkCache(int retentionFloor, long purgePolls, int purgeThreshold)
    {
        if (retentionFloor < 2 || retentionFloor > 1 << 28) throw new ArgumentOutOfRangeException(nameof(retentionFloor));
        ArgumentOutOfRangeException.ThrowIfLessThan(purgePolls, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(purgeThreshold, 1);
        _retentionFloor = retentionFloor;
        // Must exceed CHUNK_REUSE_QUEUE (the retention floor) to leave room for burst absorption.
        // TODO replace with an unbounded concurrent collection once available.
        // CLR: preserve the JVM MPMC ring's power-of-two capacity while using a bounded BCL FIFO.
        _capacity = MathUtil.SafeFindNextPositivePowerOfTwo(Math.Max(16, retentionFloor * 2));
        _purgePolls = _budget = purgePolls;
        _purgeThreshold = purgeThreshold;
    }

    public bool IsEmpty { get { lock (_sync) return _queue.Count == 0; } }
    internal int Count { get { lock (_sync) return _queue.Count; } }
    public AdaptiveChunk PollChunk(int size)
    {
        lock (_sync)
        {
            if (_disposed) return null;
            if (--_budget == 0) RunPurgeScan();
            return ScanForCapacity();
        }
    }

    // Visible for testing: triggers a purge scan bypassing the budget counter.
    internal AdaptiveChunk ForcePurge()
    {
        lock (_sync) { _budget = 1; return PollChunk(0); }
    }

    private AdaptiveSizeClassedChunk ScanForCapacity()
    {
        if (!_queue.TryDequeue(out var first)) return null;
        if (first.PurgeEpoch == 0 && first.HasRemainingCapacity) return first;
        long generation = _scanGeneration = unchecked(_scanGeneration + 1);
        first.LastScanGeneration = generation;
        AdaptiveSizeClassedChunk fallback = null;
        if (first.HasRemainingCapacity) fallback = first;
        else OfferOrDeallocate(first);
        while (_queue.TryDequeue(out var chunk))
        {
            if (chunk.LastScanGeneration >= generation) { OfferOrDeallocate(chunk); break; }
            if (chunk.HasRemainingCapacity)
            {
                if (chunk.PurgeEpoch == 0)
                {
                    if (fallback != null) OfferOrDeallocate(fallback);
                    return chunk;
                }
                if (fallback == null) { fallback = chunk; continue; }
            }
            chunk.LastScanGeneration = generation;
            OfferOrDeallocate(chunk);
        }
        if (fallback != null) fallback.PurgeEpoch = 0;
        return fallback;
    }

    private bool OfferOrDeallocate(AdaptiveSizeClassedChunk chunk)
    {
        if (_queue.Count == _capacity) { chunk.Dispose(); return false; }
        _queue.Enqueue(chunk);
        return true;
    }
    private bool OfferOrDeallocate(AdaptiveSizeClassedChunk chunk, long generation)
    { chunk.LastPurgeGeneration = generation; return OfferOrDeallocate(chunk); }

    private void RunPurgeScan()
    {
        long generation = _purgeGeneration = unchecked(_purgeGeneration + 1);
        int retained = 0;
        while (_queue.TryDequeue(out var chunk))
        {
            if (chunk.LastPurgeGeneration == generation) { OfferOrDeallocate(chunk, generation); break; }
            retained++;
            if (chunk.HasFullCapacity)
            {
                chunk.PurgeEpoch = unchecked(chunk.PurgeEpoch + 1);
                if (chunk.PurgeEpoch > _purgeThreshold) { _deferred.Add(chunk); continue; }
            }
            else chunk.PurgeEpoch = 0;
            if (chunk.RemainingCapacity > 0)
            {
                if (!OfferOrDeallocate(chunk, generation)) retained--;
            }
            else _deferred.Add(chunk);
        }
        foreach (var chunk in _deferred)
        {
            if (chunk.PurgeEpoch > _purgeThreshold && retained > _retentionFloor)
            { chunk.Dispose(); retained--; }
            else if (!OfferOrDeallocate(chunk, generation)) retained--;
        }
        _deferred.Clear();
        _budget = _purgePolls;
    }

    public bool OfferChunk(AdaptiveChunk chunk)
    {
        lock (_sync)
        {
            if (_disposed || _queue.Count == _capacity) return false;
            _queue.Enqueue((AdaptiveSizeClassedChunk)chunk);
            return true;
        }
    }
    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            while (_queue.TryDequeue(out var chunk)) chunk.Dispose();
        }
    }
}

// CLR: a locked sorted multimap replaces ConcurrentSkipListIntObjMultimap. Equal
// keys put the newest entry first, as the original base-list insertion does.
internal sealed class AdaptiveBuddyChunkCache : IAdaptiveChunkCache
{
    private readonly object _sync = new();
    private readonly SortedDictionary<int, List<AdaptiveChunk>> _chunks = new();
    private readonly int _retentionFloor;
    private int _count;
    private bool _disposed;
    internal AdaptiveBuddyChunkCache(int retentionFloor) => _retentionFloor = retentionFloor;
    public bool IsEmpty { get { lock (_sync) return _count == 0; } }
    internal int Count { get { lock (_sync) return _count; } }
    private void Put(int key, AdaptiveChunk chunk)
    {
        if (!_chunks.TryGetValue(key, out var list)) _chunks.Add(key, list = new());
        list.Insert(0, chunk); _count++;
    }
    private void Remove(int key, AdaptiveChunk chunk)
    {
        List<AdaptiveChunk> list = _chunks[key];
        list.Remove(chunk); _count--;
        if (list.Count == 0) _chunks.Remove(key);
    }
    private List<(int Key, AdaptiveChunk Chunk)> Entries()
    {
        var entries = new List<(int, AdaptiveChunk)>(_count);
        foreach (var pair in _chunks) foreach (var chunk in pair.Value) entries.Add((pair.Key, chunk));
        return entries;
    }
    public AdaptiveChunk PollChunk(int size)
    {
        lock (_sync)
        {
            if (_disposed) return null;
            AdaptiveChunk ceiling = null; int key = 0;
            foreach (var pair in _chunks)
                if (pair.Key >= size) { key = pair.Key; ceiling = pair.Value[0]; break; }
            if (ceiling != null)
            {
                Remove(key, ceiling);
                if (ceiling.HasUnprocessedFreelistEntries) ceiling.ProcessFreelistEntries();
                return ceiling;
            }
            AdaptiveChunk best = null; int bestRemaining = 0;
            foreach (var entry in Entries())
            {
                AdaptiveChunk chunk = entry.Chunk;
                if (!chunk.HasUnprocessedFreelistEntries) continue;
                Remove(entry.Key, chunk);
                chunk.ProcessFreelistEntries();
                int remaining = chunk.RemainingCapacity;
                if (remaining >= size && (best == null || remaining > bestRemaining))
                {
                    if (best != null) Put(bestRemaining, best);
                    best = chunk; bestRemaining = remaining;
                }
                else Put(remaining, chunk);
            }
            return best;
        }
    }
    public bool OfferChunk(AdaptiveChunk chunk)
    {
        lock (_sync)
        {
            if (_disposed) return false;
            Put(chunk.RemainingCapacity, chunk);
            while (_count > _retentionFloor)
            {
                AdaptiveChunk idle = null; int key = 0;
                foreach (var pair in _chunks)
                {
                    foreach (var candidate in pair.Value)
                        if (candidate.OwnerReferenceCount == 1) { idle = candidate; key = pair.Key; break; }
                    if (idle != null) break;
                }
                if (idle == null) break;
                Remove(key, idle); idle.Dispose();
            }
            return true;
        }
    }
    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            foreach (var entry in Entries()) entry.Chunk.Dispose();
            _chunks.Clear(); _count = 0;
        }
    }
}
