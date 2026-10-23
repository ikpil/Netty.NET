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

public class ByteBufComparisonSourceTest
{
    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native = new();
        private readonly PooledByteBufAllocator _pool;
        public readonly ByteBuf Root;
        public Storage(int provider, int length)
        {
            if (provider >= 3)
                _pool = new PooledByteBufAllocator(nativeAllocator: Native, maxOrder: 1,
                    smallCacheSize: 0, normalCacheSize: 0);
            Root = provider switch
            {
                0 => new UnpooledHeapByteBuf(length),
                1 => new UnpooledDirectByteBuf(length, int.MaxValue, Native),
                2 => new CompositeByteBuf(16, false, Native)
                    .AddComponent(new UnpooledHeapByteBuf(length / 2).SetIndex(0, length / 2), true)
                    .AddComponent(new UnpooledDirectByteBuf(length - length / 2, int.MaxValue, Native)
                        .SetIndex(0, length - length / 2), true),
                3 => new PooledHeapByteBuf((PoolHeapArena)_pool.HeapArenas[0], length, int.MaxValue),
                _ => new PooledDirectByteBuf((PoolDirectArena)_pool.DirectArenas[0], length, int.MaxValue)
            };
            Root.Clear();
        }
        public void Dispose()
        {
            if (Root.ReferenceCount > 0) Root.Release();
            Assert.Equal(0, Root.ReferenceCount);
            _pool?.Dispose();
            Assert.Equal(0, Native.ReservedBytes);
        }
    }

    public static IEnumerable<object[]> Providers()
    {
        for (int provider = 0; provider < 5; ++provider) yield return [provider];
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void OriginalComparableInterfaceDoesNotOverflowUnsignedWords(int provider)
    {
        using var first = new Storage(provider, 4096);
        using var second = new Storage(provider, 4096);
        ByteBuf buffer = first.Root, buffer2 = second.Root;
        Assert.False(buffer.IsReadOnly);
        buffer.WriterIndex = buffer.ReaderIndex;
        Assert.True(buffer.WritableBytes >= 4);
        buffer.WriteLong(0);
        Assert.False(buffer2.IsReadOnly);
        buffer2.WriterIndex = buffer2.ReaderIndex;
        // Write an unsigned integer that will cause buffer.getUnsignedInt() - buffer2.getUnsignedInt() to underflow the
        // int type and wrap around on the negative side.
        buffer2.WriteLong(0xF0000000L);
        Assert.True(buffer.CompareTo(buffer2) < 0); Assert.True(buffer2.CompareTo(buffer) > 0);
        Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(8, buffer.WriterIndex);
        Assert.Equal(0, buffer2.ReaderIndex); Assert.Equal(8, buffer2.WriterIndex);
        Assert.Equal(1, buffer.ReferenceCount); Assert.Equal(1, buffer2.ReferenceCount);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void OriginalEqualityUsesContentAndRejectsNullAndOtherObjects(int provider)
    {
        using var s = new Storage(provider, 4096);
        ByteBuf buffer = s.Root;
        Assert.False(buffer.Equals((object)null)); Assert.False(buffer.Equals(new object()));
        byte[] value = new byte[32];
        buffer.SetIndex(0, value.Length);
        new Random(12345).NextBytes(value);
        buffer.SetBytes(0, value);
        ByteBuf expected = Unpooled.WrappedBuffer(value);
        try
        {
            Assert.True(buffer.Equals((object)expected));
            // CLR has explicit endian accessors instead of a mutable order facade.
            Assert.True(((IEquatable<ByteBuf>)buffer).Equals(expected.Duplicate()));
            value[0] = unchecked((byte)(value[0] + 1));
            Assert.False(buffer.Equals((object)expected));
            Assert.False(((IEquatable<ByteBuf>)buffer).Equals(expected.Duplicate()));
            Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(32, buffer.WriterIndex);
            Assert.Equal(1, buffer.ReferenceCount); Assert.Equal(1, expected.ReferenceCount);
        }
        finally { Assert.True(expected.Release()); }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void OriginalComparisonUsesUnsignedContentPrefixesAndRetainedWindows(int provider)
    {
        using var s = new Storage(provider, 4096);
        ByteBuf buffer = s.Root;
        // CLR IComparable sorts a non-null value after null; Java's original test expects an exception.
        Assert.Equal(1, buffer.CompareTo(null));
        // Fill the random stuff
        byte[] value = new byte[32]; new Random(12345).NextBytes(value);
        // Prevent overflow / underflow
        if (value[0] == 0) ++value[0];
        else if (value[0] == 255) --value[0];
        buffer.SetIndex(0, value.Length).SetBytes(0, value);
        ByteBuf expected = Unpooled.WrappedBuffer(value);
        try
        {
            ByteBuf alternate = expected.Duplicate();
            Assert.Equal(0, buffer.CompareTo(expected)); Assert.Equal(0, buffer.CompareTo(alternate));
            ++value[0];
            Assert.True(buffer.CompareTo(expected) < 0); Assert.True(buffer.CompareTo(alternate) < 0);
            value[0] -= 2;
            Assert.True(buffer.CompareTo(expected) > 0); Assert.True(buffer.CompareTo(alternate) > 0);
            ++value[0];
            Assert.True(buffer.CompareTo(expected.Slice(0, 31)) > 0);
            Assert.True(buffer.CompareTo(alternate.Slice(0, 31)) > 0);
            Assert.True(buffer.Slice(0, 31).CompareTo(expected) < 0);
            Assert.True(buffer.Slice(0, 31).CompareTo(alternate) < 0);
            foreach (ByteBuf comparison in new[] { expected, alternate })
            {
                ByteBuf retained = buffer.RetainedSlice(0, 31);
                try { Assert.True(retained.CompareTo(comparison) < 0); }
                finally { retained.Release(); }
            }
            Assert.Equal(value, ByteBufUtil.GetBytes(buffer, 0, 32));
            Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(32, buffer.WriterIndex);
            Assert.Equal(1, buffer.ReferenceCount); Assert.Equal(1, expected.ReferenceCount);
        }
        finally { Assert.True(expected.Release()); }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void OriginalComparisonIsIndependentOfEndianInterpretation(int provider)
    {
        byte[] bytes = [1, 2, 3, 4], bytesReversed = [4, 3, 2, 1];
        using var first = new Storage(provider, 4);
        using var second = new Storage(provider, 4);
        using var third = new Storage(provider, 4);
        using var fourth = new Storage(provider, 4);
        ByteBuf buf1 = first.Root.Clear().WriteBytes(bytes), buf2 = second.Root.Clear().WriteBytes(bytesReversed);
        ByteBuf buf3 = third.Root.Clear().WriteBytes(bytes), buf4 = fourth.Root.Clear().WriteBytes(bytesReversed);
        // Fixed byte content is compared; explicit BE/LE reads replace Java's order facades.
        Assert.Equal(0x04030201, buf1.GetIntLE(0)); Assert.Equal(0x01020304, buf3.GetInt(0));
        Assert.Equal(0x01020304, buf2.GetIntLE(0)); Assert.Equal(0x04030201, buf4.GetInt(0));
        Assert.Equal(buf1.CompareTo(buf2), buf3.CompareTo(buf4));
        Assert.Equal(buf2.CompareTo(buf1), buf4.CompareTo(buf3));
        Assert.Equal(buf1.CompareTo(buf3), buf2.CompareTo(buf4));
        Assert.Equal(buf3.CompareTo(buf1), buf4.CompareTo(buf2));
        foreach (ByteBuf buffer in new[] { buf1, buf2, buf3, buf4 })
        { Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(4, buffer.WriterIndex); Assert.Equal(1, buffer.ReferenceCount); }
    }
}
