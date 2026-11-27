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
public class PooledBigEndianDirectByteBufTest : AbstractPooledByteBufTest
{
    // CLR: a scoped allocator over the fixture's NativeMemoryAllocator replaces the JVM-global
    // PooledByteBufAllocator.DEFAULT, so AbstractByteBufTest.Dispose can release it and prove zero reservations.
    public PooledBigEndianDirectByteBufTest()
    {
        Init(NewAllocator());
    }

    // CLR: PooledAlignedBigEndianDirectByteBufTest substitutes its aligned allocator here, before Init allocates.
    protected virtual IByteBufAllocator NewAllocator()
    {
        return new PooledByteBufAllocator(false, maxOrder: 4, smallCacheSize: 0, nativeAllocator: Native);
    }

    protected override ByteBuf Alloc(int length, int maxCapacity)
    {
        ByteBuf buffer = Allocator.DirectBuffer(length, maxCapacity);
        // CLR: assertSame(ByteOrder.BIG_ENDIAN, buffer.order()) has no counterpart: ByteBuf.order() is @Deprecated
        // ("use the Little Endian accessors") and not ported; unsuffixed multibyte accessors are always big-endian.
        return buffer;
    }
}
