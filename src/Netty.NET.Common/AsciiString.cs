/*
 * Copyright 2014 The Netty Project
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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.MathUtil;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Common;

/**
 * A string which has been encoded into a character encoding whose character always takes a single byte, similarly to
 * ASCII. It internally keeps its content in a byte array unlike {@link String}, which uses a character array, for
 * reduced memory footprint and faster data transfer from/to byte-based data structures such as a byte array and
 * {@link ByteBuffer}. It is often used in conjunction with {@code Headers} that require a {@link CharSequence}.
 * <p>
 * This class was designed to provide an immutable array of bytes, and caches some internal state based upon the value
 * of this array. However underlying access to this byte array is provided via not copying the array on construction or
 * {@link #array()}. If any changes are made to the underlying byte array it is the user's responsibility to call
 * {@link #arrayChanged()} so the state of this class can be reset.
 */
public sealed class AsciiString : ICharSequence, IEquatable<AsciiString>, IComparable<AsciiString>, IComparable
{
    public static readonly AsciiString EMPTY_STRING = Cached(string.Empty);
    private static readonly char MAX_CHAR_VALUE = (char)255;

    public static readonly int INDEX_NOT_FOUND = -1;

    public static readonly IEqualityComparer<ICharSequence> CASE_INSENSITIVE_HASHER = new CaseInsensitiveHashingStrategy();
    public static readonly IEqualityComparer<ICharSequence> CASE_SENSITIVE_HASHER = new CaseSensitiveHashingStrategy();

    public int Count => _length;

    /**
     * If this value is modified outside the constructor then call {@link #arrayChanged()}.
     */
    private readonly byte[] _value;

    /**
     * Offset into {@link #value} that all operations should use when acting upon {@link #value}.
     */
    private readonly int _offset;

    /**
     * Length in bytes for {@link #value} that we care about. This is independent from {@code value.length}
     * because we may be looking at a subsection of the array.
     */
    private readonly int _length;

    /**
     * The hash code is cached after it is first computed. It can be reset with {@link #arrayChanged()}.
     */
    private int _hash;

    /**
     * Used to cache the {@link #toString()} value.
     */
    private string _string;

    /**
     * Initialize this byte string based upon a byte array. A copy will be made.
     */
    public AsciiString(byte[] value)
        : this(value, true)
    {
    }

    /**
     * Initialize this byte string based upon a byte array.
     * {@code copy} determines if a copy is made or the array is shared.
     */
    public AsciiString(byte[] value, bool copy)
        : this(value, 0, (value ?? throw new ArgumentNullException(nameof(value))).Length, copy)
    {
    }

    /**
     * Construct a new instance from a {@code byte[]} array.
     * @param copy {@code true} then a copy of the memory will be made. {@code false} the underlying memory
     * will be shared.
     */
    public AsciiString(byte[] value, int start, int length, bool copy)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (IsOutOfBounds(start, length, value.Length))
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        if (copy)
        {
            _value = value.AsSpan(start, length).ToArray();
            _offset = 0;
        }
        else
        {
            _value = value;
            _offset = start;
        }

