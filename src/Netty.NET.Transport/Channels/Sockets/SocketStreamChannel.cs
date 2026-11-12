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
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Transport.Channels.Sockets;

public abstract class SocketStreamChannel : AbstractChannel, IDuplexChannel
{
    protected readonly Socket Socket;
    private bool _reading;
    private bool _readRequested;
    private volatile bool _inputShutdown;
    private bool _inputReadComplete;
    private int _gatheringWriteLimit;
    protected SocketStreamChannel(Socket socket, IChannel parent) : base(parent, new ChannelMetadata(false, 16))
    {
        Socket = socket ?? throw new ArgumentNullException(nameof(socket));
        SetActive(socket.Connected);
    }
    public override EndPoint LocalAddress => IsOpen ? Normalize(Socket.LocalEndPoint) : null;
    public override EndPoint RemoteAddress => IsOpen ? Normalize(Socket.RemoteEndPoint) : null;
    internal static EndPoint Normalize(EndPoint endpoint) => endpoint is IPEndPoint ip && ip.Address.IsIPv4MappedToIPv6 ? new IPEndPoint(ip.Address.MapToIPv4(), ip.Port) : endpoint;
    protected override IChannelConfig NewConfig() => new DefaultSocketChannelConfig(this, Socket);
    protected override Task DoBindAsync(EndPoint address, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); Socket.Bind(address); return Task.CompletedTask; }
    internal override bool UsesSocketCompletions => true;
    protected override async Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken cancellationToken)
    {
        if (IsActive)
            throw new SocketException((int)SocketError.IsConnected);
        if (local != null)
            Socket.Bind(local);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Schedule connect timeout.
        int milliseconds = Config.GetConnectTimeoutMillis();
        if (milliseconds > 0)
            timeout.CancelAfter(milliseconds);
        try
        { await ConnectSocketAsync(remote, timeout.Token).ConfigureAwait(false); }
        catch (Exception error)
        {
            // If the connect future is cancelled we also cancel the timeout and close the
            // underlying socket.
            try { await OnSocketLoopAsync(() => _ = CloseTransportAsync()).ConfigureAwait(false); }
            catch (RejectedExecutionException) when (EventLoop.IsShuttingDown()) { }
            if (error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                throw new ConnectTimeoutException("Connection timed out: " + remote);
            throw;
        }
        await OnSocketLoopAsync(() => { EnsureOpen(); SetActive(true); }).ConfigureAwait(false);
    }
    protected virtual ValueTask ConnectSocketAsync(EndPoint remote, CancellationToken cancellationToken) =>
        Socket.ConnectAsync(remote, cancellationToken);
    protected override void DoClose()
    {
        // Dispose aborts pending CLR socket operations. Shut down the TCP directions first so accepted
        // data and FIN retain the orderly close semantics of FileDescriptor-based Java socket close.
        try
        {
            // SO_LINGER=0 is an abortive close. Sending FIN first would change the peer's reset semantics.
            if (Socket.Connected && (Socket.AddressFamily == AddressFamily.Unix || Socket.LingerState is not { Enabled: true, LingerTime: 0 })) Socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
        finally { Socket.Dispose(); }
    }
    protected override object FilterOutboundMessage(object message)
    {
        if (OutboundBuffer.IsClosed)
            throw new IOException("Socket output is shut down.");
        return message is ByteBuf or IFileRegion ? message : throw new NotSupportedException("Stream writes require ByteBuf or IFileRegion.");
    }

    // Custom channels can override DoWrite; only the built-in socket types opt into batching.
    internal virtual bool SupportsGatheringWrites =>
        GetType() == typeof(TcpSocketChannel) || GetType() == typeof(DomainSocketChannel);

    internal void ResetGatheringWriteLimit() => Volatile.Write(ref _gatheringWriteLimit, 0);

    /**
     * Write objects to the OS.
     * @param in the collection which contains objects to write.
     * @return The value that should be decremented from the write quantum which starts at
     * {@link ChannelConfig#getWriteSpinCount()}. The typical use cases are as follows:
     * <ul>
     *     <li>0 - if no write was attempted. This is appropriate if an empty {@link ByteBuf} (or other empty content)
     * is encountered</li>
     *     <li>1 - if a single call to write data was made to the OS</li>
     *     <li>{@link ChannelUtils#WRITE_STATUS_SNDBUF_FULL} - if an attempt to write data was made to the OS, but no
     * data was accepted</li>
     * </ul>
     * @throws Exception if an I/O exception occurs during write.
     */
    internal override Task<int> DoWrite0(ChannelOutboundBuffer @in)
    {
        if (@in.IsClosed || !SupportsGatheringWrites)
            return null;
        object msg = @in.Current;
        if (msg == null)
        {
            // Directly return here so incompleteWrite(...) is not called.
            return Task.FromResult(0);
        }
        // CLR: null delegates non-ByteBuf/custom producers to their existing whole-message path.
        if (msg is not ByteBuf) return null;
        return DoWriteInternal(@in, msg);
    }

    private async Task<int> DoWriteInternal(ChannelOutboundBuffer @in, object msg)
    {
        var buf = (ByteBuf)msg;
        if (!buf.IsReadable)
        {
            @in.Remove();
            return 0;
        }

        // CLR Socket.SendAsync does not advance ByteBuf indices. Commit the returned byte count on the owner.
        int expectedWrittenBytes = buf.ReadableBytes;
        int localFlushedAmount;
        bool pending;
        buf.Retain();
        try
        {
            Task<int> operation = DoWriteBytes(buf);
            pending = !operation.IsCompleted;
            localFlushedAmount = await operation.ConfigureAwait(false);
            // A CLR asynchronous send waits for write readiness. A successful nonempty send cannot return zero.
            if (localFlushedAmount <= 0 || localFlushedAmount > expectedWrittenBytes)
                throw new IOException("Socket reported an invalid scalar write length.");
        }
        finally { ReferenceCountUtil.SafeRelease(buf); }

        await CompleteOutboundIoAsync(() =>
        {
            if (!IsOpen || @in.IsClosed) return false;
            buf.ReaderIndex += localFlushedAmount;
            @in.Progress(localFlushedAmount);
            if (IsOpen && !@in.IsClosed && !buf.IsReadable)
                @in.Remove();
            return true;
        }, pending).ConfigureAwait(false);
        return 1;
    }

    /**
     * Write bytes form the given {@link ByteBuf} to the underlying {@link java.nio.channels.Channel}.
     * @param buf           the {@link ByteBuf} from which the bytes should be written
     * @return amount       the amount of written bytes
     */
    protected virtual Task<int> DoWriteBytes(ByteBuf buf) =>
        Socket.SendAsync(buf.ReadableSequence.First, SocketFlags.None).AsTask();

    internal override Task<int> TryGatherWriteAsync()
    {
        if (OutboundBuffer.IsClosed || !SupportsGatheringWrites || OutboundBuffer.Current is not ByteBuf first ||
            (OutboundBuffer.Size == 1 && first.ReadableSequence.IsSingleSegment))
            return null;

        // A small vector bound is portable across supported socket backends.
        const int maxSegments = 16;
        int limit = Volatile.Read(ref _gatheringWriteLimit);
        if (limit == 0)
        {
            // Multiply by 2 to give some extra space in case the OS can process write data faster than we can provide.
            int initial = (int)Math.Clamp(Socket.SendBufferSize * 2L, 1, int.MaxValue);
            int previous = Interlocked.CompareExchange(ref _gatheringWriteLimit, initial, 0);
            limit = previous == 0 ? initial : previous;
        }
        List<ArraySegment<byte>> segments = null;
        List<ByteBuf> buffers = null;
        int attempted = 0;
        // Ensure the pending writes are made of ByteBufs only.
        OutboundBuffer.ForEachFlushedMessage(message =>
        {
            if (message is not ByteBuf buffer) return false;
            bool added = false;
            foreach (var memory in buffer.ReadableSequence)
            {
                // Zero length buffers are not added to nioBuffers by ChannelOutboundBuffer, so there is no need
                // to check if the total size of all the buffers is non-zero.
                if (memory.IsEmpty) continue;
                if (!MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment)) return false;
                if (!added)
                {
                    (buffers ??= new List<ByteBuf>(maxSegments)).Add(buffer);
                    added = true;
                }
                int length = Math.Min(segment.Count, limit - attempted);
                (segments ??= new List<ArraySegment<byte>>(maxSegments))
                    .Add(new ArraySegment<byte>(segment.Array, segment.Offset, length));
                attempted += length;
                if (segments.Count == maxSegments || attempted == limit) return false;
            }
            return true;
        });
        // Only one ByteBuf so use non-gathering write
        if (segments == null || segments.Count < 2) return null;
        return SendAsync();

        async Task<int> SendAsync()
        {
            int retained = 0;
            try
            {
                for (; retained < buffers.Count; retained++) buffers[retained].Retain();
                int written = await SendGatheredAsync(segments).ConfigureAwait(false);
                if (written <= 0 || written > attempted)
                    throw new IOException("Socket reported an invalid gathered write length.");
                AdjustGatheringWriteLimit(attempted, written, limit);
                return written;
            }
            finally
            {
                for (int i = 0; i < retained; i++) ReferenceCountUtil.SafeRelease(buffers[i]);
            }
        }
    }

    internal virtual Task<int> SendGatheredAsync(IList<ArraySegment<byte>> segments) =>
        Socket.SendAsync(segments, SocketFlags.None);

    private void AdjustGatheringWriteLimit(int attempted, int written, int previous)
    {
        // By default we track the SO_SNDBUF when ever it is explicitly set. However some OSes may dynamically change
        // SO_SNDBUF (and other characteristics that determine how much data can be written at once) so we should try
        // make a best effort to adjust as OS behavior changes.
        if (attempted == written)
        {
            int next = (int)Math.Min(attempted * 2L, int.MaxValue);
            if (next > previous) Interlocked.CompareExchange(ref _gatheringWriteLimit, next, previous);
        }
        else if (attempted > 4096 && written < attempted / 2)
            Interlocked.CompareExchange(ref _gatheringWriteLimit, attempted / 2, previous);
    }

    protected override async Task DoWrite(object msg)
    {
        if (msg is ByteBuf buf)
        {
            while (buf.ReadableBytes != 0)
            {
                // A composite buf has multiple borrowed segments and need not expose contiguous memory.
                int localFlushedAmount = await DoWriteBytes(buf).ConfigureAwait(false);
                if (localFlushedAmount == 0)
                    throw new IOException("Socket accepted zero bytes.");
                await OnSocketLoopAsync(() => { buf.ReaderIndex += localFlushedAmount; OutboundBuffer.Progress(localFlushedAmount); }).ConfigureAwait(false);
            }
            return;
        }
        var region = (IFileRegion)msg;
        if (region.Transferred() >= region.Count()) return;
        // Preserve virtual/custom FileRegion producers. Only the exact default region exposes a plain file range.
        if (SupportsNativeFileTransfer && region.GetType() == typeof(DefaultFileRegion) &&
            await TryWriteNativeFileRegionAsync((DefaultFileRegion)region).ConfigureAwait(false))
            return;
        // Portable synchronous FileRegion implementations are offloaded, never run on the event-loop worker.
        await Task.Run(async () =>
        {
            using var stream = new FileRegionSocketStream(Socket);
            while (region.Transferred() < region.Count())
            {
                // A custom region can advance its logical transfer counter by more than the bytes
                // written to the target (for example, a complete framed or encrypted source chunk).
                // The source contract uses transferred(), both as the position and the completion test.
                long count = region.TransferTo(stream, region.Transferred());
                if (count < 0 || count == 0 && region.Transferred() < region.Count())
                    throw new IOException("File region made no forward progress.");
                if (count > 0)
                    await OnSocketLoopAsync(() =>
                    {
                        if (!OutboundBuffer.IsClosed) OutboundBuffer.Progress(count);
                    }).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }
    internal virtual bool SupportsNativeFileTransfer => OperatingSystem.IsLinux() ||
        OperatingSystem.IsWindows() && Socket.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6;
    internal virtual Task<FileRegionSendResult> SendFilePacketAsync(FileStream file, long offset, int count) =>
        SocketFileRegionSender.SendAsync(Socket, file, offset, count);
    internal const int NativeFileChunkSize = 1024 * 1024;

    private async Task<bool> TryWriteNativeFileRegionAsync(DefaultFileRegion region)
    {
        // File open can block on storage. A supplied stream keeps its identity and mode; .NET 10 only
        // accepts asynchronous FileStreams in SendPacketsElement, so synchronous streams use TransferTo.
        FileStream file = await Task.Run(region.OpenForSocketTransfer).ConfigureAwait(false);
        if (!file.IsAsync || !file.CanRead) return false;
        for (;;)
        {
            Task<FileRegionSendResult> sending = null;
            int requested = 0;
            await OnSocketLoopAsync(() =>
            {
                EnsureOpen();
                if (OutboundBuffer.IsClosed) throw new IOException("Channel output is shut down.");
                long position = region.Transferred();
                long remaining = region.Count() - position;
                if (remaining <= 0) return;
                if (position > long.MaxValue - region.Position()) throw new IOException("File region offset exceeds Int64 range.");
                long offset = region.Position() + position;
                long available = RandomAccess.GetLength(file.SafeFileHandle) - offset;
                if (available <= 0)
                {
                    DefaultFileRegion.Validate(region, position);
                    throw new IOException("File region made no forward progress.");
                }
                requested = (int)Math.Min(Math.Min(remaining, available), NativeFileChunkSize);
                sending = SendFilePacketAsync(file, offset, requested);
            }).ConfigureAwait(false);
            if (sending == null) return true;

            FileRegionSendResult result;
            try { result = await sending.ConfigureAwait(false); }
            catch (PlatformNotSupportedException) { return false; }
            if (result.BytesTransferred < 0 || result.BytesTransferred > requested)
                throw new IOException("Socket reported an invalid file transfer length.");
            await OnSocketLoopAsync(() =>
            {
                region.RecordSocketTransfer(result.BytesTransferred);
                if (!OutboundBuffer.IsClosed && result.BytesTransferred > 0) OutboundBuffer.Progress(result.BytesTransferred);
            }).ConfigureAwait(false);

            if (result.Error != SocketError.Success)
            {
                // The file may have shrunk between the range check and the kernel call.
                if (result.Error == SocketError.InvalidArgument) DefaultFileRegion.Validate(region, region.Transferred());
                // A capability rejection before sending is safe to retry through the portable Stream producer.
                // Never resend a partially accepted range as if it had not reached the socket.
                if (result.BytesTransferred == 0 && result.Error is SocketError.OperationNotSupported or SocketError.ProtocolNotSupported or SocketError.InvalidArgument)
                    return false;
                throw new IOException("Socket file transfer failed.", new SocketException((int)result.Error));
            }
            if (result.BytesTransferred == 0)
            {
                DefaultFileRegion.Validate(region, region.Transferred());
                throw new IOException("File region made no forward progress.");
            }
        }
    }
    // NetworkStream rejects nonblocking sockets. Keep the caller's socket mode and consume every
    // Stream.Write span through partial sends, waiting for write readiness only on the file worker.
    private sealed class FileRegionSocketStream(Socket socket) : Stream
    {
        private bool _disposed;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !_disposed;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count)
        { ArgumentNullException.ThrowIfNull(buffer); Write(buffer.AsSpan(offset, count)); }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                while (!buffer.IsEmpty)
                {
                    int written = socket.Send(buffer, SocketFlags.None, out SocketError error);
                    if (written > 0) buffer = buffer[written..];
                    if (error == SocketError.WouldBlock)
                    {
                        if (!buffer.IsEmpty) socket.Poll(-1, SelectMode.SelectWrite);
                        continue;
                    }
                    if (error != SocketError.Success) throw new SocketException((int)error);
                    if (written == 0) throw new IOException("Socket accepted zero bytes.");
                }
            }
            catch (SocketException error) { throw new IOException("Socket file transfer failed.", error); }
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { _disposed = true; base.Dispose(disposing); }
    }
    protected override void DoBeginRead()
    {
        _readRequested = true;
        ResumeRead();
        if (_reading)
            return;
        if (_inputShutdown)
        {
            if (!IsRegistered || _inputReadComplete || !Config.TryGetOption(ChannelOption.ALLOW_HALF_CLOSURE, out bool half) || !half)
                return;
            _inputReadComplete = true;
            _readRequested = false;
            // CLR has no read-ready callback after EOF; complete the next logical read once on its owner.
            EventLoop.Execute(() =>
            {
                if (!IsOpen) return;
                ReceiveHandle.Reset(Config);
                ReceiveHandle.LastBytesRead(-1);
                ReceiveHandle.ReadComplete();
                Pipeline.FireChannelReadComplete();
                Pipeline.FireUserEventTriggered(ChannelInputShutdownReadComplete.INSTANCE);
            });
            return;
        }
        _reading = true;
        StartSocketWork(() => _ = ReadLoopAsync());
    }
    protected override void DoClearReadPending() => _readRequested = false;
    private async Task ReadLoopAsync()
    {
        bool cycle = false;
        try
        {
            while (IsOpen && IsActive && !_inputShutdown)
            {
                ByteBuf buffer = null;
                bool admitted = false, again = false, continuedBatch = false;
                try
                {
                    await OnSocketLoopAsync(() =>
                    {
                        if (!IsOpen || !IsRegistered || _inputShutdown || (!cycle && !Config.IsAutoRead() && !_readRequested))
                            return;
                        // One manual Read owns every available message admitted by this allocator batch.
                        // The initial native receive still obeys registration and AutoRead admission.
                        continuedBatch = cycle;
                        if (!cycle)
                        { ReceiveHandle.Reset(Config); cycle = true; }
                        buffer = ReceiveHandle.Allocate(Allocator);
                        ReceiveHandle.AttemptedBytesRead(buffer.WritableBytes);
                        admitted = true;
                    }).ConfigureAwait(false);
                    if (!admitted)
                        break;
                    int read = await ReceiveStreamAsync(buffer.AsMemory(buffer.WriterIndex, buffer.WritableBytes)).ConfigureAwait(false);
                    await DispatchReadAsync(() => continuedBatch || _readRequested || _inputShutdown, () =>
                    {
                        if (!IsOpen || _inputShutdown)
                            return;
                        _readRequested = false;
                        ReceiveHandle.LastBytesRead(read == 0 ? -1 : read);
                        if (read > 0)
                        {
                            buffer.WriterIndex += read;
                            ReceiveHandle.IncMessagesRead(1);
                            var message = buffer;
                            buffer = null;
                            Pipeline.FireChannelRead(message);
                        }
                        bool more = read > 0 && IsOpen && ReceiveHandle is IRecvByteBufAllocator.IExtendedHandle extended && extended.ContinueReading(() => Socket.Available > 0);
                        if (!more)
                        {
                            ReceiveHandle.ReadComplete();
                            Pipeline.FireChannelReadComplete();
                            cycle = false;
                        }
                        if (read == 0)
                        {
                            _inputShutdown = true;
                            if (Config.TryGetOption(ChannelOption.ALLOW_HALF_CLOSURE, out bool half) && half)
                                Pipeline.FireUserEventTriggered(ChannelInputShutdownEvent.INSTANCE);
                            else
                                _ = CloseTransportAsync();
                        }
                        again = IsOpen && !_inputShutdown && (more || Config.IsAutoRead() || _readRequested);
                    }).ConfigureAwait(false);
                }
                finally { buffer?.Release(); }
                if (!again)
                    break;
                // A synchronous CLR receive must yield after the configured read batch, just as
                // returning from the NIO read-ready callback lets the event loop process other work.
                if (!cycle)
                    await QueueSocketWorkAsync(() => { }).ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            try
            { await OnSocketLoopAsync(() => { if (IsOpen) { Pipeline.FireExceptionCaught(error); _ = CloseTransportAsync(); } }).ConfigureAwait(false); }
            catch { }
        }
        finally
        {
            try
            {
                await OnSocketLoopAsync(() =>
                {
                    _reading = false;
                    if (IsOpen && (!_inputShutdown ? _readRequested : !_inputReadComplete && (Config.IsAutoRead() || _readRequested))) DoBeginRead();
                }).ConfigureAwait(false);
            }
            catch { }
        }
    }
    private ValueTask<int> ReceiveStreamAsync(Memory<byte> buffer)
    {
        if (!OperatingSystem.IsWindows() || Socket.AddressFamily != AddressFamily.Unix)
            return Socket.ReceiveAsync(buffer, SocketFlags.None);
        return new ValueTask<int>(Task.Run(async () =>
        {
            // Windows AF_UNIX can leave overlapped ReceiveAsync pending after peer close even when
            // Poll reports readable and a synchronous receive returns EOF (also reproduced without Netty).
            // There is exactly one channel reader. Receive only after readiness, on a worker, so this
            // path neither blocks the event loop nor consumes one blocked thread per idle connection.
            // Bounded asynchronous polling trades idle wakeups/latency for reliable EOF on this backend.
            int delay = 1;
            while (IsOpen)
            {
                if (Socket.Poll(0, SelectMode.SelectRead))
                    return Socket.Receive(buffer.Span, SocketFlags.None);
                await Task.Delay(delay).ConfigureAwait(false);
                delay = Math.Min(delay * 2, 10);
            }
            throw new IOException("Channel is closed.");
        }));
    }
    public bool IsInputShutdown => _inputShutdown || !IsActive;
    public bool IsOutputShutdown => OutboundBuffer.IsClosed || !IsActive;
    public bool IsShutdown => IsInputShutdown && IsOutputShutdown;
    public Task ShutdownOutputAsync() => OnLoopAsync(() => ShutdownOutput(null));
    public Task ShutdownInputAsync() => OnLoopAsync(() =>
    {
        EnsureOpen();
        if (!_inputShutdown)
        {
            DoShutdownInput(); _inputShutdown = true; ResumeRead();
            if (!_reading && (Config.IsAutoRead() || _readRequested)) DoBeginRead();
        }
    });
    protected override void DoShutdownOutput() => Socket.Shutdown(SocketShutdown.Send);
    protected virtual void DoShutdownInput() => Socket.Shutdown(SocketShutdown.Receive);
    public async Task ShutdownAsync()
    {
        Exception outputFailure = null;
        try { await ShutdownOutputAsync().ConfigureAwait(false); }
        catch (Exception error) { outputFailure = error; }
        try { await ShutdownInputAsync().ConfigureAwait(false); }
        catch (Exception) when (outputFailure != null) { }
        if (outputFailure != null) ExceptionDispatchInfo.Capture(outputFailure).Throw();
    }
}
