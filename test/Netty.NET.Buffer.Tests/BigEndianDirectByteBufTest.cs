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
namespace Netty.NET.Buffer.Tests;

/**
 * Tests big-endian direct channel buffers
 */
public class BigEndianDirectByteBufTest : AbstractContiguousByteBufTest
{
    public BigEndianDirectByteBufTest() => Init(new UnpooledByteBufAllocator(false, Native));

    protected override ByteBuf NewBuffer(int length, int maxCapacity)
    {
        ByteBuf result = NewDirectBuffer(length, maxCapacity);
        // CLR default multibyte access is always big-endian; no mutable order facade exists.
        Assert.Equal(0, result.WriterIndex);
        return result;
    }
    protected virtual ByteBuf NewDirectBuffer(int length, int maxCapacity)
        => Allocator.DirectBuffer(length, maxCapacity);

    [Fact]
    public void TestMaxFastWritableBytes() => AssertOriginalMaxFastWritableBytes();

}
