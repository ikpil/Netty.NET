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
using Netty.NET.Common;

namespace Netty.NET.Buffer;

/// <summary>Allocates reference-counted buffers through heap and native memory pools.</summary>
/// <remarks>This implementation binds each physical thread to the least-used heap/native
/// arenas, with small/normal region caches and executor-based periodic trimming. Default
/// reads process settings and gates caching by physical thread/executor eligibility.
/// Explicit constructors retain their opt-in cache options. Buffer lifecycle events use
/// BufferAllocatorEventSource, including pooled chunk lifetime transitions.
/// Dispose closes factory allocations and pooled arenas; live pooled buffers and operation
/// leases keep their reservations until returned. Unpooled fallback buffers own independent
/// lifetimes. Empty sentinels/composites reserve no storage and remain available.
/// A positive cacheTrimPeriod schedules fixed-rate trim only when the binding is
/// first created on a mapped event executor. Zero disables scheduling. Releasing
/// the binding or allocator cancels its schedule; no caller ExecutionContext is retained.</remarks>
public partial class PooledByteBufAllocator : AbstractByteBufAllocator, IByteBufAllocatorMetricProvider, IDisposable
{
    public static PooledByteBufAllocator Default => DefaultInstance.Value;
    private readonly object _sync = new();
    private readonly NativeMemoryAllocator _nativeAllocator;
    private volatile bool _disposed;
    private readonly bool _useCacheForAllThreads;
    private readonly PoolArena[] _heapArenas;
    private readonly PoolArena[] _directArenas;
    internal IReadOnlyList<IPoolArenaMetric> HeapArenas { get; }
    internal IReadOnlyList<IPoolArenaMetric> DirectArenas { get; }
    internal int ChunkSize { get; }
    internal int SmallCacheSize { get; }
    internal int NormalCacheSize { get; }
    private readonly int _maxCachedBufferCapacity;
    private readonly int _cacheTrimInterval;
    private readonly TimeSpan _cacheTrimPeriod;
    public PooledByteBufAllocatorMetric Metric { get; }
    IByteBufAllocatorMetric IByteBufAllocatorMetricProvider.Metric => Metric;

    public PooledByteBufAllocator() : this(false)
    {
    }

#pragma warning disable CS0618 // @SuppressWarnings("deprecation")
    public PooledByteBufAllocator(bool preferDirect)
        : this(preferDirect, DefaultNumHeapArena(), DefaultNumDirectArena(), DefaultPageSize(), DefaultMaxOrder())
    {
    }

    public PooledByteBufAllocator(int nHeapArena, int nDirectArena, int pageSize, int maxOrder)
        : this(false, nHeapArena, nDirectArena, pageSize, maxOrder)
    {
    }
#pragma warning restore CS0618

    /**
     * @deprecated use
     * {@link PooledByteBufAllocator#PooledByteBufAllocator(boolean, int, int, int, int, int, int, boolean)}
     */
    [Obsolete("Use PooledByteBufAllocator(bool, int, int, int, int, int, int, bool).")]
    public PooledByteBufAllocator(bool preferDirect, int nHeapArena, int nDirectArena, int pageSize, int maxOrder)
        : this(preferDirect, nHeapArena, nDirectArena, pageSize, maxOrder,
             0, DefaultSmallCacheSize(), DefaultNormalCacheSize())
    {
    }

    /**
     * @deprecated use
     * {@link PooledByteBufAllocator#PooledByteBufAllocator(boolean, int, int, int, int, int, int, boolean)}
     */
    [Obsolete("Use PooledByteBufAllocator(bool, int, int, int, int, int, int, bool).")]
    public PooledByteBufAllocator(bool preferDirect, int nHeapArena, int nDirectArena, int pageSize, int maxOrder,
                                  int tinyCacheSize, int smallCacheSize, int normalCacheSize)
        : this(preferDirect, nHeapArena, nDirectArena, pageSize, maxOrder, smallCacheSize,
             normalCacheSize, DefaultUseCacheForAllThreads(), Configuration.Value.Alignment)
    {
    }

