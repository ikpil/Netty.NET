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
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec;

/**
 * Special {@link ByteBuf} implementation which is used by the {@link ReplayingDecoder}
 */
// CLR: the C# ByteBuf merges the abstract ByteBuf and AbstractByteBuf. Its index members (readerIndex()/
// readerIndex(int), writerIndex()/writerIndex(int), readableBytes(), writableBytes(), maxWritableBytes(),
// maxCapacity(), isReadable(), isWritable(), setIndex, clear, mark/resetReaderIndex and mark/resetWriterIndex) are
// non-virtual and read this instance's own index fields, and "You can't override a non-virtual or static method"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override). So this view keeps its own
// indices in step with the wrapped buffer: PullIndices copies the wrapped reader index and keeps the writer index at
// int.MaxValue (Capacity) while not terminated, or at the wrapped writer index once terminated. ReadableBytes,
// IsReadable(), WritableBytes, MaxWritableBytes and IsWritable() then return what the Java overrides return, except
// that once terminated WritableBytes/MaxWritableBytes/IsWritable() follow the wrapped capacity (Java: 0/0/false).
// Remaining differences: WriterIndex reads int.MaxValue while not terminated (Java: the wrapped writer index);
// MaxCapacity stays int.MaxValue (Java: capacity()); clear, setIndex, writerIndex(int), markWriterIndex and
// resetWriterIndex change this view's indices instead of throwing; markReaderIndex/resetReaderIndex use this view's
// mark (Java: the wrapped buffer's); and a reader index set through the ReaderIndex property or ResetReaderIndex()
// reaches the wrapped buffer at the next operation that uses the wrapped reader index or when ReplayingDecoder calls
// FlushReaderIndex (Java: at once, so an out-of-range index throws there).
internal sealed class ReplayingDecoderByteBuf : ByteBuf
{
    // CLR: every closed ReplayingDecoder<S> reads the same pooled Signal (see ReplayingDecoder<S>.REPLAY).
    private static readonly Signal REPLAY = ReplayingDecoder<object>.REPLAY;

    private ByteBuf buffer;
    private bool terminated;
    // CLR: the cached SwappedByteBuf of order(ByteOrder) is omitted with that deprecated method (see below).

    // CLR: the reader index last copied between this view and the wrapped buffer (see FlushReaderIndex).
    private int syncedReaderIndex;

    // CLR: @SuppressWarnings("checkstyle:StaticFinalBuffer") has no C# counterpart; its comment follows.
    // Unpooled.EMPTY_BUFFER is not writeable or readable.
    internal static readonly ReplayingDecoderByteBuf EMPTY_BUFFER = new ReplayingDecoderByteBuf(Unpooled.EmptyBuffer);

    static ReplayingDecoderByteBuf()
    {
        EMPTY_BUFFER.Terminate();
    }

    // CLR: the indices are bounded by Capacity, which is int.MaxValue while not terminated.
    internal ReplayingDecoderByteBuf() : base(int.MaxValue) { }

    internal ReplayingDecoderByteBuf(ByteBuf buffer) : base(int.MaxValue)
    {
        SetCumulation(buffer);
    }

    internal void SetCumulation(ByteBuf buffer)
    {
        this.buffer = buffer;
        // CLR: this view's indices follow the new buffer (see the class comment).
        PullIndices();
    }

    internal void Terminate()
    {
        terminated = true;
        // CLR: the writer index becomes the wrapped writer index (see the class comment).
        PullIndices();
    }

    public override int Capacity
    {
        get
        {
            if (terminated)
            {
                return buffer.Capacity;
            }
            else
            {
                return int.MaxValue;
            }
        }
        // CLR: capacity(int newCapacity) is the Capacity setter.
        set => throw Reject();
    }

    // CLR: maxCapacity() is the non-virtual MaxCapacity, fixed at int.MaxValue by the constructors (see the class
    // comment); the source returns capacity().

    public override IByteBufAllocator Alloc()
    {
        return buffer.Alloc();
    }

    public override bool IsReadOnly => false;

    // CLR: Unpooled.unmodifiableBuffer(ByteBuf) is deprecated ("Use ByteBuf#asReadOnly()") and the Buffer port has no
    // such factory; ByteBuf.AsReadOnly builds the same read-only wrapper over this buffer.
    public override ByteBuf AsReadOnly()
    {
        return base.AsReadOnly();
    }

