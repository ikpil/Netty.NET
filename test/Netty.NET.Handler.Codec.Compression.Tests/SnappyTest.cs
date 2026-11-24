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
using System.Text;
using Netty.NET.Buffer;
using static Netty.NET.Handler.Codec.Compression.Snappy;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: xUnit creates a new test class instance for every test, so the @AfterEach resetSnappy() runs from Dispose()
// (https://xunit.net/docs/shared-context) and is private (xUnit1013 asks public methods of a test class to be tests);
// the JUnit message overloads use XunitAssertMessages.AssertM. The package-private Snappy members are internal and
// reached through InternalsVisibleTo. The anonymous Executable classes are lambdas. The byte[] initializers keep the bit
// patterns of the Java byte literals: negative values are unchecked((byte) ...) and char literals are cast to byte,
// because only int constants in range convert implicitly
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types).
// StandardCharsets.US_ASCII/CharsetUtil.US_ASCII are Encoding.ASCII (docs/codec-base-porting.md), and the comparison of
// the two CharBuffer.wrap(...) views compares the source string with the text of GetCharSequence(...).
public class SnappyTest : IDisposable
{
    private readonly Snappy snappy = new Snappy();

    public void Dispose()
    {
        ResetSnappy();
    }

    private void ResetSnappy()
    {
        snappy.Reset();
    }

    [Fact]
    public void TestDecodeLiteral()
    {
        ByteBuf @in = Unpooled.WrappedBuffer(new byte[] {
            0x05, // preamble length
            0x04 << 2, // literal tag + length
            0x6e, 0x65, 0x74, 0x74, 0x79 // "netty"
        });
        ByteBuf @out = Unpooled.Buffer(5);
        snappy.Decode(@in, @out);

        // "netty"
        ByteBuf expected = Unpooled.WrappedBuffer(new byte[] {
            0x6e, 0x65, 0x74, 0x74, 0x79
        });
        Assert.Equal(expected, @out, "Literal was not decoded correctly");

        @in.Release();
        @out.Release();
        expected.Release();
    }

    [Fact]
    public void TestDecodeCopyWith1ByteOffset()
    {
        ByteBuf @in = Unpooled.WrappedBuffer(new byte[] {
            0x0a, // preamble length
            0x04 << 2, // literal tag + length
            0x6e, 0x65, 0x74, 0x74, 0x79, // "netty"
            0x01 << 2 | 0x01, // copy with 1-byte offset + length
            0x05 // offset
        });
        ByteBuf @out = Unpooled.Buffer(10);
        snappy.Decode(@in, @out);

        // "nettynetty" - we saved a whole byte :)
        ByteBuf expected = Unpooled.WrappedBuffer(new byte[] {
            0x6e, 0x65, 0x74, 0x74, 0x79, 0x6e, 0x65, 0x74, 0x74, 0x79
        });
        Assert.Equal(expected, @out, "Copy was not decoded correctly");

        @in.Release();
        @out.Release();
        expected.Release();
    }

    [Fact]
    public void TestDecodeCopyWithTinyOffset()
    {
        ByteBuf @in = Unpooled.WrappedBuffer(new byte[] {
            0x0b, // preamble length
            0x04 << 2, // literal tag + length
            0x6e, 0x65, 0x74, 0x74, 0x79, // "netty"
            0x05 << 2 | 0x01, // copy with 1-byte offset + length
            0x00 // INVALID offset (< 1)
        });
        ByteBuf @out = Unpooled.Buffer(10);
        try
        {
            Assert.Throws<DecompressionException>(() =>
            {
                snappy.Decode(@in, @out);
            });
        }
        finally
        {
            @in.Release();
            @out.Release();
        }
    }

    [Fact]
    public void TestDecodeCopyWithOffsetBeforeChunk()
    {
        ByteBuf @in = Unpooled.WrappedBuffer(new byte[] {
            0x0a, // preamble length
            0x04 << 2, // literal tag + length
            0x6e, 0x65, 0x74, 0x74, 0x79, // "netty"
            0x05 << 2 | 0x01, // copy with 1-byte offset + length
            0x0b // INVALID offset (greater than chunk size)
        });
        ByteBuf @out = Unpooled.Buffer(10);
        try
        {
            Assert.Throws<DecompressionException>(() =>
            {
                snappy.Decode(@in, @out);
            });
        }
        finally
        {
            @in.Release();
            @out.Release();
        }
    }

    [Fact]
    public void TestDecodeWithOverlyLongPreamble()
    {
        ByteBuf @in = Unpooled.WrappedBuffer(new byte[] {
            unchecked((byte) -0x80), unchecked((byte) -0x80), unchecked((byte) -0x80), unchecked((byte) -0x80), 0x7f, // preamble length
            0x04 << 2, // literal tag + length
            0x6e, 0x65, 0x74, 0x74, 0x79, // "netty"
        });
        ByteBuf @out = Unpooled.Buffer(10);
        try
        {
            Assert.Throws<DecompressionException>(() =>
            {
                snappy.Decode(@in, @out);
            });
        }
        finally
        {
            @in.Release();
            @out.Release();
        }
    }

