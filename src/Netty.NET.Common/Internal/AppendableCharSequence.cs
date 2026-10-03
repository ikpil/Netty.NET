/*
 * Copyright 2013 The Netty Project
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
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Common.Internal;

public sealed class AppendableCharSequence : ICharSequence
{
    private char[] chars;
    private int pos;
    public int Count => pos;

    /// <summary>Returns a synchronous borrowed view of the current logical characters.</summary>
    /// <remarks>Consume before append, reset or setLength; the view does not track later length or buffer changes.</remarks>
    public ReadOnlySpan<char> AsSpan() => chars.AsSpan(0, pos);

    public AppendableCharSequence(int length)
    {
        chars = new char[CheckPositive(length, "length")];
    }

    private AppendableCharSequence(char[] chars)
    {
        this.chars = CheckNonEmpty(chars, "chars");
        pos = chars.Length;
    }

    public char this[int index]
    {
        get
        {
            if (index < 0 || index >= pos)
            {
                throw new ArgumentOutOfRangeException();
            }

            return chars[index];
        }
    }

    public void SetLength(int length)
    {
        if (length < 0 || length > pos)
        {
            throw new ArgumentException("length: " + length + " (length: >= 0, <= " + pos + ')');
        }

        this.pos = length;
    }

    ICharSequence ICharSequence.SubSequence(int start, int end)
    {
        return SubSequence(start, end);
    }

    public ICharSequence SubSequence(int start)
    {
        return SubSequence(start, pos);
    }

    public char CharAt(int index)
    {
        return this[index];
    }

    public int Length()
    {
        return pos;
    }

    public int IndexOf(char ch, int start = 0)
    {
        start = Math.Max(0, start);
        return start >= pos ? -1 : Array.IndexOf(chars, ch, start, pos - start);
    }

    public bool RegionMatches(int thisStart, ICharSequence seq, int start, int length)
    {
        return CharUtil.RegionMatches(this, thisStart, seq, start, length);
    }

    public bool RegionMatchesIgnoreCase(int thisStart, ICharSequence seq, int start, int length)
    {
        return CharUtil.RegionMatchesIgnoreCase(this, thisStart, seq, start, length);
    }

    public bool ContentEquals(ICharSequence other)
    {
        return CharUtil.ContentEquals(this, other);
    }

    public bool ContentEqualsIgnoreCase(ICharSequence other)
    {
        return CharUtil.ContentEqualsIgnoreCase(this, other);
    }

    public int HashCode(bool ignoreCase)
    {
        return string.GetHashCode(AsSpan(), ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    public string ToString(int start)
    {
        return Substring(start, pos);
    }

    /**
     * Access a value in this {@link CharSequence}.
     * This method is considered unsafe as index values are assumed to be legitimate.
     * Only underlying array bounds checking is done.
     * @param index The index to access the underlying array at.
     * @return The value at {@code index}.
     */
    public char CharAtUnsafe(int index)
    {
        return chars[index];
    }

    public AppendableCharSequence SubSequence(int start, int end)
    {
        CheckRange(start, end, pos);
        if (start == end)
        {
            // If start and end index is the same we need to return an empty sequence to conform to the interface.
            // As our expanding logic depends on the fact that we have a char[] with length > 0 we need to construct
            // an instance for which this is true.
            return new AppendableCharSequence(Math.Min(16, chars.Length));
        }

        return new AppendableCharSequence(Arrays.CopyOfRange(chars, start, end));
    }

    public AppendableCharSequence Append(char c)
    {
        if (pos == chars.Length)
        {
            char[] old = chars;
            chars = new char[old.Length << 1];
            Arrays.Arraycopy(old, 0, chars, 0, old.Length);
        }

        chars[pos++] = c;
        return this;
    }

    public AppendableCharSequence Append(ICharSequence csq)
    {
        CheckNotNull(csq, nameof(csq));
        return Append(csq, 0, csq.Count);
    }
    
    public AppendableCharSequence Append(string str)
    {
        var csq = new StringCharSequence(str);
        return Append(csq, 0, csq.Count);
    }

    public AppendableCharSequence Append(ICharSequence csq, int start, int end)
    {
        CheckNotNull(csq, nameof(csq));
        CheckRange(start, end, csq.Count);

        int length = end - start;
        if (length > chars.Length - pos)
        {
            chars = Expand(chars, pos + length, pos);
        }

        if (csq is AppendableCharSequence)
        {
            // Optimize append operations via array copy
            AppendableCharSequence seq = (AppendableCharSequence)csq;
            char[] src = seq.chars;
            Arrays.Arraycopy(src, start, chars, pos, length);
            pos += length;
            return this;
        }

        for (int i = start; i < end; i++)
        {
            chars[pos++] = csq[i];
        }

        return this;
    }

    /**
     * Reset the {@link AppendableCharSequence}. Be aware this will only reset the current internal position and not
     * shrink the internal char array.
     */
    public void Reset()
    {
        pos = 0;
    }

    public IEnumerator<char> GetEnumerator()
    {
        return new CharSequenceEnumerator(this);
    }

    public override string ToString()
    {
        return new string(chars, 0, pos);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /**
     * Create a new {@link String} from the given start to end.
     */
    public string Substring(int start, int end)
    {
        CheckRange(start, end, pos);
        return new string(chars, start, end - start);
    }

    /**
     * Create a new {@link String} from the given start to end.
     * This method is considered unsafe as index values are assumed to be legitimate.
     * Only underlying array bounds checking is done.
     */
    public string SubStringUnsafe(int start, int end)
    {
        return new string(chars, start, end - start);
    }

    private static char[] Expand(char[] array, int neededSpace, int size)
    {
        int newCapacity = array.Length;
        do
        {
            // double capacity until it is big enough
            newCapacity <<= 1;

            if (newCapacity < 0)
            {
                throw new InvalidOperationException();
            }
        } while (neededSpace > newCapacity);

        char[] newArray = new char[newCapacity];
        Arrays.Arraycopy(array, 0, newArray, 0, size);

        return newArray;
    }

    private static void CheckRange(int start, int end, int length)
    {
        if (start < 0 || end < start || end > length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }
    }
}
