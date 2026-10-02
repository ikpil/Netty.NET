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
using System.Linq;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Concurrent;

// The original file name records provenance. Assertions use native Try APIs and
// snapshot + successful removal instead of a mutable Java Iterator facade.
public class ConcurrentSkipListIntObjMultimapTest
{
    private readonly ConcurrentOrderedMultiMap<string> _map = new();
    private static KeyValuePair<int, string> Entry(int key, string value) => new(key, value);

    [Fact]
    public void addIterateAndRemoveEntries()
    {
        Assert.Empty(_map.Snapshot());
        _map.Add(1, "a");
        _map.Add(2, "b");
        Assert.False(_map.IsEmpty);
        Assert.Equal(2, _map.Count);
        var entries = _map.Snapshot();
        Assert.Equal(new[] { Entry(1, "a"), Entry(2, "b") }, entries);
        foreach (var entry in entries)
            Assert.True(_map.TryRemove(entry.Key, entry.Value));
        Assert.True(_map.IsEmpty);
        Assert.Equal(0, _map.Count);
    }

    [Fact]
    public void clearMustRemoveAllEntries()
    {
        _map.Add(2, "b");
        _map.Add(1, "a");
        _map.Add(3, "c");
        Assert.Equal(3, _map.Count);
        _map.Clear();
        Assert.Equal(0, _map.Count);
        Assert.Empty(_map.Snapshot());
        Assert.True(_map.IsEmpty);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void pollingFirstOrLastEntryOfUniqueOrMultiMappedKeys(bool last, bool duplicate)
    {
        // Maps pollingFirstEntryOfUniqueKeys, pollingLastEntryOfUniqueKeys,
        // pollingFirstEntryOfMultiMappedKeys and pollingLastEntryOfMultiMappedKeys.
        _map.Add(2, "b");
        _map.Add(1, "a");
        if (duplicate)
            _map.Add(2, "b");
        _map.Add(3, "c");
        var expected = duplicate ? new[] { 1, 2, 2, 3 } : new[] { 1, 2, 3 };
        if (last)
            Array.Reverse(expected);
        foreach (int key in expected)
        {
            bool found = last ? _map.TryTakeLast(out var entry) : _map.TryTakeFirst(out entry);
            Assert.True(found);
            Assert.Equal(Entry(key, key == 1 ? "a" : key == 2 ? "b" : "c"), entry);
        }
        Assert.True(_map.IsEmpty);
        Assert.Equal(0, _map.Count);
        Assert.Empty(_map.Snapshot());
    }

    [Fact]
    public void addMultipleEntriesForSameKey()
    {
        _map.Add(2, "b1");
        _map.Add(1, "a");
        _map.Add(2, "b2"); // second entry for the 2 key
        _map.Add(3, "c");
        Assert.Equal(4, _map.Count);
        var entries = _map.Snapshot();
        Assert.Equal(Entry(1, "a"), entries[0]);
        Assert.Equal(Entry(3, "c"), entries[3]);
        Assert.Contains(entries[1], new[] { Entry(2, "b1"), Entry(2, "b2") });
        Assert.Contains(entries[2], new[] { Entry(2, "b1"), Entry(2, "b2") });
        Assert.NotEqual(entries[1], entries[2]);
        foreach (var entry in entries)
            Assert.True(_map.TryRemove(entry.Key, entry.Value));
        Assert.True(_map.IsEmpty);
        Assert.Equal(0, _map.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void iteratorRemoveSecondOfMultiMappedEntry(bool withPriorRemoval)
    {
        _map.Add(1, "a");
        _map.Add(1, "b");
        var second = _map.Snapshot()[1];
        if (withPriorRemoval)
            Assert.True(_map.TryRemove(second.Key, second.Value));
        Assert.Equal(!withPriorRemoval, _map.TryRemove(second.Key, second.Value));
        Assert.Equal(1, _map.Count);
        Assert.True(_map.TryTakeFirst(out var remaining));
        Assert.Equal(Entry(1, second.Value == "a" ? "b" : "a"), remaining);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void firstOrLastKeyOrEntry(bool last)
    {
        // Both original firstKeyOrEntry / lastKeyOrEntry incremental scenarios.
        bool found = last ? _map.TryPeekLast(out var entry) : _map.TryPeekFirst(out entry);
        Assert.False(found);
        _map.Add(2, "b");
        Assert.True(last ? _map.TryPeekLast(out entry) : _map.TryPeekFirst(out entry));
        Assert.Equal(Entry(2, "b"), entry);
        _map.Add(last ? 1 : 3, last ? "a" : "c");
        _map.Add(2, "b2");
        Assert.True(last ? _map.TryPeekLast(out entry) : _map.TryPeekFirst(out entry));
        Assert.Contains(entry, new[] { Entry(2, "b"), Entry(2, "b2") });
        _map.Add(last ? 3 : 1, last ? "c" : "a");
        _map.Add(2, "b3");
        Assert.True(last ? _map.TryPeekLast(out entry) : _map.TryPeekFirst(out entry));
        Assert.Equal(last ? Entry(3, "c") : Entry(1, "a"), entry);
        Assert.True(last ? _map.TryTakeLast(out _) : _map.TryTakeFirst(out _));
        Assert.True(last ? _map.TryPeekLast(out entry) : _map.TryPeekFirst(out entry));
        Assert.Contains(entry, new[] { Entry(2, "b"), Entry(2, "b2"), Entry(2, "b3") });
    }

    [Fact]
    public void firstLastKeyOrEntry()
    {
        var random = new Random(4201);
        for (int repetition = 0; repetition < 100; repetition++)
        {
            _map.Clear();
            var keys = new int[50];
            for (int i = 0; i < keys.Length; i++)
            { keys[i] = random.Next(50); _map.Add(keys[i], "a"); }
            Array.Sort(keys);
            Assert.True(_map.TryPeekFirst(out var first));
            Assert.Equal(Entry(keys[0], "a"), first);
            Assert.True(_map.TryPeekLast(out var last));
            Assert.Equal(Entry(keys[^1], "a"), last);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NeighborEntryOrKeyMatchesOriginalHundredRepetitions(int relation)
    {
        // Maps lowerEntryOrKey, floorEntryOrKey, ceilEntryOrKey, higherEntryOrKey.
        var random = new Random(4202 + relation);
        for (int repetition = 0; repetition < 100; repetition++)
        {
            _map.Clear();
            var keys = new int[50];
            for (int i = 0; i < keys.Length; i++)
            { keys[i] = random.Next(50); _map.Add(keys[i], keys[i].ToString()); }
            Array.Sort(keys);
            for (int trial = 0; trial < 10; trial++)
            {
                int target = keys[random.Next(keys.Length)];
                int[] candidates = keys.Where(k => relation switch { 0 => k < target, 1 => k <= target, 2 => k >= target, _ => k > target }).ToArray();
                bool found = Find(relation, target, out var actual);
                Assert.Equal(candidates.Length != 0, found);
                if (found)
                    Assert.Equal(Entry(relation < 2 ? candidates[^1] : candidates[0], actual.Key.ToString()), actual);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NeighborEntryOrKeyMismatch(bool multiMapped)
    {
        // The four original mismatch methods, each with both duplicate variants.
        _map.Add(1, "a");
        _map.Add(3, "b");
        _map.Add(4, "c");
        if (multiMapped)
        { _map.Add(1, "a"); _map.Add(3, "b"); _map.Add(4, "c"); }
        Assert.True(_map.TryGetLower(3, out var e));
        Assert.Equal(Entry(1, "a"), e);
        Assert.True(_map.TryGetLower(4, out e));
        Assert.Equal(Entry(3, "b"), e);
        Assert.False(_map.TryGetLower(1, out _));
        Assert.True(_map.TryGetFloor(2, out e));
        Assert.Equal(Entry(1, "a"), e);
        Assert.True(_map.TryGetFloor(3, out e));
        Assert.Equal(Entry(3, "b"), e);
        _map.Clear();
        _map.Add(1, "a");
        _map.Add(2, "b");
        _map.Add(4, "c");
        if (multiMapped)
        { _map.Add(1, "a"); _map.Add(2, "b"); _map.Add(4, "c"); }
        Assert.True(_map.TryGetCeiling(2, out e));
        Assert.Equal(Entry(2, "b"), e);
        Assert.True(_map.TryGetCeiling(3, out e));
        Assert.Equal(Entry(4, "c"), e);
        Assert.True(_map.TryGetHigher(2, out e));
        Assert.Equal(Entry(4, "c"), e);
        Assert.True(_map.TryGetHigher(3, out e));
        Assert.Equal(Entry(4, "c"), e);
        Assert.False(_map.TryGetHigher(4, out _));
    }

    [Fact]
    public void pollCeilingEntry()
    {
        _map.Add(1, "a");
        _map.Add(2, "b");
        _map.Add(2, "b");
        _map.Add(3, "c");
        _map.Add(4, "d");
        _map.Add(4, "d");
        foreach (int key in new[] { 2, 2, 3, 4, 4 })
        {
            Assert.True(_map.TryTakeCeiling(2, out var entry));
            Assert.Equal(Entry(key, key == 2 ? "b" : key == 3 ? "c" : "d"), entry);
        }
        Assert.False(_map.TryTakeCeiling(2, out _));
        Assert.False(_map.IsEmpty);
        Assert.Equal(1, _map.Count);
    }

    private bool Find(int relation, int key, out KeyValuePair<int, string> entry) => relation switch
    {
        0 => _map.TryGetLower(key, out entry),
        1 => _map.TryGetFloor(key, out entry),
        2 => _map.TryGetCeiling(key, out entry),
        _ => _map.TryGetHigher(key, out entry)
    };
}
