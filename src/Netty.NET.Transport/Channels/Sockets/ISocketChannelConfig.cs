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

/**
 * A {@link ChannelConfig} for a {@link SocketChannel}.
 *
 * <h3>Available options</h3>
 *
 * In addition to the options provided by {@link DuplexChannelConfig},
 * {@link SocketChannelConfig} allows the following options in the option map:
 *
 * <table border="1" cellspacing="0" cellpadding="6">
 * <tr>
 * <th>Name</th><th>Associated setter method</th>
 * </tr><tr>
 * <td>{@link ChannelOption#SO_KEEPALIVE}</td><td>{@link #setKeepAlive(boolean)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#SO_REUSEADDR}</td><td>{@link #setReuseAddress(boolean)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#SO_LINGER}</td><td>{@link #setSoLinger(int)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#TCP_NODELAY}</td><td>{@link #setTcpNoDelay(boolean)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#SO_RCVBUF}</td><td>{@link #setReceiveBufferSize(int)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#SO_SNDBUF}</td><td>{@link #setSendBufferSize(int)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#IP_TOS}</td><td>{@link #setTrafficClass(int)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#ALLOW_HALF_CLOSURE}</td><td>{@link #setAllowHalfClosure(boolean)}</td>
 * </tr>
 * </table>
 */
// CLR: setAllowHalfClosure and the ChannelConfig setters that Java re-declares here only to narrow
// the return type to SocketChannelConfig are inherited unchanged; a C# interface cannot narrow an
// inherited member's return type without hiding it.
public interface ISocketChannelConfig : IDuplexChannelConfig
{
    /**
     * Gets the {@link StandardSocketOptions#TCP_NODELAY} option.  Please note that the default value of this option
     * is {@code true} unlike the operating system default ({@code false}). However, for some buggy platforms, such as
     * Android, that shows erratic behavior with Nagle's algorithm disabled, the default value remains to be
     * {@code false}.
     */
    bool IsTcpNoDelay();

    /**
     * Sets the {@link StandardSocketOptions#TCP_NODELAY} option.  Please note that the default value of this option
     * is {@code true} unlike the operating system default ({@code false}). However, for some buggy platforms, such as
     * Android, that shows erratic behavior with Nagle's algorithm disabled, the default value remains to be
     * {@code false}.
     */
    ISocketChannelConfig SetTcpNoDelay(bool tcpNoDelay);

    /**
     * Gets the {@link StandardSocketOptions#SO_LINGER} option.
     */
    int GetSoLinger();

    /**
     * Sets the {@link StandardSocketOptions#SO_LINGER} option.
     */
    ISocketChannelConfig SetSoLinger(int soLinger);

    /**
     * Gets the {@link StandardSocketOptions#SO_SNDBUF} option.
     */
    int GetSendBufferSize();

    /**
     * Sets the {@link StandardSocketOptions#SO_SNDBUF} option.
     */
    ISocketChannelConfig SetSendBufferSize(int sendBufferSize);

    /**
     * Gets the {@link StandardSocketOptions#SO_RCVBUF} option.
     */
    int GetReceiveBufferSize();

    /**
     * Sets the {@link StandardSocketOptions#SO_RCVBUF} option.
     */
    ISocketChannelConfig SetReceiveBufferSize(int receiveBufferSize);

    /**
     * Gets the {@link StandardSocketOptions#SO_KEEPALIVE} option.
     */
    bool IsKeepAlive();

    /**
     * Sets the {@link StandardSocketOptions#SO_KEEPALIVE} option.
     */
    ISocketChannelConfig SetKeepAlive(bool keepAlive);

    /**
     * Gets the {@link StandardSocketOptions#IP_TOS} option.
     */
    int GetTrafficClass();

    /**
     * Sets the {@link StandardSocketOptions#IP_TOS} option.
     */
    ISocketChannelConfig SetTrafficClass(int trafficClass);

    /**
     * Gets the {@link StandardSocketOptions#SO_REUSEADDR} option.
     */
    bool IsReuseAddress();

    /**
     * Sets the {@link StandardSocketOptions#SO_REUSEADDR} option.
     */
    ISocketChannelConfig SetReuseAddress(bool reuseAddress);

    /**
     * Sets the performance preferences as specified in
     * {@link Socket#setPerformancePreferences(int, int, int)}.
     */
    ISocketChannelConfig SetPerformancePreferences(int connectionTime, int latency, int bandwidth);
}
