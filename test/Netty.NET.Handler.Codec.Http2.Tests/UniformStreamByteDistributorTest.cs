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

using System.Collections.Generic;
using System.Linq;
using Moq;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using TestStreamByteDistributorStreamState = Netty.NET.Handler.Codec.Http2.Tests.Http2TestUtil.TestStreamByteDistributorStreamState;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Tests for {@link UniformStreamByteDistributor}.
 */
// CLR: IntObjectMap/IntObjectHashMap are Dictionary<int,...>. The @Mock writer is a Moq
// Mock<IStreamByteDistributor.IWriter> created in the constructor (@BeforeEach).
// doAnswer(writeAnswer()).when(writer).write(any, anyInt) is a Write setup whose callback updates the stream
// state; doThrow(...)/doNothing() for write(same(stream C), eq(3)) are later setups for those arguments, which Moq
// matches before the earlier, more general one ("the last matching setup wins", Moq Quickstart,
// https://github.com/devlooped/moq/wiki/Quickstart). same(stream) is It.Is with ReferenceEquals; verify(writer) is
// Verify(..., Times.Once()), atMost(1) is Times.AtMost(1), verifyNoMoreInteractions is VerifyNoOtherCalls and
// reset(writer) is Mock.Reset() (setups and invocations, as Mockito's reset). The ArgumentCaptor of captureWrite
// is the int argument of the one verified Write invocation for the stream (Mock.Invocations).
// assertThrows(T.class,...) is Assert.ThrowsAny<T> and the anonymous Executable is a lambda.
public class UniformStreamByteDistributorTest
{
    private const int CHUNK_SIZE = DEFAULT_MIN_ALLOCATION_CHUNK;

    private const int STREAM_A = 1;
    private const int STREAM_B = 3;
    private const int STREAM_C = 5;
    private const int STREAM_D = 7;

    private IHttp2Connection connection;
    private UniformStreamByteDistributor distributor;
    private Dictionary<int, TestStreamByteDistributorStreamState> stateMap;

    private readonly Mock<IStreamByteDistributor.IWriter> writer = new Mock<IStreamByteDistributor.IWriter>();

    public UniformStreamByteDistributorTest()
    {
        stateMap = new Dictionary<int, TestStreamByteDistributorStreamState>();
        connection = new DefaultHttp2Connection(false);
        distributor = new UniformStreamByteDistributor(connection);

        // Assume we always write all the allocated bytes.
        ResetWriter();

        connection.Local().CreateStream(STREAM_A, false);
        connection.Local().CreateStream(STREAM_B, false);
        IHttp2Stream streamC = connection.Local().CreateStream(STREAM_C, false);
        IHttp2Stream streamD = connection.Local().CreateStream(STREAM_D, false);
        SetPriority(streamC.Id(), STREAM_A, DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamD.Id(), STREAM_A, DEFAULT_PRIORITY_WEIGHT, false);
    }

    private void WriteAnswer(IHttp2Stream stream, int numBytes)
    {
        TestStreamByteDistributorStreamState state = stateMap[stream.Id()];
        state.pendingBytes -= numBytes;
        state.hasFrame = state.pendingBytes > 0;
        distributor.UpdateStreamableBytes(state);
    }

