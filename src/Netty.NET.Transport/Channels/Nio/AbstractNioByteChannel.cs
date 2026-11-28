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
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Transport.Channels.Internal.ChannelUtils;

namespace Netty.NET.Transport.Channels.Nio;

// CLR: NioSocketChannel and NioDomainSocketChannel each repeat the doReadBytes/doWriteBytes/doWriteFileRegion,
// doConnect/doFinishConnect and shutdown members over their own java.nio SocketChannel. Both CLR channels drive the
// same non-blocking System.Net.Sockets.Socket operations, so those members are implemented once here, in their
// common base; each subclass keeps its own gathering doWrite(ChannelOutboundBuffer).
/**
 * {@link AbstractNioChannel} base class for {@link Channel}s that operate on bytes.
 */
public abstract class AbstractNioByteChannel : AbstractNioChannel
{
    private static readonly ChannelMetadata METADATA = new ChannelMetadata(false, 16);
    private static readonly string EXPECTED_TYPES =
            " (expected: " + StringUtil.SimpleClassName(typeof(ByteBuf)) + ", " +
            StringUtil.SimpleClassName(typeof(IFileRegion)) + ')';

    private readonly Action _flushTask;
    private bool _inputClosedSeenErrorOnRead;
    // CLR: SocketChannel.socket().isInputShutdown() has no Socket query; the channel records shutdownInput().
    private volatile bool _inputShutdown;
    // CLR: NioSocketChannelConfig.maxBytesPerGatheringWrite, shared by the TCP and Unix domain configs.
    private int _maxBytesPerGatheringWrite = int.MaxValue;
    // CLR: bytes a non-DefaultFileRegion produced into its Stream target that the socket did not accept yet.
    private FileRegionStream _fileRegionTarget;

    /**
     * Create a new instance
     *
     * @param parent            the parent {@link Channel} by which this instance was created. May be {@code null}
     * @param ch                the underlying {@link SelectableChannel} on which it operates
     */
    protected AbstractNioByteChannel(IChannel parent, Socket ch) : base(parent, ch, NioIoOps.READ, METADATA)
    {
        _flushTask = () =>
        {
            // Calling flush0 directly to ensure we not try to flush messages that were added via write(...) in the
            // meantime.
            Flush0();
        };
        SetActive(ch.Connected);
        CalculateMaxBytesPerGatheringWrite();
    }

    protected override EndPoint LocalAddress0()
    {
        return Normalize(JavaChannel().LocalEndPoint);
    }

    protected override EndPoint RemoteAddress0()
    {
        return Normalize(JavaChannel().RemoteEndPoint);
    }

    protected override IChannelConfig NewConfig()
    {
        return new DefaultSocketChannelConfig(this, JavaChannel());
    }

    /**
     * Shutdown the input side of the channel.
     */
    public Task ShutdownInputAsync()
    {
        return OnLoopAsync(() =>
        {
            EnsureOpen();

            if (!_inputShutdown)
            {
                DoShutdownInput();
                _inputShutdown = true;
            }
        });
    }

    // CLR: NioSocketChannel and NioDomainSocketChannel both return isInputShutdown().
    protected virtual bool IsInputShutdown0()
    {
        return IsInputShutdown;
    }

    protected override AbstractNioUnsafe NewUnsafe()
    {
        return new NioByteUnsafe(this);
    }

    internal bool ShouldBreakReadReady(IChannelConfig config)
    {
        return IsInputShutdown0() && (_inputClosedSeenErrorOnRead || !IsAllowHalfClosure(config));
    }

    private static bool IsAllowHalfClosure(IChannelConfig config)
    {
        return config is ISocketChannelConfig socketChannelConfig &&
                socketChannelConfig.IsAllowHalfClosure();
    }

    protected class NioByteUnsafe : AbstractNioUnsafe
    {
        private readonly AbstractNioByteChannel _channel;

        protected internal NioByteUnsafe(AbstractNioByteChannel channel) : base(channel)
        {
            _channel = channel;
        }

        private void CloseOnRead(IChannelPipeline pipeline)
        {
            if (!_channel.IsInputShutdown0())
            {
                if (IsAllowHalfClosure(_channel.Config))
                {
                    _ = _channel.ShutdownInputAsync();
                    pipeline.FireUserEventTriggered(ChannelInputShutdownEvent.INSTANCE);
                }
                else
                {
                    _ = _channel.Close();
                }
            }
            else if (!_channel._inputClosedSeenErrorOnRead)
            {
                _channel._inputClosedSeenErrorOnRead = true;
                pipeline.FireUserEventTriggered(ChannelInputShutdownReadComplete.INSTANCE);
            }
        }

