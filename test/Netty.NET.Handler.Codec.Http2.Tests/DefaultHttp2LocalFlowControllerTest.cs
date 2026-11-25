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
using System.Threading.Tasks;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http2.DefaultHttp2LocalFlowController;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using State = Netty.NET.Handler.Codec.Http2.IHttp2Stream.State;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Tests for {@link DefaultHttp2LocalFlowController}.
 */
// CLR: Mockito's @Mock fields are Moq Mock<T> fields created in the constructor (@BeforeEach; xUnit creates a test
// class instance per test, https://xunit.net/docs/shared-context); reset(ctx) is Mock.Reset() followed by the same
// setups, reset(frameWriter) clears the recorded invocations (frameWriter has no setups), verify(...) is
// Verify(..., Times.Once()), never() is Times.Never() and verifyNoMoreInteractions is VerifyNoOtherCalls (Moq
// Quickstart, https://github.com/devlooped/moq/wiki/Quickstart; docs/codec-http-porting.md, Shared CLR adaptations).
// The controller writes WINDOW_UPDATE with a promise it creates itself (Http2CodecUtil.NewPromise(), as ChannelPromise
// is TaskCompletionSource and the context has no newPromise()), so eq(promise) of the mocked ctx.newPromise() is
// It.IsAny<TaskCompletionSource>(). ctx.flush() returning the forbidden-call Answer is a Flush() setup whose callback
// calls Assert.Fail; as in Java, the listener notification of DefaultHttp2Connection catches and logs the failure.
// assertThrows(T.class, ...) is Assert.ThrowsAny<T> (docs/codec-http-porting.md, Core test notes) and the anonymous
// Executables are lambdas; IllegalArgumentException is ArgumentException.
public class DefaultHttp2LocalFlowControllerTest
{
    private const int STREAM_ID = 1;

    private DefaultHttp2LocalFlowController controller;

    private readonly Mock<IHttp2FrameWriter> frameWriter = new Mock<IHttp2FrameWriter>();

    private readonly Mock<IChannelHandlerContext> ctx = new Mock<IChannelHandlerContext>();

    private readonly Mock<IEventExecutor> executor = new Mock<IEventExecutor>();

    private DefaultHttp2Connection connection;

    public DefaultHttp2LocalFlowControllerTest()
    {
        SetupChannelHandlerContext(false);
        executor.Setup(e => e.InEventLoop()).Returns(true);

        InitController(false);
    }

    private void SetupChannelHandlerContext(bool allowFlush)
    {
        ctx.Reset();
        if (allowFlush)
        {
            ctx.Setup(c => c.Flush()).Returns(() => ctx.Object);
        }
        else
        {
            ctx.Setup(c => c.Flush()).Returns(() =>
            {
                Assert.Fail("forbidden");
                return null;
            });
        }
        ctx.Setup(c => c.Executor).Returns(executor.Object);
    }

    [Fact]
    public void DataFrameShouldBeAccepted()
    {
        ReceiveFlowControlledFrame(STREAM_ID, 10, 0, false);
        VerifyWindowUpdateNotSent();
    }

    [Fact]
    public void WindowUpdateShouldSendOnceBytesReturned()
    {
        int dataSize = (int) (DEFAULT_WINDOW_SIZE * DEFAULT_WINDOW_UPDATE_RATIO) + 1;
        ReceiveFlowControlledFrame(STREAM_ID, dataSize, 0, false);

        // Return only a few bytes and verify that the WINDOW_UPDATE hasn't been sent.
        Assert.False(ConsumeBytes(STREAM_ID, 10));
        VerifyWindowUpdateNotSent(STREAM_ID);
        VerifyWindowUpdateNotSent(CONNECTION_STREAM_ID);

        // Return the rest and verify the WINDOW_UPDATE is sent.
        Assert.True(ConsumeBytes(STREAM_ID, dataSize - 10));
        VerifyWindowUpdateSent(STREAM_ID, dataSize);
        VerifyWindowUpdateSent(CONNECTION_STREAM_ID, dataSize);
        frameWriter.VerifyNoOtherCalls();
    }

