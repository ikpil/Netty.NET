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
using System.IO.Hashing;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Compression.FastLz;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * Compresses a {@link ByteBuf} using the FastLZ algorithm.
 *
 * See <a href="https://github.com/netty/netty/issues/2750">FastLZ format</a>.
 */
// CLR: owner decision 2026-11-23: java.util.zip.Checksum is System.IO.Hashing.NonCryptographicHashAlgorithm and
// java.util.zip.Adler32 is System.IO.Hashing.Adler32 of the package 11.0.0
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.adler32?view=net-10.0-pp), so the constructor that
// takes a Checksum takes a NonCryptographicHashAlgorithm and wraps it with ByteBufChecksum.WrapChecksum;
// reset()/update(ByteBuf, int, int)/getValue() are Reset()/Update/GetValue(). The Class argument of the
// MessageToByteEncoder constructor is a Type (MessageToByteEncoder in codec-base), encode(ctx, in, out) is
// Encode(IChannelHandlerContext, ByteBuf, ByteBuf) and C# has no throws clauses. IllegalArgumentException is
// ArgumentException (docs/codec-base-porting.md) and String.format("%d") is string.Format with the invariant culture.
public class FastLzFrameEncoder : MessageToByteEncoder<ByteBuf>
{
    /**
     * Compression level.
     */
    private readonly int level;

    /**
     * Underlying checksum calculator in use.
     */
    private readonly ByteBufChecksum checksum;

    /**
     * Creates a FastLZ encoder without checksum calculator and with auto detection of compression level.
     */
    public FastLzFrameEncoder() : this(LEVEL_AUTO, null)
    {
    }

    /**
     * Creates a FastLZ encoder with specified compression level and without checksum calculator.
     *
     * @param level supports only these values:
     *        0 - Encoder will choose level automatically depending on the length of the input buffer.
     *        1 - Level 1 is the fastest compression and generally useful for short data.
     *        2 - Level 2 is slightly slower but it gives better compression ratio.
     */
    public FastLzFrameEncoder(int level) : this(level, null)
    {
    }

    /**
     * Creates a FastLZ encoder with auto detection of compression
     * level and calculation of checksums as specified.
     *
     * @param validateChecksums
     *        If true, the checksum of each block will be calculated and this value
     *        will be added to the header of block.
     *        By default {@link FastLzFrameEncoder} uses {@link java.util.zip.Adler32}
     *        for checksum calculation.
     */
    public FastLzFrameEncoder(bool validateChecksums) : this(LEVEL_AUTO, validateChecksums ? new Adler32() : null)
    {
    }

    /**
     * Creates a FastLZ encoder with specified compression level and checksum calculator.
     *
     * @param level supports only these values:
     *        0 - Encoder will choose level automatically depending on the length of the input buffer.
     *        1 - Level 1 is the fastest compression and generally useful for short data.
     *        2 - Level 2 is slightly slower but it gives better compression ratio.
     * @param checksum
     *        the {@link Checksum} instance to use to check data for integrity.
     *        You may set {@code null} if you don't want to validate checksum of each block.
     */
    public FastLzFrameEncoder(int level, NonCryptographicHashAlgorithm checksum) : base(typeof(ByteBuf))
    {
        if (level != LEVEL_AUTO && level != LEVEL_1 && level != LEVEL_2)
        {
            throw new ArgumentException(string.Format(CultureInfo.InvariantCulture,
                    "level: {0} (expected: {1} or {2} or {3})", level, LEVEL_AUTO, LEVEL_1, LEVEL_2));
        }
        this.level = level;
        this.checksum = checksum == null ? null : ByteBufChecksum.WrapChecksum(checksum);
    }

    protected override void Encode(IChannelHandlerContext ctx, ByteBuf @in, ByteBuf @out)
    {
        ByteBufChecksum checksum = this.checksum;

        for (;;)
        {
            if (!@in.IsReadable())
            {
                return;
            }
            int idx = @in.ReaderIndex;
            int length = Math.Min(@in.ReadableBytes, MAX_CHUNK_LENGTH);

            int outputIdx = @out.WriterIndex;
            @out.SetMedium(outputIdx, MAGIC_NUMBER);
            int outputOffset = outputIdx + CHECKSUM_OFFSET + (checksum != null ? 4 : 0);

            byte blockType;
            int chunkLength;
            if (length < MIN_LENGTH_TO_COMPRESSION)
            {
                blockType = BLOCK_TYPE_NON_COMPRESSED;

                @out.EnsureWritable(outputOffset + 2 + length);
                int outputPtr = outputOffset + 2;

                if (checksum != null)
                {
                    checksum.Reset();
                    checksum.Update(@in, idx, length);
                    @out.SetInt(outputIdx + CHECKSUM_OFFSET, (int) checksum.GetValue());
                }
                @out.SetBytes(outputPtr, @in, idx, length);
                chunkLength = length;
            }
            else
            {
                // try to compress
                if (checksum != null)
                {
                    checksum.Reset();
                    checksum.Update(@in, idx, length);
                    @out.SetInt(outputIdx + CHECKSUM_OFFSET, (int) checksum.GetValue());
                }

                int maxOutputLength = CalculateOutputBufferLength(length);
                @out.EnsureWritable(outputOffset + 4 + maxOutputLength);
                int outputPtr = outputOffset + 4;
                int compressedLength = Compress(@in, @in.ReaderIndex, length, @out, outputPtr, level);

                if (compressedLength < length)
                {
                    blockType = BLOCK_TYPE_COMPRESSED;
                    chunkLength = compressedLength;

                    @out.SetShort(outputOffset, chunkLength);
                    outputOffset += 2;
                }
                else
                {
                    blockType = BLOCK_TYPE_NON_COMPRESSED;
                    @out.SetBytes(outputOffset + 2, @in, idx, length);
                    chunkLength = length;
                }
            }
            @out.SetShort(outputOffset, length);

            @out.SetByte(outputIdx + OPTIONS_OFFSET,
                    blockType | (checksum != null ? BLOCK_WITH_CHECKSUM : BLOCK_WITHOUT_CHECKSUM));
            @out.WriterIndex = outputOffset + 2 + chunkLength;
            @in.SkipBytes(length);
        }
    }
}
