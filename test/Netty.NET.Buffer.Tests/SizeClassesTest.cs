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
using System.Collections.Generic;
using System.Linq;

namespace Netty.NET.Buffer.Tests;

public class SizeClassesTest
{
    public static IEnumerable<object[]> Configurations()
    {
        foreach (int pageShifts in new[] { 12, 13, 14 })
            foreach (int order in new[] { 0, 4, 9 })
                foreach (int alignment in new[] { 0, 16, 64, 256, (1 << pageShifts) / 8, order == 0 ? 0 : (1 << pageShifts) / 2, order == 0 ? 0 : 1 << pageShifts }.Distinct())
                    yield return new object[] { pageShifts, order, alignment };
    }

    private static SizeClasses Original(int alignment = 0)
    {
        //chunkSize = pageSize * (2 ^ pageShifts)
        // The original fixture uses pageSize 8192 and pageShifts 11; retain that input verbatim.
        return new SizeClasses(8192, 11, 16777216, alignment);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(64)]
    public void OriginalNormalizeCapacity(int alignment)
    {
        ISizeClassesMetric sizes = Original(alignment);
        int[] requests = { 0, 15, 510, 1024, 1023, 1025 };
        int[] expected = { alignment == 0 ? 16 : 64, alignment == 0 ? 16 : 64, 512, 1024, 1024, 1280 };
        for (int i = 0; i < requests.Length; i++)
            Assert.Equal(expected[i], sizes.SizeIdx2Size(sizes.Size2SizeIdx(requests[i])));
    }

    [Fact]
    public void OriginalSize2SizeIdxCoversEveryByteThroughSixteenMiB()
    {
        SizeClasses sizes = Original();
        for (int size = 0; size <= sizes.ChunkSize; size++)
        {
            int index = sizes.Size2SizeIdx(size);
            if (size > sizes.SizeIdx2Size(index) || index > 0 && size <= sizes.SizeIdx2Size(index - 1))
                Assert.Fail($"Wrong minimal size class at {size}: {index}.");
        }
    }

    [Fact]
    public void OriginalPageCeilingAndFloorCoverEveryPageInTheChunk()
    {
        SizeClasses sizes = Original();
        int maximum = sizes.ChunkSize >> sizes.PageShifts;
        for (int pages = 1; pages <= maximum; pages++)
        {
            long bytes = (long)pages << sizes.PageShifts;
            int floor = sizes.Pages2PageIdxFloor(pages), ceiling = sizes.Pages2PageIdx(pages);
            Assert.True(bytes >= sizes.PageIdx2Size(floor));
            Assert.True(bytes <= sizes.PageIdx2Size(ceiling));
            if (floor > 0 && pages < maximum)
                Assert.True(bytes < sizes.PageIdx2Size(floor + 1));
            if (ceiling > 0)
                Assert.True(bytes > sizes.PageIdx2Size(ceiling - 1));
        }
    }

    [Fact]
    public void OriginalSizeIdx2SizeComputeMatchesEveryTableEntry()
    {
        SizeClasses sizes = Original();
        for (int i = 0; i < sizes.NSizes; i++)
            Assert.Equal(sizes.SizeIdx2SizeCompute(i), sizes.SizeIdx2Size(i));
    }

    [Fact]
    public void OriginalPageIdx2SizeComputeMatchesEveryTableEntry()
    {
        SizeClasses sizes = Original();
        for (int i = 0; i < sizes.NPSizes; i++)
            Assert.Equal(sizes.PageIdx2SizeCompute(i), sizes.PageIdx2Size(i));
    }

    private static List<int> RawClasses(int chunkSize)
    {
        var values = new List<int> { 16, 32, 48, 64 };
        for (int basis = 64; basis < chunkSize; basis *= 2)
            for (int delta = 1; delta <= 4; delta++)
                values.Add(basis + basis / 4 * delta);
        return values;
    }
    private static int Align(int size, int alignment) => alignment == 0 ? size : checked((size + alignment - 1) / alignment * alignment);

