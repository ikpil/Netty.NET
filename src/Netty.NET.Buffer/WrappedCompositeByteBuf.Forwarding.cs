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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Netty.NET.Buffer;

// Keep public calls on the wrapped composite. Sharing component/index state is
// sufficient for nonvirtual metadata, but must not bypass its virtual operations.
internal partial class WrappedCompositeByteBuf
{
    public override Memory<byte> AsMemory(int index, int length)
    { return Wrapped.AsMemory(index, length); }

    public override Span<byte> AsSpan(int index, int length)
    { return Wrapped.AsSpan(index, length); }

    public override byte GetByte(int index)
    { return Wrapped.GetByte(index); }

    public override bool GetBoolean(int index)
    { return Wrapped.GetBoolean(index); }

    public override ByteBuf SetByte(int index, int value)
    { Wrapped.SetByte(index, value); return this; }

    public override ByteBuf SetBoolean(int index, bool value)
    { Wrapped.SetBoolean(index, value); return this; }

    public override short GetShort(int index)
    { return Wrapped.GetShort(index); }

    public override ByteBuf SetShort(int index, int value)
    { Wrapped.SetShort(index, value); return this; }

    public override short GetShortLE(int index)
    { return Wrapped.GetShortLE(index); }

    public override ByteBuf SetShortLE(int index, int value)
    { Wrapped.SetShortLE(index, value); return this; }

    public override int GetInt(int index)
    { return Wrapped.GetInt(index); }

    public override ByteBuf SetInt(int index, int value)
    { Wrapped.SetInt(index, value); return this; }

    public override int GetIntLE(int index)
    { return Wrapped.GetIntLE(index); }

    public override ByteBuf SetIntLE(int index, int value)
    { Wrapped.SetIntLE(index, value); return this; }

    public override long GetLong(int index)
    { return Wrapped.GetLong(index); }

    public override ByteBuf SetLong(int index, long value)
    { Wrapped.SetLong(index, value); return this; }

    public override long GetLongLE(int index)
    { return Wrapped.GetLongLE(index); }

    public override ByteBuf SetLongLE(int index, long value)
    { Wrapped.SetLongLE(index, value); return this; }

    public override int GetUnsignedMedium(int index)
    { return Wrapped.GetUnsignedMedium(index); }

    public override int GetUnsignedMediumLE(int index)
    { return Wrapped.GetUnsignedMediumLE(index); }

    public override int GetMedium(int index)
    { return Wrapped.GetMedium(index); }

    public override int GetMediumLE(int index)
    { return Wrapped.GetMediumLE(index); }

    public override ByteBuf SetMedium(int index, int value)
    { Wrapped.SetMedium(index, value); return this; }

    public override ByteBuf SetMediumLE(int index, int value)
    { Wrapped.SetMediumLE(index, value); return this; }

    public override ushort GetUnsignedShort(int index)
    { return Wrapped.GetUnsignedShort(index); }

    public override ushort GetUnsignedShortLE(int index)
    { return Wrapped.GetUnsignedShortLE(index); }

    public override uint GetUnsignedInt(int index)
    { return Wrapped.GetUnsignedInt(index); }

    public override uint GetUnsignedIntLE(int index)
    { return Wrapped.GetUnsignedIntLE(index); }

    public override char GetChar(int index)
    { return Wrapped.GetChar(index); }

    public override ByteBuf SetChar(int index, char value)
    { Wrapped.SetChar(index, value); return this; }

    public override float GetFloat(int index)
    { return Wrapped.GetFloat(index); }

    public override float GetFloatLE(int index)
    { return Wrapped.GetFloatLE(index); }

    public override double GetDouble(int index)
    { return Wrapped.GetDouble(index); }

    public override double GetDoubleLE(int index)
    { return Wrapped.GetDoubleLE(index); }

    public override ByteBuf SetFloat(int index, float value)
    { Wrapped.SetFloat(index, value); return this; }

    public override ByteBuf SetFloatLE(int index, float value)
    { Wrapped.SetFloatLE(index, value); return this; }

    public override ByteBuf SetDouble(int index, double value)
    { Wrapped.SetDouble(index, value); return this; }

    public override ByteBuf SetDoubleLE(int index, double value)
    { Wrapped.SetDoubleLE(index, value); return this; }

