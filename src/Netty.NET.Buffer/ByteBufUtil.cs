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
using Netty.NET.Common;

namespace Netty.NET.Buffer;

/**
 * A collection of utility methods that is related with handling {@link ByteBuf},
 * such as the generation of hex dump and swapping an integer's byte order.
 */
/// <summary>Implemented text, search, hex, content comparison/hash, text validation, array extraction, AsciiString copying and allocator-returning utilities. Other Netty ByteBufUtil operations remain pending.</summary>
public static partial class ByteBufUtil
{
    // CLR Span supplies the search strategy; no Java SWAR/Two-Way performance equivalence is claimed.
    /**
         * Returns the reader index of needle in haystack, or -1 if needle is not in haystack.
         * This method uses the <a href="https://en.wikipedia.org/wiki/Two-way_string-matching_algorithm">Two-Way
         * string matching algorithm</a>, which yields O(1) space complexity and excellent performance.
         */
    public static int IndexOf(ByteBuf needle, ByteBuf haystack)
    {
        if (needle == null || haystack == null || needle.ReadableBytes > haystack.ReadableBytes) return -1;
        if (needle.ReadableBytes == 0) return 0;
        // CLR: materialize only non-contiguous ranges; keep the contiguous Span search path.
        ReadOnlySpan<byte> input = needle.TryGetReadOnlyMemory(needle.ReaderIndex, needle.ReadableBytes, out var needleMemory)
            ? needleMemory.Span : needle.ReadableSequence.ToArray();
        ReadOnlySpan<byte> data = haystack.TryGetReadOnlyMemory(haystack.ReaderIndex, haystack.ReadableBytes, out var haystackMemory)
            ? haystackMemory.Span : haystack.ReadableSequence.ToArray();
        int found = data.IndexOf(input);
        return found < 0 ? -1 : haystack.ReaderIndex + found;
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
    public static int Utf8MaxBytes(ReadOnlySpan<char> text) => Utf8MaxBytes(text.Length);

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
        while (i < text.Length && text[i] < 0x80) ++i;
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
            else length = checked(length + 3);
        }
        return length;
    }

    /**
         * Returns the exact bytes length of UTF8 character sequence.
         * <p>
         * This method is producing the exact length according to {@link #writeUtf8(ByteBuf, CharSequence)}.
         */
    public static int Utf8Bytes(AsciiString text) => Utf8MaxBytes(text);

    /**
         * Encode a {@link CharSequence} in <a href="https://en.wikipedia.org/wiki/UTF-8">UTF-8</a> and write
         * it to a {@link ByteBuf}.
         * <p>
         * It behaves like {@link #reserveAndWriteUtf8(ByteBuf, CharSequence, int)} with {@code reserveBytes}
         * computed by {@link #utf8MaxBytes(CharSequence)}.<br>
         * This method returns the actual number of bytes written.
         */
    public static int WriteUtf8(ByteBuf buffer, ReadOnlySpan<char> text)
        => ReserveAndWriteUtf8(buffer, text, Utf8MaxBytes(text));

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
        if (reserveBytes < length) throw new ArgumentOutOfRangeException(nameof(reserveBytes));
        using var lease = buffer.PinMemoryForWrite();
        buffer.EnsureWritable(reserveBytes);
        int written;
        if (buffer.TryGetMemory(buffer.WriterIndex, length, out var memory)) written = EncodeUtf8(text, memory.Span);
        else
        {
            byte[] bytes = new byte[length]; written = EncodeUtf8(text, bytes);
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
        buffer.EnsureWritable(Utf8MaxBytes(text.Length()));
        return WriteAscii(buffer, text);
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
            if (c < 0x80) destination[written++] = (byte)c;
            else if (c < 0x800)
            {
                destination[written++] = (byte)(0xc0 | (c >> 6));
                destination[written++] = (byte)(0x80 | (c & 0x3f));
            }
            else if (char.IsSurrogate(c))
            {
                if (!char.IsHighSurrogate(c)) { destination[written++] = (byte)'?'; continue; }
                // Surrogate Pair consumes 2 characters.
                if (++i == text.Length) { destination[written++] = (byte)'?'; break; }
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
        for (int i = 0; i < text.Length; ++i) destination[i] = AsciiString.C2b(text[i]);
        if (!contiguous) buffer.SetBytes(buffer.WriterIndex, destination);
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
}
