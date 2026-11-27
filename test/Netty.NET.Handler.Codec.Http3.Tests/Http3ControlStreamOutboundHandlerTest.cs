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
using System.Collections.Generic;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Assert = XunitAssertMessages.AssertM;
using static Netty.NET.Handler.Codec.Http3.Tests.Http3TestUtils;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: the four theories of AbstractHttp3FrameTypeValidationHandlerTest run for this class too, with both data rows;
// the two inbound ones skip their cases (assumeTrue(isInbound) is Assert.SkipUnless), as JUnit aborts them. The
// @ParameterizedTest methods with @MethodSource("data") are [Theory]s with [MemberData(nameof(Data))] over the base
// class's data(); the display-name patterns have no xUnit counterpart. newHandler(boolean) narrows its return type to
// Http3ControlStreamOutboundHandler in Java; that class is internal, and a protected member of a public class may not
// expose it (CS0050), so the override returns IChannelHandler and testMaxPushIdCanBeIncreased casts the result. The
// newStream(QuicStreamType, ChannelHandler) override is private protected, as the base member, and super.newStream is
// base.NewStream. parent.close().get() is an await of CloseAsync with TestContext.Current.CancellationToken, so the
// tests that call it are async Task. readOutbound() is ReadOutbound<T>()/ReadOutbound(); getByte(int) returns the
// unsigned CLR byte for the byte overload of NumBytesForVariableLengthInteger (only bits 6 and 7 are read);
// assertEquals(long, long) of the stream type compares longs; assertEquals(settingsFrame, settings) calls Equals
// (object.Equals), as Java's equals does, because xUnit would compare the enumerable settings frames element by
// element. sentMaxPushId() returns long? (Long), so assertEquals(Long.valueOf(8), ...) compares with (long?) 8.
// assertThrows(Exception.class, ...) is Assert.ThrowsAny<Exception>. Arrays.asList is a List initializer and
// Unpooled.buffer().writeLong(8) is Unpooled.Buffer().WriteLong(8). The throws clauses have no counterpart.
public class Http3ControlStreamOutboundHandlerTest :
        AbstractHttp3FrameTypeValidationHandlerTest<IHttp3ControlStreamFrame>
{
    private readonly IHttp3SettingsFrame settingsFrame = new DefaultHttp3SettingsFrame();

    public Http3ControlStreamOutboundHandlerTest() : base(QuicStreamType.UNIDIRECTIONAL, false, true)
    {
    }

    protected override IChannelHandler NewHandler(bool server)
    {
        return new Http3ControlStreamOutboundHandler(server, settingsFrame, new ChannelInboundHandlerAdapter());
    }

    protected override List<IHttp3ControlStreamFrame> NewValidFrames()
    {
        return new List<IHttp3ControlStreamFrame>
        {
            new DefaultHttp3SettingsFrame(), new DefaultHttp3GoAwayFrame(0),
            new DefaultHttp3MaxPushIdFrame(0), new DefaultHttp3CancelPushFrame(0)
        };
    }

    protected override List<IHttp3Frame> NewInvalidFrames()
    {
        return new List<IHttp3Frame>
        {
            Http3TestUtils.NewHttp3RequestStreamFrame(), Http3TestUtils.NewHttp3PushStreamFrame()
        };
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestStreamClosedWhileParentStillActive(bool server)
    {
        SetUp(server);
        EmbeddedChannel channel = NewStream(NewHandler(server));
        Assert.False(channel.Finish());
        VerifyClose(1, Http3ErrorCode.H3_CLOSED_CRITICAL_STREAM, parent);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public async Task TestGoAwayIdDecreaseWorks(bool server)
    {
        SetUp(server);
        await parent.CloseAsync(TestContext.Current.CancellationToken);
        // Let's mark the parent as inactive before we close as otherwise we will send a close frame.
        EmbeddedChannel channel = NewStream(NewHandler(server));

        if (server)
        {
            WriteValidFrame(channel, new DefaultHttp3GoAwayFrame(8));
            WriteValidFrame(channel, new DefaultHttp3GoAwayFrame(4));
        }
        else
        {
            WriteValidFrame(channel, new DefaultHttp3GoAwayFrame(9));
            WriteValidFrame(channel, new DefaultHttp3GoAwayFrame(5));
        }

        Assert.False(channel.Finish());
    }

    [Theory]
    [MemberData(nameof(Data))]
    public async Task TestGoAwayIdIncreaseFails(bool server)
    {
        SetUp(server);
        // Let's mark the parent as inactive before we close as otherwise we will send a close frame.
        await parent.CloseAsync(TestContext.Current.CancellationToken);
        EmbeddedChannel channel = NewStream(NewHandler(server));

        if (server)
        {
            WriteValidFrame(channel, new DefaultHttp3GoAwayFrame(4));
            WriteInvalidFrame(Http3ErrorCode.H3_ID_ERROR, channel, new DefaultHttp3GoAwayFrame(8));
        }
        else
        {
            WriteValidFrame(channel, new DefaultHttp3GoAwayFrame(1));
            WriteInvalidFrame(Http3ErrorCode.H3_ID_ERROR, channel, new DefaultHttp3GoAwayFrame(3));
        }

        Assert.False(channel.Finish());
    }

    [Fact]
    public async Task TestGoAwayIdUseInvalidId()
    {
        SetUp(true);
        await parent.CloseAsync(TestContext.Current.CancellationToken);
        // Let's mark the parent as inactive before we close as otherwise we will send a close frame.
        EmbeddedChannel channel = NewStream(NewHandler(true));

        WriteInvalidFrame(Http3ErrorCode.H3_ID_ERROR, channel, new DefaultHttp3GoAwayFrame(2));

        Assert.False(channel.Finish());
    }

    [Theory]
    [MemberData(nameof(Data))]
    public async Task TestPassesUnknownFrame(bool server)
    {
        SetUp(server);
        await parent.CloseAsync(TestContext.Current.CancellationToken);
        // Let's mark the parent as inactive before we close as otherwise we will send a close frame.
        EmbeddedChannel channel = NewStream(NewHandler(server));

        WriteValidFrame(channel, new DefaultHttp3UnknownFrame(Http3CodecUtils.MIN_RESERVED_FRAME_TYPE,
                Unpooled.Buffer().WriteLong(8)));

        Assert.False(channel.Finish());
    }

    [Theory]
    [MemberData(nameof(Data))]
    public async Task TestMaxPushIdFailsWhenReduced(bool server)
    {
        SetUp(server);
        await parent.CloseAsync(TestContext.Current.CancellationToken);
        // Let's mark the parent as inactive before we close as otherwise we will send a close frame.
        EmbeddedChannel channel = NewStream(NewHandler(server));

        WriteValidFrame(channel, new DefaultHttp3MaxPushIdFrame(8));
        WriteInvalidFrame(Http3ErrorCode.H3_ID_ERROR, channel, new DefaultHttp3MaxPushIdFrame(4));

        Assert.False(channel.Finish());
    }

    [Theory]
    [MemberData(nameof(Data))]
    public async Task TestMaxPushIdCanBeIncreased(bool server)
    {
        SetUp(server);
        await parent.CloseAsync(TestContext.Current.CancellationToken);
        // Let's mark the parent as inactive before we close as otherwise we will send a close frame.
        Http3ControlStreamOutboundHandler handler = (Http3ControlStreamOutboundHandler) NewHandler(server);
        EmbeddedChannel channel = NewStream(handler);

        WriteValidFrame(channel, new DefaultHttp3MaxPushIdFrame(4));
        WriteValidFrame(channel, new DefaultHttp3MaxPushIdFrame(8));
        Assert.Equal((long?) 8, handler.SentMaxPushId());

        Assert.False(channel.Finish());
    }

    private protected override EmbeddedQuicStreamChannel NewStream(QuicStreamType streamType, IChannelHandler handler)
    {
        return NewStream(handler);
    }

    private EmbeddedQuicStreamChannel NewStream(IChannelHandler handler)
    {
        EmbeddedQuicStreamChannel channel = base.NewStream(QuicStreamType.UNIDIRECTIONAL, handler);
        ByteBuf buffer = channel.ReadOutbound<ByteBuf>();
        // Verify that we did write the control stream prefix
        int len = Http3CodecUtils.NumBytesForVariableLengthInteger(buffer.GetByte(0));
        Assert.Equal((long) Http3CodecUtils.HTTP3_CONTROL_STREAM_TYPE,
                Http3CodecUtils.ReadVariableLengthInteger(buffer, len));
        Assert.False(buffer.IsReadable());
        buffer.Release();

        IHttp3SettingsFrame settings = channel.ReadOutbound<IHttp3SettingsFrame>();
        Assert.Equal(settingsFrame, settings, (expected, actual) => object.Equals(expected, actual));

        Assert.Null(channel.ReadOutbound());
        return channel;
    }

    private void WriteInvalidFrame(Http3ErrorCode expectedCode,
                                   EmbeddedChannel channel,
                                   IHttp3Frame frame)
    {
        Exception e = Assert.ThrowsAny<Exception>(() => channel.WriteOutbound(frame));
        AssertException(expectedCode, e);
    }

    private void WriteValidFrame(EmbeddedChannel channel, IHttp3Frame frame)
    {
        try
        {
            Assert.True(channel.WriteOutbound(frame));
        }
        finally
        {
            ReferenceCountUtil.Release(channel.ReadOutbound());
        }
    }
}
