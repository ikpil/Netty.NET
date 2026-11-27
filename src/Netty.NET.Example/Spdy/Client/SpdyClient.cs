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
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;
using Protocol = Netty.NET.Handler.Ssl.ApplicationProtocolConfig.Protocol;
using SelectedListenerFailureBehavior = Netty.NET.Handler.Ssl.ApplicationProtocolConfig.SelectedListenerFailureBehavior;
using SelectorFailureBehavior = Netty.NET.Handler.Ssl.ApplicationProtocolConfig.SelectorFailureBehavior;

namespace Netty.NET.Example.Spdy.Client;

/**
 * An SPDY client that allows you to send HTTP GET to a SPDY server.
 * <p>
 * You may also use the {@code run-example.sh} script to start the client from the command line:
 * <pre>
 *     ./run-example.sh spdy-client
 * </pre>
 */
// CLR: run-example.sh is the Maven example module's launcher; this port runs the example with ExampleLauncher
// ("spdy.client.SpdyClient"). System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments) and Integer.parseInt is int.Parse with an optional sign and the invariant culture.
// The SSL context is built by the JDK (SslStream) provider, which negotiates ALPN through SslStream's application
// protocols: OPENSSL and tcnative are library-not-ported (docs/handler-porting.md). The nested ApplicationProtocolConfig
// enums are named by using aliases, as the Java imports name them. main blocks on connect().syncUninterruptibly(),
// writeAndFlush(...).sync(), queue().take().sync() and close().syncUninterruptibly(); it is the async Main, which
// awaits those Tasks (docs/transport-porting.md: sync() is awaiting the Task; a Task wait has no interruption to
// ignore) and takes from the BlockingCollection on its own thread. NioSocketChannel.class is
// Channel<NioSocketChannel>(), Unpooled.EMPTY_BUFFER is Unpooled.EmptyBuffer, group.shutdownGracefully() is
// ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future, and System.out.println is
// Console.WriteLine.
public sealed class SpdyClient
{
    internal static readonly string HOST = ExampleLauncher.GetProperty("host", "127.0.0.1");
    internal static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", "8443"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    public static async Task Main(string[] args)
    {
        // Configure SSL.
        SslContext sslCtx = SslContextBuilder.ForClient()
            .TrustManager(InsecureTrustManagerFactory.INSTANCE)
            .ApplicationProtocolConfig(new ApplicationProtocolConfig(
                        Protocol.ALPN,
                        // NO_ADVERTISE is currently the only mode supported by both OpenSsl and JDK providers.
                        SelectorFailureBehavior.NO_ADVERTISE,
                        // ACCEPT is currently the only mode supported by both OpenSsl and JDK providers.
                        SelectedListenerFailureBehavior.ACCEPT,
                        ApplicationProtocolNames.SPDY_3_1,
                        ApplicationProtocolNames.HTTP_1_1))
            .Build();

        HttpResponseClientHandler httpResponseHandler = new HttpResponseClientHandler();
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        try
        {
            Bootstrap b = new Bootstrap();
            b.Group(group);
            b.Channel<NioSocketChannel>();
            b.Option(ChannelOption.SO_KEEPALIVE, true);
            b.RemoteAddress(HOST, PORT);
            b.Handler(new SpdyClientInitializer(sslCtx, httpResponseHandler));

            // Start the client.
            IChannel channel = await b.ConnectAsync();
            Console.WriteLine("Connected to " + HOST + ':' + PORT);

            // Create a GET request.
            IHttpRequest request = new DefaultFullHttpRequest(
                    HttpVersion.HTTP_1_1, HttpMethod.GET, "", Unpooled.EmptyBuffer);
            request.Headers().Set(HttpHeaderNames.HOST, HOST);
            request.Headers().Set(HttpHeaderNames.ACCEPT_ENCODING, HttpHeaderValues.GZIP);

            // Send the GET request.
            await channel.WriteAndFlushAsync(request);

            // Waits for the complete HTTP response
            await httpResponseHandler.Queue().Take();
            Console.WriteLine("Finished SPDY HTTP GET");

            // Wait until the connection is closed.
            await channel.CloseAsync();
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
