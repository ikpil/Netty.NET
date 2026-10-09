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
 * A random and sequential accessible sequence of zero or more bytes (octets).
 * This interface provides an abstract view for one or more primitive byte
 * arrays ({@code byte[]}) and {@linkplain ByteBuffer NIO buffers}.
 *
 * <h3>Creation of a buffer</h3>
 *
 * It is recommended to create a new buffer using the helper methods in
 * {@link Unpooled} rather than calling an individual implementation's
 * constructor.
 *
 * <h3>Random Access Indexing</h3>
 *
 * Just like an ordinary primitive byte array, {@link ByteBuf} uses
 * <a href="https://en.wikipedia.org/wiki/Zero-based_numbering">zero-based indexing</a>.
 * It means the index of the first byte is always {@code 0} and the index of the last byte is
 * always {@link #capacity() capacity - 1}.  For example, to iterate all bytes of a buffer, you
 * can do the following, regardless of its internal implementation:
 *
 * <pre>
 * {@link ByteBuf} buffer = ...;
 * for (int i = 0; i &lt; buffer.capacity(); i ++) {
 *     byte b = buffer.getByte(i);
 *     System.out.println((char) b);
 * }
 * </pre>
 *
 * <h3>Sequential Access Indexing</h3>
 *
 * {@link ByteBuf} provides two pointer variables to support sequential
 * read and write operations - {@link #readerIndex() readerIndex} for a read
 * operation and {@link #writerIndex() writerIndex} for a write operation
 * respectively.  The following diagram shows how a buffer is segmented into
 * three areas by the two pointers:
 *
 * <pre>
 *      +-------------------+------------------+------------------+
 *      | discardable bytes |  readable bytes  |  writable bytes  |
 *      |                   |     (CONTENT)    |                  |
 *      +-------------------+------------------+------------------+
 *      |                   |                  |                  |
 *      0      <=      readerIndex   <=   writerIndex    <=    capacity
 * </pre>
 *
 * <h4>Readable bytes (the actual content)</h4>
 *
 * This segment is where the actual data is stored.  Any operation whose name
 * starts with {@code read} or {@code skip} will get or skip the data at the
 * current {@link #readerIndex() readerIndex} and increase it by the number of
 * read bytes.  If the argument of the read operation is also a
 * {@link ByteBuf} and no destination index is specified, the specified
 * buffer's {@link #writerIndex() writerIndex} is increased together.
 * <p>
 * If there's not enough content left, {@link IndexOutOfBoundsException} is
 * raised.  The default value of newly allocated, wrapped or copied buffer's
 * {@link #readerIndex() readerIndex} is {@code 0}.
 *
 * <pre>
 * // Iterates the readable bytes of a buffer.
 * {@link ByteBuf} buffer = ...;
 * while (buffer.isReadable()) {
 *     System.out.println(buffer.readByte());
 * }
 * </pre>
 *
 * <h4>Writable bytes</h4>
 *
 * This segment is an undefined space which needs to be filled.  Any operation
 * whose name starts with {@code write} will write the data at the current
 * {@link #writerIndex() writerIndex} and increase it by the number of written
 * bytes.  If the argument of the write operation is also a {@link ByteBuf},
 * and no source index is specified, the specified buffer's
 * {@link #readerIndex() readerIndex} is increased together.
 * <p>
 * If there's not enough writable bytes left, {@link IndexOutOfBoundsException}
 * is raised.  The default value of newly allocated buffer's
 * {@link #writerIndex() writerIndex} is {@code 0}.  The default value of
 * wrapped or copied buffer's {@link #writerIndex() writerIndex} is the
 * {@link #capacity() capacity} of the buffer.
 *
 * <pre>
 * // Fills the writable bytes of a buffer with random integers.
 * {@link ByteBuf} buffer = ...;
 * while (buffer.maxWritableBytes() >= 4) {
 *     buffer.writeInt(random.nextInt());
 * }
 * </pre>
 *
 * <h4>Discardable bytes</h4>
 *
 * This segment contains the bytes which were read already by a read operation.
 * Initially, the size of this segment is {@code 0}, but its size increases up
 * to the {@link #writerIndex() writerIndex} as read operations are executed.
 * The read bytes can be discarded by calling {@link #discardReadBytes()} to
 * reclaim unused area as depicted by the following diagram:
 *
 * <pre>
 *  BEFORE discardReadBytes()
 *
 *      +-------------------+------------------+------------------+
 *      | discardable bytes |  readable bytes  |  writable bytes  |
 *      +-------------------+------------------+------------------+
 *      |                   |                  |                  |
 *      0      <=      readerIndex   <=   writerIndex    <=    capacity
 *
 *
 *  AFTER discardReadBytes()
 *
 *      +------------------+--------------------------------------+
 *      |  readable bytes  |    writable bytes (got more space)   |
 *      +------------------+--------------------------------------+
 *      |                  |                                      |
 * readerIndex (0) <= writerIndex (decreased)        <=        capacity
 * </pre>
 *
 * Please note that there is no guarantee about the content of writable bytes
 * after calling {@link #discardReadBytes()}.  The writable bytes will not be
 * moved in most cases and could even be filled with completely different data
 * depending on the underlying buffer implementation.
 *
 * <h4>Clearing the buffer indexes</h4>
 *
 * You can set both {@link #readerIndex() readerIndex} and
 * {@link #writerIndex() writerIndex} to {@code 0} by calling {@link #clear()}.
 * It does not clear the buffer content (e.g. filling with {@code 0}) but just
 * clears the two pointers.  Please also note that the semantic of this
 * operation is different from {@link ByteBuffer#clear()}.
 *
 * <pre>
 *  BEFORE clear()
 *
 *      +-------------------+------------------+------------------+
 *      | discardable bytes |  readable bytes  |  writable bytes  |
 *      +-------------------+------------------+------------------+
 *      |                   |                  |                  |
 *      0      <=      readerIndex   <=   writerIndex    <=    capacity
 *
 *
 *  AFTER clear()
 *
 *      +---------------------------------------------------------+
 *      |             writable bytes (got more space)             |
 *      +---------------------------------------------------------+
 *      |                                                         |
 *      0 = readerIndex = writerIndex            <=            capacity
 * </pre>
 *
 * <h3>Search operations</h3>
 *
 * For simple single-byte searches, use {@link #indexOf(int, int, byte)} and {@link #bytesBefore(int, int, byte)}.
 * {@link #bytesBefore(byte)} is especially useful when you deal with a {@code NUL}-terminated string.
 * For complicated searches, use {@link #forEachByte(int, int, ByteProcessor)} with a {@link ByteProcessor}
 * implementation.
 *
 * <h3>Mark and reset</h3>
 *
 * There are two marker indexes in every buffer. One is for storing
 * {@link #readerIndex() readerIndex} and the other is for storing
 * {@link #writerIndex() writerIndex}.  You can always reposition one of the
 * two indexes by calling a reset method.  It works in a similar fashion to
 * the mark and reset methods in {@link InputStream} except that there's no
 * {@code readlimit}.
 *
 * <h3>Derived buffers</h3>
 *
 * You can create a view of an existing buffer by calling one of the following methods:
 * <ul>
 *   <li>{@link #duplicate()}</li>
 *   <li>{@link #slice()}</li>
 *   <li>{@link #slice(int, int)}</li>
 *   <li>{@link #readSlice(int)}</li>
 *   <li>{@link #retainedDuplicate()}</li>
 *   <li>{@link #retainedSlice()}</li>
 *   <li>{@link #retainedSlice(int, int)}</li>
 *   <li>{@link #readRetainedSlice(int)}</li>
 * </ul>
 * A derived buffer will have an independent {@link #readerIndex() readerIndex},
 * {@link #writerIndex() writerIndex} and marker indexes, while it shares
 * other internal data representation, just like a NIO buffer does.
 * <p>
 * In case a completely fresh copy of an existing buffer is required, please
 * call {@link #copy()} method instead.
 *
 * <h4>Non-retained and retained derived buffers</h4>
 *
 * Note that the {@link #duplicate()}, {@link #slice()}, {@link #slice(int, int)} and {@link #readSlice(int)} does NOT
 * call {@link #retain()} on the returned derived buffer, and thus its reference count will NOT be increased. If you
 * need to create a derived buffer with increased reference count, consider using {@link #retainedDuplicate()},
 * {@link #retainedSlice()}, {@link #retainedSlice(int, int)} and {@link #readRetainedSlice(int)} which may return
 * a buffer implementation that produces less garbage.
 *
 * <h3>Conversion to existing JDK types</h3>
 *
 * <h4>Byte array</h4>
 *
 * If a {@link ByteBuf} is backed by a byte array (i.e. {@code byte[]}),
 * you can access it directly via the {@link #array()} method.  To determine
 * if a buffer is backed by a byte array, {@link #hasArray()} should be used.
 *
 * <h4>NIO Buffers</h4>
 *
 * If a {@link ByteBuf} can be converted into an NIO {@link ByteBuffer} which shares its
 * content (i.e. view buffer), you can get it via the {@link #nioBuffer()} method.  To determine
 * if a buffer can be converted into an NIO buffer, use {@link #nioBufferCount()}.
 *
 * <h4>Strings</h4>
 *
 * Various {@link #toString(Charset)} methods convert a {@link ByteBuf}
 * into a {@link String}.  Please note that {@link #toString()} is not a
 * conversion method.
 *
 * <h4>I/O Streams</h4>
 *
 * Please refer to {@link ByteBufInputStream} and
 * {@link ByteBufOutputStream}.
 */
// CLR: Memory/Span replace NIO views. Byte values are unsigned CLR octets;
// explicit LE operations select wire order without a mutable ByteOrder facade.
// This first stage implements indices, primitive/bulk access and shared views.
// Memory and Span are borrowed, not retain tokens. Exclude release/reallocation
// throughout their use; a saved Memory is a view of its original allocation.
public abstract partial class ByteBuf : IReferenceCounted
{
    private int _readerIndexValue, _writerIndexValue, _markedReaderIndexValue, _markedWriterIndexValue;
    private readonly ByteBuf _indexOwner;
    // CLR: transparent wrappers share indices/marks without a second state allocation
    // or hundreds of forwarding overrides. Ordinary buffers and derived views own theirs.
    private ref int _readerIndex => ref (_indexOwner ?? this)._readerIndexValue;
    private ref int _writerIndex => ref (_indexOwner ?? this)._writerIndexValue;
    private ref int _markedReaderIndex => ref (_indexOwner ?? this)._markedReaderIndexValue;
    private ref int _markedWriterIndex => ref (_indexOwner ?? this)._markedWriterIndexValue;

    protected ByteBuf(int maxCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxCapacity);
        MaxCapacity = maxCapacity;
    }

    /**
     * Wraps another {@link ByteBuf}.
     *
     * It's important that the {@link #readerIndex()} and {@link #writerIndex()} will not do any adjustments on the
     * indices on the fly because of internal optimizations made by {@link ByteBufUtil#writeAscii(ByteBuf, CharSequence)}
     * and {@link ByteBufUtil#writeUtf8(ByteBuf, CharSequence)}.
     */
    private protected ByteBuf(ByteBuf indexOwner)
        : this(indexOwner?.MaxCapacity ?? throw new ArgumentNullException(nameof(indexOwner)))
    {
        _indexOwner = indexOwner._indexOwner ?? indexOwner;
    }

    protected abstract Memory<byte> GetMemoryCore(int index, int length);
    protected virtual ReadOnlyMemory<byte> GetReadOnlyMemoryCore(int index, int length) => GetMemoryCore(index, length);
    // A growing span write may read this buffer's old allocation. Native owners
    // pin it across replacement, so aliasing source spans stay physically valid.
    internal virtual BufferMemoryLease PinMemoryForWrite() { EnsureCanWrite(); return AcquireReadLease(); }
    /**
         * Returns the number of bytes (octets) this buffer can contain.
         */
    /**
         * Adjusts the capacity of this buffer.  If the {@code newCapacity} is less than the current
         * capacity, the content of this buffer is truncated.  If the {@code newCapacity} is greater
         * than the current capacity, the buffer is appended with unspecified data whose length is
         * {@code (newCapacity - currentCapacity)}.
         *
         * @throws IllegalArgumentException if the {@code newCapacity} is greater than {@link #maxCapacity()}
         */
    public abstract int Capacity { get; set; }
    /**
         * Returns the maximum allowed capacity of this buffer. This value provides an upper
         * bound on {@link #capacity()}.
         */
    public int MaxCapacity { get; }
    /**
         * Returns {@code true} if and only if this buffer is backed by an
         * NIO direct buffer.
         */
    public abstract bool IsDirect { get; }
    /**
         * Return the underlying buffer instance if this buffer is a wrapper of another buffer.
         *
         * @return {@code null} if this buffer is not a wrapper
         */
    public virtual ByteBuf Unwrap() => null;
    public abstract int ReferenceCount { get; }
    public abstract ByteBuf Retain(int increment = 1);
    public abstract bool Release(int decrement = 1);
    public virtual ByteBuf Touch(object hint = null) => this;
    IReferenceCounted IReferenceCounted.Retain() => Retain();
    IReferenceCounted IReferenceCounted.Retain(int increment) => Retain(increment);
    IReferenceCounted IReferenceCounted.Touch() => Touch();
    IReferenceCounted IReferenceCounted.Touch(object hint) => Touch(hint);
    bool IReferenceCounted.Release() => Release();
    bool IReferenceCounted.Release(int decrement) => Release(decrement);

    /**
         * Returns the {@code readerIndex} of this buffer.
         */
    public int ReaderIndex
    {
        get => _readerIndex;
        /**
             * Sets the {@code readerIndex} of this buffer.
             *
             * @throws IndexOutOfBoundsException
             *         if the specified {@code readerIndex} is
             *            less than {@code 0} or
             *            greater than {@code this.writerIndex}
             */
        set { CheckIndices(value, _writerIndex); _readerIndex = value; }
    }
    /**
         * Returns the {@code writerIndex} of this buffer.
         */
    public int WriterIndex
    {
        get => _writerIndex;
        /**
             * Sets the {@code writerIndex} of this buffer.
             *
             * @throws IndexOutOfBoundsException
             *         if the specified {@code writerIndex} is
             *            less than {@code this.readerIndex} or
             *            greater than {@code this.capacity}
             */
        set { CheckIndices(_readerIndex, value); _writerIndex = value; }
    }
    /**
         * Returns the number of readable bytes which is equal to
         * {@code (this.writerIndex - this.readerIndex)}.
         */
    public int ReadableBytes => _writerIndex - _readerIndex;
    /**
         * Returns the number of writable bytes which is equal to
         * {@code (this.capacity - this.writerIndex)}.
         */
    public int WritableBytes => Capacity - _writerIndex;
    /**
         * Returns the maximum possible number of writable bytes, which is equal to
         * {@code (this.maxCapacity - this.writerIndex)}.
         */
    public int MaxWritableBytes => MaxCapacity - _writerIndex;
    /**
         * Returns {@code true}
         * if and only if {@code (this.writerIndex - this.readerIndex)} is greater
         * than {@code 0}.
         */
    public bool IsReadable => _writerIndex > _readerIndex;
    /**
         * Returns {@code true}
         * if and only if {@code (this.capacity - this.writerIndex)} is greater
         * than {@code 0}.
         */
    public bool IsWritable => !IsReadOnly && Capacity > _writerIndex;

    private void CheckIndices(int reader, int writer)
    {
        if (reader < 0 || reader > writer || writer > Capacity)
            throw new ArgumentOutOfRangeException(nameof(reader), "Expected 0 <= reader <= writer <= capacity.");
    }
    /**
         * Sets the {@code readerIndex} and {@code writerIndex} of this buffer
         * in one shot.  This method is useful when you have to worry about the
         * invocation order of {@link #readerIndex(int)} and {@link #writerIndex(int)}
         * methods.  For example, the following code will fail:
         *
         * <pre>
         * // Create a buffer whose readerIndex, writerIndex and capacity are
         * // 0, 0 and 8 respectively.
         * {@link ByteBuf} buf = {@link Unpooled}.buffer(8);
         *
         * // IndexOutOfBoundsException is thrown because the specified
         * // readerIndex (2) cannot be greater than the current writerIndex (0).
         * buf.readerIndex(2);
         * buf.writerIndex(4);
         * </pre>
         *
         * The following code will also fail:
         *
         * <pre>
         * // Create a buffer whose readerIndex, writerIndex and capacity are
         * // 0, 8 and 8 respectively.
         * {@link ByteBuf} buf = {@link Unpooled}.wrappedBuffer(new byte[8]);
         *
         * // readerIndex becomes 8.
         * buf.readLong();
         *
         * // IndexOutOfBoundsException is thrown because the specified
         * // writerIndex (4) cannot be less than the current readerIndex (8).
         * buf.writerIndex(4);
         * buf.readerIndex(2);
         * </pre>
         *
         * By contrast, this method guarantees that it never
         * throws an {@link IndexOutOfBoundsException} as long as the specified
         * indexes meet basic constraints, regardless what the current index
         * values of the buffer are:
         *
         * <pre>
         * // No matter what the current state of the buffer is, the following
         * // call always succeeds as long as the capacity of the buffer is not
         * // less than 4.
         * buf.setIndex(2, 4);
         * </pre>
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code readerIndex} is less than 0,
         *         if the specified {@code writerIndex} is less than the specified
         *         {@code readerIndex} or if the specified {@code writerIndex} is
         *         greater than {@code this.capacity}
         */
    public ByteBuf SetIndex(int readerIndex, int writerIndex)
    {
        CheckIndices(readerIndex, writerIndex);
        _readerIndex = readerIndex; _writerIndex = writerIndex;
        return this;
    }
    /**
         * Sets the {@code readerIndex} and {@code writerIndex} of this buffer to
         * {@code 0}.
         * This method is identical to {@link #setIndex(int, int) setIndex(0, 0)}.
         * <p>
         * Please note that the behavior of this method is different
         * from that of NIO buffer, which sets the {@code limit} to
         * the {@code capacity} of the buffer.
         */
    public ByteBuf Clear() { _readerIndex = _writerIndex = 0; return this; }
    /**
         * Marks the current {@code readerIndex} in this buffer.  You can
         * reposition the current {@code readerIndex} to the marked
         * {@code readerIndex} by calling {@link #resetReaderIndex()}.
         * The initial value of the marked {@code readerIndex} is {@code 0}.
         */
    public ByteBuf MarkReaderIndex() { _markedReaderIndex = _readerIndex; return this; }
    /**
         * Marks the current {@code writerIndex} in this buffer.  You can
         * reposition the current {@code writerIndex} to the marked
         * {@code writerIndex} by calling {@link #resetWriterIndex()}.
         * The initial value of the marked {@code writerIndex} is {@code 0}.
         */
    public ByteBuf MarkWriterIndex() { _markedWriterIndex = _writerIndex; return this; }
    /**
         * Repositions the current {@code readerIndex} to the marked
         * {@code readerIndex} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the current {@code writerIndex} is less than the marked
         *         {@code readerIndex}
         */
    public ByteBuf ResetReaderIndex() { ReaderIndex = _markedReaderIndex; return this; }
    /**
         * Repositions the current {@code writerIndex} to the marked
         * {@code writerIndex} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the current {@code readerIndex} is greater than the marked
         *         {@code writerIndex}
         */
    public ByteBuf ResetWriterIndex() { WriterIndex = _markedWriterIndex; return this; }

    /**
         * Discards the bytes between the 0th index and {@code readerIndex}.
         * It moves the bytes between {@code readerIndex} and {@code writerIndex}
         * to the 0th index, and sets {@code readerIndex} and {@code writerIndex}
         * to {@code 0} and {@code oldWriterIndex - oldReaderIndex} respectively.
         * <p>
         * Please refer to the class documentation for more detailed explanation.
         */
    public virtual ByteBuf DiscardReadBytes()
    {
        EnsureCanWrite();
        if (_readerIndex == 0) return this;
        int consumed = _readerIndex;
        if (_readerIndex != _writerIndex)
            SetBytes(0, this, _readerIndex, ReadableBytes);
        _writerIndex -= consumed;
        AdjustMarkers(consumed);
        _readerIndex = 0;
        return this;
    }
    /**
         * Similar to {@link ByteBuf#discardReadBytes()} except that this method might discard
         * some, all, or none of read bytes depending on its internal implementation to reduce
         * overall memory bandwidth consumption at the cost of potentially additional memory
         * consumption.
         */
    public virtual ByteBuf DiscardSomeReadBytes()
    {
        EnsureAccessible();
        if (_readerIndex > 0 && _readerIndex == _writerIndex)
        {
            // All bytes were consumed; only indices and marks need adjustment, including on read-only views.
            AdjustMarkers(_readerIndex);
            _readerIndex = _writerIndex = 0;
        }
        else if (_readerIndex > 0 && _readerIndex >= (Capacity >>> 1))
            DiscardReadBytes();
        return this;
    }
    protected void AdjustMarkers(int decrement)
    {
        if (_markedReaderIndex <= decrement)
        {
            _markedReaderIndex = 0;
            _markedWriterIndex = Math.Max(0, _markedWriterIndex - decrement);
        }
        else { _markedReaderIndex -= decrement; _markedWriterIndex -= decrement; }
    }
    // Called after a capacity reduction
    protected void TrimIndicesToCapacity(int newCapacity)
    {
        if (_writerIndex > newCapacity)
        { _readerIndex = Math.Min(_readerIndex, newCapacity); _writerIndex = newCapacity; }
    }
    /**
         * Expands the buffer {@link #capacity()} to make sure the number of
         * {@linkplain #writableBytes() writable bytes} is equal to or greater than the
         * specified value.  If there are enough writable bytes in this buffer, this method
         * returns with no side effect.
         *
         * @param minWritableBytes
         *        the expected minimum number of writable bytes
         * @throws IndexOutOfBoundsException
         *         if {@link #writerIndex()} + {@code minWritableBytes} &gt; {@link #maxCapacity()}.
         * @see #capacity(int)
         */
    public ByteBuf EnsureWritable(int minimumWritableBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimumWritableBytes);
        EnsureCanWrite();
        if (minimumWritableBytes <= WritableBytes) return this;
        if (minimumWritableBytes > MaxCapacity - _writerIndex)
            throw new ArgumentOutOfRangeException(nameof(minimumWritableBytes));
        Capacity = CalculateNewCapacity(_writerIndex + minimumWritableBytes, MaxCapacity);
        return this;
    }
    /**
         * Expands the buffer {@link #capacity()} to make sure the number of
         * {@linkplain #writableBytes() writable bytes} is equal to or greater than the
         * specified value. Unlike {@link #ensureWritable(int)}, this method returns a status code.
         *
         * @param minWritableBytes
         *        the expected minimum number of writable bytes
         * @param force
         *        When {@link #writerIndex()} + {@code minWritableBytes} &gt; {@link #maxCapacity()}:
         *        <ul>
         *        <li>{@code true} - the capacity of the buffer is expanded to {@link #maxCapacity()}</li>
         *        <li>{@code false} - the capacity of the buffer is unchanged</li>
         *        </ul>
         * @return {@code 0} if the buffer has enough writable bytes, and its capacity is unchanged.
         *         {@code 1} if the buffer does not have enough bytes, and its capacity is unchanged.
         *         {@code 2} if the buffer has enough writable bytes, and its capacity has been increased.
         *         {@code 3} if the buffer does not have enough bytes, but its capacity has been
         *                   increased to its maximum.
         */
    public int EnsureWritable(int minimumWritableBytes, bool force)
    {
        EnsureAccessible();
        ArgumentOutOfRangeException.ThrowIfNegative(minimumWritableBytes);
        if (IsReadOnly) return 1;
        if (minimumWritableBytes <= WritableBytes) return 0;
        if (minimumWritableBytes > MaxCapacity - _writerIndex)
        {
            if (!force || Capacity == MaxCapacity) return 1;
            Capacity = MaxCapacity;
            return 3;
        }
        Capacity = CalculateNewCapacity(_writerIndex + minimumWritableBytes, MaxCapacity);
        return 2;
    }
    // AbstractByteBufAllocator.calculateNewCapacity: same 64-byte / 4-MiB growth policy.
    internal static int CalculateNewCapacity(int minimum, int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimum);
        if (minimum > maximum) throw new ArgumentOutOfRangeException(nameof(minimum));
        const int threshold = 4 * 1024 * 1024; // 4 MiB page
        if (minimum == threshold) return threshold;
        // If over threshold, do not double but just increase by threshold.
        if (minimum > threshold)
        {
            int capacity = minimum / threshold * threshold;
            return capacity > maximum - threshold ? maximum : capacity + threshold;
        }
        // 64 <= newCapacity is a power of 2 <= threshold
        int rounded = 64;
        while (rounded < minimum) rounded <<= 1;
        return Math.Min(rounded, maximum);
    }

    protected void EnsureAccessible()
    {
        if (ReferenceCount == 0) throw new IllegalReferenceCountException(0);
    }
    protected void CheckNewCapacity(int capacity)
    {
        EnsureAccessible();
        if (capacity < 0 || capacity > MaxCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));
    }
    protected void CheckReadableBytes(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        EnsureAccessible();
        if (length > ReadableBytes) throw new ArgumentOutOfRangeException(nameof(length));
    }
    protected void CheckIndex(int index, int length)
    {
        EnsureAccessible();
        // Subtraction avoids Java's signed index + length overflow boundary.
        if (index < 0 || length < 0 || index > Capacity - length)
            throw new ArgumentOutOfRangeException(nameof(index));
    }
    /// <summary>Returns writable borrowed memory for a contiguous range.</summary>
    /// <exception cref="NotSupportedException">The range is read-only or spans
    /// multiple components. Use SetBytes or consolidate for segmented writes.</exception>
    public Memory<byte> AsMemory(int index, int length)
    {
        EnsureCanWrite();
        CheckIndex(index, length);
        return GetMemoryCore(index, length);
    }
    public Span<byte> AsSpan(int index, int length) => AsMemory(index, length).Span;
    public ReadOnlyMemory<byte> ReadableMemory => AsReadOnlyMemory(_readerIndex, ReadableBytes);

    /**
         * Gets a byte at the specified absolute {@code index} in this buffer.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 1} is greater than {@code this.capacity}
         */
    public byte GetByte(int index) => AsReadOnlySpan(index, 1)[0];
    /**
         * Gets a boolean at the specified absolute (@code index) in this buffer.
         * This method does not modify the {@code readerIndex} or {@code writerIndex}
         * of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 1} is greater than {@code this.capacity}
         */
    public bool GetBoolean(int index) => GetByte(index) != 0;
    /**
         * Sets the specified byte at the specified absolute {@code index} in this
         * buffer.  The 24 high-order bits of the specified value are ignored.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 1} is greater than {@code this.capacity}
         */
    public ByteBuf SetByte(int index, int value) { AsSpan(index, 1)[0] = unchecked((byte)value); return this; }
    /**
         * Sets the specified boolean at the specified absolute {@code index} in this
         * buffer.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 1} is greater than {@code this.capacity}
         */
    public ByteBuf SetBoolean(int index, bool value) => SetByte(index, value ? 1 : 0);
    /**
         * Gets a 16-bit short integer at the specified absolute {@code index} in
         * this buffer.  This method does not modify {@code readerIndex} or
         * {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 2} is greater than {@code this.capacity}
         */
    public short GetShort(int index) => unchecked((short)ReadWord(index, 2, false));
    /**
         * Sets the specified 16-bit short integer at the specified absolute
         * {@code index} in this buffer.  The 16 high-order bits of the specified
         * value are ignored.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 2} is greater than {@code this.capacity}
         */
    public ByteBuf SetShort(int index, int value) => SetWord(index, unchecked((ulong)value), 2, false);
    /**
         * Gets a 16-bit short integer at the specified absolute {@code index} in
         * this buffer in Little Endian Byte Order. This method does not modify
         * {@code readerIndex} or {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 2} is greater than {@code this.capacity}
         */
    public short GetShortLE(int index) => unchecked((short)ReadWord(index, 2, true));
    /**
         * Sets the specified 16-bit short integer at the specified absolute
         * {@code index} in this buffer with the Little Endian Byte Order.
         * The 16 high-order bits of the specified value are ignored.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 2} is greater than {@code this.capacity}
         */
    public ByteBuf SetShortLE(int index, int value) => SetWord(index, unchecked((ulong)value), 2, true);
    /**
         * Gets a 32-bit integer at the specified absolute {@code index} in
         * this buffer.  This method does not modify {@code readerIndex} or
         * {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 4} is greater than {@code this.capacity}
         */
    public int GetInt(int index) => unchecked((int)ReadWord(index, 4, false));
    /**
         * Sets the specified 32-bit integer at the specified absolute
         * {@code index} in this buffer.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 4} is greater than {@code this.capacity}
         */
    public ByteBuf SetInt(int index, int value) => SetWord(index, unchecked((ulong)value), 4, false);
    /**
         * Gets a 32-bit integer at the specified absolute {@code index} in
         * this buffer with Little Endian Byte Order. This method does not
         * modify {@code readerIndex} or {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 4} is greater than {@code this.capacity}
         */
    public int GetIntLE(int index) => unchecked((int)ReadWord(index, 4, true));
    /**
         * Sets the specified 32-bit integer at the specified absolute
         * {@code index} in this buffer with Little Endian byte order
         * .
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 4} is greater than {@code this.capacity}
         */
    public ByteBuf SetIntLE(int index, int value) => SetWord(index, unchecked((ulong)value), 4, true);
    /**
         * Gets a 64-bit long integer at the specified absolute {@code index} in
         * this buffer.  This method does not modify {@code readerIndex} or
         * {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 8} is greater than {@code this.capacity}
         */
    public long GetLong(int index) => unchecked((long)ReadWord(index, 8, false));
    /**
         * Sets the specified 64-bit long integer at the specified absolute
         * {@code index} in this buffer.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 8} is greater than {@code this.capacity}
         */
    public ByteBuf SetLong(int index, long value) => SetWord(index, unchecked((ulong)value), 8, false);
    /**
         * Gets a 64-bit long integer at the specified absolute {@code index} in
         * this buffer in Little Endian Byte Order. This method does not
         * modify {@code readerIndex} or {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 8} is greater than {@code this.capacity}
         */
    public long GetLongLE(int index) => unchecked((long)ReadWord(index, 8, true));
    /**
         * Sets the specified 64-bit long integer at the specified absolute
         * {@code index} in this buffer in Little Endian Byte Order.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 8} is greater than {@code this.capacity}
         */
    public ByteBuf SetLongLE(int index, long value) => SetWord(index, unchecked((ulong)value), 8, true);
    /**
         * Gets an unsigned 24-bit medium integer at the specified absolute
         * {@code index} in this buffer.  This method does not modify
         * {@code readerIndex} or {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 3} is greater than {@code this.capacity}
         */
    public int GetUnsignedMedium(int index) => (int)ReadWord(index, 3, false);
    /**
         * Gets an unsigned 24-bit medium integer at the specified absolute
         * {@code index} in this buffer in Little Endian Byte Order.
         * This method does not modify {@code readerIndex} or
         * {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 3} is greater than {@code this.capacity}
         */
    public int GetUnsignedMediumLE(int index) => (int)ReadWord(index, 3, true);
    /**
         * Gets a 24-bit medium integer at the specified absolute {@code index} in
         * this buffer.  This method does not modify {@code readerIndex} or
         * {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 3} is greater than {@code this.capacity}
         */
    public int GetMedium(int index) => GetUnsignedMedium(index) << 8 >> 8;
    /**
         * Gets a 24-bit medium integer at the specified absolute {@code index} in
         * this buffer in the Little Endian Byte Order. This method does not
         * modify {@code readerIndex} or {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 3} is greater than {@code this.capacity}
         */
    public int GetMediumLE(int index) => GetUnsignedMediumLE(index) << 8 >> 8;
    /**
         * Sets the specified 24-bit medium integer at the specified absolute
         * {@code index} in this buffer.  Please note that the most significant
         * byte is ignored in the specified value.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 3} is greater than {@code this.capacity}
         */
    public ByteBuf SetMedium(int index, int value) => SetWord(index, unchecked((ulong)value), 3, false);
    /**
         * Sets the specified 24-bit medium integer at the specified absolute
         * {@code index} in this buffer in the Little Endian Byte Order.
         * Please note that the most significant byte is ignored in the
         * specified value.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 3} is greater than {@code this.capacity}
         */
    public ByteBuf SetMediumLE(int index, int value) => SetWord(index, unchecked((ulong)value), 3, true);
    /**
         * Gets an unsigned 16-bit short integer at the specified absolute
         * {@code index} in this buffer.  This method does not modify
         * {@code readerIndex} or {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 2} is greater than {@code this.capacity}
         */
    public ushort GetUnsignedShort(int index) => unchecked((ushort)GetShort(index));
    /**
         * Gets an unsigned 16-bit short integer at the specified absolute
         * {@code index} in this buffer in Little Endian Byte Order.
         * This method does not modify {@code readerIndex} or
         * {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 2} is greater than {@code this.capacity}
         */
    public ushort GetUnsignedShortLE(int index) => unchecked((ushort)GetShortLE(index));
    /**
         * Gets an unsigned 32-bit integer at the specified absolute {@code index}
         * in this buffer.  This method does not modify {@code readerIndex} or
         * {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 4} is greater than {@code this.capacity}
         */
    public uint GetUnsignedInt(int index) => unchecked((uint)GetInt(index));
    /**
         * Gets an unsigned 32-bit integer at the specified absolute {@code index}
         * in this buffer in Little Endian Byte Order. This method does not
         * modify {@code readerIndex} or {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 4} is greater than {@code this.capacity}
         */
    public uint GetUnsignedIntLE(int index) => unchecked((uint)GetIntLE(index));
    /**
         * Gets a 2-byte UTF-16 character at the specified absolute
         * {@code index} in this buffer.  This method does not modify
         * {@code readerIndex} or {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 2} is greater than {@code this.capacity}
         */
    public char GetChar(int index) => (char)GetUnsignedShort(index);
    /**
         * Sets the specified 2-byte UTF-16 character at the specified absolute
         * {@code index} in this buffer.
         * The 16 high-order bits of the specified value are ignored.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 2} is greater than {@code this.capacity}
         */
    public ByteBuf SetChar(int index, char value) => SetShort(index, value);
    /**
         * Gets a 32-bit floating point number at the specified absolute
         * {@code index} in this buffer.  This method does not modify
         * {@code readerIndex} or {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 4} is greater than {@code this.capacity}
         */
    public float GetFloat(int index) => BitConverter.Int32BitsToSingle(GetInt(index));
    /**
         * Gets a 32-bit floating point number at the specified absolute
         * {@code index} in this buffer in Little Endian Byte Order.
         * This method does not modify {@code readerIndex} or
         * {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 4} is greater than {@code this.capacity}
         */
    public float GetFloatLE(int index) => BitConverter.Int32BitsToSingle(GetIntLE(index));
    /**
         * Gets a 64-bit floating point number at the specified absolute
         * {@code index} in this buffer.  This method does not modify
         * {@code readerIndex} or {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 8} is greater than {@code this.capacity}
         */
    public double GetDouble(int index) => BitConverter.Int64BitsToDouble(GetLong(index));
    /**
         * Gets a 64-bit floating point number at the specified absolute
         * {@code index} in this buffer in Little Endian Byte Order.
         * This method does not modify {@code readerIndex} or
         * {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 8} is greater than {@code this.capacity}
         */
    public double GetDoubleLE(int index) => BitConverter.Int64BitsToDouble(GetLongLE(index));
    /**
         * Sets the specified 32-bit floating-point number at the specified
         * absolute {@code index} in this buffer.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 4} is greater than {@code this.capacity}
         */
    public ByteBuf SetFloat(int index, float value) => SetInt(index, BitConverter.SingleToInt32Bits(value));
    /**
         * Sets the specified 32-bit floating-point number at the specified
         * absolute {@code index} in this buffer in Little Endian Byte Order.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 4} is greater than {@code this.capacity}
         */
    public ByteBuf SetFloatLE(int index, float value) => SetIntLE(index, BitConverter.SingleToInt32Bits(value));
    /**
         * Sets the specified 64-bit floating-point number at the specified
         * absolute {@code index} in this buffer.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 8} is greater than {@code this.capacity}
         */
    public ByteBuf SetDouble(int index, double value) => SetLong(index, BitConverter.DoubleToInt64Bits(value));
    /**
         * Sets the specified 64-bit floating-point number at the specified
         * absolute {@code index} in this buffer in Little Endian Byte Order.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 8} is greater than {@code this.capacity}
         */
    public ByteBuf SetDoubleLE(int index, double value) => SetLongLE(index, BitConverter.DoubleToInt64Bits(value));

    /**
         * Gets a byte at the current {@code readerIndex} and increases
         * the {@code readerIndex} by {@code 1} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 1}
         */
    public byte ReadByte() { CheckReadableBytes(1); byte value = GetByte(_readerIndex); ++_readerIndex; return value; }
    /**
         * Sets the specified byte at the current {@code writerIndex}
         * and increases the {@code writerIndex} by {@code 1} in this buffer.
         * The 24 high-order bits of the specified value are ignored.
         * If {@code this.writableBytes} is less than {@code 1}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public ByteBuf WriteByte(int value) { EnsureWritable(1); SetByte(_writerIndex, value); ++_writerIndex; return this; }
    /**
         * Gets a boolean at the current {@code readerIndex} and increases
         * the {@code readerIndex} by {@code 1} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 1}
         */
    public bool ReadBoolean() => ReadByte() != 0;
    /**
         * Sets the specified boolean at the current {@code writerIndex}
         * and increases the {@code writerIndex} by {@code 1} in this buffer.
         * If {@code this.writableBytes} is less than {@code 1}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public ByteBuf WriteBoolean(bool value) => WriteByte(value ? 1 : 0);
    /**
         * Gets a 16-bit short integer at the current {@code readerIndex}
         * and increases the {@code readerIndex} by {@code 2} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 2}
         */
    public short ReadShort() { CheckReadableBytes(2); short value = GetShort(_readerIndex); _readerIndex += 2; return value; }
    /**
         * Sets the specified 16-bit short integer at the current
         * {@code writerIndex} and increases the {@code writerIndex} by {@code 2}
         * in this buffer.  The 16 high-order bits of the specified value are ignored.
         * If {@code this.writableBytes} is less than {@code 2}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public ByteBuf WriteShort(int value) { EnsureWritable(2); SetShort(_writerIndex, value); _writerIndex += 2; return this; }
    /**
         * Gets a 16-bit short integer at the current {@code readerIndex}
         * in the Little Endian Byte Order and increases the {@code readerIndex}
         * by {@code 2} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 2}
         */
    public short ReadShortLE() { CheckReadableBytes(2); short value = GetShortLE(_readerIndex); _readerIndex += 2; return value; }
    /**
         * Sets the specified 16-bit short integer in the Little Endian Byte
         * Order at the current {@code writerIndex} and increases the
         * {@code writerIndex} by {@code 2} in this buffer.
         * The 16 high-order bits of the specified value are ignored.
         * If {@code this.writableBytes} is less than {@code 2}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public ByteBuf WriteShortLE(int value) { EnsureWritable(2); SetShortLE(_writerIndex, value); _writerIndex += 2; return this; }
    /**
         * Gets a 24-bit medium integer at the current {@code readerIndex}
         * and increases the {@code readerIndex} by {@code 3} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 3}
         */
    public int ReadMedium() { CheckReadableBytes(3); int value = GetMedium(_readerIndex); _readerIndex += 3; return value; }
    /**
         * Sets the specified 24-bit medium integer at the current
         * {@code writerIndex} and increases the {@code writerIndex} by {@code 3}
         * in this buffer.
         * If {@code this.writableBytes} is less than {@code 3}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public ByteBuf WriteMedium(int value) { EnsureWritable(3); SetMedium(_writerIndex, value); _writerIndex += 3; return this; }
    /**
         * Gets a 24-bit medium integer at the current {@code readerIndex}
         * in the Little Endian Byte Order and increases the
         * {@code readerIndex} by {@code 3} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 3}
         */
    public int ReadMediumLE() { CheckReadableBytes(3); int value = GetMediumLE(_readerIndex); _readerIndex += 3; return value; }
    /**
         * Sets the specified 24-bit medium integer at the current
         * {@code writerIndex} in the Little Endian Byte Order and
         * increases the {@code writerIndex} by {@code 3} in this
         * buffer.
         * If {@code this.writableBytes} is less than {@code 3}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public ByteBuf WriteMediumLE(int value) { EnsureWritable(3); SetMediumLE(_writerIndex, value); _writerIndex += 3; return this; }
    /**
         * Gets a 32-bit integer at the current {@code readerIndex}
         * and increases the {@code readerIndex} by {@code 4} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 4}
         */
    public int ReadInt() { CheckReadableBytes(4); int value = GetInt(_readerIndex); _readerIndex += 4; return value; }
    /**
         * Sets the specified 32-bit integer at the current {@code writerIndex}
         * and increases the {@code writerIndex} by {@code 4} in this buffer.
         * If {@code this.writableBytes} is less than {@code 4}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public ByteBuf WriteInt(int value) { EnsureWritable(4); SetInt(_writerIndex, value); _writerIndex += 4; return this; }
    /**
         * Gets a 32-bit integer at the current {@code readerIndex}
         * in the Little Endian Byte Order and increases the {@code readerIndex}
         * by {@code 4} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 4}
         */
    public int ReadIntLE() { CheckReadableBytes(4); int value = GetIntLE(_readerIndex); _readerIndex += 4; return value; }
    /**
         * Sets the specified 32-bit integer at the current {@code writerIndex}
         * in the Little Endian Byte Order and increases the {@code writerIndex}
         * by {@code 4} in this buffer.
         * If {@code this.writableBytes} is less than {@code 4}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public ByteBuf WriteIntLE(int value) { EnsureWritable(4); SetIntLE(_writerIndex, value); _writerIndex += 4; return this; }
    /**
         * Gets a 64-bit integer at the current {@code readerIndex}
         * and increases the {@code readerIndex} by {@code 8} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 8}
         */
    public long ReadLong() { CheckReadableBytes(8); long value = GetLong(_readerIndex); _readerIndex += 8; return value; }
    /**
         * Sets the specified 64-bit long integer at the current
         * {@code writerIndex} and increases the {@code writerIndex} by {@code 8}
         * in this buffer.
         * If {@code this.writableBytes} is less than {@code 8}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public ByteBuf WriteLong(long value) { EnsureWritable(8); SetLong(_writerIndex, value); _writerIndex += 8; return this; }
    /**
         * Gets a 64-bit integer at the current {@code readerIndex}
         * in the Little Endian Byte Order and increases the {@code readerIndex}
         * by {@code 8} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 8}
         */
    public long ReadLongLE() { CheckReadableBytes(8); long value = GetLongLE(_readerIndex); _readerIndex += 8; return value; }
    /**
         * Sets the specified 64-bit long integer at the current
         * {@code writerIndex} in the Little Endian Byte Order and
         * increases the {@code writerIndex} by {@code 8}
         * in this buffer.
         * If {@code this.writableBytes} is less than {@code 8}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public ByteBuf WriteLongLE(long value) { EnsureWritable(8); SetLongLE(_writerIndex, value); _writerIndex += 8; return this; }
    /**
         * Gets a 32-bit floating point number at the current {@code readerIndex}
         * and increases the {@code readerIndex} by {@code 4} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 4}
         */
    public float ReadFloat() { CheckReadableBytes(4); float value = GetFloat(_readerIndex); _readerIndex += 4; return value; }
    /**
         * Sets the specified 32-bit floating point number at the current
         * {@code writerIndex} and increases the {@code writerIndex} by {@code 4}
         * in this buffer.
         * If {@code this.writableBytes} is less than {@code 4}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public ByteBuf WriteFloat(float value) { EnsureWritable(4); SetFloat(_writerIndex, value); _writerIndex += 4; return this; }
    /**
         * Gets a 32-bit floating point number at the current {@code readerIndex}
         * in Little Endian Byte Order and increases the {@code readerIndex}
         * by {@code 4} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 4}
         */
    public float ReadFloatLE() { CheckReadableBytes(4); float value = GetFloatLE(_readerIndex); _readerIndex += 4; return value; }
    /**
         * Sets the specified 32-bit floating point number at the current
         * {@code writerIndex} in Little Endian Byte Order and increases
         * the {@code writerIndex} by {@code 4} in this buffer.
         * If {@code this.writableBytes} is less than {@code 4}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public ByteBuf WriteFloatLE(float value) { EnsureWritable(4); SetFloatLE(_writerIndex, value); _writerIndex += 4; return this; }
    /**
         * Gets a 64-bit floating point number at the current {@code readerIndex}
         * and increases the {@code readerIndex} by {@code 8} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 8}
         */
    public double ReadDouble() { CheckReadableBytes(8); double value = GetDouble(_readerIndex); _readerIndex += 8; return value; }
    /**
         * Sets the specified 64-bit floating point number at the current
         * {@code writerIndex} and increases the {@code writerIndex} by {@code 8}
         * in this buffer.
         * If {@code this.writableBytes} is less than {@code 8}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public ByteBuf WriteDouble(double value) { EnsureWritable(8); SetDouble(_writerIndex, value); _writerIndex += 8; return this; }
    /**
         * Gets a 64-bit floating point number at the current {@code readerIndex}
         * in Little Endian Byte Order and increases the {@code readerIndex}
         * by {@code 8} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 8}
         */
    public double ReadDoubleLE() { CheckReadableBytes(8); double value = GetDoubleLE(_readerIndex); _readerIndex += 8; return value; }
    /**
         * Sets the specified 64-bit floating point number at the current
         * {@code writerIndex} in Little Endian Byte Order and increases
         * the {@code writerIndex} by {@code 8} in this buffer.
         * If {@code this.writableBytes} is less than {@code 8}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public ByteBuf WriteDoubleLE(double value) { EnsureWritable(8); SetDoubleLE(_writerIndex, value); _writerIndex += 8; return this; }
    /**
         * Gets an unsigned 16-bit short integer at the current {@code readerIndex}
         * and increases the {@code readerIndex} by {@code 2} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 2}
         */
    public ushort ReadUnsignedShort() => unchecked((ushort)ReadShort());
    /**
         * Gets an unsigned 16-bit short integer at the current {@code readerIndex}
         * in the Little Endian Byte Order and increases the {@code readerIndex}
         * by {@code 2} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 2}
         */
    public ushort ReadUnsignedShortLE() => unchecked((ushort)ReadShortLE());
    /**
         * Gets an unsigned 32-bit integer at the current {@code readerIndex}
         * and increases the {@code readerIndex} by {@code 4} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 4}
         */
    public uint ReadUnsignedInt() => unchecked((uint)ReadInt());
    /**
         * Gets an unsigned 32-bit integer at the current {@code readerIndex}
         * in the Little Endian Byte Order and increases the {@code readerIndex}
         * by {@code 4} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 4}
         */
    public uint ReadUnsignedIntLE() => unchecked((uint)ReadIntLE());
    /**
         * Gets an unsigned 24-bit medium integer at the current {@code readerIndex}
         * and increases the {@code readerIndex} by {@code 3} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 3}
         */
    public int ReadUnsignedMedium() => ReadMedium() & 0xFFFFFF;
    /**
         * Gets an unsigned 24-bit medium integer at the current {@code readerIndex}
         * in the Little Endian Byte Order and increases the {@code readerIndex}
         * by {@code 3} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 3}
         */
    public int ReadUnsignedMediumLE() => ReadMediumLE() & 0xFFFFFF;
    /**
         * Gets a 2-byte UTF-16 character at the current {@code readerIndex}
         * and increases the {@code readerIndex} by {@code 2} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 2}
         */
    public char ReadChar() => (char)ReadUnsignedShort();
    /**
         * Sets the specified 2-byte UTF-16 character at the current
         * {@code writerIndex} and increases the {@code writerIndex} by {@code 2}
         * in this buffer.  The 16 high-order bits of the specified value are ignored.
         * If {@code this.writableBytes} is less than {@code 2}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public ByteBuf WriteChar(char value) => WriteShort(value);
    /**
         * Increases the current {@code readerIndex} by the specified
         * {@code length} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code length} is greater than {@code this.readableBytes}
         */
    public ByteBuf SkipBytes(int length) { CheckReadableBytes(length); _readerIndex += length; return this; }
    /**
         * Transfers this buffer's data to the specified destination starting at
         * the specified absolute {@code index}.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         if {@code index + dst.length} is greater than
         *            {@code this.capacity}
         */
    // CLR: The CLR destination is an explicit span range with no writer index.
    public ByteBuf GetBytes(int index, Span<byte> destination)
    {
        CheckIndex(index, destination.Length);
        if (TryGetReadOnlyMemoryCore(index, destination.Length, out var memory)) memory.Span.CopyTo(destination);
        else
        {
            // Snapshot segmented input before a possibly aliased destination is modified.
            byte[] bytes = new byte[destination.Length];
            GetBytesCore(index, bytes); bytes.AsSpan().CopyTo(destination);
        }
        return this;
    }
    /**
         * Transfers the specified source array's data to this buffer starting at
         * the specified absolute {@code index}.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         if {@code index + src.length} is greater than
         *            {@code this.capacity}
         */
    // CLR: The CLR source is an explicit span range with no reader index.
    public ByteBuf SetBytes(int index, ReadOnlySpan<byte> source)
    {
        EnsureCanWrite(); CheckIndex(index, source.Length);
        if (TryGetMemoryCore(index, source.Length, out var memory)) source.CopyTo(memory.Span);
        else SetBytesCore(index, source.ToArray()); // Preserve overlap across component boundaries.
        return this;
    }
    /**
         * Transfers this buffer's data to the specified destination starting at
         * the current {@code readerIndex} and increases the {@code readerIndex}
         * by the number of the transferred bytes (= {@code dst.length}).
         *
         * @throws IndexOutOfBoundsException
         *         if {@code dst.length} is greater than {@code this.readableBytes}
         */
    // CLR: The CLR destination is an explicit span range; only this buffer reader advances.
    public ByteBuf ReadBytes(Span<byte> destination)
    {
        CheckReadableBytes(destination.Length);
        GetBytes(_readerIndex, destination); _readerIndex += destination.Length;
        return this;
    }
    /**
         * Transfers the specified source array's data to this buffer starting at
         * the current {@code writerIndex} and increases the {@code writerIndex}
         * by the number of the transferred bytes (= {@code src.length}).
         * If {@code this.writableBytes} is less than {@code src.length}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    // CLR: The CLR source is an explicit span range; only this buffer writer advances.
    public ByteBuf WriteBytes(ReadOnlySpan<byte> source)
    {
        using var lease = PinMemoryForWrite();
        EnsureWritable(source.Length);
        SetBytes(_writerIndex, source); _writerIndex += source.Length;
        return this;
    }
    /**
         * Transfers this buffer's data to the specified destination starting at
         * the specified absolute {@code index}.
         * This method does not modify {@code readerIndex} or {@code writerIndex}
         * of both the source (i.e. {@code this}) and the destination.
         *
         * @param dstIndex the first index of the destination
         * @param length   the number of bytes to transfer
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0},
         *         if the specified {@code dstIndex} is less than {@code 0},
         *         if {@code index + length} is greater than
         *            {@code this.capacity}, or
         *         if {@code dstIndex + length} is greater than
         *            {@code dst.capacity}
         */
    public ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.SetBytes(destinationIndex, this, index, length);
        return this;
    }
    /**
         * Transfers the specified source buffer's data to this buffer starting at
         * the specified absolute {@code index}.
         * This method does not modify {@code readerIndex} or {@code writerIndex}
         * of both the source (i.e. {@code this}) and the destination.
         *
         * @param srcIndex the first index of the source
         * @param length   the number of bytes to transfer
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0},
         *         if the specified {@code srcIndex} is less than {@code 0},
         *         if {@code index + length} is greater than
         *            {@code this.capacity}, or
         *         if {@code srcIndex + length} is greater than
         *            {@code src.capacity}
         */
    public ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureCanWrite(); CheckIndex(index, length); source.CheckIndex(sourceIndex, length);
        if (source.TryGetReadOnlyMemoryCore(sourceIndex, length, out var input) &&
            TryGetMemoryCore(index, length, out var output)) input.Span.CopyTo(output.Span);
        else
        {
            byte[] bytes = new byte[length];
            source.GetBytesCore(sourceIndex, bytes); SetBytesCore(index, bytes);
        }
        return this;
    }
    /**
         * Transfers this buffer's data to the specified destination starting at
         * the current {@code readerIndex} and increases the {@code readerIndex}
         * by the number of the transferred bytes (= {@code length}).  This method
         * is basically same with {@link #readBytes(ByteBuf, int, int)},
         * except that this method increases the {@code writerIndex} of the
         * destination by the number of the transferred bytes (= {@code length})
         * while {@link #readBytes(ByteBuf, int, int)} does not.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code length} is greater than {@code this.readableBytes} or
         *         if {@code length} is greater than {@code dst.writableBytes}
         */
    public ByteBuf ReadBytes(ByteBuf destination, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckReadableBytes(length);
        if (length > destination.WritableBytes) throw new ArgumentOutOfRangeException(nameof(length));
        GetBytes(_readerIndex, destination, destination._writerIndex, length);
        _readerIndex += length; destination._writerIndex += length;
        return this;
    }
    /**
         * Transfers the specified source buffer's data to this buffer starting at
         * the current {@code writerIndex} and increases the {@code writerIndex}
         * by the number of the transferred bytes (= {@code length}).  This method
         * is basically same with {@link #writeBytes(ByteBuf, int, int)},
         * except that this method increases the {@code readerIndex} of the source
         * buffer by the number of the transferred bytes (= {@code length}) while
         * {@link #writeBytes(ByteBuf, int, int)} does not.
         * If {@code this.writableBytes} is less than {@code length}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         *
         * @param length the number of bytes to transfer
         * @throws IndexOutOfBoundsException if {@code length} is greater then {@code src.readableBytes}
         */
    public ByteBuf WriteBytes(ByteBuf source, int length)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.CheckReadableBytes(length);
        EnsureWritable(length);
        SetBytes(_writerIndex, source, source._readerIndex, length);
        _writerIndex += length; source._readerIndex += length;
        return this;
    }
    /**
         * Fills this buffer with <tt>NUL (0x00)</tt> starting at the specified
         * absolute {@code index}.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @param length the number of <tt>NUL</tt>s to write to the buffer
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         if {@code index + length} is greater than {@code this.capacity}
         */
    public ByteBuf SetZero(int index, int length) { EnsureCanWrite(); CheckIndex(index, length); SetZeroCore(index, length); return this; }
    /**
         * Fills this buffer with <tt>NUL (0x00)</tt> starting at the current
         * {@code writerIndex} and increases the {@code writerIndex} by the
         * specified {@code length}.
         * If {@code this.writableBytes} is less than {@code length}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         *
         * @param length the number of <tt>NUL</tt>s to write to the buffer
         */
    public ByteBuf WriteZero(int length)
    { EnsureWritable(length); SetZero(_writerIndex, length); _writerIndex += length; return this; }
    /**
         * Returns a copy of this buffer's readable bytes.  Modifying the content
         * of the returned buffer or this buffer does not affect each other at all.
         * This method is identical to {@code buf.copy(buf.readerIndex(), buf.readableBytes())}.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         */
    public ByteBuf Copy() => Copy(_readerIndex, ReadableBytes);
    /**
         * Returns a copy of this buffer's sub-region.  Modifying the content of
         * the returned buffer or this buffer does not affect each other at all.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         */
    public virtual ByteBuf Copy(int index, int length)
    {
        CheckIndex(index, length);
        ByteBuf result = Unpooled.Buffer(length, MaxCapacity);
        try { result.SetBytes(0, this, index, length); result.WriterIndex = length; return result; }
        catch { result.Release(); throw; }
    }
    /**
         * Transfers this buffer's data to a newly created buffer starting at
         * the current {@code readerIndex} and increases the {@code readerIndex}
         * by the number of the transferred bytes (= {@code length}).
         * The returned buffer's {@code readerIndex} and {@code writerIndex} are
         * {@code 0} and {@code length} respectively.
         *
         * @param length the number of bytes to transfer
         *
         * @return the newly created buffer which contains the transferred bytes
         *
         * @throws IndexOutOfBoundsException
         *         if {@code length} is greater than {@code this.readableBytes}
         */
    public ByteBuf ReadBytes(int length)
    {
        CheckReadableBytes(length);
        if (length == 0) return Unpooled.EmptyBuffer;
        ByteBuf result = new UnpooledHeapByteBuf(length, MaxCapacity);
        try { result.SetBytes(0, this, _readerIndex, length); result.WriterIndex = length; }
        catch { result.Release(); throw; }
        _readerIndex += length;
        return result;
    }
    /**
         * Returns a slice of this buffer's readable bytes. Modifying the content
         * of the returned buffer or this buffer affects each other's content
         * while they maintain separate indexes and marks.  This method is
         * identical to {@code buf.slice(buf.readerIndex(), buf.readableBytes())}.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         * <p>
         * Also be aware that this method will NOT call {@link #retain()} and so the
         * reference count will NOT be increased.
         */
    public virtual ByteBuf Slice() => Slice(_readerIndex, ReadableBytes);
    /**
         * Returns a slice of this buffer's sub-region. Modifying the content of
         * the returned buffer or this buffer affects each other's content while
         * they maintain separate indexes and marks.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         * <p>
         * Also be aware that this method will NOT call {@link #retain()} and so the
         * reference count will NOT be increased.
         */
    public virtual ByteBuf Slice(int index, int length)
    { CheckIndex(index, length); return new ByteBufView(this, index, length); }
    /**
         * Returns a retained slice of this buffer's readable bytes. Modifying the content
         * of the returned buffer or this buffer affects each other's content
         * while they maintain separate indexes and marks.  This method is
         * identical to {@code buf.slice(buf.readerIndex(), buf.readableBytes())}.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         * <p>
         * Note that this method returns a {@linkplain #retain() retained} buffer unlike {@link #slice()}.
         * This method behaves similarly to {@code slice().retain()} except that this method may return
         * a buffer implementation that produces less garbage.
         */
    public virtual ByteBuf RetainedSlice() => Slice().Retain();
    /**
         * Returns a retained slice of this buffer's sub-region. Modifying the content of
         * the returned buffer or this buffer affects each other's content while
         * they maintain separate indexes and marks.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         * <p>
         * Note that this method returns a {@linkplain #retain() retained} buffer unlike {@link #slice(int, int)}.
         * This method behaves similarly to {@code slice(...).retain()} except that this method may return
         * a buffer implementation that produces less garbage.
         */
    public virtual ByteBuf RetainedSlice(int index, int length) => Slice(index, length).Retain();
    /**
         * Returns a buffer which shares the whole region of this buffer.
         * Modifying the content of the returned buffer or this buffer affects
         * each other's content while they maintain separate indexes and marks.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         * <p>
         * The reader and writer marks will not be duplicated. Also be aware that this method will
         * NOT call {@link #retain()} and so the reference count will NOT be increased.
         * @return A buffer whose readable content is equivalent to the buffer returned by {@link #slice()}.
         * However this buffer will share the capacity of the underlying buffer, and therefore allows access to all of the
         * underlying content if necessary.
         */
    public virtual ByteBuf Duplicate()
    { EnsureAccessible(); return new ByteBufView(this); }
    /**
         * Returns a retained buffer which shares the whole region of this buffer.
         * Modifying the content of the returned buffer or this buffer affects
         * each other's content while they maintain separate indexes and marks.
         * This method is identical to {@code buf.slice(0, buf.capacity())}.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         * <p>
         * Note that this method returns a {@linkplain #retain() retained} buffer unlike {@link #slice(int, int)}.
         * This method behaves similarly to {@code duplicate().retain()} except that this method may return
         * a buffer implementation that produces less garbage.
         */
    public virtual ByteBuf RetainedDuplicate() => Duplicate().Retain();
    /**
         * Returns a new slice of this buffer's sub-region starting at the current
         * {@code readerIndex} and increases the {@code readerIndex} by the size
         * of the new slice (= {@code length}).
         * <p>
         * Also be aware that this method will NOT call {@link #retain()} and so the
         * reference count will NOT be increased.
         *
         * @param length the size of the new slice
         *
         * @return the newly created slice
         *
         * @throws IndexOutOfBoundsException
         *         if {@code length} is greater than {@code this.readableBytes}
         */
    public virtual ByteBuf ReadSlice(int length)
    { CheckReadableBytes(length); ByteBuf result = Slice(_readerIndex, length); _readerIndex += length; return result; }
    /**
         * Returns a new retained slice of this buffer's sub-region starting at the current
         * {@code readerIndex} and increases the {@code readerIndex} by the size
         * of the new slice (= {@code length}).
         * <p>
         * Note that this method returns a {@linkplain #retain() retained} buffer unlike {@link #readSlice(int)}.
         * This method behaves similarly to {@code readSlice(...).retain()} except that this method may return
         * a buffer implementation that produces less garbage.
         *
         * @param length the size of the new slice
         *
         * @return the newly created slice
         *
         * @throws IndexOutOfBoundsException
         *         if {@code length} is greater than {@code this.readableBytes}
         */
    public virtual ByteBuf ReadRetainedSlice(int length)
    { CheckReadableBytes(length); ByteBuf result = RetainedSlice(_readerIndex, length); _readerIndex += length; return result; }
}
