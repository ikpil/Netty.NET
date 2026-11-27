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
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer;

/**
 * Wraps another {@link ByteBuf}.
 *
 * It's important that the {@link #readerIndex()} and {@link #writerIndex()} will not do any adjustments on the
 * indices on the fly because of internal optimizations made by {@link ByteBufUtil#writeAscii(ByteBuf, CharSequence)}
 * and {@link ByteBufUtil#writeUtf8(ByteBuf, CharSequence)}.
 */
/// <remarks>CLR forwarding shares nonvirtual index state and delegates public virtual
/// operations to the wrapped buffer. Views returned by this base are unwrapped;
/// lifetime-aware subclasses wrap them as needed. Endian access is explicit;
/// storage metadata is borrowed through Memory and scoped pins.</remarks>
public partial class WrappedByteBuf : ByteBuf
{
    protected readonly ByteBuf Buffer;

    protected WrappedByteBuf(ByteBuf buffer) : base(buffer)
    {
        Buffer = buffer;
    }

    public override int Capacity { get => Buffer.Capacity; set => Buffer.Capacity = value; }
    public override IByteBufAllocator Alloc()
    {
        return Buffer.Alloc();
    }
    public override bool IsContiguous => Buffer.IsContiguous;
    public override int MaxFastWritableBytes => Buffer.MaxFastWritableBytes;
    public override bool IsDirect => Buffer.IsDirect;
    public override bool IsReadOnly => Buffer.IsReadOnly;
    public override bool IsReadable(int size)
    {
        return Buffer.IsReadable(size);
    }
    public override bool IsWritable(int size)
    {
        return Buffer.IsWritable(size);
    }
    public override ByteBuf Unwrap()
    {
        return Buffer;
    }
    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) + '(' + Buffer.ToString() + ')';
    }
    public override int RefCnt => Buffer.RefCnt;
    public override ByteBuf Retain(int increment = 1) { Buffer.Retain(increment); return this; }
    public override bool Release(int decrement = 1)
    {
        return Buffer.Release(decrement);
    }
    public override ByteBuf Touch(object hint = null) { Buffer.Touch(hint); return this; }

    protected override Memory<byte> GetMemoryCore(int index, int length)
    {
        return Buffer.AsMemory(index, length);
    }
    internal override byte ReadByteCore(int index)
    {
        return Buffer.ReadByteCore(index);
    }
    internal override void WriteByteCore(int index, int value)
    {
        Buffer.WriteByteCore(index, value);
    }
    internal override ulong ReadWord(int index, int width, bool littleEndian)
    {
        return Buffer.ReadWord(index, width, littleEndian);
    }
    internal override ByteBuf SetWord(int index, ulong value, int width, bool littleEndian)
    { Buffer.SetWord(index, value, width, littleEndian); return this; }
    protected override ReadOnlyMemory<byte> GetReadOnlyMemoryCore(int index, int length)
    {
        return Buffer.AsReadOnlyMemory(index, length);
    }
    protected override bool TryGetMemoryCore(int index, int length, out Memory<byte> memory)
    {
        return Buffer.TryGetMemory(index, length, out memory);
    }
    protected override bool TryGetReadOnlyMemoryCore(int index, int length, out ReadOnlyMemory<byte> memory)
    {
        return Buffer.TryGetReadOnlyMemory(index, length, out memory);
    }
    protected override void GetBytesCore(int index, Span<byte> destination)
    {
        Buffer.GetBytes(index, destination);
    }
    protected override void SetBytesCore(int index, ReadOnlySpan<byte> source)
    {
        Buffer.SetBytes(index, source);
    }
    protected internal override void SetBytesFromIoCore(int index, ReadOnlySpan<byte> source)
    {
        Buffer.SetBytesFromIoCore(index, source);
    }
    protected override void SetZeroCore(int index, int length)
    {
        Buffer.SetZero(index, length);
    }
    protected override ReadOnlySequence<byte> GetReadOnlySequenceCore(int index, int length)
    {
        return Buffer.AsReadOnlySequence(index, length);
    }
    internal override ByteBuf UnwrapIterationStorage(ref int index)
    { return Buffer.UnwrapIterationStorage(ref index); }
    internal override bool SupportsDirectBulkCopy => Buffer.SupportsDirectBulkCopy;
    internal override ReadOnlySequence<byte> BorrowCopySequence(int index, int length)
    { CheckIndex(index, length); return Buffer.BorrowCopySequence(index, length); }
    internal override BufferMemoryLease AcquireReadLease()
    {
        return Buffer.AcquireReadLease();
    }
    internal override BufferMemoryLease PinMemoryForWrite()
    {
        return Buffer.PinMemoryForWrite();
    }

    public override ByteBuf Copy()
    {
        return Buffer.Copy();
    }
    public override ByteBuf Copy(int index, int length)
    {
        return Buffer.Copy(index, length);
    }
    public override ByteBuf ReadBytes(int length)
    {
        return Buffer.ReadBytes(length);
    }
    public override int IndexOf(int fromIndex, int toIndex, byte value)
    {
        return Buffer.IndexOf(fromIndex, toIndex, value);
    }
    public override int BytesBefore(byte value)
    {
        return Buffer.BytesBefore(value);
    }
    public override int BytesBefore(int length, byte value)
    {
        return Buffer.BytesBefore(length, value);
    }
    public override int BytesBefore(int index, int length, byte value)
    {
        return Buffer.BytesBefore(index, length, value);
    }
    public override string ToString(Encoding charset)
    {
        return Buffer.ToString(charset);
    }
    public override string ToString(int index, int length, Encoding charset)
    {
        return Buffer.ToString(index, length, charset);
    }
    public override ICharSequence GetCharSequence(int index, int length, Encoding charset)
    {
        return Buffer.GetCharSequence(index, length, charset);
    }
    public override ICharSequence ReadCharSequence(int length, Encoding charset)
    {
        return Buffer.ReadCharSequence(length, charset);
    }
    public override string ReadString(int length, Encoding charset)
    {
        return Buffer.ReadString(length, charset);
    }
    public override int SetCharSequence(int index, ReadOnlySpan<char> sequence, Encoding charset)
    {
        return Buffer.SetCharSequence(index, sequence, charset);
    }
    public override int WriteCharSequence(ReadOnlySpan<char> sequence, Encoding charset)
    {
        return Buffer.WriteCharSequence(sequence, charset);
    }
    public override int ForEachByte(Func<byte, bool> processor)
    {
        return Buffer.ForEachByte(processor);
    }
    public override int ForEachByte(int index, int length, Func<byte, bool> processor)
    {
        return Buffer.ForEachByte(index, length, processor);
    }
    public override int ForEachByteDesc(Func<byte, bool> processor)
    {
        return Buffer.ForEachByteDesc(processor);
    }
    public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
    {
        return Buffer.ForEachByteDesc(index, length, processor);
    }
    public override ByteBuf DiscardReadBytes() { Buffer.DiscardReadBytes(); return this; }
    public override ByteBuf DiscardSomeReadBytes() { Buffer.DiscardSomeReadBytes(); return this; }
    public override ByteBuf EnsureWritable(int minimumWritableBytes) { Buffer.EnsureWritable(minimumWritableBytes); return this; }
    public override int EnsureWritable(int minimumWritableBytes, bool force)
    {
        return Buffer.EnsureWritable(minimumWritableBytes, force);
    }
    public override ByteBuf SetZero(int index, int length) { Buffer.SetZero(index, length); return this; }
    public override ByteBuf WriteZero(int length) { Buffer.WriteZero(length); return this; }
    public override ByteBuf AsReadOnly()
    {
        return Buffer.AsReadOnly();
    }
    public override ByteBuf Slice()
    {
        return Buffer.Slice();
    }
    public override ByteBuf Slice(int index, int length)
    {
        return Buffer.Slice(index, length);
    }
    public override ByteBuf RetainedSlice()
    {
        return Buffer.RetainedSlice();
    }
    public override ByteBuf RetainedSlice(int index, int length)
    {
        return Buffer.RetainedSlice(index, length);
    }
    public override ByteBuf Duplicate()
    {
        return Buffer.Duplicate();
    }
    public override ByteBuf RetainedDuplicate()
    {
        return Buffer.RetainedDuplicate();
    }
    public override ByteBuf ReadSlice(int length)
    {
        return Buffer.ReadSlice(length);
    }
    public override ByteBuf ReadRetainedSlice(int length)
    {
        return Buffer.ReadRetainedSlice(length);
    }

    // Preserve Java's direct comparison forwarding: hashing/comparing the wrapper
    // must not turn its own primitive implementation reads into advanced access records.
    public override int GetHashCode()
    {
        return Buffer.GetHashCode();
    }
    // CLR reference identity stays reflexive, including after final release.
    public override bool Equals(object obj)
    {
        return ReferenceEquals(this, obj) || Buffer.Equals(obj);
    }
    public override bool Equals(ByteBuf other)
    {
        return ReferenceEquals(this, other) || Buffer.Equals(other);
    }
    public override int CompareTo(ByteBuf other)
    {
        return ReferenceEquals(this, other) ? 0 : Buffer.CompareTo(other);
    }
}
