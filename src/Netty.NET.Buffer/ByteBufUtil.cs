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
using System.Buffers;
using System.Text;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Buffers.Binary;
using System.Text.Unicode;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Buffer;

/**
 * A collection of utility methods that is related with handling {@link ByteBuf},
 * such as the generation of hex dump and swapping an integer's byte order.
 */
/// <summary>Utilities for buffer encoding, search, hexadecimal formatting, content comparison,
/// text validation, byte extraction, allocation, byte order and accessibility.</summary>
public static class ByteBufUtil
{
    /**
     * Read the given amount of bytes into a new {@link ByteBuf} that is allocated from the {@link ByteBufAllocator}.
     */
    /// <remarks>Reads exactly length bytes, even when a custom allocator returns extra capacity.
    /// The source reader index and result writer index advance by length; source writer index and marks stay unchanged.
    /// On failure the new buffer is released.
    /// Null and negative arguments are checked before allocation; source lifetime/range checks also apply to empty reads.</remarks>
    // CLR: Java calls buffer.readBytes(dst), which transfers "until the destination becomes non-writable", so an
    // allocator that returns more writable bytes than length makes it read more than length or fail on a short
    // source. The exact-length guarantee stays: when the new buffer has exactly length writable bytes, the case of
    // every Netty.NET allocator (Unpooled, Pooled, Adaptive and Transport's PreferHeap, heap and direct, measured
    // 2026-11-24), ReadBytes(ByteBuf) is called as in Java, which ReplayingDecoderByteBuf supports while it
    // rejects ReadBytes(ByteBuf, int) as Java's does; otherwise ReadBytes(ByteBuf, int) reads exactly length
    // bytes.
    public static ByteBuf ReadBytes(IByteBufAllocator allocator, ByteBuf buffer, int length)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        ByteBuf result = allocator.Buffer(length);

        try
        {
            if (result.WritableBytes == length)
            {
                buffer.ReadBytes(result);
            }
            else
            {
                buffer.ReadBytes(result, length);
            }

            return result;
        }
        catch
        {
            result.Release();
            throw;
        }
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

        try
        {
            WriteUtf8(result, text);
            return result;
        }
        catch
        {
            result.Release();
            throw;
        }
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

        try
        {
            WriteUtf8(result, text);
            return result;
        }
        catch
        {
            result.Release();
            throw;
        }
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

        try
        {
            WriteAscii(result, text);
            return result;
        }
        catch
        {
            result.Release();
            throw;
        }
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

        try
        {
            WriteAscii(result, text);
            return result;
        }
        catch
        {
            result.Release();
            throw;
        }
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

        try
        {
            result.WriteCharSequence(text, encoding);
            return result;
        }
        catch
        {
            result.Release();
            throw;
        }
    }

    // CLR: separate holders avoid a cycle when an allocator singleton initializes before
    // the global selector. Utility calls alone need not construct any allocator.
    internal static IByteBufAllocator DefaultAllocator => DefaultAllocatorHolder.Instance;
    internal static bool PreferDirect => DirectPreference.Value;

    private static class DirectPreference
    {
        // We should always prefer direct buffers by default if we can use a Cleaner to release direct buffers.
        // CLR: SafeHandle-backed native owners provide reliable release without JVM Cleaner detection.
        internal static readonly bool Value = !SystemPropertyUtil.GetBoolean("io.netty.noPreferDirect", false);
        static DirectPreference()
        {
        }
    }

    private static class DefaultAllocatorHolder
    {
        internal static readonly IByteBufAllocator Instance = Create();
        static DefaultAllocatorHolder()
        {
        }

        private static IByteBufAllocator Create()
        {
            string allocType = SystemPropertyUtil.Get("io.netty.allocator.type", "adaptive");
            IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(ByteBufUtil));
            IByteBufAllocator allocator;

            if (allocType == "unpooled")
            {
                allocator = UnpooledByteBufAllocator.Default;
                logger.Debug("-Dio.netty.allocator.type: {}", allocType);
            }
            else if (allocType == "pooled")
            {
                allocator = PooledByteBufAllocator.Default;
                logger.Debug("-Dio.netty.allocator.type: {}", allocType);
            }
            else if (allocType == "adaptive")
            {
                allocator = new AdaptiveByteBufAllocator();
                logger.Debug("-Dio.netty.allocator.type: {}", allocType);
            }
            else
            {
                allocator = PooledByteBufAllocator.Default;
                logger.Debug("-Dio.netty.allocator.type: pooled (unknown: {})", allocType);
            }

            return allocator;
        }
    }

/**
     * Create a copy of the underlying storage from {@code buf} into a byte array.
     * The copy will start at {@link ByteBuf#readerIndex()} and copy {@link ByteBuf#readableBytes()} bytes.
     */
    /// <remarks>Returns an independent snapshot of readable bytes without changing indices,
    /// marks or reference counts. Content, layout and lifetime must remain stable during extraction.</remarks>
    public static byte[] GetBytes(ByteBuf buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        return GetBytes(buffer, buffer.ReaderIndex, buffer.ReadableBytes);
    }

/**
     * Create a copy of the underlying storage from {@code buf} into a byte array.
     * The copy will start at {@code start} and copy {@code length} bytes.
     */
    /// <remarks>The absolute range may extend past WriterIndex up to Capacity.
    /// Returns an independent snapshot; empty results use Array.Empty&lt;byte&gt;().</remarks>
    public static byte[] GetBytes(ByteBuf buffer, int start, int length)
    {
        return GetBytes(buffer, start, length, true);
    }