    [Fact]
    public void ConnectionWindowShouldAutoRefillWhenDataReceived()
    {
        // Reconfigure controller to auto-refill the connection window.
        InitController(true);

        int dataSize = (int) (DEFAULT_WINDOW_SIZE * DEFAULT_WINDOW_UPDATE_RATIO) + 1;
        ReceiveFlowControlledFrame(STREAM_ID, dataSize, 0, false);
        // Verify that we immediately refill the connection window.
        VerifyWindowUpdateSent(CONNECTION_STREAM_ID, dataSize);

        // Return only a few bytes and verify that the WINDOW_UPDATE hasn't been sent for the stream.
        Assert.False(ConsumeBytes(STREAM_ID, 10));
        VerifyWindowUpdateNotSent(STREAM_ID);

        // Return the rest and verify the WINDOW_UPDATE is sent for the stream.
        Assert.True(ConsumeBytes(STREAM_ID, dataSize - 10));
        VerifyWindowUpdateSent(STREAM_ID, dataSize);
        frameWriter.VerifyNoOtherCalls();
    }

    [Fact]
    public void ConnectionFlowControlExceededShouldThrow()
    {
        // Window exceeded because of the padding.
        Assert.ThrowsAny<Http2Exception>(() => ReceiveFlowControlledFrame(STREAM_ID, DEFAULT_WINDOW_SIZE, 1, true));
    }

    [Fact]
    public void WindowUpdateShouldNotBeSentAfterEndOfStream()
    {
        int dataSize = (int) (DEFAULT_WINDOW_SIZE * DEFAULT_WINDOW_UPDATE_RATIO) + 1;

        // Set end-of-stream on the frame, so no window update will be sent for the stream.
        ReceiveFlowControlledFrame(STREAM_ID, dataSize, 0, true);
        VerifyWindowUpdateNotSent(CONNECTION_STREAM_ID);
        VerifyWindowUpdateNotSent(STREAM_ID);

        Assert.True(ConsumeBytes(STREAM_ID, dataSize));
        VerifyWindowUpdateSent(CONNECTION_STREAM_ID, dataSize);
        VerifyWindowUpdateNotSent(STREAM_ID);
    }

    [Fact]
    public void WindowUpdateShouldNotBeSentAfterStreamIsClosedForUnconsumedBytes()
    {
        int dataSize = (int) (DEFAULT_WINDOW_SIZE * DEFAULT_WINDOW_UPDATE_RATIO) + 1;

        // Don't set end-of-stream on the frame as we want to verify that we not return the unconsumed bytes in this
        // case once the stream was closed,
        ReceiveFlowControlledFrame(STREAM_ID, dataSize, 0, false);
        VerifyWindowUpdateNotSent(CONNECTION_STREAM_ID);
        VerifyWindowUpdateNotSent(STREAM_ID);

        // Close the stream
        IHttp2Stream stream = connection.Stream(STREAM_ID);
        stream.Close();
        Assert.Equal(State.CLOSED, stream.State_());
        Assert.Null(connection.Stream(STREAM_ID));

        // The window update for the connection should made it through but not the update for the already closed
        // stream
        VerifyWindowUpdateSent(CONNECTION_STREAM_ID, dataSize);
        VerifyWindowUpdateNotSent(STREAM_ID);
    }

    [Fact]
    public void WindowUpdateShouldBeWrittenWhenStreamIsClosedAndFlushed()
    {
        int dataSize = (int) (DEFAULT_WINDOW_SIZE * DEFAULT_WINDOW_UPDATE_RATIO) + 1;

        SetupChannelHandlerContext(true);

        ReceiveFlowControlledFrame(STREAM_ID, dataSize, 0, false);
        VerifyWindowUpdateNotSent(CONNECTION_STREAM_ID);
        VerifyWindowUpdateNotSent(STREAM_ID);

        connection.Stream(STREAM_ID).Close();

        VerifyWindowUpdateSent(CONNECTION_STREAM_ID, dataSize);

        // Verify we saw one flush.
        ctx.Verify(c => c.Flush(), Times.Once());
    }

