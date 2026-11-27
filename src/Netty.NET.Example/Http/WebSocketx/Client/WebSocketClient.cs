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
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Http.WebSocketx.Client;

/**
 * This is an example of a WebSocket client.
 * <p>
 * In order to run this example you need a compatible WebSocket server.
 * Therefore you can either start the WebSocket server from the examples
 * by running {@link io.netty.example.http.websocketx.server.WebSocketServer}
 * or connect to an existing WebSocket server such as
 * <a href="https://www.websocket.org/echo.html">ws://echo.websocket.org</a>.
 * <p>
 * The client will attempt to connect to the URI passed to it as the first argument.
 * You don't have to specify any arguments if you want to connect to the example WebSocket server,
 * as this is the default.
 */
// CLR adaptations:
// - java.net.URI is System.Uri. A Uri is always absolute here (new Uri(string) rejects a relative reference), so
//   getScheme() and getHost() keep the source's fallbacks for a missing (null or empty) value; getPort() is -1 for a URI
//   without an explicit port, which is a Uri whose IsDefaultPort is true.
// - main blocks on connect(...).sync(), handshakeFuture().sync() and closeFuture().sync(); it is the async Main, which
//   awaits those Tasks (docs/transport-porting.md: sync() is awaiting the Task); the handshake Task completes its
//   awaiters asynchronously (WebSocketClientHandler), so the console loop does not run on the event loop.
//   ch.writeAndFlush returns a Task, discarded as Java discards the future, and group.shutdownGracefully() is
//   ShutdownGracefullyAsync().
// - The anonymous ChannelInitializer<SocketChannel> is Transport's ActionChannelInitializer<ISocketChannel>
//   (Netty.NET.Transport.Bootstrapping), whose delegate is the initChannel body.
// - BufferedReader(InputStreamReader(System.in)).readLine() is Console.In.ReadLine(), which also returns null at the end
//   of the input. String.toLowerCase() lower-cases with the default locale, which is ToLower(CultureInfo.CurrentCulture).
// - The SSL context is built by the JDK (SslStream) provider: OPENSSL and tcnative are library-not-ported
//   (docs/handler-porting.md). String.equalsIgnoreCase is an OrdinalIgnoreCase comparison and System.err.println is
//   Console.Error.WriteLine.
public sealed class WebSocketClient
{
    internal static readonly string URL = ExampleLauncher.GetProperty("url", "ws://127.0.0.1:8080/websocket");
    internal const int MAX_CONTENT_LENGTH = 8192;

    public static async Task Main(string[] args)
    {
        Uri uri = new Uri(URL);
        string scheme = string.IsNullOrEmpty(uri.Scheme)? "ws" : uri.Scheme;
        string host = string.IsNullOrEmpty(uri.Host)? "127.0.0.1" : uri.Host;
        int port;
        if (uri.IsDefaultPort)
        {
            if ("ws".Equals(scheme, StringComparison.OrdinalIgnoreCase))
            {
                port = 80;
            }
            else if ("wss".Equals(scheme, StringComparison.OrdinalIgnoreCase))
            {
                port = 443;
            }
            else
            {
                port = -1;
            }
        }
        else
        {
            port = uri.Port;
        }

        if (!"ws".Equals(scheme, StringComparison.OrdinalIgnoreCase) && !"wss".Equals(scheme, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Only WS(S) is supported.");
            return;
        }

        bool ssl = "wss".Equals(scheme, StringComparison.OrdinalIgnoreCase);
        SslContext sslCtx;
        if (ssl)
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
            // Connect with V13 (RFC 6455 aka HyBi-17). You can change it to V08 or V00.
            // If you change it to V00, ping is not supported and remember to change
            // HttpResponseDecoder to WebSocketHttpResponseDecoder in the pipeline.
            WebSocketClientHandler handler =
                    new WebSocketClientHandler(
                            WebSocketClientHandshakerFactory.NewHandshaker(
                                    uri, WebSocketVersion.V13, null, true, new DefaultHttpHeaders()));

            Bootstrap b = new Bootstrap();
            b.Group(group)
             .Channel<NioSocketChannel>()
             .Handler(new ActionChannelInitializer<ISocketChannel>(ch =>
             {
                 IChannelPipeline p = ch.Pipeline;
                 if (sslCtx != null)
                 {
                     p.AddLast(sslCtx.NewHandler(ch.Alloc(), host, port));
                 }
                 p.AddLast(
                         new HttpClientCodec(),
                         new HttpObjectAggregator(MAX_CONTENT_LENGTH),
                         new WebSocketClientCompressionHandler(MAX_CONTENT_LENGTH),
                         handler);
             }));

            IChannel ch = await b.ConnectAsync(uri.Host, port);
            await handler.HandshakeFuture();

            TextReader console = Console.In;
            while (true)
            {
                string msg = console.ReadLine();
                if (msg == null)
                {
                    break;
                }
                else if ("bye".Equals(msg.ToLower(CultureInfo.CurrentCulture)))
                {
                    _ = ch.WriteAndFlushAsync(new CloseWebSocketFrame());
                    await ch.CloseFuture;
                    break;
                }
                else if ("ping".Equals(msg.ToLower(CultureInfo.CurrentCulture)))
                {
                    WebSocketFrame frame = new PingWebSocketFrame(Unpooled.WrappedBuffer(new byte[] { 8, 1, 8, 1 }));
                    _ = ch.WriteAndFlushAsync(frame);
                }
                else
                {
                    WebSocketFrame frame = new TextWebSocketFrame(msg);
                    _ = ch.WriteAndFlushAsync(frame);
                }
            }
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
