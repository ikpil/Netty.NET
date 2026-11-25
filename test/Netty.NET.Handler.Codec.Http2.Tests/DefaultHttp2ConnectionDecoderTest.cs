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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Http;
using Netty.NET.Transport.Channels;
using static Netty.NET.Buffer.Unpooled;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Tests for {@link DefaultHttp2ConnectionDecoder}.
 */
// CLR: @Mock fields and MockitoAnnotations.initMocks are Moq mocks created in the constructor, which runs the
// @BeforeEach setup() (xUnit creates a test class instance per test, https://xunit.net/docs/shared-context). The fields
// hold the mocked objects and Mock.Get(x) reaches their Moq mocks; when/doAnswer/verify/never/times are
// Setup(...).Returns/Callback, Verify(...), Times.Never()/Times.Exactly(n) and verify(mock) without a count is
// Times.Once() (Mockito's default times(1)); verifyNoMoreInteractions is VerifyNoOtherCalls(); isNull() is a null
// argument; ArgumentCaptor is a Callback that stores the argument (https://github.com/devlooped/moq/wiki/Quickstart).
// The generic getProperty/setProperty stubs match any type argument with It.IsAnyType and answer through
// InvocationFunc over a Dictionary with ReferenceEqualityComparer (IdentityHashMap); AtomicInteger is an int updated with
// Interlocked. The decoder creates its own promises (Http2CodecUtil.NewPromise()) and passes Task.CompletedTask for
// ctx.newSucceededFuture() (docs/codec-http-porting.md, Shared CLR adaptations), so the stubs of
// ctx.newPromise()/newSucceededFuture()/write(...) have no counterpart and the verifications that compared the stubbed
// promise or future match any TaskCompletionSource or any successfully completed Task. Strings passed as CharSequence
// are StringCharSequence values (a C# string is not an ICharSequence). assertThrows is Assert.ThrowsAny, AssertJ
// isNotInstanceOf is Assert.IsNotAssignableFrom and contains is Assert.Contains (docs/codec-http-porting.md, Core test
// notes); @ParameterizedTest @ValueSource is a [Theory] with InlineData rows. The anonymous Executables are lambdas and
// the anonymous Http2PromisedRequestVerifier is the private nested NotAuthoritativeVerifier.
public class DefaultHttp2ConnectionDecoderTest
{
    private const int STREAM_ID = 3;
    private const int PUSH_STREAM_ID = 2;
    private const int STREAM_DEPENDENCY_ID = 5;
    private const int STATE_RECV_HEADERS = 1;
    private const int STATE_RECV_TRAILERS = 1 << 1;

    private readonly IHttp2ConnectionDecoder decoder;

    private readonly IHttp2Connection connection;

    private readonly IHttp2Connection.IEndpoint<IHttp2RemoteFlowController> remote;

    private readonly IHttp2Connection.IEndpoint<IHttp2LocalFlowController> local;

    private readonly IHttp2LocalFlowController localFlow;

    private readonly IHttp2RemoteFlowController remoteFlow;

    private readonly IChannelHandlerContext ctx;

    private readonly IChannel channel;

    private readonly IHttp2Stream stream;

    private readonly IHttp2Stream pushStream;

    private readonly IHttp2FrameListener listener;

    private readonly IHttp2FrameReader reader;

    private readonly IHttp2FrameWriter writer;

    private readonly IHttp2ConnectionEncoder encoder;

    private readonly IHttp2LifecycleManager lifecycleManager;

    private int headersReceivedState;

    public DefaultHttp2ConnectionDecoderTest()
    {
        connection = new Mock<IHttp2Connection>().Object;
        remote = new Mock<IHttp2Connection.IEndpoint<IHttp2RemoteFlowController>>().Object;
        local = new Mock<IHttp2Connection.IEndpoint<IHttp2LocalFlowController>>().Object;
        localFlow = new Mock<IHttp2LocalFlowController>().Object;
        remoteFlow = new Mock<IHttp2RemoteFlowController>().Object;
        ctx = new Mock<IChannelHandlerContext>().Object;
        channel = new Mock<IChannel>().Object;
        stream = new Mock<IHttp2Stream>().Object;
        pushStream = new Mock<IHttp2Stream>().Object;
        listener = new Mock<IHttp2FrameListener>().Object;
        reader = new Mock<IHttp2FrameReader>().Object;
        writer = new Mock<IHttp2FrameWriter>().Object;
        encoder = new Mock<IHttp2ConnectionEncoder>().Object;
        lifecycleManager = new Mock<IHttp2LifecycleManager>().Object;

        Mock.Get(channel).Setup(c => c.IsActive).Returns(true);
        Mock<IHttp2Stream> streamMock = Mock.Get(stream);
        streamMock.Setup(s => s.Id()).Returns(STREAM_ID);
        streamMock.Setup(s => s.State_()).Returns(IHttp2Stream.State.OPEN);
        streamMock.Setup(s => s.Open(It.IsAny<bool>())).Returns(stream);

        // CLR: IdentityHashMap accepts the null key that the mocked connection's newKey() returns; the Dictionary
        // stores it under a private sentinel object.
        Dictionary<object, object> properties = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        object nullKey = new object();
        streamMock.Setup(s => s.GetProperty<It.IsAnyType>(It.IsAny<IHttp2Connection.IPropertyKey>()))
                .Returns(new InvocationFunc(invocation =>
                        properties.TryGetValue(invocation.Arguments[0] ?? nullKey, out object value) ? value : null));
        streamMock.Setup(s => s.SetProperty(It.IsAny<IHttp2Connection.IPropertyKey>(), It.IsAny<It.IsAnyType>()))
                .Returns(new InvocationFunc(invocation =>
                {
                    object key = invocation.Arguments[0] ?? nullKey;
                    properties.TryGetValue(key, out object previous);
                    properties[key] = invocation.Arguments[1];
                    return previous;
                }));

        Mock.Get(pushStream).Setup(s => s.Id()).Returns(PUSH_STREAM_ID);
        streamMock.Setup(s => s.IsHeadersReceived())
                .Returns(() => (Volatile.Read(ref headersReceivedState) & STATE_RECV_HEADERS) != 0);
        streamMock.Setup(s => s.IsTrailersReceived())
                .Returns(() => (Volatile.Read(ref headersReceivedState) & STATE_RECV_TRAILERS) != 0);
        streamMock.Setup(s => s.HeadersReceived(It.IsAny<bool>())).Returns<bool>(isInformational =>
        {
            if (isInformational)
            {
                return stream;
            }
            for (;;)
            {
                int current = Volatile.Read(ref headersReceivedState);
                int next = current;
                if ((current & STATE_RECV_HEADERS) != 0)
                {
                    if ((current & STATE_RECV_TRAILERS) != 0)
                    {
                        throw new InvalidOperationException("already sent headers!");
                    }
                    next |= STATE_RECV_TRAILERS;
                }
                else
                {
                    next |= STATE_RECV_HEADERS;
                }
                if (Interlocked.CompareExchange(ref headersReceivedState, next, current) == current)
                {
                    break;
                }
            }
            return stream;
        });
        Mock<IHttp2Connection> connectionMock = Mock.Get(connection);
        connectionMock.Setup(c => c.ForEachActiveStream(It.IsAny<IHttp2StreamVisitor>()))
                .Returns<IHttp2StreamVisitor>(visitor =>
                {
                    if (!visitor.Visit(stream))
                    {
                        return stream;
                    }
                    return null;
                });
        connectionMock.Setup(c => c.Stream(STREAM_ID)).Returns(stream);
        connectionMock.Setup(c => c.StreamMayHaveExisted(STREAM_ID)).Returns(true);
        connectionMock.Setup(c => c.Local()).Returns(local);
        Mock.Get(local).Setup(l => l.FlowController()).Returns(localFlow);
        Mock.Get(encoder).Setup(e => e.FlowController()).Returns(remoteFlow);
        Mock.Get(encoder).Setup(e => e.FrameWriter()).Returns(writer);
        connectionMock.Setup(c => c.Remote()).Returns(remote);
        Mock.Get(local).Setup(l => l.ReservePushStream(PUSH_STREAM_ID, stream)).Returns(pushStream);
        Mock.Get(remote).Setup(r => r.ReservePushStream(PUSH_STREAM_ID, stream)).Returns(pushStream);
        Mock.Get(ctx).Setup(c => c.Alloc()).Returns(UnpooledByteBufAllocator.Default);
        Mock.Get(ctx).Setup(c => c.Channel).Returns(channel);

        decoder = new DefaultHttp2ConnectionDecoder(connection, encoder, reader);
        SetupCodec(ctx, encoder, decoder, listener);
    }