    /**
     * @deprecated use
     * {@link PooledByteBufAllocator#PooledByteBufAllocator(boolean, int, int, int, int, int, int, boolean)}
     */
    [Obsolete("Use PooledByteBufAllocator(bool, int, int, int, int, int, int, bool).")]
    public PooledByteBufAllocator(bool preferDirect, int nHeapArena,
                                  int nDirectArena, int pageSize, int maxOrder, int tinyCacheSize,
                                  int smallCacheSize, int normalCacheSize,
                                  bool useCacheForAllThreads)
        : this(preferDirect, nHeapArena, nDirectArena, pageSize, maxOrder,
             smallCacheSize, normalCacheSize,
             useCacheForAllThreads)
    {
    }

    public PooledByteBufAllocator(bool preferDirect, int nHeapArena,
                                  int nDirectArena, int pageSize, int maxOrder,
                                  int smallCacheSize, int normalCacheSize,
                                  bool useCacheForAllThreads)
        : this(preferDirect, nHeapArena, nDirectArena, pageSize, maxOrder,
             smallCacheSize, normalCacheSize,
             useCacheForAllThreads, Configuration.Value.Alignment)
    {
    }

    /**
     * @deprecated use
     * {@link PooledByteBufAllocator#PooledByteBufAllocator(boolean, int, int, int, int, int, int, boolean, int)}
     */
    [Obsolete("Use PooledByteBufAllocator(bool, int, int, int, int, int, int, bool, int).")]
    public PooledByteBufAllocator(bool preferDirect, int nHeapArena, int nDirectArena, int pageSize, int maxOrder,
                                  int tinyCacheSize, int smallCacheSize, int normalCacheSize,
                                  bool useCacheForAllThreads, int directMemoryCacheAlignment)
        : this(preferDirect, nHeapArena, nDirectArena, pageSize, maxOrder,
             smallCacheSize, normalCacheSize,
             useCacheForAllThreads, directMemoryCacheAlignment)
    {
    }

    public PooledByteBufAllocator(bool preferDirect, int nHeapArena, int nDirectArena, int pageSize, int maxOrder,
                                  int smallCacheSize, int normalCacheSize,
                                  bool useCacheForAllThreads, int directMemoryCacheAlignment)
        : this(preferDirect, nHeapArena, nDirectArena, pageSize, maxOrder, directMemoryCacheAlignment, null,
            smallCacheSize, normalCacheSize, null, null, null, useCacheForAllThreads)
    {
    }

    // CLR: the native memory source, pool selection and per-instance overrides of the process-wide
    // DEFAULT_MAX_CACHED_BUFFER_CAPACITY, DEFAULT_CACHE_TRIM_INTERVAL and DEFAULT_CACHE_TRIM_INTERVAL_MILLIS have no
    // source constructor. These two constructors keep the source's longest positional order (pageSize, maxOrder,
    // smallCacheSize, normalCacheSize, useCacheForAllThreads, directMemoryCacheAlignment) and append the CLR-only
    // settings, so a source-shaped prefix keeps its meaning; an omitted (null) argument takes the DEFAULT_* value
    // the source's shorter constructors pass. Calls matching a source signature exactly bind to the source overloads
    // above. poolHeap/poolDirect select DEFAULT_NUM_*_ARENA or zero arenas for a storage kind.
    public PooledByteBufAllocator(bool preferDirect = false, bool poolHeap = true, bool poolDirect = true,
        int? pageSize = null, int? maxOrder = null, int? smallCacheSize = null, int? normalCacheSize = null,
        bool? useCacheForAllThreads = null, int? directMemoryCacheAlignment = null,
        NativeMemoryAllocator nativeAllocator = null, int? maxCachedBufferCapacity = null,
        int? cacheTrimInterval = null, TimeSpan? cacheTrimPeriod = null)
        : this(preferDirect, poolHeap ? DefaultNumHeapArena() : 0, poolDirect ? DefaultNumDirectArena() : 0, pageSize,
            maxOrder, smallCacheSize, normalCacheSize, useCacheForAllThreads, directMemoryCacheAlignment,
            nativeAllocator, maxCachedBufferCapacity, cacheTrimInterval, cacheTrimPeriod)
    {
    }

