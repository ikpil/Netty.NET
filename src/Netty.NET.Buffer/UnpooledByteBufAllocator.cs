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

using Netty.NET.Common;

namespace Netty.NET.Buffer;

/**
 * Simplistic {@link ByteBufAllocator} implementation that does not pool anything.
 */
/// <remarks>This stage implements allocation policy and provenance. Metrics and leak-aware wrappers remain pending.
/// NativeMemoryAllocator supplies the CLR reservation domain; JVM Unsafe/Cleaner switches are not exposed.</remarks>
public sealed class UnpooledByteBufAllocator : AbstractByteBufAllocator
{
    /// <summary>The unpooled default, using heap storage for Buffer and native storage for IoBuffer.</summary>
    public static UnpooledByteBufAllocator Default { get; } = new();

    private readonly NativeMemoryAllocator _nativeAllocator;

    /**
     * Create a new instance which uses leak-detection for direct buffers.
     *
     * @param preferDirect {@code true} if {@link #buffer(int)} should try to allocate a direct buffer rather than
     *                     a heap buffer
     */
    /// <remarks>Leak detection mentioned in the original documentation is pending in this port.
    /// An optional native allocator controls reservations for direct buffers, their copies and composite padding.</remarks>
    public UnpooledByteBufAllocator(bool preferDirect = false, NativeMemoryAllocator nativeAllocator = null)
        : base(preferDirect)
    {
        _nativeAllocator = nativeAllocator ?? NativeMemoryAllocator.Shared;
    }

    public override bool IsDirectBufferPooled => false;

    protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity)
        => new UnpooledHeapByteBuf(this, initialCapacity, maxCapacity);

    protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
        => new UnpooledDirectByteBuf(this, initialCapacity, maxCapacity, _nativeAllocator);
}
