/*
 * Copyright 2020 The Netty Project
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
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.PkiTesting;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;

namespace Netty.NET.Handler.Codec.Http3.Example;

public sealed class Http3ServerExample
{
    private static readonly byte[] CONTENT = Encoding.ASCII.GetBytes("Hello World!\r\n");
    // A server port supplied as a positional argument must also be passed to the client with -Dport.
    internal static readonly int PORT = int.Parse(ExampleLauncher.GetProperty("port", "9999"),
            NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    private Http3ServerExample()
    {
    }

    public static async Task Main(string[] args)
    {
        int port;

        // Allow to pass in the port so we can also use it to run h3spec against
        if (args.Length == 1)
        {
            port = int.Parse(args[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }
        else
        {
            port = PORT;
        }

        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        X509Bundle cert = new CertificateBuilder()
                .Subject("cn=localhost")
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();
        QuicSslContext sslContext = QuicSslContextBuilder.ForServer(
                (AsymmetricAlgorithm) cert.GetKeyPair(), null, cert.GetCertificate())
                .ApplicationProtocols(Http3.SupportedApplicationProtocols()).Build();
        IChannelHandler codec = Http3.NewQuicServerCodecBuilder()
                .SslContext(sslContext)
                .MaxIdleTimeout(TimeSpan.FromMilliseconds(5000))
                .InitialMaxData(10000000)
                .InitialMaxStreamDataBidirectionalLocal(1000000)
                .InitialMaxStreamDataBidirectionalRemote(1000000)
                .InitialMaxStreamsBidirectional(100)
                .TokenHandler(InsecureQuicTokenHandler.INSTANCE)
                .Handler(new ActionChannelInitializer<IQuicChannel>(ch =>
                {
                    // Called for each connection
                    ch.Pipeline.AddLast(new Http3ServerConnectionHandler(
                            new ActionChannelInitializer<IQuicStreamChannel>(ch =>
                            {
                                // Called for each request-stream,
                                ch.Pipeline.AddLast(new RequestStreamHandler());
                            })));
                })).Build();

        try
        {
            // MsQuic owns the UDP socket. Binding the endpoint starts the QUIC listener.
            Bootstrap bs = new Bootstrap();
            IChannel channel = await bs.Group(group)
                    .Channel<QuicEndpointChannel>()
                    .Handler(codec)
                    .BindAsync(new IPEndPoint(IPAddress.Any, port));
            await channel.CloseFuture;
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    private sealed class RequestStreamHandler : Http3RequestStreamInboundHandler
    {
        protected override void ChannelRead(
                IChannelHandlerContext ctx, IHttp3HeadersFrame frame)
        {
            ReferenceCountUtil.Release(frame);
        }

        protected override void ChannelRead(
                IChannelHandlerContext ctx, IHttp3DataFrame frame)
        {
            ReferenceCountUtil.Release(frame);
        }

        protected override void ChannelInputClosed(IChannelHandlerContext ctx)
        {
            IHttp3HeadersFrame headersFrame = new DefaultHttp3HeadersFrame();
            headersFrame.Headers().Status(new StringCharSequence("404"));
            headersFrame.Headers().Add(new StringCharSequence("server"), new StringCharSequence("netty"));
            headersFrame.Headers().AddInt(new StringCharSequence("content-length"), CONTENT.Length);
            _ = ctx.WriteAsync(headersFrame);
            IQuicStreamChannel stream = (IQuicStreamChannel) ctx.Channel;
            // Close only the output side after the response write completes.
            _ = ctx.WriteAndFlushAsync(new DefaultHttp3DataFrame(
                            Unpooled.WrappedBuffer(CONTENT)))
                    .ContinueWith(_ => stream.ShutdownOutputAsync(), TaskScheduler.Default);
        }
    }
}
