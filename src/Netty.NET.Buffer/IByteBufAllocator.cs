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

namespace Netty.NET.Buffer;

/**
 * Implementations are responsible to allocate buffers. Implementations of this interface are expected to be
 * thread-safe.
 */
/// <remarks>Optional parameters cover the Java no-argument and one-argument overloads.
/// Default currently selects the unpooled heap policy; global pooled/adaptive selection remains pending.</remarks>
public interface IByteBufAllocator
{
    static IByteBufAllocator Default => UnpooledByteBufAllocator.Default;

    /**
     * Allocate a {@link ByteBuf}. If it is a direct or heap buffer
     * depends on the actual implementation.
     */
    /**
     * Allocate a {@link ByteBuf} with the given initial capacity.
     * If it is a direct or heap buffer depends on the actual implementation.
     */
    /**
     * Allocate a {@link ByteBuf} with the given initial capacity and the given
     * maximal capacity. If it is a direct or heap buffer depends on the actual
     * implementation.
     */
    ByteBuf Buffer(int initialCapacity = 256, int maxCapacity = int.MaxValue);

    /**
     * Allocate a {@link ByteBuf}, preferably a direct buffer which is suitable for I/O.
     */
    /**
     * Allocate a {@link ByteBuf}, preferably a direct buffer which is suitable for I/O.
     */
    /**
     * Allocate a {@link ByteBuf}, preferably a direct buffer which is suitable for I/O.
     */
    ByteBuf IoBuffer(int initialCapacity = 256, int maxCapacity = int.MaxValue);

    /**
     * Allocate a heap {@link ByteBuf}.
     */
    /**
     * Allocate a heap {@link ByteBuf} with the given initial capacity.
     */
    /**
     * Allocate a heap {@link ByteBuf} with the given initial capacity and the given
     * maximal capacity.
     */
    ByteBuf HeapBuffer(int initialCapacity = 256, int maxCapacity = int.MaxValue);

    /**
     * Allocate a direct {@link ByteBuf}.
     */
    /**
     * Allocate a direct {@link ByteBuf} with the given initial capacity.
     */
    /**
     * Allocate a direct {@link ByteBuf} with the given initial capacity and the given
     * maximal capacity.
     */
    ByteBuf DirectBuffer(int initialCapacity = 256, int maxCapacity = int.MaxValue);

    /**
     * Allocate a {@link CompositeByteBuf}.
     * If it is a direct or heap buffer depends on the actual implementation.
     */
    /**
     * Allocate a {@link CompositeByteBuf} with the given maximum number of components that can be stored in it.
     * If it is a direct or heap buffer depends on the actual implementation.
     */
    CompositeByteBuf CompositeBuffer(int maxNumComponents = 16);

    /**
     * Allocate a heap {@link CompositeByteBuf}.
     */
    /**
     * Allocate a heap {@link CompositeByteBuf} with the given maximum number of components that can be stored in it.
     */
    CompositeByteBuf CompositeHeapBuffer(int maxNumComponents = 16);

    /**
     * Allocate a direct {@link CompositeByteBuf}.
     */
    /**
     * Allocate a direct {@link CompositeByteBuf} with the given maximum number of components that can be stored in it.
     */
    CompositeByteBuf CompositeDirectBuffer(int maxNumComponents = 16);

    /**
     * Returns {@code true} if direct {@link ByteBuf}'s are pooled
     */
    bool IsDirectBufferPooled { get; }

    /**
     * Calculate the new capacity of a {@link ByteBuf} that is used when a {@link ByteBuf} needs to expand by the
     * {@code minNewCapacity} with {@code maxCapacity} as upper-bound.
     */
    int CalculateNewCapacity(int minimumNewCapacity, int maxCapacity);
}
