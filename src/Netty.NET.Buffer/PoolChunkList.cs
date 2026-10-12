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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Netty.NET.Buffer;

// Mutations run under the owning arena's Monitor. Enumeration captures membership,
// while each chunk metric remains live and synchronizes its own page bookkeeping.
internal sealed class PoolChunkList : IPoolChunkListMetric
{
    private readonly PoolArena _arena;
    private readonly PoolChunkList _nextList;
    private readonly int _minUsage;
    private readonly int _maxUsage;
    private readonly int _maxCapacity;
    private PoolChunk _head;
    private readonly int _freeMinThreshold;
    private readonly int _freeMaxThreshold;

    // This is only update once when create the linked like list of PoolChunkList in PoolArena constructor.
    private PoolChunkList _prevList;

    // TODO: Test if adding padding helps under contention
    //private long pad0, pad1, pad2, pad3, pad4, pad5, pad6, pad7;

    internal PoolChunkList(PoolArena arena, PoolChunkList nextList, int minUsage, int maxUsage, int chunkSize)
    {
        Debug.Assert(minUsage <= maxUsage);
        _arena = arena;
        _nextList = nextList;
        _minUsage = minUsage;
        _maxUsage = maxUsage;
        _maxCapacity = CalculateMaxCapacity(minUsage, chunkSize);

        // the thresholds are aligned with PoolChunk.usage() logic:
        // 1) basic logic: usage() = 100 - freeBytes * 100L / chunkSize
        //    so, for example: (usage() >= maxUsage) condition can be transformed in the following way:
        //      100 - freeBytes * 100L / chunkSize >= maxUsage
        //      freeBytes <= chunkSize * (100 - maxUsage) / 100
        //      let freeMinThreshold = chunkSize * (100 - maxUsage) / 100, then freeBytes <= freeMinThreshold
        //
        //  2) usage() returns an int value and has a floor rounding during a calculation,
        //     to be aligned absolute thresholds should be shifted for "the rounding step":
        //       freeBytes * 100 / chunkSize < 1
        //       the condition can be converted to: freeBytes < 1 * chunkSize / 100
        //     this is why we have + 0.99999999 shifts. A example why just +1 shift cannot be used:
        //       freeBytes = 16777216 == freeMaxThreshold: 16777216, usage = 0 < minUsage: 1, chunkSize: 16777216
        //     At the same time we want to have zero thresholds in case of (maxUsage == 100) and (minUsage == 100).
        //
        // CLR: Java narrows out-of-range doubles by saturation, including qInit/q100.
        // Preserve that conversion explicitly under both checked and unchecked builds.
        _freeMinThreshold = maxUsage == 100 ? 0 : Saturate(chunkSize * (100.0 - maxUsage + 0.99999999) / 100L);
        _freeMaxThreshold = minUsage == 100 ? 0 : Saturate(chunkSize * (100.0 - minUsage + 0.99999999) / 100L);
    }

    private static int Saturate(double value)
        => value >= int.MaxValue ? int.MaxValue : value <= int.MinValue ? int.MinValue : (int)value;

    /**
     * Calculates the maximum capacity of a buffer that will ever be possible to allocate out of the {@link PoolChunk}s
     * that belong to the {@link PoolChunkList} with the given {@code minUsage} and {@code maxUsage} settings.
     */
    private static int CalculateMaxCapacity(int minUsage, int chunkSize)
    {
        minUsage = MinUsage0(minUsage);
        if (minUsage == 100)
        {
            // If the minUsage is 100 we can not allocate anything out of this list.
            return 0;
        }
        // Calculate the maximum amount of bytes that can be allocated from a PoolChunk in this PoolChunkList.
        //
        // As an example:
        // - If a PoolChunkList has minUsage == 25 we are allowed to allocate at most 75% of the chunkSize because
        //   this is the maximum amount available in any PoolChunk in this PoolChunkList.
        return (int)(chunkSize * (100L - minUsage) / 100L);
    }

    internal void SetPrevious(PoolChunkList prevList)
    {
        Debug.Assert(_prevList == null);
        _prevList = prevList;
    }

