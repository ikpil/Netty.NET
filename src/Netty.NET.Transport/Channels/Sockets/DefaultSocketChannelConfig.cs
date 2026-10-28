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
using System.Net.Sockets;

namespace Netty.NET.Transport.Channels.Sockets;

/// <summary>Socket options mapped to the CLR socket owned by the channel.</summary>
/**
 * The default {@link SocketChannelConfig} implementation.
 */
public class DefaultSocketChannelConfig : DefaultChannelConfig
{
    protected readonly Socket Socket;
    private volatile bool _halfClosure;
    private int _backlog = 128;
    /**
     * Creates a new instance.
     */
    public DefaultSocketChannelConfig(IChannel channel, Socket socket) : base(channel)
    {
        Socket = socket ?? throw new ArgumentNullException(nameof(socket));
        if (socket.SocketType == SocketType.Stream && channel.Parent != null)
            socket.NoDelay = true;
    }
    private static readonly ChannelOption[] Options = {
        ChannelOption.SO_RCVBUF, ChannelOption.SO_SNDBUF, ChannelOption.TCP_NODELAY,
        ChannelOption.SO_KEEPALIVE, ChannelOption.SO_REUSEADDR, ChannelOption.SO_LINGER,
        ChannelOption.IP_TOS, ChannelOption.ALLOW_HALF_CLOSURE, ChannelOption.SO_BACKLOG
    };
    public override IReadOnlyDictionary<ChannelOption, object> GetOptions()
    {
        var values = new Dictionary<ChannelOption, object>(base.GetOptions());
        foreach (var option in Options)
            if (TryGetValue(option, out var value))
                values[option] = value;
        return values;
    }
    protected override bool TryGetValue(ChannelOption option, out object value)
    {
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
        else
            return base.TryGetValue(option, out value);
        return true;
    }
    protected override bool SetValue(ChannelOption option, object value)
    {
        if (ReferenceEquals(option, ChannelOption.SO_RCVBUF))
            Socket.ReceiveBufferSize = (int)value;
        else if (ReferenceEquals(option, ChannelOption.SO_SNDBUF))
            Socket.SendBufferSize = (int)value;
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
        else
            return base.SetValue(option, value);
        return true;
    }
}
