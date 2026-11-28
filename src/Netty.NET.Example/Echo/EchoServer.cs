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
using System.Globalization;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Example.Util;
using Netty.NET.Handler.Logging;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Echo;

/**
 * Echoes back any received data from a client.
 */
// CLR: System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments)
// and Integer.parseInt is int.Parse with an optional sign and the invariant culture. main blocks on bind(PORT).sync() and
// closeFuture().sync(); the async Main awaits the Task<IChannel> of BindAsync (ChannelFuture f; f.channel() is its
// result) and the channel's CloseFuture (docs/transport-porting.md: sync() is awaiting the Task).
// group.shutdownGracefully() is ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future.
// NioServerSocketChannel.class is Channel<NioServerSocketChannel>(), and the anonymous ChannelInitializer is an
// ActionChannelInitializer whose delegate is initChannel.
public sealed class EchoServer
{

    internal static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", "8007"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    public static async Task Main(string[] args)
    {
        // Configure SSL.
        SslContext sslCtx = ServerUtil.BuildSslContext();

        // Configure the server.
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
        EchoServerHandler serverHandler = new EchoServerHandler();

        try
        {
            ServerBootstrap b = new ServerBootstrap();
            b.Group(group)
             .Channel<NioServerSocketChannel>()
             .Option(ChannelOption.SO_BACKLOG, 100)
             .Handler(new LoggingHandler(LogLevel.INFO))
             .ChildHandler(new ActionChannelInitializer<ISocketChannel>(ch =>
             {
                 IChannelPipeline p = ch.Pipeline;

                 if (sslCtx != null)
                 {
                     p.AddLast(sslCtx.NewHandler(ch.Alloc()));
                 }

                 //p.addLast(new LoggingHandler(LogLevel.INFO));
                 p.AddLast(serverHandler);
             }));

            // Start the server.
            IChannel f = await b.BindAsync(PORT);

            // Wait until the server socket is closed.
            await f.CloseFuture;
        }
        finally
        {
            // Shut down all event loops to terminate all threads.
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
