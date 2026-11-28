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
using System.Collections.Generic;
using System.Threading;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Text;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common;

namespace Netty.NET.Buffer;

/**
 * An auto-tuning pooling allocator, that follows an anti-generational hypothesis.
 * <p>
 * The allocator is organized into a list of Magazines, and each magazine has a chunk-buffer that they allocate buffers
 * from.
 * <p>
 * The magazines hold the mutexes that ensure the thread-safety of the allocator, and each thread picks a magazine
 * based on the id of the thread. This spreads the contention of multi-threaded access across the magazines.
 * If contention is detected above a certain threshold, the number of magazines are increased in response to the
 * contention.
 * <p>
 * The magazines maintain histograms of the sizes of the allocations they do. The histograms are used to compute the
 * preferred chunk size. The preferred chunk size is one that is big enough to service 10 allocations of the
 * 99-percentile size. This way, the chunk size is adapted to the allocation patterns.
 * <p>
 * Computing the preferred chunk size is a somewhat expensive operation. Therefore, the frequency with which this is
 * done, is also adapted to the allocation pattern. If a newly computed preferred chunk is the same as the previous
 * preferred chunk size, then the frequency is reduced. Otherwise, the frequency is increased.
 * <p>
 * This allows the allocator to quickly respond to changes in the application workload,
 * without suffering undue overhead from maintaining its statistics.
 * <p>
 * Since magazines are "relatively thread-local", the allocator has a chunk cache that allows excess chunks from any
 * magazine to be shared with other magazines.
 */
// CLR: the histogram description above is historical; the pinned executable
// controllers use fixed small classes and remembered buddy sizes.
// CLR: each pool owns shared size-class magazine groups and a buddy group. A lifetime
// gate closes allocation before Dispose drains every group; live regions/pins survive.
// Physical-thread caches are eligible only within guaranteed FastThreadLocal cleanup scopes.
// CLR: the source's private and package-private nested types are internal nested types here,
// one partial file per nested type or tight group, because other Buffer types and the test
// project (InternalsVisibleTo) use them.
internal sealed class AdaptivePoolingAllocator : IDisposable
{
    // CLR: the shared Memory/Span kernel provides primitive, bulk, text, sequence and
    // .NET stream/file access. The allocation callback binds the owning pool's magazine
    // selection and fallback while preserving buffer identity during reallocation.
    // Each wrapper is fresh; recycling a released CLR object could resurrect stale references.
    internal sealed class AdaptiveByteBuf : AbstractReferenceCountedByteBuf
    {
        // We need the top-level allocator so ByteBuf.capacity(int) can call reallocate()
        // CLR: the pool reservation callback replaces the chunk's allocator back-reference.
        private Func<int, int, AdaptiveChunkAllocation> _allocate;
        private Func<int, int, AdaptiveChunkAllocation, AdaptiveChunkAllocation> _reallocate;
        // this both act as adjustment and the start index for a free list segment allocation
        // CLR: the allocation owns this offset as well as operation/pin claims.
        private AdaptiveChunkAllocation _allocation;
        private int _length;
        private int _maxFastCapacity;
        internal AdaptiveChunkAllocation Allocation => _allocation;

        internal AdaptiveByteBuf(int initialCapacity, int maxCapacity, Func<int, int, AdaptiveChunkAllocation> allocate,
            Func<int, int, AdaptiveChunkAllocation, AdaptiveChunkAllocation> reallocate = null)
            : base(maxCapacity)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            ArgumentNullException.ThrowIfNull(allocate);
            if (initialCapacity < 0 || initialCapacity > maxCapacity)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            _allocate = allocate;
            _reallocate = reallocate;
            _allocation = Allocate(initialCapacity);
            _maxFastCapacity = _allocation.Length;
            _length = initialCapacity;
            BufferAllocatorEventSource.Log.Allocated(CaptureEvent(BufferAllocatorEventSource.Keywords.AllocateBuffer));
        }

        private AdaptiveChunkAllocation Allocate(int capacity)
        {
            AdaptiveChunkAllocation allocation = (_allocation != null && _reallocate != null
                ? _reallocate(capacity, MaxCapacity, _allocation) : _allocate(capacity, MaxCapacity))
                ?? throw new InvalidOperationException("The adaptive storage binding did not supply an allocation.");
            if (allocation.Length >= capacity)
            {
                return allocation;
            }
            allocation.Release();
            throw new InvalidOperationException("The adaptive reservation is smaller than the requested capacity.");
        }

        public override IByteBufAllocator Alloc()
        {
            EnsureAccessible();
            return _allocation.Allocator;
        }
        public override bool IsDirect
        {
            get
            {
                EnsureAccessible();
                return _allocation.IsDirect;
            }
        }
        public override bool IsContiguous
        {
            get
            {
                EnsureAccessible();
                return _allocation.Root.IsContiguous;
            }
        }
        public override int MaxFastWritableBytes => Math.Min(_maxFastCapacity, MaxCapacity) - WriterIndex;

        // setBytes(...) will take care of checking the indices.
        // CLR: text/bulk operations inherit those bounds from ByteBuf, then use this bounded region.
        protected override Memory<byte> GetMemoryCore(int index, int length)
        {
            return _allocation.Memory.Slice(index, length);
        }

        internal override BufferMemoryLease AcquireReadLease()
        {
            EnsureAccessible();
            return _allocation.AcquireLease();
        }

        public override int Capacity
        {
            get => _length;
            set
            {
                using var events = BufferAllocatorEventSource.Log.BeginOperation();
                CheckNewCapacity(value);
                if (_length <= value && value <= _allocation.Length)
                {
                    _length = value;
                    return;
                }
                if (value < _length)
                {
                    _length = value;
                    TrimIndicesToCapacity(value);
                    return;
                }

                // Reallocation required.
                BufferAllocatorEventSource.Log.Reallocated(CaptureEvent(BufferAllocatorEventSource.Keywords.ReallocateBuffer), value);
                AdaptiveChunkAllocation previous = _allocation;
                AdaptiveChunkAllocation replacement = Allocate(value);
                try
                {
                    // CLR: copy before publishing, so allocation/copy failure preserves the
                    // old region, logical capacity, data, indices and marks. Read through
                    // the old root's public bulk kernel, as oldRoot.getBytes does in Java.
                    // A bounded Span replaces the already-published destination wrapper:
                    // both regions remain owned and no temporary NIO cursor is needed.
                    previous.Root.GetBytes(previous.Offset, replacement.Memory.Span[.._length]);
                }
                catch
                {
                    replacement.Release();
                    throw;
                }
                _allocation = replacement;
                _maxFastCapacity = replacement.Length;
                _length = value;
                BufferAllocatorEventSource.Log.Allocated(CaptureEvent(BufferAllocatorEventSource.Keywords.AllocateBuffer));
                previous.Release();
            }
        }

        public override ByteBuf Copy(int index, int length)
        {
            CheckIndex(index, length);
            // Preserve the raw chunk delegate's copy policy, including its maximum
            // capacity and storage kind, rather than the outer buffer's maximum.
            return _allocation.Copy(index, length);
        }

        private BufferAllocatorEventSource.Snapshot? CaptureEvent(System.Diagnostics.Tracing.EventKeywords keyword)
        {
            return BufferAllocatorEventSource.Log.CaptureAdaptive(this, _allocation, keyword);
        }

