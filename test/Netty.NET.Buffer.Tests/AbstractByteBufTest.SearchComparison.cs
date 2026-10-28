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

public abstract partial class AbstractByteBufTest
{
    [Fact]
    public void TestSWARIndexOf()
    {
        ByteBuf buffer = NewBuffer(16);
        buffer.Clear();
        // Ensure the buffer is completely zero'ed.
        buffer.SetZero(0, buffer.Capacity);
        buffer.WriteByte((byte)0); // 0
        buffer.WriteByte((byte)0);
        buffer.WriteByte((byte)0);
        buffer.WriteByte((byte)0);
        buffer.WriteByte((byte)0);
        buffer.WriteByte((byte)0);
        buffer.WriteByte((byte)0);
        buffer.WriteByte((byte)0); // 7

        buffer.WriteByte((byte)0);
        buffer.WriteByte((byte)0);
        buffer.WriteByte((byte)0);
        buffer.WriteByte((byte)1); // 11
        buffer.WriteByte((byte)2);
        buffer.WriteByte((byte)3);
        buffer.WriteByte((byte)4);
        buffer.WriteByte((byte)1); // 15
        Assert.Equal(11, buffer.IndexOf(0, 12, (byte)1));
        Assert.Equal(12, buffer.IndexOf(0, 16, (byte)2));
        Assert.Equal(-1, buffer.IndexOf(0, 11, (byte)1));
        Assert.Equal(11, buffer.IndexOf(0, 16, (byte)1));

        // lastIndexOf
        Assert.Equal(15, buffer.IndexOf(16, 0, (byte)1));
        Assert.Equal(12, buffer.IndexOf(16, 0, (byte)2));
        Assert.Equal(11, buffer.IndexOf(15, 0, (byte)1));
        Assert.Equal(-1, buffer.IndexOf(11, 0, (byte)1));
        buffer.Release();
    }

    [Fact]
    public void TestUnrolledSWARIndexOf()
    {
        ByteBuf buffer = NewBuffer(15);
        buffer.Clear();
        // Ensure the buffer is completely zero'ed.
        buffer.SetZero(0, buffer.Capacity);
        buffer.WriteByte((byte)0); // 0
        buffer.WriteByte((byte)1);
        buffer.WriteByte((byte)2);
        buffer.WriteByte((byte)3);
        buffer.WriteByte((byte)4);
        buffer.WriteByte((byte)5);
        buffer.WriteByte((byte)6);
        buffer.WriteByte((byte)7); // 7

        buffer.WriteByte((byte)8); // 8
        buffer.WriteByte((byte)9);
        buffer.WriteByte((byte)10);
        buffer.WriteByte((byte)11);
        buffer.WriteByte((byte)12);
        buffer.WriteByte((byte)13);
        buffer.WriteByte((byte)14); // 14
        Assert.Equal(15, buffer.Capacity);
        for (int i = 0; i < 14; ++i)
        {
            Assert.Equal(i, buffer.IndexOf(i, buffer.Capacity, (byte)i));
        }

        // lastIndexOf
        for (int i = 0; i < 14; ++i)
        {
            Assert.Equal(i, buffer.IndexOf(buffer.Capacity, 0, (byte)i));
        }
        buffer.Release();
    }

    [Fact]
    public void TestIndexOf()
    {
        buffer.Clear();
        // Ensure the buffer is completely zero'ed.
        buffer.SetZero(0, buffer.Capacity);

        buffer.WriteByte((byte)1);
        buffer.WriteByte((byte)2);
        buffer.WriteByte((byte)3);
        buffer.WriteByte((byte)2);
        buffer.WriteByte((byte)1);

        Assert.Equal(-1, buffer.IndexOf(1, 4, (byte)1));
        Assert.Equal(-1, buffer.IndexOf(4, 1, (byte)1));
        Assert.Equal(1, buffer.IndexOf(1, 4, (byte)2));
        Assert.Equal(3, buffer.IndexOf(4, 1, (byte)2));

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.IndexOf(0, buffer.Capacity + 1, (byte)0));

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.IndexOf(buffer.Capacity, -1, (byte)0));

        Assert.Equal(4, buffer.IndexOf(buffer.Capacity + 1, 0, (byte)1));
        Assert.Equal(0, buffer.IndexOf(-1, buffer.Capacity, (byte)1));
    }

    [Fact]
    public void TestIndexOfReleaseBuffer()
    {
        ByteBuf buffer = ReleasedBuffer();
        if (buffer.Capacity != 0)
        {
            Assert.Throws<IllegalReferenceCountException>(() => buffer.IndexOf(0, 1, (byte)1));
        }
        else
        {
            Assert.Equal(-1, buffer.IndexOf(0, 1, (byte)1));
        }
    }

    [Fact]
    public void TestEndiannessIndexOf()
    {
        buffer.Clear();
        int v = 0x02030201;
        buffer.WriteIntLE(v);
        buffer.WriteByte(0x01);

        Assert.Equal(-1, buffer.IndexOf(1, 4, (byte)1));
        Assert.Equal(-1, buffer.IndexOf(4, 1, (byte)1));
        Assert.Equal(1, buffer.IndexOf(1, 4, (byte)2));
        Assert.Equal(3, buffer.IndexOf(4, 1, (byte)2));
    }

    [Fact]
    public void TestEquals()
    {
        // CLR has fixed explicit BE/LE accessors; Java-only mutable-order variants are recorded separately.
        Assert.False(buffer.Equals((object)null));
        Assert.False(buffer.Equals(new object()));

        byte[] value = new byte[32];
        buffer.SetIndex(0, value.Length);
        random.NextBytes(value);
        buffer.SetBytes(0, value);

        Assert.Equal(buffer, WrappedBuffer(value));

        value[0] = unchecked((byte)(value[0] + 1));
        Assert.False(buffer.Equals(WrappedBuffer(value)));
    }

    [Fact]
    public void TestCompareTo()
    {
        // CLR has fixed explicit BE/LE accessors; Java-only mutable-order variants are recorded separately.
        Assert.Equal(1, buffer.CompareTo(null));

        // Fill the random stuff
        byte[] value = new byte[32];
        random.NextBytes(value);
        // Prevent overflow / underflow
        if (value[0] == 0)
        {
            value[0] = unchecked((byte)(value[0] + 1));
        }
        else if (value[0] == byte.MaxValue)
        {
            value[0]--;
        }

        buffer.SetIndex(0, value.Length);
        buffer.SetBytes(0, value);

        Assert.Equal(0, buffer.CompareTo(WrappedBuffer(value)));

        value[0] = unchecked((byte)(value[0] + 1));
        Assert.True(buffer.CompareTo(WrappedBuffer(value)) < 0);
        value[0] -= 2;
        Assert.True(buffer.CompareTo(WrappedBuffer(value)) > 0);
        value[0] = unchecked((byte)(value[0] + 1));

        Assert.True(buffer.CompareTo(Own(Unpooled.WrappedBuffer(value, 0, 31))) > 0);
        Assert.True(buffer.Slice(0, 31).CompareTo(WrappedBuffer(value)) < 0);

        ByteBuf retainedSlice = buffer.RetainedSlice(0, 31);
        Assert.True(retainedSlice.CompareTo(WrappedBuffer(value)) < 0);
        retainedSlice.Release();
    }

}
