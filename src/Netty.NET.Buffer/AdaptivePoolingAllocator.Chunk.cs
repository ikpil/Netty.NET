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
using System.Threading;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer;

internal sealed partial class AdaptivePoolingAllocator
{
    // CLR: atomics replace LongAdder. Count physical chunk capacity, including cached
    // chunks and chunks awaiting their last segment/operation lease. No membership set.
    internal sealed class ChunkRegistry
    {
        private long _totalCapacity;

        internal long TotalCapacity => Volatile.Read(ref _totalCapacity);

        internal void Add(Chunk chunk) => Interlocked.Add(ref _totalCapacity, chunk.Capacity);

        internal void Remove(Chunk chunk) => Interlocked.Add(ref _totalCapacity, -chunk.Capacity);
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
        internal ByteBuf Copy(int index, int length) => Storage.Copy(index, length);
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
        private protected void Register() => _chunkRegistry.Add(this);

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
        internal virtual void ReleaseSegment(int ignoredSegmentId, int size) => Release();

        // CLR: markToDeallocate retires the magazine/cache ownership once. Live allocations and
        // leases still own their segments. Calling it again must not release that ownership again.
        internal virtual void MarkToDeallocate()
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            if (TryRetire()) Release();
        }

        // CLR: IDisposable scopes a chunk; disposing it is markToDeallocate().
        public void Dispose() => MarkToDeallocate();

        private protected void ThrowIfRetired() => ObjectDisposedException.ThrowIf(IsRetired, this);
        private protected bool TryRetire() => Interlocked.Exchange(ref _retired, 1) == 0;
        private protected void Retain() => ReferenceCountUpdater.Retain(ref _refCnt);

        private protected void Release()
        {
            if (ReferenceCountUpdater.Release(ref _refCnt)) Deallocate();
        }

        private protected void Deallocate()
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            ByteBuf @delegate = Interlocked.Exchange(ref _delegate, null);
            if (@delegate == null) return;
            OnRelease(@delegate);
            _chunkRegistry.Remove(this);
            @delegate.Release();
        }

        private void OnRelease(ByteBuf @delegate)
        {
            if (_eventsTracked) BufferAllocatorEventSource.Log.AdaptiveChunkChanged(@delegate, Pooled, false, allocated: false);
        }

        // CLR: the Memory-based counterpart of readInitInto(buf, size, startingCapacity, maxCapacity).
        // It returns a reservation that AdaptiveByteBuf consumes instead of initializing the buffer's
        // chunk and startIndex, and null where readInitInto returns false.
        internal virtual AdaptiveChunkAllocation TryAllocate(int startingCapacity)
        {
            ThrowIfRetired();
            ArgumentOutOfRangeException.ThrowIfNegative(startingCapacity);
            if (startingCapacity > Capacity - AllocatedBytes) return null;
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
}
