/*
 * Copyright 2012 The Netty Project
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
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Compression.Snappy;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * Uncompresses a {@link ByteBuf} encoded with the Snappy framing format.
 *
 * See <a href="https://github.com/google/snappy/blob/master/framing_format.txt">Snappy framing format</a>.
 *
 * Note that by default, validation of the checksum header in each chunk is
 * DISABLED for performance improvements. If performance is less of an issue,
 * or if you would prefer the safety that checksum validation brings, please
 * use the {@link #SnappyFrameDecoder(boolean)} constructor with the argument
 * set to {@code true}.
 */
// CLR: decode(ctx, in, out) is the codec-base Decode(IChannelHandlerContext, ByteBuf, IList<object>) and C# has no
// throws clauses. getUnsignedByte(idx) is GetByte(idx), which is unsigned in the Buffer port; mapChunkType((byte)
// chunkTypeVal) and checkByte compare bytes of the same type, (byte) 0xff and the 0x80 mask, so the unsigned
// System.Byte gives the results of the signed Java byte. The body of the COMPRESSED_DATA section is a nested
// block: its local checksum would otherwise share the switch block's declaration space with the checksum declared
// in a nested block of the UNCOMPRESSED_DATA section, which C# rejects ("it is not possible to declare a local
// variable... with the same name as a local variable... in an enclosing declaration space",
// https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0136). catch (Exception e) {...; throw e; } is catch
// (Exception) {...; throw; }, which rethrows the same exception. Numbers in exception messages use the invariant
// culture, and Integer.toHexString of the unsigned chunk type is the "x" format.
public class SnappyFrameDecoder : ByteToMessageDecoder
{

    private enum ChunkType
    {
        STREAM_IDENTIFIER,
        COMPRESSED_DATA,
        UNCOMPRESSED_DATA,
        RESERVED_UNSKIPPABLE,
        RESERVED_SKIPPABLE
    }

    private const int SNAPPY_IDENTIFIER_LEN = 6;
    // See https://github.com/google/snappy/blob/1.1.9/framing_format.txt#L95
    private const int MAX_UNCOMPRESSED_DATA_SIZE = 65536 + 4;
    // An uncompressed chunk contains a 4-byte masked checksum followed by the data.
    private const int MIN_UNCOMPRESSED_DATA_SIZE = 4;
    // See https://github.com/google/snappy/blob/1.1.9/framing_format.txt#L82
    private const int MAX_DECOMPRESSED_DATA_SIZE = 65536;
    // See https://github.com/google/snappy/blob/1.1.9/framing_format.txt#L82
    private const int MAX_COMPRESSED_CHUNK_SIZE = 16777216 - 1;
    // A compressed chunk contains a 4-byte masked checksum followed by a Snappy stream.
    private const int MIN_COMPRESSED_CHUNK_SIZE = 5;

    private readonly Snappy snappy = new Snappy();
    private readonly bool validateChecksums;

    private bool started;
    private bool corrupted;
    private int numBytesToSkip;

    /**
     * Creates a new snappy-framed decoder with validation of checksums
     * turned OFF. To turn checksum validation on, please use the alternate
     * {@link #SnappyFrameDecoder(boolean)} constructor.
     */
    public SnappyFrameDecoder() : this(false)
    {
    }

