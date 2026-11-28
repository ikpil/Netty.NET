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
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Stream;
using Netty.NET.PkiTesting;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using static Netty.NET.Transport.Tests.TestsuitePermutation;

namespace Netty.NET.Transport.Tests;

// CLR: the TLS engine is SslStream. @ParameterizedTest with @MethodSource("data") is [Theory] with
// [MemberData(nameof(Data))] and @Timeout(30000 ms) its Timeout, as in the handler ssl tests; the JUnit
// display-name pattern has no counterpart, xUnit names a case by its arguments. Each case runs through the
// testsuite harness (both allocators x the NIO socket combination). Two dimensions of data() shrink, each
// explained where data() builds its rows: the OPENSSL contexts (library-not-ported) and the delegated-task
// executor rows (SslHandler(SSLEngine, Executor) is not ported); the CLIENT_INITIATED and SERVER_INITIATED rows
// are generated and skipped with the reason (see TestSslEcho). SslHandler.engine() is the internal Engine() of
// Netty.NET.Handler, which opens its internals to this assembly as to Netty.NET.Handler.Tests; logStats logs its
// negotiated cipher suite (see CipherSuite and the renegotiation checks).
#pragma warning disable xUnit1069 // The xUnit Timeout bounds the Runner body, whose polling loops and future waits have no timeout in the source.
#pragma warning disable xUnit1051 // The source's bind/connect/write/close calls and polling sleeps do not observe the test cancellation.
public class SocketSslEchoTest : AbstractSocketTest
{

