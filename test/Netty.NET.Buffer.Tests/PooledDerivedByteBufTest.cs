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
using System.Collections.Generic;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class PooledDerivedByteBufTest
{
    private static ByteBuf Root(PooledByteBufAllocator a, bool direct, int length = 16)
    {
        ByteBuf b = direct ? new PooledDirectByteBuf((PoolDirectArena)a.DirectArenas[0], length, 64)
            : new PooledHeapByteBuf((PoolHeapArena)a.HeapArenas[0], length, 64);
        for (int i = 0; i < length; i++) b.WriteByte(i);
        return b;
    }
    public static IEnumerable<object[]> NestedCases()
    {
        foreach (bool direct in new[] { false, true })
            foreach (bool firstSlice in new[] { false, true })
                foreach (bool secondSlice in new[] { false, true })
                    foreach (bool childFirst in new[] { false, true }) yield return [direct, firstSlice, secondSlice, childFirst];
    }

    [Theory]
    [MemberData(nameof(NestedCases))]
    public void NestedRetainedViewsKeepEachParentAliveUntilTheirOwnFinalRelease(bool direct, bool firstSlice, bool secondSlice, bool childFirst)
    {
        var native = new NativeMemoryAllocator(); using var a = new PooledByteBufAllocator(nativeAllocator: native, maxOrder: 1);
        ByteBuf root = Root(a, direct), first = null, second = null;
        try
        {
            first = firstSlice ? root.RetainedSlice(2, 12) : root.RetainedDuplicate();
            Assert.Equal(2, root.ReferenceCount); Assert.Equal(1, first.ReferenceCount);
            second = secondSlice ? first.RetainedSlice(1, 8) : first.RetainedDuplicate();
            Assert.Equal(2, root.ReferenceCount); Assert.Equal(2, first.ReferenceCount); Assert.Equal(1, second.ReferenceCount);
            Assert.Same(root, first.Unwrap()); Assert.Same(root, second.Unwrap());
            int offset = (firstSlice ? 2 : 0) + (secondSlice ? 1 : 0);
            Assert.Equal(offset, second.GetByte(0)); second.SetByte(0, 99); Assert.Equal(99, root.GetByte(offset));
            Assert.False(root.Release());
            if (childFirst)
            {
                Assert.True(second.Release()); Assert.Equal(1, first.ReferenceCount); Assert.Equal(1, root.ReferenceCount);
                Assert.Throws<IllegalReferenceCountException>(() => second.GetByte(0));
                Assert.True(first.Release());
            }
            else
            {
                Assert.False(first.Release()); Assert.Equal(1, first.ReferenceCount);
                Assert.Equal(99, second.GetByte(0)); Assert.True(second.Release());
            }
            Assert.Equal(0, root.ReferenceCount); Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, second.ReferenceCount);
            Assert.Throws<IllegalReferenceCountException>(() => first.Unwrap());
            Assert.Throws<IllegalReferenceCountException>(() => first.Retain());
        }
        finally
        {
            if (second?.ReferenceCount > 0) second.Release(second.ReferenceCount);
            if (first?.ReferenceCount > 0) first.Release(first.ReferenceCount);
            if (root.ReferenceCount > 0) root.Release(root.ReferenceCount);
            a.Dispose();
        }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void BorrowedSlicesAndDuplicatesDelegateTheDerivedCounterRatherThanTheRoot(bool direct, bool sliced)
    {
        using var a = new PooledByteBufAllocator(maxOrder: 1);
        ByteBuf root = Root(a, direct), owner = sliced ? root.RetainedSlice(2, 8) : root.RetainedDuplicate();
        ByteBuf borrowed = owner.Slice(1, 4), alias = borrowed.Duplicate();
        try
        {
            Assert.Equal(1, borrowed.ReferenceCount); Assert.Equal(1, alias.ReferenceCount);
            Assert.Same(root, borrowed.Unwrap()); Assert.Same(root, alias.Unwrap());
            // A duplicate of a borrowed pooled slice uses the root's coordinates/capacity.
            Assert.Equal(16, alias.Capacity); Assert.Equal(64, alias.MaxCapacity);
            Assert.Equal((sliced ? 2 : 0) + 1, alias.ReaderIndex);
            Assert.Equal((sliced ? 2 : 0) + 5, alias.WriterIndex);
            Assert.Equal(0, alias.GetByte(0));
            alias.Retain(3); Assert.Equal(4, owner.ReferenceCount); Assert.Equal(2, root.ReferenceCount);
            Assert.False(borrowed.Release(3)); Assert.True(alias.Release());
            Assert.Equal(0, owner.ReferenceCount); Assert.Equal(1, root.ReferenceCount);
            Assert.Throws<IllegalReferenceCountException>(() => borrowed.GetByte(0));
            Assert.Throws<IllegalReferenceCountException>(() => alias.GetByte(0));
            Assert.Equal(0, root.GetByte(0)); // Root still has its independent caller reference.
        }
        finally { if (owner.ReferenceCount > 0) owner.Release(owner.ReferenceCount); root.Release(); }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void RetainedChildrenOfBorrowedViewsPreserveTheImmediateParentChain(bool direct, bool duplicate)
    {
        using var a = new PooledByteBufAllocator(maxOrder: 1);
        ByteBuf root = Root(a, direct), first = root.RetainedSlice(2, 8), borrowed = first.Slice(1, 4);
        ByteBuf child = duplicate ? borrowed.RetainedDuplicate() : borrowed.RetainedSlice(1, 2);
        try
        {
            Assert.Equal(2, root.ReferenceCount); Assert.Equal(2, first.ReferenceCount); Assert.Equal(1, child.ReferenceCount);
            Assert.Equal(duplicate ? 16 : 2, child.Capacity);
            Assert.Equal(duplicate ? 3 : 0, child.ReaderIndex);
            Assert.Equal(duplicate ? 7 : 2, child.WriterIndex);
            Assert.Equal(duplicate ? 0 : 4, child.GetByte(0));
            Assert.False(first.Release()); Assert.False(root.Release());
            Assert.True(child.Release()); Assert.Equal(0, first.ReferenceCount); Assert.Equal(0, root.ReferenceCount);
        }
        finally
        {
            if (child.ReferenceCount > 0) child.Release();
            if (first.ReferenceCount > 0) first.Release(first.ReferenceCount);
            if (root.ReferenceCount > 0) root.Release(root.ReferenceCount);
        }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void SiblingsHaveIndependentRetainReleaseCounts(bool direct, bool reverse)
    {
        using var a = new PooledByteBufAllocator(maxOrder: 1);
        ByteBuf root = Root(a, direct), slice = root.RetainedSlice(2, 8), duplicate = root.RetainedDuplicate();
        try
        {
            slice.Retain(2); Assert.Equal(3, slice.ReferenceCount); Assert.Equal(1, duplicate.ReferenceCount); Assert.Equal(3, root.ReferenceCount);
            root.Release(); Assert.Equal(2, root.ReferenceCount);
            ByteBuf first = reverse ? slice : duplicate, last = reverse ? duplicate : slice;
            Assert.True(first.Release(first.ReferenceCount)); Assert.Equal(1, root.ReferenceCount);
            Assert.Equal(reverse ? 0 : 2, last.GetByte(0)); Assert.True(last.Release(last.ReferenceCount));
            Assert.Equal(0, root.ReferenceCount);
        }
        finally
        {
            if (slice.ReferenceCount > 0) slice.Release(slice.ReferenceCount);
            if (duplicate.ReferenceCount > 0) duplicate.Release(duplicate.ReferenceCount);
            if (root.ReferenceCount > 0) root.Release(root.ReferenceCount);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DuplicateCapacityAndMarksFollowTheirOriginalRootOrSliceForm(bool direct)
    {
        using var a = new PooledByteBufAllocator(maxOrder: 1); ByteBuf root = Root(a, direct).SetIndex(3, 13);
        ByteBuf slice = root.RetainedSlice(2, 8).SetIndex(1, 7), duplicate = root.RetainedDuplicate();
        try
        {
            ByteBuf slicedDuplicate = slice.Duplicate(), rootDuplicate = duplicate.Duplicate();
            Assert.Equal(8, slicedDuplicate.Capacity); Assert.Equal(8, slicedDuplicate.MaxCapacity);
            slicedDuplicate.ResetReaderIndex().ResetWriterIndex(); Assert.Equal(0, slicedDuplicate.ReaderIndex); Assert.Equal(0, slicedDuplicate.WriterIndex);
            rootDuplicate.SetIndex(0, 0).ResetWriterIndex().ResetReaderIndex(); Assert.Equal(3, rootDuplicate.ReaderIndex); Assert.Equal(13, rootDuplicate.WriterIndex);
            duplicate.SetIndex(4, 12); duplicate.ResetReaderIndex().ResetWriterIndex(); Assert.Equal(3, duplicate.ReaderIndex); Assert.Equal(13, duplicate.WriterIndex);
            duplicate.Capacity = 32; Assert.Equal(32, root.Capacity); Assert.Equal(32, rootDuplicate.Capacity); Assert.Equal(8, slice.Capacity);
            Assert.Throws<NotSupportedException>(() => slice.Capacity = 8);
            ByteBuf copy = slice.Copy(1, 3);
            try { Assert.Equal(64, copy.MaxCapacity); Assert.Equal(new byte[] { 3, 4, 5 }, ByteBufUtil.GetBytes(copy)); }
            finally { copy.Release(); }
        }
        finally { slice.Release(); duplicate.Release(); root.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FailedBoundsAndReferenceOverflowDoNotAcquireExtraParents(bool direct)
    {
        using var a = new PooledByteBufAllocator(maxOrder: 1); ByteBuf root = Root(a, direct), owner = root.RetainedSlice(2, 8);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => root.RetainedSlice(15, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => owner.RetainedSlice(7, 2));
            ByteBuf borrowed = owner.Slice(1, 4);
            Assert.Throws<ArgumentOutOfRangeException>(() => borrowed.RetainedSlice(int.MaxValue, 1));
            Assert.Equal(2, root.ReferenceCount); Assert.Equal(1, owner.ReferenceCount);
            owner.Retain(int.MaxValue - 1); Assert.Equal(int.MaxValue, owner.ReferenceCount); Assert.Equal(2, root.ReferenceCount);
            Assert.Throws<IllegalReferenceCountException>(() => owner.Retain());
            Assert.True(owner.Release(int.MaxValue)); Assert.Equal(1, root.ReferenceCount);
        }
        finally { if (owner.ReferenceCount > 0) owner.Release(owner.ReferenceCount); root.Release(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalBorrowedRetainedSliceUsesTranslatedRootBounds(bool direct)
    {
        using var a = new PooledByteBufAllocator(maxOrder: 1); ByteBuf root = Root(a, direct), owner = root.RetainedSlice(2, 8);
        ByteBuf borrowed = owner.Slice(1, 2), child = borrowed.RetainedSlice(-1, 8);
        try { Assert.Equal(8, child.Capacity); Assert.Equal(2, child.GetByte(0)); Assert.Equal(9, child.GetByte(7)); }
        finally { child.Release(); owner.Release(); root.Release(); }
    }

    [Fact]
    public unsafe void NativePinsSurviveTheWholeDerivedChainAfterLogicalFinalRelease()
    {
        var native = new NativeMemoryAllocator(); using var a = new PooledByteBufAllocator(nativeAllocator: native, maxOrder: 1);
        ByteBuf root = Root(a, true), child = root.RetainedSlice(3, 8); var pin = child.AsMemory(0, 8).Pin();
        try
        {
            Assert.False(root.Release()); a.Dispose(); Assert.True(child.Release());
            Assert.Equal(0, root.ReferenceCount); Assert.Equal(0, a.PinnedDirectMemory); Assert.True(native.ReservedBytes > 0);
            Assert.Equal(3, ((byte*)pin.Pointer)[0]);
        }
        finally { pin.Dispose(); if (child.ReferenceCount > 0) child.Release(); if (root.ReferenceCount > 0) root.Release(root.ReferenceCount); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void IndependentAtomicCounterReleasesTheParentExactlyOnce(bool direct)
    {
        using var a = new PooledByteBufAllocator(maxOrder: 1); ByteBuf root = Root(a, direct), child = root.RetainedDuplicate();
        child.Retain(63); int final = 0;
        Parallel.For(0, 64, _ => { if (child.Release()) System.Threading.Interlocked.Increment(ref final); });
        Assert.Equal(1, final); Assert.Equal(0, child.ReferenceCount); Assert.Equal(1, root.ReferenceCount); Assert.True(root.Release());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LeakAwareRetainedChildrenUseIndependentTrackersAndCloseEveryParent(bool direct)
    {
        using var a = new PooledByteBufAllocator(maxOrder: 1); ByteBuf root = Root(a, direct);
        var spy = new Tracker(root); var wrapper = new SimpleLeakAwareByteBuf(root, spy);
        ByteBuf first = wrapper.RetainedSlice(2, 8), borrowed = first.Slice(1, 4), child = borrowed.RetainedDuplicate();
        var firstTracker = AllocatorLeakDetectionTest.Tracker(first); var childTracker = AllocatorLeakDetectionTest.Tracker(child);
        ByteBuf firstReferent = first.Unwrap(), childReferent = child.Unwrap();
        try
        {
            Assert.NotSame(spy, firstTracker); Assert.NotSame(firstTracker, childTracker);
            Assert.Same(firstTracker, AllocatorLeakDetectionTest.Tracker(borrowed));
            Assert.False(wrapper.Release()); Assert.False(first.Release()); Assert.Equal(0, spy.Closes);
            Assert.True(child.Release()); Assert.Equal(1, spy.Closes); Assert.Same(root, spy.Closed);
            Assert.False(firstTracker.Close(firstReferent)); Assert.False(childTracker.Close(childReferent));
            Assert.Equal(0, root.ReferenceCount);
        }
        finally
        {
            if (child.ReferenceCount > 0) child.Release();
            if (first.ReferenceCount > 0) first.Release(first.ReferenceCount);
            if (root.ReferenceCount > 0) wrapper.Release(root.ReferenceCount);
        }
    }

    [Fact]
    public void FailedLeakWrapperConstructionRollsBackItsParentReferenceAndForcedTracker()
    {
        using var a = new PooledByteBufAllocator(maxOrder: 1); ByteBuf root = Root(a, false);
        var spy = new Tracker(root); var failure = new Exception("wrapper factory failure");
        var wrapper = new FailingWrapper(root, spy, failure);
        try
        {
            Assert.Same(failure, Assert.Throws<Exception>(() => wrapper.RetainedDuplicate()));
            Assert.Equal(1, root.ReferenceCount); Assert.Equal(0, spy.Closes); Assert.Equal(0, wrapper.Derived.ReferenceCount);
            Assert.False(wrapper.Tracker.Close(wrapper.Derived));
        }
        finally { wrapper.Release(); }
        Assert.Equal(1, spy.Closes);
    }


    [Theory]
    [InlineData(false, false, false)] [InlineData(false, false, true)]
    [InlineData(false, true, false)] [InlineData(false, true, true)]
    [InlineData(true, false, false)] [InlineData(true, false, true)]
    [InlineData(true, true, false)] [InlineData(true, true, true)]
    public void OriginalMultipleLevelRetainedSliceWithNonRetained(bool direct, bool doSlice1, bool doSlice2)
    {
        using var a = new PooledByteBufAllocator(maxOrder: 1); ByteBuf root = Root(a, direct, 8);
        ByteBuf first = root.RetainedSlice(1, 6), second = null;
        try
        {
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, ByteBufUtil.GetBytes(first));
            // Simulate a handler that releases the original buffer, and propagates a slice.
            root.Release();
            second = first.RetainedSlice(1, 4);
            Assert.Equal(new byte[] { 2, 3, 4, 5 }, ByteBufUtil.GetBytes(second));
            Assert.Equal(0, second.CompareTo(second.Duplicate())); Assert.Equal(0, second.CompareTo(second.Slice()));
            ByteBuf temporary = second.RetainedDuplicate(); Assert.Equal(0, second.CompareTo(temporary)); temporary.Release();
            temporary = second.RetainedSlice(); Assert.Equal(0, second.CompareTo(temporary)); temporary.Release();
            ByteBuf third = doSlice1 ? second.Slice(1, 2) : second.Duplicate();
            Assert.Equal(doSlice1 ? new byte[] { 3, 4 } : new byte[] { 2, 3, 4, 5 }, ByteBufUtil.GetBytes(third));
            ByteBuf fourth = doSlice2 ? third.Slice(third.ReaderIndex + 1, 1) : third.Duplicate();
            if (doSlice2) Assert.Equal(new byte[] { doSlice1 ? (byte)4 : (byte)3 }, ByteBufUtil.GetBytes(fourth));
            else Assert.Equal(0, third.CompareTo(fourth));
            // Cleanup the expected buffers used for testing.
            // CLR expectations are managed byte arrays, so there are no expected ByteBuf references to release.
            // Slice 4, 3, and 2 should effectively "share" a reference count.
            fourth.Release(); Assert.Equal(third.ReferenceCount, second.ReferenceCount); Assert.Equal(third.ReferenceCount, fourth.ReferenceCount);
            // Slice 1 should also release the original underlying buffer without throwing exceptions
            Assert.True(first.Release());
            // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
            // be deallocated and have a reference count of 0.
            Assert.Equal(0, root.ReferenceCount); Assert.Equal(0, first.ReferenceCount);
            Assert.Equal(0, second.ReferenceCount); Assert.Equal(0, third.ReferenceCount);
        }
        finally
        {
            if (second?.ReferenceCount > 0) second.Release(second.ReferenceCount);
            if (first.ReferenceCount > 0) first.Release(first.ReferenceCount);
            if (root.ReferenceCount > 0) root.Release(root.ReferenceCount);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EmptyRetainedSlicesRemainIndependentlyOwned(bool direct)
    {
        using var a = new PooledByteBufAllocator(maxOrder: 1); ByteBuf root = Root(a, direct), empty = root.RetainedSlice(16, 0);
        try
        {
            Assert.NotSame(Unpooled.EmptyBuffer, empty); Assert.Equal(0, empty.Capacity); Assert.Equal(0, empty.MaxCapacity);
            Assert.Equal(2, root.ReferenceCount); Assert.Equal(1, empty.ReferenceCount); Assert.True(empty.CanRead(0));
            Assert.False(root.Release()); Assert.True(empty.Release()); Assert.Equal(0, root.ReferenceCount);
        }
        finally { if (empty.ReferenceCount > 0) empty.Release(); if (root.ReferenceCount > 0) root.Release(root.ReferenceCount); }
    }

    private sealed class Tracker(ByteBuf expected) : IResourceLeakTracker<ByteBuf>
    {
        internal int Closes; internal ByteBuf Closed;
        public void Record() { } public void Record(object hint) { }
        public bool Close(ByteBuf b) { Assert.Same(expected, b); Assert.Equal(0, b.ReferenceCount); Closed = b; return ++Closes == 1; }
        public Exception GetCloseStackTraceIfAny() => null;
    }
    private sealed class FailingWrapper(ByteBuf root, IResourceLeakTracker<ByteBuf> leak, Exception failure) : SimpleLeakAwareByteBuf(root, leak)
    {
        internal ByteBuf Derived; internal IResourceLeakTracker<ByteBuf> Tracker;
        protected override SimpleLeakAwareByteBuf NewLeakAwareByteBuf(ByteBuf buffer, ByteBuf tracked, IResourceLeakTracker<ByteBuf> tracker)
        { Derived = buffer; Tracker = tracker; throw failure; }
    }
}
