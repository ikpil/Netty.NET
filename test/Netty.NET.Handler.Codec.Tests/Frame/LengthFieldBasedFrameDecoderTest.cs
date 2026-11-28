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
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Tests.Frame;

public class LengthFieldBasedFrameDecoderTest
{
    [Fact]
    public void TestFailSlowTooLongFrameRecovery()
    {
        EmbeddedChannel ch = new EmbeddedChannel(
                new LengthFieldBasedFrameDecoder(5, 0, 4, 0, 4, false));

        for (int i = 0; i < 2; i ++)
        {
            Assert.False(ch.WriteInbound(Unpooled.WrappedBuffer(new byte[] { 0, 0, 0, 2 })));
            Assert.Throws<TooLongFrameException>(() =>
            {
                Assert.True(ch.WriteInbound(Unpooled.WrappedBuffer(new byte[] { 0, 0 })));
            });

            ch.WriteInbound(Unpooled.WrappedBuffer(new byte[] { 0, 0, 0, 1, (byte) 'A' }));
            ByteBuf buf = ch.ReadInbound<ByteBuf>();
            Assert.Equal("A", buf.ToString(Encoding.Latin1));
            buf.Release();
        }
    }

    [Fact]
    public void TestFailFastTooLongFrameRecovery()
    {
        EmbeddedChannel ch = new EmbeddedChannel(
                new LengthFieldBasedFrameDecoder(5, 0, 4, 0, 4));

        for (int i = 0; i < 2; i ++)
        {
            Assert.Throws<TooLongFrameException>(() =>
            {
                Assert.True(ch.WriteInbound(Unpooled.WrappedBuffer(new byte[] { 0, 0, 0, 2 })));
            });

            ch.WriteInbound(Unpooled.WrappedBuffer(new byte[] { 0, 0, 0, 0, 0, 1, (byte) 'A' }));
            ByteBuf buf = ch.ReadInbound<ByteBuf>();
            Assert.Equal("A", buf.ToString(Encoding.Latin1));
            buf.Release();
        }
    }
}
