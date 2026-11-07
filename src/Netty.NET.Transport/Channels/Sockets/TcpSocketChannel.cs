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
        Socket.NoDelay = true;
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
