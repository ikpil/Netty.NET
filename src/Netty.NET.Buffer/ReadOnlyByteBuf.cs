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
using System.IO;
using System.Net.Sockets;
using Microsoft.Win32.SafeHandles;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers;

namespace Netty.NET.Buffer;

/// <summary>A live read-only view. Indices are independent; data and ownership belong to the parent.</summary>
// CLR: one sealed view plus bounded ReadOnlyMemory replaces the deprecated wrapper and its unchecked JVM subtype.
/**
 * A derived buffer which forbids any write requests to its parent.  It is
 * recommended to use {@link Unpooled#unmodifiableBuffer(ByteBuf)}
 * instead of calling the constructor explicitly.
 *
 * @deprecated Do not use.
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
/**
 * Specialized {@link ReadOnlyByteBuf} sub-type which allows fast access to parent
 * without extra bound-checks.
 */
// CLR: the original subtype rationale above is retained; bounded spans replace its unchecked JVM dispatch.
internal sealed class ReadOnlyByteBuf : ByteBuf
{
    private readonly ByteBuf _parent;

    internal ReadOnlyByteBuf(ByteBuf parent)
        : base(parent?.MaxCapacity ?? throw new ArgumentNullException(nameof(parent)))
    {
        _parent = parent is ReadOnlyByteBuf readOnly ? readOnly._parent
            : parent is ByteBufView { IsSlice: false } duplicate ? duplicate.Unwrap() : parent;
        SetIndex(parent.ReaderIndex, parent.WriterIndex);
    }

    public override bool IsReadOnly => true;
    protected override bool TryGetReadOnlyMemoryCore(int index, int length, out ReadOnlyMemory<byte> memory)
        => _parent.TryGetReadOnlyMemory(index, length, out memory);
    protected override void GetBytesCore(int index, Span<byte> destination)
        => _parent.GetBytes(index, destination);
    protected override ReadOnlySequence<byte> GetReadOnlySequenceCore(int index, int length)
        => _parent.AsReadOnlySequence(index, length);
    internal override BufferMemoryLease AcquireReadLease() => _parent.AcquireReadLease();
    public override IByteBufAllocator Allocator => _parent.Allocator;
    public override bool IsContiguous => _parent.IsContiguous;
    public override bool IsDirect => _parent.IsDirect;
    public override int Capacity
    {
        get => _parent.Capacity;
        set { EnsureCanWrite(); }
    }
    public override ByteBuf Unwrap() => _parent;
    protected override Memory<byte> GetMemoryCore(int index, int length)
        => throw new NotSupportedException("The buffer is read-only.");
    protected override ReadOnlyMemory<byte> GetReadOnlyMemoryCore(int index, int length)
        => _parent.AsReadOnlyMemory(index, length);
    // Preserve parent stream overrides and translated coordinates. CLR view
    // bounds/lifetime guards remain in front of dispatch, including duplicates.
    public override ByteBuf GetBytes(int index, Stream destination, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        _parent.GetBytes(index, destination, length);
        return this;
    }
    public override async ValueTask GetBytesAsync(int index, Stream destination, int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        await _parent.GetBytesAsync(index, destination, length, cancellationToken).ConfigureAwait(false);
    }
    // Forward the channel policy with translated buffer coordinates; the file
    // position is independent of the view offset. Keep CLR guards before dispatch.
    public override int GetBytes(int index, Socket destination, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        return _parent.GetBytes(index, destination, length);
    }
    public override async ValueTask<int> GetBytesAsync(int index, Socket destination, int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        cancellationToken.ThrowIfCancellationRequested();
        return await _parent.GetBytesAsync(index, destination, length, cancellationToken).ConfigureAwait(false);
    }
    public override int GetBytes(int index, SafeFileHandle destination, long position, int length)
    {
        CheckIndex(index, length);
        CheckFileTransfer(destination, position, length);
        return _parent.GetBytes(index, destination, position, length);
    }
    public override async ValueTask<int> GetBytesAsync(int index, SafeFileHandle destination, long position, int length,
        CancellationToken cancellationToken = default)
    {
        CheckIndex(index, length);
        CheckFileTransfer(destination, position, length);
        cancellationToken.ThrowIfCancellationRequested();
        return await _parent.GetBytesAsync(index, destination, position, length, cancellationToken).ConfigureAwait(false);
    }
    public override int ReferenceCount => _parent.ReferenceCount;
    public override ByteBuf Retain(int increment = 1) { _parent.Retain(increment); return this; }
    public override bool Release(int decrement = 1) => _parent.Release(decrement);
    public override ByteBuf Touch(object hint = null) { _parent.Touch(hint); return this; }

    public override ByteBuf Copy(int index, int length) => _parent.Copy(index, length);
    // Keep readable iteration on the view's own loop, as in AbstractByteBuf.
    public override int ForEachByte(Func<byte, bool> processor)
        => base.ForEachByte(ReaderIndex, ReadableBytes, processor);
    public override int ForEachByteDesc(Func<byte, bool> processor)
        => base.ForEachByteDesc(ReaderIndex, ReadableBytes, processor);
    public override int ForEachByte(int index, int length, Func<byte, bool> processor)
    {
        EnsureAccessible();
        return _parent.ForEachByte(index, length, processor);
    }
    public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
    {
        EnsureAccessible();
        return _parent.ForEachByteDesc(index, length, processor);
    }
    public override ByteBuf Slice(int index, int length)
    {
        CheckIndex(index, length);
        return new ReadOnlyByteBuf(_parent.Slice(index, length));
    }
    public override ByteBuf Duplicate()
    {
        EnsureAccessible();
        return new ReadOnlyByteBuf(this);
    }
}
