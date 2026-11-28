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
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Handler.Logging;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Tests.Http2TestUtil;
using HttpVersion = Netty.NET.Handler.Codec.Http.HttpVersion;
using State = Netty.NET.Handler.Codec.Http2.IHttp2Stream.State;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Unit tests for {@link Http2FrameCodec}.
 */
// CLR: @BeforeEach is the constructor and @AfterEach Dispose() (xUnit creates a test-class instance per test,
// https://xunit.net/docs/shared-context); setUp()/tearDown() keep their names because some tests call them again,
// and are private because xUnit1013 reports a public non-test method of a test class. The mocked frame writer is
// the shared Http2TestUtil.MockedFrameWriter() (a Moq mock whose setups complete the promise argument);
// verify(frameWriter)/verify(frameWriter, never())/verifyNoMoreInteractions are Mock.Get(frameWriter).Verify(...,
// Times.Once()/Times.Never())/VerifyNoOtherCalls(), eq(x) is the value x (Moq compares it with Equals), same(x) a
// ReferenceEquals predicate, any(ChannelPromise.class)/anyInt()/anyLong() It.IsAny, and an ArgumentCaptor reads
// the argument of the recorded invocation (https://github.com/devlooped/moq/wiki/Quickstart;
// docs/codec-http-porting.md, Shared CLR adaptations). ChannelFuture is Task and ChannelPromise
// TaskCompletionSource; channel.writeAndFlush(msg, channel.newPromise()) is the Task of WriteAndFlushAsync(msg)
// ("The ChannelPromise overloads fold into the Task-returning members", docs/transport-porting.md, Decisions),
// isDone() is IsCompleted, isSuccess() IsCompletedSuccessfully, cause() Http2CodecUtil.Cause and
// syncUninterruptibly() an await, which throws the failure; a test that awaits is async. A ChannelFuture listener
// is a synchronous continuation of the Task: the codec returns the Task of its own promise, which has no
// RunContinuationsAsynchronously (Http2CodecUtil.NewPromise), so the continuation runs at once where the promise
// completes, on the EmbeddedChannel's event loop, as Java's listener does. new InetSocketAddress(0) is an
// IPEndPoint of IPAddress.Any and port 0 (Http2ConnectionHandlerTest). assertThrows is Assert.ThrowsAny,
// assertInstanceOf Assert.IsAssignableFrom and getCause() InnerException.
public class Http2FrameCodecTest : IDisposable
{

    // For verifying outbound frames
    private IHttp2FrameWriter frameWriter;
    private Http2FrameCodec frameCodec;
    private EmbeddedChannel channel;

    // For injecting inbound frames
    private Http2FrameInboundWriter frameInboundWriter;

    private LastInboundHandler inboundHandler;

    private readonly IHttp2Headers request = new DefaultHttp2Headers()
            .Method(HttpMethod.GET.AsciiName()).Scheme(HttpScheme.HTTPS.Name())
            .Authority(new AsciiString("example.org")).Path(new AsciiString("/foo"));
    private readonly IHttp2Headers response = new DefaultHttp2Headers()
            .Status(HttpResponseStatus.OK.CodeAsText());

    public Http2FrameCodecTest()
    {
        SetUp();
    }

    private void SetUp()
    {
        SetUp(Http2FrameCodecBuilder.ForServer(), new Http2Settings());
    }

    public void Dispose()
    {
        TearDown();
    }

    private void TearDown()
    {
        if (inboundHandler != null)
        {
            inboundHandler.FinishAndReleaseAll();
            inboundHandler = null;
        }

        if (channel != null)
        {
            channel.FinishAndReleaseAll();
            _ = channel.CloseAsync();
            channel = null;
        }
    }

    private void SetUp(Http2FrameCodecBuilder frameCodecBuilder, Http2Settings initialRemoteSettings)
    {
        SetUp(frameCodecBuilder, initialRemoteSettings, true);
    }

    private void SetUp(Http2FrameCodecBuilder frameCodecBuilder, Http2Settings initialRemoteSettings, bool preface)
    {
        /*
         * Some tests call this method twice. Once with JUnit's @Before and once directly to pass special settings.
         * This call ensures that in case of two consecutive calls to setUp(), the previous channel is shutdown and
         * ByteBufs are released correctly.
         */
        TearDown();

        frameWriter = Http2TestUtil.MockedFrameWriter();

        frameCodec = frameCodecBuilder.FrameWriter(frameWriter).FrameLogger(new Http2FrameLogger(LogLevel.TRACE))
                .InitialSettings(initialRemoteSettings).Build();
        inboundHandler = new LastInboundHandler();

        channel = new EmbeddedChannel();
        frameInboundWriter = new Http2FrameInboundWriter(channel);
        _ = channel.ConnectAsync(new IPEndPoint(IPAddress.Any, 0));
        channel.Pipeline.AddLast(frameCodec);
        channel.Pipeline.AddLast(inboundHandler);
        channel.Pipeline.FireChannelActive();

        // Handshake
        Mock.Get(frameWriter).Verify(w => w.WriteSettingsAsync(EqFrameCodecCtx(), AnyHttp2Settings(),
                AnyChannelPromise()), Times.Once());
        Mock.Get(frameWriter).VerifyNoOtherCalls();

        if (preface)
        {
            channel.WriteInbound(Http2CodecUtil.ConnectionPrefaceBuf());

            frameInboundWriter.WriteInboundSettings(initialRemoteSettings);

            Mock.Get(frameWriter).Verify(w => w.WriteSettingsAckAsync(EqFrameCodecCtx(), AnyChannelPromise()),
                    Times.Once());

            frameInboundWriter.WriteInboundSettingsAck();

            IHttp2SettingsFrame settingsFrame = inboundHandler.ReadInbound<IHttp2SettingsFrame>();
            Assert.NotNull(settingsFrame);
            IHttp2SettingsAckFrame settingsAckFrame = inboundHandler.ReadInbound<IHttp2SettingsAckFrame>();
            Assert.NotNull(settingsAckFrame);
        }
    }

