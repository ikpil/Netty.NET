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

// CLR: ported as written, with the adaptations of QuicClientExample (the async Main of ExampleLauncher, the
// QuicEndpointChannel in place of NioDatagramChannel, IQuicChannel.NewBootstrap, the nested handler classes, the
// SHUTDOWN_OUTPUT continuation, Console.Error and Encoding.ASCII). The package-private static methods are internal;
// newChannelAndSendData returns its Task and createStream the Task<IQuicStreamChannel> of CreateStreamAsync, whose
// listener is a continuation. sslEngineProvider(q -> context.newEngine(q.alloc(), "localhost", 9999)) is the
// Func<IQuicChannel, QuicSslEngine> over QuicSslContext.NewEngine(alloc, peerHost, peerPort). System.Net.Quic exposes
// no 0-RTT early data, so QuicSslContextBuilder.EarlyData(true) makes Build() throw NotSupportedException
// (docs/codec-classes-quic-porting.md, Unsupported features): run, the example ends with that exception before it
// connects, and SslEarlyDataReadyEvent is never fired.
public sealed class QuicClientZeroRTTExample
{

    private QuicClientZeroRTTExample() { }

    public static async Task Main(string[] args)
    {
        QuicSslContext context = QuicSslContextBuilder.ForClient().TrustManager(InsecureTrustManagerFactory.INSTANCE).
                ApplicationProtocols("http/0.9").EarlyData(true).Build();

        await NewChannelAndSendData(context, null);
        await NewChannelAndSendData(context, new EarlyDataSendHandler());
    }

    // The anonymous ChannelInboundHandlerAdapter of the second connection.
    private sealed class EarlyDataSendHandler : ChannelInboundHandlerAdapter
    {
        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is SslEarlyDataReadyEvent)
            {
                _ = CreateStream((IQuicChannel) ctx.Channel).ContinueWith(f =>
                {
                    if (f.IsCompletedSuccessfully)
                    {
                        IQuicStreamChannel streamChannel = f.Result;
                        streamChannel.WriteAndFlushAsync(
                                Unpooled.CopiedBuffer("0rtt stream data\r\n", Encoding.ASCII));
                    }
                }, TaskScheduler.Default);
            }
            base.UserEventTriggered(ctx, evt);
        }
    }

    internal static async Task NewChannelAndSendData(QuicSslContext context, IChannelHandler earlyDataSendHandler)
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
        try
        {
            IChannelHandler codec = new QuicClientCodecBuilder()
                    .SslEngineProvider(q => context.NewEngine(q.Alloc(), "localhost", 9999))
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

            QuicChannelBootstrap quicChannelBootstrap = IQuicChannel.NewBootstrap(channel)
                    .StreamHandler(new RemoteStreamHandler())
                    .RemoteAddress(new IPEndPoint(NetUtil.LOCALHOST4, 9999));

            if (earlyDataSendHandler != null)
            {
                quicChannelBootstrap.Handler(earlyDataSendHandler);
            }

            IQuicChannel quicChannel = await quicChannelBootstrap
                    .ConnectAsync();

            IQuicStreamChannel streamChannel = await CreateStream(quicChannel);
            // Write the data and send the FIN. After this its not possible anymore to write any more data.
            _ = streamChannel.WriteAndFlushAsync(Unpooled.CopiedBuffer("Bye\r\n", Encoding.ASCII))
                    .ContinueWith(_ => streamChannel.ShutdownOutputAsync(), TaskScheduler.Default);
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

    internal static Task<IQuicStreamChannel> CreateStream(IQuicChannel quicChannel)
    {
        return quicChannel.CreateStreamAsync(QuicStreamType.BIDIRECTIONAL,
                new ResponseHandler());
    }

    // The anonymous handler of the streams that createStream creates.
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
