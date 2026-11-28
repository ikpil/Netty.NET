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
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Logging;
using Netty.NET.Handler.Ssl;
using Netty.NET.PkiTesting;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;
using Protocol = Netty.NET.Handler.Ssl.ApplicationProtocolConfig.Protocol;
using SelectedListenerFailureBehavior = Netty.NET.Handler.Ssl.ApplicationProtocolConfig.SelectedListenerFailureBehavior;
using SelectorFailureBehavior = Netty.NET.Handler.Ssl.ApplicationProtocolConfig.SelectorFailureBehavior;

namespace Netty.NET.Example.Spdy.Server;

/**
 * A SPDY Server that responds to a GET request with a Hello World.
 * <p>
 * You may also use the {@code run-example.sh} script to start the server from the command line:
 * <pre>
 *     ./run-example.sh spdy-server
 * </pre>
 * <p>
 * Once started, you can test the server with your
 * <a href="https://en.wikipedia.org/wiki/SPDY#Browser_support_and_usage">SPDY enabled web browser</a> by navigating
 * to <a href="https://localhost:8443/">https://localhost:8443/</a>
 */
// CLR: run-example.sh is the Maven example module's launcher; this port runs the example with ExampleLauncher
// ("spdy.server.SpdyServer"). System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments) and Integer.parseInt is int.Parse with an optional sign and the invariant culture.
// The self-signed certificate comes from PkiTesting's CertificateBuilder, as in the source; the SSL context is built by
// the JDK (SslStream) provider, which negotiates ALPN through SslStream's application protocols: OPENSSL and tcnative
// are library-not-ported (docs/handler-porting.md). The nested ApplicationProtocolConfig enums are named by using
// aliases, as the Java imports name them. main blocks on bind(PORT).sync() and closeFuture().sync(); it is the async
// Main, which awaits the Task<IChannel> of BindAsync (Channel ch is its result) and the channel's CloseFuture
// (docs/transport-porting.md: sync() is awaiting the Task). NioServerSocketChannel.class is
// Channel<NioServerSocketChannel>(), group.shutdownGracefully() is ShutdownGracefullyAsync(), whose Task is discarded as
// Java discards the future, and System.err.println is Console.Error.WriteLine.
public sealed class SpdyServer
{
    internal static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", "8443"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    public static async Task Main(string[] args)
    {
        // Configure SSL.
        X509Bundle ssc = new CertificateBuilder()
                .Subject("cn=localhost")
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();
        SslContext sslCtx = SslContextBuilder.ForServer(ssc.ToKeyManagerFactory())
            .ApplicationProtocolConfig(new ApplicationProtocolConfig(
                        Protocol.ALPN,
                        // NO_ADVERTISE is currently the only mode supported by both OpenSsl and JDK providers.
                        SelectorFailureBehavior.NO_ADVERTISE,
                        // ACCEPT is currently the only mode supported by both OpenSsl and JDK providers.
                        SelectedListenerFailureBehavior.ACCEPT,
                        ApplicationProtocolNames.SPDY_3_1,
                        ApplicationProtocolNames.HTTP_1_1))
            .Build();

        // Configure the server.
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        try
        {
            ServerBootstrap b = new ServerBootstrap();
            b.Option(ChannelOption.SO_BACKLOG, 1024);
            b.Group(group)
             .Channel<NioServerSocketChannel>()
             .Handler(new LoggingHandler(LogLevel.INFO))
             .ChildHandler(new SpdyServerInitializer(sslCtx));

            IChannel ch = await b.BindAsync(PORT);

            Console.Error.WriteLine("Open your SPDY-enabled web browser and navigate to https://127.0.0.1:" + PORT + '/');
            Console.Error.WriteLine("If using Chrome browser, check your SPDY sessions at chrome://net-internals/#spdy");

            await ch.CloseFuture;
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