    [Fact]
    public void StateChanges()
    {
        frameInboundWriter.WriteInboundHeaders(1, request, 31, true);

        IHttp2Stream stream = frameCodec.Connection().Stream(1);
        Assert.NotNull(stream);
        Assert.Equal(State.HALF_CLOSED_REMOTE, stream.State_());

        Http2FrameStreamEvent @event = inboundHandler.ReadInboundMessageOrUserEvent<Http2FrameStreamEvent>();
        Assert.Equal(State.HALF_CLOSED_REMOTE, @event.Stream().State());

        IHttp2StreamFrame inboundFrame = inboundHandler.ReadInbound<IHttp2StreamFrame>();
        IHttp2FrameStream stream2 = inboundFrame.Stream();
        Assert.NotNull(stream2);
        Assert.Equal(1, stream2.Id());
        Assert.Equal<object>(inboundFrame, new DefaultHttp2HeadersFrame(request, true, 31).Stream(stream2));
        Assert.Null(inboundHandler.ReadInbound<object>());

        channel.WriteOutbound(new DefaultHttp2HeadersFrame(response, true, 27).Stream(stream2));
        Mock.Get(frameWriter).Verify(w => w.WriteHeadersAsync(
                EqFrameCodecCtx(), 1, response,
                27, true, AnyChannelPromise()), Times.Once());
        Mock.Get(frameWriter).Verify(w => w.WriteRstStreamAsync(
                EqFrameCodecCtx(), It.IsAny<int>(), It.IsAny<long>(), AnyChannelPromise()), Times.Never());

        Assert.Equal(State.CLOSED, stream.State_());
        @event = inboundHandler.ReadInboundMessageOrUserEvent<Http2FrameStreamEvent>();
        Assert.Equal(State.CLOSED, @event.Stream().State());

        Assert.True(channel.IsActive);
    }

    [Fact]
    public void HeaderRequestHeaderResponse()
    {
        frameInboundWriter.WriteInboundHeaders(1, request, 31, true);

        IHttp2Stream stream = frameCodec.Connection().Stream(1);
        Assert.NotNull(stream);
        Assert.Equal(State.HALF_CLOSED_REMOTE, stream.State_());

        IHttp2StreamFrame inboundFrame = inboundHandler.ReadInbound<IHttp2StreamFrame>();
        IHttp2FrameStream stream2 = inboundFrame.Stream();
        Assert.NotNull(stream2);
        Assert.Equal(1, stream2.Id());
        Assert.Equal<object>(inboundFrame, new DefaultHttp2HeadersFrame(request, true, 31).Stream(stream2));
        Assert.Null(inboundHandler.ReadInbound<object>());

        channel.WriteOutbound(new DefaultHttp2HeadersFrame(response, true, 27).Stream(stream2));
        Mock.Get(frameWriter).Verify(w => w.WriteHeadersAsync(
                EqFrameCodecCtx(), 1, response,
                27, true, AnyChannelPromise()), Times.Once());
        Mock.Get(frameWriter).Verify(w => w.WriteRstStreamAsync(
                EqFrameCodecCtx(), It.IsAny<int>(), It.IsAny<long>(), AnyChannelPromise()), Times.Never());

        Assert.Equal(State.CLOSED, stream.State_());
        Assert.True(channel.IsActive);
    }

    [Fact]
    public void FlowControlShouldBeResilientToMissingStreams()
    {
        IHttp2Connection conn = new DefaultHttp2Connection(true);
        IHttp2ConnectionEncoder enc = new DefaultHttp2ConnectionEncoder(conn, new DefaultHttp2FrameWriter());
        IHttp2ConnectionDecoder dec = new DefaultHttp2ConnectionDecoder(conn, enc, new DefaultHttp2FrameReader());
        Http2FrameCodec codec = new Http2FrameCodec(enc, dec, new Http2Settings(), false, true);
        EmbeddedChannel em = new EmbeddedChannel(codec);

        // We call #consumeBytes on a stream id which has not been seen yet to emulate the case
        // where a stream is deregistered which in reality can happen in response to a RST.
        Assert.False(codec.ConsumeBytes(1, 1));
        Assert.True(em.FinishAndReleaseAll());
    }

    [Fact]
    public void CanCreateCustomUnknownFrame()
    {
        IHttp2Connection conn = new DefaultHttp2Connection(true);
        IHttp2ConnectionEncoder enc = new DefaultHttp2ConnectionEncoder(conn, new DefaultHttp2FrameWriter());
        IHttp2ConnectionDecoder dec = new DefaultHttp2ConnectionDecoder(conn, enc, new DefaultHttp2FrameReader());
        _ = new CustomUnknownFrameCodec(enc, dec, new Http2Settings(), false, true);
    }

    private sealed class CustomUnknownFrameCodec : Http2FrameCodec
    {
        internal CustomUnknownFrameCodec(IHttp2ConnectionEncoder encoder, IHttp2ConnectionDecoder decoder,
                Http2Settings initialSettings, bool decoupleCloseAndGoAway, bool flushPreface)
            : base(encoder, decoder, initialSettings, decoupleCloseAndGoAway, flushPreface)
        {
        }

        protected override IHttp2StreamFrame NewHttp2UnknownFrame(byte frameType,
                                                  int streamId,
                                                  Http2Flags flags,
                                                  ByteBuf payload)
        {
            return base.NewHttp2UnknownFrame(frameType, streamId, flags, payload);
        }
    }

    [Fact]
    public void EntityRequestEntityResponse()
    {
        frameInboundWriter.WriteInboundHeaders(1, request, 0, false);

        IHttp2Stream stream = frameCodec.Connection().Stream(1);
        Assert.NotNull(stream);
        Assert.Equal(State.OPEN, stream.State_());

        IHttp2HeadersFrame inboundHeaders = inboundHandler.ReadInbound<IHttp2HeadersFrame>();
        IHttp2FrameStream stream2 = inboundHeaders.Stream();
        Assert.NotNull(stream2);
        Assert.Equal(1, stream2.Id());
        Assert.Equal<object>(new DefaultHttp2HeadersFrame(request, false).Stream(stream2), inboundHeaders);
        Assert.Null(inboundHandler.ReadInbound<object>());

        ByteBuf hello = Bb("hello");
        frameInboundWriter.WriteInboundData(1, hello, 31, true);
        IHttp2DataFrame inboundData = inboundHandler.ReadInbound<IHttp2DataFrame>();
        IHttp2DataFrame expected = new DefaultHttp2DataFrame(Bb("hello"), true, 31).Stream(stream2);
        AssertEqualsAndRelease(expected, inboundData);

        Assert.Null(inboundHandler.ReadInbound<object>());

        channel.WriteOutbound(new DefaultHttp2HeadersFrame(response, false).Stream(stream2));
        Mock.Get(frameWriter).Verify(w => w.WriteHeadersAsync(EqFrameCodecCtx(), 1, response,
                0, false, AnyChannelPromise()), Times.Once());

        channel.WriteOutbound(new DefaultHttp2DataFrame(Bb("world"), true, 27).Stream(stream2));
        Mock.Get(frameWriter).Verify(w => w.WriteDataAsync(EqFrameCodecCtx(), 1, It.IsAny<ByteBuf>(), 27,
                                      true, AnyChannelPromise()), Times.Once());
        ByteBuf outboundData = CapturedArgument<ByteBuf>(nameof(IHttp2FrameWriter.WriteDataAsync), 2);

        ByteBuf bb = Bb("world");
        Assert.Equal(bb, outboundData);
        Assert.Equal(1, outboundData.RefCnt);
        bb.Release();
        outboundData.Release();

        Mock.Get(frameWriter).Verify(w => w.WriteRstStreamAsync(EqFrameCodecCtx(), It.IsAny<int>(), It.IsAny<long>(),
                AnyChannelPromise()), Times.Never());
        Assert.True(channel.IsActive);
    }

