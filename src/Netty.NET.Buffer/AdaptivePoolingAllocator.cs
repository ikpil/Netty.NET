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
// Physical-thread local magazines and allocator events are separate pending paths.
internal sealed class AdaptivePoolingAllocator : IDisposable
{
    private readonly ReaderWriterLockSlim _lifetime = new();
    /**
     * The strategy for how {@link AdaptivePoolingAllocator} should allocate chunk buffers.
     */
    private readonly Func<int, int, ByteBuf> _chunkAllocator;
    private readonly Func<int, int, AdaptiveChunkAllocation> _reserve;
    private readonly AdaptiveMagazineGroup[] _small;
    private readonly AdaptiveMagazineGroup _large;
    private bool _disposed;
    internal AdaptiveAllocationPolicy Policy { get; }
    internal AdaptiveChunkRegistry Registry { get; } = new();
    internal long UsedMemory => Registry.TotalCapacity;
    internal AdaptiveMagazineGroup Group(int index) => index < _small.Length ? _small[index] : _large;

    internal AdaptivePoolingAllocator(Func<int, int, ByteBuf> chunkAllocator, bool lowMemory,
        int reuseCapacity, long purgePolls, int purgeThreshold, int maxStripes)
    {
        ArgumentNullException.ThrowIfNull(chunkAllocator);
        if (reuseCapacity < 2 || reuseCapacity > 1 << 28) throw new ArgumentOutOfRangeException(nameof(reuseCapacity));
        ArgumentOutOfRangeException.ThrowIfLessThan(purgePolls, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(purgeThreshold, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxStripes, 1);
        _chunkAllocator = chunkAllocator;
        _reserve = Reserve;
        Policy = new AdaptiveAllocationPolicy(lowMemory);
        int[] classes = AdaptiveAllocationPolicy.GetSizeClasses();
        _small = new AdaptiveMagazineGroup[classes.Length];
        for (int i = 0; i < classes.Length; i++)
            _small[i] = new AdaptiveMagazineGroup(this, classes[i], reuseCapacity, purgePolls, purgeThreshold, lowMemory ? 1 : maxStripes);
        _large = new AdaptiveMagazineGroup(this, 0, reuseCapacity, purgePolls, purgeThreshold, lowMemory ? 1 : maxStripes);
    }

    internal ByteBuf Allocate(int size, int maxCapacity) => new AdaptiveByteBuf(size, maxCapacity, _reserve);
    /**
     * Allocate into the given buffer. Used by {@link AdaptiveByteBuf#capacity(int)}.
     */
    // CLR: return a replacement reservation; AdaptiveByteBuf publishes it after copying.
    private AdaptiveChunkAllocation Reserve(int size, int maxCapacity)
    {
        _lifetime.EnterReadLock();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            AdaptiveChunkAllocation allocation = null;
            if (Policy.IsPooled(size)) allocation = Group(AdaptiveAllocationPolicy.SizeClassIndexOf(size)).TryAllocate(size, maxCapacity);
            return allocation ?? AllocateFallback(size, maxCapacity);
        }
        finally { _lifetime.ExitReadLock(); }
    }

    private AdaptiveChunkAllocation AllocateFallback(int size, int maxCapacity)
    {
        // If we don't already have a buffer, obtain one from the most conveniently available magazine.
        // CLR: the outer wrapper is fresh and already bound to this pool; no recycler magazine is needed.
        // Create a one-off chunk for this allocation.
        AdaptiveChunk chunk = NewChunk(size, maxCapacity, 0, false);
        try { return chunk.TryAllocate(size) ?? throw new InvalidOperationException("A dedicated chunk was exhausted."); }
        finally
        {
            // As the chunk is an one-off we need to always call release explicitly as readInitInto(...)
            // will take care of retain once when successful. Once The AdaptiveByteBuf is released it will
            // completely release the Chunk and so the contained innerChunk.
            chunk.Dispose();
        }
    }

    /**
     * Allocate a buffer for a chunk. This can be any kind of {@link AbstractByteBuf} implementation.
     *
     * @param initialCapacity The initial capacity of the returned {@link AbstractByteBuf}.
     * @param maxCapacity     The maximum capacity of the returned {@link AbstractByteBuf}.
     * @return The buffer that represents the chunk memory.
     */
    internal AdaptiveChunk NewChunk(int size, int maximum, int segmentSize, bool pooled)
    {
        ByteBuf raw = _chunkAllocator(size, maximum);
        try
        {
            return !pooled ? new AdaptiveChunk(raw, Registry)
                : segmentSize != 0 ? new AdaptiveSizeClassedChunk(raw, Registry, segmentSize)
                : new AdaptiveBuddyChunk(raw, Registry);
        }
        catch { raw.Release(); throw; }
    }

