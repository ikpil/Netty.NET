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

// The retained factory transfers its original claim, as RetainedSlicedByteBufTest.newSlice does.
// Both factories share the original slice assertions, with deterministic offsets.
public class RetainedSlicedByteBufTest
{
    private sealed class Storage : IDisposable
    {
        private readonly NativeMemoryAllocator _native = new();
        private readonly PooledByteBufAllocator _pool;
        public readonly ByteBuf Root, View;
        public Storage(int provider, bool retained, int offset)
        {
            if (provider >= 3)
                _pool = new PooledByteBufAllocator(nativeAllocator: _native, maxOrder: 1,
                    smallCacheSize: 0, normalCacheSize: 0);
            Root = provider switch
            {
                0 => new UnpooledHeapByteBuf(16, 64),
                1 => new UnpooledDirectByteBuf(16, 64, _native),
                2 => new CompositeByteBuf(16, false, _native)
                    .AddComponent(new UnpooledHeapByteBuf(8, 8).SetIndex(0, 8), true)
                    .AddComponent(new UnpooledDirectByteBuf(8, 8, _native).SetIndex(0, 8), true),
                3 => new PooledHeapByteBuf((PoolHeapArena)_pool.HeapArenas[0], 16, 64),
                _ => new PooledDirectByteBuf((PoolDirectArena)_pool.DirectArenas[0], 16, 64)
            };
            Root.SetBytes(0, new byte[16]).SetIndex(2, 14).MarkReaderIndex().MarkWriterIndex();
            View = retained ? Root.RetainedSlice(offset, 8) : Root.Slice(offset, 8);
            if (retained) Assert.False(Root.Release());
            Assert.Equal(1, View.ReferenceCount);
            Assert.Equal(Root.ReferenceCount, View.ReferenceCount);
        }
        public void Dispose()
        {
            if (View.ReferenceCount > 0) View.Release(View.ReferenceCount);
            Assert.Equal(0, Root.ReferenceCount);
            _pool?.Dispose();
            Assert.Equal(0, _native.ReservedBytes);
        }
    }

    public static IEnumerable<object[]> Factories()
    {
        for (int provider = 0; provider < 5; ++provider)
        foreach (bool retained in new[] { false, true })
        foreach (int offset in new[] { 0, 3 }) yield return [provider, retained, offset];
    }
    public static IEnumerable<object[]> ChildKinds()
    {
        foreach (object[] factory in Factories())
        foreach (bool childRetained in new[] { false, true })
            yield return [factory[0], factory[1], factory[2], childRetained];
    }
    public static IEnumerable<object[]> NestedKinds()
    {
        foreach (object[] factory in Factories())
        foreach (bool firstRetained in new[] { false, true })
        foreach (bool secondRetained in new[] { false, true })
            yield return [factory[0], factory[1], factory[2], firstRetained, secondRetained];
    }

    [Fact]
    public void ConstructorRejectsNullParent()
        => Assert.Throws<ArgumentNullException>(() => new ByteBufView(null, 0, 0));

    [Theory]
    [MemberData(nameof(Factories))]
    public void FactoryHandoffKeepsFixedRangeAndFreshMarks(int provider, bool retained, int offset)
    {
        using var s = new Storage(provider, retained, offset);
        Assert.Equal(s.Root.IsContiguous, s.View.IsContiguous);
        Assert.Equal(provider != 2, s.View.IsContiguous);
        Assert.Equal(s.Root.IsDirect, s.View.IsDirect);
        Assert.Equal(8, s.View.Capacity);
        Assert.Equal(8, s.View.MaxCapacity);
        Assert.Equal(0, s.View.ReaderIndex);
        Assert.Equal(8, s.View.WriterIndex);
        s.View.ReaderIndex++;
        s.View.ResetReaderIndex();
        Assert.Equal(0, s.View.ReaderIndex);
        s.View.WriterIndex--;
        s.View.ResetWriterIndex();
        Assert.Equal(0, s.View.WriterIndex);
        s.Root.ResetReaderIndex().ResetWriterIndex();
        Assert.Equal(2, s.Root.ReaderIndex);
        Assert.Equal(14, s.Root.WriterIndex);
        Assert.Throws<NotSupportedException>(() => s.View.Capacity = 10);
        s.View.SetByte(7, 91);
        Assert.Equal(91, s.Root.GetByte(offset + 7));
    }

    [Theory]
    [MemberData(nameof(ChildKinds))]
    public void ChildSliceContentsMatchTheOriginalWindow(int provider, bool retained, int offset, bool childRetained)
    {
        using var s = new Storage(provider, retained, offset);
        s.View.ResetWriterIndex().WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        ByteBuf child = childRetained ? s.View.RetainedSlice(3, 3) : s.View.Slice(3, 3);
        try
        {
            Assert.Equal(new byte[] { 4, 5, 6 }, ByteBufUtil.GetBytes(child));
            Assert.Equal(0, child.CompareTo(child.Duplicate()));
            ByteBuf owned = child.RetainedDuplicate();
            try { Assert.Equal(0, child.CompareTo(owned)); }
            finally { owned.Release(); }
            Assert.Equal(0, child.CompareTo(child.Slice(0, child.Capacity)));
            child.SetByte(1, 42);
            Assert.Equal(42, s.View.GetByte(4));
            Assert.Equal(42, s.Root.GetByte(offset + 4));
            child.ReaderIndex = 1;
            Assert.Equal(0, s.View.ReaderIndex);
            Assert.Equal(8, s.View.WriterIndex);
        }
        finally { if (childRetained) child.Release(); }
    }

