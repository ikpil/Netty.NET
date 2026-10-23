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

public class ByteBufIndexStateSourceTest
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
        for (int scenario = 0; scenario < 12; ++scenario) yield return [provider, scenario];
    }

    [Theory]
    [MemberData(nameof(OriginalCases))]
    public void OriginalIndexBoundariesAndSkipPreserveValidState(int provider, int scenario)
    {
        const int capacity = 4096;
        using var s = new Storage(provider, capacity);
        switch (scenario)
        {
            case 0:
                s.Root.WriterIndex = 0;
                Assert.Throws<ArgumentOutOfRangeException>(() => s.Root.ReaderIndex = -1);
                Assert.Equal(0, s.Root.ReaderIndex); Assert.Equal(0, s.Root.WriterIndex);
                break;
            case 1:
                s.Root.WriterIndex = capacity;
                Assert.Throws<ArgumentOutOfRangeException>(() => s.Root.ReaderIndex = capacity + 1);
                Assert.Equal(0, s.Root.ReaderIndex); Assert.Equal(capacity, s.Root.WriterIndex);
                break;
            case 2:
                s.Root.WriterIndex = capacity / 2;
                Assert.Throws<ArgumentOutOfRangeException>(() => s.Root.ReaderIndex = capacity * 3 / 2);
                Assert.Equal(0, s.Root.ReaderIndex); Assert.Equal(capacity / 2, s.Root.WriterIndex);
                break;
            case 3:
                s.Root.WriterIndex = 0; s.Root.ReaderIndex = 0;
                s.Root.WriterIndex = capacity; s.Root.ReaderIndex = capacity;
                Assert.Equal(capacity, s.Root.ReaderIndex); Assert.Equal(capacity, s.Root.WriterIndex);
                break;
            case 4:
                Assert.Throws<ArgumentOutOfRangeException>(() => s.Root.WriterIndex = -1);
                Assert.Equal(0, s.Root.ReaderIndex); Assert.Equal(0, s.Root.WriterIndex);
                break;
            case 5:
                s.Root.WriterIndex = capacity; s.Root.ReaderIndex = capacity;
                Assert.Throws<ArgumentOutOfRangeException>(() => s.Root.WriterIndex = capacity + 1);
                Assert.Equal(capacity, s.Root.ReaderIndex); Assert.Equal(capacity, s.Root.WriterIndex);
                break;
            case 6:
                s.Root.WriterIndex = capacity; s.Root.ReaderIndex = capacity / 2;
                Assert.Throws<ArgumentOutOfRangeException>(() => s.Root.WriterIndex = capacity / 4);
                Assert.Equal(capacity / 2, s.Root.ReaderIndex); Assert.Equal(capacity, s.Root.WriterIndex);
                break;
            case 7:
                s.Root.WriterIndex = 0; s.Root.ReaderIndex = 0; s.Root.WriterIndex = capacity;
                // CLR span replaces the original empty ByteBuffer without a separate position/limit.
                s.Root.WriteBytes(ReadOnlySpan<byte>.Empty);
                Assert.Equal(0, s.Root.ReaderIndex); Assert.Equal(capacity, s.Root.WriterIndex);
                break;
            case 8:
                Assert.Throws<ArgumentOutOfRangeException>(() => s.Root.SetIndex(-1, capacity));
                Assert.Equal(0, s.Root.ReaderIndex); Assert.Equal(0, s.Root.WriterIndex);
                break;
            case 9:
                Assert.Throws<ArgumentOutOfRangeException>(() => s.Root.SetIndex(capacity / 2, capacity / 4));
                Assert.Equal(0, s.Root.ReaderIndex); Assert.Equal(0, s.Root.WriterIndex);
                break;
            case 10:
                Assert.Throws<ArgumentOutOfRangeException>(() => s.Root.SetIndex(0, capacity + 1));
                Assert.Equal(0, s.Root.ReaderIndex); Assert.Equal(0, s.Root.WriterIndex);
                break;
            default:
                s.Root.SetIndex(capacity / 4, capacity / 2);
                Assert.Same(s.Root, s.Root.SkipBytes(capacity / 4));
                Assert.Equal(capacity / 4 * 2, s.Root.ReaderIndex);
                Assert.Throws<ArgumentOutOfRangeException>(() => s.Root.SkipBytes(capacity / 4 + 1));
                // Should remain unchanged.
                Assert.Equal(capacity / 4 * 2, s.Root.ReaderIndex);
                Assert.Equal(capacity / 2, s.Root.WriterIndex);
                break;
        }
        Assert.Equal(1, s.Root.ReferenceCount);
    }

    public static IEnumerable<object[]> MarkCases()
    {
        for (int provider = 0; provider < 5; ++provider)
        for (int view = 0; view < 6; ++view) yield return [provider, view];
    }

    [Theory]
    [MemberData(nameof(MarkCases))]
    public void ClearKeepsSavedMarksAndResetFailuresDoNotChangeIndicesOrOwnership(int provider, int shape)
    {
        using var s = new Storage(provider, 16);
        byte[] bytes = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];
        s.Root.SetBytes(0, bytes).SetIndex(3, 13);
        ByteBuf view = shape switch
        {
            1 => s.Root.Duplicate(), 2 => s.Root.Slice(2, 8), 3 => s.Root.AsReadOnly(),
            4 => s.Root.RetainedDuplicate(), 5 => s.Root.RetainedSlice(2, 8), _ => s.Root
        };
        int count = s.Root.ReferenceCount, viewCount = view.ReferenceCount;
        try
        {
            Assert.Same(view, view.SetIndex(2, 6));
            Assert.Same(view, view.MarkReaderIndex()); Assert.Same(view, view.MarkWriterIndex());
            view.SetIndex(4, 5);
            Assert.Same(view, view.Clear()); Assert.Equal(0, view.ReaderIndex); Assert.Equal(0, view.WriterIndex);
            Assert.Throws<ArgumentOutOfRangeException>(() => view.ResetReaderIndex());
            Assert.Equal(0, view.ReaderIndex); Assert.Equal(0, view.WriterIndex);
            Assert.Same(view, view.ResetWriterIndex()); Assert.Same(view, view.ResetReaderIndex());
            Assert.Equal(2, view.ReaderIndex); Assert.Equal(6, view.WriterIndex);
            view.SetIndex(0, 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => view.ResetReaderIndex());
            Assert.Equal(0, view.ReaderIndex); Assert.Equal(1, view.WriterIndex);
            view.SetIndex(7, 7);
            Assert.Throws<ArgumentOutOfRangeException>(() => view.ResetWriterIndex());
            Assert.Equal(7, view.ReaderIndex); Assert.Equal(7, view.WriterIndex);
            view.ResetReaderIndex().ResetWriterIndex();
            Assert.Same(view, view.SkipBytes(3)); Assert.Equal(5, view.ReaderIndex); Assert.Equal(6, view.WriterIndex);
            view.ResetReaderIndex();
            Assert.Throws<ArgumentOutOfRangeException>(() => view.SkipBytes(-1));
            Assert.Equal(2, view.ReaderIndex); Assert.Equal(6, view.WriterIndex);
            Assert.Equal(count, s.Root.ReferenceCount); Assert.Equal(viewCount, view.ReferenceCount);
            Assert.Equal(bytes, ByteBufUtil.GetBytes(s.Root, 0, 16));
            if (shape != 0) { Assert.Equal(3, s.Root.ReaderIndex); Assert.Equal(13, s.Root.WriterIndex); }
        }
        finally { if (shape is 4 or 5) view.Release(); }
        Assert.Equal(1, s.Root.ReferenceCount);
    }
}