    private void ResetWriter()
    {
        writer.Reset();
        writer.Setup(w => w.Write(It.IsAny<IHttp2Stream>(), It.IsAny<int>()))
            .Callback<IHttp2Stream, int>(WriteAnswer);
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
        writer.VerifyNoOtherCalls();

        Assert.False(Write(10));
        writer.VerifyNoOtherCalls();
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

        writer.Setup(w => w.Write(It.Is<IHttp2Stream>(s => ReferenceEquals(s, streamC)), 3));
        Write(10);
        VerifyWrite(STREAM_A, 1);
        VerifyWrite(STREAM_B, 2);
        VerifyWrite(STREAM_C, 3);
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
        InitState(STREAM_A, CHUNK_SIZE, true);
        InitState(STREAM_B, CHUNK_SIZE, true);
        InitState(STREAM_C, CHUNK_SIZE, true);
        InitState(STREAM_D, CHUNK_SIZE, true);

        // Only write 3 * chunkSize, so that we'll only write to the first 3 streams.
        int written = 3 * CHUNK_SIZE;
        Assert.True(Write(written));
        Assert.Equal(CHUNK_SIZE, CaptureWrite(STREAM_A));
        Assert.Equal(CHUNK_SIZE, CaptureWrite(STREAM_B));
        Assert.Equal(CHUNK_SIZE, CaptureWrite(STREAM_C));
        writer.VerifyNoOtherCalls();

        ResetWriter();

        // Now write again and verify that the last stream is written to.
        Assert.False(Write(CHUNK_SIZE));
        Assert.Equal(CHUNK_SIZE, CaptureWrite(STREAM_D));
        writer.VerifyNoOtherCalls();
    }

    [Fact]
    public void StreamWithMoreDataShouldBeEnqueuedAfterWrite()
    {
        // Give the stream a bunch of data.
        InitState(STREAM_A, 2 * CHUNK_SIZE, true);

        // Write only part of the data.
        Assert.True(Write(CHUNK_SIZE));
        Assert.Equal(CHUNK_SIZE, CaptureWrite(STREAM_A));
        writer.VerifyNoOtherCalls();

        ResetWriter();

        // Now write the rest of the data.
        Assert.False(Write(CHUNK_SIZE));
        Assert.Equal(CHUNK_SIZE, CaptureWrite(STREAM_A));
        writer.VerifyNoOtherCalls();
    }

    [Fact]
    public void EmptyFrameAtHeadIsWritten()
    {
        InitState(STREAM_A, 10, true);
        InitState(STREAM_B, 0, true);
        InitState(STREAM_C, 0, true);
        InitState(STREAM_D, 10, true);

        Assert.True(Write(10));
        VerifyWrite(STREAM_A, 10);
        VerifyWrite(STREAM_B, 0);
        VerifyWrite(STREAM_C, 0);
        writer.VerifyNoOtherCalls();
    }

    [Fact]
    public void StreamWindowExhaustedDoesNotWrite()
    {
        InitState(STREAM_A, 0, true, false);
        InitState(STREAM_B, 0, true);
        InitState(STREAM_C, 0, true);
        InitState(STREAM_D, 0, true, false);

        Assert.False(Write(10));
        VerifyWrite(STREAM_B, 0);
        VerifyWrite(STREAM_C, 0);
        writer.VerifyNoOtherCalls();
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

    private IHttp2Stream Stream(int streamId)
    {
        return connection.Stream(streamId);
    }

    private void InitState(int streamId, long streamableBytes, bool hasFrame)
    {
        InitState(streamId, streamableBytes, hasFrame, hasFrame);
    }

    private void InitState(int streamId, long pendingBytes, bool hasFrame,
            bool isWriteAllowed)
    {
        IHttp2Stream stream = Stream(streamId);
        TestStreamByteDistributorStreamState state = new TestStreamByteDistributorStreamState(stream, pendingBytes,
                hasFrame, isWriteAllowed);
        stateMap[streamId] = state;
        distributor.UpdateStreamableBytes(state);
    }

    private void SetPriority(int streamId, int parent, int weight, bool exclusive)
    {
        distributor.UpdateDependencyTree(streamId, parent, (short) weight, exclusive);
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

    private int CaptureWrite(int streamId)
    {
        IHttp2Stream stream = Stream(streamId);
        writer.Verify(w => w.Write(It.Is<IHttp2Stream>(s => ReferenceEquals(s, stream)), It.IsAny<int>()),
            Times.Once());
        return (int) writer.Invocations.Last(i => i.Method.Name == nameof(IStreamByteDistributor.IWriter.Write) &&
                                                ReferenceEquals(i.Arguments[0], stream)).Arguments[1];
    }
}
