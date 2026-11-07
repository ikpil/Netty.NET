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
public class DefaultSocketChannelConfig : DefaultChannelConfig
{
    protected override void AutoReadCleared()
    { if (Channel is AbstractChannel channel) channel.ClearReadPending(); }
    protected readonly Socket Socket;
    private volatile bool _halfClosure;
    private int _backlog = 128;
    private int _tcpFastOpen;
    private LinuxTcpMd5Keys _tcpMd5Keys;
    /**
     * Creates a new instance.
     */
    public DefaultSocketChannelConfig(IChannel channel, Socket socket)
        : base(channel, channel is IServerChannel ? new ServerChannelRecvByteBufAllocator() : new AdaptiveRecvByteBufAllocator())
    {
        Socket = socket ?? throw new ArgumentNullException(nameof(socket));
        if (socket.AddressFamily == AddressFamily.Unix && channel is IServerChannel)
            _backlog = NetUtil.DefaultListenBacklog;
        if (socket.AddressFamily != AddressFamily.Unix && socket.SocketType == SocketType.Stream && channel.Parent != null)
            socket.NoDelay = true;
    }
    private static readonly ChannelOption[] Options = {
        ChannelOption.SO_RCVBUF, ChannelOption.SO_SNDBUF, ChannelOption.TCP_NODELAY,
        ChannelOption.SO_KEEPALIVE, ChannelOption.SO_REUSEADDR, ChannelOption.SO_LINGER,
        ChannelOption.IP_TOS, ChannelOption.ALLOW_HALF_CLOSURE, ChannelOption.SO_BACKLOG
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
        ? Options : Channel is IServerChannel ? DomainListenerOptions : DomainStreamOptions;
    private bool IsLinuxTcp => OperatingSystem.IsLinux() && Socket.ProtocolType == ProtocolType.Tcp;
    private bool HasTcpFastOpen => IsLinuxTcp && Channel is IServerChannel;
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
        return values;
    }
    protected override bool TryGetValue(ChannelOption option, out object value)
    {
        if (Socket.AddressFamily == AddressFamily.Unix && Array.IndexOf(SupportedSocketOptions, option) < 0)
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
            value = (int)Socket.GetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService);
        else if (ReferenceEquals(option, ChannelOption.ALLOW_HALF_CLOSURE))
            value = _halfClosure;
        else if (ReferenceEquals(option, ChannelOption.SO_BACKLOG))
            value = _backlog;
        else if (ReferenceEquals(option, ChannelOption.TCP_FASTOPEN) && HasTcpFastOpen)
            value = Volatile.Read(ref _tcpFastOpen);
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
        if (Socket.AddressFamily == AddressFamily.Unix && Array.IndexOf(SupportedSocketOptions, option) < 0)
            return base.SetValue(option, value);
        if (ReferenceEquals(option, ChannelOption.SO_RCVBUF))
            Socket.ReceiveBufferSize = (int)value;
        else if (ReferenceEquals(option, ChannelOption.SO_SNDBUF))
        {
            Socket.SendBufferSize = (int)value;
            if (Channel is SocketStreamChannel stream) stream.ResetGatheringWriteLimit();
        }
        else if (ReferenceEquals(option, ChannelOption.TCP_NODELAY))
            Socket.NoDelay = (bool)value;
        else if (ReferenceEquals(option, ChannelOption.SO_KEEPALIVE))
            Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, (bool)value);
        else if (ReferenceEquals(option, ChannelOption.SO_REUSEADDR))
            Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, (bool)value);
        else if (ReferenceEquals(option, ChannelOption.SO_LINGER))
            Socket.LingerState = new LingerOption((int)value >= 0, Math.Max(0, (int)value));
        else if (ReferenceEquals(option, ChannelOption.IP_TOS))
            Socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService, (int)value);
        else if (ReferenceEquals(option, ChannelOption.ALLOW_HALF_CLOSURE))
            _halfClosure = (bool)value;
        else if (ReferenceEquals(option, ChannelOption.SO_BACKLOG))
        { ArgumentOutOfRangeException.ThrowIfNegative((int)value); _backlog = (int)value; }
        else if (ReferenceEquals(option, ChannelOption.TCP_FASTOPEN) && HasTcpFastOpen)
        {
            int queue = (int)value;
            ArgumentOutOfRangeException.ThrowIfNegative(queue);
            Volatile.Write(ref _tcpFastOpen, queue);
        }
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

    private (int Level, int Name)? FindLinuxOption(ChannelOption option)
    {
        foreach (var candidate in LinuxOptions)
            if (ReferenceEquals(option, candidate.Option))
                return (candidate.Level, candidate.Name);
        return null;
    }
}
