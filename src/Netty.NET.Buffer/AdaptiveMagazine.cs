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

internal sealed class AdaptiveMagazine : IDisposable
{
    // CLR: an identity token replaces the Java sentinel chunk; it owns no raw storage.
    // Constructor only used by the MAGAZINE_FREED sentinel.
    private static readonly object MagazineFreed = new();
    private readonly AdaptiveMagazineGroup _group;
    // We only need the StampedLock if this Magazine will be shared across threads.
    // CLR: shared magazines use a Monitor; local magazines rely on the pool lifetime gate.
    internal object AllocationLock { get; } = new();
    private readonly bool _threadLocal;
    internal bool IsThreadLocal => _threadLocal;
    private AdaptiveChunk _current;
    // updated via NEXT_IN_LINE
    private object _nextInLine;
    internal AdaptiveMagazine(AdaptiveMagazineGroup group, bool threadLocal = false)
    { _group = group; _threadLocal = threadLocal; }
    internal bool OfferToQueue(AdaptiveChunk chunk) => _group.OfferChunk(chunk);

    internal AdaptiveChunkAllocation TryAllocate(int size, int maxCapacity)
    {
        using var events = BufferAllocatorEventSource.Log.BeginOperation();
        if (_threadLocal)
        {
            // This magazine is not shared across threads, just allocate directly.
            return Allocate(size, maxCapacity);
        }
        // Try to retrieve the lock and if successful allocate.
        if (!Monitor.TryEnter(AllocationLock)) return AllocateWithoutLock(size, maxCapacity);
        try { return Allocate(size, maxCapacity); }
        finally { Monitor.Exit(AllocationLock); }
    }

    private AdaptiveChunk TakeNext(out bool freed)
    {
        object next = Interlocked.Exchange(ref _nextInLine, null);
        freed = ReferenceEquals(next, MagazineFreed);
        if (freed)
        {
            // Allocation raced with a stripe-resize that freed this magazine.
            RestoreMagazineFreed();
            return null;
        }
        return (AdaptiveChunk)next;
    }

    private AdaptiveChunkAllocation AllocateWithoutLock(int size, int maxCapacity)
    {
        AdaptiveChunk current = TakeNext(out bool freed);
        if (freed) return null;
        if (current == null)
        {
            current = _group.PollChunk(size);
            if (current == null) return null;
            current.AttachToMagazine(this);
        }
        AdaptiveChunkAllocation allocation = null;
        try
        {
            int remaining = current.RemainingCapacity;
            int starting = _group.FastCapacity(size, maxCapacity);
            /* never update stats as we don't hold the magazine lock */
            if (remaining >= size) allocation = current.TryAllocate(Math.Min(remaining, starting));
            return allocation;
        }
        catch { allocation?.Release(); throw; }
        finally
        {
            // CLR: restore ownership even if reservation initialization throws.
            if (current.RemainingCapacity >= 256) TransferToNextInLineOrRelease(current);
            else current.ReleaseFromMagazine();
        }
    }

    private AdaptiveChunkAllocation Allocate(int size, int maxCapacity)
    {
        int starting = _group.FastCapacity(size, maxCapacity);
        AdaptiveChunk current = _current;
        if (current != null)
        {
            AdaptiveChunkAllocation allocation = current.TryAllocate(starting);
            int remaining = current.RemainingCapacity;
            if (allocation == null && remaining > 0)
            {
                _current = null;
                TransferToNextInLineOrRelease(current);
            }
            else if (remaining == 0)
            {
                _current = null;
                current.ReleaseFromMagazine();
            }
            if (allocation != null) return allocation;
        }

        // The fast-path for allocations did not work.
        //
        // Try to fetch the next "Magazine local" Chunk first, if this fails because we don't have a
        // next-in-line chunk available, we will poll our centralQueue.
        // If this fails as well we will just allocate a new Chunk.
        //
        // In any case we will store the Chunk as the current so it will be used again for the next allocation and
        // thus be "reserved" by this Magazine for exclusive usage.
        current = TakeNext(out bool freed);
        if (freed) return null;
        if (current != null)
        {
            bool kept = false;
            try
            {
                int remaining = current.RemainingCapacity;
                AdaptiveChunkAllocation allocation;
                if (remaining > starting && (allocation = current.TryAllocate(starting)) != null)
                {
                    // We have a Chunk that has some space left.
                    _current = current;
                    kept = true;
                    return allocation;
                }
                if (remaining >= size)
                {
                    // At this point we know that this will be the last time curr will be used, so directly set it
                    // to null and release it once we are done.
                    return current.TryAllocate(remaining);
                }
            }
            finally
            {
                // Release in a finally block so even if readInitInto(...) would throw we would still correctly
                // release the current chunk before null it out.
                if (!kept) current.ReleaseFromMagazine();
            }
        }

        // Now try to poll from the central queue first
        current = _group.PollChunk(size);
        if (current == null) current = _group.NewChunk(size, this);
        else
        {
            current.AttachToMagazine(this);
            int remaining = current.RemainingCapacity;
            if (remaining == 0 || remaining < size)
            {
                // Check if we either retain the chunk in the nextInLine cache or releasing it.
                if (remaining < 256) current.ReleaseFromMagazine();
                else
                {
                    // See if it makes sense to transfer the Chunk to the nextInLine cache for later usage.
                    // This method will release curr if this is not the case
                    TransferToNextInLineOrRelease(current);
                }
                current = _group.NewChunk(size, this);
            }
        }

        _current = current;
        try
        {
            int remaining = current.RemainingCapacity;
            if (remaining > starting)
            {
                AdaptiveChunkAllocation allocation = current.TryAllocate(starting);
                current = null;
                return allocation;
            }
            return current.TryAllocate(remaining);
        }
        finally
        {
            if (current != null)
            {
                // Release in a finally block so even if readInitInto(...) would throw we would still correctly
                // release the current chunk before null it out.
                current.ReleaseFromMagazine();
                _current = null;
            }
        }
    }

    private void RestoreMagazineFreed()
    {
        object next = Interlocked.Exchange(ref _nextInLine, MagazineFreed);
        if (next is AdaptiveChunk chunk)
        {
            // A chunk snuck in through a race. Release it after restoring MAGAZINE_FREED state.
            chunk.ReleaseFromMagazine();
        }
    }
    private void TransferToNextInLineOrRelease(AdaptiveChunk chunk)
    {
        if (Interlocked.CompareExchange(ref _nextInLine, chunk, null) == null) return;
        object next = Volatile.Read(ref _nextInLine);
        if (next is AdaptiveChunk candidate && chunk.RemainingCapacity > candidate.RemainingCapacity &&
            ReferenceEquals(Interlocked.CompareExchange(ref _nextInLine, chunk, candidate), candidate))
        { candidate.ReleaseFromMagazine(); return; }
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
        lock (AllocationLock)
        {
            if (_current != null) { _current.ReleaseFromMagazine(); _current = null; }
        }
    }
}
