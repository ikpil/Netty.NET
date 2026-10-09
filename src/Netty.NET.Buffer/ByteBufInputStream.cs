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
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Buffer;

/**
 * An {@link InputStream} which reads data from a {@link ByteBuf}.
 * <p>
 * A read operation against this stream will occur at the {@code readerIndex}
 * of its underlying buffer and the {@code readerIndex} will increase during
 * the read operation.  Please note that it only reads up to the number of
 * readable bytes determined at the moment of construction.  Therefore,
 * updating {@link ByteBuf#writerIndex()} will not affect the return
 * value of {@link #available()}.
 * <p>
 * This stream implements {@link DataInput} for your convenience.
 * The endianness of the stream is not always big endian but depends on
 * the endianness of the underlying buffer.
 *
 * @see ByteBufOutputStream
 */
/// <remarks>Reads share the buffer's reader index. The captured end never follows later writes.
/// Numeric helpers use explicit big endian wire order. This stream is not thread-safe;
/// keep buffer storage and indices stable except for appending writes. Dispose always closes
/// this adapter; releaseOnDispose transfers one existing reference without retaining it.</remarks>
public sealed class ByteBufInputStream : Stream
{
    private readonly ByteBuf _buffer;
    private readonly int _startIndex, _endIndex;
    private bool _closed;
    /**
     * To preserve backwards compatibility (which didn't transfer ownership) we support a conditional flag which
     * indicates if {@link #buffer} should be released when this {@link InputStream} is closed.
     * However in future releases ownership should always be transferred and callers of this class should call
     * {@link ReferenceCounted#retain()} if necessary.
     */
    private readonly bool _releaseOnDispose;
    private StringBuilder _lineBuffer;

    /**
     * Creates a new stream which reads data from the specified {@code buffer}
     * starting at the current {@code readerIndex} and ending at the current
     * {@code writerIndex}.
     * @param buffer The buffer which provides the content for this {@link InputStream}.
     */
    public ByteBufInputStream(ByteBuf buffer) : this(buffer, ReadableLength(buffer), false) { }

    /**
     * Creates a new stream which reads data from the specified {@code buffer}
     * starting at the current {@code readerIndex} and ending at
     * {@code readerIndex + length}.
     * @param buffer The buffer which provides the content for this {@link InputStream}.
     * @param length The length of the buffer to use for this {@link InputStream}.
     * @throws IndexOutOfBoundsException
     *         if {@code readerIndex + length} is greater than
     *            {@code writerIndex}
     */
    public ByteBufInputStream(ByteBuf buffer, int length) : this(buffer, length, false) { }

    /**
     * Creates a new stream which reads data from the specified {@code buffer}
     * starting at the current {@code readerIndex} and ending at the current
     * {@code writerIndex}.
     * @param buffer The buffer which provides the content for this {@link InputStream}.
     * @param releaseOnClose {@code true} means that when {@link #close()} is called then {@link ByteBuf#release()} will
     *                       be called on {@code buffer}.
     */
    public ByteBufInputStream(ByteBuf buffer, bool releaseOnDispose)
        : this(buffer, ReadableLength(buffer), releaseOnDispose) { }

