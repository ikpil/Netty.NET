/*
 * Copyright 2026 The Netty Project
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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

// @Isolated and @DisabledForSlowLeakDetection: isolate the original allocation race
// from other fixtures and stack-trace leak instrumentation, restoring the global level.
[CollectionDefinition(nameof(AdaptiveByteBufAllocatorGrowthCollection), DisableParallelization = true)]
public sealed class AdaptiveByteBufAllocatorGrowthCollection;

[Collection(nameof(AdaptiveByteBufAllocatorGrowthCollection))]
public class AdaptiveByteBufAllocatorGrowthTest(AdaptiveByteBufAllocatorGrowthTest.State state)
    : IClassFixture<AdaptiveByteBufAllocatorGrowthTest.State>
{
    public sealed class State : IDisposable
    {
        private readonly ResourceLeakDetectorLevel _previous = ResourceLeakDetector.GetLevel();
        private readonly ConcurrentExclusiveSchedulerPair _pool = new(
            TaskScheduler.Default, Math.Max(4, Environment.ProcessorCount * 2));
        internal AdaptiveByteBufAllocator Allocator { get; }
        internal TaskFactory Threads { get; }

        public State()
        {
            ResourceLeakDetector.SetLevel(ResourceLeakDetectorLevel.DISABLED);
            try
            {
                Allocator = new AdaptiveByteBufAllocator(false);
                Threads = new TaskFactory(CancellationToken.None, TaskCreationOptions.DenyChildAttach,
                    TaskContinuationOptions.None, _pool.ConcurrentScheduler);
            }
            catch
            {
                _pool.Complete();
                ResourceLeakDetector.SetLevel(_previous);
                throw;
            }
        }

        public void Dispose()
        {
            try
            {
                _pool.Complete();
                _pool.Completion.GetAwaiter().GetResult();
                Allocator.Dispose();
                Assert.Equal(0, Allocator.UsedHeapMemory);
                Assert.Equal(0, Allocator.UsedDirectMemory);
            }
            finally
            {
                ResourceLeakDetector.SetLevel(_previous);
            }
        }
    }

    public static IEnumerable<object[]> Repetitions()
    {
        for (int repetition = 1; repetition <= 400; ++repetition)
            yield return [repetition];
    }

    [Theory]
    [MemberData(nameof(Repetitions))]
    public async Task ConcurrentBufferAllocateAndGrowth(int repetition)
    {
        // This test targets data races where Chunk.remainingCapacity() is called concurrently
        // with other operations on the chunk. It is important that calling this method does not
        // modify or corrupt the state of the chunks.

        int bufSizeBase;
        int bufSizeAdditional;
        int bufSizeGrowth;
        if ((repetition & 1) == 0)
        {
            // Target large buffers.
            bufSizeBase = 17000;
            bufSizeAdditional = 50000;
            bufSizeGrowth = 80000;
        }
        else
        {
            // Target small buffers.
            bufSizeBase = 64;
            bufSizeAdditional = 512;
            bufSizeGrowth = 1024;
        }

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        int threadCount = 20;
        using var startLatch = new ManualResetEventSlim();
        var futures = new List<Task>(Math.Max(4, Environment.ProcessorCount * 2));
        for (int t = 0; t < threadCount; ++t)
        {
            futures.Add(state.Threads.StartNew(() =>
            {
                startLatch.Wait(cancellationToken);
                // CLR Random.Shared supplies independently generated values on pool threads.
                for (int i = 0; i < 2000; ++i)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Allocate buffers in various sizes.
                    // In the BuddyChunk, we'll exercise different buddy tree levels.
                    int initialSize = bufSizeBase + Random.Shared.Next(bufSizeAdditional);
                    ByteBuf buf = state.Allocator.HeapBuffer(initialSize);
                    try
                    {
                        // Grow the buffer, which will allocate more space, and deallocate the old space.
                        int growth = Random.Shared.Next(bufSizeGrowth);
                        buf.EnsureWritable(initialSize + growth);
                    }
                    finally
                    {
                        buf.Release();
                    }
                }
            }, cancellationToken));
        }

        startLatch.Set();
        try
        {
            foreach (Task future in futures)
            {
                // We're asserting that the tasks did not fail,
                // and thus that the futures do not propagate any exception.
                await future.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }
        finally
        {
            // Keep allocator/latch ownership valid until all CLR tasks have stopped, including on failure.
            await Task.WhenAll(futures);
        }
    }
}
