/*
 * Copyright 2012 The Netty Project
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
using System.IO;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.String;
using Netty.NET.Handler.Logging;
using Netty.NET.Handler.Ssl;
using Netty.NET.PkiTesting;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

// CLR: the TLS engine is SslStream behind SslHandler (owner decision 2026-11-24, docs/handler-ssl-design.md), which has
// no SSLEngine facade, so SslContext.newEngine(alloc) is not ported; new SslHandler(engine[, startTls]) over an engine of
// a context is that context's NewHandler(alloc, startTls), whose source body is new SslHandler(newEngine(alloc),
// startTls) (handler/src/main/java/io/netty/handler/ssl/SslContext.java, newHandler(alloc, startTls); protected
// internal in Netty.NET.Handler, which opens its internals to this assembly as to Netty.NET.Handler.Tests). The
// engines' setUseClientMode(true/false) calls have no counterpart: JdkSslContext gives every engine its own mode
// (configureAndWrapEngine calls setUseClientMode(isClient())), which is the options type of the handler here.
// @ParameterizedTest with @MethodSource("data") is [Theory] with [MemberData(nameof(Data))], and the
// @Timeout(value, MILLISECONDS) the Timeout of the [Theory]; DisableDiscoveryEnumeration builds the contexts when the
// theory runs, as JUnit calls data() for each test (as AbstractEncoderTest of codec-compression). The static
// @BeforeAll/@AfterAll executor lifecycle is a class fixture, as in SocketEchoSourceTest. The certificate is the
// pkitesting bundle of the source; an exception of the static constructor fails every test as
// TypeInitializationException, the counterpart of ExceptionInInitializerError, so the try/catch is not repeated (as
// BrotliDecoderTest of codec-compression).
#pragma warning disable xUnit1069 // The xUnit Timeout bounds the Runner body, whose polling loops have no timeout in the source.
#pragma warning disable xUnit1051 // The source's bind/connect/write/close calls and polling sleeps do not observe the test cancellation.
public class SocketStartTlsTest : AbstractSocketTest, IClassFixture<SocketStartTlsTest.ExecutorFixture>
{
    // CLR: PARAMETERIZED_NAME is the JUnit display-name pattern of the @ParameterizedTest annotations; xUnit names theory
    // cases by their arguments, so the constant only documents the original names (as ParameterizedSslHandlerTest).
    private const string PARAMETERIZED_NAME = "{index}: serverEngine = {0}, clientEngine = {1}";

    // CLR: new hides the harness's instance logger, as the source's private static field hides the protected field of
    // AbstractComboTestsuiteTest.
    private static new readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(SocketStartTlsTest));

    private static readonly LogLevel LOG_LEVEL = LogLevel.TRACE;
    private static readonly FileInfo CERT_FILE;
    private static readonly FileInfo KEY_FILE;
    private static IEventExecutorGroup executor;

    static SocketStartTlsTest()
    {
        X509Bundle cert = new CertificateBuilder()
                .Subject("cn=localhost")
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();
        CERT_FILE = cert.ToTempCertChainPem();
        KEY_FILE = cert.ToTempPrivateKeyPem();
    }

    // CLR: SslProvider.OPENSSL is library-not-ported (netty-tcnative; owner decision 2026-11-24,
    // docs/handler-ssl-design.md) and OpenSsl.isAvailable() has no counterpart, so the OpenSSL server and client
    // contexts, and the warning that OpenSSL "is unavailable and thus will not be tested", are not ported: data() holds
    // the JDK pair only (as ParameterizedSslHandlerTest).
    public static IEnumerable<object[]> Data()
    {
        List<SslContext> serverContexts = new List<SslContext>();
        serverContexts.Add(SslContextBuilder.ForServer(CERT_FILE, KEY_FILE).SslProvider(SslProvider.JDK).Build());

        List<SslContext> clientContexts = new List<SslContext>();
        clientContexts.Add(SslContextBuilder.ForClient()
                .SslProvider(SslProvider.JDK)
                .TrustManager(CERT_FILE)
                .EndpointIdentificationAlgorithm(null)
                .Build());

        List<object[]> @params = new List<object[]>();

        foreach (SslContext sc in serverContexts)
        {
            foreach (SslContext cc in clientContexts)
            {
                @params.Add(new object[] { sc, cc });
            }
        }

        return @params;
    }

    // CLR: JUnit runs the static @BeforeAll/@AfterAll methods once around all tests of the class. xUnit v3 creates a
    // class fixture "Just before the first test" and disposes it "once all the tests have finished"
    // (https://xunit.net/docs/shared-context), so this fixture calls createExecutor() and shutdownExecutor() there, as
    // the GroupFixture of SocketEchoSourceTest.
    public sealed class ExecutorFixture : IDisposable
    {
        public ExecutorFixture()
        {
            CreateExecutor();
        }

        public void Dispose()
        {
            ShutdownExecutor();
        }
    }

    // CLR: internal instead of the source's public, because xUnit1013 reports public non-test methods on a test class.
    internal static void CreateExecutor()
    {
        executor = new DefaultEventExecutorGroup(2);
    }

    // CLR: shutdownGracefully().sync() blocks until the group terminated; xUnit disposes the fixture synchronously.
    internal static void ShutdownExecutor()
    {
        executor.ShutdownGracefullyAsync().GetAwaiter().GetResult();
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(Data), DisableDiscoveryEnumeration = true)]
    public async Task TestStartTls(SslContext serverCtx, SslContext clientCtx)
    {
        await Run(TestContext.Current, (serverBootstrap, bootstrap) => TestStartTls0(sb, cb, serverCtx, clientCtx));
    }

    // CLR: xUnit1024 forbids overloading the TestStartTls test name, hence the 0 suffix on the source's
    // testStartTls(sb, cb, serverCtx, clientCtx[, autoRead]) and testStartTlsNotAutoRead(sb, cb, serverCtx, clientCtx),
    // which are private here because xUnit1013 flags public non-test methods on a test class.
    private async Task TestStartTls0(ServerBootstrap sb, Bootstrap cb,
                                     SslContext serverCtx, SslContext clientCtx)
    {
        await TestStartTls0(sb, cb, serverCtx, clientCtx, true);
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(Data), DisableDiscoveryEnumeration = true)]
    public async Task TestStartTlsNotAutoRead(SslContext serverCtx, SslContext clientCtx)
    {
        await Run(TestContext.Current,
                (serverBootstrap, bootstrap) => TestStartTlsNotAutoRead0(sb, cb, serverCtx, clientCtx));
    }

    private async Task TestStartTlsNotAutoRead0(ServerBootstrap sb, Bootstrap cb,
                                                SslContext serverCtx, SslContext clientCtx)
    {
        await TestStartTls0(sb, cb, serverCtx, clientCtx, false);
    }

    private static async Task TestStartTls0(ServerBootstrap sb, Bootstrap cb,
                                            SslContext serverCtx, SslContext clientCtx, bool autoRead)
    {
        sb.ChildOption(ChannelOption.AUTO_READ, autoRead);
        cb.Option(ChannelOption.AUTO_READ, autoRead);

        IEventExecutorGroup executor = SocketStartTlsTest.executor;
        // CLR: serverCtx.newEngine(PooledByteBufAllocator.DEFAULT) and clientCtx.newEngine(...) are the handlers the
        // contexts build for these engines with the startTls flags of the source's new SslHandler(engine, true) (server)
        // and new SslHandler(engine) (client) (see the class comment); the allocator is not used by SslStream.
        SslHandler sse = serverCtx.NewHandler(PooledByteBufAllocator.Default, true);
        SslHandler cse = clientCtx.NewHandler(PooledByteBufAllocator.Default, false);

        StartTlsServerHandler sh = new StartTlsServerHandler(sse, autoRead);
        StartTlsClientHandler ch = new StartTlsClientHandler(cse, autoRead);

        // CLR: the anonymous ChannelInitializer bodies are ActionChannelInitializer delegates, as in SocketEchoSourceTest;
        // this port's pipeline has no addLast(EventExecutorGroup, ChannelHandler...) overload, and the source's overload
        // calls addLast(executor, null, h), which AddLast(executor, null, handler) does here (as SocketEchoSourceTest).
        sb.ChildHandler(new ActionChannelInitializer<IChannel>(sch =>
        {
            IChannelPipeline p = sch.Pipeline;
            p.AddLast("logger", new LoggingHandler(LOG_LEVEL));
            p.AddLast(new LineBasedFrameDecoder(64), new StringDecoder(), new StringEncoder());
            p.AddLast(executor, null, sh);
        }));

        cb.Handler(new ActionChannelInitializer<IChannel>(sch =>
        {
            IChannelPipeline p = sch.Pipeline;
            p.AddLast("logger", new LoggingHandler(LOG_LEVEL));
            p.AddLast(new LineBasedFrameDecoder(64), new StringDecoder(), new StringEncoder());
            p.AddLast(executor, null, ch);
        }));

        IChannel sc = await sb.BindAsync();
        IChannel cc = await cb.ConnectAsync(sc.LocalAddress);

        // CLR: Thread.sleep(50) is an awaited Task.Delay(50), as in SocketEchoSourceTest.
        while (cc.IsActive)
        {
            if (sh.exception.Get() != null)
            {
                break;
            }
            if (ch.exception.Get() != null)
            {
                break;
            }

            await Task.Delay(50);
        }

        while (sh.channel.IsActive)
        {
            if (sh.exception.Get() != null)
            {
                break;
            }
            if (ch.exception.Get() != null)
            {
                break;
            }

            await Task.Delay(50);
        }

        // CLR: awaitUninterruptibly() waits without rethrowing a failure, as ConfigureAwaitOptions.SuppressThrowing
        // (as DatagramMulticastSourceTest).
        await sh.channel.CloseAsync().ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await cc.CloseAsync().ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await sc.CloseAsync().ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (sh.exception.Get() != null && !(sh.exception.Get() is IOException))
        {
            throw sh.exception.Get();
        }
        if (ch.exception.Get() != null && !(ch.exception.Get() is IOException))
        {
            throw ch.exception.Get();
        }
        if (sh.exception.Get() != null)
        {
            throw sh.exception.Get();
        }
        if (ch.exception.Get() != null)
        {
            throw ch.exception.Get();
        }
    }

    private class StartTlsClientHandler : SimpleChannelInboundHandler<string>
    {
        private readonly SslHandler sslHandler;
        private readonly bool autoRead;
        // CLR: Future<Channel> is the Task<IChannel> of SslHandler.HandshakeFuture() (docs/handler-porting.md, SslHandler
        // on SslStream); isSuccess() is IsCompletedSuccessfully.
        private Task<IChannel> handshakeFuture;
        internal readonly AtomicReference<Exception> exception = new AtomicReference<Exception>();

        // CLR: the source's constructor takes the SSLEngine, calls engine.setUseClientMode(true) and wraps it in
        // new SslHandler(engine); the client context's handler for that engine is passed in (see the class comment).
        internal StartTlsClientHandler(SslHandler sslHandler, bool autoRead)
        {
            this.sslHandler = sslHandler;
            this.autoRead = autoRead;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            if (!autoRead)
            {
                ctx.Read();
            }

            _ = ctx.WriteAndFlushAsync("StartTlsRequest\n");
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, string msg)
        {
            if ("StartTlsResponse".Equals(msg))
            {
                ctx.Pipeline.AddAfter("logger", "ssl", sslHandler);
                handshakeFuture = sslHandler.HandshakeFuture();
                _ = ctx.WriteAndFlushAsync("EncryptedRequest\n");
                return;
            }

            Assert.Equal("EncryptedResponse", msg);
            Assert.NotNull(handshakeFuture);
            Assert.True(handshakeFuture.IsCompletedSuccessfully);
            _ = ctx.CloseAsync();
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            if (!autoRead)
            {
                ctx.Read();
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx,
                Exception cause)
        {
            if (logger.IsWarnEnabled())
            {
                logger.Warn("Unexpected exception from the client side", cause);
            }

            exception.CompareAndSet(null, cause);
            _ = ctx.CloseAsync();
        }
    }

    private class StartTlsServerHandler : SimpleChannelInboundHandler<string>
    {
        private readonly SslHandler sslHandler;
        private readonly bool autoRead;
        internal volatile IChannel channel;
        internal readonly AtomicReference<Exception> exception = new AtomicReference<Exception>();

        // CLR: the source's constructor takes the SSLEngine, calls engine.setUseClientMode(false) and wraps it in
        // new SslHandler(engine, true); the server context's NewHandler(alloc, true) for that engine is passed in (see
        // the class comment).
        internal StartTlsServerHandler(SslHandler sslHandler, bool autoRead)
        {
            this.sslHandler = sslHandler;
            this.autoRead = autoRead;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            channel = ctx.Channel;

            if (!autoRead)
            {
                ctx.Read();
            }
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, string msg)
        {
            if ("StartTlsRequest".Equals(msg))
            {
                ctx.Pipeline.AddAfter("logger", "ssl", sslHandler);
                _ = ctx.WriteAndFlushAsync("StartTlsResponse\n");
                return;
            }

            Assert.Equal("EncryptedRequest", msg);
            _ = ctx.WriteAndFlushAsync("EncryptedResponse\n");
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            if (!autoRead)
            {
                ctx.Read();
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx,
                                             Exception cause)
        {
            if (logger.IsWarnEnabled())
            {
                logger.Warn("Unexpected exception from the server side", cause);
            }

            exception.CompareAndSet(null, cause);
            _ = ctx.CloseAsync();
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
