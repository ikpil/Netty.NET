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

using System.Threading;
using Netty.NET.Common;

namespace Netty.NET.Buffer;

/**
 * Simplistic {@link ByteBufAllocator} implementation that does not pool anything.
 */
/// <remarks>Direct and composite allocations use sampled leak detection unless explicitly disabled.
/// NativeMemoryAllocator supplies the CLR reservation domain; JVM Unsafe/Cleaner switches are not exposed.</remarks>
public sealed class UnpooledByteBufAllocator : AbstractByteBufAllocator, IByteBufAllocatorMetricProvider
{
    /**
     * Default instance which uses leak-detection for direct buffers.
     */
    /// <summary>The unpooled default, using heap storage for Buffer and native storage for IoBuffer.</summary>
    public static UnpooledByteBufAllocator Default { get; } = new();

    private readonly NativeMemoryAllocator _nativeAllocator;
    private readonly bool _disableLeakDetector;
    private readonly UnpooledByteBufAllocatorMetric _metric = new();

    /// <summary>Capacity owned by buffers created through this allocator's factories.</summary>
    /// <remarks>Views and additional references do not allocate storage. Final buffer release
    /// removes its capacity even if a CLR pin still preserves physical native storage.
    /// NativeMemoryAllocator.ReservedBytes separately measures those physical reservations.
    /// Individual counters are atomic; reading both is not a transactional snapshot.</remarks>
    public IByteBufAllocatorMetric Metric => _metric;

    /**
     * Create a new instance which uses leak-detection for direct buffers.
     *
     * @param preferDirect {@code true} if {@link #buffer(int)} should try to allocate a direct buffer rather than
     *                     a heap buffer
     */
    /// <remarks>An optional native allocator controls reservations for direct buffers, their copies and composite padding.</remarks>
    public UnpooledByteBufAllocator(bool preferDirect = false, NativeMemoryAllocator nativeAllocator = null)
        : this(preferDirect, false, nativeAllocator) { }

    /**
     * Create a new instance
     *
     * @param preferDirect {@code true} if {@link #buffer(int)} should try to allocate a direct buffer rather than
     *                     a heap buffer
     * @param disableLeakDetector {@code true} if the leak-detection should be disabled completely for this
     *                            allocator. This can be useful if the user just want to depend on the GC to handle
     *                            direct buffers when not explicit released.
     */
    /// <remarks>NativeMemoryAllocator replaces JVM Unsafe/NoCleaner provider selection.</remarks>
    public UnpooledByteBufAllocator(bool preferDirect, bool disableLeakDetector, NativeMemoryAllocator nativeAllocator = null)
        : base(preferDirect)
    {
        _nativeAllocator = nativeAllocator ?? NativeMemoryAllocator.Shared;
        _disableLeakDetector = disableLeakDetector;
    }

    public override bool IsDirectBufferPooled => false;

    protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity)
        => new UnpooledHeapByteBuf(this, initialCapacity, maxCapacity, _metric);

    protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
    {
        var buffer = new UnpooledDirectByteBuf(this, initialCapacity, maxCapacity, _nativeAllocator, _metric);
        return _disableLeakDetector ? buffer : ToLeakAwareBuffer(buffer);
    }

    public override CompositeByteBuf CompositeHeapBuffer(int maxNumComponents = 16)
    {
        var buffer = new CompositeByteBuf(this, false, maxNumComponents);
        return _disableLeakDetector ? buffer : ToLeakAwareBuffer(buffer);
    }

    public override CompositeByteBuf CompositeDirectBuffer(int maxNumComponents = 16)
    {
        var buffer = new CompositeByteBuf(this, true, maxNumComponents);
        return _disableLeakDetector ? buffer : ToLeakAwareBuffer(buffer);
    }

    // CLR: one optional counter sink replaces Java's provider-specific instrumented subclasses.
    // Only allocator factories supply it; directly constructed or wrapped storage stays uncharged.
    internal sealed class UnpooledByteBufAllocatorMetric : IByteBufAllocatorMetric
    {
        private long _heapBytes;
        private long _directBytes;
        public long UsedHeapMemory => Volatile.Read(ref _heapBytes);
        public long UsedDirectMemory => Volatile.Read(ref _directBytes);
        internal void AddHeap(long amount) => Interlocked.Add(ref _heapBytes, amount);
        internal void AddDirect(long amount) => Interlocked.Add(ref _directBytes, amount);
        public override string ToString()
            => $"{nameof(UnpooledByteBufAllocatorMetric)}(usedHeapMemory: {UsedHeapMemory}; usedDirectMemory: {UsedDirectMemory})";
    }
}
