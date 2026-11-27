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
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Factorial;

/**
 * Sends a sequence of integers to a {@link FactorialServer} to calculate
 * the factorial of the specified integer.
 */
// CLR: System.getProperty is ExampleLauncher.GetProperty, which reads the -Dname=value arguments, and Integer.parseInt is int.Parse with an optional sign and the invariant culture. main blocks on
// connect(...).sync(); it is the async Main, which awaits the Task<IChannel> of ConnectAsync (f.channel() is its
// result, docs/transport-porting.md: sync() is awaiting the Task). NioSocketChannel.class is Channel<NioSocketChannel>()
// and group.shutdownGracefully() is ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future;
// pipeline().last() is the Last property.
// System.err.format("%,d") formats with the grouping separator of the default locale, which is the "N0" format of the
// current culture.
public sealed class FactorialClient
{
    internal static readonly string HOST = ExampleLauncher.GetProperty("host", "127.0.0.1");
    internal static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", "8322"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    internal static readonly int COUNT = int.Parse(ExampleLauncher.GetProperty("count", "1000"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    public static async Task Main(string[] args)
    {
        // Configure SSL.
        SslContext sslCtx = ServerUtil.BuildSslContext();

        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
        try
        {
            Bootstrap b = new Bootstrap();
            b.Group(group)
             .Channel<NioSocketChannel>()
             .Handler(new FactorialClientInitializer(sslCtx));

            // Make a new connection.
            IChannel f = await b.ConnectAsync(HOST, PORT);

            // Get the handler instance to retrieve the answer.
            FactorialClientHandler handler =
                (FactorialClientHandler) f.Pipeline.Last;

            // Print out the answer.
            Console.Error.Write(string.Format(CultureInfo.CurrentCulture, "Factorial of {0:N0} is: {1:N0}", COUNT, handler.GetFactorial()));
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
