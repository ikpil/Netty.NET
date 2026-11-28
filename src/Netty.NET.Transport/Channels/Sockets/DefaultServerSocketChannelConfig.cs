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
using Netty.NET.Common;

namespace Netty.NET.Transport.Channels.Sockets;

/// <summary>Socket options mapped to the CLR socket owned by a listening channel.</summary>
/**
 * The default {@link ServerSocketChannelConfig} implementation.
 */
// CLR: one implementation serves the TCP listener and the Unix domain listener. The domain
// listener keeps its separate receive-buffer and backlog option set (Java's private
// NioDomainServerSocketChannelConfig owns the backlog); Linux-only listener options, the Fast Open queue and
// TCP_MD5SIG stay on the Linux TCP listener. Typed accessors use the CLR Socket owned by the
// channel and wrap SocketException in ChannelException where the source wraps java.net.SocketException.
public class DefaultServerSocketChannelConfig : DefaultChannelConfig, IServerSocketChannelConfig
{
    protected readonly Socket JavaSocket;
    private int _backlog = NetUtil.SOMAXCONN;
    private int _tcpFastOpen;
    private LinuxTcpMd5Keys _tcpMd5Keys;

    /**
     * Creates a new instance.
     */
    public DefaultServerSocketChannelConfig(IServerChannel channel, Socket javaSocket)
        : base(channel, new ServerChannelRecvByteBufAllocator())
    {
        JavaSocket = javaSocket ?? throw new ArgumentNullException(nameof(javaSocket));
    }

    protected override void AutoReadCleared()
    {
        if (Channel is AbstractChannel channel)
            channel.ClearReadPending();
    }

    private static readonly ChannelOption[] ListenerOptions = {
        ChannelOption.SO_RCVBUF, ChannelOption.SO_REUSEADDR, ChannelOption.SO_BACKLOG
    };
    private static readonly ChannelOption[] DomainListenerOptions = { ChannelOption.SO_RCVBUF, ChannelOption.SO_BACKLOG };
    private static readonly (ChannelOption Option, int Level, int Name)[] LinuxListenerOptions = {
        (ChannelOption.SO_REUSEPORT, LinuxSocketOptions.SolSocket, LinuxSocketOptions.SoReusePort),
        (ChannelOption.IP_FREEBIND, (int)SocketOptionLevel.IP, LinuxSocketOptions.IpFreeBind),
        (ChannelOption.TCP_DEFER_ACCEPT, (int)SocketOptionLevel.Tcp, LinuxSocketOptions.TcpDeferAccept)
    };
    private ChannelOption[] SupportedSocketOptions => JavaSocket.AddressFamily != AddressFamily.Unix
        ? ListenerOptions : DomainListenerOptions;
    private bool IsLinuxTcp => OperatingSystem.IsLinux() && JavaSocket.ProtocolType == ProtocolType.Tcp;
    private ReadOnlySpan<(ChannelOption Option, int Level, int Name)> LinuxOptions => IsLinuxTcp ? LinuxListenerOptions : [];

    public override IReadOnlyDictionary<ChannelOption, object> GetOptions()
    {
        var values = new Dictionary<ChannelOption, object>(base.GetOptions());

        foreach (var option in SupportedSocketOptions)
            if (TryGetValue(option, out var value))
                values[option] = value;

        foreach (var option in LinuxOptions)
            if (TryGetValue(option.Option, out var value))
                values[option.Option] = value;

        if (IsLinuxTcp)
            values[ChannelOption.TCP_FASTOPEN] = Volatile.Read(ref _tcpFastOpen);

        return values;
    }

    protected override bool TryGetValue(ChannelOption option, out object value)
    {
        if (Array.IndexOf(SupportedSocketOptions, option) >= 0)
        {
            if (ReferenceEquals(option, ChannelOption.SO_RCVBUF))
                value = GetReceiveBufferSize();
            else if (ReferenceEquals(option, ChannelOption.SO_REUSEADDR))
                value = IsReuseAddress();
            else if (ReferenceEquals(option, ChannelOption.SO_BACKLOG))
                value = GetBacklog();
            else
                return base.TryGetValue(option, out value);

            return true;
        }

        if (ReferenceEquals(option, ChannelOption.TCP_FASTOPEN) && IsLinuxTcp)
        {
            value = Volatile.Read(ref _tcpFastOpen);
            return true;
        }

        if (FindLinuxOption(option) is { } native)
        {
            value = LinuxSocketOptions.GetOptionValue(JavaSocket, option, native.Level, native.Name);
            return true;
        }

        return base.TryGetValue(option, out value);
    }

