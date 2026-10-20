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

using System;
using System.IO;
using System.Net.Sockets;
using Microsoft.Win32.SafeHandles;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Netty.NET.Common;

namespace Netty.NET.Buffer;

/**
 * Abstract base class for derived {@link ByteBuf} implementations.
 */
// CLR: one private memory view represents pooled slices/duplicates and their borrowed
// derivatives. Retained instances use the atomic base counter; borrowed instances
// delegate reference operations. Wrappers are never recycled or resurrected.
internal sealed class PooledDerivedByteBuf : AbstractReferenceCountedByteBuf
{
    private ByteBuf _root;
    /**
     * Deallocations of a pooled derived buffer should always propagate through the entire chain of derived buffers.
     * This is because each pooled derived buffer maintains its own reference count and we should respect each one.
     * If deallocations cause a release of the "root parent" then we may prematurely release the underlying
     * content before all the derived buffers have been released.
     */
    private ByteBuf _parent;
    private readonly ByteBuf _referenceCountDelegate;
    private readonly int _offset;
    private readonly bool _fixedCapacity;
    internal bool IsIndependent => _referenceCountDelegate == null;
    private ByteBuf Root => _root ?? throw new IllegalReferenceCountException();

    private PooledDerivedByteBuf(ByteBuf root, ByteBuf parent, int offset, int maximum,
        int reader, int writer, bool fixedCapacity, bool retained) : base(maximum)
    {
        if (retained) parent.Retain(); // Retain up front to ensure the parent is accessible before doing more work.
        _root = root;
        _offset = offset;
        _fixedCapacity = fixedCapacity;
        if (retained) _parent = parent; else _referenceCountDelegate = parent;
        try
        {
            SetIndex(reader, writer); // It is assumed the bounds checking is done by the caller.
            // CLR keeps its index invariants checked, and each new owned wrapper starts at count one.
            if (!fixedCapacity) { MarkReaderIndex(); MarkWriterIndex(); }
        }
        catch
        {
            _parent = _root = null;
            if (retained) parent.Release();
            throw;
        }
    }

    internal static PooledDerivedByteBuf NewSlice(ByteBuf root, ByteBuf parent, int index, int length)
    {
        if (index < 0 || length < 0 || index > root.Capacity - length)
            throw new ArgumentOutOfRangeException(nameof(index));
        return new(root, parent, index, length, 0, length, true, true);
    }

    internal static PooledDerivedByteBuf NewDuplicate(ByteBuf root, ByteBuf parent, int reader, int writer)
        => new(root, parent, 0, root.MaxCapacity, reader, writer, false, true);

    // Called from within SimpleLeakAwareByteBuf and AdvancedLeakAwareByteBuf.
    internal void SetParent(SimpleLeakAwareByteBuf parent)
    {
        Debug.Assert(IsIndependent);
        _parent = parent;
    }