    public override bool IsDirect => buffer.IsDirect;

    // CLR: hasArray(), array(), arrayOffset(), hasMemoryAddress() and memoryAddress() have no ByteBuf counterpart; the
    // Buffer port replaces them with borrowed Memory (MemoryMarshal.TryGetArray) and scoped pins.

    // CLR: clear() is non-virtual (see the class comment); the source throws reject().

    public override bool Equals(object obj)
    {
        return ReferenceEquals(this, obj);
    }

    // CLR: IEquatable<ByteBuf>.Equals is the same identity check as equals(Object).
    public override bool Equals(ByteBuf other)
    {
        return ReferenceEquals(this, other);
    }

    public override int CompareTo(ByteBuf buffer)
    {
        throw Reject();
    }

    public override ByteBuf Copy()
    {
        throw Reject();
    }

    public override ByteBuf Copy(int index, int length)
    {
        CheckIndex(index, length);
        return buffer.Copy(index, length);
    }

    public override ByteBuf DiscardReadBytes()
    {
        throw Reject();
    }

    public override ByteBuf EnsureWritable(int writableBytes)
    {
        throw Reject();
    }

    public override int EnsureWritable(int minWritableBytes, bool force)
    {
        throw Reject();
    }

    public override ByteBuf Duplicate()
    {
        throw Reject();
    }

    public override ByteBuf RetainedDuplicate()
    {
        throw Reject();
    }

    public override bool GetBoolean(int index)
    {
        CheckIndex(index, 1);
        return buffer.GetBoolean(index);
    }

    // CLR: GetByte returns the unsigned octet, so it also is getUnsignedByte(int).
    public override byte GetByte(int index)
    {
        CheckIndex(index, 1);
        return buffer.GetByte(index);
    }

    // CLR: getBytes(int, byte[], int, int) and getBytes(int, byte[]) are this Span overload; the source's rejecting
    // getBytes(int, ByteBuffer) has no separate CLR overload.
    public override ByteBuf GetBytes(int index, Span<byte> dst)
    {
        CheckIndex(index, dst.Length);
        buffer.GetBytes(index, dst);
        return this;
    }

    public override ByteBuf GetBytes(int index, ByteBuf dst, int dstIndex, int length)
    {
        CheckIndex(index, length);
        buffer.GetBytes(index, dst, dstIndex, length);
        return this;
    }

    public override ByteBuf GetBytes(int index, ByteBuf dst, int length)
    {
        throw Reject();
    }

    public override ByteBuf GetBytes(int index, ByteBuf dst)
    {
        throw Reject();
    }

    // CLR: GatheringByteChannel is Socket, as in the Buffer port; the Async overload is the same rejected operation.
    public override int GetBytes(int index, Socket @out, int length)
    {
        throw Reject();
    }

    public override ValueTask<int> GetBytesAsync(int index, Socket @out, int length,
            CancellationToken cancellationToken = default)
    {
        throw Reject();
    }

    // CLR: FileChannel is SafeFileHandle, as in the Buffer port; the Async overload is the same rejected operation.
    public override int GetBytes(int index, SafeFileHandle @out, long position, int length)
    {
        throw Reject();
    }

    public override ValueTask<int> GetBytesAsync(int index, SafeFileHandle @out, long position, int length,
            CancellationToken cancellationToken = default)
    {
        throw Reject();
    }

    // CLR: OutputStream is Stream, as in the Buffer port; the Async overload is the same rejected operation.
    public override ByteBuf GetBytes(int index, Stream @out, int length)
    {
        throw Reject();
    }

    public override ValueTask GetBytesAsync(int index, Stream @out, int length,
            CancellationToken cancellationToken = default)
    {
        throw Reject();
    }

    public override int GetInt(int index)
    {
        CheckIndex(index, 4);
        return buffer.GetInt(index);
    }

    public override int GetIntLE(int index)
    {
        CheckIndex(index, 4);
        return buffer.GetIntLE(index);
    }

    public override uint GetUnsignedInt(int index)
    {
        CheckIndex(index, 4);
        return buffer.GetUnsignedInt(index);
    }

    public override uint GetUnsignedIntLE(int index)
    {
        CheckIndex(index, 4);
        return buffer.GetUnsignedIntLE(index);
    }

    public override long GetLong(int index)
    {
        CheckIndex(index, 8);
        return buffer.GetLong(index);
    }

