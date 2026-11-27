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
using System.Globalization;
using System.Numerics;

namespace Netty.NET.Buffer;

internal sealed partial class AdaptivePoolingAllocator
{
    // CLR: the buddy tree is owned by the allocating magazine, which serializes its operations;
    // cross-thread returns only enqueue packed segments.
    internal sealed class BuddyChunk : Chunk
    {
        internal const int MIN_BUDDY_SIZE = 32768;
        private const byte IS_CLAIMED = 1 << 7;
        private const byte HAS_CLAIMED_CHILDREN = 1 << 6;
        // CLR: the low six bits of the unsigned byte, i.e. ~(IS_CLAIMED | HAS_CLAIMED_CHILDREN).
        private const byte SHIFT_MASK = 0x3F;
        private const int PACK_OFFSET_MASK = 0xFFFF;
        // CLR: Integer.SIZE - Integer.numberOfLeadingZeros(PACK_OFFSET_MASK).
        private const int PACK_SIZE_SHIFT = 16;

        // CLR: ConcurrentQueue<int> replaces the JVM MPSC queue. Live reservations bound
        // its size; only the allocating magazine mutates the buddy tree.
        private readonly ConcurrentQueue<int> _freeList = new();
        // The bits of each buddy: [1: is claimed][1: has claimed children][30: MIN_BUDDY_SIZE shift to get size]
        // CLR uses unsigned bytes; the low six bits hold the shift and the high two are flags.
        private readonly byte[] _buddies;
        private readonly int _freeListCapacity;

        internal BuddyChunk(ByteBuf @delegate, ChunkRegistry chunkRegistry) : base(@delegate, chunkRegistry, true)
        {
            // CLR: reject shapes the byte flags cannot describe before the chunk is registered.
            if (Capacity < MIN_BUDDY_SIZE || Capacity > 8 * 1024 * 1024 || !BitOperations.IsPow2(Capacity))
                throw new ArgumentOutOfRangeException("capacity");
            _freeListCapacity = Capacity / MIN_BUDDY_SIZE;
            int maxShift = BitOperations.TrailingZeroCount((uint)_freeListCapacity);
            System.Diagnostics.Debug.Assert(maxShift <= 30); // The top 2 bits are used for marking.
            // At most half of tree (all leaf nodes) can be freed.
            _buddies = new byte[_freeListCapacity << 1];

            // Generate the buddies entries.
            int index = 1;
            int runLength = 1;
            int currentRun = 0;
            while (maxShift > 0)
            {
                _buddies[index++] = (byte)maxShift;
                if (++currentRun == runLength)
                {
                    currentRun = 0;
                    runLength <<= 1;
                    maxShift--;
                }
            }
            Register();
        }

        // CLR: the Memory-based counterpart of readInitInto (see Chunk.TryAllocate).
        internal override AdaptiveChunkAllocation TryAllocate(int startingCapacity)
        {
            ThrowIfRetired();
            if (!_freeList.IsEmpty)
            {
                ProcessFreelistEntries();
            }
            // A magazine may offer the residual capacity, which need not be a power of
            // two. The Java search returns no matching buddy in that case; return that
            // outcome before walking the tree.
            if (startingCapacity < MIN_BUDDY_SIZE || !BitOperations.IsPow2(startingCapacity))
            {
                return null;
            }
            int startIndex = ChooseFirstFreeBuddy(1, startingCapacity, 0);
            if (startIndex == -1)
            {
                return null;
            }
            Retain();
            try
            {
                var allocation = new AdaptiveChunkAllocation(this, Storage.AsMemory(startIndex, startingCapacity), startIndex);
                AllocatedBytes += startingCapacity;
                return allocation;
            }
            catch
            {
                UnreserveMatchingBuddy(1, startingCapacity, startIndex, 0);
                // If chunk is not null we know that buf.init(...) failed and so we need to manually release
                // the chunk again as we retained it before calling buf.init(...).
                Release();
                throw;
            }
        }

        // CLR: the source's IntConsumer.accept, applied while draining the free list.
        internal void Accept(int packed)
        {
            // Called by allocating thread when draining freeList.
            int size = UnpackSize(packed);
            int offset = UnpackOffset(packed);
            UnreserveMatchingBuddy(1, size, offset, 0);
            AllocatedBytes -= size;
        }

        private static int UnpackSize(int packed)
        {
            return MIN_BUDDY_SIZE << (packed >> PACK_SIZE_SHIFT);
        }

