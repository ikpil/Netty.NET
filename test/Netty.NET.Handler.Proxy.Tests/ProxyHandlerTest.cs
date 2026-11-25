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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.PkiTesting;
using Netty.NET.Resolver;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Local;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Proxy.Tests;

// CLR: InetSocketAddress.createUnresolved(host, port) is DnsEndPoint and SocketUtils.socketAddress of a literal is
// new IPEndPoint(IPAddress.Parse(...), port) (docs/handler-porting.md, Addresses). The static initializers and the static
// block run in their textual order in the static constructor. @AfterAll is the class fixture StopServersFixture,
// @BeforeEach the constructor and @AfterEach Dispose(). @ParameterizedTest @MethodSource are [Theory] [MemberData];
// TestItem is public because a theory's parameter type must be as accessible as the method. The X509Bundle key pair is
// the certificate's AsymmetricAlgorithm, as in handler's CipherSuiteCanaryTest; InsecureTrustManagerFactory.INSTANCE is
// its RemoteCertificateValidationCallback. Future.await(timeout) is Await(Task, TimeSpan) (true when the Task completed
// in time, whatever its outcome) and future.cause() is Cause(Task). LinkedBlockingQueue is ConcurrentQueue,
// CountDownLatch is CountdownEvent (see CountDown) and java.lang.AssertionError is xUnit's FailException. AssertJ's
// isInstanceOf and JUnit's assertInstanceOf are Assert.IsAssignableFrom; String.valueOf(e) is the Java toString()
// text, Common's ThrowableUtil.ThrowableToString. java.net.ConnectException is the Transport's
// SocketException(SocketError.ConnectionRefused) (src/Netty.NET.Transport/Channels/Local/LocalChannel.cs).
// CharsetUtil.US_ASCII is Encoding.ASCII. The lifecycle methods are private (xUnit1013), assertEquals(1, size()) is
// Assert.Single (xUnit2013) and the channel operations receive TestContext.Current.CancellationToken (xUnit1051).
public class ProxyHandlerTest : IClassFixture<ProxyHandlerTest.StopServersFixture>, IDisposable
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(ProxyHandlerTest));

    private static readonly EndPoint DESTINATION = new DnsEndPoint("destination.com", 42);
    private static readonly EndPoint BAD_DESTINATION = new IPEndPoint(IPAddress.Parse("1.2.3.4"), 5);
    private const string USERNAME = "testUser";
    private const string PASSWORD = "testPassword";
    private const string BAD_USERNAME = "badUser";
    private const string BAD_PASSWORD = "badPassword";

    internal static readonly IEventLoopGroup group;

    internal static readonly SslContext serverSslCtx;
    internal static readonly SslContext clientSslCtx;

    internal static readonly ProxyServer deadHttpProxy;
    internal static readonly ProxyServer interHttpProxy;
    internal static readonly ProxyServer anonHttpProxy;
    internal static readonly ProxyServer httpProxy;

    internal static readonly ProxyServer deadHttpsProxy;
    internal static readonly ProxyServer interHttpsProxy;
    internal static readonly ProxyServer anonHttpsProxy;
    internal static readonly ProxyServer httpsProxy;

    internal static readonly ProxyServer deadSocks4Proxy;
    internal static readonly ProxyServer interSocks4Proxy;
    internal static readonly ProxyServer anonSocks4Proxy;
    internal static readonly ProxyServer socks4Proxy;

    internal static readonly ProxyServer deadSocks5Proxy;
    internal static readonly ProxyServer interSocks5Proxy;
    internal static readonly ProxyServer anonSocks5Proxy;
    internal static readonly ProxyServer socks5Proxy;

    // Define private auth method and token for SOCKS5 private authentication
    internal const byte PRIVATE_AUTH_METHOD = (byte) 0x80; // Custom authentication method (range 0x80-0xFE)
    internal static readonly byte[] PRIVATE_AUTH_TOKEN;
    internal static readonly byte[] BAD_PRIVATE_AUTH_TOKEN;

    // SOCKS5 proxy with private authentication
    internal static readonly ProxyServer socks5PrivateProxy;

    private static readonly ICollection<ProxyServer> allProxies;

    // set to non-zero value in case you need predictable shuffling of test cases
    // look for "Seed used: *" debug message in test logs
    private const long reproducibleSeed = 0L;

    static ProxyHandlerTest()
    {
        group = new MultiThreadIoEventLoopGroup(
                3, new DefaultThreadFactory("proxy", true), NioIoHandler.NewFactory());

        SslContext sctx;
        SslContext cctx;
        try
        {
            X509Bundle cert = new CertificateBuilder()
                    .Subject("cn=localhost")
                    .SetIsCertificateAuthority(true)
                    .BuildSelfSigned();
            sctx = SslContextBuilder.ForServer((AsymmetricAlgorithm) cert.GetKeyPair(), cert.GetCertificatePath())
                    .Build();
            cctx = SslContextBuilder.ForClient().TrustManager(InsecureTrustManagerFactory.INSTANCE).Build();
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(e.Message, e);
        }
        serverSslCtx = sctx;
        clientSslCtx = cctx;

        deadHttpProxy = new HttpProxyServer(false, TestMode.UNRESPONSIVE, null);
        interHttpProxy = new HttpProxyServer(false, TestMode.INTERMEDIARY, null);
        anonHttpProxy = new HttpProxyServer(false, TestMode.TERMINAL, DESTINATION);
        httpProxy =
                new HttpProxyServer(false, TestMode.TERMINAL, DESTINATION, USERNAME, PASSWORD);

        deadHttpsProxy = new HttpProxyServer(true, TestMode.UNRESPONSIVE, null);
        interHttpsProxy = new HttpProxyServer(true, TestMode.INTERMEDIARY, null);
        anonHttpsProxy = new HttpProxyServer(true, TestMode.TERMINAL, DESTINATION);
        httpsProxy =
                new HttpProxyServer(true, TestMode.TERMINAL, DESTINATION, USERNAME, PASSWORD);

        deadSocks4Proxy = new Socks4ProxyServer(false, TestMode.UNRESPONSIVE, null);
        interSocks4Proxy = new Socks4ProxyServer(false, TestMode.INTERMEDIARY, null);
        anonSocks4Proxy = new Socks4ProxyServer(false, TestMode.TERMINAL, DESTINATION);
        socks4Proxy = new Socks4ProxyServer(false, TestMode.TERMINAL, DESTINATION, USERNAME);

        deadSocks5Proxy = new Socks5ProxyServer(false, TestMode.UNRESPONSIVE, null);
        interSocks5Proxy = new Socks5ProxyServer(false, TestMode.INTERMEDIARY, null);
        anonSocks5Proxy = new Socks5ProxyServer(false, TestMode.TERMINAL, DESTINATION);
        socks5Proxy =
                new Socks5ProxyServer(false, TestMode.TERMINAL, DESTINATION, USERNAME, PASSWORD);

        PRIVATE_AUTH_TOKEN = Encoding.ASCII.GetBytes("privateAuthToken123");
        BAD_PRIVATE_AUTH_TOKEN = Encoding.ASCII.GetBytes("wrongAuthToken");

        socks5PrivateProxy =
                new Socks5ProxyServer(false, TestMode.TERMINAL, DESTINATION, PRIVATE_AUTH_METHOD, PRIVATE_AUTH_TOKEN);

        allProxies = new List<ProxyServer>
        {
                deadHttpProxy, interHttpProxy, anonHttpProxy, httpProxy,
                deadHttpsProxy, interHttpsProxy, anonHttpsProxy, httpsProxy,
                deadSocks4Proxy, interSocks4Proxy, anonSocks4Proxy, socks4Proxy,
                deadSocks5Proxy, interSocks5Proxy, anonSocks5Proxy, socks5Proxy, socks5PrivateProxy
        };
    }

    // CLR: Collections.shuffle(list, rnd) swaps, for i from size down to 2, element i - 1 with element rnd.nextInt(i);
    // the same walk draws from System.Random, so a seed reproduces a C# order, not the Java order.
    // System.currentTimeMillis() is DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().
    public static IEnumerable<object[]> TestItems()
    {

        List<TestItem> items = new List<TestItem>
        {

                // HTTP -------------------------------------------------------

                new SuccessTestItem(
                        "Anonymous HTTP proxy: successful connection, AUTO_READ on",
                        DESTINATION,
                        true,
                        new HttpProxyHandler(anonHttpProxy.Address())),

                new SuccessTestItem(
                        "Anonymous HTTP proxy: successful connection, AUTO_READ off",
                        DESTINATION,
                        false,
                        new HttpProxyHandler(anonHttpProxy.Address())),

                new FailureTestItem(
                        "Anonymous HTTP proxy: rejected connection",
                        BAD_DESTINATION, "status: 403",
                        new HttpProxyHandler(anonHttpProxy.Address())),

                new FailureTestItem(
                        "HTTP proxy: rejected anonymous connection",
                        DESTINATION, "status: 401",
                        new HttpProxyHandler(httpProxy.Address())),

                new SuccessTestItem(
                        "HTTP proxy: successful connection, AUTO_READ on",
                        DESTINATION,
                        true,
                        new HttpProxyHandler(httpProxy.Address(), USERNAME, PASSWORD)),

                new SuccessTestItem(
                        "HTTP proxy: successful connection, AUTO_READ off",
                        DESTINATION,
                        false,
                        new HttpProxyHandler(httpProxy.Address(), USERNAME, PASSWORD)),

                new FailureTestItem(
                        "HTTP proxy: rejected connection",
                        BAD_DESTINATION, "status: 403",
                        new HttpProxyHandler(httpProxy.Address(), USERNAME, PASSWORD)),

                new FailureTestItem(
                        "HTTP proxy: authentication failure",
                        DESTINATION, "status: 401",
                        new HttpProxyHandler(httpProxy.Address(), BAD_USERNAME, BAD_PASSWORD)),

                new TimeoutTestItem(
                        "HTTP proxy: timeout",
                        new HttpProxyHandler(deadHttpProxy.Address())),

                // HTTPS ------------------------------------------------------

                new SuccessTestItem(
                        "Anonymous HTTPS proxy: successful connection, AUTO_READ on",
                        DESTINATION,
                        true,
                        clientSslCtx.NewHandler(PooledByteBufAllocator.Default),
                        new HttpProxyHandler(anonHttpsProxy.Address())),

                new SuccessTestItem(
                        "Anonymous HTTPS proxy: successful connection, AUTO_READ off",
                        DESTINATION,
                        false,
                        clientSslCtx.NewHandler(PooledByteBufAllocator.Default),
                        new HttpProxyHandler(anonHttpsProxy.Address())),

                new FailureTestItem(
                        "Anonymous HTTPS proxy: rejected connection",
                        BAD_DESTINATION, "status: 403",
                        clientSslCtx.NewHandler(PooledByteBufAllocator.Default),
                        new HttpProxyHandler(anonHttpsProxy.Address())),

                new FailureTestItem(
                        "HTTPS proxy: rejected anonymous connection",
                        DESTINATION, "status: 401",
                        clientSslCtx.NewHandler(PooledByteBufAllocator.Default),
                        new HttpProxyHandler(httpsProxy.Address())),

                new SuccessTestItem(
                        "HTTPS proxy: successful connection, AUTO_READ on",
                        DESTINATION,
                        true,
                        clientSslCtx.NewHandler(PooledByteBufAllocator.Default),
                        new HttpProxyHandler(httpsProxy.Address(), USERNAME, PASSWORD)),

                new SuccessTestItem(
                        "HTTPS proxy: successful connection, AUTO_READ off",
                        DESTINATION,
                        false,
                        clientSslCtx.NewHandler(PooledByteBufAllocator.Default),
                        new HttpProxyHandler(httpsProxy.Address(), USERNAME, PASSWORD)),

                new FailureTestItem(
                        "HTTPS proxy: rejected connection",
                        BAD_DESTINATION, "status: 403",
                        clientSslCtx.NewHandler(PooledByteBufAllocator.Default),
                        new HttpProxyHandler(httpsProxy.Address(), USERNAME, PASSWORD)),

                new FailureTestItem(
                        "HTTPS proxy: authentication failure",
                        DESTINATION, "status: 401",
                        clientSslCtx.NewHandler(PooledByteBufAllocator.Default),
                        new HttpProxyHandler(httpsProxy.Address(), BAD_USERNAME, BAD_PASSWORD)),

                new TimeoutTestItem(
                        "HTTPS proxy: timeout",
                        clientSslCtx.NewHandler(PooledByteBufAllocator.Default),
                        new HttpProxyHandler(deadHttpsProxy.Address())),

                // SOCKS4 -----------------------------------------------------

                new SuccessTestItem(
                        "Anonymous SOCKS4: successful connection, AUTO_READ on",
                        DESTINATION,
                        true,
                        new Socks4ProxyHandler(anonSocks4Proxy.Address())),

                new SuccessTestItem(
                        "Anonymous SOCKS4: successful connection, AUTO_READ off",
                        DESTINATION,
                        false,
                        new Socks4ProxyHandler(anonSocks4Proxy.Address())),

                new FailureTestItem(
                        "Anonymous SOCKS4: rejected connection",
                        BAD_DESTINATION, "status: REJECTED_OR_FAILED",
                        new Socks4ProxyHandler(anonSocks4Proxy.Address())),

                new FailureTestItem(
                        "SOCKS4: rejected anonymous connection",
                        DESTINATION, "status: IDENTD_AUTH_FAILURE",
                        new Socks4ProxyHandler(socks4Proxy.Address())),

                new SuccessTestItem(
                        "SOCKS4: successful connection, AUTO_READ on",
                        DESTINATION,
                        true,
                        new Socks4ProxyHandler(socks4Proxy.Address(), USERNAME)),

                new SuccessTestItem(
                        "SOCKS4: successful connection, AUTO_READ off",
                        DESTINATION,
                        false,
                        new Socks4ProxyHandler(socks4Proxy.Address(), USERNAME)),

                new FailureTestItem(
                        "SOCKS4: rejected connection",
                        BAD_DESTINATION, "status: REJECTED_OR_FAILED",
                        new Socks4ProxyHandler(socks4Proxy.Address(), USERNAME)),

                new FailureTestItem(
                        "SOCKS4: authentication failure",
                        DESTINATION, "status: IDENTD_AUTH_FAILURE",
                        new Socks4ProxyHandler(socks4Proxy.Address(), BAD_USERNAME)),

                new TimeoutTestItem(
                        "SOCKS4: timeout",
                        new Socks4ProxyHandler(deadSocks4Proxy.Address())),

                // SOCKS5 -----------------------------------------------------

                new SuccessTestItem(
                        "Anonymous SOCKS5: successful connection, AUTO_READ on",
                        DESTINATION,
                        true,
                        new Socks5ProxyHandler(anonSocks5Proxy.Address())),

                new SuccessTestItem(
                        "Anonymous SOCKS5: successful connection, AUTO_READ off",
                        DESTINATION,
                        false,
                        new Socks5ProxyHandler(anonSocks5Proxy.Address())),

                new FailureTestItem(
                        "Anonymous SOCKS5: rejected connection",
                        BAD_DESTINATION, "status: FORBIDDEN",
                        new Socks5ProxyHandler(anonSocks5Proxy.Address())),

                new FailureTestItem(
                        "SOCKS5: rejected anonymous connection",
                        DESTINATION, "unexpected authMethod: PASSWORD",
                        new Socks5ProxyHandler(socks5Proxy.Address())),

                new SuccessTestItem(
                        "SOCKS5: successful connection to anonymous server, AUTO_READ on",
                        DESTINATION,
                        true,
                        new Socks5ProxyHandler(anonSocks5Proxy.Address(), USERNAME, PASSWORD)),

                new SuccessTestItem(
                        "SOCKS5: successful connection, AUTO_READ on",
                        DESTINATION,
                        true,
                        new Socks5ProxyHandler(socks5Proxy.Address(), USERNAME, PASSWORD)),

                new SuccessTestItem(
                        "SOCKS5: successful connection, AUTO_READ off",
                        DESTINATION,
                        false,
                        new Socks5ProxyHandler(socks5Proxy.Address(), USERNAME, PASSWORD)),

                new FailureTestItem(
                        "SOCKS5: rejected connection",
                        BAD_DESTINATION, "status: FORBIDDEN",
                        new Socks5ProxyHandler(socks5Proxy.Address(), USERNAME, PASSWORD)),

                new FailureTestItem(
                        "SOCKS5: authentication failure",
                        DESTINATION, "authStatus: FAILURE",
                        new Socks5ProxyHandler(socks5Proxy.Address(), BAD_USERNAME, BAD_PASSWORD)),

                new TimeoutTestItem(
                        "SOCKS5: timeout",
                        new Socks5ProxyHandler(deadSocks5Proxy.Address())),

                // SOCKS5 Private Authentication ---------------------------
                new SuccessTestItem(
                    "SOCKS5 Private Auth: successful connection, AUTO_READ on",
                    DESTINATION,
                    true,
                    new Socks5ProxyHandler(socks5PrivateProxy.Address(), PRIVATE_AUTH_METHOD, PRIVATE_AUTH_TOKEN,
                        null)),

                new SuccessTestItem(
                    "SOCKS5: successful connection to anonymous server, AUTO_READ on",
                    DESTINATION,
                    true,
                    new Socks5ProxyHandler(anonSocks5Proxy.Address(), USERNAME, PASSWORD)),

                new SuccessTestItem(
                    "SOCKS5 Private Auth: successful connection, AUTO_READ off",
                    DESTINATION,
                    false,
                    new Socks5ProxyHandler(socks5PrivateProxy.Address(), PRIVATE_AUTH_METHOD, PRIVATE_AUTH_TOKEN,
                        null)),

                new FailureTestItem(
                    "SOCKS5 Private Auth: rejected connection",
                    BAD_DESTINATION, "status: FORBIDDEN",
                    new Socks5ProxyHandler(socks5PrivateProxy.Address(), PRIVATE_AUTH_METHOD, PRIVATE_AUTH_TOKEN,
                        null)),

                new FailureTestItem(
                    "SOCKS5 Private Auth: authentication failure",
                    DESTINATION, "privateAuthStatus: FAILURE",
                    new Socks5ProxyHandler(socks5PrivateProxy.Address(), PRIVATE_AUTH_METHOD, BAD_PRIVATE_AUTH_TOKEN,
                        null)),

                new FailureTestItem(
                    "SOCKS5 Private Auth: rejected anonymous connection",
                    DESTINATION, "unexpected authMethod",
                    new Socks5ProxyHandler(socks5PrivateProxy.Address())),

                // HTTP + HTTPS + SOCKS4 + SOCKS5

                new SuccessTestItem(
                        "Single-chain: successful connection, AUTO_READ on",
                        DESTINATION,
                        true,
                        new Socks5ProxyHandler(interSocks5Proxy.Address()), // SOCKS5
                        new Socks4ProxyHandler(interSocks4Proxy.Address()), // SOCKS4
                        clientSslCtx.NewHandler(PooledByteBufAllocator.Default),
                        new HttpProxyHandler(interHttpsProxy.Address()), // HTTPS
                        new HttpProxyHandler(interHttpProxy.Address()), // HTTP
                        new HttpProxyHandler(anonHttpProxy.Address())),

                new SuccessTestItem(
                        "Single-chain: successful connection, AUTO_READ off",
                        DESTINATION,
                        false,
                        new Socks5ProxyHandler(interSocks5Proxy.Address()), // SOCKS5
                        new Socks4ProxyHandler(interSocks4Proxy.Address()), // SOCKS4
                        clientSslCtx.NewHandler(PooledByteBufAllocator.Default),
                        new HttpProxyHandler(interHttpsProxy.Address()), // HTTPS
                        new HttpProxyHandler(interHttpProxy.Address()), // HTTP
                        new HttpProxyHandler(anonHttpProxy.Address())),

                // (HTTP + HTTPS + SOCKS4 + SOCKS5) * 2

                new SuccessTestItem(
                        "Double-chain: successful connection, AUTO_READ on",
                        DESTINATION,
                        true,
                        new Socks5ProxyHandler(interSocks5Proxy.Address()), // SOCKS5
                        new Socks4ProxyHandler(interSocks4Proxy.Address()), // SOCKS4
                        clientSslCtx.NewHandler(PooledByteBufAllocator.Default),
                        new HttpProxyHandler(interHttpsProxy.Address()), // HTTPS
                        new HttpProxyHandler(interHttpProxy.Address()), // HTTP
                        new Socks5ProxyHandler(interSocks5Proxy.Address()), // SOCKS5
                        new Socks4ProxyHandler(interSocks4Proxy.Address()), // SOCKS4
                        clientSslCtx.NewHandler(PooledByteBufAllocator.Default),
                        new HttpProxyHandler(interHttpsProxy.Address()), // HTTPS
                        new HttpProxyHandler(interHttpProxy.Address()), // HTTP
                        new HttpProxyHandler(anonHttpProxy.Address())),

                new SuccessTestItem(
                        "Double-chain: successful connection, AUTO_READ off",
                        DESTINATION,
                        false,
                        new Socks5ProxyHandler(interSocks5Proxy.Address()), // SOCKS5
                        new Socks4ProxyHandler(interSocks4Proxy.Address()), // SOCKS4
                        clientSslCtx.NewHandler(PooledByteBufAllocator.Default),
                        new HttpProxyHandler(interHttpsProxy.Address()), // HTTPS
                        new HttpProxyHandler(interHttpProxy.Address()), // HTTP
                        new Socks5ProxyHandler(interSocks5Proxy.Address()), // SOCKS5
                        new Socks4ProxyHandler(interSocks4Proxy.Address()), // SOCKS4
                        clientSslCtx.NewHandler(PooledByteBufAllocator.Default),
                        new HttpProxyHandler(interHttpsProxy.Address()), // HTTPS
                        new HttpProxyHandler(interHttpProxy.Address()), // HTTP
                        new HttpProxyHandler(anonHttpProxy.Address()))
        };

        // Convert the test items to the list of constructor parameters.
        List<object[]> @params = new List<object[]>(items.Count);
        foreach (object i in items)
        {
            @params.Add(new object[] { i });
        }

        // Randomize the execution order to increase the possibility of exposing failure dependencies.
        long seed = (reproducibleSeed == 0L) ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : reproducibleSeed;
        logger.Debug("Seed used: {}\n", seed);
        Random rnd = new Random(unchecked((int) (seed ^ (seed >> 32))));
        for (int i = @params.Count; i > 1; i--)
        {
            int j = rnd.Next(i);
            (@params[i - 1], @params[j]) = (@params[j], @params[i - 1]);
        }

        return @params;
    }

    private static void StopServers()
    {
        foreach (ProxyServer p in allProxies)
        {
            p.Stop();
        }
    }

    public sealed class StopServersFixture : IDisposable
    {
        public void Dispose()
        {
            StopServers();
        }
    }

    public ProxyHandlerTest()
    {
        ClearServerExceptions();
    }

    private void ClearServerExceptions()
    {
        foreach (ProxyServer p in allProxies)
        {
            p.ClearExceptions();
        }
    }

    [Theory]
    [MemberData(nameof(TestItems))]
    public async Task Test(TestItem testItem)
    {
        await testItem.Test();
    }

    public static IEnumerable<object[]> ProxyTypes()
    {
        return new List<object[]> { new object[] { "http" }, new object[] { "socks4" }, new object[] { "socks5" } };
    }

    private static ProxyHandler NewProxyHandler(string type, EndPoint proxyAddress)
    {
        switch (type)
        {
            case "http":
                return new HttpProxyHandler(proxyAddress);
            case "socks4":
                return new Socks4ProxyHandler(proxyAddress);
            case "socks5":
                return new Socks5ProxyHandler(proxyAddress);
            default:
                throw new ArgumentException(type);
        }
    }

    // CLR: register().sync().channel() awaits the registration Task<IChannel>; shutdownGracefully(0, 0, SECONDS) is
    // ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero), whose Task the source does not wait for either.
    [Theory]
    [MemberData(nameof(ProxyTypes))]
    public async Task TestPendingWritesFailedWhenConnectionToProxyFails(string type)
    {
        IEventLoopGroup localGroup = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());
        try
        {
            // Nothing is bound to this address, so the connection to the proxy server fails and the channel is
            // closed without ever becoming active.
            ProxyHandler handler = NewProxyHandler(type, new LocalAddress("unbound-proxy-" + type));
            IChannel ch = await new Bootstrap().Group(localGroup).Channel<LocalChannel>().Handler(handler)
                    .RegisterAsync(TestContext.Current.CancellationToken);

            // Written before the connection is established, so the handler queues it until the proxy handshake
            // is done.
            ByteBuf msg = Unpooled.CopiedBuffer("hello", Encoding.ASCII);
            Task writeFuture = ch.WriteAndFlushAsync(msg, TestContext.Current.CancellationToken);
            Task connectFuture = ch.ConnectAsync(DESTINATION, null, TestContext.Current.CancellationToken);

            Assert.True(await Await(connectFuture, TimeSpan.FromSeconds(5)));
            SocketException connectCause = Assert.IsAssignableFrom<SocketException>(Cause(connectFuture));
            Assert.Equal(SocketError.ConnectionRefused, connectCause.SocketErrorCode);
            Assert.True(await Await(writeFuture, TimeSpan.FromSeconds(5)));
            Assert.IsAssignableFrom<ProxyConnectException>(Cause(writeFuture));
            Assert.Equal(0, msg.RefCnt);
            Assert.True(await Await(handler.ConnectFuture(), TimeSpan.FromSeconds(5)));
            Assert.IsAssignableFrom<ProxyConnectException>(Cause(handler.ConnectFuture()));
        }
        finally
        {
            _ = localGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
        }
    }

    // CLR: the anonymous EmbeddedChannel subclass is the nested InactiveEmbeddedChannel.
    [Theory]
    [MemberData(nameof(ProxyTypes))]
    public void TestPendingWritesFailedWhenHandlerRemoved(string type)
    {
        ProxyHandler handler = NewProxyHandler(type, new IPEndPoint(NetUtil.LOCALHOST, 8080));
        EmbeddedChannel ch = new InactiveEmbeddedChannel(handler);
        ByteBuf msg = Unpooled.CopiedBuffer("hello", Encoding.ASCII);
        Task writeFuture = ch.WriteAndFlushAsync(msg, TestContext.Current.CancellationToken);
        Assert.False(writeFuture.IsCompleted);

        ch.Pipeline.Remove(handler);

        Assert.True(writeFuture.IsCompleted);
        Assert.IsAssignableFrom<ProxyConnectException>(Cause(writeFuture));
        Assert.Equal(0, msg.RefCnt);
        Assert.True(handler.ConnectFuture().IsCompleted);
        Assert.IsAssignableFrom<ProxyConnectException>(Cause(handler.ConnectFuture()));
        Assert.False(ch.FinishAndReleaseAll());
    }

    private sealed class InactiveEmbeddedChannel : EmbeddedChannel
    {
        internal InactiveEmbeddedChannel(params IChannelHandler[] handlers) : base(handlers)
        {
        }

        public override bool IsActive
        {
            get
            {
                // Simulate that the connection to the proxy server is not established yet.
                return false;
            }
        }
    }

    public void Dispose()
    {
        CheckServerExceptions();
    }

    private void CheckServerExceptions()
    {
        foreach (ProxyServer p in allProxies)
        {
            p.CheckExceptions();
        }
    }

    // CLR: Future.await(timeout, unit): true when the future completed in time, whatever its outcome (as in handler's
    // SSLEngineTest).
    private static async Task<bool> Await(Task future, TimeSpan timeout)
    {
        return await Task.WhenAny(future, Task.Delay(timeout)) == future;
    }

    // CLR: future.cause(): the exception of a faulted Task (the first inner exception of its AggregateException), a
    // TaskCanceledException for a canceled Task (Java's CancellationException) and null otherwise.
    private static Exception Cause(Task future)
    {
        if (future.IsFaulted)
        {
            return future.Exception.InnerException;
        }
        if (future.IsCanceled)
        {
            return new TaskCanceledException(future);
        }
        return null;
    }

    // CLR: CountDownLatch.countDown() does nothing once the count is zero, while CountdownEvent.Signal() throws
    // InvalidOperationException when "The current instance is already set"
    // (https://learn.microsoft.com/dotnet/api/system.threading.countdownevent.signal), as in handler's
    // FlowControlHandlerTest; the latch is counted down on the channel's event loop.
    private static void CountDown(CountdownEvent latch)
    {
        if (latch.CurrentCount > 0)
        {
            latch.Signal();
        }
    }

    private sealed class SuccessTestHandler : SimpleChannelInboundHandler<object>
    {

        internal readonly ConcurrentQueue<string> received = new ConcurrentQueue<string>();
        internal readonly ConcurrentQueue<Exception> exceptions = new ConcurrentQueue<Exception>();
        internal volatile int eventCount;

        private static void ReadIfNeeded(IChannelHandlerContext ctx)
        {
            if (!ctx.Channel.Config.IsAutoRead())
            {
                ctx.Read();
            }
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            _ = ctx.WriteAndFlushAsync(Unpooled.CopiedBuffer("A\n", Encoding.ASCII));
            ReadIfNeeded(ctx);
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is ProxyConnectionEvent)
            {
                eventCount ++;

                if (eventCount == 1)
                {
                    // Note that ProxyConnectionEvent can be triggered multiple times when there are multiple
                    // ProxyHandlers in the pipeline.  Therefore, we send the 'B' message only on the first event.
                    _ = ctx.WriteAndFlushAsync(Unpooled.CopiedBuffer("B\n", Encoding.ASCII));
                }
                ReadIfNeeded(ctx);
            }
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
        {
            string str = ((ByteBuf) msg).ToString(Encoding.ASCII);
            received.Enqueue(str);
            if ("2".Equals(str))
            {
                _ = ctx.WriteAndFlushAsync(Unpooled.CopiedBuffer("C\n", Encoding.ASCII));
            }
            ReadIfNeeded(ctx);
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            exceptions.Enqueue(cause);
            _ = ctx.CloseAsync();
        }
    }

    // CLR: the write listener runs on the channel's event loop through Common's EventExecutorTaskScheduler
    // (DefaultPromise.notifyListeners).
    private sealed class FailureTestHandler : SimpleChannelInboundHandler<object>
    {

        internal readonly ConcurrentQueue<Exception> exceptions = new ConcurrentQueue<Exception>();

        /**
         * A latch that counts down when:
         * - a pending write attempt in {@link #channelActive(ChannelHandlerContext)} finishes, or
         * - the channel is closed.
         * By waiting until the latch goes down to 0, we can make sure all assertion failures related with all write
         * attempts have been recorded.
         */
        internal readonly CountdownEvent latch = new CountdownEvent(2);

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            _ = ctx.WriteAndFlushAsync(Unpooled.CopiedBuffer("A\n", Encoding.ASCII)).ContinueWith(
                    future =>
                    {
                        CountDown(latch);
                        if (!(Cause(future) is ProxyConnectException))
                        {
                            exceptions.Enqueue(Xunit.Sdk.FailException.ForFailure(
                                    "Unexpected failure cause for initial write: " + Cause(future)));
                        }
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                    new EventExecutorTaskScheduler(() => ctx.Executor));
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            CountDown(latch);
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is ProxyConnectionEvent)
            {
                Assert.Fail("Unexpected event: " + evt);
            }
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
        {
            Assert.Fail("Unexpected message: " + msg);
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            exceptions.Enqueue(cause);
            _ = ctx.CloseAsync();
        }
    }

    // CLR: logger.warn("{}: failure", type, f.cause()) passes the cause as the last argument, which the logger prints as
    // the throwable, as SLF4J does.
    public abstract class TestItem
    {
        internal readonly string name;
        internal readonly EndPoint destination;
        internal readonly IChannelHandler[] clientHandlers;

        private protected TestItem(string name, EndPoint destination, params IChannelHandler[] clientHandlers)
        {
            this.name = name;
            this.destination = destination;
            this.clientHandlers = clientHandlers;
        }

        internal abstract Task Test();

        private protected void AssertProxyHandlers(bool success)
        {
            foreach (IChannelHandler h in clientHandlers)
            {
                if (h is ProxyHandler)
                {
                    ProxyHandler ph = (ProxyHandler) h;
                    string type = StringUtil.SimpleClassName(ph);
                    Task<IChannel> f = ph.ConnectFuture();
                    if (!f.IsCompleted)
                    {
                        logger.Warn("{}: not done", type);
                    }
                    else if (f.IsCompletedSuccessfully)
                    {
                        if (success)
                        {
                            logger.Debug("{}: success", type);
                        }
                        else
                        {
                            logger.Warn("{}: success", type);
                        }
                    }
                    else
                    {
                        if (success)
                        {
                            logger.Warn("{}: failure", type, Cause(f));
                        }
                        else
                        {
                            logger.Debug("{}: failure", type, Cause(f));
                        }
                    }
                }
            }

            foreach (IChannelHandler h in clientHandlers)
            {
                if (h is ProxyHandler)
                {
                    ProxyHandler ph = (ProxyHandler) h;
                    Assert.True(ph.ConnectFuture().IsCompleted);
                    Assert.Equal(ph.ConnectFuture().IsCompletedSuccessfully, success);
                }
            }
        }

        public override string ToString()
        {
            return name;
        }
    }

    // CLR: b.connect(destination).channel() returns the channel of the pending ChannelFuture at once, whose
    // closeFuture() is then awaited whatever the connect outcome. Bootstrap.ConnectAsync yields the channel only from a
    // successful Task<IChannel>, but it calls the channel factory synchronously before its first await
    // (src/Netty.NET.Transport/Bootstrapping/Bootstrap.cs), so channel(NioSocketChannel.class) is a factory that
    // captures the new channel, as Transport's SocketCloseForciblySourceTest does. The anonymous ChannelInitializer is the
    // Transport's ActionChannelInitializer.
    private sealed class SuccessTestItem : TestItem
    {

        private readonly int expectedEventCount;
        // Probably we need to be more flexible here and as for the configuration map,
        // not a single key. But as far as it works for now, I'm leaving the impl.
        // as is, in case we need to cover more cases (like, AUTO_CLOSE, TCP_NODELAY etc)
        // feel free to replace this boolean with either config or method to setup bootstrap
        private readonly bool autoRead;

        internal SuccessTestItem(string name,
                        EndPoint destination,
                        bool autoRead,
                        params IChannelHandler[] clientHandlers) : base(name, destination, clientHandlers)
        {
            int expectedEventCount = 0;
            foreach (IChannelHandler h in clientHandlers)
            {
                if (h is ProxyHandler)
                {
                    expectedEventCount++;
                }
            }

            this.expectedEventCount = expectedEventCount;
            this.autoRead = autoRead;
        }

        internal override async Task Test()
        {
            SuccessTestHandler testHandler = new SuccessTestHandler();
            IChannel channel = null;
            Bootstrap b = new Bootstrap();
            b.Group(group);
            b.ChannelFactory(() => channel = new NioSocketChannel());
            b.Option(ChannelOption.AUTO_READ, this.autoRead);
            b.Resolver(NoopAddressResolverGroup.INSTANCE);
            b.Handler(new ActionChannelInitializer<ISocketChannel>(ch =>
            {
                IChannelPipeline p = ch.Pipeline;
                p.AddLast(clientHandlers);
                p.AddLast(new LineBasedFrameDecoder(64));
                p.AddLast(testHandler);
            }));

            _ = b.ConnectAsync(destination);
            bool finished = await Await(channel.CloseFuture, TimeSpan.FromSeconds(10));

            logger.Debug("Received messages: {}", testHandler.received);

            if (testHandler.exceptions.IsEmpty)
            {
                logger.Debug("No recorded exceptions on the client side.");
            }
            else
            {
                foreach (Exception t in testHandler.exceptions)
                {
                    logger.Debug("Recorded exception on the client side: {}", t);
                }
            }

            AssertProxyHandlers(true);

            Assert.Equal(new object[] { "0", "1", "2", "3" }, testHandler.received.ToArray());
            Assert.Equal(EmptyArrays.EMPTY_OBJECTS, testHandler.exceptions.ToArray());
            Assert.Equal(expectedEventCount, testHandler.eventCount);
            Assert.True(finished);
        }
    }

    // CLR: see SuccessTestItem for connect(...).channel(); latch.await(10, SECONDS) is CountdownEvent.Wait(TimeSpan),
    // which also returns false when the time elapses.
    private sealed class FailureTestItem : TestItem
    {

        private readonly string expectedMessage;

        internal FailureTestItem(
                string name, EndPoint destination, string expectedMessage, params IChannelHandler[] clientHandlers)
            : base(name, destination, clientHandlers)
        {
            this.expectedMessage = expectedMessage;
        }

        internal override async Task Test()
        {
            FailureTestHandler testHandler = new FailureTestHandler();
            IChannel channel = null;
            Bootstrap b = new Bootstrap();
            b.Group(group);
            b.ChannelFactory(() => channel = new NioSocketChannel());
            b.Resolver(NoopAddressResolverGroup.INSTANCE);
            b.Handler(new ActionChannelInitializer<ISocketChannel>(ch =>
            {
                IChannelPipeline p = ch.Pipeline;
                p.AddLast(clientHandlers);
                p.AddLast(new LineBasedFrameDecoder(64));
                p.AddLast(testHandler);
            }));

            _ = b.ConnectAsync(destination);
            bool finished = await Await(channel.CloseFuture, TimeSpan.FromSeconds(10));
            finished &= testHandler.latch.Wait(TimeSpan.FromSeconds(10));

            logger.Debug("Recorded exceptions: {}", testHandler.exceptions);

            AssertProxyHandlers(false);

            Assert.Single(testHandler.exceptions);
            testHandler.exceptions.TryDequeue(out Exception e);
            Assert.IsAssignableFrom<ProxyConnectException>(e);
            Assert.Contains(expectedMessage, ThrowableUtil.ThrowableToString(e));
            Assert.True(finished);
        }
    }

    // CLR: see SuccessTestItem and FailureTestItem; the waits are TimeSpan.FromMilliseconds(TIMEOUT * 2).
    private sealed class TimeoutTestItem : TestItem
    {

        internal TimeoutTestItem(string name, params IChannelHandler[] clientHandlers) : base(name, null, clientHandlers)
        {
        }

        internal override async Task Test()
        {
            const long TIMEOUT = 2000;
            foreach (IChannelHandler h in clientHandlers)
            {
                if (h is ProxyHandler)
                {
                    ((ProxyHandler) h).SetConnectTimeoutMillis(TIMEOUT);
                }
            }

            FailureTestHandler testHandler = new FailureTestHandler();
            IChannel channel = null;
            Bootstrap b = new Bootstrap();
            b.Group(group);
            b.ChannelFactory(() => channel = new NioSocketChannel());
            b.Resolver(NoopAddressResolverGroup.INSTANCE);
            b.Handler(new ActionChannelInitializer<ISocketChannel>(ch =>
            {
                IChannelPipeline p = ch.Pipeline;
                p.AddLast(clientHandlers);
                p.AddLast(new LineBasedFrameDecoder(64));
                p.AddLast(testHandler);
            }));

            _ = b.ConnectAsync(DESTINATION);
            Task cf = channel.CloseFuture;
            bool finished = await Await(cf, TimeSpan.FromMilliseconds(TIMEOUT * 2));
            finished &= testHandler.latch.Wait(TimeSpan.FromMilliseconds(TIMEOUT * 2));

            logger.Debug("Recorded exceptions: {}", testHandler.exceptions);

            AssertProxyHandlers(false);

            Assert.Single(testHandler.exceptions);
            testHandler.exceptions.TryDequeue(out Exception e);
            Assert.IsAssignableFrom<ProxyConnectException>(e);
            Assert.Contains("timeout", ThrowableUtil.ThrowableToString(e));
            Assert.True(finished);
        }
    }
}
