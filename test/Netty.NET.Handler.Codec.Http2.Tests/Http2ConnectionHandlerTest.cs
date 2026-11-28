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
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Buffer.Unpooled;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Http2FrameTypes;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Tests for {@link Http2ConnectionHandler}
 */
// CLR: Mockito's @Mock fields are Moq Mock<T> fields created per test (xUnit creates a test-class instance per test,
// https://xunit.net/docs/shared-context), @BeforeEach is the constructor and @AfterEach Dispose(); when(...).thenReturn
// is Setup(...).Returns, doAnswer is Returns/Callback with the invocation arguments, verify(..., times(n)/never()/
// atLeastOnce()) is Verify(..., Times.Exactly(n)/Never()/AtLeastOnce()), ArgumentCaptor is a Callback capture,
// clearInvocations is Invocations.Clear() and verifyNoMoreInteractions/verifyNoInteractions are VerifyNoOtherCalls()
// (https://github.com/devlooped/moq/wiki/Quickstart; docs/codec-http-porting.md, Shared CLR adaptations).
// ChannelPromise is TaskCompletionSource and ChannelFuture is Task. ChannelHandlerContext has no newPromise() or
// voidPromise() in the Transport port (the handler creates its own TaskCompletionSource, Http2CodecUtil.NewPromise),
// so the stubs ctx.newPromise()/ctx.voidPromise() have no counterpart, and eq(promise) for a promise that Java's
// handler took from ctx.newPromise() is It.IsAny<TaskCompletionSource>(); where the test passes promise to the
// handler (goAway, resetStream) the same TaskCompletionSource is matched. The mocked ChannelFuture whose cause() is
// FAKE_EXCEPTION and whose addListener runs the listener at once is a completed faulted Task
// (Task.FromException(FAKE_EXCEPTION)); the tests that stub isDone()/isSuccess() to true use Task.CompletedTask.
// close(ctx, promise) is CloseAsync(ctx, CancellationToken), which returns the Task of the handler's own promise.
// The mocked EventExecutor is the private RecordingEventExecutor: the Transport's ScheduleAsync schedules only on an
// AbstractScheduledEventExecutor (src/Netty.NET.Common/Concurrent/EventExecutorSchedulingExtensions.cs), so a Moq mock
// cannot record executor.schedule(...); the executor records each scheduled delay (ValidateScheduled is called for
// every scheduled task), runs execute(...) at once and is always in its event loop, the way the listeners in Java's
// mocked futures run at once. String.getBytes(UTF_8)/copiedBuffer(s, UTF_8/US_ASCII) are Encoding.UTF8/ASCII;
// InetSocketAddress(0) is an IPEndPoint of IPAddress.Any and port 0. The anonymous Answers, the anonymous
// Http2ConnectionHandler subclass and the Executables are lambdas and the nested PrefaceEventHandler.
public class Http2ConnectionHandlerTest : IDisposable
{
    private const int STREAM_ID = 1;
    private const int NON_EXISTANT_STREAM_ID = 13;

    private Http2ConnectionHandler handler;
    private TaskCompletionSource promise;
    private TaskCompletionSource voidPromise;

    private readonly Mock<IHttp2Connection> connection = new Mock<IHttp2Connection>();

    private readonly Mock<IHttp2RemoteFlowController> remoteFlow = new Mock<IHttp2RemoteFlowController>();

    private readonly Mock<IHttp2LocalFlowController> localFlow = new Mock<IHttp2LocalFlowController>();

    private readonly Mock<IHttp2Connection.IEndpoint<IHttp2RemoteFlowController>> remote =
        new Mock<IHttp2Connection.IEndpoint<IHttp2RemoteFlowController>>();

    private readonly Mock<IHttp2RemoteFlowController> remoteFlowController = new Mock<IHttp2RemoteFlowController>();

    private readonly Mock<IHttp2Connection.IEndpoint<IHttp2LocalFlowController>> local =
        new Mock<IHttp2Connection.IEndpoint<IHttp2LocalFlowController>>();

    private readonly Mock<IHttp2LocalFlowController> localFlowController = new Mock<IHttp2LocalFlowController>();

    private readonly Mock<IChannelHandlerContext> ctx = new Mock<IChannelHandlerContext>();

    private readonly RecordingEventExecutor executor = new RecordingEventExecutor();

    private readonly Mock<IChannel> channel = new Mock<IChannel>();

    private readonly Mock<IChannelPipeline> pipeline = new Mock<IChannelPipeline>();

    private Task future;

    private readonly Mock<IHttp2Stream> stream = new Mock<IHttp2Stream>();

    private readonly Mock<IHttp2ConnectionDecoder> decoder = new Mock<IHttp2ConnectionDecoder>();

    private readonly Mock<IHttp2ConnectionEncoder> encoder = new Mock<IHttp2ConnectionEncoder>();

    private readonly Mock<IHttp2FrameWriter> frameWriter = new Mock<IHttp2FrameWriter>();

    private string goAwayDebugCap;

    public Http2ConnectionHandlerTest()
    {
        promise = new TaskCompletionSource();
        voidPromise = new TaskCompletionSource();

        channel.Setup(c => c.Metadata).Returns(new ChannelMetadata(false));
        DefaultChannelConfig config = new DefaultChannelConfig(channel.Object);
        channel.Setup(c => c.Config).Returns(config);

        Exception fakeException = Http2TestUtil.FAKE_EXCEPTION;
        future = Task.FromException(fakeException);
        encoder.Setup(e => e.Connection()).Returns(connection.Object);
        decoder.Setup(d => d.Connection()).Returns(connection.Object);
        encoder.Setup(e => e.FrameWriter()).Returns(frameWriter.Object);
        encoder.Setup(e => e.FlowController()).Returns(remoteFlow.Object);
        decoder.Setup(d => d.FlowController()).Returns(localFlow.Object);
        frameWriter.Setup(w => w.WriteGoAwayAsync(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<long>(), It.IsAny<ByteBuf>(), It.IsAny<TaskCompletionSource>()))
            .Returns((IChannelHandlerContext c, int lastStreamId, long errorCode, ByteBuf buf, TaskCompletionSource p) =>
            {
                goAwayDebugCap = buf.ToString(Encoding.UTF8);
                buf.Release();
                return future;
            });
        channel.Setup(c => c.IsActive).Returns(true);
        channel.Setup(c => c.Pipeline).Returns(pipeline.Object);
        connection.Setup(c => c.Remote()).Returns(remote.Object);
        remote.Setup(r => r.FlowController()).Returns(remoteFlowController.Object);
        connection.Setup(c => c.Local()).Returns(local.Object);
        local.Setup(l => l.FlowController()).Returns(localFlowController.Object);
        connection.Setup(c => c.ForEachActiveStream(It.IsAny<IHttp2StreamVisitor>()))
            .Returns((IHttp2StreamVisitor visitor) =>
            {
                if (!visitor.Visit(stream.Object))
                {
                    return stream.Object;
                }

                return null;
            });
        connection.Setup(c => c.Stream(NON_EXISTANT_STREAM_ID)).Returns((IHttp2Stream) null);
        connection.Setup(c => c.NumActiveStreams()).Returns(1);
        connection.Setup(c => c.Stream(STREAM_ID)).Returns(stream.Object);
        connection.Setup(c => c.GoAwaySent(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<ByteBuf>())).Returns(true);
        stream.Setup(s => s.Open(It.IsAny<bool>())).Returns(stream.Object);
        encoder.Setup(e => e.WriteSettingsAsync(ctx.Object, It.IsAny<Http2Settings>(),
                It.IsAny<TaskCompletionSource>())).Returns(() => future);
        ctx.Setup(c => c.Alloc()).Returns(UnpooledByteBufAllocator.Default);
        ctx.Setup(c => c.Channel).Returns(channel.Object);
        ctx.Setup(c => c.WriteAsync(It.IsAny<object>(), It.IsAny<CancellationToken>())).Returns(() => future);
        ctx.Setup(c => c.Executor).Returns(executor);
        ctx.Setup(c => c.FireChannelRead(It.IsAny<object>())).Callback((object msg) => ReferenceCountUtil.Release(msg));
    }