    private void SetupCodec(
            IChannelHandlerContext ctx, IHttp2ConnectionEncoder encoder, IHttp2ConnectionDecoder decoder,
            IHttp2FrameListener listener)
    {
        decoder.LifecycleManager(lifecycleManager);
        decoder.FrameListener(listener);

        // Simulate receiving the initial settings from the remote endpoint.
        Decode(decoder).OnSettingsRead(ctx, new Http2Settings());
        Mock.Get(listener).Verify(l => l.OnSettingsRead(ctx, new Http2Settings()), Times.Once());
        Assert.True(decoder.PrefaceReceived());
        Mock.Get(encoder).Verify(e => e.WriteSettingsAckAsync(ctx, It.IsAny<TaskCompletionSource>()), Times.Once());

        // Simulate receiving the SETTINGS ACK for the initial settings.
        Decode(decoder).OnSettingsAckRead(ctx);

        // Disallow any further flushes now that settings ACK has been sent
        Mock.Get(ctx).Setup(c => c.Flush()).Returns(() =>
        {
            Assert.Fail(null);
            return null;
        });
    }

    [Fact]
    public void DataReadAfterGoAwaySentShouldApplyFlowControl()
    {
        MockGoAwaySent();

        ByteBuf data = DummyData();
        int padding = 10;
        int processedBytes = data.ReadableBytes + padding;
        MockFlowControl(processedBytes);
        try
        {
            Decode().OnDataRead(ctx, STREAM_ID, data, padding, true);
            Mock.Get(localFlow).Verify(f => f.ReceiveFlowControlledFrame(stream, data, padding, true), Times.Once());
            Mock.Get(localFlow).Verify(f => f.ConsumeBytes(stream, processedBytes), Times.Once());

            // Verify that the event was absorbed and not propagated to the observer.
            Mock.Get(listener).Verify(l => l.OnDataRead(ctx, It.IsAny<int>(), It.IsAny<ByteBuf>(), It.IsAny<int>(),
                    It.IsAny<bool>()), Times.Never());
        }
        finally
        {
            data.Release();
        }
    }

    [Fact]
    public void DataReadAfterGoAwaySentShouldAllowFramesForStreamCreatedByLocalEndpoint()
    {
        MockGoAwaySentShouldAllowFramesForStreamCreatedByLocalEndpoint();

        ByteBuf data = DummyData();
        int padding = 10;
        int processedBytes = data.ReadableBytes + padding;
        MockFlowControl(processedBytes);
        try
        {
            Decode().OnDataRead(ctx, STREAM_ID, data, padding, true);
            Mock.Get(localFlow).Verify(f => f.ReceiveFlowControlledFrame(stream, data, padding, true), Times.Once());
            Mock.Get(localFlow).Verify(f => f.ConsumeBytes(stream, processedBytes), Times.Once());

            // Verify that the event was absorbed and not propagated to the observer.
            Mock.Get(listener).Verify(l => l.OnDataRead(ctx, It.IsAny<int>(), It.IsAny<ByteBuf>(), It.IsAny<int>(),
                    It.IsAny<bool>()), Times.Once());
        }
        finally
        {
            data.Release();
        }
    }

    [Fact]
    public void DataReadForUnknownStreamShouldApplyFlowControlAndFail()
    {
        Mock.Get(connection).Setup(c => c.StreamMayHaveExisted(STREAM_ID)).Returns(true);
        Mock.Get(connection).Setup(c => c.Stream(STREAM_ID)).Returns((IHttp2Stream) null);
        ByteBuf data = DummyData();
        int padding = 10;
        int processedBytes = data.ReadableBytes + padding;
        Assert.ThrowsAny<Http2Exception.StreamException>(() => Decode().OnDataRead(ctx, STREAM_ID, data, padding, true));
        try
        {
            Mock.Get(localFlow)
                    .Verify(f => f.ReceiveFlowControlledFrame((IHttp2Stream) null, data, padding, true), Times.Once());
            Mock.Get(localFlow).Verify(f => f.ConsumeBytes((IHttp2Stream) null, processedBytes), Times.Once());
            Mock.Get(localFlow).Verify(f => f.FrameWriter(It.IsAny<IHttp2FrameWriter>()), Times.Once());
            Mock.Get(localFlow).VerifyNoOtherCalls();
            Mock.Get(listener).Verify(l => l.OnDataRead(ctx, It.IsAny<int>(), It.IsAny<ByteBuf>(), It.IsAny<int>(),
                    It.IsAny<bool>()), Times.Never());
        }
        finally
        {
            data.Release();
        }
    }

    [Fact]
    public void DataReadForUnknownStreamThatCouldntExistFail()
    {
        Mock.Get(connection).Setup(c => c.StreamMayHaveExisted(STREAM_ID)).Returns(false);
        Mock.Get(connection).Setup(c => c.Stream(STREAM_ID)).Returns((IHttp2Stream) null);
        ByteBuf data = DummyData();
        int padding = 10;
        int processedBytes = data.ReadableBytes + padding;
        Assert.ThrowsAny<Http2Exception>(() =>
        {
            try
            {
                Decode().OnDataRead(ctx, STREAM_ID, data, padding, true);
            }
            catch (Http2Exception ex)
            {
                Assert.IsNotAssignableFrom<Http2Exception.StreamException>(ex);
                throw;
            }
        });
        try
        {
            Mock.Get(localFlow)
                    .Verify(f => f.ReceiveFlowControlledFrame((IHttp2Stream) null, data, padding, true), Times.Once());
            Mock.Get(localFlow).Verify(f => f.ConsumeBytes((IHttp2Stream) null, processedBytes), Times.Once());
            Mock.Get(localFlow).Verify(f => f.FrameWriter(It.IsAny<IHttp2FrameWriter>()), Times.Once());
            Mock.Get(localFlow).VerifyNoOtherCalls();
            Mock.Get(listener).Verify(l => l.OnDataRead(ctx, It.IsAny<int>(), It.IsAny<ByteBuf>(), It.IsAny<int>(),
                    It.IsAny<bool>()), Times.Never());
        }
        finally
        {
            data.Release();
        }
    }

    [Fact]
    public void DataReadForUnknownStreamShouldApplyFlowControl()
    {
        Mock.Get(connection).Setup(c => c.Stream(STREAM_ID)).Returns((IHttp2Stream) null);
        ByteBuf data = DummyData();
        int padding = 10;
        int processedBytes = data.ReadableBytes + padding;
        try
        {
            Assert.ThrowsAny<Http2Exception>(() => Decode().OnDataRead(ctx, STREAM_ID, data, padding, true));
            Mock.Get(localFlow)
                    .Verify(f => f.ReceiveFlowControlledFrame((IHttp2Stream) null, data, padding, true), Times.Once());
            Mock.Get(localFlow).Verify(f => f.ConsumeBytes((IHttp2Stream) null, processedBytes), Times.Once());
            Mock.Get(localFlow).Verify(f => f.FrameWriter(It.IsAny<IHttp2FrameWriter>()), Times.Once());
            Mock.Get(localFlow).VerifyNoOtherCalls();

            // Verify that the event was absorbed and not propagated to the observer.
            Mock.Get(listener).Verify(l => l.OnDataRead(ctx, It.IsAny<int>(), It.IsAny<ByteBuf>(), It.IsAny<int>(),
                    It.IsAny<bool>()), Times.Never());
        }
        finally
        {
            data.Release();
        }
    }

    [Fact]
    public void EmptyDataFrameShouldApplyFlowControl()
    {
        ByteBuf data = EmptyBuffer;
        int padding = 0;
        MockFlowControl(0);
        try
        {
            Decode().OnDataRead(ctx, STREAM_ID, data, padding, true);
            Mock.Get(localFlow).Verify(f => f.ReceiveFlowControlledFrame(stream, data, padding, true), Times.Once());

            // Now we ignore the empty bytes inside consumeBytes method, so it will be called once.
            Mock.Get(localFlow).Verify(f => f.ConsumeBytes(stream, 0), Times.Once());

            // Verify that the empty data event was propagated to the observer.
            Mock.Get(listener).Verify(l => l.OnDataRead(ctx, STREAM_ID, data, padding, true), Times.Once());
        }
        finally
        {
            data.Release();
        }
    }

