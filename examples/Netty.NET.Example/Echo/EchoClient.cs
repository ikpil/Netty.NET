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
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Echo;

/**
 * Sends one message when a connection is open and echoes back any received
 * data to the server.  Simply put, the echo client initiates the ping-pong
 * traffic between the echo client and server by sending the first message to
 * the server.
 */
// CLR: System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments)
// and Integer.parseInt is int.Parse with an optional sign and the invariant culture. main blocks on connect(...).sync()
// and closeFuture().sync(); the async Main awaits the Task<IChannel> of ConnectAsync (ChannelFuture f; f.channel() is its
// result) and the channel's CloseFuture (docs/transport-porting.md: sync() is awaiting the Task).
// group.shutdownGracefully() is ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future.
// NioSocketChannel.class is Channel<NioSocketChannel>(), and the anonymous ChannelInitializer is an
// ActionChannelInitializer whose delegate is initChannel. As in the source, the client takes its SslContext from
// ServerUtil.buildSslContext().
public sealed class EchoClient
{

    internal static readonly string HOST = ExampleLauncher.GetProperty("host", "127.0.0.1");
    internal static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", "8007"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    internal static readonly int SIZE = int.Parse(ExampleLauncher.GetProperty("size", "256"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    public static async Task Main(string[] args)
    {
        // Configure SSL.git
        SslContext sslCtx = ServerUtil.BuildSslContext();

        // Configure the client.
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        try
        {
            Bootstrap b = new Bootstrap();
            b.Group(group)
             .Channel<NioSocketChannel>()
             .Option(ChannelOption.TCP_NODELAY, true)
             .Handler(new ActionChannelInitializer<ISocketChannel>(ch =>
             {
                 IChannelPipeline p = ch.Pipeline;

                 if (sslCtx != null)
                 {
                     p.AddLast(sslCtx.NewHandler(ch.Alloc(), HOST, PORT));
                 }

                 //p.addLast(new LoggingHandler(LogLevel.INFO));
                 p.AddLast(new EchoClientHandler());
             }));

            // Start the client.
            IChannel f = await b.ConnectAsync(HOST, PORT);

            // Wait until the connection is closed.
            await f.CloseFuture;
        }
        finally
        {
            // Shut down the event loop to terminate all threads.
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
