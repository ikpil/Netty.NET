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
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Qotm;

/**
 * A UDP server that responds to the QOTM (quote of the moment) request to a {@link QuoteOfTheMomentClient}.
 *
 * Inspired by <a href="https://docs.oracle.com/javase/tutorial/networking/datagrams/clientServer.html">the official
 * Java tutorial</a>.
 */
// CLR: System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments)
// and Integer.parseInt is int.Parse with an optional sign and the invariant culture. main blocks on
// bind(PORT).sync().channel().closeFuture().await(); the async Main awaits the Task<IChannel> of BindAsync and then the
// channel's CloseFuture (docs/transport-porting.md: sync() is awaiting the Task). group.shutdownGracefully() is
// ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future.
public sealed class QuoteOfTheMomentServer
{

    private static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", "7686"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    public static async Task Main(string[] args)
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        try
        {
            Bootstrap b = new Bootstrap();
            b.Group(group)
             .Channel<NioDatagramChannel>()
             .Option(ChannelOption.SO_BROADCAST, true)
             .Handler(new QuoteOfTheMomentServerHandler());

            await (await b.BindAsync(PORT)).CloseFuture;
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
