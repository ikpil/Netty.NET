/*
 * Copyright 2019 The Netty Project
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
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.PkiTesting;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;
using Xunit.Sdk;
using static Netty.NET.Handler.Codec.Http2.Http2FrameCodecBuilder;
using HttpVersion = Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: the transports are the Transport ports (docs/transport-porting.md, Tests): new
// MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory()) with NioServerSocketChannel/NioSocketChannel, and
// MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory()) with LocalServerChannel/LocalChannel/LocalAddress; new
// InetSocketAddress(NetUtil.LOCALHOST, 0) is an IPEndPoint (docs/codec-http-porting.md, Shared CLR adaptations:
// InetSocketAddress is EndPoint). @BeforeEach setup() is the constructor and @AfterEach teardown() is
// IAsyncLifetime.DisposeAsync, as in Http2ConnectionRoundtripTest. ChannelFuture/Future is Task and Promise is
// TaskCompletionSource (docs/codec-http-porting.md, Shared CLR adaptations): sync()/syncUninterruptibly()/
// awaitUninterruptibly() and getNow()/get() after them are awaiting the Task (Bootstrap.BindAsync/ConnectAsync and
// Http2StreamChannelBootstrap.OpenAsync return the channel). A ChannelFutureListener/FutureListener is
// Http2CodecUtil.AddListener, a continuation on Common's EventExecutorTaskScheduler over ctx.Executor that runs on the
// event loop as DefaultPromise.notifyListeners() does (docs/codec-http-porting.md, Listener timing), and
// ChannelFutureListener.CLOSE closes the channel the operation ran on, because a Task has no channel (same document,
// Shared CLR adaptations). @Timeout is the Timeout of the [Fact] (docs/handler-porting.md, SSL tests: xUnit); when it
// elapses, "the cancellation token available via TestContext.Current.CancellationToken will be signaled"
// (https://xunit.net/docs/getting-started/v3/whats-new), so a latch await() without a timeout is
// Wait(TestContext.Current.CancellationToken) and await(timeout) Wait(timeout, token). CountDownLatch is CountdownEvent
// with countDown() as the private CountDown(latch), which ignores a zero count, and AtomicInteger/AtomicReference/
// AtomicBoolean are the Common types (docs/handler-porting.md, SSL tests: Concurrency helpers). The anonymous
// ChannelInitializer bodies are ActionChannelInitializer delegates (docs/transport-porting.md: "the anonymous
// ChannelInitializer is an ActionChannelInitializer<IChannel>") and the other anonymous classes are named nested
// classes. CharsetUtil.US_ASCII is Encoding.ASCII (docs/codec-http-porting.md, Shared CLR adaptations: Charset is
// Encoding); Http2SettingsAckFrame.INSTANCE is IHttp2SettingsAckFrame.INSTANCE, and HttpVersion is aliased to
// codec-http's type because System.Net declares an HttpVersion too. throw new AssertionError(cause) is an
// XunitException (an assertion failure) whose message is cause.toString() (ThrowableUtil.ThrowableToString) and whose
// inner exception is the cause: AssertionError(Object) takes its detail message from the object's string conversion,
// and "If the specified object is an instance of Throwable, it becomes the cause"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/AssertionError.html). The deprecated
// Http2MultiplexCodecBuilder/Http2MultiplexCodec are not ported (their @deprecated text names Http2FrameCodecBuilder
// and Http2MultiplexHandler): asyncSettingsAckWithMultiplexCodec only exercises Http2MultiplexCodec (the server variant
// of asyncSettingsAck0 that asyncSettingsAckWithMultiplexHandler runs with the replacement) and has no C# method.
// SslProvider.OPENSSL is library-not-ported (netty-tcnative; docs/handler-porting.md, SSL: Context layer,
// "OPENSSL/OPENSSL_REFCNT throw NotSupportedException"), so testSSLExceptionOpenSslTLSv12,
// testSSLExceptionOpenSslTLSv13 and testFireChannelReadAfterHandshakeSuccess_OPENSSL have no C# method either.
#pragma warning disable xUnit1051 // The source's bind/connect/write/close calls do not observe the test cancellation.
#pragma warning disable xUnit1069 // The helpers these tests call wait with TestContext.Current.CancellationToken.
public class Http2MultiplexTransportTest : IAsyncLifetime
{
    private static readonly IChannelHandler DISCARD_HANDLER = new DiscardHandler();

    // CLR: the anonymous ChannelInboundHandlerAdapter of DISCARD_HANDLER; isSharable() is the IsSharable property.
    private sealed class DiscardHandler : ChannelInboundHandlerAdapter
    {

        public override bool IsSharable => true;

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ReferenceCountUtil.Release(msg);
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            ReferenceCountUtil.Release(evt);
        }
    }

    private IEventLoopGroup eventLoopGroup;
    private IChannel clientChannel;
    private IChannel serverChannel;
    private IChannel serverConnectedChannel;

    private sealed class MultiplexInboundStream : ChannelInboundHandlerAdapter
    {
        internal Task responseFuture;
        internal readonly AtomicInteger handlerInactivatedFlushed;
        internal readonly AtomicInteger handleInactivatedNotFlushed;
        internal readonly CountdownEvent latchHandlerInactive;
        internal static readonly string LARGE_STRING = GenerateLargeString(10240);

        internal MultiplexInboundStream(AtomicInteger handleInactivatedFlushed,
                                        AtomicInteger handleInactivatedNotFlushed, CountdownEvent latchHandlerInactive)
        {
            this.handlerInactivatedFlushed = handleInactivatedFlushed;
            this.handleInactivatedNotFlushed = handleInactivatedNotFlushed;
            this.latchHandlerInactive = latchHandlerInactive;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is IHttp2HeadersFrame && ((IHttp2HeadersFrame) msg).IsEndStream())
            {
                ByteBuf response = Unpooled.CopiedBuffer(LARGE_STRING, Encoding.ASCII);
                responseFuture = ctx.WriteAndFlushAsync(new DefaultHttp2DataFrame(response, true));
            }

            ReferenceCountUtil.Release(msg);
        }

        // CLR: responseFuture.isSuccess() is Task.IsCompletedSuccessfully.
        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            if (responseFuture.IsCompletedSuccessfully)
            {
                handlerInactivatedFlushed.IncrementAndGet();
            }
            else
            {
                handleInactivatedNotFlushed.IncrementAndGet();
            }

            CountDown(latchHandlerInactive);
            ctx.FireChannelInactive();
        }

        private static string GenerateLargeString(int sizeInBytes)
        {
            StringBuilder sb = new StringBuilder(sizeInBytes);

            for (int i = 0; i < sizeInBytes; i++)
            {
                sb.Append('X');
            }

            return sb.ToString();
        }
    }

    public Http2MultiplexTransportTest()
    {
        eventLoopGroup = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    // CLR: teardown(); close() is not awaited, as the source does not sync it, and
    // shutdownGracefully(0, 0, MILLISECONDS).syncUninterruptibly() awaits the group's termination.
    public async ValueTask DisposeAsync()
    {
        if (clientChannel != null)
        {
            _ = clientChannel.CloseAsync();
        }

        if (serverChannel != null)
        {
            _ = serverChannel.CloseAsync();
        }

        if (serverConnectedChannel != null)
        {
            _ = serverConnectedChannel.CloseAsync();
        }

        await eventLoopGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
    }

    // CLR: the client of the source uses Http2MultiplexCodecBuilder.forClient(DISCARD_HANDLER)
    // .autoAckSettingsFrame(false).build(); the deprecated builder is not ported (see the class comment), so the client
    // is its named replacement, Http2FrameCodecBuilder.ForClient().AutoAckSettingsFrame(false).Build() followed by new
    // Http2MultiplexHandler(DISCARD_HANDLER) (AsyncSettingsAck0 adds it). The test checks the asynchronous SETTINGS ACK
    // of the server's frame codec and multiplex handler, which the replacement keeps.
    [Fact(Timeout = 10000)]
    public async Task AsyncSettingsAckWithMultiplexHandler()
    {
        await AsyncSettingsAck0(new Http2FrameCodecBuilder(true).Build(),
                new Http2MultiplexHandler(DISCARD_HANDLER));
    }

    private async Task AsyncSettingsAck0(Http2FrameCodec codec, IChannelHandler multiplexer)
    {
        // The client expects 2 settings frames. One from the connection setup and one from this test.
        CountdownEvent serverAckOneLatch = new CountdownEvent(1);
        CountdownEvent serverAckAllLatch = new CountdownEvent(2);
        CountdownEvent clientSettingsLatch = new CountdownEvent(2);
        CountdownEvent serverConnectedChannelLatch = new CountdownEvent(1);
        AtomicReference<IChannel> serverConnectedChannelRef = new AtomicReference<IChannel>();
        ServerBootstrap sb = new ServerBootstrap();
        sb.Group(eventLoopGroup);
        sb.Channel<NioServerSocketChannel>();
        sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
        {
            ch.Pipeline.AddLast(codec);

            if (multiplexer != null)
            {
                ch.Pipeline.AddLast(multiplexer);
            }

            ch.Pipeline.AddLast(new ServerSettingsAckHandler(serverConnectedChannelRef, serverConnectedChannelLatch,
                    serverAckOneLatch, serverAckAllLatch));
        }));
        serverChannel = await sb.BindAsync(new IPEndPoint(NetUtil.LOCALHOST, 0));

        Bootstrap bs = new Bootstrap();
        bs.Group(eventLoopGroup);
        bs.Channel<NioSocketChannel>();
        bs.Handler(new ActionChannelInitializer<IChannel>(ch =>
        {
            ch.Pipeline.AddLast(ForClient().AutoAckSettingsFrame(false).Build());
            ch.Pipeline.AddLast(new Http2MultiplexHandler(DISCARD_HANDLER));
            ch.Pipeline.AddLast(new ClientSettingsHandler(clientSettingsLatch));
        }));
        clientChannel = await bs.ConnectAsync(serverChannel.LocalAddress);
        serverConnectedChannelLatch.Wait(TestContext.Current.CancellationToken);
        serverConnectedChannel = serverConnectedChannelRef.Get();

        await serverConnectedChannel.WriteAndFlushAsync(new DefaultHttp2SettingsFrame(new Http2Settings()
                .MaxConcurrentStreams(10)));

        clientSettingsLatch.Wait(TestContext.Current.CancellationToken);

        // We expect a timeout here because we want to asynchronously generate the SETTINGS ACK below.
        Assert.False(serverAckOneLatch.Wait(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken));

        // We expect 2 settings frames, the initial settings frame during connection establishment and the setting frame
        // written in this test. We should ack both of these settings frames.
        await clientChannel.WriteAndFlushAsync(IHttp2SettingsAckFrame.INSTANCE);
        await clientChannel.WriteAndFlushAsync(IHttp2SettingsAckFrame.INSTANCE);

        serverAckAllLatch.Wait(TestContext.Current.CancellationToken);
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of the server pipeline in asyncSettingsAck0.
    private sealed class ServerSettingsAckHandler : ChannelInboundHandlerAdapter
    {
        private readonly AtomicReference<IChannel> serverConnectedChannelRef;
        private readonly CountdownEvent serverConnectedChannelLatch;
        private readonly CountdownEvent serverAckOneLatch;
        private readonly CountdownEvent serverAckAllLatch;

        internal ServerSettingsAckHandler(AtomicReference<IChannel> serverConnectedChannelRef,
                                          CountdownEvent serverConnectedChannelLatch, CountdownEvent serverAckOneLatch,
                                          CountdownEvent serverAckAllLatch)
        {
            this.serverConnectedChannelRef = serverConnectedChannelRef;
            this.serverConnectedChannelLatch = serverConnectedChannelLatch;
            this.serverAckOneLatch = serverAckOneLatch;
            this.serverAckAllLatch = serverAckAllLatch;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            serverConnectedChannelRef.Set(ctx.Channel);
            CountDown(serverConnectedChannelLatch);
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is IHttp2SettingsAckFrame)
            {
                CountDown(serverAckOneLatch);
                CountDown(serverAckAllLatch);
            }

            ReferenceCountUtil.Release(msg);
        }
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of the client pipeline in asyncSettingsAck0.
    private sealed class ClientSettingsHandler : ChannelInboundHandlerAdapter
    {
        private readonly CountdownEvent clientSettingsLatch;

        internal ClientSettingsHandler(CountdownEvent clientSettingsLatch)
        {
            this.clientSettingsLatch = clientSettingsLatch;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is IHttp2SettingsFrame)
            {
                CountDown(clientSettingsLatch);
            }

            ReferenceCountUtil.Release(msg);
        }
    }

    // CLR: Executors.newScheduledThreadPool(1) is a DefaultEventExecutorGroup(1) and schedule(runnable, 500,
    // MILLISECONDS) is ScheduleAsync(action, TimeSpan.FromMilliseconds(500)) (ScheduledExecutorService is
    // IEventExecutorGroup, docs/handler-porting.md, Executors and scheduling); shutdown() starts the shutdown without
    // waiting, as the discarded ShutdownGracefullyAsync() Task.
    [Fact(Timeout = 5000)]
    public async Task TestFlushNotDiscarded()
    {
        IEventExecutorGroup executorService = new DefaultEventExecutorGroup(1);

        try
        {
            ServerBootstrap sb = new ServerBootstrap();
            sb.Group(eventLoopGroup);
            sb.Channel<NioServerSocketChannel>();
            sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(new Http2FrameCodecBuilder(true).Build());
                ch.Pipeline.AddLast(new Http2MultiplexHandler(new DelayedResponseHandler(executorService)));
            }));
            serverChannel = await sb.BindAsync(new IPEndPoint(NetUtil.LOCALHOST, 0));

            CountdownEvent latch = new CountdownEvent(1);
            Bootstrap bs = new Bootstrap();
            bs.Group(eventLoopGroup);
            bs.Channel<NioSocketChannel>();
            bs.Handler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(new Http2FrameCodecBuilder(false).Build());
                ch.Pipeline.AddLast(new Http2MultiplexHandler(DISCARD_HANDLER));
            }));
            clientChannel = await bs.ConnectAsync(serverChannel.LocalAddress);
            Http2StreamChannelBootstrap h2Bootstrap = new Http2StreamChannelBootstrap(clientChannel);
            h2Bootstrap.Handler(new EndStreamDataLatchHandler(latch, false));
            IHttp2StreamChannel streamChannel = await h2Bootstrap.OpenAsync();
            await streamChannel.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(new DefaultHttp2Headers(), true));

            latch.Wait(TestContext.Current.CancellationToken);
        }
        finally
        {
            _ = executorService.ShutdownGracefullyAsync();
        }
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of the server's Http2MultiplexHandler in testFlushNotDiscarded,
    // with its anonymous Runnables as lambdas.
    private sealed class DelayedResponseHandler : ChannelInboundHandlerAdapter
    {
        private readonly IEventExecutorGroup executorService;

        internal DelayedResponseHandler(IEventExecutorGroup executorService)
        {
            this.executorService = executorService;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is IHttp2HeadersFrame && ((IHttp2HeadersFrame) msg).IsEndStream())
            {
                _ = executorService.ScheduleAsync(() =>
                {
                    Http2CodecUtil.AddListener(ctx, ctx.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(
                            new DefaultHttp2Headers(), false)),
                            future =>
                            {
                                _ = ctx.WriteAsync(new DefaultHttp2DataFrame(
                                        Unpooled.CopiedBuffer(
                                                "Hello World", Encoding.ASCII),
                                        true));
                                ctx.Channel.EventLoop.Execute(() =>
                                {
                                    ctx.Flush();
                                });
                            });
                }, TimeSpan.FromMilliseconds(500));
            }

            ReferenceCountUtil.Release(msg);
        }
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter instances of testFlushNotDiscarded,
    // testFireChannelReadAfterHandshakeSuccess and streamHandlerInactivatedResponseFlushed that count down a latch on
    // an end-of-stream DATA frame; the last one also returns true from isSharable(), which sharable selects.
    private sealed class EndStreamDataLatchHandler : ChannelInboundHandlerAdapter
    {
        private readonly CountdownEvent latch;
        private readonly bool sharable;

        internal EndStreamDataLatchHandler(CountdownEvent latch, bool sharable)
        {
            this.latch = latch;
            this.sharable = sharable;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is IHttp2DataFrame && ((IHttp2DataFrame) msg).IsEndStream())
            {
                CountDown(latch);
            }

            ReferenceCountUtil.Release(msg);
        }

        public override bool IsSharable => sharable || base.IsSharable;
    }

    // CLR: @Disabled is [Fact(Skip = ...)] with the annotation's reason: JUnit's @Disabled signals that the test "is
    // currently disabled" and its value is "The reason this annotated test class or test method is disabled"
    // (https://docs.junit.org/current/api/org.junit.jupiter.api/org/junit/jupiter/api/Disabled.html); a [Fact] with
    // Skip set is a "statically skipped" test (https://xunit.net/docs/getting-started/v3/whats-new). The body is
    // ported, as the source compiles it. On SslStream the TLSv1.2 body would not pass either: measured with the cipher
    // restriction removed (Windows has no CipherSuitesPolicy), the client handshake succeeds before the server rejects
    // the client certificate, because SslStream validates the peer after its handshake loop (docs/handler-porting.md,
    // Measurements and platform limits: untrusted certificate), so the body reports "TLSv1.3 expected"; the TLSv1.3
    // body passed.
    [Fact(Timeout = 5000, Skip = "JDK SSLEngine does not produce an alert")]
    public async Task TestSSLExceptionJDKTLSv12()
    {
        await TestSslException(SslProvider.JDK, false);
    }

    // CLR: see TestSSLExceptionJDKTLSv12.
    [Fact(Timeout = 5000, Skip = "JDK SSLEngine does not produce an alert")]
    public async Task TestSSLExceptionJDKTLSv13()
    {
        await TestSslException(SslProvider.JDK, true);
    }

    // CLR: assumeTrue is Xunit.Assert.SkipUnless ("will skip the test if condition is false",
    // https://xunit.net/docs/getting-started/v3/whats-new). The anonymous X509TrustManager is a
    // RemoteCertificateValidationCallback (docs/handler-porting.md, JSSE and JCA on the BCL: TrustManager), see
    // ExpiredTrustManager. cert.getKeyPair().getPrivate() is the pkitesting bundle's key pair, an AsymmetricAlgorithm
    // (docs/handler-porting.md, java.security.PrivateKey), and getCertificatePath() is GetCertificatePath().
    // SSLException is AuthenticationException (docs/handler-porting.md, JSSE and JCA on the BCL) and cause.getCause()
    // is InnerException. Http2SecurityUtil.CIPHERS needs a CipherSuitesPolicy, which Windows does not have, so
    // SslContext.NewHandler throws PlatformNotSupportedException there (docs/handler-porting.md,
    // setEnabledCipherSuites).
    private async Task TestSslException(SslProvider provider, bool tlsv13)
    {
        Assert.SkipUnless(SslProvider.IsAlpnSupported(provider), "SslProvider.isAlpnSupported(provider)");

        if (tlsv13)
        {
            Assert.SkipUnless(SslProvider.IsTlsv13Supported(provider), "SslProvider.isTlsv13Supported(provider)");
        }

        string protocol = tlsv13 ? "TLSv1.3" : "TLSv1.2";
        X509Bundle cert = new CertificateBuilder()
                .Subject("cn=localhost")
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();
        SslContext sslCtx = null;
        SslContext clientCtx = null;

        try
        {
            sslCtx = SslContextBuilder.ForServer((AsymmetricAlgorithm) cert.GetKeyPair(), cert.GetCertificatePath())
                    .TrustManager(ExpiredTrustManager).SslProvider(provider)
                    .Ciphers(Http2SecurityUtil.CIPHERS, SupportedCipherSuiteFilter.INSTANCE)
                    .Protocols(protocol)
                    .ApplicationProtocolConfig(new ApplicationProtocolConfig(
                            ApplicationProtocolConfig.Protocol.ALPN,
                            // NO_ADVERTISE is currently the only mode supported by both OpenSsl and JDK providers.
                            ApplicationProtocolConfig.SelectorFailureBehavior.NO_ADVERTISE,
                            // ACCEPT is currently the only mode supported by both OpenSsl and JDK providers.
                            ApplicationProtocolConfig.SelectedListenerFailureBehavior.ACCEPT,
                            ApplicationProtocolNames.HTTP_2,
                            ApplicationProtocolNames.HTTP_1_1)).ClientAuth(ClientAuth.REQUIRE)
                    .Build();
            SslContext sslCtx0 = sslCtx;

            ServerBootstrap sb = new ServerBootstrap();
            sb.Group(eventLoopGroup);
            sb.Channel<NioServerSocketChannel>();
            sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(sslCtx0.NewHandler(ch.Alloc()));
                ch.Pipeline.AddLast(new Http2FrameCodecBuilder(true).Build());
                ch.Pipeline.AddLast(new Http2MultiplexHandler(DISCARD_HANDLER));
            }));
            serverChannel = await sb.BindAsync(new IPEndPoint(NetUtil.LOCALHOST, 0));

            clientCtx = SslContextBuilder.ForClient()
                    .KeyManager((AsymmetricAlgorithm) cert.GetKeyPair(), cert.GetCertificatePath())
                    .SslProvider(provider)
                    /* NOTE: the cipher filter may not include all ciphers required by the HTTP/2 specification.
                     * Please refer to the HTTP/2 specification for cipher requirements. */
                    .Ciphers(Http2SecurityUtil.CIPHERS, SupportedCipherSuiteFilter.INSTANCE)
                    .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                    .Protocols(protocol)
                    .ApplicationProtocolConfig(new ApplicationProtocolConfig(
                            ApplicationProtocolConfig.Protocol.ALPN,
                            // NO_ADVERTISE is currently the only mode supported by both OpenSsl and JDK providers.
                            ApplicationProtocolConfig.SelectorFailureBehavior.NO_ADVERTISE,
                            // ACCEPT is currently the only mode supported by both OpenSsl and JDK providers.
                            ApplicationProtocolConfig.SelectedListenerFailureBehavior.ACCEPT,
                            ApplicationProtocolNames.HTTP_2,
                            ApplicationProtocolNames.HTTP_1_1))
                    .Build();

            CountdownEvent latch = new CountdownEvent(2);
            AtomicReference<Exception> errorRef = new AtomicReference<Exception>();
            SslContext clientCtx0 = clientCtx;
            Bootstrap bs = new Bootstrap();
            bs.Group(eventLoopGroup);
            bs.Channel<NioSocketChannel>();
            bs.Handler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(clientCtx0.NewHandler(ch.Alloc()));
                ch.Pipeline.AddLast(new Http2FrameCodecBuilder(false).Build());
                ch.Pipeline.AddLast(new Http2MultiplexHandler(DISCARD_HANDLER));
                ch.Pipeline.AddLast(new SslExceptionClientHandler(tlsv13, latch, errorRef));
            }));
            clientChannel = await bs.ConnectAsync(serverChannel.LocalAddress);
            latch.Wait(TestContext.Current.CancellationToken);
            Exception error = errorRef.Get();
            if (error != null)
            {
                throw error;
            }
        }
        finally
        {
            ReferenceCountUtil.Release(clientCtx);
            ReferenceCountUtil.Release(sslCtx);
        }
    }

    // CLR: the anonymous X509TrustManager of testSslException. Both checkClientTrusted and checkServerTrusted throw
    // CertificateExpiredException, the JSSE way to reject the peer's chain; a validation callback rejects it by
    // returning false ("a rejection returns false", docs/handler-porting.md, JSSE and JCA on the BCL: TrustManager),
    // after which SslStream sends the fatal alert the test waits for, as ParameterizedSslHandlerTest's failing trust
    // manager does. getAcceptedIssuers() has no callback counterpart.
    private static bool ExpiredTrustManager(object sender, X509Certificate certificate, X509Chain chain,
                                            SslPolicyErrors sslPolicyErrors)
    {
        return false;
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of the client pipeline in testSslException.
    // h2Bootstrap.open().addListener(FutureListener) is Http2CodecUtil.AddListener on the OpenAsync Task, and
    // future.getNow() its Result once it completed successfully. AssertionError is XunitException.
    private sealed class SslExceptionClientHandler : ChannelInboundHandlerAdapter
    {
        private readonly bool tlsv13;
        private readonly CountdownEvent latch;
        private readonly AtomicReference<Exception> errorRef;

        internal SslExceptionClientHandler(bool tlsv13, CountdownEvent latch, AtomicReference<Exception> errorRef)
        {
            this.tlsv13 = tlsv13;
            this.latch = latch;
            this.errorRef = errorRef;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is SslHandshakeCompletionEvent)
            {
                SslHandshakeCompletionEvent handshakeCompletionEvent =
                        (SslHandshakeCompletionEvent) evt;
                if (handshakeCompletionEvent.IsSuccess())
                {
                    // In case of TLSv1.3 we should succeed the handshake. The alert for
                    // the mTLS failure will be send in the next round-trip.
                    if (!tlsv13)
                    {
                        errorRef.Set(new XunitException("TLSv1.3 expected"));
                    }

                    Http2StreamChannelBootstrap h2Bootstrap =
                            new Http2StreamChannelBootstrap(ctx.Channel);
                    h2Bootstrap.Handler(new SslExceptionStreamHandler(latch));
                    Task<IHttp2StreamChannel> openFuture = h2Bootstrap.OpenAsync();
                    Http2CodecUtil.AddListener(ctx, openFuture, future =>
                    {
                        if (future.IsCompletedSuccessfully)
                        {
                            _ = openFuture.Result.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(
                                    new DefaultHttp2Headers(), false));
                        }
                    });

                }
                else if (handshakeCompletionEvent.Cause() is AuthenticationException)
                {
                    // In case of TLSv1.2 we should never see the handshake succeed as the alert for
                    // the mTLS failure will be send in the same round-trip.
                    if (tlsv13)
                    {
                        errorRef.Set(new XunitException("TLSv1.2 expected"));
                    }

                    CountDown(latch);
                    CountDown(latch);
                }
            }
        }
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of the stream channel in testSslException.
    private sealed class SslExceptionStreamHandler : ChannelInboundHandlerAdapter
    {
        private readonly CountdownEvent latch;

        internal SslExceptionStreamHandler(CountdownEvent latch)
        {
            this.latch = latch;
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (cause.InnerException is AuthenticationException)
            {
                CountDown(latch);
            }
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            CountDown(latch);
        }
    }

    // CLR: @DisabledOnOs(OS.WINDOWS) "is used to signal that the annotated test class or test method is disabled on one
    // or more specified" operating systems
    // (https://docs.junit.org/current/api/org.junit.jupiter.api/org/junit/jupiter/api/condition/DisabledOnOs.html);
    // Xunit.Assert.SkipWhen "will skip the test if condition is true"
    // (https://xunit.net/docs/getting-started/v3/whats-new), with the annotation's disabledReason. On Windows the port
    // could not run the source's context either: its Http2SecurityUtil.CIPHERS need a CipherSuitesPolicy, which Windows
    // does not have (docs/handler-porting.md, setEnabledCipherSuites and SSL tests: "Assert.Skip on Windows"). Measured
    // with that restriction removed, the body passed on this Windows host in three of three runs.
    [Fact(Timeout = 5000)]
    public async Task TestFireChannelReadAfterHandshakeSuccess_JDK()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "See: https://github.com/netty/netty/issues/11542");
        Assert.SkipUnless(SslProvider.IsAlpnSupported(SslProvider.JDK), "SslProvider.isAlpnSupported(SslProvider.JDK)");
        await TestFireChannelReadAfterHandshakeSuccess(SslProvider.JDK);
    }

    // CLR: see TestSslException for the SSL mappings.
    private async Task TestFireChannelReadAfterHandshakeSuccess(SslProvider provider)
    {
        X509Bundle cert = new CertificateBuilder()
                .Subject("cn=localhost")
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();
        SslContext serverCtx = null;
        SslContext clientCtx = null;

        try
        {
            serverCtx = SslContextBuilder.ForServer((AsymmetricAlgorithm) cert.GetKeyPair(),
                            cert.GetCertificatePath())
                    .SslProvider(provider)
                    .Ciphers(Http2SecurityUtil.CIPHERS, SupportedCipherSuiteFilter.INSTANCE)
                    .ApplicationProtocolConfig(new ApplicationProtocolConfig(
                            ApplicationProtocolConfig.Protocol.ALPN,
                            ApplicationProtocolConfig.SelectorFailureBehavior.NO_ADVERTISE,
                            ApplicationProtocolConfig.SelectedListenerFailureBehavior.ACCEPT,
                            ApplicationProtocolNames.HTTP_2,
                            ApplicationProtocolNames.HTTP_1_1))
                    .Build();
            SslContext serverCtx0 = serverCtx;

            ServerBootstrap sb = new ServerBootstrap();
            sb.Group(eventLoopGroup);
            sb.Channel<NioServerSocketChannel>();
            sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(serverCtx0.NewHandler(ch.Alloc()));
                ch.Pipeline.AddLast(new Http2NegotiationHandler());
            }));
            serverChannel = await sb.BindAsync(new IPEndPoint(NetUtil.LOCALHOST, 0));

            clientCtx = SslContextBuilder.ForClient()
                    .SslProvider(provider)
                    .Ciphers(Http2SecurityUtil.CIPHERS, SupportedCipherSuiteFilter.INSTANCE)
                    .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                    .ApplicationProtocolConfig(new ApplicationProtocolConfig(
                            ApplicationProtocolConfig.Protocol.ALPN,
                            ApplicationProtocolConfig.SelectorFailureBehavior.NO_ADVERTISE,
                            ApplicationProtocolConfig.SelectedListenerFailureBehavior.ACCEPT,
                            ApplicationProtocolNames.HTTP_2,
                            ApplicationProtocolNames.HTTP_1_1))
                    .Build();
            SslContext clientCtx0 = clientCtx;

            CountdownEvent latch = new CountdownEvent(1);
            Bootstrap bs = new Bootstrap();
            bs.Group(eventLoopGroup);
            bs.Channel<NioSocketChannel>();
            bs.Handler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(clientCtx0.NewHandler(ch.Alloc()));
                ch.Pipeline.AddLast(new Http2FrameCodecBuilder(false).Build());
                ch.Pipeline.AddLast(new Http2MultiplexHandler(DISCARD_HANDLER));
                ch.Pipeline.AddLast(new HandshakeSuccessClientHandler(latch));
            }));
            clientChannel = await bs.ConnectAsync(serverChannel.LocalAddress);

            latch.Wait(TestContext.Current.CancellationToken);
        }
        finally
        {
            ReferenceCountUtil.Release(serverCtx);
            ReferenceCountUtil.Release(clientCtx);
        }
    }

    // CLR: the anonymous ApplicationProtocolNegotiationHandler of testFireChannelReadAfterHandshakeSuccess.
    private sealed class Http2NegotiationHandler : ApplicationProtocolNegotiationHandler
    {
        internal Http2NegotiationHandler() : base(ApplicationProtocolNames.HTTP_1_1)
        {
        }

        protected override void ConfigurePipeline(IChannelHandlerContext ctx, string protocol)
        {
            ctx.Pipeline.AddLast(new Http2FrameCodecBuilder(true).Build());
            ctx.Pipeline.AddLast(new Http2MultiplexHandler(new HelloWorldStreamHandler()));
        }
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of the server's Http2MultiplexHandler in
    // testFireChannelReadAfterHandshakeSuccess.
    private sealed class HelloWorldStreamHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is IHttp2HeadersFrame && ((IHttp2HeadersFrame) msg).IsEndStream())
            {
                ByteBuf content = Unpooled.CopiedBuffer("Hello World", Encoding.ASCII);
                Http2CodecUtil.AddListener(ctx, ctx.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(
                                new DefaultHttp2Headers(), false)),
                        future =>
                                ctx.WriteAndFlushAsync(
                                        new DefaultHttp2DataFrame(content, true)));
            }

            ReferenceCountUtil.Release(msg);
        }
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of the client pipeline in
    // testFireChannelReadAfterHandshakeSuccess.
    private sealed class HandshakeSuccessClientHandler : ChannelInboundHandlerAdapter
    {
        private readonly CountdownEvent latch;

        internal HandshakeSuccessClientHandler(CountdownEvent latch)
        {
            this.latch = latch;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is SslHandshakeCompletionEvent)
            {
                SslHandshakeCompletionEvent handshakeCompletionEvent =
                        (SslHandshakeCompletionEvent) evt;
                if (handshakeCompletionEvent.IsSuccess())
                {
                    Http2StreamChannelBootstrap h2Bootstrap =
                            new Http2StreamChannelBootstrap(ctx.Channel);
                    h2Bootstrap.Handler(new EndStreamDataLatchHandler(latch, false));
                    Task<IHttp2StreamChannel> openFuture = h2Bootstrap.OpenAsync();
                    Http2CodecUtil.AddListener(ctx, openFuture, future =>
                    {
                        if (future.IsCompletedSuccessfully)
                        {
                            _ = openFuture.Result.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(
                                    new DefaultHttp2Headers(), true));
                        }
                    });
                }
            }
        }
    }

    /**
     * When an HTTP/2 server stream channel receives a frame with EOS flag, and when it responds with a EOS
     * flag, then the server side stream will be closed, hence the stream handler will be inactivated. This test
     * verifies that the ChannelFuture of the server response is successful at the time the server stream handler is
     * inactivated.
     */
    // CLR: new MultiThreadIoEventLoopGroup(1, ThreadFactory, NioIoHandler.newFactory()) takes Common's IThreadFactory
    // (NewThread(Action)), implemented by the nested NamedThreadFactory for the two anonymous ThreadFactory classes;
    // new Thread(r, name) is a System.Threading.Thread with that Name. ch.config().setOption(SO_SNDBUF, 1) is
    // Config.SetOption(ChannelOption.SO_SNDBUF, 1). The anonymous ChannelInitializer that removes itself is the nested
    // StreamInitializer, because the lambda of an ActionChannelInitializer cannot name the initializer (this).
    // shutdownGracefully(0, 0, MILLISECONDS) is not awaited, as in the source.
    [Fact(Timeout = 120000)]
    public async Task StreamHandlerInactivatedResponseFlushed()
    {
        IEventLoopGroup serverEventLoopGroup = null;
        IEventLoopGroup clientEventLoopGroup = null;

        try
        {
            serverEventLoopGroup = new MultiThreadIoEventLoopGroup(1, new NamedThreadFactory("serverloop"),
                    NioIoHandler.NewFactory());

            clientEventLoopGroup = new MultiThreadIoEventLoopGroup(1, new NamedThreadFactory("clientloop"),
                    NioIoHandler.NewFactory());

            int streams = 10;
            CountdownEvent latchClientResponses = new CountdownEvent(streams);
            CountdownEvent latchHandlerInactive = new CountdownEvent(streams);

            AtomicInteger handlerInactivatedFlushed = new AtomicInteger();
            AtomicInteger handleInactivatedNotFlushed = new AtomicInteger();
            ServerBootstrap sb = new ServerBootstrap();

            sb.Group(serverEventLoopGroup);
            sb.Channel<NioServerSocketChannel>();
            sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
            {
                // using a short sndbuf size will trigger writability events
                ch.Config.SetOption(ChannelOption.SO_SNDBUF, 1);
                ch.Pipeline.AddLast(new Http2FrameCodecBuilder(true).Build());
                ch.Pipeline.AddLast(new Http2MultiplexHandler(new StreamInitializer(handlerInactivatedFlushed,
                        handleInactivatedNotFlushed, latchHandlerInactive)));
            }));
            serverChannel = await sb.BindAsync(new IPEndPoint(NetUtil.LOCALHOST, 0));

            Bootstrap bs = new Bootstrap();

            bs.Group(clientEventLoopGroup);
            bs.Channel<NioSocketChannel>();
            bs.Handler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(new Http2FrameCodecBuilder(false).Build());
                ch.Pipeline.AddLast(new Http2MultiplexHandler(DISCARD_HANDLER));
            }));

            clientChannel = await bs.ConnectAsync(serverChannel.LocalAddress);
            Http2StreamChannelBootstrap h2Bootstrap = new Http2StreamChannelBootstrap(clientChannel);
            h2Bootstrap.Handler(new EndStreamDataLatchHandler(latchClientResponses, true));

            List<Task> streamFutures = new List<Task>();

            for (int i = 0; i < streams; i ++)
            {
                IHttp2StreamChannel stream = await h2Bootstrap.OpenAsync();
                streamFutures.Add(stream.WriteAndFlushAsync(
                        new DefaultHttp2HeadersFrame(new DefaultHttp2Headers(), true)));
            }

            for (int i = 0; i < streams; i ++)
            {
                await streamFutures[i];
            }

            Assert.True(latchHandlerInactive.Wait(TimeSpan.FromMilliseconds(120000),
                    TestContext.Current.CancellationToken));
            Assert.True(latchClientResponses.Wait(TimeSpan.FromMilliseconds(120000),
                    TestContext.Current.CancellationToken));
            Assert.Equal(0, handleInactivatedNotFlushed.Get());
            Assert.Equal(streams, handlerInactivatedFlushed.Get());
        }
        finally
        {
            if (serverEventLoopGroup != null)
            {
                _ = serverEventLoopGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            }

            if (clientEventLoopGroup != null)
            {
                _ = clientEventLoopGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            }
        }
    }

    // CLR: the two anonymous ThreadFactory classes of streamHandlerInactivatedResponseFlushed.
    private sealed class NamedThreadFactory : IThreadFactory
    {
        private readonly string name;

        internal NamedThreadFactory(string name)
        {
            this.name = name;
        }

        public Thread NewThread(Action r)
        {
            return new Thread(() => r()) { Name = name };
        }
    }

    // CLR: the anonymous ChannelInitializer<Channel> of the server's Http2MultiplexHandler in
    // streamHandlerInactivatedResponseFlushed.
    private sealed class StreamInitializer : ChannelInitializer<IChannel>
    {
        private readonly AtomicInteger handlerInactivatedFlushed;
        private readonly AtomicInteger handleInactivatedNotFlushed;
        private readonly CountdownEvent latchHandlerInactive;

        internal StreamInitializer(AtomicInteger handlerInactivatedFlushed, AtomicInteger handleInactivatedNotFlushed,
                                   CountdownEvent latchHandlerInactive)
        {
            this.handlerInactivatedFlushed = handlerInactivatedFlushed;
            this.handleInactivatedNotFlushed = handleInactivatedNotFlushed;
            this.latchHandlerInactive = latchHandlerInactive;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Pipeline.Remove(this);
            ch.Pipeline.AddLast(new MultiplexInboundStream(handlerInactivatedFlushed,
                    handleInactivatedNotFlushed, latchHandlerInactive));
        }
    }

    // CLR: the anonymous ChannelInitializer bodies are ActionChannelInitializer delegates; the anonymous
    // SimpleChannelInboundHandler classes are the nested FullHttpRequestResponder and FullHttpResponseLatch.
    // group.shutdownGracefully(0, 3, SECONDS) is not awaited, as in the source.
    [Fact]
    public async Task TestServerCloseShouldNotSendResetIfClientSentEOS()
    {
        IEventLoopGroup group = null;
        IChannel serverChannel = null;
        IChannel clientChannel = null;
        IChannel clientStreamChannel = null;

        try
        {
            CountdownEvent clientReceivedResponseLatch = new CountdownEvent(1);
            CountdownEvent resetFrameLatch = new CountdownEvent(1);
            group = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());
            LocalAddress serverAddress = new LocalAddress(GetType().FullName);
            ServerBootstrap sb = new ServerBootstrap()
                    .Channel<LocalServerChannel>()
                    .Group(group)
                    .ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
                    {
                        IChannelPipeline pipeline = ch.Pipeline;
                        pipeline.AddLast(ForServer().Build());
                        pipeline.AddLast(new Http2FrameIgnore<IHttp2SettingsFrame>(typeof(IHttp2SettingsFrame)));
                        pipeline.AddLast(new Http2FrameIgnore<IHttp2SettingsAckFrame>(typeof(IHttp2SettingsAckFrame)));
                        pipeline.AddLast(new Http2MultiplexHandler(
                                new ActionChannelInitializer<IHttp2StreamChannel>(ch =>
                                {
                                    IChannelPipeline pipeline = ch.Pipeline;
                                    pipeline.AddLast(new Http2StreamFrameToHttpObjectCodec(true, true));
                                    pipeline.AddLast(new HttpObjectAggregator(16384));
                                    pipeline.AddLast(new FullHttpRequestResponder());
                                })));
                    }));
            serverChannel = await sb.BindAsync(serverAddress);

            Bootstrap cb = new Bootstrap()
                    .Channel<LocalChannel>()
                    .Group(group)
                    .Handler(new ActionChannelInitializer<IChannel>(ch =>
                    {
                        IChannelPipeline pipeline = ch.Pipeline;
                        pipeline.AddLast(ForClient().Build());
                        pipeline.AddLast(new Http2FrameIgnore<IHttp2SettingsFrame>(typeof(IHttp2SettingsFrame)));
                        pipeline.AddLast(new Http2FrameIgnore<IHttp2SettingsAckFrame>(typeof(IHttp2SettingsAckFrame)));
                        pipeline.AddLast(new Http2MultiplexHandler(
                                new ActionChannelInitializer<IHttp2StreamChannel>(ch =>
                                {
                                    // noop
                                })));
                    }));

            clientChannel = await cb.ConnectAsync(serverAddress);
            clientStreamChannel = await new Http2StreamChannelBootstrap(clientChannel)
                    .Handler(new ActionChannelInitializer<IChannel>(ch =>
                    {
                        IChannelPipeline pipeline = ch.Pipeline;
                        pipeline.AddLast(new Http2StreamFrameToHttpObjectCodec(false, true));
                        pipeline.AddLast(new HttpObjectAggregator(16384));
                        pipeline.AddLast(new FullHttpResponseLatch(clientReceivedResponseLatch));
                    }))
                    .OpenAsync();

            await clientStreamChannel.WriteAndFlushAsync(
                    new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/test/"));

            Assert.True(clientReceivedResponseLatch.Wait(TimeSpan.FromSeconds(3),
                    TestContext.Current.CancellationToken));

            // The server should NOT send any RST_STREAM frame.
            Assert.False(resetFrameLatch.Wait(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        }
        finally
        {
            if (clientStreamChannel != null)
            {
                await clientStreamChannel.CloseAsync();
            }

            if (clientChannel != null)
            {
                await clientChannel.CloseAsync();
            }

            if (serverChannel != null)
            {
                await serverChannel.CloseAsync();
            }

            if (group != null)
            {
                _ = group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(3));
            }
        }
    }

    // CLR: the anonymous SimpleChannelInboundHandler<FullHttpRequest> of the server stream pipeline in
    // testServerCloseShouldNotSendResetIfClientSentEOS; addListeners(ChannelFutureListener.CLOSE) closes ctx.Channel,
    // the channel of the write (see the class comment).
    private sealed class FullHttpRequestResponder : SimpleChannelInboundHandler<IFullHttpRequest>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, IFullHttpRequest msg)
        {
            Http2CodecUtil.AddListener(ctx, ctx.WriteAndFlushAsync(
                    new DefaultFullHttpResponse(
                            msg.ProtocolVersion(), HttpResponseStatus.OK,
                            Unpooled.CopiedBuffer("hello", Encoding.ASCII))),
                    future => ctx.Channel.CloseAsync());
        }
    }

    // CLR: the anonymous SimpleChannelInboundHandler<FullHttpResponse> of the client stream pipeline in
    // testServerCloseShouldNotSendResetIfClientSentEOS.
    private sealed class FullHttpResponseLatch : SimpleChannelInboundHandler<IFullHttpResponse>
    {
        private readonly CountdownEvent clientReceivedResponseLatch;

        internal FullHttpResponseLatch(CountdownEvent clientReceivedResponseLatch)
        {
            this.clientReceivedResponseLatch = clientReceivedResponseLatch;
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, IFullHttpResponse msg)
        {
            CountDown(clientReceivedResponseLatch);
        }
    }

    // https://github.com/netty/netty/issues/17600
    // CLR: (byte) i keeps the low eight bits in both languages (the project is unchecked), so the payload octets are
    // Java's; assertArrayEquals compares octets, which no byte sign changes. ch.eventLoop().schedule(runnable, 500,
    // MILLISECONDS) is ScheduleAsync(action, TimeSpan.FromMilliseconds(500)) (docs/handler-porting.md, Executors and
    // scheduling). data.content() is the Content property, readableBytes() ReadableBytes. The anonymous handlers are
    // the nested PayloadResponder, DataCollector, DataForwarder and WritabilityToAutoReadMirror, and
    // shutdownGracefully(0, 0, MILLISECONDS) is not awaited, as in the source.
    [Fact(Timeout = 30000)]
    public async Task ProxyMirroringWritabilityToAutoReadAcrossEventLoopsDoesNotStallStream()
    {
        // Large enough to force many rounds of the (default 64KiB) HTTP/2 stream flow-control window.
        int payloadLength = 2 * 1024 * 1024;
        byte[] payloadBytes = new byte[payloadLength];

        for (int i = 0; i < payloadLength; i++)
        {
            payloadBytes[i] = (byte) i;
        }

        // Two independent Http2 "connections" (each with its own EventLoop/thread) are proxied together: data
        // received on "in" is forwarded onto "out", and "out"'s writability is mirrored onto "in"'s auto-read
        // state -- exactly the pattern from https://github.com/netty/netty/issues/17600. Because "in" and "out"
        // live on different EventLoops, this ends up calling setAutoRead() across threads.
        IEventLoopGroup inGroup = null;
        IEventLoopGroup outGroup = null;
        IChannel inServerChannel = null;
        IChannel inClientChannel = null;
        IChannel outServerChannel = null;
        IChannel outClientChannel = null;
        IHttp2StreamChannel inStream = null;
        IHttp2StreamChannel outStream = null;
        AtomicReference<ByteBuf> receivedHolder = new AtomicReference<ByteBuf>();

        try
        {
            inGroup = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());
            outGroup = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());

            LocalAddress inAddress = new LocalAddress(GetType().FullName + ".in");
            LocalAddress outAddress = new LocalAddress(GetType().FullName + ".out");

            // "in" server: sends the whole payload as fast as its peer's flow-control window allows.
            ServerBootstrap inSb = new ServerBootstrap();
            inSb.Group(inGroup);
            inSb.Channel<LocalServerChannel>();
            inSb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(new Http2FrameCodecBuilder(true).Build());
                ch.Pipeline.AddLast(new Http2MultiplexHandler(new PayloadResponder(payloadBytes)));
            }));
            inServerChannel = await inSb.BindAsync(inAddress);

            Bootstrap inBs = new Bootstrap();
            inBs.Group(inGroup);
            inBs.Channel<LocalChannel>();
            inBs.Handler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(new Http2FrameCodecBuilder(false).Build());
                ch.Pipeline.AddLast(new Http2MultiplexHandler(DISCARD_HANDLER));
            }));
            inClientChannel = await inBs.ConnectAsync(inAddress);

            // "out" server: a receiver that (like a slow / congested downstream) doesn't drain anything -- and
            // hence doesn't send WINDOW_UPDATE -- for a little while, so the proxied "out" stream genuinely
            // accumulates a write backlog and becomes unwritable, rather than always draining instantly (as it
            // would with local, same-JVM, always-on auto-read).
            CountdownEvent outComplete = new CountdownEvent(1);
            ByteBuf received = Unpooled.Buffer(payloadLength);
            receivedHolder.Set(received);
            AtomicReference<Exception> failure = new AtomicReference<Exception>();

            ServerBootstrap outSb = new ServerBootstrap();
            outSb.Group(outGroup);
            outSb.Channel<LocalServerChannel>();
            outSb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(new Http2FrameCodecBuilder(true).Build());
                ch.Pipeline.AddLast(new Http2MultiplexHandler(new ActionChannelInitializer<IHttp2StreamChannel>(ch =>
                {
                    ch.Config.SetAutoRead(false);
                    _ = ch.EventLoop.ScheduleAsync(() =>
                    {
                        ch.Config.SetAutoRead(true);
                    }, TimeSpan.FromMilliseconds(500));
                    ch.Pipeline.AddLast(new DataCollector(received, outComplete, failure));
                })));
            }));
            outServerChannel = await outSb.BindAsync(outAddress);

            Bootstrap outBs = new Bootstrap();
            outBs.Group(outGroup);
            outBs.Channel<LocalChannel>();
            outBs.Handler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(new Http2FrameCodecBuilder(false).Build());
                ch.Pipeline.AddLast(new Http2MultiplexHandler(DISCARD_HANDLER));
            }));
            outClientChannel = await outBs.ConnectAsync(outAddress);

            // Open "out" first so "in"'s handler can forward straight onto it.
            outStream = await new Http2StreamChannelBootstrap(outClientChannel).OpenAsync();
            await outStream.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(new DefaultHttp2Headers(), false));

            IHttp2StreamChannel finalOutStream = outStream;
            Http2StreamChannelBootstrap inH2Bootstrap = new Http2StreamChannelBootstrap(inClientChannel);
            inH2Bootstrap.Handler(new DataForwarder(finalOutStream));
            inStream = await inH2Bootstrap.OpenAsync();

            // Mirror "out"'s writability onto "in"'s auto-read state, exactly like
            // https://github.com/netty/netty/issues/17600. "in" and "out" are registered to different EventLoops,
            // so this ends up calling Http2StreamChannelConfig#setAutoRead() from a thread other than the one
            // "in" is registered to.
            IHttp2StreamChannel finalInStream = inStream;
            outStream.Pipeline.AddLast(new WritabilityToAutoReadMirror(finalInStream, finalOutStream));

            await inStream.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(new DefaultHttp2Headers(), true));

            Assert.True(outComplete.Wait(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken),
                    "proxying stalled: never received the full payload");
            Exception cause = failure.Get();
            if (cause != null)
            {
                throw new XunitException(ThrowableUtil.ThrowableToString(cause), cause);
            }

            Assert.Equal(payloadLength, received.ReadableBytes);
            byte[] actual = new byte[payloadLength];
            received.ReadBytes(actual);
            Assert.Equal(payloadBytes, actual);
        }
        finally
        {
            ByteBuf received = receivedHolder.Get();
            if (received != null)
            {
                received.Release();
            }

            if (inStream != null)
            {
                await inStream.CloseAsync();
            }

            if (outStream != null)
            {
                await outStream.CloseAsync();
            }

            if (inClientChannel != null)
            {
                await inClientChannel.CloseAsync();
            }

            if (outClientChannel != null)
            {
                await outClientChannel.CloseAsync();
            }

            if (inServerChannel != null)
            {
                await inServerChannel.CloseAsync();
            }

            if (outServerChannel != null)
            {
                await outServerChannel.CloseAsync();
            }

            if (inGroup != null)
            {
                _ = inGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            }

            if (outGroup != null)
            {
                _ = outGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            }
        }
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of the "in" server's Http2MultiplexHandler in
    // proxyMirroringWritabilityToAutoReadAcrossEventLoopsDoesNotStallStream, and the one of the server in
    // tortureCrossThreadSetAutoReadRace, whose bodies are the same.
    private sealed class PayloadResponder : ChannelInboundHandlerAdapter
    {
        private readonly byte[] payloadBytes;

        internal PayloadResponder(byte[] payloadBytes)
        {
            this.payloadBytes = payloadBytes;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is IHttp2HeadersFrame && ((IHttp2HeadersFrame) msg).IsEndStream())
            {
                _ = ctx.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(new DefaultHttp2Headers(), false));
                _ = ctx.WriteAndFlushAsync(new DefaultHttp2DataFrame(
                        Unpooled.WrappedBuffer(payloadBytes), true));
            }

            ReferenceCountUtil.Release(msg);
        }
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of the "out" server's stream channels in
    // proxyMirroringWritabilityToAutoReadAcrossEventLoopsDoesNotStallStream, and the one of the client stream in
    // tortureCrossThreadSetAutoReadRace, whose bodies are the same (complete is outComplete or dataComplete).
    // catch (Throwable) is catch (Exception) (docs/codec-http-porting.md, Shared CLR adaptations: Exceptions).
    private sealed class DataCollector : ChannelInboundHandlerAdapter
    {
        private readonly ByteBuf received;
        private readonly CountdownEvent complete;
        private readonly AtomicReference<Exception> failure;

        internal DataCollector(ByteBuf received, CountdownEvent complete, AtomicReference<Exception> failure)
        {
            this.received = received;
            this.complete = complete;
            this.failure = failure;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            try
            {
                if (msg is IHttp2DataFrame)
                {
                    IHttp2DataFrame data = (IHttp2DataFrame) msg;
                    received.WriteBytes(data.Content);

                    if (data.IsEndStream())
                    {
                        CountDown(complete);
                    }
                }
            }
            catch (Exception cause)
            {
                failure.CompareAndSet(null, cause);
            }
            finally
            {
                ReferenceCountUtil.Release(msg);
            }
        }
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of the "in" stream channel in
    // proxyMirroringWritabilityToAutoReadAcrossEventLoopsDoesNotStallStream; data.content().retain() is
    // Content.Retain().
    private sealed class DataForwarder : ChannelInboundHandlerAdapter
    {
        private readonly IHttp2StreamChannel finalOutStream;

        internal DataForwarder(IHttp2StreamChannel finalOutStream)
        {
            this.finalOutStream = finalOutStream;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is IHttp2DataFrame)
            {
                IHttp2DataFrame data = (IHttp2DataFrame) msg;
                _ = finalOutStream.WriteAndFlushAsync(
                        new DefaultHttp2DataFrame(data.Content.Retain(), data.IsEndStream()));
            }

            ReferenceCountUtil.Release(msg);
        }
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter added to the "out" stream channel in
    // proxyMirroringWritabilityToAutoReadAcrossEventLoopsDoesNotStallStream; isWritable() is the IsWritable property.
    private sealed class WritabilityToAutoReadMirror : ChannelInboundHandlerAdapter
    {
        private readonly IHttp2StreamChannel finalInStream;
        private readonly IHttp2StreamChannel finalOutStream;

        internal WritabilityToAutoReadMirror(IHttp2StreamChannel finalInStream, IHttp2StreamChannel finalOutStream)
        {
            this.finalInStream = finalInStream;
            this.finalOutStream = finalOutStream;
        }

        public override void ChannelWritabilityChanged(IChannelHandlerContext ctx)
        {
            finalInStream.Config.SetAutoRead(finalOutStream.IsWritable);
        }
    }

    // https://github.com/netty/netty/issues/17600
    // CLR: see ProxyMirroringWritabilityToAutoReadAcrossEventLoopsDoesNotStallStream for the payload, the handlers and
    // the shutdown. new Thread(runnable, "setAutoRead-hammer") is a System.Threading.Thread with that Name, and
    // setDaemon(true) is IsBackground = true: "background threads do not prevent a process from terminating"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.thread.isbackground?view=net-10.0). join(5000) is
    // Join(5000) and interrupt() Interrupt(). LockSupport.parkNanos(1000) disables the thread "for thread scheduling
    // purposes, for up to the specified waiting time"
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/concurrent/locks/LockSupport.html), here
    // at most one microsecond. The BCL has no timed sleep shorter than a whole millisecond (Thread.Sleep(TimeSpan)
    // "uses the total number of whole milliseconds",
    // https://learn.microsoft.com/en-us/dotnet/api/system.threading.thread.sleep?view=net-10.0), and Thread.Sleep(1)
    // toggled only 5 times during the transfer in three measured runs (about 70 ms each; HotSpot on Windows also
    // coarsens such a park to 1 ms, "Must coarsen from nanos to millis", Parker::park in
    // https://raw.githubusercontent.com/openjdk/jdk17u/master/src/hotspot/os/windows/os_windows.cpp), which defeats the
    // "as fast as possible" hammering the comment below asks for. The pause is Thread.Yield(), which yields "the rest
    // of the thread's current time slice" to a ready thread without a timed wait
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.thread.yield?view=net-10.0): 164 921 to 193 103
    // toggles per transfer in three measured runs, all passing. Yield never blocks, so Interrupt() raises no
    // ThreadInterruptedException in the hammer ("If the thread never blocks, the exception is never thrown",
    // https://learn.microsoft.com/en-us/dotnet/api/system.threading.thread.interrupt?view=net-10.0), as parkNanos
    // returns on an interrupt without one.
    [Fact(Timeout = 30000)]
    public async Task TortureCrossThreadSetAutoReadRace()
    {
        // Large enough to require many rounds of the (default 64KiB) HTTP/2 stream flow-control window.
        int payloadLength = 4 * 1024 * 1024;
        byte[] payloadBytes = new byte[payloadLength];

        for (int i = 0; i < payloadLength; i++)
        {
            payloadBytes[i] = (byte) i;
        }

        IEventLoopGroup serverGroup = null;
        IEventLoopGroup clientGroup = null;
        IChannel serverChannel = null;
        IChannel clientChannel = null;
        IHttp2StreamChannel clientStream = null;
        Thread hammer = null;
        AtomicReference<ByteBuf> receivedHolder = new AtomicReference<ByteBuf>();

        try
        {
            serverGroup = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());
            clientGroup = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());
            LocalAddress serverAddress = new LocalAddress(GetType().FullName + ".torture");

            ServerBootstrap sb = new ServerBootstrap();
            sb.Group(serverGroup);
            sb.Channel<LocalServerChannel>();
            sb.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(new Http2FrameCodecBuilder(true).Build());
                ch.Pipeline.AddLast(new Http2MultiplexHandler(new PayloadResponder(payloadBytes)));
            }));
            serverChannel = await sb.BindAsync(serverAddress);

            Bootstrap bs = new Bootstrap();
            bs.Group(clientGroup);
            bs.Channel<LocalChannel>();
            bs.Handler(new ActionChannelInitializer<IChannel>(ch =>
            {
                ch.Pipeline.AddLast(new Http2FrameCodecBuilder(false).Build());
                ch.Pipeline.AddLast(new Http2MultiplexHandler(DISCARD_HANDLER));
            }));
            clientChannel = await bs.ConnectAsync(serverAddress);

            CountdownEvent dataComplete = new CountdownEvent(1);
            ByteBuf received = Unpooled.Buffer(payloadLength);
            receivedHolder.Set(received);
            AtomicReference<Exception> failure = new AtomicReference<Exception>();

            Http2StreamChannelBootstrap h2Bootstrap = new Http2StreamChannelBootstrap(clientChannel);
            h2Bootstrap.Handler(new DataCollector(received, dataComplete, failure));
            clientStream = await h2Bootstrap.OpenAsync();

            IHttp2StreamChannel finalClientStream = clientStream;
            AtomicBoolean stop = new AtomicBoolean();
            // A plain background thread (never the stream's own EventLoop) hammering setAutoRead() as fast as
            // possible for the whole transfer -- the maximum-contention version of the cross-thread,
            // writability-driven auto-read toggling from https://github.com/netty/netty/issues/17600.
            hammer = new Thread(() =>
            {
                bool v = false;

                while (!stop.Get())
                {
                    v = !v;
                    finalClientStream.Config.SetAutoRead(v);
                    Thread.Yield();
                }
            }) { Name = "setAutoRead-hammer" };
            hammer.IsBackground = true;
            hammer.Start();

            await clientStream.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(new DefaultHttp2Headers(), true));

            bool completed = dataComplete.Wait(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            stop.Set(true);
            hammer.Join(5000);

            Assert.True(completed, "stream stalled under concurrent cross-thread setAutoRead() toggling");
            Exception cause = failure.Get();
            if (cause != null)
            {
                throw new XunitException(ThrowableUtil.ThrowableToString(cause), cause);
            }

            Assert.Equal(payloadLength, received.ReadableBytes);
            byte[] actual = new byte[payloadLength];
            received.ReadBytes(actual);
            Assert.Equal(payloadBytes, actual);
        }
        finally
        {
            ByteBuf received = receivedHolder.Get();
            if (received != null)
            {
                received.Release();
            }

            if (hammer != null)
            {
                hammer.Interrupt();
            }

            if (clientStream != null)
            {
                await clientStream.CloseAsync();
            }

            if (clientChannel != null)
            {
                await clientChannel.CloseAsync();
            }

            if (serverChannel != null)
            {
                await serverChannel.CloseAsync();
            }

            if (serverGroup != null)
            {
                _ = serverGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            }

            if (clientGroup != null)
            {
                _ = clientGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            }
        }
    }

    // CLR: CountDownLatch.countDown() does nothing when the count is already zero, where CountdownEvent.Signal() throws
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.countdownevent.signal?view=net-10.0); a Signal()
    // that loses the race to zero against another thread is retried and then stops, as Http2TestUtil's FrameCountDown
    // counts down.
    private static void CountDown(CountdownEvent latch)
    {
        while (latch.CurrentCount > 0)
        {
            try
            {
                latch.Signal();
                return;
            }
            catch (InvalidOperationException)
            {
                // CLR: the count reached zero on another thread.
            }
        }
    }

    // CLR: Class<? extends T> is the Type of the messages to accept (SimpleChannelInboundHandler(Type)).
    private sealed class Http2FrameIgnore<T> : SimpleChannelInboundHandler<T> where T : IHttp2Frame
    {
        internal Http2FrameIgnore(Type inboundMessageType) : base(inboundMessageType)
        {
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, T msg)
        {
        }
    }
}
