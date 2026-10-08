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

public class ByteBufWireContractTest
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(8, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(8, true)]
    public void PrimitiveAccessMatchesIndependentWireBytes(int width, bool littleEndian)
    {
        // Preserve AbstractByteBufTest's 100 byte-buffer consistency iterations.
        var random = new Random(0x64CC10);
        ByteBuf buffer = Unpooled.Buffer(13, 13);
        try
        {
            for (int iteration = 0; iteration < 100; ++iteration)
            {
                byte[] randomBytes = new byte[8];
                random.NextBytes(randomBytes);
                ulong bits = 0;
                for (int i = 0; i < 8; ++i) bits |= (ulong)randomBytes[i] << (i * 8);
                long value = unchecked((long)bits);
                buffer.AsSpan(0, 13).Fill(0xA5);
                Set(buffer, 3, width, littleEndian, value);
                byte[] expected = new byte[13];
                Array.Fill(expected, (byte)0xA5);
                for (int i = 0; i < width; ++i)
                    expected[3 + i] = unchecked((byte)(bits >> ((littleEndian ? i : width - i - 1) * 8)));
                Assert.Equal(expected, buffer.AsMemory(0, 13).ToArray());
                long numeric = width switch
                {
                    1 => unchecked((byte)value),
                    2 => unchecked((short)value),
                    3 => unchecked((int)value) << 8 >> 8,
                    4 => unchecked((int)value),
                    _ => value
                };
                Assert.Equal(numeric, Get(buffer, 3, width, littleEndian));
                Assert.Equal(0, buffer.ReaderIndex);
                Assert.Equal(0, buffer.WriterIndex);
                Assert.Throws<ArgumentOutOfRangeException>(() => Get(buffer, 14 - width, width, littleEndian));
                Assert.Throws<ArgumentOutOfRangeException>(() => Get(buffer, int.MaxValue, width, littleEndian));
            }
        }
        finally { buffer.Release(); }
    }

    private static void Set(ByteBuf b, int index, int width, bool le, long value)
    {
        switch (width)
        {
            case 1: b.SetByte(index, unchecked((int)value)); break;
            case 2: if (le) b.SetShortLE(index, unchecked((int)value)); else b.SetShort(index, unchecked((int)value)); break;
            case 3: if (le) b.SetMediumLE(index, unchecked((int)value)); else b.SetMedium(index, unchecked((int)value)); break;
            case 4: if (le) b.SetIntLE(index, unchecked((int)value)); else b.SetInt(index, unchecked((int)value)); break;
            case 8: if (le) b.SetLongLE(index, value); else b.SetLong(index, value); break;
        }
    }
    private static long Get(ByteBuf b, int index, int width, bool le) => width switch
    {
        1 => b.GetByte(index),
        2 => le ? b.GetShortLE(index) : b.GetShort(index),
        3 => le ? b.GetMediumLE(index) : b.GetMedium(index),
        4 => le ? b.GetIntLE(index) : b.GetInt(index),
        _ => le ? b.GetLongLE(index) : b.GetLong(index)
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SequentialAccessGrowsAndPreservesUnsignedAndFloatingBits(bool le)
    {
        ByteBuf buffer = Unpooled.Buffer(0, 64);
        try
        {
            if (le) buffer.WriteShortLE(0xFEDC).WriteMediumLE(0xFEDCBA).WriteIntLE(unchecked((int)0xFEDCBA98)).WriteLongLE(long.MinValue);
            else buffer.WriteShort(0xFEDC).WriteMedium(0xFEDCBA).WriteInt(unchecked((int)0xFEDCBA98)).WriteLong(long.MinValue);
            int floatBits = unchecked((int)0xFFC01234);
            long doubleBits = unchecked((long)0xFFF8000012345678UL);
            if (le) buffer.WriteFloatLE(BitConverter.Int32BitsToSingle(floatBits)).WriteDoubleLE(BitConverter.Int64BitsToDouble(doubleBits));
            else buffer.WriteFloat(BitConverter.Int32BitsToSingle(floatBits)).WriteDouble(BitConverter.Int64BitsToDouble(doubleBits));
            Assert.Equal((ushort)0xFEDC, le ? buffer.ReadUnsignedShortLE() : buffer.ReadUnsignedShort());
            Assert.Equal(0xFEDCBA, le ? buffer.ReadUnsignedMediumLE() : buffer.ReadUnsignedMedium());
            Assert.Equal(0xFEDCBA98u, le ? buffer.ReadUnsignedIntLE() : buffer.ReadUnsignedInt());
            Assert.Equal(long.MinValue, le ? buffer.ReadLongLE() : buffer.ReadLong());
            Assert.Equal(floatBits, BitConverter.SingleToInt32Bits(le ? buffer.ReadFloatLE() : buffer.ReadFloat()));
            Assert.Equal(doubleBits, BitConverter.DoubleToInt64Bits(le ? buffer.ReadDoubleLE() : buffer.ReadDouble()));
            Assert.Equal(buffer.WriterIndex, buffer.ReaderIndex);
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.ReadByte());
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void PrimitiveFailuresDoNotAdvanceIndices()
    {
        ByteBuf buffer = Unpooled.Buffer(3, 3);
        try
        {
            buffer.WriteByte(0xFF);
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.ReadInt());
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.WriteInt(5));
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(1, buffer.WriterIndex);
            Assert.Equal(255, buffer.ReadByte());
            buffer.Clear().WriteBoolean(true).WriteChar('\uFEDC');
            Assert.True(buffer.ReadBoolean());
            Assert.Equal('\uFEDC', buffer.ReadChar());
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void BulkCopiesHandleOverlapAndIndependentIndexRules()
    {
        ByteBuf buffer = Unpooled.CopiedBuffer(new byte[] { 0, 1, 2, 3, 4, 5 });
        ByteBuf destination = Unpooled.Buffer(8, 8);
        try
        {
            buffer.GetBytes(0, buffer, 1, 5);
            Assert.Equal(new byte[] { 0, 0, 1, 2, 3, 4 }, buffer.ReadableMemory.ToArray());
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(6, buffer.WriterIndex);
            destination.WriteBytes(buffer, 3);
            Assert.Equal(3, buffer.ReaderIndex);
            Assert.Equal(3, destination.WriterIndex);
            Assert.Equal(new byte[] { 0, 0, 1 }, destination.ReadableMemory.ToArray());
            buffer.ReadBytes(destination, 2);
            Assert.Equal(5, buffer.ReaderIndex);
            Assert.Equal(5, destination.WriterIndex);
            destination.WriteZero(3);
            Assert.Equal(new byte[] { 0, 0, 1, 2, 3, 0, 0, 0 }, destination.ReadableMemory.ToArray());
            byte[] output = new byte[8];
            destination.ReadBytes(output);
            Assert.Equal(8, destination.ReaderIndex);
            Assert.Equal(new byte[] { 0, 0, 1, 2, 3, 0, 0, 0 }, output);
        }
        finally { buffer.Release(); destination.Release(); }
    }

    [Fact]
    public void ReadsCannotAccessUnwrittenCapacityAndWritesCan()
    {
        ByteBuf buffer = Unpooled.Buffer(8);
        try
        {
            buffer.SetLong(0, long.MaxValue);
            Assert.Equal(long.MaxValue, buffer.GetLong(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.ReadLong());
            buffer.WriterIndex = 8;
            Assert.Equal(long.MaxValue, buffer.ReadLong());
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void ReleasedContentAccessFailsEvenForZeroLength()
    {
        ByteBuf buffer = Unpooled.Buffer(8);
        buffer.Release();
        Assert.Throws<IllegalReferenceCountException>(() => buffer.AsMemory(0, 0));
        Assert.Throws<IllegalReferenceCountException>(() => buffer.SetZero(0, 0));
        Assert.Throws<IllegalReferenceCountException>(() => buffer.EnsureWritable(0));
        Assert.Throws<IllegalReferenceCountException>(() => buffer.DiscardReadBytes());
        Assert.Throws<IllegalReferenceCountException>(() => buffer.DiscardSomeReadBytes());
        Assert.Throws<IllegalReferenceCountException>(() => buffer.Duplicate());
        Assert.Throws<IllegalReferenceCountException>(() => buffer.Slice(0, 0));
    }
}
