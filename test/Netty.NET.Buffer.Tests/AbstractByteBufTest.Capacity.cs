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
using System.Text;

namespace Netty.NET.Buffer.Tests;

public abstract partial class AbstractByteBufTest
{
    [Fact]
    public void TestSliceCapacityChange()
    {
        Assert.Throws<NotSupportedException>(() =>
        {
            AssertSliceCapacityChange(false);
        });
    }

    [Fact]
    public void TestRetainedSliceCapacityChange()
    {
        Assert.Throws<NotSupportedException>(() =>
        {
            AssertSliceCapacityChange(true);
        });
    }

    [Fact]
    public void TestReaderIndexLargerThanWriterIndex()
    {
        string content1 = "hello";
        string content2 = "world";
        int length = content1.Length + content2.Length;
        ByteBuf buffer = NewBuffer(length);
        buffer.SetIndex(0, 0);
        buffer.WriteString(content1, Encoding.ASCII);
        buffer.MarkWriterIndex();
        buffer.SkipBytes(content1.Length);
        buffer.WriteString(content2, Encoding.ASCII);
        buffer.SkipBytes(content2.Length);
        Assert.True(buffer.ReaderIndex <= buffer.WriterIndex);

        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                buffer.ResetWriterIndex();
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    protected void AssertOriginalCapacityEnforceMaxCapacity()
    {
        ByteBuf buffer = NewBuffer(3, 13);
        Assert.Equal(13, buffer.MaxCapacity);
        Assert.Equal(3, buffer.Capacity);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                buffer.Capacity = 14;
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    protected void AssertOriginalCapacityNegative()
    {
        ByteBuf buffer = NewBuffer(3, 13);
        Assert.Equal(13, buffer.MaxCapacity);
        Assert.Equal(3, buffer.Capacity);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                buffer.Capacity = -1;
            });
        }
        finally
        {
            buffer.Release();
        }
    }

    protected void AssertOriginalCapacityDecrease()
    {
        ByteBuf buffer = NewBuffer(3, 13);
        Assert.Equal(13, buffer.MaxCapacity);
        Assert.Equal(3, buffer.Capacity);
        try
        {
            buffer.Capacity = 2;
            Assert.Equal(2, buffer.Capacity);
            Assert.Equal(13, buffer.MaxCapacity);
        }
        finally
        {
            buffer.Release();
        }
    }

    protected void AssertOriginalCapacityIncrease()
    {
        ByteBuf buffer = NewBuffer(3, 13);
        Assert.Equal(13, buffer.MaxCapacity);
        Assert.Equal(3, buffer.Capacity);
        try
        {
            buffer.Capacity = 4;
            Assert.Equal(4, buffer.Capacity);
            Assert.Equal(13, buffer.MaxCapacity);
        }
        finally
        {
            buffer.Release();
        }
    }

    protected void AssertOriginalMaxFastWritableBytesTracksWrittenBytes()
    {
        ByteBuf buf = NewBuffer(4, 10);
        int max = buf.MaxFastWritableBytes;
        buf.WriteByte(1);
        Assert.Equal(max - 1, buf.MaxFastWritableBytes);
        buf.Release();
    }

    protected void AssertOriginalMaxFastWritableBytes()
    {
        ByteBuf buffer = NewBuffer(150, 500);
        buffer.WriterIndex = 100;
        Assert.Equal(50, buffer.WritableBytes);
        Assert.Equal(150, buffer.Capacity);
        Assert.Equal(500, buffer.MaxCapacity);
        Assert.Equal(400, buffer.MaxWritableBytes);
        // Default implementation has fast writable == writable
        Assert.Equal(50, buffer.MaxFastWritableBytes);
        buffer.Release();
    }

    private void AssertSliceCapacityChange(bool retainedSlice)
    {
        ByteBuf buf = NewBuffer(8);
        ByteBuf slice = retainedSlice ? buf.RetainedSlice(buf.ReaderIndex + 1, 3)
                                      : buf.Slice(buf.ReaderIndex + 1, 3);
        try
        {
            slice.Capacity = 10;
        }
        finally
        {
            if (retainedSlice)
            {
                slice.Release();
            }
            buf.Release();
        }
    }
}
