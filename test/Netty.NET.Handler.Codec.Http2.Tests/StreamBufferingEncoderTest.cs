/*
 * Copyright 2015 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using static Netty.NET.Buffer.Unpooled;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.IHttp2PromisedRequestVerifier;
using static Netty.NET.Handler.Codec.Http2.IHttp2Stream.State;
using static Netty.NET.Handler.Codec.Http2.StreamBufferingEncoder;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Tests for {@link StreamBufferingEncoder}.
 */
// CLR: Mockito mocks are Moq loose mocks; @BeforeEach is the constructor and @AfterEach Dispose(). verify(mock, mode)
// is Verify(..., Times) (times(1) is Times.Once(), never() Times.Never()) and the Answers are Moq
// Returns(InvocationFunc) callbacks that see every argument (Moq Quickstart,
// https://github.com/devlooped/moq/wiki/Quickstart; docs/codec-http-porting.md, Shared CLR adaptations); the
// ArgumentCaptor of multipleWritesToActiveStream reads the recorded invocation (Mock.Invocations). A
// DefaultChannelPromise is a TaskCompletionSource: newPromise() returns a new one, ChannelFuture is its Task,
// promise.isDone()/isSuccess() are Task.IsCompleted/IsCompletedSuccessfully and future.cause() is Http2CodecUtil.Cause
// (docs/codec-http-porting.md, Shared CLR adaptations). A TaskCompletionSource cannot be mocked (its members are not
// virtual), so closedBufferedStreamReleasesByteBuf passes a real promise and checks that it succeeded where Java
// verifies setSuccess() on a mocked promise. Moq cannot create a ByteBuf proxy (measured: "Can not instantiate proxy of
// class: Netty.NET.Buffer.ByteBuf", MissingMethodException for the proxy constructor, also when the protected
// ByteBuf(int) constructor argument is given), so the mocked ByteBuf is a real Unpooled.Buffer() and
// verify(data).release() is the check that its reference count dropped from 1 to 0. eq(newPromise()) matches no
// promise, so that never() verification keeps its meaning with a new promise compared by reference. The C# IChannel has
// no unsafe(), so channel.unsafe() has no stub; channel.config(), metadata(), isWritable(), isActive() and
// bytesBeforeUnwritable() are the Config, Metadata, IsWritable, IsActive and BytesBeforeUnwritable properties, and
// config.getWriteBufferHighWaterMark()/ getMessageSizeEstimator() the IChannelConfig methods. @ParameterizedTest
// @ValueSource(booleans) is a [Theory] with two InlineData rows. new RuntimeException(e) is new Exception(message, e).
// EMPTY_BUFFER is Unpooled.EmptyBuffer and Integer.MAX_VALUE int.MaxValue.
public class StreamBufferingEncoderTest : IDisposable
{

    private StreamBufferingEncoder encoder;

    private IHttp2Connection connection;

    private readonly Mock<IHttp2FrameWriter> writer = new Mock<IHttp2FrameWriter>();

    private readonly Mock<IChannelHandlerContext> ctx = new Mock<IChannelHandlerContext>();

    private readonly Mock<IChannel> channel = new Mock<IChannel>();

    private readonly Mock<IChannelConfig> config = new Mock<IChannelConfig>();

    private readonly Mock<IEventExecutor> executor = new Mock<IEventExecutor>();

