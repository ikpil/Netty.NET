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

/*
 * Copyright 2015 The Netty Project
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

/*
 * Copyright 2025 The Netty Project
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
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Microsoft.Win32.SafeHandles;

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
/**
 * All operations get and set as {@link ByteOrder#BIG_ENDIAN}.
 */
// CLR: the helper's default wire order is BE; explicit LE methods share bounded scalar access.
/**
 * A skeletal implementation of a buffer.
 */
// CLR: ByteBuf combines the original abstract contract and its skeletal implementation.
public abstract class ByteBuf : IReferenceCounted, IByteBufConvertible, IEquatable<ByteBuf>, IComparable<ByteBuf>
{
    // A composite's physical contiguous borrow does not substitute for its logical bulk kernel.
    internal virtual bool SupportsDirectBulkCopy => true;
    internal virtual ReadOnlySequence<byte> BorrowCopySequence(int index, int length)
    {
        CheckIndex(index, length);

        return GetReadOnlySequenceCore(index, length);
    }

    internal static bool BulkRangesOverlap(ByteBuf source, int sourceIndex, ByteBuf destination, int destinationIndex, int length)
    {
        if (length == 0)
        {
            return false;
        }
        if (ReferenceEquals(source, destination))
        {
            return true;
        }

        // Inspect storage without public access records. No span survives a component callback.
        ReadOnlySequence<byte> input = source.BorrowCopySequence(sourceIndex, length);
        ReadOnlySequence<byte> output = destination.BorrowCopySequence(destinationIndex, length);

        foreach (ReadOnlyMemory<byte> a in input)
        foreach (ReadOnlyMemory<byte> b in output)
            if (a.Span.Overlaps(b.Span))
                {
                    return true;
                }

        return false;
    }

/**
     * Returns a hash code which was calculated from the content of this
     * buffer.  If there's a byte array which is
     * {@linkplain #equals(Object) equal to} this array, both arrays should
     * return the same value.
     */
    /// <remarks>Depends on the current readable bytes. Do not change content, indices or
    /// lifetime while the buffer is a key in a hash-based collection.</remarks>
    public override int GetHashCode()
    {
        return ByteBufUtil.HashCode(this);
    }

/**
     * Determines if the content of the specified buffer is identical to the
     * content of this array.  'Identical' here means:
     * <ul>
     * <li>the size of the contents of the two buffers are same and</li>
     * <li>every single byte of the content of the two buffers are same.</li>
     * </ul>
     * Please note that it does not compare {@link #readerIndex()} nor
     * {@link #writerIndex()}.  This method also returns {@code false} for
     * {@code null} and an object which is not an instance of
     * {@link ByteBuf} type.
     */
    public override bool Equals(object obj)
    {
        return obj is ByteBuf other && Equals(other);
    }

    /// <summary>Compares the current readable content, regardless of storage or absolute indices.</summary>
    public virtual bool Equals(ByteBuf other)
    {
        return other is not null && ByteBufUtil.Equals(this, other);
    }

/**
     * Compares the content of the specified buffer to the content of this
     * buffer. Comparison is performed in the same manner with the string
     * comparison functions of various languages such as {@code strcmp},
     * {@code memcmp} and {@link String#compareTo(String)}.
     */
    /// <remarks>As required by IComparable, a non-null buffer sorts after null.
    /// Comparison otherwise follows ByteBufUtil.Compare over the readable bytes.</remarks>
    public virtual int CompareTo(ByteBuf other)
    {
        return other is null ? 1 : ByteBufUtil.Compare(this, other);
    }

    // CLR: AbstractByteBuf's shared detector lives on the combined skeletal ByteBuf.
    // Capture the configured factory at type initialization, before any buffer is created.
    internal static readonly ResourceLeakDetector<ByteBuf> LeakDetector =
        ResourceLeakDetectorFactory.Instance().NewResourceLeakDetector<ByteBuf>(typeof(ByteBuf));
    static ByteBuf()
    {
    }

    /**
     * A {@code ByteBuf} can turn into itself.
     * @return This {@code ByteBuf} instance.
     */
    public ByteBuf AsByteBuf()
    {
        return this;
    }

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

    /**
     * Returns the {@link ByteBufAllocator} which created this buffer.
     */
    public virtual IByteBufAllocator Alloc()
    {
        return UnpooledByteBufAllocator.Default;
    }

    protected abstract Memory<byte> GetMemoryCore(int index, int length);

    protected virtual ReadOnlyMemory<byte> GetReadOnlyMemoryCore(int index, int length)
    {
        return GetMemoryCore(index, length);
    }

    // A growing span write may read this buffer's old allocation. Storage leases
    // preserve native owners and defer pooled region reuse across replacement.
    internal virtual BufferMemoryLease PinMemoryForWrite()
    {
        EnsureCanWrite();

        return AcquireReadLease();
    }
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
     * Returns {@code true} if this {@link ByteBuf} implementation is backed by a single memory region.
     * Composite buffer implementations must return false even if they currently hold &le; 1 components.
     * For buffers that return {@code true}, it's guaranteed that a successful call to {@link #discardReadBytes()}
     * will increase the value of {@link #maxFastWritableBytes()} by the current {@code readerIndex}.
     * <p>
     * This method will return {@code false} by default, and a {@code false} return value does not necessarily
     * mean that the implementation is composite or that it is <i>not</i> backed by a single memory region.
     */
    public virtual bool IsContiguous => false;
    /**
         * Return the underlying buffer instance if this buffer is a wrapper of another buffer.
         *
         * @return {@code null} if this buffer is not a wrapper
         */
    public virtual ByteBuf Unwrap()
    {
        return null;
    }
    public abstract int RefCnt { get; }
    public abstract ByteBuf Retain(int increment = 1);

    public abstract bool Release(int decrement = 1);

    public virtual ByteBuf Touch(object hint = null)
    {
        return this;
    }

    IReferenceCounted IReferenceCounted.Retain()
    {
        return Retain();
    }

    IReferenceCounted IReferenceCounted.Retain(int increment)
    {
        return Retain(increment);
    }

    IReferenceCounted IReferenceCounted.Touch()
    {
        return Touch();
    }

    IReferenceCounted IReferenceCounted.Touch(object hint)
    {
        return Touch(hint);
    }

    bool IReferenceCounted.Release()
    {
        return Release();
    }

    bool IReferenceCounted.Release(int decrement)
    {
        return Release(decrement);
    }

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
        set
        {
            CheckIndices(value, _writerIndex);
            _readerIndex = value;
        }
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
        set
        {
            CheckIndices(_readerIndex, value);
            _writerIndex = value;
        }
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
     * Returns the maximum number of bytes which can be written for certain without involving
     * an internal reallocation or data-copy. The returned value will be &ge; {@link #writableBytes()}
     * and &le; {@link #maxWritableBytes()}.
     */
    /// <remarks>This is a storage-capacity hint, not permission to write to a read-only buffer.
    /// Current unpooled owners/views reserve no extra hidden capacity. Transparent wrappers delegate this hint.</remarks>
    public virtual int MaxFastWritableBytes => WritableBytes;
    /**
         * Returns {@code true}
         * if and only if {@code (this.writerIndex - this.readerIndex)} is greater
         * than {@code 0}.
         */
    public bool IsReadable()
    {
        return _writerIndex > _readerIndex;
    }

    /**
     * Returns {@code true} if and only if this buffer contains equal to or more than the specified number of elements.
     */
    /// <remarks>Queries readable indices without accessing content, retaining, or checking accessibility.
    /// Like Netty's isReadable(int), a nonpositive count succeeds on ordinary buffers.
    /// The shared empty sentinel always returns false.</remarks>
    public virtual bool IsReadable(int size)
    {
        return ReadableBytes >= size;
    }

    /**
         * Returns {@code true}
         * if and only if {@code (this.capacity - this.writerIndex)} is greater
         * than {@code 0}.
         */
    public bool IsWritable()
    {
        return !IsReadOnly && Capacity > _writerIndex;
    }

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
        _readerIndex = readerIndex;
        _writerIndex = writerIndex;
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
    public ByteBuf Clear()
    {
        _readerIndex = _writerIndex = 0;
        return this;
    }

    /**
         * Marks the current {@code readerIndex} in this buffer.  You can
         * reposition the current {@code readerIndex} to the marked
         * {@code readerIndex} by calling {@link #resetReaderIndex()}.
         * The initial value of the marked {@code readerIndex} is {@code 0}.
         */
    public ByteBuf MarkReaderIndex()
    {
        _markedReaderIndex = _readerIndex;
        return this;
    }

    /**
         * Marks the current {@code writerIndex} in this buffer.  You can
         * reposition the current {@code writerIndex} to the marked
         * {@code writerIndex} by calling {@link #resetWriterIndex()}.
         * The initial value of the marked {@code writerIndex} is {@code 0}.
         */
    public ByteBuf MarkWriterIndex()
    {
        _markedWriterIndex = _writerIndex;
        return this;
    }

    /**
         * Repositions the current {@code readerIndex} to the marked
         * {@code readerIndex} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the current {@code writerIndex} is less than the marked
         *         {@code readerIndex}
         */
    public ByteBuf ResetReaderIndex()
    {
        ReaderIndex = _markedReaderIndex;
        return this;
    }

    /**
         * Repositions the current {@code writerIndex} to the marked
         * {@code writerIndex} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the current {@code readerIndex} is greater than the marked
         *         {@code writerIndex}
         */
    public ByteBuf ResetWriterIndex()
    {
        WriterIndex = _markedWriterIndex;
        return this;
    }

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
        if (_readerIndex == 0)
        {
            return this;
        }

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
        {
            // Preserve AbstractByteBuf's setBytes hook; discardReadBytes is an independent virtual operation.
            SetBytes(0, this, _readerIndex, _writerIndex - _readerIndex);
            _writerIndex -= _readerIndex;
            AdjustMarkers(_readerIndex);
            _readerIndex = 0;
        }

        return this;
    }

    protected void AdjustMarkers(int decrement)
    {
        if (_markedReaderIndex <= decrement)
        {
            _markedReaderIndex = 0;
            _markedWriterIndex = Math.Max(0, _markedWriterIndex - decrement);
        }
        else
        {
            _markedReaderIndex -= decrement;
            _markedWriterIndex -= decrement;
        }
    }

    // Called after a capacity reduction
    protected void TrimIndicesToCapacity(int newCapacity)
    {
        if (_writerIndex > newCapacity)
        {
            _readerIndex = Math.Min(_readerIndex, newCapacity);
            _writerIndex = newCapacity;
        }
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
    public virtual ByteBuf EnsureWritable(int minimumWritableBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimumWritableBytes);

        EnsureWritableCore(minimumWritableBytes);
        return this;
    }

    // AbstractByteBuf.ensureWritable0 is final: primitive writes must not call an independent public override.
    private void EnsureWritableCore(int minimumWritableBytes)
    {
        EnsureCanWrite();
        // using non-short-circuit & to reduce branching - this is a hot path and targetCapacity should rarely overflow
        // CLR: subtraction-based bounds avoid overflowing targetCapacity, including in checked builds.
        if (minimumWritableBytes <= WritableBytes)
        {
            return;
        }
        if (minimumWritableBytes > MaxCapacity - _writerIndex)
            throw new ArgumentOutOfRangeException(nameof(minimumWritableBytes));

        // Normalize the target capacity to the power of 2.
        int fastWritable = MaxFastWritableBytes;
        int newCapacity = fastWritable >= minimumWritableBytes ? _writerIndex + fastWritable
            : Alloc().CalculateNewCapacity(_writerIndex + minimumWritableBytes, MaxCapacity);
        // Adjust to the new capacity.
        Capacity = newCapacity;
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
    public virtual int EnsureWritable(int minimumWritableBytes, bool force)
    {
        EnsureAccessible();
        ArgumentOutOfRangeException.ThrowIfNegative(minimumWritableBytes);
        if (IsReadOnly)
        {
            return 1;
        }
        if (minimumWritableBytes <= WritableBytes)
        {
            return 0;
        }

        if (minimumWritableBytes > MaxCapacity - _writerIndex)
        {
            if (!force || Capacity == MaxCapacity)
            {
                return 1;
            }

            Capacity = MaxCapacity;
            return 3;
        }

        int fastWritable = MaxFastWritableBytes;
        int newCapacity = fastWritable >= minimumWritableBytes ? _writerIndex + fastWritable
            : Alloc().CalculateNewCapacity(_writerIndex + minimumWritableBytes, MaxCapacity);
        // Adjust to the new capacity.
        Capacity = newCapacity;
        return 2;
    }

    // AbstractByteBufAllocator.calculateNewCapacity: same 64-byte / 4-MiB growth policy.
    internal static int CalculateNewCapacity(int minimum, int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimum);
        if (minimum > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum));
        }

        const int threshold = 4 * 1024 * 1024; // 4 MiB page
        if (minimum == threshold)
        {
            return threshold;
        }

        // If over threshold, do not double but just increase by threshold.
        if (minimum > threshold)
        {
            int capacity = minimum / threshold * threshold;
            return capacity > maximum - threshold ? maximum : capacity + threshold;
        }

        // 64 <= newCapacity is a power of 2 <= threshold
        int rounded = 64;

        while (rounded < minimum)
        {
            rounded <<= 1;
        }

        return Math.Min(rounded, maximum);
    }

