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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer.Tests;

public abstract class ByteBufAllocatorTest : IDisposable
{
    // CLR: the pooled and adaptive allocators own arenas/chunks (IDisposable). Concrete NewAllocator factories register
    // each allocator here and the fixture disposes them after the test; the JVM fixture leaves them to the GC.
    private readonly List<IDisposable> _allocators = new();

    protected TAllocator Track<TAllocator>(TAllocator allocator)
    {
        if (allocator is IDisposable disposable)
        {
            _allocators.Add(disposable);
        }
        return allocator;
    }

    public virtual void Dispose()
    {
        foreach (IDisposable allocator in _allocators)
        {
            allocator.Dispose();
        }

        _allocators.Clear();
    }

    protected abstract int DefaultMaxCapacity();

    protected abstract int DefaultMaxComponents();

    protected abstract IByteBufAllocator NewAllocator(bool preferDirect);

    [Fact]
    public void TestBuffer()
    {
        TestBuffer0(true);
        TestBuffer0(false);
    }

    // CLR: xUnit1024 forbids overloading the TestBuffer test name.
    private void TestBuffer0(bool preferDirect)
    {
        IByteBufAllocator allocator = NewAllocator(preferDirect);
        ByteBuf buffer = allocator.Buffer(1);
        try
        {
            AssertBuffer(buffer, IsDirectExpected(preferDirect), 1, DefaultMaxCapacity());
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestBufferWithCapacity()
    {
        TestBufferWithCapacity0(true, 8);
        TestBufferWithCapacity0(false, 8);
    }

    // CLR: xUnit1024 forbids overloading the TestBufferWithCapacity test name.
    private void TestBufferWithCapacity0(bool preferDirect, int maxCapacity)
    {
        IByteBufAllocator allocator = NewAllocator(preferDirect);
        ByteBuf buffer = allocator.Buffer(1, maxCapacity);
        try
        {
            AssertBuffer(buffer, IsDirectExpected(preferDirect), 1, maxCapacity);
        }
        finally
        {
            buffer.Release();
        }
    }

    protected abstract bool IsDirectExpected(bool preferDirect);

    [Fact]
    public void TestHeapBuffer()
    {
        TestHeapBuffer0(true);
        TestHeapBuffer0(false);
    }

    // CLR: xUnit1024 forbids overloading the TestHeapBuffer test name.
    private void TestHeapBuffer0(bool preferDirect)
    {
        IByteBufAllocator allocator = NewAllocator(preferDirect);
        ByteBuf buffer = allocator.HeapBuffer(1);
        try
        {
            AssertBuffer(buffer, false, 1, DefaultMaxCapacity());
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestHeapBufferMaxCapacity()
    {
        TestHeapBuffer0(true, 8);
        TestHeapBuffer0(false, 8);
    }

    private void TestHeapBuffer0(bool preferDirect, int maxCapacity)
    {
        IByteBufAllocator allocator = NewAllocator(preferDirect);
        ByteBuf buffer = allocator.HeapBuffer(1, maxCapacity);
        try
        {
            AssertBuffer(buffer, false, 1, maxCapacity);
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestDirectBuffer()
    {
        TestDirectBuffer0(true);
        TestDirectBuffer0(false);
    }

    // CLR: xUnit1024 forbids overloading the TestDirectBuffer test name.
    private void TestDirectBuffer0(bool preferDirect)
    {
        IByteBufAllocator allocator = NewAllocator(preferDirect);
        ByteBuf buffer = allocator.DirectBuffer(1);
        try
        {
            AssertBuffer(buffer, true, 1, DefaultMaxCapacity());
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestDirectBufferMaxCapacity()
    {
        TestDirectBuffer0(true, 8);
        TestDirectBuffer0(false, 8);
    }

    private void TestDirectBuffer0(bool preferDirect, int maxCapacity)
    {
        IByteBufAllocator allocator = NewAllocator(preferDirect);
        ByteBuf buffer = allocator.DirectBuffer(1, maxCapacity);
        try
        {
            AssertBuffer(buffer, true, 1, maxCapacity);
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestCompositeBuffer()
    {
        TestCompositeBuffer0(true);
        TestCompositeBuffer0(false);
    }

    // CLR: xUnit1024 forbids overloading the TestCompositeBuffer test name.
    private void TestCompositeBuffer0(bool preferDirect)
    {
        IByteBufAllocator allocator = NewAllocator(preferDirect);
        CompositeByteBuf buffer = allocator.CompositeBuffer();
        try
        {
            AssertCompositeByteBuf(buffer, DefaultMaxComponents());
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestCompositeBufferWithCapacity()
    {
        TestCompositeHeapBufferWithCapacity0(true, 8);
        TestCompositeHeapBufferWithCapacity0(false, 8);
    }

    [Fact]
    public void TestCompositeHeapBuffer()
    {
        TestCompositeHeapBuffer0(true);
        TestCompositeHeapBuffer0(false);
    }

    // CLR: xUnit1024 forbids overloading the TestCompositeHeapBuffer test name.
    private void TestCompositeHeapBuffer0(bool preferDirect)
    {
        IByteBufAllocator allocator = NewAllocator(preferDirect);
        CompositeByteBuf buffer = allocator.CompositeHeapBuffer();
        try
        {
            AssertCompositeByteBuf(buffer, DefaultMaxComponents());
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestCompositeHeapBufferWithCapacity()
    {
        TestCompositeHeapBufferWithCapacity0(true, 8);
        TestCompositeHeapBufferWithCapacity0(false, 8);
    }

    // CLR: xUnit1024 forbids overloading the TestCompositeHeapBufferWithCapacity test name.
    private void TestCompositeHeapBufferWithCapacity0(bool preferDirect, int maxNumComponents)
    {
        IByteBufAllocator allocator = NewAllocator(preferDirect);
        CompositeByteBuf buffer = allocator.CompositeHeapBuffer(maxNumComponents);
        try
        {
            AssertCompositeByteBuf(buffer, maxNumComponents);
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestCompositeDirectBuffer()
    {
        TestCompositeDirectBuffer0(true);
        TestCompositeDirectBuffer0(false);
    }

    // CLR: xUnit1024 forbids overloading the TestCompositeDirectBuffer test name.
    private void TestCompositeDirectBuffer0(bool preferDirect)
    {
        IByteBufAllocator allocator = NewAllocator(preferDirect);
        CompositeByteBuf buffer = allocator.CompositeDirectBuffer();
        try
        {
            AssertCompositeByteBuf(buffer, DefaultMaxComponents());
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestCompositeDirectBufferWithCapacity()
    {
        TestCompositeDirectBufferWithCapacity0(true, 8);
        TestCompositeDirectBufferWithCapacity0(false, 8);
    }

    // CLR: xUnit1024 forbids overloading the TestCompositeDirectBufferWithCapacity test name.
    private void TestCompositeDirectBufferWithCapacity0(bool preferDirect, int maxNumComponents)
    {
        IByteBufAllocator allocator = NewAllocator(preferDirect);
        CompositeByteBuf buffer = allocator.CompositeDirectBuffer(maxNumComponents);
        try
        {
            AssertCompositeByteBuf(buffer, maxNumComponents);
        }
        finally
        {
            buffer.Release();
        }
    }

    private static void AssertBuffer(
            ByteBuf buffer, bool expectedDirect, int expectedCapacity, int expectedMaxCapacity)
    {
        Assert.Equal(expectedDirect, buffer.IsDirect);
        Assert.Equal(expectedCapacity, buffer.Capacity);
        Assert.Equal(expectedMaxCapacity, buffer.MaxCapacity);
    }

    private void AssertCompositeByteBuf(
            CompositeByteBuf buffer, int expectedMaxNumComponents)
    {
        Assert.Equal(0, buffer.NumComponents);
        Assert.Equal(expectedMaxNumComponents, buffer.MaxNumComponents);
        AssertBuffer(buffer, false, 0, DefaultMaxCapacity());
    }
}

// CLR: supplemental allocator-factory contracts that are not part of the original abstract fixture. They exercise the
// unpooled allocator and a recording AbstractByteBufAllocator once, instead of once per concrete allocator fixture.
public class ByteBufAllocatorClrContractTest
{
    // CLR: supplemental factory checks beyond the original assertBuffer: IoBuffer storage policy, explicit
    // maximums, the creating allocator, fresh cursors and claim, default and explicit initial capacities.
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FactoryBuffersKeepTheAllocatorCursorsAndClrIoPolicy(bool preferDirect)
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
                Assert.Equal(1, b.Capacity);
                    Assert.Equal(max, b.MaxCapacity);
                    Assert.Same(a, b.Alloc());
                Assert.Equal(0, b.ReaderIndex);
                    Assert.Equal(0, b.WriterIndex);
                    Assert.Equal(1, b.RefCnt);
                b.WriteByte(255);
                    Assert.Equal(255, b.ReadByte());
            }
            finally
            {
                b.Release();
            }
        }
        foreach (var (b, initial) in new[] { (a.Buffer(), 256), (a.HeapBuffer(), 256), (a.DirectBuffer(), 256), (a.IoBuffer(), 256), (a.Buffer(17), 17), (a.HeapBuffer(17), 17), (a.DirectBuffer(17), 17), (a.IoBuffer(17), 17) })
        {
            try
            {
                Assert.Equal(initial, b.Capacity);
                Assert.Equal(int.MaxValue, b.MaxCapacity);
                Assert.Same(a, b.Alloc());
            }
            finally
            {
                b.Release();
            }
        }
        Assert.False(a.IsDirectBufferPooled);
    }

    // CLR: supplemental composite factory checks: the creating allocator, a second explicit limit, and the
    // storage policy, allocator and copy of the first component allocated by growth.
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CompositeFactoriesAllocateComponentsAfterGrowth(bool preferDirect)
    {
        IByteBufAllocator a = new UnpooledByteBufAllocator(preferDirect);
        for (int kind = 0; kind < 3; kind++)
        foreach (int max in new[] { 8, 16 })
        {
            CompositeByteBuf b = kind switch { 0 => a.CompositeBuffer(max), 1 => a.CompositeHeapBuffer(max), _ => a.CompositeDirectBuffer(max) };
            try
            {
                Assert.Same(a, b.Alloc());
                    Assert.Equal(max, b.MaxNumComponents);
                    Assert.Equal(0, b.NumComponents);
                Assert.False(b.IsDirect);
                    Assert.Equal(0, b.Capacity);
                    Assert.Equal(int.MaxValue, b.MaxCapacity);
                b.Capacity = 9;
                Assert.Equal(kind == 0 ? preferDirect : kind == 2, b.IsDirect);
                Assert.Same(a, b.Component(0).Alloc());
                    b.WriteLong(0x0102030405060708);
                ByteBuf copy = b.Copy();
                try
                    {
                        Assert.Same(a, copy.Alloc());
                        Assert.Equal(b.IsDirect, copy.IsDirect);
                        Assert.Equal(ByteBufUtil.GetBytes(b), ByteBufUtil.GetBytes(copy));
                    }
                finally
                {
                    copy.Release();
                }
            }
            finally
            {
                b.Release();
            }
        }
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
        Assert.Equal(0, a.HeapCalls);
        Assert.Equal(0, a.DirectCalls);
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
        var a = new RecordingAllocator();
        var other = new RecordingAllocator();
        ByteBuf empty = a.Buffer(0, 0);
        Assert.Same(empty, a.HeapBuffer(0, 0));
        Assert.Same(empty, a.DirectBuffer(0, 0));
        Assert.Same(empty, a.IoBuffer(0, 0));
        Assert.NotSame(empty, other.Buffer(0, 0));
        Assert.Same(a, empty.Alloc());
        Assert.Equal(0, a.HeapCalls);
        Assert.Equal(0, a.DirectCalls);
        Assert.Same(empty, empty.Retain());
        Assert.False(empty.Release());
        Assert.Equal(1, empty.RefCnt);
        ByteBuf growable = a.HeapBuffer(0, 1);
        try
        {
            Assert.NotSame(empty, growable);
            Assert.Same(a, growable.Alloc());
            growable.WriteByte(4);
            Assert.Equal(1, growable.Capacity);
        }
        finally
        {
            growable.Release();
        }
        Assert.Same(Unpooled.EmptyBuffer, UnpooledByteBufAllocator.Default.DirectBuffer(0, 0));
        Assert.Same(IByteBufAllocator.Default, ByteBufUtil.DefaultAllocator);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ViewsCopiesAndGrowthKeepTheCreatingAllocator(bool direct)
    {
        var a = new RecordingAllocator();
        ByteBuf root = direct ? a.DirectBuffer(4, 64) : a.HeapBuffer(4, 64);
        try
        {
            root.WriteBytes(new byte[] { 1, 2, 3, 4 });
            ByteBuf[] views = { root, root.Duplicate(), root.Slice(1, 2), root.AsReadOnly(), Unpooled.UnreleasableBuffer(root), root.Slice(1, 2).Duplicate().AsReadOnly() };
            foreach (ByteBuf view in views)
            {
                Assert.Same(a, view.Alloc());
                int calls = direct ? a.DirectCalls : a.HeapCalls;
                ByteBuf copy = view.Copy();
                try
                {
                    Assert.Same(a, copy.Alloc());
                    Assert.Equal(direct, copy.IsDirect);
                    Assert.Equal(64, copy.MaxCapacity);
                    Assert.Equal(ByteBufUtil.GetBytes(view), ByteBufUtil.GetBytes(copy));
                }
                finally
                {
                    copy.Release();
                }
                Assert.Equal(calls + 1, direct ? a.DirectCalls : a.HeapCalls);
            }
            root.EnsureWritable(1);
            Assert.Equal(5, root.Capacity);
            Assert.Equal((5, 64), a.LastCalculation);
            Assert.Equal(2, root.EnsureWritable(2, false));
            Assert.Equal(6, root.Capacity);
            Assert.Equal(1, root.EnsureWritable(100, false));
            Assert.Equal(6, root.Capacity);
            Assert.Equal(3, root.EnsureWritable(100, true));
            Assert.Equal(64, root.Capacity);
            Assert.Equal(4, root.WriterIndex);
            Assert.Equal(1, root.RefCnt);
        }
        finally
        {
            root.Release();
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CompositePaddingConsolidationAndCopyUseTheAllocator(bool direct)
    {
        var a = new RecordingAllocator();
        CompositeByteBuf b = direct ? a.CompositeDirectBuffer(2) : a.CompositeHeapBuffer(2);
        try
        {
            b.AddComponent(Unpooled.WrappedBuffer(new byte[] { 1, 2 }), true);
            b.AddComponent(Unpooled.WrappedBuffer(new byte[] { 3, 4 }), true);
            b.AddComponent(Unpooled.WrappedBuffer(new byte[] { 5, 6 }), true);
            Assert.Equal(1, b.NumComponents);
            Assert.Equal(direct, b.IsDirect);
            Assert.Same(a, b.Component(0).Alloc());
            Assert.Equal(direct ? 1 : 0, a.DirectCalls);
            Assert.Equal(direct ? 0 : 1, a.HeapCalls);
            b.Capacity = 8;
            Assert.Same(a, b.Component(1).Alloc());
            ByteBuf copy = b.Slice(1, 4).Copy();
            try
            {
                Assert.Same(a, copy.Alloc());
                Assert.Equal(direct, copy.IsDirect);
                Assert.Equal(new byte[] { 2, 3, 4, 5 }, ByteBufUtil.GetBytes(copy));
            }
            finally
            {
                copy.Release();
            }
        }
        finally
        {
            b.Release();
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NativeReservationDomainIncludesCopiesAndCompositeAllocations(bool useLegacyConstructor)
    {
        var native = new NativeMemoryAllocator(16);
        var a = new UnpooledByteBufAllocator(true, native);
        ByteBuf b = useLegacyConstructor ? new UnpooledDirectByteBuf(4, 8, native) : a.DirectBuffer(4, 8);
        try
        {
            b.WriteInt(0x01020304);
            IByteBufAllocator policy = b.Alloc();
            ByteBuf copy = b.Duplicate().Copy();
            try
            {
                Assert.Same(policy, copy.Alloc());
                Assert.Equal(8, native.ReservedBytes);
            }
            finally
            {
                copy.Release();
            }
            b.Capacity = 8;
            Assert.Equal(8, native.ReservedBytes);
            ByteBuf full = policy.DirectBuffer(8, 8);
            try
            {
                Assert.Throws<OutOfDirectMemoryError>(() => b.Copy());
                Assert.Equal(16, native.ReservedBytes);
                Assert.Equal(8, b.Capacity);
                Assert.Equal(4, b.WriterIndex);
            }
            finally
            {
                full.Release();
            }
        }
        finally
        {
            b.Release();
        }
        Assert.Equal(0, native.ReservedBytes);
        CompositeByteBuf composite = useLegacyConstructor ? new CompositeByteBuf(16, true, native) : a.CompositeDirectBuffer();
        try
        {
            composite.Capacity = 8;
            Assert.Equal(8, native.ReservedBytes);
            Assert.Same(composite.Alloc(), composite.Component(0).Alloc());
        }
        finally
        {
            composite.Release();
        }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void FailedGrowthKeepsTheReservationWhileAnOperationLeaseIsActive()
    {
        var native = new NativeMemoryAllocator(8);
        var a = new UnpooledByteBufAllocator(true, native);
        ByteBuf b = a.DirectBuffer(4, 8).WriteInt(0x01020304);
        using var lease = b.AcquireReadLease();
        try
        {
            Assert.Throws<OutOfDirectMemoryError>(() => b.Capacity = 8);
            Assert.Equal(4, native.ReservedBytes);
            Assert.Equal(4, b.Capacity);
            Assert.Equal(4, b.WriterIndex);
            Assert.Equal(0x01020304, b.GetInt(0));
            Assert.Same(a, b.Alloc());
            b.Release();
            Assert.Equal(4, native.ReservedBytes);
        }
        finally
        {
            if (b.RefCnt > 0)
            {
                b.Release();
            }
            lease.Dispose();
        }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public async Task AllocatorFactoriesAreSafeForConcurrentIndependentBuffers()
    {
        var native = new NativeMemoryAllocator(4096);
        var a = new UnpooledByteBufAllocator(true, native);
        await Task.WhenAll(Enumerable.Range(0, 32).Select(i => Task.Run(() =>
        {
            for (int n = 0; n < 16; n++)
            {
                ByteBuf b = (i & 1) == 0 ? a.DirectBuffer(8, 16) : a.HeapBuffer(8, 16);
                try
                {
                    b.WriteLong(i);
                    Assert.Equal(i, b.ReadLong());
                    Assert.Same(a, b.Alloc());
                }
                finally
                {
                    b.Release();
                }
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
        {
            LastCalculation = (minimumNewCapacity, maxCapacity);
            return minimumNewCapacity;
        }

        protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity)
        {
            HeapCalls++;
            return new UnpooledHeapByteBuf(this, initialCapacity, maxCapacity);
        }

        protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
        {
            DirectCalls++;
            return new UnpooledDirectByteBuf(this, initialCapacity, maxCapacity);
        }
    }
}
