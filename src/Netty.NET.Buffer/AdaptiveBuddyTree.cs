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
using System.Numerics;

namespace Netty.NET.Buffer;

// CLR: the buddy reservation tree is independent of storage ownership and pending
// cross-thread returns. Its owner must serialize operations, as the original magazine does.
internal sealed class AdaptiveBuddyTree
{
    internal const int MinBuddySize = 32768;
    private const byte IsClaimed = 1 << 7;
    private const byte HasClaimedChildren = 1 << 6;
    private const byte ShiftMask = 0x3F;
    private const int PackOffsetMask = 0xFFFF;
    private const int PackSizeShift = 16;
    // The bits of each buddy: [1: is claimed][1: has claimed children][30: MIN_BUDDY_SIZE shift to get size]
    // CLR uses unsigned bytes; the low six bits hold the shift and the high two are flags.
    private readonly byte[] _buddies;
    private int _allocatedBytes;
    internal int Capacity { get; }
    internal int RemainingCapacity => Capacity - _allocatedBytes;

    internal AdaptiveBuddyTree(int capacity)
    {
        if (capacity < MinBuddySize || capacity > 8 * 1024 * 1024 || !BitOperations.IsPow2(capacity))
            throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
        int leaves = capacity / MinBuddySize;
        int maxShift = BitOperations.TrailingZeroCount((uint)leaves);
        System.Diagnostics.Debug.Assert(maxShift <= 30); // The top 2 bits are used for marking.
        _buddies = new byte[leaves << 1];
        // Generate the buddies entries.
        int index = 1, runLength = 1, currentRun = 0;
        while (maxShift > 0)
        {
            _buddies[index++] = (byte)maxShift;
            if (++currentRun == runLength) { currentRun = 0; runLength <<= 1; maxShift--; }
        }
    }
    private static void ValidateSize(int size)
    {
        if (size < MinBuddySize || !BitOperations.IsPow2(size)) throw new ArgumentOutOfRangeException(nameof(size));
    }
    internal int TryAllocate(int size)
    {
        ValidateSize(size);
        int offset = ChooseFirstFreeBuddy(1, size, 0);
        if (offset >= 0) _allocatedBytes += size;
        return offset;
    }
    internal void Free(int offset, int size)
    {
        ValidateSize(size);
        if (offset < 0 || offset > Capacity - size || offset % size != 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        UnreserveMatchingBuddy(1, size, offset, 0);
        _allocatedBytes -= size;
    }
    internal static int PackFreeSegment(int offset, int size)
    {
        ValidateSize(size);
        if (size > 8 * 1024 * 1024 || offset < 0 || offset > 8 * 1024 * 1024 - size || offset % size != 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        int packedOffset = offset / MinBuddySize;
        int packedSize = BitOperations.TrailingZeroCount((uint)(size / MinBuddySize)) << PackSizeShift;
        return packedOffset | packedSize;
    }
    internal static int UnpackSize(int packed) => MinBuddySize << (packed >> PackSizeShift);
    internal static int UnpackOffset(int packed) => (packed & PackOffsetMask) * MinBuddySize;

    /**
     * Claim a suitable buddy and return its start offset into the delegate chunk, or return -1 if nothing claimed.
     */
    private int ChooseFirstFreeBuddy(int index, int size, int currOffset)
    {
        while (index < _buddies.Length)
        {
            byte buddy = _buddies[index];
            int currValue = MinBuddySize << (buddy & ShiftMask);
            if (currValue < size || (buddy & IsClaimed) == IsClaimed) return -1;
            if (currValue == size && (buddy & HasClaimedChildren) == 0)
            {
                _buddies[index] |= IsClaimed;
                return currOffset;
            }
            int found = ChooseFirstFreeBuddy(index << 1, size, currOffset);
            if (found != -1) { _buddies[index] |= HasClaimedChildren; return found; }
            index = (index << 1) + 1;
            currOffset += currValue >> 1; // Bump offset to skip first half of this layer.
        }
        return -1;
    }
    /**
     * Un-reserve the matching buddy and return whether there are any other child or sibling reservations.
     */
    private bool UnreserveMatchingBuddy(int index, int size, int offset, int currOffset)
    {
        if (_buddies.Length <= index) return false;
        byte buddy = _buddies[index];
        int currSize = MinBuddySize << (buddy & ShiftMask);
        if (currSize == size)
        {
            // We're at the right size level.
            if (currOffset == offset && (buddy & IsClaimed) != 0)
            {
                _buddies[index] &= ShiftMask;
                return false;
            }
            throw new InvalidOperationException($"The intended segment was not found at index {index}, for size {size} and offset {offset}.");
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
            byte sibling = _buddies[siblingIndex];
            if ((sibling & ShiftMask) == sibling)
            {
                // No claims in the sibling. We can clear this level as well.
                _buddies[index] &= ShiftMask;
                return false;
            }
        }
        return true;
    }
}
