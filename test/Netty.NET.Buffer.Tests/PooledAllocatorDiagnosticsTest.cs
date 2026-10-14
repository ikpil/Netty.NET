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
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class PooledAllocatorDiagnosticsTest
{
    private static string Normalize(string text) => Regex.Replace(text, @"Chunk\([0-9a-f]+:", "Chunk(<id>:");
    private static readonly string[] Labels = ["0~25%", "0~50%", "25~75%", "50~100%", "75~100%", "100%"];
    private static string Model(PooledByteBufAllocator allocator)
    {
        var text = new StringBuilder();
        foreach (var entry in new[] { (allocator.Metric.HeapArenas, "heap"), (allocator.Metric.DirectArenas, "direct") })
        {
            text.Append(FormattableString.Invariant($"{entry.Item1.Count} {entry.Item2} arena(s):")).Append(Environment.NewLine);
            foreach (IPoolArenaMetric arena in entry.Item1)
            {
                for (int i = 0; i < Labels.Length; i++)
                {
                    text.Append("Chunk(s) at ").Append(Labels[i]).Append(':').Append(Environment.NewLine);
                    var chunks = arena.ChunkLists[i].ToArray();
                    if (chunks.Length == 0) text.Append("none");
                    else for (int j = 0; j < chunks.Length; j++)
                    {
                        if (j != 0) text.Append(Environment.NewLine);
                        IPoolChunkMetric chunk = chunks[j];
                        int usage = chunk.FreeBytes == 0 ? 100 : Math.Min(99, 100 - (int)(100L * chunk.FreeBytes / chunk.ChunkSize));
                        text.Append(FormattableString.Invariant($"Chunk(<id>: {usage}%, {chunk.ChunkSize - chunk.FreeBytes}/{chunk.ChunkSize})"));
                    }
                    text.Append(Environment.NewLine);
                }
                text.Append("small subpages:");
                foreach (var group in arena.SmallSubpages.GroupBy(s => arena.Size2SizeIdx(s.ElementSize)).OrderBy(g => g.Key))
                {
                    text.Append(Environment.NewLine).Append(group.Key.ToString(CultureInfo.InvariantCulture)).Append(": ");
                    foreach (IPoolSubpageMetric page in group)
                    {
                        var subpage = (PoolSubpage)page;
                        int offset = Enumerable.Range(0, subpage.Chunk.ChunkSize / subpage.Chunk.PageSize)
                            .Single(i => ReferenceEquals(subpage.Chunk.SubpageAt(i), subpage));
                        text.Append(FormattableString.Invariant($"({offset}: {page.MaxNumElements - page.NumAvailable}/{page.MaxNumElements}, offset: {offset}, length: {subpage.RunSize}, elemSize: {page.ElementSize})"));
                    }
                }
                text.Append(Environment.NewLine);
            }
        }
        return text.ToString();
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(0, 1)] [InlineData(1, 0)] [InlineData(1, 1)] [InlineData(2, 3)]
    public void EmptyAndLiveReportsPreserveConfiguredArenaOrderAndMatchIndependentMetrics(int heaps, int directs)
    {
        var native = new NativeMemoryAllocator();
        using var allocator = new PooledByteBufAllocator(false, heaps, directs, pageSize: 4096, maxOrder: 2, nativeAllocator: native);
        Assert.Equal(Model(allocator), Normalize(allocator.DumpStats()));
        Assert.Equal(0, allocator.Metric.NumThreadLocalCaches); Assert.Equal(0, native.ReservedBytes);
        var live = new List<ByteBuf>();
        try
        {
            foreach (int size in new[] { 16, 64, 128, 768, 4096, 8192, 20000 })
            {
                if (heaps > 0) live.Add(allocator.HeapBuffer(size, size));
                if (directs > 0) live.Add(allocator.DirectBuffer(size, size));
            }
            string before = Model(allocator);
            Assert.Equal(before, Normalize(allocator.DumpStats())); Assert.Equal(before, Normalize(allocator.DumpStats()));
            foreach (ByteBuf b in live) { Assert.Equal(0, b.ReaderIndex); Assert.Equal(0, b.WriterIndex); Assert.Equal(1, b.ReferenceCount); }
            foreach (ByteBuf b in live) b.Release(); live.Clear();
            Assert.Equal(Model(allocator), Normalize(allocator.DumpStats()));
            allocator.Dispose(); Assert.Equal(Model(allocator), Normalize(allocator.DumpStats()));
            Assert.Equal(0, native.ReservedBytes);
        }
        finally { foreach (ByteBuf b in live) b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FullSubpagesAreOmittedAndMultipleAvailablePagesShareOneSizeClassLine(bool direct)
    {
        var native = new NativeMemoryAllocator();
        using var allocator = new PooledByteBufAllocator(nativeAllocator: native, pageSize: 4096, maxOrder: 2);
        var live = new List<ByteBuf>();
        try
        {
            for (int i = 0; i < 513; i++) live.Add(direct ? allocator.DirectBuffer(16, 16) : allocator.HeapBuffer(16, 16));
            Assert.Equal(Model(allocator), Normalize(allocator.DumpStats()));
            live[0].Release(); live[256].Release();
            Assert.Equal(Model(allocator), Normalize(allocator.DumpStats()));
            Assert.Contains("0: ", allocator.DumpStats());
        }
        finally { foreach (ByteBuf b in live) if (b.ReferenceCount != 0) b.Release(); allocator.Dispose(); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MultipleChunksAndUtilizationTransitionsKeepOriginalListOrder(bool direct)
    {
        var native = new NativeMemoryAllocator();
        using var allocator = new PooledByteBufAllocator(nativeAllocator: native, pageSize: 4096, maxOrder: 2);
        var live = new List<ByteBuf>();
        try
        {
            for (int i = 0; i < 12; i++)
            {
                live.Add(direct ? allocator.DirectBuffer(4096, 4096) : allocator.HeapBuffer(4096, 4096));
                Assert.Equal(Model(allocator), Normalize(allocator.DumpStats()));
            }
            for (int i = live.Count - 1; i >= 0; i--)
            { live[i].Release(); Assert.Equal(Model(allocator), Normalize(allocator.DumpStats())); }
            live.Clear();
        }
        finally { foreach (ByteBuf b in live) if (b.ReferenceCount != 0) b.Release(); allocator.Dispose(); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void CacheHitsDoNotChangeReportsUntilBindingDrainReturnsHandles()
    {
        var native = new NativeMemoryAllocator();
        using var allocator = new PooledByteBufAllocator(nativeAllocator: native, pageSize: 4096, maxOrder: 2,
            smallCacheSize: 4, normalCacheSize: 4);
        ByteBuf b = allocator.DirectBuffer(16, 16); string held = allocator.DumpStats();
        b.Release(); Assert.Equal(held, allocator.DumpStats());
        ByteBuf reused = allocator.DirectBuffer(16, 16); Assert.Equal(held, allocator.DumpStats()); reused.Release();
        allocator.ReleaseCurrentThreadBinding(); Assert.Equal(Model(allocator), Normalize(allocator.DumpStats()));
        Assert.NotEqual(held, allocator.DumpStats()); allocator.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public unsafe void DisposedAllocatorReportsRetiredListsWhileLiveStorageAndPinsRemainValid()
    {
        var native = new NativeMemoryAllocator();
        using var allocator = new PooledByteBufAllocator(nativeAllocator: native, pageSize: 4096, maxOrder: 2);
        ByteBuf b = allocator.DirectBuffer(16, 16).WriteByte(77);
        MemoryHandle pin = b.AsMemory(0, 1).Pin();
        allocator.Dispose();
        Assert.DoesNotMatch(@"Chunk\([0-9a-f]+:", allocator.DumpStats());
        Assert.Equal(Model(allocator), Normalize(allocator.DumpStats()));
        Assert.Equal(77, b.GetByte(0)); b.Release();
        Assert.Equal(77, *((byte*)pin.Pointer)); Assert.True(native.ReservedBytes > 0);
        Assert.Equal(Model(allocator), Normalize(allocator.DumpStats()));
        pin.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void DiagnosticNumbersKeepJavaDecimalSyntaxAcrossCallerCultures()
    {
        var native = new NativeMemoryAllocator();
        using var allocator = new PooledByteBufAllocator(false, 0, 1, pageSize: 4096, maxOrder: 2, nativeAllocator: native);
        ByteBuf b = allocator.DirectBuffer(16, 16);
        string dump = allocator.DumpStats(), metric = allocator.Metric.ToString();
        CultureInfo before = CultureInfo.CurrentCulture;
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone(); culture.NumberFormat.NegativeSign = "minus";
        try
        {
            CultureInfo.CurrentCulture = culture;
            Assert.Equal(dump, allocator.DumpStats()); Assert.Equal(metric, allocator.Metric.ToString());
            var head = PoolSubpage.CreatePoolHeads(new SizeClasses(4096, 12, 16384, 0))[0];
            Assert.Contains("offset: -1", head.ToString()); Assert.DoesNotContain("minus", head.ToString());
        }
        finally { CultureInfo.CurrentCulture = before; b.Release(); allocator.Dispose(); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void ConcurrentAllocationsReturnsAndShutdownKeepDiagnosticsTraversable()
    {
        var native = new NativeMemoryAllocator();
        using var allocator = new PooledByteBufAllocator(nativeAllocator: native, pageSize: 4096, maxOrder: 2);
        Exception failure = null; using var start = new ManualResetEventSlim();
        var worker = new Thread(() =>
        {
            start.Wait();
            try
            {
                for (int i = 0; i < 200; i++)
                {
                    ByteBuf b;
                    try { b = allocator.DirectBuffer(i % 2 == 0 ? 16 : 4096, 4096); }
                    catch (ObjectDisposedException) { break; }
                    b.Release();
                }
            }
            catch (Exception e) { failure = e; }
        });
        worker.Start(); start.Set();
        for (int i = 0; i < 100; i++)
        {
            string report = allocator.DumpStats();
            Assert.StartsWith("1 heap arena(s):" + Environment.NewLine, report);
            Assert.Equal(2, Regex.Matches(report, "small subpages:").Count);
            if (i == 50) allocator.Dispose();
        }
        Assert.True(worker.Join(TimeSpan.FromSeconds(10))); Assert.Null(failure);
        allocator.Dispose(); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(1, 2)] [InlineData(2, 0)]
    public void CompleteAllocatorMetricFacadeReportsConfigurationBindingsAndReadOnlyArenaCollections(int heaps, int directs)
    {
        var native = new NativeMemoryAllocator();
        using var allocator = new PooledByteBufAllocator(false, heaps, directs, pageSize: 4096, maxOrder: 2,
            nativeAllocator: native, smallCacheSize: 3, normalCacheSize: 5);
        PooledByteBufAllocatorMetric metric = allocator.Metric;
        Assert.Same(metric, ((IByteBufAllocatorMetricProvider)allocator).Metric);
        Assert.Equal(heaps, metric.NumHeapArenas); Assert.Equal(directs, metric.NumDirectArenas);
        Assert.Same(allocator.HeapArenas, metric.HeapArenas); Assert.Same(allocator.DirectArenas, metric.DirectArenas);
        Assert.Throws<NotSupportedException>(() => ((ICollection<IPoolArenaMetric>)metric.HeapArenas).Clear());
        Assert.Throws<NotSupportedException>(() => ((ICollection<IPoolArenaMetric>)metric.DirectArenas).Clear());
#pragma warning disable CS0618
        Assert.Equal(0, metric.TinyCacheSize);
#pragma warning restore CS0618
        Assert.Equal(3, metric.SmallCacheSize); Assert.Equal(5, metric.NormalCacheSize); Assert.Equal(16384, metric.ChunkSize);
        Assert.Equal(0, metric.NumThreadLocalCaches);
        ByteBuf heap = allocator.HeapBuffer(16, 16), direct = allocator.DirectBuffer(4096, 4096);
        try
        {
            Assert.Equal(heaps == 0 ? -1 : 16384, metric.UsedHeapMemory);
            Assert.Equal(directs == 0 ? -1 : 16384, metric.UsedDirectMemory);
            Assert.Equal(heaps + directs == 0 ? 0 : 1, metric.NumThreadLocalCaches);
            Assert.Equal(FormattableString.Invariant($"PooledByteBufAllocatorMetric(usedHeapMemory: {(heaps == 0 ? -1 : 16384)}; usedDirectMemory: {(directs == 0 ? -1 : 16384)}; numHeapArenas: {heaps}; numDirectArenas: {directs}; smallCacheSize: 3; normalCacheSize: 5; numThreadLocalCaches: {(heaps + directs == 0 ? 0 : 1)}; chunkSize: 16384)"), metric.ToString());
            allocator.ReleaseCurrentThreadBinding(); Assert.Equal(0, metric.NumThreadLocalCaches);
        }
        finally { heap.Release(); direct.Release(); allocator.Dispose(); }
        Assert.Equal(0, native.ReservedBytes);
    }
}
