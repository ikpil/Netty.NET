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
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.PkiTesting;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: the server that h3spec runs against; it lives in the examples executable (test/Netty.NET.Handler.Codec.Http3.Examples)
// with the package's namespace, and ExampleLauncher (CLR-only) runs its async Main. bind(...).sync().channel() and
// closeFuture().sync() are awaits, and group.shutdownGracefully() is ShutdownGracefullyAsync() with the Task discarded.
// NioDatagramChannel.class is Channel<QuicEndpointChannel>(): msquic owns the UDP socket, so the codec runs on the
// QuicEndpointChannel that takes the datagram channel's place, and binding it starts the QuicListener (owner decision
// 2026-11-27, docs/codec-classes-quic-porting.md, Stage 2). new InetSocketAddress(port) is the wildcard
// IPEndPoint(IPAddress.Any, port) (Transport convention). SelfSignedCertificate is @Deprecated and not ported (handler,
// deprecated-not-ported); its key and certificate come from the PkiTesting port (CertificateBuilder, as
// Netty.NET.Example's ServerUtil does), whose ECDSA P-256 key pair is the AsymmetricAlgorithm of
// X509Bundle.GetKeyPair(). maxIdleTimeout(5000, MILLISECONDS) takes a TimeSpan. The anonymous ChannelInitializer
// classes are ActionChannelInitializers whose delegates are initChannel, and the anonymous
// Http3RequestStreamInboundHandler is the private nested RequestStreamHandler. A Java String passed as a CharSequence
// (status, add, addInt) is a StringCharSequence; QuicStreamChannel.SHUTDOWN_OUTPUT has no counterpart (IQuicStreamChannel),
// so the write Task is continued with ShutdownOutputAsync(), as the listener calls shutdownOutput() on the future's
// channel. channel.eventLoop().submit(runnable) is the SubmitAsync(Action) extension of the event loop, whose Task is
// discarded, and System.out.println is Console.WriteLine. CharsetUtil.US_ASCII is Encoding.ASCII and Integer.parseInt
// is int.Parse with an optional sign and the invariant culture.
public sealed class Http3SpecTestServer
{
    private static readonly byte[] CONTENT = Encoding.ASCII.GetBytes("Hello World!\r\n");
    internal const int PORT = 9999;

    private Http3SpecTestServer() { }

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
                            }), null, null, null, true));
                })).Build();
        try
        {
            Bootstrap bs = new Bootstrap();
            IChannel channel = await bs.Group(group)
                    .Channel<QuicEndpointChannel>()
                    .Handler(codec)
                    .BindAsync(new IPEndPoint(IPAddress.Any, port));
            _ = channel.EventLoop.SubmitAsync(() => Console.WriteLine("H3SPEC_SERVER_READY"));
            await channel.CloseFuture;
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    // CLR: the anonymous Http3RequestStreamInboundHandler added for each request stream (see the class comment).
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
            _ = ctx.WriteAndFlushAsync(new DefaultHttp3DataFrame(
                            Unpooled.WrappedBuffer(CONTENT)))
                    .ContinueWith(_ => stream.ShutdownOutputAsync(), TaskScheduler.Default);
        }
    }
}
