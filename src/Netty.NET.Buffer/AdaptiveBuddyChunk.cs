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
using System.Collections.Concurrent;

namespace Netty.NET.Buffer;

internal sealed class AdaptiveBuddyChunk : AdaptiveChunk
{
    // CLR: ConcurrentQueue<int> replaces the JVM MPSC queue. Live reservations bound
    // its size; only the allocating magazine mutates the buddy tree.
    private readonly ConcurrentQueue<int> _freeList = new();
    private readonly AdaptiveBuddyTree _tree;
    private readonly int _freeListCapacity;

    internal AdaptiveBuddyChunk(ByteBuf storage, AdaptiveChunkRegistry registry) : base(storage, registry, true)
    {
        _tree = new AdaptiveBuddyTree(Capacity);
        _freeListCapacity = Capacity / AdaptiveBuddyTree.MinBuddySize;
        // At most half of tree (all leaf nodes) can be freed.
        Register();
    }

    internal override AdaptiveChunkAllocation TryAllocate(int startingCapacity)
    {
        ThrowIfRetired();
        ProcessFreelistEntries();
        int startIndex = _tree.TryAllocate(startingCapacity);
        if (startIndex == -1) return null;
        Retain();
        try
        {
            var allocation = new AdaptiveChunkAllocation(this, Storage.AsMemory(startIndex, startingCapacity), startIndex);
            AllocatedBytes += startingCapacity;
            return allocation;
        }
        catch
        {
            _tree.Free(startIndex, startingCapacity);
            // If chunk is not null we know that buf.init(...) failed and so we need to manually release
            // the chunk again as we retained it before calling buf.init(...).
            Release();
            throw;
        }
    }

    internal override void ReleaseSegment(int startingIndex, int size)
    {
        _freeList.Enqueue(AdaptiveBuddyTree.PackFreeSegment(startingIndex, size));
        Release();
    }

    internal override int RemainingCapacity
    {
        get
        {
            int pending = 0;
            // A concurrent snapshot, like weakPeekReduce, is not a transaction with allocation.
            foreach (int packed in _freeList) pending += AdaptiveBuddyTree.UnpackSize(packed);
            return base.RemainingCapacity + pending;
        }
    }

    internal override bool HasUnprocessedFreelistEntries => !_freeList.IsEmpty;
    internal override void ProcessFreelistEntries()
    {
        for (int i = 0; i < _freeListCapacity && _freeList.TryDequeue(out int packed); i++)
        {
            // Called by allocating thread when draining freeList.
            int size = AdaptiveBuddyTree.UnpackSize(packed);
            _tree.Free(AdaptiveBuddyTree.UnpackOffset(packed), size);
            AllocatedBytes -= size;
        }
    }

    public override string ToString()
        => $"BuddyChunk[capacity: {Capacity}, remaining: {Capacity - AllocatedBytes}, free list: {_freeList.Count}]";
}
