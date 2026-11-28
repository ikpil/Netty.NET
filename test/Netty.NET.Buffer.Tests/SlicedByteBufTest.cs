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
using System.Threading.Tasks;

namespace Netty.NET.Buffer.Tests;

/**
 * Tests sliced channel buffers
 */
// CLR: the original newBuffer assumes maxCapacity == Integer.MAX_VALUE, which skips the five
// finite-capacity bodies; this fixture therefore inherits AbstractSingleRegionByteBufTest instead of
// AbstractContiguousByteBufTest (see that class). ArgumentOutOfRangeException replaces
// IndexOutOfBoundsException in the overrides that expect the inherited body to fail.
public class SlicedByteBufTest : AbstractSingleRegionByteBufTest
{
    public SlicedByteBufTest()
    {
        Init(new UnpooledByteBufAllocator(false, Native));
    }

    protected sealed override ByteBuf NewBuffer(int length, int maxCapacity)
    {
        // CLR: inherited bodies only reach this factory with the maximum capacity, so the original
        // Assumptions.assumeTrue(maxCapacity == Integer.MAX_VALUE) is an assertion here.
        Assert.Equal(int.MaxValue, maxCapacity);
        int offset;
        // CLR: Random.Shared replaces ThreadLocalRandom.current()
        // (https://learn.microsoft.com/en-us/dotnet/api/system.random.shared?view=net-10.0).
        offset = length == 0 ? 0 : Random.Shared.Next(length);
        // CLR: the fixture's unpooled heap allocator stands in for Unpooled.buffer, as in BigEndianHeapByteBufTest.
        ByteBuf buffer = Allocator.HeapBuffer(length * 2);
        ByteBuf slice = NewSlice(buffer, offset, length);
        Assert.Equal(0, slice.ReaderIndex);
        Assert.Equal(length, slice.WriterIndex);
        return slice;
    }

    protected virtual ByteBuf NewSlice(ByteBuf buffer, int offset, int length)
    {
        return buffer.Slice(offset, length);
    }

    [Fact]
    public void TestIsContiguous()
    {
        ByteBuf buf = NewBuffer(4);
        Assert.Equal(buf.Unwrap().IsContiguous, buf.IsContiguous);
        buf.Release();
    }

    [Fact]
    public void ShouldNotAllowNullInConstructor()
    {
        // CLR: ArgumentNullException replaces NullPointerException; the internal ByteBufView slice
        // constructor replaces the deprecated public SlicedByteBuf constructor.
        Assert.Throws<ArgumentNullException>(() =>
        {
            new ByteBufView(null, 0, 0);
        });
    }