    protected override bool SetValue(ChannelOption option, object value)
    {
        /**
         * Set the {@code TCP_MD5SIG} option on the socket. See {@code linux/tcp.h} for more details.
         * Keys can only be set on, not read to prevent a potential leak, as they are confidential.
         * Allowing them being read would mean anyone with access to the channel could get them.
         */
        if (ReferenceEquals(option, ChannelOption.TCP_MD5SIG) && IsLinuxTcp)
        {
            var keys = LazyInitializer.EnsureInitialized(ref _tcpMd5Keys,
                () => new LinuxTcpMd5Keys(JavaSocket, false));
            keys.Replace((IReadOnlyDictionary<IPAddress, byte[]>)value);
            return true;
        }

        if (Array.IndexOf(SupportedSocketOptions, option) >= 0)
        {
            if (ReferenceEquals(option, ChannelOption.SO_RCVBUF))
                SetReceiveBufferSize((int)value);
            else if (ReferenceEquals(option, ChannelOption.SO_REUSEADDR))
                SetReuseAddress((bool)value);
            else if (ReferenceEquals(option, ChannelOption.SO_BACKLOG))
                SetBacklog((int)value);
            else
                return base.SetValue(option, value);

            return true;
        }

        if (ReferenceEquals(option, ChannelOption.TCP_FASTOPEN) && IsLinuxTcp)
        {
            int queue = (int)value;
            ArgumentOutOfRangeException.ThrowIfNegative(queue);

            Volatile.Write(ref _tcpFastOpen, queue);
            return true;
        }

        if (FindLinuxOption(option) is { } native)
        {
            LinuxSocketOptions.SetOptionValue(JavaSocket, native.Level, native.Name, value);
            return true;
        }

        return base.SetValue(option, value);
    }

    // CLR: SocketOptionName.ReuseAddress at the socket level is SO_REUSEADDR.
    public bool IsReuseAddress()
    {
        try
        {
            return (int)JavaSocket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress) != 0;
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }
    }

    public virtual IServerSocketChannelConfig SetReuseAddress(bool reuseAddress)
    {
        try
        {
            JavaSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, reuseAddress);
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }

        return this;
    }

    // CLR: Socket.ReceiveBufferSize reads and writes SO_RCVBUF.
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.receivebuffersize
    public int GetReceiveBufferSize()
    {
        try
        {
            return JavaSocket.ReceiveBufferSize;
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }
    }

    // CLR: java.net.ServerSocket rejects a nonpositive size with IllegalArgumentException before
    // the native call; Socket.ReceiveBufferSize only rejects negative values.
    public virtual IServerSocketChannelConfig SetReceiveBufferSize(int receiveBufferSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(receiveBufferSize);

        try
        {
            JavaSocket.ReceiveBufferSize = receiveBufferSize;
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }

        return this;
    }

    // CLR: OpenJDK java.net.ServerSocket.setPerformancePreferences has an empty body ("Not
    // implemented yet"), so the source call never reaches the OS. The CLR Socket has no
    // counterpart; this keeps the source's observable no-op.
    // https://github.com/openjdk/jdk/blob/master/src/java.base/share/classes/java/net/ServerSocket.java
    public virtual IServerSocketChannelConfig SetPerformancePreferences(int connectionTime, int latency, int bandwidth)
    {
        return this;
    }

    public int GetBacklog()
    {
        return Volatile.Read(ref _backlog);
    }

    public virtual IServerSocketChannelConfig SetBacklog(int backlog)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(backlog);

        Volatile.Write(ref _backlog, backlog);
        return this;
    }

    private (int Level, int Name)? FindLinuxOption(ChannelOption option)
    {
        foreach (var candidate in LinuxOptions)
            if (ReferenceEquals(option, candidate.Option))
                return (candidate.Level, candidate.Name);

        return null;
    }
}
