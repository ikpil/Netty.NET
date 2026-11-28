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
using Netty.NET.Handler.Logging;
using Netty.NET.Handler.Ssl;
using Netty.NET.PkiTesting;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.PortUnification;

/**
 * Serves two protocols (HTTP and Factorial) using only one port, enabling
 * either SSL or GZIP dynamically on demand.
 * <p>
 * Because SSL and GZIP are enabled on demand, 5 combinations per protocol
 * are possible: none, SSL only, GZIP only, SSL + GZIP, and GZIP + SSL.
 */
// CLR: System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments) and Integer.parseInt is
// int.Parse with an optional sign and the invariant culture. The self-signed certificate comes from PkiTesting's
// CertificateBuilder, as in the source, and the SSL context is built by the JDK (SslStream) provider: OPENSSL and
// tcnative are library-not-ported. The anonymous ChannelInitializer<SocketChannel> is Transport's
// ActionChannelInitializer<ISocketChannel> (Netty.NET.Transport.Bootstrapping), whose delegate is the initChannel
// body. main blocks on bind(PORT).sync() and closeFuture().sync(); it is the async Main, which awaits the
// Task<IChannel> of BindAsync and the channel's CloseFuture (docs/transport-porting.md: sync() is awaiting the
// Task). NioServerSocketChannel.class is Channel<NioServerSocketChannel>() and group.shutdownGracefully() is
// ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future.
public sealed class PortUnificationServer
{
    internal static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", "8080"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    public static async Task Main(string[] args)
    {
        // Configure SSL context
        X509Bundle ssc = new CertificateBuilder()
                .Subject("cn=localhost")
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();
        SslContext sslCtx = SslContextBuilder.ForServer(ssc.ToKeyManagerFactory())
            .Build();

        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        try
        {
            ServerBootstrap b = new ServerBootstrap();
            b.Group(group)
             .Channel<NioServerSocketChannel>()
             .Handler(new LoggingHandler(LogLevel.INFO))
             .ChildHandler(new ActionChannelInitializer<ISocketChannel>(ch =>
             {
                 ch.Pipeline.AddLast(new PortUnificationServerHandler(sslCtx));
             }));

            // Bind and start to accept incoming connections.
            await (await b.BindAsync(PORT)).CloseFuture;
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
