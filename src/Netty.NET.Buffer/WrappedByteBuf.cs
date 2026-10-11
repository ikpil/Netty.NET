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
 * Wraps another {@link ByteBuf}.
 *
 * It's important that the {@link #readerIndex()} and {@link #writerIndex()} will not do any adjustments on the
 * indices on the fly because of internal optimizations made by {@link ByteBufUtil#writeAscii(ByteBuf, CharSequence)}
 * and {@link ByteBufUtil#writeUtf8(ByteBuf, CharSequence)}.
 */
/// <remarks>CLR forwarding uses shared indices and bounded memory hooks rather than
/// duplicating every primitive operation. Views returned by this base are unwrapped;
/// lifetime-aware subclasses wrap them as needed. Java order/address facades remain pending.</remarks>
public class WrappedByteBuf : ByteBuf
{
    protected readonly ByteBuf Buffer;

    protected WrappedByteBuf(ByteBuf buffer) : base(buffer) => Buffer = buffer;

    public override int Capacity { get => Buffer.Capacity; set => Buffer.Capacity = value; }
    public override IByteBufAllocator Allocator => Buffer.Allocator;
    public override bool IsContiguous => Buffer.IsContiguous;
    public override int MaxFastWritableBytes => Buffer.MaxFastWritableBytes;
    public override bool IsDirect => Buffer.IsDirect;
    public override bool IsReadOnly => Buffer.IsReadOnly;
    public override bool CanWrite(int byteCount) => Buffer.CanWrite(byteCount);
    public override ByteBuf Unwrap() => Buffer;
    public override int ReferenceCount => Buffer.ReferenceCount;
    public override ByteBuf Retain(int increment = 1) { Buffer.Retain(increment); return this; }
    public override bool Release(int decrement = 1) => Buffer.Release(decrement);
    public override ByteBuf Touch(object hint = null) { Buffer.Touch(hint); return this; }

    protected override Memory<byte> GetMemoryCore(int index, int length) => Buffer.AsMemory(index, length);
    protected override ReadOnlyMemory<byte> GetReadOnlyMemoryCore(int index, int length)
        => Buffer.AsReadOnlyMemory(index, length);
    protected override bool TryGetMemoryCore(int index, int length, out Memory<byte> memory)
        => Buffer.TryGetMemory(index, length, out memory);
    protected override bool TryGetReadOnlyMemoryCore(int index, int length, out ReadOnlyMemory<byte> memory)
        => Buffer.TryGetReadOnlyMemory(index, length, out memory);
    protected override void GetBytesCore(int index, Span<byte> destination) => Buffer.GetBytes(index, destination);
    protected override void SetBytesCore(int index, ReadOnlySpan<byte> source) => Buffer.SetBytes(index, source);
    protected override void SetZeroCore(int index, int length) => Buffer.SetZero(index, length);
    protected override ReadOnlySequence<byte> GetReadOnlySequenceCore(int index, int length)
        => Buffer.AsReadOnlySequence(index, length);
    internal override BufferMemoryLease AcquireReadLease() => Buffer.AcquireReadLease();
    internal override BufferMemoryLease PinMemoryForWrite() => Buffer.PinMemoryForWrite();

    public override ByteBuf Copy(int index, int length) => Buffer.Copy(index, length);
    public override int IndexOf(int fromIndex, int toIndex, byte value) => Buffer.IndexOf(fromIndex, toIndex, value);
    public override ByteBuf DiscardReadBytes() { Buffer.DiscardReadBytes(); return this; }
    public override ByteBuf DiscardSomeReadBytes() { Buffer.DiscardSomeReadBytes(); return this; }
    public override ByteBuf AsReadOnly() => Buffer.AsReadOnly();
    public override ByteBuf Slice() => Buffer.Slice();
    public override ByteBuf Slice(int index, int length) => Buffer.Slice(index, length);
    public override ByteBuf RetainedSlice() => Buffer.RetainedSlice();
    public override ByteBuf RetainedSlice(int index, int length) => Buffer.RetainedSlice(index, length);
    public override ByteBuf Duplicate() => Buffer.Duplicate();
    public override ByteBuf RetainedDuplicate() => Buffer.RetainedDuplicate();
    public override ByteBuf ReadSlice(int length) => Buffer.ReadSlice(length);
    public override ByteBuf ReadRetainedSlice(int length) => Buffer.ReadRetainedSlice(length);
}
