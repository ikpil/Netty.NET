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
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Discard;

/**
 * Keeps sending random data to the specified address.
 */
// CLR: System.getProperty is ExampleLauncher.GetProperty, which reads the -Dname=value arguments, and Integer.parseInt is int.Parse with an optional sign and the invariant culture. main blocks on
// connect(...).sync() and closeFuture().sync(); the async Main awaits the Task<IChannel> of ConnectAsync (ChannelFuture f;
// f.channel() is its result) and the channel's CloseFuture (docs/transport-porting.md: sync() is awaiting the Task).
// group.shutdownGracefully() is ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future.
// NioSocketChannel.class is Channel<NioSocketChannel>(), and the anonymous ChannelInitializer is an
// ActionChannelInitializer whose delegate is initChannel. InsecureTrustManagerFactory.INSTANCE is the
// RemoteCertificateValidationCallback that SslContextBuilder.TrustManager takes; the context uses the SslStream (JDK)
// provider, since the OPENSSL provider is library-not-ported (docs/handler-porting.md).
public sealed class DiscardClient
{

    internal static readonly bool SSL = ExampleLauncher.GetProperty("ssl") != null;
    internal static readonly string HOST = ExampleLauncher.GetProperty("host", "127.0.0.1");
    internal static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", "8009"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    internal static readonly int SIZE = int.Parse(ExampleLauncher.GetProperty("size", "256"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

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
                 p.AddLast(new DiscardClientHandler());
             }));

            // Make the connection attempt.
            IChannel f = await b.ConnectAsync(HOST, PORT);

            // Wait until the connection is closed.
            await f.CloseFuture;
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
