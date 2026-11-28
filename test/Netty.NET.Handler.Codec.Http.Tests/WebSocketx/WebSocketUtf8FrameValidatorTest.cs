/*
 * Copyright 2019 The Netty Project
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

using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx;

// CLR: The third assertThrows argument is only JUnit's failure message and has no xUnit counterpart. The signed
// Java byte literals are written with unchecked((byte)...), which keeps their bit patterns (-50 is 0xCE).
// readOutbound() is ReadOutbound<T>(), refCnt() is RefCnt and isActive() is IsActive of the Transport
// EmbeddedChannel.
public class WebSocketUtf8FrameValidatorTest
{

    [Fact]
    public void TestCorruptedFrameExceptionInFinish()
    {
        AssertCorruptedFrameExceptionHandling(new byte[] { unchecked((byte) -50) });
    }

    [Fact]
    public void TestCorruptedFrameExceptionInCheck()
    {
        AssertCorruptedFrameExceptionHandling(new byte[]
        {
            unchecked((byte) -8), unchecked((byte) -120), unchecked((byte) -128), unchecked((byte) -128),
            unchecked((byte) -128)
        });
    }

    [Fact]
    public void TestNotCloseOnProtocolViolation()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new Utf8FrameValidator(false));
        TextWebSocketFrame frame = new TextWebSocketFrame(Unpooled.CopiedBuffer(new byte[] { unchecked((byte) -50) }));
        Assert.ThrowsAny<CorruptedWebSocketFrameException>(() => channel.WriteInbound(frame));

        Assert.True(channel.IsActive);
        Assert.False(channel.Finish());
        Assert.Equal(0, frame.RefCnt);
    }

    private void AssertCorruptedFrameExceptionHandling(byte[] data)
    {
        EmbeddedChannel channel = new EmbeddedChannel(new Utf8FrameValidator());
        TextWebSocketFrame frame = new TextWebSocketFrame(Unpooled.CopiedBuffer(data));
        Assert.ThrowsAny<CorruptedWebSocketFrameException>(() => channel.WriteInbound(frame));

        Assert.False(channel.IsActive);

        CloseWebSocketFrame closeFrame = channel.ReadOutbound<CloseWebSocketFrame>();
        Assert.NotNull(closeFrame);
        Assert.Equal("bytes are not UTF-8", closeFrame.ReasonText());
        Assert.Equal(1007, closeFrame.StatusCode());
        Assert.True(closeFrame.Release());

        Assert.Equal(0, frame.RefCnt);
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestCloseWithStatusInTheMiddleOfFragmentAllowed()
    {
        TestControlFrameInTheMiddleOfFragmentAllowed(new CloseWebSocketFrame(WebSocketCloseStatus.NORMAL_CLOSURE));
    }

    [Fact]
    public void TestPingInTheMiddleOfFragmentAllowed()
    {
        TestControlFrameInTheMiddleOfFragmentAllowed(new PingWebSocketFrame(Unpooled.EmptyBuffer));
    }

    [Fact]
    public void TestPongInTheMiddleOfFragmentAllowed()
    {
        TestControlFrameInTheMiddleOfFragmentAllowed(new PongWebSocketFrame(Unpooled.EmptyBuffer));
    }

    private static void TestControlFrameInTheMiddleOfFragmentAllowed(WebSocketFrame controlFrame)
    {
        EmbeddedChannel channel = new EmbeddedChannel(new Utf8FrameValidator(false));
        TextWebSocketFrame frame = new TextWebSocketFrame(false, 0, "text");
        Assert.True(channel.WriteInbound(frame));
        Assert.True(channel.WriteInbound(controlFrame));
        Assert.True(channel.FinishAndReleaseAll());
    }
}
