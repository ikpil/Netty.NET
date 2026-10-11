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
using System.Diagnostics;

namespace Netty.NET.Buffer;

/**
 * Description of algorithm for PageRun/PoolSubpage allocation from PoolChunk
 *
 * Notation: The following terms are important to understand the code
 * > page  - a page is the smallest unit of memory chunk that can be allocated
 * > run   - a run is a collection of pages
 * > chunk - a chunk is a collection of runs
 * > in this code chunkSize = maxPages * pageSize
 *
 * To begin we allocate a byte array of size = chunkSize
 * Whenever a ByteBuf of given size needs to be created we search for the first position
 * in the byte array that has enough empty space to accommodate the requested size and
 * return a (long) handle that encodes this offset information, (this memory segment is then
 * marked as reserved so it is always used by exactly one ByteBuf and no more)
 *
 * For simplicity all sizes are normalized according to {@link PoolArena#sizeClass#size2SizeIdx(int)} method.
 * This ensures that when we request for memory segments of size > pageSize the normalizedCapacity
 * equals the next nearest size in {@link SizeClasses}.
 *
 *
 *  A chunk has the following layout:
 *
 *     /-----------------\
 *     | run             |
 *     |                 |
 *     |                 |
 *     |-----------------|
 *     | run             |
 *     |                 |
 *     |-----------------|
 *     | unalloctated    |
 *     | (freed)         |
 *     |                 |
 *     |-----------------|
 *     | subpage         |
 *     |-----------------|
 *     | unallocated     |
 *     | (freed)         |
 *     | ...             |
 *     | ...             |
 *     | ...             |
 *     |                 |
 *     |                 |
 *     |                 |
 *     \-----------------/
 *
 *
 * handle:
 * -------
 * a handle is a long number, the bit layout of a run looks like:
 *
 * oooooooo ooooooos ssssssss ssssssue bbbbbbbb bbbbbbbb bbbbbbbb bbbbbbbb
 *
 * o: runOffset (page offset in the chunk), 15bit
 * s: size (number of pages) of this run, 15bit
 * u: isUsed?, 1bit
 * e: isSubpage?, 1bit
 * b: bitmapIdx of subpage, zero if it's not subpage, 32bit
 *
 * runsAvailMap:
 * ------
 * a map which manages all runs (used and not in used).
 * For each run, the first runOffset and last runOffset are stored in runsAvailMap.
 * key: runOffset
 * value: handle
 *
 * runsAvail:
 * ----------
 * an array of {@link PriorityQueue}.
 * Each queue manages same size of runs.
 * Runs are sorted by offset, so that we always allocate runs with smaller offset.
 *
 *
 * Algorithm:
 * ----------
 *
 *   As we allocate runs, we update values stored in runsAvailMap and runsAvail so that the property is maintained.
 *
 * Initialization -
 *  In the beginning we store the initial run which is the whole chunk.
 *  The initial run:
 *  runOffset = 0
 *  size = chunkSize
 *  isUsed = no
 *  isSubpage = no
 *  bitmapIdx = 0
 *
 *
 * Algorithm: [allocateRun(size)]
 * ----------
 * 1) find the first avail run using in runsAvails according to size
 * 2) if pages of run is larger than request pages then split it, and save the tailing run
 *    for later using
 *
 * Algorithm: [allocateSubpage(size)]
 * ----------
 * 1) find a not full subpage according to size.
 *    if it already exists just return, otherwise allocate a new PoolSubpage and call init()
 *    note that this subpage object is added to subpagesPool in the PoolArena when we init() it
 * 2) call subpage.allocate()
 *
 * Algorithm: [free(handle, length, nioBuffer)]
 * ----------
 * 1) if it is a subpage, return the slab back into this subpage
 * 2) if the subpage is not used or it is a run, then start free this run
 * 3) merge continuous avail runs
 * 4) save the merged run
 *
 */

/// <remarks>This stage ports page-run and subpage bookkeeping. Storage and buffer initialization,
/// arena lists and thread caches are not connected yet. Allocated handles belong to this chunk;
/// callers must return each handle exactly once and must not use stale or foreign handles.</remarks>
internal sealed partial class PoolChunk : IPoolChunkMetric
{
    internal const int IS_SUBPAGE_SHIFT = 32;
    internal const int IS_USED_SHIFT = 1 + IS_SUBPAGE_SHIFT;
    internal const int SIZE_SHIFT = 1 + IS_USED_SHIFT;
    internal const int RUN_OFFSET_SHIFT = 15 + SIZE_SHIFT;

    private readonly SizeClasses _sizeClass;

    /**
     * store the first page and last page of each avail run
     */
    private readonly Dictionary<int, long> _runsAvailMap = new();

    /**
     * manage all avail runs
     */
    private readonly PriorityQueue<int, int>[] _runsAvail;
    private readonly object _runsAvailLock = new();
    private int _freeBytes;

    internal int PageSize => _sizeClass.PageSize;
    internal int PageShifts => _sizeClass.PageShifts;
    internal SizeClasses SizeClass => _sizeClass;
    public int ChunkSize => _sizeClass.ChunkSize;

