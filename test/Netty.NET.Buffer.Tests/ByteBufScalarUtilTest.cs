/*
 * Copyright 2014 The Netty Project
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
using System.Linq;
using System.Text;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class ByteBufScalarUtilTest
{
    private static readonly int[] Values = { 0, 1, -1, int.MinValue, int.MaxValue, 0x1234, 0x123456, 0x12345678, 0x80, 0x8000, 0x800000, 0x123456ff };

    private static ByteBuf Storage(int kind, int size)
    {
        return kind switch
        {
            0 => Unpooled.Buffer(size, size),
            1 => Unpooled.DirectBuffer(size, size),
            2 => Unpooled.CompositeBuffer().AddComponent(Unpooled.WrappedBuffer(new byte[1]), true)
                .AddComponent(Unpooled.WrappedBuffer(new byte[size - 1]), true).Clear(),
            3 => Unpooled.Buffer(size + 2, size + 2).Slice(1, size).Clear(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    [Fact]
    public void SwapShortCoversAllBitPatterns()
    {
        for (int bits = 0; bits <= ushort.MaxValue; bits++)
        {
            short value = unchecked((short)bits);
            short expected = unchecked((short)((bits % 256) * 256 + bits / 256));
            Assert.Equal(expected, ByteBufUtil.SwapShort(value));
            Assert.Equal(value, ByteBufUtil.SwapShort(ByteBufUtil.SwapShort(value)));
        }
    }

    [Fact]
    public void SwapWordsPreserveBitsAndMediumTruncatesAndSignExtends()
    {
        foreach (int value in Values)
        {
            byte[] bytes = BitConverter.GetBytes(value); Array.Reverse(bytes);
            Assert.Equal(BitConverter.ToInt32(bytes), ByteBufUtil.SwapInt(value));
            Assert.Equal(value, ByteBufUtil.SwapInt(ByteBufUtil.SwapInt(value)));
            long wide = unchecked((long)(((ulong)(uint)value << 32) | (uint)~value));
            bytes = BitConverter.GetBytes(wide); Array.Reverse(bytes);
            Assert.Equal(BitConverter.ToInt64(bytes), ByteBufUtil.SwapLong(wide));
            Assert.Equal(wide, ByteBufUtil.SwapLong(ByteBufUtil.SwapLong(wide)));
            int medium = (int)ReverseWidth(value, 3);
            Assert.Equal(medium >= 0x800000 ? medium - 0x1000000 : medium, ByteBufUtil.SwapMedium(value));
            int low24 = value & 0xffffff;
            Assert.Equal(low24 >= 0x800000 ? low24 - 0x1000000 : low24, ByteBufUtil.SwapMedium(ByteBufUtil.SwapMedium(value)));
        }
        foreach (long value in new[] { long.MinValue, long.MaxValue, -1L, 0L, 1L })
        { byte[] bytes = BitConverter.GetBytes(value); Array.Reverse(bytes); Assert.Equal(BitConverter.ToInt64(bytes), ByteBufUtil.SwapLong(value)); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void OriginalWriteShortSetShortAndWriteMediumScenariosWithTruncation(int kind)
    {
        ByteBuf b = Storage(kind, 12);
        try
        {
            foreach (int value in Values)
            {
                b.Clear().WriteByte(99).MarkReaderIndex().MarkWriterIndex();
                Assert.Same(b, ByteBufUtil.WriteShortBE(b, value));
                Assert.Same(b, ByteBufUtil.WriteMediumBE(b, value));
                Assert.Equal(0, b.ReaderIndex); Assert.Equal(6, b.WriterIndex);
                Assert.Equal(99, b.GetByte(0));
                Assert.Equal(Octets(value, 2).Concat(Octets(value, 3)), ByteBufUtil.GetBytes(b, 1, 5));
                Assert.Equal(unchecked((short)value), b.GetShort(1));
                Assert.Equal(unchecked((short)ReverseWidth(value, 2)), b.GetShortLE(1));
                Assert.Same(b, ByteBufUtil.SetShortBE(b, 9, value));
                Assert.Equal(Octets(value, 2), ByteBufUtil.GetBytes(b, 9, 2));
                Assert.Equal(6, b.WriterIndex); Assert.Equal(0, b.ReaderIndex);
                b.ResetReaderIndex().ResetWriterIndex(); Assert.Equal(0, b.ReaderIndex); Assert.Equal(1, b.WriterIndex);
            }
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void OriginalUnsignedShortAndIntReadScenariosUseExplicitLeOperations(int kind)
    {
        ByteBuf b = Storage(kind, 8);
        try
        {
            int shortValue = 0x1234; // unsigned short
            int swappedShortValue = 0x3412; // swapped version of the value above
            b.WriteShort(shortValue).WriteShortLE(shortValue);
            Assert.Equal(shortValue, ByteBufUtil.ReadUnsignedShortBE(b));
            Assert.Equal(swappedShortValue, ByteBufUtil.ReadUnsignedShortBE(b));
            shortValue = 0xfedc; // unsigned short
            swappedShortValue = 0xdcfe; // swapped version of the value above
            b.Clear().WriteShort(shortValue).WriteShortLE(shortValue);
            Assert.Equal(shortValue, ByteBufUtil.ReadUnsignedShortBE(b));
            Assert.Equal(swappedShortValue, ByteBufUtil.ReadUnsignedShortBE(b));
            foreach (int value in Values)
            {
                b.Clear().WriteInt(value).WriteIntLE(value).MarkReaderIndex().MarkWriterIndex();
                Assert.Equal(value, ByteBufUtil.ReadIntBE(b));
                Assert.Equal(unchecked((int)ReverseWidth(value, 4)), ByteBufUtil.ReadIntBE(b));
                Assert.Equal(8, b.ReaderIndex); Assert.Equal(8, b.WriterIndex);
                b.ResetReaderIndex().ResetWriterIndex(); Assert.Equal(0, b.ReaderIndex); Assert.Equal(8, b.WriterIndex);
                ByteBuf readOnly = b.AsReadOnly();
                Assert.Equal(value, ByteBufUtil.ReadIntBE(readOnly)); Assert.Equal(0, b.ReaderIndex);
            }
        }
        finally { b.Release(); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void FailurePathsPreserveBytesIndicesAndReferenceCount(int kind)
    {
        ByteBuf b = Storage(kind, 4).Slice(0, 4).Clear().WriteInt(0x01020304);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.WriteShortBE(b, 7));
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.WriteMediumBE(b, 7));
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.SetShortBE(b, -1, 7));
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.SetShortBE(b, int.MaxValue, 7));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, ByteBufUtil.GetBytes(b));
            b.ReaderIndex = 3;
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.ReadUnsignedShortBE(b));
            Assert.Throws<ArgumentOutOfRangeException>(() => ByteBufUtil.ReadIntBE(b));
            Assert.Equal(3, b.ReaderIndex); Assert.Equal(4, b.WriterIndex); Assert.Equal(1, b.ReferenceCount);
            ByteBuf readOnly = b.AsReadOnly();
            Assert.Throws<NotSupportedException>(() => ByteBufUtil.SetShortBE(readOnly, 0, 7));
            Assert.Throws<NotSupportedException>(() => ByteBufUtil.WriteShortBE(readOnly, 7));
            Assert.Throws<NotSupportedException>(() => ByteBufUtil.WriteMediumBE(readOnly, 7));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, ByteBufUtil.GetBytes(b, 0, 4));
        }
        finally { b.Release(); }
        Assert.Throws<IllegalReferenceCountException>(() => ByteBufUtil.SetShortBE(b, 0, 1));
        Assert.Throws<IllegalReferenceCountException>(() => ByteBufUtil.ReadIntBE(b));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void AccessibilityChecksObserveSharedOwnershipWithoutRetaining(int kind)
    {
        ByteBuf b = Storage(kind, 4); ByteBuf retained = b.RetainedSlice(0, 4);
        ByteBuf[] views = { b, retained, b.Duplicate(), b.AsReadOnly(), Unpooled.UnreleasableBuffer(b) };
        foreach (ByteBuf view in views) { Assert.True(ByteBufUtil.IsAccessible(view)); Assert.Same(view, ByteBufUtil.EnsureAccessible(view)); }
        Assert.Equal(2, b.ReferenceCount); b.Release(); Assert.True(ByteBufUtil.IsAccessible(b)); retained.Release();
        foreach (ByteBuf view in views)
        {
            Assert.False(ByteBufUtil.IsAccessible(view));
            Assert.Throws<IllegalReferenceCountException>(() => ByteBufUtil.EnsureAccessible(view));
        }
        Assert.True(ByteBufUtil.IsAccessible(Unpooled.EmptyBuffer));
        Assert.Same(Unpooled.EmptyBuffer, ByteBufUtil.EnsureAccessible(Unpooled.EmptyBuffer));
    }

    [Fact]
    public void WritableStatusMatchesActualGrowthOutcomesAndRejectsUnknownCodes()
    {
        var a = new UnpooledByteBufAllocator(); ByteBuf b = a.HeapBuffer(2, 8);
        try
        {
            Assert.Equal(0, b.EnsureWritable(2, false)); Assert.True(ByteBufUtil.EnsureWritableSuccess(0));
            Assert.Equal(1, b.EnsureWritable(9, false)); Assert.False(ByteBufUtil.EnsureWritableSuccess(1));
            Assert.Equal(2, b.EnsureWritable(3, false)); Assert.True(ByteBufUtil.EnsureWritableSuccess(2));
        }
        finally { b.Release(); }
        b = a.HeapBuffer(2, 8);
        try
        {
            Assert.Equal(3, b.EnsureWritable(9, true)); Assert.False(ByteBufUtil.EnsureWritableSuccess(3));
            Assert.Equal(8, b.Capacity); Assert.Equal(0, b.WriterIndex);
        }
        finally { b.Release(); }
        foreach (int code in new[] { int.MinValue, -1, 4, int.MaxValue }) Assert.False(ByteBufUtil.EnsureWritableSuccess(code));
    }

    [Fact]
    public void HttpDelimiterConsumerWritesWireOrderAcrossCompositeBoundaries()
    {
        ByteBuf b = Storage(2, 8);
        try
        {
            ByteBufUtil.WriteShortBE(b, 0x0d0a); ByteBufUtil.WriteMediumBE(b, 0x300d0a);
            Assert.Equal(Encoding.ASCII.GetBytes("\r\n0\r\n"), ByteBufUtil.GetBytes(b));
        }
        finally { b.Release(); }
    }

    [Fact]
    public void NullBufferArgumentsFailExplicitly()
    {
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.IsAccessible(null));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.EnsureAccessible(null));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.WriteShortBE(null, 1));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.SetShortBE(null, 0, 1));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.WriteMediumBE(null, 1));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.ReadUnsignedShortBE(null));
        Assert.Throws<ArgumentNullException>(() => ByteBufUtil.ReadIntBE(null));
    }

    private static byte[] Octets(int value, int width)
        => Enumerable.Range(0, width).Select(i => unchecked((byte)(value >> (8 * (width - i - 1))))).ToArray();

    private static uint ReverseWidth(int value, int width)
    {
        uint result = 0;
        foreach (byte octet in Octets(value, width)) result = (result >> 8) | ((uint)octet << (8 * (width - 1)));
        return result;
    }
}
