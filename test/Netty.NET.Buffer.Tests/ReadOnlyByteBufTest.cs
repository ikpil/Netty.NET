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
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

/**
 * Tests read-only channel buffers
 */
public class ReadOnlyByteBufTest
{
    private static ByteBuf NewBuffer(bool direct, int capacity = 16, int maximum = 128)
    {
        return direct ? new UnpooledDirectByteBuf(capacity, maximum, new NativeMemoryAllocator())
            : Unpooled.Buffer(capacity, maximum);
    }

    // Original ReadOnlyByteBufTest methods, one CLR test each in source order. AsReadOnly replaces the deprecated
    // unmodifiableBuffer(ByteBuf) ("@deprecated Use ByteBuf#asReadOnly()"), Unpooled.EmptyBuffer is EMPTY_BUFFER,
    // and NotSupportedException replaces UnsupportedOperationException/ReadOnlyBufferException.

    [Fact]
    public void ShouldNotAllowNullInConstructor()
    {
        Assert.Throws<ArgumentNullException>(() => new ReadOnlyByteBuf(null));
    }

    [Fact]
    public void TestUnmodifiableBuffer()
    {
        ByteBuf buf = Unpooled.Buffer(1);
        try
        {
            Assert.IsType<ReadOnlyByteBuf>(buf.AsReadOnly());
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void TestUnmodifiableBufferDoesNotThrowClassCastException()
    {
        // CLR: a duplicated view over a wrapper that is not one of the library's buffer classes replaces
        // new DuplicatedByteBuf(mock(ByteBuf.class)).
        ByteBuf root = Unpooled.Buffer(1);
        try
        {
            _ = new ByteBufView(new ForwardingWrapper(root)).AsReadOnly();
        }
        finally
        {
            root.Release();
        }
    }

    [Fact]
    public void TestUnwrap()
    {
        ByteBuf buf = Unpooled.Buffer(1);
        try
        {
            Assert.Same(buf, buf.AsReadOnly().Unwrap());
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void ShouldHaveSameByteOrder()
    {
        // CLR has no mutable ByteOrder facade: the explicit big- and little-endian accessors of the view read the
        // parent's bytes in the same order as the parent's own accessors.
        ByteBuf buf = Unpooled.Buffer(2).WriteShort(0x0102);
        try
        {
            ByteBuf view = buf.AsReadOnly();
            Assert.Equal(buf.GetShort(0), view.GetShort(0));
            Assert.Equal(buf.GetShortLE(0), view.GetShortLE(0));
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void ShouldReturnReadOnlyDerivedBuffer()
    {
        ByteBuf parent = Unpooled.Buffer(1);
        try
        {
            ByteBuf buf = parent.AsReadOnly();
            Assert.IsType<ReadOnlyByteBuf>(buf.Duplicate());
            Assert.IsType<ReadOnlyByteBuf>(buf.Slice());
            Assert.IsType<ReadOnlyByteBuf>(buf.Slice(0, 1));
            Assert.IsType<ReadOnlyByteBuf>(buf.Duplicate());
        }
        finally
        {
            parent.Release();
        }
    }

    [Fact]
    public void ShouldReturnWritableCopy()
    {
        ByteBuf parent = Unpooled.Buffer(1);
        try
        {
            ByteBuf copy = parent.AsReadOnly().Copy();
            try
            {
                Assert.IsNotType<ReadOnlyByteBuf>(copy);
                Assert.False(copy.IsReadOnly);
            }
            finally
            {
                copy.Release();
            }
        }
        finally
        {
            parent.Release();
        }
    }

    // CLR: replaces mock(ByteBuf.class). The read-only view keeps its own null/range/lifetime checks, so real
    // destinations replace the null arguments and the parent capacity (47 = 23 + 24) covers every stubbed range;
    // the overridden parent members return the source's stubbed values and record the forwarded coordinates.
    private sealed class StubParent() : UnpooledHeapByteBuf(47, 65536)
    {
        internal readonly List<string> Calls = [];
        internal readonly ReadOnlyMemory<byte> Nio = new(new byte[100], 0, 24);
        public override int GetBytes(int index, Socket destination, int length)
        {
            Calls.Add($"channel {index} {length}");
            return index == 1 && length == 2 ? 3 : 0;
        }

        public override ByteBuf GetBytes(int index, Stream destination, int length)
        {
            Calls.Add($"stream {index} {length}");
            return this;
        }

        public override ByteBuf GetBytes(int index, Span<byte> destination)
        {
            Calls.Add($"span {index} {destination.Length}");
            return this;
        }

        public override ByteBuf GetBytes(int index, ByteBuf destination, int destinationIndex, int length)
        {
            Calls.Add($"buf {index} {destinationIndex} {length}");
            return this;
        }

        public override byte GetByte(int index)
        {
            return index == 13 ? (byte)14 : base.GetByte(index);
        }

        public override short GetShort(int index)
        {
            return index == 15 ? (short)16 : base.GetShort(index);
        }

        public override int GetUnsignedMedium(int index)
        {
            return index == 17 ? 18 : base.GetUnsignedMedium(index);
        }

        public override int GetInt(int index)
        {
            return index == 19 ? 20 : base.GetInt(index);
        }

        public override long GetLong(int index)
        {
            return index == 21 ? 22L : base.GetLong(index);
        }

        public override ReadOnlyMemory<byte> AsReadOnlyMemory(int index, int length)
        {
            return index == 23 && length == 24 ? Nio : base.AsReadOnlyMemory(index, length);
        }
    }

    [Fact]
    public void ShouldForwardReadCallsBlindly()
    {
        var buf = new StubParent();
        ByteBuf destination = Unpooled.Buffer(21);
        using var channel = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            Assert.Equal(65536, buf.MaxCapacity);
            Assert.Equal(0, buf.ReaderIndex);
            Assert.Equal(0, buf.WriterIndex);

            ByteBuf roBuf = buf.AsReadOnly();
            Assert.Equal(3, roBuf.GetBytes(1, channel, 2));
            roBuf.GetBytes(4, Stream.Null, 5);
            roBuf.GetBytes(6, new byte[15].AsSpan(7, 8));
            roBuf.GetBytes(9, destination, 10, 11);
            roBuf.GetBytes(12, new byte[4].AsSpan());
            Assert.Equal((byte)14, roBuf.GetByte(13));
            Assert.Equal((short)16, roBuf.GetShort(15));
            Assert.Equal(18, roBuf.GetUnsignedMedium(17));
            Assert.Equal(20, roBuf.GetInt(19));
            Assert.Equal(22L, roBuf.GetLong(21));
            Assert.Equal(new[] { "channel 1 2", "stream 4 5", "span 6 8", "buf 9 10 11", "span 12 4" }, buf.Calls);

            // CLR: the read-only Memory borrow replaces the read-only NIO view; the backing array is the stub's
            // 100-byte buffer and the mutable borrow is rejected.
            ReadOnlyMemory<byte> roBB = roBuf.AsReadOnlyMemory(23, 24);
            Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(roBB, out var array));
            Assert.Equal(100, array.Array.Length);
            Assert.Throws<NotSupportedException>(() => roBuf.AsMemory(23, 24));

            Assert.Equal(47, roBuf.Capacity);
        }
        finally
        {
            destination.Release();
            buf.Release();
        }
    }

    [Fact]
    public void ShouldRejectDiscardReadBytes()
    {
        Assert.Throws<NotSupportedException>(() => Unpooled.EmptyBuffer.AsReadOnly().DiscardReadBytes());
    }

    [Fact]
    public void ShouldRejectSetByte()
    {
        Assert.Throws<NotSupportedException>(() => Unpooled.EmptyBuffer.AsReadOnly().SetByte(0, 0));
    }

    [Fact]
    public void ShouldRejectSetShort()
    {
        Assert.Throws<NotSupportedException>(() => Unpooled.EmptyBuffer.AsReadOnly().SetShort(0, 0));
    }

    [Fact]
    public void ShouldRejectSetMedium()
    {
        Assert.Throws<NotSupportedException>(() => Unpooled.EmptyBuffer.AsReadOnly().SetMedium(0, 0));
    }

    [Fact]
    public void ShouldRejectSetInt()
    {
        Assert.Throws<NotSupportedException>(() => Unpooled.EmptyBuffer.AsReadOnly().SetInt(0, 0));
    }

    [Fact]
    public void ShouldRejectSetLong()
    {
        Assert.Throws<NotSupportedException>(() => Unpooled.EmptyBuffer.AsReadOnly().SetLong(0, 0));
    }

    // ReadOnlyByteBuf rejects these mutations before it inspects the source, so a null source still reports the
    // read-only error (ReadOnlyBufferException is an UnsupportedOperationException; CLR NotSupportedException).
    [Fact]
    public void ShouldRejectSetBytes1()
    {
        Assert.Throws<NotSupportedException>(() => Unpooled.EmptyBuffer.AsReadOnly().SetBytes(0, (Stream)null, 0));
    }

    // CLR: Socket replaces ScatteringByteChannel.
    [Fact]
    public void ShouldRejectSetBytes2()
    {
        Assert.Throws<NotSupportedException>(() => Unpooled.EmptyBuffer.AsReadOnly().SetBytes(0, (Socket)null, 0));
    }

    // CLR: the byte array range is a ReadOnlySpan; an empty range of an empty array stands for (byte[]) null, 0, 0.
    [Fact]
    public void ShouldRejectSetBytes3()
    {
        Assert.Throws<NotSupportedException>(() => Unpooled.EmptyBuffer.AsReadOnly().SetBytes(0, Array.Empty<byte>().AsSpan(0, 0)));
    }

    [Fact]
    public void ShouldRejectSetBytes4()
    {
        Assert.Throws<NotSupportedException>(() => Unpooled.EmptyBuffer.AsReadOnly().SetBytes(0, (ByteBuf)null, 0, 0));
    }

    // CLR: ReadOnlySpan replaces the NIO ByteBuffer; an empty span stands for (ByteBuffer) null.
    [Fact]
    public void ShouldRejectSetBytes5()
    {
        Assert.Throws<NotSupportedException>(() => Unpooled.EmptyBuffer.AsReadOnly().SetBytes(0, ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void ShouldIndicateNotWritable()
    {
        ByteBuf buf = Unpooled.Buffer(1);
        try
        {
            Assert.False(buf.AsReadOnly().IsWritable());
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void ShouldIndicateNotWritableAnyNumber()
    {
        ByteBuf buf = Unpooled.Buffer(1);
        try
        {
            Assert.False(buf.AsReadOnly().IsWritable(1));
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void EnsureWritableIntStatusShouldFailButNotThrow()
    {
        EnsureWritableIntStatusShouldFailButNotThrow0(false);
    }

    [Fact]
    public void EnsureWritableForceIntStatusShouldFailButNotThrow()
    {
        EnsureWritableIntStatusShouldFailButNotThrow0(true);
    }

    // CLR: xUnit1024 rejects a test overloading the source helper ensureWritableIntStatusShouldFailButNotThrow(boolean),
    // so the helper carries a 0 suffix.
    private static void EnsureWritableIntStatusShouldFailButNotThrow0(bool force)
    {
        ByteBuf buf = Unpooled.Buffer(1);
        ByteBuf readOnly = buf.AsReadOnly();
        int result = readOnly.EnsureWritable(1, force);
        Assert.Equal(1, result);
        Assert.False(ByteBufUtil.EnsureWritableSuccess(result));
        readOnly.Release();
    }

    [Fact]
    public void EnsureWritableShouldThrow()
    {
        ByteBuf buf = Unpooled.Buffer(1);
        ByteBuf readOnly = buf.AsReadOnly();
        try
        {
            Assert.Throws<NotSupportedException>(() => readOnly.EnsureWritable(1));
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void AsReadOnly()
    {
        ByteBuf buf = Unpooled.Buffer(1);
        ByteBuf readOnly = buf.AsReadOnly();
        Assert.True(readOnly.IsReadOnly);
        Assert.Same(readOnly, readOnly.AsReadOnly());
        readOnly.Release();
    }

    [Fact]
    public void TestDuplicateOfSliceHasTheSameCapacityAsTheSlice()
    {
        ByteBuf buffer = Unpooled.Buffer(16);

        ByteBuf slice = buffer.Slice();
        ByteBuf duplicate = slice.Duplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);

        slice = buffer.Slice(0, buffer.Capacity - 2);
        duplicate = slice.Duplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);

        slice = buffer.Slice();
        duplicate = slice.RetainedDuplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);
        duplicate.Release();

        slice = buffer.Slice(0, buffer.Capacity - 2);
        duplicate = slice.RetainedDuplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);
        duplicate.Release();

        buffer.Release();
    }

    [Fact]
    public void TestDuplicateOfRetainedSliceHasTheSameCapacityAsTheSlice()
    {
        ByteBuf buffer = Unpooled.Buffer(16);

        ByteBuf slice = buffer.RetainedSlice();
        ByteBuf duplicate = slice.Duplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);
        slice.Release();

        slice = buffer.RetainedSlice(0, buffer.Capacity - 2);
        duplicate = slice.Duplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);
        slice.Release();

        slice = buffer.RetainedSlice();
        duplicate = slice.RetainedDuplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);
        slice.Release();
        duplicate.Release();

        slice = buffer.RetainedSlice(0, buffer.Capacity - 2);
        duplicate = slice.RetainedDuplicate();
        Assert.Equal(slice.Capacity, duplicate.Capacity);
        slice.Release();
        duplicate.Release();

        buffer.Release();
    }

    // CLR supplements below: native storage, ownership, coordinate and mutation contracts that the source fixture
    // covers only through its JVM buffer types.

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
            Assert.Equal(1, view.RefCnt);
            Assert.Equal(1, view.ReaderIndex);
            Assert.Equal(4, view.WriterIndex);
            Assert.False(view.IsWritable());
            Assert.False(view.IsWritable(0));
            Assert.False(view.IsWritable(1));
            Assert.True(parent.IsWritable(1));
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
        finally
        {
            parent.Release();
        }
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
                () => view.SetCharSequence(0, "x", Encoding.UTF8), () => view.SetCharSequence(0, "", Encoding.UTF8),
                () => view.WriteCharSequence("x", Encoding.UTF8), () => view.WriteCharSequence("", Encoding.UTF8),
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
                Assert.Equal(1, parent.RefCnt);
            }
            Assert.Equal(1, view.EnsureWritable(0, false));
            Assert.Equal(1, view.EnsureWritable(1, true));
            Assert.Equal(1, view.EnsureWritable(int.MaxValue, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => view.EnsureWritable(-1, true));
        }
        finally
        {
            source.Release();
            parent.Release();
        }
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
            Assert.Equal("Netty", view.ToString(24, 5, Encoding.UTF8));
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
        finally
        {
            parent.Release();
        }
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
            finally
            {
                copy.Release();
            }
            ByteBuf allocatedRead = view.ReadBytes(1);
            try
            {
                Assert.False(allocatedRead.IsReadOnly);
                Assert.Equal(6, allocatedRead.ReadByte());
            }
            finally
            {
                allocatedRead.Release();
            }
            view.ReaderIndex = 0;
            Assert.Throws<NotSupportedException>(() => view.ReadBytes(destination.AsReadOnly(), 1));
            Assert.Equal(0, view.ReaderIndex);
            Assert.Equal(2, destination.WriterIndex);
        }
        finally
        {
            destination.Release();
            parent.Release();
        }
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
                finally
                {
                    if (retained)
                    {
                        duplicate.Release();
                        slice.Release();
                    }
                }
                parent.SetByte(3, 3);
            }
            Assert.Equal(1, parent.RefCnt);
            ByteBuf duplicateView = view.Duplicate();
            duplicateView.ReaderIndex = 4;
            duplicateView.MarkReaderIndex();
            ByteBuf second = duplicateView.Duplicate();
            Assert.Equal(4, second.ReaderIndex);
            second.ResetReaderIndex();
            Assert.Equal(0, second.ReaderIndex); // ReadOnlyByteBuf's constructor does not copy marks.
            Assert.Same(parent, second.Unwrap());
        }
        finally
        {
            parent.Release();
        }
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
        Assert.Equal(2, parent.RefCnt);
        Assert.False(contract.Release());
        Assert.Same(view, contract.Touch("read-only"));
        ByteBuf retained = view.ReadRetainedSlice(3);
        Assert.Equal(2, parent.RefCnt);
        Assert.Same(retained, retained.Touch("borrowed"));
        Assert.False(parent.Release());
        Assert.Equal(new byte[] { 1, 2, 3 }, retained.ReadableMemory.ToArray());
        Assert.True(retained.Release());
        Assert.Equal(0, view.RefCnt);
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
        finally
        {
            parent.Release();
        }
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
        finally
        {
            parent.Release();
        }
    }

    [Fact]
    public void CapturedReadOnlySpanKeepsDataAfterLogicalRelease()
    {
        var allocator = new NativeMemoryAllocator();
        ByteBuf parent = new UnpooledDirectByteBuf(8, 16, allocator);
        parent.WriteLong(0x0102030405060708L);
        ByteBuf view = parent.AsReadOnly();
        ReadOnlyMemory<byte> borrowed = view.ReadableMemory;
        ReadOnlySpan<byte> captured = borrowed.Span;
        using (MemoryHandle lease = borrowed.Pin())
        {
            Assert.True(view.Release());
            Assert.Throws<IllegalReferenceCountException>(() => view.GetByte(0));
            Assert.Throws<ObjectDisposedException>(() => borrowed.Span.ToArray());
            Assert.Equal(1, captured[0]);
            Assert.Equal(8, captured[7]);
            Assert.Equal(0, allocator.ReservedBytes);
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
        finally
        {
            parent.Release();
        }
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
        Assert.False(empty.IsWritable(0));
        Assert.False(view.IsWritable(0));
        Assert.Equal(0, view.AsReadOnlyMemory(0, 0).Length);
        Assert.Equal("", view.ToString(Encoding.UTF8));
        Assert.False(view.Copy().IsReadOnly);
        Assert.Throws<NotSupportedException>(() => view.SetByte(0, 1));
        Assert.Throws<NotSupportedException>(() => view.WriteCharSequence("", Encoding.UTF8));
        Assert.Throws<NotSupportedException>(() => view.AsMemory(0, 0));
        Assert.Equal(1, view.EnsureWritable(0, false));
        Assert.False(view.Release());
        Assert.Equal(1, view.RefCnt);
    }

    private sealed class ForwardingWrapper(ByteBuf parent) : WrappedByteBuf(parent);

    [Theory]
    [InlineData(false, 0)] [InlineData(false, 1)] [InlineData(false, 2)]
    [InlineData(true, 0)] [InlineData(true, 1)] [InlineData(true, 2)]
    public void FactoryAcceptsOrdinaryWrappedAndDuplicatedParentsWithoutJvmSubtypeCasts(bool direct, int shape)
    {
        var native = new NativeMemoryAllocator();
        ByteBuf root = direct ? new UnpooledDirectByteBuf(16, 16, native) : new UnpooledHeapByteBuf(16, 16);
        try
        {
            for (int i = 0; i < 16; i++) root.WriteByte(i);
            root.SetIndex(1, 13);
            var wrapped = new ForwardingWrapper(root);
            ByteBuf parent = shape == 0 ? root : shape == 1 ? wrapped : new ByteBufView(wrapped);
            ByteBuf view = parent.AsReadOnly();
            Assert.IsType<ReadOnlyByteBuf>(view);
            Assert.Same(shape == 2 ? wrapped : root, view.Unwrap());
            Assert.Equal(direct, view.IsDirect);
            Assert.Same(root.Alloc(), view.Alloc());
            Assert.Equal(1, view.ReaderIndex);
            Assert.Equal(13, view.WriterIndex);
            Assert.Equal(16, view.Capacity);
            Assert.Equal(1, root.RefCnt);
            Assert.Same(view, view.AsReadOnly());
            Assert.Equal(0x0203, view.GetShort(2));
            Assert.Equal(0x0302, view.GetShortLE(2));
            foreach (ByteBuf derived in new[] { view.Duplicate(), view.Slice(), view.Slice(2, 6) })
                Assert.True(derived.IsReadOnly);
            foreach (bool force in new[] { false, true })
            {
                int status = view.EnsureWritable(1, force);
                Assert.Equal(1, status); Assert.False(ByteBufUtil.EnsureWritableSuccess(status));
            }
            Assert.Equal(1, root.ReaderIndex);
            Assert.Equal(13, root.WriterIndex);
            Assert.Equal(1, view.ReaderIndex);
            Assert.Equal(13, view.WriterIndex);
        }
        finally
        {
            Assert.True(root.Release());
        }
        Assert.Equal(0, native.ReservedBytes);
    }

    public static IEnumerable<object[]> DuplicateCapacityCases()
    {
        foreach (bool direct in new[] { false, true })
        foreach (bool pooled in new[] { false, true })
        foreach (bool readOnly in new[] { false, true })
        foreach (bool retainSlice in new[] { false, true })
        foreach (bool retainDuplicate in new[] { false, true })
        for (int range = 0; range < 3; range++) yield return [direct, pooled, readOnly, retainSlice, retainDuplicate, range];
    }

    [Theory]
    [MemberData(nameof(DuplicateCapacityCases))]
    public void DuplicateOfSlicePreservesCapacityAndRetainedParentLifetime(bool direct, bool pooled, bool readOnly,
        bool retainSlice, bool retainDuplicate, int range)
    {
        var native = new NativeMemoryAllocator();
        PooledByteBufAllocator allocator = pooled ? new PooledByteBufAllocator(pageSize: 4096, maxOrder: 1, nativeAllocator: native) : null;
        ByteBuf root = pooled ? direct ? new PooledDirectByteBuf((PoolDirectArena)allocator.DirectArenas[0], 16, 32)
                : new PooledHeapByteBuf((PoolHeapArena)allocator.HeapArenas[0], 16, 32)
            : direct ? new UnpooledDirectByteBuf(16, 32, native) : new UnpooledHeapByteBuf(16, 32);
        ByteBuf slice = null, duplicate = null;
        bool rootReleased = false, sliceReleased = false, duplicateReleased = false;
        try
        {
            for (int i = 0; i < 16; i++) root.WriteByte(i);
            root.SetIndex(4, 12).MarkReaderIndex().MarkWriterIndex();
            ByteBuf parent = readOnly ? root.AsReadOnly() : root;
            int index = range == 0 ? 4 : range == 1 ? 0 : 16;
            int length = range == 0 ? 8 : range == 1 ? 14 : 0;
            slice = range == 0 ? retainSlice ? parent.RetainedSlice() : parent.Slice()
                : retainSlice ? parent.RetainedSlice(index, length) : parent.Slice(index, length);
            int count = slice.RefCnt;
            duplicate = retainDuplicate ? slice.RetainedDuplicate() : slice.Duplicate();
            Assert.NotSame(slice, duplicate);
            Assert.Equal(length, slice.Capacity);
            Assert.Equal(slice.Capacity, duplicate.Capacity);
            Assert.Equal(slice.MaxCapacity, duplicate.MaxCapacity);
            Assert.Equal(readOnly, duplicate.IsReadOnly);
            Assert.Equal(direct, duplicate.IsDirect);
            Assert.Equal(0, duplicate.ReaderIndex);
            Assert.Equal(length, duplicate.WriterIndex);
            Assert.Equal(count + (retainDuplicate ? 1 : 0), slice.RefCnt);
            if (length > 0)
            {
                Assert.Equal(index, duplicate.ReadByte());
            }
            Assert.Equal(0, slice.ReaderIndex);
            Assert.Equal(length, slice.WriterIndex);
            Assert.Equal(4, root.ReaderIndex);
            Assert.Equal(12, root.WriterIndex);
            if (retainSlice)
            {
                slice.Release();
                sliceReleased = true;
            }
            root.Release();
            rootReleased = true;
            if (retainDuplicate)
            {
                Assert.True(duplicate.RefCnt > 0);
                if (length > 0)
                {
                    Assert.Equal(index, duplicate.GetByte(0));
                }
                else
                {
                    Assert.Empty(duplicate.AsReadOnlyMemory(0, 0).ToArray());
                }
                Assert.True(duplicate.Release());
                duplicateReleased = true;
            }
            Assert.Equal(0, root.RefCnt);
            Assert.Equal(0, duplicate.RefCnt);
            Assert.Throws<IllegalReferenceCountException>(() => duplicate.AsReadOnlyMemory(0, 0));
        }
        finally
        {
            if (retainDuplicate && !duplicateReleased && duplicate?.RefCnt > 0)
            {
                duplicate.Release();
            }
            if (retainSlice && !sliceReleased && slice?.RefCnt > 0)
            {
                slice.Release();
            }
            if (!rootReleased && root.RefCnt > 0)
            {
                root.Release();
            }
            allocator?.Dispose();
        }
        Assert.Equal(0, native.ReservedBytes);
    }

    // CLR supplement: the asynchronous stream and socket forms report the same rejection through their ValueTask.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task AsynchronousNullSourceRejectsReadOnlyStorageFirst(int kind)
    {
        ByteBuf view = Unpooled.EmptyBuffer.AsReadOnly();
        CancellationToken token = TestContext.Current.CancellationToken;
        Task<int> operation = kind == 0 ? view.SetBytesAsync(0, (Stream)null, 0, token).AsTask()
            : view.SetBytesAsync(0, (Socket)null, 0, token).AsTask();
        await Assert.ThrowsAsync<NotSupportedException>(() => operation);
        Assert.Equal(0, view.ReaderIndex);
        Assert.Equal(0, view.WriterIndex);
        Assert.Equal(1, view.RefCnt);
        Assert.False(view.Release());
    }

    [Fact]
    public void EmptySpanInputStillRejectsReadOnlyStorage()
    {
        ByteBuf view = Unpooled.EmptyBuffer.AsReadOnly();
        Assert.Throws<NotSupportedException>(() => view.SetBytes(0, ReadOnlySpan<byte>.Empty));
        Assert.Throws<NotSupportedException>(() => view.WriteBytes(ReadOnlySpan<byte>.Empty));
        Assert.Equal(0, view.ReaderIndex);
        Assert.Equal(0, view.WriterIndex);
        Assert.False(view.Release());
    }

    private sealed class MemoryObservation
    {
        public int Calls, Index, Length;
        public ReadOnlyMemory<byte> Result;
        public ReadOnlyMemory<byte> Record(int index, int length, ReadOnlyMemory<byte> result)
        {
            Calls++;
            Index = index;
            Length = length;
            return Result = result;
        }
    }
    private sealed class MemoryHeap(MemoryObservation o) : UnpooledHeapByteBuf(16, 16)
    {
        public override ReadOnlyMemory<byte> AsReadOnlyMemory(int index, int length)
        {
            return o.Record(index, length, base.AsReadOnlyMemory(index, length));
        }
    }
    private sealed class MemoryDirect(MemoryObservation o, NativeMemoryAllocator native) : UnpooledDirectByteBuf(16, 16, native)
    {
        public override ReadOnlyMemory<byte> AsReadOnlyMemory(int index, int length)
        {
            return o.Record(index, length, base.AsReadOnlyMemory(index, length));
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadOnlyMemoryBorrowPreservesParentOverrideAndLiveStorage(bool direct)
    {
        var native = new NativeMemoryAllocator();
        var observed = new MemoryObservation();
        ByteBuf root = direct ? new MemoryDirect(observed, native) : new MemoryHeap(observed);
        try
        {
            for (int i = 0; i < 16; i++)
                root.WriteByte(i);
            root.SetIndex(3, 13);
            ByteBuf view = root.AsReadOnly();
            ReadOnlyMemory<byte> memory = view.AsReadOnlyMemory(7, 3);
            Assert.Equal(1, observed.Calls);
            Assert.Equal(7, observed.Index);
            Assert.Equal(3, observed.Length);
            Assert.Equal(observed.Result, memory);
            Assert.Equal(new byte[] { 7, 8, 9 }, memory.ToArray());
            root.SetByte(7, 77);
            Assert.Equal(77, memory.Span[0]);
            Assert.Throws<NotSupportedException>(() => view.AsMemory(7, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => view.AsReadOnlyMemory(16, 1));
            Assert.Equal(1, observed.Calls);
            Assert.Equal(1, root.RefCnt);
            Assert.Equal(3, view.ReaderIndex);
            Assert.Equal(13, view.WriterIndex);
            Assert.Equal(3, root.ReaderIndex);
            Assert.Equal(13, root.WriterIndex);
        }
        finally
        {
            Assert.True(root.Release());
        }
        Assert.Equal(0, native.ReservedBytes);
    }
}
