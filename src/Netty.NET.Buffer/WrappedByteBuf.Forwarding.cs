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
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Netty.NET.Buffer;

// Keep public calls on the wrapped buffer. Sharing component/index state is
// sufficient for nonvirtual metadata, but must not bypass its virtual operations.
public partial class WrappedByteBuf
{
    public override Memory<byte> AsMemory(int index, int length)
    => Buffer.AsMemory(index, length);

    public override Span<byte> AsSpan(int index, int length)
    => Buffer.AsSpan(index, length);

    public override byte GetByte(int index)
    => Buffer.GetByte(index);

    public override bool GetBoolean(int index)
    => Buffer.GetBoolean(index);

    public override ByteBuf SetByte(int index, int value)
    { Buffer.SetByte(index, value); return this; }

    public override ByteBuf SetBoolean(int index, bool value)
    { Buffer.SetBoolean(index, value); return this; }

    public override short GetShort(int index)
    => Buffer.GetShort(index);

    public override ByteBuf SetShort(int index, int value)
    { Buffer.SetShort(index, value); return this; }

    public override short GetShortLE(int index)
    => Buffer.GetShortLE(index);

    public override ByteBuf SetShortLE(int index, int value)
    { Buffer.SetShortLE(index, value); return this; }

    public override int GetInt(int index)
    => Buffer.GetInt(index);

    public override ByteBuf SetInt(int index, int value)
    { Buffer.SetInt(index, value); return this; }

    public override int GetIntLE(int index)
    => Buffer.GetIntLE(index);

    public override ByteBuf SetIntLE(int index, int value)
    { Buffer.SetIntLE(index, value); return this; }

    public override long GetLong(int index)
    => Buffer.GetLong(index);

    public override ByteBuf SetLong(int index, long value)
    { Buffer.SetLong(index, value); return this; }

    public override long GetLongLE(int index)
    => Buffer.GetLongLE(index);

    public override ByteBuf SetLongLE(int index, long value)
    { Buffer.SetLongLE(index, value); return this; }

    public override int GetUnsignedMedium(int index)
    => Buffer.GetUnsignedMedium(index);

    public override int GetUnsignedMediumLE(int index)
    => Buffer.GetUnsignedMediumLE(index);

    public override int GetMedium(int index)
    => Buffer.GetMedium(index);

    public override int GetMediumLE(int index)
    => Buffer.GetMediumLE(index);

    public override ByteBuf SetMedium(int index, int value)
    { Buffer.SetMedium(index, value); return this; }

    public override ByteBuf SetMediumLE(int index, int value)
    { Buffer.SetMediumLE(index, value); return this; }

    public override ushort GetUnsignedShort(int index)
    => Buffer.GetUnsignedShort(index);

    public override ushort GetUnsignedShortLE(int index)
    => Buffer.GetUnsignedShortLE(index);

    public override uint GetUnsignedInt(int index)
    => Buffer.GetUnsignedInt(index);

    public override uint GetUnsignedIntLE(int index)
    => Buffer.GetUnsignedIntLE(index);

    public override char GetChar(int index)
    => Buffer.GetChar(index);

    public override ByteBuf SetChar(int index, char value)
    { Buffer.SetChar(index, value); return this; }

    public override float GetFloat(int index)
    => Buffer.GetFloat(index);

    public override float GetFloatLE(int index)
    => Buffer.GetFloatLE(index);

    public override double GetDouble(int index)
    => Buffer.GetDouble(index);

    public override double GetDoubleLE(int index)
    => Buffer.GetDoubleLE(index);

    public override ByteBuf SetFloat(int index, float value)
    { Buffer.SetFloat(index, value); return this; }

    public override ByteBuf SetFloatLE(int index, float value)
    { Buffer.SetFloatLE(index, value); return this; }

    public override ByteBuf SetDouble(int index, double value)
    { Buffer.SetDouble(index, value); return this; }

    public override ByteBuf SetDoubleLE(int index, double value)
    { Buffer.SetDoubleLE(index, value); return this; }

    public override byte ReadByte()
    => Buffer.ReadByte();

    public override ByteBuf WriteByte(int value)
    { Buffer.WriteByte(value); return this; }

    public override bool ReadBoolean()
    => Buffer.ReadBoolean();

    public override ByteBuf WriteBoolean(bool value)
    { Buffer.WriteBoolean(value); return this; }

    public override short ReadShort()
    => Buffer.ReadShort();

    public override ByteBuf WriteShort(int value)
    { Buffer.WriteShort(value); return this; }

    public override short ReadShortLE()
    => Buffer.ReadShortLE();

