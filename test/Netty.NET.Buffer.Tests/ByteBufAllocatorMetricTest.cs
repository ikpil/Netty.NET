/*
 * Copyright 2017 The Netty Project
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

public class ByteBufAllocatorMetricTest
{
    [Fact]
    public void OriginalUsedDirectMemory()
    {
        var allocator = new UnpooledByteBufAllocator(true);
        IByteBufAllocatorMetric metric = ((IByteBufAllocatorMetricProvider)allocator).Metric;
        Assert.Equal(0, metric.UsedDirectMemory);
        ByteBuf buffer = allocator.DirectBuffer(1024, 4096);
        try
        {
            int capacity = buffer.Capacity;
            Assert.Equal(capacity, metric.UsedDirectMemory);
            // Double the size of the buffer
            buffer.Capacity = capacity << 1;
            Assert.Equal(buffer.Capacity, metric.UsedDirectMemory);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, metric.UsedDirectMemory);
    }

    [Fact]
    public void OriginalUsedHeapMemory()
    {
        var allocator = new UnpooledByteBufAllocator(true);
        IByteBufAllocatorMetric metric = allocator.Metric;
        Assert.Equal(0, metric.UsedHeapMemory);
        ByteBuf buffer = allocator.HeapBuffer(1024, 4096);
        try
        {
            int capacity = buffer.Capacity;
            Assert.Equal(capacity, metric.UsedHeapMemory);
            // Double the size of the buffer
            buffer.Capacity = capacity << 1;
            Assert.Equal(buffer.Capacity, metric.UsedHeapMemory);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, metric.UsedHeapMemory);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalUsedMemoryHuge(bool direct)
    {
        var allocator = new UnpooledByteBufAllocator(true);
        Assert.Equal(0, allocator.Metric.UsedHeapMemory);
        const int size = 32 * 1024 * 1024;
        ByteBuf buffer = Allocate(allocator, direct, size, size);
        try { AssertUsage(allocator, direct, size); }
        finally { buffer.Release(); }
        AssertUsage(allocator, direct, 0);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void GrowthShrinkAndRejectedResizeTrackOwnedCapacity(bool direct)
    {
        var a = new UnpooledByteBufAllocator(); ByteBuf b = Allocate(a, direct, 4, 128);
        try
        {
            b.WriteInt(0x01020304); b.EnsureWritable(1); Assert.Equal(64, b.Capacity); AssertUsage(a, direct, 64);
            b.Capacity = 2; AssertUsage(a, direct, 2); Assert.Equal(2, b.WriterIndex);
            Assert.Equal(1, b.GetByte(0)); Assert.Equal(2, b.GetByte(1));
            b.Capacity = 2; AssertUsage(a, direct, 2);
            Assert.Throws<ArgumentOutOfRangeException>(() => b.Capacity = 129);
            Assert.Throws<ArgumentOutOfRangeException>(() => b.Capacity = -1);
            AssertUsage(a, direct, 2);
            b.Capacity = 0; AssertUsage(a, direct, 0);
            b.WriteByte(9); AssertUsage(a, direct, b.Capacity);
        }
        finally { b.Release(); }
        AssertUsage(a, direct, 0);
        Assert.Throws<IllegalReferenceCountException>(() => b.Release()); AssertUsage(a, direct, 0);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ViewsRetainsAndCopiesChargeStorageExactlyOnce(bool direct)
    {
        var a = new UnpooledByteBufAllocator(); ByteBuf b = Allocate(a, direct, 8, 64).WriteLong(42);
        try
        {
            ByteBuf[] views = { b.Duplicate(), b.Slice(1, 4), b.AsReadOnly(), Unpooled.UnreleasableBuffer(b) };
            AssertUsage(a, direct, 8);
            foreach (ByteBuf view in views)
            {
                ByteBuf copy = view.Copy();
                try { AssertUsage(a, direct, 8 + view.ReadableBytes); }
                finally { copy.Release(); }
                AssertUsage(a, direct, 8);
            }
            ByteBuf retained = b.RetainedSlice(1, 4);
            Assert.False(b.Release()); AssertUsage(a, direct, 8);
            Assert.True(retained.Release()); AssertUsage(a, direct, 0);
        }
        finally { if (b.ReferenceCount > 0) b.Release(b.ReferenceCount); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DirectConstructorsAreUninstrumentedButCopiesUseAllocatorFactories(bool direct)
    {
        var a = new UnpooledByteBufAllocator();
        ByteBuf b = direct ? new UnpooledDirectByteBuf(a, 4, 64) : new UnpooledHeapByteBuf(a, 4, 64);
        try
        {
            b.WriteInt(42); b.Capacity = 8; AssertUsage(a, direct, 0);
            ByteBuf copy = b.Copy();
            try { AssertUsage(a, direct, 4); }
            finally { copy.Release(); }
            AssertUsage(a, direct, 0);
        }
        finally { b.Release(); }
        AssertUsage(a, direct, 0);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CompositeConsolidationPaddingAndCopyChargeOwnedComponents(bool direct)
    {
        var a = new UnpooledByteBufAllocator();
        CompositeByteBuf b = direct ? a.CompositeDirectBuffer(2) : a.CompositeHeapBuffer(2);
        try
        {
            for (int i = 0; i < 3; i++) b.AddComponent(Unpooled.WrappedBuffer(new byte[] { 1, 2 }), true);
            Assert.Equal(1, b.NumComponents); AssertUsage(a, direct, 6);
            b.Capacity = 9; AssertUsage(a, direct, 9);
            ByteBuf copy = b.Copy();
            try { AssertUsage(a, direct, 15); }
            finally { copy.Release(); }
            b.Capacity = 4;
            // A sliced component still owns the original six-byte consolidated allocation.
            AssertUsage(a, direct, 6);
        }
        finally { b.Release(); }
        AssertUsage(a, direct, 0);
    }

    [Fact]
    public void AllocatorsRemainSeparateWhenSharingNativeReservationsAndCompositeOwnership()
    {
        var native = new NativeMemoryAllocator(64);
        var a = new UnpooledByteBufAllocator(true, native); var other = new UnpooledByteBufAllocator(true, native);
        ByteBuf first = a.DirectBuffer(4, 8).WriteInt(1); ByteBuf second = other.DirectBuffer(8, 8).WriteLong(2);
        CompositeByteBuf composite = a.CompositeDirectBuffer();
        try
        {
            composite.AddComponent(first, true); composite.AddComponent(second, true);
            AssertUsage(a, true, 4); AssertUsage(other, true, 8); Assert.Equal(12, native.ReservedBytes);
            composite.Consolidate(); AssertUsage(a, true, 12); AssertUsage(other, true, 0);
        }
        finally { composite.Release(); }
        AssertUsage(a, true, 0); AssertUsage(other, true, 0); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void ZeroCapacityIsDistinctFromPhysicalMinimumAllocation()
    {
        var native = new NativeMemoryAllocator(8); var a = new UnpooledByteBufAllocator(true, native);
        ByteBuf empty = a.DirectBuffer(0, 0);
        Assert.Same(empty, a.HeapBuffer(0, 0)); AssertUsage(a, true, 0); Assert.Equal(0, native.ReservedBytes);
        ByteBuf b = a.DirectBuffer(0, 4);
        try { AssertUsage(a, true, 0); Assert.Equal(1, native.ReservedBytes); b.Capacity = 4; AssertUsage(a, true, 4); }
        finally { b.Release(); }
        AssertUsage(a, true, 0); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void FailedNativeAllocationGrowthAndCopyLeaveMetricsAndContentIntact()
    {
        var native = new NativeMemoryAllocator(8); var a = new UnpooledByteBufAllocator(true, native);
        ByteBuf b = a.DirectBuffer(4, 8).WriteInt(0x01020304);
        try
        {
            Assert.Throws<OutOfMemoryException>(() => a.DirectBuffer(8, 8));
            Assert.Throws<OutOfMemoryException>(() => b.Capacity = 8);
            AssertUsage(a, true, 4); Assert.Equal(4, native.ReservedBytes);
            ByteBuf full = a.DirectBuffer(4, 4);
            try { Assert.Throws<OutOfMemoryException>(() => b.Copy()); AssertUsage(a, true, 8); }
            finally { full.Release(); }
            Assert.Equal(0x01020304, b.GetInt(0)); Assert.Equal(4, b.WriterIndex); AssertUsage(a, true, 4);
        }
        finally { b.Release(); }
        AssertUsage(a, true, 0); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void UtilityReadFailureReleasesAlreadyAllocatedHeapCapacity()
    {
        var a = new UnpooledByteBufAllocator();
        ByteBuf source = Unpooled.WrappedBuffer(new byte[] { 1 });
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.ReadBytes(a, source, 2));
            AssertUsage(a, false, 0); Assert.Equal(0, source.ReaderIndex);
        }
        finally { source.Release(); }
    }

    [Fact]
    public void UtilityWriteFailureReleasesInitiallyAllocatedNativeCapacity()
    {
        var native = new NativeMemoryAllocator(4); var a = new UnpooledByteBufAllocator(true, native);
        Assert.Throws<OutOfMemoryException>(() => ByteBufUtil.WriteUtf8(a, new AsciiString("AB")));
        AssertUsage(a, true, 0); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void PinsKeepPhysicalStorageWhileResizeAndFinalReleaseUpdateOwnedCapacity()
    {
        var native = new NativeMemoryAllocator(32); var a = new UnpooledByteBufAllocator(true, native);
        ByteBuf b = a.DirectBuffer(4, 8).WriteInt(0x01020304);
        var oldPin = b.AsMemory(0, 4).Pin();
        try
        {
            b.Capacity = 8; AssertUsage(a, true, 8); Assert.Equal(12, native.ReservedBytes);
            var newPin = b.AsMemory(0, 8).Pin();
            try { b.Release(); AssertUsage(a, true, 0); Assert.Equal(12, native.ReservedBytes); }
            finally { newPin.Dispose(); }
            Assert.Equal(4, native.ReservedBytes);
        }
        finally { if (b.ReferenceCount > 0) b.Release(); oldPin.Dispose(); }
        AssertUsage(a, true, 0); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public async Task ConcurrentIndependentAllocationsAndReleasesDoNotLoseCounterUpdates()
    {
        var native = new NativeMemoryAllocator(64 * 1024); var a = new UnpooledByteBufAllocator(false, native);
        ByteBuf[] buffers = await Task.WhenAll(Enumerable.Range(0, 128).Select(i => Task.Run(() =>
        {
            ByteBuf b = Allocate(a, (i & 1) != 0, 32, 128); b.Capacity = 64; return b;
        }, TestContext.Current.CancellationToken)));
        try { Assert.Equal(4096, a.Metric.UsedHeapMemory); Assert.Equal(4096, a.Metric.UsedDirectMemory); }
        finally
        {
            await Task.WhenAll(buffers.Select(b => Task.Run(() => b.Release(), TestContext.Current.CancellationToken)));
        }
        AssertUsage(a, true, 0); Assert.Equal(0, native.ReservedBytes);
        ByteBuf shared = a.DirectBuffer(16, 16).Retain(63);
        await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() => shared.Release(), TestContext.Current.CancellationToken)));
        AssertUsage(a, true, 0); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FactoryPoliciesStableMetricIdentityAndFormatting(bool preferDirect)
    {
        var a = new UnpooledByteBufAllocator(preferDirect);
        Assert.Same(a.Metric, ((IByteBufAllocatorMetricProvider)a).Metric);
        ByteBuf b = a.Buffer(3, 3); ByteBuf io = a.IoBuffer(5, 5);
        try
        {
            Assert.Equal(preferDirect ? 0 : 3, a.Metric.UsedHeapMemory);
            Assert.Equal(preferDirect ? 8 : 5, a.Metric.UsedDirectMemory);
            Assert.Equal($"UnpooledByteBufAllocatorMetric(usedHeapMemory: {(preferDirect ? 0 : 3)}; usedDirectMemory: {(preferDirect ? 8 : 5)})", a.Metric.ToString());
        }
        finally { b.Release(); io.Release(); }
        AssertUsage(a, true, 0);
    }

    private static ByteBuf Allocate(UnpooledByteBufAllocator a, bool direct, int initial, int max)
        => direct ? a.DirectBuffer(initial, max) : a.HeapBuffer(initial, max);

    private static void AssertUsage(UnpooledByteBufAllocator a, bool direct, long expected)
    {
        Assert.Equal(direct ? 0 : expected, a.Metric.UsedHeapMemory);
        Assert.Equal(direct ? expected : 0, a.Metric.UsedDirectMemory);
    }
}
