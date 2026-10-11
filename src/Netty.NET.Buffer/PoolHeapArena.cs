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
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer;

// CLR: internal heap storage foundation. Public allocator selection, utilization
// lists, chunk eviction, thread caches and allocator metrics remain separate work.
// Until eviction is connected, chunks remain available for reuse for this arena's lifetime.
internal sealed class PoolHeapArena
{
    private readonly object _sync = new();
    private readonly Dictionary<PoolChunk, byte[]> _chunks = new();
    private readonly PoolSubpage[] _smallSubpagePools;
    internal IByteBufAllocator Parent { get; }
    internal SizeClasses SizeClass { get; }

    internal PoolHeapArena(IByteBufAllocator parent, SizeClasses sizeClass)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(sizeClass);
        if (sizeClass.DirectMemoryCacheAlignment != 0)
            throw new ArgumentException("Heap arenas do not align native addresses.", nameof(sizeClass));
        // Validate chunk geometry before any backing storage is reserved.
        _ = new PoolChunk(sizeClass);
        Parent = parent;
        SizeClass = sizeClass;
        _smallSubpagePools = PoolSubpage.CreatePoolHeads(sizeClass);
    }

    internal PooledHeapByteBuf Allocate(int initialCapacity, int maxCapacity)
        => new(this, initialCapacity, maxCapacity);

    internal Allocation AllocateMemory(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        int sizeIdx = SizeClass.Size2SizeIdx(capacity);
        if (sizeIdx == SizeClass.NSizes)
        {
            // Huge allocations are never served via the cache so just call allocateHuge
            return AllocateHuge(capacity);
        }

        int normalized = SizeClass.SizeIdx2Size(sizeIdx);
        lock (_sync)
        {
            // CLR: use existing chunks directly while utilization lists are pending.
            foreach (var entry in _chunks)
            {
                long handle = AllocateHandle(entry.Key, sizeIdx, normalized, out PoolChunk owner);
                if (handle >= 0)
                    return Bind(owner, _chunks[owner], handle, normalized);
            }

            // Add a new chunk.
            var chunk = new PoolChunk(SizeClass, _smallSubpagePools);
            if (sizeIdx <= SizeClass.SmallMaxSizeIdx && chunk.CalculateRunSize(sizeIdx) < 0)
            {
                // CLR: tiny chunk geometries can be shorter than the small class's
                // LCM run. Use an exact dedicated reservation, never a truncated slot.
                return AllocateHuge(capacity);
            }
            byte[] memory = GC.AllocateUninitializedArray<byte>(SizeClass.ChunkSize);
            // Publish storage before a subpage can join a shared head. Allocation
            // failures then leave either no chunk or a reusable registered chunk.
            _chunks.Add(chunk, memory);
            long newHandle = AllocateHandle(chunk, sizeIdx, normalized, out PoolChunk newOwner);
            if (newHandle < 0)
                throw new InvalidOperationException("A new chunk could not satisfy its size class.");
            return Bind(newOwner, _chunks[newOwner], newHandle, normalized);
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

    private Allocation Bind(PoolChunk chunk, byte[] memory, long handle, int normalized)
    {
        try
        {
            int offset = PoolChunk.RunOffset(handle) << SizeClass.PageShifts;
            if (PoolChunk.IsSubpage(handle))
                offset += PoolChunk.BitmapIdx(handle) * normalized;
            return new Allocation(this, chunk, memory, handle, offset, normalized);
        }
        catch
        {
            FreeHandle(chunk, handle);
            throw;
        }
    }

    private Allocation AllocateHuge(int capacity)
        /* unpooled buffers are never allocated out of the thread-local cache */
        => new(this, null, GC.AllocateUninitializedArray<byte>(capacity), -1, 0, capacity);

    private static void FreeHandle(PoolChunk chunk, long handle)
    {
        if (PoolChunk.IsSubpage(handle)) chunk.FreeSmall(handle);
        else chunk.FreeRun(handle);
    }

    private void Free(Allocation allocation)
    {
        if (allocation.Chunk == null) return; // Dedicated arrays are reclaimed by the GC.
        lock (_sync) FreeHandle(allocation.Chunk, allocation.Handle);
    }

    // A buffer owns one reference to its reservation. Internal span operations
    // acquire another, so replacing/releasing a buffer cannot recycle its source
    // region before that operation finishes. Raw public Memory/Span views remain
    // borrowed and must not be accessed after replacement or final buffer release.
    internal sealed class Allocation : IDisposable
    {
        private readonly PoolHeapArena _arena;
        private int _references = 1;
        internal readonly PoolChunk Chunk;
        internal readonly byte[] Memory;
        internal readonly long Handle;
        internal readonly int Offset;
        internal readonly int ReservedLength;
        internal bool IsPooled => Chunk != null;

        internal Allocation(PoolHeapArena arena, PoolChunk chunk, byte[] memory,
            long handle, int offset, int reservedLength)
        {
            _arena = arena;
            Chunk = chunk;
            Memory = memory;
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
