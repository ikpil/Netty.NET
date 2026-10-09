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

public class ByteBufAllocatorTest
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalFactoryScenariosAndClrIoPolicy(bool preferDirect)
    {
        IByteBufAllocator a = new UnpooledByteBufAllocator(preferDirect);
        Func<int, int, ByteBuf>[] factories = { (n, m) => a.Buffer(n, m), (n, m) => a.HeapBuffer(n, m), (n, m) => a.DirectBuffer(n, m), (n, m) => a.IoBuffer(n, m) };
        for (int kind = 0; kind < factories.Length; kind++)
        foreach (int max in new[] { 8, int.MaxValue })
        {
            ByteBuf b = factories[kind](1, max);
            try
            {
                Assert.Equal(kind == 0 ? preferDirect : kind >= 2, b.IsDirect);
                Assert.Equal(1, b.Capacity); Assert.Equal(max, b.MaxCapacity); Assert.Same(a, b.Allocator);
                Assert.Equal(0, b.ReaderIndex); Assert.Equal(0, b.WriterIndex); Assert.Equal(1, b.ReferenceCount);
                b.WriteByte(255); Assert.Equal(255, b.ReadByte());
            }
            finally { b.Release(); }
        }
        foreach (var (b, initial) in new[] { (a.Buffer(), 256), (a.HeapBuffer(), 256), (a.DirectBuffer(), 256), (a.IoBuffer(), 256), (a.Buffer(17), 17), (a.HeapBuffer(17), 17), (a.DirectBuffer(17), 17), (a.IoBuffer(17), 17) })
        {
            try { Assert.Equal(initial, b.Capacity); Assert.Equal(int.MaxValue, b.MaxCapacity); Assert.Same(a, b.Allocator); }
            finally { b.Release(); }
        }
        Assert.False(a.IsDirectBufferPooled);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalCompositeFactoryScenariosAndAllocationAfterGrowth(bool preferDirect)
    {
        IByteBufAllocator a = new UnpooledByteBufAllocator(preferDirect);
        for (int kind = 0; kind < 3; kind++)
        foreach (int max in new[] { 8, 16 })
        {
            CompositeByteBuf b = kind switch { 0 => a.CompositeBuffer(max), 1 => a.CompositeHeapBuffer(max), _ => a.CompositeDirectBuffer(max) };
            try
            {
                Assert.Same(a, b.Allocator); Assert.Equal(max, b.MaxNumComponents); Assert.Equal(0, b.NumComponents);
                Assert.False(b.IsDirect); Assert.Equal(0, b.Capacity); Assert.Equal(int.MaxValue, b.MaxCapacity);
                b.Capacity = 9;
                Assert.Equal(kind == 0 ? preferDirect : kind == 2, b.IsDirect);
                Assert.Same(a, b.Component(0).Allocator); b.WriteLong(0x0102030405060708);
                ByteBuf copy = b.Copy();
                try { Assert.Same(a, copy.Allocator); Assert.Equal(b.IsDirect, copy.IsDirect); Assert.Equal(ByteBufUtil.GetBytes(b), ByteBufUtil.GetBytes(copy)); }
                finally { copy.Release(); }
            }
            finally { b.Release(); }
        }
        foreach (CompositeByteBuf b in new[] { a.CompositeBuffer(), a.CompositeHeapBuffer(), a.CompositeDirectBuffer() })
        { Assert.Equal(16, b.MaxNumComponents); b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalCapacityCalculationScenarios(bool preferDirect)
    {
        var a = new UnpooledByteBufAllocator(preferDirect); const int threshold = 4 * 1024 * 1024;
        Assert.Equal(8, a.CalculateNewCapacity(1, 8)); Assert.Equal(7, a.CalculateNewCapacity(1, 7));
        Assert.Equal(64, a.CalculateNewCapacity(1, 129));
        Assert.Equal(threshold, a.CalculateNewCapacity(threshold, threshold + 1));
        Assert.Equal(threshold * 2, a.CalculateNewCapacity(threshold + 1, threshold * 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => a.CalculateNewCapacity(8, 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => a.CalculateNewCapacity(-1, 8));
        Assert.Equal(int.MaxValue, a.CalculateNewCapacity(int.MaxValue - 1, int.MaxValue));
        Assert.Equal(int.MaxValue, a.CalculateNewCapacity(int.MaxValue, int.MaxValue));
    }

    [Theory]
    [InlineData(-1, 8)] [InlineData(1, 0)] [InlineData(0, -1)] [InlineData(8, 7)]
    public void InvalidCapacityCannotReachAllocationHooks(int initial, int max)
    {
        var a = new RecordingAllocator();
        Assert.Throws<ArgumentOutOfRangeException>(() => a.Buffer(initial, max));
        Assert.Throws<ArgumentOutOfRangeException>(() => a.HeapBuffer(initial, max));
        Assert.Throws<ArgumentOutOfRangeException>(() => a.DirectBuffer(initial, max));
        Assert.Throws<ArgumentOutOfRangeException>(() => a.IoBuffer(initial, max));
        Assert.Equal(0, a.HeapCalls); Assert.Equal(0, a.DirectCalls);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(int.MinValue)]
    public void InvalidCompositeLimitsThrow(int max)
    {
        var a = new UnpooledByteBufAllocator();
        Assert.Throws<ArgumentOutOfRangeException>(() => a.CompositeBuffer(max));
        Assert.Throws<ArgumentOutOfRangeException>(() => a.CompositeHeapBuffer(max));
        Assert.Throws<ArgumentOutOfRangeException>(() => a.CompositeDirectBuffer(max));
    }

    [Fact]
    public void EmptySentinelBelongsToAllocatorAndHasNoAllocationOrOwnershipCost()
    {
        var a = new RecordingAllocator(); var other = new RecordingAllocator();
        ByteBuf empty = a.Buffer(0, 0);
        Assert.Same(empty, a.HeapBuffer(0, 0)); Assert.Same(empty, a.DirectBuffer(0, 0)); Assert.Same(empty, a.IoBuffer(0, 0));
        Assert.NotSame(empty, other.Buffer(0, 0)); Assert.Same(a, empty.Allocator);
        Assert.Equal(0, a.HeapCalls); Assert.Equal(0, a.DirectCalls);
        Assert.Same(empty, empty.Retain()); Assert.False(empty.Release()); Assert.Equal(1, empty.ReferenceCount);
        ByteBuf growable = a.HeapBuffer(0, 1);
        try { Assert.NotSame(empty, growable); Assert.Same(a, growable.Allocator); growable.WriteByte(4); Assert.Equal(1, growable.Capacity); }
        finally { growable.Release(); }
        Assert.Same(Unpooled.EmptyBuffer, UnpooledByteBufAllocator.Default.DirectBuffer(0, 0));
        Assert.Same(IByteBufAllocator.Default, UnpooledByteBufAllocator.Default);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ViewsCopiesAndGrowthKeepTheCreatingAllocator(bool direct)
    {
        var a = new RecordingAllocator(); ByteBuf root = direct ? a.DirectBuffer(4, 64) : a.HeapBuffer(4, 64);
        try
        {
            root.WriteBytes(new byte[] { 1, 2, 3, 4 });
            ByteBuf[] views = { root, root.Duplicate(), root.Slice(1, 2), root.AsReadOnly(), Unpooled.UnreleasableBuffer(root), root.Slice(1, 2).Duplicate().AsReadOnly() };
            foreach (ByteBuf view in views)
            {
                Assert.Same(a, view.Allocator); int calls = direct ? a.DirectCalls : a.HeapCalls;
                ByteBuf copy = view.Copy();
                try { Assert.Same(a, copy.Allocator); Assert.Equal(direct, copy.IsDirect); Assert.Equal(64, copy.MaxCapacity); Assert.Equal(ByteBufUtil.GetBytes(view), ByteBufUtil.GetBytes(copy)); }
                finally { copy.Release(); }
                Assert.Equal(calls + 1, direct ? a.DirectCalls : a.HeapCalls);
            }
            root.EnsureWritable(1); Assert.Equal(5, root.Capacity); Assert.Equal((5, 64), a.LastCalculation);
            Assert.Equal(2, root.EnsureWritable(2, false)); Assert.Equal(6, root.Capacity);
            Assert.Equal(1, root.EnsureWritable(100, false)); Assert.Equal(6, root.Capacity);
            Assert.Equal(3, root.EnsureWritable(100, true)); Assert.Equal(64, root.Capacity);
            Assert.Equal(4, root.WriterIndex); Assert.Equal(1, root.ReferenceCount);
        }
        finally { root.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CompositePaddingConsolidationAndCopyUseTheAllocator(bool direct)
    {
        var a = new RecordingAllocator(); CompositeByteBuf b = direct ? a.CompositeDirectBuffer(2) : a.CompositeHeapBuffer(2);
        try
        {
            b.AddComponent(Unpooled.WrappedBuffer(new byte[] { 1, 2 }), true);
            b.AddComponent(Unpooled.WrappedBuffer(new byte[] { 3, 4 }), true);
            b.AddComponent(Unpooled.WrappedBuffer(new byte[] { 5, 6 }), true);
            Assert.Equal(1, b.NumComponents); Assert.Equal(direct, b.IsDirect); Assert.Same(a, b.Component(0).Allocator);
            Assert.Equal(direct ? 1 : 0, a.DirectCalls); Assert.Equal(direct ? 0 : 1, a.HeapCalls);
            b.Capacity = 8; Assert.Same(a, b.Component(1).Allocator);
            ByteBuf copy = b.Slice(1, 4).Copy();
            try { Assert.Same(a, copy.Allocator); Assert.Equal(direct, copy.IsDirect); Assert.Equal(new byte[] { 2, 3, 4, 5 }, ByteBufUtil.GetBytes(copy)); }
            finally { copy.Release(); }
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NativeReservationDomainIncludesCopiesAndCompositeAllocations(bool useLegacyConstructor)
    {
        var native = new NativeMemoryAllocator(16); var a = new UnpooledByteBufAllocator(true, native);
        ByteBuf b = useLegacyConstructor ? new UnpooledDirectByteBuf(4, 8, native) : a.DirectBuffer(4, 8);
        try
        {
            b.WriteInt(0x01020304); IByteBufAllocator policy = b.Allocator;
            ByteBuf copy = b.Duplicate().Copy();
            try { Assert.Same(policy, copy.Allocator); Assert.Equal(8, native.ReservedBytes); }
            finally { copy.Release(); }
            b.Capacity = 8; Assert.Equal(8, native.ReservedBytes);
            ByteBuf full = policy.DirectBuffer(8, 8);
            try
            {
                Assert.Throws<OutOfMemoryException>(() => b.Copy());
                Assert.Equal(16, native.ReservedBytes); Assert.Equal(8, b.Capacity); Assert.Equal(4, b.WriterIndex);
            }
            finally { full.Release(); }
        }
        finally { b.Release(); }
        Assert.Equal(0, native.ReservedBytes);
        CompositeByteBuf composite = useLegacyConstructor ? new CompositeByteBuf(16, true, native) : a.CompositeDirectBuffer();
        try { composite.Capacity = 8; Assert.Equal(8, native.ReservedBytes); Assert.Same(composite.Allocator, composite.Component(0).Allocator); }
        finally { composite.Release(); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void FailedNativeGrowthAndPinsKeepTheirReservationSemantics()
    {
        var native = new NativeMemoryAllocator(8); var a = new UnpooledByteBufAllocator(true, native);
        ByteBuf b = a.DirectBuffer(4, 8).WriteInt(0x01020304);
        var pin = b.AsMemory(0, 4).Pin();
        try
        {
            Assert.Throws<OutOfMemoryException>(() => b.Capacity = 8);
            Assert.Equal(4, native.ReservedBytes); Assert.Equal(4, b.Capacity); Assert.Equal(4, b.WriterIndex);
            Assert.Equal(0x01020304, b.GetInt(0)); Assert.Same(a, b.Allocator);
            b.Release(); Assert.Equal(4, native.ReservedBytes);
        }
        finally { if (b.ReferenceCount > 0) b.Release(); pin.Dispose(); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public async Task AllocatorFactoriesAreSafeForConcurrentIndependentBuffers()
    {
        var native = new NativeMemoryAllocator(4096); var a = new UnpooledByteBufAllocator(true, native);
        await Task.WhenAll(Enumerable.Range(0, 32).Select(i => Task.Run(() =>
        {
            for (int n = 0; n < 16; n++)
            {
                ByteBuf b = (i & 1) == 0 ? a.DirectBuffer(8, 16) : a.HeapBuffer(8, 16);
                try { b.WriteLong(i); Assert.Equal(i, b.ReadLong()); Assert.Same(a, b.Allocator); }
                finally { b.Release(); }
            }
        }, TestContext.Current.CancellationToken)));
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void ExplicitConstructorsRejectNullAllocator()
    {
        Assert.Throws<ArgumentNullException>(() => new UnpooledHeapByteBuf(null, 1, 8));
        Assert.Throws<ArgumentNullException>(() => new UnpooledDirectByteBuf(null, 1, 8));
        Assert.Throws<ArgumentNullException>(() => new CompositeByteBuf(null, false));
    }

    private sealed class RecordingAllocator : AbstractByteBufAllocator
    {
        public int HeapCalls { get; private set; }
        public int DirectCalls { get; private set; }
        public (int Minimum, int Maximum) LastCalculation { get; private set; }
        public override bool IsDirectBufferPooled => false;
        public override int CalculateNewCapacity(int minimumNewCapacity, int maxCapacity)
        { LastCalculation = (minimumNewCapacity, maxCapacity); return minimumNewCapacity; }
        protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity)
        { HeapCalls++; return new UnpooledHeapByteBuf(this, initialCapacity, maxCapacity); }
        protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
        { DirectCalls++; return new UnpooledDirectByteBuf(this, initialCapacity, maxCapacity); }
    }
}