    public override long GetLongLE(int index)
    {
        CheckIndex(index, 8);
        return buffer.GetLongLE(index);
    }

    public override int GetMedium(int index)
    {
        CheckIndex(index, 3);
        return buffer.GetMedium(index);
    }

    public override int GetMediumLE(int index)
    {
        CheckIndex(index, 3);
        return buffer.GetMediumLE(index);
    }

    public override int GetUnsignedMedium(int index)
    {
        CheckIndex(index, 3);
        return buffer.GetUnsignedMedium(index);
    }

    public override int GetUnsignedMediumLE(int index)
    {
        CheckIndex(index, 3);
        return buffer.GetUnsignedMediumLE(index);
    }

    public override short GetShort(int index)
    {
        CheckIndex(index, 2);
        return buffer.GetShort(index);
    }

    public override short GetShortLE(int index)
    {
        CheckIndex(index, 2);
        return buffer.GetShortLE(index);
    }

    public override ushort GetUnsignedShort(int index)
    {
        CheckIndex(index, 2);
        return buffer.GetUnsignedShort(index);
    }

    public override ushort GetUnsignedShortLE(int index)
    {
        CheckIndex(index, 2);
        return buffer.GetUnsignedShortLE(index);
    }

    public override char GetChar(int index)
    {
        CheckIndex(index, 2);
        return buffer.GetChar(index);
    }

    public override float GetFloat(int index)
    {
        CheckIndex(index, 4);
        return buffer.GetFloat(index);
    }

    public override double GetDouble(int index)
    {
        CheckIndex(index, 8);
        return buffer.GetDouble(index);
    }

    public override ICharSequence GetCharSequence(int index, int length, Encoding charset)
    {
        CheckIndex(index, length);
        return buffer.GetCharSequence(index, length, charset);
    }

    public override int GetHashCode()
    {
        throw Reject();
    }

    public override int IndexOf(int fromIndex, int toIndex, byte value)
    {
        if (fromIndex == toIndex)
        {
            return -1;
        }

        if (Math.Max(fromIndex, toIndex) > buffer.WriterIndex)
        {
            throw REPLAY;
        }

        return buffer.IndexOf(fromIndex, toIndex, value);
    }

    public override int BytesBefore(byte value)
    {
        // CLR: the wrapped buffer searches from its reader index (see FlushReaderIndex).
        FlushReaderIndex();
        int bytes = buffer.BytesBefore(value);
        if (bytes < 0)
        {
            throw REPLAY;
        }
        return bytes;
    }

    public override int BytesBefore(int length, byte value)
    {
        // CLR: the wrapped buffer's reader index is this view's (see FlushReaderIndex).
        FlushReaderIndex();
        return BytesBefore(buffer.ReaderIndex, length, value);
    }

    public override int BytesBefore(int index, int length, byte value)
    {
        int writerIndex = buffer.WriterIndex;
        if (index >= writerIndex)
        {
            throw REPLAY;
        }

        if (index <= writerIndex - length)
        {
            return buffer.BytesBefore(index, length, value);
        }

        int res = buffer.BytesBefore(index, writerIndex - index, value);
        if (res < 0)
        {
            throw REPLAY;
        }
        else
        {
            return res;
        }
    }

    // CLR: ByteProcessor is Func<byte, bool> in the Buffer port (Netty.NET.Common.ByteProcessor holds the constants).
    public override int ForEachByte(Func<byte, bool> processor)
    {
        // CLR: the wrapped buffer iterates its readable bytes (see FlushReaderIndex).
        FlushReaderIndex();
        int ret = buffer.ForEachByte(processor);
        if (ret < 0)
        {
            throw REPLAY;
        }
        else
        {
            return ret;
        }
    }

    public override int ForEachByte(int index, int length, Func<byte, bool> processor)
    {
        int writerIndex = buffer.WriterIndex;
        if (index >= writerIndex)
        {
            throw REPLAY;
        }

        if (index <= writerIndex - length)
        {
            return buffer.ForEachByte(index, length, processor);
        }

        int ret = buffer.ForEachByte(index, writerIndex - index, processor);
        if (ret < 0)
        {
            throw REPLAY;
        }
        else
        {
            return ret;
        }
    }

