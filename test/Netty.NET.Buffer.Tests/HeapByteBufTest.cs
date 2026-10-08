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
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

/**
 * An abstract test class for channel buffers
 */
// CLR: selected original AbstractByteBufTest scenarios run against the native heap buffer.
// Other original scenarios are inventoried as pending, not skipped or represented by stubs.
public class HeapByteBufTest
{
    private const int Capacity = 4096; // Must be even

    protected virtual ByteBuf NewBuffer(int initialCapacity, int maxCapacity = int.MaxValue)
        => Unpooled.Buffer(initialCapacity, maxCapacity);
    protected virtual bool UsesDirectMemory => false;
    private ByteBuf NewCopiedBuffer(ReadOnlySpan<byte> bytes)
        => NewBuffer(bytes.Length).WriteBytes(bytes);

    [Fact]
    public void InitialState()
    {
        ByteBuf buffer = NewBuffer(Capacity);
        try
        {
            Assert.Equal(Capacity, buffer.Capacity);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(0, buffer.WriterIndex);
            Assert.Equal(0, buffer.ReadableBytes);
            Assert.Equal(Capacity, buffer.WritableBytes);
            Assert.False(buffer.IsReadable);
            Assert.True(buffer.IsWritable);
            Assert.Equal(1, buffer.ReferenceCount);
            Assert.Equal(UsesDirectMemory, buffer.IsDirect);
        }
        finally { Assert.True(buffer.Release()); }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 4097)]
    [InlineData(2, 1)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void IndexBoundaryChecksAreAtomic(int reader, int writer)
    {
        ByteBuf buffer = NewBuffer(Capacity);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.SetIndex(reader, writer));
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(0, buffer.WriterIndex);
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void ReaderAndWriterIndexBoundaryChecks()
    {
        ByteBuf buffer = NewBuffer(8);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.ReaderIndex = -1);
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.ReaderIndex = 1);
            buffer.WriterIndex = 8;
            buffer.ReaderIndex = 4;
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.WriterIndex = 3);
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.WriterIndex = 9);
            Assert.Equal(4, buffer.ReaderIndex);
            Assert.Equal(8, buffer.WriterIndex);
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void CapacityDecreaseAndIncreasePreserveDataAndTrimIndices()
    {
        ByteBuf buffer = NewBuffer(10, 13);
        try
        {
            for (int i = 0; i < 10; ++i) buffer.WriteByte(i);
            buffer.ReaderIndex = 7;
            buffer.Capacity = 5;
            Assert.Equal(5, buffer.ReaderIndex);
            Assert.Equal(5, buffer.WriterIndex);
            Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, buffer.AsMemory(0, 5).ToArray());
            buffer.Capacity = 13;
            Assert.Equal(13, buffer.MaxCapacity);
            Assert.Equal(5, buffer.WriterIndex);
            Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, buffer.AsMemory(0, 5).ToArray());
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Capacity = 14);
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Capacity = -1);
            Assert.Equal(13, buffer.Capacity);
            buffer.Capacity = 0;
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(0, buffer.WriterIndex);
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 64, 2)]
    [InlineData(65, 128, 2)]
    [InlineData(129, 130, 2)]
    public void EnsureWritableKeepsTheOriginalGrowthPolicy(int requested, int capacity, int status)
    {
        ByteBuf buffer = NewBuffer(0, 130);
        try
        {
            Assert.Equal(status, buffer.EnsureWritable(requested, false));
            Assert.Equal(capacity, buffer.Capacity);
            Assert.Equal(0, buffer.WriterIndex);
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void EnsureWritableForceStatusAndIntegerOverflow()
    {
        ByteBuf buffer = NewBuffer(8, 13);
        try
        {
            buffer.WriterIndex = 8;
            Assert.Equal(1, buffer.EnsureWritable(6, false));
            Assert.Equal(8, buffer.Capacity);
            Assert.Equal(3, buffer.EnsureWritable(6, true));
            Assert.Equal(13, buffer.Capacity);
            Assert.Equal(1, buffer.EnsureWritable(6, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.EnsureWritable(int.MaxValue));
            Assert.Equal(8, buffer.WriterIndex);
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void TestDiscardReadBytes()
    {
        ByteBuf buffer = NewBuffer(Capacity);
        ByteBuf copy = null;
        try
        {
            for (int i = 0; i < Capacity; i += 4) buffer.WriteInt(i);
            copy = buffer.Copy();
            // Make sure there's no effect if called when readerIndex is 0.
            buffer.ReaderIndex = Capacity / 4;
            buffer.MarkReaderIndex();
            buffer.WriterIndex = Capacity / 3;
            buffer.MarkWriterIndex();
            buffer.SetIndex(0, Capacity / 2);
            buffer.DiscardReadBytes();
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(Capacity / 2, buffer.WriterIndex);
            Assert.Equal(copy.AsMemory(0, Capacity / 2).ToArray(), buffer.AsMemory(0, Capacity / 2).ToArray());
            buffer.ResetReaderIndex();
            Assert.Equal(Capacity / 4, buffer.ReaderIndex);
            buffer.ResetWriterIndex();
            Assert.Equal(Capacity / 3, buffer.WriterIndex);

            // Make sure bytes after writerIndex is not copied.
            buffer.SetIndex(Capacity / 4, Capacity / 2);
            byte[] writable = buffer.AsMemory(Capacity / 2, Capacity / 2).ToArray();
            buffer.DiscardReadBytes();
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(Capacity / 4, buffer.WriterIndex);
            Assert.Equal(copy.AsMemory(Capacity / 4, Capacity / 4).ToArray(), buffer.ReadableMemory.ToArray());
            Assert.Equal(writable, buffer.AsMemory(Capacity / 2, Capacity / 2).ToArray());
            buffer.ResetReaderIndex();
            Assert.Equal(0, buffer.ReaderIndex);
            buffer.ResetWriterIndex();
            Assert.Equal(Capacity / 3 - Capacity / 4, buffer.WriterIndex);
        }
        finally { copy?.Release(); buffer.Release(); }
    }

    [Fact]
    public void DiscardSomeReadBytesUsesHalfCapacityThreshold()
    {
        ByteBuf buffer = NewCopiedBuffer(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 });
        try
        {
            buffer.ReaderIndex = 3;
            buffer.DiscardSomeReadBytes();
            Assert.Equal(3, buffer.ReaderIndex);
            buffer.ReaderIndex = 4;
            buffer.DiscardSomeReadBytes();
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(new byte[] { 4, 5, 6, 7 }, buffer.ReadableMemory.ToArray());
            buffer.SkipBytes(4).DiscardSomeReadBytes();
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(0, buffer.WriterIndex);
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void ClearResetsIndicesWithoutClearingBytesOrMarks()
    {
        ByteBuf buffer = NewCopiedBuffer(new byte[] { 1, 2, 3, 4 });
        try
        {
            buffer.ReaderIndex = 2;
            buffer.MarkReaderIndex().MarkWriterIndex().Clear();
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(0, buffer.WriterIndex);
            Assert.Equal(3, buffer.GetByte(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.ResetReaderIndex());
            buffer.ResetWriterIndex().ResetReaderIndex();
            Assert.Equal(2, buffer.ReaderIndex);
            Assert.Equal(4, buffer.WriterIndex);
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void CopyIsIndependentWhileDuplicateSharesDataAndSeparateIndices()
    {
        ByteBuf buffer = NewCopiedBuffer(new byte[] { 1, 2, 3, 4, 5, 6 });
        ByteBuf copy = null;
        try
        {
            buffer.SetIndex(1, 5);
            copy = buffer.Copy();
            ByteBuf duplicate = buffer.Duplicate();
            Assert.Equal(new byte[] { 2, 3, 4, 5 }, copy.ReadableMemory.ToArray());
            Assert.Equal(buffer.ReadableMemory.ToArray(), duplicate.ReadableMemory.ToArray());
            Assert.Equal(1, buffer.ReferenceCount);
            duplicate.ReadByte();
            Assert.Equal(1, buffer.ReaderIndex);
            duplicate.SetByte(2, 99);
            Assert.Equal(99, buffer.GetByte(2));
            Assert.Equal(3, copy.GetByte(1));
            duplicate.ResetReaderIndex();
            Assert.Equal(1, duplicate.ReaderIndex);
        }
        finally { copy?.Release(); buffer.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TestDuplicateOfSliceHasTheSameCapacityAsTheSlice(bool retain)
    {
        ByteBuf buffer = NewBuffer(Capacity);
        try
        {
            foreach (ByteBuf slice in new[] { buffer.Slice(), buffer.Slice(0, Capacity - 2) })
            {
                ByteBuf duplicate = retain ? slice.RetainedDuplicate() : slice.Duplicate();
                Assert.Equal(slice.Capacity, duplicate.Capacity);
                if (retain) Assert.False(duplicate.Release());
            }
            Assert.Equal(1, buffer.ReferenceCount);
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void TestRetainedSliceOfNonRetainedDerivedBufferCoversTheReadableBytes()
    {
        ByteBuf buffer = NewBuffer(Capacity);
        try
        {
            for (int i = 0; i < Capacity; ++i) buffer.SetByte(i, i);
            // a writer index below the capacity, and a reader index at zero or above
            for (int readerIndex = 0; readerIndex <= 1; ++readerIndex)
            {
                buffer.SetIndex(readerIndex, Capacity - 2);
                ByteBuf retainedDuplicate = buffer.RetainedDuplicate();
                ByteBuf retainedSlice = buffer.RetainedSlice();
                try
                {
                    foreach (ByteBuf derived in new[] { retainedDuplicate, retainedSlice })
                        foreach (ByteBuf view in new[] { derived.Duplicate(), derived.Slice(), derived.Duplicate().Duplicate(), derived.Slice().Slice() })
                        {
                            AssertRetainedReadableSlice(view);
                            view.ReadByte();
                            AssertRetainedReadableSlice(view);
                        }
                }
                finally { retainedDuplicate.Release(); retainedSlice.Release(); }
                Assert.Equal(1, buffer.ReferenceCount);
            }
        }
        finally { buffer.Release(); }
    }

    private static void AssertRetainedReadableSlice(ByteBuf buffer)
    {
        ByteBuf slice = buffer.RetainedSlice();
        try
        {
            Assert.Equal(buffer.ReadableBytes, slice.ReadableBytes);
            Assert.Equal(buffer.ReadableMemory.ToArray(), slice.ReadableMemory.ToArray());
        }
        finally { slice.Release(); }
    }

    [Fact]
    public void RetainedViewsOwnLifetimeButOrdinaryViewsDoNot()
    {
        ByteBuf buffer = NewCopiedBuffer(new byte[] { 1, 2, 3 });
        ByteBuf ordinary = buffer.Slice(1, 2);
        ByteBuf retained = buffer.RetainedSlice(1, 2);
        Assert.Equal(2, buffer.ReferenceCount);
        Assert.False(buffer.Release());
        Assert.Equal(2, retained.ReadByte());
        Assert.True(retained.Release());
        Assert.Throws<IllegalReferenceCountException>(() => ordinary.GetByte(0));
        Assert.Throws<IllegalReferenceCountException>(() => retained.GetByte(0));
    }

    [Fact]
    public void ReadSlicesAdvanceOnlyTheSourceReaderAndRespectReadableBytes()
    {
        ByteBuf buffer = NewCopiedBuffer(new byte[] { 1, 2, 3, 4 });
        try
        {
            ByteBuf slice = buffer.ReadSlice(2);
            Assert.Equal(2, buffer.ReaderIndex);
            Assert.Equal(new byte[] { 1, 2 }, slice.ReadableMemory.ToArray());
            ByteBuf retained = buffer.ReadRetainedSlice(2);
            try
            {
                Assert.Equal(4, buffer.ReaderIndex);
                Assert.Equal(2, buffer.ReferenceCount);
                Assert.Throws<ArgumentOutOfRangeException>(() => buffer.ReadSlice(1));
                Assert.Equal(4, buffer.ReaderIndex);
            }
            finally { retained.Release(); }
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void ParentResizeIsVisibleToDuplicateAndValidSlicePrefixes()
    {
        ByteBuf buffer = NewCopiedBuffer(new byte[] { 1, 2, 3, 4, 5, 6 });
        try
        {
            ByteBuf duplicate = buffer.Duplicate();
            ByteBuf slice = buffer.Slice(2, 4);
            duplicate.Capacity = 4;
            Assert.Equal(4, buffer.Capacity);
            Assert.Equal(4, duplicate.Capacity);
            Assert.Equal(4, slice.Capacity);
            Assert.Equal(3, slice.GetByte(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => slice.GetInt(0));
            Assert.Throws<NotSupportedException>(() => slice.Capacity = 4);
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void WrappedArrayAliasesAndCopiedArrayDoesNot()
    {
        byte[] input = { 1, 2, 3 };
        ByteBuf wrapped = Unpooled.WrappedBuffer(input);
        ByteBuf copied = Unpooled.CopiedBuffer(input);
        try
        {
            input[1] = 9;
            Assert.Equal(9, wrapped.GetByte(1));
            Assert.Equal(2, copied.GetByte(1));
            wrapped.SetByte(2, 8);
            Assert.Equal(8, input[2]);
            Assert.Equal(3, wrapped.WriterIndex);
            Assert.Equal(3, wrapped.MaxCapacity);
            Assert.Throws<ArgumentNullException>(() => Unpooled.WrappedBuffer(null));
        }
        finally { wrapped.Release(); copied.Release(); }
    }
}