    [Fact]
    public void DataReadForStreamInInvalidStateShouldThrow()
    {
        // Throw an exception when checking stream state.
        Mock.Get(stream).Setup(s => s.State_()).Returns(IHttp2Stream.State.CLOSED);
        ByteBuf data = DummyData();
        Assert.ThrowsAny<Http2Exception>(() => Decode().OnDataRead(ctx, STREAM_ID, data, 10, true));
        data.Release();
    }

    [Fact]
    public void DataReadAfterGoAwaySentForStreamInInvalidStateShouldIgnore()
    {
        // Throw an exception when checking stream state.
        Mock.Get(stream).Setup(s => s.State_()).Returns(IHttp2Stream.State.CLOSED);
        MockGoAwaySent();
        ByteBuf data = DummyData();
        try
        {
            Decode().OnDataRead(ctx, STREAM_ID, data, 10, true);
            Mock.Get(localFlow).Verify(f => f.ReceiveFlowControlledFrame(stream, data, 10, true), Times.Once());
            Mock.Get(listener).Verify(l => l.OnDataRead(ctx, It.IsAny<int>(), It.IsAny<ByteBuf>(), It.IsAny<int>(),
                    It.IsAny<bool>()), Times.Never());
        }
        finally
        {
            data.Release();
        }
    }

    [Fact]
    public void DataReadAfterGoAwaySentOnUnknownStreamShouldIgnore()
    {
        // Throw an exception when checking stream state.
        Mock.Get(connection).Setup(c => c.Stream(STREAM_ID)).Returns((IHttp2Stream) null);
        MockGoAwaySent();
        ByteBuf data = DummyData();
        try
        {
            Decode().OnDataRead(ctx, STREAM_ID, data, 10, true);
            Mock.Get(localFlow).Verify(f => f.ReceiveFlowControlledFrame((IHttp2Stream) null, data, 10, true), Times.Once());
            Mock.Get(listener).Verify(l => l.OnDataRead(ctx, It.IsAny<int>(), It.IsAny<ByteBuf>(), It.IsAny<int>(),
                    It.IsAny<bool>()), Times.Never());
        }
        finally
        {
            data.Release();
        }
    }

    [Fact]
    public void DataReadAfterRstStreamForStreamInInvalidStateShouldIgnore()
    {
        // Throw an exception when checking stream state.
        Mock.Get(stream).Setup(s => s.State_()).Returns(IHttp2Stream.State.CLOSED);
        Mock.Get(stream).Setup(s => s.IsResetSent()).Returns(true);
        ByteBuf data = DummyData();
        try
        {
            Decode().OnDataRead(ctx, STREAM_ID, data, 10, true);
            Mock.Get(localFlow).Verify(f => f.ReceiveFlowControlledFrame(stream, data, 10, true), Times.Once());
            Mock.Get(listener).Verify(l => l.OnDataRead(ctx, It.IsAny<int>(), It.IsAny<ByteBuf>(), It.IsAny<int>(),
                    It.IsAny<bool>()), Times.Never());
        }
        finally
        {
            data.Release();
        }
    }

    [Fact]
    public void DataReadWithEndOfStreamShouldcloseStreamRemote()
    {
        ByteBuf data = DummyData();
        try
        {
            Decode().OnDataRead(ctx, STREAM_ID, data, 10, true);
            Mock.Get(localFlow).Verify(f => f.ReceiveFlowControlledFrame(stream, data, 10, true), Times.Once());
            Mock.Get(lifecycleManager).Verify(m => m.CloseStreamRemote(stream, It.Is<Task>(t => t.IsCompletedSuccessfully)), Times.Once());
            Mock.Get(listener).Verify(l => l.OnDataRead(ctx, STREAM_ID, data, 10, true), Times.Once());
        }
        finally
        {
            data.Release();
        }
    }

    [Fact]
    public void ErrorDuringDeliveryShouldReturnCorrectNumberOfBytes()
    {
        ByteBuf data = DummyData();
        int padding = 10;
        int unprocessed = data.ReadableBytes + padding;
        Mock.Get(localFlow).Setup(f => f.UnconsumedBytes(stream)).Returns(() => Volatile.Read(ref unprocessed));
        Mock.Get(localFlow).Setup(f => f.ConsumeBytes(stream, It.IsAny<int>())).Returns<IHttp2Stream, int>((s, delta) =>
        {
            int newValue = Interlocked.Add(ref unprocessed, -delta);
            if (newValue < 0)
            {
                throw new Exception("Returned too many bytes");
            }
            return false;
        });
        // When the listener callback is called, process a few bytes and then throw.
        Mock.Get(listener).Setup(l => l.OnDataRead(ctx, STREAM_ID, It.IsAny<ByteBuf>(), 10, true))
                .Returns<IChannelHandlerContext, int, ByteBuf, int, bool>((c, id, d, p, end) =>
                {
                    localFlow.ConsumeBytes(stream, 4);
                    throw Http2TestUtil.FAKE_EXCEPTION;
                });
        try
        {
            Assert.ThrowsAny<Exception>(() => Decode().OnDataRead(ctx, STREAM_ID, data, padding, true));
            Mock.Get(localFlow)
                    .Verify(f => f.ReceiveFlowControlledFrame(stream, data, padding, true), Times.Once());
            Mock.Get(listener).Verify(l => l.OnDataRead(ctx, STREAM_ID, data, padding, true), Times.Once());
            Assert.Equal(0, localFlow.UnconsumedBytes(stream));
        }
        finally
        {
            data.Release();
        }
    }

    [Fact]
    public void HeadersReadForUnknownStreamShouldThrow()
    {
        Mock.Get(connection).Setup(c => c.Stream(STREAM_ID)).Returns((IHttp2Stream) null);
        Assert.ThrowsAny<Http2Exception>(() =>
                Decode().OnHeadersRead(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, false));
    }

    [Fact]
    public void HeadersReadForStreamThatAlreadySentResetShouldBeIgnored()
    {
        Mock.Get(stream).Setup(s => s.IsResetSent()).Returns(true);
        Decode().OnHeadersRead(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, false);
        Mock.Get(remote).Verify(r => r.CreateStream(It.IsAny<int>(), It.IsAny<bool>()), Times.Never());
        Mock.Get(stream).Verify(s => s.Open(It.IsAny<bool>()), Times.Never());

        // Verify that the event was absorbed and not propagated to the observer.
        Mock.Get(listener).Verify(l => l.OnHeadersRead(ctx, It.IsAny<int>(), It.IsAny<IHttp2Headers>(), It.IsAny<int>(),
                It.IsAny<bool>()), Times.Never());
        Mock.Get(remote).Verify(r => r.CreateStream(It.IsAny<int>(), It.IsAny<bool>()), Times.Never());
        Mock.Get(stream).Verify(s => s.Open(It.IsAny<bool>()), Times.Never());
    }

    [Fact]
    public void HeadersReadForUnknownStreamAfterGoAwayShouldBeIgnored()
    {
        MockGoAwaySent();
        Mock.Get(connection).Setup(c => c.Stream(STREAM_ID)).Returns((IHttp2Stream) null);
        Decode().OnHeadersRead(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, false);
        Mock.Get(remote).Verify(r => r.CreateStream(It.IsAny<int>(), It.IsAny<bool>()), Times.Never());
        Mock.Get(stream).Verify(s => s.Open(It.IsAny<bool>()), Times.Never());

        // Verify that the event was absorbed and not propagated to the observer.
        Mock.Get(listener).Verify(l => l.OnHeadersRead(ctx, It.IsAny<int>(), It.IsAny<IHttp2Headers>(), It.IsAny<int>(),
                It.IsAny<bool>()), Times.Never());
        Mock.Get(remote).Verify(r => r.CreateStream(It.IsAny<int>(), It.IsAny<bool>()), Times.Never());
        Mock.Get(stream).Verify(s => s.Open(It.IsAny<bool>()), Times.Never());
    }

    [Fact]
    public void HeadersReadForUnknownStreamShouldCreateStream()
    {
        int streamId = 5;
        Mock.Get(remote).Setup(r => r.CreateStream(streamId, It.IsAny<bool>())).Returns(stream);
        Decode().OnHeadersRead(ctx, streamId, EmptyHttp2Headers.INSTANCE, 0, false);
        Mock.Get(remote).Verify(r => r.CreateStream(streamId, false), Times.Once());
        Mock.Get(listener).Verify(l => l.OnHeadersRead(ctx, streamId, EmptyHttp2Headers.INSTANCE, 0,
                DEFAULT_PRIORITY_WEIGHT, false, 0, false), Times.Once());
    }

