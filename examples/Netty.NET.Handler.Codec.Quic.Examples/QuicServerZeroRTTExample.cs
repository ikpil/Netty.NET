/*
 * Copyright 2021 The Netty Project
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

// CLR: ported as written, with the adaptations of QuicServerExample (the async Main of ExampleLauncher, the
// QuicEndpointChannel in place of NioDatagramChannel, IPEndPoint(IPAddress.Any, 9999), the PkiTesting certificate
// in place of the deprecated SelfSignedCertificate, the nested handler classes, the SHUTDOWN_OUTPUT continuation
// and Encoding.ASCII). System.Net.Quic exposes no 0-RTT early data, so QuicSslContextBuilder.EarlyData(true) makes
// Build() throw NotSupportedException : run, the example ends with that exception before it binds.
// tokenHandler(null) is accepted (msquic validates addresses with its own retry tokens).
public sealed class QuicServerZeroRTTExample
{

    private static readonly IInternalLogger LOGGER = InternalLoggerFactory.GetInstance(typeof(QuicServerZeroRTTExample));

    private QuicServerZeroRTTExample()
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
                .ApplicationProtocols("http/0.9").EarlyData(true).Build();
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        IChannelHandler codec = new QuicServerCodecBuilder().SslContext(context)
                .MaxIdleTimeout(TimeSpan.FromMilliseconds(5000))
                // Configure some limits for the maximal number of streams (and the data) that we want to handle.
                .InitialMaxData(10000000)
                .InitialMaxStreamDataBidirectionalLocal(1000000)
                .InitialMaxStreamDataBidirectionalRemote(1000000)
                .InitialMaxStreamsBidirectional(100)
                .InitialMaxStreamsUnidirectional(100)

                // Disable token usage. In a production system you would want to implement and provide your custom
                // one.
                .TokenHandler(null)
                // ChannelHandler that is added into QuicChannel pipeline.
                .Handler(new ConnectionHandler())
                .StreamHandler(new ActionChannelInitializer<IQuicStreamChannel>(ch =>
                {
                    // Add a LineBasedFrameDecoder here as we just want to do some simple HTTP 0.9 handling.
                    ch.Pipeline.AddLast(new LineBasedFrameDecoder(1024))
                            .AddLast(new ByeHandler());
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
            }, TaskScheduler.Default);
        }

        public override bool IsSharable => true;
    }

    // The anonymous stream handler after the LineBasedFrameDecoder.
    private sealed class ByeHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ByteBuf byteBuf = (ByteBuf) msg;

            try
            {
                if (byteBuf.ToString(Encoding.ASCII).Trim().Equals("Bye"))
                {
                    ByteBuf buffer = ctx.Alloc().DirectBuffer();
                    buffer.WriteCharSequence("Bye\r\n", Encoding.ASCII);
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
