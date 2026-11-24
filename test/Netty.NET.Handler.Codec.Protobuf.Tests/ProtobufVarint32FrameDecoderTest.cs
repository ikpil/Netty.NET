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

using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Buffer.Unpooled;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Protobuf.Tests;

// CLR: readInbound() is ReadInbound<T>(), the JUnit assertions with a message use XunitAssertMessages.AssertM, and the
// final local remaining is a plain local. buffer() is Unpooled.Buffer(), qualified because inside Netty.NET.* the
// simple name Buffer binds to the namespace Netty.NET.Buffer (CS0118, checked with a scratch build). The Java byte
// literal -2 is unchecked((byte) -2), the same eight bits 0xFE: "Constant expressions are evaluated by default in a
// checked context" (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/checked-and-unchecked),
// and the unchecked conversion keeps the low bits
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/numeric-conversions); (byte) 0x80
// is the same bit pattern in both languages. Numbers in messages use the invariant culture, as the decoder's
// TooLongFrameException message does (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring).
public class ProtobufVarint32FrameDecoderTest
{

    private EmbeddedChannel ch;

    // CLR: xUnit creates a new test class instance for every test, so the @BeforeEach setUp() runs from the
    // constructor (https://xunit.net/docs/shared-context); it is private because xUnit1013 reports public non-test
    // methods on a test class.
    public ProtobufVarint32FrameDecoderTest()
    {
        SetUp();
    }

    private void SetUp()
    {
        ch = new EmbeddedChannel(new ProtobufVarint32FrameDecoder());
    }

    [Fact]
    public void TestTinyDecode()
    {
        byte[] b = { 4, 1, 1, 1, 1 };
        Assert.False(ch.WriteInbound(WrappedBuffer(b, 0, 1)));
        Assert.Null(ch.ReadInbound());
        Assert.False(ch.WriteInbound(WrappedBuffer(b, 1, 2)));
        Assert.Null(ch.ReadInbound());
        Assert.True(ch.WriteInbound(WrappedBuffer(b, 3, b.Length - 3)));

        ByteBuf expected = WrappedBuffer(new byte[] { 1, 1, 1, 1 });
        ByteBuf actual = ch.ReadInbound<ByteBuf>();

        Assert.Equal(expected, actual);
        Assert.False(ch.Finish());

        expected.Release();
        actual.Release();
    }

    [Fact]
    public void TestRegularDecode()
    {
        byte[] b = new byte[2048];
        for (int i = 2; i < 2048; i ++)
        {
            b[i] = 1;
        }
        b[0] = unchecked((byte) -2);
        b[1] = 15;
        Assert.False(ch.WriteInbound(WrappedBuffer(b, 0, 1)));
        Assert.Null(ch.ReadInbound());
        Assert.False(ch.WriteInbound(WrappedBuffer(b, 1, 127)));
        Assert.Null(ch.ReadInbound());
        Assert.False(ch.WriteInbound(WrappedBuffer(b, 127, 600)));
        Assert.Null(ch.ReadInbound());
        Assert.True(ch.WriteInbound(WrappedBuffer(b, 727, b.Length - 727)));

        ByteBuf expected = WrappedBuffer(b, 2, b.Length - 2);
        ByteBuf actual = ch.ReadInbound<ByteBuf>();
        Assert.Equal(expected, actual);
        Assert.False(ch.Finish());

        expected.Release();
        actual.Release();
    }

