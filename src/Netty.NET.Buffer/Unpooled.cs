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

namespace Netty.NET.Buffer;

// Implemented unpooled factories; allocator policy and remaining wrapping overloads are pending.
public static partial class Unpooled
{
    public static ByteBuf EmptyBuffer { get; } = new EmptyByteBuf();
    /**
         * Creates a new big-endian Java heap buffer with the specified
         * {@code initialCapacity}, that may grow up to {@code maxCapacity}
         * The new buffer's {@code readerIndex} and {@code writerIndex} are
         * {@code 0}.
         */
    public static ByteBuf Buffer(int initialCapacity = 256, int maxCapacity = int.MaxValue)
        => initialCapacity == 0 && maxCapacity == 0 ? EmptyBuffer : new UnpooledHeapByteBuf(initialCapacity, maxCapacity);
    /**
         * Creates a new big-endian buffer which wraps the specified {@code array}.
         * A modification on the specified array's content will be visible to the
         * returned buffer.
         */
    public static ByteBuf WrappedBuffer(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return bytes.Length == 0 ? EmptyBuffer : new UnpooledHeapByteBuf(bytes);
    }
    /**
         * Creates a new big-endian buffer whose content is a copy of the
         * specified {@code array}.  The new buffer's {@code readerIndex} and
         * {@code writerIndex} are {@code 0} and {@code array.length} respectively.
         */
    public static ByteBuf CopiedBuffer(ReadOnlySpan<byte> bytes)
        => bytes.IsEmpty ? EmptyBuffer : Buffer(bytes.Length).WriteBytes(bytes);

    /**
         * Creates a new big-endian direct buffer with the specified
         * {@code initialCapacity}, that may grow up to {@code maxCapacity}.
         * The new buffer's {@code readerIndex} and {@code writerIndex} are
         * {@code 0}.
         */
    public static ByteBuf DirectBuffer(int initialCapacity = 256, int maxCapacity = int.MaxValue)
        => initialCapacity == 0 && maxCapacity == 0 ? EmptyBuffer : new UnpooledDirectByteBuf(initialCapacity, maxCapacity);
    /**
     * Returns a new big-endian composite buffer with no components.
     */
    public static CompositeByteBuf CompositeBuffer(int maxNumComponents = 16) => new(maxNumComponents);
}
