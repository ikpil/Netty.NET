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
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Http.Cookie;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;
using ClientCookieEncoder = Netty.NET.Handler.Codec.Http.Cookie.ClientCookieEncoder;

namespace Netty.NET.Example.Http.Snoop;

/**
 * A simple HTTP client that prints out the content of the HTTP response to
 * {@link System#out} to test {@link HttpSnoopServer}.
 */
// CLR: java.net.URI is System.Uri. A Uri is always absolute here (new Uri(string) rejects a relative reference), so
// getScheme() and getHost() keep the source's fallbacks for a missing (null or empty) value; getPort() is -1 for a URI
// without an explicit port, which is a Uri whose IsDefaultPort is true, and getRawPath() is AbsolutePath (the escaped
// path). main blocks on connect(...).sync() and closeFuture().sync(); it is the async Main, which awaits the
// Task<IChannel> of ConnectAsync (Channel ch is its result) and the channel's CloseFuture (docs/transport-porting.md:
// sync() is awaiting the Task). ch.writeAndFlush returns a Task, discarded as Java discards the future, and
// group.shutdownGracefully() is ShutdownGracefullyAsync(). The SSL context is built by the JDK (SslStream) provider:
// OPENSSL and tcnative are library-not-ported (docs/handler-porting.md). The deprecated
// io.netty.handler.codec.http.ClientCookieEncoder shares the simple name, so the cookie package's encoder is named by a
// using alias. String.equalsIgnoreCase is an OrdinalIgnoreCase comparison and System.err.println is
// Console.Error.WriteLine.
public sealed class HttpSnoopClient
{
    internal static readonly string URL = ExampleLauncher.GetProperty("url", "http://127.0.0.1:8080/");

    public static async Task Main(string[] args)
    {
        Uri uri = new Uri(URL);
        string scheme = string.IsNullOrEmpty(uri.Scheme)? "http" : uri.Scheme;
        string host = string.IsNullOrEmpty(uri.Host)? "127.0.0.1" : uri.Host;
        int port = uri.IsDefaultPort ? -1 : uri.Port;
        if (port == -1)
        {
            if ("http".Equals(scheme, StringComparison.OrdinalIgnoreCase))
            {
                port = 80;
            }
            else if ("https".Equals(scheme, StringComparison.OrdinalIgnoreCase))
            {
                port = 443;
            }
        }

        if (!"http".Equals(scheme, StringComparison.OrdinalIgnoreCase) && !"https".Equals(scheme, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Only HTTP(S) is supported.");
            return;
        }

        // Configure SSL context if necessary.
        bool ssl = "https".Equals(scheme, StringComparison.OrdinalIgnoreCase);
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

        // Configure the client.
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
        try
        {
            Bootstrap b = new Bootstrap();
            b.Group(group)
             .Channel<NioSocketChannel>()
             .Handler(new HttpSnoopClientInitializer(sslCtx));

            // Make the connection attempt.
            IChannel ch = await b.ConnectAsync(host, port);

            // Prepare the HTTP request.
            IHttpRequest request = new DefaultFullHttpRequest(
                    HttpVersion.HTTP_1_1, HttpMethod.GET, uri.AbsolutePath, Unpooled.EmptyBuffer);
            request.Headers().Set(HttpHeaderNames.HOST, host);
            request.Headers().Set(HttpHeaderNames.CONNECTION, HttpHeaderValues.CLOSE);
            request.Headers().Set(HttpHeaderNames.ACCEPT_ENCODING, HttpHeaderValues.GZIP);

            // Set some example cookies.
            request.Headers().Set(
                    HttpHeaderNames.COOKIE,
                    ClientCookieEncoder.STRICT.Encode(
                            new DefaultCookie("my-cookie", "foo"),
                            new DefaultCookie("another-cookie", "bar")));

            // Send the HTTP request.
            _ = ch.WriteAndFlushAsync(request);

            // Wait for the server to close the connection.
            await ch.CloseFuture;
        }
        finally
        {
            // Shut down executor threads to exit.
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
