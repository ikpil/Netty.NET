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
using Moq;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Xunit.Sdk;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Tests for {@link DefaultHttp2RemoteFlowController}.
 */
// CLR: the abstract fixture stays abstract; xUnit runs its tests for each derived class, as JUnit does (owner rule 9).
// Mockito's @Mock fields are Moq Mock<T> fields created in the constructor (@BeforeEach; xUnit creates a test class
// instance per test, https://xunit.net/docs/shared-context). reset(ctx) is Mock.Reset() followed by the same setups,
// reset(listener) clears the listener's recorded invocations (it has no setups), verify(mock, times(n)) is
// Verify(..., Times.Exactly(n)), never() Times.Never(), atLeastOnce() Times.AtLeastOnce(), verifyNoMoreInteractions
// is VerifyNoOtherCalls and verifyNoInteractions asserts that the mock recorded no invocation at all (Moq Quickstart,
// https://github.com/devlooped/moq/wiki/Quickstart; docs/codec-http-porting.md, Shared CLR adaptations).
// channel.config()/bytesBeforeUnwritable()/isWritable() are the Transport members Config, BytesBeforeUnwritable and
// IsWritable. ctx.newPromise() is not stubbed: the controller does not create promises. ctx.flush() throwing
// AssertionFailedError("forbidden") is a Flush() setup that throws xUnit's FailException. The doAnswer stubs of the
// mocked FlowControlled are Moq callbacks; the answer that re-stubs size() to 10 before throwing changes the value that
// the size() setup returns, and AtomicInteger is a captured int. RuntimeException is Exception, assertThrows(T.class,
// ...) is Assert.ThrowsAny<T> (docs/codec-http-porting.md, Core test notes) and the anonymous Executables and Answers
// are lambdas. assertThrows(AssertionError.class, ...) checks the Java assert statements of updateDependencyTree,
// which the JUnit run enables (-ea); a failing Debug.Assert in a test run throws xUnit's TraceAssertException, "thrown
// when a failure from Trace.Assert or Debug.Assert has been detected"
// (https://raw.githubusercontent.com/xunit/xunit/main/src/xunit.v3.core/Exceptions/TraceAssertException.cs), so these
// tests need a Debug build of the library, as the JUnit tests need -ea. assertEquals(int, int, int delta) is the
// double overload with a tolerance, as Java widens the ints to the floating-point assertEquals.
public abstract class DefaultHttp2RemoteFlowControllerTest
{
    private const int STREAM_A = 1;
    private const int STREAM_B = 3;
    private const int STREAM_C = 5;
    private const int STREAM_D = 7;

    private DefaultHttp2RemoteFlowController controller;

    private readonly Mock<IChannelHandlerContext> ctx = new Mock<IChannelHandlerContext>();

    private readonly Mock<IChannel> channel = new Mock<IChannel>();

    private readonly Mock<IChannelConfig> config = new Mock<IChannelConfig>();

    private readonly Mock<IEventExecutor> executor = new Mock<IEventExecutor>();

    private readonly Mock<IHttp2RemoteFlowController.IListener> listener =
        new Mock<IHttp2RemoteFlowController.IListener>();

    private DefaultHttp2Connection connection;

    protected DefaultHttp2RemoteFlowControllerTest()
    {
        ctx.Setup(c => c.Flush()).Throws(FailException.ForFailure("forbidden"));
        SetChannelWritability(true);
        channel.Setup(c => c.Config).Returns(config.Object);
        executor.Setup(e => e.InEventLoop()).Returns(true);

        InitConnectionAndController();

        ResetCtx();
        // This is intentionally left out of initConnectionAndController so it can be tested below.
        controller.ChannelHandlerContext(ctx.Object);
        AssertWritabilityChanged(1, true);
        listener.Invocations.Clear();
    }

    protected abstract IStreamByteDistributor NewDistributor(IHttp2Connection connection);

    private void InitConnectionAndController()
    {
        connection = new DefaultHttp2Connection(false);
        controller = new DefaultHttp2RemoteFlowController(connection, NewDistributor(connection), listener.Object);
        connection.Remote().FlowController(controller);

        connection.Local().CreateStream(STREAM_A, false);
        connection.Local().CreateStream(STREAM_B, false);
        IHttp2Stream streamC = connection.Local().CreateStream(STREAM_C, false);
        IHttp2Stream streamD = connection.Local().CreateStream(STREAM_D, false);
        controller.UpdateDependencyTree(streamC.Id(), STREAM_A, DEFAULT_PRIORITY_WEIGHT, false);
        controller.UpdateDependencyTree(streamD.Id(), STREAM_A, DEFAULT_PRIORITY_WEIGHT, false);
    }

