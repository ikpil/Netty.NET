/*
 * Copyright 2012 The Netty Project
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
using System.Buffers.Binary;

namespace Netty.NET.Buffer;

public abstract partial class ByteBuf
{
    // CLR: contiguous borrows never copy. Segmented operations use bounded
    // component transfers; words crossing a boundary use at most eight stack bytes.
    protected virtual bool TryGetMemoryCore(int index, int length, out Memory<byte> memory)
    { memory = GetMemoryCore(index, length); return true; }
    protected virtual bool TryGetReadOnlyMemoryCore(int index, int length, out ReadOnlyMemory<byte> memory)
    { memory = GetReadOnlyMemoryCore(index, length); return true; }
    protected virtual void GetBytesCore(int index, Span<byte> destination)
        => GetReadOnlyMemoryCore(index, destination.Length).Span.CopyTo(destination);
    protected virtual void SetBytesCore(int index, ReadOnlySpan<byte> source)
        => source.CopyTo(GetMemoryCore(index, source.Length).Span);
    protected virtual void SetZeroCore(int index, int length) => GetMemoryCore(index, length).Span.Clear();
    protected virtual ReadOnlySequence<byte> GetReadOnlySequenceCore(int index, int length)
        => new(GetReadOnlyMemoryCore(index, length));

    internal bool TryGetMemory(int index, int length, out Memory<byte> memory)
    { EnsureCanWrite(); CheckIndex(index, length); return TryGetMemoryCore(index, length, out memory); }
    internal bool TryGetReadOnlyMemory(int index, int length, out ReadOnlyMemory<byte> memory)
    { CheckIndex(index, length); return TryGetReadOnlyMemoryCore(index, length, out memory); }

/**
     * Exposes this buffer's bytes as an NIO {@link ByteBuffer}'s for the specified index and length
     * The returned buffer either share or contains the copied content of this buffer, while changing
     * the position and limit of the returned NIO buffer does not affect the indexes and marks of this buffer.
     * This method does not modify {@code readerIndex} or {@code writerIndex} of this buffer. Please note that the
     * returned NIO buffer will not see the changes of this buffer if this buffer is a dynamic
     * buffer and it adjusted its capacity.
     *
     * @throws UnsupportedOperationException
     *         if this buffer cannot create a {@link ByteBuffer} that shares the content with itself
     *
     * @see #nioBufferCount()
     * @see #nioBuffer()
     * @see #nioBuffer(int, int)
     */
    // CLR: borrowed ReadOnlySequence segments replace the NIO buffer array.
    /// <summary>Returns borrowed segments sharing the buffer's content without retaining it.</summary>
    /// <remarks>The segment layout is captured. Exclude resize, component changes and
    /// release while using this sequence; it does not acquire native pins.</remarks>
    public ReadOnlySequence<byte> AsReadOnlySequence(int index, int length)
    { CheckIndex(index, length); return GetReadOnlySequenceCore(index, length); }
    public ReadOnlySequence<byte> ReadableSequence => AsReadOnlySequence(ReaderIndex, ReadableBytes);

    internal virtual BufferMemoryLease AcquireReadLease() { EnsureAccessible(); return default; }

    private ulong ReadWord(int index, int width, bool littleEndian)
    {
        CheckIndex(index, width);
        if (TryGetReadOnlyMemoryCore(index, width, out var memory))
            return DecodeWord(memory.Span, littleEndian);
        Span<byte> bytes = stackalloc byte[8];
        GetBytesCore(index, bytes[..width]);
        return DecodeWord(bytes[..width], littleEndian);
    }

    private static ulong DecodeWord(ReadOnlySpan<byte> bytes, bool littleEndian)
    {
        return bytes.Length switch
        {
            2 => littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes),
            3 => (ulong)(littleEndian ? bytes[2] << 16 | bytes[1] << 8 | bytes[0] : bytes[0] << 16 | bytes[1] << 8 | bytes[2]),
            4 => littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes),
            8 => littleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(bytes) : BinaryPrimitives.ReadUInt64BigEndian(bytes),
            _ => throw new ArgumentOutOfRangeException(nameof(bytes))
        };
    }

    private ByteBuf SetWord(int index, ulong value, int width, bool littleEndian)
    {
        EnsureCanWrite();
        CheckIndex(index, width);
        if (TryGetMemoryCore(index, width, out var memory))
            EncodeWord(memory.Span, value, littleEndian);
        else
        {
            Span<byte> bytes = stackalloc byte[8];
            EncodeWord(bytes[..width], value, littleEndian);
            SetBytesCore(index, bytes[..width]);
        }
        return this;
    }

    private static void EncodeWord(Span<byte> bytes, ulong value, bool littleEndian)
    {
        switch (bytes.Length)
        {
            case 2:
                if (littleEndian) BinaryPrimitives.WriteUInt16LittleEndian(bytes, unchecked((ushort)value));
                else BinaryPrimitives.WriteUInt16BigEndian(bytes, unchecked((ushort)value));
                break;
            case 3:
                bytes[littleEndian ? 2 : 0] = unchecked((byte)(value >> 16));
                bytes[1] = unchecked((byte)(value >> 8));
                bytes[littleEndian ? 0 : 2] = unchecked((byte)value);
                break;
            case 4:
                if (littleEndian) BinaryPrimitives.WriteUInt32LittleEndian(bytes, unchecked((uint)value));
                else BinaryPrimitives.WriteUInt32BigEndian(bytes, unchecked((uint)value));
                break;
            case 8:
                if (littleEndian) BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
                else BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(bytes));
        }
    }
}
