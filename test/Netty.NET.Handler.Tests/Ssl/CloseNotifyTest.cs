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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.PkiTesting;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using AuthSslProtocols = System.Security.Authentication.SslProtocols;
using static Netty.NET.Handler.Codec.ByteToMessageDecoder;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: SslProvider.OPENSSL is library-not-ported (netty-tcnative; owner decision 2026-11-24,
// docs/handler-ssl-design.md), so data() keeps the JDK rows (SslStream) and the OPENSSL rows with their
// OpenSsl.isAvailable() assumption are dropped. @ParameterizedTest with @MethodSource is [Theory] with
// [MemberData]; @Timeout(30) (seconds) is the Timeout of the [Theory] in milliseconds, which xUnit applies to
// async tests, so the test returns a Task. assumeTrue is Assert.SkipUnless. BlockingQueue/LinkedBlockingQueue are
// ConcurrentQueue<object> (the test only polls without waiting), the anonymous INACTIVE object and
// SimpleChannelInboundHandler are named nested classes, UnpooledByteBufAllocator.DEFAULT is
// UnpooledByteBufAllocator.Default and US_ASCII is Encoding.ASCII. The TLSv1.2 row's "client automatically
// responds with close_notify" is SslHandler's engine answering a received close_notify below TLS 1.3, as the JDK
// engine does (SslHandler.EngineAcknowledgeCloseNotify; SslStream itself does not answer, measured).
public class CloseNotifyTest
{

    private static readonly UnpooledByteBufAllocator ALLOC = UnpooledByteBufAllocator.Default;
    private static readonly object INACTIVE = new Inactive();

    private sealed class Inactive
    {
        public override string ToString()
        {
            return "INACTIVE";
        }
    }

    public static IEnumerable<object[]> Data()
    {
        return new List<object[]>
        {
                new object[] { SslProvider.JDK, Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_2 },
                new object[] { SslProvider.JDK, Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_3 },
        };
    }

#pragma warning disable xUnit1069 // The EmbeddedChannel body has no blocking or cancellable call for the token to end.
    [Theory(Timeout = 30000)]
    [MemberData(nameof(Data))]
    public Task EventsOrder(SslProvider provider, string protocol)
    {
        if (Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_3.Equals(protocol))
        {
            // Ensure we support TLSv1.3
            Assert.SkipUnless(SslProvider.IsTlsv13Supported(provider), "TLSv1.3 is not supported");
        }

        ConcurrentQueue<object> clientEventQueue = new ConcurrentQueue<object>();
        ConcurrentQueue<object> serverEventQueue = new ConcurrentQueue<object>();

        EmbeddedChannel clientChannel = InitChannel(provider, protocol, true, clientEventQueue);
        EmbeddedChannel serverChannel = InitChannel(provider, protocol, false, serverEventQueue);

        try
        {
            // handshake:
            ForwardData(clientChannel, serverChannel);
            ForwardData(serverChannel, clientChannel);
            ForwardData(clientChannel, serverChannel);
            ForwardData(serverChannel, clientChannel);
            Assert.IsType<SslHandshakeCompletionEvent>(Poll(clientEventQueue));
            Assert.IsType<SslHandshakeCompletionEvent>(Poll(serverEventQueue));
            Assert.Equal(protocol, HandshakenProtocol(clientChannel));

            // send data:
            clientChannel.WriteOutbound(ByteBufUtil.WriteAscii(ALLOC, "request_msg"));
            ForwardData(clientChannel, serverChannel);
            Assert.Equal("request_msg", Poll(serverEventQueue));

            // respond with data and close_notify:
            serverChannel.WriteOutbound(ByteBufUtil.WriteAscii(ALLOC, "response_msg"));
            Assert.True(serverChannel.Finish());
            Assert.IsType<SslCloseCompletionEvent>(Poll(serverEventQueue));
            Assert.True(clientEventQueue.IsEmpty);

            // consume server response with close_notify:
            ForwardAllWithCloseNotify(serverChannel, clientChannel);
            Assert.Equal("response_msg", Poll(clientEventQueue));
            Assert.IsType<SslCloseCompletionEvent>(Poll(clientEventQueue));

            // make sure client automatically responds with close_notify:
            if (!JdkTls13(provider, protocol))
            {
                // JDK impl of TLSv1.3 does not automatically generate "close_notify" in response to the received
                // "close_notify" alert. This is a legit behavior according to the spec:
                // https://tools.ietf.org/html/rfc8446#section-6.1. Handle it differently:
                AssertCloseNotify(clientChannel.ReadOutbound<ByteBuf>());
            }
        }
        finally
        {
            try
            {
                clientChannel.Finish();
            }
            finally
            {
                serverChannel.Finish();
            }
        }

        if (JdkTls13(provider, protocol))
        {
            AssertCloseNotify(clientChannel.ReadOutbound<ByteBuf>());
        }
        else
        {
            DiscardEmptyOutboundBuffers(clientChannel);
        }

        Assert.Equal(INACTIVE, Poll(clientEventQueue));
        Assert.True(clientEventQueue.IsEmpty);
        Assert.Equal(INACTIVE, Poll(serverEventQueue));
        Assert.True(serverEventQueue.IsEmpty);

        Assert.False(clientChannel.ReleaseInbound());
        Assert.False(clientChannel.ReleaseOutbound());
        Assert.False(serverChannel.ReleaseInbound());
        Assert.False(serverChannel.ReleaseOutbound());
        return Task.CompletedTask;
    }
#pragma warning restore xUnit1069

    private static bool JdkTls13(SslProvider provider, string protocol)
    {
        return provider == SslProvider.JDK && Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_3.Equals(protocol);
    }

    // CLR: InsecureTrustManagerFactory.INSTANCE is its RemoteCertificateValidationCallback (ssl context wave).
    private static EmbeddedChannel InitChannel(SslProvider provider, string protocol, bool useClientMode,
            ConcurrentQueue<object> eventQueue)
    {

        SelfSignedCertificateBundle ssc = CachedSelfSignedCertificate();
        SslContext sslContext = (useClientMode
                ? SslContextBuilder.ForClient().TrustManager(InsecureTrustManagerFactory.INSTANCE)
                : SslContextBuilder.ForServer(ssc.Certificate(), ssc.PrivateKey()))
                 .SslProvider(provider)
                 .Protocols(protocol)
                .Build();
        // use sslContext.newHandler(ALLOC) instead of new SslHandler(sslContext.newEngine(ALLOC)) to create
        // non-JDK compatible OpenSSL engine that can process partial packets:
        SslHandler handler = sslContext.NewHandler(ALLOC);
        // handler incremented the reference count and will destroy it when removed
        ReferenceCountUtil.Release(sslContext);

        return new EmbeddedChannel(
                handler,
                new EventQueueHandler(eventQueue)
        );
    }

    private sealed class EventQueueHandler : SimpleChannelInboundHandler<ByteBuf>
    {
        private readonly ConcurrentQueue<object> eventQueue;

        internal EventQueueHandler(ConcurrentQueue<object> eventQueue)
        {
            this.eventQueue = eventQueue;
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf msg)
        {
            eventQueue.Enqueue(msg.ToString(Encoding.ASCII));
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            eventQueue.Enqueue(evt);
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            eventQueue.Enqueue(INACTIVE);
            base.ChannelInactive(ctx);
        }
    }

    private static void ForwardData(EmbeddedChannel from, EmbeddedChannel to)
    {
        ByteBuf @in;

        while ((@in = from.ReadOutbound<ByteBuf>()) != null)
        {
            to.WriteInbound(@in);
        }
    }

    private static void ForwardAllWithCloseNotify(EmbeddedChannel from, EmbeddedChannel to)
    {
        ByteBuf cumulation = Unpooled.EmptyBuffer;
        ByteBuf @in, closeNotify = null;

        while ((@in = from.ReadOutbound<ByteBuf>()) != null)
        {
            if (closeNotify != null)
            {
                closeNotify.Release();
            }

            closeNotify = @in.Duplicate();
            cumulation = MERGE_CUMULATOR.Cumulate(ALLOC, cumulation, @in.Retain());
        }

        AssertCloseNotify(closeNotify);
        to.WriteInbound(cumulation);
    }

    // CLR: engine().getSession().getProtocol() is the SslStream's SslProtocol (SslHandler.Engine() is the internal
    // accessor of the handler's SslStream, owner decision 2026-11-24), "the value that represents the protocol used for
    // authentication" (https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.sslprotocol?view=net-10.0),
    // turned into the JSSE protocol name of SslProtocols.
    private static string HandshakenProtocol(EmbeddedChannel channel)
    {
        SslHandler sslHandler = channel.Pipeline.Get<SslHandler>();
        AuthSslProtocols session = sslHandler.Engine().SslProtocol;

        switch (session)
        {
            case AuthSslProtocols.Tls13:
                return Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_3;
            case AuthSslProtocols.Tls12:
                return Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_2;
            default:
                return session.ToString();
        }
    }

    // CLR: Queue<Object> outbound / peek() / poll() are the Queue<object> of OutboundMessages() with TryPeek/Dequeue.
    private static void DiscardEmptyOutboundBuffers(EmbeddedChannel channel)
    {
        Queue<object> outbound = channel.OutboundMessages();

        while (outbound.TryPeek(out object peeked) && peeked is ByteBuf)
        {
            ByteBuf buf = (ByteBuf) peeked;
            if (!buf.IsReadable())
            {
                buf.Release();
                outbound.Dequeue();
            }
            else
            {
                break;
            }
        }
    }

    internal static void AssertCloseNotify(ByteBuf closeNotify)
    {
        Assert.NotNull(closeNotify);

        try
        {
            Assert.True(closeNotify.ReadableBytes >= 7);
        }
        finally
        {
            closeNotify.Release();
        }
    }

    // CLR-only helper: BlockingQueue.poll() without a timeout (null when the queue is empty).
    private static object Poll(ConcurrentQueue<object> queue)
    {
        return queue.TryDequeue(out object value) ? value : null;
    }

    // CLR: CachedSelfSignedCertificate.getCachedCertificate() returns a cached SelfSignedCertificate; both are @Deprecated
    // test units that are not ported ("@deprecated Use the {@code CertificateBuilder} from {@code netty-pkitesting}
    // instead", recorded by the lead, docs/handler-ssl-design.md), so the nested SelfSignedCertificateBundle caches the
    // X509Bundle that SelfSignedCertificate's CertificateBuilderCertGenerator builds by default: a self-signed
    // certificate authority "CN=localhost" with an RSA 2048 key. Its Certificate()/PrivateKey() are the PEM files of the
    // certificate path and of the PKCS #8 key (X509Bundle.ToTempCertChainPem/ToTempPrivateKeyPem, deleted at process exit
    // as SelfSignedCertificate's deleteOnExit() files are), Cert() the certificate and Key() the key, which is the BCL key
    // object of the bundle's key pair (docs/pkitesting-porting.md: "A generated or given key pair is the BCL key object").
    private static readonly Lazy<SelfSignedCertificateBundle> CACHED_CERTIFICATE =
            new Lazy<SelfSignedCertificateBundle>(() => new SelfSignedCertificateBundle(new CertificateBuilder()
                    .SetIsCertificateAuthority(true)
                    .Subject("CN=localhost")
                    .Rsa2048()
                    .BuildSelfSigned()));

    private static SelfSignedCertificateBundle CachedSelfSignedCertificate()
    {
        return CACHED_CERTIFICATE.Value;
    }

    private sealed class SelfSignedCertificateBundle
    {
        private readonly X509Bundle bundle;
        private readonly Lazy<FileInfo> certificate;
        private readonly Lazy<FileInfo> privateKey;

        internal SelfSignedCertificateBundle(X509Bundle bundle)
        {
            this.bundle = bundle;
            certificate = new Lazy<FileInfo>(bundle.ToTempCertChainPem);
            privateKey = new Lazy<FileInfo>(bundle.ToTempPrivateKeyPem);
        }

        internal FileInfo Certificate()
        {
            return certificate.Value;
        }

        internal FileInfo PrivateKey()
        {
            return privateKey.Value;
        }

        internal X509Certificate2 Cert()
        {
            return bundle.GetCertificate();
        }

        internal AsymmetricAlgorithm Key()
        {
            return (AsymmetricAlgorithm) bundle.GetKeyPair();
        }
    }
}
