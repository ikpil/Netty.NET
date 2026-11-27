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
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer;

// CLR: share the component list and index owner for nonvirtual metadata. Public
// virtual operations delegate to the original composite; storage/view/reference hooks
// do not acquire an extra wrapper parent or close a leak tracker twice.
internal partial class WrappedCompositeByteBuf : CompositeByteBuf
{
    protected readonly CompositeByteBuf Wrapped;
    internal WrappedCompositeByteBuf(CompositeByteBuf wrapped) : base(wrapped)
    {
        Wrapped = wrapped;
    }

    public sealed override ByteBuf Unwrap()
    {
        return Wrapped;
    }

    public sealed override string ToString()
    {
        return Wrapped.ToString();
    }

    public override IByteBufAllocator Alloc()
    {
        return Wrapped.Alloc();
    }
    public override int Capacity { get => Wrapped.Capacity; set => Wrapped.Capacity = value; }
    public override bool IsDirect => Wrapped.IsDirect;
    public override bool IsReadOnly => Wrapped.IsReadOnly;
    public override bool IsContiguous => Wrapped.IsContiguous;
    public override int MaxFastWritableBytes => Wrapped.MaxFastWritableBytes;
    public sealed override bool IsReadable(int size)
    {
        return Wrapped.IsReadable(size);
    }

    public override bool IsWritable(int size)
    {
        return Wrapped.IsWritable(size);
    }
    public override int RefCnt => Wrapped.RefCnt;
    public override CompositeByteBuf Retain(int increment = 1)
    {
        Wrapped.Retain(increment);
        return this;
    }

    public override bool Release(int decrement = 1)
    {
        return Wrapped.Release(decrement);
    }

    public override CompositeByteBuf Touch(object hint = null)
    {
        Wrapped.Touch(hint);
        return this;
    }

    protected override Memory<byte> GetMemoryCore(int index, int length)
    {
        return Wrapped.AsMemory(index, length);
    }

    protected override ReadOnlyMemory<byte> GetReadOnlyMemoryCore(int index, int length)
    {
        return Wrapped.AsReadOnlyMemory(index, length);
    }

    protected override bool TryGetMemoryCore(int index, int length, out Memory<byte> memory)
    {
        return Wrapped.TryGetMemory(index, length, out memory);
    }

    protected override bool TryGetReadOnlyMemoryCore(int index, int length, out ReadOnlyMemory<byte> memory)
    {
        return Wrapped.TryGetReadOnlyMemory(index, length, out memory);
    }

    protected override void GetBytesCore(int index, Span<byte> destination)
    {
        Wrapped.GetBytes(index, destination);
    }

    protected override void SetBytesCore(int index, ReadOnlySpan<byte> source)
    {
        Wrapped.SetBytes(index, source);
    }

    protected internal override void SetBytesFromIoCore(int index, ReadOnlySpan<byte> source)
    {
        Wrapped.SetBytesFromIoCore(index, source);
    }

    protected override void SetZeroCore(int index, int length)
    {
        Wrapped.SetZero(index, length);
    }

    // Protected Java _get*/_set* delegates remain separate from public primitives.
    // They are used by derived views and must preserve the wrapped storage kernels.
    internal override byte ReadByteCore(int index)
    {
        return Wrapped.ReadByteCore(index);
    }

    internal override void WriteByteCore(int index, int value)
    {
        Wrapped.WriteByteCore(index, value);
    }

    internal override ulong ReadWord(int index, int width, bool littleEndian)
    {
        return Wrapped.ReadWord(index, width, littleEndian);
    }

    internal override ByteBuf SetWord(int index, ulong value, int width, bool littleEndian)
    {
        Wrapped.SetWord(index, value, width, littleEndian);
        return this;
    }

    protected override ReadOnlySequence<byte> GetReadOnlySequenceCore(int index, int length)
    {
        return Wrapped.AsReadOnlySequence(index, length);
    }

    internal override int ForEachByteAscending(int start, int end, Func<byte, bool> processor)
    {
        return Wrapped.ForEachByteAscending(start, end, processor);
    }

    internal override int ForEachByteDescending(int start, int end, Func<byte, bool> processor)
    {
        return Wrapped.ForEachByteDescending(start, end, processor);
    }
    internal override bool SupportsDirectBulkCopy => Wrapped.SupportsDirectBulkCopy;
    internal override bool SupportsDirectSearch => Wrapped.SupportsDirectSearch;
    internal override ReadOnlySequence<byte> BorrowCopySequence(int index, int length)
    {
        CheckIndex(index, length);
        return Wrapped.BorrowCopySequence(index, length);
    }

    internal override BufferMemoryLease AcquireReadLease()
    {
        return Wrapped.AcquireReadLease();
    }

