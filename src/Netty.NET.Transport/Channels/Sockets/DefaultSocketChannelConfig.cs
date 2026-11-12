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

/// <summary>Socket options mapped to the CLR socket owned by the channel.</summary>
/**
 * The default {@link SocketChannelConfig} implementation.
 */
/**
 * The default {@link ServerSocketChannelConfig} implementation.
 */
// CLR: one implementation shares the option dispatch, with separate listener,
// TCP stream and domain option sets. Typed ChannelOption access replaces Java's
// fluent config subinterfaces. Socket performance preferences have no CLR API.
public class DefaultSocketChannelConfig : DefaultChannelConfig
{
    protected override void AutoReadCleared()
    { if (Channel is AbstractChannel channel) channel.ClearReadPending(); }
    protected readonly Socket Socket;
    private volatile bool _halfClosure;
    private int _backlog = NetUtil.DefaultListenBacklog;
    private int _tcpFastOpen;
    private bool _tcpFastOpenConnect;
    private LinuxTcpMd5Keys _tcpMd5Keys;
    /**
     * Creates a new instance.
     */
    public DefaultSocketChannelConfig(IChannel channel, Socket socket)
        : base(channel, channel is IServerChannel ? new ServerChannelRecvByteBufAllocator() : new AdaptiveRecvByteBufAllocator())
    {
        Socket = socket ?? throw new ArgumentNullException(nameof(socket));
    }
    private static readonly ChannelOption[] StreamOptions = {
        ChannelOption.SO_RCVBUF, ChannelOption.SO_SNDBUF, ChannelOption.TCP_NODELAY,
        ChannelOption.SO_KEEPALIVE, ChannelOption.SO_REUSEADDR, ChannelOption.SO_LINGER,
        ChannelOption.IP_TOS, ChannelOption.ALLOW_HALF_CLOSURE
    };
    private static readonly ChannelOption[] ListenerOptions = {
        ChannelOption.SO_RCVBUF, ChannelOption.SO_REUSEADDR, ChannelOption.SO_BACKLOG
    };
    private static readonly ChannelOption[] DomainStreamOptions = {
        ChannelOption.SO_RCVBUF, ChannelOption.SO_SNDBUF, ChannelOption.ALLOW_HALF_CLOSURE
    };
    private static readonly ChannelOption[] DomainListenerOptions = { ChannelOption.SO_RCVBUF, ChannelOption.SO_BACKLOG };
    private static readonly (ChannelOption Option, int Level, int Name)[] LinuxListenerOptions = {
        (ChannelOption.SO_REUSEPORT, LinuxSocketOptions.SolSocket, LinuxSocketOptions.SoReusePort),
        (ChannelOption.IP_FREEBIND, (int)SocketOptionLevel.IP, LinuxSocketOptions.IpFreeBind),
        (ChannelOption.TCP_DEFER_ACCEPT, (int)SocketOptionLevel.Tcp, LinuxSocketOptions.TcpDeferAccept)
    };
    private static readonly (ChannelOption Option, int Level, int Name)[] LinuxStreamOptions = {
        (ChannelOption.IP_BIND_ADDRESS_NO_PORT, (int)SocketOptionLevel.IP, LinuxSocketOptions.IpBindAddressNoPort),
        (ChannelOption.TCP_CORK, (int)SocketOptionLevel.Tcp, LinuxSocketOptions.TcpCork),
        (ChannelOption.TCP_QUICKACK, (int)SocketOptionLevel.Tcp, LinuxSocketOptions.TcpQuickAck),
        (ChannelOption.TCP_NOTSENT_LOWAT, (int)SocketOptionLevel.Tcp, LinuxSocketOptions.TcpNotSentLowAt)
    };
    private ChannelOption[] SupportedSocketOptions => Socket.AddressFamily != AddressFamily.Unix
        ? Channel is IServerChannel ? ListenerOptions : StreamOptions
        : Channel is IServerChannel ? DomainListenerOptions : DomainStreamOptions;
    private bool IsLinuxTcp => OperatingSystem.IsLinux() && Socket.ProtocolType == ProtocolType.Tcp;
    private bool HasTcpFastOpen => IsLinuxTcp && Channel is IServerChannel;
    private bool IsTcpStream => Socket.ProtocolType == ProtocolType.Tcp && Channel is not IServerChannel;
    private ReadOnlySpan<(ChannelOption Option, int Level, int Name)> LinuxOptions => !IsLinuxTcp
        ? [] : Channel is IServerChannel ? LinuxListenerOptions : LinuxStreamOptions;