    private Http2ConnectionHandler NewHandler(bool flushPreface)
    {
        Http2ConnectionHandler handler = new Http2ConnectionHandlerBuilder().Codec(decoder.Object, encoder.Object)
                .FlushPreface(flushPreface).Build();
        handler.HandlerAdded(ctx.Object);
        return handler;
    }

    private Http2ConnectionHandler NewHandler()
    {
        return NewHandler(true);
    }

    public void Dispose()
    {
        if (handler != null)
        {
            handler.HandlerRemoved(ctx.Object);
        }
    }

    [Fact]
    public void OnHttpServerUpgradeWithoutHandlerAdded()
    {
        handler = new Http2ConnectionHandlerBuilder().FrameListener(new Http2FrameAdapter()).Server(true).Build();
        Http2Exception e = Assert.ThrowsAny<Http2Exception>(() => handler.OnHttpServerUpgrade(new Http2Settings()));
        Assert.Equal(Http2Error.INTERNAL_ERROR, e.Error());
    }

    [Fact]
    public void OnHttpClientUpgradeWithoutHandlerAdded()
    {
        handler = new Http2ConnectionHandlerBuilder().FrameListener(new Http2FrameAdapter()).Server(false).Build();
        Http2Exception e = Assert.ThrowsAny<Http2Exception>(() => handler.OnHttpClientUpgrade());
        Assert.Equal(Http2Error.INTERNAL_ERROR, e.Error());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClientShouldveSentPrefaceAndSettingsFrameWhenUserEventIsTriggered(bool flushPreface)
    {
        connection.Setup(c => c.IsServer()).Returns(false);
        channel.Setup(c => c.IsActive).Returns(false);
        handler = NewHandler(flushPreface);
        channel.Setup(c => c.IsActive).Returns(true);

        Http2ConnectionPrefaceAndSettingsFrameWrittenEvent evt =
                Http2ConnectionPrefaceAndSettingsFrameWrittenEvent.INSTANCE;

        bool verified = false;
        ctx.Setup(c => c.FireUserEventTriggered(evt)).Callback((object arg) =>
        {
            Assert.Equal(arg, evt);  // sanity check...
            ctx.Verify(c => c.WriteAsync(It.Is<object>(m => IsConnectionPreface(m)), It.IsAny<CancellationToken>()));
            encoder.Verify(e => e.WriteSettingsAsync(ctx.Object, It.IsAny<Http2Settings>(),
                    It.IsAny<TaskCompletionSource>()));
            verified = true;
        });

        handler.ChannelActive(ctx.Object);

        if (flushPreface)
        {
            ctx.Verify(c => c.Flush(), Times.Once());
        }
        else
        {
            ctx.Verify(c => c.Flush(), Times.Never());
        }

        Assert.True(verified);
    }

    [Fact]
    public void ClientShouldSendClientPrefaceStringWhenActive()
    {
        connection.Setup(c => c.IsServer()).Returns(false);
        channel.Setup(c => c.IsActive).Returns(false);
        handler = NewHandler();
        ctx.Verify(c => c.Flush(), Times.Never());
        channel.Setup(c => c.IsActive).Returns(true);
        handler.ChannelActive(ctx.Object);
        ctx.Verify(c => c.WriteAsync(It.Is<object>(m => IsConnectionPreface(m)), It.IsAny<CancellationToken>()));
        ctx.Verify(c => c.Flush());
    }

    [Fact]
    public void ServerShouldNotSendClientPrefaceStringWhenActive()
    {
        connection.Setup(c => c.IsServer()).Returns(true);
        channel.Setup(c => c.IsActive).Returns(false);
        handler = NewHandler();
        ctx.Verify(c => c.Flush(), Times.Never());
        channel.Setup(c => c.IsActive).Returns(true);
        handler.ChannelActive(ctx.Object);
        ctx.Verify(c => c.WriteAsync(It.Is<object>(m => IsConnectionPreface(m)), It.IsAny<CancellationToken>()),
                Times.Never());
        ctx.Verify(c => c.Flush());
    }

    [Fact]
    public void ClientShouldSendClientPrefaceStringWhenAddedAfterActive()
    {
        connection.Setup(c => c.IsServer()).Returns(false);
        channel.Setup(c => c.IsActive).Returns(true);
        handler = NewHandler();
        ctx.Verify(c => c.WriteAsync(It.Is<object>(m => IsConnectionPreface(m)), It.IsAny<CancellationToken>()));
        ctx.Verify(c => c.Flush());
    }

    [Fact]
    public void ServerShouldNotSendClientPrefaceStringWhenAddedAfterActive()
    {
        connection.Setup(c => c.IsServer()).Returns(true);
        channel.Setup(c => c.IsActive).Returns(true);
        handler = NewHandler();
        ctx.Verify(c => c.WriteAsync(It.Is<object>(m => IsConnectionPreface(m)), It.IsAny<CancellationToken>()),
                Times.Never());
        ctx.Verify(c => c.Flush());
    }

    [Fact]
    public void ChannelWritabilityChangedShouldNotReenterFlush()
    {
        channel.Setup(c => c.IsActive).Returns(false);
        channel.Setup(c => c.IsWritable).Returns(true);
        handler = NewHandler();
        ctx.Setup(c => c.Flush()).Callback(() => handler.ChannelWritabilityChanged(ctx.Object));

        handler.Flush(ctx.Object);

        remoteFlow.Verify(f => f.WritePendingBytes());
        ctx.Verify(c => c.Flush());
        remoteFlow.Verify(f => f.ChannelWritabilityChanged());
    }

    /**
     * See <a href="https://github.com/netty/netty/issues/17276">#17276</a>: {@code close()}'s own cascade
     * (e.g. {@code SslHandler.closeOutboundAndChannel()} flushing a close_notify) can synchronously trigger
     * {@code channelWritabilityChanged()} -&gt; {@code flush()} from outside any active {@code flush()} frame, so a
     * reentrancy guard scoped only to {@code flush()} itself (as added for #17256) would never see it.
     */
    [Fact]
    public void CloseShouldNotAllowChannelWritabilityChangedToReenterFlush()
    {
        channel.Setup(c => c.IsWritable).Returns(true);
        handler = new Http2ConnectionHandlerBuilder().Codec(decoder.Object, encoder.Object)
                .DecoupleCloseAndGoAway(true).Build();
        handler.HandlerAdded(ctx.Object);
        ctx.Invocations.Clear();

        ctx.Setup(c => c.CloseAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            // Simulate close()'s own cascade (e.g. SslHandler flushing a close_notify) completing a write and
            // firing channelWritabilityChanged synchronously, from outside any Http2ConnectionHandler.flush()
            // frame - exactly what AbstractKQueueStreamChannel's write-drain loop does when a write it just
            // performed flips writability.
            handler.ChannelWritabilityChanged(ctx.Object);
            return future;
        });

        _ = handler.CloseAsync(ctx.Object, CancellationToken.None);

        ctx.Verify(c => c.Flush(), Times.Never());
    }

