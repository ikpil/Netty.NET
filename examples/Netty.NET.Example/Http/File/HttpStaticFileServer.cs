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

namespace Netty.NET.Example.Http.File;

// CLR: System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments)
// and Integer.parseInt is int.Parse with an optional sign and the invariant culture. main blocks on bind(PORT).sync()
// and closeFuture().sync(); it is the async Main, which awaits the Task<IChannel> of BindAsync (Channel ch is its
// result) and the channel's CloseFuture (docs/transport-porting.md: sync() is awaiting the Task).
// NioServerSocketChannel.class is Channel<NioServerSocketChannel>() and group.shutdownGracefully() is
// ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future. System.err.println is
// Console.Error.WriteLine.
public sealed class HttpStaticFileServer
{
    internal static readonly bool SSL = ExampleLauncher.GetProperty("ssl") != null;
    internal static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", SSL? "8443" : "8080"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    public static async Task Main(string[] args)
    {
        // Configure SSL.
        SslContext sslCtx = ServerUtil.BuildSslContext();

        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        try
        {
            ServerBootstrap b = new ServerBootstrap();
            b.Group(group)
             .Channel<NioServerSocketChannel>()
             .Handler(new LoggingHandler(LogLevel.INFO))
             .ChildHandler(new HttpStaticFileServerInitializer(sslCtx));

            IChannel ch = await b.BindAsync(PORT);

            Console.Error.WriteLine("Open your web browser and navigate to " +
                    (SSL? "https" : "http") + "://127.0.0.1:" + PORT + '/');

            await ch.CloseFuture;
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
