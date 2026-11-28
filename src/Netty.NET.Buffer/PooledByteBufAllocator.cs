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
using System.Globalization;
using System.Text;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Common.Concurrent;

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
public class PooledByteBufAllocator : AbstractByteBufAllocator, IByteBufAllocatorMetricProvider, IDisposable
{
    private readonly ThreadCleanup _threadCleanup;

    // CLR: the process settings are snapshotted once, like the source's static DEFAULT_* fields; the shared Default
    // and the omitted constructor arguments use them. Snapshotting alone allocates no arenas.
    private static class Configuration
    {
        internal static readonly RuntimeDefaults Value = new();
        static Configuration()
        {
        }
    }
    private static class DefaultInstance
    {
        internal static readonly PooledByteBufAllocator Value = new(ByteBufUtil.PreferDirect);
        static DefaultInstance()
        {
        }
    }

    /**
     * Default number of heap arenas - System Property: io.netty.allocator.numHeapArenas - default 2 * cores
     */
    public static int DefaultNumHeapArena()
    {
        return Configuration.Value.HeapArenaCount;
    }

    /**
     * Default number of direct arenas - System Property: io.netty.allocator.numDirectArenas - default 2 * cores
     */
    public static int DefaultNumDirectArena()
    {
        return Configuration.Value.DirectArenaCount;
    }

    /**
     * Default buffer page size - System Property: io.netty.allocator.pageSize - default 8192
     */
    public static int DefaultPageSize()
    {
        return Configuration.Value.PageSize;
    }

    /**
     * Default maximum order - System Property: io.netty.allocator.maxOrder - default 9
     */
    public static int DefaultMaxOrder()
    {
        return Configuration.Value.MaxOrder;
    }

    /**
     * Default thread caching behavior - System Property: io.netty.allocator.useCacheForAllThreads - default false
     */
    public static bool DefaultUseCacheForAllThreads()
    {
        return Configuration.Value.UseCacheForAllThreads;
    }

    /**
     * Default small cache size - System Property: io.netty.allocator.smallCacheSize - default 256
     */
    public static int DefaultSmallCacheSize()
    {
        return Configuration.Value.SmallCacheSize;
    }

    /**
     * Default normal cache size - System Property: io.netty.allocator.normalCacheSize - default 64
     */
    public static int DefaultNormalCacheSize()
    {
        return Configuration.Value.NormalCacheSize;
    }

    /**
     * Default prefer direct - System Property: io.netty.noPreferDirect - default false
     */
    public static bool DefaultPreferDirect()
    {
        return ByteBufUtil.PreferDirect;
    }

    /**
     * Default tiny cache size - default 0
     *
     * @deprecated Tiny caches have been merged into small caches.
     */
    [Obsolete("Tiny caches have been merged into small caches.")]
    public static int DefaultTinyCacheSize()
    {
        return 0;
    }

    private sealed class RuntimeDefaults
    {
        internal readonly int PageSize, MaxOrder, Alignment, HeapArenaCount, DirectArenaCount;
        internal readonly int SmallCacheSize, NormalCacheSize, MaxCachedCapacity, TrimInterval;
        internal readonly bool UseCacheForAllThreads;
        internal readonly TimeSpan TrimPeriod;

        internal RuntimeDefaults()
        {
            IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(PooledByteBufAllocator));
            int page = SystemPropertyUtil.GetInt("io.netty.allocator.pageSize", 8192);
            int alignment = SystemPropertyUtil.GetInt("io.netty.allocator.directMemoryCacheAlignment", 0);
            if (page < 4096 || !BitOperations.IsPow2(page) || alignment > page)
            {
                logger.Debug("Invalid pooled page size/alignment; using pageSize 8192 and alignment 0");
                page = 8192;
                alignment = 0;
            }

            // Java validates alignment shape when constructing the allocator, after
            // its page fallback. Preserve that timing instead of silently correcting it.
            PageSize = page;
            Alignment = alignment;
            // 8192 << 9 = 4 MiB per chunk
            int order = SystemPropertyUtil.GetInt("io.netty.allocator.maxOrder", 9);
            if (order < 0 || order > 14 || ((long)page << order) > 1 << 30)
            {
                logger.Debug("Invalid pooled maxOrder; using 9");
                order = 9;
            }

