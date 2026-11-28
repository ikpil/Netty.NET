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
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: xUnit creates a new test class instance for every test, so the @BeforeEach initChannel() runs from the
// constructor and the @AfterEach tearDown() from Dispose() (https://xunit.net/docs/shared-context); both are private
// (xUnit1013 asks public methods of a test class to be tests). The anonymous Executable classes are lambdas, readInbound()
// is ReadInbound<ByteBuf>(), and channel.alloc() is the Alloc() of the Transport EmbeddedChannel. Each @CsvSource row of
// a @ParameterizedTest is an [InlineData] row of a [Theory]. The byte[] initializers keep the bit patterns of the Java
// byte literals: negative values are unchecked((byte) ...) and char literals are cast to byte, because only int
// constants in range convert implicitly
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types).
public class SnappyFrameDecoderTest : IDisposable
{
    private EmbeddedChannel channel;

    public SnappyFrameDecoderTest()
    {
        InitChannel();
    }

    public void Dispose()
    {
        TearDown();
    }

    private void InitChannel()
    {
        channel = new EmbeddedChannel(new SnappyFrameDecoder());
    }

    private void TearDown()
    {
        Assert.False(channel.FinishAndReleaseAll());
    }

    [Fact]
    public void TestReservedUnskippableChunkTypeCausesError()
    {
        ByteBuf @in = Unpooled.WrappedBuffer(new byte[] {
            0x03, 0x01, 0x00, 0x00, 0x00
        });

        Assert.Throws<DecompressionException>(() =>
        {
            channel.WriteInbound(@in);
        });
    }