    [Fact]
    public void TestFrameWithinMaxFrameLength()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new ProtobufVarint32FrameDecoder(10));
        byte[] b = { 4, 1, 1, 1, 1 };
        Assert.True(channel.WriteInbound(WrappedBuffer(b)));

        ByteBuf expected = WrappedBuffer(new byte[] { 1, 1, 1, 1 });
        ByteBuf actual = channel.ReadInbound<ByteBuf>();
        Assert.Equal(expected, actual);
        Assert.False(channel.Finish());

        expected.Release();
        actual.Release();
    }

    [Fact]
    public void TestFrameExceedingMaxFrameLength()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new ProtobufVarint32FrameDecoder(3));
        byte[] b = { 4, 1, 1, 1, 1 };
        Assert.Throws<TooLongFrameException>(() => channel.WriteInbound(WrappedBuffer(b)));
        Assert.Null(channel.ReadInbound());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestOversizedFramePartialDiscard()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new ProtobufVarint32FrameDecoder(3));

        // Frame with length=10, only send length byte + 5 data bytes
        byte[] partial = { 10, 1, 2, 3, 4, 5 };
        Assert.Throws<TooLongFrameException>(() => channel.WriteInbound(WrappedBuffer(partial)));

        // Send remaining 5 bytes — should be silently discarded
        byte[] remaining = { 6, 7, 8, 9, 10 };
        Assert.False(channel.WriteInbound(WrappedBuffer(remaining)));
        Assert.Null(channel.ReadInbound());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestValidFrameAfterOversized()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new ProtobufVarint32FrameDecoder(5));

        // Oversized frame: length=10, all data present
        byte[] oversized = new byte[11];
        oversized[0] = 10;
        for (int i = 1; i <= 10; i++)
        {
            oversized[i] = (byte) i;
        }
        Assert.Throws<TooLongFrameException>(() => channel.WriteInbound(WrappedBuffer(oversized)));

        // Valid frame after recovery
        byte[] valid = { 3, 10, 20, 30 };
        Assert.True(channel.WriteInbound(WrappedBuffer(valid)));
        ByteBuf expected = WrappedBuffer(new byte[] { 10, 20, 30 });
        ByteBuf actual = channel.ReadInbound<ByteBuf>();
        Assert.Equal(expected, actual);
        Assert.False(channel.Finish());

        expected.Release();
        actual.Release();
    }

    [Fact]
    public void TestLengthPrefixSplitAtEveryPosition()
    {
        // Cover 1, 2, 3 and 4 byte prefixes, including lengths whose low 21 bits are all zero (multiples of 2 MiB).
        // For these the partial varint read of the first 3 prefix bytes evaluates to 0.
        int[] lengths = {
                1, 127,
                128, 16383,
                16384, 2097151,
                2097152, 2097152 + 5, 3 * 1024 * 1024, 4 * 1024 * 1024
        };
        foreach (int length in lengths)
        {
            ByteBuf wire = Unpooled.Buffer();
            ProtobufVarint32LengthFieldPrepender.WriteRawVarint32(wire, length);
            int prefixLength = wire.ReadableBytes;
            byte[] payload = new byte[length];
            // Mark the last byte so a frame that starts or ends at the wrong position is detected.
            payload[length - 1] = 1;
            wire.WriteBytes(payload);
            ByteBuf expected = wire.Slice(prefixLength, length);

            for (int split = 1; split <= prefixLength; split++)
            {
                EmbeddedChannel channel = new EmbeddedChannel(new ProtobufVarint32FrameDecoder());
                string message = "length: " + length.ToString(CultureInfo.InvariantCulture) + ", split after: "
                        + split.ToString(CultureInfo.InvariantCulture);
                Assert.False(channel.WriteInbound(wire.RetainedSlice(0, split)), message);
                Assert.True(channel.WriteInbound(wire.RetainedSlice(split, wire.ReadableBytes - split)), message);

                ByteBuf actual = channel.ReadInbound<ByteBuf>();
                try
                {
                    Assert.Equal(length, actual.ReadableBytes, message);
                    Assert.Equal(expected, actual, message);
                }
                finally
                {
                    actual.Release();
                }
                Assert.Null(channel.ReadInbound(), message);
                Assert.False(channel.Finish(), message);
            }
            wire.Release();
        }
    }

    [Fact]
    public void TestFiveByteLengthPrefixSplitAtEveryPosition()
    {
        // 0x10000000 needs a 5 byte prefix: 80 80 80 80 01. Use a small maxFrameLength so we do not need to
        // allocate the whole frame, the TooLongFrameException proves the complete prefix was decoded.
        byte[] prefix = { (byte) 0x80, (byte) 0x80, (byte) 0x80, (byte) 0x80, 0x01 };
        for (int split = 1; split < prefix.Length; split++)
        {
            EmbeddedChannel channel = new EmbeddedChannel(new ProtobufVarint32FrameDecoder(1024));
            Assert.False(channel.WriteInbound(WrappedBuffer(prefix, 0, split)),
                    "split after: " + split.ToString(CultureInfo.InvariantCulture));
            ByteBuf remaining = WrappedBuffer(prefix, split, prefix.Length - split);
            TooLongFrameException e = Assert.Throws<TooLongFrameException>(
                    () => channel.WriteInbound(remaining), "split after: " + split.ToString(CultureInfo.InvariantCulture));
            Assert.True(e.Message.Contains(0x10000000.ToString(CultureInfo.InvariantCulture)), e.Message);
            Assert.False(channel.Finish());
        }
    }

    [Fact]
    public void TestReadRawVarint32WaitsForIncompleteVarint()
    {
        byte[][] incomplete = {
                new byte[] { (byte) 0x80 },
                new byte[] { (byte) 0x80, (byte) 0x80 },
                new byte[] { (byte) 0x80, (byte) 0x80, (byte) 0x80 },
                new byte[] { (byte) 0x80, (byte) 0x80, (byte) 0x80, (byte) 0x80 }
        };
        foreach (byte[] bytes in incomplete)
        {
            ByteBuf buf = WrappedBuffer(bytes);
            ProtobufVarint32FrameDecoder.ReadRawVarint32(buf);
            Assert.Equal(0, buf.ReaderIndex, "readerIndex must not move for "
                    + bytes.Length.ToString(CultureInfo.InvariantCulture) + " incomplete bytes");
            buf.Release();
        }
    }

    [Fact]
    public void TestReadRawVarint32RejectsSixByteVarint()
    {
        ByteBuf buf = WrappedBuffer(new byte[] { (byte) 0x80, (byte) 0x80, (byte) 0x80, (byte) 0x80, (byte) 0x80 });
        Assert.Throws<CorruptedFrameException>(() => ProtobufVarint32FrameDecoder.ReadRawVarint32(buf));
        buf.Release();
    }
}
