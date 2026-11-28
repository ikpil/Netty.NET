/*
 * Copyright 2019 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
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
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Tests for {@link Http2MaxRstFrameLimitEncoder}.
 */
// CLR: @Mock fields and MockitoAnnotations.initMocks are Moq mocks created in the constructor, which runs the
// @BeforeEach setup(); @AfterEach teardown() is Dispose() (https://xunit.net/docs/shared-context). when/thenAnswer/
// verify/atLeast/times are Moq Setup(...).Returns, Verify(..., Times.AtLeast(n)/Times.Exactly(n))
// (https://github.com/devlooped/moq/wiki/Quickstart); the answers receive the promise argument directly, so
// handlePromise takes the TaskCompletionSource where Java takes the InvocationOnMock and the argument index.
// ChannelPromise is TaskCompletionSource (newPromise() is a new TaskCompletionSource; setSuccess() is SetResult()) and
// ChannelFuture is Task, whose IsCompletedSuccessfully is isSuccess() (docs/codec-http-porting.md, Shared CLR
// adaptations). ctx.close() is CloseAsync(CancellationToken) and channel.unsafe() and the ctx.newPromise() stub have no
// counterpart. MockTicker is the Common MockTicker and ticker.advance(1, SECONDS) is Advance(TimeSpan.FromSeconds(1)).
// @EnumSource(Http2Error.class) is a [Theory] over MemberData with every Http2Error instance (Http2Error.ValueOf over
// the codes 0x0-0xD, the declared order of the Java enum): Http2Error is an enum class (sealed class with static
// readonly instances) without values().
public class Http2MaxRstFrameLimitEncoderTest : IDisposable
{

    private readonly Http2MaxRstFrameLimitEncoder encoder;

    private readonly Mock<IHttp2FrameWriter> writer;

    private readonly Mock<IChannelHandlerContext> ctxMock;
    private readonly IChannelHandlerContext ctx;

    private readonly Mock<IChannel> channel;

    private readonly Mock<IChannelConfig> config;

    private readonly Mock<IEventExecutor> executor;

    private readonly MockTicker ticker = Ticker.NewMockTicker();

    private readonly Queue<TaskCompletionSource> goAwayPromises = new Queue<TaskCompletionSource>();

    public static IEnumerable<object[]> Http2Errors()
    {
        for (long code = 0; Http2Error.ValueOf(code) != null; code++)
        {
            yield return new object[] { Http2Error.ValueOf(code) };
        }
    }

    /**
     * Init fields and do mocking.
     */
    public Http2MaxRstFrameLimitEncoderTest()
    {
        writer = new Mock<IHttp2FrameWriter>();
        ctxMock = new Mock<IChannelHandlerContext>();
        ctx = ctxMock.Object;
        channel = new Mock<IChannel>();
        config = new Mock<IChannelConfig>();
        executor = new Mock<IEventExecutor>();
        Mock<IHttp2FrameWriter.IConfiguration> configuration = new Mock<IHttp2FrameWriter.IConfiguration>();
        Mock<IHttp2FrameSizePolicy> frameSizePolicy = new Mock<IHttp2FrameSizePolicy>();
        writer.Setup(w => w.Configuration()).Returns(configuration.Object);
        configuration.Setup(c => c.FrameSizePolicy()).Returns(frameSizePolicy.Object);
        frameSizePolicy.Setup(p => p.MaxFrameSize()).Returns(DEFAULT_MAX_FRAME_SIZE);

        writer.Setup(w => w.WriteRstStreamAsync(ctx, It.IsAny<int>(), It.IsAny<long>(), It.IsAny<TaskCompletionSource>()))
                .Returns<IChannelHandlerContext, int, long, TaskCompletionSource>(
                        (c, streamId, errorCode, promise) => HandlePromise(promise));
        writer.Setup(w => w.WriteGoAwayAsync(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(), It.IsAny<long>(),
                        It.IsAny<ByteBuf>(), It.IsAny<TaskCompletionSource>()))
                .Returns<IChannelHandlerContext, int, long, ByteBuf, TaskCompletionSource>(
                        (c, lastStreamId, errorCode, debugData, promise) =>
                {
                    ReferenceCountUtil.Release(debugData);
                    goAwayPromises.Enqueue(promise);
                    return promise.Task;
                });
        IHttp2Connection connection = new DefaultHttp2Connection(false);
        connection.Remote().FlowController(new DefaultHttp2RemoteFlowController(connection));
        connection.Local().FlowController(new DefaultHttp2LocalFlowController(connection).FrameWriter(writer.Object));

        DefaultHttp2ConnectionEncoder defaultEncoder =
                new DefaultHttp2ConnectionEncoder(connection, writer.Object);
        encoder = new Http2MaxRstFrameLimitEncoder(defaultEncoder, 2, 1, ticker);
        DefaultHttp2ConnectionDecoder decoder =
                new DefaultHttp2ConnectionDecoder(connection, encoder, new Mock<IHttp2FrameReader>().Object);
        Http2ConnectionHandler handler = new Http2ConnectionHandlerBuilder()
                .FrameListener(new Mock<IHttp2FrameListener>().Object)
                .Codec(decoder, encoder).Build();

        // Set LifeCycleManager on encoder and decoder
        ctxMock.Setup(c => c.Channel).Returns(channel.Object);
        ctxMock.Setup(c => c.Alloc()).Returns(UnpooledByteBufAllocator.Default);
        channel.Setup(c => c.Alloc()).Returns(UnpooledByteBufAllocator.Default);
        executor.Setup(e => e.InEventLoop()).Returns(true);
        ctxMock.Setup(c => c.Executor).Returns(executor.Object);
        channel.Setup(c => c.IsActive).Returns(false);
        channel.Setup(c => c.Config).Returns(config.Object);
        channel.Setup(c => c.IsWritable).Returns(true);
        channel.Setup(c => c.BytesBeforeUnwritable).Returns(long.MaxValue);
        config.Setup(c => c.GetWriteBufferHighWaterMark()).Returns(int.MaxValue);
        config.Setup(c => c.GetMessageSizeEstimator()).Returns(DefaultMessageSizeEstimator.DEFAULT);
        ChannelMetadata metadata = new ChannelMetadata(false, 16);
        channel.Setup(c => c.Metadata).Returns(metadata);
        handler.HandlerAdded(ctx);
    }