    [Theory]
    [MemberData(nameof(Configurations))]
    public void TablesAndBoundaryQueriesAgreeWithIndependentGroupedSizeModel(int pageShifts, int order, int alignment)
    {
        int page = 1 << pageShifts, chunk = page << order;
        var sizes = new SizeClasses(page, pageShifts, chunk, alignment);
        List<int> raw = RawClasses(chunk);
        Assert.Equal(raw.Count, sizes.NSizes);
        Assert.Equal(page, sizes.PageSize);
        Assert.Equal(chunk, sizes.ChunkSize);
        Assert.Equal(pageShifts, sizes.PageShifts);
        Assert.Equal(alignment, sizes.DirectMemoryCacheAlignment);
        for (int i = 0; i < raw.Count; i++)
        {
            Assert.Equal(raw[i], sizes.SizeIdx2SizeCompute(i));
            Assert.Equal(Align(raw[i], alignment), sizes.SizeIdx2Size(i));
            foreach (int request in new[] { raw[i] - 1, raw[i], raw[i] + 1 })
            {
                int aligned = Align(request, alignment), expected = raw.FindIndex(value => value >= aligned);
                if (expected < 0)
                    expected = sizes.NSizes;
                Assert.Equal(expected, sizes.Size2SizeIdx(request));
                if (expected < sizes.NSizes)
                    Assert.Equal(Align(raw[expected], alignment), sizes.NormalizeSize(request));
            }
        }
        List<int> pages = raw.Where(value => value % page == 0).ToList();
        Assert.Equal(pages.Count, sizes.NPSizes);
        for (int i = 0; i < pages.Count; i++)
        {
            Assert.Equal(pages[i], sizes.PageIdx2Size(i));
            Assert.Equal(pages[i], sizes.PageIdx2SizeCompute(i));
            foreach (int count in new[] { Math.Max(1, pages[i] / page - 1), pages[i] / page, pages[i] / page + 1 })
            {
                long bytes = (long)count * page;
                int ceiling = pages.FindIndex(value => value >= bytes), floor = pages.FindLastIndex(value => value <= bytes);
                if (bytes > chunk)
                    ceiling = floor = sizes.NPSizes;
                Assert.Equal(ceiling, sizes.Pages2PageIdx(count));
                Assert.Equal(floor, sizes.Pages2PageIdxFloor(count));
            }
        }
        int subpages = raw.Count(value => value < (long)page * 4);
        Assert.Equal(subpages, sizes.NSubpages);
        Assert.Equal(subpages - 1, sizes.SmallMaxSizeIdx);
        Assert.Equal(Align(4096, alignment), sizes.LookupMaxSize);
        Assert.Equal(0, sizes.Size2SizeIdx(0));
        Assert.Equal(Align(16, alignment), sizes.NormalizeSize(0));
        Assert.Equal(sizes.NSizes, sizes.Size2SizeIdx(chunk + 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(64)]
    public void HugeAndOverflowInputsUseExplicitClrBounds(int alignment)
    {
        SizeClasses sizes = Original(alignment);
        Assert.Equal(sizes.NSizes, sizes.Size2SizeIdx(int.MaxValue));
        Assert.Equal(sizes.NPSizes, sizes.Pages2PageIdx(int.MaxValue));
        Assert.Equal(sizes.NPSizes, sizes.Pages2PageIdxFloor(int.MaxValue));
        Assert.Equal(1 << 30, sizes.NormalizeSize(1 << 30));
        Assert.Throws<OverflowException>(() => sizes.NormalizeSize(int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => sizes.NormalizeSize(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => sizes.Size2SizeIdx(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => sizes.Pages2PageIdx(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => sizes.Pages2PageIdxFloor(-1));
        foreach (int index in new[] { -1, sizes.NSizes })
        { Assert.Throws<ArgumentOutOfRangeException>(() => sizes.SizeIdx2Size(index)); Assert.Throws<ArgumentOutOfRangeException>(() => sizes.SizeIdx2SizeCompute(index)); }
        foreach (int index in new[] { -1, sizes.NPSizes })
        { Assert.Throws<ArgumentOutOfRangeException>(() => sizes.PageIdx2Size(index)); Assert.Throws<ArgumentOutOfRangeException>(() => sizes.PageIdx2SizeCompute(index)); }
    }

    [Theory]
    [InlineData(4096, 1024)]
    [InlineData(4096, 2048)]
    [InlineData(4096, 4096)]
    [InlineData(8192, 2048)]
    [InlineData(8192, 4096)]
    public void AlignmentCannotTruncateTheChunkTable(int chunk, int alignment)
    {
        // Pinned Java constructs these configurations but returns an out-of-range index for chunkSize.
        Assert.Throws<ArgumentException>(() => new SizeClasses(4096, 12, chunk, alignment));
    }

    [Fact]
    public void MaximumChunkHasRepresentableTableAndPageBoundaries()
    {
        var sizes = new SizeClasses(8192, 13, 1 << 30, 8192);
        Assert.Equal(1 << 30, sizes.SizeIdx2Size(sizes.NSizes - 1));
        Assert.Equal(1 << 30, sizes.SizeIdx2SizeCompute(sizes.NSizes - 1));
        Assert.Equal(1L << 30, sizes.PageIdx2Size(sizes.NPSizes - 1));
        Assert.Equal(1L << 30, sizes.PageIdx2SizeCompute(sizes.NPSizes - 1));
        Assert.Equal(sizes.NSizes - 1, sizes.Size2SizeIdx(1 << 30));
        Assert.Equal(sizes.NPSizes - 1, sizes.Pages2PageIdx(1 << 17));
        Assert.Equal(sizes.NPSizes - 1, sizes.Pages2PageIdxFloor(1 << 17));
        Assert.Equal(sizes.NPSizes, sizes.Pages2PageIdx((1 << 17) + 1));
        Assert.Equal(1 << 30, sizes.NormalizeSize((1 << 30) - 1));
        Assert.Throws<OverflowException>(() => sizes.NormalizeSize(int.MaxValue - 8191));
    }

    [Fact]
    public void ConstructorRejectsInvalidConfiguration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SizeClasses(4000, 12, 65536, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SizeClasses(4096, 31, 65536, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SizeClasses(4096, 12, 65535, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SizeClasses(4096, 12, 2048, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SizeClasses(4096, 12, 65536, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SizeClasses(4096, 12, 65536, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SizeClasses(4096, 12, 65536, 8192));
    }
}
