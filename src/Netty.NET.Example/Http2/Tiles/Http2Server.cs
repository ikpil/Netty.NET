/*
 * Copyright 2015 The Netty Project
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
using Netty.NET.PkiTesting;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Handler.Codec.Http2.Http2SecurityUtil;
using static Netty.NET.Handler.Ssl.ApplicationProtocolConfig;

namespace Netty.NET.Example.Http2.Tiles;

/**
 * Demonstrates an Http2 server using Netty to display a bunch of images and
 * simulate latency. It is a Netty version of the <a href="https://http2.golang.org/gophertiles?latency=0">
 * Go lang HTTP2 tiles demo</a>.
 */
// CLR: System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments) and Integer.parseInt is int.Parse with an
// optional sign and the invariant culture. start() blocks on bind(PORT).sync() and returns the channel's closeFuture();
// it is StartAsync(), which awaits BindAsync and returns the channel's CloseFuture Task (ChannelFuture is Task,
// docs/transport-porting.md). EventLoopGroup is IEventLoopGroup, SocketChannel ISocketChannel and
// NioServerSocketChannel.class Channel<NioServerSocketChannel>(); the anonymous ChannelInitializer<SocketChannel> is an
// ActionChannelInitializer<ISocketChannel> (Netty.NET.Transport.Bootstrapping) that captures sslCtx as the anonymous
// class does. The cipher suites pass through Http2ExampleUtil.PlatformCiphers, as SslStream cannot restrict them on
// Windows and Android.
public class Http2Server
{

    public static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("http2-port", "8443"),
            NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    private readonly IEventLoopGroup group;

    public Http2Server(IEventLoopGroup eventLoopGroup)
    {
        group = eventLoopGroup;
    }

    public async Task<Task> StartAsync()
    {
        SslContext sslCtx = ConfigureTLS();
        ServerBootstrap b = new ServerBootstrap();
        b.Option(ChannelOption.SO_BACKLOG, 1024);
        b.Group(group).Channel<NioServerSocketChannel>().ChildHandler(new ActionChannelInitializer<ISocketChannel>(ch =>
        {
            ch.Pipeline.AddLast(sslCtx.NewHandler(ch.Alloc()), new Http2OrHttpHandler());
        }));

        IChannel ch = await b.BindAsync(PORT);
        return ch.CloseFuture;
    }

    private static SslContext ConfigureTLS()
    {
        X509Bundle ssc = new CertificateBuilder()
                .Subject("cn=localhost")
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();
        ApplicationProtocolConfig apn = new ApplicationProtocolConfig(
                Protocol.ALPN,
                // NO_ADVERTISE is currently the only mode supported by both OpenSsl and JDK providers.
                SelectorFailureBehavior.NO_ADVERTISE,
                // ACCEPT is currently the only mode supported by both OpenSsl and JDK providers.
                SelectedListenerFailureBehavior.ACCEPT,
                ApplicationProtocolNames.HTTP_2,
                ApplicationProtocolNames.HTTP_1_1);

        return SslContextBuilder.ForServer(ssc.ToKeyManagerFactory())
                                .Ciphers(Http2ExampleUtil.PlatformCiphers(CIPHERS), SupportedCipherSuiteFilter.INSTANCE)
                                .ApplicationProtocolConfig(apn).Build();
    }
}
