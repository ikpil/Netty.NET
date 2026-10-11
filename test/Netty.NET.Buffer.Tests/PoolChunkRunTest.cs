/*
 * Copyright 2020 The Netty Project
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

public class PoolChunkRunTest
{
    private static PoolChunk Create(int order = 4, int shifts = 12, int alignment = 0)
        => new(new SizeClasses(1 << shifts, shifts, 1 << (shifts + order), alignment));

    public static IEnumerable<object[]> Configurations()
    {
        foreach (int shifts in new[] { 12, 13, 16 })
            foreach (int order in new[] { 0, 4, 8, 14 })
                foreach (int alignment in new[] { 0, 64 })
                    yield return new object[] { shifts, order, alignment };
    }

    [Fact]
    public void WholeChunkExhaustionAndReuseRetainOriginalHandleLayout()
    {
        PoolChunk chunk = Create();
        IPoolChunkMetric metric = chunk;
        Assert.Equal(65536, metric.ChunkSize);
        Assert.Equal(metric.ChunkSize, metric.FreeBytes);
        Assert.Equal(0, metric.Usage);
        long handle = chunk.AllocateRun(metric.ChunkSize);
        Assert.Equal((16L << 34) | (1L << 33), handle);
        Assert.True(PoolChunk.IsRun(handle));
        Assert.True(PoolChunk.IsUsed(handle));
        Assert.False(PoolChunk.IsSubpage(handle));
        Assert.Equal(0, PoolChunk.RunOffset(handle));
        Assert.Equal(16, PoolChunk.RunPages(handle));
        Assert.Equal(metric.ChunkSize, PoolChunk.RunSize(12, handle));
        Assert.Equal(0, PoolChunk.BitmapIdx(handle));
        Assert.Equal(100, metric.Usage);
        Assert.Equal(0, metric.FreeBytes);
        Assert.Equal(-1L, chunk.AllocateRun(4096));
        Assert.Equal(0, metric.FreeBytes);
        chunk.FreeRun(handle);
        Assert.Equal(0, metric.Usage);
        Assert.Equal(metric.ChunkSize, metric.FreeBytes);
        Assert.Equal(handle, chunk.AllocateRun(metric.ChunkSize));
        chunk.FreeRun(handle);
    }

    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(0, 2, 1)]
    [InlineData(1, 0, 2)]
    [InlineData(1, 2, 0)]
    [InlineData(2, 0, 1)]
    [InlineData(2, 1, 0)]
    public void FreeOrderCollapsesBothNeighboursIncludingSinglePageBoundaries(int first, int second, int third)
    {
        PoolChunk chunk = Create(2);
        long[] handles = Enumerable.Range(0, 3).Select(_ => chunk.AllocateRun(4096)).ToArray();
        long last = chunk.AllocateRun(4096);
        chunk.FreeRun(handles[first]);
        chunk.FreeRun(handles[second]);
        chunk.FreeRun(handles[third]);
        Assert.Equal(3 * 4096, chunk.FreeBytes);
        long merged = chunk.AllocateRun(3 * 4096);
        Assert.Equal(0, PoolChunk.RunOffset(merged));
        Assert.Equal(3, PoolChunk.RunPages(merged));
        chunk.FreeRun(merged);
        chunk.FreeRun(last);
        long whole = chunk.AllocateRun(chunk.ChunkSize);
        Assert.Equal(0, PoolChunk.RunOffset(whole));
        chunk.FreeRun(whole);
    }

    [Fact]
    public void LowestEligibleBucketPrecedesLowestOffset()
    {
        PoolChunk chunk = Create();
        long large = chunk.AllocateRun(4 * 4096);
        long barrier = chunk.AllocateRun(4096);
        long small = chunk.AllocateRun(2 * 4096);
        long rest = chunk.AllocateRun(8 * 4096);
        long last = chunk.AllocateRun(4096);
        chunk.FreeRun(large);
        chunk.FreeRun(small);
        long chosen = chunk.AllocateRun(2 * 4096);
        Assert.Equal(5, PoolChunk.RunOffset(chosen));
        chunk.FreeRun(chosen);
        chunk.FreeRun(barrier);
        chunk.FreeRun(rest);
        chunk.FreeRun(last);
        Assert.Equal(chunk.ChunkSize, chunk.FreeBytes);
    }

    [Fact]
    public void FreeRunFloorBucketAndRequestCeilingRetainOriginalConservativeSelection()
    {
        PoolChunk chunk = Create();
        long prefix = chunk.AllocateRun(7 * 4096);
        // A 9-page tail goes in the 8-page bucket; a 9-page request starts at the 10-page bucket.
        Assert.Equal(9 * 4096, chunk.FreeBytes);
        Assert.Equal(-1L, chunk.AllocateRun(9 * 4096));
        Assert.Equal(9 * 4096, chunk.FreeBytes);
        long tail = chunk.AllocateRun(8 * 4096);
        Assert.Equal(7, PoolChunk.RunOffset(tail));
        chunk.FreeRun(prefix);
        chunk.FreeRun(tail);
        Assert.Equal(chunk.ChunkSize, chunk.FreeBytes);
    }

    [Fact]
    public void EqualBucketUsesLowestOffsetAfterOutOfOrderFrees()
    {
        PoolChunk chunk = Create();
        long[] handles = Enumerable.Range(0, 16).Select(_ => chunk.AllocateRun(4096)).ToArray();
        foreach (int i in new[] { 10, 4, 8, 2 })
            chunk.FreeRun(handles[i]);
        foreach (int expected in new[] { 2, 4, 8, 10 })
        {
            long handle = chunk.AllocateRun(4096);
            Assert.Equal(expected, PoolChunk.RunOffset(handle));
            handles[expected] = handle;
        }
        foreach (long handle in handles)
            chunk.FreeRun(handle);
        Assert.Equal(chunk.ChunkSize, chunk.FreeBytes);
    }

    [Fact]
    public void AdjacentRunRemovalUpdatesNonRootPriorityQueueEntries()
    {
        PoolChunk chunk = Create(5);
        long[] h = Enumerable.Range(0, 32).Select(_ => chunk.AllocateRun(4096)).ToArray();
        foreach (int i in new[] { 2, 8, 14, 20, 26 })
            chunk.FreeRun(h[i]);
        // Freeing 15 must remove offset 14 even though offset 2 is the minimum queue entry.
        chunk.FreeRun(h[15]);
        long merged = chunk.AllocateRun(2 * 4096);
        Assert.Equal(14, PoolChunk.RunOffset(merged));
        var freed = new HashSet<int> { 2, 8, 14, 15, 20, 26 };
        for (int i = 0; i < h.Length; i++)
        if (!freed.Contains(i))
            chunk.FreeRun(h[i]);
        chunk.FreeRun(merged);
        Assert.Equal(chunk.ChunkSize, chunk.FreeBytes);
    }

    [Fact]
    public void FragmentationCanFailWithoutChangingMetrics()
    {
        PoolChunk chunk = Create(3);
        long[] h = Enumerable.Range(0, 8).Select(_ => chunk.AllocateRun(4096)).ToArray();
        foreach (int i in new[] { 0, 2, 4, 6 })
            chunk.FreeRun(h[i]);
        Assert.Equal(4 * 4096, chunk.FreeBytes);
        Assert.Equal(-1L, chunk.AllocateRun(2 * 4096));
        Assert.Equal(50, chunk.Usage);
        foreach (int i in new[] { 1, 3, 5, 7 })
            chunk.FreeRun(h[i]);
        Assert.Equal(chunk.ChunkSize, chunk.FreeBytes);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 99)]
    [InlineData(3, 99)]
    [InlineData(128, 50)]
    [InlineData(256, 0)]
    public void UsageRetainsOriginalRounding(int freePages, int expectedUsage)
    {
        PoolChunk chunk = Create(8);
        if (freePages < 256)
            chunk.AllocateRun((256 - freePages) * 4096);
        Assert.Equal(expectedUsage, chunk.Usage);
        Assert.Equal(freePages * 4096, chunk.FreeBytes);
    }

    private static List<int> PageClasses(int total)
    {
        var values = new List<int>();
        for (int n = 1; n <= Math.Min(4, total); n++)
            values.Add(n);
        for (int basis = 4; basis < total; basis *= 2)
            for (int delta = 1; delta <= 4; delta++)
                values.Add(basis + basis / 4 * delta);
        return values;
    }

    private static int ChooseRun(bool[] occupied, int request, List<int> classes)
    {
        int ceiling = classes.FindIndex(n => n >= request);
        int bestOffset = -1, bestBucket = int.MaxValue;
        for (int i = 0; i < occupied.Length;)
        {
            if (occupied[i])
            { i++; continue; }
            int start = i;
            while (i < occupied.Length && !occupied[i])
                i++;
            int floor = classes.FindLastIndex(n => n <= i - start);
            if (floor >= ceiling && floor < bestBucket)
            { bestBucket = floor; bestOffset = start; }
        }
        return bestOffset;
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void MixedAllocationAndFreeAgreeWithIndependentPageOccupancyModel(int shifts, int order, int alignment)
    {
        PoolChunk chunk = Create(order, shifts, alignment);
        var occupied = new bool[1 << order];
        List<int> classes = PageClasses(occupied.Length);
        var live = new List<(long Handle, int Offset, int Pages)>();
        var random = new Random(20261012 + shifts * 31 + order * 7 + alignment);
        for (int step = 0; step < 600; step++)
        {
            if (live.Count != 0 && random.Next(100) < 45)
            {
                int i = random.Next(live.Count);
                var item = live[i];
                chunk.FreeRun(item.Handle);
                for (int p = 0; p < item.Pages; p++)
                    occupied[item.Offset + p] = false;
                live.RemoveAt(i);
            }
            else
            {
                int count = step % 13 == 0 ? occupied.Length : random.Next(1, Math.Min(occupied.Length, 32) + 1);
                int expectedOffset = ChooseRun(occupied, count, classes);
                long handle = chunk.AllocateRun(count << shifts);
                if (expectedOffset < 0)
                    Assert.Equal(-1L, handle);
                else
                {
                    Assert.Equal(expectedOffset, PoolChunk.RunOffset(handle));
                    Assert.Equal(count, PoolChunk.RunPages(handle));
                    Assert.Equal(((long)expectedOffset << 49) | ((long)count << 34) | (1L << 33), handle);
                    for (int p = 0; p < count; p++)
                    { Assert.False(occupied[expectedOffset + p]); occupied[expectedOffset + p] = true; }
                    live.Add((handle, expectedOffset, count));
                }
            }
            int free = occupied.Count(value => !value) << shifts;
            Assert.Equal(free, chunk.FreeBytes);
            int freePercentage = (int)((long)free * 100 / chunk.ChunkSize);
            Assert.Equal(free == 0 ? 100 : freePercentage == 0 ? 99 : 100 - freePercentage, chunk.Usage);
        }
        foreach (var item in live)
            chunk.FreeRun(item.Handle);
        Assert.Equal(chunk.ChunkSize, chunk.FreeBytes);
        long whole = chunk.AllocateRun(chunk.ChunkSize);
        Assert.Equal(0, PoolChunk.RunOffset(whole));
        chunk.FreeRun(whole);
    }

    [Fact]
    public async Task ParallelPageReservationsNeverOverlapAndFullyCoalesce()
    {
        PoolChunk chunk = Create(8);
        var active = new HashSet<int>();
        var gate = new object();
        Task[] workers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 500; i++)
            {
                long handle = chunk.AllocateRun(4096);
                Assert.True(handle >= 0);
                int offset = PoolChunk.RunOffset(handle);
                lock (gate)
                    Assert.True(active.Add(offset));
                Assert.InRange(chunk.FreeBytes, 0, chunk.ChunkSize);
                lock (gate)
                {
                    chunk.FreeRun(handle);
                    Assert.True(active.Remove(offset));
                }
            }
        })).ToArray();
        await Task.WhenAll(workers);
        Assert.Empty(active);
        Assert.Equal(chunk.ChunkSize, chunk.FreeBytes);
        long whole = chunk.AllocateRun(chunk.ChunkSize);
        Assert.True(whole >= 0);
        chunk.FreeRun(whole);
    }

    [Fact]
    public void MaximumPoolOrderKeepsHighOffsetHandlesPositiveInCheckedBuilds()
    {
        PoolChunk chunk = Create(14, 16);
        long prefix = chunk.AllocateRun(chunk.ChunkSize - chunk.PageSize);
        long last = chunk.AllocateRun(chunk.PageSize);
        Assert.True(last > 0);
        Assert.Equal(16383, PoolChunk.RunOffset(last));
        Assert.Equal(1, PoolChunk.RunPages(last));
        Assert.Equal(0, chunk.FreeBytes);
        chunk.FreeRun(prefix);
        chunk.FreeRun(last);
        Assert.Equal(chunk.ChunkSize, chunk.FreeBytes);
    }

    [Fact]
    public void InvalidGeometryAndMalformedRequestsFailBeforeMutation()
    {
        Assert.Throws<ArgumentNullException>(() => new PoolChunk(null));
        Assert.Throws<ArgumentException>(() => new PoolChunk(new SizeClasses(8192, 11, 16777216, 0)));
        Assert.Throws<ArgumentException>(() => new PoolChunk(new SizeClasses(4096, 12, 1 << 27, 0)));
        PoolChunk chunk = Create();
        foreach (int size in new[] { -4096, 0, 1, 4095, 4097, chunk.ChunkSize + 4096 })
            Assert.Throws<ArgumentOutOfRangeException>(() => chunk.AllocateRun(size));
        foreach (long handle in new[] { -1L, 0L, 1L << 33, (1L << 34), (17L << 34) | (1L << 33), (1L << 34) | (1L << 33) | (1L << 32), (1L << 34) | (1L << 33) | 1 })
            Assert.Throws<ArgumentOutOfRangeException>(() => chunk.FreeRun(handle));
        Assert.Equal(chunk.ChunkSize, chunk.FreeBytes);
        long whole = chunk.AllocateRun(chunk.ChunkSize);
        Assert.True(whole >= 0);
        chunk.FreeRun(whole);
    }
}