    [Fact]
    public void SendRstStream()
    {
        frameInboundWriter.WriteInboundHeaders(3, request, 31, true);

        IHttp2Stream stream = frameCodec.Connection().Stream(3);
        Assert.NotNull(stream);
        Assert.Equal(State.HALF_CLOSED_REMOTE, stream.State_());

        IHttp2HeadersFrame inboundHeaders = inboundHandler.ReadInbound<IHttp2HeadersFrame>();
        Assert.NotNull(inboundHeaders);
        Assert.True(inboundHeaders.IsEndStream());

        IHttp2FrameStream stream2 = inboundHeaders.Stream();
        Assert.NotNull(stream2);
        Assert.Equal(3, stream2.Id());

        channel.WriteOutbound(new DefaultHttp2ResetFrame(314 /* non-standard error */).Stream(stream2));
        Mock.Get(frameWriter).Verify(w => w.WriteRstStreamAsync(EqFrameCodecCtx(), 3, 314L, AnyChannelPromise()),
                Times.Once());
        Assert.Equal(State.CLOSED, stream.State_());
        Assert.True(channel.IsActive);
    }

    [Fact]
    public void ValidateRequiredPseudoHeadersOptionRejectsMalformedRequest()
    {
        // Verify the builder option is propagated into the decoder end-to-end.
        SetUp(Http2FrameCodecBuilder.ForServer().ValidateRequiredPseudoHeaders(true), new Http2Settings());

        IHttp2Headers malformed = new DefaultHttp2Headers().Method(HttpMethod.GET.AsciiName());
        frameInboundWriter.WriteInboundHeaders(3, malformed, 31, false);

        // The malformed request (no :scheme/:path) must be rejected with a PROTOCOL_ERROR stream error.
        Http2FrameStreamException e = Assert.ThrowsAny<Http2FrameStreamException>(
                () => inboundHandler.CheckException());
        Assert.Equal(Http2Error.PROTOCOL_ERROR, e.Error());
        // It must not be delivered as a HEADERS frame.
        object msg;

        while ((msg = inboundHandler.ReadInboundMessageOrUserEvent<object>()) != null)
        {
            Assert.False(msg is IHttp2HeadersFrame);
            ReferenceCountUtil.Release(msg);
        }
    }

    [Fact]
    public void ReceiveRstStream()
    {
        frameInboundWriter.WriteInboundHeaders(3, request, 31, false);

        IHttp2Stream stream = frameCodec.Connection().Stream(3);
        Assert.NotNull(stream);
        Assert.Equal(State.OPEN, stream.State_());

        IHttp2HeadersFrame expectedHeaders = new DefaultHttp2HeadersFrame(request, false, 31);
        IHttp2HeadersFrame actualHeaders = inboundHandler.ReadInbound<IHttp2HeadersFrame>();
        Assert.Equal<object>(expectedHeaders.Stream(actualHeaders.Stream()), actualHeaders);

        frameInboundWriter.WriteInboundRstStream(3, NO_ERROR.Code());

        IHttp2ResetFrame expectedRst = new DefaultHttp2ResetFrame(NO_ERROR).Stream(actualHeaders.Stream());
        IHttp2ResetFrame actualRst = inboundHandler.ReadInbound<IHttp2ResetFrame>();
        Assert.Equal(expectedRst, actualRst);

        Assert.Null(inboundHandler.ReadInbound<object>());
    }

    [Fact]
    public void SendGoAway()
    {
        frameInboundWriter.WriteInboundHeaders(3, request, 31, false);
        IHttp2Stream stream = frameCodec.Connection().Stream(3);
        Assert.NotNull(stream);
        Assert.Equal(State.OPEN, stream.State_());

        ByteBuf debugData = Bb("debug");
        ByteBuf expected = debugData.Copy();

        IHttp2GoAwayFrame goAwayFrame = new DefaultHttp2GoAwayFrame(NO_ERROR.Code(),
                debugData.RetainedDuplicate());
        goAwayFrame.SetExtraStreamIds(2);

        channel.WriteOutbound(goAwayFrame);
        Mock.Get(frameWriter).Verify(w => w.WriteGoAwayAsync(EqFrameCodecCtx(), 7,
                NO_ERROR.Code(), expected, AnyChannelPromise()), Times.Once());
        Assert.Equal(State.OPEN, stream.State_());
        Assert.True(channel.IsActive);
        expected.Release();
        debugData.Release();
    }

    [Fact]
    public void ReceiveGoaway()
    {
        ByteBuf debugData = Bb("foo");
        frameInboundWriter.WriteInboundGoAway(2, NO_ERROR.Code(), debugData);
        IHttp2GoAwayFrame expectedFrame = new DefaultHttp2GoAwayFrame(2, NO_ERROR.Code(), Bb("foo"));
        IHttp2GoAwayFrame actualFrame = inboundHandler.ReadInbound<IHttp2GoAwayFrame>();

        AssertEqualsAndRelease(expectedFrame, actualFrame);

        Assert.Null(inboundHandler.ReadInbound<object>());
    }

    // CLR: f.await() waits for the future without throwing its failure: an await with
    // ConfigureAwaitOptions.SuppressThrowing, which "Avoids throwing an exception at the completion of awaiting a Task
    // that ends in the Faulted or Canceled state"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.configureawaitoptions?view=net-10.0), with
    // ContinueOnCapturedContext as xUnit1030 asks of a test method.
    [Fact]
    public async Task UnknownFrameTypeShouldThrowAndBeReleased()
    {
        UnknownHttp2Frame frame = new UnknownHttp2Frame();
        Assert.Equal(1, frame.RefCnt);

        Task f = channel.WriteAsync(frame, TestContext.Current.CancellationToken);
        await f.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        Assert.True(f.IsCompleted);
        Assert.False(f.IsCompletedSuccessfully);
        Assert.IsAssignableFrom<UnsupportedMessageTypeException>(Cause(f));
        Assert.Equal(0, frame.RefCnt);
    }

