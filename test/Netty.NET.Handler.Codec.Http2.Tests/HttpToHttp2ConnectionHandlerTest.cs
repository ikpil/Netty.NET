/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;
using Netty.NET.Handler.Codec.Http;
using static Netty.NET.Handler.Codec.Http.HttpMethod;
using static Netty.NET.Handler.Codec.Http.HttpVersion;
using static Netty.NET.Handler.Codec.Http2.Tests.Http2TestUtil;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Testing the {@link HttpToHttp2ConnectionHandler} for {@link FullHttpRequest} objects into HTTP/2 frames
 */
// CLR: the @Mock listeners are Moq Mock<IHttp2FrameListener> (loose, as Mockito's mocks); doAnswer(...).when(...)
// is Setup(...).Callback(...).Returns(0) (the Answer returns null for the int result, which Mockito's mock gives
// as the default 0) and verify(..., never()/atLeastOnce()) is Verify(..., Times.Never()/AtLeastOnce())
// (https://github.com/devlooped/moq/wiki/Quickstart); eq(headers) is the expected value itself, which Moq compares
// with Equals (DefaultHttp2Headers keeps Java's equals); anyShort/anyBoolean/anyInt are It.IsAny.
// @BeforeEach/@AfterEach are the field initializers and IAsyncLifetime.DisposeAsync, as
// Http2ConnectionRoundtripTest does. write(msg, promise)/writeAndFlush(msg, promise) take no promise in the
// Transport port; the returned Task replaces the promise, so the private Write/WriteAndFlush helpers complete the
// test's promise (ctx().newPromise(), a TaskCompletionSource) from the WriteAsync/WriteAndFlushAsync Task with
// Http2CodecUtil.Cascade (TrySetFromTask, the PromiseNotifier mapping of docs/common-task-composition.md) and
// return the promise's Task, as Java returns the promise. awaitUninterruptibly(timeout) returns whether the future
// completed and does not throw for a failed future, while Task.Wait(TimeSpan) throws an AggregateException when
// "an exception was thrown during the execution of the task"
// (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.wait?view=net-10.0), so
// AwaitUninterruptibly waits with Task.WhenAny of the Task and a delay. isDone()/isSuccess()/cause() are
// IsCompleted/IsCompletedSuccessfully/ Http2CodecUtil.Cause, and assertInstanceOf is Assert.IsAssignableFrom.
// CountDownLatch is CountdownEvent (Http2TestUtil.FrameCountDown counts down at most to zero).
// Collections.synchronizedList is a List<string> written under a lock. Unpooled.copiedBuffer(text, UTF_8) is
// Unpooled.CopiedBuffer(text, Encoding.UTF8) and ByteBuf.toString(UTF_8) ToString(Encoding.UTF8). The anonymous
// ChannelInitializer/ChannelInboundHandlerAdapter classes are nested classes; new LocalAddress(getClass()) is
// LocalAddress(Type).
public class HttpToHttp2ConnectionHandlerTest : IAsyncLifetime
{
    private const int WAIT_TIME_SECONDS = 5;

    private readonly Mock<IHttp2FrameListener> clientListener = new Mock<IHttp2FrameListener>();

    private readonly Mock<IHttp2FrameListener> serverListener = new Mock<IHttp2FrameListener>();

    private ServerBootstrap sb;
    private Bootstrap cb;
    private IChannel serverChannel;
    private volatile IChannel serverConnectedChannel;
    private IChannel clientChannel;
    private CountdownEvent requestLatch;
    private CountdownEvent serverSettingsAckLatch;
    private CountdownEvent trailersLatch;
    private FrameCountDown serverFrameCountDown;

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (clientChannel != null)
        {
            await clientChannel.CloseAsync();
            clientChannel = null;
        }

        if (serverChannel != null)
        {
            await serverChannel.CloseAsync();
            serverChannel = null;
        }

        IChannel serverConnectedChannel = this.serverConnectedChannel;
        if (serverConnectedChannel != null)
        {
            await serverConnectedChannel.CloseAsync();
            this.serverConnectedChannel = null;
        }

