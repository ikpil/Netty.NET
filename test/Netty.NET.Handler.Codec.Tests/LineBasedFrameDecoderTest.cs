/*
 * Copyright 2012 The Netty Project
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
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Buffer.Unpooled;

namespace Netty.NET.Handler.Codec.Tests;

// CLR: The char literals of the byte[] initializers are cast to byte, which Java narrows implicitly.
public class LineBasedFrameDecoderTest
{
    [Fact]
    public void TestDecodeWithStrip()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new LineBasedFrameDecoder(8192, true, false));

        ch.WriteInbound(CopiedBuffer("first\r\nsecond\nthird", Encoding.ASCII));

        ByteBuf buf = ch.ReadInbound<ByteBuf>();
        Assert.Equal("first", buf.ToString(Encoding.ASCII));

        ByteBuf buf2 = ch.ReadInbound<ByteBuf>();
        Assert.Equal("second", buf2.ToString(Encoding.ASCII));
        Assert.Null(ch.ReadInbound());
        ch.Finish();

        ReferenceCountUtil.Release(ch.ReadInbound());

        buf.Release();
        buf2.Release();
    }

    [Fact]
    public void TestDecodeWithoutStrip()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new LineBasedFrameDecoder(8192, false, false));

        ch.WriteInbound(CopiedBuffer("first\r\nsecond\nthird", Encoding.ASCII));

        ByteBuf buf = ch.ReadInbound<ByteBuf>();
        Assert.Equal("first\r\n", buf.ToString(Encoding.ASCII));

        ByteBuf buf2 = ch.ReadInbound<ByteBuf>();
        Assert.Equal("second\n", buf2.ToString(Encoding.ASCII));
        Assert.Null(ch.ReadInbound());
        ch.Finish();
        ReferenceCountUtil.Release(ch.ReadInbound());

        buf.Release();
        buf2.Release();
    }

    [Fact]
    public void TestTooLongLine1()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new LineBasedFrameDecoder(16, false, false));

        Exception e = Assert.ThrowsAny<Exception>(() =>
                ch.WriteInbound(CopiedBuffer("12345678901234567890\r\nfirst\nsecond", Encoding.ASCII)));
        Assert.IsAssignableFrom<TooLongFrameException>(e);

        ByteBuf buf = ch.ReadInbound<ByteBuf>();
        ByteBuf buf2 = CopiedBuffer("first\n", Encoding.ASCII);
        Assert.Equal(buf2, buf);
        Assert.False(ch.Finish());

        buf.Release();
        buf2.Release();
    }

    [Fact]
    public void TestTooLongLine2()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new LineBasedFrameDecoder(16, false, false));

        Assert.False(ch.WriteInbound(CopiedBuffer("12345678901234567", Encoding.ASCII)));
        Exception e = Assert.ThrowsAny<Exception>(() =>
                ch.WriteInbound(CopiedBuffer("890\r\nfirst\r\n", Encoding.ASCII)));
        Assert.IsAssignableFrom<TooLongFrameException>(e);

        ByteBuf buf = ch.ReadInbound<ByteBuf>();
        ByteBuf buf2 = CopiedBuffer("first\r\n", Encoding.ASCII);
        Assert.Equal(buf2, buf);
        Assert.False(ch.Finish());

        buf.Release();
        buf2.Release();
    }

    [Fact]
    public void TestTooLongLineWithFailFast()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new LineBasedFrameDecoder(16, false, true));

        Exception e = Assert.ThrowsAny<Exception>(() =>
                ch.WriteInbound(CopiedBuffer("12345678901234567", Encoding.ASCII)));
        Assert.IsAssignableFrom<TooLongFrameException>(e);

        Assert.False(ch.WriteInbound(CopiedBuffer("890", Encoding.ASCII)));
        Assert.True(ch.WriteInbound(CopiedBuffer("123\r\nfirst\r\n", Encoding.ASCII)));

        ByteBuf buf = ch.ReadInbound<ByteBuf>();
        ByteBuf buf2 = CopiedBuffer("first\r\n", Encoding.ASCII);
        Assert.Equal(buf2, buf);
        Assert.False(ch.Finish());

        buf.Release();
        buf2.Release();
    }

    [Fact]
    public void TestDecodeSplitsCorrectly()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new LineBasedFrameDecoder(8192, false, false));

        Assert.True(ch.WriteInbound(CopiedBuffer("line\r\n.\r\n", Encoding.ASCII)));

        ByteBuf buf = ch.ReadInbound<ByteBuf>();
        Assert.Equal("line\r\n", buf.ToString(Encoding.ASCII));

        ByteBuf buf2 = ch.ReadInbound<ByteBuf>();
        Assert.Equal(".\r\n", buf2.ToString(Encoding.ASCII));
        Assert.False(ch.FinishAndReleaseAll());

        buf.Release();
        buf2.Release();
    }

    [Fact]
    public void TestFragmentedDecode()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new LineBasedFrameDecoder(8192, false, false));

        Assert.False(ch.WriteInbound(CopiedBuffer("huu", Encoding.ASCII)));
        Assert.Null(ch.ReadInbound());

        Assert.False(ch.WriteInbound(CopiedBuffer("haa\r", Encoding.ASCII)));
        Assert.Null(ch.ReadInbound());

        Assert.True(ch.WriteInbound(CopiedBuffer("\nhuuhaa\r\n", Encoding.ASCII)));
        ByteBuf buf = ch.ReadInbound<ByteBuf>();
        Assert.Equal("huuhaa\r\n", buf.ToString(Encoding.ASCII));

        ByteBuf buf2 = ch.ReadInbound<ByteBuf>();
        Assert.Equal("huuhaa\r\n", buf2.ToString(Encoding.ASCII));
        Assert.False(ch.FinishAndReleaseAll());

        buf.Release();
        buf2.Release();
    }

    [Fact]
    public void TestEmptyLine()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new LineBasedFrameDecoder(8192, true, false));

        Assert.True(ch.WriteInbound(CopiedBuffer("\nabcna\r\n", Encoding.ASCII)));

        ByteBuf buf = ch.ReadInbound<ByteBuf>();
        Assert.Equal("", buf.ToString(Encoding.ASCII));

        ByteBuf buf2 = ch.ReadInbound<ByteBuf>();
        Assert.Equal("abcna", buf2.ToString(Encoding.ASCII));

        Assert.False(ch.FinishAndReleaseAll());

        buf.Release();
        buf2.Release();
    }

    [Fact]
    public void TestNotFailFast()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new LineBasedFrameDecoder(2, false, false));
        Assert.False(ch.WriteInbound(WrappedBuffer(new byte[] { 0, 1, 2 })));
        Assert.False(ch.WriteInbound(WrappedBuffer(new byte[]{ 3, 4 })));
        Assert.Throws<TooLongFrameException>(() =>
        {
            ch.WriteInbound(WrappedBuffer(new byte[] { (byte) '\n' }));
        });
        Assert.False(ch.WriteInbound(WrappedBuffer(new byte[] { (byte) '5' })));
        Assert.True(ch.WriteInbound(WrappedBuffer(new byte[] { (byte) '\n' })));

        ByteBuf expected = WrappedBuffer(new byte[] { (byte) '5', (byte) '\n' });
        ByteBuf buffer = ch.ReadInbound<ByteBuf>();
        Assert.Equal(expected, buffer);
        expected.Release();
        buffer.Release();

        Assert.False(ch.Finish());
    }
}
