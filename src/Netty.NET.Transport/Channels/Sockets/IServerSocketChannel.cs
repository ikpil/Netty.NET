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

namespace Netty.NET.Transport.Channels.Sockets;

// CLR: config(), localAddress() and remoteAddress() are re-declared in Java only to narrow their return types to
// ServerSocketChannelConfig and InetSocketAddress; they are inherited from IChannel unchanged, as a C# interface
// cannot narrow an inherited member's return type without hiding it.
/**
 * A TCP/IP {@link ServerChannel} which accepts incoming TCP/IP connections.
 */
public interface IServerSocketChannel : IServerChannel
{
}
