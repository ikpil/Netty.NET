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
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Qotm;

/**
 * A UDP broadcast client that asks for a quote of the moment (QOTM) to {@link QuoteOfTheMomentServer}.
 *
 * Inspired by <a href="https://docs.oracle.com/javase/tutorial/networking/datagrams/clientServer.html">the official
 * Java tutorial</a>.
 */
// CLR: System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments)
// and Integer.parseInt is int.Parse with an optional sign and the invariant culture. main blocks on bind(0).sync() and
// writeAndFlush(...).sync(); the async Main awaits those Tasks (docs/transport-porting.md: sync() is awaiting the
// Task), and bind(0).sync().channel() is the result of BindAsync. closeFuture().await(5000) is Task.WhenAny over the
// CloseFuture and a 5000 ms Task.Delay, which reports whether the channel closed in time without throwing.
// group.shutdownGracefully() is ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future.
// SocketUtils.socketAddress(host, port) is an IPEndPoint of the parsed address (Common's SocketUtils is retired for the
// native endpoints, docs/common-porting.md), and CharsetUtil.UTF_8 is Encoding.UTF8.
public sealed class QuoteOfTheMomentClient
{

    internal static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", "7686"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    public static async Task Main(string[] args)
    {

        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        try
        {
            Bootstrap b = new Bootstrap();
            b.Group(group)
             .Channel<NioDatagramChannel>()
             .Option(ChannelOption.SO_BROADCAST, true)
             .Handler(new QuoteOfTheMomentClientHandler());

            IChannel ch = await b.BindAsync(0);

            // Broadcast the QOTM request to port 8080.
            await ch.WriteAndFlushAsync(new DatagramPacket(
                    Unpooled.CopiedBuffer("QOTM?", Encoding.UTF8),
                    new IPEndPoint(IPAddress.Parse("255.255.255.255"), PORT)));

            // QuoteOfTheMomentClientHandler will close the DatagramChannel when a
            // response is received.  If the channel is not closed within 5 seconds,
            // print an error message and quit.
            if (await Task.WhenAny(ch.CloseFuture, Task.Delay(5000)) != ch.CloseFuture)
            {
                Console.Error.WriteLine("QOTM request timed out.");
            }
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
