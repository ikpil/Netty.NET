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

public abstract partial class AbstractByteBufTest
{
    [Fact]
    public void InitialState()
    {
        Assert.Equal(CAPACITY, buffer.Capacity);
        Assert.Equal(0, buffer.ReaderIndex);
    }

    [Fact]
    public void ComparableInterfaceNotViolated()
    {
        Assert.False(buffer.IsReadOnly);
        buffer.WriterIndex = buffer.ReaderIndex;
        Assert.True(buffer.WritableBytes >= 4);

        buffer.WriteLong(0);
        ByteBuf buffer2 = NewBuffer(CAPACITY);
        Assert.False(buffer2.IsReadOnly);
        buffer2.WriterIndex = buffer2.ReaderIndex;
        // Write an unsigned integer that will cause buffer.getUnsignedInt() - buffer2.getUnsignedInt() to underflow the
        // int type and wrap around on the negative side.
        buffer2.WriteLong(0xF0000000L);
        Assert.True(buffer.CompareTo(buffer2) < 0);
        Assert.True(buffer2.CompareTo(buffer) > 0);
        buffer2.Release();
    }

    [Fact]
    public void CopyBoundaryCheck1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.Copy(-1, 0);
        });
    }

    [Fact]
    public void CopyBoundaryCheck2()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.Copy(0, buffer.Capacity + 1);
        });
    }

    [Fact]
    public void CopyBoundaryCheck3()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.Copy(buffer.Capacity + 1, 0);
        });
    }

    [Fact]
    public void CopyBoundaryCheck4()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.Copy(buffer.Capacity, 1);
        });
    }

    [Fact]
    public void TestCopy()
    {
        for (int i = 0; i < buffer.Capacity; i ++)
        {
            byte value = unchecked((byte)NextSignedInt32());
            buffer.SetByte(i, value);
        }

        int readerIndex = CAPACITY / 3;
        int writerIndex = CAPACITY * 2 / 3;
        buffer.SetIndex(readerIndex, writerIndex);

        // Make sure all properties are copied.
        ByteBuf copy = Own(buffer.Copy());
        Assert.Equal(0, copy.ReaderIndex);
        Assert.Equal(buffer.ReadableBytes, copy.WriterIndex);
        Assert.Equal(buffer.ReadableBytes, copy.Capacity);
        // CLR BE/LE interpretation is fixed by each accessor; no mutable byte-order identity exists.
        for (int i = 0; i < copy.Capacity; i ++)
        {
            Assert.Equal(buffer.GetByte(i + readerIndex), copy.GetByte(i));
        }

        // Make sure the buffer content is independent from each other.
        buffer.SetByte(readerIndex, unchecked((byte)(buffer.GetByte(readerIndex) + 1)));
        Assert.True(buffer.GetByte(readerIndex) != copy.GetByte(0));
        copy.SetByte(1, unchecked((byte)(copy.GetByte(1) + 1)));
        Assert.True(buffer.GetByte(readerIndex + 1) != copy.GetByte(1));
        copy.Release();
    }

    [Fact]
    public void TestDuplicate()
    {
        for (int i = 0; i < buffer.Capacity; i ++)
        {
            byte value = unchecked((byte)NextSignedInt32());
            buffer.SetByte(i, value);
        }

        int readerIndex = CAPACITY / 3;
        int writerIndex = CAPACITY * 2 / 3;
        buffer.SetIndex(readerIndex, writerIndex);

        // Make sure all properties are copied.
        ByteBuf duplicate = buffer.Duplicate();
        // CLR BE/LE interpretation is fixed by each accessor; no mutable byte-order identity exists.
        Assert.Equal(buffer.ReadableBytes, duplicate.ReadableBytes);
        Assert.Equal(0, buffer.CompareTo(duplicate));

        // Make sure the buffer content is shared.
        buffer.SetByte(readerIndex, unchecked((byte)(buffer.GetByte(readerIndex) + 1)));
        Assert.Equal(buffer.GetByte(readerIndex), duplicate.GetByte(duplicate.ReaderIndex));
        duplicate.SetByte(duplicate.ReaderIndex, unchecked((byte)(duplicate.GetByte(duplicate.ReaderIndex) + 1)));
        Assert.Equal(buffer.GetByte(readerIndex), duplicate.GetByte(duplicate.ReaderIndex));
    }

    // Keep the original HashSet.Contains and Count operations rather than collection enumeration assertions.
#pragma warning disable xUnit2013, xUnit2017
    [Fact]
    public void TestHashCode()
    {
        ByteBuf elemA = Own(_peerAllocator.HeapBuffer(15));
        ByteBuf elemB = DirectBuffer(15);
        elemA.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 1, 2, 3, 4, 5 });
        elemB.WriteBytes(new byte[] { 6, 7, 8, 9, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9 });

        HashSet<ByteBuf> set = new HashSet<ByteBuf>();
        set.Add(elemA);
        set.Add(elemB);

        Assert.Equal(2, set.Count);
        ByteBuf elemACopy = Own(elemA.Copy());
        Assert.True(set.Contains(elemACopy));

        ByteBuf elemBCopy = Own(elemB.Copy());
        Assert.True(set.Contains(elemBCopy));

        buffer.Clear();
        buffer.WriteBytes(elemA.Duplicate());

        Assert.True(set.Remove(buffer));
        Assert.False(set.Contains(elemA));
        Assert.Equal(1, set.Count);

        buffer.Clear();
        buffer.WriteBytes(elemB.Duplicate());
        Assert.True(set.Remove(buffer));
        Assert.False(set.Contains(elemB));
        Assert.Equal(0, set.Count);
        elemA.Release();
        elemB.Release();
        elemACopy.Release();
        elemBCopy.Release();
    }
#pragma warning restore xUnit2013, xUnit2017

    [Fact]
    public void TestCopyAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().Copy();
        });
    }

    [Fact]
    public void TestCopyAfterRelease1()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().Copy();
        });
    }

    [Fact]
    public void TestReadOnlyRelease()
    {
        ByteBuf buf = NewBuffer(8);
        Assert.Equal(1, buf.ReferenceCount);
        Assert.True(buf.AsReadOnly().Release());
        Assert.Equal(0, buf.ReferenceCount);
    }

}
