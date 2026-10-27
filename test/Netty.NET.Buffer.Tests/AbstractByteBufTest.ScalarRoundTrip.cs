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

public abstract partial class AbstractByteBufTest
{
    [Fact]
    public void TestRandomByteAccess()
    {
        for (int i = 0; i < buffer.Capacity; i ++)
        {
            byte value = unchecked((byte)NextSignedInt32());
            buffer.SetByte(i, value);
        }

        random = new Random(seed);
        for (int i = 0; i < buffer.Capacity; i ++)
        {
            byte value = unchecked((byte)NextSignedInt32());
            Assert.Equal(value, buffer.GetByte(i));
        }
    }

    [Fact]
    public void TestRandomUnsignedByteAccess()
    {
        for (int i = 0; i < buffer.Capacity; i ++)
        {
            byte value = unchecked((byte)NextSignedInt32());
            buffer.SetByte(i, value);
        }

        random = new Random(seed);
        for (int i = 0; i < buffer.Capacity; i ++)
        {
            int value = NextSignedInt32() & 0xFF;
            Assert.Equal(value, buffer.GetByte(i));
        }
    }

    [Fact]
    public void TestRandomShortAccess()
    {
        AssertRandomShortAccess(true);
    }

    [Fact]
    public void TestRandomShortLEAccess()
    {
        AssertRandomShortAccess(false);
    }

    [Fact]
    public void TestRandomUnsignedShortAccess()
    {
        AssertRandomUnsignedShortAccess(true);
    }

    [Fact]
    public void TestRandomUnsignedShortLEAccess()
    {
        AssertRandomUnsignedShortAccess(false);
    }

    [Fact]
    public void TestRandomMediumAccess()
    {
        AssertRandomMediumAccess(true);
    }

    [Fact]
    public void TestRandomMediumLEAccess()
    {
        AssertRandomMediumAccess(false);
    }

    [Fact]
    public void TestRandomUnsignedMediumAccess()
    {
        AssertRandomUnsignedMediumAccess(true);
    }

    [Fact]
    public void TestRandomUnsignedMediumLEAccess()
    {
        AssertRandomUnsignedMediumAccess(false);
    }

    [Fact]
    public void TestRandomIntAccess()
    {
        AssertRandomIntAccess(true);
    }

    [Fact]
    public void TestRandomIntLEAccess()
    {
        AssertRandomIntAccess(false);
    }

    [Fact]
    public void TestRandomUnsignedIntAccess()
    {
        AssertRandomUnsignedIntAccess(true);
    }

    [Fact]
    public void TestRandomUnsignedIntLEAccess()
    {
        AssertRandomUnsignedIntAccess(false);
    }

    [Fact]
    public void TestRandomLongAccess()
    {
        AssertRandomLongAccess(true);
    }

    [Fact]
    public void TestRandomLongLEAccess()
    {
        AssertRandomLongAccess(false);
    }

    [Fact]
    public void TestRandomFloatAccess()
    {
        AssertRandomFloatAccess(true);
    }

    [Fact]
    public void TestRandomFloatLEAccess()
    {
        AssertRandomFloatAccess(false);
    }

    [Fact]
    public void TestRandomDoubleAccess()
    {
        AssertRandomDoubleAccess(true);
    }

    [Fact]
    public void TestRandomDoubleLEAccess()
    {
        AssertRandomDoubleAccess(false);
    }

    [Fact]
    public void TestSequentialByteAccess()
    {
        buffer.WriterIndex = 0;
        for (int i = 0; i < buffer.Capacity; i ++)
        {
            byte value = unchecked((byte)NextSignedInt32());
            Assert.Equal(i, buffer.WriterIndex);
            Assert.True(buffer.IsWritable);
            buffer.WriteByte(value);
        }

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsWritable);

        random = new Random(seed);
        for (int i = 0; i < buffer.Capacity; i ++)
        {
            byte value = unchecked((byte)NextSignedInt32());
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.True(buffer.IsReadable);
            Assert.Equal(value, buffer.ReadByte());
        }