            MaxOrder = order;

            // Determine reasonable default for nHeapArena and nDirectArena.
            // Assuming each arena has 3 chunks, the pool should not consume more than 50% of max memory.
            // CLR: GCMemoryInfo.TotalAvailableMemoryBytes (GCHeapHardLimit, a container limit or physical memory) is
            // the counterpart of Runtime.maxMemory().
            long maxMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

            /*
             * We use 2 * available processors by default to reduce contention as we use 2 * available processors for the
             * number of EventLoops in NIO and EPOLL as well. If we choose a smaller number we will run into hot spots as
             * allocation and de-allocation needs to be synchronized on the PoolArena.
             *
             * See https://github.com/netty/netty/issues/3888.
             */
            int defaultMinNumArena = NettyRuntime.AvailableProcessors() * 2;
            int defaultChunkSize = PageSize << MaxOrder;
            HeapArenaCount = Math.Max(0,
                    SystemPropertyUtil.GetInt(
                            "io.netty.allocator.numHeapArenas",
                            (int)Math.Min(
                                    defaultMinNumArena,
                                    maxMemory / defaultChunkSize / 2 / 3)));
            DirectArenaCount = Math.Max(0,
                    SystemPropertyUtil.GetInt(
                            "io.netty.allocator.numDirectArenas",
                            (int)Math.Min(
                                    defaultMinNumArena,
                                    MaxDirectMemory(maxMemory) / defaultChunkSize / 2 / 3)));
            // cache sizes
            SmallCacheSize = SystemPropertyUtil.GetInt("io.netty.allocator.smallCacheSize", 256);
            NormalCacheSize = SystemPropertyUtil.GetInt("io.netty.allocator.normalCacheSize", 64);
            // 32 kb is the default maximum capacity of the cached buffer. Similar to what is explained in
            // 'Scalable memory allocation using jemalloc'
            MaxCachedCapacity = SystemPropertyUtil.GetInt("io.netty.allocator.maxCachedBufferCapacity", 32 * 1024);
            // the number of threshold of allocations when cached entries will be freed up if not frequently used
            TrimInterval = SystemPropertyUtil.GetInt("io.netty.allocator.cacheTrimInterval", 8192);
            long milliseconds;

            if (SystemPropertyUtil.Contains("io.netty.allocation.cacheTrimIntervalMillis"))
            {
                logger.Warn("-Dio.netty.allocation.cacheTrimIntervalMillis is deprecated, use -Dio.netty.allocator.cacheTrimIntervalMillis");

                if (SystemPropertyUtil.Contains("io.netty.allocator.cacheTrimIntervalMillis"))
                {
                    // Both system properties are specified. Use the non-deprecated one.
                    milliseconds = SystemPropertyUtil.GetLong("io.netty.allocator.cacheTrimIntervalMillis", 0);
                }
                else
                    milliseconds = SystemPropertyUtil.GetLong("io.netty.allocation.cacheTrimIntervalMillis", 0);
            }
            else
                milliseconds = SystemPropertyUtil.GetLong("io.netty.allocator.cacheTrimIntervalMillis", 0);

            // CLR TimeSpan has a narrower range than Java's long millisecond delays.
            TrimPeriod = milliseconds <= 0 ? TimeSpan.Zero : TimeSpan.FromTicks(
                Math.Min(milliseconds, long.MaxValue / TimeSpan.TicksPerMillisecond) * TimeSpan.TicksPerMillisecond);
            UseCacheForAllThreads = SystemPropertyUtil.GetBoolean("io.netty.allocator.useCacheForAllThreads", false);
        }

