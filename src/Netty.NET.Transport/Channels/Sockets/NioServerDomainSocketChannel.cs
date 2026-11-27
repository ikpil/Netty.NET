/*
 * Copyright 2024 The Netty Project
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
 * A {@link io.netty.channel.ServerChannel} implementation which uses
 * NIO selector based implementation to support UNIX Domain Sockets. This is only supported when using Java 16+.
 */
// CLR: a System.Net.Sockets.Socket of AddressFamily.Unix replaces the java.nio ServerSocketChannel opened with
// StandardProtocolFamily.UNIX; accepts are non-blocking Socket.Accept calls on OP_ACCEPT readiness.
// https://learn.microsoft.com/dotnet/api/system.net.sockets.socket.accept
public class NioServerDomainSocketChannel : AbstractNioMessageChannel, IServerChannel
{
    private volatile bool bound;

    // Package-private for testing.
    internal static Socket NewChannel()
    {
        // CLR: SelectorProviderUtil.newDomainSocketChannel(provider) is a Socket of AddressFamily.Unix; there is no
        // Java 16 version gate (https://learn.microsoft.com/dotnet/api/system.net.sockets.unixdomainsocketendpoint).
        try
        {
            return new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        }
        catch (SocketException e)
        {
            throw new ChannelException("Failed to open a socket.", e);
        }
    }

    /**
     * Create a new instance
     */
    public NioServerDomainSocketChannel() : this(NewChannel())
    {
    }

    /**
     * Create a new instance using the given {@link ServerSocketChannel}.
     */
    public NioServerDomainSocketChannel(Socket socket) : base(null, Validate(socket), NioIoOps.ACCEPT, METADATA)
    {
        // Check if we already have a local address bound.
        // BCL Socket.Bind and Listen are separate operations.
        bound = socket.IsBound;
        SetActive(socket.IsBound && (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.AcceptConnection) != 0);
    }

    private static readonly ChannelMetadata METADATA = new ChannelMetadata(false, 16);
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(NioServerDomainSocketChannel));

    protected override int DoReadMessages(List<object> buf)
    {
        Socket ch = Accept(JavaChannel());

        try
        {
            if (ch != null)
            {
                buf.Add(new NioDomainSocketChannel(this, ch));
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
            return IsOpen && bound && base.IsActive;
        }
    }

    protected override EndPoint LocalAddress0()
    {
        // do not use unsafe which uses native transport (epoll or kqueue)
        // do not use javaChannel().socket() which is not nio
        try
        {
            return Normalize(JavaChannel().LocalEndPoint);
        }
        catch (Exception)
        {
            // ignore
        }
        return null;
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
        Config.TryGetOption(ChannelOption.SO_BACKLOG, out int backlog);
        JavaChannel().Listen(backlog);
        bound = true;
        SetActive(true);
        return Task.CompletedTask;
    }

    protected override void DoClose()
    {
        // Obtain the localAddress before we close the channel so it will not return null if we did not retrieve
        // it before.
        // For a managed domain socket, Socket.Bind already records the path that Dispose cleans up.
        base.DoClose();
        JavaChannel().Dispose();
    }

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

    // Override just to to be able to call directly via unit tests.
    protected override bool CloseOnReadError(Exception cause)
    {
        return base.CloseOnReadError(cause);
    }

    private static Socket Validate(Socket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        if (socket.AddressFamily != AddressFamily.Unix || socket.SocketType != SocketType.Stream)
            throw new ArgumentException("A Unix domain stream socket is required.", nameof(socket));
        return socket;
    }
}
