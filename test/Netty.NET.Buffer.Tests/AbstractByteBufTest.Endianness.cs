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
using System.Buffers.Binary;

namespace Netty.NET.Buffer.Tests;

// CLR: the two inherited bodies that only exercise the deprecated ByteBuf.order()/order(ByteOrder) facade
// ("use the Little Endian accessors, e.g. getShortLE, getIntLE instead of creating a buffer with swapped
// endianness"). They keep the existing explicit-accessor CLR adaptation (fixed BE/LE accessors instead of
// mutable order views) and now run on every concrete fixture's buffers; the five-provider versions remain as
// supplements in ByteBufComparisonSourceTest and ByteBufSnapshotSourceTest.
public abstract partial class AbstractByteBufTest
{
    [Fact]
    public void TestSliceEndianness()
    {
        // CLR: CLR ByteBuf has no mutable order() facade. Known bytes let explicit BE/LE accessors show that
        // each slice reads the parent's wire bytes in both byte orders.
        byte[] bytes = new byte[buffer.Capacity];
        random.NextBytes(bytes);
        for (int i = 0; i < bytes.Length; ++i)
        {
            buffer.SetByte(i, bytes[i]);
        }
        AssertSliceEndianness(bytes, buffer.Slice(0, buffer.Capacity), 0, buffer.Capacity);
        AssertSliceEndianness(bytes, buffer.Slice(0, buffer.Capacity - 1), 0, buffer.Capacity - 1);
        AssertSliceEndianness(bytes, buffer.Slice(1, buffer.Capacity - 1), 1, buffer.Capacity - 1);
        AssertSliceEndianness(bytes, buffer.Slice(1, buffer.Capacity - 2), 1, buffer.Capacity - 2);
    }

    private static void AssertSliceEndianness(byte[] bytes, ByteBuf slice, int offset, int length)
    {
        // CLR: explicit BE/LE accessors replace the mutable Java order identities.
        Assert.Equal(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4)), slice.GetInt(0));
        Assert.Equal(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4)), slice.GetIntLE(0));
        Assert.Equal(0, slice.ReaderIndex);
        Assert.Equal(length, slice.WriterIndex);
    }

    [Fact]
    public void TestCompareTo2()
    {
        byte[] bytes = { 1, 2, 3, 4 };
        byte[] bytesReversed = { 4, 3, 2, 1 };

        // CLR: there is no order(LITTLE_ENDIAN/BIG_ENDIAN) facade; buf1/buf2 are read through explicit LE
        // accessors and buf3/buf4 through BE accessors, while comparison uses the fixed byte content.
        ByteBuf buf1 = NewBuffer(4).Clear().WriteBytes(bytes);
        ByteBuf buf2 = NewBuffer(4).Clear().WriteBytes(bytesReversed);
        ByteBuf buf3 = NewBuffer(4).Clear().WriteBytes(bytes);
        ByteBuf buf4 = NewBuffer(4).Clear().WriteBytes(bytesReversed);
        try
        {
            Assert.Equal(0x04030201, buf1.GetIntLE(0));
            Assert.Equal(0x01020304, buf2.GetIntLE(0));
            Assert.Equal(0x01020304, buf3.GetInt(0));
            Assert.Equal(0x04030201, buf4.GetInt(0));
            Assert.Equal(buf1.CompareTo(buf2), buf3.CompareTo(buf4));
            Assert.Equal(buf2.CompareTo(buf1), buf4.CompareTo(buf3));
            Assert.Equal(buf1.CompareTo(buf3), buf2.CompareTo(buf4));
            Assert.Equal(buf3.CompareTo(buf1), buf4.CompareTo(buf2));
        }
        finally
        {
            buf1.Release();
            buf2.Release();
            buf3.Release();
            buf4.Release();
        }
    }
}