/**
     * Return an array of the underlying storage from {@code buf} into a byte array.
     * The copy will start at {@code start} and copy {@code length} bytes.
     * If {@code copy} is true a copy will be made of the memory.
     * If {@code copy} is false the underlying storage will be shared, if possible.
     */
    /// <remarks>Sharing requires a writable contiguous mapping of the complete buffer to an
    /// entire managed byte array, and a request covering that entire mapping. Read-only buffers
    /// and components, native storage, partial arrays and segmented ranges are copied.
    /// CLR memory mapping determines eligibility, including contiguous views of composites;
    /// Java's hasArray wrapper restrictions are not reproduced. Empty results use Array.Empty.
    /// A returned array keeps its managed storage alive, but shared content can change;
    /// resizing can detach the previous storage. Use copying for a stable snapshot.
    /// No ownership reference is acquired. Exclude concurrent content/layout/lifetime changes.
    /// Invalid and released ranges throw even when length is zero.</remarks>
    public static byte[] GetBytes(ByteBuf buffer, int start, int length, bool copy)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        ReadOnlySequence<byte> bytes = buffer.AsReadOnlySequence(start, length);
        if (length == 0)
        {
            return Array.Empty<byte>();
        }

        if (!copy && start == 0 && length == buffer.Capacity && !buffer.IsReadOnly)
        {
            try
            {
                // Probe writable memory, so a mutable composite cannot expose the array
                // of a read-only component through a read-only memory borrow.
                if (buffer.TryGetMemory(0, length, out Memory<byte> memory) &&
                    MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)memory, out ArraySegment<byte> array) &&
                    array.Offset == 0 && array.Count == array.Array.Length)
                    return array.Array;
            }
            catch (NotSupportedException)
            {
                // A component can deny writable borrowing even when its composite is mutable.
                // Extraction is still supported through the read-only sequence below.
            }
        }

        return bytes.ToArray();
    }

    /**
     * Copies the all content of {@code src} to a {@link ByteBuf} using {@link ByteBuf#writeBytes(byte[], int, int)}.
     *
     * @param src the source string to copy
     * @param dst the destination buffer
     */
    /// <remarks>Copies raw octets, including values above 127. Advances only the writer index.
    /// The destination may grow. Keep source content and destination lifetime/layout stable during the call.</remarks>
    public static void Copy(AsciiString source, ByteBuf destination)
    {
        ArgumentNullException.ThrowIfNull(source);

        Copy(source, 0, destination, source.Length());
    }

    /**
     * Copies the content of {@code src} to a {@link ByteBuf} using {@link ByteBuf#setBytes(int, byte[], int, int)}.
     * Unlike the {@link #copy(AsciiString, ByteBuf)} and {@link #copy(AsciiString, int, ByteBuf, int)} methods,
     * this method do not increase a {@code writerIndex} of {@code dst} buffer.
     *
     * @param src the source string to copy
     * @param srcIdx the starting offset of characters to copy
     * @param dst the destination buffer
     * @param dstIdx the starting offset in the destination buffer
     * @param length the number of characters to copy
     */
    /// <remarks>The source range is relative to the string, including its backing-array offset.
    /// The destination range is bounded by Capacity; no growth or index changes occur.
    /// Overlap follows ByteBuf.SetBytes semantics. A failing composite write may have changed earlier components.</remarks>
    public static void Copy(AsciiString source, int sourceIndex, ByteBuf destination, int destinationIndex, int length)
    {
        ArgumentNullException.ThrowIfNull(source);

        ReadOnlySpan<byte> bytes = source.AsSpan().Slice(sourceIndex, length);
        ArgumentNullException.ThrowIfNull(destination);

        destination.SetBytes(destinationIndex, bytes);
    }

    /**
     * Copies the content of {@code src} to a {@link ByteBuf} using {@link ByteBuf#writeBytes(byte[], int, int)}.
     *
     * @param src the source string to copy
     * @param srcIdx the starting offset of characters to copy
     * @param dst the destination buffer
     * @param length the number of characters to copy
     */
    /// <remarks>The source range is checked before destination growth or mutation. The writer index
    /// advances only after a successful copy; reader index, marks and reference counts are unchanged.
    /// Overlap follows ByteBuf.WriteBytes semantics. A failing composite write can leave changed bytes
    /// or increased capacity even though its writer index remains unchanged.</remarks>
    public static void Copy(AsciiString source, int sourceIndex, ByteBuf destination, int length)
    {
        ArgumentNullException.ThrowIfNull(source);

        ReadOnlySpan<byte> bytes = source.AsSpan().Slice(sourceIndex, length);
        ArgumentNullException.ThrowIfNull(destination);

        destination.WriteBytes(bytes);
    }

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
            if (a.GetLong(aStartIndex) != b.GetLong(bStartIndex))
            {
                return false;
            }

        for (int bytes = length & 7; bytes > 0; bytes--)
            if (a.GetByte(aStartIndex++) != b.GetByte(bStartIndex++))
            {
                return false;
            }

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
        if (ReferenceEquals(a, b))
        {
            return true;
        }
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
        if (ReferenceEquals(a, b))
        {
            return 0;
        }
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
            if (difference != 0)
            {
                return (int)Math.Clamp(difference, int.MinValue, int.MaxValue);
            }
        }

        for (int bytes = length & 3; bytes > 0; bytes--)
        {
            int difference = a.GetByte(aIndex++) - b.GetByte(bIndex++);
            if (difference != 0)
            {
                return difference;
            }
        }

        return aLength - bLength;
    }

    private static void EnsureComparisonAccessible(ByteBuf buffer)
    {
        if (buffer.RefCnt == 0)
        {
            throw new IllegalReferenceCountException(0);
        }
    }

    // CLR Span supplies the search strategy; no Java SWAR/Two-Way performance equivalence is claimed.
    /**
         * Returns the reader index of needle in haystack, or -1 if needle is not in haystack.
         * This method uses the <a href="https://en.wikipedia.org/wiki/Two-way_string-matching_algorithm">Two-Way
         * string matching algorithm</a>, which yields O(1) space complexity and excellent performance.
         */
    public static int IndexOf(ByteBuf needle, ByteBuf haystack)
    {
        if (needle == null || haystack == null || needle.ReadableBytes > haystack.ReadableBytes)
        {
            return -1;
        }
        if (needle.ReadableBytes == 0)
        {
            return 0;
        }

        // CLR: materialize logical scalar bytes for aggregates; keep the contiguous storage Span search path.
        ReadOnlySpan<byte> input = needle.SupportsDirectSearch && needle.TryGetReadOnlyMemory(needle.ReaderIndex, needle.ReadableBytes, out var needleMemory)
            ? needleMemory.Span : SearchBytes(needle);
        ReadOnlySpan<byte> data = haystack.SupportsDirectSearch && haystack.TryGetReadOnlyMemory(haystack.ReaderIndex, haystack.ReadableBytes, out var haystackMemory)
            ? haystackMemory.Span : SearchBytes(haystack);
        int found = data.IndexOf(input);
        return found < 0 ? -1 : haystack.ReaderIndex + found;
    }

    private static byte[] SearchBytes(ByteBuf buffer)
    {
        byte[] bytes = new byte[buffer.ReadableBytes];

        for (int i = 0; i < bytes.Length; ++i)
        {
            bytes[i] = buffer.GetByte(buffer.ReaderIndex + i);
        }

        return bytes;
    }

    /**
         * The default implementation of {@link ByteBuf#indexOf(int, int, byte)}.
         * This method is useful when implementing a new buffer type.
         */
    public static int IndexOf(ByteBuf buffer, int fromIndex, int toIndex, byte value)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        return buffer.IndexOf(fromIndex, toIndex, value);
    }

    /**
         * Returns max bytes length of UTF8 character sequence of the given length.
         */
    public static int Utf8MaxBytes(int sequenceLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sequenceLength);

        return checked(sequenceLength * 3);
    }

    /**
         * Returns max bytes length of UTF8 character sequence.
         * <p>
         * It behaves like {@link #utf8MaxBytes(int)} applied to {@code seq} {@link CharSequence#length()}.
         */
    public static int Utf8MaxBytes(ReadOnlySpan<char> text)
    {
        return Utf8MaxBytes(text.Length);
    }

    /**
         * Returns max bytes length of UTF8 character sequence.
         * <p>
         * It behaves like {@link #utf8MaxBytes(int)} applied to {@code seq} {@link CharSequence#length()}.
         */
    public static int Utf8MaxBytes(AsciiString text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.Length();
    }

    /**
         * Returns the exact bytes length of UTF8 character sequence.
         * <p>
         * This method is producing the exact length according to {@link #writeUtf8(ByteBuf, CharSequence)}.
         */
    public static int Utf8Bytes(ReadOnlySpan<char> text)
    {
        int i = 0;

        // ASCII fast path
        while (i < text.Length && text[i] < 0x80)
        {
            ++i;
        }

        int length = i;

        // !ASCII is packed in a separate method to let the ASCII case be smaller
        // CLR: keep the remainder in this method; no JVM inlining policy is reproduced.
        for (; i < text.Length; ++i)
        {
            char c = text[i];
            // making it 100% branchless isn't rewarding due to the many bit operations necessary!
            if (c < 0x800)
            {
                // branchless version of: (c <= 127 ? 0:1) + 1
                // CLR: use the direct conditional and checked arithmetic.
                length = checked(length + (c <= 127 ? 1 : 2));
            }
            else if (char.IsSurrogate(c))
            {
                if (!char.IsHighSurrogate(c))
                {
                    length = checked(length + 1);
                    // WRITE_UTF_UNKNOWN
                    continue;
                }

                // Surrogate Pair consumes 2 characters.
                if (++i == text.Length)
                {
                    length = checked(length + 1);
                    // WRITE_UTF_UNKNOWN
                    break;
                }

                if (!char.IsLowSurrogate(text[i]))
                {
                    // WRITE_UTF_UNKNOWN + (Character.isHighSurrogate(c2) ? WRITE_UTF_UNKNOWN : c2)
                    length = checked(length + 2);
                    continue;
                }

                // See https://www.unicode.org/versions/Unicode7.0.0/ch03.pdf#G2630.
                length = checked(length + 4);
            }
            else
            {
                length = checked(length + 3);
            }
        }

        return length;
    }

    /**
         * Returns the exact bytes length of UTF8 character sequence.
         * <p>
         * This method is producing the exact length according to {@link #writeUtf8(ByteBuf, CharSequence)}.
         */
    public static int Utf8Bytes(AsciiString text)
    {
        return Utf8MaxBytes(text);
    }

    /**
         * Encode a {@link CharSequence} in <a href="https://en.wikipedia.org/wiki/UTF-8">UTF-8</a> and write
         * it to a {@link ByteBuf}.
         * <p>
         * It behaves like {@link #reserveAndWriteUtf8(ByteBuf, CharSequence, int)} with {@code reserveBytes}
         * computed by {@link #utf8MaxBytes(CharSequence)}.<br>
         * This method returns the actual number of bytes written.
         */
    public static int WriteUtf8(ByteBuf buffer, ReadOnlySpan<char> text)
    {
        return ReserveAndWriteUtf8(buffer, text, Utf8MaxBytes(text));
    }

    /**
         * Encode a {@link CharSequence} in <a href="https://en.wikipedia.org/wiki/UTF-8">UTF-8</a> and write
         * it into {@code reserveBytes} of a {@link ByteBuf}.
         * <p>
         * The {@code reserveBytes} must be computed (ie eagerly using {@link #utf8MaxBytes(CharSequence)}
         * or exactly with {@link #utf8Bytes(CharSequence)}) to ensure this method to not fail: for performance reasons
         * the index checks will be performed using just {@code reserveBytes}.<br>
         * This method returns the actual number of bytes written.
         */
    public static int ReserveAndWriteUtf8(ByteBuf buffer, ReadOnlySpan<char> text, int reserveBytes)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(reserveBytes);

        // CLR: reject an undersized reservation before modifying storage; Java's unchecked path can overrun it.
        int length = Utf8Bytes(text);
        if (reserveBytes < length)
        {
            throw new ArgumentOutOfRangeException(nameof(reserveBytes));
        }

        using var lease = buffer.PinMemoryForWrite();
        buffer.EnsureWritable(reserveBytes);
        int written;

        if (buffer.TryGetMemory(buffer.WriterIndex, length, out var memory))
        {
            written = EncodeUtf8(text, memory.Span);
        }
        else
        {
            byte[] bytes = new byte[length];
            written = EncodeUtf8(text, bytes);
            buffer.SetBytes(buffer.WriterIndex, bytes.AsSpan(0, written));
        }

        buffer.WriterIndex += written;
        return written;
    }

    /**
         * Encode a {@link CharSequence} in <a href="https://en.wikipedia.org/wiki/UTF-8">UTF-8</a> and write
         * it to a {@link ByteBuf}.
         * <p>
         * It behaves like {@link #reserveAndWriteUtf8(ByteBuf, CharSequence, int)} with {@code reserveBytes}
         * computed by {@link #utf8MaxBytes(CharSequence)}.<br>
         * This method returns the actual number of bytes written.
         */
    public static int WriteUtf8(ByteBuf buffer, AsciiString text)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(text);

        // The public Java writeUtf8 uses utf8MaxBytes(int), even for AsciiString's raw-octet fast path.
        return ReserveAndWriteUtf8Bytes(buffer, text.AsSpan(), Utf8MaxBytes(text.Length()));
    }

    // safe byte[] Fast-Path implementation
    // CLR: the same wire kernel targets a bounded span for both managed and native storage.
    private static int EncodeUtf8(ReadOnlySpan<char> text, Span<byte> destination)
    {
        int written = 0;

        // We can use the _set methods as these not need to do any index checks and reference checks.
        // This is possible as we called ensureWritable(...) before.
        // CLR: the bounded destination span replaces the unchecked Java _set methods.
        for (int i = 0; i < text.Length; ++i)
        {
            char c = text[i];
            if (c < 0x80)
            {
                destination[written++] = (byte)c;
            }
            else if (c < 0x800)
            {
                destination[written++] = (byte)(0xc0 | (c >> 6));
                destination[written++] = (byte)(0x80 | (c & 0x3f));
            }
            else if (char.IsSurrogate(c))
            {
                if (!char.IsHighSurrogate(c))
                {
                    destination[written++] = (byte)'?';
                    continue;
                }

                // Surrogate Pair consumes 2 characters.
                if (++i == text.Length)
                {
                    destination[written++] = (byte)'?';
                    break;
                }

                // Extra method is copied here to NOT allow inlining of writeUtf8
                // and increase the chance to inline CharSequence::charAt instead
                // CLR: no JVM inlining strategy is reproduced; the original comment records the source rationale.
                char c2 = text[i];
                if (!char.IsLowSurrogate(c2))
                {
                    destination[written++] = (byte)'?';
                    destination[written++] = char.IsHighSurrogate(c2) ? (byte)'?' : unchecked((byte)c2);
                }
                else
                {
                    int codePoint = char.ConvertToUtf32(c, c2);
                    // See https://www.unicode.org/versions/Unicode7.0.0/ch03.pdf#G2630.
                    destination[written++] = (byte)(0xf0 | (codePoint >> 18));
                    destination[written++] = (byte)(0x80 | ((codePoint >> 12) & 0x3f));
                    destination[written++] = (byte)(0x80 | ((codePoint >> 6) & 0x3f));
                    destination[written++] = (byte)(0x80 | (codePoint & 0x3f));
                }
            }
            else
            {
                destination[written++] = (byte)(0xe0 | (c >> 12));
                destination[written++] = (byte)(0x80 | ((c >> 6) & 0x3f));
                destination[written++] = (byte)(0x80 | (c & 0x3f));
            }
        }

        return written;
    }

    /**
         * Encode a {@link CharSequence} in <a href="https://en.wikipedia.org/wiki/ASCII">ASCII</a> and write it
         * to a {@link ByteBuf}.
         *
         * This method returns the actual number of bytes written.
         */
    public static int WriteAscii(ByteBuf buffer, ReadOnlySpan<char> text)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        using var lease = buffer.PinMemoryForWrite();
        // ASCII uses 1 byte per char
        buffer.EnsureWritable(text.Length);
        bool contiguous = buffer.TryGetMemory(buffer.WriterIndex, text.Length, out var memory);
        Span<byte> destination = contiguous ? memory.Span : new byte[text.Length];

        for (int i = 0; i < text.Length; ++i)
        {
            destination[i] = AsciiString.C2b(text[i]);
        }

        if (!contiguous)
        {
            buffer.SetBytes(buffer.WriterIndex, destination);
        }

        buffer.WriterIndex += text.Length;
        return text.Length;
    }

    /**
         * Encode a {@link CharSequence} in <a href="https://en.wikipedia.org/wiki/ASCII">ASCII</a> and write it
         * to a {@link ByteBuf}.
         *
         * This method returns the actual number of bytes written.
         */
    public static int WriteAscii(ByteBuf buffer, AsciiString text)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(text);

        buffer.WriteBytes(text.AsSpan());

        return text.Length();
    }

