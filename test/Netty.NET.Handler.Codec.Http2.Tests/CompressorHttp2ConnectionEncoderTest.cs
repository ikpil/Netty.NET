/*
 * Copyright 2026 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed
 * on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for
 * the specific language governing permissions and limitations under the License.
 */

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.HttpHeaderNames;
using static Netty.NET.Handler.Codec.Http.HttpHeaderValues;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: @ParameterizedTest @ValueSource(booleans = { false, true }) is a [Theory] with two InlineData rows. Mockito's
// mock/when/doThrow are Moq Setup(...).Returns/Throws (https://github.com/devlooped/moq/wiki/Quickstart); eq(x) is the
// value itself and any(Http2Headers.class) It.IsAny<IHttp2Headers>(). ChannelPromise is TaskCompletionSource
// (docs/codec-http-porting.md, Shared CLR adaptations): new DefaultChannelPromise(mock(Channel.class),
// ImmediateEventExecutor.INSTANCE) is a new TaskCompletionSource, isDone()/isSuccess() are Task.IsCompleted/
// IsCompletedSuccessfully, setFailure is SetException and cause() is Http2CodecUtil.Cause. The Java promise notifies its
// listeners on ImmediateEventExecutor; the encoder attaches its listeners with Http2CodecUtil.AddListener on
// ctx.Executor, so the mocked context returns ImmediateEventExecutor.INSTANCE (as DefaultHttp2ConnectionEncoderTest).
// TestEmbeddedChannel overrides EmbeddedChannel.FinishAndReleaseAll(), which the Transport port declares virtual as
// Java's public non-final finishAndReleaseAll() (src/Netty.NET.Transport/Channels/Embedded/EmbeddedChannel.cs).
// RuntimeException is Exception.
public class CompressorHttp2ConnectionEncoderTest
{
    private const int STREAM_ID = 3;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompressorIsClosedWhenHeadersWriteDoesNotCreateStream(bool hasPriority)
    {
        IHttp2Connection connection = new DefaultHttp2Connection(false);
        DefaultHttp2ConnectionEncoder @delegate = NewDelegate(connection, new Mock<IHttp2FrameWriter>().Object);
        TestCompressorHttp2ConnectionEncoder encoder = new TestCompressorHttp2ConnectionEncoder(@delegate);
        IChannelHandlerContext ctx = MockContext();
        TaskCompletionSource promise = NewPromise();

        WriteHeaders(encoder, ctx, 2, promise, hasPriority);

        Assert.True(promise.Task.IsCompleted);
        Assert.False(promise.Task.IsCompletedSuccessfully);
        Assert.Null(connection.Stream(2));
        AssertClosed(encoder.compressor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompressorIsClosedWhenDelegateThrows(bool hasPriority)
    {
        Exception cause = new Exception("write failed");
        IHttp2Connection connection = new DefaultHttp2Connection(false);
        Mock<IHttp2ConnectionEncoder> @delegate = new Mock<IHttp2ConnectionEncoder>();
        @delegate.Setup(d => d.Connection()).Returns(connection);
        IChannelHandlerContext ctx = MockContext();
        TaskCompletionSource promise = NewPromise();
        FailHeadersWrite(@delegate, ctx, promise, hasPriority, cause);
        TestCompressorHttp2ConnectionEncoder encoder = new TestCompressorHttp2ConnectionEncoder(@delegate.Object);

        WriteHeaders(encoder, ctx, STREAM_ID, promise, hasPriority);

        Assert.Same(cause, Http2CodecUtil.Cause(promise.Task));
        AssertClosed(encoder.compressor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StreamOwnsCompressorWhenHeadersWriteFailsAsynchronously(bool hasPriority)
    {
        Exception cause = new Exception("write failed");
        IHttp2Connection connection = new DefaultHttp2Connection(false);
        Mock<IHttp2FrameWriter> writer = new Mock<IHttp2FrameWriter>();
        DefaultHttp2ConnectionEncoder @delegate = NewDelegate(connection, writer.Object);
        IChannelHandlerContext ctx = MockContext();
        TaskCompletionSource promise = NewPromise();
        ReturnHeadersPromise(writer, ctx, promise, hasPriority);
        TestCompressorHttp2ConnectionEncoder encoder = new TestCompressorHttp2ConnectionEncoder(@delegate);

        WriteHeaders(encoder, ctx, STREAM_ID, promise, hasPriority);

        IHttp2Stream stream = connection.Stream(STREAM_ID);
        Assert.False(promise.Task.IsCompleted);
        Assert.True(encoder.compressor.IsOpen);

        promise.SetException(cause);
        Assert.True(encoder.compressor.IsOpen);

        stream.Close();
        AssertClosed(encoder.compressor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingStreamOwnsCompressorWhenHeadersWriteFails(bool hasPriority)
    {
        IHttp2Connection connection = new DefaultHttp2Connection(false);
        IHttp2Stream stream = connection.Local().CreateStream(STREAM_ID, true);
        DefaultHttp2ConnectionEncoder @delegate = NewDelegate(connection, new Mock<IHttp2FrameWriter>().Object);
        TestCompressorHttp2ConnectionEncoder encoder = new TestCompressorHttp2ConnectionEncoder(@delegate);
        IChannelHandlerContext ctx = MockContext();
        TaskCompletionSource promise = NewPromise();

        WriteHeaders(encoder, ctx, STREAM_ID, promise, hasPriority);

        Assert.True(promise.Task.IsCompleted);
        Assert.False(promise.Task.IsCompletedSuccessfully);
        Assert.True(encoder.compressor.IsOpen);

        stream.Close();
        AssertClosed(encoder.compressor);
    }

    [Fact]
    public void CompressorIsClosedWhenTargetContentEncodingFails()
    {
        Exception cause = new Exception("encoding failed");
        Mock<IHttp2ConnectionEncoder> @delegate = new Mock<IHttp2ConnectionEncoder>();
        @delegate.Setup(d => d.Connection()).Returns(new DefaultHttp2Connection(false));
        TestCompressorHttp2ConnectionEncoder encoder = new TestCompressorHttp2ConnectionEncoder(@delegate.Object);
        encoder.targetContentEncodingFailure = cause;
        TaskCompletionSource promise = NewPromise();

        WriteHeaders(encoder, MockContext(), STREAM_ID, promise, false);

        Assert.Same(cause, Http2CodecUtil.Cause(promise.Task));
        AssertClosed(encoder.compressor);
    }

    private static void WriteHeaders(IHttp2ConnectionEncoder encoder, IChannelHandlerContext ctx, int streamId,
                                     TaskCompletionSource promise, bool hasPriority)
    {
        IHttp2Headers headers = new DefaultHttp2Headers().Set(CONTENT_ENCODING, GZIP);
        if (hasPriority)
        {
            _ = encoder.WriteHeadersAsync(ctx, streamId, headers, 0, (short) 16, false, 0, false, promise);
        }
        else
        {
            _ = encoder.WriteHeadersAsync(ctx, streamId, headers, 0, false, promise);
        }
    }

    private static void FailHeadersWrite(Mock<IHttp2ConnectionEncoder> @delegate, IChannelHandlerContext ctx,
                                         TaskCompletionSource promise, bool hasPriority, Exception cause)
    {
        if (hasPriority)
        {
            @delegate.Setup(d => d.WriteHeadersAsync(ctx, STREAM_ID, It.IsAny<IHttp2Headers>(), 0,
                    (short) 16, false, 0, false, promise)).Throws(cause);
        }
        else
        {
            @delegate.Setup(d => d.WriteHeadersAsync(ctx, STREAM_ID, It.IsAny<IHttp2Headers>(), 0,
                    false, promise)).Throws(cause);
        }
    }

    private static void ReturnHeadersPromise(Mock<IHttp2FrameWriter> writer, IChannelHandlerContext ctx,
                                             TaskCompletionSource promise, bool hasPriority)
    {
        if (hasPriority)
        {
            writer.Setup(w => w.WriteHeadersAsync(ctx, STREAM_ID, It.IsAny<IHttp2Headers>(), 0, (short) 16,
                    false, 0, false, promise)).Returns(promise.Task);
        }
        else
        {
            writer.Setup(w => w.WriteHeadersAsync(ctx, STREAM_ID, It.IsAny<IHttp2Headers>(), 0, false, promise))
                    .Returns(promise.Task);
        }
    }

    private static TaskCompletionSource NewPromise()
    {
        return new TaskCompletionSource();
    }

    // CLR: mock(ChannelHandlerContext.class) with the executor of the Java promises (see the class comment).
    private static IChannelHandlerContext MockContext()
    {
        Mock<IChannelHandlerContext> ctx = new Mock<IChannelHandlerContext>();
        ctx.Setup(c => c.Executor).Returns(ImmediateEventExecutor.INSTANCE);
        return ctx.Object;
    }

    private static DefaultHttp2ConnectionEncoder NewDelegate(IHttp2Connection connection, IHttp2FrameWriter writer)
    {
        connection.Remote().FlowController(new Mock<IHttp2RemoteFlowController>().Object);
        DefaultHttp2ConnectionEncoder @delegate = new DefaultHttp2ConnectionEncoder(connection, writer);
        @delegate.LifecycleManager(new Mock<IHttp2LifecycleManager>().Object);
        return @delegate;
    }

    private static void AssertClosed(TestEmbeddedChannel compressor)
    {
        Assert.False(compressor.IsOpen);
        Assert.Equal(1, compressor.cleanupCalls);
    }

    private sealed class TestCompressorHttp2ConnectionEncoder : CompressorHttp2ConnectionEncoder
    {
        internal TestEmbeddedChannel compressor;
        internal Exception targetContentEncodingFailure;

        internal TestCompressorHttp2ConnectionEncoder(IHttp2ConnectionEncoder @delegate) : base(@delegate)
        {
        }

        protected override EmbeddedChannel NewContentCompressor(IChannelHandlerContext ctx,
                                                                ICharSequence contentEncoding)
        {
            compressor = new TestEmbeddedChannel();
            return compressor;
        }

        protected override ICharSequence GetTargetContentEncoding(ICharSequence contentEncoding)
        {
            if (targetContentEncodingFailure != null)
            {
                throw targetContentEncodingFailure;
            }
            return contentEncoding;
        }
    }

    private sealed class TestEmbeddedChannel : EmbeddedChannel
    {
        internal int cleanupCalls;

        public override bool FinishAndReleaseAll()
        {
            ++cleanupCalls;
            return base.FinishAndReleaseAll();
        }
    }
}
