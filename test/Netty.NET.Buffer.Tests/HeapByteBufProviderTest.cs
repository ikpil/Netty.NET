/*
 * Copyright 2015 The Netty Project
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
using System.Linq;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// The JVM Unsafe heap subtypes use the shared bounded CLR heap/pooled kernel.
public class HeapByteBufProviderTest
{
    private sealed class Storage : IDisposable
    {
        public readonly IByteBufAllocator Allocator;
        public readonly ByteBuf Left, Target, Right;
        public readonly bool Pooled;
        public Storage(bool pooled)
        {
            Pooled = pooled;
            Allocator = pooled ? new PooledByteBufAllocator(false, poolDirect: false, pageSize: 4096, maxOrder: 1)
                : new UnpooledByteBufAllocator(false);
            Left = Allocator.HeapBuffer(128, 128);
            Target = Allocator.HeapBuffer(128, 512);
            Right = Allocator.HeapBuffer(128, 128);
            Left.SetBytes(0, Enumerable.Repeat((byte)0xa5, 128).ToArray());
            Right.SetBytes(0, Enumerable.Repeat((byte)0x5a, 128).ToArray());
            Assert.False(Target.IsDirect); Assert.True(Target.IsContiguous);
            Assert.Same(Allocator, Target.Allocator);
            if (pooled)
            {
                var a = Assert.IsAssignableFrom<PooledByteBuf>(Owner(Left)).Allocation;
                var b = Assert.IsAssignableFrom<PooledByteBuf>(Owner(Target)).Allocation;
                var c = Assert.IsAssignableFrom<PooledByteBuf>(Owner(Right)).Allocation;
                Assert.Same(a.Storage, b.Storage); Assert.Same(b.Storage, c.Storage);
                Assert.True(b.Offset > 0); Assert.NotEqual(a.Offset, b.Offset); Assert.NotEqual(b.Offset, c.Offset);
            }
            else Assert.IsAssignableFrom<UnpooledHeapByteBuf>(Owner(Target));
        }
        public void Neighbors()
        {
            Assert.Equal(Enumerable.Repeat((byte)0xa5, 128), ByteBufUtil.GetBytes(Left, 0, 128));
            Assert.Equal(Enumerable.Repeat((byte)0x5a, 128), ByteBufUtil.GetBytes(Right, 0, 128));
        }
        public void Dispose()
        {
            foreach (ByteBuf buffer in new[] { Left, Target, Right })
                if (buffer.ReferenceCount > 0) Assert.True(buffer.Release());
            (Allocator as IDisposable)?.Dispose();
            var metric = ((IByteBufAllocatorMetricProvider)Allocator).Metric;
            Assert.Equal(0, metric.UsedHeapMemory);
            Assert.Equal(Pooled ? -1 : 0, metric.UsedDirectMemory); // No direct arena reports unknown (-1).
        }
    }

    private static ByteBuf Owner(ByteBuf buffer)
    {
        while (buffer.Unwrap() is ByteBuf parent) buffer = parent;
        return buffer;
    }

    public static IEnumerable<object[]> ScalarCases()
    {
        foreach (bool pooled in new[] { false, true })
        foreach (int index in new[] { 0, 1, 7, 120 }) yield return [pooled, index];
    }

    [Theory]
    [MemberData(nameof(ScalarCases))]
    public void AllScalarAccessorsRespectWireOrderUnalignedRangesAndPoolOffsets(bool pooled, int index)
    {
        using var s = new Storage(pooled);
        int[] widths = [1, 2, 2, 3, 3, 4, 4, 8, 8];
        foreach (ulong value in new ulong[] { 0, 0x1122334455667788, 0xffeeddcc8000fedc })
        for (int operation = 0; operation < widths.Length; ++operation)
        {
            byte[] expected = new byte[128]; Array.Fill(expected, (byte)0xcc);
            s.Target.SetBytes(0, expected).SetIndex(3, 100).MarkReaderIndex().MarkWriterIndex();
            int width = widths[operation]; bool little = operation != 0 && (operation & 1) == 0;
            for (int i = 0; i < width; ++i)
                expected[index + i] = unchecked((byte)(value >> (8 * (little ? i : width - i - 1))));
            ByteBuf receiver = operation switch
            {
                0 => s.Target.SetByte(index, unchecked((int)value)),
                1 => s.Target.SetShort(index, unchecked((int)value)),
                2 => s.Target.SetShortLE(index, unchecked((int)value)),
                3 => s.Target.SetMedium(index, unchecked((int)value)),
                4 => s.Target.SetMediumLE(index, unchecked((int)value)),
                5 => s.Target.SetInt(index, unchecked((int)value)),
                6 => s.Target.SetIntLE(index, unchecked((int)value)),
                7 => s.Target.SetLong(index, unchecked((long)value)),
                _ => s.Target.SetLongLE(index, unchecked((long)value))
            };
            Assert.Same(s.Target, receiver);
            Assert.Equal(expected, ByteBufUtil.GetBytes(s.Target, 0, 128));
            long read = operation switch
            {
                0 => s.Target.GetByte(index), 1 => s.Target.GetShort(index), 2 => s.Target.GetShortLE(index),
                3 => s.Target.GetUnsignedMedium(index), 4 => s.Target.GetUnsignedMediumLE(index),
                5 => s.Target.GetInt(index), 6 => s.Target.GetIntLE(index),
                7 => s.Target.GetLong(index), _ => s.Target.GetLongLE(index)
            };
            long wanted = width switch
            {
                1 => unchecked((byte)value), 2 => unchecked((short)value), 3 => (long)(value & 0xffffff),
                4 => unchecked((int)value), _ => unchecked((long)value)
            };
            Assert.Equal(wanted, read);
            Assert.Equal(3, s.Target.ReaderIndex); Assert.Equal(100, s.Target.WriterIndex);
            s.Target.SetIndex(0, 128).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(3, s.Target.ReaderIndex); Assert.Equal(100, s.Target.WriterIndex);
            s.Neighbors();
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Target.GetLong(121));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Target.SetLong(121, 1));
    }

    public static IEnumerable<object[]> ZeroCases()
    {
        foreach (bool pooled in new[] { false, true })
        foreach (int length in new[] { 0, 1, 7, 8, 63, 64, 65, 128, 129 }) yield return [pooled, length];
    }

    [Theory]
    [MemberData(nameof(ZeroCases))]
    public void ZeroFillPreservesIndicesAndNeighborsAcrossOriginalSmallWriteThreshold(bool pooled, int length)
    {
        using var s = new Storage(pooled);
        byte[] expected = new byte[128]; Array.Fill(expected, (byte)0xcc);
        s.Target.SetBytes(0, expected).SetIndex(3, 100).MarkReaderIndex().MarkWriterIndex();
        int index = length < 128 ? 1 : 0;
        if (length > 128) Assert.Throws<ArgumentOutOfRangeException>(() => s.Target.SetZero(index, length));
        else
        {
            Array.Clear(expected, index, length);
            Assert.Same(s.Target, s.Target.SetZero(index, length));
        }
        Assert.Equal(expected, ByteBufUtil.GetBytes(s.Target, 0, 128));
        Assert.Equal(3, s.Target.ReaderIndex); Assert.Equal(100, s.Target.WriterIndex);
        s.Target.SetIndex(0, 128).ResetReaderIndex().ResetWriterIndex();
        Assert.Equal(3, s.Target.ReaderIndex); Assert.Equal(100, s.Target.WriterIndex);
        s.Neighbors();
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void WriteZeroReservesSpaceAndPublishesTheExactWriterAdvance(bool pooled, bool grow)
    {
        using var s = new Storage(pooled);
        byte[] prefix = new byte[128]; Array.Fill(prefix, (byte)0xcc);
        int writer = grow ? 120 : 10;
        s.Target.SetBytes(0, prefix).SetIndex(3, writer).MarkReaderIndex().MarkWriterIndex();
        Assert.Same(s.Target, s.Target.WriteZero(16));
        Assert.Equal(grow ? 256 : 128, s.Target.Capacity);
        byte[] expected = new byte[writer + 16]; Array.Fill(expected, (byte)0xcc, 0, writer);
        Assert.Equal(expected, ByteBufUtil.GetBytes(s.Target, 0, writer + 16));
        Assert.Equal(3, s.Target.ReaderIndex); Assert.Equal(writer + 16, s.Target.WriterIndex);
        s.Target.ResetReaderIndex().ResetWriterIndex();
        Assert.Equal(3, s.Target.ReaderIndex); Assert.Equal(writer, s.Target.WriterIndex);
        s.Neighbors();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SubsequentAllocationCannotResurrectAReleasedClrBuffer(bool pooled)
    {
        using var s = new Storage(pooled);
        ByteBuf staleOwner = Owner(s.Target);
        Assert.True(s.Target.Release());
        ByteBuf replacement = s.Allocator.HeapBuffer(128, 512);
        try
        {
            Assert.NotSame(staleOwner, Owner(replacement));
            Assert.Equal(0, staleOwner.ReferenceCount); Assert.Equal(1, replacement.ReferenceCount);
            Assert.Throws<IllegalReferenceCountException>(() => staleOwner.GetByte(0));
            Assert.Throws<IllegalReferenceCountException>(() => staleOwner.Retain());
            replacement.SetByte(0, 42); Assert.Equal(42, replacement.GetByte(0));
            s.Neighbors();
        }
        finally { replacement.Release(); }
    }
}
