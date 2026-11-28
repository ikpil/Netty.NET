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
using Netty.NET.Handler.Codec.Http2;
using Netty.NET.Handler.Logging;
using Netty.NET.Handler.Ssl;
using Netty.NET.PkiTesting;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Handler.Ssl.ApplicationProtocolConfig;

namespace Netty.NET.Example.Http2.HelloWorld.Server;

/**
 * An HTTP/2 Server that responds to requests with a Hello World. Once started, you can test the
 * server with the example client.
 */
// CLR: System.getProperty is ExampleLauncher.GetProperty, which reads the -Dname=value arguments; Integer.parseInt is int.Parse with an optional sign and the invariant culture. main(String[]) blocks on
// bind(port).sync() and closeFuture().sync(); it is the async Main, which awaits the Task of BindAsync and the channel's
// CloseFuture (docs/transport-porting.md: sync() is awaiting the Task). group.shutdownGracefully() is
// ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future. NioServerSocketChannel.class is
// Channel<NioServerSocketChannel>(). OpenSsl (netty-tcnative) is library-not-ported (docs/handler-porting.md), so
// OpenSsl.isAlpnSupported() is SslProvider.IsAlpnSupported(SslProvider.OPENSSL), which answers false: the JDK provider
// (SslStream, which negotiates ALPN) is chosen. The cipher suites pass through Http2ExampleUtil.PlatformCiphers, as
// SslStream cannot restrict them on Windows and Android. The package-private static fields are internal.
public sealed class Http2Server
{

    internal static readonly bool SSL = ExampleLauncher.GetProperty("ssl") != null;

    internal static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", SSL? "8443" : "8080"),
            NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    public static async Task Main(string[] args)
    {
        // Configure SSL.
        SslContext sslCtx;

        if (SSL)
        {
            SslProvider provider = SslProvider.IsAlpnSupported(SslProvider.OPENSSL) ? SslProvider.OPENSSL : SslProvider.JDK;
            X509Bundle ssc = new CertificateBuilder()
                    .Subject("cn=localhost")
                    .SetIsCertificateAuthority(true)
                    .BuildSelfSigned();
            sslCtx = SslContextBuilder.ForServer(ssc.ToKeyManagerFactory())
                .SslProvider(provider)
                /* NOTE: the cipher filter may not include all ciphers required by the HTTP/2 specification.
                 * Please refer to the HTTP/2 specification for cipher requirements. */
                .Ciphers(Http2ExampleUtil.PlatformCiphers(Http2SecurityUtil.CIPHERS), SupportedCipherSuiteFilter.INSTANCE)
                .ApplicationProtocolConfig(new ApplicationProtocolConfig(
                    Protocol.ALPN,
                    // NO_ADVERTISE is currently the only mode supported by both OpenSsl and JDK providers.
                    SelectorFailureBehavior.NO_ADVERTISE,
                    // ACCEPT is currently the only mode supported by both OpenSsl and JDK providers.
                    SelectedListenerFailureBehavior.ACCEPT,
                    ApplicationProtocolNames.HTTP_2,
                    ApplicationProtocolNames.HTTP_1_1))
                .Build();
        }
        else
        {
            sslCtx = null;
        }

        // Configure the server.
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        try
        {
            ServerBootstrap b = new ServerBootstrap();
            b.Option(ChannelOption.SO_BACKLOG, 1024);
            b.Group(group)
             .Channel<NioServerSocketChannel>()
             .Handler(new LoggingHandler(LogLevel.INFO))
             .ChildHandler(new Http2ServerInitializer(sslCtx));

            IChannel ch = await b.BindAsync(PORT);

            Console.Error.WriteLine("Open your HTTP/2-enabled web browser and navigate to " +
                    (SSL? "https" : "http") + "://127.0.0.1:" + PORT + '/');

            await ch.CloseFuture;
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
