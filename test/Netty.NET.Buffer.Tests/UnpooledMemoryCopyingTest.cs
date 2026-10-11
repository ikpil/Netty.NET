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

public class UnpooledMemoryCopyingTest
{
    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(2, false)] [InlineData(2, true)]
    public void OriginalSingleCopyCapturesRangeAndCreatesIndependentWritableHeap(int kind, bool arrayOverload)
    {
        using var storage = new ExternalStorage(kind, 16);
        for (int i = 0; i < 16; i++) storage.Memory.Span[i] = (byte)i;
        ByteBuf source = Unpooled.WrappedBuffer(storage.Memory); source.SetIndex(1, 15).MarkReaderIndex().MarkWriterIndex();
        ReadOnlyMemory<byte> range = storage.Memory.Slice(2, 8);
        ByteBuf copy = arrayOverload ? Unpooled.CopiedBuffer(new[] { range }) : Unpooled.CopiedBuffer(storage.Memory.Slice(2, 8));
        try
        {
            Assert.IsType<UnpooledHeapByteBuf>(copy); Assert.False(copy.IsDirect); Assert.False(copy.IsReadOnly);
            Assert.Equal(8, copy.Capacity); Assert.Equal(8, copy.MaxCapacity);
            Assert.Equal(0, copy.ReaderIndex); Assert.Equal(8, copy.WriterIndex); Assert.Equal(1, copy.ReferenceCount);
            Assert.Same(UnpooledByteBufAllocator.Default, copy.Allocator);
            Assert.Equal(new byte[] { 2, 3, 4, 5, 6, 7, 8, 9 }, ByteBufUtil.GetBytes(copy));
            Assert.Equal(8, range.Length); Assert.Equal(2, range.Span[0]);
            storage.Memory.Span[2] = 99; Assert.Equal(2, copy.GetByte(0));
            copy.SetByte(1, 42); Assert.Equal(3, storage.Memory.Span[3]);
            Assert.Equal(1, source.ReaderIndex); Assert.Equal(15, source.WriterIndex); Assert.Equal(1, source.ReferenceCount);
            source.SetIndex(2, 14).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(1, source.ReaderIndex); Assert.Equal(15, source.WriterIndex);
            Assert.Throws<ArgumentOutOfRangeException>(() => copy.Capacity = 9);
        }
        finally { copy.Release(); source.Release(); }
        Assert.Equal(99, storage.Memory.Span[2]);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void MixedSourceCopiesConcatenateOnlySelectedRangesAndIgnoreEmptyRanges(int firstKind)
    {
        using var a = new ExternalStorage(firstKind, 6);
        using var b = new ExternalStorage((firstKind + 1) % 3, 6);
        using var c = new ExternalStorage((firstKind + 2) % 3, 6);
        a.Memory.Span.Fill(0xee); b.Memory.Span.Fill(0xdd); c.Memory.Span.Fill(0xcc);
        new byte[] { 1, 2 }.CopyTo(a.Memory.Span[1..]); new byte[] { 3, 4, 5 }.CopyTo(b.Memory.Span[2..]); c.Memory.Span[3] = 6;
        ReadOnlyMemory<byte>[] inputs = { default, a.Memory.Slice(1, 2), b.Memory.Slice(6, 0), b.Memory.Slice(2, 3), c.Memory.Slice(3, 1), default };
        ByteBuf copy = Unpooled.CopiedBuffer(inputs);
        try
        {
            Assert.IsType<UnpooledHeapByteBuf>(copy); Assert.Equal(6, copy.MaxCapacity); Assert.Equal(6, copy.WriterIndex);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, ByteBufUtil.GetBytes(copy));
            a.Memory.Span[1] = 99; copy.SetByte(2, 42); Assert.Equal(1, copy.GetByte(0)); Assert.Equal(3, b.Memory.Span[2]);
            Assert.Equal(0xee, a.Memory.Span[0]); Assert.Equal(0xdd, b.Memory.Span[1]); Assert.Equal(0xcc, c.Memory.Span[4]);
            Assert.Equal(2, inputs[1].Length); Assert.Equal(3, inputs[3].Length); Assert.Equal(1, inputs[4].Length);
        }
        finally { copy.Release(); }
    }

    [Fact]
    public void OriginalMultipleMemoryCopiesEqualTheArrayCopy()
    {
        ReadOnlyMemory<byte>[] single = { new byte[] { 1, 2, 3 } };
        ReadOnlyMemory<byte> a = new byte[] { 1 }, b = new byte[] { 2 }, c = new byte[] { 3 };
        ByteBuf expected = Unpooled.CopiedBuffer(new byte[] { 1, 2, 3 });
        ByteBuf one = Unpooled.CopiedBuffer(single); ByteBuf many = Unpooled.CopiedBuffer(a, b, c);
        try { Assert.True(ByteBufUtil.Equals(expected, one)); Assert.True(ByteBufUtil.Equals(expected, many)); }
        finally { expected.Release(); one.Release(); many.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void OriginalEmptyMemoryCopiesReturnSentinelWithoutOwningSource(int kind)
    {
        using var storage = new ExternalStorage(kind, 8);
        ReadOnlyMemory<byte> empty = storage.Memory.Slice(8, 0);
        Assert.Same(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(empty));
        Assert.Same(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(new[] { empty }));
        Assert.Same(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(empty, empty));
        Assert.Same(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(Array.Empty<ReadOnlyMemory<byte>>()));
        storage.Memory.Span[0] = 42; Assert.Equal(42, storage.Memory.Span[0]);
    }

    [Fact]
    public void EmptyManagerRangesAreNotAccessedEvenWhenOtherRangesAreNonempty()
    {
        using var manager = new ReportedLengthMemoryManager(0);
        ReadOnlyMemory<byte> empty = manager.Range;
        Assert.Same(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(empty));
        Assert.Same(Unpooled.EmptyBuffer, Unpooled.CopiedBuffer(empty, empty));
        ReadOnlyMemory<byte> bytes = new byte[] { 1, 2 };
        ByteBuf copy = Unpooled.CopiedBuffer(empty, bytes, empty);
        try { Assert.Equal(new byte[] { 1, 2 }, ByteBufUtil.GetBytes(copy)); }
        finally { copy.Release(); }
        Assert.Equal(0, manager.SpanCalls);
    }

    [Theory]
    [InlineData(int.MaxValue, 1)]
    [InlineData(int.MaxValue - 8, 9)]
    [InlineData(1, int.MaxValue)]
    public void TotalLengthOverflowIsRejectedBeforeReadingOrAllocating(int firstLength, int secondLength)
    {
        using var a = new ReportedLengthMemoryManager(firstLength);
        using var b = new ReportedLengthMemoryManager(secondLength);
        Assert.Throws<ArgumentException>(() => Unpooled.CopiedBuffer(a.Range, b.Range));
        Assert.Equal(0, a.SpanCalls); Assert.Equal(0, b.SpanCalls);
    }

    [Fact]
    public void NullMemoryArrayIsRejected()
        => Assert.Throws<ArgumentNullException>(() => Unpooled.CopiedBuffer((ReadOnlyMemory<byte>[])null));

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DisposedNonemptySourceFailsWithoutDisposingOtherSources(bool multiple)
    {
        var native = new NativeMemoryAllocator(8); NativeMemoryOwner owner = native.Allocate(8);
        ReadOnlyMemory<byte> invalid = owner.Memory; owner.Dispose();
        using var manager = new ArrayMemoryManager(4); manager.Range.Span.Fill(7);
        ReadOnlyMemory<byte> valid = manager.Range;
        Assert.Throws<ObjectDisposedException>(() => multiple ? Unpooled.CopiedBuffer(valid, invalid) : Unpooled.CopiedBuffer(invalid));
        Assert.Equal(0, manager.DisposeCalls); Assert.Equal(0, manager.PinCalls);
        Assert.Equal(new byte[] { 7, 7, 7, 7 }, valid.ToArray()); Assert.Equal(0, native.ReservedBytes);
        ByteBuf recovered = Unpooled.CopiedBuffer(valid);
        try { Assert.Equal(new byte[] { 7, 7, 7, 7 }, ByteBufUtil.GetBytes(recovered)); }
        finally { recovered.Release(); }
    }

    [Fact]
    public void SourceAccessFailureDoesNotAcquireOrDisposeManagerOwnership()
    {
        using var first = new ArrayMemoryManager(4); using var second = new ArrayMemoryManager(4);
        first.Range.Span.Fill(1); second.Range.Span.Fill(2);
        ReadOnlyMemory<byte> a = first.Range, b = second.Range;
        second.FailAccess = true;
        Assert.Throws<InvalidOperationException>(() => Unpooled.CopiedBuffer(a, b));
        Assert.Equal(0, first.DisposeCalls); Assert.Equal(0, second.DisposeCalls);
        Assert.Equal(0, first.PinCalls); Assert.Equal(0, second.PinCalls);
        second.FailAccess = false; ByteBuf recovered = Unpooled.CopiedBuffer(a, b);
        try { Assert.Equal(new byte[] { 1, 1, 1, 1, 2, 2, 2, 2 }, ByteBufUtil.GetBytes(recovered)); }
        finally { recovered.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CompletedCopiesSurviveImmediateByteBufSourceRelease(bool direct)
    {
        var native = new NativeMemoryAllocator(16); var allocator = new UnpooledByteBufAllocator(direct, native);
        ByteBuf source = allocator.Buffer(8, 8); source.WriteLong(0x0102030405060708L);
        ReadOnlyMemory<byte> memory = source.AsReadOnlyMemory(0, 8);
        ByteBuf copy = Unpooled.CopiedBuffer(memory, memory);
        try
        {
            Assert.Equal(1, source.ReferenceCount); source.Release();
            Assert.Equal(0, native.ReservedBytes); Assert.Equal(0, allocator.Metric.UsedDirectMemory); Assert.Equal(0, allocator.Metric.UsedHeapMemory);
            Assert.Equal(0x0102030405060708L, copy.ReadLong()); Assert.Equal(0x0102030405060708L, copy.ReadLong());
        }
        finally { if (source.ReferenceCount > 0) source.Release(); copy.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ResultCanShrinkAndRegrowOnlyWithinCapturedMaximum(bool multiple)
    {
        ReadOnlyMemory<byte> a = new byte[] { 1, 2, 3, 4 }, b = new byte[] { 5, 6, 7, 8 };
        ByteBuf copy = multiple ? Unpooled.CopiedBuffer(a, b) : Unpooled.CopiedBuffer((ReadOnlyMemory<byte>)new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        try
        {
            copy.SetIndex(6, 8); copy.Capacity = 4;
            Assert.Equal(4, copy.ReaderIndex); Assert.Equal(4, copy.WriterIndex);
            copy.Capacity = 8; Assert.Equal(new byte[] { 1, 2, 3, 4, 0, 0, 0, 0 }, copy.AsMemory(0, 8).ToArray());
            Assert.Equal(8, copy.MaxCapacity); Assert.Throws<ArgumentOutOfRangeException>(() => copy.Capacity = 9);
        }
        finally { copy.Release(); }
    }

    [Fact]
    public void CopyHasNoOrderMetadataAndExplicitLittleEndianOperationsReadTheSameBytes()
    {
        ReadOnlyMemory<byte> bytes = new byte[] { 1, 2, 3, 4 };
        ByteBuf copy = Unpooled.CopiedBuffer(bytes, bytes);
        try { Assert.Equal(0x01020304, copy.GetInt(0)); Assert.Equal(0x04030201, copy.GetIntLE(0)); }
        finally { copy.Release(); }
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
        public int DisposeCalls { get; private set; }
        public int PinCalls { get; private set; }
        public bool FailAccess { get; set; }
        public Memory<byte> Range => CreateMemory(_array.Length);
        public ArrayMemoryManager(int length) => _array = new byte[length];
        public override Span<byte> GetSpan()
        {
            if (FailAccess) throw new InvalidOperationException("Source access failed.");
            return _array ?? throw new ObjectDisposedException(nameof(ArrayMemoryManager));
        }
        public override MemoryHandle Pin(int elementIndex = 0) { PinCalls++; throw new NotSupportedException(); }
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { DisposeCalls++; _array = null; }
    }

    private sealed class ReportedLengthMemoryManager : MemoryManager<byte>
    {
        private readonly int _length;
        public int SpanCalls { get; private set; }
        public Memory<byte> Range => CreateMemory(_length);
        public ReportedLengthMemoryManager(int length) => _length = length;
        public override Span<byte> GetSpan() { SpanCalls++; throw new InvalidOperationException("Must not access memory."); }
        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { }
    }
}
