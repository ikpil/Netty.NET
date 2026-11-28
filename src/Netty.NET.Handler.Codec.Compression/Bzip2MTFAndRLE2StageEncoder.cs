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

using static Netty.NET.Handler.Codec.Compression.Bzip2Constants;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * An encoder for the Bzip2 Move To Front Transform and Run-Length Encoding[2] stages.<br>
 * Although conceptually these two stages are separate, it is computationally efficient to perform
 * them in one pass.
 */
// CLR: The MTF block is a char[] in both languages (an unsigned 16-bit value). Java narrows the int constants
// HUFFMAN_SYMBOL_RUNA/RUNB to char implicitly; C# converts a constant int implicitly only "to sbyte, byte, short,
// ushort, uint, ulong, nint, or nuint"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/numeric-conversions), so those
// stores are written (char). (byte) totalUniqueValues++ stores the value 0..255 with the same eight bits, and the
// symbol map is read back only through valueToFront's byte comparison. bwtBlock holds the block bytes that the BWT
// copied into int, -128..127 in Java and 0..255 in C# (see Bzip2DivSufSort); bwtBlock[i] & 0xff gives the same
// index for both. Java's >>> is C#'s >>>. The accessors mtfBlock(), mtfLength(), mtfAlphabetSize() and
// mtfSymbolFrequencies() are the methods MtfBlock()... MtfSymbolFrequencies(); the fields keep the Java names.
internal sealed class Bzip2MTFAndRLE2StageEncoder
{
    /**
     * The Burrows-Wheeler transformed block.
     */
    private readonly int[] bwtBlock;

    /**
     * Actual length of the data in the {@link #bwtBlock} array.
     */
    private readonly int bwtLength;

    /**
     * At each position, {@code true} if the byte value with that index is present within the block,
     * otherwise {@code false}.
     */
    private readonly bool[] bwtValuesPresent;

    /**
     * The output of the Move To Front Transform and Run-Length Encoding[2] stages.
     */
    private readonly char[] mtfBlock;

    /**
     * The actual number of values contained in the {@link #mtfBlock} array.
     */
    private int mtfLength;

    /**
     * The global frequencies of values within the {@link #mtfBlock} array.
     */
    private readonly int[] mtfSymbolFrequencies = new int[HUFFMAN_MAX_ALPHABET_SIZE];

    /**
     * The encoded alphabet size.
     */
    private int alphabetSize;

    /**
     * @param bwtBlock The Burrows Wheeler Transformed block data
     * @param bwtLength The actual length of the BWT data
     * @param bwtValuesPresent The values that are present within the BWT data. For each index,
     *            {@code true} if that value is present within the data, otherwise {@code false}
     */
    internal Bzip2MTFAndRLE2StageEncoder(int[] bwtBlock, int bwtLength, bool[] bwtValuesPresent)
    {
        this.bwtBlock = bwtBlock;
        this.bwtLength = bwtLength;
        this.bwtValuesPresent = bwtValuesPresent;
        mtfBlock = new char[bwtLength + 1];
    }

    /**
     * Performs the Move To Front transform and Run Length Encoding[1] stages.
     */
    internal void Encode()
    {
        int bwtLength = this.bwtLength;
        bool[] bwtValuesPresent = this.bwtValuesPresent;
        int[] bwtBlock = this.bwtBlock;
        char[] mtfBlock = this.mtfBlock;
        int[] mtfSymbolFrequencies = this.mtfSymbolFrequencies;
        byte[] huffmanSymbolMap = new byte[256];
        Bzip2MoveToFrontTable symbolMTF = new Bzip2MoveToFrontTable();

        int totalUniqueValues = 0;

        for (int i = 0; i < huffmanSymbolMap.Length; i++)
        {
            if (bwtValuesPresent[i])
            {
                huffmanSymbolMap[i] = (byte) totalUniqueValues++;
            }
        }

        int endOfBlockSymbol = totalUniqueValues + 1;

        int mtfIndex = 0;
        int repeatCount = 0;
        int totalRunAs = 0;
        int totalRunBs = 0;

        for (int i = 0; i < bwtLength; i++)
        {
            // Move To Front
            int mtfPosition = symbolMTF.ValueToFront(huffmanSymbolMap[bwtBlock[i] & 0xff]);
            // Run Length Encode
            if (mtfPosition == 0)
            {
                repeatCount++;
            }
            else
            {
                if (repeatCount > 0)
                {
                    repeatCount--;

                    while (true)
                    {
                        if ((repeatCount & 1) == 0)
                        {
                            mtfBlock[mtfIndex++] = (char) HUFFMAN_SYMBOL_RUNA;
                            totalRunAs++;
                        }
                        else
                        {
                            mtfBlock[mtfIndex++] = (char) HUFFMAN_SYMBOL_RUNB;
                            totalRunBs++;
                        }

                        if (repeatCount <= 1)
                        {
                            break;
                        }

                        repeatCount = (repeatCount - 2) >>> 1;
                    }

                    repeatCount = 0;
                }

                mtfBlock[mtfIndex++] = (char) (mtfPosition + 1);
                mtfSymbolFrequencies[mtfPosition + 1]++;
            }
        }

        if (repeatCount > 0)
        {
            repeatCount--;

            while (true)
            {
                if ((repeatCount & 1) == 0)
                {
                    mtfBlock[mtfIndex++] = (char) HUFFMAN_SYMBOL_RUNA;
                    totalRunAs++;
                }
                else
                {
                    mtfBlock[mtfIndex++] = (char) HUFFMAN_SYMBOL_RUNB;
                    totalRunBs++;
                }

                if (repeatCount <= 1)
                {
                    break;
                }

                repeatCount = (repeatCount - 2) >>> 1;
            }
        }

        mtfBlock[mtfIndex] = (char) endOfBlockSymbol;
        mtfSymbolFrequencies[endOfBlockSymbol]++;
        mtfSymbolFrequencies[HUFFMAN_SYMBOL_RUNA] += totalRunAs;
        mtfSymbolFrequencies[HUFFMAN_SYMBOL_RUNB] += totalRunBs;

        mtfLength = mtfIndex + 1;
        alphabetSize = endOfBlockSymbol + 1;
    }

    /**
     * @return The encoded MTF block
     */
    internal char[] MtfBlock()
    {
        return mtfBlock;
    }

    /**
     * @return The actual length of the MTF block
     */
    internal int MtfLength()
    {
        return mtfLength;
    }

    /**
     * @return The size of the MTF block's alphabet
     */
    internal int MtfAlphabetSize()
    {
        return alphabetSize;
    }

    /**
     * @return The frequencies of the MTF block's symbols
     */
    internal int[] MtfSymbolFrequencies()
    {
        return mtfSymbolFrequencies;
    }
}
