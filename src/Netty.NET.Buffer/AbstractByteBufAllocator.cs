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
using Netty.NET.Common;

namespace Netty.NET.Buffer;

/**
 * Skeletal {@link ByteBufAllocator} implementation to extend.
 */
/// <remarks>NativeMemoryOwner can reliably free CLR native allocations, so preferDirect does not
/// depend on JVM Unsafe/Cleaner detection. IoBuffer always chooses native storage.</remarks>
public abstract class AbstractByteBufAllocator : IByteBufAllocator
{
    static AbstractByteBufAllocator()
        => ResourceLeakDetector.AddExclusions(typeof(AbstractByteBufAllocator), nameof(ToLeakAwareBuffer));

    protected static ByteBuf ToLeakAwareBuffer(ByteBuf buffer)
    {
        IResourceLeakTracker<ByteBuf> leak = ByteBuf.LeakDetector.Track(buffer);
        if (leak == null) return buffer;
        return ByteBuf.LeakDetector.IsRecordEnabled()
            ? new AdvancedLeakAwareByteBuf(buffer, leak) : new SimpleLeakAwareByteBuf(buffer, leak);
    }

    protected static CompositeByteBuf ToLeakAwareBuffer(CompositeByteBuf buffer)
    {
        IResourceLeakTracker<ByteBuf> leak = ByteBuf.LeakDetector.Track(buffer);
        if (leak == null) return buffer;
        return ByteBuf.LeakDetector.IsRecordEnabled()
            ? new AdvancedLeakAwareCompositeByteBuf(buffer, leak) : new SimpleLeakAwareCompositeByteBuf(buffer, leak);
    }

    private readonly ByteBuf _emptyBuffer;
    protected bool DirectByDefault { get; }

    /**
     * Instance use heap buffers by default
     */
    /**
     * Create new instance
     *
     * @param preferDirect {@code true} if {@link #buffer(int)} should try to allocate a direct buffer rather than
     *                     a heap buffer
     */
    protected AbstractByteBufAllocator(bool preferDirect = false)
    {
        DirectByDefault = preferDirect;
        _emptyBuffer = new EmptyByteBuf(this);
    }

    public virtual ByteBuf Buffer(int initialCapacity = 256, int maxCapacity = int.MaxValue)
        => DirectByDefault ? DirectBuffer(initialCapacity, maxCapacity) : HeapBuffer(initialCapacity, maxCapacity);

    public virtual ByteBuf IoBuffer(int initialCapacity = 256, int maxCapacity = int.MaxValue)
        => DirectBuffer(initialCapacity, maxCapacity);

    public virtual ByteBuf HeapBuffer(int initialCapacity = 256, int maxCapacity = int.MaxValue)
    {
        Validate(initialCapacity, maxCapacity);
        return initialCapacity == 0 && maxCapacity == 0 ? _emptyBuffer : NewHeapBuffer(initialCapacity, maxCapacity);
    }

    public virtual ByteBuf DirectBuffer(int initialCapacity = 256, int maxCapacity = int.MaxValue)
    {
        Validate(initialCapacity, maxCapacity);
        return initialCapacity == 0 && maxCapacity == 0 ? _emptyBuffer : NewDirectBuffer(initialCapacity, maxCapacity);
    }

    public virtual CompositeByteBuf CompositeBuffer(int maxNumComponents = 16)
        => DirectByDefault ? CompositeDirectBuffer(maxNumComponents) : CompositeHeapBuffer(maxNumComponents);

    public virtual CompositeByteBuf CompositeHeapBuffer(int maxNumComponents = 16)
        => ToLeakAwareBuffer(new CompositeByteBuf(this, false, maxNumComponents));

    public virtual CompositeByteBuf CompositeDirectBuffer(int maxNumComponents = 16)
        => ToLeakAwareBuffer(new CompositeByteBuf(this, true, maxNumComponents));

    public abstract bool IsDirectBufferPooled { get; }

    // 4 MiB page
    // CLR: the shared capacity kernel owns this threshold; the allocator hook delegates to it.
    public virtual int CalculateNewCapacity(int minimumNewCapacity, int maxCapacity)
        => ByteBuf.CalculateNewCapacity(minimumNewCapacity, maxCapacity);

    private static void Validate(int initialCapacity, int maxCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);
        if (initialCapacity > maxCapacity) throw new ArgumentOutOfRangeException(nameof(initialCapacity));
    }

    /**
     * Create a heap {@link ByteBuf} with the given initialCapacity and maxCapacity.
     */
    protected abstract ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity);

    /**
     * Create a direct {@link ByteBuf} with the given initialCapacity and maxCapacity.
     */
    protected abstract ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity);

    public override string ToString() => $"{GetType().Name}(directByDefault: {DirectByDefault.ToString().ToLowerInvariant()})";
}
