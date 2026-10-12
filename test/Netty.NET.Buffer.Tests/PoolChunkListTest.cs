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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Netty.NET.Buffer.Tests;

public class PoolChunkListTest
{
    private static AbstractPooledByteBufTest.TestAllocator NewAllocator(bool direct, int shifts = 13, int order = 4)
        => new(direct, shifts, order);

    private static int ListIndex(PoolArena arena, PoolChunk chunk)
        => Enumerable.Range(0, arena.ChunkLists.Count).Single(i => arena.ChunkLists[i].Contains(chunk));

    [Theory]
    [InlineData(false, 12)] [InlineData(true, 12)]
    [InlineData(false, 13)] [InlineData(true, 13)]
    [InlineData(false, 16)] [InlineData(true, 16)]
    public void QuarterChunkAllocationsPromoteAndFreesDemoteThenRetire(bool direct, int shifts)
    {
        using var a = NewAllocator(direct, shifts);
        int quarter = a.Arena.SizeClass.ChunkSize / 4;
        var buffers = new List<PooledByteBuf>();
        PoolChunk chunk = null;
        try
        {
            int[] promoted = { 1, 2, 3, 5 };
            for (int i = 0; i < 4; ++i)
            {
                var b = (PooledByteBuf)a.Buffer(quarter);
                buffers.Add(b); chunk ??= b.Allocation.Chunk;
                Assert.Same(chunk, b.Allocation.Chunk);
                Assert.Equal(promoted[i], ListIndex(a.Arena, chunk));
                Assert.Equal((i + 1) * 25, chunk.Usage);
                b.AsSpan(0, quarter).Fill((byte)(i + 1));
            }
            int[] demoted = { 4, 3, 2 };
            for (int i = 0; i < 4; ++i)
            {
                Assert.Equal(-1, buffers[i].AsSpan(0, quarter).IndexOfAnyExcept((byte)(i + 1)));
                buffers[i].Release();
                if (i < 3) Assert.Equal(demoted[i], ListIndex(a.Arena, chunk));
            }
            Assert.All(a.Arena.ChunkLists, list => Assert.Empty(list));
            Assert.Null(chunk.Parent); Assert.Null(chunk.Next); Assert.Null(chunk.Prev);
            Assert.Equal(chunk.ChunkSize, chunk.FreeBytes);
            Assert.Equal(0, a.Native.ReservedBytes);
        }
        finally { foreach (var b in buffers) if (b.ReferenceCount != 0) b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InitialListRetainsNeverPromotedChunksAndConservativelyRejectsWholeChunkRequests(bool direct)
    {
        using var a = NewAllocator(direct, order: 6);
        var b = (PooledByteBuf)a.Buffer(32768);
        PoolChunk initial = b.Allocation.Chunk;
        Assert.Equal(0, ListIndex(a.Arena, initial));
        b.Release();
        Assert.Equal(0, ListIndex(a.Arena, initial)); Assert.Equal(0, initial.Usage);
        var whole = (PooledByteBuf)a.Buffer(initial.ChunkSize);
        try
        {
            Assert.NotSame(initial, whole.Allocation.Chunk);
            Assert.Equal(5, ListIndex(a.Arena, whole.Allocation.Chunk));
        }
        finally { whole.Release(); }
        Assert.Single(a.Arena.ChunkLists[0]);
        Assert.Equal(direct ? initial.ChunkSize : 0, a.Native.ReservedBytes);
        a.Dispose(); Assert.Equal(0, a.Native.ReservedBytes);
        Assert.All(a.Arena.ChunkLists, list => Assert.Empty(list));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PreferredHalfFullListUsesHeadOrderAndRemovesMiddleAndTailCorrectly(bool direct)
    {
        using var a = NewAllocator(direct);
        int half = a.Arena.SizeClass.ChunkSize / 2;
        var first = new List<PooledByteBuf>();
        var second = new List<PooledByteBuf>();
        try
        {
            for (int i = 0; i < 3; ++i)
            {
                first.Add((PooledByteBuf)a.Buffer(half));
                second.Add((PooledByteBuf)a.Buffer(half));
                Assert.Same(first[i].Allocation.Chunk, second[i].Allocation.Chunk);
            }
            PoolChunk[] chunks = first.Select(b => b.Allocation.Chunk).ToArray();
            foreach (var b in second) b.Release();
            Assert.Equal(chunks.Reverse(), a.Arena.ChunkLists[3]);
            first[1].Release();
            Assert.Equal(new[] { chunks[2], chunks[0] }, a.Arena.ChunkLists[3]);
            first[0].Release(); Assert.Equal(new[] { chunks[2] }, a.Arena.ChunkLists[3]);
            var reused = (PooledByteBuf)a.Buffer(half);
            try { Assert.Same(chunks[2], reused.Allocation.Chunk); Assert.Empty(a.Arena.ChunkLists[3]); }
            finally { reused.Release(); }
            Assert.Equal(new[] { chunks[2] }, a.Arena.ChunkLists[3]);
        }
        finally
        {
            foreach (var b in first.Concat(second)) if (b.ReferenceCount != 0) b.Release();
        }
        Assert.All(a.Arena.ChunkLists, list => Assert.Empty(list));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AvailableSmallSlotInFullChunkBypassesListCapacityGate(bool direct)
    {
        using var a = NewAllocator(direct);
        var buffers = Enumerable.Range(0, 16).Select(_ => (PooledByteBuf)a.Buffer(8192)).ToArray();
        PoolChunk chunk = buffers[0].Allocation.Chunk;
        try
        {
            Assert.All(buffers, b => Assert.Same(chunk, b.Allocation.Chunk));
            Assert.Equal(5, ListIndex(a.Arena, chunk));
            buffers[7].Release();
            Assert.Equal(0, chunk.FreeBytes); // The last empty subpage of its class is retained.
            var reused = (PooledByteBuf)a.Buffer(8192);
            try { Assert.Same(chunk, reused.Allocation.Chunk); Assert.Equal(5, ListIndex(a.Arena, chunk)); }
            finally { reused.Release(); }
        }
        finally { foreach (var b in buffers) if (b.ReferenceCount != 0) b.Release(); }
        Assert.Equal(8192, chunk.ChunkSize - chunk.FreeBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SnapshotMembershipAndLiveMetricsSurviveMovementAndShutdown(bool direct)
    {
        using var a = NewAllocator(direct);
        var first = (PooledByteBuf)a.Buffer(65536);
        PoolChunk chunk = first.Allocation.Chunk;
        var list = a.Arena.ChunkLists[2];
        using var snapshot = list.GetEnumerator();
        IEnumerator nongeneric = ((IEnumerable)list).GetEnumerator();
        var second = a.Buffer(65536);
        Assert.Empty(list);
        Assert.True(snapshot.MoveNext()); Assert.Same(chunk, snapshot.Current);
        Assert.Equal(100, snapshot.Current.Usage); Assert.False(snapshot.MoveNext());
        Assert.True(nongeneric.MoveNext()); Assert.Same(chunk, nongeneric.Current);
        (nongeneric as IDisposable)?.Dispose();
        a.Dispose();
        Assert.All(a.Arena.ChunkLists, l => Assert.Empty(l));
        Assert.Equal(direct ? chunk.ChunkSize : 0, a.Native.ReservedBytes);
        first.Release(); second.Release(); Assert.Equal(0, a.Native.ReservedBytes);
    }

    [Fact]
    public void MetricBoundsDescriptionsAndOuterCollectionAreReadOnly()
    {
        using var a = NewAllocator(false);
        Assert.Equal(new[] { 1, 1, 25, 50, 75, 100 }, a.Arena.ChunkLists.Select(l => l.MinUsage));
        Assert.Equal(new[] { 25, 50, 75, 100, 100, 100 }, a.Arena.ChunkLists.Select(l => l.MaxUsage));
        Assert.Throws<NotSupportedException>(() => ((IList<PoolChunkList>)a.Arena.ChunkLists).Clear());
        Assert.All(a.Arena.ChunkLists, l => Assert.Equal("none", l.ToString()));
        ByteBuf b = a.Buffer(65536);
        try { Assert.Matches(@"^Chunk\([0-9a-f]+: 50%, 65536/131072\)$", a.Arena.ChunkLists[2].ToString()); }
        finally { b.Release(); }
    }

    [Fact]
    public void HeapRetainsOnlyFirstRetiredChunkForReuse()
    {
        using var a = NewAllocator(false);
        var old = Enumerable.Range(0, 3).Select(_ => (PooledByteBuf)a.Buffer(131072)).ToArray();
        var storage = old.Select(b => b.Allocation.Storage).ToArray();
        foreach (var b in old) b.Release();
        Assert.All(a.Arena.ChunkLists, list => Assert.Empty(list));
        var reused = (PooledByteBuf)a.Buffer(131072);
        var another = (PooledByteBuf)a.Buffer(131072);
        try
        {
            Assert.Same(storage[0], reused.Allocation.Storage);
            Assert.DoesNotContain(another.Allocation.Storage, storage);
            Assert.Equal(0, reused.Allocation.Offset);
        }
        finally { reused.Release(); another.Release(); }
    }

    [Fact]
    public unsafe void NativePinDelaysPhysicalFreeOfRetiredChunkWithoutReusingIt()
    {
        using var a = NewAllocator(true);
        var b = (PooledByteBuf)a.Buffer(131072).WriteByte(42);
        var storage = b.Allocation.Storage;
        Memory<byte> memory = b.AsMemory(0, 1);
        using (var pin = memory.Pin())
        {
            b.Release();
            Assert.All(a.Arena.ChunkLists, list => Assert.Empty(list));
            Assert.Equal(131072, a.Native.ReservedBytes);
            Assert.Throws<ObjectDisposedException>(() => memory.Span.ToArray());
            var fresh = (PooledByteBuf)a.Buffer(131072);
            try
            {
                Assert.NotSame(storage, fresh.Allocation.Storage);
                Assert.Equal(262144, a.Native.ReservedBytes);
                Assert.Equal(42, *(byte*)pin.Pointer);
            }
            finally { fresh.Release(); }
            Assert.Equal(131072, a.Native.ReservedBytes);
        }
        Assert.Equal(0, a.Native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OperationLeaseDefersChunkRetirementAfterLogicalFinalRelease(bool direct)
    {
        using var a = NewAllocator(direct);
        var b = (PooledByteBuf)a.Buffer(131072).WriteInt(42);
        PoolChunk chunk = b.Allocation.Chunk;
        Memory<byte> memory = b.AsMemory(0, 4);
        using (var lease = b.AcquireReadLease())
        {
            b.Release();
            Assert.Equal(5, ListIndex(a.Arena, chunk));
            Assert.Equal(new byte[] { 0, 0, 0, 42 }, memory.Span.ToArray());
        }
        Assert.All(a.Arena.ChunkLists, list => Assert.Empty(list));
        Assert.Equal(0, a.Native.ReservedBytes);
        if (direct) Assert.Throws<ObjectDisposedException>(() => memory.Span.ToArray());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ConcurrentSnapshotsAndBufferLifetimesKeepListsTraversable(bool direct)
    {
        using var a = NewAllocator(direct);
        var token = TestContext.Current.CancellationToken;
        await Task.WhenAll(Enumerable.Range(0, 5).Select(worker => Task.Run(() =>
        {
            for (int i = 0; i < 128; ++i)
            {
                if (worker == 0)
                {
                    foreach (var list in a.Arena.ChunkLists)
                    {
                        IPoolChunkMetric[] snapshot = list.ToArray();
                        Assert.Equal(snapshot.Length, snapshot.Distinct().Count());
                        Assert.All(snapshot, c => Assert.InRange(c.FreeBytes, 0, c.ChunkSize));
                    }
                }
                else
                {
                    ByteBuf b = a.Buffer(new[] { 8192, 32768, 65536, 131072 }[i % 4]);
                    try { b.AsSpan(0, b.Capacity).Fill((byte)worker); Assert.Equal(-1, b.AsSpan(0, b.Capacity).IndexOfAnyExcept((byte)worker)); }
                    finally { b.Release(); }
                }
            }
        }, token)));
        a.Dispose(); Assert.Equal(0, a.Native.ReservedBytes);
        Assert.All(a.Arena.ChunkLists, list => Assert.Empty(list));
    }
}
