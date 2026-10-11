/*
 * Copyright 2012 The Netty Project
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
using System.Diagnostics;

namespace Netty.NET.Buffer;

internal sealed partial class PoolChunk
{
    private readonly PoolSubpage[] _smallSubpagePools;
    /**
     * manage all subpages in this chunk
     */
    private readonly PoolSubpage[] _subpages;

    // Returns the actual owner: an available subpage may belong to another chunk sharing these heads.
    internal long AllocateSmall(int sizeIdx, out PoolChunk owner)
    {
        if (sizeIdx < 0 || sizeIdx > _sizeClass.SmallMaxSizeIdx)
            throw new ArgumentOutOfRangeException(nameof(sizeIdx));
        owner = null;
        PoolSubpage head = _smallSubpagePools[sizeIdx];
        // small
        // Obtain the head of the PoolSubPage pool that is owned by the PoolArena and synchronize on it.
        // This is need as we may add it back and so alter the linked-list structure.
        lock (head.SyncRoot)
        {
            PoolSubpage nextSub = head.Next;
            if (nextSub != head)
            {
                Debug.Assert(nextSub.DoNotDestroy && nextSub.ElementSize == _sizeClass.SizeIdx2Size(sizeIdx));
                long handle = nextSub.Allocate();
                Debug.Assert(handle >= 0 && IsSubpage(handle));
                owner = nextSub.Chunk;
                return handle;
            }
            long result = AllocateSubpage(sizeIdx, head);
            if (result >= 0)
                owner = this;
            return result;
        }
    }

    internal int CalculateRunSize(int sizeIdx)
    {
        if (sizeIdx < 0 || sizeIdx > _sizeClass.SmallMaxSizeIdx)
            throw new ArgumentOutOfRangeException(nameof(sizeIdx));
        int maxElements = 1 << PageShifts - SizeClasses.LOG2_QUANTUM;
        long runSize = 0;
        long nElements;
        int elemSize = _sizeClass.SizeIdx2Size(sizeIdx);

        //find lowest common multiple of pageSize and elemSize
        do
        {
            runSize += PageSize;
            nElements = runSize / elemSize;
        } while (nElements < maxElements && runSize != nElements * elemSize);
        while (nElements > maxElements)
        {
            runSize -= PageSize;
            nElements = runSize / elemSize;
        }
        Debug.Assert(nElements > 0 && runSize >= elemSize);
        // CLR: fail if the chunk cannot fit the calculated LCM run, rather than accepting a shorter run.
        return runSize > ChunkSize ? -1 : checked((int)runSize);
    }

    /**
     * Create / initialize a new PoolSubpage of normCapacity. Any PoolSubpage created / initialized here is added to
     * subpage pool in the PoolArena that owns this PoolChunk.
     *
     * @param sizeIdx sizeIdx of normalized size
     * @param head head of subpages
     *
     * @return index in memoryMap
     */
    private long AllocateSubpage(int sizeIdx, PoolSubpage head)
    {
        //allocate a new run
        int runSize = CalculateRunSize(sizeIdx);
        if (runSize < 0)
            return -1;
        //runSize must be multiples of pageSize
        long runHandle = AllocateRun(runSize);
        if (runHandle < 0)
            return -1;
        int runOffset = RunOffset(runHandle);
        Debug.Assert(_subpages[runOffset] == null);
        int elemSize = _sizeClass.SizeIdx2Size(sizeIdx);
        PoolSubpage subpage;
        try
        {
            subpage = new PoolSubpage(head, this, runOffset, RunSize(PageShifts, runHandle), elemSize);
        }
        catch
        {
            FreeRun(runHandle);
            throw;
        }
        _subpages[runOffset] = subpage;
        return subpage.Allocate();
    }

    /**
     * Free a subpage or a run of pages When a subpage is freed from PoolSubpage, it might be added back to subpage pool
     * of the owning PoolArena. If the subpage pool in PoolArena has at least one other PoolSubpage of given elemSize,
     * we can completely free the owning Page so it is available for subsequent allocations
     *
     * @param handle handle to free
     */
    internal void FreeSmall(long handle)
    {
        int offset = RunOffset(handle);
        if (handle < 0 || !IsUsed(handle) || !IsSubpage(handle) || offset >= _subpages.Length)
            throw new ArgumentOutOfRangeException(nameof(handle));
        PoolSubpage subpage = _subpages[offset];
        if (subpage == null)
            throw new InvalidOperationException("The subpage run is not allocated.");
        PoolSubpage head = subpage.Head;
        // Obtain the head of the PoolSubPage pool that is owned by the PoolArena and synchronize on it.
        // This is need as we may add it back and so alter the linked-list structure.
        lock (head.SyncRoot)
        {
            if (!ReferenceEquals(_subpages[offset], subpage) || !subpage.DoNotDestroy ||
                RunPages(handle) != subpage.RunSize >> PageShifts)
                throw new InvalidOperationException("The handle does not describe a live subpage run.");
            if (subpage.Free(BitmapIdx(handle)))
            {
                //the subpage is still used, do not free it
                return;
            }
            Debug.Assert(!subpage.DoNotDestroy);
            // Null out slot in the array as it was freed and we should not use it anymore.
            _subpages[offset] = null;
        }
        // The page-run path expects the used run with neither a subpage flag nor a bitmap index.
        FreeRun(handle & ~(1L << IS_SUBPAGE_SHIFT | 0xffffffffL));
    }

    internal PoolSubpage SubpageAt(int runOffset)
    {
        if (runOffset < 0 || runOffset >= _subpages.Length)
            throw new ArgumentOutOfRangeException(nameof(runOffset));
        return _subpages[runOffset];
    }
}
