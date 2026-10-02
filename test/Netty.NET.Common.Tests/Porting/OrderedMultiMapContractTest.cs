using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Porting;

public class OrderedMultiMapContractTest
{
    [Fact]
    public void TryResultsDistinguishAllKeysAndDefaultValuesFromAbsence()
    {
        var map = new ConcurrentOrderedMultiMap<int>();
        map.Add(int.MaxValue, 0);
        map.Add(-1, 0);
        map.Add(int.MinValue, 0);
        Assert.True(map.TryPeekFirst(out var first));
        Assert.Equal(int.MinValue, first.Key);
        Assert.True(map.TryPeekLast(out var last));
        Assert.Equal(int.MaxValue, last.Key);
        Assert.False(map.TryGetLower(int.MinValue, out _));
        Assert.False(map.TryGetHigher(int.MaxValue, out _));
        Assert.True(map.TryGetFloor(-1, out var entry));
        Assert.Equal(-1, entry.Key);
        Assert.Equal(0, entry.Value);
        Assert.True(map.TryGetCeiling(0, out entry));
        Assert.Equal(int.MaxValue, entry.Key);
    }

    [Fact]
    public void SnapshotIsOrderedStableAndDoesNotGrantRemovalOwnership()
    {
        var map = new ConcurrentOrderedMultiMap<string>();
        map.Add(2, "b");
        map.Add(1, "a");
        var snapshot = map.Snapshot();
        map.Clear();
        map.Add(3, "c");
        Assert.Equal(new[] { new KeyValuePair<int, string>(1, "a"), new(2, "b") }, snapshot);
        Assert.False(map.TryRemove(snapshot[0].Key, snapshot[0].Value));
        Assert.Equal(1, map.Count);
        Assert.True(map.TryTakeFirst(out var actual));
        Assert.Equal(3, actual.Key);
    }

    [Fact]
    public void EqualKeyValuesAreSelectedFifoAndOneEqualOccurrenceIsRemoved()
    {
        var map = new ConcurrentOrderedMultiMap<string>();
        map.Add(7, new string(new[] { 'a' }));
        map.Add(7, "a");
        map.Add(7, "b");
        Assert.True(map.TryRemove(7, new string(new[] { 'a' })));
        Assert.Equal(2, map.Count);
        Assert.True(map.TryTakeLast(out var first));
        Assert.Equal("a", first.Value);
        Assert.True(map.TryTakeFirst(out var second));
        Assert.Equal("b", second.Value);
        Assert.False(map.TryTakeFirst(out _));
    }

    [Fact]
    public void NullValuesAreRejectedAndCannotRemoveAnything()
    {
        var map = new ConcurrentOrderedMultiMap<string>();
        Assert.Throws<ArgumentNullException>(() => map.Add(1, null));
        map.Add(1, "a");
        Assert.False(map.TryRemove(1, null));
        Assert.Equal(1, map.Count);
    }

    [Fact]
    public void ReentrantEqualityCannotRemoveAnUncomparedReplacement()
    {
        var map = new ConcurrentOrderedMultiMap<Value>();
        map.Add(1, new Value(1));
        var target = new Value(1) { OnCompare = () => { map.Clear(); map.Add(1, new Value(2)); } };
        Assert.False(map.TryRemove(1, target));
        Assert.Equal(1, map.Count);
        Assert.Equal(2, map.Snapshot()[0].Value.Id);
        Assert.Equal(2, target.Comparisons);
    }