    [Fact]
    public void TestInvalidStreamIdentifierLength()
    {
        ByteBuf @in = Unpooled.WrappedBuffer(new byte[] {
            unchecked((byte) -0x80), 0x05, 0x00, 0x00, (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y'
        });

        Assert.Throws<DecompressionException>(() =>
        {
            channel.WriteInbound(@in);
        });
    }

    [Fact]
    public void TestInvalidStreamIdentifierValue()
    {
        ByteBuf @in = Unpooled.WrappedBuffer(new byte[] {
            (byte) 0xff, 0x06, 0x00, 0x00, (byte) 's', (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y'
        });

        Assert.Throws<DecompressionException>(() =>
        {
            channel.WriteInbound(@in);
        });
    }

    [Fact]
    public void TestReservedSkippableBeforeStreamIdentifier()
    {
        ByteBuf @in = Unpooled.WrappedBuffer(new byte[] {
            unchecked((byte) -0x7f), 0x06, 0x00, 0x00, (byte) 's', (byte) 'n', (byte) 'e', (byte) 't', (byte) 't',
            (byte) 'y'
        });

        Assert.Throws<DecompressionException>(() =>
        {
            channel.WriteInbound(@in);
        });
    }

    [Fact]
    public void TestUncompressedDataBeforeStreamIdentifier()
    {
        ByteBuf @in = Unpooled.WrappedBuffer(new byte[] {
            0x01, 0x05, 0x00, 0x00, (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y'
        });

        Assert.Throws<DecompressionException>(() =>
        {
            channel.WriteInbound(@in);
        });
    }

    [Fact]
    public void TestCompressedDataBeforeStreamIdentifier()
    {
        ByteBuf @in = Unpooled.WrappedBuffer(new byte[] {
            0x00, 0x05, 0x00, 0x00, (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y'
        });

        Assert.Throws<DecompressionException>(() =>
        {
            channel.WriteInbound(@in);
        });
    }

    [Fact]
    public void TestReservedSkippableSkipsInput()
    {
        ByteBuf @in = Unpooled.WrappedBuffer(new byte[] {
           (byte) 0xff, 0x06, 0x00, 0x00, 0x73, 0x4e, 0x61, 0x50, 0x70, 0x59,
           unchecked((byte) -0x7f), 0x05, 0x00, 0x00, (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y'
        });

        Assert.False(channel.WriteInbound(@in));
        Assert.Null(channel.ReadInbound());

        Assert.False(@in.IsReadable());
    }

    [Fact]
    public void TestUncompressedDataAppendsToOut()
    {
        ByteBuf @in = Unpooled.WrappedBuffer(new byte[] {
           (byte) 0xff, 0x06, 0x00, 0x00, 0x73, 0x4e, 0x61, 0x50, 0x70, 0x59,
            0x01, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y'
        });

        Assert.True(channel.WriteInbound(@in));

        ByteBuf expected = Unpooled.WrappedBuffer(new byte[] { (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y' });
        ByteBuf actual = channel.ReadInbound<ByteBuf>();
        Assert.Equal(expected, actual);

        expected.Release();
        actual.Release();
    }

    [Fact]
    public void TestCompressedDataDecodesAndAppendsToOut()
    {
        ByteBuf @in = Unpooled.WrappedBuffer(new byte[] {
           (byte) 0xff, 0x06, 0x00, 0x00, 0x73, 0x4e, 0x61, 0x50, 0x70, 0x59,
            0x00, 0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                  0x05, // preamble length
                  0x04 << 2, // literal tag + length
                  0x6e, 0x65, 0x74, 0x74, 0x79 // "netty"
        });

        Assert.True(channel.WriteInbound(@in));

        ByteBuf expected = Unpooled.WrappedBuffer(new byte[] { (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y' });
        ByteBuf actual = channel.ReadInbound<ByteBuf>();

        Assert.Equal(expected, actual);

        expected.Release();
        actual.Release();
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(false, 4)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    [InlineData(true, 4)]
    public void TestCompressedDataWithTooShortChunkLengthThrowsException(
            bool validateChecksums, int chunkLength)
    {
        AssertInvalidChunkLength(validateChecksums, (byte) 0x00, chunkLength);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public void TestUncompressedDataWithTooShortChunkLengthThrowsException(
            bool validateChecksums, int chunkLength)
    {
        AssertInvalidChunkLength(validateChecksums, (byte) 0x01, chunkLength);
    }

    // The following two tests differ in only the checksum provided for the literal
    // uncompressed string "netty"

    [Fact]
    public void TestInvalidChecksumThrowsException()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new SnappyFrameDecoder(true));

        try
        {
            // checksum here is presented as 0
            ByteBuf @in = Unpooled.WrappedBuffer(new byte[]{
                    (byte) 0xff, 0x06, 0x00, 0x00, 0x73, 0x4e, 0x61, 0x50, 0x70, 0x59,
                    0x01, 0x09, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, (byte) 'n', (byte) 'e', (byte) 't', (byte) 't',
                    (byte) 'y'
            });

            Assert.Throws<DecompressionException>(() =>
            {
                channel.WriteInbound(@in);
            });
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    [Fact]
    public void TestInvalidChecksumDoesNotThrowException()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new SnappyFrameDecoder(true));

        try
        {
            // checksum here is presented as a282986f (little endian)
            ByteBuf @in = Unpooled.WrappedBuffer(new byte[]{
                    (byte) 0xff, 0x06, 0x00, 0x00, 0x73, 0x4e, 0x61, 0x50, 0x70, 0x59,
                    0x01, 0x09, 0x00, 0x00, 0x6f, unchecked((byte) -0x68), 0x2e, unchecked((byte) -0x47), (byte) 'n',
                    (byte) 'e', (byte) 't', (byte) 't', (byte) 'y'
            });

            Assert.True(channel.WriteInbound(@in));
            ByteBuf expected = Unpooled.WrappedBuffer(new byte[] { (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y' });
            ByteBuf actual = channel.ReadInbound<ByteBuf>();
            Assert.Equal(expected, actual);

            expected.Release();
            actual.Release();
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }

    private static void AssertInvalidChunkLength(bool validateChecksums, byte chunkType, int chunkLength)
    {
        EmbeddedChannel channel = new EmbeddedChannel(new SnappyFrameDecoder(validateChecksums));

        try
        {
            ByteBuf @in = channel.Alloc().Buffer(14 + chunkLength);

            // Snappy stream identifier chunk: type 0xff, 3-byte little-endian length 6, payload "sNaPpY".
            @in.WriteByte(0xff);
            @in.WriteMediumLE(6);
            @in.WriteByte('s');
            @in.WriteByte('N');
            @in.WriteByte('a');
            @in.WriteByte('P');
            @in.WriteByte('p');
            @in.WriteByte('Y');

            // Invalid data chunk header: caller-supplied type and too-short 3-byte little-endian length.
            @in.WriteByte(chunkType);
            @in.WriteMediumLE(chunkLength);
            @in.WriteZero(chunkLength);

            Assert.Throws<DecompressionException>(() =>
            {
                channel.WriteInbound(@in);
            });
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }
}
