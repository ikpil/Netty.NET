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
using Netty.NET.Common;

namespace Netty.NET.Buffer;

public static partial class ByteBufUtil
{
    /**
     * Read the given amount of bytes into a new {@link ByteBuf} that is allocated from the {@link ByteBufAllocator}.
     */
    /// <remarks>Reads exactly length bytes, even when a custom allocator returns extra capacity.
    /// The source reader index and result writer index advance by length; source writer index and marks stay unchanged.
    /// On failure the new buffer is released.
    /// Null and negative arguments are checked before allocation; source lifetime/range checks also apply to empty reads.</remarks>
    public static ByteBuf ReadBytes(IByteBufAllocator allocator, ByteBuf buffer, int length)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ByteBuf result = allocator.Buffer(length);
        try { buffer.ReadBytes(result, length); return result; }
        catch { result.Release(); throw; }
    }

    /**
     * Encode a {@link CharSequence} in <a href="https://en.wikipedia.org/wiki/UTF-8">UTF-8</a> and write
     * it to a {@link ByteBuf} allocated with {@code alloc}.
     * @param alloc The allocator used to allocate a new {@link ByteBuf}.
     * @param seq The characters to write into a buffer.
     * @return The {@link ByteBuf} which contains the <a href="https://en.wikipedia.org/wiki/UTF-8">UTF-8</a> encoded
     * result.
     */
    /// <remarks>The initial capacity reserves three bytes per UTF-16 code unit. The existing
    /// Netty UTF-8 writer supplies malformed-surrogate mappings. A failed write releases the new owner.</remarks>
    public static ByteBuf WriteUtf8(IByteBufAllocator allocator, ReadOnlySpan<char> text)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        // UTF-8 uses max. 3 bytes per char, so calculate the worst case.
        ByteBuf result = allocator.Buffer(Utf8MaxBytes(text));
        try { WriteUtf8(result, text); return result; }
        catch { result.Release(); throw; }
    }

    /**
     * Encode a {@link CharSequence} in <a href="https://en.wikipedia.org/wiki/UTF-8">UTF-8</a> and write
     * it to a {@link ByteBuf} allocated with {@code alloc}.
     * @param alloc The allocator used to allocate a new {@link ByteBuf}.
     * @param seq The characters to write into a buffer.
     * @return The {@link ByteBuf} which contains the <a href="https://en.wikipedia.org/wiki/UTF-8">UTF-8</a> encoded
     * result.
     */
    /// <remarks>AsciiString uses the original raw-octet path, including bytes above 127.
    /// Initial allocation requests its length, then the public writer reserves three times that length,
    /// which may grow the buffer. On failure the newly allocated owner is released.</remarks>
    public static ByteBuf WriteUtf8(IByteBufAllocator allocator, AsciiString text)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(text);
        ByteBuf result = allocator.Buffer(Utf8MaxBytes(text));
        try { WriteUtf8(result, text); return result; }
        catch { result.Release(); throw; }
    }

    /**
     * Encode a {@link CharSequence} in <a href="https://en.wikipedia.org/wiki/ASCII">ASCII</a> and write
     * it to a {@link ByteBuf} allocated with {@code alloc}.
     * @param alloc The allocator used to allocate a new {@link ByteBuf}.
     * @param seq The characters to write into a buffer.
     * @return The {@link ByteBuf} which contains the <a href="https://en.wikipedia.org/wiki/ASCII">ASCII</a> encoded
     * result.
     */
    /// <remarks>Reserves one byte per UTF-16 code unit and uses the existing Netty octet mapping.
    /// On failure the newly allocated owner is released.</remarks>
    public static ByteBuf WriteAscii(IByteBufAllocator allocator, ReadOnlySpan<char> text)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        // ASCII uses 1 byte per char
        ByteBuf result = allocator.Buffer(text.Length);
        try { WriteAscii(result, text); return result; }
        catch { result.Release(); throw; }
    }

    /**
     * Encode a {@link CharSequence} in <a href="https://en.wikipedia.org/wiki/ASCII">ASCII</a> and write
     * it to a {@link ByteBuf} allocated with {@code alloc}.
     * @param alloc The allocator used to allocate a new {@link ByteBuf}.
     * @param seq The characters to write into a buffer.
     * @return The {@link ByteBuf} which contains the <a href="https://en.wikipedia.org/wiki/ASCII">ASCII</a> encoded
     * result.
     */
    /// <remarks>Copies raw octets using the AsciiString logical range and backing-array offset.
    /// On failure the newly allocated owner is released.</remarks>
    public static ByteBuf WriteAscii(IByteBufAllocator allocator, AsciiString text)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(text);
        ByteBuf result = allocator.Buffer(text.Length());
        try { WriteAscii(result, text); return result; }
        catch { result.Release(); throw; }
    }

    /**
     * Encode the given {@link CharBuffer} using the given {@link Charset} into a new {@link ByteBuf} which
     * is allocated via the {@link ByteBufAllocator}.
     */
    /**
     * Encode the given {@link CharBuffer} using the given {@link Charset} into a new {@link ByteBuf} which
     * is allocated via the {@link ByteBufAllocator}.
     *
     * @param alloc The {@link ByteBufAllocator} to allocate {@link ByteBuf}.
     * @param src The {@link CharBuffer} to encode.
     * @param charset The specified {@link Charset}.
     * @param extraCapacity the extra capacity to alloc except the space for decoding.
     */
    /// <remarks>ReadOnlySpan replaces the remaining CharBuffer range without a mutable cursor.
    /// The caller's Encoding and fallback policy are honored, with no preamble. Capacity is the exact
    /// encoded byte count plus extraCapacity, rather than the Java encoder's worst-case bound.
    /// Extra capacity stays unwritten for framing suffixes. Size overflow fails before allocation.
    /// Encoding/writing failure releases the new owner, including native storage.
    /// Keep source content and Encoding configuration stable throughout sizing and writing.</remarks>
    public static ByteBuf EncodeString(IByteBufAllocator allocator, ReadOnlySpan<char> text,
        Encoding encoding, int extraCapacity = 0)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(encoding);
        ArgumentOutOfRangeException.ThrowIfNegative(extraCapacity);
        int capacity = checked(encoding.GetByteCount(text) + extraCapacity);
        ByteBuf result = allocator.Buffer(capacity);
        try { result.WriteString(text, encoding); return result; }
        catch { result.Release(); throw; }
    }
}
