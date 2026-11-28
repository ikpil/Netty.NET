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
using Netty.NET.Transport.Channels.Nio;

namespace Netty.NET.Transport.Channels.Sockets;

/// <summary>Socket options mapped to the CLR socket owned by a connected stream channel.</summary>
/**
 * The default {@link SocketChannelConfig} implementation.
 */
// CLR: one implementation serves the connected TCP stream and the Unix domain stream. The domain
// stream keeps the separate receive/send buffer and half-closure option set of Java's private
// NioDomainSocketChannelConfig; Linux-only TCP stream options stay on the TCP stream. The listener
// role is DefaultServerSocketChannelConfig. Typed accessors use the CLR Socket owned by the channel
// and wrap SocketException in ChannelException where the source wraps java.net.SocketException.
public class DefaultSocketChannelConfig : DefaultChannelConfig, ISocketChannelConfig
{
    protected readonly Socket JavaSocket;
    private volatile bool _allowHalfClosure;
    private bool _tcpFastOpenConnect;
    private LinuxTcpMd5Keys _tcpMd5Keys;

    /**
     * Creates a new instance.
     */
    // CLR: TCP_NODELAY is enabled once by the NioSocketChannel constructor, under the same
    // PlatformDependent.canEnableTcpNoDelayByDefault() guard and ignoring any failure, so lazy Config creation
    // cannot overwrite a caller or accepted-socket choice made afterwards.
    public DefaultSocketChannelConfig(IChannel channel, Socket javaSocket) : base(channel)
    {
        JavaSocket = javaSocket ?? throw new ArgumentNullException(nameof(javaSocket));
    }

    protected override void AutoReadCleared()
    {
        if (Channel is AbstractChannel channel)
            channel.ClearReadPending();
    }

    private static readonly ChannelOption[] StreamOptions = {
        ChannelOption.SO_RCVBUF, ChannelOption.SO_SNDBUF, ChannelOption.TCP_NODELAY,
        ChannelOption.SO_KEEPALIVE, ChannelOption.SO_REUSEADDR, ChannelOption.SO_LINGER,
        ChannelOption.IP_TOS, ChannelOption.ALLOW_HALF_CLOSURE
    };
    private static readonly ChannelOption[] DomainStreamOptions = {
        ChannelOption.SO_RCVBUF, ChannelOption.SO_SNDBUF, ChannelOption.ALLOW_HALF_CLOSURE
    };
    private static readonly (ChannelOption Option, int Level, int Name)[] LinuxStreamOptions = {
        (ChannelOption.IP_BIND_ADDRESS_NO_PORT, (int)SocketOptionLevel.IP, LinuxSocketOptions.IpBindAddressNoPort),
        (ChannelOption.TCP_CORK, (int)SocketOptionLevel.Tcp, LinuxSocketOptions.TcpCork),
        (ChannelOption.TCP_QUICKACK, (int)SocketOptionLevel.Tcp, LinuxSocketOptions.TcpQuickAck),
        (ChannelOption.TCP_NOTSENT_LOWAT, (int)SocketOptionLevel.Tcp, LinuxSocketOptions.TcpNotSentLowAt)
    };
    private ChannelOption[] SupportedSocketOptions => JavaSocket.AddressFamily != AddressFamily.Unix
        ? StreamOptions : DomainStreamOptions;
    private bool IsLinuxTcp => OperatingSystem.IsLinux() && JavaSocket.ProtocolType == ProtocolType.Tcp;
    private bool IsTcpStream => JavaSocket.ProtocolType == ProtocolType.Tcp;
    private ReadOnlySpan<(ChannelOption Option, int Level, int Name)> LinuxOptions => IsLinuxTcp ? LinuxStreamOptions : [];

    public override IReadOnlyDictionary<ChannelOption, object> GetOptions()
    {
        var values = new Dictionary<ChannelOption, object>(base.GetOptions());

        foreach (var option in SupportedSocketOptions)
            if (TryGetValue(option, out var value))
                values[option] = value;

        foreach (var option in LinuxOptions)
            if (TryGetValue(option.Option, out var value))
                values[option.Option] = value;

        if (IsTcpStream)
            values[ChannelOption.TCP_FASTOPEN_CONNECT] = Volatile.Read(ref _tcpFastOpenConnect);

        return values;
    }

