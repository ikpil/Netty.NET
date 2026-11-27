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
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels.Nio;

namespace Netty.NET.Transport.Channels.Sockets;

/**
 * {@link io.netty.channel.socket.SocketChannel} which uses NIO selector based implementation.
 */
// CLR: System.Net.Sockets.Socket replaces the java.nio SocketChannel; the stream operations live in
// AbstractNioByteChannel, shared with NioDomainSocketChannel.
// https://learn.microsoft.com/dotnet/api/system.net.sockets.socket
public class NioSocketChannel : AbstractNioByteChannel, ISocketChannel
{
    private EndPoint _assignedLocalAddress;
    // CLR: SocketChannelImpl.remoteAddress, which socket().getRemoteSocketAddress() returns. The JDK records it when
    // the channel is accepted and when it begins to connect, and close never clears it, so a connected channel keeps
    // its remote address after it is closed. Socket.RemoteEndPoint throws ObjectDisposedException once the socket is
    // closed (measured 2026-11-27), so the channel records the address itself.
    // https://github.com/openjdk/jdk17u/blob/master/src/java.base/share/classes/sun/nio/ch/SocketChannelImpl.java
    private volatile EndPoint _remoteAddress;
    // CLR: SocketChannelImpl.localAddress, which socket().getLocalSocketAddress() returns. The JDK records it in the
    // accepted-channel constructor, in bind and when a connect completes (endConnect, endFinishConnect), and close
    // never clears it. Socket.LocalEndPoint throws ObjectDisposedException once the socket is closed.
    private volatile EndPoint _localAddress;

    protected override TaskScheduler PrepareToClose()
    {
        try
        {
            if (!JavaChannel().SafeHandle.IsClosed && JavaChannel().LingerState is { Enabled: true, LingerTime: > 0 })
            {
                // We need to cancel this key of the channel so we may not end up in a eventloop spin
                // because we try to read or write until the actual close happens which may be later due
                // SO_LINGER handling.
                // See https://github.com/netty/netty/issues/4449
                DoDeregister();
                // CLR: TaskScheduler.Default stands for GlobalEventExecutor.INSTANCE.
                return TaskScheduler.Default;
            }
        }
        catch (Exception ignore)
        {
            // Ignore the error as the underlying channel may be closed in the meantime and so
            // getSoLinger() may produce an exception. In this case we just return null.
            // See https://github.com/netty/netty/issues/4449
            _ = ignore;
        }
        return null;
    }


    private static Socket NewChannel(SocketProtocolFamily? family)
    {
        AddressFamily addressFamily;
        if (family == null)
        {
            // CLR: provider.openSocketChannel() uses the OS default, which prefers IPv6 when available.
            addressFamily = Socket.OSSupportsIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
        }
        else
        {
            addressFamily = family.Value.ToJdkFamily();
            // CLR: SocketChannel.open(family) rejects an unavailable family, and NioSocketChannel's IP-only
            // socket() view rejects a Unix domain channel, both with UnsupportedOperationException.
            if (addressFamily == AddressFamily.Unix ||
                addressFamily == AddressFamily.InterNetworkV6 && !Socket.OSSupportsIPv6)
            {
                throw new NotSupportedException("ProtocolFamily is not supported: " + family);
            }
        }
        return NewIpChannel(addressFamily, SocketType.Stream, ProtocolType.Tcp);
    }

    // CLR: OpenJDK Net.socket0 clears IPV6_V6ONLY on AF_INET6 sockets when IPv4 is available; a CLR
    // Socket(AddressFamily, ...) starts with DualMode=false. IOException becomes ChannelException as in newChannel.
    internal static Socket NewIpChannel(AddressFamily addressFamily, SocketType socketType, ProtocolType protocolType)
    {
        Socket channel = null;
        try
        {
            channel = new Socket(addressFamily, socketType, protocolType);
            if (addressFamily == AddressFamily.InterNetworkV6 && Socket.OSSupportsIPv4)
            {
                channel.DualMode = true;
            }
            return channel;
        }
        catch (SocketException e)
        {
            channel?.Dispose();
            throw new ChannelException("Failed to open a socket.", e);
        }
    }

    /**
     * Create a new instance
     */
    public NioSocketChannel() : this(null, NewChannel(null))
    {
    }

    public NioSocketChannel(AddressFamily family) : this(null, new Socket(family, SocketType.Stream, ProtocolType.Tcp))
    {
    }