    public override int ForEachByteDesc(Func<byte, bool> processor)
    {
        if (terminated)
        {
            // CLR: the wrapped buffer iterates its readable bytes (see FlushReaderIndex).
            FlushReaderIndex();
            return buffer.ForEachByteDesc(processor);
        }
        else
        {
            throw Reject();
        }
    }

    public override int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
    {
        if (index + length > buffer.WriterIndex)
        {
            throw REPLAY;
        }

        return buffer.ForEachByteDesc(index, length, processor);
    }

    // CLR: markReaderIndex() and markWriterIndex() are non-virtual (see the class comment); the source delegates the
    // former to the wrapped buffer and throws reject() from the latter.

    // CLR: order() and order(ByteOrder) are not ported: ByteBuf.order is @deprecated "use the Little Endian
    // accessors, e.g. getShortLE, getIntLE instead of creating a buffer with swapped endianness", and the Buffer port
    // has explicit BE/LE accessors and no SwappedByteBuf (docs/buffer-porting.md, Explicit endian replacements for
    // swapped buffers).

    // CLR: isReadable() is non-virtual; while not terminated this view's writer index is int.MaxValue, so it is true
    // as the source's "!terminated || buffer.isReadable()" (see the class comment).

    public override bool IsReadable(int size)
    {
        // CLR: the wrapped buffer counts from its reader index (see FlushReaderIndex).
        FlushReaderIndex();
        return !terminated || buffer.IsReadable(size);
    }

    // CLR: readableBytes() is non-virtual; with this view's writer index it is int.MaxValue - readerIndex while not
    // terminated and the wrapped readable bytes once terminated, as in the source (see the class comment).

    public override bool ReadBoolean()
    {
        CheckReadableBytes(1);
        return PullIndices(buffer.ReadBoolean());
    }

    // CLR: ReadByte returns the unsigned octet, so it also is readUnsignedByte().
    public override byte ReadByte()
    {
        CheckReadableBytes(1);
        return PullIndices(buffer.ReadByte());
    }

    // CLR: readBytes(byte[], int, int) and readBytes(byte[]) are this Span overload; the source's rejecting
    // readBytes(ByteBuffer) has no separate CLR overload.
    public override ByteBuf ReadBytes(Span<byte> dst)
    {
        CheckReadableBytes(dst.Length);
        buffer.ReadBytes(dst);
        PullIndices();
        return this;
    }

    public override ByteBuf ReadBytes(ByteBuf dst, int dstIndex, int length)
    {
        CheckReadableBytes(length);
        buffer.ReadBytes(dst, dstIndex, length);
        PullIndices();
        return this;
    }

    public override ByteBuf ReadBytes(ByteBuf dst, int length)
    {
        throw Reject();
    }

    public override ByteBuf ReadBytes(ByteBuf dst)
    {
        CheckReadableBytes(dst.WritableBytes);
        buffer.ReadBytes(dst);
        PullIndices();
        return this;
    }

    // CLR: GatheringByteChannel is Socket, as in the Buffer port; the Async overload is the same rejected operation.
    public override int ReadBytes(Socket @out, int length)
    {
        throw Reject();
    }

    public override ValueTask<int> ReadBytesAsync(Socket @out, int length,
            CancellationToken cancellationToken = default)
    {
        throw Reject();
    }

    // CLR: FileChannel is SafeFileHandle, as in the Buffer port; the Async overload is the same rejected operation.
    public override int ReadBytes(SafeFileHandle @out, long position, int length)
    {
        throw Reject();
    }

    public override ValueTask<int> ReadBytesAsync(SafeFileHandle @out, long position, int length,
            CancellationToken cancellationToken = default)
    {
        throw Reject();
    }

    public override ByteBuf ReadBytes(int length)
    {
        CheckReadableBytes(length);
        return PullIndices(buffer.ReadBytes(length));
    }

    public override ByteBuf ReadSlice(int length)
    {
        CheckReadableBytes(length);
        return PullIndices(buffer.ReadSlice(length));
    }

    public override ByteBuf ReadRetainedSlice(int length)
    {
        CheckReadableBytes(length);
        return PullIndices(buffer.ReadRetainedSlice(length));
    }

    // CLR: OutputStream is Stream, as in the Buffer port; the Async overload is the same rejected operation.
    public override ByteBuf ReadBytes(Stream @out, int length)
    {
        throw Reject();
    }

    public override ValueTask ReadBytesAsync(Stream @out, int length, CancellationToken cancellationToken = default)
    {
        throw Reject();
    }

