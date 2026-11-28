/*
* Copyright 2014 The Netty Project
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
using System.Net.Security;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Logging;
using Netty.NET.Handler.Ssl;
using Netty.NET.PkiTesting;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

// CLR: the TLS engine is SslStream. @ParameterizedTest with @MethodSource("data") is [Theory] with
// [MemberData(nameof(Data))] and @Timeout(30000 ms) its Timeout, as in the handler ssl tests; the JUnit
// display-name pattern has no counterpart, xUnit names a case by its arguments. Each case runs through the
// testsuite harness (both allocators x the NIO socket combination). SslProvider.OPENSSL is library-not-ported and
// SslContext.newHandler(alloc, Executor) is not ported (see data() and newSslHandler), so data() holds the JDK
// pair without the delegate rows. CountDownLatch is a TaskCompletionSource and AtomicReference the Common type.
#pragma warning disable xUnit1069 // The xUnit Timeout bounds the Runner body, whose latch wait has no timeout in the source.
#pragma warning disable xUnit1051 // The source's bind/connect/write/close calls and latch wait do not observe the test cancellation.
public class SocketSslGreetingTest : AbstractSocketTest
{

    // CLR: the source's static logger hides the inherited instance field logger of AbstractComboTestsuiteTest, which
    // C# states with the new modifier.
    private new static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(SocketSslGreetingTest));

    private static readonly LogLevel LOG_LEVEL = LogLevel.TRACE;
    private static readonly FileInfo CERT_FILE;
    private static readonly FileInfo KEY_FILE;

    // CLR: an exception thrown by a static constructor fails every use of the class as TypeInitializationException, the
    // counterpart of ExceptionInInitializerError, so the try/catch that wraps the certificate generation is not repeated
    // (as BrotliDecoderTest notes).
    static SocketSslGreetingTest()
    {
        X509Bundle cert = new CertificateBuilder()
                .Subject("cn=localhost")
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();
        CERT_FILE = cert.ToTempCertChainPem();
        KEY_FILE = cert.ToTempPrivateKeyPem();
    }

    public static IEnumerable<object[]> Data()
    {
        List<SslContext> serverContexts = new List<SslContext>();
        serverContexts.Add(SslContextBuilder.ForServer(CERT_FILE, KEY_FILE).SslProvider(SslProvider.JDK).Build());

        List<SslContext> clientContexts = new List<SslContext>();
        clientContexts.Add(SslContextBuilder.ForClient().SslProvider(SslProvider.JDK)
                .EndpointIdentificationAlgorithm(null).TrustManager(CERT_FILE).Build());

        // CLR: SslProvider.OPENSSL is library-not-ported (netty-tcnative; owner decision 2026-11-24,
        // docs/handler-ssl-design.md) and OpenSsl.isAvailable() has no counterpart, so the OPENSSL server and client
        // contexts that the source adds when OpenSSL is available, and the warning it logs otherwise, are not ported;
        // the JDK pair remains, as in ParameterizedSslHandlerTest and CloseNotifyTest.

        // CLR: the source adds each pair twice, with delegate = true and false. delegate = true makes
        // newSslHandler call sslCtx.newHandler(allocator, executor), which passes the executor for
        // SSLEngine.getDelegatedTask(); SslStream has no delegated tasks, so those overloads are not ported (class
        // comment of src/Netty.NET.Handler/Ssl/SslContext.cs) and only the delegate = false row remains, without
        // the parameter, as CipherSuiteCanaryTest drops its delegate rows.
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

    // CLR: the executor parameter and its newHandler(allocator, executor) branch are not ported (see Data()).
    private static SslHandler NewSslHandler(SslContext sslCtx, IByteBufAllocator allocator)
    {
        return sslCtx.NewHandler(allocator);
    }

    // Test for https://github.com/netty/netty/pull/2437
    [Theory(Timeout = 30000)]
    [MemberData(nameof(Data))]
    public async Task TestSslGreeting(SslContext serverCtx, SslContext clientCtx)
    {
        await Run(TestContext.Current, (serverBootstrap, bootstrap) => TestSslGreeting0(sb, cb, serverCtx, clientCtx));
    }

    // CLR: xUnit1024 forbids overloading the TestSslGreeting test name. Executors.newCachedThreadPool() for
    // delegate = true, and its shutdown and assertTrue(awaitTermination(5 s)) in the finally block, belong to the rows
    // that are not generated (see Data()), so the body has no executor and no try/finally.
    private async Task TestSslGreeting0(ServerBootstrap sb, Bootstrap cb, SslContext serverCtx,
                                        SslContext clientCtx)
    {
        ServerHandler sh = new ServerHandler();
        ClientHandler ch = new ClientHandler();

        // CLR: the anonymous ChannelInitializer bodies are ActionChannelInitializer delegates, as in
        // SocketEchoSourceTest.
        sb.ChildHandler(new ActionChannelInitializer<IChannel>(sch =>
        {
            IChannelPipeline p = sch.Pipeline;
            p.AddLast(NewSslHandler(serverCtx, sch.Alloc()));
            p.AddLast(new LoggingHandler(LOG_LEVEL));
            p.AddLast(sh);
        }));

        cb.Handler(new ActionChannelInitializer<IChannel>(sch =>
        {
            IChannelPipeline p = sch.Pipeline;
            p.AddLast(NewSslHandler(clientCtx, sch.Alloc()));
            p.AddLast(new LoggingHandler(LOG_LEVEL));
            p.AddLast(ch);
        }));

        IChannel sc = await sb.BindAsync();
        IChannel cc = await cb.ConnectAsync(sc.LocalAddress);

        await ch.latch.Task;

        await sh.channel.CloseAsync();
        await cc.CloseAsync();
        await sc.CloseAsync();

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

    private class ClientHandler : SimpleChannelInboundHandler<ByteBuf>
    {

        internal readonly AtomicReference<Exception> exception = new AtomicReference<Exception>();
        // CLR: CountDownLatch(1) is a TaskCompletionSource: countDown() is TrySetResult(), which returns false instead
        // of failing once the Task is complete, and await() awaits its Task (as SocketBufReleaseSourceTest).
        internal readonly TaskCompletionSource latch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf buf)
        {
            Assert.Equal((byte) 'a', buf.ReadByte());
            Assert.False(buf.IsReadable());
            latch.TrySetResult();
            _ = ctx.CloseAsync();
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

    private class ServerHandler : SimpleChannelInboundHandler<string>
    {
        internal volatile IChannel channel;
        internal readonly AtomicReference<Exception> exception = new AtomicReference<Exception>();

        protected override void ChannelRead0(IChannelHandlerContext ctx, string msg)
        {
            // discard
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            channel = ctx.Channel;
            _ = channel.WriteAndFlushAsync(ctx.Alloc().Buffer().WriteByte('a'));
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

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is SslHandshakeCompletionEvent)
            {
                SslHandshakeCompletionEvent @event = (SslHandshakeCompletionEvent) evt;
                if (@event.IsSuccess())
                {
                    // CLR: the source reads the server's SSLSession through ctx.pipeline().get(SslHandler.class)
                    // .engine().getSession() and asserts that the client is unauthenticated: getPeerCertificates() and
                    // getPeerPrincipal() throw SSLPeerUnverifiedException, and getPeerCertificateChain() throws it or,
                    // from Java 15, UnsupportedOperationException. On SslStream the peer's certificate is
                    // RemoteCertificate, "null if no certificate was supplied"
                    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.remotecertificate?view=net-10.0),
                    // which carries all three checks. SslHandler.engine() is the internal Engine() of Netty.NET.Handler,
                    // which opens its internals to this test assembly as to Netty.NET.Handler.Tests. The comments of the
                    // source's catch clauses:
                    // expected
                    // Starting from Java15 this method throws UnsupportedOperationException as it was
                    // deprecated before and getPeerCertificates() should be used
                    SslStream session = ctx.Pipeline.Get<SslHandler>().Engine();
                    Assert.Null(session.RemoteCertificate);
                }
            }

            ctx.FireUserEventTriggered(evt);
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
