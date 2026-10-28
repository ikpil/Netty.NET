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

namespace Netty.NET.Transport.Channels;

/**
 * Represents the properties of a {@link Channel} implementation.
 */
public sealed class ChannelMetadata
{
    private readonly bool _hasDisconnect;
    private readonly int _defaultMaxMessagesPerRead;
    /**
     * Create a new instance
     *
     * @param hasDisconnect     {@code true} if and only if the channel has the {@code disconnect()} operation
     *                          that allows a user to disconnect and then call {@link Channel#connect(SocketAddress)}
     *                          again, such as UDP/IP.
     */
    public ChannelMetadata(bool hasDisconnect) : this(hasDisconnect, 16) { }
    /**
     * Create a new instance
     *
     * @param hasDisconnect     {@code true} if and only if the channel has the {@code disconnect()} operation
     *                          that allows a user to disconnect and then call {@link Channel#connect(SocketAddress)}
     *                          again, such as UDP/IP.
     * @param defaultMaxMessagesPerRead If a {@link MaxMessagesRecvByteBufAllocator} is in use, then this value will be
     * set for {@link MaxMessagesRecvByteBufAllocator#maxMessagesPerRead()}. Must be {@code > 0}.
     */
    public ChannelMetadata(bool hasDisconnect, int defaultMaxMessagesPerRead)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(defaultMaxMessagesPerRead);
        _hasDisconnect = hasDisconnect;
        _defaultMaxMessagesPerRead = defaultMaxMessagesPerRead;
    }
    /**
     * Returns {@code true} if and only if the channel has the {@code disconnect()} operation
     * that allows a user to disconnect and then call {@link Channel#connect(SocketAddress)} again,
     * such as UDP/IP.
     */
    public bool HasDisconnect() => _hasDisconnect;
    /**
     * If a {@link MaxMessagesRecvByteBufAllocator} is in use, then this is the default value for
     * {@link MaxMessagesRecvByteBufAllocator#maxMessagesPerRead()}.
     */
    public int DefaultMaxMessagesPerRead() => _defaultMaxMessagesPerRead;
}
