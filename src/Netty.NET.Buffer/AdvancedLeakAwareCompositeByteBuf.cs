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
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Netty.NET.Common;
using static Netty.NET.Buffer.AdvancedLeakAwareByteBuf;

namespace Netty.NET.Buffer;

// CLR: delegate each data/structural call to the wrapped composite so its internal
// virtual calls cannot record the same public access twice. Views use the simple
// composite's factory to preserve the original referent and shared tracker.
internal sealed class AdvancedLeakAwareCompositeByteBuf : SimpleLeakAwareCompositeByteBuf
{
    internal AdvancedLeakAwareCompositeByteBuf(CompositeByteBuf wrapped, IResourceLeakTracker<ByteBuf> leak)
        : base(wrapped, leak) { }

    public override bool IsReadOnly
    {
        get { RecordLeakNonRefCountingOperation(Leak); return Wrapped.IsReadOnly; }
    }

    public override int Capacity
    {
        get => Wrapped.Capacity;
        set { RecordLeakNonRefCountingOperation(Leak); Wrapped.Capacity = value; }
    }

    public override CompositeByteBuf Retain(int increment = 1) { Leak.Record(); return base.Retain(increment); }
    public override bool Release(int decrement = 1) { Leak.Record(); return base.Release(decrement); }
    public override CompositeByteBuf Touch(object hint = null)
    {
        // CLR's optional hint combines Java's two overloads; null has the same no-hint record semantics.
        if (hint == null) Leak.Record(); else Leak.Record(hint);
        return this;
    }

    protected override SimpleLeakAwareByteBuf NewLeakAwareByteBuf(ByteBuf buffer, ByteBuf trackedByteBuf,
        IResourceLeakTracker<ByteBuf> leakTracker) => new AdvancedLeakAwareByteBuf(buffer, trackedByteBuf, leakTracker);