    // CLR: readerIndex() and readerIndex(int readerIndex) are the non-virtual ReaderIndex property, which this view
    // keeps equal to the wrapped reader index (see the class comment and FlushReaderIndex).

    public override int ReadInt()
    {
        CheckReadableBytes(4);
        return PullIndices(buffer.ReadInt());
    }

    public override int ReadIntLE()
    {
        CheckReadableBytes(4);
        return PullIndices(buffer.ReadIntLE());
    }

    public override uint ReadUnsignedInt()
    {
        CheckReadableBytes(4);
        return PullIndices(buffer.ReadUnsignedInt());
    }

    public override uint ReadUnsignedIntLE()
    {
        CheckReadableBytes(4);
        return PullIndices(buffer.ReadUnsignedIntLE());
    }

    public override long ReadLong()
    {
        CheckReadableBytes(8);
        return PullIndices(buffer.ReadLong());
    }

    public override long ReadLongLE()
    {
        CheckReadableBytes(8);
        return PullIndices(buffer.ReadLongLE());
    }

    public override int ReadMedium()
    {
        CheckReadableBytes(3);
        return PullIndices(buffer.ReadMedium());
    }

    public override int ReadMediumLE()
    {
        CheckReadableBytes(3);
        return PullIndices(buffer.ReadMediumLE());
    }

    public override int ReadUnsignedMedium()
    {
        CheckReadableBytes(3);
        return PullIndices(buffer.ReadUnsignedMedium());
    }

    public override int ReadUnsignedMediumLE()
    {
        CheckReadableBytes(3);
        return PullIndices(buffer.ReadUnsignedMediumLE());
    }

    public override short ReadShort()
    {
        CheckReadableBytes(2);
        return PullIndices(buffer.ReadShort());
    }

    public override short ReadShortLE()
    {
        CheckReadableBytes(2);
        return PullIndices(buffer.ReadShortLE());
    }

    public override ushort ReadUnsignedShort()
    {
        CheckReadableBytes(2);
        return PullIndices(buffer.ReadUnsignedShort());
    }

    public override ushort ReadUnsignedShortLE()
    {
        CheckReadableBytes(2);
        return PullIndices(buffer.ReadUnsignedShortLE());
    }

    public override char ReadChar()
    {
        CheckReadableBytes(2);
        return PullIndices(buffer.ReadChar());
    }

    public override float ReadFloat()
    {
        CheckReadableBytes(4);
        return PullIndices(buffer.ReadFloat());
    }

    public override double ReadDouble()
    {
        CheckReadableBytes(8);
        return PullIndices(buffer.ReadDouble());
    }

    public override ICharSequence ReadCharSequence(int length, Encoding charset)
    {
        CheckReadableBytes(length);
        return PullIndices(buffer.ReadCharSequence(length, charset));
    }

    public override string ReadString(int length, Encoding charset)
    {
        CheckReadableBytes(length);
        return PullIndices(buffer.ReadString(length, charset));
    }

    // CLR: resetReaderIndex() and resetWriterIndex() are non-virtual (see the class comment); the source delegates
    // the former to the wrapped buffer and throws reject() from the latter.

    public override ByteBuf SetBoolean(int index, bool value)
    {
        throw Reject();
    }

    public override ByteBuf SetByte(int index, int value)
    {
        throw Reject();
    }

    // CLR: setBytes(int, byte[], int, int), setBytes(int, byte[]) and setBytes(int, ByteBuffer) are this ReadOnlySpan
    // overload.
    public override ByteBuf SetBytes(int index, ReadOnlySpan<byte> src)
    {
        throw Reject();
    }

    public override ByteBuf SetBytes(int index, ByteBuf src, int srcIndex, int length)
    {
        throw Reject();
    }

    public override ByteBuf SetBytes(int index, ByteBuf src, int length)
    {
        throw Reject();
    }

    public override ByteBuf SetBytes(int index, ByteBuf src)
    {
        throw Reject();
    }

    // CLR: InputStream is Stream, as in the Buffer port; the Async overload is the same rejected operation.
    public override int SetBytes(int index, Stream @in, int length)
    {
        throw Reject();
    }

    public override ValueTask<int> SetBytesAsync(int index, Stream @in, int length,
            CancellationToken cancellationToken = default)
    {
        throw Reject();
    }

    public override ByteBuf SetZero(int index, int length)
    {
        throw Reject();
    }