        private void HandleReadException(IChannelPipeline pipeline, ByteBuf byteBuf, Exception cause, bool close,
                IRecvByteBufAllocator.IHandle allocHandle)
        {
            if (byteBuf != null)
            {
                if (byteBuf.IsReadable())
                {
                    _channel.ReadPending = false;

                    try
                    {
                        pipeline.FireChannelRead(byteBuf);
                    }
                    catch (Exception)
                    {
                        // CLR: Exception has no addSuppressed; the read failure stays the reported cause.
                    }
                }
                else
                {
                    byteBuf.Release();
                }
            }

            allocHandle.ReadComplete();
            pipeline.FireChannelReadComplete();
            pipeline.FireExceptionCaught(cause);

            // If oom will close the read event, release connection.
            // See https://github.com/netty/netty/issues/10434
            // CLR: a SocketException is not an IOException, so both close on read.
            if (close ||
                    cause is OutOfMemoryException ||
                    cause is LeakPresenceDetector.AllocationProhibitedException ||
                    cause is IOException || cause is SocketException)
            {
                CloseOnRead(pipeline);
            }
        }

        public sealed override void Read()
        {
            IChannelConfig config = _channel.Config;
            if (_channel.ShouldBreakReadReady(config))
            {
                _channel.ClearReadPending();
                return;
            }

            IChannelPipeline pipeline = _channel.Pipeline;
            IByteBufAllocator allocator = config.GetAllocator();
            IRecvByteBufAllocator.IHandle allocHandle = _channel.RecvBufAllocHandle();
            allocHandle.Reset(config);

            ByteBuf byteBuf = null;
            bool close = false;

            try
            {
                do
                {
                    byteBuf = allocHandle.Allocate(allocator);
                    allocHandle.LastBytesRead(_channel.DoReadBytes(byteBuf));

                    if (allocHandle.LastBytesRead() <= 0)
                    {
                        // nothing was read. release the buffer.
                        byteBuf.Release();
                        byteBuf = null;
                        close = allocHandle.LastBytesRead() < 0;
                        if (close)
                        {
                            // There is nothing left to read as we received an EOF.
                            _channel.ReadPending = false;
                        }

                        break;
                    }

                    allocHandle.IncMessagesRead(1);
                    _channel.ReadPending = false;
                    pipeline.FireChannelRead(byteBuf);
                    byteBuf = null;
                } while (allocHandle.ContinueReading());

                allocHandle.ReadComplete();
                pipeline.FireChannelReadComplete();

                if (close)
                {
                    CloseOnRead(pipeline);
                }
            }
            catch (Exception t)
            {
                HandleReadException(pipeline, byteBuf, t, close, allocHandle);
            }
            finally
            {
                // Check if there is a readPending which was not processed yet.
                // This could be for two reasons:
                // * The user called Channel.read() or ChannelHandlerContext.read() in channelRead(...) method
                // * The user called Channel.read() or ChannelHandlerContext.read() in channelReadComplete(...) method
                //
                // See https://github.com/netty/netty/issues/2254
                if (!_channel.ReadPending && !config.IsAutoRead())
                {
                    RemoveReadOp();
                }
            }
        }
    }

    // CLR: Winsock stops reporting a socket whose receive side was shut down as readable, while the JDK keeps
    // reporting the EOF that read() turns into ChannelInputShutdownReadComplete. A read requested after
    // shutdownInput() therefore runs once on the event loop instead of waiting for a selection that never comes.
    // https://learn.microsoft.com/windows/win32/api/winsock/nf-winsock-shutdown
    protected override void DoBeginRead()
    {
        base.DoBeginRead();

        if (ReadPending && _inputShutdown && !_inputClosedSeenErrorOnRead && IsAllowHalfClosure(Config))
        {
            EventLoop.Execute(() =>
            {
                if (IsOpen && IsRegistered && ReadPending && !_inputClosedSeenErrorOnRead)
                {
                    Unsafe().Read();
                }
            });
        }
    }

