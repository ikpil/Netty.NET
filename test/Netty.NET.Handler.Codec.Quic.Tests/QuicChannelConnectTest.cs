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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR: the methods whose scenario System.Net.Quic supports on loopback (docs/codec-native-quic-porting.md); the others
// are recorded CLR-inapplicable with their reasons in docs/codec-native-quic-porting-manifest.json, and their original
// comments follow the class. @ParameterizedTest @MethodSource("newSslTaskExecutors") is a theory over
// AbstractQuicTest.NewSslTaskExecutors(); Future.await().cause() is the exception of the awaited Task, get() and sync()
// are awaits. new DatagramSocket() (a wildcard UDP socket that no QUIC server answers) is a UdpClient on the IPv4
// loopback, because msquic sends to the address it is given and 0.0.0.0 is not a destination on Windows.
// TestQuicTokenHandler (a token handler of its own) is reported unsupported by the msquic server codec builder, which
// validates addresses with msquic's retry tokens, so the scenarios that only need a working server use
// InsecureQuicTokenHandler. AlreadyConnectedException is SocketException(SocketError.IsConnected) and SSLException
// AuthenticationException (the Transport and handler mappings); an X509TrustManager whose checkServerTrusted throws
// CertificateException is a RemoteCertificateValidationCallback that returns false (TrustManagerFactoryWrapper is not
// needed), for the extended and the plain trust manager alike; the X509ExtendedKeyManager without a client alias is a
// LocalCertificateSelectionCallback that selects no certificate. fail() is Assert.Fail and BlockingQueue take() waits at
// most the test timeout.
public class QuicChannelConnectTest : AbstractQuicTest
{

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestConnectTimeout(IExecutor executor)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        // Bind to something so we can use the port to connect too and so can ensure we really timeout.
        UdpClient socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        IChannel channel = await QuicTestUtils.NewClient(executor);

        try
        {
            ChannelStateVerifyHandler verifyHandler = new ChannelStateVerifyHandler();
            Task<IQuicChannel> future = QuicTestUtils.NewQuicChannelBootstrap(channel)
                    .Handler(verifyHandler)
                    .StreamHandler(new ChannelInboundHandlerAdapter())
                    .Option(ChannelOption.CONNECT_TIMEOUT_MILLIS, 10)
                    .RemoteAddress(socket.Client.LocalEndPoint)
                    .ConnectAsync(cancellationToken);
            Exception cause = await Cause(future, cancellationToken);
            Assert.IsAssignableFrom<ConnectTimeoutException>(cause);
            verifyHandler.AssertState();
        }
        finally
        {
            socket.Dispose();
            // Close the parent Datagram channel as well.
            await channel.CloseAsync(CancellationToken.None);

            Shutdown(executor);
        }
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestConnectFailsInParentPipeline(IExecutor executor)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        // Bind to something so we can use the port to connect too and so can ensure we really timeout.
        UdpClient socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        IChannel channel = await QuicTestUtils.NewClient(executor);
        Exception exception = new NotSupportedException();
        channel.Pipeline.AddLast(new FailConnectHandler(exception));

