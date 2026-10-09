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

using System;
using System.Buffers;

namespace Netty.NET.Buffer;

/**
 * A {@link ByteBuf} implementation that wraps another buffer to prevent a user from increasing or decreasing the
 * wrapped buffer's reference count.
 */
/// <remarks>This borrowed facade does not retain its parent or make it immortal.
/// The caller must keep the underlying owner alive. Indices/marks and storage are
/// shared with the wrapped buffer. Copies have ordinary independent ownership.
/// Explicit BE/LE operations replace Java's mutable byte-order facade.</remarks>
internal sealed class UnreleasableByteBuf : ByteBuf
{
    private readonly ByteBuf _parent;

    internal UnreleasableByteBuf(ByteBuf parent) : base(parent)
    {
        _parent = parent is UnreleasableByteBuf unreleasable ? unreleasable._parent : parent;
    }

    public override int Capacity { get => _parent.Capacity; set => _parent.Capacity = value; }
    public override IByteBufAllocator Allocator => _parent.Allocator;
    public override bool IsDirect => _parent.IsDirect;
    public override bool IsReadOnly => _parent.IsReadOnly;
    public override bool CanWrite(int byteCount) => _parent.CanWrite(byteCount);
    public override ByteBuf Unwrap() => _parent;
    public override int ReferenceCount => _parent.ReferenceCount;
    // Original no-ops ignore even nonpositive counts and do not resurrect a dead owner.
    public override ByteBuf Retain(int increment = 1) => this;
    public override bool Release(int decrement = 1) => false;
    public override ByteBuf Touch(object hint = null) => this;

    protected override Memory<byte> GetMemoryCore(int index, int length) => _parent.AsMemory(index, length);
    protected override ReadOnlyMemory<byte> GetReadOnlyMemoryCore(int index, int length)
        => _parent.AsReadOnlyMemory(index, length);
    protected override bool TryGetMemoryCore(int index, int length, out Memory<byte> memory)
        => _parent.TryGetMemory(index, length, out memory);
    protected override bool TryGetReadOnlyMemoryCore(int index, int length, out ReadOnlyMemory<byte> memory)
        => _parent.TryGetReadOnlyMemory(index, length, out memory);
    protected override void GetBytesCore(int index, Span<byte> destination) => _parent.GetBytes(index, destination);
    protected override void SetBytesCore(int index, ReadOnlySpan<byte> source) => _parent.SetBytes(index, source);
    protected override void SetZeroCore(int index, int length) => _parent.SetZero(index, length);
    protected override ReadOnlySequence<byte> GetReadOnlySequenceCore(int index, int length)
        => _parent.AsReadOnlySequence(index, length);
    internal override BufferMemoryLease AcquireReadLease() => _parent.AcquireReadLease();
    internal override BufferMemoryLease PinMemoryForWrite() => _parent.PinMemoryForWrite();

    public override ByteBuf Copy(int index, int length) => _parent.Copy(index, length);
    public override int IndexOf(int fromIndex, int toIndex, byte value)
        => _parent.IndexOf(fromIndex, toIndex, value);
    public override ByteBuf DiscardReadBytes() { _parent.DiscardReadBytes(); return this; }
    public override ByteBuf DiscardSomeReadBytes() { _parent.DiscardSomeReadBytes(); return this; }
    public override ByteBuf AsReadOnly()
        => _parent.IsReadOnly ? this : new UnreleasableByteBuf(_parent.AsReadOnly());
    public override ByteBuf ReadSlice(int length) => new UnreleasableByteBuf(_parent.ReadSlice(length));
    public override ByteBuf ReadRetainedSlice(int length)
    {
        // We could call buf.readSlice(..), and then call buf.release(). However this creates a leak in unit tests
        // because the release method on UnreleasableByteBuf will never allow the leak record to be cleaned up.
        // So we just use readSlice(..) because the end result should be logically equivalent.
        return ReadSlice(length);
    }
    public override ByteBuf Slice() => new UnreleasableByteBuf(_parent.Slice());
    public override ByteBuf RetainedSlice()
    {
        // We could call buf.retainedSlice(), and then call buf.release(). However this creates a leak in unit tests
        // because the release method on UnreleasableByteBuf will never allow the leak record to be cleaned up.
        // So we just use slice() because the end result should be logically equivalent.
        return Slice();
    }
    public override ByteBuf Slice(int index, int length) => new UnreleasableByteBuf(_parent.Slice(index, length));
    public override ByteBuf RetainedSlice(int index, int length)
    {
        // We could call buf.retainedSlice(..), and then call buf.release(). However this creates a leak in unit tests
        // because the release method on UnreleasableByteBuf will never allow the leak record to be cleaned up.
        // So we just use slice(..) because the end result should be logically equivalent.
        return Slice(index, length);
    }
    public override ByteBuf Duplicate() => new UnreleasableByteBuf(_parent.Duplicate());
    public override ByteBuf RetainedDuplicate()
    {
        // We could call buf.retainedDuplicate(), and then call buf.release(). However this creates a leak in unit tests
        // because the release method on UnreleasableByteBuf will never allow the leak record to be cleaned up.
        // So we just use duplicate() because the end result should be logically equivalent.
        return Duplicate();
    }
}
