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
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Common.ReferenceCountUtil;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests;

#pragma warning disable xUnit1051 // The source's writeAndFlush(...)/shutdownOutput() observe no test cancellation.
#pragma warning disable xUnit1031 // The source blocks with bind(...).sync(), connect(...).awaitUninterruptibly() and shutdownGracefully().syncUninterruptibly().

// CLR: CharsetUtil.ISO_8859_1/US_ASCII/UTF_8 are Encoding.Latin1/ASCII/UTF8 and String.getBytes() encodes with the
// default charset, Encoding.Default (docs/codec-smtp-porting.md; codec-stomp's tests). assertInstanceOf and
// assertThat(x).isNotInstanceOf(T.class) are Assert.IsAssignableFrom/IsNotAssignableFrom, and assertThrows(T.class, ...),
// which also accepts a subclass of T, is Assert.ThrowsAny<T>
// (https://raw.githubusercontent.com/xunit/assert.xunit/main/TypeAsserts.cs,
// https://raw.githubusercontent.com/xunit/assert.xunit/main/ExceptionAsserts.cs); the Java messages use
// XunitAssertMessages.AssertM. The @Deprecated constructors that the source calls are replaced by the ones their
// @deprecated texts name (owner decision 2026-11-16): HttpClientCodec(int, int, int, boolean, boolean validateHeaders)
// by HttpClientCodec(int, int, int, boolean) (validateHeaders true is the default), HttpClientCodec(int, int, int,
// boolean, boolean, boolean parseHttpAfterConnectRequest) by HttpClientCodec(HttpDecoderConfig, boolean, boolean), and
// HttpRequestDecoder(int, int, int, boolean) by HttpRequestDecoder(HttpDecoderConfig), with the same values. The
// abstract Consumer callback and the anonymous ChannelInitializers/SimpleChannelInboundHandlers are named nested
// classes. In testServerCloseSocketInputProvidesData, the Transport port's bootstrap API is asynchronous
// (bind(...).sync().channel() and connect(...).awaitUninterruptibly() wait for BindAsync/ConnectAsync), ChannelFuture
// listeners are Task continuations, CountDownLatch is CountdownEvent
// (https://learn.microsoft.com/dotnet/api/system.threading.countdownevent), shutdownOutput() is ShutdownOutputAsync and
// shutdownGracefully().syncUninterruptibly() waits for ShutdownGracefullyAsync, as the handler tests do
// (test/Netty.NET.Handler.Tests/Flow/FlowControlHandlerTest.cs). new InetSocketAddress(0) is the IPv4 wildcard, as the
// Transport's ServerBootstrap.BindAsync(int) binds it (src/Netty.NET.Transport/Bootstrapping/ServerBootstrap.cs), so the
// client connects to the IPv4 loopback NetUtil.LOCALHOST4.
public class HttpClientCodecTest
{

    private const string EMPTY_RESPONSE = "HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n";
    private const string RESPONSE = "HTTP/1.0 200 OK\r\n" + "Date: Fri, 31 Dec 1999 23:59:59 GMT\r\n" +
            "Content-Type: text/html\r\n" + "Content-Length: 28\r\n" + "\r\n"
            + "<html><body></body></html>\r\n";
    private const string INCOMPLETE_CHUNKED_RESPONSE = "HTTP/1.1 200 OK\r\n" + "Content-Type: text/plain\r\n" +
            "Transfer-Encoding: chunked\r\n" + "\r\n" +
            "5\r\n" + "first\r\n" + "6\r\n" + "second\r\n" + "0\r\n";
    private const string CHUNKED_RESPONSE = INCOMPLETE_CHUNKED_RESPONSE + "\r\n";

    [Fact]
    public void TestConnectWithResponseContent()
    {
        HttpClientCodec codec = new HttpClientCodec(4096, 8192, 8192, true);
        EmbeddedChannel ch = new EmbeddedChannel(codec);

        SendRequestAndReadResponse(ch, HttpMethod.CONNECT, RESPONSE);
        ch.Finish();
    }