    // CLR: ScatteringByteChannel is Socket, as in the Buffer port; the Async overload is the same rejected operation.
    public override int SetBytes(int index, Socket @in, int length)
    {
        throw Reject();
    }

    public override ValueTask<int> SetBytesAsync(int index, Socket @in, int length,
            CancellationToken cancellationToken = default)
    {
        throw Reject();
    }

    // CLR: FileChannel is SafeFileHandle, as in the Buffer port; the Async overload is the same rejected operation.
    public override int SetBytes(int index, SafeFileHandle @in, long position, int length)
    {
        throw Reject();
    }

    public override ValueTask<int> SetBytesAsync(int index, SafeFileHandle @in, long position, int length,
            CancellationToken cancellationToken = default)
    {
        throw Reject();
    }

    // CLR: setIndex(int, int) is non-virtual (see the class comment); the source throws reject().

    public override ByteBuf SetInt(int index, int value)
    {
        throw Reject();
    }

    public override ByteBuf SetIntLE(int index, int value)
    {
        throw Reject();
    }

    public override ByteBuf SetLong(int index, long value)
    {
        throw Reject();
    }

    public override ByteBuf SetLongLE(int index, long value)
    {
        throw Reject();
    }

    public override ByteBuf SetMedium(int index, int value)
    {
        throw Reject();
    }

    public override ByteBuf SetMediumLE(int index, int value)
    {
        throw Reject();
    }

    public override ByteBuf SetShort(int index, int value)
    {
        throw Reject();
    }

    public override ByteBuf SetShortLE(int index, int value)
    {
        throw Reject();
    }

    // CLR: the Buffer port's SetChar takes a char (setChar(int, int) in the source).
    public override ByteBuf SetChar(int index, char value)
    {
        throw Reject();
    }

    public override ByteBuf SetFloat(int index, float value)
    {
        throw Reject();
    }

    public override ByteBuf SetDouble(int index, double value)
    {
        throw Reject();
    }

    public override ByteBuf SkipBytes(int length)
    {
        CheckReadableBytes(length);
        buffer.SkipBytes(length);
        PullIndices();
        return this;
    }

    public override ByteBuf Slice()
    {
        throw Reject();
    }

    public override ByteBuf RetainedSlice()
    {
        throw Reject();
    }

    public override ByteBuf Slice(int index, int length)
    {
        CheckIndex(index, length);
        return buffer.Slice(index, length);
    }

    public override ByteBuf RetainedSlice(int index, int length)
    {
        CheckIndex(index, length);
        return buffer.RetainedSlice(index, length);
    }

    // CLR: nioBufferCount() has no ByteBuf counterpart. nioBuffer() and nioBuffers() are the non-virtual
    // ReadableMemory and ReadableSequence properties, which range over ReadableBytes and so reach CheckIndex below
    // (REPLAY while not terminated) instead of throwing reject().

    // CLR: nioBuffer(int, int) is AsReadOnlyMemory, which carries its Javadoc in the Buffer port.
    public override ReadOnlyMemory<byte> AsReadOnlyMemory(int index, int length)
    {
        CheckIndex(index, length);
        return buffer.AsReadOnlyMemory(index, length);
    }

    // CLR: nioBuffers(int, int) is AsReadOnlySequence, whose segments replace the NIO buffer array.
    public override ReadOnlySequence<byte> AsReadOnlySequence(int index, int length)
    {
        CheckIndex(index, length);
        return buffer.AsReadOnlySequence(index, length);
    }

    // CLR: internalNioBuffer(int, int) is AsMemory, the writable borrowed view of a range.
    public override Memory<byte> AsMemory(int index, int length)
    {
        CheckIndex(index, length);
        return buffer.AsMemory(index, length);
    }

    public override string ToString(int index, int length, Encoding charset)
    {
        CheckIndex(index, length);
        return buffer.ToString(index, length, charset);
    }

    public override string ToString(Encoding charsetName)
    {
        throw Reject();
    }

