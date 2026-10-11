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
using System.Threading;

namespace Netty.NET.Buffer;

/// <remarks>Mutations require the owning size-class head lock. Chunk storage/destruction
/// remains separate from this bitmap and available-list implementation.</remarks>
internal sealed class PoolSubpage : IPoolSubpageMetric
{
    internal readonly PoolChunk Chunk;
    internal readonly SizeClasses SizeClass;
    internal readonly int HeadIndex;
    internal readonly PoolSubpage Head;
    internal readonly object SyncRoot;
    private readonly int _pageShifts;
    private readonly int _runOffset;
    private readonly int _runSize;
    private readonly ulong[] _bitmap;
    private readonly int _maxNumElems;
    private int _nextAvail;
    private int _numAvail;
    internal PoolSubpage Prev;
    internal PoolSubpage Next;
    internal bool DoNotDestroy;

    // TODO: Test if adding padding helps under contention
    //private long pad0, pad1, pad2, pad3, pad4, pad5, pad6, pad7;

    /** Special constructor that creates a linked list head */
    internal PoolSubpage(int headIndex, SizeClasses sizeClass)
    {
        ArgumentNullException.ThrowIfNull(sizeClass);
        if (headIndex < 0 || headIndex >= sizeClass.NSubpages)
            throw new ArgumentOutOfRangeException(nameof(headIndex));
        SizeClass = sizeClass;
        HeadIndex = headIndex;
        Head = this;
        SyncRoot = new object();
        _pageShifts = _runOffset = _runSize = ElementSize = -1;
        // CLR: initialize the sentinel here rather than exposing the Java arena's two-step setup.
        Prev = Next = this;
    }

    internal PoolSubpage(PoolSubpage head, PoolChunk chunk, int runOffset, int runSize, int elemSize)
    {
        ArgumentNullException.ThrowIfNull(head);
        ArgumentNullException.ThrowIfNull(chunk);
        if (head.Chunk != null || !ReferenceEquals(head.SizeClass, chunk.SizeClass))
            throw new ArgumentException("The head must belong to this chunk's size-class domain.", nameof(head));
        if (runOffset < 0 || runSize <= 0 || runSize % chunk.PageSize != 0 ||
            (long)runOffset * chunk.PageSize + runSize > chunk.ChunkSize)
            throw new ArgumentOutOfRangeException(nameof(runSize));
        if (elemSize <= 0 || elemSize > runSize)
            throw new ArgumentOutOfRangeException(nameof(elemSize));
        Debug.Assert(Monitor.IsEntered(head.SyncRoot));
        Head = head;
        SyncRoot = head.SyncRoot;
        SizeClass = head.SizeClass;
        HeadIndex = head.HeadIndex;
        Chunk = chunk;
        _pageShifts = chunk.PageShifts;
        _runOffset = runOffset;
        _runSize = runSize;
        ElementSize = elemSize;
        DoNotDestroy = true;
        _maxNumElems = _numAvail = runSize / elemSize;
        _bitmap = new ulong[(_maxNumElems + 63) >> 6];
        _nextAvail = 0;
        AddToPool();
    }