        _length = length;
    }

    /**
     * Create a copy of the underlying storage from {@code value}.
     * The copy will start at {@link ByteBuffer#position()} and copy {@link ByteBuffer#remaining()} bytes.
     */
    // CLR adaptation: callers express ByteBuffer position/remaining as a memory
    // slice. No stream position, capacity or seek policy is involved.
    public AsciiString(ReadOnlyMemory<byte> value)
        : this(value, true)
    {
    }

    /**
     * Initialize an instance based upon the underlying storage from {@code value}.
     * There is a potential to share the underlying array storage if {@link ByteBuffer#hasArray()} is {@code true}.
     * if {@code copy} is {@code true} a copy will be made of the memory.
     * if {@code copy} is {@code false} the underlying storage will be shared, if possible.
     */
    public AsciiString(ReadOnlyMemory<byte> value, bool copy)
        : this(value, 0, value.Length, copy)
    {
    }

    /**
     * Initialize an {@link AsciiString} based upon the underlying storage from {@code value}.
     * There is a potential to share the underlying array storage if {@link ByteBuffer#hasArray()} is {@code true}.
     * if {@code copy} is {@code true} a copy will be made of the memory.
     * if {@code copy} is {@code false} the underlying storage will be shared, if possible.
     */
    // CLR adaptation: array-backed memory can be shared; custom/native memory is
    // copied into owned storage, like the original non-array ByteBuffer branch.
    // A shared array must remain valid and arrayChanged() must follow mutations.
    public AsciiString(ReadOnlyMemory<byte> value, int start, int length, bool copy)
    {
        ReadOnlyMemory<byte> slice = value.Slice(start, length);
        if (!copy && MemoryMarshal.TryGetArray(slice, out ArraySegment<byte> segment))
        {
            _value = segment.Array ?? global::System.Array.Empty<byte>();
            _offset = segment.Offset;
        }
        else
        {
            _value = slice.Span.ToArray();
            _offset = 0;
        }

        _length = length;
    }

    /// <summary>Copies characters using Netty's single-byte mapping: U+0000–U+00FF
    /// are preserved and each larger UTF-16 code unit becomes '?'.</summary>
    public AsciiString(ReadOnlySpan<char> value)
    {
        _value = new byte[value.Length];
        for (int i = 0; i < value.Length; i++)
        {
            _value[i] = C2b(value[i]);
        }
        _offset = 0;
        _length = value.Length;
    }

    public AsciiString(string value)
        : this((value ?? throw new ArgumentNullException(nameof(value))).AsSpan())
    {
    }

    public AsciiString(string value, int start, int length)
        : this((value ?? throw new ArgumentNullException(nameof(value))).AsSpan(start, length))
    {
    }

    /// <summary>Copies encoded bytes using the selected CLR Encoding, including
    /// its fallback policy. Encoding.GetBytes does not prepend a preamble.</summary>
    public AsciiString(ReadOnlySpan<char> value, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        _value = new byte[encoding.GetByteCount(value)];
        encoding.GetBytes(value, _value.AsSpan());
        _offset = 0;
        _length = _value.Length;
    }

    public AsciiString(string value, Encoding encoding)
        : this((value ?? throw new ArgumentNullException(nameof(value))).AsSpan(), encoding)
    {
    }

    public AsciiString(string value, Encoding encoding, int start, int length)
        : this((value ?? throw new ArgumentNullException(nameof(value))).AsSpan(start, length), encoding)
    {
    }

    /// <summary>Returns a read-only view of the logical bytes. It shares storage;
    /// it does not freeze an array exposed through array() or a no-copy constructor.</summary>
    public ReadOnlyMemory<byte> AsMemory() => _value.AsMemory(_offset, _length);

    /// <summary>Returns a read-only view of the logical bytes for synchronous use.</summary>
    public ReadOnlySpan<byte> AsSpan() => _value.AsSpan(_offset, _length);

    /**
     * Create a copy of {@code value} into this instance assuming ASCII encoding.
     */
    public AsciiString(char[] value)
        : this(value, 0, value.Length)
    {
    }

    /**
     * Create a copy of {@code value} into this instance assuming ASCII encoding.
     * The copy will start at index {@code start} and copy {@code length} bytes.
     */
    public AsciiString(char[] value, int start, int length)
    {
        if (IsOutOfBounds(start, length, value.Length))
        {
            throw new ArgumentOutOfRangeException("expected: " + "0 <= start(" + start + ") <= start + length(" + length
                                                  + ") <= " + "value.length(" + value.Length + ')');
        }

        _value = GC.AllocateUninitializedArray<byte>(length);
        for (int i = 0, j = start; i < length; i++, j++)
        {
            _value[i] = C2b(value[j]);
        }

        _offset = 0;
        _length = length;
    }

    /**
     * Create a copy of {@code value} into this instance using the encoding type of {@code charset}.
     */
    public AsciiString(char[] value, Encoding charset)
        : this(value, charset, 0, value.Length)
    {
    }

    /**
     * Create a copy of {@code value} into a this instance using the encoding type of {@code charset}.
     * The copy will start at index {@code start} and copy {@code length} bytes.
     */
    public AsciiString(char[] value, Encoding encoding, int start, int length)
        : this((value ?? throw new ArgumentNullException(nameof(value))).AsSpan(start, length), encoding)
    {
    }

    /**
     * Create a copy of {@code value} into this instance assuming ASCII encoding.
     */
    public AsciiString(ICharSequence value)
        : this(value, 0, value.Length())
    {
    }

    /**
     * Create a copy of {@code value} into this instance assuming ASCII encoding.
     * The copy will start at index {@code start} and copy {@code length} bytes.
     */
    public AsciiString(ICharSequence value, int start, int length)
    {
        if (IsOutOfBounds(start, length, value.Length()))
        {
            throw new ArgumentOutOfRangeException("expected: " + "0 <= start(" + start + ") <= start + length(" + length
                                                  + ") <= " + "value.length(" + value.Length() + ')');
        }

        _value = GC.AllocateUninitializedArray<byte>(length);
        for (int i = 0, j = start; i < length; i++, j++)
        {
            _value[i] = C2b(value.CharAt(j));
        }

        _offset = 0;
        _length = length;
    }

    /**
     * Create a copy of {@code value} into this instance using the encoding type of {@code charset}.
     */
    public AsciiString(ICharSequence value, Encoding encoding)
        : this(value, encoding, 0, value.Length())
    {
    }

    /**
     * Create a copy of {@code value} into this instance using the encoding type of {@code charset}.
     * The copy will start at index {@code start} and copy {@code length} bytes.
     */
    public AsciiString(ICharSequence value, Encoding encoding, int start, int length)
        : this((value ?? throw new ArgumentNullException(nameof(value))).ToString(0).AsSpan(start, length), encoding)
    {
    }

    public char this[int index] => B2c(ByteAt(index));

    /**
     * Iterates over the readable bytes of this buffer with the specified {@code processor} in ascending order.
     *
     * @return {@code -1} if the processor iterated to or beyond the end of the readable bytes.
     *         The last-visited index If the {@link ByteProcessor#process(byte)} returned {@code false}.
     */
    // Original ByteProcessor.process return contract for the visitor argument:
    /**
     * @return {@code true} if the processor wants to continue the loop and handle the next byte in the buffer.
     *         {@code false} if the processor wants to stop handling bytes and abort the loop.
     */
    public int ForEachByte(Func<byte, bool> visitor)
    {
        return ForEachByte0(0, Length(), visitor);
    }

    /**
     * Iterates over the specified area of this buffer with the specified {@code processor} in ascending order.
     * (i.e. {@code index}, {@code (index + 1)},  .. {@code (index + length - 1)}).
     *
     * @return {@code -1} if the processor iterated to or beyond the end of the specified area.
     *         The last-visited index If the {@link ByteProcessor#process(byte)} returned {@code false}.
     */
    public int ForEachByte(int index, int length, Func<byte, bool> visitor)
    {
        if (IsOutOfBounds(index, length, this.Length()))
        {
            throw new ArgumentOutOfRangeException("expected: " + "0 <= index(" + index + ") <= start + length(" + length
                                                  + ") <= " + "length(" + this.Length() + ')');
        }

        return ForEachByte0(index, length, visitor);
    }

    private int ForEachByte0(int index, int length, Func<byte, bool> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        int len = _offset + index + length;
        for (int i = _offset + index; i < len; ++i)
        {
            if (!visitor(_value[i]))
            {
                return i - _offset;
            }
        }

        return -1;
    }

    /**
     * Iterates over the readable bytes of this buffer with the specified {@code processor} in descending order.
     *
     * @return {@code -1} if the processor iterated to or beyond the beginning of the readable bytes.
     *         The last-visited index If the {@link ByteProcessor#process(byte)} returned {@code false}.
     */
    public int ForEachByteDesc(Func<byte, bool> visitor)
    {
        return ForEachByteDesc0(0, Length(), visitor);
    }

    /**
     * Iterates over the specified area of this buffer with the specified {@code processor} in descending order.
     * (i.e. {@code (index + length - 1)}, {@code (index + length - 2)}, ... {@code index}).
     *
     * @return {@code -1} if the processor iterated to or beyond the beginning of the specified area.
     *         The last-visited index If the {@link ByteProcessor#process(byte)} returned {@code false}.
     */
    public int ForEachByteDesc(int index, int length, Func<byte, bool> visitor)
    {
        if (IsOutOfBounds(index, length, this.Length()))
        {
            throw new ArgumentOutOfRangeException("expected: " + "0 <= index(" + index + ") <= start + length(" + length
                                                  + ") <= " + "length(" + this.Length() + ')');
        }

        return ForEachByteDesc0(index, length, visitor);
    }

    private int ForEachByteDesc0(int index, int length, Func<byte, bool> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        int end = _offset + index;
        for (int i = _offset + index + length - 1; i >= end; --i)
        {
            if (!visitor(_value[i]))
            {
                return i - _offset;
            }
        }

        return -1;
    }

    public byte ByteAt(int index)
    {
        // We must do a range check here to enforce the access does not go outside our sub region of the array.
        // We rely on the array access itself to pick up the array out of bounds conditions
        if (index < 0 || index >= _length)
        {
            throw new ArgumentOutOfRangeException("index: " + index + " must be in the range [0," + _length + ")");
        }

        // CLR adaptation: indexed byte access retains the logical slice check;
        // there is no JVM Unsafe strategy or array-header offset.
        return _value[index + _offset];
    }

    /**
     * Determine if this instance has 0 length.
     */
    public bool IsEmpty()
    {
        return _length == 0;
    }

    /**
     * The length in bytes of this instance.
     */
    public int Length()
    {
        return _length;
    }

    /**
     * During normal use cases the {@link AsciiString} should be immutable, but if the underlying array is shared,
     * and changes then this needs to be called.
     */
    public void ArrayChanged()
    {
        _string = null;
        _hash = 0;
    }

    /**
     * This gives direct access to the underlying storage array.
     * The {@link #toByteArray()} should be preferred over this method.
     * If the return value is changed then {@link #arrayChanged()} must be called.
     * @see #arrayOffset()
     * @see #isEntireArrayUsed()
     */
    public byte[] Array()
    {
        return _value;
    }

    /**
     * The offset into {@link #array()} for which data for this ByteString begins.
     * @see #array()
     * @see #isEntireArrayUsed()
     */
    public int ArrayOffset()
    {
        return _offset;
    }

    /**
     * Determine if the storage represented by {@link #array()} is entirely used.
     * @see #array()
     */
    public bool IsEntireArrayUsed()
    {
        return _offset == 0 && _length == _value.Length;
    }

    /**
     * Converts this string to a byte array.
     */
    public byte[] ToByteArray()
    {
        return ToByteArray(0, Length());
    }

    /**
     * Converts a subset of this string to a byte array.
     * The subset is defined by the range [{@code start}, {@code end}).
     */
    public byte[] ToByteArray(int start, int end)
    {
        return Arrays.CopyOfRange(_value, start + _offset, end + _offset);
    }

    /**
     * Copies the content of this string to a byte array.
     *
     * @param srcIdx the starting offset of characters to copy.
     * @param dst the destination byte array.
     * @param dstIdx the starting offset in the destination byte array.
     * @param length the number of characters to copy.
     */
    public void Copy(int srcIdx, byte[] dst, int dstIdx, int length)
    {
        if (IsOutOfBounds(srcIdx, length, this.Length()))
        {
            throw new ArgumentOutOfRangeException("expected: " + "0 <= srcIdx(" + srcIdx + ") <= srcIdx + length("
                                                  + length + ") <= srcLen(" + this.Length() + ')');
        }

        Arrays.Arraycopy(_value, srcIdx + _offset, CheckNotNull(dst, "dst"), dstIdx, length);
    }

    public char CharAt(int index)
    {
        return B2c(ByteAt(index));
    }

    /**
     * Compares the specified string to this string using the ASCII values of the characters. Returns 0 if the strings
     * contain the same characters in the same order. Returns a negative integer if the first non-equal character in
     * this string has an ASCII value which is less than the ASCII value of the character at the same position in the
     * specified string, or if this string is a prefix of the specified string. Returns a positive integer if the first
     * non-equal character in this string has a ASCII value which is greater than the ASCII value of the character at
     * the same position in the specified string, or if the specified string is a prefix of this string.
     *
     * @param string the string to compare.
     * @return 0 if the strings are equal, a negative integer if this string is before the specified string, or a
     *         positive integer if this string is after the specified string.
     * @throws NullPointerException if {@code string} is {@code null}.
     */
    public int CompareTo(AsciiString str)
    {
        if (Equals(str))
        {
            return 0;
        }

        int result;
        int length1 = Length();
        int length2 = str.Length();
        int minLength = Math.Min(length1, length2);
        for (int i = 0, j = ArrayOffset(); i < minLength; i++, j++)
        {
            result = B2c(_value[j]) - str.CharAt(i);
            if (result != 0)
            {
                return result;
            }
        }

        return length1 - length2;
    }

    public int CompareTo(object obj)
    {
        return CompareTo(obj as AsciiString);
    }

    /**
     * Concatenates this string and the specified string.
     *
     * @param string the string to concatenate
     * @return a new string which is the concatenation of this string and the specified string.
     */
    public AsciiString Concat(ICharSequence str)
    {
        int thisLen = Length();
        int thatLen = str.Length();
        if (thatLen == 0)
        {
            return this;
        }

        if (str is AsciiString)
        {
            AsciiString that = (AsciiString)str;
            if (IsEmpty())
            {
                return that;
            }

            byte[] newValue = GC.AllocateUninitializedArray<byte>(thisLen + thatLen);
            Arrays.Arraycopy(_value, ArrayOffset(), newValue, 0, thisLen);
            Arrays.Arraycopy(that._value, that.ArrayOffset(), newValue, thisLen, thatLen);
            return new AsciiString(newValue, false);
        }

        if (IsEmpty())
        {
            return new AsciiString(str);
        }

        {
            byte[] newValue = GC.AllocateUninitializedArray<byte>(thisLen + thatLen);
            Arrays.Arraycopy(_value, ArrayOffset(), newValue, 0, thisLen);
            for (int i = thisLen, j = 0; i < newValue.Length; i++, j++)
            {
                newValue[i] = C2b(str.CharAt(j));
            }

            return new AsciiString(newValue, false);
        }
    }

    /**
     * Compares the specified string to this string to determine if the specified string is a suffix.
     *
     * @param suffix the suffix to look for.
     * @return {@code true} if the specified string is a suffix of this string, {@code false} otherwise.
     * @throws NullPointerException if {@code suffix} is {@code null}.
     */
    public bool EndsWith(ICharSequence suffix)
    {
        int suffixLen = suffix.Length();
        return RegionMatches(Length() - suffixLen, suffix, 0, suffixLen);
    }

    /**
     * Compares the specified string to this string ignoring the case of the characters and returns true if they are
     * equal.
     *
     * @param string the string to compare.
     * @return {@code true} if the specified string is equal to this string, {@code false} otherwise.
     */
    public bool ContentEqualsIgnoreCase(ICharSequence str)
    {
        if (this == str)
        {
            return true;
        }

        if (str == null || str.Length() != Length())
        {
            return false;
        }

        if (str is AsciiString)
        {
            AsciiString other = (AsciiString)str;
            byte[] value = _value;
            if (_offset == 0 && other._offset == 0 && _length == value.Length)
            {
                byte[] otherValue = other._value;
                for (int i = 0; i < value.Length; ++i)
                {
                    if (!EqualsIgnoreCase(value[i], otherValue[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            return MisalignedEqualsIgnoreCase(other);
        }

        {
            byte[] value = _value;
            for (int i = _offset, j = 0; j < str.Length(); ++i, ++j)
            {
                if (!EqualsIgnoreCase(B2c(value[i]), str.CharAt(j)))
                {
                    return false;
                }
            }

            return true;
        }
    }


    public int HashCode(bool ignoreCase)
    {
        return ignoreCase
            ? CASE_INSENSITIVE_HASHER.GetHashCode(this)
            : GetHashCode();
    }

    private bool MisalignedEqualsIgnoreCase(AsciiString other)
    {
        byte[] value = _value;
        byte[] otherValue = other._value;
        for (int i = _offset, j = other._offset, end = _offset + _length; i < end; ++i, ++j)
        {
            if (!EqualsIgnoreCase(value[i], otherValue[j]))
            {
                return false;
            }
        }

        return true;
    }

    /**
     * Copies the characters in this string to a character array.
     *
     * @return a character array containing the characters of this string.
     */
    public char[] ToCharArray()
    {
        return ToCharArray(0, Length());
    }

    /**
     * Copies the characters in this string to a character array.
     *
     * @return a character array containing the characters of this string.
     */
    public char[] ToCharArray(int start, int end)
    {
        int length = end - start;
        if (length == 0)
        {
            return EmptyArrays.EMPTY_CHARS;
        }

        if (IsOutOfBounds(start, length, this.Length()))
        {
            throw new ArgumentOutOfRangeException("expected: " + "0 <= start(" + start + ") <= srcIdx + length("
                                                  + length + ") <= srcLen(" + this.Length() + ')');
        }

        char[] buffer = new char[length];
        for (int i = 0, j = start + ArrayOffset(); i < length; i++, j++)
        {
            buffer[i] = B2c(_value[j]);
        }

        return buffer;
    }

    /**
     * Copied the content of this string to a character array.
     *
     * @param srcIdx the starting offset of characters to copy.
     * @param dst the destination character array.
     * @param dstIdx the starting offset in the destination byte array.
     * @param length the number of characters to copy.
     */
    public void Copy(int srcIdx, char[] dst, int dstIdx, int length)
    {
        ObjectUtil.CheckNotNull(dst, "dst");

        if (IsOutOfBounds(srcIdx, length, this.Length()))
        {
            throw new ArgumentOutOfRangeException("expected: " + "0 <= srcIdx(" + srcIdx + ") <= srcIdx + length("
                                                  + length + ") <= srcLen(" + this.Length() + ')');
        }

        int dstEnd = dstIdx + length;
        for (int i = dstIdx, j = srcIdx + ArrayOffset(); i < dstEnd; i++, j++)
        {
            dst[i] = B2c(_value[j]);
        }
    }

    /**
     * Copies a range of characters into a new string.
     * @param start the offset of the first character (inclusive).
     * @return a new string containing the characters from start to the end of the string.
     * @throws IndexOutOfBoundsException if {@code start < 0} or {@code start > length()}.
     */
    public ICharSequence SubSequence(int start)
    {
        return SubSequence(start, Length());
    }

    /**
     * Copies a range of characters into a new string.
     * @param start the offset of the first character (inclusive).
     * @param end The index to stop at (exclusive).
     * @return a new string containing the characters from start to the end of the string.
     * @throws IndexOutOfBoundsException if {@code start < 0} or {@code start > length()}.
     */
    public ICharSequence SubSequence(int start, int end)
    {
        return SubSequence(start, end, true);
    }

    /**
     * Either copy or share a subset of underlying sub-sequence of bytes.
     * @param start the offset of the first character (inclusive).
     * @param end The index to stop at (exclusive).
     * @param copy If {@code true} then a copy of the underlying storage will be made.
     * If {@code false} then the underlying storage will be shared.
     * @return a new string containing the characters from start to the end of the string.
     * @throws IndexOutOfBoundsException if {@code start < 0} or {@code start > length()}.
     */
    public AsciiString SubSequence(int start, int end, bool copy)
    {
        // Validate endpoints before subtraction, including in checked builds.
        if (start < 0 || start > _length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }
        if (end < start || end > _length)
        {
            throw new ArgumentOutOfRangeException(nameof(end));
        }

        if (start == 0 && end == Length())
        {
            return this;
        }

        if (end == start)
        {
            return EMPTY_STRING;
        }

        return new AsciiString(_value, start + _offset, end - start, copy);
    }

    /**
     * Searches in this string for the index of the specified char {@code ch}.
     * The search for the char starts at the specified offset {@code start} and moves towards the end of this string.
     *
     * @param ch the char to find.
     * @param start the starting offset.
     * @return the index of the first occurrence of the specified char {@code ch} in this string,
     * -1 if found no occurrence.
     */
    public int IndexOf(char ch, int start)
    {
        start = Math.Max(0, start);
        if (ch > MAX_CHAR_VALUE || start >= _length)
        {
            return INDEX_NOT_FOUND;
        }

        // Bound the logical start before slicing; adding the backing offset can overflow.
        int index = AsSpan().Slice(start).IndexOf((byte)ch);
        return index < 0 ? INDEX_NOT_FOUND : start + index;
    }

    /**
     * Compares the specified string to this string and compares the specified range of characters to determine if they
     * are the same.
     *
     * @param thisStart the starting offset in this string.
     * @param string the string to compare.
     * @param start the starting offset in the specified string.
     * @param length the number of characters to compare.
     * @return {@code true} if the ranges of characters are equal, {@code false} otherwise
     * @throws NullPointerException if {@code string} is {@code null}.
     */
    public bool RegionMatches(int thisStart, ICharSequence str, int start, int length)
    {
        ObjectUtil.CheckNotNull(str, "string");

        if (start < 0 || str.Length() - start < length)
        {
            return false;
        }

        int thisLen = this.Length();
        if (thisStart < 0 || thisLen - thisStart < length)
        {
            return false;
        }

        if (length <= 0)
        {
            return true;
        }

        if (str is AsciiString asciiString)
        {
            return PlatformDependent.Equals(_value, thisStart + _offset, asciiString._value,
                start + asciiString._offset, length);
        }

        int thatEnd = start + length;
        for (int i = start, j = thisStart + ArrayOffset(); i < thatEnd; i++, j++)
        {
            if (B2c(_value[j]) != str.CharAt(i))
            {
                return false;
            }
        }

        return true;
    }

    public bool RegionMatchesIgnoreCase(int thisStart, ICharSequence seq, int start, int count)
    {
        Contract.Requires(seq != null);

        int thisLen = _length;
        if (thisStart < 0 || count > thisLen - thisStart)
        {
            return false;
        }

        if (start < 0 || count > seq.Count - start)
        {
            return false;
        }

        thisStart += _offset;
        int thisEnd = thisStart + count;
        while (thisStart < thisEnd)
        {
            if (!EqualsIgnoreCase(B2c(_value[thisStart++]), seq[start++]))
            {
                return false;
            }
        }

        return true;
    }

    /**
     * Compares the specified string to this string and compares the specified range of characters to determine if they
     * are the same. When ignoreCase is true, the case of the characters is ignored during the comparison.
     *
     * @param ignoreCase specifies if case should be ignored.
     * @param thisStart the starting offset in this string.
     * @param string the string to compare.
     * @param start the starting offset in the specified string.
     * @param length the number of characters to compare.
     * @return {@code true} if the ranges of characters are equal, {@code false} otherwise.
     * @throws NullPointerException if {@code string} is {@code null}.
     */
    public bool RegionMatches(bool ignoreCase, int thisStart, ICharSequence str, int start, int length)
    {
        if (!ignoreCase)
        {
            return RegionMatches(thisStart, str, start, length);
        }

        ObjectUtil.CheckNotNull(str, "string");

        int thisLen = this.Length();
        if (thisStart < 0 || length > thisLen - thisStart)
        {
            return false;
        }

        if (start < 0 || length > str.Length() - start)
        {
            return false;
        }

        thisStart += ArrayOffset();
        int thisEnd = thisStart + length;
        if (str is AsciiString asciiString)
        {
            byte[] value = _value;
            byte[] otherValue = asciiString._value;
            start += asciiString._offset;
            while (thisStart < thisEnd)
            {
                if (!EqualsIgnoreCase(value[thisStart++], otherValue[start++]))
                {
                    return false;
                }
            }

            return true;
        }

        while (thisStart < thisEnd)
        {
            if (!EqualsIgnoreCase(B2c(_value[thisStart++]), str.CharAt(start++)))
            {
                return false;
            }
        }

        return true;
    }

    /**
     * Copies this string replacing occurrences of the specified character with another character.
     *
     * @param oldChar the character to replace.
     * @param newChar the replacement character.
     * @return a new string with occurrences of oldChar replaced by newChar.
     */
    public AsciiString Replace(char oldChar, char newChar)
    {
        if (oldChar > MAX_CHAR_VALUE)
        {
            return this;
        }

        byte oldCharAsByte = C2b0(oldChar);
        byte newCharAsByte = C2b(newChar);
        int len = _offset + _length;
        for (int i = _offset; i < len; ++i)
        {
            if (_value[i] == oldCharAsByte)
            {
                byte[] buffer = GC.AllocateUninitializedArray<byte>(Length());
                Arrays.Arraycopy(_value, _offset, buffer, 0, i - _offset);
                buffer[i - _offset] = newCharAsByte;
                ++i;
                for (; i < len; ++i)
                {
                    byte oldValue = _value[i];
                    buffer[i - _offset] = oldValue != oldCharAsByte ? oldValue : newCharAsByte;
                }

                return new AsciiString(buffer, false);
            }
        }

        return this;
    }

    /**
     * Compares the specified string to this string to determine if the specified string is a prefix.
     *
     * @param prefix the string to look for.
     * @return {@code true} if the specified string is a prefix of this string, {@code false} otherwise
     * @throws NullPointerException if {@code prefix} is {@code null}.
     */
    public bool StartsWith(ICharSequence prefix)
    {
        return StartsWith(prefix, 0);
    }

    /**
     * Compares the specified string to this string, starting at the specified offset, to determine if the specified
     * string is a prefix.
     *
     * @param prefix the string to look for.
     * @param start the starting offset.
     * @return {@code true} if the specified string occurs in this string at the specified offset, {@code false}
     *         otherwise.
     * @throws NullPointerException if {@code prefix} is {@code null}.
     */
    public bool StartsWith(ICharSequence prefix, int start)
    {
        return RegionMatches(start, prefix, 0, prefix.Length());
    }

    /**
     * Converts the characters in this string to lowercase, using the default Locale.
     *
     * @return a new string containing the lowercase characters equivalent to the characters in this string.
     */
    public AsciiString ToLowerCase()
    {
        // The protocol conversion folds only A-Z, independently of culture.
        return AsciiStringUtil.ToLowerCase(this);
    }

    /**
     * Converts the characters in this string to uppercase, using the default Locale.
     *
     * @return a new string containing the uppercase characters equivalent to the characters in this string.
     */
    public AsciiString ToUpperCase()
    {
        // The protocol conversion folds only a-z, independently of culture.
        return AsciiStringUtil.ToUpperCase(this);
    }

    /**
     * Copies this string removing white space characters from the beginning and end of the string, and tries not to
     * copy if possible.
     *
     * @param c The {@link CharSequence} to trim.
     * @return a new string with characters {@code <= \\u0020} removed from the beginning and the end.
     */
    public static ICharSequence Trim(ICharSequence c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (c is AsciiString asciiString)
        {
            return asciiString.Trim();
        }

        int length = c.Length();
        int start = 0, end = length;
        if (c is StringCharSequence || c is AppendableCharSequence)
        {
            ReadOnlySpan<char> chars = c is StringCharSequence text
                ? text.AsSpan()
                : ((AppendableCharSequence)c).AsSpan();
            start = chars.IndexOfAnyExceptInRange('\0', ' ');
            if (start < 0)
            {
                start = end = length;
            }
            else
            {
                end = chars.LastIndexOfAnyExceptInRange('\0', ' ') + 1;
            }
        }
        else
        {
            while (start < end && c.CharAt(start) <= ' ') start++;
            while (end > start && c.CharAt(end - 1) <= ' ') end--;
        }

        // subSequence has an exclusive end; retain the final non-control char.
        return start == 0 && end == length ? c : c.SubSequence(start, end);
    }

    /**
     * Duplicates this string removing white space characters from the beginning and end of the
     * string, without copying.
     *
     * @return a new string with characters {@code <= \\u0020} removed from the beginning and the end.
     */
    public AsciiString Trim()
    {
        ReadOnlySpan<byte> bytes = AsSpan();
        int start = bytes.IndexOfAnyExceptInRange((byte)0, (byte)' ');
        if (start < 0)
        {
            return bytes.IsEmpty ? this : new AsciiString(_value, ArrayOffset() + bytes.Length, 0, false);
        }

        int end = bytes.LastIndexOfAnyExceptInRange((byte)0, (byte)' ') + 1;
        // CLR bytes are unsigned: keep 0x80-0xff and unchanged logical views.
        return start == 0 && end == bytes.Length
            ? this
            : new AsciiString(_value, ArrayOffset() + start, end - start, false);
    }

    /**
     * Compares a {@code CharSequence} to this {@code String} to determine if their contents are equal.
     *
     * @param a the character sequence to compare to.
     * @return {@code true} if equal, otherwise {@code false}
     */
    public bool ContentEquals(ICharSequence a)
    {
        if (this == a)
        {
            return true;
        }

        if (a == null || a.Length() != Length())
        {
            return false;
        }

        if (a is AsciiString)
        {
            return Equals(a);
        }

        for (int i = ArrayOffset(), j = 0; j < a.Length(); ++i, ++j)
        {
            if (B2c(_value[i]) != a.CharAt(j))
            {
                return false;
            }
        }

        return true;
    }

    /**
     * {@inheritDoc}
     * <p>
     * Provides a case-insensitive hash code for Ascii like byte strings.
     */
    public override int GetHashCode()
    {
        int h = _hash;
        if (h == 0)
        {
            h = PlatformDependent.HashCodeAscii(_value, _offset, _length);
            _hash = h;
        }

        return h;
    }

    public bool Equals(AsciiString other)
    {
        if (null == other)
            return false;

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Length() == other.Length() &&
               GetHashCode() == other.GetHashCode() &&
               PlatformDependent.Equals(Array(), ArrayOffset(), other.Array(), other.ArrayOffset(), Length());
    }


    public override bool Equals(object obj)
    {
        if (obj == null)
        {
            return false;
        }

        if (obj is AsciiString otherStr)
            return Equals(otherStr);

        return false;
    }

    /**
     * Translates the entire byte string to a {@link String}.
     * @see #toString(int)
     */
    public override string ToString()
    {
        string cache = _string;
        if (cache == null)
        {
            cache = ToString(0);
            _string = cache;
        }

        return cache;
    }

    public IEnumerator<char> GetEnumerator()
    {
        return new CharSequenceEnumerator(this);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /**
     * Translates the entire byte string to a {@link String} using the {@code charset} encoding.
     * @see #toString(int, int)
     */
    public string ToString(int start)
    {
        return ToString(start, Length());
    }

    /**
     * Translates the [{@code start}, {@code end}) range of this byte string to a {@link String}.
     */
    public string ToString(int start, int end)
    {
        int length = end - start;
        if (length == 0)
        {
            return "";
        }

        if (IsOutOfBounds(start, length, this.Length()))
        {
            throw new ArgumentOutOfRangeException("expected: " + "0 <= start(" + start + ") <= srcIdx + length("
                                                  + length + ") <= srcLen(" + this.Length() + ')');
        }

        //@SuppressWarnings("deprecation")
        // The original new String(bytes, 0, offset, length) widens each unsigned
        // byte directly. ASCII decoding would lose every value above 127.
        return Encoding.Latin1.GetString(_value, _offset + start, length);
    }

    public bool ParseBoolean()
    {
        return _length >= 1 && _value[_offset] != 0;
    }

    public char ParseChar()
    {
        return ParseChar(0);
    }

    public char ParseChar(int start)
    {
        if (start + 1 >= Length())
        {
            throw new ArgumentOutOfRangeException("2 bytes required to convert to character. index " +
                                                  start + " would go out of bounds.");
        }

        int startWithOffset = start + _offset;
        return (char)((B2c(_value[startWithOffset]) << 8) | B2c(_value[startWithOffset + 1]));
    }

    // CLR adaptation: parse logical byte spans without allocating a string. Keep
    // Netty's radix 2..36 grammar (optional '-', no '+', whitespace or prefixes).
    // Invalid slices/radices are argument errors; malformed numbers are format
    // errors and representable-range failures are overflow errors. TryParse leaves
    // zero on numeric failure and still rejects invalid arguments.
    public short ParseInt16(int radix = 10) => ParseInteger<short>(AsSpan(), radix);

    // start is inclusive and end is exclusive, relative to this logical view.
    public short ParseInt16(int start, int end, int radix = 10) =>
        ParseInteger<short>(NumericSlice(start, end), radix);

    public bool TryParseInt16(out short result, int radix = 10) =>
        TryParseInteger(AsSpan(), radix, out result);

    public bool TryParseInt16(int start, int end, out short result, int radix = 10) =>
        TryParseInteger(NumericSlice(start, end), radix, out result);

    public int ParseInt32(int radix = 10) => ParseInteger<int>(AsSpan(), radix);

    // start is inclusive and end is exclusive, relative to this logical view.
    public int ParseInt32(int start, int end, int radix = 10) =>
        ParseInteger<int>(NumericSlice(start, end), radix);

    public bool TryParseInt32(out int result, int radix = 10) =>
        TryParseInteger(AsSpan(), radix, out result);

    public bool TryParseInt32(int start, int end, out int result, int radix = 10) =>
        TryParseInteger(NumericSlice(start, end), radix, out result);

    public long ParseInt64(int radix = 10) => ParseInteger<long>(AsSpan(), radix);

    // start is inclusive and end is exclusive, relative to this logical view.
    public long ParseInt64(int start, int end, int radix = 10) =>
        ParseInteger<long>(NumericSlice(start, end), radix);

    public bool TryParseInt64(out long result, int radix = 10) =>
        TryParseInteger(AsSpan(), radix, out result);

    public bool TryParseInt64(int start, int end, out long result, int radix = 10) =>
        TryParseInteger(NumericSlice(start, end), radix, out result);

    private ReadOnlySpan<byte> NumericSlice(int start, int end)
    {
        if (start < 0 || start > _length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        if (end < start || end > _length)
        {
            throw new ArgumentOutOfRangeException(nameof(end));
        }

        return AsSpan().Slice(start, end - start);
    }

    private enum IntegerParseResult { Success, Invalid, Overflow }

    private static T ParseInteger<T>(ReadOnlySpan<byte> bytes, int radix)
        where T : IBinaryInteger<T>, IMinMaxValue<T>
    {
        ValidateIntegerRadix(radix);
        IntegerParseResult status = ParseIntegerCore(bytes, radix, out T result);
        return status switch
        {
            IntegerParseResult.Success => result,
            IntegerParseResult.Overflow => throw new OverflowException("Value is outside the range of " + typeof(T).Name + "."),
            _ => throw new FormatException("Input is not a valid integer.")
        };
    }

    private static bool TryParseInteger<T>(ReadOnlySpan<byte> bytes, int radix, out T result)
        where T : IBinaryInteger<T>, IMinMaxValue<T>
    {
        ValidateIntegerRadix(radix);
        return ParseIntegerCore(bytes, radix, out result) == IntegerParseResult.Success;
    }

    private static void ValidateIntegerRadix(int radix)
    {
        if (radix < 2 || radix > 36)
        {
            throw new ArgumentOutOfRangeException(nameof(radix), radix, "Radix must be between 2 and 36.");
        }
    }

    private static IntegerParseResult ParseIntegerCore<T>(ReadOnlySpan<byte> bytes, int radix, out T result)
        where T : IBinaryInteger<T>, IMinMaxValue<T>
    {
        result = T.Zero;
        if (bytes.IsEmpty)
        {
            return IntegerParseResult.Invalid;
        }

        bool negative = bytes[0] == '-';
        int start = negative ? 1 : 0;
        if (start == bytes.Length)
        {
            return IntegerParseResult.Invalid;
        }

        // Negative accumulation represents MinValue without taking its absolute
        // value. Guard both operations before evaluating them, so checked and
        // unchecked CLR builds have identical results and never wrap on overflow.
        T limit = negative ? T.MinValue : -T.MaxValue;
        T numberBase = T.CreateChecked(radix);
        T multiplyLimit = limit / numberBase;
        T accumulated = T.Zero;
        for (int i = start; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            int digit = b switch
            {
                >= (byte)'0' and <= (byte)'9' => b - '0',
                >= (byte)'A' and <= (byte)'Z' => b - 'A' + 10,
                >= (byte)'a' and <= (byte)'z' => b - 'a' + 10,
                _ => -1
            };
            if (digit < 0 || digit >= radix)
            {
                return IntegerParseResult.Invalid;
            }

            if (accumulated < multiplyLimit)
            {
                return IntegerParseResult.Overflow;
            }

            T next = accumulated * numberBase;
            T numericDigit = T.CreateChecked(digit);
            if (next < limit + numericDigit)
            {
                return IntegerParseResult.Overflow;
            }

            accumulated = next - numericDigit;
        }

        result = negative ? accumulated : -accumulated;
        return IntegerParseResult.Success;
    }

    // CLR adaptation: retain Java's floating-point input grammar, using invariant
    // BCL byte-span conversion for decimals and exact binary rounding for hex.
    // Parse throws FormatException for numeric failure; TryParse returns false
    // and zero. Overflow/underflow produce signed infinity/zero, as in Java.
    public float ParseSingle() => ParseFloatingPoint<float>(AsSpan());

    // Logical [start,end) bounds are checked even for empty input.
    public float ParseSingle(int start, int end) => ParseFloatingPoint<float>(NumericSlice(start, end));

    public bool TryParseSingle(out float result) => TryParseFloatingPoint(AsSpan(), out result);

    public bool TryParseSingle(int start, int end, out float result) =>
        TryParseFloatingPoint(NumericSlice(start, end), out result);

    public double ParseDouble() => ParseFloatingPoint<double>(AsSpan());

    public double ParseDouble(int start, int end) => ParseFloatingPoint<double>(NumericSlice(start, end));

    public bool TryParseDouble(out double result) => TryParseFloatingPoint(AsSpan(), out result);

    public bool TryParseDouble(int start, int end, out double result) =>
        TryParseFloatingPoint(NumericSlice(start, end), out result);

    private static T ParseFloatingPoint<T>(ReadOnlySpan<byte> bytes) where T : IFloatingPointIeee754<T>
    {
        if (TryParseFloatingPoint(bytes, out T result)) return result;
        throw new FormatException("Input is not a valid floating-point number.");
    }

    private static bool TryParseFloatingPoint<T>(ReadOnlySpan<byte> bytes, out T result)
        where T : IFloatingPointIeee754<T>
    {
        result = T.Zero;
        // String.trim() removes every code unit <= U+0020, including NUL.
        int start = 0;
        int end = bytes.Length;
        while (start < end && bytes[start] <= 0x20) start++;
        while (end > start && bytes[end - 1] <= 0x20) end--;
        bytes = bytes.Slice(start, end - start);
        if (bytes.IsEmpty) return false;

        bool negative = bytes[0] == '-';
        int signLength = negative || bytes[0] == '+' ? 1 : 0;
        ReadOnlySpan<byte> unsigned = bytes[signLength..];
        if (unsigned.IsEmpty) return false;
        if (unsigned.SequenceEqual("NaN"u8))
        {
            // Use the native canonical NaN; NaN payload/sign is not a text contract.
            result = T.NaN;
            return true;
        }
        if (unsigned.SequenceEqual("Infinity"u8))
        {
            result = negative ? T.NegativeInfinity : T.PositiveInfinity;
            return true;
        }

        if (unsigned[^1] is (byte)'f' or (byte)'F' or (byte)'d' or (byte)'D')
        {
            bytes = bytes[..^1];
            unsigned = bytes[signLength..];
            if (unsigned.IsEmpty) return false;
        }

        if (unsigned.Length >= 2 && unsigned[0] == '0' && unsigned[1] is (byte)'x' or (byte)'X')
        {
            return TryParseHexFloatingPoint(unsigned, negative, out result);
        }

        if (!IsDecimalFloatingPoint(unsigned)) return false;
        const NumberStyles styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint |
                                    NumberStyles.AllowExponent;
        if (T.TryParse(bytes, styles, CultureInfo.InvariantCulture, out T parsed))
        {
            result = parsed;
            return true;
        }
        return false;
    }

    private static bool IsDecimalFloatingPoint(ReadOnlySpan<byte> bytes)
    {
        int i = 0;
        int digits = 0;
        while (i < bytes.Length && bytes[i] is >= (byte)'0' and <= (byte)'9') { i++; digits++; }
        if (i < bytes.Length && bytes[i] == '.')
        {
            i++;
            while (i < bytes.Length && bytes[i] is >= (byte)'0' and <= (byte)'9') { i++; digits++; }
        }
        if (digits == 0) return false;
        if (i < bytes.Length && bytes[i] is (byte)'e' or (byte)'E')
        {
            i++;
            if (i < bytes.Length && bytes[i] is (byte)'+' or (byte)'-') i++;
            int exponentStart = i;
            while (i < bytes.Length && bytes[i] is >= (byte)'0' and <= (byte)'9') i++;
            if (i == exponentStart) return false;
        }
        return i == bytes.Length;
    }

    private static bool TryParseHexFloatingPoint<T>(ReadOnlySpan<byte> bytes, bool negative, out T result)
        where T : IFloatingPointIeee754<T>
    {
        result = T.Zero;
        bool single = typeof(T) == typeof(float);
        int precision = single ? 24 : 53;
        int bias = single ? 127 : 1023;
        int minNormal = 1 - bias;
        int minSubnormal = minNormal - (precision - 1);
        ulong sign = negative ? 1UL << (single ? 31 : 63) : 0;
        ulong leading = 0;
        int captured = 0;
        long significantBits = 0;
        int digits = 0;
        int fractionalDigits = 0;
        bool point = false;
        bool sticky = false;
        int i = 2;
        for (; i < bytes.Length && bytes[i] is not ((byte)'p' or (byte)'P'); i++)
        {
            byte b = bytes[i];
            if (b == '.')
            {
                if (point) return false;
                point = true;
                continue;
            }
            int digit = b switch
            {
                >= (byte)'0' and <= (byte)'9' => b - '0',
                >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
                >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
                _ => -1
            };
            if (digit < 0) return false;
            digits++;
            if (point) fractionalDigits++;
            if (significantBits == 0 && digit == 0) continue;
            int width = significantBits == 0 ? BitOperations.Log2((uint)digit) + 1 : 4;
            significantBits += width;
            // Only precision+1 leading bits and a sticky tail are needed. Storage
            // stays bounded even for arbitrarily long mantissas.
            int take = Math.Min(width, precision + 1 - captured);
            leading = (leading << take) | (ulong)(digit >> (width - take));
            if ((digit & ((1 << (width - take)) - 1)) != 0) sticky = true;
            captured += take;
        }
        if (digits == 0 || i == bytes.Length) return false;
        i++; // A hexadecimal literal requires a binary exponent.
        bool exponentNegative = i < bytes.Length && bytes[i] == '-';
        if (i < bytes.Length && bytes[i] is (byte)'+' or (byte)'-') i++;
        if (i == bytes.Length) return false;
        long exponent = 0;
        const long exponentLimit = 1L << 60;
        for (; i < bytes.Length; i++)
        {
            int digit = bytes[i] - '0';
            if (digit < 0 || digit > 9) return false;
            // Saturation is far beyond any offset possible in an Int32-sized span;
            // it avoids exponent overflow without imposing an input length limit.
            exponent = exponent <= (exponentLimit - digit) / 10 ? exponent * 10 + digit : exponentLimit;
        }
        if (exponentNegative) exponent = -exponent;
        long binaryExponent = significantBits - 1 + exponent - 4L * fractionalDigits;
        if (significantBits == 0 || binaryExponent < minSubnormal - 1)
        {
            result = FloatingPointFromBits<T>(sign);
            return true;
        }
        if (binaryExponent > bias)
        {
            result = negative ? T.NegativeInfinity : T.PositiveInfinity;
            return true;
        }

        int keep = binaryExponent < minNormal ? (int)(binaryExponent - minSubnormal + 1) : precision;
        ulong rounded;
        if (significantBits <= keep)
        {
            rounded = leading << (keep - captured);
        }
        else
        {
            int shift = captured - keep;
            rounded = leading >> shift;
            bool guard = ((leading >> (shift - 1)) & 1) != 0;
            bool tail = sticky || (leading & ((1UL << (shift - 1)) - 1)) != 0;
            // Round once to nearest, ties to even, including subnormal/zero ties.
            if (guard && (tail || (rounded & 1) != 0)) rounded++;
        }
        ulong bits;
        if (binaryExponent < minNormal)
        {
            // Carry into bit precision-1 is exactly the smallest normal value.
            bits = rounded;
        }
        else
        {
            if (rounded == 1UL << precision)
            {
                rounded >>= 1;
                binaryExponent++;
            }
            if (binaryExponent > bias)
            {
                result = negative ? T.NegativeInfinity : T.PositiveInfinity;
                return true;
            }
            bits = ((ulong)(binaryExponent + bias) << (precision - 1)) |
                   (rounded & ((1UL << (precision - 1)) - 1));
        }
        result = FloatingPointFromBits<T>(sign | bits);
        return true;
    }

    private static T FloatingPointFromBits<T>(ulong bits) where T : IFloatingPointIeee754<T> =>
        typeof(T) == typeof(float)
            ? T.CreateChecked(BitConverter.Int32BitsToSingle(unchecked((int)(uint)bits)))
            : T.CreateChecked(BitConverter.Int64BitsToDouble(unchecked((long)bits)));


    /**
     * Returns an {@link AsciiString} containing the given character sequence. If the given string is already a
     * {@link AsciiString}, just returns the same instance.
     */
    public static AsciiString Of(ICharSequence str)
    {
        return str is AsciiString ? (AsciiString)str : new AsciiString(str);
    }

    /**
     * Returns an {@link AsciiString} containing the given string and retains/caches the
     * input string for later use in {@link #toString()}.
     * If the input contains only Latin-1 characters (0-255), the original string is reused
     * to preserve identity for faster equality checks. Otherwise, the string is reconstructed
     * from the Latin-1 byte content to guarantee consistency (the constructor's {@link #c2b(char)}
     * converts non-Latin-1 characters to {@code '?'}).
     * Used for the constants (which already stored in the JVM's string table) and in cases
     * where the guaranteed use of the {@link #toString()} method.
     */
    // CLR adaptation: native string input preserves identity only when its UTF-16
    // characters agree with the stored single-byte content. ToString() is the observer.
    public static AsciiString Cached(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] bytes = new byte[value.Length];
        bool allLatin1 = true;
        for (int i = 0; i < bytes.Length; i++)
        {
            char c = value[i];
            bytes[i] = C2b(c);
            allLatin1 &= c <= MAX_CHAR_VALUE;
        }

        AsciiString asciiString = new AsciiString(bytes, false);
        asciiString._string = allLatin1 ? value : asciiString.ToString(0);
        return asciiString;
    }

    /**
     * Returns the case-insensitive hash code of the specified string. Note that this method uses the same hashing
     * algorithm with {@link #hashCode()} so that you can put both {@link AsciiString}s and arbitrary
     * {@link CharSequence}s into the same headers.
     */
    public static int HashCode(ICharSequence value)
    {
        if (value == null)
        {
            return 0;
        }

        if (value is AsciiString)
        {
            return value.GetHashCode();
        }

        return PlatformDependent.HashCodeAscii(value);
    }

    /**
     * Determine if {@code a} contains {@code b} in a case sensitive manner.
     */
    public static bool Contains(ICharSequence a, ICharSequence b)
    {
        return Contains(a, b, DefaultCharEqualityComparator.INSTANCE);
    }

    /**
     * Determine if {@code a} contains {@code b} in a case insensitive manner.
     */
    public static bool ContainsIgnoreCase(ICharSequence a, ICharSequence b)
    {
        return Contains(a, b, AsciiCaseInsensitiveCharEqualityComparator.INSTANCE);
    }

    /**
     * Returns {@code true} if both {@link CharSequence}'s are equals when ignore the case. This only supports 8-bit
     * ASCII.
     */
    public static bool ContentEqualsIgnoreCase(ICharSequence a, ICharSequence b)
    {
        if (a == null || b == null)
        {
            return a == b;
        }

        if (a is AsciiString)
        {
            return ((AsciiString)a).ContentEqualsIgnoreCase(b);
        }

        if (b is AsciiString)
        {
            return ((AsciiString)b).ContentEqualsIgnoreCase(a);
        }

        if (a.Length() != b.Length())
        {
            return false;
        }

        for (int i = 0; i < a.Length(); ++i)
        {
            if (!EqualsIgnoreCase(a.CharAt(i), b.CharAt(i)))
            {
                return false;
            }
        }

        return true;
    }

    /**
     * Determine if {@code collection} contains {@code value} and using
     * {@link #contentEqualsIgnoreCase(CharSequence, CharSequence)} to compare values.
     * @param collection The collection to look for and equivalent element as {@code value}.
     * @param value The value to look for in {@code collection}.
     * @return {@code true} if {@code collection} contains {@code value} according to
     * {@link #contentEqualsIgnoreCase(CharSequence, CharSequence)}. {@code false} otherwise.
     * @see #contentEqualsIgnoreCase(CharSequence, CharSequence)
     */
    public static bool ContainsContentEqualsIgnoreCase(ICollection<ICharSequence> collection, ICharSequence value)
    {
        foreach (ICharSequence v in collection)
        {
            if (ContentEqualsIgnoreCase(value, v))
            {
                return true;
            }
        }

        return false;
    }

    /**
     * Determine if {@code a} contains all of the values in {@code b} using
     * {@link #contentEqualsIgnoreCase(CharSequence, CharSequence)} to compare values.
     * @param a The collection under test.
     * @param b The values to test for.
     * @return {@code true} if {@code a} contains all of the values in {@code b} using
     * {@link #contentEqualsIgnoreCase(CharSequence, CharSequence)} to compare values. {@code false} otherwise.
     * @see #contentEqualsIgnoreCase(CharSequence, CharSequence)
     */
    public static bool ContainsAllContentEqualsIgnoreCase(ICollection<ICharSequence> a, ICollection<ICharSequence> b)
    {
        foreach (ICharSequence v in b)
        {
            if (!ContainsContentEqualsIgnoreCase(a, v))
            {
                return false;
            }
        }

        return true;
    }

    /**
     * Returns {@code true} if the content of both {@link CharSequence}'s are equals. This only supports 8-bit ASCII.
     */
    public static bool ContentEquals(ICharSequence a, ICharSequence b)
    {
        if (a == null || b == null)
        {
            return a == b;
        }

        if (a is AsciiString)
        {
            return ((AsciiString)a).ContentEquals(b);
        }

        if (b is AsciiString)
        {
            return ((AsciiString)b).ContentEquals(a);
        }

        if (a.Length() != b.Length())
        {
            return false;
        }

        for (int i = 0; i < a.Length(); ++i)
        {
            if (a.CharAt(i) != b.CharAt(i))
            {
                return false;
            }
        }

        return true;
    }

    private static AsciiString[] ToAsciiStringArray(string[] jdkResult)
    {
        AsciiString[] res = new AsciiString[jdkResult.Length];
        for (int i = 0; i < jdkResult.Length; i++)
        {
            res[i] = new AsciiString(new StringCharSequence(jdkResult[i]));
        }

        return res;
    }


    private static bool Contains(ICharSequence a, ICharSequence b, ICharEqualityComparator cmp)
    {
        if (a == null || b == null || a.Length() < b.Length())
        {
            return false;
        }

        if (b.Length() == 0)
        {
            return true;
        }

        int bStart = 0;
        for (int i = 0; i < a.Length(); ++i)
        {
            if (cmp.Equals(b.CharAt(bStart), a.CharAt(i)))
            {
                // If b is consumed then true.
                if (++bStart == b.Length())
                {
                    return true;
                }
            }
            else if (a.Length() - i < b.Length())
            {
                // If there are not enough characters left in a for b to be contained, then false.
                return false;
            }
            else
            {
                bStart = 0;
            }
        }

        return false;
    }

    private static bool RegionMatchesCharSequences(ICharSequence cs, int csStart,
        ICharSequence str, int start, int length,
        ICharEqualityComparator charEqualityComparator)
    {
        //general purpose implementation for CharSequences
        if (csStart < 0 || length > cs.Length() - csStart)
        {
            return false;
        }

        if (start < 0 || length > str.Length() - start)
        {
            return false;
        }

        int csIndex = csStart;
        int csEnd = csIndex + length;
        int stringIndex = start;

        while (csIndex < csEnd)
        {
            char c1 = cs.CharAt(csIndex++);
            char c2 = str.CharAt(stringIndex++);

            if (!charEqualityComparator.Equals(c1, c2))
            {
                return false;
            }
        }

        return true;
    }

    /**
     * This methods make regionMatches operation correctly for any chars in strings
     * @param cs the {@code CharSequence} to be processed
     * @param ignoreCase specifies if case should be ignored.
     * @param csStart the starting offset in the {@code cs} CharSequence
     * @param string the {@code CharSequence} to compare.
     * @param start the starting offset in the specified {@code string}.
     * @param length the number of characters to compare.
     * @return {@code true} if the ranges of characters are equal, {@code false} otherwise.
     */
    public static bool RegionMatches(ICharSequence cs, bool ignoreCase, int csStart,
        ICharSequence str, int start, int length)
    {
        if (cs == null || str == null)
        {
            return false;
        }

        if (cs is AsciiString)
        {
            return ((AsciiString)cs).RegionMatches(ignoreCase, csStart, str, start, length);
        }

        return ignoreCase
            ? CharUtil.RegionMatchesIgnoreCase(cs, csStart, str, start, length)
            : CharUtil.RegionMatches(cs, csStart, str, start, length);
    }

    /**
     * This is optimized version of regionMatches for string with ASCII chars only
     * @param cs the {@code CharSequence} to be processed
     * @param ignoreCase specifies if case should be ignored.
     * @param csStart the starting offset in the {@code cs} CharSequence
     * @param string the {@code CharSequence} to compare.
     * @param start the starting offset in the specified {@code string}.
     * @param length the number of characters to compare.
     * @return {@code true} if the ranges of characters are equal, {@code false} otherwise.
     */
    public static bool RegionMatchesAscii(ICharSequence cs, bool ignoreCase, int csStart,
        ICharSequence str, int start, int length)
    {
        if (cs == null || str == null)
        {
            return false;
        }

        if (!ignoreCase && cs is StringCharSequence && str is StringCharSequence)
        {
            //we don't call regionMatches from String for ignoreCase==true. It's a general purpose method,
            //which make complex comparison in case of ignoreCase==true, which is useless for ASCII-only strings.
            //To avoid applying this complex ignore-case comparison, we will use regionMatchesCharSequences
            return ((StringCharSequence)cs).RegionMatches(false, csStart, str, start, length);
        }

        if (cs is AsciiString)
        {
            return ((AsciiString)cs).RegionMatches(ignoreCase, csStart, str, start, length);
        }

        return RegionMatchesCharSequences(cs, csStart, str, start, length,
            ignoreCase ? AsciiCaseInsensitiveCharEqualityComparator.INSTANCE : DefaultCharEqualityComparator.INSTANCE);
    }

    /**
     * <p>Case in-sensitive find of the first index within a CharSequence
     * from the specified position.</p>
     *
     * <p>A {@code null} CharSequence will return {@code -1}.
     * A negative start position is treated as zero.
     * An empty ("") search CharSequence always matches.
     * A start position greater than the string length only matches
     * an empty search CharSequence.</p>
     *
     * <pre>
     * AsciiString.indexOfIgnoreCase(null, *, *)          = -1
     * AsciiString.indexOfIgnoreCase(*, null, *)          = -1
     * AsciiString.indexOfIgnoreCase("", "", 0)           = 0
     * AsciiString.indexOfIgnoreCase("aabaabaa", "A", 0)  = 0
     * AsciiString.indexOfIgnoreCase("aabaabaa", "B", 0)  = 2
     * AsciiString.indexOfIgnoreCase("aabaabaa", "AB", 0) = 1
     * AsciiString.indexOfIgnoreCase("aabaabaa", "B", 3)  = 5
     * AsciiString.indexOfIgnoreCase("aabaabaa", "B", 9)  = -1
     * AsciiString.indexOfIgnoreCase("aabaabaa", "B", -1) = 2
     * AsciiString.indexOfIgnoreCase("aabaabaa", "", 2)   = 2
     * AsciiString.indexOfIgnoreCase("abc", "", 9)        = -1
     * </pre>
     *
     * @param str  the CharSequence to check, may be null
     * @param searchStr  the CharSequence to find, may be null
     * @param startPos  the start position, negative treated as zero
     * @return the first index of the search CharSequence (always &ge; startPos),
     *  -1 if no match or {@code null} string input
     */
    public static int IndexOfIgnoreCase(ICharSequence str, ICharSequence searchStr, int startPos)
    {
        if (str == null || searchStr == null)
        {
            return INDEX_NOT_FOUND;
        }

        if (startPos < 0)
        {
            startPos = 0;
        }

        int searchStrLen = searchStr.Length();
        int endLimit = str.Length() - searchStrLen + 1;
        if (startPos > endLimit)
        {
            return INDEX_NOT_FOUND;
        }

        if (searchStrLen == 0)
        {
            return startPos;
        }

        for (int i = startPos; i < endLimit; i++)
        {
            if (RegionMatches(str, true, i, searchStr, 0, searchStrLen))
            {
                return i;
            }
        }

        return INDEX_NOT_FOUND;
    }

    /**
     * <p>Case in-sensitive find of the first index within a CharSequence
     * from the specified position. This method optimized and works correctly for ASCII CharSequences only</p>
     *
     * <p>A {@code null} CharSequence will return {@code -1}.
     * A negative start position is treated as zero.
     * An empty ("") search CharSequence always matches.
     * A start position greater than the string length only matches
     * an empty search CharSequence.</p>
     *
     * <pre>
     * AsciiString.indexOfIgnoreCase(null, *, *)          = -1
     * AsciiString.indexOfIgnoreCase(*, null, *)          = -1
     * AsciiString.indexOfIgnoreCase("", "", 0)           = 0
     * AsciiString.indexOfIgnoreCase("aabaabaa", "A", 0)  = 0
     * AsciiString.indexOfIgnoreCase("aabaabaa", "B", 0)  = 2
     * AsciiString.indexOfIgnoreCase("aabaabaa", "AB", 0) = 1
     * AsciiString.indexOfIgnoreCase("aabaabaa", "B", 3)  = 5
     * AsciiString.indexOfIgnoreCase("aabaabaa", "B", 9)  = -1
     * AsciiString.indexOfIgnoreCase("aabaabaa", "B", -1) = 2
     * AsciiString.indexOfIgnoreCase("aabaabaa", "", 2)   = 2
     * AsciiString.indexOfIgnoreCase("abc", "", 9)        = -1
     * </pre>
     *
     * @param str  the CharSequence to check, may be null
     * @param searchStr  the CharSequence to find, may be null
     * @param startPos  the start position, negative treated as zero
     * @return the first index of the search CharSequence (always &ge; startPos),
     *  -1 if no match or {@code null} string input
     */
    public static int IndexOfIgnoreCaseAscii(ICharSequence str, ICharSequence searchStr, int startPos)
    {
        if (str == null || searchStr == null)
        {
            return INDEX_NOT_FOUND;
        }

        if (startPos < 0)
        {
            startPos = 0;
        }

        int searchStrLen = searchStr.Length();
        int endLimit = str.Length() - searchStrLen + 1;
        if (startPos > endLimit)
        {
            return INDEX_NOT_FOUND;
        }

        if (searchStrLen == 0)
        {
            return startPos;
        }

        for (int i = startPos; i < endLimit; i++)
        {
            if (RegionMatchesAscii(str, true, i, searchStr, 0, searchStrLen))
            {
                return i;
            }
        }

        return INDEX_NOT_FOUND;
    }

    /**
     * <p>Finds the first index in the {@code CharSequence} that matches the
     * specified character.</p>
     *
     * @param cs  the {@code CharSequence} to be processed, not null
     * @param searchChar the char to be searched for
     * @param start  the start index, negative starts at the string start
     * @return the index where the search char was found,
     * -1 if char {@code searchChar} is not found or {@code cs == null}
     */
    //-----------------------------------------------------------------------
    public static int IndexOf(ICharSequence cs, char searchChar, int start)
    {
        if (cs is StringCharSequence)
        {
            return ((StringCharSequence)cs).IndexOf(searchChar, start);
        }
        else if (cs is AsciiString)
        {
            return ((AsciiString)cs).IndexOf(searchChar, start);
        }

        if (cs == null)
        {
            return INDEX_NOT_FOUND;
        }

        int sz = cs.Length();
        for (int i = start < 0 ? 0 : start; i < sz; i++)
        {
            if (cs.CharAt(i) == searchChar)
            {
                return i;
            }
        }

        return INDEX_NOT_FOUND;
    }

    private static bool EqualsIgnoreCase(byte a, byte b)
    {
        return a == b || AsciiStringUtil.ToLowerCase(a) == AsciiStringUtil.ToLowerCase(b);
    }

    private static bool EqualsIgnoreCase(char a, char b)
    {
        return a == b || ToLowerCase(a) == ToLowerCase(b);
    }

    /**
     * If the character is uppercase - converts the character to lowercase,
     * otherwise returns the character as it is. Only for ASCII characters.
     *
     * @return lowercase ASCII character equivalent
     */
    public static char ToLowerCase(char c)
    {
        return IsUpperCase(c) ? (char)(c + 32) : c;
    }

    private static byte ToUpperCase(byte b)
    {
        return AsciiStringUtil.ToUpperCase(b);
    }

    public static bool IsUpperCase(byte value)
    {
        return AsciiStringUtil.IsUpperCase(value);
    }

    public static bool IsUpperCase(char value)
    {
        return value >= 'A' && value <= 'Z';
    }

    public static byte C2b(char c)
    {
        return (byte)((c > MAX_CHAR_VALUE) ? '?' : c);
    }

    private static byte C2b0(char c)
    {
        return (byte)c;
    }

    public static char B2c(byte b)
    {
        return (char)(b & 0xFF);
    }
}
