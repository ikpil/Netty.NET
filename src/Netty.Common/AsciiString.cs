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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.MathUtil;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Common;

/**
 * A string which has been encoded into a character encoding whose character always takes a single byte, similarly to
 * ASCII. It internally keeps its content in a byte array unlike {@link string}, which uses a character array, for
 * reduced memory footprint and faster data transfer from/to byte-based data structures such as a byte array and
 * {@link ByteBuffer}. It is often used in conjunction with {@code Headers} that require a {@link ICharSequence}.
 * <p>
 * This class was designed to provide an immutable array of bytes, and caches some internal state based upon the value
 * of this array. However underlying access to this byte array is provided via not copying the array on construction or
 * {@link #array()}. If any changes are made to the underlying byte array it is the user's responsibility to call
 * {@link #arrayChanged()} so the state of this class can be reset.
 */
public sealed class AsciiString : ICharSequence, IEquatable<AsciiString>, IComparable<AsciiString>, IComparable
{
    public static readonly AsciiString EMPTY_STRING = cached(StringCharSequence.Empty);
    private static readonly char MAX_CHAR_VALUE = (char)255;

    public static readonly int INDEX_NOT_FOUND = -1;

    public const int CHARACTER_MIN_RADIX = 2;
    public const int CHARACTER_MAX_RADIX = 36;

    public static readonly IHashingStrategy<ICharSequence> CASE_INSENSITIVE_HASHER = new CaseInsensitiveHashingStrategy();
    public static readonly IHashingStrategy<ICharSequence> CASE_SENSITIVE_HASHER = new CaseSensitiveHashingStrategy();

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
        : this(value, 0, value.Length, copy)
    {
    }

    /**
     * Construct a new instance from a {@code byte[]} array.
     * @param copy {@code true} then a copy of the memory will be made. {@code false} the underlying memory
     * will be shared.
     */
    public AsciiString(byte[] value, int start, int length, bool copy)
    {
        if (copy)
        {
            byte[] rangedCopy = new byte[length];
            Arrays.arraycopy(value, start, rangedCopy, 0, rangedCopy.Length);
            _value = rangedCopy;
            _offset = 0;
        }
        else
        {
            if (isOutOfBounds(start, length, value.Length))
            {
                throw new ArgumentOutOfRangeException("expected: " + "0 <= start(" + start + ") <= start + length(" +
                                                      length + ") <= " + "value.length(" + value.Length + ')');
            }

            _value = value;
            _offset = start;
        }

        _length = length;
    }

    /**
     * Create a copy of the underlying storage from {@code value}.
     * The copy will start at {@link ByteBuffer#position()} and copy {@link ByteBuffer#remaining()} bytes.
     */
    public AsciiString(ArraySegment<byte> value)
        : this(value, true)
    {
    }

    /**
     * Initialize an instance based upon the underlying storage from {@code value}.
     * There is a potential to share the underlying array storage if {@link ByteBuffer#hasArray()} is {@code true}.
     * if {@code copy} is {@code true} a copy will be made of the memory.
     * if {@code copy} is {@code false} the underlying storage will be shared, if possible.
     */
    public AsciiString(ArraySegment<byte> value, bool copy)
        : this(value, value.position(), value.remaining(), copy)
    {
    }

    /**
     * Initialize an {@link AsciiString} based upon the underlying storage from {@code value}.
     * There is a potential to share the underlying array storage if {@link ByteBuffer#hasArray()} is {@code true}.
     * if {@code copy} is {@code true} a copy will be made of the memory.
     * if {@code copy} is {@code false} the underlying storage will be shared, if possible.
     */
    public AsciiString(ArraySegment<byte> value, int start, int length, bool copy)
    {
        if (isOutOfBounds(start, length, value.capacity()))
        {
            throw new ArgumentOutOfRangeException("expected: " + "0 <= start(" + start + ") <= start + length(" + length
                                                  + ") <= " + "value.capacity(" + value.capacity() + ')');
        }

        if (value.hasArray())
        {
            if (copy)
            {
                int bufferOffset = value.arrayOffset() + start;
                _value = Arrays.copyOfRange(value.array(), bufferOffset, bufferOffset + length);
                _offset = 0;
            }
            else
            {
                _value = value.array();
                _offset = start;
            }
        }
        else
        {
            _value = PlatformDependent.allocateUninitializedArray(length);
            int oldPos = value.position();
            value.get(_value, 0, length);
            value.position(oldPos);
            _offset = 0;
        }

        _length = length;
    }

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
        if (isOutOfBounds(start, length, value.Length))
        {
            throw new ArgumentOutOfRangeException("expected: " + "0 <= start(" + start + ") <= start + length(" + length
                                                  + ") <= " + "value.length(" + value.Length + ')');
        }

