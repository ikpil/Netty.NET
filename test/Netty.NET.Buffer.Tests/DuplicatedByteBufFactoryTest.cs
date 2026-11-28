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
// CLR: provider supplements formerly in DuplicatedByteBufTest; the Java-shaped DuplicatedByteBufTest and
// RetainedDuplicatedByteBufTest fixtures now run the declared and inherited original bodies.
public class DuplicatedByteBufFactoryTest
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

            if (retained)
            {
                Root.Release();
            }

            Assert.Equal(Root.ReaderIndex, Buffer.ReaderIndex);
            Assert.Equal(Root.WriterIndex, Buffer.WriterIndex);
            Assert.Equal(1, Buffer.RefCnt);
        }

        public void Dispose()
        {
            if (Buffer.RefCnt > 0)
            {
                Buffer.Release(Buffer.RefCnt);
            }

            Assert.Equal(0, Root.RefCnt);
            _pool?.Dispose();
            Assert.Equal(0, _native.ReservedBytes);
        }
    }

    public static IEnumerable<object[]> Factories()
    {
        for (int provider = 0; provider < 5; ++provider)
        foreach (bool retained in new[] { false, true })
            {
                yield return [provider, retained];
            }
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestIsContiguous(int provider, bool retained)
    {
        using var s = new Storage(provider, retained);
        Assert.Equal(s.Root.IsContiguous, s.Buffer.IsContiguous);
        // CLR: the composite provider is the only segmented storage; the facade also keeps the
        // parent's storage kind and maximum capacity.
        Assert.Equal(provider != 2, s.Buffer.IsContiguous);
        Assert.Equal(s.Root.IsDirect, s.Buffer.IsDirect);
        Assert.Equal(s.Root.MaxCapacity, s.Buffer.MaxCapacity);
    }

    [Fact]
    public void ShouldNotAllowNullInConstructor()
        // CLR: ArgumentNullException replaces NullPointerException.
    {
        Assert.Throws<ArgumentNullException>(() => new ByteBufView(null));
    }

    // See https://github.com/netty/netty/issues/1800
    [Theory]
    [MemberData(nameof(Factories))]
    public void TestIncreaseCapacityWrapped(int provider, bool retained)
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
    public void TestMarksInitialized(int provider, bool retained)
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

    // Inherited AbstractByteBufTest methods follow; Storage stands in for newBuffer(8) of both original factories.
    [Theory]
    [MemberData(nameof(Factories))]
    public void TestRetainedDuplicateReleaseOriginal1(int provider, bool factoryRetained)
    {
        TestDuplicateReleaseOriginal(provider, factoryRetained, true, true);
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestRetainedDuplicateReleaseOriginal2(int provider, bool factoryRetained)
    {
        TestDuplicateReleaseOriginal(provider, factoryRetained, true, false);
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestRetainedDuplicateReleaseOriginal3(int provider, bool factoryRetained)
    {
        TestDuplicateReleaseOriginal(provider, factoryRetained, false, true);
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestRetainedDuplicateReleaseOriginal4(int provider, bool factoryRetained)
    {
        TestDuplicateReleaseOriginal(provider, factoryRetained, false, false);
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestMultipleRetainedDuplicateReleaseOriginal1(int provider, bool factoryRetained)
    {
        TestMultipleRetainedDuplicateReleaseOriginal(provider, factoryRetained, true, true);
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestMultipleRetainedDuplicateReleaseOriginal2(int provider, bool factoryRetained)
    {
        TestMultipleRetainedDuplicateReleaseOriginal(provider, factoryRetained, true, false);
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestMultipleRetainedDuplicateReleaseOriginal3(int provider, bool factoryRetained)
    {
        TestMultipleRetainedDuplicateReleaseOriginal(provider, factoryRetained, false, true);
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestMultipleRetainedDuplicateReleaseOriginal4(int provider, bool factoryRetained)
    {
        TestMultipleRetainedDuplicateReleaseOriginal(provider, factoryRetained, false, false);
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestRetainedDuplicateContents(int provider, bool factoryRetained)
    {
        TestDuplicateContents0(provider, factoryRetained, true);
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestDuplicateContents(int provider, bool factoryRetained)
    {
        TestDuplicateContents0(provider, factoryRetained, false);
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestDuplicateCapacityChange(int provider, bool factoryRetained)
    {
        TestDuplicateCapacityChange0(provider, factoryRetained, false);
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestRetainedDuplicateCapacityChange(int provider, bool factoryRetained)
    {
        TestDuplicateCapacityChange0(provider, factoryRetained, true);
    }

    // CLR: xUnit1024 forbids overloading the TestDuplicateCapacityChange test name.
    private static void TestDuplicateCapacityChange0(int provider, bool factoryRetained, bool retainedDuplicate)
    {
        using var s = new Storage(provider, factoryRetained);
        ByteBuf buf = s.Buffer;
        ByteBuf dup = retainedDuplicate ? buf.RetainedDuplicate() : buf.Duplicate();

        try
        {
            dup.Capacity = 10;
            // CLR: the shared capacity and the unchanged indices are checked explicitly as well.
            Assert.Equal(10, buf.Capacity);
            Assert.Equal(buf.Capacity, dup.Capacity);
            dup.Capacity = 5;
            Assert.Equal(5, buf.Capacity);
            Assert.Equal(buf.Capacity, dup.Capacity);
            Assert.Equal(0, buf.ReaderIndex);
            Assert.Equal(0, buf.WriterIndex);
        }
        finally
        {
            if (retainedDuplicate)
            {
                dup.Release();
            }
            // CLR: Storage.Dispose releases buf and checks the final counters.
        }
    }

    private static void TestDuplicateReleaseOriginal(int provider, bool factoryRetained, bool retainedDuplicate1, bool retainedDuplicate2)
    {
        using var s = new Storage(provider, factoryRetained);
        ByteBuf buf = s.Buffer;
        buf.ResetWriterIndex().WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        // CLR: a managed array replaces the separately allocated expected buffer.
        byte[] expected = { 1, 2, 3, 4, 5, 6, 7, 8 };
        ByteBuf dup1 = retainedDuplicate1 ? buf.RetainedDuplicate()
                                          : buf.Duplicate().Retain();
        Assert.Equal(expected, ByteBufUtil.GetBytes(dup1));
        // Simulate a handler that releases the original buffer, and propagates a slice.
        Assert.False(buf.Release());

        ByteBuf dup2 = retainedDuplicate2 ? dup1.RetainedDuplicate()
                                          : dup1.Duplicate().Retain();
        Assert.Equal(expected, ByteBufUtil.GetBytes(dup2));
        // CLR: both duplicates share their bytes.
        dup2.SetByte(7, 42);
        Assert.Equal(42, dup1.GetByte(7));

        // Cleanup the expected buffers used for testing.
        // CLR expected bytes are managed arrays; no reference-counted expected buffer is allocated.

        // The handler created a slice of the slice and is now done with it.
        dup2.Release();

        // The handler is now done with the original slice
        Assert.True(dup1.Release());

        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, buf.RefCnt);
        Assert.Equal(0, dup1.RefCnt);
        Assert.Equal(0, dup2.RefCnt);
        // CLR: access after the final release is rejected.
        Assert.Throws<IllegalReferenceCountException>(() => dup2.GetByte(0));
    }

    private static void TestMultipleRetainedDuplicateReleaseOriginal(int provider, bool factoryRetained, bool retainedDuplicate1, bool retainedDuplicate2)
    {
        using var s = new Storage(provider, factoryRetained);
        ByteBuf buf = s.Buffer;
        buf.ResetWriterIndex().WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        // CLR: a managed array replaces the separately allocated expected buffer.
        byte[] expected = { 1, 2, 3, 4, 5, 6, 7, 8 };
        ByteBuf dup1 = retainedDuplicate1 ? buf.RetainedDuplicate()
                                          : buf.Duplicate().Retain();
        Assert.Equal(expected, ByteBufUtil.GetBytes(dup1));
        // Simulate a handler that releases the original buffer, and propagates a slice.
        buf.Release();

        ByteBuf dup2 = retainedDuplicate2 ? dup1.RetainedDuplicate()
                                          : dup1.Duplicate().Retain();
        Assert.Equal(expected, ByteBufUtil.GetBytes(dup2));
        Assert.Equal(0, dup2.CompareTo(dup2.Duplicate()));
        Assert.Equal(0, dup2.CompareTo(dup2.Slice()));

        ByteBuf tmpBuf = dup2.RetainedDuplicate();
        Assert.Equal(0, dup2.CompareTo(tmpBuf));
        tmpBuf.Release();
        tmpBuf = dup2.RetainedSlice();
        Assert.Equal(0, dup2.CompareTo(tmpBuf));
        tmpBuf.Release();

        // The handler created a slice of the slice and is now done with it.
        dup2.Release();

        ByteBuf dup3 = dup1.RetainedDuplicate();
        Assert.Equal(expected, ByteBufUtil.GetBytes(dup3));

        // The handler created another slice of the slice and is now done with it.
        dup3.Release();

        // The handler is now done with the original slice
        Assert.True(dup1.Release());

        // Cleanup the expected buffers used for testing.
        // CLR expected bytes are managed arrays; no reference-counted expected buffer is allocated.

        // Reference counting may be shared, or may be independently tracked, but at this point all buffers should
        // be deallocated and have a reference count of 0.
        Assert.Equal(0, buf.RefCnt);
        Assert.Equal(0, dup1.RefCnt);
        Assert.Equal(0, dup2.RefCnt);
        Assert.Equal(0, dup3.RefCnt);
    }

    // CLR: xUnit1024 forbids overloading the TestDuplicateContents test name.
    private static void TestDuplicateContents0(int provider, bool factoryRetained, bool retainedDuplicate)
    {
        using var s = new Storage(provider, factoryRetained);
        ByteBuf buf = s.Buffer;
        buf.ResetWriterIndex().WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        ByteBuf dup = retainedDuplicate ? buf.RetainedDuplicate() : buf.Duplicate();

        try
        {
            Assert.Equal(0, dup.CompareTo(buf));
            Assert.Equal(0, dup.CompareTo(dup.Duplicate()));
            ByteBuf b = dup.RetainedDuplicate();

            try
            {
                Assert.Equal(0, dup.CompareTo(b));
            }
            finally
            {
                b.Release();
            }

            Assert.Equal(0, dup.CompareTo(dup.Slice(dup.ReaderIndex, dup.ReadableBytes)));
            // CLR: mutations are shared while the reader indices stay independent.
            dup.SetByte(3, 99);
            Assert.Equal(99, buf.GetByte(3));
            buf.SetByte(4, 88);
            Assert.Equal(88, dup.GetByte(4));
            dup.ReaderIndex = 2;
            Assert.Equal(0, buf.ReaderIndex);
        }
        finally
        {
            if (retainedDuplicate)
            {
                dup.Release();
            }
            // CLR: Storage.Dispose releases buf and checks the final counters.
        }
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void TestDuplicateRelease(int provider, bool retained)
    {
        using var s = new Storage(provider, retained);
        ByteBuf buf = s.Buffer;
        Assert.Equal(1, buf.RefCnt);
        Assert.True(buf.Duplicate().Release());
        Assert.Equal(0, buf.RefCnt);
        // CLR: the factory root shares the released claim.
        Assert.Equal(0, s.Root.RefCnt);
    }
}