    public override ByteBuf Unwrap() => Root;
    public override IByteBufAllocator Allocator => Root.Allocator;
    public override bool IsDirect => Root.IsDirect;
    public override bool IsReadOnly => Root.IsReadOnly;
    public override bool IsContiguous => Root.IsContiguous;
    public override int Capacity
    {
        get => _fixedCapacity ? MaxCapacity : Root.Capacity;
        set
        {
            EnsureCanWrite();
            if (_fixedCapacity) throw new NotSupportedException("A slice has fixed capacity.");
            Root.Capacity = value;
        }
    }
    protected override Memory<byte> GetMemoryCore(int index, int length) => Root.AsMemory(_offset + index, length);
    protected override ReadOnlyMemory<byte> GetReadOnlyMemoryCore(int index, int length)
        => Root.AsReadOnlyMemory(_offset + index, length);
    internal override BufferMemoryLease AcquireReadLease() { EnsureAccessible(); return Root.AcquireReadLease(); }
    internal override BufferMemoryLease PinMemoryForWrite() { EnsureCanWrite(); return Root.PinMemoryForWrite(); }
    public override ByteBuf Copy(int index, int length) { CheckIndex(index, length); return Root.Copy(_offset + index, length); }
    // Readable iteration stays on this view; indexed iteration follows the original root delegates.
    public override int ForEachByte(Func<byte, bool> processor)
        => base.ForEachByte(ReaderIndex, ReadableBytes, processor);
    public override int ForEachByteDesc(Func<byte, bool> processor)
        => base.ForEachByteDesc(ReaderIndex, ReadableBytes, processor);
    public override int ForEachByte(int index, int length, Func<byte, bool> processor)
    {
        if (_fixedCapacity) CheckIndex(index, length); else EnsureAccessible();
        int result = Root.ForEachByte(_offset + index, length, processor);
        return _fixedCapacity ? result < _offset ? -1 : result - _offset : result;
    }
    public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
    {
        if (_fixedCapacity) CheckIndex(index, length); else EnsureAccessible();
        int result = Root.ForEachByteDesc(_offset + index, length, processor);
        return _fixedCapacity ? result < _offset ? -1 : result - _offset : result;
    }
    // Preserve parent stream overrides and translated coordinates. CLR view
    // bounds/lifetime guards remain in front of dispatch, including duplicates.
    public override ByteBuf GetBytes(int index, Stream destination, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        Root.GetBytes(_offset + index, destination, length);
        return this;
    }
    public override async ValueTask GetBytesAsync(int index, Stream destination, int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        await Root.GetBytesAsync(_offset + index, destination, length, cancellationToken).ConfigureAwait(false);
    }
    public override int SetBytes(int index, Stream source, int length)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureCanWrite(); CheckIndex(index, length);
        return Root.SetBytes(_offset + index, source, length);
    }
    public override async ValueTask<int> SetBytesAsync(int index, Stream source, int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureCanWrite(); CheckIndex(index, length);
        return await Root.SetBytesAsync(_offset + index, source, length, cancellationToken).ConfigureAwait(false);
    }
    // Forward the channel policy with translated buffer coordinates; the file
    // position is independent of the view offset. Keep CLR guards before dispatch.
    public override int GetBytes(int index, Socket destination, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        return Root.GetBytes(_offset + index, destination, length);
    }
    public override async ValueTask<int> GetBytesAsync(int index, Socket destination, int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        cancellationToken.ThrowIfCancellationRequested();
        return await Root.GetBytesAsync(_offset + index, destination, length, cancellationToken).ConfigureAwait(false);
    }
    public override int SetBytes(int index, Socket source, int length)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureCanWrite();
        CheckIndex(index, length);
        return Root.SetBytes(_offset + index, source, length);
    }
    public override async ValueTask<int> SetBytesAsync(int index, Socket source, int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureCanWrite();
        CheckIndex(index, length);
        cancellationToken.ThrowIfCancellationRequested();
        return await Root.SetBytesAsync(_offset + index, source, length, cancellationToken).ConfigureAwait(false);
    }
    public override int GetBytes(int index, SafeFileHandle destination, long position, int length)
    {
        CheckIndex(index, length);
        CheckFileTransfer(destination, position, length);
        return Root.GetBytes(_offset + index, destination, position, length);
    }
    public override async ValueTask<int> GetBytesAsync(int index, SafeFileHandle destination, long position, int length,
        CancellationToken cancellationToken = default)
    {
        CheckIndex(index, length);
        CheckFileTransfer(destination, position, length);
        cancellationToken.ThrowIfCancellationRequested();
        return await Root.GetBytesAsync(_offset + index, destination, position, length, cancellationToken).ConfigureAwait(false);
    }
    public override int SetBytes(int index, SafeFileHandle source, long position, int length)
    {
        EnsureCanWrite();
        CheckIndex(index, length);
        CheckFileTransfer(source, position, length);
        return Root.SetBytes(_offset + index, source, position, length);
    }
    public override async ValueTask<int> SetBytesAsync(int index, SafeFileHandle source, long position, int length,
        CancellationToken cancellationToken = default)
    {
        EnsureCanWrite();
        CheckIndex(index, length);
        CheckFileTransfer(source, position, length);
        cancellationToken.ThrowIfCancellationRequested();
        return await Root.SetBytesAsync(_offset + index, source, position, length, cancellationToken).ConfigureAwait(false);
    }
    // Preserve the parent bulk overload and peer identity. Span replaces the
    // Java array/ByteBuffer range; relative operations still own their cursors.
    public override ByteBuf GetBytes(int index, Span<byte> destination)
    {
        CheckIndex(index, destination.Length);
        Root.GetBytes(_offset + index, destination);
        return this;
    }
    public override ByteBuf SetBytes(int index, ReadOnlySpan<byte> source)
    {
        EnsureCanWrite();
        CheckIndex(index, source.Length);
        Root.SetBytes(_offset + index, source);
        return this;
    }
    public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        Root.GetBytes(_offset + index, destination, destinationIndex, length);
        return this;
    }
    public override ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureCanWrite();
        CheckIndex(index, length);
        Root.SetBytes(_offset + index, source, sourceIndex, length);
        return this;
    }
    public override int ReferenceCount => _referenceCountDelegate?.ReferenceCount ?? base.ReferenceCount;
    public override ByteBuf Retain(int increment = 1)
    {
        if (IsIndependent) base.Retain(increment); else _referenceCountDelegate.Retain(increment);
        return this;
    }
    public override bool Release(int decrement = 1)
        => IsIndependent ? base.Release(decrement) : _referenceCountDelegate.Release(decrement);
    public override ByteBuf Touch(object hint = null)
    {
        if (!IsIndependent) _referenceCountDelegate.Touch(hint);
        return this;
    }

    public override ByteBuf Slice(int index, int length)
    {
        CheckIndex(index, length);
        // All reference count methods should be inherited from this object (this is the "parent").
        return new PooledDerivedByteBuf(Root, _referenceCountDelegate ?? this, _offset + index,
            length, 0, length, true, false);
    }
    public override ByteBuf RetainedSlice() => RetainedSlice(ReaderIndex, ReadableBytes);
    public override ByteBuf RetainedSlice(int index, int length)
    {
        // The original non-retained slice validates the translated root coordinates.
        // Its retained child may therefore extend outside the borrowed slice's own range.
        if (IsIndependent) CheckIndex(index, length);
        long translated = (long)_offset + index;
        if (translated < 0 || translated > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(index));
        return NewSlice(Root, this, (int)translated, length);
    }
    public override ByteBuf Duplicate()
    {
        EnsureAccessible();
        if (IsIndependent && _fixedCapacity) return Slice(0, Capacity).SetIndex(ReaderIndex, WriterIndex);
        // All reference count methods should be inherited from this object (this is the "parent").
        ByteBuf marks = !IsIndependent && !_fixedCapacity ? this : Root;
        return new PooledDerivedByteBuf(Root, _referenceCountDelegate ?? this, 0, Root.MaxCapacity,
            marks.ReaderIndex, marks.WriterIndex, false, false).SetIndex(_offset + ReaderIndex, _offset + WriterIndex);
    }
    public override ByteBuf RetainedDuplicate()
    {
        EnsureAccessible();
        return IsIndependent && _fixedCapacity
            ? RetainedSlice(0, Capacity).SetIndex(ReaderIndex, WriterIndex)
            : NewDuplicate(Root, this, _offset + ReaderIndex, _offset + WriterIndex);
    }
    protected override void Deallocate()
    {
        // We need to first store a reference to the parent before recycle this instance. This is needed as
        // otherwise it is possible that the same AbstractPooledDerivedByteBuf is again obtained and init(...) is
        // called before we actually have a chance to call release(). This leads to call release() on the wrong parent.
        // CLR: wrapper recycling is deliberately absent, while release still targets the immediate parent.
        ByteBuf parent = _parent;
        // Remove references to parent and root so that they can be GCed for leak detection [netty/netty#14247]
        _parent = _root = null;
        parent.Release();
    }
}
