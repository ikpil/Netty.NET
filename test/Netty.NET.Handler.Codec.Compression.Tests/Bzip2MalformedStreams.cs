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

using System.IO;
using static Netty.NET.Handler.Codec.Compression.Bzip2Constants;

namespace Netty.NET.Handler.Codec.Compression.Tests;

/**
 * Hand-crafted, minimal bzip2 byte streams used to test handling of malformed input.
 */
// CLR: ByteArrayOutputStream is MemoryStream: write(int) writes "the eight low-order bits of the argument b. The
// 24 high-order bits of b are ignored"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/OutputStream.html), which is
// WriteByte((byte) value), an unchecked narrowing that keeps the low eight bits ("the source value is truncated by
// discarding its "extra" most significant bits",
// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/numeric-conversions);
// write(byte[], int, int) is Write and toByteArray() is ToArray(). Java's >>> is C#'s >>>.
internal sealed class Bzip2MalformedStreams
{

    private Bzip2MalformedStreams()
    {
    }

    /**
     * A stream that declares the minimum number of Huffman tables (2, so the only valid MTF
     * selector indices are 0 and 1) but encodes its single selector as the unary-coded index 9,
     * which is out of range.
     */
    internal static byte[] SelectorIndexOutOfRange()
    {
        BitWriter bits = new BitWriter();
        bits.WriteBit(0);            // randomised = false
        bits.WriteBits(0, 24);       // bwtStartPointer
        bits.WriteBits(0x0001, 16);  // huffmanInUse16: only the range 0xF0-0xFF is in use
        bits.WriteBits(0x8000, 16);  // symbol sub-bitmap: only 0xF0 is used
        bits.WriteBits(2, 3);        // totalTables = 2 (minimum allowed -> valid indices 0 or 1)
        bits.WriteBits(1, 15);       // totalSelectors = 1

        for (int i = 0; i < 9; i++)
        {
            bits.WriteBit(1);
        }

        bits.WriteBit(0);            // unary-coded selector index = 9 (out of range)

        MemoryStream @out = new MemoryStream();
        WriteMedium(@out, MAGIC_NUMBER);
        @out.WriteByte((byte) '1');  // block size digit
        WriteMedium(@out, BLOCK_HEADER_MAGIC_1);
        WriteMedium(@out, BLOCK_HEADER_MAGIC_2);
        WriteInt(@out, 0);            // block CRC (irrelevant, the fault triggers earlier)
        byte[] bitBytes = bits.ToByteArray();
        @out.Write(bitBytes, 0, bitBytes.Length);

        return @out.ToArray();
    }

    private static void WriteMedium(MemoryStream @out, int value)
    {
        @out.WriteByte((byte) (value >>> 16));
        @out.WriteByte((byte) (value >>> 8));
        @out.WriteByte((byte) value);
    }

    private static void WriteInt(MemoryStream @out, int value)
    {
        @out.WriteByte((byte) (value >>> 24));
        @out.WriteByte((byte) (value >>> 16));
        @out.WriteByte((byte) (value >>> 8));
        @out.WriteByte((byte) value);
    }

    /**
     * Packs bits MSB-first into bytes, matching {@link Bzip2BitReader}.
     */
    private sealed class BitWriter
    {
        private readonly MemoryStream @out = new MemoryStream();
        private int currentByte;
        private int bitsInCurrentByte;

        internal void WriteBit(int bit)
        {
            currentByte = currentByte << 1 | bit & 1;

            if (++bitsInCurrentByte == 8)
            {
                @out.WriteByte((byte) currentByte);
                currentByte = 0;
                bitsInCurrentByte = 0;
            }
        }

        internal void WriteBits(int value, int count)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                WriteBit(value >>> i);
            }
        }

        internal byte[] ToByteArray()
        {
            while (bitsInCurrentByte != 0)
            {
                WriteBit(0);
            }

            return @out.ToArray();
        }
    }
}
