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
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class CompositeByteBufLookupTest
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

    public static IEnumerable<object[]> Limits()
    {
        foreach (object[] provider in Providers())
        foreach (int limit in new[] { 0, 1, 5 }) yield return [provider[0], provider[1], limit];
    }

    /**
     * Tests the "getBufferFor" method
     */
    [Theory]
    [MemberData(nameof(Providers))]
    public void ComponentAtEveryOffsetReturnsTheOriginalSourceDuplicate(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        ByteBuf first = storage.Allocator.Buffer(5, 5).WriteBytes(new byte[] { 1, 2, 3, 4, 5 });
        ByteBuf second = storage.Allocator.Buffer(7, 7).WriteBytes(new byte[] { 4, 5, 6, 7, 8, 9, 26 });
        CompositeByteBuf composite = storage.Allocator.CompositeBuffer().AddComponents(new[] { first, second }, true);
        try
        {
            //Ensure that a random place will be fine
            Assert.Equal(5, composite.ComponentAtOffset(2).Capacity);
            //Loop through each byte
            for (int offset = 0; offset < composite.Capacity; ++offset)
            {
                ByteBuf view = composite.ComponentAtOffset(offset);
                ByteBuf source = offset < 5 ? first : second;
                Assert.NotNull(view);
                Assert.True(view.Capacity > 0);
                Assert.True(view.GetByte(0) > 0);
                Assert.True(view.GetByte(view.ReadableBytes - 1) > 0);
                Assert.Equal(source.Capacity, view.Capacity);
                Assert.Equal(source.GetByte(0), view.GetByte(0));
                Assert.Equal(source.GetByte(source.Capacity - 1), view.GetByte(view.Capacity - 1));
                Assert.NotSame(source, view);
                view.ReaderIndex = 1;
                Assert.Equal(0, source.ReaderIndex);
                Assert.Equal(1, source.ReferenceCount);
            }
            Assert.Equal(0, composite.ReaderIndex);
            Assert.Equal(12, composite.WriterIndex);
        }
        finally { composite.Release(); }
        Assert.Equal(0, first.ReferenceCount);
        Assert.Equal(0, second.ReferenceCount);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void ByteAndComponentIndicesMatchEveryOriginalBoundary(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        byte[][] bytes = [new byte[] { 1, 2, 3, 4, 5 }, new byte[] { 4, 5, 6, 7, 8, 9, 26 },
            new byte[] { 10, 9, 8, 7, 6, 5, 33 }];
        CompositeByteBuf composite = storage.Allocator.CompositeBuffer();
        try
        {
            foreach (byte[] data in bytes)
                composite.AddComponent(storage.Allocator.Buffer(data.Length, data.Length).WriteBytes(data), true);
            // spot checks
            Assert.Equal(0, composite.ToComponentIndex(4));
            Assert.Equal(1, composite.ToComponentIndex(5));
            Assert.Equal(2, composite.ToComponentIndex(15));
            //Loop through each byte
            for (int offset = 0; offset < composite.Capacity; ++offset)
            {
                int componentIndex = composite.ToComponentIndex(offset);
                Assert.Equal(offset < 5 ? 0 : offset < 12 ? 1 : 2, componentIndex);
                int start = composite.ToByteIndex(componentIndex);
                Assert.Equal(bytes[componentIndex][offset - start], composite.GetByte(offset));
            }
            // spot checks
            Assert.Equal(0, composite.ToByteIndex(0));
            Assert.Equal(5, composite.ToByteIndex(1));
            Assert.Equal(12, composite.ToByteIndex(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => composite.ToComponentIndex(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => composite.ToComponentIndex(19));
            Assert.Throws<ArgumentOutOfRangeException>(() => composite.ToByteIndex(-1));
            // CLR rejects the missing component explicitly; pinned Java dereferences a null slot here.
            Assert.Throws<ArgumentOutOfRangeException>(() => composite.ToByteIndex(3));
        }
        finally { composite.Release(); }
    }

    private static CompositeByteBuf NewFragmentedBuffer(IByteBufAllocator allocator, int length)
    {
        // Ensure that we are really testing a CompositeByteBuf
        CompositeByteBuf composite = allocator.CompositeBuffer(int.MaxValue);
        try
        {
            for (int offset = 0; offset < length + 45; offset += 45)
            {
                composite.AddComponent(Unpooled.EmptyBuffer);
                for (int size = 1; size <= 9; ++size)
                {
                    composite.AddComponent(allocator.Buffer(size, size).WriteZero(size), true);
                    composite.AddComponent(Unpooled.EmptyBuffer);
                }
            }
            // Truncate to the requested capacity.
            composite.Capacity = length;
            Assert.Equal(length, composite.Capacity);
            Assert.Equal(length, composite.ReadableBytes);
            Assert.False(composite.IsWritable);
            composite.WriterIndex = 0;
            return composite;
        }
        catch { composite.Release(); throw; }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void FragmentedFactoryDoesNotClaimContiguousStorage(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        CompositeByteBuf composite = NewFragmentedBuffer(storage.Allocator, 4);
        try { Assert.False(composite.IsContiguous); }
        finally { Assert.True(composite.Release()); }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void RelativeReadsFindNestedComponentsAcrossSkippedBytes(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        CompositeByteBuf composite = storage.Allocator.CompositeBuffer();
        var sources = new List<ByteBuf>();
        try
        {
            foreach (byte value in new byte[] { (byte)'a', (byte)'b', (byte)'c' })
            {
                CompositeByteBuf source = NewFragmentedBuffer(storage.Allocator, 10);
                source.WriteByte(value);
                sources.Add(source);
                composite.AddComponent(source, true);
            }
            Assert.Equal((byte)'a', composite.ReadByte());
            composite.SkipBytes(1);
            Assert.Equal((byte)'c', composite.ReadByte());
            Assert.Equal(3, composite.ReaderIndex);
            Assert.Equal(3, composite.WriterIndex);
            Assert.Equal(3, composite.Capacity);
        }
        finally { composite.Release(); }
        Assert.All(sources, source => Assert.Equal(0, source.ReferenceCount));
    }

    /**
     * Assert that a new {@linkplain CompositeByteBuf} was created successfully with the desired number of max
     * components.
     */
    [Theory]
    [MemberData(nameof(Limits))]
    public void ComponentLimitRejectsZeroAndPreservesPositiveValues(bool pooled, bool direct, int limit)
    {
        using var storage = new Storage(pooled, direct);
        if (limit == 0)
        {
            // CLR uses its standard parameter exception rather than Java's message format.
            var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CompositeByteBuf(storage.Allocator, direct, limit));
            Assert.Equal("maxNumComponents", error.ParamName);
            return;
        }
        CompositeByteBuf composite = new(storage.Allocator, direct, limit);
        try { Assert.Equal(limit, composite.MaxNumComponents); }
        finally { Assert.True(composite.Release()); }
    }
}
