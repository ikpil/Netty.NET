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
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class UnpooledMemoryWrappingTest
{
    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(2, false)] [InlineData(2, true)]
    public void OriginalMultipleRangesAliasCapturedStorageAndSkipEmpties(int kind, bool readOnly)
    {
        using var storage = new ExternalStorage(kind, 16);
        for (int i = 0; i < 16; i++) storage.Memory.Span[i] = (byte)i;
        Memory<byte>[] ranges = { default, storage.Memory.Slice(2, 3), storage.Memory.Slice(16, 0), storage.Memory.Slice(8, 4), default };
        ByteBuf result = Wrap(ranges, readOnly);
        try
        {
            var composite = Assert.IsType<CompositeByteBuf>(result);
            Assert.Equal(2, composite.NumComponents); Assert.Equal(5, composite.MaxNumComponents);
            Assert.Equal(7, result.Capacity); Assert.Equal(int.MaxValue, result.MaxCapacity);
            Assert.Equal(0, result.ReaderIndex); Assert.Equal(7, result.WriterIndex); Assert.Equal(1, result.ReferenceCount);
            Assert.False(result.IsReadOnly); Assert.Equal(kind == 1, result.IsDirect);
            Assert.Equal(new byte[] { 2, 3, 4, 8, 9, 10, 11 }, ByteBufUtil.GetBytes(result));
            storage.Memory.Span[2] = 99; Assert.Equal(99, result.GetByte(0));
            Assert.Equal(readOnly, composite.Component(0).IsReadOnly);
            if (readOnly) Assert.Throws<NotSupportedException>(() => result.SetByte(0, 42));
            else { result.SetByte(0, 42); Assert.Equal(42, storage.Memory.Span[2]); }
            Assert.Equal(3, ranges[1].Length); Assert.Equal(4, ranges[3].Length);
            Assert.Equal(1, storage.Memory.Span[1]); Assert.Equal(12, storage.Memory.Span[12]);
        }
        finally { result.Release(); }
        Assert.Equal(readOnly ? 99 : 42, storage.Memory.Span[2]);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(2, false)] [InlineData(2, true)]
    public void OriginalComponentLimitConsolidatesOnceAndDetachesAllRanges(int kind, bool readOnly)
    {
        using var storage = new ExternalStorage(kind, 6);
        var ranges = new Memory<byte>[6];
        for (int i = 0; i < 6; i++) { storage.Memory.Span[i] = (byte)(i + 1); ranges[i] = storage.Memory.Slice(i, 1); }
        ByteBuf result = Wrap(ranges, readOnly, 2);
        try
        {
            var composite = Assert.IsType<CompositeByteBuf>(result);
            Assert.Equal(1, composite.NumComponents); Assert.Equal(2, composite.MaxNumComponents);
            Assert.False(result.IsDirect); Assert.False(composite.Component(0).IsReadOnly);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, ByteBufUtil.GetBytes(result));
            storage.Memory.Span[0] = 99; Assert.Equal(1, result.GetByte(0));
            result.SetByte(1, 42); Assert.Equal(2, storage.Memory.Span[1]);
            Assert.Equal(6, result.WriterIndex);
        }
        finally { result.Release(); }
        Assert.Equal(99, storage.Memory.Span[0]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalArityDistinguishesSingleRootFromCompositeWithOneNonemptyRange(bool readOnly)
    {
        using var storage = new ExternalStorage(1, 8);
        Memory<byte> range = storage.Memory.Slice(2, 4);
        ByteBuf single = Wrap(new[] { range }, readOnly, -1);
        ByteBuf multiple = Wrap(new[] { Memory<byte>.Empty, range }, readOnly, 1);
        try
        {
            Assert.IsNotType<CompositeByteBuf>(single); Assert.Equal(4, single.MaxCapacity); Assert.Equal(readOnly, single.IsReadOnly);
            var composite = Assert.IsType<CompositeByteBuf>(multiple);
            Assert.Equal(1, composite.NumComponents); Assert.Equal(int.MaxValue, multiple.MaxCapacity);
            Assert.False(multiple.IsReadOnly); Assert.Equal(readOnly, composite.Component(0).IsReadOnly);
            Assert.True(single.IsDirect); Assert.True(multiple.IsDirect);
        }
        finally { single.Release(); multiple.Release(); }
        storage.Memory.Span[0] = 42;
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalEmptyRangesReturnSharedSentinelEvenWithUnusedInvalidLimit(bool readOnly)
    {
        using var storage = new ExternalStorage(1, 8);
        Memory<byte> empty = storage.Memory.Slice(8, 0);
        Assert.Same(Unpooled.EmptyBuffer, Wrap(Array.Empty<Memory<byte>>(), readOnly, -1));
        Assert.Same(Unpooled.EmptyBuffer, Wrap(new[] { empty }, readOnly, -1));
        Assert.Same(Unpooled.EmptyBuffer, Wrap(new[] { empty, empty }, readOnly, 0));
        Assert.Same(Unpooled.EmptyBuffer, Wrap(new[] { default(Memory<byte>), empty, default }, readOnly));
        storage.Memory.Span[0] = 42;
    }

    [Fact]
    public void OriginalSingleAndMultipleNioArrayScenariosProduceEqualContent()
    {
        Memory<byte> a = new byte[] { 1 }, b = new byte[] { 2 }, c = new byte[] { 3 };
        ByteBuf one = Unpooled.WrappedBuffer(new Memory<byte>[] { new byte[] { 1, 2, 3 } });
        ByteBuf many = Unpooled.WrappedBuffer(a, b, c);
        try { Assert.True(ByteBufUtil.Equals(one, many)); Assert.Equal(3, Assert.IsType<CompositeByteBuf>(many).NumComponents); }
        finally { one.Release(); many.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RepeatedRangesShareBytesButOwnIndependentWrapperReferences(bool readOnly)
    {
        using var storage = new ExternalStorage(1, 4); storage.Memory.Span.Fill(7);
        ByteBuf result = Wrap(new[] { storage.Memory, storage.Memory }, readOnly);
        var composite = Assert.IsType<CompositeByteBuf>(result);
        ByteBuf first = composite.Component(0), second = composite.Component(1);
        Assert.Equal(1, first.ReferenceCount); Assert.Equal(1, second.ReferenceCount);
        storage.Memory.Span[0] = 99; Assert.Equal(99, result.GetByte(0)); Assert.Equal(99, result.GetByte(4));
        ByteBuf retained = result.RetainedSlice(1, 6); Assert.False(result.Release());
        Assert.Equal(1, retained.ReferenceCount); Assert.Equal(99, retained.GetByte(3)); retained.Release();
        Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, second.ReferenceCount); Assert.Equal(99, storage.Memory.Span[0]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InvalidCompositeLimitRejectsNonemptyMultipleInputsWithoutDisposingOwners(bool readOnly)
    {
        using var storage = new ExternalStorage(1, 8);
        Assert.Throws<ArgumentOutOfRangeException>(() => Wrap(new[] { default(Memory<byte>), storage.Memory }, readOnly, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Wrap(new[] { storage.Memory, storage.Memory }, readOnly, -1));
        storage.Memory.Span[0] = 42;
    }

    [Fact]
    public void NullArraysAreRejectedByBothTypedOverloads()
    {
        Assert.Throws<ArgumentNullException>(() => Unpooled.WrappedBuffer((Memory<byte>[])null));
        Assert.Throws<ArgumentNullException>(() => Unpooled.WrappedBuffer((ReadOnlyMemory<byte>[])null));
        Assert.Throws<ArgumentNullException>(() => Unpooled.WrappedBuffer(2, (Memory<byte>[])null));
        Assert.Throws<ArgumentNullException>(() => Unpooled.WrappedBuffer(2, (ReadOnlyMemory<byte>[])null));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FailedConsolidationPreservesExternalOwnershipAndCanBeRetried(bool readOnly)
    {
        using var first = new ArrayMemoryManager(4); using var second = new ArrayMemoryManager(4);
        Memory<byte> a = first.Range, b = second.Range; a.Span.Fill(1); b.Span.Fill(2); second.FailAccess = true;
        Assert.Throws<InvalidOperationException>(() => Wrap(new[] { a, b }, readOnly, 1));
        Assert.Equal(0, first.DisposeCalls); Assert.Equal(0, second.DisposeCalls);
        second.FailAccess = false;
        ByteBuf recovered = Wrap(new[] { a, b }, readOnly, 1);
        try { Assert.Equal(new byte[] { 1, 1, 1, 1, 2, 2, 2, 2 }, ByteBufUtil.GetBytes(recovered)); }
        finally { recovered.Release(); }
        Assert.Equal(0, first.DisposeCalls); Assert.Equal(0, second.DisposeCalls);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CapacityOverflowReleasesOnlyWrappersWithoutReadingOrDisposingManagers(bool readOnly)
    {
        using var first = new ReportedLengthMemoryManager(int.MaxValue);
        using var second = new ReportedLengthMemoryManager(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => Wrap(new[] { first.Range, second.Range }, readOnly));
        Assert.Equal(0, first.SpanCalls); Assert.Equal(0, second.SpanCalls);
        Assert.Equal(0, first.DisposeCalls); Assert.Equal(0, second.DisposeCalls);
    }

    [Fact]
    public void ExplicitByteBufWrappingCanMixWritableAndReadOnlyExternalRanges()
    {
        Memory<byte> first = new byte[] { 1, 2 }; ReadOnlyMemory<byte> second = new byte[] { 3, 4 };
        ByteBuf result = Unpooled.WrappedBuffer(Unpooled.WrappedBuffer(first), Unpooled.WrappedBuffer(second));
        try
        {
            result.SetByte(0, 99); Assert.Equal(99, first.Span[0]);
            Assert.Throws<NotSupportedException>(() => result.SetByte(2, 42)); Assert.Equal(3, result.GetByte(2));
        }
        finally { result.Release(); }
    }

    private static ByteBuf Wrap(Memory<byte>[] buffers, bool readOnly, int? maximum = null)
    {
        if (!readOnly) return maximum.HasValue ? Unpooled.WrappedBuffer(maximum.Value, buffers) : Unpooled.WrappedBuffer(buffers);
        ReadOnlyMemory<byte>[] ranges = Array.ConvertAll(buffers, static memory => (ReadOnlyMemory<byte>)memory);
        return maximum.HasValue ? Unpooled.WrappedBuffer(maximum.Value, ranges) : Unpooled.WrappedBuffer(ranges);
    }

    private sealed class ExternalStorage : IDisposable
    {
        private readonly NativeMemoryOwner _native;
        private readonly ArrayMemoryManager _manager;
        public Memory<byte> Memory { get; }
        public ExternalStorage(int kind, int length)
        {
            if (kind == 1) { _native = new NativeMemoryAllocator(1024).Allocate(length, clear: true); Memory = _native.Memory; }
            else if (kind == 2) { _manager = new ArrayMemoryManager(length); Memory = _manager.Range; }
            else Memory = new byte[length];
        }
        public void Dispose() { _native?.Dispose(); ((IDisposable)_manager)?.Dispose(); }
    }

    private sealed class ArrayMemoryManager : MemoryManager<byte>
    {
        private byte[] _array;
        public Memory<byte> Range => CreateMemory(_array.Length);
        public bool FailAccess { get; set; }
        public int DisposeCalls { get; private set; }
        public ArrayMemoryManager(int length) => _array = new byte[length];
        public override Span<byte> GetSpan()
        {
            if (FailAccess) throw new InvalidOperationException("Source access failed.");
            return _array ?? throw new ObjectDisposedException(nameof(ArrayMemoryManager));
        }
        public override MemoryHandle Pin(int elementIndex = 0) => _array.AsMemory(elementIndex).Pin();
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { DisposeCalls++; _array = null; }
    }

    private sealed class ReportedLengthMemoryManager : MemoryManager<byte>
    {
        private readonly int _length;
        public Memory<byte> Range => CreateMemory(_length);
        public int SpanCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public ReportedLengthMemoryManager(int length) => _length = length;
        public override Span<byte> GetSpan() { SpanCalls++; throw new InvalidOperationException("Must not access memory."); }
        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { DisposeCalls++; }
    }
}
