/*
 * Copyright 2016 The Netty Project
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
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Netty.NET.Buffer.Tests;

// CLR: the original extends BigEndianUnsafeDirectByteBufTest, whose only addition is the
// PlatformDependent.hasUnsafe() selection of sun.misc.Unsafe direct buffers (JVM-only; the CLR has
// one native direct provider, exercised by BigEndianDirectByteBufTest). The original
// PlatformDependent.useDirectBufferNoCleaner() precondition always holds for CLR raw native memory.
// Its newBuffer assumes maxCapacity == Integer.MAX_VALUE, which skips the five finite-capacity bodies
// and the inherited testMaxFastWritableBytes, so this fixture inherits AbstractSingleRegionByteBufTest;
// testIsContiguous, which the original inherits from BigEndianDirectByteBufTest, is declared here.
// ArgumentOutOfRangeException replaces IndexOutOfBoundsException and IllegalArgumentException
// (ByteBuf.CheckNewCapacity) in the overrides that expect the inherited body to fail.
public class WrappedUnpooledUnsafeByteBufTest : AbstractSingleRegionByteBufTest
{
    public WrappedUnpooledUnsafeByteBufTest() => Init(new UnpooledByteBufAllocator(false, Native));

    protected override ByteBuf NewBuffer(int length, int maxCapacity)
    {
        // CLR: inherited bodies only reach this factory with the maximum capacity, so the original
        // Assumptions.assumeTrue(maxCapacity == Integer.MAX_VALUE) is an assertion here.
        Assert.Equal(int.MaxValue, maxCapacity);

        // CLR: a bounded direct buffer supplies the fully readable range.
        return Unpooled.DirectBuffer(length, length).SetIndex(0, length);
    }

    [Fact]
    public void TestIsContiguous()
    {
        ByteBuf buf = NewBuffer(4);
        Assert.True(buf.IsContiguous);
        buf.Release();
    }

    [Fact]
    public override async Task TestInternalNioBuffer()
    {
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
    public override void TestReadBytes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            base.TestReadBytes();
        });
    }

    [Fact]
    public override void TestDuplicateCapacityChange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            base.TestDuplicateCapacityChange();
        });
    }

    [Fact]
    public override void TestRetainedDuplicateCapacityChange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            base.TestRetainedDuplicateCapacityChange();
        });
    }

    [Fact]
    public override void TestLittleEndianWithExpand()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            base.TestLittleEndianWithExpand();
        });
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
    public override void TestGetBytesByteBuffer()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            base.TestGetBytesByteBuffer();
        });
    }

    [Fact]
    public override void TestForEachByteDesc2()
    {
        // Ignore
    }

    [Fact]
    public override void TestForEachByte2()
    {
        // Ignore
    }
}
