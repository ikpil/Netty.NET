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

using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Buffer.Unpooled;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Tests.Frame;

// CLR: CharsetUtil.ISO_8859_1 is Encoding.Latin1, and java.nio.ByteOrder is the Common port's ByteOrder. xUnit creates
// a new test class instance for every test, so the @BeforeEach setUp() runs from the constructor; it is internal
// instead of the source's public, because xUnit1013 reports public non-test methods on a test class.
public class LengthFieldPrependerTest
{

    private ByteBuf msg;

    public LengthFieldPrependerTest()
    {
        SetUp();
    }

    internal void SetUp()
    {
        msg = CopiedBuffer("A", Encoding.Latin1);
    }

    [Fact]
    public void TestPrependLength()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new LengthFieldPrepender(4));
        ch.WriteOutbound(msg);
        ByteBuf buf = ch.ReadOutbound<ByteBuf>();
        Assert.Equal(4, buf.ReadableBytes);
        Assert.Equal(msg.ReadableBytes, buf.ReadInt());
        buf.Release();

        buf = ch.ReadOutbound<ByteBuf>();
        Assert.Same(buf, msg);
        buf.Release();
    }

    [Fact]
    public void TestPrependLengthIncludesLengthFieldLength()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new LengthFieldPrepender(4, true));
        ch.WriteOutbound(msg);
        ByteBuf buf = ch.ReadOutbound<ByteBuf>();
        Assert.Equal(4, buf.ReadableBytes);
        Assert.Equal(5, buf.ReadInt());
        buf.Release();

        buf = ch.ReadOutbound<ByteBuf>();
        Assert.Same(buf, msg);
        buf.Release();
    }

    [Fact]
    public void TestPrependAdjustedLength()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new LengthFieldPrepender(4, -1));
        ch.WriteOutbound(msg);
        ByteBuf buf = ch.ReadOutbound<ByteBuf>();
        Assert.Equal(4, buf.ReadableBytes);
        Assert.Equal(msg.ReadableBytes - 1, buf.ReadInt());
        buf.Release();

        buf = ch.ReadOutbound<ByteBuf>();
        Assert.Same(buf, msg);
        buf.Release();
    }

    [Fact]
    public void TestAdjustedLengthLessThanZero()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new LengthFieldPrepender(4, -2));
        Assert.Throws<EncoderException>(() => ch.WriteOutbound(msg));
    }

    [Fact]
    public void TestPrependLengthInLittleEndian()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new LengthFieldPrepender(ByteOrder.LITTLE_ENDIAN, 4, 0, false));
        ch.WriteOutbound(msg);
        ByteBuf buf = ch.ReadOutbound<ByteBuf>();
        Assert.Equal(4, buf.ReadableBytes);
        byte[] writtenBytes = new byte[buf.ReadableBytes];
        buf.GetBytes(0, writtenBytes);
        Assert.Equal(1, writtenBytes[0]);
        Assert.Equal(0, writtenBytes[1]);
        Assert.Equal(0, writtenBytes[2]);
        Assert.Equal(0, writtenBytes[3]);
        buf.Release();

        buf = ch.ReadOutbound<ByteBuf>();
        Assert.Same(buf, msg);
        buf.Release();
        Assert.False(ch.Finish(), "The channel must have been completely read");
    }

}
