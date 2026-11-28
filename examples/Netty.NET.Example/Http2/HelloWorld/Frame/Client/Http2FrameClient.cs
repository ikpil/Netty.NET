/*
 * Copyright 2020 The Netty Project
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
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http2;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Handler.Ssl.ApplicationProtocolConfig;

namespace Netty.NET.Example.Http2.HelloWorld.Frame.Client;

/**
 * An HTTP2 client that allows you to send HTTP2 frames to a server using the newer HTTP2
 * approach (via {@link io.netty.handler.codec.http2.Http2FrameCodec}).
 * When run from the command-line, sends a single HEADERS frame (with prior knowledge) to
 * the server configured at host:port/path.
 * You should include {@link io.netty.handler.codec.http2.Http2ClientUpgradeCodec} if the
 * HTTP/2 server you are hitting doesn't support h2c/prior knowledge.
 */
// CLR: System.getProperty is ExampleLauncher.GetProperty, which reads the -Dname=value arguments; Integer.parseInt is int.Parse with an optional sign and the invariant culture. main(String[]) blocks; it is
// the async Main: connect().syncUninterruptibly().channel() awaits ConnectAsync(), open().syncUninterruptibly().getNow()
// awaits OpenAsync() (as codec-http2's Http2MultiplexHandlerTest), and channel.close().syncUninterruptibly() awaits
// CloseAsync(). streamChannel.writeAndFlush(...) is WriteAndFlushAsync, whose Task is discarded as Java discards the
// future. The String values given to the Http2Headers setters are CharSequences in Java; they are wrapped in Common's
// StringCharSequence (docs/codec-http-porting.md, Shared CLR adaptations). group.shutdownGracefully() is
// ShutdownGracefullyAsync(), whose Task is discarded. NioSocketChannel.class is Channel<NioSocketChannel>(). OpenSsl
// (netty-tcnative) is library-not-ported (docs/handler-porting.md): SslProvider.IsAlpnSupported(SslProvider.OPENSSL)
// answers false, so the JDK provider (SslStream, which negotiates ALPN) is chosen. The cipher suites pass through
// Http2ExampleUtil.PlatformCiphers, as SslStream cannot restrict them on Windows and Android;
// InsecureTrustManagerFactory.INSTANCE is the RemoteCertificateValidationCallback that SslContextBuilder.TrustManager
// takes. The package-private static fields are internal; the private constructor of the final class is kept.
public sealed class Http2FrameClient
{

    internal static readonly bool SSL = ExampleLauncher.GetProperty("ssl") != null;
    internal static readonly string HOST = ExampleLauncher.GetProperty("host", "127.0.0.1");
    internal static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", SSL? "8443" : "8080"),
            NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    internal static readonly string PATH = ExampleLauncher.GetProperty("path", "/");

    private Http2FrameClient()
    {
    }

    public static async Task Main(string[] args)
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());

        // Configure SSL.
        SslContext sslCtx;

        if (SSL)
        {
            SslProvider provider =
                    SslProvider.IsAlpnSupported(SslProvider.OPENSSL)? SslProvider.OPENSSL : SslProvider.JDK;
            sslCtx = SslContextBuilder.ForClient()
                  .SslProvider(provider)
                  .Ciphers(Http2ExampleUtil.PlatformCiphers(Http2SecurityUtil.CIPHERS), SupportedCipherSuiteFilter.INSTANCE)
                  // you probably won't want to use this in production, but it is fine for this example:
                  .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                  .ApplicationProtocolConfig(new ApplicationProtocolConfig(
                          Protocol.ALPN,
                          SelectorFailureBehavior.NO_ADVERTISE,
                          SelectedListenerFailureBehavior.ACCEPT,
                          ApplicationProtocolNames.HTTP_2,
                          ApplicationProtocolNames.HTTP_1_1))
                  .Build();
        }
        else
        {
            sslCtx = null;
        }

        try
        {
            Bootstrap b = new Bootstrap();
            b.Group(group);
            b.Channel<NioSocketChannel>();
            b.Option(ChannelOption.SO_KEEPALIVE, true);
            b.RemoteAddress(HOST, PORT);
            b.Handler(new Http2ClientFrameInitializer(sslCtx));

            // Start the client.
            IChannel channel = await b.ConnectAsync();
            Console.WriteLine("Connected to [" + HOST + ':' + PORT + ']');

            Http2ClientStreamFrameResponseHandler streamFrameResponseHandler =
                    new Http2ClientStreamFrameResponseHandler();

            Http2StreamChannelBootstrap streamChannelBootstrap = new Http2StreamChannelBootstrap(channel);
            IHttp2StreamChannel streamChannel = await streamChannelBootstrap.OpenAsync();
            streamChannel.Pipeline.AddLast(streamFrameResponseHandler);

            // Send request (a HTTP/2 HEADERS frame - with ':method = GET' in this case)
            DefaultHttp2Headers headers = new DefaultHttp2Headers();
            headers.Method(new StringCharSequence("GET"));
            headers.Path(new StringCharSequence(PATH));
            headers.Scheme(new StringCharSequence(SSL? "https" : "http"));
            IHttp2HeadersFrame headersFrame = new DefaultHttp2HeadersFrame(headers, true);
            _ = streamChannel.WriteAndFlushAsync(headersFrame);
            Console.WriteLine("Sent HTTP/2 GET request to " + PATH);

            // Wait for the responses (or for the latch to expire), then clean up the connections
            if (!streamFrameResponseHandler.ResponseSuccessfullyCompleted())
            {
                Console.Error.WriteLine("Did not get HTTP/2 response in expected time.");
            }

            Console.WriteLine("Finished HTTP/2 request, will close the connection.");

            // Wait until the connection is closed.
            await channel.CloseAsync();
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

}
