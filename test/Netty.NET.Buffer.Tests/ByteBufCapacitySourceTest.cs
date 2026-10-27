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
using System.Text;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ByteBufCapacitySourceTest
{
    private sealed class Storage : IDisposable
    {
        public readonly NativeMemoryAllocator Native = new();
        private readonly PooledByteBufAllocator _pool;
        public readonly ByteBuf Root;
        public Storage(int provider, int length, int maximum)
        {
            if (provider == 2 && maximum != int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(maximum));
            if (provider >= 3)
                _pool = new PooledByteBufAllocator(nativeAllocator: Native, maxOrder: 1,
                    smallCacheSize: 0, normalCacheSize: 0);
            Root = provider switch
            {
                0 => new UnpooledHeapByteBuf(length, maximum),
                1 => new UnpooledDirectByteBuf(length, maximum, Native),
                2 => new CompositeByteBuf(16, false, Native)
                    .AddComponent(new UnpooledHeapByteBuf(length / 2).SetIndex(0, length / 2), true)
                    .AddComponent(new UnpooledDirectByteBuf(length - length / 2, int.MaxValue, Native)
                        .SetIndex(0, length - length / 2), true),
                3 => new PooledHeapByteBuf((PoolHeapArena)_pool.HeapArenas[0], length, maximum),
                _ => new PooledDirectByteBuf((PoolDirectArena)_pool.DirectArenas[0], length, maximum)
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

    // Supplemental state/ownership checks; named original bodies use the actual inherited roots.
    public enum CapacityCase
    {
        AboveMaximum, NegativeCapacity, Decrease, Increase, Slice, RetainedSlice,
        DefaultFastWritable, FastWritableAfterWrite, ReaderPastSavedWriter
    }

    public static IEnumerable<object[]> SupplementalCases()
    {
        for (int provider = 0; provider < 5; ++provider)
        foreach (CapacityCase scenario in Enum.GetValues<CapacityCase>())
        {
            // The original composite fixture requires an unlimited maximum.
            if (provider == 2 && (scenario is CapacityCase.AboveMaximum or CapacityCase.NegativeCapacity or CapacityCase.Decrease or CapacityCase.Increase || scenario is CapacityCase.DefaultFastWritable or CapacityCase.FastWritableAfterWrite)) continue;
            // The pooled fixture overrides the default fast-writable test.
            if (provider >= 3 && scenario == CapacityCase.DefaultFastWritable) continue;
            yield return [provider, scenario];
        }
    }

    [Theory]
    [MemberData(nameof(SupplementalCases))]
    public void CapacityAndFastWritablePreserveStateAndOwnership(int provider, CapacityCase scenario)
    {
        (int initial, int maximum) = scenario switch
        {
            CapacityCase.AboveMaximum or CapacityCase.NegativeCapacity or CapacityCase.Decrease or CapacityCase.Increase => (3, 13), CapacityCase.DefaultFastWritable => (150, 500), CapacityCase.FastWritableAfterWrite => (4, 10), CapacityCase.ReaderPastSavedWriter => (10, int.MaxValue), _ => (8, int.MaxValue)
        };
        using var s = new Storage(provider, initial, maximum);
        ByteBuf buffer = s.Root;
        if (scenario is CapacityCase.AboveMaximum or CapacityCase.NegativeCapacity or CapacityCase.Decrease or CapacityCase.Increase)
        {
            Assert.Equal(13, buffer.MaxCapacity); Assert.Equal(3, buffer.Capacity);
            switch (scenario)
            {
                case CapacityCase.AboveMaximum: Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Capacity = 14); break;
                case CapacityCase.NegativeCapacity: Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Capacity = -1); break;
                case CapacityCase.Decrease: buffer.Capacity = 2; break;
                default: buffer.Capacity = 4; break;
            }
            Assert.Equal(scenario is CapacityCase.AboveMaximum or CapacityCase.NegativeCapacity ? 3 : scenario == CapacityCase.Decrease ? 2 : 4, buffer.Capacity);
            Assert.Equal(13, buffer.MaxCapacity);
            Assert.Equal(0, buffer.ReaderIndex); Assert.Equal(0, buffer.WriterIndex);
        }
        else if (scenario is CapacityCase.Slice or CapacityCase.RetainedSlice)
        {
            bool retained = scenario == CapacityCase.RetainedSlice;
            ByteBuf slice = retained ? buffer.RetainedSlice(buffer.ReaderIndex + 1, 3)
                : buffer.Slice(buffer.ReaderIndex + 1, 3);
            int rootCount = buffer.ReferenceCount, sliceCount = slice.ReferenceCount;
            try
            {
                Assert.Throws<NotSupportedException>(() => slice.Capacity = 10);
                Assert.Equal(3, slice.Capacity); Assert.Equal(3, slice.MaxCapacity);
                Assert.Equal(8, buffer.Capacity);
                Assert.Equal(rootCount, buffer.ReferenceCount); Assert.Equal(sliceCount, slice.ReferenceCount);
            }
            finally { if (retained) slice.Release(); }
        }
        else if (scenario == CapacityCase.DefaultFastWritable)
        {
            buffer.WriterIndex = 100;
            Assert.Equal(50, buffer.WritableBytes); Assert.Equal(150, buffer.Capacity);
            Assert.Equal(500, buffer.MaxCapacity); Assert.Equal(400, buffer.MaxWritableBytes);
            // Default implementation has fast writable == writable
            Assert.Equal(50, buffer.MaxFastWritableBytes);
        }
        else if (scenario == CapacityCase.FastWritableAfterWrite)
        {
            int max = buffer.MaxFastWritableBytes;
            buffer.WriteByte(1);
            Assert.Equal(max - 1, buffer.MaxFastWritableBytes);
            Assert.Equal(4, buffer.Capacity); Assert.Equal(10, buffer.MaxCapacity);
        }
        else
        {
            string content1 = "hello", content2 = "world";
            buffer.SetIndex(0, 0);
            buffer.WriteString(content1, Encoding.ASCII);
            buffer.MarkWriterIndex();
            buffer.SkipBytes(content1.Length);
            buffer.WriteString(content2, Encoding.ASCII);
            buffer.SkipBytes(content2.Length);
            Assert.True(buffer.ReaderIndex <= buffer.WriterIndex);
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.ResetWriterIndex());
            Assert.Equal(10, buffer.ReaderIndex); Assert.Equal(10, buffer.WriterIndex);
            Assert.Equal(Encoding.ASCII.GetBytes(content1 + content2), ByteBufUtil.GetBytes(buffer, 0, 10));
        }
        Assert.Equal(1, buffer.ReferenceCount);
    }
}