    [Fact]
    public void HalfWindowRemainingShouldUpdateAllWindows()
    {
        int dataSize = (int) (DEFAULT_WINDOW_SIZE * DEFAULT_WINDOW_UPDATE_RATIO) + 1;
        int initialWindowSize = DEFAULT_WINDOW_SIZE;
        int windowDelta = GetWindowDelta(initialWindowSize, initialWindowSize, dataSize);

        // Don't set end-of-stream so we'll get a window update for the stream as well.
        ReceiveFlowControlledFrame(STREAM_ID, dataSize, 0, false);
        Assert.True(ConsumeBytes(STREAM_ID, dataSize));
        VerifyWindowUpdateSent(CONNECTION_STREAM_ID, windowDelta);
        VerifyWindowUpdateSent(STREAM_ID, windowDelta);
    }

    [Fact]
    public void InitialWindowUpdateShouldAllowMoreFrames()
    {
        // Send a frame that takes up the entire window.
        int initialWindowSize = DEFAULT_WINDOW_SIZE;
        ReceiveFlowControlledFrame(STREAM_ID, initialWindowSize, 0, false);
        Assert.Equal(0, Window(STREAM_ID));
        Assert.Equal(0, Window(CONNECTION_STREAM_ID));
        ConsumeBytes(STREAM_ID, initialWindowSize);
        Assert.Equal(initialWindowSize, Window(STREAM_ID));
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(CONNECTION_STREAM_ID));

