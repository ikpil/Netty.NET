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
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.PkiTesting;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: data() keeps the JDK provider (SslStream): OpenSSL (netty-tcnative) is library-not-ported (owner decision
// 2026-11-24, docs/handler-ssl-design.md), so OpenSsl.isAvailable() is false and the OPENSSL/OPENSSL_REFCNT rows are not
// added. @ParameterizedTest with @MethodSource is [Theory] with [MemberData]; @Timeout is the Timeout of the [Theory] in
// milliseconds, which xUnit applies to async tests, so those tests return a Task (as CloseNotifyTest does). The SniHandler
// constructors take Func<string, SslContext> for Mapping and DomainNameMapping<SslContext>
// (src/Netty.NET.Handler/Ssl/SniHandler.cs); lookup(ctx, hostname) returns a Task<SslContext>, and
// ctx.executor().newSucceededFuture(value) is Task.FromResult(value) (as SslClientHelloHandlerTest maps
// ImmediateEventExecutor.newSucceededFuture). AtomicReference/AtomicBoolean/AtomicLong are the Netty.NET.Common.Concurrent
// types, CountDownLatch is CountdownEvent (as FlowControlHandlerTest maps it), the anonymous handlers, allocator and
// buffer are named nested classes and e.getCause() is InnerException. Mockito.mock(DomainNameMapping.class) is a Moq class
// mock, whose constructor runs: DomainNameMapping rejects a null default value, so the mock gets a mock SslContext as its
// default; the tests that use it never reach map(...). CachedSelfSignedCertificate (a cache of the @Deprecated
// SelfSignedCertificate, not ported) is a pkitesting bundle of a self-signed "CN=localhost" RSA 2048 certificate (see
// SslContextBuilderTest) and cert.key()/cert.cert() are its key pair and certificate. new SslHandler(ctx.newEngine(alloc,
// host, -1)) is ctx.newHandler(alloc, host, -1) (owner decision 2: SslContext.newEngine is not ported).
// java.lang.Error for an unexpected provider is UnreachableException, as SslContext maps it.
public class SniHandlerTest
{

    private static ApplicationProtocolConfig NewAlpnConfig()
    {
        return new ApplicationProtocolConfig(
                ApplicationProtocolConfig.Protocol.ALPN,
                // NO_ADVERTISE is currently the only mode supported by both OpenSsl and JDK providers.
                ApplicationProtocolConfig.SelectorFailureBehavior.NO_ADVERTISE,
                // ACCEPT is currently the only mode supported by both OpenSsl and JDK providers.
                ApplicationProtocolConfig.SelectedListenerFailureBehavior.ACCEPT,
                "myprotocol");
    }

    // CLR: OpenSsl.isAlpnSupported() is SslProvider.IsAlpnSupported(provider), which answers for the library-not-ported
    // OpenSsl (false); assumeTrue is Assert.SkipUnless. SslProvider is a sealed class (src/Netty.NET.Handler/Ssl/
    // SslProvider.cs), so the switch compares the instances.
    private static void AssumeAlpnSupported(SslProvider provider)
    {
        if (provider == SslProvider.OPENSSL || provider == SslProvider.OPENSSL_REFCNT)
        {
            Assert.SkipUnless(SslProvider.IsAlpnSupported(provider), "ALPN is not supported by " + provider);
        }
        else if (provider == SslProvider.JDK)
        {
        }
        else
        {
            throw new UnreachableException("Unexpected alpn provider: " + provider);
        }
    }

    private static SslContext MakeSslContext(SslProvider provider, bool alpn)
    {
        if (alpn)
        {
            AssumeAlpnSupported(provider);
        }

        FileInfo keyFile = ResourcesUtil.GetFile(typeof(SniHandlerTest), "test_encrypted.pem");
        FileInfo crtFile = ResourcesUtil.GetFile(typeof(SniHandlerTest), "test.crt");

        SslContextBuilder sslCtxBuilder = SslContextBuilder.ForServer(crtFile, keyFile, "12345")
                .SslProvider(provider);

        if (alpn)
        {
            sslCtxBuilder.ApplicationProtocolConfig(NewAlpnConfig());
        }

        return sslCtxBuilder.Build();
    }

    private static SslContext MakeSslClientContext(SslProvider provider, bool alpn)
    {
        if (alpn)
        {
            AssumeAlpnSupported(provider);
        }

        FileInfo crtFile = ResourcesUtil.GetFile(typeof(SniHandlerTest), "test.crt");

        SslContextBuilder sslCtxBuilder = SslContextBuilder.ForClient()
                .TrustManager(crtFile)
                .EndpointIdentificationAlgorithm(null)
                .SslProvider(provider);

        if (alpn)
        {
            sslCtxBuilder.ApplicationProtocolConfig(NewAlpnConfig());
        }

        return sslCtxBuilder.Build();
    }

    public static IEnumerable<object[]> Data()
    {
        List<object[]> @params = new List<object[]>(3);
        @params.Add(new object[] { SslProvider.JDK });
        return @params;
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestNonSslRecord(SslProvider provider)
    {
        SslContext nettyContext = MakeSslContext(provider, false);

        try
        {
            AtomicReference<SslHandshakeCompletionEvent> evtRef =
                    new AtomicReference<SslHandshakeCompletionEvent>();
            SniHandler handler = new SniHandler(new DomainNameMappingBuilder<SslContext>(nettyContext).Build());
            EmbeddedChannel ch = new EmbeddedChannel(handler, new HandshakeEventRecorder(evtRef));

            try
            {
                byte[] bytes = new byte[1024];
                bytes[0] = SslUtils.SSL_CONTENT_TYPE_ALERT;

                DecoderException e = Assert.Throws<DecoderException>(() =>
                {
                    ch.WriteInbound(Unpooled.WrappedBuffer(bytes));
                });
                Assert.IsAssignableFrom<NotSslRecordException>(e.InnerException);
                Assert.False(ch.Finish());
            }
            finally
            {
                ch.FinishAndReleaseAll();
            }

            Assert.IsAssignableFrom<NotSslRecordException>(evtRef.Get().Cause());
        }
        finally
        {
            ReleaseAll(nettyContext);
        }
    }

    private sealed class HandshakeEventRecorder : ChannelInboundHandlerAdapter
    {
        private readonly AtomicReference<SslHandshakeCompletionEvent> evtRef;

        internal HandshakeEventRecorder(AtomicReference<SslHandshakeCompletionEvent> evtRef)
        {
            this.evtRef = evtRef;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is SslHandshakeCompletionEvent)
            {
                Assert.True(evtRef.CompareAndSet(null, (SslHandshakeCompletionEvent) evt));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestServerNameParsing(SslProvider provider)
    {
        SslContext nettyContext = MakeSslContext(provider, false);
        SslContext leanContext = MakeSslContext(provider, false);
        SslContext leanContext2 = MakeSslContext(provider, false);

        try
        {
            DomainNameMapping<SslContext> mapping = new DomainNameMappingBuilder<SslContext>(nettyContext)
                    .Add("*.netty.io", nettyContext)
                    // input with custom cases
                    .Add("*.LEANCLOUD.CN", leanContext)
                    // a hostname conflict with previous one, since we are using order-sensitive config,
                    // the engine won't be used with the handler.
                    .Add("chat4.leancloud.cn", leanContext2)
                    .Build();

            AtomicReference<SniCompletionEvent> evtRef = new AtomicReference<SniCompletionEvent>();
            SniHandler handler = new SniHandler(mapping);
            EmbeddedChannel ch = new EmbeddedChannel(handler, new SniEventRecorder(evtRef));

            try
            {
                // hex dump of a client hello packet, which contains hostname "CHAT4.LEANCLOUD.CN"
                string tlsHandshakeMessageHex1 = "16030100";
                // part 2
                string tlsHandshakeMessageHex = "c6010000c20303bb0855d66532c05a0ef784f7c384feeafa68b3" +
                        "b655ac7288650d5eed4aa3fb52000038c02cc030009fcca9cca8ccaac02b" +
                        "c02f009ec024c028006bc023c0270067c00ac0140039c009c0130033009d" +
                        "009c003d003c0035002f00ff010000610000001700150000124348415434" +
                        "2e4c45414e434c4f55442e434e000b000403000102000a000a0008001d00" +
                        "170019001800230000000d0020001e060106020603050105020503040104" +
                        "0204030301030203030201020202030016000000170000";

                ch.WriteInbound(Unpooled.WrappedBuffer(StringUtil.DecodeHexDump(tlsHandshakeMessageHex1)));
                ch.WriteInbound(Unpooled.WrappedBuffer(StringUtil.DecodeHexDump(tlsHandshakeMessageHex)));

                // This should produce an alert
                Assert.True(ch.Finish());

                Assert.Equal("chat4.leancloud.cn", handler.Hostname());
                Assert.Equal(leanContext, handler.SslContext());

                SniCompletionEvent evt = evtRef.Get();
                Assert.NotNull(evt);
                Assert.Equal("chat4.leancloud.cn", evt.Hostname());
                Assert.True(evt.IsSuccess());
                Assert.Null(evt.Cause());
            }
            finally
            {
                ch.FinishAndReleaseAll();
            }
        }
        finally
        {
            ReleaseAll(leanContext, leanContext2, nettyContext);
        }
    }

    private sealed class SniEventRecorder : ChannelInboundHandlerAdapter
    {
        private readonly AtomicReference<SniCompletionEvent> evtRef;

        internal SniEventRecorder(AtomicReference<SniCompletionEvent> evtRef)
        {
            this.evtRef = evtRef;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is SniCompletionEvent)
            {
                Assert.True(evtRef.CompareAndSet(null, (SniCompletionEvent) evt));
            }
            else
            {
                ctx.FireUserEventTriggered(evt);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestNonAsciiServerNameParsing(SslProvider provider)
    {
        SslContext nettyContext = MakeSslContext(provider, false);
        SslContext leanContext = MakeSslContext(provider, false);
        SslContext leanContext2 = MakeSslContext(provider, false);

        try
        {
            DomainNameMapping<SslContext> mapping = new DomainNameMappingBuilder<SslContext>(nettyContext)
                    .Add("*.netty.io", nettyContext)
                    // input with custom cases
                    .Add("*.LEANCLOUD.CN", leanContext)
                    // a hostname conflict with previous one, since we are using order-sensitive config,
                    // the engine won't be used with the handler.
                    .Add("chat4.leancloud.cn", leanContext2)
                    .Build();

            AtomicReference<Exception> exceptionRef = new AtomicReference<Exception>();
            SniHandler handler = new SniHandler(mapping);
            EmbeddedChannel ch = new EmbeddedChannel(handler, new ExceptionRecorder(exceptionRef));

            try
            {
                // hex dump of a client hello packet, which contains an invalid hostname "CHAT4。LEANCLOUD。CN"
                string tlsHandshakeMessageHex1 = "16030100";
                // part 2
                string tlsHandshakeMessageHex = "bd010000b90303a74225676d1814ba57faff3b366" +
                        "3656ed05ee9dbb2a4dbb1bb1c32d2ea5fc39e0000000100008c0000001700150000164348" +
                        "415434E380824C45414E434C4F5544E38082434E000b000403000102000a00340032000e0" +
                        "00d0019000b000c00180009000a0016001700080006000700140015000400050012001300" +
                        "0100020003000f0010001100230000000d0020001e0601060206030501050205030401040" +
                        "20403030103020303020102020203000f00010133740000";

                // Push the handshake message.
                // Decode should fail because of the badly encoded "HostName" string in the SNI extension
                // that isn't ASCII as per RFC 6066 - https://tools.ietf.org/html/rfc6066#page-6
                ch.WriteInbound(Unpooled.WrappedBuffer(StringUtil.DecodeHexDump(tlsHandshakeMessageHex1)));
                ch.WriteInbound(Unpooled.WrappedBuffer(StringUtil.DecodeHexDump(tlsHandshakeMessageHex)));

                Exception cause = exceptionRef.Get();
                Assert.NotNull(cause);
                Assert.IsAssignableFrom<DecoderException>(cause);
            }
            finally
            {
                ch.FinishAndReleaseAll();
            }
        }
        finally
        {
            ReleaseAll(leanContext, leanContext2, nettyContext);
        }
    }

    private sealed class ExceptionRecorder : ChannelInboundHandlerAdapter
    {
        private readonly AtomicReference<Exception> exceptionRef;

        internal ExceptionRecorder(AtomicReference<Exception> exceptionRef)
        {
            this.exceptionRef = exceptionRef;
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            exceptionRef.CompareAndSet(null, cause);
        }
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestZeroLengthHandshakeRecordFailsHandshake(SslProvider provider)
    {
        SslContext nettyContext = MakeSslContext(provider, false);
        SslContext leanContext = MakeSslContext(provider, false);
        SslContext leanContext2 = MakeSslContext(provider, false);

        try
        {
            DomainNameMapping<SslContext> mapping = new DomainNameMappingBuilder<SslContext>(nettyContext)
                    .Add("*.netty.io", nettyContext)
                    // input with custom cases
                    .Add("*.LEANCLOUD.CN", leanContext)
                    // a hostname conflict with previous one, since we are using order-sensitive config,
                    // the engine won't be used with the handler.
                    .Add("chat4.leancloud.cn", leanContext2)
                    .Build();

            SniHandler handler = new SniHandler(mapping);
            EmbeddedChannel ch = new EmbeddedChannel(handler);

            try
            {
                // Zero-length handshake records are forbidden and must not fall back to the default context.
                // Otherwise a TLS implementation that skips them could process a ClientHello that follows and
                // bypass the SNI based selection.
                DecoderException e = Assert.Throws<DecoderException>(() =>
                {
                    ch.WriteInbound(Unpooled.WrappedBuffer(new byte[] {22, 3, 1, 0, 0}));
                });
                Assert.IsAssignableFrom<NotSslRecordException>(e.InnerException);
                Assert.False(ch.IsActive);
            }
            finally
            {
                ch.FinishAndReleaseAll();
            }

            Assert.Null(handler.Hostname());
            Assert.Null(handler.SslContext());
        }
        finally
        {
            ReleaseAll(leanContext, leanContext2, nettyContext);
        }
    }

    // CLR: Java's lookup(ctx, clientHello) declares "throws Exception" and rethrows with "throw e"; the C# override
    // rethrows with "throw;", which keeps the exception object.
    [Theory]
    [MemberData(nameof(Data))]
    public void TestMalformedClientHelloWithOversizedSessionIdLengthFallsBackToDefaultContext(SslProvider provider)
    {
        SslContext nettyContext = MakeSslContext(provider, false);
        SslContext secureContext = MakeSslContext(provider, false);

        try
        {
            DomainNameMapping<SslContext> mapping = new DomainNameMappingBuilder<SslContext>(nettyContext)
                    .Add("secure.example", secureContext)
                    .Build();

            // decode() swallows any exception thrown while resolving the SslContext for a record and
            // silently falls back to the default context (see testFallbackToDefaultContext above), so an
            // IndexOutOfBoundsException thrown while SniHandler parses the SNI extension would never be
            // observable from the pipeline's perspective. Intercept the internal ByteBuf-based lookup()
            // call -- where extractSniHostname() actually runs -- to assert it completes without throwing.
            AtomicReference<Exception> clientHelloLookupFailure = new AtomicReference<Exception>();
            SniHandler handler = new LookupFailureRecordingSniHandler(mapping, clientHelloLookupFailure);
            EmbeddedChannel ch = new EmbeddedChannel(handler);

            try
            {
                // A single, non-fragmented ClientHello record whose SessionID length field (0xFF)
                // pushes the offset for the subsequent cipher_suites/compression_methods/extensions
                // reads past the end of the record. This used to throw an IndexOutOfBoundsException
                // while SniHandler was still parsing the SNI extension, instead of gracefully falling
                // back to the default SslContext.
                ByteBuf buffer = ch.Alloc().Buffer();
                buffer.WriteByte(0x16);      // Content Type: Handshake
                buffer.WriteShort(0x0303);   // TLS 1.2
                buffer.WriteShort(44);       // Record length: 4 (handshake header) + 40 (body)
                buffer.WriteByte(0x01);      // Handshake Type: ClientHello
                buffer.WriteMedium(40);      // Handshake length
                buffer.WriteZero(34);        // client_version (2) + random (32)
                buffer.WriteByte(0xFF);      // SessionID length -- attacker controlled, way too big
                buffer.WriteZero(5);         // padding so the body is exactly 40 bytes

                try
                {
                    // Once SniHandler resolves the (default) SslContext it hands the record off to a
                    // real SslHandler, which may legitimately reject it as it isn't a well-formed
                    // ClientHello. That failure is unrelated to the bug under test and is asserted
                    // against separately via clientHelloLookupFailure.
                    ch.WriteInbound(buffer);
                }
                catch (Exception)
                {
                    // expected: the garbage ClientHello isn't valid enough for a real SSLEngine handshake
                }

                Assert.Null(clientHelloLookupFailure.Get(), "extractSniHostname() must not throw: "
                        + clientHelloLookupFailure.Get());

                ch.CloseAsync(TestContext.Current.CancellationToken);

                // Discard any outbound alert bytes produced while shutting down the SSLEngine.
                for (;;)
                {
                    ByteBuf buf = ch.ReadOutbound<ByteBuf>();
                    if (buf == null)
                    {
                        break;
                    }

                    buf.Release();
                }

                Assert.False(ch.Finish());
                Assert.Null(handler.Hostname());
                Assert.Equal(nettyContext, handler.SslContext());
            }
            finally
            {
                ch.FinishAndReleaseAll();
            }
        }
        finally
        {
            ReleaseAll(secureContext, nettyContext);
        }
    }

    private sealed class LookupFailureRecordingSniHandler : SniHandler
    {
        private readonly AtomicReference<Exception> clientHelloLookupFailure;

        internal LookupFailureRecordingSniHandler(DomainNameMapping<SslContext> mapping,
                                                  AtomicReference<Exception> clientHelloLookupFailure)
            : base(mapping)
        {
            this.clientHelloLookupFailure = clientHelloLookupFailure;
        }

        protected override Task<SslContext> Lookup(IChannelHandlerContext ctx, ByteBuf clientHello)
        {
            try
            {
                return base.Lookup(ctx, clientHello);
            }
            catch (Exception e)
            {
                clientHelloLookupFailure.CompareAndSet(null, e);
                throw;
            }
        }
    }

    [Theory(Timeout = 10000)]
    [MemberData(nameof(Data))]
    public Task TestMajorVersionNot3(SslProvider provider)
    {
        SslContext nettyContext = MakeSslContext(provider, false);

        try
        {
            DomainNameMapping<SslContext> mapping = new DomainNameMappingBuilder<SslContext>(nettyContext).Build();

            SniHandler handler = new SniHandler(mapping);
            EmbeddedChannel ch = new EmbeddedChannel(handler);

            // invalid
            byte[] message = {22, 2, 0, 0, 0};

            try
            {
                // Push the handshake message.
                ch.WriteInbound(Unpooled.WrappedBuffer(message));
                // TODO(scott): This should fail because the engine should reject zero length records during handshake.
                // See https://github.com/netty/netty/issues/6348.
                // fail();
            }
            catch (Exception)
            {
                // expected
            }

            ch.CloseAsync(TestContext.Current.CancellationToken);

            // Consume all the outbound data that may be produced by the SSLEngine.
            for (;;)
            {
                ByteBuf buf = ch.ReadOutbound<ByteBuf>();
                if (buf == null)
                {
                    break;
                }

                buf.Release();
            }

            Assert.False(ch.Finish());
            Assert.Null(handler.Hostname());
            Assert.Equal(nettyContext, handler.SslContext());
        }
        finally
        {
            ReleaseAll(nettyContext);
        }

        return Task.CompletedTask;
    }

    // CLR: the NIO transport is Netty.NET.Transport.Channels.Nio/Sockets; bind(...)/connect(...)/close().sync() await
    // BindAsync/ConnectAsync/CloseAsync and shutdownGracefully(100, 5000, MILLISECONDS).sync() awaits
    // ShutdownGracefullyAsync with TimeSpans. new InetSocketAddress(0) is the wildcard IPEndPoint of port 0; the JDK
    // connects to the loopback address when given the wildcard, a CLR Socket does not, so the client connects to the
    // loopback address of the bound port (as FlowControlHandlerTest.NewClient replaces it). latch.await(5, SECONDS) is
    // CountdownEvent.Wait(TimeSpan, CancellationToken).
    [Theory]
    [MemberData(nameof(Data))]
    public async Task TestSniWithAlpnHandler(SslProvider provider)
    {
        SslContext nettyContext = null;
        SslContext sniContext = null;
        SslContext clientContext;

        try
        {
            nettyContext = MakeSslContext(provider, true);
            sniContext = MakeSslContext(provider, true);
            clientContext = MakeSslClientContext(provider, true);
        }
        catch (Exception)
        {
            ReferenceCountUtil.SafeRelease(nettyContext);
            ReferenceCountUtil.SafeRelease(sniContext);
            throw;
        }

        try
        {
            AtomicBoolean serverAlpnCtx = new AtomicBoolean(false);
            AtomicBoolean clientAlpnCtx = new AtomicBoolean(false);
            CountdownEvent serverAlpnDoneLatch = new CountdownEvent(1);
            CountdownEvent clientAlpnDoneLatch = new CountdownEvent(1);

            DomainNameMapping<SslContext> mapping = new DomainNameMappingBuilder<SslContext>(nettyContext)
                    .Add("*.netty.io", nettyContext)
                    .Add("sni.fake.site", sniContext).Build();
            SniHandler handler = new SniHandler(mapping);
            IEventLoopGroup group = new MultiThreadIoEventLoopGroup(2, NioIoHandler.NewFactory());
            IChannel serverChannel = null;
            IChannel clientChannel = null;

            try
            {
                ServerBootstrap sb = new ServerBootstrap();
                sb.Group(group);
                sb.Channel<NioServerSocketChannel>();
                sb.ChildHandler(new AlpnServerInitializer(handler, serverAlpnCtx, serverAlpnDoneLatch));

                Bootstrap cb = new Bootstrap();
                cb.Group(group);
                cb.Channel<NioSocketChannel>();
                cb.Handler(new AlpnClientInitializer(clientContext, clientAlpnCtx, clientAlpnDoneLatch));

                serverChannel = await sb.BindAsync(new IPEndPoint(IPAddress.Any, 0), TestContext.Current.CancellationToken);

                IPEndPoint serverAddress = (IPEndPoint) serverChannel.LocalAddress;
                IChannel ccf = await cb.ConnectAsync(new IPEndPoint(IPAddress.Loopback, serverAddress.Port),
                        TestContext.Current.CancellationToken);
                clientChannel = ccf;

                Assert.True(serverAlpnDoneLatch.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
                Assert.True(clientAlpnDoneLatch.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
                Assert.True(serverAlpnCtx.Get());
                Assert.True(clientAlpnCtx.Get());
                Assert.Equal("sni.fake.site", handler.Hostname());
                Assert.Equal(sniContext, handler.SslContext());
            }
            finally
            {
                if (serverChannel != null)
                {
                    await serverChannel.CloseAsync(TestContext.Current.CancellationToken);
                }

                if (clientChannel != null)
                {
                    await clientChannel.CloseAsync(TestContext.Current.CancellationToken);
                }

                await group.ShutdownGracefullyAsync(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(5000));
            }
        }
        finally
        {
            ReleaseAll(clientContext, nettyContext, sniContext);
        }
    }

    private sealed class AlpnServerInitializer : ChannelInitializer<IChannel>
    {
        private readonly SniHandler handler;
        private readonly AtomicBoolean serverAlpnCtx;
        private readonly CountdownEvent serverAlpnDoneLatch;

        internal AlpnServerInitializer(SniHandler handler, AtomicBoolean serverAlpnCtx,
                                       CountdownEvent serverAlpnDoneLatch)
        {
            this.handler = handler;
            this.serverAlpnCtx = serverAlpnCtx;
            this.serverAlpnDoneLatch = serverAlpnDoneLatch;
        }

        protected override void InitChannel(IChannel ch)
        {
            IChannelPipeline p = ch.Pipeline;
            // Server side SNI.
            p.AddLast(handler);
            // Catch the notification event that ALPN has completed successfully.
            p.AddLast(new AlpnDoneHandler(serverAlpnCtx, serverAlpnDoneLatch));
        }
    }

    private sealed class AlpnClientInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslContext clientContext;
        private readonly AtomicBoolean clientAlpnCtx;
        private readonly CountdownEvent clientAlpnDoneLatch;

        internal AlpnClientInitializer(SslContext clientContext, AtomicBoolean clientAlpnCtx,
                                       CountdownEvent clientAlpnDoneLatch)
        {
            this.clientContext = clientContext;
            this.clientAlpnCtx = clientAlpnCtx;
            this.clientAlpnDoneLatch = clientAlpnDoneLatch;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Pipeline.AddLast(clientContext.NewHandler(
                    ch.Alloc(), "sni.fake.site", -1));
            // Catch the notification event that ALPN has completed successfully.
            ch.Pipeline.AddLast(new AlpnDoneHandler(clientAlpnCtx, clientAlpnDoneLatch));
        }
    }

    private sealed class AlpnDoneHandler : ApplicationProtocolNegotiationHandler
    {
        private readonly AtomicBoolean alpnCtx;
        private readonly CountdownEvent alpnDoneLatch;

        internal AlpnDoneHandler(AtomicBoolean alpnCtx, CountdownEvent alpnDoneLatch) : base("foo")
        {
            this.alpnCtx = alpnCtx;
            this.alpnDoneLatch = alpnDoneLatch;
        }

        protected override void ConfigurePipeline(IChannelHandlerContext ctx, string protocol)
        {
            // addresses issue #9131
            alpnCtx.Set(ctx.Pipeline.Context(this) != null);
            alpnDoneLatch.Signal();
        }
    }

    // CLR: the OPENSSL/OPENSSL_REFCNT branch is not ported: it checks the reference count of a
    // ReferenceCountedOpenSslContext and of its SSLEngine (newEngine) inside a custom replaceHandler, and OpenSSL is
    // library-not-ported (owner decision 2026-11-24); it is unreachable, as data() has no OpenSSL row, and skips. Its
    // CustomSslHandler (an SslHandler(SSLEngine) that releases the reference-counted SslContext in handlerRemoved0) only
    // serves it and is not ported either; JdkSslContext is not reference counted. As in Java, the JDK case returns at
    // once. The original comments of the branch and of CustomSslHandler:
    // The SniHandler's replaceHandler() method allows us to implement custom behavior.
    // As an example, we want to release() the SslContext upon channelInactive() or rather
    // when the SslHandler closes it's SslEngine. If you take a close look at SslHandler
    // you'll see that it's doing it in the #handlerRemoved0() method.
    // Notice how the server's SslContext refCnt is 2 as it is incremented when the SSLEngine is created
    // and only decremented once it is destroyed.
    // The client disconnects
    // We should have successfully release() the SslContext
    /**
     * This is a {@link SslHandler} that will call {@code release()} on the {@link SslContext} when
     * the client disconnects.
     *
     * @see SniHandlerTest#testReplaceHandler(SslProvider)
     */
#pragma warning disable xUnit1069 // The JDK case returns at once; there is no call for the token to end.
    [Theory(Timeout = 30000)]
    [MemberData(nameof(Data))]
    public Task TestReplaceHandler(SslProvider provider)
    {
        if (provider == SslProvider.OPENSSL || provider == SslProvider.OPENSSL_REFCNT)
        {
            Assert.Skip("OpenSSL (netty-tcnative) is library-not-ported: " + provider);
        }
        else if (provider == SslProvider.JDK)
        {
            return Task.CompletedTask;
        }

        throw new UnreachableException("Unexpected ssl provider: " + provider);
    }
#pragma warning restore xUnit1069

    // CLR: ReferenceCountUtil.release of a JdkSslContext, which is not reference counted, returns false, as in Java.
    private static void ReleaseAll(params SslContext[] contexts)
    {
        foreach (SslContext ctx in contexts)
        {
            ReferenceCountUtil.Release(ctx);
        }
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestNonFragmented(SslProvider provider)
    {
        TestWithFragmentSize(provider, int.MaxValue);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestFragmented(SslProvider provider)
    {
        TestWithFragmentSize(provider, 50);
    }

    public static IEnumerable<object[]> TinyFragmentData()
    {
        List<object[]> args = new List<object[]>();

        foreach (object[] provider in Data())
        {
            // Fragment sizes smaller than the 4-byte handshake header, so the header itself is
            // split across multiple TLS records.
            for (int size = 1; size <= 4; size++)
            {
                args.Add(new object[] { provider[0], size });
            }
        }

        return args;
    }

    [Theory]
    [MemberData(nameof(TinyFragmentData))]
    public void TestTinyFragments(SslProvider provider, int fragmentSize)
    {
        TestWithFragmentSize(provider, fragmentSize);
    }

    // CLR: server.config().setAllocator(...) is Config.SetAllocator; fragments.remove(size - 1) takes and removes
    // the last element.
    [Theory]
    [MemberData(nameof(Data))]
    public void TestTinyFragmentsAreAggregatedOnlyOnce(SslProvider provider)
    {
        AtomicLong copiedBytes = new AtomicLong();
        EmbeddedChannel server = new EmbeddedChannel(new SniHandler(MockDomainNameMapping()));
        server.Config.SetAllocator(new CountingAllocator(copiedBytes));

        try
        {
            List<ByteBuf> fragments = ClientHelloInMultipleFragments(provider, "netty.io", 1, 1);
            // Hold back the last fragment on purpose, so the handler keeps aggregating the ClientHello.
            ByteBuf last = fragments[fragments.Count - 1];
            fragments.RemoveAt(fragments.Count - 1);
            ReferenceCountUtil.Release(last);

            foreach (ByteBuf fragment in fragments)
            {
                Assert.False(server.WriteInbound(fragment));
            }

            Assert.Equal(fragments.Count, copiedBytes.Get());
        }
        finally
        {
            server.FinishAndReleaseAll();
        }
    }

    private sealed class CountingAllocator : AbstractByteBufAllocator
    {
        private readonly AtomicLong copiedBytes;

        internal CountingAllocator(AtomicLong copiedBytes)
        {
            this.copiedBytes = copiedBytes;
        }

        public override bool IsDirectBufferPooled => false;

        protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity)
        {
            return CountingBuffer(Unpooled.Buffer(initialCapacity, maxCapacity), copiedBytes);
        }

        protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
        {
            return CountingBuffer(Unpooled.DirectBuffer(initialCapacity, maxCapacity), copiedBytes);
        }
    }

    private static ByteBuf CountingBuffer(ByteBuf buffer, AtomicLong copiedBytes)
    {
        return new CountingWrappedByteBuf(buffer, copiedBytes);
    }

    private sealed class CountingWrappedByteBuf : WrappedByteBuf
    {
        private readonly AtomicLong copiedBytes;

        internal CountingWrappedByteBuf(ByteBuf buffer, AtomicLong copiedBytes) : base(buffer)
        {
            this.copiedBytes = copiedBytes;
        }

        public override ByteBuf WriteBytes(ByteBuf src, int srcIndex, int length)
        {
            copiedBytes.AddAndGet(length);

            return base.WriteBytes(src, srcIndex, length);
        }
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestTinyFirstFragment(SslProvider provider)
    {
        TestWithFragmentSize(provider, 1, int.MaxValue);
    }

    private void TestWithFragmentSize(SslProvider provider, int maxFragmentSize)
    {
        TestWithFragmentSize(provider, maxFragmentSize, maxFragmentSize);
    }

    // CLR: platform limit (measured on Windows 11 10.0.26300,.NET 10.0.8): the SChannel-based SslStream server
    // rejects a ClientHello whose first TLS record carries only a few of its bytes. A raw SslStream server (no
    // Netty) fed the same ClientHello re-fragmented with a first record of 1, 4 or 5 bytes fails with
    // AuthenticationException (inner Win32Exception "The message received was unexpected or badly formatted") and
    // writes a 7-byte alert, while first records of 10, 20, 30, 40 or 50 bytes and an unfragmented record are
    // accepted (the server answers with its handshake flight). SniHandler itself aggregates the tiny fragments
    // (testTinyFragmentsAreAggregatedOnlyOnce passes); the SslHandler it installs then hands the same records to
    // SslStream, so testTinyFragments (1 to 4 bytes) and testTinyFirstFragment (1 byte) skip on Windows with this
    // reason, as other measured SslStream platform limits do.
    private void TestWithFragmentSize(SslProvider provider, int firstFragmentSize, int maxFragmentSize)
    {
        if (OperatingSystem.IsWindows() && firstFragmentSize <= 5)
        {
            Assert.Skip("SslStream (SChannel) rejects a ClientHello whose first TLS record carries " + firstFragmentSize +
                    " byte(s) of it: \"The message received was unexpected or badly formatted\" (measured on Windows)");
        }

        string sni = "netty.io";
        X509Bundle cert = CachedSelfSignedCertificate();
        SslContext context = SslContextBuilder.ForServer((AsymmetricAlgorithm) cert.GetKeyPair(), cert.GetCertificate())
                .SslProvider(provider)
                .Build();

        try
        {
            EmbeddedChannel server = new EmbeddedChannel(
                    new FixedLookupSniHandler(MockDomainNameMapping(), sni, context));

            List<ByteBuf> buffers =
                    ClientHelloInMultipleFragments(provider, sni, firstFragmentSize, maxFragmentSize);

            foreach (ByteBuf buffer in buffers)
            {
                server.WriteInbound(buffer);
            }

            Assert.True(server.FinishAndReleaseAll());
        }
        finally
        {
            ReleaseAll(context);
        }
    }

    private sealed class FixedLookupSniHandler : SniHandler
    {
        private readonly string sni;
        private readonly SslContext context;

        internal FixedLookupSniHandler(DomainNameMapping<SslContext> mapping, string sni, SslContext context)
            : base(mapping)
        {
            this.sni = sni;
            this.context = context;
        }

        protected override Task<SslContext> Lookup(IChannelHandlerContext ctx, string hostname)
        {
            Assert.Equal(sni, hostname);

            return Task.FromResult(context);
        }
    }

    private static List<ByteBuf> ClientHelloInMultipleFragments(
            SslProvider provider, string hostname, int firstTlsPlaintextSize, int maxTlsPlaintextSize)
    {
        EmbeddedChannel client = new EmbeddedChannel();
        SslContext ctx = SslContextBuilder.ForClient()
                .SslProvider(provider)
                .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                .Build();

        try
        {
            SslHandler sslHandler = ctx.NewHandler(client.Alloc(), hostname, -1);
            client.Pipeline.AddLast(sslHandler);
            ByteBuf clientHello = client.ReadOutbound<ByteBuf>();
            List<ByteBuf> buffers = Split(clientHello, firstTlsPlaintextSize, maxTlsPlaintextSize);
            Assert.True(client.FinishAndReleaseAll());
            return buffers;
        }
        finally
        {
            ReleaseAll(ctx);
        }
    }

    // CLR: readUnsignedByte() is ReadByte() (an unsigned System.Byte, owner decision) and readUnsignedShort() is
    // ReadUnsignedShort(); Math.min is Math.Min.
    private static List<ByteBuf> Split(ByteBuf clientHello, int firstSize, int maxSize)
    {
        int type = clientHello.ReadByte();
        int version = clientHello.ReadUnsignedShort();
        int length = clientHello.ReadUnsignedShort();
        Assert.Equal(length, clientHello.ReadableBytes);

        List<ByteBuf> result = new List<ByteBuf>();

        while (clientHello.ReadableBytes > 0)
        {
            int toRead = Math.Min(result.Count == 0 ? firstSize : maxSize, clientHello.ReadableBytes);
            ByteBuf bb = clientHello.Alloc().Buffer(SslUtils.SSL_RECORD_HEADER_LENGTH + toRead);
            bb.WriteByte(type);
            bb.WriteShort(version);
            bb.WriteShort(toRead);
            bb.WriteBytes(clientHello, toRead);
            result.Add(bb);
        }

        clientHello.Release();
        return result;
    }

    // CLR: UnsupportedOperationException is NotSupportedException; (short)/(byte) casts of the written values have no
    // counterpart, as the ByteBuf write methods take an int.
    [Fact]
    public void TestSniHandlerFailsOnTooBigClientHello()
    {
        SniHandler handler = new SniHandler(input =>
        {
            throw new NotSupportedException("Should not be called");
        }, 10, 0);

        AtomicReference<SniCompletionEvent> completionEventRef =
                new AtomicReference<SniCompletionEvent>();
        EmbeddedChannel ch = new EmbeddedChannel(handler, new SniEventSetter(completionEventRef));
        ByteBuf buffer = ch.Alloc().Buffer();
        buffer.WriteByte(0x16);      // Content Type: Handshake
        buffer.WriteShort(0x0303); // TLS 1.2
        buffer.WriteShort(0x0006); // Packet length

        // 16_777_215
        buffer.WriteByte(0x01); // Client Hello
        buffer.WriteMedium(0xFFFFFF); // Length
        buffer.WriteShort(0x0303); // TLS 1.2

        Assert.Throws<TooLongFrameException>(() =>
        {
            ch.WriteInbound(buffer);
        });

        try
        {
            while (completionEventRef.Get() == null)
            {
                Thread.Sleep(100);
                // We need to run all pending tasks as the handshake timeout is scheduled on the EventLoop.
                ch.RunPendingTasks();
            }

            SniCompletionEvent completionEvent = completionEventRef.Get();
            Assert.NotNull(completionEvent);
            Assert.NotNull(completionEvent.Cause());
            Assert.Equal(typeof(TooLongFrameException), completionEvent.Cause().GetType());
        }
        finally
        {
            ch.FinishAndReleaseAll();
        }
    }

    private sealed class SniEventSetter : ChannelInboundHandlerAdapter
    {
        private readonly AtomicReference<SniCompletionEvent> completionEventRef;

        internal SniEventSetter(AtomicReference<SniCompletionEvent> completionEventRef)
        {
            this.completionEventRef = completionEventRef;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is SniCompletionEvent)
            {
                completionEventRef.Set((SniCompletionEvent) evt);
            }
        }
    }

    [Fact]
    public void TestSniHandlerFiresHandshakeTimeout()
    {
        SniHandler handler = new SniHandler(input =>
        {
            throw new NotSupportedException("Should not be called");
        }, 0, 10);

        AtomicReference<SniCompletionEvent> completionEventRef =
            new AtomicReference<SniCompletionEvent>();
        EmbeddedChannel ch = new EmbeddedChannel(handler, new SniEventSetter(completionEventRef));

        try
        {
            while (completionEventRef.Get() == null)
            {
                Thread.Sleep(100);
                // We need to run all pending tasks as the handshake timeout is scheduled on the EventLoop.
                ch.RunPendingTasks();
            }

            SniCompletionEvent completionEvent = completionEventRef.Get();
            Assert.NotNull(completionEvent);
            Assert.NotNull(completionEvent.Cause());
            Assert.Equal(typeof(SslHandshakeTimeoutException), completionEvent.Cause().GetType());
        }
        finally
        {
            ch.FinishAndReleaseAll();
        }
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestSslHandlerFiresHandshakeTimeout(SslProvider provider)
    {
        SslContext context = MakeSslContext(provider, false);
        SniHandler handler = new SniHandler(input => context, 0, 100);

        AtomicReference<SniCompletionEvent> sniCompletionEventRef =
            new AtomicReference<SniCompletionEvent>();
        AtomicReference<SslHandshakeCompletionEvent> handshakeCompletionEventRef =
            new AtomicReference<SslHandshakeCompletionEvent>();
        EmbeddedChannel ch = new EmbeddedChannel(handler,
                new CompletionEventSetter(sniCompletionEventRef, handshakeCompletionEventRef));

        try
        {
            // Send enough data to add the SslHandler and let the handshake incomplete
            // Client Hello with "host1" server name
            ch.WriteInbound(Unpooled.WrappedBuffer(StringUtil.DecodeHexDump(
                "16030301800100017c0303478ae7e536aa7a9debad1f873121862d2d3d3173e0ef42975c31007faeb2" +
                "52522047f55f81fc84fe58951e2af14026147d6178498fde551fcbafc636462c016ec9005a13011302" +
                "c02cc02bc030009dc02ec032009f00a3c02f009cc02dc031009e00a2c024c028003dc026c02a006b00" +
                "6ac00ac0140035c005c00f00390038c023c027003cc025c02900670040c009c013002fc004c00e0033" +
                "003200ff010000d90000000a0008000005686f737431000500050100000000000a00160014001d0017" +
                "00180019001e01000101010201030104000b00020100000d0028002604030503060308040805080608" +
                "09080a080b040105010601040203030301030202030201020200320028002604030503060308040805" +
                "08060809080a080b040105010601040203030301030202030201020200110009000702000400000000" +
                "00170000002b00050403040303002d00020101003300260024001d00200bbc37375e214c1e4e7cb90f" +
                "869e131dc983a21f8205ba24456177f340904935")));

            while (handshakeCompletionEventRef.Get() == null)
            {
                Thread.Sleep(10);
                // We need to run all pending tasks as the handshake timeout is scheduled on the EventLoop.
                ch.RunPendingTasks();
            }

            SniCompletionEvent sniCompletionEvent = sniCompletionEventRef.Get();
            Assert.NotNull(sniCompletionEvent);
            Assert.Equal("host1", sniCompletionEvent.Hostname());
            SslCompletionEvent handshakeCompletionEvent = handshakeCompletionEventRef.Get();
            Assert.NotNull(handshakeCompletionEvent);
            Assert.NotNull(handshakeCompletionEvent.Cause());
            Assert.Equal(typeof(SslHandshakeTimeoutException), handshakeCompletionEvent.Cause().GetType());
        }
        finally
        {
            ch.FinishAndReleaseAll();
            ReleaseAll(context);
        }
    }

    private sealed class CompletionEventSetter : ChannelInboundHandlerAdapter
    {
        private readonly AtomicReference<SniCompletionEvent> sniCompletionEventRef;
        private readonly AtomicReference<SslHandshakeCompletionEvent> handshakeCompletionEventRef;

        internal CompletionEventSetter(AtomicReference<SniCompletionEvent> sniCompletionEventRef,
                                       AtomicReference<SslHandshakeCompletionEvent> handshakeCompletionEventRef)
        {
            this.sniCompletionEventRef = sniCompletionEventRef;
            this.handshakeCompletionEventRef = handshakeCompletionEventRef;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is SniCompletionEvent)
            {
                sniCompletionEventRef.Set((SniCompletionEvent) evt);
            }
            else if (evt is SslHandshakeCompletionEvent)
            {
                handshakeCompletionEventRef.Set((SslHandshakeCompletionEvent) evt);
            }
        }
    }

    // CLR: mock(DomainNameMapping.class).
    private static DomainNameMapping<SslContext> MockDomainNameMapping()
    {
        return new Mock<DomainNameMapping<SslContext>>(new Mock<SslContext>().Object).Object;
    }

    private static readonly Lazy<X509Bundle> CACHED_CERTIFICATE = new Lazy<X509Bundle>(
            () => new CertificateBuilder()
                    .Subject("CN=localhost")
                    .Rsa2048()
                    .SetIsCertificateAuthority(true)
                    .BuildSelfSigned());

    private static X509Bundle CachedSelfSignedCertificate()
    {
        return CACHED_CERTIFICATE.Value;
    }
}
