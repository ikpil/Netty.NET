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
    public void TestSetZero()
    {
        buffer.Clear();
        while (buffer.IsWritable)
        {
            buffer.WriteByte((byte)0xFF);
        }

        for (int i = 0; i < buffer.Capacity;)
        {
            int length = Math.Min(buffer.Capacity - i, random.Next(32));
            buffer.SetZero(i, length);
            i += length;
        }

        for (int i = 0; i < buffer.Capacity; i++)
        {
            Assert.Equal(0, buffer.GetByte(i));
        }
    }

    [Fact]
    public void TestWriteZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.WriteZero(-1));

        buffer.Clear();
        while (buffer.IsWritable)
        {
            buffer.WriteByte((byte)0xFF);
        }

        buffer.Clear();
        for (int i = 0; i < buffer.Capacity;)
        {
            int length = Math.Min(buffer.Capacity - i, random.Next(32));
            buffer.WriteZero(length);
            i += length;
        }

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);

        for (int i = 0; i < buffer.Capacity; i++)
        {
            Assert.Equal(0, buffer.GetByte(i));
        }
    }

    [Fact]
    public void TestSetZeroAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetZero(0, 1);
        });
    }

    [Fact]
    public void TestWriteZeroAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteZero(1);
        });
    }

    [Fact]
    public void TestForEachByte()
    {
        buffer.Clear();
        for (int i = 0; i < CAPACITY; i++)
        {
            buffer.WriteByte(i + 1);
        }

        int lastIndex = 0;
        buffer.SetIndex(CAPACITY / 4, CAPACITY * 3 / 4);
        int processorIndex = CAPACITY / 4;
        Assert.Equal(-1, buffer.ForEachByte(value =>
        {
            Assert.Equal(unchecked((byte)(processorIndex + 1)), value);
            lastIndex = processorIndex;
            processorIndex++;
            return true;
        }));

        Assert.Equal(CAPACITY * 3 / 4 - 1, lastIndex);
    }

    [Fact]
    public void TestForEachByteAbort()
    {
        buffer.Clear();
        for (int i = 0; i < CAPACITY; i++)
        {
            buffer.WriteByte(i + 1);
        }

        int stop = CAPACITY / 2;
        int processorIndex = CAPACITY / 3;
        Assert.Equal(stop, buffer.ForEachByte(CAPACITY / 3, CAPACITY / 3, value =>
        {
            Assert.Equal(unchecked((byte)(processorIndex + 1)), value);
            if (processorIndex == stop)
            {
                return false;
            }

            processorIndex++;
            return true;
        }));
    }

    [Fact]
    public void TestForEachByteDesc()
    {
        buffer.Clear();
        for (int i = 0; i < CAPACITY; i++)
        {
            buffer.WriteByte(i + 1);
        }

        int lastIndex = 0;
        int processorIndex = CAPACITY * 3 / 4 - 1;
        Assert.Equal(-1, buffer.ForEachByteDesc(CAPACITY / 4, CAPACITY * 2 / 4, value =>
        {
            Assert.Equal(unchecked((byte)(processorIndex + 1)), value);
            lastIndex = processorIndex;
            processorIndex--;
            return true;
        }));

        Assert.Equal(CAPACITY / 4, lastIndex);
    }

    [Fact]
    public void TestForEachByteDesc2()
    {
        byte[] expected = new byte[] { 1, 2, 3, 4 };
        ByteBuf buf = NewBuffer(expected.Length);
        try
        {
            buf.WriteBytes(expected);
            byte[] bytes = new byte[expected.Length];
            int processorIndex = bytes.Length - 1;
            int i = buf.ForEachByteDesc(value =>
            {
                bytes[processorIndex--] = value;
                return true;
            });
            Assert.Equal(-1, i);
            Assert.Equal(expected, bytes);
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void TestForEachByte2()
    {
        byte[] expected = new byte[] { 1, 2, 3, 4 };
        ByteBuf buf = NewBuffer(expected.Length);
        try
        {
            buf.WriteBytes(expected);
            byte[] bytes = new byte[expected.Length];
            int processorIndex = 0;
            int i = buf.ForEachByte(value =>
            {
                bytes[processorIndex++] = value;
                return true;
            });
            Assert.Equal(-1, i);
            Assert.Equal(expected, bytes);
        }
        finally
        {
            buf.Release();
        }
    }

    [Fact]
    public void TestForEachByteAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(
                () =>
                {
                    ReleasedBuffer().ForEachByte(_ => true);
                });
    }

    [Fact]
    public void TestForEachByteAfterRelease1()
    {
        Assert.Throws<IllegalReferenceCountException>(
                () =>
                {
                    ReleasedBuffer().ForEachByte(0, 1, _ => true);
                });
    }

    [Fact]
    public void TestForEachByteDescAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(
                () =>
                {
                    ReleasedBuffer().ForEachByteDesc(_ => true);
                });
    }

    [Fact]
    public void TestForEachByteDescAfterRelease1()
    {
        Assert.Throws<IllegalReferenceCountException>(
                () =>
                {
                    ReleasedBuffer().ForEachByteDesc(0, 1, _ => true);
                });
    }

    [Fact]
    public void TestWriteBooleanAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteBoolean(true);
        });
    }

    [Fact]
    public void TestWriteByteAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteByte(1);
        });
    }

    [Fact]
    public void TestWriteShortAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteShort(1);
        });
    }

    [Fact]
    public void TestWriteShortLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteShortLE(1);
        });
    }

    [Fact]
    public void TestWriteMediumAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteMedium(1);
        });
    }

    [Fact]
    public void TestWriteMediumLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteMediumLE(1);
        });
    }

    [Fact]
    public void TestWriteIntAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteInt(1);
        });
    }

    [Fact]
    public void TestWriteIntLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteIntLE(1);
        });
    }

    [Fact]
    public void TestWriteLongAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteLong(1);
        });
    }

    [Fact]
    public void TestWriteLongLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteLongLE(1);
        });
    }

    [Fact]
    public void TestWriteCharAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteChar((char)1);
        });
    }

    [Fact]
    public void TestWriteFloatAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteFloat(1);
        });
    }

    [Fact]
    public void TestWriteFloatLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteFloatLE(1);
        });
    }

    [Fact]
    public void TestWriteDoubleAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteDouble(1);
        });
    }

    [Fact]
    public void TestWriteDoubleLEAfterRelease()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteDoubleLE(1);
        });
    }

}