    [Theory]
    [MemberData(nameof(NestedKinds))]
    public void RetainedSlicesSurviveOriginalRelease(int provider, bool retained, int offset, bool firstRetained, bool secondRetained)
    {
        using var s = new Storage(provider, retained, offset);
        s.View.ResetWriterIndex().WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        ByteBuf first = firstRetained ? s.View.RetainedSlice(5, 3) : s.View.Slice(5, 3).Retain();
        Assert.Equal(new byte[] { 6, 7, 8 }, ByteBufUtil.GetBytes(first));
        // Simulate a handler that releases the original buffer, and propagates a slice.
        s.View.Release();
        ByteBuf second = secondRetained ? first.RetainedSlice(1, 2) : first.Slice(1, 2).Retain();
        Assert.Equal(new byte[] { 7, 8 }, ByteBufUtil.GetBytes(second));
        // Cleanup the expected buffers used for testing.
        // CLR expectations are managed byte arrays without reference-counted claims.
        // The handler created a slice of the slice and is now done with it.
        second.Release();
        // The handler is now done with the original slice
        Assert.True(first.Release());
        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, s.View.ReferenceCount);
        Assert.Equal(0, first.ReferenceCount);
        Assert.Equal(0, second.ReferenceCount);
        Assert.Throws<IllegalReferenceCountException>(() => second.GetByte(0));
    }

    [Theory]
    [MemberData(nameof(NestedKinds))]
    public void RepeatedRetainedSlicesReleaseEveryClaim(int provider, bool retained, int offset, bool firstRetained, bool secondRetained)
    {
        using var s = new Storage(provider, retained, offset);
        s.View.ResetWriterIndex().WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        ByteBuf first = firstRetained ? s.View.RetainedSlice(5, 3) : s.View.Slice(5, 3).Retain();
        Assert.Equal(new byte[] { 6, 7, 8 }, ByteBufUtil.GetBytes(first));
        // Simulate a handler that releases the original buffer, and propagates a slice.
        s.View.Release();
        ByteBuf second = secondRetained ? first.RetainedSlice(1, 2) : first.Slice(1, 2).Retain();
        Assert.Equal(new byte[] { 7, 8 }, ByteBufUtil.GetBytes(second));
        // The handler created a slice of the slice and is now done with it.
        second.Release();
        ByteBuf third = first.RetainedSlice(0, 2);
        Assert.Equal(new byte[] { 6, 7 }, ByteBufUtil.GetBytes(third));
        // The handler created another slice of the slice and is now done with it.
        third.Release();
        // The handler is now done with the original slice
        Assert.True(first.Release());
        // Cleanup the expected buffers used for testing.
        // CLR expectations are managed byte arrays without reference-counted claims.
        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, s.View.ReferenceCount);
        Assert.Equal(0, first.ReferenceCount);
        Assert.Equal(0, second.ReferenceCount);
        Assert.Equal(0, third.ReferenceCount);
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void EmptyBorrowedSlicesShareTheOwningCounter(int provider, bool retained, int offset)
    {
        using var s = new Storage(provider, retained, offset);
        s.View.Clear().Retain();
        Assert.Equal(2, s.View.ReferenceCount);
        ByteBuf first = s.View.Slice(), second = first.Slice();
        Assert.NotSame(Unpooled.EmptyBuffer, first);
        Assert.NotSame(Unpooled.EmptyBuffer, second);
        Assert.Equal(0, first.Capacity);
        Assert.Equal(0, second.Capacity);
        Assert.Equal(2, first.ReferenceCount);
        Assert.Equal(2, second.ReferenceCount);
        Assert.False(second.Release());
        Assert.Equal(1, s.View.ReferenceCount);
        Assert.Equal(1, first.ReferenceCount);
        Assert.Equal(1, second.ReferenceCount);
        Assert.True(second.Release());
        Assert.Equal(0, s.View.ReferenceCount);
        Assert.Equal(0, first.ReferenceCount);
        Assert.Equal(0, second.ReferenceCount);
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void OversizedDestinationCannotReadBeyondSlice(int provider, bool retained, int offset)
    {
        using var s = new Storage(provider, retained, offset);
        // Ensure destination buffer is bigger then what is wrapped in the ByteBuf.
        byte[] destination = new byte[9]; Array.Fill(destination, (byte)0xcc);
        Assert.Throws<ArgumentOutOfRangeException>(() => s.View.GetBytes(0, destination));
        Assert.Equal(new byte[] { 0xcc, 0xcc, 0xcc, 0xcc, 0xcc, 0xcc, 0xcc, 0xcc, 0xcc }, destination);
        Assert.Equal(0, s.View.ReaderIndex);
        Assert.Equal(8, s.View.WriterIndex);
        Assert.Equal(1, s.View.ReferenceCount);
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void EnsureWritableUsesTheSliceSpaceWithoutChangingIndices(int provider, bool retained, int offset)
    {
        using var s = new Storage(provider, retained, offset);
        s.View.WriterIndex = 0;
        // Run ensureWritable and verify this doesn't change any indexes.
        Assert.Same(s.View, s.View.EnsureWritable(0));
        Assert.Same(s.View, s.View.EnsureWritable(8));
        Assert.Equal(0, s.View.ReaderIndex);
        Assert.Equal(0, s.View.WriterIndex);
        Assert.Equal(0, s.View.ReadableBytes);
        s.View.WriterIndex = 8;
        Assert.Throws<ArgumentOutOfRangeException>(() => s.View.EnsureWritable(1));
        Assert.Equal(0, s.View.ReaderIndex);
        Assert.Equal(8, s.View.WriterIndex);
        Assert.Equal(8, s.View.Capacity);
        Assert.Equal(2, s.Root.ReaderIndex);
        Assert.Equal(14, s.Root.WriterIndex);
    }
}