        Task serverGroup = sb.Config.Group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        Task serverChildGroup = sb.Config.ChildGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        Task clientGroup = cb.Config.Group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        await serverGroup;
        await serverChildGroup;
        await clientGroup;
    }

    [Fact]
    public async Task TestHeadersOnlyRequest()
    {
        await BootstrapEnv(2, 1, 0);
        IFullHttpRequest request = new DefaultFullHttpRequest(HTTP_1_1, GET,
                "http://my-user_name@www.example.org:5555/example");
        HttpHeaders httpHeaders = request.Headers();
        httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 5);
        httpHeaders.Set(HttpHeaderNames.HOST, "my-user_name@www.example.org:5555");
        httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text(), "http");
        httpHeaders.Add(Of("foo"), Of("goo"));
        httpHeaders.Add(Of("foo"), Of("goo2"));
        httpHeaders.Add(Of("foo2"), Of("goo2"));
        IHttp2Headers http2Headers =
                new DefaultHttp2Headers().Method(new AsciiString("GET")).Path(new AsciiString("/example"))
                .Authority(new AsciiString("www.example.org:5555")).Scheme(new AsciiString("http"))
                .Add(new AsciiString("foo"), new AsciiString("goo"))
                .Add(new AsciiString("foo"), new AsciiString("goo2"))
                .Add(new AsciiString("foo2"), new AsciiString("goo2"));

        TaskCompletionSource writePromise = NewPromise();
        VerifyHeadersOnly(http2Headers, writePromise, WriteAndFlush(request, writePromise));
    }

    [Fact]
    public async Task TestHttpScheme()
    {
        await BootstrapEnv(2, 1, 0);
        IFullHttpRequest request = new DefaultFullHttpRequest(HTTP_1_1, GET,
                "http://my-user_name@www.example.org:5555/example");
        HttpHeaders httpHeaders = request.Headers();
        httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 5);
        httpHeaders.Set(HttpHeaderNames.HOST, "my-user_name@www.example.org:5555");
        httpHeaders.Add(Of("foo"), Of("goo"));
        httpHeaders.Add(Of("foo"), Of("goo2"));
        httpHeaders.Add(Of("foo2"), Of("goo2"));
        IHttp2Headers http2Headers =
                new DefaultHttp2Headers().Method(new AsciiString("GET")).Path(new AsciiString("/example"))
                        .Authority(new AsciiString("www.example.org:5555")).Scheme(new AsciiString("http"))
                        .Scheme(new AsciiString("http"))
                        .Add(new AsciiString("foo"), new AsciiString("goo"))
                        .Add(new AsciiString("foo"), new AsciiString("goo2"))
                        .Add(new AsciiString("foo2"), new AsciiString("goo2"));

        TaskCompletionSource writePromise = NewPromise();
        VerifyHeadersOnly(http2Headers, writePromise, WriteAndFlush(request, writePromise));
    }

    [Fact]
    public async Task TestMultipleCookieEntriesAreCombined()
    {
        await BootstrapEnv(2, 1, 0);
        IFullHttpRequest request = new DefaultFullHttpRequest(HTTP_1_1, GET,
                "http://my-user_name@www.example.org:5555/example");
        HttpHeaders httpHeaders = request.Headers();
        httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 5);
        httpHeaders.Set(HttpHeaderNames.HOST, "my-user_name@www.example.org:5555");
        httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text(), "http");
        httpHeaders.Set(HttpHeaderNames.COOKIE, "a=b; c=d; e=f");
        IHttp2Headers http2Headers =
                new DefaultHttp2Headers().Method(new AsciiString("GET")).Path(new AsciiString("/example"))
                .Authority(new AsciiString("www.example.org:5555")).Scheme(new AsciiString("http"))
                .Add(HttpHeaderNames.COOKIE, Of("a=b"))
                .Add(HttpHeaderNames.COOKIE, Of("c=d"))
                .Add(HttpHeaderNames.COOKIE, Of("e=f"));

        TaskCompletionSource writePromise = NewPromise();
        VerifyHeadersOnly(http2Headers, writePromise, WriteAndFlush(request, writePromise));
    }

    [Fact]
    public async Task TestOriginFormRequestTargetHandled()
    {
        await BootstrapEnv(2, 1, 0);
        IFullHttpRequest request = new DefaultFullHttpRequest(HTTP_1_1, GET, "/where?q=now&f=then#section1");
        HttpHeaders httpHeaders = request.Headers();
        httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 5);
        httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text(), "http");
        IHttp2Headers http2Headers =
                new DefaultHttp2Headers().Method(new AsciiString("GET"))
                .Path(new AsciiString("/where?q=now&f=then#section1"))
                .Scheme(new AsciiString("http"));

        TaskCompletionSource writePromise = NewPromise();
        VerifyHeadersOnly(http2Headers, writePromise, WriteAndFlush(request, writePromise));
    }

    [Fact]
    public async Task TestOriginFormRequestTargetHandledFromUrlencodedUri()
    {
        await BootstrapEnv(2, 1, 0);
        IFullHttpRequest request = new DefaultFullHttpRequest(
                HTTP_1_1, GET, "/where%2B0?q=now%2B0&f=then%2B0#section1%2B0");
        HttpHeaders httpHeaders = request.Headers();
        httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 5);
        httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text(), "http");
        IHttp2Headers http2Headers =
                new DefaultHttp2Headers().Method(new AsciiString("GET"))
                                         .Path(new AsciiString("/where%2B0?q=now%2B0&f=then%2B0#section1%2B0"))
                                         .Scheme(new AsciiString("http"));

        TaskCompletionSource writePromise = NewPromise();
        VerifyHeadersOnly(http2Headers, writePromise, WriteAndFlush(request, writePromise));
    }

    [Fact]
    public async Task TestAbsoluteFormRequestTargetHandledFromHeaders()
    {
        await BootstrapEnv(2, 1, 0);
        IFullHttpRequest request = new DefaultFullHttpRequest(HTTP_1_1, GET, "/pub/WWW/TheProject.html");
        HttpHeaders httpHeaders = request.Headers();
        httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 5);
        httpHeaders.Set(HttpHeaderNames.HOST, "foouser@www.example.org:5555");
        httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.PATH.Text(), "ignored_path");
        httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text(), "https");
        IHttp2Headers http2Headers =
                new DefaultHttp2Headers().Method(new AsciiString("GET"))
                .Path(new AsciiString("/pub/WWW/TheProject.html"))
                .Authority(new AsciiString("www.example.org:5555")).Scheme(new AsciiString("https"));

        TaskCompletionSource writePromise = NewPromise();
        VerifyHeadersOnly(http2Headers, writePromise, WriteAndFlush(request, writePromise));
    }

    [Fact]
    public async Task TestAbsoluteFormRequestTargetHandledFromRequestTargetUri()
    {
        await BootstrapEnv(2, 1, 0);
        IFullHttpRequest request = new DefaultFullHttpRequest(HTTP_1_1, GET,
                "http://foouser@www.example.org:5555/pub/WWW/TheProject.html");
        HttpHeaders httpHeaders = request.Headers();
        httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 5);
        IHttp2Headers http2Headers =
                new DefaultHttp2Headers().Method(new AsciiString("GET"))
                .Path(new AsciiString("/pub/WWW/TheProject.html"))
                .Authority(new AsciiString("www.example.org:5555")).Scheme(new AsciiString("http"));

        TaskCompletionSource writePromise = NewPromise();
        VerifyHeadersOnly(http2Headers, writePromise, WriteAndFlush(request, writePromise));
    }

    [Fact]
    public async Task TestAuthorityFormRequestTargetHandled()
    {
        await BootstrapEnv(2, 1, 0);
        // https://datatracker.ietf.org/doc/html/rfc9112#section-3.2.3 : the request-target for CONNECT is
        // authority-form, i.e. host:port, not an absolute-form URI with a scheme.
        IFullHttpRequest request = new DefaultFullHttpRequest(HTTP_1_1, CONNECT, "www.example.com:80");
        HttpHeaders httpHeaders = request.Headers();
        httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 5);
        httpHeaders.Set(HttpHeaderNames.HOST, "www.example.com:80");
        // https://datatracker.ietf.org/doc/html/rfc9113#section-8.5 : HTTP/2 CONNECT requests must omit
        // :scheme and :path, and carry the tunnel target in :authority.
        IHttp2Headers http2Headers =
                new DefaultHttp2Headers().Method(new AsciiString("CONNECT"))
                .Authority(new AsciiString("www.example.com:80"));

        TaskCompletionSource writePromise = NewPromise();
        VerifyHeadersOnly(http2Headers, writePromise, WriteAndFlush(request, writePromise));
    }

    [Fact]
    public async Task TestAuthorityFormRequestTargetIgnoresConflictingHostHeader()
    {
        await BootstrapEnv(2, 1, 0);
        // A conflicting Host header must not override the CONNECT authority-form request-target when
        // converting to HTTP/2, otherwise the HTTP/2 :authority (tunnel target) could disagree with the
        // request-target a proxy/gateway validated against policy.
        IFullHttpRequest request = new DefaultFullHttpRequest(HTTP_1_1, CONNECT, "trusted.example:443");
        HttpHeaders httpHeaders = request.Headers();
        httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 5);
        httpHeaders.Set(HttpHeaderNames.HOST, "attacker.example:443");
        IHttp2Headers http2Headers =
                new DefaultHttp2Headers().Method(new AsciiString("CONNECT"))
                .Authority(new AsciiString("trusted.example:443"));

        TaskCompletionSource writePromise = NewPromise();
        VerifyHeadersOnly(http2Headers, writePromise, WriteAndFlush(request, writePromise));
    }

    [Fact]
    public async Task TestAsterikFormRequestTargetHandled()
    {
        await BootstrapEnv(2, 1, 0);
        IFullHttpRequest request = new DefaultFullHttpRequest(HTTP_1_1, OPTIONS, "*");
        HttpHeaders httpHeaders = request.Headers();
        httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 5);
        httpHeaders.Set(HttpHeaderNames.HOST, "www.example.com:80");
        httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text(), "http");
        IHttp2Headers http2Headers =
                new DefaultHttp2Headers().Method(new AsciiString("OPTIONS")).Path(new AsciiString("*"))
                .Scheme(new AsciiString("http")).Authority(new AsciiString("www.example.com:80"));

        TaskCompletionSource writePromise = NewPromise();
        VerifyHeadersOnly(http2Headers, writePromise, WriteAndFlush(request, writePromise));
    }

    [Fact]
    public async Task TestHostIPv6FormRequestTargetHandled()
    {
        // Valid according to
        // https://tools.ietf.org/html/rfc7230#section-2.7.1 -> https://tools.ietf.org/html/rfc3986#section-3.2.2
        await BootstrapEnv(2, 1, 0);
        IFullHttpRequest request = new DefaultFullHttpRequest(HTTP_1_1, GET, "/");
        HttpHeaders httpHeaders = request.Headers();
        httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 5);
        httpHeaders.Set(HttpHeaderNames.HOST, "[::1]:80");
        httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text(), "http");
        IHttp2Headers http2Headers =
                new DefaultHttp2Headers().Method(new AsciiString("GET")).Path(new AsciiString("/"))
                .Scheme(new AsciiString("http")).Authority(new AsciiString("[::1]:80"));

        TaskCompletionSource writePromise = NewPromise();
        VerifyHeadersOnly(http2Headers, writePromise, WriteAndFlush(request, writePromise));
    }

    [Fact]
    public async Task TestHostFormRequestTargetHandled()
    {
        await BootstrapEnv(2, 1, 0);
        IFullHttpRequest request = new DefaultFullHttpRequest(HTTP_1_1, GET, "/");
        HttpHeaders httpHeaders = request.Headers();
        httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 5);
        httpHeaders.Set(HttpHeaderNames.HOST, "localhost:80");
        httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text(), "http");
        IHttp2Headers http2Headers =
                new DefaultHttp2Headers().Method(new AsciiString("GET")).Path(new AsciiString("/"))
                .Scheme(new AsciiString("http")).Authority(new AsciiString("localhost:80"));

        TaskCompletionSource writePromise = NewPromise();
        VerifyHeadersOnly(http2Headers, writePromise, WriteAndFlush(request, writePromise));
    }

    [Fact]
    public async Task TestHostIPv4FormRequestTargetHandled()
    {
        await BootstrapEnv(2, 1, 0);
        IFullHttpRequest request = new DefaultFullHttpRequest(HTTP_1_1, GET, "/");
        HttpHeaders httpHeaders = request.Headers();
        httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 5);
        httpHeaders.Set(HttpHeaderNames.HOST, "1.2.3.4:80");
        httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text(), "http");
        IHttp2Headers http2Headers =
                new DefaultHttp2Headers().Method(new AsciiString("GET")).Path(new AsciiString("/"))
                .Scheme(new AsciiString("http")).Authority(new AsciiString("1.2.3.4:80"));

        TaskCompletionSource writePromise = NewPromise();
        VerifyHeadersOnly(http2Headers, writePromise, WriteAndFlush(request, writePromise));
    }

    [Fact]
    public async Task TestNoSchemeRequestTargetHandled()
    {
        await BootstrapEnv(2, 1, 0);
        IFullHttpRequest request = new DefaultFullHttpRequest(HTTP_1_1, GET, "/");
        HttpHeaders httpHeaders = request.Headers();
        httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 5);
        httpHeaders.Set(HttpHeaderNames.HOST, "localhost");
        TaskCompletionSource writePromise = NewPromise();
        Task writeFuture = WriteAndFlush(request, writePromise);

        Assert.True(AwaitUninterruptibly(writePromise.Task, TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
        Assert.True(writePromise.Task.IsCompleted);
        Assert.False(writePromise.Task.IsCompletedSuccessfully);
        Assert.True(writeFuture.IsCompleted);
        Assert.False(writeFuture.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task TestInvalidStreamId()
    {
        await BootstrapEnv(2, 1, 0);
        IFullHttpRequest request = new DefaultFullHttpRequest(HTTP_1_1, POST, "/foo",
                Unpooled.CopiedBuffer("foobar", Encoding.UTF8));
        HttpHeaders httpHeaders = request.Headers();
        httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), -1);
        httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text(), "http");
        httpHeaders.Set(HttpHeaderNames.HOST, "localhost");
        TaskCompletionSource writePromise = NewPromise();
        Task writeFuture = WriteAndFlush(request, writePromise);

        Assert.True(AwaitUninterruptibly(writePromise.Task, TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
        Assert.True(writePromise.Task.IsCompleted);
        Assert.False(writePromise.Task.IsCompletedSuccessfully);
        Exception cause = Http2CodecUtil.Cause(writePromise.Task);
        Assert.IsAssignableFrom<Http2NoMoreStreamIdsException>(cause);

        Assert.True(writeFuture.IsCompleted);
        Assert.False(writeFuture.IsCompletedSuccessfully);
        cause = Http2CodecUtil.Cause(writeFuture);
        Assert.IsAssignableFrom<Http2NoMoreStreamIdsException>(cause);
    }

    [Fact]
    public async Task TestRequestWithBody()
    {
        string text = "foooooogoooo";
        List<string> receivedBuffers = new List<string>();
        serverListener.Setup(l => l.OnDataRead(It.IsAny<IChannelHandlerContext>(), 3,
                It.IsAny<ByteBuf>(), 0, true))
            .Callback((IChannelHandlerContext ctx, int streamId, ByteBuf data, int padding, bool endOfStream) =>
            {
                lock (receivedBuffers)
                {
                    receivedBuffers.Add(data.ToString(Encoding.UTF8));
                }
            })
            .Returns(0);
        await BootstrapEnv(3, 1, 0);
        IFullHttpRequest request = new DefaultFullHttpRequest(HTTP_1_1, POST,
                "http://your_user-name123@www.example.org:5555/example",
                Unpooled.CopiedBuffer(text, Encoding.UTF8));
        HttpHeaders httpHeaders = request.Headers();
        httpHeaders.Set(HttpHeaderNames.HOST, "www.example-origin.org:5555");
        httpHeaders.Add(Of("foo"), Of("goo"));
        httpHeaders.Add(Of("foo"), Of("goo2"));
        httpHeaders.Add(Of("foo2"), Of("goo2"));
        IHttp2Headers http2Headers =
                new DefaultHttp2Headers().Method(new AsciiString("POST")).Path(new AsciiString("/example"))
                .Authority(new AsciiString("www.example.org:5555")).Scheme(new AsciiString("http"))
                .Add(new AsciiString("foo"), new AsciiString("goo"))
                .Add(new AsciiString("foo"), new AsciiString("goo2"))
                .Add(new AsciiString("foo2"), new AsciiString("goo2"));
        TaskCompletionSource writePromise = NewPromise();
        Task writeFuture = WriteAndFlush(request, writePromise);

        Assert.True(AwaitUninterruptibly(writePromise.Task, TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
        Assert.True(writePromise.Task.IsCompletedSuccessfully);
        Assert.True(AwaitUninterruptibly(writeFuture, TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
        Assert.True(writeFuture.IsCompletedSuccessfully);
        AwaitRequests();
        serverListener.Verify(l => l.OnHeadersRead(It.IsAny<IChannelHandlerContext>(), 3, http2Headers, 0,
                It.IsAny<short>(), It.IsAny<bool>(), 0, false), Times.Once());
        serverListener.Verify(l => l.OnDataRead(It.IsAny<IChannelHandlerContext>(), 3, It.IsAny<ByteBuf>(), 0,
                true), Times.Once());

        lock (receivedBuffers)
        {
            Assert.Equal(1, receivedBuffers.Count);
            Assert.Equal(text, receivedBuffers[0]);
        }
    }

    [Fact]
    public async Task TestRequestWithBodyAndTrailingHeaders()
    {
        string text = "foooooogoooo";
        List<string> receivedBuffers = new List<string>();
        serverListener.Setup(l => l.OnDataRead(It.IsAny<IChannelHandlerContext>(), 3,
                It.IsAny<ByteBuf>(), 0, false))
            .Callback((IChannelHandlerContext ctx, int streamId, ByteBuf data, int padding, bool endOfStream) =>
            {
                lock (receivedBuffers)
                {
                    receivedBuffers.Add(data.ToString(Encoding.UTF8));
                }
            })
            .Returns(0);
        await BootstrapEnv(4, 1, 1);
        IFullHttpRequest request = new DefaultFullHttpRequest(HTTP_1_1, POST,
                "http://your_user-name123@www.example.org:5555/example",
                Unpooled.CopiedBuffer(text, Encoding.UTF8));
        HttpHeaders httpHeaders = request.Headers();
        httpHeaders.Set(HttpHeaderNames.HOST, "www.example.org:5555");
        httpHeaders.Add(Of("foo"), Of("goo"));
        httpHeaders.Add(Of("foo"), Of("goo2"));
        httpHeaders.Add(Of("foo2"), Of("goo2"));
        IHttp2Headers http2Headers =
                new DefaultHttp2Headers().Method(new AsciiString("POST")).Path(new AsciiString("/example"))
                        .Authority(new AsciiString("www.example.org:5555")).Scheme(new AsciiString("http"))
                        .Add(new AsciiString("foo"), new AsciiString("goo"))
                        .Add(new AsciiString("foo"), new AsciiString("goo2"))
                        .Add(new AsciiString("foo2"), new AsciiString("goo2"));

        request.TrailingHeaders().Add(Of("trailing"), Of("bar"));

        IHttp2Headers http2TrailingHeaders = new DefaultHttp2Headers()
                .Add(new AsciiString("trailing"), new AsciiString("bar"));

        TaskCompletionSource writePromise = NewPromise();
        Task writeFuture = WriteAndFlush(request, writePromise);

        Assert.True(AwaitUninterruptibly(writePromise.Task, TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
        Assert.True(writePromise.Task.IsCompletedSuccessfully);
        Assert.True(AwaitUninterruptibly(writeFuture, TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
        Assert.True(writeFuture.IsCompletedSuccessfully);
        AwaitRequests();
        serverListener.Verify(l => l.OnHeadersRead(It.IsAny<IChannelHandlerContext>(), 3, http2Headers, 0,
                It.IsAny<short>(), It.IsAny<bool>(), 0, false), Times.Once());
        serverListener.Verify(l => l.OnDataRead(It.IsAny<IChannelHandlerContext>(), 3, It.IsAny<ByteBuf>(), 0,
                false), Times.Once());
        serverListener.Verify(l => l.OnHeadersRead(It.IsAny<IChannelHandlerContext>(), 3, http2TrailingHeaders, 0,
                It.IsAny<short>(), It.IsAny<bool>(), 0, true), Times.Once());

        lock (receivedBuffers)
        {
            Assert.Equal(1, receivedBuffers.Count);
            Assert.Equal(text, receivedBuffers[0]);
        }
    }

    [Fact]
    public async Task TestChunkedRequestWithBodyAndTrailingHeaders()
    {
        string text = "foooooo";
        string text2 = "goooo";
        List<string> receivedBuffers = new List<string>();
        serverListener.Setup(l => l.OnDataRead(It.IsAny<IChannelHandlerContext>(), 3,
                It.IsAny<ByteBuf>(), 0, false))
            .Callback((IChannelHandlerContext ctx, int streamId, ByteBuf data, int padding, bool endOfStream) =>
            {
                lock (receivedBuffers)
                {
                    receivedBuffers.Add(data.ToString(Encoding.UTF8));
                }
            })
            .Returns(0);
        await BootstrapEnv(4, 1, 1);
        IHttpRequest request = new DefaultHttpRequest(HTTP_1_1, POST,
                "http://your_user-name123@www.example.org:5555/example");
        HttpHeaders httpHeaders = request.Headers();
        httpHeaders.Set(HttpHeaderNames.HOST, "www.example.org:5555");
        httpHeaders.Add(HttpHeaderNames.TRANSFER_ENCODING, "chunked");
        httpHeaders.Add(Of("foo"), Of("goo"));
        httpHeaders.Add(Of("foo"), Of("goo2"));
        httpHeaders.Add(Of("foo2"), Of("goo2"));
        IHttp2Headers http2Headers =
                new DefaultHttp2Headers().Method(new AsciiString("POST")).Path(new AsciiString("/example"))
                        .Authority(new AsciiString("www.example.org:5555")).Scheme(new AsciiString("http"))
                        .Add(new AsciiString("foo"), new AsciiString("goo"))
                        .Add(new AsciiString("foo"), new AsciiString("goo2"))
                        .Add(new AsciiString("foo2"), new AsciiString("goo2"));

        DefaultHttpContent httpContent = new DefaultHttpContent(Unpooled.CopiedBuffer(text, Encoding.UTF8));
        ILastHttpContent lastHttpContent = new DefaultLastHttpContent(Unpooled.CopiedBuffer(text2, Encoding.UTF8));

        lastHttpContent.TrailingHeaders().Add(Of("trailing"), Of("bar"));

        IHttp2Headers http2TrailingHeaders = new DefaultHttp2Headers()
                .Add(new AsciiString("trailing"), new AsciiString("bar"));

        TaskCompletionSource writePromise = NewPromise();
        Task writeFuture = Write(request, writePromise);
        TaskCompletionSource contentPromise = NewPromise();
        Task contentFuture = Write(httpContent, contentPromise);
        TaskCompletionSource lastContentPromise = NewPromise();
        Task lastContentFuture = Write(lastHttpContent, lastContentPromise);

        clientChannel.Flush();

        Assert.True(AwaitUninterruptibly(writePromise.Task, TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
        Assert.True(writePromise.Task.IsCompletedSuccessfully);
        Assert.True(AwaitUninterruptibly(writeFuture, TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
        Assert.True(writeFuture.IsCompletedSuccessfully);

        Assert.True(AwaitUninterruptibly(contentPromise.Task, TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
        Assert.True(contentPromise.Task.IsCompletedSuccessfully);
        Assert.True(AwaitUninterruptibly(contentFuture, TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
        Assert.True(contentFuture.IsCompletedSuccessfully);

        Assert.True(AwaitUninterruptibly(lastContentPromise.Task, TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
        Assert.True(lastContentPromise.Task.IsCompletedSuccessfully);
        Assert.True(AwaitUninterruptibly(lastContentFuture, TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
        Assert.True(lastContentFuture.IsCompletedSuccessfully);

        AwaitRequests();
        serverListener.Verify(l => l.OnHeadersRead(It.IsAny<IChannelHandlerContext>(), 3, http2Headers, 0,
                It.IsAny<short>(), It.IsAny<bool>(), 0, false), Times.Once());
        // The two HttpContent writes may or may not be coalesced into a single DATA frame by the flow controller
        // depending on timing, so tolerate either 1 or 2 onDataRead(...) invocations and just verify the
        // concatenation of everything that was received matches what was sent.
        serverListener.Verify(l => l.OnDataRead(It.IsAny<IChannelHandlerContext>(), 3,
                It.IsAny<ByteBuf>(), 0, false), Times.AtLeastOnce());
        serverListener.Verify(l => l.OnHeadersRead(It.IsAny<IChannelHandlerContext>(), 3, http2TrailingHeaders, 0,
                It.IsAny<short>(), It.IsAny<bool>(), 0, true), Times.Once());
        StringBuilder receivedText = new StringBuilder();

        lock (receivedBuffers)
        {
            foreach (string buffer in receivedBuffers)
            {
                receivedText.Append(buffer);
            }
        }

        Assert.Equal(text + text2, receivedText.ToString());
    }

    private async Task BootstrapEnv(int requestCountDown, int serverSettingsAckCount, int trailersCount)
    {
        CountdownEvent prefaceWrittenLatch = new CountdownEvent(1);
        CountdownEvent serverChannelLatch = new CountdownEvent(1);
        requestLatch = new CountdownEvent(requestCountDown);
        serverSettingsAckLatch = new CountdownEvent(serverSettingsAckCount);
        trailersLatch = trailersCount == 0 ? null : new CountdownEvent(trailersCount);

        sb = new ServerBootstrap();
        cb = new Bootstrap();

        sb.Group(new MultiThreadIoEventLoopGroup(LocalIoHandler.NewFactory()));
        sb.Channel<LocalServerChannel>();
        sb.ChildHandler(new ServerInitializer(this, serverChannelLatch));

        cb.Group(new MultiThreadIoEventLoopGroup(LocalIoHandler.NewFactory()));
        cb.Channel<LocalChannel>();
        cb.Handler(new ClientInitializer(this, prefaceWrittenLatch));

        serverChannel = await sb.BindAsync(new LocalAddress(GetType()));

        clientChannel = await cb.ConnectAsync(serverChannel.LocalAddress);
        Assert.True(prefaceWrittenLatch.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(serverChannelLatch.Wait(TimeSpan.FromSeconds(WAIT_TIME_SECONDS),
                TestContext.Current.CancellationToken));
    }

    private sealed class ServerInitializer : ChannelInitializer<IChannel>
    {
        private readonly HttpToHttp2ConnectionHandlerTest test;
        private readonly CountdownEvent serverChannelLatch;

        internal ServerInitializer(HttpToHttp2ConnectionHandlerTest test, CountdownEvent serverChannelLatch)
        {
            this.test = test;
            this.serverChannelLatch = serverChannelLatch;
        }

        protected override void InitChannel(IChannel ch)
        {
            test.serverConnectedChannel = ch;
            IChannelPipeline p = ch.Pipeline;
            test.serverFrameCountDown =
                    new FrameCountDown(test.serverListener.Object, test.serverSettingsAckLatch, test.requestLatch,
                            null, test.trailersLatch);
            p.AddLast(new HttpToHttp2ConnectionHandlerBuilder()
                       .Server(true)
                       .FrameListener(test.serverFrameCountDown)
                       .HttpScheme(HttpScheme.HTTP)
                       .Build());
            serverChannelLatch.Signal();
        }
    }

    private sealed class ClientInitializer : ChannelInitializer<IChannel>
    {
        private readonly HttpToHttp2ConnectionHandlerTest test;
        private readonly CountdownEvent prefaceWrittenLatch;

        internal ClientInitializer(HttpToHttp2ConnectionHandlerTest test, CountdownEvent prefaceWrittenLatch)
        {
            this.test = test;
            this.prefaceWrittenLatch = prefaceWrittenLatch;
        }

        protected override void InitChannel(IChannel ch)
        {
            IChannelPipeline p = ch.Pipeline;
            HttpToHttp2ConnectionHandler handler = new HttpToHttp2ConnectionHandlerBuilder()
                    .Server(false)
                    .FrameListener(test.clientListener.Object)
                    .GracefulShutdownTimeoutMillis(0)
                    .Build();
            p.AddLast(handler);
            p.AddLast(new PrefaceWrittenHandler(prefaceWrittenLatch));
        }
    }

    private sealed class PrefaceWrittenHandler : ChannelInboundHandlerAdapter
    {
        private readonly CountdownEvent prefaceWrittenLatch;

        internal PrefaceWrittenHandler(CountdownEvent prefaceWrittenLatch)
        {
            this.prefaceWrittenLatch = prefaceWrittenLatch;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt == Http2ConnectionPrefaceAndSettingsFrameWrittenEvent.INSTANCE)
            {
                prefaceWrittenLatch.Signal();
                ctx.Pipeline.Remove(this);
            }
        }
    }

    private void VerifyHeadersOnly(IHttp2Headers expected, TaskCompletionSource writePromise, Task writeFuture)
    {
        Assert.True(AwaitUninterruptibly(writePromise.Task, TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
        Assert.True(writePromise.Task.IsCompletedSuccessfully);
        Assert.True(AwaitUninterruptibly(writeFuture, TimeSpan.FromSeconds(WAIT_TIME_SECONDS)));
        Assert.True(writeFuture.IsCompletedSuccessfully);
        AwaitRequests();
        serverListener.Verify(l => l.OnHeadersRead(It.IsAny<IChannelHandlerContext>(), 5,
                expected, 0, It.IsAny<short>(), It.IsAny<bool>(), 0, true), Times.Once());
        serverListener.Verify(l => l.OnDataRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<ByteBuf>(), It.IsAny<int>(), It.IsAny<bool>()), Times.Never());
    }

    private void AwaitRequests()
    {
        Assert.True(requestLatch.Wait(TimeSpan.FromSeconds(WAIT_TIME_SECONDS), TestContext.Current.CancellationToken));

        if (trailersLatch != null)
        {
            Assert.True(trailersLatch.Wait(TimeSpan.FromSeconds(WAIT_TIME_SECONDS),
                    TestContext.Current.CancellationToken));
        }

        Assert.True(serverSettingsAckLatch.Wait(TimeSpan.FromSeconds(WAIT_TIME_SECONDS),
                TestContext.Current.CancellationToken));
    }

    private IChannelHandlerContext Ctx()
    {
        return clientChannel.Pipeline.FirstContext;
    }

    private TaskCompletionSource NewPromise()
    {
        return Http2CodecUtil.NewPromise();
    }

    // CLR: channel.write(msg, promise) (see the class comment).
    private Task Write(object msg, TaskCompletionSource promise)
    {
        Http2CodecUtil.Cascade(Ctx(), clientChannel.WriteAsync(msg), promise);
        return promise.Task;
    }

    // CLR: channel.writeAndFlush(msg, promise) (see the class comment).
    private Task WriteAndFlush(object msg, TaskCompletionSource promise)
    {
        Http2CodecUtil.Cascade(Ctx(), clientChannel.WriteAndFlushAsync(msg), promise);
        return promise.Task;
    }

    // CLR: Future.awaitUninterruptibly(timeout) (see the class comment).
    private static bool AwaitUninterruptibly(Task future, TimeSpan timeout)
    {
        return Task.WhenAny(future, Task.Delay(timeout)).GetAwaiter().GetResult() == future;
    }
}