    private static Task HandlePromise(TaskCompletionSource promise)
    {
        promise.SetResult();
        return promise.Task;
    }

    public void Dispose()
    {
        // Close and release any buffered frames.
        encoder.Close();

        // Notify all goAway ChannelPromise instances now as these will also release the retained ByteBuf for the
        // debugData.
        for (;;)
        {
            if (!goAwayPromises.TryDequeue(out TaskCompletionSource promise))
            {
                break;
            }

            promise.SetResult();
        }
    }

    [Theory]
    [MemberData(nameof(Http2Errors))]
    public void TestLimitRst(Http2Error error)
    {
        Assert.True(encoder.WriteRstStreamAsync(ctx, 1, error.Code(), NewPromise()).IsCompletedSuccessfully);
        Assert.True(encoder.WriteRstStreamAsync(ctx, 1, error.Code(), NewPromise()).IsCompletedSuccessfully);
        VerifyFlushAndClose(0, false);
        Assert.True(encoder.WriteRstStreamAsync(ctx, 1, error.Code(), NewPromise()).IsCompletedSuccessfully);

        if (error == CANCEL || error == NO_ERROR)
        {
            // CANCEL and NO_ERROR are ignored as these will not be caused by a stream error.
            VerifyFlushAndClose(0, false);
        }
        else
        {
            VerifyFlushAndClose(1, true);
        }
    }

    [Theory]
    [MemberData(nameof(Http2Errors))]
    public void TestLimitRstReset(Http2Error error)
    {
        Assert.True(encoder.WriteRstStreamAsync(ctx, 1, error.Code(), NewPromise()).IsCompletedSuccessfully);
        Assert.True(encoder.WriteRstStreamAsync(ctx, 1, error.Code(), NewPromise()).IsCompletedSuccessfully);
        VerifyFlushAndClose(0, false);
        ticker.Advance(TimeSpan.FromSeconds(1));
        Assert.True(encoder.WriteRstStreamAsync(ctx, 1, error.Code(), NewPromise()).IsCompletedSuccessfully);
        VerifyFlushAndClose(0, false);
    }

    private void VerifyFlushAndClose(int invocations, bool failed)
    {
        // CLR: Mockito's atLeast(0) holds for any number of calls; Moq's Times.AtLeast requires a count of at least 1,
        // so the flush check runs only for a positive count.
        if (invocations > 0)
        {
            ctxMock.Verify(c => c.Flush(), Times.AtLeast(invocations));
        }

        ctxMock.Verify(c => c.CloseAsync(It.IsAny<CancellationToken>()), Times.Exactly(invocations));

        if (failed)
        {
            writer.Verify(w => w.WriteGoAwayAsync(ctx, int.MaxValue, ENHANCE_YOUR_CALM.Code(),
                    It.IsAny<ByteBuf>(), It.IsAny<TaskCompletionSource>()), Times.Exactly(1));
        }
    }

    private static TaskCompletionSource NewPromise()
    {
        return new TaskCompletionSource();
    }
}
