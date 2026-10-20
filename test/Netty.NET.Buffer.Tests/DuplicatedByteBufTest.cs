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

/*
 * Copyright 2016 The Netty Project
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
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

/**
 * Tests duplicated channel buffers
 */
// Both original factories share these assertions. The retained factory releases
// its original claim before returning, including when pooled counters are independent.
public class DuplicatedByteBufTest
{
    private sealed class Storage : IDisposable
    {
        private readonly NativeMemoryAllocator _native = new();
        private readonly PooledByteBufAllocator _pool;
        public readonly ByteBuf Root, Buffer;
        public Storage(int provider, bool retained)
        {
            if (provider >= 3)
                _pool = new PooledByteBufAllocator(nativeAllocator: _native, maxOrder: 1,
                    smallCacheSize: 0, normalCacheSize: 0);
            Root = provider switch
            {
                0 => new UnpooledHeapByteBuf(8, 64),
                1 => new UnpooledDirectByteBuf(8, 64, _native),
                2 => new CompositeByteBuf(16, false, _native)
                    .AddComponent(new UnpooledHeapByteBuf(4, 4).SetIndex(0, 4), true)
                    .AddComponent(new UnpooledDirectByteBuf(4, 4, _native).SetIndex(0, 4), true),
                3 => new PooledHeapByteBuf((PoolHeapArena)_pool.HeapArenas[0], 8, 64),
                _ => new PooledDirectByteBuf((PoolDirectArena)_pool.DirectArenas[0], 8, 64)
            };
            Root.Clear();
            Buffer = retained ? Root.RetainedDuplicate() : Root.Duplicate();
            if (retained) Root.Release();
            Assert.Equal(Root.ReaderIndex, Buffer.ReaderIndex);
            Assert.Equal(Root.WriterIndex, Buffer.WriterIndex);
            Assert.Equal(1, Buffer.ReferenceCount);
        }
        public void Dispose()
        {
            if (Buffer.ReferenceCount > 0) Buffer.Release(Buffer.ReferenceCount);
            Assert.Equal(0, Root.ReferenceCount);
            _pool?.Dispose();
            Assert.Equal(0, _native.ReservedBytes);
        }
    }

