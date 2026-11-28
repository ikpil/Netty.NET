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
using System.Diagnostics;
using System.Globalization;
using System.IO.Hashing;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Compression.FastLz;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * Uncompresses a {@link ByteBuf} encoded by {@link FastLzFrameEncoder} using the FastLZ algorithm.
 *
 * See <a href="https://github.com/netty/netty/issues/2750">FastLZ format</a>.
 * <p>
 * This API is still in progress as part of the decompression migration tracked by
 * <a href="https://github.com/netty/netty/issues/16743">#16743</a>.
 */
// CLR: Decompressor is IDecompressor, its nested Status enum and AbstractDecompressorBuilder are written
// IDecompressor.Status and IDecompressor.AbstractDecompressorBuilder (a class does not inherit the nested types of
// its interfaces), and status() is Status_() (owner decision, as the core's IDecompressor). The package-private
// constructor, processInput and processOutput are internal, as in InputBufferingDecompressor. builder() is
// Builder_(): a method named Builder cannot be declared next to the nested Builder class
// (https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0102), and the nested type keeps its Java name, as
// HAProxyTLV.type() is Type_(). Owner decision 2026-11-23: java.util.zip.Checksum is
// System.IO.Hashing.NonCryptographicHashAlgorithm and java.util.zip.Adler32 is System.IO.Hashing.Adler32
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.adler32?view=net-10.0-pp), wrapped with
// ByteBufChecksum.WrapChecksum. Java switch fall-through is goto case. IllegalStateException is
// InvalidOperationException, and AssertionError, a java.lang.Error, is UnreachableException. readUnsignedShort()
// returns a ushort; options & 0x01 and options & 0x10 mask the byte, so the unsigned System.Byte gives the Java
// results. String.format("%d") is string.Format with the invariant culture. C# has no throws clauses.
[UnstableApi]
public sealed class FastLzFrameDecompressor : InputBufferingDecompressor
{
    /**
     * Current state of decompression.
     */
    private enum State
    {
        INIT_BLOCK,
        INIT_BLOCK_PARAMS,
        DECOMPRESS_DATA,
        COMPLETE
    }

    private State currentState = State.INIT_BLOCK;

    /**
     * Underlying checksum calculator in use.
     */
    private readonly ByteBufChecksum checksum;

    /**
     * Length of the current received chunk of data.
     */
    private int chunkLength;

    /**
     * Original length of the current received chunk of data.
     * It is equal to {@link #chunkLength} for uncompressed chunks.
     */
    private int originalLength;

    /**
     * Indicates whether this chunk is compressed.
     */
    private bool isCompressed;

    /**
     * Indicates whether this chunk has a checksum.
     */
    private bool hasChecksum;

    /**
     * Checksum value of the current received chunk of data when present.
     */
    private int currentChecksum;

    internal FastLzFrameDecompressor(Builder builder, IByteBufAllocator allocator) : base(allocator)
    {
        this.checksum = builder.checksum == null ? null : ByteBufChecksum.WrapChecksum(builder.checksum);
    }

    internal override void ProcessInput(ByteBuf buf)
    {
        switch (currentState)
        {
            case State.INIT_BLOCK:
                if (buf.ReadableBytes < 4)
                {
                    break;
                }

                int magic = buf.ReadUnsignedMedium();
                if (magic != MAGIC_NUMBER)
                {
                    throw new DecompressionException("unexpected block identifier");
                }

                byte options = buf.ReadByte();
                isCompressed = (options & 0x01) == BLOCK_TYPE_COMPRESSED;
                hasChecksum = (options & 0x10) == BLOCK_WITH_CHECKSUM;

                currentState = State.INIT_BLOCK_PARAMS;
                // fall through
                goto case State.INIT_BLOCK_PARAMS;
            case State.INIT_BLOCK_PARAMS:
                if (buf.ReadableBytes < 2 + (isCompressed ? 2 : 0) + (hasChecksum ? 4 : 0))
                {
                    break;
                }

                currentChecksum = hasChecksum ? buf.ReadInt() : 0;
                chunkLength = buf.ReadUnsignedShort();
                originalLength = isCompressed ? buf.ReadUnsignedShort() : chunkLength;

                currentState = State.DECOMPRESS_DATA;
                // fall through
                goto case State.DECOMPRESS_DATA;
            case State.DECOMPRESS_DATA:
                break;
            default:
                throw new InvalidOperationException();
        }
    }

    public override IDecompressor.Status Status_()
    {
        switch (currentState)
        {
            case State.INIT_BLOCK:
            case State.INIT_BLOCK_PARAMS:
                return IDecompressor.Status.NEED_INPUT;
            case State.DECOMPRESS_DATA:
                if (Available() < chunkLength)
                {
                    return IDecompressor.Status.NEED_INPUT;
                }
                else
                {
                    return IDecompressor.Status.NEED_OUTPUT;
                }
            case State.COMPLETE:
                return IDecompressor.Status.COMPLETE;
            default:
                throw new UnreachableException("Unknown state: " + currentState);
        }
    }

    public override void EndOfInput()
    {
        if (currentState != State.INIT_BLOCK)
        {
            throw new DecompressionException("Unexpected end of input");
        }

        currentState = State.COMPLETE;
    }

    internal override ByteBuf ProcessOutput(ByteBuf @in)
    {
        int chunkLength = this.chunkLength;
        if (@in.ReadableBytes < chunkLength)
        {
            throw new InvalidOperationException("Not in state NEED_OUTPUT");
        }

        int idx = @in.ReaderIndex;
        int originalLength = this.originalLength;

        ByteBuf output = null;

        try
        {
            if (isCompressed)
            {
                output = allocator.Buffer(originalLength);
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

            @in.SkipBytes(chunkLength);

            currentState = State.INIT_BLOCK;
            ByteBuf b = output;
            output = null;
            return b;
        }
        finally
        {
            if (output != null)
            {
                output.Release();
            }
        }
    }

    [UnstableApi]
    public static Builder Builder_()
    {
        return new Builder();
    }

    // CLR: the private field checksum is internal, because a C# enclosing class cannot read the private members of
    // its nested class (the Java constructor reads builder.checksum).
    [UnstableApi]
    public sealed class Builder : IDecompressor.AbstractDecompressorBuilder
    {
        internal NonCryptographicHashAlgorithm checksum;

        internal Builder()
        {
        }

        /**
         * A checksum to use to validate each block. Defaults to no checksum validation.
         *
         * @param checksum The checksum to use for validation
         * @return This builder
         */
        [UnstableApi]
        public Builder Checksum(NonCryptographicHashAlgorithm checksum)
        {
            this.checksum = checksum;
            return this;
        }

        /**
         * Enable validation using the default checksum, Adler32.
         *
         * @return This builder
         */
        [UnstableApi]
        public Builder DefaultChecksum()
        {
            return Checksum(new Adler32());
        }

        [UnstableApi]
        public override IDecompressor Build(IByteBufAllocator allocator)
        {
            return new DefensiveDecompressor(new FastLzFrameDecompressor(this, allocator));
        }
    }
}
