/*
 * Copyright 2014 The Netty Project
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
using System.IO;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Memcache.Binary;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Memcache.Binary;

/**
 * Simple memcache client that demonstrates get and set commands against a memcache server.
 */
// CLR: System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments)
// and Integer.parseInt is int.Parse with an optional sign and the invariant culture. main blocks on connect(...).sync(),
// close().sync() and lastWriteFuture.sync(); the async Main awaits those Tasks (docs/transport-porting.md: sync() is
// awaiting the Task), and connect(...).sync().channel() is the result of ConnectAsync. group.shutdownGracefully() is
// ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future. The anonymous ChannelInitializer is an
// ActionChannelInitializer, Integer.MAX_VALUE is int.MaxValue, and new BufferedReader(new InputStreamReader(System.in))
// is Console.In, whose ReadLine also returns null at the end of the input. line.toLowerCase() uses the default locale,
// as ToLower() uses the current culture. InsecureTrustManagerFactory.INSTANCE is the RemoteCertificateValidationCallback
// that SslContextBuilder.TrustManager takes; the context uses the SslStream (JDK) provider, since the OPENSSL provider is
// library-not-ported (docs/handler-porting.md).
public sealed class MemcacheClient
{

    internal static readonly bool SSL = ExampleLauncher.GetProperty("ssl") != null;
    internal static readonly string HOST = ExampleLauncher.GetProperty("host", "127.0.0.1");
    internal static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", "11211"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    public static async Task Main(string[] args)
    {
        // Configure SSL.
        SslContext sslCtx;

        if (SSL)
        {
            sslCtx = SslContextBuilder.ForClient()
                .TrustManager(InsecureTrustManagerFactory.INSTANCE).Build();
        }
        else
        {
            sslCtx = null;
        }

        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        try
        {
            Bootstrap b = new Bootstrap();
            b.Group(group)
                    .Channel<NioSocketChannel>()
                    .Handler(new ActionChannelInitializer<ISocketChannel>(ch =>
                    {
                        IChannelPipeline p = ch.Pipeline;

                        if (sslCtx != null)
                        {
                            p.AddLast(sslCtx.NewHandler(ch.Alloc(), HOST, PORT));
                        }

                        p.AddLast(new BinaryMemcacheClientCodec());
                        p.AddLast(new BinaryMemcacheObjectAggregator(int.MaxValue));
                        p.AddLast(new MemcacheClientHandler());
                    }));

            // Start the connection attempt.
            IChannel ch = await b.ConnectAsync(HOST, PORT);

            // Read commands from the stdin.
            Console.WriteLine("Enter commands (quit to end)");
            Console.WriteLine("get <key>");
            Console.WriteLine("set <key> <value>");
            Task lastWriteFuture = null;
            TextReader @in = Console.In;

            for (;;)
            {
                string line = @in.ReadLine();
                if (line == null)
                {
                    break;
                }

                if ("quit".Equals(line.ToLower()))
                {
                    await ch.CloseAsync();
                    break;
                }

                // Sends the received line to the server.
                lastWriteFuture = ch.WriteAndFlushAsync(line);
            }

            // Wait until all messages are flushed before closing the channel.
            if (lastWriteFuture != null)
            {
                await lastWriteFuture;
            }
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
