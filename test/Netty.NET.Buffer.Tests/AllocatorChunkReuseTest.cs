/*
 * Copyright 2017 The Netty Project
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
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// Port of AbstractByteBufAllocatorTest.shouldReuseChunks. The original unpooled
// fixture disables this requirement; its allocation path is only a negative control here.
[Collection(nameof(AllocatorLeakDetectionCollection))]
public class AllocatorChunkReuseTest(ITestOutputHelper output)
{
    private const int BufferSize = 1024 * 1024;
    private const int Iterations = 100;
    private const long AllocationLimit = 8 * 1024 * 1024;

    private static long MeasureHeapAllocationCycles(IByteBufAllocator allocator)
    {
        // Compare backing reuse without stack-trace recording, matching the Java
        // reference run. The nonparallel collection isolates this global policy;
        // leak tracking itself is covered by AllocatorLeakDetectionTest.
        ResourceLeakDetectorLevel previous = ResourceLeakDetector.GetLevel();
        try
        {
            ResourceLeakDetector.SetLevel(ResourceLeakDetectorLevel.DISABLED);
            // Keep the original single warm-up and 100 allocate/release operations.
            // GC's physical-thread counter replaces the optional JVM ThreadMXBean.
            allocator.HeapBuffer(BufferSize, BufferSize).Release();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < Iterations; ++i)
                allocator.HeapBuffer(BufferSize, BufferSize).Release();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        finally { ResourceLeakDetector.SetLevel(previous); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginalShouldReuseChunksStaysWithinThreadAllocationBudget(bool adaptive)
    {
        var native = new NativeMemoryAllocator();
        IByteBufAllocator allocator = adaptive
            ? new AdaptiveByteBufAllocator(false, lowMemory: false, nativeAllocator: native,
                useCacheForNonEventLoopThreads: false)
            : new PooledByteBufAllocator(false, nativeAllocator: native);
        var metric = ((IByteBufAllocatorMetricProvider)allocator).Metric;
        try
        {
            ResourceLeakDetectorLevel previous = ResourceLeakDetector.GetLevel();
            long allocated = MeasureHeapAllocationCycles(allocator);
            Assert.Equal(previous, ResourceLeakDetector.GetLevel());
            output.WriteLine("{0}: {1} allocated bytes for {2} one-MiB heap cycles", adaptive ? "adaptive" : "pooled", allocated, Iterations);
            Assert.InRange(allocated, 0, AllocationLimit - 1);
            Assert.Equal(0, native.ReservedBytes);
        }
        finally { ((IDisposable)allocator).Dispose(); }
        Assert.Equal(0, metric.UsedHeapMemory);
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void AllocationCounterDetectsRepeatedUnpooledBackingAllocations()
    {
        var allocator = new UnpooledByteBufAllocator(false, disableLeakDetector: true);
        ResourceLeakDetectorLevel previous = ResourceLeakDetector.GetLevel();
        long allocated = MeasureHeapAllocationCycles(allocator);
        Assert.Equal(previous, ResourceLeakDetector.GetLevel());
        output.WriteLine("unpooled negative control: {0} allocated bytes", allocated);
        // A fresh one-MiB array per iteration must exceed the pooling budget.
        // This checks the counter/measurement boundary without requiring unpooled reuse.
        Assert.True(allocated >= (long)BufferSize * Iterations);
        Assert.True(allocated >= AllocationLimit);
        Assert.Equal(0, allocator.Metric.UsedHeapMemory);
    }
}
