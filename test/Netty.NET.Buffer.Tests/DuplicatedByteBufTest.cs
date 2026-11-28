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

/**
 * Tests duplicated channel buffers
 */
public class DuplicatedByteBufTest : AbstractContiguousByteBufTest
{
    public DuplicatedByteBufTest()
    {
        Init(new UnpooledByteBufAllocator(false, Native));
    }

    protected override ByteBuf NewBuffer(int length, int maxCapacity)
    {
        // CLR: the fixture's unpooled heap allocator stands in for Unpooled.buffer, as in BigEndianHeapByteBufTest,
        // and the internal ByteBufView duplicate constructor replaces the deprecated DuplicatedByteBuf constructor.
        ByteBuf wrapped = Allocator.HeapBuffer(length, maxCapacity);
        ByteBuf buffer = new ByteBufView(wrapped);
        Assert.Equal(wrapped.WriterIndex, buffer.WriterIndex);
        Assert.Equal(wrapped.ReaderIndex, buffer.ReaderIndex);
        return buffer;
    }

    // CLR: the inherited AbstractByteBufTest.testMaxFastWritableBytes is declared per finite-capacity
    // fixture in this port (see BigEndianHeapByteBufTest); the original runs it on the duplicate too.
    [Fact]
    public void TestMaxFastWritableBytes()
    {
        AssertOriginalMaxFastWritableBytes();
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
        // CLR: ArgumentNullException replaces NullPointerException.
        Assert.Throws<ArgumentNullException>(() =>
        {
            new ByteBufView(null);
        });
    }

    // See https://github.com/netty/netty/issues/1800
    [Fact]
    public void TestIncreaseCapacityWrapped()
    {
        ByteBuf buffer = NewBuffer(8);
        ByteBuf wrapped = buffer.Unwrap();
        wrapped.WriteByte(0);
        wrapped.ReaderIndex = wrapped.ReaderIndex + 1;
        buffer.WriterIndex = buffer.WriterIndex + 1;
        wrapped.Capacity = wrapped.Capacity * 2;

        Assert.Equal((byte)0, buffer.ReadByte());
    }

    [Fact]
    public void TestMarksInitialized()
    {
        ByteBuf wrapped = Unpooled.Buffer(8);

        try
        {
            wrapped.WriterIndex = 6;
            wrapped.ReaderIndex = 1;
            ByteBuf duplicate = new ByteBufView(wrapped);

            // Test writer mark
            duplicate.WriterIndex = duplicate.WriterIndex + 1;
            duplicate.ResetWriterIndex();
            Assert.Equal(wrapped.WriterIndex, duplicate.WriterIndex);

            // Test reader mark
            duplicate.ReaderIndex = duplicate.ReaderIndex + 1;
            duplicate.ResetReaderIndex();
            Assert.Equal(wrapped.ReaderIndex, duplicate.ReaderIndex);
        }
        finally
        {
            wrapped.Release();
        }
    }
}
