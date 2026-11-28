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

namespace Netty.NET.Handler.Codec.Stomp.Tests;

// CLR: xUnit creates a new test class instance for every test, so the @BeforeEach setup() runs from the
// constructor and the @AfterEach teardown() from Dispose() (https://xunit.net/docs/shared-context).
// CharsetUtil.UTF_8 and StandardCharsets.UTF_8 are Encoding.UTF8. Java passes Strings as CharSequence header names
// and values; C# string is not an ICharSequence, so Of(...) wraps them in the Common StringCharSequence, and
// AsciiString.of(CharSequence) takes it too. The AssertJ chain
// assertThatThrownBy(...).isInstanceOf(EncoderException).hasRootCauseInstanceOf(IllegalArgumentException).hasMessageContaining(text)
// is Assert.ThrowsAny<EncoderException> (isInstanceOf accepts subtypes), Exception.GetBaseException() for the root
// cause (https://learn.microsoft.com/en-us/dotnet/api/system.exception.getbaseexception), Assert.IsAssignableFrom
// and Assert.Contains on the thrown exception's message, whose Java detail text is cause.toString().
// @ParameterizedTest @ValueSource(chars) is a [Theory] with one [InlineData] per char.
public class StompSubframeEncoderTest : IDisposable
{

    private EmbeddedChannel channel;

    public StompSubframeEncoderTest()
    {
        Setup();
    }

    public void Dispose()
    {
        Teardown();
    }

    private void Setup()
    {
        channel = new EmbeddedChannel(new StompSubframeEncoder());
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
    public void TestFrameAndContentEncoding()
    {
        IStompHeadersSubframe frame = new DefaultStompHeadersSubframe(StompCommand.CONNECT);
        IStompHeaders headers = frame.Headers();
        headers.Set(IStompHeaders.HOST, Of("stomp.github.org"));
        headers.Set(IStompHeaders.ACCEPT_VERSION, Of("1.1,1.2"));
        channel.WriteOutbound(frame);
        channel.WriteOutbound(ILastStompContentSubframe.EMPTY_LAST_CONTENT);
        ByteBuf aggregatedBuffer = Unpooled.Buffer();
        ByteBuf byteBuf = channel.ReadOutbound<ByteBuf>();
        Assert.NotNull(byteBuf);
        aggregatedBuffer.WriteBytes(byteBuf);
        byteBuf.Release();

        byteBuf = channel.ReadOutbound<ByteBuf>();
        Assert.NotNull(byteBuf);
        aggregatedBuffer.WriteBytes(byteBuf);
        byteBuf.Release();

        aggregatedBuffer.ResetReaderIndex();
        string content = aggregatedBuffer.ToString(Encoding.UTF8);
        Assert.Equal(StompTestConstants.CONNECT_FRAME, content);
        aggregatedBuffer.Release();
    }

    [Fact]
    public void TestUtf8FrameEncoding()
    {
        IStompFrame frame = new DefaultStompFrame(StompCommand.SEND,
                                                 Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes("body")));
        IStompHeaders incoming = frame.Headers();
        incoming.Set(IStompHeaders.DESTINATION, Of("/queue/№11±♛нетти♕"));
        incoming.Set(IStompHeaders.CONTENT_TYPE, AsciiString.Of(Of("text/plain")));

        channel.WriteOutbound(frame);

        ByteBuf fullFrame = channel.ReadOutbound<ByteBuf>();
        Assert.Equal(SEND_FRAME_UTF8, fullFrame.ToString(Encoding.UTF8));
        Assert.True(fullFrame.Release());
    }

    [Fact]
    public void TestOneBufferForStompFrameWithEmptyContent()
    {
        IStompFrame connectedFrame = new DefaultStompFrame(StompCommand.CONNECTED);
        connectedFrame.Headers().Set(IStompHeaders.VERSION, Of("1.2"));

        Assert.True(channel.WriteOutbound(connectedFrame));

        ByteBuf stompBuffer = channel.ReadOutbound<ByteBuf>();

        Assert.NotNull(stompBuffer);
        Assert.Null(channel.ReadOutbound());
        Assert.Equal("CONNECTED\nversion:1.2\n\n\0", stompBuffer.ToString(Encoding.UTF8));
        Assert.True(stompBuffer.Release());
    }

    [Fact]
    public void TestEscapeStompHeaders()
    {
        IStompFrame messageFrame = new DefaultStompFrame(StompCommand.MESSAGE);
        messageFrame.Headers()
                  .Add(IStompHeaders.MESSAGE_ID, Of("100"))
                  .Add(IStompHeaders.SUBSCRIPTION, Of("1"))
                  .Add(IStompHeaders.DESTINATION, Of("/queue/a:"))
                  .Add(Of("header\\\r\n:Name"), Of("header\\\r\n:Value"))
                  .Add(Of("header_\\_\r_\n_:_Name"), Of("header_\\_\r_\n_:_Value"))
                  .Add(Of("headerName:"), Of(":headerValue"));

        Assert.True(channel.WriteOutbound(messageFrame));

        ByteBuf stompBuffer = channel.ReadOutbound<ByteBuf>();
        Assert.NotNull(stompBuffer);
        Assert.Null(channel.ReadOutbound());

        Assert.Equal(StompTestConstants.ESCAPED_MESSAGE_FRAME, stompBuffer.ToString(Encoding.UTF8));
        Assert.True(stompBuffer.Release());
    }

