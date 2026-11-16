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

using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace Netty.NET.Transport.Channels.Sockets;

// CLR: config(), localAddress() and remoteAddress() are re-declared in Java only to narrow their return types to
// DatagramChannelConfig and InetSocketAddress; they are inherited from IChannel unchanged, as a C# interface cannot
// narrow an inherited member's return type without hiding it. A returned Task replaces ChannelFuture, so the
// ChannelPromise overloads fold into the overloads below. InetAddress/InetSocketAddress/NetworkInterface map to
// System.Net.IPAddress, System.Net.IPEndPoint and System.Net.NetworkInformation.NetworkInterface.
/**
 * A UDP/IP {@link Channel}.
 */
public interface IDatagramChannel : IChannel
{
    /**
     * Return {@code true} if the {@link DatagramChannel} is connected to the remote peer.
     */
    bool IsConnected { get; }

    /**
     * Joins a multicast group and notifies the {@link ChannelFuture} once the operation completes.
     */
    Task JoinGroupAsync(IPAddress multicastAddress);

    /**
     * Joins the specified multicast group at the specified interface and notifies the {@link ChannelFuture}
     * once the operation completes.
     */
    Task JoinGroupAsync(IPEndPoint multicastAddress, NetworkInterface networkInterface);

    /**
     * Joins the specified multicast group at the specified interface and notifies the {@link ChannelFuture}
     * once the operation completes.
     */
    Task JoinGroupAsync(IPAddress multicastAddress, NetworkInterface networkInterface, IPAddress source);

    /**
     * Leaves a multicast group and notifies the {@link ChannelFuture} once the operation completes.
     */
    Task LeaveGroupAsync(IPAddress multicastAddress);

    /**
     * Leaves a multicast group on a specified local interface and notifies the {@link ChannelFuture} once the
     * operation completes.
     */
    Task LeaveGroupAsync(IPEndPoint multicastAddress, NetworkInterface networkInterface);

    /**
     * Leave the specified multicast group at the specified interface using the specified source and notifies
     * the {@link ChannelFuture} once the operation completes.
     *
     */
    Task LeaveGroupAsync(IPAddress multicastAddress, NetworkInterface networkInterface, IPAddress source);

    /**
     * Block the given sourceToBlock address for the given multicastAddress on the given networkInterface and notifies
     * the {@link ChannelFuture} once the operation completes.
     *
     * The given {@link ChannelFuture} will be notified and also returned.
     */
    Task BlockAsync(IPAddress multicastAddress, NetworkInterface networkInterface, IPAddress sourceToBlock);

    /**
     * Block the given sourceToBlock address for the given multicastAddress and notifies the {@link ChannelFuture} once
     * the operation completes.
     *
     * The given {@link ChannelFuture} will be notified and also returned.
     */
    Task BlockAsync(IPAddress multicastAddress, IPAddress sourceToBlock);
}
