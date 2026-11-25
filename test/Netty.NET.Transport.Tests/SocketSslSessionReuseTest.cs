/*
 * Copyright 2015 The Netty Project
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
using System.Net;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Ssl;
using Netty.NET.PkiTesting;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using static Netty.NET.Transport.Tests.TestsuitePermutation;

namespace Netty.NET.Transport.Tests;

// CLR: the TLS engine is SslStream behind SslHandler (owner decision 2026-11-24, docs/handler-ssl-design.md), which has
// no SSLEngine facade, so SslContext.newEngine(...) and sessionContext() are not ported; new SslHandler(engine) over an
// engine of a context is that context's NewHandler(alloc[, peerHost, peerPort]), whose source body is
// new SslHandler(newEngine(alloc[, peerHost, peerPort]), startTls) (handler/src/main/java/io/netty/handler/ssl/
// SslContext.java), and the engines' setUseClientMode(...) calls have no counterpart, because JdkSslContext gives every
// engine its own mode (configureAndWrapEngine calls setUseClientMode(isClient())). @ParameterizedTest with
// @MethodSource is [Theory] with [MemberData], and the @Timeout(value, MILLISECONDS) the Timeout of the [Theory];
// DisableDiscoveryEnumeration builds the builders when the theory runs, as JUnit calls the method source for each test
// (as AbstractEncoderTest of codec-compression). The certificate is the pkitesting bundle of the source; an exception of
// the static constructor fails every test as TypeInitializationException, the counterpart of
// ExceptionInInitializerError, so the try/catch is not repeated (as BrotliDecoderTest of codec-compression).
#pragma warning disable xUnit1069 // The xUnit Timeout bounds the Runner body, whose waits have no timeout in the source.
#pragma warning disable xUnit1051 // The source's bind/connect/write/close calls do not observe the test cancellation.
public class SocketSslSessionReuseTest : AbstractSocketTest
{

    // CLR: new hides the harness's instance logger, as the source's private static field hides the protected field of
    // AbstractComboTestsuiteTest.
    private static new readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(SocketSslSessionReuseTest));

    private static readonly FileInfo CERT_FILE;
    private static readonly FileInfo KEY_FILE;

    static SocketSslSessionReuseTest()
    {
        X509Bundle cert = new CertificateBuilder()
                .Subject("cn=localhost")
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();
        CERT_FILE = cert.ToTempCertChainPem();
        KEY_FILE = cert.ToTempPrivateKeyPem();
    }

    // CLR: Collections.singleton(row) is a one-row list.
    public static IEnumerable<object[]> JdkOnly()
    {
        return new List<object[]>
        {
            new object[]
            {
                SslContextBuilder.ForServer(CERT_FILE, KEY_FILE).SslProvider(SslProvider.JDK),
                SslContextBuilder.ForClient().TrustManager(CERT_FILE).SslProvider(SslProvider.JDK)
                        .EndpointIdentificationAlgorithm(null)
            }
        };
    }

    // CLR: jdkAndOpenSSL() only serves testSslSessionTrustManagerResumption, which is CLR-inapplicable (transport
    // porting manifest), and its second row is SslProvider.OPENSSL (netty-tcnative, library-not-ported), so it is not
    // ported.

    [Theory(Timeout = 30000)]
    [MemberData(nameof(JdkOnly), DisableDiscoveryEnumeration = true)]
    public async Task TestSslSessionReuse(SslContextBuilder serverCtx, SslContextBuilder clientCtx)
    {
        await Run(TestContext.Current, (serverBootstrap, bootstrap) => TestSslSessionReuse0(sb, cb, serverCtx, clientCtx));
    }

    // CLR: xUnit1024 forbids overloading the TestSslSessionReuse test name, hence the 0 suffix; private because xUnit1013
    // flags public non-test methods on a test class. The source's run() passes serverCtx.build() and clientCtx.build();
    // this body takes the builders and builds the contexts after setting the protocols (see below).
    private static async Task TestSslSessionReuse0(ServerBootstrap sb, Bootstrap cb,
                                                   SslContextBuilder serverCtxBldr, SslContextBuilder clientCtxBldr)
    {
        ReadAndDiscardHandler sh = new ReadAndDiscardHandler(true, true);
        ReadAndDiscardHandler ch = new ReadAndDiscardHandler(false, true);
        string[] protocols = { "TLSv1", "TLSv1.1", "TLSv1.2" };
        // CLR: engine.setEnabledProtocols(protocols) on every engine of the two contexts is the builders'
        // protocols(protocols), which JdkSslContext applies to each engine it creates (configureAndWrapEngine calls
        // engine.setEnabledProtocols(protocols)), as testSslSessionTrustManagerResumption of the source sets them; the
        // engine settings are internal to SslHandler here (SslHandler.ServerOptions()/ClientOptions()).
        SslContext serverCtx = serverCtxBldr.Protocols(protocols).Build();
        SslContext clientCtx = clientCtxBldr.Protocols(protocols).Build();
        // CLR: clientCtx.sessionContext() (SSLSessionContext) is not ported and SslStream reports no session id (see the
        // class comment). With the protocols above the session is a TLS 1.2 or older one, whose id the client learns from
        // the ServerHello: "If a match is found ... the server will respond with the same value as was supplied by the
        // client", "Otherwise, this field will contain a different value identifying the new session", and "The server may
        // return an empty session_id to indicate that the session will not be cached and therefore cannot be resumed"
        // (RFC 5246 7.4.1.3, https://www.rfc-editor.org/rfc/rfc5246.txt). So the client session context is
        // ClientSessionContext, which records the non-empty ServerHello session ids the client channels receive, read
        // before the client SslHandler; getIds() enumerates them, as the JSSE client cache enumerates the ids of its
        // sessions. Measured on Windows (SChannel): the second ServerHello repeats the 32-byte id of the first, and as
        // SChannel resumes sessions across the connections of one process (docs/handler-ssl-design.md, Known
        // differences), the first connection of the next allocator permutation already resumes it, where each JSSE
        // context has its own cache; the assertion compares the ids before and after the second connection either way.
        ClientSessionContext clientSessionContext = new ClientSessionContext();

        // CLR: the anonymous ChannelInitializer bodies are ActionChannelInitializer delegates, as in SocketEchoSourceTest.
        sb.ChildHandler(new ActionChannelInitializer<IChannel>(sch =>
        {
            SslHandler engine = serverCtx.NewHandler(sch.Alloc());

            sch.Pipeline.AddLast(engine);
            sch.Pipeline.AddLast(sh);
        }));
        IChannel sc = await sb.BindAsync();

        cb.Handler(new ActionChannelInitializer<IChannel>(sch =>
        {
            // CLR: InetSocketAddress is IPEndPoint; getHostString() of the bound address is the literal address text.
            IPEndPoint serverAddr = (IPEndPoint) sc.LocalAddress;
            SslHandler engine = clientCtx.NewHandler(sch.Alloc(), serverAddr.Address.ToString(), serverAddr.Port);

            sch.Pipeline.AddLast(clientSessionContext.NewServerHelloRecorder());
            sch.Pipeline.AddLast(engine);
            sch.Pipeline.AddLast(ch);
        }));

        try
        {
            // CLR: clientCtx.sessionContext() is the ClientSessionContext above.
            ClientSessionContext clientSessionCtx = clientSessionContext;
            IChannel cc = await cb.ConnectAsync(sc.LocalAddress);
            ByteBuf msg = RandomBufferType(cc.Alloc(), new byte[] { 0xa, 0xb, 0xc, 0xd }, 0, 4);

            await WriteAndFlushAndClose(cc, msg);
            await cc.CloseFuture;
            RethrowHandlerExceptions(sh, ch);
            ISet<string> sessions = SessionIdSet(clientSessionCtx.GetIds());

            cc = await cb.ConnectAsync(sc.LocalAddress);
            msg = RandomBufferType(cc.Alloc(), new byte[] { 0xa, 0xb, 0xc, 0xd }, 0, 4);
            await WriteAndFlushAndClose(cc, msg);
            await cc.CloseFuture;
            AssertEquals(sessions, SessionIdSet(clientSessionCtx.GetIds()), "Expected no new sessions");
            RethrowHandlerExceptions(sh, ch);
        }
        finally
        {
            // CLR: awaitUninterruptibly() waits without rethrowing a failure, as ConfigureAwaitOptions.SuppressThrowing
            // (as DatagramMulticastSourceTest).
            await sc.CloseAsync().ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    // CLR: cc.writeAndFlush(msg).addListener(ChannelFutureListener.CLOSE).sync(): the listener closes the channel when
    // the write completes, a continuation as in SocketHalfClosedSourceTest, and sync() awaits the write, rethrowing its
    // failure.
    private static async Task WriteAndFlushAndClose(IChannel cc, ByteBuf msg)
    {
        Task future = cc.WriteAndFlushAsync(msg);
        _ = future.ContinueWith(_ => cc.CloseAsync(), TaskScheduler.Default);
        await future;
    }

    // CLR: assertEquals(expected, actual, message) of two Sets compares them with Set.equals; xUnit's Assert.Equal has no
    // message argument, so the sets are compared with SetEquals and the message keeps the JUnit text.
    private static void AssertEquals(ISet<string> expected, ISet<string> actual, string message)
    {
        Assert.True(expected.SetEquals(actual), message + " ==> expected: <[" + string.Join(", ", expected) +
                "]> but was: <[" + string.Join(", ", actual) + "]>");
    }

    // CLR: testSslSessionTrustManagerResumption is CLR-inapplicable (transport porting manifest): its subject is the
    // ResumableX509ExtendedTrustManager callback resumeServerTrusted(chain, engine), which JSSE calls instead of
    // checkServerTrusted when a session is resumed, and the SSLSession value it stores (putValue("key", "value") on the
    // handshake session or the resumed session, read back with engine().getSession().getValue("key") on
    // SslHandshakeCompletionEvent.SUCCESS). ResumableX509ExtendedTrustManager is jvm-only-replaced
    // (docs/handler-porting-manifest.json: "SslStream exposes no session-resumption signal", the trust decision is the
    // RemoteCertificateValidationCallback), SslStream has no session object with values (docs/handler-porting.md,
    // "SSLSession/ExtendedSSLSession facts"), and SslHandler.engine() is internal. Its OPENSSL
    // row (jdkAndOpenSSL()) is library-not-ported. Its helpers (SessionSettingTrustManager, the anonymous
    // ReadAndDiscardHandler subclass with its sessionValue queue) are not ported, as in SSLEngineTest of the handler
    // tests (mustCallResumeTrustedOnSessionResumption).

    // CLR: ExecutionException wraps the failure of a task for Future.get(); its counterpart is AggregateException, which
    // "is used extensively in the Task Parallel Library (TPL)" to report a task's failure
    // (https://learn.microsoft.com/en-us/dotnet/api/system.aggregateexception?view=net-10.0).
    private static void RethrowHandlerExceptions(ReadAndDiscardHandler sh, ReadAndDiscardHandler ch)
    {
        if (sh.exception.Get() != null && !(sh.exception.Get() is IOException))
        {
            throw new AggregateException(sh.exception.Get());
        }
        if (ch.exception.Get() != null && !(ch.exception.Get() is IOException))
        {
            throw new AggregateException(ch.exception.Get());
        }
        if (sh.exception.Get() != null)
        {
            throw new AggregateException(sh.exception.Get());
        }
        if (ch.exception.Get() != null)
        {
            throw new AggregateException(ch.exception.Get());
        }
    }

    // CLR: Enumeration<byte[]> is IEnumerator<byte[]> (hasMoreElements()/nextElement() are MoveNext()/Current).
    private static ISet<string> SessionIdSet(IEnumerator<byte[]> sessionIds)
    {
        ISet<string> idSet = new HashSet<string>();
        byte[] id;
        while (sessionIds.MoveNext())
        {
            id = sessionIds.Current;
            idSet.Add(ByteBufUtil.HexDump(Unpooled.WrappedBuffer(id)));
        }
        return idSet;
    }

    [Sharable]
    private class ReadAndDiscardHandler : SimpleChannelInboundHandler<ByteBuf>
    {
        internal readonly AtomicReference<Exception> exception = new AtomicReference<Exception>();
        private readonly bool server;
        private readonly bool autoRead;

        internal ReadAndDiscardHandler(bool server, bool autoRead)
        {
            this.server = server;
            this.autoRead = autoRead;
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf @in)
        {
            @in.SkipBytes(@in.ReadableBytes);
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            try
            {
                ctx.Flush();
            }
            finally
            {
                if (!autoRead)
                {
                    ctx.Read();
                }
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (logger.IsWarnEnabled())
            {
                logger.Warn(
                        "Unexpected exception from the " +
                        (server? "server" : "client") + " side", cause);
            }

            exception.CompareAndSet(null, cause);
            _ = ctx.CloseAsync();
        }
    }

    // CLR-only helper standing in for the client's SSLSessionContext (see TestSslSessionReuse0): it keeps the session ids
    // of the ServerHello messages that its recorders read.
    private sealed class ClientSessionContext
    {
        private readonly ConcurrentQueue<byte[]> ids = new ConcurrentQueue<byte[]>();

        internal IEnumerator<byte[]> GetIds()
        {
            return ids.GetEnumerator();
        }

        internal IChannelHandler NewServerHelloRecorder()
        {
            return new ServerHelloRecorder(this);
        }

        // Reads the first inbound bytes of a client channel without consuming them: the ServerHello is the first
        // handshake message of the server ("TLSPlaintext" record header of 5 bytes, then "HandshakeType msg_type;
        // uint24 length;", then "ProtocolVersion server_version; Random random; SessionID session_id;" with a 32-byte
        // Random and "opaque SessionID<0..32>", RFC 5246 6.2.1, 7.4 and 7.4.1.3).
        private sealed class ServerHelloRecorder : ChannelInboundHandlerAdapter
        {
            private const byte HANDSHAKE = 22;
            private const byte SERVER_HELLO = 2;
            private const int SESSION_ID_LENGTH_OFFSET = 5 + 4 + 2 + 32;

            private readonly ClientSessionContext sessionContext;
            private ByteBuf cumulation = Unpooled.Buffer();

            internal ServerHelloRecorder(ClientSessionContext sessionContext)
            {
                this.sessionContext = sessionContext;
            }

            public override void ChannelRead(IChannelHandlerContext ctx, object msg)
            {
                if (cumulation != null && msg is ByteBuf buf)
                {
                    cumulation.WriteBytes(buf, buf.ReaderIndex, buf.ReadableBytes);
                    Record();
                }
                ctx.FireChannelRead(msg);
            }

            public override void HandlerRemoved(IChannelHandlerContext ctx)
            {
                Release();
            }

            private void Record()
            {
                if (cumulation.ReadableBytes <= SESSION_ID_LENGTH_OFFSET)
                {
                    return;
                }
                if (cumulation.GetByte(0) == HANDSHAKE && cumulation.GetByte(5) == SERVER_HELLO)
                {
                    int length = cumulation.GetByte(SESSION_ID_LENGTH_OFFSET);
                    if (cumulation.ReadableBytes < SESSION_ID_LENGTH_OFFSET + 1 + length)
                    {
                        return;
                    }
                    if (length > 0)
                    {
                        byte[] id = new byte[length];
                        cumulation.GetBytes(SESSION_ID_LENGTH_OFFSET + 1, id);
                        sessionContext.ids.Enqueue(id);
                    }
                }
                Release();
            }

            private void Release()
            {
                if (cumulation != null)
                {
                    cumulation.Release();
                    cumulation = null;
                }
            }
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
