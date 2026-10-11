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
using System;
using System.Diagnostics;
using System.Numerics;

namespace Netty.NET.Buffer;

/**
 * SizeClasses requires {@code pageShifts} to be defined prior to inclusion,
 * and it in turn defines:
 * <p>
 *   LOG2_SIZE_CLASS_GROUP: Log of size class count for each size doubling.
 *   LOG2_MAX_LOOKUP_SIZE: Log of max size class in the lookup table.
 *   sizeClasses: Complete table of [index, log2Group, log2Delta, nDelta, isMultiPageSize,
 *                 isSubPage, log2DeltaLookup] tuples.
 *     index: Size class index.
 *     log2Group: Log of group base size (no deltas added).
 *     log2Delta: Log of delta to previous size class.
 *     nDelta: Delta multiplier.
 *     isMultiPageSize: 'yes' if a multiple of the page size, 'no' otherwise.
 *     isSubPage: 'yes' if a subpage size class, 'no' otherwise.
 *     log2DeltaLookup: Same as log2Delta if a lookup table size class, 'no'
 *                      otherwise.
 * <p>
 *   nSubpages: Number of subpages size classes.
 *   nSizes: Number of size classes.
 *   nPSizes: Number of size classes that are multiples of pageSize.
 *
 *   smallMaxSizeIdx: Maximum small size class index.
 *
 *   lookupMaxClass: Maximum size class included in lookup table.
 *   log2NormalMinClass: Log of minimum normal size class.
 * <p>
 *   The first size class and spacing are 1 << LOG2_QUANTUM.
 *   Each group has 1 << LOG2_SIZE_CLASS_GROUP of size classes.
 *
 *   size = 1 << log2Group + nDelta * (1 << log2Delta)
 *
 *   The first size class has an unusual encoding, because the size has to be
 *   split between group and delta*nDelta.
 *
 *   If pageShift = 13, sizeClasses looks like this:
 *
 *   (index, log2Group, log2Delta, nDelta, isMultiPageSize, isSubPage, log2DeltaLookup)
 * <p>
 *   ( 0,     4,        4,         0,       no,             yes,        4)
 *   ( 1,     4,        4,         1,       no,             yes,        4)
 *   ( 2,     4,        4,         2,       no,             yes,        4)
 *   ( 3,     4,        4,         3,       no,             yes,        4)
 * <p>
 *   ( 4,     6,        4,         1,       no,             yes,        4)
 *   ( 5,     6,        4,         2,       no,             yes,        4)
 *   ( 6,     6,        4,         3,       no,             yes,        4)
 *   ( 7,     6,        4,         4,       no,             yes,        4)
 * <p>
 *   ( 8,     7,        5,         1,       no,             yes,        5)
 *   ( 9,     7,        5,         2,       no,             yes,        5)
 *   ( 10,    7,        5,         3,       no,             yes,        5)
 *   ( 11,    7,        5,         4,       no,             yes,        5)
 *   ...
 *   ...
 *   ( 72,    23,       21,        1,       yes,            no,        no)
 *   ( 73,    23,       21,        2,       yes,            no,        no)
 *   ( 74,    23,       21,        3,       yes,            no,        no)
 *   ( 75,    23,       21,        4,       yes,            no,        no)
 * <p>
 *   ( 76,    24,       22,        1,       yes,            no,        no)
 */
internal sealed class SizeClasses : ISizeClassesMetric
{

    internal const int LOG2_QUANTUM = 4;

    private const int LOG2_SIZE_CLASS_GROUP = 2;
    private const int LOG2_MAX_LOOKUP_SIZE = 12;

    private const int LOG2GROUP_IDX = 1;
    private const int LOG2DELTA_IDX = 2;
    private const int NDELTA_IDX = 3;
    private const int PAGESIZE_IDX = 4;
    private const int SUBPAGE_IDX = 5;
    private const int LOG2_DELTA_LOOKUP_IDX = 6;

    private const byte No = 0, Yes = 1;

    internal readonly int PageSize;
    internal readonly int PageShifts;
    internal readonly int ChunkSize;
    internal readonly int DirectMemoryCacheAlignment;

    internal readonly int NSizes;
    internal readonly int NSubpages;
    internal readonly int NPSizes;
    internal readonly int LookupMaxSize;
    internal readonly int SmallMaxSizeIdx;

    private readonly int[] pageIdx2SizeTab;

    // lookup table for sizeIdx < nSizes
    private readonly int[] sizeIdx2SizeTab;

    // lookup table used for size <= lookupMaxClass
    // spacing is 1 << LOG2_QUANTUM, so the size of array is lookupMaxClass >> LOG2_QUANTUM
    private readonly int[] size2IdxTab;

