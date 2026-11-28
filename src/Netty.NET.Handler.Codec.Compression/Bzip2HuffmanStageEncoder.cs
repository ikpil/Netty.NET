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
using Netty.NET.Buffer;
using static Netty.NET.Handler.Codec.Compression.Bzip2Constants;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * An encoder for the Bzip2 Huffman encoding stage.
 */
// CLR: the package-private final class is an internal sealed class and the out parameters are @out. The MTF block is a
// char[] in both languages: C# char is an unsigned 16-bit value like Java char, and it converts implicitly to int where
// it is used as an index or value. new int[totalTables][mtfAlphabetSize] allocates every row in Java; a C# jagged array
// "is an array of arrays, and each member array has the default value of null"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/arrays), so the rows are allocated in
// loops. Arrays.sort(int[]) "Sorts the specified array into ascending numerical order"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Arrays.html); Array.Sort(int[]) sorts the
// elements "using the IComparable<T> generic interface implementation of each element"
// (https://learn.microsoft.com/en-us/dotnet/api/system.array.sort?view=net-10.0). Equal ints are indistinguishable, so
// both give the same array. The int code lengths, the byte selectors (0..5) and the merged code symbols passed to the
// long parameter of writeBits are widened exactly as in Java, and >>> is C#'s >>>.
internal sealed class Bzip2HuffmanStageEncoder
{
    /**
     * Used in initial Huffman table generation.
     */
    private const int HUFFMAN_HIGH_SYMBOL_COST = 15;

    /**
     * The {@link Bzip2BitWriter} to which the Huffman tables and data is written.
     */
    private readonly Bzip2BitWriter writer;

    /**
     * The output of the Move To Front Transform and Run Length Encoding[2] stages.
     */
    private readonly char[] mtfBlock;

    /**
     * The actual number of values contained in the {@link #mtfBlock} array.
     */
    private readonly int mtfLength;

    /**
     * The number of unique values in the {@link #mtfBlock} array.
     */
    private readonly int mtfAlphabetSize;

    /**
     * The global frequencies of values within the {@link #mtfBlock} array.
     */
    private readonly int[] mtfSymbolFrequencies;

    /**
     * The Canonical Huffman code lengths for each table.
     */
    private readonly int[][] huffmanCodeLengths;

    /**
     * Merged code symbols for each table. The value at each position is ((code length << 24) | code).
     */
    private readonly int[][] huffmanMergedCodeSymbols;

    /**
     * The selectors for each segment.
     */
    private readonly byte[] selectors;

    /**
     * @param writer The {@link Bzip2BitWriter} which provides bit-level writes
     * @param mtfBlock The MTF block data
     * @param mtfLength The actual length of the MTF block
     * @param mtfAlphabetSize The size of the MTF block's alphabet
     * @param mtfSymbolFrequencies The frequencies the MTF block's symbols
     */
    internal Bzip2HuffmanStageEncoder(Bzip2BitWriter writer, char[] mtfBlock,
                                      int mtfLength, int mtfAlphabetSize, int[] mtfSymbolFrequencies)
    {
        this.writer = writer;
        this.mtfBlock = mtfBlock;
        this.mtfLength = mtfLength;
        this.mtfAlphabetSize = mtfAlphabetSize;
        this.mtfSymbolFrequencies = mtfSymbolFrequencies;

        int totalTables = SelectTableCount(mtfLength);

        huffmanCodeLengths = new int[totalTables][];
        huffmanMergedCodeSymbols = new int[totalTables][];

        for (int i = 0; i < totalTables; i++)
        {
            huffmanCodeLengths[i] = new int[mtfAlphabetSize];
            huffmanMergedCodeSymbols[i] = new int[mtfAlphabetSize];
        }

        selectors = new byte[(mtfLength + HUFFMAN_GROUP_RUN_LENGTH - 1) / HUFFMAN_GROUP_RUN_LENGTH];
    }

    /**
     * Selects an appropriate table count for a given MTF length.
     * @param mtfLength The length to select a table count for
     * @return The selected table count
     */
    private static int SelectTableCount(int mtfLength)
    {
        if (mtfLength >= 2400)
        {
            return 6;
        }
        if (mtfLength >= 1200)
        {
            return 5;
        }
        if (mtfLength >= 600)
        {
            return 4;
        }
        if (mtfLength >= 200)
        {
            return 3;
        }

        return 2;
    }

