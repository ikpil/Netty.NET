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
 * A {@link ChannelConfig} for a {@link ServerSocketChannel}.
 *
 * <h3>Available options</h3>
 *
 * In addition to the options provided by {@link ChannelConfig},
 * {@link ServerSocketChannelConfig} allows the following options in the
 * option map:
 *
 * <table border="1" cellspacing="0" cellpadding="6">
 * <tr>
 * <th>Name</th><th>Associated setter method</th>
 * </tr><tr>
 * <td>{@code "backlog"}</td><td>{@link #setBacklog(int)}</td>
 * </tr><tr>
 * <td>{@code "reuseAddress"}</td><td>{@link #setReuseAddress(boolean)}</td>
 * </tr><tr>
 * <td>{@code "receiveBufferSize"}</td><td>{@link #setReceiveBufferSize(int)}</td>
 * </tr>
 * </table>
 */
// CLR: the ChannelConfig setters that Java re-declares here only to narrow the return type to
// ServerSocketChannelConfig are inherited from IChannelConfig unchanged; a C# interface cannot
// narrow an inherited member's return type without hiding it.
public interface IServerSocketChannelConfig : IChannelConfig
{
    /**
     * Gets the backlog value to specify when the channel binds to a local
     * address.
     */
    int GetBacklog();

    /**
     * Sets the backlog value to specify when the channel binds to a local
     * address.
     */
    IServerSocketChannelConfig SetBacklog(int backlog);

    /**
     * Gets the {@link StandardSocketOptions#SO_REUSEADDR} option.
     */
    bool IsReuseAddress();

    /**
     * Sets the {@link StandardSocketOptions#SO_REUSEADDR} option.
     */
    IServerSocketChannelConfig SetReuseAddress(bool reuseAddress);

    /**
     * Gets the {@link StandardSocketOptions#SO_RCVBUF} option.
     */
    int GetReceiveBufferSize();

    /**
     * Gets the {@link StandardSocketOptions#SO_SNDBUF} option.
     */
    IServerSocketChannelConfig SetReceiveBufferSize(int receiveBufferSize);

    /**
     * Sets the performance preferences as specified in
     * {@link ServerSocket#setPerformancePreferences(int, int, int)}.
     */
    IServerSocketChannelConfig SetPerformancePreferences(int connectionTime, int latency, int bandwidth);
}