    internal PoolChunk(SizeClasses sizeClass, PoolSubpage[] smallSubpagePools = null)
    {
        ArgumentNullException.ThrowIfNull(sizeClass);
        if (sizeClass.PageSize != 1 << sizeClass.PageShifts)
            throw new ArgumentException("Page size and shift must describe the same pool geometry.", nameof(sizeClass));
        // The original allocator limits maxOrder to 14; larger offsets make signed handles negative.
        if (sizeClass.ChunkSize / sizeClass.PageSize > 1 << 14)
            throw new ArgumentException("A pooled chunk must contain at most 16384 pages.", nameof(sizeClass));
        if (smallSubpagePools != null)
        {
            if (smallSubpagePools.Length != sizeClass.NSubpages)
                throw new ArgumentException("Incorrect number of subpage heads.", nameof(smallSubpagePools));
            for (int i = 0; i < smallSubpagePools.Length; i++)
                if (smallSubpagePools[i] == null || smallSubpagePools[i].Chunk != null ||
                    smallSubpagePools[i].HeadIndex != i || !ReferenceEquals(smallSubpagePools[i].SizeClass, sizeClass))
                    throw new ArgumentException("Subpage heads must share this size-class domain.", nameof(smallSubpagePools));
        }
        _sizeClass = sizeClass;
        _freeBytes = ChunkSize;
        _runsAvail = NewRunsAvailQueueArray(sizeClass.NPSizes);
        _smallSubpagePools = smallSubpagePools == null ? PoolSubpage.CreatePoolHeads(sizeClass) :
            (PoolSubpage[])smallSubpagePools.Clone();
        _subpages = new PoolSubpage[sizeClass.ChunkSize >> sizeClass.PageShifts];

        //insert initial run, offset = 0, pages = chunkSize / pageSize
        int pages = ChunkSize >> PageShifts;
        long initHandle = (long)pages << SIZE_SHIFT;
        InsertAvailRun(0, pages, initHandle);
    }

    // Original Java IntPriorityQueue documentation (the CLR replacement below uses the BCL heap):
    /*
     * Copyright 2020 The Netty Project
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
    /**
     * Internal primitive priority queue, used by {@link PoolChunk}.
     * The implementation is based on the binary heap, as described in Algorithms by Sedgewick and Wayne.
     */
    private static PriorityQueue<int, int>[] NewRunsAvailQueueArray(int size)
    {
        var queueArray = new PriorityQueue<int, int>[size];
        for (int i = 0; i < queueArray.Length; i++)
            queueArray[i] = new PriorityQueue<int, int>();
        return queueArray;
    }

    private void InsertAvailRun(int runOffset, int pages, long handle)
    {
        int pageIdxFloor = _sizeClass.Pages2PageIdxFloor(pages);
        Debug.Assert(IsRun(handle));
        int entry = (int)(handle >> 32);
        // Grow queue capacity.
        // CLR: PriorityQueue grows automatically when Enqueue requires more space.
        _runsAvail[pageIdxFloor].Enqueue(entry, entry);

        //insert first page of run
        InsertAvailRun0(runOffset, handle);
        if (pages > 1)
        {
            //insert last page of run
            InsertAvailRun0(LastPage(runOffset, pages), handle);
        }
    }

    private void InsertAvailRun0(int runOffset, long handle)
    {
        // A boundary has exactly one free run. Dictionary.Add also detects duplicate boundaries.
        _runsAvailMap.Add(runOffset, handle);
    }

    private void RemoveAvailRun(long handle)
    {
        int pageIdxFloor = _sizeClass.Pages2PageIdxFloor(RunPages(handle));
        bool removed = _runsAvail[pageIdxFloor].Remove((int)(handle >> 32), out _, out _);
        Debug.Assert(removed);
        RemoveAvailRun0(handle);
    }

    private void RemoveAvailRun0(long handle)
    {
        int runOffset = RunOffset(handle);
        int pages = RunPages(handle);
        //remove first page of run
        _runsAvailMap.Remove(runOffset);
        if (pages > 1)
        {
            //remove last page of run
            _runsAvailMap.Remove(LastPage(runOffset, pages));
        }
    }

    private static int LastPage(int runOffset, int pages) => runOffset + pages - 1;

    private long GetAvailRunByOffset(int runOffset) => _runsAvailMap.GetValueOrDefault(runOffset, -1L);

    public int Usage
    {
        get
        {
            lock (_runsAvailLock)
                return CalculateUsage(_freeBytes);
        }
    }

    private int CalculateUsage(int freeBytes)
    {
        if (freeBytes == 0)
            return 100;
        int freePercentage = (int)(freeBytes * 100L / ChunkSize);
        return freePercentage == 0 ? 99 : 100 - freePercentage;
    }

    public int FreeBytes
    {
        get
        {
            lock (_runsAvailLock)
                return _freeBytes;
        }
    }