    [Fact]
    public void EncodeShortTextIsLiteral()
    {
        ByteBuf @in = Unpooled.WrappedBuffer(new byte[] {
            0x6e, 0x65, 0x74, 0x74, 0x79
        });
        ByteBuf @out = Unpooled.Buffer(7);
        snappy.Encode(@in, @out, 5);

        ByteBuf expected = Unpooled.WrappedBuffer(new byte[] {
            0x05, // preamble length
            0x04 << 2, // literal tag + length
            0x6e, 0x65, 0x74, 0x74, 0x79 // "netty"
        });
        Assert.Equal(expected, @out, "Encoded literal was invalid");

        @in.Release();
        @out.Release();
        expected.Release();
    }

    [Fact]
    public void EncodeAndDecodeLongTextUsesCopy()
    {
        string srcStr = "Netty has been designed carefully with the experiences " +
                   "earned from the implementation of a lot of protocols " +
                   "such as FTP, SMTP, HTTP, and various binary and " +
                   "text-based legacy protocols";
        ByteBuf @in = Unpooled.WrappedBuffer(Encoding.ASCII.GetBytes(srcStr));
        ByteBuf @out = Unpooled.Buffer(180);
        snappy.Encode(@in, @out, @in.ReadableBytes);

        // The only compressibility in the above are the words:
        // "the ", "rotocols", " of ", "TP, " and "and ". So this is a literal,
        // followed by a copy followed by another literal, followed by another copy...
        ByteBuf expected = Unpooled.WrappedBuffer(new byte[] {
            unchecked((byte) -0x49), 0x01, // preamble length
            unchecked((byte) -0x10), 0x42, // literal tag + length

            // Literal
            0x4e, 0x65, 0x74, 0x74, 0x79, 0x20, 0x68, 0x61, 0x73, 0x20,
            0x62, 0x65, 0x65, 0x6e, 0x20, 0x64, 0x65, 0x73, 0x69, 0x67,
            0x6e, 0x65, 0x64, 0x20, 0x63, 0x61, 0x72, 0x65, 0x66, 0x75,
            0x6c, 0x6c, 0x79, 0x20, 0x77, 0x69, 0x74, 0x68, 0x20, 0x74,
            0x68, 0x65, 0x20, 0x65, 0x78, 0x70, 0x65, 0x72, 0x69, 0x65,
            0x6e, 0x63, 0x65, 0x73, 0x20, 0x65, 0x61, 0x72, 0x6e, 0x65,
            0x64, 0x20, 0x66, 0x72, 0x6f, 0x6d, 0x20,

            // copy of "the "
            0x01, 0x1c, 0x58,

            // Next literal
            0x69, 0x6d, 0x70, 0x6c, 0x65, 0x6d, 0x65, 0x6e, 0x74, 0x61,
            0x74, 0x69, 0x6f, 0x6e, 0x20, 0x6f, 0x66, 0x20, 0x61, 0x20,
            0x6c, 0x6f, 0x74,

            // copy of " of "
            0x01, 0x09, 0x60,

            // literal
            0x70, 0x72, 0x6f, 0x74, 0x6f, 0x63, 0x6f, 0x6c, 0x73, 0x20,
            0x73, 0x75, 0x63, 0x68, 0x20, 0x61, 0x73, 0x20, 0x46, 0x54,
            0x50, 0x2c, 0x20, 0x53, 0x4d,

            // copy of " TP, "
            0x01, 0x06, 0x04,

            // literal
            0x48, 0x54,

            // copy of " TP, "
            0x01, 0x06, 0x44,

            // literal
            0x61, 0x6e, 0x64, 0x20, 0x76, 0x61, 0x72, 0x69, 0x6f, 0x75,
            0x73, 0x20, 0x62, 0x69, 0x6e, 0x61, 0x72, 0x79,

            // copy of "and "
            0x05, 0x13, 0x48,

            // literal
            0x74, 0x65, 0x78, 0x74, 0x2d, 0x62, 0x61, 0x73, 0x65,
            0x64, 0x20, 0x6c, 0x65, 0x67, 0x61, 0x63, 0x79, 0x20, 0x70,

            // copy of "rotocols"
            0x11, 0x4c,
        });

        Assert.Equal(expected, @out, "Encoded result was incorrect");

        // Decode
        ByteBuf outDecoded = Unpooled.Buffer();
        snappy.Decode(@out, outDecoded);
        Assert.Equal(srcStr,
            outDecoded.GetCharSequence(0, outDecoded.WriterIndex, Encoding.ASCII).ToString());

        @in.Release();
        @out.Release();
        outDecoded.Release();
    }

