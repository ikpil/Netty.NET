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
        foreach (bool direct in new[] { false, true })
            {
                yield return [pooled, direct];
            }
    }

    /**
     * Tests the "getBufferFor" method
     */
    [Theory]
    [MemberData(nameof(Providers))]
    public void TestComponentAtOffset(bool pooled, bool direct)
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
                Assert.Equal(1, source.RefCnt);
            }

            Assert.Equal(0, composite.ReaderIndex);
            Assert.Equal(12, composite.WriterIndex);
        }
        finally
        {
            composite.Release();
        }

        Assert.Equal(0, first.RefCnt);
        Assert.Equal(0, second.RefCnt);
    }

    private static readonly byte[][] IndexBytes =
        [new byte[] { 1, 2, 3, 4, 5 }, new byte[] { 4, 5, 6, 7, 8, 9, 26 }, new byte[] { 10, 9, 8, 7, 6, 5, 33 }];

    // CLR: allocator-owned components over heap/native pooled/unpooled factories replace wrappedBuffer(byte[]...).
    private static CompositeByteBuf NewIndexBuffer(Storage storage)
    {
        CompositeByteBuf composite = storage.Allocator.CompositeBuffer();

        try
        {
            foreach (byte[] data in IndexBytes)
                composite.AddComponent(storage.Allocator.Buffer(data.Length, data.Length).WriteBytes(data), true);

            return composite;
        }
        catch
        {
            composite.Release();
            throw;
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestToComponentIndex(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        CompositeByteBuf buf = NewIndexBuffer(storage);

        try
        {
            // spot checks
            Assert.Equal(0, buf.ToComponentIndex(4));
            Assert.Equal(1, buf.ToComponentIndex(5));
            Assert.Equal(2, buf.ToComponentIndex(15));

            //Loop through each byte

            byte index = 0;

            while (index < buf.Capacity)
            {
                int offset = index;
                int cindex = buf.ToComponentIndex(index++);
                Assert.True(cindex >= 0 && cindex < buf.NumComponents);
                // CLR: the exact component of every offset, and its byte through the component start.
                Assert.Equal(offset < 5 ? 0 : offset < 12 ? 1 : 2, cindex);
                Assert.Equal(IndexBytes[cindex][offset - buf.ToByteIndex(cindex)], buf.GetByte(offset));
            }

            // CLR: negative and end offsets are rejected with ArgumentOutOfRangeException.
            Assert.Throws<ArgumentOutOfRangeException>(() => buf.ToComponentIndex(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => buf.ToComponentIndex(19));
        }
        finally
        {
            buf.Release();
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestToByteIndex(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        CompositeByteBuf buf = NewIndexBuffer(storage);

        try
        {
            // spot checks
            Assert.Equal(0, buf.ToByteIndex(0));
            Assert.Equal(5, buf.ToByteIndex(1));
            Assert.Equal(12, buf.ToByteIndex(2));

            // CLR: every component start maps back to its component.
            for (int cindex = 0; cindex < buf.NumComponents; ++cindex)
                Assert.Equal(cindex, buf.ToComponentIndex(buf.ToByteIndex(cindex)));

            Assert.Throws<ArgumentOutOfRangeException>(() => buf.ToByteIndex(-1));
            // CLR rejects the missing component explicitly; pinned Java dereferences a null slot here.
            Assert.Throws<ArgumentOutOfRangeException>(() => buf.ToByteIndex(3));
        }
        finally
        {
            buf.Release();
        }
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
            Assert.False(composite.IsWritable());
            composite.WriterIndex = 0;
            return composite;
        }
        catch
        {
            composite.Release();
            throw;
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestIsContiguousAcrossProviders(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        CompositeByteBuf composite = NewFragmentedBuffer(storage.Allocator, 4);

        try
        {
            Assert.False(composite.IsContiguous);
        }
        finally
        {
            Assert.True(composite.Release());
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestFindComponentAcrossProviders(bool pooled, bool direct)
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
        finally
        {
            composite.Release();
        }

        Assert.All(sources, source => Assert.Equal(0, source.RefCnt));
    }

    // CLR: the original explicit allocator/direct constructor values over heap/native pooled/unpooled factories.
    [Theory]
    [MemberData(nameof(Providers))]
    public void TestComponentsLessThanLowerBound(bool pooled, bool direct)
    {
        using var storage = new Storage(pooled, direct);
        // CLR uses its standard parameter exception rather than Java's message format.
        var e = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CompositeByteBuf(storage.Allocator, direct, 0));
        Assert.Equal("maxNumComponents", e.ParamName);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestComponentsEqualToLowerBound(bool pooled, bool direct)
    {
        AssertCompositeBufCreated(pooled, direct, 1);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TestComponentsGreaterThanLowerBound(bool pooled, bool direct)
    {
        AssertCompositeBufCreated(pooled, direct, 5);
    }

    /**
     * Assert that a new {@linkplain CompositeByteBuf} was created successfully with the desired number of max
     * components.
     */
    private static void AssertCompositeBufCreated(bool pooled, bool direct, int expectedMaxComponents)
    {
        using var storage = new Storage(pooled, direct);
        CompositeByteBuf buf = new(storage.Allocator, direct, expectedMaxComponents);

        try
        {
            Assert.Equal(expectedMaxComponents, buf.MaxNumComponents);
        }
        finally
        {
            Assert.True(buf.Release());
        }
    }
}
