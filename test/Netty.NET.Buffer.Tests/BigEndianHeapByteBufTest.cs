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
 * Tests big-endian heap channel buffers
 */
public class BigEndianHeapByteBufTest : AbstractContiguousByteBufTest
{
    public BigEndianHeapByteBufTest() => Init(new UnpooledByteBufAllocator(false, Native));

    protected override ByteBuf NewBuffer(int length, int maxCapacity)
    {
        ByteBuf result = Allocator.HeapBuffer(length, maxCapacity);
        Assert.Equal(0, result.WriterIndex);
        return result;
    }

    [Fact]
    public void TestMaxFastWritableBytes() => AssertOriginalMaxFastWritableBytes();

    [Fact]
    public void ShouldNotAllowNullInConstructor1()
    {
        // CLR: ArgumentNullException replaces NullPointerException. The CLR array constructor
        // UnpooledHeapByteBuf(byte[]) has no allocator parameter, so the public allocator constructor carries
        // the null-allocator check; its capacity 1 stands for new byte[1], and the null check precedes the
        // maxCapacity 0 range check as in the original.
        Assert.Throws<ArgumentNullException>(() =>
        {
            new UnpooledHeapByteBuf(null, 1, 0);
        });
    }

    [Fact]
    public void ShouldNotAllowNullInConstructor2()
    {
        // CLR: ArgumentNullException replaces NullPointerException; the internal UnpooledHeapByteBuf(byte[])
        // array constructor replaces the protected (alloc, byte[], maxCapacity) constructor.
        Assert.Throws<ArgumentNullException>(() =>
        {
            new UnpooledHeapByteBuf((byte[])null);
        });
    }

}