/**
     * Returns a <a href="https://en.wikipedia.org/wiki/Hex_dump">hex dump</a>
     * of the specified buffer's readable bytes.
     */
    public static string HexDump(ByteBuf buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        return HexDump(buffer, buffer.ReaderIndex, buffer.ReadableBytes);
    }

/**
     * Returns a <a href="https://en.wikipedia.org/wiki/Hex_dump">hex dump</a>
     * of the specified buffer's sub-region.
     */
    /// <remarks>The range is absolute and may extend beyond WriterIndex up to Capacity.
    /// No indices change. Buffer lifetime and content must remain stable during formatting.</remarks>
    public static string HexDump(ByteBuf buffer, int fromIndex, int length)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        ReadOnlySequence<byte> sequence = buffer.AsReadOnlySequence(fromIndex, length);
        int characters = checked(length * 2);
        if (sequence.IsSingleSegment)
        {
            return HexDump(sequence.FirstSpan);
        }

        return string.Create(characters, sequence, static (destination, bytes) =>
        {
            const string digits = "0123456789abcdef";
            int i = 0;

            foreach (ReadOnlyMemory<byte> segment in bytes)
                foreach (byte value in segment.Span)
                {
                    destination[i++] = digits[value >> 4];
                    destination[i++] = digits[value & 15];
                }
        });
    }