    public override byte ReadByte()
    { return Wrapped.ReadByte(); }

    public override ByteBuf WriteByte(int value)
    { Wrapped.WriteByte(value); return this; }

    public override bool ReadBoolean()
    { return Wrapped.ReadBoolean(); }

    public override ByteBuf WriteBoolean(bool value)
    { Wrapped.WriteBoolean(value); return this; }

    public override short ReadShort()
    { return Wrapped.ReadShort(); }

    public override ByteBuf WriteShort(int value)
    { Wrapped.WriteShort(value); return this; }

    public override short ReadShortLE()
    { return Wrapped.ReadShortLE(); }

    public override ByteBuf WriteShortLE(int value)
    { Wrapped.WriteShortLE(value); return this; }

    public override int ReadMedium()
    { return Wrapped.ReadMedium(); }

    public override ByteBuf WriteMedium(int value)
    { Wrapped.WriteMedium(value); return this; }

    public override int ReadMediumLE()
    { return Wrapped.ReadMediumLE(); }

    public override ByteBuf WriteMediumLE(int value)
    { Wrapped.WriteMediumLE(value); return this; }

    public override int ReadInt()
    { return Wrapped.ReadInt(); }

    public override ByteBuf WriteInt(int value)
    { Wrapped.WriteInt(value); return this; }

    public override int ReadIntLE()
    { return Wrapped.ReadIntLE(); }

    public override ByteBuf WriteIntLE(int value)
    { Wrapped.WriteIntLE(value); return this; }

    public override long ReadLong()
    { return Wrapped.ReadLong(); }

    public override ByteBuf WriteLong(long value)
    { Wrapped.WriteLong(value); return this; }

    public override long ReadLongLE()
    { return Wrapped.ReadLongLE(); }

    public override ByteBuf WriteLongLE(long value)
    { Wrapped.WriteLongLE(value); return this; }

    public override float ReadFloat()
    { return Wrapped.ReadFloat(); }

    public override ByteBuf WriteFloat(float value)
    { Wrapped.WriteFloat(value); return this; }

    public override float ReadFloatLE()
    { return Wrapped.ReadFloatLE(); }

    public override ByteBuf WriteFloatLE(float value)
    { Wrapped.WriteFloatLE(value); return this; }

    public override double ReadDouble()
    { return Wrapped.ReadDouble(); }

    public override ByteBuf WriteDouble(double value)
    { Wrapped.WriteDouble(value); return this; }

    public override double ReadDoubleLE()
    { return Wrapped.ReadDoubleLE(); }

    public override ByteBuf WriteDoubleLE(double value)
    { Wrapped.WriteDoubleLE(value); return this; }

    public override ushort ReadUnsignedShort()
    { return Wrapped.ReadUnsignedShort(); }

    public override ushort ReadUnsignedShortLE()
    { return Wrapped.ReadUnsignedShortLE(); }

    public override uint ReadUnsignedInt()
    { return Wrapped.ReadUnsignedInt(); }

    public override uint ReadUnsignedIntLE()
    { return Wrapped.ReadUnsignedIntLE(); }

    public override int ReadUnsignedMedium()
    { return Wrapped.ReadUnsignedMedium(); }

    public override int ReadUnsignedMediumLE()
    { return Wrapped.ReadUnsignedMediumLE(); }

    public override char ReadChar()
    { return Wrapped.ReadChar(); }

    public override ByteBuf WriteChar(char value)
    { Wrapped.WriteChar(value); return this; }

    public override ByteBuf SkipBytes(int length)
    { Wrapped.SkipBytes(length); return this; }

    public override ByteBuf GetBytes(int index, Span<byte> destination)
    { Wrapped.GetBytes(index, destination); return this; }

    public override ByteBuf SetBytes(int index, ReadOnlySpan<byte> source)
    { Wrapped.SetBytes(index, source); return this; }

    public override ByteBuf ReadBytes(Span<byte> destination)
    { Wrapped.ReadBytes(destination); return this; }

    public override ByteBuf WriteBytes(ReadOnlySpan<byte> source)
    { Wrapped.WriteBytes(source); return this; }

    public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
    { Wrapped.GetBytes(index, destination, destinationIndex, length); return this; }