    [Fact]
    public void HeadersReadForUnknownStreamShouldCreateHalfClosedStream()
    {
        int streamId = 5;
        Mock.Get(remote).Setup(r => r.CreateStream(streamId, It.IsAny<bool>())).Returns(stream);
        Decode().OnHeadersRead(ctx, streamId, EmptyHttp2Headers.INSTANCE, 0, true);
        Mock.Get(remote).Verify(r => r.CreateStream(streamId, true), Times.Once());
        Mock.Get(listener).Verify(l => l.OnHeadersRead(ctx, streamId, EmptyHttp2Headers.INSTANCE, 0,
                DEFAULT_PRIORITY_WEIGHT, false, 0, true), Times.Once());
    }

    [Fact]
    public void HeadersReadForPromisedStreamShouldHalfOpenStream()
    {
        Mock.Get(stream).Setup(s => s.State_()).Returns(IHttp2Stream.State.RESERVED_REMOTE);
        Decode().OnHeadersRead(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, false);
        Mock.Get(stream).Verify(s => s.Open(false), Times.Once());
        Mock.Get(listener).Verify(l => l.OnHeadersRead(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0,
                DEFAULT_PRIORITY_WEIGHT, false, 0, false), Times.Once());
    }

    [Fact]
    public void TrailersDoNotEndStreamThrows()
    {
        Decode().OnHeadersRead(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, false);
        // Trailers must end the stream!
        Assert.ThrowsAny<Http2Exception>(() => Decode().OnHeadersRead(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, false));
    }

    [Theory]
    [InlineData(":scheme")]
    [InlineData(":custom-pseudo-header")]
    public void TrailersWithPseudoHeadersThrows(string pseudoHeader)
    {
        Decode().OnHeadersRead(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, false);

        IHttp2Headers trailers = new DefaultHttp2Headers(false);
        trailers.Add(new StringCharSequence(pseudoHeader), new StringCharSequence("something"));
        Http2Exception ex = Assert.ThrowsAny<Http2Exception>(() =>
                Decode().OnHeadersRead(ctx, STREAM_ID, trailers, 0, true));
        Assert.Equal(PROTOCOL_ERROR, ex.Error());
        Assert.Contains(pseudoHeader, ex.Message);
    }

    // Builds a decoder with validateRequiredPseudoHeaders enabled, primed past the preface.
    private IHttp2FrameListener StrictDecode()
    {
        DefaultHttp2ConnectionDecoder strict = new DefaultHttp2ConnectionDecoder(
                connection, encoder, reader, IHttp2PromisedRequestVerifier.ALWAYS_VERIFY, true, true, true, true);
        strict.LifecycleManager(lifecycleManager);
        strict.FrameListener(listener);
        Decode(strict).OnSettingsRead(ctx, new Http2Settings());
        return Decode(strict);
    }

    private static IHttp2Headers Request()
    {
        return new DefaultHttp2Headers().Method(new StringCharSequence("GET")).Scheme(new StringCharSequence("https"))
                .Authority(new StringCharSequence("example.org")).Path(new StringCharSequence("/"));
    }

    private Http2Exception AssertHeadersRejected(IHttp2FrameListener dec, IHttp2Headers headers,
            bool endOfStream)
    {
        Http2Exception ex = Assert.ThrowsAny<Http2Exception>(() =>
                dec.OnHeadersRead(ctx, STREAM_ID, headers, 0, endOfStream));
        Assert.Equal(PROTOCOL_ERROR, ex.Error());
        Mock.Get(listener).Verify(l => l.OnHeadersRead(ctx, STREAM_ID, headers, 0,
                DEFAULT_PRIORITY_WEIGHT, false, 0, endOfStream), Times.Never());
        return ex;
    }

    [Theory]
    [InlineData(":method")]
    [InlineData(":scheme")]
    [InlineData(":path")]
    public void RequestMissingMandatoryPseudoHeaderRejectedWhenEnabled(string missing)
    {
        Mock.Get(connection).Setup(c => c.IsServer()).Returns(true);
        IHttp2Headers headers = Request();
        headers.Remove(new StringCharSequence(missing));
        Assert.Contains(missing, AssertHeadersRejected(StrictDecode(), headers, true).Message);
    }

    [Fact]
    public void RequestEmptyPathRejectedWhenEnabled()
    {
        Mock.Get(connection).Setup(c => c.IsServer()).Returns(true);
        // Disable header-level validation so the empty :path reaches the decoder's own check.
        IHttp2Headers headers = new DefaultHttp2Headers(false)
                .Method(new StringCharSequence("GET")).Scheme(new StringCharSequence("https"))
                .Authority(new StringCharSequence("example.org")).Path(new StringCharSequence(""));
        Assert.Contains(":path", AssertHeadersRejected(StrictDecode(), headers, true).Message);
    }

    [Fact]
    public void EmptyRequestRejectedWhenEnabled()
    {
        Mock.Get(connection).Setup(c => c.IsServer()).Returns(true);
        AssertHeadersRejected(StrictDecode(), EmptyHttp2Headers.INSTANCE, true);
    }

    [Fact]
    public void ValidRequestAcceptedWhenEnabled()
    {
        Mock.Get(connection).Setup(c => c.IsServer()).Returns(true);
        IHttp2Headers headers = Request();
        StrictDecode().OnHeadersRead(ctx, STREAM_ID, headers, 0, true);
        Mock.Get(listener).Verify(l => l.OnHeadersRead(ctx, STREAM_ID, headers, 0,
                DEFAULT_PRIORITY_WEIGHT, false, 0, true), Times.Once());
    }

    [Fact]
    public void ConnectRequestWithoutSchemeAndPathAcceptedWhenEnabled()
    {
        Mock.Get(connection).Setup(c => c.IsServer()).Returns(true);
        // A CONNECT request (RFC 9113, 8.5) omits :scheme and :path and only carries :authority.
        IHttp2Headers headers = new DefaultHttp2Headers().Method(new StringCharSequence("CONNECT"))
                .Authority(new StringCharSequence("example.org:443"));
        StrictDecode().OnHeadersRead(ctx, STREAM_ID, headers, 0, false);
        Mock.Get(listener).Verify(l => l.OnHeadersRead(ctx, STREAM_ID, headers, 0,
                DEFAULT_PRIORITY_WEIGHT, false, 0, false), Times.Once());
    }

    [Fact]
    public void ConnectRequestMissingAuthorityRejectedWhenEnabled()
    {
        Mock.Get(connection).Setup(c => c.IsServer()).Returns(true);
        IHttp2Headers headers = new DefaultHttp2Headers().Method(new StringCharSequence("CONNECT"));
        Assert.Contains(":authority", AssertHeadersRejected(StrictDecode(), headers, false).Message);
    }

    [Fact]
    public void ExtendedConnectRequiresSchemeAndPathWhenEnabled()
    {
        Mock.Get(connection).Setup(c => c.IsServer()).Returns(true);
        // Extended CONNECT (RFC 8441) is identified by :protocol and must include :scheme and :path.
        IHttp2Headers headers = new DefaultHttp2Headers().Method(new StringCharSequence("CONNECT"))
                .Authority(new StringCharSequence("example.org"));
        headers.Add(IHttp2Headers.PseudoHeaderName.PROTOCOL.Value(), new StringCharSequence("websocket"));
        Assert.Contains(":scheme", AssertHeadersRejected(StrictDecode(), headers, false).Message);
    }

    [Fact]
    public void ExtendedConnectAcceptedWhenEnabled()
    {
        Mock.Get(connection).Setup(c => c.IsServer()).Returns(true);
        IHttp2Headers headers = new DefaultHttp2Headers().Method(new StringCharSequence("CONNECT"))
                .Scheme(new StringCharSequence("https"))
                .Authority(new StringCharSequence("example.org")).Path(new StringCharSequence("/chat"));
        headers.Add(IHttp2Headers.PseudoHeaderName.PROTOCOL.Value(), new StringCharSequence("websocket"));
        StrictDecode().OnHeadersRead(ctx, STREAM_ID, headers, 0, false);
        Mock.Get(listener).Verify(l => l.OnHeadersRead(ctx, STREAM_ID, headers, 0,
                DEFAULT_PRIORITY_WEIGHT, false, 0, false), Times.Once());
    }

