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
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;
using static Netty.NET.Buffer.Unpooled;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Tests.Http2TestUtil;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Tests the full HTTP/2 framing stack including the connection and preface handlers.
 */
// CLR: the @Mock listeners are Moq Mock<IHttp2FrameListener> (loose, as Mockito's mocks); doAnswer/doThrow/when are
// Setup(...).Returns/Callback/Throws and verify(..., never()/times(n)) is Verify(..., Times.Never()/Exactly(n))
// (https://github.com/devlooped/moq/wiki/Quickstart); eq(headers) is the expected value itself, which Moq compares
// with Equals (DefaultHttp2Headers keeps Java's equals). @BeforeEach/@AfterEach are the constructor and
// IAsyncLifetime.DisposeAsync, which awaits the close Tasks and the graceful shutdown of the event loop groups
// (syncUninterruptibly() is awaiting the Task). CountDownLatch is CountdownEvent and await(timeout) Wait(timeout);
// AtomicReference/AtomicBoolean/AtomicInteger are locals written on the event loop and read after a latch, whose
// Signal/Wait order the accesses. The anonymous Http2Runnable instances are the nested ActionRunnable over a lambda,
// the anonymous ChannelInitializer/ChannelInboundHandlerAdapter/ChannelHandlerAdapter/ChannelOutboundHandlerAdapter
// classes are nested classes, and a ChannelFuture listener is a synchronous Task continuation (the promises are
// TaskCompletionSource values without RunContinuationsAsynchronously, so the continuation runs where the promise
// completes, on the event loop, as Java's listener does). ctx.newPromise() is a new TaskCompletionSource
// (Http2CodecUtil.NewPromise) and future.cause() is Http2CodecUtil.Cause(task). Future.get() throws
// ExecutionException with the cause; awaiting the Task throws the cause itself, so assertThrows(ExecutionException)
// with e.getCause() is Assert.ThrowsAnyAsync of the cause type (docs/codec-http-porting.md, Core test notes).
// MultiThreadIoEventLoopGroup/LocalIoHandler/LocalServerChannel/LocalChannel/ServerBootstrap/Bootstrap are the
// Transport ports used as codec-http's HttpContentCompressorTest does; new LocalAddress(getClass()) is
// LocalAddress(Type). ByteArrayOutputStream is MemoryStream, ThreadLocalRandom.current().nextBytes is
// Random.Shared.NextBytes and data.array() is ByteBufUtil.GetBytes(data). Thread.sleep is Thread.Sleep.
public class Http2ConnectionRoundtripTest : IAsyncLifetime
{

    private static readonly TimeSpan DEFAULT_AWAIT_TIMEOUT_SECONDS = TimeSpan.FromSeconds(30);

    private readonly Mock<IHttp2FrameListener> clientListener = new Mock<IHttp2FrameListener>();

    private readonly Mock<IHttp2FrameListener> serverListener = new Mock<IHttp2FrameListener>();

    private Http2ConnectionHandler http2Client;
    private Http2ConnectionHandler http2Server;
    private ServerBootstrap sb;
    private Bootstrap cb;
    private IChannel serverChannel;
    private volatile IChannel serverConnectedChannel;
    private IChannel clientChannel;
    private FrameCountDown serverFrameCountDown;
    private CountdownEvent requestLatch;
    private CountdownEvent serverSettingsAckLatch;
    private CountdownEvent dataLatch;
    private CountdownEvent trailersLatch;
    private CountdownEvent goAwayLatch;

