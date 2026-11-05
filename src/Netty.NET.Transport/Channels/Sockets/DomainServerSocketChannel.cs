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
using System.Net.Sockets;

namespace Netty.NET.Transport.Channels.Sockets;

/**
 * A {@link io.netty.channel.ServerChannel} implementation which uses
 * NIO selector based implementation to support UNIX Domain Sockets. This is only supported when using Java 16+.
 */
/// <summary>A Unix domain listener that owns its CLR socket and accepted children.</summary>
public class DomainServerSocketChannel : SocketListenerChannel
{
    /**
     * Create a new instance
     */
    public DomainServerSocketChannel() : this(new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)) { }

    /**
     * Create a new instance using the given {@link ServerSocketChannel}.
     */
    public DomainServerSocketChannel(Socket socket) : base(Validate(socket)) { }

    protected override IChannel CreateChild(Socket socket) => new DomainSocketChannel(this, socket);

    private static Socket Validate(Socket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        if (socket.AddressFamily != AddressFamily.Unix || socket.SocketType != SocketType.Stream)
            throw new ArgumentException("A Unix domain stream socket is required.", nameof(socket));
        return socket;
    }
}