        // Update the initial window size to allow another frame.
        int newInitialWindowSize = 2 * initialWindowSize;
        controller.InitialWindowSize(newInitialWindowSize);
        Assert.Equal(newInitialWindowSize, Window(STREAM_ID));
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(CONNECTION_STREAM_ID));

        // Clear any previous calls to the writer.
        frameWriter.Invocations.Clear();

        // Send the next frame and verify that the expected window updates were sent.
        ReceiveFlowControlledFrame(STREAM_ID, initialWindowSize, 0, false);
        Assert.True(ConsumeBytes(STREAM_ID, initialWindowSize));
        int delta = newInitialWindowSize - initialWindowSize;
        VerifyWindowUpdateSent(STREAM_ID, delta);
        VerifyWindowUpdateSent(CONNECTION_STREAM_ID, delta);
    }

    [Fact]
    public void ConnectionWindowShouldAdjustWithMultipleStreams()
    {
        int newStreamId = 3;
        connection.Local().CreateStream(newStreamId, false);

        try
        {
            Assert.Equal(DEFAULT_WINDOW_SIZE, Window(STREAM_ID));
            Assert.Equal(DEFAULT_WINDOW_SIZE, Window(CONNECTION_STREAM_ID));

            // Test that both stream and connection window are updated (or not updated) together
            int data1 = (int) (DEFAULT_WINDOW_SIZE * DEFAULT_WINDOW_UPDATE_RATIO) + 1;
            ReceiveFlowControlledFrame(STREAM_ID, data1, 0, false);
            VerifyWindowUpdateNotSent(STREAM_ID);
            VerifyWindowUpdateNotSent(CONNECTION_STREAM_ID);
            Assert.Equal(DEFAULT_WINDOW_SIZE - data1, Window(STREAM_ID));
            Assert.Equal(DEFAULT_WINDOW_SIZE - data1, Window(CONNECTION_STREAM_ID));
            Assert.True(ConsumeBytes(STREAM_ID, data1));
            VerifyWindowUpdateSent(STREAM_ID, data1);
            VerifyWindowUpdateSent(CONNECTION_STREAM_ID, data1);

            frameWriter.Invocations.Clear();

            // Create a scenario where data is depleted from multiple streams, but not enough data
            // to generate a window update on those streams. The amount will be enough to generate
            // a window update for the connection stream.
            --data1;
            int data2 = data1 >> 1;
            ReceiveFlowControlledFrame(STREAM_ID, data1, 0, false);
            ReceiveFlowControlledFrame(newStreamId, data1, 0, false);
            VerifyWindowUpdateNotSent(STREAM_ID);
            VerifyWindowUpdateNotSent(newStreamId);
            VerifyWindowUpdateNotSent(CONNECTION_STREAM_ID);
            Assert.Equal(DEFAULT_WINDOW_SIZE - data1, Window(STREAM_ID));
            Assert.Equal(DEFAULT_WINDOW_SIZE - data1, Window(newStreamId));
            Assert.Equal(DEFAULT_WINDOW_SIZE - (data1 << 1), Window(CONNECTION_STREAM_ID));
            Assert.False(ConsumeBytes(STREAM_ID, data1));
            Assert.True(ConsumeBytes(newStreamId, data2));
            VerifyWindowUpdateNotSent(STREAM_ID);
            VerifyWindowUpdateNotSent(newStreamId);
            VerifyWindowUpdateSent(CONNECTION_STREAM_ID, data1 + data2);
            Assert.Equal(DEFAULT_WINDOW_SIZE - data1, Window(STREAM_ID));
            Assert.Equal(DEFAULT_WINDOW_SIZE - data1, Window(newStreamId));
            Assert.Equal(DEFAULT_WINDOW_SIZE - (data1 - data2), Window(CONNECTION_STREAM_ID));
        }
        finally
        {
            connection.Stream(newStreamId).Close();
        }
    }

    [Fact]
    public void CloseShouldConsumeBytes()
    {
        ReceiveFlowControlledFrame(STREAM_ID, 10, 0, false);
        Assert.Equal(10, controller.UnconsumedBytes(connection.ConnectionStream()));
        Stream(STREAM_ID).Close();
        Assert.Equal(0, controller.UnconsumedBytes(connection.ConnectionStream()));
    }

    [Fact]
    public void CloseShouldNotConsumeConnectionWindowWhenAutoRefilled()
    {
        // Reconfigure controller to auto-refill the connection window.
        InitController(true);

        ReceiveFlowControlledFrame(STREAM_ID, 10, 0, false);
        Assert.Equal(0, controller.UnconsumedBytes(connection.ConnectionStream()));
        Stream(STREAM_ID).Close();
        Assert.Equal(0, controller.UnconsumedBytes(connection.ConnectionStream()));
    }

    [Fact]
    public void DataReceivedForClosedStreamShouldImmediatelyConsumeBytes()
    {
        IHttp2Stream stream = Stream(STREAM_ID);
        stream.Close();
        ReceiveFlowControlledFrame(stream, 10, 0, false);
        Assert.Equal(0, controller.UnconsumedBytes(connection.ConnectionStream()));
    }

    [Fact]
    public void DataReceivedForNullStreamShouldImmediatelyConsumeBytes()
    {
        ReceiveFlowControlledFrame(null, 10, 0, false);
        Assert.Equal(0, controller.UnconsumedBytes(connection.ConnectionStream()));
    }

    [Fact]
    public void ConsumeBytesForNullStreamShouldIgnore()
    {
        controller.ConsumeBytes(null, 10);
        Assert.Equal(0, controller.UnconsumedBytes(connection.ConnectionStream()));
    }

    [Fact]
    public void GlobalRatioShouldImpactStreams()
    {
        float ratio = 0.6f;
        controller.WindowUpdateRatio(ratio);
        TestRatio(ratio, DEFAULT_WINDOW_SIZE << 1, 3, false);
    }

    [Fact]
    public void StreamlRatioShouldImpactStreams()
    {
        float ratio = 0.6f;
        TestRatio(ratio, DEFAULT_WINDOW_SIZE << 1, 3, true);
    }

    [Fact]
    public void ConsumeBytesForZeroNumBytesShouldIgnore()
    {
        Assert.False(controller.ConsumeBytes(connection.Stream(STREAM_ID), 0));
    }

    [Fact]
    public void ConsumeBytesForNegativeNumBytesShouldFail()
    {
        Assert.ThrowsAny<ArgumentException>(() => controller.ConsumeBytes(connection.Stream(STREAM_ID), -1));
    }

    private void TestRatio(float ratio, int newDefaultWindowSize, int newStreamId, bool setStreamRatio)
    {
        int delta = newDefaultWindowSize - DEFAULT_WINDOW_SIZE;
        controller.IncrementWindowSize(Stream(0), delta);
        IHttp2Stream stream = connection.Local().CreateStream(newStreamId, false);
        if (setStreamRatio)
        {
            controller.WindowUpdateRatio(stream, ratio);
        }
        controller.IncrementWindowSize(stream, delta);
        frameWriter.Invocations.Clear();
        try
        {
            int data1 = (int) (newDefaultWindowSize * ratio) + 1;
            int data2 = (int) (DEFAULT_WINDOW_SIZE * DEFAULT_WINDOW_UPDATE_RATIO) >> 1;
            ReceiveFlowControlledFrame(STREAM_ID, data2, 0, false);
            ReceiveFlowControlledFrame(newStreamId, data1, 0, false);
            VerifyWindowUpdateNotSent(STREAM_ID);
            VerifyWindowUpdateNotSent(newStreamId);
            VerifyWindowUpdateNotSent(CONNECTION_STREAM_ID);
            Assert.Equal(DEFAULT_WINDOW_SIZE - data2, Window(STREAM_ID));
            Assert.Equal(newDefaultWindowSize - data1, Window(newStreamId));
            Assert.Equal(newDefaultWindowSize - data2 - data1, Window(CONNECTION_STREAM_ID));
            Assert.False(ConsumeBytes(STREAM_ID, data2));
            Assert.True(ConsumeBytes(newStreamId, data1));
            VerifyWindowUpdateNotSent(STREAM_ID);
            VerifyWindowUpdateSent(newStreamId, data1);
            VerifyWindowUpdateSent(CONNECTION_STREAM_ID, data1 + data2);
            Assert.Equal(DEFAULT_WINDOW_SIZE - data2, Window(STREAM_ID));
            Assert.Equal(newDefaultWindowSize, Window(newStreamId));
            Assert.Equal(newDefaultWindowSize, Window(CONNECTION_STREAM_ID));
        }
        finally
        {
            connection.Stream(newStreamId).Close();
        }
    }

    private static int GetWindowDelta(int initialSize, int windowSize, int dataSize)
    {
        int newWindowSize = windowSize - dataSize;
        return initialSize - newWindowSize;
    }

    private void ReceiveFlowControlledFrame(int streamId, int dataSize, int padding,
                                            bool endOfStream)
    {
        ReceiveFlowControlledFrame(Stream(streamId), dataSize, padding, endOfStream);
    }

    private void ReceiveFlowControlledFrame(IHttp2Stream stream, int dataSize, int padding,
                                            bool endOfStream)
    {
        ByteBuf buf = DummyData(dataSize);
        try
        {
            controller.ReceiveFlowControlledFrame(stream, buf, padding, endOfStream);
        }
        finally
        {
            buf.Release();
        }
    }

    private static ByteBuf DummyData(int size)
    {
        ByteBuf buffer = Unpooled.Buffer(size);
        buffer.WriterIndex = size;
        return buffer;
    }

    private bool ConsumeBytes(int streamId, int numBytes)
    {
        return controller.ConsumeBytes(Stream(streamId), numBytes);
    }

    private void VerifyWindowUpdateSent(int streamId, int windowSizeIncrement)
    {
        frameWriter.Verify(w => w.WriteWindowUpdateAsync(ctx.Object, streamId, windowSizeIncrement,
            It.IsAny<TaskCompletionSource>()), Times.Once());
    }

    private void VerifyWindowUpdateNotSent(int streamId)
    {
        frameWriter.Verify(w => w.WriteWindowUpdateAsync(ctx.Object, streamId, It.IsAny<int>(),
            It.IsAny<TaskCompletionSource>()), Times.Never());
    }

    private void VerifyWindowUpdateNotSent()
    {
        frameWriter.Verify(w => w.WriteWindowUpdateAsync(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<TaskCompletionSource>()), Times.Never());
    }

    private int Window(int streamId)
    {
        return controller.WindowSize(Stream(streamId));
    }

    private IHttp2Stream Stream(int streamId)
    {
        return connection.Stream(streamId);
    }

    private void InitController(bool autoRefillConnectionWindow)
    {
        connection = new DefaultHttp2Connection(false);
        controller = new DefaultHttp2LocalFlowController(connection,
                DEFAULT_WINDOW_UPDATE_RATIO, autoRefillConnectionWindow).FrameWriter(frameWriter.Object);
        connection.Local().FlowController(controller);
        connection.Local().CreateStream(STREAM_ID, false);
        controller.ChannelHandlerContext(ctx.Object);
    }
}
