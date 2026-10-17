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
using System.Linq;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

/**
 * Tests wrapping a wrapped buffer does not go way too deep chaining.
 */
public class ByteBufDerivationTest
{
    private static void WithRoot(bool direct, Action<ByteBuf> test, int capacity = 8)
    {
        var native = new NativeMemoryAllocator();
        ByteBuf root = direct ? new UnpooledDirectByteBuf(capacity, capacity * 2, native) : new UnpooledHeapByteBuf(capacity, capacity * 2);
        root.WriteBytes(Enumerable.Range(0, capacity).Select(i => unchecked((byte)i)).ToArray());
        try { test(root); }
        finally { root.Release(); }
        Assert.Equal(0, native.ReservedBytes);
    }

    private static void SameShape(ByteBuf expected, ByteBuf actual)
    {
        Assert.Equal(expected.ReaderIndex, actual.ReaderIndex); Assert.Equal(expected.WriterIndex, actual.WriterIndex);
        Assert.Equal(expected.Capacity, actual.Capacity); Assert.Equal(expected.MaxCapacity, actual.MaxCapacity);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void TestSlice(bool direct) => WithRoot(direct, root =>
    {
        root.SetIndex(1, 7); ByteBuf slice = root.Slice(1, 7);
        Assert.Same(root, slice.Unwrap()); Assert.Equal(0, slice.ReaderIndex); Assert.Equal(7, slice.WriterIndex);
        Assert.Equal(7, slice.Capacity); Assert.Equal(7, slice.MaxCapacity); slice.SetIndex(1, 6);
        Assert.Equal(1, root.ReaderIndex); Assert.Equal(7, root.WriterIndex);
    });

    [Theory] [InlineData(false)] [InlineData(true)]
    public void TestSliceOfSlice(bool direct) => WithRoot(direct, root =>
    {
        ByteBuf slice = root.Slice(1, 7), second = slice.Slice(0, 6);
        Assert.NotSame(slice, second); Assert.Same(root, second.Unwrap()); Assert.Equal(6, second.WriterIndex); Assert.Equal(6, second.Capacity);
    });

    [Theory] [InlineData(false)] [InlineData(true)]
    public void TestDuplicate(bool direct) => WithRoot(direct, root =>
    {
        root.SetIndex(1, 7); ByteBuf duplicate = root.Duplicate();
        Assert.Same(root, duplicate.Unwrap()); SameShape(root, duplicate); duplicate.SetIndex(2, 6);
        Assert.Equal(1, root.ReaderIndex); Assert.Equal(7, root.WriterIndex);
    });

    [Theory] [InlineData(false)] [InlineData(true)]
    public void TestDuplicateOfDuplicate(bool direct) => WithRoot(direct, root =>
    {
        root.SetIndex(1, 7); ByteBuf duplicate = root.Duplicate().SetIndex(2, 6), second = duplicate.Duplicate();
        Assert.NotSame(duplicate, second); Assert.Same(root, second.Unwrap()); SameShape(duplicate, second);
    });

    [Theory] [InlineData(false)] [InlineData(true)]
    public void TestReadOnly(bool direct) => WithRoot(direct, root =>
    {
        root.SetIndex(1, 7); ByteBuf readOnly = root.AsReadOnly();
        Assert.True(readOnly.IsReadOnly); Assert.Same(root, readOnly.Unwrap()); SameShape(root, readOnly);
        readOnly.SetIndex(2, 6); Assert.Equal(1, root.ReaderIndex);
    });

    [Theory] [InlineData(false)] [InlineData(true)]
    public void TestReadOnlyOfReadOnly(bool direct) => WithRoot(direct, root =>
    {
        root.SetIndex(1, 7); ByteBuf readOnly = root.AsReadOnly().SetIndex(2, 6), second = readOnly.Duplicate();
        // CLR AsReadOnly is idempotent; Duplicate supplies the independent read-only view
        // requested by the original deprecated Unpooled.unmodifiableBuffer factory.
        Assert.NotSame(readOnly, second); Assert.True(second.IsReadOnly); Assert.Same(root, second.Unwrap()); SameShape(readOnly, second);
    });

    [Theory] [InlineData(false)] [InlineData(true)]
    public void TestReadOnlyOfDuplicate(bool direct) => WithRoot(direct, root =>
    {
        root.SetIndex(1, 7); ByteBuf duplicate = root.Duplicate().SetIndex(2, 6), readOnly = duplicate.AsReadOnly();
        Assert.True(readOnly.IsReadOnly); Assert.Same(root, readOnly.Unwrap()); SameShape(duplicate, readOnly);
    });

    [Theory] [InlineData(false)] [InlineData(true)]
    public void TestDuplicateOfReadOnly(bool direct) => WithRoot(direct, root =>
    {
        root.SetIndex(1, 7); ByteBuf readOnly = root.AsReadOnly().SetIndex(2, 6), duplicate = readOnly.Duplicate();
        Assert.True(duplicate.IsReadOnly); Assert.Same(root, duplicate.Unwrap()); SameShape(readOnly, duplicate);
    });

    private static int Depth(ByteBuf buffer)
    {
        int depth = 0;
        while (buffer.Unwrap() is { } parent) { if (++depth > 4) break; buffer = parent; }
        return depth;
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void TestSwapThroughExplicitAccessorsSharesTheOriginalCursorOwner(bool direct) => WithRoot(direct, root =>
    {
        root.SetIndex(1, 7);
        Assert.Equal(0x04030201, root.GetIntLE(root.ReaderIndex)); Assert.Equal(0x01020304, root.GetInt(root.ReaderIndex));
        root.SetIndex(2, 6);
        Assert.Equal(0x05040302, root.GetIntLE(root.ReaderIndex)); Assert.Equal(0x02030405, root.GetInt(root.ReaderIndex));
        Assert.Equal(2, root.ReaderIndex); Assert.Equal(6, root.WriterIndex); Assert.Equal(1, root.ReferenceCount);
    });

    [Theory] [InlineData(false)] [InlineData(true)]
    public void FullMixtureKeepsOrderSelectionAtTheCallSiteAcrossTenThousandDerivations(bool direct) => WithRoot(direct, root =>
    {
        uint random = 0x12345678; ByteBuf derived = root; int offset = 0; bool little = false;
        for (int i = 0; i < 10000; i++)
        {
            random = unchecked(random * 1664525 + 1013904223);
            switch ((random >> 16) % 4)
            {
                case 0: derived = derived.Slice(1, derived.Capacity - 1); offset++; break;
                case 1: derived = derived.Duplicate(); break;
                case 2: little = !little; break;
                case 3: derived = new ReadOnlyByteBuf(derived); break;
            }
            byte[] bytes = Enumerable.Range(offset, 4).Select(value => unchecked((byte)value)).ToArray();
            long expected = 0; foreach (byte value in little ? bytes.Reverse() : bytes) expected = (expected << 8) | value;
            Assert.Equal(unchecked((int)expected), little ? derived.GetIntLE(0) : derived.GetInt(0));
            Assert.InRange(Depth(derived), 0, 2); Assert.Equal(1, derived.ReferenceCount);
        }
    }, 10000);

    [Theory] [InlineData(false)] [InlineData(true)]
    public void PortableMixtureKeepsTenThousandDerivationsWithinTwoWrappers(bool direct) => WithRoot(direct, root =>
    {
        uint random = 0x12345678; ByteBuf derived = root; int offset = 0;
        for (int i = 0; i < 10000; i++)
        {
            random = unchecked(random * 1664525 + 1013904223);
            derived = (random % 3) switch
            {
                0 => NextSlice(), 1 => derived.Duplicate(), _ => new ReadOnlyByteBuf(derived)
            };
            ByteBuf NextSlice() { offset++; return derived.Slice(1, derived.Capacity - 1); }
            Assert.InRange(Depth(derived), 1, 2); Assert.Equal(unchecked((byte)offset), derived.GetByte(0)); Assert.Equal(1, derived.ReferenceCount);
        }
    }, 10000);

    [Theory] [InlineData(false)] [InlineData(true)]
    public void DuplicateOfSliceKeepsFixedBoundsAndFreshSliceMarks(bool direct) => WithRoot(direct, root =>
    {
        ByteBuf slice = root.Slice(2, 6).SetIndex(1, 5), duplicate = slice.Duplicate();
        Assert.Same(root, duplicate.Unwrap()); SameShape(slice, duplicate);
        duplicate.ResetReaderIndex(); duplicate.ResetWriterIndex(); Assert.Equal(0, duplicate.ReaderIndex); Assert.Equal(0, duplicate.WriterIndex);
        Assert.Throws<NotSupportedException>(() => duplicate.Capacity = 7);
        duplicate.SetByte(0, 77); Assert.Equal(77, root.GetByte(2)); Assert.Equal(1, slice.ReaderIndex); Assert.Equal(5, slice.WriterIndex);
    });

    [Theory] [InlineData(false)] [InlineData(true)]
    public void FlattenedViewsShareLiveRootBoundsAndRetainedOwnership(bool direct)
    {
        var native = new NativeMemoryAllocator();
        ByteBuf root = direct ? new UnpooledDirectByteBuf(8, 32, native) : new UnpooledHeapByteBuf(8, 32);
        root.WriteLong(0x0102030405060708);
        ByteBuf retained = root.Slice(1, 7).Duplicate().RetainedSlice(1, 5);
        Assert.Equal(2, root.ReferenceCount); Assert.Same(root, retained.Unwrap());
        root.Capacity = 4; Assert.Equal(3, retained.GetByte(0)); Assert.Throws<ArgumentOutOfRangeException>(() => retained.GetByte(4));
        root.Capacity = 16; retained.SetByte(4, 99); Assert.Equal(99, root.GetByte(6));
        Assert.False(root.Release()); Assert.Equal(3, retained.GetByte(0)); Assert.True(retained.Release());
        Assert.Throws<IllegalReferenceCountException>(() => retained.GetByte(0)); Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void PooledRetainedOwnersKeepTheirIndependentCounterAcrossReadOnlyDerivation(bool direct, bool readOnly)
    {
        var native = new NativeMemoryAllocator(); using var allocator = new PooledByteBufAllocator(nativeAllocator: native, maxOrder: 1);
        ByteBuf root = direct ? new PooledDirectByteBuf((PoolDirectArena)allocator.DirectArenas[0], 8, 32)
            : new PooledHeapByteBuf((PoolHeapArena)allocator.HeapArenas[0], 8, 32);
        root.WriteLong(0x0102030405060708);
        ByteBuf owner = root.RetainedSlice(1, 7);
        try
        {
            ByteBuf borrowed = (readOnly ? owner.AsReadOnly() : owner).Duplicate().Slice(1, 4).Duplicate();
            Assert.Equal(1, borrowed.ReferenceCount); Assert.Equal(2, root.ReferenceCount); Assert.Equal(3, borrowed.GetByte(readOnly ? 0 : 2));
            borrowed.Retain(); Assert.Equal(2, owner.ReferenceCount); Assert.Equal(2, root.ReferenceCount);
            Assert.False(owner.Release()); Assert.True(borrowed.Release()); Assert.Equal(0, owner.ReferenceCount); Assert.Equal(1, root.ReferenceCount);
            Assert.Throws<IllegalReferenceCountException>(() => borrowed.GetByte(0)); Assert.Equal(1, root.GetByte(0));
        }
        finally { if (owner.ReferenceCount > 0) owner.Release(owner.ReferenceCount); root.Release(); allocator.Dispose(); }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void LeakFacadesKeepOneSharedTrackerAcrossNormalizedDerivations(bool direct, bool advanced)
    {
        var native = new NativeMemoryAllocator();
        ByteBuf root = direct ? new UnpooledDirectByteBuf(8, 16, native) : new UnpooledHeapByteBuf(8, 16);
        root.WriteLong(0x0102030405060708); var tracker = new Tracker(root);
        ByteBuf facade = advanced ? new AdvancedLeakAwareByteBuf(root, tracker) : new SimpleLeakAwareByteBuf(root, tracker);
        try
        {
            ByteBuf derived = facade.Slice(1, 7).Duplicate().Slice(1, 5).Duplicate().AsReadOnly().Duplicate();
            Assert.IsType(advanced ? typeof(AdvancedLeakAwareByteBuf) : typeof(SimpleLeakAwareByteBuf), derived);
            Assert.Equal(3, derived.GetByte(0)); derived.Retain(); Assert.Equal(2, root.ReferenceCount);
            Assert.False(facade.Release()); Assert.Equal(0, tracker.Closes); Assert.True(derived.Release()); Assert.Equal(1, tracker.Closes);
            Assert.Equal(0, root.ReferenceCount); Assert.Throws<IllegalReferenceCountException>(() => derived.GetByte(0));
        }
        finally { if (root.ReferenceCount > 0) root.Release(root.ReferenceCount); }
        Assert.Equal(0, native.ReservedBytes);
    }

    private sealed class Tracker(ByteBuf expected) : IResourceLeakTracker<ByteBuf>
    {
        internal int Closes;
        public void Record() { }
        public void Record(object hint) { }
        public bool Close(ByteBuf trackedObject) { Assert.Same(expected, trackedObject); Closes++; return true; }
    }
}
