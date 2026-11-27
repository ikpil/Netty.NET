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
using System.Diagnostics;
using System.Threading;

namespace Netty.NET.Buffer;

internal sealed partial class AdaptivePoolingAllocator
{
    private sealed class IntStack
    {
        private readonly int[] _stack;
        private int _top;

        internal IntStack(int[] initialValues)
        {
            _stack = initialValues;
            _top = initialValues.Length - 1;
        }

        public bool IsEmpty()
        {
            return _top == -1;
        }

        public int Pop()
        {
            int last = _stack[_top];
            _top--;
            return last;
        }

        public void Push(int value)
        {
            _stack[_top + 1] = value;
            _top++;
        }

        public int Size()
        {
            return _top + 1;
        }
    }

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
    // CLR: ConcurrentQueue replaces the JVM MPSC queue. A full fence after enqueue
    // supplies the source's StoreLoad guarantee explicitly. Allocation stays on the physical
    // owner thread. Local returns and retirement share a gate so allocator Dispose may run
    // elsewhere after its lifetime gate has quiesced allocations and cache inspection.
    // CLR: the constructor takes the segment geometry and ownership instead of the magazine and
    // controller, so chunks also exist without a magazine; it builds the free lists the source's
    // SizeClassChunkController builds (createFreeList/createEmptyFreeList/createLocalFreeList).
    internal sealed class SizeClassedChunk : Chunk
    {
        private const int FREE_LIST_EMPTY = -1;
        private const int AVAILABLE = -1;
        // Integer.MIN_VALUE so that `DEALLOCATED + externalFreeList.size()` can never equal `segments`,
        // making late-arriving releaseSegment calls on external threads arithmetically harmless.
        private const int DEALLOCATED = int.MinValue;
        private int _state = AVAILABLE;
        private readonly int _segments;
        private readonly int _segmentSize;
        private readonly ConcurrentQueue<int> _externalFreeList;
        private readonly IntStack _localFreeList;
        private Thread _ownerThread;
        private readonly object _localReturnLock = new();
        internal int PurgeEpoch;
        internal long LastPurgeGeneration;
        internal long LastScanGeneration;

        internal SizeClassedChunk(ByteBuf @delegate, ChunkRegistry chunkRegistry, int segmentSize,
            bool threadLocal = false) : base(@delegate, chunkRegistry, true)
        {
            if (Array.IndexOf(SIZE_CLASSES, segmentSize) < 0 || segmentSize > Capacity)
                throw new ArgumentOutOfRangeException(nameof(segmentSize));
            _segmentSize = segmentSize;
            _segments = Capacity / segmentSize;
            if (!threadLocal)
            {
                _externalFreeList = CreateFreeList();
                _localFreeList = null;
            }
            else
            {
                _ownerThread = Thread.CurrentThread;
                _externalFreeList = CreateEmptyFreeList();
                _localFreeList = CreateLocalFreeList();
            }
            Register();
        }

        private static ConcurrentQueue<int> CreateEmptyFreeList()
        {
            return new ConcurrentQueue<int>();
        }

        private ConcurrentQueue<int> CreateFreeList()
        {
            int segmentsCount = _segments;
            var freeList = new ConcurrentQueue<int>();
            int segmentOffset = 0;
            for (int i = 0; i < segmentsCount; i++)
            {
                freeList.Enqueue(segmentOffset);
                segmentOffset += _segmentSize;
            }
            return freeList;
        }

        private IntStack CreateLocalFreeList()
        {
            int segmentsCount = _segments;
            // The source stack starts at chunkSize, so an indivisible tail shifts
            // all local offsets by the remainder; shared FIFO offsets start at zero.
            int segmentOffset = Capacity;
            int[] offsets = new int[segmentsCount];
            for (int i = 0; i < segmentsCount; i++)
            {
                segmentOffset -= _segmentSize;
                offsets[i] = segmentOffset;
            }
            return new IntStack(offsets);
        }

        private void CheckOwner()
        {
            if (_ownerThread != null && Thread.CurrentThread != _ownerThread)
                throw new InvalidOperationException("Only the physical owner thread may allocate, inspect or retire this local chunk.");
        }

        // CLR: the Memory-based counterpart of readInitInto (see Chunk.TryAllocate).
        internal override AdaptiveChunkAllocation TryAllocate(int startingCapacity)
        {
            CheckOwner();
            ThrowIfRetired();
            if (startingCapacity < 0 || startingCapacity > _segmentSize)
                throw new ArgumentOutOfRangeException(nameof(startingCapacity));
            int startIndex = NextAvailableSegmentOffset();
            if (startIndex == FREE_LIST_EMPTY)
            {
                return null;
            }
            AllocatedBytes += _segmentSize;
            try
            {
                return new AdaptiveChunkAllocation(this, Storage.AsMemory(startIndex, startingCapacity), startIndex);
            }
            catch
            {
                AllocatedBytes -= _segmentSize;
                ReleaseSegmentOffsetIntoFreeList(startIndex);
                throw;
            }
        }

