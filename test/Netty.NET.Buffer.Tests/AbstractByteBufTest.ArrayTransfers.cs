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
    public void GetByteArrayBoundaryCheck1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetBytes(-1, Array.Empty<byte>());
        });
    }

    [Fact]
    public void GetByteArrayBoundaryCheck2()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            buffer.GetBytes(-1, Array.Empty<byte>().AsSpan(0, 0));
        });
    }

    [Fact]
    public void GetByteArrayBoundaryCheck3()
    {
        byte[] dst = new byte[4];
        buffer.SetInt(0, 0x01020304);
        // CLR: AsSpan rejects this array range before GetBytes can be invoked.
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetBytes(0, dst.AsSpan(-1, 4)));

        // No partial copy is expected.
        Assert.Equal(0, dst[0]);
        Assert.Equal(0, dst[1]);
        Assert.Equal(0, dst[2]);
        Assert.Equal(0, dst[3]);
    }

    [Fact]
    public void GetByteArrayBoundaryCheck4()
    {
        byte[] dst = new byte[4];
        buffer.SetInt(0, 0x01020304);
        // CLR: AsSpan rejects this array range before GetBytes can be invoked.
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetBytes(0, dst.AsSpan(1, 4)));

        // No partial copy is expected.
        Assert.Equal(0, dst[0]);
        Assert.Equal(0, dst[1]);
        Assert.Equal(0, dst[2]);
        Assert.Equal(0, dst[3]);
    }

    [Fact]
    public void TestByteArrayTransfer()
    {
        byte[] value = new byte[BLOCK_SIZE * 2];
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(value);
            buffer.SetBytes(i, value.AsSpan(random.Next(BLOCK_SIZE), BLOCK_SIZE));
        }

        random = new Random(seed);
        byte[] expectedValue = new byte[BLOCK_SIZE * 2];
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValue);
            int valueOffset = random.Next(BLOCK_SIZE);
            buffer.GetBytes(i, value.AsSpan(valueOffset, BLOCK_SIZE));
            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue[j], value[j]);
            }
        }
    }

    [Fact]
    public void TestRandomByteArrayTransfer1()
    {
        byte[] value = new byte[BLOCK_SIZE];
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(value);
            buffer.SetBytes(i, value);
        }

        random = new Random(seed);
        byte[] expectedValueContent = new byte[BLOCK_SIZE];
        ByteBuf expectedValue = WrappedBuffer(expectedValueContent);
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValueContent);
            buffer.GetBytes(i, value);
            for (int j = 0; j < BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue.GetByte(j), value[j]);
            }
        }
    }

    [Fact]
    public void TestRandomByteArrayTransfer2()
    {
        byte[] value = new byte[BLOCK_SIZE * 2];
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(value);
            buffer.SetBytes(i, value.AsSpan(random.Next(BLOCK_SIZE), BLOCK_SIZE));
        }

        random = new Random(seed);
        byte[] expectedValueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf expectedValue = WrappedBuffer(expectedValueContent);
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValueContent);
            int valueOffset = random.Next(BLOCK_SIZE);
            buffer.GetBytes(i, value.AsSpan(valueOffset, BLOCK_SIZE));
            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue.GetByte(j), value[j]);
            }
        }
    }

    [Fact]
    public void TestSequentialByteArrayTransfer1()
    {
        byte[] value = new byte[BLOCK_SIZE];
        buffer.WriterIndex = 0;
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(value);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(i, buffer.WriterIndex);
            buffer.WriteBytes(value);
        }

        random = new Random(seed);
        byte[] expectedValue = new byte[BLOCK_SIZE];
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValue);
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.Equal(CAPACITY, buffer.WriterIndex);
            buffer.ReadBytes(value);
            for (int j = 0; j < BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue[j], value[j]);
            }
        }
    }

    [Fact]
    public void TestSequentialByteArrayTransfer2()
    {
        byte[] value = new byte[BLOCK_SIZE * 2];
        buffer.WriterIndex = 0;
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(value);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(i, buffer.WriterIndex);
            int readerIndex = random.Next(BLOCK_SIZE);
            buffer.WriteBytes(value.AsSpan(readerIndex, BLOCK_SIZE));
        }

        random = new Random(seed);
        byte[] expectedValue = new byte[BLOCK_SIZE * 2];
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValue);
            int valueOffset = random.Next(BLOCK_SIZE);
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.Equal(CAPACITY, buffer.WriterIndex);
            buffer.ReadBytes(value.AsSpan(valueOffset, BLOCK_SIZE));
            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue[j], value[j]);
            }
        }
    }

    [Fact]
    public void TestGetBytesAfterRelease4()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetBytes(0, new byte[8]);
        });
    }

    [Fact]
    public void TestGetBytesAfterRelease5()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().GetBytes(0, new byte[8].AsSpan(0, 1));
        });
    }

    [Fact]
    public void TestSetBytesAfterRelease4()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetBytes(0, new byte[8]);
        });
    }

    [Fact]
    public void TestSetBytesAfterRelease5()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().SetBytes(0, new byte[8].AsSpan(0, 1));
        });
    }

    [Fact]
    public void TestReadBytesAfterRelease5()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadBytes(new byte[8]);
        });
    }

    [Fact]
    public void TestReadBytesAfterRelease6()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().ReadBytes(new byte[8].AsSpan(0, 1));
        });
    }

    [Fact]
    public void TestWriteBytesAfterRelease4()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteBytes(new byte[8]);
        });
    }

    [Fact]
    public void TestWriteBytesAfterRelease5()
    {
        Assert.Throws<IllegalReferenceCountException>(() =>
        {
            ReleasedBuffer().WriteBytes(new byte[8].AsSpan(0, 1));
        });
    }

}
