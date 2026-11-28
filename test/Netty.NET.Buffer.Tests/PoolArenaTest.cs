/*
 * Copyright 2012 The Netty Project
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

namespace Netty.NET.Buffer.Tests;

// CLR: the six size-class tests build `new PoolArena.DirectArena(null, sc)` only to read its sizeClass field. The CLR
// PoolArena constructor rejects a null parent and validates chunk geometry before any storage exists
// (src/Netty.NET.Buffer/PoolArena.cs: ThrowIfNull(parent), `_ = new PoolChunk(sizeClass)`), and the pinned
// pageSize 8192/pageShifts 11 pair is not a consistent page geometry, so these tests query that same SizeClasses
// instance directly; every size/page computation and input is unchanged.
public class PoolArenaTest
{
    private const int PAGE_SIZE = 8192;
    private const int PAGE_SHIFTS = 11;
    //chunkSize = pageSize * (2 ^ pageShifts)
    private const int CHUNK_SIZE = 16777216;

    [Fact]
    public void TestNormalizeCapacity()
    {
        SizeClasses sc = new SizeClasses(PAGE_SIZE, PAGE_SHIFTS, CHUNK_SIZE, 0);
        int[] reqCapacities = { 0, 15, 510, 1024, 1023, 1025 };
        int[] expectedResult = { 16, 16, 512, 1024, 1024, 1280 };

        for (int i = 0; i < reqCapacities.Length; i++)
        {
            Assert.Equal(expectedResult[i],
                    sc.SizeIdx2Size(sc.Size2SizeIdx(reqCapacities[i])));
        }
    }

    [Fact]
    public void TestNormalizeAlignedCapacity()
    {
        SizeClasses sc = new SizeClasses(PAGE_SIZE, PAGE_SHIFTS, CHUNK_SIZE, 64);
        int[] reqCapacities = { 0, 15, 510, 1024, 1023, 1025 };
        int[] expectedResult = { 64, 64, 512, 1024, 1024, 1280 };

        for (int i = 0; i < reqCapacities.Length; i++)
        {
            Assert.Equal(expectedResult[i],
                    sc.SizeIdx2Size(sc.Size2SizeIdx(reqCapacities[i])));
        }
    }

    [Fact]
    public void TestSize2SizeIdx()
    {
        SizeClasses sc = new SizeClasses(PAGE_SIZE, PAGE_SHIFTS, CHUNK_SIZE, 0);

        for (int sz = 0; sz <= CHUNK_SIZE; sz++)
        {
            int sizeIdx = sc.Size2SizeIdx(sz);
            Assert.True(sz <= sc.SizeIdx2Size(sizeIdx));

            if (sizeIdx > 0)
            {
                Assert.True(sz > sc.SizeIdx2Size(sizeIdx - 1));
            }
        }
    }

    [Fact]
    public void TestPages2PageIdx()
    {
        int pageShifts = PAGE_SHIFTS;
        SizeClasses sc = new SizeClasses(PAGE_SIZE, PAGE_SHIFTS, CHUNK_SIZE, 0);

        int maxPages = CHUNK_SIZE >> pageShifts;

        for (int pages = 1; pages <= maxPages; pages++)
        {
            int pageIdxFloor = sc.Pages2PageIdxFloor(pages);
            Assert.True(pages << pageShifts >= sc.PageIdx2Size(pageIdxFloor));

            if (pageIdxFloor > 0 && pages < maxPages)
            {
                Assert.True(pages << pageShifts < sc.PageIdx2Size(pageIdxFloor + 1));
            }

            int pageIdxCeiling = sc.Pages2PageIdx(pages);
            Assert.True(pages << pageShifts <= sc.PageIdx2Size(pageIdxCeiling));

            if (pageIdxCeiling > 0)
            {
                Assert.True(pages << pageShifts > sc.PageIdx2Size(pageIdxCeiling - 1));
            }
        }
    }

    [Fact]
    public void TestSizeIdx2size()
    {
        SizeClasses sc = new SizeClasses(PAGE_SIZE, PAGE_SHIFTS, CHUNK_SIZE, 0);

        for (int i = 0; i < sc.NSizes; i++)
        {
            Assert.Equal(sc.SizeIdx2SizeCompute(i), sc.SizeIdx2Size(i));
        }
    }

    [Fact]
    public void TestPageIdx2size()
    {
        SizeClasses sc = new SizeClasses(PAGE_SIZE, PAGE_SHIFTS, CHUNK_SIZE, 0);

        for (int i = 0; i < sc.NPSizes; i++)
        {
            Assert.Equal(sc.PageIdx2SizeCompute(i), sc.PageIdx2Size(i));
        }
    }

    [Fact]
    public void TestAllocationCounter()
    {
        // CLR: the source fixture calls the @Deprecated tinyCacheSize constructor; the CLR keeps it as an
        // [Obsolete] overload (src/Netty.NET.Buffer/PooledByteBufAllocator.cs), so the call is kept verbatim.
#pragma warning disable CS0618
        // CLR: PooledByteBufAllocator is IDisposable (src/Netty.NET.Buffer/PooledByteBufAllocator.cs); disposal after
        // the original assertions returns the native chunk the JVM fixture leaves to its Cleaner.
        using PooledByteBufAllocator allocator = new PooledByteBufAllocator(
                true,   // preferDirect
                0,      // nHeapArena
                1,      // nDirectArena
                8192,   // pageSize
                11,     // maxOrder
                0,      // tinyCacheSize
                0,      // smallCacheSize
                0,      // normalCacheSize
                true    // useCacheForAllThreads
                );
#pragma warning restore CS0618

        // create small buffer
        ByteBuf b1 = allocator.DirectBuffer(800);
        // create normal buffer
        ByteBuf b2 = allocator.DirectBuffer(8192 * 5);

        Assert.NotNull(b1);
        Assert.NotNull(b2);

        // then release buffer to deallocated memory while threadlocal cache has been disabled
        // allocations counter value must equals deallocations counter value
        Assert.True(b1.Release());
        Assert.True(b2.Release());

        // CLR: PooledByteBufAllocator.directArenas() is @deprecated "use PooledByteBufAllocatorMetric#directArenas()";
        // the CLR exposes only that replacement publicly (PooledByteBufAllocatorMetric.DirectArenas).
        Assert.True(allocator.Metric.DirectArenas.Count >= 1);
        IPoolArenaMetric metric = allocator.Metric.DirectArenas[0];

        Assert.Equal(2, metric.NumDeallocations);
        Assert.Equal(2, metric.NumAllocations);

        Assert.Equal(1, metric.NumSmallDeallocations);
        Assert.Equal(1, metric.NumSmallAllocations);
        Assert.Equal(1, metric.NumNormalDeallocations);
        Assert.Equal(1, metric.NumNormalAllocations);

        Assert.Equal(1, metric.NumChunkAllocations);
        Assert.Equal(0, metric.NumChunkDeallocations);
        Assert.Equal(1, metric.NumActiveChunks);
    }

    [Fact]
    public void TestDirectArenaMemoryCopy()
    {
        ByteBuf src = PooledByteBufAllocator.Default.DirectBuffer(512);
        ByteBuf dst = PooledByteBufAllocator.Default.DirectBuffer(512);

        PooledByteBuf pooledSrc = UnwrapIfNeeded(src);
        PooledByteBuf pooledDst = UnwrapIfNeeded(dst);

        // This causes the internal reused ByteBuffer duplicate limit to be set to 128
        pooledDst.WriteBytes(new ReadOnlySpan<byte>(new byte[128]));
        // Ensure internal ByteBuffer duplicate limit is properly reset (used in memoryCopy non-Unsafe case)
        // CLR: PoolArena.memoryCopy(T src, int srcOffset, PooledByteBuf<T> dst, int length) has no CLR member: the
        // reallocation copy is inlined in the PooledByteBuf.Capacity setter as Memory<byte>.Span.CopyTo
        // (src/Netty.NET.Buffer/PooledByteBuf.cs) and no NIO duplicate with a cached position/limit exists. The
        // same transfer (all 512 bytes of the source region from offset 0 into the destination region at its
        // offset) runs through the public absolute copy right after the 128-byte write.
        pooledSrc.GetBytes(0, pooledDst, 0, 512);

        src.Release();
        dst.Release();
    }

    private PooledByteBuf UnwrapIfNeeded(ByteBuf buf)
    {
        return (PooledByteBuf)(buf is PooledByteBuf ? buf : buf.Unwrap());
    }
}