        try
        {
            ChannelStateVerifyHandler verifyHandler = new ChannelStateVerifyHandler();
            Task<IQuicChannel> future = QuicTestUtils.NewQuicChannelBootstrap(channel)
                    .Handler(verifyHandler)
                    .StreamHandler(new ChannelInboundHandlerAdapter())
                    .RemoteAddress(socket.Client.LocalEndPoint)
                    .ConnectAsync(cancellationToken);
            Exception cause = await Cause(future, cancellationToken);
            Assert.Same(cause, exception);
            verifyHandler.AssertState();
        }
        finally
        {
            socket.Dispose();
            // Close the parent Datagram channel as well.
            await channel.CloseAsync(CancellationToken.None);

            Shutdown(executor);
        }
    }

    // The anonymous ChannelOutboundHandlerAdapter of testConnectFailsInParentPipeline.
    private sealed class FailConnectHandler : ChannelOutboundHandlerAdapter
    {
        private readonly Exception exception;

        internal FailConnectHandler(Exception exception)
        {
            this.exception = exception;
        }

        public override Task ConnectAsync(IChannelHandlerContext ctx, EndPoint remoteAddress, EndPoint localAddress,
                                          CancellationToken cancellationToken)
        {
            return Task.FromException(exception);
        }
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestConnectAlreadyConnected(IExecutor executor)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ChannelActiveVerifyHandler serverQuicChannelHandler = new ChannelActiveVerifyHandler();
        ChannelStateVerifyHandler serverQuicStreamHandler = new ChannelStateVerifyHandler();

        IChannel server = await QuicTestUtils.NewServer(executor, serverQuicChannelHandler, serverQuicStreamHandler);
        IPEndPoint address = (IPEndPoint) server.LocalAddress;
        IChannel channel = await QuicTestUtils.NewClient(executor);

        try
        {
            ChannelActiveVerifyHandler clientQuicChannelHandler = new ChannelActiveVerifyHandler();
            IQuicChannel quicChannel = await QuicTestUtils.NewQuicChannelBootstrap(channel)
                    .Handler(clientQuicChannelHandler)
                    .StreamHandler(new ChannelInboundHandlerAdapter())
                    .RemoteAddress(address)
                    .ConnectAsync(cancellationToken);

            // Try to connect again
            Task connectFuture = quicChannel.ConnectAsync(QuicConnectionAddress.Random(), null, cancellationToken);
            Exception cause = await Cause(connectFuture, cancellationToken);
            SocketException alreadyConnected = Assert.IsType<SocketException>(cause);
            Assert.Equal(SocketError.IsConnected, alreadyConnected.SocketErrorCode);
            await quicChannel.CloseAsync(cancellationToken);
            await quicChannel.CloseFuture.WaitAsync(cancellationToken);
            Assert.True(quicChannel.CloseFuture.IsCompletedSuccessfully);
            clientQuicChannelHandler.AssertState();
            serverQuicChannelHandler.AssertState();
            serverQuicStreamHandler.AssertState();
        }
        finally
        {
            await server.CloseAsync(CancellationToken.None);
            // Close the parent Datagram channel as well.
            await channel.CloseAsync(CancellationToken.None);

            Shutdown(executor);
        }
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestConnectMultipleTimes(IExecutor executor)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        IChannel server = await QuicTestUtils.NewServer(executor, QuicTestUtils.NOOP_HANDLER,
                QuicTestUtils.NOOP_HANDLER);
        IPEndPoint address = (IPEndPoint) server.LocalAddress;
        IChannel channel = await QuicTestUtils.NewClient(executor);

        try
        {
            QuicChannelBootstrap cb = QuicTestUtils.NewQuicChannelBootstrap(channel)
                    .Handler(QuicTestUtils.NOOP_HANDLER)
                    .StreamHandler(QuicTestUtils.NOOP_HANDLER)
                    .RemoteAddress(address);
            List<IQuicChannel> channels = new List<IQuicChannel>();

            for (int i = 0; i < 5; i++)
            {
                channels.Add(await cb
                        .ConnectAsync(cancellationToken));
            }

            foreach (IQuicChannel ch in channels)
            {
                await ch.CloseAsync(cancellationToken);
            }
        }
        finally
        {
            await server.CloseAsync(CancellationToken.None);
            // Close the parent Datagram channel as well.
            await channel.CloseAsync(CancellationToken.None);

            Shutdown(executor);
        }
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestExtendedTrustManagerFailureOnTheClient(IExecutor executor)
    {
        await TestTrustManagerFailureOnTheClient0(executor, true, TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestTrustManagerFailureOnTheClient(IExecutor executor)
    {
        await TestTrustManagerFailureOnTheClient0(executor, false, TestContext.Current.CancellationToken);
    }

    // CLR: the private overload is TestTrustManagerFailureOnTheClient0 (xUnit rejects an overloaded test method name).
    private static async Task TestTrustManagerFailureOnTheClient0(IExecutor executor, bool extended,
                                                                 CancellationToken cancellationToken)
    {
        // checkServerTrusted throws CertificateException (see the class comment).
        RemoteCertificateValidationCallback trustManager = (sender, certificate, chain, errors) => false;
        IChannel server = await QuicTestUtils.NewServer(executor, new ChannelInboundHandlerAdapter(),
                new ChannelInboundHandlerAdapter());
        IPEndPoint address = (IPEndPoint) server.LocalAddress;
        QuicSslContextBuilder ctxBuilder = QuicSslContextBuilder.ForClient()
            .TrustManager(trustManager)
            .ApplicationProtocols(QuicTestUtils.PROTOS);

        if (!extended)
        {
            ctxBuilder.EndpointIdentificationAlgorithm(null);
        }

        IChannel channel = await QuicTestUtils.NewClient(
                QuicTestUtils.NewQuicClientBuilder(executor, ctxBuilder.Build()));

        try
        {
            Exception cause = await Cause(QuicTestUtils.NewQuicChannelBootstrap(channel)
                    .Handler(new ChannelInboundHandlerAdapter())
                    .StreamHandler(new ChannelInboundHandlerAdapter())
                    .RemoteAddress(address)
                    .ConnectAsync(cancellationToken), cancellationToken);
            Assert.IsAssignableFrom<AuthenticationException>(cause);
        }
        finally
        {
            await server.CloseAsync(CancellationToken.None);
            // Close the parent Datagram channel as well.
            await channel.CloseAsync(CancellationToken.None);

            Shutdown(executor);
        }
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestConnectSuccessWhenTrustManagerBuildFromSameCert(IExecutor executor)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        IChannel server = await QuicTestUtils.NewServer(QuicTestUtils.NewQuicServerBuilder(executor,
                        QuicSslContextBuilder.ForServer(
                                QuicTestUtils.SELF_SIGNED_CERTIFICATE.PrivateKey(), null,
                                        QuicTestUtils.SELF_SIGNED_CERTIFICATE.Certificate())
                                .ApplicationProtocols(QuicTestUtils.PROTOS).ClientAuth(ClientAuth.NONE).Build()),
                InsecureQuicTokenHandler.INSTANCE, new ChannelInboundHandlerAdapter(),
                new ChannelInboundHandlerAdapter());
        IPEndPoint address = (IPEndPoint) server.LocalAddress;

        QuicSslContext clientSslContext = QuicSslContextBuilder.ForClient()
                .TrustManager(QuicTestUtils.SELF_SIGNED_CERTIFICATE.Certificate())
                .ApplicationProtocols(QuicTestUtils.PROTOS).Build();
        IChannel channel = await QuicTestUtils.NewClient(QuicTestUtils.NewQuicClientBuilder(executor,
                        clientSslContext)
                        .SslEngineProvider(ch =>
                                clientSslContext.NewEngine(ch.Alloc(), "localhost", address.Port)));

        try
        {
            ChannelActiveVerifyHandler clientQuicChannelHandler = new ChannelActiveVerifyHandler();
            IQuicChannel quicChannel = await QuicTestUtils.NewQuicChannelBootstrap(channel)
                    .Handler(clientQuicChannelHandler)
                    .StreamHandler(new ChannelInboundHandlerAdapter())
                    .RemoteAddress(address)
                    .ConnectAsync(cancellationToken);
            await quicChannel.CloseAsync(cancellationToken);
            await quicChannel.CloseFuture.WaitAsync(cancellationToken);
            Assert.True(quicChannel.CloseFuture.IsCompletedSuccessfully);
            clientQuicChannelHandler.AssertState();
        }
        finally
        {
            await server.CloseAsync(CancellationToken.None);
            // Close the parent Datagram channel as well.
            await channel.CloseAsync(CancellationToken.None);

            Shutdown(executor);
        }
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestConnectMutualAuthRequiredSuccess(IExecutor executor)
    {
        await TestConnectMutualAuthSuccess(executor, MutalAuthTestMode.REQUIRED,
                TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestConnectMutualAuthOptionalWithCertSuccess(IExecutor executor)
    {
        await TestConnectMutualAuthSuccess(executor, MutalAuthTestMode.OPTIONAL_CERT,
                TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestConnectMutualAuthOptionalWithoutKeyManagerSuccess(IExecutor executor)
    {
        await TestConnectMutualAuthSuccess(executor, MutalAuthTestMode.OPTIONAL_NO_KEYMANAGER,
                TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestConnectMutualAuthOptionalWithoutKeyInKeyManagerSuccess(IExecutor executor)
    {
        await TestConnectMutualAuthSuccess(executor, MutalAuthTestMode.OPTIONAL_NO_KEY_IN_KEYMANAGER,
                TestContext.Current.CancellationToken);
    }

    private static async Task TestConnectMutualAuthSuccess(IExecutor executor, MutalAuthTestMode mode,
                                                           CancellationToken cancellationToken)
    {
        // CLR: the server's QuicChannel handler records its channelActive, and the client closes only after it (see
        // ServerActiveHandler).
        ServerActiveHandler serverActive = new ServerActiveHandler();
        IChannel server = await QuicTestUtils.NewServer(QuicTestUtils.NewQuicServerBuilder(executor,
                        QuicSslContextBuilder.ForServer(
                                        QuicTestUtils.SELF_SIGNED_CERTIFICATE.PrivateKey(), null,
                                        QuicTestUtils.SELF_SIGNED_CERTIFICATE.Certificate()).TrustManager(
                                        InsecureTrustManagerFactory.INSTANCE)
                                .ApplicationProtocols(QuicTestUtils.PROTOS)
                                .ClientAuth(mode == MutalAuthTestMode.REQUIRED ?
                                        ClientAuth.REQUIRE : ClientAuth.OPTIONAL).Build()),
                InsecureQuicTokenHandler.INSTANCE, serverActive,
                new ChannelInboundHandlerAdapter());
        IPEndPoint address = (IPEndPoint) server.LocalAddress;

        QuicSslContextBuilder clientSslCtxBuilder = QuicSslContextBuilder.ForClient()
                .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                .ApplicationProtocols(QuicTestUtils.PROTOS);

        switch (mode)
        {
            case MutalAuthTestMode.OPTIONAL_CERT:
            case MutalAuthTestMode.REQUIRED:
                clientSslCtxBuilder.KeyManager(
                        QuicTestUtils.SELF_SIGNED_CERTIFICATE.PrivateKey(), null,
                        QuicTestUtils.SELF_SIGNED_CERTIFICATE.Certificate());
                break;
            case MutalAuthTestMode.OPTIONAL_NO_KEY_IN_KEYMANAGER:
                clientSslCtxBuilder.KeyManager(
                        (LocalCertificateSelectionCallback) ((sender, targetHost, localCertificates,
                                                             remoteCertificate, acceptableIssuers) => null), null);
                break;
            case MutalAuthTestMode.OPTIONAL_NO_KEYMANAGER:
                break;
            default:
                throw new InvalidOperationException();
        }

        IChannel channel = await QuicTestUtils.NewClient(QuicTestUtils.NewQuicClientBuilder(executor,
                clientSslCtxBuilder.Build()));

        try
        {
            ChannelActiveVerifyHandler clientQuicChannelHandler = new ChannelActiveVerifyHandler();
            IQuicChannel quicChannel = await QuicTestUtils.NewQuicChannelBootstrap(channel)
                    .Handler(clientQuicChannelHandler)
                    .StreamHandler(new ChannelInboundHandlerAdapter())
                    .RemoteAddress(address)
                    .ConnectAsync(cancellationToken);
            await serverActive.Active.WaitAsync(cancellationToken);
            await quicChannel.CloseAsync(cancellationToken);
            await quicChannel.CloseFuture.WaitAsync(cancellationToken);
            Assert.True(quicChannel.CloseFuture.IsCompletedSuccessfully);
            clientQuicChannelHandler.AssertState();
        }
        finally
        {
            await server.CloseAsync(CancellationToken.None);
            // Close the parent Datagram channel as well.
            await channel.CloseAsync(CancellationToken.None);

            Shutdown(executor);
        }
    }

    // CLR-only: the server QuicChannel handler of the mutual authentication tests. With TLS 1.3 the client's connect
    // completes before the server has validated the client certificate; System.Net.Quic 10.0.11 on Windows ends the
    // process with an access violation when a server connection closes (the client's close or the listener's dispose)
    // while that validation is still running (reproduced with System.Net.Quic alone, docs/codec-native-quic-porting.md,
    // Known problems), so the test waits for the server's channelActive (the accepted connection) before it closes.
    private sealed class ServerActiveHandler : ChannelInboundHandlerAdapter
    {
        private readonly TaskCompletionSource active =
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Active => active.Task;

        public override bool IsSharable => true;

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            active.TrySetResult();
            ctx.FireChannelActive();
        }
    }

    private enum MutalAuthTestMode
    {
        REQUIRED,
        OPTIONAL_CERT,
        OPTIONAL_NO_KEYMANAGER,
        OPTIONAL_NO_KEY_IN_KEYMANAGER
    }

    [Theory(Timeout = TIMEOUT)]
    [MemberData(nameof(NewSslTaskExecutors), DisableDiscoveryEnumeration = true)]
    public async Task TestConnectionCloseFrameDeliveredWhenParentClosedImmediately(IExecutor executor)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        int numConnections = 10;
        int streamsPerConnection = 50;

        CountdownEvent serverStreamsLatch = new CountdownEvent(numConnections * streamsPerConnection);
        List<IQuicChannel> serverConnections = new List<IQuicChannel>(numConnections);

        IChannel server = await QuicTestUtils.NewServer(executor,
                new ConnectionCollectingHandler(serverConnections), new StreamCountingHandler(serverStreamsLatch));

        server.Pipeline.AddFirst(new DelayingOutboundHandler());
        IPEndPoint address = (IPEndPoint) server.LocalAddress;
        IChannel channel = await QuicTestUtils.NewClient(executor);

        CountdownEvent clientConnectionsLatch = new CountdownEvent(numConnections);

        try
        {
            for (int i = 0; i < numConnections; i++)
            {
                QuicChannelBootstrap quicChannelBootstrap = QuicTestUtils.NewQuicChannelBootstrap(channel);
                quicChannelBootstrap.Handler(new CloseEventCountingHandler(clientConnectionsLatch));
                IQuicChannel quicChannel = await quicChannelBootstrap.StreamHandler(new ChannelInboundHandlerAdapter())
                                                              .RemoteAddress(address)
                                                              .ConnectAsync(cancellationToken);

                for (int j = 0; j < streamsPerConnection; j++)
                {
                    IQuicStreamChannel stream =
                            await quicChannel.CreateStreamAsync(QuicStreamType.BIDIRECTIONAL,
                                    new ChannelInboundHandlerAdapter(), cancellationToken);
                    await stream.WriteAndFlushAsync(Unpooled.DirectBuffer().WriteZero(1), cancellationToken);
                }
            }

            Assert.True(serverStreamsLatch.Wait(TimeSpan.FromSeconds(5), cancellationToken),
                    "Server did not receive all streams");

            lock (serverConnections)
            {
                Assert.Equal(numConnections, serverConnections.Count);
            }

            int closedConnections = 0;
            server.EventLoop.Execute(() =>
            {
                for (int i = 0; i < numConnections; i++)
                {
                    IQuicChannel serverConnection;

                    lock (serverConnections)
                    {
                        serverConnection = serverConnections[i];
                    }

                    _ = serverConnection.CloseAsync().ContinueWith(v =>
                    {
                        if (Interlocked.Increment(ref closedConnections) == numConnections)
                        {
                            _ = server.CloseAsync();
                        }
                    }, TaskScheduler.Default);
                }
            });

            Assert.True(clientConnectionsLatch.Wait(TimeSpan.FromSeconds(5), cancellationToken),
                       "Not all clients received QuicConnectionCloseEvent");
        }
        finally
        {
            await server.CloseAsync(CancellationToken.None);
            await channel.CloseAsync(CancellationToken.None);
            Shutdown(executor);
        }
    }

    // The anonymous sharable server QuicChannel handler of testConnectionCloseFrameDeliveredWhenParentClosedImmediately.
    private sealed class ConnectionCollectingHandler : ChannelInboundHandlerAdapter
    {
        private readonly List<IQuicChannel> serverConnections;

        internal ConnectionCollectingHandler(List<IQuicChannel> serverConnections)
        {
            this.serverConnections = serverConnections;
        }

        public override bool IsSharable => true;

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            lock (serverConnections)
            {
                serverConnections.Add((IQuicChannel) ctx.Channel);
            }

            ctx.FireChannelActive();
        }
    }

    // The anonymous sharable server stream handler of testConnectionCloseFrameDeliveredWhenParentClosedImmediately.
    private sealed class StreamCountingHandler : ChannelInboundHandlerAdapter
    {
        private readonly CountdownEvent serverStreamsLatch;

        internal StreamCountingHandler(CountdownEvent serverStreamsLatch)
        {
            this.serverStreamsLatch = serverStreamsLatch;
        }

        public override bool IsSharable => true;

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            CountDown(serverStreamsLatch);
            ctx.FireChannelActive();
        }
    }

    // The anonymous ChannelOutboundHandlerAdapter of testConnectionCloseFrameDeliveredWhenParentClosedImmediately. The
    // QuicEndpointChannel carries no packets (msquic writes them), so it has nothing to delay.
    private sealed class DelayingOutboundHandler : ChannelOutboundHandlerAdapter
    {
        // simulate delay for writing and flushing packets so it would race with closing.
        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            TaskCompletionSource promise = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ctx.Executor.Execute(() =>
            {
                Task write = ctx.WriteAsync(msg, cancellationToken);
                _ = write.ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        promise.TrySetResult();
                    }
                    else
                    {
                        promise.TrySetException(t.Exception?.InnerException ?? new OperationCanceledException());
                    }
                }, TaskScheduler.Default);
            });
            return promise.Task;
        }

        public override void Flush(IChannelHandlerContext ctx)
        {
            ctx.Executor.Execute(() => ctx.Flush());
        }
    }

    // The anonymous sharable client handler of testConnectionCloseFrameDeliveredWhenParentClosedImmediately.
    private sealed class CloseEventCountingHandler : ChannelInboundHandlerAdapter
    {
        private readonly CountdownEvent clientConnectionsLatch;

        internal CloseEventCountingHandler(CountdownEvent clientConnectionsLatch)
        {
            this.clientConnectionsLatch = clientConnectionsLatch;
        }

        public override bool IsSharable => true;

        public override void UserEventTriggered(IChannelHandlerContext ctx,
                                                object evt)
        {
            if (evt is QuicConnectionCloseEvent)
            {
                CountDown(clientConnectionsLatch);
            }

            ctx.FireUserEventTriggered(evt);
        }
    }

    private sealed class ChannelStateVerifyHandler : QuicChannelValidationHandler
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            ctx.FireChannelActive();
            Assert.Fail();
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            ctx.FireChannelInactive();
            Assert.Fail();
        }
    }

    private sealed class ChannelActiveVerifyHandler : QuicChannelValidationHandler
    {
        private readonly BlockingCollection<int> states = new BlockingCollection<int>();

        public override void ChannelRegistered(IChannelHandlerContext ctx)
        {
            ctx.FireChannelRegistered();
            states.Add(0);
        }

        public override void ChannelUnregistered(IChannelHandlerContext ctx)
        {
            ctx.FireChannelUnregistered();
            states.Add(3);
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            base.ChannelActive(ctx);
            states.Add(1);
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            ctx.FireChannelInactive();
            states.Add(2);
        }

        internal new void AssertState()
        {
            // Check that we receive the different events in the correct order.
            for (long i = 0; i < 4; i++)
            {
                Assert.True(states.TryTake(out int state, TIMEOUT));
                Assert.Equal(i, (long) state);
            }

            Assert.False(states.TryTake(out _));
            base.AssertState();
        }
    }

    // CLR-only helper: Future.await().cause(), the failure of the completed Task (null when it succeeded).
    private static async Task<Exception> Cause(Task future, CancellationToken cancellationToken)
    {
        try
        {
            await future.WaitAsync(cancellationToken);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            return future.Exception?.InnerException ?? e;
        }
    }

    // CLR-only helper: CountDownLatch.countDown() does nothing once the count is zero, where CountdownEvent.Signal throws.
    private static void CountDown(CountdownEvent latch)
    {
        lock (latch)
        {
            if (latch.CurrentCount > 0)
            {
                latch.Signal();
            }
        }
    }

    // CLR: the original comments of the CLR-inapplicable methods (QLOG, key logging, address validation, connection ID
    // lengths and generators, dropped packets, the idle timeout of a stalled path, token validation, BoringSSL key
    // types, 0-RTT, stream priorities, the ALPN mismatch and missing client certificate events of a server QuicChannel,
    // SNI events, keyless signing, session tickets and reuse, the active connection ID limit), kept here:
    // Some log should have been written at some point.
    // Server closes the stream whenever the client sends a FIN.
    // Have the server drop the few first numDroppedPackets incoming packets.
    // Have the server drop packets once we tell it to do so.
    // Disable token validation
    // Use readInt() so we adjust the readerIndex of the token.
    // Just do the cast as getApplicationProtocol() only exists in SSLEngine itself since Java9+ and
    // we may run on an earlier version
    // Let's spin until the session shows up in the cache. This is needed as this might happen a bit after
    // the connection is already established.
    // See https://commondatastorage.googleapis.com/chromium-boringssl-docs/ssl.h.html#SSL_CTX_sess_set_new_cb
    // Check again in 100ms.
    // This should succeed as we have the transport params cached as part of
    // the session.
    // Let's write some data as part of the client hello.
    // 120 is the ALPN error.
    // See https://datatracker.ietf.org/doc/html/rfc8446#section-6
    // Delegate signing to Java implementation.
    // Depending on the Java version it will pick one or the other.
    // Ensure the session is reused.
    // Let's wait a bit and re-check if the handshake is done.
    // NOOP
}