    protected override bool TryGetValue(ChannelOption option, out object value)
    {
        if (Array.IndexOf(SupportedSocketOptions, option) >= 0)
        {
            if (ReferenceEquals(option, ChannelOption.SO_RCVBUF))
                value = GetReceiveBufferSize();
            else if (ReferenceEquals(option, ChannelOption.SO_SNDBUF))
                value = GetSendBufferSize();
            else if (ReferenceEquals(option, ChannelOption.TCP_NODELAY))
                value = IsTcpNoDelay();
            else if (ReferenceEquals(option, ChannelOption.SO_KEEPALIVE))
                value = IsKeepAlive();
            else if (ReferenceEquals(option, ChannelOption.SO_REUSEADDR))
                value = IsReuseAddress();
            else if (ReferenceEquals(option, ChannelOption.SO_LINGER))
                value = GetSoLinger();
            else if (ReferenceEquals(option, ChannelOption.IP_TOS))
                value = GetTrafficClass();
            else if (ReferenceEquals(option, ChannelOption.ALLOW_HALF_CLOSURE))
                value = IsAllowHalfClosure();
            else
                return base.TryGetValue(option, out value);

            return true;
        }

        if (ReferenceEquals(option, ChannelOption.TCP_FASTOPEN_CONNECT) && IsTcpStream)
        {
            value = Volatile.Read(ref _tcpFastOpenConnect);
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
                () => new LinuxTcpMd5Keys(JavaSocket, Channel.Parent is IServerChannel));
            keys.Replace((IReadOnlyDictionary<IPAddress, byte[]>)value);
            return true;
        }

        if (Array.IndexOf(SupportedSocketOptions, option) >= 0)
        {
            if (ReferenceEquals(option, ChannelOption.SO_RCVBUF))
                SetReceiveBufferSize((int)value);
            else if (ReferenceEquals(option, ChannelOption.SO_SNDBUF))
                SetSendBufferSize((int)value);
            else if (ReferenceEquals(option, ChannelOption.TCP_NODELAY))
                SetTcpNoDelay((bool)value);
            else if (ReferenceEquals(option, ChannelOption.SO_KEEPALIVE))
                SetKeepAlive((bool)value);
            else if (ReferenceEquals(option, ChannelOption.SO_REUSEADDR))
                SetReuseAddress((bool)value);
            else if (ReferenceEquals(option, ChannelOption.SO_LINGER))
                SetSoLinger((int)value);
            else if (ReferenceEquals(option, ChannelOption.IP_TOS))
                SetTrafficClass((int)value);
            else if (ReferenceEquals(option, ChannelOption.ALLOW_HALF_CLOSURE))
                SetAllowHalfClosure((bool)value);
            else
                return base.SetValue(option, value);

            return true;
        }

        if (ReferenceEquals(option, ChannelOption.TCP_FASTOPEN_CONNECT) && IsTcpStream)
        {
            Volatile.Write(ref _tcpFastOpenConnect, (bool)value);
            return true;
        }

        if (FindLinuxOption(option) is { } native)
        {
            LinuxSocketOptions.SetOptionValue(JavaSocket, native.Level, native.Name, value);
            return true;
        }

        return base.SetValue(option, value);
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

    // CLR: Socket.SendBufferSize reads and writes SO_SNDBUF.
    public int GetSendBufferSize()
    {
        try
        {
            return JavaSocket.SendBufferSize;
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }
    }

    // CLR: java.net.Socket.getSoLinger returns -1 when lingering is disabled; LingerOption carries
    // the enabled flag separately. https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.lingerstate
    public int GetSoLinger()
    {
        try
        {
            LingerOption linger = JavaSocket.LingerState;
            return linger.Enabled ? linger.LingerTime : -1;
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }
    }

