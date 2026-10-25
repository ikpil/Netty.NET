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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public abstract class AbstractPooledByteBufTest
{
    protected abstract bool Direct { get; }

    internal sealed class TestAllocator : AbstractByteBufAllocator, IDisposable
    {
        internal readonly PoolArena Arena;
        internal readonly NativeMemoryAllocator Native;
        internal int Calculations;
        private readonly bool _direct;
        internal TestAllocator(bool direct, int shifts = 13, int order = 4, int alignment = 0,
            long maximumBytes = long.MaxValue) : base(direct)
        {
            _direct = direct;
            Native = new NativeMemoryAllocator(maximumBytes);
            var sizes = new SizeClasses(1 << shifts, shifts, 1 << (shifts + order), alignment);
            Arena = direct ? new PoolDirectArena(this, sizes, Native) : new PoolHeapArena(this, sizes);
        }
        public override bool IsDirectBufferPooled => _direct;
        protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity)
            => _direct ? new UnpooledHeapByteBuf(this, initialCapacity, maxCapacity)
                : Arena.Allocate(initialCapacity, maxCapacity);
        protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
            => _direct ? Arena.Allocate(initialCapacity, maxCapacity)
                : new UnpooledDirectByteBuf(this, initialCapacity, maxCapacity, Native);
        public override int CalculateNewCapacity(int minimumNewCapacity, int maxCapacity)
        {
            ++Calculations;
            return base.CalculateNewCapacity(minimumNewCapacity, maxCapacity);
        }
        public void Dispose() => Arena.Dispose();
    }

    private protected TestAllocator NewAllocator(int shifts = 13, int order = 4, int alignment = 0,
        long maximumBytes = long.MaxValue) => new(Direct, shifts, order, alignment, maximumBytes);

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(4)] [InlineData(150)] [InlineData(16384)] [InlineData(65536)] [InlineData(131073)]
    public void OriginalInitialIndicesMarksAndStorage(int length)
    {
        using var allocator = NewAllocator();
        ByteBuf buffer = allocator.Buffer(length, int.MaxValue);
        try
        {
            // Testing if the writerIndex and readerIndex are correct when allocate and also after we reset the mark.
            Assert.Equal(0, buffer.WriterIndex);
            Assert.Equal(0, buffer.ReaderIndex);
            buffer.ResetReaderIndex(); buffer.ResetWriterIndex();
            Assert.Equal(0, buffer.WriterIndex); Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(length, buffer.Capacity); Assert.True(buffer.IsContiguous);
            Assert.Equal(Direct, buffer.IsDirect); Assert.Same(allocator, buffer.Allocator);
            Assert.Equal(1, buffer.ReferenceCount);
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void OriginalEnsureWritableWithEnoughSpaceShouldNotThrow()
    {
        using var allocator = NewAllocator();
        ByteBuf buffer = allocator.Buffer(1, 10);
        try { buffer.EnsureWritable(3); Assert.True(buffer.WritableBytes >= 3); }
        finally { buffer.Release(); }
    }

    [Fact]
    public void OriginalEnsureWritableWithNotEnoughSpaceShouldThrow()
    {
        using var allocator = NewAllocator();
        ByteBuf buffer = allocator.Buffer(1, 10);
        try { Assert.Throws<ArgumentOutOfRangeException>(() => buffer.EnsureWritable(11)); }
        finally { buffer.Release(); }
    }

    [Fact]
    public void OriginalMaxFastWritableBytes()
    {
        using var allocator = NewAllocator();
        var buffer = (PooledByteBuf)allocator.Buffer(150, 500).SetIndex(0, 100);
        try
        {
            Assert.Equal(50, buffer.WritableBytes); Assert.Equal(150, buffer.Capacity);
            Assert.Equal(500, buffer.MaxCapacity); Assert.Equal(400, buffer.MaxWritableBytes);
            int chunkSize = buffer.Allocation.ReservedLength;
            Assert.True(chunkSize >= 150);
            int remainingInAlloc = Math.Min(chunkSize - 100, 400);
            Assert.Equal(remainingInAlloc, buffer.MaxFastWritableBytes);
            // write up to max, chunk alloc should not change (same handle)
            var allocationBefore = buffer.Allocation;
            buffer.WriteBytes(new byte[remainingInAlloc]);
            Assert.Same(allocationBefore, buffer.Allocation);
            Assert.Equal(0, buffer.MaxFastWritableBytes);
            // writing one more should trigger a reallocation (new handle)
            buffer.WriteByte(7);
            Assert.NotSame(allocationBefore, buffer.Allocation);
            // should not exceed maxCapacity even if chunk alloc does
            buffer.Capacity = 500;
            Assert.Equal(500 - buffer.WriterIndex, buffer.MaxFastWritableBytes);
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalEnsureWritableDoesntGrowTooMuch(bool statusOverload)
    {
        using var allocator = NewAllocator();
        var buffer = (PooledByteBuf)allocator.Buffer(150, 500).SetIndex(0, 100);
        try
        {
            Assert.Equal(50, buffer.WritableBytes);
            int fastWritable = buffer.MaxFastWritableBytes;
            Assert.True(fastWritable > 50);
            var before = buffer.Allocation;
            // capacity expansion should not cause reallocation
            // (should grow precisely the specified amount)
            if (statusOverload) Assert.Equal(2, buffer.EnsureWritable(fastWritable, false));
            else buffer.EnsureWritable(fastWritable);
            Assert.Same(before, buffer.Allocation);
            Assert.Equal(100 + fastWritable, buffer.Capacity);
            Assert.Equal(buffer.WritableBytes, buffer.MaxFastWritableBytes);
            Assert.Equal(0, allocator.Calculations);
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void OriginalDistinctBuffersMustNotOverlap()
    {
        using var allocator = NewAllocator();
        ByteBuf a = allocator.Buffer(16384), b = allocator.Buffer(65536);
        try
        {
            a.SetByte(a.Capacity - 1, 1); b.SetByte(0, 2);
            Assert.Equal(1, a.GetByte(a.Capacity - 1));
        }
        finally { a.Release(); b.Release(); }
    }

    [Theory]
    [InlineData(17, 31, true)] [InlineData(160, 145, true)] [InlineData(160, 144, false)]
    [InlineData(512, 497, true)] [InlineData(512, 496, false)]
    [InlineData(1024, 513, true)] [InlineData(1024, 512, false)]
    [InlineData(65536, 32769, true)] [InlineData(65536, 32768, false)]
    [InlineData(131073, 131074, false)] [InlineData(131074, 131073, false)]
    [InlineData(150, 161, false)] [InlineData(0, 16, true)] [InlineData(0, 0, true)]
    public void CapacityThresholdsPreserveBytesAndTrimIndices(int oldCapacity, int newCapacity, bool sameRegion)
    {
        using var allocator = NewAllocator();
        var buffer = (PooledByteBuf)allocator.Buffer(oldCapacity);
        byte[] bytes = Enumerable.Range(0, oldCapacity).Select(i => (byte)(i % 251)).ToArray();
        buffer.WriteBytes(bytes).SetIndex(oldCapacity / 3, oldCapacity);
        buffer.MarkReaderIndex(); buffer.MarkWriterIndex();
        var before = buffer.Allocation;
        try
        {
            buffer.Capacity = newCapacity;
            Assert.Equal(sameRegion, ReferenceEquals(before, buffer.Allocation));
            Assert.Equal(newCapacity, buffer.Capacity);
            Assert.Equal(Math.Min(oldCapacity, newCapacity), buffer.WriterIndex);
            Assert.Equal(Math.Min(oldCapacity / 3, newCapacity), buffer.ReaderIndex);
            Assert.Equal(bytes.AsSpan(0, Math.Min(oldCapacity, newCapacity)).ToArray(),
                buffer.AsSpan(0, Math.Min(oldCapacity, newCapacity)).ToArray());
            if (oldCapacity <= newCapacity)
            {
                buffer.ResetReaderIndex(); buffer.ResetWriterIndex();
                Assert.Equal(oldCapacity / 3, buffer.ReaderIndex); Assert.Equal(oldCapacity, buffer.WriterIndex);
            }
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(13, 4)] [InlineData(12, 4)] [InlineData(13, 0)]
    public void EverySizeClassHasRealNonoverlappingBoundedStorage(int shifts, int order)
    {
        using var allocator = NewAllocator(shifts, order);
        var buffers = new List<PooledByteBuf>();
        try
        {
            for (int i = 0; i < allocator.Arena.SizeClass.NSizes; ++i)
            {
                int size = allocator.Arena.SizeClass.SizeIdx2Size(i);
                for (int j = 0; j < 3; ++j)
                {
                    var b = (PooledByteBuf)allocator.Buffer(size);
                    buffers.Add(b);
                    b.AsSpan(0, size).Fill((byte)(i * 3 + j));
                    var region = b.Allocation;
                    Assert.True(region.Offset >= 0 && region.Offset <= region.Memory.Length - region.ReservedLength);
                    foreach (var previous in buffers.Take(buffers.Count - 1))
                        if (ReferenceEquals(previous.Allocation.Storage, region.Storage))
                            Assert.True(previous.Allocation.Offset + previous.Allocation.ReservedLength <= region.Offset ||
                                region.Offset + region.ReservedLength <= previous.Allocation.Offset);
                }
            }
            for (int i = 0; i < buffers.Count; ++i)
                Assert.All(buffers[i].AsSpan(0, buffers[i].Capacity).ToArray(), value => Assert.Equal((byte)i, value));
        }
        finally { foreach (var b in buffers) b.Release(); }
    }

    [Fact]
    public void FinalReleaseReturnsRegionOnceAndNeverRecyclesTheWrapper()
    {
        using var allocator = NewAllocator();
        var first = (PooledByteBuf)allocator.Buffer(48);
        var region = first.Allocation;
        Assert.True(first.Release());
        var second = (PooledByteBuf)allocator.Buffer(48);
        try
        {
            Assert.NotSame(first, second); Assert.Same(region.Chunk, second.Allocation.Chunk);
            Assert.Equal(region.Handle, second.Allocation.Handle);
            Assert.Equal(0, first.ReferenceCount); Assert.Equal(1, second.ReferenceCount);
            Assert.Throws<IllegalReferenceCountException>(() => first.Release());
            Assert.Throws<IllegalReferenceCountException>(() => first.Retain());
            Assert.Throws<IllegalReferenceCountException>(() => first.GetByte(0));
            Assert.Throws<IllegalReferenceCountException>(() => first.Capacity = 48);
            second.SetByte(0, 99); Assert.Equal(99, second.GetByte(0));
        }
        finally { second.Release(); }
    }

    [Fact]
    public void SharedSubpageHeadBindsTheActualOwnersArrayAndNormalReleaseReturnsPages()
    {
        using var allocator = NewAllocator();
        var full = (PooledByteBuf)allocator.Buffer(allocator.Arena.SizeClass.ChunkSize);
        var first = (PooledByteBuf)allocator.Buffer(48);
        var second = (PooledByteBuf)allocator.Buffer(48);
        PoolChunk fullChunk = full.Allocation.Chunk;
        try
        {
            Assert.NotSame(fullChunk, first.Allocation.Chunk);
            Assert.Same(first.Allocation.Chunk, second.Allocation.Chunk);
            Assert.Same(first.Allocation.Storage, second.Allocation.Storage);
            Assert.NotEqual(first.Allocation.Offset, second.Allocation.Offset);
            first.AsSpan(0, 48).Fill(0x12); second.AsSpan(0, 48).Fill(0x34);
            Assert.Equal(-1, first.AsSpan(0, 48).IndexOfAnyExcept((byte)0x12));
            Assert.Equal(0, fullChunk.FreeBytes);
        }
        finally { full.Release(); first.Release(); second.Release(); }
        Assert.Equal(fullChunk.ChunkSize, fullChunk.FreeBytes);
    }

    [Fact]
    public void OriginalNormalPoolSubpageReleaseThroughRealBuffers()
    {
        // 16 < elemSize <= 7168 or 8192 < elemSize <= 28672, 1 < subpage.maxNumElems <= 256
        // 7168 <= elemSize <= 8192, subpage.maxNumElems == 1
        const int elemSize = 8192, length = 1024;
        using var allocator = NewAllocator(13, 11);
        PooledByteBuf[] buffers = Enumerable.Range(0, length)
            .Select(_ => (PooledByteBuf)allocator.Buffer(elemSize, elemSize)).ToArray();
        PoolChunk chunk = buffers[0].Allocation.Chunk;
        int before = chunk.FreeBytes;
        foreach (var b in buffers) b.Release();
        Assert.True(before < chunk.FreeBytes);
        Assert.Equal(chunk.ChunkSize - elemSize, chunk.FreeBytes);
    }

    [Fact]
    public void RetainedViewsAndCopiesUseTheCreatingAllocatorAndOwnership()
    {
        using var allocator = NewAllocator();
        var root = (PooledByteBuf)allocator.Buffer(17, 500).WriteBytes(new byte[] { 10, 20, 30, 40 });
        ByteBuf slice = root.RetainedSlice(1, 2), duplicate = root.RetainedDuplicate();
        ByteBuf copy = slice.Copy();
        try
        {
            Assert.Same(allocator, copy.Allocator); Assert.IsAssignableFrom<PooledByteBuf>(copy); Assert.Equal(Direct, copy.IsDirect);
            Assert.Equal(new byte[] { 20, 30 }, ByteBufUtil.GetBytes(copy));
            Assert.Equal(500, copy.MaxCapacity);
            Assert.False(root.Release()); Assert.Equal(1, slice.ReferenceCount); Assert.Equal(1, duplicate.ReferenceCount);
            root.Capacity = 500;
            slice.SetByte(0, 77); Assert.Equal(77, duplicate.GetByte(1));
            Assert.Equal(20, copy.GetByte(0));
            Assert.True(duplicate.Release()); Assert.Equal(1, root.ReferenceCount); Assert.True(slice.Release());
            Assert.Equal(0, root.ReferenceCount);
        }
        finally
        {
            copy.Release();
            if (slice.ReferenceCount > 0) slice.Release();
            if (duplicate.ReferenceCount > 0) duplicate.Release();
            if (root.ReferenceCount > 0) root.Release(root.ReferenceCount);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OperationLeaseDefersReuseAcrossReplacementAndLogicalRelease(bool replace)
    {
        using var allocator = NewAllocator();
        var root = (PooledByteBuf)allocator.Buffer(48);
        root.AsSpan(0, 48).Fill(0xA5);
        var old = root.Allocation;
        using (var lease = root.AsReadOnly().AcquireReadLease())
        {
            if (replace) root.Capacity = 65536;
            else Assert.True(root.Release());
            var other = (PooledByteBuf)allocator.Buffer(48);
            try
            {
                Assert.NotEqual(old.Handle, other.Allocation.Handle);
                other.AsSpan(0, 48).Fill(0xCC);
                Assert.All(old.Memory.Span.Slice(old.Offset, 48).ToArray(), x => Assert.Equal(0xA5, x));
            }
            finally { other.Release(); }
        }
        var reused = (PooledByteBuf)allocator.Buffer(48);
        try { Assert.Equal(old.Handle, reused.Allocation.Handle); Assert.Same(old.Chunk, reused.Allocation.Chunk); }
        finally { reused.Release(); if (root.ReferenceCount != 0) root.Release(); }
    }

    [Fact]
    public void AliasedSpanGrowthPreservesSourceAndReleasesOldReservationAfterWrite()
    {
        using var allocator = NewAllocator();
        var buffer = (PooledByteBuf)allocator.Buffer(48);
        byte[] bytes = Enumerable.Range(1, 48).Select(i => (byte)i).ToArray();
        buffer.WriteBytes(bytes);
        var old = buffer.Allocation;
        try
        {
            buffer.WriteBytes(buffer.AsSpan(0, 48));
            Assert.Equal(bytes.Concat(bytes).ToArray(), ByteBufUtil.GetBytes(buffer));
            var reused = (PooledByteBuf)allocator.Buffer(48);
            try { Assert.Same(old.Chunk, reused.Allocation.Chunk); Assert.Equal(old.Handle, reused.Allocation.Handle); }
            finally { reused.Release(); }
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void CompositeLeaseProtectsReleasedPooledComponents()
    {
        using var allocator = NewAllocator();
        var component = (PooledByteBuf)allocator.Buffer(48).WriteBytes(new byte[48]);
        var old = component.Allocation;
        var composite = allocator.CompositeBuffer(1).AddComponent(component, true);
        using (var lease = composite.AcquireReadLease())
        {
            composite.Capacity = 96;
            Assert.Equal(0, component.ReferenceCount);
            var other = (PooledByteBuf)allocator.Buffer(48);
            try { Assert.NotEqual(old.Handle, other.Allocation.Handle); }
            finally { other.Release(); }
        }
        var reused = (PooledByteBuf)allocator.Buffer(48);
        try { Assert.Equal(old.Handle, reused.Allocation.Handle); }
        finally { reused.Release(); composite.Release(); }
    }

    [Fact]
    public void KernelPrimitivesSearchOverlapAndStreamRespectRegionOffset()
    {
        using var allocator = NewAllocator();
        ByteBuf guard = allocator.Buffer(128).WriteBytes(Enumerable.Repeat((byte)0xCC, 128).ToArray());
        var b = (PooledByteBuf)allocator.Buffer(128);
        Assert.True(b.Allocation.Offset > 0);
        try
        {
            b.WriteLong(0x0102030405060708).WriteIntLE(0x11223344).WriteShort(0x5566).WriteMediumLE(0x778899);
            byte[] expected = { 1, 2, 3, 4, 5, 6, 7, 8, 0x44, 0x33, 0x22, 0x11, 0x55, 0x66, 0x99, 0x88, 0x77 };
            Assert.Equal(expected, ByteBufUtil.GetBytes(b));
            Assert.Equal(0x0102030405060708, b.GetLong(0)); Assert.Equal(0x11223344, b.GetIntLE(8));
            Assert.Equal(0x5566, b.GetShort(12)); Assert.Equal(0x778899, b.GetUnsignedMediumLE(14));
            Assert.Equal(8, b.IndexOf(0, b.WriterIndex, 0x44));
            b.SetBytes(2, b.AsSpan(0, 12));
            expected.AsSpan(0, 12).CopyTo(expected.AsSpan(2));
            using var output = new MemoryStream();
            b.GetBytes(0, output, b.WriterIndex); Assert.Equal(expected, output.ToArray());
            b.SetBytes(0, new MemoryStream(new byte[] { 9, 8, 7 }), 3);
            Assert.Equal(new byte[] { 9, 8, 7 }, b.AsSpan(0, 3).ToArray());
            Assert.All(ByteBufUtil.GetBytes(guard), x => Assert.Equal(0xCC, x));
        }
        finally { b.Release(); guard.Release(); }
    }

    [Fact]
    public async Task ParallelBufferLifetimesNeverOverlapOrCorruptLiveRegions()
    {
        using var allocator = NewAllocator();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (int i = 0; i < 256; ++i)
            {
                int size = new[] { 16, 48, 150, 16384, 65536, 131073 }[i % 6];
                ByteBuf b = allocator.Buffer(size);
                try
                {
                    b.AsSpan(0, size).Fill((byte)(worker + 1));
                    b.Capacity = size * 2;
                    Assert.Equal(-1, b.AsSpan(0, size).IndexOfAnyExcept((byte)(worker + 1)));
                }
                finally { b.Release(); }
            }
        })));
    }

    [Fact]
    public void InvalidCapacitiesAndGeometryDoNotChangeLiveStorage()
    {
        using var allocator = NewAllocator();
        Assert.Throws<ArgumentOutOfRangeException>(() => allocator.Arena.Allocate(-1, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => allocator.Arena.Allocate(101, 100));
        if (!Direct) Assert.Throws<ArgumentException>(() => new PoolHeapArena(allocator, new SizeClasses(8192, 13, 131072, 64)));
        Assert.Throws<ArgumentException>(() => new PoolHeapArena(allocator, new SizeClasses(8192, 12, 131072, 0)));
        var buffer = (PooledByteBuf)allocator.Buffer(48, 100);
        var before = buffer.Allocation;
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Capacity = 101);
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Capacity = -1);
            Assert.Same(before, buffer.Allocation); Assert.Equal(48, buffer.Capacity);
        }
        finally { buffer.Release(); }
    }
}
