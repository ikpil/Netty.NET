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
using System.Collections.Generic;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class PooledAllocatorConstructionTest
{
    public static IEnumerable<object[]> ConstructionOptions()
    {
        foreach (bool preferDirect in new[] { false, true })
        foreach (int alignment in new[] { 0, 64, 8192 })
        for (int heap = 0; heap < 3; ++heap)
        for (int direct = 0; direct < 3; ++direct)
        {
            yield return [preferDirect, alignment, heap, direct, false];
            if (heap < 2 && direct < 2) yield return [preferDirect, alignment, heap, direct, true];
        }
    }

    [Theory]
    [MemberData(nameof(ConstructionOptions))]
    public unsafe void NamedConstructorOptionsReachArenaGeometryFactorySelectionAndPhysicalAlignment(
        bool preferDirect, int alignment, int heap, int direct, bool convenience)
    {
        var native = new NativeMemoryAllocator();
        using var allocator = convenience
            ? new PooledByteBufAllocator(preferDirect, heap != 0, direct != 0,
                pageSize: 4096, maxOrder: 4, directMemoryCacheAlignment: alignment, nativeAllocator: native)
            : new PooledByteBufAllocator(preferDirect, heap, direct,
                pageSize: 4096, maxOrder: 4, directMemoryCacheAlignment: alignment, nativeAllocator: native);
        Assert.Equal(heap, allocator.Metric.NumHeapArenas);
        Assert.Equal(direct, allocator.Metric.NumDirectArenas);
        Assert.Equal(Math.Max(4096, alignment) * 16, allocator.Metric.ChunkSize);
        Assert.Equal(0, allocator.Metric.SmallCacheSize);
        Assert.Equal(0, allocator.Metric.NormalCacheSize);
        Assert.Equal(0, allocator.Metric.NumThreadLocalCaches);
        Assert.Equal(0, native.ReservedBytes);
        Assert.Equal(direct != 0, allocator.IsDirectBufferPooled);
        ByteBuf selected = allocator.Buffer(17, 64), h = allocator.HeapBuffer(17, 64), d = allocator.DirectBuffer(17, 64);
        try
        {
            Assert.Equal(preferDirect, selected.IsDirect);
            Assert.False(h.IsDirect); Assert.True(d.IsDirect);
            foreach (ByteBuf buffer in new[] { selected, h, d })
            {
                Assert.Same(allocator, buffer.Allocator);
                Assert.Equal(17, buffer.Capacity);
                Assert.Equal(64, buffer.MaxCapacity);
                buffer.WriteLongLE(0x0102030405060708L);
                Assert.Equal(new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 }, buffer.AsSpan(0, 8).ToArray());
            }
            if (direct != 0 && alignment != 0)
            {
                using var pin = d.AsMemory(0, 17).Pin();
                Assert.Equal(0UL, (ulong)pin.Pointer % (uint)alignment);
            }
            Assert.Equal(heap + direct == 0 ? 0 : 1, allocator.Metric.NumThreadLocalCaches);
        }
        finally { selected.Release(); h.Release(); d.Release(); }
        allocator.Dispose();
        Assert.Equal(0, native.ReservedBytes);
    }
}
