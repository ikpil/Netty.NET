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
using System.Globalization;
using static Netty.NET.Handler.Codec.Compression.Bzip2Constants;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * Reads and decompresses a single Bzip2 block.<br><br>
 *
 * Block decoding consists of the following stages:<br>
 * 1. Read block header<br>
 * 2. Read Huffman tables<br>
 * 3. Read and decode Huffman encoded data - {@link #decodeHuffmanData(Bzip2HuffmanStageDecoder)}<br>
 * 4. Run-Length Decoding[2] - {@link #decodeHuffmanData(Bzip2HuffmanStageDecoder)}<br>
 * 5. Inverse Move To Front Transform - {@link #decodeHuffmanData(Bzip2HuffmanStageDecoder)}<br>
 * 6. Inverse Burrows Wheeler Transform - {@link #initialiseInverseBWT()}<br>
 * 7. Run-Length Decoding[1] - {@link #read()}<br>
 * 8. Optional Block De-Randomisation - {@link #read()} (through {@link #decodeNextBWTByte()})
 */
// CLR: the package-private final class is an internal sealed class; its package-private fields are internal fields with
// the Java names. The decoded bytes are only used masked (& 0xff), stored back into byte arrays or compared for equality,
// so the unsigned C# bytes give the Java results. System.arraycopy between two arrays is Array.Copy. The int arithmetic
// (repeatCount, repeatIncrement, the merged pointers) wraps as in Java (the projects are unchecked), which
// Bzip2BlockDecompressorTest relies on, and >>> is C#'s >>>. The numbers in the exception message use the invariant
// culture.
internal sealed class Bzip2BlockDecompressor
{
    /**
     * A reader that provides bit-level reads.
     */
    private readonly Bzip2BitReader reader;

    /**
     * Calculates the block CRC from the fully decoded bytes of the block.
     */
    private readonly Crc32 crc = new Crc32();

    /**
     * The CRC of the current block as read from the block header.
     */
    private readonly int blockCRC;

    /**
     * {@code true} if the current block is randomised, otherwise {@code false}.
     */
    private readonly bool blockRandomised;

    /* Huffman Decoding stage */
    /**
     * The end-of-block Huffman symbol. Decoding of the block ends when this is encountered.
     */
    internal int huffmanEndOfBlockSymbol;

    /**
     * Bitmap, of ranges of 16 bytes, present/not present.
     */
    internal int huffmanInUse16;

    /**
     * A map from Huffman symbol index to output character. Some types of data (e.g. ASCII text)
     * may contain only a limited number of byte values; Huffman symbols are only allocated to
     * those values that actually occur in the uncompressed data.
     */
    internal readonly byte[] huffmanSymbolMap = new byte[256];

    /* Move To Front stage */
    /**
     * Counts of each byte value within the {@link Bzip2BlockDecompressor#huffmanSymbolMap} data.
     * Collected at the Move To Front stage, consumed by the Inverse Burrows Wheeler Transform stage.
     */
    private readonly int[] bwtByteCounts = new int[256];

    /**
     * The Burrows-Wheeler Transform processed data. Read at the Move To Front stage, consumed by the
     * Inverse Burrows Wheeler Transform stage.
     */
    private readonly byte[] bwtBlock;

    /**
     * Starting pointer into BWT for after untransform.
     */
    private readonly int bwtStartPointer;

    /* Inverse Burrows-Wheeler Transform stage */
    /**
     * At each position contains the union of :-
     *   An output character (8 bits)
     *   A pointer from each position to its successor (24 bits, left shifted 8 bits)
     * As the pointer cannot exceed the maximum block size of 900k, 24 bits is more than enough to
     * hold it; Folding the character data into the spare bits while performing the inverse BWT,
     * when both pieces of information are available, saves a large number of memory accesses in
     * the final decoding stages.
     */
    private int[] bwtMergedPointers;

    /**
     * The current merged pointer into the Burrow-Wheeler Transform array.
     */
    private int bwtCurrentMergedPointer;

    /**
     * The actual length in bytes of the current block at the Inverse Burrows Wheeler Transform
     * stage (before final Run-Length Decoding).
     */
    private int bwtBlockLength;

    /**
     * The number of output bytes that have been decoded up to the Inverse Burrows Wheeler Transform stage.
     */
    private int bwtBytesDecoded;

    /* Run-Length Encoding and Random Perturbation stage */
    /**
     * The most recently RLE decoded byte.
     */
    private int rleLastDecodedByte = -1;

    /**
     * The number of previous identical output bytes decoded. After 4 identical bytes, the next byte
     * decoded is an RLE repeat count.
     */
    private int rleAccumulator;

    /**
     * The RLE repeat count of the current decoded byte. When this reaches zero, a new byte is decoded.
     */
    private int rleRepeat;

    /**
     * If the current block is randomised, the position within the RNUMS randomisation array.
     */
    private int randomIndex;

    /**
     * If the current block is randomised, the remaining count at the current RNUMS position.
     */
    private int randomCount = Bzip2Rand.RNums(0) - 1;

    /**
     * Table for Move To Front transformations.
     */
    private readonly Bzip2MoveToFrontTable symbolMTF = new Bzip2MoveToFrontTable();

    // This variables is used to save current state if we haven't got enough readable bits
    private int repeatCount;
    private int repeatIncrement = 1;
    private int mtfValue;

    internal Bzip2BlockDecompressor(int blockSize, int blockCRC, bool blockRandomised,
                                    int bwtStartPointer, Bzip2BitReader reader)
    {

        bwtBlock = new byte[blockSize];

        this.blockCRC = blockCRC;
        this.blockRandomised = blockRandomised;
        this.bwtStartPointer = bwtStartPointer;

        this.reader = reader;
    }

    /**
     * Reads the Huffman encoded data from the input stream, performs Run-Length Decoding and
     * applies the Move To Front transform to reconstruct the Burrows-Wheeler Transform array.
     */
    // CLR: the two nextByte locals of the source (one in the repeat block, one after it) are one local declared before
    // them, because C# forbids a nested local "with the same name as a local variable or constant in an enclosing
    // declaration space" and reports it "regardless of the textual order of the variable declarations"
    // (https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0136).
    internal bool DecodeHuffmanData(Bzip2HuffmanStageDecoder huffmanDecoder)
    {
        Bzip2BitReader reader = this.reader;
        byte[] bwtBlock = this.bwtBlock;
        byte[] huffmanSymbolMap = this.huffmanSymbolMap;
        int streamBlockSize = this.bwtBlock.Length;
        int huffmanEndOfBlockSymbol = this.huffmanEndOfBlockSymbol;
        int[] bwtByteCounts = this.bwtByteCounts;
        Bzip2MoveToFrontTable symbolMTF = this.symbolMTF;

        int bwtBlockLength = this.bwtBlockLength;
        int repeatCount = this.repeatCount;
        int repeatIncrement = this.repeatIncrement;
        int mtfValue = this.mtfValue;

        for (;;)
        {
            if (!reader.HasReadableBits(HUFFMAN_DECODE_MAX_CODE_LENGTH))
            {
                this.bwtBlockLength = bwtBlockLength;
                this.repeatCount = repeatCount;
                this.repeatIncrement = repeatIncrement;
                this.mtfValue = mtfValue;
                return false;
            }

            int nextSymbol = huffmanDecoder.NextSymbol();

            if (nextSymbol == HUFFMAN_SYMBOL_RUNA)
            {
                repeatCount += repeatIncrement;
                repeatIncrement <<= 1;
            }
            else if (nextSymbol == HUFFMAN_SYMBOL_RUNB)
            {
                repeatCount += repeatIncrement << 1;
                repeatIncrement <<= 1;
            }
            else
            {
                byte nextByte;

                if (repeatCount > 0)
                {
                    if (repeatCount > streamBlockSize - bwtBlockLength)
                    {
                        throw new DecompressionException("block exceeds declared block size");
                    }

                    nextByte = huffmanSymbolMap[mtfValue];
                    bwtByteCounts[nextByte & 0xff] += repeatCount;

                    while (--repeatCount >= 0)
                    {
                        bwtBlock[bwtBlockLength++] = nextByte;
                    }

                    repeatCount = 0;
                    repeatIncrement = 1;
                }

                if (nextSymbol == huffmanEndOfBlockSymbol)
                {
                    break;
                }

                if (bwtBlockLength >= streamBlockSize)
                {
                    throw new DecompressionException("block exceeds declared block size");
                }

                mtfValue = symbolMTF.IndexToFront(nextSymbol - 1) & 0xff;

                nextByte = huffmanSymbolMap[mtfValue];
                bwtByteCounts[nextByte & 0xff]++;
                bwtBlock[bwtBlockLength++] = nextByte;
            }
        }

        if (bwtBlockLength > MAX_BLOCK_LENGTH)
        {
            throw new DecompressionException("block length exceeds max block length: "
                    + bwtBlockLength.ToString(CultureInfo.InvariantCulture) + " > "
                    + MAX_BLOCK_LENGTH.ToString(CultureInfo.InvariantCulture));
        }

        this.bwtBlockLength = bwtBlockLength;
        InitialiseInverseBWT();
        return true;
    }

    /**
     * Set up the Inverse Burrows-Wheeler Transform merged pointer array.
     */
    private void InitialiseInverseBWT()
    {
        int bwtStartPointer = this.bwtStartPointer;
        byte[] bwtBlock  = this.bwtBlock;
        int[] bwtMergedPointers = new int[bwtBlockLength];
        int[] characterBase = new int[256];

        if (bwtStartPointer < 0 || bwtStartPointer >= bwtBlockLength)
        {
            throw new DecompressionException("start pointer invalid");
        }

        // Cumulative character counts
        Array.Copy(bwtByteCounts, 0, characterBase, 1, 255);

        for (int i = 2; i <= 255; i++)
        {
            characterBase[i] += characterBase[i - 1];
        }

        // Merged-Array Inverse Burrows-Wheeler Transform
        // Combining the output characters and forward pointers into a single array here, where we
        // have already read both of the corresponding values, cuts down on memory accesses in the
        // final walk through the array
        for (int i = 0; i < bwtBlockLength; i++)
        {
            int value = bwtBlock[i] & 0xff;
            bwtMergedPointers[characterBase[value]++] = (i << 8) + value;
        }

        this.bwtMergedPointers = bwtMergedPointers;
        bwtCurrentMergedPointer = bwtMergedPointers[bwtStartPointer];
    }

    /**
     * Decodes a byte from the final Run-Length Encoding stage, pulling a new byte from the
     * Burrows-Wheeler Transform stage when required.
     * @return The decoded byte, or -1 if there are no more bytes
     */
    public int Read()
    {
        while (rleRepeat < 1)
        {
            if (bwtBytesDecoded == bwtBlockLength)
            {
                return -1;
            }

            int nextByte = DecodeNextBWTByte();
            if (nextByte != rleLastDecodedByte)
            {
                // New byte, restart accumulation
                rleLastDecodedByte = nextByte;
                rleRepeat = 1;
                rleAccumulator = 1;
                crc.UpdateCRC(nextByte);
            }
            else
            {
                if (++rleAccumulator == 4)
                {
                    if (bwtBytesDecoded >= bwtBlockLength)
                    {
                        throw new DecompressionException("malformed RLE: run-length byte missing at end of block");
                    }

                    // Accumulation complete, start repetition
                    int rleRepeat = DecodeNextBWTByte() + 1;
                    this.rleRepeat = rleRepeat;
                    rleAccumulator = 0;
                    crc.UpdateCRC(nextByte, rleRepeat);
                }
                else
                {
                    rleRepeat = 1;
                    crc.UpdateCRC(nextByte);
                }
            }
        }

        rleRepeat--;

        return rleLastDecodedByte;
    }

    /**
     * Decodes a byte from the Burrows-Wheeler Transform stage. If the block has randomisation
     * applied, reverses the randomisation.
     * @return The decoded byte
     */
    private int DecodeNextBWTByte()
    {
        int mergedPointer = bwtCurrentMergedPointer;
        int nextDecodedByte =  mergedPointer & 0xff;
        bwtCurrentMergedPointer = bwtMergedPointers[mergedPointer >>> 8];

        if (blockRandomised)
        {
            if (--randomCount == 0)
            {
                nextDecodedByte ^= 1;
                randomIndex = (randomIndex + 1) % 512;
                randomCount = Bzip2Rand.RNums(randomIndex);
            }
        }

        bwtBytesDecoded++;

        return nextDecodedByte;
    }

    public int BlockLength()
    {
        return bwtBlockLength;
    }

    /**
     * Verify and return the block CRC. This method may only be called
     * after all of the block's bytes have been read.
     * @return The block CRC
     */
    internal int CheckCRC()
    {
        int computedBlockCRC = crc.GetCRC();
        if (blockCRC != computedBlockCRC)
        {
            throw new DecompressionException("block CRC error");
        }

        return computedBlockCRC;
    }
}
