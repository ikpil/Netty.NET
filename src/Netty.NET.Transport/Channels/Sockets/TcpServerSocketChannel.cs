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

/// <summary>An asynchronous TCP listener; accepted child channels pass through the server pipeline.</summary>
/**
 * A {@link io.netty.channel.socket.ServerSocketChannel} implementation which uses
 * NIO selector based implementation to accept new connections.
 */
public class TcpServerSocketChannel : SocketListenerChannel
{
    public TcpServerSocketChannel() : this(Socket.OSSupportsIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork)
    { if (ListenerSocket.AddressFamily == AddressFamily.InterNetworkV6) ListenerSocket.DualMode = true; }
    public TcpServerSocketChannel(AddressFamily family) : this(new Socket(family, SocketType.Stream, ProtocolType.Tcp)) { }
    /**
     * Create a new instance using the given {@link ServerSocketChannel}.
     */
    public TcpServerSocketChannel(Socket socket) : base(socket) { }
    protected override IChannel CreateChild(Socket socket) => new TcpSocketChannel(socket, this);
}
