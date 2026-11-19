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
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Stomp.Tests;

// CLR: xUnit creates a new test class instance for every test, so the @BeforeEach setup() runs from the constructor
// and the @AfterEach teardown() from Dispose() (https://xunit.net/docs/shared-context). String.getBytes() encodes with
// the default charset, Encoding.Default, and CharsetUtil.UTF_8 is Encoding.UTF8 (docs/codec-base-porting.md); the
// anonymous Executable is a lambda.
public class StompSubframeAggregatorTest : IDisposable
{

    private EmbeddedChannel channel;

    public StompSubframeAggregatorTest()
    {
        Setup();
    }

    public void Dispose()
    {
        Teardown();
    }

    private void Setup()
    {
        channel = new EmbeddedChannel(new StompSubframeDecoder(), new StompSubframeAggregator(100000));
    }

    private void Teardown()
    {
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestSingleFrameDecoding()
    {
        ByteBuf incoming = Unpooled.Buffer();
        incoming.WriteBytes(Encoding.Default.GetBytes(StompTestConstants.CONNECT_FRAME));
        channel.WriteInbound(incoming);

        IStompFrame frame = channel.ReadInbound<IStompFrame>();
        frame.Release();

        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestSingleFrameWithBodyAndContentLength()
    {
        ByteBuf incoming = Unpooled.Buffer();
        incoming.WriteBytes(Encoding.Default.GetBytes(StompTestConstants.SEND_FRAME_2));
        channel.WriteInbound(incoming);

        IStompFrame frame = channel.ReadInbound<IStompFrame>();
        Assert.NotNull(frame);
        Assert.Equal(StompCommand.SEND, frame.Command());
        Assert.Equal("hello, queue a!!!", frame.Content.ToString(Encoding.UTF8));
        frame.Release();

        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestSingleFrameWithBodyAndNoContentLength()
    {
        ByteBuf incoming = Unpooled.Buffer();
        incoming.WriteBytes(Encoding.Default.GetBytes(StompTestConstants.SEND_FRAME_4));
        channel.WriteInbound(incoming);

        IStompFrame frame = channel.ReadInbound<IStompFrame>();
        Assert.NotNull(frame);
        Assert.Equal(StompCommand.SEND, frame.Command());
        Assert.Equal("body", frame.Content.ToString(Encoding.UTF8));
        frame.Release();

        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestSingleFrameWithSplitBodyAndNoContentLength()
    {
        for (int n = 0; n < StompTestConstants.SEND_FRAMES_3.Length; ++n)
        {
            ByteBuf incoming = Unpooled.Buffer();
            incoming.WriteBytes(Encoding.Default.GetBytes(StompTestConstants.SEND_FRAMES_3[n]));
            channel.WriteInbound(incoming);
            channel.Flush();
        }

        IStompFrame frame = channel.ReadInbound<IStompFrame>();
        Assert.NotNull(frame);
        Assert.Equal(StompCommand.SEND, frame.Command());
        Assert.Equal("first part of body\nsecond part of body", frame.Content.ToString(Encoding.UTF8));
        frame.Release();

        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestSingleFrameChunked()
    {
        EmbeddedChannel channel = new EmbeddedChannel(
                new StompSubframeDecoder(10000, 5), new StompSubframeAggregator(100000));
        ByteBuf incoming = Unpooled.Buffer();
        incoming.WriteBytes(Encoding.Default.GetBytes(StompTestConstants.SEND_FRAME_2));
        channel.WriteInbound(incoming);

        IStompFrame frame = channel.ReadInbound<IStompFrame>();
        Assert.NotNull(frame);
        Assert.Equal(StompCommand.SEND, frame.Command());
        frame.Release();

        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestMultipleFramesDecoding()
    {
        ByteBuf incoming = Unpooled.Buffer();
        incoming.WriteBytes(Encoding.Default.GetBytes(StompTestConstants.CONNECT_FRAME));
        incoming.WriteBytes(Encoding.Default.GetBytes(StompTestConstants.CONNECTED_FRAME));
        channel.WriteInbound(incoming);
        channel.WriteInbound(Unpooled.WrappedBuffer(Encoding.Default.GetBytes(StompTestConstants.SEND_FRAME_1)));

        IStompFrame frame = channel.ReadInbound<IStompFrame>();
        Assert.Equal(StompCommand.CONNECT, frame.Command());
        frame.Release();

        frame = channel.ReadInbound<IStompFrame>();
        Assert.Equal(StompCommand.CONNECTED, frame.Command());
        frame.Release();

        frame = channel.ReadInbound<IStompFrame>();
        Assert.Equal(StompCommand.SEND, frame.Command());
        frame.Release();

        Assert.Null(channel.ReadInbound());
    }

    [Fact]
    public void TestTooLongFrameException()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new StompSubframeDecoder(),
                new StompSubframeAggregator(10));
        Assert.Throws<TooLongFrameException>(() =>
                channel.WriteInbound(Unpooled.WrappedBuffer(Encoding.Default.GetBytes(StompTestConstants.SEND_FRAME_1))));
    }
}
