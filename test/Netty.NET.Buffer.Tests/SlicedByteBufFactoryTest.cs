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
// CLR: provider supplements formerly in RetainedSlicedByteBufTest; the Java-shaped SlicedByteBufTest and
// RetainedSlicedByteBufTest fixtures now run the declared and inherited original bodies.
public class SlicedByteBufFactoryTest
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
            Assert.Equal(1, View.RefCnt);
            Assert.Equal(Root.RefCnt, View.RefCnt);
        }
        public void Dispose()
        {
            if (View.RefCnt > 0) View.Release(View.RefCnt);
            Assert.Equal(0, Root.RefCnt);
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

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestIsContiguous(int provider, bool retained, int offset)
    {
        using var s = new Storage(provider, retained, offset);
        Assert.Equal(s.Root.IsContiguous, s.View.IsContiguous);
        // CLR: the composite provider is the only segmented storage.
        Assert.Equal(provider != 2, s.View.IsContiguous);
    }

    [Fact]
    public void ShouldNotAllowNullInConstructor()
        // CLR: ArgumentNullException replaces NullPointerException; the slice constructor stays internal.
        => Assert.Throws<ArgumentNullException>(() => new ByteBufView(null, 0, 0));

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestReaderIndexAndMarks(int provider, bool retained, int offset)
    {
        // CLR: Storage sets the root to writer 14, reader 2 and marks both before slicing 8 bytes.
        using var s = new Storage(provider, retained, offset);
        ByteBuf slice = s.View;
        Assert.Equal(0, slice.ReaderIndex);
        Assert.Equal(8, slice.WriterIndex);

        slice.ReaderIndex++;
        slice.ResetReaderIndex();
        Assert.Equal(0, slice.ReaderIndex);

        slice.WriterIndex--;
        slice.ResetWriterIndex();
        Assert.Equal(0, slice.WriterIndex);
        // CLR: the root marks stay independent of the slice marks.
        s.Root.ResetReaderIndex().ResetWriterIndex();
        Assert.Equal(2, s.Root.ReaderIndex);
        Assert.Equal(14, s.Root.WriterIndex);
    }

    // CLR: supplemental checks of the shared factory handoff that no original method states directly.
    [Theory]
    [MemberData(nameof(Factories))]
    public void FactoryHandoffKeepsFixedRangeAndSharedStorage(int provider, bool retained, int offset)
    {
        using var s = new Storage(provider, retained, offset);
        Assert.Equal(s.Root.IsDirect, s.View.IsDirect);
        Assert.Equal(8, s.View.Capacity);
        Assert.Equal(8, s.View.MaxCapacity);
        Assert.Throws<NotSupportedException>(() => s.View.Capacity = 10);
        s.View.SetByte(7, 91);
        Assert.Equal(91, s.Root.GetByte(offset + 7));
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void SliceEmptyNotLeak(int provider, bool retained, int offset)
    {
        using var s = new Storage(provider, retained, offset);
        // CLR: the Storage slice, emptied, stands in for Unpooled.buffer(8).
        ByteBuf buffer = s.View.Clear().Retain();
        Assert.Equal(2, buffer.RefCnt);

        ByteBuf slice1 = buffer.Slice();
        // CLR: an empty slice is a real view, not a global empty sentinel.
        Assert.NotSame(Unpooled.EmptyBuffer, slice1);
        Assert.Equal(0, slice1.Capacity);
        Assert.Equal(2, slice1.RefCnt);

        ByteBuf slice2 = slice1.Slice();
        Assert.NotSame(Unpooled.EmptyBuffer, slice2);
        Assert.Equal(0, slice2.Capacity);
        Assert.Equal(2, slice2.RefCnt);

        Assert.False(slice2.Release());
        Assert.Equal(1, buffer.RefCnt);
        Assert.Equal(1, slice1.RefCnt);
        Assert.Equal(1, slice2.RefCnt);

        Assert.True(slice2.Release());

        Assert.Equal(0, buffer.RefCnt);
        Assert.Equal(0, slice1.RefCnt);
        Assert.Equal(0, slice2.RefCnt);
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestGetBytesByteBuffer(int provider, bool retained, int offset)
    {
        using var s = new Storage(provider, retained, offset);
        // Ensure destination buffer is bigger then what is wrapped in the ByteBuf.
        byte[] destination = new byte[9]; Array.Fill(destination, (byte)0xcc);
        ByteBuf wrappedBuffer = s.View;
        // CLR: the array converts to the Span destination that replaces ByteBuffer; ArgumentOutOfRangeException
        // replaces IndexOutOfBoundsException. The root has capacity beyond the slice.
        Assert.Throws<ArgumentOutOfRangeException>(() => wrappedBuffer.GetBytes(wrappedBuffer.ReaderIndex, destination));
        // CLR: the rejected transfer leaves the destination, indices and claim unchanged.
        Assert.Equal(new byte[] { 0xcc, 0xcc, 0xcc, 0xcc, 0xcc, 0xcc, 0xcc, 0xcc, 0xcc }, destination);
        Assert.Equal(0, s.View.ReaderIndex);
        Assert.Equal(8, s.View.WriterIndex);
        Assert.Equal(1, s.View.RefCnt);
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void EnsureWritableWithEnoughSpaceShouldNotThrow(int provider, bool retained, int offset)
    {
        // CLR: Storage leaves the root writer (14) beyond the slice, as unwrapped.writerIndex + 5 does.
        using var s = new Storage(provider, retained, offset);
        ByteBuf slice = s.View;
        slice.WriterIndex = slice.ReaderIndex;

        // Run ensureWritable and verify this doesn't change any indexes.
        int originalWriterIndex = slice.WriterIndex;
        int originalReadableBytes = slice.ReadableBytes;
        Assert.Same(slice, slice.EnsureWritable(originalWriterIndex - slice.WriterIndex));
        Assert.Equal(originalWriterIndex, slice.WriterIndex);
        Assert.Equal(originalReadableBytes, slice.ReadableBytes);
        // CLR: all eight writable slice bytes are available without growing the parent.
        Assert.Same(slice, slice.EnsureWritable(8));
        Assert.Equal(0, slice.ReaderIndex);
        Assert.Equal(0, slice.WriterIndex);
        Assert.Equal(8, slice.Capacity);
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void EnsureWritableWithNotEnoughSpaceShouldThrow(int provider, bool retained, int offset)
    {
        // CLR: Storage leaves the root writer (14) beyond the slice, as unwrapped.writerIndex + 5 does.
        using var s = new Storage(provider, retained, offset);
        ByteBuf slice = s.View;
        // CLR: ArgumentOutOfRangeException replaces IndexOutOfBoundsException.
        Assert.Throws<ArgumentOutOfRangeException>(() => slice.EnsureWritable(1));
        // CLR: the full fixed slice and its parent keep their indices and capacity.
        Assert.Equal(0, slice.ReaderIndex);
        Assert.Equal(8, slice.WriterIndex);
        Assert.Equal(8, slice.Capacity);
        Assert.Equal(2, s.Root.ReaderIndex);
        Assert.Equal(14, s.Root.WriterIndex);
    }

    // Inherited AbstractByteBufTest methods follow; Storage stands in for newBuffer(8) of both original factories.
    [Theory]
    [MemberData(nameof(Factories))]
    public void TestRetainedSliceContents(int provider, bool retained, int offset)
        => TestSliceContents0(provider, retained, offset, true);

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestRetainedSliceReleaseOriginal1(int provider, bool retained, int offset)
        => TestSliceReleaseOriginal(provider, retained, offset, true, true);

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestRetainedSliceReleaseOriginal2(int provider, bool retained, int offset)
        => TestSliceReleaseOriginal(provider, retained, offset, true, false);

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestRetainedSliceReleaseOriginal3(int provider, bool retained, int offset)
        => TestSliceReleaseOriginal(provider, retained, offset, false, true);

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestRetainedSliceReleaseOriginal4(int provider, bool retained, int offset)
        => TestSliceReleaseOriginal(provider, retained, offset, false, false);

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestMultipleRetainedSliceReleaseOriginal1(int provider, bool retained, int offset)
        => TestMultipleRetainedSliceReleaseOriginal(provider, retained, offset, true, true);

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestMultipleRetainedSliceReleaseOriginal2(int provider, bool retained, int offset)
        => TestMultipleRetainedSliceReleaseOriginal(provider, retained, offset, true, false);

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestMultipleRetainedSliceReleaseOriginal3(int provider, bool retained, int offset)
        => TestMultipleRetainedSliceReleaseOriginal(provider, retained, offset, false, true);

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestMultipleRetainedSliceReleaseOriginal4(int provider, bool retained, int offset)
        => TestMultipleRetainedSliceReleaseOriginal(provider, retained, offset, false, false);

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestSliceContents(int provider, bool retained, int offset)
        => TestSliceContents0(provider, retained, offset, false);

    // CLR: xUnit1024 forbids overloading the TestSliceContents test name.
    private static void TestSliceContents0(int provider, bool retained, int offset, bool retainedSlice)
    {
        using var s = new Storage(provider, retained, offset);
        ByteBuf buf = s.View.ResetWriterIndex();
        buf.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        // CLR: a managed array replaces the separately allocated expected buffer.
        byte[] expected = { 4, 5, 6 };
        ByteBuf slice = retainedSlice ? buf.RetainedSlice(buf.ReaderIndex + 3, 3)
                                      : buf.Slice(buf.ReaderIndex + 3, 3);
        try
        {
            Assert.Equal(expected, ByteBufUtil.GetBytes(slice));
            Assert.Equal(0, slice.CompareTo(slice.Duplicate()));
            ByteBuf b = slice.RetainedDuplicate();
            try { Assert.Equal(0, slice.CompareTo(b)); }
            finally { b.Release(); }
            Assert.Equal(0, slice.CompareTo(slice.Slice(0, slice.Capacity)));
            // CLR: mutations are shared while the parent indices stay independent.
            slice.SetByte(1, 42);
            Assert.Equal(42, buf.GetByte(4));
            Assert.Equal(42, s.Root.GetByte(offset + 4));
            slice.ReaderIndex = 1;
            Assert.Equal(0, buf.ReaderIndex);
            Assert.Equal(8, buf.WriterIndex);
        }
        finally
        {
            if (retainedSlice)
            {
                slice.Release();
            }
            // CLR: Storage.Dispose releases buf and checks the final counters.
        }
    }

    private static void TestSliceReleaseOriginal(int provider, bool retained, int offset, bool retainedSlice1, bool retainedSlice2)
    {
        using var s = new Storage(provider, retained, offset);
        ByteBuf buf = s.View.ResetWriterIndex();
        // CLR: managed arrays replace the separately allocated expected buffers.
        byte[] expected1 = { 6, 7, 8 };
        byte[] expected2 = { 7, 8 };
        buf.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        ByteBuf slice1 = retainedSlice1 ? buf.RetainedSlice(buf.ReaderIndex + 5, 3)
                                        : buf.Slice(buf.ReaderIndex + 5, 3).Retain();
        Assert.Equal(expected1, ByteBufUtil.GetBytes(slice1));
        // Simulate a handler that releases the original buffer, and propagates a slice.
        buf.Release();

        ByteBuf slice2 = retainedSlice2 ? slice1.RetainedSlice(slice1.ReaderIndex + 1, 2)
                                        : slice1.Slice(slice1.ReaderIndex + 1, 2).Retain();
        Assert.Equal(expected2, ByteBufUtil.GetBytes(slice2));

        // Cleanup the expected buffers used for testing.
        // CLR expectations are managed byte arrays without reference-counted claims.

        // The handler created a slice of the slice and is now done with it.
        slice2.Release();

        // The handler is now done with the original slice
        Assert.True(slice1.Release());

        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, buf.RefCnt);
        Assert.Equal(0, slice1.RefCnt);
        Assert.Equal(0, slice2.RefCnt);
        // CLR: access after the final release is rejected.
        Assert.Throws<IllegalReferenceCountException>(() => slice2.GetByte(0));
    }

    private static void TestMultipleRetainedSliceReleaseOriginal(int provider, bool retained, int offset, bool retainedSlice1, bool retainedSlice2)
    {
        using var s = new Storage(provider, retained, offset);
        ByteBuf buf = s.View.ResetWriterIndex();
        // CLR: managed arrays replace the separately allocated expected buffers.
        byte[] expected1 = { 6, 7, 8 };
        byte[] expected2 = { 7, 8 };
        byte[] expected3 = { 6, 7 };
        buf.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        ByteBuf slice1 = retainedSlice1 ? buf.RetainedSlice(buf.ReaderIndex + 5, 3)
                                        : buf.Slice(buf.ReaderIndex + 5, 3).Retain();
        Assert.Equal(expected1, ByteBufUtil.GetBytes(slice1));
        // Simulate a handler that releases the original buffer, and propagates a slice.
        buf.Release();

        ByteBuf slice2 = retainedSlice2 ? slice1.RetainedSlice(slice1.ReaderIndex + 1, 2)
                                        : slice1.Slice(slice1.ReaderIndex + 1, 2).Retain();
        Assert.Equal(expected2, ByteBufUtil.GetBytes(slice2));

        // The handler created a slice of the slice and is now done with it.
        slice2.Release();

        ByteBuf slice3 = slice1.RetainedSlice(slice1.ReaderIndex, 2);
        Assert.Equal(expected3, ByteBufUtil.GetBytes(slice3));

        // The handler created another slice of the slice and is now done with it.
        slice3.Release();

        // The handler is now done with the original slice
        Assert.True(slice1.Release());

        // Cleanup the expected buffers used for testing.
        // CLR expectations are managed byte arrays without reference-counted claims.

        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, buf.RefCnt);
        Assert.Equal(0, slice1.RefCnt);
        Assert.Equal(0, slice2.RefCnt);
        Assert.Equal(0, slice3.RefCnt);
    }
}