    public override ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
    { Wrapped.SetBytes(index, source, sourceIndex, length); return this; }

    public override ByteBuf ReadBytes(ByteBuf destination, int length)
    { Wrapped.ReadBytes(destination, length); return this; }

    public override ByteBuf WriteBytes(ByteBuf source, int length)
    { Wrapped.WriteBytes(source, length); return this; }

    public override int GetBytes(int index, Socket destination, int length)
    { return Wrapped.GetBytes(index, destination, length); }

    public override ValueTask<int> GetBytesAsync(int index, Socket destination, int length,
        CancellationToken cancellationToken = default)
    { return Wrapped.GetBytesAsync(index, destination, length, cancellationToken); }

    public override int SetBytes(int index, Socket source, int length)
    { return Wrapped.SetBytes(index, source, length); }

    public override ValueTask<int> SetBytesAsync(int index, Socket source, int length,
        CancellationToken cancellationToken = default)
    { return Wrapped.SetBytesAsync(index, source, length, cancellationToken); }

    public override int ReadBytes(Socket destination, int length)
    { return Wrapped.ReadBytes(destination, length); }

    public override ValueTask<int> ReadBytesAsync(Socket destination, int length,
        CancellationToken cancellationToken = default)
    { return Wrapped.ReadBytesAsync(destination, length, cancellationToken); }

    public override int WriteBytes(Socket source, int length)
    { return Wrapped.WriteBytes(source, length); }

    public override ValueTask<int> WriteBytesAsync(Socket source, int length,
        CancellationToken cancellationToken = default)
    { return Wrapped.WriteBytesAsync(source, length, cancellationToken); }

    public override int GetBytes(int index, SafeFileHandle destination, long position, int length)
    { return Wrapped.GetBytes(index, destination, position, length); }

    public override int SetBytes(int index, SafeFileHandle source, long position, int length)
    { return Wrapped.SetBytes(index, source, position, length); }

    public override int ReadBytes(SafeFileHandle destination, long position, int length)
    { return Wrapped.ReadBytes(destination, position, length); }

    public override int WriteBytes(SafeFileHandle source, long position, int length)
    { return Wrapped.WriteBytes(source, position, length); }

    public override ValueTask<int> GetBytesAsync(int index, SafeFileHandle destination, long position, int length,
        CancellationToken cancellationToken = default)
    { return Wrapped.GetBytesAsync(index, destination, position, length, cancellationToken); }

    public override ValueTask<int> SetBytesAsync(int index, SafeFileHandle source, long position, int length,
        CancellationToken cancellationToken = default)
    { return Wrapped.SetBytesAsync(index, source, position, length, cancellationToken); }

    public override ValueTask<int> ReadBytesAsync(SafeFileHandle destination, long position, int length,
        CancellationToken cancellationToken = default)
    { return Wrapped.ReadBytesAsync(destination, position, length, cancellationToken); }

    public override ValueTask<int> WriteBytesAsync(SafeFileHandle source, long position, int length,
        CancellationToken cancellationToken = default)
    { return Wrapped.WriteBytesAsync(source, position, length, cancellationToken); }

    public override ReadOnlyMemory<byte> AsReadOnlyMemory(int index, int length)
    { return Wrapped.AsReadOnlyMemory(index, length); }

    public override ReadOnlySpan<byte> AsReadOnlySpan(int index, int length)
    { return Wrapped.AsReadOnlySpan(index, length); }

    public override ReadOnlySequence<byte> AsReadOnlySequence(int index, int length)
    { return Wrapped.AsReadOnlySequence(index, length); }

    public override ByteBuf GetBytes(int index, Stream destination, int length)
    { Wrapped.GetBytes(index, destination, length); return this; }

    public override int SetBytes(int index, Stream source, int length)
    { return Wrapped.SetBytes(index, source, length); }

    public override ByteBuf ReadBytes(Stream destination, int length)
    { Wrapped.ReadBytes(destination, length); return this; }

    public override int WriteBytes(Stream source, int length)
    { return Wrapped.WriteBytes(source, length); }

    public override ValueTask GetBytesAsync(int index, Stream destination, int length,
        CancellationToken cancellationToken = default)
    { return Wrapped.GetBytesAsync(index, destination, length, cancellationToken); }

