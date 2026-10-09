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
using Netty.NET.Common;

namespace Netty.NET.Buffer;

public static partial class ByteBufUtil
{
/**
     * Calculates the hash code of the specified buffer.  This method is
     * useful when implementing a new buffer type.
     */
    /// <remarks>Uses Java's wrapping Int32 arithmetic and signed trailing bytes.
    /// Content, readable indices and lifetime must remain stable during the operation.</remarks>
    public static int HashCode(ByteBuf buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        EnsureComparisonAccessible(buffer);
        int length = buffer.ReadableBytes;
        int index = buffer.ReaderIndex;
        int hash = 1;
        unchecked
        {
            for (int words = length >> 2; words > 0; words--, index += 4)
                hash = 31 * hash + buffer.GetInt(index);
            // CLR byte is unsigned; Java byte contributes a signed value to the hash.
            for (int bytes = length & 3; bytes > 0; bytes--)
                hash = 31 * hash + (sbyte)buffer.GetByte(index++);
        }
        return hash == 0 ? 1 : hash;
    }

/**
     * Returns {@code true} if and only if the two specified buffers are
     * identical to each other for {@code length} bytes starting at {@code aStartIndex}
     * index for the {@code a} buffer and {@code bStartIndex} index for the {@code b} buffer.
     * A more compact way to express this is:
     * <p>
     * {@code a[aStartIndex : aStartIndex + length] == b[bStartIndex : bStartIndex + length]}
     */
    /// <remarks>Absolute ranges are bounded by WriterIndex and may precede ReaderIndex.
    /// Nonnegative out-of-range requests return false, including empty ranges past WriterIndex.
    /// Both buffers must be accessible, including for empty requests.</remarks>
    public static bool Equals(ByteBuf a, int aStartIndex, ByteBuf b, int bStartIndex, int length)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        // All indexes and lengths must be non-negative
        ArgumentOutOfRangeException.ThrowIfNegative(aStartIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(bStartIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        EnsureComparisonAccessible(a);
        EnsureComparisonAccessible(b);
        if (a.WriterIndex - length < aStartIndex || b.WriterIndex - length < bStartIndex)
            return false;

        if (a.TryGetReadOnlyMemory(aStartIndex, length, out var aMemory) &&
            b.TryGetReadOnlyMemory(bStartIndex, length, out var bMemory))
            return aMemory.Span.SequenceEqual(bMemory.Span);

        // Explicit big-endian access also handles words crossing component boundaries.
        for (int words = length >> 3; words > 0; words--, aStartIndex += 8, bStartIndex += 8)
            if (a.GetLong(aStartIndex) != b.GetLong(bStartIndex)) return false;
        for (int bytes = length & 7; bytes > 0; bytes--)
            if (a.GetByte(aStartIndex++) != b.GetByte(bStartIndex++)) return false;
        return true;
    }

/**
     * Returns {@code true} if and only if the two specified buffers are
     * identical to each other as described in {@link ByteBuf#equals(Object)}.
     * This method is useful when implementing a new buffer type.
     */
    /// <remarks>Reference identity is reflexive, even for null or a released buffer.
    /// Distinct buffers must be accessible; their readable ranges are compared.</remarks>
    public static bool Equals(ByteBuf a, ByteBuf b)
    {
        if (ReferenceEquals(a, b)) return true;
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        EnsureComparisonAccessible(a);
        EnsureComparisonAccessible(b);
        return a.ReadableBytes == b.ReadableBytes &&
            Equals(a, a.ReaderIndex, b, b.ReaderIndex, a.ReadableBytes);
    }

/**
     * Compares the two specified buffers as described in {@link ByteBuf#compareTo(ByteBuf)}.
     * This method is useful when implementing a new buffer type.
     */
    /// <remarks>Compares unsigned bytes lexicographically, preserving the original
    /// clamped unsigned word difference, trailing byte difference and prefix length difference.
    /// Reference identity returns zero; distinct buffers must be accessible.</remarks>
    public static int Compare(ByteBuf a, ByteBuf b)
    {
        if (ReferenceEquals(a, b)) return 0;
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        EnsureComparisonAccessible(a);
        EnsureComparisonAccessible(b);
        int aLength = a.ReadableBytes;
        int bLength = b.ReadableBytes;
        int length = Math.Min(aLength, bLength);
        int aIndex = a.ReaderIndex;
        int bIndex = b.ReaderIndex;
        for (int words = length >> 2; words > 0; words--, aIndex += 4, bIndex += 4)
        {
            long difference = (long)a.GetUnsignedInt(aIndex) - b.GetUnsignedInt(bIndex);
            // Ensure we not overflow when cast
            if (difference != 0) return (int)Math.Clamp(difference, int.MinValue, int.MaxValue);
        }
        for (int bytes = length & 3; bytes > 0; bytes--)
        {
            int difference = a.GetByte(aIndex++) - b.GetByte(bIndex++);
            if (difference != 0) return difference;
        }
        return aLength - bLength;
    }

    private static void EnsureComparisonAccessible(ByteBuf buffer)
    {
        if (buffer.ReferenceCount == 0) throw new IllegalReferenceCountException(0);
    }
}
