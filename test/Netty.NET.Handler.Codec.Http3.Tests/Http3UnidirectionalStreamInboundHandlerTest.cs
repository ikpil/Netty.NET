/*
 * Copyright 2020 The Netty Project
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
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: @AfterEach tearDown() is Dispose() (xUnit creates a test class instance per test case and disposes it after the
// case). The @ParameterizedTest methods with @ValueSource(booleans = {false, true}) are [Theory]s with
// [InlineData(false)] and [InlineData(true)]; the display-name patterns have no xUnit counterpart. The private helper
// testPushStream(boolean, long), which overloads a test name, is TestPushStream0 (xUnit1024;
// docs/codec-http-porting.md, Shared CLR adaptations). The handler types are internal (package-private in Java) and
// reached through InternalsVisibleTo; the static imports of Http3CodecUtils' stream types are qualified, and
// QpackSensitivityDetector is the interface IQpackSensitivityDetector. LongFunction<ChannelHandler> is Func<long,
// IChannelHandler>, the Supplier lambdas of the QPACK handlers are Func<IChannelHandler>, the Http3FrameCodecFactory
// lambda (v, __, ___, ____) -> new CodecHandler() is the private nested class CodecHandlerFactory, the
// NonStandardHttp3SettingsValidator lambda (id, v) -> false the private nested class NoNonStandardSettings, and the
// anonymous Http3PushStreamClientInitializer the private nested class CodecPushStreamClientInitializer. Class<? extends
// ChannelHandler> is Type and pipeline().context(Class) is Pipeline.Context(Type) (it matches subclasses, as
// Class.isAssignableFrom). parent.createStream(...).get() is the result of the completed Task of CreateStreamAsync
// (GetAwaiter().GetResult()). The conditional server ? new Http3ServerConnectionHandler(...) : new
// Http3ClientConnectionHandler() needs the IChannelHandler cast, as the two classes have no conversion to each other.
// refCnt() is the RefCnt property, isActive() IsActive, parent() Parent, readInbound()/readOutbound()
// ReadInbound<T>()/ReadOutbound(), and DefaultChannelId.newInstance() is DefaultChannelId.NewInstance().
// ChannelHandlerAdapter is abstract in both. The throws clauses and @Nullable (org.jetbrains.annotations) have no
// counterpart.
public class Http3UnidirectionalStreamInboundHandlerTest : IDisposable
{

    private EmbeddedQuicChannel parent;
    private Http3ControlStreamOutboundHandler remoteControlStreamHandler;
    private Http3ControlStreamInboundHandler localControlStreamHandler;
    private QpackEncoder qpackEncoder;
    private QpackDecoder qpackDecoder;

    private void Setup(bool server)
    {
        parent = new EmbeddedQuicChannel(server);
        qpackEncoder = new QpackEncoder(IQpackSensitivityDetector.NEVER_SENSITIVE);
        qpackDecoder = new QpackDecoder(0, 0);
        localControlStreamHandler = new Http3ControlStreamInboundHandler(server, null, qpackEncoder,
                remoteControlStreamHandler);
        remoteControlStreamHandler = new Http3ControlStreamOutboundHandler(server, new DefaultHttp3SettingsFrame(),
                new CodecHandler());
    }

    public void Dispose()
    {
        Assert.False(parent.Finish());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestUnkownStream(bool server)
    {
        Setup(server);
        EmbeddedChannel channel = NewChannel(server);
        ByteBuf buffer = Unpooled.Buffer(8);
        Http3CodecUtils.WriteVariableLengthInteger(buffer, 0x06);
        Assert.False(channel.WriteInbound(buffer));
        Assert.Equal(0, buffer.RefCnt);
        Assert.Null(channel.Pipeline.Context(typeof(Http3UnidirectionalStreamInboundHandler)));
        Assert.True(channel.IsActive);

        // Write some buffer to the stream. This should be just released.
        ByteBuf someBuffer = Unpooled.Buffer();
        Assert.False(channel.WriteInbound(someBuffer));
        Assert.Equal(0, someBuffer.RefCnt);
        Assert.False(channel.Finish());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestUnknownStreamWithCustomHandler(bool server)
    {
        Setup(server);
        long streamType = 0x06;
        EmbeddedChannel channel = NewChannel(server, v =>
        {
            Assert.Equal(streamType, v);

            // Add an handler that will just forward the received bytes.
            return new ChannelInboundHandlerAdapter();
        });
        ByteBuf buffer = Unpooled.Buffer(8);
        Http3CodecUtils.WriteVariableLengthInteger(buffer, streamType);
        Assert.False(channel.WriteInbound(buffer));
        Assert.Equal(0, buffer.RefCnt);
        Assert.Null(channel.Pipeline.Context(typeof(Http3UnidirectionalStreamInboundHandler)));
        Assert.True(channel.IsActive);

        // Write some buffer to the stream. This should be just released.
        ByteBuf someBuffer = Unpooled.Buffer().WriteLong(9);
        Assert.True(channel.WriteInbound(someBuffer.RetainedDuplicate()));
        Assert.True(channel.Finish());

        Http3TestUtils.AssertBufferEquals(someBuffer, channel.ReadInbound<ByteBuf>());
        Assert.Null(channel.ReadInbound());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestPushStream(bool server)
    {
        Setup(server);
        EmbeddedChannel channel = NewChannel(server);
        ByteBuf buffer = Unpooled.Buffer(8);
        Http3CodecUtils.WriteVariableLengthInteger(buffer, Http3CodecUtils.HTTP3_PUSH_STREAM_TYPE);
        Http3CodecUtils.WriteVariableLengthInteger(buffer, 2);
        Assert.False(channel.WriteInbound(buffer));
        Assert.Equal(0, buffer.RefCnt);

        if (server)
        {
            Http3TestUtils.VerifyClose(Http3ErrorCode.H3_STREAM_CREATION_ERROR, (EmbeddedQuicChannel) channel.Parent);
        }
        else
        {
            ByteBuf b = Unpooled.Buffer();
            Assert.False(channel.WriteInbound(b));
            Assert.Equal(0, b.RefCnt);
        }

        Assert.False(channel.Finish());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestPushStreamNoMaxPushIdFrameSent(bool server)
    {
        TestPushStream0(server, -1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestPushStreamMaxPushIdFrameSentWithSmallerId(bool server)
    {
        TestPushStream0(server, 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestPushStreamMaxPushIdFrameSentWithSameId(bool server)
    {
        TestPushStream0(server, 2);
    }

    // CLR: testPushStream(boolean, long) (see the type comment).
    private void TestPushStream0(bool server, long maxPushId)
    {
        Setup(server);
        Assert.False(parent.Finish());
        parent = new EmbeddedQuicChannel(server, server ?
                (IChannelHandler) new Http3ServerConnectionHandler(new ChannelInboundHandlerAdapter()) :
                new Http3ClientConnectionHandler());
        EmbeddedQuicStreamChannel localControlStream =
                (EmbeddedQuicStreamChannel) Http3.GetLocalControlStream(parent);
        Assert.NotNull(localControlStream);
        Assert.True(localControlStream.ReleaseOutbound());
        EmbeddedQuicStreamChannel outboundControlChannel =
                (EmbeddedQuicStreamChannel) parent.CreateStreamAsync(QuicStreamType.BIDIRECTIONAL,
                        remoteControlStreamHandler).GetAwaiter().GetResult();

        // Let's drain everything that was written while channelActive(...) was called.
        for (;;)
        {
            object written = outboundControlChannel.ReadOutbound();
            if (written == null)
            {
                break;
            }

            ReferenceCountUtil.Release(written);
        }

        if (maxPushId >= 0)
        {
            Assert.True(outboundControlChannel.WriteOutbound(new DefaultHttp3MaxPushIdFrame(maxPushId)));
            object push = outboundControlChannel.ReadOutbound();
            ReferenceCountUtil.Release(push);
        }

        Http3UnidirectionalStreamInboundHandler handler = NewUniStreamInboundHandler(server, null);
        EmbeddedQuicStreamChannel channel =
                (EmbeddedQuicStreamChannel) parent.CreateStreamAsync(QuicStreamType.UNIDIRECTIONAL, handler)
                        .GetAwaiter().GetResult();

        ByteBuf buffer = Unpooled.Buffer(8);
        Http3CodecUtils.WriteVariableLengthInteger(buffer, Http3CodecUtils.HTTP3_PUSH_STREAM_TYPE);
        int pushId = 2;
        Http3CodecUtils.WriteVariableLengthInteger(buffer, pushId);

        Assert.False(channel.WriteInbound(buffer));
        Assert.Equal(0, buffer.RefCnt);

        if (server)
        {
            Http3TestUtils.VerifyClose(Http3ErrorCode.H3_STREAM_CREATION_ERROR, parent);
        }
        else
        {
            if (pushId > maxPushId)
            {
                Http3TestUtils.VerifyClose(Http3ErrorCode.H3_ID_ERROR, parent);
            }
            else
            {
                Assert.True(parent.IsActive);
                Assert.NotNull(channel.Pipeline.Context(typeof(CodecHandler)));
            }
        }

        Assert.False(channel.Finish());
        Assert.False(outboundControlChannel.Finish());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestControlStream(bool server)
    {
        TestStreamSetup(server, Http3CodecUtils.HTTP3_CONTROL_STREAM_TYPE, typeof(Http3ControlStreamInboundHandler),
                true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestQpackEncoderStream(bool server)
    {
        TestStreamSetup(server, Http3CodecUtils.HTTP3_QPACK_ENCODER_STREAM_TYPE, typeof(QpackEncoderHandler), false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestQpackDecoderStream(bool server)
    {
        TestStreamSetup(server, Http3CodecUtils.HTTP3_QPACK_DECODER_STREAM_TYPE, typeof(QpackDecoderHandler), false);
    }

    private void TestStreamSetup(bool server, long type,
                                 Type clazz, bool hasCodec)
    {
        Setup(server);
        EmbeddedChannel channel = NewChannel(server);
        ByteBuf buffer = Unpooled.Buffer(8);
        Http3CodecUtils.WriteVariableLengthInteger(buffer, type);
        Assert.False(channel.WriteInbound(buffer));
        Assert.Equal(0, buffer.RefCnt);
        Assert.Null(channel.Pipeline.Context(typeof(Http3UnidirectionalStreamInboundHandler)));
        Assert.NotNull(channel.Pipeline.Context(clazz));

        if (hasCodec)
        {
            Assert.NotNull(channel.Pipeline.Context(typeof(CodecHandler)));
        }
        else
        {
            Assert.Null(channel.Pipeline.Context(typeof(CodecHandler)));
        }

        Assert.False(channel.Finish());

        channel = new EmbeddedChannel(channel.Parent, DefaultChannelId.NewInstance(),
                true, false, NewUniStreamInboundHandler(server, null));

        // Try to create the stream a second time, this should fail
        buffer = Unpooled.Buffer(8);
        Http3CodecUtils.WriteVariableLengthInteger(buffer, type);
        Assert.False(channel.WriteInbound(buffer));
        Assert.Equal(0, buffer.RefCnt);
        Http3TestUtils.VerifyClose(Http3ErrorCode.H3_STREAM_CREATION_ERROR, (EmbeddedQuicChannel) channel.Parent);
        Assert.False(channel.Finish());
    }

    private EmbeddedChannel NewChannel(bool server)
    {
        return NewChannel(server, null);
    }

    private EmbeddedChannel NewChannel(bool server,
                                       Func<long, IChannelHandler> unknownStreamHandlerFactory)
    {
        Http3UnidirectionalStreamInboundHandler handler =
                NewUniStreamInboundHandler(server, unknownStreamHandlerFactory);
        return (EmbeddedQuicStreamChannel) parent.CreateStreamAsync(QuicStreamType.BIDIRECTIONAL, handler)
                .GetAwaiter().GetResult();
    }

    private Http3UnidirectionalStreamInboundHandler NewUniStreamInboundHandler(bool server,
            Func<long, IChannelHandler> unknownStreamHandlerFactory)
    {
        return server ?
                new Http3UnidirectionalStreamInboundServerHandler(new CodecHandlerFactory(),
                        new NoNonStandardSettings(),
                        localControlStreamHandler, remoteControlStreamHandler, unknownStreamHandlerFactory,
                        () => new QpackEncoderHandler((long) int.MaxValue, qpackDecoder),
                        () => new QpackDecoderHandler(qpackEncoder)) :
                new Http3UnidirectionalStreamInboundClientHandler(new CodecHandlerFactory(),
                        new NoNonStandardSettings(),
                        localControlStreamHandler, remoteControlStreamHandler,
                        unknownStreamHandlerFactory,
                        pushId => new CodecPushStreamClientInitializer(),
                        () => new QpackEncoderHandler((long) int.MaxValue,
                        qpackDecoder), () => new QpackDecoderHandler(qpackEncoder));
    }

    // CLR: the Http3FrameCodecFactory lambda (v, __, ___, ____) -> new CodecHandler() of newUniStreamInboundHandler.
    private sealed class CodecHandlerFactory : Http3FrameCodec.IHttp3FrameCodecFactory
    {
        public IChannelHandler NewCodec(IHttp3FrameTypeValidator validator, IHttp3RequestStreamCodecState encodeState,
                                        IHttp3RequestStreamCodecState decodeState,
                                        Http3Settings.INonStandardHttp3SettingsValidator nonStandardSettingsValidator)
        {
            return new CodecHandler();
        }
    }

    // CLR: the NonStandardHttp3SettingsValidator lambda (id, v) -> false of newUniStreamInboundHandler.
    private sealed class NoNonStandardSettings : Http3Settings.INonStandardHttp3SettingsValidator
    {
        public bool Validate(long id, long value)
        {
            return false;
        }
    }

    // CLR: the anonymous Http3PushStreamClientInitializer of newUniStreamInboundHandler.
    private sealed class CodecPushStreamClientInitializer : Http3PushStreamClientInitializer
    {
        protected override void InitPushStream(IQuicStreamChannel ch)
        {
            ch.Pipeline.AddLast(new CodecHandler());
        }
    }

    private sealed class CodecHandler : ChannelHandlerAdapter {  }
}
