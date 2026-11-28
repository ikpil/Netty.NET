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
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Assert = XunitAssertMessages.AssertM;
using static Netty.NET.Handler.Codec.Http3.Tests.Http3TestUtils;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: the four theories of AbstractHttp3FrameTypeValidationHandlerTest run for this class too; it is neither
// inbound nor outbound (super(UNIDIRECTIONAL, false, false)), so they skip every case (assumeTrue is
// Assert.SkipUnless), as JUnit aborts them. The @ParameterizedTest methods with @MethodSource("testData") are
// [Theory]s with [MemberData(nameof(TestData))] over a public static TheoryData<bool, bool> built by the same
// loops (the four combinations in the same order); @ValueSource(booleans = { true, false }) is [InlineData(true)]
// and [InlineData(false)]; the display-name patterns have no xUnit counterpart. Http3ControlStreamInboundHandler,
// Http3ControlStreamOutboundHandler, QpackEncoder and QpackAttributes are internal (package-private in Java) and
// reached through InternalsVisibleTo; QpackSensitivityDetector is the interface IQpackSensitivityDetector. The
// lambda frame () -> 9999 of testInvalidFirstFrameNonControlFrame is the private nested class NonControlFrame.
// assertThrows(Exception.class,...) is Assert.ThrowsAny<Exception>; readInbound()/readOutbound() are
// ReadInbound<T>()/ReadOutbound(). Collection<Object[]> is TheoryData<bool, bool> and Arrays.asList a List
// initializer. The throws clauses have no counterpart.
public class Http3ControlStreamInboundHandlerTest :
        AbstractHttp3FrameTypeValidationHandlerTest<IHttp3ControlStreamFrame>
{

    private QpackEncoder qpackEncoder;
    private Http3ControlStreamOutboundHandler remoteControlStreamHandler;

    public Http3ControlStreamInboundHandlerTest() : base(QuicStreamType.UNIDIRECTIONAL, false, false)
    {
    }

    public static TheoryData<bool, bool> TestData()
    {
        TheoryData<bool, bool> config = new TheoryData<bool, bool>();

        for (int a = 0; a < 2; a++)
        {
            for (int b = 0; b < 2; b++)
            {
                config.Add(a == 0, b == 0);
            }
        }

        return config;
    }

    protected override void SetUp(bool server)
    {
        base.SetUp(server);
        qpackEncoder = new QpackEncoder(IQpackSensitivityDetector.NEVER_SENSITIVE);
        remoteControlStreamHandler = new Http3ControlStreamOutboundHandler(server, new DefaultHttp3SettingsFrame(),
                new ChannelInboundHandlerAdapter());
    }

    protected override void AfterSettingsFrameRead(IHttp3SettingsFrame settingsFrame)
    {
        if (!qpackAttributes.DynamicTableDisabled())
        {
            // settings frame initialize QPACK streams
            ReadAndReleaseStreamHeader(QPACKEncoderStream());
            ReadAndReleaseStreamHeader(QPACKDecoderStream());
        }
    }

    protected override IChannelHandler NewHandler(bool server)
    {
        return new Http3ControlStreamInboundHandler(server, new ChannelInboundHandlerAdapter(), qpackEncoder,
                remoteControlStreamHandler);
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
    [MemberData(nameof(TestData))]
    public void TestInvalidFirstFrameHttp3GoAwayFrame(bool server, bool forwardControlFrames)
    {
        SetUp(server);
        TestInvalidFirstFrame(server, forwardControlFrames, new DefaultHttp3GoAwayFrame(0));
    }

    [Theory]
    [MemberData(nameof(TestData))]
    public void TestInvalidFirstFrameHttp3MaxPushIdFrame(bool server, bool forwardControlFrames)
    {
        SetUp(server);
        TestInvalidFirstFrame(server, forwardControlFrames, new DefaultHttp3MaxPushIdFrame(0));
    }

    [Theory]
    [MemberData(nameof(TestData))]
    public void TestInvalidFirstFrameHttp3CancelPushFrame(bool server, bool forwardControlFrames)
    {
        SetUp(server);
        TestInvalidFirstFrame(server, forwardControlFrames, new DefaultHttp3CancelPushFrame(0));
    }

    [Theory]
    [MemberData(nameof(TestData))]
    public void TestInvalidFirstFrameNonControlFrame(bool server, bool forwardControlFrames)
    {
        SetUp(server);
        TestInvalidFirstFrame(server, forwardControlFrames, new NonControlFrame());
    }

    // CLR: the lambda () -> 9999 of testInvalidFirstFrameNonControlFrame.
    private sealed class NonControlFrame : IHttp3Frame
    {
        public long Type()
        {
            return 9999;
        }
    }

    private void TestInvalidFirstFrame(bool server, bool forwardControlFrames, IHttp3Frame frame)
    {
        EmbeddedQuicStreamChannel channel = NewStream(QuicStreamType.BIDIRECTIONAL,
                        new Http3ControlStreamInboundHandler(server,
                                forwardControlFrames ? new ChannelInboundHandlerAdapter() : null,
                                qpackEncoder, remoteControlStreamHandler));

        WriteInvalidFrame(forwardControlFrames, Http3ErrorCode.H3_MISSING_SETTINGS, channel, frame);
        VerifyClose(Http3ErrorCode.H3_MISSING_SETTINGS, parent);

        Assert.False(channel.Finish());
    }

    [Theory]
    [MemberData(nameof(TestData))]
    public void TestValidGoAwayFrame(bool server, bool forwardControlFrames)
    {
        SetUp(server);
        EmbeddedChannel channel = NewStream(server, forwardControlFrames);
        WriteValidFrame(forwardControlFrames, channel, new DefaultHttp3GoAwayFrame(0));
        WriteValidFrame(forwardControlFrames, channel, new DefaultHttp3GoAwayFrame(0));
        Assert.False(channel.Finish());
    }

    [Theory]
    [MemberData(nameof(TestData))]
    public void TestSecondGoAwayFrameFailsWithHigherId(bool server, bool forwardControlFrames)
    {
        SetUp(server);
        EmbeddedChannel channel = NewStream(server, forwardControlFrames);
        WriteValidFrame(forwardControlFrames, channel, new DefaultHttp3GoAwayFrame(0));
        WriteInvalidFrame(forwardControlFrames, Http3ErrorCode.H3_ID_ERROR, channel, new DefaultHttp3GoAwayFrame(4));
        VerifyClose(Http3ErrorCode.H3_ID_ERROR, parent);
        Assert.False(channel.Finish());
    }

    [Theory]
    [MemberData(nameof(TestData))]
    public void TestGoAwayFrameIdNonRequestStream(bool server, bool forwardControlFrames)
    {
        SetUp(server);
        EmbeddedChannel channel = NewStream(server, forwardControlFrames);

        if (server)
        {
            WriteValidFrame(forwardControlFrames, channel, new DefaultHttp3GoAwayFrame(3));
        }
        else
        {
            WriteInvalidFrame(forwardControlFrames, Http3ErrorCode.H3_FRAME_UNEXPECTED, channel,
                    new DefaultHttp3GoAwayFrame(3));
            VerifyClose(Http3ErrorCode.H3_FRAME_UNEXPECTED, parent);
        }

        Assert.False(channel.Finish());
    }

    [Theory]
    [MemberData(nameof(TestData))]
    public void TestHttp3MaxPushIdFrames(bool server, bool forwardControlFrames)
    {
        SetUp(server);
        EmbeddedChannel channel = NewStream(server, forwardControlFrames);

        if (server)
        {
            WriteValidFrame(forwardControlFrames, channel, new DefaultHttp3MaxPushIdFrame(0));
            WriteValidFrame(forwardControlFrames, channel, new DefaultHttp3MaxPushIdFrame(4));
        }
        else
        {
            WriteInvalidFrame(forwardControlFrames, Http3ErrorCode.H3_FRAME_UNEXPECTED, channel,
                    new DefaultHttp3MaxPushIdFrame(4));
            VerifyClose(Http3ErrorCode.H3_FRAME_UNEXPECTED, parent);
        }

        Assert.False(channel.Finish());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestSecondHttp3MaxPushIdFrameFailsWithSmallerId(bool forwardControlFrames)
    {
        SetUp(true);
        EmbeddedChannel channel = NewStream(true, forwardControlFrames);
        WriteValidFrame(forwardControlFrames, channel, new DefaultHttp3MaxPushIdFrame(4));
        WriteInvalidFrame(forwardControlFrames, Http3ErrorCode.H3_ID_ERROR, channel, new DefaultHttp3MaxPushIdFrame(0));
        VerifyClose(Http3ErrorCode.H3_ID_ERROR, parent);
        Assert.False(channel.Finish());
    }

    private EmbeddedQuicStreamChannel NewStream(bool server, bool forwardControlFrames)
    {
        EmbeddedQuicStreamChannel channel = NewStream(QuicStreamType.UNIDIRECTIONAL,
                        new Http3ControlStreamInboundHandler(server,
                                forwardControlFrames ? new ChannelInboundHandlerAdapter() : null,
                                qpackEncoder, remoteControlStreamHandler));

        // We always need to start with a settings frame.
        IHttp3SettingsFrame settingsFrame = new DefaultHttp3SettingsFrame();
        Assert.Equal(forwardControlFrames, channel.WriteInbound(settingsFrame));

        if (forwardControlFrames)
        {
            AssertFrameSame(settingsFrame, channel.ReadInbound<IHttp3Frame>());
        }
        else
        {
            AssertFrameReleased(settingsFrame);
        }

        object streamType = QPACKEncoderStream().ReadOutbound();
        Assert.NotNull(streamType);
        ReferenceCountUtil.Release(streamType);

        streamType = QPACKDecoderStream().ReadOutbound();
        Assert.NotNull(streamType);
        ReferenceCountUtil.Release(streamType);
        return channel;
    }

    private void WriteValidFrame(bool forwardControlFrames, EmbeddedChannel channel,
                                 IHttp3ControlStreamFrame controlStreamFrame)
    {
        Assert.Equal(forwardControlFrames, channel.WriteInbound(controlStreamFrame));

        if (forwardControlFrames)
        {
            AssertFrameSame(controlStreamFrame, channel.ReadInbound<IHttp3Frame>());
        }
        else
        {
            AssertFrameReleased(controlStreamFrame);
        }
    }

    private void WriteInvalidFrame(bool forwardControlFrames, Http3ErrorCode expectedCode, EmbeddedChannel channel,
                                   IHttp3Frame frame)
    {
        if (forwardControlFrames)
        {
            Exception e = Assert.ThrowsAny<Exception>(() => channel.WriteInbound(frame));
            AssertException(expectedCode, e);
        }
        else
        {
            Assert.False(channel.WriteInbound(frame));
        }

        AssertFrameReleased(frame);
    }

    [Theory]
    [MemberData(nameof(TestData))]
    public void TestSecondSettingsFrameFails(bool server, bool forwardControlFrames)
    {
        SetUp(server);
        EmbeddedChannel channel = NewStream(server, forwardControlFrames);
        WriteInvalidFrame(forwardControlFrames, Http3ErrorCode.H3_FRAME_UNEXPECTED, channel,
                new DefaultHttp3SettingsFrame());
        VerifyClose(Http3ErrorCode.H3_FRAME_UNEXPECTED, parent);
        Assert.False(channel.Finish());
    }

    [Theory]
    [MemberData(nameof(TestData))]
    public void TestControlStreamClosed(bool server, bool forwardControlFrames)
    {
        SetUp(server);
        EmbeddedQuicStreamChannel channel = NewStream(server, forwardControlFrames);
        channel.WriteInboundFin();
        VerifyClose(Http3ErrorCode.H3_CLOSED_CRITICAL_STREAM, parent);
        Assert.False(channel.Finish());
    }

    protected override Http3ErrorCode InboundErrorCodeInvalid()
    {
        return Http3ErrorCode.H3_MISSING_SETTINGS;
    }
}
