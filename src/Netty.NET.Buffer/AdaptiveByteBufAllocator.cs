/*
 * Copyright 2024 The Netty Project
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
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer;

/**
 * An auto-tuning pooling {@link ByteBufAllocator}, that follows an anti-generational hypothesis.
 * <p>
 * <strong>Note:</strong> this allocator is <strong>experimental</strong>. It is recommended to roll out usage slowly,
 * and to carefully monitor application performance in the process.
 * <p>
 * See the {@link AdaptivePoolingAllocator} class documentation for implementation details.
 */
/// <remarks>Shared striped magazines pool heap/native chunks. Physical-thread local
/// magazines and events remain pending. lowMemory is explicit; CLR GC heap limits are
/// not treated as native memory budgets. Dispose closes allocations and drains all groups;
/// live buffers and pin/operation claims keep their regions until final return. Raw Memory
/// remains borrowed. Empty sentinels/composites reserve no storage and remain available.
/// The global default allocator is unchanged.</remarks>
public sealed class AdaptiveByteBufAllocator : AbstractByteBufAllocator, IByteBufAllocatorMetricProvider,
    IByteBufAllocatorMetric, IDisposable
{
    private readonly object _disposeLock = new();
    private bool _disposed;
    internal AdaptivePoolingAllocator HeapPool { get; }
    internal AdaptivePoolingAllocator DirectPool { get; }
    public IByteBufAllocatorMetric Metric => this;
    public long UsedHeapMemory => HeapPool.UsedMemory;
    public long UsedDirectMemory => DirectPool.UsedMemory;
    public override bool IsDirectBufferPooled => true;

    /**
     * The capacity if the chunk reuse queues, that allow chunks to be shared across magazines in a group.
     * The default size is twice {@link NettyRuntime#availableProcessors()},
     * same as the maximum number of magazines per magazine group.
     */
    /// <summary>Creates an adaptive allocator using shared magazines and explicit native reservations.</summary>
    public AdaptiveByteBufAllocator(bool preferDirect = true, bool lowMemory = false, NativeMemoryAllocator nativeAllocator = null)
        : this(preferDirect, lowMemory, nativeAllocator,
            Math.Max(2, SystemPropertyUtil.GetInt("io.netty.allocator.chunkReuseQueueCapacity", Environment.ProcessorCount * 2)),
            Math.Max(1, SystemPropertyUtil.GetLong("io.netty.allocator.chunkPurgePollsShared", 128)),
            Math.Max(1, SystemPropertyUtil.GetInt("io.netty.allocator.chunkPurgeThreshold", 3)), Environment.ProcessorCount * 2) { }

    internal AdaptiveByteBufAllocator(bool preferDirect, bool lowMemory, NativeMemoryAllocator nativeAllocator,
        int reuseCapacity, long purgePolls, int purgeThreshold, int maxStripes) : base(preferDirect)
    {
        NativeMemoryAllocator native = nativeAllocator ?? NativeMemoryAllocator.Shared;
        HeapPool = new AdaptivePoolingAllocator((size, max) => new UnpooledHeapByteBuf(this, size, max),
            lowMemory, reuseCapacity, purgePolls, purgeThreshold, maxStripes);
        DirectPool = new AdaptivePoolingAllocator((size, max) => new UnpooledDirectByteBuf(this, size, max, native),
            lowMemory, reuseCapacity, purgePolls, purgeThreshold, maxStripes);
    }
    protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
        return ToLeakAwareBuffer(HeapPool.Allocate(initialCapacity, maxCapacity));
    }
    protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
        return ToLeakAwareBuffer(DirectPool.Allocate(initialCapacity, maxCapacity));
    }
    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_disposed) return;
            Volatile.Write(ref _disposed, true);
            HeapPool.Dispose(); DirectPool.Dispose();
        }
    }
}
