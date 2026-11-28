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
using static Netty.NET.Handler.Codec.Compression.Bzip2Constants;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * A decoder for the Bzip2 Huffman coding stage.
 */
// CLR: the package-private final class is an internal sealed class; its package-private fields are internal fields with
// the Java names. new int[totalTables][N] allocates every row in Java; a C# jagged array "is an array of arrays, and each
// member array has the default value of null" and "You must initialize a jagged array's elements before you can use
// them" (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/arrays), so the constructor
// allocates the rows in a loop.
internal sealed class Bzip2HuffmanStageDecoder
{
    /**
     * A reader that provides bit-level reads.
     */
    private readonly Bzip2BitReader reader;

    /**
     * The Huffman table number to use for each group of 50 symbols.
     */
    internal byte[] selectors;

    /**
     * The minimum code length for each Huffman table.
     */
    private readonly int[] minimumLengths;

    /**
     * An array of values for each Huffman table that must be subtracted from the numerical value of
     * a Huffman code of a given bit length to give its canonical code index.
     */
    private readonly int[][] codeBases;

    /**
     * An array of values for each Huffman table that gives the highest numerical value of a Huffman
     * code of a given bit length.
     */
    private readonly int[][] codeLimits;

    /**
     * A mapping for each Huffman table from canonical code index to output symbol.
     */
    private readonly int[][] codeSymbols;

    /**
     * The Huffman table for the current group.
     */
    private int currentTable;

    /**
     * The index of the current group within the selectors array.
     */
    private int groupIndex = -1;

    /**
     * The byte position within the current group. A new group is selected every 50 decoded bytes.
     */
    private int groupPosition = -1;

    /**
     * Total number of used Huffman tables in range 2..6.
     */
    internal readonly int totalTables;

    /**
     * The total number of codes (uniform for each table).
     */
    internal readonly int alphabetSize;

    /**
     * Table for Move To Front transformations.
     */
    internal readonly Bzip2MoveToFrontTable tableMTF = new Bzip2MoveToFrontTable();

    // For saving state if end of current ByteBuf was reached
    internal int currentSelector;

    /**
     * The Canonical Huffman code lengths for each table.
     */
    internal readonly byte[][] tableCodeLengths;

    // For saving state if end of current ByteBuf was reached
    internal int currentGroup;
    internal int currentLength = -1;
    internal int currentAlpha;
    internal bool modifyLength;

    internal Bzip2HuffmanStageDecoder(Bzip2BitReader reader, int totalTables, int alphabetSize)
    {
        this.reader = reader;
        this.totalTables = totalTables;
        this.alphabetSize = alphabetSize;

        minimumLengths = new int[totalTables];
        codeBases = new int[totalTables][];
        codeLimits = new int[totalTables][];
        codeSymbols = new int[totalTables][];
        tableCodeLengths = new byte[totalTables][];

        for (int table = 0; table < totalTables; table++)
        {
            codeBases[table] = new int[HUFFMAN_DECODE_MAX_CODE_LENGTH + 2];
            codeLimits[table] = new int[HUFFMAN_DECODE_MAX_CODE_LENGTH + 1];
            codeSymbols[table] = new int[HUFFMAN_MAX_ALPHABET_SIZE];
            tableCodeLengths[table] = new byte[HUFFMAN_MAX_ALPHABET_SIZE];
        }
    }

    /**
     * Constructs Huffman decoding tables from lists of Canonical Huffman code lengths.
     */
    // CLR: byte-sign spot. The decoders store each code length as (byte) currLength, and a malformed stream can drive
    // currLength below zero or above 127; Java reads the signed byte back into the int arithmetic below. The unsigned C#
    // byte keeps the same eight bits, so each read is cast to sbyte, which gives the Java value (an unchecked conversion
    // between types of the same size "is treated as a value of the destination type",
    // https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/numeric-conversions). The local
    // base is @base because base is a C# keyword. currentTable = selectors[0] needs no cast: the selectors are MTF
    // indexes below the number of tables (2..6).
    internal void CreateHuffmanDecodingTables()
    {
        int alphabetSize = this.alphabetSize;

        for (int table = 0; table < tableCodeLengths.Length; table++)
        {
            int[] tableBases = codeBases[table];
            int[] tableLimits = codeLimits[table];
            int[] tableSymbols = codeSymbols[table];
            byte[] codeLengths = tableCodeLengths[table];

            int minimumLength = HUFFMAN_DECODE_MAX_CODE_LENGTH;
            int maximumLength = 0;

            // Find the minimum and maximum code length for the table
            for (int i = 0; i < alphabetSize; i++)
            {
                sbyte currLength = (sbyte) codeLengths[i];
                maximumLength = Math.Max(currLength, maximumLength);
                minimumLength = Math.Min(currLength, minimumLength);
            }

            minimumLengths[table] = minimumLength;

            // Calculate the first output symbol for each code length
            for (int i = 0; i < alphabetSize; i++)
            {
                tableBases[(sbyte) codeLengths[i] + 1]++;
            }

            for (int i = 1, b = tableBases[0]; i < HUFFMAN_DECODE_MAX_CODE_LENGTH + 2; i++)
            {
                b += tableBases[i];
                tableBases[i] = b;
            }

            // Calculate the first and last Huffman code for each code length (codes at a given
            // length are sequential in value)
            for (int i = minimumLength, code = 0; i <= maximumLength; i++)
            {
                int @base = code;
                code += tableBases[i + 1] - tableBases[i];
                tableBases[i] = @base - tableBases[i];
                tableLimits[i] = code - 1;
                code <<= 1;
            }

            // Populate the mapping from canonical code index to output symbol
            for (int bitLength = minimumLength, codeIndex = 0; bitLength <= maximumLength; bitLength++)
            {
                for (int symbol = 0; symbol < alphabetSize; symbol++)
                {
                    if ((sbyte) codeLengths[symbol] == bitLength)
                    {
                        tableSymbols[codeIndex++] = symbol;
                    }
                }
            }
        }

        currentTable = selectors[0];
    }

    /**
     * Decodes and returns the next symbol.
     * @return The decoded symbol
     */
    // CLR: the field assignment currentTable = ... is this.currentTable = ...: a C# local variable's scope is its whole
    // block, so the simple name would bind to the local currentTable declared further down ("Cannot use local variable
    // 'name' before it is declared", https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0841), while Java's local
    // scope starts at its declaration.
    internal int NextSymbol()
    {
        // Move to next group selector if required
        if (++groupPosition % HUFFMAN_GROUP_RUN_LENGTH == 0)
        {
            groupIndex++;
            if (groupIndex == selectors.Length)
            {
                throw new DecompressionException("error decoding block");
            }

            this.currentTable = selectors[groupIndex] & 0xff;
        }

        Bzip2BitReader reader = this.reader;
        int currentTable = this.currentTable;
        int[] tableLimits = codeLimits[currentTable];
        int[] tableBases = codeBases[currentTable];
        int[] tableSymbols = codeSymbols[currentTable];
        int codeLength = minimumLengths[currentTable];

        // Starting with the minimum bit length for the table, read additional bits one at a time
        // until a complete code is recognised
        int codeBits = reader.ReadBits(codeLength);

        for (; codeLength <= HUFFMAN_DECODE_MAX_CODE_LENGTH; codeLength++)
        {
            if (codeBits <= tableLimits[codeLength])
            {
                // Convert the code to a symbol index and return
                return tableSymbols[codeBits - tableBases[codeLength]];
            }

            codeBits = codeBits << 1 | reader.ReadBits(1);
        }

        throw new DecompressionException("a valid code was not recognised");
    }
}