    /**
     * A large body must fully drain even when the channel briefly becomes unwritable while it is being written
     * and then flips back to writable synchronously during {@code flush()}. That writable transition re-enters
     * {@link Http2ConnectionHandler#channelWritabilityChanged(ChannelHandlerContext)} while the flush is still in
     * progress; the handler must not recurse (which livelocks), but it must still write the frames that were
     * queued in the remote flow controller. Dropping the reentrant flush strands the tail of the body forever.
     */
    [Fact]
    public void LargeWriteDrainsFullyWhenWritabilityTogglesDuringFlush()
    {
        // Small watermarks so writing ~1 KiB flips the channel unwritable, and EmbeddedChannel draining the
        // outbound buffer during flush() flips it back to writable synchronously (ChannelOutboundBuffer.remove
        // fires the event inline), re-entering flush().
        EmbeddedChannel channel = new EmbeddedChannel();
        channel.Config.SetWriteBufferWaterMark(new WriteBufferWaterMark(512, 1024));

        Http2ConnectionHandler handler = new Http2ConnectionHandlerBuilder()
                .Server(false)
                .FrameListener(new Http2FrameAdapter())
                .ValidateHeaders(false)
                .GracefulShutdownTimeoutMillis(0)
                .Build();

        _ = channel.ConnectAsync(new IPEndPoint(IPAddress.Any, 0), null, TestContext.Current.CancellationToken);
        channel.Pipeline.AddLast(handler);
        channel.Pipeline.FireChannelActive();
        // Discard the connection preface + SETTINGS the handler wrote on activation.
        channel.ReleaseOutbound();

        IChannelHandlerContext ctx = channel.Pipeline.Context(handler);
        _ = handler.Encoder().WriteHeadersAsync(ctx, 3, new DefaultHttp2Headers(), 0, false, new TaskCompletionSource());

        // Larger than the write high-watermark but within the default 65535-byte flow-control window, so only
        // channel writability (not flow control) gates the write.
        int size = 60 * 1024;
        ByteBuf data = Unpooled.Buffer(size).WriteZero(size);
        TaskCompletionSource dataPromise = new TaskCompletionSource();
        _ = handler.Encoder().WriteDataAsync(ctx, 3, data, 0, false, dataPromise);
        handler.Flush(ctx);
        Assert.True(dataPromise.Task.IsCompletedSuccessfully);
        channel.FinishAndReleaseAll();
    }

    [Fact]
    public void ServerReceivingInvalidClientPrefaceStringShouldHandleException()
    {
        connection.Setup(c => c.IsServer()).Returns(true);
        handler = NewHandler();
        CaptureGoAwayDebugData();
        handler.ChannelRead(ctx.Object, CopiedBuffer("BAD_PREFACE", Encoding.UTF8));
        frameWriter.Verify(w => w.WriteGoAwayAsync(ctx.Object, int.MaxValue, PROTOCOL_ERROR.Code(),
                It.IsAny<ByteBuf>(), It.IsAny<TaskCompletionSource>()));
        Assert.Equal(0, LastCaptured().RefCnt);
    }

    [Fact]
    public void ServerReceivingInvalidClientSettingsAfterPrefaceShouldHandleException()
    {
        ByteBuf buf = ctx.Object.Alloc().Buffer(FRAME_HEADER_LENGTH);
        WriteFrameHeaderInternal(buf, 0, SETTINGS, new Http2Flags().Ack(true), 0);

        connection.Setup(c => c.IsServer()).Returns(true);
        handler = NewHandler();
        CaptureGoAwayDebugData();
        handler.ChannelRead(ctx.Object, Unpooled.WrappedBuffer(ConnectionPrefaceBuf(), buf));
        frameWriter.Verify(w => w.WriteGoAwayAsync(ctx.Object, int.MaxValue, PROTOCOL_ERROR.Code(),
                It.IsAny<ByteBuf>(), It.IsAny<TaskCompletionSource>()));
        Assert.Equal(0, LastCaptured().RefCnt);
    }

    [Fact]
    public void ServerReceivingHttp1ClientPrefaceStringShouldIncludePreface()
    {
        connection.Setup(c => c.IsServer()).Returns(true);
        handler = NewHandler();
        CaptureGoAwayDebugData();
        handler.ChannelRead(ctx.Object, CopiedBuffer("GET /path HTTP/1.1", Encoding.ASCII));
        frameWriter.Verify(w => w.WriteGoAwayAsync(ctx.Object, int.MaxValue, PROTOCOL_ERROR.Code(),
                It.IsAny<ByteBuf>(), It.IsAny<TaskCompletionSource>()));
        Assert.Equal(0, LastCaptured().RefCnt);
        Assert.True(goAwayDebugCap.Contains("/path"));
    }

    [Fact]
    public void ServerReceivingClientPrefaceStringFollowedByNonSettingsShouldHandleException()
    {
        connection.Setup(c => c.IsServer()).Returns(true);
        handler = NewHandler();

        // Create a connection preface followed by a bunch of zeros (i.e. not a settings frame).
        ByteBuf buf = Unpooled.Buffer().WriteBytes(ConnectionPrefaceBuf()).WriteZero(10);
        CaptureGoAwayDebugData();
        handler.ChannelRead(ctx.Object, buf);
        frameWriter.Verify(w => w.WriteGoAwayAsync(ctx.Object, int.MaxValue, PROTOCOL_ERROR.Code(),
                It.IsAny<ByteBuf>(), It.IsAny<TaskCompletionSource>()), Times.AtLeastOnce());
        Assert.Equal(0, LastCaptured().RefCnt);
    }

    [Fact]
    public void ServerReceivingValidClientPrefaceStringShouldContinueReadingFrames()
    {
        connection.Setup(c => c.IsServer()).Returns(true);
        handler = NewHandler();
        ByteBuf prefacePlusSome = AddSettingsHeader(Unpooled.Buffer().WriteBytes(ConnectionPrefaceBuf()));
        handler.ChannelRead(ctx.Object, prefacePlusSome);
        decoder.Verify(d => d.DecodeFrame(It.IsAny<IChannelHandlerContext>(),
                It.IsAny<ByteBuf>(), It.IsAny<IList<object>>()), Times.AtLeastOnce());
    }

    [Fact]
    public void VerifyChannelHandlerCanBeReusedInPipeline()
    {
        connection.Setup(c => c.IsServer()).Returns(true);
        handler = NewHandler();
        // Only read the connection preface...after preface is read internal state of Http2ConnectionHandler
        // is expected to change relative to the pipeline.
        ByteBuf preface = ConnectionPrefaceBuf();
        handler.ChannelRead(ctx.Object, preface);
        decoder.Verify(d => d.DecodeFrame(It.IsAny<IChannelHandlerContext>(),
                It.IsAny<ByteBuf>(), It.IsAny<IList<object>>()), Times.Never());

        // Now remove and add the handler...this is setting up the test condition.
        handler.HandlerRemoved(ctx.Object);
        handler.HandlerAdded(ctx.Object);

        // Now verify we can continue as normal, reading connection preface plus more.
        ByteBuf prefacePlusSome = AddSettingsHeader(Unpooled.Buffer().WriteBytes(ConnectionPrefaceBuf()));
        handler.ChannelRead(ctx.Object, prefacePlusSome);
        decoder.Verify(d => d.DecodeFrame(ctx.Object, It.IsAny<ByteBuf>(), It.IsAny<IList<object>>()),
                Times.AtLeastOnce());
    }