        private static int UnpackOffset(int packed)
        {
            return (packed & PACK_OFFSET_MASK) * MIN_BUDDY_SIZE;
        }

        internal override void ReleaseSegment(int startingIndex, int size)
        {
            int packedOffset = startingIndex / MIN_BUDDY_SIZE;
            int packedSize = BitOperations.TrailingZeroCount((uint)(size / MIN_BUDDY_SIZE)) << PACK_SIZE_SHIFT;
            int packed = packedOffset | packedSize;
            _freeList.Enqueue(packed);
            Release();
        }

        internal override int RemainingCapacity
        {
            get
            {
                int capacityInFreeList = 0;
                if (!_freeList.IsEmpty)
                {
                    // A concurrent snapshot, like weakPeekReduce, is not a transaction with allocation.
                    foreach (int entry in _freeList)
                    {
                        capacityInFreeList += UnpackSize(entry);
                    }
                }
                return base.RemainingCapacity + capacityInFreeList;
            }
        }

        internal override bool HasUnprocessedFreelistEntries => !_freeList.IsEmpty;

        internal override void ProcessFreelistEntries()
        {
            // CLR: drain(freeListCapacity, this) as a bounded dequeue loop.
            for (int i = 0; i < _freeListCapacity && _freeList.TryDequeue(out int packed); i++)
            {
                Accept(packed);
            }
        }

        /**
         * Claim a suitable buddy and return its start offset into the delegate chunk, or return -1 if nothing claimed.
         */
        private int ChooseFirstFreeBuddy(int index, int size, int currOffset)
        {
            byte[] buddies = _buddies;
            while (index < buddies.Length)
            {
                byte buddy = buddies[index];
                int currValue = MIN_BUDDY_SIZE << (buddy & SHIFT_MASK);
                if (currValue < size || (buddy & IS_CLAIMED) == IS_CLAIMED)
                {
                    return -1;
                }
                if (currValue == size && (buddy & HAS_CLAIMED_CHILDREN) == 0)
                {
                    buddies[index] |= IS_CLAIMED;
                    return currOffset;
                }
                int found = ChooseFirstFreeBuddy(index << 1, size, currOffset);
                if (found != -1)
                {
                    buddies[index] |= HAS_CLAIMED_CHILDREN;
                    return found;
                }
                index = (index << 1) + 1;
                currOffset += currValue >> 1; // Bump offset to skip first half of this layer.
            }
            return -1;
        }

        /**
         * Un-reserve the matching buddy and return whether there are any other child or sibling reservations.
         */
        // CLR: internal so tests can present returns that no live reservation can produce.
        internal bool UnreserveMatchingBuddy(int index, int size, int offset, int currOffset)
        {
            byte[] buddies = _buddies;
            if (buddies.Length <= index)
            {
                return false;
            }
            byte buddy = buddies[index];
            int currSize = MIN_BUDDY_SIZE << (buddy & SHIFT_MASK);

            if (currSize == size)
            {
                // We're at the right size level.
                // CLR: also require the claim bit, so a duplicate return cannot clear a live sibling.
                if (currOffset == offset && (buddy & IS_CLAIMED) != 0)
                {
                    buddies[index] &= SHIFT_MASK;
                    return false;
                }
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                    $"The intended segment was not found at index {index}, for size {size} and offset {offset}"));
            }

            // We're at a parent size level. Use the target offset to guide our drill-down path.
            bool claims;
            int siblingIndex;
            if (offset < currOffset + (currSize >> 1))
            {
                // Must be down the left path.
                claims = UnreserveMatchingBuddy(index << 1, size, offset, currOffset);
                siblingIndex = (index << 1) + 1;
            }
            else
            {
                // Must be down the rigth path.
                claims = UnreserveMatchingBuddy((index << 1) + 1, size, offset, currOffset + (currSize >> 1));
                siblingIndex = index << 1;
            }
            if (!claims)
            {
                // No other claims down the path we took. Check if the sibling has claims.
                byte sibling = buddies[siblingIndex];
                if ((sibling & SHIFT_MASK) == sibling)
                {
                    // No claims in the sibling. We can clear this level as well.
                    buddies[index] &= SHIFT_MASK;
                    return false;
                }
            }
            return true;
        }

        public override string ToString()
        {
            int capacity = Capacity;
            int remaining = capacity - AllocatedBytes;
            return string.Create(CultureInfo.InvariantCulture,
                $"BuddyChunk[capacity: {capacity}, remaining: {remaining}, free list: {_freeList.Count}]");
        }
    }
}