    // CLR: the source's static logger hides the inherited instance field logger of AbstractComboTestsuiteTest, which
    // C# states with the new modifier.
    private new static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(SocketSslEchoTest));

    private const int FIRST_MESSAGE_SIZE = 16384;
    private static readonly Random random = new Random();
    private static readonly FileInfo CERT_FILE;
    private static readonly FileInfo KEY_FILE;
    internal static readonly byte[] data = new byte[1048576];

    // CLR: an exception thrown by a static constructor fails every use of the class as TypeInitializationException, the
    // counterpart of ExceptionInInitializerError, so the try/catch that wraps the certificate generation is not repeated
    // (as BrotliDecoderTest notes).
    static SocketSslEchoTest()
    {
        // CLR: PlatformDependent.splittableRandomNextBytes(new SplittableRandom(random.nextLong()), data) is a Random
        // seeded from the shared random filling the array, as in SocketEchoSourceTest
        // (https://learn.microsoft.com/en-us/dotnet/api/system.random.nextbytes).
        new Random(random.Next()).NextBytes(data);

        X509Bundle cert = new CertificateBuilder()
                .Rsa2048()
                .Subject("cn=localhost")
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();
        CERT_FILE = cert.ToTempCertChainPem();
        KEY_FILE = cert.ToTempPrivateKeyPem();
    }

    // CLR: public instead of protected: the nested types are parameter types of the public theory TestSslEcho, and C#
    // rejects a parameter type less accessible than its method (CS0051).
    public enum RenegotiationType
    {
        NONE, // no renegotiation
        CLIENT_INITIATED, // renegotiation from client
        SERVER_INITIATED, // renegotiation from server
    }

    public class Renegotiation
    {
        internal static readonly Renegotiation NONE = new Renegotiation(RenegotiationType.NONE, null);

        internal readonly RenegotiationType type;
        internal readonly string cipherSuite;

        internal Renegotiation(RenegotiationType type, string cipherSuite)
        {
            this.type = type;
            this.cipherSuite = cipherSuite;
        }

        public override string ToString()
        {
            if (type == RenegotiationType.NONE)
            {
                return "NONE";
            }

            return type + "(" + cipherSuite + ')';
        }
    }

    public static IEnumerable<object[]> Data()
    {
        List<SslContext> serverContexts = new List<SslContext>();
        serverContexts.Add(SslContextBuilder.ForServer(CERT_FILE, KEY_FILE)
                                            .SslProvider(SslProvider.JDK)
                                            // As we test renegotiation we should use a protocol that support it.
                                            .Protocols("TLSv1.2")
                                            .Build());

        List<SslContext> clientContexts = new List<SslContext>();
        clientContexts.Add(SslContextBuilder.ForClient()
                                            .SslProvider(SslProvider.JDK)
                                            .TrustManager(CERT_FILE)
                                            // As we test renegotiation we should use a protocol that support it.
                                            .Protocols("TLSv1.2")
                                            .EndpointIdentificationAlgorithm(null)
                                            .Build());

        // CLR: SslProvider.OPENSSL is library-not-ported (netty-tcnative; owner decision 2026-11-24,
        // docs/handler-ssl-design.md) and OpenSsl.isAvailable() has no counterpart, so the OPENSSL server and client
        // contexts that the source adds when OpenSSL is available, and the warning it logs otherwise, are not ported;
        // the JDK pair remains, as in ParameterizedSslHandlerTest and CloseNotifyTest.

        List<object[]> @params = new List<object[]>();

        foreach (SslContext sc in serverContexts)
        {
            foreach (SslContext cc in clientContexts)
            {
                foreach (RenegotiationType rt in Enum.GetValues<RenegotiationType>())
                {
                    // CLR: the source skips the renegotiation rows of a pair with an OpenSslContext here
                    // (rt != NONE && (sc instanceof OpenSslContext || cc instanceof OpenSslContext)), with the comment:
                    // TODO: OpenSslEngine does not support renegotiation yet.
                    // No OpenSslContext exists in this port, so every row is generated; the renegotiation rows that
                    // SslStream cannot run are skipped by TestSslEcho with the reason.

                    Renegotiation r;

                    switch (rt)
                    {
                        case RenegotiationType.NONE:
                            r = Renegotiation.NONE;
                            break;
                        case RenegotiationType.SERVER_INITIATED:
                            r = new Renegotiation(rt, sc.CipherSuites()[sc.CipherSuites().Count - 1]);
                            break;
                        case RenegotiationType.CLIENT_INITIATED:
                            r = new Renegotiation(rt, cc.CipherSuites()[cc.CipherSuites().Count - 1]);
                            break;
                        default:
                            // CLR: java.lang.Error is UnreachableException, as SslContext maps it.
                            throw new UnreachableException("Unexpected renegotiation type: " + rt);
                    }

                    for (int i = 0; i < 32; i++)
                    {
                        // CLR: bits 16 and 8 are serverUsesDelegatedTaskExecutor and clientUsesDelegatedTaskExecutor,
                        // which make the source build new SslHandler(ctx.newEngine(alloc), delegatedTaskExecutor).
                        // SslStream has no delegated tasks, so SslContext.newEngine and the Executor constructors of
                        // SslHandler are not ported (class comments of src/Netty.NET.Handler/Ssl/SslContext.cs and
                        // SslHandler.cs); the rows that set either bit are not generated and the theory has no such
                        // parameters, as CipherSuiteCanaryTest drops its delegate rows (docs/handler-porting.md).
                        if ((i & 16) != 0 || (i & 8) != 0)
                        {
                            continue;
                        }

                        @params.Add(new object[] {
                                sc, cc, r,
                                (i & 4) != 0, (i & 2) != 0, (i & 1) != 0 });
                    }
                }
            }
        }

        return @params;
    }

    private readonly AtomicReference<Exception> clientException = new AtomicReference<Exception>();
    private readonly AtomicReference<Exception> serverException = new AtomicReference<Exception>();
    private readonly AtomicInteger clientSendCounter = new AtomicInteger();
    private readonly AtomicInteger clientRecvCounter = new AtomicInteger();
    private readonly AtomicInteger serverRecvCounter = new AtomicInteger();

    private readonly AtomicInteger clientNegoCounter = new AtomicInteger();
    private readonly AtomicInteger serverNegoCounter = new AtomicInteger();

    private volatile IChannel clientChannel;
    private volatile IChannel serverChannel;

    private volatile SslHandler clientSslHandler;
    private volatile SslHandler serverSslHandler;

    private readonly EchoClientHandler clientHandler;

    private readonly EchoServerHandler serverHandler;

    private SslContext serverCtx;
    private SslContext clientCtx;
    private Renegotiation renegotiation;
    private bool autoRead;
    private bool useChunkedWriteHandler;
    private bool useCompositeByteBuf;

    // CLR: the field initializers of clientHandler and serverHandler create inner-class handlers bound to this
    // instance; a C# field initializer cannot reference the instance ("A field initializer cannot reference the
    // non-static field, method, or property", https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0236), and a C#
    // nested class reaches its containing instance only when it is passed in ("To access the containing type, pass it
    // as an argument to the constructor of the nested type",
    // https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/nested-types), so the
    // constructor creates them with this.
    public SocketSslEchoTest()
    {
        clientHandler = new EchoClientHandler(this, clientRecvCounter, clientNegoCounter, clientException);
        serverHandler = new EchoServerHandler(this, serverRecvCounter, serverNegoCounter, serverException);
    }

    // CLR: the source's @AfterAll compressHeapDumps() calls TestUtils.compressHeapDumps(), which xz-compresses the
    // heap dumps that TestUtils.dump(...) writes through the JVM-only HotSpotDiagnosticMXBean; this port's TestUtils
    // does not port them (see TestUtils.cs and SocketGatheringWriteSourceTest), so no class fixture replaces it.

    [Theory(Timeout = 30000)]
    [MemberData(nameof(Data))]
    public async Task TestSslEcho(
            SslContext serverCtx, SslContext clientCtx, Renegotiation renegotiation,
            bool autoRead, bool useChunkedWriteHandler, bool useCompositeByteBuf)
    {
        this.serverCtx = serverCtx;
        this.clientCtx = clientCtx;
        this.renegotiation = renegotiation;
        this.autoRead = autoRead;
        this.useChunkedWriteHandler = useChunkedWriteHandler;
        this.useCompositeByteBuf = useCompositeByteBuf;
        // CLR: SslHandler renegotiates on SslStream, which renegotiates only as a server, through
        // SslStream.NegotiateClientCertificateAsync ("Negotiates the client certificate on the authenticated
        // connection", https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.negotiateclientcertificateasync?view=net-10.0).
        // A client renegotiate() fails the new handshake with NotSupportedException and closes the channel, and a server
        // renegotiation fails with AuthenticationException ("Received data during renegotiation.") when application
        // data arrives before the client answered on TLS 1.2 (docs/handler-porting.md, "Measurements and platform
        // limits"): SslStream's ReceiveHandshakeFrameAsync throws for a TlsContentType.AppData record "if (_isRenego &&
        // SslProtocol != SslProtocols.Tls13)"
        // (https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Net.Security/src/System/Net/Security/SslStream.IO.cs).
        // This test renegotiates while the client keeps sending: measured on Windows (2026-11-24), all 8
        // SERVER_INITIATED rows failed that way after about 520 KiB, with the client about 450 KiB ahead. Both
        // renegotiation types are therefore skipped like the source's OpenSslEngine rows, which it does not generate.
        Assert.SkipWhen(renegotiation.type == RenegotiationType.CLIENT_INITIATED,
                "SslStream renegotiates only as a server; a client SslHandler.Renegotiate() fails with NotSupportedException");
        Assert.SkipWhen(renegotiation.type == RenegotiationType.SERVER_INITIATED,
                "SslStream rejects application data received during a server renegotiation on TLS 1.2 " +
                "(\"Received data during renegotiation.\"), and the client keeps sending while the server renegotiates");
        await Run(TestContext.Current, TestSslEcho0);
    }

    // CLR: xUnit1024 forbids overloading the TestSslEcho test name.
    private async Task TestSslEcho0(ServerBootstrap sb, Bootstrap cb)
    {
        // CLR: Executors.newCachedThreadPool() only serves the delegated-task SslHandler of the rows that are not
        // generated (see Data()), so no executor is created, shut down or awaited.
        Reset();

        sb.ChildOption(ChannelOption.AUTO_READ, autoRead);
        cb.Option(ChannelOption.AUTO_READ, autoRead);

        // CLR: the anonymous ChannelInitializer bodies are ActionChannelInitializer delegates, as in
        // SocketEchoSourceTest; without delegated-task executors the handlers are always ctx.newHandler(sch.alloc()).
        sb.ChildHandler(new ActionChannelInitializer<IChannel>(sch =>
        {
            serverChannel = sch;

            serverSslHandler = serverCtx.NewHandler(sch.Alloc());
            serverSslHandler.SetHandshakeTimeoutMillis(0);

            sch.Pipeline.AddLast("ssl", serverSslHandler);

            if (useChunkedWriteHandler)
            {
                sch.Pipeline.AddLast(new ChunkedWriteHandler());
            }

            sch.Pipeline.AddLast("serverHandler", serverHandler);
        }));

        // CLR: CountDownLatch(1) is a TaskCompletionSource: countDown() is TrySetResult(), which returns false instead
        // of failing once the Task is complete, and await() awaits its Task (as SocketBufReleaseSourceTest).
        TaskCompletionSource clientHandshakeEventLatch =
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cb.Handler(new ActionChannelInitializer<IChannel>(sch =>
        {
            clientChannel = sch;

            clientSslHandler = clientCtx.NewHandler(sch.Alloc());
            clientSslHandler.SetHandshakeTimeoutMillis(0);

            sch.Pipeline.AddLast("ssl", clientSslHandler);

            if (useChunkedWriteHandler)
            {
                sch.Pipeline.AddLast(new ChunkedWriteHandler());
            }

            sch.Pipeline.AddLast("clientHandler", clientHandler);
            sch.Pipeline.AddLast(new HandshakeEventLatchHandler(clientHandshakeEventLatch));
        }));

        IChannel sc = await sb.BindAsync();
        await cb.ConnectAsync(sc.LocalAddress);

        Task<IChannel> clientHandshakeFuture = clientSslHandler.HandshakeFuture();

        // Wait for the handshake to complete before we flush anything. SslHandler should flush non-application data.
        await clientHandshakeFuture;
        await clientHandshakeEventLatch.Task;

        _ = clientChannel.WriteAndFlushAsync(RandomBufferType(clientChannel.Alloc(), data, 0, FIRST_MESSAGE_SIZE));
        clientSendCounter.Set(FIRST_MESSAGE_SIZE);

        bool needsRenegotiation = renegotiation.type == RenegotiationType.CLIENT_INITIATED;
        Task<IChannel> renegoFuture = null;
        // CLR: new SplittableRandom(random.nextLong()) is a Random seeded from the shared random; Next(maxValue) keeps
        // nextInt(bound)'s exclusive upper bound (as SocketFixedLengthEchoTest).
        Random rng = new Random(random.Next());

        while (clientSendCounter.Get() < data.Length)
        {
            int clientSendCounterVal = clientSendCounter.Get();
            int length = Math.Min(rng.Next(1024 * 64), data.Length - clientSendCounterVal);
            ByteBuf buf = RandomBufferType(clientChannel.Alloc(), data, clientSendCounterVal, length);

            if (useCompositeByteBuf)
            {
                // CLR: the Buffer port's addComponent(true, buf) is AddComponent(buf, true).
                buf = Unpooled.CompositeBuffer().AddComponent(buf, true);
            }

            Task future = clientChannel.WriteAndFlushAsync(buf);
            clientSendCounter.Set(clientSendCounterVal += length);
            await future;

            if (needsRenegotiation && clientSendCounterVal >= data.Length / 2)
            {
                needsRenegotiation = false;
                // CLR: clientSslHandler.engine().setEnabledCipherSuites(new String[] { renegotiation.cipherSuite
                // }) has no counterpart: SslStream takes its cipher suites only as the CipherSuitesPolicy of the
                // options handed to AuthenticateAsClientAsync, which Windows does not support. This branch is not
                // reached: TestSslEcho skips the CLIENT_INITIATED rows.
                renegoFuture = clientSslHandler.Renegotiate();
                LogStats("CLIENT RENEGOTIATES");
                Assert.NotSame(renegoFuture, clientHandshakeFuture);
            }
        }

        // Ensure all data has been exchanged.
        // CLR: Thread.sleep(50) is an awaited Task.Delay(50), as in SocketEchoSourceTest.
        while (clientRecvCounter.Get() < data.Length)
        {
            if (serverException.Get() != null)
            {
                break;
            }
            if (clientException.Get() != null)
            {
                break;
            }

            await Task.Delay(50);
        }

        while (serverRecvCounter.Get() < data.Length)
        {
            if (serverException.Get() != null)
            {
                break;
            }
            if (clientException.Get() != null)
            {
                break;
            }

            await Task.Delay(50);
        }

        // Wait until renegotiation is done.
        if (renegoFuture != null)
        {
            await renegoFuture;
        }

        if (serverHandler.renegoFuture != null)
        {
            await serverHandler.renegoFuture;
        }

        await serverChannel.CloseAsync();
        await clientChannel.CloseAsync();
        await sc.CloseAsync();

        if (serverException.Get() != null && !(serverException.Get() is IOException))
        {
            throw serverException.Get();
        }
        if (clientException.Get() != null && !(clientException.Get() is IOException))
        {
            throw clientException.Get();
        }
        if (serverException.Get() != null)
        {
            throw serverException.Get();
        }
        if (clientException.Get() != null)
        {
            throw clientException.Get();
        }

        // When renegotiation is done, at least the initiating side should be notified.
        // CLR: assertThat(x).isIn(1, 2) is Assert.Contains(x, new[] { 1, 2 }). The source also asserts that the
        // initiating side's session uses renegotiation.cipherSuite, the cipher suite it enabled before renegotiating
        // (engine().getSession().getCipherSuite()). SslStream cannot change its cipher suites on an authenticated
        // stream (they are the CipherSuitesPolicy of the authentication options, unsupported on Windows), so that
        // assertion is not ported (the renegotiation rows are skipped, see TestSslEcho).
        try
        {
            switch (renegotiation.type)
            {
            case RenegotiationType.SERVER_INITIATED:
                Assert.Equal(2, serverNegoCounter.Get());
                Assert.Contains(clientNegoCounter.Get(), new[] { 1, 2 });
                break;
            case RenegotiationType.CLIENT_INITIATED:
                Assert.Contains(serverNegoCounter.Get(), new[] { 1, 2 });
                Assert.Equal(2, clientNegoCounter.Get());
                break;
            case RenegotiationType.NONE:
                Assert.Equal(1, serverNegoCounter.Get());
                Assert.Equal(1, clientNegoCounter.Get());
                break;
            }
        }
        finally
        {
            LogStats("STATS");
        }
    }

    private void Reset()
    {
        clientException.Set(null);
        serverException.Set(null);

        clientSendCounter.Set(0);
        clientRecvCounter.Set(0);
        serverRecvCounter.Set(0);

        clientNegoCounter.Set(0);
        serverNegoCounter.Set(0);

        clientChannel = null;
        serverChannel = null;

        clientSslHandler = null;
        serverSslHandler = null;
    }

    // CLR: AtomicInteger has no ToString override, so the counters are logged through Get() (the source's toString()).
    // engine().getSession().getCipherSuite() is CipherSuite(handler) below.
    internal void LogStats(string message)
    {
        logger.Debug(
                "{}:\n" +
                "\tclient { sent: {}, rcvd: {}, nego: {}, cipher: {} },\n" +
                "\tserver { rcvd: {}, nego: {}, cipher: {} }",
                message,
                clientSendCounter.Get(), clientRecvCounter.Get(), clientNegoCounter.Get(),
                CipherSuite(clientSslHandler),
                serverRecvCounter.Get(), serverNegoCounter.Get(),
                CipherSuite(serverSslHandler));
    }

    // CLR-only helper: the session cipher suite is NegotiatedCipherSuite ("Gets the cipher suite that was negotiated for
    // this connection",
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.negotiatedciphersuite?view=net-10.0)
    // of the SslStream that the internal SslHandler.Engine() returns (Netty.NET.Handler opens its internals to this
    // assembly). It throws InvalidOperationException before the stream is authenticated and ObjectDisposedException
    // after the handler disposed it (measured), where an SSLEngine's session "reports an invalid cipher suite of
    // "SSL_NULL_WITH_NULL_NULL"" until the initial handshake has completed
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/javax/net/ssl/SSLEngine.html#getSession()); the
    // log shows that name in both cases. TlsCipherSuite names are the IANA names that JSSE reports.
    private static string CipherSuite(SslHandler handler)
    {
        try
        {
            return handler.Engine().NegotiatedCipherSuite.ToString();
        }
        catch (Exception e) when (e is InvalidOperationException || e is ObjectDisposedException)
        {
            return "SSL_NULL_WITH_NULL_NULL";
        }
    }

    private sealed class HandshakeEventLatchHandler : ChannelInboundHandlerAdapter
    {
        private readonly TaskCompletionSource clientHandshakeEventLatch;

        internal HandshakeEventLatchHandler(TaskCompletionSource clientHandshakeEventLatch)
        {
            this.clientHandshakeEventLatch = clientHandshakeEventLatch;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is SslHandshakeCompletionEvent)
            {
                clientHandshakeEventLatch.TrySetResult();
            }

            ctx.FireUserEventTriggered(evt);
        }
    }

    // CLR: the inner (non-static) handler classes read the enclosing test's fields; C# nested classes take the
    // enclosing instance as the outer constructor parameter (see the constructor above).
    [Sharable]
    private abstract class EchoHandler : SimpleChannelInboundHandler<ByteBuf>
    {

        protected readonly SocketSslEchoTest outer;
        protected readonly AtomicInteger recvCounter;
        protected readonly AtomicInteger negoCounter;
        protected readonly AtomicReference<Exception> exception;

        internal EchoHandler(
                SocketSslEchoTest outer,
                AtomicInteger recvCounter, AtomicInteger negoCounter,
                AtomicReference<Exception> exception)
        {

            this.outer = outer;
            this.recvCounter = recvCounter;
            this.negoCounter = negoCounter;
            this.exception = exception;
        }

        public sealed override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            // We intentionally do not ctx.flush() here because we want to verify the SslHandler correctly flushing
            // non-application and previously flushed writes internally.
            if (!outer.autoRead)
            {
                ctx.Read();
            }

            ctx.FireChannelReadComplete();
        }

        public sealed override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is SslHandshakeCompletionEvent)
            {
                SslHandshakeCompletionEvent handshakeEvt = (SslHandshakeCompletionEvent) evt;
                if (handshakeEvt.Cause() != null)
                {
                    logger.Warn("Handshake failed:", handshakeEvt.Cause());
                }

                Assert.Same(SslHandshakeCompletionEvent.SUCCESS, evt);
                negoCounter.IncrementAndGet();
                outer.LogStats("HANDSHAKEN");
            }

            ctx.FireUserEventTriggered(evt);
        }

        public sealed override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (logger.IsWarnEnabled())
            {
                logger.Warn("Unexpected exception from the client side:", cause);
            }

            exception.CompareAndSet(null, cause);
            _ = ctx.CloseAsync();
        }
    }

    private class EchoClientHandler : EchoHandler
    {

        internal EchoClientHandler(
                SocketSslEchoTest outer,
                AtomicInteger recvCounter, AtomicInteger negoCounter,
                AtomicReference<Exception> exception) : base(outer, recvCounter, negoCounter, exception)
        {
        }

        public override void HandlerAdded(IChannelHandlerContext ctx)
        {
            if (!outer.autoRead)
            {
                // CLR: handshakeFuture().addListener(future -> ctx.read()) is a ContinueWith continuation, which runs
                // whatever the outcome (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.continuewith);
                // ctx.Read() hands the read to the event loop when called from another thread, as in Java.
                _ = ctx.Pipeline.Get<SslHandler>().HandshakeFuture().ContinueWith(future => ctx.Read(),
                        TaskScheduler.Default);
            }
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf @in)
        {
            byte[] actual = new byte[@in.ReadableBytes];
            @in.ReadBytes(actual);

            int lastIdx = recvCounter.Get();

            for (int i = 0; i < actual.Length; i ++)
            {
                Assert.Equal(data[i + lastIdx], actual[i]);
            }

            recvCounter.AddAndGet(actual.Length);
        }
    }

    private class EchoServerHandler : EchoHandler
    {
        internal volatile Task<IChannel> renegoFuture;

        internal EchoServerHandler(
                SocketSslEchoTest outer,
                AtomicInteger recvCounter, AtomicInteger negoCounter,
                AtomicReference<Exception> exception) : base(outer, recvCounter, negoCounter, exception)
        {
        }

        public sealed override void ChannelRegistered(IChannelHandlerContext ctx)
        {
            renegoFuture = null;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            if (!outer.autoRead)
            {
                ctx.Read();
            }

            ctx.FireChannelActive();
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf @in)
        {
            byte[] actual = new byte[@in.ReadableBytes];
            @in.ReadBytes(actual);

            int lastIdx = recvCounter.Get();

            for (int i = 0; i < actual.Length; i ++)
            {
                Assert.Equal(data[i + lastIdx], actual[i]);
            }

            ByteBuf buf = RandomBufferType(ctx.Alloc(), actual, 0, actual.Length);

            if (outer.useCompositeByteBuf)
            {
                buf = Unpooled.CompositeBuffer().AddComponent(buf, true);
            }

            _ = ctx.WriteAndFlushAsync(buf);

            recvCounter.AddAndGet(actual.Length);

            // Perform server-initiated renegotiation if necessary.
            if (outer.renegotiation.type == RenegotiationType.SERVER_INITIATED &&
                recvCounter.Get() > data.Length / 2 && renegoFuture == null)
            {

                SslHandler sslHandler = ctx.Pipeline.Get<SslHandler>();

                Task<IChannel> hf = sslHandler.HandshakeFuture();
                Assert.True(hf.IsCompleted);

                // CLR: sslHandler.engine().setEnabledCipherSuites(new String[] { renegotiation.cipherSuite }) has no
                // counterpart: SslStream's cipher suites are
                // fixed by the authentication options (CipherSuitesPolicy, unsupported on Windows); the renegotiation
                // keeps the negotiated cipher suite.
                outer.LogStats("SERVER RENEGOTIATES");
                renegoFuture = sslHandler.Renegotiate();
                Assert.NotSame(renegoFuture, hf);
                Assert.Same(renegoFuture, sslHandler.HandshakeFuture());
                Assert.False(renegoFuture.IsCompleted);
            }
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
