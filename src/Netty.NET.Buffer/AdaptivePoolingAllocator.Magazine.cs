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
    // CLR: no AdaptiveRecycler. Fresh AdaptiveByteBuf wrappers replace the recycler queues and
    // newBuffer(); recycling a released CLR object could resurrect stale references.
    internal sealed class Magazine : IDisposable
    {
        // CLR: an identity token replaces the Java sentinel chunk; it owns no raw storage.
        // Constructor only used by the MAGAZINE_FREED sentinel.
        private static readonly object MAGAZINE_FREED = new();

        private Chunk _current;
        // updated via NEXT_IN_LINE
        private object _nextInLine;
        private readonly MagazineGroup _group;
        private readonly IChunkController _chunkController;
        // CLR: shared magazines use a Monitor; local magazines rely on the pool lifetime gate.
        internal object AllocationLock { get; }
        internal IChunkController ChunkController => _chunkController;

        internal Magazine(MagazineGroup group, bool shareable, IChunkController chunkController)
        {
            _group = group;
            _chunkController = chunkController;

            if (shareable)
            {
                // We only need the StampedLock if this Magazine will be shared across threads.
                AllocationLock = new object();
            }
            else
            {
                AllocationLock = null;
            }
        }

        // CLR: returns the reservation (or null) instead of initializing a recycled buffer.
        internal AdaptiveChunkAllocation TryAllocate(int size, int maxCapacity, bool reallocate)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            if (AllocationLock == null)
            {
                // This magazine is not shared across threads, just allocate directly.
                return Allocate(size, maxCapacity, reallocate);
            }

            // Try to retrieve the lock and if successful allocate.
            if (Monitor.TryEnter(AllocationLock))
            {
                try
                {
                    return Allocate(size, maxCapacity, reallocate);
                }
                finally
                {
                    Monitor.Exit(AllocationLock);
                }
            }
            return AllocateWithoutLock(size, maxCapacity);
        }

        private AdaptiveChunkAllocation AllocateWithoutLock(int size, int maxCapacity)
        {
            object next = Interlocked.Exchange(ref _nextInLine, null);
            if (ReferenceEquals(next, MAGAZINE_FREED))
            {
                // Allocation raced with a stripe-resize that freed this magazine.
                RestoreMagazineFreed();
                return null;
            }
            Chunk curr = (Chunk)next;
            if (curr == null)
            {
                curr = _group.PollChunk(size);
                if (curr == null)
                {
                    return null;
                }
                curr.AttachToMagazine(this);
            }
            AdaptiveChunkAllocation allocated = null;
            try
            {
                int remainingCapacity = curr.RemainingCapacity;
                int startingCapacity = _chunkController.ComputeBufferCapacity(
                        size, maxCapacity, true /* never update stats as we don't hold the magazine lock */);
                if (remainingCapacity >= size)
                {
                    allocated = curr.TryAllocate(Math.Min(remainingCapacity, startingCapacity));
                }
                return allocated;
            }
            catch
            {
                allocated?.Release();
                throw;
            }
            finally
            {
                // CLR: restore ownership even if reservation initialization throws.
                if (curr.RemainingCapacity >= RETIRE_CAPACITY)
                {
                    TransferToNextInLineOrRelease(curr);
                }
                else
                {
                    curr.ReleaseFromMagazine();
                }
            }
        }

        private AdaptiveChunkAllocation Allocate(int size, int maxCapacity, bool reallocate)
        {
            int startingCapacity = _chunkController.ComputeBufferCapacity(size, maxCapacity, reallocate);
            Chunk curr = _current;
            if (curr != null)
            {
                AdaptiveChunkAllocation success = curr.TryAllocate(startingCapacity);
                int remainingCapacity = curr.RemainingCapacity;
                if (success == null && remainingCapacity > 0)
                {
                    _current = null;
                    TransferToNextInLineOrRelease(curr);
                }
                else if (remainingCapacity == 0)
                {
                    _current = null;
                    curr.ReleaseFromMagazine();
                }
                if (success != null)
                {
                    return success;
                }
            }

            // The fast-path for allocations did not work.
            //
            // Try to fetch the next "Magazine local" Chunk first, if this fails because we don't have a
            // next-in-line chunk available, we will poll our centralQueue.
            // If this fails as well we will just allocate a new Chunk.
            //
            // In any case we will store the Chunk as the current so it will be used again for the next allocation and
            // thus be "reserved" by this Magazine for exclusive usage.
            object next = Interlocked.Exchange(ref _nextInLine, null);
            if (next != null)
            {
                if (ReferenceEquals(next, MAGAZINE_FREED))
                {
                    // Allocation raced with a stripe-resize that freed this magazine.
                    RestoreMagazineFreed();
                    return null;
                }
                curr = (Chunk)next;

                bool kept = false;
                try
                {
                    int remainingCapacity = curr.RemainingCapacity;
                    AdaptiveChunkAllocation allocated;
                    if (remainingCapacity > startingCapacity &&
                            (allocated = curr.TryAllocate(startingCapacity)) != null)
                    {
                        // We have a Chunk that has some space left.
                        _current = curr;
                        kept = true;
                        return allocated;
                    }

                    if (remainingCapacity >= size)
                    {
                        // At this point we know that this will be the last time curr will be used, so directly set it
                        // to null and release it once we are done.
                        return curr.TryAllocate(remainingCapacity);
                    }
                }
                finally
                {
                    // Release in a finally block so even if readInitInto(...) would throw we would still correctly
                    // release the current chunk before null it out.
                    if (!kept)
                    {
                        curr.ReleaseFromMagazine();
                    }
                }
            }

            // Now try to poll from the central queue first
            curr = _group.PollChunk(size);
            if (curr == null)
            {
                curr = _chunkController.NewChunkAllocation(size, this);
            }
            else
            {
                curr.AttachToMagazine(this);

                int remainingCapacity = curr.RemainingCapacity;
                if (remainingCapacity == 0 || remainingCapacity < size)
                {
                    // Check if we either retain the chunk in the nextInLine cache or releasing it.
                    if (remainingCapacity < RETIRE_CAPACITY)
                    {
                        curr.ReleaseFromMagazine();
                    }
                    else
                    {
                        // See if it makes sense to transfer the Chunk to the nextInLine cache for later usage.
                        // This method will release curr if this is not the case
                        TransferToNextInLineOrRelease(curr);
                    }
                    curr = _chunkController.NewChunkAllocation(size, this);
                }
            }

            _current = curr;
            try
            {
                int remainingCapacity = curr.RemainingCapacity;
                if (remainingCapacity > startingCapacity)
                {
                    AdaptiveChunkAllocation success = curr.TryAllocate(startingCapacity);
                    curr = null;
                    return success;
                }
                return curr.TryAllocate(remainingCapacity);
            }
            finally
            {
                if (curr != null)
                {
                    // Release in a finally block so even if readInitInto(...) would throw we would still correctly
                    // release the current chunk before null it out.
                    curr.ReleaseFromMagazine();
                    _current = null;
                }
            }
        }

        private void RestoreMagazineFreed()
        {
            object next = Interlocked.Exchange(ref _nextInLine, MAGAZINE_FREED);
            if (next is Chunk chunk)
            {
                // A chunk snuck in through a race. Release it after restoring MAGAZINE_FREED state.
                chunk.ReleaseFromMagazine();
            }
        }

        private void TransferToNextInLineOrRelease(Chunk chunk)
        {
            if (Interlocked.CompareExchange(ref _nextInLine, chunk, null) == null)
            {
                return;
            }

            object next = Volatile.Read(ref _nextInLine);
            if (next is Chunk nextChunk && chunk.RemainingCapacity > nextChunk.RemainingCapacity)
            {
                if (ReferenceEquals(Interlocked.CompareExchange(ref _nextInLine, chunk, nextChunk), nextChunk))
                {
                    nextChunk.ReleaseFromMagazine();
                    return;
                }
            }
            // Next-in-line is occupied. We don't try to add it to the central queue yet as it might still be used
            // by some buffers and so is attached to a Magazine.
            // Once a Chunk is completely released by Chunk.release() it will try to move itself to the queue
            // as last resort.
            // CLR: releaseFromMagazine detaches and offers immediately, as the pinned implementation does.
            chunk.ReleaseFromMagazine();
        }

        public void Dispose()
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            // Release the current Chunk and the next that was stored for later usage.
            RestoreMagazineFreed();
            // CLR: a Monitor replaces the StampedLock write lock; thread-local magazines have none.
            object allocationLock = AllocationLock;
            if (allocationLock != null)
            {
                Monitor.Enter(allocationLock);
            }
            try
            {
                if (_current != null)
                {
                    _current.ReleaseFromMagazine();
                    _current = null;
                }
            }
            finally
            {
                if (allocationLock != null)
                {
                    Monitor.Exit(allocationLock);
                }
            }
        }

        internal bool OfferToQueue(Chunk chunk)
        {
            return _group.OfferChunk(chunk);
        }
    }
}
