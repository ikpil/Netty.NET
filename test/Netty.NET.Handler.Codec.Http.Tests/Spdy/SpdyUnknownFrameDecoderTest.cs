/*
 * Copyright 2015 The Netty Project
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
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Spdy;
using static Netty.NET.Handler.Codec.Spdy.SpdyCodecUtil;
using static Netty.NET.Handler.Codec.Http.Tests.Spdy.SpdyFrameDecoderTest;

namespace Netty.NET.Handler.Codec.Http.Tests.Spdy;

// CLR: Mockito is Moq, with the verify mapping of SpdyFrameDecoderTest (verify(delegate).x(...) is Verify(d =>
// d.X(...), Times.Once())); the mock is @delegate (a C# keyword). The anonymous SpdyFrameDecoder subclass of
// createDecoder is the private nested UnknownFrameDecoder. @BeforeEach is the constructor and @AfterEach is Dispose()
// (https://xunit.net/docs/shared-context). The unseeded java.util.Random is an unseeded System.Random and nextLong() is
// NextInt64() (only the eight payload bytes matter, rule 9). The Java byte flags (byte) 0xFF is the CLR byte 0xFF.
public class SpdyUnknownFrameDecoderTest : IDisposable
{

    private static readonly Random RANDOM = new Random();

    private readonly Mock<ISpdyFrameDecoderDelegate> @delegate = new Mock<ISpdyFrameDecoderDelegate>();
    private readonly TestSpdyFrameDecoderDelegate testDelegate;
    private SpdyFrameDecoder decoder;

    public SpdyUnknownFrameDecoderTest()
    {
        testDelegate = new TestSpdyFrameDecoderDelegate(@delegate.Object);
        CreateDecoder();
    }

    private void CreateDecoder()
    {
        decoder = new UnknownFrameDecoder(SpdyVersion.SPDY_3_1, testDelegate);
    }

    private sealed class UnknownFrameDecoder : SpdyFrameDecoder
    {
        internal UnknownFrameDecoder(SpdyVersion spdyVersion, ISpdyFrameDecoderDelegate @delegate)
            : base(spdyVersion, @delegate)
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

    public void Dispose()
    {
        ReleaseBuffers();
    }

    private void ReleaseBuffers()
    {
        testDelegate.ReleaseAll();
    }

    [Fact]
    public void TestDecodeUnknownFrame()
    {
        short type = 200;
        byte flags = (byte) 0xFF;
        int length = 8;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        long value = RANDOM.NextInt64();
        buf.WriteLong(value);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadUnknownFrame(type, flags, buf.Slice(SPDY_HEADER_SIZE, 8)), Times.Once());
        Assert.False(buf.IsReadable());
    }

}
