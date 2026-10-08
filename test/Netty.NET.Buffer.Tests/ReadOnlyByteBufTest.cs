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
using System.Buffers;
using System.Text;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

/**
 * Tests read-only channel buffers
 */
public class ReadOnlyByteBufTest
{
    private static ByteBuf NewBuffer(bool direct, int capacity = 16, int maximum = 128)
        => direct ? new UnpooledDirectByteBuf(capacity, maximum, new NativeMemoryAllocator())
            : Unpooled.Buffer(capacity, maximum);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadOnlyViewSharesContentAndOwnershipWithIndependentIndicesAndMarks(bool direct)
    {
        ByteBuf parent = NewBuffer(direct);
        try
        {
            parent.WriteBytes(new byte[] { 1, 2, 3, 4 }).SetIndex(1, 4).MarkReaderIndex().MarkWriterIndex();
            ByteBuf view = parent.AsReadOnly();
            Assert.True(view.IsReadOnly);
            Assert.False(parent.IsReadOnly);
            Assert.Equal(direct, view.IsDirect);
            Assert.Same(parent, view.Unwrap());
            Assert.Same(view, view.AsReadOnly());
            Assert.Equal(1, view.ReferenceCount);
            Assert.Equal(1, view.ReaderIndex);
            Assert.Equal(4, view.WriterIndex);
            Assert.False(view.IsWritable);
            Assert.False(view.CanWrite(0));
            Assert.False(view.CanWrite(1));
            Assert.True(parent.CanWrite(1));
            Assert.Equal(12, view.WritableBytes); // Byte-count metadata does not confer write permission.
            view.ResetReaderIndex();
            Assert.Equal(0, view.ReaderIndex);
            Assert.Equal(1, parent.ReaderIndex);
            view.ResetWriterIndex();
            Assert.Equal(0, view.WriterIndex);
            Assert.Equal(4, parent.WriterIndex);
            view.SetIndex(1, 4);
            ReadOnlyMemory<byte> borrowed = view.ReadableMemory;
            parent.SetByte(2, 99);
            Assert.Equal(new byte[] { 2, 99, 4 }, borrowed.ToArray());
            Assert.Equal(2, view.ReadByte());
            Assert.Equal(2, view.ReaderIndex);
            Assert.Equal(1, parent.ReaderIndex);
            view.Clear();
            Assert.Equal(99, view.GetByte(2));
            Assert.Equal(4, parent.WriterIndex);
        }
        finally { parent.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryImplementedMutationAndMutableBorrowRejectsWithoutChangingDataOrIndices(bool direct)
    {
        ByteBuf parent = NewBuffer(direct, 64);
        ByteBuf source = Unpooled.WrappedBuffer(new byte[] { 77 });
        try
        {
            parent.WriteBytes(new byte[] { 1, 2, 3, 4 });
            ByteBuf view = parent.AsReadOnly();
            byte[] original = parent.AsReadOnlyMemory(0, 64).ToArray();
            Action[] mutations =
            {
                () => view.AsMemory(0, 0), () => view.AsSpan(0, 0),
                () => view.Capacity = view.Capacity, () => view.EnsureWritable(0), () => view.EnsureWritable(1),
                () => view.DiscardReadBytes(),
                () => view.SetByte(0, 9), () => view.SetBoolean(0, true), () => view.SetShort(0, 9),
                () => view.SetShortLE(0, 9), () => view.SetMedium(0, 9), () => view.SetMediumLE(0, 9),
                () => view.SetInt(0, 9), () => view.SetIntLE(0, 9), () => view.SetLong(0, 9),
                () => view.SetLongLE(0, 9), () => view.SetFloat(0, 9), () => view.SetFloatLE(0, 9),
                () => view.SetDouble(0, 9), () => view.SetDoubleLE(0, 9), () => view.SetChar(0, 'x'),
                () => view.WriteByte(9), () => view.WriteBoolean(true), () => view.WriteShort(9),
                () => view.WriteShortLE(9), () => view.WriteMedium(9), () => view.WriteMediumLE(9),
                () => view.WriteInt(9), () => view.WriteIntLE(9), () => view.WriteLong(9),
                () => view.WriteLongLE(9), () => view.WriteFloat(9), () => view.WriteFloatLE(9),
                () => view.WriteDouble(9), () => view.WriteDoubleLE(9), () => view.WriteChar('x'),
                () => view.SetBytes(0, new byte[] { 9 }), () => view.SetBytes(0, ReadOnlySpan<byte>.Empty),
                () => view.SetBytes(0, source, 0, 1), () => view.SetZero(0, 0), () => view.SetZero(0, 4),
                () => view.WriteBytes(new byte[] { 9 }), () => view.WriteBytes(ReadOnlySpan<byte>.Empty),
                () => view.WriteBytes(source, 1), () => view.WriteZero(0), () => view.WriteZero(4),
                () => view.SetString(0, "x", Encoding.UTF8), () => view.SetString(0, "", Encoding.UTF8),
                () => view.WriteString("x", Encoding.UTF8), () => view.WriteString("", Encoding.UTF8),
                () => ByteBufUtil.WriteUtf8(view, "x"), () => ByteBufUtil.WriteUtf8(view, ""),
                () => ByteBufUtil.ReserveAndWriteUtf8(view, "x", 1), () => ByteBufUtil.WriteAscii(view, "x"),
                () => ByteBufUtil.WriteAscii(view, new AsciiString("x")),
                () => ByteBufUtil.WriteUtf8(view, new AsciiString("x"))
            };
            foreach (Action mutation in mutations)
            {
                Assert.Throws<NotSupportedException>(mutation);
                Assert.Equal(original, parent.AsReadOnlyMemory(0, 64).ToArray());
                Assert.Equal(0, view.ReaderIndex);
                Assert.Equal(4, view.WriterIndex);
                Assert.Equal(0, source.ReaderIndex);
                Assert.Equal(64, parent.Capacity);
                Assert.Equal(1, parent.ReferenceCount);
            }
            Assert.Equal(1, view.EnsureWritable(0, false));
            Assert.Equal(1, view.EnsureWritable(1, true));
            Assert.Equal(1, view.EnsureWritable(int.MaxValue, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => view.EnsureWritable(-1, true));
        }
        finally { source.Release(); parent.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsWordsStringsSearchesAndProcessorsThroughReadOnlyStorage(bool direct)
    {
        ByteBuf parent = NewBuffer(direct, 32);
        try
        {
            parent.WriteBytes(new byte[] { 1, 0x80, 0xfe, 0xdc, 0xba, 0x98, 0x76, 0x54, 0x32, 0x10,
                0, 0, 0x3f, 0x80, 0, 0, 0x3f, 0xf0, 0, 0, 0, 0, 0, 0, 78, 101, 116, 116, 121 });
            ByteBuf view = parent.AsReadOnly();
            Assert.True(view.GetBoolean(0));
            Assert.Equal((byte)0xfe, view.GetByte(2));
            Assert.Equal(unchecked((short)0x80fe), view.GetShort(1));
            Assert.Equal(unchecked((short)0xfe80), view.GetShortLE(1));
            Assert.Equal((ushort)0x80fe, view.GetUnsignedShort(1));
            Assert.Equal((ushort)0xfe80, view.GetUnsignedShortLE(1));
            Assert.Equal('\u0180', view.GetChar(0));
            Assert.Equal(0xfedcba, view.GetUnsignedMedium(2));
            Assert.Equal(0xbadcfe, view.GetUnsignedMediumLE(2));
            Assert.Equal(unchecked((int)0xfffedcba), view.GetMedium(2));
            Assert.Equal(unchecked((int)0xffbadcfe), view.GetMediumLE(2));
            Assert.Equal(unchecked((int)0xfedcba98), view.GetInt(2));
            Assert.Equal(unchecked((int)0x98badcfe), view.GetIntLE(2));
            Assert.Equal(0xfedcba98U, view.GetUnsignedInt(2));
            Assert.Equal(0x98badcfeU, view.GetUnsignedIntLE(2));
            Assert.Equal(unchecked((long)0xfedcba9876543210UL), view.GetLong(2));
            Assert.Equal(0x1032547698badcfeL, view.GetLongLE(2));
            Assert.Equal(1f, view.GetFloat(12));
            Assert.Equal(0x803f, BitConverter.SingleToInt32Bits(view.GetFloatLE(12)));
            Assert.Equal(1d, view.GetDouble(16));
            Assert.Equal(0xf03f, BitConverter.DoubleToInt64Bits(view.GetDoubleLE(16)));
            Assert.Equal("Netty", view.GetString(24, 5, Encoding.UTF8));
            Assert.Equal(26, view.IndexOf(24, 29, 116));
            Assert.Equal(27, view.IndexOf(29, 24, 116));
            Assert.Equal(2, view.BytesBefore(24, 5, 116));
            Assert.Equal(26, view.ForEachByte(24, 5, b => b != 116));
            Assert.Equal(27, view.ForEachByteDesc(24, 5, b => b != 116));
            view.SetIndex(2, 29);
            Assert.Equal(unchecked((int)0xfedcba98), view.ReadInt());
            Assert.Equal(6, view.ReaderIndex);
            Assert.Equal(0, parent.ReaderIndex);
            view.ReaderIndex = 24;
            Assert.Equal("Netty", view.ReadString(5, Encoding.UTF8));
            Assert.Equal(29, view.ReaderIndex);
            Assert.Equal(0, parent.ReaderIndex);
            Assert.Throws<ArgumentOutOfRangeException>(() => view.GetByte(32));
        }
        finally { parent.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BulkReadCopyAndWritableDestinationConsumeOnlyTheReadOnlyView(bool direct)
    {
        ByteBuf parent = NewBuffer(direct);
        ByteBuf destination = NewBuffer(direct, 0);
        try
        {
            parent.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6 });
            ByteBuf view = parent.AsReadOnly();
            byte[] bytes = new byte[3];
            view.GetBytes(1, bytes);
            Assert.Equal(new byte[] { 2, 3, 4 }, bytes);
            destination.WriteBytes(view, 2);
            Assert.Equal(new byte[] { 1, 2 }, destination.ReadableMemory.ToArray());
            Assert.Equal(2, view.ReaderIndex);
            Assert.Equal(0, parent.ReaderIndex);
            destination.SetBytes(0, view, 4, 2);
            Assert.Equal(new byte[] { 5, 6 }, destination.ReadableMemory.ToArray());
            view.ReadBytes(bytes);
            Assert.Equal(new byte[] { 3, 4, 5 }, bytes);
            Assert.Equal(5, view.ReaderIndex);
            ByteBuf copy = view.Copy(1, 4);
            try
            {
                Assert.False(copy.IsReadOnly);
                Assert.Equal(direct, copy.IsDirect);
                Assert.Equal(new byte[] { 2, 3, 4, 5 }, copy.ReadableMemory.ToArray());
                copy.SetByte(0, 99);
                Assert.Equal(2, parent.GetByte(1));
            }
            finally { copy.Release(); }
            ByteBuf allocatedRead = view.ReadBytes(1);
            try { Assert.False(allocatedRead.IsReadOnly); Assert.Equal(6, allocatedRead.ReadByte()); }
            finally { allocatedRead.Release(); }
            view.ReaderIndex = 0;
            Assert.Throws<NotSupportedException>(() => view.ReadBytes(destination.AsReadOnly(), 1));
            Assert.Equal(0, view.ReaderIndex);
            Assert.Equal(2, destination.WriterIndex);
        }
        finally { destination.Release(); parent.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedSlicesAndDuplicatesRemainReadOnlyWithTheirOwnCoordinateSpace(bool direct)
    {
        ByteBuf parent = NewBuffer(direct);
        try
        {
            for (int i = 0; i < 16; ++i) parent.WriteByte(i);
            ByteBuf view = parent.AsReadOnly();
            foreach (bool retained in new[] { false, true })
            {
                ByteBuf slice = retained ? view.RetainedSlice(2, 6) : view.Slice(2, 6);
                ByteBuf duplicate = retained ? slice.RetainedDuplicate() : slice.Duplicate();
                try
                {
                    Assert.True(slice.IsReadOnly);
                    Assert.True(duplicate.IsReadOnly);
                    Assert.Equal(6, duplicate.Capacity);
                    Assert.Equal(0, duplicate.ReaderIndex);
                    Assert.Equal(6, duplicate.WriterIndex);
                    Assert.Equal(2, duplicate.ReadByte());
                    Assert.Equal(0, slice.ReaderIndex);
                    Assert.Throws<NotSupportedException>(() => duplicate.SetByte(0, 99));
                    Assert.Throws<NotSupportedException>(() => slice.AsMemory(0, 6));
                    ByteBuf nested = duplicate.ReadSlice(2);
                    Assert.True(nested.IsReadOnly);
                    Assert.Equal(new byte[] { 3, 4 }, nested.ReadableMemory.ToArray());
                    parent.SetByte(3, 88);
                    Assert.Equal(88, nested.GetByte(0));
                    Assert.Equal(3, duplicate.ReaderIndex);
                }
                finally { if (retained) { duplicate.Release(); slice.Release(); } }
                parent.SetByte(3, 3);
            }
            Assert.Equal(1, parent.ReferenceCount);
            ByteBuf duplicateView = view.Duplicate();
            duplicateView.ReaderIndex = 4;
            duplicateView.MarkReaderIndex();
            ByteBuf second = duplicateView.Duplicate();
            Assert.Equal(4, second.ReaderIndex);
            second.ResetReaderIndex();
            Assert.Equal(0, second.ReaderIndex); // ReadOnlyByteBuf's constructor does not copy marks.
            Assert.Same(parent, second.Unwrap());
        }
        finally { parent.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedReadOnlyViewsKeepSharedOwnershipAfterParentRelease(bool direct)
    {
        ByteBuf parent = NewBuffer(direct);
        parent.WriteBytes(new byte[] { 1, 2, 3, 4 });
        ByteBuf view = parent.AsReadOnly();
        IReferenceCounted contract = view;
        Assert.Same(view, contract.Retain());
        Assert.Equal(2, parent.ReferenceCount);
        Assert.False(contract.Release());
        Assert.Same(view, contract.Touch("read-only"));
        ByteBuf retained = view.ReadRetainedSlice(3);
        Assert.Equal(2, parent.ReferenceCount);
        Assert.Same(retained, retained.Touch("borrowed"));
        Assert.False(parent.Release());
        Assert.Equal(new byte[] { 1, 2, 3 }, retained.ReadableMemory.ToArray());
        Assert.True(retained.Release());
        Assert.Equal(0, view.ReferenceCount);
        Assert.Throws<IllegalReferenceCountException>(() => view.GetByte(0));
        Assert.Throws<IllegalReferenceCountException>(() => retained.AsReadOnlyMemory(0, 0));
        Assert.Throws<IllegalReferenceCountException>(() => parent.AsReadOnly());
        Assert.Throws<IllegalReferenceCountException>(() => view.AsMemory(0, 0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiscardSomeReadBytesSeparatesIndexOnlyResetFromDataCompaction(bool direct)
    {
        ByteBuf parent = NewBuffer(direct, 8);
        try
        {
            parent.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6 });
            ByteBuf view = parent.AsReadOnly();
            view.SetIndex(1, 6);
            Assert.Same(view, view.DiscardSomeReadBytes());
            Assert.Equal(1, view.ReaderIndex);
            view.ReaderIndex = 4;
            Assert.Throws<NotSupportedException>(() => view.DiscardSomeReadBytes());
            Assert.Equal(4, view.ReaderIndex);
            view.SetIndex(6, 6).MarkReaderIndex().MarkWriterIndex();
            Assert.Same(view, view.DiscardSomeReadBytes());
            Assert.Equal(0, view.ReaderIndex);
            Assert.Equal(0, view.WriterIndex);
            view.ResetReaderIndex().ResetWriterIndex();
            Assert.Equal(0, view.WriterIndex);
            Assert.Throws<NotSupportedException>(() => view.DiscardReadBytes());
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, parent.ReadableMemory.ToArray());
        }
        finally { parent.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParentResizeUpdatesRootViewCapacityAndChecksSlicePhysicalRanges(bool direct)
    {
        ByteBuf parent = NewBuffer(direct, 8);
        try
        {
            parent.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6 });
            ByteBuf view = parent.AsReadOnly();
            ByteBuf slice = view.Slice(0, 6);
            parent.Capacity = 64;
            Assert.Equal(64, view.Capacity);
            Assert.Equal(6, slice.Capacity);
            Assert.Equal(6, view.WriterIndex);
            Assert.Equal(0, view.GetByte(63));
            parent.Capacity = 4;
            Assert.Equal(4, view.Capacity);
            Assert.Equal(6, view.WriterIndex); // Other views' indices are independent, as in Java.
            Assert.Equal(4, slice.GetByte(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => slice.GetByte(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => view.GetByte(4));
        }
        finally { parent.Release(); }
    }

    [Fact]
    public unsafe void NativeReadOnlyMemoryPinsRetainPhysicalStorageAfterLogicalRelease()
    {
        var allocator = new NativeMemoryAllocator();
        ByteBuf parent = new UnpooledDirectByteBuf(8, 16, allocator);
        parent.WriteLong(0x0102030405060708L);
        ByteBuf view = parent.AsReadOnly();
        ReadOnlyMemory<byte> borrowed = view.ReadableMemory;
        using (MemoryHandle lease = borrowed.Pin())
        {
            Assert.True(view.Release());
            Assert.Throws<IllegalReferenceCountException>(() => view.GetByte(0));
            Assert.Throws<ObjectDisposedException>(() => borrowed.Span.ToArray());
            Assert.Equal(1, ((byte*)lease.Pointer)[0]);
            Assert.Equal(8, ((byte*)lease.Pointer)[7]);
            Assert.Equal(8, allocator.ReservedBytes);
        }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void NativeReadableSourceViewSurvivesDestinationGrowthOnTheSameOwner()
    {
        var allocator = new NativeMemoryAllocator();
        ByteBuf parent = new UnpooledDirectByteBuf(4, 64, allocator);
        try
        {
            parent.WriteBytes(new byte[] { 1, 2, 3, 4 });
            ByteBuf source = parent.AsReadOnly();
            parent.WriteBytes(source, 4);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 1, 2, 3, 4 }, parent.ReadableMemory.ToArray());
            Assert.Equal(4, source.ReaderIndex);
            Assert.Equal(4, source.WriterIndex);
            Assert.Equal(0, parent.ReaderIndex);
            Assert.Equal(8, parent.WriterIndex);
        }
        finally { parent.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void EmptyReadOnlyViewIsDistinctAndPermanentlyAccessibleButCannotWrite()
    {
        ByteBuf empty = Unpooled.EmptyBuffer;
        ByteBuf view = empty.AsReadOnly();
        Assert.NotSame(empty, view);
        Assert.False(empty.IsReadOnly);
        Assert.True(view.IsReadOnly);
        Assert.Same(view, view.AsReadOnly());
        Assert.False(empty.CanWrite(0));
        Assert.False(view.CanWrite(0));
        Assert.Equal(0, view.AsReadOnlyMemory(0, 0).Length);
        Assert.Equal("", view.GetString(Encoding.UTF8));
        Assert.False(view.Copy().IsReadOnly);
        Assert.Throws<NotSupportedException>(() => view.SetByte(0, 1));
        Assert.Throws<NotSupportedException>(() => view.WriteString("", Encoding.UTF8));
        Assert.Throws<NotSupportedException>(() => view.AsMemory(0, 0));
        Assert.Equal(1, view.EnsureWritable(0, false));
        Assert.False(view.Release());
        Assert.Equal(1, view.ReferenceCount);
    }
}