        protected override void Deallocate()
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            BufferAllocatorEventSource.Log.Freed(CaptureEvent(BufferAllocatorEventSource.Keywords.FreeBuffer));
            AdaptiveChunkAllocation allocation = _allocation;
            _allocation = null;
            _allocate = null;
            _reallocate = null;
            allocation.Release();
        }

        // Original rootParent()._get*/_set* bypass public primitive overrides;
        // keep the allocation's logical storage kernels distinct from its physical Memory borrow.
        internal override byte ReadByteCore(int index)
        {
            CheckIndex(index, 1);
            return _allocation.Root.ReadByteCore(_allocation.Offset + index);
        }

        internal override void WriteByteCore(int index, int value)
        {
            EnsureCanWrite();
            CheckIndex(index, 1);
            _allocation.Root.WriteByteCore(_allocation.Offset + index, value);
        }

        internal override ulong ReadWord(int index, int width, bool littleEndian)
        {
            CheckIndex(index, width);
            return _allocation.Root.ReadWord(_allocation.Offset + index, width, littleEndian);
        }

        internal override ByteBuf SetWord(int index, ulong value, int width, bool littleEndian)
        {
            EnsureCanWrite();
            CheckIndex(index, width);
            _allocation.Root.SetWord(_allocation.Offset + index, value, width, littleEndian);
            return this;
        }

        public override ByteBuf GetBytes(int index, Span<byte> destination)
        {
            CheckIndex(index, destination.Length);
            _allocation.Root.GetBytes(_allocation.Offset + index, destination);
            return this;
        }

        public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
        {
            ArgumentNullException.ThrowIfNull(destination);
            CheckIndex(index, length);
            _allocation.Root.GetBytes(_allocation.Offset + index, destination, destinationIndex, length);
            return this;
        }

        protected override void GetBytesCore(int index, Span<byte> destination)
        {
            _allocation.Root.GetBytes(_allocation.Offset + index, destination);
        }

        protected override void SetZeroCore(int index, int length)
        {
            _allocation.Root.SetZero(_allocation.Offset + index, length);
        }
        internal override bool SupportsDirectBulkCopy => _allocation.Root.SupportsDirectBulkCopy;
        internal override bool SupportsDirectSearch => _allocation.Root.SupportsDirectSearch;

        public override int ForEachByte(int index, int length, Func<byte, bool> processor)
        {
            ArgumentNullException.ThrowIfNull(processor);
            CheckIndex(index, length);
            AdaptiveChunkAllocation allocation = _allocation;
            // CLR: a callback can return or resize this buffer. Hold the old region
            // until the root iteration finishes so it cannot be reused mid-operation.
            using var lease = allocation.AcquireLease();
            int result = allocation.Root.ForEachByte(allocation.Offset + index, length, processor);
            return result < allocation.Offset ? -1 : result - allocation.Offset;
        }

        public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
        {
            ArgumentNullException.ThrowIfNull(processor);
            CheckIndex(index, length);
            AdaptiveChunkAllocation allocation = _allocation;
            using var lease = allocation.AcquireLease();
            int result = allocation.Root.ForEachByteDesc(allocation.Offset + index, length, processor);
            return result < allocation.Offset ? -1 : result - allocation.Offset;
        }
    }

    // CLR: a region has one buffer claim and temporary operation leases. Returning
    // the buffer invalidates its borrowed Memory, but a lease delays slot reuse. Lease tokens
    // are never recycled; old references cannot attach to a new allocation.
    // CLR: this reservation is what Chunk.TryAllocate hands to AdaptiveByteBuf where the source's
    // readInitInto initializes the buffer's chunk, rootParent and startIndex; it has no source type.
    internal sealed class AdaptiveChunkAllocation : MemoryManager<byte>
    {
        private Chunk _chunk;
        private Memory<byte> _memory;
        private int _references = 1;
        private int _disposed;
        internal int Offset { get; }
        internal int Length { get; }
        internal ByteBuf Root
        {
            get
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                return _chunk.Root;
            }
        }
        internal IByteBufAllocator Allocator => _chunk.Allocator;
        internal bool IsDirect => _chunk.IsDirect;
        internal bool IsPooled => _chunk.Pooled;
        internal bool IsThreadLocal { get; }
        internal bool CurrentThreadLocalAssociation => _chunk.IsThreadLocal;

        internal ByteBuf Copy(int index, int length)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (index < 0 || length < 0 || index > Length - length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return _chunk.Copy(Offset + index, length);
        }

        internal AdaptiveChunkAllocation(Chunk chunk, Memory<byte> memory, int offset)
        {
            _chunk = chunk;
            // Capture before a successful last-slot allocation detaches its magazine.
            IsThreadLocal = chunk.IsThreadLocal;
            _memory = memory;
            Offset = offset;
            Length = memory.Length;
        }

        public override Span<byte> GetSpan()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return _memory.Span;
        }

        protected override bool TryGetArray(out ArraySegment<byte> segment)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)_memory, out segment);
        }

        internal BufferMemoryLease AcquireLease()
        {
            return new(AcquireClaim());
        }

        private Claim AcquireClaim()
        {
            // Allocate first so failure cannot strand a reference count increment.
            var claim = new Claim();
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            ReferenceCountUpdater.Retain(ref _references);
            claim.Activate(this);
            if (Volatile.Read(ref _disposed) == 0)
            {
                return claim;
            }
            claim.Dispose();
            throw new ObjectDisposedException(nameof(AdaptiveChunkAllocation));
        }

        internal bool TryGetAddress(int offset, out nint address)
        {
            return NativeMemoryOwner.TryGetAddress(_memory.Slice(offset), out address);
        }

        internal ReadOnlyMemory<byte> GetStorage(int offset, int length)
        {
            return _memory.Slice(offset, length);
        }

        public override MemoryHandle Pin(int elementIndex = 0)
        {
            throw new NotSupportedException("Keep the buffer retained while using this reusable memory.");
        }

        public override void Unpin()
        {
            throw new NotSupportedException();
        }

        internal void Release()
        {
            Dispose(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                ReleaseClaim();
            }
        }

        private void ReleaseClaim()
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            if (!ReferenceCountUpdater.Release(ref _references))
            {
                return;
            }
            Chunk chunk = _chunk;
            _chunk = null;
            _memory = default;
            chunk.ReleaseSegment(Offset, Length);
        }

        private sealed class Claim : IDisposable
        {
            private AdaptiveChunkAllocation _allocation;
            internal void Activate(AdaptiveChunkAllocation allocation)
            {
                _allocation = allocation;
            }

            public void Dispose()
            {
                AdaptiveChunkAllocation allocation = Interlocked.Exchange(ref _allocation, null);
                allocation?.ReleaseClaim();
                GC.SuppressFinalize(this);
            }

            ~Claim()
            {
                Dispose();
            }
        }
    }

    // CLR: the buddy tree is owned by the allocating magazine, which serializes its operations;
    // cross-thread returns only enqueue packed segments.
    internal sealed class BuddyChunk : Chunk
    {
        internal const int MIN_BUDDY_SIZE = 32768;
        private const byte IS_CLAIMED = 1 << 7;
        private const byte HAS_CLAIMED_CHILDREN = 1 << 6;
        // CLR: the low six bits of the unsigned byte, i.e. ~(IS_CLAIMED | HAS_CLAIMED_CHILDREN).
        private const byte SHIFT_MASK = 0x3F;
        private const int PACK_OFFSET_MASK = 0xFFFF;
        // CLR: Integer.SIZE - Integer.numberOfLeadingZeros(PACK_OFFSET_MASK).
        private const int PACK_SIZE_SHIFT = 16;

        // CLR: ConcurrentQueue<int> replaces the JVM MPSC queue. Live reservations bound
        // its size; only the allocating magazine mutates the buddy tree.
        private readonly ConcurrentQueue<int> _freeList = new();
        // The bits of each buddy: [1: is claimed][1: has claimed children][30: MIN_BUDDY_SIZE shift to get size]
        // CLR uses unsigned bytes; the low six bits hold the shift and the high two are flags.
        private readonly byte[] _buddies;
        private readonly int _freeListCapacity;

        internal BuddyChunk(ByteBuf @delegate, ChunkRegistry chunkRegistry) : base(@delegate, chunkRegistry, true)
        {
            // CLR: reject shapes the byte flags cannot describe before the chunk is registered.
            if (Capacity < MIN_BUDDY_SIZE || Capacity > 8 * 1024 * 1024 || !BitOperations.IsPow2(Capacity))
                throw new ArgumentOutOfRangeException("capacity");
            _freeListCapacity = Capacity / MIN_BUDDY_SIZE;
            int maxShift = BitOperations.TrailingZeroCount((uint)_freeListCapacity);
            System.Diagnostics.Debug.Assert(maxShift <= 30); // The top 2 bits are used for marking.
            // At most half of tree (all leaf nodes) can be freed.
            _buddies = new byte[_freeListCapacity << 1];

            // Generate the buddies entries.
            int index = 1;
            int runLength = 1;
            int currentRun = 0;
            while (maxShift > 0)
            {
                _buddies[index++] = (byte)maxShift;
                if (++currentRun == runLength)
                {
                    currentRun = 0;
                    runLength <<= 1;
                    maxShift--;
                }
            }
            Register();
        }

        // CLR: the Memory-based counterpart of readInitInto (see Chunk.TryAllocate).
        internal override AdaptiveChunkAllocation TryAllocate(int startingCapacity)
        {
            ThrowIfRetired();
            if (!_freeList.IsEmpty)
            {
                ProcessFreelistEntries();
            }
            // A magazine may offer the residual capacity, which need not be a power of
            // two. The Java search returns no matching buddy in that case; return that
            // outcome before walking the tree.
            if (startingCapacity < MIN_BUDDY_SIZE || !BitOperations.IsPow2(startingCapacity))
            {
                return null;
            }
            int startIndex = ChooseFirstFreeBuddy(1, startingCapacity, 0);
            if (startIndex == -1)
            {
                return null;
            }
            Retain();
            try
            {
                var allocation = new AdaptiveChunkAllocation(this, Storage.AsMemory(startIndex, startingCapacity), startIndex);
                AllocatedBytes += startingCapacity;
                return allocation;
            }
            catch
            {
                UnreserveMatchingBuddy(1, startingCapacity, startIndex, 0);
                // If chunk is not null we know that buf.init(...) failed and so we need to manually release
                // the chunk again as we retained it before calling buf.init(...).
                Release();
                throw;
            }
        }

        // CLR: the source's IntConsumer.accept, applied while draining the free list.
        internal void Accept(int packed)
        {
            // Called by allocating thread when draining freeList.
            int size = UnpackSize(packed);
            int offset = UnpackOffset(packed);
            UnreserveMatchingBuddy(1, size, offset, 0);
            AllocatedBytes -= size;
        }

        private static int UnpackSize(int packed)
        {
            return MIN_BUDDY_SIZE << (packed >> PACK_SIZE_SHIFT);
        }

        private static int UnpackOffset(int packed)
        {
            return (packed & PACK_OFFSET_MASK) * MIN_BUDDY_SIZE;
        }

        internal override void ReleaseSegment(int startingIndex, int size)
        {
            int packedOffset = startingIndex / MIN_BUDDY_SIZE;
            int packedSize = BitOperations.TrailingZeroCount((uint)(size / MIN_BUDDY_SIZE)) << PACK_SIZE_SHIFT;
            int packed = packedOffset | packedSize;
            _freeList.Enqueue(packed);
            Release();
        }

        internal override int RemainingCapacity
        {
            get
            {
                int capacityInFreeList = 0;
                if (!_freeList.IsEmpty)
                {
                    // A concurrent snapshot, like weakPeekReduce, is not a transaction with allocation.
                    foreach (int entry in _freeList)
                    {
                        capacityInFreeList += UnpackSize(entry);
                    }
                }
                return base.RemainingCapacity + capacityInFreeList;
            }
        }

        internal override bool HasUnprocessedFreelistEntries => !_freeList.IsEmpty;

        internal override void ProcessFreelistEntries()
        {
            // CLR: drain(freeListCapacity, this) as a bounded dequeue loop.
            for (int i = 0; i < _freeListCapacity && _freeList.TryDequeue(out int packed); i++)
            {
                Accept(packed);
            }
        }

        /**
         * Claim a suitable buddy and return its start offset into the delegate chunk, or return -1 if nothing claimed.
         */
        private int ChooseFirstFreeBuddy(int index, int size, int currOffset)
        {
            byte[] buddies = _buddies;
            while (index < buddies.Length)
            {
                byte buddy = buddies[index];
                int currValue = MIN_BUDDY_SIZE << (buddy & SHIFT_MASK);
                if (currValue < size || (buddy & IS_CLAIMED) == IS_CLAIMED)
                {
                    return -1;
                }
                if (currValue == size && (buddy & HAS_CLAIMED_CHILDREN) == 0)
                {
                    buddies[index] |= IS_CLAIMED;
                    return currOffset;
                }
                int found = ChooseFirstFreeBuddy(index << 1, size, currOffset);
                if (found != -1)
                {
                    buddies[index] |= HAS_CLAIMED_CHILDREN;
                    return found;
                }
                index = (index << 1) + 1;
                currOffset += currValue >> 1; // Bump offset to skip first half of this layer.
            }
            return -1;
        }

        /**
         * Un-reserve the matching buddy and return whether there are any other child or sibling reservations.
         */
        // CLR: internal so tests can present returns that no live reservation can produce.
        internal bool UnreserveMatchingBuddy(int index, int size, int offset, int currOffset)
        {
            byte[] buddies = _buddies;
            if (buddies.Length <= index)
            {
                return false;
            }
            byte buddy = buddies[index];
            int currSize = MIN_BUDDY_SIZE << (buddy & SHIFT_MASK);

            if (currSize == size)
            {
                // We're at the right size level.
                // CLR: also require the claim bit, so a duplicate return cannot clear a live sibling.
                if (currOffset == offset && (buddy & IS_CLAIMED) != 0)
                {
                    buddies[index] &= SHIFT_MASK;
                    return false;
                }
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                    $"The intended segment was not found at index {index}, for size {size} and offset {offset}"));
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
                byte sibling = buddies[siblingIndex];
                if ((sibling & SHIFT_MASK) == sibling)
                {
                    // No claims in the sibling. We can clear this level as well.
                    buddies[index] &= SHIFT_MASK;
                    return false;
                }
            }
            return true;
        }

        public override string ToString()
        {
            int capacity = Capacity;
            int remaining = capacity - AllocatedBytes;
            return string.Create(CultureInfo.InvariantCulture,
                $"BuddyChunk[capacity: {capacity}, remaining: {remaining}, free list: {_freeList.Count}]");
        }
    }

    // CLR: atomics replace LongAdder. Count physical chunk capacity, including cached
    // chunks and chunks awaiting their last segment/operation lease. No membership set.
    internal sealed class ChunkRegistry
    {
        private long _totalCapacity;

        internal long TotalCapacity => Volatile.Read(ref _totalCapacity);

        internal void Add(Chunk chunk)
        {
            Interlocked.Add(ref _totalCapacity, chunk.Capacity);
        }

        internal void Remove(Chunk chunk)
        {
            Interlocked.Add(ref _totalCapacity, -chunk.Capacity);
        }
    }

    // The allocating magazine must serialize allocation, capacity inspection and retirement.
    // Returns may come from other threads. Construction transfers the raw delegate's single
    // reference only on success. Events describe successful creation and final deallocation.
    // CLR: a chunk takes its registry instead of a magazine and registers itself after successful
    // construction, so chunks also exist without a magazine; the creator attaches one afterwards.
    internal class Chunk : IDisposable
    {
        private ByteBuf _delegate;
        private Magazine _magazine;
        private readonly ChunkRegistry _chunkRegistry;
        // Always populate the refCnt field, so HotSpot doesn't emit `null` checks.
        // This is safe to do even on native-image.
        // CLR: a non-nullable integer and ReferenceCountUpdater replace the JVM RefCnt object.
        private int _refCnt = 1;
        private int _retired;
        private bool _eventsTracked;
        internal bool FallbackThreadLocal { private get; set; }
        // CLR: thread-local magazines have no allocation lock, as in the source's event metadata.
        internal bool IsThreadLocal => _magazine != null ? _magazine.AllocationLock == null : FallbackThreadLocal;
        protected int AllocatedBytes;
        internal int Capacity { get; }
        internal bool IsDirect { get; }
        internal bool Pooled { get; }
        internal int RefCnt => ReferenceCountUpdater.GetCount(ref _refCnt);
        internal ByteBuf Root => Storage;
        internal IByteBufAllocator Allocator => Storage.Alloc();
        internal ByteBuf Copy(int index, int length)
        {
            return Storage.Copy(index, length);
        }
        internal bool IsDeallocated => Volatile.Read(ref _delegate) == null;
        protected ByteBuf Storage => Volatile.Read(ref _delegate) ?? throw new ObjectDisposedException(nameof(Chunk));
        protected bool IsRetired => Volatile.Read(ref _retired) != 0;

        internal Chunk(ByteBuf @delegate, ChunkRegistry chunkRegistry)
            : this(@delegate, chunkRegistry, false)
        {
            Register();
        }

        private protected Chunk(ByteBuf @delegate, ChunkRegistry chunkRegistry, bool pooled)
        {
            ArgumentNullException.ThrowIfNull(@delegate);
            ArgumentNullException.ThrowIfNull(chunkRegistry);
            if (@delegate.RefCnt != 1)
                throw new ArgumentException("The raw chunk delegate must have one live reference.", nameof(@delegate));
            _delegate = @delegate;
            _chunkRegistry = chunkRegistry;
            Capacity = @delegate.Capacity;
            IsDirect = @delegate.IsDirect;
            Pooled = pooled;
        }

        // Derived constructors register only after all fallible initialization succeeds.
        private protected void Register()
        {
            _chunkRegistry.Add(this);
        }

        internal void TrackAllocation(bool threadLocal)
        {
            _eventsTracked = true;
            BufferAllocatorEventSource.Log.AdaptiveChunkChanged(Storage, Pooled, threadLocal, allocated: true);
        }

        internal void DetachFromMagazine()
        {
            if (_magazine != null)
            {
                _magazine = null;
            }
        }

        internal void AttachToMagazine(Magazine magazine)
        {
            System.Diagnostics.Debug.Assert(_magazine == null);
            _magazine = magazine;
        }

        /**
         * Called when a magazine is done using this chunk, probably because it was emptied.
         */
        internal void ReleaseFromMagazine()
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            // Chunks can be reused before they become empty.
            // We can therefor put them in the shared queue as soon as the magazine is done with this chunk.
            Magazine mag = _magazine;
            DetachFromMagazine();
            if (!mag.OfferToQueue(this))
            {
                MarkToDeallocate();
            }
        }

        /**
         * Called when a ByteBuf is done using its allocation in this chunk.
         */
        internal virtual void ReleaseSegment(int ignoredSegmentId, int size)
        {
            Release();
        }

        // CLR: markToDeallocate retires the magazine/cache ownership once. Live allocations and
        // leases still own their segments. Calling it again must not release that ownership again.
        internal virtual void MarkToDeallocate()
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            if (TryRetire())
            {
                Release();
            }
        }

        // CLR: IDisposable scopes a chunk; disposing it is markToDeallocate().
        public void Dispose()
        {
            MarkToDeallocate();
        }

        private protected void ThrowIfRetired()
        {
            ObjectDisposedException.ThrowIf(IsRetired, this);
        }

        private protected bool TryRetire()
        {
            return Interlocked.Exchange(ref _retired, 1) == 0;
        }

        private protected void Retain()
        {
            ReferenceCountUpdater.Retain(ref _refCnt);
        }

        private protected void Release()
        {
            if (ReferenceCountUpdater.Release(ref _refCnt))
            {
                Deallocate();
            }
        }

        private protected void Deallocate()
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            ByteBuf @delegate = Interlocked.Exchange(ref _delegate, null);
            if (@delegate == null)
            {
                return;
            }
            OnRelease(@delegate);
            _chunkRegistry.Remove(this);
            @delegate.Release();
        }

        private void OnRelease(ByteBuf @delegate)
        {
            if (_eventsTracked)
            {
                BufferAllocatorEventSource.Log.AdaptiveChunkChanged(@delegate, Pooled, false, allocated: false);
            }
        }

        // CLR: the Memory-based counterpart of readInitInto(buf, size, startingCapacity, maxCapacity).
        // It returns a reservation that AdaptiveByteBuf consumes instead of initializing the buffer's
        // chunk and startIndex, and null where readInitInto returns false.
        internal virtual AdaptiveChunkAllocation TryAllocate(int startingCapacity)
        {
            ThrowIfRetired();
            ArgumentOutOfRangeException.ThrowIfNegative(startingCapacity);
            if (startingCapacity > Capacity - AllocatedBytes)
            {
                return null;
            }
            int startIndex = AllocatedBytes;
            AllocatedBytes = startIndex + startingCapacity;
            Retain();
            try
            {
                return new AdaptiveChunkAllocation(this, Storage.AsMemory(startIndex, startingCapacity), startIndex);
            }
            catch
            {
                // If chunk is not null we know that buf.init(...) failed and so we need to manually release
                // the chunk again as we retained it before calling buf.init(...). Beside this we also need to
                // restore the old allocatedBytes value.
                // CLR: allocation initialization replaces buf.init here; ownership is transferred on success.
                AllocatedBytes = startIndex;
                Release();
                throw;
            }
        }

        internal virtual int RemainingCapacity => Capacity - AllocatedBytes;

        internal virtual bool HasUnprocessedFreelistEntries => false;

        internal virtual void ProcessFreelistEntries()
        {
        }
    }

    // CLR: the source's free() is IDisposable.Dispose().
    internal interface IChunkCache : IDisposable
    {
        Chunk PollChunk(int size);

        bool OfferChunk(Chunk chunk);

        bool IsEmpty { get; }
    }

    // Cached chunks are detached from magazines: no readInitInto can happen, so segment count
    // can only grow (external releaseSegment returns) and never shrink. Once a chunk reaches
    // full capacity (hasFullCapacity), it stays idle while in the cache.
    //
    // Epoch-based aging invariants (both caches):
    //
    // 1. CLASSIFICATION: purge scans all chunks. Idle (hasFullCapacity) → epoch++.
    //    Non-idle → epoch = 0. Only idle chunks can accumulate epoch.
    //
    // 2. EVICTION: idle chunks with epoch > CHUNK_PURGE_THRESHOLD are evicted (markToDeallocate).
    //    Eviction is immediate — all segments are in, no outstanding references.
    //    Non-idle chunks are never evicted (deallocation would be deferred, not immediate).
    //    A retention floor prevents over-eviction: CHUNK_REUSE_QUEUE for the shared cache,
    //    purgeRetentionFloor (from THREAD_LOCAL_CACHE_MIN_BYTES) for the thread-local cache.
    //
    // 3. SCAN RESET: scanForCapacity resets purgeEpoch = 0 on the chunk it picks. The scan
    //    knows the chunk is being used. The chunk gets allocated from, becomes non-idle, and
    //    the next purge resets its epoch anyway (non-idle → 0). The scan reset covers the case
    //    where all segments return before the next purge (short-lived buffers).
    //
    // 4. CONVERGENCE: idle chunks that are never picked by scan age undisturbed across
    //    purge cycles. After CHUNK_PURGE_THRESHOLD + 1 consecutive cycles of being idle and
    //    unpolled, they are evicted. Chunks picked by scan get epoch reset — aging interrupted.
    //    Thread-local: partition orders [epoch=0 | 0<epoch<T | epoch>=T | noCap]. Scan takes
    //    from head (epoch=0 first). Chunks with epoch>=threshold are placed at the back of
    //    the hasCap zone so scan doesn't reach them — they age to threshold+1 and get evicted.
    //    Shared: approximate, converges over multiple cycles (FIFO queue ordering,
    //    LRU preference in scan, retained counter in purge).
    internal abstract class SizeClassedChunkCache : IChunkCache
    {
        // CLR: the allocator supplies its per-instance purge and retention settings, which replace
        // the source's CHUNK_* and THREAD_LOCAL_CACHE_* statics.
        internal static SizeClassedChunkCache Create(bool isThreadLocal, int chunkSize, AdaptivePoolingAllocator allocator)
        {
            return isThreadLocal ? new ThreadLocalSizeClassedChunkCache(chunkSize, allocator._chunkPurgePollsThreadLocal,
                    allocator._chunkPurgeThreshold, allocator._threadLocalCacheMaxBytes, allocator._threadLocalCacheMinBytes) :
                    new SharedSizeClassedChunkCache(allocator._chunkReuseQueue, allocator._chunkPurgePollsShared,
                    allocator._chunkPurgeThreshold);
        }

        public abstract SizeClassedChunk PollChunk(int size);

        Chunk IChunkCache.PollChunk(int size)
        {
            return PollChunk(size);
        }

        public abstract bool OfferChunk(Chunk chunk);

        public abstract void Dispose();

        public abstract bool IsEmpty { get; }

        // Visible for testing: triggers a purge scan bypassing the budget counter.
        internal abstract SizeClassedChunk ForcePurge();
    }

    internal interface IChunkManagementStrategy
    {
        IChunkController CreateController(MagazineGroup group);

        IChunkCache CreateChunkCache(bool isThreadLocal);
    }

    internal interface IChunkController
    {
        /**
         * Compute the "fast max capacity" value for the buffer.
         */
        int ComputeBufferCapacity(int requestedSize, int maxCapacity, bool isReallocation);

        /**
         * Allocate a new {@link Chunk} for the given {@link Magazine}.
         */
        Chunk NewChunkAllocation(int promptingSize, Magazine magazine);
    }

    // CLR: the strategies keep the allocator only to read its per-instance cache settings.
    internal sealed class SizeClassChunkManagementStrategy : IChunkManagementStrategy
    {
        // To amortize activation/deactivation of chunks, we should have a minimum number of segments per chunk.
        // We choose 32 because it seems neither too small nor too big.
        // For segments of 16 KiB, the chunks will be half a megabyte.
        private const int MIN_SEGMENTS_PER_CHUNK = 32;
        private readonly AdaptivePoolingAllocator _allocator;
        private readonly int _segmentSize;
        private readonly int _chunkSize;

        internal SizeClassChunkManagementStrategy(AdaptivePoolingAllocator allocator, int segmentSize)
        {
            _allocator = allocator;
            _segmentSize = ObjectUtil.CheckPositive(segmentSize, nameof(segmentSize));
            _chunkSize = Math.Max(MIN_CHUNK_SIZE, segmentSize * MIN_SEGMENTS_PER_CHUNK);
        }

        public IChunkController CreateController(MagazineGroup group)
        {
            return new SizeClassChunkController(group, _segmentSize, _chunkSize);
        }

        public IChunkCache CreateChunkCache(bool isThreadLocal)
        {
            return SizeClassedChunkCache.Create(isThreadLocal, _chunkSize, _allocator);
        }
    }

    internal sealed class SizeClassChunkController : IChunkController
    {
        private readonly IChunkAllocator _chunkAllocator;
        internal readonly int SegmentSize;
        internal readonly int ChunkSize;
        private readonly ChunkRegistry _chunkRegistry;

        internal SizeClassChunkController(MagazineGroup group, int segmentSize, int chunkSize)
        {
            _chunkAllocator = group.ChunkAllocator;
            SegmentSize = segmentSize;
            ChunkSize = chunkSize;
            _chunkRegistry = group.Allocator._chunkRegistry;
        }

        public int ComputeBufferCapacity(
                int requestedSize, int maxCapacity, bool isReallocation)
        {
            // CLR: argument guards; magazines only ask for valid pooled requests.
            ArgumentOutOfRangeException.ThrowIfNegative(requestedSize);
            if (maxCapacity < requestedSize)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCapacity));
            }
            return Math.Min(SegmentSize, maxCapacity);
        }

        public Chunk NewChunkAllocation(int promptingSize, Magazine magazine)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            ByteBuf chunkBuffer = _chunkAllocator.Allocate(ChunkSize, ChunkSize);
            // CLR: thread-local magazines have no allocation lock; their chunks keep the physical owner.
            bool threadLocal = magazine.AllocationLock == null;
            SizeClassedChunk chunk;
            try
            {
                chunk = new SizeClassedChunk(chunkBuffer, _chunkRegistry, SegmentSize, threadLocal);
                chunk.TrackAllocation(threadLocal);
            }
            catch
            {
                chunkBuffer.Release();
                throw;
            }
            chunk.AttachToMagazine(magazine);
            return chunk;
        }
    }

    internal sealed class BuddyChunkManagementStrategy : IChunkManagementStrategy
    {
        // CLR: StrongBox<int> with Volatile access replaces the shared AtomicInteger.
        private readonly StrongBox<int> _maxChunkSize = new();
        private readonly AdaptivePoolingAllocator _allocator;

        internal BuddyChunkManagementStrategy(AdaptivePoolingAllocator allocator)
        {
            _allocator = allocator;
        }

        public IChunkController CreateController(MagazineGroup group)
        {
            return new BuddyChunkController(group, _maxChunkSize);
        }

        public IChunkCache CreateChunkCache(bool isThreadLocal)
        {
            return new ConcurrentSkipListChunkCache(_allocator._chunkReuseQueue);
        }
    }

    internal sealed class BuddyChunkController : IChunkController
    {
        private readonly IChunkAllocator _chunkAllocator;
        private readonly ChunkRegistry _chunkRegistry;
        private readonly StrongBox<int> _maxChunkSize;
        // CLR: reads the allocator's per-instance MAX_CHUNK_SIZE.
        private readonly AdaptivePoolingAllocator _allocator;

        internal BuddyChunkController(MagazineGroup group, StrongBox<int> maxChunkSize)
        {
            _chunkAllocator = group.ChunkAllocator;
            _chunkRegistry = group.Allocator._chunkRegistry;
            _maxChunkSize = maxChunkSize;
            _allocator = group.Allocator;
        }

        public int ComputeBufferCapacity(int requestedSize, int maxCapacity, bool isReallocation)
        {
            // CLR: argument guards; magazines only ask for valid pooled requests.
            ArgumentOutOfRangeException.ThrowIfNegative(requestedSize);
            if (maxCapacity < requestedSize)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCapacity));
            }
            // Buddy reservations stay powers of two even when the buffer maximum is smaller.
            // The public MaxFastWritableBytes contract clamps that reservation to maxCapacity.
            return MathUtil.SafeFindNextPositivePowerOfTwo(requestedSize);
        }

        public Chunk NewChunkAllocation(int promptingSize, Magazine magazine)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            // CLR: argument guard before the remembered size can change.
            ArgumentOutOfRangeException.ThrowIfNegative(promptingSize);
            int maxChunkSize = Volatile.Read(ref _maxChunkSize.Value);
            int proposedChunkSize = MathUtil.SafeFindNextPositivePowerOfTwo(BUFS_PER_CHUNK * promptingSize);
            int chunkSize = Math.Min(_allocator.MaxChunkSize, Math.Max(maxChunkSize, proposedChunkSize));
            if (chunkSize > maxChunkSize)
            {
                // Update our stored max chunk size. It's fine that this is racy.
                Volatile.Write(ref _maxChunkSize.Value, chunkSize);
            }
            ByteBuf chunkBuffer = _chunkAllocator.Allocate(chunkSize, chunkSize);
            bool threadLocal = magazine.AllocationLock == null;
            BuddyChunk chunk;
            try
            {
                chunk = new BuddyChunk(chunkBuffer, _chunkRegistry);
                chunk.TrackAllocation(threadLocal);
            }
            catch
            {
                chunkBuffer.Release();
                throw;
            }
            chunk.AttachToMagazine(magazine);
            return chunk;
        }
    }

    // CLR: the BCL has no ConcurrentSkipListMap. A locked sorted multimap replaces
    // ConcurrentSkipListIntObjMultimap under the source's type name. Equal keys put the
    // newest entry first, as the original base-list insertion does.
    internal sealed class ConcurrentSkipListChunkCache : IChunkCache
    {
        private readonly object _sync = new();
        private readonly SortedDictionary<int, List<Chunk>> _chunks = new();
        // CLR: per-instance CHUNK_REUSE_QUEUE.
        private readonly int _chunkReuseQueue;
        private int _count;
        private bool _disposed;

        internal ConcurrentSkipListChunkCache(int chunkReuseQueue)
        {
            _chunkReuseQueue = chunkReuseQueue;
        }

        public bool IsEmpty
        {
            get
            {
                lock (_sync)
                {
                    return _count == 0;
                }
            }
        }
        internal int Count
        {
            get
            {
                lock (_sync)
                {
                    return _count;
                }
            }
        }

        // CLR: multimap put/remove/iteration helpers over the sorted dictionary.
        private void Put(int key, Chunk chunk)
        {
            if (!_chunks.TryGetValue(key, out var list))
            {
                _chunks.Add(key, list = new());
            }
            list.Insert(0, chunk);
            _count++;
        }

        private void Remove(int key, Chunk chunk)
        {
            List<Chunk> list = _chunks[key];
            list.Remove(chunk);
            _count--;
            if (list.Count == 0)
            {
                _chunks.Remove(key);
            }
        }

        private List<(int Key, Chunk Chunk)> Entries()
        {
            var entries = new List<(int, Chunk)>(_count);

            foreach (var pair in _chunks)
            {
                foreach (var chunk in pair.Value)
                {
                    entries.Add((pair.Key, chunk));
                }
            }

            return entries;
        }

        public Chunk PollChunk(int size)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            lock (_sync)
            {
                if (_disposed || _count == 0)
                {
                    return null;
                }
                Chunk ceiling = null;
                int key = 0;
                foreach (var pair in _chunks)
                {
                    if (pair.Key >= size)
                    {
                        key = pair.Key;
                        ceiling = pair.Value[0];
                        break;
                    }
                }
                if (ceiling != null)
                {
                    Remove(key, ceiling);
                    Chunk chunk = ceiling;
                    if (chunk.HasUnprocessedFreelistEntries)
                    {
                        chunk.ProcessFreelistEntries();
                    }
                    return chunk;
                }

                Chunk bestChunk = null;
                int bestRemainingCapacity = 0;
                foreach (var entry in Entries())
                {
                    Chunk chunk = entry.Chunk;
                    if (chunk.HasUnprocessedFreelistEntries)
                    {
                        Remove(entry.Key, chunk);
                        chunk.ProcessFreelistEntries();
                        int remainingCapacity = chunk.RemainingCapacity;
                        if (remainingCapacity >= size &&
                                (bestChunk == null || remainingCapacity > bestRemainingCapacity))
                        {
                            if (bestChunk != null)
                            {
                                Put(bestRemainingCapacity, bestChunk);
                            }
                            bestChunk = chunk;
                            bestRemainingCapacity = remainingCapacity;
                        }
                        else
                        {
                            Put(remainingCapacity, chunk);
                        }
                    }
                }

                return bestChunk;
            }
        }

        public bool OfferChunk(Chunk chunk)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            lock (_sync)
            {
                if (_disposed)
                {
                    return false;
                }
                Put(chunk.RemainingCapacity, chunk);

                int size = _count;
                while (size > _chunkReuseQueue)
                {
                    int key = -1;
                    Chunk toDeallocate = null;
                    foreach (var entry in _chunks)
                    {
                        foreach (Chunk candidate in entry.Value)
                        {
                            if (candidate.RefCnt == 1)
                            {
                                toDeallocate = candidate;
                                key = entry.Key;
                                break;
                            }
                        }
                        if (toDeallocate != null)
                        {
                            break;
                        }
                    }
                    if (toDeallocate == null)
                    {
                        break;
                    }
                    Remove(key, toDeallocate);
                    toDeallocate.MarkToDeallocate();
                    size = _count;
                }
                return true;
            }
        }

        public void Dispose()
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            lock (_sync)
            {
                _disposed = true;
                foreach (var entry in Entries())
                {
                    entry.Chunk.MarkToDeallocate();
                }
                _chunks.Clear();
                _count = 0;
            }
        }
    }

    private const int LOW_MEM_THRESHOLD = 512 * 1024 * 1024;
    // CLR: GCMemoryInfo.TotalAvailableMemoryBytes (GCHeapHardLimit, a container limit or physical memory) is the
    // counterpart of Runtime.maxMemory().
    internal static readonly bool IS_LOW_MEM = SystemPropertyUtil.GetBoolean(
            "io.netty.allocator.lowMemory",
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes <= LOW_MEM_THRESHOLD);

    /**
     * The 128 KiB minimum chunk size is chosen to encourage the system allocator to delegate to mmap for chunk
     * allocations. For instance, glibc will do this.
     * This pushes any fragmentation from chunk size deviations off physical memory, onto virtual memory,
     * which is a much, much larger space. Chunks are also allocated in whole multiples of the minimum
     * chunk size, which itself is a whole multiple of popular page sizes like 4 KiB, 16 KiB, and 64 KiB.
     */
    internal const int MIN_CHUNK_SIZE = 128 * 1024;
    // The current fixed-class controller can produce chunks that are not multiples of this minimum.
    private const int EXPANSION_ATTEMPTS = 3;
    private const int INITIAL_MAGAZINES = 1;
    private const int RETIRE_CAPACITY = 256;
    private const int BUFS_PER_CHUNK = 8; // For large buffers, aim to have about this many buffers per chunk.

    /**
     * The size classes are chosen based on the following observation:
     * <p>
     * Most allocations, particularly ones above 256 bytes, aim to be a power-of-2. However, many use cases, such
     * as framing protocols, are themselves operating or moving power-of-2 sized payloads, to which they add a
     * small amount of overhead, such as headers or checksums.
     * This means we seem to get a lot of mileage out of having both power-of-2 sizes, and power-of-2-plus-a-bit.
     * <p>
     * On the conflicting requirements of both having as few chunks as possible, and having as little wasted
     * memory within each chunk as possible, this seems to strike a surprisingly good balance for the use cases
     * tested so far.
     */
    private static readonly int[] SIZE_CLASSES = [
            32,
            64,
            128,
            256,
            512,
            640, // 512 + 128
            1024,
            1152, // 1024 + 128
            2048,
            2304, // 2048 + 256
            4096,
            4352, // 4096 + 256
            8192,
            8704, // 8192 + 512
            16384,
            16896, // 16384 + 512
    ];

    private static readonly int SIZE_CLASSES_COUNT = SIZE_CLASSES.Length;
    private static readonly byte[] SIZE_INDEXES = CreateSizeIndexes();

    // CLR: the source's static initializer block; it fills SIZE_INDEXES.
    private static byte[] CreateSizeIndexes()
    {
        var indexes = new byte[SIZE_CLASSES[SIZE_CLASSES_COUNT - 1] / 32 + 1];
        int lastIndex = 0;
        for (int i = 0; i < SIZE_CLASSES_COUNT; i++)
        {
            int sizeClass = SIZE_CLASSES[i];
            //noinspection ConstantValue
            System.Diagnostics.Debug.Assert((sizeClass & 31) == 0, "Size class must be a multiple of 32");
            int sizeIndex = SizeIndexOf(sizeClass);
            Array.Fill(indexes, (byte)i, lastIndex + 1, sizeIndex - lastIndex);
            lastIndex = sizeIndex;
        }
        return indexes;
    }

    // CLR: the source reads the following settings from class-initializer statics. Each pool
    // carries them per instance instead (AdaptiveByteBufAllocator supplies the same defaults).
    // IS_LOW_MEM
    private readonly bool _isLowMem;
    // MAX_STRIPES
    private readonly int _maxStripes;
    /**
     * The maximum size of a pooled chunk, in bytes. Allocations bigger than this will never be pooled.
     * <p>
     * This number is 8 MiB, and is derived from the limitations of internal histograms.
     */
    // CLR: replaces the static MAX_CHUNK_SIZE.
    internal readonly int MaxChunkSize;
    // The original histogram rationale is retained above; the pinned implementation
    // uses fixed classes and a remembered maximum for buddy chunks.
    // CLR: replaces the static MAX_POOLED_BUF_SIZE.
    internal readonly int MaxPooledBufSize;
    // CHUNK_REUSE_QUEUE
    private readonly int _chunkReuseQueue;
    // CHUNK_PURGE_POLLS_THREAD_LOCAL
    private readonly long _chunkPurgePollsThreadLocal;
    // CHUNK_PURGE_POLLS_SHARED
    private readonly long _chunkPurgePollsShared;
    // CHUNK_PURGE_THRESHOLD
    private readonly int _chunkPurgeThreshold;
    /**
     * Per-size-class upper bound (in bytes) on the thread-local chunk cache.
     * When a size class cache holds this many bytes worth of chunks,
     * further offers are rejected and the chunk is marked for immediate deallocation.
     * Chunks already in the cache are only evicted by the purge mechanism (they must be full and idle for
     * {@link #CHUNK_PURGE_THRESHOLD} consecutive purge cycles).
     */
    private readonly int _threadLocalCacheMaxBytes;
    /**
     * Per-size-class lower bound (in bytes) on the thread-local chunk cache.
     * The purge mechanism will not evict chunks below this retention floor, even if they are full and idle.
     * Clamped to {@link #THREAD_LOCAL_CACHE_MAX_BYTES} if the configured value exceeds it.
     * When equal to {@link #THREAD_LOCAL_CACHE_MAX_BYTES}, purge eviction is effectively disabled.
     */
    private readonly int _threadLocalCacheMinBytes;

    private readonly ReaderWriterLockSlim _lifetime = new();
    private readonly IChunkAllocator _chunkAllocator;
    private readonly ChunkRegistry _chunkRegistry;
    private readonly MagazineGroup[] _sizeClassedMagazineGroups;
    private readonly MagazineGroup _largeBufferMagazineGroup;
    private readonly LocalGroups _threadLocalGroup;
    private readonly HashSet<MagazineGroup[]> _localBindings = new();
    // CLR: the buffer's reservation callbacks replace the chunk's allocator back-reference.
    private readonly Func<int, int, AdaptiveChunkAllocation> _allocate;
    private readonly Func<int, int, AdaptiveChunkAllocation, AdaptiveChunkAllocation> _reallocate;
    private bool _disposed;
    internal MagazineGroup[] SizeClassedMagazineGroups => _sizeClassedMagazineGroups;
    internal MagazineGroup LargeBufferMagazineGroup => _largeBufferMagazineGroup;
    internal LocalGroups ThreadLocalGroup => _threadLocalGroup;
    internal int LocalBindingCount
    {
        get
        {
            lock (_localBindings)
            {
                return _localBindings.Count;
            }
        }
    }

    // CLR: lowMemory and the remaining settings are the per-instance replacements listed above.
    internal AdaptivePoolingAllocator(IChunkAllocator chunkAllocator, bool lowMemory,
        int chunkReuseQueue, long chunkPurgePollsShared, int chunkPurgeThreshold, int maxStripes,
        bool useCacheForNonEventLoopThreads = false, long chunkPurgePollsThreadLocal = 16,
        int threadLocalCacheMaxBytes = 8 * 1024 * 1024, int threadLocalCacheMinBytes = 4 * 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(chunkAllocator);
        _chunkAllocator = chunkAllocator;
        if (chunkReuseQueue < 2 || chunkReuseQueue > 1 << 28)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkReuseQueue));
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkPurgePollsShared, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkPurgeThreshold, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxStripes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkPurgePollsThreadLocal, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(threadLocalCacheMaxBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(threadLocalCacheMinBytes, 1);
        _isLowMem = lowMemory;
        _maxStripes = lowMemory ? 1 : maxStripes;
        MaxChunkSize = lowMemory
            ? 2 * 1024 * 1024 // 2 MiB for systems with small heaps.
            : 8 * 1024 * 1024; // 8 MiB.
        MaxPooledBufSize = MaxChunkSize / BUFS_PER_CHUNK;
        _chunkReuseQueue = chunkReuseQueue;
        _chunkPurgePollsShared = chunkPurgePollsShared;
        _chunkPurgeThreshold = chunkPurgeThreshold;
        _chunkPurgePollsThreadLocal = chunkPurgePollsThreadLocal;
        _threadLocalCacheMaxBytes = threadLocalCacheMaxBytes;
        _threadLocalCacheMinBytes = Math.Min(threadLocalCacheMaxBytes, threadLocalCacheMinBytes);
        _allocate = (size, maxCapacity) => Allocate(size, maxCapacity, null);
        _reallocate = Reallocate;
        _chunkRegistry = new ChunkRegistry();
        _sizeClassedMagazineGroups = CreateMagazineGroupSizeClasses(this, false);
        _largeBufferMagazineGroup = new MagazineGroup(
                this, chunkAllocator, new BuddyChunkManagementStrategy(this), false);
        // CLR: the FastThreadLocal always exists; low-memory pools never consult it, which is
        // the effect the source's disableThreadLocalGroups null check has on allocation.
        _threadLocalGroup = new LocalGroups(this, useCacheForNonEventLoopThreads);
    }

    private static MagazineGroup[] CreateMagazineGroupSizeClasses(
            AdaptivePoolingAllocator allocator, bool isThreadLocal)
    {
        MagazineGroup[] groups = new MagazineGroup[SIZE_CLASSES.Length];
        for (int i = 0; i < SIZE_CLASSES.Length; i++)
        {
            int segmentSize = SIZE_CLASSES[i];
            groups[i] = new MagazineGroup(allocator, allocator._chunkAllocator,
                    new SizeClassChunkManagementStrategy(allocator, segmentSize), isThreadLocal);
        }
        return groups;
    }

    internal ByteBuf Allocate(int size, int maxCapacity)
    {
        return new AdaptiveByteBuf(size, maxCapacity, _allocate, _reallocate);
    }

    // CLR: returns a reservation instead of initializing a recycled buffer; buf is the reservation
    // being replaced by a reallocation, or null.
    private AdaptiveChunkAllocation Allocate(int size, int maxCapacity, AdaptiveChunkAllocation buf)
    {
        using var events = BufferAllocatorEventSource.Log.BeginOperation();
        _lifetime.EnterReadLock();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            bool reallocate = buf != null;
            AdaptiveChunkAllocation allocated = null;
            if (size <= MaxPooledBufSize)
            {
                int index = SizeClassIndexOf(size);
                MagazineGroup[] magazineGroups;
                if (!FastThreadLocalThread.CurrentThreadWillCleanupFastThreadLocals() ||
                        _isLowMem ||
                        (magazineGroups = _threadLocalGroup.Get()) == null)
                {
                    magazineGroups = _sizeClassedMagazineGroups;
                }
                if (index < magazineGroups.Length)
                {
                    allocated = magazineGroups[index].Allocate(size, maxCapacity, reallocate);
                }
                else if (!_isLowMem)
                {
                    allocated = _largeBufferMagazineGroup.Allocate(size, maxCapacity, reallocate);
                }
            }
            if (allocated == null)
            {
                allocated = AllocateFallback(size, maxCapacity, buf);
            }
            return allocated;
        }
        finally
        {
            _lifetime.ExitReadLock();
        }
    }

    private static int SizeIndexOf(int size)
    {
        // this is aligning the size to the next multiple of 32 and dividing by 32 to get the size index.
        // CLR widens before addition so valid nonnegative input is safe in checked builds.
        return (int)(((long)size + 31) >> 5);
    }

    internal static int SizeClassIndexOf(int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        int sizeIndex = SizeIndexOf(size);
        if (sizeIndex < SIZE_INDEXES.Length)
        {
            return SIZE_INDEXES[sizeIndex];
        }
        return SIZE_CLASSES_COUNT;
    }

    internal static int[] GetSizeClasses()
    {
        return (int[])SIZE_CLASSES.Clone();
    }

    private AdaptiveChunkAllocation AllocateFallback(int size, int maxCapacity, AdaptiveChunkAllocation buf)
    {
        using var events = BufferAllocatorEventSource.Log.BeginOperation();
        // If we don't already have a buffer, obtain one from the most conveniently available magazine.
        // CLR: the outer wrapper is fresh and already bound to this pool; no recycler magazine is needed.
        // A dedicated Java fallback keeps its prior magazine association for
        // metadata/reallocation. CLR needs only that bit, not a recycler owner.
        bool threadLocal = buf?.CurrentThreadLocalAssociation ?? false;
        // Create a one-off chunk for this allocation.
        ByteBuf innerChunk = _chunkAllocator.Allocate(size, maxCapacity);
        Chunk chunk;
        try
        {
            chunk = new Chunk(innerChunk, _chunkRegistry);
            chunk.FallbackThreadLocal = threadLocal;
            chunk.TrackAllocation(threadLocal);
        }
        catch
        {
            innerChunk.Release();
            throw;
        }
        try
        {
            return chunk.TryAllocate(size) ?? throw new InvalidOperationException("A dedicated chunk was exhausted.");
        }
        finally
        {
            // As the chunk is an one-off we need to always call release explicitly as readInitInto(...)
            // will take care of retain once when successful. Once The AdaptiveByteBuf is released it will
            // completely release the Chunk and so the contained innerChunk.
            chunk.MarkToDeallocate();
        }
    }

    /**
     * Allocate into the given buffer. Used by {@link AdaptiveByteBuf#capacity(int)}.
     */
    // CLR: return a replacement reservation; AdaptiveByteBuf publishes it after copying.
    private AdaptiveChunkAllocation Reallocate(int size, int maxCapacity, AdaptiveChunkAllocation into)
    {
        return Allocate(size, maxCapacity, into);
    }

    internal long UsedMemory => _chunkRegistry.TotalCapacity;

    public void Dispose()
    {
        using var events = BufferAllocatorEventSource.Log.BeginOperation();
        // Ensure that we release all previous pooled resources when this object is finalized. This is needed as otherwise
        // we might end up with leaks. While these leaks are usually harmless in reality it would still at least be
        // very confusing for users.
        // CLR: IDisposable drains all groups; SafeHandle-backed delegates cover unreachable native storage.
        _lifetime.EnterWriteLock();
        try
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            lock (_localBindings)
            {
                foreach (var groups in _localBindings)
                {
                    foreach (var group in groups)
                    {
                        group.Dispose();
                    }
                }

                _localBindings.Clear();
            }

            // CLR: explicit lifetime closes every group, including all fixed classes.
            // Native delegates additionally have SafeHandle cleanup when unreachable.
            foreach (var group in _sizeClassedMagazineGroups)
            {
                group.Dispose();
            }

            _largeBufferMagazineGroup.Dispose();
        }
        finally
        {
            _lifetime.ExitWriteLock();
        }
    }

    // CLR: a named FastThreadLocal subclass replaces the source's anonymous threadLocalGroup class.
    internal sealed class LocalGroups(AdaptivePoolingAllocator allocator, bool useCacheForNonEventLoopThreads)
        : FastThreadLocal<MagazineGroup[]>
    {
        protected override MagazineGroup[] InitialValue()
        {
            if (useCacheForNonEventLoopThreads || ThreadExecutorMap.CurrentExecutor() != null)
            {
                MagazineGroup[] groups = CreateMagazineGroupSizeClasses(allocator, true);
                lock (allocator._localBindings)
                {
                    allocator._localBindings.Add(groups);
                }
                return groups;
            }
            return null;
        }

        protected override void OnRemoval(MagazineGroup[] groups)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            if (groups == null)
            {
                return;
            }
            // CLR: cleanup and allocator Dispose may run on different physical threads.
            // The write gate quiesces allocations/inspection before owner handoff.
            allocator._lifetime.EnterWriteLock();
            try
            {
                lock (allocator._localBindings)
                {
                    allocator._localBindings.Remove(groups);
                }

                foreach (MagazineGroup group in groups)
                {
                    group.Dispose();
                }
            }
            finally
            {
                allocator._lifetime.ExitWriteLock();
            }
        }
    }

    /**
     * The strategy for how {@link AdaptivePoolingAllocator} should allocate chunk buffers.
     */
    internal interface IChunkAllocator
    {
        /**
         * Allocate a buffer for a chunk. This can be any kind of {@link AbstractByteBuf} implementation.
         *
         * @param initialCapacity The initial capacity of the returned {@link AbstractByteBuf}.
         * @param maxCapacity     The maximum capacity of the returned {@link AbstractByteBuf}.
         * @return The buffer that represents the chunk memory.
         */
        // CLR: ByteBuf is the CLR base for the source's AbstractByteBuf.
        ByteBuf Allocate(int initialCapacity, int maxCapacity);
    }

    // CLR: no AdaptiveRecycler. Fresh AdaptiveByteBuf wrappers replace the recycler queues and
    // newBuffer(); recycling a released CLR object could resurrect stale references.
    internal sealed class Magazine : IDisposable
    {
        // CLR: an identity token replaces the Java sentinel chunk; it owns no raw storage.
        // Constructor only used by the MAGAZINE_FREED sentinel.
        private static readonly object MAGAZINE_FREED = new();

        private Chunk _current;
        // updated via NEXT_IN_LINE
        private object _nextInLine;
        private readonly MagazineGroup _group;
        private readonly IChunkController _chunkController;
        // CLR: shared magazines use a Monitor; local magazines rely on the pool lifetime gate.
        internal object AllocationLock { get; }
        internal IChunkController ChunkController => _chunkController;

        internal Magazine(MagazineGroup group, bool shareable, IChunkController chunkController)
        {
            _group = group;
            _chunkController = chunkController;

            if (shareable)
            {
                // We only need the StampedLock if this Magazine will be shared across threads.
                AllocationLock = new object();
            }
            else
            {
                AllocationLock = null;
            }
        }

        // CLR: returns the reservation (or null) instead of initializing a recycled buffer.
        internal AdaptiveChunkAllocation TryAllocate(int size, int maxCapacity, bool reallocate)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            if (AllocationLock == null)
            {
                // This magazine is not shared across threads, just allocate directly.
                return Allocate(size, maxCapacity, reallocate);
            }

            // Try to retrieve the lock and if successful allocate.
            if (Monitor.TryEnter(AllocationLock))
            {
                try
                {
                    return Allocate(size, maxCapacity, reallocate);
                }
                finally
                {
                    Monitor.Exit(AllocationLock);
                }
            }
            return AllocateWithoutLock(size, maxCapacity);
        }

        private AdaptiveChunkAllocation AllocateWithoutLock(int size, int maxCapacity)
        {
            object next = Interlocked.Exchange(ref _nextInLine, null);
            if (ReferenceEquals(next, MAGAZINE_FREED))
            {
                // Allocation raced with a stripe-resize that freed this magazine.
                RestoreMagazineFreed();
                return null;
            }
            Chunk curr = (Chunk)next;
            if (curr == null)
            {
                curr = _group.PollChunk(size);
                if (curr == null)
                {
                    return null;
                }
                curr.AttachToMagazine(this);
            }
            AdaptiveChunkAllocation allocated = null;
            try
            {
                int remainingCapacity = curr.RemainingCapacity;
                int startingCapacity = _chunkController.ComputeBufferCapacity(
                        size, maxCapacity, true /* never update stats as we don't hold the magazine lock */);
                if (remainingCapacity >= size)
                {
                    allocated = curr.TryAllocate(Math.Min(remainingCapacity, startingCapacity));
                }
                return allocated;
            }
            catch
            {
                allocated?.Release();
                throw;
            }
            finally
            {
                // CLR: restore ownership even if reservation initialization throws.
                if (curr.RemainingCapacity >= RETIRE_CAPACITY)
                {
                    TransferToNextInLineOrRelease(curr);
                }
                else
                {
                    curr.ReleaseFromMagazine();
                }
            }
        }

        private AdaptiveChunkAllocation Allocate(int size, int maxCapacity, bool reallocate)
        {
            int startingCapacity = _chunkController.ComputeBufferCapacity(size, maxCapacity, reallocate);
            Chunk curr = _current;
            if (curr != null)
            {
                AdaptiveChunkAllocation success = curr.TryAllocate(startingCapacity);
                int remainingCapacity = curr.RemainingCapacity;
                if (success == null && remainingCapacity > 0)
                {
                    _current = null;
                    TransferToNextInLineOrRelease(curr);
                }
                else if (remainingCapacity == 0)
                {
                    _current = null;
                    curr.ReleaseFromMagazine();
                }
                if (success != null)
                {
                    return success;
                }
            }

            // The fast-path for allocations did not work.
            //
            // Try to fetch the next "Magazine local" Chunk first, if this fails because we don't have a
            // next-in-line chunk available, we will poll our centralQueue.
            // If this fails as well we will just allocate a new Chunk.
            //
            // In any case we will store the Chunk as the current so it will be used again for the next allocation and
            // thus be "reserved" by this Magazine for exclusive usage.
            object next = Interlocked.Exchange(ref _nextInLine, null);
            if (next != null)
            {
                if (ReferenceEquals(next, MAGAZINE_FREED))
                {
                    // Allocation raced with a stripe-resize that freed this magazine.
                    RestoreMagazineFreed();
                    return null;
                }
                curr = (Chunk)next;

                bool kept = false;
                try
                {
                    int remainingCapacity = curr.RemainingCapacity;
                    AdaptiveChunkAllocation allocated;
                    if (remainingCapacity > startingCapacity &&
                            (allocated = curr.TryAllocate(startingCapacity)) != null)
                    {
                        // We have a Chunk that has some space left.
                        _current = curr;
                        kept = true;
                        return allocated;
                    }

                    if (remainingCapacity >= size)
                    {
                        // At this point we know that this will be the last time curr will be used, so directly set it
                        // to null and release it once we are done.
                        return curr.TryAllocate(remainingCapacity);
                    }
                }
                finally
                {
                    // Release in a finally block so even if readInitInto(...) would throw we would still correctly
                    // release the current chunk before null it out.
                    if (!kept)
                    {
                        curr.ReleaseFromMagazine();
                    }
                }
            }

            // Now try to poll from the central queue first
            curr = _group.PollChunk(size);
            if (curr == null)
            {
                curr = _chunkController.NewChunkAllocation(size, this);
            }
            else
            {
                curr.AttachToMagazine(this);

                int remainingCapacity = curr.RemainingCapacity;
                if (remainingCapacity == 0 || remainingCapacity < size)
                {
                    // Check if we either retain the chunk in the nextInLine cache or releasing it.
                    if (remainingCapacity < RETIRE_CAPACITY)
                    {
                        curr.ReleaseFromMagazine();
                    }
                    else
                    {
                        // See if it makes sense to transfer the Chunk to the nextInLine cache for later usage.
                        // This method will release curr if this is not the case
                        TransferToNextInLineOrRelease(curr);
                    }
                    curr = _chunkController.NewChunkAllocation(size, this);
                }
            }

            _current = curr;
            try
            {
                int remainingCapacity = curr.RemainingCapacity;
                if (remainingCapacity > startingCapacity)
                {
                    AdaptiveChunkAllocation success = curr.TryAllocate(startingCapacity);
                    curr = null;
                    return success;
                }
                return curr.TryAllocate(remainingCapacity);
            }
            finally
            {
                if (curr != null)
                {
                    // Release in a finally block so even if readInitInto(...) would throw we would still correctly
                    // release the current chunk before null it out.
                    curr.ReleaseFromMagazine();
                    _current = null;
                }
            }
        }

        private void RestoreMagazineFreed()
        {
            object next = Interlocked.Exchange(ref _nextInLine, MAGAZINE_FREED);
            if (next is Chunk chunk)
            {
                // A chunk snuck in through a race. Release it after restoring MAGAZINE_FREED state.
                chunk.ReleaseFromMagazine();
            }
        }

        private void TransferToNextInLineOrRelease(Chunk chunk)
        {
            if (Interlocked.CompareExchange(ref _nextInLine, chunk, null) == null)
            {
                return;
            }

            object next = Volatile.Read(ref _nextInLine);
            if (next is Chunk nextChunk && chunk.RemainingCapacity > nextChunk.RemainingCapacity)
            {
                if (ReferenceEquals(Interlocked.CompareExchange(ref _nextInLine, chunk, nextChunk), nextChunk))
                {
                    nextChunk.ReleaseFromMagazine();
                    return;
                }
            }
            // Next-in-line is occupied. We don't try to add it to the central queue yet as it might still be used
            // by some buffers and so is attached to a Magazine.
            // Once a Chunk is completely released by Chunk.release() it will try to move itself to the queue
            // as last resort.
            // CLR: releaseFromMagazine detaches and offers immediately, as the pinned implementation does.
            chunk.ReleaseFromMagazine();
        }

        public void Dispose()
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            // Release the current Chunk and the next that was stored for later usage.
            RestoreMagazineFreed();
            // CLR: a Monitor replaces the StampedLock write lock; thread-local magazines have none.
            object allocationLock = AllocationLock;
            if (allocationLock != null)
            {
                Monitor.Enter(allocationLock);
            }
            try
            {
                if (_current != null)
                {
                    _current.ReleaseFromMagazine();
                    _current = null;
                }
            }
            finally
            {
                if (allocationLock != null)
                {
                    Monitor.Exit(allocationLock);
                }
            }
        }

        internal bool OfferToQueue(Chunk chunk)
        {
            return _group.OfferChunk(chunk);
        }
    }

    internal sealed class MagazineGroup : IDisposable
    {
        private readonly AdaptivePoolingAllocator _allocator;
        private readonly IChunkAllocator _chunkAllocator;
        private readonly IChunkManagementStrategy _chunkManagementStrategy;
        private readonly IChunkCache _chunkCache;
        // CLR: a Monitor replaces the StampedLock; thread-local groups have none, as in the source.
        private readonly object _magazineExpandLock;
        private readonly Magazine _threadLocalMagazine;
        // CLR: no ownerThread field; a thread-local SizeClassedChunk records and hands over its
        // physical owner itself (see SizeClassedChunk.MarkToDeallocate).
        private Magazine[] _magazines;
        private bool _freed;
        internal AdaptivePoolingAllocator Allocator => _allocator;
        internal IChunkAllocator ChunkAllocator => _chunkAllocator;
        internal IChunkManagementStrategy ChunkManagementStrategy => _chunkManagementStrategy;
        internal IChunkCache ChunkCache => _chunkCache;
        internal Magazine[] Magazines => Volatile.Read(ref _magazines);
        internal bool Freed => Volatile.Read(ref _freed);

        internal MagazineGroup(AdaptivePoolingAllocator allocator,
                      IChunkAllocator chunkAllocator,
                      IChunkManagementStrategy chunkManagementStrategy,
                      bool isThreadLocal)
        {
            _allocator = allocator;
            _chunkAllocator = chunkAllocator;
            _chunkManagementStrategy = chunkManagementStrategy;
            _chunkCache = chunkManagementStrategy.CreateChunkCache(isThreadLocal);
            if (isThreadLocal)
            {
                _magazineExpandLock = null;
                _threadLocalMagazine = new Magazine(this, false, chunkManagementStrategy.CreateController(this));
                _magazines = [];
            }
            else
            {
                _magazineExpandLock = new object();
                _threadLocalMagazine = null;
                Magazine[] mags = new Magazine[INITIAL_MAGAZINES];
                for (int i = 0; i < mags.Length; i++)
                {
                    mags[i] = new Magazine(this, true, chunkManagementStrategy.CreateController(this));
                }
                _magazines = mags;
            }
        }

        // CLR: returns the reservation (or null) instead of initializing a recycled buffer.
        internal AdaptiveChunkAllocation Allocate(int size, int maxCapacity, bool reallocate)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            // Path for thread-local allocation.
            Magazine tlMag = _threadLocalMagazine;
            if (tlMag != null)
            {
                return tlMag.TryAllocate(size, maxCapacity, reallocate);
            }

            // Path for concurrent allocation.
            int threadId = Environment.CurrentManagedThreadId;
            Magazine[] mags;
            int expansions = 0;
            do
            {
                mags = Magazines;
                int mask = mags.Length - 1;
                int index = threadId & mask;
                for (int i = 0, m = mags.Length << 1; i < m; i++)
                {
                    Magazine mag = mags[(index + i) & mask];
                    AdaptiveChunkAllocation allocation = mag.TryAllocate(size, maxCapacity, reallocate);
                    if (allocation != null)
                    {
                        // Was able to allocate.
                        return allocation;
                    }
                }
                expansions++;
            } while (expansions <= EXPANSION_ATTEMPTS && TryExpandMagazines(mags.Length));

            // The magazines failed us; contention too high and we don't want to spend more effort expanding the array.
            // CLR: no unused outer recycler object was claimed on this reservation path.
            return null;
        }

        private bool TryExpandMagazines(int currentLength)
        {
            if (currentLength >= _allocator._maxStripes)
            {
                return true;
            }
            Magazine[] mags;
            // CLR: Monitor.TryEnter replaces StampedLock.tryWriteLock.
            if (Monitor.TryEnter(_magazineExpandLock))
            {
                try
                {
                    mags = Magazines;
                    if (mags.Length >= _allocator._maxStripes || mags.Length > currentLength || Freed)
                    {
                        return true;
                    }
                    Magazine[] expanded = new Magazine[mags.Length * 2];
                    for (int i = 0, l = expanded.Length; i < l; i++)
                    {
                        expanded[i] = new Magazine(this, true, _chunkManagementStrategy.CreateController(this));
                    }
                    Volatile.Write(ref _magazines, expanded);
                }
                finally
                {
                    Monitor.Exit(_magazineExpandLock);
                }
                foreach (Magazine magazine in mags)
                {
                    magazine.Dispose();
                }
            }
            return true;
        }

        internal Chunk PollChunk(int size)
        {
            return _chunkCache.PollChunk(size);
        }

        internal bool OfferChunk(Chunk chunk)
        {
            if (Freed)
            {
                return false;
            }

            if (chunk.HasUnprocessedFreelistEntries)
            {
                chunk.ProcessFreelistEntries();
            }
            bool isAdded = _chunkCache.OfferChunk(chunk);

            if (Freed && isAdded)
            {
                // Help to free the reuse queue.
                FreeChunkReuseQueue();
            }
            return isAdded;
        }

        public void Dispose()
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            Volatile.Write(ref _freed, true);
            if (_threadLocalMagazine != null)
            {
                _threadLocalMagazine.Dispose();
            }
            else
            {
                lock (_magazineExpandLock)
                {
                    Magazine[] mags = Magazines;
                    foreach (Magazine magazine in mags)
                    {
                        magazine.Dispose();
                    }
                }
            }
            FreeChunkReuseQueue();
        }

        // CLR: no owner thread is handed over here; SizeClassedChunk.MarkToDeallocate clears its own
        // physical owner under its local-return lock.
        private void FreeChunkReuseQueue()
        {
            _chunkCache.Dispose();
        }
    }

    /**
     * MPMC queue cache for shared (cross-thread) chunk reuse.
     *
     * <p><b>scanForCapacity</b> — LRU preference with fallback:
     * <pre>
     *   fast path: head chunk has purgeEpoch == 0 and capacity → return O(1)
     *
     *   slow path: scan for epoch=0 chunk, hold first idle (epoch &gt; 0) as fallback
     *     queue: [E&gt;0, E&gt;0, E=0, E&gt;0, ...]
     *             skip   skip  ↑ return (put fallback back)
     *
     *   no epoch=0 found → use fallback, reset its epoch to 0
     * </pre>
     *
     * <p>The LRU preference creates a natural separation: recently-used chunks (epoch=0,
     * returned via {@link #offerChunk} after magazine use) cycle at the front. Idle chunks
     * (epoch &gt; 0, aged by purge) are scanned past but never returned — they age undisturbed.
     * When no recently-used chunks exist, idle ones are reused (fallback) rather than
     * allocating new chunks.
     *
     * <p>All re-offered chunks are stamped with {@code lastScanGeneration} for cycle detection.
     * The {@code >=} check terminates the scan when encountering any chunk already processed
     * by this or a later scan, preventing livelock under concurrent access.
     *
     * <p><b>runPurgeScan</b> (every {@link #CHUNK_PURGE_POLLS_SHARED} polls):
     * drains the queue, ages full chunks (epoch++), resets non-full (epoch=0).
     * Non-candidate capacity chunks are re-offered inline. Eviction candidates (full,
     * epoch past threshold) and no-capacity chunks are deferred to a buffer. After the drain,
     * the buffer is walked with the known total: candidates are evicted while above
     * {@link #CHUNK_REUSE_QUEUE}, remainder re-offered. No selection — that is
     * {@code scanForCapacity}'s job (called after purge via {@code pollChunk}).
     */
    // CLR: the cache gate serializes BCL FIFO operations, selection and purge. Allocation
    // is still striped across magazine locks; external segment returns remain concurrent.
    internal sealed class SharedSizeClassedChunkCache : SizeClassedChunkCache
    {
        private readonly object _sync = new();
        private readonly int _sharedCacheCapacity;
        private readonly Queue<SizeClassedChunk> _queue = new();
        private long _purgeBudget;
        private readonly List<SizeClassedChunk> _deferredBuffer = new();
        private long _purgeGeneration;
        private long _scanGeneration;
        // CLR: per-instance CHUNK_REUSE_QUEUE, CHUNK_PURGE_POLLS_SHARED and CHUNK_PURGE_THRESHOLD.
        private readonly int _chunkReuseQueue, _chunkPurgeThreshold;
        private readonly long _chunkPurgePollsShared;
        private bool _disposed;

        internal SharedSizeClassedChunkCache(int chunkReuseQueue, long chunkPurgePollsShared, int chunkPurgeThreshold)
        {
            if (chunkReuseQueue < 2 || chunkReuseQueue > 1 << 28)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkReuseQueue));
            }
            ArgumentOutOfRangeException.ThrowIfLessThan(chunkPurgePollsShared, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(chunkPurgeThreshold, 1);
            _chunkReuseQueue = chunkReuseQueue;
            // Must exceed CHUNK_REUSE_QUEUE (the retention floor) to leave room for burst absorption.
            // TODO replace with an unbounded concurrent collection once available.
            // CLR: preserve the JVM MPMC ring's power-of-two capacity while using a bounded BCL FIFO.
            _sharedCacheCapacity = MathUtil.SafeFindNextPositivePowerOfTwo(Math.Max(16, chunkReuseQueue * 2));
            _chunkPurgePollsShared = _purgeBudget = chunkPurgePollsShared;
            _chunkPurgeThreshold = chunkPurgeThreshold;
        }

        public override bool IsEmpty
        {
            get
            {
                lock (_sync)
                {
                    return _queue.Count == 0;
                }
            }
        }
        internal int Count
        {
            get
            {
                lock (_sync)
                {
                    return _queue.Count;
                }
            }
        }

        internal override SizeClassedChunk ForcePurge()
        {
            lock (_sync)
            {
                _purgeBudget = 1;
                return PollChunk(0);
            }
        }

        public override SizeClassedChunk PollChunk(int size)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            lock (_sync)
            {
                if (_disposed)
                {
                    return null;
                }
                long budget = --_purgeBudget;
                if (budget == 0)
                {
                    RunPurgeScan();
                }
                return ScanForCapacity();
            }
        }

        private SizeClassedChunk ScanForCapacity()
        {
            if (!_queue.TryDequeue(out SizeClassedChunk first))
            {
                return null;
            }
            if (first.PurgeEpoch == 0 && first.HasRemainingCapacity)
            {
                return first;
            }
            long generation = _scanGeneration = unchecked(_scanGeneration + 1);
            first.LastScanGeneration = generation;
            if (first.HasRemainingCapacity)
            {
                return ScanForCapacitySlow(generation, first);
            }
            OfferOrDeallocate(first);
            return ScanForCapacitySlow(generation, null);
        }

        private SizeClassedChunk ScanForCapacitySlow(long generation, SizeClassedChunk fallback)
        {
            while (_queue.TryDequeue(out SizeClassedChunk chunk))
            {
                if (chunk.LastScanGeneration >= generation)
                {
                    OfferOrDeallocate(chunk);
                    break;
                }
                if (chunk.HasRemainingCapacity)
                {
                    if (chunk.PurgeEpoch == 0)
                    {
                        if (fallback != null)
                        {
                            OfferOrDeallocate(fallback);
                        }
                        return chunk;
                    }
                    if (fallback == null)
                    {
                        fallback = chunk;
                        continue;
                    }
                }
                chunk.LastScanGeneration = generation;
                OfferOrDeallocate(chunk);
            }
            if (fallback != null)
            {
                fallback.PurgeEpoch = 0;
                return fallback;
            }
            return null;
        }

        private bool OfferOrDeallocate(SizeClassedChunk chunk)
        {
            if (_queue.Count == _sharedCacheCapacity)
            {
                chunk.MarkToDeallocate();
                return false;
            }
            _queue.Enqueue(chunk);
            return true;
        }

        private bool OfferOrDeallocate(SizeClassedChunk chunk, long generation)
        {
            chunk.LastPurgeGeneration = generation;
            return OfferOrDeallocate(chunk);
        }

        private void RunPurgeScan()
        {
            long generation = _purgeGeneration = unchecked(_purgeGeneration + 1);
            int retained = 0;
            List<SizeClassedChunk> deferred = _deferredBuffer;
            while (_queue.TryDequeue(out SizeClassedChunk chunk))
            {
                if (chunk.LastPurgeGeneration == generation)
                {
                    OfferOrDeallocate(chunk, generation);
                    break;
                }
                retained++;
                if (chunk.HasFullCapacity)
                {
                    chunk.PurgeEpoch = unchecked(chunk.PurgeEpoch + 1);
                    if (chunk.PurgeEpoch > _chunkPurgeThreshold)
                    {
                        deferred.Add(chunk);
                        continue;
                    }
                }
                else
                {
                    chunk.PurgeEpoch = 0;
                }
                int remaining = chunk.RemainingCapacity;
                if (remaining > 0)
                {
                    if (!OfferOrDeallocate(chunk, generation))
                    {
                        retained--;
                    }
                }
                else
                {
                    deferred.Add(chunk);
                }
            }
            for (int i = 0, size = deferred.Count; i < size; i++)
            {
                SizeClassedChunk chunk = deferred[i];
                if (chunk.PurgeEpoch > _chunkPurgeThreshold && retained > _chunkReuseQueue)
                {
                    chunk.MarkToDeallocate();
                    retained--;
                }
                else
                {
                    if (!OfferOrDeallocate(chunk, generation))
                    {
                        retained--;
                    }
                }
            }
            deferred.Clear();
            _purgeBudget = _chunkPurgePollsShared;
        }

        public override bool OfferChunk(Chunk chunk)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            lock (_sync)
            {
                if (_disposed || _queue.Count == _sharedCacheCapacity)
                {
                    return false;
                }
                _queue.Enqueue((SizeClassedChunk)chunk);
                return true;
            }
        }

        public override void Dispose()
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            lock (_sync)
            {
                _disposed = true;
                while (_queue.TryDequeue(out SizeClassedChunk chunk))
                {
                    chunk.MarkToDeallocate();
                }
            }
        }
    }

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
    internal sealed class ThreadLocalSizeClassedChunkCache : SizeClassedChunkCache
    {
        private SizeClassedChunk[] _chunks; // package-private for testing
        private int _head;
        private int _tail;
        private int _count;
        private int _notEmptyCount;
        private long _purgeBudget;
        // CLR: per-instance CHUNK_PURGE_POLLS_THREAD_LOCAL and CHUNK_PURGE_THRESHOLD.
        private readonly long _chunkPurgePollsThreadLocal;
        private readonly int _chunkPurgeThreshold;
        private bool _disposed;
        internal int Count => _count;
        internal int RingLength => _chunks.Length;
        internal readonly int MaxCachedChunks; // package-private for testing
        internal readonly int PurgeRetentionFloor; // package-private for testing

        // CLR: threadLocalCacheMaxBytes/threadLocalCacheMinBytes replace THREAD_LOCAL_CACHE_MAX_BYTES/MIN_BYTES.
        internal ThreadLocalSizeClassedChunkCache(int chunkSize, long chunkPurgePollsThreadLocal, int chunkPurgeThreshold,
            int threadLocalCacheMaxBytes, int threadLocalCacheMinBytes)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(chunkPurgePollsThreadLocal, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(chunkPurgeThreshold, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(threadLocalCacheMaxBytes, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(threadLocalCacheMinBytes, 1);
            _chunkPurgePollsThreadLocal = chunkPurgePollsThreadLocal;
            _chunkPurgeThreshold = chunkPurgeThreshold;
            _chunks = new SizeClassedChunk[8];
            _purgeBudget = _chunkPurgePollsThreadLocal;
            MaxCachedChunks = Math.Max(1, threadLocalCacheMaxBytes / chunkSize);
            PurgeRetentionFloor = Math.Min(MaxCachedChunks,
                    Math.Max(1, threadLocalCacheMinBytes / chunkSize));
        }

        internal override SizeClassedChunk ForcePurge()
        {
            _purgeBudget = 1;
            return PollChunk(0);
        }

        public override SizeClassedChunk PollChunk(int size)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            if (_disposed)
            {
                return null;
            }
            if (--_purgeBudget == 0)
            {
                RunPurgeScan();
            }
            return ScanForCapacity();
        }

        private SizeClassedChunk ScanForCapacity()
        {
            if (_notEmptyCount > 0)
            {
                SizeClassedChunk chunk = _chunks[_head];
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

        private SizeClassedChunk ScanForCapacityFallback()
        {
            int mask = _chunks.Length - 1;
            int emptyCount = _count - _notEmptyCount;
            int pos = (_head + _notEmptyCount) & mask;
            for (int i = 0; i < emptyCount; i++)
            {
                SizeClassedChunk chunk = _chunks[pos];
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
                SizeClassedChunk chunk = _chunks[readIdx];
                if (chunk.PurgeEpoch > 0)
                {
                    Debug.Assert(chunk.HasFullCapacity);
                    chunk.PurgeEpoch = unchecked(chunk.PurgeEpoch + 1);
                    if (chunk.PurgeEpoch > _chunkPurgeThreshold && survivors > PurgeRetentionFloor)
                    {
                        chunk.MarkToDeallocate();
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
            _purgeBudget = _chunkPurgePollsThreadLocal;
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
                    SizeClassedChunk tmp = _chunks[loIdx];
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
                SizeClassedChunk c = _chunks[emidIdx];
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
                else if (c.PurgeEpoch < _chunkPurgeThreshold)
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

        public override bool OfferChunk(Chunk chunk)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            if (_disposed || _count >= MaxCachedChunks)
            {
                return false;
            }
            if (_count == _chunks.Length)
            {
                SizeClassedChunk[] newChunks = new SizeClassedChunk[_chunks.Length * 2];
                for (int i = 0; i < _count; i++)
                {
                    newChunks[i] = _chunks[(_head + i) & (_chunks.Length - 1)];
                }
                _chunks = newChunks;
                _head = 0;
                _tail = _count;
            }
            _chunks[_tail] = (SizeClassedChunk)chunk;
            _tail = (_tail + 1) & (_chunks.Length - 1);
            _count++;
            return true;
        }

        public override string ToString()
        {
            int mask = _chunks.Length - 1;
            var sb = new StringBuilder();
            sb.Append("ThreadLocalCache[head=").Append(_head.ToString(CultureInfo.InvariantCulture)).Append(", tail=").Append(_tail.ToString(CultureInfo.InvariantCulture))
                .Append(", count=").Append(_count.ToString(CultureInfo.InvariantCulture)).Append(", notEmpty=").Append(_notEmptyCount.ToString(CultureInfo.InvariantCulture))
                .Append(", length=").Append(_chunks.Length.ToString(CultureInfo.InvariantCulture)).Append("]\n  ");
            for (int i = 0; i < _count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                if (i == _notEmptyCount)
                {
                    sb.Append("| ");
                }
                SizeClassedChunk c = _chunks[(_head + i) & mask];
                string region = i < _notEmptyCount ? "notEmpty" : "empty";
                string actual = c == null ? "null" : c.HasRemainingCapacity ? "hasCap" : "noCap";
                sb.Append('[').Append(region).Append(':').Append(actual)
                    .Append(",ep=").Append((c == null ? -1 : c.PurgeEpoch).ToString(CultureInfo.InvariantCulture)).Append(']');
            }
            return sb.ToString();
        }

        public override void Dispose()
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            _disposed = true;
            int mask = _chunks.Length - 1;
            for (int i = 0; i < _count; i++)
            {
                int idx = (_head + i) & mask;
                _chunks[idx].MarkToDeallocate();
                _chunks[idx] = null;
            }
            _head = 0;
            _tail = 0;
            _count = 0;
            _notEmptyCount = 0;
        }

        public override bool IsEmpty => _count == 0;
    }
}