    internal override BufferMemoryLease PinMemoryForWrite()
    {
        return Wrapped.PinMemoryForWrite();
    }

    public override ByteBuf Copy()
    {
        return Wrapped.Copy();
    }

    public override ByteBuf Copy(int index, int length)
    {
        return Wrapped.Copy(index, length);
    }

    public override ByteBuf ReadBytes(int length)
    {
        return Wrapped.ReadBytes(length);
    }

    public override int IndexOf(int fromIndex, int toIndex, byte value)
    {
        return Wrapped.IndexOf(fromIndex, toIndex, value);
    }

    public override int BytesBefore(byte value)
    {
        return Wrapped.BytesBefore(value);
    }

    public override int BytesBefore(int length, byte value)
    {
        return Wrapped.BytesBefore(length, value);
    }

    public override int BytesBefore(int index, int length, byte value)
    {
        return Wrapped.BytesBefore(index, length, value);
    }

    public override string ToString(Encoding charset)
    {
        return Wrapped.ToString(charset);
    }

    public override string ToString(int index, int length, Encoding charset)
    {
        return Wrapped.ToString(index, length, charset);
    }

    public override ICharSequence GetCharSequence(int index, int length, Encoding charset)
    {
        return Wrapped.GetCharSequence(index, length, charset);
    }

    public override ICharSequence ReadCharSequence(int length, Encoding charset)
    {
        return Wrapped.ReadCharSequence(length, charset);
    }

    public override string ReadString(int length, Encoding charset)
    {
        return Wrapped.ReadString(length, charset);
    }

    public override int SetCharSequence(int index, ReadOnlySpan<char> sequence, Encoding charset)
    {
        return Wrapped.SetCharSequence(index, sequence, charset);
    }

    public override int WriteCharSequence(ReadOnlySpan<char> sequence, Encoding charset)
    {
        return Wrapped.WriteCharSequence(sequence, charset);
    }

    public override int ForEachByte(Func<byte, bool> processor)
    {
        return Wrapped.ForEachByte(processor);
    }

    public override int ForEachByte(int index, int length, Func<byte, bool> processor)
    {
        return Wrapped.ForEachByte(index, length, processor);
    }

    public override int ForEachByteDesc(Func<byte, bool> processor)
    {
        return Wrapped.ForEachByteDesc(processor);
    }

    public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
    {
        return Wrapped.ForEachByteDesc(index, length, processor);
    }

    public override ByteBuf Slice()
    {
        return Wrapped.Slice();
    }

    public override ByteBuf Slice(int index, int length)
    {
        return Wrapped.Slice(index, length);
    }

    public override ByteBuf RetainedSlice()
    {
        return Wrapped.RetainedSlice();
    }

    public override ByteBuf RetainedSlice(int index, int length)
    {
        return Wrapped.RetainedSlice(index, length);
    }

    public override ByteBuf Duplicate()
    {
        return Wrapped.Duplicate();
    }

    public override ByteBuf RetainedDuplicate()
    {
        return Wrapped.RetainedDuplicate();
    }

    public override ByteBuf ReadSlice(int length)
    {
        return Wrapped.ReadSlice(length);
    }

    public override ByteBuf ReadRetainedSlice(int length)
    {
        return Wrapped.ReadRetainedSlice(length);
    }

    public override ByteBuf AsReadOnly()
    {
        return Wrapped.AsReadOnly();
    }

    public override CompositeByteBuf DiscardReadBytes()
    {
        Wrapped.DiscardReadBytes();
        return this;
    }

    public override CompositeByteBuf DiscardSomeReadBytes()
    {
        Wrapped.DiscardSomeReadBytes();
        return this;
    }

    public override CompositeByteBuf EnsureWritable(int minimumWritableBytes)
    {
        Wrapped.EnsureWritable(minimumWritableBytes);
        return this;
    }

    public override int EnsureWritable(int minimumWritableBytes, bool force)
    {
        return Wrapped.EnsureWritable(minimumWritableBytes, force);
    }

    public override ByteBuf SetZero(int index, int length)
    {
        Wrapped.SetZero(index, length);
        return this;
    }

    public override ByteBuf WriteZero(int length)
    {
        Wrapped.WriteZero(length);
        return this;
    }

    public override int GetHashCode()
    {
        return Wrapped.GetHashCode();
    }

    public override bool Equals(object obj)
    {
        return ReferenceEquals(this, obj) || Wrapped.Equals(obj);
    }

    public override bool Equals(ByteBuf other)
    {
        return ReferenceEquals(this, other) || Wrapped.Equals(other);
    }

    public override int CompareTo(ByteBuf other)
    {
        return ReferenceEquals(this, other) ? 0 : Wrapped.CompareTo(other);
    }
}