    /**
     * Create a new instance using the given {@link SelectorProvider} and protocol family (supported only since JDK 15).
     *
     * @deprecated use {@link NioSocketChannel#NioSocketChannel(SelectorProvider, SocketProtocolFamily)}
     */
    [Obsolete("use NioSocketChannel(SocketProtocolFamily?)")]
    public NioSocketChannel(InternetProtocolFamily? family) : this(family == null ? null : family.Value.ToSocketProtocolFamily())
    {
    }

    /**
     * Create a new instance using the given {@link SelectorProvider} and protocol family (supported only since JDK 15).
     */
    // CLR: there is no SelectorProvider; the CLR socket is opened directly for the family.
    public NioSocketChannel(SocketProtocolFamily? family) : this(null, NewChannel(family))
    {
    }

    /**
     * Create a new instance using the given {@link SocketChannel}.
     */
    public NioSocketChannel(Socket socket) : this(null, socket)
    {
    }

    /**
     * Create a new instance
     *
     * @param parent    the {@link Channel} which created this instance or {@code null} if it was created by the user
     * @param socket    the {@link SocketChannel} which will be used
     */
    public NioSocketChannel(IChannel parent, Socket socket) : base(parent, socket)
    {
        // CLR: an accepted SocketChannelImpl is constructed with the remote address that accept returned and its local
        // address. An accepted or caller-connected Socket reports them through RemoteEndPoint and LocalEndPoint; a
        // caller-bound Socket has its local address. If a read fails, RemoteAddress0 or LocalAddress0 reads it again.
        if (socket.Connected)
        {
            try
            {
                _remoteAddress = Normalize(socket.RemoteEndPoint);
            }
            catch (SocketException)
            {
                // Ignore.
            }
        }
        if (socket.IsBound)
        {
            RecordLocalAddress(socket);
        }

        // Enable TCP_NODELAY by default if possible.
        // CLR: DefaultSocketChannelConfig's constructor does this in Java (NioSocketChannel creates its config
        // eagerly). The CLR config is created lazily, so the channel constructor applies it once, before caller or
        // child options, with the same PlatformDependent.canEnableTcpNoDelayByDefault() guard, and ignores any
        // failure as the Java constructor ignores the exception of setTcpNoDelay(true).
        if (PlatformDependent.CanEnableTcpNoDelayByDefault())
        {
            try
            {
                JavaChannel().NoDelay = true;
            }
            catch (Exception)
            {
                // Ignore.
            }
        }
    }

    protected override EndPoint RemoteAddress0()
    {
        // CLR: javaChannel().socket().getRemoteSocketAddress() is the recorded address; a socket whose address was
        // not recorded (an unresolved DnsEndPoint target, which Java rejects before connecting) reports the live one.
        return _remoteAddress ?? Normalize(JavaChannel().RemoteEndPoint);
    }

    protected override EndPoint LocalAddress0()
    {
        // CLR: javaChannel().socket().getLocalSocketAddress() is the recorded address; an unbound socket, or one whose
        // address could not be read, reports the live one.
        return _localAddress ?? base.LocalAddress0();
    }

    // CLR: Net.localAddress(fd), which SocketChannelImpl records after bind and after a completed connect. After a
    // Linux address-only bind (IP_BIND_ADDRESS_NO_PORT) Socket.LocalEndPoint may keep port 0 while the kernel assigns
    // the port at connect, so a connected socket reads it from getsockname, as LocalAddress does.
    private void RecordLocalAddress(Socket socket)
    {
        try
        {
            EndPoint local = Normalize(socket.LocalEndPoint);
            if (local is IPEndPoint { Port: 0 } && socket.Connected && OperatingSystem.IsLinux())
            {
                local = Normalize(LinuxSocketOptions.GetLocalEndpoint(socket));
            }
            _localAddress = local;
        }
        catch (SocketException)
        {
            // Ignore; LocalAddress0 reads the live address.
        }
    }

    internal override void Bind(Socket socket, EndPoint localAddress)
    {
        base.Bind(socket, localAddress);
        RecordLocalAddress(socket);
    }

    protected override void DoFinishConnect()
    {
        base.DoFinishConnect();
        // CLR: endFinishConnect records the local address of the completed connect.
        RecordLocalAddress(JavaChannel());
    }

