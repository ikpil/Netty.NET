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
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Codec.Quic.Example;

// CLR: main is the async Main of ExampleLauncher (CLR-only); bind(0).sync().channel(), connect().get(),
// createStream(...).sync().getNow(), closeFuture().sync() and close().sync() are awaits, and group.shutdownGracefully()
// is ShutdownGracefullyAsync() with the Task discarded. NioDatagramChannel.class is Channel<QuicEndpointChannel>(): msquic
// owns the UDP socket, so the codec runs on the QuicEndpointChannel that takes the datagram channel's place (owner
// decision 2026-11-27, docs/codec-classes-quic-porting.md, Stage 2), and QuicChannel.newBootstrap(channel) is
// IQuicChannel.NewBootstrap(channel). InsecureTrustManagerFactory.INSTANCE is the RemoteCertificateValidationCallback of
// the handler port, maxIdleTimeout(5000, MILLISECONDS) takes a TimeSpan, the anonymous handlers are the private nested
// classes below and QuicStreamChannel.SHUTDOWN_OUTPUT is a continuation that calls ShutdownOutputAsync(). System.err is
// Console.Error and CharsetUtil.US_ASCII Encoding.ASCII. close(true, 0, reason) is CloseAsync(true, 0, reason):
// System.Net.Quic sends no reason phrase, so the reason buffer is released unused.
public sealed class QuicClientExample
{

    private QuicClientExample()
    {
    }

    public static async Task Main(string[] args)
    {
        QuicSslContext context = QuicSslContextBuilder.ForClient().TrustManager(InsecureTrustManagerFactory.INSTANCE).
                ApplicationProtocols("http/0.9").Build();
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        try
        {
            IChannelHandler codec = new QuicClientCodecBuilder()
                    .SslContext(context)
                    .MaxIdleTimeout(TimeSpan.FromMilliseconds(5000))
                    .InitialMaxData(10000000)
                    // As we don't want to support remote initiated streams just setup the limit for local initiated
                    // streams in this example.
                    .InitialMaxStreamDataBidirectionalLocal(1000000)
                    .Build();

            Bootstrap bs = new Bootstrap();
            IChannel channel = await bs.Group(group)
                    .Channel<QuicEndpointChannel>()
                    .Handler(codec)
                    .BindAsync(0);

            IQuicChannel quicChannel = await IQuicChannel.NewBootstrap(channel)
                    .StreamHandler(new RemoteStreamHandler())
                    .RemoteAddress(new IPEndPoint(NetUtil.LOCALHOST4, 9999))
                    .ConnectAsync();

            IQuicStreamChannel streamChannel = await quicChannel.CreateStreamAsync(QuicStreamType.BIDIRECTIONAL,
                    new ResponseHandler());
            // Write the data and send the FIN. After this its not possible anymore to write any more data.
            _ = streamChannel.WriteAndFlushAsync(Unpooled.CopiedBuffer("GET /\r\n", Encoding.ASCII))
                    .ContinueWith(_ => streamChannel.ShutdownOutputAsync(), TaskScheduler.Default);

            // Wait for the stream channel and quic channel to be closed (this will happen after we received the FIN).
            // After this is done we will close the underlying datagram channel.
            await streamChannel.CloseFuture;
            await quicChannel.CloseFuture;
            await channel.CloseAsync();
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    // The anonymous stream handler of the QuicChannel bootstrap (remote initiated streams).
    private sealed class RemoteStreamHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            // As we did not allow any remote initiated streams we will never see this method called.
            // That said just let us keep it here to demonstrate that this handle would be called
            // for each remote initiated stream.
            ctx.CloseAsync();
        }
    }

    // The anonymous handler of the stream that the client creates.
    private sealed class ResponseHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ByteBuf byteBuf = (ByteBuf) msg;
            Console.Error.WriteLine(byteBuf.ToString(Encoding.ASCII));
            byteBuf.Release();
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt == ChannelInputShutdownReadComplete.INSTANCE)
            {
                // Close the connection once the remote peer did send the FIN for this stream.
                ((IQuicChannel) ctx.Channel.Parent).CloseAsync(true, 0,
                        ctx.Alloc().DirectBuffer(16)
                                .WriteBytes(new byte[] { (byte) 'k', (byte) 't', (byte) 'h', (byte) 'x', (byte) 'b',
                                        (byte) 'y', (byte) 'e' }));
            }
        }
    }
}
