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
    /**
     * Creates a new buffer whose content is a copy of the specified
     * {@code buffer}'s current slice.  The new buffer's {@code readerIndex}
     * and {@code writerIndex} are {@code 0} and {@code buffer.remaining}
     * respectively.
     */
    /// <remarks>Memory slicing selects the source range. The writable heap result
    /// has a maximum equal to its length and does not borrow the source owner.</remarks>
    public static ByteBuf CopiedBuffer(ReadOnlyMemory<byte> buffer)
    {
        if (buffer.IsEmpty) return EmptyBuffer;
        // Duplicate the buffer so we not adjust the position during our get operation.
        // See https://github.com/netty/netty/issues/3896
        // CLR: ReadOnlyMemory has immutable range metadata and no mutable cursor.
        return CopiedBuffer(buffer.Span);
    }

    /**
     * Creates a new buffer whose content is a merged copy of the specified
     * {@code buffers}' slices.  The new buffer's {@code readerIndex}
     * and {@code writerIndex} are {@code 0} and the sum of all buffers'
     * {@code remaining} respectively.
     *
     * @throws IllegalArgumentException
     *         if the specified buffers' endianness are different from each
     *         other
     */
    /// <remarks>ReadOnlyMemory has no byte-order metadata. Bytes are copied unchanged;
    /// use explicit BE/LE operations on the result. Empty ranges are not accessed.
    /// Input owners must remain valid during the call and are never disposed.</remarks>
    // Original varargs overload documentation; CLR uses immutable range descriptors.
    /**
     * Creates a new buffer whose content is a merged copy of the specified
     * {@code buffers}' slices.  The new buffer's {@code readerIndex} and
     * {@code writerIndex} are {@code 0} and the sum of all buffers'
     * {@code remaining} respectively.
     *
     * @throws IllegalArgumentException
     *         if the specified buffers' endianness are different from each
     *         other
     */
    public static ByteBuf CopiedBuffer(params ReadOnlyMemory<byte>[] buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        if (buffers.Length == 1) return CopiedBuffer(buffers[0]);
        // Merge the specified buffers into one buffer.
        int length = 0;
        foreach (ReadOnlyMemory<byte> buffer in buffers)
        {
            if (buffer.Length > int.MaxValue - length)
                throw new ArgumentException("The total length of the specified buffers is too big.", nameof(buffers));
            length += buffer.Length;
        }
        if (length == 0) return EmptyBuffer;

        byte[] merged = new byte[length];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> buffer in buffers)
        {
            if (buffer.IsEmpty) continue;
            // Duplicate the buffer so we not adjust the position during our get operation.
            // See https://github.com/netty/netty/issues/3896
            // CLR: no cursor or source owner is changed, including when Span access fails.
            buffer.Span.CopyTo(merged.AsSpan(offset, buffer.Length));
            offset += buffer.Length;
        }
        return WrappedBuffer(merged);
    }
}
