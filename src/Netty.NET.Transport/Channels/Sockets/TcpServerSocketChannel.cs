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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Transport.Channels.Sockets;

/// <summary>An asynchronous TCP listener; accepted child channels pass through the server pipeline.</summary>
/**
 * A {@link io.netty.channel.socket.ServerSocketChannel} implementation which uses
 * NIO selector based implementation to accept new connections.
 */
public class TcpServerSocketChannel : AbstractChannel, IServerChannel
{
    private readonly Socket _socket;
    private bool _accepting;
    private bool _readRequested;
    public TcpServerSocketChannel() : this(Socket.OSSupportsIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork)
    { if (_socket.AddressFamily == AddressFamily.InterNetworkV6) _socket.DualMode = true; }
    public TcpServerSocketChannel(AddressFamily family) : base(null, new ChannelMetadata(false, 16))
    { _socket = new Socket(family, SocketType.Stream, ProtocolType.Tcp); }
    public override EndPoint LocalAddress => IsOpen ? TcpSocketChannel.Normalize(_socket.LocalEndPoint) : null;
    public override EndPoint RemoteAddress => null;
    protected override IChannelConfig NewConfig() => new DefaultSocketChannelConfig(this, _socket);
    protected override Task DoBindAsync(EndPoint address, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _socket.Bind(address);
        Config.TryGetOption(ChannelOption.SO_BACKLOG, out int backlog);
        _socket.Listen(backlog);
        SetActive(true);
        return Task.CompletedTask;
    }
    protected override Task DoConnectAsync(EndPoint remote, EndPoint local, CancellationToken cancellationToken) => Task.FromException(new NotSupportedException("A server channel cannot connect."));
    protected override void DoClose() => _socket.Dispose();
    protected override object FilterOutboundMessage(object message) => throw new NotSupportedException("A server channel cannot write.");
    protected override Task DoWriteAsync(object message) => Task.FromException(new NotSupportedException());
    protected override void DoBeginRead()
    {
        _readRequested = true;
        ResumeRead();
        if (_accepting)
            return;
        _accepting = true;
        EventLoop.Execute(() => _ = AcceptLoopAsync());
    }
    protected override void DoClearReadPending() => _readRequested = false;
    private async Task AcceptLoopAsync()
    {
        try
        {
            bool again = true;
            while (again && IsOpen)
            {
                Socket accepted = await _socket.AcceptAsync().ConfigureAwait(false);
                try
                {
                    await DispatchReadAsync(() => _readRequested, () =>
                    {
                        if (!IsOpen)
                        { accepted.Dispose(); return; }
                        _readRequested = false;
                        Pipeline.FireChannelRead(new TcpSocketChannel(accepted, this));
                        accepted = null;
                        Pipeline.FireChannelReadComplete();
                        again = IsOpen && (Config.IsAutoRead() || _readRequested);
                    }).ConfigureAwait(false);
                }
                finally { accepted?.Dispose(); }
            }
        }
        catch (Exception error)
        {
            try
            { await OnLoopAsync(() => { if (IsOpen) Pipeline.FireExceptionCaught(error); }).ConfigureAwait(false); }
            catch { }
        }
        finally
        {
            try
            { await OnLoopAsync(() => { _accepting = false; if (IsOpen && _readRequested) DoBeginRead(); }).ConfigureAwait(false); }
            catch { }
        }
    }
}
