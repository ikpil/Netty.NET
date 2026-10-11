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
     * Equivalent to <code>{@link #utf8Bytes(CharSequence) utf8Bytes(seq.subSequence(start, end))}</code>
     * but avoids subsequence object allocation.
     * <p>
     * This method is producing the exact length according to {@link #writeUtf8(ByteBuf, CharSequence, int, int)}.
     */
    public static int Utf8Bytes(ReadOnlySpan<char> text, int start, int end)
        => Utf8Bytes(Utf8Range(text, start, end));

    /// <summary>Counts the raw octets in the logical [start, end) range, including values above 127.</summary>
    public static int Utf8Bytes(AsciiString text, int start, int end)
        => Utf8Range(text, start, end).Length;

    /**
     * Equivalent to <code>{@link #writeUtf8(ByteBuf, CharSequence) writeUtf8(buf, seq.subSequence(start, end))}</code>
     * but avoids subsequence object allocation.
     */
    public static int WriteUtf8(ByteBuf buffer, ReadOnlySpan<char> text, int start, int end)
        => WriteUtf8(buffer, Utf8Range(text, start, end));

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
        => ReserveAndWriteUtf8(buffer, Utf8Range(text, start, end), reserveBytes);

    /// <summary>Writes raw octets in the logical [start, end) range using an explicit reservation.</summary>
    public static int ReserveAndWriteUtf8(ByteBuf buffer, AsciiString text, int start, int end, int reserveBytes)
        => ReserveAndWriteUtf8Bytes(buffer, Utf8Range(text, start, end), reserveBytes);

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
        if (reserveBytes < bytes.Length) throw new ArgumentOutOfRangeException(nameof(reserveBytes));
        using var lease = buffer.PinMemoryForWrite();
        buffer.EnsureWritable(reserveBytes);
        buffer.SetBytes(buffer.WriterIndex, bytes);
        buffer.WriterIndex += bytes.Length;
        return bytes.Length;
    }

    // Validate endpoints before subtraction, including when overflow checking is enabled.
    private static void CheckUtf8Range(int length, int start, int end)
    {
        if (start < 0 || start > length) throw new ArgumentOutOfRangeException(nameof(start));
        if (end < start || end > length) throw new ArgumentOutOfRangeException(nameof(end));
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
