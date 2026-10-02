/*
 * Copyright 2026 The Netty Project
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

namespace Netty.NET.Common.Concurrent;

/// <summary>A synchronized ordered map allowing multiple values per integer key.</summary>
/// <remarks>Try methods distinguish absence from every valid key/value. Equal-key
/// values are selected in insertion order. Snapshot does not transfer ownership;
/// consumers must successfully remove an entry before processing pooled values.
/// This SortedList-backed implementation uses a lock, not a nonblocking skip list.
/// Original contracts/comments and CLR differences: docs/common-ordered-multimap.md.</remarks>
public sealed class ConcurrentOrderedMultiMap<T>
{
    private readonly object _gate = new();
    private readonly SortedList<int, List<Entry>> _buckets = new();
    private int _count;

    // Distinct identity for each insertion permits equality callbacks outside
    // the lock and prevents a stale comparison from deleting a replacement.
    private sealed class Entry
    {
        internal readonly T Value;
        internal Entry(T value) => Value = value;
    }

    public int Count { get { lock (_gate) return _count; } }
    public bool IsEmpty { get { lock (_gate) return _count == 0; } }

    public void Add(int key, T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var entry = new Entry(value);
        lock (_gate)
        {
            // Validate the total before mutating either storage or count.
            int count = checked(_count + 1);
            if (_buckets.TryGetValue(key, out var bucket))
                bucket.Add(entry);
            else
                _buckets.Add(key, new List<Entry> { entry });
            _count = count;
        }
    }

    public void Clear()
    {
        lock (_gate)
        { _buckets.Clear(); _count = 0; }
    }

    public KeyValuePair<int, T>[] Snapshot()
    {
        lock (_gate)
        {
            var result = new KeyValuePair<int, T>[_count];
            int position = 0;
            foreach (var pair in _buckets)
                foreach (var entry in pair.Value)
                    result[position++] = new KeyValuePair<int, T>(pair.Key, entry.Value);
            return result;
        }
    }

    public bool TryPeekFirst(out KeyValuePair<int, T> entry)
    {
        lock (_gate)
            return Select(0, false, out entry);
    }

    public bool TryPeekLast(out KeyValuePair<int, T> entry)
    {
        lock (_gate)
            return Select(_buckets.Count - 1, false, out entry);
    }

    public bool TryTakeFirst(out KeyValuePair<int, T> entry)
    {
        lock (_gate)
            return Select(0, true, out entry);
    }

    public bool TryTakeLast(out KeyValuePair<int, T> entry)
    {
        lock (_gate)
            return Select(_buckets.Count - 1, true, out entry);
    }

    public bool TryGetLower(int key, out KeyValuePair<int, T> entry)
    {
        lock (_gate)
            return Select(LowerBound(key, false) - 1, false, out entry);
    }

    public bool TryGetFloor(int key, out KeyValuePair<int, T> entry)
    {
        lock (_gate)
            return Select(LowerBound(key, true) - 1, false, out entry);
    }

    public bool TryGetCeiling(int key, out KeyValuePair<int, T> entry)
    {
        lock (_gate)
            return Select(LowerBound(key, false), false, out entry);
    }

    public bool TryGetHigher(int key, out KeyValuePair<int, T> entry)
    {
        lock (_gate)
            return Select(LowerBound(key, true), false, out entry);
    }

    /// <summary>Atomically removes one value at the smallest key at least key.</summary>
    public bool TryTakeCeiling(int key, out KeyValuePair<int, T> entry)
    {
        lock (_gate)
            return Select(LowerBound(key, false), true, out entry);
    }

    /// <summary>Removes one equal value at key. Null never matches an entry.</summary>
    public bool TryRemove(int key, T value)
    {
        if (value is null)
            return false;
        while (true)
        {
            Entry[] candidates;
            lock (_gate)
            {
                if (!_buckets.TryGetValue(key, out var bucket))
                    return false;
                candidates = bucket.ToArray();
            }
            bool lostMatch = false;
            foreach (Entry candidate in candidates)
            {
                // User-defined Equals may reenter or throw. It cannot run under
                // the storage lock or invalidate a live bucket iterator.
                if (!EqualityComparer<T>.Default.Equals(value, candidate.Value))
                    continue;
                lock (_gate)
                {
                    if (_buckets.TryGetValue(key, out var bucket))
                    {
                        int index = bucket.IndexOf(candidate);
                        if (index >= 0)
                        {
                            bucket.RemoveAt(index);
                            if (bucket.Count == 0)
                                _buckets.Remove(key);
                            _count--;
                            return true;
                        }
                    }
                }
                lostMatch = true;
            }
            if (!lostMatch)
                return false;
            // A competing removal won after the snapshot. Retry the current
            // bucket rather than granting ownership of a stale entry.
        }
    }

    // All selection and mutation callers hold _gate. Binary search compares
    // integer keys directly, avoiding subtraction overflow and sentinel keys.
    private int LowerBound(int key, bool exclusive)
    {
        int low = 0, high = _buckets.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            int candidate = _buckets.Keys[middle];
            if (candidate < key || (exclusive && candidate == key))
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }

    private bool Select(int index, bool remove, out KeyValuePair<int, T> entry)
    {
        if ((uint)index >= (uint)_buckets.Count)
        { entry = default; return false; }
        int key = _buckets.Keys[index];
        var bucket = _buckets.Values[index];
        entry = new KeyValuePair<int, T>(key, bucket[0].Value);
        if (remove)
        {
            bucket.RemoveAt(0);
            if (bucket.Count == 0)
                _buckets.RemoveAt(index);
            _count--;
        }
        return true;
    }
}
