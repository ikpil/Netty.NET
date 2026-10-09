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

// Selected pinned UnpooledTest wrapping/release contracts, plus CLR failure cleanup.
public class UnpooledWrappingTest
{
    private static byte[] Bytes(ByteBuf buffer) => buffer.ReadableSequence.ToArray();

    [Fact]
    public void EmptyCallsAndTypedEmptyArraysReturnTheSharedSentinel()
    {
        Assert.Same(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer());
        Assert.Same(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(0));
        Assert.Same(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(Array.Empty<ByteBuf>()));
        Assert.Same(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(Array.Empty<byte[]>()));
        Assert.Same(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(-1, Array.Empty<ByteBuf>()));
        Assert.Same(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(-1, Array.Empty<byte[]>()));
        Assert.Same(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(new byte[0], new byte[0]));
        Assert.False(Unpooled.EmptyBuffer.Release()); // EMPTY_BUFFER cannot be released
    }

    [Fact]
    public void ArrayRegionWrapsWithoutCopyAndHasIndependentIndices()
    {
        byte[] data = { 9, 1, 2, 8 };
        ByteBuf buffer = Unpooled.WrappedBuffer(data, 1, 2);
        try
        {
            Assert.Equal(2, buffer.Capacity);
            Assert.Equal(2, buffer.MaxCapacity);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(2, buffer.WriterIndex);
            Assert.Equal(new byte[] { 1, 2 }, Bytes(buffer));
            data[1] = 7;
            Assert.Equal(7, buffer.GetByte(0));
            buffer.SetByte(1, 6);
            Assert.Equal(6, data[2]);
            Assert.Throws<NotSupportedException>(() => buffer.Capacity = 3);
        }
        finally { buffer.Release(); }
        Assert.Same(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(data, data.Length, 0));
        ByteBuf whole = Unpooled.WrappedBuffer(data, 0, data.Length);
        try { Assert.Equal(data.Length, whole.Capacity); Assert.Null(whole.Unwrap()); }
        finally { whole.Release(); }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(5, 0)]
    [InlineData(0, -1)]
    [InlineData(3, 2)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void RegionArgumentsAreCheckedEvenForZeroLength(int offset, int length)
        => Assert.Throws<ArgumentOutOfRangeException>(() => Unpooled.WrappedBuffer(new byte[4], offset, length));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SingleByteBufTransfersReadableSliceAndKeepsSourceIndices(bool direct, bool arrayOverload)
    {
        var allocator = new NativeMemoryAllocator(64);
        ByteBuf source = direct ? new UnpooledDirectByteBuf(4, allocator: allocator) : Unpooled.Buffer(4);
        source.WriteBytes(new byte[] { 9, 1, 2, 8 }).SetIndex(1, 3);
        ByteBuf buffer = arrayOverload ? Unpooled.WrappedBuffer(-1, new[] { source }) : Unpooled.WrappedBuffer(source);
        try
        {
            Assert.NotSame(source, buffer);
            Assert.IsNotType<CompositeByteBuf>(buffer);
            Assert.Equal(new byte[] { 1, 2 }, Bytes(buffer));
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(2, buffer.WriterIndex);
            Assert.Equal(direct, buffer.IsDirect);
            Assert.Equal(1, source.ReaderIndex);
            Assert.Equal(3, source.WriterIndex);
            Assert.Equal(1, source.ReferenceCount);
            buffer.SetByte(0, 7);
            Assert.Equal(7, source.GetByte(1));
        }
        finally { Assert.True(buffer.Release()); }
        Assert.Equal(0, source.ReferenceCount);
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnreadableSingleAndAllUnreadableInputsAreReleased(bool direct)
    {
        var allocator = new NativeMemoryAllocator(64);
        ByteBuf first = direct ? new UnpooledDirectByteBuf(12, allocator: allocator) : Unpooled.Buffer(12);
        ByteBuf wrapped = Unpooled.WrappedBuffer(first);
        Assert.Same(Unpooled.EmptyBuffer, wrapped);
        Assert.False(wrapped.Release()); // EMPTY_BUFFER cannot be released
        Assert.Equal(0, first.ReferenceCount);
        ByteBuf a = direct ? new UnpooledDirectByteBuf(12, allocator: allocator) : Unpooled.Buffer(12);
        ByteBuf b = direct ? new UnpooledDirectByteBuf(12, allocator: allocator) : Unpooled.Buffer(12);
        Assert.Same(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(0, a, b));
        Assert.Equal(0, a.ReferenceCount);
        Assert.Equal(0, b.ReferenceCount);
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void CompositeHeaderAndPayloadPreserveIndicesAndShareStorage()
    {
        ByteBuf header = Unpooled.Buffer(12).WriteZero(12);
        ByteBuf payload = Unpooled.Buffer(512).WriteZero(512);
        ByteBuf buffer = Unpooled.WrappedBuffer(header, payload);
        try
        {
            Assert.Equal(12, header.ReadableBytes);
            Assert.Equal(512, payload.ReadableBytes);
            Assert.Equal(524, buffer.ReadableBytes);
            Assert.Equal(2, Assert.IsType<CompositeByteBuf>(buffer).NumComponents);
            buffer.SetByte(12, 7);
            Assert.Equal(7, payload.GetByte(0));
        }
        finally { Assert.True(buffer.Release()); }
        Assert.Equal(0, header.ReferenceCount);
        Assert.Equal(0, payload.ReferenceCount);
    }

    [Fact]
    public void LeadingUnreadableInputsAreReleasedButLaterEmptyComponentsStayOwned()
    {
        // See https://github.com/netty/netty/issues/5597
        ByteBuf first = Unpooled.Buffer(8);
        ByteBuf second = Unpooled.Buffer(8).WriteZero(8); // Ensure the ByteBuf is readable.
        ByteBuf third = Unpooled.Buffer(8);
        ByteBuf fourth = Unpooled.Buffer(8).WriteZero(8); // Ensure the ByteBuf is readable.
        ByteBuf buffer = Unpooled.WrappedBuffer(first, second, third, fourth);
        Assert.Equal(0, first.ReferenceCount);
        Assert.Equal(1, third.ReferenceCount);
        Assert.Equal(3, Assert.IsType<CompositeByteBuf>(buffer).NumComponents);
        Assert.Equal(16, buffer.ReadableBytes);
        Assert.True(buffer.Release());
        Assert.Equal(new[] { 0, 0, 0, 0 }, new[] { first.ReferenceCount, second.ReferenceCount, third.ReferenceCount, fourth.ReferenceCount });
        Assert.Equal(0, buffer.ReferenceCount);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void TrailingEmptyByteBufCountsTowardsConsolidation(int maxComponents, bool copies)
    {
        ByteBuf source = Unpooled.WrappedBuffer(new byte[] { 1, 2 });
        ByteBuf empty = Unpooled.Buffer(1);
        ByteBuf buffer = Unpooled.WrappedBuffer(maxComponents, source, empty);
        try
        {
            Assert.Equal(copies ? 1 : 2, Assert.IsType<CompositeByteBuf>(buffer).NumComponents);
            Assert.Equal(copies ? 0 : 1, source.ReferenceCount);
            Assert.Equal(copies ? 0 : 1, empty.ReferenceCount);
            Assert.Equal(new byte[] { 1, 2 }, Bytes(buffer));
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void ByteArrayParamsSkipEmptiesAndCopyOnlyWhenLimitIsExceeded(int maxComponents, bool copies)
    {
        byte[] first = { 1, 2 }, second = { 3 };
        ByteBuf buffer = Unpooled.WrappedBuffer(maxComponents, Array.Empty<byte>(), first, Array.Empty<byte>(), second, Array.Empty<byte>());
        try
        {
            Assert.Equal(copies ? 1 : 2, Assert.IsType<CompositeByteBuf>(buffer).NumComponents);
            Assert.Equal(3, buffer.WriterIndex);
            Assert.Equal(new byte[] { 1, 2, 3 }, Bytes(buffer));
            first[0] = 7;
            Assert.Equal(copies ? 1 : 7, buffer.GetByte(0));
            buffer.SetByte(2, 8);
            Assert.Equal(copies ? 3 : 8, second[0]);
            Assert.False(buffer.IsDirect);
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void SingleArrayAndMultiArrayWithOneNonemptyInputHaveDifferentShapes()
    {
        byte[] bytes = { 1, 2 };
        ByteBuf single = Unpooled.WrappedBuffer(0, new[] { bytes });
        ByteBuf composite = Unpooled.WrappedBuffer(Array.Empty<byte>(), bytes, Array.Empty<byte>());
        try
        {
            Assert.IsType<UnpooledHeapByteBuf>(single);
            Assert.Equal(1, Assert.IsType<CompositeByteBuf>(composite).NumComponents);
            Assert.Equal(3, ((CompositeByteBuf)composite).MaxNumComponents);
            bytes[1] = 7;
            Assert.Equal(7, single.GetByte(1));
            Assert.Equal(7, composite.GetByte(1));
        }
        finally { single.Release(); composite.Release(); }
    }

    [Fact]
    public void ByteArrayNullTerminatesBeforeOrAfterFirstNonemptyInput()
    {
        byte[] a = { 1 }, ignored = { 2 };
        Assert.Same(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(new byte[][] { null, a }));
        Assert.Same(Unpooled.EmptyBuffer, Unpooled.WrappedBuffer(Array.Empty<byte>(), null, a));
        ByteBuf buffer = Unpooled.WrappedBuffer(a, null, ignored);
        try
        {
            Assert.Equal(new byte[] { 1 }, Bytes(buffer));
            Assert.Equal(1, Assert.IsType<CompositeByteBuf>(buffer).NumComponents);
            ignored[0] = 9;
            Assert.Equal(new byte[] { 1 }, Bytes(buffer));
        }
        finally { buffer.Release(); }
        Assert.Throws<ArgumentNullException>(() => Unpooled.WrappedBuffer(new byte[][] { null }));
    }

    [Fact]
    public void ByteBufNullAfterReadableInputDrainsTheTail()
    {
        ByteBuf first = Unpooled.WrappedBuffer(new byte[] { 1 });
        ByteBuf tail = Unpooled.WrappedBuffer(new byte[] { 2 });
        ByteBuf buffer = Unpooled.WrappedBuffer(first, null, tail);
        try
        {
            Assert.Equal(new byte[] { 1 }, Bytes(buffer));
            Assert.Equal(0, tail.ReferenceCount);
            Assert.Equal(1, first.ReferenceCount);
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void NullBeforeReadableInputLeavesUnvisitedInputsCallerOwned()
    {
        ByteBuf empty = Unpooled.Buffer(1), tail = Unpooled.WrappedBuffer(new byte[] { 1 });
        Assert.Throws<ArgumentNullException>(() => Unpooled.WrappedBuffer(empty, null, tail));
        Assert.Equal(0, empty.ReferenceCount);
        Assert.Equal(1, tail.ReferenceCount);
        tail.Release();
    }

    [Fact]
    public void InvalidComponentLimitConsumesOnlyAlreadyDiscardedLeadingInputs()
    {
        ByteBuf empty = Unpooled.Buffer(1), a = Unpooled.WrappedBuffer(new byte[] { 1 }), b = Unpooled.WrappedBuffer(new byte[] { 2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => Unpooled.WrappedBuffer(0, empty, a, b));
        Assert.Equal(new[] { 0, 1, 1 }, new[] { empty.ReferenceCount, a.ReferenceCount, b.ReferenceCount });
        a.Release(); b.Release();
        Assert.Throws<ArgumentOutOfRangeException>(() => Unpooled.WrappedBuffer(0, new byte[] { 1 }, new byte[] { 2 }));
    }

    [Fact]
    public void NullTopLevelArgumentsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => Unpooled.WrappedBuffer((byte[])null, 0, 0));
        Assert.Throws<ArgumentNullException>(() => Unpooled.WrappedBuffer((ByteBuf)null));
        Assert.Throws<ArgumentNullException>(() => Unpooled.WrappedBuffer((ByteBuf[])null));
        Assert.Throws<ArgumentNullException>(() => Unpooled.WrappedBuffer((byte[][])null));
        Assert.Throws<ArgumentNullException>(() => Unpooled.WrappedBuffer(2, (ByteBuf[])null));
        Assert.Throws<ArgumentNullException>(() => Unpooled.WrappedBuffer(2, (byte[][])null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeAndReadOnlyViewsKeepPermissionsUntilHeapConsolidation(bool consolidate)
    {
        var allocator = new NativeMemoryAllocator(64);
        ByteBuf a = new UnpooledDirectByteBuf(4, allocator: allocator).WriteBytes(new byte[] { 9, 1, 2, 8 }).SetIndex(1, 3).AsReadOnly();
        ByteBuf b = new UnpooledDirectByteBuf(1, allocator: allocator).WriteByte(3);
        ByteBuf buffer = Unpooled.WrappedBuffer(consolidate ? 1 : 2, a, b);
        try
        {
            Assert.Equal(!consolidate, buffer.IsDirect);
            Assert.Equal(new byte[] { 1, 2, 3 }, Bytes(buffer));
            if (consolidate) buffer.SetByte(0, 7);
            else Assert.Throws<NotSupportedException>(() => buffer.SetByte(0, 7));
            buffer.SetByte(2, 8);
            Assert.Equal(consolidate ? 0 : 1, a.ReferenceCount);
            Assert.Equal(consolidate ? 0 : 1, b.ReferenceCount);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void RepeatedRetainedSourceAndNestedCompositePreserveOneReferencePerInput()
    {
        ByteBuf shared = Unpooled.WrappedBuffer(new byte[] { 1, 2 }).Retain();
        var nested = new CompositeByteBuf();
        nested.AddComponent(Unpooled.WrappedBuffer(new byte[] { 3 }), true);
        ByteBuf buffer = Unpooled.WrappedBuffer(shared, shared, nested);
        try
        {
            Assert.Equal(3, Assert.IsType<CompositeByteBuf>(buffer).NumComponents);
            Assert.Equal(new byte[] { 1, 2, 1, 2, 3 }, Bytes(buffer));
            Assert.Equal(2, shared.ReferenceCount);
            Assert.Equal(1, nested.ReferenceCount);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, shared.ReferenceCount);
        Assert.Equal(0, nested.ReferenceCount);
    }

    [Fact]
    public void FailedInsertionReleasesTheOtherwiseUnreachablePrefixAndTail()
    {
        var allocator = new NativeMemoryAllocator(64);
        ByteBuf prefix = new UnpooledDirectByteBuf(1, allocator: allocator).WriteByte(1);
        ByteBuf dead = Unpooled.Buffer(1).WriteByte(2); dead.Release();
        ByteBuf tail = new UnpooledDirectByteBuf(1, allocator: allocator).WriteByte(3);
        Assert.Throws<IllegalReferenceCountException>(() => Unpooled.WrappedBuffer(prefix, dead, tail));
        Assert.Equal(new[] { 0, 0, 0 }, new[] { prefix.ReferenceCount, dead.ReferenceCount, tail.ReferenceCount });
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void FailedConsolidationReleasesEveryAcquiredInputWithoutMaskingFailure()
    {
        var failing = new FailingReadHeapByteBuf();
        ByteBuf other = Unpooled.Buffer(1).WriteByte(2);
        Assert.Same(failing.Failure, Assert.Throws<InvalidOperationException>(() => Unpooled.WrappedBuffer(1, failing, other)));
        Assert.Equal(0, failing.ReferenceCount);
        Assert.Equal(0, other.ReferenceCount);
    }

    [Fact]
    public void OverflowPreflightLeavesTheUntransferredSuffixCallerOwned()
    {
        ByteBuf owner = Unpooled.Buffer(1 << 20).WriteZero(1 << 20);
        ByteBuf leading = Unpooled.Buffer(1);
        ByteBuf[] inputs = new ByteBuf[2049]; inputs[0] = leading;
        for (int i = 1; i < inputs.Length; ++i) inputs[i] = owner.RetainedDuplicate();
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Unpooled.WrappedBuffer(inputs));
            Assert.Equal(0, leading.ReferenceCount);
            Assert.Equal(2049, owner.ReferenceCount);
        }
        finally { for (int i = 1; i < inputs.Length; ++i) inputs[i].Release(); owner.Release(); }
    }

    private sealed class FailingReadHeapByteBuf : UnpooledHeapByteBuf
    {
        private int _reads;
        internal readonly InvalidOperationException Failure = new("Source read failed during consolidation.");
        internal FailingReadHeapByteBuf() : base(1) => WriteByte(1);
        protected override ReadOnlyMemory<byte> GetReadOnlyMemoryCore(int index, int length)
        {
            if (++_reads == 2) throw Failure;
            return base.GetReadOnlyMemoryCore(index, length);
        }
    }
}