    /**
     * Creates a new stream which reads data from the specified {@code buffer}
     * starting at the current {@code readerIndex} and ending at
     * {@code readerIndex + length}.
     * @param buffer The buffer which provides the content for this {@link InputStream}.
     * @param length The length of the buffer to use for this {@link InputStream}.
     * @param releaseOnClose {@code true} means that when {@link #close()} is called then {@link ByteBuf#release()} will
     *                       be called on {@code buffer}.
     * @throws IndexOutOfBoundsException
     *         if {@code readerIndex + length} is greater than
     *            {@code writerIndex}
     */
    public ByteBufInputStream(ByteBuf buffer, int length, bool releaseOnDispose)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (length < 0 || length > buffer.ReadableBytes)
        {
            // Match ownership transfer on invalid length: return the supplied reference.
            if (releaseOnDispose) buffer.Release();
            throw new ArgumentOutOfRangeException(nameof(length));
        }
        // Reject dead owners before publishing the adapter or altering marks.
        buffer.AsReadOnlySequence(buffer.ReaderIndex, 0);
        _buffer = buffer;
        _releaseOnDispose = releaseOnDispose;
        _startIndex = buffer.ReaderIndex;
        _endIndex = _startIndex + length;
        buffer.MarkReaderIndex();
    }

    private static int ReadableLength(ByteBuf buffer)
    { ArgumentNullException.ThrowIfNull(buffer); return buffer.ReadableBytes; }

    /**
     * Returns the number of read bytes by this stream so far.
     */
    public int BytesRead => _buffer.ReaderIndex - _startIndex;

    public int Available
    {
        get
        {
            ThrowIfClosed();
            int reader = _buffer.ReaderIndex;
            if (reader < _startIndex || reader > _endIndex)
                throw new InvalidOperationException("The buffer reader index moved outside this stream's range.");
            return _endIndex - reader;
        }
    }

    public override bool CanRead => !_closed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    protected override void Dispose(bool disposing)
    {
        try
        {
            // The Closable interface says "If the stream is already closed then invoking this method has no effect."
            // CLR: close borrowed adapters too; only ownership release is conditional.
            if (!_closed)
            {
                _closed = true;
                if (disposing && _releaseOnDispose) _buffer.Release();
            }
        }
        finally { base.Dispose(disposing); }
    }

    // Suppress a warning since the class is not thread-safe
    public void Mark() { ThrowIfClosed(); _buffer.MarkReaderIndex(); }

    // Suppress a warning since the class is not thread-safe
    public void Reset() { ThrowIfClosed(); _buffer.ResetReaderIndex(); }

    public override int ReadByte()
    {
        if (Available == 0) return -1;
        return _buffer.ReadByte();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> destination)
    {
        int count = Math.Min(Available, destination.Length);
        // CLR Stream.Read returns zero at EOF and for empty destinations.
        if (count != 0) _buffer.ReadBytes(destination[..count]);
        return count;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return ValueTask.FromCanceled<int>(cancellationToken);
        try { return ValueTask.FromResult(Read(destination.Span)); }
        catch (Exception exception) { return ValueTask.FromException<int>(exception); }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public long Skip(long count) => count <= 0 ? SkipBytes(0) : SkipBytes((int)Math.Min(count, int.MaxValue));

    public int SkipBytes(int count)
    {
        int available = Available;
        if (count <= 0) return 0;
        count = Math.Min(available, count);
        _buffer.SkipBytes(count);
        return count;
    }

    public bool ReadBoolean() => ReadUnsignedByte() != 0;
    public sbyte ReadSByte() => unchecked((sbyte)ReadUnsignedByte());
    public byte ReadUnsignedByte() { CheckAvailable(1); return _buffer.ReadByte(); }
    public short ReadInt16() { CheckAvailable(2); return _buffer.ReadShort(); }
    public ushort ReadUInt16() => unchecked((ushort)ReadInt16());
    public char ReadChar() => unchecked((char)ReadInt16());
    public int ReadInt32() { CheckAvailable(4); return _buffer.ReadInt(); }
    public long ReadInt64() { CheckAvailable(8); return _buffer.ReadLong(); }
    public float ReadSingle() => BitConverter.Int32BitsToSingle(ReadInt32());
    public double ReadDouble() => BitConverter.Int64BitsToDouble(ReadInt64());

    public void ReadFully(byte[] buffer)
    { ArgumentNullException.ThrowIfNull(buffer); ReadFully(buffer.AsSpan()); }

    public void ReadFully(byte[] buffer, int offset, int count)
    { ValidateBufferArguments(buffer, offset, count); ReadFully(buffer.AsSpan(offset, count)); }

    public void ReadFully(Span<byte> destination)
    { CheckAvailable(destination.Length); _buffer.ReadBytes(destination); }

    /// <summary>Reads a byte-oriented line, mapping each octet to one UTF-16 code unit.</summary>
    public string ReadLine()
    {
        int available = Available;
        if (available == 0) return null;
        _lineBuffer?.Clear();
        do
        {
            byte value = _buffer.ReadByte();
            --available;
            if (value == '\n') break;
            if (value == '\r')
            {
                if (available > 0 && _buffer.GetByte(_buffer.ReaderIndex) == '\n') _buffer.SkipBytes(1);
                break;
            }
            (_lineBuffer ??= new StringBuilder()).Append((char)value);
        } while (available > 0);
        return _lineBuffer?.ToString() ?? string.Empty;
    }

    /// <summary>Reads Java DataInput's unsigned-length-prefixed modified UTF-8 wire format.</summary>
    public string ReadUtf()
    {
        // Encoding.UTF8 and BinaryReader.ReadString have different framing, NUL and surrogate rules.
        int length = ReadUInt16();
        byte[] bytes = new byte[length];
        ReadFully(bytes);
        char[] characters = new char[length];
        int written = 0;
        for (int i = 0; i < length;)
        {
            int first = bytes[i++];
            if (first < 0x80) { characters[written++] = (char)first; continue; }
            if ((first & 0xE0) == 0xC0)
            {
                if (i >= length || (bytes[i] & 0xC0) != 0x80) throw new InvalidDataException("Malformed modified UTF-8.");
                characters[written++] = (char)((first & 0x1F) << 6 | bytes[i++] & 0x3F);
            }
            else if ((first & 0xF0) == 0xE0)
            {
                if (length - i < 2 || (bytes[i] & 0xC0) != 0x80 || (bytes[i + 1] & 0xC0) != 0x80)
                    throw new InvalidDataException("Malformed modified UTF-8.");
                characters[written++] = (char)((first & 0x0F) << 12 | (bytes[i] & 0x3F) << 6 | bytes[i + 1] & 0x3F);
                i += 2;
            }
            else throw new InvalidDataException("Malformed modified UTF-8.");
        }
        return new string(characters, 0, written);
    }

    private void CheckAvailable(int fieldSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fieldSize);
        int available = Available;
        if (fieldSize > available) throw new EndOfStreamException($"Needs {fieldSize} bytes, but only {available} remain.");
    }

    private void ThrowIfClosed() => ObjectDisposedException.ThrowIf(_closed, this);
    public override void Flush() => ThrowIfClosed();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();
}
