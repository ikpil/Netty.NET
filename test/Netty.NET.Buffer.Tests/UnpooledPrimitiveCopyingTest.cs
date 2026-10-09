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

namespace Netty.NET.Buffer.Tests;

// Selected pinned UnpooledTest primitive factories and CLR Span/wire/lifetime contracts.
public class UnpooledPrimitiveCopyingTest
{
    private static void CheckOwnedCopy(ByteBuf buffer, string hex)
    {
        byte[] expected = Convert.FromHexString(hex);
        try
        {
            Assert.IsType<UnpooledHeapByteBuf>(buffer);
            Assert.False(buffer.IsReadOnly);
            Assert.Equal(expected.Length, buffer.Capacity);
            Assert.Equal(int.MaxValue, buffer.MaxCapacity);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(expected.Length, buffer.WriterIndex);
            Assert.Equal(1, buffer.ReferenceCount);
            Assert.Equal(expected, buffer.ReadableMemory.ToArray());
            buffer.WriteByte(0x7f);
            Assert.Equal(expected.Length + 1, buffer.WriterIndex);
            Assert.Equal(expected, buffer.AsMemory(0, expected.Length).ToArray());
            Assert.Equal(0x7f, buffer.GetByte(expected.Length));
        }
        finally { Assert.True(buffer.Release()); }
        Assert.Equal(0, buffer.ReferenceCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void OriginalSingleValueFactoriesRoundtripAndExhaustTheReadableRange(int kind)
    {
        ByteBuf copy = kind switch
        {
            0 => Unpooled.CopyInt(42), 1 => Unpooled.CopyShort(42), 2 => Unpooled.CopyMedium(42),
            3 => Unpooled.CopyLong(42), 4 => Unpooled.CopyBoolean(true),
            5 => Unpooled.CopyFloat(42), _ => Unpooled.CopyDouble(42)
        };
        try
        {
            Assert.Equal(kind switch { 1 => 2, 2 => 3, 3 or 6 => 8, 4 => 1, _ => 4 }, copy.Capacity);
            double actual = kind switch
            {
                0 => copy.ReadInt(), 1 => copy.ReadShort(), 2 => copy.ReadMedium(), 3 => copy.ReadLong(),
                4 => copy.ReadBoolean() ? 1 : 0, 5 => copy.ReadFloat(), _ => copy.ReadDouble()
            };
            Assert.Equal(kind == 4 ? 1 : 42, actual);
            Assert.False(copy.IsReadable);
        }
        finally { copy.Release(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void OriginalSequenceFactoriesRoundtripInOrder(int kind)
    {
        ByteBuf copy = kind switch
        {
            0 => Unpooled.CopyInt(1, 4), 1 => Unpooled.CopyShort(new short[] { 1, 4 }),
            2 => Unpooled.CopyShort(1, 4), 3 => Unpooled.CopyMedium(1, 4),
            4 => Unpooled.CopyLong(1, 4), 5 => Unpooled.CopyBoolean(true, false),
            6 => Unpooled.CopyFloat(1, 4), _ => Unpooled.CopyDouble(1, 4)
        };
        try
        {
            Assert.Equal(kind switch { 1 or 2 => 4, 3 => 6, 4 or 7 => 16, 5 => 2, _ => 8 }, copy.Capacity);
            for (int i = 0; i < 2; ++i)
            {
                double actual = kind switch
                {
                    0 => copy.ReadInt(), 1 or 2 => copy.ReadShort(), 3 => copy.ReadMedium(),
                    4 => copy.ReadLong(), 5 => copy.ReadBoolean() ? 1 : 0,
                    6 => copy.ReadFloat(), _ => copy.ReadDouble()
                };
                Assert.Equal(kind == 5 ? (i == 0 ? 1 : 0) : (i == 0 ? 1 : 4), actual);
            }
            Assert.False(copy.IsReadable);
        }
        finally { copy.Release(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void NullArraysEmptyArraysAndEmptySpansUseTheSharedSentinel(int kind)
    {
        ByteBuf fromNull = kind switch
        {
            0 => Unpooled.CopyInt(null), 1 => Unpooled.CopyShort((short[])null),
            2 => Unpooled.CopyShort((int[])null), 3 => Unpooled.CopyMedium(null),
            4 => Unpooled.CopyLong(null), 5 => Unpooled.CopyBoolean(null),
            6 => Unpooled.CopyFloat(null), _ => Unpooled.CopyDouble(null)
        };
        ByteBuf fromEmpty = kind switch
        {
            0 => Unpooled.CopyInt(Array.Empty<int>()), 1 => Unpooled.CopyShort(Array.Empty<short>()),
            2 => Unpooled.CopyShort(Array.Empty<int>()), 3 => Unpooled.CopyMedium(Array.Empty<int>()),
            4 => Unpooled.CopyLong(Array.Empty<long>()), 5 => Unpooled.CopyBoolean(Array.Empty<bool>()),
            6 => Unpooled.CopyFloat(Array.Empty<float>()), _ => Unpooled.CopyDouble(Array.Empty<double>())
        };
        ByteBuf fromSpan = kind switch
        {
            0 => Unpooled.CopyInt(ReadOnlySpan<int>.Empty), 1 => Unpooled.CopyShort(ReadOnlySpan<short>.Empty),
            2 => Unpooled.CopyShort(ReadOnlySpan<int>.Empty), 3 => Unpooled.CopyMedium(ReadOnlySpan<int>.Empty),
            4 => Unpooled.CopyLong(ReadOnlySpan<long>.Empty), 5 => Unpooled.CopyBoolean(ReadOnlySpan<bool>.Empty),
            6 => Unpooled.CopyFloat(ReadOnlySpan<float>.Empty), _ => Unpooled.CopyDouble(ReadOnlySpan<double>.Empty)
        };
        ByteBuf fromNoArguments = kind switch
        {
            0 => Unpooled.CopyInt(), 1 or 2 => Unpooled.CopyShort(), 3 => Unpooled.CopyMedium(),
            4 => Unpooled.CopyLong(), 5 => Unpooled.CopyBoolean(),
            6 => Unpooled.CopyFloat(), _ => Unpooled.CopyDouble()
        };
        foreach (ByteBuf result in new[] { fromNull, fromEmpty, fromSpan, fromNoArguments })
        {
            Assert.Same(Unpooled.EmptyBuffer, result);
            Assert.Equal(0, result.Capacity);
            Assert.Equal(0, result.MaxCapacity);
            Assert.False(result.Release());
            Assert.Equal(1, result.ReferenceCount);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void ArrayAndSpanCopiesHaveGoldenWireBytesAndIndependentStorage(int kind)
    {
        int[] ints = { int.MinValue, -1, 0, int.MaxValue };
        short[] shorts = { short.MinValue, -1, 0, short.MaxValue };
        long[] longs = { long.MinValue, -1, 0, long.MaxValue };
        bool[] booleans = { true, false, true };
        float[] floats = { 1f, -0f, float.PositiveInfinity };
        double[] doubles = { 1d, -0d, double.PositiveInfinity };
        ByteBuf arrayCopy = kind switch
        {
            0 => Unpooled.CopyInt(ints), 1 => Unpooled.CopyShort(shorts), 2 => Unpooled.CopyShort(ints),
            3 => Unpooled.CopyMedium(ints), 4 => Unpooled.CopyLong(longs),
            5 => Unpooled.CopyBoolean(booleans), 6 => Unpooled.CopyFloat(floats), _ => Unpooled.CopyDouble(doubles)
        };
        ByteBuf spanCopy = kind switch
        {
            0 => Unpooled.CopyInt(ints.AsSpan()), 1 => Unpooled.CopyShort(shorts.AsSpan()),
            2 => Unpooled.CopyShort(ints.AsSpan()), 3 => Unpooled.CopyMedium(ints.AsSpan()),
            4 => Unpooled.CopyLong(longs.AsSpan()), 5 => Unpooled.CopyBoolean(booleans.AsSpan()),
            6 => Unpooled.CopyFloat(floats.AsSpan()), _ => Unpooled.CopyDouble(doubles.AsSpan())
        };
        string hex = kind switch
        {
            0 => "80000000FFFFFFFF000000007FFFFFFF", 1 => "8000FFFF00007FFF", 2 => "0000FFFF0000FFFF",
            3 => "000000FFFFFF000000FFFFFF", 4 => "8000000000000000FFFFFFFFFFFFFFFF00000000000000007FFFFFFFFFFFFFFF",
            5 => "010001", 6 => "3F800000800000007F800000", _ => "3FF000000000000080000000000000007FF0000000000000"
        };
        Array.Clear(ints); Array.Clear(shorts); Array.Clear(longs);
        Array.Clear(booleans); Array.Clear(floats); Array.Clear(doubles);
        try
        {
            arrayCopy.SetByte(0, 0x55);
            Assert.Equal(Convert.FromHexString(hex), spanCopy.ReadableMemory.ToArray());
            arrayCopy.SetByte(0, Convert.FromHexString(hex)[0]);
            ByteBuf first = arrayCopy;
            arrayCopy = null;
            CheckOwnedCopy(first, hex);
            ByteBuf second = spanCopy;
            spanCopy = null;
            CheckOwnedCopy(second, hex);
        }
        finally { arrayCopy?.Release(); spanCopy?.Release(); }
    }

    [Theory]
    [InlineData(0x12345678, "5678", "345678")]
    [InlineData(-1, "FFFF", "FFFFFF")]
    [InlineData(int.MinValue, "0000", "000000")]
    [InlineData(int.MaxValue, "FFFF", "FFFFFF")]
    [InlineData(0x00800000, "0000", "800000")]
    [InlineData(0x00008000, "8000", "008000")]
    public void ShortAndMediumScalarsDiscardHighBitsEvenInCheckedBuilds(int value, string shortHex, string mediumHex)
    {
        CheckOwnedCopy(Unpooled.CopyShort(value), shortHex);
        CheckOwnedCopy(Unpooled.CopyMedium(value), mediumHex);
    }

    [Theory]
    [InlineData(0x00000000)]
    [InlineData(unchecked((int)0x80000000))]
    [InlineData(0x00000001)]
    [InlineData(0x007fffff)]
    [InlineData(0x00800000)]
    [InlineData(0x7f7fffff)]
    [InlineData(0x7f800000)]
    [InlineData(unchecked((int)0xff800000))]
    [InlineData(0x7fc12345)]
    [InlineData(unchecked((int)0xffc12345))]
    [InlineData(0x7fa12345)]
    [InlineData(unchecked((int)0xff812345))]
    public void FloatFactoriesPreserveRawIeeeBitsIncludingNaNPayloads(int bits)
    {
        float value = BitConverter.Int32BitsToSingle(bits);
        string hex = bits.ToString("X8");
        CheckOwnedCopy(Unpooled.CopyFloat(value), hex);
        CheckOwnedCopy(Unpooled.CopyFloat(new[] { 1f, value }), "3F800000" + hex);
        Span<float> span = stackalloc float[] { value };
        CheckOwnedCopy(Unpooled.CopyFloat(span), hex);
    }

    [Theory]
    [InlineData(0x0000000000000000L)]
    [InlineData(unchecked((long)0x8000000000000000UL))]
    [InlineData(0x0000000000000001L)]
    [InlineData(0x000fffffffffffffL)]
    [InlineData(0x0010000000000000L)]
    [InlineData(0x7fefffffffffffffL)]
    [InlineData(0x7ff0000000000000L)]
    [InlineData(unchecked((long)0xfff0000000000000UL))]
    [InlineData(0x7ff8123456789abcL)]
    [InlineData(unchecked((long)0xfff8123456789abcUL))]
    [InlineData(0x7ff0123456789abcL)]
    [InlineData(unchecked((long)0xfff0123456789abcUL))]
    public void DoubleFactoriesPreserveRawIeeeBitsIncludingNaNPayloads(long bits)
    {
        double value = BitConverter.Int64BitsToDouble(bits);
        string hex = bits.ToString("X16");
        CheckOwnedCopy(Unpooled.CopyDouble(value), hex);
        CheckOwnedCopy(Unpooled.CopyDouble(new[] { 1d, value }), "3FF0000000000000" + hex);
        Span<double> span = stackalloc double[] { value };
        CheckOwnedCopy(Unpooled.CopyDouble(span), hex);
    }

    [Fact]
    public void StackAndSlicedSpansCopyOnlyRequestedValues()
    {
        Span<int> values = stackalloc int[] { 99, 0x12345678, -1, 88 };
        CheckOwnedCopy(Unpooled.CopyInt(values.Slice(1, 2)), "12345678FFFFFFFF");
        CheckOwnedCopy(Unpooled.CopyShort(values.Slice(1, 2)), "5678FFFF");
        CheckOwnedCopy(Unpooled.CopyMedium(values.Slice(1, 2)), "345678FFFFFF");
        Span<short> shorts = stackalloc short[] { 99, -1, 0x1234, 88 };
        CheckOwnedCopy(Unpooled.CopyShort(shorts.Slice(1, 2)), "FFFF1234");
        Span<long> longs = stackalloc long[] { 99, 0x0123456789abcdef, -1, 88 };
        CheckOwnedCopy(Unpooled.CopyLong(longs.Slice(1, 2)), "0123456789ABCDEFFFFFFFFFFFFFFFFF");
        Span<bool> booleans = stackalloc bool[] { false, true, false, true };
        CheckOwnedCopy(Unpooled.CopyBoolean(booleans.Slice(1, 2)), "0100");
        CheckOwnedCopy(Unpooled.CopyBoolean(false), "00");
        CheckOwnedCopy(Unpooled.CopyInt(0), "00000000");
    }
}
