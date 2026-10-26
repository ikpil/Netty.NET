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

/**
 * Creates a new {@link ByteBuf} by allocating new space or by wrapping
 * or copying existing byte arrays, byte buffers and a string.
 *
 * <h3>Use static import</h3>
 * This classes is intended to be used with Java 5 static import statement:
 *
 * <pre>
 * import static io.netty.buffer.{@link Unpooled}.*;
 *
 * {@link ByteBuf} heapBuffer    = buffer(128);
 * {@link ByteBuf} directBuffer  = directBuffer(256);
 * {@link ByteBuf} wrappedBuffer = wrappedBuffer(new byte[128], new byte[256]);
 * {@link ByteBuf} copiedBuffer  = copiedBuffer({@link ByteBuffer}.allocate(128));
 * </pre>
 *
 * <h3>Allocating a new buffer</h3>
 *
 * Three buffer types are provided out of the box.
 *
 * <ul>
 * <li>{@link #buffer(int)} allocates a new fixed-capacity heap buffer.</li>
 * <li>{@link #directBuffer(int)} allocates a new fixed-capacity direct buffer.</li>
 * </ul>
 *
 * <h3>Creating a wrapped buffer</h3>
 *
 * Wrapped buffer is a buffer which is a view of one or more existing
 * byte arrays and byte buffers.  Any changes in the content of the original
 * array or buffer will be visible in the wrapped buffer.  Various wrapper
 * methods are provided and their name is all {@code wrappedBuffer()}.
 * You might want to take a look at the methods that accept varargs closely if
 * you want to create a buffer which is composed of more than one array to
 * reduce the number of memory copy.
 *
 * <h3>Creating a copied buffer</h3>
 *
 * Copied buffer is a deep copy of one or more existing byte arrays, byte
 * buffers or a string.  Unlike a wrapped buffer, there's no shared data
 * between the original data and the copied buffer.  Various copy methods are
 * provided and their name is all {@code copiedBuffer()}.  It is also convenient
 * to use this operation to merge multiple buffers into one buffer.
 */
// CLR: using static Netty.NET.Buffer.Unpooled provides the same factory import style.
public static partial class Unpooled
{
    /**
     * A buffer whose capacity is {@code 0}.
     */
    // EmptyByteBuf is not writeable or readable.
    public static ByteBuf EmptyBuffer { get; } = UnpooledByteBufAllocator.Default.HeapBuffer(0, 0);
    /**
     * Creates a new big-endian Java heap buffer with reasonably small initial capacity, which
     * expands its capacity boundlessly on demand.
     */
    /**
     * Creates a new big-endian Java heap buffer with the specified {@code capacity}, which
     * expands its capacity boundlessly on demand.  The new buffer's {@code readerIndex} and
     * {@code writerIndex} are {@code 0}.
     */
    /**
         * Creates a new big-endian Java heap buffer with the specified
         * {@code initialCapacity}, that may grow up to {@code maxCapacity}
         * The new buffer's {@code readerIndex} and {@code writerIndex} are
         * {@code 0}.
         */
    public static ByteBuf Buffer(int initialCapacity = 256, int maxCapacity = int.MaxValue)
        => UnpooledByteBufAllocator.Default.HeapBuffer(initialCapacity, maxCapacity);
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
        => bytes.IsEmpty ? EmptyBuffer : WrappedBuffer(bytes.ToArray());

    /**
     * Creates a new big-endian direct buffer with reasonably small initial capacity, which
     * expands its capacity boundlessly on demand.
     */
    /**
     * Creates a new big-endian direct buffer with the specified {@code capacity}, which
     * expands its capacity boundlessly on demand.  The new buffer's {@code readerIndex} and
     * {@code writerIndex} are {@code 0}.
     */
    /**
         * Creates a new big-endian direct buffer with the specified
         * {@code initialCapacity}, that may grow up to {@code maxCapacity}.
         * The new buffer's {@code readerIndex} and {@code writerIndex} are
         * {@code 0}.
         */
    public static ByteBuf DirectBuffer(int initialCapacity = 256, int maxCapacity = int.MaxValue)
        => UnpooledByteBufAllocator.Default.DirectBuffer(initialCapacity, maxCapacity);
    /**
     * Returns a new big-endian composite buffer with no components.
     */
    /**
     * Returns a new big-endian composite buffer with no components.
     */
    public static CompositeByteBuf CompositeBuffer(int maxNumComponents = 16) => UnpooledByteBufAllocator.Default.CompositeHeapBuffer(maxNumComponents);
}