    public PooledByteBufAllocator(bool preferDirect, int nHeapArena, int nDirectArena,
        int? pageSize = null, int? maxOrder = null, int? smallCacheSize = null, int? normalCacheSize = null,
        bool? useCacheForAllThreads = null, int? directMemoryCacheAlignment = null,
        NativeMemoryAllocator nativeAllocator = null, int? maxCachedBufferCapacity = null,
        int? cacheTrimInterval = null, TimeSpan? cacheTrimPeriod = null)
        : this(preferDirect, nHeapArena, nDirectArena, pageSize ?? DefaultPageSize(), maxOrder ?? DefaultMaxOrder(),
            directMemoryCacheAlignment ?? Configuration.Value.Alignment, nativeAllocator,
            smallCacheSize ?? DefaultSmallCacheSize(), normalCacheSize ?? DefaultNormalCacheSize(),
            maxCachedBufferCapacity, cacheTrimInterval, cacheTrimPeriod,
            useCacheForAllThreads ?? DefaultUseCacheForAllThreads())
    {
    }

    private PooledByteBufAllocator(bool preferDirect, int nHeapArena, int nDirectArena, int pageSize, int maxOrder,
        int directMemoryCacheAlignment, NativeMemoryAllocator nativeAllocator, int smallCacheSize,
        int normalCacheSize, int? maxCachedBufferCapacity, int? cacheTrimInterval, TimeSpan? cacheTrimPeriod,
        bool useCacheForAllThreads) : base(preferDirect)
    {
        _useCacheForAllThreads = useCacheForAllThreads;
        if (directMemoryCacheAlignment < 0 ||
            directMemoryCacheAlignment != 0 && !BitOperations.IsPow2(directMemoryCacheAlignment))
            throw new ArgumentOutOfRangeException(nameof(directMemoryCacheAlignment));
        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }
        if (directMemoryCacheAlignment > 0)
        {
            // Ensure page size is a whole multiple of the alignment, or bump it to the next whole multiple.
            long aligned = ((long)pageSize + directMemoryCacheAlignment - 1) & -(long)directMemoryCacheAlignment;
            if (aligned > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            }
            pageSize = (int)aligned;
        }
        if (pageSize < 4096 || !BitOperations.IsPow2(pageSize))
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (maxOrder < 0 || maxOrder > 14)
            throw new ArgumentOutOfRangeException(null,
                "maxOrder: " + maxOrder.ToString(System.Globalization.CultureInfo.InvariantCulture) + " (expected: 0-14)");
        // Ensure the resulting chunkSize does not overflow.
        long chunkSize = (long)pageSize << maxOrder;
        if (chunkSize > 1 << 30)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOrder));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(nHeapArena);
        ArgumentOutOfRangeException.ThrowIfNegative(nDirectArena);
        // The source passes cache sizes to PoolThreadCache unchecked: nonpositive sizes disable that cache, and the
        // DEFAULT_* capacity/trim values are only validated once an eligible thread actually creates caches.
        // CLR: explicit overrides of those process-wide values are validated here.
        if (maxCachedBufferCapacity is int capacity)
            ArgumentOutOfRangeException.ThrowIfNegative(capacity, nameof(maxCachedBufferCapacity));
        if (cacheTrimInterval is int interval)
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(interval, nameof(cacheTrimInterval));
        if (cacheTrimPeriod < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cacheTrimPeriod));
        }
        SmallCacheSize = smallCacheSize;
        NormalCacheSize = normalCacheSize;
        _maxCachedBufferCapacity = maxCachedBufferCapacity ?? Configuration.Value.MaxCachedCapacity;
        _cacheTrimInterval = cacheTrimInterval ?? Configuration.Value.TrimInterval;
        _cacheTrimPeriod = cacheTrimPeriod ?? Configuration.Value.TrimPeriod;
        ChunkSize = (int)chunkSize;
        // Logarithm base 2. At this point we know that pageSize is a power of two.
        int shifts = BitOperations.Log2((uint)pageSize);
        _nativeAllocator = nativeAllocator ?? NativeMemoryAllocator.Shared;
        _heapArenas = new PoolArena[nHeapArena];
        _directArenas = new PoolArena[nDirectArena];
        if (nHeapArena > 0)
        {
            var sizeClasses = new SizeClasses(pageSize, shifts, ChunkSize, 0);

            for (int i = 0; i < nHeapArena; ++i)
            {
                _heapArenas[i] = new PoolHeapArena(this, sizeClasses);
            }
        }
        if (nDirectArena > 0)
        {
            var sizeClasses = new SizeClasses(pageSize, shifts, ChunkSize, directMemoryCacheAlignment);
            for (int i = 0; i < nDirectArena; ++i)
                _directArenas[i] = new PoolDirectArena(this, sizeClasses, _nativeAllocator);
        }
        HeapArenas = Array.AsReadOnly<IPoolArenaMetric>(_heapArenas);
        DirectArenas = Array.AsReadOnly<IPoolArenaMetric>(_directArenas);
        Metric = new PooledByteBufAllocatorMetric(this);
        _threadCleanup = new ThreadCleanup(this);
    }

    public override bool IsDirectBufferPooled => _directArenas.Length != 0;

    // The source takes no allocator-wide lock: the thread's cache selects its arena, and PoolArena/PoolThreadCache
    // synchronize their own state.
    protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity)
    {
        using var events = BufferAllocatorEventSource.Log.BeginOperation();
        ThreadBinding cache = GetThreadBinding();
        PoolArena heapArena = cache?.HeapArena;

        ByteBuf buf;
        if (heapArena != null)
        {
            buf = AllocateFromArena(heapArena, initialCapacity, maxCapacity, cache.Cache);
        }
        else
        {
            buf = new UnpooledHeapByteBuf(this, initialCapacity, maxCapacity);
        }

        // CLR: detector callbacks run after allocation; the buffer owns its reservation.
        BufferAllocatorEventSource.Log.Allocated(buf);
        return ToLeakAwareBuffer(buf);
    }

    protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
    {
        using var events = BufferAllocatorEventSource.Log.BeginOperation();
        ThreadBinding cache = GetThreadBinding();
        PoolArena directArena = cache?.DirectArena;

        ByteBuf buf;
        if (directArena != null)
        {
            buf = AllocateFromArena(directArena, initialCapacity, maxCapacity, cache.Cache);
        }
        else
        {
            buf = new UnpooledDirectByteBuf(this, initialCapacity, maxCapacity, _nativeAllocator);
        }

        BufferAllocatorEventSource.Log.Allocated(buf);
        return ToLeakAwareBuffer(buf);
    }

    private ByteBuf AllocateFromArena(PoolArena arena, int initialCapacity, int maxCapacity, PoolThreadCache cache)
    {
        try
        {
            return arena.Allocate(initialCapacity, maxCapacity, cache);
        }
        catch (ObjectDisposedException) when (_disposed)
        {
            // CLR: Dispose raced with this allocation; report the allocator rather than its arena.
            ObjectDisposedException.ThrowIf(true, this);
            throw;
        }
    }

    /**
     * Returns the number of bytes of heap memory that is currently pinned to heap buffers allocated by a
     * {@link ByteBufAllocator}, or {@code -1} if unknown.
     * A buffer can pin more memory than its {@linkplain ByteBuf#capacity() capacity} might indicate,
     * due to implementation details of the allocator.
     */
    public long PinnedHeapMemory => SumMemory(_heapArenas, pinned: true);

    /**
     * Returns the number of bytes of direct memory that is currently pinned to direct buffers allocated by a
     * {@link ByteBufAllocator}, or {@code -1} if unknown.
     * A buffer can pin more memory than its {@linkplain ByteBuf#capacity() capacity} might indicate,
     * due to implementation details of the allocator.
     */
    public long PinnedDirectMemory => SumMemory(_directArenas, pinned: true);

    internal long UsedHeapMemory => SumMemory(_heapArenas, pinned: false);
    internal long UsedDirectMemory => SumMemory(_directArenas, pinned: false);

    private static long SumMemory(PoolArena[] arenas, bool pinned)
    {
        if (arenas.Length == 0)
        {
            return -1;
        }
        long total = 0;
        foreach (PoolArena arena in arenas)
        {
            total = unchecked(total + (pinned ? arena.NumPinnedBytes : arena.NumActiveBytes));
            if (total < 0)
            {
                return long.MaxValue;
            }
        }
        return total;
    }

    /**
     * Return {@code true} if direct memory cache alignment is supported, {@code false} otherwise.
     */
    public static bool IsDirectMemoryCacheAlignmentSupported => true;

    public void Dispose()
    {
        using var events = BufferAllocatorEventSource.Log.BeginOperation();
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            foreach (ThreadBinding binding in _threadBindings.Values)
            {
                binding.Detach();
            }

            _threadBindings.Clear();

            foreach (PoolArena arena in _heapArenas)
            {
                arena.Dispose();
            }
            foreach (PoolArena arena in _directArenas)
            {
                arena.Dispose();
            }
        }
    }
}
