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
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Transport.Channels.Sockets;

public abstract class SocketStreamChannel : AbstractChannel
{
    protected readonly Socket Socket;
    private bool _reading;
    private bool _readRequested;
    private volatile bool _inputShutdown;
    private volatile bool _outputShutdown;
    private bool _inputReadComplete;
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
    protected override async Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken cancellationToken)
    {
        if (local != null)
            Socket.Bind(local);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        int milliseconds = Config.GetConnectTimeoutMillis();
        if (milliseconds > 0)
            timeout.CancelAfter(milliseconds);
        try
        { await Socket.ConnectAsync(remote, timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { await OnLoopAsync(() => _ = CloseTransportAsync()).ConfigureAwait(false); throw new ConnectTimeoutException("Connection timed out: " + remote); }
        catch { await OnLoopAsync(() => _ = CloseTransportAsync()).ConfigureAwait(false); throw; }
        await OnLoopAsync(() => { EnsureOpen(); SetActive(true); }).ConfigureAwait(false);
    }
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
        if (_outputShutdown)
            throw new IOException("Socket output is shut down.");
        return message is ByteBuf or IFileRegion ? message : throw new NotSupportedException("Stream writes require ByteBuf or IFileRegion.");
    }
    protected override async Task DoWriteAsync(object message)
    {
        if (message is ByteBuf buffer)
        {
            while (buffer.ReadableBytes != 0)
            {
                // A composite buffer has multiple borrowed segments and need not expose contiguous memory.
                int sent = await Socket.SendAsync(buffer.ReadableSequence.First, SocketFlags.None).ConfigureAwait(false);
                if (sent == 0)
                    throw new IOException("Socket accepted zero bytes.");
                await OnLoopAsync(() => { buffer.ReaderIndex += sent; OutboundBuffer.Progress(sent); }).ConfigureAwait(false);
            }
            return;
        }
        var region = (IFileRegion)message;
        // Portable synchronous FileRegion implementations are offloaded, never run on the event-loop worker.
        await Task.Run(() =>
        {
            using var stream = new NetworkStream(Socket, ownsSocket: false);
            long offset = 0;
            while (offset < region.Count())
            {
                long count = region.TransferTo(stream, offset);
                if (count == 0)
                    throw new IOException("File region made no forward progress.");
                offset += count;
            }
        }).ConfigureAwait(false);
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
        EventLoop.Execute(() => _ = ReadLoopAsync());
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
                    await OnLoopAsync(() =>
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
                    int read = await Socket.ReceiveAsync(buffer.AsMemory(buffer.WriterIndex, buffer.WritableBytes), SocketFlags.None).ConfigureAwait(false);
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
                    await EventLoop.SubmitAsync(() => { }).ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            try
            { await OnLoopAsync(() => { if (IsOpen) { Pipeline.FireExceptionCaught(error); _ = CloseTransportAsync(); } }).ConfigureAwait(false); }
            catch { }
        }
        finally
        {
            try
            {
                await OnLoopAsync(() =>
                {
                    _reading = false;
                    if (IsOpen && (!_inputShutdown ? _readRequested : !_inputReadComplete && (Config.IsAutoRead() || _readRequested))) DoBeginRead();
                }).ConfigureAwait(false);
            }
            catch { }
        }
    }
    public bool IsInputShutdown => _inputShutdown || !IsActive;
    public bool IsOutputShutdown => _outputShutdown || !IsActive;
    protected override void HandleWriteError(Exception error)
    {
        if (OutboundBuffer.IsClosed) return;
        // Java SocketException is an IOException; the CLR SocketException hierarchy is different.
        if (Config.IsAutoClose() && error is IOException or SocketException)
        {
            CloseOnWriteError(error);
            return;
        }
        try { ShutdownOutput(error); }
        catch (Exception shutdownFailure) { Pipeline.FireExceptionCaught(shutdownFailure); }
    }
    public Task ShutdownOutputAsync() => OnLoopAsync(() => ShutdownOutput(null));
    private void ShutdownOutput(Exception cause)
    {
        EnsureOpen();
        if (_outputShutdown) throw new IOException("Socket output is shut down.");
        // Disallow adding any messages and flushes to outboundBuffer.
        _outputShutdown = true;
        // When a side enables SO_LINGER and calls showdownOutput(...) to start TCP half-closure
        // we can not call doDeregister here because we should ensure this side in fin_wait2 state
        // can still receive and process the data which is send by another side in the close_wait state。
        // See https://github.com/netty/netty/issues/11981
        try
        {
            // The shutdown function does not block regardless of the SO_LINGER setting on the socket
            // so we don't need to use GlobalEventExecutor to execute the shutdown
            DoShutdownOutput();
        }
        finally
        {
            OutboundBuffer.Close(cause == null ? new ChannelOutputShutdownException("Channel output shutdown")
                : new ChannelOutputShutdownException("Channel output shutdown", cause));
            Pipeline.FireUserEventTriggered(ChannelOutputShutdownEvent.INSTANCE);
        }
    }
    public Task ShutdownInputAsync() => OnLoopAsync(() =>
    {
        EnsureOpen();
        if (!_inputShutdown)
        {
            DoShutdownInput(); _inputShutdown = true; ResumeRead();
            if (!_reading && (Config.IsAutoRead() || _readRequested)) DoBeginRead();
        }
    });
    protected virtual void DoShutdownOutput() => Socket.Shutdown(SocketShutdown.Send);
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