    /**
     * Init fields and do mocking.
     */
    public StreamBufferingEncoderTest()
    {
        Mock<IHttp2FrameWriter.IConfiguration> configuration = new Mock<IHttp2FrameWriter.IConfiguration>();
        Mock<IHttp2FrameSizePolicy> frameSizePolicy = new Mock<IHttp2FrameSizePolicy>();
        writer.Setup(w => w.Configuration()).Returns(configuration.Object);
        configuration.Setup(c => c.FrameSizePolicy()).Returns(frameSizePolicy.Object);
        frameSizePolicy.Setup(p => p.MaxFrameSize()).Returns(DEFAULT_MAX_FRAME_SIZE);
        writer.Setup(w => w.WriteDataAsync(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(), It.IsAny<ByteBuf>(),
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<TaskCompletionSource>())).Returns(SuccessAnswer());
        writer.Setup(w => w.WriteRstStreamAsync(ctx.Object, It.IsAny<int>(), It.IsAny<long>(),
                It.IsAny<TaskCompletionSource>())).Returns(SuccessAnswer());
        writer.Setup(w => w.WriteGoAwayAsync(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(), It.IsAny<long>(),
                It.IsAny<ByteBuf>(), It.IsAny<TaskCompletionSource>()))
                .Returns(SuccessAnswer());
        writer.Setup(w => w.WriteHeadersAsync(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<IHttp2Headers>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<TaskCompletionSource>()))
                .Returns(NoopAnswer());
        writer.Setup(w => w.WriteHeadersAsync(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<IHttp2Headers>(), It.IsAny<int>(), It.IsAny<short>(), It.IsAny<bool>(), It.IsAny<int>(),
                It.IsAny<bool>(), It.IsAny<TaskCompletionSource>()))
                .Returns(NoopAnswer());

        connection = new DefaultHttp2Connection(false);
        connection.Remote().FlowController(new DefaultHttp2RemoteFlowController(connection));
        connection.Local().FlowController(new DefaultHttp2LocalFlowController(connection).FrameWriter(writer.Object));

        DefaultHttp2ConnectionEncoder defaultEncoder =
                new DefaultHttp2ConnectionEncoder(connection, writer.Object);
        encoder = new StreamBufferingEncoder(defaultEncoder);
        DefaultHttp2ConnectionDecoder decoder = new DefaultHttp2ConnectionDecoder(
                connection, encoder, new Mock<IHttp2FrameReader>().Object, ALWAYS_VERIFY, false);
        Http2ConnectionHandler handler = new Http2ConnectionHandlerBuilder()
                .FrameListener(new Mock<IHttp2FrameListener>().Object)
                .Codec(decoder, encoder).Build();

        // Set LifeCycleManager on encoder and decoder
        ctx.SetupGet(c => c.Channel).Returns(channel.Object);
        ctx.Setup(c => c.Alloc()).Returns(UnpooledByteBufAllocator.Default);
        channel.Setup(c => c.Alloc()).Returns(UnpooledByteBufAllocator.Default);
        executor.Setup(e => e.InEventLoop()).Returns(true);
        ctx.SetupGet(c => c.Executor).Returns(executor.Object);
        channel.SetupGet(c => c.IsActive).Returns(false);
        channel.SetupGet(c => c.Config).Returns(config.Object);
        channel.SetupGet(c => c.IsWritable).Returns(true);
        channel.SetupGet(c => c.BytesBeforeUnwritable).Returns(long.MaxValue);
        config.Setup(c => c.GetWriteBufferHighWaterMark()).Returns(int.MaxValue);
        config.Setup(c => c.GetMessageSizeEstimator()).Returns(DefaultMessageSizeEstimator.DEFAULT);
        ChannelMetadata metadata = new ChannelMetadata(false, 16);
        channel.SetupGet(c => c.Metadata).Returns(metadata);
        handler.HandlerAdded(ctx.Object);
    }

    public void Dispose()
    {
        // Close and release any buffered frames.
        encoder.Close();
    }

    [Fact]
    public void MultipleWritesToActiveStream()
    {
        encoder.WriteSettingsAckAsync(ctx.Object, NewPromise());
        EncoderWriteHeaders(3, NewPromise(), false);
        Assert.Equal(0, encoder.NumBufferedStreams());
        ByteBuf data = Data();
        int expectedBytes = data.ReadableBytes * 3;
        encoder.WriteDataAsync(ctx.Object, 3, data, 0, false, NewPromise());
        encoder.WriteDataAsync(ctx.Object, 3, Data(), 0, false, NewPromise());
        encoder.WriteDataAsync(ctx.Object, 3, Data(), 0, false, NewPromise());
        EncoderWriteHeaders(3, NewPromise());

        WriteVerifyWriteHeaders(Times.Once(), 3, false);
        // Contiguous data writes are coalesced
        writer.Verify(w => w.WriteDataAsync(ctx.Object, 3, It.IsAny<ByteBuf>(), 0, false,
                It.IsAny<TaskCompletionSource>()), Times.Once());
        ByteBuf captured = null;

        foreach (IInvocation invocation in writer.Invocations)
        {
            if (invocation.Method.Name == nameof(IHttp2FrameWriter.WriteDataAsync))
            {
                captured = (ByteBuf) invocation.Arguments[2];
            }
        }

        Assert.Equal(expectedBytes, captured.ReadableBytes);
    }

