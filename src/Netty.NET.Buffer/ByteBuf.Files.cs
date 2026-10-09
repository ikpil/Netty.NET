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
using Microsoft.Win32.SafeHandles;

namespace Netty.NET.Buffer;

public abstract partial class ByteBuf
{
/**
     * Transfers this buffer's data starting at the specified absolute {@code index}
     * to the specified channel starting at the given file position.
     * This method does not modify {@code readerIndex} or {@code writerIndex} of
     * this buffer. This method does not modify the channel's position.
     *
     * @param position the file position at which the transfer is to begin
     * @param length the maximum number of bytes to transfer
     *
     * @return the actual number of bytes written out to the specified channel
     *
     * @throws IndexOutOfBoundsException
     *         if the specified {@code index} is less than {@code 0} or
     *         if {@code index + length} is greater than
     *            {@code this.capacity}
     * @throws IOException
     *         if the specified channel threw an exception during I/O
     */
    /// <summary>Writes an absolute buffer range at a file offset without moving either cursor.</summary>
    /// <returns>The requested length after a successful complete RandomAccess.Write.</returns>
    /// <remarks>CLR: RandomAccess writes the complete range or throws; Java permits
    /// a partial write count. The handle is borrowed and is never disposed here.
    /// Pooled snapshots use memory proportional to length, without borrowing native
    /// memory during I/O. File writes can have partial effects before throwing.</remarks>
    public int GetBytes(int index, SafeFileHandle destination, long position, int length)
    {
        CheckIndex(index, length);
        CheckFileTransfer(destination, position, length);
        if (length == 0) return 0;
        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            GetBytesCore(index, bytes.AsSpan(0, length));
            RandomAccess.Write(destination, bytes.AsSpan(0, length), position);
            return length;
        }
        finally { ArrayPool<byte>.Shared.Return(bytes); }
    }

/**
     * Transfers the content of the specified source channel starting at the given file position
     * to this buffer starting at the specified absolute {@code index}.
     * This method does not modify {@code readerIndex} or {@code writerIndex} of
     * this buffer. This method does not modify the channel's position.
     *
     * @param position the file position at which the transfer is to begin
     * @param length the maximum number of bytes to transfer
     *
     * @return the actual number of bytes read in from the specified channel.
     *         {@code -1} if the specified channel is closed or reached EOF.
     *
     * @throws IndexOutOfBoundsException
     *         if the specified {@code index} is less than {@code 0} or
     *         if {@code index + length} is greater than {@code this.capacity}
     * @throws IOException
     *         if the specified channel threw an exception during I/O
     */
    /// <summary>Reads once at a file offset into an absolute buffer range without moving either cursor.</summary>
    /// <returns>The actual count, or zero at EOF or for an empty range.</returns>
    /// <remarks>CLR: a closed handle throws instead of returning Java's minus-one
    /// sentinel. Composite storage also uses one bounded read. A throwing file read
    /// does not publish staging bytes; a later composite commit can change a prefix.</remarks>
    public int SetBytes(int index, SafeFileHandle source, long position, int length)
    {
        EnsureCanWrite();
        CheckIndex(index, length);
        CheckFileTransfer(source, position, length);
        if (length == 0) return 0;
        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            int count = RandomAccess.Read(source, bytes.AsSpan(0, length), position);
            if (count != 0) SetBytesCore(index, bytes.AsSpan(0, count));
            return count;
        }
        finally { ArrayPool<byte>.Shared.Return(bytes); }
    }

/**
     * Transfers this buffer's data starting at the current {@code readerIndex}
     * to the specified channel starting at the given file position.
     * This method does not modify the channel's position.
     *
     * @param position the file position at which the transfer is to begin
     * @param length the maximum number of bytes to transfer
     *
     * @return the actual number of bytes written out to the specified channel
     *
     * @throws IndexOutOfBoundsException
     *         if {@code length} is greater than {@code this.readableBytes}
     * @throws IOException
     *         if the specified channel threw an exception during I/O
     */
    /// <summary>Writes readable bytes at a file offset and advances the reader only after success.</summary>
    public int ReadBytes(SafeFileHandle destination, long position, int length)
    {
        CheckReadableBytes(length);
        int count = GetBytes(_readerIndex, destination, position, length);
        _readerIndex += count;
        return count;
    }

