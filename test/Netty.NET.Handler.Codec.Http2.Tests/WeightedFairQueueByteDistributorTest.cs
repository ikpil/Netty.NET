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

using System.Linq;
using Moq;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: @BeforeEach setup() is the constructor (xUnit creates a test class instance per test,
// https://xunit.net/docs/shared-context) and MockitoAnnotations.initMocks is the Moq writer field of the base class.
// doAnswer(writeAnswer(...)).when(writer).write(any, anyInt) is a Write setup with the base class callback;
// doThrow(...)/doAnswer(...) for write(same(stream C), eq(3)) are later setups for those arguments, which Moq matches
// before the general one, and reset(writer) is Mock.Reset() (setups and invocations, as Mockito's reset; Moq
// Quickstart, https://github.com/devlooped/moq/wiki/Quickstart). same(stream) is It.Is with ReferenceEquals, verify
// modes are Moq Times (times(n) Times.Exactly(n), atMost/atLeastOnce/never), VerificationMode is Times, and the
// ArgumentCaptor of captureWrites is the int arguments of the verified Write invocations for the stream
// (Mock.Invocations). assertThrows(T.class, ...) is Assert.ThrowsAny<T> (docs/codec-http-porting.md, Core test notes)
// and the anonymous Executable is a lambda; getCause() is InnerException.
public class WeightedFairQueueByteDistributorTest : AbstractWeightedFairQueueByteDistributorDependencyTest
{
    private const int STREAM_A = 1;
    private const int STREAM_B = 3;
    private const int STREAM_C = 5;
    private const int STREAM_D = 7;
    private const int STREAM_E = 9;
    private const int ALLOCATION_QUANTUM = 100;

    public WeightedFairQueueByteDistributorTest()
    {
        // Assume we always write all the allocated bytes.
        writer.Setup(w => w.Write(It.IsAny<IHttp2Stream>(), It.IsAny<int>())).Callback(WriteAnswer(false));

        Setup(-1);
    }

    private void Setup(int maxStateOnlySize)
    {
        connection = new DefaultHttp2Connection(false);
        distributor = maxStateOnlySize >= 0 ? new WeightedFairQueueByteDistributor(connection, maxStateOnlySize)
                                            : new WeightedFairQueueByteDistributor(connection);
        distributor.AllocationQuantum(ALLOCATION_QUANTUM);

        connection.Local().CreateStream(STREAM_A, false);
        connection.Local().CreateStream(STREAM_B, false);
        IHttp2Stream streamC = connection.Local().CreateStream(STREAM_C, false);
        IHttp2Stream streamD = connection.Local().CreateStream(STREAM_D, false);
        SetPriority(streamC.Id(), STREAM_A, DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamD.Id(), STREAM_A, DEFAULT_PRIORITY_WEIGHT, false);
    }

    /**
     * In this test, we block B such that it has no frames. We distribute enough bytes for all streams and stream B
     * should be preserved in the priority queue structure until it has no "active" children, but it should not be
     * doubly added to stream 0.
     *
     * <pre>
     *         0
     *         |
     *         A
     *         |
     *        [B]
     *         |
     *         C
     *         |
     *         D
     * </pre>
     *
     * After the write:
     * <pre>
     *         0
     * </pre>
     */
    [Fact]
    public void WriteWithNonActiveStreamShouldNotDobuleAddToPriorityQueue()
    {
        InitState(STREAM_A, 400, true);
        InitState(STREAM_B, 500, true);
        InitState(STREAM_C, 600, true);
        InitState(STREAM_D, 700, true);

        SetPriority(STREAM_B, STREAM_A, DEFAULT_PRIORITY_WEIGHT, true);
        SetPriority(STREAM_D, STREAM_C, DEFAULT_PRIORITY_WEIGHT, true);

        // Block B, but it should still remain in the queue/tree structure.
        InitState(STREAM_B, 0, false);

        // Get the streams before the write, because they may be closed.
        IHttp2Stream streamA = Stream(STREAM_A);
        IHttp2Stream streamB = Stream(STREAM_B);
        IHttp2Stream streamC = Stream(STREAM_C);
        IHttp2Stream streamD = Stream(STREAM_D);

        writer.Reset();
        writer.Setup(w => w.Write(It.IsAny<IHttp2Stream>(), It.IsAny<int>())).Callback(WriteAnswer(true));

        Assert.False(Write(400 + 600 + 700));
        Assert.Equal(400, CaptureWrites(streamA));
        VerifyNeverWrite(streamB);
        Assert.Equal(600, CaptureWrites(streamC));
        Assert.Equal(700, CaptureWrites(streamD));
    }

    [Fact]
    public void BytesUnassignedAfterProcessing()
    {
        InitState(STREAM_A, 1, true);
        InitState(STREAM_B, 2, true);
        InitState(STREAM_C, 3, true);
        InitState(STREAM_D, 4, true);

        Assert.False(Write(10));
        VerifyWrite(STREAM_A, 1);
        VerifyWrite(STREAM_B, 2);
        VerifyWrite(STREAM_C, 3);
        VerifyWrite(STREAM_D, 4);

        Assert.False(Write(10));
        VerifyAnyWrite(STREAM_A, 1);
        VerifyAnyWrite(STREAM_B, 1);
        VerifyAnyWrite(STREAM_C, 1);
        VerifyAnyWrite(STREAM_D, 1);
    }

    [Fact]
    public void ConnectionErrorForWriterException()
    {
        InitState(STREAM_A, 1, true);
        InitState(STREAM_B, 2, true);
        InitState(STREAM_C, 3, true);
        InitState(STREAM_D, 4, true);

        IHttp2Stream streamC = Stream(STREAM_C);
        writer.Setup(w => w.Write(It.Is<IHttp2Stream>(s => ReferenceEquals(s, streamC)), 3))
            .Throws(Http2TestUtil.FAKE_EXCEPTION);

        Http2Exception e = Assert.ThrowsAny<Http2Exception>(() => Write(10));
        Assert.False(Http2Exception.IsStreamError(e));
        Assert.Equal(Http2Error.INTERNAL_ERROR, e.Error());
        Assert.Same(Http2TestUtil.FAKE_EXCEPTION, e.InnerException);

        VerifyWrite(Times.AtMost(1), STREAM_A, 1);
        VerifyWrite(Times.AtMost(1), STREAM_B, 2);
        VerifyWrite(STREAM_C, 3);
        VerifyWrite(Times.AtMost(1), STREAM_D, 4);

        writer.Setup(w => w.Write(It.Is<IHttp2Stream>(s => ReferenceEquals(s, streamC)), 3))
            .Callback(WriteAnswer(false));
        Assert.False(Write(10));
        VerifyWrite(STREAM_A, 1);
        VerifyWrite(STREAM_B, 2);
        VerifyWrite(Times.Exactly(2), STREAM_C, 3);
        VerifyWrite(STREAM_D, 4);
    }

    /**
     * In this test, we verify that each stream is allocated a minimum chunk size. When bytes
     * run out, the remaining streams will be next in line for the next iteration.
     */
    [Fact]
    public void MinChunkShouldBeAllocatedPerStream()
    {
        // Re-assign weights.
        SetPriority(STREAM_A, 0, (short) 50, false);
        SetPriority(STREAM_B, 0, (short) 200, false);
        SetPriority(STREAM_C, STREAM_A, (short) 100, false);
        SetPriority(STREAM_D, STREAM_A, (short) 100, false);

        // Update the streams.
        InitState(STREAM_A, ALLOCATION_QUANTUM, true);
        InitState(STREAM_B, ALLOCATION_QUANTUM, true);
        InitState(STREAM_C, ALLOCATION_QUANTUM, true);
        InitState(STREAM_D, ALLOCATION_QUANTUM, true);

        // Only write 3 * chunkSize, so that we'll only write to the first 3 streams.
        int written = 3 * ALLOCATION_QUANTUM;
        Assert.True(Write(written));
        Assert.Equal(ALLOCATION_QUANTUM, CaptureWrites(STREAM_A));
        Assert.Equal(ALLOCATION_QUANTUM, CaptureWrites(STREAM_B));
        Assert.Equal(ALLOCATION_QUANTUM, CaptureWrites(STREAM_C));
        VerifyWrite(Times.AtMost(1), STREAM_D, 0);

        // Now write again and verify that the last stream is written to.
        Assert.False(Write(ALLOCATION_QUANTUM));
        Assert.Equal(ALLOCATION_QUANTUM, CaptureWrites(STREAM_A));
        Assert.Equal(ALLOCATION_QUANTUM, CaptureWrites(STREAM_B));
        Assert.Equal(ALLOCATION_QUANTUM, CaptureWrites(STREAM_C));
        Assert.Equal(ALLOCATION_QUANTUM, CaptureWrites(STREAM_D));
    }

    /**
     * In this test, we verify that the highest priority frame which has 0 bytes to send, but an empty frame is able
     * to send that empty frame.
     *
     * <pre>
     *         0
     *        / \
     *       A   B
     *      / \
     *     C   D
     * </pre>
     *
     * After the tree shift:
     *
     * <pre>
     *         0
     *         |
     *         A
     *         |
     *         B
     *        / \
     *       C   D
     * </pre>
     */
    [Fact]
    public void EmptyFrameAtHeadIsWritten()
    {
        InitState(STREAM_A, 0, true);
        InitState(STREAM_B, 0, true);
        InitState(STREAM_C, 0, true);
        InitState(STREAM_D, 10, true);

        SetPriority(STREAM_B, STREAM_A, DEFAULT_PRIORITY_WEIGHT, true);

        Assert.False(Write(10));
        VerifyWrite(STREAM_A, 0);
        VerifyWrite(STREAM_B, 0);
        VerifyWrite(STREAM_C, 0);
        VerifyWrite(STREAM_D, 10);
    }

    /**
     * In this test, we block A which allows bytes to be written by C and D. Here's a view of the tree (stream A is
     * blocked).
     *
     * <pre>
     *         0
     *        / \
     *      [A]  B
     *      / \
     *     C   D
     * </pre>
     */
    [Fact]
    public void BlockedStreamNoDataShouldSpreadDataToChildren()
    {
        BlockedStreamShouldSpreadDataToChildren(false);
    }

    /**
     * In this test, we block A and also give it an empty data frame to send.
     * All bytes should be delegated to by C and D. Here's a view of the tree (stream A is blocked).
     *
     * <pre>
     *           0
     *         /   \
     *      [A](0)  B
     *      / \
     *     C   D
     * </pre>
     */
    [Fact]
    public void BlockedStreamWithDataAndNotAllowedToSendShouldSpreadDataToChildren()
    {
        // A cannot stream.
        InitState(STREAM_A, 0, true, false);
        BlockedStreamShouldSpreadDataToChildren(false);
    }

    /**
     * In this test, we allow A to send, but expect the flow controller will only write to the stream 1 time.
     * This is because we give the stream a chance to write its empty frame 1 time, and the stream will not
     * be written to again until a update stream is called.
     *
     * <pre>
     *         0
     *        / \
     *       A   B
     *      / \
     *     C   D
     * </pre>
     */
    [Fact]
    public void StreamWithZeroFlowControlWindowAndDataShouldWriteOnlyOnce()
    {
        InitState(STREAM_A, 0, true, true);
        BlockedStreamShouldSpreadDataToChildren(true);

        // Make sure if we call update stream again, A should write 1 more time.
        InitState(STREAM_A, 0, true, true);
        Assert.False(Write(1));
        VerifyWrite(Times.Exactly(2), STREAM_A, 0);

        // Try to write again, but since no initState A should not write again
        Assert.False(Write(1));
        VerifyWrite(Times.Exactly(2), STREAM_A, 0);
    }

    private void BlockedStreamShouldSpreadDataToChildren(bool streamAShouldWriteZero)
    {
        InitState(STREAM_B, 10, true);
        InitState(STREAM_C, 10, true);
        InitState(STREAM_D, 10, true);

        // Write up to 10 bytes.
        Assert.True(Write(10));

        if (streamAShouldWriteZero)
        {
            VerifyWrite(STREAM_A, 0);
        }
        else
        {
            VerifyNeverWrite(STREAM_A);
        }
        VerifyWrite(Times.AtMost(1), STREAM_C, 0);
        VerifyWrite(Times.AtMost(1), STREAM_D, 0);

        // B is entirely written
        VerifyWrite(STREAM_B, 10);

        // Now test that writes get delegated from A (which is blocked) to its children
        Assert.True(Write(5));
        if (streamAShouldWriteZero)
        {
            VerifyWrite(Times.Exactly(1), STREAM_A, 0);
        }
        else
        {
            VerifyNeverWrite(STREAM_A);
        }
        VerifyWrite(STREAM_D, 5);
        VerifyWrite(Times.AtMost(1), STREAM_C, 0);

        Assert.True(Write(5));
        if (streamAShouldWriteZero)
        {
            VerifyWrite(Times.Exactly(1), STREAM_A, 0);
        }
        else
        {
            VerifyNeverWrite(STREAM_A);
        }
        Assert.Equal(10, CaptureWrites(STREAM_C) + CaptureWrites(STREAM_D));

        Assert.True(Write(5));
        Assert.False(Write(5));
        if (streamAShouldWriteZero)
        {
            VerifyWrite(Times.Exactly(1), STREAM_A, 0);
        }
        else
        {
            VerifyNeverWrite(STREAM_A);
        }
        VerifyWrite(Times.Exactly(2), STREAM_C, 5);
        VerifyWrite(Times.Exactly(2), STREAM_D, 5);
    }

    /**
     * In this test, we block B which allows all bytes to be written by A. A should not share the data with its children
     * since it's not blocked.
     *
     * <pre>
     *         0
     *        / \
     *       A  [B]
     *      / \
     *     C   D
     * </pre>
     */
    [Fact]
    public void ChildrenShouldNotSendDataUntilParentBlocked()
    {
        // B cannot stream.
        InitState(STREAM_A, 10, true);
        InitState(STREAM_C, 10, true);
        InitState(STREAM_D, 10, true);

        // Write up to 10 bytes.
        Assert.True(Write(10));

        // A is assigned all of the bytes.
        VerifyWrite(STREAM_A, 10);
        VerifyNeverWrite(STREAM_B);
        VerifyWrite(Times.AtMost(1), STREAM_C, 0);
        VerifyWrite(Times.AtMost(1), STREAM_D, 0);
    }

    /**
     * In this test, we block B which allows all bytes to be written by A. Once A is complete, it will spill over the
     * remaining of its portion to its children.
     *
     * <pre>
     *         0
     *        / \
     *       A  [B]
     *      / \
     *     C   D
     * </pre>
     */
    [Fact]
    public void ParentShouldWaterFallDataToChildren()
    {
        // B cannot stream.
        InitState(STREAM_A, 5, true);
        InitState(STREAM_C, 10, true);
        InitState(STREAM_D, 10, true);

        // Write up to 10 bytes.
        Assert.True(Write(10));

        VerifyWrite(STREAM_A, 5);
        VerifyNeverWrite(STREAM_B);
        VerifyWrite(STREAM_C, 5);
        VerifyNeverWrite(STREAM_D);

        Assert.False(Write(15));
        VerifyAnyWrite(STREAM_A, 1);
        VerifyNeverWrite(STREAM_B);
        VerifyWrite(Times.Exactly(2), STREAM_C, 5);
        VerifyWrite(STREAM_D, 10);
    }

    /**
     * In this test, we verify re-prioritizing a stream. We start out with B blocked:
     *
     * <pre>
     *         0
     *        / \
     *       A  [B]
     *      / \
     *     C   D
     * </pre>
     *
     * We then re-prioritize D so that it's directly off of the connection and verify that A and D split the written
     * bytes between them.
     *
     * <pre>
     *           0
     *          /|\
     *        /  |  \
     *       A  [B]  D
     *      /
     *     C
     * </pre>
     */
    [Fact]
    public void ReprioritizeShouldAdjustOutboundFlow()
    {
        // B cannot stream.
        InitState(STREAM_A, 10, true);
        InitState(STREAM_C, 10, true);
        InitState(STREAM_D, 10, true);

        // Re-prioritize D as a direct child of the connection.
        SetPriority(STREAM_D, 0, DEFAULT_PRIORITY_WEIGHT, false);

        Assert.True(Write(10));

        VerifyWrite(STREAM_A, 10);
        VerifyNeverWrite(STREAM_B);
        VerifyNeverWrite(STREAM_C);
        VerifyWrite(Times.AtMost(1), STREAM_D, 0);

        Assert.False(Write(20));
        VerifyAnyWrite(STREAM_A, 1);
        VerifyNeverWrite(STREAM_B);
        VerifyWrite(STREAM_C, 10);
        VerifyWrite(STREAM_D, 10);
    }

    /**
     * Test that the maximum allowed amount the flow controller allows to be sent is always fully allocated if
     * the streams have at least this much data to send. See https://github.com/netty/netty/issues/4266.
     * <pre>
     *            0
     *          / | \
     *        /   |   \
     *      A(0) B(0) C(0)
     *     /
     *    D(> allowed to send in 1 allocation attempt)
     * </pre>
     */
    [Fact]
    public void UnstreamableParentsShouldFeedHungryChildren()
    {
        // Setup the priority tree.
        SetPriority(STREAM_A, 0, (short) 32, false);
        SetPriority(STREAM_B, 0, (short) 16, false);
        SetPriority(STREAM_C, 0, (short) 16, false);
        SetPriority(STREAM_D, STREAM_A, (short) 16, false);

        int writableBytes = 100;

        // Send enough so it can not be completely written out
        int expectedUnsentAmount = 1;
        InitState(STREAM_D, writableBytes + expectedUnsentAmount, true);

        Assert.True(Write(writableBytes));
        VerifyWrite(STREAM_D, writableBytes);

        Assert.False(Write(expectedUnsentAmount));
        VerifyWrite(STREAM_D, expectedUnsentAmount);
    }

    /**
     * In this test, we root all streams at the connection, and then verify that data is split appropriately based on
     * weight (all available data is the same).
     *
     * <pre>
     *           0
     *        / / \ \
     *       A B   C D
     * </pre>
     */
    [Fact]
    public void WriteShouldPreferHighestWeight()
    {
        // Root the streams at the connection and assign weights.
        SetPriority(STREAM_A, 0, (short) 50, false);
        SetPriority(STREAM_B, 0, (short) 200, false);
        SetPriority(STREAM_C, 0, (short) 100, false);
        SetPriority(STREAM_D, 0, (short) 100, false);

        InitState(STREAM_A, 1000, true);
        InitState(STREAM_B, 1000, true);
        InitState(STREAM_C, 1000, true);
        InitState(STREAM_D, 1000, true);

        // Set allocation quantum to 1 so it is easier to see the ratio of total bytes written between each stream.
        distributor.AllocationQuantum(1);
        Assert.True(Write(1000));

        Assert.Equal(100, CaptureWrites(STREAM_A));
        Assert.Equal(450, CaptureWrites(STREAM_B));
        Assert.Equal(225, CaptureWrites(STREAM_C));
        Assert.Equal(225, CaptureWrites(STREAM_D));
    }

    /**
     * In this test, we root all streams at the connection, block streams C and D, and then verify that data is
     * prioritized toward stream B which has a higher weight than stream A.
     * <p>
     * We also verify that the amount that is written is not uniform, and not always the allocation quantum.
     *
     * <pre>
     *            0
     *        / /  \  \
     *       A B   [C] [D]
     * </pre>
     */
    [Fact]
    public void WriteShouldFavorPriority()
    {
        // Root the streams at the connection and assign weights.
        SetPriority(STREAM_A, 0, (short) 50, false);
        SetPriority(STREAM_B, 0, (short) 200, false);
        SetPriority(STREAM_C, 0, (short) 100, false);
        SetPriority(STREAM_D, 0, (short) 100, false);

        InitState(STREAM_A, 1000, true);
        InitState(STREAM_B, 1000, true);
        InitState(STREAM_C, 1000, false);
        InitState(STREAM_D, 1000, false);

        // Set allocation quantum to 1 so it is easier to see the ratio of total bytes written between each stream.
        distributor.AllocationQuantum(1);

        Assert.True(Write(100));
        Assert.Equal(20, CaptureWrites(STREAM_A));
        VerifyWrite(Times.Exactly(20), STREAM_A, 1);
        Assert.Equal(80, CaptureWrites(STREAM_B));
        VerifyWrite(Times.Exactly(0), STREAM_B, 1);
        VerifyNeverWrite(STREAM_C);
        VerifyNeverWrite(STREAM_D);

        Assert.True(Write(100));
        Assert.Equal(40, CaptureWrites(STREAM_A));
        VerifyWrite(Times.Exactly(40), STREAM_A, 1);
        Assert.Equal(160, CaptureWrites(STREAM_B));
        VerifyWrite(Times.AtMost(1), STREAM_B, 1);
        VerifyNeverWrite(STREAM_C);
        VerifyNeverWrite(STREAM_D);

        Assert.True(Write(1050));
        Assert.Equal(250, CaptureWrites(STREAM_A));
        VerifyWrite(Times.Exactly(250), STREAM_A, 1);
        Assert.Equal(1000, CaptureWrites(STREAM_B));
        VerifyWrite(Times.AtMost(2), STREAM_B, 1);
        VerifyNeverWrite(STREAM_C);
        VerifyNeverWrite(STREAM_D);

        Assert.False(Write(750));
        Assert.Equal(1000, CaptureWrites(STREAM_A));
        VerifyWrite(Times.Exactly(1), STREAM_A, 750);
        Assert.Equal(1000, CaptureWrites(STREAM_B));
        VerifyWrite(Times.Exactly(0), STREAM_B, 0);
        VerifyNeverWrite(STREAM_C);
        VerifyNeverWrite(STREAM_D);
    }

    /**
     * In this test, we root all streams at the connection, and then verify that data is split equally among the stream,
     * since they all have the same weight.
     *
     * <pre>
     *           0
     *        / / \ \
     *       A B   C D
     * </pre>
     */
    [Fact]
    public void SamePriorityShouldDistributeBasedOnData()
    {
        // Root the streams at the connection with the same weights.
        SetPriority(STREAM_A, 0, DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(STREAM_B, 0, DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(STREAM_C, 0, DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(STREAM_D, 0, DEFAULT_PRIORITY_WEIGHT, false);

        InitState(STREAM_A, 400, true);
        InitState(STREAM_B, 500, true);
        InitState(STREAM_C, 0, true);
        InitState(STREAM_D, 700, true);

        // Set allocation quantum to 1 so it is easier to see the ratio of total bytes written between each stream.
        distributor.AllocationQuantum(1);
        Assert.True(Write(999));

        Assert.Equal(333, CaptureWrites(STREAM_A));
        Assert.Equal(333, CaptureWrites(STREAM_B));
        VerifyWrite(Times.Exactly(1), STREAM_C, 0);
        Assert.Equal(333, CaptureWrites(STREAM_D));
    }

    /**
     * In this test, we call distribute with 0 bytes and verify that all streams with 0 bytes are written.
     *
     * <pre>
     *         0
     *        / \
     *       A   B
     *      / \
     *     C   D
     * </pre>
     *
     * After the tree shift:
     *
     * <pre>
     *         0
     *         |
     *        [A]
     *         |
     *         B
     *        / \
     *       C   D
     * </pre>
     */
    [Fact]
    public void ZeroDistributeShouldWriteAllZeroFrames()
    {
        InitState(STREAM_A, 400, false);
        InitState(STREAM_B, 0, true);
        InitState(STREAM_C, 0, true);
        InitState(STREAM_D, 0, true);

        SetPriority(STREAM_B, STREAM_A, DEFAULT_PRIORITY_WEIGHT, true);

        Assert.False(Write(0));
        VerifyNeverWrite(STREAM_A);
        VerifyWrite(STREAM_B, 0);
        VerifyAnyWrite(STREAM_B, 1);
        VerifyWrite(STREAM_C, 0);
        VerifyAnyWrite(STREAM_C, 1);
        VerifyWrite(STREAM_D, 0);
        VerifyAnyWrite(STREAM_D, 1);
    }

    /**
     * In this test, we call distribute with 100 bytes which is the total amount eligible to be written, and also have
     * streams with 0 bytes to write. All of these streams should be written with a single call to distribute.
     *
     * <pre>
     *         0
     *        / \
     *       A   B
     *      / \
     *     C   D
     * </pre>
     *
     * After the tree shift:
     *
     * <pre>
     *         0
     *         |
     *        [A]
     *         |
     *         B
     *        / \
     *       C   D
     * </pre>
     */
    [Fact]
    public void NonZeroDistributeShouldWriteAllZeroFramesIfAllEligibleDataIsWritten()
    {
        InitState(STREAM_A, 400, false);
        InitState(STREAM_B, 100, true);
        InitState(STREAM_C, 0, true);
        InitState(STREAM_D, 0, true);

        SetPriority(STREAM_B, STREAM_A, DEFAULT_PRIORITY_WEIGHT, true);

        Assert.False(Write(100));
        VerifyNeverWrite(STREAM_A);
        VerifyWrite(STREAM_B, 100);
        VerifyAnyWrite(STREAM_B, 1);
        VerifyWrite(STREAM_C, 0);
        VerifyAnyWrite(STREAM_C, 1);
        VerifyWrite(STREAM_D, 0);
        VerifyAnyWrite(STREAM_D, 1);
    }

    /**
     * In this test, we shift the priority tree and verify priority bytes for each subtree are correct
     *
     * <pre>
     *         0
     *        / \
     *       A   B
     *      / \
     *     C   D
     * </pre>
     *
     * After the tree shift:
     *
     * <pre>
     *         0
     *         |
     *         A
     *         |
     *         B
     *        / \
     *       C   D
     * </pre>
     */
    [Fact]
    public void BytesDistributedWithRestructureShouldBeCorrect()
    {
        InitState(STREAM_A, 400, true);
        InitState(STREAM_B, 500, true);
        InitState(STREAM_C, 600, true);
        InitState(STREAM_D, 700, true);

        SetPriority(STREAM_B, STREAM_A, DEFAULT_PRIORITY_WEIGHT, true);

        Assert.True(Write(500));
        Assert.Equal(400, CaptureWrites(STREAM_A));
        VerifyWrite(STREAM_B, 100);
        VerifyNeverWrite(STREAM_C);
        VerifyNeverWrite(STREAM_D);

        Assert.True(Write(400));
        Assert.Equal(400, CaptureWrites(STREAM_A));
        Assert.Equal(500, CaptureWrites(STREAM_B));
        VerifyWrite(Times.AtMost(1), STREAM_C, 0);
        VerifyWrite(Times.AtMost(1), STREAM_D, 0);

        Assert.False(Write(1300));
        Assert.Equal(400, CaptureWrites(STREAM_A));
        Assert.Equal(500, CaptureWrites(STREAM_B));
        Assert.Equal(600, CaptureWrites(STREAM_C));
        Assert.Equal(700, CaptureWrites(STREAM_D));
    }

    /**
     * In this test, we add a node to the priority tree and verify
     *
     * <pre>
     *         0
     *        / \
     *       A   B
     *      / \
     *     C   D
     * </pre>
     *
     * After the tree shift:
     *
     * <pre>
     *         0
     *        / \
     *       A   B
     *       |
     *       E
     *      / \
     *     C   D
     * </pre>
     */
    [Fact]
    public void BytesDistributedWithAdditionShouldBeCorrect()
    {
        IHttp2Stream streamE = connection.Local().CreateStream(STREAM_E, false);
        SetPriority(streamE.Id(), STREAM_A, DEFAULT_PRIORITY_WEIGHT, true);

        // Send a bunch of data on each stream.
        InitState(STREAM_A, 400, true);
        InitState(STREAM_B, 500, true);
        InitState(STREAM_C, 600, true);
        InitState(STREAM_D, 700, true);
        InitState(STREAM_E, 900, true);

        Assert.True(Write(900));
        Assert.Equal(400, CaptureWrites(STREAM_A));
        Assert.Equal(500, CaptureWrites(STREAM_B));
        VerifyNeverWrite(STREAM_C);
        VerifyNeverWrite(STREAM_D);
        VerifyWrite(Times.AtMost(1), STREAM_E, 0);

        Assert.True(Write(900));
        Assert.Equal(400, CaptureWrites(STREAM_A));
        Assert.Equal(500, CaptureWrites(STREAM_B));
        VerifyWrite(Times.AtMost(1), STREAM_C, 0);
        VerifyWrite(Times.AtMost(1), STREAM_D, 0);
        Assert.Equal(900, CaptureWrites(STREAM_E));

        Assert.False(Write(1301));
        Assert.Equal(400, CaptureWrites(STREAM_A));
        Assert.Equal(500, CaptureWrites(STREAM_B));
        Assert.Equal(600, CaptureWrites(STREAM_C));
        Assert.Equal(700, CaptureWrites(STREAM_D));
        Assert.Equal(900, CaptureWrites(STREAM_E));
    }

    /**
     * In this test, we close an internal stream in the priority tree.
     *
     * <pre>
     *         0
     *        / \
     *       A   B
     *      / \
     *     C   D
     * </pre>
     *
     * After the close:
     * <pre>
     *          0
     *        / | \
     *       C  D  B
     * </pre>
     */
    [Fact]
    public void BytesDistributedShouldBeCorrectWithInternalStreamClose()
    {
        InitState(STREAM_A, 400, true);
        InitState(STREAM_B, 500, true);
        InitState(STREAM_C, 600, true);
        InitState(STREAM_D, 700, true);

        Stream(STREAM_A).Close();

        Assert.True(Write(500));
        VerifyNeverWrite(STREAM_A);
        Assert.Equal(500, CaptureWrites(STREAM_B) + CaptureWrites(STREAM_C) + CaptureWrites(STREAM_D));

        Assert.False(Write(1300));
        VerifyNeverWrite(STREAM_A);
        Assert.Equal(500, CaptureWrites(STREAM_B));
        Assert.Equal(600, CaptureWrites(STREAM_C));
        Assert.Equal(700, CaptureWrites(STREAM_D));
    }

    /**
     * In this test, we close a leaf stream in the priority tree and verify distribution.
     *
     * <pre>
     *         0
     *        / \
     *       A   B
     *      / \
     *     C   D
     * </pre>
     *
     * After the close:
     * <pre>
     *         0
     *        / \
     *       A   B
     *       |
     *       D
     * </pre>
     */
    [Fact]
    public void BytesDistributedShouldBeCorrectWithLeafStreamClose()
    {
        InitState(STREAM_A, 400, true);
        InitState(STREAM_B, 500, true);
        InitState(STREAM_C, 600, true);
        InitState(STREAM_D, 700, true);

        Stream(STREAM_C).Close();

        Assert.True(Write(900));
        Assert.Equal(400, CaptureWrites(STREAM_A));
        Assert.Equal(500, CaptureWrites(STREAM_B));
        VerifyNeverWrite(STREAM_C);
        VerifyWrite(Times.AtMost(1), STREAM_D, 0);

        Assert.False(Write(700));
        Assert.Equal(400, CaptureWrites(STREAM_A));
        Assert.Equal(500, CaptureWrites(STREAM_B));
        VerifyNeverWrite(STREAM_C);
        Assert.Equal(700, CaptureWrites(STREAM_D));
    }

    [Fact]
    public void ActiveStreamDependentOnNewNonActiveStreamGetsQuantum()
    {
        Setup(0);
        InitState(STREAM_D, 700, true);
        SetPriority(STREAM_D, STREAM_E, DEFAULT_PRIORITY_WEIGHT, true);

        Assert.False(Write(700));
        Assert.Equal(700, CaptureWrites(STREAM_D));
    }

    [Fact]
    public void StreamWindowLargerThanIntDoesNotInfiniteLoop()
    {
        InitState(STREAM_A, int.MaxValue + 1L, true, true);
        Assert.True(Write(int.MaxValue));
        VerifyWrite(STREAM_A, int.MaxValue);
        Assert.False(Write(1));
        VerifyWrite(STREAM_A, 1);
    }

    private bool Write(int numBytes)
    {
        return distributor.Distribute(numBytes, writer.Object);
    }

    private void VerifyWrite(int streamId, int numBytes)
    {
        IHttp2Stream stream = Stream(streamId);
        writer.Verify(w => w.Write(It.Is<IHttp2Stream>(s => ReferenceEquals(s, stream)), numBytes), Times.Once());
    }

    private void VerifyWrite(Times mode, int streamId, int numBytes)
    {
        IHttp2Stream stream = Stream(streamId);
        writer.Verify(w => w.Write(It.Is<IHttp2Stream>(s => ReferenceEquals(s, stream)), numBytes), mode);
    }

    private void VerifyAnyWrite(int streamId, int times)
    {
        IHttp2Stream stream = Stream(streamId);
        writer.Verify(w => w.Write(It.Is<IHttp2Stream>(s => ReferenceEquals(s, stream)), It.IsAny<int>()),
            Times.Exactly(times));
    }

    private void VerifyNeverWrite(int streamId)
    {
        VerifyNeverWrite(Stream(streamId));
    }

    private void VerifyNeverWrite(IHttp2Stream stream)
    {
        writer.Verify(w => w.Write(It.Is<IHttp2Stream>(s => ReferenceEquals(s, stream)), It.IsAny<int>()),
            Times.Never());
    }

    private int CaptureWrites(int streamId)
    {
        return CaptureWrites(Stream(streamId));
    }

    private int CaptureWrites(IHttp2Stream stream)
    {
        writer.Verify(w => w.Write(It.Is<IHttp2Stream>(s => ReferenceEquals(s, stream)), It.IsAny<int>()),
            Times.AtLeastOnce());
        int total = 0;
        foreach (int x in writer.Invocations.Where(i => i.Method.Name == nameof(IStreamByteDistributor.IWriter.Write) &&
                                                       ReferenceEquals(i.Arguments[0], stream))
                                            .Select(i => (int) i.Arguments[1]))
        {
            total += x;
        }
        return total;
    }
}
