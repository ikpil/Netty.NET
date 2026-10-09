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
using System.IO;

namespace Netty.NET.Buffer;

public abstract partial class ByteBuf
{
/**
     * Transfers this buffer's data to the specified stream starting at the
     * specified absolute {@code index}.
     * This method does not modify {@code readerIndex} or {@code writerIndex} of
     * this buffer.
     *
     * @param length the number of bytes to transfer
     *
     * @throws IndexOutOfBoundsException
     *         if the specified {@code index} is less than {@code 0} or
     *         if {@code index + length} is greater than
     *            {@code this.capacity}
     * @throws IOException
     *         if the specified stream threw an exception during I/O
     */
    /// <summary>Writes a snapshot of an absolute range without changing buffer indices.</summary>
    /// <remarks>The stream is borrowed. Temporary pooled storage prevents callbacks
    /// from invalidating native memory or later composite segments during the write.
    /// Exclude concurrent changes, release and external cursor mutation during I/O.</remarks>
    public ByteBuf GetBytes(int index, Stream destination, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        if (length == 0) return this;
        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            GetBytesCore(index, bytes.AsSpan(0, length));
            destination.Write(bytes.AsSpan(0, length));
        }
        finally { ArrayPool<byte>.Shared.Return(bytes); }
        return this;
    }

/**
     * Transfers the content of the specified source stream to this buffer
     * starting at the specified absolute {@code index}.
     * This method does not modify {@code readerIndex} or {@code writerIndex} of
     * this buffer.
     *
     * @param length the number of bytes to transfer
     *
     * @return the actual number of bytes read in from the specified channel.
     *         {@code -1} if the specified {@link InputStream} reached EOF.
     *
     * @throws IndexOutOfBoundsException
     *         if the specified {@code index} is less than {@code 0} or
     *         if {@code index + length} is greater than {@code this.capacity}
     * @throws IOException
     *         if the specified stream threw an exception during I/O
     */
    /// <summary>Performs one bounded Stream.Read into an absolute range.</summary>
    /// <returns>The actual count, or zero at EOF or for an empty request.</returns>
    /// <remarks>CLR: all storage kinds use one read, including composites. A short
    /// read is returned immediately; callers requiring a full field must loop.
    /// Staging means a throwing read cannot publish bytes into this buffer. The
    /// stream is borrowed; this method does not retain the buffer or change indices.</remarks>
    public int SetBytes(int index, Stream source, int length)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureCanWrite();
        CheckIndex(index, length);
        if (length == 0) return 0;
        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            int count = source.Read(bytes.AsSpan(0, length));
            if (count < 0 || count > length)
                throw new IOException("The stream returned an invalid byte count.");
            // A stream callback can resize storage. Resolve the destination again
            // after reading instead of keeping a borrowed native span across I/O.
            if (count != 0)
            {
                EnsureCanWrite();
                CheckIndex(index, count);
                SetBytesCore(index, bytes.AsSpan(0, count));
            }
            return count;
        }
        finally { ArrayPool<byte>.Shared.Return(bytes); }
    }

/**
     * Transfers this buffer's data to the specified stream starting at the
     * current {@code readerIndex}.
     *
     * @param length the number of bytes to transfer
     *
     * @throws IndexOutOfBoundsException
     *         if {@code length} is greater than {@code this.readableBytes}
     * @throws IOException
     *         if the specified stream threw an exception during I/O
     */
    /// <summary>Writes readable bytes and advances the reader only after success.</summary>
    /// <remarks>A throwing destination may already have accepted some bytes; the
    /// buffer reader remains unchanged. The stream is not disposed.</remarks>
    public ByteBuf ReadBytes(Stream destination, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckReadableBytes(length);
        GetBytes(_readerIndex, destination, length);
        _readerIndex += length;
        return this;
    }

/**
     * Transfers the content of the specified stream to this buffer
     * starting at the current {@code writerIndex} and increases the
     * {@code writerIndex} by the number of the transferred bytes.
     * If {@code this.writableBytes} is less than {@code length}, {@link #ensureWritable(int)}
     * will be called in an attempt to expand capacity to accommodate.
     *
     * @param length the number of bytes to transfer
     *
     * @return the actual number of bytes read in from the specified channel.
     *         {@code -1} if the specified {@link InputStream} reached EOF.
     *
     * @throws IOException if the specified stream threw an exception during I/O
     */
    /// <summary>Reserves the requested space, reads once and advances by the actual count.</summary>
    /// <returns>The actual count, or zero at EOF or for an empty request.</returns>
    /// <remarks>Capacity can grow even at EOF or on I/O failure, as in Netty.
    /// The writer advances only after a successful read and buffer transfer.</remarks>
    public int WriteBytes(Stream source, int length)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureWritable(length);
        int count = SetBytes(_writerIndex, source, length);
        _writerIndex += count;
        return count;
    }
}
