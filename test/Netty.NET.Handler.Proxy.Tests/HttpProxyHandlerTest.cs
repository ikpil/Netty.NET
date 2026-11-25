/*
 * Copyright 2016 The Netty Project
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
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Resolver;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Local;
using static Netty.NET.Handler.Proxy.HttpProxyHandler;
using HttpVersion = Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Handler.Proxy.Tests;

// CLR: InetSocketAddress.createUnresolved(host, port) is DnsEndPoint; new InetSocketAddress(InetAddress.getByName(host),
// port) and new InetSocketAddress(host, port) resolve host with Resolver's DefaultNameResolver, the port of
// InetAddress.getByName (JDK literal rules, java.net.preferIPv6Addresses order), as Transport's AbstractBootstrap does
// for SocketUtils.socketAddress (docs/handler-porting.md, Addresses). A resolved InetSocketAddress keeps the host name it
// was created from for getHostString(), while IPEndPoint keeps only the IPAddress and NetUtil.GetHostname returns the
// numeric address (docs/codec-http-porting.md, HttpUtilTest.testFormatHostnameForHttpFromResolvedAddressWithHostname):
// testHostname, testHostHeaderWithHttpDefaultPort and testHostHeaderWithHttpsDefaultPort therefore expect the numeric
// text of the resolved localhost (HostText) where the source expects "localhost", and still check the CONNECT URL and
// the Host header choice for that address. @ParameterizedTest @ValueSource(booleans) is [Theory] [InlineData]. Mockito
// is Moq: the ChannelPromise of connect(ctx, remote, local, promise) is the CancellationToken the handler passes on
// (and the Task it returns), so same(promise)/verify(...).connect(..., promise) match that token;
// verifyNoMoreInteractions(promise) on the new mock checks no interaction and has no counterpart without a promise
// object. new DefaultHttpHeaders(false) is DefaultHttpHeadersFactory.HeadersFactory().WithValidation(false)
// .NewHeaders(), as in codec-http's HttpHeadersTest (the @Deprecated constructor is not ported). The protected
// newInitialMessage is reached through the internal InvokeNewInitialMessage. The anonymous classes are nested classes;
// AtomicBoolean/AtomicReference are Common's. IllegalArgumentException is ArgumentException, assertInstanceOf is
// Assert.IsAssignableFrom and AssertJ's isSameAs is Assert.Same. CharsetUtil.US_ASCII is Encoding.ASCII. The channel
// operations receive TestContext.Current.CancellationToken (xUnit1051).
public class HttpProxyHandlerTest
{
    [Fact]
    public async Task TestHostname()
    {
        IPEndPoint socketAddress = new IPEndPoint(await AddressByName("localhost"), 8080);
        TestInitialMessage(
                socketAddress,
                HostText(socketAddress) + ":8080",
                HostText(socketAddress) + ":8080",
                null,
                true);
    }

    [Fact]
    public void TestHostnameUnresolved()
    {
        EndPoint socketAddress = new DnsEndPoint("localhost", 8080);
        TestInitialMessage(
                socketAddress,
                "localhost:8080",
                "localhost:8080",
                null,
                true);
    }

    [Fact]
    public async Task TestHostHeaderWithHttpDefaultPort()
    {
        IPEndPoint socketAddress = new IPEndPoint(await AddressByName("localhost"), 80);
        TestInitialMessage(socketAddress,
                HostText(socketAddress) + ":80",
                HostText(socketAddress) + ":80", null,
                false);
    }

    [Fact]
    public void TestHostHeaderWithHttpDefaultPortIgnored()
    {
        EndPoint socketAddress = new DnsEndPoint("localhost", 80);
        TestInitialMessage(
                socketAddress,
                "localhost:80",
                "localhost",
                null,
                true);
    }

    [Fact]
    public async Task TestHostHeaderWithHttpsDefaultPort()
    {
        IPEndPoint socketAddress = new IPEndPoint(await AddressByName("localhost"), 443);
        TestInitialMessage(
                socketAddress,
                HostText(socketAddress) + ":443",
                HostText(socketAddress) + ":443",
                null,
                false);
    }

    [Fact]
    public void TestHostHeaderWithHttpsDefaultPortIgnored()
    {
        EndPoint socketAddress = new DnsEndPoint("localhost", 443);
        TestInitialMessage(
                socketAddress,
                "localhost:443",
                "localhost",
                null,
                true);
    }

    [Fact]
    public async Task TestIpv6()
    {
        EndPoint socketAddress = new IPEndPoint(await AddressByName("::1"), 8080);
        TestInitialMessage(
                socketAddress,
                "[::1]:8080",
                "[::1]:8080",
                null,
                true);
    }

    [Fact]
    public void TestIpv6Unresolved()
    {
        EndPoint socketAddress = new DnsEndPoint("::1", 8080);
        TestInitialMessage(
                socketAddress,
                "[::1]:8080",
                "[::1]:8080",
                null,
                true);
    }

    [Fact]
    public async Task TestIpv4()
    {
        EndPoint socketAddress = new IPEndPoint(await AddressByName("10.0.0.1"), 8080);
        TestInitialMessage(socketAddress,
                "10.0.0.1:8080",
                "10.0.0.1:8080",
                null,
                true);
    }

    [Fact]
    public void TestIpv4Unresolved()
    {
        EndPoint socketAddress = new DnsEndPoint("10.0.0.1", 8080);
        TestInitialMessage(
                socketAddress,
                "10.0.0.1:8080",
                "10.0.0.1:8080",
                null,
                true);
    }

    [Fact]
    public void TestCustomHeaders()
    {
        EndPoint socketAddress = new DnsEndPoint("10.0.0.1", 8080);
        TestInitialMessage(
                socketAddress,
                "10.0.0.1:8080",
                "10.0.0.1:8080",
                new DefaultHttpHeaders()
                        .Add("CUSTOM_HEADER", "CUSTOM_VALUE1")
                        .Add("CUSTOM_HEADER", "CUSTOM_VALUE2"),
                true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestInvalidHeaders(bool validation)
    {
        EndPoint socketAddress = new DnsEndPoint("10.0.0.1", 8080);
        try
        {
            TestInitialMessage(
                    socketAddress,
                    "10.0.0.1:8080",
                    "10.0.0.1:8080",
                    DefaultHttpHeadersFactory.HeadersFactory().WithValidation(false).NewHeaders()
                            .Add("CUSTOM_HEADER", "CUSTOM_VALUE1\r\nInvalid: true")
                            .Add("CUSTOM_HEADER", "CUSTOM_VALUE2"),
                    true, validation);
            if (validation)
            {
                Assert.Fail("Validation should have failed for the provided headers");
            }
        }
        catch (ArgumentException)
        {
            if (!validation)
            {
                throw;
            }
        }
    }

    // CLR: sync() awaits the bind/connect/close Tasks; the anonymous ChannelInitializers are the Transport's
    // ActionChannelInitializer and the anonymous ChannelInboundHandlerAdapter is ExceptionRecorder.
    [Fact]
    public async Task TestExceptionDuringConnect()
    {
        IEventLoopGroup group = null;
        IChannel serverChannel = null;
        IChannel clientChannel = null;
        try
        {
            group = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());
            LocalAddress addr = new LocalAddress(typeof(HttpProxyHandlerTest));
            AtomicReference<Exception> exception = new AtomicReference<Exception>();
            Task<IChannel> sf =
                new ServerBootstrap().Channel<LocalServerChannel>().Group(group).ChildHandler(
                    new ActionChannelInitializer<IChannel>(ch =>
                    {
                        ch.Pipeline.AddFirst(new HttpResponseEncoder());
                        DefaultFullHttpResponse response = new DefaultFullHttpResponse(
                            HttpVersion.HTTP_1_1,
                            HttpResponseStatus.BAD_GATEWAY);
                        response.Headers().Add("name", "value");
                        response.Headers().Add(HttpHeaderNames.CONTENT_LENGTH, "0");
                        _ = ch.WriteAndFlushAsync(response);
                    })).BindAsync(addr, TestContext.Current.CancellationToken);
            serverChannel = await sf;
            EndPoint destination = new IPEndPoint(await AddressByName("localhost"), 1234);
            Task<IChannel> cf = new Bootstrap().Channel<LocalChannel>().Group(group).Handler(
                new ActionChannelInitializer<IChannel>(ch =>
                {
                    ch.Pipeline.AddFirst(new HttpProxyHandler(addr));
                    ch.Pipeline.AddLast(new ExceptionRecorder(exception));
                })).ConnectAsync(destination, TestContext.Current.CancellationToken);
            clientChannel = await cf;
            await clientChannel.CloseAsync(TestContext.Current.CancellationToken);

            Assert.IsAssignableFrom<HttpProxyConnectException>(exception.Get());
            HttpProxyConnectException actual = (HttpProxyConnectException) exception.Get();
            Assert.NotNull(actual.Headers());
            Assert.Equal("value", actual.Headers().Get("name"));
        }
        finally
        {
            if (clientChannel != null)
            {
                _ = clientChannel.CloseAsync(TestContext.Current.CancellationToken);
            }
            if (serverChannel != null)
            {
                _ = serverChannel.CloseAsync(TestContext.Current.CancellationToken);
            }
            if (group != null)
            {
                _ = group.ShutdownGracefullyAsync();
            }
        }
    }

    private sealed class ExceptionRecorder : ChannelInboundHandlerAdapter
    {
        private readonly AtomicReference<Exception> exception;

        internal ExceptionRecorder(AtomicReference<Exception> exception)
        {
            this.exception = exception;
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx,
            Exception cause)
        {
            exception.Set(cause);
        }
    }

    private static void TestInitialMessage(EndPoint socketAddress,
                                           string expectedUrl,
                                           string expectedHostHeader,
                                           HttpHeaders headers,
                                           bool ignoreDefaultPortsInConnectHostHeader)
    {
        TestInitialMessage(socketAddress, expectedUrl, expectedHostHeader, headers,
                ignoreDefaultPortsInConnectHostHeader, true);
    }

    private static void TestInitialMessage(EndPoint socketAddress,
                                           string expectedUrl,
                                           string expectedHostHeader,
                                           HttpHeaders headers,
                                           bool ignoreDefaultPortsInConnectHostHeader,
                                           bool validateInitialHeaders)
    {
        EndPoint proxyAddress = new IPEndPoint(NetUtil.LOCALHOST, 8080);

        CancellationToken promise = new CancellationTokenSource().Token;

        Mock<IChannelHandlerContext> ctx = new Mock<IChannelHandlerContext>();
        ctx.Setup(c => c.ConnectAsync(It.Is<EndPoint>(a => ReferenceEquals(a, proxyAddress)),
                It.Is<EndPoint>(a => a == null), promise)).Returns(Task.CompletedTask);

        HttpProxyHandler handler = new HttpProxyHandler(
                new IPEndPoint(NetUtil.LOCALHOST, 8080),
                headers,
                ignoreDefaultPortsInConnectHostHeader, validateInitialHeaders);
        _ = handler.ConnectAsync(ctx.Object, socketAddress, null, promise);

        IFullHttpRequest request = (IFullHttpRequest) handler.InvokeNewInitialMessage(ctx.Object);
        try
        {
            Assert.Equal(HttpVersion.HTTP_1_1, request.ProtocolVersion());
            Assert.Equal(expectedUrl, request.Uri());
            HttpHeaders actualHeaders = request.Headers();
            Assert.Equal(expectedHostHeader, actualHeaders.Get(HttpHeaderNames.HOST));

            if (headers != null)
            {
                // The actual request header is a strict superset of the custom header
                foreach (string name in headers.Names())
                {
                    Assert.Equal(headers.GetAll(name), actualHeaders.GetAll(name));
                }
            }
        }
        finally
        {
            request.Release();
        }
        ctx.Verify(c => c.ConnectAsync(proxyAddress, null, promise), Times.Once());
    }

    [Fact]
    public void TestHttpClientCodecIsInvisible()
    {
        EmbeddedChannel channel = new InactiveEmbeddedChannel(new HttpProxyHandler(
                new IPEndPoint(NetUtil.LOCALHOST, 8080)));
        Assert.NotNull(channel.Pipeline.Get<HttpProxyHandler>());
        Assert.Null(channel.Pipeline.Get<HttpClientCodec>());
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
                // We want to simulate that the Channel did not become active yet.
                return false;
            }
        }
    }

    [Fact]
    public void TestRemoveOnProxyConnectionEventWritesPendingWrites()
    {
        HttpProxyHandler handler = new HttpProxyHandler(new IPEndPoint(NetUtil.LOCALHOST, 8080));
        AtomicBoolean active = new AtomicBoolean();
        EmbeddedChannel channel = new ActiveFlagEmbeddedChannel(active, handler, new RemoveOnProxyConnectionEvent());
        _ = channel.ConnectAsync(new IPEndPoint(NetUtil.LOCALHOST, 443), null, TestContext.Current.CancellationToken);
        ByteBuf msg = Unpooled.CopiedBuffer("hello", Encoding.ASCII);
        Task writeFuture = channel.WriteAndFlushAsync(msg, TestContext.Current.CancellationToken);
        Assert.False(writeFuture.IsCompleted);

        active.Set(true);
        channel.Pipeline.FireChannelActive();
        ByteBuf connectRequest = channel.ReadOutbound<ByteBuf>();
        Assert.StartsWith("CONNECT ", connectRequest.ToString(Encoding.ASCII));
        connectRequest.Release();
        channel.WriteInbound(Unpooled.CopiedBuffer("HTTP/1.1 200 OK\r\n\r\n", Encoding.ASCII));

        Assert.True(handler.ConnectFuture().IsCompletedSuccessfully);
        Assert.True(writeFuture.IsCompletedSuccessfully);
        Assert.Null(channel.Pipeline.Get<HttpProxyHandler>());
        Assert.Same(msg, channel.ReadOutbound<object>());
        msg.Release();
        Assert.False(channel.FinishAndReleaseAll());
    }

    private sealed class RemoveOnProxyConnectionEvent : ChannelInboundHandlerAdapter
    {
        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is ProxyConnectionEvent)
            {
                // The handshake is done, so the proxy handler is not needed anymore.
                ctx.Pipeline.Remove<HttpProxyHandler>();
            }
            ctx.FireUserEventTriggered(evt);
        }
    }

    // CLR: the anonymous subclass reads the captured AtomicBoolean. The EmbeddedChannel constructor reads IsActive before
    // this constructor's body stores the flag; it is false then, as the new AtomicBoolean is in the source.
    private sealed class ActiveFlagEmbeddedChannel : EmbeddedChannel
    {
        private readonly AtomicBoolean active;

        internal ActiveFlagEmbeddedChannel(AtomicBoolean active, params IChannelHandler[] handlers) : base(handlers)
        {
            this.active = active;
        }

        public override bool IsActive => active != null && active.Get();
    }

    // CLR: InetAddress.getByName(host) (see the class comment).
    private static async Task<IPAddress> AddressByName(string host)
    {
        using DefaultNameResolver resolver = new DefaultNameResolver(ImmediateEventExecutor.INSTANCE);
        return await resolver.ResolveAsync(host);
    }

    // CLR: the host text HttpUtil.formatHostnameForHttp gives a resolved address without a host name: the numeric
    // address, in brackets for IPv6 (see the class comment).
    private static string HostText(IPEndPoint address)
    {
        string text = NetUtil.ToAddressString(address.Address);
        return address.AddressFamily == AddressFamily.InterNetworkV6 ? '[' + text + ']' : text;
    }
}
