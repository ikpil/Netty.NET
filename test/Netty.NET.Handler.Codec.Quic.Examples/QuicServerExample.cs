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
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.PkiTesting;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;

namespace Netty.NET.Handler.Codec.Quic.Example;

// CLR: main is the async Main of ExampleLauncher (CLR-only); bind(...).sync().channel() and closeFuture().sync() are
// awaits, and group.shutdownGracefully() is ShutdownGracefullyAsync() with the Task discarded. NioDatagramChannel.class
// is Channel<QuicEndpointChannel>(): msquic owns the UDP socket, so the codec runs on the QuicEndpointChannel that takes
// the datagram channel's place (owner decision 2026-11-27, docs/codec-classes-quic-porting.md, Stage 2), and binding it
// starts the QuicListener. new InetSocketAddress(9999) is the wildcard IPEndPoint(IPAddress.Any, 9999) (Transport
// convention). SelfSignedCertificate is @Deprecated and not ported (handler, deprecated-not-ported); its key and
// certificate come from the PkiTesting port (CertificateBuilder, as Netty.NET.Example's ServerUtil does), whose ECDSA
// P-256 key pair is the AsymmetricAlgorithm of X509Bundle.GetKeyPair(). maxIdleTimeout(5000, MILLISECONDS) takes a
// TimeSpan, the anonymous handlers are the private nested classes below and the anonymous
// ChannelInitializer<QuicStreamChannel> is an ActionChannelInitializer<IQuicStreamChannel>. collectStats().addListener
// is a continuation of CollectStatsAsync(), which System.Net.Quic cannot serve (NotSupportedException), so nothing is
// logged and the listener still closes the parent; QuicStreamChannel.SHUTDOWN_OUTPUT is a continuation that calls
// ShutdownOutputAsync(). CharsetUtil.US_ASCII is Encoding.ASCII.
public sealed class QuicServerExample
{

    private static readonly IInternalLogger LOGGER = InternalLoggerFactory.GetInstance(typeof(QuicServerExample));

    private QuicServerExample()
    {
    }

    public static async Task Main(string[] args)
    {
        X509Bundle selfSignedCertificate = new CertificateBuilder()
                .Subject("cn=localhost")
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();
        QuicSslContext context = QuicSslContextBuilder.ForServer(
                (AsymmetricAlgorithm) selfSignedCertificate.GetKeyPair(), null, selfSignedCertificate.GetCertificate())
                .ApplicationProtocols("http/0.9").Build();
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        IChannelHandler codec = new QuicServerCodecBuilder().SslContext(context)
                .MaxIdleTimeout(TimeSpan.FromMilliseconds(5000))
                // Configure some limits for the maximal number of streams (and the data) that we want to handle.
                .InitialMaxData(10000000)
                .InitialMaxStreamDataBidirectionalLocal(1000000)
                .InitialMaxStreamDataBidirectionalRemote(1000000)
                .InitialMaxStreamsBidirectional(100)
                .InitialMaxStreamsUnidirectional(100)
                .ActiveMigration(true)

                // Setup a token handler. In a production system you would want to implement and provide your custom
                // one.
                .TokenHandler(InsecureQuicTokenHandler.INSTANCE)
                // ChannelHandler that is added into QuicChannel pipeline.
                .Handler(new ConnectionHandler())
                .StreamHandler(new ActionChannelInitializer<IQuicStreamChannel>(ch =>
                {
                    // Add a LineBasedFrameDecoder here as we just want to do some simple HTTP 0.9 handling.
                    ch.Pipeline.AddLast(new LineBasedFrameDecoder(1024))
                            .AddLast(new HelloWorldHandler());
                })).Build();
        try
        {
            Bootstrap bs = new Bootstrap();
            IChannel channel = await bs.Group(group)
                    .Channel<QuicEndpointChannel>()
                    .Handler(codec)
                    .BindAsync(new IPEndPoint(IPAddress.Any, 9999));
            await channel.CloseFuture;
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    // The anonymous ChannelHandler that is added into the QuicChannel pipeline.
    private sealed class ConnectionHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            IQuicChannel channel = (IQuicChannel) ctx.Channel;
            // Create streams etc..
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            _ = ((IQuicChannel) ctx.Channel).CollectStatsAsync().ContinueWith(f =>
            {
                if (f.IsCompletedSuccessfully)
                {
                    LOGGER.Info("Connection closed: {}", f.Result);
                }
                //only test for first connection
                ctx.Channel.Parent.CloseAsync();
            }, TaskScheduler.Default);
        }

        public override bool IsSharable => true;
    }

    // The anonymous stream handler after the LineBasedFrameDecoder.
    private sealed class HelloWorldHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ByteBuf byteBuf = (ByteBuf) msg;
            try
            {
                if (byteBuf.ToString(Encoding.ASCII).Trim().Equals("GET /"))
                {
                    ByteBuf buffer = ctx.Alloc().DirectBuffer();
                    buffer.WriteCharSequence("Hello World!\r\n", Encoding.ASCII);
                    // Write the buffer and shutdown the output by writing a FIN.
                    IQuicStreamChannel stream = (IQuicStreamChannel) ctx.Channel;
                    _ = ctx.WriteAndFlushAsync(buffer).ContinueWith(_ => stream.ShutdownOutputAsync(),
                            TaskScheduler.Default);
                }
            }
            finally
            {
                byteBuf.Release();
            }
        }
    }
}