    public override ValueTask<int> SetBytesAsync(int index, Stream source, int length,
        CancellationToken cancellationToken = default)
    { return Wrapped.SetBytesAsync(index, source, length, cancellationToken); }

    public override ValueTask ReadBytesAsync(Stream destination, int length,
        CancellationToken cancellationToken = default)
    { return Wrapped.ReadBytesAsync(destination, length, cancellationToken); }

    public override ValueTask<int> WriteBytesAsync(Stream source, int length,
        CancellationToken cancellationToken = default)
    { return Wrapped.WriteBytesAsync(source, length, cancellationToken); }

    public override ByteBuf GetBytes(int index, ByteBuf destination)
    { Wrapped.GetBytes(index, destination); return this; }

    public override ByteBuf GetBytes(int index, ByteBuf destination, int length)
    { Wrapped.GetBytes(index, destination, length); return this; }

    public override ByteBuf SetBytes(int index, ByteBuf source)
    { Wrapped.SetBytes(index, source); return this; }

    public override ByteBuf SetBytes(int index, ByteBuf source, int length)
    { Wrapped.SetBytes(index, source, length); return this; }

    public override ByteBuf ReadBytes(ByteBuf destination)
    { Wrapped.ReadBytes(destination); return this; }

    public override ByteBuf ReadBytes(ByteBuf destination, int destinationIndex, int length)
    { Wrapped.ReadBytes(destination, destinationIndex, length); return this; }

    public override ByteBuf WriteBytes(ByteBuf source)
    { Wrapped.WriteBytes(source); return this; }

    public override ByteBuf WriteBytes(ByteBuf source, int sourceIndex, int length)
    { Wrapped.WriteBytes(source, sourceIndex, length); return this; }

    public override CompositeByteBuf AddComponent(ByteBuf buffer, bool increaseWriterIndex = false)
    { Wrapped.AddComponent(buffer, increaseWriterIndex); return this; }

    public override CompositeByteBuf AddComponent(int componentIndex, ByteBuf buffer, bool increaseWriterIndex = false)
    { Wrapped.AddComponent(componentIndex, buffer, increaseWriterIndex); return this; }

    public override CompositeByteBuf RemoveComponent(int componentIndex)
    { Wrapped.RemoveComponent(componentIndex); return this; }

    public override CompositeByteBuf RemoveComponents(int componentIndex, int numComponents)
    { Wrapped.RemoveComponents(componentIndex, numComponents); return this; }

    public override IReadOnlyList<ByteBuf> Decompose(int offset, int length)
    { return Wrapped.Decompose(offset, length); }

    public override CompositeByteBuf Consolidate()
    { Wrapped.Consolidate(); return this; }

    public override CompositeByteBuf Consolidate(int componentIndex, int numComponents)
    { Wrapped.Consolidate(componentIndex, numComponents); return this; }

    public override CompositeByteBuf DiscardReadComponents()
    { Wrapped.DiscardReadComponents(); return this; }

    public override CompositeByteBuf AddComponents(params ByteBuf[] buffers)
    { Wrapped.AddComponents(buffers); return this; }

    public override CompositeByteBuf AddComponents(ByteBuf[] buffers, bool increaseWriterIndex)
    { Wrapped.AddComponents(buffers, increaseWriterIndex); return this; }

    public override CompositeByteBuf AddComponents(int componentIndex, ByteBuf[] buffers, bool increaseWriterIndex = false)
    { Wrapped.AddComponents(componentIndex, buffers, increaseWriterIndex); return this; }

    public override CompositeByteBuf AddComponents(IEnumerable<ByteBuf> buffers, bool increaseWriterIndex = false)
    { Wrapped.AddComponents(buffers, increaseWriterIndex); return this; }

    public override CompositeByteBuf AddComponents(int componentIndex, IEnumerable<ByteBuf> buffers, bool increaseWriterIndex = false)
    { Wrapped.AddComponents(componentIndex, buffers, increaseWriterIndex); return this; }

    public override CompositeByteBuf AddFlattenedComponents(ByteBuf buffer, bool increaseWriterIndex = false)
    { Wrapped.AddFlattenedComponents(buffer, increaseWriterIndex); return this; }

    public override IEnumerator<ByteBuf> GetEnumerator()
    { return Wrapped.GetEnumerator(); }
}
