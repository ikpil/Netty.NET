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
public class PooledBigEndianDirectByteBufTest : AbstractPooledByteBufTransferTest
{
    public PooledBigEndianDirectByteBufTest() => Init(new PooledByteBufAllocator(false, maxOrder: 4, smallCacheSize: 0, nativeAllocator: Native));

    protected override ByteBuf Alloc(int length, int maxCapacity)
    {
        ByteBuf result = Allocator.DirectBuffer(length, maxCapacity);
        // CLR default multibyte access is always big-endian; no mutable order facade exists.
        return result;
    }

    [Fact]
    public void TestMaxFastWritableBytes() => AssertOriginalPooledMaxFastWritableBytes();

    [Fact]
    public void TestEnsureWritableDoesntGrowTooMuch() => AssertOriginalPooledEnsureWritableDoesntGrowTooMuch();

}
