/*
 * Copyright 2017 The Netty Project
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
using Netty.NET.Handler.Logging;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Uptime;

/**
 * Uptime server is served as a connection server.
 * So it simply discards all message received.
 */
// CLR: System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments) and Integer.parseInt is
// int.Parse with an optional sign and the invariant culture. main blocks on bind(PORT).sync() and
// closeFuture().sync(); the async Main awaits the Task<IChannel> of BindAsync (ChannelFuture f; f.channel() is its
// result) and the channel's CloseFuture (docs/transport-porting.md: sync() is awaiting the Task).
// group.shutdownGracefully() is ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future.
public sealed class UptimeServer
{
    private static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", "8080"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    private static readonly UptimeServerHandler handler = new UptimeServerHandler();

    private UptimeServer()
    {
    }

    public static async Task Main(string[] args)
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        try
        {
            ServerBootstrap b = new ServerBootstrap();
            b.Group(group)
                    .Channel<NioServerSocketChannel>()
                    .Handler(new LoggingHandler(LogLevel.INFO))
                    .ChildHandler(new ActionChannelInitializer<ISocketChannel>(ch =>
                    {
                        ch.Pipeline.AddLast(handler);
                    }));

            // Bind and start to accept incoming connections.
            IChannel f = await b.BindAsync(PORT);

            // Wait until the server socket is closed.
            // In this example, this does not happen, but you can do that to gracefully
            // shut down your server.
            await f.CloseFuture;
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