    [Fact]
    public void TestFailsNotOnRequestResponseChunked()
    {
        HttpClientCodec codec = new HttpClientCodec(4096, 8192, 8192, true);
        EmbeddedChannel ch = new EmbeddedChannel(codec);

        SendRequestAndReadResponse(ch, HttpMethod.GET, CHUNKED_RESPONSE);
        ch.Finish();
    }

    [Fact]
    public void TestFailsOnMissingResponse()
    {
        HttpClientCodec codec = new HttpClientCodec(4096, 8192, 8192, true);
        EmbeddedChannel ch = new EmbeddedChannel(codec);

        Assert.True(ch.WriteOutbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET,
                "http://localhost/")));
        ByteBuf buffer = ch.ReadOutbound<ByteBuf>();
        Assert.NotNull(buffer);
        buffer.Release();
        CodecException e = Assert.ThrowsAny<CodecException>(() => ch.Finish());
        Assert.IsAssignableFrom<PrematureChannelClosureException>(e);
    }

    [Fact]
    public void TestFailsOnIncompleteChunkedResponse()
    {
        HttpClientCodec codec = new HttpClientCodec(4096, 8192, 8192, true);
        EmbeddedChannel ch = new EmbeddedChannel(codec);

        ch.WriteOutbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "http://localhost/"));
        ByteBuf buffer = ch.ReadOutbound<ByteBuf>();
        Assert.NotNull(buffer);
        buffer.Release();
        Assert.Null(ch.ReadInbound());
        ch.WriteInbound(Unpooled.CopiedBuffer(INCOMPLETE_CHUNKED_RESPONSE, Encoding.Latin1));
        Assert.IsAssignableFrom<IHttpResponse>(ch.ReadInbound());
        ((IHttpContent) ch.ReadInbound()).Release(); // Chunk 'first'
        ((IHttpContent) ch.ReadInbound()).Release(); // Chunk 'second'
        Assert.Null(ch.ReadInbound());

        CodecException e = Assert.ThrowsAny<CodecException>(() => ch.Finish());
        Assert.IsAssignableFrom<PrematureChannelClosureException>(e);
    }

    [Fact]
    public void TestServerCloseSocketInputProvidesData()
    {
        ServerBootstrap sb = new ServerBootstrap();
        Bootstrap cb = new Bootstrap();
        CountdownEvent serverChannelLatch = new CountdownEvent(1);
        CountdownEvent responseReceivedLatch = new CountdownEvent(1);
        IEventLoopGroup serverGroup = null;
        IEventLoopGroup clientGroup = null;

        try
        {
            serverGroup = new MultiThreadIoEventLoopGroup(2, NioIoHandler.NewFactory());
            sb.Group(serverGroup);
            sb.Channel<NioServerSocketChannel>();
            sb.ChildHandler(new ServerInitializer(serverChannelLatch));

            clientGroup = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());
            cb.Group(clientGroup);
            cb.Channel<NioSocketChannel>();
            cb.Option(ChannelOption.ALLOW_HALF_CLOSURE, true);
            cb.Handler(new ClientInitializer(responseReceivedLatch));

            IChannel serverChannel = sb.BindAsync(new IPEndPoint(IPAddress.Any, 0)).GetAwaiter().GetResult();
            int port = ((IPEndPoint) serverChannel.LocalAddress).Port;

            Task<IChannel> ccf = cb.ConnectAsync(new IPEndPoint(NetUtil.LOCALHOST4, port));
            Assert.True(((IAsyncResult) ccf).AsyncWaitHandle.WaitOne() && ccf.IsCompletedSuccessfully);
            IChannel clientChannel = ccf.Result;
            Assert.True(serverChannelLatch.Wait(TimeSpan.FromSeconds(5)));
            _ = clientChannel.WriteAndFlushAsync(new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/"));
            Assert.True(responseReceivedLatch.Wait(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            serverGroup?.ShutdownGracefullyAsync().Wait();
            clientGroup?.ShutdownGracefullyAsync().Wait();
        }
    }

    private sealed class ServerInitializer : ChannelInitializer<IChannel>
    {
        private readonly CountdownEvent serverChannelLatch;

        internal ServerInitializer(CountdownEvent serverChannelLatch)
        {
            this.serverChannelLatch = serverChannelLatch;
        }

        protected override void InitChannel(IChannel ch)
        {
            // Don't use the HttpServerCodec, because we don't want to have content-length or anything added.
            ch.Pipeline.AddLast(new HttpRequestDecoder(new HttpDecoderConfig()
                    .SetMaxInitialLineLength(4096)
                    .SetMaxHeaderSize(8192)
                    .SetMaxChunkSize(8192)
                    .SetValidateHeaders(true)));
            ch.Pipeline.AddLast(new HttpObjectAggregator(4096));
            ch.Pipeline.AddLast(new ServerHandler());
            serverChannelLatch.Signal();
        }
    }

    private sealed class ServerHandler : SimpleChannelInboundHandler<IFullHttpRequest>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, IFullHttpRequest msg)
        {
            // This is just a simple demo...don't block in IO
            Assert.IsAssignableFrom<ISocketChannel>(ctx.Channel);
            ISocketChannel sChannel = (ISocketChannel) ctx.Channel;
            /**
             * The point of this test is to not add any content-length or content-encoding headers
             * and the client should still handle this.
             * See <a href="https://tools.ietf.org/html/rfc7230#section-3.3.3">RFC 7230, 3.3.3</a>.
             */
            _ = sChannel.WriteAndFlushAsync(Unpooled.WrappedBuffer(Encoding.Latin1.GetBytes("HTTP/1.0 200 OK\r\n" +
            "Date: Fri, 31 Dec 1999 23:59:59 GMT\r\n" +
            "Content-Type: text/html\r\n\r\n")))
                    .ContinueWith(future =>
                    {
                        Assert.True(future.IsCompletedSuccessfully);
                        _ = sChannel.WriteAndFlushAsync(Unpooled.WrappedBuffer(Encoding.Latin1.GetBytes(
                                "<html><body>hello half closed!</body></html>\r\n")))
                                .ContinueWith(f =>
                                {
                                    Assert.True(f.IsCompletedSuccessfully);
                                    _ = sChannel.ShutdownOutputAsync();
                                }, TaskContinuationOptions.ExecuteSynchronously);
                    }, TaskContinuationOptions.ExecuteSynchronously);
        }
    }

    private sealed class ClientInitializer : ChannelInitializer<IChannel>
    {
        private readonly CountdownEvent responseReceivedLatch;

        internal ClientInitializer(CountdownEvent responseReceivedLatch)
        {
            this.responseReceivedLatch = responseReceivedLatch;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Pipeline.AddLast(new HttpClientCodec(4096, 8192, 8192, true));
            ch.Pipeline.AddLast(new HttpObjectAggregator(4096));
            ch.Pipeline.AddLast(new ClientHandler(responseReceivedLatch));
        }
    }

    private sealed class ClientHandler : SimpleChannelInboundHandler<IFullHttpResponse>
    {
        private readonly CountdownEvent responseReceivedLatch;

        internal ClientHandler(CountdownEvent responseReceivedLatch)
        {
            this.responseReceivedLatch = responseReceivedLatch;
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, IFullHttpResponse msg)
        {
            responseReceivedLatch.Signal();
        }
    }

    [Fact]
    public void TestContinueParsingAfterConnect()
    {
        TestAfterConnect(true);
    }

    [Fact]
    public void TestPassThroughAfterConnect()
    {
        TestAfterConnect(false);
    }

    private static void TestAfterConnect(bool parseAfterConnect)
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpClientCodec(new HttpDecoderConfig()
                .SetMaxInitialLineLength(4096)
                .SetMaxHeaderSize(8192)
                .SetMaxChunkSize(8192)
                .SetValidateHeaders(true), parseAfterConnect, true));

        Consumer connectResponseConsumer = new Consumer();
        SendRequestAndReadResponse(ch, HttpMethod.CONNECT, EMPTY_RESPONSE, connectResponseConsumer);
        Assert.True(connectResponseConsumer.GetReceivedCount() > 0, "No connect response messages received.");
        Consumer responseConsumer = new ParseAfterConnectConsumer(parseAfterConnect);
        SendRequestAndReadResponse(ch, HttpMethod.GET, RESPONSE, responseConsumer);
        Assert.True(responseConsumer.GetReceivedCount() > 0, "No response messages received.");
        Assert.False(ch.Finish(), "Channel finish failed.");
    }

    private sealed class ParseAfterConnectConsumer : Consumer
    {
        private readonly bool parseAfterConnect;

        internal ParseAfterConnectConsumer(bool parseAfterConnect)
        {
            this.parseAfterConnect = parseAfterConnect;
        }

        internal override void Accept(object @object)
        {
            if (parseAfterConnect)
            {
                Assert.IsAssignableFrom<IHttpObject>(@object);
            }
            else
            {
                Assert.IsNotAssignableFrom<IHttpObject>(@object);
            }
        }
    }

    private static void SendRequestAndReadResponse(EmbeddedChannel ch, HttpMethod httpMethod, string response)
    {
        SendRequestAndReadResponse(ch, httpMethod, response, new Consumer());
    }

    private static void SendRequestAndReadResponse(EmbeddedChannel ch, HttpMethod httpMethod, string response,
                                                   Consumer responseConsumer)
    {
        Assert.True(ch.WriteOutbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, httpMethod, "http://localhost/")),
                "Channel outbound write failed.");
        Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(response, Encoding.Latin1)),
                "Channel inbound write failed.");

        for (;;)
        {
            object msg = ch.ReadOutbound();
            if (msg == null)
            {
                break;
            }

            Release(msg);
        }

        for (;;)
        {
            object msg = ch.ReadInbound();
            if (msg == null)
            {
                break;
            }

            responseConsumer.OnResponse(msg);
            Release(msg);
        }
    }

    private class Consumer
    {

        private int receivedCount;

        internal void OnResponse(object @object)
        {
            receivedCount++;
            Accept(@object);
        }

        internal virtual void Accept(object @object)
        {
            // Default noop.
        }

        internal int GetReceivedCount()
        {
            return receivedCount;
        }
    }

    [Fact]
    public void TestDecodesFinalResponseAfterSwitchingProtocols()
    {
        string SWITCHING_PROTOCOLS_RESPONSE = "HTTP/1.1 101 Switching Protocols\r\n" +
                "Connection: Upgrade\r\n" +
                "Upgrade: TLS/1.2, HTTP/1.1\r\n\r\n";

        HttpClientCodec codec = new HttpClientCodec(4096, 8192, 8192, true);
        EmbeddedChannel ch = new EmbeddedChannel(codec, new HttpObjectAggregator(1024));

        IHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "http://localhost/");
        request.Headers().Set(HttpHeaderNames.CONNECTION, HttpHeaderValues.UPGRADE);
        request.Headers().Set(HttpHeaderNames.UPGRADE, "TLS/1.2");
        Assert.True(ch.WriteOutbound(request), "Channel outbound write failed.");

        Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(SWITCHING_PROTOCOLS_RESPONSE, Encoding.Latin1)),
                "Channel inbound write failed.");
        object switchingProtocolsResponse = ch.ReadInbound();
        Assert.NotNull(switchingProtocolsResponse, "No response received");
        Assert.IsAssignableFrom<IFullHttpResponse>(switchingProtocolsResponse);
        ((IFullHttpResponse) switchingProtocolsResponse).Release();

        Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(RESPONSE, Encoding.Latin1)),
                "Channel inbound write failed");
        object finalResponse = ch.ReadInbound();
        Assert.NotNull(finalResponse, "No response received");
        Assert.IsAssignableFrom<IFullHttpResponse>(finalResponse);
        ((IFullHttpResponse) finalResponse).Release();
        Assert.True(ch.FinishAndReleaseAll(), "Channel finish failed");
    }

    [Fact]
    public void TestWebSocket00Response()
    {
        byte[] data = Encoding.Default.GetBytes("HTTP/1.1 101 WebSocket Protocol Handshake\r\n" +
                "Upgrade: WebSocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Origin: http://localhost:8080\r\n" +
                "Sec-WebSocket-Location: ws://localhost/some/path\r\n" +
                "\r\n" +
                "1234567812345678");
        EmbeddedChannel ch = new EmbeddedChannel(new HttpClientCodec());
        Assert.True(ch.WriteInbound(Unpooled.WrappedBuffer(data)));

        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.SWITCHING_PROTOCOLS, res.Status());
        IHttpContent content = ch.ReadInbound<IHttpContent>();
        Assert.Equal(16, content.Content.ReadableBytes);
        content.Release();

        Assert.False(ch.Finish());

        Assert.Null(ch.ReadInbound());
    }

    [Fact]
    public void TestWebDavResponse()
    {
        byte[] data = Encoding.Default.GetBytes("HTTP/1.1 102 Processing\r\n" +
                       "Status-URI: Status-URI:http://status.com; 404\r\n" +
                       "\r\n" +
                       "1234567812345678");
        EmbeddedChannel ch = new EmbeddedChannel(new HttpClientCodec());
        Assert.True(ch.WriteInbound(Unpooled.WrappedBuffer(data)));

        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.PROCESSING, res.Status());
        IHttpContent content = ch.ReadInbound<IHttpContent>();
        // HTTP 102 is not allowed to have content.
        Assert.Equal(0, content.Content.ReadableBytes);
        content.Release();

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestInformationalResponseKeepsPairsInSync()
    {
        string data = "HTTP/1.1 102 Processing\r\n" +
                "Status-URI: Status-URI:http://status.com; 404\r\n" +
                "\r\n" +
                "HTTP/1.1 200 OK\r\n" +
                "Content-Length: 5\r\n" +
                "\r\n"; // No contents; we're responding to a HEAD request.
        string data2 = "HTTP/1.1 200 OK\r\n" +
                "Content-Length: 8\r\n" +
                "\r\n" +
                "12345678";
        EmbeddedChannel ch = new EmbeddedChannel(new HttpClientCodec());
        Assert.True(ch.WriteOutbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.HEAD, "/")));
        ByteBuf buffer = ch.ReadOutbound<ByteBuf>();
        buffer.Release();
        Assert.Null(ch.ReadOutbound());
        Assert.True(ch.WriteInbound(Unpooled.WrappedBuffer(Encoding.Latin1.GetBytes(data))));
        IHttpResponse res = ch.ReadInbound<IHttpResponse>();
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.PROCESSING, res.Status());
        IHttpContent content = ch.ReadInbound<IHttpContent>();
        // HTTP 102 is not allowed to have content.
        Assert.Equal(0, content.Content.ReadableBytes);
        Assert.IsAssignableFrom<ILastHttpContent>(content);
        content.Release();
        res = ch.ReadInbound<IHttpResponse>();
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.OK, res.Status());
        // If it had not been a HEAD request, server *would* have sent 5 bytes of contents...
        Assert.Equal(5, res.Headers().GetInt(HttpHeaderNames.CONTENT_LENGTH));
        content = ch.ReadInbound<IHttpContent>();
        // ... but it is a HEAD request, so we get zero bytes.
        Assert.Equal(0, content.Content.ReadableBytes);
        content.Release();

        Assert.True(ch.WriteOutbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/")));
        buffer = ch.ReadOutbound<ByteBuf>();
        buffer.Release();
        Assert.Null(ch.ReadOutbound());
        Assert.True(ch.WriteInbound(Unpooled.WrappedBuffer(Encoding.Latin1.GetBytes(data2))));

        res = ch.ReadInbound<IHttpResponse>();
        Assert.Same(HttpVersion.HTTP_1_1, res.ProtocolVersion());
        Assert.Equal(HttpResponseStatus.OK, res.Status());
        content = ch.ReadInbound<IHttpContent>();
        // HTTP 200 has content.
        Assert.Equal(8, content.Content.ReadableBytes);
        Assert.IsAssignableFrom<ILastHttpContent>(content);
        content.Release();

        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestInformationalFollowedByResponse()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpClientCodec());

        Assert.True(channel.WriteOutbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/1")));
        ByteBuf request = channel.ReadOutbound<ByteBuf>();
        request.Release();
        Assert.Null(channel.ReadOutbound());

        Assert.True(channel.WriteOutbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.HEAD, "/2")));
        request = channel.ReadOutbound<ByteBuf>();
        request.Release();
        Assert.Null(channel.ReadOutbound());

        string responseStr =
                "HTTP/1.1 103 Early Hints\r\n\r\n" + // Early response to first GET request
                "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello" + // Actual response to first GET request
                "HTTP/1.1 200 OK\r\n\r\n"; // Body-less response to second HEAD request
        Assert.True(channel.WriteInbound(Unpooled.CopiedBuffer(responseStr, Encoding.ASCII)));

        // Response 1: Early hints to first GET request.
        IHttpResponse response = channel.ReadInbound<IHttpResponse>();
        Assert.Equal(HttpResponseStatus.EARLY_HINTS, response.Status());
        ILastHttpContent last = channel.ReadInbound<ILastHttpContent>();
        Assert.Equal(0, last.Content.ReadableBytes);
        last.Release();

        // Response 2: Actual response, with contents, to first GET request.
        response = channel.ReadInbound<IHttpResponse>();
        Assert.Equal(HttpResponseStatus.OK, response.Status());
        Assert.Equal(5, response.Headers().GetInt(HttpHeaderNames.CONTENT_LENGTH));
        last = channel.ReadInbound<ILastHttpContent>();
        Assert.Equal(5, last.Content.ReadableBytes);
        last.Release();

        // Response 3: Actual response, with no contents, to second HEAD request.
        response = channel.ReadInbound<IHttpResponse>();
        Assert.Equal(HttpResponseStatus.OK, response.Status());
        last = channel.ReadInbound<ILastHttpContent>();
        Assert.Equal(0, last.Content.ReadableBytes);
        last.Release();

        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestMultipleResponses()
    {
        string response = "HTTP/1.1 200 OK\r\n" +
                "Content-Length: 0\r\n\r\n";

        HttpClientCodec codec = new HttpClientCodec(4096, 8192, 8192, true);
        EmbeddedChannel ch = new EmbeddedChannel(codec, new HttpObjectAggregator(1024));

        IHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "http://localhost/");
        Assert.True(ch.WriteOutbound(request));

        Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(response, Encoding.UTF8)));
        Assert.True(ch.WriteInbound(Unpooled.CopiedBuffer(response, Encoding.UTF8)));
        IFullHttpResponse resp = ch.ReadInbound<IFullHttpResponse>();
        Assert.True(resp.DecoderResult().IsSuccess());
        resp.Release();

        resp = ch.ReadInbound<IFullHttpResponse>();
        Assert.True(resp.DecoderResult().IsSuccess());
        resp.Release();
        Assert.True(ch.FinishAndReleaseAll());
    }

    [Fact]
    public void TestWriteThroughAfterUpgrade()
    {
        HttpClientCodec codec = new HttpClientCodec();
        EmbeddedChannel ch = new EmbeddedChannel(codec);
        codec.PrepareUpgradeFrom(null);

        ByteBuf buffer = ch.Alloc().Buffer();
        Assert.Equal(1, buffer.RefCnt);
        Assert.True(ch.WriteOutbound(buffer));
        // buffer should pass through unchanged
        Assert.Same(buffer, ch.ReadOutbound<ByteBuf>());
        Assert.Equal(1, buffer.RefCnt);

        buffer.Release();
    }
}
