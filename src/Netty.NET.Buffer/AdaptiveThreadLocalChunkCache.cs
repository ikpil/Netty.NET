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
using System.Diagnostics;
using System.Text;

namespace Netty.NET.Buffer;

// CLR: the pool lifetime gate serializes owner operations and off-thread disposal.
/**
 * Ring buffer cache for thread-local chunk reuse (SPSC — only the owner thread accesses it).
 *
 * <p>Logical layout after purge:
 * <pre>
 *   head                          tail
 *   v                             v
 *   [..., notEmpty, notEmpty, ..., empty, empty, ..., null, ...]
 *        |--- notEmptyCount ---|--- emptyCount --|
 *        |------------ count ------------------|
 * </pre>
 *
 * <p>Physical layout when the ring wraps:
 * <pre>
 *   0         tail          head          length
 *   v         v             v             v
 *   [...tail] [  unused  ]  [head................]
 *             ^             |--- content wraps ---|
 *             wrap point
 * </pre>
 *
 * <p><b>scanForCapacity</b> — O(1) fast path takes from head while {@code notEmptyCount > 0}:
 * <pre>
 *   before: notEmptyCount=2, count=5
 *   [NE, NE, E, E, E, _, _, _]
 *    ^head            ^tail
 *
 *   after: returns NE, notEmptyCount=1, count=4
 *   [_,  NE, E, E, E, _, _, _]
 *        ^head        ^tail
 * </pre>
 * Fallback when {@code notEmptyCount == 0}: linear scan of the empty zone for chunks
 * that gained capacity from external segment returns.
 *
 * <p><b>offerChunk</b> — write at tail, grow (double + linearize) if full:
 * <pre>
 *   before: count=4
 *   [_,  NE, E, E, E, _, _, _]
 *        ^head        ^tail
 *
 *   after: count=5
 *   [_,  NE, E, E, E, X, _, _]
 *        ^head           ^tail
 * </pre>
 *
 * <p><b>runPurgeScan</b> (every {@link #CHUNK_PURGE_POLLS_THREAD_LOCAL} polls) —
 * two passes. Pass 1: age idle chunks (full → epoch++, non-full → epoch=0), evict
 * past threshold, compact survivors (nulls stale slots inline). Pass 2: partition
 * hasCap to front / noCap to back, then three-way Dutch-flag within hasCap into
 * [epoch=0 | 0&lt;epoch&lt;threshold | epoch&gt;=threshold]. Chunks with epoch&gt;=threshold
 * are placed at the back of hasCap so scan doesn't reach them — they age to
 * threshold+1 and get evicted. Never selects — selection is always
 * {@code scanForCapacity}.
 *
 * <p>Case 1 — no eviction, an empty chunk gained capacity externally (common):
 * <pre>
 *   before (E* gained capacity since last purge):
 *   [NE, NE, E*, E, _, _, _, _]
 *    ^head            ^tail
 *    notEmptyCount=2
 *
 *   pass 1: age idle chunks. None past threshold. No compaction needed.
 *   pass 2 (partition): E* now has capacity → placed in notEmpty zone.
 *
 *   after:
 *   [NE, NE, E*, E, _, _, _, _]
 *    ^head            ^tail
 *    notEmptyCount=3
 * </pre>
 *
 * <p>Case 2 — eviction (uncommon, burst wind-down):
 * <pre>
 *   before (ring wraps, IDLE* = idle past threshold):
 *   [E, NE, _,  IDLE*, NE, E, E, NE]
 *          ^tail ^head
 *
 *   pass 1: IDLE* evicted (markToDeallocate), survivors compacted, stale slots nulled.
 *   [_, _, _,  NE, E, E, NE, E]
 *     ^tail    ^head
 *              |--- kept=6 ---|
 *
 *   pass 2 (partition): [epoch=0 hasCap | 0&lt;epoch&lt;T hasCap | epoch&gt;=T hasCap | noCap].
 *   [_, _, _,  NE, NE, E, E, E]
 *     ^tail    ^head
 *              notEmptyCount=2, count=6
 * </pre>
 * Idle chunks ({@code remainingCapacity == capacity}) age via purgeEpoch and are evicted
 * past threshold, but at least {@code purgeRetentionFloor} chunks are always retained.
 */
