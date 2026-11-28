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

using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec;

namespace Netty.NET.Handler.Codec.Tests;

// CLR: CharsetUtil.ISO_8859_1 is Encoding.Latin1. getUnsignedByte(int) is GetByte(int), which returns the unsigned
// octet, and readByte() returns the unsigned byte, so the int expectations compare as in the source.
public class ReplayingDecoderByteBufTest
{
    /**
     * See https://github.com/netty/netty/issues/445
     */
    [Fact]
    public void TestGetUnsignedByte()
    {
        ByteBuf buf = Unpooled.CopiedBuffer("TestBuffer", Encoding.Latin1);
        ReplayingDecoderByteBuf buffer = new ReplayingDecoderByteBuf(buf);

        bool error;
        int i = 0;

        try
        {
            for (;;)
            {
                buffer.GetByte(i);
                i++;
            }
        }
        catch (Signal)
        {
            error = true;
        }

        Assert.True(error);
        Assert.Equal(10, i);

        buf.Release();
    }

    /**
     * See https://github.com/netty/netty/issues/445
     */
    [Fact]
    public void TestGetByte()
    {
        ByteBuf buf = Unpooled.CopiedBuffer("TestBuffer", Encoding.Latin1);
        ReplayingDecoderByteBuf buffer = new ReplayingDecoderByteBuf(buf);

        bool error;
        int i = 0;

        try
        {
            for (;;)
            {
                buffer.GetByte(i);
                i++;
            }
        }
        catch (Signal)
        {
            error = true;
        }

        Assert.True(error);
        Assert.Equal(10, i);

        buf.Release();
    }

    /**
     * See https://github.com/netty/netty/issues/445
     */
    [Fact]
    public void TestGetBoolean()
    {
        ByteBuf buf = Unpooled.Buffer(10);

        while (buf.IsWritable())
        {
            buf.WriteBoolean(true);
        }

        ReplayingDecoderByteBuf buffer = new ReplayingDecoderByteBuf(buf);

        bool error;
        int i = 0;

        try
        {
            for (;;)
            {
                buffer.GetBoolean(i);
                i++;
            }
        }
        catch (Signal)
        {
            error = true;
        }

        Assert.True(error);
        Assert.Equal(10, i);

        buf.Release();
    }

    // See https://github.com/netty/netty/issues/13455
    [Fact]
    public void TestRetainedSlice()
    {
        ByteBuf buf = Unpooled.Buffer(10);
        int i = 0;

        while (buf.IsWritable())
        {
            buf.WriteByte(i++);
        }

        ReplayingDecoderByteBuf buffer = new ReplayingDecoderByteBuf(buf);
        ByteBuf slice = buffer.RetainedSlice(0, 4);
        Assert.Equal(2, slice.RefCnt);

        i = 0;

        while (slice.IsReadable())
        {
            Assert.Equal(i++, slice.ReadByte());
        }

        slice.Release();
        buf.Release();
        Assert.Equal(0, slice.RefCnt);
        Assert.Equal(0, buf.RefCnt);
        Assert.Equal(0, buffer.RefCnt);
    }
}