    [Fact]
    public void ResponseMissingStatusRejectedWhenEnabled()
    {
        // isServer() defaults to false, so inbound HEADERS are treated as a response.
        Assert.Contains(":status", AssertHeadersRejected(StrictDecode(), EmptyHttp2Headers.INSTANCE, true).Message);
    }

    [Fact]
    public void ValidResponseAcceptedWhenEnabled()
    {
        IHttp2Headers headers = new DefaultHttp2Headers().Status(new StringCharSequence("200"));
        StrictDecode().OnHeadersRead(ctx, STREAM_ID, headers, 0, true);
        Mock.Get(listener).Verify(l => l.OnHeadersRead(ctx, STREAM_ID, headers, 0,
                DEFAULT_PRIORITY_WEIGHT, false, 0, true), Times.Once());
    }

    [Fact]
    public void TrailersNotRequiredToCarryPseudoHeadersWhenEnabled()
    {
        Mock.Get(connection).Setup(c => c.IsServer()).Returns(true);
        IHttp2FrameListener dec = StrictDecode();
        // Valid initial request headers.
        dec.OnHeadersRead(ctx, STREAM_ID, Request(), 0, false);
        // Trailers carry no pseudo-headers and must still be accepted (check applies to initial HEADERS only).
        IHttp2Headers trailers = new DefaultHttp2Headers().Add(new StringCharSequence("x-trailer"),
                new StringCharSequence("value"));
        dec.OnHeadersRead(ctx, STREAM_ID, trailers, 0, true);
        Mock.Get(listener).Verify(l => l.OnHeadersRead(ctx, STREAM_ID, trailers, 0,
                DEFAULT_PRIORITY_WEIGHT, false, 0, true), Times.Once());
    }

    [Fact]
    public void DefaultDecoderDoesNotValidateMandatoryPseudoHeaders()
    {
        Mock.Get(connection).Setup(c => c.IsServer()).Returns(true);
        // The default decoder has validateRequiredPseudoHeaders disabled, so an empty request is accepted.
        Decode().OnHeadersRead(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, true);
        Mock.Get(listener).Verify(l => l.OnHeadersRead(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0,
                DEFAULT_PRIORITY_WEIGHT, false, 0, true), Times.Once());
    }

    [Fact]
    public void TooManyHeadersEOSThrows()
    {
        TooManyHeaderThrows(true);
    }

    [Fact]
    public void TooManyHeadersNoEOSThrows()
    {
        TooManyHeaderThrows(false);
    }

    private void TooManyHeaderThrows(bool eos)
    {
        Decode().OnHeadersRead(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, false);
        Decode().OnHeadersRead(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, true);
        // We already received the trailers!
        Assert.ThrowsAny<Http2Exception>(() => Decode().OnHeadersRead(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, eos));
    }

    private static IHttp2Headers InformationalHeaders()
    {
        IHttp2Headers headers = new DefaultHttp2Headers();
        headers.Status(HttpResponseStatus.CONTINUE.CodeAsText());
        return headers;
    }

    [Fact]
    public void InfoHeadersAndTrailersAllowed()
    {
        InfoHeadersAndTrailersAllowed0(true, 1);
    }

    [Fact]
    public void MultipleInfoHeadersAndTrailersAllowed()
    {
        InfoHeadersAndTrailersAllowed0(true, 10);
    }

    [Fact]
    public void InfoHeadersAndTrailersNoEOSThrows()
    {
        InfoHeadersAndTrailersAllowed0(false, 1);
    }

    [Fact]
    public void MultipleInfoHeadersAndTrailersNoEOSThrows()
    {
        InfoHeadersAndTrailersAllowed0(false, 10);
    }

    // CLR: the overloaded private helper of the infoHeadersAndTrailersAllowed() test takes the suffix 0 (xUnit1024,
    // docs/codec-http-porting.md, Core test notes).
    private void InfoHeadersAndTrailersAllowed0(bool eos, int infoHeaderCount)
    {
        for (int i = 0; i < infoHeaderCount; ++i)
        {
            Decode().OnHeadersRead(ctx, STREAM_ID, InformationalHeaders(), 0, false);
        }
        Decode().OnHeadersRead(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, false);
        if (eos)
        {
            Decode().OnHeadersRead(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, eos);
        }
        else
        {
            Assert.ThrowsAny<Http2Exception>(() => Decode().OnHeadersRead(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, eos));
        }
    }

    [Fact]
    public void HeadersReadForPromisedStreamShouldCloseStream()
    {
        Mock.Get(stream).Setup(s => s.State_()).Returns(IHttp2Stream.State.RESERVED_REMOTE);
        Decode().OnHeadersRead(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, true);
        Mock.Get(stream).Verify(s => s.Open(true), Times.Once());
        Mock.Get(lifecycleManager).Verify(m => m.CloseStreamRemote(stream, It.Is<Task>(t => t.IsCompletedSuccessfully)), Times.Once());
        Mock.Get(listener).Verify(l => l.OnHeadersRead(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, 0,
                DEFAULT_PRIORITY_WEIGHT, false, 0, true), Times.Once());
    }

    [Fact]
    public void HeadersDependencyNotCreatedShouldCreateAndSucceed()
    {
        short weight = 1;
        Decode().OnHeadersRead(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, STREAM_DEPENDENCY_ID,
                weight, true, 0, true);
        Mock.Get(listener).Verify(l => l.OnHeadersRead(ctx, STREAM_ID, EmptyHttp2Headers.INSTANCE, STREAM_DEPENDENCY_ID,
                weight, true, 0, true), Times.Once());
        Mock.Get(remoteFlow).Verify(f => f.UpdateDependencyTree(STREAM_ID, STREAM_DEPENDENCY_ID, weight, true), Times.Once());
        Mock.Get(lifecycleManager).Verify(m => m.CloseStreamRemote(stream, It.IsAny<Task>()), Times.Once());
    }

    [Fact]
    public void PushPromiseReadAfterGoAwaySentShouldBeIgnored()
    {
        MockGoAwaySent();
        Decode().OnPushPromiseRead(ctx, STREAM_ID, PUSH_STREAM_ID, EmptyHttp2Headers.INSTANCE, 0);
        Mock.Get(remote).Verify(r => r.ReservePushStream(It.IsAny<int>(), It.IsAny<IHttp2Stream>()), Times.Never());
        Mock.Get(listener).Verify(l => l.OnPushPromiseRead(ctx, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IHttp2Headers>(),
                It.IsAny<int>()), Times.Never());
    }

    [Fact]
    public void PushPromiseReadAfterGoAwayShouldAllowFramesForStreamCreatedByLocalEndpoint()
    {
        MockGoAwaySentShouldAllowFramesForStreamCreatedByLocalEndpoint();
        Decode().OnPushPromiseRead(ctx, STREAM_ID, PUSH_STREAM_ID, EmptyHttp2Headers.INSTANCE, 0);
        Mock.Get(remote).Verify(r => r.ReservePushStream(It.IsAny<int>(), It.IsAny<IHttp2Stream>()), Times.Once());
        Mock.Get(listener).Verify(l => l.OnPushPromiseRead(ctx, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IHttp2Headers>(),
                It.IsAny<int>()), Times.Once());
    }

    [Fact]
    public void PushPromiseReadForUnknownStreamShouldThrow()
    {
        Mock.Get(connection).Setup(c => c.Stream(STREAM_ID)).Returns((IHttp2Stream) null);
        Assert.ThrowsAny<Http2Exception>(() =>
                Decode().OnPushPromiseRead(ctx, STREAM_ID, PUSH_STREAM_ID, EmptyHttp2Headers.INSTANCE, 0));
    }

    [Fact]
    public void PushPromiseReadShouldSucceed()
    {
        Decode().OnPushPromiseRead(ctx, STREAM_ID, PUSH_STREAM_ID, EmptyHttp2Headers.INSTANCE, 0);
        Mock.Get(remote).Verify(r => r.ReservePushStream(PUSH_STREAM_ID, stream), Times.Once());
        Mock.Get(listener).Verify(l => l.OnPushPromiseRead(ctx, STREAM_ID, PUSH_STREAM_ID,
                EmptyHttp2Headers.INSTANCE, 0), Times.Once());
    }

