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
 * Compresses and writes a single Bzip2 block.<br><br>
 *
 * Block encoding consists of the following stages:<br>
 * 1. Run-Length Encoding[1] - {@link #write(int)}<br>
 * 2. Burrows Wheeler Transform - {@link #close(ByteBuf)} (through {@link Bzip2DivSufSort})<br>
 * 3. Write block header - {@link #close(ByteBuf)}<br>
 * 4. Move To Front Transform - {@link #close(ByteBuf)} (through {@link Bzip2HuffmanStageEncoder})<br>
 * 5. Run-Length Encoding[2] - {@link #close(ByteBuf)}  (through {@link Bzip2HuffmanStageEncoder})<br>
 * 6. Create and write Huffman tables - {@link #close(ByteBuf)} (through {@link Bzip2HuffmanStageEncoder})<br>
 * 7. Huffman encode and write data - {@link #close(ByteBuf)} (through {@link Bzip2HuffmanStageEncoder})
 */
// CLR: The methods keep the Java names: write is Write (both overloads), close(ByteBuf) is Close(ByteBuf), and
// crc() is Crc(), next to the field crc. (byte) value and (byte) runLength store values 0..255 with the same eight
// bits in the unsigned C# byte.
internal sealed class Bzip2BlockCompressor
{
    // CLR: the anonymous ByteProcessor is a Func<byte, bool> (the Buffer port's forEachByte processor, as in
    // ByteBufChecksum); it is created in the constructor because a C# field initializer cannot reference this.
    // forEachByte passes a signed Java byte (-128..127) to write(int), the Buffer port an unsigned byte (0..255). write
    // only compares values with each other and passes rleCurrentValue & 0xff on, so both give the same block (byte-sign
    // spot without an observable difference).
    private readonly Func<byte, bool> writeProcessor;

    /**
     * A writer that provides bit-level writes.
     */
    private readonly Bzip2BitWriter writer;

    /**
     * CRC builder for the block.
     */
    private readonly Crc32 crc = new Crc32();

    /**
     * The RLE'd block data.
     */
    private readonly byte[] block;

    /**
     * Current length of the data within the {@link #block} array.
     */
    private int blockLength;

    /**
     * A limit beyond which new data will not be accepted into the block.
     */
    private readonly int blockLengthLimit;

    /**
     * The values that are present within the RLE'd block data. For each index, {@code true} if that
     * value is present within the data, otherwise {@code false}.
     */
    private readonly bool[] blockValuesPresent = new bool[256];

    /**
     * The Burrows Wheeler Transformed block data.
     */
    private readonly int[] bwtBlock;

    /**
     * The current RLE value being accumulated (undefined when {@link #rleLength} is 0).
     */
    private int rleCurrentValue = -1;

    /**
     * The repeat count of the current RLE value.
     */
    private int rleLength;

    /**
     * @param writer The {@link Bzip2BitWriter} which provides bit-level writes
     * @param blockSize The declared block size in bytes. Up to this many bytes will be accepted
     *                  into the block after Run-Length Encoding is applied
     */
    internal Bzip2BlockCompressor(Bzip2BitWriter writer, int blockSize)
    {
        writeProcessor = value => Write(value);
        this.writer = writer;

        // One extra byte is added to allow for the block wrap applied in close()
        block = new byte[blockSize + 1];
        bwtBlock = new int[blockSize + 1];
        blockLengthLimit = blockSize - 6; // 5 bytes for one RLE run plus one byte - see {@link #write(int)}
    }

    /**
     * Write the Huffman symbol to output byte map.
     */
    private void WriteSymbolMap(ByteBuf @out)
    {
        Bzip2BitWriter writer = this.writer;

        bool[] blockValuesPresent = this.blockValuesPresent;
        bool[] condensedInUse = new bool[16];

        for (int i = 0; i < condensedInUse.Length; i++)
        {
            for (int j = 0, k = i << 4; j < HUFFMAN_SYMBOL_RANGE_SIZE; j++, k++)
            {
                if (blockValuesPresent[k])
                {
                    condensedInUse[i] = true;
                    break;
                }
            }
        }

        foreach (bool isCondensedInUse in condensedInUse)
        {
            writer.WriteBoolean(@out, isCondensedInUse);
        }

        for (int i = 0; i < condensedInUse.Length; i++)
        {
            if (condensedInUse[i])
            {
                for (int j = 0, k = i << 4; j < HUFFMAN_SYMBOL_RANGE_SIZE; j++, k++)
                {
                    writer.WriteBoolean(@out, blockValuesPresent[k]);
                }
            }
        }
    }

    /**
     * Writes an RLE run to the block array, updating the block CRC and present values array as required.
     * @param value The value to write
     * @param runLength The run length of the value to write
     */
    private void WriteRun(int value, int runLength)
    {
        int blockLength = this.blockLength;
        byte[] block = this.block;

        blockValuesPresent[value] = true;
        crc.UpdateCRC(value, runLength);

        byte byteValue = (byte) value;

        switch (runLength)
        {
            case 1:
                block[blockLength] = byteValue;
                this.blockLength = blockLength + 1;
                break;
            case 2:
                block[blockLength] = byteValue;
                block[blockLength + 1] = byteValue;
                this.blockLength = blockLength + 2;
                break;
            case 3:
                block[blockLength] = byteValue;
                block[blockLength + 1] = byteValue;
                block[blockLength + 2] = byteValue;
                this.blockLength = blockLength + 3;
                break;
            default:
                runLength -= 4;
                blockValuesPresent[runLength] = true;
                block[blockLength] = byteValue;
                block[blockLength + 1] = byteValue;
                block[blockLength + 2] = byteValue;
                block[blockLength + 3] = byteValue;
                block[blockLength + 4] = (byte) runLength;
                this.blockLength = blockLength + 5;
                break;
        }
    }

    /**
     * Writes a byte to the block, accumulating to an RLE run where possible.
     * @param value The byte to write
     * @return {@code true} if the byte was written, or {@code false} if the block is already full
     */
    internal bool Write(int value)
    {
        if (blockLength > blockLengthLimit)
        {
            return false;
        }

        int rleCurrentValue = this.rleCurrentValue;
        int rleLength = this.rleLength;

        if (rleLength == 0)
        {
            this.rleCurrentValue = value;
            this.rleLength = 1;
        }
        else if (rleCurrentValue != value)
        {
            // This path commits us to write 6 bytes - one RLE run (5 bytes) plus one extra
            WriteRun(rleCurrentValue & 0xff, rleLength);
            this.rleCurrentValue = value;
            this.rleLength = 1;
        }
        else
        {
            if (rleLength == 254)
            {
                WriteRun(rleCurrentValue & 0xff, 255);
                this.rleLength = 0;
            }
            else
            {
                this.rleLength = rleLength + 1;
            }
        }

        return true;
    }

    /**
     * Writes an array to the block.
     * @param buffer The buffer to write
     * @param offset The offset within the input data to write from
     * @param length The number of bytes of input data to write
     * @return The actual number of input bytes written. May be less than the number requested, or
     *         zero if the block is already full
     */
    internal int Write(ByteBuf buffer, int offset, int length)
    {
        int index = buffer.ForEachByte(offset, length, writeProcessor);
        return index == -1 ? length : index - offset;
    }

    /**
     * Compresses and writes out the block.
     */
    internal void Close(ByteBuf @out)
    {
        // If an RLE run is in progress, write it out
        if (rleLength > 0)
        {
            WriteRun(rleCurrentValue & 0xff, rleLength);
        }

        // Apply a one byte block wrap required by the BWT implementation
        block[blockLength] = block[0];

        // Perform the Burrows Wheeler Transform
        Bzip2DivSufSort divSufSort = new Bzip2DivSufSort(block, bwtBlock, blockLength);
        int bwtStartPointer = divSufSort.Bwt();

        Bzip2BitWriter writer = this.writer;

        // Write out the block header
        writer.WriteBits(@out, 24, BLOCK_HEADER_MAGIC_1);
        writer.WriteBits(@out, 24, BLOCK_HEADER_MAGIC_2);
        writer.WriteInt(@out, crc.GetCRC());
        writer.WriteBoolean(@out, false); // Randomised block flag. We never create randomised blocks
        writer.WriteBits(@out, 24, bwtStartPointer);

        // Write out the symbol map
        WriteSymbolMap(@out);

        // Perform the Move To Front Transform and Run-Length Encoding[2] stages
        Bzip2MTFAndRLE2StageEncoder mtfEncoder = new Bzip2MTFAndRLE2StageEncoder(bwtBlock, blockLength,
                                                                                  blockValuesPresent);
        mtfEncoder.Encode();

        // Perform the Huffman Encoding stage and write out the encoded data
        Bzip2HuffmanStageEncoder huffmanEncoder = new Bzip2HuffmanStageEncoder(writer,
                mtfEncoder.MtfBlock(),
                mtfEncoder.MtfLength(),
                mtfEncoder.MtfAlphabetSize(),
                mtfEncoder.MtfSymbolFrequencies());
        huffmanEncoder.Encode(@out);
    }

    /**
     * Gets available size of the current block.
     * @return Number of available bytes which can be written
     */
    internal int AvailableSize()
    {
        if (blockLength == 0)
        {
            return blockLengthLimit + 2;
        }

        return blockLengthLimit - blockLength + 1;
    }

    /**
     * Determines if the block is full and ready for compression.
     * @return {@code true} if the block is full, otherwise {@code false}
     */
    internal bool IsFull()
    {
        return blockLength > blockLengthLimit;
    }

    /**
     * Determines if any bytes have been written to the block.
     * @return {@code true} if one or more bytes has been written to the block, otherwise {@code false}
     */
    internal bool IsEmpty()
    {
        return blockLength == 0 && rleLength == 0;
    }

    /**
     * Gets the CRC of the completed block. Only valid after calling {@link #close(ByteBuf)}.
     * @return The block's CRC
     */
    internal int Crc()
    {
        return crc.GetCRC();
    }
}
