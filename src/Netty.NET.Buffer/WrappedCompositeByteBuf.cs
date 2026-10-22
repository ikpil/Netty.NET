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
using System.Buffers;
using System.Text;

namespace Netty.NET.Buffer;

// CLR: share the component list and index owner instead of duplicating every fluent
// operation. Storage/view/reference hooks delegate to the original composite so views
// do not acquire an extra wrapper parent or close a leak tracker twice.
internal class WrappedCompositeByteBuf : CompositeByteBuf
{
    protected readonly CompositeByteBuf Wrapped;
    internal WrappedCompositeByteBuf(CompositeByteBuf wrapped) : base(wrapped) => Wrapped = wrapped;

    public sealed override ByteBuf Unwrap() => Wrapped;
    public sealed override string ToString() => Wrapped.ToString();
    public override IByteBufAllocator Allocator => Wrapped.Allocator;
    public override int Capacity { get => Wrapped.Capacity; set => Wrapped.Capacity = value; }
    public override bool IsDirect => Wrapped.IsDirect;
    public override bool IsReadOnly => Wrapped.IsReadOnly;
    public override bool IsContiguous => Wrapped.IsContiguous;
    public override int MaxFastWritableBytes => Wrapped.MaxFastWritableBytes;
    public sealed override bool CanRead(int byteCount) => Wrapped.CanRead(byteCount);
    public override bool CanWrite(int byteCount) => Wrapped.CanWrite(byteCount);
    public override int ReferenceCount => Wrapped.ReferenceCount;
    public override CompositeByteBuf Retain(int increment = 1) { Wrapped.Retain(increment); return this; }
    public override bool Release(int decrement = 1) => Wrapped.Release(decrement);
    public override CompositeByteBuf Touch(object hint = null) { Wrapped.Touch(hint); return this; }

    protected override Memory<byte> GetMemoryCore(int index, int length) => Wrapped.AsMemory(index, length);
    protected override ReadOnlyMemory<byte> GetReadOnlyMemoryCore(int index, int length)
        => Wrapped.AsReadOnlyMemory(index, length);
    protected override bool TryGetMemoryCore(int index, int length, out Memory<byte> memory)
        => Wrapped.TryGetMemory(index, length, out memory);
    protected override bool TryGetReadOnlyMemoryCore(int index, int length, out ReadOnlyMemory<byte> memory)
        => Wrapped.TryGetReadOnlyMemory(index, length, out memory);
    protected override void GetBytesCore(int index, Span<byte> destination) => Wrapped.GetBytes(index, destination);
    protected override void SetBytesCore(int index, ReadOnlySpan<byte> source) => Wrapped.SetBytes(index, source);
    protected override void SetZeroCore(int index, int length) => Wrapped.SetZero(index, length);
    protected override ReadOnlySequence<byte> GetReadOnlySequenceCore(int index, int length)
        => Wrapped.AsReadOnlySequence(index, length);
    internal override BufferMemoryLease AcquireReadLease() => Wrapped.AcquireReadLease();
    internal override BufferMemoryLease PinMemoryForWrite() => Wrapped.PinMemoryForWrite();

    public override ByteBuf Copy(int index, int length) => Wrapped.Copy(index, length);
    public override ByteBuf ReadBytes(int length) => Wrapped.ReadBytes(length);
    public override int IndexOf(int fromIndex, int toIndex, byte value) => Wrapped.IndexOf(fromIndex, toIndex, value);
    public override int BytesBefore(byte value) => Wrapped.BytesBefore(value);
    public override int BytesBefore(int length, byte value) => Wrapped.BytesBefore(length, value);
    public override int BytesBefore(int index, int length, byte value) => Wrapped.BytesBefore(index, length, value);
    public override string GetString(Encoding encoding) => Wrapped.GetString(encoding);
    public override string GetString(int index, int length, Encoding encoding)
        => Wrapped.GetString(index, length, encoding);
    public override string ReadString(int length, Encoding encoding) => Wrapped.ReadString(length, encoding);
    public override int SetString(int index, ReadOnlySpan<char> text, Encoding encoding)
        => Wrapped.SetString(index, text, encoding);
    public override int WriteString(ReadOnlySpan<char> text, Encoding encoding) => Wrapped.WriteString(text, encoding);
    public override int ForEachByte(Func<byte, bool> processor) => Wrapped.ForEachByte(processor);
    public override int ForEachByte(int index, int length, Func<byte, bool> processor)
        => Wrapped.ForEachByte(index, length, processor);
    public override int ForEachByteDesc(Func<byte, bool> processor) => Wrapped.ForEachByteDesc(processor);
    public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
        => Wrapped.ForEachByteDesc(index, length, processor);
    public override ByteBuf Slice() => Wrapped.Slice();
    public override ByteBuf Slice(int index, int length) => Wrapped.Slice(index, length);
    public override ByteBuf RetainedSlice() => Wrapped.RetainedSlice();
    public override ByteBuf RetainedSlice(int index, int length) => Wrapped.RetainedSlice(index, length);
    public override ByteBuf Duplicate() => Wrapped.Duplicate();
    public override ByteBuf RetainedDuplicate() => Wrapped.RetainedDuplicate();
    public override ByteBuf ReadSlice(int length) => Wrapped.ReadSlice(length);
    public override ByteBuf ReadRetainedSlice(int length) => Wrapped.ReadRetainedSlice(length);
    public override ByteBuf AsReadOnly() => Wrapped.AsReadOnly();
    public override CompositeByteBuf DiscardReadBytes() { Wrapped.DiscardReadBytes(); return this; }
    public override CompositeByteBuf DiscardSomeReadBytes() { Wrapped.DiscardSomeReadBytes(); return this; }
    public override CompositeByteBuf EnsureWritable(int minimumWritableBytes) { Wrapped.EnsureWritable(minimumWritableBytes); return this; }
    public override int EnsureWritable(int minimumWritableBytes, bool force) => Wrapped.EnsureWritable(minimumWritableBytes, force);
    public override ByteBuf SetZero(int index, int length) { Wrapped.SetZero(index, length); return this; }
    public override ByteBuf WriteZero(int length) { Wrapped.WriteZero(length); return this; }

    public override int GetHashCode() => Wrapped.GetHashCode();
    public override bool Equals(object obj) => ReferenceEquals(this, obj) || Wrapped.Equals(obj);
    public override bool Equals(ByteBuf other) => ReferenceEquals(this, other) || Wrapped.Equals(other);
    public override int CompareTo(ByteBuf other) => ReferenceEquals(this, other) ? 0 : Wrapped.CompareTo(other);
}
