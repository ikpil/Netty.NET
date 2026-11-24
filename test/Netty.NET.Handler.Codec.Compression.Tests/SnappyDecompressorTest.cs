/*
 * Copyright 2025 The Netty Project
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

using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: the inherited tests of AbstractDecompressorTest run for this class as JUnit runs them; createCompressor() and
// createDecompressor() implement its abstract members. Decompressor is IDecompressor, try-with-resources is a using
// statement whose Dispose() calls Close(), status() is Status_() and builder() is Builder_() (see
// SnappyFrameDecompressor). ByteBufAllocator.DEFAULT is IByteBufAllocator.Default, ChannelHandler is IChannelHandler,
// and decompressor::endOfInput is the method group decompressor.EndOfInput. Each @CsvSource row of a
// @ParameterizedTest is an [InlineData] row of a [Theory]. The char literals of the byte[] initializers are cast to
// byte, (byte) 0x81 keeps its bit pattern, and writeByte(int) writes the low eight bits of -0x68 and -0x47 as in Java.
public class SnappyDecompressorTest : AbstractDecompressorTest
{
    protected override IChannelHandler CreateCompressor()
    {
        return new SnappyFrameEncoder();
    }

    protected override IDecompressor.AbstractDecompressorBuilder CreateDecompressor()
    {
        return SnappyFrameDecompressor.Builder_();
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

    [Fact]
    public void TestInvalidChecksumThrowsException()
    {
        using (IDecompressor decompressor = SnappyFrameDecompressor.Builder_()
                .ValidateChecksums(true).Build(IByteBufAllocator.Default))
        {
            ByteBuf @in = UncompressedDataWithChecksum(0, 0, 0, 0);

            Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
            Assert.Throws<DecompressionException>(() => decompressor.AddInput(@in));
        }
    }

    [Fact]
    public void TestValidChecksumProducesOutput()
    {
        using (IDecompressor decompressor = SnappyFrameDecompressor.Builder_()
                .ValidateChecksums(true).Build(IByteBufAllocator.Default))
        {
            ByteBuf @in = UncompressedDataWithChecksum(0x6f, -0x68, 0x2e, -0x47);

            Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
            decompressor.AddInput(@in);
            Assert.Equal(IDecompressor.Status.NEED_OUTPUT, decompressor.Status_());

            ByteBuf expected = Unpooled.WrappedBuffer(new byte[] { (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y' });
            ByteBuf actual = decompressor.TakeOutput();
            try
            {
                Assert.Equal(expected, actual);
            }
            finally
            {
                expected.Release();
                actual.Release();
            }

            Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
            decompressor.EndOfInput();
            Assert.Equal(IDecompressor.Status.COMPLETE, decompressor.Status_());
        }
    }

    [Fact]
    public void TestEndOfInputWithPartialFrameHeaderThrowsException()
    {
        AssertTruncated(Unpooled.WrappedBuffer(new byte[] { (byte) 0xff, 0x06 }));
    }

    [Fact]
    public void TestEndOfInputWithPartialStreamIdentifierThrowsException()
    {
        AssertTruncated(Unpooled.WrappedBuffer(new byte[] { (byte) 0xff, 0x06, 0x00, 0x00, (byte) 's', (byte) 'N' }));
    }

    [Fact]
    public void TestEndOfInputWithPartialUncompressedDataThrowsException()
    {
        AssertTruncated(Unpooled.WrappedBuffer(new byte[] {
                (byte) 0xff, 0x06, 0x00, 0x00, (byte) 's', (byte) 'N', (byte) 'a', (byte) 'P', (byte) 'p', (byte) 'Y',
                0x01, 0x09, 0x00, 0x00, 0x00, 0x00
        }));
    }

    [Fact]
    public void TestEndOfInputWithPartialSkippableChunkThrowsException()
    {
        AssertTruncated(Unpooled.WrappedBuffer(new byte[] {
                (byte) 0xff, 0x06, 0x00, 0x00, (byte) 's', (byte) 'N', (byte) 'a', (byte) 'P', (byte) 'p', (byte) 'Y',
                (byte) 0x81, 0x05, 0x00, 0x00, (byte) 'n', (byte) 'e'
        }));
    }

    private static void AssertInvalidChunkLength(bool validateChecksums, byte chunkType, int chunkLength)
    {
        using (IDecompressor decompressor = SnappyFrameDecompressor.Builder_()
                .ValidateChecksums(validateChecksums).Build(IByteBufAllocator.Default))
        {
            ByteBuf @in = IByteBufAllocator.Default.Buffer(14 + chunkLength);

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

            Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
            Assert.Throws<DecompressionException>(() => decompressor.AddInput(@in));
        }
    }

    private static ByteBuf UncompressedDataWithChecksum(int checksumByte1, int checksumByte2,
            int checksumByte3, int checksumByte4)
    {
        ByteBuf @in = IByteBufAllocator.Default.Buffer(23);
        @in.WriteByte(0xff);
        @in.WriteMediumLE(6);
        @in.WriteByte('s');
        @in.WriteByte('N');
        @in.WriteByte('a');
        @in.WriteByte('P');
        @in.WriteByte('p');
        @in.WriteByte('Y');
        @in.WriteByte(0x01);
        @in.WriteMediumLE(9);
        @in.WriteByte(checksumByte1);
        @in.WriteByte(checksumByte2);
        @in.WriteByte(checksumByte3);
        @in.WriteByte(checksumByte4);
        @in.WriteByte('n');
        @in.WriteByte('e');
        @in.WriteByte('t');
        @in.WriteByte('t');
        @in.WriteByte('y');
        return @in;
    }

    private static void AssertTruncated(ByteBuf @in)
    {
        using (IDecompressor decompressor = SnappyFrameDecompressor.Builder_().Build(IByteBufAllocator.Default))
        {
            Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
            decompressor.AddInput(@in);
            Assert.Equal(IDecompressor.Status.NEED_INPUT, decompressor.Status_());
            Assert.Throws<DecompressionException>(decompressor.EndOfInput);
        }
    }
}
