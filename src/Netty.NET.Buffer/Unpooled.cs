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
using System.Runtime.InteropServices;
using System.Text;
using Netty.NET.Common;

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
public static class Unpooled
{
    /// <summary>Returns the shared empty buffer when there are no inputs.</summary>
    // CLR: resolves the two params overloads for a zero-input call.
    public static ByteBuf CopiedBuffer()
    {
        return EmptyBuffer;
    }

    /**
     * Creates a new big-endian buffer whose content is a copy of the
     * specified {@code array}.  The new buffer's {@code readerIndex} and
     * {@code writerIndex} are {@code 0} and {@code array.length} respectively.
     */
    public static ByteBuf CopiedBuffer(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return CopiedBuffer(bytes.AsSpan());
    }

    /**
     * Creates a new big-endian buffer whose content is a copy of the
     * specified {@code array}'s sub-region.  The new buffer's
     * {@code readerIndex} and {@code writerIndex} are {@code 0} and
     * the specified {@code length} respectively.
     */
    /// <remarks>CLR null/range validation also applies to empty ranges.</remarks>
    public static ByteBuf CopiedBuffer(byte[] bytes, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        return CopiedBuffer(bytes.AsSpan(offset, length));
    }

    /**
     * Creates a new buffer whose content is a copy of the specified
     * {@code buffer}'s readable bytes.  The new buffer's {@code readerIndex}
     * and {@code writerIndex} are {@code 0} and {@code buffer.readableBytes}
     * respectively.
     */
    /// <remarks>Input ownership and indices are unchanged. Unlike array and
    /// multi-buffer copies, this single-buffer result can grow, matching Netty.</remarks>
    public static ByteBuf CopiedBuffer(ByteBuf buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        int readable = buffer.ReadableBytes;
        if (readable <= 0)
        {
            return EmptyBuffer;
        }
        ByteBuf copy = Buffer(readable);
        try
        {
            // CLR: absolute transfer preserves the source reader index.
            copy.SetBytes(0, buffer, buffer.ReaderIndex, readable);
            copy.WriterIndex = readable;
            return copy;
        }
        catch
        {
            // CLR: a failed factory cannot return its newly allocated owner.
            copy.Release();
            throw;
        }
    }

    /**
     * Creates a new big-endian buffer whose content is a merged copy of
     * the specified {@code arrays}.  The new buffer's {@code readerIndex}
     * and {@code writerIndex} are {@code 0} and the sum of all arrays'
     * {@code length} respectively.
     */
    public static ByteBuf CopiedBuffer(params byte[][] arrays)
    {
        ArgumentNullException.ThrowIfNull(arrays);
        if (arrays.Length == 1)
        {
            return CopiedBuffer(arrays[0]);
        }
        // Merge the specified arrays into one array.
        int length = 0;
        foreach (byte[] array in arrays)
        {
            ArgumentNullException.ThrowIfNull(array);
            if (array.Length > int.MaxValue - length)
                throw new ArgumentException("The total length of the specified arrays is too big.", nameof(arrays));
            length += array.Length;
        }
        if (length == 0)
        {
            return EmptyBuffer;
        }
        byte[] merged = new byte[length];
        int offset = 0;
        foreach (byte[] array in arrays)
        {
            array.CopyTo(merged.AsSpan(offset, array.Length));
            offset += array.Length;
        }
        return WrappedBuffer(merged);
    }