/**
     * Returns a <a href="https://en.wikipedia.org/wiki/Hex_dump">hex dump</a>
     * of the specified byte array.
     */
    public static string HexDump(byte[] array)
    {
        ArgumentNullException.ThrowIfNull(array);

        return HexDump(array.AsSpan());
    }

/**
     * Returns a <a href="https://en.wikipedia.org/wiki/Hex_dump">hex dump</a>
     * of the specified byte array's sub-region.
     */
    public static string HexDump(byte[] array, int fromIndex, int length)
    {
        ArgumentNullException.ThrowIfNull(array);

        CheckHexRange(array.Length, fromIndex, length);

        return HexDump(array.AsSpan(fromIndex, length));
    }

    /// <summary>Formats every octet as two lowercase ASCII hex digits without separators.</summary>
    public static string HexDump(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexStringLower(bytes);
    }

/**
     * Decode a 2-digit hex byte from within a string.
     */
    public static byte DecodeHexByte(string text, int position)
    {
        return StringUtil.DecodeHexByte(text, position);
    }

    /// <summary>Decodes exactly two ASCII hex digits at the specified character position.</summary>
    public static byte DecodeHexByte(ReadOnlySpan<char> text, int position)
    {
        CheckHexRange(text.Length, position, 2);
        int hi = StringUtil.DecodeHexNibble(text[position]);
        int lo = StringUtil.DecodeHexNibble(text[position + 1]);
        if (hi < 0 || lo < 0)
        {
            throw new ArgumentException("Expected two ASCII hex digits.", nameof(text));
        }

        return (byte)((hi << 4) | lo);
    }