    internal SizeClasses(int pageSize, int pageShifts, int chunkSize, int directMemoryCacheAlignment)
    {
        if (pageSize < 4096 || !BitOperations.IsPow2(pageSize))
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (pageShifts < 4 || pageShifts > 30)
            throw new ArgumentOutOfRangeException(nameof(pageShifts));
        if (chunkSize < pageSize || chunkSize > 1 << 30 || !BitOperations.IsPow2(chunkSize))
            throw new ArgumentOutOfRangeException(nameof(chunkSize));
        if (directMemoryCacheAlignment < 0 || directMemoryCacheAlignment > pageSize ||
            directMemoryCacheAlignment != 0 && !BitOperations.IsPow2(directMemoryCacheAlignment))
            throw new ArgumentOutOfRangeException(nameof(directMemoryCacheAlignment));

        int group = Log2(chunkSize) - LOG2_QUANTUM - LOG2_SIZE_CLASS_GROUP + 1;

        //generate size classes
        //[index, log2Group, log2Delta, nDelta, isMultiPageSize, isSubPage, log2DeltaLookup]
        short[][] sizeClasses = new short[group << LOG2_SIZE_CLASS_GROUP][];

        int normalMaxSize = -1;
        int nSizes = 0;
        int size = 0;

        int log2Group = LOG2_QUANTUM;
        int log2Delta = LOG2_QUANTUM;
        int ndeltaLimit = 1 << LOG2_SIZE_CLASS_GROUP;

        //First small group, nDelta start at 0.
        //first size class is 1 << LOG2_QUANTUM
        for (int nDelta = 0; nDelta < ndeltaLimit; nDelta++, nSizes++)
        {
            short[] sizeClass = NewSizeClass(nSizes, log2Group, log2Delta, nDelta, pageShifts);
            sizeClasses[nSizes] = sizeClass;
            size = SizeOf(sizeClass, directMemoryCacheAlignment);
        }

        log2Group += LOG2_SIZE_CLASS_GROUP;

        //All remaining groups, nDelta start at 1.
        for (; size < chunkSize; log2Group++, log2Delta++)
        {
            for (int nDelta = 1; nDelta <= ndeltaLimit && size < chunkSize; nDelta++, nSizes++)
            {
                short[] sizeClass = NewSizeClass(nSizes, log2Group, log2Delta, nDelta, pageShifts);
                sizeClasses[nSizes] = sizeClass;
                size = normalMaxSize = SizeOf(sizeClass, directMemoryCacheAlignment);
            }
        }

        // Alignment can make the original loop stop before the raw chunk class exists.
        // Reject the resulting incomplete tables instead of exposing invalid size/page indices.
        short[] last = sizeClasses[nSizes - 1];
        if (CalculateSize(last[LOG2GROUP_IDX], last[NDELTA_IDX], last[LOG2DELTA_IDX]) != chunkSize)
            throw new ArgumentException("Alignment truncates the chunk size-class table.", nameof(directMemoryCacheAlignment));

        //chunkSize must be normalMaxSize
        Debug.Assert(chunkSize == normalMaxSize);

        int smallMaxSizeIdx = 0;
        int lookupMaxSize = 0;
        int nPSizes = 0;
        int nSubpages = 0;
        for (int idx = 0; idx < nSizes; idx++)
        {
            short[] sz = sizeClasses[idx];
            if (sz[PAGESIZE_IDX] == Yes)
            {
                nPSizes++;
            }
            if (sz[SUBPAGE_IDX] == Yes)
            {
                nSubpages++;
                smallMaxSizeIdx = idx;
            }
            if (sz[LOG2_DELTA_LOOKUP_IDX] != No)
            {
                lookupMaxSize = SizeOf(sz, directMemoryCacheAlignment);
            }
        }
        this.SmallMaxSizeIdx = smallMaxSizeIdx;
        this.LookupMaxSize = lookupMaxSize;
        this.NPSizes = nPSizes;
        this.NSubpages = nSubpages;
        this.NSizes = nSizes;

        this.PageSize = pageSize;
        this.PageShifts = pageShifts;
        this.ChunkSize = chunkSize;
        this.DirectMemoryCacheAlignment = directMemoryCacheAlignment;

        //generate lookup tables
        this.sizeIdx2SizeTab = NewIdx2SizeTab(sizeClasses, nSizes, directMemoryCacheAlignment);
        this.pageIdx2SizeTab = NewPageIdx2SizeTab(sizeClasses, nSizes, nPSizes, directMemoryCacheAlignment);
        this.size2IdxTab = NewSize2IdxTab(lookupMaxSize, sizeClasses);
    }

