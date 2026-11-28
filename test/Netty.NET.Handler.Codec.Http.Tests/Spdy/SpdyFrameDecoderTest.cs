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
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Spdy;
using static Netty.NET.Handler.Codec.Spdy.SpdyCodecUtil;
using static Netty.NET.Handler.Codec.Spdy.SpdyFrameDecoder;

namespace Netty.NET.Handler.Codec.Http.Tests.Spdy;

// CLR: Mockito is Moq (owner rule 9): mock(SpdyFrameDecoderDelegate.class) is a Mock<ISpdyFrameDecoderDelegate> named
// @delegate (delegate is a C# keyword) whose Object is passed to TestSpdyFrameDecoderDelegate. verify(delegate).x(...)
// checks exactly one matching call (Mockito's times(1)), so it is Verify(d => d.X(...), Times.Once()), the Times
// argument of Moq's Verify (https://github.com/devlooped/moq/wiki/Quickstart); verify(delegate, times(n)) is
// Times.Exactly(n), verifyNoInteractions(delegate) on a mock without verified calls is VerifyNoOtherCalls(), any() is
// It.IsAny<string>() and anyString(), which does not match null, is It.IsNotNull<string>(). Mockito compares the
// ByteBuf arguments with equals(Object) and Moq compares a value argument with Equals, both the readable-content
// equality of ByteBuf (docs/buffer-porting.md). @BeforeEach is the constructor and @AfterEach is Dispose(): xUnit
// "creates a new instance of the test class for every test that is run", and cleanup code goes in Dispose()
// (https://xunit.net/docs/shared-context). The unseeded java.util.Random is an unseeded System.Random (rule 9): only
// random 32-bit values matter, so nextInt() is the low 32 bits of NextInt64() (all int values, as Java's) and
// nextLong() is NextInt64(). The uint literal 0x80000000 is unchecked((int) 0x80000000), the Java int value; the Java
// byte flags are CLR bytes ((byte) 0xFF and friends keep the bit pattern).
public class SpdyFrameDecoderTest : IDisposable
{

    private static readonly Random RANDOM = new Random();

    private readonly Mock<ISpdyFrameDecoderDelegate> @delegate = new Mock<ISpdyFrameDecoderDelegate>();
    private readonly TestSpdyFrameDecoderDelegate testDelegate;
    private SpdyFrameDecoder decoder;

    public SpdyFrameDecoderTest()
    {
        testDelegate = new TestSpdyFrameDecoderDelegate(@delegate.Object);
        CreateDecoder();
    }

    private void CreateDecoder()
    {
        decoder = new SpdyFrameDecoder(SpdyVersion.SPDY_3_1, testDelegate);
    }

    public void Dispose()
    {
        ReleaseBuffers();
    }

    private void ReleaseBuffers()
    {
        testDelegate.ReleaseAll();
    }

    // CLR: RANDOM.nextInt(): all 32-bit values.
    private static int NextInt()
    {
        return unchecked((int) RANDOM.NextInt64());
    }

    // CLR: RANDOM.nextLong().
    private static long NextLong()
    {
        return RANDOM.NextInt64();
    }

    private static void EncodeDataFrameHeader(ByteBuf buffer, int streamId, byte flags, int length)
    {
        buffer.WriteInt(streamId & 0x7FFFFFFF);
        buffer.WriteByte(flags);
        buffer.WriteMedium(length);
    }

    internal static void EncodeControlFrameHeader(ByteBuf buffer, short type, byte flags, int length)
    {
        buffer.WriteShort(0x8000 | SpdyVersion.SPDY_3_1.Version());
        buffer.WriteShort(type);
        buffer.WriteByte(flags);
        buffer.WriteMedium(length);
    }