    public void Dispose()
    {
        // Ensure that we release all previous pooled resources when this object is finalized. This is needed as otherwise
        // we might end up with leaks. While these leaks are usually harmless in reality it would still at least be
        // very confusing for users.
        // CLR: IDisposable drains all groups; SafeHandle-backed delegates cover unreachable native storage.
        _lifetime.EnterWriteLock();
        try
        {
            if (_disposed) return;
            _disposed = true;
            // CLR: explicit lifetime closes every group, including all fixed classes.
            // Native delegates additionally have SafeHandle cleanup when unreachable.
            foreach (var group in _small) group.Dispose();
            _large.Dispose();
        }
        finally { _lifetime.ExitWriteLock(); }
    }
}

internal sealed class AdaptiveMagazineGroup : IDisposable
{
    private readonly object _expandLock = new();
    private readonly AdaptivePoolingAllocator _pool;
    private readonly int _segmentSize, _maxStripes;
    private readonly IAdaptiveChunkCache _cache;
    private AdaptiveMagazine[] _magazines = [];
    private bool _freed;
    internal AdaptiveMagazine[] Magazines => Volatile.Read(ref _magazines);
    internal IAdaptiveChunkCache Cache => _cache;
    internal bool IsFreed => Volatile.Read(ref _freed);
    internal int FastCapacity(int size, int max) => _pool.Policy.ComputeBufferCapacity(size, max);

    internal AdaptiveMagazineGroup(AdaptivePoolingAllocator pool, int segmentSize, int reuseCapacity,
        long purgePolls, int purgeThreshold, int maxStripes)
    {
        _pool = pool; _segmentSize = segmentSize; _maxStripes = maxStripes;
        _cache = segmentSize != 0 ? new AdaptiveSharedChunkCache(reuseCapacity, purgePolls, purgeThreshold)
            : new AdaptiveBuddyChunkCache(reuseCapacity);
        _magazines = [new AdaptiveMagazine(this)];
    }

    internal AdaptiveChunkAllocation TryAllocate(int size, int maxCapacity)
    {
        // Path for concurrent allocation.
        int threadId = Environment.CurrentManagedThreadId;
        AdaptiveMagazine[] magazines; int expansions = 0;
        do
        {
            magazines = Magazines;
            int mask = magazines.Length - 1, index = threadId & mask;
            for (int i = 0, attempts = magazines.Length << 1; i < attempts; i++)
            {
                AdaptiveChunkAllocation allocation = magazines[(index + i) & mask].TryAllocate(size, maxCapacity);
                if (allocation != null)
                {
                    // Was able to allocate.
                    return allocation;
                }
            }
            expansions++;
        } while (expansions <= 3 && TryExpandMagazines(magazines.Length));
        // The magazines failed us; contention too high and we don't want to spend more effort expanding the array.
        // CLR: no unused outer recycler object was claimed on this reservation path.
        return null;
    }

    private bool TryExpandMagazines(int currentLength)
    {
        if (currentLength >= _maxStripes) return true;
        if (!Monitor.TryEnter(_expandLock)) return true;
        AdaptiveMagazine[] previous = null;
        try
        {
            var magazines = Magazines;
            if (magazines.Length >= _maxStripes || magazines.Length > currentLength || IsFreed) return true;
            var expanded = new AdaptiveMagazine[magazines.Length * 2];
            for (int i = 0; i < expanded.Length; i++) expanded[i] = new AdaptiveMagazine(this);
            previous = magazines;
            Volatile.Write(ref _magazines, expanded);
        }
        finally { Monitor.Exit(_expandLock); }
        foreach (var magazine in previous) magazine.Dispose();
        return true;
    }

    /**
     * Allocate a new {@link Chunk} for the given {@link Magazine}.
     */
    internal AdaptiveChunk NewChunk(int size, AdaptiveMagazine magazine)
    {
        int capacity = _pool.Policy.ComputeChunkSize(size);
        AdaptiveChunk chunk = _pool.NewChunk(capacity, capacity, _segmentSize, true);
        chunk.AttachToMagazine(magazine);
        return chunk;
    }
    internal AdaptiveChunk PollChunk(int size) => _cache.PollChunk(size);
    internal bool OfferChunk(AdaptiveChunk chunk)
    {
        if (IsFreed) return false;
        if (chunk.HasUnprocessedFreelistEntries) chunk.ProcessFreelistEntries();
        bool added = _cache.OfferChunk(chunk);
        if (IsFreed && added)
        {
            // Help to free the reuse queue.
            _cache.Dispose();
        }
        return added;
    }
    public void Dispose()
    {
        Volatile.Write(ref _freed, true);
        lock (_expandLock) foreach (var magazine in Magazines) magazine.Dispose();
        _cache.Dispose();
    }
}