    /**
     * Write objects to the OS.
     * @param in the collection which contains objects to write.
     * @return The value that should be decremented from the write quantum which starts at
     * {@link ChannelConfig#getWriteSpinCount()}. The typical use cases are as follows:
     * <ul>
     *     <li>0 - if no write was attempted. This is appropriate if an empty {@link ByteBuf} (or other empty content)
     *     is encountered</li>
     *     <li>1 - if a single call to write data was made to the OS</li>
     *     <li>{@link ChannelUtils#WRITE_STATUS_SNDBUF_FULL} - if an attempt to write data was made to the OS, but no
     *     data was accepted</li>
     * </ul>
     * @throws Exception if an I/O exception occurs during write.
     */
    protected int DoWrite0(ChannelOutboundBuffer @in)
    {
        object msg = @in.Current;
        if (msg == null)
        {
            // Directly return here so incompleteWrite(...) is not called.
            return 0;
        }

        return DoWriteInternal(@in, @in.Current);
    }

    private int DoWriteInternal(ChannelOutboundBuffer @in, object msg)
    {
        if (msg is ByteBuf buf)
        {
            if (!buf.IsReadable())
            {
                @in.Remove();
                return 0;
            }

            int localFlushedAmount = DoWriteBytes(buf);
            if (localFlushedAmount > 0)
            {
                @in.Progress(localFlushedAmount);

                if (!buf.IsReadable())
                {
                    @in.Remove();
                }

                return 1;
            }
        }
        else if (msg is IFileRegion region)
        {
            // CLR: bytes a custom region already produced are flushed before the region counts as transferred.
            if (region.Transferred() >= region.Count() && !HasPendingFileRegionBytes())
            {
                @in.Remove();
                return 0;
            }

            long localFlushedAmount = DoWriteFileRegion(region);
            if (localFlushedAmount > 0)
            {
                @in.Progress(localFlushedAmount);

                if (region.Transferred() >= region.Count() && !HasPendingFileRegionBytes())
                {
                    @in.Remove();
                }

                return 1;
            }
        }
        else
        {
            // Should not reach here.
            // CLR: InvalidOperationException replaces java.lang.Error.
            throw new InvalidOperationException("Unexpected message type: " + StringUtil.ClassName(msg));
        }

        return WRITE_STATUS_SNDBUF_FULL;
    }

    protected override void DoWrite(ChannelOutboundBuffer @in)
    {
        int writeSpinCount = Config.GetWriteSpinCount();

        do
        {
            object msg = @in.Current;
            if (msg == null)
            {
                // Wrote all messages.
                ClearOpWrite();
                // Directly return here so incompleteWrite(...) is not called.
                return;
            }

            writeSpinCount -= DoWriteInternal(@in, msg);
        } while (writeSpinCount > 0);

        IncompleteWrite(writeSpinCount < 0);
    }

    // CLR: ByteBuf storage is managed, so heap buffers are not copied into direct buffers.
    protected override object FilterOutboundMessage(object msg)
    {
        if (msg is ByteBuf)
        {
            return msg;
        }

        if (msg is IFileRegion)
        {
            return msg;
        }

        throw new NotSupportedException(
                "unsupported message type: " + StringUtil.SimpleClassName(msg) + EXPECTED_TYPES);
    }

    protected void IncompleteWrite(bool setOpWrite)
    {
        // Did not write completely.
        if (setOpWrite)
        {
            SetOpWrite();
        }
        else
        {
            // It is possible that we have set the write OP, woken up by NIO because the socket is writable, and then
            // use our write quantum. In this case we no longer want to set the write OP because the socket is still
            // writable (as far as we know). We will find out next time we attempt to write if the socket is writable
            // and set the write OP if necessary.
            ClearOpWrite();

            // Schedule flush again later so other tasks can be picked up in the meantime
            EventLoop.Execute(_flushTask);
        }
    }

    /**
     * Write a {@link FileRegion}
     *
     * @param region        the {@link FileRegion} from which the bytes should be written
     * @return amount       the amount of written bytes
     */
    // CLR: FileRegion.transferTo(WritableByteChannel, long) accepts partial writes. A DefaultFileRegion transfers
    // one positional file chunk with a non-blocking send; any other region writes into a Stream, which has no partial
    // write result, so the bytes the socket does not accept are kept and sent first on the next attempt (the
    // io_uring transport queues non-DefaultFileRegion chunks the same way).
    protected virtual long DoWriteFileRegion(IFileRegion region)
    {
        if (HasPendingFileRegionBytes())
        {
            return _fileRegionTarget.FlushPending();
        }

        long position = region.Transferred();
        if (region is DefaultFileRegion defaultFileRegion && region.GetType() == typeof(DefaultFileRegion))
        {
            return defaultFileRegion.TransferTo(JavaChannel(), position);
        }

        FileRegionStream target = _fileRegionTarget ??= new FileRegionStream(this);
        long written = region.TransferTo(target, position);
        return written > 0 ? target.FlushPending() : written;
    }

