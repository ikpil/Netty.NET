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
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

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
internal sealed partial class AdaptivePoolingAllocator : IDisposable
{
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
    internal int LocalBindingCount { get { lock (_localBindings) return _localBindings.Count; } }

    // CLR: lowMemory and the remaining settings are the per-instance replacements listed above.
    internal AdaptivePoolingAllocator(IChunkAllocator chunkAllocator, bool lowMemory,
        int chunkReuseQueue, long chunkPurgePollsShared, int chunkPurgeThreshold, int maxStripes,
        bool useCacheForNonEventLoopThreads = false, long chunkPurgePollsThreadLocal = 16,
        int threadLocalCacheMaxBytes = 8 * 1024 * 1024, int threadLocalCacheMinBytes = 4 * 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(chunkAllocator);
        _chunkAllocator = chunkAllocator;
        if (chunkReuseQueue < 2 || chunkReuseQueue > 1 << 28) throw new ArgumentOutOfRangeException(nameof(chunkReuseQueue));
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
        finally { _lifetime.ExitReadLock(); }
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
        catch { innerChunk.Release(); throw; }
        try { return chunk.TryAllocate(size) ?? throw new InvalidOperationException("A dedicated chunk was exhausted."); }
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
            if (_disposed) return;
            _disposed = true;
            lock (_localBindings)
            {
                foreach (var groups in _localBindings) foreach (var group in groups) group.Dispose();
                _localBindings.Clear();
            }
            // CLR: explicit lifetime closes every group, including all fixed classes.
            // Native delegates additionally have SafeHandle cleanup when unreachable.
            foreach (var group in _sizeClassedMagazineGroups) group.Dispose();
            _largeBufferMagazineGroup.Dispose();
        }
        finally { _lifetime.ExitWriteLock(); }
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
                lock (allocator._localBindings) allocator._localBindings.Add(groups);
                return groups;
            }
            return null;
        }

        protected override void OnRemoval(MagazineGroup[] groups)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            if (groups == null) return;
            // CLR: cleanup and allocator Dispose may run on different physical threads.
            // The write gate quiesces allocations/inspection before owner handoff.
            allocator._lifetime.EnterWriteLock();
            try
            {
                lock (allocator._localBindings) allocator._localBindings.Remove(groups);
                foreach (MagazineGroup group in groups) group.Dispose();
            }
            finally { allocator._lifetime.ExitWriteLock(); }
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
}