    /**
     * Should be called by every method that tries to access the buffers content to check
     * if the buffer was released before.
     */
    // CLR range/lifetime checks stay enabled; JVM opt-out properties are not applied.
    protected void EnsureAccessible()
    {
        if (RefCnt == 0)
        {
            throw new IllegalReferenceCountException(0);
        }
    }

    protected void CheckNewCapacity(int capacity)
    {
        EnsureAccessible();
        if (capacity < 0 || capacity > MaxCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));
    }

    /**
     * Throws an {@link IndexOutOfBoundsException} if the current
     * {@linkplain #readableBytes() readable bytes} of this buffer is less
     * than the specified value.
     */
    protected void CheckReadableBytes(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        EnsureAccessible();
        if (length > ReadableBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }
    }

    protected internal void CheckIndex(int index, int length)
    {
        EnsureAccessible();
        // Original trusted-capacity helper notes:
        /**
     * This is a simplified version of MathUtil.isOutOfBounds that does not check for capacity negative values.
     */
        // keep these as branches since would make it easier to be constant-folded
        // CLR: subtraction avoids signed index + length overflow, including in checked builds.
        if (index < 0 || length < 0 || index > Capacity - length)
            throw new ArgumentOutOfRangeException(nameof(index));
    }

    /// <summary>Returns writable borrowed memory for a contiguous range.</summary>
    /// <remarks>Keep the buffer retained and its storage unchanged for the duration of the borrow.
    /// Reusable adaptive and thread-local memory views do not support caller-created pins.</remarks>
    /// <exception cref="NotSupportedException">The range is read-only or spans
    /// multiple components. Use SetBytes or consolidate for segmented writes.</exception>
    public virtual Memory<byte> AsMemory(int index, int length)
    {
        EnsureCanWrite();
        CheckIndex(index, length);

        return GetMemoryCore(index, length);
    }

    public virtual Span<byte> AsSpan(int index, int length)
    {
        return AsMemory(index, length).Span;
    }
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
    /**
     * Gets an unsigned byte at the specified absolute {@code index} in this
     * buffer.  This method does not modify {@code readerIndex} or
     * {@code writerIndex} of this buffer.
     *
     * @throws IndexOutOfBoundsException
     *         if the specified {@code index} is less than {@code 0} or
     *         {@code index + 1} is greater than {@code this.capacity}
     */
    // CLR: byte already represents 0..255, so signed/unsigned Java octet APIs share this method.
    /// <remarks>Use unchecked((sbyte)value) when a signed Java byte value is required.</remarks>
    public virtual byte GetByte(int index)
    {
        return ReadByteCore(index);
    }

    /**
         * Gets a boolean at the specified absolute (@code index) in this buffer.
         * This method does not modify the {@code readerIndex} or {@code writerIndex}
         * of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 1} is greater than {@code this.capacity}
         */
    public virtual bool GetBoolean(int index)
    {
        return GetByte(index) != 0;
    }

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
    public virtual ByteBuf SetByte(int index, int value)
    {
        WriteByteCore(index, value);
        return this;
    }

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
    public virtual ByteBuf SetBoolean(int index, bool value)
    {
        SetByte(index, value ? 1 : 0);
        return this;
    }

    /**
         * Gets a 16-bit short integer at the specified absolute {@code index} in
         * this buffer.  This method does not modify {@code readerIndex} or
         * {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 2} is greater than {@code this.capacity}
         */
    public virtual short GetShort(int index)
    {
        return unchecked((short)ReadWord(index, 2, false));
    }

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
    public virtual ByteBuf SetShort(int index, int value)
    {
        return SetWord(index, unchecked((ulong)value), 2, false);
    }

    /**
         * Gets a 16-bit short integer at the specified absolute {@code index} in
         * this buffer in Little Endian Byte Order. This method does not modify
         * {@code readerIndex} or {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 2} is greater than {@code this.capacity}
         */
    public virtual short GetShortLE(int index)
    {
        return unchecked((short)ReadWord(index, 2, true));
    }

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
    public virtual ByteBuf SetShortLE(int index, int value)
    {
        return SetWord(index, unchecked((ulong)value), 2, true);
    }

    /**
         * Gets a 32-bit integer at the specified absolute {@code index} in
         * this buffer.  This method does not modify {@code readerIndex} or
         * {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 4} is greater than {@code this.capacity}
         */
    public virtual int GetInt(int index)
    {
        return unchecked((int)ReadWord(index, 4, false));
    }

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
    public virtual ByteBuf SetInt(int index, int value)
    {
        return SetWord(index, unchecked((ulong)value), 4, false);
    }

    /**
         * Gets a 32-bit integer at the specified absolute {@code index} in
         * this buffer with Little Endian Byte Order. This method does not
         * modify {@code readerIndex} or {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 4} is greater than {@code this.capacity}
         */
    public virtual int GetIntLE(int index)
    {
        return unchecked((int)ReadWord(index, 4, true));
    }

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
    public virtual ByteBuf SetIntLE(int index, int value)
    {
        return SetWord(index, unchecked((ulong)value), 4, true);
    }

    /**
         * Gets a 64-bit long integer at the specified absolute {@code index} in
         * this buffer.  This method does not modify {@code readerIndex} or
         * {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 8} is greater than {@code this.capacity}
         */
    public virtual long GetLong(int index)
    {
        return unchecked((long)ReadWord(index, 8, false));
    }

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
    public virtual ByteBuf SetLong(int index, long value)
    {
        return SetWord(index, unchecked((ulong)value), 8, false);
    }

    /**
         * Gets a 64-bit long integer at the specified absolute {@code index} in
         * this buffer in Little Endian Byte Order. This method does not
         * modify {@code readerIndex} or {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 8} is greater than {@code this.capacity}
         */
    public virtual long GetLongLE(int index)
    {
        return unchecked((long)ReadWord(index, 8, true));
    }

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
    public virtual ByteBuf SetLongLE(int index, long value)
    {
        return SetWord(index, unchecked((ulong)value), 8, true);
    }

    /**
         * Gets an unsigned 24-bit medium integer at the specified absolute
         * {@code index} in this buffer.  This method does not modify
         * {@code readerIndex} or {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 3} is greater than {@code this.capacity}
         */
    public virtual int GetUnsignedMedium(int index)
    {
        return (int)ReadWord(index, 3, false);
    }

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
    public virtual int GetUnsignedMediumLE(int index)
    {
        return (int)ReadWord(index, 3, true);
    }

    /**
         * Gets a 24-bit medium integer at the specified absolute {@code index} in
         * this buffer.  This method does not modify {@code readerIndex} or
         * {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 3} is greater than {@code this.capacity}
         */
    public virtual int GetMedium(int index)
    {
        return GetUnsignedMedium(index) << 8 >> 8;
    }

    /**
         * Gets a 24-bit medium integer at the specified absolute {@code index} in
         * this buffer in the Little Endian Byte Order. This method does not
         * modify {@code readerIndex} or {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 3} is greater than {@code this.capacity}
         */
    public virtual int GetMediumLE(int index)
    {
        return GetUnsignedMediumLE(index) << 8 >> 8;
    }

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
    public virtual ByteBuf SetMedium(int index, int value)
    {
        return SetWord(index, unchecked((ulong)value), 3, false);
    }

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
    public virtual ByteBuf SetMediumLE(int index, int value)
    {
        return SetWord(index, unchecked((ulong)value), 3, true);
    }

    /**
         * Gets an unsigned 16-bit short integer at the specified absolute
         * {@code index} in this buffer.  This method does not modify
         * {@code readerIndex} or {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 2} is greater than {@code this.capacity}
         */
    public virtual ushort GetUnsignedShort(int index)
    {
        return unchecked((ushort)GetShort(index));
    }

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
    public virtual ushort GetUnsignedShortLE(int index)
    {
        return unchecked((ushort)GetShortLE(index));
    }

    /**
         * Gets an unsigned 32-bit integer at the specified absolute {@code index}
         * in this buffer.  This method does not modify {@code readerIndex} or
         * {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 4} is greater than {@code this.capacity}
         */
    public virtual uint GetUnsignedInt(int index)
    {
        return unchecked((uint)GetInt(index));
    }

    /**
         * Gets an unsigned 32-bit integer at the specified absolute {@code index}
         * in this buffer in Little Endian Byte Order. This method does not
         * modify {@code readerIndex} or {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 4} is greater than {@code this.capacity}
         */
    public virtual uint GetUnsignedIntLE(int index)
    {
        return unchecked((uint)GetIntLE(index));
    }

    /**
         * Gets a 2-byte UTF-16 character at the specified absolute
         * {@code index} in this buffer.  This method does not modify
         * {@code readerIndex} or {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 2} is greater than {@code this.capacity}
         */
    public virtual char GetChar(int index)
    {
        return unchecked((char)GetShort(index));
    }

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
    public virtual ByteBuf SetChar(int index, char value)
    {
        SetShort(index, value);
        return this;
    }

    /**
         * Gets a 32-bit floating point number at the specified absolute
         * {@code index} in this buffer.  This method does not modify
         * {@code readerIndex} or {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 4} is greater than {@code this.capacity}
         */
    public virtual float GetFloat(int index)
    {
        return BitConverter.Int32BitsToSingle(GetInt(index));
    }

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
    public virtual float GetFloatLE(int index)
    {
        return BitConverter.Int32BitsToSingle(GetIntLE(index));
    }

    /**
         * Gets a 64-bit floating point number at the specified absolute
         * {@code index} in this buffer.  This method does not modify
         * {@code readerIndex} or {@code writerIndex} of this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if the specified {@code index} is less than {@code 0} or
         *         {@code index + 8} is greater than {@code this.capacity}
         */
    public virtual double GetDouble(int index)
    {
        return BitConverter.Int64BitsToDouble(GetLong(index));
    }

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
    public virtual double GetDoubleLE(int index)
    {
        return BitConverter.Int64BitsToDouble(GetLongLE(index));
    }

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
    public virtual ByteBuf SetFloat(int index, float value)
    {
        SetInt(index, BitConverter.SingleToInt32Bits(value));
        return this;
    }

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
    public virtual ByteBuf SetFloatLE(int index, float value)
    {
        return SetIntLE(index, BitConverter.SingleToInt32Bits(value));
    }

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
    public virtual ByteBuf SetDouble(int index, double value)
    {
        SetLong(index, BitConverter.DoubleToInt64Bits(value));
        return this;
    }

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
    public virtual ByteBuf SetDoubleLE(int index, double value)
    {
        return SetLongLE(index, BitConverter.DoubleToInt64Bits(value));
    }

    /**
         * Gets a byte at the current {@code readerIndex} and increases
         * the {@code readerIndex} by {@code 1} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 1}
         */
    /**
     * Gets an unsigned byte at the current {@code readerIndex} and increases
     * the {@code readerIndex} by {@code 1} in this buffer.
     *
     * @throws IndexOutOfBoundsException
     *         if {@code this.readableBytes} is less than {@code 1}
     */
    // CLR: byte already represents 0..255, so signed/unsigned Java octet APIs share this method.
    /// <remarks>Use unchecked((sbyte)value) when a signed Java byte value is required.</remarks>
    // Relative primitives follow AbstractByteBuf storage kernels, bypassing public
    // absolute overrides. Floating-point and signed-medium methods compose the
    // corresponding relative integer APIs, preserving their override policy.
    public virtual byte ReadByte()
    {
        CheckReadableBytes(1);

        byte value = ReadByteCore(_readerIndex);
        ++_readerIndex;
        return value;
    }

    /**
         * Sets the specified byte at the current {@code writerIndex}
         * and increases the {@code writerIndex} by {@code 1} in this buffer.
         * The 24 high-order bits of the specified value are ignored.
         * If {@code this.writableBytes} is less than {@code 1}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public virtual ByteBuf WriteByte(int value)
    {
        EnsureWritableCore(1);
        WriteByteCore(_writerIndex, value);
        ++_writerIndex;
        return this;
    }

    /**
         * Gets a boolean at the current {@code readerIndex} and increases
         * the {@code readerIndex} by {@code 1} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 1}
         */
    public virtual bool ReadBoolean()
    {
        return ReadByte() != 0;
    }

    /**
         * Sets the specified boolean at the current {@code writerIndex}
         * and increases the {@code writerIndex} by {@code 1} in this buffer.
         * If {@code this.writableBytes} is less than {@code 1}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public virtual ByteBuf WriteBoolean(bool value)
    {
        WriteByte(value ? 1 : 0);
        return this;
    }

    /**
         * Gets a 16-bit short integer at the current {@code readerIndex}
         * and increases the {@code readerIndex} by {@code 2} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 2}
         */
    public virtual short ReadShort()
    {
        CheckReadableBytes(2);

        short value = unchecked((short)ReadWord(_readerIndex, 2, false));
        _readerIndex += 2;
        return value;
    }

    /**
         * Sets the specified 16-bit short integer at the current
         * {@code writerIndex} and increases the {@code writerIndex} by {@code 2}
         * in this buffer.  The 16 high-order bits of the specified value are ignored.
         * If {@code this.writableBytes} is less than {@code 2}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public virtual ByteBuf WriteShort(int value)
    {
        EnsureWritableCore(2);
        SetWord(_writerIndex, unchecked((ulong)value), 2, false);
        _writerIndex += 2;
        return this;
    }

    /**
         * Gets a 16-bit short integer at the current {@code readerIndex}
         * in the Little Endian Byte Order and increases the {@code readerIndex}
         * by {@code 2} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 2}
         */
    public virtual short ReadShortLE()
    {
        CheckReadableBytes(2);

        short value = unchecked((short)ReadWord(_readerIndex, 2, true));
        _readerIndex += 2;
        return value;
    }

    /**
         * Sets the specified 16-bit short integer in the Little Endian Byte
         * Order at the current {@code writerIndex} and increases the
         * {@code writerIndex} by {@code 2} in this buffer.
         * The 16 high-order bits of the specified value are ignored.
         * If {@code this.writableBytes} is less than {@code 2}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public virtual ByteBuf WriteShortLE(int value)
    {
        EnsureWritableCore(2);
        SetWord(_writerIndex, unchecked((ulong)value), 2, true);
        _writerIndex += 2;
        return this;
    }

    /**
         * Gets a 24-bit medium integer at the current {@code readerIndex}
         * and increases the {@code readerIndex} by {@code 3} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 3}
         */
    public virtual int ReadMedium()
    {
        return ReadUnsignedMedium() << 8 >> 8;
    }

    /**
         * Sets the specified 24-bit medium integer at the current
         * {@code writerIndex} and increases the {@code writerIndex} by {@code 3}
         * in this buffer.
         * If {@code this.writableBytes} is less than {@code 3}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public virtual ByteBuf WriteMedium(int value)
    {
        EnsureWritableCore(3);
        SetWord(_writerIndex, unchecked((ulong)value), 3, false);
        _writerIndex += 3;
        return this;
    }

    /**
         * Gets a 24-bit medium integer at the current {@code readerIndex}
         * in the Little Endian Byte Order and increases the
         * {@code readerIndex} by {@code 3} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 3}
         */
    public virtual int ReadMediumLE()
    {
        return ReadUnsignedMediumLE() << 8 >> 8;
    }

    /**
         * Sets the specified 24-bit medium integer at the current
         * {@code writerIndex} in the Little Endian Byte Order and
         * increases the {@code writerIndex} by {@code 3} in this
         * buffer.
         * If {@code this.writableBytes} is less than {@code 3}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public virtual ByteBuf WriteMediumLE(int value)
    {
        EnsureWritableCore(3);
        SetWord(_writerIndex, unchecked((ulong)value), 3, true);
        _writerIndex += 3;
        return this;
    }

    /**
         * Gets a 32-bit integer at the current {@code readerIndex}
         * and increases the {@code readerIndex} by {@code 4} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 4}
         */
    public virtual int ReadInt()
    {
        CheckReadableBytes(4);

        int value = unchecked((int)ReadWord(_readerIndex, 4, false));
        _readerIndex += 4;
        return value;
    }

    /**
         * Sets the specified 32-bit integer at the current {@code writerIndex}
         * and increases the {@code writerIndex} by {@code 4} in this buffer.
         * If {@code this.writableBytes} is less than {@code 4}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public virtual ByteBuf WriteInt(int value)
    {
        EnsureWritableCore(4);
        SetWord(_writerIndex, unchecked((ulong)value), 4, false);
        _writerIndex += 4;
        return this;
    }

    /**
         * Gets a 32-bit integer at the current {@code readerIndex}
         * in the Little Endian Byte Order and increases the {@code readerIndex}
         * by {@code 4} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 4}
         */
    public virtual int ReadIntLE()
    {
        CheckReadableBytes(4);

        int value = unchecked((int)ReadWord(_readerIndex, 4, true));
        _readerIndex += 4;
        return value;
    }

    /**
         * Sets the specified 32-bit integer at the current {@code writerIndex}
         * in the Little Endian Byte Order and increases the {@code writerIndex}
         * by {@code 4} in this buffer.
         * If {@code this.writableBytes} is less than {@code 4}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public virtual ByteBuf WriteIntLE(int value)
    {
        EnsureWritableCore(4);
        SetWord(_writerIndex, unchecked((ulong)value), 4, true);
        _writerIndex += 4;
        return this;
    }

    /**
         * Gets a 64-bit integer at the current {@code readerIndex}
         * and increases the {@code readerIndex} by {@code 8} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 8}
         */
    public virtual long ReadLong()
    {
        CheckReadableBytes(8);

        long value = unchecked((long)ReadWord(_readerIndex, 8, false));
        _readerIndex += 8;
        return value;
    }

    /**
         * Sets the specified 64-bit long integer at the current
         * {@code writerIndex} and increases the {@code writerIndex} by {@code 8}
         * in this buffer.
         * If {@code this.writableBytes} is less than {@code 8}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public virtual ByteBuf WriteLong(long value)
    {
        EnsureWritableCore(8);
        SetWord(_writerIndex, unchecked((ulong)value), 8, false);
        _writerIndex += 8;
        return this;
    }

    /**
         * Gets a 64-bit integer at the current {@code readerIndex}
         * in the Little Endian Byte Order and increases the {@code readerIndex}
         * by {@code 8} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 8}
         */
    public virtual long ReadLongLE()
    {
        CheckReadableBytes(8);

        long value = unchecked((long)ReadWord(_readerIndex, 8, true));
        _readerIndex += 8;
        return value;
    }

    /**
         * Sets the specified 64-bit long integer at the current
         * {@code writerIndex} in the Little Endian Byte Order and
         * increases the {@code writerIndex} by {@code 8}
         * in this buffer.
         * If {@code this.writableBytes} is less than {@code 8}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public virtual ByteBuf WriteLongLE(long value)
    {
        EnsureWritableCore(8);
        SetWord(_writerIndex, unchecked((ulong)value), 8, true);
        _writerIndex += 8;
        return this;
    }

    /**
         * Gets a 32-bit floating point number at the current {@code readerIndex}
         * and increases the {@code readerIndex} by {@code 4} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 4}
         */
    public virtual float ReadFloat()
    {
        return BitConverter.Int32BitsToSingle(ReadInt());
    }

    /**
         * Sets the specified 32-bit floating point number at the current
         * {@code writerIndex} and increases the {@code writerIndex} by {@code 4}
         * in this buffer.
         * If {@code this.writableBytes} is less than {@code 4}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public virtual ByteBuf WriteFloat(float value)
    {
        WriteInt(BitConverter.SingleToInt32Bits(value));
        return this;
    }

    /**
         * Gets a 32-bit floating point number at the current {@code readerIndex}
         * in Little Endian Byte Order and increases the {@code readerIndex}
         * by {@code 4} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 4}
         */
    public virtual float ReadFloatLE()
    {
        return BitConverter.Int32BitsToSingle(ReadIntLE());
    }

    /**
         * Sets the specified 32-bit floating point number at the current
         * {@code writerIndex} in Little Endian Byte Order and increases
         * the {@code writerIndex} by {@code 4} in this buffer.
         * If {@code this.writableBytes} is less than {@code 4}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public virtual ByteBuf WriteFloatLE(float value)
    {
        return WriteIntLE(BitConverter.SingleToInt32Bits(value));
    }

    /**
         * Gets a 64-bit floating point number at the current {@code readerIndex}
         * and increases the {@code readerIndex} by {@code 8} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 8}
         */
    public virtual double ReadDouble()
    {
        return BitConverter.Int64BitsToDouble(ReadLong());
    }

    /**
         * Sets the specified 64-bit floating point number at the current
         * {@code writerIndex} and increases the {@code writerIndex} by {@code 8}
         * in this buffer.
         * If {@code this.writableBytes} is less than {@code 8}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public virtual ByteBuf WriteDouble(double value)
    {
        WriteLong(BitConverter.DoubleToInt64Bits(value));
        return this;
    }

    /**
         * Gets a 64-bit floating point number at the current {@code readerIndex}
         * in Little Endian Byte Order and increases the {@code readerIndex}
         * by {@code 8} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 8}
         */
    public virtual double ReadDoubleLE()
    {
        return BitConverter.Int64BitsToDouble(ReadLongLE());
    }

    /**
         * Sets the specified 64-bit floating point number at the current
         * {@code writerIndex} in Little Endian Byte Order and increases
         * the {@code writerIndex} by {@code 8} in this buffer.
         * If {@code this.writableBytes} is less than {@code 8}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public virtual ByteBuf WriteDoubleLE(double value)
    {
        return WriteLongLE(BitConverter.DoubleToInt64Bits(value));
    }

    /**
         * Gets an unsigned 16-bit short integer at the current {@code readerIndex}
         * and increases the {@code readerIndex} by {@code 2} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 2}
         */
    public virtual ushort ReadUnsignedShort()
    {
        return unchecked((ushort)ReadShort());
    }

    /**
         * Gets an unsigned 16-bit short integer at the current {@code readerIndex}
         * in the Little Endian Byte Order and increases the {@code readerIndex}
         * by {@code 2} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 2}
         */
    public virtual ushort ReadUnsignedShortLE()
    {
        return unchecked((ushort)ReadShortLE());
    }

    /**
         * Gets an unsigned 32-bit integer at the current {@code readerIndex}
         * and increases the {@code readerIndex} by {@code 4} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 4}
         */
    public virtual uint ReadUnsignedInt()
    {
        return unchecked((uint)ReadInt());
    }

    /**
         * Gets an unsigned 32-bit integer at the current {@code readerIndex}
         * in the Little Endian Byte Order and increases the {@code readerIndex}
         * by {@code 4} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 4}
         */
    public virtual uint ReadUnsignedIntLE()
    {
        return unchecked((uint)ReadIntLE());
    }

    /**
         * Gets an unsigned 24-bit medium integer at the current {@code readerIndex}
         * and increases the {@code readerIndex} by {@code 3} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 3}
         */
    public virtual int ReadUnsignedMedium()
    {
        CheckReadableBytes(3);

        int value = (int)ReadWord(_readerIndex, 3, false);
        _readerIndex += 3;
        return value;
    }

    /**
         * Gets an unsigned 24-bit medium integer at the current {@code readerIndex}
         * in the Little Endian Byte Order and increases the {@code readerIndex}
         * by {@code 3} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 3}
         */
    public virtual int ReadUnsignedMediumLE()
    {
        CheckReadableBytes(3);

        int value = (int)ReadWord(_readerIndex, 3, true);
        _readerIndex += 3;
        return value;
    }

    /**
         * Gets a 2-byte UTF-16 character at the current {@code readerIndex}
         * and increases the {@code readerIndex} by {@code 2} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code this.readableBytes} is less than {@code 2}
         */
    public virtual char ReadChar()
    {
        return unchecked((char)ReadShort());
    }

    /**
         * Sets the specified 2-byte UTF-16 character at the current
         * {@code writerIndex} and increases the {@code writerIndex} by {@code 2}
         * in this buffer.  The 16 high-order bits of the specified value are ignored.
         * If {@code this.writableBytes} is less than {@code 2}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    public virtual ByteBuf WriteChar(char value)
    {
        WriteShort(value);
        return this;
    }

    /**
         * Increases the current {@code readerIndex} by the specified
         * {@code length} in this buffer.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code length} is greater than {@code this.readableBytes}
         */
    public virtual ByteBuf SkipBytes(int length)
    {
        CheckReadableBytes(length);

        _readerIndex += length;
        return this;
    }

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
    /**
     * Transfers this buffer's data to the specified destination starting at
     * the specified absolute {@code index}.
     * This method does not modify {@code readerIndex} or {@code writerIndex}
     * of this buffer.
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
     *            {@code dst.length}
     */
    // CLR: The original array range overload above uses array.AsSpan(offset, length);
    // range creation validates array bounds before this method is invoked.
    // CLR: The CLR destination is an explicit span range with no writer index.
    public virtual ByteBuf GetBytes(int index, Span<byte> destination)
    {
        CheckIndex(index, destination.Length);

        // We need to check if dst is ready-only so we not write something in it by using Unsafe.
        // Copy to direct memory
        // Copy to array
        // CLR: Span<byte> represents a writable destination of either storage kind;
        // ByteBuf destinations separately enforce their read-only guard in SetBytes.
        if (TryGetReadOnlyMemoryCore(index, destination.Length, out var memory))
        {
            memory.Span.CopyTo(destination);
        }
        else
        {
            // Snapshot segmented input before a possibly aliased destination is modified.
            byte[] bytes = new byte[destination.Length];
            GetBytesCore(index, bytes);
            bytes.AsSpan().CopyTo(destination);
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
    /**
     * Transfers the specified source array's data to this buffer starting at
     * the specified absolute {@code index}.
     * This method does not modify {@code readerIndex} or {@code writerIndex} of
     * this buffer.
     *
     * @throws IndexOutOfBoundsException
     *         if the specified {@code index} is less than {@code 0},
     *         if the specified {@code srcIndex} is less than {@code 0},
     *         if {@code index + length} is greater than
     *            {@code this.capacity}, or
     *         if {@code srcIndex + length} is greater than {@code src.length}
     */
    // CLR: The original array range overload above uses array.AsSpan(offset, length);
    // range creation validates array bounds before this method is invoked.
    // CLR: The CLR source is an explicit span range with no reader index.
    public virtual ByteBuf SetBytes(int index, ReadOnlySpan<byte> source)
    {
        EnsureCanWrite();
        CheckIndex(index, source.Length);

        // we need to check not null for src as it may cause the JVM crash
        // See https://github.com/netty/netty/issues/10791
        // Copy from direct memory
        // Copy from array
        // CLR: the validated span represents either source; no raw address or nullable array enters the copy.
        if (TryGetMemoryCore(index, source.Length, out var memory))
            source.CopyTo(memory.Span);
        else
            SetBytesCore(index, source.ToArray()); // Preserve overlap across component boundaries.

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
    /**
     * Transfers this buffer's data to the specified destination starting at
     * the current {@code readerIndex} and increases the {@code readerIndex}
     * by the number of the transferred bytes (= {@code length}).
     *
     * @param dstIndex the first index of the destination
     * @param length   the number of bytes to transfer
     *
     * @throws IndexOutOfBoundsException
     *         if the specified {@code dstIndex} is less than {@code 0},
     *         if {@code length} is greater than {@code this.readableBytes}, or
     *         if {@code dstIndex + length} is greater than {@code dst.length}
     */
    // CLR: The original array range overload above uses array.AsSpan(offset, length);
    // range creation validates array bounds before this method is invoked.
    // CLR: The CLR destination is an explicit span range; only this buffer reader advances.
    public virtual ByteBuf ReadBytes(Span<byte> destination)
    {
        CheckReadableBytes(destination.Length);

        GetBytes(_readerIndex, destination);
        _readerIndex += destination.Length;
        return this;
    }

    /**
         * Transfers the specified source array's data to this buffer starting at
         * the current {@code writerIndex} and increases the {@code writerIndex}
         * by the number of the transferred bytes (= {@code src.length}).
         * If {@code this.writableBytes} is less than {@code src.length}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         */
    /**
     * Transfers the specified source array's data to this buffer starting at
     * the current {@code writerIndex} and increases the {@code writerIndex}
     * by the number of the transferred bytes (= {@code length}).
     * If {@code this.writableBytes} is less than {@code length}, {@link #ensureWritable(int)}
     * will be called in an attempt to expand capacity to accommodate.
     *
     * @param srcIndex the first index of the source
     * @param length   the number of bytes to transfer
     *
     * @throws IndexOutOfBoundsException
     *         if the specified {@code srcIndex} is less than {@code 0}, or
     *         if {@code srcIndex + length} is greater than {@code src.length}
     */
    // CLR: The original array range overload above uses array.AsSpan(offset, length);
    // range creation validates array bounds before this method is invoked.
    // CLR: The CLR source is an explicit span range; only this buffer writer advances.
    public virtual ByteBuf WriteBytes(ReadOnlySpan<byte> source)
    {
        using var lease = PinMemoryForWrite();
        EnsureWritable(source.Length);
        SetBytes(_writerIndex, source);
        _writerIndex += source.Length;
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
    public virtual ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
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
    public virtual ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
    {
        EnsureCanWrite();
        ArgumentNullException.ThrowIfNull(source);
        CheckIndex(index, length);
        source.CheckIndex(sourceIndex, length);

        if (source.SupportsDirectBulkCopy && SupportsDirectBulkCopy &&
            source.TryGetReadOnlyMemoryCore(sourceIndex, length, out var input) &&
            TryGetMemoryCore(index, length, out var output))
            input.Span.CopyTo(output.Span);
        else
        {
            byte[] bytes = new byte[length];
            source.GetBytesCore(sourceIndex, bytes);
            SetBytesCore(index, bytes);
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
    public virtual ByteBuf ReadBytes(ByteBuf destination, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (length > destination.WritableBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        // The indexed overload owns source validation/copying and advances this reader.
        ReadBytes(destination, destination.WriterIndex, length);
        destination.WriterIndex += length;
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
    public virtual ByteBuf WriteBytes(ByteBuf source, int length)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.CheckReadableBytes(length);

        // Commit the source cursor only after the indexed virtual write succeeds.
        WriteBytes(source, source.ReaderIndex, length);
        source.ReaderIndex += length;
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
    public virtual ByteBuf SetZero(int index, int length)
    {
        EnsureCanWrite();
        CheckIndex(index, length);

        SetZeroCore(index, length);
        return this;
    }

    /**
         * Fills this buffer with <tt>NUL (0x00)</tt> starting at the current
         * {@code writerIndex} and increases the {@code writerIndex} by the
         * specified {@code length}.
         * If {@code this.writableBytes} is less than {@code length}, {@link #ensureWritable(int)}
         * will be called in an attempt to expand capacity to accommodate.
         *
         * @param length the number of <tt>NUL</tt>s to write to the buffer
         */
    public virtual ByteBuf WriteZero(int length)
    {
        EnsureWritable(length);
        CheckIndex(_writerIndex, length);
        // Like AbstractByteBuf's low-level writes, the storage kernel is independent of public SetZero overrides.
        EnsureCanWrite();

        SetZeroCore(_writerIndex, length);
        _writerIndex += length;
        return this;
    }

    /**
         * Returns a copy of this buffer's readable bytes.  Modifying the content
         * of the returned buffer or this buffer does not affect each other at all.
         * This method is identical to {@code buf.copy(buf.readerIndex(), buf.readableBytes())}.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         */
    public virtual ByteBuf Copy()
    {
        return Copy(_readerIndex, ReadableBytes);
    }

    /**
         * Returns a copy of this buffer's sub-region.  Modifying the content of
         * the returned buffer or this buffer does not affect each other at all.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         */
    public virtual ByteBuf Copy(int index, int length)
    {
        CheckIndex(index, length);

        ByteBuf result = Alloc().HeapBuffer(length, MaxCapacity);

        try
        {
            result.SetBytes(0, this, index, length);
            result.WriterIndex = length;
            return result;
        }
        catch
        {
            result.Release();
            throw;
        }
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
    public virtual ByteBuf ReadBytes(int length)
    {
        CheckReadableBytes(length);
        if (length == 0)
        {
            return Unpooled.EmptyBuffer;
        }

        ByteBuf result = Alloc().Buffer(length, MaxCapacity);

        try
        {
            result.WriteBytes(this, _readerIndex, length);
        }
        catch
        {
            result.Release();
            throw;
        }

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
    public virtual ByteBuf Slice()
    {
        return Slice(_readerIndex, ReadableBytes);
    }

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
    {
        CheckIndex(index, length);

        return new ByteBufView(this, index, length);
    }

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
    public virtual ByteBuf RetainedSlice()
    {
        return Slice().Retain();
    }

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
    public virtual ByteBuf RetainedSlice(int index, int length)
    {
        return Slice(index, length).Retain();
    }

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
    {
        EnsureAccessible();

        return new ByteBufView(this);
    }

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
    public virtual ByteBuf RetainedDuplicate()
    {
        return Duplicate().Retain();
    }

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
    {
        CheckReadableBytes(length);

        ByteBuf result = Slice(_readerIndex, length);
        _readerIndex += length;
        return result;
    }

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
    {
        CheckReadableBytes(length);

        ByteBuf result = RetainedSlice(_readerIndex, length);
        _readerIndex += length;
        return result;
    }

    private protected virtual int DiagnosticMaxCapacity => MaxCapacity;
    /**
     * Returns the string representation of this buffer.  This method does not
     * necessarily return the whole content of the buffer but returns
     * the values of the key properties such as {@link #readerIndex()},
     * {@link #writerIndex()} and {@link #capacity()}.
     */
    public override string ToString()
    {
        string name = StringUtil.SimpleClassName(this);
        if (RefCnt == 0)
        {
            return name + "(freed)";
        }

        // CLR: invariant formatting preserves Java's decimal metadata across caller cultures.
        string result = FormattableString.Invariant($"{name}(ridx: {_readerIndex}, widx: {_writerIndex}, cap: {Capacity}");
        int maximum = DiagnosticMaxCapacity;
        if (maximum != int.MaxValue)
        {
            result += FormattableString.Invariant($"/{maximum}");
        }

        ByteBuf unwrapped = Unwrap();
        if (unwrapped != null)
        {
            result += ", unwrapped: " + unwrapped;
        }

        return result + ')';
    }

/**
     * Transfers this buffer's data starting at the specified absolute {@code index}
     * to the specified channel starting at the given file position.
     * This method does not modify {@code readerIndex} or {@code writerIndex} of
     * this buffer. This method does not modify the channel's position.
     *
     * @param position the file position at which the transfer is to begin
     * @param length the maximum number of bytes to transfer
     *
     * @return the actual number of bytes written out to the specified channel
     *
     * @throws IndexOutOfBoundsException
     *         if the specified {@code index} is less than {@code 0} or
     *         if {@code index + length} is greater than
     *            {@code this.capacity}
     * @throws IOException
     *         if the specified channel threw an exception during I/O
     */
    /// <summary>Writes an absolute buffer range at a file offset without moving either cursor.</summary>
    /// <returns>The requested length after a successful complete RandomAccess.Write.</returns>
    /// <remarks>CLR: RandomAccess writes the complete range or throws; Java permits
    /// a partial write count. The handle is borrowed and is never disposed here.
    /// Pooled snapshots use memory proportional to length, without borrowing native
    /// memory during I/O. File writes can have partial effects before throwing.</remarks>
    public virtual int GetBytes(int index, SafeFileHandle destination, long position, int length)
    {
        CheckIndex(index, length);
        CheckFileTransfer(destination, position, length);
        if (length == 0)
        {
            return 0;
        }

        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            GetBytesCore(index, bytes.AsSpan(0, length));
            RandomAccess.Write(destination, bytes.AsSpan(0, length), position);
            return length;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

/**
     * Transfers the content of the specified source channel starting at the given file position
     * to this buffer starting at the specified absolute {@code index}.
     * This method does not modify {@code readerIndex} or {@code writerIndex} of
     * this buffer. This method does not modify the channel's position.
     *
     * @param position the file position at which the transfer is to begin
     * @param length the maximum number of bytes to transfer
     *
     * @return the actual number of bytes read in from the specified channel.
     *         {@code -1} if the specified channel is closed or reached EOF.
     *
     * @throws IndexOutOfBoundsException
     *         if the specified {@code index} is less than {@code 0} or
     *         if {@code index + length} is greater than {@code this.capacity}
     * @throws IOException
     *         if the specified channel threw an exception during I/O
     */
    /// <summary>Reads once at a file offset into an absolute buffer range without moving either cursor.</summary>
    /// <returns>The actual count, or zero at EOF or for an empty range.</returns>
    /// <remarks>CLR: a closed handle throws instead of returning Java's minus-one
    /// sentinel. Composite storage also uses one bounded read. A throwing file read
    /// does not publish staging bytes; a later composite commit can change a prefix.</remarks>
    public virtual int SetBytes(int index, SafeFileHandle source, long position, int length)
    {
        EnsureCanWrite();
        CheckIndex(index, length);
        CheckFileTransfer(source, position, length);
        if (length == 0)
        {
            return 0;
        }

        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            int count = RandomAccess.Read(source, bytes.AsSpan(0, length), position);
            if (count != 0)
            {
                SetBytesFromIoCore(index, bytes.AsSpan(0, count));
            }

            return count;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

/**
     * Transfers this buffer's data starting at the current {@code readerIndex}
     * to the specified channel starting at the given file position.
     * This method does not modify the channel's position.
     *
     * @param position the file position at which the transfer is to begin
     * @param length the maximum number of bytes to transfer
     *
     * @return the actual number of bytes written out to the specified channel
     *
     * @throws IndexOutOfBoundsException
     *         if {@code length} is greater than {@code this.readableBytes}
     * @throws IOException
     *         if the specified channel threw an exception during I/O
     */
    /// <summary>Writes readable bytes at a file offset and advances the reader only after success.</summary>
    public virtual int ReadBytes(SafeFileHandle destination, long position, int length)
    {
        CheckReadableBytes(length);

        int count = GetBytes(_readerIndex, destination, position, length);
        _readerIndex += count;
        return count;
    }

/**
     * Transfers the content of the specified channel starting at the given file position
     * to this buffer starting at the current {@code writerIndex} and increases the
     * {@code writerIndex} by the number of the transferred bytes.
     * This method does not modify the channel's position.
     * If {@code this.writableBytes} is less than {@code length}, {@link #ensureWritable(int)}
     * will be called in an attempt to expand capacity to accommodate.
     *
     * @param position the file position at which the transfer is to begin
     * @param length the maximum number of bytes to transfer
     *
     * @return the actual number of bytes read in from the specified channel.
     *         {@code -1} if the specified channel is closed or reached EOF.
     *
     * @throws IOException
     *         if the specified channel threw an exception during I/O
     */
    /// <summary>Reserves requested capacity, reads once at a file offset and advances by the actual count.</summary>
    /// <remarks>Handle/range/permission validation precedes growth. Once started,
    /// growth is not rolled back on EOF or I/O failure.</remarks>
    public virtual int WriteBytes(SafeFileHandle source, long position, int length)
    {
        EnsureCanWrite();
        CheckFileTransfer(source, position, length);

        EnsureWritable(length);
        int count = SetBytes(_writerIndex, source, position, length);
        _writerIndex += count;
        return count;
    }

    /// <summary>Asynchronously writes an absolute snapshot at a file offset without moving either cursor.</summary>
    /// <returns>The requested length after a successful complete RandomAccess.WriteAsync.</returns>
    /// <remarks>CLR async counterpart of the pinned synchronous file contract.
    /// Keep the borrowed handle and buffer alive until completion. Exclude concurrent
    /// buffer mutation/release and external changes to the active cursor. Managed
    /// staging remains owned until I/O finishes; no native borrow survives await.</remarks>
    public virtual async ValueTask<int> GetBytesAsync(int index, SafeFileHandle destination, long position, int length,
        CancellationToken cancellationToken = default)
    {
        CheckIndex(index, length);
        CheckFileTransfer(destination, position, length);
        cancellationToken.ThrowIfCancellationRequested();
        if (length == 0)
        {
            return 0;
        }

        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            GetBytesCore(index, bytes.AsSpan(0, length));
            await RandomAccess.WriteAsync(destination, bytes.AsMemory(0, length), position, cancellationToken)
                .ConfigureAwait(false);
            return length;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    /// <summary>Asynchronously reads once at a file offset into an absolute range without moving either cursor.</summary>
    /// <returns>The actual count, or zero at EOF or for an empty range.</returns>
    /// <remarks>A thrown cancellation or I/O failure does not publish staged input.
    /// A valid result commits despite late token cancellation. Buffer/handle ownership
    /// is borrowed through completion; concurrent mutation/release must be excluded.</remarks>
    public virtual async ValueTask<int> SetBytesAsync(int index, SafeFileHandle source, long position, int length,
        CancellationToken cancellationToken = default)
    {
        EnsureCanWrite();
        CheckIndex(index, length);
        CheckFileTransfer(source, position, length);
        cancellationToken.ThrowIfCancellationRequested();
        if (length == 0)
        {
            return 0;
        }

        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            int count = await RandomAccess.ReadAsync(source, bytes.AsMemory(0, length), position, cancellationToken)
                .ConfigureAwait(false);
            if (count != 0)
            {
                EnsureCanWrite();
                CheckIndex(index, count);

                SetBytesFromIoCore(index, bytes.AsSpan(0, count));
            }

            return count;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    /// <summary>Asynchronously writes readable bytes at a file offset, advancing the reader after success.</summary>
    /// <remarks>A file can be partially modified before failure or cancellation;
    /// the reader remains unchanged if the awaited write throws.</remarks>
    public virtual async ValueTask<int> ReadBytesAsync(SafeFileHandle destination, long position, int length,
        CancellationToken cancellationToken = default)
    {
        CheckReadableBytes(length);

        int count = await GetBytesAsync(_readerIndex, destination, position, length, cancellationToken)
            .ConfigureAwait(false);
        _readerIndex += count;
        return count;
    }

    /// <summary>Reserves space, asynchronously reads once at a file offset and advances by the actual count.</summary>
    /// <remarks>Validation precedes cancellation; a pre-canceled token prevents growth
    /// and I/O. Once started, capacity growth is not rolled back. Exclude external
    /// writer changes until completion. The file's current position stays unchanged.</remarks>
    public virtual async ValueTask<int> WriteBytesAsync(SafeFileHandle source, long position, int length,
        CancellationToken cancellationToken = default)
    {
        EnsureCanWrite();
        CheckFileTransfer(source, position, length);
        if (length > MaxWritableBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }
        cancellationToken.ThrowIfCancellationRequested();

        EnsureWritable(length);
        int count = await SetBytesAsync(_writerIndex, source, position, length, cancellationToken)
            .ConfigureAwait(false);
        _writerIndex += count;
        return count;
    }

    private protected static void CheckFileTransfer(SafeFileHandle handle, long position, int length)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (handle.IsClosed)
        {
            throw new ObjectDisposedException(nameof(handle));
        }
        if (handle.IsInvalid)
        {
            throw new ArgumentException("The file handle is invalid.", nameof(handle));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        // Validate the complete file range without signed overflow, before any I/O or growth.
        if (length > long.MaxValue - position)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }
    }

    /**
         * Returns {@code true} if and only if this buffer is read-only.
         */
    public virtual bool IsReadOnly => false;

    // Original deprecated Java factory documentation; AsReadOnly is the supported CLR entry point.
    /**
     * Creates a read-only buffer which disallows any modification operations
     * on the specified {@code buffer}.  The new buffer has the same
     * {@code readerIndex} and {@code writerIndex} with the specified
     * {@code buffer}.
     *
     * @deprecated Use {@link ByteBuf#asReadOnly()}.
     */
    /**
         * Returns a read-only version of this buffer.
         */
    /// <summary>Returns a borrowed read-only view with independent indices and shared reference count.</summary>
    public virtual ByteBuf AsReadOnly()
    {
        EnsureAccessible();

        // We can only use ReadOnlyAbstractByteBuf if we either have nothing to unwrap or the unwrapped buffer is of
        // type AbstractByteBuf. Otherwise we will produce a CCE later.
        // CLR: the original factory rationale is retained; one bounded-memory view avoids subtype casts entirely.
        return IsReadOnly ? this : new ReadOnlyByteBuf(this);
    }

    /**
         * Returns {@code true} if and only if this buffer has enough room to allow writing the specified number of
         * elements.
         */
    /// <summary>Checks write permission and available bytes without growing the buffer.</summary>
    /// <remarks>Like Netty's isWritable(int), negative counts are compared without validation.
    /// Read-only buffers and the shared empty sentinel always return false.</remarks>
    public virtual bool IsWritable(int size)
    {
        return !IsReadOnly && size <= WritableBytes;
    }

    /**
         * Exposes this buffer's sub-region as an NIO {@link ByteBuffer}. The returned buffer
         * either share or contains the copied content of this buffer, while changing the position
         * and limit of the returned NIO buffer does not affect the indexes and marks of this buffer.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of this buffer.
         * Please note that the returned NIO buffer will not see the changes of this buffer if this buffer
         * is a dynamic buffer and it adjusted its capacity.
         *
         * @throws UnsupportedOperationException
         *         if this buffer cannot create a {@link ByteBuffer} that shares the content with itself
         *
         * @see #nioBufferCount()
         * @see #nioBuffers()
         * @see #nioBuffers(int, int)
         */
    /// <summary>Returns a borrowed capacity-bounded read-only range without changing indices.</summary>
    /// <remarks>This is a live view, not a snapshot. Exclude concurrent resize/release while borrowing it.</remarks>
    /// <exception cref="NotSupportedException">The range spans multiple components;
    /// use AsReadOnlySequence or GetBytes for such ranges.</exception>
    public virtual ReadOnlyMemory<byte> AsReadOnlyMemory(int index, int length)
    {
        CheckIndex(index, length);

        return GetReadOnlyMemoryCore(index, length);
    }

    public virtual ReadOnlySpan<byte> AsReadOnlySpan(int index, int length)
    {
        return AsReadOnlyMemory(index, length).Span;
    }

    protected internal void EnsureCanWrite()
    {
        EnsureAccessible();
        if (IsReadOnly)
        {
            throw new NotSupportedException("The buffer is read-only.");
        }
    }

    // CLR: contiguous borrows never copy. Segmented operations use bounded
    // component transfers; words crossing a boundary use at most eight stack bytes.
    protected virtual bool TryGetMemoryCore(int index, int length, out Memory<byte> memory)
    {
        memory = GetMemoryCore(index, length);
        return true;
    }

    protected virtual bool TryGetReadOnlyMemoryCore(int index, int length, out ReadOnlyMemory<byte> memory)
    {
        memory = GetReadOnlyMemoryCore(index, length);
        return true;
    }

    protected virtual void GetBytesCore(int index, Span<byte> destination)
    {
        GetReadOnlyMemoryCore(index, destination.Length).Span.CopyTo(destination);
    }

    protected virtual void SetBytesCore(int index, ReadOnlySpan<byte> source)
    {
        source.CopyTo(GetMemoryCore(index, source.Length).Span);
    }

    // CLR: publish staged input through the source's I/O kernel. Composite input
    // skips empty components, while ordinary bulk setters preserve their dispatch.
    protected internal virtual void SetBytesFromIoCore(int index, ReadOnlySpan<byte> source)
    {
        SetBytes(index, source);
    }

    // fast-path for small writes to avoid thread-state change JDK's handling
    // CLR: Span.Clear replaces the heap helper's manual batch/64-byte threshold strategy.
    protected virtual void SetZeroCore(int index, int length)
    {
        GetMemoryCore(index, length).Span.Clear();
    }

    protected virtual ReadOnlySequence<byte> GetReadOnlySequenceCore(int index, int length)
    {
        return new(GetReadOnlyMemoryCore(index, length));
    }

    internal bool TryGetMemory(int index, int length, out Memory<byte> memory)
    {
        EnsureCanWrite();
        CheckIndex(index, length);

        return TryGetMemoryCore(index, length, out memory);
    }

    internal bool TryGetReadOnlyMemory(int index, int length, out ReadOnlyMemory<byte> memory)
    {
        CheckIndex(index, length);

        return TryGetReadOnlyMemoryCore(index, length, out memory);
    }

/**
     * Exposes this buffer's bytes as an NIO {@link ByteBuffer}'s for the specified index and length
     * The returned buffer either share or contains the copied content of this buffer, while changing
     * the position and limit of the returned NIO buffer does not affect the indexes and marks of this buffer.
     * This method does not modify {@code readerIndex} or {@code writerIndex} of this buffer. Please note that the
     * returned NIO buffer will not see the changes of this buffer if this buffer is a dynamic
     * buffer and it adjusted its capacity.
     *
     * @throws UnsupportedOperationException
     *         if this buffer cannot create a {@link ByteBuffer} that shares the content with itself
     *
     * @see #nioBufferCount()
     * @see #nioBuffer()
     * @see #nioBuffer(int, int)
     */
    // CLR: borrowed ReadOnlySequence segments replace the NIO buffer array.
    /// <summary>Returns borrowed segments sharing the buffer's content without retaining it.</summary>
    /// <remarks>The segment layout is captured. Exclude resize, component changes and
    /// release while using this sequence; it does not acquire native pins.</remarks>
    public virtual ReadOnlySequence<byte> AsReadOnlySequence(int index, int length)
    {
        CheckIndex(index, length);

        return GetReadOnlySequenceCore(index, length);
    }
    public ReadOnlySequence<byte> ReadableSequence => AsReadOnlySequence(ReaderIndex, ReadableBytes);

    internal virtual BufferMemoryLease AcquireReadLease()
    {
        EnsureAccessible();

        return default;
    }

    // Original helper descriptions retained for provenance. CLR heap/native/segmented
    // consumers share bounded spans and BinaryPrimitives; no JVM VarHandle loading gate
    // or duplicate heap-only helper is needed. JVM inspection directives/group labels
    // are retained as referenceComments in the existing porting manifest.
    /**
     * Utility class for heap buffers.
     */
    /**
     * Centralizes all ByteBuffer VarHandle get/set calls so classes like UnpooledDirectByteBuf
     * don't directly reference signature-polymorphic methods. This allows avoiding class verification
     * failures on older Android runtimes by not loading this class when VarHandle is disabled.
     *
     * Methods here must only be called when PlatformDependent.hasVarHandle() is true.
     */
    // Keep scalar storage dispatch distinct from public getters/setters. Composite kernels
    // call their components' public operations; optimized views delegate to these kernels.
    internal virtual byte ReadByteCore(int index)
    {
        return AsReadOnlySpan(index, 1)[0];
    }

    internal virtual void WriteByteCore(int index, int value)
    {
        AsSpan(index, 1)[0] = unchecked((byte)value);
    }

    internal virtual ulong ReadWord(int index, int width, bool littleEndian)
    {
        CheckIndex(index, width);
        if (TryGetReadOnlyMemoryCore(index, width, out var memory))
            return DecodeWord(memory.Span, littleEndian);

        Span<byte> bytes = stackalloc byte[8];
        GetBytesCore(index, bytes[..width]);

        return DecodeWord(bytes[..width], littleEndian);
    }

    private static ulong DecodeWord(ReadOnlySpan<byte> bytes, bool littleEndian)
    {
        return bytes.Length switch
        {
            2 => littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes),
            3 => (ulong)(littleEndian ? bytes[2] << 16 | bytes[1] << 8 | bytes[0] : bytes[0] << 16 | bytes[1] << 8 | bytes[2]),
            4 => littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes),
            8 => littleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(bytes) : BinaryPrimitives.ReadUInt64BigEndian(bytes),
            _ => throw new ArgumentOutOfRangeException(nameof(bytes))
        };
    }

    internal virtual ByteBuf SetWord(int index, ulong value, int width, bool littleEndian)
    {
        EnsureCanWrite();
        CheckIndex(index, width);

        if (TryGetMemoryCore(index, width, out var memory))
            EncodeWord(memory.Span, value, littleEndian);
        else
        {
            Span<byte> bytes = stackalloc byte[8];
            EncodeWord(bytes[..width], value, littleEndian);
            SetBytesCore(index, bytes[..width]);
        }

        return this;
    }

    private static void EncodeWord(Span<byte> bytes, ulong value, bool littleEndian)
    {
        switch (bytes.Length)
        {
            case 2:
                if (littleEndian)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes, unchecked((ushort)value));
                }
                else
                {
                    BinaryPrimitives.WriteUInt16BigEndian(bytes, unchecked((ushort)value));
                }

                break;
            case 3:
                bytes[littleEndian ? 2 : 0] = unchecked((byte)(value >> 16));
                bytes[1] = unchecked((byte)(value >> 8));
                bytes[littleEndian ? 0 : 2] = unchecked((byte)value);
                break;
            case 4:
                if (littleEndian)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes, unchecked((uint)value));
                }
                else
                {
                    BinaryPrimitives.WriteUInt32BigEndian(bytes, unchecked((uint)value));
                }

                break;
            case 8:
                if (littleEndian)
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
                }
                else
                {
                    BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(bytes));
        }
    }

    /**
     * Transfers this buffer's data to the specified channel starting at the
     * specified absolute {@code index}.
     * This method does not modify {@code readerIndex} or {@code writerIndex} of
     * this buffer.
     *
     * @param length the maximum number of bytes to transfer
     *
     * @return the actual number of bytes written out to the specified channel
     *
     * @throws IndexOutOfBoundsException
     *         if the specified {@code index} is less than {@code 0} or
     *         if {@code index + length} is greater than
     *            {@code this.capacity}
     * @throws IOException
     *         if the specified channel threw an exception during I/O
     */
    /// <summary>Sends an absolute snapshot once and returns the actual count without changing indices.</summary>
    /// <remarks>The connected socket is borrowed. Nonblocking WouldBlock returns zero;
    /// other socket errors propagate. Empty ranges validate the buffer but skip I/O.
    /// Managed staging costs O(length) memory and keeps native storage out of socket I/O.
    /// Exclude concurrent mutation, release and external cursor changes during the operation.</remarks>
    public virtual int GetBytes(int index, Socket destination, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        if (length == 0)
        {
            return 0;
        }

        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            GetBytesCore(index, bytes.AsSpan(0, length));
            int count = destination.Send(bytes.AsSpan(0, length), SocketFlags.None, out SocketError error);
            return SocketTransferCount(count, error);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    /**
     * Transfers the content of the specified source channel to this buffer
     * starting at the specified absolute {@code index}.
     * This method does not modify {@code readerIndex} or {@code writerIndex} of
     * this buffer.
     *
     * @param length the maximum number of bytes to transfer
     *
     * @return the actual number of bytes read in from the specified channel.
     *         {@code -1} if the specified channel is closed or reached EOF.
     *
     * @throws IndexOutOfBoundsException
     *         if the specified {@code index} is less than {@code 0} or
     *         if {@code index + length} is greater than {@code this.capacity}
     * @throws IOException
     *         if the specified channel threw an exception during I/O
     */
    /// <summary>Receives once into an absolute range without changing indices.</summary>
    /// <returns>The actual count; zero for stream EOF, WouldBlock or an empty request.</returns>
    /// <remarks>CLR: a closed socket throws. Composite storage also receives once;
    /// Java may read repeatedly across components. Staging prevents failed I/O from
    /// publishing input, although a later failed composite commit can change a prefix.
    /// The socket and buffer are borrowed; no reference count is acquired.</remarks>
    public virtual int SetBytes(int index, Socket source, int length)
    {
        EnsureCanWrite();
        ArgumentNullException.ThrowIfNull(source);
        CheckIndex(index, length);
        if (length == 0)
        {
            return 0;
        }

        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            int count = source.Receive(bytes.AsSpan(0, length), SocketFlags.None, out SocketError error);
            count = SocketTransferCount(count, error);
            if (count != 0)
            {
                SetBytesFromIoCore(index, bytes.AsSpan(0, count));
            }

            return count;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    /**
     * Transfers this buffer's data to the specified stream starting at the
     * current {@code readerIndex}.
     *
     * @param length the maximum number of bytes to transfer
     *
     * @return the actual number of bytes written out to the specified channel
     *
     * @throws IndexOutOfBoundsException
     *         if {@code length} is greater than {@code this.readableBytes}
     * @throws IOException
     *         if the specified channel threw an exception during I/O
     */
    /// <summary>Sends readable bytes once and advances the reader by the actual successful count.</summary>
    /// <remarks>Failure leaves the reader unchanged, even if the peer received a prefix.</remarks>
    public virtual int ReadBytes(Socket destination, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckReadableBytes(length);

        int count = GetBytes(_readerIndex, destination, length);
        _readerIndex += count;
        return count;
    }

    /**
     * Transfers the content of the specified channel to this buffer
     * starting at the current {@code writerIndex} and increases the
     * {@code writerIndex} by the number of the transferred bytes.
     * If {@code this.writableBytes} is less than {@code length}, {@link #ensureWritable(int)}
     * will be called in an attempt to expand capacity to accommodate.
     *
     * @param length the maximum number of bytes to transfer
     *
     * @return the actual number of bytes read in from the specified channel.
     *         {@code -1} if the specified channel is closed or reached EOF.
     *
     * @throws IOException
     *         if the specified channel threw an exception during I/O
     */
    /// <summary>Reserves requested space, receives once and advances the writer by the actual count.</summary>
    /// <remarks>Growth is retained on EOF, WouldBlock or I/O failure, as in Netty.</remarks>
    public virtual int WriteBytes(Socket source, int length)
    {
        EnsureWritable(length);
        ArgumentNullException.ThrowIfNull(source);

        int count = SetBytes(_writerIndex, source, length);
        _writerIndex += count;
        return count;
    }

    /// <summary>Asynchronously sends one absolute snapshot without changing indices.</summary>
    /// <remarks>Uses Socket.SendAsync directly. The returned count can be shorter than
    /// requested; this method does not retry. Staging survives suspension; no native
    /// borrow does. Keep the borrowed socket/buffer alive and exclude concurrent mutation,
    /// release and external cursor changes until completion.</remarks>
    public virtual async ValueTask<int> GetBytesAsync(int index, Socket destination, int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        cancellationToken.ThrowIfCancellationRequested();
        if (length == 0)
        {
            return 0;
        }

        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            GetBytesCore(index, bytes.AsSpan(0, length));
            return await destination.SendAsync(bytes.AsMemory(0, length), SocketFlags.None, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    /// <summary>Asynchronously receives once into an absolute range without changing indices.</summary>
    /// <returns>The actual count; zero for stream EOF or an empty request.</returns>
    /// <remarks>A thrown cancellation/failure publishes no staged input. A successful
    /// result commits despite late cancellation. Async socket I/O waits for readiness;
    /// it does not expose synchronous nonblocking WouldBlock. Borrowed lifetime and
    /// exclusion requirements are the same as GetBytesAsync(index, Socket, length).</remarks>
    public virtual async ValueTask<int> SetBytesAsync(int index, Socket source, int length,
        CancellationToken cancellationToken = default)
    {
        EnsureCanWrite();
        ArgumentNullException.ThrowIfNull(source);
        CheckIndex(index, length);
        cancellationToken.ThrowIfCancellationRequested();
        if (length == 0)
        {
            return 0;
        }

        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            int count = await source.ReceiveAsync(bytes.AsMemory(0, length), SocketFlags.None, cancellationToken)
                .ConfigureAwait(false);
            if (count != 0)
            {
                EnsureCanWrite();
                CheckIndex(index, count);

                SetBytesFromIoCore(index, bytes.AsSpan(0, count));
            }

            return count;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    /// <summary>Asynchronously sends readable bytes once, advancing the reader by the returned count.</summary>
    /// <remarks>Thrown cancellation/failure leaves the reader unchanged. The socket
    /// may already have accepted a prefix. Exclude external reader changes until completion.</remarks>
    public virtual async ValueTask<int> ReadBytesAsync(Socket destination, int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckReadableBytes(length);

        int count = await GetBytesAsync(_readerIndex, destination, length, cancellationToken).ConfigureAwait(false);
        _readerIndex += count;
        return count;
    }

    /// <summary>Reserves space, asynchronously receives once and advances by the actual count.</summary>
    /// <remarks>Validation precedes cancellation. Pre-cancellation prevents growth;
    /// started growth is not rolled back. Exclude external writer changes until completion.</remarks>
    public virtual async ValueTask<int> WriteBytesAsync(Socket source, int length,
        CancellationToken cancellationToken = default)
    {
        EnsureCanWrite();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (length > MaxWritableBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }
        cancellationToken.ThrowIfCancellationRequested();

        EnsureWritable(length);
        int count = await SetBytesAsync(_writerIndex, source, length, cancellationToken).ConfigureAwait(false);
        _writerIndex += count;
        return count;
    }

    private static int SocketTransferCount(int count, SocketError error)
    {
        if (error == SocketError.WouldBlock)
        {
            return 0;
        }
        if (error != SocketError.Success)
        {
            throw new SocketException((int)error);
        }

        return count;
    }

/**
     * Transfers this buffer's data to the specified stream starting at the
     * specified absolute {@code index}.
     * This method does not modify {@code readerIndex} or {@code writerIndex} of
     * this buffer.
     *
     * @param length the number of bytes to transfer
     *
     * @throws IndexOutOfBoundsException
     *         if the specified {@code index} is less than {@code 0} or
     *         if {@code index + length} is greater than
     *            {@code this.capacity}
     * @throws IOException
     *         if the specified stream threw an exception during I/O
     */
    /// <summary>Writes a snapshot of an absolute range without changing buffer indices.</summary>
    /// <remarks>The stream is borrowed. Temporary pooled storage prevents callbacks
    /// from invalidating native memory or later composite segments during the write.
    /// Exclude concurrent changes, release and external cursor mutation during I/O.</remarks>
    // Original ByteBufUtil internal transfer contract; bounded ranges replace NIO cursor state.
    /**
     * Read bytes from the given {@link ByteBuffer} into the given {@link OutputStream} using the {@code position} and
     * {@code length}. The position and limit of the given {@link ByteBuffer} may be adjusted.
     */
    public virtual ByteBuf GetBytes(int index, Stream destination, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        if (length == 0)
        {
            return this;
        }

        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            GetBytesCore(index, bytes.AsSpan(0, length));
            destination.Write(bytes.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }

        return this;
    }

/**
     * Transfers the content of the specified source stream to this buffer
     * starting at the specified absolute {@code index}.
     * This method does not modify {@code readerIndex} or {@code writerIndex} of
     * this buffer.
     *
     * @param length the number of bytes to transfer
     *
     * @return the actual number of bytes read in from the specified channel.
     *         {@code -1} if the specified {@link InputStream} reached EOF.
     *
     * @throws IndexOutOfBoundsException
     *         if the specified {@code index} is less than {@code 0} or
     *         if {@code index + length} is greater than {@code this.capacity}
     * @throws IOException
     *         if the specified stream threw an exception during I/O
     */
    /// <summary>Performs one bounded Stream.Read into an absolute range.</summary>
    /// <returns>The actual count, or zero at EOF or for an empty request.</returns>
    /// <remarks>CLR: all storage kinds use one read, including composites. A short
    /// read is returned immediately; callers requiring a full field must loop.
    /// Staging means a throwing read cannot publish bytes into this buffer. The
    /// stream is borrowed; this method does not retain the buffer or change indices.</remarks>
    public virtual int SetBytes(int index, Stream source, int length)
    {
        EnsureCanWrite();
        ArgumentNullException.ThrowIfNull(source);
        CheckIndex(index, length);
        if (length == 0)
        {
            return 0;
        }

        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            int count = source.Read(bytes.AsSpan(0, length));
            if (count < 0 || count > length)
                throw new IOException("The stream returned an invalid byte count.");

            // A stream callback can resize storage. Resolve the destination again
            // after reading instead of keeping a borrowed native span across I/O.
            if (count != 0)
            {
                EnsureCanWrite();
                CheckIndex(index, count);

                SetBytesFromIoCore(index, bytes.AsSpan(0, count));
            }

            return count;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

/**
     * Transfers this buffer's data to the specified stream starting at the
     * current {@code readerIndex}.
     *
     * @param length the number of bytes to transfer
     *
     * @throws IndexOutOfBoundsException
     *         if {@code length} is greater than {@code this.readableBytes}
     * @throws IOException
     *         if the specified stream threw an exception during I/O
     */
    /// <summary>Writes readable bytes and advances the reader only after success.</summary>
    /// <remarks>A throwing destination may already have accepted some bytes; the
    /// buffer reader remains unchanged. The stream is not disposed.</remarks>
    public virtual ByteBuf ReadBytes(Stream destination, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckReadableBytes(length);

        GetBytes(_readerIndex, destination, length);
        _readerIndex += length;
        return this;
    }

/**
     * Transfers the content of the specified stream to this buffer
     * starting at the current {@code writerIndex} and increases the
     * {@code writerIndex} by the number of the transferred bytes.
     * If {@code this.writableBytes} is less than {@code length}, {@link #ensureWritable(int)}
     * will be called in an attempt to expand capacity to accommodate.
     *
     * @param length the number of bytes to transfer
     *
     * @return the actual number of bytes read in from the specified channel.
     *         {@code -1} if the specified {@link InputStream} reached EOF.
     *
     * @throws IOException if the specified stream threw an exception during I/O
     */
    /// <summary>Reserves the requested space, reads once and advances by the actual count.</summary>
    /// <returns>The actual count, or zero at EOF or for an empty request.</returns>
    /// <remarks>Capacity can grow even at EOF or on I/O failure, as in Netty.
    /// The writer advances only after a successful read and buffer transfer.</remarks>
    public virtual int WriteBytes(Stream source, int length)
    {
        EnsureWritable(length);
        ArgumentNullException.ThrowIfNull(source);

        int count = SetBytes(_writerIndex, source, length);
        _writerIndex += count;
        return count;
    }

    /// <summary>Asynchronously writes a snapshot of an absolute range without changing indices.</summary>
    /// <remarks>CLR asynchronous counterpart of GetBytes(index, Stream, length).
    /// Calls the stream's Memory-based WriteAsync directly without wrapping synchronous I/O in Task.Run.
    /// The stream and buffer are borrowed for the full operation. Exclude concurrent
    /// mutation, release and external cursor changes until the returned ValueTask completes.
    /// Temporary managed storage is kept until I/O finishes; native memory is not borrowed across await.</remarks>
    public virtual async ValueTask GetBytesAsync(int index, Stream destination, int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        cancellationToken.ThrowIfCancellationRequested();
        if (length == 0)
        {
            return;
        }

        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            GetBytesCore(index, bytes.AsSpan(0, length));
            await destination.WriteAsync(bytes.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    /// <summary>Performs one asynchronous bounded read into an absolute range without changing indices.</summary>
    /// <returns>The actual count, or zero at EOF or for an empty request.</returns>
    /// <remarks>Uses the same one-read and staging policy as SetBytes(index, Stream, length).
    /// Cancellation or failure from the source does not publish staging bytes. Source
    /// consumption can already have occurred. A valid result is committed even if the
    /// token was canceled meanwhile: cancellation is cooperative, not a rollback.
    /// Keep the borrowed buffer and stream alive and exclude concurrent mutation until completion.</remarks>
    public virtual async ValueTask<int> SetBytesAsync(int index, Stream source, int length,
        CancellationToken cancellationToken = default)
    {
        EnsureCanWrite();
        ArgumentNullException.ThrowIfNull(source);
        CheckIndex(index, length);
        cancellationToken.ThrowIfCancellationRequested();
        if (length == 0)
        {
            return 0;
        }

        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            int count = await source.ReadAsync(bytes.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            if (count < 0 || count > length)
                throw new IOException("The stream returned an invalid byte count.");

            if (count != 0)
            {
                // Resolve storage after I/O; no borrowed native span survives suspension.
                EnsureCanWrite();
                CheckIndex(index, count);

                SetBytesFromIoCore(index, bytes.AsSpan(0, count));
            }

            return count;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    /// <summary>Asynchronously writes readable bytes, then advances the reader after success.</summary>
    /// <remarks>Cancellation or failure leaves the reader unchanged even when the
    /// destination already accepted a prefix. Exclude external reader changes until completion.</remarks>
    public virtual async ValueTask ReadBytesAsync(Stream destination, int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckReadableBytes(length);

        await GetBytesAsync(_readerIndex, destination, length, cancellationToken).ConfigureAwait(false);
        _readerIndex += length;
    }

    /// <summary>Reserves requested capacity, asynchronously reads once and advances by the actual count.</summary>
    /// <returns>The actual count, or zero at EOF or for an empty request.</returns>
    /// <remarks>A pre-canceled token prevents capacity growth and I/O after argument
    /// and permission validation. Once started, growth is not rolled back on EOF,
    /// cancellation or failure. Exclude external writer changes until completion.</remarks>
    public virtual async ValueTask<int> WriteBytesAsync(Stream source, int length,
        CancellationToken cancellationToken = default)
    {
        EnsureCanWrite();
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (length > MaxWritableBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }
        cancellationToken.ThrowIfCancellationRequested();

        EnsureWritable(length);
        int count = await SetBytesAsync(_writerIndex, source, length, cancellationToken).ConfigureAwait(false);
        _writerIndex += count;
        return count;
    }

    /**
         * Decodes this buffer's readable bytes into a string with the specified
         * character set name.  This method is identical to
         * {@code buf.toString(buf.readerIndex(), buf.readableBytes(), charsetName)}.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @throws UnsupportedCharsetException
         *         if the specified character set name is not supported by the
         *         current VM
         */
    /// <summary>Decodes readable bytes using the caller's encoding and fallback policy.</summary>
    public virtual string ToString(Encoding charset)
    {
        return ToString(_readerIndex, ReadableBytes, charset);
    }

    /**
         * Decodes this buffer's sub-region into a string with the specified
         * character set.  This method does not modify {@code readerIndex} or
         * {@code writerIndex} of this buffer.
         */
    /// <summary>Decodes a capacity-bounded range without changing either index.</summary>
    public virtual string ToString(int index, int length, Encoding charset)
    {
        ArgumentNullException.ThrowIfNull(charset);
        if (TryGetReadOnlyMemory(index, length, out var memory))
            return charset.GetString(memory.Span);

        byte[] bytes = new byte[length];
        GetBytesCore(index, bytes);

        return charset.GetString(bytes);
    }

    /**
     * Gets a {@link CharSequence} with the given length at the given index.
     *
     * @param length the length to read
     * @param charset that should be used
     * @return the sequence
     * @throws IndexOutOfBoundsException
     *         if {@code length} is greater than {@code this.readableBytes}
     */
    // CLR: CharSequence is ICharSequence. .NET has no Charset identity, so US-ASCII and ISO-8859-1 are recognized by
    // their code pages (20127, 28591); any other encoding returns the decoded string as a StringCharSequence.
    public virtual ICharSequence GetCharSequence(int index, int length, Encoding charset)
    {
        ArgumentNullException.ThrowIfNull(charset);
        if (charset.CodePage is 20127 or 28591)
        {
            // ByteBufUtil.getBytes(...) will return a new copy which the AsciiString uses directly
            return new AsciiString(ByteBufUtil.GetBytes(this, index, length, true), false);
        }

        return new StringCharSequence(ToString(index, length, charset));
    }

    /**
     * Gets a {@link CharSequence} with the given length at the current {@code readerIndex}
     * and increases the {@code readerIndex} by the given length.
     *
     * @param length the length to read
     * @param charset that should be used
     * @return the sequence
     * @throws IndexOutOfBoundsException
     *         if {@code length} is greater than {@code this.readableBytes}
     */
    // CLR: as ReadString does, the readable range is checked first (the Javadoc's IndexOutOfBoundsException contract)
    // and the reader advances only after decoding succeeds.
    public virtual ICharSequence ReadCharSequence(int length, Encoding charset)
    {
        CheckReadableBytes(length);

        ICharSequence sequence = GetCharSequence(_readerIndex, length, charset);
        _readerIndex += length;
        return sequence;
    }

    /**
         * Gets a {@link String} with the given length at the current {@code readerIndex}
         * and increases the {@code readerIndex} by the given length.
         *
         * @param length the length to read
         * @param charset that should be used
         * @return the string
         * @throws IndexOutOfBoundsException
         *         if {@code length} is greater than {@code this.readableBytes}
         */
    /// <summary>Decodes written bytes and advances the reader only after successful decoding.</summary>
    public virtual string ReadString(int length, Encoding charset)
    {
        CheckReadableBytes(length);

        string result = ToString(_readerIndex, length, charset);
        _readerIndex += length;
        return result;
    }

    /**
         * Writes the specified {@link CharSequence} at the given {@code index}.
         * The {@code writerIndex} is not modified by this method.
         *
         * @param index on which the sequence should be written
         * @param sequence to write
         * @param charset that should be used.
         * @return the written number of bytes.
         * @throws IndexOutOfBoundsException
         *         if the sequence at the given index would be out of bounds of the buffer capacity
         */
    /// <summary>Encodes text without growing the buffer or changing indices. No preamble is emitted.</summary>
    public virtual int SetCharSequence(int index, ReadOnlySpan<char> sequence, Encoding charset)
    {
        ArgumentNullException.ThrowIfNull(charset);
        EnsureCanWrite();

        int length = charset.GetByteCount(sequence);
        return EncodeString(index, sequence, charset, length);
    }

    private int EncodeString(int index, ReadOnlySpan<char> text, Encoding encoding, int length)
    {
        // Original charset fallback:
        // setBytes(...) will take care of checking the indices.
        // CLR: the bounded memory lookup below validates the range before encoding.
        if (TryGetMemory(index, length, out var memory))
            return encoding.GetBytes(text, memory.Span);

        byte[] bytes = new byte[length];
        int written = encoding.GetBytes(text, bytes);
        SetBytesCore(index, bytes.AsSpan(0, written));
        return written;
    }

    /**
         * Writes the specified {@link CharSequence} at the current {@code writerIndex} and increases
         * the {@code writerIndex} by the written bytes.
         * If {@code this.writableBytes} is not large enough to write the whole sequence,
         * {@link #ensureWritable(int)} will be called in an attempt to expand capacity to accommodate.
         *
         * @param sequence to write
         * @param charset that should be used
         * @return the written number of bytes
         */
    /// <summary>Reserves the exact encoded length, writes text, and advances the writer after success.</summary>
    /// <remarks>The supplied Encoding controls malformed/unmappable input. No preamble is emitted.
    /// Use ByteBufUtil.WriteUtf8/WriteAscii when Netty's specialized byte mapping is required.</remarks>
    public virtual int WriteCharSequence(ReadOnlySpan<char> sequence, Encoding charset)
    {
        ArgumentNullException.ThrowIfNull(charset);

        // A UTF-16 span can alias native storage through MemoryMarshal.Cast. Keep it alive across growth.
        using var lease = PinMemoryForWrite();
        EnsureAccessible();

        int length = charset.GetByteCount(sequence);
        EnsureWritable(length);
        int written = EncodeString(_writerIndex, sequence, charset, length);
        _writerIndex += written;
        return written;
    }

    /**
         * Locates the first occurrence of the specified {@code value} in this
         * buffer. The search takes place from the specified {@code fromIndex}
         * (inclusive) to the specified {@code toIndex} (exclusive).
         * <p>
         * If {@code fromIndex} is greater than {@code toIndex}, the search is
         * performed in a reversed order from {@code fromIndex} (exclusive)
         * down to {@code toIndex} (inclusive).
         * <p>
         * Note that the lower index is always included and higher always excluded.
         * <p>
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @return the absolute index of the first occurrence if found.
         *         {@code -1} otherwise.
         */
    public virtual int IndexOf(int fromIndex, int toIndex, byte value)
    {
        if (fromIndex <= toIndex)
        {
            fromIndex = Math.Max(fromIndex, 0);
            if (fromIndex >= toIndex || Capacity == 0)
            {
                return -1;
            }

            int length = checked(toIndex - fromIndex);
            CheckIndex(fromIndex, length);

            if (!SupportsDirectSearch || !TryGetReadOnlyMemory(fromIndex, length, out var memory))
            {
                // Match the original low-level search reads, independently of public GetByte overrides.
                for (int i = fromIndex; i < toIndex; ++i)
                if (ReadByteCore(i) == value)
                {
                    return i;
                }

                return -1;
            }

            int found = memory.Span.IndexOf(value);
            return found < 0 ? -1 : fromIndex + found;
        }

        fromIndex = Math.Min(fromIndex, Capacity);
        if (fromIndex <= 0)
        {
            return -1; // fromIndex is the exclusive upper bound.
        }
        CheckIndex(toIndex, 0);

        int reverseLength = checked(fromIndex - toIndex);
        CheckIndex(toIndex, reverseLength);

        if (!SupportsDirectSearch || !TryGetReadOnlyMemory(toIndex, reverseLength, out var reverseMemory))
        {
            for (int i = fromIndex - 1; i >= toIndex; --i)
            if (ReadByteCore(i) == value)
            {
                return i;
            }

            return -1;
        }

        int reverseFound = reverseMemory.Span.LastIndexOf(value);
        return reverseFound < 0 ? -1 : toIndex + reverseFound;
    }

    /**
         * Locates the first occurrence of the specified {@code value} in this
         * buffer.  The search takes place from the current {@code readerIndex}
         * (inclusive) to the current {@code writerIndex} (exclusive).
         * <p>
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @return the number of bytes between the current {@code readerIndex}
         *         and the first occurrence if found. {@code -1} otherwise.
         */
    public virtual int BytesBefore(byte value)
    {
        return BytesBefore(_readerIndex, ReadableBytes, value);
    }

    /**
         * Locates the first occurrence of the specified {@code value} in this
         * buffer.  The search starts from the current {@code readerIndex}
         * (inclusive) and lasts for the specified {@code length}.
         * <p>
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @return the number of bytes between the current {@code readerIndex}
         *         and the first occurrence if found. {@code -1} otherwise.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code length} is greater than {@code this.readableBytes}
         */
    public virtual int BytesBefore(int length, byte value)
    {
        CheckReadableBytes(length);

        return BytesBefore(_readerIndex, length, value);
    }

    /**
         * Locates the first occurrence of the specified {@code value} in this
         * buffer.  The search starts from the specified {@code index} (inclusive)
         * and lasts for the specified {@code length}.
         * <p>
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @return the number of bytes between the specified {@code index}
         *         and the first occurrence if found. {@code -1} otherwise.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code index + length} is greater than {@code this.capacity}
         */
    public virtual int BytesBefore(int index, int length, byte value)
    {
        CheckIndex(index, length);

        int found = IndexOf(index, index + length, value);
        return found < 0 ? -1 : found - index;
    }

    /**
         * Iterates over the readable bytes of this buffer with the specified {@code processor} in ascending order.
         *
         * @return {@code -1} if the processor iterated to or beyond the end of the readable bytes.
         *         The last-visited index If the {@link ByteProcessor#process(byte)} returned {@code false}.
         */
    public virtual int ForEachByte(Func<byte, bool> processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        CheckIndex(_readerIndex, ReadableBytes);

        return ForEachByteAscending(_readerIndex, _writerIndex, processor);
    }

    /**
         * Iterates over the specified area of this buffer with the specified {@code processor} in ascending order.
         * (i.e. {@code index}, {@code (index + 1)},  .. {@code (index + length - 1)})
         *
         * @return {@code -1} if the processor iterated to or beyond the end of the specified area.
         *         The last-visited index If the {@link ByteProcessor#process(byte)} returned {@code false}.
         */
    public virtual int ForEachByte(int index, int length, Func<byte, bool> processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        CheckIndex(index, length);

        return ForEachByteAscending(index, index + length, processor);
    }

    // A physical aggregate borrow can differ from its logical scalar search representation.
    internal virtual bool SupportsDirectSearch => SupportsDirectBulkCopy;

    // Component iteration bypasses public iteration overrides after peeling transparent/derived layers.
    // The original owner still holds the claim; no reference is transferred by this lookup.
    internal virtual ByteBuf UnwrapIterationStorage(ref int index)
    {
        return this;
    }

    internal virtual int ForEachByteAscending(int start, int end, Func<byte, bool> processor)
    {
        // Use the storage kernel independently of public GetByte/indexed iteration overrides.
        // Reacquire each byte: a callback can resize or release native storage. Never retain a span across callbacks.
        for (int i = start; i < end; ++i)
            if (!processor(ReadByteCore(i)))
            {
                return i;
            }

        return -1;
    }

    /**
         * Iterates over the readable bytes of this buffer with the specified {@code processor} in descending order.
         *
         * @return {@code -1} if the processor iterated to or beyond the beginning of the readable bytes.
         *         The last-visited index If the {@link ByteProcessor#process(byte)} returned {@code false}.
         */
    public virtual int ForEachByteDesc(Func<byte, bool> processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        CheckIndex(_readerIndex, ReadableBytes);

        return ForEachByteDescending(_writerIndex - 1, _readerIndex, processor);
    }

    /**
         * Iterates over the specified area of this buffer with the specified {@code processor} in descending order.
         * (i.e. {@code (index + length - 1)}, {@code (index + length - 2)}, ... {@code index})
         *
         *
         * @return {@code -1} if the processor iterated to or beyond the beginning of the specified area.
         *         The last-visited index If the {@link ByteProcessor#process(byte)} returned {@code false}.
         */
    public virtual int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        CheckIndex(index, length);

        return ForEachByteDescending(index + length - 1, index, processor);
    }

    internal virtual int ForEachByteDescending(int start, int end, Func<byte, bool> processor)
    {
        for (int i = start; i >= end; --i)
            if (!processor(ReadByteCore(i)))
            {
                return i;
            }

        return -1;
    }

    // CLR byte[] ranges use Span slices. These overloads preserve the distinct ByteBuf
    // cursor contracts; all copies share the existing bounded, overlap-safe storage kernel.
    /**
     * Transfers this buffer's data to the specified destination starting at
     * the specified absolute {@code index} until the destination becomes
     * non-writable.  This method is basically same with
     * {@link #getBytes(int, ByteBuf, int, int)}, except that this
     * method increases the {@code writerIndex} of the destination by the
     * number of the transferred bytes while
     * {@link #getBytes(int, ByteBuf, int, int)} does not.
     * This method does not modify {@code readerIndex} or {@code writerIndex} of
     * the source buffer (i.e. {@code this}).
     *
     * @throws IndexOutOfBoundsException
     *         if the specified {@code index} is less than {@code 0} or
     *         if {@code index + dst.writableBytes} is greater than
     *            {@code this.capacity}
     */
    public virtual ByteBuf GetBytes(int index, ByteBuf destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.EnsureAccessible();

        GetBytes(index, destination, destination.WritableBytes);
        return this;
    }

    /**
     * Transfers this buffer's data to the specified destination starting at
     * the specified absolute {@code index}.  This method is basically same
     * with {@link #getBytes(int, ByteBuf, int, int)}, except that this
     * method increases the {@code writerIndex} of the destination by the
     * number of the transferred bytes while
     * {@link #getBytes(int, ByteBuf, int, int)} does not.
     * This method does not modify {@code readerIndex} or {@code writerIndex} of
     * the source buffer (i.e. {@code this}).
     *
     * @param length the number of bytes to transfer
     *
     * @throws IndexOutOfBoundsException
     *         if the specified {@code index} is less than {@code 0},
     *         if {@code index + length} is greater than
     *            {@code this.capacity}, or
     *         if {@code length} is greater than {@code dst.writableBytes}
     */
    public virtual ByteBuf GetBytes(int index, ByteBuf destination, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);

        GetBytes(index, destination, destination.WriterIndex, length);
        destination.WriterIndex += length;
        return this;
    }

    /**
     * Transfers the specified source buffer's data to this buffer starting at
     * the specified absolute {@code index} until the source buffer becomes
     * unreadable.  This method is basically same with
     * {@link #setBytes(int, ByteBuf, int, int)}, except that this
     * method increases the {@code readerIndex} of the source buffer by
     * the number of the transferred bytes while
     * {@link #setBytes(int, ByteBuf, int, int)} does not.
     * This method does not modify {@code readerIndex} or {@code writerIndex} of
     * this buffer (i.e. {@code this}).
     *
     * @throws IndexOutOfBoundsException
     *         if the specified {@code index} is less than {@code 0} or
     *         if {@code index + src.readableBytes} is greater than
     *            {@code this.capacity}
     */
    public virtual ByteBuf SetBytes(int index, ByteBuf source)
    {
        ArgumentNullException.ThrowIfNull(source);

        SetBytes(index, source, source.ReadableBytes);
        return this;
    }

    /**
     * Transfers the specified source buffer's data to this buffer starting at
     * the specified absolute {@code index}.  This method is basically same
     * with {@link #setBytes(int, ByteBuf, int, int)}, except that this
     * method increases the {@code readerIndex} of the source buffer by
     * the number of the transferred bytes while
     * {@link #setBytes(int, ByteBuf, int, int)} does not.
     * This method does not modify {@code readerIndex} or {@code writerIndex} of
     * this buffer (i.e. {@code this}).
     *
     * @param length the number of bytes to transfer
     *
     * @throws IndexOutOfBoundsException
     *         if the specified {@code index} is less than {@code 0},
     *         if {@code index + length} is greater than
     *            {@code this.capacity}, or
     *         if {@code length} is greater than {@code src.readableBytes}
     */
    public virtual ByteBuf SetBytes(int index, ByteBuf source, int length)
    {
        ArgumentNullException.ThrowIfNull(source);
        // Validate this range before the indexed override can run or consume the source.
        CheckIndex(index, length);
        source.CheckReadableBytes(length);

        SetBytes(index, source, source.ReaderIndex, length);
        source.ReaderIndex += length;
        return this;
    }

    /**
     * Transfers this buffer's data to the specified destination starting at
     * the current {@code readerIndex} until the destination becomes
     * non-writable, and increases the {@code readerIndex} by the number of the
     * transferred bytes.  This method is basically same with
     * {@link #readBytes(ByteBuf, int, int)}, except that this method
     * increases the {@code writerIndex} of the destination by the number of
     * the transferred bytes while {@link #readBytes(ByteBuf, int, int)}
     * does not.
     *
     * @throws IndexOutOfBoundsException
     *         if {@code dst.writableBytes} is greater than
     *            {@code this.readableBytes}
     */
    public virtual ByteBuf ReadBytes(ByteBuf destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.EnsureAccessible();

        ReadBytes(destination, destination.WritableBytes);
        return this;
    }

    /**
     * Transfers this buffer's data to the specified destination starting at
     * the current {@code readerIndex} and increases the {@code readerIndex}
     * by the number of the transferred bytes (= {@code length}).
     *
     * @param dstIndex the first index of the destination
     * @param length   the number of bytes to transfer
     *
     * @throws IndexOutOfBoundsException
     *         if the specified {@code dstIndex} is less than {@code 0},
     *         if {@code length} is greater than {@code this.readableBytes}, or
     *         if {@code dstIndex + length} is greater than
     *            {@code dst.capacity}
     */
    public virtual ByteBuf ReadBytes(ByteBuf destination, int destinationIndex, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckReadableBytes(length);

        GetBytes(_readerIndex, destination, destinationIndex, length);
        _readerIndex += length;
        return this;
    }

    /**
     * Transfers the specified source buffer's data to this buffer starting at
     * the current {@code writerIndex} until the source buffer becomes
     * unreadable, and increases the {@code writerIndex} by the number of
     * the transferred bytes.  This method is basically same with
     * {@link #writeBytes(ByteBuf, int, int)}, except that this method
     * increases the {@code readerIndex} of the source buffer by the number of
     * the transferred bytes while {@link #writeBytes(ByteBuf, int, int)}
     * does not.
     * If {@code this.writableBytes} is less than {@code src.readableBytes},
     * {@link #ensureWritable(int)} will be called in an attempt to expand
     * capacity to accommodate.
     */
    public virtual ByteBuf WriteBytes(ByteBuf source)
    {
        ArgumentNullException.ThrowIfNull(source);

        WriteBytes(source, source.ReadableBytes);
        return this;
    }

    /**
     * Transfers the specified source buffer's data to this buffer starting at
     * the current {@code writerIndex} and increases the {@code writerIndex}
     * by the number of the transferred bytes (= {@code length}).
     * If {@code this.writableBytes} is less than {@code length}, {@link #ensureWritable(int)}
     * will be called in an attempt to expand capacity to accommodate.
     *
     * @param srcIndex the first index of the source
     * @param length   the number of bytes to transfer
     *
     * @throws IndexOutOfBoundsException
     *         if the specified {@code srcIndex} is less than {@code 0}, or
     *         if {@code srcIndex + length} is greater than {@code src.capacity}
     */
    /// <remarks>Like Netty, growth occurs before validating the absolute source range.
    /// A failed copy leaves the writer index unchanged but may leave increased capacity or partially written bytes.</remarks>
    public virtual ByteBuf WriteBytes(ByteBuf source, int sourceIndex, int length)
    {
        EnsureWritable(length);
        ArgumentNullException.ThrowIfNull(source);

        SetBytes(_writerIndex, source, sourceIndex, length);
        _writerIndex += length;
        return this;
    }
}
