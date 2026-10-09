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
using System.Text.Unicode;

namespace Netty.NET.Buffer;

public static partial class ByteBufUtil
{
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
        if (ReferenceEquals(encoding, Encoding.UTF8)) return IsUtf8Text(bytes);
        if (ReferenceEquals(encoding, Encoding.ASCII)) return IsAsciiText(bytes);
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
            if (!Ascii.IsValid(segment.Span)) return false;
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
        if (bytes.IsSingleSegment) return Utf8.IsValid(bytes.FirstSpan);
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
        catch (DecoderFallbackException) { return false; }
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
}
