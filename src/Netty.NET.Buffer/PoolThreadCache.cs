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
using System.Numerics;

namespace Netty.NET.Buffer;

/**
 * Acts a Thread cache for allocations. This implementation is moduled after
 * <a href="https://people.freebsd.org/~jasone/jemalloc/bsdcan2006/jemalloc.pdf">jemalloc</a> and the descripted
 * technics of
 * <a href="https://www.facebook.com/notes/facebook-engineering/scalable-memory-allocation-using-jemalloc/480222803919">
 * Scalable memory allocation using jemalloc</a>.
 */
// CLR: a Monitor protects bounded FIFO queues, cross-thread returns and explicit
// close. Lock order is allocator -> cache -> arena; arena returns drop their lock
// before entering the cache. Value entries replace Recycler/NIO buffer objects.
internal sealed class PoolThreadCache : IDisposable
{
    private readonly object _sync = new();
    // Hold the caches for the different size classes, which are small and normal.
    private readonly RegionCache[] _smallHeap, _smallDirect, _normalHeap, _normalDirect;
    private readonly int _freeSweepAllocationThreshold;
    private int _allocations;
    private bool _freed;
    // TODO: Test if adding padding helps under contention
    //private long pad0, pad1, pad2, pad3, pad4, pad5, pad6, pad7;

