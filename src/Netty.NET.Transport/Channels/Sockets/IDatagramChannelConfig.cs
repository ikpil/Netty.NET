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

namespace Netty.NET.Transport.Channels.Sockets;

/**
 * A {@link ChannelConfig} for a {@link DatagramChannel}.
 *
 * <h3>Available options</h3>
 *
 * In addition to the options provided by {@link ChannelConfig},
 * {@link DatagramChannelConfig} allows the following options in the option map:
 *
 * <table border="1" cellspacing="0" cellpadding="6">
 * <tr>
 * <th>Name</th><th>Associated setter method</th>
 * </tr><tr>
 * <td>{@link ChannelOption#SO_BROADCAST}</td><td>{@link #setBroadcast(boolean)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#IP_MULTICAST_ADDR}</td><td>{@link #setInterface(InetAddress)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#IP_MULTICAST_LOOP_DISABLED}</td>
 * <td>{@link #setLoopbackModeDisabled(boolean)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#IP_MULTICAST_IF}</td>
 * <td>{@link #setNetworkInterface(NetworkInterface)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#SO_REUSEADDR}</td><td>{@link #setReuseAddress(boolean)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#SO_RCVBUF}</td><td>{@link #setReceiveBufferSize(int)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#SO_SNDBUF}</td><td>{@link #setSendBufferSize(int)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#IP_MULTICAST_TTL}</td><td>{@link #setTimeToLive(int)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#IP_TOS}</td><td>{@link #setTrafficClass(int)}</td>
 * </tr>
 * </table>
 */
// CLR: java.net.InetAddress and java.net.NetworkInterface map to System.Net.IPAddress and
// System.Net.NetworkInformation.NetworkInterface. The ChannelConfig setters that Java re-declares
// here only to narrow the return type to DatagramChannelConfig are inherited from IChannelConfig
// unchanged; a C# interface cannot narrow an inherited member's return type without hiding it.
public interface IDatagramChannelConfig : IChannelConfig
{
    /**
     * Gets the {@link StandardSocketOptions#SO_SNDBUF} option.
     */
    int GetSendBufferSize();

    /**
     * Sets the {@link StandardSocketOptions#SO_SNDBUF} option.
     */
    IDatagramChannelConfig SetSendBufferSize(int sendBufferSize);

    /**
     * Gets the {@link StandardSocketOptions#SO_RCVBUF} option.
     */
    int GetReceiveBufferSize();

    /**
     * Sets the {@link StandardSocketOptions#SO_RCVBUF} option.
     */
    IDatagramChannelConfig SetReceiveBufferSize(int receiveBufferSize);

    /**
     * Gets the {@link StandardSocketOptions#IP_TOS} option.
     */
    int GetTrafficClass();

    /**
     * Sets the {@link StandardSocketOptions#IP_TOS} option.
     */
    IDatagramChannelConfig SetTrafficClass(int trafficClass);

    /**
     * Gets the {@link StandardSocketOptions#SO_REUSEADDR} option.
     */
    bool IsReuseAddress();

    /**
     * Gets the {@link StandardSocketOptions#SO_REUSEADDR} option.
     */
    IDatagramChannelConfig SetReuseAddress(bool reuseAddress);

    /**
     * Gets the {@link StandardSocketOptions#SO_BROADCAST} option.
     */
    bool IsBroadcast();

    /**
     * Sets the {@link StandardSocketOptions#SO_BROADCAST} option.
     */
    IDatagramChannelConfig SetBroadcast(bool broadcast);

    /**
     * Gets the {@link StandardSocketOptions#IP_MULTICAST_LOOP} option.
     *
     * @return {@code true} if and only if the loopback mode has been disabled
     */
    bool IsLoopbackModeDisabled();

    /**
     * Sets the {@link StandardSocketOptions#IP_MULTICAST_LOOP} option.
     *
     * @param loopbackModeDisabled
     *        {@code true} if and only if the loopback mode has been disabled
     */
    IDatagramChannelConfig SetLoopbackModeDisabled(bool loopbackModeDisabled);

    /**
     * Gets the {@link StandardSocketOptions#IP_MULTICAST_TTL} option.
     */
    int GetTimeToLive();

    /**
     * Sets the {@link StandardSocketOptions#IP_MULTICAST_TTL} option.
     */
    IDatagramChannelConfig SetTimeToLive(int ttl);

    /**
     * Gets the address of the network interface used for multicast packets.
     */
    IPAddress GetInterface();

    /**
     * Sets the address of the network interface used for multicast packets.
     */
    IDatagramChannelConfig SetInterface(IPAddress interfaceAddress);

    /**
     * Gets the {@link StandardSocketOptions#IP_MULTICAST_IF} option.
     */
    NetworkInterface GetNetworkInterface();

    /**
     * Sets the {@link StandardSocketOptions#IP_MULTICAST_IF} option.
     */
    IDatagramChannelConfig SetNetworkInterface(NetworkInterface networkInterface);
}