    // The promised stream must still be reserved (i.e. its existence tracked by the connection) even when the
    // Http2PromisedRequestVerifier subsequently rejects the promised request, so that the stream ID cannot be
    // silently reused or its book keeping lost. A follow-up frame for that stream must then be rejected with a
    // stream error rather than a connection error, since the connection is now aware of the stream.
    [Fact]
    public void PushPromiseReadRejectedByVerifierShouldStillReserveStream()
    {
        IHttp2PromisedRequestVerifier verifier = new NotAuthoritativeVerifier();
        DefaultHttp2ConnectionDecoder rejecting =
                new DefaultHttp2ConnectionDecoder(connection, encoder, reader, verifier);
        rejecting.LifecycleManager(lifecycleManager);
        rejecting.FrameListener(listener);
        // Prime the decoder past the connection preface so PUSH_PROMISE is not treated as an unexpected
        // first frame.
        Decode(rejecting).OnSettingsRead(ctx, new Http2Settings());
        IHttp2FrameListener dec = Decode(rejecting);

        Http2Exception ex = Assert.ThrowsAny<Http2Exception>(() =>
                dec.OnPushPromiseRead(ctx, STREAM_ID, PUSH_STREAM_ID, EmptyHttp2Headers.INSTANCE, 0));
        Assert.Equal(PROTOCOL_ERROR, ex.Error());
        Assert.Equal(PUSH_STREAM_ID, Http2Exception.StreamId(ex));

        Mock.Get(remote).Verify(r => r.ReservePushStream(PUSH_STREAM_ID, stream), Times.Once());
        Mock.Get(listener).Verify(l => l.OnPushPromiseRead(ctx, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IHttp2Headers>(),
                It.IsAny<int>()), Times.Never());

        // Because the promised stream was reserved despite the rejection, the connection now knows about its
        // existence. A follow-up HEADERS frame for that stream (e.g. once it has been reset and closed) must
        // therefore be rejected with a stream error scoped to that stream, rather than a connection error.
        Mock.Get(connection).Setup(c => c.Stream(PUSH_STREAM_ID)).Returns(pushStream);
        Mock.Get(pushStream).Setup(s => s.State_()).Returns(IHttp2Stream.State.CLOSED);
        Http2Exception followUpEx = Assert.ThrowsAny<Http2Exception>(() =>
                dec.OnHeadersRead(ctx, PUSH_STREAM_ID, EmptyHttp2Headers.INSTANCE, 0, true));
        Assert.Equal(STREAM_CLOSED, followUpEx.Error());
        Assert.Equal(PUSH_STREAM_ID, Http2Exception.StreamId(followUpEx));
    }

    // CLR: the anonymous Http2PromisedRequestVerifier of pushPromiseReadRejectedByVerifierShouldStillReserveStream.
    private sealed class NotAuthoritativeVerifier : IHttp2PromisedRequestVerifier
    {
        public bool IsAuthoritative(IChannelHandlerContext ctx, IHttp2Headers headers)
        {
            return false;
        }

        public bool IsCacheable(IHttp2Headers headers)
        {
            return true;
        }

        public bool IsSafe(IHttp2Headers headers)
        {
            return true;
        }
    }

    // A PUSH_PROMISE field block always carries a request header set (RFC 9113, 8.4.1). When required
    // pseudo-header validation is enabled, an incomplete promised request MUST be rejected with a PROTOCOL_ERROR
    // on the promised stream, not the stream that carried the PUSH_PROMISE frame.
    [Fact]
    public void PushPromiseIncompleteRequestRejectedWhenEnabled()
    {
        IHttp2FrameListener dec = StrictDecode();
        Http2Exception ex = Assert.ThrowsAny<Http2Exception>(() =>
                dec.OnPushPromiseRead(ctx, STREAM_ID, PUSH_STREAM_ID, EmptyHttp2Headers.INSTANCE, 0));
        Assert.Equal(PROTOCOL_ERROR, ex.Error());
        Assert.Equal(PUSH_STREAM_ID, Http2Exception.StreamId(ex));
        Mock.Get(remote).Verify(r => r.ReservePushStream(PUSH_STREAM_ID, It.IsAny<IHttp2Stream>()), Times.Once());
        Mock.Get(listener).Verify(l => l.OnPushPromiseRead(ctx, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IHttp2Headers>(),
                It.IsAny<int>()), Times.Never());
    }

    [Fact]
    public void PushPromiseCompleteRequestAcceptedWhenEnabled()
    {
        IHttp2Headers headers = Request();
        StrictDecode().OnPushPromiseRead(ctx, STREAM_ID, PUSH_STREAM_ID, headers, 0);
        Mock.Get(remote).Verify(r => r.ReservePushStream(PUSH_STREAM_ID, stream), Times.Once());
        Mock.Get(listener).Verify(l => l.OnPushPromiseRead(ctx, STREAM_ID, PUSH_STREAM_ID, headers, 0), Times.Once());
    }

    // https://www.rfc-editor.org/rfc/rfc9113.html#name-server-push
    // Promised requests cannot include any content or a trailer section.
    [Fact]
    public void TestPromiseWithContentLength()
    {
        IHttp2FrameListener dec = StrictDecode();
        IHttp2Headers headers = Request().SetInt(HttpHeaderNames.CONTENT_LENGTH, 10);
        Assert.ThrowsAny<Http2Exception>(() =>
            dec.OnPushPromiseRead(ctx, STREAM_ID, PUSH_STREAM_ID, headers, 0));
    }

    // https://www.rfc-editor.org/rfc/rfc9113.html#name-server-push
    // Promised requests cannot include any content or a trailer section.
    [Fact]
    public void TestPromiseWithContentLengthOfZero()
    {
        IHttp2FrameListener dec = StrictDecode();
        IHttp2Headers headers = Request().SetInt(HttpHeaderNames.CONTENT_LENGTH, 0);
        dec.OnPushPromiseRead(ctx, STREAM_ID, PUSH_STREAM_ID, headers, 0);
        Mock.Get(remote).Verify(r => r.ReservePushStream(PUSH_STREAM_ID, stream), Times.Once());
        Mock.Get(listener).Verify(l => l.OnPushPromiseRead(ctx, STREAM_ID, PUSH_STREAM_ID, headers, 0), Times.Once());
    }

    [Fact]
    public void PriorityReadAfterGoAwaySentShouldAllowFramesForStreamCreatedByLocalEndpoint()
    {
        MockGoAwaySentShouldAllowFramesForStreamCreatedByLocalEndpoint();
        Decode().OnPriorityRead(ctx, STREAM_ID, 0, (short) 255, true);
        Mock.Get(remoteFlow).Verify(f => f.UpdateDependencyTree(STREAM_ID, 0, (short) 255, true), Times.Once());
        Mock.Get(listener).Verify(l => l.OnPriorityRead(ctx, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<short>(),
                It.IsAny<bool>()), Times.Once());
    }

    [Fact]
    public void PriorityReadForUnknownStreamShouldNotBeIgnored()
    {
        Mock.Get(connection).Setup(c => c.Stream(STREAM_ID)).Returns((IHttp2Stream) null);
        Decode().OnPriorityRead(ctx, STREAM_ID, 0, (short) 255, true);
        Mock.Get(remoteFlow).Verify(f => f.UpdateDependencyTree(STREAM_ID, 0, (short) 255, true), Times.Once());
        Mock.Get(listener).Verify(l => l.OnPriorityRead(ctx, STREAM_ID, 0, (short) 255, true), Times.Once());
    }

    [Fact]
    public void PriorityReadShouldNotCreateNewStream()
    {
        Mock.Get(connection).Setup(c => c.StreamMayHaveExisted(STREAM_ID)).Returns(false);
        Mock.Get(connection).Setup(c => c.Stream(STREAM_ID)).Returns((IHttp2Stream) null);
        Decode().OnPriorityRead(ctx, STREAM_ID, STREAM_DEPENDENCY_ID, (short) 255, true);
        Mock.Get(remoteFlow).Verify(f => f.UpdateDependencyTree(STREAM_ID, STREAM_DEPENDENCY_ID, (short) 255, true), Times.Once());
        Mock.Get(listener).Verify(l => l.OnPriorityRead(ctx, STREAM_ID, STREAM_DEPENDENCY_ID, (short) 255, true), Times.Once());
        Mock.Get(remote).Verify(r => r.CreateStream(STREAM_ID, It.IsAny<bool>()), Times.Never());
        Mock.Get(stream).Verify(s => s.Open(It.IsAny<bool>()), Times.Never());
    }