/**
     * Decodes a string generated by {@link #hexDump(byte[])}
     */
    public static byte[] DecodeHexDump(string hexDump)
    {
        ArgumentNullException.ThrowIfNull(hexDump);

        return DecodeHexDump(hexDump.AsSpan());
    }

/**
     * Decodes part of a string generated by {@link #hexDump(byte[])}
     */
    public static byte[] DecodeHexDump(string hexDump, int fromIndex, int length)
    {
        ArgumentNullException.ThrowIfNull(hexDump);

        CheckHexRange(hexDump.Length, fromIndex, length);

        return DecodeHexDump(hexDump.AsSpan(fromIndex, length));
    }

    /// <summary>Decodes ASCII hex pairs; whitespace, separators and prefixes are rejected.</summary>
    public static byte[] DecodeHexDump(ReadOnlySpan<char> hexDump)
    {
        if ((hexDump.Length & 1) != 0)
        {
            throw new ArgumentException("The hex length must be even.", nameof(hexDump));
        }
        if (hexDump.IsEmpty)
        {
            return Array.Empty<byte>();
        }

        byte[] result = new byte[hexDump.Length / 2];

        for (int i = 0; i < result.Length; i++)
        {
            result[i] = DecodeHexByte(hexDump, i * 2);
        }

        return result;
    }

/**
     * Returns a multi-line hexadecimal dump of the specified {@link ByteBuf} that is easy to read by humans.
     */
    public static string PrettyHexDump(ByteBuf buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        return PrettyHexDump(buffer, buffer.ReaderIndex, buffer.ReadableBytes);
    }

/**
     * Returns a multi-line hexadecimal dump of the specified {@link ByteBuf} that is easy to read by humans,
     * starting at the given {@code offset} using the given {@code length}.
     */
    public static string PrettyHexDump(ByteBuf buffer, int offset, int length)
    {
        var dump = new StringBuilder();
        AppendPrettyHexDump(dump, buffer, offset, length);

        return dump.ToString();
    }

/**
     * Appends the prettified multi-line hexadecimal dump of the specified {@link ByteBuf} to the specified
     * {@link StringBuilder} that is easy to read by humans.
     */
    public static void AppendPrettyHexDump(StringBuilder dump, ByteBuf buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        AppendPrettyHexDump(dump, buffer, buffer.ReaderIndex, buffer.ReadableBytes);
    }