    public static IEnumerable<object[]> Factories()
    {
        for (int provider = 0; provider < 5; ++provider)
        foreach (bool retained in new[] { false, true }) yield return [provider, retained];
    }
    public static IEnumerable<object[]> DuplicateKinds()
    {
        foreach (object[] factory in Factories())
        foreach (bool retained in new[] { false, true }) yield return [factory[0], factory[1], retained];
    }
    public static IEnumerable<object[]> NestedKinds()
    {
        foreach (object[] factory in Factories())
        foreach (bool first in new[] { false, true })
        foreach (bool second in new[] { false, true }) yield return [factory[0], factory[1], first, second];
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void ContiguityMatchesTheWrappedStorage(int provider, bool retained)
    {
        using var s = new Storage(provider, retained);
        Assert.Equal(s.Root.IsContiguous, s.Buffer.IsContiguous);
        Assert.Equal(provider != 2, s.Buffer.IsContiguous);
        Assert.Equal(s.Root.IsDirect, s.Buffer.IsDirect);
        Assert.Equal(s.Root.MaxCapacity, s.Buffer.MaxCapacity);
    }

    [Fact]
    public void ConstructorRejectsNullParent()
        => Assert.Throws<ArgumentNullException>(() => new ByteBufView(null));

    // See https://github.com/netty/netty/issues/1800
    [Theory]
    [MemberData(nameof(Factories))]
    public void IncreasingWrappedCapacityPreservesIndependentDuplicateReader(int provider, bool retained)
    {
        using var s = new Storage(provider, retained);
        s.Root.WriteByte(0);
        s.Root.ReaderIndex++;
        s.Buffer.WriterIndex++;
        s.Root.Capacity *= 2;
        Assert.Equal(16, s.Buffer.Capacity);
        Assert.Equal(0, s.Buffer.ReadByte());
        Assert.Equal(1, s.Root.ReaderIndex);
        Assert.Equal(1, s.Root.WriterIndex);
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void MarksStartAtParentIndicesAndRemainIndependent(int provider, bool retained)
    {
        using var s = new Storage(provider, retained);
        s.Buffer.SetIndex(1, 6);
        ByteBuf duplicate = new ByteBufView(s.Buffer);
        // Test writer mark
        duplicate.WriterIndex++;
        duplicate.ResetWriterIndex();
        Assert.Equal(s.Buffer.WriterIndex, duplicate.WriterIndex);
        // Test reader mark
        duplicate.ReaderIndex++;
        duplicate.ResetReaderIndex();
        Assert.Equal(s.Buffer.ReaderIndex, duplicate.ReaderIndex);
        s.Buffer.SetIndex(2, 7).MarkReaderIndex().MarkWriterIndex();
        duplicate.SetIndex(3, 8).ResetWriterIndex().ResetReaderIndex();
        Assert.Equal(1, duplicate.ReaderIndex);
        Assert.Equal(6, duplicate.WriterIndex);
        s.Buffer.ResetReaderIndex().ResetWriterIndex();
        Assert.Equal(2, s.Buffer.ReaderIndex);
        Assert.Equal(7, s.Buffer.WriterIndex);
    }


    [Theory]
    [MemberData(nameof(Factories))]
    public void FactoryDuplicateKeepsTheOriginalPooledMarkRule(int provider, bool retained)
    {
        using var s = new Storage(provider, retained);
        s.Buffer.SetIndex(1, 6);
        ByteBuf duplicate = s.Buffer.Duplicate();
        Assert.Equal(1, duplicate.ReaderIndex);
        Assert.Equal(6, duplicate.WriterIndex);
        // PooledDuplicatedByteBuf.duplicate0 constructs from unwrap(), then
        // setIndex restores the owner's current indices without replacing marks.
        duplicate.SetIndex(0, 8).ResetWriterIndex().ResetReaderIndex();
        bool rootMarks = provider >= 3 && retained;
        Assert.Equal(rootMarks ? 0 : 1, duplicate.ReaderIndex);
        Assert.Equal(rootMarks ? 0 : 6, duplicate.WriterIndex);
        Assert.Equal(1, s.Buffer.ReaderIndex);
        Assert.Equal(6, s.Buffer.WriterIndex);
    }

    [Theory]
    [MemberData(nameof(DuplicateKinds))]
    public void CapacityChangesThroughDuplicateAreShared(int provider, bool factoryRetained, bool retained)
    {
        using var s = new Storage(provider, factoryRetained);
        ByteBuf duplicate = retained ? s.Buffer.RetainedDuplicate() : s.Buffer.Duplicate();
        try
        {
            duplicate.Capacity = 10;
            Assert.Equal(10, s.Buffer.Capacity);
            Assert.Equal(s.Buffer.Capacity, duplicate.Capacity);
            duplicate.Capacity = 5;
            Assert.Equal(5, s.Buffer.Capacity);
            Assert.Equal(s.Buffer.Capacity, duplicate.Capacity);
            Assert.Equal(0, s.Buffer.ReaderIndex);
            Assert.Equal(0, s.Buffer.WriterIndex);
        }
        finally { if (retained) duplicate.Release(); }
    }

    [Theory]
    [MemberData(nameof(DuplicateKinds))]
    public void NestedDuplicateContentsShareStorageAndKeepIndependentIndices(int provider, bool factoryRetained, bool retained)
    {
        using var s = new Storage(provider, factoryRetained);
        s.Buffer.ResetWriterIndex().WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        ByteBuf duplicate = retained ? s.Buffer.RetainedDuplicate() : s.Buffer.Duplicate();
        try
        {
            Assert.Equal(0, duplicate.CompareTo(s.Buffer));
            Assert.Equal(0, duplicate.CompareTo(duplicate.Duplicate()));
            ByteBuf owned = duplicate.RetainedDuplicate();
            try { Assert.Equal(0, duplicate.CompareTo(owned)); }
            finally { owned.Release(); }
            Assert.Equal(0, duplicate.CompareTo(duplicate.Slice(duplicate.ReaderIndex, duplicate.ReadableBytes)));
            duplicate.SetByte(3, 99);
            Assert.Equal(99, s.Buffer.GetByte(3));
            s.Buffer.SetByte(4, 88);
            Assert.Equal(88, duplicate.GetByte(4));
            duplicate.ReaderIndex = 2;
            Assert.Equal(0, s.Buffer.ReaderIndex);
        }
        finally { if (retained) duplicate.Release(); }
    }

    [Theory]
    [MemberData(nameof(NestedKinds))]
    public void RetainedDuplicatesSurviveOriginalRelease(int provider, bool factoryRetained, bool firstRetained, bool secondRetained)
    {
        using var s = new Storage(provider, factoryRetained);
        ByteBuf original = s.Buffer;
        original.ResetWriterIndex().WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        ByteBuf first = firstRetained ? original.RetainedDuplicate() : original.Duplicate().Retain();
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, ByteBufUtil.GetBytes(first));
        // Simulate a handler that releases the original buffer, and propagates a slice.
        Assert.False(original.Release());
        ByteBuf second = secondRetained ? first.RetainedDuplicate() : first.Duplicate().Retain();
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, ByteBufUtil.GetBytes(second));
        second.SetByte(7, 42);
        Assert.Equal(42, first.GetByte(7));
        // Cleanup the expected buffers used for testing.
        // CLR expected bytes are managed arrays; no reference-counted expected buffer is allocated.
        // The handler created a slice of the slice and is now done with it.
        second.Release();
        // The handler is now done with the original slice
        Assert.True(first.Release());
        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, original.ReferenceCount);
        Assert.Equal(0, first.ReferenceCount);
        Assert.Equal(0, second.ReferenceCount);
        Assert.Throws<IllegalReferenceCountException>(() => second.GetByte(0));
    }

    [Theory]
    [MemberData(nameof(NestedKinds))]
    public void MultipleRetainedDuplicatesReleaseEveryClaim(int provider, bool factoryRetained, bool firstRetained, bool secondRetained)
    {
        using var s = new Storage(provider, factoryRetained);
        ByteBuf original = s.Buffer;
        original.ResetWriterIndex().WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        ByteBuf first = firstRetained ? original.RetainedDuplicate() : original.Duplicate().Retain();
        // Simulate a handler that releases the original buffer, and propagates a slice.
        original.Release();
        ByteBuf second = secondRetained ? first.RetainedDuplicate() : first.Duplicate().Retain();
        Assert.Equal(0, second.CompareTo(second.Duplicate()));
        Assert.Equal(0, second.CompareTo(second.Slice()));
        ByteBuf temporary = second.RetainedDuplicate();
        Assert.Equal(0, second.CompareTo(temporary));
        temporary.Release();
        temporary = second.RetainedSlice();
        Assert.Equal(0, second.CompareTo(temporary));
        temporary.Release();
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, ByteBufUtil.GetBytes(second));
        // The handler created a slice of the slice and is now done with it.
        second.Release();
        ByteBuf third = first.RetainedDuplicate();
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, ByteBufUtil.GetBytes(third));
        // The handler created another slice of the slice and is now done with it.
        third.Release();
        // The handler is now done with the original slice
        Assert.True(first.Release());
        // Cleanup the expected buffers used for testing.
        // CLR expected bytes are managed arrays; no reference-counted expected buffer is allocated.
        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, original.ReferenceCount);
        Assert.Equal(0, first.ReferenceCount);
        Assert.Equal(0, second.ReferenceCount);
        Assert.Equal(0, third.ReferenceCount);
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void BorrowedDuplicateReleaseConsumesTheOriginalClaim(int provider, bool retained)
    {
        using var s = new Storage(provider, retained);
        Assert.True(s.Buffer.Duplicate().Release());
        Assert.Equal(0, s.Buffer.ReferenceCount);
        Assert.Equal(0, s.Root.ReferenceCount);
    }
}
