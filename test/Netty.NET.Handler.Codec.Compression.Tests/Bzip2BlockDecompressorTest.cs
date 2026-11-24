/*
 * Copyright 2026 The Netty Project
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
using static Netty.NET.Handler.Codec.Compression.Bzip2Constants;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Compression.Tests;

// CLR: the test drives the internal Bzip2BitReader, Bzip2HuffmanStageDecoder and Bzip2BlockDecompressor through
// InternalsVisibleTo, as the Java test uses the package-private classes. Arrays.fill(a, from, to, (byte) 2) "Assigns the
// specified byte value to each element of the specified range" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Arrays.html)
// and is Array.Fill(a, (byte) 2, from, to - from), which assigns the value to the elements "within the range of
// startIndex (inclusive) and the next count number of indices"
// (https://learn.microsoft.com/en-us/dotnet/api/system.array.fill?view=net-10.0); the anonymous Executable is a lambda;
// assertThrows with a message is
// Assert.Throws from Xunit.v3.AssertMessages (DecompressionException has no subclasses, so the exact-type check of
// Assert.Throws accepts what JUnit accepts). The nested BitWriter keeps its Java shape; >>> is C#'s >>>.
public class Bzip2BlockDecompressorTest
{

    /**
     * Regression test for an integer overflow in {@link Bzip2BlockDecompressor#decodeHuffmanData}.
     *
     * <p>{@code repeatCount} accumulates RUNA/RUNB run lengths and doubles on every symbol with no
     * ceiling. The guard that protects the output block, {@code bwtBlockLength + repeatCount >
     * streamBlockSize}, adds before comparing, so once a run drives {@code repeatCount} close to
     * {@link Integer#MAX_VALUE} the sum wraps negative and the guard is bypassed, letting the decoder
     * attempt to write a run far larger than the block it was validating against.
     *
     * <p>This test drives {@link Bzip2BlockDecompressor} directly, bypassing the full bzip2 header
     * parsing, by building a minimal one-table Huffman alphabet of 4 symbols (RUNA, RUNB, one literal,
     * and end-of-block) and feeding it: two literals (so {@code bwtBlockLength == 2}), 30 consecutive
     * RUNB symbols (so {@code repeatCount == 2^31 - 2 == 2147483646}, still positive), then
     * end-of-block. {@code 2 + 2147483646} overflows to {@code Integer.MIN_VALUE}, which is not
     * greater than the tiny declared block size used here, so the unfixed guard lets execution reach
     * the write loop instead of raising {@link DecompressionException}.
     */
    [Fact]
    public void TestHuffmanRunLengthOverflowDoesNotBypassBlockSizeCheck()
    {
        int totalTables = 1;
        int alphabetSize = 4; // RUNA=0, RUNB=1, literal=2, EOB=3
        int literalSymbol = 2;
        int endOfBlockSymbol = 3;
        int streamBlockSize = 5;

        BitWriter writer = new BitWriter();
        writer.WriteBits(2, literalSymbol);
        writer.WriteBits(2, literalSymbol);
        for (int i = 0; i < 30; i++)
        {
            writer.WriteBits(2, HUFFMAN_SYMBOL_RUNB);
        }
        writer.WriteBits(2, endOfBlockSymbol);
        // Trailing padding so hasReadableBits() lookahead never runs dry before EOB is decoded.
        for (int i = 0; i < 8; i++)
        {
            writer.WriteBits(8, 0);
        }

        ByteBuf input = writer.Finish();
        Bzip2BitReader reader = new Bzip2BitReader();
        reader.SetByteBuf(input);

        Bzip2HuffmanStageDecoder huffmanDecoder =
            new Bzip2HuffmanStageDecoder(reader, totalTables, alphabetSize);
        Array.Fill(huffmanDecoder.tableCodeLengths[0], (byte) 2, 0, alphabetSize);
        huffmanDecoder.selectors = new byte[] { 0 };
        huffmanDecoder.CreateHuffmanDecodingTables();

        Bzip2BlockDecompressor decompressor =
            new Bzip2BlockDecompressor(streamBlockSize, 0, false, 0, reader);
        decompressor.huffmanEndOfBlockSymbol = endOfBlockSymbol;

        Assert.Throws<DecompressionException>(() =>
        {
            decompressor.DecodeHuffmanData(huffmanDecoder);
        }, "repeatCount overflow must not bypass the block size check");

        input.Release();
    }

    /**
     * Minimal MSB-first bit packer mirroring {@link Bzip2BitReader}'s unpacking order, used to build
     * synthetic Huffman-coded symbol streams for {@link #testHuffmanRunLengthOverflowDoesNotBypassBlockSizeCheck()}.
     */
    private sealed class BitWriter
    {
        private readonly ByteBuf buf = Unpooled.Buffer();
        private long bitBuf;
        private int bitCount;

        internal void WriteBits(int count, int value)
        {
            bitBuf = bitBuf << count | value & ((1L << count) - 1);
            bitCount += count;
            while (bitCount >= 8)
            {
                bitCount -= 8;
                buf.WriteByte((int) (bitBuf >>> bitCount) & 0xFF);
            }
        }

        internal ByteBuf Finish()
        {
            if (bitCount > 0)
            {
                buf.WriteByte((int) (bitBuf << 8 - bitCount) & 0xFF);
                bitCount = 0;
            }
            return buf;
        }
    }
}
