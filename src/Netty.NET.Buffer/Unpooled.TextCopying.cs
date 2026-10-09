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
using System.Text;

namespace Netty.NET.Buffer;

public static partial class Unpooled
{
    /**
     * Creates a new big-endian buffer whose content is the specified
     * {@code string} encoded in the specified {@code charset}.
     * The new buffer's {@code readerIndex} and {@code writerIndex} are
     * {@code 0} and the length of the encoded string respectively.
     */
    /// <remarks>The supplied Encoding controls malformed/unmappable input, just as
    /// for ByteBuf.WriteString. No preamble is emitted. Unlike Java's whole-string
    /// UTF-8/ASCII shortcuts, this honors custom fallback policies. Netty's specialized
    /// byte mappings are available through ByteBufUtil.WriteUtf8/WriteAscii.
    /// Capacity is the exact encoded length rather than a CharsetEncoder upper bound;
    /// the result can grow up to int.MaxValue. An empty whole string has its own owner.</remarks>
    public static ByteBuf CopiedBuffer(string text, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(encoding);
        return CopyEncodedText(text.AsSpan(), encoding);
    }

    /**
     * Creates a new big-endian buffer whose content is a subregion of
     * the specified {@code string} encoded in the specified {@code charset}.
     * The new buffer's {@code readerIndex} and {@code writerIndex} are
     * {@code 0} and the length of the encoded string respectively.
     */
    /// <remarks>Offset and length count UTF-16 code units, not Unicode scalars.
    /// CLR null/range validation also applies to empty ranges. The supplied Encoding
    /// handles a surrogate pair split by the range. No preamble is emitted.</remarks>
    public static ByteBuf CopiedBuffer(string text, int offset, int length, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(encoding);
        if (offset < 0 || length < 0 || offset > text.Length - length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        return CopiedBuffer(text.AsSpan(offset, length), encoding);
    }

    /**
     * Creates a new big-endian buffer whose content is the specified
     * {@code array} encoded in the specified {@code charset}.
     * The new buffer's {@code readerIndex} and {@code writerIndex} are
     * {@code 0} and the length of the encoded string respectively.
     */
    /// <remarks>The supplied Encoding and its fallback policy are used without a preamble.</remarks>
    public static ByteBuf CopiedBuffer(char[] chars, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(chars);
        return CopiedBuffer(chars.AsSpan(), encoding);
    }

    /**
     * Creates a new big-endian buffer whose content is a subregion of
     * the specified {@code array} encoded in the specified {@code charset}.
     * The new buffer's {@code readerIndex} and {@code writerIndex} are
     * {@code 0} and the length of the encoded string respectively.
     */
    /// <remarks>Offset and length count UTF-16 code units. CLR null/range validation
    /// also applies to empty ranges; the supplied Encoding determines fallback.</remarks>
    public static ByteBuf CopiedBuffer(char[] chars, int offset, int length, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(chars);
        ArgumentNullException.ThrowIfNull(encoding);
        if (offset < 0 || length < 0 || offset > chars.Length - length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        return CopiedBuffer(chars.AsSpan(offset, length), encoding);
    }

    /// <summary>Copies encoded UTF-16 input into independent writable heap storage.
    /// An empty span returns the shared empty buffer.</summary>
    /// <remarks>Span replaces Java CharBuffer ranges. The supplied Encoding controls
    /// fallback; no preamble is emitted. Capacity is the exact encoded length and
    /// the result can grow up to int.MaxValue. Keep the input stable during copying.</remarks>
    public static ByteBuf CopiedBuffer(ReadOnlySpan<char> text, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        return text.IsEmpty ? EmptyBuffer : CopyEncodedText(text, encoding);
    }

    private static ByteBuf CopyEncodedText(ReadOnlySpan<char> text, Encoding encoding)
    {
        // Mimic the same behavior as other copiedBuffer implementations.
        // CLR: size using the caller's Encoding, then encode directly into the new owner.
        int length = encoding.GetByteCount(text);
        ByteBuf copy = Buffer(length);
        try
        {
            copy.WriterIndex = encoding.GetBytes(text, copy.AsSpan(0, length));
            return copy;
        }
        catch
        {
            // A factory failure cannot return its newly allocated reference.
            copy.Release();
            throw;
        }
    }
}