    [Fact]
    public void WindowUpdateReadAfterGoAwaySentShouldBeIgnored()
    {
        MockGoAwaySent();
        Decode().OnWindowUpdateRead(ctx, STREAM_ID, 10);
        Mock.Get(remoteFlow).Verify(f => f.IncrementWindowSize(It.IsAny<IHttp2Stream>(), It.IsAny<int>()), Times.Never());
        Mock.Get(listener).Verify(l => l.OnWindowUpdateRead(ctx, It.IsAny<int>(), It.IsAny<int>()), Times.Never());
    }

    [Fact]
    public void WindowUpdateReadAfterGoAwaySentShouldAllowFramesForStreamCreatedByLocalEndpoint()
    {
        MockGoAwaySentShouldAllowFramesForStreamCreatedByLocalEndpoint();
        Decode().OnWindowUpdateRead(ctx, STREAM_ID, 10);
        Mock.Get(remoteFlow).Verify(f => f.IncrementWindowSize(It.IsAny<IHttp2Stream>(), It.IsAny<int>()), Times.Once());
        Mock.Get(listener).Verify(l => l.OnWindowUpdateRead(ctx, It.IsAny<int>(), It.IsAny<int>()), Times.Once());
    }

    [Fact]
    public void WindowUpdateReadForUnknownStreamShouldThrow()
    {
        Mock.Get(connection).Setup(c => c.StreamMayHaveExisted(STREAM_ID)).Returns(false);
        Mock.Get(connection).Setup(c => c.Stream(STREAM_ID)).Returns((IHttp2Stream) null);
        Assert.ThrowsAny<Http2Exception>(() => Decode().OnWindowUpdateRead(ctx, STREAM_ID, 10));
    }

    [Fact]
    public void WindowUpdateReadForUnknownStreamShouldBeIgnored()
    {
        Mock.Get(connection).Setup(c => c.Stream(STREAM_ID)).Returns((IHttp2Stream) null);
        Decode().OnWindowUpdateRead(ctx, STREAM_ID, 10);
        Mock.Get(remoteFlow).Verify(f => f.IncrementWindowSize(It.IsAny<IHttp2Stream>(), It.IsAny<int>()), Times.Never());
        Mock.Get(listener).Verify(l => l.OnWindowUpdateRead(ctx, It.IsAny<int>(), It.IsAny<int>()), Times.Never());
    }

    [Fact]
    public void WindowUpdateReadShouldSucceed()
    {
        Decode().OnWindowUpdateRead(ctx, STREAM_ID, 10);
        Mock.Get(remoteFlow).Verify(f => f.IncrementWindowSize(stream, 10), Times.Once());
        Mock.Get(listener).Verify(l => l.OnWindowUpdateRead(ctx, STREAM_ID, 10), Times.Once());
    }

    [Fact]
    public void RstStreamReadAfterGoAwayShouldSucceed()
    {
        Mock.Get(connection).Setup(c => c.GoAwaySent()).Returns(true);
        Decode().OnRstStreamRead(ctx, STREAM_ID, PROTOCOL_ERROR.Code());
        Mock.Get(lifecycleManager).Verify(m => m.CloseStream(stream, It.Is<Task>(t => t.IsCompletedSuccessfully)), Times.Once());
        Mock.Get(listener).Verify(l => l.OnRstStreamRead(ctx, It.IsAny<int>(), It.IsAny<long>()), Times.Once());
    }

    [Fact]
    public void RstStreamReadForUnknownStreamShouldThrow()
    {
        Mock.Get(connection).Setup(c => c.StreamMayHaveExisted(STREAM_ID)).Returns(false);
        Mock.Get(connection).Setup(c => c.Stream(STREAM_ID)).Returns((IHttp2Stream) null);
        Assert.ThrowsAny<Http2Exception>(() => Decode().OnRstStreamRead(ctx, STREAM_ID, PROTOCOL_ERROR.Code()));
    }

    [Fact]
    public void RstStreamReadForUnknownStreamShouldBeIgnored()
    {
        Mock.Get(connection).Setup(c => c.Stream(STREAM_ID)).Returns((IHttp2Stream) null);
        Decode().OnRstStreamRead(ctx, STREAM_ID, PROTOCOL_ERROR.Code());
        Mock.Get(lifecycleManager).Verify(m => m.CloseStream(stream, It.Is<Task>(t => t.IsCompletedSuccessfully)),
                Times.Never());
        Mock.Get(listener).Verify(l => l.OnRstStreamRead(ctx, It.IsAny<int>(), It.IsAny<long>()), Times.Never());
    }

    [Fact]
    public void RstStreamReadShouldCloseStream()
    {
        Decode().OnRstStreamRead(ctx, STREAM_ID, PROTOCOL_ERROR.Code());
        Mock.Get(lifecycleManager).Verify(m => m.CloseStream(stream, It.Is<Task>(t => t.IsCompletedSuccessfully)), Times.Once());
        Mock.Get(listener).Verify(l => l.OnRstStreamRead(ctx, STREAM_ID, PROTOCOL_ERROR.Code()), Times.Once());
    }

    [Fact]
    public void RstStreamOnIdleStreamShouldThrow()
    {
        Mock.Get(stream).Setup(s => s.State_()).Returns(IHttp2Stream.State.IDLE);
        Assert.ThrowsAny<Http2Exception>(() => Decode().OnRstStreamRead(ctx, STREAM_ID, PROTOCOL_ERROR.Code()));
        Mock.Get(listener).Verify(l => l.OnRstStreamRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<long>()), Times.Never());
    }

    [Fact]
    public void PingReadWithAckShouldNotifyListener()
    {
        Decode().OnPingAckRead(ctx, 0L);
        Mock.Get(listener).Verify(l => l.OnPingAckRead(ctx, 0L), Times.Once());
    }

    [Fact]
    public void PingReadShouldReplyWithAck()
    {
        Decode().OnPingRead(ctx, 0L);
        Mock.Get(encoder).Verify(e => e.WritePingAsync(ctx, true, 0L, It.IsAny<TaskCompletionSource>()), Times.Once());
        Mock.Get(listener).Verify(l => l.OnPingAckRead(ctx, It.IsAny<long>()), Times.Never());
    }

    // CLR: the source calls the @Deprecated constructor (autoAckPing), which is ported with [Obsolete] (CS0612).
#pragma warning disable CS0612
    [Fact]
    public void PingReadShouldRespectNoAutoAck()
    {
        IChannelHandlerContext ctx = new Mock<IChannelHandlerContext>().Object;
        IHttp2ConnectionEncoder encoder = new Mock<IHttp2ConnectionEncoder>().Object;
        IHttp2ConnectionDecoder decoder = new DefaultHttp2ConnectionDecoder(connection, encoder, reader,
                IHttp2PromisedRequestVerifier.ALWAYS_VERIFY, true, false);
        IHttp2FrameListener listener = new Mock<IHttp2FrameListener>().Object;
        SetupCodec(ctx, encoder, decoder, listener);
        Decode(decoder).OnPingRead(ctx, 0L);
        Mock.Get(encoder).Verify(e => e.WritePingAsync(ctx, true, 0L, It.IsAny<TaskCompletionSource>()), Times.Never());
    }