        _value = PlatformDependent.allocateUninitializedArray(length);
        for (int i = 0, j = start; i < length; i++, j++)
        {
            _value[i] = c2b(value[j]);
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
    public AsciiString(char[] value, Encoding charset, int start, int length)
    {
        CharBuffer cbuf = CharBuffer.wrap(value, start, length);
        Encoder encoder = CharsetUtil.encoder(charset);
        ByteBuffer nativeBuffer = ByteBuffer.allocate((int)(encoder.maxBytesPerChar() * length));
        encoder.encode(cbuf, nativeBuffer, true);
        int bufferOffset = nativeBuffer.arrayOffset();
        _value = Arrays.copyOfRange(nativeBuffer.array(), bufferOffset, bufferOffset + nativeBuffer.position());
        _offset = 0;
        _length = _value.Length;
    }

    /**
     * Create a copy of {@code value} into this instance assuming ASCII encoding.
     */
    public AsciiString(ICharSequence value)
        : this(value, 0, value.length())
    {
    }

    /**
     * Create a copy of {@code value} into this instance assuming ASCII encoding.
     * The copy will start at index {@code start} and copy {@code length} bytes.
     */
    public AsciiString(ICharSequence value, int start, int length)
    {
        if (isOutOfBounds(start, length, value.length()))
        {
            throw new ArgumentOutOfRangeException("expected: " + "0 <= start(" + start + ") <= start + length(" + length
                                                  + ") <= " + "value.length(" + value.length() + ')');
        }

        _value = PlatformDependent.allocateUninitializedArray(length);
        for (int i = 0, j = start; i < length; i++, j++)
        {
            _value[i] = c2b(value.charAt(j));
        }

        _offset = 0;
        _length = length;
    }

    /**
     * Create a copy of {@code value} into this instance using the encoding type of {@code charset}.
     */
    public AsciiString(ICharSequence value, Encoding charset)
        : this(value, charset, 0, value.length())
    {
    }

    /**
     * Create a copy of {@code value} into this instance using the encoding type of {@code charset}.
     * The copy will start at index {@code start} and copy {@code length} bytes.
     */
    public AsciiString(ICharSequence value, Encoding charset, int start, int length)
    {
        CharBuffer cbuf = CharBuffer.wrap(value, start, start + length);
        Encoder encoder = CharsetUtil.encoder(charset);
        ByteBuffer nativeBuffer = ByteBuffer.allocate((int)(encoder.maxBytesPerChar() * length));
        encoder.encode(cbuf, nativeBuffer, true);
        int offset = nativeBuffer.arrayOffset();
        _value = Arrays.copyOfRange(nativeBuffer.array(), offset, offset + nativeBuffer.position());
        _offset = 0;
        _length = _value.Length;
    }


    /**
     * Iterates over the readable bytes of this buffer with the specified {@code processor} in ascending order.
     *
     * @return {@code -1} if the processor iterated to or beyond the end of the readable bytes.
     *         The last-visited index If the {@link IByteProcessor#process(byte)} returned {@code false}.
     */
    public int forEachByte(IByteProcessor visitor)
    {
        return forEachByte0(0, length(), visitor);
    }

    /**
     * Iterates over the specified area of this buffer with the specified {@code processor} in ascending order.
     * (i.e. {@code index}, {@code (index + 1)},  .. {@code (index + length - 1)}).
     *
     * @return {@code -1} if the processor iterated to or beyond the end of the specified area.
     *         The last-visited index If the {@link IByteProcessor#process(byte)} returned {@code false}.
     */
    public int forEachByte(int index, int length, IByteProcessor visitor)
    {
        if (isOutOfBounds(index, length, this.length()))
        {
            throw new ArgumentOutOfRangeException("expected: " + "0 <= index(" + index + ") <= start + length(" + length
                                                  + ") <= " + "length(" + this.length() + ')');
        }

        return forEachByte0(index, length, visitor);
    }

    private int forEachByte0(int index, int length, IByteProcessor visitor)
    {
        int len = _offset + index + length;
        for (int i = _offset + index; i < len; ++i)
        {
            if (!visitor.process(_value[i]))
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
     *         The last-visited index If the {@link IByteProcessor#process(byte)} returned {@code false}.
     */
    public int forEachByteDesc(IByteProcessor visitor)
    {
        return forEachByteDesc0(0, length(), visitor);
    }

    /**
     * Iterates over the specified area of this buffer with the specified {@code processor} in descending order.
     * (i.e. {@code (index + length - 1)}, {@code (index + length - 2)}, ... {@code index}).
     *
     * @return {@code -1} if the processor iterated to or beyond the beginning of the specified area.
     *         The last-visited index If the {@link IByteProcessor#process(byte)} returned {@code false}.
     */
    public int forEachByteDesc(int index, int length, IByteProcessor visitor)
    {
        if (isOutOfBounds(index, length, this.length()))
        {
            throw new ArgumentOutOfRangeException("expected: " + "0 <= index(" + index + ") <= start + length(" + length
                                                  + ") <= " + "length(" + this.length() + ')');
        }

        return forEachByteDesc0(index, length, visitor);
    }

    private int forEachByteDesc0(int index, int length, IByteProcessor visitor)
    {
        int end = _offset + index;
        for (int i = _offset + index + length - 1; i >= end; --i)
        {
            if (!visitor.process(_value[i]))
            {
                return i - _offset;
            }
        }

        return -1;
    }

    public byte byteAt(int index)
    {
        // We must do a range check here to enforce the access does not go outside our sub region of the array.
        // We rely on the array access itself to pick up the array out of bounds conditions
        if (index < 0 || index >= _length)
        {
            throw new ArgumentOutOfRangeException("index: " + index + " must be in the range [0," + _length + ")");
        }

        // Try to use unsafe to avoid double checking the index bounds
        if (PlatformDependent.hasUnsafe())
        {
            return PlatformDependent.getByte(_value, index + _offset);
        }

        return _value[index + _offset];
    }

    /**
     * Determine if this instance has 0 length.
     */
    public bool isEmpty()
    {
        return _length == 0;
    }

    /**
     * The length in bytes of this instance.
     */
    public int length()
    {
        return _length;
    }

    /**
     * During normal use cases the {@link AsciiString} should be immutable, but if the underlying array is shared,
     * and changes then this needs to be called.
     */
    public void arrayChanged()
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
    public byte[] array()
    {
        return _value;
    }

    /**
     * The offset into {@link #array()} for which data for this ByteString begins.
     * @see #array()
     * @see #isEntireArrayUsed()
     */
    public int arrayOffset()
    {
        return _offset;
    }

    /**
     * Determine if the storage represented by {@link #array()} is entirely used.
     * @see #array()
     */
    public bool isEntireArrayUsed()
    {
        return _offset == 0 && _length == _value.Length;
    }

    /**
     * Converts this string to a byte array.
     */
    public byte[] toByteArray()
    {
        return toByteArray(0, length());
    }

    /**
     * Converts a subset of this string to a byte array.
     * The subset is defined by the range [{@code start}, {@code end}).
     */
    public byte[] toByteArray(int start, int end)
    {
        return Arrays.copyOfRange(_value, start + _offset, end + _offset);
    }

    /**
     * Copies the content of this string to a byte array.
     *
     * @param srcIdx the starting offset of characters to copy.
     * @param dst the destination byte array.
     * @param dstIdx the starting offset in the destination byte array.
     * @param length the number of characters to copy.
     */
    public void copy(int srcIdx, byte[] dst, int dstIdx, int length)
    {
        if (isOutOfBounds(srcIdx, length, this.length()))
        {
            throw new ArgumentOutOfRangeException("expected: " + "0 <= srcIdx(" + srcIdx + ") <= srcIdx + length("
                                                  + length + ") <= srcLen(" + this.length() + ')');
        }

        Arrays.arraycopy(_value, srcIdx + _offset, checkNotNull(dst, "dst"), dstIdx, length);
    }

    public char charAt(int index)
    {
        return b2c(byteAt(index));
    }

    /**
     * Determines if this {@code string} contains the sequence of characters in the {@code ICharSequence} passed.
     *
     * @param cs the character sequence to search for.
     * @return {@code true} if the sequence of characters are contained in this string, otherwise {@code false}.
     */
    public bool contains(ICharSequence cs)
    {
        return indexOf(cs) >= 0;
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
    public int CompareTo(ICharSequence str)
    {
        if (this == str)
        {
            return 0;
        }

        int result;
        int length1 = length();
        int length2 = str.length();
        int minLength = Math.Min(length1, length2);
        for (int i = 0, j = arrayOffset(); i < minLength; i++, j++)
        {
            result = b2c(_value[j]) - str.charAt(i);
            if (result != 0)
            {
                return result;
            }
        }

        return length1 - length2;
    }

    /**
     * Concatenates this string and the specified string.
     *
     * @param string the string to concatenate
     * @return a new string which is the concatenation of this string and the specified string.
     */
    public AsciiString concat(ICharSequence str)
    {
        int thisLen = length();
        int thatLen = str.length();
        if (thatLen == 0)
        {
            return this;
        }

        if (str is AsciiString)
        {
            AsciiString that = (AsciiString)str;
            if (isEmpty())
            {
                return that;
            }

            byte[] newValue = PlatformDependent.allocateUninitializedArray(thisLen + thatLen);
            Arrays.arraycopy(_value, arrayOffset(), newValue, 0, thisLen);
            Arrays.arraycopy(that._value, that.arrayOffset(), newValue, thisLen, thatLen);
            return new AsciiString(newValue, false);
        }

        if (isEmpty())
        {
            return new AsciiString(str);
        }

        {
            byte[] newValue = PlatformDependent.allocateUninitializedArray(thisLen + thatLen);
            Arrays.arraycopy(_value, arrayOffset(), newValue, 0, thisLen);
            for (int i = thisLen, j = 0; i < newValue.Length; i++, j++)
            {
                newValue[i] = c2b(str.charAt(j));
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
    public bool endsWith(ICharSequence suffix)
    {
        int suffixLen = suffix.length();
        return regionMatches(length() - suffixLen, suffix, 0, suffixLen);
    }

    /**
     * Compares the specified string to this string ignoring the case of the characters and returns true if they are
     * equal.
     *
     * @param string the string to compare.
     * @return {@code true} if the specified string is equal to this string, {@code false} otherwise.
     */
    public bool contentEqualsIgnoreCase(ICharSequence str)
    {
        if (this == str)
        {
            return true;
        }

        if (str == null || str.length() != length())
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
                    if (!equalsIgnoreCase(value[i], otherValue[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            return misalignedEqualsIgnoreCase(other);
        }

        {
            byte[] value = _value;
            for (int i = _offset, j = 0; j < str.length(); ++i, ++j)
            {
                if (!equalsIgnoreCase(b2c(value[i]), str.charAt(j)))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private bool misalignedEqualsIgnoreCase(AsciiString other)
    {
        byte[] value = _value;
        byte[] otherValue = other._value;
        for (int i = _offset, j = other._offset, end = _offset + _length; i < end; ++i, ++j)
        {
            if (!equalsIgnoreCase(value[i], otherValue[j]))
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
    public char[] toCharArray()
    {
        return toCharArray(0, length());
    }

    /**
     * Copies the characters in this string to a character array.
     *
     * @return a character array containing the characters of this string.
     */
    public char[] toCharArray(int start, int end)
    {
        int length = end - start;
        if (length == 0)
        {
            return EmptyArrays.EMPTY_CHARS;
        }

        if (isOutOfBounds(start, length, this.length()))
        {
            throw new ArgumentOutOfRangeException("expected: " + "0 <= start(" + start + ") <= srcIdx + length("
                                                  + length + ") <= srcLen(" + this.length() + ')');
        }

        char[] buffer = new char[length];
        for (int i = 0, j = start + arrayOffset(); i < length; i++, j++)
        {
            buffer[i] = b2c(_value[j]);
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
    public void copy(int srcIdx, char[] dst, int dstIdx, int length)
    {
        ObjectUtil.checkNotNull(dst, "dst");

        if (isOutOfBounds(srcIdx, length, this.length()))
        {
            throw new ArgumentOutOfRangeException("expected: " + "0 <= srcIdx(" + srcIdx + ") <= srcIdx + length("
                                                  + length + ") <= srcLen(" + this.length() + ')');
        }

        int dstEnd = dstIdx + length;
        for (int i = dstIdx, j = srcIdx + arrayOffset(); i < dstEnd; i++, j++)
        {
            dst[i] = b2c(_value[j]);
        }
    }

    /**
     * Copies a range of characters into a new string.
     * @param start the offset of the first character (inclusive).
     * @return a new string containing the characters from start to the end of the string.
     * @throws ArgumentOutOfRangeException if {@code start < 0} or {@code start > length()}.
     */
    public ICharSequence subSequence(int start)
    {
        return subSequence(start, length());
    }

    /**
     * Copies a range of characters into a new string.
     * @param start the offset of the first character (inclusive).
     * @param end The index to stop at (exclusive).
     * @return a new string containing the characters from start to the end of the string.
     * @throws ArgumentOutOfRangeException if {@code start < 0} or {@code start > length()}.
     */
    public ICharSequence subSequence(int start, int end)
    {
        return subSequence(start, end, true);
    }

    /**
     * Either copy or share a subset of underlying sub-sequence of bytes.
     * @param start the offset of the first character (inclusive).
     * @param end The index to stop at (exclusive).
     * @param copy If {@code true} then a copy of the underlying storage will be made.
     * If {@code false} then the underlying storage will be shared.
     * @return a new string containing the characters from start to the end of the string.
     * @throws ArgumentOutOfRangeException if {@code start < 0} or {@code start > length()}.
     */
    public AsciiString subSequence(int start, int end, bool copy)
    {
        if (isOutOfBounds(start, end - start, length()))
        {
            throw new ArgumentOutOfRangeException("expected: 0 <= start(" + start + ") <= end (" + end + ") <= length("
                                                  + length() + ')');
        }

        if (start == 0 && end == length())
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
     * Searches in this string for the first index of the specified string. The search for the string starts at the
     * beginning and moves towards the end of this string.
     *
     * @param string the string to find.
     * @return the index of the first character of the specified string in this string, -1 if the specified string is
     *         not a substring.
     * @throws NullPointerException if {@code string} is {@code null}.
     */
    public int indexOf(ICharSequence str)
    {
        return indexOf(str, 0);
    }

    /**
     * Searches in this string for the index of the specified string. The search for the string starts at the specified
     * offset and moves towards the end of this string.
     *
     * @param subString the string to find.
     * @param start the starting offset.
     * @return the index of the first character of the specified string in this string, -1 if the specified string is
     *         not a substring.
     * @throws NullPointerException if {@code subString} is {@code null}.
     */
    public int indexOf(ICharSequence subString, int start)
    {
        int subCount = subString.length();
        if (start < 0)
        {
            start = 0;
        }

        if (subCount <= 0)
        {
            return start < _length ? start : _length;
        }

        if (subCount > _length - start)
        {
            return INDEX_NOT_FOUND;
        }

        char firstChar = subString.charAt(0);
        if (firstChar > MAX_CHAR_VALUE)
        {
            return INDEX_NOT_FOUND;
        }

        byte firstCharAsByte = c2b0(firstChar);
        int len = _offset + _length - subCount;
        for (int i = start + _offset; i <= len; ++i)
        {
            if (_value[i] == firstCharAsByte)
            {
                int o1 = i, o2 = 0;
                while (++o2 < subCount && b2c(_value[++o1]) == subString.charAt(o2))
                {
                    // Intentionally empty
                }

                if (o2 == subCount)
                {
                    return i - _offset;
                }
            }
        }

        return INDEX_NOT_FOUND;
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
    public int indexOf(char ch, int start)
    {
        if (ch > MAX_CHAR_VALUE)
        {
            return INDEX_NOT_FOUND;
        }

        if (start < 0)
        {
            start = 0;
        }

        byte chAsByte = c2b0(ch);
        int len = _offset + _length;
        for (int i = start + _offset; i < len; ++i)
        {
            if (_value[i] == chAsByte)
            {
                return i - _offset;
            }
        }

        return INDEX_NOT_FOUND;
    }

    /**
     * Searches in this string for the last index of the specified string. The search for the string starts at the end
     * and moves towards the beginning of this string.
     *
     * @param string the string to find.
     * @return the index of the first character of the specified string in this string, -1 if the specified string is
     *         not a substring.
     * @throws NullPointerException if {@code string} is {@code null}.
     */
    public int lastIndexOf(ICharSequence str)
    {
        // Use count instead of count - 1 so lastIndexOf("") answers count
        return lastIndexOf(str, _length);
    }

    /**
     * Searches in this string for the index of the specified string. The search for the string starts at the specified
     * offset and moves towards the beginning of this string.
     *
     * @param subString the string to find.
     * @param start the starting offset.
     * @return the index of the first character of the specified string in this string , -1 if the specified string is
     *         not a substring.
     * @throws NullPointerException if {@code subString} is {@code null}.
     */
    public int lastIndexOf(ICharSequence subString, int start)
    {
        int subCount = subString.length();
        start = Math.Min(start, _length - subCount);
        if (start < 0)
        {
            return INDEX_NOT_FOUND;
        }

        if (subCount == 0)
        {
            return start;
        }

        char firstChar = subString.charAt(0);
        if (firstChar > MAX_CHAR_VALUE)
        {
            return INDEX_NOT_FOUND;
        }

        byte firstCharAsByte = c2b0(firstChar);
        for (int i = _offset + start; i >= _offset; --i)
        {
            if (_value[i] == firstCharAsByte)
            {
                int o1 = i, o2 = 0;
                while (++o2 < subCount && b2c(_value[++o1]) == subString.charAt(o2))
                {
                    // Intentionally empty
                }

                if (o2 == subCount)
                {
                    return i - _offset;
                }
            }
        }

        return INDEX_NOT_FOUND;
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
    public bool regionMatches(int thisStart, ICharSequence str, int start, int length)
    {
        ObjectUtil.checkNotNull(str, "string");

        if (start < 0 || str.length() - start < length)
        {
            return false;
        }

        int thisLen = this.length();
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
            return PlatformDependent.equals(_value, thisStart + _offset, asciiString._value,
                start + asciiString._offset, length);
        }

        int thatEnd = start + length;
        for (int i = start, j = thisStart + arrayOffset(); i < thatEnd; i++, j++)
        {
            if (b2c(_value[j]) != str.charAt(i))
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
    public bool regionMatches(bool ignoreCase, int thisStart, ICharSequence str, int start, int length)
    {
        if (!ignoreCase)
        {
            return regionMatches(thisStart, str, start, length);
        }

        ObjectUtil.checkNotNull(str, "string");

        int thisLen = this.length();
        if (thisStart < 0 || length > thisLen - thisStart)
        {
            return false;
        }

        if (start < 0 || length > str.length() - start)
        {
            return false;
        }

        thisStart += arrayOffset();
        int thisEnd = thisStart + length;
        if (str is AsciiString asciiString)
        {
            byte[] value = _value;
            byte[] otherValue = asciiString._value;
            start += asciiString._offset;
            while (thisStart < thisEnd)
            {
                if (!equalsIgnoreCase(value[thisStart++], otherValue[start++]))
                {
                    return false;
                }
            }

            return true;
        }

        while (thisStart < thisEnd)
        {
            if (!equalsIgnoreCase(b2c(_value[thisStart++]), str.charAt(start++)))
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
    public AsciiString replace(char oldChar, char newChar)
    {
        if (oldChar > MAX_CHAR_VALUE)
        {
            return this;
        }

        byte oldCharAsByte = c2b0(oldChar);
        byte newCharAsByte = c2b(newChar);
        int len = _offset + _length;
        for (int i = _offset; i < len; ++i)
        {
            if (_value[i] == oldCharAsByte)
            {
                byte[] buffer = PlatformDependent.allocateUninitializedArray(length());
                Arrays.arraycopy(_value, _offset, buffer, 0, i - _offset);
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
        return regionMatches(start, prefix, 0, prefix.length());
    }

    /**
     * Converts the characters in this string to lowercase, using the default Locale.
     *
     * @return a new string containing the lowercase characters equivalent to the characters in this string.
     */
    public AsciiString toLowerCase()
    {
        return AsciiStringUtil.toLowerCase(this);
    }

    /**
     * Converts the characters in this string to uppercase, using the default Locale.
     *
     * @return a new string containing the uppercase characters equivalent to the characters in this string.
     */
    public AsciiString toUpperCase()
    {
        return AsciiStringUtil.toUpperCase(this);
    }

    /**
     * Copies this string removing white space characters from the beginning and end of the string, and tries not to
     * copy if possible.
     *
     * @param c The {@link ICharSequence} to trim.
     * @return a new string with characters {@code <= \\u0020} removed from the beginning and the end.
     */
    public static ICharSequence trim(ICharSequence c)
    {
        if (c is AsciiString asciiString)
        {
            return asciiString.trim();
        }

        // ..
        // if (c is string) {
        //     return ((string) c).trim();
        // }

        int start = 0, last = c.length() - 1;
        int end = last;
        while (start <= end && c.charAt(start) <= ' ')
        {
            start++;
        }

        while (end >= start && c.charAt(end) <= ' ')
        {
            end--;
        }

        if (start == 0 && end == last)
        {
            return c;
        }

        return c.subSequence(start, end);
    }

    /**
     * Duplicates this string removing white space characters from the beginning and end of the
     * string, without copying.
     *
     * @return a new string with characters {@code <= \\u0020} removed from the beginning and the end.
     */
    public AsciiString trim()
    {
        int start = arrayOffset(), last = arrayOffset() + length() - 1;
        int end = last;
        while (start <= end && _value[start] <= ' ')
        {
            start++;
        }

        while (end >= start && _value[end] <= ' ')
        {
            end--;
        }

        if (start == 0 && end == last)
        {
            return this;
        }

        return new AsciiString(_value, start, end - start + 1, false);
    }

    /**
     * Compares a {@code ICharSequence} to this {@code string} to determine if their contents are equal.
     *
     * @param a the character sequence to compare to.
     * @return {@code true} if equal, otherwise {@code false}
     */
    public bool contentEquals(ICharSequence a)
    {
        if (this == a)
        {
            return true;
        }

        if (a == null || a.length() != length())
        {
            return false;
        }

        if (a is AsciiString)
        {
            return Equals(a);
        }

        for (int i = arrayOffset(), j = 0; j < a.length(); ++i, ++j)
        {
            if (b2c(_value[i]) != a.charAt(j))
            {
                return false;
            }
        }

        return true;
    }

    /**
     * Determines whether this string matches a given regular expression.
     *
     * @param expr the regular expression to be matched.
     * @return {@code true} if the expression matches, otherwise {@code false}.
     * @throws PatternSyntaxException if the syntax of the supplied regular expression is not valid.
     * @throws NullPointerException if {@code expr} is {@code null}.
     */
    public bool matches(string expr)
    {
        return 0 < Regex.Matches(ToString(), expr).Count;
    }

    /**
     * Splits this string using the supplied regular expression {@code expr}. The parameter {@code max} controls the
     * behavior how many times the pattern is applied to the string.
     *
     * @param expr the regular expression used to divide the string.
     * @param max the number of entries in the resulting array.
     * @return an array of Strings created by separating the string along matches of the regular expression.
     * @throws NullPointerException if {@code expr} is {@code null}.
     * @throws PatternSyntaxException if the syntax of the supplied regular expression is not valid.
     * @see Pattern#split(ICharSequence, int)
     */
    public AsciiString[] split(string expr)
    {
        var splits = Regex.Split(ToString(), expr);
        return splits
            .Select(x => new StringCharSequence(x))
            .Select(x => new AsciiString(x))
            .ToArray();
    }

    /**
     * Splits the specified {@link string} with the specified delimiter..
     */
    public AsciiString[] split(char delim)
    {
        List<AsciiString> res = InternalThreadLocalMap.get().arrayList<AsciiString>();

        int start = 0;
        int length = this.length();
        for (int i = start; i < length; i++)
        {
            if (charAt(i) == delim)
            {
                if (start == i)
                {
                    res.Add(EMPTY_STRING);
                }
                else
                {
                    res.Add(new AsciiString(_value, start + arrayOffset(), i - start, false));
                }

                start = i + 1;
            }
        }

        if (start == 0)
        {
            // If no delimiter was found in the value
            res.Add(this);
        }
        else
        {
            if (start != length)
            {
                // Add the last element if it's not empty.
                res.Add(new AsciiString(_value, start + arrayOffset(), length - start, false));
            }
            else
            {
                // Truncate trailing empty elements.
                for (int i = res.Count - 1; i >= 0; i--)
                {
                    if (res[i].isEmpty())
                    {
                        res.RemoveAt(i);
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        return res.ToArray();
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
            h = PlatformDependent.hashCodeAscii(_value, _offset, _length);
            _hash = h;
        }

        return h;
    }

    public override bool Equals(object obj)
    {
        if (obj == null || obj.GetType() != typeof(AsciiString))
        {
            return false;
        }

        if (this == obj)
        {
            return true;
        }

        AsciiString other = (AsciiString)obj;
        return length() == other.length() &&
               GetHashCode() == other.GetHashCode() &&
               PlatformDependent.equals(array(), arrayOffset(), other.array(), other.arrayOffset(), length());
    }

    /**
     * Translates the entire byte string to a {@link string}.
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

    /**
     * Translates the entire byte string to a {@link string} using the {@code charset} encoding.
     * @see #toString(int, int)
     */
    public string ToString(int start)
    {
        return ToString(start, length());
    }

    /**
     * Translates the [{@code start}, {@code end}) range of this byte string to a {@link string}.
     */
    public string ToString(int start, int end)
    {
        int length = end - start;
        if (length == 0)
        {
            return "";
        }

        if (isOutOfBounds(start, length, this.length()))
        {
            throw new ArgumentOutOfRangeException("expected: " + "0 <= start(" + start + ") <= srcIdx + length("
                                                  + length + ") <= srcLen(" + this.length() + ')');
        }

        //@SuppressWarnings("deprecation")
        return Encoding.ASCII.GetString(_value, _offset + start, length);
    }

    public bool parseBoolean()
    {
        return _length >= 1 && _value[_offset] != 0;
    }

    public char parseChar()
    {
        return parseChar(0);
    }

    public char parseChar(int start)
    {
        if (start + 1 >= length())
        {
            throw new ArgumentOutOfRangeException("2 bytes required to convert to character. index " +
                                                  start + " would go out of bounds.");
        }

        int startWithOffset = start + _offset;
        return (char)((b2c(_value[startWithOffset]) << 8) | b2c(_value[startWithOffset + 1]));
    }

    public short parseShort()
    {
        return parseShort(0, length(), 10);
    }

    public short parseShort(int radix)
    {
        return parseShort(0, length(), radix);
    }

    public short parseShort(int start, int end)
    {
        return parseShort(start, end, 10);
    }

    public short parseShort(int start, int end, int radix)
    {
        int intValue = parseInt(start, end, radix);
        short result = (short)intValue;
        if (result != intValue)
        {
            throw new FormatException(subSequence(start, end, false).ToString());
        }

        return result;
    }

    public int parseInt()
    {
        return parseInt(0, length(), 10);
    }

    public int parseInt(int radix)
    {
        return parseInt(0, length(), radix);
    }

    public int parseInt(int start, int end)
    {
        return parseInt(start, end, 10);
    }

    public int parseInt(int start, int end, int radix)
    {
        if (radix < CHARACTER_MIN_RADIX || radix > CHARACTER_MAX_RADIX)
        {
            throw new FormatException();
        }

        if (start == end)
        {
            throw new FormatException();
        }

        int i = start;
        bool negative = byteAt(i) == '-';
        if (negative && ++i == end)
        {
            throw new FormatException(subSequence(start, end, false).ToString());
        }

        return parseInt(i, end, radix, negative);
    }

    private int parseInt(int start, int end, int radix, bool negative)
    {
        int max = int.MinValue / radix;
        int result = 0;
        int currOffset = start;
        while (currOffset < end)
        {
            int digit = CharUtil.Digit((char)(_value[currOffset++ + _offset] & 0xFF), radix);
            if (digit == -1)
            {
                throw new FormatException(subSequence(start, end, false).ToString());
            }

            if (max > result)
            {
                throw new FormatException(subSequence(start, end, false).ToString());
            }

            int next = result * radix - digit;
            if (next > result)
            {
                throw new FormatException(subSequence(start, end, false).ToString());
            }

            result = next;
        }

        if (!negative)
        {
            result = -result;
            if (result < 0)
            {
                throw new FormatException(subSequence(start, end, false).ToString());
            }
        }

        return result;
    }

    public long parseLong()
    {
        return parseLong(0, length(), 10);
    }

    public long parseLong(int radix)
    {
        return parseLong(0, length(), radix);
    }

    public long parseLong(int start, int end)
    {
        return parseLong(start, end, 10);
    }

    public long parseLong(int start, int end, int radix)
    {
        if (radix < CharUtil.MIN_RADIX || radix > CharUtil.MAX_RADIX)
        {
            throw new FormatException();
        }

        if (start == end)
        {
            throw new FormatException();
        }

        int i = start;
        bool negative = byteAt(i) == '-';
        if (negative && ++i == end)
        {
            throw new FormatException(subSequence(start, end, false).ToString());
        }

        return parseLong(i, end, radix, negative);
    }

    private long parseLong(int start, int end, int radix, bool negative)
    {
        long max = long.MinValue / radix;
        long result = 0;
        int currOffset = start;
        while (currOffset < end)
        {
            int digit = CharUtil.Digit((char)(_value[currOffset++ + _offset] & 0xFF), radix);
            if (digit == -1)
            {
                throw new FormatException(subSequence(start, end, false).ToString());
            }

            if (max > result)
            {
                throw new FormatException(subSequence(start, end, false).ToString());
            }

            long next = result * radix - digit;
            if (next > result)
            {
                throw new FormatException(subSequence(start, end, false).ToString());
            }

            result = next;
        }

        if (!negative)
        {
            result = -result;
            if (result < 0)
            {
                throw new FormatException(subSequence(start, end, false).ToString());
            }
        }

        return result;
    }

    public float parseFloat()
    {
        return parseFloat(0, length());
    }

    public float parseFloat(int start, int end)
    {
        return float.Parse(ToString(start, end));
    }

    public double parseDouble()
    {
        return parseDouble(0, length());
    }

    public double parseDouble(int start, int end)
    {
        return double.Parse(ToString(start, end));
    }


    /**
     * Returns an {@link AsciiString} containing the given character sequence. If the given string is already a
     * {@link AsciiString}, just returns the same instance.
     */
    public static AsciiString of(ICharSequence str)
    {
        return str is AsciiString ? (AsciiString)str : new AsciiString(str);
    }

    /**
     * Returns an {@link AsciiString} containing the given string and retains/caches the input
     * string for later use in {@link #ToString()}.
     * Used for the constants (which already stored in the JVM's string table) and in cases
     * where the guaranteed use of the {@link #ToString()} method.
     */
    public static AsciiString cached(StringCharSequence str)
    {
        AsciiString asciiString = new AsciiString(str);
        asciiString._string = str.ToString();
        return asciiString;
    }

    /**
     * Returns the case-insensitive hash code of the specified string. Note that this method uses the same hashing
     * algorithm with {@link #hashCode()} so that you can put both {@link AsciiString}s and arbitrary
     * {@link ICharSequence}s into the same headers.
     */
    public static int hashCode(ICharSequence value)
    {
        if (value == null)
        {
            return 0;
        }

        if (value is AsciiString)
        {
            return value.GetHashCode();
        }

        return PlatformDependent.hashCodeAscii(value);
    }

    /**
     * Determine if {@code a} contains {@code b} in a case sensitive manner.
     */
    public static bool contains(ICharSequence a, ICharSequence b)
    {
        return contains(a, b, DefaultCharEqualityComparator.INSTANCE);
    }

    /**
     * Determine if {@code a} contains {@code b} in a case insensitive manner.
     */
    public static bool containsIgnoreCase(ICharSequence a, ICharSequence b)
    {
        return contains(a, b, AsciiCaseInsensitiveCharEqualityComparator.INSTANCE);
    }

    /**
     * Returns {@code true} if both {@link ICharSequence}'s are equals when ignore the case. This only supports 8-bit
     * ASCII.
     */
    public static bool contentEqualsIgnoreCase(ICharSequence a, ICharSequence b)
    {
        if (a == null || b == null)
        {
            return a == b;
        }

        if (a is AsciiString)
        {
            return ((AsciiString)a).contentEqualsIgnoreCase(b);
        }

        if (b is AsciiString)
        {
            return ((AsciiString)b).contentEqualsIgnoreCase(a);
        }

        if (a.length() != b.length())
        {
            return false;
        }

        for (int i = 0; i < a.length(); ++i)
        {
            if (!equalsIgnoreCase(a.charAt(i), b.charAt(i)))
            {
                return false;
            }
        }

        return true;
    }

    /**
     * Determine if {@code collection} contains {@code value} and using
     * {@link #contentEqualsIgnoreCase(ICharSequence, ICharSequence)} to compare values.
     * @param collection The collection to look for and equivalent element as {@code value}.
     * @param value The value to look for in {@code collection}.
     * @return {@code true} if {@code collection} contains {@code value} according to
     * {@link #contentEqualsIgnoreCase(ICharSequence, ICharSequence)}. {@code false} otherwise.
     * @see #contentEqualsIgnoreCase(ICharSequence, ICharSequence)
     */
    public static bool containsContentEqualsIgnoreCase(ICollection<ICharSequence> collection, ICharSequence value)
    {
        foreach (ICharSequence v in collection)
        {
            if (contentEqualsIgnoreCase(value, v))
            {
                return true;
            }
        }

        return false;
    }

    /**
     * Determine if {@code a} contains all of the values in {@code b} using
     * {@link #contentEqualsIgnoreCase(ICharSequence, ICharSequence)} to compare values.
     * @param a The collection under test.
     * @param b The values to test for.
     * @return {@code true} if {@code a} contains all of the values in {@code b} using
     * {@link #contentEqualsIgnoreCase(ICharSequence, ICharSequence)} to compare values. {@code false} otherwise.
     * @see #contentEqualsIgnoreCase(ICharSequence, ICharSequence)
     */
    public static bool containsAllContentEqualsIgnoreCase(ICollection<ICharSequence> a, ICollection<ICharSequence> b)
    {
        foreach (ICharSequence v in b)
        {
            if (!containsContentEqualsIgnoreCase(a, v))
            {
                return false;
            }
        }

        return true;
    }

    /**
     * Returns {@code true} if the content of both {@link ICharSequence}'s are equals. This only supports 8-bit ASCII.
     */
    public static bool contentEquals(ICharSequence a, ICharSequence b)
    {
        if (a == null || b == null)
        {
            return a == b;
        }

        if (a is AsciiString)
        {
            return ((AsciiString)a).contentEquals(b);
        }

        if (b is AsciiString)
        {
            return ((AsciiString)b).contentEquals(a);
        }

        if (a.length() != b.length())
        {
            return false;
        }

        for (int i = 0; i < a.length(); ++i)
        {
            if (a.charAt(i) != b.charAt(i))
            {
                return false;
            }
        }

        return true;
    }

    private static AsciiString[] toAsciiStringArray(string[] jdkResult)
    {
        AsciiString[] res = new AsciiString[jdkResult.Length];
        for (int i = 0; i < jdkResult.Length; i++)
        {
            res[i] = new AsciiString(new StringCharSequence(jdkResult[i]));
        }

        return res;
    }


    private static bool contains(ICharSequence a, ICharSequence b, ICharEqualityComparator cmp)
    {
        if (a == null || b == null || a.length() < b.length())
        {
            return false;
        }

        if (b.length() == 0)
        {
            return true;
        }

        int bStart = 0;
        for (int i = 0; i < a.length(); ++i)
        {
            if (cmp.equals(b.charAt(bStart), a.charAt(i)))
            {
                // If b is consumed then true.
                if (++bStart == b.length())
                {
                    return true;
                }
            }
            else if (a.length() - i < b.length())
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

    private static bool regionMatchesCharSequences(ICharSequence cs, int csStart,
        ICharSequence str, int start, int length,
        ICharEqualityComparator charEqualityComparator)
    {
        //general purpose implementation for CharSequences
        if (csStart < 0 || length > cs.length() - csStart)
        {
            return false;
        }

        if (start < 0 || length > str.length() - start)
        {
            return false;
        }

        int csIndex = csStart;
        int csEnd = csIndex + length;
        int stringIndex = start;

        while (csIndex < csEnd)
        {
            char c1 = cs.charAt(csIndex++);
            char c2 = str.charAt(stringIndex++);

            if (!charEqualityComparator.equals(c1, c2))
            {
                return false;
            }
        }

        return true;
    }

    /**
     * This methods make regionMatches operation correctly for any chars in strings
     * @param cs the {@code ICharSequence} to be processed
     * @param ignoreCase specifies if case should be ignored.
     * @param csStart the starting offset in the {@code cs} ICharSequence
     * @param string the {@code ICharSequence} to compare.
     * @param start the starting offset in the specified {@code string}.
     * @param length the number of characters to compare.
     * @return {@code true} if the ranges of characters are equal, {@code false} otherwise.
     */
    public static bool regionMatches(ICharSequence cs, bool ignoreCase, int csStart,
        ICharSequence str, int start, int length)
    {
        if (cs == null || str == null)
        {
            return false;
        }

        if (cs is StringCharSequence && str is StringCharSequence)
        {
            return ((StringCharSequence)cs).regionMatches(ignoreCase, csStart, str, start, length);
        }

        if (cs is AsciiString)
        {
            return ((AsciiString)cs).regionMatches(ignoreCase, csStart, str, start, length);
        }

        return regionMatchesCharSequences(cs, csStart, str, start, length,
            ignoreCase ? GeneralCaseInsensitiveCharEqualityComparator.INSTANCE : DefaultCharEqualityComparator.INSTANCE);
    }

    /**
     * This is optimized version of regionMatches for string with ASCII chars only
     * @param cs the {@code ICharSequence} to be processed
     * @param ignoreCase specifies if case should be ignored.
     * @param csStart the starting offset in the {@code cs} ICharSequence
     * @param string the {@code ICharSequence} to compare.
     * @param start the starting offset in the specified {@code string}.
     * @param length the number of characters to compare.
     * @return {@code true} if the ranges of characters are equal, {@code false} otherwise.
     */
    public static bool regionMatchesAscii(ICharSequence cs, bool ignoreCase, int csStart,
        ICharSequence str, int start, int length)
    {
        if (cs == null || str == null)
        {
            return false;
        }

        if (!ignoreCase && cs is StringCharSequence && str is StringCharSequence)
        {
            //we don't call regionMatches from string for ignoreCase==true. It's a general purpose method,
            //which make complex comparison in case of ignoreCase==true, which is useless for ASCII-only strings.
            //To avoid applying this complex ignore-case comparison, we will use regionMatchesCharSequences
            return ((StringCharSequence)cs).regionMatches(false, csStart, str, start, length);
        }

        if (cs is AsciiString)
        {
            return ((AsciiString)cs).regionMatches(ignoreCase, csStart, str, start, length);
        }

        return regionMatchesCharSequences(cs, csStart, str, start, length,
            ignoreCase ? AsciiCaseInsensitiveCharEqualityComparator.INSTANCE : DefaultCharEqualityComparator.INSTANCE);
    }

    /**
     * <p>Case in-sensitive find of the first index within a ICharSequence
     * from the specified position.</p>
     *
     * <p>A {@code null} ICharSequence will return {@code -1}.
     * A negative start position is treated as zero.
     * An empty ("") search ICharSequence always matches.
     * A start position greater than the string length only matches
     * an empty search ICharSequence.</p>
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
     * @param str  the ICharSequence to check, may be null
     * @param searchStr  the ICharSequence to find, may be null
     * @param startPos  the start position, negative treated as zero
     * @return the first index of the search ICharSequence (always &ge; startPos),
     *  -1 if no match or {@code null} string input
     */
    public static int indexOfIgnoreCase(ICharSequence str, ICharSequence searchStr, int startPos)
    {
        if (str == null || searchStr == null)
        {
            return INDEX_NOT_FOUND;
        }

        if (startPos < 0)
        {
            startPos = 0;
        }

        int searchStrLen = searchStr.length();
        int endLimit = str.length() - searchStrLen + 1;
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
            if (regionMatches(str, true, i, searchStr, 0, searchStrLen))
            {
                return i;
            }
        }

        return INDEX_NOT_FOUND;
    }

    /**
     * <p>Case in-sensitive find of the first index within a ICharSequence
     * from the specified position. This method optimized and works correctly for ASCII CharSequences only</p>
     *
     * <p>A {@code null} ICharSequence will return {@code -1}.
     * A negative start position is treated as zero.
     * An empty ("") search ICharSequence always matches.
     * A start position greater than the string length only matches
     * an empty search ICharSequence.</p>
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
     * @param str  the ICharSequence to check, may be null
     * @param searchStr  the ICharSequence to find, may be null
     * @param startPos  the start position, negative treated as zero
     * @return the first index of the search ICharSequence (always &ge; startPos),
     *  -1 if no match or {@code null} string input
     */
    public static int indexOfIgnoreCaseAscii(ICharSequence str, ICharSequence searchStr, int startPos)
    {
        if (str == null || searchStr == null)
        {
            return INDEX_NOT_FOUND;
        }

        if (startPos < 0)
        {
            startPos = 0;
        }

        int searchStrLen = searchStr.length();
        int endLimit = str.length() - searchStrLen + 1;
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
            if (regionMatchesAscii(str, true, i, searchStr, 0, searchStrLen))
            {
                return i;
            }
        }

        return INDEX_NOT_FOUND;
    }

    /**
     * <p>Finds the first index in the {@code ICharSequence} that matches the
     * specified character.</p>
     *
     * @param cs  the {@code ICharSequence} to be processed, not null
     * @param searchChar the char to be searched for
     * @param start  the start index, negative starts at the string start
     * @return the index where the search char was found,
     * -1 if char {@code searchChar} is not found or {@code cs == null}
     */
    //-----------------------------------------------------------------------
    public static int indexOf(string cs, char searchChar, int start)
    {
        if (cs is string)
        {
            return ((string)cs).indexOf(searchChar, start);
        }
        else if (cs is AsciiString)
        {
            return ((AsciiString)cs).indexOf(searchChar, start);
        }

        if (cs == null)
        {
            return INDEX_NOT_FOUND;
        }

        int sz = cs.length();
        for (int i = start < 0 ? 0 : start; i < sz; i++)
        {
            if (cs.charAt(i) == searchChar)
            {
                return i;
            }
        }

        return INDEX_NOT_FOUND;
    }

    private static bool equalsIgnoreCase(byte a, byte b)
    {
        return a == b || AsciiStringUtil.toLowerCase(a) == AsciiStringUtil.toLowerCase(b);
    }

    private static bool equalsIgnoreCase(char a, char b)
    {
        return a == b || toLowerCase(a) == toLowerCase(b);
    }

    /**
     * If the character is uppercase - converts the character to lowercase,
     * otherwise returns the character as it is. Only for ASCII characters.
     *
     * @return lowercase ASCII character equivalent
     */
    public static char toLowerCase(char c)
    {
        return isUpperCase(c) ? (char)(c + 32) : c;
    }

    private static byte toUpperCase(byte b)
    {
        return AsciiStringUtil.toUpperCase(b);
    }

    public static bool isUpperCase(byte value)
    {
        return AsciiStringUtil.isUpperCase(value);
    }

    public static bool isUpperCase(char value)
    {
        return value >= 'A' && value <= 'Z';
    }

    public static byte c2b(char c)
    {
        return (byte)((c > MAX_CHAR_VALUE) ? '?' : c);
    }

    private static byte c2b0(char c)
    {
        return (byte)c;
    }

    public static char b2c(byte b)
    {
        return (char)(b & 0xFF);
    }
}