internal sealed class AdaptiveThreadLocalChunkCache : IAdaptiveChunkCache
{
    private AdaptiveSizeClassedChunk[] _chunks; // package-private for testing
    private int _head;
    private int _tail;
    private int _count;
    private int _notEmptyCount;
    private long _purgeBudget;
    private readonly long _purgePolls;
    private readonly int _purgeThreshold;
    private bool _disposed;
    internal int Count => _count;
    internal int RingLength => _chunks.Length;
    internal readonly int MaxCachedChunks; // package-private for testing
    internal readonly int PurgeRetentionFloor; // package-private for testing

    internal AdaptiveThreadLocalChunkCache(int chunkSize, long purgePolls, int purgeThreshold,
        int maxBytes, int minBytes)
        {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(purgePolls, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(purgeThreshold, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(minBytes, 1);
        _purgePolls = purgePolls;
        _purgeThreshold = purgeThreshold;
        _chunks = new AdaptiveSizeClassedChunk[8];
        _purgeBudget = _purgePolls;
        MaxCachedChunks = Math.Max(1, maxBytes / chunkSize);
        PurgeRetentionFloor = Math.Min(MaxCachedChunks,
                Math.Max(1, minBytes / chunkSize));
    }

    internal AdaptiveSizeClassedChunk ForcePurge()
    {
        _purgeBudget = 1;
        return (AdaptiveSizeClassedChunk)PollChunk(0);
    }

    public AdaptiveChunk PollChunk(int size)
    {
        if (_disposed) return null;
        if (--_purgeBudget == 0)
        {
            RunPurgeScan();
        }
        return ScanForCapacity();
    }

    private AdaptiveSizeClassedChunk ScanForCapacity()
    {
        if (_notEmptyCount > 0)
        {
            AdaptiveSizeClassedChunk chunk = _chunks[_head];
            Debug.Assert(chunk.HasRemainingCapacity);
            chunk.PurgeEpoch = 0;
            _chunks[_head] = null;
            _head = (_head + 1) & (_chunks.Length - 1);
            _count--;
            _notEmptyCount--;
            return chunk;
        }
        return ScanForCapacityFallback();
    }

    private AdaptiveSizeClassedChunk ScanForCapacityFallback()
    {
        int mask = _chunks.Length - 1;
        int emptyCount = _count - _notEmptyCount;
        int pos = (_head + _notEmptyCount) & mask;
        for (int i = 0; i < emptyCount; i++)
        {
            AdaptiveSizeClassedChunk chunk = _chunks[pos];
            if (chunk.HasRemainingCapacity)
            {
                chunk.PurgeEpoch = 0;
                int lastIdx = (_tail - 1) & mask;
                _chunks[pos] = _chunks[lastIdx];
                _chunks[lastIdx] = null;
                _tail = lastIdx;
                _count--;
                return chunk;
            }
            pos = (pos + 1) & mask;
        }
        return null;
    }

    private void RunPurgeScan()
    {
        int mask = _chunks.Length - 1;
        int kept = 0;
        int survivors = _count;
        for (int i = 0; i < _count; i++)
        {
            int readIdx = (_head + i) & mask;
            AdaptiveSizeClassedChunk chunk = _chunks[readIdx];
            if (chunk.PurgeEpoch > 0)
            {
                Debug.Assert(chunk.HasFullCapacity);
                chunk.PurgeEpoch = unchecked(chunk.PurgeEpoch + 1);
                if (chunk.PurgeEpoch > _purgeThreshold && survivors > PurgeRetentionFloor)
                {
                    chunk.Dispose();
                    _chunks[readIdx] = null;
                    survivors--;
                    continue;
                }
            }
            else if (chunk.HasFullCapacity)
            {
                chunk.PurgeEpoch = 1;
            }
            int writeIdx = (_head + kept) & mask;
            if (writeIdx != readIdx)
            {
                _chunks[writeIdx] = chunk;
                _chunks[readIdx] = null;
            }
            kept++;
        }
        _tail = (_head + kept) & mask;
        _count = kept;
        Partition(kept);
        _purgeBudget = _purgePolls;
    }

    private void Partition(int size)
    {
        int mask = _chunks.Length - 1;
        // Pass 1: hasCapacity to front, noCapacity to back.
        int lo = 0;
        int hi = size - 1;
        while (lo <= hi)
        {
            int loIdx = (_head + lo) & mask;
            if (_chunks[loIdx].HasRemainingCapacity)
            {
                lo++;
            }
            else
            {
                int hiIdx = (_head + hi) & mask;
                AdaptiveSizeClassedChunk tmp = _chunks[loIdx];
                _chunks[loIdx] = _chunks[hiIdx];
                _chunks[hiIdx] = tmp;
                hi--;
            }
        }
        _notEmptyCount = lo;
        // Pass 2: three-way Dutch-flag within notEmpty:
        //   [epoch=0 | 0<epoch<threshold | epoch>=threshold]
        //
        // Epoch=0 (recently used) at head — scan picks these first.
        // Epoch>=threshold (about to be evicted) at back — scan doesn't reach them,
        // so they age one more cycle to threshold+1 and get evicted.
        //
        // This ordering guarantees convergence regardless of count/polls ratio.
        // Without it (e.g., a simple epoch=0/epoch>0 split with mid++), when
        // count/polls == threshold the groups rotate perfectly and max epoch never
        // exceeds threshold — eviction stalls at threshold * polls chunks.
        int elo = 0;
        int emid = 0;
        int ehi = lo - 1;
        while (emid <= ehi)
        {
            int emidIdx = (_head + emid) & mask;
            AdaptiveSizeClassedChunk c = _chunks[emidIdx];
            if (c.PurgeEpoch == 0)
            {
                if (elo != emid)
                {
                    int eloIdx = (_head + elo) & mask;
                    _chunks[emidIdx] = _chunks[eloIdx];
                    _chunks[eloIdx] = c;
                }
                elo++;
                emid++;
            }
            else if (c.PurgeEpoch < _purgeThreshold)
            {
                emid++;
            }
            else
            {
                int ehiIdx = (_head + ehi) & mask;
                _chunks[emidIdx] = _chunks[ehiIdx];
                _chunks[ehiIdx] = c;
                ehi--;
            }
        }
    }

    public bool OfferChunk(AdaptiveChunk chunk)
    {
        if (_disposed || _count >= MaxCachedChunks)
        {
            return false;
        }
        if (_count == _chunks.Length)
        {
            AdaptiveSizeClassedChunk[] newChunks = new AdaptiveSizeClassedChunk[_chunks.Length * 2];
            for (int i = 0; i < _count; i++)
            {
                newChunks[i] = _chunks[(_head + i) & (_chunks.Length - 1)];
            }
            _chunks = newChunks;
            _head = 0;
            _tail = _count;
        }
        _chunks[_tail] = (AdaptiveSizeClassedChunk)chunk;
        _tail = (_tail + 1) & (_chunks.Length - 1);
        _count++;
        return true;
    }

    public override string ToString()
    {
        int mask = _chunks.Length - 1;
        var sb = new StringBuilder();
        sb.Append("ThreadLocalCache[head=").Append(_head).Append(", tail=").Append(_tail)
            .Append(", count=").Append(_count).Append(", notEmpty=").Append(_notEmptyCount)
            .Append(", length=").Append(_chunks.Length).Append("]\n  ");
        for (int i = 0; i < _count; i++)
        {
            if (i > 0) sb.Append(", ");
            if (i == _notEmptyCount) sb.Append("| ");
            var c = _chunks[(_head + i) & mask];
            string region = i < _notEmptyCount ? "notEmpty" : "empty";
            string actual = c == null ? "null" : c.HasRemainingCapacity ? "hasCap" : "noCap";
            sb.Append('[').Append(region).Append(':').Append(actual)
                .Append(",ep=").Append(c == null ? -1 : c.PurgeEpoch).Append(']');
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        _disposed = true;
        int mask = _chunks.Length - 1;
        for (int i = 0; i < _count; i++)
        {
            int idx = (_head + i) & mask;
            _chunks[idx].Dispose();
            _chunks[idx] = null;
        }
        _head = 0;
        _tail = 0;
        _count = 0;
        _notEmptyCount = 0;
    }

    public bool IsEmpty => _count == 0;
}
