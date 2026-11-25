/*
 * Copyright 2013 The Netty Project
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
using System.Linq;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Spdy;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Http.Tests.HttpHeadersTestUtils;

namespace Netty.NET.Handler.Codec.Http.Tests.Spdy;

#pragma warning disable xUnit1051 // The source's writeAndFlush(...)/close() calls observe no test cancellation.

// CLR: the static initializer of closeMessage is the static constructor. assertInstanceOf is Assert.IsAssignableFrom
// (https://raw.githubusercontent.com/xunit/assert.xunit/main/TypeAsserts.cs) and assertThrows, which also accepts a
// subclass, is Assert.ThrowsAny (IllegalArgumentException is ArgumentException, docs/codec-base-porting.md).
// List.containsAll is Enumerable.All over List.Contains. The Strings passed as CharSequence header names and values are
// wrapped by the core test helper HttpHeadersTestUtils.Of (StringCharSequence). The private static nested EchoHandler
// is a private sealed ChannelInboundHandlerAdapter; ctx.writeAndFlush(msg)/ctx.close() are
// WriteAndFlushAsync/CloseAsync with the Task discarded, as the Java futures are. Map.Entry<CharSequence, CharSequence>
// is the codec-base IEntry<ICharSequence, ICharSequence>. The Java byte priority (byte) 0 is the CLR byte 0.
public class SpdySessionHandlerTest
{

    private static readonly IInternalLogger logger =
            InternalLoggerFactory.GetInstance(typeof(SpdySessionHandlerTest));

    private const int closeSignal = SpdyCodecUtil.SPDY_SETTINGS_MAX_ID;
    private static readonly ISpdySettingsFrame closeMessage = new DefaultSpdySettingsFrame();

    static SpdySessionHandlerTest()
    {
        closeMessage.SetValue(closeSignal, 0);
    }

    private static void AssertDataFrame(object msg, int streamId, bool last)
    {
        Assert.NotNull(msg);
        Assert.IsAssignableFrom<ISpdyDataFrame>(msg);
        ISpdyDataFrame spdyDataFrame = (ISpdyDataFrame) msg;
        Assert.Equal(streamId, spdyDataFrame.StreamId());
        Assert.Equal(last, spdyDataFrame.IsLast());
    }

    private static void AssertSynReply(object msg, int streamId, bool last, ISpdyHeaders headers)
    {
        Assert.NotNull(msg);
        Assert.IsAssignableFrom<ISpdySynReplyFrame>(msg);
        AssertHeaders(msg, streamId, last, headers);
    }

    private static void AssertRstStream(object msg, int streamId, SpdyStreamStatus status)
    {
        Assert.NotNull(msg);
        Assert.IsAssignableFrom<ISpdyRstStreamFrame>(msg);
        ISpdyRstStreamFrame spdyRstStreamFrame = (ISpdyRstStreamFrame) msg;
        Assert.Equal(streamId, spdyRstStreamFrame.StreamId());
        Assert.Equal(status, spdyRstStreamFrame.Status());
    }

    private static void AssertPing(object msg, int id)
    {
        Assert.NotNull(msg);
        Assert.IsAssignableFrom<ISpdyPingFrame>(msg);
        ISpdyPingFrame spdyPingFrame = (ISpdyPingFrame) msg;
        Assert.Equal(id, spdyPingFrame.Id());
    }

    private static void AssertGoAway(object msg, int lastGoodStreamId)
    {
        Assert.NotNull(msg);
        Assert.IsAssignableFrom<ISpdyGoAwayFrame>(msg);
        ISpdyGoAwayFrame spdyGoAwayFrame = (ISpdyGoAwayFrame) msg;
        Assert.Equal(lastGoodStreamId, spdyGoAwayFrame.LastGoodStreamId());
    }

    private static void AssertMaxConcurrentStreamsSettings(object msg, int maxConcurrentStreams)
    {
        Assert.NotNull(msg);
        Assert.IsAssignableFrom<ISpdySettingsFrame>(msg);
        ISpdySettingsFrame spdySettingsFrame = (ISpdySettingsFrame) msg;
        Assert.Equal(maxConcurrentStreams,
                spdySettingsFrame.GetValue(ISpdySettingsFrame.SETTINGS_MAX_CONCURRENT_STREAMS));
    }

    private static void AssertHeaders(object msg, int streamId, bool last, ISpdyHeaders headers)
    {
        Assert.NotNull(msg);
        Assert.IsAssignableFrom<ISpdyHeadersFrame>(msg);
        ISpdyHeadersFrame spdyHeadersFrame = (ISpdyHeadersFrame) msg;
        Assert.Equal(streamId, spdyHeadersFrame.StreamId());
        Assert.Equal(last, spdyHeadersFrame.IsLast());
        foreach (ICharSequence name in headers.Names())
        {
            IList<ICharSequence> expectedValues = headers.GetAll(name);
            IList<ICharSequence> receivedValues = spdyHeadersFrame.Headers().GetAll(name);
            Assert.True(expectedValues.All(receivedValues.Contains));
            Assert.Equal(expectedValues.Count, receivedValues.Count);
            spdyHeadersFrame.Headers().Remove(name);
        }
        Assert.True(spdyHeadersFrame.Headers().IsEmpty());
    }

    private static void TestSpdySessionHandler(SpdyVersion version, bool server)
    {
        EmbeddedChannel sessionHandler = new EmbeddedChannel(
                new SpdySessionHandler(version, server), new EchoHandler(closeSignal, server));

        while (sessionHandler.ReadOutbound<object>() != null)
        {
            continue;
        }

        int localStreamId = server ? 1 : 2;
        int remoteStreamId = server ? 2 : 1;

        ISpdySynStreamFrame spdySynStreamFrame =
                new DefaultSpdySynStreamFrame(localStreamId, 0, (byte) 0);
        spdySynStreamFrame.Headers().Set(Of("compression"), Of("test"));

        ISpdyDataFrame spdyDataFrame = new DefaultSpdyDataFrame(localStreamId);
        spdyDataFrame.SetLast(true);

        // Check if session handler returns INVALID_STREAM if it receives
        // a data frame for a Stream-ID that is not open
        sessionHandler.WriteInbound(new DefaultSpdyDataFrame(localStreamId));
        AssertRstStream(sessionHandler.ReadOutbound<object>(), localStreamId, SpdyStreamStatus.INVALID_STREAM);
        Assert.Null(sessionHandler.ReadOutbound<object>());

        // Check if session handler returns PROTOCOL_ERROR if it receives
        // a data frame for a Stream-ID before receiving a SYN_REPLY frame
        sessionHandler.WriteInbound(new DefaultSpdyDataFrame(remoteStreamId));
        AssertRstStream(sessionHandler.ReadOutbound<object>(), remoteStreamId, SpdyStreamStatus.PROTOCOL_ERROR);
        Assert.Null(sessionHandler.ReadOutbound<object>());
        remoteStreamId += 2;

        // Check if session handler returns PROTOCOL_ERROR if it receives
        // multiple SYN_REPLY frames for the same active Stream-ID
        sessionHandler.WriteInbound(new DefaultSpdySynReplyFrame(remoteStreamId));
        Assert.Null(sessionHandler.ReadOutbound<object>());
        sessionHandler.WriteInbound(new DefaultSpdySynReplyFrame(remoteStreamId));
        AssertRstStream(sessionHandler.ReadOutbound<object>(), remoteStreamId, SpdyStreamStatus.STREAM_IN_USE);
        Assert.Null(sessionHandler.ReadOutbound<object>());
        remoteStreamId += 2;

        // Check if frame codec correctly compresses/uncompresses headers
        sessionHandler.WriteInbound(spdySynStreamFrame);
        AssertSynReply(sessionHandler.ReadOutbound<object>(), localStreamId, false, spdySynStreamFrame.Headers());
        Assert.Null(sessionHandler.ReadOutbound<object>());
        ISpdyHeadersFrame spdyHeadersFrame = new DefaultSpdyHeadersFrame(localStreamId);

        spdyHeadersFrame.Headers().Add(Of("header"), Of("test1"));
        spdyHeadersFrame.Headers().Add(Of("header"), Of("test2"));

        sessionHandler.WriteInbound(spdyHeadersFrame);
        AssertHeaders(sessionHandler.ReadOutbound<object>(), localStreamId, false, spdyHeadersFrame.Headers());
        Assert.Null(sessionHandler.ReadOutbound<object>());
        localStreamId += 2;

        // Check if session handler closed the streams using the number
        // of concurrent streams and that it returns REFUSED_STREAM
        // if it receives a SYN_STREAM frame it does not wish to accept
        spdySynStreamFrame.SetStreamId(localStreamId);
        spdySynStreamFrame.SetLast(true);
        spdySynStreamFrame.SetUnidirectional(true);

        sessionHandler.WriteInbound(spdySynStreamFrame);
        AssertRstStream(sessionHandler.ReadOutbound<object>(), localStreamId, SpdyStreamStatus.REFUSED_STREAM);
        Assert.Null(sessionHandler.ReadOutbound<object>());

        // Check if session handler rejects HEADERS for closed streams
        int testStreamId = spdyDataFrame.StreamId();
        sessionHandler.WriteInbound(spdyDataFrame);
        AssertDataFrame(sessionHandler.ReadOutbound<object>(), testStreamId, spdyDataFrame.IsLast());
        Assert.Null(sessionHandler.ReadOutbound<object>());
        spdyHeadersFrame.SetStreamId(testStreamId);

        sessionHandler.WriteInbound(spdyHeadersFrame);
        AssertRstStream(sessionHandler.ReadOutbound<object>(), testStreamId, SpdyStreamStatus.INVALID_STREAM);
        Assert.Null(sessionHandler.ReadOutbound<object>());

        // Check if session handler drops active streams if it receives
        // a RST_STREAM frame for that Stream-ID
        sessionHandler.WriteInbound(new DefaultSpdyRstStreamFrame(remoteStreamId, 3));
        Assert.Null(sessionHandler.ReadOutbound<object>());
        //remoteStreamId += 2;

        // Check if session handler honors UNIDIRECTIONAL streams
        spdySynStreamFrame.SetLast(false);
        sessionHandler.WriteInbound(spdySynStreamFrame);
        Assert.Null(sessionHandler.ReadOutbound<object>());
        spdySynStreamFrame.SetUnidirectional(false);

        // Check if session handler returns PROTOCOL_ERROR if it receives
        // multiple SYN_STREAM frames for the same active Stream-ID
        sessionHandler.WriteInbound(spdySynStreamFrame);
        AssertRstStream(sessionHandler.ReadOutbound<object>(), localStreamId, SpdyStreamStatus.PROTOCOL_ERROR);
        Assert.Null(sessionHandler.ReadOutbound<object>());
        localStreamId += 2;

        // Check if session handler returns PROTOCOL_ERROR if it receives
        // a SYN_STREAM frame with an invalid Stream-ID
        spdySynStreamFrame.SetStreamId(localStreamId - 1);
        sessionHandler.WriteInbound(spdySynStreamFrame);
        AssertRstStream(sessionHandler.ReadOutbound<object>(), localStreamId - 1, SpdyStreamStatus.PROTOCOL_ERROR);
        Assert.Null(sessionHandler.ReadOutbound<object>());
        spdySynStreamFrame.SetStreamId(localStreamId);

        // Check if session handler returns PROTOCOL_ERROR if it receives
        // an invalid HEADERS frame
        spdyHeadersFrame.SetStreamId(localStreamId);

        spdyHeadersFrame.SetInvalid();
        sessionHandler.WriteInbound(spdyHeadersFrame);
        AssertRstStream(sessionHandler.ReadOutbound<object>(), localStreamId, SpdyStreamStatus.PROTOCOL_ERROR);
        Assert.Null(sessionHandler.ReadOutbound<object>());

        sessionHandler.Finish();
    }

    private static void TestSpdySessionHandlerPing(SpdyVersion version, bool server)
    {
        EmbeddedChannel sessionHandler = new EmbeddedChannel(
                new SpdySessionHandler(version, server), new EchoHandler(closeSignal, server));

        while (sessionHandler.ReadOutbound<object>() != null)
        {
            continue;
        }

        int localStreamId = server ? 1 : 2;
        int remoteStreamId = server ? 2 : 1;

        ISpdyPingFrame localPingFrame = new DefaultSpdyPingFrame(localStreamId);
        ISpdyPingFrame remotePingFrame = new DefaultSpdyPingFrame(remoteStreamId);

        // Check if session handler returns identical local PINGs
        sessionHandler.WriteInbound(localPingFrame);
        AssertPing(sessionHandler.ReadOutbound<object>(), localPingFrame.Id());
        Assert.Null(sessionHandler.ReadOutbound<object>());

        // Check if session handler ignores un-initiated remote PINGs
        sessionHandler.WriteInbound(remotePingFrame);
        Assert.Null(sessionHandler.ReadOutbound<object>());

        sessionHandler.Finish();
    }

    private static void TestSpdySessionHandlerGoAway(SpdyVersion version, bool server)
    {
        EmbeddedChannel sessionHandler = new EmbeddedChannel(
                new SpdySessionHandler(version, server), new EchoHandler(closeSignal, server));

        while (sessionHandler.ReadOutbound<object>() != null)
        {
            continue;
        }

        int localStreamId = server ? 1 : 2;

        ISpdySynStreamFrame spdySynStreamFrame =
                new DefaultSpdySynStreamFrame(localStreamId, 0, (byte) 0);
        spdySynStreamFrame.Headers().Set(Of("compression"), Of("test"));

        ISpdyDataFrame spdyDataFrame = new DefaultSpdyDataFrame(localStreamId);
        spdyDataFrame.SetLast(true);

        // Send an initial request
        sessionHandler.WriteInbound(spdySynStreamFrame);
        AssertSynReply(sessionHandler.ReadOutbound<object>(), localStreamId, false, spdySynStreamFrame.Headers());
        Assert.Null(sessionHandler.ReadOutbound<object>());
        sessionHandler.WriteInbound(spdyDataFrame);
        AssertDataFrame(sessionHandler.ReadOutbound<object>(), localStreamId, true);
        Assert.Null(sessionHandler.ReadOutbound<object>());

        // Check if session handler sends a GOAWAY frame when closing
        sessionHandler.WriteInbound(closeMessage);
        AssertGoAway(sessionHandler.ReadOutbound<object>(), localStreamId);
        Assert.Null(sessionHandler.ReadOutbound<object>());
        localStreamId += 2;

        // Check if session handler returns REFUSED_STREAM if it receives
        // SYN_STREAM frames after sending a GOAWAY frame
        spdySynStreamFrame.SetStreamId(localStreamId);
        sessionHandler.WriteInbound(spdySynStreamFrame);
        AssertRstStream(sessionHandler.ReadOutbound<object>(), localStreamId, SpdyStreamStatus.REFUSED_STREAM);
        Assert.Null(sessionHandler.ReadOutbound<object>());

        // Check if session handler ignores Data frames after sending
        // a GOAWAY frame
        spdyDataFrame.SetStreamId(localStreamId);
        sessionHandler.WriteInbound(spdyDataFrame);
        Assert.Null(sessionHandler.ReadOutbound<object>());

        sessionHandler.Finish();
    }

    // Verifies the fix for the remote-initiated stream admission DoS: a SpdySessionHandler
    // created with the default (no-arg concurrency) constructor must reject SYN_STREAM frames
    // once the safe default limit (100, matching the HTTP/2 default) is reached, instead of
    // accepting an unbounded number of remote-initiated streams.
    private static void TestSpdySessionHandlerDefaultMaxConcurrentStreams(SpdyVersion version, bool server)
    {
        EmbeddedChannel sessionHandler = new EmbeddedChannel(new SpdySessionHandler(version, server));

        AssertMaxConcurrentStreamsSettings(sessionHandler.ReadOutbound<object>(), 100);
        Assert.Null(sessionHandler.ReadOutbound<object>());

        int remoteStreamId = server ? 1 : 2;
        for (int i = 0; i < 100; i++)
        {
            ISpdySynStreamFrame spdySynStreamFrame =
                    new DefaultSpdySynStreamFrame(remoteStreamId, 0, (byte) 0);
            spdySynStreamFrame.SetUnidirectional(true);
            sessionHandler.WriteInbound(spdySynStreamFrame);
            Assert.Null(sessionHandler.ReadOutbound<object>());
            remoteStreamId += 2;
        }

        // The 101st remote-initiated stream must be refused rather than accepted unconditionally.
        ISpdySynStreamFrame refusedFrame =
                new DefaultSpdySynStreamFrame(remoteStreamId, 0, (byte) 0);
        refusedFrame.SetUnidirectional(true);
        sessionHandler.WriteInbound(refusedFrame);
        AssertRstStream(sessionHandler.ReadOutbound<object>(), remoteStreamId, SpdyStreamStatus.REFUSED_STREAM);
        Assert.Null(sessionHandler.ReadOutbound<object>());

        sessionHandler.Finish();
    }

    // Verifies that the maxLocalConcurrentStreams constructor parameter is honored: streams
    // beyond the configured limit are refused with RST_STREAM(REFUSED_STREAM), and streams
    // within the limit are accepted normally.
    private static void TestSpdySessionHandlerCustomMaxConcurrentStreams(SpdyVersion version, bool server)
    {
        int maxConcurrentStreams = 4;
        EmbeddedChannel sessionHandler =
                new EmbeddedChannel(new SpdySessionHandler(version, server, maxConcurrentStreams));

        AssertMaxConcurrentStreamsSettings(sessionHandler.ReadOutbound<object>(), maxConcurrentStreams);
        Assert.Null(sessionHandler.ReadOutbound<object>());

        int remoteStreamId = server ? 1 : 2;
        for (int i = 0; i < maxConcurrentStreams; i++)
        {
            ISpdySynStreamFrame spdySynStreamFrame =
                    new DefaultSpdySynStreamFrame(remoteStreamId, 0, (byte) 0);
            spdySynStreamFrame.SetUnidirectional(true);
            Assert.True(sessionHandler.WriteInbound(spdySynStreamFrame));
            Assert.Null(sessionHandler.ReadOutbound<object>());
            remoteStreamId += 2;
        }

        ISpdySynStreamFrame refusedFrame =
                new DefaultSpdySynStreamFrame(remoteStreamId, 0, (byte) 0);
        refusedFrame.SetUnidirectional(true);
        Assert.True(sessionHandler.WriteInbound(refusedFrame));
        AssertRstStream(sessionHandler.ReadOutbound<object>(), remoteStreamId, SpdyStreamStatus.REFUSED_STREAM);
        Assert.Null(sessionHandler.ReadOutbound<object>());

        Assert.True(sessionHandler.FinishAndReleaseAll());
    }

    [Fact]
    public void TestSpdySessionHandlerRejectsNonPositiveMaxConcurrentStreams()
    {
        Assert.ThrowsAny<ArgumentException>(
                () => new SpdySessionHandler(SpdyVersion.SPDY_3_1, true, 0));
        Assert.ThrowsAny<ArgumentException>(
                () => new SpdySessionHandler(SpdyVersion.SPDY_3_1, true, -1));
    }

    [Fact]
    public void TestSpdyClientSessionHandlerDefaultMaxConcurrentStreams()
    {
        TestSpdySessionHandlerDefaultMaxConcurrentStreams(SpdyVersion.SPDY_3_1, false);
    }

    [Fact]
    public void TestSpdyServerSessionHandlerDefaultMaxConcurrentStreams()
    {
        TestSpdySessionHandlerDefaultMaxConcurrentStreams(SpdyVersion.SPDY_3_1, true);
    }

    [Fact]
    public void TestSpdyClientSessionHandlerCustomMaxConcurrentStreams()
    {
        TestSpdySessionHandlerCustomMaxConcurrentStreams(SpdyVersion.SPDY_3_1, false);
    }

    [Fact]
    public void TestSpdyServerSessionHandlerCustomMaxConcurrentStreams()
    {
        TestSpdySessionHandlerCustomMaxConcurrentStreams(SpdyVersion.SPDY_3_1, true);
    }

    [Fact]
    public void TestSpdyClientSessionHandler()
    {
        logger.Info("Running: testSpdyClientSessionHandler v3.1");
        TestSpdySessionHandler(SpdyVersion.SPDY_3_1, false);
    }

    [Fact]
    public void TestSpdyClientSessionHandlerPing()
    {
        logger.Info("Running: testSpdyClientSessionHandlerPing v3.1");
        TestSpdySessionHandlerPing(SpdyVersion.SPDY_3_1, false);
    }

    [Fact]
    public void TestSpdyClientSessionHandlerGoAway()
    {
        logger.Info("Running: testSpdyClientSessionHandlerGoAway v3.1");
        TestSpdySessionHandlerGoAway(SpdyVersion.SPDY_3_1, false);
    }

    [Fact]
    public void TestSpdyServerSessionHandler()
    {
        logger.Info("Running: testSpdyServerSessionHandler v3.1");
        TestSpdySessionHandler(SpdyVersion.SPDY_3_1, true);
    }

    [Fact]
    public void TestSpdyServerSessionHandlerPing()
    {
        logger.Info("Running: testSpdyServerSessionHandlerPing v3.1");
        TestSpdySessionHandlerPing(SpdyVersion.SPDY_3_1, true);
    }

    [Fact]
    public void TestSpdyServerSessionHandlerGoAway()
    {
        logger.Info("Running: testSpdyServerSessionHandlerGoAway v3.1");
        TestSpdySessionHandlerGoAway(SpdyVersion.SPDY_3_1, true);
    }

    // Echo Handler opens 4 half-closed streams on session connection
    // and then sets the number of concurrent streams to 1
    private sealed class EchoHandler : ChannelInboundHandlerAdapter
    {
        private readonly int closeSignal;
        private readonly bool server;

        internal EchoHandler(int closeSignal, bool server)
        {
            this.closeSignal = closeSignal;
            this.server = server;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            // Initiate 4 new streams
            int streamId = server ? 2 : 1;
            ISpdySynStreamFrame spdySynStreamFrame =
                    new DefaultSpdySynStreamFrame(streamId, 0, (byte) 0);
            spdySynStreamFrame.SetLast(true);
            _ = ctx.WriteAndFlushAsync(spdySynStreamFrame);
            spdySynStreamFrame.SetStreamId(spdySynStreamFrame.StreamId() + 2);
            _ = ctx.WriteAndFlushAsync(spdySynStreamFrame);
            spdySynStreamFrame.SetStreamId(spdySynStreamFrame.StreamId() + 2);
            _ = ctx.WriteAndFlushAsync(spdySynStreamFrame);
            spdySynStreamFrame.SetStreamId(spdySynStreamFrame.StreamId() + 2);
            _ = ctx.WriteAndFlushAsync(spdySynStreamFrame);

            // Limit the number of concurrent streams to 1
            ISpdySettingsFrame spdySettingsFrame = new DefaultSpdySettingsFrame();
            spdySettingsFrame.SetValue(ISpdySettingsFrame.SETTINGS_MAX_CONCURRENT_STREAMS, 1);
            _ = ctx.WriteAndFlushAsync(spdySettingsFrame);
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is ISpdySynStreamFrame)
            {

                ISpdySynStreamFrame spdySynStreamFrame = (ISpdySynStreamFrame) msg;
                if (!spdySynStreamFrame.IsUnidirectional())
                {
                    int streamId = spdySynStreamFrame.StreamId();
                    ISpdySynReplyFrame spdySynReplyFrame = new DefaultSpdySynReplyFrame(streamId);
                    spdySynReplyFrame.SetLast(spdySynStreamFrame.IsLast());
                    foreach (IEntry<ICharSequence, ICharSequence> entry in spdySynStreamFrame.Headers())
                    {
                        spdySynReplyFrame.Headers().Add(entry.GetKey(), entry.GetValue());
                    }

                    _ = ctx.WriteAndFlushAsync(spdySynReplyFrame);
                }
                return;
            }

            if (msg is ISpdySynReplyFrame)
            {
                return;
            }

            if (msg is ISpdyDataFrame ||
                msg is ISpdyPingFrame ||
                msg is ISpdyHeadersFrame)
            {

                _ = ctx.WriteAndFlushAsync(msg);
                return;
            }

            if (msg is ISpdySettingsFrame)
            {
                ISpdySettingsFrame spdySettingsFrame = (ISpdySettingsFrame) msg;
                if (spdySettingsFrame.IsSet(closeSignal))
                {
                    _ = ctx.CloseAsync();
                }
            }
        }
    }
}
