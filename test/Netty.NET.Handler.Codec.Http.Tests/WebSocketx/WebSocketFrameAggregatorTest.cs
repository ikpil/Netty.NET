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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx;

// CLR: the static initializer is the static constructor and System.arraycopy is Array.Copy. String.getBytes(UTF_8) is
// Encoding.UTF8.GetBytes (CharsetUtil.UTF_8 is Encoding.UTF8, docs/codec-smtp-porting.md), Integer.MAX_VALUE is
// int.MaxValue, assertArrayEquals is Assert.Equal over the arrays, and assertThrows(TooLongFrameException.class, ...)
// is Assert.ThrowsAny<TooLongFrameException> (JUnit accepts subtypes too). readInbound() is ReadInbound<T>().
public class WebSocketFrameAggregatorTest
{
    private static readonly byte[] content1 = Encoding.UTF8.GetBytes("Content1");
    private static readonly byte[] content2 = Encoding.UTF8.GetBytes("Content2");
    private static readonly byte[] content3 = Encoding.UTF8.GetBytes("Content3");
    private static readonly byte[] aggregatedContent = new byte[content1.Length + content2.Length + content3.Length];
    static WebSocketFrameAggregatorTest()
    {
        Array.Copy(content1, 0, aggregatedContent, 0, content1.Length);
        Array.Copy(content2, 0, aggregatedContent, content1.Length, content2.Length);
        Array.Copy(content3, 0, aggregatedContent, content1.Length + content2.Length, content3.Length);
    }

    [Fact]
    public void TestAggregationBinary()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new WebSocketFrameAggregator(int.MaxValue));
        channel.WriteInbound(new BinaryWebSocketFrame(true, 1, Unpooled.WrappedBuffer(content1)));
        channel.WriteInbound(new BinaryWebSocketFrame(false, 0, Unpooled.WrappedBuffer(content1)));
        channel.WriteInbound(new ContinuationWebSocketFrame(false, 0, Unpooled.WrappedBuffer(content2)));
        channel.WriteInbound(new PingWebSocketFrame(Unpooled.WrappedBuffer(content1)));
        channel.WriteInbound(new PongWebSocketFrame(Unpooled.WrappedBuffer(content1)));
        channel.WriteInbound(new ContinuationWebSocketFrame(true, 0, Unpooled.WrappedBuffer(content3)));

        Assert.True(channel.Finish());

        BinaryWebSocketFrame frame = channel.ReadInbound<BinaryWebSocketFrame>();
        Assert.True(frame.IsFinalFragment());
        Assert.Equal(1, frame.Rsv());
        Assert.Equal(content1, ToBytes(frame.Content));

        PingWebSocketFrame frame2 = channel.ReadInbound<PingWebSocketFrame>();
        Assert.True(frame2.IsFinalFragment());
        Assert.Equal(0, frame2.Rsv());
        Assert.Equal(content1, ToBytes(frame2.Content));

        PongWebSocketFrame frame3 = channel.ReadInbound<PongWebSocketFrame>();
        Assert.True(frame3.IsFinalFragment());
        Assert.Equal(0, frame3.Rsv());
        Assert.Equal(content1, ToBytes(frame3.Content));

        BinaryWebSocketFrame frame4 = channel.ReadInbound<BinaryWebSocketFrame>();
        Assert.True(frame4.IsFinalFragment());
        Assert.Equal(0, frame4.Rsv());
        Assert.Equal(aggregatedContent, ToBytes(frame4.Content));

        Assert.Null(channel.ReadInbound<object>());
    }

    [Fact]
    public void TestAggregationText()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new WebSocketFrameAggregator(int.MaxValue));
        channel.WriteInbound(new TextWebSocketFrame(true, 1, Unpooled.WrappedBuffer(content1)));
        channel.WriteInbound(new TextWebSocketFrame(false, 0, Unpooled.WrappedBuffer(content1)));
        channel.WriteInbound(new ContinuationWebSocketFrame(false, 0, Unpooled.WrappedBuffer(content2)));
        channel.WriteInbound(new PingWebSocketFrame(Unpooled.WrappedBuffer(content1)));
        channel.WriteInbound(new PongWebSocketFrame(Unpooled.WrappedBuffer(content1)));
        channel.WriteInbound(new ContinuationWebSocketFrame(true, 0, Unpooled.WrappedBuffer(content3)));

        Assert.True(channel.Finish());

        TextWebSocketFrame frame = channel.ReadInbound<TextWebSocketFrame>();
        Assert.True(frame.IsFinalFragment());
        Assert.Equal(1, frame.Rsv());
        Assert.Equal(content1, ToBytes(frame.Content));

        PingWebSocketFrame frame2 = channel.ReadInbound<PingWebSocketFrame>();
        Assert.True(frame2.IsFinalFragment());
        Assert.Equal(0, frame2.Rsv());
        Assert.Equal(content1, ToBytes(frame2.Content));

        PongWebSocketFrame frame3 = channel.ReadInbound<PongWebSocketFrame>();
        Assert.True(frame3.IsFinalFragment());
        Assert.Equal(0, frame3.Rsv());
        Assert.Equal(content1, ToBytes(frame3.Content));

        TextWebSocketFrame frame4 = channel.ReadInbound<TextWebSocketFrame>();
        Assert.True(frame4.IsFinalFragment());
        Assert.Equal(0, frame4.Rsv());
        Assert.Equal(aggregatedContent, ToBytes(frame4.Content));

        Assert.Null(channel.ReadInbound<object>());
    }

    [Fact]
    public void TextFrameTooBig()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new WebSocketFrameAggregator(8));
        channel.WriteInbound(new BinaryWebSocketFrame(true, 1, Unpooled.WrappedBuffer(content1)));
        channel.WriteInbound(new BinaryWebSocketFrame(false, 0, Unpooled.WrappedBuffer(content1)));
        Assert.ThrowsAny<TooLongFrameException>(() =>
                channel.WriteInbound(new ContinuationWebSocketFrame(false, 0, Unpooled.WrappedBuffer(content2))));
        channel.WriteInbound(new ContinuationWebSocketFrame(false, 0, Unpooled.WrappedBuffer(content2)));
        channel.WriteInbound(new ContinuationWebSocketFrame(true, 0, Unpooled.WrappedBuffer(content2)));

        channel.WriteInbound(new BinaryWebSocketFrame(true, 1, Unpooled.WrappedBuffer(content1)));
        channel.WriteInbound(new BinaryWebSocketFrame(false, 0, Unpooled.WrappedBuffer(content1)));
        Assert.ThrowsAny<TooLongFrameException>(() =>
                channel.WriteInbound(new ContinuationWebSocketFrame(false, 0, Unpooled.WrappedBuffer(content2))));
        channel.WriteInbound(new ContinuationWebSocketFrame(false, 0, Unpooled.WrappedBuffer(content2)));
        channel.WriteInbound(new ContinuationWebSocketFrame(true, 0, Unpooled.WrappedBuffer(content2)));

        for (;;)
        {
            object msg = channel.ReadInbound<object>();
            if (msg == null)
            {
                break;
            }

            ReferenceCountUtil.Release(msg);
        }

        channel.Finish();
    }

    private static byte[] ToBytes(ByteBuf buf)
    {
        byte[] bytes = new byte[buf.ReadableBytes];
        buf.ReadBytes(bytes);
        buf.Release();
        return bytes;
    }
}
