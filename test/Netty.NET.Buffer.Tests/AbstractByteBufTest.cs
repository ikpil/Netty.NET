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
using System.IO;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

/**
 * An abstract test class for channel buffers
 */
public abstract class AbstractByteBufTest : IDisposable
{
    private const int CAPACITY = 4096; // Must be even
    private const int BLOCK_SIZE = 128;
    private int seed;
    private Random random;
    private ByteBuf buffer;
    private readonly List<ByteBuf> _peers = new();
    protected readonly NativeMemoryAllocator Native = new();
    protected IByteBufAllocator Allocator;
    private UnpooledByteBufAllocator _peerAllocator;

    protected ByteBuf NewBuffer(int capacity) => NewBuffer(capacity, int.MaxValue);
    protected abstract ByteBuf NewBuffer(int capacity, int maxCapacity);

    protected void Init(IByteBufAllocator allocator)
    {
        Allocator = allocator;
        _peerAllocator = new UnpooledByteBufAllocator(false, Native);
        buffer = NewBuffer(CAPACITY);
        seed = unchecked((int)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        random = new Random(seed);
    }

    // Unpooled peer storage is independent of the fixture's actual root allocator.
    private ByteBuf WrappedBuffer(byte[] bytes) => Own(Unpooled.WrappedBuffer(bytes));
    private ByteBuf WrappedMemory(Memory<byte> bytes) => Own(Unpooled.WrappedBuffer(bytes));
    private ByteBuf DirectBuffer(int capacity) => Own(_peerAllocator.DirectBuffer(capacity));
    private ByteBuf Own(ByteBuf value) { _peers.Add(value); return value; }

    public void Dispose()
    {
        try
        {
            if (buffer != null)
            {
                Assert.True(buffer.Release());
                Assert.Equal(0, buffer.ReferenceCount);
                try
                { buffer.Release(); }
                catch (Exception)
                {
                    // Ignore.
                }
                buffer = null;
            }
        }
        finally
        {
            // Heap peers omitted by the Java GC-owned fixture also have CLR reference-counted facades.
            foreach (ByteBuf peer in _peers)
            if (peer.ReferenceCount > 0)
                peer.Release();
            (Allocator as IDisposable)?.Dispose();
            Assert.Equal(0, Native.ReservedBytes);
        }
    }

    [Fact]
    public void TestRandomHeapBufferTransfer1()
    {
        byte[] valueContent = new byte[BLOCK_SIZE];
        ByteBuf value = WrappedBuffer(valueContent);
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(valueContent);
            value.SetIndex(0, BLOCK_SIZE);
            buffer.SetBytes(i, value);
            Assert.Equal(BLOCK_SIZE, value.ReaderIndex);
            Assert.Equal(BLOCK_SIZE, value.WriterIndex);
        }

        random = new Random(seed);
        byte[] expectedValueContent = new byte[BLOCK_SIZE];
        ByteBuf expectedValue = WrappedBuffer(expectedValueContent);
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValueContent);
            value.Clear();
            buffer.GetBytes(i, value);
            Assert.Equal(0, value.ReaderIndex);
            Assert.Equal(BLOCK_SIZE, value.WriterIndex);
            for (int j = 0; j < BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue.GetByte(j), value.GetByte(j));
            }
        }

    }

    [Fact]
    public void TestRandomHeapBufferTransfer2()
    {
        byte[] valueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf value = WrappedBuffer(valueContent);
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(valueContent);
            buffer.SetBytes(i, value, random.Next(BLOCK_SIZE), BLOCK_SIZE);
        }

        random = new Random(seed);
        byte[] expectedValueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf expectedValue = WrappedBuffer(expectedValueContent);
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValueContent);
            int valueOffset = random.Next(BLOCK_SIZE);
            buffer.GetBytes(i, value, valueOffset, BLOCK_SIZE);
            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue.GetByte(j), value.GetByte(j));
            }
        }

    }

    [Fact]
    public void TestRandomDirectBufferTransfer()
    {
        byte[] tmp = new byte[BLOCK_SIZE * 2];
        ByteBuf value = DirectBuffer(BLOCK_SIZE * 2);
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(tmp);
            value.SetBytes(0, tmp.AsSpan(0, value.Capacity));
            buffer.SetBytes(i, value, random.Next(BLOCK_SIZE), BLOCK_SIZE);
        }

        random = new Random(seed);
        ByteBuf expectedValue = DirectBuffer(BLOCK_SIZE * 2);
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(tmp);
            expectedValue.SetBytes(0, tmp.AsSpan(0, expectedValue.Capacity));
            int valueOffset = random.Next(BLOCK_SIZE);
            buffer.GetBytes(i, value, valueOffset, BLOCK_SIZE);
            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue.GetByte(j), value.GetByte(j));
            }
        }
        value.Release();
        expectedValue.Release();

    }

    [Fact]
    public void TestSequentialHeapBufferTransfer1()
    {
        byte[] valueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf value = WrappedBuffer(valueContent);
        buffer.WriterIndex = 0;
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(valueContent);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(i, buffer.WriterIndex);
            buffer.WriteBytes(value, random.Next(BLOCK_SIZE), BLOCK_SIZE);
            Assert.Equal(0, value.ReaderIndex);
            Assert.Equal(valueContent.Length, value.WriterIndex);
        }

        random = new Random(seed);
        byte[] expectedValueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf expectedValue = WrappedBuffer(expectedValueContent);
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValueContent);
            int valueOffset = random.Next(BLOCK_SIZE);
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.Equal(CAPACITY, buffer.WriterIndex);
            buffer.ReadBytes(value, valueOffset, BLOCK_SIZE);
            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue.GetByte(j), value.GetByte(j));
            }
            Assert.Equal(0, value.ReaderIndex);
            Assert.Equal(valueContent.Length, value.WriterIndex);
        }

    }

    [Fact]
    public void TestSequentialHeapBufferTransfer2()
    {
        byte[] valueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf value = WrappedBuffer(valueContent);
        buffer.WriterIndex = 0;
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(valueContent);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(i, buffer.WriterIndex);
            int readerIndex = random.Next(BLOCK_SIZE);
            value.ReaderIndex = readerIndex;
            value.WriterIndex = readerIndex + BLOCK_SIZE;
            buffer.WriteBytes(value);
            Assert.Equal(readerIndex + BLOCK_SIZE, value.WriterIndex);
            Assert.Equal(value.WriterIndex, value.ReaderIndex);
        }

        random = new Random(seed);
        byte[] expectedValueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf expectedValue = WrappedBuffer(expectedValueContent);
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValueContent);
            int valueOffset = random.Next(BLOCK_SIZE);
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.Equal(CAPACITY, buffer.WriterIndex);
            value.ReaderIndex = valueOffset;
            value.WriterIndex = valueOffset;
            buffer.ReadBytes(value, BLOCK_SIZE);
            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue.GetByte(j), value.GetByte(j));
            }
            Assert.Equal(valueOffset, value.ReaderIndex);
            Assert.Equal(valueOffset + BLOCK_SIZE, value.WriterIndex);
        }

    }

    [Fact]
    public void TestSequentialDirectBufferTransfer1()
    {
        byte[] valueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf value = DirectBuffer(BLOCK_SIZE * 2);
        buffer.WriterIndex = 0;
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(valueContent);
            value.SetBytes(0, valueContent);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(i, buffer.WriterIndex);
            buffer.WriteBytes(value, random.Next(BLOCK_SIZE), BLOCK_SIZE);
            Assert.Equal(0, value.ReaderIndex);
            Assert.Equal(0, value.WriterIndex);
        }

        random = new Random(seed);
        byte[] expectedValueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf expectedValue = WrappedBuffer(expectedValueContent);
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValueContent);
            int valueOffset = random.Next(BLOCK_SIZE);
            value.SetBytes(0, valueContent);
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.Equal(CAPACITY, buffer.WriterIndex);
            buffer.ReadBytes(value, valueOffset, BLOCK_SIZE);
            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue.GetByte(j), value.GetByte(j));
            }
            Assert.Equal(0, value.ReaderIndex);
            Assert.Equal(0, value.WriterIndex);
        }
        value.Release();
        expectedValue.Release();

    }

    [Fact]
    public void TestSequentialDirectBufferTransfer2()
    {
        byte[] valueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf value = DirectBuffer(BLOCK_SIZE * 2);
        buffer.WriterIndex = 0;
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(valueContent);
            value.SetBytes(0, valueContent);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(i, buffer.WriterIndex);
            int readerIndex = random.Next(BLOCK_SIZE);
            value.ReaderIndex = 0;
            value.WriterIndex = readerIndex + BLOCK_SIZE;
            value.ReaderIndex = readerIndex;
            buffer.WriteBytes(value);
            Assert.Equal(readerIndex + BLOCK_SIZE, value.WriterIndex);
            Assert.Equal(value.WriterIndex, value.ReaderIndex);
        }

        random = new Random(seed);
        byte[] expectedValueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf expectedValue = WrappedBuffer(expectedValueContent);
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValueContent);
            value.SetBytes(0, valueContent);
            int valueOffset = random.Next(BLOCK_SIZE);
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.Equal(CAPACITY, buffer.WriterIndex);
            value.ReaderIndex = valueOffset;
            value.WriterIndex = valueOffset;
            buffer.ReadBytes(value, BLOCK_SIZE);
            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue.GetByte(j), value.GetByte(j));
            }
            Assert.Equal(valueOffset, value.ReaderIndex);
            Assert.Equal(valueOffset + BLOCK_SIZE, value.WriterIndex);
        }
        value.Release();
        expectedValue.Release();

    }

    [Fact]
    public void TestSequentialByteBufferBackedHeapBufferTransfer1()
    {
        byte[] valueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf value = WrappedMemory(new byte[BLOCK_SIZE * 2].AsMemory());
        value.WriterIndex = 0;
        buffer.WriterIndex = 0;
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(valueContent);
            value.SetBytes(0, valueContent);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(i, buffer.WriterIndex);
            buffer.WriteBytes(value, random.Next(BLOCK_SIZE), BLOCK_SIZE);
            Assert.Equal(0, value.ReaderIndex);
            Assert.Equal(0, value.WriterIndex);
        }

        random = new Random(seed);
        byte[] expectedValueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf expectedValue = WrappedBuffer(expectedValueContent);
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValueContent);
            int valueOffset = random.Next(BLOCK_SIZE);
            value.SetBytes(0, valueContent);
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.Equal(CAPACITY, buffer.WriterIndex);
            buffer.ReadBytes(value, valueOffset, BLOCK_SIZE);
            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue.GetByte(j), value.GetByte(j));
            }
            Assert.Equal(0, value.ReaderIndex);
            Assert.Equal(0, value.WriterIndex);
        }

    }

    [Fact]
    public void TestSequentialByteBufferBackedHeapBufferTransfer2()
    {
        byte[] valueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf value = WrappedMemory(new byte[BLOCK_SIZE * 2].AsMemory());
        value.WriterIndex = 0;
        buffer.WriterIndex = 0;
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(valueContent);
            value.SetBytes(0, valueContent);
            Assert.Equal(0, buffer.ReaderIndex);
            Assert.Equal(i, buffer.WriterIndex);
            int readerIndex = random.Next(BLOCK_SIZE);
            value.ReaderIndex = 0;
            value.WriterIndex = readerIndex + BLOCK_SIZE;
            value.ReaderIndex = readerIndex;
            buffer.WriteBytes(value);
            Assert.Equal(readerIndex + BLOCK_SIZE, value.WriterIndex);
            Assert.Equal(value.WriterIndex, value.ReaderIndex);
        }

        random = new Random(seed);
        byte[] expectedValueContent = new byte[BLOCK_SIZE * 2];
        ByteBuf expectedValue = WrappedBuffer(expectedValueContent);
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValueContent);
            value.SetBytes(0, valueContent);
            int valueOffset = random.Next(BLOCK_SIZE);
            Assert.Equal(i, buffer.ReaderIndex);
            Assert.Equal(CAPACITY, buffer.WriterIndex);
            value.ReaderIndex = valueOffset;
            value.WriterIndex = valueOffset;
            buffer.ReadBytes(value, BLOCK_SIZE);
            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
            {
                Assert.Equal(expectedValue.GetByte(j), value.GetByte(j));
            }
            Assert.Equal(valueOffset, value.ReaderIndex);
            Assert.Equal(valueOffset + BLOCK_SIZE, value.WriterIndex);
        }

    }

    [Fact]
    public void TestSequentialCopiedBufferTransfer1()
    {
        buffer.WriterIndex = 0;
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            byte[] value = new byte[BLOCK_SIZE];
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
            ByteBuf actualValue = buffer.ReadBytes(BLOCK_SIZE);
            Assert.Equal(WrappedBuffer(expectedValue), actualValue);

            // Make sure if it is a copied buffer.
            actualValue.SetByte(0, unchecked((byte)(actualValue.GetByte(0) + 1)));
            Assert.False(buffer.GetByte(i) == actualValue.GetByte(0));
            actualValue.Release();
        }

    }

    [Fact]
    public void TestStreamTransfer1()
    {
        byte[] expected = new byte[buffer.Capacity];
        random.NextBytes(expected);
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            using var input = new MemoryStream(expected, i, BLOCK_SIZE, false);
            Assert.Equal(BLOCK_SIZE, buffer.SetBytes(i, input, BLOCK_SIZE));
            // CLR zero-length reads return zero; verify the original EOF condition independently.
            Assert.Equal(0, buffer.SetBytes(i, input, 0));
            Assert.Equal(-1, input.ReadByte());
        }
        using var output = new MemoryStream();
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            buffer.GetBytes(i, output, BLOCK_SIZE);
        }
        Assert.Equal(expected, output.ToArray());
    }

    [Fact]
    public void TestStreamTransfer2()
    {
        byte[] expected = new byte[buffer.Capacity];
        random.NextBytes(expected);
        buffer.Clear();
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            using var input = new MemoryStream(expected, i, BLOCK_SIZE, false);
            Assert.Equal(i, buffer.WriterIndex);
            buffer.WriteBytes(input, BLOCK_SIZE);
            Assert.Equal(i + BLOCK_SIZE, buffer.WriterIndex);
        }
        using var output = new MemoryStream();
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            Assert.Equal(i, buffer.ReaderIndex);
            buffer.ReadBytes(output, BLOCK_SIZE);
            Assert.Equal(i + BLOCK_SIZE, buffer.ReaderIndex);
        }
        Assert.Equal(expected, output.ToArray());
    }

}