    internal PoolThreadCache(PoolArena heapArena, PoolArena directArena, int smallCacheSize,
        int normalCacheSize, int maxCachedBufferCapacity, int freeSweepAllocationThreshold)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxCachedBufferCapacity);
        _freeSweepAllocationThreshold = freeSweepAllocationThreshold;
        if (directArena != null)
        {
            _smallDirect = CreateSubPageCaches(smallCacheSize, directArena.SizeClass.NSubpages);
            _normalDirect = CreateNormalCaches(normalCacheSize, maxCachedBufferCapacity, directArena);
        }
        else
        {
            // No directArea is configured so just null out all caches
            _smallDirect = null;
            _normalDirect = null;
        }
        if (heapArena != null)
        {
            // Create the caches for the heap allocations
            _smallHeap = CreateSubPageCaches(smallCacheSize, heapArena.SizeClass.NSubpages);
            _normalHeap = CreateNormalCaches(normalCacheSize, maxCachedBufferCapacity, heapArena);
        }
        else
        {
            // No heapArea is configured so just null out all caches
            _smallHeap = null;
            _normalHeap = null;
        }
        // Only check if there are caches in use.
        if ((_smallDirect != null || _normalDirect != null || _smallHeap != null || _normalHeap != null)
            && freeSweepAllocationThreshold < 1)
            throw new ArgumentOutOfRangeException(nameof(freeSweepAllocationThreshold));
    }

    private static RegionCache[] CreateSubPageCaches(int size, int count)
    {
        if (size <= 0 || count <= 0) return null;
        var caches = new RegionCache[count];
        for (int i = 0; i < count; ++i)
            // TODO: maybe use cacheSize / cache.length
            caches[i] = new RegionCache(size);
        return caches;
    }

    private static RegionCache[] CreateNormalCaches(int size, int maxCapacity, PoolArena arena)
    {
        if (size <= 0 || maxCapacity <= 0) return null;
        int max = Math.Min(arena.SizeClass.ChunkSize, maxCapacity);
        // Create as many normal caches as we support based on how many sizeIdx we have and what the upper
        // bound is that we want to cache in general.
        var caches = new List<RegionCache>();
        for (int i = arena.SizeClass.NSubpages; i < arena.SizeClass.NSizes && arena.SizeClass.SizeIdx2Size(i) <= max; ++i)
            caches.Add(new RegionCache(size));
        return caches.ToArray();
    }

    // Selection deliberately uses storage kind and size class, not arena identity:
    // reallocation may place another arena's handle in the calling thread's cache.
    private RegionCache Cache(PoolArena arena, int sizeIdx)
    {
        RegionCache[] caches;
        if (sizeIdx < arena.SizeClass.NSubpages)
            caches = arena.IsDirect ? _smallDirect : _smallHeap;
        else
        {
            // We need to subtract area.sizeClass.nSubpages as sizeIdx is the overall index for all sizes.
            sizeIdx -= arena.SizeClass.NSubpages;
            caches = arena.IsDirect ? _normalDirect : _normalHeap;
        }
        return caches != null && (uint)sizeIdx < (uint)caches.Length ? caches[sizeIdx] : null;
    }

    /**
     * Try to allocate a small buffer out of the cache. Returns {@code true} if successful {@code false} otherwise
     */
    /**
     * Try to allocate a normal buffer out of the cache. Returns {@code true} if successful {@code false} otherwise
     */
    internal PoolArena.Allocation TryAllocate(PoolArena arena, int sizeIdx)
    {
        lock (_sync)
        {
            RegionCache cache = Cache(arena, sizeIdx);
            // no cache found so just return false here
            if (_freed || cache == null) return null;
            PoolArena.Allocation allocation = cache.Allocate(this);
            if (++_allocations >= _freeSweepAllocationThreshold)
            {
                _allocations = 0;
                TrimCore();
            }
            return allocation;
        }
    }

    /**
     * Add {@link PoolChunk} and {@code handle} to the cache if there is enough room.
     * Returns {@code true} if it fit into the cache {@code false} otherwise.
     */
    internal bool Add(PoolArena.Allocation allocation)
    {
        lock (_sync)
        {
            if (_freed) return false;
            return Cache(allocation.Arena, allocation.Arena.SizeClass.Size2SizeIdx(allocation.ReservedLength))
                ?.Add(new Entry(allocation.Arena, allocation.Storage, allocation.Handle, allocation.ReservedLength)) == true;
        }
    }

    internal void Trim()
    {
        lock (_sync) { if (!_freed) TrimCore(); }
    }

    private void TrimCore()
    {
        Trim(_smallDirect); Trim(_normalDirect); Trim(_smallHeap); Trim(_normalHeap);
    }

    private static void Trim(RegionCache[] caches)
    {
        if (caches == null) return;
        foreach (RegionCache cache in caches) cache.Trim();
    }

    /**
     *  Should be called if the Thread that uses this cache is about to exit to release resources out of the cache
     */
    public void Dispose()
    {
        lock (_sync)
        {
            // As free() may be called either by the finalizer or by FastThreadLocal.onRemoval(...) we need to ensure
            // we only call this one time.
            // CLR: explicit allocator/thread-binding cleanup replaces JVM finalizers.
            if (_freed) return;
            _freed = true;
            Free(_smallDirect); Free(_normalDirect); Free(_smallHeap); Free(_normalHeap);
        }
    }

    private static void Free(RegionCache[] caches)
    {
        if (caches == null) return;
        foreach (RegionCache cache in caches) cache.Free(int.MaxValue);
    }

    private readonly record struct Entry(PoolArena Arena, PoolArena.ChunkStorage Storage, long Handle, int Length);

    /**
     * Cache used for buffers which are backed by TINY or SMALL size.
     */
    /**
     * Cache used for buffers which are backed by NORMAL size.
     */
    // CLR: one implementation shares both handle kinds; the arena decodes the handle.
    private sealed class RegionCache
    {
        private readonly int _size;
        private readonly Queue<Entry> _queue = new();
        private int _allocations;

        internal RegionCache(int size)
            => _size = size >= 1 << 30 ? 1 << 30 : (int)BitOperations.RoundUpToPowerOf2((uint)size);

        /**
         * Add to cache if not already full.
         */
        internal bool Add(Entry entry)
        {
            if (_queue.Count >= _size) return false;
            try { _queue.Enqueue(entry); return true; }
            // CLR: lazy BCL queue growth must not lose a reservation on return.
            catch (OutOfMemoryException) { return false; }
        }

        /**
         * Allocate something out of the cache if possible and remove the entry from the cache.
         */
        /**
         * Init the {@link PooledByteBuf} using the provided chunk and handle with the capacity restrictions.
         */
        internal PoolArena.Allocation Allocate(PoolThreadCache threadCache)
        {
            if (!_queue.TryPeek(out Entry entry)) return null;
            // Init the buffer before removing the entry: a failed CLR wrapper allocation
            // leaves the cached handle reachable for the next request or shutdown.
            PoolArena.Allocation allocation = entry.Arena.BindCached(entry.Storage, entry.Handle, entry.Length, threadCache);
            _queue.Dequeue();
            // allocations is not thread-safe which is fine as this is only called from the same thread all time.
            // CLR: the enclosing Monitor also serializes trimming and explicit close.
            unchecked { ++_allocations; }
            return allocation;
        }

        /**
         * Clear out this cache and free up all previous cached {@link PoolChunk}s and {@code handle}s.
         */
        internal void Free(int max)
        {
            for (int i = 0; i < max; ++i)
            {
                if (!_queue.TryDequeue(out Entry entry))
                    // all cleared
                    return;
                // Capture entry state before we recycle the entry object.
                // CLR: Queue clears removed value entries, allowing storage to be collected.
                entry.Arena.ReturnCached(entry.Storage, entry.Handle);
            }
        }

        /**
         * Free up cached {@link PoolChunk}s if not allocated frequently enough.
         */
        internal void Trim()
        {
            int free = unchecked(_size - _allocations);
            _allocations = 0;
            // We not even allocated all the number that are
            if (free > 0) Free(free);
        }
    }
}