    // CLR: TCP_FASTOPEN_CONNECT (Linux) defers the SYN to the first write, so a non-blocking connect completes
    // at once and the gathering doWrite sends the initial data with the SYN.
    // https://man7.org/linux/man-pages/man7/tcp.7.html
    internal override bool Connect(Socket socket, EndPoint remoteAddress)
    {
        // CLR: SocketChannelImpl.connect(SocketAddress) first runs checkRemote, which replaces a wildcard (any-local)
        // address with the loopback address of the same family, and beginConnect then records that address as the
        // remote address before the native connect. Socket.Connect fails a wildcard target on Windows with
        // WSAEADDRNOTAVAIL (10049, measured 2026-11-27).
        if (remoteAddress is IPEndPoint target)
        {
            if (target.Address.Equals(IPAddress.Any))
            {
                remoteAddress = new IPEndPoint(IPAddress.Loopback, target.Port);
            }
            else if (target.Address.Equals(IPAddress.IPv6Any))
            {
                remoteAddress = new IPEndPoint(IPAddress.IPv6Loopback, target.Port);
            }
            _remoteAddress = Normalize(remoteAddress);
        }

        if (OperatingSystem.IsLinux() &&
            Config.TryGetOption(ChannelOption.TCP_FASTOPEN_CONNECT, out bool fastOpen) && fastOpen)
        {
            try
            {
                LinuxSocketOptions.SetFlag(socket, (int)SocketOptionLevel.Tcp, LinuxSocketOptions.TcpFastOpenConnect, true);
            }
            catch (SocketException error) when (error.SocketErrorCode is SocketError.ProtocolOption or SocketError.OperationNotSupported)
            {
                // The kernel does not support TCP_FASTOPEN_CONNECT; connect normally.
            }
        }
        bool connected = base.Connect(socket, remoteAddress);
        if (connected)
        {
            // CLR: endConnect records the local address of a connect that completed at once.
            RecordLocalAddress(socket);
        }
        return connected;
    }

    protected override void DoWrite(ChannelOutboundBuffer @in)
    {
        int writeSpinCount = Config.GetWriteSpinCount();
        do
        {
            if (@in.IsEmpty)
            {
                // All written so clear OP_WRITE
                ClearOpWrite();
                // Directly return here so incompleteWrite(...) is not called.
                return;
            }

            // Ensure the pending writes are made of ByteBufs only.
            int maxBytesPerGatheringWrite = GetMaxBytesPerGatheringWrite();
            ReadOnlyMemory<byte>[] nioBuffers = @in.NioBuffers(1024, maxBytesPerGatheringWrite);
            int nioBufferCnt = @in.NioBufferCount;

            // Always use nioBuffers() to workaround data-corruption.
            // See https://github.com/netty/netty/issues/2761
            switch (nioBufferCnt)
            {
                case 0:
                    // We have something else beside ByteBuffers to write so fallback to normal writes.
                    writeSpinCount -= DoWrite0(@in);
                    break;
                case 1:
                {
                    // Only one ByteBuf so use non-gathering write
                    // Zero length buffers are not added to nioBuffers by ChannelOutboundBuffer, so there is no need
                    // to check if the total size of all the buffers is non-zero.
                    ReadOnlyMemory<byte> buffer = nioBuffers[0];
                    int attemptedBytes = buffer.Length;
                    int localWrittenBytes = WriteSocket(buffer);
                    if (localWrittenBytes <= 0)
                    {
                        IncompleteWrite(true);
                        return;
                    }
                    AdjustMaxBytesPerGatheringWrite(attemptedBytes, localWrittenBytes, maxBytesPerGatheringWrite);
                    @in.RemoveBytes(localWrittenBytes);
                    --writeSpinCount;
                    break;
                }
                default:
                {
                    // Zero length buffers are not added to nioBuffers by ChannelOutboundBuffer, so there is no need
                    // to check if the total size of all the buffers is non-zero.
                    // We limit the max amount to int above so cast is safe
                    long attemptedBytes = @in.NioBufferSize;
                    long localWrittenBytes = WriteSocket(nioBuffers, 0, nioBufferCnt);
                    if (localWrittenBytes <= 0)
                    {
                        IncompleteWrite(true);
                        return;
                    }
                    // Casting to int is safe because we limit the total amount of data in the nioBuffers to int above.
                    AdjustMaxBytesPerGatheringWrite((int)attemptedBytes, (int)localWrittenBytes,
                            maxBytesPerGatheringWrite);
                    @in.RemoveBytes(localWrittenBytes);
                    --writeSpinCount;
                    break;
                }
            }
        } while (writeSpinCount > 0);

        IncompleteWrite(writeSpinCount < 0);
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
            assigned = Normalize(LinuxSocketOptions.GetLocalEndpoint(JavaChannel()));
            Volatile.Write(ref _assignedLocalAddress, assigned);
            return assigned;
        }
    }
}
