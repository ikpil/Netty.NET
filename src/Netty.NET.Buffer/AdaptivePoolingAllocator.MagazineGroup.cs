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
using System.Threading;

namespace Netty.NET.Buffer;

internal sealed partial class AdaptivePoolingAllocator
{
    internal sealed class MagazineGroup : IDisposable
    {
        private readonly AdaptivePoolingAllocator _allocator;
        private readonly IChunkAllocator _chunkAllocator;
        private readonly IChunkManagementStrategy _chunkManagementStrategy;
        private readonly IChunkCache _chunkCache;
        // CLR: a Monitor replaces the StampedLock; thread-local groups have none, as in the source.
        private readonly object _magazineExpandLock;
        private readonly Magazine _threadLocalMagazine;
        // CLR: no ownerThread field; a thread-local SizeClassedChunk records and hands over its
        // physical owner itself (see SizeClassedChunk.MarkToDeallocate).
        private Magazine[] _magazines;
        private bool _freed;
        internal AdaptivePoolingAllocator Allocator => _allocator;
        internal IChunkAllocator ChunkAllocator => _chunkAllocator;
        internal IChunkManagementStrategy ChunkManagementStrategy => _chunkManagementStrategy;
        internal IChunkCache ChunkCache => _chunkCache;
        internal Magazine[] Magazines => Volatile.Read(ref _magazines);
        internal bool Freed => Volatile.Read(ref _freed);

        internal MagazineGroup(AdaptivePoolingAllocator allocator,
                      IChunkAllocator chunkAllocator,
                      IChunkManagementStrategy chunkManagementStrategy,
                      bool isThreadLocal)
        {
            _allocator = allocator;
            _chunkAllocator = chunkAllocator;
            _chunkManagementStrategy = chunkManagementStrategy;
            _chunkCache = chunkManagementStrategy.CreateChunkCache(isThreadLocal);
            if (isThreadLocal)
            {
                _magazineExpandLock = null;
                _threadLocalMagazine = new Magazine(this, false, chunkManagementStrategy.CreateController(this));
                _magazines = [];
            }
            else
            {
                _magazineExpandLock = new object();
                _threadLocalMagazine = null;
                Magazine[] mags = new Magazine[INITIAL_MAGAZINES];
                for (int i = 0; i < mags.Length; i++)
                {
                    mags[i] = new Magazine(this, true, chunkManagementStrategy.CreateController(this));
                }
                _magazines = mags;
            }
        }

        // CLR: returns the reservation (or null) instead of initializing a recycled buffer.
        internal AdaptiveChunkAllocation Allocate(int size, int maxCapacity, bool reallocate)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            // Path for thread-local allocation.
            Magazine tlMag = _threadLocalMagazine;
            if (tlMag != null)
            {
                return tlMag.TryAllocate(size, maxCapacity, reallocate);
            }

            // Path for concurrent allocation.
            int threadId = Environment.CurrentManagedThreadId;
            Magazine[] mags;
            int expansions = 0;
            do
            {
                mags = Magazines;
                int mask = mags.Length - 1;
                int index = threadId & mask;
                for (int i = 0, m = mags.Length << 1; i < m; i++)
                {
                    Magazine mag = mags[(index + i) & mask];
                    AdaptiveChunkAllocation allocation = mag.TryAllocate(size, maxCapacity, reallocate);
                    if (allocation != null)
                    {
                        // Was able to allocate.
                        return allocation;
                    }
                }
                expansions++;
            } while (expansions <= EXPANSION_ATTEMPTS && TryExpandMagazines(mags.Length));

            // The magazines failed us; contention too high and we don't want to spend more effort expanding the array.
            // CLR: no unused outer recycler object was claimed on this reservation path.
            return null;
        }

        private bool TryExpandMagazines(int currentLength)
        {
            if (currentLength >= _allocator._maxStripes)
            {
                return true;
            }
            Magazine[] mags;
            // CLR: Monitor.TryEnter replaces StampedLock.tryWriteLock.
            if (Monitor.TryEnter(_magazineExpandLock))
            {
                try
                {
                    mags = Magazines;
                    if (mags.Length >= _allocator._maxStripes || mags.Length > currentLength || Freed)
                    {
                        return true;
                    }
                    Magazine[] expanded = new Magazine[mags.Length * 2];
                    for (int i = 0, l = expanded.Length; i < l; i++)
                    {
                        expanded[i] = new Magazine(this, true, _chunkManagementStrategy.CreateController(this));
                    }
                    Volatile.Write(ref _magazines, expanded);
                }
                finally
                {
                    Monitor.Exit(_magazineExpandLock);
                }
                foreach (Magazine magazine in mags)
                {
                    magazine.Dispose();
                }
            }
            return true;
        }

        internal Chunk PollChunk(int size)
        {
            return _chunkCache.PollChunk(size);
        }

        internal bool OfferChunk(Chunk chunk)
        {
            if (Freed)
            {
                return false;
            }

            if (chunk.HasUnprocessedFreelistEntries)
            {
                chunk.ProcessFreelistEntries();
            }
            bool isAdded = _chunkCache.OfferChunk(chunk);

            if (Freed && isAdded)
            {
                // Help to free the reuse queue.
                FreeChunkReuseQueue();
            }
            return isAdded;
        }

        public void Dispose()
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            Volatile.Write(ref _freed, true);
            if (_threadLocalMagazine != null)
            {
                _threadLocalMagazine.Dispose();
            }
            else
            {
                lock (_magazineExpandLock)
                {
                    Magazine[] mags = Magazines;
                    foreach (Magazine magazine in mags)
                    {
                        magazine.Dispose();
                    }
                }
            }
            FreeChunkReuseQueue();
        }

        // CLR: no owner thread is handed over here; SizeClassedChunk.MarkToDeallocate clears its own
        // physical owner under its local-return lock.
        private void FreeChunkReuseQueue()
        {
            _chunkCache.Dispose();
        }
    }
}