    [Fact]
    public void TestCalculateChecksum()
    {
        ByteBuf input = Unpooled.WrappedBuffer(new byte[] {
                (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y'
        });

        Assert.Equal(MaskChecksum(0xd6cb8b55L), CalculateChecksum(input));
        input.Release();
    }

    [Fact]
    public void TestMaskChecksum()
    {
        ByteBuf input = Unpooled.WrappedBuffer(new byte[] {
                0x00, 0x00, 0x00, 0x0f, 0x00, 0x00, 0x00, 0x00,
                0x5f, 0x68, 0x65, 0x61, 0x72, 0x74, 0x62, 0x65,
                0x61, 0x74, 0x5f,
        });
        Assert.Equal(0x44a4301f, CalculateChecksum(input));
        input.Release();
    }

    [Fact]
    public void TestValidateChecksumMatches()
    {
        ByteBuf input = Unpooled.WrappedBuffer(new byte[] {
                (byte) 'y', (byte) 't', (byte) 't', (byte) 'e', (byte) 'n'
        });

        ValidateChecksum(MaskChecksum(0x2d4d3535), input);
        input.Release();
    }

    // CLR: the Java argument 0xd6cb8b55 is an int literal (negative), which maskChecksum(long) sign-extends; the unsuffixed
    // C# literal would be a positive uint
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types), whose
    // rotation differs in bits 17 to 31, so the argument is unchecked((int) 0xd6cb8b55) to pass the Java value.
    [Fact]
    public void TestValidateChecksumFails()
    {
        ByteBuf input = Unpooled.WrappedBuffer(new byte[] {
                (byte) 'y', (byte) 't', (byte) 't', (byte) 'e', (byte) 'n'
        });
        try
        {
            Assert.Throws<DecompressionException>(() =>
            {
                ValidateChecksum(MaskChecksum(unchecked((int) 0xd6cb8b55)), input);
            });
        }
        finally
        {
            input.Release();
        }
    }

    [Fact]
    public void TestEncodeLiteralAndDecodeLiteral()
    {
        int[] lengths = {
            0x11, // default
            0x100, // case 60
            0x1000, // case 61
            0x100000, // case 62
            0x1000001 // case 63
        };
        foreach (int len in lengths)
        {
            ByteBuf @in = Unpooled.WrappedBuffer(new byte[len]);
            ByteBuf encoded = Unpooled.Buffer(10);
            ByteBuf decoded = Unpooled.Buffer(10);
            ByteBuf expected = Unpooled.WrappedBuffer(new byte[len]);
            try
            {
                EncodeLiteral(@in, encoded, len);
                byte tag = encoded.ReadByte();
                DecodeLiteral(tag, encoded, decoded);
                Assert.Equal(expected, decoded, "Encoded or decoded literal was incorrect");
            }
            finally
            {
                @in.Release();
                encoded.Release();
                decoded.Release();
                expected.Release();
            }
        }
    }

    [Fact]
    public void TestLarge2ByteLiteralLengthAndCopyOffset()
    {
        ByteBuf compressed = Unpooled.Buffer();
        ByteBuf actualDecompressed = Unpooled.Buffer();
        ByteBuf expectedDecompressed = Unpooled.Buffer().WriteByte(0x01).WriteZero(0x8000).WriteByte(0x01);
        try
        {
            // Generate a Snappy-encoded buffer that can only be decompressed correctly if
            // the decoder treats 2-byte literal lengths and 2-byte copy offsets as unsigned values.

            // Write preamble, uncompressed content length (0x8002) encoded as varint.
            compressed.WriteByte(0x82).WriteByte(0x80).WriteByte(0x02);

            // Write a literal consisting of 0x01 followed by 0x8000 zeroes.
            // The total length of this literal is 0x8001, which gets encoded as 0x8000 (length - 1).
            // This length was selected because the encoded form is one larger than the maximum value
            // representable using a signed 16-bit integer, and we want to assert the decoder is reading
            // the length as an unsigned value.
            compressed.WriteByte(61 << 2); // tag for LITERAL with a 2-byte length
            compressed.WriteShortLE(0x8000); // length - 1
            compressed.WriteByte(0x01).WriteZero(0x8000); // literal content

            // Similarly, for a 2-byte copy operation we want to ensure the offset is treated as unsigned.
            // Copy the initial 0x01 which was written 0x8001 bytes back in the stream.
            compressed.WriteByte(0x02); // tag for COPY with 2-byte offset, length = 1
            compressed.WriteShortLE(0x8001); // offset

            snappy.Decode(compressed, actualDecompressed);
            Assert.Equal(expectedDecompressed, actualDecompressed);
        }
        finally
        {
            compressed.Release();
            actualDecompressed.Release();
            expectedDecompressed.Release();
        }
    }
}
