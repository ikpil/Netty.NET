/*
 * Copyright 2013 The Netty Project
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
 * Copyright 2016 The Netty Project
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

using System;
using System.IO;
using System.Net.Sockets;
using Microsoft.Win32.SafeHandles;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers;
using System.Text;

namespace Netty.NET.Buffer;

/**
 * Abstract base class for {@link ByteBuf} implementations that wrap another
 * {@link ByteBuf}.
 *
 * @deprecated Do not use.
 */
// CLR: one private view represents fixed slices or dynamic-capacity duplicates.
// It delegates shared ownership to the parent and keeps independent indices.
/**
 * A special {@link AbstractUnpooledSlicedByteBuf} that can make optimizations because it knows the sliced buffer is of
 * type {@link AbstractByteBuf}.
 */
// CLR: bounded memory hooks provide the shared primitive path without that JVM subtype.
/**
 * A derived buffer which exposes its parent's sub-region only.  It is
 * recommended to use {@link ByteBuf#slice()} and
 * {@link ByteBuf#slice(int, int)} instead of calling the constructor
 * explicitly.
 *
 * @deprecated Do not use.
 */
// CLR: SlicedByteBuf's stored length and UnpooledSlicedByteBuf's capacity specialization
// both use the fixed MaxCapacity below. Slice factories replace the deprecated public constructor.
/**
 * {@link DuplicatedByteBuf} implementation that can do optimizations because it knows the duplicated buffer
 * is of type {@link AbstractByteBuf}.
 */