    //calculate size class
    private static short[] NewSizeClass(int index, int log2Group, int log2Delta, int nDelta, int pageShifts)
    {
        short isMultiPageSize;
        if (log2Delta >= pageShifts)
        {
            isMultiPageSize = Yes;
        }
        else
        {
            int pageSize = 1 << pageShifts;
            int size = CalculateSize(log2Group, nDelta, log2Delta);

            isMultiPageSize = size == size / pageSize * pageSize ? Yes : No;
        }

        int log2Ndelta = nDelta == 0 ? 0 : Log2(nDelta);

        byte remove = 1 << log2Ndelta < nDelta ? Yes : No;

        int log2Size = log2Delta + log2Ndelta == log2Group ? log2Group + 1 : log2Group;
        if (log2Size == log2Group)
        {
            remove = Yes;
        }

        short isSubpage = log2Size < pageShifts + LOG2_SIZE_CLASS_GROUP ? Yes : No;

        int log2DeltaLookup = log2Size < LOG2_MAX_LOOKUP_SIZE ||
                              log2Size == LOG2_MAX_LOOKUP_SIZE && remove == No
                ? log2Delta : No;

        return new short[] {
                (short) index, (short) log2Group, (short) log2Delta,
                (short) nDelta, isMultiPageSize, isSubpage, (short) log2DeltaLookup
        };
    }

    private static int[] NewIdx2SizeTab(short[][] sizeClasses, int nSizes, int directMemoryCacheAlignment)
    {
        int[] sizeIdx2SizeTab = new int[nSizes];

        for (int i = 0; i < nSizes; i++)
        {
            short[] sizeClass = sizeClasses[i];
            sizeIdx2SizeTab[i] = SizeOf(sizeClass, directMemoryCacheAlignment);
        }
        return sizeIdx2SizeTab;
    }

    private static int CalculateSize(int log2Group, int nDelta, int log2Delta)
    {
        return (1 << log2Group) + (nDelta << log2Delta);
    }

    private static int SizeOf(short[] sizeClass, int directMemoryCacheAlignment)
    {
        int log2Group = sizeClass[LOG2GROUP_IDX];
        int log2Delta = sizeClass[LOG2DELTA_IDX];
        int nDelta = sizeClass[NDELTA_IDX];

        int size = CalculateSize(log2Group, nDelta, log2Delta);

        return AlignSizeIfNeeded(size, directMemoryCacheAlignment);
    }

    private static int[] NewPageIdx2SizeTab(short[][] sizeClasses, int nSizes, int nPSizes,
                                            int directMemoryCacheAlignment)
    {
        int[] pageIdx2SizeTab = new int[nPSizes];
        int pageIdx = 0;
        for (int i = 0; i < nSizes; i++)
        {
            short[] sizeClass = sizeClasses[i];
            if (sizeClass[PAGESIZE_IDX] == Yes)
            {
                pageIdx2SizeTab[pageIdx++] = SizeOf(sizeClass, directMemoryCacheAlignment);
            }
        }
        return pageIdx2SizeTab;
    }

    private static int[] NewSize2IdxTab(int lookupMaxSize, short[][] sizeClasses)
    {
        int[] size2IdxTab = new int[lookupMaxSize >> LOG2_QUANTUM];
        int idx = 0;
        int size = 0;

        for (int i = 0; size <= lookupMaxSize; i++)
        {
            int log2Delta = sizeClasses[i][LOG2DELTA_IDX];
            int times = 1 << log2Delta - LOG2_QUANTUM;

            while (size <= lookupMaxSize && times-- > 0)
            {
                size2IdxTab[idx++] = i;
                size = idx + 1 << LOG2_QUANTUM;
            }
        }
        return size2IdxTab;
    }

    public int SizeIdx2Size(int sizeIdx)
    {
        if (sizeIdx < 0 || sizeIdx >= NSizes)
            throw new ArgumentOutOfRangeException(nameof(sizeIdx));
        return sizeIdx2SizeTab[sizeIdx];
    }

    public int SizeIdx2SizeCompute(int sizeIdx)
    {
        if (sizeIdx < 0 || sizeIdx >= NSizes)
            throw new ArgumentOutOfRangeException(nameof(sizeIdx));
        int group = sizeIdx >> LOG2_SIZE_CLASS_GROUP;
        int mod = sizeIdx & (1 << LOG2_SIZE_CLASS_GROUP) - 1;

        int groupSize = group == 0 ? 0 :
                1 << LOG2_QUANTUM + LOG2_SIZE_CLASS_GROUP - 1 << group;

        int shift = group == 0 ? 1 : group;
        int lgDelta = shift + LOG2_QUANTUM - 1;
        int modSize = mod + 1 << lgDelta;

        return groupSize + modSize;
    }

