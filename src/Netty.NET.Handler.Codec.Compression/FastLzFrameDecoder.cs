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
using System.IO.Hashing;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Compression.FastLz;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * Uncompresses a {@link ByteBuf} encoded by {@link FastLzFrameEncoder} using the FastLZ algorithm.
 *
 * See <a href="https://github.com/netty/netty/issues/2750">FastLZ format</a>.
 */
// CLR: owner decision 2026-11-23: java.util.zip.Checksum is System.IO.Hashing.NonCryptographicHashAlgorithm and
// java.util.zip.Adler32 ("A class that can be used to compute the Adler-32 checksum of a data stream",
// https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/zip/Adler32.html) is System.IO.Hashing.Adler32
// of the package 11.0.0 (https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.adler32?view=net-10.0-pp), so
// the constructor that takes a Checksum takes a NonCryptographicHashAlgorithm and wraps it with
// ByteBufChecksum.WrapChecksum; reset()/update(ByteBuf, int, int)/getValue() are Reset()/Update/GetValue().
// decode(ctx, in, out) is the codec-base Decode(IChannelHandlerContext, ByteBuf, IList<object>) and C# has no throws
// clauses. Java switch fall-through is goto case (docs/codec-memcache-porting.md). The DECOMPRESS_DATA section is a
// nested block: its locals chunkLength and originalLength shadow the fields, and C# would otherwise put them in scope in
// the whole switch block, where the earlier sections assign the fields with the same names. readUnsignedShort() returns
// a ushort; options & 0x01 and options & 0x10 mask the byte, so the unsigned System.Byte gives the Java results.
// IllegalStateException is InvalidOperationException and catch (Exception e) { ...; throw e; } is
// catch (Exception) { ...; throw; } (docs/codec-base-porting.md). String.format("%d") is string.Format with the
// invariant culture.
public class FastLzFrameDecoder : ByteToMessageDecoder
{
    /**
     * Current state of decompression.
     */
    private enum State
    {
        INIT_BLOCK,
        INIT_BLOCK_PARAMS,
        DECOMPRESS_DATA,
        CORRUPTED
    }

    private State currentState = State.INIT_BLOCK;

    /**
     * Underlying checksum calculator in use.
     */
    private readonly ByteBufChecksum checksum;

    /**
     * Length of current received chunk of data.
     */
    private int chunkLength;

    /**
     * Original of current received chunk of data.
     * It is equal to {@link #chunkLength} for non compressed chunks.
     */
    private int originalLength;

    /**
     * Indicates is this chunk compressed or not.
     */
    private bool isCompressed;

    /**
     * Indicates is this chunk has checksum or not.
     */
    private bool hasChecksum;

    /**
     * Checksum value of current received chunk of data which has checksum.
     */
    private int currentChecksum;

    /**
     * Creates the fastest FastLZ decoder without checksum calculation.
     */
    public FastLzFrameDecoder() : this(false)
    {
    }

    /**
     * Creates a FastLZ decoder with calculation of checksums as specified.
     *
     * @param validateChecksums
     *        If true, the checksum field will be validated against the actual
     *        uncompressed data, and if the checksums do not match, a suitable
     *        {@link DecompressionException} will be thrown.
     *        Note, that in this case decoder will use {@link java.util.zip.Adler32}
     *        as a default checksum calculator.
     */
    public FastLzFrameDecoder(bool validateChecksums) : this(validateChecksums ? new Adler32() : null)
    {
    }

    /**
     * Creates a FastLZ decoder with specified checksum calculator.
     *
     * @param checksum
     *        the {@link Checksum} instance to use to check data for integrity.
     *        You may set {@code null} if you do not want to validate checksum of each block.
     */
    public FastLzFrameDecoder(NonCryptographicHashAlgorithm checksum)
    {
        this.checksum = checksum == null ? null : ByteBufChecksum.WrapChecksum(checksum);
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        try
        {
            switch (currentState)
            {
            case State.INIT_BLOCK:
                if (@in.ReadableBytes < 4)
                {
                    break;
                }

                int magic = @in.ReadUnsignedMedium();
                if (magic != MAGIC_NUMBER)
                {
                    throw new DecompressionException("unexpected block identifier");
                }

                byte options = @in.ReadByte();
                isCompressed = (options & 0x01) == BLOCK_TYPE_COMPRESSED;
                hasChecksum = (options & 0x10) == BLOCK_WITH_CHECKSUM;

                currentState = State.INIT_BLOCK_PARAMS;
                // fall through
                goto case State.INIT_BLOCK_PARAMS;
            case State.INIT_BLOCK_PARAMS:
                if (@in.ReadableBytes < 2 + (isCompressed ? 2 : 0) + (hasChecksum ? 4 : 0))
                {
                    break;
                }
                currentChecksum = hasChecksum ? @in.ReadInt() : 0;
                chunkLength = @in.ReadUnsignedShort();
                originalLength = isCompressed ? @in.ReadUnsignedShort() : chunkLength;

                currentState = State.DECOMPRESS_DATA;
                // fall through
                goto case State.DECOMPRESS_DATA;
            case State.DECOMPRESS_DATA:
            {
                int chunkLength = this.chunkLength;
                if (@in.ReadableBytes < chunkLength)
                {
                    break;
                }

                int idx = @in.ReaderIndex;
                int originalLength = this.originalLength;

                ByteBuf output = null;

                try
                {
                    if (isCompressed)
                    {

                        output = ctx.Alloc().Buffer(originalLength);
                        int outputOffset = output.WriterIndex;
                        int decompressedBytes = Decompress(@in, idx, chunkLength,
                                output, outputOffset, originalLength);
                        if (originalLength != decompressedBytes)
                        {
                            throw new DecompressionException(string.Format(CultureInfo.InvariantCulture,
                                    "stream corrupted: originalLength({0}) and actual length({1}) mismatch",
                                    originalLength, decompressedBytes));
                        }
                        output.WriterIndex = output.WriterIndex + decompressedBytes;
                    }
                    else
                    {
                        output = @in.RetainedSlice(idx, chunkLength);
                    }

                    ByteBufChecksum checksum = this.checksum;
                    if (hasChecksum && checksum != null)
                    {
                        checksum.Reset();
                        checksum.Update(output, output.ReaderIndex, output.ReadableBytes);
                        int checksumResult = (int) checksum.GetValue();
                        if (checksumResult != currentChecksum)
                        {
                            throw new DecompressionException(string.Format(CultureInfo.InvariantCulture,
                                    "stream corrupted: mismatching checksum: {0} (expected: {1})",
                                    checksumResult, currentChecksum));
                        }
                    }

                    if (output.ReadableBytes > 0)
                    {
                        @out.Add(output);
                    }
                    else
                    {
                        output.Release();
                    }
                    output = null;
                    @in.SkipBytes(chunkLength);

                    currentState = State.INIT_BLOCK;
                }
                finally
                {
                    if (output != null)
                    {
                        output.Release();
                    }
                }
                break;
            }
            case State.CORRUPTED:
                @in.SkipBytes(@in.ReadableBytes);
                break;
            default:
                throw new InvalidOperationException();
            }
        }
        catch (Exception)
        {
            currentState = State.CORRUPTED;
            throw;
        }
    }
}
