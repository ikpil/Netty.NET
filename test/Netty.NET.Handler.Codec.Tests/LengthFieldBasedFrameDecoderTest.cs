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

using Netty.NET.Buffer;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Tests;

// CLR: assertEquals('a', b.readByte()) compares the char and the byte as ints, as Java promotes them.
public class LengthFieldBasedFrameDecoderTest
{
    [Fact]
    public void TestDiscardTooLongFrame1()
    {
        ByteBuf buf = Unpooled.Buffer();
        buf.WriteInt(32);
        for (int i = 0; i < 32; i++)
        {
            buf.WriteByte(i);
        }
        buf.WriteInt(1);
        buf.WriteByte('a');
        EmbeddedChannel channel = new EmbeddedChannel(new LengthFieldBasedFrameDecoder(16, 0, 4));
        Assert.Throws<TooLongFrameException>(() => channel.WriteInbound(buf));
        Assert.True(channel.Finish());

        ByteBuf b = channel.ReadInbound<ByteBuf>();
        Assert.Equal(5, b.ReadableBytes);
        Assert.Equal(1, b.ReadInt());
        Assert.Equal<int>('a', b.ReadByte());
        b.Release();

        Assert.Null(channel.ReadInbound());
        channel.Finish();
    }

    [Fact]
    public void TestDiscardTooLongFrame2()
    {
        ByteBuf buf = Unpooled.Buffer();
        buf.WriteInt(32);
        for (int i = 0; i < 32; i++)
        {
            buf.WriteByte(i);
        }
        buf.WriteInt(1);
        buf.WriteByte('a');
        EmbeddedChannel channel = new EmbeddedChannel(new LengthFieldBasedFrameDecoder(16, 0, 4));
        Assert.Throws<TooLongFrameException>(() => channel.WriteInbound(buf.ReadRetainedSlice(14)));
        Assert.True(channel.WriteInbound(buf.ReadRetainedSlice(buf.ReadableBytes)));

        Assert.True(channel.Finish());

        ByteBuf b = channel.ReadInbound<ByteBuf>();
        Assert.Equal(5, b.ReadableBytes);
        Assert.Equal(1, b.ReadInt());
        Assert.Equal<int>('a', b.ReadByte());
        b.Release();

        Assert.Null(channel.ReadInbound());
        channel.Finish();

        buf.Release();
    }

    [Fact]
    public void TestDecodeNextFrameAfterFrameLengthLessThanInitialBytesToStrip()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new LengthFieldBasedFrameDecoder(16, 0, 4, 0, 6));

        // The adjusted frame length (4) is less than initialBytesToStrip (6).
        Assert.Throws<CorruptedFrameException>(() => channel.WriteInbound(Unpooled.Buffer().WriteInt(0)));

        // The next frame must be decoded again.
        ByteBuf buf = Unpooled.Buffer().WriteInt(4).WriteShort(1).WriteShort(2);
        Assert.True(channel.WriteInbound(buf));
        ByteBuf b = channel.ReadInbound<ByteBuf>();
        Assert.Equal(2, b.ReadableBytes);
        Assert.Equal(2, b.ReadShort());
        b.Release();

        Assert.Null(channel.ReadInbound());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestDecodeNextFrameAfterFrameLengthLessThanInitialBytesToStripInSeparateReads()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new LengthFieldBasedFrameDecoder(16, 0, 4, 0, 10));

        // The length field arrives first, and the rest of the malformed frame (adjusted length 8 is less than
        // initialBytesToStrip 10) arrives in a later read.
        Assert.False(channel.WriteInbound(Unpooled.Buffer().WriteInt(4)));
        Assert.Throws<CorruptedFrameException>(() => channel.WriteInbound(Unpooled.Buffer().WriteInt(0)));

        ByteBuf buf = Unpooled.Buffer().WriteInt(8).WriteInt(1).WriteShort(2).WriteShort(3);
        Assert.True(channel.WriteInbound(buf));
        ByteBuf b = channel.ReadInbound<ByteBuf>();
        Assert.Equal(2, b.ReadableBytes);
        Assert.Equal(3, b.ReadShort());
        b.Release();

        Assert.Null(channel.ReadInbound());
        Assert.False(channel.Finish());
    }
}
