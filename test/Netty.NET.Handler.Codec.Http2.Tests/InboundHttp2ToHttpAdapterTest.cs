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
using System.Linq;
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
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;
using static Netty.NET.Handler.Codec.Http2.Tests.Http2TestUtil;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Testing the {@link InboundHttp2ToHttpAdapter} and base class {@link InboundHttp2ToHttpAdapter} for HTTP/2
 * frames into {@link HttpObject}s
 */
// CLR: the @Mock listeners are Moq mocks created with the test instance (MockitoAnnotations.initMocks in @BeforeEach);
// verify(..., times(n)) is Verify(..., Times.Once()/Exactly(n)) and an ArgumentCaptor's values are the arguments of the
// recorded MessageReceived invocations (CapturedMessages; getValue() is the last one)
// (https://github.com/devlooped/moq/wiki/Quickstart). Castle DynamicProxy, which Moq uses, cannot implement a private
// interface, so the private HttpResponseListener/HttpSettingsListener are public nested interfaces. @AfterEach is
// IAsyncLifetime.DisposeAsync, which awaits the close Tasks and the graceful shutdown of the event loop groups
// (syncUninterruptibly() is awaiting the Task), as Http2ConnectionRoundtripTest does. CountDownLatch is CountdownEvent
// and await(5, SECONDS) Wait(5 s); latch.countDown() is CountDown(latch), which stops at zero as CountDownLatch does
// (CountdownEvent.Signal throws at zero). The anonymous Http2Runnable instances are the nested ActionRunnable over a lambda;
// the anonymous ChannelInitializer/ChannelHandlerAdapter/ChannelInboundHandlerAdapter classes are nested classes.
// ctx.newPromise() is Http2CodecUtil.NewPromise() and the encoder's returned Tasks are discarded, as Java ignores the
// futures. MultiThreadIoEventLoopGroup/LocalIoHandler/LocalServerChannel/LocalChannel/ServerBootstrap/Bootstrap are the
// Transport ports; bind(...).sync().channel() and connect(...).awaitUninterruptibly() are BindAsync/ConnectAsync, and new
// LocalAddress(getClass()) is LocalAddress(Type). assertEquals(request, captured) compares with Equals
// (DefaultFullHttpRequest/DefaultFullHttpResponse keep Java's equals); assertEquals of two Http2Settings calls equals,
// which AssertSettingsEquals keeps (xUnit would compare the dictionaries entry by entry). String.getBytes() with the
// default charset is Encoding.UTF8 (the texts are ASCII); "çã".getBytes(UTF_8) is Encoding.UTF8.GetBytes.
// FullHttpMessage.replace(ByteBuf) returns IByteBufHolder (narrowing re-declarations are inherited,
// docs/codec-http-porting.md), so its result is cast to IFullHttpMessage. SimpleChannelInboundHandler<HttpObject> is
// SimpleChannelInboundHandler<IHttpObject>. The @Deprecated DefaultFullHttpRequest(..., boolean validateHeaders) and
// DefaultFullHttpResponse(..., boolean validateHeaders) constructors are not ported in codec-http (their @deprecated
// texts name the HttpHeadersFactory constructors; docs/codec-http-porting.md, Core not ported); every call here passes
// true, which gives headersFactory()/trailersFactory() with validation, the factories of the constructors without the
// flag, so those constructors are used.
public class InboundHttp2ToHttpAdapterTest : IAsyncLifetime
{
    private IList<IFullHttpMessage> capturedRequests;
    private IList<IFullHttpMessage> capturedResponses;

    private readonly Mock<IHttpResponseListener> serverListener = new Mock<IHttpResponseListener>();

    private readonly Mock<IHttpResponseListener> clientListener = new Mock<IHttpResponseListener>();

    private readonly Mock<IHttpSettingsListener> settingsListener = new Mock<IHttpSettingsListener>();