    public int GetTrafficClass()
    {
        try
        {
            if (OperatingSystem.IsLinux() && JavaSocket.AddressFamily == AddressFamily.InterNetworkV6)
                return LinuxSocketOptions.GetInt(JavaSocket, (int)SocketOptionLevel.IPv6, LinuxIPv6TrafficClass);

            return (int)JavaSocket.GetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService);
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }
    }

    // CLR: SocketOptionName.KeepAlive at the socket level is SO_KEEPALIVE.
    public bool IsKeepAlive()
    {
        try
        {
            return (int)JavaSocket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive) != 0;
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }
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

    // CLR: Socket.NoDelay reads and writes TCP_NODELAY.
    public bool IsTcpNoDelay()
    {
        try
        {
            return JavaSocket.NoDelay;
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }
    }

    public virtual ISocketChannelConfig SetKeepAlive(bool keepAlive)
    {
        try
        {
            JavaSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, keepAlive);
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }

        return this;
    }

    // CLR: OpenJDK java.net.Socket.setPerformancePreferences has an empty body ("Not implemented yet")
    // and sun.nio.ch.SocketAdaptor does not override it, so the source call never reaches the OS.
    // The CLR Socket has no counterpart; this keeps the source's observable no-op.
    // https://github.com/openjdk/jdk/blob/master/src/java.base/share/classes/java/net/Socket.java
    public virtual ISocketChannelConfig SetPerformancePreferences(
            int connectionTime, int latency, int bandwidth)
    {
        return this;
    }

    // CLR: java.net.Socket rejects a nonpositive size with IllegalArgumentException before the
    // native call; Socket.ReceiveBufferSize only rejects negative values.
    public virtual ISocketChannelConfig SetReceiveBufferSize(int receiveBufferSize)
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

    public virtual ISocketChannelConfig SetReuseAddress(bool reuseAddress)
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

    // CLR: java.net.Socket rejects a nonpositive size with IllegalArgumentException before the
    // native call. The NIO stream configs then recalculate the gathering-write limit
    // (NioSocketChannelConfig/NioDomainSocketChannelConfig.setSendBufferSize).
    public virtual ISocketChannelConfig SetSendBufferSize(int sendBufferSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sendBufferSize);

        try
        {
            JavaSocket.SendBufferSize = sendBufferSize;

            if (Channel is AbstractNioByteChannel stream)
            {
                stream.CalculateMaxBytesPerGatheringWrite();
            }
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }

        return this;
    }

    public virtual ISocketChannelConfig SetSoLinger(int soLinger)
    {
        try
        {
            // java.net.Socket disables negative linger and clamps enabled linger to an unsigned short.
            JavaSocket.LingerState = new LingerOption(soLinger >= 0, Math.Clamp(soLinger, 0, ushort.MaxValue));
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }

        return this;
    }

    public virtual ISocketChannelConfig SetTcpNoDelay(bool tcpNoDelay)
    {
        try
        {
            JavaSocket.NoDelay = tcpNoDelay;
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }

        return this;
    }

    // CLR: java.net.Socket.setTrafficClass rejects values outside 0..255 before the native call.
    // An IPv6 Linux socket sets IPV6_TCLASS, and also IP_TOS when it carries IPv4-mapped peers.
    public virtual ISocketChannelConfig SetTrafficClass(int trafficClass)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(trafficClass);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(trafficClass, 255);

        try
        {
            if (OperatingSystem.IsLinux() && JavaSocket.AddressFamily == AddressFamily.InterNetworkV6)
            {
                LinuxSocketOptions.SetInt(JavaSocket, (int)SocketOptionLevel.IPv6, LinuxIPv6TrafficClass, trafficClass);

                if (JavaSocket.DualMode)
                    JavaSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService, trafficClass);
            }
            else
                JavaSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService, trafficClass);
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }

        return this;
    }

    public bool IsAllowHalfClosure()
    {
        return _allowHalfClosure;
    }

    public virtual IDuplexChannelConfig SetAllowHalfClosure(bool allowHalfClosure)
    {
        _allowHalfClosure = allowHalfClosure;
        return this;
    }

    // Linux IPV6_TCLASS has no SocketOptionName mapping. An IPv6 TCP socket must
    // read and set its IPv6 class, even when it can also accept IPv4-mapped peers.
    private const int LinuxIPv6TrafficClass = 67;

    private (int Level, int Name)? FindLinuxOption(ChannelOption option)
    {
        foreach (var candidate in LinuxOptions)
            if (ReferenceEquals(option, candidate.Option))
                return (candidate.Level, candidate.Name);

        return null;
    }
}
