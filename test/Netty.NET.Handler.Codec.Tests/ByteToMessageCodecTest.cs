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
using System.Collections.Generic;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Tests;

public class ByteToMessageCodecTest
{
    [Fact]
    public void TestSharable()
    {
        // CLR: IllegalStateException is InvalidOperationException.
        Assert.Throws<InvalidOperationException>(() => new InvalidByteToMessageCodec());
    }

    [Fact]
    public void TestSharable2()
    {
        Assert.Throws<InvalidOperationException>(() => new InvalidByteToMessageCodec2());
    }

    [Fact]
    public void TestForwardPendingData()
    {
        ByteToMessageCodec<int> codec = new IntegerCodec();

        ByteBuf buffer = Unpooled.Buffer();
        buffer.WriteInt(1);
        buffer.WriteByte('0');

        EmbeddedChannel ch = new EmbeddedChannel(codec);
        Assert.True(ch.WriteInbound(buffer));
        ch.Pipeline.Remove(codec);
        Assert.True(ch.Finish());
        Assert.Equal(1, ch.ReadInbound<int>());

        ByteBuf buf = ch.ReadInbound<ByteBuf>();
        Assert.Equal(Unpooled.WrappedBuffer(new byte[] { (byte) '0' }), buf);
        buf.Release();
        Assert.Null(ch.ReadInbound());
        Assert.Null(ch.ReadOutbound());
    }

    // CLR: Integer is int.
    private sealed class IntegerCodec : ByteToMessageCodec<int>
    {
        protected override void Encode(IChannelHandlerContext ctx, int msg, ByteBuf @out)
        {
            @out.WriteInt(msg);
        }

        protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
        {
            if (@in.ReadableBytes >= 4)
            {
                @out.Add(@in.ReadInt());
            }
        }
    }

    [Sharable]
    private sealed class InvalidByteToMessageCodec : ByteToMessageCodec<int>
    {
        internal InvalidByteToMessageCodec() : base(true)
        {
        }

        protected override void Encode(IChannelHandlerContext ctx, int msg, ByteBuf @out)
        {
        }

        protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
        {
        }
    }

    [Sharable]
    private sealed class InvalidByteToMessageCodec2 : ByteToMessageCodec<int>
    {
        internal InvalidByteToMessageCodec2() : base(typeof(int), true)
        {
        }

        protected override void Encode(IChannelHandlerContext ctx, int msg, ByteBuf @out)
        {
        }

        protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
        {
        }
    }
}
