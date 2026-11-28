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
using Netty.NET.Example.Util;
using Netty.NET.Handler.Logging;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Example.Http.Cors;

/**
 * This example server aims to demonstrate
 * <a href="https://www.w3.org/TR/cors/">Cross Origin Resource Sharing</a> (CORS) in Netty.
 * It does not have a client like most of the other examples, but instead has
 * a html page that is loaded to try out CORS support in a web browser.
 * <p>
 *
 * CORS is configured in {@link HttpCorsServerInitializer} and by updating the config you can
 * try out various combinations, like using a specific origin instead of a
 * wildcard origin ('*').
 * <p>
 *
 * The file {@code src/main/resources/cors/cors.html} contains a very basic example client
 * which can be used to try out different configurations. For example, you can add
 * custom headers to force a CORS preflight request to make the request fail. Then
 * to enable a successful request, configure the CorsHandler to allow that/those
 * request headers.
 *
 * <h2>Testing CORS</h2>
 * You can either load the file {@code src/main/resources/cors/cors.html} using a web server
 * or load it from the file system using a web browser.
 *
 * <h3>Using a web server</h3>
 * To test CORS support you can serve the file {@code src/main/resources/cors/cors.html}
 * using a web server. You can then add a new host name to your systems hosts file, for
 * example if you are on Linux you may update /etc/hosts to add an additional name
 * for you local system:
 * <pre>
 * 127.0.0.1   localhost domain1.com
 * </pre>
 * Now, you should be able to access {@code http://domain1.com/cors.html} depending on how you
 * have configured you local web server the exact url may differ.
 *
 * <h3>Using a web browser</h3>
 * Open the file {@code src/main/resources/cors/cors.html} in a web browser. You should see
 * loaded page and in the text area the following message:
 * <pre>
 * 'CORS is not working'
 * </pre>
 *
 * If you inspect the headers being sent using your browser you'll see that the 'Origin'
 * request header is {@code 'null'}. This is expected and happens when you load a file from the
 * local file system. Netty can handle this by configuring the CorsHandler which is done
 * in the {@link HttpCorsServerInitializer}.
 *
 */
// CLR: the Maven resource src/main/resources/cors/cors.html (and css/cors.css) is the byte-identical copy
// src/Netty.NET.Example/Resources/cors/cors.html (and Resources/cors/css/cors.css), which the project copies next to
// the assembly (Resources/cors/... in the output directory). As in Java, no code loads it: it is opened in a browser or
// served by a web server. System.getProperty is ExampleLauncher.GetProperty (the -Dname=value arguments) and Integer.parseInt is int.Parse with an optional sign and the invariant culture.
// main blocks on bind(PORT).sync() and closeFuture().sync(); it is the async Main, which awaits the Task<IChannel> of
// BindAsync and the channel's CloseFuture (docs/transport-porting.md: sync() is awaiting the Task).
// NioServerSocketChannel.class is Channel<NioServerSocketChannel>() and group.shutdownGracefully() is
// ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future.
public sealed class HttpCorsServer
{
    internal static readonly bool SSL = ExampleLauncher.GetProperty("ssl") != null;
    internal static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", SSL? "8443" : "8080"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    public static async Task Main(string[] args)
    {
        // Configure SSL.
        SslContext sslCtx = ServerUtil.BuildSslContext();

        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        try
        {
            ServerBootstrap b = new ServerBootstrap();
            b.Group(group)
             .Channel<NioServerSocketChannel>()
             .Handler(new LoggingHandler(LogLevel.INFO))
             .ChildHandler(new HttpCorsServerInitializer(sslCtx));

            await (await b.BindAsync(PORT)).CloseFuture;
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