        Assert.Equal(buffer.Capacity, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsReadable);
        Assert.False(buffer.IsWritable);
    }

    [Fact]
    public void TestSequentialUnsignedByteAccess()
    {
        buffer.WriterIndex = 0;
        for (int i = 0; i < buffer.Capacity; i ++)
        {
            byte value = unchecked((byte)NextSignedInt32());
            Assert.Equal(i, buffer.WriterIndex);
            Assert.True(buffer.IsWritable);
            buffer.WriteByte(value);
        }

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsWritable);

        random = new Random(seed);
        for (int i = 0; i < buffer.Capacity; i ++)
        {
            int value = NextSignedInt32() & 0xFF;
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.True(buffer.IsReadable);
            Assert.Equal(value, buffer.ReadByte());
        }

        Assert.Equal(buffer.Capacity, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsReadable);
        Assert.False(buffer.IsWritable);
    }

    [Fact]
    public void TestSequentialShortAccess()
    {
        AssertSequentialShortAccess(true);
    }

    [Fact]
    public void TestSequentialShortLEAccess()
    {
        AssertSequentialShortAccess(false);
    }

    [Fact]
    public void TestSequentialUnsignedShortAccess()
    {
        AssertSequentialUnsignedShortAccess(true);
    }

    [Fact]
    public void TestSequentialUnsignedShortLEAccess()
    {
        AssertSequentialUnsignedShortAccess(true);
    }

    [Fact]
    public void TestSequentialMediumAccess()
    {
        AssertSequentialMediumAccess(true);
    }

    [Fact]
    public void TestSequentialMediumLEAccess()
    {
        AssertSequentialMediumAccess(false);
    }

    [Fact]
    public void TestSequentialUnsignedMediumAccess()
    {
        AssertSequentialUnsignedMediumAccess(true);
    }

    [Fact]
    public void TestSequentialUnsignedMediumLEAccess()
    {
        AssertSequentialUnsignedMediumAccess(false);
    }

    [Fact]
    public void TestSequentialIntAccess()
    {
        AssertSequentialIntAccess(true);
    }

    [Fact]
    public void TestSequentialIntLEAccess()
    {
        AssertSequentialIntAccess(false);
    }

    [Fact]
    public void TestSequentialUnsignedIntAccess()
    {
        AssertSequentialUnsignedIntAccess(true);
    }

    [Fact]
    public void TestSequentialUnsignedIntLEAccess()
    {
        AssertSequentialUnsignedIntAccess(false);
    }

    [Fact]
    public void TestSequentialLongAccess()
    {
        AssertSequentialLongAccess(true);
    }

    [Fact]
    public void TestSequentialLongLEAccess()
    {
        AssertSequentialLongAccess(false);
    }

    private void AssertRandomShortAccess(bool testBigEndian)
    {
        for (int i = 0; i < buffer.Capacity - 1; i += 2)
        {
            short value = unchecked((short)NextSignedInt32());
            if (testBigEndian)
            {
                buffer.SetShort(i, value);
            }
            else
            {
                buffer.SetShortLE(i, value);
            }
        }

        random = new Random(seed);
        for (int i = 0; i < buffer.Capacity - 1; i += 2)
        {
            short value = unchecked((short)NextSignedInt32());
            if (testBigEndian)
            {
                Assert.Equal(value, buffer.GetShort(i));
            }
            else
            {
                Assert.Equal(value, buffer.GetShortLE(i));
            }
        }
    }

    private void AssertRandomUnsignedShortAccess(bool testBigEndian)
    {
        for (int i = 0; i < buffer.Capacity - 1; i += 2)
        {
            short value = unchecked((short)NextSignedInt32());
            if (testBigEndian)
            {
                buffer.SetShort(i, value);
            }
            else
            {
                buffer.SetShortLE(i, value);
            }
        }

        random = new Random(seed);
        for (int i = 0; i < buffer.Capacity - 1; i += 2)
        {
            int value = NextSignedInt32() & 0xFFFF;
            if (testBigEndian)
            {
                Assert.Equal(value, buffer.GetUnsignedShort(i));
            }
            else
            {
                Assert.Equal(value, buffer.GetUnsignedShortLE(i));
            }
        }
    }

    private void AssertRandomMediumAccess(bool testBigEndian)
    {
        for (int i = 0; i < buffer.Capacity - 2; i += 3)
        {
            int value = NextSignedInt32();
            if (testBigEndian)
            {
                buffer.SetMedium(i, value);
            }
            else
            {
                buffer.SetMediumLE(i, value);
            }
        }

        random = new Random(seed);
        for (int i = 0; i < buffer.Capacity - 2; i += 3)
        {
            int value = NextSignedInt32() << 8 >> 8;
            if (testBigEndian)
            {
                Assert.Equal(value, buffer.GetMedium(i));
            }
            else
            {
                Assert.Equal(value, buffer.GetMediumLE(i));
            }
        }
    }

    private void AssertRandomUnsignedMediumAccess(bool testBigEndian)
    {
        for (int i = 0; i < buffer.Capacity - 2; i += 3)
        {
            int value = NextSignedInt32();
            if (testBigEndian)
            {
                buffer.SetMedium(i, value);
            }
            else
            {
                buffer.SetMediumLE(i, value);
            }
        }

        random = new Random(seed);
        for (int i = 0; i < buffer.Capacity - 2; i += 3)
        {
            int value = NextSignedInt32() & 0x00FFFFFF;
            if (testBigEndian)
            {
                Assert.Equal(value, buffer.GetUnsignedMedium(i));
            }
            else
            {
                Assert.Equal(value, buffer.GetUnsignedMediumLE(i));
            }
        }
    }

    private void AssertRandomIntAccess(bool testBigEndian)
    {
        for (int i = 0; i < buffer.Capacity - 3; i += 4)
        {
            int value = NextSignedInt32();
            if (testBigEndian)
            {
                buffer.SetInt(i, value);
            }
            else
            {
                buffer.SetIntLE(i, value);
            }
        }

        random = new Random(seed);
        for (int i = 0; i < buffer.Capacity - 3; i += 4)
        {
            int value = NextSignedInt32();
            if (testBigEndian)
            {
                Assert.Equal(value, buffer.GetInt(i));
            }
            else
            {
                Assert.Equal(value, buffer.GetIntLE(i));
            }
        }
    }

    private void AssertRandomUnsignedIntAccess(bool testBigEndian)
    {
        for (int i = 0; i < buffer.Capacity - 3; i += 4)
        {
            int value = NextSignedInt32();
            if (testBigEndian)
            {
                buffer.SetInt(i, value);
            }
            else
            {
                buffer.SetIntLE(i, value);
            }
        }

        random = new Random(seed);
        for (int i = 0; i < buffer.Capacity - 3; i += 4)
        {
            long value = NextSignedInt32() & 0xFFFFFFFFL;
            if (testBigEndian)
            {
                Assert.Equal(value, buffer.GetUnsignedInt(i));
            }
            else
            {
                Assert.Equal(value, buffer.GetUnsignedIntLE(i));
            }
        }
    }

    private void AssertRandomLongAccess(bool testBigEndian)
    {
        for (int i = 0; i < buffer.Capacity - 7; i += 8)
        {
            long value = NextSignedInt64();
            if (testBigEndian)
            {
                buffer.SetLong(i, value);
            }
            else
            {
                buffer.SetLongLE(i, value);
            }
        }

        random = new Random(seed);
        for (int i = 0; i < buffer.Capacity - 7; i += 8)
        {
            long value = NextSignedInt64();
            if (testBigEndian)
            {
                Assert.Equal(value, buffer.GetLong(i));
            }
            else
            {
                Assert.Equal(value, buffer.GetLongLE(i));
            }
        }
    }

    private void AssertRandomFloatAccess(bool testBigEndian)
    {
        for (int i = 0; i < buffer.Capacity - 7; i += 8)
        {
            float value = random.NextSingle();
            if (testBigEndian)
            {
                buffer.SetFloat(i, value);
            }
            else
            {
                buffer.SetFloatLE(i, value);
            }
        }

        random = new Random(seed);
        for (int i = 0; i < buffer.Capacity - 7; i += 8)
        {
            float expected = random.NextSingle();
            float actual = testBigEndian? buffer.GetFloat(i) : buffer.GetFloatLE(i);
            Assert.Equal((double)expected, (double)actual, 0.01);
        }
    }

    private void AssertRandomDoubleAccess(bool testBigEndian)
    {
        for (int i = 0; i < buffer.Capacity - 7; i += 8)
        {
            double value = random.NextDouble();
            if (testBigEndian)
            {
                buffer.SetDouble(i, value);
            }
            else
            {
                buffer.SetDoubleLE(i, value);
            }
        }

        random = new Random(seed);
        for (int i = 0; i < buffer.Capacity - 7; i += 8)
        {
            double expected = random.NextDouble();
            double actual = testBigEndian? buffer.GetDouble(i) : buffer.GetDoubleLE(i);
            Assert.Equal(expected, actual, 0.01);
        }
    }

    private void AssertSequentialShortAccess(bool testBigEndian)
    {
        buffer.WriterIndex = 0;
        for (int i = 0; i < buffer.Capacity; i += 2)
        {
            short value = unchecked((short)NextSignedInt32());
            Assert.Equal(i, buffer.WriterIndex);
            Assert.True(buffer.IsWritable);
            if (testBigEndian)
            {
                buffer.WriteShort(value);
            }
            else
            {
                buffer.WriteShortLE(value);
            }
        }

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsWritable);

        random = new Random(seed);
        for (int i = 0; i < buffer.Capacity; i += 2)
        {
            short value = unchecked((short)NextSignedInt32());
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.True(buffer.IsReadable);
            if (testBigEndian)
            {
                Assert.Equal(value, buffer.ReadShort());
            }
            else
            {
                Assert.Equal(value, buffer.ReadShortLE());
            }
        }

        Assert.Equal(buffer.Capacity, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsReadable);
        Assert.False(buffer.IsWritable);
    }

    private void AssertSequentialUnsignedShortAccess(bool testBigEndian)
    {
        buffer.WriterIndex = 0;
        for (int i = 0; i < buffer.Capacity; i += 2)
        {
            short value = unchecked((short)NextSignedInt32());
            Assert.Equal(i, buffer.WriterIndex);
            Assert.True(buffer.IsWritable);
            if (testBigEndian)
            {
                buffer.WriteShort(value);
            }
            else
            {
                buffer.WriteShortLE(value);
            }
        }

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsWritable);

        random = new Random(seed);
        for (int i = 0; i < buffer.Capacity; i += 2)
        {
            int value = NextSignedInt32() & 0xFFFF;
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.True(buffer.IsReadable);
            if (testBigEndian)
            {
                Assert.Equal(value, buffer.ReadUnsignedShort());
            }
            else
            {
                Assert.Equal(value, buffer.ReadUnsignedShortLE());
            }
        }

        Assert.Equal(buffer.Capacity, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsReadable);
        Assert.False(buffer.IsWritable);
    }

    private void AssertSequentialMediumAccess(bool testBigEndian)
    {
        buffer.WriterIndex = 0;
        for (int i = 0; i < buffer.Capacity / 3 * 3; i += 3)
        {
            int value = NextSignedInt32();
            Assert.Equal(i, buffer.WriterIndex);
            Assert.True(buffer.IsWritable);
            if (testBigEndian)
            {
                buffer.WriteMedium(value);
            }
            else
            {
                buffer.WriteMediumLE(value);
            }
        }

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity / 3 * 3, buffer.WriterIndex);
        Assert.Equal(buffer.Capacity % 3, buffer.WritableBytes);

        random = new Random(seed);
        for (int i = 0; i < buffer.Capacity / 3 * 3; i += 3)
        {
            int value = NextSignedInt32() << 8 >> 8;
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.True(buffer.IsReadable);
            if (testBigEndian)
            {
                Assert.Equal(value, buffer.ReadMedium());
            }
            else
            {
                Assert.Equal(value, buffer.ReadMediumLE());
            }
        }

        Assert.Equal(buffer.Capacity / 3 * 3, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity / 3 * 3, buffer.WriterIndex);
        Assert.Equal(0, buffer.ReadableBytes);
        Assert.Equal(buffer.Capacity % 3, buffer.WritableBytes);
    }

    private void AssertSequentialUnsignedMediumAccess(bool testBigEndian)
    {
        buffer.WriterIndex = 0;
        for (int i = 0; i < buffer.Capacity / 3 * 3; i += 3)
        {
            int value = NextSignedInt32() & 0x00FFFFFF;
            Assert.Equal(i, buffer.WriterIndex);
            Assert.True(buffer.IsWritable);
            if (testBigEndian)
            {
                buffer.WriteMedium(value);
            }
            else
            {
                buffer.WriteMediumLE(value);
            }
        }

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity / 3 * 3, buffer.WriterIndex);
        Assert.Equal(buffer.Capacity % 3, buffer.WritableBytes);

        random = new Random(seed);
        for (int i = 0; i < buffer.Capacity / 3 * 3; i += 3)
        {
            int value = NextSignedInt32() & 0x00FFFFFF;
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.True(buffer.IsReadable);
            if (testBigEndian)
            {
                Assert.Equal(value, buffer.ReadUnsignedMedium());
            }
            else
            {
                Assert.Equal(value, buffer.ReadUnsignedMediumLE());
            }
        }

        Assert.Equal(buffer.Capacity / 3 * 3, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity / 3 * 3, buffer.WriterIndex);
        Assert.Equal(0, buffer.ReadableBytes);
        Assert.Equal(buffer.Capacity % 3, buffer.WritableBytes);
    }

    private void AssertSequentialIntAccess(bool testBigEndian)
    {
        buffer.WriterIndex = 0;
        for (int i = 0; i < buffer.Capacity; i += 4)
        {
            int value = NextSignedInt32();
            Assert.Equal(i, buffer.WriterIndex);
            Assert.True(buffer.IsWritable);
            if (testBigEndian)
            {
                buffer.WriteInt(value);
            }
            else
            {
                buffer.WriteIntLE(value);
            }
        }

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsWritable);

        random = new Random(seed);
        for (int i = 0; i < buffer.Capacity; i += 4)
        {
            int value = NextSignedInt32();
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.True(buffer.IsReadable);
            if (testBigEndian)
            {
                Assert.Equal(value, buffer.ReadInt());
            }
            else
            {
                Assert.Equal(value, buffer.ReadIntLE());
            }
        }

        Assert.Equal(buffer.Capacity, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsReadable);
        Assert.False(buffer.IsWritable);
    }

    private void AssertSequentialUnsignedIntAccess(bool testBigEndian)
    {
        buffer.WriterIndex = 0;
        for (int i = 0; i < buffer.Capacity; i += 4)
        {
            int value = NextSignedInt32();
            Assert.Equal(i, buffer.WriterIndex);
            Assert.True(buffer.IsWritable);
            if (testBigEndian)
            {
                buffer.WriteInt(value);
            }
            else
            {
                buffer.WriteIntLE(value);
            }
        }

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsWritable);

        random = new Random(seed);
        for (int i = 0; i < buffer.Capacity; i += 4)
        {
            long value = NextSignedInt32() & 0xFFFFFFFFL;
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.True(buffer.IsReadable);
            if (testBigEndian)
            {
                Assert.Equal(value, buffer.ReadUnsignedInt());
            }
            else
            {
                Assert.Equal(value, buffer.ReadUnsignedIntLE());
            }
        }

        Assert.Equal(buffer.Capacity, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsReadable);
        Assert.False(buffer.IsWritable);
    }

    private void AssertSequentialLongAccess(bool testBigEndian)
    {
        buffer.WriterIndex = 0;
        for (int i = 0; i < buffer.Capacity; i += 8)
        {
            long value = NextSignedInt64();
            Assert.Equal(i, buffer.WriterIndex);
            Assert.True(buffer.IsWritable);
            if (testBigEndian)
            {
                buffer.WriteLong(value);
            }
            else
            {
                buffer.WriteLongLE(value);
            }
        }

        Assert.Equal(0, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsWritable);

        random = new Random(seed);
        for (int i = 0; i < buffer.Capacity; i += 8)
        {
            long value = NextSignedInt64();
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.True(buffer.IsReadable);
            if (testBigEndian)
            {
                Assert.Equal(value, buffer.ReadLong());
            }
            else
            {
                Assert.Equal(value, buffer.ReadLongLE());
            }
        }

        Assert.Equal(buffer.Capacity, buffer.ReaderIndex);
        Assert.Equal(buffer.Capacity, buffer.WriterIndex);
        Assert.False(buffer.IsReadable);
        Assert.False(buffer.IsWritable);
    }

}