    // CLR: the local class UnknownHttp2Frame of unknownFrameTypeShouldThrowAndBeReleased.
    private sealed class UnknownHttp2Frame : AbstractReferenceCounted, IHttp2Frame
    {
        public string Name()
        {
            return "UNKNOWN";
        }

        protected override void Deallocate()
        {
        }

        public override IReferenceCounted Touch(object hint)
        {
            return this;
        }
    }

    [Fact]
    public void UnknownFrameTypeOnConnectionStream()
    {
        // handle the case where unknown frames are sent before a stream is created,
        // for example: HTTP/2 GREASE testing
        ByteBuf debugData = Bb("debug");
        frameInboundWriter.WriteInboundFrame((byte) 0xb, 0, new Http2Flags(), debugData);
        channel.Flush();

        Assert.Equal(0, debugData.RefCnt);
        Assert.True(channel.IsActive);
    }

    [Fact]
    public void UnknownFrameOnMissingStream()
    {
        ByteBuf debugData = Bb("debug");
        frameInboundWriter.WriteInboundFrame((byte) 0xb, 101, new Http2Flags(), debugData);
        channel.Flush();

        Assert.Equal(0, debugData.RefCnt);
        Assert.True(channel.IsActive);
    }

    [Fact]
    public void UnknownFrameOnMissingStreamFirstPacket()
    {
        SetUp(Http2FrameCodecBuilder.ForClient(), new Http2Settings(), false);

        // SETTINGS and UNKNOWN must come in on the same packet to trigger the bug
        channel.Pipeline.AddFirst("combine", new CombineHandler());

        frameInboundWriter.WriteInboundSettings(new Http2Settings());

        ByteBuf debugData = Bb("debug");
        frameInboundWriter.WriteInboundFrame((byte) 0xb, 101, new Http2Flags(), debugData);

        channel.Pipeline.Remove("combine");
        channel.FlushInbound();

        Assert.Equal(0, debugData.RefCnt);
        Assert.True(channel.IsActive);
    }

    // CLR: CompositeByteBuf addComponent(true, buf) is AddComponent(buf, true) (the Buffer port's parameter order,
    // src/Netty.NET.Buffer/CompositeByteBuf.cs).
    private sealed class CombineHandler : ChannelInboundHandlerAdapter
    {
        private CompositeByteBuf accumulate;

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (accumulate == null)
            {
                accumulate = ctx.Alloc().CompositeBuffer();
            }

            accumulate.AddComponent((ByteBuf) msg, true);
        }