    // CLR: the widx part reads the wrapped writer index, which the source's writerIndex() returns (this view's
    // WriterIndex is int.MaxValue while not terminated); numbers use the invariant culture.
    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) + '(' +
               "ridx=" +
               ReaderIndex.ToString(CultureInfo.InvariantCulture) +
               ", " +
               "widx=" +
               buffer.WriterIndex.ToString(CultureInfo.InvariantCulture) +
               ')';
    }

    // CLR: isWritable() is non-virtual; it is false while not terminated, as in the source (see the class comment).

    public override bool IsWritable(int size)
    {
        return false;
    }

    // CLR: writableBytes() and maxWritableBytes() are non-virtual; they are 0 while not terminated, as in the source
    // (see the class comment).

    public override ByteBuf WriteBoolean(bool value)
    {
        throw Reject();
    }

    public override ByteBuf WriteByte(int value)
    {
        throw Reject();
    }

    // CLR: writeBytes(byte[], int, int), writeBytes(byte[]) and writeBytes(ByteBuffer) are this ReadOnlySpan overload.
    public override ByteBuf WriteBytes(ReadOnlySpan<byte> src)
    {
        throw Reject();
    }

    public override ByteBuf WriteBytes(ByteBuf src, int srcIndex, int length)
    {
        throw Reject();
    }

    public override ByteBuf WriteBytes(ByteBuf src, int length)
    {
        throw Reject();
    }

    public override ByteBuf WriteBytes(ByteBuf src)
    {
        throw Reject();
    }

    // CLR: InputStream is Stream, as in the Buffer port; the Async overload is the same rejected operation.
    public override int WriteBytes(Stream @in, int length)
    {
        throw Reject();
    }

    public override ValueTask<int> WriteBytesAsync(Stream @in, int length, CancellationToken cancellationToken = default)
    {
        throw Reject();
    }

    // CLR: ScatteringByteChannel is Socket, as in the Buffer port; the Async overload is the same rejected operation.
    public override int WriteBytes(Socket @in, int length)
    {
        throw Reject();
    }

    public override ValueTask<int> WriteBytesAsync(Socket @in, int length, CancellationToken cancellationToken = default)
    {
        throw Reject();
    }

    // CLR: FileChannel is SafeFileHandle, as in the Buffer port; the Async overload is the same rejected operation.
    public override int WriteBytes(SafeFileHandle @in, long position, int length)
    {
        throw Reject();
    }

    public override ValueTask<int> WriteBytesAsync(SafeFileHandle @in, long position, int length,
            CancellationToken cancellationToken = default)
    {
        throw Reject();
    }

    public override ByteBuf WriteInt(int value)
    {
        throw Reject();
    }

    public override ByteBuf WriteIntLE(int value)
    {
        throw Reject();
    }

    public override ByteBuf WriteLong(long value)
    {
        throw Reject();
    }

    public override ByteBuf WriteLongLE(long value)
    {
        throw Reject();
    }

    public override ByteBuf WriteMedium(int value)
    {
        throw Reject();
    }

    public override ByteBuf WriteMediumLE(int value)
    {
        throw Reject();
    }

    public override ByteBuf WriteZero(int length)
    {
        throw Reject();
    }

    // CLR: writerIndex() and writerIndex(int) are the non-virtual WriterIndex property (see the class comment); the
    // source returns the wrapped writer index and throws reject() from the setter.

    public override ByteBuf WriteShort(int value)
    {
        throw Reject();
    }

    public override ByteBuf WriteShortLE(int value)
    {
        throw Reject();
    }

    // CLR: the Buffer port's WriteChar takes a char (writeChar(int) in the source).
    public override ByteBuf WriteChar(char value)
    {
        throw Reject();
    }

    public override ByteBuf WriteFloat(float value)
    {
        throw Reject();
    }

    public override ByteBuf WriteDouble(double value)
    {
        throw Reject();
    }

    // CLR: CharSequence/Charset are ReadOnlySpan<char>/Encoding in the Buffer port.
    public override int SetCharSequence(int index, ReadOnlySpan<char> sequence, Encoding charset)
    {
        throw Reject();
    }

    public override int WriteCharSequence(ReadOnlySpan<char> sequence, Encoding charset)
    {
        throw Reject();
    }

    // CLR: hides ByteBuf.CheckIndex (new modifier,
    // https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/new-modifier).
    private new void CheckIndex(int index, int length)
    {
        if (index + length > buffer.WriterIndex)
        {
            throw REPLAY;
        }
    }

    // CLR: hides ByteBuf.CheckReadableBytes (new modifier); this view's reader index is pushed first, since the
    // wrapped readable bytes are counted from the wrapped reader index (see FlushReaderIndex).
    private new void CheckReadableBytes(int readableBytes)
    {
        FlushReaderIndex();
        if (buffer.ReadableBytes < readableBytes)
        {
            throw REPLAY;
        }
    }

    public override ByteBuf DiscardSomeReadBytes()
    {
        throw Reject();
    }

    public override int RefCnt => buffer.RefCnt;

    // CLR: retain() and retain(int increment) are one method with a default argument in the Buffer port.
    public override ByteBuf Retain(int increment = 1)
    {
        throw Reject();
    }

    // CLR: touch() and touch(Object hint) are one method with a default argument in the Buffer port.
    public override ByteBuf Touch(object hint = null)
    {
        buffer.Touch(hint);
        return this;
    }

    // CLR: release() and release(int decrement) are one method with a default argument in the Buffer port.
    public override bool Release(int decrement = 1)
    {
        throw Reject();
    }

    public override ByteBuf Unwrap()
    {
        throw Reject();
    }

    // CLR: UnsupportedOperationException is NotSupportedException.
    private static NotSupportedException Reject()
    {
        return new NotSupportedException("not a replayable operation");
    }

    // CLR: the Buffer port's storage hooks, which other buffers use when this buffer is the source of a copy. Reads
    // check the range as getBytes(int, byte[], int, int) does and then use the wrapped buffer's public views (a derived
    // class cannot reach the wrapped buffer's protected hooks, "only if the access occurs through a variable of the
    // derived class type", https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/protected-internal);
    // writes are rejected as the set*/write* operations are.
    protected override Memory<byte> GetMemoryCore(int index, int length)
    {
        throw Reject();
    }

    protected override bool TryGetMemoryCore(int index, int length, out Memory<byte> memory)
    {
        throw Reject();
    }

    protected override ReadOnlyMemory<byte> GetReadOnlyMemoryCore(int index, int length)
    {
        CheckIndex(index, length);
        return buffer.AsReadOnlyMemory(index, length);
    }

    protected override bool TryGetReadOnlyMemoryCore(int index, int length, out ReadOnlyMemory<byte> memory)
    {
        CheckIndex(index, length);
        ReadOnlySequence<byte> sequence = buffer.AsReadOnlySequence(index, length);
        if (sequence.IsSingleSegment)
        {
            memory = sequence.First;
            return true;
        }
        memory = default;
        return false;
    }

    protected override ReadOnlySequence<byte> GetReadOnlySequenceCore(int index, int length)
    {
        CheckIndex(index, length);
        return buffer.AsReadOnlySequence(index, length);
    }

    protected override void GetBytesCore(int index, Span<byte> destination)
    {
        CheckIndex(index, destination.Length);
        buffer.GetBytes(index, destination);
    }

    protected override void SetBytesCore(int index, ReadOnlySpan<byte> source)
    {
        throw Reject();
    }

    // CLR: a protected internal member overridden from another assembly is protected.
    protected override void SetBytesFromIoCore(int index, ReadOnlySpan<byte> source)
    {
        throw Reject();
    }

    protected override void SetZeroCore(int index, int length)
    {
        throw Reject();
    }

    // CLR: copies the wrapped reader index to this view (clamped for a released buffer, whose capacity may have
    // shrunk), and the writer index described in the class comment.
    private void PullIndices()
    {
        ByteBuf buffer = this.buffer;
        if (buffer == null)
        {
            return;
        }
        int writerIndex = terminated ? Math.Min(buffer.WriterIndex, buffer.Capacity) : int.MaxValue;
        int readerIndex = Math.Min(buffer.ReaderIndex, writerIndex);
        SetIndex(readerIndex, writerIndex);
        syncedReaderIndex = readerIndex;
    }

    // CLR: PullIndices after a delegated read, returning the read's result.
    private T PullIndices<T>(T result)
    {
        PullIndices();
        return result;
    }

    // CLR: copies this view's reader index to the wrapped buffer if it was set through the non-virtual ReaderIndex
    // property or ResetReaderIndex() since the last copy (the source's readerIndex(int) and resetReaderIndex() update
    // the wrapped buffer at once). ReplayingDecoder calls it before it reads the cumulation's reader index.
    internal void FlushReaderIndex()
    {
        int readerIndex = ReaderIndex;
        if (readerIndex != syncedReaderIndex)
        {
            buffer.ReaderIndex = readerIndex;
            syncedReaderIndex = readerIndex;
        }
    }
}
