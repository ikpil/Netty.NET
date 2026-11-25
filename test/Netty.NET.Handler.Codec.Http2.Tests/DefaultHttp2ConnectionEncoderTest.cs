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
using System.Threading.Tasks;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;
using static Netty.NET.Buffer.Unpooled;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using FlowControlled = Netty.NET.Handler.Codec.Http2.IHttp2RemoteFlowController.IFlowControlled;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Tests for {@link DefaultHttp2ConnectionEncoder}
 */
// CLR: @Mock fields and MockitoAnnotations.initMocks are Moq mocks created in the constructor, which runs the
// @BeforeEach setup() (xUnit creates a test class instance per test, https://xunit.net/docs/shared-context). Mockito
// when/doAnswer/verify/never/times are Moq Setup(...).Returns/Callback, Verify(...), Times.Never()/Times.Exactly(n)
// (https://github.com/devlooped/moq/wiki/Quickstart); verifyNoMoreInteractions is VerifyNoOtherCalls(). The
// ArgumentCaptor of addFlowControlled is a Callback that appends to a list (getValue() is the last element,
// getAllValues() the list), and InOrder is a list of the recorded lifecycle calls. ChannelPromise is
// TaskCompletionSource (newPromise() is a new TaskCompletionSource; setSuccess()/setFailure(e) are
// SetResult()/SetException(e) followed by returning the Task) and ChannelFuture is Task; isDone()/isSuccess() are
// IsCompleted/IsCompletedSuccessfully and cause() is Http2CodecUtil.Cause (docs/codec-http-porting.md, Shared CLR
// adaptations). The Java promises run their listeners on ImmediateEventExecutor; the encoder attaches its listeners
// with Http2CodecUtil.AddListener on ctx.Executor, so the mocked context returns ImmediateEventExecutor.INSTANCE. The
// stubs of ctx.newPromise()/newSucceededFuture() and channel.newFailedFuture()/unsafe() only serve DefaultChannelPromise
// and have no counterpart (the encoder creates TaskCompletionSource promises itself). ctx.alloc()/channel.alloc() are
// Alloc() and the channel's config is a DefaultChannelConfig. assertInstanceOf is Assert.IsAssignableFrom and
// assertThrows Assert.ThrowsAny (docs/codec-http-porting.md, Core test notes). A mocked ByteBuf is a Moq mock of the
// abstract ByteBuf (its constructor takes maxCapacity), whose release() is the abstract Release(int) called with 1.
// newVoidPromise(channel) is Http2TestUtil.NewVoidPromise(IChannel): a TaskCompletionSource whose failure calls
// channel.Pipeline.FireExceptionCaught(cause), Java's tryFailure override and unvoid() listener folded into one
// promise, because promise.unvoid() is the promise itself in the C# encoder (there is no void promise,
// docs/transport-porting.md: VoidChannelPromise is JVM-only); promise.isVoid() has no counterpart.
// dataFramesShouldMergeUseVoidPromise is CLR-inapplicable: Java's void promise is never completed because unvoid()
// returns a new promise, while here the same promise completes when the merged frame is written.
public class DefaultHttp2ConnectionEncoderTest
{
    private const int STREAM_ID = 2;
    private const int PUSH_STREAM_ID = 4;

    private readonly Mock<IHttp2RemoteFlowController> remoteFlow;

    private readonly Mock<IChannelHandlerContext> ctxMock;
    private readonly IChannelHandlerContext ctx;

    private readonly Mock<IChannel> channel;

    private readonly Mock<IChannelPipeline> pipeline;

    private readonly Mock<IHttp2FrameWriter> writer;

    private readonly Mock<IHttp2FrameWriter.IConfiguration> writerConfig;

    private readonly Mock<IHttp2FrameSizePolicy> frameSizePolicy;

    private readonly Mock<IHttp2LifecycleManager> lifecycleManager;

    private readonly DefaultHttp2ConnectionEncoder encoder;
    private readonly IHttp2Connection connection;
    private readonly List<FlowControlled> payloadCaptor = new List<FlowControlled>();
    private readonly List<string> writtenData;
    private readonly List<int> writtenPadding;
    private bool streamClosed;