    /**
     * Generate a Huffman code length table for a given list of symbol frequencies.
     * @param alphabetSize The total number of symbols
     * @param symbolFrequencies The frequencies of the symbols
     * @param codeLengths The array to which the generated code lengths should be written
     */
    private static void GenerateHuffmanCodeLengths(int alphabetSize,
                                                   int[] symbolFrequencies, int[] codeLengths)
    {

        int[] mergedFrequenciesAndIndices = new int[alphabetSize];
        int[] sortedFrequencies = new int[alphabetSize];

        // The Huffman allocator needs its input symbol frequencies to be sorted, but we need to
        // return code lengths in the same order as the corresponding frequencies are passed in.

        // The symbol frequency and index are merged into a single array of
        // integers - frequency in the high 23 bits, index in the low 9 bits.
        //     2^23 = 8,388,608 which is higher than the maximum possible frequency for one symbol in a block
        //     2^9 = 512 which is higher than the maximum possible alphabet size (== 258)
        // Sorting this array simultaneously sorts the frequencies and
        // leaves a lookup that can be used to cheaply invert the sort.
        for (int i = 0; i < alphabetSize; i++)
        {
            mergedFrequenciesAndIndices[i] = (symbolFrequencies[i] << 9) | i;
        }

        Array.Sort(mergedFrequenciesAndIndices);

        for (int i = 0; i < alphabetSize; i++)
        {
            sortedFrequencies[i] = mergedFrequenciesAndIndices[i] >>> 9;
        }

        // Allocate code lengths - the allocation is in place,
        // so the code lengths will be in the sortedFrequencies array afterwards
        Bzip2HuffmanAllocator.AllocateHuffmanCodeLengths(sortedFrequencies, HUFFMAN_ENCODE_MAX_CODE_LENGTH);

        // Reverse the sort to place the code lengths in the same order as the symbols whose frequencies were passed in
        for (int i = 0; i < alphabetSize; i++)
        {
            codeLengths[mergedFrequenciesAndIndices[i] & 0x1ff] = sortedFrequencies[i];
        }
    }

    /**
     * Generate initial Huffman code length tables, giving each table a different low cost section
     * of the alphabet that is roughly equal in overall cumulative frequency. Note that the initial
     * tables are invalid for actual Huffman code generation, and only serve as the seed for later
     * iterative optimisation in {@link #optimiseSelectorsAndHuffmanTables(boolean)}.
     */
    private void GenerateHuffmanOptimisationSeeds()
    {
        int[][] huffmanCodeLengths = this.huffmanCodeLengths;
        int[] mtfSymbolFrequencies = this.mtfSymbolFrequencies;
        int mtfAlphabetSize = this.mtfAlphabetSize;

        int totalTables = huffmanCodeLengths.Length;

        int remainingLength = mtfLength;
        int lowCostEnd = -1;

        for (int i = 0; i < totalTables; i++)
        {

            int targetCumulativeFrequency = remainingLength / (totalTables - i);
            int lowCostStart = lowCostEnd + 1;
            int actualCumulativeFrequency = 0;

            while (actualCumulativeFrequency < targetCumulativeFrequency && lowCostEnd < mtfAlphabetSize - 1)
            {
                actualCumulativeFrequency += mtfSymbolFrequencies[++lowCostEnd];
            }

            if (lowCostEnd > lowCostStart && i != 0 && i != totalTables - 1 && (totalTables - i & 1) == 0)
            {
                actualCumulativeFrequency -= mtfSymbolFrequencies[lowCostEnd--];
            }

            int[] tableCodeLengths = huffmanCodeLengths[i];

            for (int j = 0; j < mtfAlphabetSize; j++)
            {
                if (j < lowCostStart || j > lowCostEnd)
                {
                    tableCodeLengths[j] = HUFFMAN_HIGH_SYMBOL_COST;
                }
            }

            remainingLength -= actualCumulativeFrequency;
        }
    }

    /**
     * Co-optimise the selector list and the alternative Huffman table code lengths. This method is
     * called repeatedly in the hope that the total encoded size of the selectors, the Huffman code
     * lengths and the block data encoded with them will converge towards a minimum.<br>
     * If the data is highly incompressible, it is possible that the total encoded size will
     * instead diverge (increase) slightly.<br>
     * @param storeSelectors If {@code true}, write out the (final) chosen selectors
     */
    private void OptimiseSelectorsAndHuffmanTables(bool storeSelectors)
    {
        char[] mtfBlock = this.mtfBlock;
        byte[] selectors = this.selectors;
        int[][] huffmanCodeLengths = this.huffmanCodeLengths;
        int mtfLength = this.mtfLength;
        int mtfAlphabetSize = this.mtfAlphabetSize;

        int totalTables = huffmanCodeLengths.Length;
        int[][] tableFrequencies = new int[totalTables][];

        for (int i = 0; i < totalTables; i++)
        {
            tableFrequencies[i] = new int[mtfAlphabetSize];
        }

        int selectorIndex = 0;

        // Find the best table for each group of 50 block bytes based on the current Huffman code lengths
        for (int groupStart = 0; groupStart < mtfLength;)
        {

            int groupEnd = Math.Min(groupStart + HUFFMAN_GROUP_RUN_LENGTH, mtfLength) - 1;

            // Calculate the cost of this group when encoded by each table
            int[] cost = new int[totalTables];

            for (int i = groupStart; i <= groupEnd; i++)
            {
                int value = mtfBlock[i];

                for (int j = 0; j < totalTables; j++)
                {
                    cost[j] += huffmanCodeLengths[j][value];
                }
            }

            // Find the table with the least cost for this group
            byte bestTable = 0;
            int bestCost = cost[0];

            for (byte i = 1 ; i < totalTables; i++)
            {
                int tableCost = cost[i];
                if (tableCost < bestCost)
                {
                    bestCost = tableCost;
                    bestTable = i;
                }
            }

            // Accumulate symbol frequencies for the table chosen for this block
            int[] bestGroupFrequencies = tableFrequencies[bestTable];

            for (int i = groupStart; i <= groupEnd; i++)
            {
                bestGroupFrequencies[mtfBlock[i]]++;
            }

            // Store a selector indicating the table chosen for this block
            if (storeSelectors)
            {
                selectors[selectorIndex++] = bestTable;
            }

            groupStart = groupEnd + 1;
        }

        // Generate new Huffman code lengths based on the frequencies for each table accumulated in this iteration
        for (int i = 0; i < totalTables; i++)
        {
            GenerateHuffmanCodeLengths(mtfAlphabetSize, tableFrequencies[i], huffmanCodeLengths[i]);
        }
    }

