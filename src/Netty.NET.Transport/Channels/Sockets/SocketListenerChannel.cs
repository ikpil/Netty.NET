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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Transport.Channels.Sockets;

public abstract class SocketListenerChannel : AbstractChannel, IServerChannel
{
    private readonly Socket _socket;
    private bool _accepting;
    private bool _readRequested;
    protected Socket ListenerSocket => _socket;
    protected SocketListenerChannel(Socket socket) : base(null, new ChannelMetadata(false, 16))
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        // Check if we already have a local address bound.
        // BCL Socket.Bind and Listen are separate operations.
        SetActive(socket.IsBound && (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.AcceptConnection) != 0);
    }
    protected abstract IChannel CreateChild(Socket socket);
    public override EndPoint LocalAddress => IsOpen ? SocketStreamChannel.Normalize(_socket.LocalEndPoint) : null;
    public override EndPoint RemoteAddress => null;
    protected override IChannelConfig NewConfig() => new DefaultSocketChannelConfig(this, _socket);
    protected override Task DoBindAsync(EndPoint address, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _socket.Bind(address);
        if (OperatingSystem.IsLinux() && _socket.ProtocolType == ProtocolType.Tcp &&
            Config.TryGetOption(ChannelOption.TCP_FASTOPEN, out int fastOpen) && fastOpen > 0)
        {
            try { LinuxSocketOptions.SetInt(_socket, (int)SocketOptionLevel.Tcp, LinuxSocketOptions.TcpFastOpen, fastOpen); }
            catch (SocketException error) when (error.SocketErrorCode is SocketError.ProtocolOption or SocketError.OperationNotSupported)
            {
                // The source only applies the configured queue on a supporting kernel.
            }
        }
        Config.TryGetOption(ChannelOption.SO_BACKLOG, out int backlog);
        _socket.Listen(backlog);
        SetActive(true);
        return Task.CompletedTask;
    }
    protected override Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken cancellationToken) => Task.FromException(new NotSupportedException("A server channel cannot connect."));
    protected override void DoClose()
    {
        // Obtain the localAddress before we close the channel so it will not return null if we did not retrieve
        // it before.
        // For a managed domain socket, Socket.Bind already records the path that Dispose cleans up.
        _socket.Dispose();
    }
    protected override object FilterOutboundMessage(object message) => throw new NotSupportedException("A server channel cannot write.");
    protected override Task DoWriteAsync(object message) => Task.FromException(new NotSupportedException());
    protected override void DoBeginRead()
    {
        _readRequested = true;
        ResumeRead();
        if (_accepting)
            return;
        _accepting = true;
        StartSocketWork(() => _ = AcceptLoopAsync());
    }
    protected override void DoClearReadPending() => _readRequested = false;
    protected virtual Task<Socket> AcceptSocketAsync() => _socket.AcceptAsync();
    protected virtual bool CloseOnReadError(Exception error)
    {
        // If the channel is not active anymore for whatever reason we should not try to continue reading.
        if (!IsActive) return true;
        // ServerChannel should not be closed even on IOException because it can often continue
        // accepting incoming connections. (e.g. too many open files)
        return error is not IOException and not SocketException;
    }
    private async Task AcceptLoopAsync()
    {
        try
        {
            bool again = true;
            while (again && IsOpen)
            {
                var batch = new List<Socket>();
                Exception failure = null;
                bool admitted = false;
                try
                {
                    await OnSocketLoopAsync(() =>
                    {
                        if (!IsOpen || !IsActive || !IsRegistered || (!Config.IsAutoRead() && !_readRequested)) return;
                        ReceiveHandle.Reset(Config); admitted = true;
                    }).ConfigureAwait(false);
                    if (!admitted) break;
                    bool more;
                    do
                    {
                        try
                        {
                            Task<Socket> accepting = null;
                            await OnSocketLoopAsync(() => { if (IsOpen) accepting = AcceptSocketAsync(); }).ConfigureAwait(false);
                            if (accepting == null) break;
                            batch.Add(await accepting.ConfigureAwait(false));
                        }
                        catch (Exception error) { failure = error; break; }
                        more = false;
                        try
                        {
                            await OnSocketLoopAsync(() =>
                            {
                                ReceiveHandle.IncMessagesRead(1);
                                more = IsOpen && ReceiveHandle.ContinueReading() && _socket.Poll(0, SelectMode.SelectRead);
                            }).ConfigureAwait(false);
                        }
                        catch (Exception error) { failure = error; break; }
                    } while (more);
                    await DispatchReadAsync(() => _readRequested, () =>
                    {
                        if (!IsOpen) return;
                        for (int i = 0; i < batch.Count && IsOpen; i++)
                        {
                            _readRequested = false;
                            var child = CreateChild(batch[i]);
                            batch[i] = null;
                            Pipeline.FireChannelRead(child);
                        }
                        ReceiveHandle.ReadComplete();
                        Pipeline.FireChannelReadComplete();
                        if (failure != null)
                        {
                            bool close = CloseOnReadError(failure);
                            Pipeline.FireExceptionCaught(failure);
                            if (close && IsOpen) _ = CloseTransportAsync();
                        }
                        again = IsOpen && IsActive && (Config.IsAutoRead() || _readRequested);
                    }).ConfigureAwait(false);
                }
                finally { foreach (var socket in batch) socket?.Dispose(); }
                // Synchronously failing native accepts must yield instead of spinning on the owner.
                if (again) await QueueSocketWorkAsync(() => { }).ConfigureAwait(false);
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
                // Check if there is a readPending which was not processed yet.
                // This could be for two reasons:
                // * The user called Channel.read() or ChannelHandlerContext.read() in channelRead(...) method
                // * The user called Channel.read() or ChannelHandlerContext.read() in channelReadComplete(...) method
                //
                // See https://github.com/netty/netty/issues/2254
                await OnSocketLoopAsync(() => { _accepting = false; if (IsOpen && IsRegistered && _readRequested) DoBeginRead(); }).ConfigureAwait(false);
            }
            catch { }
        }
    }
}