    private Http2ConnectionHandler serverHandler;
    private Http2ConnectionHandler clientHandler;
    private ServerBootstrap sb;
    private Bootstrap cb;
    private IChannel serverChannel;
    private volatile IChannel serverConnectedChannel;
    private IChannel clientChannel;
    private CountdownEvent serverLatch;
    private CountdownEvent clientLatch;
    private CountdownEvent serverLatch2;
    private CountdownEvent clientLatch2;
    private CountdownEvent settingsLatch;
    private CountdownEvent clientHandlersAddedLatch;
    private int maxContentLength;
    private HttpResponseDelegator serverDelegator;
    private HttpResponseDelegator clientDelegator;
    private HttpSettingsDelegator settingsDelegator;
    private Http2Exception clientException;

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        CleanupCapturedRequests();
        CleanupCapturedResponses();
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
    public async Task ClientRequestSingleHeaderNoDataFrames()
    {
        await BoostrapEnv(1, 1, 1);
        IFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET,
                "/some/path/resource2");
        try
        {
            HttpHeaders httpHeaders = request.Headers();
            httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text(), "https");
            httpHeaders.Set(HttpHeaderNames.HOST, "example.org");
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 3);
            httpHeaders.SetInt(HttpHeaderNames.CONTENT_LENGTH, 0);
            httpHeaders.SetShort(HttpConversionUtil.ExtensionHeaderNames.STREAM_WEIGHT.Text(), (short) 16);
            IHttp2Headers http2Headers = new DefaultHttp2Headers().Method(new AsciiString("GET")).
                    Scheme(new AsciiString("https")).Authority(new AsciiString("example.org"))
                    .Path(new AsciiString("/some/path/resource2"));
            RunInChannel(clientChannel, new ActionRunnable(() =>
            {
                _ = clientHandler.Encoder().WriteHeadersAsync(CtxClient(), 3, http2Headers, 0, true, NewPromiseClient());
                clientChannel.Flush();
            }));
            AwaitRequests();
            serverListener.Verify(l => l.MessageReceived(It.IsAny<IHttpObject>()), Times.Once());
            capturedRequests = CapturedMessages(serverListener);
            Assert.Equal<object>(request, capturedRequests[0]);
        }
        finally
        {
            request.Release();
        }
    }

    [Fact]
    public async Task ClientRequestSingleHeaderCookieSplitIntoMultipleEntries()
    {
        await BoostrapEnv(1, 1, 1);
        IFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET,
                "/some/path/resource2");
        try
        {
            HttpHeaders httpHeaders = request.Headers();
            httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text(), "https");
            httpHeaders.Set(HttpHeaderNames.HOST, "example.org");
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 3);
            httpHeaders.SetInt(HttpHeaderNames.CONTENT_LENGTH, 0);
            httpHeaders.Set(HttpHeaderNames.COOKIE, "a=b; c=d; e=f");
            httpHeaders.SetShort(HttpConversionUtil.ExtensionHeaderNames.STREAM_WEIGHT.Text(), (short) 16);
            IHttp2Headers http2Headers = new DefaultHttp2Headers().Method(new AsciiString("GET")).
                    Scheme(new AsciiString("https")).Authority(new AsciiString("example.org"))
                    .Path(new AsciiString("/some/path/resource2"))
                    .Add(HttpHeaderNames.COOKIE, Of("a=b"))
                    .Add(HttpHeaderNames.COOKIE, Of("c=d"))
                    .Add(HttpHeaderNames.COOKIE, Of("e=f"));
            RunInChannel(clientChannel, new ActionRunnable(() =>
            {
                _ = clientHandler.Encoder().WriteHeadersAsync(CtxClient(), 3, http2Headers, 0, true, NewPromiseClient());
                clientChannel.Flush();
            }));
            AwaitRequests();
            serverListener.Verify(l => l.MessageReceived(It.IsAny<IHttpObject>()), Times.Once());
            capturedRequests = CapturedMessages(serverListener);
            Assert.Equal<object>(request, capturedRequests[0]);
        }
        finally
        {
            request.Release();
        }
    }

    [Fact]
    public async Task ClientRequestSingleHeaderCookieSplitIntoMultipleEntries2()
    {
        await BoostrapEnv(1, 1, 1);
        IFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET,
                "/some/path/resource2");
        try
        {
            HttpHeaders httpHeaders = request.Headers();
            httpHeaders.Set(HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text(), "https");
            httpHeaders.Set(HttpHeaderNames.HOST, "example.org");
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 3);
            httpHeaders.SetInt(HttpHeaderNames.CONTENT_LENGTH, 0);
            httpHeaders.Set(HttpHeaderNames.COOKIE, "a=b; c=d; e=f");
            httpHeaders.SetShort(HttpConversionUtil.ExtensionHeaderNames.STREAM_WEIGHT.Text(), (short) 16);
            IHttp2Headers http2Headers = new DefaultHttp2Headers().Method(new AsciiString("GET")).
                    Scheme(new AsciiString("https")).Authority(new AsciiString("example.org"))
                    .Path(new AsciiString("/some/path/resource2"))
                    .Add(HttpHeaderNames.COOKIE, Of("a=b; c=d"))
                    .Add(HttpHeaderNames.COOKIE, Of("e=f"));
            RunInChannel(clientChannel, new ActionRunnable(() =>
            {
                _ = clientHandler.Encoder().WriteHeadersAsync(CtxClient(), 3, http2Headers, 0, true, NewPromiseClient());
                clientChannel.Flush();
            }));
            AwaitRequests();
            serverListener.Verify(l => l.MessageReceived(It.IsAny<IHttpObject>()), Times.Once());
            capturedRequests = CapturedMessages(serverListener);
            Assert.Equal<object>(request, capturedRequests[0]);
        }
        finally
        {
            request.Release();
        }
    }

    [Fact]
    public async Task ClientRequestSingleHeaderNonAsciiShouldThrow()
    {
        await BoostrapEnv(1, 1, 1);
        // Disable validation on the client side so the non-ASCII header name reaches the wire; the
        // server-side validation (RFC 9113 §8.2.1 requires field names to be valid HTTP/1.1 tokens)
        // then rejects it, which surfaces as a stream error.
        IHttp2Headers http2Headers = new DefaultHttp2Headers(false)
                .Method(new AsciiString("GET"))
                .Scheme(new AsciiString("https"))
                .Authority(new AsciiString("example.org"))
                .Path(new AsciiString("/some/path/resource2"))
                .Add(new AsciiString(Encoding.UTF8.GetBytes("çã")),
                        new AsciiString(Encoding.UTF8.GetBytes("Ãã")));
        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            _ = clientHandler.Encoder().WriteHeadersAsync(CtxClient(), 3, http2Headers, 0, true, NewPromiseClient());
            clientChannel.Flush();
        }));
        AwaitResponses();
        Assert.True(IsStreamError(clientException));
    }

    [Fact]
    public async Task ClientRequestOneDataFrame()
    {
        await BoostrapEnv(1, 1, 1);
        string text = "hello world";
        ByteBuf content = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes(text));
        IFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET,
                "/some/path/resource2", content);
        try
        {
            HttpHeaders httpHeaders = request.Headers();
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 3);
            httpHeaders.SetInt(HttpHeaderNames.CONTENT_LENGTH, text.Length);
            httpHeaders.SetShort(HttpConversionUtil.ExtensionHeaderNames.STREAM_WEIGHT.Text(), (short) 16);
            IHttp2Headers http2Headers = new DefaultHttp2Headers().Method(new AsciiString("GET")).Path(
                    new AsciiString("/some/path/resource2"));
            RunInChannel(clientChannel, new ActionRunnable(() =>
            {
                _ = clientHandler.Encoder().WriteHeadersAsync(CtxClient(), 3, http2Headers, 0, false, NewPromiseClient());
                _ = clientHandler.Encoder().WriteDataAsync(CtxClient(), 3, content.RetainedDuplicate(), 0, true,
                                                  NewPromiseClient());
                clientChannel.Flush();
            }));
            AwaitRequests();
            serverListener.Verify(l => l.MessageReceived(It.IsAny<IHttpObject>()), Times.Once());
            capturedRequests = CapturedMessages(serverListener);
            Assert.Equal<object>(request, capturedRequests[0]);
        }
        finally
        {
            request.Release();
        }
    }

    [Fact]
    public async Task ExceedMaxContentLengthShouldCauseStreamErrorNotConnectionError()
    {
        // Verify that exceeding maxContentLength causes a stream error (RST_STREAM)
        // not a connection error (GOAWAY), so other streams can continue.
        // This is the fix for https://github.com/netty/netty/issues/11994
        //
        // clientLatch=1: RST_STREAM for stream 3 triggers onRstStreamRead on the client,
        //   which fires exceptionCaught and counts down clientLatch.
        //   With a connection error (GOAWAY), no RST_STREAM is sent, so clientLatch
        //   never counts down and awaitResponses() times out — failing the test.
        // serverLatch=1: stream 5 request should be delivered normally.
        await BoostrapEnv(1, 1, 1);
        byte[] oversizedData = new byte[maxContentLength + 1];
        ByteBuf oversizedContent = Unpooled.WrappedBuffer(oversizedData);
        string normalText = "hello";
        ByteBuf normalContent = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes(normalText));
        IFullHttpRequest expectedRequest = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET,
                "/normal/path", normalContent.Copy());
        try
        {
            HttpHeaders httpHeaders = expectedRequest.Headers();
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 5);
            httpHeaders.SetInt(HttpHeaderNames.CONTENT_LENGTH, normalText.Length);
            httpHeaders.SetShort(HttpConversionUtil.ExtensionHeaderNames.STREAM_WEIGHT.Text(), (short) 16);
            IHttp2Headers oversizedHeaders = new DefaultHttp2Headers().Method(new AsciiString("POST")).Path(
                    new AsciiString("/oversized/path"));
            IHttp2Headers normalHeaders = new DefaultHttp2Headers().Method(new AsciiString("GET")).Path(
                    new AsciiString("/normal/path"));
            RunInChannel(clientChannel, new ActionRunnable(() =>
            {
                // Stream 3: send data exceeding maxContentLength - should cause stream error
                _ = clientHandler.Encoder().WriteHeadersAsync(CtxClient(), 3, oversizedHeaders, 0, false,
                        NewPromiseClient());
                _ = clientHandler.Encoder().WriteDataAsync(CtxClient(), 3, oversizedContent, 0, true,
                        NewPromiseClient());
                // Stream 5: send a normal request - should succeed if connection is still alive
                _ = clientHandler.Encoder().WriteHeadersAsync(CtxClient(), 5, normalHeaders, 0, false,
                        NewPromiseClient());
                _ = clientHandler.Encoder().WriteDataAsync(CtxClient(), 5, normalContent, 0, true,
                        NewPromiseClient());
                clientChannel.Flush();
            }));

            // Verify stream 5 is delivered successfully on the server
            AwaitRequests();
            serverListener.Verify(l => l.MessageReceived(It.IsAny<IHttpObject>()), Times.Once());
            capturedRequests = CapturedMessages(serverListener);
            Assert.Equal<object>(expectedRequest, capturedRequests[0]);

            // Verify the client received RST_STREAM (not GOAWAY) for the oversized stream.
            // The server's onStreamError sends RST_STREAM, which the client's
            // InboundHttp2ToHttpAdapter.onRstStreamRead translates into an exceptionCaught
            // event carrying a StreamException — this counts down clientLatch.
            // With a connection error, the server sends GOAWAY instead — no RST_STREAM is
            // received, exceptionCaught never fires, and awaitResponses() times out.
            AwaitResponses();
            Assert.NotNull(clientException);
            Assert.True(IsStreamError(clientException));
            Http2Exception.StreamException streamEx = (Http2Exception.StreamException) clientException;
            Assert.Equal(3, streamEx.StreamId());
            Assert.Equal(Http2Error.ENHANCE_YOUR_CALM, streamEx.Error());
        }
        finally
        {
            expectedRequest.Release();
        }
    }

    [Fact]
    public async Task ClientRequestMultipleDataFrames()
    {
        await BoostrapEnv(1, 1, 1);
        string text = "hello world big time data!";
        ByteBuf content = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes(text));
        IFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET,
                "/some/path/resource2", content);
        try
        {
            HttpHeaders httpHeaders = request.Headers();
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 3);
            httpHeaders.SetInt(HttpHeaderNames.CONTENT_LENGTH, text.Length);
            httpHeaders.SetShort(HttpConversionUtil.ExtensionHeaderNames.STREAM_WEIGHT.Text(), (short) 16);
            IHttp2Headers http2Headers = new DefaultHttp2Headers().Method(new AsciiString("GET")).Path(
                    new AsciiString("/some/path/resource2"));
            int midPoint = text.Length / 2;
            RunInChannel(clientChannel, new ActionRunnable(() =>
            {
                _ = clientHandler.Encoder().WriteHeadersAsync(CtxClient(), 3, http2Headers, 0, false, NewPromiseClient());
                _ = clientHandler.Encoder().WriteDataAsync(
                        CtxClient(), 3, content.RetainedSlice(0, midPoint), 0, false, NewPromiseClient());
                _ = clientHandler.Encoder().WriteDataAsync(
                        CtxClient(), 3, content.RetainedSlice(midPoint, text.Length - midPoint),
                        0, true, NewPromiseClient());
                clientChannel.Flush();
            }));
            AwaitRequests();
            serverListener.Verify(l => l.MessageReceived(It.IsAny<IHttpObject>()), Times.Once());
            capturedRequests = CapturedMessages(serverListener);
            Assert.Equal<object>(request, capturedRequests[0]);
        }
        finally
        {
            request.Release();
        }
    }

    [Fact]
    public async Task ClientRequestMultipleEmptyDataFrames()
    {
        await BoostrapEnv(1, 1, 1);
        string text = "";
        ByteBuf content = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes(text));
        IFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET,
                "/some/path/resource2", content);
        try
        {
            HttpHeaders httpHeaders = request.Headers();
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 3);
            httpHeaders.SetInt(HttpHeaderNames.CONTENT_LENGTH, text.Length);
            httpHeaders.SetShort(HttpConversionUtil.ExtensionHeaderNames.STREAM_WEIGHT.Text(), (short) 16);
            IHttp2Headers http2Headers = new DefaultHttp2Headers().Method(new AsciiString("GET")).Path(
                    new AsciiString("/some/path/resource2"));
            RunInChannel(clientChannel, new ActionRunnable(() =>
            {
                _ = clientHandler.Encoder().WriteHeadersAsync(CtxClient(), 3, http2Headers, 0, false, NewPromiseClient());
                _ = clientHandler.Encoder().WriteDataAsync(CtxClient(), 3, content.Retain(), 0, false, NewPromiseClient());
                _ = clientHandler.Encoder().WriteDataAsync(CtxClient(), 3, content.Retain(), 0, false, NewPromiseClient());
                _ = clientHandler.Encoder().WriteDataAsync(CtxClient(), 3, content.Retain(), 0, true, NewPromiseClient());
                clientChannel.Flush();
            }));
            AwaitRequests();
            serverListener.Verify(l => l.MessageReceived(It.IsAny<IHttpObject>()), Times.Once());
            capturedRequests = CapturedMessages(serverListener);
            Assert.Equal<object>(request, capturedRequests[0]);
        }
        finally
        {
            request.Release();
        }
    }

    [Fact]
    public async Task ClientRequestTrailingHeaders()
    {
        await BoostrapEnv(1, 1, 1);
        string text = "some data";
        ByteBuf content = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes(text));
        IFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET,
                "/some/path/resource2", content);
        try
        {
            HttpHeaders httpHeaders = request.Headers();
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 3);
            httpHeaders.SetInt(HttpHeaderNames.CONTENT_LENGTH, text.Length);
            httpHeaders.SetShort(HttpConversionUtil.ExtensionHeaderNames.STREAM_WEIGHT.Text(), (short) 16);
            HttpHeaders trailingHeaders = request.TrailingHeaders();
            trailingHeaders.Set(Of("Foo"), Of("goo"));
            trailingHeaders.Set(Of("fOo2"), Of("goo2"));
            trailingHeaders.Add(Of("foO2"), Of("goo3"));
            IHttp2Headers http2Headers = new DefaultHttp2Headers().Method(new AsciiString("GET")).Path(
                    new AsciiString("/some/path/resource2"));
            IHttp2Headers http2Headers2 = new DefaultHttp2Headers()
                    .Set(new AsciiString("foo"), new AsciiString("goo"))
                    .Set(new AsciiString("foo2"), new AsciiString("goo2"))
                    .Add(new AsciiString("foo2"), new AsciiString("goo3"));
            RunInChannel(clientChannel, new ActionRunnable(() =>
            {
                _ = clientHandler.Encoder().WriteHeadersAsync(CtxClient(), 3, http2Headers, 0, false, NewPromiseClient());
                _ = clientHandler.Encoder().WriteDataAsync(CtxClient(), 3, content.RetainedDuplicate(), 0, false,
                                                  NewPromiseClient());
                _ = clientHandler.Encoder().WriteHeadersAsync(CtxClient(), 3, http2Headers2, 0, true, NewPromiseClient());
                clientChannel.Flush();
            }));
            AwaitRequests();
            serverListener.Verify(l => l.MessageReceived(It.IsAny<IHttpObject>()), Times.Once());
            capturedRequests = CapturedMessages(serverListener);
            Assert.Equal<object>(request, capturedRequests[0]);
        }
        finally
        {
            request.Release();
        }
    }

    [Fact]
    public async Task ClientRequestStreamDependencyInHttpMessageFlow()
    {
        await BoostrapEnv(1, 2, 1);
        string text = "hello world big time data!";
        ByteBuf content = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes(text));
        string text2 = "hello world big time data...number 2!!";
        ByteBuf content2 = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes(text2));
        IFullHttpRequest request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.PUT,
                "/some/path/resource", content);
        IFullHttpMessage request2 = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.PUT,
                "/some/path/resource2", content2);
        try
        {
            HttpHeaders httpHeaders = request.Headers();
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 3);
            httpHeaders.SetInt(HttpHeaderNames.CONTENT_LENGTH, text.Length);
            httpHeaders.SetShort(HttpConversionUtil.ExtensionHeaderNames.STREAM_WEIGHT.Text(), (short) 16);
            HttpHeaders httpHeaders2 = request2.Headers();
            httpHeaders2.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 5);
            httpHeaders2.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_DEPENDENCY_ID.Text(), 3);
            httpHeaders2.SetShort(HttpConversionUtil.ExtensionHeaderNames.STREAM_WEIGHT.Text(), (short) 123);
            httpHeaders2.SetInt(HttpHeaderNames.CONTENT_LENGTH, text2.Length);
            IHttp2Headers http2Headers = new DefaultHttp2Headers().Method(new AsciiString("PUT")).Path(
                    new AsciiString("/some/path/resource"));
            IHttp2Headers http2Headers2 = new DefaultHttp2Headers().Method(new AsciiString("PUT")).Path(
                    new AsciiString("/some/path/resource2"));
            RunInChannel(clientChannel, new ActionRunnable(() =>
            {
                _ = clientHandler.Encoder().WriteHeadersAsync(CtxClient(), 3, http2Headers, 0, false, NewPromiseClient());
                _ = clientHandler.Encoder().WriteHeadersAsync(CtxClient(), 5, http2Headers2, 3, (short) 123, true, 0,
                        false, NewPromiseClient());
                clientChannel.Flush(); // Headers are queued in the flow controller and so flush them.
                _ = clientHandler.Encoder().WriteDataAsync(CtxClient(), 3, content.RetainedDuplicate(), 0, true,
                                                  NewPromiseClient());
                _ = clientHandler.Encoder().WriteDataAsync(CtxClient(), 5, content2.RetainedDuplicate(), 0, true,
                                                  NewPromiseClient());
                clientChannel.Flush();
            }));
            AwaitRequests();
            serverListener.Verify(l => l.MessageReceived(It.IsAny<IHttpObject>()), Times.Exactly(2));
            capturedRequests = CapturedMessages(serverListener);
            Assert.Equal<object>(request, capturedRequests[0]);
            Assert.Equal<object>(request2, capturedRequests[1]);
        }
        finally
        {
            request.Release();
            request2.Release();
        }
    }

    [Fact]
    public async Task ServerRequestPushPromise()
    {
        await BoostrapEnv(1, 1, 1);
        string text = "hello world big time data!";
        ByteBuf content = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes(text));
        string text2 = "hello world smaller data?";
        ByteBuf content2 = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes(text2));
        IFullHttpMessage response = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
                content);
        IFullHttpMessage response2 = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.CREATED,
                content2);
        IFullHttpMessage request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/push/test");
        try
        {
            HttpHeaders httpHeaders = response.Headers();
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 3);
            httpHeaders.SetInt(HttpHeaderNames.CONTENT_LENGTH, text.Length);
            httpHeaders.SetShort(HttpConversionUtil.ExtensionHeaderNames.STREAM_WEIGHT.Text(), (short) 16);
            HttpHeaders httpHeaders2 = response2.Headers();
            httpHeaders2.Set(HttpConversionUtil.ExtensionHeaderNames.SCHEME.Text(), "https");
            httpHeaders2.Set(HttpHeaderNames.HOST, "example.org");
            httpHeaders2.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 5);
            httpHeaders2.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_PROMISE_ID.Text(), 3);
            httpHeaders2.SetInt(HttpHeaderNames.CONTENT_LENGTH, text2.Length);

            httpHeaders = request.Headers();
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 3);
            httpHeaders.SetInt(HttpHeaderNames.CONTENT_LENGTH, 0);
            httpHeaders.SetShort(HttpConversionUtil.ExtensionHeaderNames.STREAM_WEIGHT.Text(), (short) 16);
            IHttp2Headers http2Headers3 = new DefaultHttp2Headers().Method(new AsciiString("GET"))
                    .Path(new AsciiString("/push/test"));
            RunInChannel(clientChannel, new ActionRunnable(() =>
            {
                _ = clientHandler.Encoder().WriteHeadersAsync(CtxClient(), 3, http2Headers3, 0, true, NewPromiseClient());
                clientChannel.Flush();
            }));
            AwaitRequests();
            serverListener.Verify(l => l.MessageReceived(It.IsAny<IHttpObject>()), Times.Once());
            capturedRequests = CapturedMessages(serverListener);
            Assert.Equal<object>(request, capturedRequests[0]);

            IHttp2Headers http2Headers = new DefaultHttp2Headers().Status(new AsciiString("200"));
            // The PUSH_PROMISE frame includes a header block that contains a
            // complete set of request header fields that the server attributes to
            // the request.
            // https://tools.ietf.org/html/rfc7540#section-8.2.1
            // Therefore, we should consider the case where there is no Http response status.
            IHttp2Headers http2Headers2 = new DefaultHttp2Headers()
                    .Scheme(new AsciiString("https"))
                    .Authority(new AsciiString("example.org"));
            RunInChannel(serverConnectedChannel, new ActionRunnable(() =>
            {
                _ = serverHandler.Encoder().WriteHeadersAsync(CtxServer(), 3, http2Headers, 0, false, NewPromiseServer());
                _ = serverHandler.Encoder().WritePushPromiseAsync(CtxServer(), 3, 2, http2Headers2, 0, NewPromiseServer());
                _ = serverHandler.Encoder().WriteDataAsync(CtxServer(), 3, content.RetainedDuplicate(), 0, true,
                                                  NewPromiseServer());
                _ = serverHandler.Encoder().WriteDataAsync(CtxServer(), 5, content2.RetainedDuplicate(), 0, true,
                                                  NewPromiseServer());
                serverConnectedChannel.Flush();
            }));
            AwaitResponses();
            clientListener.Verify(l => l.MessageReceived(It.IsAny<IHttpObject>()), Times.Once());
            capturedResponses = CapturedMessages(clientListener);
            Assert.Equal<object>(response, capturedResponses[0]);
        }
        finally
        {
            request.Release();
            response.Release();
            response2.Release();
        }
    }

    [Fact]
    public async Task ServerResponseHeaderInformational()
    {
        await BoostrapEnv(1, 2, 1, 2, 1);
        IFullHttpMessage request = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.PUT, "/info/test");
        HttpHeaders httpHeaders = request.Headers();
        httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 3);
        httpHeaders.Set(HttpHeaderNames.EXPECT, HttpHeaderValues.CONTINUE);
        httpHeaders.SetInt(HttpHeaderNames.CONTENT_LENGTH, 0);
        httpHeaders.SetShort(HttpConversionUtil.ExtensionHeaderNames.STREAM_WEIGHT.Text(), (short) 16);

        IHttp2Headers http2Headers = new DefaultHttp2Headers().Method(new AsciiString("PUT"))
                .Path(new AsciiString("/info/test"))
                .Set(new AsciiString(HttpHeaderNames.EXPECT.ToString()),
                     new AsciiString(HttpHeaderValues.CONTINUE.ToString()));
        IFullHttpMessage response = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.CONTINUE);
        string text = "a big payload";
        ByteBuf payload = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes(text));
        IFullHttpMessage request2 = (IFullHttpMessage) request.Replace(payload);
        IFullHttpMessage response2 = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);

        try
        {
            RunInChannel(clientChannel, new ActionRunnable(() =>
            {
                _ = clientHandler.Encoder().WriteHeadersAsync(CtxClient(), 3, http2Headers, 0, false, NewPromiseClient());
                clientChannel.Flush();
            }));

            AwaitRequests();
            httpHeaders = response.Headers();
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 3);
            httpHeaders.SetInt(HttpHeaderNames.CONTENT_LENGTH, 0);
            IHttp2Headers http2HeadersResponse = new DefaultHttp2Headers().Status(new AsciiString("100"));
            RunInChannel(serverConnectedChannel, new ActionRunnable(() =>
            {
                _ = serverHandler.Encoder().WriteHeadersAsync(CtxServer(), 3, http2HeadersResponse, 0, false,
                                                     NewPromiseServer());
                serverConnectedChannel.Flush();
            }));

            AwaitResponses();
            httpHeaders = request2.Headers();
            httpHeaders.SetInt(HttpHeaderNames.CONTENT_LENGTH, text.Length);
            httpHeaders.Remove(HttpHeaderNames.EXPECT);
            RunInChannel(clientChannel, new ActionRunnable(() =>
            {
                _ = clientHandler.Encoder().WriteDataAsync(CtxClient(), 3, payload.RetainedDuplicate(), 0, true,
                                                  NewPromiseClient());
                clientChannel.Flush();
            }));

            AwaitRequests2();
            httpHeaders = response2.Headers();
            httpHeaders.SetInt(HttpConversionUtil.ExtensionHeaderNames.STREAM_ID.Text(), 3);
            httpHeaders.SetInt(HttpHeaderNames.CONTENT_LENGTH, 0);
            httpHeaders.SetShort(HttpConversionUtil.ExtensionHeaderNames.STREAM_WEIGHT.Text(), (short) 16);

            IHttp2Headers http2HeadersResponse2 = new DefaultHttp2Headers().Status(new AsciiString("200"));
            RunInChannel(serverConnectedChannel, new ActionRunnable(() =>
            {
                _ = serverHandler.Encoder().WriteHeadersAsync(CtxServer(), 3, http2HeadersResponse2, 0, true,
                                                     NewPromiseServer());
                serverConnectedChannel.Flush();
            }));

            AwaitResponses2();
            serverListener.Verify(l => l.MessageReceived(It.IsAny<IHttpObject>()), Times.Exactly(2));
            capturedRequests = CapturedMessages(serverListener);
            Assert.Equal(2, capturedRequests.Count);
            // We do not expect to have this header in the captured request so remove it now.
            Assert.NotNull(request.Headers().Remove("x-http2-stream-weight"));

            Assert.Equal<object>(request, capturedRequests[0]);
            Assert.Equal<object>(request2, capturedRequests[1]);

            clientListener.Verify(l => l.MessageReceived(It.IsAny<IHttpObject>()), Times.Exactly(2));
            capturedResponses = CapturedMessages(clientListener);
            Assert.Equal(2, capturedResponses.Count);
            Assert.Equal<object>(response, capturedResponses[0]);
            Assert.Equal<object>(response2, capturedResponses[1]);
        }
        finally
        {
            request.Release();
            request2.Release();
            response.Release();
            response2.Release();
        }
    }

    [Fact]
    public async Task PropagateSettings()
    {
        await BoostrapEnv(1, 1, 2);
        Http2Settings settings = new Http2Settings().PushEnabled(true);
        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            _ = clientHandler.Encoder().WriteSettingsAsync(CtxClient(), settings, NewPromiseClient());
            clientChannel.Flush();
        }));
        Assert.True(settingsLatch.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        settingsListener.Verify(l => l.MessageReceived(It.IsAny<Http2Settings>()), Times.Exactly(2));
        Http2Settings captured = settingsListener.Invocations
                .Where(i => i.Method.Name == nameof(IHttpSettingsListener.MessageReceived))
                .Select(i => (Http2Settings) i.Arguments[0]).Last();
        AssertSettingsEquals(settings, captured);
    }

    private Task BoostrapEnv(int clientLatchCount, int serverLatchCount, int settingsLatchCount)
    {
        return BoostrapEnv(clientLatchCount, clientLatchCount, serverLatchCount, serverLatchCount, settingsLatchCount);
    }

    private async Task BoostrapEnv(int clientLatchCount, int clientLatchCount2, int serverLatchCount,
            int serverLatchCount2, int settingsLatchCount)
    {
        CountdownEvent prefaceWrittenLatch = new CountdownEvent(1);
        clientDelegator = null;
        serverDelegator = null;
        serverConnectedChannel = null;
        maxContentLength = 1024;
        CountdownEvent serverChannelLatch = new CountdownEvent(1);
        serverLatch = new CountdownEvent(serverLatchCount);
        clientLatch = new CountdownEvent(clientLatchCount);
        serverLatch2 = new CountdownEvent(serverLatchCount2);
        clientLatch2 = new CountdownEvent(clientLatchCount2);
        settingsLatch = new CountdownEvent(settingsLatchCount);
        clientHandlersAddedLatch = new CountdownEvent(1);

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
        Assert.True(serverChannelLatch.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(clientHandlersAddedLatch.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    // CLR: the anonymous ChannelInitializer<Channel> of the server side of boostrapEnv(...).
    private sealed class ServerInitializer : ChannelInitializer<IChannel>
    {
        private readonly InboundHttp2ToHttpAdapterTest test;
        private readonly CountdownEvent serverChannelLatch;

        internal ServerInitializer(InboundHttp2ToHttpAdapterTest test, CountdownEvent serverChannelLatch)
        {
            this.test = test;
            this.serverChannelLatch = serverChannelLatch;
        }

        protected override void InitChannel(IChannel ch)
        {
            test.serverConnectedChannel = ch;
            IChannelPipeline p = ch.Pipeline;
            IHttp2Connection connection = new DefaultHttp2Connection(true);

            test.serverHandler = new Http2ConnectionHandlerBuilder().FrameListener(
                    new InboundHttp2ToHttpAdapterBuilder(connection)
                       .MaxContentLength(test.maxContentLength)
                       .ValidateHttpHeaders(true)
                       .PropagateSettings(true)
                       .Build())
               .Connection(connection)
               .GracefulShutdownTimeoutMillis(0)
               .Build();
            p.AddLast(test.serverHandler);

            test.serverDelegator = new HttpResponseDelegator(test.serverListener.Object, test.serverLatch,
                    test.serverLatch2);
            p.AddLast(test.serverDelegator);
            test.settingsDelegator = new HttpSettingsDelegator(test.settingsListener.Object, test.settingsLatch);
            p.AddLast(test.settingsDelegator);
            CountDown(serverChannelLatch);
        }
    }

    // CLR: the anonymous ChannelInitializer<Channel> of the client side of boostrapEnv(...) and its three anonymous
    // handlers (ExceptionCatcher, PrefaceWrittenHandler, HandlersAddedHandler).
    private sealed class ClientInitializer : ChannelInitializer<IChannel>
    {
        private readonly InboundHttp2ToHttpAdapterTest test;
        private readonly CountdownEvent prefaceWrittenLatch;

        internal ClientInitializer(InboundHttp2ToHttpAdapterTest test, CountdownEvent prefaceWrittenLatch)
        {
            this.test = test;
            this.prefaceWrittenLatch = prefaceWrittenLatch;
        }

        protected override void InitChannel(IChannel ch)
        {
            IChannelPipeline p = ch.Pipeline;
            IHttp2Connection connection = new DefaultHttp2Connection(false);

            test.clientHandler = new Http2ConnectionHandlerBuilder().FrameListener(
                    new InboundHttp2ToHttpAdapterBuilder(connection)
                       .MaxContentLength(test.maxContentLength)
                       .Build())
               .Connection(connection)
               .GracefulShutdownTimeoutMillis(0)
               .Build();
            p.AddLast(test.clientHandler);

            test.clientDelegator = new HttpResponseDelegator(test.clientListener.Object, test.clientLatch,
                    test.clientLatch2);
            p.AddLast(test.clientDelegator);
            p.AddLast(new ExceptionCatcher(test));
            p.AddLast(new PrefaceWrittenHandler(prefaceWrittenLatch));
            p.AddLast(new HandlersAddedHandler(test));
        }
    }

    private sealed class ExceptionCatcher : ChannelHandlerAdapter
    {
        private readonly InboundHttp2ToHttpAdapterTest test;

        internal ExceptionCatcher(InboundHttp2ToHttpAdapterTest test)
        {
            this.test = test;
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            Http2Exception e = GetEmbeddedHttp2Exception(cause);
            if (e != null)
            {
                test.clientException = e;
                CountDown(test.clientLatch);
            }
            else
            {
                base.ExceptionCaught(ctx, cause);
            }
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
                CountDown(prefaceWrittenLatch);
                ctx.Pipeline.Remove(this);
            }
        }
    }

    private sealed class HandlersAddedHandler : ChannelInboundHandlerAdapter
    {
        private readonly InboundHttp2ToHttpAdapterTest test;

        internal HandlersAddedHandler(InboundHttp2ToHttpAdapterTest test)
        {
            this.test = test;
        }

        public override void HandlerAdded(IChannelHandlerContext ctx)
        {
            CountDown(test.clientHandlersAddedLatch);
        }
    }

    private void CleanupCapturedRequests()
    {
        if (capturedRequests != null)
        {
            foreach (IFullHttpMessage capturedRequest in capturedRequests)
            {
                capturedRequest.Release();
            }
            capturedRequests = null;
        }
    }

    private void CleanupCapturedResponses()
    {
        if (capturedResponses != null)
        {
            foreach (IFullHttpMessage capturedResponse in capturedResponses)
            {
                capturedResponse.Release();
            }
            capturedResponses = null;
        }
    }

    private void AwaitRequests()
    {
        Assert.True(serverLatch.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    private void AwaitResponses()
    {
        Assert.True(clientLatch.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    private void AwaitRequests2()
    {
        Assert.True(serverLatch2.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    private void AwaitResponses2()
    {
        Assert.True(clientLatch2.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    private IChannelHandlerContext CtxClient()
    {
        return clientChannel.Pipeline.FirstContext;
    }

    private TaskCompletionSource NewPromiseClient()
    {
        return NewPromise();
    }

    private IChannelHandlerContext CtxServer()
    {
        return serverConnectedChannel.Pipeline.FirstContext;
    }

    private TaskCompletionSource NewPromiseServer()
    {
        return NewPromise();
    }

    public interface IHttpResponseListener
    {
        void MessageReceived(IHttpObject obj);
    }

    public interface IHttpSettingsListener
    {
        void MessageReceived(Http2Settings settings);
    }

    private sealed class HttpResponseDelegator : SimpleChannelInboundHandler<IHttpObject>
    {
        private readonly IHttpResponseListener listener;
        private readonly CountdownEvent latch;
        private readonly CountdownEvent latch2;

        internal HttpResponseDelegator(IHttpResponseListener listener, CountdownEvent latch, CountdownEvent latch2)
            : base(false)
        {
            this.listener = listener;
            this.latch = latch;
            this.latch2 = latch2;
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, IHttpObject msg)
        {
            listener.MessageReceived(msg);
            CountDown(latch);
            CountDown(latch2);
        }
    }

    private sealed class HttpSettingsDelegator : SimpleChannelInboundHandler<Http2Settings>
    {
        private readonly IHttpSettingsListener listener;
        private readonly CountdownEvent latch;

        internal HttpSettingsDelegator(IHttpSettingsListener listener, CountdownEvent latch)
            : base(false)
        {
            this.listener = listener;
            this.latch = latch;
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, Http2Settings settings)
        {
            listener.MessageReceived(settings);
            CountDown(latch);
        }
    }

    // CLR: the values an ArgumentCaptor<FullHttpMessage> captures from listener.messageReceived (see the class comment).
    private static IList<IFullHttpMessage> CapturedMessages(Mock<IHttpResponseListener> listener)
    {
        return listener.Invocations
                .Where(i => i.Method.Name == nameof(IHttpResponseListener.MessageReceived))
                .Select(i => (IFullHttpMessage) i.Arguments[0])
                .ToList();
    }

    // CLR: CountDownLatch.countDown(), which does nothing at zero (see the class comment); a Signal() that loses the race
    // to zero against another thread is retried and then stops.
    private static void CountDown(CountdownEvent latch)
    {
        while (latch.CurrentCount > 0)
        {
            try
            {
                latch.Signal();
                return;
            }
            catch (InvalidOperationException)
            {
                // CLR: the count reached zero on another thread.
            }
        }
    }

    // CLR: assertEquals of two Http2Settings, which calls equals (see the class comment).
    private static void AssertSettingsEquals(Http2Settings expected, Http2Settings actual)
    {
        Assert.True(Equals(expected, actual), "expected: <" + expected + "> but was: <" + actual + ">");
    }

    // CLR: the anonymous Http2Runnable instances (see the class comment).
    private sealed class ActionRunnable : IHttp2Runnable
    {
        private readonly Action action;

        internal ActionRunnable(Action action)
        {
            this.action = action;
        }

        public void Run()
        {
            action();
        }
    }
}