    public DefaultHttp2ConnectionEncoderTest()
    {
        remoteFlow = new Mock<IHttp2RemoteFlowController>();
        ctxMock = new Mock<IChannelHandlerContext>();
        ctx = ctxMock.Object;
        channel = new Mock<IChannel>();
        pipeline = new Mock<IChannelPipeline>();
        writer = new Mock<IHttp2FrameWriter>();
        writerConfig = new Mock<IHttp2FrameWriter.IConfiguration>();
        frameSizePolicy = new Mock<IHttp2FrameSizePolicy>();
        lifecycleManager = new Mock<IHttp2LifecycleManager>();

        ChannelMetadata metadata = new ChannelMetadata(false, 16);
        channel.Setup(c => c.IsActive).Returns(true);
        channel.Setup(c => c.Pipeline).Returns(pipeline.Object);
        channel.Setup(c => c.Metadata).Returns(metadata);
        IChannelConfig config = new DefaultChannelConfig(channel.Object);
        channel.Setup(c => c.Config).Returns(config);

        writer.Setup(w => w.Configuration()).Returns(writerConfig.Object);
        writerConfig.Setup(c => c.FrameSizePolicy()).Returns(frameSizePolicy.Object);
        frameSizePolicy.Setup(p => p.MaxFrameSize()).Returns(64);
        writer.Setup(w => w.WriteSettingsAsync(ctx, It.IsAny<Http2Settings>(), It.IsAny<TaskCompletionSource>()))
                .Returns<IChannelHandlerContext, Http2Settings, TaskCompletionSource>((c, s, promise) =>
                {
                    promise.SetResult();
                    return promise.Task;
                });
        writer.Setup(w => w.WriteGoAwayAsync(ctx, It.IsAny<int>(), It.IsAny<long>(), It.IsAny<ByteBuf>(),
                        It.IsAny<TaskCompletionSource>()))
                .Returns<IChannelHandlerContext, int, long, ByteBuf, TaskCompletionSource>((c, id, code, data, promise) =>
                {
                    data.Release();
                    promise.SetResult();
                    return promise.Task;
                });
        writtenData = new List<string>();
        writtenPadding = new List<int>();
        writer.Setup(w => w.WriteDataAsync(ctx, It.IsAny<int>(), It.IsAny<ByteBuf>(), It.IsAny<int>(), It.IsAny<bool>(),
                        It.IsAny<TaskCompletionSource>()))
                .Returns<IChannelHandlerContext, int, ByteBuf, int, bool, TaskCompletionSource>(
                        (c, id, data, padding, endStream, promise) =>
                {
                    // Make sure we only receive stream closure on the last frame and that void promises
                    // are used for all writes except the last one.
                    if (streamClosed)
                    {
                        Assert.Fail("Stream already closed");
                    }
                    else
                    {
                        streamClosed = endStream;
                    }
                    writtenPadding.Add(padding);
                    writtenData.Add(data.ToString(Encoding.UTF8));
                    // Release the buffer just as DefaultHttp2FrameWriter does
                    data.Release();
                    // Let the promise succeed to trigger listeners.
                    promise.SetResult();
                    return promise.Task;
                });
        writer.Setup(w => w.WriteHeadersAsync(ctx, It.IsAny<int>(), It.IsAny<IHttp2Headers>(), It.IsAny<int>(),
                        It.IsAny<short>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<bool>(),
                        It.IsAny<TaskCompletionSource>()))
                .Returns<IChannelHandlerContext, int, IHttp2Headers, int, short, bool, int, bool, TaskCompletionSource>(
                        (c, id, headers, dependency, weight, exclusive, padding, endStream, promise) =>
                {
                    if (streamClosed)
                    {
                        Assert.Fail("Stream already closed");
                    }
                    else
                    {
                        streamClosed = endStream;
                    }
                    promise.SetResult();
                    return promise.Task;
                });
        writer.Setup(w => w.WriteHeadersAsync(ctx, It.IsAny<int>(), It.IsAny<IHttp2Headers>(),
                        It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<TaskCompletionSource>()))
                .Returns<IChannelHandlerContext, int, IHttp2Headers, int, bool, TaskCompletionSource>(
                        (c, id, headers, padding, endStream, promise) =>
                {
                    if (streamClosed)
                    {
                        Assert.Fail("Stream already closed");
                    }
                    else
                    {
                        streamClosed = endStream;
                    }
                    promise.SetResult();
                    return promise.Task;
                });
        remoteFlow.Setup(f => f.AddFlowControlled(It.IsAny<IHttp2Stream>(), It.IsAny<FlowControlled>()))
                .Callback<IHttp2Stream, FlowControlled>((s, payload) => payloadCaptor.Add(payload));
        ctxMock.Setup(c => c.Alloc()).Returns(UnpooledByteBufAllocator.Default);
        ctxMock.Setup(c => c.Channel).Returns(channel.Object);
        ctxMock.Setup(c => c.Executor).Returns(ImmediateEventExecutor.INSTANCE);
        ctxMock.Setup(c => c.Flush()).Returns(() =>
        {
            Assert.Fail("forbidden");
            return null;
        });
        channel.Setup(c => c.Alloc()).Returns(PooledByteBufAllocator.Default);

        // Use a server-side connection so we can test server push.
        connection = new DefaultHttp2Connection(true);
        connection.Remote().FlowController(remoteFlow.Object);

        encoder = new DefaultHttp2ConnectionEncoder(connection, writer.Object);
        encoder.LifecycleManager(lifecycleManager.Object);
    }

    [Fact]
    public void DataWithEndOfStreamWriteShouldSignalThatFrameWasConsumedOnError()
    {
        DataWriteShouldSignalThatFrameWasConsumedOnError0(true);
    }

    [Fact]
    public void DataWriteShouldSignalThatFrameWasConsumedOnError()
    {
        DataWriteShouldSignalThatFrameWasConsumedOnError0(false);
    }

    private void DataWriteShouldSignalThatFrameWasConsumedOnError0(bool endOfStream)
    {
        CreateStream(STREAM_ID, false);
        ByteBuf data = DummyData();
        TaskCompletionSource p = NewPromise();
        encoder.WriteDataAsync(ctx, STREAM_ID, data, 0, endOfStream, p);

        FlowControlled controlled = payloadCaptor[^1];
        Assert.Equal(8, controlled.Size());
        payloadCaptor[^1].Write(ctx, 4);
        Assert.Equal(4, controlled.Size());

        Exception error = new InvalidOperationException();
        payloadCaptor[^1].Error(ctx, error);
        payloadCaptor[^1].Write(ctx, 8);
        Assert.Equal(0, controlled.Size());
        Assert.Equal("abcd", writtenData[0]);
        Assert.Equal(0, data.RefCnt);
        Assert.Same(error, Cause(p.Task));
    }

