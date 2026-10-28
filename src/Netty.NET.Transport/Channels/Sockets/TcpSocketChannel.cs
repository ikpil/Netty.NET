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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;

namespace Netty.NET.Transport.Channels.Sockets;

/// <summary>A TCP stream channel using CLR asynchronous socket completions and explicit event-loop reentry.</summary>
/**
 * {@link io.netty.channel.socket.SocketChannel} which uses NIO selector based implementation.
 */
public class TcpSocketChannel : AbstractChannel
{
    protected readonly Socket Socket;
    private bool _reading;
    private bool _readRequested;
    private bool _inputShutdown;
    private bool _outputShutdown;
    public TcpSocketChannel() : this(new Socket(Socket.OSSupportsIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), null)
    { if (Socket.AddressFamily == AddressFamily.InterNetworkV6) Socket.DualMode = true; }
    public TcpSocketChannel(AddressFamily family) : this(new Socket(family, SocketType.Stream, ProtocolType.Tcp), null) { }
    internal TcpSocketChannel(Socket socket, IChannel parent) : base(parent, new ChannelMetadata(false))
    {
        Socket = socket;
        Socket.NoDelay = true;
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
        { if (Socket.Connected) Socket.Shutdown(SocketShutdown.Both); }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
        finally { Socket.Dispose(); }
    }
    protected override object FilterOutboundMessage(object message)
    {
        if (_outputShutdown)
            throw new IOException("Socket output is shut down.");
        return message is ByteBuf or IFileRegion ? message : throw new NotSupportedException("TCP writes require ByteBuf or IFileRegion.");
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
        if (_reading || _inputShutdown)
            return;
        _reading = true;
        _ = ReadLoopAsync();
    }
    private async Task ReadLoopAsync()
    {
        bool cycle = false;
        try
        {
            while (IsOpen && IsActive && !_inputShutdown)
            {
                ByteBuf buffer = null;
                bool admitted = false, again = false;
                try
                {
                    await OnLoopAsync(() =>
                    {
                        if (!IsOpen || _inputShutdown || (!Config.IsAutoRead() && !_readRequested))
                            return;
                        if (!cycle)
                        { ReceiveHandle.Reset(Config); cycle = true; }
                        buffer = ReceiveHandle.Allocate(Allocator);
                        ReceiveHandle.AttemptedBytesRead(buffer.WritableBytes);
                        _readRequested = false;
                        admitted = true;
                    }).ConfigureAwait(false);
                    if (!admitted)
                        break;
                    int read = await Socket.ReceiveAsync(buffer.AsMemory(buffer.WriterIndex, buffer.WritableBytes), SocketFlags.None).ConfigureAwait(false);
                    await OnLoopAsync(() =>
                    {
                        if (!IsOpen)
                            return;
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
                        again = IsOpen && !_inputShutdown && (Config.IsAutoRead() || _readRequested);
                    }).ConfigureAwait(false);
                }
                finally { buffer?.Release(); }
                if (!again)
                    break;
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
            { await OnLoopAsync(() => { _reading = false; if (IsOpen && !_inputShutdown && _readRequested) DoBeginRead(); }).ConfigureAwait(false); }
            catch { }
        }
    }
    public bool IsInputShutdown => _inputShutdown || !IsOpen;
    public bool IsOutputShutdown => _outputShutdown || !IsOpen;
    public Task ShutdownOutputAsync() => OnLoopAsync(() =>
    {
        if (_outputShutdown)
            return;
        Socket.Shutdown(SocketShutdown.Send);
        _outputShutdown = true;
        OutboundBuffer.Close(new IOException("Socket output is shut down."));
        Pipeline.FireUserEventTriggered(ChannelOutputShutdownEvent.INSTANCE);
    });
    public Task ShutdownInputAsync() => OnLoopAsync(() =>
    { if (!_inputShutdown) { Socket.Shutdown(SocketShutdown.Receive); _inputShutdown = true; } });
    public Task ShutdownAsync() => Task.WhenAll(ShutdownInputAsync(), ShutdownOutputAsync());
}