    [Fact]
    public void ChannelInactiveShouldCloseStreams()
    {
        handler = NewHandler();
        handler.ChannelInactive(ctx.Object);
        connection.Verify(c => c.CloseAsync(It.IsAny<TaskCompletionSource>()));
    }

    [Fact]
    public void ConnectionErrorShouldStartShutdown()
    {
        handler = NewHandler();
        Http2Exception e = new Http2Exception(PROTOCOL_ERROR);
        // There's no guarantee that lastStreamCreated in correct, as the error could have occurred during header
        // processing before it was updated. Thus, it should _not_ be used for the GOAWAY.
        // https://github.com/netty/netty/issues/10670
        remote.Setup(r => r.LastStreamCreated()).Returns(STREAM_ID);
        CaptureGoAwayDebugData();
        handler.ExceptionCaught(ctx.Object, e);
        frameWriter.Verify(w => w.WriteGoAwayAsync(ctx.Object, int.MaxValue, PROTOCOL_ERROR.Code(),
                It.IsAny<ByteBuf>(), It.IsAny<TaskCompletionSource>()));
        // CLR: Java's exception has no message, so its debug data is the unreleasable EMPTY_BUFFER and the extra
        // release() does nothing; a CLR exception always has a Message (Http2CodecUtil.ToByteBuf), so the debug data
        // is a buffer that goAway(...) already released.
        ByteBuf debugData = LastCaptured();
        if (debugData.RefCnt > 0)
        {
            debugData.Release();
        }
    }

    [Fact]
    public void CompositeStreamExceptionReportsEachAffectedStream()
    {
        handler = NewHandler();
        Http2Exception.StreamException streamException1 =
                new Http2Exception.StreamException(STREAM_ID, PROTOCOL_ERROR, "stream 1 error");
        Http2Exception.StreamException streamException2 =
                new Http2Exception.StreamException(NON_EXISTANT_STREAM_ID, PROTOCOL_ERROR, "stream 2 error");
        Http2Exception.CompositeStreamException compositeException =
                new Http2Exception.CompositeStreamException(PROTOCOL_ERROR, 2);
        compositeException.Add(streamException1);
        compositeException.Add(streamException2);

        stream.Setup(s => s.Id()).Returns(STREAM_ID);
        encoder.Setup(e => e.WriteRstStreamAsync(ctx.Object, It.IsAny<int>(), It.IsAny<long>(),
                It.IsAny<TaskCompletionSource>())).Returns(() => future);

        handler.ExceptionCaught(ctx.Object, compositeException);

        // Each StreamException in the composite represents an independent error for a distinct stream
        // (e.g. one per active stream whose flow-control window overflowed when the initial window size
        // setting changed). Every affected stream must be reset individually, otherwise it would be left
        // open with a corrupted flow-control window.
        encoder.Verify(e => e.WriteRstStreamAsync(ctx.Object, It.IsAny<int>(), It.IsAny<long>(),
                It.IsAny<TaskCompletionSource>()), Times.Exactly(2));
        encoder.Verify(e => e.WriteRstStreamAsync(ctx.Object, STREAM_ID, PROTOCOL_ERROR.Code(),
                It.IsAny<TaskCompletionSource>()));
        encoder.Verify(e => e.WriteRstStreamAsync(ctx.Object, NON_EXISTANT_STREAM_ID, PROTOCOL_ERROR.Code(),
                It.IsAny<TaskCompletionSource>()));
    }

    [Fact]
    public void CompositeStreamExceptionOnlyReportsFirstErrorForSameStream()
    {
        handler = NewHandler();
        Http2Exception.StreamException streamException1 =
                new Http2Exception.StreamException(STREAM_ID, PROTOCOL_ERROR, "first error");
        Http2Exception.StreamException streamException2 =
                new Http2Exception.StreamException(STREAM_ID, CANCEL, "second error");
        Http2Exception.CompositeStreamException compositeException =
                new Http2Exception.CompositeStreamException(PROTOCOL_ERROR, 2);
        compositeException.Add(streamException1);
        compositeException.Add(streamException2);

        stream.Setup(s => s.Id()).Returns(STREAM_ID);
        encoder.Setup(e => e.WriteRstStreamAsync(ctx.Object, It.IsAny<int>(), It.IsAny<long>(),
                It.IsAny<TaskCompletionSource>())).Returns(() => future);

        handler.ExceptionCaught(ctx.Object, compositeException);

        // RFC 9113, Section 5.4: implementations SHOULD report at most one stream error per stream. Only the
        // first StreamException seen for a given stream id should result in a RST_STREAM.
        encoder.Verify(e => e.WriteRstStreamAsync(ctx.Object, It.IsAny<int>(), It.IsAny<long>(),
                It.IsAny<TaskCompletionSource>()), Times.Exactly(1));
        encoder.Verify(e => e.WriteRstStreamAsync(ctx.Object, STREAM_ID, PROTOCOL_ERROR.Code(),
                It.IsAny<TaskCompletionSource>()));
    }

    [Fact]
    public void ServerShouldSend431OnHeaderSizeErrorWhenDecodingInitialHeaders()
    {
        int padding = 0;
        handler = NewHandler();
        Http2Exception e = new Http2Exception.HeaderListSizeException(STREAM_ID, PROTOCOL_ERROR,
                "Header size exceeded max allowed size 8196", true);

        stream.Setup(s => s.Id()).Returns(STREAM_ID);
        connection.Setup(c => c.IsServer()).Returns(true);
        stream.Setup(s => s.IsHeadersSent()).Returns(false);
        remote.Setup(r => r.LastStreamCreated()).Returns(STREAM_ID);
        encoder.Setup(en => en.WriteRstStreamAsync(ctx.Object, STREAM_ID,
                PROTOCOL_ERROR.Code(), It.IsAny<TaskCompletionSource>())).Returns(() => future);

        // CLR: the ArgumentCaptor<Http2Headers> is a Callback that keeps the headers of writeHeaders(...).
        IHttp2Headers headers = null;
        encoder.Setup(en => en.WriteHeadersAsync(ctx.Object, STREAM_ID, It.IsAny<IHttp2Headers>(), padding, true,
                It.IsAny<TaskCompletionSource>()))
            .Callback((IChannelHandlerContext c, int id, IHttp2Headers h, int p, bool end, TaskCompletionSource pr) =>
                headers = h);

        handler.ExceptionCaught(ctx.Object, e);

        encoder.Verify(en => en.WriteHeadersAsync(ctx.Object, STREAM_ID,
                It.IsAny<IHttp2Headers>(), padding, true, It.IsAny<TaskCompletionSource>()));
        Assert.True(AsciiString.ContentEquals(HttpResponseStatus.REQUEST_HEADER_FIELDS_TOO_LARGE.CodeAsText(),
                headers.Status()));
        encoder.Verify(en => en.WriteRstStreamAsync(ctx.Object, STREAM_ID, PROTOCOL_ERROR.Code(),
                It.IsAny<TaskCompletionSource>()));
    }