    [Fact]
    public void DataWriteShouldSucceed()
    {
        CreateStream(STREAM_ID, false);
        ByteBuf data = DummyData();
        TaskCompletionSource p = NewPromise();
        encoder.WriteDataAsync(ctx, STREAM_ID, data, 0, true, p);
        Assert.Equal(8, payloadCaptor[^1].Size());
        payloadCaptor[^1].Write(ctx, 8);
        Assert.Equal(0, payloadCaptor[^1].Size());
        Assert.Equal("abcdefgh", writtenData[0]);
        Assert.Equal(0, data.RefCnt);
        Assert.True(p.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public void DataFramesShouldMerge()
    {
        CreateStream(STREAM_ID, false);
        ByteBuf data = DummyData().Retain();

        TaskCompletionSource promise1 = NewPromise();
        encoder.WriteDataAsync(ctx, STREAM_ID, data, 0, true, promise1);
        TaskCompletionSource promise2 = NewPromise();
        encoder.WriteDataAsync(ctx, STREAM_ID, data, 0, true, promise2);

        // Now merge the two payloads.
        List<FlowControlled> capturedWrites = payloadCaptor;
        FlowControlled mergedPayload = capturedWrites[0];
        mergedPayload.Merge(ctx, capturedWrites[1]);
        Assert.Equal(16, mergedPayload.Size());
        Assert.False(promise1.Task.IsCompleted);
        Assert.False(promise2.Task.IsCompleted);

        // Write the merged payloads and verify it was written correctly.
        mergedPayload.Write(ctx, 16);
        Assert.Equal(0, mergedPayload.Size());
        Assert.Equal("abcdefghabcdefgh", writtenData[0]);
        Assert.Equal(0, data.RefCnt);
        Assert.True(promise1.Task.IsCompletedSuccessfully);
        Assert.True(promise2.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public void DataFramesDontMergeWithHeaders()
    {
        CreateStream(STREAM_ID, false);
        ByteBuf data = DummyData().Retain();
        encoder.WriteDataAsync(ctx, STREAM_ID, data, 0, false, NewPromise());
        remoteFlow.Setup(f => f.HasFlowControlled(It.IsAny<IHttp2Stream>())).Returns(true);
        encoder.WriteHeadersAsync(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, true, NewPromise());
        List<FlowControlled> capturedWrites = payloadCaptor;
        Assert.False(capturedWrites[0].Merge(ctx, capturedWrites[1]));
    }

    // CLR: dataFramesShouldMergeUseVoidPromise is CLR-inapplicable (see the type comment); its original comment, other
    // than those that the ported tests repeat, is:
    // The promises won't be set since there are no listeners.

    [Fact]
    public void EmptyFrameShouldSplitPadding()
    {
        ByteBuf data = Unpooled.Buffer(0);
        AssertSplitPaddingOnEmptyBuffer(data);
        Assert.Equal(0, data.RefCnt);
    }

    [Fact]
    public void WriteHeadersUsingVoidPromise()
    {
        Exception cause = Http2TestUtil.FAKE_EXCEPTION;
        writer.Setup(w => w.WriteHeadersAsync(ctx, STREAM_ID, It.IsAny<IHttp2Headers>(),
                                 It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<TaskCompletionSource>()))
                .Returns<IChannelHandlerContext, int, IHttp2Headers, int, bool, TaskCompletionSource>(
                        (c, id, headers, padding, endStream, promise) =>
                {
                    // CLR: assertFalse(promise.isVoid()) has no counterpart; there is no void promise.
                    promise.SetException(cause);
                    return promise.Task;
                });
        CreateStream(STREAM_ID, false);
        // END_STREAM flag, so that a listener is added to the future.
        encoder.WriteHeadersAsync(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, true, Http2TestUtil.NewVoidPromise(channel.Object));

        writer.Verify(w => w.WriteHeadersAsync(ctx, STREAM_ID, It.IsAny<IHttp2Headers>(),
                                    It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<TaskCompletionSource>()), Times.Once());
        // When using a void promise, the error should be propagated via the channel pipeline.
        pipeline.Verify(p => p.FireExceptionCaught(cause), Times.Once());
    }

    private void AssertSplitPaddingOnEmptyBuffer(ByteBuf data)
    {
        CreateStream(STREAM_ID, false);
        frameSizePolicy.Setup(p => p.MaxFrameSize()).Returns(5);
        TaskCompletionSource p = NewPromise();
        encoder.WriteDataAsync(ctx, STREAM_ID, data, 10, true, p);
        Assert.Equal(10, payloadCaptor[^1].Size());
        payloadCaptor[^1].Write(ctx, 10);
        // writer was called 2 times
        Assert.Single(writtenData);
        Assert.Equal("", writtenData[0]);
        Assert.Equal(10, writtenPadding[0]);
        Assert.Equal(0, data.RefCnt);
        Assert.True(p.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public void WriteDataFailsStreamWhenFlowControlledQueueUnderDelivers()
    {
        CreateStream(STREAM_ID, false);
        ByteBuf data = WrappedBuffer(new byte[10]);
        TaskCompletionSource promise = NewPromise();
        // Include padding so we also cover that dataSize and padding are reset on failure.
        encoder.WriteDataAsync(ctx, STREAM_ID, data, 10, false, promise);
        FlowControlled fc = payloadCaptor[^1];
        Assert.Equal(20, fc.Size());

        // Simulate a queued buffer being consumed out from under the queue
        data.SkipBytes(data.ReadableBytes);

        fc.Write(ctx, 20);

        // Expect the stream to fail rather than emitting any frames
        Assert.False(promise.Task.IsCompletedSuccessfully);
        Assert.IsAssignableFrom<Http2Exception>(Cause(promise.Task));
        Assert.Equal(Http2Error.INTERNAL_ERROR, ((Http2Exception) Cause(promise.Task)).Error());
        Assert.Empty(writtenData);
        // The frame reports as fully consumed so it is removed and its bytes return to flow control.
        Assert.Equal(0, fc.Size());
        Assert.Equal(0, data.RefCnt);
    }

    [Fact]
    public void HeadersWriteForUnknownStreamShouldCreateStream()
    {
        WriteAllFlowControlledFrames();
        int streamId = 6;
        TaskCompletionSource promise = NewPromise();
        encoder.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE, 0, false, promise);
        writer.Verify(w => w.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE, 0,
                false, promise), Times.Once());
        Assert.True(promise.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public void HeadersWriteShouldOpenStreamForPush()
    {
        WriteAllFlowControlledFrames();
        IHttp2Stream parent = CreateStream(STREAM_ID, false);
        ReservePushStream(PUSH_STREAM_ID, parent);

        TaskCompletionSource promise = NewPromise();
        encoder.WriteHeadersAsync(ctx, PUSH_STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, false, promise);
        Assert.Equal(IHttp2Stream.State.HALF_CLOSED_REMOTE, Stream(PUSH_STREAM_ID).State_());
        writer.Verify(w => w.WriteHeadersAsync(ctx, PUSH_STREAM_ID, EmptyHttp2Headers.INSTANCE,
                0, false, promise), Times.Once());
    }

    [Fact]
    public void TrailersDoNotEndStreamThrows()
    {
        WriteAllFlowControlledFrames();
        int streamId = 6;
        TaskCompletionSource promise = NewPromise();
        encoder.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE, 0, false, promise);

        TaskCompletionSource promise2 = NewPromise();
        Task future = encoder.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE, 0, false, promise2);
        Assert.True(future.IsCompleted);
        Assert.False(future.IsCompletedSuccessfully);

        writer.Verify(w => w.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE,
                0, false, promise), Times.Exactly(1));
    }

    [Fact]
    public void TrailersDoNotEndStreamWithDataThrows()
    {
        WriteAllFlowControlledFrames();
        int streamId = 6;
        TaskCompletionSource promise = NewPromise();
        encoder.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE, 0, false, promise);

        IHttp2Stream stream = connection.Stream(streamId);
        remoteFlow.Setup(f => f.HasFlowControlled(stream)).Returns(true);

        TaskCompletionSource promise2 = NewPromise();
        Task future = encoder.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE, 0, false, promise2);
        Assert.True(future.IsCompleted);
        Assert.False(future.IsCompletedSuccessfully);

        writer.Verify(w => w.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE,
                0, false, promise), Times.Exactly(1));
    }

