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
/// arenas, with opt-in small/normal region caches and executor-based periodic trimming. Allocator events
/// remain pending. CLR options replace JVM runtime/system-property heuristics.
/// Dispose closes factory allocations and pooled arenas; live pooled buffers and operation
/// leases keep their reservations until returned. Unpooled fallback buffers own independent
/// lifetimes. Empty sentinels/composites reserve no storage and remain available.
/// A positive cacheTrimPeriod schedules fixed-rate trim only when the binding is
/// first created on a mapped event executor. Zero disables scheduling. Releasing
/// the binding or allocator cancels its schedule; no caller ExecutionContext is retained.</remarks>
public partial class PooledByteBufAllocator : AbstractByteBufAllocator, IByteBufAllocatorMetricProvider, IDisposable
{
    public static PooledByteBufAllocator Default { get; } = new(preferDirect: ByteBufUtil.PreferDirect);
    private readonly object _sync = new();
    private readonly NativeMemoryAllocator _nativeAllocator;
    private bool _disposed;
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

    // No caching so just use 0 as sizes.
    // CLR: keep the existing default policy; explicit nonzero sizes enable caching.
    public PooledByteBufAllocator(bool preferDirect = false, bool poolHeap = true, bool poolDirect = true,
        int pageSize = 8192, int maxOrder = 9, int directMemoryCacheAlignment = 0,
        NativeMemoryAllocator nativeAllocator = null, int smallCacheSize = 0, int normalCacheSize = 0,
        int maxCachedBufferCapacity = 32768, int cacheTrimInterval = 8192, TimeSpan cacheTrimPeriod = default)
        : this(preferDirect, poolHeap ? 1 : 0, poolDirect ? 1 : 0, pageSize, maxOrder,
            directMemoryCacheAlignment, nativeAllocator, smallCacheSize, normalCacheSize,
            maxCachedBufferCapacity, cacheTrimInterval, cacheTrimPeriod) { }

    public PooledByteBufAllocator(bool preferDirect, int heapArenaCount, int directArenaCount,
        int pageSize = 8192, int maxOrder = 9, int directMemoryCacheAlignment = 0,
        NativeMemoryAllocator nativeAllocator = null, int smallCacheSize = 0, int normalCacheSize = 0,
        int maxCachedBufferCapacity = 32768, int cacheTrimInterval = 8192, TimeSpan cacheTrimPeriod = default) : base(preferDirect)
    {
        if (directMemoryCacheAlignment < 0 ||
            directMemoryCacheAlignment != 0 && !BitOperations.IsPow2(directMemoryCacheAlignment))
            throw new ArgumentOutOfRangeException(nameof(directMemoryCacheAlignment));
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (directMemoryCacheAlignment > 0)
        {
            // Ensure page size is a whole multiple of the alignment, or bump it to the next whole multiple.
            long aligned = ((long)pageSize + directMemoryCacheAlignment - 1) & -(long)directMemoryCacheAlignment;
            if (aligned > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(pageSize));
            pageSize = (int)aligned;
        }
        if (pageSize < 4096 || !BitOperations.IsPow2(pageSize))
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (maxOrder < 0 || maxOrder > 14) throw new ArgumentOutOfRangeException(nameof(maxOrder));
        // Ensure the resulting chunkSize does not overflow.
        long chunkSize = (long)pageSize << maxOrder;
        if (chunkSize > 1 << 30) throw new ArgumentOutOfRangeException(nameof(maxOrder));
        ArgumentOutOfRangeException.ThrowIfNegative(heapArenaCount);
        ArgumentOutOfRangeException.ThrowIfNegative(directArenaCount);
        ArgumentOutOfRangeException.ThrowIfNegative(smallCacheSize);
        ArgumentOutOfRangeException.ThrowIfNegative(normalCacheSize);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCachedBufferCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cacheTrimInterval);
        if (cacheTrimPeriod < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(cacheTrimPeriod));
        SmallCacheSize = smallCacheSize;
        NormalCacheSize = normalCacheSize;
        _maxCachedBufferCapacity = maxCachedBufferCapacity;
        _cacheTrimInterval = cacheTrimInterval;
        _cacheTrimPeriod = cacheTrimPeriod;
        ChunkSize = (int)chunkSize;
        // Logarithm base 2. At this point we know that pageSize is a power of two.
        int shifts = BitOperations.Log2((uint)pageSize);
        _nativeAllocator = nativeAllocator ?? NativeMemoryAllocator.Shared;
        _heapArenas = new PoolArena[heapArenaCount];
        _directArenas = new PoolArena[directArenaCount];
        if (heapArenaCount != 0)
        {
            var sizeClasses = new SizeClasses(pageSize, shifts, ChunkSize, 0);
            for (int i = 0; i < heapArenaCount; ++i) _heapArenas[i] = new PoolHeapArena(this, sizeClasses);
        }
        if (directArenaCount != 0)
        {
            var sizeClasses = new SizeClasses(pageSize, shifts, ChunkSize, directMemoryCacheAlignment);
            for (int i = 0; i < directArenaCount; ++i)
                _directArenas[i] = new PoolDirectArena(this, sizeClasses, _nativeAllocator);
        }
        HeapArenas = Array.AsReadOnly<IPoolArenaMetric>(_heapArenas);
        DirectArenas = Array.AsReadOnly<IPoolArenaMetric>(_directArenas);
        Metric = new PooledByteBufAllocatorMetric(this);
    }

    public override bool IsDirectBufferPooled => _directArenas.Length != 0;

    protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity)
    {
        ByteBuf buffer;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ThreadBinding binding = GetThreadBinding();
            PoolArena arena = binding?.HeapArena;
            buffer = arena != null ? arena.Allocate(initialCapacity, maxCapacity, binding.Cache)
                : new UnpooledHeapByteBuf(this, initialCapacity, maxCapacity);
        }
        // CLR: detector callbacks run after the allocator lock; the buffer owns its reservation.
        return ToLeakAwareBuffer(buffer);
    }

    protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
    {
        ByteBuf buffer;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ThreadBinding binding = GetThreadBinding();
            PoolArena arena = binding?.DirectArena;
            buffer = arena != null ? arena.Allocate(initialCapacity, maxCapacity, binding.Cache)
                : new UnpooledDirectByteBuf(this, initialCapacity, maxCapacity, _nativeAllocator);
        }
        return ToLeakAwareBuffer(buffer);
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
        if (arenas.Length == 0) return -1;
        long total = 0;
        foreach (PoolArena arena in arenas)
        {
            total = unchecked(total + (pinned ? arena.NumPinnedBytes : arena.NumActiveBytes));
            if (total < 0) return long.MaxValue;
        }
        return total;
    }

    /**
     * Return {@code true} if direct memory cache alignment is supported, {@code false} otherwise.
     */
    public static bool IsDirectMemoryCacheAlignmentSupported => true;

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (ThreadBinding binding in _threadBindings.Values) binding.Detach();
            _threadBindings.Clear();
            foreach (PoolArena arena in _heapArenas) arena.Dispose();
            foreach (PoolArena arena in _directArenas) arena.Dispose();
        }
    }
}
