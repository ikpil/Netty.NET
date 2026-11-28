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
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.SecureChat;

/**
 * Simple SSL chat client modified from {@link TelnetClient}.
 */
// CLR: System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments) and Integer.parseInt is
// int.Parse with an optional sign and the invariant culture. InsecureTrustManagerFactory.INSTANCE is the
// RemoteCertificateValidationCallback that SslContextBuilder.TrustManager takes (the JDK provider is SslStream).
// main(String[]) blocks; it is the async Main: connect(HOST, PORT).sync().channel() awaits ConnectAsync,
// ch.closeFuture().sync() awaits CloseFuture and lastWriteFuture.sync() awaits the last write's Task
// (ChannelFuture is Task, docs/transport-porting.md). The BufferedReader over System.in is Console.In, whose
// ReadLine() returns null at the end of the input as readLine() does. NioSocketChannel.class is
// Channel<NioSocketChannel>().
public sealed class SecureChatClient
{

    internal static readonly string HOST = ExampleLauncher.GetProperty("host", "127.0.0.1");
    internal static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", "8992"),
            NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    public static async Task Main(string[] args)
    {
        // Configure SSL.
        SslContext sslCtx = SslContextBuilder.ForClient()
            .TrustManager(InsecureTrustManagerFactory.INSTANCE).Build();

        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        try
        {
            Bootstrap b = new Bootstrap();
            b.Group(group)
             .Channel<NioSocketChannel>()
             .Handler(new SecureChatClientInitializer(sslCtx));

            // Start the connection attempt.
            IChannel ch = await b.ConnectAsync(HOST, PORT);

            // Read commands from the stdin.
            Task lastWriteFuture = null;

            for (;;)
            {
                string line = Console.In.ReadLine();
                if (line == null)
                {
                    break;
                }

                // Sends the received line to the server.
                lastWriteFuture = ch.WriteAndFlushAsync(line + "\r\n");

                // If user typed the 'bye' command, wait until the server closes
                // the connection.
                if ("bye".Equals(line.ToLowerInvariant()))
                {
                    await ch.CloseFuture;
                    break;
                }
            }

            // Wait until all messages are flushed before closing the channel.
            if (lastWriteFuture != null)
            {
                await lastWriteFuture;
            }
        }
        finally
        {
            // The connection is closed automatically on shutdown.
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
