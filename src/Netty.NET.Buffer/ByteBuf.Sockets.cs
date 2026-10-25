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
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Buffer;

public abstract partial class ByteBuf
{
    /**
     * Transfers this buffer's data to the specified channel starting at the
     * specified absolute {@code index}.
     * This method does not modify {@code readerIndex} or {@code writerIndex} of
     * this buffer.
     *
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
    /// <summary>Sends an absolute snapshot once and returns the actual count without changing indices.</summary>
    /// <remarks>The connected socket is borrowed. Nonblocking WouldBlock returns zero;
    /// other socket errors propagate. Empty ranges validate the buffer but skip I/O.
    /// Managed staging costs O(length) memory and keeps native storage out of socket I/O.
    /// Exclude concurrent mutation, release and external cursor changes during the operation.</remarks>
    public virtual int GetBytes(int index, Socket destination, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        if (length == 0) return 0;
        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            GetBytesCore(index, bytes.AsSpan(0, length));
            int count = destination.Send(bytes.AsSpan(0, length), SocketFlags.None, out SocketError error);
            return SocketTransferCount(count, error);
        }
        finally { ArrayPool<byte>.Shared.Return(bytes); }
    }

    /**
     * Transfers the content of the specified source channel to this buffer
     * starting at the specified absolute {@code index}.
     * This method does not modify {@code readerIndex} or {@code writerIndex} of
     * this buffer.
     *
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
    /// <summary>Receives once into an absolute range without changing indices.</summary>
    /// <returns>The actual count; zero for stream EOF, WouldBlock or an empty request.</returns>
    /// <remarks>CLR: a closed socket throws. Composite storage also receives once;
    /// Java may read repeatedly across components. Staging prevents failed I/O from
    /// publishing input, although a later failed composite commit can change a prefix.
    /// The socket and buffer are borrowed; no reference count is acquired.</remarks>
    public virtual int SetBytes(int index, Socket source, int length)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureCanWrite();
        CheckIndex(index, length);
        if (length == 0) return 0;
        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            int count = source.Receive(bytes.AsSpan(0, length), SocketFlags.None, out SocketError error);
            count = SocketTransferCount(count, error);
            if (count != 0) SetBytesFromIoCore(index, bytes.AsSpan(0, count));
            return count;
        }
        finally { ArrayPool<byte>.Shared.Return(bytes); }
    }

    /**
     * Transfers this buffer's data to the specified stream starting at the
     * current {@code readerIndex}.
     *
     * @param length the maximum number of bytes to transfer
     *
     * @return the actual number of bytes written out to the specified channel
     *
     * @throws IndexOutOfBoundsException
     *         if {@code length} is greater than {@code this.readableBytes}
     * @throws IOException
     *         if the specified channel threw an exception during I/O
     */
    /// <summary>Sends readable bytes once and advances the reader by the actual successful count.</summary>
    /// <remarks>Failure leaves the reader unchanged, even if the peer received a prefix.</remarks>
    public virtual int ReadBytes(Socket destination, int length)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckReadableBytes(length);
        int count = GetBytes(_readerIndex, destination, length);
        _readerIndex += count;
        return count;
    }

    /**
     * Transfers the content of the specified channel to this buffer
     * starting at the current {@code writerIndex} and increases the
     * {@code writerIndex} by the number of the transferred bytes.
     * If {@code this.writableBytes} is less than {@code length}, {@link #ensureWritable(int)}
     * will be called in an attempt to expand capacity to accommodate.
     *
     * @param length the maximum number of bytes to transfer
     *
     * @return the actual number of bytes read in from the specified channel.
     *         {@code -1} if the specified channel is closed or reached EOF.
     *
     * @throws IOException
     *         if the specified channel threw an exception during I/O
     */
    /// <summary>Reserves requested space, receives once and advances the writer by the actual count.</summary>
    /// <remarks>Growth is retained on EOF, WouldBlock or I/O failure, as in Netty.</remarks>
    public virtual int WriteBytes(Socket source, int length)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureWritable(length);
        int count = SetBytes(_writerIndex, source, length);
        _writerIndex += count;
        return count;
    }

    /// <summary>Asynchronously sends one absolute snapshot without changing indices.</summary>
    /// <remarks>Uses Socket.SendAsync directly. The returned count can be shorter than
    /// requested; this method does not retry. Staging survives suspension; no native
    /// borrow does. Keep the borrowed socket/buffer alive and exclude concurrent mutation,
    /// release and external cursor changes until completion.</remarks>
    public virtual async ValueTask<int> GetBytesAsync(int index, Socket destination, int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckIndex(index, length);
        cancellationToken.ThrowIfCancellationRequested();
        if (length == 0) return 0;
        byte[] bytes = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            GetBytesCore(index, bytes.AsSpan(0, length));
            return await destination.SendAsync(bytes.AsMemory(0, length), SocketFlags.None, cancellationToken)
                .ConfigureAwait(false);
        }
        finally { ArrayPool<byte>.Shared.Return(bytes); }
    }

    /// <summary>Asynchronously receives once into an absolute range without changing indices.</summary>
    /// <returns>The actual count; zero for stream EOF or an empty request.</returns>
    /// <remarks>A thrown cancellation/failure publishes no staged input. A successful
    /// result commits despite late cancellation. Async socket I/O waits for readiness;
    /// it does not expose synchronous nonblocking WouldBlock. Borrowed lifetime and
    /// exclusion requirements are the same as GetBytesAsync(index, Socket, length).</remarks>
    public virtual async ValueTask<int> SetBytesAsync(int index, Socket source, int length,
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
            int count = await source.ReceiveAsync(bytes.AsMemory(0, length), SocketFlags.None, cancellationToken)
                .ConfigureAwait(false);
            if (count != 0)
            {
                EnsureCanWrite();
                CheckIndex(index, count);
                SetBytesFromIoCore(index, bytes.AsSpan(0, count));
            }
            return count;
        }
        finally { ArrayPool<byte>.Shared.Return(bytes); }
    }

    /// <summary>Asynchronously sends readable bytes once, advancing the reader by the returned count.</summary>
    /// <remarks>Thrown cancellation/failure leaves the reader unchanged. The socket
    /// may already have accepted a prefix. Exclude external reader changes until completion.</remarks>
    public virtual async ValueTask<int> ReadBytesAsync(Socket destination, int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        CheckReadableBytes(length);
        int count = await GetBytesAsync(_readerIndex, destination, length, cancellationToken).ConfigureAwait(false);
        _readerIndex += count;
        return count;
    }

    /// <summary>Reserves space, asynchronously receives once and advances by the actual count.</summary>
    /// <remarks>Validation precedes cancellation. Pre-cancellation prevents growth;
    /// started growth is not rolled back. Exclude external writer changes until completion.</remarks>
    public virtual async ValueTask<int> WriteBytesAsync(Socket source, int length,
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

    private static int SocketTransferCount(int count, SocketError error)
    {
        if (error == SocketError.WouldBlock) return 0;
        if (error != SocketError.Success) throw new SocketException((int)error);
        return count;
    }
}
