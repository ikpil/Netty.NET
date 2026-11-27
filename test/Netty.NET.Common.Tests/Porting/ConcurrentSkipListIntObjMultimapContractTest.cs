using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Porting;

// CLR contract cases for ConcurrentSkipListIntObjMultimap beyond the 1:1 original test class. Each case checks a
// contract of the Java source: int key order without overflow, the noKey sentinel, null rejection, value-equality
// conditional removal as the ownership boundary, and entry conservation under concurrent put/poll/remove.
public class ConcurrentSkipListIntObjMultimapContractTest
{
    [Fact]
    public void IntegerExtremesAreOrderedAndNeighborQueriesReportAbsence()
    {
        var map = new ConcurrentSkipListIntObjMultimap<string>(0);
        map.Put(int.MaxValue, "max");
        map.Put(-1, "minus one");
        map.Put(int.MinValue, "min");
        Assert.Equal(int.MinValue, map.FirstKey());
        Assert.Equal(int.MaxValue, map.LastKey());
        Assert.Null(map.LowerEntry(int.MinValue));
        Assert.Equal(0, map.LowerKey(int.MinValue));
        Assert.Null(map.HigherEntry(int.MaxValue));
        Assert.Equal(0, map.HigherKey(int.MaxValue));
        var entry = map.FloorEntry(-1);
        Assert.Equal(-1, entry.GetKey());
        Assert.Equal("minus one", entry.GetValue());
        Assert.Equal(int.MaxValue, map.CeilingKey(1));
    }

    [Fact]
    public void NoKeySentinelIsRejectedAsAKey()
    {
        var map = new ConcurrentSkipListIntObjMultimap<string>(-1);
        Assert.Throws<ArgumentException>(() => map.Put(-1, "a"));
        Assert.Throws<ArgumentException>(() => map.Remove(-1, "a"));
        Assert.Throws<ArgumentException>(() => map.CeilingEntry(-1));
        Assert.True(map.IsEmpty());
        Assert.Equal(0, map.Size());
    }

    [Fact]
    public void RemovingAnEntryThatIsGoneDoesNotRemoveANewerEntry()
    {
        var map = new ConcurrentSkipListIntObjMultimap<string>(-1);
        map.Put(2, "b");
        map.Put(1, "a");
        var first = map.FirstEntry();
        map.Clear();
        map.Put(3, "c");
        Assert.Equal(new ConcurrentSkipListIntObjMultimap<string>.IntEntry(1, "a"), first);
        Assert.False(map.Remove(first.GetKey(), first.GetValue()));
        Assert.Equal(1, map.Size());
        Assert.Equal(3, map.PollFirstEntry().GetKey());
    }

    [Fact]
    public void ConditionalRemovalRemovesOneValueEqualOccurrence()
    {
        // The source documents no ordering among values of one key, so only the remaining multiset is checked.
        var map = new ConcurrentSkipListIntObjMultimap<string>(-1);
        map.Put(7, new string(new[] { 'a' }));
        map.Put(7, "a");
        map.Put(7, "b");
        Assert.True(map.Remove(7, new string(new[] { 'a' })));
        Assert.Equal(2, map.Size());
        var remaining = new List<string> { map.PollFirstEntry().GetValue(), map.PollFirstEntry().GetValue() };
        remaining.Sort(StringComparer.Ordinal);
        Assert.Equal(new[] { "a", "b" }, remaining);
        Assert.Null(map.PollFirstEntry());
    }

    [Fact]
    public void NullValuesAreRejectedAndCannotRemoveAnything()
    {
        var map = new ConcurrentSkipListIntObjMultimap<string>(-1);
        Assert.Throws<ArgumentNullException>(() => map.Put(1, null));
        map.Put(1, "a");
        Assert.False(map.Remove(1, null));
        Assert.Equal(1, map.Size());
    }

    [Fact]
    public void ReentrantEqualityCannotRemoveAnUncomparedReplacement()
    {
        var map = new ConcurrentSkipListIntObjMultimap<Value>(-1);
        map.Put(1, new Value(1));
        var target = new Value(1) { OnCompare = () => { map.Clear(); map.Put(1, new Value(2)); } };
        Assert.False(map.Remove(1, target));
        Assert.Equal(1, map.Size());
        Assert.Equal(2, map.FirstEntry().GetValue().Id);
        Assert.Equal(2, target.Comparisons);
    }