    [Fact]
    public void FailedEqualityDoesNotChangeStorageOrCount()
    {
        var map = new ConcurrentOrderedMultiMap<Value>();
        var value = new Value(1);
        map.Add(1, value);
        var failure = new InvalidOperationException("comparison failed");
        var target = new Value(1) { OnCompare = () => throw failure };
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => map.TryRemove(1, target)));
        Assert.Equal(1, map.Count);
        Assert.Same(value, map.Snapshot()[0].Value);
    }

    [Fact]
    public async Task ConcurrentPublishAndCeilingClaimsNeverLoseOrDuplicateValues()
    {
        var map = new ConcurrentOrderedMultiMap<int>();
        var published = Enumerable.Range(0, 8).Select(producer => Task.Run(() =>
        {
            for (int i = 0; i < 250; i++)
            { int id = producer * 250 + i; map.Add(id % 17, id); }
        }, TestContext.Current.CancellationToken)).ToArray();
        await Task.WhenAll(published);
        Assert.Equal(2000, map.Count);
        var claimed = new ConcurrentDictionary<int, byte>();
        var consumers = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            while (map.TryTakeCeiling(0, out var entry))
                Assert.True(claimed.TryAdd(entry.Value, 0));
        }, TestContext.Current.CancellationToken)).ToArray();
        await Task.WhenAll(consumers);
        Assert.Equal(2000, claimed.Count);
        Assert.True(map.IsEmpty);
        Assert.Equal(0, map.Count);
    }

    [Fact]
    public async Task CompetingSnapshotRemoversGrantOneOwnerPerValue()
    {
        var map = new ConcurrentOrderedMultiMap<int>();
        for (int i = 0; i < 1000; i++)
            map.Add(i % 31, i);
        var snapshots = Enumerable.Range(0, 8).Select(_ => map.Snapshot()).ToArray();
        var owned = new ConcurrentDictionary<int, byte>();
        await Task.WhenAll(snapshots.Select(snapshot => Task.Run(() =>
        {
            foreach (var entry in snapshot)
                if (map.TryRemove(entry.Key, entry.Value))
                    Assert.True(owned.TryAdd(entry.Value, 0));
        }, TestContext.Current.CancellationToken)));
        Assert.Equal(1000, owned.Count);
        Assert.True(map.IsEmpty);
    }

    [Fact]
    public async Task PublishingWhileTakingDoesNotLoseAnEntryAtEmptyTransitions()
    {
        var map = new ConcurrentOrderedMultiMap<int>();
        var claimed = new ConcurrentDictionary<int, byte>();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var producers = Enumerable.Range(0, 8).Select(p => Task.Run(async () =>
        {
            await start.Task;
            for (int i = 0; i < 250; i++)
            {
                map.Add(i % 17, p * 250 + i);
                if ((i & 15) == 0) await Task.Yield();
            }
        }, TestContext.Current.CancellationToken)).ToArray();
        Task publishing = Task.WhenAll(producers);
        var consumers = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            start.TrySetResult();
            while (true)
            {
                if (map.TryTakeFirst(out var entry))
                    Assert.True(claimed.TryAdd(entry.Value, 0));
                // Recheck emptiness after publication finishes; an earlier
                // failed take can precede the final published entry.
                else if (publishing.IsCompleted && map.IsEmpty)
                    break;
                else
                    await Task.Yield();
            }
        }, TestContext.Current.CancellationToken)).ToArray();
        await publishing;
        await Task.WhenAll(consumers);
        Assert.Equal(2000, claimed.Count);
        Assert.True(map.IsEmpty);
    }

    [Fact]
    public void AdaptiveChunkCacheClaimsProcessesAndReindexesDirtyChunks()
    {
        // Port the fallback algorithm of AdaptivePoolingAllocator's concurrent
        // chunk cache: a snapshot is only a candidate list, not ownership.
        var map = new ConcurrentOrderedMultiMap<Chunk>();
        var smaller = new Chunk(4, 24);
        var larger = new Chunk(6, 40);
        map.Add(smaller.Capacity, smaller);
        map.Add(larger.Capacity, larger);
        Assert.False(map.TryTakeCeiling(16, out _));
        Chunk best = null;
        foreach (var entry in map.Snapshot())
        {
            if (!map.TryRemove(entry.Key, entry.Value))
                continue;
            var candidate = entry.Value;
            candidate.Process();
            if (candidate.Capacity >= 16 && (best is null || candidate.Capacity > best.Capacity))
            {
                if (best is not null)
                    map.Add(best.Capacity, best);
                best = candidate;
            }
            else
                map.Add(candidate.Capacity, candidate);
        }
        Assert.Same(larger, best);
        Assert.Equal(1, smaller.ProcessCount);
        Assert.Equal(1, larger.ProcessCount);
        Assert.True(map.TryTakeCeiling(16, out var remaining));
        Assert.Equal(24, remaining.Key);
        Assert.Same(smaller, remaining.Value);
        Assert.True(map.IsEmpty);
    }

    private sealed class Value : IEquatable<Value>
    {
        internal readonly int Id;
        internal Action OnCompare;
        internal int Comparisons;
        internal Value(int id) => Id = id;
        public bool Equals(Value other)
        {
            Comparisons++;
            Action callback = OnCompare;
            OnCompare = null;
            callback?.Invoke();
            return other is not null && Id == other.Id;
        }
        public override bool Equals(object other) => other is Value value && Equals(value);
        public override int GetHashCode() => Id;
    }

    private sealed class Chunk
    {
        internal int Capacity;
        private readonly int _pending;
        internal int ProcessCount;
        internal Chunk(int capacity, int pending) { Capacity = capacity; _pending = pending; }
        internal void Process() { Capacity = _pending; ProcessCount++; }
    }
}
