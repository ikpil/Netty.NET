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
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Microsoft.Win32.SafeHandles;

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
public class WrappedByteBuf : ByteBuf
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
    public override ByteBuf Retain(int increment = 1)
    {
        Buffer.Retain(increment);
        return this;
    }

    public override bool Release(int decrement = 1)
    {
        return Buffer.Release(decrement);
    }

    public override ByteBuf Touch(object hint = null)
    {
        Buffer.Touch(hint);
        return this;
    }

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
    {
        Buffer.SetWord(index, value, width, littleEndian);
        return this;
    }

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
    {
        return Buffer.UnwrapIterationStorage(ref index);
    }
    internal override bool SupportsDirectBulkCopy => Buffer.SupportsDirectBulkCopy;
    internal override ReadOnlySequence<byte> BorrowCopySequence(int index, int length)
    {
        CheckIndex(index, length);
        return Buffer.BorrowCopySequence(index, length);
    }

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

    public override ByteBuf DiscardReadBytes()
    {
        Buffer.DiscardReadBytes();
        return this;
    }

    public override ByteBuf DiscardSomeReadBytes()
    {
        Buffer.DiscardSomeReadBytes();
        return this;
    }

    public override ByteBuf EnsureWritable(int minimumWritableBytes)
    {
        Buffer.EnsureWritable(minimumWritableBytes);
        return this;
    }

    public override int EnsureWritable(int minimumWritableBytes, bool force)
    {
        return Buffer.EnsureWritable(minimumWritableBytes, force);
    }

    public override ByteBuf SetZero(int index, int length)
    {
        Buffer.SetZero(index, length);
        return this;
    }

    public override ByteBuf WriteZero(int length)
    {
        Buffer.WriteZero(length);
        return this;
    }

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

    // Keep public calls on the wrapped buffer. Sharing component/index state is
    // sufficient for nonvirtual metadata, but must not bypass its virtual operations.
    public override Memory<byte> AsMemory(int index, int length)
    {
        return Buffer.AsMemory(index, length);
    }

    public override Span<byte> AsSpan(int index, int length)
    {
        return Buffer.AsSpan(index, length);
    }

    public override byte GetByte(int index)
    {
        return Buffer.GetByte(index);
    }

    public override bool GetBoolean(int index)
    {
        return Buffer.GetBoolean(index);
    }

    public override ByteBuf SetByte(int index, int value)
    {
        Buffer.SetByte(index, value);
        return this;
    }

    public override ByteBuf SetBoolean(int index, bool value)
    {
        Buffer.SetBoolean(index, value);
        return this;
    }

    public override short GetShort(int index)
    {
        return Buffer.GetShort(index);
    }

    public override ByteBuf SetShort(int index, int value)
    {
        Buffer.SetShort(index, value);
        return this;
    }

    public override short GetShortLE(int index)
    {
        return Buffer.GetShortLE(index);
    }

    public override ByteBuf SetShortLE(int index, int value)
    {
        Buffer.SetShortLE(index, value);
        return this;
    }

    public override int GetInt(int index)
    {
        return Buffer.GetInt(index);
    }

    public override ByteBuf SetInt(int index, int value)
    {
        Buffer.SetInt(index, value);
        return this;
    }

    public override int GetIntLE(int index)
    {
        return Buffer.GetIntLE(index);
    }

    public override ByteBuf SetIntLE(int index, int value)
    {
        Buffer.SetIntLE(index, value);
        return this;
    }

    public override long GetLong(int index)
    {
        return Buffer.GetLong(index);
    }

    public override ByteBuf SetLong(int index, long value)
    {
        Buffer.SetLong(index, value);
        return this;
    }

    public override long GetLongLE(int index)
    {
        return Buffer.GetLongLE(index);
    }

    public override ByteBuf SetLongLE(int index, long value)
    {
        Buffer.SetLongLE(index, value);
        return this;
    }

    public override int GetUnsignedMedium(int index)
    {
        return Buffer.GetUnsignedMedium(index);
    }

    public override int GetUnsignedMediumLE(int index)
    {
        return Buffer.GetUnsignedMediumLE(index);
    }

    public override int GetMedium(int index)
    {
        return Buffer.GetMedium(index);
    }

    public override int GetMediumLE(int index)
    {
        return Buffer.GetMediumLE(index);
    }

    public override ByteBuf SetMedium(int index, int value)
    {
        Buffer.SetMedium(index, value);
        return this;
    }

    public override ByteBuf SetMediumLE(int index, int value)
    {
        Buffer.SetMediumLE(index, value);
        return this;
    }

    public override ushort GetUnsignedShort(int index)
    {
        return Buffer.GetUnsignedShort(index);
    }

    public override ushort GetUnsignedShortLE(int index)
    {
        return Buffer.GetUnsignedShortLE(index);
    }

    public override uint GetUnsignedInt(int index)
    {
        return Buffer.GetUnsignedInt(index);
    }

    public override uint GetUnsignedIntLE(int index)
    {
        return Buffer.GetUnsignedIntLE(index);
    }

    public override char GetChar(int index)
    {
        return Buffer.GetChar(index);
    }

    public override ByteBuf SetChar(int index, char value)
    {
        Buffer.SetChar(index, value);
        return this;
    }

    public override float GetFloat(int index)
    {
        return Buffer.GetFloat(index);
    }

    public override float GetFloatLE(int index)
    {
        return Buffer.GetFloatLE(index);
    }

    public override double GetDouble(int index)
    {
        return Buffer.GetDouble(index);
    }

    public override double GetDoubleLE(int index)
    {
        return Buffer.GetDoubleLE(index);
    }

    public override ByteBuf SetFloat(int index, float value)
    {
        Buffer.SetFloat(index, value);
        return this;
    }

    public override ByteBuf SetFloatLE(int index, float value)
    {
        Buffer.SetFloatLE(index, value);
        return this;
    }

    public override ByteBuf SetDouble(int index, double value)
    {
        Buffer.SetDouble(index, value);
        return this;
    }

    public override ByteBuf SetDoubleLE(int index, double value)
    {
        Buffer.SetDoubleLE(index, value);
        return this;
    }

    public override byte ReadByte()
    {
        return Buffer.ReadByte();
    }

    public override ByteBuf WriteByte(int value)
    {
        Buffer.WriteByte(value);
        return this;
    }

    public override bool ReadBoolean()
    {
        return Buffer.ReadBoolean();
    }

    public override ByteBuf WriteBoolean(bool value)
    {
        Buffer.WriteBoolean(value);
        return this;
    }

    public override short ReadShort()
    {
        return Buffer.ReadShort();
    }

    public override ByteBuf WriteShort(int value)
    {
        Buffer.WriteShort(value);
        return this;
    }

    public override short ReadShortLE()
    {
        return Buffer.ReadShortLE();
    }

    public override ByteBuf WriteShortLE(int value)
    {
        Buffer.WriteShortLE(value);
        return this;
    }

    public override int ReadMedium()
    {
        return Buffer.ReadMedium();
    }

    public override ByteBuf WriteMedium(int value)
    {
        Buffer.WriteMedium(value);
        return this;
    }

    public override int ReadMediumLE()
    {
        return Buffer.ReadMediumLE();
    }

    public override ByteBuf WriteMediumLE(int value)
    {
        Buffer.WriteMediumLE(value);
        return this;
    }

    public override int ReadInt()
    {
        return Buffer.ReadInt();
    }

    public override ByteBuf WriteInt(int value)
    {
        Buffer.WriteInt(value);
        return this;
    }

    public override int ReadIntLE()
    {
        return Buffer.ReadIntLE();
    }

    public override ByteBuf WriteIntLE(int value)
    {
        Buffer.WriteIntLE(value);
        return this;
    }

    public override long ReadLong()
    {
        return Buffer.ReadLong();
    }

    public override ByteBuf WriteLong(long value)
    {
        Buffer.WriteLong(value);
        return this;
    }

    public override long ReadLongLE()
    {
        return Buffer.ReadLongLE();
    }

    public override ByteBuf WriteLongLE(long value)
    {
        Buffer.WriteLongLE(value);
        return this;
    }

    public override float ReadFloat()
    {
        return Buffer.ReadFloat();
    }

    public override ByteBuf WriteFloat(float value)
    {
        Buffer.WriteFloat(value);
        return this;
    }

    public override float ReadFloatLE()
    {
        return Buffer.ReadFloatLE();
    }

    public override ByteBuf WriteFloatLE(float value)
    {
        Buffer.WriteFloatLE(value);
        return this;
    }

    public override double ReadDouble()
    {
        return Buffer.ReadDouble();
    }

    public override ByteBuf WriteDouble(double value)
    {
        Buffer.WriteDouble(value);
        return this;
    }

    public override double ReadDoubleLE()
    {
        return Buffer.ReadDoubleLE();
    }

    public override ByteBuf WriteDoubleLE(double value)
    {
        Buffer.WriteDoubleLE(value);
        return this;
    }

    public override ushort ReadUnsignedShort()
    {
        return Buffer.ReadUnsignedShort();
    }

    public override ushort ReadUnsignedShortLE()
    {
        return Buffer.ReadUnsignedShortLE();
    }

    public override uint ReadUnsignedInt()
    {
        return Buffer.ReadUnsignedInt();
    }

    public override uint ReadUnsignedIntLE()
    {
        return Buffer.ReadUnsignedIntLE();
    }

    public override int ReadUnsignedMedium()
    {
        return Buffer.ReadUnsignedMedium();
    }

    public override int ReadUnsignedMediumLE()
    {
        return Buffer.ReadUnsignedMediumLE();
    }

    public override char ReadChar()
    {
        return Buffer.ReadChar();
    }

    public override ByteBuf WriteChar(char value)
    {
        Buffer.WriteChar(value);
        return this;
    }

    public override ByteBuf SkipBytes(int length)
    {
        Buffer.SkipBytes(length);
        return this;
    }

    public override ByteBuf GetBytes(int index, Span<byte> destination)
    {
        Buffer.GetBytes(index, destination);
        return this;
    }

    public override ByteBuf SetBytes(int index, ReadOnlySpan<byte> source)
    {
        Buffer.SetBytes(index, source);
        return this;
    }

    public override ByteBuf ReadBytes(Span<byte> destination)
    {
        Buffer.ReadBytes(destination);
        return this;
    }

    public override ByteBuf WriteBytes(ReadOnlySpan<byte> source)
    {
        Buffer.WriteBytes(source);
        return this;
    }

    public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
    {
        Buffer.GetBytes(index, destination, destinationIndex, length);
        return this;
    }

    public override ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
    {
        Buffer.SetBytes(index, source, sourceIndex, length);
        return this;
    }

    public override ByteBuf ReadBytes(ByteBuf destination, int length)
    {
        Buffer.ReadBytes(destination, length);
        return this;
    }

    public override ByteBuf WriteBytes(ByteBuf source, int length)
    {
        Buffer.WriteBytes(source, length);
        return this;
    }

    public override int GetBytes(int index, SafeFileHandle destination, long position, int length)
    {
        return Buffer.GetBytes(index, destination, position, length);
    }

    public override int SetBytes(int index, SafeFileHandle source, long position, int length)
    {
        return Buffer.SetBytes(index, source, position, length);
    }

    public override int ReadBytes(SafeFileHandle destination, long position, int length)
    {
        return Buffer.ReadBytes(destination, position, length);
    }

    public override int WriteBytes(SafeFileHandle source, long position, int length)
    {
        return Buffer.WriteBytes(source, position, length);
    }

    public override ValueTask<int> GetBytesAsync(int index, SafeFileHandle destination, long position, int length,
        CancellationToken cancellationToken = default)
    {
        return Buffer.GetBytesAsync(index, destination, position, length, cancellationToken);
    }

    public override ValueTask<int> SetBytesAsync(int index, SafeFileHandle source, long position, int length,
        CancellationToken cancellationToken = default)
    {
        return Buffer.SetBytesAsync(index, source, position, length, cancellationToken);
    }

    public override ValueTask<int> ReadBytesAsync(SafeFileHandle destination, long position, int length,
        CancellationToken cancellationToken = default)
    {
        return Buffer.ReadBytesAsync(destination, position, length, cancellationToken);
    }

    public override ValueTask<int> WriteBytesAsync(SafeFileHandle source, long position, int length,
        CancellationToken cancellationToken = default)
    {
        return Buffer.WriteBytesAsync(source, position, length, cancellationToken);
    }

    public override ReadOnlyMemory<byte> AsReadOnlyMemory(int index, int length)
    {
        return Buffer.AsReadOnlyMemory(index, length);
    }

    public override ReadOnlySpan<byte> AsReadOnlySpan(int index, int length)
    {
        return Buffer.AsReadOnlySpan(index, length);
    }

    public override ReadOnlySequence<byte> AsReadOnlySequence(int index, int length)
    {
        return Buffer.AsReadOnlySequence(index, length);
    }

    public override int GetBytes(int index, Socket destination, int length)
    {
        return Buffer.GetBytes(index, destination, length);
    }

    public override int SetBytes(int index, Socket source, int length)
    {
        return Buffer.SetBytes(index, source, length);
    }

    public override int ReadBytes(Socket destination, int length)
    {
        return Buffer.ReadBytes(destination, length);
    }

    public override int WriteBytes(Socket source, int length)
    {
        return Buffer.WriteBytes(source, length);
    }

    public override ValueTask<int> GetBytesAsync(int index, Socket destination, int length,
        CancellationToken cancellationToken = default)
    {
        return Buffer.GetBytesAsync(index, destination, length, cancellationToken);
    }

    public override ValueTask<int> SetBytesAsync(int index, Socket source, int length,
        CancellationToken cancellationToken = default)
    {
        return Buffer.SetBytesAsync(index, source, length, cancellationToken);
    }

    public override ValueTask<int> ReadBytesAsync(Socket destination, int length,
        CancellationToken cancellationToken = default)
    {
        return Buffer.ReadBytesAsync(destination, length, cancellationToken);
    }

    public override ValueTask<int> WriteBytesAsync(Socket source, int length,
        CancellationToken cancellationToken = default)
    {
        return Buffer.WriteBytesAsync(source, length, cancellationToken);
    }

    public override ByteBuf GetBytes(int index, Stream destination, int length)
    {
        Buffer.GetBytes(index, destination, length);
        return this;
    }

    public override int SetBytes(int index, Stream source, int length)
    {
        return Buffer.SetBytes(index, source, length);
    }

    public override ByteBuf ReadBytes(Stream destination, int length)
    {
        Buffer.ReadBytes(destination, length);
        return this;
    }

    public override int WriteBytes(Stream source, int length)
    {
        return Buffer.WriteBytes(source, length);
    }

    public override ValueTask GetBytesAsync(int index, Stream destination, int length,
        CancellationToken cancellationToken = default)
    {
        return Buffer.GetBytesAsync(index, destination, length, cancellationToken);
    }

    public override ValueTask<int> SetBytesAsync(int index, Stream source, int length,
        CancellationToken cancellationToken = default)
    {
        return Buffer.SetBytesAsync(index, source, length, cancellationToken);
    }

    public override ValueTask ReadBytesAsync(Stream destination, int length,
        CancellationToken cancellationToken = default)
    {
        return Buffer.ReadBytesAsync(destination, length, cancellationToken);
    }

    public override ValueTask<int> WriteBytesAsync(Stream source, int length,
        CancellationToken cancellationToken = default)
    {
        return Buffer.WriteBytesAsync(source, length, cancellationToken);
    }

    public override ByteBuf GetBytes(int index, ByteBuf destination)
    {
        Buffer.GetBytes(index, destination);
        return this;
    }

    public override ByteBuf GetBytes(int index, ByteBuf destination, int length)
    {
        Buffer.GetBytes(index, destination, length);
        return this;
    }

    public override ByteBuf SetBytes(int index, ByteBuf source)
    {
        Buffer.SetBytes(index, source);
        return this;
    }

    public override ByteBuf SetBytes(int index, ByteBuf source, int length)
    {
        Buffer.SetBytes(index, source, length);
        return this;
    }

    public override ByteBuf ReadBytes(ByteBuf destination)
    {
        Buffer.ReadBytes(destination);
        return this;
    }

    public override ByteBuf ReadBytes(ByteBuf destination, int destinationIndex, int length)
    {
        Buffer.ReadBytes(destination, destinationIndex, length);
        return this;
    }

    public override ByteBuf WriteBytes(ByteBuf source)
    {
        Buffer.WriteBytes(source);
        return this;
    }

    public override ByteBuf WriteBytes(ByteBuf source, int sourceIndex, int length)
    {
        Buffer.WriteBytes(source, sourceIndex, length);
        return this;
    }
}
