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

public class ByteBufViewFactorySourceTest
{
    private const int Capacity = 4096; // Must be even
    private const int BlockSize = 128;

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
    public static IEnumerable<object[]> RetainedProviders()
    {
        for (int provider = 0; provider < 5; ++provider)
        foreach (bool retained in new[] { false, true }) yield return [provider, retained];
    }
    public static IEnumerable<object[]> NestedBoundsCases()
    {
        for (int provider = 0; provider < 5; ++provider)
        foreach (bool initialRetained in new[] { false, true })
        foreach (bool finalRetained in new[] { false, true })
        foreach (bool invalidIndex in new[] { false, true })
            yield return [provider, initialRetained, finalRetained, invalidIndex];
    }

    // The eight declared testSlice*/testMixedSlice*/testRetainedSlice* bounds
    // tests all call AbstractByteBufTest.testSliceOutOfBounds with these flags.
    [Theory]
    [MemberData(nameof(NestedBoundsCases))]
    public void NestedSliceBoundsDoNotEscapeTheOriginalWindow(int provider, bool initialRetained, bool finalRetained, bool invalidIndex)
    {
        using var s = new Storage(provider, 8);
        ByteBuf slice = initialRetained ? s.Root.RetainedSlice(s.Root.ReaderIndex + 1, 2)
            : s.Root.Slice(s.Root.ReaderIndex + 1, 2);
        ByteBuf unexpected = null;
        try
        {
            Assert.Equal(2, slice.Capacity); Assert.Equal(2, slice.MaxCapacity);
            int index = invalidIndex ? 3 : 0, length = invalidIndex ? 0 : 3;
            int rootCount = s.Root.ReferenceCount, sliceCount = slice.ReferenceCount;
            long reserved = s.Native.ReservedBytes;
            slice.MarkReaderIndex().MarkWriterIndex();
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                // This is expected to fail ... so no need to release.
                // CLR: keep a cleanup path if a regression unexpectedly returns an owned child.
                unexpected = finalRetained ? slice.RetainedSlice(index, length) : slice.Slice(index, length);
            });
            Assert.Equal(rootCount, s.Root.ReferenceCount); Assert.Equal(sliceCount, slice.ReferenceCount);
            Assert.Equal(reserved, s.Native.ReservedBytes);
            Assert.Equal(0, s.Root.ReaderIndex); Assert.Equal(0, s.Root.WriterIndex);
            Assert.Equal(0, slice.ReaderIndex); Assert.Equal(2, slice.WriterIndex);
            slice.SetIndex(1, 1).ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(0, slice.ReaderIndex); Assert.Equal(2, slice.WriterIndex);
        }
        finally
        {
            if (finalRetained && unexpected != null) unexpected.Release();
            if (initialRetained) slice.Release();
        }
        Assert.Equal(1, s.Root.ReferenceCount);
    }

    [Theory]
    [MemberData(nameof(RetainedProviders))]
    public void ReadSliceRejectsBytesPastWriterWithoutMovingReaderOrRetaining(int provider, bool retained)
    {
        using var s = new Storage(provider, 100);
        s.Root.WriteZero(50).MarkReaderIndex().MarkWriterIndex();
        Assert.Throws<ArgumentOutOfRangeException>(() => retained ? s.Root.ReadRetainedSlice(51) : s.Root.ReadSlice(51));
        Assert.Equal(0, s.Root.ReaderIndex); Assert.Equal(50, s.Root.WriterIndex);
        Assert.Equal(1, s.Root.ReferenceCount);
        s.Root.SetIndex(1, 51).ResetReaderIndex().ResetWriterIndex();
        Assert.Equal(0, s.Root.ReaderIndex); Assert.Equal(50, s.Root.WriterIndex);
        Assert.Equal(new byte[50], ByteBufUtil.GetBytes(s.Root));
    }

    public static IEnumerable<object[]> ReleasedCases()
    {
        for (int provider = 0; provider < 5; ++provider)
        for (int factory = 0; factory < 6; ++factory) yield return [provider, factory];
    }
    [Theory]
    [MemberData(nameof(ReleasedCases))]
    public void ViewFactoriesRejectReleasedOwners(int provider, int factory)
    {
        using var s = new Storage(provider, 8);
        // Clear the buffer so we are sure the reader and writer indices are 0.
        // This is important as we may return a slice from newBuffer(...).
        s.Root.Clear(); Assert.True(s.Root.Release());
        Assert.Throws<IllegalReferenceCountException>(() => factory switch
        {
            0 => s.Root.Slice(),
            1 => s.Root.Slice(0, 1),
            2 => s.Root.RetainedSlice(),
            3 => s.Root.RetainedSlice(0, 1),
            4 => s.Root.Duplicate(),
            _ => s.Root.RetainedDuplicate()
        });
        Assert.Equal(0, s.Root.ReferenceCount);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void SliceReleaseReleasesItsSharedOriginalClaim(int provider)
    {
        using var s = new Storage(provider, 8);
        Assert.Equal(1, s.Root.ReferenceCount);
        Assert.True(s.Root.Slice().Release());
        Assert.Equal(0, s.Root.ReferenceCount);
    }

    [Theory]
    [MemberData(nameof(RetainedProviders))]
    public void SliceFactoriesStartWithZeroReaderAndTheExactWindowWriter(int provider, bool retained)
    {
        using var s = new Storage(provider, Capacity);
        (int Index, int Length)[] ranges = [(0, Capacity), (0, Capacity - 1), (1, Capacity - 1), (1, Capacity - 2)];
        for (int pass = 0; pass < 2; ++pass)
        foreach (var range in ranges)
        {
            ByteBuf slice = retained ? s.Root.RetainedSlice(range.Index, range.Length) : s.Root.Slice(range.Index, range.Length);
            try
            {
                if (pass == 0) Assert.Equal(0, slice.ReaderIndex);
                else Assert.Equal(range.Length, slice.WriterIndex);
            }
            finally { if (retained) slice.Release(); }
        }
        Assert.Equal(0, s.Root.ReaderIndex); Assert.Equal(0, s.Root.WriterIndex);
        Assert.Equal(1, s.Root.ReferenceCount);
    }

    [Theory]
    [MemberData(nameof(RetainedProviders))]
    public void DuplicatesOfBorrowedAndRetainedSlicesKeepSliceCapacity(int provider, bool retained)
    {
        using var s = new Storage(provider, Capacity);
        foreach (bool retainedDuplicate in new[] { false, true })
        foreach (bool indexed in new[] { false, true })
        {
            ByteBuf slice = retained
                ? indexed ? s.Root.RetainedSlice(0, Capacity - 2) : s.Root.RetainedSlice()
                : indexed ? s.Root.Slice(0, Capacity - 2) : s.Root.Slice();
            ByteBuf duplicate = null;
            try
            {
                duplicate = retainedDuplicate ? slice.RetainedDuplicate() : slice.Duplicate();
                Assert.Equal(slice.Capacity, duplicate.Capacity);
                // Preserve the original release order, including an independent pooled duplicate.
                if (retained) { slice.Release(); slice = null; }
            }
            finally
            {
                if (retained && slice != null) slice.Release();
                if (retainedDuplicate) duplicate?.Release();
            }
        }
        Assert.Equal(1, s.Root.ReferenceCount);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void RetainedSlicesOfBorrowedDerivedViewsCoverTheirReadableBytesBeforeAndAfterRead(int provider)
    {
        using var s = new Storage(provider, Capacity);
        for (int i = 0; i < Capacity; ++i) s.Root.SetByte(i, i);
        // a writer index below the capacity, and a reader index at zero or above
        for (int reader = 0; reader <= 1; ++reader)
        {
            s.Root.SetIndex(reader, Capacity - 2);
            ByteBuf duplicate = s.Root.RetainedDuplicate(), slice = s.Root.RetainedSlice();
            try
            {
                foreach (ByteBuf derived in new[] { duplicate, slice })
                {
                    CheckReadableRetainedSlice(derived.Duplicate(), reader);
                    CheckReadableRetainedSlice(derived.Slice(), reader);
                    CheckReadableRetainedSlice(derived.Duplicate().Duplicate(), reader);
                    CheckReadableRetainedSlice(derived.Slice().Slice(), reader);
                }
            }
            finally { duplicate.Release(); slice.Release(); }
            Assert.Equal(1, s.Root.ReferenceCount);
        }
    }

    private static void CheckReadableRetainedSlice(ByteBuf buffer, int originalStart)
    {
        for (int phase = 0; phase < 2; ++phase)
        {
            ByteBuf retained = buffer.RetainedSlice();
            try
            {
                Assert.Equal(buffer.ReadableBytes, retained.ReadableBytes);
                Assert.True(buffer.Slice().Equals(retained));
                byte[] expected = new byte[buffer.ReadableBytes];
                for (int i = 0; i < expected.Length; ++i) expected[i] = unchecked((byte)(originalStart + phase + i));
                Assert.Equal(expected, ByteBufUtil.GetBytes(retained));
            }
            finally { retained.Release(); }
            if (phase == 0) Assert.Equal(unchecked((byte)originalStart), buffer.ReadByte());
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void SequentialReadSlicesShareStorageAndAdvanceByOriginalBlockSize(int provider)
    {
        using var s = new Storage(provider, Capacity);
        var random = new Random(12345);
        s.Root.WriterIndex = 0;
        for (int i = 0; i < Capacity - BlockSize + 1; i += BlockSize)
        {
            byte[] value = new byte[BlockSize]; random.NextBytes(value);
            Assert.Equal(0, s.Root.ReaderIndex); Assert.Equal(i, s.Root.WriterIndex);
            s.Root.WriteBytes(value);
        }
        random = new Random(12345);
        byte[] expected = new byte[BlockSize];
        for (int i = 0; i < Capacity - BlockSize + 1; i += BlockSize)
        {
            random.NextBytes(expected);
            Assert.Equal(i, s.Root.ReaderIndex); Assert.Equal(Capacity, s.Root.WriterIndex);
            ByteBuf actual = s.Root.ReadSlice(BlockSize);
            Assert.Equal(expected, ByteBufUtil.GetBytes(actual));
            // CLR has explicit BE/LE operations instead of a mutable byte-order facade.
            Assert.Equal(s.Root.GetInt(i), actual.GetInt(0));
            Assert.Equal(s.Root.GetIntLE(i), actual.GetIntLE(0));
            // Make sure if it is a sliced buffer.
            actual.SetByte(0, unchecked((byte)(actual.GetByte(0) + 1)));
            Assert.Equal(s.Root.GetByte(i), actual.GetByte(0));
            Assert.Equal(1, s.Root.ReferenceCount);
        }
        Assert.Equal(Capacity, s.Root.ReaderIndex);
    }
}