    private bool HasPendingFileRegionBytes()
    {
        return _fileRegionTarget != null && _fileRegionTarget.HasPending;
    }

    /**
     * Read bytes into the given {@link ByteBuf} and return the amount.
     */
    // CLR: ByteBuf.WriteBytes(Socket, int) reports EOF as 0, like WouldBlock, so the channel receives directly into
    // the writable range to keep the Java -1 EOF result of byteBuf.writeBytes(javaChannel(), ...).
    // https://learn.microsoft.com/dotnet/api/system.net.sockets.socket.receive
    protected virtual int DoReadBytes(ByteBuf byteBuf)
    {
        IRecvByteBufAllocator.IHandle allocHandle = RecvBufAllocHandle();
        allocHandle.AttemptedBytesRead(byteBuf.WritableBytes);
        int attempted = allocHandle.AttemptedBytesRead();
        if (_inputShutdown)
        {
            return -1;
        }

        int read = JavaChannel().Receive(byteBuf.AsSpan(byteBuf.WriterIndex, attempted), SocketFlags.None,
            out SocketError error);

        switch (error)
        {
            case SocketError.Success:
                break;
            case SocketError.WouldBlock:
                return 0;
            // Winsock reports WSAESHUTDOWN where the JDK returns EOF for a shut down receive side.
            case SocketError.Shutdown:
                return -1;
            default:
                throw new SocketException((int)error);
        }

        if (read == 0)
        {
            return -1;
        }

        byteBuf.WriterIndex += read;
        return read;
    }

    /**
     * Write bytes form the given {@link ByteBuf} to the underlying {@link java.nio.channels.Channel}.
     * @param buf           the {@link ByteBuf} from which the bytes should be written
     * @return amount       the amount of written bytes
     */
    protected virtual int DoWriteBytes(ByteBuf buf)
    {
        int expectedWrittenBytes = buf.ReadableBytes;
        return buf.ReadBytes(JavaChannel(), expectedWrittenBytes);
    }

    protected void SetOpWrite()
    {
        IIoRegistration registration = _registration;
        // Check first if the key is still valid as it may be canceled as part of the deregistration
        // from the EventLoop
        // See https://github.com/netty/netty/issues/2104
        // CLR: a deregistered channel has no registration, which the JDK reports as an invalid key.
        if (registration == null || !registration.IsValid())
        {
            return;
        }

        AddAndSubmit(NioIoOps.WRITE);
    }

    protected void ClearOpWrite()
    {
        IIoRegistration registration = _registration;
        // Check first if the key is still valid as it may be canceled as part of the deregistration
        // from the EventLoop
        // See https://github.com/netty/netty/issues/2104
        // CLR: a deregistered channel has no registration, which the JDK reports as an invalid key.
        if (registration == null || !registration.IsValid())
        {
            return;
        }

        RemoveAndSubmit(NioIoOps.WRITE);
    }

    // ---- NioSocketChannel / NioDomainSocketChannel members shared by both CLR stream channels ----

    internal int GetMaxBytesPerGatheringWrite()
    {
        return Volatile.Read(ref _maxBytesPerGatheringWrite);
    }

    internal void SetMaxBytesPerGatheringWrite(int maxBytesPerGatheringWrite)
    {
        Volatile.Write(ref _maxBytesPerGatheringWrite, maxBytesPerGatheringWrite);
    }

    internal void CalculateMaxBytesPerGatheringWrite()
    {
        // Multiply by 2 to give some extra space in case the OS can process write data faster than we can provide.
        int newSendBufferSize = unchecked(JavaChannel().SendBufferSize << 1);
        if (newSendBufferSize > 0)
        {
            SetMaxBytesPerGatheringWrite(newSendBufferSize);
        }
    }

