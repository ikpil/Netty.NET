/*
 * Copyright 2015 The Netty Project
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
using System.Globalization;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Logging;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Http2.Tiles;

/**
 * Demonstrates an http server using Netty to display a bunch of images, simulate
 * latency and compare it against the http2 implementation.
 */
// CLR: System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments) and Integer.parseInt is int.Parse with an
// optional sign and the invariant culture. start() blocks on bind(PORT).sync() and returns the channel's closeFuture();
// it is StartAsync(), which awaits BindAsync and returns the channel's CloseFuture Task (ChannelFuture is Task,
// docs/transport-porting.md). EventLoopGroup is IEventLoopGroup, SocketChannel ISocketChannel and
// NioServerSocketChannel.class Channel<NioServerSocketChannel>(); the anonymous ChannelInitializer<SocketChannel> is an
// ActionChannelInitializer<ISocketChannel> (Netty.NET.Transport.Bootstrapping).
public sealed class HttpServer
{

    public static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("http-port", "8080"),
            NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    private const int MAX_CONTENT_LENGTH = 1024 * 100;

    private readonly IEventLoopGroup group;

    public HttpServer(IEventLoopGroup eventLoopGroup)
    {
        group = eventLoopGroup;
    }

    public async Task<Task> StartAsync()
    {
        ServerBootstrap b = new ServerBootstrap();
        b.Option(ChannelOption.SO_BACKLOG, 1024);

        b.Group(group).Channel<NioServerSocketChannel>().Handler(new LoggingHandler(LogLevel.INFO))
        .ChildHandler(new ActionChannelInitializer<ISocketChannel>(ch =>
        {
            ch.Pipeline.AddLast(new HttpRequestDecoder(),
                                  new HttpResponseEncoder(),
                                  new HttpObjectAggregator(MAX_CONTENT_LENGTH),
                                  new Http1RequestHandler());
        }));

        IChannel ch = await b.BindAsync(PORT);
        return ch.CloseFuture;
    }
}