    public override IReadOnlyDictionary<ChannelOption, object> GetOptions()
    {
        var values = new Dictionary<ChannelOption, object>(base.GetOptions());
        foreach (var option in SupportedSocketOptions)
            if (TryGetValue(option, out var value))
                values[option] = value;
        foreach (var option in LinuxOptions)
            if (TryGetValue(option.Option, out var value))
                values[option.Option] = value;
        if (HasTcpFastOpen)
            values[ChannelOption.TCP_FASTOPEN] = Volatile.Read(ref _tcpFastOpen);
        if (IsTcpStream)
            values[ChannelOption.TCP_FASTOPEN_CONNECT] = Volatile.Read(ref _tcpFastOpenConnect);
        return values;
    }
    protected override bool TryGetValue(ChannelOption option, out object value)
    {
        if (Array.IndexOf(SupportedSocketOptions, option) < 0 &&
            !(ReferenceEquals(option, ChannelOption.TCP_FASTOPEN) && HasTcpFastOpen) &&
            !(ReferenceEquals(option, ChannelOption.TCP_FASTOPEN_CONNECT) && IsTcpStream) &&
            FindLinuxOption(option) == null)
            return base.TryGetValue(option, out value);
        if (ReferenceEquals(option, ChannelOption.SO_RCVBUF))
            value = Socket.ReceiveBufferSize;
        else if (ReferenceEquals(option, ChannelOption.SO_SNDBUF))
            value = Socket.SendBufferSize;
        else if (ReferenceEquals(option, ChannelOption.TCP_NODELAY))
            value = Socket.NoDelay;
        else if (ReferenceEquals(option, ChannelOption.SO_KEEPALIVE))
            value = (int)Socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive) != 0;
        else if (ReferenceEquals(option, ChannelOption.SO_REUSEADDR))
            value = (int)Socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress) != 0;
        else if (ReferenceEquals(option, ChannelOption.SO_LINGER))
            value = Socket.LingerState.Enabled ? Socket.LingerState.LingerTime : -1;
        else if (ReferenceEquals(option, ChannelOption.IP_TOS))
            value = GetTrafficClass();
        else if (ReferenceEquals(option, ChannelOption.ALLOW_HALF_CLOSURE))
            value = _halfClosure;
        else if (ReferenceEquals(option, ChannelOption.SO_BACKLOG))
            value = Volatile.Read(ref _backlog);
        else if (ReferenceEquals(option, ChannelOption.TCP_FASTOPEN) && HasTcpFastOpen)
            value = Volatile.Read(ref _tcpFastOpen);
        else if (ReferenceEquals(option, ChannelOption.TCP_FASTOPEN_CONNECT) && IsTcpStream)
            value = Volatile.Read(ref _tcpFastOpenConnect);
        else if (FindLinuxOption(option) is { } native)
        {
            int raw = LinuxSocketOptions.GetInt(Socket, native.Level, native.Name);
            value = option switch
            {
                ChannelOption<bool> => raw != 0,
                ChannelOption<long> => (long)unchecked((uint)raw),
                _ => raw
            };
        }
        else
            return base.TryGetValue(option, out value);
        return true;
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
                () => new LinuxTcpMd5Keys(Socket, Channel.Parent is IServerChannel));
            keys.Replace((IReadOnlyDictionary<IPAddress, byte[]>)value);
            return true;
        }
        if (Array.IndexOf(SupportedSocketOptions, option) < 0 &&
            !(ReferenceEquals(option, ChannelOption.TCP_FASTOPEN) && HasTcpFastOpen) &&
            !(ReferenceEquals(option, ChannelOption.TCP_FASTOPEN_CONNECT) && IsTcpStream) &&
            FindLinuxOption(option) == null)
            return base.SetValue(option, value);
        if (ReferenceEquals(option, ChannelOption.SO_RCVBUF))
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero((int)value);
            Socket.ReceiveBufferSize = (int)value;
        }
        else if (ReferenceEquals(option, ChannelOption.SO_SNDBUF))
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero((int)value);
            Socket.SendBufferSize = (int)value;
            if (Channel is SocketStreamChannel stream) stream.CalculateMaxBytesPerGatheringWrite();
        }
        else if (ReferenceEquals(option, ChannelOption.TCP_NODELAY))
            Socket.NoDelay = (bool)value;
        else if (ReferenceEquals(option, ChannelOption.SO_KEEPALIVE))
            Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, (bool)value);
        else if (ReferenceEquals(option, ChannelOption.SO_REUSEADDR))
            Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, (bool)value);
        else if (ReferenceEquals(option, ChannelOption.SO_LINGER))
            // java.net.Socket disables negative linger and clamps enabled linger to an unsigned short.
            Socket.LingerState = new LingerOption((int)value >= 0, Math.Clamp((int)value, 0, ushort.MaxValue));
        else if (ReferenceEquals(option, ChannelOption.IP_TOS))
            SetTrafficClass((int)value);
        else if (ReferenceEquals(option, ChannelOption.ALLOW_HALF_CLOSURE))
            _halfClosure = (bool)value;
        else if (ReferenceEquals(option, ChannelOption.SO_BACKLOG))
        { ArgumentOutOfRangeException.ThrowIfNegative((int)value); Volatile.Write(ref _backlog, (int)value); }
        else if (ReferenceEquals(option, ChannelOption.TCP_FASTOPEN) && HasTcpFastOpen)
        {
            int queue = (int)value;
            ArgumentOutOfRangeException.ThrowIfNegative(queue);
            Volatile.Write(ref _tcpFastOpen, queue);
        }
        else if (ReferenceEquals(option, ChannelOption.TCP_FASTOPEN_CONNECT) && IsTcpStream)
            Volatile.Write(ref _tcpFastOpenConnect, (bool)value);
        else if (FindLinuxOption(option) is { } native)
        {
            int raw;
            if (value is long threshold)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(threshold);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(threshold, (long)uint.MaxValue);
                raw = unchecked((int)threshold);
            }
            else
                raw = value is bool enabled ? enabled ? 1 : 0 : (int)value;
            LinuxSocketOptions.SetInt(Socket, native.Level, native.Name, raw);
        }
        else
            return base.SetValue(option, value);
        return true;
    }

    // Linux IPV6_TCLASS has no SocketOptionName mapping. An IPv6 TCP socket must
    // read and set its IPv6 class, even when it can also accept IPv4-mapped peers.
    private const int LinuxIPv6TrafficClass = 67;

    private int GetTrafficClass()
    {
        if (OperatingSystem.IsLinux() && Socket.AddressFamily == AddressFamily.InterNetworkV6)
            return LinuxSocketOptions.GetInt(Socket, (int)SocketOptionLevel.IPv6, LinuxIPv6TrafficClass);
        return (int)Socket.GetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService);
    }

    private void SetTrafficClass(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 255);
        if (OperatingSystem.IsLinux() && Socket.AddressFamily == AddressFamily.InterNetworkV6)
        {
            LinuxSocketOptions.SetInt(Socket, (int)SocketOptionLevel.IPv6, LinuxIPv6TrafficClass, value);
            if (Socket.DualMode)
                Socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService, value);
        }
        else
            Socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService, value);
    }

    private (int Level, int Name)? FindLinuxOption(ChannelOption option)
    {
        foreach (var candidate in LinuxOptions)
            if (ReferenceEquals(option, candidate.Option))
                return (candidate.Level, candidate.Name);
        return null;
    }
}
