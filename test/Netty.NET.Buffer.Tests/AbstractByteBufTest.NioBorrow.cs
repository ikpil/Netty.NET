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

namespace Netty.NET.Buffer.Tests;

public abstract partial class AbstractByteBufTest
{
    protected void AssertOriginalNioBuffer1()
    {
        Assert.True(buffer.IsContiguous);
        byte[] value = new byte[buffer.Capacity];
        random.NextBytes(value);
        buffer.Clear();
        buffer.WriteBytes(value);
        AssertRemainingEquals(value, buffer.ReadableMemory);
    }

    protected void AssertOriginalToByteBuffer2()
    {
        Assert.True(buffer.IsContiguous);
        byte[] value = new byte[buffer.Capacity];
        random.NextBytes(value);
        buffer.Clear();
        buffer.WriteBytes(value);
        for (int i = 0; i < buffer.Capacity - BLOCK_SIZE + 1; i += BLOCK_SIZE)
            AssertRemainingEquals(value.AsSpan(i, BLOCK_SIZE), buffer.AsReadOnlyMemory(i, BLOCK_SIZE));
    }

    private static void AssertRemainingEquals(ReadOnlySpan<byte> expected, ReadOnlyMemory<byte> actual)
    {
        int remaining = expected.Length;
        int remaining2 = actual.Length;
        Assert.Equal(remaining, remaining2);
        byte[] array1 = new byte[remaining];
        byte[] array2 = new byte[remaining2];
        expected.CopyTo(array1);
        actual.Span.CopyTo(array2);
        Assert.Equal(array1, array2);
    }

    protected void AssertOriginalInternalNioBufferGrowth()
    {
        AssertInternalBorrowAfterGrowth(128);
        AssertInternalBorrowAfterGrowth(1024);
        AssertInternalBorrowAfterGrowth(4 * 1024);
        AssertInternalBorrowAfterGrowth(64 * 1024);
        AssertInternalBorrowAfterGrowth(32 * 1024 * 1024);
        AssertInternalBorrowAfterGrowth(64 * 1024 * 1024);
    }

