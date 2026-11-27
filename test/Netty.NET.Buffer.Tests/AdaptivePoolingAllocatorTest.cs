/*
 * Copyright 2024 The Netty Project
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using static Netty.NET.Buffer.AdaptivePoolingAllocator;

namespace Netty.NET.Buffer.Tests;

public class AdaptivePoolingAllocatorTest
{
    [Fact]
    public void SizeClassComputations()
    {
        int[] sizeClasses = AdaptivePoolingAllocator.GetSizeClasses();
        for (int sizeClassIndex = 0; sizeClassIndex < sizeClasses.Length; sizeClassIndex++)
        {
            int previousSizeIncluded = sizeClassIndex == 0 ? 0 : sizeClasses[sizeClassIndex - 1] + 1;
            AssertSizeClassOf(sizeClassIndex, previousSizeIncluded, sizeClasses[sizeClassIndex]);
        }
        // beyond the last size class, we return the size class array's length
        AssertSizeClassOf(sizeClasses.Length, sizeClasses[^1] + 1, sizeClasses[^1] + 1);
    }

    private static void AssertSizeClassOf(int expectedSizeClass, int previousSizeIncluded, int maxSizeIncluded)
    {
        for (int size = previousSizeIncluded; size <= maxSizeIncluded; size++)
            Assert.Equal(expectedSizeClass, AdaptivePoolingAllocator.SizeClassIndexOf(size));
    }

    private static readonly int[] Segments = [32, 64, 128, 256, 512, 640, 1024, 1152, 2048, 2304, 4096, 4352, 8192, 8704, 16384, 16896];
    private static readonly int[] ChunkSizes = [131072, 131072, 131072, 131072, 131072, 131072, 131072, 131072, 131072, 131072, 131072, 139264, 262144, 278528, 524288, 540672];
    public static IEnumerable<object[]> FixedClassCases()
    {
        for (int index = 0; index < Segments.Length; index++)
            foreach (bool lowMemory in new[] { false, true }) yield return [index, lowMemory];
    }

    // Allocates through the pool and checks whether the reservation came from a pooled chunk.
    private static void AssertRouting(AdaptivePoolingAllocator pool, int size, bool pooled)
    {
        long before = pool.UsedMemory;
        var buffer = (AdaptiveByteBuf)pool.Allocate(size, size);
        try
        {
            Assert.Equal(pooled, buffer.Allocation.IsPooled);
            if (!pooled) Assert.Equal(size, buffer.Allocation.Length);
        }
        finally { buffer.Release(); }
        if (!pooled) Assert.Equal(before, pool.UsedMemory);
    }

    [Theory]
    [MemberData(nameof(FixedClassCases))]
    public void FixedControllersPreserveOriginalFastCapacityAndChunkGeometry(int index, bool lowMemory)
    {
        using var pool = TestChunkAllocator.Pool(lowMemory);
        int requested = index == 0 ? 0 : Segments[index - 1] + 1;
        Assert.True(requested <= pool.MaxPooledBufSize);
        Assert.Equal(index, AdaptivePoolingAllocator.SizeClassIndexOf(requested));
        Magazine magazine = pool.SizeClassedMagazineGroups[index].Magazines[0];
        SizeClassChunkController controller = TestChunkAllocator.SizeClassController(pool, index);
        Assert.Same(controller, magazine.ChunkController);
        Assert.Equal(Segments[index], controller.ComputeBufferCapacity(requested, int.MaxValue, false));
        Assert.Equal(requested, controller.ComputeBufferCapacity(requested, requested, false));
        Assert.Equal(Segments[index], controller.SegmentSize);
        Assert.Equal(ChunkSizes[index], controller.ChunkSize);
        Chunk chunk = controller.NewChunkAllocation(requested, magazine);
        try
        {
            Assert.IsType<SizeClassedChunk>(chunk);
            Assert.Equal(ChunkSizes[index], chunk.Capacity);
            Assert.Equal(ChunkSizes[index], pool.UsedMemory);
        }
        finally { chunk.Dispose(); }
        Assert.Equal(0, pool.UsedMemory);
        Assert.True(ChunkSizes[index] / Segments[index] >= 32);
    }

    [Fact]
    public void SizeClassSnapshotsAreIndependentAndLargeInputCannotOverflowTheLookup()
    {
        int[] first = AdaptivePoolingAllocator.GetSizeClasses(), second = AdaptivePoolingAllocator.GetSizeClasses();
        Assert.Equal(Segments, first);
        Assert.NotSame(first, second);
        first[0] = 999;
        Assert.Equal(32, second[0]);
        Assert.Equal(32, AdaptivePoolingAllocator.GetSizeClasses()[0]);
        Assert.Equal(16, AdaptivePoolingAllocator.SizeClassIndexOf(int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => AdaptivePoolingAllocator.SizeClassIndexOf(-1));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LowMemoryAndMaximumPoolCutoffsSelectOriginalDedicatedFallback(bool lowMemory)
    {
        using var pool = TestChunkAllocator.Pool(lowMemory);
        Assert.Equal(lowMemory ? 2097152 : 8388608, pool.MaxChunkSize);
        Assert.Equal(lowMemory ? 262144 : 1048576, pool.MaxPooledBufSize);
        AssertRouting(pool, 16896, true);
        AssertRouting(pool, 16897, !lowMemory);
        AssertRouting(pool, pool.MaxPooledBufSize, !lowMemory);
        // A dedicated fallback chunk reserves exactly the requested size.
        AssertRouting(pool, pool.MaxPooledBufSize + 1, false);
        Assert.Equal(AdaptivePoolingAllocator.GetSizeClasses().Length, AdaptivePoolingAllocator.SizeClassIndexOf(int.MaxValue));
    }

    [Fact]
    public void BuddyControllersRememberLargerChunksWithoutClampingTheRawReservation()
    {
        using var pool = TestChunkAllocator.Pool();
        BuddyChunkController controller = TestChunkAllocator.BuddyController(pool);
        Magazine magazine = pool.LargeBufferMagazineGroup.Magazines[0];
        // 1048577 is above the pooled maximum: it is served by a dedicated chunk and is not remembered.
        int[] requests = [16897, 32768, 32769, 65536, 262144, 1048576, 1048577, 16897];
        int[] chunks = [262144, 262144, 524288, 524288, 2097152, 8388608, 1048577, 8388608];
        int[] reservations = [32768, 32768, 65536, 65536, 262144, 1048576, 1048577, 32768];
        for (int i = 0; i < requests.Length; i++)
        {
            if (requests[i] > pool.MaxPooledBufSize)
            {
                var buffer = (AdaptiveByteBuf)pool.Allocate(requests[i], requests[i]);
                try
                {
                    Assert.False(buffer.Allocation.IsPooled); Assert.Equal(reservations[i], buffer.Allocation.Length);
                    Assert.Equal(chunks[i], pool.UsedMemory);
                }
                finally { buffer.Release(); }
                continue;
            }
            Chunk chunk = controller.NewChunkAllocation(requests[i], magazine);
            try { Assert.IsType<BuddyChunk>(chunk); Assert.Equal(chunks[i], chunk.Capacity); }
            finally { chunk.Dispose(); }
            Assert.Equal(reservations[i], controller.ComputeBufferCapacity(requests[i], requests[i], false));
        }
        Assert.Equal(0, pool.UsedMemory);
    }

    [Fact]
    public void LowMemorySkipsBuddyPoolingAndDoesNotAffectOtherPolicyInstances()
    {
        using var low = TestChunkAllocator.Pool(true);
        using var normal = TestChunkAllocator.Pool(false);
        var buffer = (AdaptiveByteBuf)low.Allocate(32769, 40000);
        try
        {
            Assert.False(buffer.Allocation.IsPooled);
            Assert.Equal(32769, buffer.Allocation.Length);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, low.UsedMemory);
        Chunk chunk = TestChunkAllocator.BuddyController(normal).NewChunkAllocation(32769, normal.LargeBufferMagazineGroup.Magazines[0]);
        try
        {
            Assert.Equal(524288, chunk.Capacity);
        }
        finally { chunk.Dispose(); }
        using var separate = TestChunkAllocator.Pool(false);
        chunk = TestChunkAllocator.BuddyController(separate).NewChunkAllocation(16897, separate.LargeBufferMagazineGroup.Magazines[0]);
        try
        {
            Assert.Equal(262144, chunk.Capacity);
        }
        finally { chunk.Dispose(); }
    }

    [Fact]
    public void InvalidRequestsFailBeforeMutatingRememberedChunkSizing()
    {
        using var p = TestChunkAllocator.Pool(false);
        BuddyChunkController controller = TestChunkAllocator.BuddyController(p);
        Magazine magazine = p.LargeBufferMagazineGroup.Magazines[0];
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.NewChunkAllocation(-1, magazine));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.ComputeBufferCapacity(-1, 100, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.ComputeBufferCapacity(100, 99, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.ComputeBufferCapacity(0, -1, false));
        Assert.Equal(0, p.UsedMemory);
        Chunk chunk = controller.NewChunkAllocation(16897, magazine);
        try { Assert.Equal(262144, chunk.Capacity); }
        finally { chunk.Dispose(); }
    }

    private sealed class RecordedChunkSize(int size) : Exception
    {
        internal int Size { get; } = size;
    }

    [Fact]
    public void ConcurrentControllerDecisionsStayWithinTheirOriginalCapacityBounds()
    {
        // The chunk allocator records the controller's decision and refuses the storage, so
        // the concurrent decisions are checked without allocating every chunk.
        using var p = TestChunkAllocator.Pool(false, (size, _) => throw new RecordedChunkSize(size));
        BuddyChunkController controller = TestChunkAllocator.BuddyController(p);
        Magazine magazine = p.LargeBufferMagazineGroup.Magazines[0];
        int[] requests = [16897, 32769, 131073, 262145, 1048576];
        var decisions = new ConcurrentBag<(int Size, int Chunk)>();
        Parallel.For(0, 128, i =>
        {
            int size = requests[i % requests.Length];
            var recorded = Assert.Throws<RecordedChunkSize>(() => controller.NewChunkAllocation(size, magazine));
            decisions.Add((size, recorded.Size));
        });
        Assert.Equal(128, decisions.Count);
        foreach (var (size, chunk) in decisions)
        {
            Assert.True(BitOperations.IsPow2(chunk)); Assert.InRange(chunk, size * 8, 8388608);
        }
        Assert.Equal(8388608, Assert.Throws<RecordedChunkSize>(() => controller.NewChunkAllocation(1048576, magazine)).Size);
        Assert.Equal(0, p.UsedMemory);
    }
}