    /**
     * Creates a new buffer whose content is a merged copy of the specified
     * {@code buffers}' readable bytes.  The new buffer's {@code readerIndex}
     * and {@code writerIndex} are {@code 0} and the sum of all buffers'
     * {@code readableBytes} respectively.
     *
     * @throws IllegalArgumentException
     *         if the specified buffers' endianness are different from each
     *         other
     */
    /// <remarks>C# buffers use explicit BE/LE operations and have no mutable
    /// byte-order state to reconcile. Bytes are copied without interpretation;
    /// source references, permissions, indices and marks are unchanged.</remarks>
    public static ByteBuf CopiedBuffer(params ByteBuf[] buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        if (buffers.Length == 1)
        {
            return CopiedBuffer(buffers[0]);
        }
        // Merge the specified buffers into one buffer.
        int length = 0;
        foreach (ByteBuf buffer in buffers)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            int readable = buffer.ReadableBytes;
            if (readable <= 0)
            {
                continue;
            }
            if (readable > int.MaxValue - length)
                throw new ArgumentException("The total length of the specified buffers is too big.", nameof(buffers));
            length += readable;
        }
        if (length == 0)
        {
            return EmptyBuffer;
        }
        byte[] merged = new byte[length];
        int offset = 0;
        foreach (ByteBuf buffer in buffers)
        {
            int readable = buffer.ReadableBytes;
            // Keep zero-length reads: when there is readable input, the original
            // also checks accessibility/ranges on empty components during copying.
            buffer.GetBytes(buffer.ReaderIndex, merged.AsSpan(offset, readable));
            offset += readable;
        }
        // CLR: publish ownership only after every source read succeeds.
        return WrappedBuffer(merged);
    }

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
    {
        return UnpooledByteBufAllocator.Default.HeapBuffer(initialCapacity, maxCapacity);
    }

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
    {
        return bytes.IsEmpty ? EmptyBuffer : WrappedBuffer(bytes.ToArray());
    }

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
    {
        return UnpooledByteBufAllocator.Default.DirectBuffer(initialCapacity, maxCapacity);
    }

    /**
     * Returns a new big-endian composite buffer with no components.
     */
    /**
     * Returns a new big-endian composite buffer with no components.
     */
    // CLR: new CompositeByteBuf(ALLOC, false, maxNumComponents) as in the source: the buffer is not passed through the
    // allocator's compositeHeapBuffer, so it is not leak-tracked (the leak presence detector of testsuite-common
    // reported HttpDataTest's undisposed MemoryAttribute content while the call went through the allocator, 2026-11-27).
    public static CompositeByteBuf CompositeBuffer(int maxNumComponents = 16)
    {
        return new CompositeByteBuf(UnpooledByteBufAllocator.Default, false, maxNumComponents);
    }

    // Original deprecated alias documentation; both array policies use the CLR snapshot factory.
    /**
     * Wrap the given {@link ByteBuf}s in an unmodifiable {@link ByteBuf}. Be aware the returned {@link ByteBuf} will
     * not try to slice the given {@link ByteBuf}s to reduce GC-Pressure.
     *
     * @deprecated Use {@link #wrappedUnmodifiableBuffer(ByteBuf...)}.
     */
    /**
     * Wrap the given {@link ByteBuf}s in an unmodifiable {@link ByteBuf}. Be aware the returned {@link ByteBuf} will
     * not try to slice the given {@link ByteBuf}s to reduce GC-Pressure.
     *
     * The returned {@link ByteBuf} may wrap the provided array directly, and so should not be subsequently modified.
     */
    /// <summary>Wraps buffers without copying their content or acquiring references.</summary>
    /// <remarks>Multiple inputs transfer one existing reference each. Their captured readable lengths
    /// map from absolute index zero, as in Netty; pass Slice() for each input to wrap its readable range.
    /// Keep component storage and layout stable during use. This CLR implementation snapshots the input
    /// array and layout. A single input returns AsReadOnly(), preserving its indices and shared ownership.</remarks>
    public static ByteBuf WrappedUnmodifiableBuffer(params ByteBuf[] buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        return buffers.Length switch
        {
            0 => EmptyBuffer,
            1 => (buffers[0] ?? throw new ArgumentException("A component is null.", nameof(buffers))).AsReadOnly(),
            _ => new FixedCompositeByteBuf(buffers)
        };
    }

    /**
     * Creates a new buffer which wraps the specified NIO buffer's current
     * slice.  A modification on the specified buffer's content will be
     * visible to the returned buffer.
     */
    /// <summary>Wraps a writable memory range without copying or acquiring its owner's lifetime.</summary>
    /// <remarks>Keep the external storage valid until buffer access ends. Array ranges
    /// reuse array wrapping. NativeMemoryOwner ranges may detach on capacity change;
    /// other managers have fixed capacity and heap copy policy, since Memory cannot
    /// identify their storage kind. Use explicit LE operations for little-endian data.</remarks>
    public static ByteBuf WrappedBuffer(Memory<byte> memory)
    {
        if (memory.IsEmpty)
        {
            return EmptyBuffer;
        }
        if (MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)memory, out ArraySegment<byte> array))
            return WrappedBuffer(array.Array, array.Offset, array.Count);
        if (NativeMemoryOwner.TryGetOwner(memory, out _))
            return new UnpooledDirectByteBuf(UnpooledByteBufAllocator.Default, memory, memory.Length);
        return new WrappedMemoryByteBuf(memory);
    }

    // CLR: non-array managers have no Java provider equivalent or reliable native-kind
    // metadata. A fixed borrowed range preserves writable Memory access without guessing.
    private sealed class WrappedMemoryByteBuf : AbstractReferenceCountedByteBuf
    {
        private Memory<byte> _memory;
        internal WrappedMemoryByteBuf(Memory<byte> memory) : base(memory.Length)
        {
            _memory = memory;
            SetIndex(0, memory.Length);
        }

        public override IByteBufAllocator Alloc()
        {
            return UnpooledByteBufAllocator.Default;
        }
        public override bool IsContiguous => true;
        public override bool IsDirect => false;
        public override int Capacity
        {
            get => MaxCapacity;
            set
            {
                CheckNewCapacity(value);
                throw new NotSupportedException("A borrowed manager range has fixed capacity.");
            }
        }
        protected override Memory<byte> GetMemoryCore(int index, int length)
        {
            return _memory.Slice(index, length);
        }

        internal override BufferMemoryLease AcquireReadLease()
        {
            EnsureAccessible();
            return BufferMemoryLease.ForBorrowedMemory(_memory);
        }

        protected override void Deallocate()
        {
            _memory = default;
        }
    }

    /**
     * Creates a new buffer whose content is a copy of the specified
     * {@code buffer}'s current slice.  The new buffer's {@code readerIndex}
     * and {@code writerIndex} are {@code 0} and {@code buffer.remaining}
     * respectively.
     */
    /// <remarks>Memory slicing selects the source range. The writable heap result
    /// has a maximum equal to its length and does not borrow the source owner.</remarks>
    public static ByteBuf CopiedBuffer(ReadOnlyMemory<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return EmptyBuffer;
        }
        // Duplicate the buffer so we not adjust the position during our get operation.
        // See https://github.com/netty/netty/issues/3896
        // CLR: ReadOnlyMemory has immutable range metadata and no mutable cursor.
        return CopiedBuffer(buffer.Span);
    }

    /**
     * Creates a new buffer whose content is a merged copy of the specified
     * {@code buffers}' slices.  The new buffer's {@code readerIndex}
     * and {@code writerIndex} are {@code 0} and the sum of all buffers'
     * {@code remaining} respectively.
     *
     * @throws IllegalArgumentException
     *         if the specified buffers' endianness are different from each
     *         other
     */
    /// <remarks>ReadOnlyMemory has no byte-order metadata. Bytes are copied unchanged;
    /// use explicit BE/LE operations on the result. Empty ranges are not accessed.
    /// Input owners must remain valid during the call and are never disposed.</remarks>
    // Original varargs overload documentation; CLR uses immutable range descriptors.
    /**
     * Creates a new buffer whose content is a merged copy of the specified
     * {@code buffers}' slices.  The new buffer's {@code readerIndex} and
     * {@code writerIndex} are {@code 0} and the sum of all buffers'
     * {@code remaining} respectively.
     *
     * @throws IllegalArgumentException
     *         if the specified buffers' endianness are different from each
     *         other
     */
    public static ByteBuf CopiedBuffer(params ReadOnlyMemory<byte>[] buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        if (buffers.Length == 1)
        {
            return CopiedBuffer(buffers[0]);
        }
        // Merge the specified buffers into one buffer.
        int length = 0;
        foreach (ReadOnlyMemory<byte> buffer in buffers)
        {
            if (buffer.Length > int.MaxValue - length)
                throw new ArgumentException("The total length of the specified buffers is too big.", nameof(buffers));
            length += buffer.Length;
        }
        if (length == 0)
        {
            return EmptyBuffer;
        }

        byte[] merged = new byte[length];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> buffer in buffers)
        {
            if (buffer.IsEmpty)
            {
                continue;
            }
            // Duplicate the buffer so we not adjust the position during our get operation.
            // See https://github.com/netty/netty/issues/3896
            // CLR: no cursor or source owner is changed, including when Span access fails.
            buffer.Span.CopyTo(merged.AsSpan(offset, buffer.Length));
            offset += buffer.Length;
        }
        return WrappedBuffer(merged);
    }

    /**
     * Creates a new big-endian composite buffer which wraps the slices of the specified
     * NIO buffers without copying them.  A modification on the content of the
     * specified buffers will be visible to the returned buffer.
     */
    public static ByteBuf WrappedBuffer(params Memory<byte>[] buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        return WrappedBuffer(buffers.Length, buffers);
    }

    /**
     * Creates a new big-endian composite buffer which wraps the slices of the specified
     * NIO buffers without copying them.  A modification on the content of the
     * specified buffers will be visible to the returned buffer.
     */
    public static ByteBuf WrappedBuffer(params ReadOnlyMemory<byte>[] buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        return WrappedBuffer(buffers.Length, buffers);
    }

    /**
     * Creates a new big-endian composite buffer which wraps the slices of the specified
     * NIO buffers without copying them.  A modification on the content of the
     * specified buffers will be visible to the returned buffer.
     */
    /// <remarks>External owners are borrowed and must remain valid during access.
    /// Empty ranges are skipped; multiple supplied ranges produce a composite even
    /// when only one is nonempty. Consolidation runs once after all additions and
    /// copies into owned heap storage if the component limit is exceeded.</remarks>
    public static ByteBuf WrappedBuffer(int maxNumComponents, params Memory<byte>[] buffers)
    {
        return WrapMemoryBuffers(maxNumComponents, buffers, static memory => memory.IsEmpty, static memory => WrappedBuffer(memory));
    }

    /**
     * Creates a new big-endian composite buffer which wraps the slices of the specified
     * NIO buffers without copying them.  A modification on the content of the
     * specified buffers will be visible to the returned buffer.
     */
    /// <remarks>Read-only component access rejects writes before consolidation.
    /// The composite itself follows Netty's ordinary writable composite policy;
    /// consolidation creates independent writable storage. Use ByteBuf[] wrapping
    /// to mix writable and read-only ranges explicitly. Default memory is empty;
    /// there are no nullable elements or mutable byte-order cursors.</remarks>
    public static ByteBuf WrappedBuffer(int maxNumComponents, params ReadOnlyMemory<byte>[] buffers)
    {
        return WrapMemoryBuffers(maxNumComponents, buffers, static memory => memory.IsEmpty, static memory => WrappedBuffer(memory));
    }

    private static ByteBuf WrapMemoryBuffers<T>(int maxNumComponents, T[] buffers,
        Func<T, bool> isEmpty, Func<T, ByteBuf> wrap) where T : struct
    {
        ArgumentNullException.ThrowIfNull(buffers);
        if (buffers.Length == 1)
        {
            return wrap(buffers[0]);
        }
        for (int i = 0; i < buffers.Length; ++i)
        {
            if (isEmpty(buffers[i]))
            {
                continue;
            }
            var composite = new CompositeByteBuf(maxNumComponents);
            try
            {
                return composite.AddWrappedMemory(buffers, i, isEmpty, wrap);
            }
            catch
            {
                // CLR: only temporary wrapper references were acquired. External
                // owners remain caller-owned even when insertion/consolidation fails.
                ReferenceCountUtil.SafeRelease(composite);
                throw;
            }
        }
        return EmptyBuffer;
    }

    /**
     * Creates a new buffer which wraps the specified memory address. If {@code doFree} is true the
     * memoryAddress will automatically be freed once the reference count of the {@link ByteBuf} reaches {@code 0}.
     */
    /// <remarks>Use the overload that accepts bounded Memory&lt;byte&gt;. This overload
    /// always throws before accessing the address or taking ownership.</remarks>
    [Obsolete("Wrapping raw addresses is unsupported. Use WrappedBuffer(Memory<byte>).")]
    public static ByteBuf WrappedBuffer(nint memoryAddress, int size, bool doFree)
    {
        throw new PlatformNotSupportedException("Raw address buffers are unavailable on the managed allocator.");
    }

    // CLR: disambiguates the two params CopyShort overloads for zero-input calls.
    public static ByteBuf CopyShort()
    {
        return EmptyBuffer;
    }

    /**
     * Creates a new 4-byte big-endian buffer that holds the specified 32-bit integer.
     */
    public static ByteBuf CopyInt(int value)
    {
        return Buffer(4).WriteInt(value);
    }

    /**
     * Create a big-endian buffer that holds a sequence of the specified 32-bit integers.
     */
    /// <remarks>Null and empty arrays return the shared empty buffer, as in Netty.</remarks>
    public static ByteBuf CopyInt(params int[] values)
    {
        return CopyInt(values.AsSpan());
    }

    /// <summary>Copies values in big-endian order into independent writable heap storage.</summary>
    /// <remarks>An empty span returns the shared empty buffer. The byte count is checked
    /// before allocation; a nonempty result can grow up to int.MaxValue.</remarks>
    public static ByteBuf CopyInt(ReadOnlySpan<int> values)
    {
        if (values.IsEmpty)
        {
            return EmptyBuffer;
        }
        ByteBuf buffer = Buffer(checked(values.Length * 4));

        foreach (int value in values)
        {
            buffer.WriteInt(value);
        }

        return buffer;
    }

    /**
     * Creates a new 2-byte big-endian buffer that holds the specified 16-bit integer.
     */
    public static ByteBuf CopyShort(int value)
    {
        return Buffer(2).WriteShort(value);
    }

    /**
     * Create a new big-endian buffer that holds a sequence of the specified 16-bit integers.
     */
    /// <remarks>Null and empty arrays return the shared empty buffer, as in Netty.</remarks>
    public static ByteBuf CopyShort(params short[] values)
    {
        return CopyShort(values.AsSpan());
    }

    /// <summary>Copies values in big-endian order into independent writable heap storage.</summary>
    /// <remarks>An empty span returns the shared empty buffer. The byte count is checked
    /// before allocation; a nonempty result can grow up to int.MaxValue.</remarks>
    public static ByteBuf CopyShort(ReadOnlySpan<short> values)
    {
        if (values.IsEmpty)
        {
            return EmptyBuffer;
        }
        ByteBuf buffer = Buffer(checked(values.Length * 2));

        foreach (short value in values)
        {
            buffer.WriteShort(value);
        }

        return buffer;
    }

    /**
     * Create a new big-endian buffer that holds a sequence of the specified 16-bit integers.
     */
    /// <remarks>Null and empty arrays return the shared empty buffer, as in Netty.</remarks>
    public static ByteBuf CopyShort(params int[] values)
    {
        return CopyShort(values.AsSpan());
    }

    /// <summary>Copies values in big-endian order into independent writable heap storage.</summary>
    /// <remarks>An empty span returns the shared empty buffer. The byte count is checked
    /// before allocation; a nonempty result can grow up to int.MaxValue.</remarks>
    public static ByteBuf CopyShort(ReadOnlySpan<int> values)
    {
        if (values.IsEmpty)
        {
            return EmptyBuffer;
        }
        ByteBuf buffer = Buffer(checked(values.Length * 2));

        foreach (int value in values)
        {
            buffer.WriteShort(value);
        }

        return buffer;
    }

    /**
     * Creates a new 3-byte big-endian buffer that holds the specified 24-bit integer.
     */
    public static ByteBuf CopyMedium(int value)
    {
        return Buffer(3).WriteMedium(value);
    }

    /**
     * Create a new big-endian buffer that holds a sequence of the specified 24-bit integers.
     */
    /// <remarks>Null and empty arrays return the shared empty buffer, as in Netty.</remarks>
    public static ByteBuf CopyMedium(params int[] values)
    {
        return CopyMedium(values.AsSpan());
    }

    /// <summary>Copies values in big-endian order into independent writable heap storage.</summary>
    /// <remarks>An empty span returns the shared empty buffer. The byte count is checked
    /// before allocation; a nonempty result can grow up to int.MaxValue.</remarks>
    public static ByteBuf CopyMedium(ReadOnlySpan<int> values)
    {
        if (values.IsEmpty)
        {
            return EmptyBuffer;
        }
        ByteBuf buffer = Buffer(checked(values.Length * 3));

        foreach (int value in values)
        {
            buffer.WriteMedium(value);
        }

        return buffer;
    }

    /**
     * Creates a new 8-byte big-endian buffer that holds the specified 64-bit integer.
     */
    public static ByteBuf CopyLong(long value)
    {
        return Buffer(8).WriteLong(value);
    }

    /**
     * Create a new big-endian buffer that holds a sequence of the specified 64-bit integers.
     */
    /// <remarks>Null and empty arrays return the shared empty buffer, as in Netty.</remarks>
    public static ByteBuf CopyLong(params long[] values)
    {
        return CopyLong(values.AsSpan());
    }

    /// <summary>Copies values in big-endian order into independent writable heap storage.</summary>
    /// <remarks>An empty span returns the shared empty buffer. The byte count is checked
    /// before allocation; a nonempty result can grow up to int.MaxValue.</remarks>
    public static ByteBuf CopyLong(ReadOnlySpan<long> values)
    {
        if (values.IsEmpty)
        {
            return EmptyBuffer;
        }
        ByteBuf buffer = Buffer(checked(values.Length * 8));

        foreach (long value in values)
        {
            buffer.WriteLong(value);
        }

        return buffer;
    }

    /**
     * Creates a new single-byte big-endian buffer that holds the specified boolean value.
     */
    public static ByteBuf CopyBoolean(bool value)
    {
        return Buffer(1).WriteBoolean(value);
    }

    /**
     * Create a new big-endian buffer that holds a sequence of the specified boolean values.
     */
    /// <remarks>Null and empty arrays return the shared empty buffer, as in Netty.</remarks>
    public static ByteBuf CopyBoolean(params bool[] values)
    {
        return CopyBoolean(values.AsSpan());
    }

    /// <summary>Copies values in big-endian order into independent writable heap storage.</summary>
    /// <remarks>An empty span returns the shared empty buffer. The byte count is checked
    /// before allocation; a nonempty result can grow up to int.MaxValue.</remarks>
    public static ByteBuf CopyBoolean(ReadOnlySpan<bool> values)
    {
        if (values.IsEmpty)
        {
            return EmptyBuffer;
        }
        ByteBuf buffer = Buffer(checked(values.Length * 1));

        foreach (bool value in values)
        {
            buffer.WriteBoolean(value);
        }

        return buffer;
    }

    /**
     * Creates a new 4-byte big-endian buffer that holds the specified 32-bit floating point number.
     */
    public static ByteBuf CopyFloat(float value)
    {
        return Buffer(4).WriteFloat(value);
    }

    /**
     * Create a new big-endian buffer that holds a sequence of the specified 32-bit floating point numbers.
     */
    /// <remarks>Null and empty arrays return the shared empty buffer, as in Netty.</remarks>
    public static ByteBuf CopyFloat(params float[] values)
    {
        return CopyFloat(values.AsSpan());
    }

    /// <summary>Copies values in big-endian order into independent writable heap storage.</summary>
    /// <remarks>An empty span returns the shared empty buffer. The byte count is checked
    /// before allocation; a nonempty result can grow up to int.MaxValue.</remarks>
    public static ByteBuf CopyFloat(ReadOnlySpan<float> values)
    {
        if (values.IsEmpty)
        {
            return EmptyBuffer;
        }
        ByteBuf buffer = Buffer(checked(values.Length * 4));

        foreach (float value in values)
        {
            buffer.WriteFloat(value);
        }

        return buffer;
    }

    /**
     * Creates a new 8-byte big-endian buffer that holds the specified 64-bit floating point number.
     */
    public static ByteBuf CopyDouble(double value)
    {
        return Buffer(8).WriteDouble(value);
    }

    /**
     * Create a new big-endian buffer that holds a sequence of the specified 64-bit floating point numbers.
     */
    /// <remarks>Null and empty arrays return the shared empty buffer, as in Netty.</remarks>
    public static ByteBuf CopyDouble(params double[] values)
    {
        return CopyDouble(values.AsSpan());
    }

    /// <summary>Copies values in big-endian order into independent writable heap storage.</summary>
    /// <remarks>An empty span returns the shared empty buffer. The byte count is checked
    /// before allocation; a nonempty result can grow up to int.MaxValue.</remarks>
    public static ByteBuf CopyDouble(ReadOnlySpan<double> values)
    {
        if (values.IsEmpty)
        {
            return EmptyBuffer;
        }
        ByteBuf buffer = Buffer(checked(values.Length * 8));

        foreach (double value in values)
        {
            buffer.WriteDouble(value);
        }

        return buffer;
    }

    /**
     * Creates a new buffer which wraps the specified NIO buffer's current
     * slice.  A modification on the specified buffer's content will be
     * visible to the returned buffer.
     */
    /// <summary>Wraps an external read-only range without copying or taking ownership of its owner.</summary>
    /// <remarks>Slice the ReadOnlyMemory before calling to select the remaining range.
    /// The source owner must stay valid during access. Empty memory uses the shared sentinel.</remarks>
    public static ByteBuf WrappedBuffer(ReadOnlyMemory<byte> memory)
    {
        return memory.IsEmpty ? EmptyBuffer : new ReadOnlyMemoryByteBuf(UnpooledByteBufAllocator.Default, memory);
    }

    /**
     * Creates a new big-endian buffer whose content is the specified
     * {@code string} encoded in the specified {@code charset}.
     * The new buffer's {@code readerIndex} and {@code writerIndex} are
     * {@code 0} and the length of the encoded string respectively.
     */
    /// <remarks>The supplied Encoding controls malformed/unmappable input, just as
    /// for ByteBuf.WriteString. No preamble is emitted. Unlike Java's whole-string
    /// UTF-8/ASCII shortcuts, this honors custom fallback policies. Netty's specialized
    /// byte mappings are available through ByteBufUtil.WriteUtf8/WriteAscii.
    /// Capacity is the exact encoded length rather than a CharsetEncoder upper bound;
    /// the result can grow up to int.MaxValue. An empty whole string has its own owner.</remarks>
    public static ByteBuf CopiedBuffer(string text, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(encoding);
        return CopyEncodedText(text.AsSpan(), encoding);
    }

    /**
     * Creates a new big-endian buffer whose content is a subregion of
     * the specified {@code string} encoded in the specified {@code charset}.
     * The new buffer's {@code readerIndex} and {@code writerIndex} are
     * {@code 0} and the length of the encoded string respectively.
     */
    /// <remarks>Offset and length count UTF-16 code units, not Unicode scalars.
    /// CLR null/range validation also applies to empty ranges. The supplied Encoding
    /// handles a surrogate pair split by the range. No preamble is emitted.</remarks>
    public static ByteBuf CopiedBuffer(string text, int offset, int length, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(encoding);
        if (offset < 0 || length < 0 || offset > text.Length - length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        return CopiedBuffer(text.AsSpan(offset, length), encoding);
    }

    /**
     * Creates a new big-endian buffer whose content is the specified
     * {@code array} encoded in the specified {@code charset}.
     * The new buffer's {@code readerIndex} and {@code writerIndex} are
     * {@code 0} and the length of the encoded string respectively.
     */
    /// <remarks>The supplied Encoding and its fallback policy are used without a preamble.</remarks>
    public static ByteBuf CopiedBuffer(char[] chars, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(chars);
        return CopiedBuffer(chars.AsSpan(), encoding);
    }

    /**
     * Creates a new big-endian buffer whose content is a subregion of
     * the specified {@code array} encoded in the specified {@code charset}.
     * The new buffer's {@code readerIndex} and {@code writerIndex} are
     * {@code 0} and the length of the encoded string respectively.
     */
    /// <remarks>Offset and length count UTF-16 code units. CLR null/range validation
    /// also applies to empty ranges; the supplied Encoding determines fallback.</remarks>
    public static ByteBuf CopiedBuffer(char[] chars, int offset, int length, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(chars);
        ArgumentNullException.ThrowIfNull(encoding);
        if (offset < 0 || length < 0 || offset > chars.Length - length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        return CopiedBuffer(chars.AsSpan(offset, length), encoding);
    }

    /// <summary>Copies encoded UTF-16 input into independent writable heap storage.
    /// An empty span returns the shared empty buffer.</summary>
    /// <remarks>Span replaces Java CharBuffer ranges. The supplied Encoding controls
    /// fallback; no preamble is emitted. Capacity is the exact encoded length and
    /// the result can grow up to int.MaxValue. Keep the input stable during copying.</remarks>
    public static ByteBuf CopiedBuffer(ReadOnlySpan<char> text, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        return text.IsEmpty ? EmptyBuffer : CopyEncodedText(text, encoding);
    }

    /// <summary>Copies the logical AsciiString range into independent heap storage.</summary>
    /// <remarks>Like Netty's whole-CharSequence factory, UTF-8 and ASCII preserve raw
    /// AsciiString octets, including values above 127, without character fallback.
    /// Other encodings widen each unsigned octet to a character and honor Encoding.
    /// Even an empty whole value has an independently releasable, growable owner.</remarks>
    public static ByteBuf CopiedBuffer(AsciiString text, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(encoding);
        if (encoding.CodePage is not (65001 or 20127))
            return CopyEncodedText(text.ToString(0, text.Length()).AsSpan(), encoding);

        // Mimic the same behavior as other copiedBuffer implementations.
        ByteBuf copy = Buffer(text.Length());
        try
        {
            return copy.WriteBytes(text.AsSpan());
        }
        catch
        {
            copy.Release();
            throw;
        }
    }

    /// <summary>Encodes a logical AsciiString subregion as unsigned-octet characters.</summary>
    /// <remarks>Netty's ranged factory uses character encoding, even for UTF-8/ASCII;
    /// it does not take the whole-value raw-octet shortcuts. Empty ranges return
    /// the shared sentinel. CLR validates null/range arguments for empty input too.
    /// Read the live backing range rather than AsciiString's cached whole string.</remarks>
    public static ByteBuf CopiedBuffer(AsciiString text, int offset, int length, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(encoding);
        if (offset < 0 || length < 0 || offset > text.Length() - length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        return CopiedBuffer(text.ToString(offset, offset + length).AsSpan(), encoding);
    }

    private static ByteBuf CopyEncodedText(ReadOnlySpan<char> text, Encoding encoding)
    {
        // Mimic the same behavior as other copiedBuffer implementations.
        // CLR: size using the caller's Encoding, then encode directly into the new owner.
        int length = encoding.GetByteCount(text);
        ByteBuf copy = Buffer(length);
        try
        {
            copy.WriterIndex = encoding.GetBytes(text, copy.AsSpan(0, length));
            return copy;
        }
        catch
        {
            // A factory failure cannot return its newly allocated reference.
            copy.Release();
            throw;
        }
    }

    /**
     * Return a unreleasable view on the given {@link ByteBuf} which will just ignore release and retain calls.
     */
    /// <remarks>Borrowed facade: no reference is acquired. The original owner must
    /// remain alive and is responsible for the final release.</remarks>
    public static ByteBuf UnreleasableBuffer(ByteBuf buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return new UnreleasableByteBuf(buffer);
    }

    /// <summary>Returns the shared empty buffer.</summary>
    // CLR: resolves the otherwise ambiguous two params overloads for an empty call.
    public static ByteBuf WrappedBuffer()
    {
        return EmptyBuffer;
    }

    /// <summary>Returns the shared empty buffer when no inputs are supplied.</summary>
    public static ByteBuf WrappedBuffer(int maxNumComponents)
    {
        return EmptyBuffer;
    }

    /**
     * Creates a new big-endian buffer which wraps the sub-region of the
     * specified {@code array}.  A modification on the specified array's
     * content will be visible to the returned buffer.
     */
    /// <remarks>CLR argument validation also applies to empty ranges.</remarks>
    public static ByteBuf WrappedBuffer(byte[] bytes, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (length == 0)
        {
            return EmptyBuffer;
        }
        return offset == 0 && length == bytes.Length ? WrappedBuffer(bytes) : WrappedBuffer(bytes).Slice(offset, length);
    }

    /**
     * Creates a new buffer which wraps the specified buffer's readable bytes.
     * A modification on the specified buffer's content will be visible to the
     * returned buffer.
     * @param buffer The buffer to wrap. Reference count ownership of this variable is transferred to this method.
     * @return The readable portion of the {@code buffer}, or an empty buffer if there is no readable portion.
     * The caller is responsible for releasing this buffer.
     */
    public static ByteBuf WrappedBuffer(ByteBuf buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.IsReadable())
        {
            return buffer.Slice();
        }
        buffer.Release();
        return EmptyBuffer;
    }

    /**
     * Creates a new big-endian composite buffer which wraps the specified
     * arrays without copying them.  A modification on the specified arrays'
     * content will be visible to the returned buffer.
     */
    public static ByteBuf WrappedBuffer(params byte[][] arrays)
    {
        ArgumentNullException.ThrowIfNull(arrays);
        return WrappedBuffer(arrays.Length, arrays);
    }

    /**
     * Creates a new big-endian composite buffer which wraps the readable bytes of the
     * specified buffers without copying them.  A modification on the content
     * of the specified buffers will be visible to the returned buffer.
     * @param buffers The buffers to wrap. Reference count ownership of all variables is transferred to this method.
     * @return The readable portion of the {@code buffers}. The caller is responsible for releasing this buffer.
     */
    public static ByteBuf WrappedBuffer(params ByteBuf[] buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        return WrappedBuffer(buffers.Length, buffers);
    }

    /**
     * Creates a new big-endian composite buffer which wraps the specified
     * arrays without copying them.  A modification on the specified arrays'
     * content will be visible to the returned buffer.
     */
    /// <remarks>Empty arrays are skipped; null terminates the input. If more
    /// nonempty components than the limit are present, consolidation copies them.</remarks>
    public static ByteBuf WrappedBuffer(int maxNumComponents, params byte[][] arrays)
    {
        ArgumentNullException.ThrowIfNull(arrays);
        if (arrays.Length == 1)
        {
            ArgumentNullException.ThrowIfNull(arrays[0]);
            return WrappedBuffer(arrays[0]);
        }
        for (int i = 0; i < arrays.Length; ++i)
        {
            byte[] array = arrays[i];
            if (array == null)
            {
                return EmptyBuffer;
            }
            if (array.Length == 0)
            {
                continue;
            }
            var composite = new CompositeByteBuf(maxNumComponents);
            try
            {
                return composite.AddWrappedArrays(arrays, i);
            }
            catch
            {
                // CLR: a failing factory cannot return its partially built owner.
                // Release its acquired components without masking the original failure.
                ReferenceCountUtil.SafeRelease(composite);
                throw;
            }
        }
        return EmptyBuffer;
    }

    /**
     * Creates a new big-endian composite buffer which wraps the readable bytes of the
     * specified buffers without copying them.  A modification on the content
     * of the specified buffers will be visible to the returned buffer.
     * @param maxNumComponents Advisement as to how many independent buffers are allowed to exist before
     * consolidation occurs.
     * @param buffers The buffers to wrap. Reference count ownership of all variables is transferred to this method.
     * @return The readable portion of the {@code buffers}. The caller is responsible for releasing this buffer.
     */
    /// <remarks>Leading unreadable buffers are released immediately. Once a
    /// readable input is found, array insertion owns subsequent empty components
    /// and safely releases a null-terminated tail. Preflight failure leaves the
    /// untransferred suffix caller-owned. Later failure releases the inaccessible
    /// partial composite, including its successfully transferred prefix.</remarks>
    public static ByteBuf WrappedBuffer(int maxNumComponents, params ByteBuf[] buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        if (buffers.Length == 1)
        {
            return WrappedBuffer(buffers[0]);
        }
        for (int i = 0; i < buffers.Length; ++i)
        {
            ByteBuf buffer = buffers[i];
            ArgumentNullException.ThrowIfNull(buffer);
            if (!buffer.IsReadable())
            {
                buffer.Release();
                continue;
            }
            var composite = new CompositeByteBuf(maxNumComponents);
            try
            {
                return composite.AddWrappedComponents(buffers, i);
            }
            catch
            {
                // CLR: release only transferred ownership. Array preflight may
                // have failed before consuming any inputs from this suffix.
                ReferenceCountUtil.SafeRelease(composite);
                throw;
            }
        }
        return EmptyBuffer;
    }
}
