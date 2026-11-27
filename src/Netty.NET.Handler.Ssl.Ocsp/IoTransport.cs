/*
 * Copyright 2022 The Netty Project
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
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Ssl.Ocsp;

/**
 * {@link IoTransport} holds {@link EventLoop}, {@link SocketChannel}
 * and {@link DatagramChannel} for DNS I/O.
 */
// CLR: ChannelFactory<T> is Func<T>, as resolver-dns's DnsNameResolverBuilder takes it, and SocketChannel/
// DatagramChannel/EventLoop are ISocketChannel/IDatagramChannel/IEventLoop. The anonymous ChannelFactory classes of
// DEFAULT are lambdas.
public sealed class IoTransport
{
    private readonly IEventLoop eventLoop;
    private readonly Func<ISocketChannel> socketChannel;
    private readonly Func<IDatagramChannel> datagramChannel;

    /**
     * Default {@link IoTransport} which uses {@link NioIoHandler}, {@link NioSocketChannel}
     * and {@link NioDatagramChannel}.
     */
    public static readonly IoTransport DEFAULT = new IoTransport(
            new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory()).Next(),
            () => new NioSocketChannel(),
            () => new NioDatagramChannel());

    /**
     * Create a new {@link IoTransport} instance
     *
     * @param eventLoop       {@link EventLoop} to use for I/O
     * @param socketChannel   {@link SocketChannel} for TCP DNS lookup and OCSP query
     * @param datagramChannel {@link DatagramChannel} for UDP DNS lookup
     * @return {@link NullPointerException} if any parameter is {@code null}
     */
    public static IoTransport Create(IEventLoop eventLoop, Func<ISocketChannel> socketChannel,
                                     Func<IDatagramChannel> datagramChannel)
    {
        return new IoTransport(eventLoop, socketChannel, datagramChannel);
    }

    private IoTransport(IEventLoop eventLoop, Func<ISocketChannel> socketChannel,
                        Func<IDatagramChannel> datagramChannel)
    {
        this.eventLoop = ObjectUtil.CheckNotNull(eventLoop, "EventLoop");
        this.socketChannel = ObjectUtil.CheckNotNull(socketChannel, "SocketChannel");
        this.datagramChannel = ObjectUtil.CheckNotNull(datagramChannel, "DatagramChannel");
    }

    public IEventLoop EventLoop()
    {
        return eventLoop;
    }

    public Func<ISocketChannel> SocketChannel()
    {
        return socketChannel;
    }

    public Func<IDatagramChannel> DatagramChannel()
    {
        return datagramChannel;
    }
}
