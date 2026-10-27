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
            => Allocator = pooled
                ? new PooledByteBufAllocator(direct, pageSize: 4096, maxOrder: 1, nativeAllocator: Native)
                : new UnpooledByteBufAllocator(direct, Native);

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

    public static IEnumerable<object[]> Consolidations()
    {
        foreach (object[] provider in Providers())
        foreach (int mode in new[] { 0, 1, 2 }) yield return [provider[0], provider[1], mode];
    }

    public static IEnumerable<object[]> Copies()
    {
        foreach (object[] provider in Providers())
        foreach (bool indexed in new[] { false, true }) yield return [provider[0], provider[1], indexed];
    }

    [Theory]
    [MemberData(nameof(Consolidations))]
    public void OriginalConsolidationShapesPreserveUnwrittenCapacityAndReleaseReplacedClaims(
        bool pooled, bool direct, int mode)
    {
        using var storage = new Storage(pooled, direct);
        CompositeByteBuf composite = storage.Allocator.CompositeBuffer(mode == 0 ? 2 : int.MaxValue);
        var sources = new List<ByteBuf>();
        byte[][] data = [new byte[] { 1 }, new byte[] { 2, 3 }, new byte[] { 4, 5, 6 },
            new byte[] { 7, 8, 9, 10 }];
        try
        {
            int count = mode == 2 ? 4 : 3;
            for (int i = 0; i < count; ++i)
            {
                ByteBuf source = storage.Allocator.Buffer(data[i].Length, data[i].Length).WriteBytes(data[i]);
                sources.Add(source);
                composite.AddComponent(source);
                Assert.Equal(mode == 0 && i == 2 ? 1 : i + 1, composite.NumComponents);
                Assert.Equal(0, composite.WriterIndex);
            }
            if (mode == 1) Assert.Same(composite, composite.Consolidate());
            if (mode == 2) Assert.Same(composite, composite.Consolidate(1, 2));
            Assert.Equal(mode == 2 ? 3 : 1, composite.NumComponents);
            Assert.Equal(mode == 2 ? 10 : 6, composite.Capacity);
            Assert.Equal(0, composite.ReaderIndex);
            Assert.Equal(0, composite.WriterIndex);
            Assert.Equal(direct, composite.IsDirect);
            byte[] expected = mode == 2 ? [1, 2, 3, 4, 5, 6, 7, 8, 9, 10] : [1, 2, 3, 4, 5, 6];
            var actual = new byte[expected.Length];
            composite.GetBytes(0, actual);
            Assert.Equal(expected, actual);
            if (mode == 2)
            {
                Assert.Equal(data[0], composite.Component(0).ReadableMemory.ToArray());
                Assert.Equal(new byte[] { 2, 3, 4, 5, 6 }, composite.Component(1).ReadableMemory.ToArray());
                Assert.Equal(data[3], composite.Component(2).ReadableMemory.ToArray());
            }
            else
            {
                ReadOnlyMemory<byte> memory = composite.AsMemory(0, 6);
                Assert.Equal(expected, memory.ToArray());
                Assert.Equal(!direct, MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> array));
                if (!direct)
                {
                    Assert.NotNull(array.Array);
                    Assert.Equal(6, array.Count);
                    // CLR pooled memory can start inside an array; unpooled storage retains the original zero offset.
                    if (!pooled) Assert.Equal(0, array.Offset);
                }
            }
            for (int i = 0; i < sources.Count; ++i)
                Assert.Equal(mode == 2 && (i == 0 || i == 3) ? 1 : 0, sources[i].ReferenceCount);
        }
        finally { composite.Release(); }
        Assert.All(sources, source => Assert.Equal(0, source.ReferenceCount));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void ExplicitContiguousCopyIncludesCapacityBeyondTheWriterWithoutChangingTheLayout(bool pooled, bool direct)
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
            Assert.Same(composite.Allocator, copy.Allocator);
            Assert.Equal(3, composite.NumComponents);
            Assert.Equal(0, composite.WriterIndex);
            Assert.All(sources, source => Assert.Equal(1, source.ReferenceCount));
            copy.SetByte(0, 99);
            Assert.Equal(1, composite.GetByte(0));
        }
        finally { copy?.Release(); composite.Release(); }
        Assert.All(sources, source => Assert.Equal(0, source.ReferenceCount));
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
            Assert.Same(composite.Allocator, copy.Allocator);
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
            if (composite.ReferenceCount > 0) composite.Release();
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
            Assert.Equal(1, composite.ReferenceCount);
            Assert.Equal(1, copy.ReferenceCount);
            Assert.Equal(1, duplicate.ReferenceCount);
            Assert.True(composite.Release());
            Assert.Equal(0, duplicate.ReferenceCount);
            Assert.Equal(1, copy.ReferenceCount);
            Assert.Equal(0, copy.ReadableBytes);
        }
        finally
        {
            copy?.Release();
            if (composite.ReferenceCount > 0) composite.Release();
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
