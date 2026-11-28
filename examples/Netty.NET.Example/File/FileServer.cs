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
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Example.Util;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.String;
using Netty.NET.Handler.Logging;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Stream;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.File;

/**
 * Server that accept the path of a file an echo back its content.
 */
// CLR: System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments) and Integer.parseInt is int.Parse with an
// optional sign and the invariant culture. main(String[]) blocks on bind(PORT).sync() and closeFuture().sync(); it is the
// async Main, which awaits the Task of BindAsync (the Task of the bound channel stands for the bind future f, and
// f.channel() is its result) and the channel's CloseFuture. group.shutdownGracefully() is ShutdownGracefullyAsync(), whose
// Task is discarded as Java discards the future. NioServerSocketChannel.class is Channel<NioServerSocketChannel>(); the
// anonymous ChannelInitializer<SocketChannel> is an ActionChannelInitializer<ISocketChannel>
// (Netty.NET.Transport.Bootstrapping) that captures sslCtx as the anonymous class does. CharsetUtil.UTF_8 is
// Encoding.UTF8. The package-private static fields are internal.
public sealed class FileServer
{

    internal static readonly bool SSL = ExampleLauncher.GetProperty("ssl") != null;
    // Use the same default port with the telnet example so that we can use the telnet client example to access it.
    internal static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", SSL? "8992" : "8023"),
            NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    public static async Task Main(string[] args)
    {
        // Configure SSL.
        SslContext sslCtx = ServerUtil.BuildSslContext();

        // Configure the server.
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());

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

                 p.AddLast(
                         new StringEncoder(Encoding.UTF8),
                         new LineBasedFrameDecoder(8192),
                         new StringDecoder(Encoding.UTF8),
                         new ChunkedWriteHandler(),
                         new FileServerHandler());
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
