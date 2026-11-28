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
using Netty.NET.Handler.Logging;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Testsuite.Http2;

/**
 * An HTTP/2 Server that responds to requests with a Hello World. Once started, you can test the
 * server with the example client.
 */
// CLR: run() and main(String[]) block on bind(port).sync() and closeFuture().sync(); they are RunAsync() and the async
// Main, which await the Task of BindAsync and the channel's CloseFuture (docs/transport-porting.md: sync() is awaiting
// the Task). group.shutdownGracefully() is ShutdownGracefullyAsync(), whose Task is discarded as Java discards the
// future. NioServerSocketChannel.class is Channel<NioServerSocketChannel>(). Integer.parseInt is int.Parse with an
// optional sign and the invariant culture (codec-http's HttpResponseDecoder). The package-private constructor and run()
// are internal.
public sealed class Http2Server
{

    private readonly int port;

    internal Http2Server(int port)
    {
        this.port = port;
    }

    internal async Task RunAsync()
    {
        // Configure the server.
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        try
        {
            ServerBootstrap b = new ServerBootstrap();
            b.Option(ChannelOption.SO_BACKLOG, 1024);
            b.Group(group)
                    .Channel<NioServerSocketChannel>()
                    .Handler(new LoggingHandler(LogLevel.INFO))
                    .ChildHandler(new Http2ServerInitializer());

            IChannel ch = await b.BindAsync(port);

            await ch.CloseFuture;
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    public static async Task Main(string[] args)
    {
        int port;

        if (args.Length > 0)
        {
            port = int.Parse(args[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }
        else
        {
            port = 9000;
        }

        await new Http2Server(port).RunAsync();
    }
}
