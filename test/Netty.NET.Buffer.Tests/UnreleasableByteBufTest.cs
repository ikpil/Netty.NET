/*
 * Copyright 2013 The Netty Project
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
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// The full pinned UnreleaseableByteBufTest (upstream spelling), selected
// AbstractByteBufTest retained-view scenarios, and CLR delegation/lifetime regressions.
public class UnreleasableByteBufTest
{
    private static ByteBuf NewOwner(int kind)
    {
        if (kind == 0) return Unpooled.Buffer(8, 64);
        if (kind == 1) return Unpooled.DirectBuffer(8, 64);
        return new CompositeByteBuf().AddComponent(Unpooled.Buffer(4).WriteZero(4), true)
            .AddComponent(Unpooled.Buffer(4).WriteZero(4), true).Clear();
    }

    private static void Free(ByteBuf owner)
    {
        if (owner.ReferenceCount > 0) owner.Release(owner.ReferenceCount);
    }

    [Fact]
    public void OriginalCantRelease()
    {
        ByteBuf owner = Unpooled.CopyInt(1);
        ByteBuf buffer = Unpooled.UnreleasableBuffer(owner);
        try
        {
            Assert.Equal(1, buffer.ReferenceCount);
            Assert.False(buffer.Release());
            Assert.Equal(1, buffer.ReferenceCount);
            Assert.False(buffer.Release());
            Assert.Equal(1, buffer.ReferenceCount);
            buffer.Retain(5);
            Assert.Equal(1, buffer.ReferenceCount);
            buffer.Retain();
            Assert.Equal(1, buffer.ReferenceCount);
            Assert.True(buffer.Unwrap().Release());
            Assert.Equal(0, buffer.ReferenceCount);
        }
        finally { Free(owner); }
    }

    [Fact]
    public void OriginalWrappedReadOnly()
    {
        ByteBuf owner = Unpooled.Buffer(1).AsReadOnly();
        ByteBuf buffer = Unpooled.UnreleasableBuffer(owner);
        try
        {
            Assert.Same(buffer, buffer.AsReadOnly());
            Assert.True(buffer.Unwrap().Release());
        }
        finally { Free(owner); }
    }

    public static IEnumerable<object[]> RetainedViews()
    {
        for (int kind = 0; kind < 3; ++kind)
        for (int shape = 0; shape < 3; ++shape)
        foreach (bool initial in new[] { false, true })
        foreach (bool final in new[] { false, true })
            yield return new object[] { kind, shape, initial, final };
    }

    [Theory]
    [MemberData(nameof(RetainedViews))]
    public void OriginalRetainedViewCombinationsAcquireNoAdditionalReference(int kind, int shape, bool initial, bool final)
    {
        ByteBuf owner = NewOwner(kind);
        try
        {
            ByteBuf first = shape == 2
                ? initial ? owner.RetainedDuplicate() : owner.Duplicate().Retain()
                : initial ? owner.RetainedSlice() : owner.Slice().Retain();
            ByteBuf second = Unpooled.UnreleasableBuffer(first);
            ByteBuf third = shape switch
            {
                0 => final ? second.RetainedSlice() : second.Slice().Retain(),
                1 => final ? second.ReadRetainedSlice(second.ReadableBytes) : second.ReadSlice(second.ReadableBytes).Retain(),
                _ => final ? second.RetainedDuplicate() : second.Duplicate().Retain()
            };
            Assert.Equal(2, owner.ReferenceCount);
            Assert.False(third.Release());
            Assert.False(second.Release());
            Assert.False(first.Release());
            Assert.True(owner.Release());
            Assert.Equal(0, first.ReferenceCount);
            Assert.Equal(0, owner.ReferenceCount);
            Assert.Throws<IllegalReferenceCountException>(() => third.AsReadOnlyMemory(0, 0));
        }
        finally { Free(owner); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ParentAndWrapperShareIndicesMarksAndStorage(int kind)
    {
        ByteBuf owner = NewOwner(kind).WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6 });
        ByteBuf buffer = Unpooled.UnreleasableBuffer(owner);
        try
        {
            Assert.Same(owner, buffer.Unwrap());
            Assert.Equal(owner.IsDirect, buffer.IsDirect);
            Assert.Equal(1, buffer.ReadByte());
            Assert.Equal(1, owner.ReaderIndex);
            owner.ReaderIndex = 2;
            Assert.Equal(2, buffer.ReaderIndex);
            buffer.MarkReaderIndex();
            owner.ReaderIndex = 4;
            owner.ResetReaderIndex();
            Assert.Equal(2, buffer.ReaderIndex);
            owner.MarkWriterIndex();
            buffer.WriterIndex = 5;
            buffer.ResetWriterIndex();
            Assert.Equal(6, owner.WriterIndex);
            Assert.Same(buffer, buffer.SetIndex(1, 5));
            Assert.Equal(1, owner.ReaderIndex);
            Assert.Equal(5, owner.WriterIndex);
            buffer.SetByte(2, 77);
            Assert.Equal(77, owner.GetByte(2));
            Assert.Same(buffer, buffer.Clear());
            buffer.WriteInt(0x12345678);
            Assert.Equal(4, owner.WriterIndex);
            Assert.Equal(0x12345678, owner.ReadInt());
            Assert.Equal(4, buffer.ReaderIndex);
        }
        finally { Free(owner); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AllDerivedViewsIgnoreLifetimeCallsAndKeepTheirOwnIndices(int kind)
    {
        ByteBuf owner = NewOwner(kind).WriteBytes(new byte[] { 1, 2, 3, 4 });
        ByteBuf buffer = Unpooled.UnreleasableBuffer(owner);
        try
        {
            ByteBuf[] views = { buffer.Slice(), buffer.Slice(1, 2), buffer.RetainedSlice(), buffer.RetainedSlice(1, 2),
                buffer.Duplicate(), buffer.RetainedDuplicate(), buffer.AsReadOnly(), buffer.ReadSlice(1), buffer.ReadRetainedSlice(1) };
            Assert.Equal(2, owner.ReaderIndex);
            Assert.Equal(1, owner.ReferenceCount);
            foreach (ByteBuf view in views)
            {
                Assert.Same(view, view.Retain(-1));
                Assert.False(view.Release(int.MaxValue));
                Assert.Same(view, view.Touch("ignored"));
                Assert.Equal(1, view.ReferenceCount);
            }
            views[0].ReaderIndex = 1;
            Assert.Equal(2, buffer.ReaderIndex);
            ByteBuf readOnly = views[6];
            Assert.Same(readOnly, readOnly.AsReadOnly());
            Assert.Throws<NotSupportedException>(() => readOnly.SetByte(0, 1));
            Assert.Throws<NotSupportedException>(() => readOnly.AsMemory(0, 0));
            Assert.True(owner.Release());
            foreach (ByteBuf view in views)
            {
                Assert.Equal(0, view.ReferenceCount);
                Assert.False(view.Release());
                Assert.Throws<IllegalReferenceCountException>(() => view.AsReadOnlyMemory(0, 0));
            }
        }
        finally { Free(owner); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NestedWrappingRemovesTheRedundantLayerAndIgnoresAllCounts(int kind)
    {
        ByteBuf owner = NewOwner(kind);
        ByteBuf first = Unpooled.UnreleasableBuffer(owner), second = Unpooled.UnreleasableBuffer(first);
        try
        {
            Assert.NotSame(first, second);
            Assert.Same(owner, second.Unwrap());
            foreach (int count in new[] { int.MinValue, -1, 0, 1, int.MaxValue })
            {
                IReferenceCounted reference = second;
                Assert.Same(second, reference.Retain(count));
                Assert.False(reference.Release(count));
                Assert.Same(second, reference.Touch());
                Assert.Same(second, reference.Touch(count));
                Assert.Equal(1, owner.ReferenceCount);
            }
            first.WriteByte(7);
            Assert.Equal(1, second.WriterIndex);
            Assert.Equal(7, second.ReadByte());
            Assert.Equal(1, first.ReaderIndex);
            Assert.True(owner.Release());
            Assert.Same(second, second.Retain());
            Assert.False(second.Release());
            Assert.Equal(0, second.ReferenceCount);
        }
        finally { Free(owner); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CopiesHaveOrdinaryLifetimeAndRemainReadableAfterOriginalOwnerDies(int kind)
    {
        ByteBuf owner = NewOwner(kind).WriteBytes(new byte[] { 1, 2, 3, 4 });
        ByteBuf buffer = Unpooled.UnreleasableBuffer(owner);
        ByteBuf copy = null, read = null;
        try
        {
            copy = buffer.Copy(1, 2);
            read = buffer.ReadBytes(2);
            Assert.Equal(2, owner.ReaderIndex);
            Assert.Equal(1, owner.ReferenceCount);
            owner.SetByte(1, 7);
            Assert.True(owner.Release());
            Assert.Equal(new byte[] { 2, 3 }, copy.ReadableSequence.ToArray());
            Assert.Equal(new byte[] { 1, 2 }, read.ReadableSequence.ToArray());
            Assert.True(copy.Release()); copy = null;
            Assert.True(read.Release()); read = null;
        }
        finally { copy?.Release(); read?.Release(); Free(owner); }
    }

    [Fact]
    public void CompositeDiscardsUseTheParentsStructuralPolicy()
    {
        ByteBuf first = Unpooled.CopyShort(0x0102), second = Unpooled.CopyShort(0x0304), third = Unpooled.CopyShort(0x0506);
        CompositeByteBuf owner = new CompositeByteBuf().AddComponents(new[] { first, second, third }, true);
        ByteBuf buffer = Unpooled.UnreleasableBuffer(owner);
        try
        {
            buffer.ReaderIndex = 3;
            Assert.Same(buffer, buffer.DiscardSomeReadBytes());
            Assert.Equal(0, first.ReferenceCount);
            Assert.Equal(4, buffer.Capacity);
            Assert.Equal(1, owner.ReaderIndex);
            Assert.Equal(4, owner.WriterIndex);
            Assert.Same(buffer, buffer.DiscardReadBytes());
            Assert.Equal(3, buffer.Capacity);
            Assert.Equal(0, owner.ReaderIndex);
            Assert.Equal(new byte[] { 4, 5, 6 }, buffer.ReadableSequence.ToArray());
        }
        finally { Free(owner); }
        Assert.Equal(0, second.ReferenceCount);
        Assert.Equal(0, third.ReferenceCount);
    }

    [Fact]
    public void NativeAliasedWritesPinTheOwnerAcrossGrowthAndCapacityChangesShareIndices()
    {
        var allocator = new NativeMemoryAllocator(128);
        ByteBuf owner = new UnpooledDirectByteBuf(4, 64, allocator).WriteInt(0x01020304);
        ByteBuf buffer = Unpooled.UnreleasableBuffer(owner);
        try
        {
            buffer.WriteBytes(owner.AsReadOnlySpan(0, 4));
            Assert.Equal(new byte[] { 1, 2, 3, 4, 1, 2, 3, 4 }, owner.ReadableMemory.ToArray());
            Assert.Equal(8, owner.WriterIndex);
            Assert.Equal(64, buffer.Capacity);
            buffer.ReaderIndex = 6;
            buffer.Capacity = 3;
            Assert.Equal(3, buffer.ReaderIndex);
            Assert.Equal(3, buffer.WriterIndex);
            Assert.Equal(3, allocator.ReservedBytes);
            Assert.False(buffer.Release());
            Assert.Equal(3, allocator.ReservedBytes);
        }
        finally { Free(owner); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void CompositeOwnershipOfUnreleasableInputDoesNotConsumeTheActualOwner()
    {
        ByteBuf owner = Unpooled.CopyInt(0x01020304);
        CompositeByteBuf composite = Unpooled.CompositeBuffer();
        try
        {
            composite.AddComponent(Unpooled.UnreleasableBuffer(owner), true);
            Assert.Equal(0x01020304, composite.ReadInt());
            Assert.True(composite.Release());
            Assert.Equal(1, owner.ReferenceCount);
            Assert.Equal(0x01020304, owner.GetInt(0));
        }
        finally { Free(composite); Free(owner); }
    }

    [Fact]
    public void NullEmptyAndDeadOwnerContractsRemainExplicit()
    {
        Assert.Throws<ArgumentNullException>(() => Unpooled.UnreleasableBuffer(null));
        ByteBuf empty = Unpooled.UnreleasableBuffer(Unpooled.EmptyBuffer);
        Assert.NotSame(Unpooled.EmptyBuffer, empty);
        Assert.Same(Unpooled.EmptyBuffer, empty.Unwrap());
        Assert.False(empty.CanWrite(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.IndexOf(0, 1, 1));
        Assert.Same(Unpooled.EmptyBuffer, empty.Copy());
        Assert.False(empty.Release());
        ByteBuf owner = Unpooled.Buffer(1);
        owner.Release();
        ByteBuf dead = Unpooled.UnreleasableBuffer(owner);
        Assert.Equal(0, dead.ReferenceCount);
        Assert.Same(dead, dead.Retain(5));
        Assert.False(dead.Release(-1));
        Assert.Throws<IllegalReferenceCountException>(() => dead.GetByte(0));
        Assert.Throws<IllegalReferenceCountException>(() => dead.Duplicate());
    }

    [Fact]
    public void TouchDoesNotReachTheUnderlyingOwner()
    {
        var owner = new ObservedHeapByteBuf();
        try
        {
            ByteBuf buffer = Unpooled.UnreleasableBuffer(owner);
            Assert.Same(buffer, buffer.Touch());
            Assert.Same(buffer, buffer.Touch("ignored"));
            ((IReferenceCounted)buffer).Touch(new object());
            buffer.Duplicate().Touch();
            buffer.AsReadOnly().Touch();
            Assert.Equal(0, owner.TouchCalls);
            Assert.Equal(1, owner.ReferenceCount);
        }
        finally { Free(owner); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InvalidDerivedRangesPreserveParentIndicesAndReferences(int kind)
    {
        ByteBuf owner = NewOwner(kind).WriteInt(1);
        ByteBuf buffer = Unpooled.UnreleasableBuffer(owner);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.ReadSlice(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.ReadRetainedSlice(5));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Slice(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.RetainedSlice(1, int.MaxValue));
            Assert.Equal(0, owner.ReaderIndex);
            Assert.Equal(4, owner.WriterIndex);
            Assert.Equal(1, owner.ReferenceCount);
        }
        finally { Free(owner); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void BulkTransfersAdvanceBothIndexOwnersIncludingAliasedInput(int kind)
    {
        ByteBuf owner = NewOwner(kind).WriteBytes(new byte[] { 1, 2, 3, 4 });
        ByteBuf destination = NewOwner(kind);
        try
        {
            ByteBuf buffer = Unpooled.UnreleasableBuffer(owner);
            buffer.WriteBytes(owner, 2);
            Assert.Equal(2, owner.ReaderIndex);
            Assert.Equal(6, owner.WriterIndex);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 1, 2 }, buffer.AsReadOnlySequence(0, 6).ToArray());
            ByteBuf target = Unpooled.UnreleasableBuffer(destination);
            Assert.Same(buffer, buffer.ReadBytes(target, 3));
            Assert.Equal(5, owner.ReaderIndex);
            Assert.Equal(3, destination.WriterIndex);
            Assert.Equal(new byte[] { 3, 4, 1 }, destination.ReadableSequence.ToArray());
            Assert.Equal(1, owner.ReferenceCount);
            Assert.Equal(1, destination.ReferenceCount);
        }
        finally { Free(owner); Free(destination); }
    }

    private sealed class ObservedHeapByteBuf : UnpooledHeapByteBuf
    {
        internal int TouchCalls;
        internal ObservedHeapByteBuf() : base(1) { }
        public override ByteBuf Touch(object hint = null) { ++TouchCalls; return this; }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void DerivedCopiesPreserveParentAllocationPolicyAndMaximum(int kind, bool readOnly)
    {
        ByteBuf owner = NewOwner(kind).WriteBytes(new byte[] { 1, 2, 3, 4 });
        ByteBuf source = readOnly ? owner.AsReadOnly() : owner;
        ByteBuf buffer = Unpooled.UnreleasableBuffer(source);
        try
        {
            foreach (ByteBuf view in new[] { buffer.Slice(1, 2), buffer.Slice().Slice(1, 2),
                         buffer.Duplicate().Slice(1, 2), buffer.RetainedSlice(1, 2) })
            {
                ByteBuf copy = view.Copy();
                try
                {
                    Assert.Equal(kind == 1, copy.IsDirect);
                    Assert.False(copy.IsReadOnly);
                    Assert.Equal(owner.MaxCapacity, copy.MaxCapacity);
                    Assert.Equal(2, copy.Capacity);
                    Assert.Equal(new byte[] { 2, 3 }, copy.ReadableMemory.ToArray());
                    copy.WriteByte(7);
                    Assert.Equal(0, source.ReaderIndex);
                    Assert.Equal(4, source.WriterIndex);
                    Assert.Equal(1, owner.ReferenceCount);
                }
                finally { Assert.True(copy.Release()); }
            }
        }
        finally { Free(owner); }
    }
}
