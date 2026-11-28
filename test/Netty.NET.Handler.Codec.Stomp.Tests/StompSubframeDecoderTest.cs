/*
 * Copyright 2014 The Netty Project
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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Handler.Codec.Stomp.Tests.StompTestConstants;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Stomp.Tests;

// CLR: xUnit creates a new test class instance for every test, so the @BeforeEach setup() runs from the
// constructor and the @AfterEach teardown() from Dispose() (https://xunit.net/docs/shared-context).
// String.getBytes() encodes with the default charset, Encoding.Default, and UTF_8 is Encoding.UTF8;
// assertInstanceOf is Assert.IsAssignableFrom
// (https://raw.githubusercontent.com/xunit/assert.xunit/main/TypeAsserts.cs), and the message assertion uses
// XunitAssertMessages.AssertM. Java passes Strings as CharSequence header names and compares String header values;
// C# string is not an ICharSequence, so Of(...) wraps them in the Common StringCharSequence, whose Equals matches
// String.equals. @ParameterizedTest @ValueSource(ints) is a [Theory] with one [InlineData] per value.
#pragma warning disable xUnit2013 // assertEquals(n, size()) keeps the source assertions.
public class StompSubframeDecoderTest : IDisposable
{

    private EmbeddedChannel channel;

    public StompSubframeDecoderTest()
    {
        Setup();
    }

    public void Dispose()
    {
        Teardown();
    }

    private void Setup()
    {
        channel = new EmbeddedChannel(new StompSubframeDecoder());
    }

    private void Teardown()
    {
        Assert.False(channel.Finish());
    }

    private static ICharSequence Of(string str)
    {
        return new StringCharSequence(str);
    }

    [Fact]
    public void TestSingleFrameDecoding()
    {
        ByteBuf incoming = Unpooled.Buffer();
        incoming.WriteBytes(Encoding.Default.GetBytes(StompTestConstants.CONNECT_FRAME));
        channel.WriteInbound(incoming);

        IStompHeadersSubframe frame = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(frame);
        Assert.Equal(StompCommand.CONNECT, frame.Command());

        IStompContentSubframe content = channel.ReadInbound<IStompContentSubframe>();
        Assert.Same(ILastStompContentSubframe.EMPTY_LAST_CONTENT, content);
        content.Release();

        object o = channel.ReadInbound();
        Assert.Null(o);
    }

    [Fact]
    public void TestSingleFrameWithBodyAndContentLength()
    {
        ByteBuf incoming = Unpooled.Buffer();
        incoming.WriteBytes(Encoding.Default.GetBytes(StompTestConstants.SEND_FRAME_2));
        channel.WriteInbound(incoming);

        IStompHeadersSubframe frame = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(frame);
        Assert.Equal(StompCommand.SEND, frame.Command());

        IStompContentSubframe content = channel.ReadInbound<IStompContentSubframe>();
        Assert.IsAssignableFrom<ILastStompContentSubframe>(content);
        string s = content.Content.ToString(Encoding.UTF8);
        Assert.Equal("hello, queue a!!!", s);
        content.Release();

        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestSingleFrameWithBodyWithoutContentLength()
    {
        ByteBuf incoming = Unpooled.Buffer();
        incoming.WriteBytes(Encoding.Default.GetBytes(StompTestConstants.SEND_FRAME_1));
        channel.WriteInbound(incoming);

        IStompHeadersSubframe frame = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(frame);
        Assert.Equal(StompCommand.SEND, frame.Command());

        IStompContentSubframe content = channel.ReadInbound<IStompContentSubframe>();
        Assert.IsAssignableFrom<ILastStompContentSubframe>(content);
        string s = content.Content.ToString(Encoding.UTF8);
        Assert.Equal("hello, queue a!", s);
        content.Release();

        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestSingleFrameChunked()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new StompSubframeDecoder(10000, 5));

        ByteBuf incoming = Unpooled.Buffer();
        incoming.WriteBytes(Encoding.Default.GetBytes(StompTestConstants.SEND_FRAME_2));
        channel.WriteInbound(incoming);

        IStompHeadersSubframe frame = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(frame);
        Assert.Equal(StompCommand.SEND, frame.Command());

        IStompContentSubframe content = channel.ReadInbound<IStompContentSubframe>();
        string s = content.Content.ToString(Encoding.UTF8);
        Assert.Equal("hello", s);
        content.Release();

        content = channel.ReadInbound<IStompContentSubframe>();
        s = content.Content.ToString(Encoding.UTF8);
        Assert.Equal(", que", s);
        content.Release();

        content = channel.ReadInbound<IStompContentSubframe>();
        s = content.Content.ToString(Encoding.UTF8);
        Assert.Equal("ue a!", s);
        content.Release();

        content = channel.ReadInbound<IStompContentSubframe>();
        s = content.Content.ToString(Encoding.UTF8);
        Assert.Equal("!!", s);
        content.Release();

        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestFrameChunkedIncomplete()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new StompSubframeDecoder(10000, 100));

        ByteBuf incoming = Unpooled.Buffer();
        incoming.WriteBytes(Encoding.Default.GetBytes(StompTestConstants.SEND_FRAME_2));
        // Let's truncate the buffer so we don't have anything complete after the header.
        incoming.WriterIndex = incoming.WriterIndex - 2;
        Assert.True(channel.WriteInbound(incoming));

        IStompHeadersSubframe frame = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(frame);
        Assert.Equal(StompCommand.SEND, frame.Command());

        // There is nothing complete to read.
        Assert.Null(channel.ReadInbound());
        Assert.False(channel.FinishAndReleaseAll());
    }

    [Fact]
    public void TestMultipleFramesDecoding()
    {
        ByteBuf incoming = Unpooled.Buffer();
        incoming.WriteBytes(Encoding.Default.GetBytes(StompTestConstants.CONNECT_FRAME));
        incoming.WriteBytes(Encoding.Default.GetBytes(StompTestConstants.CONNECTED_FRAME));
        channel.WriteInbound(incoming);

        IStompHeadersSubframe frame = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(frame);
        Assert.Equal(StompCommand.CONNECT, frame.Command());

        IStompContentSubframe content = channel.ReadInbound<IStompContentSubframe>();
        Assert.Same(ILastStompContentSubframe.EMPTY_LAST_CONTENT, content);
        content.Release();

        IStompHeadersSubframe frame2 = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(frame2);
        Assert.Equal(StompCommand.CONNECTED, frame2.Command());

        IStompContentSubframe content2 = channel.ReadInbound<IStompContentSubframe>();
        Assert.Same(ILastStompContentSubframe.EMPTY_LAST_CONTENT, content2);
        content2.Release();

        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestValidateHeadersDecodingDisabled()
    {
        ByteBuf invalidIncoming = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes(FRAME_WITH_INVALID_HEADER));
        Assert.True(channel.WriteInbound(invalidIncoming));

        IStompHeadersSubframe frame = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(frame);
        Assert.Equal(StompCommand.SEND, frame.Command());
        Assert.True(frame.Headers().Contains(Of("destination")));
        Assert.True(frame.Headers().Contains(Of("content-type")));
        Assert.False(frame.Headers().Contains(Of("current-time")));

        IStompContentSubframe content = channel.ReadInbound<IStompContentSubframe>();
        string s = content.Content.ToString(Encoding.UTF8);
        Assert.Equal("some body", s);
        content.Release();
    }

    [Fact]
    public void TestValidateHeadersDecodingEnabled()
    {
        channel = new EmbeddedChannel(new StompSubframeDecoder(true));

        ByteBuf invalidIncoming = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(FRAME_WITH_INVALID_HEADER));
        Assert.True(channel.WriteInbound(invalidIncoming));

        IStompHeadersSubframe frame = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(frame);
        Assert.True(frame.DecoderResult().IsFailure());
        Assert.Equal("a header value or name contains a prohibited character ':', current-time:2000-01-01T00:00:00",
                frame.DecoderResult().Cause().Message);
    }

    [Fact]
    public void TestNotValidFrameWithEmptyHeaderName()
    {
        channel = new EmbeddedChannel(new StompSubframeDecoder(true));

        ByteBuf invalidIncoming = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(FRAME_WITH_EMPTY_HEADER_NAME));
        Assert.True(channel.WriteInbound(invalidIncoming));

        IStompHeadersSubframe frame = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(frame);
        Assert.True(frame.DecoderResult().IsFailure());
        Assert.Equal("received an invalid header line ':header-value'",
                     frame.DecoderResult().Cause().Message);
    }

    [Fact]
    public void TestUtf8FrameDecoding()
    {
        channel = new EmbeddedChannel(new StompSubframeDecoder(true));

        ByteBuf incoming = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(SEND_FRAME_UTF8));
        Assert.True(channel.WriteInbound(incoming));

        IStompHeadersSubframe headersSubFrame = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(headersSubFrame);
        Assert.False(headersSubFrame.DecoderResult().IsFailure());
        Assert.Equal("/queue/№11±♛нетти♕", headersSubFrame.Headers().GetAsString(Of("destination")));
        Assert.True(headersSubFrame.Headers().Contains(Of("content-type")));

        IStompContentSubframe contentSubFrame = channel.ReadInbound<IStompContentSubframe>();
        Assert.NotNull(contentSubFrame);
        Assert.Equal("body", contentSubFrame.Content.ToString(Encoding.UTF8));
        Assert.True(contentSubFrame.Release());
    }

    [Fact]
    public void TestFrameWithContentLengthAndWithoutNullEnding()
    {
        channel = new EmbeddedChannel(new StompSubframeDecoder(true));

        ByteBuf incoming = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(FRAME_WITHOUT_NULL_ENDING));
        Assert.True(channel.WriteInbound(incoming));

        IStompHeadersSubframe headersFrame = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(headersFrame);
        Assert.False(headersFrame.DecoderResult().IsFailure());

        IStompContentSubframe lastContentFrame = channel.ReadInbound<IStompContentSubframe>();
        Assert.NotNull(lastContentFrame);
        Assert.True(lastContentFrame.DecoderResult().IsFailure());
        Assert.Equal("unexpected byte in buffer 1 while expecting NULL byte",
                     lastContentFrame.DecoderResult().Cause().Message);
    }

    [Fact]
    public void TestUnescapeHeaders()
    {
        channel = new EmbeddedChannel(new StompSubframeDecoder(true));

        ByteBuf incoming = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(StompTestConstants.ESCAPED_MESSAGE_FRAME));
        Assert.True(channel.WriteInbound(incoming));

        IStompHeadersSubframe headersSubFrame = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(headersSubFrame);
        Assert.False(headersSubFrame.DecoderResult().IsFailure());
        Assert.Equal(6, headersSubFrame.Headers().Size());
        Assert.Equal(Of("/queue/a:"), headersSubFrame.Headers().Get(IStompHeaders.DESTINATION));
        Assert.Equal(Of("header\\\r\n:Value"), headersSubFrame.Headers().Get(Of("header\\\r\n:Name")));
        Assert.Equal(Of("header_\\_\r_\n_:_Value"), headersSubFrame.Headers().Get(Of("header_\\_\r_\n_:_Name")));
        Assert.Equal(Of(":headerValue"), headersSubFrame.Headers().Get(Of("headerName:")));

        IStompContentSubframe content = channel.ReadInbound<IStompContentSubframe>();
        Assert.Same(ILastStompContentSubframe.EMPTY_LAST_CONTENT, content);
        content.Release();

        object obj = channel.ReadInbound();
        Assert.Null(obj);
    }

    [Fact]
    public void TestNotUnescapeHeadersForConnectCommand()
    {
        string expectedStompFrame = "CONNECT\n"
                + "headerName-\\\\:headerValue-\\\\\n"
                + "\n" + '\0';
        channel = new EmbeddedChannel(new StompSubframeDecoder(true));

        ByteBuf incoming = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(expectedStompFrame));
        Assert.True(channel.WriteInbound(incoming));

        IStompHeadersSubframe headersSubFrame = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(headersSubFrame);
        Assert.False(headersSubFrame.DecoderResult().IsFailure());
        Assert.Equal(1, headersSubFrame.Headers().Size());
        Assert.Equal(Of("headerValue-\\\\"), headersSubFrame.Headers().Get(Of("headerName-\\\\")));

        IStompContentSubframe content = channel.ReadInbound<IStompContentSubframe>();
        Assert.Same(ILastStompContentSubframe.EMPTY_LAST_CONTENT, content);
        content.Release();

        object obj = channel.ReadInbound();
        Assert.Null(obj);
    }

    [Fact]
    public void TestNotUnescapeHeadersForConnectedCommand()
    {
        string expectedStompFrame = "CONNECTED\n"
                + "headerName-\\\\:headerValue-\\\\\n"
                + "\n" + '\0';
        channel = new EmbeddedChannel(new StompSubframeDecoder(true));

        ByteBuf incoming = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(expectedStompFrame));
        Assert.True(channel.WriteInbound(incoming));

        IStompHeadersSubframe headersSubFrame = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(headersSubFrame);
        Assert.False(headersSubFrame.DecoderResult().IsFailure());
        Assert.Equal(1, headersSubFrame.Headers().Size());
        Assert.Equal(Of("headerValue-\\\\"), headersSubFrame.Headers().Get(Of("headerName-\\\\")));

        IStompContentSubframe content = channel.ReadInbound<IStompContentSubframe>();
        Assert.Same(ILastStompContentSubframe.EMPTY_LAST_CONTENT, content);
        content.Release();

        object obj = channel.ReadInbound();
        Assert.Null(obj);
    }

    [Fact]
    public void TestHeartbeatOnlyDoesNotThrowException()
    {
        // STOMP heartbeat is just a LF byte - should not cause IndexOutOfBoundsException
        ByteBuf heartbeat = Unpooled.Buffer();
        heartbeat.WriteByte('\n');
        channel.WriteInbound(heartbeat);

        // Heartbeat should be consumed silently, no output produced
        object result = channel.ReadInbound();
        Assert.Null(result);
    }

    [Fact]
    public void TestMultipleHeartbeatsDoNotThrowException()
    {
        // Multiple consecutive heartbeats
        ByteBuf heartbeats = Unpooled.Buffer();
        heartbeats.WriteByte('\n');
        heartbeats.WriteByte('\n');
        heartbeats.WriteByte('\n');
        channel.WriteInbound(heartbeats);

        object result = channel.ReadInbound();
        Assert.Null(result);
    }

    [Fact]
    public void TestCarriageReturnLineFeedHeartbeat()
    {
        // CR+LF heartbeat
        ByteBuf heartbeat = Unpooled.Buffer();
        heartbeat.WriteByte('\r');
        heartbeat.WriteByte('\n');
        channel.WriteInbound(heartbeat);

        object result = channel.ReadInbound();
        Assert.Null(result);
    }

    [Fact]
    public void TestHeartbeatFollowedByFrame()
    {
        // Heartbeat bytes followed by a real STOMP frame should decode correctly
        ByteBuf incoming = Unpooled.Buffer();
        incoming.WriteByte('\n');
        incoming.WriteByte('\n');
        incoming.WriteBytes(Encoding.Default.GetBytes(StompTestConstants.CONNECT_FRAME));
        channel.WriteInbound(incoming);

        IStompHeadersSubframe frame = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(frame);
        Assert.Equal(StompCommand.CONNECT, frame.Command());

        IStompContentSubframe content = channel.ReadInbound<IStompContentSubframe>();
        Assert.Same(ILastStompContentSubframe.EMPTY_LAST_CONTENT, content);
        content.Release();

        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestHeartbeatBetweenFrames()
    {
        // Heartbeat bytes between two STOMP frames
        ByteBuf incoming = Unpooled.Buffer();
        incoming.WriteBytes(Encoding.Default.GetBytes(StompTestConstants.CONNECT_FRAME));
        incoming.WriteByte('\n');
        incoming.WriteByte('\n');
        incoming.WriteBytes(Encoding.Default.GetBytes(StompTestConstants.CONNECTED_FRAME));
        channel.WriteInbound(incoming);

        IStompHeadersSubframe frame1 = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(frame1);
        Assert.Equal(StompCommand.CONNECT, frame1.Command());

        IStompContentSubframe content1 = channel.ReadInbound<IStompContentSubframe>();
        Assert.Same(ILastStompContentSubframe.EMPTY_LAST_CONTENT, content1);
        content1.Release();

        IStompHeadersSubframe frame2 = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(frame2);
        Assert.Equal(StompCommand.CONNECTED, frame2.Command());

        IStompContentSubframe content2 = channel.ReadInbound<IStompContentSubframe>();
        Assert.Same(ILastStompContentSubframe.EMPTY_LAST_CONTENT, content2);
        content2.Release();

        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestHeartbeatSentSeparatelyThenFrame()
    {
        // Simulate heartbeat arriving in a separate TCP segment, then a frame later
        ByteBuf heartbeat = Unpooled.Buffer();
        heartbeat.WriteByte('\n');
        channel.WriteInbound(heartbeat);

        // No output from heartbeat
        Assert.Null(channel.ReadInbound());

        // Now send a real frame
        ByteBuf frame = Unpooled.Buffer();
        frame.WriteBytes(Encoding.Default.GetBytes(StompTestConstants.CONNECT_FRAME));
        channel.WriteInbound(frame);

        IStompHeadersSubframe headersSubframe = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(headersSubframe);
        Assert.Equal(StompCommand.CONNECT, headersSubframe.Command());

        IStompContentSubframe content = channel.ReadInbound<IStompContentSubframe>();
        Assert.Same(ILastStompContentSubframe.EMPTY_LAST_CONTENT, content);
        content.Release();

        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestInvalidEscapeHeadersSequence()
    {
        channel = new EmbeddedChannel(new StompSubframeDecoder(true));

        ByteBuf incoming = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(INVALID_ESCAPED_MESSAGE_FRAME));
        Assert.True(channel.WriteInbound(incoming));

        IStompHeadersSubframe headersSubFrame = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(headersSubFrame);
        Assert.True(headersSubFrame.DecoderResult().IsFailure());

        Assert.Equal("received an invalid escape header sequence 'custom_invalid\\t'",
                     headersSubFrame.DecoderResult().Cause().Message);
    }

    [Fact]
    public void TestCRResetsUtf8DecodeState()
    {
        // When a CR byte appears during a multi-byte UTF-8 sequence, the parser's interim state
        // should be cleared so that subsequent bytes are decoded correctly.
        // Bug: CR is skipped without resetting interim/nextRead, so the next byte gets
        // incorrectly combined with dirty UTF-8 state, producing garbage characters.
        //
        // Craft a header value where:
        // - 0xC3 starts a 2-byte UTF-8 sequence (sets interim)
        // - 0x0D (CR) is skipped but should clear interim state
        // - 0x41 ('A') should be decoded as plain ASCII 'A', not combined with dirty interim
        channel = new EmbeddedChannel(new StompSubframeDecoder());

        ByteBuf incoming = Unpooled.Buffer();
        // CONNECT command line
        incoming.WriteBytes(Encoding.UTF8.GetBytes("CONNECT\r\n"));
        // header with multi-byte UTF-8 start byte (0xC3), then CR, then ASCII 'A'
        incoming.WriteByte((byte) 'h');
        incoming.WriteByte((byte) 'e');
        incoming.WriteByte((byte) 'a');
        incoming.WriteByte((byte) 'd');
        incoming.WriteByte((byte) 'e');
        incoming.WriteByte((byte) 'r');
        incoming.WriteByte((byte) ':');
        // 0xC3 starts a 2-byte UTF-8 sequence - sets interim state
        incoming.WriteByte((byte) 0xC3);
        // CR (0x0D) - should be skipped AND clear the interim UTF-8 state
        incoming.WriteByte((byte) 0x0D);
        // 'A' - should be decoded as plain 'A' since CR should have reset state
        incoming.WriteByte((byte) 'A');
        // end of header line
        incoming.WriteByte((byte) '\n');
        // empty line to end headers
        incoming.WriteByte((byte) '\n');
        // null byte to end frame
        incoming.WriteByte((byte) '\0');

        Assert.True(channel.WriteInbound(incoming));

        IStompHeadersSubframe frame = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(frame);
        Assert.Equal(StompCommand.CONNECT, frame.Command());
        // The header value should be just "A" (CR is skipped, UTF-8 state reset)
        // With the bug, it would contain corrupted UTF-8 combining 0xC3 with 'A'
        Assert.Equal(Of("A"), frame.Headers().Get(Of("header")));

        IStompContentSubframe content = channel.ReadInbound<IStompContentSubframe>();
        Assert.Same(ILastStompContentSubframe.EMPTY_LAST_CONTENT, content);
        content.Release();

        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestMaxNumHeadersEnforced()
    {
        // limit to 1 header as CONNECT_FRAME has 2.
        channel = new EmbeddedChannel(new StompSubframeDecoder(1024, 1024, 1, true));

        ByteBuf incoming = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(CONNECT_FRAME));
        Assert.True(channel.WriteInbound(incoming));

        IStompHeadersSubframe headersSubFrame = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(headersSubFrame);
        Assert.True(headersSubFrame.DecoderResult().IsFailure());

        Assert.IsAssignableFrom<TooLongFrameException>(
                headersSubFrame.DecoderResult().Cause());
    }

    [Fact]
    public void TestMaxNumHeadersEnforcedForInvalidHeaders()
    {
        // limit to 1 header as CONNECT_FRAME has 2.
        channel = new EmbeddedChannel(new StompSubframeDecoder(1024, 1024, 2, false));

        ByteBuf incoming = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(FRAME_WITH_INVALID_HEADER));
        Assert.True(channel.WriteInbound(incoming));

        IStompHeadersSubframe headersSubFrame = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(headersSubFrame);
        Assert.True(headersSubFrame.DecoderResult().IsFailure());

        Assert.IsAssignableFrom<TooLongFrameException>(
                headersSubFrame.DecoderResult().Cause());
    }

    [Fact]
    public void TestContentLengthExceedingIntegerMaxValueIsRejected()
    {
        // content-length larger than Integer.MAX_VALUE must be rejected, otherwise the truncating
        // cast to int when computing the remaining chunk length can wrap around and cause the
        // decoder to loop indefinitely instead of terminating the frame.
        string frame = "SEND\n"
                + "destination:/queue/a\n"
                + "content-length:2147483648\n"
                + "\n" + '\0';
        ByteBuf incoming = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(frame));
        Assert.True(channel.WriteInbound(incoming));

        IStompHeadersSubframe headersSubFrame = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(headersSubFrame);
        Assert.True(headersSubFrame.DecoderResult().IsFailure());
        Assert.IsAssignableFrom<TooLongFrameException>(headersSubFrame.DecoderResult().Cause());

        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestContentLengthEqualToIntegerMaxValueIsAccepted()
    {
        channel = new EmbeddedChannel(new StompSubframeDecoder());
        string frame = "SEND\n"
                + "destination:/queue/a\n"
                + "content-length:2147483647\n"
                + "\n" + '\0';
        ByteBuf incoming = Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes(frame));
        Assert.True(channel.WriteInbound(incoming));

        IStompHeadersSubframe headersSubFrame = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(headersSubFrame);
        Assert.False(headersSubFrame.DecoderResult().IsFailure());

        // No content was actually sent, so the decoder should simply wait for more bytes
        // rather than producing any (partial) content subframes.
        Assert.Null(channel.ReadInbound());
        Assert.True(channel.FinishAndReleaseAll());
    }

    // Read sizes: byte by byte, a few bytes, about half of the frame, and the whole frame in one read.
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(90)]
    [InlineData(int.MaxValue)]
    public void TestMaxNumHeadersNotExceededWhenHeadersSplitAcrossReads(int readSize)
    {
        // 10 headers and a limit of 10: the frame must be accepted however it is split into reads.
        IStompHeadersSubframe headersSubFrame = DecodeHeaders(FrameWithHeaders(10), readSize, 10);
        Assert.True(headersSubFrame.DecoderResult().IsSuccess(), headersSubFrame.DecoderResult().ToString());
        Assert.Equal(10, headersSubFrame.Headers().Size());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(90)]
    [InlineData(int.MaxValue)]
    public void TestMaxNumHeadersEnforcedWhenHeadersSplitAcrossReads(int readSize)
    {
        IStompHeadersSubframe headersSubFrame = DecodeHeaders(FrameWithHeaders(11), readSize, 10);
        Assert.True(headersSubFrame.DecoderResult().IsFailure());
        Assert.IsAssignableFrom<TooLongFrameException>(headersSubFrame.DecoderResult().Cause());
    }

    private static byte[] FrameWithHeaders(int numHeaders)
    {
        StringBuilder frame = new StringBuilder("SEND\ndestination:/queue/a\n");

        for (int i = 1; i < numHeaders; i++)
        {
            frame.Append("header").Append(i).Append(":value").Append(i).Append('\n');
        }

        return Encoding.UTF8.GetBytes(frame.Append("\nbody\0").ToString());
    }

    private static IStompHeadersSubframe DecodeHeaders(byte[] frame, int readSize, int maxNumHeaders)
    {
        EmbeddedChannel channel = new EmbeddedChannel(new StompSubframeDecoder(1024, 1024, maxNumHeaders, true));

        for (int i = 0; i < frame.Length; i += Math.Min(readSize, frame.Length - i))
        {
            channel.WriteInbound(Unpooled.WrappedBuffer(frame, i, Math.Min(readSize, frame.Length - i)));
        }

        IStompHeadersSubframe headersSubFrame = channel.ReadInbound<IStompHeadersSubframe>();
        Assert.NotNull(headersSubFrame);
        channel.FinishAndReleaseAll();
        return headersSubFrame;
    }
}
