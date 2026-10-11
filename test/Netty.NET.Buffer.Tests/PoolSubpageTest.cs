/*
 * Copyright 2015 The Netty Project
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
using System.Threading.Tasks;

namespace Netty.NET.Buffer.Tests;

public class PoolSubpageTest
{
    private static SizeClasses Sizes(int shifts = 12, int order = 6, int alignment = 0)
        => new(1 << shifts, shifts, 1 << (shifts + order), alignment);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(256)]
    [InlineData(257)]
    public void BitmapWordsAndPartialLastWordReuseExactlyTheFreedSlots(int count)
    {
        int order = 0;
        while (1 << order < count)
            order++;
        SizeClasses sizes = Sizes(order: order);
        var chunk = new PoolChunk(sizes);
        var head = new PoolSubpage(sizes.Size2SizeIdx(4096), sizes);
        long run = chunk.AllocateRun(count * 4096);
        PoolSubpage sub;
        lock (head.SyncRoot)
        {
            sub = new PoolSubpage(head, chunk, PoolChunk.RunOffset(run), count * 4096, 4096);
            for (int i = 0; i < count; i++)
                Assert.Equal(i, PoolChunk.BitmapIdx(sub.Allocate()));
            Assert.Equal(-1L, sub.Allocate());
            Assert.Same(head, head.Next);
            Assert.Equal(0, sub.NumAvailable);
            sub.Free(count - 1);
            if (count > 1)
                sub.Free(0);
            Assert.Same(sub, head.Next);
            Assert.Equal(count > 1 ? 0 : count - 1, PoolChunk.BitmapIdx(sub.Allocate()));
            if (count > 1)
                Assert.Equal(count - 1, PoolChunk.BitmapIdx(sub.Allocate()));
            for (int i = 0; i < count; i++)
                Assert.True(sub.Free(i));
            Assert.Equal(count, sub.NumAvailable);
            Assert.True(sub.IsDoNotDestroy);
            Assert.Same(head, sub.Next);
            Assert.Same(head, sub.Prev);
        }
        IPoolSubpageMetric metric = sub;
        Assert.Equal(count, metric.MaxNumElements);
        Assert.Equal(4096, metric.ElementSize);
        Assert.Equal(4096, metric.PageSize);
        Assert.Contains($"0/{count}", sub.ToString());
    }

    [Fact]
    public void SingleAvailableSubpageKeepsItsRunAndReusesTheLastFreedSlot()
    {
        SizeClasses sizes = Sizes();
        var chunk = new PoolChunk(sizes);
        long handle = chunk.AllocateSmall(0, out PoolChunk owner);
        Assert.Same(chunk, owner);
        Assert.Equal((1L << 34) | (1L << 33) | (1L << 32), handle);
        Assert.True(PoolChunk.IsSubpage(handle));
        PoolSubpage sub = chunk.SubpageAt(0);
        Assert.Equal(256, sub.MaxNumElements);
        Assert.Equal(255, sub.NumAvailable);
        chunk.FreeSmall(handle);
        Assert.Equal(chunk.ChunkSize - 4096, chunk.FreeBytes);
        Assert.Equal(256, sub.NumAvailable);
        Assert.Equal(handle, chunk.AllocateSmall(0, out owner));
        chunk.FreeSmall(handle);
    }

    public static IEnumerable<object[]> Configurations()
    {
        foreach (int shifts in new[] { 12, 13, 16 })
            foreach (int alignment in new[] { 0, 64, 1 << shifts })
                yield return new object[] { shifts, alignment };
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0)
        { int remainder = a % b; a = b; b = remainder; }
        return a;
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void EverySmallClassPartitionsRunsAndReturnsAllButOneCachedRun(int shifts, int alignment)
    {
        SizeClasses sizes = Sizes(shifts, alignment: alignment);
        for (int index = 0; index <= sizes.SmallMaxSizeIdx; index++)
        {
            var chunk = new PoolChunk(sizes);
            int elem = sizes.SizeIdx2Size(index);
            int expectedRun = sizes.PageSize / Gcd(sizes.PageSize, elem) * elem;
            Assert.Equal(expectedRun, chunk.CalculateRunSize(index));
            int capacity = expectedRun / elem;
            var handles = new List<long>();
            var offsets = new HashSet<int>();
            for (int i = 0; i <= capacity; i++)
            {
                long h = chunk.AllocateSmall(index, out PoolChunk owner);
                Assert.Same(chunk, owner);
                Assert.True(h >= 0);
                handles.Add(h);
                int offset = PoolChunk.RunOffset(h) * sizes.PageSize + PoolChunk.BitmapIdx(h) * elem;
                Assert.True(offsets.Add(offset));
                Assert.InRange(offset + elem, 1, chunk.ChunkSize);
            }
            int[] sorted = offsets.Order().ToArray();
            for (int i = 1; i < sorted.Length; i++)
                Assert.True(sorted[i - 1] + elem <= sorted[i]);
            Assert.Equal(chunk.ChunkSize - 2 * expectedRun, chunk.FreeBytes);
            PoolSubpage first = chunk.SubpageAt(0);
            PoolSubpage second = chunk.SubpageAt(PoolChunk.RunOffset(handles[^1]));
            foreach (long h in handles)
                chunk.FreeSmall(h);
            int cachedOffset = capacity == 1 ? 0 : expectedRun / sizes.PageSize;
            int releasedOffset = capacity == 1 ? expectedRun / sizes.PageSize : 0;
            Assert.Null(chunk.SubpageAt(releasedOffset));
            PoolSubpage released = capacity == 1 ? second : first;
            Assert.False(released.IsDoNotDestroy);
            Assert.Equal($"({releasedOffset}: not in use)", released.ToString());
            Assert.Equal(chunk.ChunkSize - expectedRun, chunk.FreeBytes);
            long reused = chunk.AllocateSmall(index, out _);
            Assert.Equal(cachedOffset, PoolChunk.RunOffset(reused));
            Assert.Equal(0, PoolChunk.BitmapIdx(reused));
            chunk.FreeSmall(reused);
        }
    }

    [Fact]
    public void OriginalNormalPoolSubpageReleaseReturnsSingleElementRuns()
    {
        // 16 < elemSize <= 7168 or 8192 < elemSize <= 28672, 1 < subpage.maxNumElems <= 256
        // 7168 <= elemSize <= 8192, subpage.maxNumElems == 1
        SizeClasses sizes = Sizes(13, 11);
        var chunk = new PoolChunk(sizes);
        int index = sizes.Size2SizeIdx(8192);
        long[] handles = new long[1024];
        for (int i = 0; i < handles.Length; i++)
            handles[i] = chunk.AllocateSmall(index, out _);
        int before = chunk.FreeBytes;
        foreach (long handle in handles)
            chunk.FreeSmall(handle);
        Assert.True(before < chunk.FreeBytes);
        Assert.Equal(chunk.ChunkSize - 8192, chunk.FreeBytes);
        Assert.Equal(1, chunk.SubpageAt(0).MaxNumElements);
        Assert.Equal(1, chunk.SubpageAt(0).NumAvailable);
    }

    [Fact]
    public void SharedHeadsReturnTheActualOwnerAndKeepOnlyOneEmptyRunAcrossChunks()
    {
        SizeClasses sizes = Sizes();
        PoolSubpage[] heads = PoolSubpage.CreatePoolHeads(sizes);
        var first = new PoolChunk(sizes, heads);
        var second = new PoolChunk(sizes, heads);
        int index = sizes.Size2SizeIdx(sizes.PageSize);
        long a = first.AllocateSmall(index, out PoolChunk owner);
        Assert.Same(first, owner);
        long b = second.AllocateSmall(index, out owner);
        Assert.Same(second, owner);
        first.FreeSmall(a);
        second.FreeSmall(b);
        Assert.Equal(first.ChunkSize - sizes.PageSize, first.FreeBytes);
        Assert.Equal(second.ChunkSize, second.FreeBytes);
        long reused = second.AllocateSmall(index, out owner);
        Assert.Same(first, owner);
        Assert.Equal(a, reused);
        owner.FreeSmall(reused);
        heads[index] = null; // Mutating the caller array cannot replace the chunk's captured heads.
        Assert.Equal(a, second.AllocateSmall(index, out owner));
        owner.FreeSmall(a);
    }

    [Fact]
    public void MostRecentFreedSlotIsPreferredThenBitmapSearchChoosesLowestHole()
    {
        SizeClasses sizes = Sizes();
        var chunk = new PoolChunk(sizes);
        int index = sizes.Size2SizeIdx(64);
        long[] h = Enumerable.Range(0, 64).Select(i => chunk.AllocateSmall(index, out _)).ToArray();
        chunk.FreeSmall(h[5]);
        chunk.FreeSmall(h[63]);
        chunk.FreeSmall(h[31]);
        foreach (int expected in new[] { 31, 5, 63 })
        {
            long reused = chunk.AllocateSmall(index, out _);
            Assert.Equal(expected, PoolChunk.BitmapIdx(reused));
            h[expected] = reused;
        }
        foreach (long handle in h)
            chunk.FreeSmall(handle);
    }

    [Fact]
    public void RunExhaustionAndLcmLargerThanChunkDoNotPublishSubpages()
    {
        SizeClasses sizes = Sizes(order: 0);
        var chunk = new PoolChunk(sizes);
        int index = sizes.Size2SizeIdx(48);
        Assert.Equal(-1, chunk.CalculateRunSize(index));
        Assert.Equal(-1L, chunk.AllocateSmall(index, out PoolChunk owner));
        Assert.Null(owner);
        Assert.Null(chunk.SubpageAt(0));
        Assert.Equal(chunk.ChunkSize, chunk.FreeBytes);
        long whole = chunk.AllocateRun(chunk.ChunkSize);
        Assert.Equal(-1L, chunk.AllocateSmall(0, out owner));
        Assert.Null(owner);
        Assert.Null(chunk.SubpageAt(0));
        chunk.FreeRun(whole);
        var hugePage = new PoolChunk(Sizes(30, 0));
        Assert.Equal(-1L, hugePage.AllocateSmall(2, out owner));
        Assert.Null(owner);
    }

    [Fact]
    public void HeadMetricsAndMalformedHandlesUseExplicitClrBounds()
    {
        SizeClasses sizes = Sizes();
        PoolSubpage[] heads = PoolSubpage.CreatePoolHeads(sizes);
        Assert.Equal(0, heads[0].MaxNumElements);
        Assert.Equal(0, heads[0].NumAvailable);
        Assert.Equal(-1, heads[0].ElementSize);
        Assert.Equal(0, heads[0].PageSize);
        Assert.True(heads[0].IsDoNotDestroy);
        Assert.Throws<ArgumentException>(() => new PoolChunk(sizes, Array.Empty<PoolSubpage>()));
        Assert.Throws<ArgumentException>(() => new PoolChunk(Sizes(), heads));
        var chunk = new PoolChunk(sizes);
        foreach (int index in new[] { -1, sizes.SmallMaxSizeIdx + 1 })
        { Assert.Throws<ArgumentOutOfRangeException>(() => chunk.AllocateSmall(index, out _)); Assert.Throws<ArgumentOutOfRangeException>(() => chunk.CalculateRunSize(index)); }
        foreach (long handle in new[] { -1L, 0, 1L << 33, (64L << 49) | (1L << 34) | (3L << 32) })
            Assert.Throws<ArgumentOutOfRangeException>(() => chunk.FreeSmall(handle));
        long valid = chunk.AllocateSmall(0, out _);
        int before = chunk.FreeBytes;
        Assert.Throws<ArgumentOutOfRangeException>(() => chunk.FreeSmall(valid | 256L));
        Assert.Throws<InvalidOperationException>(() => chunk.FreeSmall(valid | (2L << 34)));
        Assert.Equal(before, chunk.FreeBytes);
        Assert.Equal(255, chunk.SubpageAt(0).NumAvailable);
        chunk.FreeSmall(valid);
        Assert.Throws<InvalidOperationException>(() => chunk.FreeSmall(valid));
        Assert.Equal(256, chunk.SubpageAt(0).NumAvailable);
    }

    [Fact]
    public async Task SharedHeadLockPreventsSlotOverlapAndDeadlockAcrossChunks()
    {
        SizeClasses sizes = Sizes();
        PoolSubpage[] heads = PoolSubpage.CreatePoolHeads(sizes);
        PoolChunk[] chunks = Enumerable.Range(0, 3).Select(_ => new PoolChunk(sizes, heads)).ToArray();
        var active = new HashSet<(PoolChunk, long)>();
        var gate = new object();
        Task[] workers = Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (int i = 0; i < 300; i++)
            {
                int index = sizes.Size2SizeIdx(16 << (i % 7));
                long handle = chunks[worker % chunks.Length].AllocateSmall(index, out PoolChunk owner);
                Assert.True(handle >= 0);
                Assert.NotNull(owner);
                lock (gate)
                    Assert.True(active.Add((owner, handle)));
                Assert.InRange(owner.SubpageAt(PoolChunk.RunOffset(handle)).NumAvailable, 0, 4096 / 16);
                lock (gate)
                { owner.FreeSmall(handle); Assert.True(active.Remove((owner, handle))); }
            }
        })).ToArray();
        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        Assert.Empty(active);
        int reserved = 0;
        foreach (PoolSubpage head in heads)
            lock (head.SyncRoot)
                if (head.Next != head)
                {
                    Assert.Same(head, head.Next.Next);
                    Assert.Equal(head.Next.MaxNumElements, head.Next.NumAvailable);
                    reserved += head.Next.RunSize;
                }
        Assert.Equal(chunks.Sum(c => c.ChunkSize) - reserved, chunks.Sum(c => c.FreeBytes));
    }
}
