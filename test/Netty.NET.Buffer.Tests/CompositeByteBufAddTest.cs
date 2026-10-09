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
using System.Collections;
using System.Collections.Generic;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// Selected pinned AbstractCompositeByteBufTest batch/flatten contracts plus CLR
// enumerator disposal, native quota and failure-ownership regressions.
public class CompositeByteBufAddTest
{
    private static byte[] Bytes(ByteBuf buffer) => buffer.ReadableSequence.ToArray();
    private static ByteBuf Segment(byte[] bytes, bool direct, NativeMemoryAllocator allocator)
        => direct ? new UnpooledDirectByteBuf(bytes.Length, allocator: allocator).WriteBytes(bytes) : Unpooled.WrappedBuffer(bytes);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BatchInsertionPreservesCapturedRangesAndUpdatesWriterOnlyWhenRequested(bool enumerable, bool increaseWriterIndex)
    {
        CompositeByteBuf buffer = new();
        ByteBuf first = Unpooled.WrappedBuffer(new byte[] { 8, 1, 2, 9 }).SetIndex(1, 3);
        ByteBuf second = Unpooled.WrappedBuffer(new byte[] { 3, 4 });
        ByteBuf[] batch = { first, Unpooled.EmptyBuffer, second };
        try
        {
            buffer.AddComponent(Unpooled.WrappedBuffer(new byte[] { 0 }), true)
                .AddComponent(Unpooled.WrappedBuffer(new byte[] { 5 }), true);
            if (enumerable) buffer.AddComponents(1, (IEnumerable<ByteBuf>)batch, increaseWriterIndex);
            else buffer.AddComponents(1, batch, increaseWriterIndex);
            Assert.Equal(5, buffer.NumComponents);
            Assert.Equal(6, buffer.Capacity);
            Assert.Equal(increaseWriterIndex ? 6 : 2, buffer.WriterIndex);
            Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5 }, buffer.AsReadOnlySequence(0, 6).ToArray());
            Assert.Equal(1, first.ReaderIndex); Assert.Equal(3, first.WriterIndex);
            Assert.Equal(0, second.ReaderIndex); Assert.Equal(2, second.WriterIndex);
            Assert.Equal(1, first.ReferenceCount); Assert.Equal(1, second.ReferenceCount);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, second.ReferenceCount);
    }

    [Fact]
    public void ParamsAndEmptyBatchKeepDefaultWriterIndexAndOwnedEmptyComponents()
    {
        CompositeByteBuf buffer = new();
        ByteBuf empty = Unpooled.Buffer(0);
        try
        {
            buffer.AddComponents(Unpooled.WrappedBuffer(new byte[] { 1 }), empty);
            Assert.Equal(0, buffer.WriterIndex);
            Assert.Equal(1, buffer.Capacity);
            Assert.Equal(2, buffer.NumComponents);
            buffer.AddComponents();
            buffer.AddComponents(Array.Empty<ByteBuf>(), true);
            buffer.AddComponents((IEnumerable<ByteBuf>)Array.Empty<ByteBuf>(), true);
            Assert.Equal(2, buffer.NumComponents);
            Assert.Equal(1, empty.ReferenceCount);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, empty.ReferenceCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullStopsBatchInsertionAndReleasesEveryRemainingEntry(bool enumerable)
    {
        // See https://github.com/netty/netty/issues/11612
        ByteBuf first = Unpooled.Buffer(8).WriteZero(8);
        ByteBuf tail = Unpooled.Buffer(1).WriteByte(9);
        ByteBuf[] batch = { first, null, tail, null };
        var stream = new TrackingEnumerable(batch);
        CompositeByteBuf buffer = new();
        try
        {
            if (enumerable) buffer.AddComponents(stream, true);
            else buffer.AddComponents(batch, true);
            Assert.Equal(1, buffer.NumComponents);
            Assert.Equal(8, buffer.WriterIndex);
            Assert.Equal(new byte[8], Bytes(buffer));
            Assert.Equal(0, tail.ReferenceCount);
            Assert.Equal(enumerable ? 1 : 0, stream.DisposeCount);
            Assert.Equal(enumerable ? 1 : 0, stream.EnumeratorCount);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, first.ReferenceCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TailReleaseFailureDoesNotPreventReleasingLaterEntries(bool enumerable)
    {
        ByteBuf prefix = Unpooled.WrappedBuffer(new byte[] { 1 });
        ByteBuf dead = Unpooled.Buffer(1); dead.Release();
        ByteBuf tail = Unpooled.WrappedBuffer(new byte[] { 2 });
        ByteBuf[] batch = { prefix, null, dead, tail };
        CompositeByteBuf buffer = new();
        try
        {
            if (enumerable) buffer.AddComponents((IEnumerable<ByteBuf>)batch, true);
            else buffer.AddComponents(batch, true);
            Assert.Equal(new byte[] { 1 }, Bytes(buffer));
            Assert.Equal(0, dead.ReferenceCount); Assert.Equal(0, tail.ReferenceCount);
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LaterInsertionFailureKeepsPrefixAndReleasesFailedAndUnvisitedReferences(bool enumerable)
    {
        CompositeByteBuf buffer = new();
        ByteBuf first = Unpooled.WrappedBuffer(new byte[] { 1, 2 });
        ByteBuf dead = Unpooled.Buffer(1).WriteByte(3); dead.Release();
        ByteBuf tail = Unpooled.WrappedBuffer(new byte[] { 4 });
        ByteBuf[] batch = { first, dead, tail };
        var stream = new TrackingEnumerable(batch);
        try
        {
            Assert.Throws<IllegalReferenceCountException>(() =>
            {
                if (enumerable) buffer.AddComponents(stream, true);
                else buffer.AddComponents(batch, true);
            });
            Assert.Equal(1, buffer.NumComponents);
            Assert.Equal(2, buffer.WriterIndex);
            Assert.Equal(new byte[] { 1, 2 }, Bytes(buffer));
            Assert.Equal(1, first.ReferenceCount);
            Assert.Equal(0, dead.ReferenceCount); Assert.Equal(0, tail.ReferenceCount);
            Assert.Equal(enumerable ? 1 : 0, stream.DisposeCount);
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void ArrayPreflightFailureLeavesAllReferencesWithCallerButStreamingFailureDrainsThem()
    {
        CompositeByteBuf buffer = new();
        ByteBuf first = Unpooled.WrappedBuffer(new byte[] { 1 });
        ByteBuf second = Unpooled.WrappedBuffer(new byte[] { 2 });
        var stream = new TrackingEnumerable(new[] { first, second });
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.AddComponents(1, new[] { first, second }, true));
            Assert.Equal(1, first.ReferenceCount); Assert.Equal(1, second.ReferenceCount);
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.AddComponents(1, stream, true));
            Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, second.ReferenceCount);
            Assert.Equal(1, stream.DisposeCount);
            Assert.Equal(0, buffer.NumComponents);
            Assert.Throws<ArgumentNullException>(() => buffer.AddComponents((ByteBuf[])null));
            Assert.Throws<ArgumentNullException>(() => buffer.AddComponents((IEnumerable<ByteBuf>)null));
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapacityOverflowPreflightDiffersFromStreamingOwnershipWithoutHugeAllocation(bool enumerable)
    {
        const int capacity = 1024 * 1024; // 1MB
        ByteBuf owner = Unpooled.Buffer(capacity).WriteZero(capacity);
        CompositeByteBuf buffer = new(int.MaxValue);
        for (int i = 0; i < 2046; ++i) buffer.AddComponent(owner.RetainedDuplicate());
        ByteBuf first = owner.RetainedDuplicate();
        ByteBuf second = owner.RetainedDuplicate();
        ByteBuf tail = Unpooled.WrappedBuffer(new byte[] { 7 });
        ByteBuf[] batch = { first, second, tail };
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                if (enumerable) buffer.AddComponents((IEnumerable<ByteBuf>)batch, true);
                else buffer.AddComponents(batch, true);
            });
            Assert.Equal(enumerable ? 2047 : 2046, buffer.NumComponents);
            Assert.Equal(enumerable ? capacity : 0, buffer.WriterIndex);
            Assert.Equal(enumerable ? 2048 : 2049, owner.ReferenceCount);
            Assert.Equal(enumerable ? 0 : 1, tail.ReferenceCount);
        }
        finally
        {
            if (!enumerable) { first.Release(); second.Release(); tail.Release(); }
            buffer.Release(); owner.Release();
        }
        Assert.Equal(0, owner.ReferenceCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BatchConsolidatesOnlyOnceAfterAllEntriesAndFailureKeepsEntireBatch(bool enumerable)
    {
        var allocator = new NativeMemoryAllocator(6);
        ByteBuf first = Segment(new byte[] { 1 }, true, allocator);
        ByteBuf second = Segment(new byte[] { 2 }, true, allocator);
        ByteBuf third = new ObservingDirectByteBuf(3, allocator, () =>
        {
            // The third input observes the earlier inputs while being inserted/read.
            // They must still be alive until the complete batch has been copied.
            Assert.Equal(1, first.ReferenceCount); Assert.Equal(1, second.ReferenceCount);
        });
        CompositeByteBuf buffer = new(1, true, allocator);
        try
        {
            if (enumerable) buffer.AddComponents((IEnumerable<ByteBuf>)new[] { first, second, third }, true);
            else buffer.AddComponents(new[] { first, second, third }, true);
            Assert.Equal(1, buffer.NumComponents);
            Assert.Equal(new byte[] { 1, 2, 3 }, Bytes(buffer));
            Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, second.ReferenceCount); Assert.Equal(0, third.ReferenceCount);
            Assert.Equal(3, allocator.ReservedBytes);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);

        allocator = new NativeMemoryAllocator(3);
        first = Segment(new byte[] { 1 }, true, allocator);
        second = Segment(new byte[] { 2 }, true, allocator);
        third = Segment(new byte[] { 3 }, true, allocator);
        buffer = new(1, true, allocator);
        try
        {
            Assert.Throws<OutOfMemoryException>(() =>
            {
                if (enumerable) buffer.AddComponents((IEnumerable<ByteBuf>)new[] { first, second, third }, true);
                else buffer.AddComponents(new[] { first, second, third }, true);
            });
            Assert.Equal(3, buffer.NumComponents);
            Assert.Equal(3, buffer.WriterIndex);
            Assert.Equal(new byte[] { 1, 2, 3 }, Bytes(buffer));
            Assert.Equal(1, first.ReferenceCount); Assert.Equal(1, second.ReferenceCount); Assert.Equal(1, third.ReferenceCount);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void EnumerableFailuresDrainYieldedRemainderAndDisposeEnumerator()
    {
        var failure = new InvalidOperationException("enumerator failure");
        ByteBuf first = Unpooled.WrappedBuffer(new byte[] { 1 });
        ByteBuf tail = Unpooled.WrappedBuffer(new byte[] { 2 });
        var stream = new TrackingEnumerable(new[] { first, tail }, failure);
        CompositeByteBuf buffer = new();
        try
        {
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => buffer.AddComponents(stream, true)));
            Assert.Equal(new byte[] { 1 }, Bytes(buffer));
            Assert.Equal(0, tail.ReferenceCount);
            Assert.Equal(1, stream.DisposeCount);
            Assert.Equal(1, stream.EnumeratorCount);
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void ByteBufThatAlsoImplementsEnumerableIsAddedAsOneOwnedBuffer()
    {
        var source = new EnumerableHeapByteBuf(new byte[] { 1, 2 });
        CompositeByteBuf buffer = new();
        try
        {
            buffer.AddComponents((IEnumerable<ByteBuf>)source, true);
            Assert.Equal(1, buffer.NumComponents);
            Assert.Equal(1, source.ReferenceCount);
            Assert.Equal(new byte[] { 1, 2 }, Bytes(buffer));
        }
        finally { buffer.Release(); }
        Assert.Equal(0, source.ReferenceCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FlattenConnectsOnlyReadableComponentIntersectionsAndReleasesInputContainer(bool direct)
    {
        var allocator = new NativeMemoryAllocator(128);
        CompositeByteBuf source = new(), target = new();
        ByteBuf first = Segment(new byte[] { 8, 1, 2, 9 }, direct, allocator).SetIndex(1, 3);
        ByteBuf second = Segment(new byte[] { 3, 4 }, direct, allocator);
        ByteBuf third = Segment(new byte[] { 5, 6 }, direct, allocator);
        ByteBuf excluded = Segment(new byte[] { 7, 8 }, direct, allocator);
        source.AddComponents(new[] { first, Unpooled.EmptyBuffer, second, third, excluded }, true);
        // set readable range to be from middle of first component
        // to middle of penultimate component
        source.SetIndex(1, 5);
        target.AddComponent(Segment(new byte[] { 0 }, direct, allocator), true);
        target.AddFlattenedComponents(source, true);
        try
        {
            // verify that added range matches
            Assert.Equal(new byte[] { 0, 2, 3, 4, 5 }, Bytes(target));
            // should not include empty component or last component
            // (latter outside of the readable range)
            Assert.Equal(4, target.NumComponents);
            Assert.Equal(0, source.ReferenceCount);
            // s4 wasn't in added range so should have been jettisoned
            Assert.Equal(0, excluded.ReferenceCount);
            Assert.Equal(1, first.ReferenceCount); Assert.Equal(1, second.ReferenceCount); Assert.Equal(1, third.ReferenceCount);
            Assert.Equal(1, first.ReaderIndex); Assert.Equal(3, first.WriterIndex);
            first.SetByte(2, 9); Assert.Equal(9, target.GetByte(1));
            Assert.Equal(direct, target.IsDirect);
        }
        finally { target.Release(); }
        // releasing composite should release the remaining components
        Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, second.ReferenceCount); Assert.Equal(0, third.ReferenceCount);
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void FlattenIsShallowAndDoesNotPeelDerivedOrReadOnlyViews()
    {
        CompositeByteBuf nested = new(), source = new(), target = new();
        nested.AddComponents(new[] { Unpooled.WrappedBuffer(new byte[] { 1 }), Unpooled.WrappedBuffer(new byte[] { 2 }) }, true);
        source.AddComponent(nested, true);
        target.AddFlattenedComponents(source, true);
        try
        {
            Assert.Equal(1, target.NumComponents);
            Assert.Equal(1, nested.ReferenceCount);
            Assert.Equal(2, nested.NumComponents);
            Assert.Equal(new byte[] { 1, 2 }, Bytes(target));
        }
        finally { target.Release(); }
        Assert.Equal(0, nested.ReferenceCount);

        source = new(); target = new();
        source.AddComponents(new[] { Unpooled.WrappedBuffer(new byte[] { 1, 2 }), Unpooled.WrappedBuffer(new byte[] { 3, 4 }) }, true);
        ByteBuf view = source.Slice(1, 2).AsReadOnly();
        target.AddFlattenedComponents(view, true);
        try
        {
            Assert.Equal(1, source.ReferenceCount);
            Assert.Equal(1, target.NumComponents);
            Assert.Equal(new byte[] { 2, 3 }, Bytes(target));
            Assert.True(target.ComponentSlice(0).IsReadOnly);
            Assert.Throws<NotSupportedException>(() => target.SetByte(0, 7));
        }
        finally { target.Release(); }
        Assert.Equal(0, source.ReferenceCount);
    }

    [Fact]
    public void FlattenRetainsOriginalSliceOwnershipAndHonorsSeparatelyRetainedContainer()
    {
        // It is important to use a pooled allocator here to ensure
        // the slices returned by readRetainedSlice are of type
        // PooledSlicedByteBuf, which maintains an independent refcount
        // (so that we can be sure to cover this case)
        // CLR: the same ownership transfer uses shared native root counts; no pooled subtype is claimed.
        var allocator = new NativeMemoryAllocator(128);
        ByteBuf owner = Segment(new byte[] { 1, 2, 3, 4 }, true, allocator);
        // use mixture of slice and retained slice
        ByteBuf first = owner.ReadRetainedSlice(2), second = owner.Slice(2, 2).Retain();
        owner.Release();
        CompositeByteBuf source = new(), target = new();
        source.AddComponents(new[] { first, second }, true); source.Retain();
        source.SetIndex(1, 3);
        target.AddFlattenedComponents(source);
        try
        {
            Assert.Equal(1, source.ReferenceCount);
            Assert.Equal(4, owner.ReferenceCount);
            Assert.Equal(0, target.WriterIndex); Assert.Equal(2, target.Capacity);
            source.SetIndex(0, 4);
            Assert.Equal(new byte[] { 2, 3 }, target.AsReadOnlySequence(0, 2).ToArray());
            source.Release();
            Assert.Equal(2, owner.ReferenceCount);
            target.WriterIndex = 2;
            Assert.Equal(new byte[] { 2, 3 }, Bytes(target));
        }
        finally { target.Release(); }
        Assert.Equal(0, owner.ReferenceCount); Assert.Equal(0, allocator.ReservedBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FlattenUnreadableInputTransfersItsReferenceButAddsNoComponents(bool composite)
    {
        ByteBuf child = Unpooled.Buffer(2).WriteShort(7);
        ByteBuf source = composite ? new CompositeByteBuf().AddComponent(child, true) : child;
        source.ReaderIndex = source.WriterIndex;
        CompositeByteBuf target = new();
        try
        {
            target.AddFlattenedComponents(source, true);
            Assert.Equal(0, source.ReferenceCount); Assert.Equal(0, child.ReferenceCount);
            Assert.Equal(0, target.NumComponents); Assert.Equal(0, target.Capacity); Assert.Equal(0, target.WriterIndex);
            target.AddFlattenedComponents(Unpooled.EmptyBuffer, true);
            Assert.Equal(1, Unpooled.EmptyBuffer.ReferenceCount);
            Assert.Equal(0, target.NumComponents);
        }
        finally { target.Release(); }
    }

    [Fact]
    public void FlattenFailureRollsBackNewReferencesAndWriterButKeepsCallerInputOwned()
    {
        var allocator = new NativeMemoryAllocator(4);
        ByteBuf prefix = Segment(new byte[] { 1, 2 }, true, allocator);
        ByteBuf incoming = Segment(new byte[] { 3, 4 }, true, allocator);
        CompositeByteBuf target = new(1, true, allocator), source = new();
        target.AddComponent(prefix, true); target.ReaderIndex = 1; target.MarkReaderIndex(); target.MarkWriterIndex();
        source.AddComponent(incoming, true);
        try
        {
            Assert.Throws<OutOfMemoryException>(() => target.AddFlattenedComponents(source, true));
            Assert.Equal(1, target.NumComponents); Assert.Equal(2, target.Capacity);
            Assert.Equal(1, target.ReaderIndex); Assert.Equal(2, target.WriterIndex);
            Assert.Equal(new byte[] { 2 }, Bytes(target));
            target.ResetReaderIndex(); target.ResetWriterIndex();
            Assert.Equal(1, target.ReaderIndex); Assert.Equal(2, target.WriterIndex);
            Assert.Equal(1, source.ReferenceCount); Assert.Equal(1, incoming.ReferenceCount);
            Assert.Equal(new byte[] { 3, 4 }, Bytes(source)); Assert.Equal(4, allocator.ReservedBytes);
        }
        finally { source.Release(); target.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void FlattenRetainFailureReleasesPreviouslyAcquiredReferences()
    {
        ByteBuf first = Unpooled.WrappedBuffer(new byte[] { 1 });
        var failure = new InvalidOperationException("retain failure");
        var second = new FailingRetainByteBuf(Unpooled.WrappedBuffer(new byte[] { 2 }), failure);
        CompositeByteBuf source = new(), target = new();
        source.AddComponents(new ByteBuf[] { first, second }, true);
        try
        {
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => target.AddFlattenedComponents(source, true)));
            Assert.Equal(0, target.NumComponents); Assert.Equal(0, target.WriterIndex);
            Assert.Equal(1, source.ReferenceCount); Assert.Equal(1, first.ReferenceCount); Assert.Equal(1, second.ReferenceCount);
        }
        finally { source.Release(); target.Release(); }
    }

    [Fact]
    public void FlattenSourceReadFailureRestoresDestinationAndAllowsRetryAfterRepair()
    {
        var allocator = new NativeMemoryAllocator(8);
        ByteBuf incoming = Unpooled.WrappedBuffer(new byte[] { 3, 4 });
        CompositeByteBuf source = new(), target = new(1, true, allocator);
        source.AddComponent(incoming, true);
        target.AddComponent(Segment(new byte[] { 1 }, true, allocator), true);
        // The source layout captured two bytes; external shrink makes its range invalid.
        incoming.Capacity = 1;
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => target.AddFlattenedComponents(source, true));
            Assert.Equal(new byte[] { 1 }, Bytes(target));
            Assert.Equal(1, target.NumComponents); Assert.Equal(1, target.WriterIndex);
            Assert.Equal(1, source.ReferenceCount); Assert.Equal(1, incoming.ReferenceCount);
            Assert.Equal(1, allocator.ReservedBytes);
            incoming.Capacity = 2;
            target.AddFlattenedComponents(source, true);
            Assert.Equal(new byte[] { 1, 3, 0 }, Bytes(target));
            Assert.Equal(0, source.ReferenceCount); Assert.Equal(0, incoming.ReferenceCount);
            Assert.Equal(3, allocator.ReservedBytes);
        }
        finally { if (source.ReferenceCount > 0) source.Release(); target.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void FlattenSeparatelyRetainedSourceAlreadyOwnedByDestinationSurvivesConsolidation()
    {
        var allocator = new NativeMemoryAllocator(8);
        ByteBuf child = Segment(new byte[] { 1, 2 }, true, allocator);
        CompositeByteBuf source = new(), target = new(1, true, allocator);
        source.AddComponent(child, true);
        target.AddComponent(source, true);
        source.Retain().SetIndex(1, 2);
        try
        {
            target.AddFlattenedComponents(source, true);
            Assert.Equal(new byte[] { 1, 2, 2 }, Bytes(target));
            Assert.Equal(1, target.NumComponents);
            Assert.Equal(0, source.ReferenceCount); Assert.Equal(0, child.ReferenceCount);
            Assert.Equal(3, allocator.ReservedBytes);
        }
        finally { target.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void FlattenOverflowIsCheckedBeforeAcquiringOrTransferringReferences()
    {
        const int capacity = 1024 * 1024;
        ByteBuf owner = Unpooled.Buffer(capacity).WriteZero(capacity);
        CompositeByteBuf source = new(), target = new(int.MaxValue);
        for (int i = 0; i < 2047; ++i) target.AddComponent(owner.RetainedDuplicate());
        source.AddComponent(owner.RetainedDuplicate(), true);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => target.AddFlattenedComponents(source, true));
            Assert.Equal(2047, target.NumComponents); Assert.Equal(0, target.WriterIndex);
            Assert.Equal(1, source.ReferenceCount); Assert.Equal(2049, owner.ReferenceCount);
        }
        finally { source.Release(); target.Release(); owner.Release(); }
        Assert.Equal(0, owner.ReferenceCount);
    }

    [Fact]
    public void BatchAndFlattenRejectCyclesWithoutConsumingCyclicReferences()
    {
        CompositeByteBuf target = new();
        ByteBuf prefix = Unpooled.WrappedBuffer(new byte[] { 1 });
        ByteBuf tail = Unpooled.WrappedBuffer(new byte[] { 2 });
        try
        {
            Assert.Throws<ArgumentException>(() => target.AddComponents(new ByteBuf[] { prefix, null, target, tail }, true));
            Assert.Equal(1, prefix.ReferenceCount); Assert.Equal(1, tail.ReferenceCount);
            Assert.Equal(0, target.NumComponents); Assert.Equal(1, target.ReferenceCount);
            Assert.Throws<ArgumentException>(() => target.AddFlattenedComponents(target, true));
            Assert.Equal(1, target.ReferenceCount);
            Assert.Throws<ArgumentException>(() => target.AddComponents((IEnumerable<ByteBuf>)new ByteBuf[] { prefix, target, tail }, true));
            Assert.Equal(1, target.NumComponents); Assert.Equal(1, target.WriterIndex);
            Assert.Equal(0, tail.ReferenceCount); Assert.Equal(1, target.ReferenceCount);
        }
        finally { target.Release(); }
        Assert.Equal(0, prefix.ReferenceCount);
    }

    [Fact]
    public void OffsetSourceDiscardThenFurtherFlatteningKeepsCoordinatesCorrect()
    {
        // See https://github.com/netty/netty/issues/16799
        var allocator = new NativeMemoryAllocator(1024);
        CompositeByteBuf target = new();
        target.AddFlattenedComponents(Segment(new byte[] { 9, 9, 1, 1, 1, 1 }, true, allocator).SetIndex(2, 6), true);
        target.AddFlattenedComponents(Segment(new byte[] { 8, 8, 8, 2, 2, 2, 2 }, true, allocator).SetIndex(3, 7), true);
        try
        {
            target.SkipBytes(5); Assert.Equal(2, target.ReadByte());
            target.DiscardReadComponents();
            target.AddFlattenedComponents(Segment(new byte[] { 3, 3, 3, 3 }, true, allocator), true);
            target.AddFlattenedComponents(Segment(new byte[] { 4, 4, 4, 4 }, true, allocator), true);
            Assert.Equal(new byte[] { 2, 2, 3, 3, 3, 3, 4, 4, 4, 4 }, Bytes(target));
            target.SkipBytes(4); Assert.Equal(3, target.ReadByte());
        }
        finally { target.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    private sealed class TrackingEnumerable(ByteBuf[] buffers, Exception failure = null) : IEnumerable<ByteBuf>
    {
        private readonly ByteBuf[] _buffers = buffers;
        private readonly Exception _failure = failure;
        internal int EnumeratorCount, DisposeCount;
        public IEnumerator<ByteBuf> GetEnumerator() { ++EnumeratorCount; return new Enumerator(this); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        private sealed class Enumerator(TrackingEnumerable owner) : IEnumerator<ByteBuf>
        {
            private int _index = -1;
            private bool _failed;
            public ByteBuf Current => owner._buffers[_index];
            object IEnumerator.Current => Current;
            public bool MoveNext()
            {
                if (_index == 0 && owner._failure != null && !_failed) { _failed = true; throw owner._failure; }
                return ++_index < owner._buffers.Length;
            }
            public void Reset() => throw new NotSupportedException();
            public void Dispose() => ++owner.DisposeCount;
        }
    }
    private sealed class EnumerableHeapByteBuf : UnpooledHeapByteBuf, IEnumerable<ByteBuf>
    {
        internal EnumerableHeapByteBuf(byte[] bytes) : base(bytes.Length) { WriteBytes(bytes); }
        public IEnumerator<ByteBuf> GetEnumerator() => throw new InvalidOperationException("Buffer enumeration is forbidden in this fixture.");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private sealed class ObservingDirectByteBuf : UnpooledDirectByteBuf
    {
        private readonly Action _observer;
        internal ObservingDirectByteBuf(byte value, NativeMemoryAllocator allocator, Action observer)
            : base(1, allocator: allocator) { WriteByte(value); _observer = observer; }
        protected override Memory<byte> GetMemoryCore(int index, int length)
        { _observer?.Invoke(); return base.GetMemoryCore(index, length); }
    }
    private sealed class FailingRetainByteBuf : ByteBuf
    {
        private readonly ByteBuf _parent;
        private readonly Exception _failure;
        internal FailingRetainByteBuf(ByteBuf parent, Exception failure) : base(parent.MaxCapacity)
        { _parent = parent; _failure = failure; SetIndex(parent.ReaderIndex, parent.WriterIndex); }
        public override int Capacity { get => _parent.Capacity; set => _parent.Capacity = value; }
        public override bool IsDirect => _parent.IsDirect;
        public override int ReferenceCount => _parent.ReferenceCount;
        public override ByteBuf Retain(int increment = 1) => throw _failure;
        public override bool Release(int decrement = 1) => _parent.Release(decrement);
        public override ByteBuf Unwrap() => _parent;
        protected override Memory<byte> GetMemoryCore(int index, int length) => _parent.AsMemory(index, length);
    }
}