    [Fact]
    public void ServerShouldNeverSend431HeaderSizeErrorWhenEncoding()
    {
        int padding = 0;
        handler = NewHandler();
        Http2Exception e = new Http2Exception.HeaderListSizeException(STREAM_ID, PROTOCOL_ERROR,
            "Header size exceeded max allowed size 8196", false);

        stream.Setup(s => s.Id()).Returns(STREAM_ID);
        connection.Setup(c => c.IsServer()).Returns(true);
        stream.Setup(s => s.IsHeadersSent()).Returns(false);
        remote.Setup(r => r.LastStreamCreated()).Returns(STREAM_ID);
        encoder.Setup(en => en.WriteRstStreamAsync(ctx.Object, STREAM_ID,
            PROTOCOL_ERROR.Code(), It.IsAny<TaskCompletionSource>())).Returns(() => future);

        handler.ExceptionCaught(ctx.Object, e);

        encoder.Verify(en => en.WriteHeadersAsync(ctx.Object, STREAM_ID,
            It.IsAny<IHttp2Headers>(), padding, true, It.IsAny<TaskCompletionSource>()), Times.Never());
        encoder.Verify(en => en.WriteRstStreamAsync(ctx.Object, STREAM_ID, PROTOCOL_ERROR.Code(),
                It.IsAny<TaskCompletionSource>()));
    }

    [Fact]
    public void ClientShouldNeverSend431WhenHeadersAreTooLarge()
    {
        int padding = 0;
        handler = NewHandler();
        Http2Exception e = new Http2Exception.HeaderListSizeException(STREAM_ID, PROTOCOL_ERROR,
                "Header size exceeded max allowed size 8196", true);

        stream.Setup(s => s.Id()).Returns(STREAM_ID);
        connection.Setup(c => c.IsServer()).Returns(false);
        stream.Setup(s => s.IsHeadersSent()).Returns(false);
        remote.Setup(r => r.LastStreamCreated()).Returns(STREAM_ID);
        encoder.Setup(en => en.WriteRstStreamAsync(ctx.Object, STREAM_ID,
                PROTOCOL_ERROR.Code(), It.IsAny<TaskCompletionSource>())).Returns(() => future);

        handler.ExceptionCaught(ctx.Object, e);

        encoder.Verify(en => en.WriteHeadersAsync(ctx.Object, STREAM_ID,
                It.IsAny<IHttp2Headers>(), padding, true, It.IsAny<TaskCompletionSource>()), Times.Never());
        encoder.Verify(en => en.WriteRstStreamAsync(ctx.Object, STREAM_ID, PROTOCOL_ERROR.Code(),
                It.IsAny<TaskCompletionSource>()));
    }

