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
using System.Runtime.InteropServices;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class CompositeByteBufConsolidationTest
{
    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native = new();
        public readonly IByteBufAllocator Allocator;

        public Storage(bool pooled, bool direct)
        {
            Allocator = pooled
                ? new PooledByteBufAllocator(direct, pageSize: 4096, maxOrder: 1, nativeAllocator: Native)
                : new UnpooledByteBufAllocator(direct, Native);
        }

        public void Dispose()
        {
            (Allocator as IDisposable)?.Dispose();
            Assert.Equal(0, Native.ReservedBytes);
        }
    }

    public static IEnumerable<object[]> Providers()
    {
        foreach (bool pooled in new[] { false, true })
        foreach (bool direct in new[] { false, true }) yield return [pooled, direct];
    }

    public static IEnumerable<object[]> Copies()
    {
        foreach (object[] provider in Providers())
        foreach (bool indexed in new[] { false, true }) yield return [provider[0], provider[1], indexed];
    }

    // CLR: allocator-owned sources over heap/native pooled/unpooled factories replace wrappedBuffer.
    private static void AddSource(Storage storage, CompositeByteBuf buf, List<ByteBuf> sources, params byte[] data)
    {
        ByteBuf source = storage.Allocator.Buffer(data.Length, data.Length).WriteBytes(data);
        sources.Add(source);
        buf.AddComponent(source);
        // CLR: AddComponent without increaseWriterIndex leaves the writer at zero.
        Assert.Equal(0, buf.WriterIndex);
    }

    // CLR: contiguous Memory and MemoryMarshal.TryGetArray replace hasArray()/array()/arrayOffset(). Native storage
    // has no array; pooled heap memory can start inside its array, so only unpooled heap requires offset zero.
    private static void AssertSingleConsolidatedComponent(CompositeByteBuf buf, bool pooled, bool direct)
    {
        Assert.Equal(6, buf.Capacity);
        Assert.Equal(0, buf.ReaderIndex);
        Assert.Equal(0, buf.WriterIndex);
        Assert.Equal(direct, buf.IsDirect);
        byte[] expected = [1, 2, 3, 4, 5, 6];
        var actual = new byte[expected.Length];
        buf.GetBytes(0, actual);
        Assert.Equal(expected, actual);
        ReadOnlyMemory<byte> memory = buf.AsMemory(0, 6);
        Assert.Equal(expected, memory.ToArray());
        Assert.Equal(!direct, MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> array));
        if (!direct)
        {
            Assert.NotNull(array.Array);
            Assert.Equal(6, array.Count);
            if (!pooled) Assert.Equal(0, array.Offset);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestAutoConsolidation(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        CompositeByteBuf buf = storage.Allocator.CompositeBuffer(2);
        var sources = new List<ByteBuf>();
        try
        {
            AddSource(storage, buf, sources, 1);
            Assert.Equal(1, buf.NumComponents);

            AddSource(storage, buf, sources, 2, 3);
            Assert.Equal(2, buf.NumComponents);

            AddSource(storage, buf, sources, 4, 5, 6);

            Assert.Equal(1, buf.NumComponents);
            AssertSingleConsolidatedComponent(buf, pooled, direct);
            // CLR: the replaced sources are released.
            Assert.All(sources, source => Assert.Equal(0, source.RefCnt));
        }
        finally { buf.Release(); }
        Assert.All(sources, source => Assert.Equal(0, source.RefCnt));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestFullConsolidation(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        CompositeByteBuf buf = storage.Allocator.CompositeBuffer(int.MaxValue);
        var sources = new List<ByteBuf>();
        try
        {
            AddSource(storage, buf, sources, 1);
            AddSource(storage, buf, sources, 2, 3);
            AddSource(storage, buf, sources, 4, 5, 6);
            // CLR: each source is still its own component before consolidate().
            Assert.Equal(3, buf.NumComponents);
            Assert.Same(buf, buf.Consolidate());

            Assert.Equal(1, buf.NumComponents);
            AssertSingleConsolidatedComponent(buf, pooled, direct);
            // CLR: the replaced sources are released.
            Assert.All(sources, source => Assert.Equal(0, source.RefCnt));
        }
        finally { buf.Release(); }
        Assert.All(sources, source => Assert.Equal(0, source.RefCnt));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestRangedConsolidation(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        CompositeByteBuf buf = storage.Allocator.CompositeBuffer(int.MaxValue);
        var sources = new List<ByteBuf>();
        try
        {
            AddSource(storage, buf, sources, 1);
            AddSource(storage, buf, sources, 2, 3);
            AddSource(storage, buf, sources, 4, 5, 6);
            AddSource(storage, buf, sources, 7, 8, 9, 10);
            // CLR: each source is still its own component before consolidate(1, 2).
            Assert.Equal(4, buf.NumComponents);
            Assert.Same(buf, buf.Consolidate(1, 2));

            Assert.Equal(3, buf.NumComponents);
            // CLR: readable component bytes replace the equality with freshly wrapped buffers.
            Assert.Equal(new byte[] { 1 }, buf.Component(0).ReadableMemory.ToArray());
            Assert.Equal(new byte[] { 2, 3, 4, 5, 6 }, buf.Component(1).ReadableMemory.ToArray());
            Assert.Equal(new byte[] { 7, 8, 9, 10 }, buf.Component(2).ReadableMemory.ToArray());
            // CLR: the layout, storage kind and bytes are unchanged; only the two replaced sources are released.
            Assert.Equal(10, buf.Capacity);
            Assert.Equal(0, buf.ReaderIndex);
            Assert.Equal(0, buf.WriterIndex);
            Assert.Equal(direct, buf.IsDirect);
            var actual = new byte[10];
            buf.GetBytes(0, actual);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, actual);
            Assert.Equal(1, sources[0].RefCnt);
            Assert.Equal(0, sources[1].RefCnt);
            Assert.Equal(0, sources[2].RefCnt);
            Assert.Equal(1, sources[3].RefCnt);
        }
        finally { buf.Release(); }
        Assert.All(sources, source => Assert.Equal(0, source.RefCnt));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestCompositeToSingleBuffer(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        CompositeByteBuf composite = storage.Allocator.CompositeBuffer(3);
        var sources = new List<ByteBuf>();
        ByteBuf copy = null;
        try
        {
            foreach (byte[] data in new[] { new byte[] { 1, 2, 3 }, new byte[] { 4 }, new byte[] { 5, 6 } })
            {
                ByteBuf source = storage.Allocator.Buffer(data.Length, data.Length).WriteBytes(data);
                sources.Add(source);
                composite.AddComponent(source);
                Assert.Equal(sources.Count, composite.NumComponents);
            }
            // NOTE: hard-coding 6 here, since it seems like addComponent doesn't bump the writer index.
            // I'm unsure as to whether or not this is correct behavior
            // CLR: Java's merged nioBuffer is replaced by an explicit owned copy before borrowing Memory.
            copy = composite.Copy(0, 6);
            Assert.Equal(6, copy.ReadableMemory.Length);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, copy.ReadableMemory.ToArray());
            Assert.Same(composite.Alloc(), copy.Alloc());
            Assert.Equal(3, composite.NumComponents);
            Assert.Equal(0, composite.WriterIndex);
            Assert.All(sources, source => Assert.Equal(1, source.RefCnt));
            copy.SetByte(0, 99);
            Assert.Equal(1, composite.GetByte(0));
        }
        finally { copy?.Release(); composite.Release(); }
        Assert.All(sources, source => Assert.Equal(0, source.RefCnt));
    }

    [Theory]
    [MemberData(nameof(Copies))]
    public void BothCopyOverloadsKeepAllocatorStoragePolicyAndIndependentBytes(bool pooled, bool direct, bool indexed)
    {
        using var storage = new Storage(pooled, direct);
        CompositeByteBuf composite = storage.Allocator.CompositeBuffer();
        ByteBuf copy = null;
        try
        {
            // CLR: the original truncated eight-byte factory contributes 1/2/3/2 real bytes.
            // Empty separators are omitted here so the native variation also has an all-native component set.
            foreach (int size in new[] { 1, 2, 3, 2 })
                composite.AddComponent(storage.Allocator.Buffer(size, size).WriteZero(size), true);
            composite.WriterIndex = 0;
            composite.WriteZero(4);
            copy = indexed ? composite.Copy(0, 4) : composite.Copy();
            Assert.True(ByteBufUtil.Equals(composite, copy));
            Assert.Equal(composite.IsDirect, copy.IsDirect);
            Assert.Same(composite.Alloc(), copy.Alloc());
            Assert.Equal(4, copy.ReadableBytes);
            Assert.Equal(0, composite.ReaderIndex);
            Assert.Equal(4, composite.WriterIndex);
            copy.SetByte(0, 7);
            Assert.Equal(0, composite.GetByte(0));
            Assert.True(composite.Release());
            Assert.Equal(new byte[] { 7, 0, 0, 0 }, copy.ReadableMemory.ToArray());
        }
        finally
        {
            copy?.Release();
            if (composite.RefCnt > 0) composite.Release();
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void EmptyCopyOwnsItsLifetimeWhileEmptyDuplicateBorrowsTheComposite(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        CompositeByteBuf composite = storage.Allocator.CompositeBuffer();
        ByteBuf copy = null, duplicate = null;
        try
        {
            Assert.Equal(0, composite.NumComponents);
            copy = composite.Copy();
            duplicate = composite.Duplicate();
            Assert.Equal(0, copy.ReadableBytes);
            Assert.Equal(0, duplicate.ReadableBytes);
            Assert.Equal(1, composite.RefCnt);
            Assert.Equal(1, copy.RefCnt);
            Assert.Equal(1, duplicate.RefCnt);
            Assert.True(composite.Release());
            Assert.Equal(0, duplicate.RefCnt);
            Assert.Equal(1, copy.RefCnt);
            Assert.Equal(0, copy.ReadableBytes);
        }
        finally
        {
            copy?.Release();
            if (composite.RefCnt > 0) composite.Release();
        }
    }

    [Fact]
    public void TestCopyEmpty()
    {
        using var storage = new Storage(false, false);
        CompositeByteBuf buf = storage.Allocator.CompositeBuffer();
        ByteBuf copy = null;
        try
        {
            Assert.Equal(0, buf.NumComponents);
            copy = buf.Copy();
            Assert.Equal(0, copy.ReadableBytes);
        }
        finally { buf.Release(); copy?.Release(); }
    }

    [Fact]
    public void TestDuplicateEmpty()
    {
        using var storage = new Storage(false, false);
        CompositeByteBuf buf = storage.Allocator.CompositeBuffer();
        try
        {
            Assert.Equal(0, buf.NumComponents);
            Assert.Equal(0, buf.Duplicate().ReadableBytes);
        }
        finally { buf.Release(); }
    }
}
