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
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer;

// CLR: Memory<byte> shares the allocation kernel across heap and native storage.
// Public allocator selection, utilization lists/eviction, metrics and caches remain pending.
// Pooled chunks are retained for reuse until this arena is disposed.
internal abstract class PoolArena : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<PoolChunk, ChunkStorage> _chunks = new();
    private readonly PoolSubpage[] _smallSubpagePools;
    private bool _disposed;
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
        _smallSubpagePools = PoolSubpage.CreatePoolHeads(sizeClass);
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
            // CLR: use existing chunks directly while utilization lists are pending.
            foreach (var entry in _chunks)
            {
                long handle = AllocateHandle(entry.Key, sizeIdx, normalized, out PoolChunk owner);
                if (handle >= 0) return Bind(_chunks[owner], handle, normalized);
            }

            // Add a new chunk.
            var chunk = new PoolChunk(SizeClass, _smallSubpagePools);
            if (sizeIdx <= SizeClass.SmallMaxSizeIdx && chunk.CalculateRunSize(sizeIdx) < 0)
            {
                // CLR: tiny chunk geometries can be shorter than the small class's
                // LCM run. Use a dedicated reservation, never a truncated slot.
                return AllocateHuge(SizeClass.DirectMemoryCacheAlignment == 0 ? capacity : normalized);
            }
            ChunkStorage storage = NewStorage(chunk, SizeClass.ChunkSize);
            try { _chunks.Add(chunk, storage); }
            catch { storage.Owner?.Dispose(); throw; }
            // Publish storage before a subpage can join a shared head. Allocation
            // failures then leave either no chunk or a reusable registered chunk.
            long newHandle = AllocateHandle(chunk, sizeIdx, normalized, out PoolChunk newOwner);
            if (newHandle < 0)
                throw new InvalidOperationException("A new chunk could not satisfy its size class.");
            return Bind(_chunks[newOwner], newHandle, normalized);
        }
    }

    private long AllocateHandle(PoolChunk chunk, int sizeIdx, int normalized, out PoolChunk owner)
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
            return allocation;
        }
        catch
        {
            FreeHandle(storage.Chunk, handle);
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
        try { return new Allocation(this, storage, -1, 0, capacity); }
        catch { storage.Owner?.Dispose(); throw; }
    }

    private static void FreeHandle(PoolChunk chunk, long handle)
    {
        if (PoolChunk.IsSubpage(handle)) chunk.FreeSmall(handle);
        else chunk.FreeRun(handle);
    }

    private void Free(Allocation allocation)
    {
        ChunkStorage storage = allocation.Storage;
        if (storage.Chunk == null)
        {
            storage.Owner?.Dispose();
            return;
        }
        lock (_sync)
        {
            if (!_disposed) FreeHandle(storage.Chunk, allocation.Handle);
            if (--storage.ActiveAllocations == 0 && _disposed) storage.Owner?.Dispose();
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
            foreach (ChunkStorage storage in _chunks.Values)
                if (storage.ActiveAllocations == 0) storage.Owner?.Dispose();
            _chunks.Clear();
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
