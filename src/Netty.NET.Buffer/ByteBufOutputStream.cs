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
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Buffer;

/**
 * An {@link OutputStream} which writes data to a {@link ByteBuf}.
 * <p>
 * A write operation against this stream will occur at the {@code writerIndex}
 * of its underlying buffer and the {@code writerIndex} will increase during
 * the write operation.
 * <p>
 * This stream implements {@link DataOutput} for your convenience.
 * The endianness of the stream is not always big endian but depends on
 * the endianness of the underlying buffer.
 *
 * @see ByteBufInputStream
 */
/// <remarks>Writes share the buffer's writer index. Numeric helpers use explicit big endian
/// wire order. Dispose always closes this adapter; releaseOnDispose transfers one existing
/// reference without retaining it. Concurrent writes, external index changes and release
/// must be excluded while using this stream.</remarks>
public sealed class ByteBufOutputStream : Stream
{
    private readonly ByteBuf _buffer;
    private readonly int _startIndex;
    private readonly bool _releaseOnDispose;
    private bool _closed;

    /**
     * Creates a new stream which writes data to the specified {@code buffer}.
     */
    public ByteBufOutputStream(ByteBuf buffer) : this(buffer, false) { }

    /**
     * Creates a new stream which writes data to the specified {@code buffer}.
     *
     * @param buffer Writes data to the buffer for this {@link OutputStream}.
     * @param releaseOnClose {@code true} means that when {@link #close()} is called then {@link ByteBuf#release()} will
     *                       be called on {@code buffer}.
     */
    public ByteBufOutputStream(ByteBuf buffer, bool releaseOnDispose)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.AsReadOnlySequence(buffer.ReaderIndex, 0);
        _buffer = buffer;
        _startIndex = buffer.WriterIndex;
        _releaseOnDispose = releaseOnDispose;
    }

    /**
     * Returns the number of written bytes by this stream so far.
     */
    public int BytesWritten => _buffer.WriterIndex - _startIndex;

    /**
     * Returns the buffer where this stream is writing data.
     */
    public ByteBuf Buffer => _buffer;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !_closed && !_buffer.IsReadOnly;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (!_closed)
            {
                _closed = true;
                if (disposing && _releaseOnDispose) _buffer.Release();
            }
        }
        finally { base.Dispose(disposing); }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> source)
    { ThrowIfClosed(); _buffer.WriteBytes(source); }

    public override void WriteByte(byte value)
    { ThrowIfClosed(); _buffer.WriteByte(value); }

    public void WriteSByte(sbyte value) => WriteByte(unchecked((byte)value));
    public void WriteBoolean(bool value) { ThrowIfClosed(); _buffer.WriteBoolean(value); }
    public void WriteInt16(int value) { ThrowIfClosed(); _buffer.WriteShort(value); }
    public void WriteUInt16(ushort value) => WriteInt16(value);
    public void WriteChar(char value) { ThrowIfClosed(); _buffer.WriteChar(value); }
    public void WriteInt32(int value) { ThrowIfClosed(); _buffer.WriteInt(value); }
    public void WriteInt64(long value) { ThrowIfClosed(); _buffer.WriteLong(value); }
    public void WriteSingle(float value) { ThrowIfClosed(); _buffer.WriteFloat(value); }
    public void WriteDouble(double value) { ThrowIfClosed(); _buffer.WriteDouble(value); }

    /// <summary>Writes the low eight bits of each UTF-16 code unit, without encoding fallback.</summary>
    public void WriteBytes(string value)
    {
        ThrowIfClosed();
        ArgumentNullException.ThrowIfNull(value);
        // We don't use `ByteBuf.writeCharSequence` here, because `writeBytes` is specified to only write the
        // lower-order by of multibyte characters (exactly one byte per character in the string), while
        // `writeCharSequence` will instead write a '?' replacement character.
        _buffer.EnsureWritable(value.Length);
        int offset = _buffer.WriterIndex;
        for (int i = 0; i < value.Length; ++i) _buffer.SetByte(offset + i, unchecked((byte)value[i]));
        _buffer.WriterIndex = offset + value.Length;
    }

    /// <summary>Writes each UTF-16 code unit as a big endian two-byte value.</summary>
    public void WriteChars(string value)
    {
        ThrowIfClosed();
        ArgumentNullException.ThrowIfNull(value);
        // CLR: validate permission even for an empty string. Preserve per-character progress
        // on capacity failure, rather than making the original loop transactional.
        _buffer.EnsureWritable(0);
        foreach (char character in value) _buffer.WriteChar(character);
    }

    /// <summary>Writes Java DataOutput's unsigned-length-prefixed modified UTF-8 wire format.</summary>
    public void WriteUtf(string value)
    {
        ThrowIfClosed();
        ArgumentNullException.ThrowIfNull(value);
        // lazily-instantiated
        // Suppress a warning since the stream is closed in the close() method
        // CLR: encode a complete frame directly; no lazily created DataOutputStream is needed.
        int length = 0;
        foreach (char character in value)
        {
            int width = character is > '\0' and < '\u0080' ? 1 : character <= '\u07FF' ? 2 : 3;
            if (width > ushort.MaxValue - length)
                throw new InvalidDataException("The modified UTF-8 payload exceeds 65535 bytes.");
            length += width;
        }
        byte[] bytes = new byte[length + 2];
        bytes[0] = unchecked((byte)(length >>> 8));
        bytes[1] = unchecked((byte)length);
        int offset = 2;
        foreach (char character in value)
        {
            if (character is > '\0' and < '\u0080') bytes[offset++] = (byte)character;
            else if (character <= '\u07FF')
            {
                bytes[offset++] = (byte)(0xC0 | character >> 6);
                bytes[offset++] = (byte)(0x80 | character & 0x3F);
            }
            else
            {
                bytes[offset++] = (byte)(0xE0 | character >> 12);
                bytes[offset++] = (byte)(0x80 | character >> 6 & 0x3F);
                bytes[offset++] = (byte)(0x80 | character & 0x3F);
            }
        }
        Write(bytes);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return ValueTask.FromCanceled(cancellationToken);
        try { Write(source.Span); return ValueTask.CompletedTask; }
        catch (Exception exception) { return ValueTask.FromException(exception); }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    private void ThrowIfClosed() => ObjectDisposedException.ThrowIf(_closed, this);
    public override void Flush() => ThrowIfClosed();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override int Read(Span<byte> buffer) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
