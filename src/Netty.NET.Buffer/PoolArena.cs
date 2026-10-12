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
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer;

// CLR: Memory<byte> shares the allocation kernel across heap and native storage.
// Thread caches, allocator events and automatic leak wrappers remain pending.
internal abstract partial class PoolArena : IDisposable, IPoolArenaMetric
{
    private readonly object _sync = new();
    private readonly Dictionary<PoolChunk, ChunkStorage> _chunks = new();
    private readonly PoolSubpage[] _smallSubpagePools;
    private readonly PoolChunkList _qInit, _q000, _q025, _q050, _q075, _q100;
    private readonly PoolChunkList[] _chunkLists;
    private readonly bool _direct;
    private ChunkStorage _lastDestroyedChunk;
    // TODO: Test if adding padding helps under contention
    //private long pad0, pad1, pad2, pad3, pad4, pad5, pad6, pad7;
    private bool _disposed;
    internal object SyncRoot => _sync;
    internal IReadOnlyList<PoolChunkList> ChunkLists { get; }
    internal IByteBufAllocator Parent { get; }
    internal SizeClasses SizeClass { get; }

    protected PoolArena(IByteBufAllocator parent, SizeClasses sizeClass, bool direct)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(sizeClass);
        if (!direct && sizeClass.DirectMemoryCacheAlignment != 0)
            throw new ArgumentException("Heap arenas do not align native addresses.", nameof(sizeClass));
        // Validate chunk geometry before any backing storage is reserved.
        _ = new PoolChunk(sizeClass);
        Parent = parent;
        SizeClass = sizeClass;
        _direct = direct;
        _smallSubpagePools = PoolSubpage.CreatePoolHeads(sizeClass);
        _q100 = new PoolChunkList(this, null, 100, int.MaxValue, sizeClass.ChunkSize);
        _q075 = new PoolChunkList(this, _q100, 75, 100, sizeClass.ChunkSize);
        _q050 = new PoolChunkList(this, _q100, 50, 100, sizeClass.ChunkSize);
        _q025 = new PoolChunkList(this, _q050, 25, 75, sizeClass.ChunkSize);
        _q000 = new PoolChunkList(this, _q025, 1, 50, sizeClass.ChunkSize);
        _qInit = new PoolChunkList(this, _q000, int.MinValue, 25, sizeClass.ChunkSize);
        _q100.SetPrevious(_q075);
        _q075.SetPrevious(_q050);
        _q050.SetPrevious(_q025);
        _q025.SetPrevious(_q000);
        _q000.SetPrevious(null);
        _qInit.SetPrevious(_qInit);
        _chunkLists = new[] { _qInit, _q000, _q025, _q050, _q075, _q100 };
        ChunkLists = Array.AsReadOnly(_chunkLists);
    }

    internal abstract PooledByteBuf Allocate(int initialCapacity, int maxCapacity);
    protected abstract Memory<byte> AllocateBacking(int length, out NativeMemoryOwner owner);

    internal Allocation AllocateMemory(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            int sizeIdx = SizeClass.Size2SizeIdx(capacity);
            if (sizeIdx == SizeClass.NSizes)
            {
                // Huge allocations are never served via the cache so just call allocateHuge
                return AllocateHuge(SizeClass.DirectMemoryCacheAlignment > 0
                    ? SizeClass.NormalizeSize(capacity) : capacity);
            }

            int normalized = SizeClass.SizeIdx2Size(sizeIdx);
            // Available subpages are selected before applying chunk-list capacity limits.
            // Taking a slot from an existing run does not change its owning chunk's usage.
            if (sizeIdx <= SizeClass.SmallMaxSizeIdx)
            {
                PoolSubpage head = _smallSubpagePools[sizeIdx];
                lock (head.SyncRoot)
                {
                    PoolSubpage subpage = head.Next;
                    if (subpage != head)
                        return Bind(_chunks[subpage.Chunk], subpage.Allocate(), normalized);
                }
            }

            if (_q050.TryAllocate(sizeIdx, out PoolChunk owner, out long handle) ||
                _q025.TryAllocate(sizeIdx, out owner, out handle) ||
                _q000.TryAllocate(sizeIdx, out owner, out handle) ||
                _qInit.TryAllocate(sizeIdx, out owner, out handle) ||
                _q075.TryAllocate(sizeIdx, out owner, out handle))
                return Bind(_chunks[owner], handle, normalized);

            // Add a new chunk.
            ChunkStorage storage = _lastDestroyedChunk;
            // The parameters are always the same, so it's fine to reuse a previously allocated chunk.
            Debug.Assert(storage == null || ReferenceEquals(storage.Chunk.SizeClass, SizeClass));
            var chunk = storage?.Chunk ?? new PoolChunk(SizeClass, _smallSubpagePools);
            if (sizeIdx <= SizeClass.SmallMaxSizeIdx && chunk.CalculateRunSize(sizeIdx) < 0)
            {
                // CLR: tiny chunk geometries can be shorter than the small class's
                // LCM run. Use a dedicated reservation, never a truncated slot.
                return AllocateHuge(SizeClass.DirectMemoryCacheAlignment == 0 ? capacity : normalized);
            }
            _lastDestroyedChunk = null;
            storage ??= NewStorage(chunk, SizeClass.ChunkSize);
            try { _chunks.Add(chunk, storage); }
            catch { storage.Owner?.Dispose(); throw; }
            // Publish storage before a subpage can join a shared head. Allocation
            // failures then leave either no chunk or a reusable registered chunk.
            long newHandle;
            PoolChunk newOwner;
            try
            {
                newHandle = AllocateHandle(chunk, sizeIdx, normalized, out newOwner);
                if (newHandle < 0)
                    throw new InvalidOperationException("A new chunk could not satisfy its size class.");
            }
            catch
            {
                // Keep registered storage reachable by shutdown and later reuse even
                // if subpage metadata allocation fails before the initial binding.
                TrackChunk(chunk);
                throw;
            }
            TrackChunk(chunk);
            return Bind(_chunks[newOwner], newHandle, normalized);
        }
    }

    private void TrackChunk(PoolChunk chunk)
    {
        _qInit.Add(chunk);
        unchecked { ++_chunkAllocations; }
    }

    internal long AllocateHandle(PoolChunk chunk, int sizeIdx, int normalized, out PoolChunk owner)
    {
        if (sizeIdx <= SizeClass.SmallMaxSizeIdx)
        {
            /*
             * Synchronize on the head. This is needed as {@link PoolChunk#allocateSubpage(int)} and
             * {@link PoolChunk#free(long)} may modify the doubly linked list as well.
             */
            // CLR: AllocateSmall owns that scoped head lock, inside the arena lock.
            return chunk.AllocateSmall(sizeIdx, out owner);
        }
        owner = chunk;
        return chunk.AllocateRun(normalized);
    }

    private Allocation Bind(ChunkStorage storage, long handle, int normalized)
    {
        try
        {
            int offset = PoolChunk.RunOffset(handle) << SizeClass.PageShifts;
            if (PoolChunk.IsSubpage(handle)) offset += PoolChunk.BitmapIdx(handle) * normalized;
            var allocation = new Allocation(this, storage, handle, offset, normalized);
            ++storage.ActiveAllocations;
            unchecked
            {
                if (PoolChunk.IsSubpage(handle)) ++_smallAllocations;
                else ++_normalAllocations;
                _pinnedBytes += normalized;
            }
            return allocation;
        }
        catch
        {
            if (!storage.Chunk.Parent.Free(storage.Chunk, handle)) DestroyChunk(storage.Chunk);
            throw;
        }
    }

    private ChunkStorage NewStorage(PoolChunk chunk, int length)
    {
        Memory<byte> memory = AllocateBacking(length, out NativeMemoryOwner owner);
        try { return new ChunkStorage(chunk, memory, owner); }
        catch { owner?.Dispose(); throw; }
    }

    private Allocation AllocateHuge(int capacity)
    {
        /* unpooled buffers are never allocated out of the thread-local cache */
        ChunkStorage storage = NewStorage(null, capacity);
        try
        {
            var allocation = new Allocation(this, storage, -1, 0, capacity);
            unchecked
            {
                ++_hugeAllocations;
                _activeBytesHuge += capacity;
                _pinnedBytes += capacity;
            }
            return allocation;
        }
        catch { storage.Owner?.Dispose(); throw; }
    }

    internal static void FreeHandle(PoolChunk chunk, long handle)
    {
        if (PoolChunk.IsSubpage(handle)) chunk.FreeSmall(handle);
        else chunk.FreeRun(handle);
    }

    private void Free(Allocation allocation)
    {
        ChunkStorage storage = allocation.Storage;
        lock (_sync)
        {
            unchecked { _pinnedBytes -= allocation.ReservedLength; }
            if (storage.Chunk == null)
            {
                unchecked
                {
                    ++_hugeDeallocations;
                    _activeBytesHuge -= allocation.ReservedLength;
                }
                storage.Owner?.Dispose();
                return;
            }
            unchecked
            {
                if (PoolChunk.IsSubpage(allocation.Handle)) ++_smallDeallocations;
                else ++_normalDeallocations;
            }
            --storage.ActiveAllocations;
            if (_disposed)
            {
                if (storage.ActiveAllocations == 0) DestroyChunk(storage.Chunk);
            }
            else if (!storage.Chunk.Parent.Free(storage.Chunk, allocation.Handle))
                DestroyChunk(storage.Chunk);
        }
    }

    internal void DestroyChunk(PoolChunk chunk)
    {
        Debug.Assert(System.Threading.Monitor.IsEntered(_sync));
        ChunkStorage storage = _chunks[chunk];
        // CLR: after explicit shutdown, active storage stays in the metric registry
        // until its last region lease returns, even though it has left the lists.
        if (_disposed && storage.ActiveAllocations != 0) return;
        _chunks.Remove(chunk);
        unchecked { ++_chunkDeallocations; }
        if (_disposed)
        {
            if (storage.ActiveAllocations == 0) storage.Owner?.Dispose();
            return;
        }
        Debug.Assert(storage.ActiveAllocations == 0 && chunk.FreeBytes == chunk.ChunkSize);
        // Rely on GC. But keep one chunk for reuse.
        // CLR: the arena Monitor makes the original AtomicReference unnecessary.
        if (!_direct && _lastDestroyedChunk == null)
            // The check-and-set does not need to be atomic.
            _lastDestroyedChunk = storage;
        else
        {
            // destroyChunk not need to be called while holding the synchronized lock.
            // CLR: owner cleanup has no arena/user callback; keep release serialized
            // with shutdown and storage registration under the same Monitor.
            storage.Owner?.Dispose();
        }
    }

    // CLR: closing an arena stops new reservations. Live buffers and internal
    // operation leases keep their storage usable until the last reservation returns.
    // Native owners release physical memory; outstanding native pins defer that free.
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (PoolChunkList list in _chunkLists) list.Destroy();
            _lastDestroyedChunk = null;
            Array.Clear(_smallSubpagePools);
        }
    }

    internal sealed class ChunkStorage
    {
        internal readonly PoolChunk Chunk;
        internal readonly Memory<byte> Memory;
        internal readonly NativeMemoryOwner Owner;
        internal int ActiveAllocations;

        internal ChunkStorage(PoolChunk chunk, Memory<byte> memory, NativeMemoryOwner owner)
        {
            Chunk = chunk;
            Memory = memory;
            Owner = owner;
        }
    }

    // A buffer owns one reference to its reservation. Internal span operations
    // acquire another, so replacing/releasing a buffer cannot recycle its source
    // region before that operation finishes. Raw public Memory/Span views remain
    // borrowed and must not be accessed after replacement or final buffer release.
    internal sealed class Allocation : IDisposable
    {
        private readonly PoolArena _arena;
        private int _references = 1;
        internal readonly ChunkStorage Storage;
        internal PoolChunk Chunk => Storage.Chunk;
        internal Memory<byte> Memory => Storage.Memory;
        internal readonly long Handle;
        internal readonly int Offset;
        internal readonly int ReservedLength;
        internal bool IsPooled => Chunk != null;

        internal Allocation(PoolArena arena, ChunkStorage storage, long handle, int offset, int reservedLength)
        {
            _arena = arena;
            Storage = storage;
            Handle = handle;
            Offset = offset;
            ReservedLength = reservedLength;
        }

        internal BufferMemoryLease AcquireLease()
        {
            ReferenceCountUpdater.Retain(ref _references, 1);
            return new BufferMemoryLease(this);
        }

        public void Dispose()
        {
            if (ReferenceCountUpdater.Release(ref _references, 1)) _arena.Free(this);
        }
    }
}
