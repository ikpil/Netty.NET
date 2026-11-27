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
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;

namespace Netty.NET.Handler.Codec.Http3.Example;

// CLR: main(String...) is the async Main that ExampleLauncher (CLR-only) runs; bind(0).sync().channel(), connect().get(),
// newRequestStream(...).sync().getNow(), writeAndFlush(...).sync(), closeFuture().sync() and close().sync() are awaits,
// and group.shutdownGracefully() is ShutdownGracefullyAsync() with the Task discarded. NioDatagramChannel.class is
// Channel<QuicEndpointChannel>(): msquic owns the UDP socket, so the codec runs on the QuicEndpointChannel that takes the
// datagram channel's place; binding it with the client codec records the local address, and IQuicChannel.NewBootstrap
// (QuicChannel.newBootstrap) connects through it (owner decision 2026-11-27, docs/codec-classes-quic-porting.md, Stage 2).
// bind(0) is BindAsync(0), the wildcard address. maxIdleTimeout(5000, MILLISECONDS) takes a TimeSpan;
// InsecureTrustManagerFactory.INSTANCE is the handler port's accepting RemoteCertificateValidationCallback.
// Http3.newRequestStream is Http3.NewRequestStreamAsync, and the anonymous Http3RequestStreamInboundHandler is the private
// nested ResponseHandler. A Java String passed as a CharSequence (method, path, authority, scheme) is a
// StringCharSequence; NetUtil.LOCALHOST4.getHostAddress() is the text of the IPAddress (127.0.0.1) and the port prints
// with the invariant culture. QuicStreamChannel.SHUTDOWN_OUTPUT has no counterpart (IQuicStreamChannel), so the write
// Task is continued with ShutdownOutputAsync(), as the listener calls shutdownOutput() on the future's channel.
// System.err.print is Console.Error.Write, CharsetUtil.US_ASCII is Encoding.ASCII and content() the Content property.
// Http3ServerExample.PORT is 9999 unless the CLR-only -Dport=<port> property of the launcher gives another one.
public sealed class Http3ClientExample
{
    private Http3ClientExample()
    {
    }

    public static async Task Main(string[] args)
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());

        try
        {
            QuicSslContext context = QuicSslContextBuilder.ForClient()
                    .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                    .ApplicationProtocols(Http3.SupportedApplicationProtocols()).Build();
            IChannelHandler codec = Http3.NewQuicClientCodecBuilder()
                    .SslContext(context)
                    .MaxIdleTimeout(TimeSpan.FromMilliseconds(5000))
                    .InitialMaxData(10000000)
                    .InitialMaxStreamDataBidirectionalLocal(1000000)
                    .Build();

            Bootstrap bs = new Bootstrap();
            IChannel channel = await bs.Group(group)
                    .Channel<QuicEndpointChannel>()
                    .Handler(codec)
                    .BindAsync(0);

            IQuicChannel quicChannel = await IQuicChannel.NewBootstrap(channel)
                    .Handler(new Http3ClientConnectionHandler())
                    .RemoteAddress(new IPEndPoint(NetUtil.LOCALHOST4, Http3ServerExample.PORT))
                    .ConnectAsync();

            IQuicStreamChannel streamChannel = await Http3.NewRequestStreamAsync(quicChannel,
                    new ResponseHandler());

            // Write the Header frame and send the FIN to mark the end of the request.
            // After this its not possible anymore to write any more data.
            IHttp3HeadersFrame frame = new DefaultHttp3HeadersFrame();
            frame.Headers().Method(new StringCharSequence("GET")).Path(new StringCharSequence("/"))
                    .Authority(new StringCharSequence(NetUtil.LOCALHOST4.ToString() + ":" +
                            Http3ServerExample.PORT.ToString(CultureInfo.InvariantCulture)))
                    .Scheme(new StringCharSequence("https"));
            Task writeFuture = streamChannel.WriteAndFlushAsync(frame);
            _ = writeFuture.ContinueWith(_ => streamChannel.ShutdownOutputAsync(), TaskScheduler.Default);
            await writeFuture;

            // Wait for the stream channel and quic channel to be closed (this will happen after we received the FIN).
            // After this is done we will close the underlying datagram channel.
            await streamChannel.CloseFuture;

            // After we received the response lets also close the underlying QUIC channel and datagram channel.
            await quicChannel.CloseAsync();
            await channel.CloseAsync();
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    // CLR: the anonymous Http3RequestStreamInboundHandler of the request stream (see the class comment).
    private sealed class ResponseHandler : Http3RequestStreamInboundHandler
    {
        protected override void ChannelRead(IChannelHandlerContext ctx, IHttp3HeadersFrame frame)
        {
            ReferenceCountUtil.Release(frame);
        }

        protected override void ChannelRead(IChannelHandlerContext ctx, IHttp3DataFrame frame)
        {
            Console.Error.Write(frame.Content.ToString(Encoding.ASCII));
            ReferenceCountUtil.Release(frame);
        }

        protected override void ChannelInputClosed(IChannelHandlerContext ctx)
        {
            _ = ctx.CloseAsync();
        }
    }
}
