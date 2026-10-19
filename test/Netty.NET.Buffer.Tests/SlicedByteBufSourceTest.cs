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

// Source mapping for SlicedByteBuf's length storage and UnpooledSlicedByteBuf's
// nine pairs of unchecked scalar delegates. CLR views use bounded memory instead.
/**
 * Tests sliced channel buffers
 */
public class SlicedByteBufSourceTest
{
    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native = new();
        public readonly PooledByteBufAllocator Pool;
        public readonly ByteBuf Root, Parent, View;
        public Storage(int path, int offset)
        {
            if (path >= 3) Pool = new PooledByteBufAllocator(nativeAllocator: Native, maxOrder: 1);
            Root = path switch
            {
                0 => new UnpooledHeapByteBuf(32, 64),
                1 => new UnpooledDirectByteBuf(32, 64, Native),
                2 => new CompositeByteBuf(16, false, Native)
                    .AddComponent(new UnpooledHeapByteBuf(16, 16).SetIndex(0, 16), true)
                    .AddComponent(new UnpooledDirectByteBuf(16, 16, Native).SetIndex(0, 16), true),
                3 or 5 => new PooledHeapByteBuf((PoolHeapArena)Pool.HeapArenas[0], 32, 64),
                _ => new PooledDirectByteBuf((PoolDirectArena)Pool.DirectArenas[0], 32, 64)
            };
            Root.SetBytes(0, new byte[32]).SetIndex(1, 31).MarkReaderIndex().MarkWriterIndex();
            Parent = path >= 5 ? Root.RetainedSlice(0, 32) : Root;
            View = Parent.Slice(offset, 16);
        }
        public void Dispose()
        {
            if (!ReferenceEquals(Parent, Root) && Parent.ReferenceCount > 0) Parent.Release(Parent.ReferenceCount);
            if (Root.ReferenceCount > 0) Root.Release(Root.ReferenceCount);
            Pool?.Dispose();
            Assert.Equal(0, Native.ReservedBytes);
        }
    }

    public static IEnumerable<object[]> Cases()
    {
        for (int path = 0; path < 7; ++path)
        foreach (int offset in new[] { 0, 3 }) yield return [path, offset];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryScalarDelegateUsesTheSliceAdjustmentAndLeavesNeighborBytesUntouched(int path, int offset)
    {
        using var s = new Storage(path, offset);
        const ulong value = 0xfedcba9876543210;
        int[] widths = [1, 2, 2, 3, 3, 4, 4, 8, 8];
        for (int operation = 0; operation < widths.Length; ++operation)
        {
            byte[] expected = new byte[32]; Array.Fill(expected, (byte)0xcc);
            s.Root.SetBytes(0, expected);
            int width = widths[operation];
            bool little = operation != 0 && (operation & 1) == 0;
            for (int i = 0; i < width; ++i)
                expected[offset + 7 + i] = unchecked((byte)(value >> (8 * (little ? i : width - i - 1))));
            switch (operation)
            {
                case 0: s.View.SetByte(7, unchecked((int)value)); break;
                case 1: s.View.SetShort(7, unchecked((int)value)); break;
                case 2: s.View.SetShortLE(7, unchecked((int)value)); break;
                case 3: s.View.SetMedium(7, unchecked((int)value)); break;
                case 4: s.View.SetMediumLE(7, unchecked((int)value)); break;
                case 5: s.View.SetInt(7, unchecked((int)value)); break;
                case 6: s.View.SetIntLE(7, unchecked((int)value)); break;
                case 7: s.View.SetLong(7, unchecked((long)value)); break;
                default: s.View.SetLongLE(7, unchecked((long)value)); break;
            }
            byte[] actual = new byte[32]; s.Root.GetBytes(0, actual); Assert.Equal(expected, actual);
            long read = operation switch
            {
                0 => s.View.GetByte(7), 1 => s.View.GetShort(7), 2 => s.View.GetShortLE(7),
                3 => s.View.GetUnsignedMedium(7), 4 => s.View.GetUnsignedMediumLE(7),
                5 => s.View.GetInt(7), 6 => s.View.GetIntLE(7),
                7 => s.View.GetLong(7), _ => s.View.GetLongLE(7)
            };
            long wanted = width switch
            {
                1 => unchecked((byte)value), 2 => unchecked((short)value),
                3 => (long)(value & 0xffffff), 4 => unchecked((int)value), _ => unchecked((long)value)
            };
            Assert.Equal(wanted, read);
            Assert.Equal(0, s.View.ReaderIndex); Assert.Equal(16, s.View.WriterIndex);
            Assert.Equal(1, s.Root.ReaderIndex); Assert.Equal(31, s.Root.WriterIndex);
            Assert.Equal(16, s.View.Capacity); Assert.Equal(16, s.View.MaxCapacity);
            Assert.Equal(s.Root.IsContiguous, s.View.IsContiguous);
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void FixedLengthIndependentMarksAndOwnershipSurviveParentCapacityChanges(int path, int offset)
    {
        using var s = new Storage(path, offset);
        Assert.Equal(0, s.View.ReaderIndex); Assert.Equal(16, s.View.WriterIndex);
        s.View.ReaderIndex = 1; s.View.ResetReaderIndex(); Assert.Equal(0, s.View.ReaderIndex);
        s.View.WriterIndex = 15; s.View.ResetWriterIndex(); Assert.Equal(0, s.View.WriterIndex);
        // Run ensureWritable and verify this doesn't change any indexes.
        s.View.EnsureWritable(16); Assert.Equal(0, s.View.WriterIndex);
        s.View.WriterIndex = 16;
        Assert.Throws<ArgumentOutOfRangeException>(() => s.View.EnsureWritable(1));
        Assert.Throws<NotSupportedException>(() => s.View.Capacity = 16);
        // Ensure destination buffer is bigger then what is wrapped in the ByteBuf.
        Assert.Throws<ArgumentOutOfRangeException>(() => s.View.GetBytes(0, new byte[17]));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.View.GetByte(16));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.View.SetByte(16, 1));
        ByteBuf nested = s.View.Slice(2, 10), duplicate = s.View.Duplicate();
        s.Root.SetByte(offset + 4, 23);
        Assert.Equal(23, nested.GetByte(2));
        // The pooled borrowed subclass overrides Duplicate to use root coordinates/capacity.
        Assert.Equal(23, duplicate.GetByte(path >= 5 ? offset + 4 : 4));
        Assert.Equal(path >= 5 ? 32 : 16, duplicate.Capacity);
        Assert.Equal(path >= 5 ? 64 : 16, duplicate.MaxCapacity);
        int count = s.View.ReferenceCount;
        s.View.Retain(); Assert.Equal(count + 1, nested.ReferenceCount);
        Assert.False(nested.Release()); Assert.Equal(count, s.View.ReferenceCount);
        Assert.Equal(path >= 5 ? 2 : 1, s.Root.ReferenceCount);
        Assert.Equal(1, s.Root.ReaderIndex); Assert.Equal(31, s.Root.WriterIndex);
        s.Root.ResetReaderIndex().ResetWriterIndex();
        Assert.Equal(1, s.Root.ReaderIndex); Assert.Equal(31, s.Root.WriterIndex);

        s.Root.Capacity = offset + 4;
        Assert.Equal(16, s.View.Capacity);
        Assert.Equal(path >= 5 ? offset + 4 : 16, duplicate.Capacity);
        Assert.Equal(10, nested.Capacity); Assert.Equal(0, s.View.GetByte(3));
        // CLR safety: validate the live parent's capacity even if pooled physical storage is larger.
        Assert.Throws<ArgumentOutOfRangeException>(() => s.View.GetLong(0));
        s.Root.Capacity = 32;
        s.View.SetByte(4, 99); Assert.Equal(99, s.Root.GetByte(offset + 4));
        Assert.Equal(16, s.View.Capacity); Assert.Equal(16, s.View.WriterIndex);
    }
}