    public long PageIdx2Size(int pageIdx)
    {
        if (pageIdx < 0 || pageIdx >= NPSizes)
            throw new ArgumentOutOfRangeException(nameof(pageIdx));
        return pageIdx2SizeTab[pageIdx];
    }

    public long PageIdx2SizeCompute(int pageIdx)
    {
        if (pageIdx < 0 || pageIdx >= NPSizes)
            throw new ArgumentOutOfRangeException(nameof(pageIdx));
        int group = pageIdx >> LOG2_SIZE_CLASS_GROUP;
        int mod = pageIdx & (1 << LOG2_SIZE_CLASS_GROUP) - 1;

        long groupSize = group == 0 ? 0 :
                1L << PageShifts + LOG2_SIZE_CLASS_GROUP - 1 << group;

        int shift = group == 0 ? 1 : group;
        int log2Delta = shift + PageShifts - 1;
        int modSize = mod + 1 << log2Delta;

        return groupSize + modSize;
    }

    public int Size2SizeIdx(int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        if (size == 0)
        {
            return 0;
        }
        if (size > ChunkSize)
        {
            return NSizes;
        }

        size = AlignSizeIfNeeded(size, DirectMemoryCacheAlignment);

        if (size <= LookupMaxSize)
        {
            //size-1 / MIN_TINY
            return size2IdxTab[size - 1 >> LOG2_QUANTUM];
        }

        int x = BitOperations.Log2(((uint)size << 1) - 1u);
        int shift = x < LOG2_SIZE_CLASS_GROUP + LOG2_QUANTUM + 1
                ? 0 : x - (LOG2_SIZE_CLASS_GROUP + LOG2_QUANTUM);

        int group = shift << LOG2_SIZE_CLASS_GROUP;

        int log2Delta = x < LOG2_SIZE_CLASS_GROUP + LOG2_QUANTUM + 1
                ? LOG2_QUANTUM : x - LOG2_SIZE_CLASS_GROUP - 1;

        int mod = size - 1 >> log2Delta & (1 << LOG2_SIZE_CLASS_GROUP) - 1;

        return group + mod;
    }

    public int Pages2PageIdx(int pages)
    {
        return Pages2PageIdxCompute(pages, false);
    }

    public int Pages2PageIdxFloor(int pages)
    {
        return Pages2PageIdxCompute(pages, true);
    }

    private int Pages2PageIdxCompute(int pages, bool floor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pages);
        long pageSize = (long)pages << PageShifts;
        if (pageSize > ChunkSize)
        {
            return NPSizes;
        }

        int x = BitOperations.Log2((ulong)((pageSize << 1) - 1));

        int shift = x < LOG2_SIZE_CLASS_GROUP + PageShifts
                ? 0 : x - (LOG2_SIZE_CLASS_GROUP + PageShifts);

        int group = shift << LOG2_SIZE_CLASS_GROUP;

        int log2Delta = x < LOG2_SIZE_CLASS_GROUP + PageShifts + 1 ?
                PageShifts : x - LOG2_SIZE_CLASS_GROUP - 1;

        int mod = (int)((pageSize - 1 >> log2Delta) & (1 << LOG2_SIZE_CLASS_GROUP) - 1);

        int pageIdx = group + mod;

        if (floor && pageIdx2SizeTab[pageIdx] > pageSize)
        {
            pageIdx--;
        }

        return pageIdx;
    }

    // Round size up to the nearest multiple of alignment.
    private static int AlignSizeIfNeeded(int size, int directMemoryCacheAlignment)
    {
        if (directMemoryCacheAlignment <= 0)
        {
            return size;
        }
        int delta = size & directMemoryCacheAlignment - 1;
        return delta == 0 ? size : checked(size + directMemoryCacheAlignment - delta);
    }

    public int NormalizeSize(int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        if (size == 0)
        {
            return sizeIdx2SizeTab[0];
        }
        size = AlignSizeIfNeeded(size, DirectMemoryCacheAlignment);
        if (size <= LookupMaxSize)
        {
            int ret = sizeIdx2SizeTab[size2IdxTab[size - 1 >> LOG2_QUANTUM]];
            Debug.Assert(ret == NormalizeSizeCompute(size));
            return ret;
        }
        return NormalizeSizeCompute(size);
    }

    private static int NormalizeSizeCompute(int size)
    {
        int x = BitOperations.Log2(((uint)size << 1) - 1u);
        int log2Delta = x < LOG2_SIZE_CLASS_GROUP + LOG2_QUANTUM + 1
                ? LOG2_QUANTUM : x - LOG2_SIZE_CLASS_GROUP - 1;
        int delta = 1 << log2Delta;
        int deltaMask = delta - 1;
        return checked(size + deltaMask) & ~deltaMask;
    }

    private static int Log2(int value) => BitOperations.Log2((uint)value);
}
