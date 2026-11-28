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
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Compression.Bzip2Constants;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * Uncompresses a {@link ByteBuf} encoded with the Bzip2 format.
 *
 * See <a href="https://en.wikipedia.org/wiki/Bzip2">Bzip2</a>.
 */
// CLR: IllegalStateException is InvalidOperationException. The private enum State is a nested C# enum. isClosed()
// is IsClosed().
public class Bzip2Decoder : ByteToMessageDecoder
{
    /**
     * Current state of stream.
     */
    private enum State
    {
        INIT,
        INIT_BLOCK,
        INIT_BLOCK_PARAMS,
        RECEIVE_HUFFMAN_USED_MAP,
        RECEIVE_HUFFMAN_USED_BITMAPS,
        RECEIVE_SELECTORS_NUMBER,
        RECEIVE_SELECTORS,
        RECEIVE_HUFFMAN_LENGTH,
        DECODE_HUFFMAN_DATA,
        EOF
    }
    private State currentState = State.INIT;

    /**
     * A reader that provides bit-level reads.
     */
    private readonly Bzip2BitReader reader = new Bzip2BitReader();

    /**
     * The decompressor for the current block.
     */
    private Bzip2BlockDecompressor blockDecompressor;

    /**
     * Bzip2 Huffman coding stage.
     */
    private Bzip2HuffmanStageDecoder huffmanStageDecoder;

    /**
     * Always: in the range 0 .. 9. The current block size is 100000 * this number.
     */
    private int blockSize;

    /**
     * The CRC of the current block as read from the block header.
     */
    private int blockCRC;

    /**
     * The merged CRC of all blocks decompressed so far.
     */
    private int streamCRC;

    // CLR: the Java switch falls through from state to state; C# switch sections cannot ("control can't fall through
    // from one switch section to the next"; "To imitate the fall-through behavior and pass control to other switch
    // section, you can use the goto statement",
    // https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/selection-statements), so each
    // "// fall through" is followed by goto case. The labeled break loop of RECEIVE_HUFFMAN_LENGTH is goto
    // saveStateCheck, a label on the statement after the loop: C# 14 has no labeled break, and "you can use the goto
    // statement to exit a nested loop" (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/jump-statements);
    // the unlabeled break of the outer loop reaches the same statement. A C# local declared in a switch section is in
    // scope in the whole switch block, so before the section that declares the locals blockDecompressor and
    // huffmanStageDecoder the fields are written this.blockDecompressor and this.huffmanStageDecoder (Java's local scope
    // starts at the declaration; "Cannot use local variable 'name' before it is declared",
    // https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0841). Integer.bitCount is BitOperations.PopCount over the
    // same 32 bits ("Returns the number of one-bits in the two's complement binary representation",
    // https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Integer.html; "Returns the population count
    // (number of bits set) of a mask", https://learn.microsoft.com/en-us/dotnet/api/system.numerics.bitoperations.popcount?view=net-10.0).
    // Byte-sign spots: in.readByte() - '0' is a signed byte in Java and the unsigned octet in C#; a byte of 0x80 or more
    // gives -176..-49 in Java and 80..207 in C#, both outside 1..9, so the same "block size is invalid" exception
    // follows. (byte) k stores 0..255 with the same eight bits, and (byte) currLength keeps the eight bits that
    // Bzip2HuffmanStageDecoder reads back as sbyte. The numbers in the selector message use the invariant culture.
    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        if (!@in.IsReadable())
        {
            return;
        }

        Bzip2BitReader reader = this.reader;
        reader.SetByteBuf(@in);

