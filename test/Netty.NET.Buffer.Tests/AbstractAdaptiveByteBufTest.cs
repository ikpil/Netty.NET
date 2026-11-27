/*
 * Copyright 2024 The Netty Project
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

public abstract class AbstractAdaptiveByteBufTest : AbstractPooledByteBufTest
{
    // CLR: the source's per-instance field `new AdaptiveByteBufAllocator()` becomes the fixture allocator passed to
    // AbstractByteBufTest.Init, over the fixture's own NativeMemoryAllocator so Dispose can prove every native
    // reservation returned. lowMemory: false is the source IS_LOW_MEM value on hosts above its 512 MiB budget
    // (src/Netty.NET.Buffer/AdaptivePoolingAllocator.cs IS_LOW_MEM), pinned so small hosts run the same chunks.
    protected AbstractAdaptiveByteBufTest()
    {
        Init(new AdaptiveByteBufAllocator(false, lowMemory: false, nativeAllocator: Native));
    }

    protected sealed override ByteBuf Alloc(int length, int maxCapacity)
    {
        return Alloc((AdaptiveByteBufAllocator)Allocator, length, maxCapacity);
    }

    protected abstract ByteBuf Alloc(AdaptiveByteBufAllocator allocator, int length, int maxCapacity);

    [Fact(Skip = "Assumes the ByteBuf can be cast to PooledByteBuf")]
    public override void TestMaxFastWritableBytes()
    {
    }

    [Fact(Skip = "Assumes the ByteBuf can be cast to PooledByteBuf")]
    public override void TestEnsureWritableDoesntGrowTooMuch()
    {
    }
}
