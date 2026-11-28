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
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * Decompresses a {@link ByteBuf} using the deflate algorithm.
 */
// CLR: checkPositiveOrZero is the Common ObjectUtil.CheckPositiveOrZero, whose IllegalArgumentException is
// ArgumentException. ctx.alloc().heapBuffer(...) is ctx.Alloc().HeapBuffer(...), and the ByteBuf accessors
// (readableBytes(), maxCapacity(), isWritable()) are the Buffer port's properties and methods. The overridable
// Java methods prepareDecompressBuffer and decompressionBufferExhausted are virtual. Numbers in the exception
// message use the invariant culture.
public abstract class ZlibDecoder : ByteToMessageDecoder
{

    /**
     * Maximum allowed size of the decompression buffer.
     */
    protected readonly int maxAllocation;

    /**
     * Same as {@link #ZlibDecoder(int)} with maxAllocation = 0.
     */
    public ZlibDecoder() : this(0)
    {
    }

    /**
     * Construct a new ZlibDecoder.
     * @param maxAllocation
     *          Maximum size of the decompression buffer. Must be &gt;= 0.
     *          If zero, maximum size is decided by the {@link ByteBufAllocator}.
     */
    public ZlibDecoder(int maxAllocation)
    {
        this.maxAllocation = ObjectUtil.CheckPositiveOrZero(maxAllocation, "maxAllocation");
    }

    /**
     * Returns {@code true} if and only if the end of the compressed stream
     * has been reached.
     */
    public abstract bool IsClosed();

    /**
     * Allocate or expand the decompression buffer, without exceeding the maximum allocation.
     * Calls {@link #decompressionBufferExhausted(ByteBuf)} if the buffer is full and cannot be expanded further.
     */
    protected virtual ByteBuf PrepareDecompressBuffer(IChannelHandlerContext ctx, ByteBuf buffer, int preferredSize)
    {
        if (buffer == null)
        {
            if (maxAllocation == 0)
            {
                return ctx.Alloc().HeapBuffer(preferredSize);
            }

            return ctx.Alloc().HeapBuffer(Math.Min(preferredSize, maxAllocation), maxAllocation);
        }

        // this always expands the buffer if possible, even if the expansion is less than preferredSize
        // we throw the exception only if the buffer could not be expanded at all and has no space left
        // this means that one final attempt to deserialize will always be made with the buffer at maxAllocation
        if (buffer.EnsureWritable(preferredSize, true) == 1 && !buffer.IsWritable())
        {
            // buffer must be consumed so subclasses don't add it to output
            // we therefore duplicate it when calling decompressionBufferExhausted() to guarantee non-interference
            // but wait until after to consume it so the subclass can tell how much output is really in the buffer
            DecompressionBufferExhausted(buffer.Duplicate());
            buffer.SkipBytes(buffer.ReadableBytes);
            throw new DecompressionException("Decompression buffer has reached maximum size: " +
                                             buffer.MaxCapacity.ToString(CultureInfo.InvariantCulture));
        }

        return buffer;
    }

    /**
     * Called when the decompression buffer cannot be expanded further.
     * Default implementation is a no-op, but subclasses can override in case they want to
     * do something before the {@link DecompressionException} is thrown, such as log the
     * data that was decompressed so far.
     */
    protected virtual void DecompressionBufferExhausted(ByteBuf buffer)
    {
    }

}