    private void AssertInternalBorrowAfterGrowth(int a)
    {
        ByteBuf buffer = NewBuffer(2);
        try
        {
            ReadOnlyMemory<byte> buf = buffer.AsReadOnlyMemory(buffer.ReaderIndex, 1);
            Assert.Equal(1, buf.Length);
            byte[] data = new byte[a];
            Random.Shared.NextBytes(data);
            buffer.WriteBytes(data);

            // Reacquire after growth. A prior CLR borrow must not be used across resize.
            buf = buffer.AsReadOnlyMemory(buffer.ReaderIndex, a);
            Assert.Equal(a, buf.Length);
            var reader = new SequenceReader<byte>(new ReadOnlySequence<byte>(buf));
            for (int i = 0; i < a; i++)
            {
                byte actual = reader.UnreadSpan[0];
                reader.Advance(1);
                // Preserve every original byte comparison and first-failure order;
                // avoid generic assertion dispatch for hundreds of millions of equal bytes.
                if (data[i] != actual) Assert.Equal(data[i], actual);
            }
            Assert.True(reader.End);
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void TestGetReadOnlyDirectDst() => AssertGetReadOnlyDst(true);

    [Fact]
    public void TestGetReadOnlyHeapDst() => AssertGetReadOnlyDst(false);

    private void AssertGetReadOnlyDst(bool direct)
    {
        byte[] bytes = { (byte)'a', (byte)'b', (byte)'c', (byte)'d' };
        ByteBuf buffer = NewBuffer(bytes.Length);
        try
        {
            buffer.WriteBytes(bytes);
            ByteBuf dst = direct ? DirectBuffer(bytes.Length) : Own(_peerAllocator.HeapBuffer(bytes.Length));
            dst.SetZero(0, bytes.Length);
            ByteBuf readOnlyDst = dst.AsReadOnly();
            // CLR Span rejects ReadOnlySpan at compile time. ByteBuf destinations
            // additionally preserve the original dynamic write-permission rejection.
            Assert.Throws<NotSupportedException>(() => buffer.GetBytes(0, readOnlyDst));
            Assert.Equal(0, readOnlyDst.WriterIndex);
            Assert.Equal(new byte[bytes.Length], ByteBufUtil.GetBytes(dst, 0, bytes.Length));
        }
        finally { buffer.Release(); }
    }

    [Fact]
    public void TestReadyOnlyNioBuffer() => AssertReadyOnlyNioBuffer(buffer.AsReadOnly());

    [Fact]
    public void TestReadyOnlySliceNioBuffer() => AssertReadyOnlyNioBuffer(buffer.AsReadOnly().Slice());

    [Fact]
    public void TestReadyOnlyDuplicateNioBuffer() => AssertReadyOnlyNioBuffer(buffer.AsReadOnly().Duplicate());

    private static void AssertReadyOnlyNioBuffer(ByteBuf buffer)
    {
        Assert.True(buffer.IsReadOnly);
        // ReadOnlyMemory/ReadOnlySpan carry write prohibition in their types, not an isReadOnly flag.
        ReadOnlyMemory<byte> memory = buffer.ReadableMemory;
        Assert.Equal(buffer.ReadableBytes, memory.Length);
        Assert.Throws<NotSupportedException>(() => buffer.AsMemory(buffer.ReaderIndex, buffer.ReadableBytes));
    }

    [Fact]
    public void TestReadyOnlyNioBufferWithPositionLength() => AssertReadyOnlyNioBufferWithPositionLength(buffer.AsReadOnly());

    [Fact]
    public void TestReadyOnlySliceNioBufferWithPositionLength() => AssertReadyOnlyNioBufferWithPositionLength(buffer.AsReadOnly().Slice());

    [Fact]
    public void TestReadyOnlyDuplicateNioBufferWithPositionLength() => AssertReadyOnlyNioBufferWithPositionLength(buffer.AsReadOnly().Duplicate());

    private static void AssertReadyOnlyNioBufferWithPositionLength(ByteBuf buffer)
    {
        Assert.True(buffer.IsReadOnly);
        // CLR contiguous borrows never flatten composite storage. Use the sequence contract for that range.
        long length = buffer.IsContiguous ? buffer.AsReadOnlyMemory(0, buffer.Capacity).Length
            : buffer.AsReadOnlySequence(0, buffer.Capacity).Length;
        Assert.Equal(buffer.Capacity, length);
        Assert.Throws<NotSupportedException>(() => buffer.AsMemory(0, buffer.Capacity));
    }

    [Fact]
    public void TestReadyOnlyNioBuffers() => AssertReadyOnlyNioBuffers(buffer.AsReadOnly());

    [Fact]
    public void TestReadyOnlySliceNioBuffers() => AssertReadyOnlyNioBuffers(buffer.AsReadOnly().Slice());

    [Fact]
    public void TestReadyOnlyDuplicateNioBuffers() => AssertReadyOnlyNioBuffers(buffer.AsReadOnly().Duplicate());

    private static void AssertReadyOnlyNioBuffers(ByteBuf buffer)
    {
        Assert.True(buffer.IsReadOnly);
        ReadOnlySequence<byte> sequence = buffer.ReadableSequence;
        Assert.Equal(buffer.ReadableBytes, sequence.Length);
        // Each segment is statically ReadOnlyMemory<byte>; compiler probes check its write prohibition.
        Assert.Throws<NotSupportedException>(() => buffer.AsMemory(buffer.ReaderIndex, buffer.ReadableBytes));
    }

    [Fact]
    public void TestReadyOnlyNioBuffersWithPositionLength() => AssertReadyOnlyNioBuffersWithPositionLength(buffer.AsReadOnly());

    [Fact]
    public void TestReadyOnlySliceNioBuffersWithPositionLength() => AssertReadyOnlyNioBuffersWithPositionLength(buffer.AsReadOnly().Slice());

    [Fact]
    public void TestReadyOnlyDuplicateNioBuffersWithPositionLength() => AssertReadyOnlyNioBuffersWithPositionLength(buffer.AsReadOnly().Duplicate());

    private static void AssertReadyOnlyNioBuffersWithPositionLength(ByteBuf buffer)
    {
        Assert.True(buffer.IsReadOnly);
        ReadOnlySequence<byte> sequence = buffer.AsReadOnly().AsReadOnlySequence(0, buffer.Capacity);
        Assert.Equal(buffer.Capacity, sequence.Length);
        Assert.Throws<NotSupportedException>(() => buffer.AsMemory(0, buffer.Capacity));
    }

    [Fact]
    public void TestInternalNioBufferHelper()
    {
        ByteBuf buf = Unpooled.Buffer(8, 16);
        try
        {
            // _internalNioBuffer exposes capacity, not just the currently empty readable region.
            var reader = new SequenceReader<byte>(new ReadOnlySequence<byte>(buf.AsReadOnlyMemory(0, buf.Capacity)));
            Assert.Equal(0, reader.Consumed);
            Assert.True(reader.Remaining >= 8);
        }
        finally { buf.Release(); }
    }
}
