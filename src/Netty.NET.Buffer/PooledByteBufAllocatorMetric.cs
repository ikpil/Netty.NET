/*
 * Copyright 2017 The Netty Project
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

namespace Netty.NET.Buffer;

/**
 * Exposed metric for {@link PooledByteBufAllocator}.
 */
/// <remarks>Cache sizes reflect configured entries per size class. NumThreadLocalCaches counts arena bindings,
/// including bindings with no cached regions, as in Java. Memory values
/// measure arena storage, excluding retained retired heap storage, alignment headroom
/// and raw native pins; NativeMemoryAllocator.ReservedBytes measures physical reservations.
/// Separate property reads are not a transactional snapshot.</remarks>
public sealed class PooledByteBufAllocatorMetric : IByteBufAllocatorMetric
{
    private readonly PooledByteBufAllocator _allocator;
    internal PooledByteBufAllocatorMetric(PooledByteBufAllocator allocator) => _allocator = allocator;

    /**
     * Return the number of heap arenas.
     */
    public int NumHeapArenas => HeapArenas.Count;

    /**
     * Return the number of direct arenas.
     */
    public int NumDirectArenas => DirectArenas.Count;

    /**
     * Return a {@link List} of all heap {@link PoolArenaMetric}s that are provided by this pool.
     */
    public IReadOnlyList<IPoolArenaMetric> HeapArenas => _allocator.HeapArenas;

    /**
     * Return a {@link List} of all direct {@link PoolArenaMetric}s that are provided by this pool.
     */
    public IReadOnlyList<IPoolArenaMetric> DirectArenas => _allocator.DirectArenas;

    /**
     * Return the number of thread local caches used by this {@link PooledByteBufAllocator}.
     */
    public int NumThreadLocalCaches => _allocator.NumThreadBindings;

    /**
     * Return the size of the tiny cache.
     *
     * @deprecated Tiny caches have been merged into small caches.
     */
    [Obsolete("Tiny caches have been merged into small caches.")]
    public int TinyCacheSize => 0;

    /**
     * Return the size of the small cache.
     */
    public int SmallCacheSize => _allocator.SmallCacheSize;

    /**
     * Return the size of the normal cache.
     */
    public int NormalCacheSize => _allocator.NormalCacheSize;

    /**
     * Return the chunk size for an arena.
     */
    public int ChunkSize => _allocator.ChunkSize;

    public long UsedHeapMemory => _allocator.UsedHeapMemory;
    public long UsedDirectMemory => _allocator.UsedDirectMemory;
    public override string ToString()
        => $"{nameof(PooledByteBufAllocatorMetric)}(usedHeapMemory: {UsedHeapMemory}; usedDirectMemory: {UsedDirectMemory}; " +
            $"numHeapArenas: {NumHeapArenas}; numDirectArenas: {NumDirectArenas}; smallCacheSize: {SmallCacheSize}; " +
            $"normalCacheSize: {NormalCacheSize}; numThreadLocalCaches: {NumThreadLocalCaches}; chunkSize: {ChunkSize})";
}
