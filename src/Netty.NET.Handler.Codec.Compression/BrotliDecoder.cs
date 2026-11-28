/*
 * Copyright 2021 The Netty Project
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
using System.Collections.Generic;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * Decompresses a {@link ByteBuf} encoded with the brotli format.
 * <p>
 * See <a href="https://github.com/google/brotli">brotli</a>.
 */
// CLR: Keep the input, pending output and last decoder status separately because the BCL consumes input and emits
// output in the same call.
// Done can include final output. Drain it before reporting completion and reject trailing bytes in the pushed
// input. Keep the mutable decoder in a writable field so calls share its native state.
public sealed class BrotliDecoder : ByteToMessageDecoder
{

    private static readonly int DEFAULT_MAX_FORWARD_BYTES = CompressionUtil.DEFAULT_MAX_FORWARD_BYTES;
    private const int DEFAULT_INPUT_BUFFER_SIZE = 8 * 1024;

    // CLR: the most output one Decompress call writes (the size of outputBuffer), 64 KiB, the Java default of
    // DEFAULT_MAX_FORWARD_BYTES and of BrotliDecompressor's maxOutputChunkSize. pull(outputBufferSize) returns at most
    // outputBufferSize bytes and less when less is available; the cap keeps a large outputBufferSize from allocating an
    // array of that size, where brotli4j hands out its native buffer.
    private const int MAX_OUTPUT_BUFFER_SIZE = 64 * 1024;

    private enum State
    {
        DONE, NEEDS_MORE_INPUT, ERROR
    }

    static BrotliDecoder()
    {
        Brotli.EnsureAvailability();
    }

    private readonly int inputBufferSize;
    private readonly int outputBufferSize;
    private System.IO.Compression.BrotliDecoder decoder;
    private bool destroyed;
    private bool needsRead;
    private ByteBuf accumBuffer;
    // CLR: the DecoderJNI.Wrapper state (see the class comment).
    private byte[] inputBuffer;
    private int inputOffset;
    private int inputLength;
    private byte[] outputBuffer;
    private int outputLength;
    private OperationStatus lastStatus = OperationStatus.NeedMoreData;

    /**
     * Creates a new BrotliDecoder with a default 8kB input buffer
     */
    public BrotliDecoder() : this(DEFAULT_INPUT_BUFFER_SIZE)
    {
    }

    /**
     * Creates a new BrotliDecoder
     * @param inputBufferSize desired size of the input buffer in bytes
     */
    public BrotliDecoder(int inputBufferSize)
            : this(inputBufferSize == 0 ? DEFAULT_INPUT_BUFFER_SIZE : inputBufferSize, DEFAULT_MAX_FORWARD_BYTES)
    {
    }

    /**
     * Creates a new BrotliDecoder
     * @param inputBufferSize desired size of the input buffer in bytes
     * @param outputBufferSize desired max size of the output buffer in bytes
     *                         (produce multiple output buffers if exceeded)
     */
    public BrotliDecoder(int inputBufferSize, int outputBufferSize)
    {
        this.inputBufferSize = ObjectUtil.CheckPositive(inputBufferSize, "inputBufferSize");
        this.outputBufferSize = ObjectUtil.CheckPositive(outputBufferSize, "outputBufferSize");
    }

    /**
     * Creates a new {@link BrotliDecoder} that use the {@code maxAllocation}
     * semantics: the supplied value bounds the size of the emitted decompressed chunks.
     * The input buffer size stays at the decoder's default.
     *
     * @param maxAllocation maximum size, in bytes, of each decompressed output
     *                      buffer forwarded downstream; if {@code 0}, the
     *                      decoder's default output cap is used.
     */
    public static BrotliDecoder NewDecoderWithMaxAllocation(int maxAllocation)
    {
        ObjectUtil.CheckPositiveOrZero(maxAllocation, "maxAllocation");

        return maxAllocation > 0 ?
                new BrotliDecoder(DEFAULT_INPUT_BUFFER_SIZE, maxAllocation) :
                new BrotliDecoder();
    }

    // CLR: Pull() then releases it and continues decoding when the decoder may hold more (see Pull).
    private void ForwardOutput(IChannelHandlerContext ctx)
    {
        ReadOnlySpan<byte> nativeBuffer = outputBuffer.AsSpan(0, outputLength);
        // nativeBuffer actually wraps brotli's internal buffer so we need to copy its content
        int remaining = nativeBuffer.Length;

        if (accumBuffer == null)
        {
            accumBuffer = ctx.Alloc().Buffer(remaining);
        }

        accumBuffer.WriteBytes(nativeBuffer);
        Pull();
        needsRead = false;

        if (accumBuffer.ReadableBytes >= outputBufferSize)
        {
            ctx.FireChannelRead(accumBuffer);
            accumBuffer = null;
        }
    }