#pragma warning restore CS0612

    [Fact]
    public void SettingsReadWithAckShouldNotifyListener()
    {
        Decode().OnSettingsAckRead(ctx);
        // Take into account the time this was called during setup().
        Mock.Get(listener).Verify(l => l.OnSettingsAckRead(ctx), Times.Exactly(2));
    }

    [Fact]
    public void SettingsReadShouldSetValues()
    {
        Http2Settings settings = new Http2Settings();
        settings.PushEnabled(true);
        settings.InitialWindowSize(123);
        settings.MaxConcurrentStreams(456);
        settings.HeaderTableSize(789);
        Decode().OnSettingsRead(ctx, settings);
        Mock.Get(encoder).Verify(e => e.RemoteSettings(settings), Times.Once());
        Mock.Get(listener).Verify(l => l.OnSettingsRead(ctx, settings), Times.Once());
    }

    [Fact]
    public void GoAwayShouldReadShouldUpdateConnectionState()
    {
        Decode().OnGoAwayRead(ctx, 1, 2L, EmptyBuffer);
        Mock.Get(connection).Verify(c => c.GoAwayReceived(1, 2L, EmptyBuffer), Times.Once());
        Mock.Get(listener).Verify(l => l.OnGoAwayRead(ctx, 1, 2L, EmptyBuffer), Times.Once());
    }

    [Fact]
    public void DataContentLengthMissmatch()
    {
        DataContentLengthInvalid0(false);
    }

    [Fact]
    public void DataContentLengthInvalid()
    {
        DataContentLengthInvalid0(true);
    }

    // CLR: the private helper overloads the dataContentLengthInvalid() test, so it takes the suffix 0 (xUnit1024).
    private void DataContentLengthInvalid0(bool negative)
    {
        ByteBuf data = DummyData();
        int padding = 10;
        int processedBytes = data.ReadableBytes + padding;
        MockFlowControl(processedBytes);
        try
        {
            if (negative)
            {
                Assert.ThrowsAny<Http2Exception.StreamException>(() =>
                        Decode().OnHeadersRead(ctx, STREAM_ID, new DefaultHttp2Headers()
                                .SetLong(HttpHeaderNames.CONTENT_LENGTH, -1L), padding, false));
            }
            else
            {
                Decode().OnHeadersRead(ctx, STREAM_ID, new DefaultHttp2Headers()
                        .SetLong(HttpHeaderNames.CONTENT_LENGTH, 1L), padding, false);
                Assert.ThrowsAny<Http2Exception.StreamException>(() =>
                        Decode().OnDataRead(ctx, STREAM_ID, data, padding, true));
                Mock.Get(localFlow).Verify(f => f.ReceiveFlowControlledFrame(stream, data, padding, true), Times.Once());
                Mock.Get(localFlow).Verify(f => f.ConsumeBytes(stream, processedBytes), Times.Once());

                Mock.Get(listener).Verify(l => l.OnHeadersRead(ctx, It.IsAny<int>(),
                        It.IsAny<IHttp2Headers>(), 0, DEFAULT_PRIORITY_WEIGHT, false,
                        padding, false), Times.Exactly(1));
            }
            // Verify that the event was absorbed and not propagated to the observer.
            Mock.Get(listener).Verify(l => l.OnDataRead(ctx, It.IsAny<int>(), It.IsAny<ByteBuf>(), It.IsAny<int>(),
                    It.IsAny<bool>()), Times.Never());
        }
        finally
        {
            data.Release();
        }
    }

    [Fact]
    public void HeadersContentLengthPositiveSign()
    {
        HeadersContentLengthSign("+1");
    }

    [Fact]
    public void HeadersContentLengthNegativeSign()
    {
        HeadersContentLengthSign("-1");
    }

    private void HeadersContentLengthSign(string length)
    {
        int padding = 10;
        Mock.Get(connection).Setup(c => c.IsServer()).Returns(true);

        Assert.ThrowsAny<Http2Exception.StreamException>(() =>
                Decode().OnHeadersRead(ctx, STREAM_ID, new DefaultHttp2Headers()
                        .Set(HttpHeaderNames.CONTENT_LENGTH, new StringCharSequence(length)), padding, false));

        // Verify that the event was absorbed and not propagated to the observer.
        Mock.Get(listener).Verify(l => l.OnHeadersRead(ctx, It.IsAny<int>(),
                It.IsAny<IHttp2Headers>(), It.IsAny<int>(), It.IsAny<short>(), It.IsAny<bool>(), It.IsAny<int>(),
                It.IsAny<bool>()), Times.Never());
    }

    [Fact]
    public void HeadersContentLengthMissmatch()
    {
        HeadersContentLength(false);
    }

    [Fact]
    public void HeadersContentLengthInvalid()
    {
        HeadersContentLength(true);
    }

    private void HeadersContentLength(bool negative)
    {
        int padding = 10;
        Mock.Get(connection).Setup(c => c.IsServer()).Returns(true);
        Assert.ThrowsAny<Http2Exception.StreamException>(() =>
                Decode().OnHeadersRead(ctx, STREAM_ID, new DefaultHttp2Headers()
                        .SetLong(HttpHeaderNames.CONTENT_LENGTH, negative ? -1L : 1L), padding, true));

        // Verify that the event was absorbed and not propagated to the observer.
        Mock.Get(listener).Verify(l => l.OnHeadersRead(ctx, It.IsAny<int>(),
                It.IsAny<IHttp2Headers>(), It.IsAny<int>(), It.IsAny<short>(), It.IsAny<bool>(), It.IsAny<int>(),
                It.IsAny<bool>()), Times.Never());
    }

    [Fact]
    public void MultipleHeadersContentLengthSame()
    {
        MultipleHeadersContentLength(true);
    }

    [Fact]
    public void MultipleHeadersContentLengthDifferent()
    {
        MultipleHeadersContentLength(false);
    }

    private void MultipleHeadersContentLength(bool same)
    {
        int padding = 10;
        Mock.Get(connection).Setup(c => c.IsServer()).Returns(true);
        IHttp2Headers headers = new DefaultHttp2Headers();
        if (same)
        {
            headers.AddLong(HttpHeaderNames.CONTENT_LENGTH, 0);
            headers.AddLong(HttpHeaderNames.CONTENT_LENGTH, 0);
        }
        else
        {
            headers.AddLong(HttpHeaderNames.CONTENT_LENGTH, 0);
            headers.AddLong(HttpHeaderNames.CONTENT_LENGTH, 1);
        }

        if (same)
        {
            Decode().OnHeadersRead(ctx, STREAM_ID, headers, padding, true);
            Mock.Get(listener).Verify(l => l.OnHeadersRead(ctx, It.IsAny<int>(),
                    It.IsAny<IHttp2Headers>(), It.IsAny<int>(), It.IsAny<short>(), It.IsAny<bool>(), It.IsAny<int>(),
                    It.IsAny<bool>()), Times.Exactly(1));
            Assert.Single(headers.GetAll(HttpHeaderNames.CONTENT_LENGTH));
        }
        else
        {
            Assert.ThrowsAny<Http2Exception.StreamException>(() =>
                    Decode().OnHeadersRead(ctx, STREAM_ID, headers, padding, true));

            // Verify that the event was absorbed and not propagated to the observer.
            Mock.Get(listener).Verify(l => l.OnHeadersRead(ctx, It.IsAny<int>(),
                    It.IsAny<IHttp2Headers>(), It.IsAny<int>(), It.IsAny<short>(), It.IsAny<bool>(), It.IsAny<int>(),
                    It.IsAny<bool>()), Times.Never());
        }
    }

    private static ByteBuf DummyData()
    {
        // The buffer is purposely 8 bytes so it will even work for a ping frame.
        return WrappedBuffer(Encoding.UTF8.GetBytes("abcdefgh"));
    }

    /**
     * Calls the decode method on the handler and gets back the captured internal listener
     */
    private IHttp2FrameListener Decode()
    {
        return Decode(decoder);
    }

    private IHttp2FrameListener Decode(IHttp2ConnectionDecoder decoder)
    {
        IHttp2FrameListener internalListener = null;
        Mock.Get(reader).Setup(r => r.ReadFrame(ctx, It.IsAny<ByteBuf>(), It.IsAny<IHttp2FrameListener>()))
                .Callback<IChannelHandlerContext, ByteBuf, IHttp2FrameListener>((c, b, l) => internalListener = l);
        decoder.DecodeFrame(ctx, EmptyBuffer, Array.Empty<object>());
        return internalListener;
    }

    private void MockFlowControl(int processedBytes)
    {
        Mock.Get(listener).Setup(l => l.OnDataRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<ByteBuf>(), It.IsAny<int>(), It.IsAny<bool>())).Returns(processedBytes);
    }

    private void MockGoAwaySent()
    {
        Mock.Get(connection).Setup(c => c.GoAwaySent()).Returns(true);
        Mock.Get(remote).Setup(r => r.IsValidStreamId(STREAM_ID)).Returns(true);
        Mock.Get(remote).Setup(r => r.LastStreamKnownByPeer()).Returns(0);
    }

    private void MockGoAwaySentShouldAllowFramesForStreamCreatedByLocalEndpoint()
    {
        Mock.Get(connection).Setup(c => c.GoAwaySent()).Returns(true);
        Mock.Get(remote).Setup(r => r.IsValidStreamId(STREAM_ID)).Returns(false);
        Mock.Get(remote).Setup(r => r.LastStreamKnownByPeer()).Returns(0);
    }
}
