/*
 * Copyright 2016 The Netty Project
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
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Common.ReferenceCountUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Tests.Http2TestUtil;
using Assert = XunitAssertMessages.AssertM;
using State = Netty.NET.Handler.Codec.Http2.IHttp2Stream.State;
using StreamException = Netty.NET.Handler.Codec.Http2.Http2Exception.StreamException;
using UnsupportedMessageTypeException = Netty.NET.Handler.Codec.UnsupportedMessageTypeException;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: the abstract JUnit fixture is an abstract generic xUnit class whose [Fact]/[Theory] methods run once per derived
// class, as JUnit runs the inherited tests (owner rule 9); C extends Http2FrameCodec is the constraint where C :
// Http2FrameCodec. @BeforeEach is the constructor and @AfterEach Dispose() (xUnit creates a test-class instance per
// test, docs/codec-http-porting.md, Shared CLR adaptations); setUp()/tearDown() are private, as in Http2FrameCodecTest,
// because xUnit1013 reports a public non-test method of a test class. The constructor calls the abstract newCodec and
// newMultiplexer, as Java's setUp() does; the derived class has no state of its own.
// Mockito is Moq (docs/codec-http-porting.md, Shared CLR adaptations): the mocked frame writer is the shared
// Http2TestUtil.MockedFrameWriter() and verify(frameWriter)/verify(frameWriter, never()) are
// Mock.Get(frameWriter).Verify(..., Times.Once()/Times.Never()); eq(x) is the value x (Moq compares it with Equals),
// eqCodecCtx() codec.ctx, any(...)/anyInt()/anyLong()/anyBoolean() It.IsAny and argThat(matcher) It.Is(predicate);
// when(...).thenAnswer(answer) is Setup(...).Returns(...) whose callback completes the TaskCompletionSource argument
// and returns its Task, as the Answer returns the completed promise. ChannelFuture is Task and ChannelPromise
// TaskCompletionSource; the ChannelPromise overloads fold into the Task-returning members (docs/transport-porting.md,
// Decisions), so close(promise) is the Task of CloseAsync(), and a void promise is a discarded Task
// (docs/transport-porting.md, Not-ported Transport sources: VoidChannelPromise). syncUninterruptibly() is an await,
// which throws the failure, awaitUninterruptibly() an await with SuppressThrowing | ContinueOnCapturedContext, and a
// test that awaits is async (docs/handler-porting.md, Test notes: ChunkedWriteHandlerTest); isDone() is IsCompleted,
// isSuccess() IsCompletedSuccessfully and cause() Http2CodecUtil.Cause. The EmbeddedChannel's operations complete on
// the test thread, as in Java, so every awaited Task is already complete. Http2StreamChannel is IHttp2StreamChannel,
// Http2Stream.State the sealed class IHttp2Stream.State (state() is State()), and the Channel getters (isActive(),
// isOpen(), isWritable(), bytesBeforeUnwritable(), config(), pipeline(), parent(), closeFuture()) are properties of the
// Transport's IChannel (src/Netty.NET.Transport/Channels/IChannel.cs). channel.unsafe() is the internal
// Unsafe() of AbstractHttp2StreamChannel, which returns its Http2ChannelUnsafe
// (src/Netty.NET.Handler.Codec.Http2/AbstractHttp2StreamChannel.cs), and parentChannel.unsafe().outboundBuffer() is the
// public OutboundBuffer of the Transport's AbstractChannel (src/Netty.NET.Transport/Channels/AbstractChannel.cs).
// assertThrows is Assert.ThrowsAny/ThrowsAnyAsync (docs/codec-http-porting.md, Core test notes); the JUnit message
// overloads use XunitAssertMessages.AssertM. HttpHeaders equality calls Equals directly, as Java's assertEquals calls
// equals (docs/codec-http-porting.md, Shared CLR adaptations). Java Strings passed as CharSequence are Http2TestUtil.Of
// (StringCharSequence); new InetSocketAddress(0) is an IPEndPoint of IPAddress.Any and port 0 (Http2FrameCodecTest).
// The Common AtomicBoolean/AtomicInteger/AtomicReference replace the java.util.concurrent.atomic classes. The anonymous
// classes are private nested classes; identical anonymous bodies share one. The checked "throws" clauses have no
// counterpart.
public abstract class Http2MultiplexTest<C> : IDisposable where C : Http2FrameCodec
{
    private readonly IHttp2Headers request = new DefaultHttp2Headers()
            .Method(HttpMethod.GET.AsciiName()).Scheme(HttpScheme.HTTPS.Name())
            .Authority(new AsciiString("example.org")).Path(new AsciiString("/foo"));

    private EmbeddedChannel parentChannel;
    private IHttp2FrameWriter frameWriter;
    private Http2FrameInboundWriter frameInboundWriter;
    private TestChannelInitializer childChannelInitializer;
    private C codec;

    private const int initialRemoteStreamWindow = 1024;

    protected abstract C NewCodec(TestChannelInitializer childChannelInitializer,  IHttp2FrameWriter frameWriter);

    protected abstract IChannelHandler NewMultiplexer(TestChannelInitializer childChannelInitializer);

    protected Http2MultiplexTest()
    {
        SetUp();
    }

    private void SetUp()
    {
        childChannelInitializer = new TestChannelInitializer();
        parentChannel = new EmbeddedChannel();
        frameInboundWriter = new Http2FrameInboundWriter(parentChannel);
        _ = parentChannel.ConnectAsync(new IPEndPoint(IPAddress.Any, 0));
        frameWriter = Http2TestUtil.MockedFrameWriter();
        codec = NewCodec(childChannelInitializer, frameWriter);
        parentChannel.Pipeline.AddLast(codec);
        IChannelHandler multiplexer = NewMultiplexer(childChannelInitializer);
        if (multiplexer != null)
        {
            parentChannel.Pipeline.AddLast(multiplexer);
        }

        parentChannel.RunPendingTasks();
        parentChannel.Pipeline.FireChannelActive();

        parentChannel.WriteInbound(Http2CodecUtil.ConnectionPrefaceBuf());

        Http2Settings settings = new Http2Settings().InitialWindowSize(initialRemoteStreamWindow);
        frameInboundWriter.WriteInboundSettings(settings);

        Mock.Get(frameWriter).Verify(w => w.WriteSettingsAckAsync(EqCodecCtx(), AnyChannelPromise()), Times.Once());

        frameInboundWriter.WriteInboundSettingsAck();

        IHttp2SettingsFrame settingsFrame = parentChannel.ReadInbound<IHttp2SettingsFrame>();
        Assert.NotNull(settingsFrame);
        IHttp2SettingsAckFrame settingsAckFrame = parentChannel.ReadInbound<IHttp2SettingsAckFrame>();
        Assert.NotNull(settingsAckFrame);

        // Handshake
        Mock.Get(frameWriter).Verify(w => w.WriteSettingsAsync(EqCodecCtx(),
                AnyHttp2Settings(), AnyChannelPromise()), Times.Once());
    }

    private IChannelHandlerContext EqCodecCtx()
    {
        return codec.ctx;
    }

    public void Dispose()
    {
        TearDown();
    }

    private void TearDown()
    {
        if (childChannelInitializer.handler is LastInboundHandler)
        {
            ((LastInboundHandler) childChannelInitializer.handler).FinishAndReleaseAll();
        }

        parentChannel.FinishAndReleaseAll();
        codec = null;
    }

    // TODO(buchgr): Flush from child channel
    // TODO(buchgr): ChildChannel.childReadComplete()
    // TODO(buchgr): GOAWAY Logic
    // TODO(buchgr): Test ChannelConfig.setMaxMessagesPerRead

    [Fact]
    public void WriteUnknownFrame()
    {
        IHttp2StreamChannel childChannel = NewOutboundStream(new UnknownFrameWritingHandler());
        Assert.True(childChannel.IsActive);

        parentChannel.RunPendingTasks();

        Mock.Get(frameWriter).Verify(w => w.WriteFrameAsync(codec.ctx, (byte) 99, EqStreamId(childChannel),
                It.IsAny<Http2Flags>(), It.IsAny<ByteBuf>(), It.IsAny<TaskCompletionSource>()), Times.Once());
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of writeUnknownFrame; ctx.writeAndFlush(msg) discards the Task.
    private sealed class UnknownFrameWritingHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            _ = ctx.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(new DefaultHttp2Headers()));
            _ = ctx.WriteAndFlushAsync(new DefaultHttp2UnknownFrame((byte) 99, new Http2Flags()));
            ctx.FireChannelActive();
        }
    }

    // CLR: package-private, so internal; Http2MultiplexHandlerTest calls it.
    internal IHttp2StreamChannel NewInboundStream(int streamId, bool endStream, IChannelHandler childHandler)
    {
        return NewInboundStream(streamId, endStream, null, childHandler);
    }

    private IHttp2StreamChannel NewInboundStream(int streamId, bool endStream,
                                                AtomicInteger maxReads, IChannelHandler childHandler)
    {
        AtomicReference<IHttp2StreamChannel> streamChannelRef = new AtomicReference<IHttp2StreamChannel>();
        childChannelInitializer.maxReads = maxReads;
        childChannelInitializer.handler = new StreamChannelCapturingHandler(streamChannelRef, childHandler);

        frameInboundWriter.WriteInboundHeaders(streamId, request, 0, endStream);
        parentChannel.RunPendingTasks();
        IHttp2StreamChannel channel = streamChannelRef.Get();
        Assert.Equal(streamId, channel.Stream().Id());
        return channel;
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of newInboundStream.
    private sealed class StreamChannelCapturingHandler : ChannelInboundHandlerAdapter
    {
        private readonly AtomicReference<IHttp2StreamChannel> streamChannelRef;
        private readonly IChannelHandler childHandler;

        internal StreamChannelCapturingHandler(AtomicReference<IHttp2StreamChannel> streamChannelRef,
                                               IChannelHandler childHandler)
        {
            this.streamChannelRef = streamChannelRef;
            this.childHandler = childHandler;
        }

        public override void ChannelRegistered(IChannelHandlerContext ctx)
        {
            Assert.Null(streamChannelRef.Get());
            streamChannelRef.Set((IHttp2StreamChannel) ctx.Channel);
            ctx.Pipeline.AddLast(childHandler);
            ctx.FireChannelRegistered();
        }
    }

    [Fact]
    public void ReadUnkownFrame()
    {
        LastInboundHandler handler = new LastInboundHandler();

        IHttp2StreamChannel channel = NewInboundStream(3, true, handler);
        frameInboundWriter.WriteInboundFrame((byte) 99, channel.Stream().Id(), new Http2Flags(), Unpooled.EmptyBuffer);

        // header frame and unknown frame
        VerifyFramesMultiplexedToCorrectChannel(channel, handler, 2);

        IChannel childChannel = NewOutboundStream(new ChannelInboundHandlerAdapter());
        Assert.True(childChannel.IsActive);
    }

    [Fact]
    public void ReadPriorityFrame()
    {
        LastInboundHandler handler = new LastInboundHandler();

        IHttp2StreamChannel channel = NewInboundStream(3, true, handler);
        frameInboundWriter.WriteInboundPriority(channel.Stream().Id(), 0, (short) 2, false);

        // header frame should be multiplexed via fireChannelRead(...)
        int numFrames = UseUserEventForPriorityFrame() ? 1 : 2;
        VerifyFramesMultiplexedToCorrectChannel(channel, handler, numFrames);

        if (numFrames == 1)
        {
            IHttp2PriorityFrame priorityFrame = handler.ReadUserEvent<IHttp2PriorityFrame>();
            Assert.Equal(channel.Stream(), priorityFrame.Stream());
            Assert.Equal(0, priorityFrame.StreamDependency());
            Assert.Equal(2, priorityFrame.Weight());
            Assert.False(priorityFrame.Exclusive());
        }
    }

    [Fact]
    public void HeaderAndDataFramesShouldBeDelivered()
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();

        IHttp2StreamChannel channel = NewInboundStream(3, false, inboundHandler);
        IHttp2HeadersFrame headersFrame = new DefaultHttp2HeadersFrame(request).Stream(channel.Stream());
        IHttp2DataFrame dataFrame1 = new DefaultHttp2DataFrame(Bb("hello")).Stream(channel.Stream());
        IHttp2DataFrame dataFrame2 = new DefaultHttp2DataFrame(Bb("world")).Stream(channel.Stream());

        Assert.True(inboundHandler.IsChannelActive());
        frameInboundWriter.WriteInboundData(channel.Stream().Id(), Bb("hello"), 0, false);
        frameInboundWriter.WriteInboundData(channel.Stream().Id(), Bb("world"), 0, false);

        Assert.Equal<object>(headersFrame, inboundHandler.ReadInbound<object>());

        AssertEqualsAndRelease(dataFrame1, inboundHandler.ReadInbound<IHttp2Frame>());
        AssertEqualsAndRelease(dataFrame2, inboundHandler.ReadInbound<IHttp2Frame>());

        Assert.Null(inboundHandler.ReadInbound<object>());
    }

    // CLR: package-private in Java; public because it is the parameter type of the public theory method below:
    // "Inconsistent accessibility: parameter type 'type' is less accessible than method 'method'"
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs0051).
    public enum RstFrameTestMode
    {
        HEADERS_END_STREAM,
        DATA_END_STREAM,
        TRAILERS_END_STREAM
    }

    // CLR: @EnumSource(RstFrameTestMode.class) supplies every constant of the enum; the theory reads them from this
    // public member (MemberData, which "may only point at public members",
    // https://xunit.net/xunit.analyzers/rules/xUnit1016), because a constant of an enum nested in the generic fixture
    // is no attribute argument: [InlineData(RstFrameTestMode.HEADERS_END_STREAM)] fails with CS0182 (measured), "An
    // attribute argument must be a constant expression, typeof expression, or array creation expression of an
    // attribute parameter type"
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/parameter-argument-mismatch).
    public static TheoryData<RstFrameTestMode> RstFrameTestModes()
    {
        return new TheoryData<RstFrameTestMode>(Enum.GetValues<RstFrameTestMode>());
    }

    [Theory]
    [MemberData(nameof(RstFrameTestModes))]
    public async Task NoRstFrameSentOnCloseViaListener(RstFrameTestMode mode)
    {
        LastInboundHandler inboundHandler = new EndStreamCloseHandler(mode);

        IHttp2StreamChannel channel = NewInboundStream(3, mode == RstFrameTestMode.HEADERS_END_STREAM, inboundHandler);

        if (mode != RstFrameTestMode.HEADERS_END_STREAM)
        {
            frameInboundWriter.WriteInboundData(
                    channel.Stream().Id(), Bb("something"), 0, mode == RstFrameTestMode.DATA_END_STREAM);

            if (mode != RstFrameTestMode.DATA_END_STREAM)
            {
                frameInboundWriter.WriteInboundHeaders(channel.Stream().Id(), new DefaultHttp2Headers(), 0, true);
            }
        }

        await channel.CloseFuture;

        // We should never produce a RST frame in this case as we received the endOfStream before we write a frame
        // with the endOfStream flag.
        Mock.Get(frameWriter).Verify(w => w.WriteRstStreamAsync(EqCodecCtx(),
                EqStreamId(channel), It.IsAny<long>(), AnyChannelPromise()), Times.Never());
        inboundHandler.CheckException();
    }

    // CLR: the anonymous LastInboundHandler of noRstFrameSentOnCloseViaListener. The switch on the simple enum is a C#
    // switch; fail() is Assert.Fail(null). ChannelFutureListener.CLOSE closes the channel of the write
    // (docs/codec-http-porting.md, Shared CLR adaptations) and is added with Http2CodecUtil.AddListener, the listener
    // timing of DefaultPromise.notifyListeners() (docs/codec-http-porting.md, Listener timing).
    private sealed class EndStreamCloseHandler : LastInboundHandler
    {
        private readonly RstFrameTestMode mode;
        private bool headersReceived;

        internal EndStreamCloseHandler(RstFrameTestMode mode)
        {
            this.mode = mode;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            try
            {
                bool endStream;

                if (msg is IHttp2HeadersFrame)
                {
                    endStream = ((IHttp2HeadersFrame) msg).IsEndStream();

                    switch (mode)
                    {
                        case RstFrameTestMode.HEADERS_END_STREAM:
                            Assert.False(headersReceived);
                            Assert.True(endStream);
                            break;
                        case RstFrameTestMode.TRAILERS_END_STREAM:
                            if (headersReceived)
                            {
                                Assert.True(endStream);
                            }
                            else
                            {
                                Assert.False(endStream);
                            }

                            break;
                        case RstFrameTestMode.DATA_END_STREAM:
                            Assert.False(endStream);
                            break;
                        default:
                            Assert.Fail(null);
                            break;
                    }

                    headersReceived = true;
                }
                else if (msg is IHttp2DataFrame)
                {
                    endStream = ((IHttp2DataFrame) msg).IsEndStream();

                    switch (mode)
                    {
                        case RstFrameTestMode.HEADERS_END_STREAM:
                            Assert.Fail(null);
                            break;
                        case RstFrameTestMode.TRAILERS_END_STREAM:
                            Assert.False(endStream);
                            break;
                        case RstFrameTestMode.DATA_END_STREAM:
                            Assert.True(endStream);
                            break;
                        default:
                            Assert.Fail(null);
                            break;
                    }
                }
                else
                {
                    throw new UnsupportedMessageTypeException(msg);
                }

                if (endStream)
                {
                    Http2CodecUtil.AddListener(ctx,
                            ctx.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(new DefaultHttp2Headers(), true, 0)),
                            future => _ = ctx.Channel.CloseAsync());
                }
            }
            finally
            {
                ReferenceCountUtil.Release(msg);
            }
        }
    }

    [Fact]
    public void HeaderMultipleContentLengthValidationShouldPropagate()
    {
        HeaderMultipleContentLengthValidationShouldPropagate0(false);
    }

    [Fact]
    public void HeaderMultipleContentLengthValidationShouldPropagateWithEndStream()
    {
        HeaderMultipleContentLengthValidationShouldPropagate0(true);
    }

    // CLR: the private overload of the test method takes the suffix 0, because "Test methods cannot have overloads"
    // (https://xunit.net/xunit.analyzers/rules/xUnit1024).
    private void HeaderMultipleContentLengthValidationShouldPropagate0(bool endStream)
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        request.AddLong(HttpHeaderNames.CONTENT_LENGTH, 0);
        request.AddLong(HttpHeaderNames.CONTENT_LENGTH, 1);
        IHttp2StreamChannel channel = NewInboundStream(3, endStream, inboundHandler);

        Assert.ThrowsAny<StreamException>(() => inboundHandler.CheckException());
        Assert.Null(inboundHandler.ReadInbound<object>());
        Assert.False(channel.IsActive);
    }

    [Fact]
    public void HeaderPlusSignContentLengthValidationShouldPropagate()
    {
        HeaderSignContentLengthValidationShouldPropagateWithEndStream(false, false);
    }

    [Fact]
    public void HeaderPlusSignContentLengthValidationShouldPropagateWithEndStream()
    {
        HeaderSignContentLengthValidationShouldPropagateWithEndStream(false, true);
    }

    [Fact]
    public void HeaderMinusSignContentLengthValidationShouldPropagate()
    {
        HeaderSignContentLengthValidationShouldPropagateWithEndStream(true, false);
    }

    [Fact]
    public void HeaderMinusSignContentLengthValidationShouldPropagateWithEndStream()
    {
        HeaderSignContentLengthValidationShouldPropagateWithEndStream(true, true);
    }

    private void HeaderSignContentLengthValidationShouldPropagateWithEndStream(bool minus, bool endStream)
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        request.Add(HttpHeaderNames.CONTENT_LENGTH, Of((minus ? "-" : "+") + 1));
        IHttp2StreamChannel channel = NewInboundStream(3, endStream, inboundHandler);
        Assert.ThrowsAny<StreamException>(() => inboundHandler.CheckException());

        Assert.Null(inboundHandler.ReadInbound<object>());
        Assert.False(channel.IsActive);
    }

    [Fact]
    public Task HeaderContentLengthNotMatchValidationShouldPropagate()
    {
        return HeaderContentLengthNotMatchValidationShouldPropagate0(false, false, false);
    }

    [Fact]
    public Task HeaderContentLengthNotMatchValidationShouldPropagateWithEndStream()
    {
        return HeaderContentLengthNotMatchValidationShouldPropagate0(false, true, false);
    }

    [Fact]
    public Task HeaderContentLengthNotMatchValidationShouldPropagateCloseLocal()
    {
        return HeaderContentLengthNotMatchValidationShouldPropagate0(true, false, false);
    }

    [Fact]
    public Task HeaderContentLengthNotMatchValidationShouldPropagateWithEndStreamCloseLocal()
    {
        return HeaderContentLengthNotMatchValidationShouldPropagate0(true, true, false);
    }

    [Fact]
    public Task HeaderContentLengthNotMatchValidationShouldPropagateTrailers()
    {
        return HeaderContentLengthNotMatchValidationShouldPropagate0(false, false, true);
    }

    [Fact]
    public Task HeaderContentLengthNotMatchValidationShouldPropagateWithEndStreamTrailers()
    {
        return HeaderContentLengthNotMatchValidationShouldPropagate0(false, true, true);
    }

    [Fact]
    public Task HeaderContentLengthNotMatchValidationShouldPropagateCloseLocalTrailers()
    {
        return HeaderContentLengthNotMatchValidationShouldPropagate0(true, false, true);
    }

    [Fact]
    public Task HeaderContentLengthNotMatchValidationShouldPropagateWithEndStreamCloseLocalTrailers()
    {
        return HeaderContentLengthNotMatchValidationShouldPropagate0(true, true, true);
    }

    // CLR: see HeaderMultipleContentLengthValidationShouldPropagate0 (xUnit1024).
    private async Task HeaderContentLengthNotMatchValidationShouldPropagate0(
            bool closeLocal, bool endStream, bool trailer)
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        request.AddLong(HttpHeaderNames.CONTENT_LENGTH, 1);
        IHttp2StreamChannel channel = NewInboundStream(3, false, inboundHandler);
        Assert.True(channel.IsActive);

        if (closeLocal)
        {
            await channel.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(new DefaultHttp2Headers(), true),
                    TestContext.Current.CancellationToken);
            Assert.Equal(State.HALF_CLOSED_LOCAL, channel.Stream().State());
        }
        else
        {
            Assert.Equal(State.OPEN, channel.Stream().State());
        }

        if (trailer)
        {
            frameInboundWriter.WriteInboundHeaders(channel.Stream().Id(), new DefaultHttp2Headers(), 0, endStream);
        }
        else
        {
            frameInboundWriter.WriteInboundData(channel.Stream().Id(), Bb("foo"), 0, endStream);
        }

        Assert.ThrowsAny<StreamException>(() => inboundHandler.CheckException());

        IHttp2HeadersFrame headersFrame = new DefaultHttp2HeadersFrame(request).Stream(channel.Stream());
        Assert.Equal<object>(headersFrame, inboundHandler.ReadInbound<object>());
        Assert.Null(inboundHandler.ReadInbound<object>());
        Assert.False(channel.IsActive);
    }

    [Fact]
    public void StreamExceptionCauseRstStreamWithProtocolError()
    {
        request.AddLong(HttpHeaderNames.CONTENT_LENGTH, 10);
        IHttp2StreamChannel channel = NewInboundStream(3, false, new ChannelInboundHandlerAdapter());
        channel.Pipeline.FireExceptionCaught(new Http2FrameStreamException(channel.Stream(),
                Http2Error.PROTOCOL_ERROR, new ArgumentException()));
        Assert.False(channel.IsActive);
        Mock.Get(frameWriter).Verify(w => w.WriteRstStreamAsync(EqCodecCtx(), 3,
                Http2Error.PROTOCOL_ERROR.Code(), AnyChannelPromise()), Times.Once());
    }

    [Fact]
    public void ContentLengthNotMatchRstStreamWithProtocolError()
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        request.AddLong(HttpHeaderNames.CONTENT_LENGTH, 10);
        IHttp2StreamChannel channel = NewInboundStream(3, false, inboundHandler);
        frameInboundWriter.WriteInboundData(3, Bb(8), 0, true);
        Assert.ThrowsAny<StreamException>(() => inboundHandler.CheckException());
        Assert.NotNull(inboundHandler.ReadInbound<object>());
        Assert.False(channel.IsActive);
        Mock.Get(frameWriter).Verify(w => w.WriteRstStreamAsync(EqCodecCtx(), 3,
                Http2Error.PROTOCOL_ERROR.Code(), AnyChannelPromise()), Times.Once());
    }

    [Fact]
    public void FramesShouldBeMultiplexed()
    {
        LastInboundHandler handler1 = new LastInboundHandler();
        IHttp2StreamChannel channel1 = NewInboundStream(3, false, handler1);
        LastInboundHandler handler2 = new LastInboundHandler();
        IHttp2StreamChannel channel2 = NewInboundStream(5, false, handler2);
        LastInboundHandler handler3 = new LastInboundHandler();
        IHttp2StreamChannel channel3 = NewInboundStream(11, false, handler3);

        VerifyFramesMultiplexedToCorrectChannel(channel1, handler1, 1);
        VerifyFramesMultiplexedToCorrectChannel(channel2, handler2, 1);
        VerifyFramesMultiplexedToCorrectChannel(channel3, handler3, 1);

        frameInboundWriter.WriteInboundData(channel2.Stream().Id(), Bb("hello"), 0, false);
        frameInboundWriter.WriteInboundData(channel1.Stream().Id(), Bb("foo"), 0, true);
        frameInboundWriter.WriteInboundData(channel2.Stream().Id(), Bb("world"), 0, true);
        frameInboundWriter.WriteInboundData(channel3.Stream().Id(), Bb("bar"), 0, true);

        VerifyFramesMultiplexedToCorrectChannel(channel1, handler1, 1);
        VerifyFramesMultiplexedToCorrectChannel(channel2, handler2, 2);
        VerifyFramesMultiplexedToCorrectChannel(channel3, handler3, 1);
    }

    [Fact]
    public void InboundDataFrameShouldUpdateLocalFlowController()
    {
        Mock<IHttp2LocalFlowController> flowController = new Mock<IHttp2LocalFlowController>();
        codec.Connection().Local().FlowController(flowController.Object);

        LastInboundHandler handler = new LastInboundHandler();
        IHttp2StreamChannel channel = NewInboundStream(3, false, handler);

        ByteBuf tenBytes = Bb("0123456789");

        frameInboundWriter.WriteInboundData(channel.Stream().Id(), tenBytes, 0, true);

        // Verify we marked the bytes as consumed
        flowController.Verify(f => f.ConsumeBytes(
                It.Is<IHttp2Stream>(http2Stream => http2Stream.Id() == channel.Stream().Id()), 10), Times.Once());

        // headers and data frame
        VerifyFramesMultiplexedToCorrectChannel(channel, handler, 2);
    }

    [Fact]
    public void UnhandledHttp2FramesShouldBePropagated()
    {
        IHttp2PingFrame pingFrame = new DefaultHttp2PingFrame(0);
        frameInboundWriter.WriteInboundPing(false, 0);
        Assert.Equal<object>(parentChannel.ReadInbound<object>(), pingFrame);

        DefaultHttp2GoAwayFrame goAwayFrame = new DefaultHttp2GoAwayFrame(1,
                parentChannel.Alloc().Buffer().WriteLong(8));
        frameInboundWriter.WriteInboundGoAway(0, goAwayFrame.ErrorCode(), goAwayFrame.Content.RetainedDuplicate());

        IHttp2GoAwayFrame frame = parentChannel.ReadInbound<IHttp2GoAwayFrame>();
        AssertEqualsAndRelease(frame, goAwayFrame);
    }

    [Fact]
    public void ChannelReadShouldRespectAutoRead()
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        IHttp2StreamChannel childChannel = NewInboundStream(3, false, inboundHandler);
        Assert.True(childChannel.Config.IsAutoRead());
        IHttp2HeadersFrame headersFrame = inboundHandler.ReadInbound<IHttp2HeadersFrame>();
        Assert.NotNull(headersFrame);

        childChannel.Config.SetAutoRead(false);

        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("hello world"), 0, false);
        IHttp2DataFrame dataFrame0 = inboundHandler.ReadInbound<IHttp2DataFrame>();
        Assert.NotNull(dataFrame0);
        Release(dataFrame0);

        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("foo"), 0, false);
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("bar"), 0, false);

        Assert.Null(inboundHandler.ReadInbound<object>());

        childChannel.Config.SetAutoRead(true);
        VerifyFramesMultiplexedToCorrectChannel(childChannel, inboundHandler, 2);
    }

    // CLR: String.valueOf(i) is the invariant decimal text of i.
    [Fact]
    public void NoAutoReadWithReentrantReadDoesNotSOOE()
    {
        AtomicBoolean shouldRead = new AtomicBoolean();
        LastInboundHandler.IConsumer<IChannelHandlerContext> ctxConsumer = new ReadIfRequestedConsumer(shouldRead);
        LastInboundHandler inboundHandler = new LastInboundHandler(ctxConsumer);
        AtomicInteger maxReads = new AtomicInteger(1);
        IHttp2StreamChannel childChannel = NewInboundStream(3, false, maxReads, inboundHandler);
        Assert.True(childChannel.Config.IsAutoRead());
        IHttp2HeadersFrame headersFrame = inboundHandler.ReadInbound<IHttp2HeadersFrame>();
        Assert.NotNull(headersFrame);

        childChannel.Config.SetAutoRead(false);

        const int maxWrites = 10000; // enough writes to generated SOOE.

        for (int i = 0; i < maxWrites; ++i)
        {
            frameInboundWriter.WriteInboundData(childChannel.Stream().Id(),
                    Bb(i.ToString(CultureInfo.InvariantCulture)), 0, false);
        }

        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(),
                Bb(maxWrites.ToString(CultureInfo.InvariantCulture)), 0, true);
        shouldRead.Set(true);
        childChannel.Read();

        for (int i = 0; i < maxWrites; ++i)
        {
            IHttp2DataFrame dataFrame1 = inboundHandler.ReadInbound<IHttp2DataFrame>();
            Assert.NotNull(dataFrame1);
            Release(dataFrame1);
        }

        // CLR: the loop body's dataFrame0 is dataFrame1 in the port: "within a nested declaration space it is not
        // possible to declare a local variable or constant with the same name as a local variable or constant in an
        // enclosing declaration space" (https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0136).
        IHttp2DataFrame dataFrame0 = inboundHandler.ReadInbound<IHttp2DataFrame>();
        Assert.True(dataFrame0.IsEndStream());
        Release(dataFrame0);

        VerifyFramesMultiplexedToCorrectChannel(childChannel, inboundHandler, 0);
    }

    // CLR: the anonymous Consumer of noAutoReadWithReentrantReadDoesNotSOOE; obj.read() discards the invoker.
    private sealed class ReadIfRequestedConsumer : LastInboundHandler.IConsumer<IChannelHandlerContext>
    {
        private readonly AtomicBoolean shouldRead;

        internal ReadIfRequestedConsumer(AtomicBoolean shouldRead)
        {
            this.shouldRead = shouldRead;
        }

        public void Accept(IChannelHandlerContext obj)
        {
            if (shouldRead.Get())
            {
                obj.Read();
            }
        }
    }

    [Fact]
    public async Task ReadNotRequiredToEndStream()
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        AtomicInteger maxReads = new AtomicInteger(1);
        IHttp2StreamChannel childChannel = NewInboundStream(3, false, maxReads, inboundHandler);
        Assert.True(childChannel.Config.IsAutoRead());

        childChannel.Config.SetAutoRead(false);

        IHttp2HeadersFrame headersFrame = inboundHandler.ReadInbound<IHttp2HeadersFrame>();
        Assert.NotNull(headersFrame);

        Assert.Null(inboundHandler.ReadInbound<object>());

        frameInboundWriter.WriteInboundRstStream(childChannel.Stream().Id(), NO_ERROR.Code());

        Assert.False(inboundHandler.IsChannelActive());
        await childChannel.CloseFuture;

        IHttp2ResetFrame resetFrame = UseUserEventForResetFrame() ? inboundHandler.ReadUserEvent<IHttp2ResetFrame>() :
                inboundHandler.ReadInbound<IHttp2ResetFrame>();

        Assert.Equal(childChannel.Stream(), resetFrame.Stream());
        Assert.Equal(NO_ERROR.Code(), resetFrame.ErrorCode());

        VerifyFramesMultiplexedToCorrectChannel(childChannel, inboundHandler, 0);
    }

    [Fact]
    public void ChannelReadShouldRespectAutoReadAndNotProduceNPE()
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        IHttp2StreamChannel childChannel = NewInboundStream(3, false, inboundHandler);
        Assert.True(childChannel.Config.IsAutoRead());
        IHttp2HeadersFrame headersFrame = inboundHandler.ReadInbound<IHttp2HeadersFrame>();
        Assert.NotNull(headersFrame);

        childChannel.Config.SetAutoRead(false);
        childChannel.Pipeline.AddFirst(new CloseAfterTwoReadsHandler());
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("hello world"), 0, false);
        IHttp2DataFrame dataFrame0 = inboundHandler.ReadInbound<IHttp2DataFrame>();
        Assert.NotNull(dataFrame0);
        Release(dataFrame0);

        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("foo"), 0, false);
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("bar"), 0, false);
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("bar"), 0, false);

        Assert.Null(inboundHandler.ReadInbound<object>());

        childChannel.Config.SetAutoRead(true);
        VerifyFramesMultiplexedToCorrectChannel(childChannel, inboundHandler, 3);
        inboundHandler.CheckException();
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of channelReadShouldRespectAutoReadAndNotProduceNPE; ctx.close()
    // discards the Task.
    private sealed class CloseAfterTwoReadsHandler : ChannelInboundHandlerAdapter
    {
        private int count;

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ctx.FireChannelRead(msg);

            // Close channel after 2 reads so there is still something in the inboundBuffer when the close happens.
            if (++count == 2)
            {
                _ = ctx.CloseAsync();
            }
        }
    }

    [Fact]
    public void ReadInChannelReadWithoutAutoRead()
    {
        UseReadWithoutAutoRead(false);
    }

    [Fact]
    public void ReadInChannelReadCompleteWithoutAutoRead()
    {
        UseReadWithoutAutoRead(true);
    }

    private void UseReadWithoutAutoRead(bool readComplete)
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        IHttp2StreamChannel childChannel = NewInboundStream(3, false, inboundHandler);
        Assert.True(childChannel.Config.IsAutoRead());
        childChannel.Config.SetAutoRead(false);
        Assert.False(childChannel.Config.IsAutoRead());

        IHttp2HeadersFrame headersFrame = inboundHandler.ReadInbound<IHttp2HeadersFrame>();
        Assert.NotNull(headersFrame);

        // Add a handler which will request reads.
        childChannel.Pipeline.AddFirst(new ReadRequestingHandler(readComplete));

        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("hello world"), 0, false);
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("foo"), 0, false);
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("bar"), 0, false);
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("hello world"), 0, false);
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("foo"), 0, false);
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("bar"), 0, true);

        VerifyFramesMultiplexedToCorrectChannel(childChannel, inboundHandler, 6);
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of useReadWithoutAutoRead.
    private sealed class ReadRequestingHandler : ChannelInboundHandlerAdapter
    {
        private readonly bool readComplete;

        internal ReadRequestingHandler(bool readComplete)
        {
            this.readComplete = readComplete;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ctx.FireChannelRead(msg);

            if (!readComplete)
            {
                ctx.Read();
            }
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            ctx.FireChannelReadComplete();

            if (readComplete)
            {
                ctx.Read();
            }
        }
    }

    // CLR: parentChannel.close() discards the Task.
    [Fact]
    public void AllQueuedFramesDeliveredAfterParentIsClosed()
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        IHttp2StreamChannel childChannel = NewInboundStream(3, false, new AtomicInteger(1), inboundHandler);
        Assert.True(childChannel.Config.IsAutoRead());
        childChannel.Config.SetAutoRead(false);
        Assert.False(childChannel.Config.IsAutoRead());

        IHttp2HeadersFrame headersFrame = inboundHandler.ReadInbound<IHttp2HeadersFrame>();
        Assert.NotNull(headersFrame);

        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("foo"), 0, false);
        VerifyFramesMultiplexedToCorrectChannel(childChannel, inboundHandler, 1);
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("bar"), 0, false);
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("baz"), 0, true);
        Assert.Null(inboundHandler.ReadInbound<object>());

        _ = parentChannel.CloseAsync(TestContext.Current.CancellationToken);
        Assert.True(childChannel.IsActive);
        childChannel.Read();
        inboundHandler.CheckException();

        VerifyFramesMultiplexedToCorrectChannel(childChannel, inboundHandler, 2);
        Assert.False(childChannel.IsActive);
    }

    [Fact]
    public void AllQueuedFramesDeliveredToOutboundStreamAfterParentIsClosed()
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        IHttp2StreamChannel childChannel = NewOutboundStream(inboundHandler);
        Assert.True(childChannel.IsActive);

        IHttp2Headers headers = new DefaultHttp2Headers().Scheme(Of("https")).Method(Of("GET")).Path(Of("/foo.txt"));
        _ = childChannel.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(headers, true),
                TestContext.Current.CancellationToken);
        childChannel.Config.SetAutoRead(false);

        frameInboundWriter.WriteInboundHeaders(childChannel.Stream().Id(), headers, 0, false);
        IHttp2HeadersFrame headersFrame = inboundHandler.ReadInbound<IHttp2HeadersFrame>();
        Assert.NotNull(headersFrame);

        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("bar"), 0, false);
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("baz"), 0, true);
        Assert.Null(inboundHandler.ReadInbound<object>());

        _ = parentChannel.CloseAsync(TestContext.Current.CancellationToken);
        Assert.True(childChannel.IsActive);
        childChannel.Read();
        inboundHandler.CheckException();

        VerifyFramesMultiplexedToCorrectChannel(childChannel, inboundHandler, 2);
        Assert.False(childChannel.IsActive);
    }

    // CLR: open().syncUninterruptibly().getNow(): the Task of OpenAsync() is complete when it returns, because the
    // EmbeddedChannel's event loop registers the stream channel and runs the registration listener on the calling
    // thread; GetAwaiter().GetResult() returns the channel or throws the failure, as syncUninterruptibly() does.
    private IHttp2StreamChannel NewOutboundStream(IChannelHandler handler)
    {
        return new Http2StreamChannelBootstrap(parentChannel).Handler(handler)
                .OpenAsync().GetAwaiter().GetResult();
    }

    /**
     * A child channel for an HTTP/2 stream in IDLE state (that is no headers sent or received),
     * should not emit a RST_STREAM frame on close, as this is a connection error of type protocol error.
     */
    [Fact]
    public void IdleOutboundStreamShouldNotWriteResetFrameOnClose()
    {
        LastInboundHandler handler = new LastInboundHandler();

        IChannel childChannel = NewOutboundStream(handler);
        Assert.True(childChannel.IsActive);

        _ = childChannel.CloseAsync(TestContext.Current.CancellationToken);
        parentChannel.RunPendingTasks();

        Assert.False(childChannel.IsOpen);
        Assert.False(childChannel.IsActive);
        Assert.Null(parentChannel.ReadOutbound<object>());
    }

    [Fact]
    public void OutboundStreamShouldWriteResetFrameOnClose_headersSent()
    {
        IChannelHandler handler = new HeadersWritingHandler();

        IHttp2StreamChannel childChannel = NewOutboundStream(handler);
        Assert.True(childChannel.IsActive);

        _ = childChannel.CloseAsync(TestContext.Current.CancellationToken);
        Mock.Get(frameWriter).Verify(w => w.WriteRstStreamAsync(EqCodecCtx(),
                EqStreamId(childChannel), Http2Error.CANCEL.Code(), AnyChannelPromise()), Times.Once());
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of outboundStreamShouldWriteResetFrameOnClose_headersSent and
    // outboundStreamShouldNotWriteResetFrameOnClose_IfStreamDidntExist (identical bodies).
    private sealed class HeadersWritingHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            _ = ctx.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(new DefaultHttp2Headers()));
            ctx.FireChannelActive();
        }
    }

    [Fact]
    public void OutboundStreamShouldNotWriteResetFrameOnClose_IfStreamDidntExist()
    {
        FailFirstHeadersAnswer answer = new FailFirstHeadersAnswer();
        Mock.Get(frameWriter).Setup(w => w.WriteHeadersAsync(EqCodecCtx(), It.IsAny<int>(),
                It.IsAny<IHttp2Headers>(), It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<TaskCompletionSource>())).Returns(
            (IChannelHandlerContext ctx, int streamId, IHttp2Headers headers, int padding, bool endStream,
                TaskCompletionSource promise) => answer.Answer(promise));

        IHttp2StreamChannel childChannel = NewOutboundStream(new HeadersWritingHandler());

        Assert.False(childChannel.IsActive);

        _ = childChannel.CloseAsync(TestContext.Current.CancellationToken);
        parentChannel.RunPendingTasks();
        // The channel was never active so we should not generate a RST frame.
        Mock.Get(frameWriter).Verify(w => w.WriteRstStreamAsync(EqCodecCtx(), EqStreamId(childChannel),
                It.IsAny<long>(), AnyChannelPromise()), Times.Never());

        Assert.Empty(parentChannel.OutboundMessages());
    }

    // CLR: the anonymous Answer<ChannelFuture> of outboundStreamShouldNotWriteResetFrameOnClose_IfStreamDidntExist; the
    // promise is getArgument(5) and setFailure/setSuccess return it as the future.
    private sealed class FailFirstHeadersAnswer
    {
        private bool headersWritten;

        internal Task Answer(TaskCompletionSource promise)
        {
            // We want to fail to write the first headers frame. This is what happens if the connection
            // refuses to allocate a new stream due to having received a GOAWAY.
            if (!headersWritten)
            {
                headersWritten = true;
                promise.SetException(new Exception("boom"));
                return promise.Task;
            }

            promise.SetResult();
            return promise.Task;
        }
    }

    [Fact]
    public void InboundRstStreamFireChannelInactive()
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        IHttp2StreamChannel channel = NewInboundStream(3, false, inboundHandler);
        Assert.True(inboundHandler.IsChannelActive());
        frameInboundWriter.WriteInboundRstStream(channel.Stream().Id(), Http2Error.INTERNAL_ERROR.Code());

        Assert.False(inboundHandler.IsChannelActive());

        // A RST_STREAM frame should NOT be emitted, as we received a RST_STREAM.
        Mock.Get(frameWriter).Verify(w => w.WriteRstStreamAsync(EqCodecCtx(), EqStreamId(channel),
                It.IsAny<long>(), AnyChannelPromise()), Times.Never());
    }

    [Fact]
    public void StreamExceptionTriggersChildChannelExceptionAndClose()
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        IHttp2StreamChannel channel = NewInboundStream(3, false, inboundHandler);
        Assert.True(channel.IsActive);
        StreamException cause = new StreamException(channel.Stream().Id(), Http2Error.PROTOCOL_ERROR, "baaam!");
        parentChannel.Pipeline.FireExceptionCaught(cause);

        Assert.False(channel.IsActive);

        Assert.ThrowsAny<StreamException>(() => inboundHandler.CheckException());
    }

    // CLR: java.nio.channels.ClosedChannelException is the Transport's ClosedChannelException
    // (src/Netty.NET.Transport/Channels/ClosedChannelException.cs).
    [Fact]
    public async Task StreamClosedErrorTranslatedToClosedChannelExceptionOnWrites()
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();

        IHttp2StreamChannel childChannel = NewOutboundStream(inboundHandler);
        Assert.True(childChannel.IsActive);

        IHttp2Headers headers = new DefaultHttp2Headers();
        Mock.Get(frameWriter).Setup(w => w.WriteHeadersAsync(EqCodecCtx(), It.IsAny<int>(),
                headers, It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<TaskCompletionSource>())).Returns(
            (IChannelHandlerContext ctx, int streamId, IHttp2Headers h, int padding, bool endStream,
                TaskCompletionSource promise) =>
            {
                promise.SetException(
                        new StreamException(childChannel.Stream().Id(), Http2Error.STREAM_CLOSED, "Stream Closed"));
                return promise.Task;
            });
        Task future = childChannel.WriteAndFlushAsync(
                new DefaultHttp2HeadersFrame(new DefaultHttp2Headers()), TestContext.Current.CancellationToken);

        parentChannel.Flush();

        Assert.False(childChannel.IsActive);
        Assert.False(childChannel.IsOpen);

        inboundHandler.CheckException();

        await Assert.ThrowsAnyAsync<ClosedChannelException>(() => future);
    }

    [Fact]
    public void CreatingWritingReadingAndClosingOutboundStreamShouldWork()
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        IHttp2StreamChannel childChannel = NewOutboundStream(inboundHandler);
        Assert.True(childChannel.IsActive);
        Assert.True(inboundHandler.IsChannelActive());

        // Write to the child channel
        IHttp2Headers headers = new DefaultHttp2Headers().Scheme(Of("https")).Method(Of("GET")).Path(Of("/foo.txt"));
        _ = childChannel.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(headers),
                TestContext.Current.CancellationToken);

        // Read from the child channel
        frameInboundWriter.WriteInboundHeaders(childChannel.Stream().Id(), headers, 0, false);

        IHttp2HeadersFrame headersFrame = inboundHandler.ReadInbound<IHttp2HeadersFrame>();
        Assert.NotNull(headersFrame);
        Assert.True(headers.Equals(headersFrame.Headers()));

        // Close the child channel.
        _ = childChannel.CloseAsync(TestContext.Current.CancellationToken);

        parentChannel.RunPendingTasks();
        // An active outbound stream should emit a RST_STREAM frame.
        Mock.Get(frameWriter).Verify(w => w.WriteRstStreamAsync(EqCodecCtx(), EqStreamId(childChannel),
                It.IsAny<long>(), AnyChannelPromise()), Times.Once());

        Assert.False(childChannel.IsOpen);
        Assert.False(childChannel.IsActive);
        Assert.False(inboundHandler.IsChannelActive());
    }

    // Test failing the promise of the first headers frame of an outbound stream. In practice this error case would most
    // likely happen due to the max concurrent streams limit being hit or the channel running out of stream identifiers.
    //
    [Fact]
    public async Task FailedOutboundStreamCreationThrowsAndClosesChannel()
    {
        LastInboundHandler handler = new LastInboundHandler();
        IHttp2StreamChannel childChannel = NewOutboundStream(handler);
        Assert.True(childChannel.IsActive);

        IHttp2Headers headers = new DefaultHttp2Headers();
        Mock.Get(frameWriter).Setup(w => w.WriteHeadersAsync(EqCodecCtx(), It.IsAny<int>(),
               headers, It.IsAny<int>(), It.IsAny<bool>(),
               It.IsAny<TaskCompletionSource>())).Returns(
            (IChannelHandlerContext ctx, int streamId, IHttp2Headers h, int padding, bool endStream,
                TaskCompletionSource promise) =>
            {
                promise.SetException(new Http2NoMoreStreamIdsException());
                return promise.Task;
            });

        Task future = childChannel.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(headers),
                TestContext.Current.CancellationToken);
        parentChannel.Flush();

        Assert.False(childChannel.IsActive);
        Assert.False(childChannel.IsOpen);

        handler.CheckException();

        await Assert.ThrowsAnyAsync<Http2NoMoreStreamIdsException>(() => future);
    }

    // CLR: close(promise) has no counterpart (the ChannelPromise overloads fold into CloseAsync(),
    // docs/transport-porting.md, Decisions), so the listener is added to the Task of CloseAsync(), the child channel's
    // own close promise; future.channel() is the child channel. AddListener gives the listener Java's timing: the Task
    // is complete, so it runs at once on the EmbeddedChannel's thread.
    [Fact]
    public async Task ChannelClosedWhenCloseListenerCompletes()
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        IHttp2StreamChannel childChannel = NewInboundStream(3, false, inboundHandler);

        Assert.True(childChannel.IsOpen);
        Assert.True(childChannel.IsActive);

        AtomicBoolean channelOpen = new AtomicBoolean(true);
        AtomicBoolean channelActive = new AtomicBoolean(true);

        // Create a promise before actually doing the close, because otherwise we would be adding a listener to a future
        // that is already completed because we are using EmbeddedChannel which executes code in the JUnit thread.
        Task p = childChannel.CloseAsync(TestContext.Current.CancellationToken);
        _ = AddListener(childChannel, p, future =>
        {
            channelOpen.Set(childChannel.IsOpen);
            channelActive.Set(childChannel.IsActive);
        });
        await p;

        Assert.False(channelOpen.Get());
        Assert.False(channelActive.Get());
        Assert.False(childChannel.IsActive);
    }

    // CLR: the listener on closeFuture() is added with AddListener (Java's timing: the continuation runs on the event
    // loop when closeFuture completes there); future.channel() is the child channel.
    [Fact]
    public async Task ChannelClosedWhenChannelClosePromiseCompletes()
    {
         LastInboundHandler inboundHandler = new LastInboundHandler();
         IHttp2StreamChannel childChannel = NewInboundStream(3, false, inboundHandler);

         Assert.True(childChannel.IsOpen);
         Assert.True(childChannel.IsActive);

         AtomicBoolean channelOpen = new AtomicBoolean(true);
         AtomicBoolean channelActive = new AtomicBoolean(true);

         _ = AddListener(childChannel, childChannel.CloseFuture, future =>
         {
             channelOpen.Set(childChannel.IsOpen);
             channelActive.Set(childChannel.IsActive);
         });
         await childChannel.CloseAsync(TestContext.Current.CancellationToken);

         Assert.False(channelOpen.Get());
         Assert.False(channelActive.Get());
         Assert.False(childChannel.IsActive);
    }

    // CLR: ArrayDeque is Queue and poll() TryDequeue; the ChannelPromise that the frame writer received is the
    // TaskCompletionSource argument. f.awaitUninterruptibly() waits without throwing the failure: an await with
    // ConfigureAwaitOptions.SuppressThrowing, which "Avoids throwing an exception at the completion of awaiting a Task
    // that ends in the Faulted or Canceled state"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.configureawaitoptions?view=net-10.0), with
    // ContinueOnCapturedContext as xUnit1030 asks of a test method. The listener is added with AddListener (Java's
    // timing: it runs on the event loop when the write's promise fails there).
    [Fact]
    public async Task ChannelClosedWhenWriteFutureFails()
    {
        Queue<TaskCompletionSource> writePromises = new Queue<TaskCompletionSource>();

        LastInboundHandler inboundHandler = new LastInboundHandler();
        IHttp2StreamChannel childChannel = NewInboundStream(3, false, inboundHandler);

        Assert.True(childChannel.IsOpen);
        Assert.True(childChannel.IsActive);

        AtomicBoolean channelOpen = new AtomicBoolean(true);
        AtomicBoolean channelActive = new AtomicBoolean(true);

        IHttp2Headers headers = new DefaultHttp2Headers();
        Mock.Get(frameWriter).Setup(w => w.WriteHeadersAsync(EqCodecCtx(), It.IsAny<int>(),
                headers, It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<TaskCompletionSource>())).Returns(
            (IChannelHandlerContext ctx, int streamId, IHttp2Headers h, int padding, bool endStream,
                TaskCompletionSource promise) =>
            {
                writePromises.Enqueue(promise);
                return promise.Task;
            });

        Task f = childChannel.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(headers),
                TestContext.Current.CancellationToken);
        Assert.False(f.IsCompleted);
        _ = AddListener(childChannel, f, future =>
        {
            channelOpen.Set(childChannel.IsOpen);
            channelActive.Set(childChannel.IsActive);
        });

        writePromises.TryDequeue(out TaskCompletionSource first);
        first.SetException(new ClosedChannelException());
        await f.ConfigureAwait(
                ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);

        Assert.False(channelOpen.Get());
        Assert.False(channelActive.Get());
        Assert.False(childChannel.IsActive);
    }

    [Fact]
    public async Task ChannelClosedTwiceMarksPromiseAsSuccessful()
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        IHttp2StreamChannel childChannel = NewInboundStream(3, false, inboundHandler);

        Assert.True(childChannel.IsOpen);
        Assert.True(childChannel.IsActive);
        await childChannel.CloseAsync(TestContext.Current.CancellationToken);
        await childChannel.CloseAsync(TestContext.Current.CancellationToken);

        Assert.False(childChannel.IsOpen);
        Assert.False(childChannel.IsActive);
    }

    // CLR: config().setAutoRead(false).setWriteSpinCount(1000) chains on the returned IChannelConfig;
    // AttributeKey.newInstance(UUID.randomUUID().toString()) is AttributeKey<string>.NewInstance of a new Guid's text.
    [Fact]
    public void SettingChannelOptsAndAttrs()
    {
        AttributeKey<string> key = AttributeKey<string>.NewInstance(Guid.NewGuid().ToString());

        IChannel childChannel = NewOutboundStream(new ChannelInboundHandlerAdapter());
        childChannel.Config.SetAutoRead(false).SetWriteSpinCount(1000);
        childChannel.Attr(key).Set("bar");
        Assert.False(childChannel.Config.IsAutoRead());
        Assert.Equal(1000, childChannel.Config.GetWriteSpinCount());
        Assert.Equal("bar", childChannel.Attr(key).Get());
    }

    [Fact]
    public void OutboundFlowControlWritability()
    {
        IHttp2StreamChannel childChannel = NewOutboundStream(new ChannelInboundHandlerAdapter());
        Assert.True(childChannel.IsActive);

        Assert.True(childChannel.IsWritable);
        _ = childChannel.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(new DefaultHttp2Headers()),
                TestContext.Current.CancellationToken);
        parentChannel.Flush();

        // Test for initial window size
        Assert.True(initialRemoteStreamWindow < childChannel.Config.GetWriteBufferHighWaterMark());

        Assert.True(childChannel.IsWritable);
        _ = childChannel.WriteAsync(new DefaultHttp2DataFrame(Unpooled.Buffer().WriteZero(16 * 1024 * 1024)),
                TestContext.Current.CancellationToken);
        Assert.Equal(0L, childChannel.BytesBeforeUnwritable);
        Assert.False(childChannel.IsWritable);
    }

    // CLR: addMessage(msg, size, parentChannel.voidPromise()) discards the Task of AddMessage(msg, size)
    // (docs/transport-porting.md, Not-ported Transport sources: VoidChannelPromise).
    [Fact]
    public void WritabilityOfParentIsRespected()
    {
        IHttp2StreamChannel childChannel = NewOutboundStream(new ChannelInboundHandlerAdapter());
        childChannel.Config.SetWriteBufferWaterMark(new WriteBufferWaterMark(2048, 4096));
        parentChannel.Config.SetWriteBufferWaterMark(new WriteBufferWaterMark(256, 512));
        Assert.True(childChannel.IsWritable);
        Assert.True(parentChannel.IsActive);

        _ = childChannel.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(new DefaultHttp2Headers()),
                TestContext.Current.CancellationToken);
        parentChannel.Flush();

        Assert.True(childChannel.IsWritable);
        _ = childChannel.WriteAsync(new DefaultHttp2DataFrame(Unpooled.Buffer().WriteZero(256)),
                TestContext.Current.CancellationToken);
        Assert.True(childChannel.IsWritable);
        _ = childChannel.WriteAndFlushAsync(new DefaultHttp2DataFrame(Unpooled.Buffer().WriteZero(512)),
                TestContext.Current.CancellationToken);

        long bytesBeforeUnwritable = childChannel.BytesBeforeUnwritable;
        Assert.NotEqual(0L, bytesBeforeUnwritable);
        // Add something to the ChannelOutboundBuffer of the parent to simulate queuing in the parents channel buffer
        // and verify that this only affect the writability of the parent channel while the child stays writable
        // until it used all of its credits.
        _ = parentChannel.OutboundBuffer.AddMessage(
                Unpooled.Buffer().WriteZero(800), 800, TestContext.Current.CancellationToken);
        Assert.False(parentChannel.IsWritable);

        Assert.True(childChannel.IsWritable);
        Assert.Equal(4097L, childChannel.BytesBeforeUnwritable);

        // Flush everything which simulate writing everything to the socket.
        parentChannel.Flush();
        Assert.True(parentChannel.IsWritable);
        Assert.True(childChannel.IsWritable);
        Assert.Equal(bytesBeforeUnwritable, childChannel.BytesBeforeUnwritable);

        Task future = childChannel.WriteAndFlushAsync(new DefaultHttp2DataFrame(
                Unpooled.Buffer().WriteZero((int) bytesBeforeUnwritable)), TestContext.Current.CancellationToken);
        Assert.False(childChannel.IsWritable);
        Assert.True(parentChannel.IsWritable);

        parentChannel.Flush();
        Assert.False(future.IsCompleted);
        Assert.True(parentChannel.IsWritable);
        Assert.False(childChannel.IsWritable);

        // Now write an window update frame for the stream which then should ensure we will flush the bytes that were
        // queued in the RemoteFlowController before for the stream.
        frameInboundWriter.WriteInboundWindowUpdate(childChannel.Stream().Id(), (int) bytesBeforeUnwritable);
        Assert.True(childChannel.IsWritable);
        Assert.True(future.IsCompleted);
    }

    [Fact]
    public async Task ChannelClosedWhenInactiveFired()
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        IHttp2StreamChannel childChannel = NewInboundStream(3, false, inboundHandler);

        AtomicBoolean channelOpen = new AtomicBoolean(false);
        AtomicBoolean channelActive = new AtomicBoolean(false);
        Assert.True(childChannel.IsOpen);
        Assert.True(childChannel.IsActive);

        childChannel.Pipeline.AddLast(new InactiveStateRecorder(channelOpen, channelActive));

        await childChannel.CloseAsync(TestContext.Current.CancellationToken);
        Assert.False(channelOpen.Get());
        Assert.False(channelActive.Get());
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of channelClosedWhenInactiveFired.
    private sealed class InactiveStateRecorder : ChannelInboundHandlerAdapter
    {
        private readonly AtomicBoolean channelOpen;
        private readonly AtomicBoolean channelActive;

        internal InactiveStateRecorder(AtomicBoolean channelOpen, AtomicBoolean channelActive)
        {
            this.channelOpen = channelOpen;
            this.channelActive = channelActive;
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            channelOpen.Set(ctx.Channel.IsOpen);
            channelActive.Set(ctx.Channel.IsActive);

            base.ChannelInactive(ctx);
        }
    }

    [Fact]
    public void ChannelInactiveHappensAfterExceptionCaughtEvents()
    {
        AtomicInteger count = new AtomicInteger(0);
        AtomicInteger exceptionCaught = new AtomicInteger(-1);
        AtomicInteger channelInactive = new AtomicInteger(-1);
        AtomicInteger channelUnregistered = new AtomicInteger(-1);
        IHttp2StreamChannel childChannel = NewOutboundStream(new CloseAndThrowOnUserEventHandler());

        childChannel.Pipeline.AddLast(new EventOrderRecorder(count, exceptionCaught, channelInactive,
                channelUnregistered));

        childChannel.Pipeline.FireUserEventTriggered(new object());
        parentChannel.RunPendingTasks();

        // The events should have happened in this order because the inactive and deregistration events
        // get deferred as they do in the AbstractChannel.
        Assert.Equal(0, exceptionCaught.Get());
        Assert.Equal(1, channelInactive.Get());
        Assert.Equal(2, channelUnregistered.Get());
    }

    // CLR: the first anonymous ChannelInboundHandlerAdapter of channelInactiveHappensAfterExceptionCaughtEvents;
    // ctx.close() discards the Task.
    private sealed class CloseAndThrowOnUserEventHandler : ChannelInboundHandlerAdapter
    {
        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            _ = ctx.CloseAsync();
            throw new Exception("exception");
        }
    }

    // CLR: the second anonymous ChannelInboundHandlerAdapter of channelInactiveHappensAfterExceptionCaughtEvents.
    private sealed class EventOrderRecorder : ChannelInboundHandlerAdapter
    {
        private readonly AtomicInteger count;
        private readonly AtomicInteger exceptionCaught;
        private readonly AtomicInteger channelInactive;
        private readonly AtomicInteger channelUnregistered;

        internal EventOrderRecorder(AtomicInteger count, AtomicInteger exceptionCaught, AtomicInteger channelInactive,
                                    AtomicInteger channelUnregistered)
        {
            this.count = count;
            this.exceptionCaught = exceptionCaught;
            this.channelInactive = channelInactive;
            this.channelUnregistered = channelUnregistered;
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            channelInactive.Set(count.GetAndIncrement());
            base.ChannelInactive(ctx);
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            exceptionCaught.Set(count.GetAndIncrement());
            base.ExceptionCaught(ctx, cause);
        }

        public override void ChannelUnregistered(IChannelHandlerContext ctx)
        {
            channelUnregistered.Set(count.GetAndIncrement());
            base.ChannelUnregistered(ctx);
        }
    }

    // CLR: childChannel.unsafe() is the internal Unsafe() of AbstractHttp2StreamChannel, voidPromise() a promise nobody
    // observes and newPromise() a TaskCompletionSource (see the type comment).
    [Fact]
    public async Task CallUnsafeCloseMultipleTimes()
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        IHttp2StreamChannel childChannel = NewInboundStream(3, false, inboundHandler);
        ((AbstractHttp2StreamChannel) childChannel).Unsafe().Close(Http2CodecUtil.NewPromise());

        TaskCompletionSource promise = new TaskCompletionSource();
        ((AbstractHttp2StreamChannel) childChannel).Unsafe().Close(promise);
        await promise.Task;
        await childChannel.CloseFuture;
    }

    [Fact]
    public async Task EndOfStreamDoesNotDiscardData()
    {
        AtomicInteger numReads = new AtomicInteger(1);
        AtomicBoolean shouldDisableAutoRead = new AtomicBoolean();
        LastInboundHandler.IConsumer<IChannelHandlerContext> ctxConsumer =
                new DisableAutoReadConsumer(shouldDisableAutoRead, null);
        LastInboundHandler inboundHandler = new LastInboundHandler(ctxConsumer);
        IHttp2StreamChannel childChannel = NewInboundStream(3, false, numReads, inboundHandler);
        childChannel.Config.SetAutoRead(false);

        IHttp2DataFrame dataFrame1 = new DefaultHttp2DataFrame(Bb("1")).Stream(childChannel.Stream());
        IHttp2DataFrame dataFrame2 = new DefaultHttp2DataFrame(Bb("2")).Stream(childChannel.Stream());
        IHttp2DataFrame dataFrame3 = new DefaultHttp2DataFrame(Bb("3")).Stream(childChannel.Stream());
        IHttp2DataFrame dataFrame4 = new DefaultHttp2DataFrame(Bb("4")).Stream(childChannel.Stream());

        Assert.Equal<object>(new DefaultHttp2HeadersFrame(request).Stream(childChannel.Stream()),
                inboundHandler.ReadInbound<object>());

        IChannelHandler readCompleteSupressHandler = new ReadCompleteSuppressingHandler();

        parentChannel.Pipeline.AddFirst(readCompleteSupressHandler);
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("1"), 0, false);

        AssertEqualsAndRelease(dataFrame1, inboundHandler.ReadInbound<IHttp2DataFrame>());

        // Deliver frames, and then a stream closed while read is inactive.
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("2"), 0, false);
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("3"), 0, false);
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("4"), 0, false);

        shouldDisableAutoRead.Set(true);
        childChannel.Config.SetAutoRead(true);
        numReads.Set(1);

        frameInboundWriter.WriteInboundRstStream(childChannel.Stream().Id(), NO_ERROR.Code());

        // Detecting EOS should flush all pending data regardless of read calls.
        AssertEqualsAndRelease(dataFrame2, inboundHandler.ReadInbound<IHttp2DataFrame>());
        Assert.Null(inboundHandler.ReadInbound<object>());

        // As we limited the number to 1 we also need to call read() again.
        childChannel.Read();

        AssertEqualsAndRelease(dataFrame3, inboundHandler.ReadInbound<IHttp2DataFrame>());
        AssertEqualsAndRelease(dataFrame4, inboundHandler.ReadInbound<IHttp2DataFrame>());

        IHttp2ResetFrame resetFrame = UseUserEventForResetFrame() ? inboundHandler.ReadUserEvent<IHttp2ResetFrame>() :
                inboundHandler.ReadInbound<IHttp2ResetFrame>();

        Assert.Equal(childChannel.Stream(), resetFrame.Stream());
        Assert.Equal(NO_ERROR.Code(), resetFrame.ErrorCode());

        Assert.Null(inboundHandler.ReadInbound<object>());

        // Now we want to call channelReadComplete and simulate the end of the read loop.
        parentChannel.Pipeline.Remove(readCompleteSupressHandler);
        parentChannel.FlushInbound();

        await childChannel.CloseFuture;
    }

    // CLR: the anonymous Consumer of endOfStreamDoesNotDiscardData (no counter) and of the two
    // childQueueIsDrainedAndNewDataIsDispatchedInParentReadLoop tests (with channelReadCompleteCount).
    private sealed class DisableAutoReadConsumer : LastInboundHandler.IConsumer<IChannelHandlerContext>
    {
        private readonly AtomicBoolean shouldDisableAutoRead;
        private readonly AtomicInteger channelReadCompleteCount;

        internal DisableAutoReadConsumer(AtomicBoolean shouldDisableAutoRead, AtomicInteger channelReadCompleteCount)
        {
            this.shouldDisableAutoRead = shouldDisableAutoRead;
            this.channelReadCompleteCount = channelReadCompleteCount;
        }

        public void Accept(IChannelHandlerContext obj)
        {
            if (channelReadCompleteCount != null)
            {
                channelReadCompleteCount.IncrementAndGet();
            }

            if (shouldDisableAutoRead.Get())
            {
                obj.Channel.Config.SetAutoRead(false);
            }
        }
    }

    // CLR: the anonymous readCompleteSupressHandler of endOfStreamDoesNotDiscardData and of the two
    // childQueueIsDrainedAndNewDataIsDispatchedInParentReadLoop tests (identical bodies).
    private sealed class ReadCompleteSuppressingHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            // We want to simulate the parent channel calling channelRead and delay calling channelReadComplete.
        }
    }

    protected abstract bool UseUserEventForPriorityFrame();

    protected abstract bool UseUserEventForResetFrame();

    protected abstract bool IgnoreWindowUpdateFrames();

    [Fact]
    public async Task WindowUpdateFrames()
    {
        AtomicInteger numReads = new AtomicInteger(1);
        LastInboundHandler inboundHandler = new LastInboundHandler();
        IHttp2StreamChannel childChannel = NewInboundStream(3, false, numReads, inboundHandler);

        Assert.Equal<object>(new DefaultHttp2HeadersFrame(request).Stream(childChannel.Stream()),
                inboundHandler.ReadInbound<object>());

        frameInboundWriter.WriteInboundWindowUpdate(childChannel.Stream().Id(), 4);

        IHttp2WindowUpdateFrame updateFrame = inboundHandler.ReadInbound<IHttp2WindowUpdateFrame>();

        if (IgnoreWindowUpdateFrames())
        {
            Assert.Null(updateFrame);
        }
        else
        {
            Assert.Equal<object>(new DefaultHttp2WindowUpdateFrame(4).Stream(childChannel.Stream()), updateFrame);
        }

        frameInboundWriter.WriteInboundWindowUpdate(Http2CodecUtil.CONNECTION_STREAM_ID, 6);

        Assert.Null(parentChannel.ReadInbound<object>());
        await childChannel.CloseAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void ChildQueueIsDrainedAndNewDataIsDispatchedInParentReadLoopAutoRead()
    {
        AtomicInteger numReads = new AtomicInteger(1);
        AtomicInteger channelReadCompleteCount = new AtomicInteger(0);
        AtomicBoolean shouldDisableAutoRead = new AtomicBoolean();
        LastInboundHandler.IConsumer<IChannelHandlerContext> ctxConsumer =
                new DisableAutoReadConsumer(shouldDisableAutoRead, channelReadCompleteCount);
        LastInboundHandler inboundHandler = new LastInboundHandler(ctxConsumer);
        IHttp2StreamChannel childChannel = NewInboundStream(3, false, numReads, inboundHandler);
        childChannel.Config.SetAutoRead(false);

        IHttp2DataFrame dataFrame1 = new DefaultHttp2DataFrame(Bb("1")).Stream(childChannel.Stream());
        IHttp2DataFrame dataFrame2 = new DefaultHttp2DataFrame(Bb("2")).Stream(childChannel.Stream());
        IHttp2DataFrame dataFrame3 = new DefaultHttp2DataFrame(Bb("3")).Stream(childChannel.Stream());
        IHttp2DataFrame dataFrame4 = new DefaultHttp2DataFrame(Bb("4")).Stream(childChannel.Stream());

        Assert.Equal<object>(new DefaultHttp2HeadersFrame(request).Stream(childChannel.Stream()),
                inboundHandler.ReadInbound<object>());

        IChannelHandler readCompleteSupressHandler = new ReadCompleteSuppressingHandler();
        parentChannel.Pipeline.AddFirst(readCompleteSupressHandler);

        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("1"), 0, false);

        AssertEqualsAndRelease(dataFrame1, inboundHandler.ReadInbound<IHttp2DataFrame>());

        // We want one item to be in the queue, and allow the numReads to be larger than 1. This will ensure that
        // when beginRead() is called the child channel is added to the readPending queue of the parent channel.
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("2"), 0, false);

        numReads.Set(10);
        shouldDisableAutoRead.Set(true);
        childChannel.Config.SetAutoRead(true);

        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("3"), 0, false);
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("4"), 0, false);

        // Detecting EOS should flush all pending data regardless of read calls.
        AssertEqualsAndRelease(dataFrame2, inboundHandler.ReadInbound<IHttp2DataFrame>());
        AssertEqualsAndRelease(dataFrame3, inboundHandler.ReadInbound<IHttp2DataFrame>());
        AssertEqualsAndRelease(dataFrame4, inboundHandler.ReadInbound<IHttp2DataFrame>());

        Assert.Null(inboundHandler.ReadInbound<object>());

        // Now we want to call channelReadComplete and simulate the end of the read loop.
        parentChannel.Pipeline.Remove(readCompleteSupressHandler);
        parentChannel.FlushInbound();

        // 3 = 1 for initialization + 1 for read when auto read was off + 1 for when auto read was back on
        Assert.Equal(3, channelReadCompleteCount.Get());
    }

    [Fact]
    public void ChildQueueIsDrainedAndNewDataIsDispatchedInParentReadLoopNoAutoRead()
    {
        AtomicInteger numReads = new AtomicInteger(1);
        AtomicInteger channelReadCompleteCount = new AtomicInteger(0);
        AtomicBoolean shouldDisableAutoRead = new AtomicBoolean();
        LastInboundHandler.IConsumer<IChannelHandlerContext> ctxConsumer =
                new DisableAutoReadConsumer(shouldDisableAutoRead, channelReadCompleteCount);
        LastInboundHandler inboundHandler = new LastInboundHandler(ctxConsumer);
        IHttp2StreamChannel childChannel = NewInboundStream(3, false, numReads, inboundHandler);
        childChannel.Config.SetAutoRead(false);

        IHttp2DataFrame dataFrame1 = new DefaultHttp2DataFrame(Bb("1")).Stream(childChannel.Stream());
        IHttp2DataFrame dataFrame2 = new DefaultHttp2DataFrame(Bb("2")).Stream(childChannel.Stream());
        IHttp2DataFrame dataFrame3 = new DefaultHttp2DataFrame(Bb("3")).Stream(childChannel.Stream());
        IHttp2DataFrame dataFrame4 = new DefaultHttp2DataFrame(Bb("4")).Stream(childChannel.Stream());

        Assert.Equal<object>(new DefaultHttp2HeadersFrame(request).Stream(childChannel.Stream()),
                inboundHandler.ReadInbound<object>());

        IChannelHandler readCompleteSupressHandler = new ReadCompleteSuppressingHandler();
        parentChannel.Pipeline.AddFirst(readCompleteSupressHandler);

        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("1"), 0, false);

        AssertEqualsAndRelease(dataFrame1, inboundHandler.ReadInbound<IHttp2Frame>());

        // We want one item to be in the queue, and allow the numReads to be larger than 1. This will ensure that
        // when beginRead() is called the child channel is added to the readPending queue of the parent channel.
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("2"), 0, false);

        numReads.Set(2);
        childChannel.Read();

        AssertEqualsAndRelease(dataFrame2, inboundHandler.ReadInbound<IHttp2Frame>());

        Assert.Null(inboundHandler.ReadInbound<object>());

        // This is the second item that was read, this should be the last until we call read() again. This should also
        // notify of readComplete().
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("3"), 0, false);

        AssertEqualsAndRelease(dataFrame3, inboundHandler.ReadInbound<IHttp2Frame>());

        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("4"), 0, false);
        Assert.Null(inboundHandler.ReadInbound<object>());

        childChannel.Read();

        AssertEqualsAndRelease(dataFrame4, inboundHandler.ReadInbound<IHttp2Frame>());

        Assert.Null(inboundHandler.ReadInbound<object>());

        // Now we want to call channelReadComplete and simulate the end of the read loop.
        parentChannel.Pipeline.Remove(readCompleteSupressHandler);
        parentChannel.FlushInbound();

        // 3 = 1 for initialization + 1 for first read of 2 items + 1 for second read of 2 items +
        // 1 for parent channel readComplete
        Assert.Equal(4, channelReadCompleteCount.Get());
    }

    [Fact]
    public void UseReadWithoutAutoReadInRead()
    {
        UseReadWithoutAutoReadBuffered(false);
    }

    [Fact]
    public void UseReadWithoutAutoReadInReadComplete()
    {
        UseReadWithoutAutoReadBuffered(true);
    }

    private void UseReadWithoutAutoReadBuffered(bool triggerOnReadComplete)
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        IHttp2StreamChannel childChannel = NewInboundStream(3, false, inboundHandler);
        Assert.True(childChannel.Config.IsAutoRead());
        childChannel.Config.SetAutoRead(false);
        Assert.False(childChannel.Config.IsAutoRead());

        IHttp2HeadersFrame headersFrame = inboundHandler.ReadInbound<IHttp2HeadersFrame>();
        Assert.NotNull(headersFrame);

        // Write some bytes to get the channel into the idle state with buffered data and also verify we
        // do not dispatch it until we receive a read() call.
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("hello world"), 0, false);
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("foo"), 0, false);
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("bar"), 0, false);

        // Add a handler which will request reads.
        childChannel.Pipeline.AddFirst(new DoubleReadRequestingHandler(triggerOnReadComplete));

        inboundHandler.Channel().Read();

        VerifyFramesMultiplexedToCorrectChannel(childChannel, inboundHandler, 3);

        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("hello world2"), 0, false);
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("foo2"), 0, false);
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb("bar2"), 0, true);

        VerifyFramesMultiplexedToCorrectChannel(childChannel, inboundHandler, 3);
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of useReadWithoutAutoReadBuffered.
    private sealed class DoubleReadRequestingHandler : ChannelInboundHandlerAdapter
    {
        private readonly bool triggerOnReadComplete;

        internal DoubleReadRequestingHandler(bool triggerOnReadComplete)
        {
            this.triggerOnReadComplete = triggerOnReadComplete;
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            base.ChannelReadComplete(ctx);

            if (triggerOnReadComplete)
            {
                ctx.Read();
                ctx.Read();
            }
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ctx.FireChannelRead(msg);

            if (!triggerOnReadComplete)
            {
                ctx.Read();
                ctx.Read();
            }
        }
    }

    private sealed class FlushSniffer : ChannelOutboundHandlerAdapter
    {

        private bool didFlush;

        public bool CheckFlush()
        {
            bool r = didFlush;
            didFlush = false;
            return r;
        }

        public override void Flush(IChannelHandlerContext ctx)
        {
            didFlush = true;
            base.Flush(ctx);
        }
    }

    [Fact]
    public void WindowUpdatesAreFlushed()
    {
        WindowUpdatesAreFlushed0(true);
    }

    [Fact]
    public void WindowUpdatesNotDoneAutomatically()
    {
        WindowUpdatesAreFlushed0(false);
    }

    // CLR: see HeaderMultipleContentLengthValidationShouldPropagate0 (xUnit1024). f.isSuccess() is
    // IsCompletedSuccessfully and f.cause() Http2CodecUtil.Cause.
    private void WindowUpdatesAreFlushed0(bool autoWriteWindowUpdateFrames)
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        FlushSniffer flushSniffer = new FlushSniffer();
        parentChannel.Pipeline.AddFirst(flushSniffer);

        IHttp2StreamChannel childChannel = NewInboundStream(3, false, inboundHandler);
        childChannel.Config.SetOption(
                Http2StreamChannelOption.AUTO_STREAM_FLOW_CONTROL, autoWriteWindowUpdateFrames);

        Assert.True(childChannel.Config.IsAutoRead());
        childChannel.Config.SetAutoRead(false);
        Assert.False(childChannel.Config.IsAutoRead());

        IHttp2HeadersFrame headersFrame = inboundHandler.ReadInbound<IHttp2HeadersFrame>();
        Assert.NotNull(headersFrame);

        Assert.True(flushSniffer.CheckFlush());

        // Write some bytes to get the channel into the idle state with buffered data and also verify we
        // do not dispatch it until we receive a read() call.
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb(16 * 1024), 0, false);
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb(16 * 1024), 0, false);
        Assert.True(flushSniffer.CheckFlush());

        Mock.Get(frameWriter).Verify(w => w.WriteWindowUpdateAsync(EqCodecCtx(), It.IsAny<int>(), It.IsAny<int>(),
                AnyChannelPromise()), Times.Never());
        // only the first one was read because it was legacy auto-read behavior.
        VerifyFramesMultiplexedToCorrectChannel(childChannel, inboundHandler, 1);
        Assert.False(flushSniffer.CheckFlush());

        // Trigger a read of the second frame.
        childChannel.Read();
        VerifyFramesMultiplexedToCorrectChannel(childChannel, inboundHandler, 1);

        if (autoWriteWindowUpdateFrames)
        {
            // We expect a flush here because the StreamChannel will flush the smaller increment but the
            // connection will collect the bytes and decide not to send a wire level frame until more are consumed.
            Assert.True(flushSniffer.CheckFlush());
        }
        else
        {
            Assert.False(flushSniffer.CheckFlush());
        }

        Mock.Get(frameWriter).Verify(w => w.WriteWindowUpdateAsync(EqCodecCtx(), It.IsAny<int>(), It.IsAny<int>(),
                AnyChannelPromise()), Times.Never());

        // Call read one more time which should trigger the writing of the flow control update.
        childChannel.Read();

        if (autoWriteWindowUpdateFrames)
        {
            Mock.Get(frameWriter).Verify(w => w.WriteWindowUpdateAsync(EqCodecCtx(), 0, 32 * 1024,
                    AnyChannelPromise()), Times.Once());
            Mock.Get(frameWriter).Verify(w => w.WriteWindowUpdateAsync(
                    EqCodecCtx(), childChannel.Stream().Id(), 32 * 1024, AnyChannelPromise()), Times.Once());
            Assert.True(flushSniffer.CheckFlush());
        }
        else
        {
            Mock.Get(frameWriter).Verify(w => w.WriteWindowUpdateAsync(EqCodecCtx(), It.IsAny<int>(),
                    It.IsAny<int>(), AnyChannelPromise()), Times.Never());
            Assert.False(flushSniffer.CheckFlush());

            // Let's manually send a window update frame now.
            Task f = childChannel.WriteAndFlushAsync(new DefaultHttp2WindowUpdateFrame(32 * 1024)
                    .Stream(childChannel.Stream()));
            Assert.True(f.IsCompletedSuccessfully);
            Mock.Get(frameWriter).Verify(w => w.WriteWindowUpdateAsync(EqCodecCtx(), 0, 32 * 1024,
                    AnyChannelPromise()), Times.Once());
            Mock.Get(frameWriter).Verify(w => w.WriteWindowUpdateAsync(
                    EqCodecCtx(), childChannel.Stream().Id(), 32 * 1024, AnyChannelPromise()), Times.Once());
            Assert.True(flushSniffer.CheckFlush());

            // Let's try to send one more even though there are no more pending bytes
            f = childChannel.WriteAndFlushAsync(new DefaultHttp2WindowUpdateFrame(32 * 1024)
                    .Stream(childChannel.Stream()));
            Assert.NotNull(Http2CodecUtil.Cause(f));
        }
    }

    [Fact]
    public void WindowUpdatesSendWhenAutoReadEnabled()
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        FlushSniffer flushSniffer = new FlushSniffer();
        parentChannel.Pipeline.AddFirst(flushSniffer);

        IHttp2StreamChannel childChannel = NewInboundStream(3, false, inboundHandler);
        childChannel.Config.SetOption(
                Http2StreamChannelOption.AUTO_STREAM_FLOW_CONTROL, false);

        Assert.True(childChannel.Config.IsAutoRead());
        childChannel.Config.SetAutoRead(false);
        Assert.False(childChannel.Config.IsAutoRead());

        IHttp2HeadersFrame headersFrame = inboundHandler.ReadInbound<IHttp2HeadersFrame>();
        Assert.NotNull(headersFrame);

        Assert.True(flushSniffer.CheckFlush());

        // Write some bytes to get the channel into the idle state with buffered data and also verify we
        // do not dispatch it until we receive a read() call.
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb(16 * 1024), 0, false);
        frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb(16 * 1024), 0, false);
        Assert.True(flushSniffer.CheckFlush());

        Mock.Get(frameWriter).Verify(w => w.WriteWindowUpdateAsync(EqCodecCtx(), It.IsAny<int>(), It.IsAny<int>(),
                AnyChannelPromise()), Times.Never());
        // only the first one was read because it was legacy auto-read behavior.
        VerifyFramesMultiplexedToCorrectChannel(childChannel, inboundHandler, 1);
        Assert.False(flushSniffer.CheckFlush());

        // Trigger a read of the second frame.
        childChannel.Read();
        VerifyFramesMultiplexedToCorrectChannel(childChannel, inboundHandler, 1);
        Assert.False(flushSniffer.CheckFlush());

        Mock.Get(frameWriter).Verify(w => w.WriteWindowUpdateAsync(EqCodecCtx(), It.IsAny<int>(), It.IsAny<int>(),
                AnyChannelPromise()), Times.Never());

        childChannel.Read();

        Mock.Get(frameWriter).Verify(w => w.WriteWindowUpdateAsync(EqCodecCtx(), It.IsAny<int>(), It.IsAny<int>(),
                AnyChannelPromise()), Times.Never());
        Assert.False(flushSniffer.CheckFlush());

        childChannel.Config.SetOption(
                Http2StreamChannelOption.AUTO_STREAM_FLOW_CONTROL, true);
        Mock.Get(frameWriter).Verify(w => w.WriteWindowUpdateAsync(EqCodecCtx(), 0, 32 * 1024, AnyChannelPromise()),
                Times.Once());
        Mock.Get(frameWriter).Verify(w => w.WriteWindowUpdateAsync(
                EqCodecCtx(), childChannel.Stream().Id(), 32 * 1024, AnyChannelPromise()), Times.Once());
        Assert.True(flushSniffer.CheckFlush());
    }

    // CLR: flushSniffer.checkFlush() whose result Java ignores is a discard.
    [Fact]
    public void WindowUpdateFlushedWhenReadRequestedWhileReadInProgress()
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        FlushSniffer flushSniffer = new FlushSniffer();
        parentChannel.Pipeline.AddFirst(flushSniffer);

        IHttp2StreamChannel childChannel = NewInboundStream(3, false, inboundHandler);
        Assert.True(childChannel.Config.IsAutoRead());
        IHttp2HeadersFrame headersFrame = inboundHandler.ReadInbound<IHttp2HeadersFrame>();
        Assert.NotNull(headersFrame);

        // Each round: a read() is requested while the child is waiting for data (the same thing a
        // setAutoRead(false) -> setAutoRead(true) flip does), auto-read is disabled again, and then a DATA frame
        // arrives. The frame is still delivered because a read was pending, but its bytes are not returned yet.
        for (int i = 0; i < 2; i++)
        {
            childChannel.Config.SetAutoRead(false);
            childChannel.Config.SetAutoRead(true);
            childChannel.Config.SetAutoRead(false);
            frameInboundWriter.WriteInboundData(childChannel.Stream().Id(), Bb(16 * 1024), 0, false);
            VerifyFramesMultiplexedToCorrectChannel(childChannel, inboundHandler, 1);
        }

        Mock.Get(frameWriter).Verify(w => w.WriteWindowUpdateAsync(EqCodecCtx(), It.IsAny<int>(), It.IsAny<int>(),
                AnyChannelPromise()), Times.Never());
        _ = flushSniffer.CheckFlush();

        // Re-enabling auto-read triggers a read() which returns the consumed bytes to the flow controller. That
        // results in a wire-level WINDOW_UPDATE which must be flushed, otherwise the remote peer may stall forever
        // once its send window is exhausted.
        childChannel.Config.SetAutoRead(true);
        Mock.Get(frameWriter).Verify(w => w.WriteWindowUpdateAsync(EqCodecCtx(), 0, 32 * 1024, AnyChannelPromise()),
                Times.Once());
        Mock.Get(frameWriter).Verify(w => w.WriteWindowUpdateAsync(
                EqCodecCtx(), childChannel.Stream().Id(), 32 * 1024, AnyChannelPromise()), Times.Once());
        Assert.True(flushSniffer.CheckFlush(), "WINDOW_UPDATE was written but never flushed");
    }

    [Fact]
    public void FramesDeliveredReentrantlyWhileFramesQueuedAreNotReordered()
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        IHttp2StreamChannel childChannel = NewInboundStream(3, false, inboundHandler);
        Assert.NotNull(inboundHandler.ReadInbound<object>());
        int streamId = childChannel.Stream().Id();

        // Queue two frames while auto-read is disabled. The first is dispatched immediately as a read was pending.
        childChannel.Config.SetAutoRead(false);
        frameInboundWriter.WriteInboundData(streamId, Bb(1), 0, false);
        AssertDataFrame(inboundHandler, 1, false);
        frameInboundWriter.WriteInboundData(streamId, Bb(2), 0, false);
        frameInboundWriter.WriteInboundData(streamId, Bb(3), 0, false);
        Assert.Null(inboundHandler.ReadInbound<object>());

        // Stop reading again after the first queued frame, and have a new frame arrive re-entrantly (e.g. because a
        // flush synchronously triggered the remote peer to send more) while the read loop is still completing.
        AtomicBoolean armed = new AtomicBoolean(true);
        childChannel.Pipeline.AddFirst(new ReentrantDataWritingHandler(armed, frameInboundWriter, streamId));
        childChannel.Config.SetAutoRead(true);
        AssertDataFrame(inboundHandler, 2, false);
        Assert.Null(inboundHandler.ReadInbound<object>());

        // The re-entrant frame must be delivered after the frame that was already queued.
        childChannel.Read();
        AssertDataFrame(inboundHandler, 3, false);
        childChannel.Read();
        AssertDataFrame(inboundHandler, 4, false);
        Assert.Null(inboundHandler.ReadInbound<object>());
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of framesDeliveredReentrantlyWhileFramesQueuedAreNotReordered.
    // armed.getAndSet(false) is the Common AtomicBoolean.Set(false), which returns the previous value
    // (Interlocked.Exchange, src/Netty.NET.Common/Concurrent/AtomicBoolean.cs).
    private sealed class ReentrantDataWritingHandler : ChannelInboundHandlerAdapter
    {
        private readonly AtomicBoolean armed;
        private readonly Http2FrameInboundWriter frameInboundWriter;
        private readonly int streamId;

        internal ReentrantDataWritingHandler(AtomicBoolean armed, Http2FrameInboundWriter frameInboundWriter,
                                             int streamId)
        {
            this.armed = armed;
            this.frameInboundWriter = frameInboundWriter;
            this.streamId = streamId;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ctx.Channel.Config.SetAutoRead(false);
            ctx.FireChannelRead(msg);
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            if (armed.Set(false))
            {
                frameInboundWriter.WriteInboundData(streamId, Bb(4), 0, false);
            }

            ctx.FireChannelReadComplete();
        }
    }

    [Fact]
    public void EndOfStreamDeliveredReentrantlyDoesNotDropQueuedFrames()
    {
        LastInboundHandler inboundHandler = new LastInboundHandler();
        IHttp2StreamChannel childChannel = NewInboundStream(3, false, inboundHandler);
        Assert.NotNull(inboundHandler.ReadInbound<object>());
        int streamId = childChannel.Stream().Id();
        // Half-close our side so that receiving END_STREAM fully closes the stream.
        Assert.True(childChannel.WriteAndFlushAsync(
                new DefaultHttp2HeadersFrame(new DefaultHttp2Headers(), true),
                TestContext.Current.CancellationToken).IsCompletedSuccessfully);

        childChannel.Config.SetAutoRead(false);
        frameInboundWriter.WriteInboundData(streamId, Bb(1), 0, false);
        AssertDataFrame(inboundHandler, 1, false);
        frameInboundWriter.WriteInboundData(streamId, Bb(2), 0, false);
        Assert.Null(inboundHandler.ReadInbound<object>());

        // While the read loop completes, stop reading and have the rest of the stream (including END_STREAM)
        // arrive re-entrantly. The last frame gets queued and the stream is closed at the HTTP/2 level.
        AtomicBoolean armed = new AtomicBoolean(true);
        childChannel.Pipeline.AddFirst(new ReentrantEndStreamWritingHandler(armed, frameInboundWriter, streamId));
        childChannel.Config.SetAutoRead(true);
        AssertDataFrame(inboundHandler, 2, false);
        AssertDataFrame(inboundHandler, 3, false);
        Assert.Null(inboundHandler.ReadInbound<object>());

        // The queued END_STREAM frame must not be dropped by closing the channel early.
        Assert.True(childChannel.IsActive);
        childChannel.Read();
        AssertDataFrame(inboundHandler, 4, true);
        Assert.False(childChannel.IsActive);
    }

    // CLR: the anonymous ChannelInboundHandlerAdapter of endOfStreamDeliveredReentrantlyDoesNotDropQueuedFrames;
    // armed.getAndSet(false) is AtomicBoolean.Set(false) (see ReentrantDataWritingHandler).
    private sealed class ReentrantEndStreamWritingHandler : ChannelInboundHandlerAdapter
    {
        private readonly AtomicBoolean armed;
        private readonly Http2FrameInboundWriter frameInboundWriter;
        private readonly int streamId;

        internal ReentrantEndStreamWritingHandler(AtomicBoolean armed, Http2FrameInboundWriter frameInboundWriter,
                                                  int streamId)
        {
            this.armed = armed;
            this.frameInboundWriter = frameInboundWriter;
            this.streamId = streamId;
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            if (armed.Set(false))
            {
                ctx.Channel.Config.SetAutoRead(false);
                frameInboundWriter.WriteInboundData(streamId, Bb(3), 0, false);
                frameInboundWriter.WriteInboundData(streamId, Bb(4), 0, true);
            }

            ctx.FireChannelReadComplete();
        }
    }

    // CLR: content().readableBytes() is Content.ReadableBytes and release() Release().
    private static void AssertDataFrame(LastInboundHandler inboundHandler, int expectedBytes, bool endStream)
    {
        IHttp2DataFrame frame = inboundHandler.ReadInbound<IHttp2DataFrame>();
        Assert.NotNull(frame, "expected a DATA frame with " + expectedBytes + " bytes");

        try
        {
            Assert.Equal(expectedBytes, frame.Content.ReadableBytes);
            Assert.Equal(endStream, frame.IsEndStream());
        }
        finally
        {
            frame.Release();
        }
    }

    // CLR: @MethodSource("userEvents") is MemberData of the public static UserEvents(); the events are not
    // serializable, so the theory rows are not enumerated at discovery (DisableDiscoveryEnumeration, as
    // docs/codec-http-porting.md, Core test notes). The display-name pattern has no counterpart.
    [Theory]
    [MemberData(nameof(UserEvents), DisableDiscoveryEnumeration = true)]
    public void UserEventsThatPropagatedToChildChannels(object userEvent)
    {
        LastInboundHandler inboundParentHandler = new LastInboundHandler();
        LastInboundHandler inboundHandler = new LastInboundHandler();
        IHttp2StreamChannel channel = NewInboundStream(3, false, inboundHandler);
        Assert.True(channel.IsActive);
        parentChannel.Pipeline.AddLast(inboundParentHandler);
        parentChannel.Pipeline.FireUserEventTriggered(userEvent);
        Assert.Equal(userEvent, inboundHandler.ReadUserEvent<object>());
        Assert.Equal(userEvent, inboundParentHandler.ReadUserEvent<object>());
        Assert.Null(inboundHandler.ReadUserEvent<object>());
        Assert.Null(inboundParentHandler.ReadUserEvent<object>());
    }

    // CLR: Arrays.asList(...) of the three events, as TheoryData; private static in Java, public here because
    // MemberData "may only point at public members" (https://xunit.net/xunit.analyzers/rules/xUnit1016).
    public static TheoryData<object> UserEvents()
    {
        return new TheoryData<object>
        {
            ChannelInputShutdownReadComplete.INSTANCE,
            ChannelOutputShutdownEvent.INSTANCE,
            SslCloseCompletionEvent.SUCCESS
        };
    }

    private static void VerifyFramesMultiplexedToCorrectChannel(IHttp2StreamChannel streamChannel,
                                                                LastInboundHandler inboundHandler,
                                                                int numFrames)
    {
        for (int i = 0; i < numFrames; i++)
        {
            IHttp2StreamFrame frame = inboundHandler.ReadInbound<IHttp2StreamFrame>();
            Assert.NotNull(frame, i + " out of " + numFrames + " received");
            Assert.Equal(streamChannel.Stream(), frame.Stream());
            Release(frame);
        }

        Assert.Null(inboundHandler.ReadInbound<object>());
    }

    private static int EqStreamId(IHttp2StreamChannel channel)
    {
        return channel.Stream().Id();
    }

    // CLR: future.addListener(listener) on a Task of the stream channel: the continuation runs on Common's
    // EventExecutorTaskScheduler over the channel's event loop, the timing of DefaultPromise.notifyListeners()
    // (docs/codec-http-porting.md, Listener timing), so it runs on the EmbeddedChannel's thread as Java's listener
    // does, and with ExecuteSynchronously "If the antecedent is already complete when the continuation is created, the
    // continuation will run on the thread that creates the continuation"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcontinuationoptions?view=net-10.0).
    private static Task AddListener(IChannel channel, Task future, Action<Task> listener)
    {
        return future.ContinueWith(listener, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                new EventExecutorTaskScheduler(() => channel.EventLoop));
    }
}