    [Fact]
    public void PrefaceUserEventProcessed()
    {
        CountdownEvent latch = new CountdownEvent(1);
        handler = new PrefaceEventHandler(decoder.Object, encoder.Object, new Http2Settings(), latch);
        handler.HandlerAdded(ctx.Object);
        Assert.True(latch.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    // CLR: the anonymous Http2ConnectionHandler subclass of prefaceUserEventProcessed().
    private sealed class PrefaceEventHandler : Http2ConnectionHandler
    {
        private readonly CountdownEvent latch;

        internal PrefaceEventHandler(IHttp2ConnectionDecoder decoder, IHttp2ConnectionEncoder encoder,
                                     Http2Settings initialSettings, CountdownEvent latch)
            : base(decoder, encoder, initialSettings)
        {
            this.latch = latch;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt == Http2ConnectionPrefaceAndSettingsFrameWrittenEvent.INSTANCE)
            {
                latch.Signal();
            }
        }
    }

    [Fact]
    public void ServerShouldNeverSend431IfHeadersAlreadySent()
    {
        int padding = 0;
        handler = NewHandler();
        Http2Exception e = new Http2Exception.HeaderListSizeException(STREAM_ID, PROTOCOL_ERROR,
            "Header size exceeded max allowed size 8196", true);

        stream.Setup(s => s.Id()).Returns(STREAM_ID);
        connection.Setup(c => c.IsServer()).Returns(true);
        stream.Setup(s => s.IsHeadersSent()).Returns(true);
        remote.Setup(r => r.LastStreamCreated()).Returns(STREAM_ID);
        encoder.Setup(en => en.WriteRstStreamAsync(ctx.Object, STREAM_ID,
            PROTOCOL_ERROR.Code(), It.IsAny<TaskCompletionSource>())).Returns(() => future);
        handler.ExceptionCaught(ctx.Object, e);

        encoder.Verify(en => en.WriteHeadersAsync(ctx.Object, STREAM_ID,
            It.IsAny<IHttp2Headers>(), padding, true, It.IsAny<TaskCompletionSource>()), Times.Never());

        encoder.Verify(en => en.WriteRstStreamAsync(ctx.Object, STREAM_ID, PROTOCOL_ERROR.Code(),
                It.IsAny<TaskCompletionSource>()));
    }

    [Fact]
    public void ServerShouldCreateStreamIfNeededBeforeSending431()
    {
        int padding = 0;
        handler = NewHandler();
        Http2Exception e = new Http2Exception.HeaderListSizeException(STREAM_ID, PROTOCOL_ERROR,
            "Header size exceeded max allowed size 8196", true);

        connection.Setup(c => c.Stream(STREAM_ID)).Returns((IHttp2Stream) null);
        remote.Setup(r => r.CreateStream(STREAM_ID, true)).Returns(stream.Object);
        stream.Setup(s => s.Id()).Returns(STREAM_ID);

        connection.Setup(c => c.IsServer()).Returns(true);
        stream.Setup(s => s.IsHeadersSent()).Returns(false);
        remote.Setup(r => r.LastStreamCreated()).Returns(STREAM_ID);
        encoder.Setup(en => en.WriteRstStreamAsync(ctx.Object, STREAM_ID,
            PROTOCOL_ERROR.Code(), It.IsAny<TaskCompletionSource>())).Returns(() => future);
        handler.ExceptionCaught(ctx.Object, e);

        remote.Verify(r => r.CreateStream(STREAM_ID, true));
        encoder.Verify(en => en.WriteHeadersAsync(ctx.Object, STREAM_ID,
            It.IsAny<IHttp2Headers>(), padding, true, It.IsAny<TaskCompletionSource>()));

        encoder.Verify(en => en.WriteRstStreamAsync(ctx.Object, STREAM_ID, PROTOCOL_ERROR.Code(),
                It.IsAny<TaskCompletionSource>()));
    }

    [Fact]
    public void EncoderAndDecoderAreClosedOnChannelInactive()
    {
        handler = NewHandler();
        handler.ChannelActive(ctx.Object);
        channel.Setup(c => c.IsActive).Returns(false);
        handler.ChannelInactive(ctx.Object);
        encoder.Verify(e => e.Close());
        decoder.Verify(d => d.Close());
    }

    [Fact]
    public void WriteRstOnNonExistantStreamShouldSucceed()
    {
        handler = NewHandler();
        frameWriter.Setup(w => w.WriteRstStreamAsync(ctx.Object, NON_EXISTANT_STREAM_ID,
                                        STREAM_CLOSED.Code(), promise)).Returns(() => future);
        _ = handler.ResetStreamAsync(ctx.Object, NON_EXISTANT_STREAM_ID, STREAM_CLOSED.Code(), promise);
        frameWriter.Verify(w => w.WriteRstStreamAsync(ctx.Object, NON_EXISTANT_STREAM_ID, STREAM_CLOSED.Code(),
                promise));
    }

    [Fact]
    public void WriteRstOnClosedStreamShouldSucceed()
    {
        handler = NewHandler();
        stream.Setup(s => s.Id()).Returns(STREAM_ID);
        frameWriter.Setup(w => w.WriteRstStreamAsync(ctx.Object, STREAM_ID,
                It.IsAny<long>(), It.IsAny<TaskCompletionSource>())).Returns(() => future);
        stream.Setup(s => s.State_()).Returns(IHttp2Stream.State.CLOSED);
        stream.Setup(s => s.IsHeadersSent()).Returns(true);
        // The stream is "closed" but is still known about by the connection (connection().stream(..)
        // will return the stream). We should still write a RST_STREAM frame in this scenario.
        _ = handler.ResetStreamAsync(ctx.Object, STREAM_ID, STREAM_CLOSED.Code(), promise);
        frameWriter.Verify(w => w.WriteRstStreamAsync(ctx.Object, STREAM_ID, It.IsAny<long>(),
                It.IsAny<TaskCompletionSource>()));
    }

    [Fact]
    public void WriteRstOnIdleStreamShouldNotWriteButStillSucceed()
    {
        handler = NewHandler();
        stream.Setup(s => s.State_()).Returns(IHttp2Stream.State.IDLE);
        _ = handler.ResetStreamAsync(ctx.Object, STREAM_ID, STREAM_CLOSED.Code(), promise);
        frameWriter.Verify(w => w.WriteRstStreamAsync(ctx.Object, STREAM_ID, It.IsAny<long>(),
                It.IsAny<TaskCompletionSource>()), Times.Never());
        stream.Verify(s => s.Close());
    }

    [Fact]
    public void CloseListenerShouldBeNotifiedOnlyOneTime()
    {
        handler = NewHandler();
        future = Task.CompletedTask;
        // CLR: a Task has no addListener(...) to stub. Java's answer is not reached either: the future is done, so
        // close(...) keeps the listener until the stream is closed below. Its comments:
        // Simulate that all streams have become inactive by the time the future completes.
        // Simulate the future being completed.
        _ = handler.CloseAsync(ctx.Object, CancellationToken.None);

        if (future.IsCompleted)
        {
            connection.Setup(c => c.NumActiveStreams()).Returns(0);
        }

        handler.CloseStream(stream.Object, future);
        // Simulate another stream close call being made after the context should already be closed.
        handler.CloseStream(stream.Object, future);
        ctx.Verify(c => c.CloseAsync(It.IsAny<CancellationToken>()), Times.Exactly(1));
    }

    [Fact]
    public void CanSendGoAwayFrame()
    {
        ByteBuf data = DummyData();
        long errorCode = Http2Error.INTERNAL_ERROR.Code();
        future = Task.CompletedTask;
        handler = NewHandler();
        _ = handler.GoAwayAsync(ctx.Object, STREAM_ID, errorCode, data, promise);

        connection.Verify(c => c.GoAwaySent(STREAM_ID, errorCode, data));
        frameWriter.Verify(w => w.WriteGoAwayAsync(ctx.Object, STREAM_ID, errorCode, data,
                promise));
        ctx.Verify(c => c.CloseAsync(It.IsAny<CancellationToken>()));
        Assert.Equal(0, data.RefCnt);
    }

    [Fact]
    public void CanSendGoAwayFramesWithDecreasingLastStreamIds()
    {
        handler = NewHandler();
        ByteBuf data = DummyData();
        long errorCode = Http2Error.INTERNAL_ERROR.Code();

        _ = handler.GoAwayAsync(ctx.Object, STREAM_ID + 2, errorCode, data.Retain(), promise);
        frameWriter.Verify(w => w.WriteGoAwayAsync(ctx.Object, STREAM_ID + 2, errorCode, data,
                promise));
        connection.Verify(c => c.GoAwaySent(STREAM_ID + 2, errorCode, data));
        promise = new TaskCompletionSource();
        _ = handler.GoAwayAsync(ctx.Object, STREAM_ID, errorCode, data, promise);
        frameWriter.Verify(w => w.WriteGoAwayAsync(ctx.Object, STREAM_ID, errorCode, data, promise));
        connection.Verify(c => c.GoAwaySent(STREAM_ID, errorCode, data));
        Assert.Equal(0, data.RefCnt);
    }

    [Fact]
    public void CannotSendGoAwayFrameWithIncreasingLastStreamIds()
    {
        handler = NewHandler();
        ByteBuf data = DummyData();
        long errorCode = Http2Error.INTERNAL_ERROR.Code();

        _ = handler.GoAwayAsync(ctx.Object, STREAM_ID, errorCode, data.Retain(), promise);
        connection.Verify(c => c.GoAwaySent(STREAM_ID, errorCode, data));
        frameWriter.Verify(w => w.WriteGoAwayAsync(ctx.Object, STREAM_ID, errorCode, data, promise));
        // The frameWriter is only mocked, so it should not have interacted with the promise.
        Assert.False(promise.Task.IsCompleted);

        connection.Setup(c => c.GoAwaySent()).Returns(true);
        remote.Setup(r => r.LastStreamKnownByPeer()).Returns(STREAM_ID);
        connection.Setup(c => c.GoAwaySent(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<ByteBuf>()))
            .Throws(new InvalidOperationException());
        _ = handler.GoAwayAsync(ctx.Object, STREAM_ID + 2, errorCode, data, promise);
        Assert.True(promise.Task.IsCompleted);
        Assert.False(promise.Task.IsCompletedSuccessfully);
        Assert.Equal(0, data.RefCnt);
        frameWriter.VerifyNoOtherCalls();
    }

    [Fact]
    public void CanSendGoAwayUsingVoidPromise()
    {
        handler = NewHandler();
        ByteBuf data = DummyData();
        long errorCode = Http2Error.INTERNAL_ERROR.Code();
        handler = NewHandler();
        frameWriter.Setup(w => w.WriteGoAwayAsync(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<long>(), It.IsAny<ByteBuf>(), It.IsAny<TaskCompletionSource>()))
            .Returns((IChannelHandlerContext c, int l, long e, ByteBuf buf, TaskCompletionSource p) =>
            {
                // CLR: promise.isVoid() has no counterpart (there are no void promises); the handler passes the
                // promise it was given.
                // This is what DefaultHttp2FrameWriter does... I hate mocking :-(.
                SimpleChannelPromiseAggregator aggregatedPromise =
                        new SimpleChannelPromiseAggregator(p, channel.Object, ImmediateEventExecutor.INSTANCE);
                aggregatedPromise.NewPromise();
                aggregatedPromise.DoneAllocatingPromises();
                return aggregatedPromise.SetFailure(Http2TestUtil.FAKE_EXCEPTION).Task;
            });
        _ = handler.GoAwayAsync(ctx.Object, STREAM_ID, errorCode, data, Http2TestUtil.NewVoidPromise(channel.Object));
        pipeline.Verify(p => p.FireExceptionCaught(Http2TestUtil.FAKE_EXCEPTION));
    }

    [Fact]
    public void CanCloseStreamWithVoidPromise()
    {
        handler = NewHandler();
        voidPromise.SetResult();
        handler.CloseStream(stream.Object, voidPromise.Task);
        stream.Verify(s => s.Close(), Times.Exactly(1));
        stream.VerifyNoOtherCalls();
    }

    [Fact]
    public void ChannelReadCompleteTriggersFlush()
    {
        // Create the handler in a way that it will flush the preface by itself
        handler = NewHandler(false);
        handler.ChannelReadComplete(ctx.Object);
        ctx.Verify(c => c.Flush(), Times.Exactly(1));
    }

    [Fact]
    public void ChannelReadCompleteCallsReadWhenAutoReadFalse()
    {
        channel.Object.Config.SetAutoRead(false);
        handler = NewHandler();
        handler.ChannelReadComplete(ctx.Object);
        ctx.Verify(c => c.Read(), Times.Exactly(1));
    }

    [Fact]
    public void ChannelClosedDoesNotThrowPrefaceException()
    {
        connection.Setup(c => c.IsServer()).Returns(true);
        handler = NewHandler();
        channel.Setup(c => c.IsActive).Returns(false);
        handler.ChannelInactive(ctx.Object);
        frameWriter.Verify(w => w.WriteGoAwayAsync(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<long>(), It.IsAny<ByteBuf>(), It.IsAny<TaskCompletionSource>()), Times.Never());
        frameWriter.Verify(w => w.WriteRstStreamAsync(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<long>(), It.IsAny<TaskCompletionSource>()), Times.Never());
    }

    [Fact]
    public void ClientChannelClosedDoesNotSendGoAwayBeforePreface()
    {
        connection.Setup(c => c.IsServer()).Returns(false);
        channel.Setup(c => c.IsActive).Returns(false);
        handler = NewHandler();
        channel.Setup(c => c.IsActive).Returns(true);
        _ = handler.CloseAsync(ctx.Object, CancellationToken.None);
        frameWriter.VerifyNoOtherCalls();
    }

    [Fact]
    public void WriteRstStreamForUnknownStreamUsingVoidPromise()
    {
        WriteRstStreamUsingVoidPromise(NON_EXISTANT_STREAM_ID);
    }

    [Fact]
    public void WriteRstStreamForKnownStreamUsingVoidPromise()
    {
        WriteRstStreamUsingVoidPromise(STREAM_ID);
    }

    [Fact]
    public void GracefulShutdownTimeoutWhenConnectionErrorHardShutdownTest()
    {
        GracefulShutdownTimeoutWhenConnectionErrorTest0(Http2Exception.ShutdownHint.HARD_SHUTDOWN);
    }

    [Fact]
    public void GracefulShutdownTimeoutWhenConnectionErrorGracefulShutdownTest()
    {
        GracefulShutdownTimeoutWhenConnectionErrorTest0(Http2Exception.ShutdownHint.GRACEFUL_SHUTDOWN);
    }

    private void GracefulShutdownTimeoutWhenConnectionErrorTest0(Http2Exception.ShutdownHint hint)
    {
        handler = NewHandler();
        long expectedMillis = 1234;
        handler.GracefulShutdownTimeoutMillis(expectedMillis);
        Http2Exception exception = new Http2Exception(PROTOCOL_ERROR, "Test error", hint);
        handler.OnConnectionError(ctx.Object, false, exception, exception);
        Assert.Contains(TimeSpan.FromMilliseconds(expectedMillis), executor.ScheduledDelays);
    }

    [Fact]
    public void GracefulShutdownTimeoutTest()
    {
        handler = NewHandler();
        long expectedMillis = 1234;
        handler.GracefulShutdownTimeoutMillis(expectedMillis);
        _ = handler.CloseAsync(ctx.Object, CancellationToken.None);
        Assert.Contains(TimeSpan.FromMilliseconds(expectedMillis), executor.ScheduledDelays);
    }

    [Fact]
    public void GracefulShutdownTimeoutNoActiveStreams()
    {
        handler = NewHandler();
        connection.Setup(c => c.NumActiveStreams()).Returns(0);
        long expectedMillis = 1234;
        handler.GracefulShutdownTimeoutMillis(expectedMillis);
        _ = handler.CloseAsync(ctx.Object, CancellationToken.None);
        Assert.Contains(TimeSpan.FromMilliseconds(expectedMillis), executor.ScheduledDelays);
    }

    [Fact]
    public void GracefulShutdownIndefiniteTimeoutTest()
    {
        handler = NewHandler();
        handler.GracefulShutdownTimeoutMillis(-1);
        _ = handler.CloseAsync(ctx.Object, CancellationToken.None);
        Assert.Empty(executor.ScheduledDelays);
    }

    // CLR: client.unsafe().close(client.voidPromise()) closes the transport without passing through the pipeline's
    // outbound handlers; the folded unsafe operations of the Transport port are internal (docs/transport-porting.md,
    // EmbeddedChannel), so the test closes from the handler's own context, whose close goes to the pipeline head
    // only (ctx.close() "will be forwarded to the next ChannelOutboundHandler", which is the head here).
    // client.pipeline().get(Class) is Pipeline.Get<T>(), server.newPromise() a new TaskCompletionSource and
    // ChannelFuture.isDone()/isSuccess() Task.IsCompleted/IsCompletedSuccessfully.
    [Theory]
    [InlineData(30000L)]
    [InlineData(-1L)]
    public void GracefulShutdownCompletesWhenGoAwayClosesLastStream(long gracefulShutdownTimeoutMillis)
    {
        Http2ConnectionHandler serverHandler = new Http2ConnectionHandlerBuilder()
                .Server(true).FrameListener(new Http2FrameAdapter()).Build();
        EmbeddedChannel server = new EmbeddedChannel(serverHandler);
        EmbeddedChannel client = NewClientChannel(gracefulShutdownTimeoutMillis);
        Http2ConnectionHandler clientHandler = client.Pipeline.Get<Http2ConnectionHandler>();

        try
        {
            ExchangeFrames(client, server);
            WriteHeadersOnStream3(clientHandler, client);

            // Close via the pipeline: EmbeddedChannel.close() would cancel the graceful shutdown timeout task.
            Task closeFuture = client.Pipeline.CloseAsync(TestContext.Current.CancellationToken);
            Assert.False(closeFuture.IsCompleted);

            // The server shuts down before it has seen stream 3 and answers with a GOAWAY that does not include it.
            client.ReleaseOutbound();
            _ = serverHandler.GoAwayAsync(server.Pipeline.FirstContext, 0, Http2Error.NO_ERROR.Code(),
                    Unpooled.EmptyBuffer, new TaskCompletionSource());
            server.Flush();
            ExchangeFrames(server, client);
            client.RunPendingTasks();

            Assert.Equal(0, clientHandler.Connection().NumActiveStreams());
            Assert.True(closeFuture.IsCompletedSuccessfully, "close() must complete once the GOAWAY closed the last stream");
            Assert.False(client.IsOpen);
        }
        finally
        {
            client.FinishAndReleaseAll();
            server.FinishAndReleaseAll();
        }
    }

    [Theory]
    [InlineData(30000L)]
    [InlineData(-1L)]
    public void GracefulShutdownCompletesWhenChannelBecomesInactive(long gracefulShutdownTimeoutMillis)
    {
        Http2ConnectionHandler serverHandler = new Http2ConnectionHandlerBuilder()
                .Server(true).FrameListener(new Http2FrameAdapter()).Build();
        EmbeddedChannel server = new EmbeddedChannel(serverHandler);
        EmbeddedChannel client = NewClientChannel(gracefulShutdownTimeoutMillis);
        Http2ConnectionHandler clientHandler = client.Pipeline.Get<Http2ConnectionHandler>();

        try
        {
            ExchangeFrames(client, server);
            WriteHeadersOnStream3(clientHandler, client);

            // Close via the pipeline: EmbeddedChannel.close() would cancel the graceful shutdown timeout task.
            Task closeFuture = client.Pipeline.CloseAsync(TestContext.Current.CancellationToken);
            Assert.False(closeFuture.IsCompleted);

            // The connection is lost while the graceful shutdown waits for stream 3.
            _ = client.Pipeline.Context(clientHandler).CloseAsync(TestContext.Current.CancellationToken);
            client.RunPendingTasks();

            Assert.False(client.IsOpen);
            Assert.Equal(0, clientHandler.Connection().NumActiveStreams());
            Assert.True(closeFuture.IsCompletedSuccessfully, "close() must complete once the channel is closed");
        }
        finally
        {
            client.FinishAndReleaseAll();
            server.FinishAndReleaseAll();
        }
    }

    private static EmbeddedChannel NewClientChannel(long gracefulShutdownTimeoutMillis)
    {
        EmbeddedChannel client = new EmbeddedChannel(new Http2ConnectionHandlerBuilder()
                .Server(false).FrameListener(new Http2FrameAdapter())
                .GracefulShutdownTimeoutMillis(gracefulShutdownTimeoutMillis).Build());
        // The graceful shutdown timeout must not elapse during the test.
        client.FreezeTime();
        return client;
    }

    // CLR: the String pseudo-header values are StringCharSequence values (a C# string is not an ICharSequence;
    // docs/codec-http-porting.md, Shared CLR adaptations).
    private static void WriteHeadersOnStream3(Http2ConnectionHandler clientHandler, EmbeddedChannel client)
    {
        IChannelHandlerContext ctx = client.Pipeline.Context(clientHandler);
        _ = clientHandler.Encoder().WriteHeadersAsync(ctx, 3,
                new DefaultHttp2Headers().Method(new StringCharSequence("GET")).Path(new StringCharSequence("/"))
                        .Scheme(new StringCharSequence("http")).Authority(new StringCharSequence("netty.io")),
                0, false, new TaskCompletionSource());
        client.Flush();
        Assert.Equal(1, clientHandler.Connection().NumActiveStreams());
    }

    private static void ExchangeFrames(EmbeddedChannel from, EmbeddedChannel to)
    {
        for (int i = 0; i < 5; i++)
        {
            TransferFrames(from, to);
            TransferFrames(to, from);
        }
    }

    private static void TransferFrames(EmbeddedChannel from, EmbeddedChannel to)
    {
        for (object msg; (msg = from.ReadOutbound()) != null;)
        {
            if (to.IsOpen)
            {
                to.WriteInbound(msg);
            }
            else
            {
                ReferenceCountUtil.Release(msg);
            }
        }
    }

    [Fact]
    public void WriteMultipleRstFramesForSameStream()
    {
        handler = NewHandler();
        stream.Setup(s => s.Id()).Returns(STREAM_ID);

        bool resetSent = false;
        stream.Setup(s => s.ResetSent()).Returns(() =>
        {
            resetSent = true;
            return stream.Object;
        });
        stream.Setup(s => s.IsResetSent()).Returns(() => resetSent);
        frameWriter.Setup(w => w.WriteRstStreamAsync(ctx.Object, STREAM_ID, It.IsAny<long>(),
                It.IsAny<TaskCompletionSource>()))
                .Returns((IChannelHandlerContext c, int streamId, long errorCode, TaskCompletionSource p) =>
                {
                    p.SetResult();
                    return p.Task;
                });

        TaskCompletionSource promise = new TaskCompletionSource();
        TaskCompletionSource promise2 = new TaskCompletionSource();
        _ = promise.Task.ContinueWith(f => handler.ResetStreamAsync(ctx.Object, STREAM_ID, STREAM_CLOSED.Code(),
                promise2), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

        _ = handler.ResetStreamAsync(ctx.Object, STREAM_ID, CANCEL.Code(), promise);
        frameWriter.Verify(w => w.WriteRstStreamAsync(ctx.Object, STREAM_ID, It.IsAny<long>(),
                It.IsAny<TaskCompletionSource>()));
        Assert.True(promise.Task.IsCompletedSuccessfully);
        Assert.True(promise2.Task.IsCompletedSuccessfully);
    }

    private void WriteRstStreamUsingVoidPromise(int streamId)
    {
        handler = NewHandler();
        stream.Setup(s => s.Id()).Returns(STREAM_ID);
        frameWriter.Setup(w => w.WriteRstStreamAsync(ctx.Object, streamId, It.IsAny<long>(),
                It.IsAny<TaskCompletionSource>()))
                .Returns((IChannelHandlerContext c, int id, long errorCode, TaskCompletionSource p) =>
                {
                    // CLR: promise.isVoid() has no counterpart (there are no void promises).
                    p.SetException(Http2TestUtil.FAKE_EXCEPTION);
                    return p.Task;
                });
        _ = handler.ResetStreamAsync(ctx.Object, streamId, STREAM_CLOSED.Code(),
                Http2TestUtil.NewVoidPromise(channel.Object));
        frameWriter.Verify(w => w.WriteRstStreamAsync(ctx.Object, streamId, It.IsAny<long>(),
                It.IsAny<TaskCompletionSource>()));
        pipeline.Verify(p => p.FireExceptionCaught(Http2TestUtil.FAKE_EXCEPTION));
    }

    private static ByteBuf DummyData()
    {
        return Unpooled.Buffer().WriteBytes(Encoding.UTF8.GetBytes("abcdefgh"));
    }

    private static ByteBuf AddSettingsHeader(ByteBuf buf)
    {
        buf.WriteMedium(Http2CodecUtil.SETTING_ENTRY_LENGTH);
        buf.WriteByte(Http2FrameTypes.SETTINGS);
        buf.WriteByte(0);
        buf.WriteInt(0);
        return buf;
    }

    // CLR: eq(connectionPrefaceBuf()) compares buffers with ByteBuf.equals, which is ByteBuf.Equals.
    private static bool IsConnectionPreface(object msg)
    {
        ByteBuf preface = ConnectionPrefaceBuf();

        try
        {
            return msg is ByteBuf buf && buf.Equals(preface);
        }
        finally
        {
            preface.Release();
        }
    }

    // CLR: ArgumentCaptor<ByteBuf> of the GOAWAY debug data: the buffers given to writeGoAway, in call order.
    private readonly List<ByteBuf> capturedDebugData = new List<ByteBuf>();

    private void CaptureGoAwayDebugData()
    {
        frameWriter.Setup(w => w.WriteGoAwayAsync(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<long>(), It.IsAny<ByteBuf>(), It.IsAny<TaskCompletionSource>()))
            .Returns((IChannelHandlerContext c, int lastStreamId, long errorCode, ByteBuf buf, TaskCompletionSource p) =>
            {
                capturedDebugData.Add(buf);
                goAwayDebugCap = buf.ToString(Encoding.UTF8);
                buf.Release();
                return future;
            });
    }

    private ByteBuf LastCaptured()
    {
        return capturedDebugData[capturedDebugData.Count - 1];
    }

    // CLR: the mocked EventExecutor; see the type comment.
    private sealed class RecordingEventExecutor : AbstractScheduledEventExecutor
    {
        internal readonly List<TimeSpan> ScheduledDelays = new List<TimeSpan>();

        [Obsolete("Records the delay of every scheduled task (ValidateScheduled is the only hook).")]
        protected override void ValidateScheduled(TimeSpan amount)
        {
            ScheduledDelays.Add(amount);
        }

        public override bool IsShuttingDown()
        {
            return false;
        }

        public override bool InEventLoop(Thread thread)
        {
            return true;
        }

        [Obsolete("Overrides the deprecated AbstractEventExecutor.Shutdown().")]
        public override void Shutdown()
        {
            // NOOP
        }

        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout)
        {
            throw new NotSupportedException();
        }
        public override Task TerminationFuture => throw new NotSupportedException();
        public override bool IsShutdown()
        {
            return false;
        }

        public override bool IsTerminated()
        {
            return false;
        }

        public override bool AwaitTermination(TimeSpan timeout)
        {
            return false;
        }

        public override void Execute(Action command)
        {
            command();
        }
    }
}