    public override ByteBuf WriteShortLE(int value)
    { Buffer.WriteShortLE(value); return this; }

    public override int ReadMedium()
    => Buffer.ReadMedium();

    public override ByteBuf WriteMedium(int value)
    { Buffer.WriteMedium(value); return this; }

    public override int ReadMediumLE()
    => Buffer.ReadMediumLE();

    public override ByteBuf WriteMediumLE(int value)
    { Buffer.WriteMediumLE(value); return this; }

    public override int ReadInt()
    => Buffer.ReadInt();

    public override ByteBuf WriteInt(int value)
    { Buffer.WriteInt(value); return this; }

    public override int ReadIntLE()
    => Buffer.ReadIntLE();

    public override ByteBuf WriteIntLE(int value)
    { Buffer.WriteIntLE(value); return this; }

    public override long ReadLong()
    => Buffer.ReadLong();

    public override ByteBuf WriteLong(long value)
    { Buffer.WriteLong(value); return this; }

    public override long ReadLongLE()
    => Buffer.ReadLongLE();

    public override ByteBuf WriteLongLE(long value)
    { Buffer.WriteLongLE(value); return this; }

    public override float ReadFloat()
    => Buffer.ReadFloat();

    public override ByteBuf WriteFloat(float value)
    { Buffer.WriteFloat(value); return this; }

    public override float ReadFloatLE()
    => Buffer.ReadFloatLE();

    public override ByteBuf WriteFloatLE(float value)
    { Buffer.WriteFloatLE(value); return this; }

    public override double ReadDouble()
    => Buffer.ReadDouble();

    public override ByteBuf WriteDouble(double value)
    { Buffer.WriteDouble(value); return this; }

    public override double ReadDoubleLE()
    => Buffer.ReadDoubleLE();

    public override ByteBuf WriteDoubleLE(double value)
    { Buffer.WriteDoubleLE(value); return this; }

    public override ushort ReadUnsignedShort()
    => Buffer.ReadUnsignedShort();

    public override ushort ReadUnsignedShortLE()
    => Buffer.ReadUnsignedShortLE();

    public override uint ReadUnsignedInt()
    => Buffer.ReadUnsignedInt();

    public override uint ReadUnsignedIntLE()
    => Buffer.ReadUnsignedIntLE();

    public override int ReadUnsignedMedium()
    => Buffer.ReadUnsignedMedium();

    public override int ReadUnsignedMediumLE()
    => Buffer.ReadUnsignedMediumLE();

    public override char ReadChar()
    => Buffer.ReadChar();

    public override ByteBuf WriteChar(char value)
    { Buffer.WriteChar(value); return this; }

    public override ByteBuf SkipBytes(int length)
    { Buffer.SkipBytes(length); return this; }

    public override ByteBuf GetBytes(int index, Span<byte> destination)
    { Buffer.GetBytes(index, destination); return this; }

    public override ByteBuf SetBytes(int index, ReadOnlySpan<byte> source)
    { Buffer.SetBytes(index, source); return this; }

    public override ByteBuf ReadBytes(Span<byte> destination)
    { Buffer.ReadBytes(destination); return this; }

    public override ByteBuf WriteBytes(ReadOnlySpan<byte> source)
    { Buffer.WriteBytes(source); return this; }

    public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
    { Buffer.GetBytes(index, destination, destinationIndex, length); return this; }

    public override ByteBuf SetBytes(int index, ByteBuf source, int sourceIndex, int length)
    { Buffer.SetBytes(index, source, sourceIndex, length); return this; }

    public override ByteBuf ReadBytes(ByteBuf destination, int length)
    { Buffer.ReadBytes(destination, length); return this; }

    public override ByteBuf WriteBytes(ByteBuf source, int length)
    { Buffer.WriteBytes(source, length); return this; }

    public override int GetBytes(int index, SafeFileHandle destination, long position, int length)
    => Buffer.GetBytes(index, destination, position, length);

    public override int SetBytes(int index, SafeFileHandle source, long position, int length)
    => Buffer.SetBytes(index, source, position, length);

    public override int ReadBytes(SafeFileHandle destination, long position, int length)
    => Buffer.ReadBytes(destination, position, length);

    public override int WriteBytes(SafeFileHandle source, long position, int length)
    => Buffer.WriteBytes(source, position, length);

    public override ValueTask<int> GetBytesAsync(int index, SafeFileHandle destination, long position, int length,
        CancellationToken cancellationToken = default)
    => Buffer.GetBytesAsync(index, destination, position, length, cancellationToken);

