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
using System.Globalization;
using Netty.NET.Common.Concurrent;
using IntEntry = Netty.NET.Common.Concurrent.ConcurrentSkipListIntObjMultimap<string>.IntEntry;
using Iterator = Netty.NET.Common.Concurrent.ConcurrentSkipListIntObjMultimap<string>.Iter<
    Netty.NET.Common.Concurrent.ConcurrentSkipListIntObjMultimap<string>.IntEntry>;

namespace Netty.NET.Common.Tests.Concurrent;

public class ConcurrentSkipListIntObjMultimapTest
{
    private ConcurrentSkipListIntObjMultimap<string> _map;
    private int _noKey;

    // CLR: xUnit creates a new test class instance for every test case, so the @BeforeEach method runs from the
    // constructor. A @RepeatedTest(100) method is a [Theory] over 100 repetition rows: each repetition is its own case
    // with a fresh map, as in JUnit. ThreadLocalRandom.current().nextInt(bound) is Random.Shared.Next(bound).
    public ConcurrentSkipListIntObjMultimapTest()
    {
        SetUp();
    }

    public static IEnumerable<object[]> Repetitions()
    {
        for (int i = 0; i < 100; i++)
        {
            yield return new object[] { i };
        }
    }

    private void SetUp()
    {
        _noKey = -1;
        _map = new ConcurrentSkipListIntObjMultimap<string>(_noKey);
    }

    [Fact]
    public void AddIterateAndRemoveEntries()
    {
        Assert.False(_map.Iterator().HasNext());
        _map.Put(1, "a");
        _map.Put(2, "b");
        Assert.False(_map.IsEmpty());
        Assert.Equal(2, _map.Size());
        IntEntry entry;
        Iterator itr = _map.Iterator();
        Assert.True(itr.HasNext());
        entry = itr.Next();
        itr.Remove();
        Assert.Equal(new IntEntry(1, "a"), entry);
        Assert.True(itr.HasNext());
        entry = itr.Next();
        itr.Remove();
        Assert.Equal(new IntEntry(2, "b"), entry);
        Assert.False(itr.HasNext());
        Assert.True(_map.IsEmpty());
        Assert.Equal(0, _map.Size());
    }

    [Fact]
    public void ClearMustRemoveAllEntries()
    {
        _map.Put(2, "b");
        _map.Put(1, "a");
        _map.Put(3, "c");
        Assert.Equal(3, _map.Size());
        _map.Clear();
        Assert.Equal(0, _map.Size());
        Assert.False(_map.Iterator().HasNext());
        Assert.True(_map.IsEmpty());
    }

    [Fact]
    public void PollingFirstEntryOfUniqueKeys()
    {
        _map.Put(2, "b");
        _map.Put(1, "a");
        _map.Put(3, "c");
        Assert.Equal(new IntEntry(1, "a"), _map.PollFirstEntry());
        Assert.Equal(new IntEntry(2, "b"), _map.PollFirstEntry());
        Assert.Equal(new IntEntry(3, "c"), _map.PollFirstEntry());
        Assert.True(_map.IsEmpty());
        Assert.Equal(0, _map.Size());
        Assert.False(_map.Iterator().HasNext());
    }

    [Fact]
    public void PollingLastEntryOfUniqueKeys()
    {
        _map.Put(2, "b");
        _map.Put(1, "a");
        _map.Put(3, "c");
        Assert.Equal(new IntEntry(3, "c"), _map.PollLastEntry());
        Assert.Equal(new IntEntry(2, "b"), _map.PollLastEntry());
        Assert.Equal(new IntEntry(1, "a"), _map.PollLastEntry());
        Assert.True(_map.IsEmpty());
        Assert.Equal(0, _map.Size());
        Assert.False(_map.Iterator().HasNext());
    }