    private void FlushAccumBuffer(IChannelHandlerContext ctx)
    {
        if (accumBuffer != null && accumBuffer.IsReadable())
        {
            ctx.FireChannelRead(accumBuffer);
        }
        else if (accumBuffer != null)
        {
            accumBuffer.Release();
        }

        accumBuffer = null;
    }

    // CLR: the switch over decoder.getStatus() is a switch over lastStatus (see the class comment): DONE forwards the
    // output that the finishing call wrote before it returns, and the OK case (decoder.push(0)) does not occur.
    // decoder.hasOutput() is outputLength != 0; getInputBuffer()/clear() is inputBuffer, which ReadBytes fills from its
    // start.
    private State Decompress(IChannelHandlerContext ctx, ByteBuf input)
    {
        for (;;)
        {
            switch (lastStatus)
            {
                case OperationStatus.Done:
                    if (outputLength != 0)
                    {
                        ForwardOutput(ctx);
                        break;
                    }

                    return inputOffset == inputLength ? State.DONE : State.ERROR;

                case OperationStatus.NeedMoreData:
                    while (outputLength != 0)
                    {
                        ForwardOutput(ctx);
                    }

                    if (!input.IsReadable())
                    {
                        return State.NEEDS_MORE_INPUT;
                    }

                    byte[] decoderInputBuffer = inputBuffer;
                    int readBytes = ReadBytes(input, decoderInputBuffer);
                    Push(readBytes);
                    break;

                case OperationStatus.DestinationTooSmall:
                    ForwardOutput(ctx);
                    break;

                default:
                    return State.ERROR;
            }
        }
    }

    // CLR: the destination is the byte[] input buffer (java.nio.ByteBuffer dest), filled from index 0 after clear(), so
    // the slice/limit/position steps are ReadBytes into the span of the first limit bytes.
    private static int ReadBytes(ByteBuf @in, byte[] dest)
    {
        int limit = Math.Min(@in.ReadableBytes, dest.Length);
        @in.ReadBytes(dest.AsSpan(0, limit));
        return limit;
    }

    // CLR: DecoderJNI.Wrapper.push(length): length new bytes at the start of inputBuffer (0 continues with the input left
    // over), decoded by one Decompress call into the empty outputBuffer. When that call returns NeedMoreData with
    // outputBuffer full, the decoder may still hold output, so the status is DestinationTooSmall (NEEDS_MORE_OUTPUT).
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

    // CLR: the rest of DecoderJNI.Wrapper.pull(): the output in outputBuffer has been copied, and after
    // DestinationTooSmall decoding continues (push(0)) into the emptied buffer, so lastStatus says again whether more
    // output, more input, the end of the stream or an error follows.
    private void Pull()
    {
        outputLength = 0;

        if (lastStatus == OperationStatus.DestinationTooSmall)
        {
            Push(0);
        }
    }

    // CLR: new DecoderJNI.Wrapper(inputBufferSize) is the BCL decoder (created on first use) with its input buffer and the
    // output buffer (see MAX_OUTPUT_BUFFER_SIZE).
    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        decoder = new System.IO.Compression.BrotliDecoder();
        inputBuffer = new byte[inputBufferSize];
        outputBuffer = new byte[Math.Min(outputBufferSize, MAX_OUTPUT_BUFFER_SIZE)];
    }

    protected override void Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object> @out)
    {
        needsRead = true;

        if (destroyed)
        {
            // Skip data received after finished.
            @in.SkipBytes(@in.ReadableBytes);
            return;
        }

        if (!@in.IsReadable())
        {
            return;
        }

        try
        {
            State state = Decompress(ctx, @in);
            if (state == State.DONE)
            {
                Destroy();
            }
            else if (state == State.ERROR)
            {
                throw new DecompressionException("Brotli stream corrupted");
            }
        }
        catch (Exception)
        {
            Destroy();
            throw;
        }
        finally
        {
            FlushAccumBuffer(ctx);
        }
    }

    private void Destroy()
    {
        if (!destroyed)
        {
            destroyed = true;
            decoder.Dispose();
        }
    }

    protected override void HandlerRemoved0(IChannelHandlerContext ctx)
    {
        try
        {
            Destroy();
        }
        finally
        {
            base.HandlerRemoved0(ctx);
        }
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        try
        {
            Destroy();
        }
        finally
        {
            base.ChannelInactive(ctx);
        }
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        // Discard bytes of the cumulation buffer if needed.
        DiscardSomeReadBytes();

        if (needsRead && !ctx.Channel.Config.IsAutoRead())
        {
            ctx.Read();
        }

        ctx.FireChannelReadComplete();
    }
}
