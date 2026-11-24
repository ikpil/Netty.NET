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

using System;
using System.Globalization;
using System.Numerics;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Compression.Bzip2Constants;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * Uncompresses a {@link ByteBuf} encoded with the Bzip2 format.
 *
 * See <a href="https://en.wikipedia.org/wiki/Bzip2">Bzip2</a>.
 */
// CLR: @UnstableApi is the Common [UnstableApi] attribute. The package-private processInput/processOutput override the
// internal members of InputBufferingDecompressor; status() is Status_() (owner decision 2026-11-23: a method named Status
// cannot be declared next to the nested Status enum, "The type 'type name' already contains a definition for
// 'identifier'", https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0102, as HAProxyTLV.type() is Type_() in
// docs/codec-haproxy-porting.md). For the same reason the static builder() is Builder_(): the class also declares the
// nested class Builder. A class does not inherit the nested types of its interfaces, so Status and
// AbstractDecompressorBuilder are written IDecompressor.Status and IDecompressor.AbstractDecompressorBuilder. The
// package-private constructors are internal; C# has no throws clauses; catch (Throwable t) { ...; throw t; } is
// catch (Exception) { ...; throw; }, which rethrows the same exception object. ObjectUtil.checkPositive is the Common
// ObjectUtil.CheckPositive. IllegalStateException is InvalidOperationException (docs/codec-base-porting.md).
[UnstableApi]
public sealed class Bzip2Decompressor : InputBufferingDecompressor
{
    private readonly int outputBufferSize;

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
        DECODED_HUFFMAN_DATA,
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

    internal Bzip2Decompressor(Builder builder, IByteBufAllocator allocator) : base(allocator)
    {
        this.outputBufferSize = builder.outputBufferSize;
    }

    // CLR: the state machine is Bzip2Decoder.decode's, with the same C# adaptations (see the comment there): each
    // "// fall through" is followed by goto case
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/selection-statements), the labeled
    // break loop is goto saveStateCheck (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/jump-statements),
    // the fields are written this.blockDecompressor/this.huffmanStageDecoder before the sections that declare the
    // same-named locals (https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0841), Integer.bitCount is
    // BitOperations.PopCount, and buf.readByte() - '0' gives an invalid block size for every byte of 0x80 or more in
    // both languages (byte-sign spot without an observable difference).
    internal override void ProcessInput(ByteBuf buf)
    {
        reader.SetByteBuf(buf);
        switch (currentState)
        {
            case State.INIT:
                if (buf.ReadableBytes < 4)
                {
                    break;
                }
                int magicNumber = buf.ReadUnsignedMedium();
                if (magicNumber != MAGIC_NUMBER)
                {
                    throw new DecompressionException("Unexpected stream identifier contents. Mismatched bzip2 " +
                            "protocol version?");
                }
                int blockSize = buf.ReadByte() - '0';
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
                    break;
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
                    break;
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
                    break;
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
                    break;
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
                    break;
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
                    break;
                }

                // Finally create the Huffman tables
                huffmanStageDecoder.CreateHuffmanDecodingTables();
                currentState = State.DECODE_HUFFMAN_DATA;
                // fall through
                goto case State.DECODE_HUFFMAN_DATA;
            case State.DECODE_HUFFMAN_DATA:
                blockDecompressor = this.blockDecompressor;
                bool decoded = blockDecompressor.DecodeHuffmanData(this.huffmanStageDecoder);
                if (!decoded)
                {
                    return;
                }
                currentState = State.DECODED_HUFFMAN_DATA;
                break;
        }
    }

    internal override ByteBuf ProcessOutput(ByteBuf buf)
    {
        if (currentState != State.DECODED_HUFFMAN_DATA)
        {
            throw new InvalidOperationException("Not in state NEED_OUTPUT");
        }
        ByteBuf uncompressed = allocator.Buffer(outputBufferSize);
        try
        {
            int uncByte;
            while ((uncByte = blockDecompressor.Read()) >= 0)
            {
                uncompressed.WriteByte(uncByte);
                if (uncompressed.ReadableBytes >= outputBufferSize)
                {
                    break;
                }
            }
            if (uncByte < 0)
            {
                // all data read
                currentState = State.INIT_BLOCK;
                int currentBlockCRC = blockDecompressor.CheckCRC();
                streamCRC = (streamCRC << 1 | streamCRC >>> 31) ^ currentBlockCRC;
            }
            return uncompressed;
        }
        catch (Exception)
        {
            uncompressed.Release();
            throw;
        }
    }

    public override void EndOfInput()
    {
        if (currentState != State.EOF)
        {
            throw new DecompressionException("Unexpected end of input");
        }
    }

    public override IDecompressor.Status Status_()
    {
        switch (currentState)
        {
            case State.DECODED_HUFFMAN_DATA:
                return IDecompressor.Status.NEED_OUTPUT;
            case State.EOF:
                return IDecompressor.Status.COMPLETE;
            default:
                return IDecompressor.Status.NEED_INPUT;
        }
    }

    [UnstableApi]
    public static Builder Builder_()
    {
        return new Builder();
    }

    // CLR: outputBufferSize(int) is OutputBufferSize(int), next to the field outputBufferSize; build(ByteBufAllocator)
    // is Build(IByteBufAllocator) and returns the IDecompressor. The private field outputBufferSize is internal: the
    // enclosing Bzip2Decompressor reads it, and "You can access private members only within the body of the class or the
    // struct where you declare them" (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/private),
    // while Java lets the enclosing class read a private member of its nested class.
    [UnstableApi]
    public sealed class Builder : IDecompressor.AbstractDecompressorBuilder
    {
        internal int outputBufferSize = 65536;

        internal Builder()
        {
        }

        /**
         * Size of the output buffer to return from {@link #takeOutput()}. Default 64K.
         *
         * @param outputBufferSize Output buffer size
         * @return This builder
         */
        [UnstableApi]
        public Builder OutputBufferSize(int outputBufferSize)
        {
            this.outputBufferSize = ObjectUtil.CheckPositive(outputBufferSize, "outputBufferSize");
            return this;
        }

        [UnstableApi]
        public override IDecompressor Build(IByteBufAllocator allocator)
        {
            return new DefensiveDecompressor(new Bzip2Decompressor(this, allocator));
        }
    }
}
