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
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// Selected pinned AbstractCompositeByteBufTest contracts, with additional CLR
// sequence, native-quota and aliasing fixtures. Unported cases remain inventoried.
public class CompositeByteBufTest
{
    private static ByteBuf Segment(byte[] bytes, bool direct, NativeMemoryAllocator allocator)
        => direct ? new UnpooledDirectByteBuf(bytes.Length, allocator: allocator).WriteBytes(bytes) : Unpooled.WrappedBuffer(bytes);

    private static byte[] Bytes(ByteBuf buffer) => buffer.ReadableSequence.ToArray();

    [Fact]
    public void EmptyCompositeHasIndependentOwnershipAndCheckedAccess()
    {
        CompositeByteBuf buffer = Unpooled.CompositeBuffer();
        Assert.Equal(16, buffer.MaxNumComponents);
        Assert.Equal(0, buffer.NumComponents);
        Assert.Equal(0, buffer.Capacity);
        Assert.False(buffer.IsDirect);
        Assert.False(buffer.IsReadOnly);
        Assert.True(buffer.ReadableSequence.IsEmpty);
        Assert.True(buffer.AsMemory(0, 0).IsEmpty);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetByte(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.ToComponentIndex(0));
        ByteBuf copy = buffer.Copy();
        try { Assert.Equal(0, copy.Capacity); Assert.Equal(1, copy.ReferenceCount); }
        finally { copy.Release(); }
        Assert.True(buffer.Release());
        Assert.Throws<IllegalReferenceCountException>(() => buffer.AsReadOnlySequence(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Unpooled.CompositeBuffer(0));
    }

    [Fact]
    public void AddCapturesReadableRangeWithoutRetainingOrMovingSourceIndices()
    {
        ByteBuf source = Unpooled.WrappedBuffer(new byte[] { 9, 1, 2, 8 }).SetIndex(1, 3);
        CompositeByteBuf buffer = Unpooled.CompositeBuffer();
        try
        {
            buffer.AddComponent(source);
            Assert.Equal(2, buffer.Capacity);
            Assert.Equal(0, buffer.WriterIndex);
            Assert.Equal(1, source.ReferenceCount);
            Assert.Equal(1, source.ReaderIndex);
            Assert.Equal(3, source.WriterIndex);
            source.SetIndex(0, 4);
            buffer.WriterIndex = 2;
            Assert.Equal(new byte[] { 1, 2 }, Bytes(buffer));
            ByteBuf duplicate = buffer.Component(0);
            ByteBuf slice = buffer.ComponentSlice(0);
            Assert.Equal(4, duplicate.Capacity);
            Assert.Equal(0, duplicate.ReaderIndex);
            Assert.Equal(4, duplicate.WriterIndex);
            Assert.Equal(2, slice.Capacity);
            Assert.Equal(0, slice.ReaderIndex);
            Assert.Equal(2, slice.WriterIndex);
            slice.SetByte(0, 7);
            Assert.Equal(7, buffer.GetByte(0));
        }
        finally { buffer.Release(); }
        Assert.Equal(0, source.ReferenceCount);
    }

    [Fact]
    public void ComponentsKeepOriginalReferenceCountsAndReleaseOneOwnedReferenceEach()
    {
        ByteBuf c1 = Unpooled.Buffer(1).WriteByte(1);
        ByteBuf c2 = Unpooled.Buffer(1).WriteByte(2).Retain();
        ByteBuf c3 = Unpooled.Buffer(1).WriteByte(3).Retain(2);
        CompositeByteBuf buffer = Unpooled.CompositeBuffer();
        buffer.AddComponent(c1, true).AddComponent(c2, true).AddComponent(c3, true);
        // Ensure that c[123]'s refCount did not change.
        Assert.Equal(new[] { 1, 2, 3 }, new[] { c1.ReferenceCount, c2.ReferenceCount, c3.ReferenceCount });
        Assert.Equal(1, buffer.ReferenceCount);
        buffer.Release();
        Assert.Equal(new[] { 0, 1, 2 }, new[] { c1.ReferenceCount, c2.ReferenceCount, c3.ReferenceCount });
        c2.Release(); c3.Release(2);
    }

    [Fact]
    public void RetainedSlicesReleaseTheOriginalOwnerOncePerTransferredReference()
    {
        ByteBuf owner = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4 });
        ByteBuf c1 = owner.ReadRetainedSlice(2);
        ByteBuf c2 = owner.ReadRetainedSlice(2);
        CompositeByteBuf buffer = Unpooled.CompositeBuffer();
        buffer.AddComponent(c1, true).AddComponent(c2, true);
        Assert.Equal(3, owner.ReferenceCount);
        // releasing composite should release the components
        buffer.Release();
        Assert.Equal(1, owner.ReferenceCount);
        // last remaining ref to buffer
        owner.Release();
        Assert.Equal(0, owner.ReferenceCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EveryWordWidthAndEndianWorksAcrossAllComponentBoundaries(bool direct, bool littleEndian)
    {
        var allocator = new NativeMemoryAllocator(1024);
        byte[] expected = littleEndian ? new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 } : new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        foreach (int width in new[] { 2, 3, 4, 8 })
        for (int split = 1; split < width; ++split)
        {
            CompositeByteBuf buffer = new();
            try
            {
                buffer.AddComponent(Segment(new byte[split], direct, allocator), true);
                buffer.AddComponent(Unpooled.Buffer(0, 0));
                buffer.AddComponent(Segment(new byte[width - split], direct, allocator), true);
                switch (width)
                {
                    case 2:
                        if (littleEndian) buffer.SetShortLE(0, 0x0708); else buffer.SetShort(0, 0x0102);
                        Assert.Equal(littleEndian ? 0x0708 : 0x0102, littleEndian ? buffer.ReadUnsignedShortLE() : buffer.ReadUnsignedShort());
                        break;
                    case 3:
                        if (littleEndian) buffer.SetMediumLE(0, 0x060708); else buffer.SetMedium(0, 0x010203);
                        Assert.Equal(littleEndian ? 0x060708 : 0x010203, littleEndian ? buffer.ReadUnsignedMediumLE() : buffer.ReadUnsignedMedium());
                        break;
                    case 4:
                        if (littleEndian) buffer.SetIntLE(0, 0x05060708); else buffer.SetInt(0, 0x01020304);
                        Assert.Equal(littleEndian ? 0x05060708 : 0x01020304, littleEndian ? buffer.ReadIntLE() : buffer.ReadInt());
                        break;
                    case 8:
                        if (littleEndian) buffer.SetLongLE(0, 0x0102030405060708); else buffer.SetLong(0, 0x0102030405060708);
                        Assert.Equal(0x0102030405060708, littleEndian ? buffer.ReadLongLE() : buffer.ReadLong());
                        break;
                }
                buffer.ReaderIndex = 0;
                Assert.Equal(expected[..width], Bytes(buffer));
                Assert.Equal(width, buffer.WriterIndex);
                Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetLong(width - 1));
            }
            finally { buffer.Release(); }
        }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleByteSegmentsHandleSignedWordsFloatingBitsAndSequentialWrites(bool direct)
    {
        var allocator = new NativeMemoryAllocator(1024);
        CompositeByteBuf buffer = new(64);
        try
        {
            for (int i = 0; i < 32; ++i) buffer.AddComponent(Segment(new byte[1], direct, allocator));
            buffer.WriteShort(-1).WriteMedium(-2).WriteIntLE(int.MinValue).WriteLong(long.MinValue)
                .WriteFloatLE(BitConverter.Int32BitsToSingle(unchecked((int)0x7fc01234))).WriteDouble(-0.0);
            Assert.Equal(-1, buffer.ReadShort());
            Assert.Equal(-2, buffer.ReadMedium());
            Assert.Equal(int.MinValue, buffer.ReadIntLE());
            Assert.Equal(long.MinValue, buffer.ReadLong());
            Assert.Equal(unchecked((int)0x7fc01234), BitConverter.SingleToInt32Bits(buffer.ReadFloatLE()));
            Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(buffer.ReadDouble()));
        }
        finally { buffer.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadOnlySequencesExposeSharedSegmentsAndNestedViewCoordinates(bool direct)
    {
        var allocator = new NativeMemoryAllocator(1024);
        CompositeByteBuf nested = new(), buffer = new();
        nested.AddComponent(Segment(new byte[] { 1, 2 }, direct, allocator), true)
            .AddComponent(Segment(new byte[] { 3, 4 }, direct, allocator), true);
        buffer.AddComponent(Segment(new byte[] { 0 }, direct, allocator), true).AddComponent(nested, true);
        try
        {
            ByteBuf view = buffer.Slice(1, 4).Duplicate().AsReadOnly();
            ReadOnlySequence<byte> sequence = view.ReadableSequence;
            Assert.False(sequence.IsSingleSegment);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, sequence.ToArray());
            Assert.Equal(new byte[] { 2, 3 }, view.AsReadOnlySequence(1, 2).ToArray());
            Assert.Equal(new byte[] { 1, 2 }, view.AsReadOnlyMemory(0, 2).ToArray());
            Assert.Throws<NotSupportedException>(() => view.ReadableMemory);
            Assert.Throws<NotSupportedException>(() => buffer.AsMemory(0, 5));
            nested.SetByte(2, 7);
            Assert.Equal(new byte[] { 1, 2, 7, 4 }, sequence.ToArray());
            Assert.Equal(0x01020704, view.GetInt(0));
            Assert.Throws<NotSupportedException>(() => view.SetInt(0, 0));
            ByteBuf copy = view.Copy();
            try { copy.SetByte(0, 9); Assert.Equal(1, view.GetByte(0)); }
            finally { copy.Release(); }
        }
        finally { buffer.Release(); }
        Assert.Equal(0, nested.ReferenceCount);
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void InsertEmptyComponentsAndOffsetMappingPreserveSourceCoordinates()
    {
        CompositeByteBuf buffer = new();
        try
        {
            buffer.AddComponent(Unpooled.EmptyBuffer).AddComponent(Unpooled.WrappedBuffer(new byte[] { 1, 2 }), true)
                .AddComponent(Unpooled.WrappedBuffer(new byte[] { 3, 4 }), true);
            buffer.AddComponent(2, Unpooled.EmptyBuffer);
            buffer.AddComponent(3, Unpooled.WrappedBuffer(new byte[] { 9 }), true);
            Assert.Equal(new byte[] { 1, 2, 9, 3, 4 }, Bytes(buffer));
            Assert.Equal(1, buffer.ToComponentIndex(0));
            Assert.Equal(3, buffer.ToComponentIndex(2));
            Assert.Equal(4, buffer.ToComponentIndex(3));
            Assert.Equal(2, buffer.ToByteIndex(2));
            Assert.Equal(2, buffer.ToByteIndex(3));
            Assert.Equal(new byte[] { 9 }, Bytes(buffer.ComponentAtOffset(2)));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Component(buffer.NumComponents));
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsolidationThresholdRangesAndAllocationPolicyPreserveContent(bool direct)
    {
        var allocator = new NativeMemoryAllocator(1024);
        var sources = new List<ByteBuf>();
        CompositeByteBuf buffer = new(3, direct, allocator);
        try
        {
            foreach (byte[] bytes in new[] { new byte[] { 1 }, new byte[] { 2, 3 }, new byte[] { 4, 5, 6 } })
            { ByteBuf source = Segment(bytes, direct, allocator); sources.Add(source); buffer.AddComponent(source, true); }
            Assert.Equal(3, buffer.NumComponents);
            ByteBuf fourth = Segment(new byte[] { 7 }, direct, allocator); sources.Add(fourth);
            buffer.AddComponent(fourth, true);
            Assert.Equal(1, buffer.NumComponents);
            Assert.All(sources, source => Assert.Equal(0, source.ReferenceCount));
            Assert.Equal(direct, buffer.IsDirect);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7 }, Bytes(buffer));
            Assert.True(buffer.ReadableSequence.IsSingleSegment);
            Assert.Equal(7, buffer.ReadableMemory.Length);
            buffer.AddComponent(Segment(new byte[] { 8, 9 }, direct, allocator), true)
                .AddComponent(Segment(new byte[] { 10 }, direct, allocator), true);
            buffer.ReaderIndex = 1;
            buffer.Consolidate(1, 2);
            Assert.Equal(2, buffer.NumComponents);
            Assert.Equal(1, buffer.ReaderIndex);
            Assert.Equal(10, buffer.WriterIndex);
            Assert.Equal(new byte[] { 8, 9, 10 }, Bytes(buffer.ComponentSlice(1)));
        }
        finally { buffer.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void NativeConsolidationFailurePreservesLayoutOwnershipAndIndices()
    {
        var allocator = new NativeMemoryAllocator(4);
        ByteBuf first = Segment(new byte[] { 1, 2 }, true, allocator);
        ByteBuf second = Segment(new byte[] { 3, 4 }, true, allocator);
        CompositeByteBuf buffer = new(16, true, allocator);
        buffer.AddComponent(first, true).AddComponent(second, true);
        try
        {
            buffer.ReaderIndex = 1;
            Assert.Throws<OutOfMemoryException>(() => buffer.Consolidate());
            Assert.Equal(2, buffer.NumComponents);
            Assert.Equal(1, first.ReferenceCount);
            Assert.Equal(1, second.ReferenceCount);
            Assert.Equal(1, buffer.ReaderIndex);
            Assert.Equal(4, buffer.WriterIndex);
            Assert.Equal(new byte[] { 2, 3, 4 }, Bytes(buffer));
            Assert.Equal(4, allocator.ReservedBytes);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void FailedAutoConsolidationKeepsTheInsertedComponentOwned()
    {
        var allocator = new NativeMemoryAllocator(4);
        ByteBuf first = Segment(new byte[] { 1, 2 }, true, allocator);
        ByteBuf second = Segment(new byte[] { 3, 4 }, true, allocator);
        CompositeByteBuf buffer = new(1, true, allocator);
        buffer.AddComponent(first, true);
        try
        {
            Assert.Throws<OutOfMemoryException>(() => buffer.AddComponent(second, true));
            Assert.Equal(2, buffer.NumComponents);
            Assert.Equal(4, buffer.WriterIndex);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, Bytes(buffer));
        }
        finally { buffer.Release(); }
        Assert.Equal(0, first.ReferenceCount);
        Assert.Equal(0, second.ReferenceCount);
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShrinkDropsOwnedTailComponentsAndGrowthAddsWritablePadding(bool direct)
    {
        var allocator = new NativeMemoryAllocator(1024);
        ByteBuf first = Segment(new byte[] { 1, 2 }, direct, allocator);
        ByteBuf second = Segment(new byte[] { 3, 4 }, direct, allocator);
        CompositeByteBuf buffer = new(16, direct, allocator);
        // composite takes ownership of b1 and b2
        buffer.AddComponent(first, true).AddComponent(second, true);
        try
        {
            buffer.ReaderIndex = 3;
            // reduce capacity down to two, will drop the second component
            buffer.Capacity = 2;
            Assert.Equal(0, second.ReferenceCount);
            Assert.Equal(2, buffer.ReaderIndex);
            Assert.Equal(2, buffer.WriterIndex);
            Assert.Equal(1, buffer.NumComponents);
            buffer.Capacity = 1;
            Assert.Equal(1, first.ReferenceCount);
            Assert.Equal(1, buffer.Capacity);
            Assert.Equal(2, first.Capacity);
            buffer.Clear(); buffer.WriterIndex = 1;
            buffer.Capacity = 8;
            Assert.Equal(1, buffer.WriterIndex);
            Assert.Equal(direct, buffer.IsDirect);
            buffer.WriteIntLE(0x05040302);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, Bytes(buffer));
            buffer.Capacity = 0;
            Assert.Equal(0, buffer.NumComponents);
            Assert.Equal(0, first.ReferenceCount);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void ExplicitRemovalReleasesOwnershipAndLeavesIndicesUnchanged()
    {
        ByteBuf first = Unpooled.WrappedBuffer(new byte[] { 1, 2 });
        CompositeByteBuf buffer = new();
        buffer.AddComponent(first, true).AddComponent(Unpooled.WrappedBuffer(new byte[] { 3, 4 }), true);
        try
        {
            buffer.RemoveComponent(0);
            Assert.Equal(0, first.ReferenceCount);
            Assert.Equal(2, buffer.Capacity);
            Assert.Equal(4, buffer.WriterIndex);
            buffer.SetIndex(0, 2);
            Assert.Equal(new byte[] { 3, 4 }, Bytes(buffer));
            buffer.RemoveComponents(1, 0);
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiscardReadComponentsKeepsPartialComponentAndDiscardBytesTrimsWithoutCopy(bool direct)
    {
        var allocator = new NativeMemoryAllocator(1024);
        ByteBuf first = Segment(new byte[] { 9, 1, 2 }, direct, allocator).SetIndex(1, 3);
        ByteBuf second = Segment(new byte[] { 8, 8, 3, 4, 5 }, direct, allocator).SetIndex(2, 5);
        CompositeByteBuf buffer = new();
        buffer.AddComponent(first, true).AddComponent(second, true);
        try
        {
            buffer.ReaderIndex = 3; buffer.MarkReaderIndex(); buffer.MarkWriterIndex();
            buffer.DiscardSomeReadBytes();
            Assert.Equal(0, first.ReferenceCount);
            Assert.Equal(1, buffer.ReaderIndex);
            Assert.Equal(3, buffer.WriterIndex);
            Assert.Equal(3, buffer.Capacity);
            Assert.Equal(new byte[] { 4, 5 }, Bytes(buffer));
            buffer.ResetReaderIndex(); Assert.Equal(1, buffer.ReaderIndex);
            buffer.ResetWriterIndex(); Assert.Equal(3, buffer.WriterIndex);
            buffer.DiscardReadBytes();
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(2, buffer.Capacity);
            Assert.Equal(2, buffer.WriterIndex);
            second.SetByte(3, 7);
            Assert.Equal(new byte[] { 7, 5 }, Bytes(buffer));
            buffer.ResetReaderIndex(); Assert.Equal(0, buffer.ReaderIndex);
            buffer.ResetWriterIndex(); Assert.Equal(2, buffer.WriterIndex);
            buffer.ReaderIndex = 2;
            buffer.DiscardReadComponents();
            Assert.Equal(0, buffer.Capacity);
            Assert.Equal(0, second.ReferenceCount);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void DecomposeReturnsBorrowedOriginalSlicesWithIndependentIndices()
    {
        CompositeByteBuf buffer = new();
        buffer.AddComponent(Unpooled.WrappedBuffer(new byte[] { 9, 1, 2 }).SetIndex(1, 3), true)
            .AddComponent(Unpooled.EmptyBuffer).AddComponent(Unpooled.WrappedBuffer(new byte[] { 3, 4, 5 }), true);
        try
        {
            IReadOnlyList<ByteBuf> slices = buffer.Decompose(1, 3);
            Assert.Equal(3, slices.Count); // Includes the zero-length component between the byte ranges.
            Assert.Equal(new byte[] { 2, 3, 4 }, slices.SelectMany(Bytes).ToArray());
            Assert.All(slices, slice => Assert.Equal(0, slice.ReaderIndex));
            slices[2].SetByte(0, 7);
            Assert.Equal(7, buffer.GetByte(2));
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Empty(buffer.Decompose(5, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Decompose(4, 2));
        }
        finally { buffer.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CrossComponentTransfersPreserveOverlapAndSourceDestinationIndices(bool direct)
    {
        var allocator = new NativeMemoryAllocator(1024);
        CompositeByteBuf buffer = new();
        buffer.AddComponent(Segment(new byte[] { 1, 2, 3 }, direct, allocator), true)
            .AddComponent(Segment(new byte[] { 4, 5, 6 }, direct, allocator), true);
        ByteBuf destination = Unpooled.Buffer(6);
        try
        {
            buffer.SetBytes(1, buffer, 0, 5);
            Assert.Equal(new byte[] { 1, 1, 2, 3, 4, 5 }, Bytes(buffer));
            buffer.SetBytes(0, buffer.AsReadOnlySpan(3, 3));
            Assert.Equal(new byte[] { 3, 4, 5, 3, 4, 5 }, Bytes(buffer));
            buffer.GetBytes(0, destination.AsSpan(0, 6));
            destination.WriterIndex = 6;
            Assert.Equal(Bytes(buffer), Bytes(destination));
            destination.Clear(); buffer.ReadBytes(destination, 4);
            Assert.Equal(4, buffer.ReaderIndex); Assert.Equal(4, destination.WriterIndex);
            Assert.Equal(new byte[] { 3, 4, 5, 3 }, Bytes(destination));
            buffer.SetZero(2, 3);
            Assert.Equal(new byte[] { 3, 4, 0, 0, 0, 5 }, buffer.AsReadOnlySequence(0, 6).ToArray());
            buffer.ReaderIndex = 0;
            ByteBuf copied = buffer.ReadBytes(6);
            try { Assert.Equal(Bytes(destination)[0], copied.GetByte(0)); Assert.Equal(6, buffer.ReaderIndex); }
            finally { copied.Release(); }
        }
        finally { destination.Release(); buffer.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EncodingAndSearchHandleMultibyteCharactersSplitBetweenSegments(bool direct)
    {
        var allocator = new NativeMemoryAllocator(1024);
        CompositeByteBuf buffer = new(64);
        for (int i = 0; i < 24; ++i) buffer.AddComponent(Segment(new byte[1], direct, allocator));
        ByteBuf needle = Unpooled.WrappedBuffer(new byte[] { 0xe2, 0x82, 0xac });
        try
        {
            int written = buffer.WriteString("a€😀z", Encoding.UTF8);
            Assert.Equal(9, written);
            Assert.Equal(new byte[] { 0x61, 0xe2, 0x82, 0xac, 0xf0, 0x9f, 0x98, 0x80, 0x7a }, Bytes(buffer));
            Assert.Equal("a€😀z", buffer.AsReadOnly().GetString(Encoding.UTF8));
            Assert.Equal(1, ByteBufUtil.IndexOf(needle, buffer));
            Assert.Equal(8, buffer.IndexOf(0, 9, (byte)'z'));
            Assert.Equal(0, buffer.IndexOf(9, 0, (byte)'a'));
            Assert.Equal(1, buffer.BytesBefore((byte)0xe2));
            Assert.Equal(4, buffer.ForEachByte(value => value != 0xf0));
            Assert.Equal(3, buffer.ForEachByteDesc(value => value != 0xac));
            buffer.Clear();
            Assert.Equal(5, ByteBufUtil.WriteUtf8(buffer, new char[] { '\ud800', 'a', '€' }));
            Assert.Equal(new byte[] { 0x3f, 0x61, 0xe2, 0x82, 0xac }, Bytes(buffer));
            buffer.Clear();
            Assert.Equal(3, ByteBufUtil.WriteAscii(buffer, "aé€"));
            Assert.Equal(new byte[] { 0x61, 0xe9, 0x3f }, Bytes(buffer));
        }
        finally { needle.Release(); buffer.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void NativeAliasedSpanSurvivesGrowthAndAutomaticConsolidation()
    {
        var allocator = new NativeMemoryAllocator(128);
        ByteBuf component = Segment(new byte[] { 1, 2, 3, 4 }, true, allocator);
        CompositeByteBuf buffer = new(1, true, allocator);
        buffer.AddComponent(component, true);
        try
        {
            buffer.WriteBytes(buffer.AsReadOnlySpan(0, 4));
            Assert.Equal(0, component.ReferenceCount);
            Assert.Equal(1, buffer.NumComponents);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 1, 2, 3, 4 }, Bytes(buffer));
            Assert.Equal(64, allocator.ReservedBytes);
        }
        finally { buffer.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void MixedReadOnlyComponentsRemainReadableAndConsolidationMakesIndependentWritableStorage()
    {
        var allocator = new NativeMemoryAllocator(128);
        ByteBuf first = Segment(new byte[] { 1, 2 }, true, allocator).AsReadOnly();
        CompositeByteBuf buffer = new();
        buffer.AddComponent(first, true).AddComponent(Unpooled.WrappedBuffer(new byte[] { 3, 4 }), true);
        try
        {
            Assert.False(buffer.IsReadOnly);
            Assert.False(buffer.IsDirect);
            Assert.Equal(0x01020304, buffer.GetInt(0));
            Assert.Throws<NotSupportedException>(() => buffer.SetByte(0, 9));
            Assert.Throws<NotSupportedException>(() => buffer.AsMemory(0, 1));
            buffer.SetByte(3, 9);
            buffer.Consolidate();
            buffer.SetInt(0, 0x05060708);
            Assert.Equal(new byte[] { 5, 6, 7, 8 }, Bytes(buffer));
            Assert.Equal(0, first.ReferenceCount);
            Assert.Equal(0, allocator.ReservedBytes);
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void InvalidAddReleasesIncomingOwnershipAndCyclesAreRejectedBeforeTransfer()
    {
        CompositeByteBuf buffer = new(), parent = new();
        ByteBuf incoming = Unpooled.Buffer(1).WriteByte(1);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.AddComponent(1, incoming));
            Assert.Equal(0, incoming.ReferenceCount);
            Assert.Throws<ArgumentNullException>(() => buffer.AddComponent(null));
            Assert.Throws<ArgumentException>(() => buffer.AddComponent(buffer));
            Assert.Equal(1, buffer.ReferenceCount);
            parent.AddComponent(buffer.Retain());
            Assert.Throws<ArgumentException>(() => buffer.AddComponent(parent));
            Assert.Equal(1, parent.ReferenceCount);
        }
        finally { parent.Release(); buffer.Release(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RandomLayoutsAgreeWithIndependentFlatWireBytes(bool direct)
    {
        var random = new Random(0x16799);
        var allocator = new NativeMemoryAllocator(4096);
        for (int iteration = 0; iteration < 100; ++iteration)
        {
            byte[] expected = new byte[64]; random.NextBytes(expected);
            CompositeByteBuf buffer = new(128);
            try
            {
                int offset = 0;
                while (offset < expected.Length)
                {
                    int length = Math.Min(random.Next(1, 9), expected.Length - offset);
                    byte[] padded = new byte[length + 2];
                    expected.AsSpan(offset, length).CopyTo(padded.AsSpan(1));
                    buffer.AddComponent(Segment(padded, direct, allocator).SetIndex(1, length + 1), true);
                    if (random.Next(3) == 0) buffer.AddComponent(Unpooled.EmptyBuffer);
                    offset += length;
                }
                for (int index = 0; index <= expected.Length - 8; ++index)
                {
                    ulong big = 0, little = 0;
                    for (int i = 0; i < 8; ++i)
                    { big = (big << 8) | expected[index + i]; little |= (ulong)expected[index + i] << (i * 8); }
                    Assert.Equal(unchecked((long)big), buffer.GetLong(index));
                    Assert.Equal(unchecked((long)little), buffer.GetLongLE(index));
                }
                buffer.SetLong(17, unchecked((long)0x89abcdef01234567));
                new byte[] { 0x89, 0xab, 0xcd, 0xef, 1, 0x23, 0x45, 0x67 }.CopyTo(expected, 17);
                Assert.Equal(expected, Bytes(buffer));
                int consumed = random.Next(1, 63);
                buffer.ReaderIndex = consumed;
                buffer.DiscardReadBytes();
                Assert.Equal(expected[consumed..], Bytes(buffer));
                Assert.Equal(expected.Length - consumed, buffer.Capacity);
                buffer.Consolidate();
                Assert.Equal(expected[consumed..], Bytes(buffer));
                Assert.Equal(1, buffer.NumComponents);
            }
            finally { buffer.Release(); }
            Assert.Equal(0, allocator.ReservedBytes);
        }
    }

    [Fact]
    public void NativePinsOutliveComponentRemovalAndFinalCompositeRelease()
    {
        var allocator = new NativeMemoryAllocator(8);
        CompositeByteBuf buffer = new();
        buffer.AddComponent(Segment(new byte[] { 1, 2 }, true, allocator), true)
            .AddComponent(Segment(new byte[] { 3, 4 }, true, allocator), true);
        ReadOnlyMemory<byte> saved = buffer.AsReadOnlyMemory(0, 2);
        using (MemoryHandle pin = saved.Pin())
        {
            buffer.RemoveComponent(0);
            Assert.Equal(4, allocator.ReservedBytes);
            buffer.Release();
            Assert.Equal(2, allocator.ReservedBytes);
            Assert.Throws<ObjectDisposedException>(() => saved.Span.ToArray());
            unsafe { Assert.Equal((byte)1, *(byte*)pin.Pointer); }
        }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void FailedGroupLeaseReleasesEarlierPins()
    {
        var allocator = new NativeMemoryAllocator(8);
        ByteBuf first = Segment(new byte[] { 1, 2 }, true, allocator);
        ByteBuf second = Segment(new byte[] { 3, 4 }, true, allocator);
        CompositeByteBuf buffer = new();
        buffer.AddComponent(first, true).AddComponent(second, true);
        // Deliberate ownership misuse makes the second component inaccessible;
        // acquiring a group must still release the first component's temporary pin.
        second.Release();
        Assert.Throws<IllegalReferenceCountException>(() => buffer.WriteBytes(new byte[] { 1 }));
        buffer.RemoveComponent(0);
        Assert.Equal(0, allocator.ReservedBytes);
        Assert.Throws<IllegalReferenceCountException>(() => buffer.Release());
        Assert.Equal(0, buffer.ReferenceCount);
    }

    [Fact]
    public void CapacityOverflowReleasesRejectedReferenceWithoutAllocatingGigabytes()
    {
        const int capacity = 1024 * 1024; // 1MB
        ByteBuf source = Unpooled.Buffer(capacity).WriteZero(capacity);
        CompositeByteBuf buffer = new(int.MaxValue);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                for (int i = 0; i < 2048; ++i) buffer.AddComponent(source.RetainedDuplicate());
            });
            Assert.Equal(2047, buffer.NumComponents);
            Assert.Equal(2047 * capacity, buffer.Capacity);
            Assert.Equal(2048, source.ReferenceCount);
            Assert.Equal(0, buffer.WriterIndex);
        }
        finally { buffer.Release(); source.Release(); }
        Assert.Equal(0, source.ReferenceCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NativeAliasedTextSurvivesConsolidatingReadOnlyAndWritableComponents(int encodingKind)
    {
        var allocator = new NativeMemoryAllocator(128);
        ByteBuf source = new UnpooledDirectByteBuf(4, allocator: allocator);
        source.WriteShortLE('A').WriteShortLE('€');
        ReadOnlySpan<char> text = MemoryMarshal.Cast<byte, char>(source.AsSpan(0, 4));
        CompositeByteBuf buffer = new(3, true, allocator);
        buffer.AddComponent(source.AsReadOnly(), true)
            .AddComponent(Segment(new byte[] { 1 }, true, allocator), true)
            .AddComponent(Segment(new byte[] { 2 }, true, allocator), true);
        try
        {
            int written = encodingKind switch
            {
                0 => buffer.WriteString(text, Encoding.UTF8),
                1 => ByteBufUtil.WriteUtf8(buffer, text),
                _ => ByteBufUtil.WriteAscii(buffer, text)
            };
            Assert.Equal(encodingKind == 2 ? 2 : 4, written);
            Assert.Equal(0, source.ReferenceCount);
            Assert.Equal(1, buffer.NumComponents);
            Assert.Equal(64, allocator.ReservedBytes);
            byte[] expected = encodingKind == 2
                ? new byte[] { 0x41, 0, 0xac, 0x20, 1, 2, 0x41, 0x3f }
                : new byte[] { 0x41, 0, 0xac, 0x20, 1, 2, 0x41, 0xe2, 0x82, 0xac };
            Assert.Equal(expected, Bytes(buffer));
        }
        finally { buffer.Release(); }
        Assert.Equal(0, allocator.ReservedBytes);
    }

    [Fact]
    public void RetainingCompositeSliceDoesNotRetainRemovedComponentsOrExposeFreedStorage()
    {
        CompositeByteBuf buffer = new();
        ByteBuf component = Unpooled.Buffer(8).WriteZero(8);
        buffer.AddComponent(component, true);
        ByteBuf slice = buffer.RetainedSlice();
        try
        {
            buffer.SkipBytes(8).DiscardSomeReadBytes();
            Assert.Equal(2, buffer.ReferenceCount);
            Assert.Equal(0, component.ReferenceCount);
            Assert.Throws<ArgumentOutOfRangeException>(() => slice.ReadByte());
            Assert.Equal(0, slice.ReaderIndex);
        }
        finally { slice.Release(); buffer.Release(); }
    }
}