    internal bool TryAllocate(int sizeIdx, out PoolChunk chunk, out long handle)
    {
        Debug.Assert(System.Threading.Monitor.IsEntered(_arena.SyncRoot));
        int normCapacity = _arena.SizeClass.SizeIdx2Size(sizeIdx);
        chunk = null;
        handle = -1;
        if (normCapacity > _maxCapacity)
        {
            // Either this PoolChunkList is empty or the requested capacity is larger then the capacity which can
            // be handled by the PoolChunks that are contained in this PoolChunkList.
            return false;
        }
        for (PoolChunk cur = _head; cur != null; cur = cur.Next)
        {
            handle = _arena.AllocateHandle(cur, sizeIdx, normCapacity, out chunk);
            if (handle >= 0)
            {
                Debug.Assert(ReferenceEquals(cur, chunk));
                if (cur.FreeBytes <= _freeMinThreshold)
                {
                    Remove(cur);
                    _nextList.Add(cur);
                }
                return true;
            }
        }
        return false;
    }

    internal bool Free(PoolChunk chunk, long handle)
    {
        Debug.Assert(System.Threading.Monitor.IsEntered(_arena.SyncRoot));
        PoolArena.FreeHandle(chunk, handle);
        if (chunk.FreeBytes > _freeMaxThreshold)
        {
            Remove(chunk);
            // Move the PoolChunk down the PoolChunkList linked-list.
            return Move0(chunk);
        }
        return true;
    }

    private bool Move(PoolChunk chunk)
    {
        Debug.Assert(chunk.Usage < _maxUsage);
        if (chunk.FreeBytes > _freeMaxThreshold)
        {
            // Move the PoolChunk down the PoolChunkList linked-list.
            return Move0(chunk);
        }
        // PoolChunk fits into this PoolChunkList, adding it here.
        Add0(chunk);
        return true;
    }

    /**
     * Moves the {@link PoolChunk} down the {@link PoolChunkList} linked-list so it will end up in the right
     * {@link PoolChunkList} that has the correct minUsage / maxUsage in respect to {@link PoolChunk#usage()}.
     */
    private bool Move0(PoolChunk chunk)
    {
        if (_prevList == null)
        {
            // There is no previous PoolChunkList so return false which result in having the PoolChunk destroyed and
            // all memory associated with the PoolChunk will be released.
            Debug.Assert(chunk.Usage == 0);
            return false;
        }
        return _prevList.Move(chunk);
    }

    internal void Add(PoolChunk chunk)
    {
        Debug.Assert(System.Threading.Monitor.IsEntered(_arena.SyncRoot));
        if (chunk.FreeBytes <= _freeMinThreshold)
        {
            _nextList.Add(chunk);
            return;
        }
        Add0(chunk);
    }

    /**
     * Adds the {@link PoolChunk} to this {@link PoolChunkList}.
     */
    private void Add0(PoolChunk chunk)
    {
        chunk.Parent = this;
        chunk.Prev = null;
        chunk.Next = _head;
        if (_head != null) _head.Prev = chunk;
        _head = chunk;
    }

    private void Remove(PoolChunk cur)
    {
        if (cur == _head)
        {
            _head = cur.Next;
            if (_head != null) _head.Prev = null;
        }
        else
        {
            PoolChunk next = cur.Next;
            cur.Prev.Next = next;
            if (next != null) next.Prev = cur.Prev;
        }
        // CLR: detached chunks do not retain stale list/neighbor references.
        cur.Parent = null;
        cur.Prev = cur.Next = null;
    }

    public int MinUsage => MinUsage0(_minUsage);
    public int MaxUsage => Math.Min(_maxUsage, 100);
    private static int MinUsage0(int value) => Math.Max(1, value);

    public IEnumerator<IPoolChunkMetric> GetEnumerator()
    {
        lock (_arena.SyncRoot)
        {
            var metrics = new List<IPoolChunkMetric>();
            for (PoolChunk cur = _head; cur != null; cur = cur.Next) metrics.Add(cur);
            return metrics.GetEnumerator();
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString()
    {
        lock (_arena.SyncRoot)
            return _head == null ? "none" : string.Join(Environment.NewLine, this.Select(chunk => chunk.ToString()));
    }

    internal void Destroy()
    {
        Debug.Assert(System.Threading.Monitor.IsEntered(_arena.SyncRoot));
        PoolChunk chunk = _head;
        while (chunk != null)
        {
            PoolChunk next = chunk.Next;
            Remove(chunk);
            _arena.DestroyChunk(chunk);
            chunk = next;
        }
    }
}
