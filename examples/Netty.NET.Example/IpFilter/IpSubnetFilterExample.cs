/*
 * Copyright 2024 The Netty Project
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
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.IpFilter;
using Netty.NET.Handler.Logging;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.IpFilter;

/**
 * Discards any incoming data from a blacklisteded IP address subnet and accepts the rest.
 */
// CLR: System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments)
// and Integer.parseInt is int.Parse with an optional sign and the invariant culture. main blocks on bind(PORT).sync() and
// closeFuture().sync(); the async Main awaits the Task<IChannel> of BindAsync (ChannelFuture f; f.channel() is its
// result) and the channel's CloseFuture (docs/transport-porting.md: sync() is awaiting the Task).
// group.shutdownGracefully() is ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future.
// ArrayList is List, the anonymous ChannelInitializer is an ActionChannelInitializer and the anonymous
// SimpleChannelInboundHandler the nested ReceivedDataHandler class.
public sealed class IpSubnetFilterExample
{

    internal static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", "8009"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    public static async Task Main(string[] args)
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        try
        {
            List<IpSubnetFilterRule> rules = new List<IpSubnetFilterRule>();

            // Reject 10.10.10.0/24 and 192.168.0.0/16 ranges but accept the rest
            rules.Add(new IpSubnetFilterRule("10.10.10.0", 24, IpFilterRuleType.REJECT));
            rules.Add(new IpSubnetFilterRule("192.168.0.0", 16, IpFilterRuleType.REJECT));

            // Share this same Handler instance with multiple ChannelPipeline(s).
            IpSubnetFilter ipFilter = new IpSubnetFilter(rules);

            ServerBootstrap b = new ServerBootstrap();
            b.Group(group)
                    .Channel<NioServerSocketChannel>()
                    .Handler(new LoggingHandler(LogLevel.INFO))
                    .ChildHandler(new ActionChannelInitializer<ISocketChannel>(ch =>
                    {
                        IChannelPipeline p = ch.Pipeline;
                        p.AddFirst(ipFilter);

                        p.AddLast(new ReceivedDataHandler());
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

    private sealed class ReceivedDataHandler : SimpleChannelInboundHandler<ByteBuf>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf msg)
        {
            Console.WriteLine("Received data from: " + ctx.Channel.RemoteAddress);
        }
    }
}
