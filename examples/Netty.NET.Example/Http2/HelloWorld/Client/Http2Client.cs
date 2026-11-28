/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */
using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Codec.Http2;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Buffer.Unpooled;
using static Netty.NET.Handler.Codec.Http.HttpMethod;
using static Netty.NET.Handler.Codec.Http.HttpVersion;
using static Netty.NET.Handler.Ssl.ApplicationProtocolConfig;

namespace Netty.NET.Example.Http2.HelloWorld.Client;

/**
 * An HTTP2 client that allows you to send HTTP2 frames to a server using HTTP1-style approaches
 * (via {@link io.netty.handler.codec.http2.InboundHttp2ToHttpAdapter}). Inbound and outbound
 * frames are logged.
 * When run from the command-line, sends a single HEADERS frame to the server and gets back
 * a "Hello World" response.
 * See the ./http2/helloworld/frame/client/ example for a HTTP2 client example which does not use
 * HTTP1-style objects and patterns.
 */
// CLR: System.getProperty is ExampleLauncher.GetProperty, which reads the -Dname=value arguments; Integer.parseInt is int.Parse with an optional sign and the invariant culture. main(String[]) blocks; it is
// the async Main: connect().syncUninterruptibly().channel() awaits ConnectAsync(), awaitSettings/awaitResponses are the
// awaited AwaitSettingsAsync/AwaitResponsesAsync with a 5 second TimeSpan, and channel.close().syncUninterruptibly() awaits
// CloseAsync(). channel.write(request) is WriteAsync and channel.newPromise() a TaskCompletionSource whose continuations
// run asynchronously (the response handler completes it on the event loop). group.shutdownGracefully() is
// ShutdownGracefullyAsync(), whose Task is discarded as Java discards the future. NioSocketChannel.class is
// Channel<NioSocketChannel>(). OpenSsl (netty-tcnative) is library-not-ported (docs/handler-porting.md), so
// OpenSsl.isAlpnSupported() is SslProvider.IsAlpnSupported(SslProvider.OPENSSL), which answers false: the JDK provider
// (SslStream, which negotiates ALPN) is chosen. The cipher suites pass through Http2ExampleUtil.PlatformCiphers, as
// SslStream cannot restrict them on Windows and Android; InsecureTrustManagerFactory.INSTANCE is the
// RemoteCertificateValidationCallback that SslContextBuilder.TrustManager takes. URL2DATA.getBytes(UTF_8) is
// Encoding.UTF8.GetBytes and Unpooled.EMPTY_BUFFER is Unpooled.EmptyBuffer. The package-private static fields are internal.
public sealed class Http2Client
{

    internal static readonly bool SSL = ExampleLauncher.GetProperty("ssl") != null;
    internal static readonly string HOST = ExampleLauncher.GetProperty("host", "127.0.0.1");
    internal static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", SSL? "8443" : "8080"),
            NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    internal static readonly string URL = ExampleLauncher.GetProperty("url", "/whatever");
    internal static readonly string URL2 = ExampleLauncher.GetProperty("url2");
    internal static readonly string URL2DATA = ExampleLauncher.GetProperty("url2data", "test data!");

    public static async Task Main(string[] args)
    {
        // Configure SSL.
        SslContext sslCtx;

        if (SSL)
        {
            SslProvider provider = SslProvider.IsAlpnSupported(SslProvider.OPENSSL) ? SslProvider.OPENSSL : SslProvider.JDK;
            sslCtx = SslContextBuilder.ForClient()
                .SslProvider(provider)
                /* NOTE: the cipher filter may not include all ciphers required by the HTTP/2 specification.
                 * Please refer to the HTTP/2 specification for cipher requirements. */
                .Ciphers(Http2ExampleUtil.PlatformCiphers(Http2SecurityUtil.CIPHERS), SupportedCipherSuiteFilter.INSTANCE)
                .TrustManager(InsecureTrustManagerFactory.INSTANCE)
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

        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
        Http2ClientInitializer initializer = new Http2ClientInitializer(sslCtx, int.MaxValue);

        try
        {
            // Configure the client.
            Bootstrap b = new Bootstrap();
            b.Group(group);
            b.Channel<NioSocketChannel>();
            b.Option(ChannelOption.SO_KEEPALIVE, true);
            b.RemoteAddress(HOST, PORT);
            b.Handler(initializer);

            // Start the client.
            IChannel channel = await b.ConnectAsync();
            Console.WriteLine("Connected to [" + HOST + ':' + PORT + ']');

            // Wait for the HTTP/2 upgrade to occur.
            Http2SettingsHandler http2SettingsHandler = initializer.SettingsHandler();
            await http2SettingsHandler.AwaitSettingsAsync(TimeSpan.FromSeconds(5));

            HttpResponseHandler responseHandler = initializer.ResponseHandler();
            int streamId = 3;
            HttpScheme scheme = SSL ? HttpScheme.HTTPS : HttpScheme.HTTP;
            AsciiString hostName = new AsciiString(HOST + ':' + PORT);
            Console.Error.WriteLine("Sending request(s)...");

            if (URL != null)
            {
                // Create a simple GET request.
                IFullHttpRequest request = new DefaultFullHttpRequest(HTTP_1_1, GET, URL, EmptyBuffer);
                request.Headers().Add(HttpHeaderNames.HOST, hostName);
                request.Headers().Add(HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text(), scheme.Name());
                request.Headers().Add(HttpHeaderNames.ACCEPT_ENCODING, HttpHeaderValues.GZIP);
                request.Headers().Add(HttpHeaderNames.ACCEPT_ENCODING, HttpHeaderValues.DEFLATE);
                responseHandler.Put(streamId, channel.WriteAsync(request),
                        new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
                streamId += 2;
            }

            if (URL2 != null)
            {
                // Create a simple POST request with a body.
                IFullHttpRequest request = new DefaultFullHttpRequest(HTTP_1_1, POST, URL2,
                        WrappedBuffer(Encoding.UTF8.GetBytes(URL2DATA)));
                request.Headers().Add(HttpHeaderNames.HOST, hostName);
                request.Headers().Add(HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text(), scheme.Name());
                request.Headers().Add(HttpHeaderNames.ACCEPT_ENCODING, HttpHeaderValues.GZIP);
                request.Headers().Add(HttpHeaderNames.ACCEPT_ENCODING, HttpHeaderValues.DEFLATE);
                responseHandler.Put(streamId, channel.WriteAsync(request),
                        new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            }

            channel.Flush();
            await responseHandler.AwaitResponsesAsync(TimeSpan.FromSeconds(5));
            Console.WriteLine("Finished HTTP/2 request(s)");

            // Wait until the connection is closed.
            await channel.CloseAsync();
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