        public override void HandlerRemoved(IChannelHandlerContext ctx)
        {
            if (accumulate != null)
            {
                ctx.FireChannelRead(accumulate);
                ctx.FireChannelReadComplete();
            }
        }
    }

    [Fact]
    public void GoAwayLastStreamIdOverflowed()
    {
        frameInboundWriter.WriteInboundHeaders(5, request, 31, false);

        IHttp2Stream stream = frameCodec.Connection().Stream(5);
        Assert.NotNull(stream);
        Assert.Equal(State.OPEN, stream.State_());

        ByteBuf debugData = Bb("debug");
        IHttp2GoAwayFrame goAwayFrame = new DefaultHttp2GoAwayFrame(NO_ERROR.Code(),
                debugData.RetainedDuplicate());
        goAwayFrame.SetExtraStreamIds(int.MaxValue);

        channel.WriteOutbound(goAwayFrame);
        // When the last stream id computation overflows, the last stream id should just be set to 2^31 - 1.
        Mock.Get(frameWriter).Verify(w => w.WriteGoAwayAsync(EqFrameCodecCtx(), int.MaxValue,
                NO_ERROR.Code(), debugData, AnyChannelPromise()), Times.Once());
        debugData.Release();
        Assert.Equal(State.OPEN, stream.State_());
        Assert.True(channel.IsActive);
    }

    [Fact]
    public void StreamErrorShouldFireExceptionForInbound()
    {
        frameInboundWriter.WriteInboundHeaders(3, request, 31, false);

        IHttp2Stream stream = frameCodec.Connection().Stream(3);
        Assert.NotNull(stream);

        Http2Exception.StreamException streamEx = new Http2Exception.StreamException(3, Http2Error.INTERNAL_ERROR, "foo");
        channel.Pipeline.FireExceptionCaught(streamEx);

        Http2FrameStreamEvent @event = inboundHandler.ReadInboundMessageOrUserEvent<Http2FrameStreamEvent>();
        Assert.Equal(Http2FrameStreamEvent.Type.State, @event.Type_());
        Assert.Equal(State.OPEN, @event.Stream().State());
        IHttp2HeadersFrame headersFrame = inboundHandler.ReadInboundMessageOrUserEvent<IHttp2HeadersFrame>();
        Assert.NotNull(headersFrame);

        Http2FrameStreamException e = Assert.ThrowsAny<Http2FrameStreamException>(
                () => inboundHandler.CheckException());
        Assert.Equal<Exception>(streamEx, e.InnerException);

        Assert.Null(inboundHandler.ReadInboundMessageOrUserEvent<object>());
    }

    [Fact]
    public void StreamErrorShouldNotFireExceptionForOutbound()
    {
        frameInboundWriter.WriteInboundHeaders(3, request, 31, false);

        IHttp2Stream stream = frameCodec.Connection().Stream(3);
        Assert.NotNull(stream);

        Http2Exception.StreamException streamEx = new Http2Exception.StreamException(3, Http2Error.INTERNAL_ERROR, "foo");
        frameCodec.OnError(frameCodec.ctx, true, streamEx);

        Http2FrameStreamEvent @event = inboundHandler.ReadInboundMessageOrUserEvent<Http2FrameStreamEvent>();
        Assert.Equal(Http2FrameStreamEvent.Type.State, @event.Type_());
        Assert.Equal(State.OPEN, @event.Stream().State());
        IHttp2HeadersFrame headersFrame = inboundHandler.ReadInboundMessageOrUserEvent<IHttp2HeadersFrame>();
        Assert.NotNull(headersFrame);

        // No exception expected
        inboundHandler.CheckException();

        Assert.Null(inboundHandler.ReadInboundMessageOrUserEvent<object>());
    }

    [Fact]
    public void WindowUpdateFrameDecrementsConsumedBytes()
    {
        frameInboundWriter.WriteInboundHeaders(3, request, 31, false);

        IHttp2Connection connection = frameCodec.Connection();
        IHttp2Stream stream = connection.Stream(3);
        Assert.NotNull(stream);

        ByteBuf data = Unpooled.Buffer(100).WriteZero(100);
        frameInboundWriter.WriteInboundData(3, data, 0, false);

        IHttp2HeadersFrame inboundHeaders = inboundHandler.ReadInbound<IHttp2HeadersFrame>();
        Assert.NotNull(inboundHeaders);
        Assert.NotNull(inboundHeaders.Stream());

        IHttp2FrameStream stream2 = inboundHeaders.Stream();

        int before = connection.Local().FlowController().UnconsumedBytes(stream);
        Task f = channel.WriteAsync(new DefaultHttp2WindowUpdateFrame(100).Stream(stream2), TestContext.Current.CancellationToken);
        int after = connection.Local().FlowController().UnconsumedBytes(stream);
        Assert.Equal(100, before - after);
        Assert.True(f.IsCompletedSuccessfully);
    }

    [Fact]
    public void WindowUpdateMayFail()
    {
        frameInboundWriter.WriteInboundHeaders(3, request, 31, false);
        IHttp2Connection connection = frameCodec.Connection();
        IHttp2Stream stream = connection.Stream(3);
        Assert.NotNull(stream);

        IHttp2HeadersFrame inboundHeaders = inboundHandler.ReadInbound<IHttp2HeadersFrame>();
        Assert.NotNull(inboundHeaders);

        IHttp2FrameStream stream2 = inboundHeaders.Stream();

        // Fails, cause trying to return too many bytes to the flow controller
        Task f = channel.WriteAsync(new DefaultHttp2WindowUpdateFrame(100).Stream(stream2), TestContext.Current.CancellationToken);
        Assert.True(f.IsCompleted);
        Assert.False(f.IsCompletedSuccessfully);
        Assert.IsAssignableFrom<Http2Exception>(Cause(f));
    }

    [Fact]
    public void InboundWindowUpdateShouldBeForwarded()
    {
        frameInboundWriter.WriteInboundHeaders(3, request, 31, false);
        frameInboundWriter.WriteInboundWindowUpdate(3, 100);
        // Connection-level window update
        frameInboundWriter.WriteInboundWindowUpdate(0, 100);

        IHttp2HeadersFrame headersFrame = inboundHandler.ReadInbound<IHttp2HeadersFrame>();
        Assert.NotNull(headersFrame);

        IHttp2WindowUpdateFrame windowUpdateFrame = inboundHandler.ReadInbound<IHttp2WindowUpdateFrame>();
        Assert.NotNull(windowUpdateFrame);
        Assert.Equal(3, windowUpdateFrame.Stream().Id());
        Assert.Equal(100, windowUpdateFrame.WindowSizeIncrement());

        // Window update for the connection should not be forwarded.
        Assert.Null(inboundHandler.ReadInbound<object>());
    }

    [Fact]
    public void StreamZeroWindowUpdateIncrementsConnectionWindow()
    {
        IHttp2Connection connection = frameCodec.Connection();
        IHttp2LocalFlowController localFlow = connection.Local().FlowController();
        int initialWindowSizeBefore = localFlow.InitialWindowSize();
        IHttp2Stream connectionStream = connection.ConnectionStream();
        int connectionWindowSizeBefore = localFlow.WindowSize(connectionStream);
        // We only replenish the flow control window after the amount consumed drops below the following threshold.
        // We make the threshold very "high" so that window updates will be sent when the delta is relatively small.
        ((DefaultHttp2LocalFlowController) localFlow).WindowUpdateRatio(connectionStream, .999f);

        int windowUpdate = 1024;

        _ = channel.WriteAsync(new DefaultHttp2WindowUpdateFrame(windowUpdate), TestContext.Current.CancellationToken);

        // The initial window size is only changed by Http2Settings, so it shouldn't change.
        Assert.Equal(initialWindowSizeBefore, localFlow.InitialWindowSize());
        // The connection window should be increased by the delta amount.
        Assert.Equal(connectionWindowSizeBefore + windowUpdate, localFlow.WindowSize(connectionStream));
    }

    [Fact]
    public void WindowUpdateDoesNotOverflowConnectionWindow()
    {
        IHttp2Connection connection = frameCodec.Connection();
        IHttp2LocalFlowController localFlow = connection.Local().FlowController();
        int initialWindowSizeBefore = localFlow.InitialWindowSize();

        _ = channel.WriteAsync(new DefaultHttp2WindowUpdateFrame(int.MaxValue), TestContext.Current.CancellationToken);

        // The initial window size is only changed by Http2Settings, so it shouldn't change.
        Assert.Equal(initialWindowSizeBefore, localFlow.InitialWindowSize());
        // The connection window should be increased by the delta amount.
        Assert.Equal(int.MaxValue, localFlow.WindowSize(connection.ConnectionStream()));
    }

    [Fact]
    public void WriteUnknownFrame()
    {
        IHttp2FrameStream stream = frameCodec.NewStream();

        ByteBuf buffer = Unpooled.Buffer().WriteByte(1);
        DefaultHttp2UnknownFrame unknownFrame = new DefaultHttp2UnknownFrame(
                (byte) 20, new Http2Flags().Ack(true), buffer);
        unknownFrame.Stream(stream);
        _ = channel.WriteAsync(unknownFrame, TestContext.Current.CancellationToken);

        Mock.Get(frameWriter).Verify(w => w.WriteFrameAsync(EqFrameCodecCtx(), unknownFrame.FrameType(),
                unknownFrame.Stream().Id(), unknownFrame.Flags(), buffer, It.IsAny<TaskCompletionSource>()),
                Times.Once());
    }

    [Fact]
    public void SendSettingsFrame()
    {
        Http2Settings settings = new Http2Settings();
        _ = channel.WriteAsync(new DefaultHttp2SettingsFrame(settings), TestContext.Current.CancellationToken);

        Mock.Get(frameWriter).Verify(w => w.WriteSettingsAsync(EqFrameCodecCtx(),
                It.Is<Http2Settings>(s => ReferenceEquals(s, settings)), It.IsAny<TaskCompletionSource>()),
                Times.Once());
    }

    // CLR: @Timeout(5000 ms) is the xUnit Timeout of [Fact], which applies to async tests (as codec-http's
    // WebSocketClientHandshakerTest); the Promise<Void> of GlobalEventExecutor is a TaskCompletionSource whose
    // syncUninterruptibly() is the await of its Task.
    [Fact(Timeout = 5000)]
    public async Task NewOutboundStream()
    {
        IHttp2FrameStream stream = frameCodec.NewStream();

        Assert.NotNull(stream);
        Assert.False(IsStreamIdValid(stream.Id()));

        TaskCompletionSource listenerExecuted = new TaskCompletionSource();

        _ = channel.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(new DefaultHttp2Headers(), false).Stream(stream), TestContext.Current.CancellationToken)
               .ContinueWith(future =>
               {
                   Assert.True(future.IsCompletedSuccessfully);
                   Assert.True(IsStreamIdValid(stream.Id()));
                   listenerExecuted.SetResult();
               }, TestContext.Current.CancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        ByteBuf data = Unpooled.Buffer().WriteZero(100);
        Task f = channel.WriteAndFlushAsync(new DefaultHttp2DataFrame(data).Stream(stream), TestContext.Current.CancellationToken);
        Assert.True(f.IsCompletedSuccessfully);

        await listenerExecuted.Task;
        Assert.True(listenerExecuted.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task NewOutboundStreamsShouldBeBuffered()
    {
        SetUp(Http2FrameCodecBuilder.ForServer().EncoderEnforceMaxConcurrentStreams(true),
              new Http2Settings().MaxConcurrentStreams(1));

        IHttp2FrameStream stream1 = frameCodec.NewStream();
        IHttp2FrameStream stream2 = frameCodec.NewStream();

        Task promise1 = channel.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(new DefaultHttp2Headers()).Stream(stream1), TestContext.Current.CancellationToken);
        Task promise2 = channel.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(new DefaultHttp2Headers()).Stream(stream2), TestContext.Current.CancellationToken);

        Assert.True(IsStreamIdValid(stream1.Id()));
        channel.RunPendingTasks();
        Assert.True(IsStreamIdValid(stream2.Id()));

        await promise1;
        Assert.True(promise1.IsCompletedSuccessfully);
        Assert.False(promise2.IsCompleted);

        // Increase concurrent streams limit to 2
        frameInboundWriter.WriteInboundSettings(new Http2Settings().MaxConcurrentStreams(2));

        channel.Flush();

        await promise2;
        Assert.True(promise2.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task MultipleNewOutboundStreamsShouldBeBuffered()
    {
        // We use a limit of 1 and then increase it step by step.
        SetUp(Http2FrameCodecBuilder.ForServer().EncoderEnforceMaxConcurrentStreams(true),
                new Http2Settings().MaxConcurrentStreams(1));

        IHttp2FrameStream stream1 = frameCodec.NewStream();
        IHttp2FrameStream stream2 = frameCodec.NewStream();
        IHttp2FrameStream stream3 = frameCodec.NewStream();

        Task promise1 = channel.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(new DefaultHttp2Headers()).Stream(stream1), TestContext.Current.CancellationToken);
        Task promise2 = channel.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(new DefaultHttp2Headers()).Stream(stream2), TestContext.Current.CancellationToken);
        Task promise3 = channel.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(new DefaultHttp2Headers()).Stream(stream3), TestContext.Current.CancellationToken);

        Assert.True(IsStreamIdValid(stream1.Id()));
        channel.RunPendingTasks();
        Assert.True(IsStreamIdValid(stream2.Id()));

        await promise1;
        Assert.True(promise1.IsCompletedSuccessfully);
        Assert.False(promise2.IsCompleted);
        Assert.False(promise3.IsCompleted);

        // Increase concurrent streams limit to 2
        frameInboundWriter.WriteInboundSettings(new Http2Settings().MaxConcurrentStreams(2));
        channel.Flush();

        // As we increased the limit to 2 we should have also succeed the second frame.
        await promise2;
        Assert.True(promise2.IsCompletedSuccessfully);
        Assert.False(promise3.IsCompleted);

        frameInboundWriter.WriteInboundSettings(new Http2Settings().MaxConcurrentStreams(3));
        channel.Flush();

        // With the max streams of 3 all streams should be succeed now.
        await promise3;
        Assert.True(promise3.IsCompletedSuccessfully);

        Assert.False(channel.FinishAndReleaseAll());
    }

    [Fact]
    public async Task DoNotLeakOnFailedInitializationForChannels()
    {
        SetUp(Http2FrameCodecBuilder.ForServer(), new Http2Settings().MaxConcurrentStreams(2));

        IHttp2FrameStream stream1 = frameCodec.NewStream();
        IHttp2FrameStream stream2 = frameCodec.NewStream();

        Task stream1HeaderPromise = channel.WriteAndFlushAsync(
                new DefaultHttp2HeadersFrame(new DefaultHttp2Headers()).Stream(stream1), TestContext.Current.CancellationToken);
        channel.RunPendingTasks();

        frameInboundWriter.WriteInboundGoAway(stream1.Id(), 0L, Unpooled.EmptyBuffer);

        Task stream2HeaderPromise = channel.WriteAndFlushAsync(
                new DefaultHttp2HeadersFrame(new DefaultHttp2Headers()).Stream(stream2), TestContext.Current.CancellationToken);
        channel.RunPendingTasks();

        await stream1HeaderPromise;
        Assert.True(stream1HeaderPromise.IsCompletedSuccessfully);
        Assert.True(stream2HeaderPromise.IsCompleted);

        Assert.Equal(0, frameCodec.NumInitializingStreams());
        Assert.False(channel.FinishAndReleaseAll());
    }

    [Fact]
    public void StreamIdentifiersExhausted()
    {
        int maxServerStreamId = int.MaxValue - 1;

        Assert.NotNull(frameCodec.Connection().Local().CreateStream(maxServerStreamId, false));

        IHttp2FrameStream stream = frameCodec.NewStream();
        Assert.NotNull(stream);

        Task writePromise = channel.WriteAndFlushAsync(
                new DefaultHttp2HeadersFrame(new DefaultHttp2Headers()).Stream(stream), TestContext.Current.CancellationToken);

        IHttp2GoAwayFrame goAwayFrame = inboundHandler.ReadInbound<IHttp2GoAwayFrame>();
        Assert.NotNull(goAwayFrame);
        Assert.Equal(NO_ERROR.Code(), goAwayFrame.ErrorCode());
        Assert.Equal(int.MaxValue, goAwayFrame.LastStreamId());
        goAwayFrame.Release();
        Assert.IsAssignableFrom<Http2NoMoreStreamIdsException>(Cause(writePromise));
    }

    [Fact]
    public void ReceivePing()
    {
        frameInboundWriter.WriteInboundPing(false, 12345L);

        IHttp2PingFrame pingFrame = inboundHandler.ReadInbound<IHttp2PingFrame>();
        Assert.NotNull(pingFrame);

        Assert.Equal(12345, pingFrame.Content());
        Assert.False(pingFrame.Ack());
    }

    [Fact]
    public void SendPing()
    {
        _ = channel.WriteAndFlushAsync(new DefaultHttp2PingFrame(12345), TestContext.Current.CancellationToken);

        Mock.Get(frameWriter).Verify(w => w.WritePingAsync(EqFrameCodecCtx(), false, 12345L, AnyChannelPromise()),
                Times.Once());
    }

    [Fact]
    public void ReceiveSettings()
    {
        Http2Settings settings = new Http2Settings().MaxConcurrentStreams(1);
        frameInboundWriter.WriteInboundSettings(settings);

        IHttp2SettingsFrame settingsFrame = inboundHandler.ReadInbound<IHttp2SettingsFrame>();
        Assert.NotNull(settingsFrame);
        Assert.Equal(settings, settingsFrame.Settings());
    }

    [Fact]
    public void SendSettings()
    {
        Http2Settings settings = new Http2Settings().MaxConcurrentStreams(1);
        _ = channel.WriteAndFlushAsync(new DefaultHttp2SettingsFrame(settings), TestContext.Current.CancellationToken);

        Mock.Get(frameWriter).Verify(w => w.WriteSettingsAsync(EqFrameCodecCtx(), settings, AnyChannelPromise()),
                Times.Once());
    }

    // CLR: assertEquals of two Sets is Set.equals (same elements in any order), which is HashSet.SetEquals.
    [Fact]
    public void IterateActiveStreams()
    {
        SetUp(Http2FrameCodecBuilder.ForServer().EncoderEnforceMaxConcurrentStreams(true),
              new Http2Settings().MaxConcurrentStreams(1));

        frameInboundWriter.WriteInboundHeaders(3, request, 0, false);

        IHttp2HeadersFrame headersFrame = inboundHandler.ReadInbound<IHttp2HeadersFrame>();
        Assert.NotNull(headersFrame);

        IHttp2FrameStream activeInbond = headersFrame.Stream();

        IHttp2FrameStream activeOutbound = frameCodec.NewStream();
        _ = channel.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(new DefaultHttp2Headers()).Stream(activeOutbound), TestContext.Current.CancellationToken);

        IHttp2FrameStream bufferedOutbound = frameCodec.NewStream();
        _ = channel.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(new DefaultHttp2Headers()).Stream(bufferedOutbound), TestContext.Current.CancellationToken);

        // CLR: @SuppressWarnings("unused") has no counterpart; the build reports no warning for this local (measured).
        IHttp2FrameStream idleStream = frameCodec.NewStream();

        HashSet<IHttp2FrameStream> activeStreams = new HashSet<IHttp2FrameStream>();
        frameCodec.ForEachActiveStream(new CollectingStreamVisitor(activeStreams));

        Assert.Equal(2, activeStreams.Count);

        HashSet<IHttp2FrameStream> expectedStreams = new HashSet<IHttp2FrameStream>();
        expectedStreams.Add(activeInbond);
        expectedStreams.Add(activeOutbound);
        Assert.True(expectedStreams.SetEquals(activeStreams));
    }

    private sealed class CollectingStreamVisitor : IHttp2FrameStreamVisitor
    {
        private readonly HashSet<IHttp2FrameStream> activeStreams;

        internal CollectingStreamVisitor(HashSet<IHttp2FrameStream> activeStreams)
        {
            this.activeStreams = activeStreams;
        }

        public bool Visit(IHttp2FrameStream stream)
        {
            activeStreams.Add(stream);
            return true;
        }
    }

    [Fact]
    public void AutoAckPingTrue()
    {
        SetUp(Http2FrameCodecBuilder.ForServer().AutoAckPingFrame(true), new Http2Settings());
        frameInboundWriter.WriteInboundPing(false, 8);
        IHttp2PingFrame frame = inboundHandler.ReadInbound<IHttp2PingFrame>();
        Assert.False(frame.Ack());
        Assert.Equal(8, frame.Content());
        Mock.Get(frameWriter).Verify(w => w.WritePingAsync(EqFrameCodecCtx(), true, 8L, AnyChannelPromise()),
                Times.Once());
    }

    [Fact]
    public void AutoAckPingFalse()
    {
        SetUp(Http2FrameCodecBuilder.ForServer().AutoAckPingFrame(false), new Http2Settings());
        frameInboundWriter.WriteInboundPing(false, 8);
        Mock.Get(frameWriter).Verify(w => w.WritePingAsync(EqFrameCodecCtx(), true, 8L, AnyChannelPromise()),
                Times.Never());
        IHttp2PingFrame frame = inboundHandler.ReadInbound<IHttp2PingFrame>();
        Assert.False(frame.Ack());
        Assert.Equal(8, frame.Content());

        // Now ack the frame manually.
        _ = channel.WriteAndFlushAsync(new DefaultHttp2PingFrame(8, true), TestContext.Current.CancellationToken);
        Mock.Get(frameWriter).Verify(w => w.WritePingAsync(EqFrameCodecCtx(), true, 8L, AnyChannelPromise()),
                Times.Once());
    }

    [Fact]
    public void StreamShouldBeOpenInListener()
    {
        IHttp2FrameStream stream2 = frameCodec.NewStream();
        Assert.Equal(State.IDLE, stream2.State());

        AtomicBoolean listenerExecuted = new AtomicBoolean();
        _ = channel.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(new DefaultHttp2Headers()).Stream(stream2), TestContext.Current.CancellationToken)
                .ContinueWith(future =>
                {
                    Assert.True(future.IsCompletedSuccessfully);
                    Assert.Equal(State.OPEN, stream2.State());
                    listenerExecuted.Set(true);
                }, TestContext.Current.CancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        Assert.True(listenerExecuted.Get());
    }

    // CLR: channel.voidPromise() has no counterpart; the returned Task is discarded.
    [Fact]
    public void WriteHeadersVoidPromise()
    {
        IHttp2FrameStream stream = frameCodec.NewStream();
        _ = channel.WriteAndFlushAsync(new DefaultHttp2HeadersFrame(new DefaultHttp2Headers()).Stream(stream), TestContext.Current.CancellationToken);
    }

    // CLR: UpgradeEvent.class.getDeclaredConstructor(CharSequence.class, FullHttpRequest.class) and
    // newInstance(...) are Type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, types) ("Specify
    // BindingFlags.NonPublic to include non-public constructors (that is, private, internal, and protected
    // constructors) in the search",
    // https://learn.microsoft.com/en-us/dotnet/api/system.type.getconstructor?view=net-10.0) and
    // ConstructorInfo.Invoke ("Access restrictions are ignored for fully trusted code",
    // https://learn.microsoft.com/en-us/dotnet/api/system.reflection.constructorinfo.invoke?view=net-10.0); the
    // constructor is internal in codec-http (src/Netty.NET.Handler.Codec.Http/HttpServerUpgradeHandler.cs), which
    // does not open its internals to this assembly. Assumptions.assumeTrue(ReflectionUtil.trySetAccessible(...) ==
    // null) has no counterpart: CLR members have no accessible flag and the no-op trySetAccessible is retired from
    // the Common port, so the test always runs. The "HTTP/2" String passed as CharSequence is Http2TestUtil.Of.
    [Fact]
    public void UpgradeEventNoRefCntError()
    {
        frameInboundWriter.WriteInboundHeaders(Http2CodecUtil.HTTP_UPGRADE_STREAM_ID, request, 31, false);
        // Using reflect as the constructor is package-private and the class is final.
        ConstructorInfo constructor = typeof(HttpServerUpgradeHandler.UpgradeEvent).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic, new Type[] { typeof(ICharSequence), typeof(IFullHttpRequest) });

        // Check if we could make it accessible which may fail on java9.
        // CLR: no counterpart (see the comment of upgradeEventNoRefCntError).

        HttpServerUpgradeHandler.UpgradeEvent upgradeEvent = (HttpServerUpgradeHandler.UpgradeEvent) constructor.Invoke(
                new object[] { Of("HTTP/2"), new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/") });
        channel.Pipeline.FireUserEventTriggered(upgradeEvent);
        Assert.Equal(1, upgradeEvent.RefCnt);
    }

    // CLR: the reflective UpgradeEvent construction is the one of upgradeEventNoRefCntError. The local request hides the
    // field in the whole method body in C# ("Because a local variable declared later with the same type name, it
    // shadows the type", https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0841), so the earlier use of the field
    // is this.request. future.addListener of the window update is Http2CodecUtil.AddListener (the DefaultPromise
    // listener timing on the channel's event loop) and future.cause() Http2CodecUtil.Cause.
    [Fact]
    public void UpgradeWithoutFlowControlling()
    {
        channel.Pipeline.AddAfter(frameCodec.ctx.Name, null, new WindowUpdatingHandler());

        frameInboundWriter.WriteInboundHeaders(Http2CodecUtil.HTTP_UPGRADE_STREAM_ID, this.request, 31, false);

        // Using reflect as the constructor is package-private and the class is final.
        ConstructorInfo constructor = typeof(HttpServerUpgradeHandler.UpgradeEvent).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic, new Type[] { typeof(ICharSequence), typeof(IFullHttpRequest) });

        // Check if we could make it accessible which may fail on java9.
        // CLR: no counterpart (see the comment of upgradeEventNoRefCntError).

        string longString = new string(new char[70000]).Replace("\0", "*");
        DefaultFullHttpRequest request =
            new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/", Bb(longString));

        HttpServerUpgradeHandler.UpgradeEvent upgradeEvent = (HttpServerUpgradeHandler.UpgradeEvent) constructor.Invoke(
            new object[] { Of("HTTP/2"), request });
        channel.Pipeline.FireUserEventTriggered(upgradeEvent);
    }

    private sealed class WindowUpdatingHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is IHttp2DataFrame)
            {
                // Simulate consuming the frame and update the flow-controller.
                IHttp2DataFrame data = (IHttp2DataFrame) msg;
                AddListener(ctx, ctx.WriteAndFlushAsync(new DefaultHttp2WindowUpdateFrame(data.InitialFlowControlledBytes())
                        .Stream(data.Stream())), future =>
                        {
                            Exception cause = Cause(future);
                            if (cause != null)
                            {
                                ctx.FireExceptionCaught(cause);
                            }
                        });
            }

            ReferenceCountUtil.Release(msg);
        }
    }

    [Fact]
    public void PriorityForNonExistingStream()
    {
        WriteHeaderAndAssert(1);

        frameInboundWriter.WriteInboundPriority(3, 1, (short) 31, true);
    }

    [Fact]
    public void PriorityForExistingStream()
    {
        WriteHeaderAndAssert(1);
        WriteHeaderAndAssert(3);
        frameInboundWriter.WriteInboundPriority(3, 1, (short) 31, true);

        AssertInboundStreamFrame(3, new DefaultHttp2PriorityFrame(1, (short) 31, true));
    }

    private void WriteHeaderAndAssert(int streamId)
    {
        frameInboundWriter.WriteInboundHeaders(streamId, request, 31, false);

        IHttp2Stream stream = frameCodec.Connection().Stream(streamId);
        Assert.NotNull(stream);
        Assert.Equal(State.OPEN, stream.State_());

        AssertInboundStreamFrame(streamId, new DefaultHttp2HeadersFrame(request, false, 31));
    }

    private void AssertInboundStreamFrame(int expectedId, IHttp2StreamFrame streamFrame)
    {
        IHttp2StreamFrame inboundFrame = inboundHandler.ReadInbound<IHttp2StreamFrame>();
        IHttp2FrameStream stream2 = inboundFrame.Stream();
        Assert.NotNull(stream2);
        Assert.Equal(expectedId, stream2.Id());
        Assert.Equal(inboundFrame, streamFrame.Stream(stream2));
    }

    // CLR: eq(frameCodec.ctx); Moq compares the returned value with Equals.
    private IChannelHandlerContext EqFrameCodecCtx()
    {
        return frameCodec.ctx;
    }

    // CLR: ArgumentCaptor.capture() and getValue(): the argument at index of the last invocation of the method that the
    // mock recorded (Mock.Invocations, https://github.com/devlooped/moq/wiki/Quickstart).
    private T CapturedArgument<T>(string methodName, int index)
    {
        IInvocation invocation = Mock.Get(frameWriter).Invocations.Last(i => i.Method.Name == methodName);
        return (T) invocation.Arguments[index];
    }

    [Fact]
    public void InvalidPayloadLength()
    {
        frameInboundWriter.WriteInboundSettings(new Http2Settings());
        channel.WriteInbound(Unpooled.WrappedBuffer(new byte[]{
                0, 0, 4, // length
                0, // type: DATA
                9, // flags: PADDED, END_STREAM
                1, 0, 0, 0, // stream id
                4, // pad length
                0, 0, 0 // not enough space for padding
        }));
        Assert.ThrowsAny<Http2Exception>(() => inboundHandler.CheckException());
    }
}
