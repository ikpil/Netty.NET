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
using System.Buffers;
using System.Diagnostics;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * Decompresses a {@link ByteBuf} encoded with the brotli format.
 *
 * See <a href="https://github.com/google/brotli">brotli</a>.
 */
// CLR: The BCL creates native decoder state on the first decode, so initialization errors surface there. Drain
// pending output before treating Done as complete, then reject any remaining input.
[UnstableApi]
public sealed class BrotliDecompressor : IDecompressor
{
    // CLR: the most output one Decompress call writes, as in BrotliDecoder: 64 KiB, the default maxOutputChunkSize.
    // pull() returns at most maxOutputChunkSize bytes and less when less is available.
    private const int MAX_OUTPUT_BUFFER_SIZE = 64 * 1024;

    private readonly IByteBufAllocator allocator;
    private System.IO.Compression.BrotliDecoder decoder;
    private ByteBuf unusedInput;
    // CLR: the DecoderJNI.Wrapper state (see the class comment).
    private readonly byte[] inputBuffer;
    private int inputOffset;
    private int inputLength;
    private readonly byte[] outputBuffer;
    private int outputLength;
    private OperationStatus lastStatus = OperationStatus.NeedMoreData;

    static BrotliDecompressor()
    {
        Brotli.EnsureAvailability();
    }

    internal BrotliDecompressor(Builder builder, IByteBufAllocator allocator)
    {
        this.allocator = allocator;
        this.decoder = new System.IO.Compression.BrotliDecoder();
        this.inputBuffer = new byte[builder.inputBufferSize];
        this.outputBuffer = new byte[Math.Min(builder.maxOutputChunkSize, MAX_OUTPUT_BUFFER_SIZE)];
    }

    // CLR: DONE with output left reports NEED_OUTPUT first, and DONE with pushed input left is the ERROR of
    // decoder_jni.cc.
    public IDecompressor.Status Status_()
    {
        while (true)
        {
            switch (lastStatus)
            {
                case OperationStatus.InvalidData:
                    throw new DecompressionException("Brotli error status");
                case OperationStatus.Done:
                    if (outputLength != 0)
                    {
                        return IDecompressor.Status.NEED_OUTPUT;
                    }
                    if (inputOffset != inputLength)
                    {
                        throw new DecompressionException("Brotli error status");
                    }

                    return IDecompressor.Status.COMPLETE;
                case OperationStatus.NeedMoreData:
                    if (outputLength != 0)
                    {
                        return IDecompressor.Status.NEED_OUTPUT;
                    }
                    if (unusedInput == null)
                    {
                        return IDecompressor.Status.NEED_INPUT;
                    }

                    AddSomeInput(unusedInput);

                    if (!unusedInput.IsReadable())
                    {
                        unusedInput.Release();
                        unusedInput = null;
                    }

                    break;
                case OperationStatus.DestinationTooSmall:
                    return IDecompressor.Status.NEED_OUTPUT;
                default:
                    throw new UnreachableException("Unknown status: " + lastStatus);
            }
        }
    }

    public void AddInput(ByteBuf buf)
    {
        try
        {
            if (unusedInput != null)
            {
                throw new InvalidOperationException("Not in state NEED_INPUT");
            }

            AddSomeInput(buf);
        }
        catch (Exception)
        {
            buf.Release();
            throw;
        }

        if (buf.IsReadable())
        {
            this.unusedInput = buf;
        }
        else
        {
            buf.Release();
        }
    }

    // CLR: getInputBuffer()/clear() is inputBuffer, which ReadBytes fills from its start.
    private void AddSomeInput(ByteBuf buf)
    {
        byte[] decoderInputBuffer = inputBuffer;
        int readBytes = ReadBytes(buf, decoderInputBuffer);
        Push(readBytes);
    }

    public void EndOfInput()
    {
        if (lastStatus == OperationStatus.NeedMoreData)
        {
            Debug.Assert(unusedInput == null, "Expected to be in NEED_INPUT state");
            Push(0);
            if (lastStatus == OperationStatus.NeedMoreData)
            {
                throw new DecompressionException("Truncated brotli stream");
            }
        }
    }

    // CLR: Pull() then releases it and continues decoding when the decoder may hold more (see BrotliDecoder).
    public ByteBuf TakeOutput()
    {
        ReadOnlySpan<byte> nativeBuffer = outputBuffer.AsSpan(0, outputLength);
        // nativeBuffer actually wraps brotli's internal buffer so we need to copy its content
        // size limited by maxOutputChunkSize
        ByteBuf copy = allocator.Buffer(nativeBuffer.Length);
        copy.WriteBytes(nativeBuffer);
        Pull();
        return copy;
    }

    public void Close()
    {
        decoder.Dispose();

        if (unusedInput != null)
        {
            unusedInput.Release();
            unusedInput = null;
        }
    }

    // CLR: as BrotliDecoder.ReadBytes: the destination is the byte[] input buffer, filled from index 0.
    private static int ReadBytes(ByteBuf @in, byte[] dest)
    {
        int limit = Math.Min(@in.ReadableBytes, dest.Length);
        @in.ReadBytes(dest.AsSpan(0, limit));
        return limit;
    }

    // CLR: DecoderJNI.Wrapper.push(length), as in BrotliDecoder.
    private void Push(int length)
    {
        if (length != 0)
        {
            inputOffset = 0;
            inputLength = length;
        }

        lastStatus = decoder.Decompress(inputBuffer.AsSpan(inputOffset, inputLength - inputOffset), outputBuffer,
                out int bytesConsumed, out outputLength);
        inputOffset += bytesConsumed;

        if (lastStatus == OperationStatus.NeedMoreData && outputLength == outputBuffer.Length)
        {
            lastStatus = OperationStatus.DestinationTooSmall;
        }
    }

    // CLR: the rest of DecoderJNI.Wrapper.pull(), as in BrotliDecoder.
    private void Pull()
    {
        outputLength = 0;

        if (lastStatus == OperationStatus.DestinationTooSmall)
        {
            Push(0);
        }
    }

    [UnstableApi]
    public static Builder Builder_()
    {
        return new Builder();
    }

    // CLR: the private fields are internal, because a C# enclosing class cannot read the private members of its
    // nested class (the Java constructor reads builder.inputBufferSize and builder.maxOutputChunkSize).
    [UnstableApi]
    public sealed class Builder : IDecompressor.AbstractDecompressorBuilder
    {
        internal int inputBufferSize = 8 * 1024;
        internal int maxOutputChunkSize = 64 * 1024;

        internal Builder()
        {
        }

        /**
         * Desired size of the input buffer in bytes. Default 8K.
         *
         * @param inputBufferSize desired size of the input buffer in bytes
         * @return This builder
         */
        public Builder InputBufferSize(int inputBufferSize)
        {
            this.inputBufferSize = ObjectUtil.CheckPositive(inputBufferSize, "inputBufferSize");
            return this;
        }

        /**
         * Number of bytes of output to consume at a time. Default 64K.
         *
         * @param maxOutputChunkSize Maximum output chunk size
         * @return This builder
         */
        public Builder MaxOutputChunkSize(int maxOutputChunkSize)
        {
            this.maxOutputChunkSize = ObjectUtil.CheckPositive(maxOutputChunkSize, "maxOutputChunkSize");
            return this;
        }

        public override IDecompressor Build(IByteBufAllocator allocator)
        {
            return new DefensiveDecompressor(new BrotliDecompressor(this, allocator));
        }
    }
}