    [Fact]
    public void MustRejectNulCharacterInHeaders()
    {
        // The NUL character has no escape and is always rejected.
        IStompFrame frame1 = new DefaultStompFrame(StompCommand.MESSAGE);
        frame1.Headers()
                .Add(Of("header\0"), Of("value"));
        EncoderException thrown1 = Assert.ThrowsAny<EncoderException>(() => channel.WriteOutbound(frame1));
        Assert.IsAssignableFrom<ArgumentException>(thrown1.GetBaseException());
        Assert.Contains("illegal character", thrown1.Message);

        IStompFrame frame2 = new DefaultStompFrame(StompCommand.CONNECT);
        frame2.Headers()
                .Add(Of("header"), Of("value\0"));
        EncoderException thrown2 = Assert.ThrowsAny<EncoderException>(() => channel.WriteOutbound(frame2));
        Assert.IsAssignableFrom<ArgumentException>(thrown2.GetBaseException());
        Assert.Contains("illegal character", thrown2.Message);
    }

    [Fact]
    public void MustRejectEmptyHeaderNames()
    {
        IStompFrame frame1 = new DefaultStompFrame(StompCommand.MESSAGE);
        frame1.Headers()
                .Add(Of(""), Of("value"));
        EncoderException thrown = Assert.ThrowsAny<EncoderException>(() => channel.WriteOutbound(frame1));
        Assert.IsAssignableFrom<ArgumentException>(thrown.GetBaseException());
        Assert.Contains("empty header name", thrown.Message);
    }

    [Fact]
    public void TestNotEscapeStompHeadersForConnectCommand()
    {
        string expectedStompFrame = "CONNECT\n"
                + "backslashHeaderName-\\:backslashHeaderValue-\\\n"
                + '\n' + '\0';
        IStompFrame connectFrame = new DefaultStompFrame(StompCommand.CONNECT);
        connectFrame.Headers()
                  .Add(Of("backslashHeaderName-\\"), Of("backslashHeaderValue-\\"));

        Assert.True(channel.WriteOutbound(connectFrame));

        ByteBuf stompBuffer = channel.ReadOutbound<ByteBuf>();
        Assert.NotNull(stompBuffer);
        Assert.Null(channel.ReadOutbound());

        Assert.Equal(expectedStompFrame, stompBuffer.ToString(Encoding.UTF8));
        Assert.True(stompBuffer.Release());
    }

    [Theory]
    [InlineData('\r')]
    [InlineData('\n')]
    [InlineData('\0')]
    [InlineData(':')]
    public void MustRejectIllegalCharsInConnectCommandHeaders(char illegalChar)
    {
        IStompFrame connectFrame1 = new DefaultStompFrame(StompCommand.CONNECT);
        connectFrame1.Headers()
                .Add(Of("header" + illegalChar), Of("value"));
        EncoderException thrown1 = Assert.ThrowsAny<EncoderException>(() => channel.WriteOutbound(connectFrame1));
        Assert.IsAssignableFrom<ArgumentException>(thrown1.GetBaseException());
        Assert.Contains("illegal character", thrown1.Message);

        IStompFrame connectFrame2 = new DefaultStompFrame(StompCommand.CONNECT);
        connectFrame2.Headers()
                .Add(Of("header"), Of("value" + illegalChar));
        EncoderException thrown2 = Assert.ThrowsAny<EncoderException>(() => channel.WriteOutbound(connectFrame2));
        Assert.IsAssignableFrom<ArgumentException>(thrown2.GetBaseException());
        Assert.Contains("illegal character", thrown2.Message);
    }

    [Fact]
    public void TestNotEscapeStompHeadersForConnectedCommand()
    {
        string expectedStompFrame = "CONNECTED\n"
                                    + "backslashHeaderName-\\:backslashHeaderValue-\\\n"
                                    + '\n' + '\0';
        IStompFrame connectedFrame = new DefaultStompFrame(StompCommand.CONNECTED);
        connectedFrame.Headers()
                    .Add(Of("backslashHeaderName-\\"), Of("backslashHeaderValue-\\"));

        Assert.True(channel.WriteOutbound(connectedFrame));

        ByteBuf stompBuffer = channel.ReadOutbound<ByteBuf>();
        Assert.NotNull(stompBuffer);
        Assert.Null(channel.ReadOutbound());

        Assert.Equal(expectedStompFrame, stompBuffer.ToString(Encoding.UTF8));
        Assert.True(stompBuffer.Release());
    }

    [Theory]
    [InlineData('\r')]
    [InlineData('\n')]
    [InlineData('\0')]
    [InlineData(':')]
    public void MustRejectIllegalCharsInConnectedCommandHeaders(char illegalChar)
    {
        IStompFrame connectedFrame1 = new DefaultStompFrame(StompCommand.CONNECTED);
        connectedFrame1.Headers()
                .Add(Of("header" + illegalChar), Of("name"));
        EncoderException thrown1 = Assert.ThrowsAny<EncoderException>(() => channel.WriteOutbound(connectedFrame1));
        Assert.IsAssignableFrom<ArgumentException>(thrown1.GetBaseException());
        Assert.Contains("illegal character", thrown1.Message);

        IStompFrame connectedFrame2 = new DefaultStompFrame(StompCommand.CONNECTED);
        connectedFrame2.Headers()
                .Add(Of("header"), Of("name" + illegalChar));
        EncoderException thrown2 = Assert.ThrowsAny<EncoderException>(() => channel.WriteOutbound(connectedFrame2));
        Assert.IsAssignableFrom<ArgumentException>(thrown2.GetBaseException());
        Assert.Contains("illegal character", thrown2.Message);
    }
}
