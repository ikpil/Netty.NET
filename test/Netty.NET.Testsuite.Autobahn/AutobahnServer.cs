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
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Testsuite.Autobahn;

/**
 * A Web Socket echo server for running the
 * <a href="http://autobahn.ws/testsuite/">autobahn test suite</a>
 */
// CLR: run() and main(String[]) block on bind(port).sync() and f.channel().closeFuture().sync(); they are RunAsync()
// and the async Main, which await the Task<IChannel> of BindAsync (ChannelFuture f; f.channel() is its result) and the
// channel's CloseFuture (docs/transport-porting.md: sync() is awaiting the Task). group.shutdownGracefully() is
// ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future. NioServerSocketChannel.class is
// Channel<NioServerSocketChannel>() and PooledByteBufAllocator.DEFAULT is PooledByteBufAllocator.Default.
// System.out.println is Console.WriteLine. Integer.parseInt is int.Parse with an optional sign and the invariant culture
// (codec-http's HttpResponseDecoder).
public class AutobahnServer
{

    private readonly int port;

    public AutobahnServer(int port)
    {
        this.port = port;
    }

    public async Task RunAsync()
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        try
        {
            ServerBootstrap b = new ServerBootstrap();
            b.Group(group)
             .Channel<NioServerSocketChannel>()
             .ChildOption(ChannelOption.ALLOCATOR, PooledByteBufAllocator.Default)
             .ChildHandler(new AutobahnServerInitializer());

            Task<IChannel> f = b.BindAsync(port);
            await f;
            Console.WriteLine("Web Socket Server started at port " + port);
            await f.Result.CloseFuture;
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

        await new AutobahnServer(port).RunAsync();
    }
}