    [Fact]
    public void TooManyHeadersNoEOSThrows()
    {
        TooManyHeadersThrows(false);
    }

    [Fact]
    public void TooManyHeadersEOSThrows()
    {
        TooManyHeadersThrows(true);
    }

    private void TooManyHeadersThrows(bool eos)
    {
        WriteAllFlowControlledFrames();
        int streamId = 6;
        TaskCompletionSource promise = NewPromise();
        encoder.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE, 0, false, promise);
        TaskCompletionSource promise2 = NewPromise();
        encoder.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE, 0, true, promise2);

        TaskCompletionSource promise3 = NewPromise();
        Task future = encoder.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE, 0, eos, promise3);
        Assert.True(future.IsCompleted);
        Assert.False(future.IsCompletedSuccessfully);

        writer.Verify(w => w.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE,
                0, false, promise), Times.Exactly(1));
        writer.Verify(w => w.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE,
                0, true, promise2), Times.Exactly(1));
    }

    [Fact]
    public void InfoHeadersAndTrailersAllowed()
    {
        InfoHeadersAndTrailers(true, 1);
    }

    [Fact]
    public void MultipleInfoHeadersAndTrailersAllowed()
    {
        InfoHeadersAndTrailers(true, 10);
    }

    [Fact]
    public void InfoHeadersAndTrailersNoEOSThrows()
    {
        InfoHeadersAndTrailers(false, 1);
    }

    [Fact]
    public void MultipleInfoHeadersAndTrailersNoEOSThrows()
    {
        InfoHeadersAndTrailers(false, 10);
    }

    private void InfoHeadersAndTrailers(bool eos, int infoHeaderCount)
    {
        WriteAllFlowControlledFrames();
        int streamId = 6;
        IHttp2Headers infoHeaders = InformationalHeaders();
        for (int i = 0; i < infoHeaderCount; ++i)
        {
            encoder.WriteHeadersAsync(ctx, streamId, infoHeaders, 0, false, NewPromise());
        }
        TaskCompletionSource promise2 = NewPromise();
        encoder.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE, 0, false, promise2);

        TaskCompletionSource promise3 = NewPromise();
        Task future = encoder.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE, 0, eos, promise3);
        Assert.True(future.IsCompleted);
        Assert.Equal(eos, future.IsCompletedSuccessfully);

        writer.Verify(w => w.WriteHeadersAsync(ctx, streamId, infoHeaders,
                0, false, It.IsAny<TaskCompletionSource>()), Times.Exactly(infoHeaderCount));
        writer.Verify(w => w.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE,
                0, false, promise2), Times.Exactly(1));
        if (eos)
        {
            writer.Verify(w => w.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE,
                    0, true, promise3), Times.Exactly(1));
        }
    }

    private static IHttp2Headers InformationalHeaders()
    {
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Status(HttpResponseStatus.CONTINUE.CodeAsText());
        return headers;
    }

    [Fact]
    public void TooManyHeadersWithDataNoEOSThrows()
    {
        TooManyHeadersWithDataThrows(false);
    }

    [Fact]
    public void TooManyHeadersWithDataEOSThrows()
    {
        TooManyHeadersWithDataThrows(true);
    }

    private void TooManyHeadersWithDataThrows(bool eos)
    {
        WriteAllFlowControlledFrames();
        int streamId = 6;
        TaskCompletionSource promise = NewPromise();
        encoder.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE, 0, false, promise);

        IHttp2Stream stream = connection.Stream(streamId);
        remoteFlow.Setup(f => f.HasFlowControlled(stream)).Returns(true);

        TaskCompletionSource promise2 = NewPromise();
        encoder.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE, 0, true, promise2);

        TaskCompletionSource promise3 = NewPromise();
        Task future = encoder.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE, 0, eos, promise3);
        Assert.True(future.IsCompleted);
        Assert.False(future.IsCompletedSuccessfully);

        writer.Verify(w => w.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE,
                0, false, promise), Times.Exactly(1));
        writer.Verify(w => w.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE,
                0, true, promise2), Times.Exactly(1));
    }

    [Fact]
    public void InfoHeadersAndTrailersWithDataAllowed()
    {
        InfoHeadersAndTrailersWithData(true, 1);
    }

    [Fact]
    public void MultipleInfoHeadersAndTrailersWithDataAllowed()
    {
        InfoHeadersAndTrailersWithData(true, 10);
    }

    [Fact]
    public void InfoHeadersAndTrailersWithDataNoEOSThrows()
    {
        InfoHeadersAndTrailersWithData(false, 1);
    }

    [Fact]
    public void MultipleInfoHeadersAndTrailersWithDataNoEOSThrows()
    {
        InfoHeadersAndTrailersWithData(false, 10);
    }

    private void InfoHeadersAndTrailersWithData(bool eos, int infoHeaderCount)
    {
        WriteAllFlowControlledFrames();
        int streamId = 6;
        IHttp2Headers infoHeaders = InformationalHeaders();
        for (int i = 0; i < infoHeaderCount; ++i)
        {
            encoder.WriteHeadersAsync(ctx, streamId, infoHeaders, 0, false, NewPromise());
        }

        IHttp2Stream stream = connection.Stream(streamId);
        remoteFlow.Setup(f => f.HasFlowControlled(stream)).Returns(true);

        TaskCompletionSource promise2 = NewPromise();
        encoder.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE, 0, false, promise2);

        TaskCompletionSource promise3 = NewPromise();
        Task future = encoder.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE, 0, eos, promise3);
        Assert.True(future.IsCompleted);
        Assert.Equal(eos, future.IsCompletedSuccessfully);

        writer.Verify(w => w.WriteHeadersAsync(ctx, streamId, infoHeaders,
                0, false, It.IsAny<TaskCompletionSource>()), Times.Exactly(infoHeaderCount));
        writer.Verify(w => w.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE,
                0, false, promise2), Times.Exactly(1));
        if (eos)
        {
            writer.Verify(w => w.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE,
                    0, true, promise3), Times.Exactly(1));
        }
    }

    [Fact]
    public void PushPromiseWriteAfterGoAwayReceivedShouldFail()
    {
        CreateStream(STREAM_ID, false);
        GoAwayReceived(0);
        Task future = encoder.WritePushPromiseAsync(ctx, STREAM_ID, PUSH_STREAM_ID, EmptyHttp2Headers.INSTANCE, 0,
                NewPromise());
        Assert.True(future.IsCompleted);
        Assert.False(future.IsCompletedSuccessfully);
    }

    [Fact]
    public void PushPromiseWriteShouldReserveStream()
    {
        CreateStream(STREAM_ID, false);
        TaskCompletionSource promise = NewPromise();
        encoder.WritePushPromiseAsync(ctx, STREAM_ID, PUSH_STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, promise);
        Assert.Equal(IHttp2Stream.State.RESERVED_LOCAL, Stream(PUSH_STREAM_ID).State_());
        writer.Verify(w => w.WritePushPromiseAsync(ctx, STREAM_ID, PUSH_STREAM_ID,
                EmptyHttp2Headers.INSTANCE, 0, promise), Times.Once());
    }

    [Fact]
    public void PriorityWriteAfterGoAwayShouldSucceed()
    {
        CreateStream(STREAM_ID, false);
        GoAwayReceived(int.MaxValue);
        TaskCompletionSource promise = NewPromise();
        encoder.WritePriorityAsync(ctx, STREAM_ID, 0, (short) 255, true, promise);
        writer.Verify(w => w.WritePriorityAsync(ctx, STREAM_ID, 0, (short) 255, true, promise), Times.Once());
    }

    [Fact]
    public void PriorityWriteShouldSetPriorityForStream()
    {
        TaskCompletionSource promise = NewPromise();
        short weight = 255;
        encoder.WritePriorityAsync(ctx, STREAM_ID, 0, weight, true, promise);

        // Verify that this did NOT create a stream object.
        IHttp2Stream stream = Stream(STREAM_ID);
        Assert.Null(stream);

        writer.Verify(w => w.WritePriorityAsync(ctx, STREAM_ID, 0, (short) 255, true, promise), Times.Once());
    }

    [Fact]
    public void PriorityWriteOnPreviouslyExistingStreamShouldSucceed()
    {
        CreateStream(STREAM_ID, false).Close();
        TaskCompletionSource promise = NewPromise();
        short weight = 255;
        encoder.WritePriorityAsync(ctx, STREAM_ID, 0, weight, true, promise);
        writer.Verify(w => w.WritePriorityAsync(ctx, STREAM_ID, 0, weight, true, promise), Times.Once());
    }

    [Fact]
    public void PriorityWriteOnPreviouslyExistingParentStreamShouldSucceed()
    {
        int parentStreamId = STREAM_ID + 2;
        CreateStream(STREAM_ID, false);
        CreateStream(parentStreamId, false).Close();

        TaskCompletionSource promise = NewPromise();
        short weight = 255;
        encoder.WritePriorityAsync(ctx, STREAM_ID, parentStreamId, weight, true, promise);
        writer.Verify(w => w.WritePriorityAsync(ctx, STREAM_ID, parentStreamId, weight, true, promise), Times.Once());
    }

    [Fact]
    public void RstStreamWriteForUnknownStreamShouldIgnore()
    {
        TaskCompletionSource promise = NewPromise();
        encoder.WriteRstStreamAsync(ctx, 5, PROTOCOL_ERROR.Code(), promise);
        writer.Verify(w => w.WriteRstStreamAsync(ctx, It.IsAny<int>(), It.IsAny<long>(), promise), Times.Never());
    }

    [Fact]
    public void RstStreamShouldCloseStream()
    {
        // Create the stream and send headers.
        WriteAllFlowControlledFrames();
        encoder.WriteHeadersAsync(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, true, NewPromise());

        // Now verify that a stream reset is performed.
        Stream(STREAM_ID);
        TaskCompletionSource promise = NewPromise();
        encoder.WriteRstStreamAsync(ctx, STREAM_ID, PROTOCOL_ERROR.Code(), promise);
        lifecycleManager.Verify(m => m.ResetStreamAsync(ctx, STREAM_ID, It.IsAny<long>(), promise), Times.Once());
    }

    [Fact]
    public void PingWriteAfterGoAwayShouldSucceed()
    {
        TaskCompletionSource promise = NewPromise();
        GoAwayReceived(0);
        encoder.WritePingAsync(ctx, false, 0L, promise);
        writer.Verify(w => w.WritePingAsync(ctx, false, 0L, promise), Times.Once());
    }

    [Fact]
    public void PingWriteShouldSucceed()
    {
        TaskCompletionSource promise = NewPromise();
        encoder.WritePingAsync(ctx, false, 0L, promise);
        writer.Verify(w => w.WritePingAsync(ctx, false, 0L, promise), Times.Once());
    }

    [Fact]
    public void SettingsWriteAfterGoAwayShouldSucceed()
    {
        GoAwayReceived(0);
        TaskCompletionSource promise = NewPromise();
        encoder.WriteSettingsAsync(ctx, new Http2Settings(), promise);
        writer.Verify(w => w.WriteSettingsAsync(ctx, It.IsAny<Http2Settings>(), promise), Times.Once());
    }

    [Fact]
    public void SettingsWriteShouldNotUpdateSettings()
    {
        Http2Settings settings = new Http2Settings();
        settings.InitialWindowSize(100);
        settings.MaxConcurrentStreams(1000);
        settings.HeaderTableSize(2000);

        TaskCompletionSource promise = NewPromise();
        encoder.WriteSettingsAsync(ctx, settings, promise);
        writer.Verify(w => w.WriteSettingsAsync(ctx, settings, promise), Times.Once());
    }

    [Fact]
    public void SettingsValidationFailureDoesNotConsumeNextSettingsAck()
    {
        Http2Settings invalidSettings = new Http2Settings().PushEnabled(true);
        TaskCompletionSource failedPromise = NewPromise();

        encoder.WriteSettingsAsync(ctx, invalidSettings, failedPromise);

        Assert.False(failedPromise.Task.IsCompletedSuccessfully);
        Assert.Equal(PROTOCOL_ERROR, ((Http2Exception) Cause(failedPromise.Task)).Error());
        writer.Verify(w => w.WriteSettingsAsync(ctx, invalidSettings, It.IsAny<TaskCompletionSource>()), Times.Never());

        Http2Settings validSettings = new Http2Settings().InitialWindowSize(100);
        encoder.WriteSettingsAsync(ctx, validSettings, NewPromise());

        Assert.Same(validSettings, encoder.PollSentSettings());
        Assert.Null(encoder.PollSentSettings());
    }

    [Fact]
    public void SynchronousSettingsWriteFailureDoesNotConsumeNextSettingsAck()
    {
        TaskCompletionSource failedPromise = NewPromise();
        writer.Setup(w => w.WriteSettingsAsync(ctx, It.IsAny<Http2Settings>(), It.IsAny<TaskCompletionSource>()))
                .Returns<IChannelHandlerContext, Http2Settings, TaskCompletionSource>((c, s, promise) =>
                {
                    if (promise == failedPromise)
                    {
                        promise.SetException(Http2TestUtil.FAKE_EXCEPTION);
                    }
                    else
                    {
                        promise.SetResult();
                    }
                    return promise.Task;
                });

        encoder.WriteSettingsAsync(ctx, new Http2Settings().InitialWindowSize(100), failedPromise);
        Http2Settings validSettings = new Http2Settings().InitialWindowSize(200);
        encoder.WriteSettingsAsync(ctx, validSettings, NewPromise());

        Assert.Same(Http2TestUtil.FAKE_EXCEPTION, Cause(failedPromise.Task));
        Assert.Same(validSettings, encoder.PollSentSettings());
        Assert.Null(encoder.PollSentSettings());
    }

    [Fact]
    public void AsynchronousSettingsWriteFailurePreservesQueueOrder()
    {
        writer.Setup(w => w.WriteSettingsAsync(ctx, It.IsAny<Http2Settings>(), It.IsAny<TaskCompletionSource>()))
                .Returns<IChannelHandlerContext, Http2Settings, TaskCompletionSource>((c, s, promise) => promise.Task);

        Http2Settings firstSettings = new Http2Settings().InitialWindowSize(100);
        Http2Settings failedSettings = new Http2Settings().InitialWindowSize(100);
        Http2Settings lastSettings = new Http2Settings().InitialWindowSize(200);
        TaskCompletionSource firstPromise = NewPromise();
        TaskCompletionSource failedPromise = NewPromise();
        TaskCompletionSource lastPromise = NewPromise();

        encoder.WriteSettingsAsync(ctx, firstSettings, firstPromise);
        encoder.WriteSettingsAsync(ctx, failedSettings, failedPromise);
        encoder.WriteSettingsAsync(ctx, lastSettings, lastPromise);
        failedPromise.SetException(Http2TestUtil.FAKE_EXCEPTION);
        firstPromise.SetResult();
        lastPromise.SetResult();

        Assert.Same(firstSettings, encoder.PollSentSettings());
        Assert.Same(lastSettings, encoder.PollSentSettings());
        Assert.Null(encoder.PollSentSettings());
    }

    [Fact]
    public void AsynchronousSettingsWriteFailureSupportsVoidPromise()
    {
        writer.Setup(w => w.WriteSettingsAsync(ctx, It.IsAny<Http2Settings>(), It.IsAny<TaskCompletionSource>()))
                .Returns<IChannelHandlerContext, Http2Settings, TaskCompletionSource>((c, s, promise) => promise.Task);

        // CLR: Java casts the returned future to the unvoided ChannelPromise; here unvoid() is the promise itself, so
        // the returned Task is the Task of the promise passed in, which the test keeps to fail it (isVoid() has no
        // counterpart).
        TaskCompletionSource voidPromise = Http2TestUtil.NewVoidPromise(channel.Object);
        Task future = encoder.WriteSettingsAsync(ctx,
                new Http2Settings().InitialWindowSize(100), voidPromise);
        Assert.Same(voidPromise.Task, future);
        Assert.False(future.IsCompleted);
        voidPromise.SetException(Http2TestUtil.FAKE_EXCEPTION);

        Http2Settings validSettings = new Http2Settings().InitialWindowSize(200);
        TaskCompletionSource validPromise = NewPromise();
        encoder.WriteSettingsAsync(ctx, validSettings, validPromise);
        validPromise.SetResult();

        pipeline.Verify(p => p.FireExceptionCaught(Http2TestUtil.FAKE_EXCEPTION), Times.Once());
        Assert.Same(validSettings, encoder.PollSentSettings());
        Assert.Null(encoder.PollSentSettings());
    }

    [Fact]
    public void DataWriteShouldCreateHalfClosedStream()
    {
        WriteAllFlowControlledFrames();

        IHttp2Stream stream = CreateStream(STREAM_ID, false);
        ByteBuf data = DummyData();
        TaskCompletionSource promise = NewPromise();
        encoder.WriteDataAsync(ctx, STREAM_ID, data.Retain(), 0, true, promise);
        Assert.True(promise.Task.IsCompletedSuccessfully);
        remoteFlow.Verify(f => f.AddFlowControlled(stream, It.IsAny<FlowControlled>()), Times.Once());
        lifecycleManager.Verify(m => m.CloseStreamLocal(stream, promise.Task), Times.Once());
        Assert.Equal(data.ToString(Encoding.UTF8), writtenData[0]);
        data.Release();
    }

    [Fact]
    public void HeadersWriteShouldHalfCloseStream()
    {
        WriteAllFlowControlledFrames();
        CreateStream(STREAM_ID, false);
        TaskCompletionSource promise = NewPromise();
        encoder.WriteHeadersAsync(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, true, promise);

        Assert.True(promise.Task.IsCompletedSuccessfully);
        lifecycleManager.Verify(m => m.CloseStreamLocal(Stream(STREAM_ID), promise.Task), Times.Once());
    }

    [Fact]
    public void HeadersWriteShouldHalfClosePushStream()
    {
        WriteAllFlowControlledFrames();
        IHttp2Stream parent = CreateStream(STREAM_ID, false);
        IHttp2Stream stream = ReservePushStream(PUSH_STREAM_ID, parent);
        TaskCompletionSource promise = NewPromise();
        encoder.WriteHeadersAsync(ctx, PUSH_STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, true, promise);
        Assert.Equal(IHttp2Stream.State.HALF_CLOSED_REMOTE, stream.State_());
        Assert.True(promise.Task.IsCompletedSuccessfully);
        lifecycleManager.Verify(m => m.CloseStreamLocal(stream, promise.Task), Times.Once());
    }

    [Fact]
    public void HeadersWriteShouldHalfCloseAfterOnErrorForPreCreatedStream()
    {
        TaskCompletionSource promise = NewPromise();
        Exception ex = new Exception();
        // Fake an encoding error, like HPACK's HeaderListSizeException
        writer.Setup(w => w.WriteHeadersAsync(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, true, promise))
            .Returns(() =>
            {
                promise.SetException(ex);
                return promise.Task;
            });
        List<string> inOrder = RecordLifecycleOrder();

        WriteAllFlowControlledFrames();
        IHttp2Stream stream = CreateStream(STREAM_ID, false);
        encoder.WriteHeadersAsync(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, true, promise);

        Assert.True(promise.Task.IsCompleted);
        Assert.False(promise.Task.IsCompletedSuccessfully);
        Assert.False(stream.IsHeadersSent());
        lifecycleManager.Verify(m => m.OnError(ctx, true, ex), Times.Once());
        lifecycleManager.Verify(m => m.CloseStreamLocal(Stream(STREAM_ID), promise.Task), Times.Once());
        Assert.Equal(new[] { "onError", "closeStreamLocal" }, inOrder);
    }

    [Fact]
    public void HeadersWriteShouldHalfCloseAfterOnErrorForImplicitlyCreatedStream()
    {
        TaskCompletionSource promise = NewPromise();
        Exception ex = new Exception();
        // Fake an encoding error, like HPACK's HeaderListSizeException
        writer.Setup(w => w.WriteHeadersAsync(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, true, promise))
            .Returns(() =>
            {
                promise.SetException(ex);
                return promise.Task;
            });
        List<string> inOrder = RecordLifecycleOrder();

        WriteAllFlowControlledFrames();
        encoder.WriteHeadersAsync(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, true, promise);

        Assert.True(promise.Task.IsCompleted);
        Assert.False(promise.Task.IsCompletedSuccessfully);
        Assert.False(Stream(STREAM_ID).IsHeadersSent());
        lifecycleManager.Verify(m => m.OnError(ctx, true, ex), Times.Once());
        lifecycleManager.Verify(m => m.CloseStreamLocal(Stream(STREAM_ID), promise.Task), Times.Once());
        Assert.Equal(new[] { "onError", "closeStreamLocal" }, inOrder);
    }

    // CLR: Mockito's InOrder: the lifecycle calls are recorded in the order they happen.
    private List<string> RecordLifecycleOrder()
    {
        List<string> calls = new List<string>();
        lifecycleManager.Setup(m => m.OnError(It.IsAny<IChannelHandlerContext>(), It.IsAny<bool>(),
                It.IsAny<Exception>())).Callback(() => calls.Add("onError"));
        lifecycleManager.Setup(m => m.CloseStreamLocal(It.IsAny<IHttp2Stream>(), It.IsAny<Task>()))
                .Callback(() => calls.Add("closeStreamLocal"));
        return calls;
    }

    [Fact]
    public void EncoderDelegatesGoAwayToLifeCycleManager()
    {
        TaskCompletionSource promise = NewPromise();
        encoder.WriteGoAwayAsync(ctx, STREAM_ID, Http2Error.INTERNAL_ERROR.Code(), null, promise);
        lifecycleManager.Verify(m => m.GoAwayAsync(ctx, STREAM_ID, Http2Error.INTERNAL_ERROR.Code(),
                (ByteBuf) null, promise), Times.Once());
        writer.VerifyNoOtherCalls();
    }

    [Fact]
    public void DataWriteToClosedStreamShouldFail()
    {
        CreateStream(STREAM_ID, false).Close();
        Mock<ByteBuf> data = new Mock<ByteBuf>(MockBehavior.Default, 0);
        TaskCompletionSource promise = NewPromise();
        encoder.WriteDataAsync(ctx, STREAM_ID, data.Object, 0, false, promise);
        Assert.True(promise.Task.IsCompleted);
        Assert.False(promise.Task.IsCompletedSuccessfully);
        Assert.IsAssignableFrom<ArgumentException>(Cause(promise.Task));
        data.Verify(d => d.Release(1), Times.Once());
    }

    [Fact]
    public void DataWriteToHalfClosedLocalStreamShouldFail()
    {
        CreateStream(STREAM_ID, true);
        Mock<ByteBuf> data = new Mock<ByteBuf>(MockBehavior.Default, 0);
        TaskCompletionSource promise = NewPromise();
        encoder.WriteDataAsync(ctx, STREAM_ID, data.Object, 0, false, promise);
        Assert.True(promise.Task.IsCompleted);
        Assert.False(promise.Task.IsCompletedSuccessfully);
        Assert.IsAssignableFrom<InvalidOperationException>(Cause(promise.Task));
        data.Verify(d => d.Release(1), Times.Once());
    }

    [Fact]
    public void CanWriteDataFrameAfterGoAwaySent()
    {
        IHttp2Stream stream = CreateStream(STREAM_ID, false);
        connection.GoAwaySent(0, 0, EmptyBuffer);
        ByteBuf data = new Mock<ByteBuf>(MockBehavior.Default, 0).Object;
        encoder.WriteDataAsync(ctx, STREAM_ID, data, 0, false, NewPromise());
        remoteFlow.Verify(f => f.AddFlowControlled(stream, It.IsAny<FlowControlled>()), Times.Once());
    }

    [Fact]
    public void CanWriteHeaderFrameAfterGoAwaySent()
    {
        WriteAllFlowControlledFrames();
        CreateStream(STREAM_ID, false);
        GoAwaySent(0);
        TaskCompletionSource promise = NewPromise();
        encoder.WriteHeadersAsync(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, false, promise);
        writer.Verify(w => w.WriteHeadersAsync(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE,
                0, false, promise), Times.Once());
    }

    [Fact]
    public void CanWriteDataFrameAfterGoAwayReceived()
    {
        IHttp2Stream stream = CreateStream(STREAM_ID, false);
        GoAwayReceived(STREAM_ID);
        ByteBuf data = new Mock<ByteBuf>(MockBehavior.Default, 0).Object;
        encoder.WriteDataAsync(ctx, STREAM_ID, data, 0, false, NewPromise());
        remoteFlow.Verify(f => f.AddFlowControlled(stream, It.IsAny<FlowControlled>()), Times.Once());
    }

    [Fact]
    public void CanWriteHeaderFrameAfterGoAwayReceived()
    {
        WriteAllFlowControlledFrames();
        GoAwayReceived(STREAM_ID);
        TaskCompletionSource promise = NewPromise();
        encoder.WriteHeadersAsync(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, false, promise);
        writer.Verify(w => w.WriteHeadersAsync(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE,
                0, false, promise), Times.Once());
    }

    [Fact]
    public void HeadersWithNoPriority()
    {
        WriteAllFlowControlledFrames();
        int streamId = 6;
        TaskCompletionSource promise = NewPromise();
        encoder.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE, 0, false, promise);
        writer.Verify(w => w.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE,
                0, false, promise), Times.Once());
    }

    [Fact]
    public void HeadersWithPriority()
    {
        WriteAllFlowControlledFrames();
        int streamId = 6;
        TaskCompletionSource promise = NewPromise();
        encoder.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE, 10, DEFAULT_PRIORITY_WEIGHT,
                true, 1, false, promise);
        writer.Verify(w => w.WriteHeadersAsync(ctx, streamId, EmptyHttp2Headers.INSTANCE, 10,
                DEFAULT_PRIORITY_WEIGHT, true, 1, false, promise), Times.Once());
    }

    private void WriteAllFlowControlledFrames()
    {
        remoteFlow.Setup(f => f.AddFlowControlled(It.IsAny<IHttp2Stream>(), It.IsAny<FlowControlled>()))
                .Callback<IHttp2Stream, FlowControlled>((s, flowControlled) =>
                {
                    payloadCaptor.Add(flowControlled);
                    flowControlled.Write(ctx, int.MaxValue);
                    flowControlled.WriteComplete();
                });
    }

    private IHttp2Stream CreateStream(int streamId, bool halfClosed)
    {
        return connection.Local().CreateStream(streamId, halfClosed);
    }

    private IHttp2Stream ReservePushStream(int pushStreamId, IHttp2Stream parent)
    {
        return connection.Local().ReservePushStream(pushStreamId, parent);
    }

    private IHttp2Stream Stream(int streamId)
    {
        return connection.Stream(streamId);
    }

    private void GoAwayReceived(int lastStreamId)
    {
        connection.GoAwayReceived(lastStreamId, 0, EmptyBuffer);
    }

    private void GoAwaySent(int lastStreamId)
    {
        connection.GoAwaySent(lastStreamId, 0, EmptyBuffer);
    }

    private static TaskCompletionSource NewPromise()
    {
        return new TaskCompletionSource();
    }

    private static ByteBuf DummyData()
    {
        // The buffer is purposely 8 bytes so it will even work for a ping frame.
        return WrappedBuffer(Encoding.UTF8.GetBytes("abcdefgh"));
    }
}
