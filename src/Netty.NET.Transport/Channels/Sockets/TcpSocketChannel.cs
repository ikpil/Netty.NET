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
using Netty.NET.Buffer;

namespace Netty.NET.Transport.Channels.Sockets;

/// <summary>A TCP stream channel using CLR asynchronous socket completions and explicit event-loop reentry.</summary>
/**
 * {@link io.netty.channel.socket.SocketChannel} which uses NIO selector based implementation.
 */
public class TcpSocketChannel : SocketStreamChannel
{
    private EndPoint _assignedLocalAddress;

    public TcpSocketChannel() : this(new Socket(Socket.OSSupportsIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), null)
    { if (Socket.AddressFamily == AddressFamily.InterNetworkV6) Socket.DualMode = true; }
    public TcpSocketChannel(AddressFamily family) : this(new Socket(family, SocketType.Stream, ProtocolType.Tcp), null) { }
    /**
     * Create a new instance using the given {@link SocketChannel}.
     */
    public TcpSocketChannel(Socket socket) : this(socket, null) { }
    internal TcpSocketChannel(Socket socket, IChannel parent) : base(socket, parent)
    {
        // Enable TCP_NODELAY by default if possible.
        // CLR: initialize once at construction, before caller/child options are
        // applied; lazy configuration access must not reset the native socket.
        try { Socket.NoDelay = true; }
        catch (Exception error) when (error is SocketException or ObjectDisposedException)
        {
            // Ignore.
        }
    }

    protected override ValueTask ConnectSocketAsync(EndPoint remote, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux() ||
            !Config.TryGetOption(ChannelOption.TCP_FASTOPEN_CONNECT, out bool fastOpen) || !fastOpen)
            return base.ConnectSocketAsync(remote, cancellationToken);

        OutboundBuffer.AddFlush();
        if (OutboundBuffer.Current is not ByteBuf initial || !initial.IsReadable)
            return base.ConnectSocketAsync(remote, cancellationToken);

        var memory = initial.ReadableSequence.First;
        if (memory.IsEmpty)
            return base.ConnectSocketAsync(remote, cancellationToken);
        int maximumSegment;
        try
        {
            Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.FastOpen, true);
            LinuxSocketOptions.SetFlag(Socket, (int)SocketOptionLevel.Tcp, LinuxSocketOptions.TcpFastOpenConnect, true);
            maximumSegment = LinuxSocketOptions.GetInt(Socket, (int)SocketOptionLevel.Tcp, LinuxSocketOptions.TcpMaxSegment);
        }
        catch (SocketException error) when (error.SocketErrorCode is SocketError.ProtocolOption or SocketError.OperationNotSupported)
        {
            return base.ConnectSocketAsync(remote, cancellationToken);
        }
        // Limit the initial write to one kernel-reported segment. Remaining data stays queued
        // for normal writes, as with the source's potentially partial Fast Open write.
        if (maximumSegment <= 0)
            return base.ConnectSocketAsync(remote, cancellationToken);
        initial.Retain();
        return ConnectWithInitialDataAsync();

        async ValueTask ConnectWithInitialDataAsync()
        {
            try
            {
                int length = Math.Min(memory.Length, maximumSegment);
                // If no cookie is present, the write fails with EINPROGRESS and this call basically
                // becomes a normal async connect. All writes will be sent normally afterwards.
                // CLR: ConnectAsync waits for the handshake without a cookie. With a cached cookie,
                // the first SendAsync starts the SYN and supplies its data.
                await base.ConnectSocketAsync(remote, cancellationToken).ConfigureAwait(false);
                int written = await Socket.SendAsync(memory[..length], SocketFlags.None, cancellationToken).ConfigureAwait(false);
                // We had a cookie and our fast-open proceeded. Remove written data
                // then continue with normal TCP operation.
                await OnSocketLoopAsync(() =>
                {
                    if (IsOpen)
                        OutboundBuffer.RemoveBytes(written);
                }).ConfigureAwait(false);
            }
            finally { initial.Release(); }
        }
    }

    public override EndPoint LocalAddress
    {
        get
        {
            var address = base.LocalAddress;
            if (address is not IPEndPoint { Port: 0 } || !IsActive || !OperatingSystem.IsLinux())
                return address;

            // Socket.LocalEndPoint may cache the address-only bind. Connect assigns the kernel port.
            if (Volatile.Read(ref _assignedLocalAddress) is { } assigned)
                return assigned;
            assigned = Normalize(LinuxSocketOptions.GetLocalEndpoint(Socket));
            Volatile.Write(ref _assignedLocalAddress, assigned);
            return assigned;
        }
    }
}
