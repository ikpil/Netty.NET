/*
 * Copyright 2021 The Netty Project
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

public class PooledAlignedBigEndianDirectByteBufTest : PooledBigEndianDirectByteBufTest
{
    private const int directMemoryCacheAlignment = 1;

    // CLR: the source creates one allocator in @BeforeAll setUpAllocator and drops it in @AfterAll releaseAllocator.
    // AbstractByteBufTest disposes its allocator after every test and then proves the fixture's native domain is
    // empty, so each test instance gets the same allocator configuration instead of sharing one across the class.
    protected override IByteBufAllocator NewAllocator()
    {
        // CLR: assumeTrue(isDirectMemoryCacheAlignmentSupported()); the CLR aligns inside its own native reservation
        // and always supports alignment (src/Netty.NET.Buffer/PooledByteBufAllocator.cs), so the assumption holds.
        Assert.True(PooledByteBufAllocator.IsDirectMemoryCacheAlignmentSupported);
        // CLR: the source's nine-argument constructor followed by the CLR native-memory argument
        // (src/Netty.NET.Buffer/PooledByteBufAllocator.cs keeps the source positional prefix).
        return new PooledByteBufAllocator(
                true,
                PooledByteBufAllocator.DefaultNumHeapArena(),
                PooledByteBufAllocator.DefaultNumDirectArena(),
                PooledByteBufAllocator.DefaultPageSize(),
                11,
                PooledByteBufAllocator.DefaultSmallCacheSize(),
                64,
                PooledByteBufAllocator.DefaultUseCacheForAllThreads(),
                directMemoryCacheAlignment,
                Native);
    }

    protected override ByteBuf Alloc(int length, int maxCapacity)
    {
        ByteBuf buffer = Allocator.DirectBuffer(length, maxCapacity);
        // CLR: assertSame(ByteOrder.BIG_ENDIAN, buffer.order()) has no counterpart: ByteBuf.order() is @Deprecated
        // ("use the Little Endian accessors") and not ported; unsuffixed multibyte accessors are always big-endian.
        return buffer;
    }
}