// CLR: bounded scalar storage access replaces the optimized JVM duplicate subtype.
internal sealed class ByteBufView : ByteBuf
{
    private readonly ByteBuf _parent;
    /**
     * Returns the index with the needed adjustment.
     */
    // CLR: each memory hook applies this adjustment inline.
    private readonly int _offset;
    private readonly bool _fixedCapacity;
    internal bool IsSlice => _fixedCapacity;
    /**
     * A derived buffer which simply forwards all data access requests to its
     * parent.  It is recommended to use {@link ByteBuf#duplicate()} instead
     * of calling the constructor explicitly.
     *
     * @deprecated Do not use.
     */
    internal ByteBufView(ByteBuf parent) : base((parent ?? throw new ArgumentNullException(nameof(parent))).MaxCapacity)
    {
        // DuplicatedByteBuf skips an existing duplicate, but retains a slice's
        // fixed range and any owner with its own reference-counting contract.
        _parent = parent is ByteBufView { IsSlice: false } duplicate ? duplicate._parent : parent;
        SetIndex(parent.ReaderIndex, parent.WriterIndex);
        MarkReaderIndex(); MarkWriterIndex();
    }
    internal ByteBufView(ByteBuf parent, int offset, int length) : base(length)
    {
        /**
         * Called by the constructor before {@link #writerIndex(int)}.
         * @param length the {@code length} argument from the constructor.
         */
        // CLR: the base constructor stores the length; no empty initLength override hook is needed.
        ArgumentNullException.ThrowIfNull(parent);
        _parent = parent; _offset = offset; _fixedCapacity = true;
        WriterIndex = length;
    }
    protected override Memory<byte> GetMemoryCore(int index, int length) => _parent.AsMemory(_offset + index, length);
    internal override byte ReadByteCore(int index)
    { CheckIndex(index, 1); return _parent.ReadByteCore(_offset + index); }
    internal override void WriteByteCore(int index, int value)
    { EnsureCanWrite(); CheckIndex(index, 1); _parent.WriteByteCore(_offset + index, value); }
    internal override ulong ReadWord(int index, int width, bool littleEndian)
    { CheckIndex(index, width); return _parent.ReadWord(_offset + index, width, littleEndian); }
    internal override ByteBuf SetWord(int index, ulong value, int width, bool littleEndian)
    { EnsureCanWrite(); CheckIndex(index, width); _parent.SetWord(_offset + index, value, width, littleEndian); return this; }
    protected override ReadOnlyMemory<byte> GetReadOnlyMemoryCore(int index, int length)
        => _parent.AsReadOnlyMemory(_offset + index, length);
    internal override BufferMemoryLease PinMemoryForWrite() => _parent.PinMemoryForWrite();
    protected override bool TryGetReadOnlyMemoryCore(int index, int length, out ReadOnlyMemory<byte> memory)
        => _parent.TryGetReadOnlyMemory(_offset + index, length, out memory);
    protected override void GetBytesCore(int index, Span<byte> destination)
        => _parent.GetBytes(_offset + index, destination);
    protected override ReadOnlySequence<byte> GetReadOnlySequenceCore(int index, int length)
        => _parent.AsReadOnlySequence(_offset + index, length);
    internal override ByteBuf UnwrapIterationStorage(ref int index)
    { index += _offset; return _parent.UnwrapIterationStorage(ref index); }
    internal override bool SupportsDirectBulkCopy => _parent.SupportsDirectBulkCopy;
    internal override ReadOnlySequence<byte> BorrowCopySequence(int index, int length)
    { CheckIndex(index, length); return _parent.BorrowCopySequence(_offset + index, length); }
    internal override BufferMemoryLease AcquireReadLease() => _parent.AcquireReadLease();
    protected override bool TryGetMemoryCore(int index, int length, out Memory<byte> memory)
        => _parent.TryGetMemory(_offset + index, length, out memory);
    protected override void SetBytesCore(int index, ReadOnlySpan<byte> source) => _parent.SetBytes(_offset + index, source);
    protected internal override void SetBytesFromIoCore(int index, ReadOnlySpan<byte> source)
    { EnsureCanWrite(); CheckIndex(index, source.Length); _parent.SetBytesFromIoCore(_offset + index, source); }
    protected override void SetZeroCore(int index, int length) => _parent.SetZero(_offset + index, length);
    public override IByteBufAllocator Allocator => _parent.Allocator;
    public override bool IsContiguous => _parent.IsContiguous;
    public override bool IsDirect => _parent.IsDirect;
    public override bool IsReadOnly => _parent.IsReadOnly;
    public override int Capacity
    {
        get => _fixedCapacity ? MaxCapacity : _parent.Capacity;
        set
        {
            EnsureCanWrite();
            if (_fixedCapacity) throw new NotSupportedException("A slice has fixed capacity.");
            _parent.Capacity = value;
        }
    }
    public override ByteBuf Unwrap() => _parent;
    public override string GetString(int index, int length, Encoding encoding)
    {
        if (!_fixedCapacity) return base.GetString(index, length, encoding);
        // AbstractUnpooledSlicedByteBuf.getCharSequence delegates to its parent.
        // CLR merges String/CharSequence reads into this one string API.
        ArgumentNullException.ThrowIfNull(encoding);
        CheckIndex(index, length);
        return _parent.GetString(_offset + index, length, encoding);
    }
    // AbstractByteBuf's readable overload uses its own loop, not the indexed virtual delegate.
    public override int ForEachByte(Func<byte, bool> processor)
        => base.ForEachByte(ReaderIndex, ReadableBytes, processor);
    public override int ForEachByteDesc(Func<byte, bool> processor)
        => base.ForEachByteDesc(ReaderIndex, ReadableBytes, processor);
    public override int ForEachByte(int index, int length, Func<byte, bool> processor)
    {
        if (_fixedCapacity) CheckIndex(index, length); else EnsureAccessible();
        int result = _parent.ForEachByte(_offset + index, length, processor);
        return _fixedCapacity ? result < _offset ? -1 : result - _offset : result;
    }
    public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
    {
        if (_fixedCapacity) CheckIndex(index, length); else EnsureAccessible();
        int result = _parent.ForEachByteDesc(_offset + index, length, processor);
        return _fixedCapacity ? result < _offset ? -1 : result - _offset : result;
    }
    public override ByteBuf Slice(int index, int length)
    {
        CheckIndex(index, length);
        // AbstractUnpooledSlicedByteBuf composes the adjustment on its parent.
        // Dispatch through that parent so read-only and lifetime-aware views
        // keep their own factory/ownership rules instead of being unpeeled.
        return _parent.Slice(_offset + index, length);
    }
    public override ByteBuf Duplicate()
    {
        EnsureAccessible();
        // A slice duplicates as another fixed slice with fresh zero marks;
        // an ordinary duplicate snapshots its current indices as initial marks.
        return _fixedCapacity ? Slice(0, Capacity).SetIndex(ReaderIndex, WriterIndex) : new ByteBufView(this);
    }
    public override ByteBuf Copy(int index, int length)
    {
        CheckIndex(index, length);
        // AbstractUnpooledSlicedByteBuf/DuplicatedByteBuf preserve the parent's
        // allocation policy and maximum; only the source coordinates are adjusted.
        return _parent.Copy(_offset + index, length);
    }
    // Preserve parent stream overrides and translated coordinates. CLR view
    // bounds/lifetime guards remain in front of dispatch, including duplicates.
    public override ByteBuf GetBytes(int index, Stream destination, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        _parent.GetBytes(_offset + index, destination, length);
        return this;
    }
    public override async ValueTask GetBytesAsync(int index, Stream destination, int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        await _parent.GetBytesAsync(_offset + index, destination, length, cancellationToken).ConfigureAwait(false);
    }
    public override int SetBytes(int index, Stream source, int length)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureCanWrite(); CheckIndex(index, length);
        return _parent.SetBytes(_offset + index, source, length);
    }
    public override async ValueTask<int> SetBytesAsync(int index, Stream source, int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureCanWrite(); CheckIndex(index, length);
        return await _parent.SetBytesAsync(_offset + index, source, length, cancellationToken).ConfigureAwait(false);
    }
    // Forward the channel policy with translated buffer coordinates; the file
    // position is independent of the view offset. Keep CLR guards before dispatch.
    public override int GetBytes(int index, Socket destination, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        return _parent.GetBytes(_offset + index, destination, length);
    }
    public override async ValueTask<int> GetBytesAsync(int index, Socket destination, int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        cancellationToken.ThrowIfCancellationRequested();
        return await _parent.GetBytesAsync(_offset + index, destination, length, cancellationToken).ConfigureAwait(false);
    }
    public override int SetBytes(int index, Socket source, int length)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureCanWrite();
        CheckIndex(index, length);
        return _parent.SetBytes(_offset + index, source, length);
    }
    public override async ValueTask<int> SetBytesAsync(int index, Socket source, int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureCanWrite();
        CheckIndex(index, length);
        cancellationToken.ThrowIfCancellationRequested();
        return await _parent.SetBytesAsync(_offset + index, source, length, cancellationToken).ConfigureAwait(false);
    }
    public override int GetBytes(int index, SafeFileHandle destination, long position, int length)
    {
        CheckIndex(index, length);
        CheckFileTransfer(destination, position, length);
        return _parent.GetBytes(_offset + index, destination, position, length);
    }
    public override async ValueTask<int> GetBytesAsync(int index, SafeFileHandle destination, long position, int length,
        CancellationToken cancellationToken = default)
    {
        CheckIndex(index, length);
        CheckFileTransfer(destination, position, length);
        cancellationToken.ThrowIfCancellationRequested();
        return await _parent.GetBytesAsync(_offset + index, destination, position, length, cancellationToken).ConfigureAwait(false);
    }
    public override int SetBytes(int index, SafeFileHandle source, long position, int length)
    {
        EnsureCanWrite();
        CheckIndex(index, length);
        CheckFileTransfer(source, position, length);
        return _parent.SetBytes(_offset + index, source, position, length);
    }
    public override async ValueTask<int> SetBytesAsync(int index, SafeFileHandle source, long position, int length,
        CancellationToken cancellationToken = default)
    {
        EnsureCanWrite();
        CheckIndex(index, length);
        CheckFileTransfer(source, position, length);
        cancellationToken.ThrowIfCancellationRequested();
        return await _parent.SetBytesAsync(_offset + index, source, position, length, cancellationToken).ConfigureAwait(false);
    }
    // Preserve the parent bulk overload and peer identity. Span replaces the
    // Java array/ByteBuffer range; relative operations still own their cursors.
    public override ByteBuf GetBytes(int index, Span<byte> destination)
    {
        CheckIndex(index, destination.Length);
        _parent.GetBytes(_offset + index, destination);
        return this;
    }
    public override ByteBuf SetBytes(int index, ReadOnlySpan<byte> source)
    {
        EnsureCanWrite();
        CheckIndex(index, source.Length);
        _parent.SetBytes(_offset + index, source);
        return this;
    }
    public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        _parent.GetBytes(_offset + index, destination, destinationIndex, length);
        return this;
    }
    public override ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureCanWrite();
        CheckIndex(index, length);
        _parent.SetBytes(_offset + index, source, sourceIndex, length);
        return this;
    }
    // Public absolute scalar dispatch differs from the optimized storage
    // kernel used by relative operations and slices.
    public override byte GetByte(int index)
    {
        CheckIndex(index, 1);
        if (_fixedCapacity) return base.GetByte(index);
        return _parent.GetByte(_offset + index);
    }
    public override ByteBuf SetByte(int index, int value)
    {
        EnsureCanWrite();
        CheckIndex(index, 1);
        if (_fixedCapacity) return base.SetByte(index, value);
        _parent.SetByte(_offset + index, value);
        return this;
    }
    public override short GetShort(int index)
    {
        CheckIndex(index, 2);
        if (_fixedCapacity) return base.GetShort(index);
        return _parent.GetShort(_offset + index);
    }
    public override ByteBuf SetShort(int index, int value)
    {
        EnsureCanWrite();
        CheckIndex(index, 2);
        if (_fixedCapacity) return base.SetShort(index, value);
        _parent.SetShort(_offset + index, value);
        return this;
    }
    public override short GetShortLE(int index)
    {
        CheckIndex(index, 2);
        if (_fixedCapacity) return base.GetShortLE(index);
        return _parent.GetShortLE(_offset + index);
    }
    public override ByteBuf SetShortLE(int index, int value)
    {
        EnsureCanWrite();
        CheckIndex(index, 2);
        if (_fixedCapacity) return base.SetShortLE(index, value);
        _parent.SetShortLE(_offset + index, value);
        return this;
    }
    public override int GetUnsignedMedium(int index)
    {
        CheckIndex(index, 3);
        if (_fixedCapacity) return base.GetUnsignedMedium(index);
        return _parent.GetUnsignedMedium(_offset + index);
    }
    public override ByteBuf SetMedium(int index, int value)
    {
        EnsureCanWrite();
        CheckIndex(index, 3);
        if (_fixedCapacity) return base.SetMedium(index, value);
        _parent.SetMedium(_offset + index, value);
        return this;
    }
    public override int GetUnsignedMediumLE(int index)
    {
        CheckIndex(index, 3);
        if (_fixedCapacity) return base.GetUnsignedMediumLE(index);
        return _parent.GetUnsignedMediumLE(_offset + index);
    }
    public override ByteBuf SetMediumLE(int index, int value)
    {
        EnsureCanWrite();
        CheckIndex(index, 3);
        if (_fixedCapacity) return base.SetMediumLE(index, value);
        _parent.SetMediumLE(_offset + index, value);
        return this;
    }
    public override int GetInt(int index)
    {
        CheckIndex(index, 4);
        if (_fixedCapacity) return base.GetInt(index);
        return _parent.GetInt(_offset + index);
    }
    public override ByteBuf SetInt(int index, int value)
    {
        EnsureCanWrite();
        CheckIndex(index, 4);
        if (_fixedCapacity) return base.SetInt(index, value);
        _parent.SetInt(_offset + index, value);
        return this;
    }
    public override int GetIntLE(int index)
    {
        CheckIndex(index, 4);
        if (_fixedCapacity) return base.GetIntLE(index);
        return _parent.GetIntLE(_offset + index);
    }
    public override ByteBuf SetIntLE(int index, int value)
    {
        EnsureCanWrite();
        CheckIndex(index, 4);
        if (_fixedCapacity) return base.SetIntLE(index, value);
        _parent.SetIntLE(_offset + index, value);
        return this;
    }
    public override long GetLong(int index)
    {
        CheckIndex(index, 8);
        if (_fixedCapacity) return base.GetLong(index);
        return _parent.GetLong(_offset + index);
    }
    public override ByteBuf SetLong(int index, long value)
    {
        EnsureCanWrite();
        CheckIndex(index, 8);
        if (_fixedCapacity) return base.SetLong(index, value);
        _parent.SetLong(_offset + index, value);
        return this;
    }
    public override long GetLongLE(int index)
    {
        CheckIndex(index, 8);
        if (_fixedCapacity) return base.GetLongLE(index);
        return _parent.GetLongLE(_offset + index);
    }
    public override ByteBuf SetLongLE(int index, long value)
    {
        EnsureCanWrite();
        CheckIndex(index, 8);
        if (_fixedCapacity) return base.SetLongLE(index, value);
        _parent.SetLongLE(_offset + index, value);
        return this;
    }
    public override int ReferenceCount => _parent.ReferenceCount;
    public override ByteBuf Retain(int increment = 1) { _parent.Retain(increment); return this; }
    public override bool Release(int decrement = 1) => _parent.Release(decrement);
    public override ByteBuf Touch(object hint = null) { _parent.Touch(hint); return this; }
}
