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
using Netty.NET.Handler.Logging;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Proxy;

// CLR: System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments)
// and Integer.parseInt is int.Parse with an optional sign and the invariant culture. main blocks on
// bind(LOCAL_PORT).sync().channel().closeFuture().sync(); the async Main awaits the Task<IChannel> of BindAsync and then
// the channel's CloseFuture (docs/transport-porting.md: sync() is awaiting the Task). group.shutdownGracefully() is
// ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future. System.err.println is
// Console.Error.WriteLine; string + char appends the character, as in Java.
public sealed class HexDumpProxy
{

    internal static readonly int LOCAL_PORT = int.Parse(ExampleLauncher.GetProperty("localPort", "8443"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    internal static readonly string REMOTE_HOST = ExampleLauncher.GetProperty("remoteHost", "www.google.com");
    internal static readonly int REMOTE_PORT = int.Parse(ExampleLauncher.GetProperty("remotePort", "443"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    public static async Task Main(string[] args)
    {
        Console.Error.WriteLine("Proxying *:" + LOCAL_PORT + " to " + REMOTE_HOST + ':' + REMOTE_PORT + " ...");

        // Configure the bootstrap.
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
        try
        {
            ServerBootstrap b = new ServerBootstrap();
            await (await b.Group(group)
             .Channel<NioServerSocketChannel>()
             .Handler(new LoggingHandler(LogLevel.INFO))
             .ChildHandler(new HexDumpProxyInitializer(REMOTE_HOST, REMOTE_PORT))
             .ChildOption(ChannelOption.AUTO_READ, false)
             .BindAsync(LOCAL_PORT)).CloseFuture;
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
