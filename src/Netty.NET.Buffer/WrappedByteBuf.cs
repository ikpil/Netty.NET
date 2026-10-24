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
/// <remarks>CLR forwarding uses shared indices and bounded memory hooks rather than
/// duplicating every primitive operation. Views returned by this base are unwrapped;
/// lifetime-aware subclasses wrap them as needed. Endian access is explicit;
/// storage metadata is borrowed through Memory and scoped pins.</remarks>
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
    public override bool CanRead(int byteCount) => Buffer.CanRead(byteCount);
    public override bool CanWrite(int byteCount) => Buffer.CanWrite(byteCount);
    public override ByteBuf Unwrap() => Buffer;
    public override string ToString() => StringUtil.SimpleClassName(this) + '(' + Buffer.ToString() + ')';
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
    internal override bool SupportsDirectBulkCopy => Buffer.SupportsDirectBulkCopy;
    internal override ReadOnlySequence<byte> BorrowCopySequence(int index, int length)
    { CheckIndex(index, length); return Buffer.BorrowCopySequence(index, length); }
    internal override BufferMemoryLease AcquireReadLease() => Buffer.AcquireReadLease();
    internal override BufferMemoryLease PinMemoryForWrite() => Buffer.PinMemoryForWrite();

    public override ByteBuf Copy() => Buffer.Copy();
    public override ByteBuf Copy(int index, int length) => Buffer.Copy(index, length);
    public override ByteBuf ReadBytes(int length) => Buffer.ReadBytes(length);
    public override int IndexOf(int fromIndex, int toIndex, byte value) => Buffer.IndexOf(fromIndex, toIndex, value);
    public override int BytesBefore(byte value) => Buffer.BytesBefore(value);
    public override int BytesBefore(int length, byte value) => Buffer.BytesBefore(length, value);
    public override int BytesBefore(int index, int length, byte value) => Buffer.BytesBefore(index, length, value);
    public override string GetString(Encoding encoding) => Buffer.GetString(encoding);
    public override string GetString(int index, int length, Encoding encoding)
        => Buffer.GetString(index, length, encoding);
    public override string ReadString(int length, Encoding encoding) => Buffer.ReadString(length, encoding);
    public override int SetString(int index, ReadOnlySpan<char> text, Encoding encoding)
        => Buffer.SetString(index, text, encoding);
    public override int WriteString(ReadOnlySpan<char> text, Encoding encoding) => Buffer.WriteString(text, encoding);
    public override int ForEachByte(Func<byte, bool> processor) => Buffer.ForEachByte(processor);
    public override int ForEachByte(int index, int length, Func<byte, bool> processor)
        => Buffer.ForEachByte(index, length, processor);
    public override int ForEachByteDesc(Func<byte, bool> processor) => Buffer.ForEachByteDesc(processor);
    public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
        => Buffer.ForEachByteDesc(index, length, processor);
    public override ByteBuf DiscardReadBytes() { Buffer.DiscardReadBytes(); return this; }
    public override ByteBuf DiscardSomeReadBytes() { Buffer.DiscardSomeReadBytes(); return this; }
    public override ByteBuf EnsureWritable(int minimumWritableBytes) { Buffer.EnsureWritable(minimumWritableBytes); return this; }
    public override int EnsureWritable(int minimumWritableBytes, bool force) => Buffer.EnsureWritable(minimumWritableBytes, force);
    public override ByteBuf SetZero(int index, int length) { Buffer.SetZero(index, length); return this; }
    public override ByteBuf WriteZero(int length) { Buffer.WriteZero(length); return this; }
    public override ByteBuf AsReadOnly() => Buffer.AsReadOnly();
    public override ByteBuf Slice() => Buffer.Slice();
    public override ByteBuf Slice(int index, int length) => Buffer.Slice(index, length);
    public override ByteBuf RetainedSlice() => Buffer.RetainedSlice();
    public override ByteBuf RetainedSlice(int index, int length) => Buffer.RetainedSlice(index, length);
    public override ByteBuf Duplicate() => Buffer.Duplicate();
    public override ByteBuf RetainedDuplicate() => Buffer.RetainedDuplicate();
    public override ByteBuf ReadSlice(int length) => Buffer.ReadSlice(length);
    public override ByteBuf ReadRetainedSlice(int length) => Buffer.ReadRetainedSlice(length);

    // Preserve Java's direct comparison forwarding: hashing/comparing the wrapper
    // must not turn its own primitive implementation reads into advanced access records.
    public override int GetHashCode() => Buffer.GetHashCode();
    // CLR reference identity stays reflexive, including after final release.
    public override bool Equals(object obj) => ReferenceEquals(this, obj) || Buffer.Equals(obj);
    public override bool Equals(ByteBuf other) => ReferenceEquals(this, other) || Buffer.Equals(other);
    public override int CompareTo(ByteBuf other) => ReferenceEquals(this, other) ? 0 : Buffer.CompareTo(other);
}