    [Fact]
    public void FailedEqualityDoesNotChangeStorageOrCount()
    {
        var map = new ConcurrentSkipListIntObjMultimap<Value>(-1);
        var value = new Value(1);
        map.Put(1, value);
        var failure = new InvalidOperationException("comparison failed");
        var target = new Value(1) { OnCompare = () => throw failure };
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => map.Remove(1, target)));
        Assert.Equal(1, map.Size());
        Assert.Same(value, map.FirstEntry().GetValue());
    }

    [Fact]
    public async Task ConcurrentPublishAndCeilingClaimsNeverLoseOrDuplicateValues()
    {
        var map = new ConcurrentSkipListIntObjMultimap<object>(-1);
        var published = Enumerable.Range(0, 8).Select(producer => Task.Run(() =>
        {
            for (int i = 0; i < 250; i++)
            { int id = producer * 250 + i; map.Put(id % 17, id); }
        }, TestContext.Current.CancellationToken)).ToArray();
        await Task.WhenAll(published);
        Assert.Equal(2000, map.Size());
        var claimed = new ConcurrentDictionary<int, byte>();
        var consumers = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            ConcurrentSkipListIntObjMultimap<object>.IntEntry entry;
            while ((entry = map.PollCeilingEntry(0)) != null)
                Assert.True(claimed.TryAdd((int) entry.GetValue(), 0));
        }, TestContext.Current.CancellationToken)).ToArray();
        await Task.WhenAll(consumers);
        Assert.Equal(2000, claimed.Count);
        Assert.True(map.IsEmpty());
        Assert.Equal(0, map.Size());
    }

    [Fact]
    public async Task CompetingIteratorRemoversGrantOneOwnerPerValue()
    {
        var map = new ConcurrentSkipListIntObjMultimap<object>(-1);
        for (int i = 0; i < 1000; i++)
            map.Put(i % 31, i);
        var owned = new ConcurrentDictionary<int, byte>();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            // Weakly consistent iteration: an entry is only a candidate until Remove(key, value) succeeds.
            foreach (var entry in map)
                if (map.Remove(entry.GetKey(), entry.GetValue()))
                    Assert.True(owned.TryAdd((int) entry.GetValue(), 0));
        }, TestContext.Current.CancellationToken)));
        Assert.Equal(1000, owned.Count);
        Assert.True(map.IsEmpty());
        Assert.Equal(0, map.Size());
    }

    [Fact]
    public async Task PublishingWhileTakingDoesNotLoseAnEntryAtEmptyTransitions()
    {
        var map = new ConcurrentSkipListIntObjMultimap<object>(-1);
        var claimed = new ConcurrentDictionary<int, byte>();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var producers = Enumerable.Range(0, 8).Select(p => Task.Run(async () =>
        {
            await start.Task;
            for (int i = 0; i < 250; i++)
            {
                map.Put(i % 17, p * 250 + i);
                if ((i & 15) == 0) await Task.Yield();
            }
        }, TestContext.Current.CancellationToken)).ToArray();
        Task publishing = Task.WhenAll(producers);
        var consumers = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            start.TrySetResult();
            while (true)
            {
                var entry = map.PollFirstEntry();
                if (entry != null)
                    Assert.True(claimed.TryAdd((int) entry.GetValue(), 0));
                // Recheck emptiness after publication finishes; an earlier
                // failed take can precede the final published entry.
                else if (publishing.IsCompleted && map.IsEmpty())
                    break;
                else
                    await Task.Yield();
            }
        }, TestContext.Current.CancellationToken)).ToArray();
        await publishing;
        await Task.WhenAll(consumers);
        Assert.Equal(2000, claimed.Count);
        Assert.True(map.IsEmpty());
        Assert.Equal(0, map.Size());
    }

    [Fact]
    public void AdaptiveChunkCacheClaimsProcessesAndReindexesDirtyChunks()
    {
        // The pollChunk algorithm of AdaptivePoolingAllocator.ConcurrentSkipListChunkCache in the source: an
        // iterated entry is only a candidate; a chunk is processed only after remove(key, chunk) succeeds, and is
        // reinserted under its new remaining capacity.
        var map = new ConcurrentSkipListIntObjMultimap<Chunk>(-1);
        var smaller = new Chunk(4, 24);
        var larger = new Chunk(6, 40);
        map.Put(smaller.RemainingCapacity(), smaller);
        map.Put(larger.RemainingCapacity(), larger);
        const int size = 16;
        Assert.Null(map.PollCeilingEntry(size));
        Chunk bestChunk = null;
        int bestRemainingCapacity = 0;
        var itr = map.Iterator();
        while (itr.HasNext())
        {
            var entry = itr.Next();
            Chunk chunk;
            if (entry != null && (chunk = entry.GetValue()).HasUnprocessedFreelistEntries())
            {
                if (!map.Remove(entry.GetKey(), entry.GetValue()))
                {
                    continue;
                }
                chunk.ProcessFreelistEntries();
                int remainingCapacity = chunk.RemainingCapacity();
                if (remainingCapacity >= size &&
                        (bestChunk == null || remainingCapacity > bestRemainingCapacity))
                {
                    if (bestChunk != null)
                    {
                        map.Put(bestRemainingCapacity, bestChunk);
                    }
                    bestChunk = chunk;
                    bestRemainingCapacity = remainingCapacity;
                }
                else
                {
                    map.Put(remainingCapacity, chunk);
                }
            }
        }
        Assert.Same(larger, bestChunk);
        Assert.Equal(1, smaller.ProcessCount);
        Assert.Equal(1, larger.ProcessCount);
        var remaining = map.PollCeilingEntry(size);
        Assert.Equal(24, remaining.GetKey());
        Assert.Same(smaller, remaining.GetValue());
        Assert.True(map.IsEmpty());
    }

    private sealed class Value : IEquatable<Value>
    {
        internal readonly int Id;
        internal Action OnCompare;
        internal int Comparisons;
        internal Value(int id)
        {
            Id = id;
        }

        public bool Equals(Value other)
        {
            Comparisons++;
            Action callback = OnCompare;
            OnCompare = null;
            callback?.Invoke();
            return other is not null && Id == other.Id;
        }

        public override bool Equals(object other)
        {
            return other is Value value && Equals(value);
        }

        public override int GetHashCode()
        {
            return Id;
        }
    }

    private sealed class Chunk
    {
        private int _capacity;
        private readonly int _pending;
        private bool _dirty = true;
        internal int ProcessCount;
        internal Chunk(int capacity, int pending)
        {
            _capacity = capacity;
            _pending = pending;
        }

        internal int RemainingCapacity()
        {
            return _capacity;
        }

        internal bool HasUnprocessedFreelistEntries()
        {
            return _dirty;
        }

        internal void ProcessFreelistEntries()
        {
            _capacity = _pending;
            _dirty = false;
            ProcessCount++;
        }
    }
}
