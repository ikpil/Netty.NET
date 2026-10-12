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
using System.Collections.Generic;
using System.Threading;

namespace Netty.NET.Buffer;

internal abstract partial class PoolArena
{
    // Metrics for allocations and deallocations
    // CLR: one arena Monitor protects every counter, replacing Java's split
    // LongCounter/lock strategy. Counts refer to reservations, not wrapper objects.
    // Original Java counter rationale (CLR: these counters now share the arena lock):
    // We need to use the LongCounter here as this is not guarded via synchronized block.
    // We need to use the LongCounter here as this is not guarded via synchronized block.
    private long _smallAllocations, _normalAllocations, _hugeAllocations;
    private long _smallDeallocations, _normalDeallocations, _hugeDeallocations;
    private long _chunkAllocations, _chunkDeallocations, _activeBytesHuge, _pinnedBytes;
    // Huge chunks are exact-sized for the buffers they were allocated to.
    // CLR: huge reservations include normalized size, excluding native alignment headroom.

    private int _threadBindings;
    // Java counts a thread's arena binding even when its region cache sizes are zero.
    public int NumThreadCaches => Volatile.Read(ref _threadBindings);
    internal void AddThreadBinding() => Interlocked.Increment(ref _threadBindings);
    internal void RemoveThreadBinding() => Interlocked.Decrement(ref _threadBindings);
    [Obsolete("Tiny allocations have been merged into small allocations.")]
    public int NumTinySubpages => 0;
    public int NumSmallSubpages => SizeClass.NSubpages;
    public int NumChunkLists => ChunkLists.Count;
    [Obsolete("Tiny allocations have been merged into small allocations.")]
    public IReadOnlyList<IPoolSubpageMetric> TinySubpages => Array.Empty<IPoolSubpageMetric>();
    public IReadOnlyList<IPoolSubpageMetric> SmallSubpages
    {
        get
        {
            lock (_sync)
            {
                var result = new List<IPoolSubpageMetric>();
                foreach (PoolSubpage head in _smallSubpagePools)
                {
                    if (head == null) continue;
                    lock (head.SyncRoot)
                        for (PoolSubpage subpage = head.Next; subpage != head; subpage = subpage.Next)
                            result.Add(subpage);
                }
                return result.AsReadOnly();
            }
        }
    }
    IReadOnlyList<IPoolChunkListMetric> IPoolArenaMetric.ChunkLists => ChunkLists;

    public long NumAllocations { get { lock (_sync) return unchecked(_smallAllocations + _normalAllocations + _hugeAllocations); } }
    public long NumDeallocations { get { lock (_sync) return unchecked(_smallDeallocations + _normalDeallocations + _hugeDeallocations); } }
    public long NumSmallAllocations { get { lock (_sync) return _smallAllocations; } }
    public long NumNormalAllocations { get { lock (_sync) return _normalAllocations; } }
    public long NumHugeAllocations { get { lock (_sync) return _hugeAllocations; } }
    public long NumSmallDeallocations { get { lock (_sync) return _smallDeallocations; } }
    public long NumNormalDeallocations { get { lock (_sync) return _normalDeallocations; } }
    public long NumHugeDeallocations { get { lock (_sync) return _hugeDeallocations; } }
    public long NumChunkAllocations { get { lock (_sync) return _chunkAllocations; } }
    public long NumChunkDeallocations { get { lock (_sync) return _chunkDeallocations; } }
    public long NumActiveAllocations { get { lock (_sync) return Math.Max(unchecked(_smallAllocations + _normalAllocations + _hugeAllocations - _smallDeallocations - _normalDeallocations - _hugeDeallocations), 0); } }
    public long NumActiveSmallAllocations { get { lock (_sync) return Math.Max(unchecked(_smallAllocations - _smallDeallocations), 0); } }
    public long NumActiveNormalAllocations { get { lock (_sync) return Math.Max(unchecked(_normalAllocations - _normalDeallocations), 0); } }
    public long NumActiveHugeAllocations { get { lock (_sync) return Math.Max(unchecked(_hugeAllocations - _hugeDeallocations), 0); } }
    public long NumActiveChunks { get { lock (_sync) return Math.Max(unchecked(_chunkAllocations - _chunkDeallocations), 0); } }
    public long NumActiveBytes { get { lock (_sync) return Math.Max(unchecked(_chunks.Count * (long)SizeClass.ChunkSize + _activeBytesHuge), 0); } }

    [Obsolete("Tiny allocations have been merged into small allocations.")]
    public long NumTinyAllocations => 0;
    [Obsolete("Tiny allocations have been merged into small allocations.")]
    public long NumTinyDeallocations => 0;
    [Obsolete("Tiny allocations have been merged into small allocations.")]
    public long NumActiveTinyAllocations => 0;

    /**
     * Return an estimate of the number of bytes that are currently pinned to buffer instances, by the arena. The
     * pinned memory is not accessible for use by any other allocation, until the buffers using have all been released.
     */
    // CLR: internal operation leases also hold reservations. Raw native pins alone
    // preserve physical storage but do not contribute to this logical count.
    public long NumPinnedBytes { get { lock (_sync) return Math.Max(_pinnedBytes, 0); } }

    public int SizeIdx2Size(int sizeIdx) => SizeClass.SizeIdx2Size(sizeIdx);
    public int SizeIdx2SizeCompute(int sizeIdx) => SizeClass.SizeIdx2SizeCompute(sizeIdx);
    public long PageIdx2Size(int pageIdx) => SizeClass.PageIdx2Size(pageIdx);
    public long PageIdx2SizeCompute(int pageIdx) => SizeClass.PageIdx2SizeCompute(pageIdx);
    public int Size2SizeIdx(int size) => SizeClass.Size2SizeIdx(size);
    public int Pages2PageIdx(int pages) => SizeClass.Pages2PageIdx(pages);
    public int Pages2PageIdxFloor(int pages) => SizeClass.Pages2PageIdxFloor(pages);
    public int NormalizeSize(int size) => SizeClass.NormalizeSize(size);
}