    [Fact]
    public override async Task TestInternalNioBuffer()
    {
        // CLR: ArgumentOutOfRangeException replaces IndexOutOfBoundsException.
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => base.TestInternalNioBuffer());
    }

    [Fact]
    public override void TestDuplicateReadGatheringByteChannelMultipleThreads()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            base.TestDuplicateReadGatheringByteChannelMultipleThreads();
        });
    }

    [Fact]
    public override void TestSliceReadGatheringByteChannelMultipleThreads()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            base.TestSliceReadGatheringByteChannelMultipleThreads();
        });
    }

    [Fact]
    public override void TestDuplicateReadOutputStreamMultipleThreads()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            base.TestDuplicateReadOutputStreamMultipleThreads();
        });
    }

    [Fact]
    public override void TestSliceReadOutputStreamMultipleThreads()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            base.TestSliceReadOutputStreamMultipleThreads();
        });
    }

    [Fact]
    public override async Task TestDuplicateBytesInArrayMultipleThreads()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => base.TestDuplicateBytesInArrayMultipleThreads());
    }

    [Fact]
    public override async Task TestSliceBytesInArrayMultipleThreads()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => base.TestSliceBytesInArrayMultipleThreads());
    }

    [Fact]
    public override void TestNioBufferExposeOnlyRegion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            base.TestNioBufferExposeOnlyRegion();
        });
    }

    [Fact]
    public override void TestGetReadOnlyDirectDst()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            base.TestGetReadOnlyDirectDst();
        });
    }

    [Fact]
    public override void TestGetReadOnlyHeapDst()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            base.TestGetReadOnlyHeapDst();
        });
    }

    [Fact]
    public override void TestLittleEndianWithExpand()
    {
        // ignore for SlicedByteBuf
    }

    [Fact]
    public override void TestReadBytes()
    {
        // ignore for SlicedByteBuf
    }

    [Fact]
    public override void TestForEachByteDesc2()
    {
        // Ignore for SlicedByteBuf
    }

    [Fact]
    public override void TestForEachByte2()
    {
        // Ignore for SlicedByteBuf
    }

    [Fact(Skip = "Sliced ByteBuf objects don't allow the capacity to change. So this test would fail and shouldn't be run")]
    public override void TestDuplicateCapacityChange()
    {
    }

    [Fact(Skip = "Sliced ByteBuf objects don't allow the capacity to change. So this test would fail and shouldn't be run")]
    public override void TestRetainedDuplicateCapacityChange()
    {
    }

    [Fact]
    public void TestReaderIndexAndMarks()
    {
        ByteBuf wrapped = Unpooled.Buffer(16);

        try
        {
            wrapped.WriterIndex = 14;
            wrapped.ReaderIndex = 2;
            wrapped.MarkWriterIndex();
            wrapped.MarkReaderIndex();
            ByteBuf slice = wrapped.Slice(4, 4);
            Assert.Equal(0, slice.ReaderIndex);
            Assert.Equal(4, slice.WriterIndex);

            slice.ReaderIndex = slice.ReaderIndex + 1;
            slice.ResetReaderIndex();
            Assert.Equal(0, slice.ReaderIndex);

            slice.WriterIndex = slice.WriterIndex - 1;
            slice.ResetWriterIndex();
            Assert.Equal(0, slice.WriterIndex);
        }
        finally
        {
            wrapped.Release();
        }
    }

    [Fact]
    public void SliceEmptyNotLeak()
    {
        ByteBuf buffer = Unpooled.Buffer(8).Retain();
        Assert.Equal(2, buffer.RefCnt);

        ByteBuf slice1 = buffer.Slice();
        Assert.Equal(2, slice1.RefCnt);

        ByteBuf slice2 = slice1.Slice();
        Assert.Equal(2, slice2.RefCnt);

        Assert.False(slice2.Release());
        Assert.Equal(1, buffer.RefCnt);
        Assert.Equal(1, slice1.RefCnt);
        Assert.Equal(1, slice2.RefCnt);

        Assert.True(slice2.Release());

        Assert.Equal(0, buffer.RefCnt);
        Assert.Equal(0, slice1.RefCnt);
        Assert.Equal(0, slice2.RefCnt);
    }

    [Fact]
    public override void TestGetBytesByteBuffer()
    {
        byte[] bytes = { (byte)'a', (byte)'b', (byte)'c', (byte)'d', (byte)'e', (byte)'f', (byte)'g' };
        // Ensure destination buffer is bigger then what is wrapped in the ByteBuf.
        // CLR: a byte array converts to the Span destination that replaces the NIO ByteBuffer.
        byte[] nioBuffer = new byte[bytes.Length + 1];
        ByteBuf wrappedBuffer = Unpooled.WrappedBuffer(bytes).Slice(0, bytes.Length - 1);

        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                wrappedBuffer.GetBytes(wrappedBuffer.ReaderIndex, nioBuffer);
            });
        }
        finally
        {
            wrappedBuffer.Release();
        }
    }

    [Fact]
    public override void TestWriteUsAsciiCharSequenceExpand()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            base.TestWriteUsAsciiCharSequenceExpand();
        });
    }

    [Fact]
    public override void TestWriteUtf8CharSequenceExpand()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            base.TestWriteUtf8CharSequenceExpand();
        });
    }

    [Fact]
    public override void TestWriteIso88591CharSequenceExpand()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            base.TestWriteIso88591CharSequenceExpand();
        });
    }

    [Fact]
    public override void TestWriteUtf16CharSequenceExpand()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            base.TestWriteUtf16CharSequenceExpand();
        });
    }

    [Fact]
    public void EnsureWritableWithEnoughSpaceShouldNotThrow()
    {
        ByteBuf slice = NewBuffer(10);
        ByteBuf unwrapped = slice.Unwrap();
        unwrapped.WriterIndex = unwrapped.WriterIndex + 5;
        slice.WriterIndex = slice.ReaderIndex;

        // Run ensureWritable and verify this doesn't change any indexes.
        int originalWriterIndex = slice.WriterIndex;
        int originalReadableBytes = slice.ReadableBytes;
        slice.EnsureWritable(originalWriterIndex - slice.WriterIndex);
        Assert.Equal(originalWriterIndex, slice.WriterIndex);
        Assert.Equal(originalReadableBytes, slice.ReadableBytes);
        slice.Release();
    }

    [Fact]
    public void EnsureWritableWithNotEnoughSpaceShouldThrow()
    {
        ByteBuf slice = NewBuffer(10);
        ByteBuf unwrapped = slice.Unwrap();
        unwrapped.WriterIndex = unwrapped.WriterIndex + 5;

        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                slice.EnsureWritable(1);
            });
        }
        finally
        {
            slice.Release();
        }
    }
}
