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

    public AppendableCharSequence(int length)
    {
        chars = new char[checkPositive(length, "length")];
    }

    private AppendableCharSequence(char[] chars)
    {
        this.chars = checkNonEmpty(chars, "chars");
        pos = chars.Length;
    }

    public char this[int index]
    {
        get
        {
            if (index > pos)
            {
                throw new ArgumentOutOfRangeException();
            }

            return chars[index];
        }
    }

    public void setLength(int length)
    {
        if (length < 0 || length > pos)
        {
            throw new ArgumentException("length: " + length + " (length: >= 0, <= " + pos + ')');
        }

        this.pos = length;
    }

    ICharSequence ICharSequence.subSequence(int start, int end)
    {
        return subSequence(start, end);
    }

    public ICharSequence subSequence(int start)
    {
        throw new NotImplementedException();
    }

    public char charAt(int index)
    {
        throw new NotImplementedException();
    }

    public int length()
    {
        return pos;
    }

    public int indexOf(char ch, int start = 0)
    {
        throw new NotImplementedException();
    }

    public bool regionMatches(int thisStart, ICharSequence seq, int start, int length)
    {
        throw new NotImplementedException();
    }

    public bool regionMatchesIgnoreCase(int thisStart, ICharSequence seq, int start, int length)
    {
        throw new NotImplementedException();
    }

    public bool contentEquals(ICharSequence other)
    {
        throw new NotImplementedException();
    }

    public bool contentEqualsIgnoreCase(ICharSequence other)
    {
        throw new NotImplementedException();
    }

    public int hashCode(bool ignoreCase)
    {
        throw new NotImplementedException();
    }

    public string ToString(int start)
    {
        throw new NotImplementedException();
    }

    /**
     * Access a value in this {@link ICharSequence}.
     * This method is considered unsafe as index values are assumed to be legitimate.
     * Only underlying array bounds checking is done.
     * @param index The index to access the underlying array at.
     * @return The value at {@code index}.
     */
    public char charAtUnsafe(int index)
    {
        return chars[index];
    }

    public AppendableCharSequence subSequence(int start, int end)
    {
        if (start == end)
        {
            // If start and end index is the same we need to return an empty sequence to conform to the interface.
            // As our expanding logic depends on the fact that we have a char[] with length > 0 we need to construct
            // an instance for which this is true.
            return new AppendableCharSequence(Math.Min(16, chars.Length));
        }

        return new AppendableCharSequence(Arrays.copyOfRange(chars, start, end));
    }

    public AppendableCharSequence append(char c)
    {
        if (pos == chars.Length)
        {
            char[] old = chars;
            chars = new char[old.Length << 1];
            Arrays.arraycopy(old, 0, chars, 0, old.Length);
        }

        chars[pos++] = c;
        return this;
    }

    public AppendableCharSequence append(ICharSequence csq)
    {
        return append(csq, 0, csq.Count);
    }
    
    public AppendableCharSequence append(string str)
    {
        var csq = new StringCharSequence(str);
        return append(csq, 0, csq.Count);
    }

    public AppendableCharSequence append(ICharSequence csq, int start, int end)
    {
        if (csq.Count < end)
        {
            throw new ArgumentOutOfRangeException("expected: csq.Count >= ("
                                                  + end + "),but actual is (" + csq.Count + ")");
        }

        int length = end - start;
        if (length > chars.Length - pos)
        {
            chars = expand(chars, pos + length, pos);
        }

        if (csq is AppendableCharSequence)
        {
            // Optimize append operations via array copy
            AppendableCharSequence seq = (AppendableCharSequence)csq;
            char[] src = seq.chars;
            Arrays.arraycopy(src, start, chars, pos, length);
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
    public void reset()
    {
        pos = 0;
    }

    public IEnumerator<char> GetEnumerator()
    {
        throw new NotImplementedException();
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
     * Create a new {@link string} from the given start to end.
     */
    public string substring(int start, int end)
    {
        int length = end - start;
        if (start > pos || length > pos)
        {
            throw new ArgumentOutOfRangeException("expected: start and length <= ("
                                                  + pos + ")");
        }

        return new string(chars, start, length);
    }

    /**
     * Create a new {@link string} from the given start to end.
     * This method is considered unsafe as index values are assumed to be legitimate.
     * Only underlying array bounds checking is done.
     */
    public string subStringUnsafe(int start, int end)
    {
        return new string(chars, start, end - start);
    }

    private static char[] expand(char[] array, int neededSpace, int size)
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
        Arrays.arraycopy(array, 0, newArray, 0, size);

        return newArray;
    }
}