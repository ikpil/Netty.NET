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

namespace Netty.NET.Buffer;

internal sealed partial class AdaptivePoolingAllocator
{
    // CLR: the source's free() is IDisposable.Dispose().
    internal interface IChunkCache : IDisposable
    {
        Chunk PollChunk(int size);

        bool OfferChunk(Chunk chunk);

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
    internal abstract class SizeClassedChunkCache : IChunkCache
    {
        // CLR: the allocator supplies its per-instance purge and retention settings, which replace
        // the source's CHUNK_* and THREAD_LOCAL_CACHE_* statics.
        internal static SizeClassedChunkCache Create(bool isThreadLocal, int chunkSize, AdaptivePoolingAllocator allocator)
        {
            return isThreadLocal ? new ThreadLocalSizeClassedChunkCache(chunkSize, allocator._chunkPurgePollsThreadLocal,
                    allocator._chunkPurgeThreshold, allocator._threadLocalCacheMaxBytes, allocator._threadLocalCacheMinBytes) :
                    new SharedSizeClassedChunkCache(allocator._chunkReuseQueue, allocator._chunkPurgePollsShared,
                    allocator._chunkPurgeThreshold);
        }

        public abstract SizeClassedChunk PollChunk(int size);

        Chunk IChunkCache.PollChunk(int size)
        {
            return PollChunk(size);
        }

        public abstract bool OfferChunk(Chunk chunk);

        public abstract void Dispose();

        public abstract bool IsEmpty { get; }

        // Visible for testing: triggers a purge scan bypassing the budget counter.
        internal abstract SizeClassedChunk ForcePurge();
    }
}