        private int NextAvailableSegmentOffset()
        {
            int startIndex;
            IntStack localFreeList = _localFreeList;
            if (localFreeList != null)
            {
                // CLR: CheckOwner has enforced the source's owner-thread assertion.
                if (localFreeList.IsEmpty())
                {
                    startIndex = _externalFreeList.TryDequeue(out int polled) ? polled : FREE_LIST_EMPTY;
                }
                else
                {
                    startIndex = localFreeList.Pop();
                }
            }
            else
            {
                startIndex = _externalFreeList.TryDequeue(out int polled) ? polled : FREE_LIST_EMPTY;
            }
            return startIndex;
        }

        // this can be used by the ConcurrentQueueChunkCache to find the first buffer to use:
        // it doesn't update the remaining capacity and it's not consider a single segmentSize
        // case as not suitable to be reused
        internal bool HasRemainingCapacity
        {
            get
            {
                CheckOwner();
                int remaining = base.RemainingCapacity;
                if (remaining > 0)
                {
                    return true;
                }
                if (_localFreeList != null)
                {
                    return !_localFreeList.IsEmpty();
                }
                return !_externalFreeList.IsEmpty;
            }
        }

        internal bool HasFullCapacity
        {
            get
            {
                CheckOwner();
                int free = _externalFreeList.Count;
                IntStack local = _localFreeList;
                if (local != null)
                {
                    free += local.Size();
                }
                return free == _segments;
            }
        }

        internal override int RemainingCapacity
        {
            get
            {
                CheckOwner();
                int remaining = base.RemainingCapacity;
                return remaining > _segmentSize ? remaining : UpdateRemainingCapacity(remaining);
            }
        }

        private int UpdateRemainingCapacity(int snapshotted)
        {
            int freeSegments = _externalFreeList.Count;
            IntStack localFreeList = _localFreeList;
            if (localFreeList != null)
            {
                freeSegments += localFreeList.Size();
            }
            int updated = freeSegments * _segmentSize;
            if (updated != snapshotted)
            {
                AllocatedBytes = Capacity - updated;
            }
            return updated;
        }

        private void ReleaseSegmentOffsetIntoFreeList(int startIndex)
        {
            IntStack localFreeList = _localFreeList;
            if (localFreeList != null && Thread.CurrentThread == _ownerThread)
            {
                localFreeList.Push(startIndex);
            }
            else
            {
                _externalFreeList.Enqueue(startIndex);
            }
        }

        internal override void ReleaseSegment(int startIndex, int size)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            IntStack localFreeList = _localFreeList;
            if (localFreeList != null)
            {
                lock (_localReturnLock)
                {
                    if (Thread.CurrentThread == _ownerThread)
                    {
                        localFreeList.Push(startIndex);
                        int state = Volatile.Read(ref _state);
                        if (state != AVAILABLE)
                        {
                            UpdateStateOnLocalReleaseSegment(state, localFreeList);
                        }
                        return;
                    }
                }
            }
            _externalFreeList.Enqueue(startIndex);
            // implicit StoreLoad barrier from MPSC offer()
            // CLR ConcurrentQueue does not promise that barrier; supply it explicitly.
            Thread.MemoryBarrier();
            int externalState = Volatile.Read(ref _state);
            if (externalState != AVAILABLE)
            {
                DeallocateIfNeeded(externalState);
            }
        }

        private void UpdateStateOnLocalReleaseSegment(int previousLocalSize, IntStack localFreeList)
        {
            int newLocalSize = localFreeList.Size();
            bool alwaysTrue = Interlocked.CompareExchange(ref _state, newLocalSize, previousLocalSize) == previousLocalSize;
            Debug.Assert(alwaysTrue, "this shouldn't happen unless double release in the local free list");
            DeallocateIfNeeded(newLocalSize);
        }

        private void DeallocateIfNeeded(int localSize)
        {
            // Check if all segments have been returned.
            int totalFreeSegments = localSize + _externalFreeList.Count;
            if (totalFreeSegments == _segments && Interlocked.CompareExchange(ref _state, DEALLOCATED, localSize) == localSize)
            {
                Deallocate();
            }
        }

        internal override void MarkToDeallocate()
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            if (_localFreeList != null)
            {
                lock (_localReturnLock)
                {
                    // CLR: caller quiesces owner allocation/inspection before handoff.
                    // Subsequent returns, including the former owner's, use the external queue.
                    _ownerThread = null;
                    Retire();
                }
            }
            else
            {
                Retire();
            }
        }

        private void Retire()
        {
            if (!TryRetire())
            {
                return;
            }
            IntStack localFreeList = _localFreeList;
            int localSize = localFreeList != null ? localFreeList.Size() : 0;
            // Exchange publishes retirement with the StoreLoad ordering used by the Java volatile write.
            Interlocked.Exchange(ref _state, localSize);
            DeallocateIfNeeded(localSize);
        }
    }
}
