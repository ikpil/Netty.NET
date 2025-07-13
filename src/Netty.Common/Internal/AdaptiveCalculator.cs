/*
 * Copyright 2025 The Netty Project
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
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Common.Internal;

/**
 * Calculate sizes in a adaptive way.
 */
public sealed class AdaptiveCalculator
{
    private static readonly int INDEX_INCREMENT = 4;
    private static readonly int INDEX_DECREMENT = 1;

    private static readonly int[] SIZE_TABLE;

    static AdaptiveCalculator()
    {
        List<int> sizeTable = new List<int>();
        for (int i = 16; i < 512; i += 16)
        {
            sizeTable.Add(i);
        }

        // Suppress a warning since i becomes negative when an integer overflow happens
        for (int i = 512; i > 0; i <<= 1)
        {
            sizeTable.Add(i);
        }

        SIZE_TABLE = new int[sizeTable.Count];
        for (int i = 0; i < SIZE_TABLE.Length; i++)
        {
            SIZE_TABLE[i] = sizeTable[i];
        }
    }

    private static int getSizeTableIndex(int size)
    {
        for (int low = 0, high = SIZE_TABLE.Length - 1;;)
        {
            if (high < low)
            {
                return low;
            }

            if (high == low)
            {
                return high;
            }

            int mid = low + high >>> 1;
            int a = SIZE_TABLE[mid];
            int b = SIZE_TABLE[mid + 1];
            if (size > b)
            {
                low = mid + 1;
            }
            else if (size < a)
            {
                high = mid - 1;
            }
            else if (size == a)
            {
                return mid;
            }
            else
            {
                return mid + 1;
            }
        }
    }

    private readonly int minIndex;
    private readonly int maxIndex;
    private readonly int minCapacity;
    private readonly int maxCapacity;
    private int index;
    private int _nextSize;
    private bool decreaseNow;

    public AdaptiveCalculator(int minimum, int initial, int maximum)
    {
        checkPositive(minimum, "minimum");
        if (initial < minimum)
        {
            throw new ArgumentException("initial: " + initial);
        }

        if (maximum < initial)
        {
            throw new ArgumentException("maximum: " + maximum);
        }

        int minIndex = getSizeTableIndex(minimum);
        if (SIZE_TABLE[minIndex] < minimum)
        {
            this.minIndex = minIndex + 1;
        }
        else
        {
            this.minIndex = minIndex;
        }

        int maxIndex = getSizeTableIndex(maximum);
        if (SIZE_TABLE[maxIndex] > maximum)
        {
            this.maxIndex = maxIndex - 1;
        }
        else
        {
            this.maxIndex = maxIndex;
        }

        int initialIndex = getSizeTableIndex(initial);
        if (SIZE_TABLE[initialIndex] > initial)
        {
            this.index = initialIndex - 1;
        }
        else
        {
            this.index = initialIndex;
        }

        this.minCapacity = minimum;
        this.maxCapacity = maximum;
        _nextSize = Math.Max(SIZE_TABLE[index], minCapacity);
    }

    public void record(int size)
    {
        if (size <= SIZE_TABLE[Math.Max(0, index - INDEX_DECREMENT)])
        {
            if (decreaseNow)
            {
                index = Math.Max(index - INDEX_DECREMENT, minIndex);
                _nextSize = Math.Max(SIZE_TABLE[index], minCapacity);
                decreaseNow = false;
            }
            else
            {
                decreaseNow = true;
            }
        }
        else if (size >= _nextSize)
        {
            index = Math.Min(index + INDEX_INCREMENT, maxIndex);
            _nextSize = Math.Min(SIZE_TABLE[index], maxCapacity);
            decreaseNow = false;
        }
    }

    public int nextSize()
    {
        return _nextSize;
    }
}