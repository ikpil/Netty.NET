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
using System.Linq;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ByteBufDecoderPolicyTest
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalFastWritableScenariosTrackWritesAndCompaction(bool direct)
    {
        ByteBuf buffer = direct ? Unpooled.DirectBuffer(150, 500) : Unpooled.Buffer(150, 500);
        try
        {
            buffer.WriterIndex = 100;
            Assert.Equal(50, buffer.WritableBytes); Assert.Equal(150, buffer.Capacity);
            Assert.Equal(500, buffer.MaxCapacity); Assert.Equal(400, buffer.MaxWritableBytes);
            // Default implementation has fast writable == writable
            Assert.Equal(50, buffer.MaxFastWritableBytes);
            Assert.True(buffer.IsContiguous);
            buffer.WriteByte(1); Assert.Equal(49, buffer.MaxFastWritableBytes);
            buffer.SetZero(0, buffer.Capacity).SetIndex(20, 101).MarkReaderIndex().MarkWriterIndex();
            int previous = buffer.MaxFastWritableBytes;
            buffer.DiscardReadBytes();
            Assert.Equal(previous + 20, buffer.MaxFastWritableBytes);
            Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(81, buffer.WriterIndex);
            Assert.Equal(1, buffer.ReferenceCount);
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void MetadataFollowsStorageTopologyThroughViewsAndWrappers(int kind)
    {
        ByteBuf owner = kind switch
        {
            1 => Unpooled.DirectBuffer(8, 32).WriteZero(8),
            2 => Unpooled.CompositeBuffer(),
            3 => Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(new byte[8]), true),
            4 => Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(new byte[4]), true)
                .AddComponent(Unpooled.WrappedBuffer(new byte[4]), true),
            5 => Unpooled.WrappedUnmodifiableBuffer(Unpooled.WrappedBuffer(new byte[4]), Unpooled.WrappedBuffer(new byte[4])),
            6 => new ReadOnlyMemoryByteBuf(UnpooledByteBufAllocator.Default, new byte[8]),
            7 => Unpooled.EmptyBuffer,
            8 => Unpooled.Buffer(0, 32),
            _ => Unpooled.Buffer(8, 32).WriteZero(8)
        };
        try
        {
            int capacity = owner.Capacity, reader = capacity == 0 ? 0 : 2, writer = capacity == 0 ? 0 : 6;
            owner.SetIndex(reader, writer);
            bool contiguous = kind is not (2 or 3 or 4 or 5);
            ByteBuf[] views = { owner, owner.Duplicate(), owner.AsReadOnly(), owner.Slice(0, capacity),
                Unpooled.UnreleasableBuffer(owner), new SimpleLeakAwareByteBuf(owner, new Tracker()) };
            foreach (ByteBuf view in views)
            {
                int r = view.ReaderIndex, w = view.WriterIndex;
                Assert.Equal(contiguous, view.IsContiguous);
                Assert.Equal(view.WritableBytes, view.MaxFastWritableBytes);
                Assert.Equal(r, view.ReaderIndex); Assert.Equal(w, view.WriterIndex);
                Assert.Equal(1, view.ReferenceCount);
            }
            if (kind == 3)
            {
                Assert.True(owner.TryGetReadOnlyMemory(0, capacity, out _));
                Assert.True(owner.ReadableSequence.IsSingleSegment);
                Assert.False(owner.IsContiguous); Assert.False(owner.Slice(0, 4).IsContiguous);
            }
            ByteBuf readOnly = owner.AsReadOnly();
            Assert.Equal(capacity - writer, readOnly.MaxFastWritableBytes);
            Assert.False(readOnly.CanWrite(0));
            Assert.Equal(reader, owner.ReaderIndex); Assert.Equal(writer, owner.WriterIndex);
        }
        finally { owner.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CompositeHintRemainsConservativeAcrossStructuralChanges(bool direct)
    {
        CompositeByteBuf buffer = new(16, direct);
        try
        {
            Assert.False(buffer.IsContiguous); Assert.Equal(0, buffer.MaxFastWritableBytes);
            buffer.AddComponent(Unpooled.WrappedBuffer(new byte[4]), true);
            Assert.False(buffer.IsContiguous); Assert.True(buffer.TryGetReadOnlyMemory(0, 4, out _));
            buffer.AddComponent(Unpooled.WrappedBuffer(new byte[4]), true).SetIndex(2, 6);
            ByteBuf view = buffer.Duplicate();
            Assert.False(view.IsContiguous); Assert.Equal(2, view.MaxFastWritableBytes);
            buffer.Consolidate(); Assert.Equal(1, buffer.NumComponents);
            Assert.False(buffer.IsContiguous); Assert.False(view.IsContiguous);
            Assert.Equal(2, buffer.MaxFastWritableBytes);
            buffer.Capacity = 12; Assert.Equal(6, buffer.MaxFastWritableBytes);
            buffer.RemoveComponents(0, buffer.NumComponents).Clear();
            Assert.False(buffer.IsContiguous); Assert.Equal(0, buffer.MaxFastWritableBytes);
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void TransparentWrappersForwardHiddenReservationWhileDerivedViewsUseTheirOwnBounds()
    {
        ByteBuf owner = new ReservedBuffer().SetIndex(1, 6);
        try
        {
            ByteBuf wrapped = new SimpleLeakAwareByteBuf(owner, new Tracker());
            ByteBuf unreleasable = Unpooled.UnreleasableBuffer(owner);
            Assert.True(wrapped.IsContiguous); Assert.True(unreleasable.IsContiguous);
            Assert.Equal(10, wrapped.MaxFastWritableBytes); Assert.Equal(10, unreleasable.MaxFastWritableBytes);
            Assert.Equal(2, owner.WritableBytes);
            Assert.Equal(2, owner.Duplicate().MaxFastWritableBytes);
            Assert.Equal(2, owner.AsReadOnly().MaxFastWritableBytes);
            Assert.Equal(0, wrapped.Slice(2, 3).MaxFastWritableBytes);
            wrapped.WriteByte(9);
            Assert.Equal(9, owner.MaxFastWritableBytes); Assert.Equal(9, unreleasable.MaxFastWritableBytes);
            owner.EnsureWritable(4);
            Assert.Equal(16, owner.Capacity); Assert.Equal(9, wrapped.MaxFastWritableBytes);
            Assert.Equal(9, wrapped.WritableBytes); Assert.Equal(9, owner.GetByte(6));
            Assert.Equal(1, owner.ReferenceCount);
        }
        finally { owner.Release(); }
    }

    public static IEnumerable<object[]> AllocatorCases()
    {
        foreach (bool preferDirect in new[] { false, true })
            for (int kind = 0; kind < 9; kind++) yield return new object[] { preferDirect, kind };
    }

    private static (ByteBuf Buffer, ByteBuf Owner) Source(UnpooledByteBufAllocator allocator, int kind)
    {
        byte[] data = { 1, 2, 3, 4, 5, 6, 7, 8 };
        ByteBuf owner = kind switch
        {
            1 => allocator.DirectBuffer(8, 32).WriteBytes(data),
            6 => new CompositeByteBuf(allocator, false).AddComponent(allocator.HeapBuffer(4, 32).WriteBytes(data[..4]), true)
                .AddComponent(allocator.HeapBuffer(4, 32).WriteBytes(data[4..]), true),
            7 => new ReadOnlyMemoryByteBuf(allocator, data),
            _ => allocator.HeapBuffer(8, 32).WriteBytes(data)
        };
        ByteBuf view = kind switch
        {
            2 => owner.Slice(1, 6), 3 => owner.Duplicate(), 4 => owner.AsReadOnly(),
            5 => Unpooled.UnreleasableBuffer(owner), 8 => new SimpleLeakAwareByteBuf(owner, new Tracker()), _ => owner
        };
        return (view, owner);
    }

    [Theory]
    [MemberData(nameof(AllocatorCases))]
    public void OriginalAllocatedReadContractUsesCreatorPolicyAndIndependentLifetime(bool preferDirect, int kind)
    {
        var native = new NativeMemoryAllocator(); var allocator = new UnpooledByteBufAllocator(preferDirect, native);
        var (buffer, owner) = Source(allocator, kind); ByteBuf read = null;
        buffer.SetIndex(1, 5).MarkReaderIndex().MarkWriterIndex();
        byte[] expected = ByteBufUtil.GetBytes(buffer, 1, 3);
        long heap = allocator.Metric.UsedHeapMemory, direct = allocator.Metric.UsedDirectMemory;
        try
        {
            read = buffer.ReadBytes(3);
            Assert.Same(allocator, read.Allocator); Assert.Equal(preferDirect, read.IsDirect);
            Assert.False(read.IsReadOnly); Assert.True(read.IsContiguous);
            Assert.Equal(3, read.Capacity); Assert.Equal(buffer.MaxCapacity, read.MaxCapacity);
            Assert.Equal(0, read.ReaderIndex); Assert.Equal(3, read.WriterIndex);
            Assert.Equal(4, buffer.ReaderIndex); Assert.Equal(5, buffer.WriterIndex);
            Assert.Equal(expected, ByteBufUtil.GetBytes(read)); Assert.Equal(1, owner.ReferenceCount);
            Assert.Equal(heap + (preferDirect ? 0 : 3), allocator.Metric.UsedHeapMemory);
            Assert.Equal(direct + (preferDirect ? 3 : 0), allocator.Metric.UsedDirectMemory);
            read.SetByte(0, 99); Assert.Equal(expected[0], buffer.GetByte(1)); read.SetByte(0, expected[0]);
            buffer.ResetReaderIndex().ResetWriterIndex(); Assert.Equal(1, buffer.ReaderIndex); Assert.Equal(5, buffer.WriterIndex);
            owner.Release(); Assert.Equal(0, owner.ReferenceCount);
            Assert.Equal(expected, ByteBufUtil.GetBytes(read));
            Assert.True(read.Release()); read = null;
            Assert.Equal(0, allocator.Metric.UsedHeapMemory); Assert.Equal(0, allocator.Metric.UsedDirectMemory);
            Assert.Equal(0, native.ReservedBytes);
        }
        finally { read?.Release(); if (owner.ReferenceCount > 0) owner.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ZeroAndInvalidAllocatedReadsDoNotInvokeAllocator(bool preferDirect)
    {
        var allocator = new ProbeAllocator(preferDirect) { FailAllocation = true };
        ByteBuf source = new UnpooledHeapByteBuf(allocator, 8, 32).WriteZero(8).SetIndex(2, 5);
        try
        {
            Assert.Same(Unpooled.EmptyBuffer, source.ReadBytes(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.ReadBytes(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.ReadBytes(4));
            Assert.Equal(0, allocator.Calls); Assert.Equal(2, source.ReaderIndex);
            source.Release();
            Assert.Throws<IllegalReferenceCountException>(() => source.ReadBytes(0));
            Assert.Equal(0, allocator.Calls);
        }
        finally { if (source.ReferenceCount > 0) source.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CustomAllocatorExtraCapacityDoesNotExpandTheCopiedReadableRange(bool preferDirect)
    {
        var allocator = new ProbeAllocator(preferDirect) { ExtraCapacity = 5 };
        ByteBuf source = new UnpooledHeapByteBuf(allocator, 8, 32).WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }).SetIndex(2, 7);
        ByteBuf read = null;
        try
        {
            read = source.ReadBytes(3);
            Assert.Equal((3, 32), allocator.Requested); Assert.Equal(1, allocator.Calls);
            Assert.Same(allocator, read.Allocator); Assert.Equal(preferDirect, read.IsDirect);
            Assert.Equal(8, read.Capacity); Assert.Equal(3, read.WriterIndex); Assert.Equal(5, source.ReaderIndex);
            Assert.Equal(new byte[] { 3, 4, 5 }, ByteBufUtil.GetBytes(read));
            Assert.Equal(new byte[5], ByteBufUtil.GetBytes(read, 3, 5));
        }
        finally { read?.Release(); source.Release(); }
        Assert.Equal(0, allocator.Native.ReservedBytes);
    }

    [Theory]
    [InlineData(false, 0)] [InlineData(false, 1)] [InlineData(false, 2)]
    [InlineData(true, 0)] [InlineData(true, 1)] [InlineData(true, 2)]
    public void FailedAllocatedReadsReleaseTheNewOwnerWithoutAdvancingSource(bool preferDirect, int failure)
    {
        var allocator = new ProbeAllocator(preferDirect) { FailAllocation = failure == 0, ReadOnly = failure == 1 };
        ByteBuf source = failure == 2 ? new FailingReadBuffer(allocator) : new UnpooledHeapByteBuf(allocator, 8, 32);
        source.WriteZero(8).SetIndex(2, 7);
        try
        {
            if (failure == 1) Assert.Throws<NotSupportedException>(() => source.ReadBytes(3));
            else Assert.Throws<InvalidOperationException>(() => source.ReadBytes(3));
            Assert.Equal(1, allocator.Calls); Assert.Equal((3, 32), allocator.Requested);
            Assert.Equal(2, source.ReaderIndex); Assert.Equal(7, source.WriterIndex); Assert.Equal(1, source.ReferenceCount);
            if (failure == 0) Assert.Null(allocator.Last);
            else Assert.Equal(0, allocator.Last.ReferenceCount);
            Assert.Equal(0, allocator.Native.ReservedBytes);
        }
        finally { source.Release(); }
    }

    [Fact]
    public void BorrowedMemoryManagerHasContiguousStorageWithoutTakingOwnership()
    {
        using var manager = new ArrayManager();
        ByteBuf writable = Unpooled.WrappedBuffer(manager.Memory);
        ByteBuf readOnly = new ReadOnlyMemoryByteBuf(UnpooledByteBufAllocator.Default, manager.Memory);
        try
        {
            Assert.True(writable.IsContiguous); Assert.True(readOnly.IsContiguous);
            writable.SetIndex(1, 5); readOnly.SetIndex(2, 6);
            Assert.Equal(3, writable.MaxFastWritableBytes); Assert.Equal(2, readOnly.MaxFastWritableBytes);
            writable.SetByte(2, 77); Assert.Equal(77, readOnly.GetByte(2));
        }
        finally { writable.Release(); readOnly.Release(); }
        Assert.Equal(77, manager.GetSpan()[2]); Assert.False(manager.Disposed);
    }

    private sealed class ReservedBuffer : AbstractReferenceCountedByteBuf
    {
        private byte[] _storage = new byte[16]; private int _capacity = 8;
        internal ReservedBuffer() : base(16) { }
        public override bool IsDirect => false;
        public override bool IsContiguous => true;
        public override int MaxFastWritableBytes => _storage.Length - WriterIndex;
        public override int Capacity
        {
            get => _capacity;
            set { CheckNewCapacity(value); _capacity = value; TrimIndicesToCapacity(value); }
        }
        protected override Memory<byte> GetMemoryCore(int index, int length) => _storage.AsMemory(index, length);
        protected override void Deallocate() { _storage = Array.Empty<byte>(); _capacity = 0; }
    }

    private sealed class ProbeAllocator(bool direct) : AbstractByteBufAllocator(direct)
    {
        internal NativeMemoryAllocator Native { get; } = new();
        internal bool FailAllocation, ReadOnly; internal int ExtraCapacity, Calls;
        internal (int Initial, int Maximum) Requested; internal ByteBuf Last;
        public override bool IsDirectBufferPooled => false;
        protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity) => Allocate(initialCapacity, maxCapacity, false);
        protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity) => Allocate(initialCapacity, maxCapacity, true);
        private ByteBuf Allocate(int initial, int maximum, bool direct)
        {
            Calls++; Requested = (initial, maximum);
            if (FailAllocation) throw new InvalidOperationException("Deliberate allocation failure.");
            ByteBuf result = direct ? new UnpooledDirectByteBuf(this, initial + ExtraCapacity, maximum, Native)
                : new UnpooledHeapByteBuf(this, initial + ExtraCapacity, maximum);
            Last = ReadOnly ? result.AsReadOnly() : result;
            return Last;
        }
    }

    private sealed class FailingReadBuffer(IByteBufAllocator allocator) : UnpooledHeapByteBuf(allocator, 8, 32)
    {
        protected override bool TryGetReadOnlyMemoryCore(int index, int length, out ReadOnlyMemory<byte> memory)
            => throw new InvalidOperationException("Deliberate source read failure.");
    }

    private sealed class ArrayManager : MemoryManager<byte>
    {
        private readonly byte[] _bytes = new byte[8]; internal bool Disposed;
        public override Span<byte> GetSpan() => _bytes;
        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
        public override void Unpin() { }
        protected override void Dispose(bool disposing) => Disposed = true;
    }

    private sealed class Tracker : IResourceLeakTracker<ByteBuf>
    {
        public void Record() { }
        public void Record(object hint) { }
        public bool Close(ByteBuf buffer) => true;
    }
}