        for (;;)
        {
            switch (currentState)
            {
            case State.INIT:
                if (@in.ReadableBytes < 4)
                {
                    return;
                }

                int magicNumber = @in.ReadUnsignedMedium();
                if (magicNumber != MAGIC_NUMBER)
                {
                    throw new DecompressionException("Unexpected stream identifier contents. Mismatched bzip2 " +
                            "protocol version?");
                }

                int blockSize = @in.ReadByte() - '0';
                if (blockSize < MIN_BLOCK_SIZE || blockSize > MAX_BLOCK_SIZE)
                {
                    throw new DecompressionException("block size is invalid");
                }

                this.blockSize = blockSize * BASE_BLOCK_SIZE;

                streamCRC = 0;
                currentState = State.INIT_BLOCK;
                 // fall through
                goto case State.INIT_BLOCK;
            case State.INIT_BLOCK:
                if (!reader.HasReadableBytes(10))
                {
                    return;
                }

                // Get the block magic bytes.
                int magic1 = reader.ReadBits(24);
                int magic2 = reader.ReadBits(24);
                if (magic1 == END_OF_STREAM_MAGIC_1 && magic2 == END_OF_STREAM_MAGIC_2)
                {
                    // End of stream was reached. Check the combined CRC.
                    int storedCombinedCRC = reader.ReadInt();
                    if (storedCombinedCRC != streamCRC)
                    {
                        throw new DecompressionException("stream CRC error");
                    }

                    currentState = State.EOF;
                    break;
                }

                if (magic1 != BLOCK_HEADER_MAGIC_1 || magic2 != BLOCK_HEADER_MAGIC_2)
                {
                    throw new DecompressionException("bad block header");
                }

                blockCRC = reader.ReadInt();
                currentState = State.INIT_BLOCK_PARAMS;
                // fall through
                goto case State.INIT_BLOCK_PARAMS;
            case State.INIT_BLOCK_PARAMS:
                if (!reader.HasReadableBits(25))
                {
                    return;
                }

                bool blockRandomised = reader.ReadBoolean();
                int bwtStartPointer = reader.ReadBits(24);

                this.blockDecompressor = new Bzip2BlockDecompressor(this.blockSize, blockCRC,
                                                                     blockRandomised, bwtStartPointer, reader);
                currentState = State.RECEIVE_HUFFMAN_USED_MAP;
                // fall through
                goto case State.RECEIVE_HUFFMAN_USED_MAP;
            case State.RECEIVE_HUFFMAN_USED_MAP:
                if (!reader.HasReadableBits(16))
                {
                    return;
                }

                this.blockDecompressor.huffmanInUse16 = reader.ReadBits(16);
                currentState = State.RECEIVE_HUFFMAN_USED_BITMAPS;
                // fall through
                goto case State.RECEIVE_HUFFMAN_USED_BITMAPS;
            case State.RECEIVE_HUFFMAN_USED_BITMAPS:
                Bzip2BlockDecompressor blockDecompressor = this.blockDecompressor;
                int inUse16 = blockDecompressor.huffmanInUse16;
                int bitNumber = BitOperations.PopCount((uint) inUse16);
                byte[] huffmanSymbolMap = blockDecompressor.huffmanSymbolMap;

                if (!reader.HasReadableBits(bitNumber * HUFFMAN_SYMBOL_RANGE_SIZE + 3))
                {
                    return;
                }

                int huffmanSymbolCount = 0;

                if (bitNumber > 0)
                {
                    for (int i = 0; i < 16; i++)
                    {
                        if ((inUse16 & 1 << 15 >>> i) != 0)
                        {
                            for (int j = 0, k = i << 4; j < HUFFMAN_SYMBOL_RANGE_SIZE; j++, k++)
                            {
                                if (reader.ReadBoolean())
                                {
                                    huffmanSymbolMap[huffmanSymbolCount++] = (byte) k;
                                }
                            }
                        }
                    }
                }

                blockDecompressor.huffmanEndOfBlockSymbol = huffmanSymbolCount + 1;

                int totalTables = reader.ReadBits(3);
                if (totalTables < HUFFMAN_MINIMUM_TABLES || totalTables > HUFFMAN_MAXIMUM_TABLES)
                {
                    throw new DecompressionException("incorrect huffman groups number");
                }

                int alphaSize = huffmanSymbolCount + 2;
                if (alphaSize > HUFFMAN_MAX_ALPHABET_SIZE)
                {
                    throw new DecompressionException("incorrect alphabet size");
                }

                this.huffmanStageDecoder = new Bzip2HuffmanStageDecoder(reader, totalTables, alphaSize);
                currentState = State.RECEIVE_SELECTORS_NUMBER;
                // fall through
                goto case State.RECEIVE_SELECTORS_NUMBER;
            case State.RECEIVE_SELECTORS_NUMBER:
                if (!reader.HasReadableBits(15))
                {
                    return;
                }

                int totalSelectors = reader.ReadBits(15);
                if (totalSelectors < 1 || totalSelectors > MAX_SELECTORS)
                {
                    throw new DecompressionException("incorrect selectors number");
                }

                this.huffmanStageDecoder.selectors = new byte[totalSelectors];

                currentState = State.RECEIVE_SELECTORS;
                // fall through
                goto case State.RECEIVE_SELECTORS;
            case State.RECEIVE_SELECTORS:
                Bzip2HuffmanStageDecoder huffmanStageDecoder = this.huffmanStageDecoder;
                byte[] selectors = huffmanStageDecoder.selectors;
                totalSelectors = selectors.Length;
                Bzip2MoveToFrontTable tableMtf = huffmanStageDecoder.tableMTF;

                int currSelector;

                // Get zero-terminated bit runs (0..62) of MTF'ed Huffman table. length = 1..6
                for (currSelector = huffmanStageDecoder.currentSelector;
                            currSelector < totalSelectors; currSelector++)
                {
                    if (!reader.HasReadableBits(HUFFMAN_SELECTOR_LIST_MAX_LENGTH))
                    {
                        // Save state if end of current ByteBuf was reached
                        huffmanStageDecoder.currentSelector = currSelector;
                        return;
                    }

                    int index = 0;

                    while (reader.ReadBoolean())
                    {
                        index++;
                        if (index >= huffmanStageDecoder.totalTables)
                        {
                            throw new DecompressionException("incorrect selector index " +
                                    index.ToString(CultureInfo.InvariantCulture));
                        }
                    }

                    selectors[currSelector] = tableMtf.IndexToFront(index);
                }

                currentState = State.RECEIVE_HUFFMAN_LENGTH;
                // fall through
                goto case State.RECEIVE_HUFFMAN_LENGTH;
            case State.RECEIVE_HUFFMAN_LENGTH:
                huffmanStageDecoder = this.huffmanStageDecoder;
                totalTables = huffmanStageDecoder.totalTables;
                byte[][] codeLength = huffmanStageDecoder.tableCodeLengths;
                alphaSize = huffmanStageDecoder.alphabetSize;

                /* Now the coding tables */
                int currGroup;
                int currLength = huffmanStageDecoder.currentLength;
                int currAlpha = 0;
                bool modifyLength = huffmanStageDecoder.modifyLength;
                bool saveStateAndReturn = false;

                for (currGroup = huffmanStageDecoder.currentGroup; currGroup < totalTables; currGroup++)
                {
                    // start_huffman_length
                    if (!reader.HasReadableBits(5))
                    {
                        saveStateAndReturn = true;
                        break;
                    }

                    if (currLength < 0)
                    {
                        currLength = reader.ReadBits(5);
                    }

                    for (currAlpha = huffmanStageDecoder.currentAlpha; currAlpha < alphaSize; currAlpha++)
                    {
                        // delta_bit_length: 1..40
                        if (!reader.IsReadable())
                        {
                            saveStateAndReturn = true;
                            goto saveStateCheck;
                        }

                        while (modifyLength || reader.ReadBoolean())  // 0=>next symbol; 1=>alter length
                        {
                            if (!reader.IsReadable())
                            {
                                modifyLength = true;
                                saveStateAndReturn = true;
                                goto saveStateCheck;
                            }

                            // 1=>decrement length;  0=>increment length
                            currLength += reader.ReadBoolean() ? -1 : 1;
                            modifyLength = false;

                            if (!reader.IsReadable())
                            {
                                saveStateAndReturn = true;
                                goto saveStateCheck;
                            }
                        }

                        codeLength[currGroup][currAlpha] = (byte) currLength;
                    }

                    currLength = -1;
                    currAlpha = huffmanStageDecoder.currentAlpha = 0;
                    modifyLength = false;
                }
                saveStateCheck:
                if (saveStateAndReturn)
                {
                    // Save state if end of current ByteBuf was reached
                    huffmanStageDecoder.currentGroup = currGroup;
                    huffmanStageDecoder.currentLength = currLength;
                    huffmanStageDecoder.currentAlpha = currAlpha;
                    huffmanStageDecoder.modifyLength = modifyLength;
                    return;
                }

                // Finally create the Huffman tables
                huffmanStageDecoder.CreateHuffmanDecodingTables();
                currentState = State.DECODE_HUFFMAN_DATA;
                // fall through
                goto case State.DECODE_HUFFMAN_DATA;
            case State.DECODE_HUFFMAN_DATA:
                blockDecompressor = this.blockDecompressor;
                int oldReaderIndex = @in.ReaderIndex;
                bool decoded = blockDecompressor.DecodeHuffmanData(this.huffmanStageDecoder);
                if (!decoded)
                {
                    return;
                }

                // It used to avoid "Bzip2Decoder.decode() did not read anything but decoded a message" exception.
                // Because previous operation may read only a few bits from Bzip2BitReader.bitBuffer and
                // don't read incoming ByteBuf.
                if (@in.ReaderIndex == oldReaderIndex && @in.IsReadable())
                {
                    reader.Refill();
                }

                int blockLength = blockDecompressor.BlockLength();
                ByteBuf uncompressed = ctx.Alloc().Buffer(blockLength);

                try
                {
                    int uncByte;

                    while ((uncByte = blockDecompressor.Read()) >= 0)
                    {
                        uncompressed.WriteByte(uncByte);
                    }

                    // We did read all the data, lets reset the state and do the CRC check.
                    currentState = State.INIT_BLOCK;
                    int currentBlockCRC = blockDecompressor.CheckCRC();
                    streamCRC = (streamCRC << 1 | streamCRC >>> 31) ^ currentBlockCRC;

                    @out.Add(uncompressed);
                    uncompressed = null;
                }
                finally
                {
                    if (uncompressed != null)
                    {
                        uncompressed.Release();
                    }
                }

                // Return here so the ByteBuf that was put in the List will be forwarded to the user and so can be
                // released as soon as possible.
                return;
            case State.EOF:
                @in.SkipBytes(@in.ReadableBytes);
                return;
            default:
                throw new InvalidOperationException();
            }
        }
    }

    /**
     * Returns {@code true} if and only if the end of the compressed stream
     * has been reached.
     */
    public bool IsClosed()
    {
        return currentState == State.EOF;
    }
}
