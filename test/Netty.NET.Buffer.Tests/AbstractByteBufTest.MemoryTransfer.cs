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
using System.Buffers;
using System.Runtime.InteropServices;

namespace Netty.NET.Buffer.Tests;

public abstract partial class AbstractByteBufTest
{
    [Fact]
    public void GetByteBufferBoundaryCheck()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetBytes(-1, new byte[0]));
    }

    [Fact]
    public void GetByteBufferState()
    {
        byte[] dst = new byte[4];
        Memory<byte> window = dst.AsMemory(1, 2);
        buffer.SetByte(0, 1);
        buffer.SetByte(1, 2);
        buffer.SetByte(2, 3);
        buffer.SetByte(3, 4);
        int readerIndex = buffer.ReaderIndex;
        int writerIndex = buffer.WriterIndex;
        buffer.GetBytes(1, window.Span);

        // CLR: immutable range bounds replace ByteBuffer position/limit; no synthetic cursor is advanced.
        Assert.Equal((byte)0, dst[0]);
        Assert.Equal((byte)2, dst[1]);
        Assert.Equal((byte)3, dst[2]);
        Assert.Equal((byte)0, dst[3]);
        Assert.Equal(readerIndex, buffer.ReaderIndex);
        Assert.Equal(writerIndex, buffer.WriterIndex);
    }

    [Fact]
    public unsafe void GetDirectByteBufferBoundaryCheck()
    {
        nint address = (nint)NativeMemory.AllocZeroed(1);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetBytes(-1, new Span<byte>((void*)address, 0)));
        }
        finally { NativeMemory.Free((void*)address); }
    }

    [Fact]
    public unsafe void GetDirectByteBufferState()
    {
        void* allocation = NativeMemory.AllocZeroed(4);
        try
        {
            Span<byte> dst = new(allocation, 4);
            Span<byte> window = dst.Slice(1, 2);
            buffer.SetByte(0, 1);
            buffer.SetByte(1, 2);
            buffer.SetByte(2, 3);
            buffer.SetByte(3, 4);
            int readerIndex = buffer.ReaderIndex;
            int writerIndex = buffer.WriterIndex;
            buffer.GetBytes(1, window);

            // JVM direct allocation is zero-initialized; AllocZeroed preserves the two original guard bytes.
            Assert.Equal((byte)0, dst[0]);
            Assert.Equal((byte)2, dst[1]);
            Assert.Equal((byte)3, dst[2]);
            Assert.Equal((byte)0, dst[3]);
            Assert.Equal(readerIndex, buffer.ReaderIndex);
            Assert.Equal(writerIndex, buffer.WriterIndex);
        }
        finally { NativeMemory.Free(allocation); }
    }

    [Fact]
    public void TestRandomByteBufferTransfer()
    {
        byte[] value = new byte[BLOCK_SIZE * 2];
        int readerIndex = buffer.ReaderIndex;
        int writerIndex = buffer.WriterIndex;
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(value);
            int valueOffset = random.Next(BLOCK_SIZE);
            buffer.SetBytes(i, value.AsSpan(valueOffset, BLOCK_SIZE));
        }

        random = new Random(seed);
        byte[] expectedValue = new byte[BLOCK_SIZE * 2];
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValue);
            int valueOffset = random.Next(BLOCK_SIZE);
            Memory<byte> window = value.AsMemory(valueOffset, BLOCK_SIZE);
            byte[] before = (byte[])value.Clone();
            buffer.GetBytes(i, window.Span);
            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
                Assert.Equal(expectedValue[j], value[j]);

            // Preserve the selected external range, including bytes before and after it.
            Assert.Equal(before.AsSpan(0, valueOffset).ToArray(), value.AsSpan(0, valueOffset).ToArray());
            Assert.Equal(before.AsSpan(valueOffset + BLOCK_SIZE).ToArray(), value.AsSpan(valueOffset + BLOCK_SIZE).ToArray());
            Assert.Equal(readerIndex, buffer.ReaderIndex);
            Assert.Equal(writerIndex, buffer.WriterIndex);
        }
    }

    [Fact]
    public void TestSequentialByteBufferTransfer()
    {
        buffer.WriterIndex = 0;
        byte[] value = new byte[BLOCK_SIZE * 2];
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(value);
            int valueOffset = random.Next(BLOCK_SIZE);
            buffer.WriteBytes(value.AsSpan(valueOffset, BLOCK_SIZE));
            Assert.Equal(i + BLOCK_SIZE, buffer.WriterIndex);
        }

        random = new Random(seed);
        byte[] expectedValue = new byte[BLOCK_SIZE * 2];
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
        {
            random.NextBytes(expectedValue);
            int valueOffset = random.Next(BLOCK_SIZE);
            Memory<byte> window = value.AsMemory(valueOffset, BLOCK_SIZE);
            byte[] before = (byte[])value.Clone();
            buffer.ReadBytes(window.Span);
            for (int j = valueOffset; j < valueOffset + BLOCK_SIZE; j++)
                Assert.Equal(expectedValue[j], value[j]);

            // Span has no advancing cursor; the real ByteBuf must consume the entire selected range.
            Assert.Equal(i + BLOCK_SIZE, buffer.ReaderIndex);
            Assert.Equal(before.AsSpan(0, valueOffset).ToArray(), value.AsSpan(0, valueOffset).ToArray());
            Assert.Equal(before.AsSpan(valueOffset + BLOCK_SIZE).ToArray(), value.AsSpan(valueOffset + BLOCK_SIZE).ToArray());
        }
    }

    [Fact]
    public void TestNioBufferExposeOnlyRegion()
    {
        ByteBuf buffer = NewBuffer(8);
        try
        {
            byte[] data = new byte[8];
            random.NextBytes(data);
            buffer.WriteBytes(data);
            var reader = new SequenceReader<byte>(buffer.AsReadOnlySequence(1, data.Length - 2));
            Assert.Equal(0, reader.Consumed);
            Assert.Equal(6, reader.Remaining);
            for (int i = 1; reader.TryRead(out byte actual); i++)
                Assert.Equal(data[i], actual);
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void TestEmptyNioBuffers()
    {
        ByteBuf buffer = NewBuffer(8);
        try
        {
            buffer.Clear();
            Assert.False(buffer.IsReadable);
            ReadOnlySequence<byte> sequence = buffer.ReadableSequence;
            // CLR: an empty sequence is one logical empty range, without a Java array or cursor object.
            Assert.True(sequence.IsSingleSegment);
            Assert.True(sequence.First.IsEmpty);
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void TestReadBytes()
    {
        ByteBuf buffer = NewBuffer(8);
        ByteBuf buffer2 = null;
        try
        {
            byte[] bytes = new byte[8];
            buffer.WriteBytes(bytes);
            buffer2 = buffer.ReadBytes(4);
            Assert.Same(buffer.Allocator, buffer2.Allocator);
            Assert.Equal(4, buffer.ReaderIndex);
            Assert.True(buffer.Release());
            Assert.Equal(0, buffer.ReferenceCount);
            Assert.True(buffer2.Release());
            Assert.Equal(0, buffer2.ReferenceCount);
        }
        finally
        {
            if (buffer.ReferenceCount > 0) buffer.Release();
            if (buffer2?.ReferenceCount > 0) buffer2.Release();
        }
    }

    [Fact]
    public void TestGetBytesByteBuffer()
    {
        byte[] bytes = { (byte)'a', (byte)'b', (byte)'c', (byte)'d', (byte)'e', (byte)'f', (byte)'g' };
        // Ensure destination buffer is bigger then what is in the ByteBuf.
        byte[] nioBuffer = new byte[bytes.Length + 1];
        ByteBuf buffer = NewBuffer(bytes.Length);
        try
        {
            buffer.WriteBytes(bytes);
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetBytes(buffer.ReaderIndex, nioBuffer));
        }
        finally { buffer.Release(); }
    }
}
