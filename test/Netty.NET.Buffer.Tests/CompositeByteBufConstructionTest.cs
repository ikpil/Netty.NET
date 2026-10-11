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
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class CompositeByteBufConstructionTest
{
    private static CompositeByteBuf Construct(IByteBufAllocator allocator, bool direct, int limit,
        ByteBuf[] buffers, bool enumerable) => enumerable
        ? new CompositeByteBuf(allocator, direct, limit, (IEnumerable<ByteBuf>)buffers)
        : new CompositeByteBuf(allocator, direct, limit, buffers);

    [Theory]
    [InlineData(false, false, 1)] [InlineData(false, true, 1)]
    [InlineData(true, false, 1)] [InlineData(true, true, 1)]
    [InlineData(false, false, 8)] [InlineData(false, true, 8)]
    [InlineData(true, false, 8)] [InlineData(true, true, 8)]
    public void CapturedRangesAreReadableAndConsolidationUsesCreatorPolicy(bool enumerable, bool direct, int limit)
    {
        var native = new NativeMemoryAllocator(64);
        var allocator = new UnpooledByteBufAllocator(false, native);
        byte[] external = [8, 1, 2, 9];
        ByteBuf first = Unpooled.WrappedBuffer(external).SetIndex(1, 3).AsReadOnly();
        ByteBuf empty = allocator.HeapBuffer(0, 1);
        ByteBuf second = allocator.DirectBuffer(6, 6).WriteBytes(new byte[] { 8, 8, 3, 4, 9, 9 }).SetIndex(2, 4);
        first.MarkReaderIndex().MarkWriterIndex(); second.MarkReaderIndex().MarkWriterIndex();
        CompositeByteBuf composite = Construct(allocator, direct, limit, [first, empty, second], enumerable);
        try
        {
            Assert.Same(allocator, composite.Allocator);
            Assert.Equal(limit, composite.MaxNumComponents);
            Assert.Equal(limit == 1 ? 1 : 3, composite.NumComponents);
            Assert.Equal(4, composite.Capacity);
            Assert.Equal(int.MaxValue, composite.MaxCapacity);
            Assert.Equal(0, composite.ReaderIndex); Assert.Equal(4, composite.WriterIndex);
            Assert.Equal("01020304", ByteBufUtil.HexDump(composite));
            Assert.Equal(1, first.ReaderIndex); Assert.Equal(3, first.WriterIndex);
            Assert.Equal(2, second.ReaderIndex); Assert.Equal(4, second.WriterIndex);
            Assert.Equal(limit == 1 ? 0 : 1, first.ReferenceCount);
            Assert.Equal(limit == 1 ? 0 : 1, empty.ReferenceCount);
            Assert.Equal(limit == 1 ? 0 : 1, second.ReferenceCount);
            Assert.Equal(limit == 1 && direct, composite.IsDirect);
            external[1] = 99;
            Assert.Equal(limit == 1 ? 1 : 99, composite.GetByte(0));
            if (limit == 1)
            {
                composite.SetByte(0, 7);
                Assert.Equal(99, external[1]);
                Assert.Equal(direct ? 0 : 4, allocator.Metric.UsedHeapMemory);
                Assert.Equal(direct ? 4 : 0, allocator.Metric.UsedDirectMemory);
            }
            else
            {
                Assert.Throws<NotSupportedException>(() => composite.SetByte(0, 7));
                first.SetIndex(0, 4).ResetReaderIndex().ResetWriterIndex();
                second.SetIndex(0, 6).ResetReaderIndex().ResetWriterIndex();
                Assert.Equal(1, first.ReaderIndex); Assert.Equal(3, first.WriterIndex);
                Assert.Equal(2, second.ReaderIndex); Assert.Equal(4, second.WriterIndex);
                Assert.Equal("63020304", ByteBufUtil.HexDump(composite));
            }
        }
        finally { composite.Release(); }
        Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, empty.ReferenceCount); Assert.Equal(0, second.ReferenceCount);
        Assert.Equal(0, native.ReservedBytes);
        Assert.Equal(0, allocator.Metric.UsedHeapMemory); Assert.Equal(0, allocator.Metric.UsedDirectMemory);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EmptyInputsAndOwnedEmptyComponentsRemainDistinct(bool enumerable)
    {
        var allocator = new UnpooledByteBufAllocator();
        CompositeByteBuf empty = Construct(allocator, true, 1, [], enumerable);
        Assert.Equal(0, empty.NumComponents); Assert.Equal(0, empty.ReadableBytes);
        Assert.Equal(1, empty.ReferenceCount); Assert.False(empty.IsDirect);
        Assert.True(empty.Release());

        ByteBuf first = allocator.HeapBuffer(0, 1), second = allocator.HeapBuffer(0, 1);
        CompositeByteBuf owned = Construct(allocator, false, 8, [first, second], enumerable);
        Assert.Equal(2, owned.NumComponents); Assert.Equal(0, owned.WriterIndex);
        Assert.Equal(1, first.ReferenceCount); Assert.Equal(1, second.ReferenceCount);
        Assert.True(owned.Release());
        Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, second.ReferenceCount);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NullTerminatesInsertionAndReleasesTrailingReferences(bool enumerable)
    {
        // See https://github.com/netty/netty/issues/11612
        ByteBuf prefix = Unpooled.WrappedBuffer(new byte[] { 1, 2 });
        ByteBuf tail = Unpooled.Buffer(1, 1).WriteByte(3);
        CompositeByteBuf composite = Construct(UnpooledByteBufAllocator.Default, false, 8,
            [prefix, null, tail, null], enumerable);
        try
        {
            Assert.Equal(1, composite.NumComponents);
            Assert.Equal("0102", ByteBufUtil.HexDump(composite));
            Assert.Equal(1, prefix.ReferenceCount); Assert.Equal(0, tail.ReferenceCount);
        }
        finally { composite.Release(); }
        Assert.Equal(0, prefix.ReferenceCount);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InvalidConstructorArgumentsLeaveInputsCallerOwned(bool enumerable)
    {
        ByteBuf input = Unpooled.WrappedBuffer(new byte[] { 1 });
        try
        {
            Assert.Throws<ArgumentNullException>(() => Construct(null, false, 8, [input], enumerable));
            Assert.Throws<ArgumentOutOfRangeException>(() => Construct(UnpooledByteBufAllocator.Default, false, 0, [input], enumerable));
            Assert.Throws<ArgumentOutOfRangeException>(() => Construct(UnpooledByteBufAllocator.Default, false, -1, [input], enumerable));
            Assert.Equal(1, input.ReferenceCount);
            Assert.Throws<ArgumentNullException>(() => Construct(UnpooledByteBufAllocator.Default, false, 8, null, enumerable));
        }
        finally { input.Release(); }
    }

    [Fact]
    public void ParamsOverloadAndSingleCompositeEnumerableTransferReferencesWithoutRetain()
    {
        var allocator = new UnpooledByteBufAllocator();
        ByteBuf a = allocator.HeapBuffer(1, 1).WriteByte(1), b = allocator.HeapBuffer(1, 1).WriteByte(2);
        var inner = new CompositeByteBuf(allocator, false, 8, a, b);
        CompositeByteBuf outer = new(allocator, false, 8, (IEnumerable<ByteBuf>)inner);
        try
        {
            Assert.Equal(2, inner.NumComponents); Assert.Equal(1, outer.NumComponents);
            Assert.Equal("0102", ByteBufUtil.HexDump(outer));
            Assert.Equal(1, inner.ReferenceCount); Assert.Equal(1, a.ReferenceCount); Assert.Equal(1, b.ReferenceCount);
        }
        finally { outer.Release(); }
        Assert.Equal(0, inner.ReferenceCount); Assert.Equal(0, a.ReferenceCount); Assert.Equal(0, b.ReferenceCount);
        Assert.Equal(0, allocator.Metric.UsedHeapMemory);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LaterInsertionFailureReleasesAcquiredPrefixAndRemainingInputs(bool enumerable)
    {
        var native = new NativeMemoryAllocator(16);
        var allocator = new UnpooledByteBufAllocator(true, native);
        ByteBuf prefix = allocator.DirectBuffer(2, 2).WriteShort(1);
        ByteBuf dead = allocator.DirectBuffer(2, 2).WriteShort(2);
        ByteBuf tail = allocator.DirectBuffer(2, 2).WriteShort(3);
        dead.Release();
        Assert.Throws<IllegalReferenceCountException>(() => Construct(allocator, true, 8, [prefix, dead, tail], enumerable));
        Assert.Equal(0, prefix.ReferenceCount); Assert.Equal(0, tail.ReferenceCount);
        Assert.Equal(0, native.ReservedBytes); Assert.Equal(0, allocator.Metric.UsedDirectMemory);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ConsolidationAllocationFailureReleasesAllInputsAndAllowsRetry(bool enumerable)
    {
        var native = new NativeMemoryAllocator(3);
        var allocator = new UnpooledByteBufAllocator(true, native);
        ByteBuf[] inputs = [allocator.DirectBuffer(1, 1).WriteByte(1), allocator.DirectBuffer(1, 1).WriteByte(2),
            allocator.DirectBuffer(1, 1).WriteByte(3)];
        Assert.Throws<OutOfMemoryException>(() => Construct(allocator, true, 1, inputs, enumerable));
        foreach (ByteBuf input in inputs) Assert.Equal(0, input.ReferenceCount);
        Assert.Equal(0, native.ReservedBytes); Assert.Equal(0, allocator.Metric.UsedDirectMemory);
        ByteBuf[] retryInputs = [allocator.DirectBuffer(1, 1).WriteByte(1), allocator.DirectBuffer(1, 1).WriteByte(2),
            allocator.DirectBuffer(1, 1).WriteByte(3)];
        CompositeByteBuf retry = Construct(allocator, true, 8, retryInputs, enumerable);
        try { Assert.Equal("010203", ByteBufUtil.HexDump(retry)); }
        finally { retry.Release(); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CopyFailureReleasesTemporaryReplacementAndAllTransferredInputs(bool enumerable)
    {
        var native = new NativeMemoryAllocator(8);
        var allocator = new UnpooledByteBufAllocator(true, native);
        ByteBuf first = allocator.DirectBuffer(2, 4).WriteBytes(new byte[] { 1, 2 });
        ByteBuf second = allocator.DirectBuffer(2, 4).WriteBytes(new byte[] { 3, 4 });
        var stream = new ShrinkingEnumerable(first, second);
        if (!enumerable)
        {
            // Array insertion can also observe mutation while capturing the next component's range.
            second = new ObservingBuffer(second, () => first.Capacity = 1);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            if (enumerable) _ = new CompositeByteBuf(allocator, true, 1, stream);
            else _ = new CompositeByteBuf(allocator, true, 1, first, second);
        });
        Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, second.ReferenceCount);
        Assert.Equal(0, native.ReservedBytes); Assert.Equal(0, allocator.Metric.UsedDirectMemory);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OverflowIsDetectedWithoutLargeAllocationsAndRespectsInputForm(bool enumerable)
    {
        ByteBuf first = new LogicalBuffer(int.MaxValue), second = new LogicalBuffer(1);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Construct(UnpooledByteBufAllocator.Default, false,
                int.MaxValue, [first, second], enumerable));
            Assert.Equal(enumerable ? 0 : 1, first.ReferenceCount);
            Assert.Equal(enumerable ? 0 : 1, second.ReferenceCount);
        }
        finally
        {
            if (first.ReferenceCount > 0) first.Release();
            if (second.ReferenceCount > 0) second.Release();
        }
    }

    [Fact]
    public void ThrowingEnumeratorIsDisposedAndAcquiredNativeReferencesAreReleased()
    {
        var native = new NativeMemoryAllocator(16);
        var allocator = new UnpooledByteBufAllocator(true, native);
        ByteBuf first = allocator.DirectBuffer(1, 1).WriteByte(1), tail = allocator.DirectBuffer(1, 1).WriteByte(2);
        var failure = new InvalidOperationException("iteration failed");
        var stream = new FailingEnumerable(first, tail, failure, false);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => new CompositeByteBuf(allocator, true, 8, stream)));
        Assert.Equal(1, stream.Disposals);
        Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, tail.ReferenceCount);
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void UnyieldedInputsStayCallerOwnedWhenEnumerationCannotRecover()
    {
        var allocator = new UnpooledByteBufAllocator();
        ByteBuf first = allocator.HeapBuffer(1, 1).WriteByte(1), tail = allocator.HeapBuffer(1, 1).WriteByte(2);
        var failure = new InvalidOperationException("iteration failed");
        var stream = new FailingEnumerable(first, tail, failure, true);
        try
        {
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => new CompositeByteBuf(allocator, false, 8, stream)));
            Assert.Equal(1, stream.Disposals);
            Assert.Equal(0, first.ReferenceCount); Assert.Equal(1, tail.ReferenceCount);
        }
        finally { tail.Release(); }
        Assert.Equal(0, allocator.Metric.UsedHeapMemory);
    }

    [Fact]
    public void ConstructorCleanupDoesNotDispatchDerivedDeallocate()
    {
        var allocator = new UnpooledByteBufAllocator();
        ByteBuf first = allocator.HeapBuffer(1, 1).WriteByte(1), dead = allocator.HeapBuffer(1, 1).WriteByte(2);
        dead.Release();
        Assert.Throws<IllegalReferenceCountException>(() => new DerivedComposite(allocator, [first, dead]));
        Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, allocator.Metric.UsedHeapMemory);
    }

    private sealed class DerivedComposite(IByteBufAllocator allocator, ByteBuf[] buffers)
        : CompositeByteBuf(allocator, false, 8, buffers)
    {
        protected override void Deallocate() => throw new InvalidOperationException("Derived state is not initialized.");
    }

    private sealed class LogicalBuffer : AbstractReferenceCountedByteBuf
    {
        internal LogicalBuffer(int length) : base(length) { Capacity = length; SetIndex(0, length); }
        public override int Capacity { get; set; }
        public override bool IsDirect => false;
        protected override bool TryGetReadOnlyMemoryCore(int index, int length, out ReadOnlyMemory<byte> memory)
        { memory = default; return false; }
        protected override Memory<byte> GetMemoryCore(int index, int length) => throw new InvalidOperationException("No physical allocation.");
        protected override void Deallocate() { }
    }

    private sealed class ObservingBuffer : ByteBuf
    {
        private readonly ByteBuf _source;
        private readonly Action _action;
        private bool _observed;
        internal ObservingBuffer(ByteBuf source, Action action) : base(source.MaxCapacity)
        {
            _source = source; _action = action;
            SetIndex(source.ReaderIndex, source.WriterIndex);
        }
        public override int Capacity { get => _source.Capacity; set => _source.Capacity = value; }
        public override bool IsDirect => _source.IsDirect;
        public override int ReferenceCount => _source.ReferenceCount;
        public override ByteBuf Retain(int increment = 1) { _source.Retain(increment); return this; }
        public override bool Release(int decrement = 1) => _source.Release(decrement);
        protected override Memory<byte> GetMemoryCore(int index, int length)
        {
            if (!_observed) { _observed = true; _action(); }
            return _source.AsMemory(index, length);
        }
    }

    private sealed class ShrinkingEnumerable(ByteBuf first, ByteBuf second) : IEnumerable<ByteBuf>
    {
        public IEnumerator<ByteBuf> GetEnumerator()
        {
            yield return first;
            first.Capacity = 1;
            yield return second;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class FailingEnumerable(ByteBuf first, ByteBuf tail, Exception failure, bool persistent) : IEnumerable<ByteBuf>
    {
        internal int Disposals;
        public IEnumerator<ByteBuf> GetEnumerator() => new Enumerator(this, first, tail, failure, persistent);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        private sealed class Enumerator(FailingEnumerable owner, ByteBuf first, ByteBuf tail, Exception failure, bool persistent) : IEnumerator<ByteBuf>
        {
            private int _position;
            private bool _failed;
            public ByteBuf Current => _position == 1 ? first : tail;
            object IEnumerator.Current => Current;
            public bool MoveNext()
            {
                if (_position == 1 && (persistent || !_failed)) { _failed = true; throw failure; }
                return ++_position <= 2;
            }
            public void Reset() => throw new NotSupportedException();
            public void Dispose() => owner.Disposals++;
        }
    }
}