    public override ValueTask<int> SetBytesAsync(int index, SafeFileHandle source, long position, int length,
        CancellationToken cancellationToken = default)
    => Buffer.SetBytesAsync(index, source, position, length, cancellationToken);

    public override ValueTask<int> ReadBytesAsync(SafeFileHandle destination, long position, int length,
        CancellationToken cancellationToken = default)
    => Buffer.ReadBytesAsync(destination, position, length, cancellationToken);

    public override ValueTask<int> WriteBytesAsync(SafeFileHandle source, long position, int length,
        CancellationToken cancellationToken = default)
    => Buffer.WriteBytesAsync(source, position, length, cancellationToken);

    public override ReadOnlyMemory<byte> AsReadOnlyMemory(int index, int length)
    => Buffer.AsReadOnlyMemory(index, length);

    public override ReadOnlySpan<byte> AsReadOnlySpan(int index, int length)
    => Buffer.AsReadOnlySpan(index, length);

    public override ReadOnlySequence<byte> AsReadOnlySequence(int index, int length)
    => Buffer.AsReadOnlySequence(index, length);

    public override int GetBytes(int index, Socket destination, int length)
    => Buffer.GetBytes(index, destination, length);

    public override int SetBytes(int index, Socket source, int length)
    => Buffer.SetBytes(index, source, length);

    public override int ReadBytes(Socket destination, int length)
    => Buffer.ReadBytes(destination, length);

    public override int WriteBytes(Socket source, int length)
    => Buffer.WriteBytes(source, length);

    public override ValueTask<int> GetBytesAsync(int index, Socket destination, int length,
        CancellationToken cancellationToken = default)
    => Buffer.GetBytesAsync(index, destination, length, cancellationToken);

    public override ValueTask<int> SetBytesAsync(int index, Socket source, int length,
        CancellationToken cancellationToken = default)
    => Buffer.SetBytesAsync(index, source, length, cancellationToken);

    public override ValueTask<int> ReadBytesAsync(Socket destination, int length,
        CancellationToken cancellationToken = default)
    => Buffer.ReadBytesAsync(destination, length, cancellationToken);

    public override ValueTask<int> WriteBytesAsync(Socket source, int length,
        CancellationToken cancellationToken = default)
    => Buffer.WriteBytesAsync(source, length, cancellationToken);

    public override ByteBuf GetBytes(int index, Stream destination, int length)
    { Buffer.GetBytes(index, destination, length); return this; }

    public override int SetBytes(int index, Stream source, int length)
    => Buffer.SetBytes(index, source, length);

    public override ByteBuf ReadBytes(Stream destination, int length)
    { Buffer.ReadBytes(destination, length); return this; }

    public override int WriteBytes(Stream source, int length)
    => Buffer.WriteBytes(source, length);

    public override ValueTask GetBytesAsync(int index, Stream destination, int length,
        CancellationToken cancellationToken = default)
    => Buffer.GetBytesAsync(index, destination, length, cancellationToken);

    public override ValueTask<int> SetBytesAsync(int index, Stream source, int length,
        CancellationToken cancellationToken = default)
    => Buffer.SetBytesAsync(index, source, length, cancellationToken);

    public override ValueTask ReadBytesAsync(Stream destination, int length,
        CancellationToken cancellationToken = default)
    => Buffer.ReadBytesAsync(destination, length, cancellationToken);

    public override ValueTask<int> WriteBytesAsync(Stream source, int length,
        CancellationToken cancellationToken = default)
    => Buffer.WriteBytesAsync(source, length, cancellationToken);

    public override ByteBuf GetBytes(int index, ByteBuf destination)
    { Buffer.GetBytes(index, destination); return this; }

    public override ByteBuf GetBytes(int index, ByteBuf destination, int length)
    { Buffer.GetBytes(index, destination, length); return this; }

    public override ByteBuf SetBytes(int index, ByteBuf source)
    { Buffer.SetBytes(index, source); return this; }

    public override ByteBuf SetBytes(int index, ByteBuf source, int length)
    { Buffer.SetBytes(index, source, length); return this; }

    public override ByteBuf ReadBytes(ByteBuf destination)
    { Buffer.ReadBytes(destination); return this; }

    public override ByteBuf ReadBytes(ByteBuf destination, int destinationIndex, int length)
    { Buffer.ReadBytes(destination, destinationIndex, length); return this; }

    public override ByteBuf WriteBytes(ByteBuf source)
    { Buffer.WriteBytes(source); return this; }

    public override ByteBuf WriteBytes(ByteBuf source, int sourceIndex, int length)
    { Buffer.WriteBytes(source, sourceIndex, length); return this; }
}