    [Fact]
    public void TestSpdyDataFrame()
    {
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;
        byte flags = 0;
        int length = 1024;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeDataFrameHeader(buf, streamId, flags, length);

        for (int i = 0; i < 256; i ++)
        {
            buf.WriteInt(NextInt());
        }

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadDataFrame(streamId, false, buf.Slice(SPDY_HEADER_SIZE, length)), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestEmptySpdyDataFrame()
    {
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;
        byte flags = 0;
        int length = 0;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeDataFrameHeader(buf, streamId, flags, length);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadDataFrame(streamId, false, Unpooled.EmptyBuffer), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestLastSpdyDataFrame()
    {
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;
        byte flags = 0x01; // FLAG_FIN
        int length = 0;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeDataFrameHeader(buf, streamId, flags, length);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadDataFrame(streamId, true, Unpooled.EmptyBuffer), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestUnknownSpdyDataFrameFlags()
    {
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;
        byte flags = (byte) 0xFE; // should ignore any unknown flags
        int length = 0;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeDataFrameHeader(buf, streamId, flags, length);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadDataFrame(streamId, false, Unpooled.EmptyBuffer), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestIllegalSpdyDataFrameStreamId()
    {
        int streamId = 0; // illegal stream identifier
        byte flags = 0;
        int length = 0;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeDataFrameHeader(buf, streamId, flags, length);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadFrameError(It.IsAny<string>()), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestPipelinedSpdyDataFrames()
    {
        int streamId1 = NextInt() & 0x7FFFFFFF | 0x01;
        int streamId2 = NextInt() & 0x7FFFFFFF | 0x01;
        byte flags = 0;
        int length = 0;

        ByteBuf buf = Unpooled.Buffer(2 * (SPDY_HEADER_SIZE + length));
        EncodeDataFrameHeader(buf, streamId1, flags, length);
        EncodeDataFrameHeader(buf, streamId2, flags, length);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadDataFrame(streamId1, false, Unpooled.EmptyBuffer), Times.Once());
        @delegate.Verify(d => d.ReadDataFrame(streamId2, false, Unpooled.EmptyBuffer), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestSpdySynStreamFrame()
    {
        short type = 1;
        byte flags = 0;
        int length = 10;
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;
        int associatedToStreamId = NextInt() & 0x7FFFFFFF;
        byte priority = (byte) (NextInt() & 0x07);

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);
        buf.WriteInt(associatedToStreamId);
        buf.WriteByte(priority << 5);
        buf.WriteByte(0);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadSynStreamFrame(streamId, associatedToStreamId, priority, false, false),
                Times.Once());
        @delegate.Verify(d => d.ReadHeaderBlockEnd(), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestLastSpdySynStreamFrame()
    {
        short type = 1;
        byte flags = 0x01; // FLAG_FIN
        int length = 10;
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;
        int associatedToStreamId = NextInt() & 0x7FFFFFFF;
        byte priority = (byte) (NextInt() & 0x07);

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);
        buf.WriteInt(associatedToStreamId);
        buf.WriteByte(priority << 5);
        buf.WriteByte(0);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadSynStreamFrame(streamId, associatedToStreamId, priority, true, false),
                Times.Once());
        @delegate.Verify(d => d.ReadHeaderBlockEnd(), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestUnidirectionalSpdySynStreamFrame()
    {
        short type = 1;
        byte flags = 0x02; // FLAG_UNIDIRECTIONAL
        int length = 10;
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;
        int associatedToStreamId = NextInt() & 0x7FFFFFFF;
        byte priority = (byte) (NextInt() & 0x07);

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);
        buf.WriteInt(associatedToStreamId);
        buf.WriteByte(priority << 5);
        buf.WriteByte(0);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadSynStreamFrame(streamId, associatedToStreamId, priority, false, true),
                Times.Once());
        @delegate.Verify(d => d.ReadHeaderBlockEnd(), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestIndependentSpdySynStreamFrame()
    {
        short type = 1;
        byte flags = 0;
        int length = 10;
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;
        int associatedToStreamId = 0; // independent of all other streams
        byte priority = (byte) (NextInt() & 0x07);

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);
        buf.WriteInt(associatedToStreamId);
        buf.WriteByte(priority << 5);
        buf.WriteByte(0);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadSynStreamFrame(streamId, associatedToStreamId, priority, false, false),
                Times.Once());
        @delegate.Verify(d => d.ReadHeaderBlockEnd(), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestUnknownSpdySynStreamFrameFlags()
    {
        short type = 1;
        byte flags = (byte) 0xFC; // undefined flags
        int length = 10;
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;
        int associatedToStreamId = NextInt() & 0x7FFFFFFF;
        byte priority = (byte) (NextInt() & 0x07);

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);
        buf.WriteInt(associatedToStreamId);
        buf.WriteByte(priority << 5);
        buf.WriteByte(0);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadSynStreamFrame(streamId, associatedToStreamId, priority, false, false),
                Times.Once());
        @delegate.Verify(d => d.ReadHeaderBlockEnd(), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestReservedSpdySynStreamFrameBits()
    {
        short type = 1;
        byte flags = 0;
        int length = 10;
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;
        int associatedToStreamId = NextInt() & 0x7FFFFFFF;
        byte priority = (byte) (NextInt() & 0x07);

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId | unchecked((int) 0x80000000)); // should ignore reserved bit
        buf.WriteInt(associatedToStreamId | unchecked((int) 0x80000000)); // should ignore reserved bit
        buf.WriteByte(priority << 5 | 0x1F); // should ignore reserved bits
        buf.WriteByte(0xFF); // should ignore reserved bits

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadSynStreamFrame(streamId, associatedToStreamId, priority, false, false),
                Times.Once());
        @delegate.Verify(d => d.ReadHeaderBlockEnd(), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestInvalidSpdySynStreamFrameLength()
    {
        short type = 1;
        byte flags = 0;
        int length = 8; // invalid length
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;
        int associatedToStreamId = NextInt() & 0x7FFFFFFF;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);
        buf.WriteInt(associatedToStreamId);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadFrameError(It.IsNotNull<string>()), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestIllegalSpdySynStreamFrameStreamId()
    {
        short type = 1;
        byte flags = 0;
        int length = 10;
        int streamId = 0; // invalid stream identifier
        int associatedToStreamId = NextInt() & 0x7FFFFFFF;
        byte priority = (byte) (NextInt() & 0x07);

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);
        buf.WriteInt(associatedToStreamId);
        buf.WriteByte(priority << 5);
        buf.WriteByte(0);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadFrameError(It.IsNotNull<string>()), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestSpdySynStreamFrameHeaderBlock()
    {
        short type = 1;
        byte flags = 0;
        int length = 10;
        int headerBlockLength = 1024;
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;
        int associatedToStreamId = NextInt() & 0x7FFFFFFF;
        byte priority = (byte) (NextInt() & 0x07);

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length + headerBlockLength);
        EncodeControlFrameHeader(buf, type, flags, length + headerBlockLength);
        buf.WriteInt(streamId);
        buf.WriteInt(associatedToStreamId);
        buf.WriteByte(priority << 5);
        buf.WriteByte(0);

        ByteBuf headerBlock = Unpooled.Buffer(headerBlockLength);

        for (int i = 0; i < 256; i ++)
        {
            headerBlock.WriteInt(NextInt());
        }

        decoder.Decode(buf);
        decoder.Decode(headerBlock);
        @delegate.Verify(d => d.ReadSynStreamFrame(streamId, associatedToStreamId, priority, false, false),
                Times.Once());
        @delegate.Verify(d => d.ReadHeaderBlock(headerBlock.Slice(0, headerBlock.WriterIndex)), Times.Once());
        @delegate.Verify(d => d.ReadHeaderBlockEnd(), Times.Once());
        Assert.False(buf.IsReadable());
        Assert.False(headerBlock.IsReadable());
        buf.Release();
        headerBlock.Release();
    }

    [Fact]
    public void TestSpdySynReplyFrame()
    {
        short type = 2;
        byte flags = 0;
        int length = 4;
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadSynReplyFrame(streamId, false), Times.Once());
        @delegate.Verify(d => d.ReadHeaderBlockEnd(), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestLastSpdySynReplyFrame()
    {
        short type = 2;
        byte flags = 0x01; // FLAG_FIN
        int length = 4;
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadSynReplyFrame(streamId, true), Times.Once());
        @delegate.Verify(d => d.ReadHeaderBlockEnd(), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestUnknownSpdySynReplyFrameFlags()
    {
        short type = 2;
        byte flags = (byte) 0xFE; // undefined flags
        int length = 4;
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadSynReplyFrame(streamId, false), Times.Once());
        @delegate.Verify(d => d.ReadHeaderBlockEnd(), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestReservedSpdySynReplyFrameBits()
    {
        short type = 2;
        byte flags = 0;
        int length = 4;
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId | unchecked((int) 0x80000000)); // should ignore reserved bit

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadSynReplyFrame(streamId, false), Times.Once());
        @delegate.Verify(d => d.ReadHeaderBlockEnd(), Times.Once());

        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestInvalidSpdySynReplyFrameLength()
    {
        short type = 2;
        byte flags = 0;
        int length = 0; // invalid length

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadFrameError(It.IsNotNull<string>()), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestIllegalSpdySynReplyFrameStreamId()
    {
        short type = 2;
        byte flags = 0;
        int length = 4;
        int streamId = 0; // invalid stream identifier

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadFrameError(It.IsNotNull<string>()), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestSpdySynReplyFrameHeaderBlock()
    {
        short type = 2;
        byte flags = 0;
        int length = 4;
        int headerBlockLength = 1024;
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length + headerBlockLength);
        EncodeControlFrameHeader(buf, type, flags, length + headerBlockLength);
        buf.WriteInt(streamId);

        ByteBuf headerBlock = Unpooled.Buffer(headerBlockLength);

        for (int i = 0; i < 256; i ++)
        {
            headerBlock.WriteInt(NextInt());
        }

        decoder.Decode(buf);
        decoder.Decode(headerBlock);
        @delegate.Verify(d => d.ReadSynReplyFrame(streamId, false), Times.Once());
        @delegate.Verify(d => d.ReadHeaderBlock(headerBlock.Slice(0, headerBlock.WriterIndex)), Times.Once());
        @delegate.Verify(d => d.ReadHeaderBlockEnd(), Times.Once());
        Assert.False(buf.IsReadable());
        Assert.False(headerBlock.IsReadable());
        buf.Release();
        headerBlock.Release();
    }

    [Fact]
    public void TestSpdyRstStreamFrame()
    {
        short type = 3;
        byte flags = 0;
        int length = 8;
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;
        int statusCode = NextInt() | 0x01;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);
        buf.WriteInt(statusCode);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadRstStreamFrame(streamId, statusCode), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestReservedSpdyRstStreamFrameBits()
    {
        short type = 3;
        byte flags = 0;
        int length = 8;
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;
        int statusCode = NextInt() | 0x01;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId | unchecked((int) 0x80000000)); // should ignore reserved bit
        buf.WriteInt(statusCode);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadRstStreamFrame(streamId, statusCode), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestInvalidSpdyRstStreamFrameFlags()
    {
        short type = 3;
        byte flags = (byte) 0xFF; // invalid flags
        int length = 8;
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;
        int statusCode = NextInt() | 0x01;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);
        buf.WriteInt(statusCode);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadFrameError(It.IsNotNull<string>()), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestInvalidSpdyRstStreamFrameLength()
    {
        short type = 3;
        byte flags = 0;
        int length = 12; // invalid length
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;
        int statusCode = NextInt() | 0x01;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);
        buf.WriteInt(statusCode);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadFrameError(It.IsNotNull<string>()), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestIllegalSpdyRstStreamFrameStreamId()
    {
        short type = 3;
        byte flags = 0;
        int length = 8;
        int streamId = 0; // invalid stream identifier
        int statusCode = NextInt() | 0x01;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);
        buf.WriteInt(statusCode);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadFrameError(It.IsNotNull<string>()), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestIllegalSpdyRstStreamFrameStatusCode()
    {
        short type = 3;
        byte flags = 0;
        int length = 8;
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;
        int statusCode = 0; // invalid status code

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);
        buf.WriteInt(statusCode);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadFrameError(It.IsNotNull<string>()), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestSpdySettingsFrame()
    {
        short type = 4;
        byte flags = 0;
        int numSettings = 2;
        int length = 8 * numSettings + 4;
        byte idFlags = 0;
        int id = NextInt() & 0x00FFFFFF;
        int value = NextInt();

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(numSettings);

        for (int i = 0; i < numSettings; i++)
        {
            buf.WriteByte(idFlags);
            buf.WriteMedium(id);
            buf.WriteInt(value);
        }

        @delegate.Object.ReadSettingsEnd();
        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadSettingsFrame(false), Times.Once());
        @delegate.Verify(d => d.ReadSetting(id, value, false, false), Times.Exactly(numSettings));
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestEmptySpdySettingsFrame()
    {
        short type = 4;
        byte flags = 0;
        int numSettings = 0;
        int length = 8 * numSettings + 4;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(numSettings);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadSettingsFrame(false), Times.Once());
        @delegate.Verify(d => d.ReadSettingsEnd(), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestSpdySettingsFrameClearFlag()
    {
        short type = 4;
        byte flags = 0x01; // FLAG_SETTINGS_CLEAR_SETTINGS
        int numSettings = 0;
        int length = 8 * numSettings + 4;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(numSettings);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadSettingsFrame(true), Times.Once());
        @delegate.Verify(d => d.ReadSettingsEnd(), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestSpdySettingsPersistValues()
    {
        short type = 4;
        byte flags = 0;
        int numSettings = 1;
        int length = 8 * numSettings + 4;
        byte idFlags = 0x01; // FLAG_SETTINGS_PERSIST_VALUE
        int id = NextInt() & 0x00FFFFFF;
        int value = NextInt();

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(numSettings);

        for (int i = 0; i < numSettings; i++)
        {
            buf.WriteByte(idFlags);
            buf.WriteMedium(id);
            buf.WriteInt(value);
        }

        @delegate.Object.ReadSettingsEnd();
        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadSettingsFrame(false), Times.Once());
        @delegate.Verify(d => d.ReadSetting(id, value, true, false), Times.Exactly(numSettings));
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestSpdySettingsPersistedValues()
    {
        short type = 4;
        byte flags = 0;
        int numSettings = 1;
        int length = 8 * numSettings + 4;
        byte idFlags = 0x02; // FLAG_SETTINGS_PERSISTED
        int id = NextInt() & 0x00FFFFFF;
        int value = NextInt();

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(numSettings);

        for (int i = 0; i < numSettings; i++)
        {
            buf.WriteByte(idFlags);
            buf.WriteMedium(id);
            buf.WriteInt(value);
        }

        @delegate.Object.ReadSettingsEnd();
        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadSettingsFrame(false), Times.Once());
        @delegate.Verify(d => d.ReadSetting(id, value, false, true), Times.Exactly(numSettings));
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestUnknownSpdySettingsFrameFlags()
    {
        short type = 4;
        byte flags = (byte) 0xFE; // undefined flags
        int numSettings = 0;
        int length = 8 * numSettings + 4;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(numSettings);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadSettingsFrame(false), Times.Once());
        @delegate.Verify(d => d.ReadSettingsEnd(), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestUnknownSpdySettingsFlags()
    {
        short type = 4;
        byte flags = 0;
        int numSettings = 1;
        int length = 8 * numSettings + 4;
        byte idFlags = (byte) 0xFC; // undefined flags
        int id = NextInt() & 0x00FFFFFF;
        int value = NextInt();

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(numSettings);

        for (int i = 0; i < numSettings; i++)
        {
            buf.WriteByte(idFlags);
            buf.WriteMedium(id);
            buf.WriteInt(value);
        }

        @delegate.Object.ReadSettingsEnd();
        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadSettingsFrame(false), Times.Once());
        @delegate.Verify(d => d.ReadSetting(id, value, false, false), Times.Exactly(numSettings));
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestInvalidSpdySettingsFrameLength()
    {
        short type = 4;
        byte flags = 0;
        int numSettings = 2;
        int length = 8 * numSettings + 8; // invalid length
        byte idFlags = 0;
        int id = NextInt() & 0x00FFFFFF;
        int value = NextInt();

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(numSettings);

        for (int i = 0; i < numSettings; i++)
        {
            buf.WriteByte(idFlags);
            buf.WriteMedium(id);
            buf.WriteInt(value);
        }

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadFrameError(It.IsNotNull<string>()), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestInvalidSpdySettingsFrameNumSettings()
    {
        short type = 4;
        byte flags = 0;
        int numSettings = 2;
        int length = 8 * numSettings + 4;
        byte idFlags = 0;
        int id = NextInt() & 0x00FFFFFF;
        int value = NextInt();

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(0); // invalid num_settings

        for (int i = 0; i < numSettings; i++)
        {
            buf.WriteByte(idFlags);
            buf.WriteMedium(id);
            buf.WriteInt(value);
        }

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadFrameError(It.IsNotNull<string>()), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestSpdySettingsFrameContainTooManySettings()
    {
        short type = 4;
        byte flags = 0;
        int numSettings = DEFAULT_MAX_NUM_SETTINGS * 2;
        int length = 8 * numSettings + 4;
        byte idFlags = 0;
        int id = NextInt() & 0x00FFFFFF;
        int value = NextInt();

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(numSettings);

        for (int i = 0; i < numSettings; i++)
        {
            buf.WriteByte(idFlags);
            buf.WriteMedium(id);
            buf.WriteInt(value);
        }

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadFrameError(It.IsNotNull<string>()), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestDiscardUnknownFrame()
    {
        short type = 5;
        byte flags = (byte) 0xFF;
        int length = 8;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteLong(NextLong());

        decoder.Decode(buf);
        @delegate.VerifyNoOtherCalls();
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestDiscardUnknownEmptyFrame()
    {
        short type = 5;
        byte flags = (byte) 0xFF;
        int length = 0;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);

        decoder.Decode(buf);
        @delegate.VerifyNoOtherCalls();
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestProgressivelyDiscardUnknownEmptyFrame()
    {
        short type = 5;
        byte flags = (byte) 0xFF;
        int segment = 4;
        int length = 2 * segment;

        ByteBuf header = Unpooled.Buffer(SPDY_HEADER_SIZE);
        ByteBuf segment1 = Unpooled.Buffer(segment);
        ByteBuf segment2 = Unpooled.Buffer(segment);
        EncodeControlFrameHeader(header, type, flags, length);
        segment1.WriteInt(NextInt());
        segment2.WriteInt(NextInt());

        decoder.Decode(header);
        decoder.Decode(segment1);
        decoder.Decode(segment2);
        @delegate.VerifyNoOtherCalls();
        Assert.False(header.IsReadable());
        Assert.False(segment1.IsReadable());
        Assert.False(segment2.IsReadable());
        header.Release();
        segment1.Release();
        segment2.Release();
    }

    [Fact]
    public void TestSpdyPingFrame()
    {
        short type = 6;
        byte flags = 0;
        int length = 4;
        int id = NextInt();

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(id);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadPingFrame(id), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestUnknownSpdyPingFrameFlags()
    {
        short type = 6;
        byte flags = (byte) 0xFF; // undefined flags
        int length = 4;
        int id = NextInt();

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(id);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadPingFrame(id), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestInvalidSpdyPingFrameLength()
    {
        short type = 6;
        byte flags = 0;
        int length = 8; // invalid length
        int id = NextInt();

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(id);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadFrameError(It.IsNotNull<string>()), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestSpdyGoAwayFrame()
    {
        short type = 7;
        byte flags = 0;
        int length = 8;
        int lastGoodStreamId = NextInt() & 0x7FFFFFFF;
        int statusCode = NextInt() | 0x01;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(lastGoodStreamId);
        buf.WriteInt(statusCode);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadGoAwayFrame(lastGoodStreamId, statusCode), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestUnknownSpdyGoAwayFrameFlags()
    {
        short type = 7;
        byte flags = (byte) 0xFF; // undefined flags
        int length = 8;
        int lastGoodStreamId = NextInt() & 0x7FFFFFFF;
        int statusCode = NextInt() | 0x01;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(lastGoodStreamId);
        buf.WriteInt(statusCode);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadGoAwayFrame(lastGoodStreamId, statusCode), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestReservedSpdyGoAwayFrameBits()
    {
        short type = 7;
        byte flags = 0;
        int length = 8;
        int lastGoodStreamId = NextInt() & 0x7FFFFFFF;
        int statusCode = NextInt() | 0x01;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(lastGoodStreamId | unchecked((int) 0x80000000)); // should ignore reserved bit
        buf.WriteInt(statusCode);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadGoAwayFrame(lastGoodStreamId, statusCode), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestInvalidSpdyGoAwayFrameLength()
    {
        short type = 7;
        byte flags = 0;
        int length = 12; // invalid length
        int lastGoodStreamId = NextInt() & 0x7FFFFFFF;
        int statusCode = NextInt() | 0x01;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(lastGoodStreamId);
        buf.WriteInt(statusCode);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadFrameError(It.IsNotNull<string>()), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestSpdyHeadersFrame()
    {
        short type = 8;
        byte flags = 0;
        int length = 4;
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadHeadersFrame(streamId, false), Times.Once());
        @delegate.Verify(d => d.ReadHeaderBlockEnd(), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestLastSpdyHeadersFrame()
    {
        short type = 8;
        byte flags = 0x01; // FLAG_FIN
        int length = 4;
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadHeadersFrame(streamId, true), Times.Once());
        @delegate.Verify(d => d.ReadHeaderBlockEnd(), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestUnknownSpdyHeadersFrameFlags()
    {
        short type = 8;
        byte flags = (byte) 0xFE; // undefined flags
        int length = 4;
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadHeadersFrame(streamId, false), Times.Once());
        @delegate.Verify(d => d.ReadHeaderBlockEnd(), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestReservedSpdyHeadersFrameBits()
    {
        short type = 8;
        byte flags = 0;
        int length = 4;
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId | unchecked((int) 0x80000000)); // should ignore reserved bit

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadHeadersFrame(streamId, false), Times.Once());
        @delegate.Verify(d => d.ReadHeaderBlockEnd(), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestInvalidSpdyHeadersFrameLength()
    {
        short type = 8;
        byte flags = 0;
        int length = 0; // invalid length

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadFrameError(It.IsNotNull<string>()), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestInvalidSpdyHeadersFrameStreamId()
    {
        short type = 8;
        byte flags = 0;
        int length = 4;
        int streamId = 0; // invalid stream identifier

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadFrameError(It.IsNotNull<string>()), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestSpdyHeadersFrameHeaderBlock()
    {
        short type = 8;
        byte flags = 0;
        int length = 4;
        int headerBlockLength = 1024;
        int streamId = NextInt() & 0x7FFFFFFF | 0x01;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length + headerBlockLength);
        buf.WriteInt(streamId);

        ByteBuf headerBlock = Unpooled.Buffer(headerBlockLength);

        for (int i = 0; i < 256; i ++)
        {
            headerBlock.WriteInt(NextInt());
        }

        decoder.Decode(buf);
        decoder.Decode(headerBlock);
        @delegate.Verify(d => d.ReadHeadersFrame(streamId, false), Times.Once());
        @delegate.Verify(d => d.ReadHeaderBlock(headerBlock.Slice(0, headerBlock.WriterIndex)), Times.Once());
        @delegate.Verify(d => d.ReadHeaderBlockEnd(), Times.Once());
        Assert.False(buf.IsReadable());
        Assert.False(headerBlock.IsReadable());
        buf.Release();
        headerBlock.Release();
    }

    [Fact]
    public void TestSpdyWindowUpdateFrame()
    {
        short type = 9;
        byte flags = 0;
        int length = 8;
        int streamId = NextInt() & 0x7FFFFFFF;
        int deltaWindowSize = NextInt() & 0x7FFFFFFF | 0x01;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);
        buf.WriteInt(deltaWindowSize);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadWindowUpdateFrame(streamId, deltaWindowSize), Times.Once());
        Assert.False(buf.IsReadable());
    }

    [Fact]
    public void TestUnknownSpdyWindowUpdateFrameFlags()
    {
        short type = 9;
        byte flags = (byte) 0xFF; // undefined flags
        int length = 8;
        int streamId = NextInt() & 0x7FFFFFFF;
        int deltaWindowSize = NextInt() & 0x7FFFFFFF | 0x01;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);
        buf.WriteInt(deltaWindowSize);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadWindowUpdateFrame(streamId, deltaWindowSize), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestReservedSpdyWindowUpdateFrameBits()
    {
        short type = 9;
        byte flags = 0;
        int length = 8;
        int streamId = NextInt() & 0x7FFFFFFF;
        int deltaWindowSize = NextInt() & 0x7FFFFFFF | 0x01;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId | unchecked((int) 0x80000000)); // should ignore reserved bit
        buf.WriteInt(deltaWindowSize | unchecked((int) 0x80000000)); // should ignore reserved bit

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadWindowUpdateFrame(streamId, deltaWindowSize), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestInvalidSpdyWindowUpdateFrameLength()
    {
        short type = 9;
        byte flags = 0;
        int length = 12; // invalid length
        int streamId = NextInt() & 0x7FFFFFFF;
        int deltaWindowSize = NextInt() & 0x7FFFFFFF | 0x01;

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);
        buf.WriteInt(deltaWindowSize);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadFrameError(It.IsNotNull<string>()), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }

    [Fact]
    public void TestIllegalSpdyWindowUpdateFrameDeltaWindowSize()
    {
        short type = 9;
        byte flags = 0;
        int length = 8;
        int streamId = NextInt() & 0x7FFFFFFF;
        int deltaWindowSize = 0; // invalid delta window size

        ByteBuf buf = Unpooled.Buffer(SPDY_HEADER_SIZE + length);
        EncodeControlFrameHeader(buf, type, flags, length);
        buf.WriteInt(streamId);
        buf.WriteInt(deltaWindowSize);

        decoder.Decode(buf);
        @delegate.Verify(d => d.ReadFrameError(It.IsNotNull<string>()), Times.Once());
        Assert.False(buf.IsReadable());
        buf.Release();
    }
}