    public override CompositeByteBuf DiscardReadBytes()
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.DiscardReadBytes(); return this; }

    public override CompositeByteBuf DiscardSomeReadBytes()
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.DiscardSomeReadBytes(); return this; }

    public override ByteBuf EnsureWritable(int minimumWritableBytes)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.EnsureWritable(minimumWritableBytes); return this; }

    public override int EnsureWritable(int minimumWritableBytes, bool force)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.EnsureWritable(minimumWritableBytes, force); }

    public override Memory<byte> AsMemory(int index, int length)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.AsMemory(index, length); }

    public override Span<byte> AsSpan(int index, int length)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.AsSpan(index, length); }

    public override byte GetByte(int index)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetByte(index); }

    public override bool GetBoolean(int index)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetBoolean(index); }

    public override ByteBuf SetByte(int index, int value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.SetByte(index, value); return this; }

    public override ByteBuf SetBoolean(int index, bool value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.SetBoolean(index, value); return this; }

    public override short GetShort(int index)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetShort(index); }

    public override ByteBuf SetShort(int index, int value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.SetShort(index, value); return this; }

    public override short GetShortLE(int index)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetShortLE(index); }

    public override ByteBuf SetShortLE(int index, int value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.SetShortLE(index, value); return this; }

    public override int GetInt(int index)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetInt(index); }

    public override ByteBuf SetInt(int index, int value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.SetInt(index, value); return this; }

    public override int GetIntLE(int index)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetIntLE(index); }

    public override ByteBuf SetIntLE(int index, int value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.SetIntLE(index, value); return this; }

    public override long GetLong(int index)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetLong(index); }

    public override ByteBuf SetLong(int index, long value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.SetLong(index, value); return this; }

    public override long GetLongLE(int index)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetLongLE(index); }

    public override ByteBuf SetLongLE(int index, long value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.SetLongLE(index, value); return this; }

    public override int GetUnsignedMedium(int index)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetUnsignedMedium(index); }

    public override int GetUnsignedMediumLE(int index)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetUnsignedMediumLE(index); }

    public override int GetMedium(int index)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetMedium(index); }

    public override int GetMediumLE(int index)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetMediumLE(index); }

    public override ByteBuf SetMedium(int index, int value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.SetMedium(index, value); return this; }

    public override ByteBuf SetMediumLE(int index, int value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.SetMediumLE(index, value); return this; }

    public override ushort GetUnsignedShort(int index)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetUnsignedShort(index); }

    public override ushort GetUnsignedShortLE(int index)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetUnsignedShortLE(index); }

    public override uint GetUnsignedInt(int index)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetUnsignedInt(index); }

    public override uint GetUnsignedIntLE(int index)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetUnsignedIntLE(index); }

    public override char GetChar(int index)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetChar(index); }

    public override ByteBuf SetChar(int index, char value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.SetChar(index, value); return this; }

    public override float GetFloat(int index)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetFloat(index); }

    public override float GetFloatLE(int index)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetFloatLE(index); }

    public override double GetDouble(int index)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetDouble(index); }

    public override double GetDoubleLE(int index)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetDoubleLE(index); }

    public override ByteBuf SetFloat(int index, float value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.SetFloat(index, value); return this; }

    public override ByteBuf SetFloatLE(int index, float value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.SetFloatLE(index, value); return this; }

    public override ByteBuf SetDouble(int index, double value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.SetDouble(index, value); return this; }

    public override ByteBuf SetDoubleLE(int index, double value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.SetDoubleLE(index, value); return this; }

    public override byte ReadByte()
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadByte(); }

    public override ByteBuf WriteByte(int value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.WriteByte(value); return this; }

    public override bool ReadBoolean()
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadBoolean(); }

    public override ByteBuf WriteBoolean(bool value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.WriteBoolean(value); return this; }

    public override short ReadShort()
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadShort(); }

    public override ByteBuf WriteShort(int value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.WriteShort(value); return this; }

    public override short ReadShortLE()
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadShortLE(); }

    public override ByteBuf WriteShortLE(int value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.WriteShortLE(value); return this; }

    public override int ReadMedium()
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadMedium(); }

    public override ByteBuf WriteMedium(int value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.WriteMedium(value); return this; }

    public override int ReadMediumLE()
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadMediumLE(); }

    public override ByteBuf WriteMediumLE(int value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.WriteMediumLE(value); return this; }

    public override int ReadInt()
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadInt(); }

    public override ByteBuf WriteInt(int value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.WriteInt(value); return this; }

    public override int ReadIntLE()
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadIntLE(); }

    public override ByteBuf WriteIntLE(int value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.WriteIntLE(value); return this; }

    public override long ReadLong()
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadLong(); }

    public override ByteBuf WriteLong(long value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.WriteLong(value); return this; }

    public override long ReadLongLE()
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadLongLE(); }

    public override ByteBuf WriteLongLE(long value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.WriteLongLE(value); return this; }

    public override float ReadFloat()
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadFloat(); }

    public override ByteBuf WriteFloat(float value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.WriteFloat(value); return this; }

    public override float ReadFloatLE()
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadFloatLE(); }

    public override ByteBuf WriteFloatLE(float value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.WriteFloatLE(value); return this; }

    public override double ReadDouble()
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadDouble(); }

    public override ByteBuf WriteDouble(double value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.WriteDouble(value); return this; }

    public override double ReadDoubleLE()
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadDoubleLE(); }

    public override ByteBuf WriteDoubleLE(double value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.WriteDoubleLE(value); return this; }

    public override ushort ReadUnsignedShort()
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadUnsignedShort(); }

    public override ushort ReadUnsignedShortLE()
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadUnsignedShortLE(); }

    public override uint ReadUnsignedInt()
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadUnsignedInt(); }

    public override uint ReadUnsignedIntLE()
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadUnsignedIntLE(); }

    public override int ReadUnsignedMedium()
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadUnsignedMedium(); }

    public override int ReadUnsignedMediumLE()
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadUnsignedMediumLE(); }

    public override char ReadChar()
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadChar(); }

    public override ByteBuf WriteChar(char value)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.WriteChar(value); return this; }

    public override ByteBuf SkipBytes(int length)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.SkipBytes(length); return this; }

    public override ByteBuf GetBytes(int index, Span<byte> destination)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.GetBytes(index, destination); return this; }

    public override ByteBuf SetBytes(int index, ReadOnlySpan<byte> source)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.SetBytes(index, source); return this; }

    public override ByteBuf ReadBytes(Span<byte> destination)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.ReadBytes(destination); return this; }

    public override ByteBuf WriteBytes(ReadOnlySpan<byte> source)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.WriteBytes(source); return this; }

    public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.GetBytes(index, destination, destinationIndex, length); return this; }

    public override ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.SetBytes(index, source, sourceIndex, length); return this; }

    public override ByteBuf ReadBytes(ByteBuf destination, int length)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.ReadBytes(destination, length); return this; }

    public override ByteBuf WriteBytes(ByteBuf source, int length)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.WriteBytes(source, length); return this; }

    public override ByteBuf SetZero(int index, int length)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.SetZero(index, length); return this; }

    public override ByteBuf WriteZero(int length)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.WriteZero(length); return this; }

    public override ByteBuf Copy()
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.Copy(); }

    public override ByteBuf Copy(int index, int length)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.Copy(index, length); }

    public override ByteBuf ReadBytes(int length)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadBytes(length); }

    public override ByteBuf Slice()
    { RecordLeakNonRefCountingOperation(Leak); return base.Slice(); }

    public override ByteBuf Slice(int index, int length)
    { RecordLeakNonRefCountingOperation(Leak); return base.Slice(index, length); }

    public override ByteBuf RetainedSlice()
    { RecordLeakNonRefCountingOperation(Leak); return base.RetainedSlice(); }

    public override ByteBuf RetainedSlice(int index, int length)
    { RecordLeakNonRefCountingOperation(Leak); return base.RetainedSlice(index, length); }

    public override ByteBuf Duplicate()
    { RecordLeakNonRefCountingOperation(Leak); return base.Duplicate(); }

    public override ByteBuf RetainedDuplicate()
    { RecordLeakNonRefCountingOperation(Leak); return base.RetainedDuplicate(); }

    public override ByteBuf ReadSlice(int length)
    { RecordLeakNonRefCountingOperation(Leak); return base.ReadSlice(length); }

    public override ByteBuf ReadRetainedSlice(int length)
    { RecordLeakNonRefCountingOperation(Leak); return base.ReadRetainedSlice(length); }

    public override int GetBytes(int index, Socket destination, int length)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetBytes(index, destination, length); }

    public override ValueTask<int> GetBytesAsync(int index, Socket destination, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetBytesAsync(index, destination, length, cancellationToken); }

    public override int SetBytes(int index, Socket source, int length)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.SetBytes(index, source, length); }

    public override ValueTask<int> SetBytesAsync(int index, Socket source, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.SetBytesAsync(index, source, length, cancellationToken); }

    public override int ReadBytes(Socket destination, int length)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadBytes(destination, length); }

    public override ValueTask<int> ReadBytesAsync(Socket destination, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadBytesAsync(destination, length, cancellationToken); }

    public override int WriteBytes(Socket source, int length)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.WriteBytes(source, length); }

    public override ValueTask<int> WriteBytesAsync(Socket source, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.WriteBytesAsync(source, length, cancellationToken); }

    public override int GetBytes(int index, SafeFileHandle destination, long position, int length)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetBytes(index, destination, position, length); }

    public override int SetBytes(int index, SafeFileHandle source, long position, int length)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.SetBytes(index, source, position, length); }

    public override int ReadBytes(SafeFileHandle destination, long position, int length)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadBytes(destination, position, length); }

    public override int WriteBytes(SafeFileHandle source, long position, int length)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.WriteBytes(source, position, length); }

    public override ValueTask<int> GetBytesAsync(int index, SafeFileHandle destination, long position, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetBytesAsync(index, destination, position, length, cancellationToken); }

    public override ValueTask<int> SetBytesAsync(int index, SafeFileHandle source, long position, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.SetBytesAsync(index, source, position, length, cancellationToken); }

    public override ValueTask<int> ReadBytesAsync(SafeFileHandle destination, long position, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadBytesAsync(destination, position, length, cancellationToken); }

    public override ValueTask<int> WriteBytesAsync(SafeFileHandle source, long position, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.WriteBytesAsync(source, position, length, cancellationToken); }

    public override ByteBuf AsReadOnly()
    { RecordLeakNonRefCountingOperation(Leak); return base.AsReadOnly(); }

    public override ReadOnlyMemory<byte> AsReadOnlyMemory(int index, int length)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.AsReadOnlyMemory(index, length); }

    public override ReadOnlySpan<byte> AsReadOnlySpan(int index, int length)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.AsReadOnlySpan(index, length); }

    public override ReadOnlySequence<byte> AsReadOnlySequence(int index, int length)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.AsReadOnlySequence(index, length); }

    public override ByteBuf GetBytes(int index, Stream destination, int length)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.GetBytes(index, destination, length); return this; }

    public override int SetBytes(int index, Stream source, int length)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.SetBytes(index, source, length); }

    public override ByteBuf ReadBytes(Stream destination, int length)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.ReadBytes(destination, length); return this; }

    public override int WriteBytes(Stream source, int length)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.WriteBytes(source, length); }

    public override ValueTask GetBytesAsync(int index, Stream destination, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetBytesAsync(index, destination, length, cancellationToken); }

    public override ValueTask<int> SetBytesAsync(int index, Stream source, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.SetBytesAsync(index, source, length, cancellationToken); }

    public override ValueTask ReadBytesAsync(Stream destination, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadBytesAsync(destination, length, cancellationToken); }

    public override ValueTask<int> WriteBytesAsync(Stream source, int length,
        CancellationToken cancellationToken = default)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.WriteBytesAsync(source, length, cancellationToken); }

    public override string GetString(Encoding encoding)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetString(encoding); }

    public override string GetString(int index, int length, Encoding encoding)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetString(index, length, encoding); }

    public override string ReadString(int length, Encoding encoding)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ReadString(length, encoding); }

    public override int SetString(int index, ReadOnlySpan<char> text, Encoding encoding)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.SetString(index, text, encoding); }

    public override int WriteString(ReadOnlySpan<char> text, Encoding encoding)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.WriteString(text, encoding); }

    public override int IndexOf(int fromIndex, int toIndex, byte value)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.IndexOf(fromIndex, toIndex, value); }

    public override int BytesBefore(byte value)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.BytesBefore(value); }

    public override int BytesBefore(int length, byte value)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.BytesBefore(length, value); }

    public override int BytesBefore(int index, int length, byte value)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.BytesBefore(index, length, value); }

    public override int ForEachByte(Func<byte, bool> processor)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ForEachByte(processor); }

    public override int ForEachByte(int index, int length, Func<byte, bool> processor)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ForEachByte(index, length, processor); }

    public override int ForEachByteDesc(Func<byte, bool> processor)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ForEachByteDesc(processor); }

    public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.ForEachByteDesc(index, length, processor); }

    public override ByteBuf GetBytes(int index, ByteBuf destination)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.GetBytes(index, destination); return this; }

    public override ByteBuf GetBytes(int index, ByteBuf destination, int length)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.GetBytes(index, destination, length); return this; }

    public override ByteBuf SetBytes(int index, ByteBuf source)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.SetBytes(index, source); return this; }

    public override ByteBuf SetBytes(int index, ByteBuf source, int length)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.SetBytes(index, source, length); return this; }

    public override ByteBuf ReadBytes(ByteBuf destination)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.ReadBytes(destination); return this; }

    public override ByteBuf ReadBytes(ByteBuf destination, int destinationIndex, int length)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.ReadBytes(destination, destinationIndex, length); return this; }

    public override ByteBuf WriteBytes(ByteBuf source)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.WriteBytes(source); return this; }

    public override ByteBuf WriteBytes(ByteBuf source, int sourceIndex, int length)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.WriteBytes(source, sourceIndex, length); return this; }


    public override CompositeByteBuf AddComponent(ByteBuf buffer, bool increaseWriterIndex = false)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.AddComponent(buffer, increaseWriterIndex); return this; }

    public override CompositeByteBuf AddComponent(int componentIndex, ByteBuf buffer, bool increaseWriterIndex = false)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.AddComponent(componentIndex, buffer, increaseWriterIndex); return this; }

    public override CompositeByteBuf RemoveComponent(int componentIndex)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.RemoveComponent(componentIndex); return this; }

    public override CompositeByteBuf RemoveComponents(int componentIndex, int numComponents)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.RemoveComponents(componentIndex, numComponents); return this; }

    public override IReadOnlyList<ByteBuf> Decompose(int offset, int length)
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.Decompose(offset, length); }

    public override CompositeByteBuf Consolidate()
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.Consolidate(); return this; }

    public override CompositeByteBuf Consolidate(int componentIndex, int numComponents)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.Consolidate(componentIndex, numComponents); return this; }

    public override CompositeByteBuf DiscardReadComponents()
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.DiscardReadComponents(); return this; }

    public override CompositeByteBuf AddComponents(params ByteBuf[] buffers)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.AddComponents(buffers); return this; }

    public override CompositeByteBuf AddComponents(ByteBuf[] buffers, bool increaseWriterIndex)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.AddComponents(buffers, increaseWriterIndex); return this; }

    public override CompositeByteBuf AddComponents(int componentIndex, ByteBuf[] buffers, bool increaseWriterIndex = false)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.AddComponents(componentIndex, buffers, increaseWriterIndex); return this; }

    public override CompositeByteBuf AddComponents(IEnumerable<ByteBuf> buffers, bool increaseWriterIndex = false)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.AddComponents(buffers, increaseWriterIndex); return this; }

    public override CompositeByteBuf AddComponents(int componentIndex, IEnumerable<ByteBuf> buffers, bool increaseWriterIndex = false)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.AddComponents(componentIndex, buffers, increaseWriterIndex); return this; }

    public override CompositeByteBuf AddFlattenedComponents(ByteBuf buffer, bool increaseWriterIndex = false)
    { RecordLeakNonRefCountingOperation(Leak); Wrapped.AddFlattenedComponents(buffer, increaseWriterIndex); return this; }

    public override IEnumerator<ByteBuf> GetEnumerator()
    { RecordLeakNonRefCountingOperation(Leak); return Wrapped.GetEnumerator(); }
}
