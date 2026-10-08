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

public abstract partial class ByteBuf
{
    /**
         * Decodes this buffer's readable bytes into a string with the specified
         * character set name.  This method is identical to
         * {@code buf.toString(buf.readerIndex(), buf.readableBytes(), charsetName)}.
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @throws UnsupportedCharsetException
         *         if the specified character set name is not supported by the
         *         current VM
         */
    /// <summary>Decodes readable bytes using the caller's encoding and fallback policy.</summary>
    public string GetString(Encoding encoding) => GetString(_readerIndex, ReadableBytes, encoding);

    /**
         * Decodes this buffer's sub-region into a string with the specified
         * character set.  This method does not modify {@code readerIndex} or
         * {@code writerIndex} of this buffer.
         */
    /// <summary>Decodes a capacity-bounded range without changing either index.</summary>
    public string GetString(int index, int length, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        return encoding.GetString(AsSpan(index, length));
    }

    /**
         * Gets a {@link String} with the given length at the current {@code readerIndex}
         * and increases the {@code readerIndex} by the given length.
         *
         * @param length the length to read
         * @param charset that should be used
         * @return the string
         * @throws IndexOutOfBoundsException
         *         if {@code length} is greater than {@code this.readableBytes}
         */
    /// <summary>Decodes written bytes and advances the reader only after successful decoding.</summary>
    public string ReadString(int length, Encoding encoding)
    {
        CheckReadableBytes(length);
        string result = GetString(_readerIndex, length, encoding);
        _readerIndex += length;
        return result;
    }

    /**
         * Writes the specified {@link CharSequence} at the given {@code index}.
         * The {@code writerIndex} is not modified by this method.
         *
         * @param index on which the sequence should be written
         * @param sequence to write
         * @param charset that should be used.
         * @return the written number of bytes.
         * @throws IndexOutOfBoundsException
         *         if the sequence at the given index would be out of bounds of the buffer capacity
         */
    /// <summary>Encodes text without growing the buffer or changing indices. No preamble is emitted.</summary>
    public int SetString(int index, ReadOnlySpan<char> text, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        EnsureAccessible();
        int length = encoding.GetByteCount(text);
        return encoding.GetBytes(text, AsSpan(index, length));
    }

    /**
         * Writes the specified {@link CharSequence} at the current {@code writerIndex} and increases
         * the {@code writerIndex} by the written bytes.
         * If {@code this.writableBytes} is not large enough to write the whole sequence,
         * {@link #ensureWritable(int)} will be called in an attempt to expand capacity to accommodate.
         *
         * @param sequence to write
         * @param charset that should be used
         * @return the written number of bytes
         */
    /// <summary>Reserves the exact encoded length, writes text, and advances the writer after success.</summary>
    /// <remarks>The supplied Encoding controls malformed/unmappable input. No preamble is emitted.
    /// Use ByteBufUtil.WriteUtf8/WriteAscii when Netty's specialized byte mapping is required.</remarks>
    public int WriteString(ReadOnlySpan<char> text, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        // A UTF-16 span can alias native storage through MemoryMarshal.Cast. Keep it alive across growth.
        using var lease = PinMemoryForWrite();
        EnsureAccessible();
        int length = encoding.GetByteCount(text);
        EnsureWritable(length);
        int written = encoding.GetBytes(text, AsSpan(_writerIndex, length));
        _writerIndex += written;
        return written;
    }

    /**
         * Locates the first occurrence of the specified {@code value} in this
         * buffer. The search takes place from the specified {@code fromIndex}
         * (inclusive) to the specified {@code toIndex} (exclusive).
         * <p>
         * If {@code fromIndex} is greater than {@code toIndex}, the search is
         * performed in a reversed order from {@code fromIndex} (exclusive)
         * down to {@code toIndex} (inclusive).
         * <p>
         * Note that the lower index is always included and higher always excluded.
         * <p>
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @return the absolute index of the first occurrence if found.
         *         {@code -1} otherwise.
         */
    public virtual int IndexOf(int fromIndex, int toIndex, byte value)
    {
        if (fromIndex <= toIndex)
        {
            fromIndex = Math.Max(fromIndex, 0);
            if (fromIndex >= toIndex || Capacity == 0) return -1;
            int found = AsSpan(fromIndex, checked(toIndex - fromIndex)).IndexOf(value);
            return found < 0 ? -1 : fromIndex + found;
        }

        fromIndex = Math.Min(fromIndex, Capacity);
        if (fromIndex <= 0) return -1; // fromIndex is the exclusive upper bound.
        CheckIndex(toIndex, 0);
        int reverseFound = AsSpan(toIndex, checked(fromIndex - toIndex)).LastIndexOf(value);
        return reverseFound < 0 ? -1 : toIndex + reverseFound;
    }