    [Fact]
    public void AddMultipleEntriesForSameKey()
    {
        _map.Put(2, "b1");
        _map.Put(1, "a");
        _map.Put(2, "b2"); // second entry for the 2 key
        _map.Put(3, "c");
        Assert.Equal(4, _map.Size());

        IntEntry entry;
        Iterator itr = _map.Iterator();
        Assert.True(itr.HasNext());
        entry = itr.Next();
        itr.Remove();
        Assert.Equal(new IntEntry(1, "a"), entry);
        Assert.True(itr.HasNext());
        entry = itr.Next();
        IntEntry otherB = entry;
        itr.Remove();
        Assert.Contains(entry, new[] { new IntEntry(2, "b1"), new IntEntry(2, "b2") });
        Assert.True(itr.HasNext());
        entry = itr.Next();
        itr.Remove();
        Assert.Contains(entry, new[] { new IntEntry(2, "b1"), new IntEntry(2, "b2") });
        Assert.NotEqual(otherB, entry);
        Assert.True(itr.HasNext());
        entry = itr.Next();
        itr.Remove();
        Assert.Equal(new IntEntry(3, "c"), entry);
        Assert.False(itr.HasNext());
        Assert.True(_map.IsEmpty());
        Assert.Equal(0, _map.Size());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IteratorRemoveSecondOfMultiMappedEntry(bool withPriorRemoval)
    {
        _map.Put(1, "a");
        _map.Put(1, "b");

        Iterator itr = _map.Iterator();
        itr.Next();
        IntEntry entry = itr.Next();

        if (withPriorRemoval)
        {
            _map.Remove(entry.GetKey(), entry.GetValue());
        }

        itr.Remove();
        Assert.Equal(1, _map.Size());

        if (entry.Equals(new IntEntry(1, "a")))
        {
            Assert.Equal(new IntEntry(1, "b"), _map.PollFirstEntry());
        }
        else
        {
            Assert.Equal(new IntEntry(1, "a"), _map.PollFirstEntry());
        }
    }

    [Fact]
    public void FirstKeyOrEntry()
    {
        Assert.Equal(_noKey, _map.FirstKey());
        Assert.Null(_map.FirstEntry());
        _map.Put(2, "b");
        Assert.Equal(2, _map.FirstKey());
        Assert.Equal(new IntEntry(2, "b"), _map.FirstEntry());
        _map.Put(3, "c");
        Assert.Equal(2, _map.FirstKey());
        Assert.Equal(new IntEntry(2, "b"), _map.FirstEntry());
        _map.Put(2, "b2");
        Assert.Equal(2, _map.FirstKey());
        Assert.Contains(_map.FirstEntry(), new[] { new IntEntry(2, "b"), new IntEntry(2, "b2") });
        _map.Put(1, "a");
        Assert.Equal(1, _map.FirstKey());
        Assert.Equal(new IntEntry(1, "a"), _map.FirstEntry());
        _map.Put(2, "b3");
        Assert.Equal(1, _map.FirstKey());
        Assert.Equal(new IntEntry(1, "a"), _map.FirstEntry());
        _map.PollFirstEntry();
        Assert.Equal(2, _map.FirstKey());
        Assert.Contains(_map.FirstEntry(), new[] { new IntEntry(2, "b"), new IntEntry(2, "b2"), new IntEntry(2, "b3") });
    }

    [Fact]
    public void LastKeyOrEntry()
    {
        Assert.Equal(_noKey, _map.LastKey());
        Assert.Null(_map.LastEntry());
        _map.Put(2, "b");
        Assert.Equal(2, _map.LastKey());
        Assert.Equal(new IntEntry(2, "b"), _map.LastEntry());
        _map.Put(1, "a");
        Assert.Equal(2, _map.LastKey());
        Assert.Equal(new IntEntry(2, "b"), _map.LastEntry());
        _map.Put(2, "b2");
        Assert.Equal(2, _map.LastKey());
        Assert.Contains(_map.LastEntry(), new[] { new IntEntry(2, "b"), new IntEntry(2, "b2") });
        _map.Put(3, "c");
        Assert.Equal(3, _map.LastKey());
        Assert.Equal(new IntEntry(3, "c"), _map.LastEntry());
        _map.Put(2, "b3");
        Assert.Equal(3, _map.LastKey());
        Assert.Equal(new IntEntry(3, "c"), _map.LastEntry());
        _map.PollLastEntry();
        Assert.Equal(2, _map.LastKey());
        Assert.Contains(_map.LastEntry(), new[] { new IntEntry(2, "b"), new IntEntry(2, "b2"), new IntEntry(2, "b3") });
    }

    [Theory]
    [MemberData(nameof(Repetitions))]
    public void FirstLastKeyOrEntry(int repetition)
    {
        _ = repetition;
        int[] xs = new int[50];

        for (int i = 0; i < xs.Length; i++)
        {
            int key = Random.Shared.Next(50);
            _map.Put(key, "a");
            xs[i] = key;
        }

        Array.Sort(xs);
        Assert.Equal(xs[0], _map.FirstKey());
        Assert.Equal(new IntEntry(xs[0], "a"), _map.FirstEntry());
        Assert.Equal(xs[xs.Length - 1], _map.LastKey());
        Assert.Equal(new IntEntry(xs[xs.Length - 1], "a"), _map.LastEntry());
    }

    [Theory]
    [MemberData(nameof(Repetitions))]
    public void LowerEntryOrKey(int repetition)
    {
        _ = repetition;
        IntEntry[] xs = new IntEntry[50];

        for (int i = 0; i < xs.Length; i++)
        {
            int key = Random.Shared.Next(50);
            xs[i] = new IntEntry(key, key.ToString(CultureInfo.InvariantCulture));
            _map.Put(key, xs[i].GetValue());
        }

        Array.Sort(xs);

        for (int i = 0; i < 10; i++)
        {
            IntEntry target = xs[Random.Shared.Next(xs.Length)];
            IntEntry expected = null;

            foreach (IntEntry x in xs)
            {
                if (x.CompareTo(target) < 0)
                {
                    expected = x;
                }
                else
                {
                    break;
                }
            }

            Assert.Equal(expected, _map.LowerEntry(target.GetKey()));
            Assert.Equal(expected == null ? _noKey : expected.GetKey(), _map.LowerKey(target.GetKey()));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LowerEntryOrKeyMismatch(bool multiMapped)
    {
        _map.Put(1, "a");
        _map.Put(3, "b");
        _map.Put(4, "c");

        if (multiMapped)
        {
            _map.Put(1, "a");
            _map.Put(3, "b");
            _map.Put(4, "c");
        }

        Assert.Equal(1, _map.LowerKey(3));
        Assert.Equal(new IntEntry(1, "a"), _map.LowerEntry(3));
        Assert.Equal(3, _map.LowerKey(4));
        Assert.Equal(new IntEntry(3, "b"), _map.LowerEntry(4));
        Assert.Equal(_noKey, _map.LowerKey(1));
        Assert.Null(_map.LowerEntry(1));
    }

    [Theory]
    [MemberData(nameof(Repetitions))]
    public void FloorEntryOrKey(int repetition)
    {
        _ = repetition;
        IntEntry[] xs = new IntEntry[50];

        for (int i = 0; i < xs.Length; i++)
        {
            int key = Random.Shared.Next(50);
            xs[i] = new IntEntry(key, key.ToString(CultureInfo.InvariantCulture));
            _map.Put(key, xs[i].GetValue());
        }

        Array.Sort(xs);

        for (int i = 0; i < 10; i++)
        {
            IntEntry target = xs[Random.Shared.Next(xs.Length)];
            IntEntry expected = null;

            foreach (IntEntry x in xs)
            {
                if (x.CompareTo(target) <= 0)
                {
                    expected = x;
                }
                else
                {
                    break;
                }
            }

            Assert.Equal(expected, _map.FloorEntry(target.GetKey()));
            Assert.Equal(expected == null ? _noKey : expected.GetKey(), _map.FloorKey(target.GetKey()));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FloorEntryOrKeyMismatch(bool multiMapped)
    {
        _map.Put(1, "a");
        _map.Put(3, "b");
        _map.Put(4, "c");

        if (multiMapped)
        {
            _map.Put(1, "a");
            _map.Put(3, "b");
            _map.Put(4, "c");
        }

        Assert.Equal(1, _map.FloorKey(2));
        Assert.Equal(new IntEntry(1, "a"), _map.FloorEntry(2));
        Assert.Equal(3, _map.FloorKey(3));
        Assert.Equal(new IntEntry(3, "b"), _map.FloorEntry(3));
    }

    [Theory]
    [MemberData(nameof(Repetitions))]
    public void CeilEntryOrKey(int repetition)
    {
        _ = repetition;
        IntEntry[] xs = new IntEntry[50];

        for (int i = 0; i < xs.Length; i++)
        {
            int key = Random.Shared.Next(50);
            xs[i] = new IntEntry(key, key.ToString(CultureInfo.InvariantCulture));
            _map.Put(key, xs[i].GetValue());
        }

        Array.Sort(xs);

        for (int i = 0; i < 10; i++)
        {
            IntEntry target = xs[Random.Shared.Next(xs.Length)];
            IntEntry expected = null;

            foreach (IntEntry x in xs)
            {
                if (x.CompareTo(target) >= 0)
                {
                    expected = x;
                    break;
                }
            }

            Assert.Equal(expected, _map.CeilingEntry(target.GetKey()));
            Assert.Equal(expected == null ? _noKey : expected.GetKey(), _map.CeilingKey(target.GetKey()));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CeilEntryOrKeyMismatch(bool multiMapped)
    {
        _map.Put(1, "a");
        _map.Put(2, "b");
        _map.Put(4, "c");

        if (multiMapped)
        {
            _map.Put(1, "a");
            _map.Put(2, "b");
            _map.Put(4, "c");
        }

        Assert.Equal(2, _map.CeilingKey(2));
        Assert.Equal(new IntEntry(2, "b"), _map.CeilingEntry(2));
        Assert.Equal(4, _map.CeilingKey(3));
        Assert.Equal(new IntEntry(4, "c"), _map.CeilingEntry(3));
    }

    [Theory]
    [MemberData(nameof(Repetitions))]
    public void HigherEntryOrKey(int repetition)
    {
        _ = repetition;
        IntEntry[] xs = new IntEntry[50];

        for (int i = 0; i < xs.Length; i++)
        {
            int key = Random.Shared.Next(50);
            xs[i] = new IntEntry(key, key.ToString(CultureInfo.InvariantCulture));
            _map.Put(key, xs[i].GetValue());
        }

        Array.Sort(xs);

        for (int i = 0; i < 10; i++)
        {
            IntEntry target = xs[Random.Shared.Next(xs.Length)];
            IntEntry expected = null;

            foreach (IntEntry x in xs)
            {
                if (x.CompareTo(target) > 0)
                {
                    expected = x;
                    break;
                }
            }

            Assert.Equal(expected, _map.HigherEntry(target.GetKey()));
            Assert.Equal(expected == null ? _noKey : expected.GetKey(), _map.HigherKey(target.GetKey()));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HigherEntryOrKeyMismatch(bool multiMapped)
    {
        _map.Put(1, "a");
        _map.Put(2, "b");
        _map.Put(4, "c");

        if (multiMapped)
        {
            _map.Put(1, "a");
            _map.Put(2, "b");
            _map.Put(4, "c");
        }

        Assert.Equal(4, _map.HigherKey(2));
        Assert.Equal(new IntEntry(4, "c"), _map.HigherEntry(2));
        Assert.Equal(4, _map.HigherKey(3));
        Assert.Equal(new IntEntry(4, "c"), _map.HigherEntry(3));
        Assert.Equal(_noKey, _map.HigherKey(4));
        Assert.Null(_map.HigherEntry(4));
    }

    [Fact]
    public void PollingFirstEntryOfMultiMappedKeys()
    {
        _map.Put(2, "b");
        _map.Put(1, "a");
        _map.Put(2, "b");
        _map.Put(3, "c");
        Assert.Equal(new IntEntry(1, "a"), _map.PollFirstEntry());
        Assert.Equal(new IntEntry(2, "b"), _map.PollFirstEntry());
        Assert.Equal(new IntEntry(2, "b"), _map.PollFirstEntry());
        Assert.Equal(new IntEntry(3, "c"), _map.PollFirstEntry());
        Assert.True(_map.IsEmpty());
        Assert.Equal(0, _map.Size());
        Assert.False(_map.Iterator().HasNext());
    }

    [Fact]
    public void PollingLastEntryOfMultiMappedKeys()
    {
        _map.Put(2, "b");
        _map.Put(1, "a");
        _map.Put(2, "b");
        _map.Put(3, "c");
        Assert.Equal(new IntEntry(3, "c"), _map.PollLastEntry());
        Assert.Equal(new IntEntry(2, "b"), _map.PollLastEntry());
        Assert.Equal(new IntEntry(2, "b"), _map.PollLastEntry());
        Assert.Equal(new IntEntry(1, "a"), _map.PollLastEntry());
        Assert.True(_map.IsEmpty());
        Assert.Equal(0, _map.Size());
        Assert.False(_map.Iterator().HasNext());
    }

    [Fact]
    public void PollCeilingEntry()
    {
        _map.Put(1, "a");
        _map.Put(2, "b");
        _map.Put(2, "b");
        _map.Put(3, "c");
        _map.Put(4, "d");
        _map.Put(4, "d");
        Assert.Equal(new IntEntry(2, "b"), _map.PollCeilingEntry(2));
        Assert.Equal(new IntEntry(2, "b"), _map.PollCeilingEntry(2));
        Assert.Equal(new IntEntry(3, "c"), _map.PollCeilingEntry(2));
        Assert.Equal(new IntEntry(4, "d"), _map.PollCeilingEntry(2));
        Assert.Equal(new IntEntry(4, "d"), _map.PollCeilingEntry(2));
        Assert.Null(_map.PollCeilingEntry(2));
        Assert.False(_map.IsEmpty());
        Assert.Equal(1, _map.Size());
    }
}
