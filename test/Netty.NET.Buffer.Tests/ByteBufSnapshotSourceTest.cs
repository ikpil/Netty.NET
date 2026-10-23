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
using System.Buffers.Binary;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ByteBufSnapshotSourceTest
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

    public static IEnumerable<object[]> OriginalCases()
    {
        for (int provider = 0; provider < 5; ++provider)
        for (int scenario = 0; scenario < 10; ++scenario) yield return [provider, scenario];
    }

    [Theory]
    [MemberData(nameof(OriginalCases))]
    public void OriginalCopyDuplicateAndReadOnlyFixtures(int provider, int scenario)
    {
        const int capacity = 4096;
        using var s = new Storage(provider, scenario >= 7 ? 8 : capacity);
        ByteBuf buffer = s.Root;
        if (scenario >= 7)
        {
            if (scenario == 9)
            {
                Assert.Equal(1, buffer.ReferenceCount);
                Assert.True(buffer.AsReadOnly().Release());
                Assert.Equal(0, buffer.ReferenceCount);
            }
            else
            {
                // Clear the buffer so we are sure the reader and writer indices are 0.
                // This is important as we may return a slice from newBuffer(...).
                buffer.Clear(); Assert.True(buffer.Release());
                // Both original declared methods call copy(), including testCopyAfterRelease1.
                Assert.Throws<IllegalReferenceCountException>(() => buffer.Copy());
            }
            return;
        }
        if (scenario < 4)
        {
            (int index, int length) = scenario switch
            { 0 => (-1, 0), 1 => (0, capacity + 1), 2 => (capacity + 1, 0), _ => (capacity, 1) };
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Copy(index, length));
            Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(0, buffer.WriterIndex);
            Assert.Equal(1, buffer.ReferenceCount);
            return;
        }
        byte[] bytes = new byte[capacity]; new Random(12345).NextBytes(bytes);
        for (int i = 0; i < bytes.Length; ++i) buffer.SetByte(i, bytes[i]);
        if (scenario == 6)
        {
            foreach ((int offset, int length) in new[] { (0, capacity), (0, capacity - 1), (1, capacity - 1), (1, capacity - 2) })
            {
                ByteBuf slice = buffer.Slice(offset, length);
                // Explicit BE/LE accessors replace mutable Java order identities.
                Assert.Equal(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4)), slice.GetInt(0));
                Assert.Equal(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4)), slice.GetIntLE(0));
                Assert.Equal(0, slice.ReaderIndex); Assert.Equal(length, slice.WriterIndex);
            }
            Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(0, buffer.WriterIndex);
            return;
        }
        const int reader = capacity / 3, writer = capacity * 2 / 3;
        buffer.SetIndex(reader, writer);
        if (scenario == 4)
        {
            // Make sure all properties are copied.
            ByteBuf copy = buffer.Copy();
            try
            {
                Assert.Equal(0, copy.ReaderIndex); Assert.Equal(buffer.ReadableBytes, copy.WriterIndex);
                Assert.Equal(buffer.ReadableBytes, copy.Capacity);
                Assert.Equal(bytes[reader..writer], ByteBufUtil.GetBytes(copy, 0, copy.Capacity));
                // Make sure the buffer content is independent from each other.
                buffer.SetByte(reader, unchecked((byte)(buffer.GetByte(reader) + 1)));
                Assert.NotEqual(buffer.GetByte(reader), copy.GetByte(0));
                copy.SetByte(1, unchecked((byte)(copy.GetByte(1) + 1)));
                Assert.NotEqual(buffer.GetByte(reader + 1), copy.GetByte(1));
            }
            finally { Assert.True(copy.Release()); Assert.Equal(0, copy.ReferenceCount); }
        }
        else
        {
            // Make sure all properties are copied.
            ByteBuf duplicate = buffer.Duplicate();
            Assert.Equal(buffer.ReadableBytes, duplicate.ReadableBytes); Assert.Equal(0, buffer.CompareTo(duplicate));
            // Make sure the buffer content is shared.
            buffer.SetByte(reader, unchecked((byte)(buffer.GetByte(reader) + 1)));
            Assert.Equal(buffer.GetByte(reader), duplicate.GetByte(duplicate.ReaderIndex));
            duplicate.SetByte(duplicate.ReaderIndex, unchecked((byte)(duplicate.GetByte(duplicate.ReaderIndex) + 1)));
            Assert.Equal(buffer.GetByte(reader), duplicate.GetByte(duplicate.ReaderIndex));
            duplicate.ReaderIndex++;
            Assert.Equal(reader, buffer.ReaderIndex); Assert.Equal(writer, buffer.WriterIndex);
        }
        Assert.Equal(reader, buffer.ReaderIndex); Assert.Equal(writer, buffer.WriterIndex);
        Assert.Equal(1, buffer.ReferenceCount);
    }

    public static IEnumerable<object[]> IndexedCases()
    {
        for (int provider = 0; provider < 5; ++provider)
        for (int form = 0; form < 2; ++form)
        for (int range = 0; range < 3; ++range) yield return [provider, form, range];
    }

    [Theory]
    [MemberData(nameof(IndexedCases))]
    public void IndexedSnapshotsCopyCapacityRangesThroughReadOnlyViewsWithoutSharingClaims(int provider, int form, int range)
    {
        using var s = new Storage(provider, 32);
        byte[] bytes = new byte[32]; for (int i = 0; i < bytes.Length; ++i) bytes[i] = (byte)(i * 7);
        s.Root.SetBytes(0, bytes).SetIndex(5, 9).MarkReaderIndex().MarkWriterIndex();
        Assert.False(s.Root.IsReadOnly);
        ByteBuf source = form == 0 ? s.Root : s.Root.AsReadOnly().SetIndex(3, 7).MarkReaderIndex().MarkWriterIndex();
        if (form == 1) { Assert.True(source.IsReadOnly); Assert.Same(source, source.AsReadOnly()); }
        (int index, int length) = range switch { 0 => (15, 4), 1 => (32, 0), _ => (0, 32) };
        int reader = source.ReaderIndex, writer = source.WriterIndex;
        ByteBuf copy = source.Copy(index, length);
        try
        {
            Assert.Equal(bytes[index..(index + length)], ByteBufUtil.GetBytes(copy, 0, length));
            Assert.Equal(length, copy.Capacity); Assert.Equal(0, copy.ReaderIndex); Assert.Equal(length, copy.WriterIndex);
            Assert.False(copy.IsReadOnly); Assert.Equal(provider is 1 or 4, copy.IsDirect);
            Assert.Same(source.Allocator, copy.Allocator); Assert.Equal(source.MaxCapacity, copy.MaxCapacity);
            Assert.Equal(1, copy.ReferenceCount); Assert.Equal(1, source.ReferenceCount); Assert.Equal(1, s.Root.ReferenceCount);
            if (length != 0)
            {
                s.Root.SetByte(index, unchecked((byte)(bytes[index] + 1)));
                Assert.Equal(bytes[index], copy.GetByte(0));
                copy.SetByte(length - 1, unchecked((byte)(copy.GetByte(length - 1) + 1)));
                Assert.Equal(bytes[index + length - 1], s.Root.GetByte(index + length - 1));
            }
            Assert.Equal(reader, source.ReaderIndex); Assert.Equal(writer, source.WriterIndex);
            Assert.Equal(5, s.Root.ReaderIndex); Assert.Equal(9, s.Root.WriterIndex);
            source.SetIndex(0, source.Capacity).ResetWriterIndex().ResetReaderIndex();
            Assert.Equal(reader, source.ReaderIndex); Assert.Equal(writer, source.WriterIndex);
            if (form != 0) s.Root.SetIndex(0, 32).ResetWriterIndex().ResetReaderIndex();
            Assert.Equal(5, s.Root.ReaderIndex); Assert.Equal(9, s.Root.WriterIndex);
        }
        finally { Assert.True(copy.Release()); Assert.Equal(0, copy.ReferenceCount); }
        Assert.Equal(1, s.Root.ReferenceCount);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void IndexedCopyRejectsReleasedStorageIncludingEmptyCapacityRanges(int provider)
    {
        using var s = new Storage(provider, 8);
        s.Root.Clear(); Assert.True(s.Root.Release());
        foreach ((int index, int length) in new[] { (0, 0), (0, 1), (8, 0) })
            Assert.Throws<IllegalReferenceCountException>(() => s.Root.Copy(index, length));
        Assert.Equal(0, s.Root.ReferenceCount);
    }
}