/**
     * Appends the prettified multi-line hexadecimal dump of the specified {@link ByteBuf} to the specified
     * {@link StringBuilder} that is easy to read by humans, starting at the given {@code offset} using
     * the given {@code length}.
     */
    /// <remarks>Uses StringUtil.NEWLINE and preserves the pinned Java row-label rule:
    /// rows below 4096 are relative; later labels include the absolute buffer offset.
    /// Invalid, empty and released ranges are checked before modifying the builder.</remarks>
    public static void AppendPrettyHexDump(StringBuilder dump, ByteBuf buffer, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(dump);
        ArgumentNullException.ThrowIfNull(buffer);

        var reader = new SequenceReader<byte>(buffer.AsReadOnlySequence(offset, length));
        if (length == 0)
        {
            return;
        }

        dump.Append("         +-------------------------------------------------+")
            .Append(StringUtil.NEWLINE).Append("         |  0  1  2  3  4  5  6  7  8  9  a  b  c  d  e  f |")
            .Append(StringUtil.NEWLINE).Append("+--------+-------------------------------------------------+----------------+");
        Span<byte> bytes = stackalloc byte[16];
        int fullRows = length / 16;

        // Dump the rows which have 16 bytes.
        for (int row = 0; row < fullRows; row++)
        {
            reader.TryCopyTo(bytes);
            reader.Advance(16);
            // Per-row prefix.
            // Hex dump
            // ASCII dump
            AppendPrettyHexRow(dump, bytes, row, offset + row * 16);
        }

        // Dump the last row which has less than 16 bytes.
        int remainder = length & 15;
        if (remainder != 0)
        {
            reader.TryCopyTo(bytes[..remainder]);
            // Hex dump
            // Ascii dump
            AppendPrettyHexRow(dump, bytes[..remainder], fullRows, offset + fullRows * 16);
        }

        dump.Append(StringUtil.NEWLINE).Append("+--------+-------------------------------------------------+----------------+");
    }

    /* Separate class so that the expensive static initialization is only done when needed */
    // CLR: BCL conversion and computed row padding replace HexUtil and its Java lookup tables.
    private static void AppendPrettyHexRow(StringBuilder dump, ReadOnlySpan<byte> bytes, int row, int absoluteIndex)
    {
        // Generate the lookup table for the start-offset header in each row (up to 64KiB).
        // CLR: preserve the cached-vs-fallback label rule without caching row strings.
        int label = row < 4096 ? row * 16 : absoluteIndex;
        dump.Append(StringUtil.NEWLINE).Append('|').Append(label.ToString("x8", CultureInfo.InvariantCulture)).Append('|');

        // Generate the lookup table for byte-to-hex-dump conversion
        foreach (byte value in bytes)
        {
            dump.Append(' ').Append(StringUtil.ByteToHexStringPadded(value));
        }

        // Generate the lookup table for hex dump paddings
        dump.Append(' ', (16 - bytes.Length) * 3).Append(" |");

        // Generate the lookup table for byte-to-char conversion
        foreach (byte value in bytes)
        {
            dump.Append(value is >= 32 and <= 126 ? (char)value : '.');
        }

        // Generate the lookup table for byte dump paddings
        dump.Append(' ', 16 - bytes.Length).Append('|');
    }

    private static void CheckHexRange(int capacity, int index, int length)
    {
        if (index < 0 || length < 0 || index > capacity - length)
            throw new ArgumentOutOfRangeException(nameof(index));
    }

    /**
     * Set {@link AbstractByteBuf#leakDetector}'s {@link ResourceLeakDetector.LeakListener}.
     *
     * @param leakListener If leakListener is not null, it will be notified once a ByteBuf leak is detected.
     */
    /// <remarks>Replaces the shared buffer detector's listener; null removes it.
    /// Notifications follow the existing detector's reporting policy and duplicate suppression,
    /// on the thread that drains collected leaks during a subsequent tracking call.</remarks>
    public static void SetLeakListener(ResourceLeakDetector<ByteBuf>.LeakListener leakListener)
    {
        ByteBuf.LeakDetector.SetLeakListener(leakListener);
    }

    // CLR: ByteBuf's unsuffixed word operations are always big endian. Explicit LE methods
    // replace Java's mutable byte-order facade. The unsigned 16-bit result uses ushort.
    // Accessibility is an observation, not a retain token or a guarantee against a concurrent release.
    /**
     * @return whether the specified buffer has a nonzero ref count
     */
    public static bool IsAccessible(ByteBuf buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        return buffer.RefCnt != 0;
    }

    /**
     * @throws IllegalReferenceCountException if the buffer has a zero ref count
     * @return the passed in buffer
     */
    public static ByteBuf EnsureAccessible(ByteBuf buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        int count = buffer.RefCnt;
        if (count == 0)
        {
            throw new IllegalReferenceCountException(count);
        }

        return buffer;
    }

    /**
     * Used to determine if the return value of {@link ByteBuf#ensureWritable(int, boolean)} means that there is
     * adequate space and a write operation will succeed.
     * @param ensureWritableResult The return value from {@link ByteBuf#ensureWritable(int, boolean)}.
     * @return {@code true} if {@code ensureWritableResult} means that there is adequate space and a write operation
     * will succeed.
     */
    public static bool EnsureWritableSuccess(int ensureWritableResult)
    {
        return ensureWritableResult is 0 or 2;
    }

    /**
     * Toggles the endianness of the specified 16-bit short integer.
     */
    public static short SwapShort(short value)
    {
        return BinaryPrimitives.ReverseEndianness(value);
    }

    /**
     * Toggles the endianness of the specified 24-bit medium integer.
     */
    public static int SwapMedium(int value)
    {
        int swapped = ((value & 0xff) << 16) | (value & 0xff00) | ((value >> 16) & 0xff);
        return (swapped & 0x800000) != 0 ? swapped | ~0xffffff : swapped;
    }

    /**
     * Toggles the endianness of the specified 32-bit integer.
     */
    public static int SwapInt(int value)
    {
        return BinaryPrimitives.ReverseEndianness(value);
    }

    /**
     * Toggles the endianness of the specified 64-bit long integer.
     */
    public static long SwapLong(long value)
    {
        return BinaryPrimitives.ReverseEndianness(value);
    }

    /**
     * Writes a big-endian 16-bit short integer to the buffer.
     */
    public static ByteBuf WriteShortBE(ByteBuf buffer, int value)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        return buffer.WriteShort(value);
    }

    /**
     * Sets a big-endian 16-bit short integer to the buffer.
     */
    public static ByteBuf SetShortBE(ByteBuf buffer, int index, int value)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        return buffer.SetShort(index, value);
    }

    /**
     * Writes a big-endian 24-bit medium integer to the buffer.
     */
    public static ByteBuf WriteMediumBE(ByteBuf buffer, int value)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        return buffer.WriteMedium(value);
    }

    /**
     * Reads a big-endian unsigned 16-bit short integer from the buffer.
     */
    public static ushort ReadUnsignedShortBE(ByteBuf buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        return buffer.ReadUnsignedShort();
    }

    /**
     * Reads a big-endian 32-bit integer from the buffer.
     */
    public static int ReadIntBE(ByteBuf buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        return buffer.ReadInt();
    }

    private static readonly UTF8Encoding StrictTextUtf8 = new(false, true);

/**
     * Returns {@code true} if the given {@link ByteBuf} is valid text using the given {@link Charset},
     * otherwise return {@code false}.
     *
     * @param buf The given {@link ByteBuf}.
     * @param charset The specified {@link Charset}.
     */
    /// <remarks>Tests the readable range with strict decoding, without changing the encoding,
    /// indices, marks or reference counts. Content and lifetime must remain stable during validation.</remarks>
    public static bool IsText(ByteBuf buffer, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        return IsText(buffer, buffer.ReaderIndex, buffer.ReadableBytes, encoding);
    }

/**
     * Returns {@code true} if the specified {@link ByteBuf} starting at {@code index} with {@code length} is valid
     * text using the given {@link Charset}, otherwise return {@code false}.
     *
     * @param buf The given {@link ByteBuf}.
     * @param index The start index of the specified buffer.
     * @param length The length of the specified buffer.
     * @param charset The specified {@link Charset}.
     *
     * @throws IndexOutOfBoundsException if {@code index} + {@code length} is greater than {@code buf.readableBytes}
     */
    /// <remarks>The absolute range may precede ReaderIndex but must end at WriterIndex.
    /// Invalid ranges and released buffers throw, including empty requests. Invalid encoded
    /// content returns false. A fresh strict decoder preserves state across component boundaries;
    /// the caller's Encoding and fallback remain unchanged. Keep content, layout and lifetime stable.</remarks>
    public static bool IsText(ByteBuf buffer, int index, int length, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(encoding);
        if (index < 0 || length < 0 || index > buffer.WriterIndex - length)
            throw new ArgumentOutOfRangeException(nameof(index));

        ReadOnlySequence<byte> bytes = buffer.AsReadOnlySequence(index, length);
        // Only canonical CLR instances use shortcuts. Custom encodings, even with the
        // same code page, retain their own GetDecoder implementation and byte semantics.
        if (ReferenceEquals(encoding, Encoding.UTF8))
        {
            return IsUtf8Text(bytes);
        }
        if (ReferenceEquals(encoding, Encoding.ASCII))
        {
            return IsAsciiText(bytes);
        }

        return IsEncodedText(bytes, encoding.GetDecoder());
    }

