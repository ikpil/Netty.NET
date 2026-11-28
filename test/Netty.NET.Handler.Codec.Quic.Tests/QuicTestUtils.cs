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
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR: the CLR version of the QUIC test utilities on the QuicEndpointChannel, which takes the DatagramChannel's
// place : the client and server "datagram channels" are QuicEndpointChannels with the codec as their handler, on a
// MultiThreadIoEventLoopGroup with the NIO handler factory. Epoll (EpollDatagramChannel, recvmmsg, SO_REUSEPORT
// and the EpollQuicUtils segmented allocator) is not ported (transport-classes-epoll is recorded), so the branches
// for it are gone and soReusePortOption() returns null, as Java does without epoll; the FixedRecvByteBufAllocator
// for datagrams has no use on an endpoint whose datagrams msquic receives. SelfSignedCertificate (generated with
// BouncyCastle or the JDK's internal classes) is a self-signed ECDSA P-256 certificate for "localhost" created
// with System.Security.Cryptography (CertificateRequest.CreateSelfSigned). The builders take TimeSpan for (5000,
// TimeUnit.MILLISECONDS). The shutdown hook is AppDomain.ProcessExit. Bootstrap.bind().sync().channel() is await
// BindAsync(); the methods returning a Channel return Task<IChannel>. The builders leave out
// activeMigration(false): it asks for the disable_active_migration transport parameter, which System.Net.Quic
// cannot set, so the msquic codec builders report it unsupported (msquic allows migration, which is
// activeMigration(true)); no test migrates a connection.
internal static class QuicTestUtils
{
    internal static readonly string[] PROTOS = new string[] { "hq-29" };
    internal static readonly SelfSignedCertificate SELF_SIGNED_CERTIFICATE;

    private const int DATAGRAM_SIZE = 2048;