    /**
         * Locates the first occurrence of the specified {@code value} in this
         * buffer.  The search takes place from the current {@code readerIndex}
         * (inclusive) to the current {@code writerIndex} (exclusive).
         * <p>
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @return the number of bytes between the current {@code readerIndex}
         *         and the first occurrence if found. {@code -1} otherwise.
         */
    public int BytesBefore(byte value) => BytesBefore(_readerIndex, ReadableBytes, value);

    /**
         * Locates the first occurrence of the specified {@code value} in this
         * buffer.  The search starts from the current {@code readerIndex}
         * (inclusive) and lasts for the specified {@code length}.
         * <p>
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @return the number of bytes between the current {@code readerIndex}
         *         and the first occurrence if found. {@code -1} otherwise.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code length} is greater than {@code this.readableBytes}
         */
    public int BytesBefore(int length, byte value)
    {
        CheckReadableBytes(length);
        return BytesBefore(_readerIndex, length, value);
    }

    /**
         * Locates the first occurrence of the specified {@code value} in this
         * buffer.  The search starts from the specified {@code index} (inclusive)
         * and lasts for the specified {@code length}.
         * <p>
         * This method does not modify {@code readerIndex} or {@code writerIndex} of
         * this buffer.
         *
         * @return the number of bytes between the specified {@code index}
         *         and the first occurrence if found. {@code -1} otherwise.
         *
         * @throws IndexOutOfBoundsException
         *         if {@code index + length} is greater than {@code this.capacity}
         */
    public int BytesBefore(int index, int length, byte value)
    {
        CheckIndex(index, length);
        return AsSpan(index, length).IndexOf(value);
    }

    /**
         * Iterates over the readable bytes of this buffer with the specified {@code processor} in ascending order.
         *
         * @return {@code -1} if the processor iterated to or beyond the end of the readable bytes.
         *         The last-visited index If the {@link ByteProcessor#process(byte)} returned {@code false}.
         */
    public int ForEachByte(Func<byte, bool> processor) => ForEachByte(_readerIndex, ReadableBytes, processor);

    /**
         * Iterates over the specified area of this buffer with the specified {@code processor} in ascending order.
         * (i.e. {@code index}, {@code (index + 1)},  .. {@code (index + length - 1)})
         *
         * @return {@code -1} if the processor iterated to or beyond the end of the specified area.
         *         The last-visited index If the {@link ByteProcessor#process(byte)} returned {@code false}.
         */
    public int ForEachByte(int index, int length, Func<byte, bool> processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        CheckIndex(index, length);
        int end = index + length;
        // Reacquire each byte: a callback can resize or release native storage. Never retain a span across callbacks.
        for (int i = index; i < end; ++i)
            if (!processor(GetByte(i))) return i;
        return -1;
    }

    /**
         * Iterates over the readable bytes of this buffer with the specified {@code processor} in descending order.
         *
         * @return {@code -1} if the processor iterated to or beyond the beginning of the readable bytes.
         *         The last-visited index If the {@link ByteProcessor#process(byte)} returned {@code false}.
         */
    public int ForEachByteDesc(Func<byte, bool> processor) => ForEachByteDesc(_readerIndex, ReadableBytes, processor);

    /**
         * Iterates over the specified area of this buffer with the specified {@code processor} in descending order.
         * (i.e. {@code (index + length - 1)}, {@code (index + length - 2)}, ... {@code index})
         *
         *
         * @return {@code -1} if the processor iterated to or beyond the beginning of the specified area.
         *         The last-visited index If the {@link ByteProcessor#process(byte)} returned {@code false}.
         */
    public int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        CheckIndex(index, length);
        for (int i = index + length - 1; i >= index; --i)
            if (!processor(GetByte(i))) return i;
        return -1;
    }
}
