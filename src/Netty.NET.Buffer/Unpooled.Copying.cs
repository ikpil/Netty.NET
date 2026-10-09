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

namespace Netty.NET.Buffer;

public static partial class Unpooled
{
    /// <summary>Returns the shared empty buffer when there are no inputs.</summary>
    // CLR: resolves the two params overloads for a zero-input call.
    public static ByteBuf CopiedBuffer() => EmptyBuffer;

    /**
     * Creates a new big-endian buffer whose content is a copy of the
     * specified {@code array}.  The new buffer's {@code readerIndex} and
     * {@code writerIndex} are {@code 0} and {@code array.length} respectively.
     */
    public static ByteBuf CopiedBuffer(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return CopiedBuffer(bytes.AsSpan());
    }

    /**
     * Creates a new big-endian buffer whose content is a copy of the
     * specified {@code array}'s sub-region.  The new buffer's
     * {@code readerIndex} and {@code writerIndex} are {@code 0} and
     * the specified {@code length} respectively.
     */
    /// <remarks>CLR null/range validation also applies to empty ranges.</remarks>
    public static ByteBuf CopiedBuffer(byte[] bytes, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        return CopiedBuffer(bytes.AsSpan(offset, length));
    }

    /**
     * Creates a new buffer whose content is a copy of the specified
     * {@code buffer}'s readable bytes.  The new buffer's {@code readerIndex}
     * and {@code writerIndex} are {@code 0} and {@code buffer.readableBytes}
     * respectively.
     */
    /// <remarks>Input ownership and indices are unchanged. Unlike array and
    /// multi-buffer copies, this single-buffer result can grow, matching Netty.</remarks>
    public static ByteBuf CopiedBuffer(ByteBuf buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        int readable = buffer.ReadableBytes;
        if (readable <= 0) return EmptyBuffer;
        ByteBuf copy = Buffer(readable);
        try
        {
            // CLR: absolute transfer preserves the source reader index.
            copy.SetBytes(0, buffer, buffer.ReaderIndex, readable);
            copy.WriterIndex = readable;
            return copy;
        }
        catch
        {
            // CLR: a failed factory cannot return its newly allocated owner.
            copy.Release();
            throw;
        }
    }

    /**
     * Creates a new big-endian buffer whose content is a merged copy of
     * the specified {@code arrays}.  The new buffer's {@code readerIndex}
     * and {@code writerIndex} are {@code 0} and the sum of all arrays'
     * {@code length} respectively.
     */
    public static ByteBuf CopiedBuffer(params byte[][] arrays)
    {
        ArgumentNullException.ThrowIfNull(arrays);
        if (arrays.Length == 1) return CopiedBuffer(arrays[0]);
        // Merge the specified arrays into one array.
        int length = 0;
        foreach (byte[] array in arrays)
        {
            ArgumentNullException.ThrowIfNull(array);
            if (array.Length > int.MaxValue - length)
                throw new ArgumentException("The total length of the specified arrays is too big.", nameof(arrays));
            length += array.Length;
        }
        if (length == 0) return EmptyBuffer;
        byte[] merged = new byte[length];
        int offset = 0;
        foreach (byte[] array in arrays)
        {
            array.CopyTo(merged.AsSpan(offset, array.Length));
            offset += array.Length;
        }
        return WrappedBuffer(merged);
    }

    /**
     * Creates a new buffer whose content is a merged copy of the specified
     * {@code buffers}' readable bytes.  The new buffer's {@code readerIndex}
     * and {@code writerIndex} are {@code 0} and the sum of all buffers'
     * {@code readableBytes} respectively.
     *
     * @throws IllegalArgumentException
     *         if the specified buffers' endianness are different from each
     *         other
     */
    /// <remarks>C# buffers use explicit BE/LE operations and have no mutable
    /// byte-order state to reconcile. Bytes are copied without interpretation;
    /// source references, permissions, indices and marks are unchanged.</remarks>
    public static ByteBuf CopiedBuffer(params ByteBuf[] buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        if (buffers.Length == 1) return CopiedBuffer(buffers[0]);
        // Merge the specified buffers into one buffer.
        int length = 0;
        foreach (ByteBuf buffer in buffers)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            int readable = buffer.ReadableBytes;
            if (readable <= 0) continue;
            if (readable > int.MaxValue - length)
                throw new ArgumentException("The total length of the specified buffers is too big.", nameof(buffers));
            length += readable;
        }
        if (length == 0) return EmptyBuffer;
        byte[] merged = new byte[length];
        int offset = 0;
        foreach (ByteBuf buffer in buffers)
        {
            int readable = buffer.ReadableBytes;
            // Keep zero-length reads: when there is readable input, the original
            // also checks accessibility/ranges on empty components during copying.
            buffer.GetBytes(buffer.ReaderIndex, merged.AsSpan(offset, readable));
            offset += readable;
        }
        // CLR: publish ownership only after every source read succeeds.
        return WrappedBuffer(merged);
    }
}
