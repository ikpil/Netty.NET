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
/// <remarks>This implementation uses one arena per enabled storage kind and no thread cache.
/// Multi-arena thread assignment, cache scheduling, allocator events and automatic leak
/// wrapping remain pending. CLR options replace JVM runtime/system-property heuristics.
/// Dispose closes factory allocations and pooled arenas; live pooled buffers and operation
/// leases keep their reservations until returned. Unpooled fallback buffers own independent
/// lifetimes. Empty sentinels/composites reserve no storage and remain available.</remarks>
public class PooledByteBufAllocator : AbstractByteBufAllocator, IByteBufAllocatorMetricProvider, IDisposable
{
    public static PooledByteBufAllocator Default { get; } = new(preferDirect: true);
    private readonly object _sync = new();
    private readonly NativeMemoryAllocator _nativeAllocator;
    private bool _disposed;
    internal PoolHeapArena HeapArena { get; }
    internal PoolDirectArena DirectArena { get; }
    internal IReadOnlyList<IPoolArenaMetric> HeapArenas { get; }
    internal IReadOnlyList<IPoolArenaMetric> DirectArenas { get; }
    internal int ChunkSize { get; }
    public PooledByteBufAllocatorMetric Metric { get; }
    IByteBufAllocatorMetric IByteBufAllocatorMetricProvider.Metric => Metric;

    public PooledByteBufAllocator(bool preferDirect = false, bool poolHeap = true, bool poolDirect = true,
        int pageSize = 8192, int maxOrder = 9, int directMemoryCacheAlignment = 0,
        NativeMemoryAllocator nativeAllocator = null) : base(preferDirect)
    {
        if (directMemoryCacheAlignment < 0 ||
            directMemoryCacheAlignment != 0 && !BitOperations.IsPow2(directMemoryCacheAlignment))
            throw new ArgumentOutOfRangeException(nameof(directMemoryCacheAlignment));
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (directMemoryCacheAlignment > 0)
        {
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
        ChunkSize = (int)chunkSize;
        // Logarithm base 2. At this point we know that pageSize is a power of two.
        int shifts = BitOperations.Log2((uint)pageSize);
        _nativeAllocator = nativeAllocator ?? NativeMemoryAllocator.Shared;
        if (poolHeap) HeapArena = new PoolHeapArena(this, new SizeClasses(pageSize, shifts, ChunkSize, 0));
        if (poolDirect) DirectArena = new PoolDirectArena(this,
            new SizeClasses(pageSize, shifts, ChunkSize, directMemoryCacheAlignment), _nativeAllocator);
        HeapArenas = poolHeap ? Array.AsReadOnly(new IPoolArenaMetric[] { HeapArena }) : Array.Empty<IPoolArenaMetric>();
        DirectArenas = poolDirect ? Array.AsReadOnly(new IPoolArenaMetric[] { DirectArena }) : Array.Empty<IPoolArenaMetric>();
        Metric = new PooledByteBufAllocatorMetric(this);
    }

    public override bool IsDirectBufferPooled => DirectArena != null;

    protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return HeapArena != null ? HeapArena.Allocate(initialCapacity, maxCapacity)
                : new UnpooledHeapByteBuf(this, initialCapacity, maxCapacity);
        }
    }

    protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return DirectArena != null ? DirectArena.Allocate(initialCapacity, maxCapacity)
                : new UnpooledDirectByteBuf(this, initialCapacity, maxCapacity, _nativeAllocator);
        }
    }

    /**
     * Returns the number of bytes of heap memory that is currently pinned to heap buffers allocated by a
     * {@link ByteBufAllocator}, or {@code -1} if unknown.
     * A buffer can pin more memory than its {@linkplain ByteBuf#capacity() capacity} might indicate,
     * due to implementation details of the allocator.
     */
    public long PinnedHeapMemory => HeapArena?.NumPinnedBytes ?? -1;

    /**
     * Returns the number of bytes of direct memory that is currently pinned to direct buffers allocated by a
     * {@link ByteBufAllocator}, or {@code -1} if unknown.
     * A buffer can pin more memory than its {@linkplain ByteBuf#capacity() capacity} might indicate,
     * due to implementation details of the allocator.
     */
    public long PinnedDirectMemory => DirectArena?.NumPinnedBytes ?? -1;

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
            HeapArena?.Dispose();
            DirectArena?.Dispose();
        }
    }
}
