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

/*
 * Copyright 2021 The Netty Project
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
using System.Linq;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

/**
 * Tests big-endian direct channel buffers
 */
public class PooledDirectByteBufTest : AbstractPooledByteBufTest
{
    protected override bool Direct => true;

    [Theory]
    [InlineData(1)] [InlineData(8)] [InlineData(16)] [InlineData(64)] [InlineData(8192)]
    public unsafe void EveryAlignedClassAndHugeReservationHasAlignedBoundedStorage(int alignment)
    {
        using var allocator = NewAllocator(alignment: alignment);
        var sizes = allocator.Arena.SizeClass;
        for (int i = 0; i <= sizes.NSizes; ++i)
        {
            int requested = i == sizes.NSizes ? sizes.ChunkSize + 1 : Math.Max(1, sizes.SizeIdx2Size(i) - 1);
            var b = (PooledDirectByteBuf)allocator.DirectBuffer(requested, requested);
            try
            {
                Assert.Equal(requested, b.Capacity); Assert.Equal(requested, b.MaxCapacity);
                Assert.True(b.Allocation.ReservedLength >= requested);
                Assert.Equal(0, b.Allocation.ReservedLength % alignment);
                Assert.True(NativeMemoryOwner.TryGetOwner(b.AsMemory(0, requested), out var owner));
                Assert.Equal(b.Allocation.Memory.Length + alignment, owner.Length);
                using var pin = b.AsMemory(0, requested).Pin();
                Assert.Equal((nuint)0, (nuint)pin.Pointer % (nuint)alignment);
                b.AsSpan(0, requested).Fill(0xA5);
                Assert.Equal(-1, b.AsSpan(0, requested).IndexOfAnyExcept((byte)0xA5));
            }
            finally { b.Release(); }
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(64)] [InlineData(8192)]
    public void HugeAlignedLogicalCapacityRespectsMaximumAndCopyPolicy(int alignment)
    {
        using var allocator = NewAllocator(alignment: alignment);
        int requested = allocator.Arena.SizeClass.ChunkSize + 1;
        var b = (PooledDirectByteBuf)allocator.DirectBuffer(requested, requested);
        try
        {
            Assert.Equal(requested, b.Capacity); Assert.Equal(requested, b.MaxCapacity);
            Assert.Equal(alignment == 0 ? requested : allocator.Arena.SizeClass.NormalizeSize(requested),
                b.Allocation.ReservedLength);
            b.WriteLong(0x0102030405060708);
            ByteBuf copy = b.Copy(1, 6);
            try
            {
                Assert.IsType<PooledDirectByteBuf>(copy); Assert.True(copy.IsDirect);
                Assert.Same(allocator, copy.Allocator); Assert.Equal(requested, copy.MaxCapacity);
                Assert.Equal(new byte[] { 2, 3, 4, 5, 6, 7 }, ByteBufUtil.GetBytes(copy));
                copy.SetByte(0, 99); Assert.Equal(2, b.GetByte(1));
            }
            finally { copy.Release(); }
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(64)]
    public void FailedNativeGrowthPreservesReservationBytesIndicesMarksAndRetry(int alignment)
    {
        const int chunkSize = 131072;
        using var allocator = NewAllocator(alignment: alignment, maximumBytes: chunkSize + alignment);
        var b = (PooledDirectByteBuf)allocator.DirectBuffer(chunkSize, chunkSize * 4);
        b.AsSpan(0, chunkSize).Fill(0xA5);
        b.SetIndex(10, 100).MarkReaderIndex().MarkWriterIndex();
        var before = b.Allocation;
        try
        {
            Assert.Throws<OutOfMemoryException>(() => b.Capacity = chunkSize + 1);
            Assert.Same(before, b.Allocation); Assert.Equal(chunkSize, b.Capacity);
            Assert.Equal(10, b.ReaderIndex); Assert.Equal(100, b.WriterIndex);
            Assert.Equal(chunkSize + alignment, allocator.Native.ReservedBytes);
            Assert.Equal(-1, b.AsSpan(0, chunkSize).IndexOfAnyExcept((byte)0xA5));
            b.SetIndex(20, 90).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(10, b.ReaderIndex); Assert.Equal(100, b.WriterIndex);
            Assert.Throws<OutOfMemoryException>(() => allocator.DirectBuffer(48));
        }
        finally { b.Release(); }
        Assert.Equal(0, allocator.Native.ReservedBytes);
        var retry = (PooledDirectByteBuf)allocator.DirectBuffer(48);
        try { Assert.NotSame(before.Storage, retry.Allocation.Storage); retry.SetByte(0, 99); }
        finally { retry.Release(); }
    }

    [Theory]
    [InlineData(48)] [InlineData(131073)]
    public void RepeatedInitialFailureReservesNothing(int size)
    {
        using var allocator = NewAllocator(maximumBytes: 0);
        for (int i = 0; i < 16; ++i)
        {
            Assert.Throws<OutOfMemoryException>(() => allocator.DirectBuffer(size));
            Assert.Equal(0, allocator.Native.ReservedBytes);
        }
    }

    [Fact]
    public void AlignmentOverflowFailsBeforeNativeReservation()
    {
        using var allocator = NewAllocator(alignment: 64, maximumBytes: 0);
        Assert.Throws<OverflowException>(() => allocator.DirectBuffer(int.MaxValue, int.MaxValue));
        Assert.Equal(0, allocator.Native.ReservedBytes);
    }

    [Theory]
    [InlineData(48)] [InlineData(131073)]
    public void ArenaCloseAndFinalReleaseWaitForInternalOperationLease(int size)
    {
        using var allocator = NewAllocator();
        var b = (PooledDirectByteBuf)allocator.DirectBuffer(size);
        Memory<byte> oldMemory = b.AsMemory(0, size);
        oldMemory.Span.Fill(0x12);
        using (var lease = b.AcquireReadLease())
        {
            allocator.Arena.Dispose();
            Assert.True(b.Release()); Assert.Equal(0, b.ReferenceCount);
            Assert.True(allocator.Native.ReservedBytes > 0);
            Assert.Equal(-1, oldMemory.Span.IndexOfAnyExcept((byte)0x12));
            Assert.Throws<ObjectDisposedException>(() => allocator.DirectBuffer(1));
        }
        Assert.Equal(0, allocator.Native.ReservedBytes);
        Assert.Throws<ObjectDisposedException>(() => oldMemory.Span.ToArray());
    }

    [Theory]
    [InlineData(48)] [InlineData(131073)]
    public unsafe void NativePinKeepsPhysicalStorageAliveAfterArenaClose(int size)
    {
        using var allocator = NewAllocator();
        ByteBuf b = allocator.DirectBuffer(size).WriteByte(0xA5);
        Memory<byte> memory = b.AsMemory(0, 1);
        using (var pin = memory.Pin())
        {
            b.Release(); allocator.Arena.Dispose();
            Assert.True(allocator.Native.ReservedBytes > 0);
            Assert.Equal(0xA5, *(byte*)pin.Pointer);
            Assert.Throws<ObjectDisposedException>(() => memory.Span.ToArray());
        }
        Assert.Equal(0, allocator.Native.ReservedBytes);
    }

    [Fact]
    public void ArenaCloseRetiresUnusedChunksAndPreservesActiveSiblingViews()
    {
        using var allocator = NewAllocator();
        const int chunk = 131072;
        ByteBuf unused = allocator.DirectBuffer(chunk), live = allocator.DirectBuffer(17, 100);
        ByteBuf huge = allocator.DirectBuffer(chunk + 1);
        ByteBuf view = live.RetainedSlice(0, 17);
        unused.Release(); live.SetByte(0, 42);
        Assert.Equal(2L * chunk + 1, allocator.Native.ReservedBytes);
        allocator.Arena.Dispose(); allocator.Arena.Dispose();
        Assert.Equal(2L * chunk + 1, allocator.Native.ReservedBytes);
        Assert.Equal(42, view.GetByte(0));
        live.Capacity = 32;
        Assert.Throws<ObjectDisposedException>(() => live.Capacity = 65);
        Assert.Equal(32, live.Capacity); Assert.Equal(42, live.GetByte(0));
        Assert.False(live.Release()); Assert.Equal(42, view.GetByte(0));
        huge.Release(); Assert.Equal(chunk, allocator.Native.ReservedBytes);
        view.Release(); Assert.Equal(0, allocator.Native.ReservedBytes);
    }

    [Fact]
    public void EmptyPoolIsRetainedUntilArenaCloseWhileHugeStorageReturnsImmediately()
    {
        using var allocator = NewAllocator();
        ByteBuf pooled = allocator.DirectBuffer(48), huge = allocator.DirectBuffer(131073);
        Assert.Equal(262145, allocator.Native.ReservedBytes);
        huge.Release(); Assert.Equal(131072, allocator.Native.ReservedBytes);
        pooled.Release(); Assert.Equal(131072, allocator.Native.ReservedBytes);
        allocator.Arena.Dispose(); Assert.Equal(0, allocator.Native.ReservedBytes);
        Assert.Throws<ObjectDisposedException>(() => allocator.DirectBuffer(0, 1));
    }

    [Fact]
    public void TinyUnalignedChunkFallbackKeepsExactDedicatedCapacity()
    {
        using var allocator = NewAllocator(12, 0);
        var b = (PooledDirectByteBuf)allocator.DirectBuffer(47);
        try
        {
            Assert.False(b.Allocation.IsPooled);
            Assert.Equal(47, b.Capacity); Assert.Equal(47, b.Allocation.ReservedLength);
            Assert.Equal(47, allocator.Native.ReservedBytes);
        }
        finally { b.Release(); }
        Assert.Equal(0, allocator.Native.ReservedBytes);
    }

    [Fact]
    public async Task ConcurrentArenaCloseAndIndependentFinalReleasesReturnAllNativeMemory()
    {
        using var allocator = NewAllocator();
        ByteBuf[] buffers = Enumerable.Range(0, 128)
            .Select(i => allocator.DirectBuffer(new[] { 48, 8192, 131073 }[i % 3]).WriteInt(i)).ToArray();
        var cancellation = TestContext.Current.CancellationToken;
        Task close = Task.Run(() => allocator.Arena.Dispose(), cancellation);
        await Task.WhenAll(buffers.Select((b, i) => Task.Run(() =>
        {
            try { Assert.Equal(i, b.GetInt(0)); }
            finally { b.Release(); }
        }, cancellation)).Append(close));
        Assert.Equal(0, allocator.Native.ReservedBytes);
    }
}