    /**
     * Creates a new snappy-framed decoder with validation of checksums
     * as specified.
     *
     * @param validateChecksums
     *        If true, the checksum field will be validated against the actual
     *        uncompressed data, and if the checksums do not match, a suitable
     *        {@link DecompressionException} will be thrown
     */
    public SnappyFrameDecoder(bool validateChecksums)
    {
        this.validateChecksums = validateChecksums;
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        if (corrupted)
        {
            @in.SkipBytes(@in.ReadableBytes);
            return;
        }

        if (numBytesToSkip != 0)
        {
            // The last chunkType we detected was RESERVED_SKIPPABLE and we still have some bytes to skip.
            int skipBytes = Math.Min(numBytesToSkip, @in.ReadableBytes);
            @in.SkipBytes(skipBytes);
            numBytesToSkip -= skipBytes;

            // Let's return and try again.
            return;
        }

        try
        {
            int idx = @in.ReaderIndex;
            int inSize = @in.ReadableBytes;
            if (inSize < 4)
            {
                // We need to be at least able to read the chunk type identifier (one byte),
                // and the length of the chunk (3 bytes) in order to proceed
                return;
            }

            int chunkTypeVal = @in.GetByte(idx);
            ChunkType chunkType = MapChunkType((byte) chunkTypeVal);
            int chunkLength = @in.GetUnsignedMediumLE(idx + 1);

            switch (chunkType)
            {
                case ChunkType.STREAM_IDENTIFIER:
                    if (chunkLength != SNAPPY_IDENTIFIER_LEN)
                    {
                        throw new DecompressionException("Unexpected length of stream identifier: " +
                                chunkLength.ToString(CultureInfo.InvariantCulture));
                    }

                    if (inSize < 4 + SNAPPY_IDENTIFIER_LEN)
                    {
                        break;
                    }

                    @in.SkipBytes(4);
                    int offset = @in.ReaderIndex;
                    @in.SkipBytes(SNAPPY_IDENTIFIER_LEN);

                    CheckByte(@in.GetByte(offset++), (byte) 's');
                    CheckByte(@in.GetByte(offset++), (byte) 'N');
                    CheckByte(@in.GetByte(offset++), (byte) 'a');
                    CheckByte(@in.GetByte(offset++), (byte) 'P');
                    CheckByte(@in.GetByte(offset++), (byte) 'p');
                    CheckByte(@in.GetByte(offset), (byte) 'Y');

                    started = true;
                    break;
                case ChunkType.RESERVED_SKIPPABLE:
                    if (!started)
                    {
                        throw new DecompressionException("Received RESERVED_SKIPPABLE tag before STREAM_IDENTIFIER");
                    }

                    @in.SkipBytes(4);

                    int skipBytes = Math.Min(chunkLength, @in.ReadableBytes);
                    @in.SkipBytes(skipBytes);

                    if (skipBytes != chunkLength)
                    {
                        // We could skip all bytes, let's store the remaining so we can do so once we receive more
                        // data.
                        numBytesToSkip = chunkLength - skipBytes;
                    }

                    break;
                case ChunkType.RESERVED_UNSKIPPABLE:
                    // The spec mandates that reserved unskippable chunks must immediately
                    // return an error, as we must assume that we cannot decode the stream
                    // correctly
                    throw new DecompressionException(
                            "Found reserved unskippable chunk type: 0x" +
                            chunkTypeVal.ToString("x", CultureInfo.InvariantCulture));
                case ChunkType.UNCOMPRESSED_DATA:
                    if (!started)
                    {
                        throw new DecompressionException("Received UNCOMPRESSED_DATA tag before STREAM_IDENTIFIER");
                    }
                    if (chunkLength > MAX_UNCOMPRESSED_DATA_SIZE)
                    {
                        throw new DecompressionException("Received UNCOMPRESSED_DATA larger than " +
                                MAX_UNCOMPRESSED_DATA_SIZE.ToString(CultureInfo.InvariantCulture) + " bytes");
                    }
                    if (chunkLength < MIN_UNCOMPRESSED_DATA_SIZE)
                    {
                        throw new DecompressionException("Received UNCOMPRESSED_DATA with invalid chunk length: " +
                                chunkLength.ToString(CultureInfo.InvariantCulture));
                    }

                    if (inSize < 4 + chunkLength)
                    {
                        return;
                    }

                    @in.SkipBytes(4);

                    if (validateChecksums)
                    {
                        int checksum = @in.ReadIntLE();
                        ValidateChecksum(checksum, @in, @in.ReaderIndex, chunkLength - 4);
                    }
                    else
                    {
                        @in.SkipBytes(4);
                    }

                    @out.Add(@in.ReadRetainedSlice(chunkLength - 4));
                    break;
                case ChunkType.COMPRESSED_DATA:
                {
                    if (!started)
                    {
                        throw new DecompressionException("Received COMPRESSED_DATA tag before STREAM_IDENTIFIER");
                    }

                    if (chunkLength > MAX_COMPRESSED_CHUNK_SIZE)
                    {
                        throw new DecompressionException("Received COMPRESSED_DATA that contains" +
                                " chunk that exceeds " + MAX_COMPRESSED_CHUNK_SIZE.ToString(CultureInfo.InvariantCulture) +
                                " bytes");
                    }
                    if (chunkLength < MIN_COMPRESSED_CHUNK_SIZE)
                    {
                        throw new DecompressionException("Received COMPRESSED_DATA with invalid chunk length: " +
                                chunkLength.ToString(CultureInfo.InvariantCulture));
                    }

                    if (inSize < 4 + chunkLength)
                    {
                        return;
                    }

                    @in.SkipBytes(4);
                    int checksum = @in.ReadIntLE();

                    int uncompressedSize = snappy.GetPreamble(@in);
                    if (uncompressedSize > MAX_DECOMPRESSED_DATA_SIZE)
                    {
                        throw new DecompressionException("Received COMPRESSED_DATA that contains" +
                                " uncompressed data that exceeds " +
                                MAX_DECOMPRESSED_DATA_SIZE.ToString(CultureInfo.InvariantCulture) + " bytes");
                    }

                    ByteBuf uncompressed = ctx.Alloc().Buffer(uncompressedSize, MAX_DECOMPRESSED_DATA_SIZE);

                    try
                    {
                        if (validateChecksums)
                        {
                            int oldWriterIndex = @in.WriterIndex;

                            try
                            {
                                @in.WriterIndex = @in.ReaderIndex + chunkLength - 4;
                                snappy.Decode(@in, uncompressed);
                            }
                            finally
                            {
                                @in.WriterIndex = oldWriterIndex;
                            }

                            ValidateChecksum(checksum, uncompressed, 0, uncompressed.WriterIndex);
                        }
                        else
                        {
                            snappy.Decode(@in.ReadSlice(chunkLength - 4), uncompressed);
                        }

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

                    snappy.Reset();
                    break;
                }
            }
        }
        catch (Exception)
        {
            corrupted = true;
            throw;
        }
    }

    private static void CheckByte(byte actual, byte expect)
    {
        if (actual != expect)
        {
            throw new DecompressionException("Unexpected stream identifier contents. Mismatched snappy " +
                    "protocol version?");
        }
    }

    /**
     * Decodes the chunk type from the type tag byte.
     *
     * @param type The tag byte extracted from the stream
     * @return The appropriate {@link ChunkType}, defaulting to {@link ChunkType#RESERVED_UNSKIPPABLE}
     */
    private static ChunkType MapChunkType(byte type)
    {
        if (type == 0)
        {
            return ChunkType.COMPRESSED_DATA;
        }
        else if (type == 1)
        {
            return ChunkType.UNCOMPRESSED_DATA;
        }
        else if (type == (byte) 0xff)
        {
            return ChunkType.STREAM_IDENTIFIER;
        }
        else if ((type & 0x80) == 0x80)
        {
            return ChunkType.RESERVED_SKIPPABLE;
        }
        else
        {
            return ChunkType.RESERVED_UNSKIPPABLE;
        }
    }
}