    public Http2ConnectionRoundtripTest()
    {
        MockFlowControl(clientListener);
        MockFlowControl(serverListener);
    }

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
        if (sb == null)
        {
            return;
        }
        Task serverGroup = sb.Config.Group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        Task serverChildGroup = sb.Config.ChildGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        Task clientGroup = cb.Config.Group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        await serverGroup;
        await serverChildGroup;
        await clientGroup;
    }

    [Fact]
    public async Task InflightFrameAfterStreamResetShouldNotMakeConnectionUnusable()
    {
        CountdownEvent latch = new CountdownEvent(1);
        serverListener.Setup(l => l.OnHeadersRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<IHttp2Headers>(), It.IsAny<int>(), It.IsAny<short>(), It.IsAny<bool>(), It.IsAny<int>(),
                It.IsAny<bool>()))
            .Callback((IChannelHandlerContext ctx, int streamId, IHttp2Headers headers, int streamDependency,
                       short weight, bool exclusive, int padding, bool endOfStream) =>
            {
                _ = http2Server.Encoder().WriteHeadersAsync(ctx,
                        streamId,
                        headers,
                        0,
                        false,
                        NewPromise());
                http2Server.Flush(ctx);
            });

        clientListener.Setup(l => l.OnHeadersRead(It.IsAny<IChannelHandlerContext>(), 5, It.IsAny<IHttp2Headers>(),
                It.IsAny<int>(), It.IsAny<short>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<bool>()))
            .Callback(() => latch.Signal());

        await BootstrapEnv(1, 1, 2, 1);

        // Create a single stream by sending a HEADERS frame to the server.
        short weight = 16;
        IHttp2Headers headers = DummyHeaders();
        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), 3, headers, 0, weight, false, 0, false, NewPromise());
            http2Client.Flush(Ctx());
            _ = http2Client.Encoder().WriteRstStreamAsync(Ctx(), 3, Http2Error.INTERNAL_ERROR.Code(), NewPromise());
            http2Client.Flush(Ctx());
        }));

        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), 5, headers, 0, weight, false, 0, false, NewPromise());
            http2Client.Flush(Ctx());
        }));

        Assert.True(latch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HeadersWithEndStreamShouldNotSendError()
    {
        await BootstrapEnv(1, 1, 2, 1);

        // Create a single stream by sending a HEADERS frame to the server.
        short weight = 16;
        IHttp2Headers headers = DummyHeaders();
        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), 3, headers, 0, weight, false, 0, true,
                    NewPromise());
            http2Client.Flush(Ctx());
        }));

        Assert.True(requestLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
        serverListener.Verify(l => l.OnHeadersRead(It.IsAny<IChannelHandlerContext>(), 3, headers,
                0, weight, false, 0, true));
        // Wait for some time to see if a go_away or reset frame will be received.
        Thread.Sleep(1000);

        // Verify that no errors have been received.
        serverListener.Verify(l => l.OnGoAwayRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<long>(), It.IsAny<ByteBuf>()), Times.Never());
        serverListener.Verify(l => l.OnRstStreamRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<long>()), Times.Never());

        // The server will not respond, and so don't wait for graceful shutdown
        SetClientGracefulShutdownTime(0);
    }

    [Fact]
    public async Task EncodeViolatesMaxHeaderListSizeCanStillUseConnection()
    {
        CountdownEvent serverSettingsAckLatch1 = new CountdownEvent(2);
        CountdownEvent serverSettingsAckLatch2 = new CountdownEvent(3);
        CountdownEvent clientSettingsLatch1 = new CountdownEvent(3);
        CountdownEvent serverRevHeadersLatch = new CountdownEvent(1);
        CountdownEvent clientHeadersLatch = new CountdownEvent(1);
        CountdownEvent clientDataWrite = new CountdownEvent(1);
        Exception clientHeadersWriteException = null;
        Exception clientHeadersWriteException2 = null;
        Exception clientDataWriteException = null;

        IHttp2Headers headers = DummyHeaders();

        serverListener.Setup(l => l.OnSettingsAckRead(It.IsAny<IChannelHandlerContext>())).Callback(() =>
        {
            SignalIfPending(serverSettingsAckLatch1);
            SignalIfPending(serverSettingsAckLatch2);
        });
        clientListener.Setup(l => l.OnSettingsRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<Http2Settings>()))
            .Callback(() => SignalIfPending(clientSettingsLatch1));

        // Manually add a listener for when we receive the expected headers on the server.
        serverListener.Setup(l => l.OnHeadersRead(It.IsAny<IChannelHandlerContext>(), 5, headers,
                It.IsAny<int>(), It.IsAny<short>(), It.IsAny<bool>(), 0, true))
            .Callback(() => SignalIfPending(serverRevHeadersLatch));

        await BootstrapEnv(1, 2, 2, 0, 0, -1);

        // Set the maxHeaderListSize to 100 so we may be able to write some headers, but not all. We want to verify
        // that we don't corrupt state if some can be written but not all.
        RunInChannel(serverConnectedChannel, new ActionRunnable(() =>
        {
            _ = http2Server.Encoder().WriteSettingsAsync(ServerCtx(),
                    new Http2Settings().CopyFrom(http2Server.Decoder().LocalSettings())
                            .MaxHeaderListSize(100),
                    ServerNewPromise());
            http2Server.Flush(ServerCtx());
        }));

        Assert.True(serverSettingsAckLatch1.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));

        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            Listen(http2Client.Encoder().WriteHeadersAsync(Ctx(), 3, headers, 0, false, NewPromise()),
                    future => clientHeadersWriteException = Cause(future));
            // It is expected that this write should fail locally and the remote peer will never see this.
            Listen(http2Client.Encoder().WriteDataAsync(Ctx(), 3, Unpooled.Buffer(), 0, true, NewPromise()),
                future =>
                {
                    clientDataWriteException = Cause(future);
                    clientDataWrite.Signal();
                });
            http2Client.Flush(Ctx());
        }));

        Assert.True(clientDataWrite.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
        Assert.NotNull(clientHeadersWriteException, "Header encode should have exceeded maxHeaderListSize!");
        Assert.NotNull(clientDataWriteException, "Data on closed stream should fail!");

        // Set the maxHeaderListSize to the max value so we can send the headers.
        RunInChannel(serverConnectedChannel, new ActionRunnable(() =>
        {
            _ = http2Server.Encoder().WriteSettingsAsync(ServerCtx(),
                    new Http2Settings().CopyFrom(http2Server.Decoder().LocalSettings())
                            .MaxHeaderListSize(Http2CodecUtil.MAX_HEADER_LIST_SIZE),
                    ServerNewPromise());
            http2Server.Flush(ServerCtx());
        }));

        Assert.True(clientSettingsLatch1.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
        Assert.True(serverSettingsAckLatch2.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));

        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            Listen(http2Client.Encoder().WriteHeadersAsync(Ctx(), 5, headers, 0, true,
                    NewPromise()), future =>
                    {
                        clientHeadersWriteException2 = Cause(future);
                        clientHeadersLatch.Signal();
                    });
            http2Client.Flush(Ctx());
        }));

        Assert.True(clientHeadersLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
        Assert.Null(clientHeadersWriteException2,
                "Client write of headers should succeed with increased header list size!");
        Assert.True(serverRevHeadersLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));

        serverListener.Verify(l => l.OnDataRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<ByteBuf>(), It.IsAny<int>(), It.IsAny<bool>()), Times.Never());

        // Verify that no errors have been received.
        VerifyNoErrors();
    }

    [Fact]
    public async Task TestSettingsAckIsSentBeforeUsingFlowControl()
    {
        CountdownEvent serverSettingsAckLatch1 = new CountdownEvent(1);
        CountdownEvent serverSettingsAckLatch2 = new CountdownEvent(2);
        CountdownEvent serverDataLatch = new CountdownEvent(1);
        CountdownEvent clientWriteDataLatch = new CountdownEvent(1);
        byte[] data = new byte[] {1, 2, 3, 4, 5};
        MemoryStream @out = new MemoryStream(data.Length);

        serverListener.Setup(l => l.OnSettingsAckRead(It.IsAny<IChannelHandlerContext>())).Callback(() =>
        {
            SignalIfPending(serverSettingsAckLatch1);
            SignalIfPending(serverSettingsAckLatch2);
        });
        serverListener.Setup(l => l.OnDataRead(It.IsAny<IChannelHandlerContext>(), 3,
                It.IsAny<ByteBuf>(), 0, It.IsAny<bool>()))
            .Returns((IChannelHandlerContext ctx, int streamId, ByteBuf buf, int padding, bool endOfStream) =>
            {
                int processedBytes = buf.ReadableBytes + padding;

                buf.ReadBytes(@out, buf.ReadableBytes);
                SignalIfPending(serverDataLatch);
                return processedBytes;
            });

        await BootstrapEnv(1, 1, 2, 1);

        IHttp2Headers headers = DummyHeaders();

        // The server initially reduces the connection flow control window to 0.
        RunInChannel(serverConnectedChannel, new ActionRunnable(() =>
        {
            _ = http2Server.Encoder().WriteSettingsAsync(ServerCtx(),
                    new Http2Settings().CopyFrom(http2Server.Decoder().LocalSettings())
                            .InitialWindowSize(0),
                    ServerNewPromise());
            http2Server.Flush(ServerCtx());
        }));

        Assert.True(serverSettingsAckLatch1.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));

        // The client should now attempt to send data, but the window size is 0 so it will be queued in the flow
        // controller.
        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), 3, headers, 0, (short) 16, false, 0, false,
                    NewPromise());
            _ = http2Client.Encoder().WriteDataAsync(Ctx(), 3, Unpooled.WrappedBuffer(data), 0, true, NewPromise());
            http2Client.Flush(Ctx());
            clientWriteDataLatch.Signal();
        }));

        Assert.True(clientWriteDataLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));

        // Now the server opens up the connection window to allow the client to send the pending data.
        RunInChannel(serverConnectedChannel, new ActionRunnable(() =>
        {
            _ = http2Server.Encoder().WriteSettingsAsync(ServerCtx(),
                    new Http2Settings().CopyFrom(http2Server.Decoder().LocalSettings())
                            .InitialWindowSize(data.Length),
                    ServerNewPromise());
            http2Server.Flush(ServerCtx());
        }));

        Assert.True(serverSettingsAckLatch2.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
        Assert.True(serverDataLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
        Assert.Equal(data, @out.ToArray());

        // Verify that no errors have been received.
        VerifyNoErrors();
    }

    [Fact]
    public async Task FailedSettingsWriteDoesNotCorruptNextSettingsAck()
    {
        Exception failedSettingsWriteCause = null;
        await BootstrapEnv(0, 2, 0, 0);

        RunInChannel(serverConnectedChannel, new ActionRunnable(() =>
        {
            Listen(http2Server.Encoder().WriteSettingsAsync(ServerCtx(), new Http2Settings().PushEnabled(true),
                    ServerNewPromise()), future => failedSettingsWriteCause = Cause(future));
            _ = http2Server.Encoder().WriteSettingsAsync(ServerCtx(), new Http2Settings().InitialWindowSize(100),
                    ServerNewPromise());
            http2Server.Flush(ServerCtx());
        }));

        Assert.True(serverSettingsAckLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
        Assert.IsAssignableFrom<Http2Exception>(failedSettingsWriteCause);
        Assert.Equal(PROTOCOL_ERROR, ((Http2Exception) failedSettingsWriteCause).Error());
        Assert.Equal(100, http2Server.Decoder().FlowController().InitialWindowSize());
    }

    [Fact]
    public async Task PriorityUsingHigherValuedStreamIdDoesNotPreventUsingLowerStreamId()
    {
        await BootstrapEnv(1, 1, 3, 0);

        IHttp2Headers headers = DummyHeaders();
        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            _ = http2Client.Encoder().WritePriorityAsync(Ctx(), 5, 3, (short) 14, false, NewPromise());
            _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), 3, headers, 0, (short) 16, false, 0, false,
                    NewPromise());
            http2Client.Flush(Ctx());
        }));

        Assert.True(serverSettingsAckLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
        Assert.True(requestLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));

        serverListener.Verify(l => l.OnPriorityRead(It.IsAny<IChannelHandlerContext>(), 5, 3, (short) 14,
                false));
        serverListener.Verify(l => l.OnHeadersRead(It.IsAny<IChannelHandlerContext>(), 3, headers, 0,
                (short) 16, false, 0, false));

        // Verify that no errors have been received.
        VerifyNoErrors();
    }

    [Fact]
    public async Task HeadersUsingHigherValuedStreamIdPreventsUsingLowerStreamId()
    {
        await BootstrapEnv(1, 1, 2, 0);

        IHttp2Headers headers = DummyHeaders();
        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), 5, headers, 0, (short) 16, false, 0, false,
                    NewPromise());
            _ = http2Client.Encoder().FrameWriter().WriteHeadersAsync(Ctx(), 3, headers, 0, (short) 16, false, 0,
                    false, NewPromise());
            http2Client.Flush(Ctx());
        }));

        Assert.True(serverSettingsAckLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
        Assert.True(requestLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));

        serverListener.Verify(l => l.OnHeadersRead(It.IsAny<IChannelHandlerContext>(), 5, headers, 0,
                (short) 16, false, 0, false));
        serverListener.Verify(l => l.OnHeadersRead(It.IsAny<IChannelHandlerContext>(), 3, It.IsAny<IHttp2Headers>(),
                It.IsAny<int>(), It.IsAny<short>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<bool>()), Times.Never());

        // Client should receive a RST_STREAM for stream 3, but there is not Http2Stream object so the listener is never
        // notified.
        VerifyNoErrors();
    }

    [Fact]
    public async Task HeadersWriteForPeerStreamWhichWasResetShouldNotGoAway()
    {
        CountdownEvent serverGotRstLatch = new CountdownEvent(1);
        CountdownEvent serverWriteHeadersLatch = new CountdownEvent(1);
        Exception serverWriteHeadersCauseRef = null;

        int streamId = 3;
        serverListener.Setup(l => l.OnRstStreamRead(It.IsAny<IChannelHandlerContext>(), streamId, It.IsAny<long>()))
            .Callback((IChannelHandlerContext ctx, int id, long errorCode) =>
            {
                if (streamId == id)
                {
                    SignalIfPending(serverGotRstLatch);
                }
            });

        await BootstrapEnv(1, 1, 1, 0);

        IHttp2Headers headers = DummyHeaders();
        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), streamId, headers, CONNECTION_STREAM_ID,
                    DEFAULT_PRIORITY_WEIGHT, false, 0, false, NewPromise());
            _ = http2Client.Encoder().WriteRstStreamAsync(Ctx(), streamId, Http2Error.CANCEL.Code(), NewPromise());
            http2Client.Flush(Ctx());
        }));

        Assert.True(serverSettingsAckLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
        Assert.True(serverGotRstLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));

        serverListener.Verify(l => l.OnHeadersRead(It.IsAny<IChannelHandlerContext>(), streamId, headers,
                It.IsAny<int>(), It.IsAny<short>(), It.IsAny<bool>(), It.IsAny<int>(), false));

        // Now have the server attempt to send a headers frame simulating some asynchronous work.
        RunInChannel(serverConnectedChannel, new ActionRunnable(() =>
        {
            Listen(http2Server.Encoder().WriteHeadersAsync(ServerCtx(), streamId, headers, 0, true,
                    ServerNewPromise()), future =>
                    {
                        serverWriteHeadersCauseRef = Cause(future);
                        serverWriteHeadersLatch.Signal();
                    });
            http2Server.Flush(ServerCtx());
        }));

        Assert.True(serverWriteHeadersLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
        Exception serverWriteHeadersCause = serverWriteHeadersCauseRef;
        Assert.NotNull(serverWriteHeadersCause);
        Assert.IsNotAssignableFrom<Http2Exception>(serverWriteHeadersCauseRef);

        // Server should receive a RST_STREAM for stream 3.
        serverListener.Verify(l => l.OnGoAwayRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<long>(), It.IsAny<ByteBuf>()), Times.Never());
        clientListener.Verify(l => l.OnGoAwayRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<long>(), It.IsAny<ByteBuf>()), Times.Never());
        clientListener.Verify(l => l.OnRstStreamRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<long>()), Times.Never());
    }

    [Fact]
    public async Task Http2ExceptionInPipelineShouldCloseConnection()
    {
        await BootstrapEnv(1, 1, 2, 1);

        // Create a latch to track when the close occurs.
        CountdownEvent closeLatch = new CountdownEvent(1);
        Listen(clientChannel.CloseFuture, future => closeLatch.Signal());

        // Create a single stream by sending a HEADERS frame to the server.
        IHttp2Headers headers = DummyHeaders();
        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), 3, headers, 0, (short) 16, false, 0, false,
                    NewPromise());
            http2Client.Flush(Ctx());
        }));

        // Wait for the server to create the stream.
        Assert.True(serverSettingsAckLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
        Assert.True(requestLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));

        // Add a handler that will immediately throw an exception.
        clientChannel.Pipeline.AddFirst(new ThrowingHandler(Http2Exception.ConnectionError(PROTOCOL_ERROR,
                "Fake Exception")));

        // Wait for the close to occur.
        Assert.True(closeLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
        Assert.False(clientChannel.IsOpen);
    }

    [Fact]
    public async Task ListenerExceptionShouldCloseConnection()
    {
        IHttp2Headers headers = DummyHeaders();
        serverListener.Setup(l => l.OnHeadersRead(
                It.IsAny<IChannelHandlerContext>(), 3, headers, 0, (short) 16,
                false, 0, false)).Throws(Http2TestUtil.FAKE_EXCEPTION);

        await BootstrapEnv(1, 0, 1, 1);

        // Create a latch to track when the close occurs.
        CountdownEvent closeLatch = new CountdownEvent(1);
        Listen(clientChannel.CloseFuture, future => closeLatch.Signal());

        // Create a single stream by sending a HEADERS frame to the server.
        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), 3, headers, 0, (short) 16, false, 0, false,
                    NewPromise());
            http2Client.Flush(Ctx());
        }));

        // Wait for the server to create the stream.
        Assert.True(serverSettingsAckLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
        Assert.True(requestLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));

        // Wait for the close to occur.
        Assert.True(closeLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
        Assert.False(clientChannel.IsOpen);
    }

    private enum WriteEmptyBufferMode
    {
        SINGLE_END_OF_STREAM,
        SECOND_END_OF_STREAM,
        SINGLE_WITH_TRAILERS,
        SECOND_WITH_TRAILERS
    }

    [Fact]
    public Task WriteOfEmptyReleasedBufferSingleBufferQueuedInFlowControllerShouldFail()
    {
        return WriteOfEmptyReleasedBufferQueuedInFlowControllerShouldFail(WriteEmptyBufferMode.SINGLE_END_OF_STREAM);
    }

    [Fact]
    public Task WriteOfEmptyReleasedBufferSingleBufferTrailersQueuedInFlowControllerShouldFail()
    {
        return WriteOfEmptyReleasedBufferQueuedInFlowControllerShouldFail(WriteEmptyBufferMode.SINGLE_WITH_TRAILERS);
    }

    [Fact]
    public Task WriteOfEmptyReleasedBufferMultipleBuffersQueuedInFlowControllerShouldFail()
    {
        return WriteOfEmptyReleasedBufferQueuedInFlowControllerShouldFail(WriteEmptyBufferMode.SECOND_END_OF_STREAM);
    }

    [Fact]
    public Task WriteOfEmptyReleasedBufferMultipleBuffersTrailersQueuedInFlowControllerShouldFail()
    {
        return WriteOfEmptyReleasedBufferQueuedInFlowControllerShouldFail(WriteEmptyBufferMode.SECOND_WITH_TRAILERS);
    }

    // CLR: the switch on the enum keeps Java's default branch; java.lang.Error is UnreachableException.
    private async Task WriteOfEmptyReleasedBufferQueuedInFlowControllerShouldFail(WriteEmptyBufferMode mode)
    {
        await BootstrapEnv(1, 1, 2, 1);

        TaskCompletionSource emptyDataPromise = NewPromise();
        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), 3, EmptyHttp2Headers.INSTANCE, 0, (short) 16, false,
                    0, false, NewPromise());
            ByteBuf emptyBuf = Unpooled.Buffer();
            emptyBuf.Release();
            switch (mode)
            {
                case WriteEmptyBufferMode.SINGLE_END_OF_STREAM:
                    _ = http2Client.Encoder().WriteDataAsync(Ctx(), 3, emptyBuf, 0, true, emptyDataPromise);
                    break;
                case WriteEmptyBufferMode.SECOND_END_OF_STREAM:
                    _ = http2Client.Encoder().WriteDataAsync(Ctx(), 3, emptyBuf, 0, false, emptyDataPromise);
                    _ = http2Client.Encoder().WriteDataAsync(Ctx(), 3, RandomBytes(8), 0, true, NewPromise());
                    break;
                case WriteEmptyBufferMode.SINGLE_WITH_TRAILERS:
                    _ = http2Client.Encoder().WriteDataAsync(Ctx(), 3, emptyBuf, 0, false, emptyDataPromise);
                    _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), 3, EmptyHttp2Headers.INSTANCE, 0,
                            (short) 16, false, 0, true, NewPromise());
                    break;
                case WriteEmptyBufferMode.SECOND_WITH_TRAILERS:
                    _ = http2Client.Encoder().WriteDataAsync(Ctx(), 3, emptyBuf, 0, false, emptyDataPromise);
                    _ = http2Client.Encoder().WriteDataAsync(Ctx(), 3, RandomBytes(8), 0, false, NewPromise());
                    _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), 3, EmptyHttp2Headers.INSTANCE, 0,
                            (short) 16, false, 0, true, NewPromise());
                    break;
                default:
                    throw new System.Diagnostics.UnreachableException("Unexpected WriteEmptyBufferMode: " + mode);
            }
            http2Client.Flush(Ctx());
        }));

        await Assert.ThrowsAnyAsync<IllegalReferenceCountException>(() => emptyDataPromise.Task);
    }

    [Fact]
    public async Task WriteFailureFlowControllerRemoveFrame()
    {
        await BootstrapEnv(1, 1, 3, 1);

        TaskCompletionSource dataPromise = NewPromise();
        TaskCompletionSource assertPromise = NewPromise();

        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), 3, EmptyHttp2Headers.INSTANCE, 0, (short) 16, false,
                    0, false, NewPromise());
            clientChannel.Pipeline.AddFirst(new FailingWriteHandler(this));

            http2Client.Encoder().FlowController().InitialWindowSize(4);
            _ = http2Client.Encoder().WriteDataAsync(Ctx(), 3, RandomBytes(8), 0, false, dataPromise);
            Assert.True(http2Client.Encoder().FlowController()
                    .HasFlowControlled(http2Client.Connection().Stream(3)));

            http2Client.Flush(Ctx());

            try
            {
                // The Frame should have been removed after the write failed.
                Assert.False(http2Client.Encoder().FlowController()
                        .HasFlowControlled(http2Client.Connection().Stream(3)));
                assertPromise.SetResult();
            }
            catch (Exception error)
            {
                assertPromise.SetException(error);
            }
        }));

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => dataPromise.Task);
        await assertPromise.Task;
    }

    // CLR: the anonymous ChannelOutboundHandlerAdapter of writeFailureFlowControllerRemoveFrame();
    // promise.setFailure(e) is the failed Task the outbound write returns.
    private sealed class FailingWriteHandler : ChannelOutboundHandlerAdapter
    {
        private readonly Http2ConnectionRoundtripTest test;

        internal FailingWriteHandler(Http2ConnectionRoundtripTest test)
        {
            this.test = test;
        }

        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            ReferenceCountUtil.Release(msg);

            // Ensure we update the window size so we will try to write the rest of the frame while
            // processing the flush.
            test.http2Client.Encoder().FlowController().InitialWindowSize(8);
            return Task.FromException(new InvalidOperationException());
        }
    }

    [Fact]
    public async Task NonHttp2ExceptionInPipelineShouldNotCloseConnection()
    {
        await BootstrapEnv(1, 1, 2, 1);

        // Create a latch to track when the close occurs.
        CountdownEvent closeLatch = new CountdownEvent(1);
        Listen(clientChannel.CloseFuture, future => closeLatch.Signal());

        // Create a single stream by sending a HEADERS frame to the server.
        IHttp2Headers headers = DummyHeaders();
        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), 3, headers, 0, (short) 16, false, 0, false,
                    NewPromise());
            http2Client.Flush(Ctx());
        }));

        // Wait for the server to create the stream.
        Assert.True(serverSettingsAckLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
        Assert.True(requestLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));

        // Add a handler that will immediately throw an exception.
        clientChannel.Pipeline.AddFirst(new ThrowingHandler(Http2TestUtil.FAKE_EXCEPTION));

        // The close should NOT occur.
        Assert.False(closeLatch.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.True(clientChannel.IsOpen);

        // Set the timeout very low because we know graceful shutdown won't complete
        SetClientGracefulShutdownTime(0);
    }

    // CLR: the anonymous ChannelHandlerAdapter classes whose handlerAdded(...) throws the given exception.
    private sealed class ThrowingHandler : ChannelHandlerAdapter
    {
        private readonly Exception exception;

        internal ThrowingHandler(Exception exception)
        {
            this.exception = exception;
        }

        public override void HandlerAdded(IChannelHandlerContext ctx)
        {
            throw exception;
        }
    }

    [Fact]
    public async Task NoMoreStreamIdsShouldSendGoAway()
    {
        await BootstrapEnv(1, 1, 4, 1, 1, -1);

        // Don't wait for the server to close streams
        SetClientGracefulShutdownTime(0);

        // Create a single stream by sending a HEADERS frame to the server.
        IHttp2Headers headers = DummyHeaders();
        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), 3, headers, 0, (short) 16, false, 0,
                    true, NewPromise());
            http2Client.Flush(Ctx());
        }));

        Assert.True(serverSettingsAckLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));

        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), unchecked(int.MaxValue + 1), headers, 0, (short) 16,
                    false, 0, true, NewPromise());
            http2Client.Flush(Ctx());
        }));

        Assert.True(goAwayLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
        serverListener.Verify(l => l.OnGoAwayRead(It.IsAny<IChannelHandlerContext>(), 0,
                PROTOCOL_ERROR.Code(), It.IsAny<ByteBuf>()));
    }

    [Fact]
    public async Task CreateStreamAfterReceiveGoAwayShouldNotSendGoAway()
    {
        CountdownEvent clientGoAwayLatch = new CountdownEvent(1);
        clientListener.Setup(l => l.OnGoAwayRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<long>(), It.IsAny<ByteBuf>())).Callback(() => SignalIfPending(clientGoAwayLatch));

        await BootstrapEnv(1, 1, 2, 1, 1, -1);

        // We want both sides to do graceful shutdown during the test.
        SetClientGracefulShutdownTime(10000);
        SetServerGracefulShutdownTime(10000);

        // Create a single stream by sending a HEADERS frame to the server.
        IHttp2Headers headers = DummyHeaders();
        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), 3, headers, 0, (short) 16, false, 0,
                    false, NewPromise());
            http2Client.Flush(Ctx());
        }));

        Assert.True(serverSettingsAckLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));

        // Server has received the headers, so the stream is open
        Assert.True(requestLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));

        RunInChannel(serverChannel, new ActionRunnable(() =>
        {
            _ = http2Server.Encoder().WriteGoAwayAsync(ServerCtx(), 3, NO_ERROR.Code(), EmptyBuffer,
                    ServerNewPromise());
            http2Server.Flush(ServerCtx());
        }));

        // wait for the client to receive the GO_AWAY.
        Assert.True(clientGoAwayLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
        clientListener.Verify(l => l.OnGoAwayRead(It.IsAny<IChannelHandlerContext>(), 3, NO_ERROR.Code(),
                It.IsAny<ByteBuf>()));

        Task clientWriteAfterGoAwayFutureRef = null;
        CountdownEvent clientWriteAfterGoAwayLatch = new CountdownEvent(1);
        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            Task f = http2Client.Encoder().WriteHeadersAsync(Ctx(), 5, headers, 0, (short) 16, false, 0,
                    true, NewPromise());
            clientWriteAfterGoAwayFutureRef = f;
            http2Client.Flush(Ctx());
            Listen(f, future => clientWriteAfterGoAwayLatch.Signal());
        }));

        // Wait for the client's write operation to complete.
        Assert.True(clientWriteAfterGoAwayLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));

        Task clientWriteAfterGoAwayFuture = clientWriteAfterGoAwayFutureRef;
        Assert.NotNull(clientWriteAfterGoAwayFuture);
        Exception clientCause = Cause(clientWriteAfterGoAwayFuture);
        Assert.IsAssignableFrom<Http2Exception.StreamException>(clientCause);
        Assert.Equal(Http2Error.REFUSED_STREAM.Code(), ((Http2Exception.StreamException) clientCause).Error().Code());

        // Wait for the server to receive a GO_AWAY, but this is expected to timeout!
        Assert.False(goAwayLatch.Wait(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        serverListener.Verify(l => l.OnGoAwayRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<long>(), It.IsAny<ByteBuf>()), Times.Never());

        // Shutdown shouldn't wait for the server to close streams
        SetClientGracefulShutdownTime(0);
        SetServerGracefulShutdownTime(0);
    }

    [Fact]
    public async Task ListenerIsNotifiedOfGoawayBeforeStreamsAreRemovedFromTheConnection()
    {
        IHttp2Stream.State clientStream3State = null;
        CountdownEvent clientGoAwayLatch = new CountdownEvent(1);
        clientListener.Setup(l => l.OnGoAwayRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<long>(), It.IsAny<ByteBuf>())).Callback(() =>
        {
            clientStream3State = http2Client.Connection().Stream(3).State_();
            SignalIfPending(clientGoAwayLatch);
        });

        await BootstrapEnv(1, 1, 3, 1, 1, -1);

        // We want both sides to do graceful shutdown during the test.
        SetClientGracefulShutdownTime(10000);
        SetServerGracefulShutdownTime(10000);

        // Create a single stream by sending a HEADERS frame to the server.
        IHttp2Headers headers = DummyHeaders();
        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), 1, headers, 0, (short) 16, false, 0,
                false, NewPromise());
            _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), 3, headers, 0, (short) 16, false, 0,
                false, NewPromise());
            http2Client.Flush(Ctx());
        }));

        Assert.True(serverSettingsAckLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));

        // Server has received the headers, so the stream is open
        Assert.True(requestLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));

        RunInChannel(serverChannel, new ActionRunnable(() =>
        {
            _ = http2Server.Encoder().WriteGoAwayAsync(ServerCtx(), 1, NO_ERROR.Code(), EmptyBuffer,
                    ServerNewPromise());
            http2Server.Flush(ServerCtx());
        }));

        // wait for the client to receive the GO_AWAY.
        Assert.True(clientGoAwayLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
        clientListener.Verify(l => l.OnGoAwayRead(It.IsAny<IChannelHandlerContext>(), 1, NO_ERROR.Code(),
            It.IsAny<ByteBuf>()));
        Assert.Same(IHttp2Stream.State.OPEN, clientStream3State);

        // Make sure that stream 3 has been closed which is true if it's gone.
        CountdownEvent probeStreamCount = new CountdownEvent(1);
        bool stream3Exists = false;
        int streamCount = 0;
        RunInChannel(this.clientChannel, new ActionRunnable(() =>
        {
            stream3Exists = http2Client.Connection().Stream(3) != null;
            streamCount = http2Client.Connection().NumActiveStreams();
            probeStreamCount.Signal();
        }));
        // The stream should be closed right after
        Assert.True(probeStreamCount.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
        Assert.Equal(1, streamCount);
        Assert.False(stream3Exists);

        // Wait for the server to receive a GO_AWAY, but this is expected to timeout!
        Assert.False(goAwayLatch.Wait(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        serverListener.Verify(l => l.OnGoAwayRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
            It.IsAny<long>(), It.IsAny<ByteBuf>()), Times.Never());

        // Shutdown shouldn't wait for the server to close streams
        SetClientGracefulShutdownTime(0);
        SetServerGracefulShutdownTime(0);
    }

    [Fact]
    public async Task FlowControlProperlyChunksLargeMessage()
    {
        IHttp2Headers headers = DummyHeaders();
        IHttp2Headers trailers = DummyTrailers();

        // Create a large message to send.
        int length = 10485760; // 10MB

        // Create a buffer filled with random bytes.
        ByteBuf data = RandomBytes(length);
        MemoryStream @out = new MemoryStream(length);
        serverListener.Setup(l => l.OnDataRead(It.IsAny<IChannelHandlerContext>(), 3,
                It.IsAny<ByteBuf>(), 0, It.IsAny<bool>()))
            .Returns((IChannelHandlerContext ctx, int streamId, ByteBuf buf, int padding, bool endOfStream) =>
            {
                int processedBytes = buf.ReadableBytes + padding;

                buf.ReadBytes(@out, buf.ReadableBytes);
                return processedBytes;
            });
        try
        {
            // Initialize the data latch based on the number of bytes expected.
            await BootstrapEnv(length, 1, 3, 1);

            // Create the stream and send all of the data at once.
            RunInChannel(clientChannel, new ActionRunnable(() =>
            {
                _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), 3, headers, 0, (short) 16, false, 0,
                        false, NewPromise());
                _ = http2Client.Encoder().WriteDataAsync(Ctx(), 3, data.RetainedDuplicate(), 0, false, NewPromise());

                // Write trailers.
                _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), 3, trailers, 0, (short) 16, false, 0,
                        true, NewPromise());
                http2Client.Flush(Ctx());
            }));

            // Wait for the trailers to be received.
            Assert.True(serverSettingsAckLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
            Assert.True(trailersLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));

            // Verify that headers and trailers were received.
            serverListener.Verify(l => l.OnHeadersRead(It.IsAny<IChannelHandlerContext>(), 3, headers, 0,
                    (short) 16, false, 0, false));
            serverListener.Verify(l => l.OnHeadersRead(It.IsAny<IChannelHandlerContext>(), 3, trailers, 0,
                    (short) 16, false, 0, true));

            // Verify we received all the bytes.
            Assert.Equal(0, dataLatch.CurrentCount);
            @out.Flush();
            byte[] received = @out.ToArray();
            Assert.Equal(ByteBufUtil.GetBytes(data), received);
        }
        finally
        {
            // Don't wait for server to close streams
            SetClientGracefulShutdownTime(0);
            data.Release();
        }
    }

    [Fact]
    public async Task StressTest()
    {
        IHttp2Headers headers = DummyHeaders();
        IHttp2Headers trailers = DummyTrailers();
        int length = 10;
        ByteBuf data = RandomBytes(length);
        string dataAsHex = ByteBufUtil.HexDump(data);
        long pingData = 8;
        int numStreams = 2000;

        // Collect all the ping buffers as we receive them at the server.
        long[] receivedPings = new long[numStreams];
        int nextIndex = 0;
        serverListener.Setup(l => l.OnPingRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<long>()))
            .Callback((IChannelHandlerContext ctx, long ping) => receivedPings[nextIndex++] = ping);

        // Collect all the data buffers as we receive them at the server.
        StringBuilder[] receivedData = new StringBuilder[numStreams];
        serverListener.Setup(l => l.OnDataRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<ByteBuf>(), It.IsAny<int>(), It.IsAny<bool>()))
            .Returns((IChannelHandlerContext ctx, int streamId, ByteBuf buf, int padding, bool endOfStream) =>
            {
                int processedBytes = buf.ReadableBytes + padding;

                int streamIndex = (streamId - 3) / 2;
                StringBuilder builder = receivedData[streamIndex];
                if (builder == null)
                {
                    builder = new StringBuilder(dataAsHex.Length);
                    receivedData[streamIndex] = builder;
                }
                builder.Append(ByteBufUtil.HexDump(buf));
                return processedBytes;
            });
        try
        {
            await BootstrapEnv(numStreams * length, 1, numStreams * 4 + 1 , numStreams, -1, numStreams);
            RunInChannel(clientChannel, new ActionRunnable(() =>
            {
                int upperLimit = 3 + 2 * numStreams;
                for (int streamId = 3; streamId < upperLimit; streamId += 2)
                {
                    // Send a bunch of data on each stream.
                    _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), streamId, headers, 0, (short) 16,
                            false, 0, false, NewPromise());
                    _ = http2Client.Encoder().WritePingAsync(Ctx(), false, pingData,
                            NewPromise());
                    _ = http2Client.Encoder().WriteDataAsync(Ctx(), streamId, data.RetainedSlice(), 0,
                                                    false, NewPromise());
                    // Write trailers.
                    _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), streamId, trailers, 0, (short) 16,
                            false, 0, true, NewPromise());
                    http2Client.Flush(Ctx());
                }
            }));
            // Wait for all frames to be received.
            Assert.True(serverSettingsAckLatch.Wait(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));
            Assert.True(trailersLatch.Wait(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));
            serverListener.Verify(l => l.OnHeadersRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                    headers, 0, (short) 16, false, 0, false), Times.Exactly(numStreams));
            serverListener.Verify(l => l.OnHeadersRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                    trailers, 0, (short) 16, false, 0, true), Times.Exactly(numStreams));
            serverListener.Verify(l => l.OnPingRead(It.IsAny<IChannelHandlerContext>(),
                    It.IsAny<long>()), Times.Exactly(numStreams));
            serverListener.Verify(l => l.OnDataRead(It.IsAny<IChannelHandlerContext>(),
                    It.IsAny<int>(), It.IsAny<ByteBuf>(), 0, true), Times.Never());
            foreach (StringBuilder builder in receivedData)
            {
                Assert.Equal(dataAsHex, builder.ToString());
            }
            foreach (long receivedPing in receivedPings)
            {
                Assert.Equal(pingData, receivedPing);
            }
        }
        finally
        {
            // Don't wait for server to close streams
            SetClientGracefulShutdownTime(0);
            data.Release();
        }
    }

    [Fact]
    public async Task ServerShouldNotEnforceClientAdvertisedMaxHeaderListSize()
    {
        // Verifies that SETTINGS_MAX_HEADER_LIST_SIZE sent by a client is treated as advisory
        // (per RFC 9113 §6.5.2) and does not prevent the server from encoding response headers.
        CountdownEvent clientSettingsAckLatch = new CountdownEvent(2);
        CountdownEvent responseLatch = new CountdownEvent(1);
        Exception serverWriteError = null;

        serverListener.Setup(l => l.OnHeadersRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<IHttp2Headers>(), It.IsAny<int>(), It.IsAny<short>(), It.IsAny<bool>(), It.IsAny<int>(),
                It.IsAny<bool>()))
            .Callback((IChannelHandlerContext ctx, int streamId, IHttp2Headers headers, int streamDependency,
                       short weight, bool exclusive, int padding, bool endOfStream) =>
            {
                IChannelHandlerContext sCtx = ServerCtx();
                IHttp2Headers responseHeaders = new DefaultHttp2Headers().Status(new StringCharSequence("200"));
                Listen(http2Server.Encoder().WriteHeadersAsync(sCtx, streamId, responseHeaders, 0, true,
                        NewPromise()), future =>
                        {
                            serverWriteError = Cause(future);
                            responseLatch.Signal();
                        });
                http2Server.Flush(sCtx);
            });

        clientListener.Setup(l => l.OnSettingsAckRead(It.IsAny<IChannelHandlerContext>()))
            .Callback(() => SignalIfPending(clientSettingsAckLatch));

        await BootstrapEnv(0, 1, 2, 0);

        // Client advertises a tiny MAX_HEADER_LIST_SIZE (2 bytes) to the server.
        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            _ = http2Client.Encoder().WriteSettingsAsync(Ctx(),
                    new Http2Settings().MaxHeaderListSize(2),
                    NewPromise());
            http2Client.Flush(Ctx());
        }));

        // Wait for the server to acknowledge both the initial settings and our custom settings.
        Assert.True(clientSettingsAckLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));

        // Send a request; the server will attempt to respond with headers far exceeding 2 bytes.
        short weight = 16;
        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            _ = http2Client.Encoder().WriteHeadersAsync(Ctx(), 3, DummyHeaders(), 0, weight, false, 0, true,
                    NewPromise());
            http2Client.Flush(Ctx());
        }));

        Assert.True(responseLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
        Assert.Null(serverWriteError,
                "Server must succeed writing response headers regardless of client's SETTINGS_MAX_HEADER_LIST_SIZE");
    }

    private Task BootstrapEnv(int dataCountDown, int settingsAckCount,
            int requestCountDown, int trailersCountDown)
    {
        return BootstrapEnv(dataCountDown, settingsAckCount, requestCountDown, trailersCountDown, -1, -1);
    }

    private async Task BootstrapEnv(int dataCountDown, int settingsAckCount,
            int requestCountDown, int trailersCountDown, int goAwayCountDown, long maxConcurrentStreams)
    {
        CountdownEvent prefaceWrittenLatch = new CountdownEvent(1);
        requestLatch = new CountdownEvent(requestCountDown);
        serverSettingsAckLatch = new CountdownEvent(settingsAckCount);
        dataLatch = new CountdownEvent(dataCountDown);
        trailersLatch = new CountdownEvent(trailersCountDown);
        goAwayLatch = goAwayCountDown > 0 ? new CountdownEvent(goAwayCountDown) : requestLatch;
        sb = new ServerBootstrap();
        cb = new Bootstrap();

        Http2ConnectionHandler[] serverHandlerRef = new Http2ConnectionHandler[1];
        CountdownEvent serverInitLatch = new CountdownEvent(1);
        sb.Group(new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory()));
        sb.Channel<LocalServerChannel>();
        sb.ChildHandler(new ServerInitializer(this, maxConcurrentStreams, serverHandlerRef, serverInitLatch));

        cb.Group(new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory()));
        cb.Channel<LocalChannel>();
        cb.Handler(new ClientInitializer(this, prefaceWrittenLatch));

        serverChannel = await sb.BindAsync(new LocalAddress(GetType()));

        clientChannel = await cb.ConnectAsync(serverChannel.LocalAddress);
        Assert.True(prefaceWrittenLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
        http2Client = clientChannel.Pipeline.Get<Http2ConnectionHandler>();
        Assert.True(serverInitLatch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
        http2Server = Volatile.Read(ref serverHandlerRef[0]);
    }

    // CLR: the anonymous ChannelInitializer<Channel> of the server side of bootstrapEnv(...).
    private sealed class ServerInitializer : ChannelInitializer<IChannel>
    {
        private readonly Http2ConnectionRoundtripTest test;
        private readonly long maxConcurrentStreams;
        private readonly Http2ConnectionHandler[] serverHandlerRef;
        private readonly CountdownEvent serverInitLatch;

        internal ServerInitializer(Http2ConnectionRoundtripTest test, long maxConcurrentStreams,
                                   Http2ConnectionHandler[] serverHandlerRef, CountdownEvent serverInitLatch)
        {
            this.test = test;
            this.maxConcurrentStreams = maxConcurrentStreams;
            this.serverHandlerRef = serverHandlerRef;
            this.serverInitLatch = serverInitLatch;
        }

        protected override void InitChannel(IChannel ch)
        {
            test.serverConnectedChannel = ch;
            IChannelPipeline p = ch.Pipeline;
            test.serverFrameCountDown =
                    new FrameCountDown(test.serverListener.Object, test.serverSettingsAckLatch,
                            test.requestLatch, test.dataLatch, test.trailersLatch, test.goAwayLatch);
            Http2ConnectionHandlerBuilder builder = new Http2ConnectionHandlerBuilder()
                    .Server(true)
                    .FrameListener(test.serverFrameCountDown)
                    .ValidateHeaders(false);
            if (maxConcurrentStreams != -1)
            {
                builder.InitialSettings(Http2Settings.DefaultSettings().MaxConcurrentStreams(maxConcurrentStreams));
            }
            Volatile.Write(ref serverHandlerRef[0], builder.Build());
            p.AddLast(serverHandlerRef[0]);
            serverInitLatch.Signal();
        }
    }

    // CLR: the anonymous ChannelInitializer<Channel> of the client side of bootstrapEnv(...) and its anonymous
    // ChannelInboundHandlerAdapter (PrefaceWrittenHandler).
    private sealed class ClientInitializer : ChannelInitializer<IChannel>
    {
        private readonly Http2ConnectionRoundtripTest test;
        private readonly CountdownEvent prefaceWrittenLatch;

        internal ClientInitializer(Http2ConnectionRoundtripTest test, CountdownEvent prefaceWrittenLatch)
        {
            this.test = test;
            this.prefaceWrittenLatch = prefaceWrittenLatch;
        }

        protected override void InitChannel(IChannel ch)
        {
            IChannelPipeline p = ch.Pipeline;
            p.AddLast(new Http2ConnectionHandlerBuilder()
                    .Server(false)
                    .FrameListener(test.clientListener.Object)
                    .ValidateHeaders(false)
                    .GracefulShutdownTimeoutMillis(0)
                    .Build());
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

    private IChannelHandlerContext Ctx()
    {
        return clientChannel.Pipeline.FirstContext;
    }

    private IChannelHandlerContext ServerCtx()
    {
        return serverConnectedChannel.Pipeline.FirstContext;
    }

    private TaskCompletionSource NewPromise()
    {
        return Http2CodecUtil.NewPromise();
    }

    private TaskCompletionSource ServerNewPromise()
    {
        return Http2CodecUtil.NewPromise();
    }

    // CLR: the String/AsciiString values are AsciiString and StringCharSequence values (a C# string is not an
    // ICharSequence; docs/codec-http-porting.md, Shared CLR adaptations).
    private static IHttp2Headers DummyHeaders()
    {
        return new DefaultHttp2Headers(false).Method(new AsciiString("GET")).Scheme(new AsciiString("https"))
        .Authority(new AsciiString("example.org")).Path(new AsciiString("/some/path/resource2"))
        .Add(RandomString(), RandomString());
    }

    private static IHttp2Headers DummyTrailers()
    {
        return new DefaultHttp2Headers(false)
        .Add(new StringCharSequence("header-" + RandomString()), RandomString());
    }

    private static void MockFlowControl(Mock<IHttp2FrameListener> listener)
    {
        listener.Setup(l => l.OnDataRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<ByteBuf>(), It.IsAny<int>(), It.IsAny<bool>()))
            .Returns((IChannelHandlerContext ctx, int streamId, ByteBuf buf, int padding, bool endOfStream) =>
                buf.ReadableBytes + padding);
    }

    private void SetClientGracefulShutdownTime(long millis)
    {
        SetGracefulShutdownTime(clientChannel, http2Client, millis);
    }

    private void SetServerGracefulShutdownTime(long millis)
    {
        SetGracefulShutdownTime(serverChannel, http2Server, millis);
    }

    private static void SetGracefulShutdownTime(IChannel channel, Http2ConnectionHandler handler,
                                                long millis)
    {
        CountdownEvent latch = new CountdownEvent(1);
        RunInChannel(channel, new ActionRunnable(() =>
        {
            handler.GracefulShutdownTimeoutMillis(millis);
            latch.Signal();
        }));

        Assert.True(latch.Wait(DEFAULT_AWAIT_TIMEOUT_SECONDS, TestContext.Current.CancellationToken));
    }

    /**
     * Creates a {@link ByteBuf} of the given length, filled with random bytes.
     */
    private static ByteBuf RandomBytes(int length)
    {
        byte[] bytes = new byte[length];
        Random.Shared.NextBytes(bytes);
        return Unpooled.WrappedBuffer(bytes);
    }

    // CLR: the repeated "Verify that no errors have been received." checks.
    private void VerifyNoErrors()
    {
        serverListener.Verify(l => l.OnGoAwayRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<long>(), It.IsAny<ByteBuf>()), Times.Never());
        serverListener.Verify(l => l.OnRstStreamRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<long>()), Times.Never());
        clientListener.Verify(l => l.OnGoAwayRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<long>(), It.IsAny<ByteBuf>()), Times.Never());
        clientListener.Verify(l => l.OnRstStreamRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<long>()), Times.Never());
    }

    // CLR: CountDownLatch.countDown() does nothing when the count is already zero, where CountdownEvent.Signal()
    // throws; the latches that a listener may count down more often than their count use this.
    private static void SignalIfPending(CountdownEvent latch)
    {
        try
        {
            if (latch.CurrentCount > 0)
            {
                latch.Signal();
            }
        }
        catch (InvalidOperationException)
        {
            // Raced to zero.
        }
    }

    // CLR: future.addListener(listener) in the test: a synchronous continuation (see the type comment).
    private static void Listen(Task future, Action<Task> listener)
    {
        _ = future.ContinueWith(listener, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }

    // CLR: an anonymous Http2Runnable over the lambda body.
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