    internal long AllocateRun(int runSize)
    {
        if (runSize <= 0 || runSize > ChunkSize || runSize % PageSize != 0)
            throw new ArgumentOutOfRangeException(nameof(runSize));
        int pages = runSize >> PageShifts;
        int pageIdx = _sizeClass.Pages2PageIdx(pages);
        lock (_runsAvailLock)
        {
            //find first queue which has at least one big enough run
            int queueIdx = RunFirstBestFit(pageIdx);
            if (queueIdx == -1)
                return -1;

            //get run with min offset in this queue
            long handle = _runsAvail[queueIdx].Dequeue();
            handle <<= 32;
            Debug.Assert(!IsUsed(handle), "invalid handle: " + handle);
            RemoveAvailRun0(handle);
            handle = SplitLargeRun(handle, pages);
            _freeBytes -= RunSize(PageShifts, handle);
            return handle;
        }
    }

    private int RunFirstBestFit(int pageIdx)
    {
        if (_freeBytes == ChunkSize)
            return _sizeClass.NPSizes - 1;
        for (int i = pageIdx; i < _sizeClass.NPSizes; i++)
            if (_runsAvail[i].Count != 0)
                return i;
        return -1;
    }

    private long SplitLargeRun(long handle, int needPages)
    {
        Debug.Assert(needPages > 0);
        int totalPages = RunPages(handle);
        Debug.Assert(needPages <= totalPages);
        int remPages = totalPages - needPages;
        if (remPages > 0)
        {
            int runOffset = RunOffset(handle);

            // keep track of trailing unused pages for later use
            int availOffset = runOffset + needPages;
            long availRun = ToRunHandle(availOffset, remPages, 0);
            InsertAvailRun(availOffset, remPages, availRun);

            // not avail
            return ToRunHandle(runOffset, needPages, 1);
        }

        //mark it as used
        return handle | 1L << IS_USED_SHIFT;
    }

    /// <summary>Returns an owned, live page-run handle exactly once; subpages use a separate path.</summary>
    internal void FreeRun(long handle)
    {
        int offset = RunOffset(handle);
        int pages = RunPages(handle);
        if (handle < 0 || !IsUsed(handle) || !IsRun(handle) || BitmapIdx(handle) != 0 || pages == 0 ||
            offset + pages > ChunkSize / PageSize)
            throw new ArgumentOutOfRangeException(nameof(handle));
        int runSize = RunSize(PageShifts, handle);
        //start free run
        lock (_runsAvailLock)
        {
            // collapse continuous runs, successfully collapsed runs
            // will be removed from runsAvail and runsAvailMap
            long finalRun = CollapseRuns(handle);

            //set run as not used
            finalRun &= ~(1L << IS_USED_SHIFT);
            //if it is a subpage, set it to run
            finalRun &= ~(1L << IS_SUBPAGE_SHIFT);
            InsertAvailRun(RunOffset(finalRun), RunPages(finalRun), finalRun);
            _freeBytes += runSize;
        }
    }

    private long CollapseRuns(long handle) => CollapseNext(CollapsePast(handle));

    private long CollapsePast(long handle)
    {
        for (; ; )
        {
            int runOffset = RunOffset(handle);
            int runPages = RunPages(handle);
            long pastRun = GetAvailRunByOffset(runOffset - 1);
            if (pastRun == -1)
                return handle;
            int pastOffset = RunOffset(pastRun);
            int pastPages = RunPages(pastRun);

            //is continuous
            if (pastRun != handle && pastOffset + pastPages == runOffset)
            {
                //remove past run
                RemoveAvailRun(pastRun);
                handle = ToRunHandle(pastOffset, pastPages + runPages, 0);
            }
            else
                return handle;
        }
    }

    private long CollapseNext(long handle)
    {
        for (; ; )
        {
            int runOffset = RunOffset(handle);
            int runPages = RunPages(handle);
            long nextRun = GetAvailRunByOffset(runOffset + runPages);
            if (nextRun == -1)
                return handle;
            int nextOffset = RunOffset(nextRun);
            int nextPages = RunPages(nextRun);

            //is continuous
            if (nextRun != handle && runOffset + runPages == nextOffset)
            {
                //remove next run
                RemoveAvailRun(nextRun);
                handle = ToRunHandle(runOffset, runPages + nextPages, 0);
            }
            else
                return handle;
        }
    }

    private static long ToRunHandle(int runOffset, int runPages, int inUsed) =>
        (long)runOffset << RUN_OFFSET_SHIFT | (long)runPages << SIZE_SHIFT | (long)inUsed << IS_USED_SHIFT;

    internal static int RunOffset(long handle) => (int)(handle >> RUN_OFFSET_SHIFT);
    internal static int RunSize(int pageShifts, long handle) => RunPages(handle) << pageShifts;
    internal static int RunPages(long handle) => (int)(handle >> SIZE_SHIFT & 0x7fff);
    internal static bool IsUsed(long handle) => (handle >> IS_USED_SHIFT & 1) == 1L;
    internal static bool IsRun(long handle) => !IsSubpage(handle);
    internal static bool IsSubpage(long handle) => (handle >> IS_SUBPAGE_SHIFT & 1) == 1L;
    internal static int BitmapIdx(long handle) => unchecked((int)handle);
}