    static QuicTestUtils()
    {
        SelfSignedCertificate cert;

        try
        {
            cert = new SelfSignedCertificate();
        }
        catch (Exception e)
        {
            throw new TypeInitializationException(typeof(QuicTestUtils).FullName, e);
        }

        SELF_SIGNED_CERTIFICATE = cert;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            GROUP.ShutdownGracefullyAsync().Wait();
        };
    }

    // CLR: daemon (background) threads, so the test host can exit as a JVM with the shutdown hook does.
    private static readonly IEventLoopGroup GROUP = new MultiThreadIoEventLoopGroup(0,
            new DefaultThreadFactory("quicTestGroup", true), NioIoHandler.NewFactory());

    internal static readonly ChannelHandlerAdapter NOOP_HANDLER = new SharableChannelHandlerAdapter();

    // The anonymous sharable ChannelHandlerAdapter.
    private sealed class SharableChannelHandlerAdapter : ChannelHandlerAdapter
    {
        public override bool IsSharable => true;
    }

    // CLR: io.netty.handler.ssl.util.SelfSignedCertificate, reduced to the key and certificate the tests use.
    internal sealed class SelfSignedCertificate
    {
        private readonly ECDsa key;
        private readonly X509Certificate2 cert;

        internal SelfSignedCertificate()
        {
            key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            CertificateRequest request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
            SubjectAlternativeNameBuilder san = new SubjectAlternativeNameBuilder();
            san.AddDnsName("localhost");
            request.CertificateExtensions.Add(san.Build());
            using X509Certificate2 selfSigned = request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            cert = X509CertificateLoader.LoadCertificate(selfSigned.RawData);
        }

        internal AsymmetricAlgorithm PrivateKey()
        {
            return key;
        }

        internal X509Certificate2 Certificate()
        {
            return cert;
        }
    }

    internal static Task<IChannel> NewClient(IExecutor sslTaskExecutor)
    {
        return NewClient(NewQuicClientBuilder(sslTaskExecutor));
    }

    private static Bootstrap NewBootstrap()
    {
        Bootstrap bs = new Bootstrap();
        // Use recvmmsg when possible.
        // CLR: the endpoint channel replaces EpollDatagramChannel and NioDatagramChannel (see the class comment).
        bs.Channel<QuicEndpointChannel>();

        return bs.Group(GROUP);
    }

    internal static async Task<IChannel> NewClient(QuicClientCodecBuilder builder)
    {
        return await NewBootstrap()
                // We don't want any special handling of the channel so just use a dummy handler.
                .Handler(builder.Build())
                .BindAsync(new IPEndPoint(NetUtil.LOCALHOST4, 0));
    }

    internal static QuicChannelBootstrap NewQuicChannelBootstrap(IChannel channel)
    {
        QuicChannelBootstrap bs = IQuicChannel.NewBootstrap(channel);
        return bs;
    }

    internal static QuicClientCodecBuilder NewQuicClientBuilder(IExecutor sslTaskExecutor)
    {
        return NewQuicClientBuilder(sslTaskExecutor, QuicSslContextBuilder.ForClient()
                .TrustManager(InsecureTrustManagerFactory.INSTANCE).ApplicationProtocols(PROTOS).Build());
    }

    internal static QuicClientCodecBuilder NewQuicClientBuilder(IExecutor sslTaskExecutor, QuicSslContext sslContext)
    {
        return new QuicClientCodecBuilder()
                .SslEngineProvider(q => sslContext.NewEngine(q.Alloc()))
                .MaxIdleTimeout(TimeSpan.FromMilliseconds(5000))
                .InitialMaxData(10000000)
                .InitialMaxStreamDataBidirectionalLocal(1000000)
                .InitialMaxStreamDataBidirectionalRemote(1000000)
                .InitialMaxStreamsBidirectional(100)
                .InitialMaxStreamsUnidirectional(100)
                .InitialMaxStreamDataUnidirectional(1000000)
                .SslTaskExecutor(sslTaskExecutor);
    }

    internal static QuicServerCodecBuilder NewQuicServerBuilder(IExecutor sslTaskExecutor)
    {
        return NewQuicServerBuilder(sslTaskExecutor, QuicSslContextBuilder.ForServer(
                SELF_SIGNED_CERTIFICATE.PrivateKey(), null, SELF_SIGNED_CERTIFICATE.Certificate())
                .ApplicationProtocols(PROTOS).Build());
    }

    internal static QuicServerCodecBuilder NewQuicServerBuilder(IExecutor sslTaskExecutor, QuicSslContext context)
    {
        QuicServerCodecBuilder builder = new QuicServerCodecBuilder()
                .SslEngineProvider(q => context.NewEngine(q.Alloc()))
                .MaxIdleTimeout(TimeSpan.FromMilliseconds(5000))
                .InitialMaxData(10000000)
                .InitialMaxStreamDataBidirectionalLocal(1000000)
                .InitialMaxStreamDataBidirectionalRemote(1000000)
                .InitialMaxStreamDataUnidirectional(1000000)
                .InitialMaxStreamsBidirectional(100)
                .InitialMaxStreamsUnidirectional(100)
                .SslTaskExecutor(sslTaskExecutor);
        return builder;
    }

    private static Bootstrap NewServerBootstrap(QuicServerCodecBuilder serverBuilder,
                                                IQuicTokenHandler tokenHandler, IChannelHandler handler,
                                                IChannelHandler streamHandler)
    {
        serverBuilder.TokenHandler(tokenHandler)
                .StreamHandler(streamHandler);

        if (handler != null)
        {
            serverBuilder.Handler(handler);
        }

        IChannelHandler codec = serverBuilder.Build();
        return NewServerBootstrap()
                .Handler(codec);
    }

    internal static Bootstrap NewServerBootstrap()
    {
        return NewBootstrap()
                .LocalAddress(new IPEndPoint(NetUtil.LOCALHOST4, 0));
    }

    internal static Task<IChannel> NewServer(QuicServerCodecBuilder serverBuilder, IQuicTokenHandler tokenHandler,
                                             IChannelHandler handler, IChannelHandler streamHandler)
    {
        return NewServerBootstrap(serverBuilder, tokenHandler, handler, streamHandler)
                .BindAsync();
    }

    internal static Task<IChannel> NewServer(IExecutor sslTaskExecutor, IQuicTokenHandler tokenHandler,
                                             IChannelHandler handler, IChannelHandler streamHandler)
    {
        return NewServer(NewQuicServerBuilder(sslTaskExecutor), tokenHandler, handler, streamHandler);
    }

    internal static Task<IChannel> NewServer(IExecutor sslTaskExecutor, IChannelHandler handler,
                                             IChannelHandler streamHandler)
    {
        return NewServer(sslTaskExecutor, InsecureQuicTokenHandler.INSTANCE, handler, streamHandler);
    }

    internal static async Task CloseIfNotNull(IChannel channel)
    {
        if (channel != null)
        {
            await channel.CloseAsync();
        }
    }

    internal static ChannelOption<bool> SoReusePortOption()
    {
        return null;
    }
}