    [Fact]
    public void InitialWindowSizeShouldOnlyChangeStreams()
    {
        controller.InitialWindowSize(0);
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(CONNECTION_STREAM_ID));
        Assert.Equal(0, Window(STREAM_A));
        Assert.Equal(0, Window(STREAM_B));
        Assert.Equal(0, Window(STREAM_C));
        Assert.Equal(0, Window(STREAM_D));
        AssertWritabilityChanged(1, false);
    }

    [Fact]
    public void InitialWindowSizeMayReachMaxValue()
    {
        IncrementWindowSize(STREAM_C, int.MaxValue - DEFAULT_WINDOW_SIZE - 1);

        controller.InitialWindowSize(DEFAULT_WINDOW_SIZE + 1);

        Assert.Equal(int.MaxValue, Window(STREAM_C));
        Assert.Equal(DEFAULT_WINDOW_SIZE + 1, Window(STREAM_A));
        Assert.Equal(DEFAULT_WINDOW_SIZE + 1, Window(STREAM_B));
        Assert.Equal(DEFAULT_WINDOW_SIZE + 1, Window(STREAM_D));
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(CONNECTION_STREAM_ID));
    }

    [Fact]
    public void InitialWindowSizeOverflowShouldThrowConnectionError()
    {
        IncrementWindowSize(STREAM_C, int.MaxValue - DEFAULT_WINDOW_SIZE);

        Http2Exception e = Assert.ThrowsAny<Http2Exception>(() => controller.InitialWindowSize(DEFAULT_WINDOW_SIZE + 1));

        Assert.Equal(FLOW_CONTROL_ERROR, e.Error());
        Assert.False(IsStreamError(e));
        Assert.Equal(int.MaxValue, Window(STREAM_C));
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(CONNECTION_STREAM_ID));
    }

    [Fact]
    public void WindowUpdateOverflowShouldThrowStreamError()
    {
        IncrementWindowSize(STREAM_A, int.MaxValue - DEFAULT_WINDOW_SIZE);

        StreamException e = Assert.ThrowsAny<StreamException>(() => IncrementWindowSize(STREAM_A, 1));

        Assert.Equal(FLOW_CONTROL_ERROR, e.Error());
        Assert.Equal(STREAM_A, e.StreamId());
        Assert.Equal(int.MaxValue, Window(STREAM_A));
    }

    [Fact]
    public void WindowUpdateOverflowShouldThrowConnectionError()
    {
        IncrementWindowSize(CONNECTION_STREAM_ID, int.MaxValue - DEFAULT_WINDOW_SIZE);

        Http2Exception e = Assert.ThrowsAny<Http2Exception>(() => IncrementWindowSize(CONNECTION_STREAM_ID, 1));

        Assert.Equal(FLOW_CONTROL_ERROR, e.Error());
        Assert.False(IsStreamError(e));
        Assert.Equal(int.MaxValue, Window(CONNECTION_STREAM_ID));
    }

    [Fact]
    public void WindowUpdateShouldChangeConnectionWindow()
    {
        IncrementWindowSize(CONNECTION_STREAM_ID, 100);
        Assert.Equal(DEFAULT_WINDOW_SIZE + 100, Window(CONNECTION_STREAM_ID));
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(STREAM_A));
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(STREAM_B));
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(STREAM_C));
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(STREAM_D));
        VerifyNoInteractions(listener);
    }

    [Fact]
    public void WindowUpdateShouldChangeStreamWindow()
    {
        IncrementWindowSize(STREAM_A, 100);
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(CONNECTION_STREAM_ID));
        Assert.Equal(DEFAULT_WINDOW_SIZE + 100, Window(STREAM_A));
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(STREAM_B));
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(STREAM_C));
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(STREAM_D));
        VerifyNoInteractions(listener);
    }

    [Fact]
    public void PayloadSmallerThanWindowShouldBeWrittenImmediately()
    {
        FakeFlowControlled data = new FakeFlowControlled(5);
        SendData(STREAM_A, data);
        data.AssertNotWritten();
        VerifyNoInteractions(listener);
        controller.WritePendingBytes();
        data.AssertFullyWritten();
        VerifyNoInteractions(listener);
    }

    [Fact]
    public void EmptyPayloadShouldBeWrittenImmediately()
    {
        FakeFlowControlled data = new FakeFlowControlled(0);
        SendData(STREAM_A, data);
        data.AssertNotWritten();
        controller.WritePendingBytes();
        data.AssertFullyWritten();
        VerifyNoInteractions(listener);
    }

    [Fact]
    public void UnflushedPayloadsShouldBeDroppedOnCancel()
    {
        FakeFlowControlled data = new FakeFlowControlled(5);
        IHttp2Stream streamA = Stream(STREAM_A);
        SendData(STREAM_A, data);
        streamA.Close();
        controller.WritePendingBytes();
        data.AssertNotWritten();
        controller.WritePendingBytes();
        data.AssertNotWritten();
        listener.Verify(l => l.WritabilityChanged(streamA), Times.Exactly(1));
        Assert.False(controller.IsWritable(streamA));
    }

    [Fact]
    public void PayloadsShouldMerge()
    {
        controller.InitialWindowSize(15);
        FakeFlowControlled data1 = new FakeFlowControlled(5, true);
        FakeFlowControlled data2 = new FakeFlowControlled(10, true);
        SendData(STREAM_A, data1);
        SendData(STREAM_A, data2);
        data1.AssertNotWritten();
        data1.AssertNotWritten();
        data2.AssertMerged();
        controller.WritePendingBytes();
        data1.AssertFullyWritten();
        data2.AssertNotWritten();
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Exactly(1));
        Assert.False(controller.IsWritable(Stream(STREAM_A)));
    }

    [Fact]
    public void FlowControllerCorrectlyAccountsForBytesWithMerge()
    {
        controller.InitialWindowSize(112); // This must be more than the total merged frame size 110
        FakeFlowControlled data1 = new FakeFlowControlled(5, 2, true);
        FakeFlowControlled data2 = new FakeFlowControlled(5, 100, true);
        SendData(STREAM_A, data1);
        SendData(STREAM_A, data2);
        data1.AssertNotWritten();
        data1.AssertNotWritten();
        data2.AssertMerged();
        controller.WritePendingBytes();
        data1.AssertFullyWritten();
        data2.AssertNotWritten();
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Never());
        Assert.True(controller.IsWritable(Stream(STREAM_A)));
    }

    [Fact]
    public void StalledStreamShouldQueuePayloads()
    {
        controller.InitialWindowSize(0);
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Exactly(1));
        Assert.False(controller.IsWritable(Stream(STREAM_A)));
        listener.Invocations.Clear();

        FakeFlowControlled data = new FakeFlowControlled(15);
        FakeFlowControlled moreData = new FakeFlowControlled(0);
        SendData(STREAM_A, data);
        controller.WritePendingBytes();
        data.AssertNotWritten();
        SendData(STREAM_A, moreData);
        controller.WritePendingBytes();
        moreData.AssertNotWritten();
        VerifyNoInteractions(listener);
    }

    [Fact]
    public void QueuedPayloadsReceiveErrorOnStreamClose()
    {
        controller.InitialWindowSize(0);
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Exactly(1));
        Assert.False(controller.IsWritable(Stream(STREAM_A)));
        listener.Invocations.Clear();

        FakeFlowControlled data = new FakeFlowControlled(15);
        FakeFlowControlled moreData = new FakeFlowControlled(0);
        SendData(STREAM_A, data);
        controller.WritePendingBytes();
        data.AssertNotWritten();
        SendData(STREAM_A, moreData);
        controller.WritePendingBytes();
        moreData.AssertNotWritten();

        connection.Stream(STREAM_A).Close();
        data.AssertError(Http2Error.STREAM_CLOSED);
        moreData.AssertError(Http2Error.STREAM_CLOSED);
        VerifyNoInteractions(listener);
    }

    [Fact]
    public void PayloadLargerThanWindowShouldWritePartial()
    {
        controller.InitialWindowSize(5);
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Never());
        Assert.True(controller.IsWritable(Stream(STREAM_A)));
        listener.Invocations.Clear();

        FakeFlowControlled data = new FakeFlowControlled(10);
        SendData(STREAM_A, data);
        controller.WritePendingBytes();
        // Verify that a partial frame of 5 remains to be sent
        data.AssertPartiallyWritten(5);
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Exactly(1));
        Assert.False(controller.IsWritable(Stream(STREAM_A)));
        listener.VerifyNoOtherCalls();
    }

    [Fact]
    public void WindowUpdateAndFlushShouldTriggerWrite()
    {
        controller.InitialWindowSize(10);
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Never());
        Assert.True(controller.IsWritable(Stream(STREAM_A)));

        FakeFlowControlled data = new FakeFlowControlled(20);
        FakeFlowControlled moreData = new FakeFlowControlled(10);
        SendData(STREAM_A, data);
        SendData(STREAM_A, moreData);
        controller.WritePendingBytes();
        data.AssertPartiallyWritten(10);
        moreData.AssertNotWritten();
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Exactly(1));
        Assert.False(controller.IsWritable(Stream(STREAM_A)));
        listener.Invocations.Clear();
        ResetCtx();

        // Update the window and verify that the rest of data and some of moreData are written
        IncrementWindowSize(STREAM_A, 15);
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Never());
        Assert.False(controller.IsWritable(Stream(STREAM_A)));
        listener.Invocations.Clear();

        controller.WritePendingBytes();

        data.AssertFullyWritten();
        moreData.AssertPartiallyWritten(5);
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Never());
        Assert.False(controller.IsWritable(Stream(STREAM_A)));

        Assert.Equal(DEFAULT_WINDOW_SIZE - 25, Window(CONNECTION_STREAM_ID));
        Assert.Equal(0, Window(STREAM_A));
        Assert.Equal(10, Window(STREAM_B));
        Assert.Equal(10, Window(STREAM_C));
        Assert.Equal(10, Window(STREAM_D));
    }

    [Fact]
    public void InitialWindowUpdateShouldSendPayload()
    {
        IncrementWindowSize(CONNECTION_STREAM_ID, -Window(CONNECTION_STREAM_ID) + 10);
        AssertWritabilityChanged(0, true);
        listener.Invocations.Clear();

        controller.InitialWindowSize(0);
        AssertWritabilityChanged(1, false);
        listener.Invocations.Clear();

        FakeFlowControlled data = new FakeFlowControlled(10);
        SendData(STREAM_A, data);
        controller.WritePendingBytes();
        data.AssertNotWritten();

        // Verify that the entire frame was sent.
        controller.InitialWindowSize(10);
        data.AssertFullyWritten();
        AssertWritabilityChanged(0, false);
    }

    [Fact]
    public void SuccessiveSendsShouldNotInteract()
    {
        // Collapse the connection window to force queueing.
        IncrementWindowSize(CONNECTION_STREAM_ID, -Window(CONNECTION_STREAM_ID));
        Assert.Equal(0, Window(CONNECTION_STREAM_ID));
        AssertWritabilityChanged(1, false);
        listener.Invocations.Clear();

        FakeFlowControlled dataA = new FakeFlowControlled(10);
        // Queue data for stream A and allow most of it to be written.
        SendData(STREAM_A, dataA);
        controller.WritePendingBytes();
        dataA.AssertNotWritten();
        IncrementWindowSize(CONNECTION_STREAM_ID, 8);
        AssertWritabilityChanged(0, false);
        listener.Invocations.Clear();

        controller.WritePendingBytes();
        dataA.AssertPartiallyWritten(8);
        Assert.Equal(65527, Window(STREAM_A));
        Assert.Equal(0, Window(CONNECTION_STREAM_ID));
        AssertWritabilityChanged(0, false);
        listener.Invocations.Clear();

        // Queue data for stream B and allow the rest of A and all of B to be written.
        FakeFlowControlled dataB = new FakeFlowControlled(10);
        SendData(STREAM_B, dataB);
        controller.WritePendingBytes();
        dataB.AssertNotWritten();
        IncrementWindowSize(CONNECTION_STREAM_ID, 12);
        AssertWritabilityChanged(0, false);
        listener.Invocations.Clear();

        controller.WritePendingBytes();
        Assert.Equal(0, Window(CONNECTION_STREAM_ID));
        AssertWritabilityChanged(0, false);

        // Verify the rest of A is written.
        dataA.AssertFullyWritten();
        Assert.Equal(65525, Window(STREAM_A));

        dataB.AssertFullyWritten();
        Assert.Equal(65525, Window(STREAM_B));
        listener.VerifyNoOtherCalls();
    }

    [Fact]
    public void NegativeWindowShouldNotThrowException()
    {
        int initWindow = 20;
        int secondWindowSize = 10;
        controller.InitialWindowSize(initWindow);
        AssertWritabilityChanged(0, true);
        listener.Invocations.Clear();

        FakeFlowControlled data1 = new FakeFlowControlled(initWindow);
        FakeFlowControlled data2 = new FakeFlowControlled(5);

        // Deplete the stream A window to 0
        SendData(STREAM_A, data1);
        controller.WritePendingBytes();
        data1.AssertFullyWritten();
        Assert.True(Window(CONNECTION_STREAM_ID) > 0);
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Exactly(1));
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_B)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_C)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_D)), Times.Never());
        Assert.False(controller.IsWritable(Stream(STREAM_A)));
        Assert.True(controller.IsWritable(Stream(STREAM_B)));
        Assert.True(controller.IsWritable(Stream(STREAM_C)));
        Assert.True(controller.IsWritable(Stream(STREAM_D)));
        listener.Invocations.Clear();

        // Make the window size for stream A negative
        controller.InitialWindowSize(initWindow - secondWindowSize);
        Assert.Equal(-secondWindowSize, Window(STREAM_A));
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_B)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_C)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_D)), Times.Never());
        Assert.False(controller.IsWritable(Stream(STREAM_A)));
        Assert.True(controller.IsWritable(Stream(STREAM_B)));
        Assert.True(controller.IsWritable(Stream(STREAM_C)));
        Assert.True(controller.IsWritable(Stream(STREAM_D)));
        listener.Invocations.Clear();

        // Queue up a write. It should not be written now because the window is negative
        SendData(STREAM_A, data2);
        controller.WritePendingBytes();
        data2.AssertNotWritten();
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_B)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_C)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_D)), Times.Never());
        Assert.False(controller.IsWritable(Stream(STREAM_A)));
        Assert.True(controller.IsWritable(Stream(STREAM_B)));
        Assert.True(controller.IsWritable(Stream(STREAM_C)));
        Assert.True(controller.IsWritable(Stream(STREAM_D)));
        listener.Invocations.Clear();

        // Open the window size back up a bit (no send should happen)
        IncrementWindowSize(STREAM_A, 5);
        controller.WritePendingBytes();
        Assert.Equal(-5, Window(STREAM_A));
        data2.AssertNotWritten();
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_B)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_C)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_D)), Times.Never());
        Assert.False(controller.IsWritable(Stream(STREAM_A)));
        Assert.True(controller.IsWritable(Stream(STREAM_B)));
        Assert.True(controller.IsWritable(Stream(STREAM_C)));
        Assert.True(controller.IsWritable(Stream(STREAM_D)));
        listener.Invocations.Clear();

        // Open the window size back up a bit (no send should happen)
        IncrementWindowSize(STREAM_A, 5);
        controller.WritePendingBytes();
        Assert.Equal(0, Window(STREAM_A));
        data2.AssertNotWritten();
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_B)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_C)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_D)), Times.Never());
        Assert.False(controller.IsWritable(Stream(STREAM_A)));
        Assert.True(controller.IsWritable(Stream(STREAM_B)));
        Assert.True(controller.IsWritable(Stream(STREAM_C)));
        Assert.True(controller.IsWritable(Stream(STREAM_D)));
        listener.Invocations.Clear();

        // Open the window size back up and allow the write to happen
        IncrementWindowSize(STREAM_A, 5);
        controller.WritePendingBytes();
        data2.AssertFullyWritten();
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_B)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_C)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_D)), Times.Never());
        Assert.False(controller.IsWritable(Stream(STREAM_A)));
        Assert.True(controller.IsWritable(Stream(STREAM_B)));
        Assert.True(controller.IsWritable(Stream(STREAM_C)));
        Assert.True(controller.IsWritable(Stream(STREAM_D)));
    }

    [Fact]
    public void InitialWindowUpdateShouldSendEmptyFrame()
    {
        controller.InitialWindowSize(0);
        AssertWritabilityChanged(1, false);
        listener.Invocations.Clear();

        // First send a frame that will get buffered.
        FakeFlowControlled data = new FakeFlowControlled(10, false);
        SendData(STREAM_A, data);
        controller.WritePendingBytes();
        data.AssertNotWritten();

        // Now send an empty frame on the same stream and verify that it's also buffered.
        FakeFlowControlled data2 = new FakeFlowControlled(0, false);
        SendData(STREAM_A, data2);
        controller.WritePendingBytes();
        data2.AssertNotWritten();

        // Re-expand the window and verify that both frames were sent.
        controller.InitialWindowSize(10);
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_B)), Times.Exactly(1));
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_C)), Times.Exactly(1));
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_D)), Times.Exactly(1));
        Assert.False(controller.IsWritable(Stream(STREAM_A)));
        Assert.True(controller.IsWritable(Stream(STREAM_B)));
        Assert.True(controller.IsWritable(Stream(STREAM_C)));
        Assert.True(controller.IsWritable(Stream(STREAM_D)));

        data.AssertFullyWritten();
        data2.AssertFullyWritten();
    }

    [Fact]
    public void InitialWindowUpdateShouldSendPartialFrame()
    {
        controller.InitialWindowSize(0);
        AssertWritabilityChanged(1, false);
        listener.Invocations.Clear();

        FakeFlowControlled data = new FakeFlowControlled(10);
        SendData(STREAM_A, data);
        controller.WritePendingBytes();
        data.AssertNotWritten();

        // Verify that a partial frame of 5 was sent.
        controller.InitialWindowSize(5);
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_B)), Times.Exactly(1));
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_C)), Times.Exactly(1));
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_D)), Times.Exactly(1));
        Assert.False(controller.IsWritable(Stream(STREAM_A)));
        Assert.True(controller.IsWritable(Stream(STREAM_B)));
        Assert.True(controller.IsWritable(Stream(STREAM_C)));
        Assert.True(controller.IsWritable(Stream(STREAM_D)));

        data.AssertPartiallyWritten(5);
    }

    [Fact]
    public void ConnectionWindowUpdateShouldSendFrame()
    {
        // Set the connection window size to zero.
        ExhaustStreamWindow(CONNECTION_STREAM_ID);
        AssertWritabilityChanged(1, false);
        listener.Invocations.Clear();

        FakeFlowControlled data = new FakeFlowControlled(10);
        SendData(STREAM_A, data);
        controller.WritePendingBytes();
        data.AssertNotWritten();
        AssertWritabilityChanged(0, false);
        listener.Invocations.Clear();

        // Verify that the entire frame was sent.
        IncrementWindowSize(CONNECTION_STREAM_ID, 10);
        AssertWritabilityChanged(0, false);
        listener.Invocations.Clear();
        data.AssertNotWritten();

        controller.WritePendingBytes();
        data.AssertFullyWritten();
        AssertWritabilityChanged(0, false);
        Assert.Equal(0, Window(CONNECTION_STREAM_ID));
        Assert.Equal(DEFAULT_WINDOW_SIZE - 10, Window(STREAM_A));
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(STREAM_B));
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(STREAM_C));
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(STREAM_D));
    }

    [Fact]
    public void ConnectionWindowUpdateShouldSendPartialFrame()
    {
        // Set the connection window size to zero.
        ExhaustStreamWindow(CONNECTION_STREAM_ID);
        AssertWritabilityChanged(1, false);
        listener.Invocations.Clear();

        FakeFlowControlled data = new FakeFlowControlled(10);
        SendData(STREAM_A, data);
        controller.WritePendingBytes();
        data.AssertNotWritten();

        // Verify that a partial frame of 5 was sent.
        IncrementWindowSize(CONNECTION_STREAM_ID, 5);
        data.AssertNotWritten();
        AssertWritabilityChanged(0, false);
        listener.Invocations.Clear();

        controller.WritePendingBytes();
        data.AssertPartiallyWritten(5);
        AssertWritabilityChanged(0, false);
        Assert.Equal(0, Window(CONNECTION_STREAM_ID));
        Assert.Equal(DEFAULT_WINDOW_SIZE - 5, Window(STREAM_A));
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(STREAM_B));
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(STREAM_C));
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(STREAM_D));
    }

    [Fact]
    public void StreamWindowUpdateShouldSendFrame()
    {
        // Set the stream window size to zero.
        ExhaustStreamWindow(STREAM_A);
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Exactly(1));
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_B)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_C)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_D)), Times.Never());
        Assert.False(controller.IsWritable(Stream(STREAM_A)));
        Assert.True(controller.IsWritable(Stream(STREAM_B)));
        Assert.True(controller.IsWritable(Stream(STREAM_C)));
        Assert.True(controller.IsWritable(Stream(STREAM_D)));
        listener.Invocations.Clear();

        FakeFlowControlled data = new FakeFlowControlled(10);
        SendData(STREAM_A, data);
        controller.WritePendingBytes();
        data.AssertNotWritten();

        // Verify that the entire frame was sent.
        IncrementWindowSize(STREAM_A, 10);
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_B)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_C)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_D)), Times.Never());
        Assert.False(controller.IsWritable(Stream(STREAM_A)));
        Assert.True(controller.IsWritable(Stream(STREAM_B)));
        Assert.True(controller.IsWritable(Stream(STREAM_C)));
        Assert.True(controller.IsWritable(Stream(STREAM_D)));
        listener.Invocations.Clear();

        data.AssertNotWritten();
        controller.WritePendingBytes();
        data.AssertFullyWritten();
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_B)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_C)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_D)), Times.Never());
        Assert.False(controller.IsWritable(Stream(STREAM_A)));
        Assert.True(controller.IsWritable(Stream(STREAM_B)));
        Assert.True(controller.IsWritable(Stream(STREAM_C)));
        Assert.True(controller.IsWritable(Stream(STREAM_D)));
        Assert.Equal(DEFAULT_WINDOW_SIZE - 10, Window(CONNECTION_STREAM_ID));
        Assert.Equal(0, Window(STREAM_A));
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(STREAM_B));
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(STREAM_C));
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(STREAM_D));
    }

    [Fact]
    public void StreamWindowUpdateShouldSendPartialFrame()
    {
        // Set the stream window size to zero.
        ExhaustStreamWindow(STREAM_A);
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Exactly(1));
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_B)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_C)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_D)), Times.Never());
        Assert.False(controller.IsWritable(Stream(STREAM_A)));
        Assert.True(controller.IsWritable(Stream(STREAM_B)));
        Assert.True(controller.IsWritable(Stream(STREAM_C)));
        Assert.True(controller.IsWritable(Stream(STREAM_D)));
        listener.Invocations.Clear();

        FakeFlowControlled data = new FakeFlowControlled(10);
        SendData(STREAM_A, data);
        controller.WritePendingBytes();
        data.AssertNotWritten();

        // Verify that a partial frame of 5 was sent.
        IncrementWindowSize(STREAM_A, 5);
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_B)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_C)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_D)), Times.Never());
        Assert.False(controller.IsWritable(Stream(STREAM_A)));
        Assert.True(controller.IsWritable(Stream(STREAM_B)));
        Assert.True(controller.IsWritable(Stream(STREAM_C)));
        Assert.True(controller.IsWritable(Stream(STREAM_D)));
        listener.Invocations.Clear();

        data.AssertNotWritten();
        controller.WritePendingBytes();
        data.AssertPartiallyWritten(5);
        Assert.Equal(DEFAULT_WINDOW_SIZE - 5, Window(CONNECTION_STREAM_ID));
        Assert.Equal(0, Window(STREAM_A));
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(STREAM_B));
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(STREAM_C));
        Assert.Equal(DEFAULT_WINDOW_SIZE, Window(STREAM_D));
    }

    [Fact]
    public void FlowControlledWriteThrowsAnException()
    {
        Mock<IHttp2RemoteFlowController.IFlowControlled> flowControlled = MockedFlowControlledThatThrowsOnWrite();
        IHttp2Stream stream = Stream(STREAM_A);
        flowControlled.Setup(f => f.Error(It.IsAny<IChannelHandlerContext>(), It.IsAny<Exception>()))
            .Callback(() => stream.CloseLocalSide());

        int windowBefore = Window(STREAM_A);

        controller.AddFlowControlled(stream, flowControlled.Object);
        controller.WritePendingBytes();

        flowControlled.Verify(f => f.Write(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>()), Times.AtLeastOnce());
        flowControlled.Verify(f => f.Error(It.IsAny<IChannelHandlerContext>(), It.IsAny<Exception>()), Times.Once());
        flowControlled.Verify(f => f.WriteComplete(), Times.Never());

        Assert.Equal(90, windowBefore - Window(STREAM_A));
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Exactly(1));
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_B)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_C)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_D)), Times.Never());
        Assert.False(controller.IsWritable(Stream(STREAM_A)));
        Assert.True(controller.IsWritable(Stream(STREAM_B)));
        Assert.True(controller.IsWritable(Stream(STREAM_C)));
        Assert.True(controller.IsWritable(Stream(STREAM_D)));
    }

    [Fact]
    public void FlowControlledWriteAndErrorThrowAnException()
    {
        Mock<IHttp2RemoteFlowController.IFlowControlled> flowControlled = MockedFlowControlledThatThrowsOnWrite();
        IHttp2Stream stream = Stream(STREAM_A);
        flowControlled.Setup(f => f.Error(It.IsAny<IChannelHandlerContext>(), It.IsAny<Exception>()))
            .Throws(Http2TestUtil.FAKE_EXCEPTION);

        int windowBefore = Window(STREAM_A);

        Http2Exception e = Assert.ThrowsAny<Http2Exception>(() =>
        {
            controller.AddFlowControlled(stream, flowControlled.Object);
            controller.WritePendingBytes();
        });
        Assert.Same(Http2TestUtil.FAKE_EXCEPTION, e.InnerException);

        flowControlled.Verify(f => f.Write(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>()), Times.AtLeastOnce());
        flowControlled.Verify(f => f.Error(It.IsAny<IChannelHandlerContext>(), It.IsAny<Exception>()), Times.Once());
        flowControlled.Verify(f => f.WriteComplete(), Times.Never());

        Assert.Equal(90, windowBefore - Window(STREAM_A));
        VerifyNoInteractions(listener);
    }

    [Fact]
    public void FlowControlledWriteCompleteThrowsAnException()
    {
        Mock<IHttp2RemoteFlowController.IFlowControlled> flowControlled =
                new Mock<IHttp2RemoteFlowController.IFlowControlled>();
        IHttp2Stream streamA = Stream(STREAM_A);
        int size = 150;
        flowControlled.Setup(f => f.Size()).Returns(() => size);
        flowControlled.Setup(f => f.Write(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>()))
            .Callback(() => size -= 50);

        IHttp2Stream stream = Stream(STREAM_A);
        flowControlled.Setup(f => f.WriteComplete()).Throws(new Exception("writeComplete failed"));

        int windowBefore = Window(STREAM_A);

        controller.AddFlowControlled(stream, flowControlled.Object);
        controller.WritePendingBytes();

        flowControlled.Verify(f => f.Write(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>()), Times.Exactly(3));
        flowControlled.Verify(f => f.Error(It.IsAny<IChannelHandlerContext>(), It.IsAny<Exception>()), Times.Never());
        flowControlled.Verify(f => f.WriteComplete(), Times.Once());

        Assert.Equal(150, windowBefore - Window(STREAM_A));
        listener.Verify(l => l.WritabilityChanged(streamA), Times.Exactly(1));
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_B)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_C)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_D)), Times.Never());
        Assert.False(controller.IsWritable(streamA));
        Assert.True(controller.IsWritable(Stream(STREAM_B)));
        Assert.True(controller.IsWritable(Stream(STREAM_C)));
        Assert.True(controller.IsWritable(Stream(STREAM_D)));
    }

    [Fact]
    public void CloseStreamInFlowControlledError()
    {
        Mock<IHttp2RemoteFlowController.IFlowControlled> flowControlled =
                new Mock<IHttp2RemoteFlowController.IFlowControlled>();
        IHttp2Stream stream = Stream(STREAM_A);
        flowControlled.Setup(f => f.Size()).Returns(100);
        flowControlled.Setup(f => f.Write(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>()))
            .Throws(new Exception("write failed"));
        flowControlled.Setup(f => f.Error(It.IsAny<IChannelHandlerContext>(), It.IsAny<Exception>()))
            .Callback(() => stream.Close());

        controller.AddFlowControlled(stream, flowControlled.Object);
        controller.WritePendingBytes();

        flowControlled.Verify(f => f.Write(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>()), Times.Once());
        flowControlled.Verify(f => f.Error(It.IsAny<IChannelHandlerContext>(), It.IsAny<Exception>()), Times.Once());
        flowControlled.Verify(f => f.WriteComplete(), Times.Never());
        listener.Verify(l => l.WritabilityChanged(stream), Times.Exactly(1));
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_B)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_C)), Times.Never());
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_D)), Times.Never());
        Assert.False(controller.IsWritable(stream));
        Assert.True(controller.IsWritable(Stream(STREAM_B)));
        Assert.True(controller.IsWritable(Stream(STREAM_C)));
        Assert.True(controller.IsWritable(Stream(STREAM_D)));
    }

    [Fact]
    public void NonWritableChannelDoesNotAttemptToWrite()
    {
        // Start the channel as not writable and exercise the public methods of the flow controller
        // making sure no frames are written.
        SetChannelWritability(false);
        AssertWritabilityChanged(1, false);
        listener.Invocations.Clear();
        FakeFlowControlled dataA = new FakeFlowControlled(1);
        FakeFlowControlled dataB = new FakeFlowControlled(1);
        IHttp2Stream stream = Stream(STREAM_A);

        controller.AddFlowControlled(stream, dataA);
        controller.WritePendingBytes();
        dataA.AssertNotWritten();

        controller.IncrementWindowSize(stream, 100);
        controller.WritePendingBytes();
        dataA.AssertNotWritten();

        controller.AddFlowControlled(stream, dataB);
        controller.WritePendingBytes();
        dataA.AssertNotWritten();
        dataB.AssertNotWritten();
        AssertWritabilityChanged(0, false);

        // Now change the channel to writable and make sure frames are written.
        SetChannelWritability(true);
        AssertWritabilityChanged(1, true);
        controller.WritePendingBytes();
        dataA.AssertFullyWritten();
        dataB.AssertFullyWritten();
    }

    [Fact]
    public void ContextShouldSendQueuedFramesWhenSet()
    {
        // Re-initialize the controller so we can ensure the context hasn't been set yet.
        InitConnectionAndController();

        FakeFlowControlled dataA = new FakeFlowControlled(1);
        IHttp2Stream stream = Stream(STREAM_A);

        // Queue some frames
        controller.AddFlowControlled(stream, dataA);
        dataA.AssertNotWritten();

        controller.IncrementWindowSize(stream, 100);
        dataA.AssertNotWritten();

        AssertWritabilityChanged(0, false);

        // Set the controller
        controller.ChannelHandlerContext(ctx.Object);
        dataA.AssertFullyWritten();

        AssertWritabilityChanged(1, true);
    }

    [Fact]
    public void InitialWindowSizeWithNoContextShouldNotThrow()
    {
        // Re-initialize the controller so we can ensure the context hasn't been set yet.
        InitConnectionAndController();

        // This should not throw.
        controller.InitialWindowSize(1024 * 100);

        FakeFlowControlled dataA = new FakeFlowControlled(1);
        IHttp2Stream stream = Stream(STREAM_A);

        // Queue some frames
        controller.AddFlowControlled(stream, dataA);
        dataA.AssertNotWritten();

        // Set the controller
        controller.ChannelHandlerContext(ctx.Object);
        dataA.AssertFullyWritten();
    }

    [Fact]
    public void InvalidParentStreamIdThrows()
    {
        Assert.ThrowsAny<TraceAssertException>(
            () => controller.UpdateDependencyTree(STREAM_D, -1, DEFAULT_PRIORITY_WEIGHT, true));
    }

    [Fact]
    public void InvalidChildStreamIdThrows()
    {
        Assert.ThrowsAny<TraceAssertException>(
            () => controller.UpdateDependencyTree(-1, STREAM_D, DEFAULT_PRIORITY_WEIGHT, true));
    }

    [Fact]
    public void ConnectionChildStreamIdThrows()
    {
        Assert.ThrowsAny<TraceAssertException>(
            () => controller.UpdateDependencyTree(0, STREAM_D, DEFAULT_PRIORITY_WEIGHT, true));
    }

    [Fact]
    public void InvalidWeightTooSmallThrows()
    {
        Assert.ThrowsAny<TraceAssertException>(
            () => controller.UpdateDependencyTree(STREAM_A, STREAM_D, (short) (MIN_WEIGHT - 1), true));
    }

    [Fact]
    public void InvalidWeightTooBigThrows()
    {
        Assert.ThrowsAny<TraceAssertException>(
            () => controller.UpdateDependencyTree(STREAM_A, STREAM_D, (short) (MAX_WEIGHT + 1), true));
    }

    [Fact]
    public void DependencyOnSelfThrows()
    {
        Assert.ThrowsAny<TraceAssertException>(
            () => controller.UpdateDependencyTree(STREAM_A, STREAM_A, DEFAULT_PRIORITY_WEIGHT, true));
    }

    private void AssertWritabilityChanged(int amt, bool writable)
    {
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_A)), Times.Exactly(amt));
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_B)), Times.Exactly(amt));
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_C)), Times.Exactly(amt));
        listener.Verify(l => l.WritabilityChanged(Stream(STREAM_D)), Times.Exactly(amt));
        if (writable)
        {
            Assert.True(controller.IsWritable(Stream(STREAM_A)));
            Assert.True(controller.IsWritable(Stream(STREAM_B)));
            Assert.True(controller.IsWritable(Stream(STREAM_C)));
            Assert.True(controller.IsWritable(Stream(STREAM_D)));
        }
        else
        {
            Assert.False(controller.IsWritable(Stream(STREAM_A)));
            Assert.False(controller.IsWritable(Stream(STREAM_B)));
            Assert.False(controller.IsWritable(Stream(STREAM_C)));
            Assert.False(controller.IsWritable(Stream(STREAM_D)));
        }
    }

    // CLR: verifyNoInteractions(mock): the mock recorded no invocation.
    private static void VerifyNoInteractions<T>(Mock<T> mock) where T : class
    {
        Assert.Empty(mock.Invocations);
    }

    private static Mock<IHttp2RemoteFlowController.IFlowControlled> MockedFlowControlledThatThrowsOnWrite()
    {
        Mock<IHttp2RemoteFlowController.IFlowControlled> flowControlled =
                new Mock<IHttp2RemoteFlowController.IFlowControlled>();
        int size = 100;
        flowControlled.Setup(f => f.Size()).Returns(() => size);
        flowControlled.Setup(f => f.Write(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>()))
            .Callback(() =>
            {
                // Write most of the bytes and then fail
                size = 10;
                throw new Exception("Write failed");
            });
        return flowControlled;
    }

    private void SendData(int streamId, FakeFlowControlled data)
    {
        IHttp2Stream stream = Stream(streamId);
        controller.AddFlowControlled(stream, data);
    }

    private void ExhaustStreamWindow(int streamId)
    {
        IncrementWindowSize(streamId, -Window(streamId));
    }

    private int Window(int streamId)
    {
        return controller.WindowSize(Stream(streamId));
    }

    private void IncrementWindowSize(int streamId, int delta)
    {
        controller.IncrementWindowSize(Stream(streamId), delta);
    }

    private IHttp2Stream Stream(int streamId)
    {
        return connection.Stream(streamId);
    }

    private void ResetCtx()
    {
        ctx.Reset();
        ctx.Setup(c => c.Channel).Returns(channel.Object);
        ctx.Setup(c => c.Executor).Returns(executor.Object);
    }

    private void SetChannelWritability(bool isWritable)
    {
        channel.Setup(c => c.BytesBeforeUnwritable).Returns(isWritable ? long.MaxValue : 0);
        channel.Setup(c => c.IsWritable).Returns(isWritable);
        if (controller != null)
        {
            controller.ChannelWritabilityChanged();
        }
    }

    private sealed class FakeFlowControlled : IHttp2RemoteFlowController.IFlowControlled
    {
        private int currentPadding;
        private int currentPayloadSize;
        private int originalPayloadSize;
        private int originalPadding;
        private bool writeCalled;
        private readonly bool mergeable;
        private bool merged;

        private Exception t;

        internal FakeFlowControlled(int size) : this(size, false)
        {
        }

        internal FakeFlowControlled(int size, bool mergeable) : this(size, 0, mergeable)
        {
        }

        internal FakeFlowControlled(int payloadSize, int padding, bool mergeable)
        {
            currentPayloadSize = originalPayloadSize = payloadSize;
            currentPadding = originalPadding = padding;
            this.mergeable = mergeable;
        }

        public int Size()
        {
            return currentPayloadSize + currentPadding;
        }

        private int OriginalSize()
        {
            return originalPayloadSize + originalPadding;
        }

        public void Error(IChannelHandlerContext ctx, Exception t)
        {
            this.t = t;
        }

        public void WriteComplete()
        {
        }

        public void Write(IChannelHandlerContext ctx, int allowedBytes)
        {
            if (allowedBytes <= 0 && Size() != 0)
            {
                // Write has been called but no data can be written
                return;
            }
            writeCalled = true;
            int written = Math.Min(Size(), allowedBytes);
            if (written > currentPayloadSize)
            {
                written -= currentPayloadSize;
                currentPayloadSize = 0;
                currentPadding -= written;
            }
            else
            {
                currentPayloadSize -= written;
            }
        }

        public bool Merge(IChannelHandlerContext ctx, IHttp2RemoteFlowController.IFlowControlled next)
        {
            if (mergeable && next is FakeFlowControlled)
            {
                FakeFlowControlled ffcNext = (FakeFlowControlled) next;
                originalPayloadSize += ffcNext.originalPayloadSize;
                currentPayloadSize += ffcNext.originalPayloadSize;
                currentPadding = originalPadding = Math.Max(originalPadding, ffcNext.originalPadding);
                ffcNext.merged = true;
                return true;
            }
            return false;
        }

        public int Written()
        {
            return OriginalSize() - Size();
        }

        public void AssertNotWritten()
        {
            Assert.False(writeCalled);
        }

        public void AssertPartiallyWritten(int expectedWritten)
        {
            AssertPartiallyWritten(expectedWritten, 0);
        }

        public void AssertPartiallyWritten(int expectedWritten, int delta)
        {
            Assert.True(writeCalled);
            Assert.Equal(expectedWritten, Written(), (double) delta);
        }

        public void AssertFullyWritten()
        {
            Assert.True(writeCalled);
            Assert.Equal(0, currentPayloadSize);
            Assert.Equal(0, currentPadding);
        }

        public bool AssertMerged()
        {
            return merged;
        }

        public void AssertError(Http2Error error)
        {
            Assert.NotNull(t);
            if (error != null)
            {
                Assert.Same(error, ((Http2Exception) t).Error());
            }
        }
    }
}