    /**
     * Assigns Canonical Huffman codes based on the calculated lengths.
     */
    private void AssignHuffmanCodeSymbols()
    {
        int[][] huffmanMergedCodeSymbols = this.huffmanMergedCodeSymbols;
        int[][] huffmanCodeLengths = this.huffmanCodeLengths;
        int mtfAlphabetSize = this.mtfAlphabetSize;

        int totalTables = huffmanCodeLengths.Length;

        for (int i = 0; i < totalTables; i++)
        {
            int[] tableLengths = huffmanCodeLengths[i];

            int minimumLength = 32;
            int maximumLength = 0;

            for (int j = 0; j < mtfAlphabetSize; j++)
            {
                int length = tableLengths[j];
                if (length > maximumLength)
                {
                    maximumLength = length;
                }

                if (length < minimumLength)
                {
                    minimumLength = length;
                }
            }

            int code = 0;

            for (int j = minimumLength; j <= maximumLength; j++)
            {
                for (int k = 0; k < mtfAlphabetSize; k++)
                {
                    if ((huffmanCodeLengths[i][k] & 0xff) == j)
                    {
                        huffmanMergedCodeSymbols[i][k] = (j << 24) | code;
                        code++;
                    }
                }

                code <<= 1;
            }
        }
    }

    /**
     * Write out the selector list and Huffman tables.
     */
    private void WriteSelectorsAndHuffmanTables(ByteBuf @out)
    {
        Bzip2BitWriter writer = this.writer;
        byte[] selectors = this.selectors;
        int totalSelectors = selectors.Length;
        int[][] huffmanCodeLengths = this.huffmanCodeLengths;
        int totalTables = huffmanCodeLengths.Length;
        int mtfAlphabetSize = this.mtfAlphabetSize;

        writer.WriteBits(@out, 3, totalTables);
        writer.WriteBits(@out, 15, totalSelectors);

        // Write the selectors
        Bzip2MoveToFrontTable selectorMTF = new Bzip2MoveToFrontTable();

        foreach (byte selector in selectors)
        {
            writer.WriteUnary(@out, selectorMTF.ValueToFront(selector));
        }

        // Write the Huffman tables
        foreach (int[] tableLengths in huffmanCodeLengths)
        {
            int currentLength = tableLengths[0];

            writer.WriteBits(@out, 5, currentLength);

            for (int j = 0; j < mtfAlphabetSize; j++)
            {
                int codeLength = tableLengths[j];
                int value = currentLength < codeLength ? 2 : 3;
                int delta = Math.Abs(codeLength - currentLength);

                while (delta-- > 0)
                {
                    writer.WriteBits(@out, 2, value);
                }

                writer.WriteBoolean(@out, false);
                currentLength = codeLength;
            }
        }
    }

    /**
     * Writes out the encoded block data.
     */
    private void WriteBlockData(ByteBuf @out)
    {
        Bzip2BitWriter writer = this.writer;
        int[][] huffmanMergedCodeSymbols = this.huffmanMergedCodeSymbols;
        byte[] selectors = this.selectors;
        int mtfLength = this.mtfLength;

        int selectorIndex = 0;

        for (int mtfIndex = 0; mtfIndex < mtfLength;)
        {
            int groupEnd = Math.Min(mtfIndex + HUFFMAN_GROUP_RUN_LENGTH, mtfLength) - 1;
            int[] tableMergedCodeSymbols = huffmanMergedCodeSymbols[selectors[selectorIndex++]];

            while (mtfIndex <= groupEnd)
            {
                int mergedCodeSymbol = tableMergedCodeSymbols[mtfBlock[mtfIndex++]];
                writer.WriteBits(@out, mergedCodeSymbol >>> 24, mergedCodeSymbol);
            }
        }
    }

    /**
     * Encodes and writes the block data.
     */
    internal void Encode(ByteBuf @out)
    {
        // Create optimised selector list and Huffman tables
        GenerateHuffmanOptimisationSeeds();

        for (int i = 3; i >= 0; i--)
        {
            OptimiseSelectorsAndHuffmanTables(i == 0);
        }

        AssignHuffmanCodeSymbols();

        // Write out the tables and the block data encoded with them
        WriteSelectorsAndHuffmanTables(@out);
        WriteBlockData(@out);
    }
}