    internal static PoolSubpage[] CreatePoolHeads(SizeClasses sizes)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        var heads = new PoolSubpage[sizes.NSubpages];
        for (int i = 0; i < heads.Length; i++)
            heads[i] = new PoolSubpage(i, sizes);
        return heads;
    }

    /**
     * Returns the bitmap index of the subpage allocation.
     */
    internal long Allocate()
    {
        Debug.Assert(Monitor.IsEntered(SyncRoot));
        if (_numAvail == 0 || !DoNotDestroy)
            return -1;
        int bitmapIdx = GetNextAvail();
        if (bitmapIdx < 0)
        {
            RemoveFromPool(); // Subpage appear to be in an invalid state. Remove to prevent repeated errors.
            throw new InvalidOperationException($"No next available bitmap index found (bitmapIdx = {bitmapIdx}), " +
                $"even though there are supposed to be (numAvail = {_numAvail}) out of (maxNumElems = {_maxNumElems}) available indexes.");
        }
        int q = bitmapIdx >> 6;
        int r = bitmapIdx & 63;
        Debug.Assert((_bitmap[q] >> r & 1) == 0);
        _bitmap[q] |= 1UL << r;
        if (--_numAvail == 0)
            RemoveFromPool();
        return ToHandle(bitmapIdx);
    }

    /**
     * @return {@code true} if this subpage is in use.
     *         {@code false} if this subpage is not used by its chunk and thus it's OK to be released.
     */
    internal bool Free(int bitmapIdx)
    {
        Debug.Assert(Monitor.IsEntered(SyncRoot));
        if (bitmapIdx < 0 || bitmapIdx >= _maxNumElems)
            throw new ArgumentOutOfRangeException(nameof(bitmapIdx));
        int q = bitmapIdx >> 6;
        int r = bitmapIdx & 63;
        if (!DoNotDestroy || (_bitmap[q] >> r & 1) == 0)
            throw new InvalidOperationException("The subpage slot is not allocated.");
        _bitmap[q] ^= 1UL << r;
        _nextAvail = bitmapIdx;
        if (_numAvail++ == 0)
        {
            AddToPool();
            /* When maxNumElems == 1, the maximum numAvail is also 1.
             * Each of these PoolSubpages will go in here when they do free operation.
             * If they return true directly from here, then the rest of the code will be unreachable
             * and they will not actually be recycled. So return true only on maxNumElems > 1. */
            if (_maxNumElems > 1)
                return true;
        }
        if (_numAvail != _maxNumElems)
            return true;

        // Subpage not in use (numAvail == maxNumElems)
        if (Prev == Next)
        {
            // Do not remove if this subpage is the only one left in the pool.
            return true;
        }
        // Remove this subpage from the pool if there are other subpages left in the pool.
        DoNotDestroy = false;
        RemoveFromPool();
        return false;
    }

    private void AddToPool()
    {
        Debug.Assert(Prev == null && Next == null);
        Prev = Head;
        Next = Head.Next;
        Next.Prev = this;
        Head.Next = this;
    }

    private void RemoveFromPool()
    {
        Debug.Assert(Prev != null && Next != null);
        Prev.Next = Next;
        Next.Prev = Prev;
        Next = Prev = null;
    }

    private int GetNextAvail()
    {
        int nextAvail = _nextAvail;
        if (nextAvail >= 0)
        {
            _nextAvail = -1;
            return nextAvail;
        }
        for (int i = 0; i < _bitmap.Length; i++)
        {
            ulong bits = _bitmap[i];
            if (~bits == 0)
                continue;
            int baseVal = i << 6;
            for (int j = 0; j < 64; j++, bits >>= 1)
            {
                if ((bits & 1) != 0)
                    continue;
                int val = baseVal | j;
                return val < _maxNumElems ? val : -1;
            }
        }
        return -1;
    }

    private long ToHandle(int bitmapIdx) => (long)_runOffset << PoolChunk.RUN_OFFSET_SHIFT |
        (long)(_runSize >> _pageShifts) << PoolChunk.SIZE_SHIFT | 1L << PoolChunk.IS_USED_SHIFT |
        1L << PoolChunk.IS_SUBPAGE_SHIFT | (uint)bitmapIdx;

    public override string ToString()
    {
        int numAvail;
        if (Chunk == null)
        {
            // This is the head so there is no need to synchronize at all as these never change.
            numAvail = 0;
        }
        else
        {
            bool doNotDestroy;
            lock (SyncRoot)
            {
                doNotDestroy = DoNotDestroy;
                numAvail = _numAvail;
            }
            if (!doNotDestroy)
            {
                // Not used for creating the String.
                return $"({_runOffset}: not in use)";
            }
        }
        return $"({_runOffset}: {_maxNumElems - numAvail}/{_maxNumElems}, offset: {_runOffset}, length: {_runSize}, elemSize: {ElementSize})";
    }

    public int MaxNumElements => _maxNumElems;
    internal int RunSize => _runSize;
    public int NumAvailable
    {
        get
        {
            if (Chunk == null)
            {
                // It's the head.
                return 0;
            }
            lock (SyncRoot)
                return _numAvail;
        }
    }
    public int ElementSize { get; }
    // CLR: a sentinel has no page; Java's 1 << -1 returns an incidental negative value.
    public int PageSize => Chunk == null ? 0 : 1 << _pageShifts;
    internal bool IsDoNotDestroy
    {
        get
        {
            if (Chunk == null)
            {
                // It's the head.
                return true;
            }
            lock (SyncRoot)
                return DoNotDestroy;
        }
    }
}