    protected void AdjustMaxBytesPerGatheringWrite(int attempted, int written, int oldMaxBytesPerGatheringWrite)
    {
        // By default we track the SO_SNDBUF when ever it is explicitly set. However some OSes may dynamically change
        // SO_SNDBUF (and other characteristics that determine how much data can be written at once) so we should try
        // make a best effort to adjust as OS behavior changes.
        if (attempted == written)
        {
            if (unchecked(attempted << 1) > oldMaxBytesPerGatheringWrite)
            {
                SetMaxBytesPerGatheringWrite(unchecked(attempted << 1));
            }
        }
        else if (attempted > MAX_BYTES_PER_GATHERING_WRITE_ATTEMPTED_LOW_THRESHOLD && written < attempted >>> 1)
        {
            SetMaxBytesPerGatheringWrite(attempted >>> 1);
        }
    }

    /// <summary>The non-blocking SocketChannel.write(ByteBuffer) counterpart; WouldBlock writes zero bytes.</summary>
    internal virtual int WriteSocket(ReadOnlyMemory<byte> buffer)
    {
        int written = MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> segment)
            ? JavaChannel().Send(segment.Array, segment.Offset, segment.Count, SocketFlags.None, out SocketError error)
            : JavaChannel().Send(buffer.Span, SocketFlags.None, out error);
        return SocketWriteResult(written, error);
    }

    private readonly List<ArraySegment<byte>> _gatheringSegments = new();

    /// <summary>The non-blocking gathering SocketChannel.write(ByteBuffer[], int, int) counterpart.</summary>
    // CLR: Socket.Send(IList<ArraySegment<byte>>) gathers array-backed memory; other memory is staged into one pooled
    // array so a single send call still covers the vector.
    // https://learn.microsoft.com/dotnet/api/system.net.sockets.socket.send
    internal virtual long WriteSocket(ReadOnlyMemory<byte>[] buffers, int offset, int length)
    {
        List<ArraySegment<byte>> segments = _gatheringSegments;
        segments.Clear();
        bool arrays = true;
        long total = 0;

        for (int i = offset; i < offset + length; i++)
        {
            if (!MemoryMarshal.TryGetArray(buffers[i], out ArraySegment<byte> segment))
            {
                arrays = false;
            }

            segments.Add(segment);
            total += buffers[i].Length;
        }

        try
        {
            if (arrays)
            {
                int written = JavaChannel().Send(segments, SocketFlags.None, out SocketError error);
                return SocketWriteResult(written, error);
            }

            byte[] staging = ArrayPool<byte>.Shared.Rent((int)total);

            try
            {
                int position = 0;

                for (int i = offset; i < offset + length; i++)
                {
                    buffers[i].Span.CopyTo(staging.AsSpan(position));
                    position += buffers[i].Length;
                }

                int written = JavaChannel().Send(staging, 0, position, SocketFlags.None, out SocketError error);
                return SocketWriteResult(written, error);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(staging);
            }
        }
        finally
        {
            segments.Clear();
        }
    }

    // Non-blocking send of FileRegion bytes; WouldBlock writes zero bytes.
    private int WriteFileBytes(ReadOnlySpan<byte> buffer)
    {
        int written = JavaChannel().Send(buffer, SocketFlags.None, out SocketError error);
        return SocketWriteResult(written, error);
    }

    private static int SocketWriteResult(int written, SocketError error)
    {
        return error switch
        {
            SocketError.Success => written,
            SocketError.WouldBlock => 0,
            _ => throw new SocketException((int)error),
        };
    }

    protected void DoBind0(EndPoint localAddress)
    {
        Bind(JavaChannel(), localAddress);
    }

    // CLR: SocketUtils.bind(SocketChannel, SocketAddress), kept apart as Connect is so that NioSocketChannel can record
    // the bound address as SocketChannelImpl.bind does.
    internal virtual void Bind(Socket socket, EndPoint localAddress)
    {
        socket.Bind(localAddress);
    }

    protected override Task DoBindAsync(EndPoint address, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DoBind0(address);
        return Task.CompletedTask;
    }

    protected override bool DoConnect(EndPoint remoteAddress, EndPoint localAddress)
    {
        // CLR: SocketError.IsConnected stands for AlreadyConnectedException.
        if (IsActive)
        {
            throw new SocketException((int)SocketError.IsConnected);
        }

        if (localAddress != null)
        {
            DoBind0(localAddress);
        }

        bool success = false;

        try
        {
            bool connected = Connect(JavaChannel(), remoteAddress);
            if (!connected)
            {
                AddAndSubmit(NioIoOps.CONNECT);
            }
            else
            {
                SetActive(true);
            }

            success = true;
            return connected;
        }
        finally
        {
            if (!success)
            {
                DoClose();
            }
        }
    }

    // CLR: SocketUtils.connect(SocketChannel, SocketAddress). A non-blocking Socket.Connect reports a pending
    // connect as WouldBlock (InProgress on some platforms) where SocketChannel.connect returns false.
    // https://learn.microsoft.com/dotnet/api/system.net.sockets.socket.connect
    internal virtual bool Connect(Socket socket, EndPoint remoteAddress)
    {
        try
        {
            socket.Connect(remoteAddress);
            return true;
        }
        catch (SocketException e) when (e.SocketErrorCode is SocketError.WouldBlock or SocketError.InProgress)
        {
            return false;
        }
    }

    // CLR: SocketChannel.finishConnect() throws the pending connect's failure. For a selected non-blocking
    // Socket.Connect the failure is the SO_ERROR value, and Socket.Connected completes the connected state.
    // https://learn.microsoft.com/windows/win32/winsock/sol-socket-socket-options
    protected override void DoFinishConnect()
    {
        int error = (int)JavaChannel().GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error);
        if (error != 0)
        {
            throw new SocketException(error);
        }
        if (!JavaChannel().Connected)
        {
            throw new NotSupportedException("finishConnect is not supported for " + GetType().FullName);
        }

        SetActive(true);
    }

    protected override void DoClose()
    {
        base.DoClose();

        // Dispose aborts pending CLR socket operations. Shut down the TCP directions first so accepted
        // data and FIN retain the orderly close semantics of FileDescriptor-based Java socket close.
        try
        {
            // SO_LINGER=0 is an abortive close. Sending FIN first would change the peer's reset semantics.
            if (JavaChannel().Connected && (JavaChannel().AddressFamily == AddressFamily.Unix || JavaChannel().LingerState is not { Enabled: true, LingerTime: 0 }))
            {
                JavaChannel().Shutdown(SocketShutdown.Both);
            }
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            JavaChannel().Dispose();
            _fileRegionTarget?.Clear();
        }
    }

    public bool IsInputShutdown => _inputShutdown || !IsActive;
    public bool IsOutputShutdown => OutboundBuffer.IsClosed || !IsActive;
    public bool IsShutdown => IsInputShutdown && IsOutputShutdown;
    public Task ShutdownOutputAsync()
    {
        return OnLoopAsync(() => ShutdownOutput(null));
    }

    protected override void DoShutdownOutput()
    {
        JavaChannel().Shutdown(SocketShutdown.Send);
    }

    protected virtual void DoShutdownInput()
    {
        JavaChannel().Shutdown(SocketShutdown.Receive);
    }

    public async Task ShutdownAsync()
    {
        Exception outputFailure = null;

        try
        {
            await ShutdownOutputAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            outputFailure = error;
        }

        try
        {
            await ShutdownInputAsync().ConfigureAwait(false);
        }
        catch (Exception) when (outputFailure != null)
        {
        }

        if (outputFailure != null)
        {
            ExceptionDispatchInfo.Capture(outputFailure).Throw();
        }
    }

    // CLR: the Stream target of a non-DefaultFileRegion. Each write is offered to the non-blocking socket; what the
    // socket does not accept is kept and sent before any later data, so Stream.Write never needs a partial result.
    private sealed class FileRegionStream(AbstractNioByteChannel channel) : Stream
    {
        private readonly Queue<byte[]> _pending = new();
        private int _pendingOffset;
        private long _accepted;

        internal bool HasPending => _pending.Count != 0;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush()
        {
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (!HasPending)
            {
                int written = channel.WriteFileBytes(buffer);
                _accepted += written;
                buffer = buffer[written..];
            }

            if (!buffer.IsEmpty)
            {
                _pending.Enqueue(buffer.ToArray());
            }
        }

        // Returns the bytes accepted by the socket since the last call and sends what is still pending.
        internal long FlushPending()
        {
            while (_pending.Count != 0)
            {
                byte[] head = _pending.Peek();
                int written = channel.WriteFileBytes(head.AsSpan(_pendingOffset));
                _accepted += written;
                _pendingOffset += written;
                if (_pendingOffset < head.Length)
                {
                    break;
                }

                _pending.Dequeue();
                _pendingOffset = 0;
            }

            long accepted = _accepted;
            _accepted = 0;
            return accepted;
        }

        internal void Clear()
        {
            _pending.Clear();
            _pendingOffset = 0;
            _accepted = 0;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
    }
}
