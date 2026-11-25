/*
 * Copyright 2024 The Netty Project
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
using Netty.NET.Handler.Codec.Spdy;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Http.Tests.Spdy;

// CLR: the anonymous SpdyFrameCodec subclass is the private nested UnknownFrameCodec; field initializers cannot read
// instance fields (CS0236, https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0236), so the channel is created in
// the constructor from the codec field. CharsetUtil.UTF_8 is Encoding.UTF8 (docs/codec-base-porting.md) and
// UnpooledByteBufAllocator.DEFAULT is UnpooledByteBufAllocator.Default. The Java byte (byte) 13 is the CLR byte 13.
public class SpdyFrameCodecTest
{
    private readonly SpdyFrameCodec codec = new UnknownFrameCodec();
    private readonly EmbeddedChannel channel;

    public SpdyFrameCodecTest()
    {
        channel = new EmbeddedChannel(
            codec
        );
    }

    private sealed class UnknownFrameCodec : SpdyFrameCodec
    {
        internal UnknownFrameCodec() : base(SpdyVersion.SPDY_3_1, 8192, 16384, 6, 15, 8, true, true)
        {
        }

        protected override bool IsValidUnknownFrameHeader(int streamId,
                                                          int type,
                                                          byte flags,
                                                          int length)
        {
            return true;
        }
    }

    [Fact]
    public void TestDecodeUnknownFrame()
    {
        SpdyFrameEncoder encoder = new SpdyFrameEncoder(SpdyVersion.SPDY_3_1);
        ByteBuf buf = encoder.EncodeUnknownFrame(
            UnpooledByteBufAllocator.Default,
            200,
            (byte) 13,
            Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes("Hello, world!")));
        channel.WriteInbound(buf);
        ISpdyUnknownFrame frame = channel.ReadInbound<ISpdyUnknownFrame>();
        Assert.NotNull(frame);
        Assert.Equal(200, frame.FrameType());
        Assert.Equal((byte) 13, frame.Flags());
        ByteBuf data = frame.Content;
        Assert.Equal("Hello, world!", data.ToString(Encoding.UTF8));
        data.Release();
    }

    [Fact]
    public void TestEncodeUnknownFrame()
    {
        ISpdyUnknownFrame spdyUnknownFrame = new DefaultSpdyUnknownFrame(
            200,
            (byte) 13,
            Unpooled.WrappedBuffer(Encoding.UTF8.GetBytes("Hello, world!")));
        channel.WriteOutbound(spdyUnknownFrame);
        ByteBuf buf = channel.ReadOutbound<ByteBuf>();
        Assert.NotNull(buf);
        channel.WriteInbound(buf);
        ISpdyUnknownFrame frame = channel.ReadInbound<ISpdyUnknownFrame>();
        Assert.NotNull(frame);
        Assert.Equal(200, frame.FrameType());
        Assert.Equal((byte) 13, frame.Flags());
        ByteBuf data = frame.Content;
        Assert.Equal("Hello, world!", data.ToString(Encoding.UTF8));
        data.Release();
    }

}
