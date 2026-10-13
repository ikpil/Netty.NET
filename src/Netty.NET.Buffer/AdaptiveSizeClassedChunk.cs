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
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Netty.NET.Buffer;

/**
 * Removes per-allocation retain()/release() atomic ops from the hot path by replacing ref counting
 * with a segment-count state machine. Atomics are only needed on the cold deallocation path
 * ({@link #markToDeallocate()}), which is rare for long-lived chunks that cycle segments many times.
 * The tradeoff is a {@link MpscIntQueue#size()} call (volatile reads, no RMW) per remaining segment
 * return after mark — acceptable since it avoids atomic RMWs entirely.
 * <p>
 * State transitions:
 * <ul>
 *   <li>{@link #AVAILABLE} (-1): chunk is in use, no deallocation tracking needed</li>
 *   <li>0..N: local free list size at the time {@link #markToDeallocate()} was called;
 *       used to track when all segments have been returned</li>
 *   <li>{@link #DEALLOCATED} (Integer.MIN_VALUE): all segments returned, chunk deallocated</li>
 * </ul>
 * <p>
 * Ordering: external {@link #releaseSegment} pushes to the MPSC queue (which has an implicit
 * StoreLoad barrier via its {@code offer()}), then reads {@code state} — this guarantees
 * visibility of any preceding {@link #markToDeallocate()} write.
 */
// CLR: ConcurrentQueue/Stack replace JVM primitive containers. A full fence after enqueue
// supplies the source's StoreLoad guarantee explicitly. Stack access/retirement stay on
// the physical owner thread; a future magazine must serialize shared chunk allocation.
internal sealed class AdaptiveSizeClassedChunk : AdaptiveChunk
{
    private const int Available = -1;
    // Integer.MIN_VALUE so that `DEALLOCATED + externalFreeList.size()` can never equal `segments`,
    // making late-arriving releaseSegment calls on external threads arithmetically harmless.
    private const int Deallocated = int.MinValue;
    private int _state = Available;
    internal int PurgeEpoch;
    internal long LastPurgeGeneration;
    internal long LastScanGeneration;
    private readonly int _segmentSize;
    private readonly int _segments;
    private readonly ConcurrentQueue<int> _externalFreeList = new();
    private readonly Stack<int> _localFreeList;
    private readonly Thread _ownerThread;

    internal AdaptiveSizeClassedChunk(ByteBuf storage, AdaptiveChunkRegistry registry, int segmentSize,
        bool threadLocal = false) : base(storage, registry, true)
    {
        if (Array.IndexOf(AdaptiveAllocationPolicy.GetSizeClasses(), segmentSize) < 0 || segmentSize > Capacity)
            throw new ArgumentOutOfRangeException(nameof(segmentSize));
        _segmentSize = segmentSize;
        _segments = Capacity / segmentSize;
        if (threadLocal)
        {
            _ownerThread = Thread.CurrentThread;
            _localFreeList = new Stack<int>(_segments);
            // The source stack starts at chunkSize, so an indivisible tail shifts
            // all local offsets by the remainder; shared FIFO offsets start at zero.
            for (int i = 1; i <= _segments; i++) _localFreeList.Push(Capacity - i * segmentSize);
        }
        else
        {
            for (int i = 0; i < _segments; i++) _externalFreeList.Enqueue(i * segmentSize);
        }
        Register();
    }

    private void CheckOwner()
    {
        if (_ownerThread != null && Thread.CurrentThread != _ownerThread)
            throw new InvalidOperationException("Only the physical owner thread may allocate, inspect or retire this local chunk.");
    }

    internal override AdaptiveChunkAllocation TryAllocate(int startingCapacity)
    {
        CheckOwner();
        ThrowIfRetired();
        if (startingCapacity < 0 || startingCapacity > _segmentSize)
            throw new ArgumentOutOfRangeException(nameof(startingCapacity));
        int startIndex;
        if (_localFreeList != null && _localFreeList.Count != 0) startIndex = _localFreeList.Pop();
        else if (!_externalFreeList.TryDequeue(out startIndex)) return null;
        AllocatedBytes += _segmentSize;
        try
        {
            return new AdaptiveChunkAllocation(this, Storage.AsMemory(startIndex, startingCapacity), startIndex);
        }
        catch
        {
            AllocatedBytes -= _segmentSize;
            ReturnOffset(startIndex);
            throw;
        }
    }

    // this can be used by the ConcurrentQueueChunkCache to find the first buffer to use:
    // it doesn't update the remaining capacity and it's not consider a single segmentSize
    // case as not suitable to be reused
    internal bool HasRemainingCapacity
    {
        get
        {
            CheckOwner();
            if (base.RemainingCapacity > 0) return true;
            return _localFreeList != null ? _localFreeList.Count != 0 : !_externalFreeList.IsEmpty;
        }
    }

    internal bool HasFullCapacity
    {
        get
        {
            CheckOwner();
            return _externalFreeList.Count + (_localFreeList?.Count ?? 0) == _segments;
        }
    }

    internal override int RemainingCapacity
    {
        get
        {
            CheckOwner();
            int remaining = base.RemainingCapacity;
            if (remaining > _segmentSize) return remaining;
            int updated = (_externalFreeList.Count + (_localFreeList?.Count ?? 0)) * _segmentSize;
            if (updated != remaining) AllocatedBytes = Capacity - updated;
            return updated;
        }
    }

    private void ReturnOffset(int offset)
    {
        if (_localFreeList != null && Thread.CurrentThread == _ownerThread) _localFreeList.Push(offset);
        else _externalFreeList.Enqueue(offset);
    }

    internal override void ReleaseSegment(int startIndex, int size)
    {
        if (_localFreeList != null && Thread.CurrentThread == _ownerThread)
        {
            _localFreeList.Push(startIndex);
            int state = Volatile.Read(ref _state);
            if (state == Available) return;
            int newLocalSize = _localFreeList.Count;
            bool alwaysTrue = Interlocked.CompareExchange(ref _state, newLocalSize, state) == state;
            Debug.Assert(alwaysTrue, "this shouldn't happen unless double release in the local free list");
            DeallocateIfNeeded(newLocalSize);
        }
        else
        {
            _externalFreeList.Enqueue(startIndex);
            // implicit StoreLoad barrier from MPSC offer()
            // CLR ConcurrentQueue does not promise that barrier; supply it explicitly.
            Thread.MemoryBarrier();
            int state = Volatile.Read(ref _state);
            if (state != Available) DeallocateIfNeeded(state);
        }
    }

    private void DeallocateIfNeeded(int localSize)
    {
        // Check if all segments have been returned.
        int totalFreeSegments = localSize + _externalFreeList.Count;
        if (totalFreeSegments == _segments && Interlocked.CompareExchange(ref _state, Deallocated, localSize) == localSize)
            Deallocate();
    }

    public override void Dispose()
    {
        CheckOwner();
        if (!TryRetire()) return;
        int localSize = _localFreeList?.Count ?? 0;
        // Exchange publishes retirement with the StoreLoad ordering used by the Java volatile write.
        Interlocked.Exchange(ref _state, localSize);
        DeallocateIfNeeded(localSize);
    }
}