/**
     * Aborts on a byte which is not a valid ASCII character.
     */
    // CLR: Ascii.IsValid replaces the Java ByteProcessor's signed-byte check.
/**
     * Returns {@code true} if the specified {@link ByteBuf} starting at {@code index} with {@code length} is valid
     * ASCII text, otherwise return {@code false}.
     *
     * @param buf    The given {@link ByteBuf}.
     * @param index  The start index of the specified buffer.
     * @param length The length of the specified buffer.
     */
    private static bool IsAsciiText(ReadOnlySequence<byte> bytes)
    {
        foreach (ReadOnlyMemory<byte> segment in bytes)
            if (!Ascii.IsValid(segment.Span))
            {
                return false;
            }

        return true;
    }

/**
     * Returns {@code true} if the specified {@link ByteBuf} starting at {@code index} with {@code length} is valid
     * UTF8 text, otherwise return {@code false}.
     *
     * @param buf The given {@link ByteBuf}.
     * @param index The start index of the specified buffer.
     * @param length The length of the specified buffer.
     *
     * @see
     * <a href=https://www.ietf.org/rfc/rfc3629.txt>UTF-8 Definition</a>
     *
     * <pre>
     * 1. Bytes format of UTF-8
     *
     * The table below summarizes the format of these different octet types.
     * The letter x indicates bits available for encoding bits of the character number.
     *
     * Char. number range  |        UTF-8 octet sequence
     *    (hexadecimal)    |              (binary)
     * --------------------+---------------------------------------------
     * 0000 0000-0000 007F | 0xxxxxxx
     * 0000 0080-0000 07FF | 110xxxxx 10xxxxxx
     * 0000 0800-0000 FFFF | 1110xxxx 10xxxxxx 10xxxxxx
     * 0001 0000-0010 FFFF | 11110xxx 10xxxxxx 10xxxxxx 10xxxxxx
     * </pre>
     *
     * <pre>
     * 2. Syntax of UTF-8 Byte Sequences
     *
     * UTF8-octets = *( UTF8-char )
     * UTF8-char   = UTF8-1 / UTF8-2 / UTF8-3 / UTF8-4
     * UTF8-1      = %x00-7F
     * UTF8-2      = %xC2-DF UTF8-tail
     * UTF8-3      = %xE0 %xA0-BF UTF8-tail /
     *               %xE1-EC 2( UTF8-tail ) /
     *               %xED %x80-9F UTF8-tail /
     *               %xEE-EF 2( UTF8-tail )
     * UTF8-4      = %xF0 %x90-BF 2( UTF8-tail ) /
     *               %xF1-F3 3( UTF8-tail ) /
     *               %xF4 %x80-8F 2( UTF8-tail )
     * UTF8-tail   = %x80-BF
     * </pre>
     */
    private static bool IsUtf8Text(ReadOnlySequence<byte> bytes)
    {
        // CLR: Utf8.IsValid and the strict incremental decoder implement the original
        // RFC 3629 rules. Original algorithm notes are retained below; no Java state machine is needed.
        // 1 byte
        // 2 bytes
        //
        // Bit/Byte pattern
        // 110xxxxx    10xxxxxx
        // C2..DF      80..BF
        // no enough bytes
        // 2nd byte not starts with 10
        // out of lower bound
        // 3 bytes
        //
        // Bit/Byte pattern
        // 1110xxxx    10xxxxxx    10xxxxxx
        // E0          A0..BF      80..BF
        // E1..EC      80..BF      80..BF
        // ED          80..9F      80..BF
        // E1..EF      80..BF      80..BF
        // no enough bytes
        // 2nd or 3rd bytes not start with 10
        // out of lower bound
        // out of upper bound
        // 4 bytes
        //
        // Bit/Byte pattern
        // 11110xxx    10xxxxxx    10xxxxxx    10xxxxxx
        // F0          90..BF      80..BF      80..BF
        // F1..F3      80..BF      80..BF      80..BF
        // F4          80..8F      80..BF      80..BF
        // no enough bytes
        // 2nd, 3rd or 4th bytes not start with 10
        // b1 invalid
        // b2 out of lower bound
        // b2 out of upper bound
        if (bytes.IsSingleSegment)
        {
            return Utf8.IsValid(bytes.FirstSpan);
        }

        return IsEncodedText(bytes, StrictTextUtf8.GetDecoder());
    }

    private static bool IsEncodedText(ReadOnlySequence<byte> bytes, Decoder decoder)
    {
        // Validation must report malformed/unmappable bytes, regardless of replacement
        // fallback selected for ordinary string decoding. This changes only the local decoder.
        decoder.Fallback = DecoderFallback.ExceptionFallback;
        Span<char> scratch = stackalloc char[256];

        try
        {
            foreach (ReadOnlyMemory<byte> segment in bytes)
                ValidateTextChunk(decoder, segment.Span, scratch, false);

            // Flush even an empty range so an incomplete final sequence is rejected.
            ValidateTextChunk(decoder, ReadOnlySpan<byte>.Empty, scratch, true);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static void ValidateTextChunk(Decoder decoder, ReadOnlySpan<byte> bytes, Span<char> scratch, bool flush)
    {
        bool completed;

        do
        {
            decoder.Convert(bytes, scratch, flush, out int bytesUsed, out int charsUsed, out completed);
            if (!completed && bytesUsed == 0 && charsUsed == 0)
                throw new InvalidOperationException("The text decoder made no progress.");

            bytes = bytes[bytesUsed..];
        } while (!completed);
    }

    /**
     * Returns a cached thread-local direct buffer, if available.
     *
     * @return a cached thread-local direct buffer, if available.  {@code null} otherwise.
     */
    /// <remarks>Disabled by default; io.netty.threadLocalDirectBufferSize snapshots
    /// on the first call. Guaranteed physical fast-thread cleanup scopes cache one
    /// idle native owner at most. Other callers receive fresh uncached native storage.
    /// Wrappers and memory facades stay fresh; final release invalidates old borrows.
    /// Pins/operation leases delay reuse. Cross-thread returns and over-limit owners
    /// are freed. This raw transport helper does not add automatic leak wrapping.</remarks>
    public static ByteBuf ThreadLocalDirectBuffer()
    {
        if (ThreadLocalDirectSettings.Size <= 0)
        {
            return null;
        }

        return FastThreadLocalThread.CurrentThreadWillCleanupFastThreadLocals()
            ? ThreadLocalDirectCaches.Local.Get().Rent()
            : ThreadLocalDirectByteBuf.Create(null);
    }

    private static class ThreadLocalDirectSettings
    {
        internal static readonly int Size = SystemPropertyUtil.GetInt("io.netty.threadLocalDirectBufferSize", 0);
        static ThreadLocalDirectSettings()
        {
        }
    }

    private static class ThreadLocalDirectCaches
    {
        internal static readonly LocalCache Local = new();
        static ThreadLocalDirectCaches()
        {
        }
    }

    private sealed class LocalCache : FastThreadLocal<ThreadLocalDirectBufferCache>
    {
        protected override ThreadLocalDirectBufferCache InitialValue()
        {
            return new(ThreadLocalDirectSettings.Size);
        }

        protected override void OnRemoval(ThreadLocalDirectBufferCache cache)
        {
            cache.Dispose();
        }
    }

    /**
     * Equivalent to <code>{@link #utf8Bytes(CharSequence) utf8Bytes(seq.subSequence(start, end))}</code>
     * but avoids subsequence object allocation.
     * <p>
     * This method is producing the exact length according to {@link #writeUtf8(ByteBuf, CharSequence, int, int)}.
     */
    public static int Utf8Bytes(ReadOnlySpan<char> text, int start, int end)
    {
        return Utf8Bytes(Utf8Range(text, start, end));
    }

    /// <summary>Counts the raw octets in the logical [start, end) range, including values above 127.</summary>
    public static int Utf8Bytes(AsciiString text, int start, int end)
    {
        return Utf8Range(text, start, end).Length;
    }

    /**
     * Equivalent to <code>{@link #writeUtf8(ByteBuf, CharSequence) writeUtf8(buf, seq.subSequence(start, end))}</code>
     * but avoids subsequence object allocation.
     */
    public static int WriteUtf8(ByteBuf buffer, ReadOnlySpan<char> text, int start, int end)
    {
        return WriteUtf8(buffer, Utf8Range(text, start, end));
    }

    /// <summary>Writes raw octets in [start, end), reserving three bytes per selected character as in Netty.</summary>
    public static int WriteUtf8(ByteBuf buffer, AsciiString text, int start, int end)
    {
        ReadOnlySpan<byte> bytes = Utf8Range(text, start, end);
        return ReserveAndWriteUtf8Bytes(buffer, bytes, Utf8MaxBytes(bytes.Length));
    }

    /**
     * Equivalent to <code>{@link #reserveAndWriteUtf8(ByteBuf, CharSequence, int)
     * reserveAndWriteUtf8(buf, seq.subSequence(start, end), reserveBytes)}</code> but avoids
     * subsequence object allocation if possible.
     *
     * @return actual number of bytes written
     */
    public static int ReserveAndWriteUtf8(ByteBuf buffer, ReadOnlySpan<char> text, int start, int end, int reserveBytes)
    {
        return ReserveAndWriteUtf8(buffer, Utf8Range(text, start, end), reserveBytes);
    }

    /// <summary>Writes raw octets in the logical [start, end) range using an explicit reservation.</summary>
    public static int ReserveAndWriteUtf8(ByteBuf buffer, AsciiString text, int start, int end, int reserveBytes)
    {
        return ReserveAndWriteUtf8Bytes(buffer, Utf8Range(text, start, end), reserveBytes);
    }

    /// <summary>Writes all raw AsciiString octets using an explicit reservation.</summary>
    /// <remarks>Values above 127 are copied unchanged. The reservation must cover the selected octets;
    /// an undersized reservation fails before growth or mutation, following the CLR UTF-8 writer policy.</remarks>
    public static int ReserveAndWriteUtf8(ByteBuf buffer, AsciiString text, int reserveBytes)
    {
        ArgumentNullException.ThrowIfNull(text);

        return ReserveAndWriteUtf8Bytes(buffer, text.AsSpan(), reserveBytes);
    }

    private static int ReserveAndWriteUtf8Bytes(ByteBuf buffer, ReadOnlySpan<byte> bytes, int reserveBytes)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(reserveBytes);
        if (reserveBytes < bytes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(reserveBytes));
        }

        using var lease = buffer.PinMemoryForWrite();
        buffer.EnsureWritable(reserveBytes);
        buffer.SetBytes(buffer.WriterIndex, bytes);
        buffer.WriterIndex += bytes.Length;
        return bytes.Length;
    }

    // Validate endpoints before subtraction, including when overflow checking is enabled.
    private static void CheckUtf8Range(int length, int start, int end)
    {
        if (start < 0 || start > length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }
        if (end < start || end > length)
        {
            throw new ArgumentOutOfRangeException(nameof(end));
        }
    }

    private static ReadOnlySpan<char> Utf8Range(ReadOnlySpan<char> text, int start, int end)
    {
        CheckUtf8Range(text.Length, start, end);

        return text.Slice(start, end - start);
    }

    private static ReadOnlySpan<byte> Utf8Range(AsciiString text, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(text);

        CheckUtf8Range(text.Length(), start, end);

        return text.AsSpan().Slice(start, end - start);
    }
}
