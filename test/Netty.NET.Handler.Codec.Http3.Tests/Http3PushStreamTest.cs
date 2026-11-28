/*
 * Copyright 2021 The Netty Project
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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;
using Assert = XunitAssertMessages.AssertM;
using static Netty.NET.Handler.Codec.Http3.Tests.Http3TestUtils;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: @BeforeEach setUp() runs in the constructor and @AfterEach tearDown() is Dispose() (xUnit creates a test class
// instance per test and disposes it after the test). The project has no Moq (docs/codec-http3-porting.md), so
// mock(ChannelHandlerContext.class) with when(serverControlStreamHandlerCtx.channel())
// .thenReturn(serverLocalControlStream) is the private nested class MockChannelHandlerContext with Mockito's default
// answers: Channel returns the stubbed stream and every other member does nothing and returns null, false or the default
// value. writeAndFlush(maxPushIdFrame).addListener(CLOSE_ON_FAILURE) is Http3CodecUtils.CloseOnFailure(channel,
// channel.WriteAndFlushAsync(...)) (docs/codec-http3-porting.md, Shared CLR adaptations: a Task has no channel()).
// Http3ServerPushStreamManager.newPushStream(pushId, handler) is NewPushStreamAsync and createStream(type, handler)
// CreateStreamAsync, whose get() is GetAwaiter().GetResult(). The anonymous Http3PushStreamServerInitializer and
// Http3PushStreamClientInitializer with the noop initPushStream are the private nested classes
// NoopPushStreamServerInitializer and NoopPushStreamClientInitializer; the Http3FrameCodecFactory lambda
// (__, encodeState, decodeState, ___) -> clientConnectionHandler.newCodec(encodeState, decodeState) is the private nested
// class ClientCodecFactory, the NonStandardHttp3SettingsValidator lambda (id, v) -> false the private nested class
// NoNonStandardSettings, the LongFunction lambda __ -> new Http3PushStreamClientInitializer() {...} a Func<long,
// IChannelHandler> and ChannelDuplexHandler::new the Func<IChannelHandler> () => new ChannelDuplexHandler(). The
// handler types and the connection handlers' localControlStreamHandler/remoteControlStreamHandler/newCodec are internal
// (package-private in Java) and reached through InternalsVisibleTo. Http3RequestStreamFrame... is params
// IHttp3RequestStreamFrame[]; ReferenceCountUtil.retain(frame) is ReferenceCountUtil.Retain. alloc() is Alloc(),
// readOutbound()/readInbound() are ReadOutbound<T>()/ReadInbound<T>() and isActive() the IsActive property. The throws
// clauses have no counterpart.
public class Http3PushStreamTest : IDisposable
{
    private Http3ServerConnectionHandler serverConnectionHandler;
    private EmbeddedQuicChannel serverChannel;
    private Http3ServerPushStreamManager pushStreamManager;
    private Http3ClientConnectionHandler clientConnectionHandler;
    private EmbeddedQuicChannel clientChannel;
    private int maxPushId;
    private IChannelHandlerContext serverControlStreamHandlerCtx;
    private EmbeddedQuicStreamChannel serverLocalControlStream;
    private EmbeddedQuicStreamChannel clientLocalControlStream;

    public Http3PushStreamTest()
    {
        SetUp();
    }

    private void SetUp()
    {
        serverConnectionHandler = new Http3ServerConnectionHandler(new ChannelDuplexHandler(), null, null, null, true);
        serverChannel = new EmbeddedQuicChannel(true, serverConnectionHandler);
        serverLocalControlStream = (EmbeddedQuicStreamChannel) Http3.GetLocalControlStream(serverChannel);
        Assert.NotNull(serverLocalControlStream);
        serverControlStreamHandlerCtx = new MockChannelHandlerContext(serverLocalControlStream);

        serverConnectionHandler.localControlStreamHandler.ChannelRead(serverControlStreamHandlerCtx,
                new DefaultHttp3SettingsFrame());
        pushStreamManager = new Http3ServerPushStreamManager(serverChannel);

        clientConnectionHandler = new Http3ClientConnectionHandler(null, null, null, null, true);
        clientChannel = new EmbeddedQuicChannel(false, clientConnectionHandler);
        clientLocalControlStream = (EmbeddedQuicStreamChannel) Http3.GetLocalControlStream(clientChannel);
        Assert.NotNull(clientLocalControlStream);

        Assert.True(serverLocalControlStream.ReleaseOutbound());
        Assert.True(clientLocalControlStream.ReleaseOutbound());
        maxPushId = 0; // allow 1 push
        SendMaxPushId(maxPushId);
    }

    private void SendMaxPushId(int maxPushId)
    {
        DefaultHttp3MaxPushIdFrame maxPushIdFrame = new DefaultHttp3MaxPushIdFrame(maxPushId);
        serverConnectionHandler.localControlStreamHandler.ChannelRead(serverControlStreamHandlerCtx, maxPushIdFrame);
        Assert.True(serverChannel.IsActive);

        Http3CodecUtils.CloseOnFailure(clientLocalControlStream,
                clientLocalControlStream.WriteAndFlushAsync(maxPushIdFrame));
        Assert.True(clientChannel.IsActive);
        Assert.True(clientLocalControlStream.ReleaseOutbound());
    }

    public void Dispose()
    {
        TearDown();
    }

    private void TearDown()
    {
        Assert.False(serverLocalControlStream.Finish());
        Assert.False(serverChannel.Finish());
        Assert.False(clientLocalControlStream.Finish());
        Assert.False(clientChannel.Finish());
    }

    [Fact]
    public void HeadersData()
    {
        TestWriteAndReadFrames(Http3TestUtils.NewHeadersFrameWithPseudoHeaders(), NewDataFrame());
    }

    [Fact]
    public void HeadersDataTrailers()
    {
        TestWriteAndReadFrames(Http3TestUtils.NewHeadersFrameWithPseudoHeaders(), NewDataFrame(),
                new DefaultHttp3HeadersFrame());
    }

    [Fact]
    public void PushPromise()
    {
        EmbeddedQuicStreamChannel serverStream = NewServerStream();
        ReadStreamHeader(serverStream).Release();

        try
        {
            Assert.ThrowsAny<Http3Exception>(() => serverStream.WriteOutbound(new DefaultHttp3PushPromiseFrame(1)));
        }
        finally
        {
            Assert.False(serverStream.Finish());
        }
    }

    [Fact]
    public async Task InvalidPushId()
    {
        // CLR: get() in the test method is an await of the completed Task (xUnit1031), with the test's
        // cancellation token (xUnit1051).
        EmbeddedQuicStreamChannel serverStream =
                (EmbeddedQuicStreamChannel) await serverChannel.CreateStreamAsync(QuicStreamType.UNIDIRECTIONAL,
                        new NoopPushStreamServerInitializer(maxPushId + 1), TestContext.Current.CancellationToken);
        EmbeddedQuicStreamChannel clientStream = NewClientStreamUninitialized();

        try
        {
            ByteBuf streamHeader = ReadStreamHeader(serverStream);
            Assert.False(clientStream.WriteInbound(streamHeader));
            VerifyClose(Http3ErrorCode.H3_ID_ERROR, clientChannel);
        }
        finally
        {
            Assert.False(serverStream.Finish());
            Assert.False(clientStream.Finish());
        }
    }

    // CLR: the anonymous Http3PushStreamServerInitializer of invalidPushId().
    private sealed class NoopPushStreamServerInitializer : Http3PushStreamServerInitializer
    {
        internal NoopPushStreamServerInitializer(long pushId) : base(pushId)
        {
        }

        protected internal override void InitPushStream(IQuicStreamChannel ch)
        {
            // noop
        }
    }

    [Fact]
    public void UpdateMaxPushId()
    {
        TestWriteAndReadFrames(Http3TestUtils.NewHeadersFrameWithPseudoHeaders(), NewDataFrame());
        Assert.False(pushStreamManager.IsPushAllowed());

        SendMaxPushId(maxPushId + 1);
        TestWriteAndReadFrames(Http3TestUtils.NewHeadersFrameWithPseudoHeaders(), NewDataFrame());
    }

    private void TestWriteAndReadFrames(params IHttp3RequestStreamFrame[] frames)
    {
        EmbeddedQuicStreamChannel serverStream = NewServerStream();
        EmbeddedQuicStreamChannel clientStream = NewClientStream(serverStream);

        try
        {
            foreach (IHttp3RequestStreamFrame frame in frames)
            {
                WriteAndReadFrame(serverStream, clientStream, frame);
            }
        }
        finally
        {
            Assert.False(serverStream.Finish());
            Assert.False(clientStream.Finish());
        }
    }

    private static void WriteAndReadFrame(EmbeddedQuicStreamChannel serverStream,
                                          EmbeddedQuicStreamChannel clientStream, IHttp3RequestStreamFrame frame)
    {
        ReferenceCountUtil.Retain(frame); // retain so that we can compare later
        Assert.True(serverStream.WriteOutbound(frame));
        ByteBuf encodedFrame = serverStream.ReadOutbound<ByteBuf>();
        Assert.NotNull(encodedFrame);
        Assert.True(clientStream.WriteInbound(encodedFrame));
        IHttp3RequestStreamFrame readFrame = clientStream.ReadInbound<IHttp3RequestStreamFrame>();
        AssertFrameEquals(frame, readFrame); // releases both the frames.

        Assert.True(serverStream.IsActive);
        Assert.True(clientStream.IsActive);
    }

    private DefaultHttp3DataFrame NewDataFrame()
    {
        return new DefaultHttp3DataFrame(serverChannel.Alloc().Buffer(1).WriteByte(1));
    }

    private EmbeddedQuicStreamChannel NewServerStream()
    {
        Assert.True(pushStreamManager.IsPushAllowed());
        long pushId = pushStreamManager.ReserveNextPushId();
        return (EmbeddedQuicStreamChannel) pushStreamManager.NewPushStreamAsync(pushId, null).GetAwaiter().GetResult();
    }

    private EmbeddedQuicStreamChannel NewClientStream(EmbeddedQuicStreamChannel serverStream)
    {
        EmbeddedQuicStreamChannel clientStream = NewClientStreamUninitialized();
        ByteBuf streamHeader = ReadStreamHeader(serverStream);
        Assert.False(clientStream.WriteInbound(streamHeader));
        Assert.True(clientChannel.IsActive);
        return clientStream;
    }

    private EmbeddedQuicStreamChannel NewClientStreamUninitialized()
    {
        return (EmbeddedQuicStreamChannel) clientChannel.CreateStreamAsync(QuicStreamType.UNIDIRECTIONAL,
                new Http3UnidirectionalStreamInboundClientHandler(
                        new ClientCodecFactory(clientConnectionHandler),
                        new NoNonStandardSettings(),
                        clientConnectionHandler.localControlStreamHandler,
                        clientConnectionHandler.remoteControlStreamHandler, null,
                        __ => new NoopPushStreamClientInitializer(),
                        () => new ChannelDuplexHandler(), () => new ChannelDuplexHandler()))
                .GetAwaiter().GetResult();
    }

    // CLR: the Http3FrameCodecFactory lambda of newClientStreamUninitialized().
    private sealed class ClientCodecFactory : Http3FrameCodec.IHttp3FrameCodecFactory
    {
        private readonly Http3ClientConnectionHandler clientConnectionHandler;

        internal ClientCodecFactory(Http3ClientConnectionHandler clientConnectionHandler)
        {
            this.clientConnectionHandler = clientConnectionHandler;
        }

        public IChannelHandler NewCodec(IHttp3FrameTypeValidator validator, IHttp3RequestStreamCodecState encodeState,
                                        IHttp3RequestStreamCodecState decodeState,
                                        Http3Settings.INonStandardHttp3SettingsValidator nonStandardSettingsValidator)
        {
            return clientConnectionHandler.NewCodec(encodeState, decodeState);
        }
    }

    // CLR: the NonStandardHttp3SettingsValidator lambda (id, v) -> false of newClientStreamUninitialized().
    private sealed class NoNonStandardSettings : Http3Settings.INonStandardHttp3SettingsValidator
    {
        public bool Validate(long id, long value)
        {
            return false;
        }
    }

    // CLR: the anonymous Http3PushStreamClientInitializer of newClientStreamUninitialized().
    private sealed class NoopPushStreamClientInitializer : Http3PushStreamClientInitializer
    {
        protected override void InitPushStream(IQuicStreamChannel ch)
        {
            // noop
        }
    }

    private ByteBuf ReadStreamHeader(EmbeddedQuicStreamChannel serverStream)
    {
        serverStream.FlushOutbound(); // flush the stream header
        ByteBuf streamHeader = serverStream.ReadOutbound<ByteBuf>();
        Assert.NotNull(streamHeader);
        return streamHeader;
    }

    // CLR: mock(ChannelHandlerContext.class) with its stubbing (see the class comment).
    private sealed class MockChannelHandlerContext : IChannelHandlerContext
    {
        private readonly IChannel channel;

        internal MockChannelHandlerContext(IChannel channel)
        {
            this.channel = channel;
        }

        public IChannel Channel => channel;

        public IEventExecutor Executor => null;

        public string Name => null;

        public IChannelHandler Handler => null;

        public bool IsRemoved => false;

        public IChannelPipeline Pipeline => null;

        public IByteBufAllocator Alloc()
        {
            return null;
        }

        public IAttribute<T> Attr<T>(AttributeKey<T> key) where T : class
        {
            return null;
        }

        public bool HasAttr<T>(AttributeKey<T> key) where T : class
        {
            return false;
        }

        public IChannelInboundInvoker FireChannelRegistered()
        {
            return null;
        }

        public IChannelInboundInvoker FireChannelUnregistered()
        {
            return null;
        }

        public IChannelInboundInvoker FireChannelActive()
        {
            return null;
        }

        public IChannelInboundInvoker FireChannelInactive()
        {
            return null;
        }

        public IChannelInboundInvoker FireExceptionCaught(Exception cause)
        {
            return null;
        }

        public IChannelInboundInvoker FireUserEventTriggered(object @event)
        {
            return null;
        }

        public IChannelInboundInvoker FireChannelRead(object msg)
        {
            return null;
        }

        public IChannelInboundInvoker FireChannelReadComplete()
        {
            return null;
        }

        public IChannelInboundInvoker FireChannelWritabilityChanged()
        {
            return null;
        }

        public Task BindAsync(EndPoint localAddress, CancellationToken cancellationToken = default)
        {
            return null;
        }

        public Task ConnectAsync(EndPoint remoteAddress, EndPoint localAddress = null,
                                 CancellationToken cancellationToken = default)
        {
            return null;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            return null;
        }

        public Task CloseAsync(CancellationToken cancellationToken = default)
        {
            return null;
        }

        public Task DeregisterAsync(CancellationToken cancellationToken = default)
        {
            return null;
        }

        public Task WriteAsync(object msg, CancellationToken cancellationToken = default)
        {
            return null;
        }

        public Task WriteAndFlushAsync(object msg, CancellationToken cancellationToken = default)
        {
            return null;
        }

        public IChannelOutboundInvoker Read()
        {
            return null;
        }

        public IChannelOutboundInvoker Flush()
        {
            return null;
        }
    }
}