    [Fact]
    public void EnsureCanCreateNextStreamWhenStreamCloses()
    {
        SetMaxConcurrentStreams(1);

        EncoderWriteHeaders(3, NewPromise());
        Assert.Equal(0, encoder.NumBufferedStreams());

        // This one gets buffered.
        EncoderWriteHeaders(5, NewPromise());
        Assert.Equal(1, connection.NumActiveStreams());
        Assert.Equal(1, encoder.NumBufferedStreams());

        // Now prevent us from creating another stream.
        SetMaxConcurrentStreams(0);

        // Close the previous stream.
        connection.Stream(3).Close();

        // Ensure that no streams are currently active and that only the HEADERS from the first
        // stream were written.
        WriteVerifyWriteHeaders(Times.Once(), 3);
        WriteVerifyWriteHeaders(Times.Never(), 5);
        Assert.Equal(0, connection.NumActiveStreams());
        Assert.Equal(1, encoder.NumBufferedStreams());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AlternatingWritesToActiveAndBufferedStreams(bool autoAckSettings)
    {
        SetMaxConcurrentStreams(autoAckSettings, 1);

        EncoderWriteHeaders(3, NewPromise());
        Assert.Equal(0, encoder.NumBufferedStreams());

        EncoderWriteHeaders(5, NewPromise());
        Assert.Equal(1, connection.NumActiveStreams());
        Assert.Equal(1, encoder.NumBufferedStreams());

        encoder.WriteDataAsync(ctx.Object, 3, EmptyBuffer, 0, false, NewPromise());
        WriteVerifyWriteHeaders(Times.Once(), 3);
        encoder.WriteDataAsync(ctx.Object, 5, EmptyBuffer, 0, false, NewPromise());
        TaskCompletionSource notWritten = NewPromise();
        writer.Verify(w => w.WriteDataAsync(ctx.Object, 5, It.IsAny<ByteBuf>(), 0, false, notWritten),
                Times.Never());
    }

    [Fact]
    public void BufferingNewStreamFailsAfterGoAwayReceived()
    {
        SetMaxConcurrentStreams(0);
        connection.GoAwayReceived(1, 8, EmptyBuffer);

        TaskCompletionSource promise = NewPromise();
        EncoderWriteHeaders(3, promise);
        Assert.Equal(0, encoder.NumBufferedStreams());
        Assert.True(promise.Task.IsCompleted);
        Assert.False(promise.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public void ReceivingGoAwayFailsBufferedStreams()
    {
        SetMaxConcurrentStreams(5);

        int streamId = 3;
        List<Task> futures = new List<Task>();

        for (int i = 0; i < 9; i++)
        {
            futures.Add(EncoderWriteHeaders(streamId, NewPromise()));
            streamId += 2;
        }

        Assert.Equal(5, connection.NumActiveStreams());
        Assert.Equal(4, encoder.NumBufferedStreams());

        connection.GoAwayReceived(11, 8, EmptyBuffer);

        Assert.Equal(5, connection.NumActiveStreams());
        Assert.Equal(0, encoder.NumBufferedStreams());
        int failCount = 0;

        foreach (Task f in futures)
        {
            if (Cause(f) != null)
            {
                Assert.IsAssignableFrom<Http2GoAwayException>(Cause(f));
                failCount++;
            }
        }

        Assert.Equal(4, failCount);
    }

    [Fact]
    public void ReceivingGoAwayFailsNewStreamIfMaxConcurrentStreamsReached()
    {
        SetMaxConcurrentStreams(1);
        EncoderWriteHeaders(3, NewPromise());
        connection.GoAwayReceived(11, 8, EmptyBuffer);
        Task f = EncoderWriteHeaders(5, NewPromise());

        Assert.IsAssignableFrom<Http2GoAwayException>(Cause(f));
        Assert.Equal(0, encoder.NumBufferedStreams());
    }

    [Fact]
    public void SendingGoAwayShouldNotFailStreams()
    {
        SetMaxConcurrentStreams(1);

        writer.Setup(w => w.WriteHeadersAsync(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
              It.IsAny<IHttp2Headers>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<TaskCompletionSource>()))
              .Returns(SuccessAnswer());
        writer.Setup(w => w.WriteHeadersAsync(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
              It.IsAny<IHttp2Headers>(), It.IsAny<int>(), It.IsAny<short>(), It.IsAny<bool>(), It.IsAny<int>(),
              It.IsAny<bool>(), It.IsAny<TaskCompletionSource>())).Returns(SuccessAnswer());

        Task f1 = EncoderWriteHeaders(3, NewPromise());
        Assert.Equal(0, encoder.NumBufferedStreams());
        Task f2 = EncoderWriteHeaders(5, NewPromise());
        Assert.Equal(1, encoder.NumBufferedStreams());
        Task f3 = EncoderWriteHeaders(7, NewPromise());
        Assert.Equal(2, encoder.NumBufferedStreams());

        ByteBuf empty = Unpooled.Buffer(0);
        encoder.WriteGoAwayAsync(ctx.Object, 3, CANCEL.Code(), empty, NewPromise());

        Assert.Equal(1, connection.NumActiveStreams());
        Assert.Equal(2, encoder.NumBufferedStreams());
        Assert.False(f1.IsCompleted);
        Assert.False(f2.IsCompleted);
        Assert.False(f3.IsCompleted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EndStreamDoesNotFailBufferedStream(bool autoAckSettings)
    {
        SetMaxConcurrentStreams(autoAckSettings, 0);

        EncoderWriteHeaders(3, NewPromise());
        Assert.Equal(1, encoder.NumBufferedStreams());

        encoder.WriteDataAsync(ctx.Object, 3, EmptyBuffer, 0, true, NewPromise());

        Assert.Equal(0, connection.NumActiveStreams());
        Assert.Equal(1, encoder.NumBufferedStreams());

        // Simulate that we received a SETTINGS frame which
        // increased MAX_CONCURRENT_STREAMS to 1.
        SetMaxConcurrentStreams(autoAckSettings, 1);

        Assert.Equal(1, connection.NumActiveStreams());
        Assert.Equal(0, encoder.NumBufferedStreams());
        Assert.Same(HALF_CLOSED_LOCAL, connection.Stream(3).State_());
    }

    [Fact]
    public void RstStreamClosesBufferedStream()
    {
        SetMaxConcurrentStreams(0);

        EncoderWriteHeaders(3, NewPromise());
        Assert.Equal(1, encoder.NumBufferedStreams());

        TaskCompletionSource rstStreamPromise = NewPromise();
        encoder.WriteRstStreamAsync(ctx.Object, 3, CANCEL.Code(), rstStreamPromise);
        Assert.True(rstStreamPromise.Task.IsCompletedSuccessfully);
        Assert.Equal(0, encoder.NumBufferedStreams());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BufferUntilActiveStreamsAreReset(bool autoAckSettings)
    {
        SetMaxConcurrentStreams(autoAckSettings, 1);

        EncoderWriteHeaders(3, NewPromise());
        Assert.Equal(0, encoder.NumBufferedStreams());
        EncoderWriteHeaders(5, NewPromise());
        Assert.Equal(1, encoder.NumBufferedStreams());
        EncoderWriteHeaders(7, NewPromise());
        Assert.Equal(2, encoder.NumBufferedStreams());

        WriteVerifyWriteHeaders(Times.Once(), 3);
        WriteVerifyWriteHeaders(Times.Never(), 5);
        WriteVerifyWriteHeaders(Times.Never(), 7);

        encoder.WriteRstStreamAsync(ctx.Object, 3, CANCEL.Code(), NewPromise());
        connection.Remote().FlowController().WritePendingBytes();
        WriteVerifyWriteHeaders(Times.Once(), 5);
        WriteVerifyWriteHeaders(Times.Never(), 7);
        Assert.Equal(1, connection.NumActiveStreams());
        Assert.Equal(1, encoder.NumBufferedStreams());

        encoder.WriteRstStreamAsync(ctx.Object, 5, CANCEL.Code(), NewPromise());
        connection.Remote().FlowController().WritePendingBytes();
        WriteVerifyWriteHeaders(Times.Once(), 7);
        Assert.Equal(1, connection.NumActiveStreams());
        Assert.Equal(0, encoder.NumBufferedStreams());

        encoder.WriteRstStreamAsync(ctx.Object, 7, CANCEL.Code(), NewPromise());
        Assert.Equal(0, connection.NumActiveStreams());
        Assert.Equal(0, encoder.NumBufferedStreams());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BufferUntilMaxStreamsIncreased(bool autoAckSettings)
    {
        SetMaxConcurrentStreams(autoAckSettings, 2);

        EncoderWriteHeaders(3, NewPromise());
        EncoderWriteHeaders(5, NewPromise());
        EncoderWriteHeaders(7, NewPromise());
        EncoderWriteHeaders(9, NewPromise());
        Assert.Equal(2, encoder.NumBufferedStreams());

        WriteVerifyWriteHeaders(Times.Once(), 3);
        WriteVerifyWriteHeaders(Times.Once(), 5);
        WriteVerifyWriteHeaders(Times.Never(), 7);
        WriteVerifyWriteHeaders(Times.Never(), 9);

        // Simulate that we received a SETTINGS frame which
        // increased MAX_CONCURRENT_STREAMS to 5.
        SetMaxConcurrentStreams(autoAckSettings, 5);

        Assert.Equal(0, encoder.NumBufferedStreams());
        WriteVerifyWriteHeaders(Times.Once(), 7);
        WriteVerifyWriteHeaders(Times.Once(), 9);

        EncoderWriteHeaders(11, NewPromise());

        WriteVerifyWriteHeaders(Times.Once(), 11);

        Assert.Equal(5, connection.Local().NumActiveStreams());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BufferUntilSettingsReceived(bool autoAckSettings)
    {
        int initialLimit = SMALLEST_MAX_CONCURRENT_STREAMS;
        int numStreams = initialLimit * 2;

        for (int ix = 0, nextStreamId = 3; ix < numStreams; ++ix, nextStreamId += 2)
        {
            EncoderWriteHeaders(nextStreamId, NewPromise());

            if (ix < initialLimit)
            {
                WriteVerifyWriteHeaders(Times.Once(), nextStreamId);
            }
            else
            {
                WriteVerifyWriteHeaders(Times.Never(), nextStreamId);
            }
        }

        Assert.Equal(numStreams / 2, encoder.NumBufferedStreams());

        // Simulate that we received a SETTINGS frame.
        SetMaxConcurrentStreams(autoAckSettings, initialLimit * 2);

        Assert.Equal(0, encoder.NumBufferedStreams());
        Assert.Equal(numStreams, connection.Local().NumActiveStreams());
    }

    [Fact]
    public void BufferUntilSettingsReceivedWithNoMaxConcurrentStreamValue()
    {
        int initialLimit = SMALLEST_MAX_CONCURRENT_STREAMS;
        int numStreams = initialLimit * 2;

        for (int ix = 0, nextStreamId = 3; ix < numStreams; ++ix, nextStreamId += 2)
        {
            EncoderWriteHeaders(nextStreamId, NewPromise());

            if (ix < initialLimit)
            {
                WriteVerifyWriteHeaders(Times.Once(), nextStreamId);
            }
            else
            {
                WriteVerifyWriteHeaders(Times.Never(), nextStreamId);
            }
        }

        Assert.Equal(numStreams / 2, encoder.NumBufferedStreams());

        // Simulate that we received an empty SETTINGS frame.
        encoder.RemoteSettings(new Http2Settings());

        Assert.Equal(0, encoder.NumBufferedStreams());
        Assert.Equal(numStreams, connection.Local().NumActiveStreams());
    }

    [Fact]
    public void ExhaustedStreamsDoNotBuffer()
    {
        // Write the highest possible stream ID for the client.
        // This will cause the next stream ID to be negative.
        EncoderWriteHeaders(int.MaxValue, NewPromise());

        // Disallow any further streams.
        SetMaxConcurrentStreams(0);

        // Simulate numeric overflow for the next stream ID.
        Task f = EncoderWriteHeaders(-1, NewPromise());

        // Verify that the write fails.
        Assert.NotNull(Cause(f));
    }

    [Fact]
    public void ClosedBufferedStreamReleasesByteBuf()
    {
        SetMaxConcurrentStreams(0);
        ByteBuf data = Unpooled.Buffer();
        Task f1 = EncoderWriteHeaders(3, NewPromise());
        Assert.Equal(1, encoder.NumBufferedStreams());
        Task f2 = encoder.WriteDataAsync(ctx.Object, 3, data, 0, false, NewPromise());

        TaskCompletionSource rstPromise = NewPromise();
        encoder.WriteRstStreamAsync(ctx.Object, 3, CANCEL.Code(), rstPromise);

        Assert.Equal(0, encoder.NumBufferedStreams());
        Assert.True(rstPromise.Task.IsCompletedSuccessfully);
        Assert.True(f1.IsCompletedSuccessfully);
        Assert.True(f2.IsCompletedSuccessfully);
        Assert.Equal(0, data.RefCnt);
    }

    [Fact]
    public void CloseShouldCancelAllBufferedStreams()
    {
        encoder.WriteSettingsAckAsync(ctx.Object, NewPromise());
        connection.Local().MaxActiveStreams(0);

        Task f1 = EncoderWriteHeaders(3, NewPromise());
        Task f2 = EncoderWriteHeaders(5, NewPromise());
        Task f3 = EncoderWriteHeaders(7, NewPromise());

        encoder.Close();
        Assert.NotNull(Cause(f1));
        Assert.NotNull(Cause(f2));
        Assert.NotNull(Cause(f3));
    }

    [Fact]
    public void HeadersAfterCloseShouldImmediatelyFail()
    {
        encoder.WriteSettingsAckAsync(ctx.Object, NewPromise());
        encoder.Close();

        Task f = EncoderWriteHeaders(3, NewPromise());
        Assert.NotNull(Cause(f));
    }

    [Fact]
    public void TestExhaustedStreamId()
    {
        TestStreamId(int.MaxValue - 2);
        TestStreamId(connection.Local().IncrementAndGetNextStreamId());
    }

    private void TestStreamId(int nextStreamId)
    {
        connection.Local().CreateStream(nextStreamId, false);
        Task channelFuture = encoder.WriteDataAsync(ctx.Object, nextStreamId, EmptyBuffer, 0, false, NewPromise());
        Assert.Null(Cause(channelFuture));
    }

    private void SetMaxConcurrentStreams(int newValue)
    {
        SetMaxConcurrentStreams(true, newValue);
    }

    private void SetMaxConcurrentStreams(bool autoAckSettings, int newValue)
    {
        try
        {
            Http2Settings settings = new Http2Settings().MaxConcurrentStreams(newValue);

            // Mimic behavior of DefaultHttp2ConnectionDecoder.onSettingsRead:
            if (autoAckSettings)
            {
                encoder.WriteSettingsAckAsync(ctx.Object, NewPromise());
                encoder.RemoteSettings(settings);
            }
            else
            {
                encoder.ConsumeReceivedSettings(settings);
                encoder.WriteSettingsAckAsync(ctx.Object, NewPromise());
            }

            // Flush the remote flow controller to write data
            encoder.FlowController().WritePendingBytes();
        }
        catch (Http2Exception e)
        {
            throw new Exception(e.Message, e);
        }
    }

    private Task EncoderWriteHeaders(int streamId, TaskCompletionSource promise)
    {
        return EncoderWriteHeaders(streamId, promise, true);
    }

    private Task EncoderWriteHeaders(int streamId, TaskCompletionSource promise, bool hasPriority)
    {
        if (hasPriority)
        {
            encoder.WriteHeadersAsync(ctx.Object, streamId, new DefaultHttp2Headers(), 0, DEFAULT_PRIORITY_WEIGHT,
                                 false, 0, false, promise);
        }
        else
        {
            encoder.WriteHeadersAsync(ctx.Object, streamId, new DefaultHttp2Headers(), 0, false, promise);
        }

        try
        {
            encoder.FlowController().WritePendingBytes();
            return promise.Task;
        }
        catch (Http2Exception e)
        {
            throw new Exception(e.Message, e);
        }
    }

    private void WriteVerifyWriteHeaders(Times mode, int streamId)
    {
        WriteVerifyWriteHeaders(mode, streamId, true);
    }

    private void WriteVerifyWriteHeaders(Times mode, int streamId, bool hasPriority)
    {
        if (hasPriority)
        {
            writer.Verify(w => w.WriteHeadersAsync(ctx.Object, streamId, It.IsAny<IHttp2Headers>(), 0,
                                              DEFAULT_PRIORITY_WEIGHT, false, 0,
                                              false, It.IsAny<TaskCompletionSource>()), mode);
        }
        else
        {
            writer.Verify(w => w.WriteHeadersAsync(ctx.Object, streamId, It.IsAny<IHttp2Headers>(), 0,
                                              false, It.IsAny<TaskCompletionSource>()), mode);
        }
    }

    private InvocationFunc SuccessAnswer()
    {
        return new InvocationFunc(invocation =>
        {
            foreach (object a in invocation.Arguments)
            {
                ReferenceCountUtil.SafeRelease(a);
            }

            TaskCompletionSource future = NewPromise();
            future.SetResult();
            return future.Task;
        });
    }

    private InvocationFunc NoopAnswer()
    {
        return new InvocationFunc(invocation =>
        {
            foreach (object a in invocation.Arguments)
            {
                if (a is TaskCompletionSource)
                {
                    return ((TaskCompletionSource) a).Task;
                }
            }

            return NewPromise().Task;
        });
    }

    private TaskCompletionSource NewPromise()
    {
        return new TaskCompletionSource();
    }

    private static ByteBuf Data()
    {
        ByteBuf buf = Unpooled.Buffer(10);

        for (int i = 0; i < buf.WritableBytes; i++)
        {
            buf.WriteByte(i);
        }

        return buf;
    }
}
