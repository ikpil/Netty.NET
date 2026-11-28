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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels.Nio;

namespace Netty.NET.Transport.Channels.Sockets;

/**
 * A {@link io.netty.channel.socket.ServerSocketChannel} implementation which uses
 * NIO selector based implementation to accept new connections.
 */
// CLR: System.Net.Sockets.Socket replaces the java.nio ServerSocketChannel; accepts are non-blocking Socket.Accept calls on OP_ACCEPT readiness.
// https://learn.microsoft.com/dotnet/api/system.net.sockets.socket.accept
public class NioServerSocketChannel : AbstractNioMessageChannel, IServerSocketChannel
{
    private static Socket NewChannel(SocketProtocolFamily? family)
    {
        AddressFamily addressFamily;

        if (family == null)
        {
            // CLR: provider.openServerSocketChannel() uses the OS default, which prefers IPv6 when available.
            addressFamily = Socket.OSSupportsIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
        }
        else
        {
            addressFamily = family.Value.ToJdkFamily();
            // CLR: ServerSocketChannel.open(family) rejects an unavailable family, and NioServerSocketChannel's
            // IP-only socket() view rejects a Unix domain channel, both with UnsupportedOperationException.
            if (addressFamily == AddressFamily.Unix ||
                addressFamily == AddressFamily.InterNetworkV6 && !Socket.OSSupportsIPv6)
            {
                throw new NotSupportedException("ProtocolFamily is not supported: " + family);
            }
        }

        return NioSocketChannel.NewIpChannel(addressFamily, SocketType.Stream, ProtocolType.Tcp);
    }

    /**
     * Create a new instance
     */
    public NioServerSocketChannel() : this(NewChannel(null))
    {
    }

    public NioServerSocketChannel(AddressFamily family) : this(new Socket(family, SocketType.Stream, ProtocolType.Tcp))
    {
    }

    /**
     * Create a new instance using the given {@link SelectorProvider} and protocol family (supported only since JDK 15).
     *
     * @deprecated use {@link NioServerSocketChannel#NioServerSocketChannel(SelectorProvider, SocketProtocolFamily)}
     */
    [Obsolete("use NioServerSocketChannel(SocketProtocolFamily?)")]
    public NioServerSocketChannel(InternetProtocolFamily? family) : this(family == null ? null : family.Value.ToSocketProtocolFamily())
    {
    }

    /**
     * Create a new instance using the given {@link SelectorProvider} and protocol family (supported only since JDK 15).
     */
    // CLR: there is no SelectorProvider; the CLR socket is opened directly for the family.
    public NioServerSocketChannel(SocketProtocolFamily? family) : this(NewChannel(family))
    {
    }

    /**
     * Create a new instance using the given {@link ServerSocketChannel}.
     */
    public NioServerSocketChannel(Socket socket) : base(null, socket ?? throw new ArgumentNullException(nameof(socket)), NioIoOps.ACCEPT, METADATA)
    {
        // Check if we already have a local address bound.
        // BCL Socket.Bind and Listen are separate operations.
        SetActive(socket.IsBound && (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.AcceptConnection) != 0);
    }
    private static readonly ChannelMetadata METADATA = new ChannelMetadata(false, 16);
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(NioServerSocketChannel));

    protected override int DoReadMessages(List<object> buf)
    {
        Socket ch = Accept(JavaChannel());

        try
        {
            if (ch != null)
            {
                buf.Add(new NioSocketChannel(this, ch));
                return 1;
            }
        }
        catch (Exception t)
        {
            logger.Warn("Failed to create a new channel from an accepted socket.", t);

            try
            {
                ch.Close();
            }
            catch (Exception t2)
            {
                logger.Warn("Failed to close a socket.", t2);
            }
        }

        return 0;
    }

    // CLR: SocketUtils.accept(ServerSocketChannel). A non-blocking Socket.Accept reports no pending connection as
    // WouldBlock where ServerSocketChannel.accept returns null.
    // https://learn.microsoft.com/dotnet/api/system.net.sockets.socket.accept
    internal static Socket Accept(Socket serverSocketChannel)
    {
        try
        {
            return serverSocketChannel.Accept();
        }
        catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock)
        {
            return null;
        }
    }
    public override bool IsActive
    {
        get
        {
            // As java.nio.ServerSocketChannel.isBound() will continue to return true even after the channel was closed
            // we will also need to check if it is open.
            // CLR: Socket.IsBound likewise stays true after Dispose
            // (https://learn.microsoft.com/dotnet/api/system.net.sockets.socket.isbound); AbstractChannel's lifecycle
            // flag tracks bind/close as well.
            return IsOpen && JavaChannel().IsBound && base.IsActive;
        }
    }

    protected override EndPoint LocalAddress0()
    {
        return Normalize(JavaChannel().LocalEndPoint);
    }

    protected override EndPoint RemoteAddress0()
    {
        return null;
    }

    protected override IChannelConfig NewConfig()
    {
        return new DefaultServerSocketChannelConfig(this, JavaChannel());
    }

    protected override Task DoBindAsync(EndPoint address, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        JavaChannel().Bind(address);

        if (OperatingSystem.IsLinux() && JavaChannel().ProtocolType == ProtocolType.Tcp &&
            Config.TryGetOption(ChannelOption.TCP_FASTOPEN, out int fastOpen) && fastOpen > 0)
        {
            try
            {
                LinuxSocketOptions.SetInt(JavaChannel(), (int)SocketOptionLevel.Tcp, LinuxSocketOptions.TcpFastOpen, fastOpen);
            }
            catch (SocketException error) when (error.SocketErrorCode is SocketError.ProtocolOption or SocketError.OperationNotSupported)
            {
                // The source only applies the configured queue on a supporting kernel.
            }
        }

        Config.TryGetOption(ChannelOption.SO_BACKLOG, out int backlog);
        JavaChannel().Listen(backlog);
        SetActive(true);
        return Task.CompletedTask;
    }

    protected override void DoClose()
    {
        base.DoClose();
        JavaChannel().Dispose();
    }

    // Unnecessary stuff
    protected override bool DoConnect(EndPoint remoteAddress, EndPoint localAddress)
    {
        throw new NotSupportedException();
    }

    protected override void DoFinishConnect()
    {
        throw new NotSupportedException();
    }

    protected override bool DoWriteMessage(object msg, ChannelOutboundBuffer @in)
    {
        throw new NotSupportedException();
    }

    protected override object FilterOutboundMessage(object msg)
    {
        throw new NotSupportedException();
    }

    // Override just to be able to call directly via unit tests.
    protected override bool CloseOnReadError(Exception cause)
    {
        return base.CloseOnReadError(cause);
    }
}
