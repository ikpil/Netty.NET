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
using System.Threading;
using System.Threading.Tasks;

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

    /// <summary>Asynchronously writes a snapshot of an absolute range without changing indices.</summary>
    /// <remarks>CLR asynchronous counterpart of GetBytes(index, Stream, length).
    /// Calls the stream's Memory-based WriteAsync directly without wrapping synchronous I/O in Task.Run.
    /// The stream and buffer are borrowed for the full operation. Exclude concurrent
    /// mutation, release and external cursor changes until the returned ValueTask completes.
    /// Temporary managed storage is kept until I/O finishes; native memory is not borrowed across await.</remarks>
    public async ValueTask GetBytesAsync(int index, Stream destination, int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        cancellationToken.ThrowIfCancellationRequested();
        if (length == 0) return;
        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            GetBytesCore(index, bytes.AsSpan(0, length));
            await destination.WriteAsync(bytes.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
        }
        finally { ArrayPool<byte>.Shared.Return(bytes); }
    }

    /// <summary>Performs one asynchronous bounded read into an absolute range without changing indices.</summary>
    /// <returns>The actual count, or zero at EOF or for an empty request.</returns>
    /// <remarks>Uses the same one-read and staging policy as SetBytes(index, Stream, length).
    /// Cancellation or failure from the source does not publish staging bytes. Source
    /// consumption can already have occurred. A valid result is committed even if the
    /// token was canceled meanwhile: cancellation is cooperative, not a rollback.
    /// Keep the borrowed buffer and stream alive and exclude concurrent mutation until completion.</remarks>
    public async ValueTask<int> SetBytesAsync(int index, Stream source, int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureCanWrite();
        CheckIndex(index, length);
        cancellationToken.ThrowIfCancellationRequested();
        if (length == 0) return 0;
        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            int count = await source.ReadAsync(bytes.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            if (count < 0 || count > length)
                throw new IOException("The stream returned an invalid byte count.");
            if (count != 0)
            {
                // Resolve storage after I/O; no borrowed native span survives suspension.
                EnsureCanWrite();
                CheckIndex(index, count);
                SetBytesCore(index, bytes.AsSpan(0, count));
            }
            return count;
        }
        finally { ArrayPool<byte>.Shared.Return(bytes); }
    }

    /// <summary>Asynchronously writes readable bytes, then advances the reader after success.</summary>
    /// <remarks>Cancellation or failure leaves the reader unchanged even when the
    /// destination already accepted a prefix. Exclude external reader changes until completion.</remarks>
    public async ValueTask ReadBytesAsync(Stream destination, int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckReadableBytes(length);
        await GetBytesAsync(_readerIndex, destination, length, cancellationToken).ConfigureAwait(false);
        _readerIndex += length;
    }

    /// <summary>Reserves requested capacity, asynchronously reads once and advances by the actual count.</summary>
    /// <returns>The actual count, or zero at EOF or for an empty request.</returns>
    /// <remarks>A pre-canceled token prevents capacity growth and I/O after argument
    /// and permission validation. Once started, growth is not rolled back on EOF,
    /// cancellation or failure. Exclude external writer changes until completion.</remarks>
    public async ValueTask<int> WriteBytesAsync(Stream source, int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureCanWrite();
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (length > MaxWritableBytes) throw new ArgumentOutOfRangeException(nameof(length));
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWritable(length);
        int count = await SetBytesAsync(_writerIndex, source, length, cancellationToken).ConfigureAwait(false);
        _writerIndex += count;
        return count;
    }
}
