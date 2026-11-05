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

using System.Net.Sockets;

namespace Netty.NET.Transport.Channels.Sockets;

/// <summary>A TCP stream channel using CLR asynchronous socket completions and explicit event-loop reentry.</summary>
/**
 * {@link io.netty.channel.socket.SocketChannel} which uses NIO selector based implementation.
 */
public class TcpSocketChannel : SocketStreamChannel
{
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
}
