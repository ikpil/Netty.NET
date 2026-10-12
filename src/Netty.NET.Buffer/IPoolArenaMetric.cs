/*
 * Copyright 2015 The Netty Project
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
 * Expose metrics for an arena.
 */
/// <remarks>CLR uses one arena per enabled storage kind without thread bindings or caches,
/// so NumThreadCaches is zero. Java creates a thread binding even with zero cache sizes.
/// Properties are individually synchronized; separate reads are not one snapshot.
/// Explicit shutdown keeps active reservations in the byte/chunk counts until returned.</remarks>
public interface IPoolArenaMetric : ISizeClassesMetric
{
    /**
     * Returns the number of thread caches backed by this arena.
     */
    int NumThreadCaches { get; }

    /**
     * Returns the number of tiny sub-pages for the arena.
     *
     * @deprecated Tiny sub-pages have been merged into small sub-pages.
     */
    [Obsolete("Tiny allocations have been merged into small allocations.")]
    int NumTinySubpages { get; }

    /**
     * Returns the number of small sub-pages for the arena.
     */
    int NumSmallSubpages { get; }

    /**
     * Returns the number of chunk lists for the arena.
     */
    int NumChunkLists { get; }

    /**
     * Returns an unmodifiable {@link List} which holds {@link PoolSubpageMetric}s for tiny sub-pages.
     *
     * @deprecated Tiny sub-pages have been merged into small sub-pages.
     */
    [Obsolete("Tiny allocations have been merged into small allocations.")]
    IReadOnlyList<IPoolSubpageMetric> TinySubpages { get; }

    /**
     * Returns an unmodifiable {@link List} which holds {@link PoolSubpageMetric}s for small sub-pages.
     */
    IReadOnlyList<IPoolSubpageMetric> SmallSubpages { get; }

    /**
     * Returns an unmodifiable {@link List} which holds {@link PoolChunkListMetric}s.
     */
    IReadOnlyList<IPoolChunkListMetric> ChunkLists { get; }

    /**
     * Return the number of allocations done via the arena. This includes all sizes.
     */
    long NumAllocations { get; }

    /**
     * Return the number of tiny allocations done via the arena.
     *
     * @deprecated Tiny allocations have been merged into small allocations.
     */
    [Obsolete("Tiny allocations have been merged into small allocations.")]
    long NumTinyAllocations { get; }

    /**
     * Return the number of small allocations done via the arena.
     */
    long NumSmallAllocations { get; }

    /**
     * Return the number of normal allocations done via the arena.
     */
    long NumNormalAllocations { get; }

    /**
     * Return the number of huge allocations done via the arena.
     */
    long NumHugeAllocations { get; }

    /**
     * Return the number of chunks allocations done via the arena, or -1 if not defined.
     */
    long NumChunkAllocations { get; }

    /**
     * Return the number of deallocations done via the arena. This includes all sizes.
     */
    long NumDeallocations { get; }

    /**
     * Return the number of tiny deallocations done via the arena.
     *
     * @deprecated Tiny deallocations have been merged into small deallocations.
     */
    [Obsolete("Tiny allocations have been merged into small allocations.")]
    long NumTinyDeallocations { get; }

    /**
     * Return the number of small deallocations done via the arena.
     */
    long NumSmallDeallocations { get; }

    /**
     * Return the number of normal deallocations done via the arena.
     */
    long NumNormalDeallocations { get; }

    /**
     * Return the number of huge deallocations done via the arena.
     */
    long NumHugeDeallocations { get; }

    /**
     * Return the number of chunk deallocations done via the arena, or -1 if nor defined.
     */
    long NumChunkDeallocations { get; }

    /**
     * Return the number of currently active allocations.
     */
    long NumActiveAllocations { get; }

    /**
     * Return the number of currently active tiny allocations.
     *
     * @deprecated Tiny allocations have been merged into small allocations.
     */
    [Obsolete("Tiny allocations have been merged into small allocations.")]
    long NumActiveTinyAllocations { get; }

    /**
     * Return the number of currently active small allocations.
     */
    long NumActiveSmallAllocations { get; }

    /**
     * Return the number of currently active normal allocations.
     */
    long NumActiveNormalAllocations { get; }

    /**
     * Return the number of currently active huge allocations.
     */
    long NumActiveHugeAllocations { get; }

    /**
     * Return the number of currently active chunks, or -1 if not defined
     */
    long NumActiveChunks { get; }

    /**
     * Return the number of active bytes that are currently allocated by the arena.
     */
    long NumActiveBytes { get; }
}
