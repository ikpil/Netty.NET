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
using Netty.NET.Common.Internal;

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
    public virtual string ToString(Encoding charset)
    {
        return ToString(_readerIndex, ReadableBytes, charset);
    }

    /**
         * Decodes this buffer's sub-region into a string with the specified
         * character set.  This method does not modify {@code readerIndex} or
         * {@code writerIndex} of this buffer.
         */
    /// <summary>Decodes a capacity-bounded range without changing either index.</summary>
    public virtual string ToString(int index, int length, Encoding charset)
    {
        ArgumentNullException.ThrowIfNull(charset);
        if (TryGetReadOnlyMemory(index, length, out var memory)) return charset.GetString(memory.Span);
        byte[] bytes = new byte[length]; GetBytesCore(index, bytes);
        return charset.GetString(bytes);
    }

    /**
     * Gets a {@link CharSequence} with the given length at the given index.
     *
     * @param length the length to read
     * @param charset that should be used
     * @return the sequence
     * @throws IndexOutOfBoundsException
     *         if {@code length} is greater than {@code this.readableBytes}
     */
    // CLR: CharSequence is ICharSequence. .NET has no Charset identity, so US-ASCII and ISO-8859-1 are recognized by
    // their code pages (20127, 28591); any other encoding returns the decoded string as a StringCharSequence.
    public virtual ICharSequence GetCharSequence(int index, int length, Encoding charset)
    {
        ArgumentNullException.ThrowIfNull(charset);
        if (charset.CodePage is 20127 or 28591)
        {
            // ByteBufUtil.getBytes(...) will return a new copy which the AsciiString uses directly
            return new AsciiString(ByteBufUtil.GetBytes(this, index, length, true), false);
        }
        return new StringCharSequence(ToString(index, length, charset));
    }

    /**
     * Gets a {@link CharSequence} with the given length at the current {@code readerIndex}
     * and increases the {@code readerIndex} by the given length.
     *
     * @param length the length to read
     * @param charset that should be used
     * @return the sequence
     * @throws IndexOutOfBoundsException
     *         if {@code length} is greater than {@code this.readableBytes}
     */
    // CLR: as ReadString does, the readable range is checked first (the Javadoc's IndexOutOfBoundsException contract)
    // and the reader advances only after decoding succeeds.
    public virtual ICharSequence ReadCharSequence(int length, Encoding charset)
    {
        CheckReadableBytes(length);
        ICharSequence sequence = GetCharSequence(_readerIndex, length, charset);
        _readerIndex += length;
        return sequence;
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
    public virtual string ReadString(int length, Encoding charset)
    {
        CheckReadableBytes(length);
        string result = ToString(_readerIndex, length, charset);
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
    public virtual int SetCharSequence(int index, ReadOnlySpan<char> sequence, Encoding charset)
    {
        ArgumentNullException.ThrowIfNull(charset);
        EnsureCanWrite();
        int length = charset.GetByteCount(sequence);
        return EncodeString(index, sequence, charset, length);
    }

    private int EncodeString(int index, ReadOnlySpan<char> text, Encoding encoding, int length)
    {
        // Original charset fallback:
        // setBytes(...) will take care of checking the indices.
        // CLR: the bounded memory lookup below validates the range before encoding.
        if (TryGetMemory(index, length, out var memory)) return encoding.GetBytes(text, memory.Span);
        byte[] bytes = new byte[length];
        int written = encoding.GetBytes(text, bytes);
        SetBytesCore(index, bytes.AsSpan(0, written)); return written;
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
    public virtual int WriteCharSequence(ReadOnlySpan<char> sequence, Encoding charset)
    {
        ArgumentNullException.ThrowIfNull(charset);
        // A UTF-16 span can alias native storage through MemoryMarshal.Cast. Keep it alive across growth.
        using var lease = PinMemoryForWrite();
        EnsureAccessible();
        int length = charset.GetByteCount(sequence);
        EnsureWritable(length);
        int written = EncodeString(_writerIndex, sequence, charset, length);
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
            int length = checked(toIndex - fromIndex);
            CheckIndex(fromIndex, length);
            if (!SupportsDirectSearch || !TryGetReadOnlyMemory(fromIndex, length, out var memory))
            {
                // Match the original low-level search reads, independently of public GetByte overrides.
                for (int i = fromIndex; i < toIndex; ++i) if (ReadByteCore(i) == value) return i;
                return -1;
            }
            int found = memory.Span.IndexOf(value);
            return found < 0 ? -1 : fromIndex + found;
        }

        fromIndex = Math.Min(fromIndex, Capacity);
        if (fromIndex <= 0) return -1; // fromIndex is the exclusive upper bound.
        CheckIndex(toIndex, 0);
        int reverseLength = checked(fromIndex - toIndex);
        CheckIndex(toIndex, reverseLength);
        if (!SupportsDirectSearch || !TryGetReadOnlyMemory(toIndex, reverseLength, out var reverseMemory))
        {
            for (int i = fromIndex - 1; i >= toIndex; --i) if (ReadByteCore(i) == value) return i;
            return -1;
        }
        int reverseFound = reverseMemory.Span.LastIndexOf(value);
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
    public virtual int BytesBefore(byte value)
    {
        return BytesBefore(_readerIndex, ReadableBytes, value);
    }

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
    public virtual int BytesBefore(int length, byte value)
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
    public virtual int BytesBefore(int index, int length, byte value)
    {
        CheckIndex(index, length);
        int found = IndexOf(index, index + length, value);
        return found < 0 ? -1 : found - index;
    }

    /**
         * Iterates over the readable bytes of this buffer with the specified {@code processor} in ascending order.
         *
         * @return {@code -1} if the processor iterated to or beyond the end of the readable bytes.
         *         The last-visited index If the {@link ByteProcessor#process(byte)} returned {@code false}.
         */
    public virtual int ForEachByte(Func<byte, bool> processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        CheckIndex(_readerIndex, ReadableBytes);
        return ForEachByteAscending(_readerIndex, _writerIndex, processor);
    }

    /**
         * Iterates over the specified area of this buffer with the specified {@code processor} in ascending order.
         * (i.e. {@code index}, {@code (index + 1)},  .. {@code (index + length - 1)})
         *
         * @return {@code -1} if the processor iterated to or beyond the end of the specified area.
         *         The last-visited index If the {@link ByteProcessor#process(byte)} returned {@code false}.
         */
    public virtual int ForEachByte(int index, int length, Func<byte, bool> processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        CheckIndex(index, length);
        return ForEachByteAscending(index, index + length, processor);
    }

    // A physical aggregate borrow can differ from its logical scalar search representation.
    internal virtual bool SupportsDirectSearch => SupportsDirectBulkCopy;

    // Component iteration bypasses public iteration overrides after peeling transparent/derived layers.
    // The original owner still holds the claim; no reference is transferred by this lookup.
    internal virtual ByteBuf UnwrapIterationStorage(ref int index)
    {
        return this;
    }

    internal virtual int ForEachByteAscending(int start, int end, Func<byte, bool> processor)
    {
        // Use the storage kernel independently of public GetByte/indexed iteration overrides.
        // Reacquire each byte: a callback can resize or release native storage. Never retain a span across callbacks.
        for (int i = start; i < end; ++i)
            if (!processor(ReadByteCore(i))) return i;
        return -1;
    }

    /**
         * Iterates over the readable bytes of this buffer with the specified {@code processor} in descending order.
         *
         * @return {@code -1} if the processor iterated to or beyond the beginning of the readable bytes.
         *         The last-visited index If the {@link ByteProcessor#process(byte)} returned {@code false}.
         */
    public virtual int ForEachByteDesc(Func<byte, bool> processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        CheckIndex(_readerIndex, ReadableBytes);
        return ForEachByteDescending(_writerIndex - 1, _readerIndex, processor);
    }

    /**
         * Iterates over the specified area of this buffer with the specified {@code processor} in descending order.
         * (i.e. {@code (index + length - 1)}, {@code (index + length - 2)}, ... {@code index})
         *
         *
         * @return {@code -1} if the processor iterated to or beyond the beginning of the specified area.
         *         The last-visited index If the {@link ByteProcessor#process(byte)} returned {@code false}.
         */
    public virtual int ForEachByteDesc(int index, int length, Func<byte, bool> processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        CheckIndex(index, length);
        return ForEachByteDescending(index + length - 1, index, processor);
    }

    internal virtual int ForEachByteDescending(int start, int end, Func<byte, bool> processor)
    {
        for (int i = start; i >= end; --i)
            if (!processor(ReadByteCore(i))) return i;
        return -1;
    }
}