/**
     * Transfers the content of the specified channel starting at the given file position
     * to this buffer starting at the current {@code writerIndex} and increases the
     * {@code writerIndex} by the number of the transferred bytes.
     * This method does not modify the channel's position.
     * If {@code this.writableBytes} is less than {@code length}, {@link #ensureWritable(int)}
     * will be called in an attempt to expand capacity to accommodate.
     *
     * @param position the file position at which the transfer is to begin
     * @param length the maximum number of bytes to transfer
     *
     * @return the actual number of bytes read in from the specified channel.
     *         {@code -1} if the specified channel is closed or reached EOF.
     *
     * @throws IOException
     *         if the specified channel threw an exception during I/O
     */
    /// <summary>Reserves requested capacity, reads once at a file offset and advances by the actual count.</summary>
    /// <remarks>Handle/range/permission validation precedes growth. Once started,
    /// growth is not rolled back on EOF or I/O failure.</remarks>
    public int WriteBytes(SafeFileHandle source, long position, int length)
    {
        EnsureCanWrite();
        CheckFileTransfer(source, position, length);
        EnsureWritable(length);
        int count = SetBytes(_writerIndex, source, position, length);
        _writerIndex += count;
        return count;
    }

    /// <summary>Asynchronously writes an absolute snapshot at a file offset without moving either cursor.</summary>
    /// <returns>The requested length after a successful complete RandomAccess.WriteAsync.</returns>
    /// <remarks>CLR async counterpart of the pinned synchronous file contract.
    /// Keep the borrowed handle and buffer alive until completion. Exclude concurrent
    /// buffer mutation/release and external changes to the active cursor. Managed
    /// staging remains owned until I/O finishes; no native borrow survives await.</remarks>
    public async ValueTask<int> GetBytesAsync(int index, SafeFileHandle destination, long position, int length,
        CancellationToken cancellationToken = default)
    {
        CheckIndex(index, length);
        CheckFileTransfer(destination, position, length);
        cancellationToken.ThrowIfCancellationRequested();
        if (length == 0) return 0;
        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            GetBytesCore(index, bytes.AsSpan(0, length));
            await RandomAccess.WriteAsync(destination, bytes.AsMemory(0, length), position, cancellationToken)
                .ConfigureAwait(false);
            return length;
        }
        finally { ArrayPool<byte>.Shared.Return(bytes); }
    }

    /// <summary>Asynchronously reads once at a file offset into an absolute range without moving either cursor.</summary>
    /// <returns>The actual count, or zero at EOF or for an empty range.</returns>
    /// <remarks>A thrown cancellation or I/O failure does not publish staged input.
    /// A valid result commits despite late token cancellation. Buffer/handle ownership
    /// is borrowed through completion; concurrent mutation/release must be excluded.</remarks>
    public async ValueTask<int> SetBytesAsync(int index, SafeFileHandle source, long position, int length,
        CancellationToken cancellationToken = default)
    {
        EnsureCanWrite();
        CheckIndex(index, length);
        CheckFileTransfer(source, position, length);
        cancellationToken.ThrowIfCancellationRequested();
        if (length == 0) return 0;
        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            int count = await RandomAccess.ReadAsync(source, bytes.AsMemory(0, length), position, cancellationToken)
                .ConfigureAwait(false);
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

    /// <summary>Asynchronously writes readable bytes at a file offset, advancing the reader after success.</summary>
    /// <remarks>A file can be partially modified before failure or cancellation;
    /// the reader remains unchanged if the awaited write throws.</remarks>
    public async ValueTask<int> ReadBytesAsync(SafeFileHandle destination, long position, int length,
        CancellationToken cancellationToken = default)
    {
        CheckReadableBytes(length);
        int count = await GetBytesAsync(_readerIndex, destination, position, length, cancellationToken)
            .ConfigureAwait(false);
        _readerIndex += count;
        return count;
    }

    /// <summary>Reserves space, asynchronously reads once at a file offset and advances by the actual count.</summary>
    /// <remarks>Validation precedes cancellation; a pre-canceled token prevents growth
    /// and I/O. Once started, capacity growth is not rolled back. Exclude external
    /// writer changes until completion. The file's current position stays unchanged.</remarks>
    public async ValueTask<int> WriteBytesAsync(SafeFileHandle source, long position, int length,
        CancellationToken cancellationToken = default)
    {
        EnsureCanWrite();
        CheckFileTransfer(source, position, length);
        if (length > MaxWritableBytes) throw new ArgumentOutOfRangeException(nameof(length));
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWritable(length);
        int count = await SetBytesAsync(_writerIndex, source, position, length, cancellationToken)
            .ConfigureAwait(false);
        _writerIndex += count;
        return count;
    }

    private static void CheckFileTransfer(SafeFileHandle handle, long position, int length)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (handle.IsClosed) throw new ObjectDisposedException(nameof(handle));
        if (handle.IsInvalid) throw new ArgumentException("The file handle is invalid.", nameof(handle));
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        // Validate the complete file range without signed overflow, before any I/O or growth.
        if (length > long.MaxValue - position) throw new ArgumentOutOfRangeException(nameof(length));
    }
}
