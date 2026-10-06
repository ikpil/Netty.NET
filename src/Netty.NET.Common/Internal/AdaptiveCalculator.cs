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

namespace Netty.NET.Common.Internal;

/**
 * Calculate sizes in a adaptive way.
 */
public sealed class AdaptiveCalculator
{
    private const int INDEX_INCREMENT = 4;
    private const int INDEX_DECREMENT = 1;

    private static readonly int[] SIZE_TABLE;

    static AdaptiveCalculator()
    {
        List<int> sizeTable = new List<int>();
        // CLR: cover every positive Int32 bound, including values below the
        // original first bucket. Keep the original 16-byte/power-of-two buckets.
        for (int i = 1; i < 16; i++)
        {
            sizeTable.Add(i);
        }
        for (int i = 16; i < 512; i += 16)
        {
            sizeTable.Add(i);
        }

        // Suppress a warning since i becomes negative when an integer overflow happens
        for (int i = 512; i > 0; i <<= 1)
        {
            sizeTable.Add(i);
        }

        sizeTable.Add(int.MaxValue);
        SIZE_TABLE = sizeTable.ToArray();
    }

    private static int GetFloorIndex(int size)
    {
        int found = Array.BinarySearch(SIZE_TABLE, size);
        return found >= 0 ? found : ~found - 1;
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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimum);
        if (initial < minimum)
        {
            throw new ArgumentOutOfRangeException(nameof(initial), initial, "Initial size must not be less than minimum.");
        }

        if (maximum < initial)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum), maximum, "Maximum size must not be less than initial.");
        }

        // Keep an ordered index interval even when no original bucket fits the
        // requested range; clamp the selected size rather than raising its floor.
        minIndex = GetFloorIndex(minimum);
        maxIndex = GetFloorIndex(maximum);
        index = GetFloorIndex(initial);

        this.minCapacity = minimum;
        this.maxCapacity = maximum;
        _nextSize = Math.Clamp(SIZE_TABLE[index], minCapacity, maxCapacity);
    }

    public void Record(int size)
    {
        // The first positive bucket has a zero-size predecessor: a full one-byte
        // read must grow when the configured maximum leaves room.
        int previousSize = index == 0 ? 0 : SIZE_TABLE[index - INDEX_DECREMENT];
        if (size <= previousSize)
        {
            if (decreaseNow)
            {
                index = Math.Max(index - INDEX_DECREMENT, minIndex);
                _nextSize = Math.Clamp(SIZE_TABLE[index], minCapacity, maxCapacity);
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
            _nextSize = Math.Clamp(SIZE_TABLE[index], minCapacity, maxCapacity);
            decreaseNow = false;
        }
    }

    // Observes this owner's current guess; Record and queries require serial ownership.
    public int NextSize => _nextSize;
}
