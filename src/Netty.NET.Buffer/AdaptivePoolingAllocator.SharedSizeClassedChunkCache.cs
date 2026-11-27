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

internal sealed partial class AdaptivePoolingAllocator
{
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
    internal sealed class SharedSizeClassedChunkCache : SizeClassedChunkCache
    {
        private readonly object _sync = new();
        private readonly int _sharedCacheCapacity;
        private readonly Queue<SizeClassedChunk> _queue = new();
        private long _purgeBudget;
        private readonly List<SizeClassedChunk> _deferredBuffer = new();
        private long _purgeGeneration;
        private long _scanGeneration;
        // CLR: per-instance CHUNK_REUSE_QUEUE, CHUNK_PURGE_POLLS_SHARED and CHUNK_PURGE_THRESHOLD.
        private readonly int _chunkReuseQueue, _chunkPurgeThreshold;
        private readonly long _chunkPurgePollsShared;
        private bool _disposed;

        internal SharedSizeClassedChunkCache(int chunkReuseQueue, long chunkPurgePollsShared, int chunkPurgeThreshold)
        {
            if (chunkReuseQueue < 2 || chunkReuseQueue > 1 << 28)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkReuseQueue));
            }
            ArgumentOutOfRangeException.ThrowIfLessThan(chunkPurgePollsShared, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(chunkPurgeThreshold, 1);
            _chunkReuseQueue = chunkReuseQueue;
            // Must exceed CHUNK_REUSE_QUEUE (the retention floor) to leave room for burst absorption.
            // TODO replace with an unbounded concurrent collection once available.
            // CLR: preserve the JVM MPMC ring's power-of-two capacity while using a bounded BCL FIFO.
            _sharedCacheCapacity = MathUtil.SafeFindNextPositivePowerOfTwo(Math.Max(16, chunkReuseQueue * 2));
            _chunkPurgePollsShared = _purgeBudget = chunkPurgePollsShared;
            _chunkPurgeThreshold = chunkPurgeThreshold;
        }

        public override bool IsEmpty
        {
            get
            {
                lock (_sync)
                {
                    return _queue.Count == 0;
                }
            }
        }
        internal int Count
        {
            get
            {
                lock (_sync)
                {
                    return _queue.Count;
                }
            }
        }

        internal override SizeClassedChunk ForcePurge()
        {
            lock (_sync)
            {
                _purgeBudget = 1;
                return PollChunk(0);
            }
        }

        public override SizeClassedChunk PollChunk(int size)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            lock (_sync)
            {
                if (_disposed)
                {
                    return null;
                }
                long budget = --_purgeBudget;
                if (budget == 0)
                {
                    RunPurgeScan();
                }
                return ScanForCapacity();
            }
        }

        private SizeClassedChunk ScanForCapacity()
        {
            if (!_queue.TryDequeue(out SizeClassedChunk first))
            {
                return null;
            }
            if (first.PurgeEpoch == 0 && first.HasRemainingCapacity)
            {
                return first;
            }
            long generation = _scanGeneration = unchecked(_scanGeneration + 1);
            first.LastScanGeneration = generation;
            if (first.HasRemainingCapacity)
            {
                return ScanForCapacitySlow(generation, first);
            }
            OfferOrDeallocate(first);
            return ScanForCapacitySlow(generation, null);
        }

        private SizeClassedChunk ScanForCapacitySlow(long generation, SizeClassedChunk fallback)
        {
            while (_queue.TryDequeue(out SizeClassedChunk chunk))
            {
                if (chunk.LastScanGeneration >= generation)
                {
                    OfferOrDeallocate(chunk);
                    break;
                }
                if (chunk.HasRemainingCapacity)
                {
                    if (chunk.PurgeEpoch == 0)
                    {
                        if (fallback != null)
                        {
                            OfferOrDeallocate(fallback);
                        }
                        return chunk;
                    }
                    if (fallback == null)
                    {
                        fallback = chunk;
                        continue;
                    }
                }
                chunk.LastScanGeneration = generation;
                OfferOrDeallocate(chunk);
            }
            if (fallback != null)
            {
                fallback.PurgeEpoch = 0;
                return fallback;
            }
            return null;
        }

        private bool OfferOrDeallocate(SizeClassedChunk chunk)
        {
            if (_queue.Count == _sharedCacheCapacity)
            {
                chunk.MarkToDeallocate();
                return false;
            }
            _queue.Enqueue(chunk);
            return true;
        }

        private bool OfferOrDeallocate(SizeClassedChunk chunk, long generation)
        {
            chunk.LastPurgeGeneration = generation;
            return OfferOrDeallocate(chunk);
        }

        private void RunPurgeScan()
        {
            long generation = _purgeGeneration = unchecked(_purgeGeneration + 1);
            int retained = 0;
            List<SizeClassedChunk> deferred = _deferredBuffer;
            while (_queue.TryDequeue(out SizeClassedChunk chunk))
            {
                if (chunk.LastPurgeGeneration == generation)
                {
                    OfferOrDeallocate(chunk, generation);
                    break;
                }
                retained++;
                if (chunk.HasFullCapacity)
                {
                    chunk.PurgeEpoch = unchecked(chunk.PurgeEpoch + 1);
                    if (chunk.PurgeEpoch > _chunkPurgeThreshold)
                    {
                        deferred.Add(chunk);
                        continue;
                    }
                }
                else
                {
                    chunk.PurgeEpoch = 0;
                }
                int remaining = chunk.RemainingCapacity;
                if (remaining > 0)
                {
                    if (!OfferOrDeallocate(chunk, generation))
                    {
                        retained--;
                    }
                }
                else
                {
                    deferred.Add(chunk);
                }
            }
            for (int i = 0, size = deferred.Count; i < size; i++)
            {
                SizeClassedChunk chunk = deferred[i];
                if (chunk.PurgeEpoch > _chunkPurgeThreshold && retained > _chunkReuseQueue)
                {
                    chunk.MarkToDeallocate();
                    retained--;
                }
                else
                {
                    if (!OfferOrDeallocate(chunk, generation))
                    {
                        retained--;
                    }
                }
            }
            deferred.Clear();
            _purgeBudget = _chunkPurgePollsShared;
        }

        public override bool OfferChunk(Chunk chunk)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            lock (_sync)
            {
                if (_disposed || _queue.Count == _sharedCacheCapacity)
                {
                    return false;
                }
                _queue.Enqueue((SizeClassedChunk)chunk);
                return true;
            }
        }

        public override void Dispose()
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            lock (_sync)
            {
                _disposed = true;
                while (_queue.TryDequeue(out SizeClassedChunk chunk))
                {
                    chunk.MarkToDeallocate();
                }
            }
        }
    }
}