        // PlatformDependent.maxDirectMemory(): a positive io.netty.maxDirectMemory, otherwise estimateMaxDirectMemory(),
        // which falls back to Runtime.maxMemory() without -XX:MaxDirectMemorySize. CLR: there is no JVM
        // direct-memory flag, so the GC memory budget is that fallback.
        private static long MaxDirectMemory(long maxMemory)
        {
            long maxDirectMemory = SystemPropertyUtil.GetLong("io.netty.maxDirectMemory", -1);
            return maxDirectMemory >= 1 ? maxDirectMemory : maxMemory;
        }
    }

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

    /**
     * Returns the status of the allocator (which contains all metrics) as string. Be aware this may be expensive
     * and so should not called too frequently.
     */
    /// <remarks>Reports configured heap/direct arena order, utilization lists and available small subpages.
    /// Each arena is read under its own lock; the entire allocator is not one transactional snapshot.
    /// Diagnostics reserve no buffer storage and stay available after CLR disposal, when live
    /// reservations may remain while their retired lists are already empty.</remarks>
    public string DumpStats()
    {
        var text = new StringBuilder(512);
        text.Append(_heapArenas.Length.ToString(CultureInfo.InvariantCulture)).Append(" heap arena(s):").Append(Environment.NewLine);

        foreach (PoolArena arena in _heapArenas)
        {
            text.Append(arena);
        }

        text.Append(_directArenas.Length.ToString(CultureInfo.InvariantCulture)).Append(" direct arena(s):").Append(Environment.NewLine);

        foreach (PoolArena arena in _directArenas)
        {
            text.Append(arena);
        }

        return text.ToString();
    }

    // CLR: physical-thread affinity must not flow with ExecutionContext/AsyncLocal.
    // The allocator owns this registry; no thread-local root retains the allocator.
    // Weak thread identity prevents retaining terminated Thread objects and protects
    // against managed thread ID reuse. Each binding owns its optional region caches.
    // CLR: allocation reads the registry without the allocator lock, as the source reads its FastThreadLocal;
    // creation and removal stay serialized by _sync.
    private readonly ConcurrentDictionary<int, ThreadBinding> _threadBindings = new();

    // Slow resize initializes the caller's binding, just like Java reallocate's
    // parent.threadCache(). A miss uses the old arena; a hit uses the cached owner.
    internal PoolThreadCache PrepareThreadBinding()
    {
        using var events = BufferAllocatorEventSource.Log.BeginOperation();
        return GetThreadBinding()?.Cache;
    }

    private ThreadBinding GetThreadBinding()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_heapArenas.Length == 0 && _directArenas.Length == 0)
        {
            return null;
        }

        Thread thread = Thread.CurrentThread;
        if (_threadBindings.TryGetValue(thread.ManagedThreadId, out ThreadBinding binding) && binding.IsOwner(thread))
            return binding;

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return NewThreadBinding(thread);
        }
    }

    // PoolThreadLocalCache.initialValue()
    private ThreadBinding NewThreadBinding(Thread thread)
    {
        Debug.Assert(Monitor.IsEntered(_sync));
        int id = thread.ManagedThreadId;
        if (_threadBindings.TryGetValue(id, out ThreadBinding binding) && binding.IsOwner(thread))
        {
            return binding;
        }

        ReleaseTerminatedThreadBindingsCore();
        IEventExecutor executor = ThreadExecutorMap.CurrentExecutor();
        bool useCache = _useCacheForAllThreads ||
            // If the current thread is a FastThreadLocalThread we will always use the cache
            FastThreadLocalThread.CurrentThreadHasFastThreadLocal() || executor != null;
        // No caching so just use 0 as sizes.
        binding = new ThreadBinding(thread, LeastUsedArena(_heapArenas), LeastUsedArena(_directArenas),
            useCache ? SmallCacheSize : 0, useCache ? NormalCacheSize : 0,
            useCache ? _maxCachedBufferCapacity : 0, useCache ? _cacheTrimInterval : 0);
        _threadBindings[id] = binding;
        binding.HeapArena?.AddThreadBinding();
        binding.DirectArena?.AddThreadBinding();

        try
        {
            // The Thread is used by an EventExecutor, let's use the cache as the chances are good that we
            // will allocate a lot!
            // CLR: explicit constructor sizes remain opt-in. Runtime defaults follow
            // the original eligibility rules; mapped executors permit periodic trim.
            if (_cacheTrimPeriod > TimeSpan.Zero && executor != null)
                binding.StartTrimming(executor, _cacheTrimPeriod);

            if (_threadCleanup != null && FastThreadLocalThread.CurrentThreadWillCleanupFastThreadLocals())
                _threadCleanup.Set(new WeakReference<ThreadBinding>(binding));
        }
        catch
        {
            // Failed scheduling must not publish a partial binding/count or reserve storage.
            _threadBindings.TryRemove(id, out _);
            binding.Detach();
            throw;
        }

        return binding;
    }

    // CLR: a cleanup registration must not root the allocator through its arena/binding.
    // Both links are weak. The allocator registry keeps active bindings alive until detach.
    private sealed class ThreadCleanup(PooledByteBufAllocator allocator) : FastThreadLocal<WeakReference<ThreadBinding>>
    {
        private readonly WeakReference<PooledByteBufAllocator> _allocator = new(allocator);
        protected override void OnRemoval(WeakReference<ThreadBinding> value)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            if (value == null || !value.TryGetTarget(out var binding) || !_allocator.TryGetTarget(out var owner))
            {
                return;
            }

            lock (owner._sync)
            {
                int id = Environment.CurrentManagedThreadId;
                if (!owner._threadBindings.TryGetValue(id, out var current) || !ReferenceEquals(current, binding))
                {
                    return;
                }

                owner._threadBindings.TryRemove(id, out _);
                binding.Detach();
            }
        }
    }

    private static PoolArena LeastUsedArena(PoolArena[] arenas)
    {
        if (arenas.Length == 0)
        {
            return null;
        }

        PoolArena minArena = arenas[0];
        //optimized
        //If it is the first execution, directly return minarena and reduce the number of for loop comparisons below
        if (minArena.NumThreadCaches == 0)
        {
            return minArena;
        }

        for (int i = 1; i < arenas.Length; ++i)
            if (arenas[i].NumThreadCaches < minArena.NumThreadCaches)
            {
                minArena = arenas[i];
            }

        return minArena;
    }

    /**
     * Trim thread local cache for the current {@link Thread}, which will give back any cached memory that was not
     * allocated frequently since the last trim operation.
     *
     * Returns {@code true} if a cache for the current {@link Thread} exists and so was trimmed, false otherwise.
     */
    public bool TrimCurrentThreadCache()
    {
        using var events = BufferAllocatorEventSource.Log.BeginOperation();

        lock (_sync)
        {
            Thread thread = Thread.CurrentThread;
            if (!_threadBindings.TryGetValue(thread.ManagedThreadId, out ThreadBinding binding) || !binding.IsOwner(thread))
                return false;

            binding.Cache.Trim();
            return true;
        }
    }

    /**
     * @deprecated will be removed
     * Returns {@code true} if the calling {@link Thread} has a {@link ThreadLocal} cache for the allocated
     * buffers.
     */
    /// <summary>Whether the calling physical thread currently owns an arena binding.</summary>
    /// <remarks>Replaces the deprecated JVM cache-presence query. This includes cache-disabled
    /// bindings; reading it never creates, trims or removes a binding. Arena-free fallback,
    /// empty sentinels and composite factories create none. False after allocator disposal.</remarks>
    public bool HasCurrentThreadBinding
    {
        get
        {
            lock (_sync)
            {
                Thread thread = Thread.CurrentThread;
                return _threadBindings.TryGetValue(thread.ManagedThreadId, out ThreadBinding binding) && binding.IsOwner(thread);
            }
        }
    }

    /// <summary>Drains cached regions and releases the calling physical thread's arena assignment.</summary>
    /// <remarks>Returns false if the thread has no assignment. The next factory allocation
    /// chooses the least-used arenas again. This operation does not return live buffers
    /// or their operation leases. Later returns bypass the detached cache.</remarks>
    public bool ReleaseCurrentThreadBinding()
    {
        using var events = BufferAllocatorEventSource.Log.BeginOperation();

        lock (_sync)
        {
            Thread thread = Thread.CurrentThread;
            if (!_threadBindings.TryGetValue(thread.ManagedThreadId, out ThreadBinding binding) || !binding.IsOwner(thread))
                return false;

            _threadBindings.TryRemove(thread.ManagedThreadId, out _);
            binding.Detach();
            return true;
        }
    }

    /// <summary>Reclaims arena assignments for terminated or collected physical threads.</summary>
    /// <returns>The number of removed thread assignments, independent of storage kind.</returns>
    /// <remarks>Assignments on live CLR worker threads remain until explicit release or
    /// allocator disposal. Buffers retain their owning arena across thread exit and awaits.</remarks>
    public int ReleaseTerminatedThreadBindings()
    {
        using var events = BufferAllocatorEventSource.Log.BeginOperation();

        lock (_sync)
        {
            return ReleaseTerminatedThreadBindingsCore();
        }
    }

    private int ReleaseTerminatedThreadBindingsCore()
    {
        Debug.Assert(Monitor.IsEntered(_sync));
        List<int> removed = null;

        foreach (var pair in _threadBindings)
            if (!pair.Value.IsAlive)
            {
                (removed ??= new List<int>()).Add(pair.Key);
            }

        if (removed == null)
        {
            return 0;
        }

        foreach (int id in removed)
        {
            _threadBindings.TryRemove(id, out ThreadBinding binding);
            binding.Detach();
        }

        return removed.Count;
    }

    internal int NumThreadBindings
    {
        get
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();

            lock (_sync)
            {
                ReleaseTerminatedThreadBindingsCore();
                return _threadBindings.Count;
            }
        }
    }

    private sealed class ThreadBinding
    {
        private readonly WeakReference<Thread> _thread;
        internal readonly PoolArena HeapArena;
        internal readonly PoolArena DirectArena;
        internal readonly PoolThreadCache Cache;
        private CancellationTokenSource _trimCancellation;

        internal ThreadBinding(Thread thread, PoolArena heapArena, PoolArena directArena,
            int smallCacheSize, int normalCacheSize, int maxCachedBufferCapacity, int cacheTrimInterval)
        {
            _thread = new WeakReference<Thread>(thread);
            HeapArena = heapArena;
            DirectArena = directArena;
            Cache = new PoolThreadCache(heapArena, directArena, smallCacheSize, normalCacheSize,
                maxCachedBufferCapacity, cacheTrimInterval);
        }

        internal bool IsOwner(Thread thread)
        {
            return _thread.TryGetTarget(out Thread owner) && ReferenceEquals(owner, thread);
        }
        internal bool IsAlive => _thread.TryGetTarget(out Thread thread) && thread.IsAlive;

        internal void StartTrimming(IEventExecutor executor, TimeSpan period)
        {
            // Housekeeping must not retain the caller's AsyncLocal payload/context.
            if (ExecutionContext.IsFlowSuppressed())
            {
                StartTrimmingCore(executor, period);
            }
            else
            {
                using (ExecutionContext.SuppressFlow())
                {
                    StartTrimmingCore(executor, period);
                }
            }
        }

        private void StartTrimmingCore(IEventExecutor executor, TimeSpan period)
        {
            _trimCancellation = new CancellationTokenSource();
            // Target this registration, not a task continuation's current thread.
            // Cache's Monitor permits unordered executors and prevents an old
            // reservation from trimming a later binding after release/reassignment.
            Task task = executor.ScheduleAtFixedRateAsync(Cache.Trim, period, period, _trimCancellation.Token);
            if (task.IsCompleted)
            {
                task.GetAwaiter().GetResult();
            }

            // Immediate rejection is propagated to the factory; later scheduler
            // faults are observed without retaining the binding in a continuation.
            _ = task.ContinueWith(static failed =>
            {
                _ = failed.Exception;
            }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        /**
         *  Should be called if the Thread that uses this cache is about to exit to release resources out of the cache
         */
        internal void Detach()
        {
            // As free() may be called either by the finalizer or by FastThreadLocal.onRemoval(...) we need to ensure
            // we only call this one time.
            // CLR: removing the registration under the allocator Monitor makes detach
            // exactly once, replacing the Java finalizer/AtomicBoolean strategy.
            // CLR: cancellation removes queued housekeeping and releases captured
            // cache/allocator references. A callback already transferred to a ready
            // queue cannot revive the closed cache or initialize another binding.
            try
            {
                _trimCancellation?.Cancel();
            }
            finally
            {
                _trimCancellation?.Dispose();
                Cache.Dispose();
                HeapArena?.RemoveThreadBinding();
                DirectArena?.RemoveThreadBinding();
            }
        }
    }
}
