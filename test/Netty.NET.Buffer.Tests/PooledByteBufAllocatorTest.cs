/*
 * Copyright 2015 The Netty Project
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using static Netty.NET.Buffer.Tests.AllocatorLeakDetectionTest;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer.Tests;

// CLR: the original fixture calls the deprecated tiny-cache constructors; Java only warns about them.
#pragma warning disable CS0618
public class PooledByteBufAllocatorTest : AbstractByteBufAllocatorTest<PooledByteBufAllocator>
{
    protected override PooledByteBufAllocator NewAllocator(bool preferDirect)
    {
        return Track(new PooledByteBufAllocator(preferDirect));
    }

    protected override PooledByteBufAllocator NewUnpooledAllocator()
    {
        return Track(new PooledByteBufAllocator(0, 0, 8192, 1));
    }

    protected override long ExpectedUsedMemory(PooledByteBufAllocator allocator, int capacity)
    {
        return allocator.Metric.ChunkSize;
    }

    protected override long ExpectedUsedMemoryAfterRelease(PooledByteBufAllocator allocator, int capacity)
    {
        // This is the case as allocations will start in qInit and chunks in qInit will never be released until
        // these are moved to q000.
        // See https://www.bsdcan.org/2006/papers/jemalloc.pdf
        return allocator.Metric.ChunkSize;
    }

    protected override void TrimCaches(PooledByteBufAllocator allocator)
    {
        allocator.TrimCurrentThreadCache();
    }

    [Fact]
    public void TestRejectNegativeMaxOrder()
    {
        // CLR: ArgumentOutOfRangeException replaces IllegalArgumentException.
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => new PooledByteBufAllocator(true, 1, 1, 8192, -1, 0, 0, false));

        Assert.Equal("maxOrder: -1 (expected: 0-14)", exception.Message);
    }

    [Fact]
    public void TestTrim()
    {
        PooledByteBufAllocator allocator = NewAllocator(true);

        // Should return false as we never allocated from this thread yet.
        Assert.False(allocator.TrimCurrentThreadCache());

        ByteBuf directBuffer = allocator.DirectBuffer();

        Assert.True(directBuffer.Release());

        // Should return true now a cache exists for the calling thread.
        Assert.True(allocator.TrimCurrentThreadCache());
    }

    [Fact]
    public void TestPooledUnsafeHeapBufferAndUnsafeDirectBuffer()
    {
        PooledByteBufAllocator allocator = NewAllocator(true);
        ByteBuf directBuffer = allocator.DirectBuffer();
        // CLR: without sun.misc.Unsafe there is no PooledUnsafe* subclass; the hasUnsafe() == false branch is the only
        // CLR storage class (src/Netty.NET.Buffer/PooledDirectByteBuf.cs, src/Netty.NET.Buffer/PooledHeapByteBuf.cs).
        AssertInstanceOf(directBuffer, typeof(PooledDirectByteBuf));
        directBuffer.Release();

        ByteBuf heapBuffer = allocator.HeapBuffer();
        AssertInstanceOf(heapBuffer, typeof(PooledHeapByteBuf));
        heapBuffer.Release();
    }

    [Fact]
    public void TestIOBuffersAreDirectWhenCleanerAvailableOrDirectBuffersPooled()
    {
        PooledByteBufAllocator allocator = NewAllocator(true);
        ByteBuf ioBuffer = allocator.IoBuffer();

        Assert.True(ioBuffer.IsDirect);
        ioBuffer.Release();

        PooledByteBufAllocator unpooledAllocator = NewUnpooledAllocator();
        ioBuffer = unpooledAllocator.IoBuffer();

        // CLR: PlatformDependent.canReliabilyFreeDirectBuffers() is always satisfied: native storage is owned and freed
        // deterministically, and IoBuffer always chooses it (src/Netty.NET.Buffer/AbstractByteBufAllocator.cs).
        Assert.True(ioBuffer.IsDirect);
        ioBuffer.Release();
    }

    [Fact]
    public void TestWithoutUseCacheForAllThreads()
    {
        // CLR: a FastThreadLocalThread is detected through the thread's FastThreadLocal scope, as Thread is sealed
        // (src/Netty.NET.Common/Concurrent/FastThreadLocalThread.cs).
        Assert.False(FastThreadLocalThread.CurrentThreadHasFastThreadLocal());

        using PooledByteBufAllocator pool = new PooledByteBufAllocator(
                /*preferDirect=*/ false,
                /*nHeapArena=*/ 1,
                /*nDirectArena=*/ 1,
                /*pageSize=*/8192,
                /*maxOrder=*/ 9,
                /*tinyCacheSize=*/ 0,
                /*smallCacheSize=*/ 0,
                /*normalCacheSize=*/ 0,
                /*useCacheForAllThreads=*/ false);
        ByteBuf buf = pool.Buffer(1);
        buf.Release();
    }

    [Fact]
    public void TestArenaMetricsNoCache()
    {
        using var allocator = new PooledByteBufAllocator(true, 2, 2, 8192, 9, 0, 0, 0);
        TestArenaMetrics0(allocator, 100, 0, 100, 100);
    }

    [Fact]
    public void TestArenaMetricsCache()
    {
        using var allocator = new PooledByteBufAllocator(true, 2, 2, 8192, 9, 1000, 1000, 1000, true, 0);
        TestArenaMetrics0(allocator, 100, 1, 1, 0);
    }

    [Fact]
    public void TestArenaMetricsNoCacheAlign()
    {
        Assert.SkipUnless(PooledByteBufAllocator.IsDirectMemoryCacheAlignmentSupported,
                "PooledByteBufAllocator.isDirectMemoryCacheAlignmentSupported()");
        using var allocator = new PooledByteBufAllocator(true, 2, 2, 8192, 9, 0, 0, 0, true, 64);
        TestArenaMetrics0(allocator, 100, 0, 100, 100);
    }

    [Fact]
    public void TestArenaMetricsCacheAlign()
    {
        Assert.SkipUnless(PooledByteBufAllocator.IsDirectMemoryCacheAlignmentSupported,
                "PooledByteBufAllocator.isDirectMemoryCacheAlignmentSupported()");
        using var allocator = new PooledByteBufAllocator(true, 2, 2, 8192, 9, 1000, 1000, 1000, true, 64);
        TestArenaMetrics0(allocator, 100, 1, 1, 0);
    }

    private static void TestArenaMetrics0(
            PooledByteBufAllocator allocator, int num, int expectedActive, int expectedAlloc, int expectedDealloc)
    {
        for (int i = 0; i < num; i++)
        {
            Assert.True(allocator.DirectBuffer().Release());
            Assert.True(allocator.HeapBuffer().Release());
        }

        AssertArenaMetrics(allocator.Metric.DirectArenas, expectedActive, expectedAlloc, expectedDealloc);
        AssertArenaMetrics(allocator.Metric.HeapArenas, expectedActive, expectedAlloc, expectedDealloc);
    }

    private static void AssertArenaMetrics(
            IReadOnlyList<IPoolArenaMetric> arenaMetrics, int expectedActive, int expectedAlloc, int expectedDealloc)
    {
        long active = 0;
        long alloc = 0;
        long dealloc = 0;
        foreach (IPoolArenaMetric arena in arenaMetrics)
        {
            active += arena.NumActiveAllocations;
            alloc += arena.NumAllocations;
            dealloc += arena.NumDeallocations;
        }
        Assert.Equal(expectedActive, active);
        Assert.Equal(expectedAlloc, alloc);
        Assert.Equal(expectedDealloc, dealloc);
    }

    [Fact]
    public void TestPoolChunkListMetric()
    {
        foreach (IPoolArenaMetric arenaMetric in PooledByteBufAllocator.Default.Metric.HeapArenas)
        {
            AssertPoolChunkListMetric(arenaMetric);
        }
    }

    private static void AssertPoolChunkListMetric(IPoolArenaMetric arenaMetric)
    {
        IReadOnlyList<IPoolChunkListMetric> lists = arenaMetric.ChunkLists;
        Assert.Equal(6, lists.Count);
        AssertPoolChunkListMetric(lists[0], 1, 25);
        AssertPoolChunkListMetric(lists[1], 1, 50);
        AssertPoolChunkListMetric(lists[2], 25, 75);
        AssertPoolChunkListMetric(lists[4], 75, 100);
        AssertPoolChunkListMetric(lists[5], 100, 100);
    }

    private static void AssertPoolChunkListMetric(IPoolChunkListMetric m, int min, int max)
    {
        Assert.Equal(min, m.MinUsage);
        Assert.Equal(max, m.MaxUsage);
    }

    [Fact]
    public void TestSmallSubpageMetric()
    {
        using PooledByteBufAllocator allocator = new PooledByteBufAllocator(true, 1, 1, 8192, 9, 0, 0, 0);
        ByteBuf buffer = allocator.HeapBuffer(500);
        try
        {
            IPoolArenaMetric metric = allocator.Metric.HeapArenas[0];
            IPoolSubpageMetric subpageMetric = metric.SmallSubpages[0];
            Assert.Equal(1, subpageMetric.MaxNumElements - subpageMetric.NumAvailable);
        }
        finally
        {
            buffer.Release();
        }
    }

    [Fact]
    public void TestAllocNotNull()
    {
        using PooledByteBufAllocator allocator = new PooledByteBufAllocator(true, 1, 1, 8192, 9, 0, 0, 0);
        // Huge allocation
        TestAllocNotNull0(allocator, allocator.Metric.ChunkSize + 1);
        // Normal allocation
        TestAllocNotNull0(allocator, 1024);
        // Small allocation
        TestAllocNotNull0(allocator, 512);
        TestAllocNotNull0(allocator, 1);
    }

    // CLR: xUnit1024 forbids overloading the TestAllocNotNull test name.
    private static void TestAllocNotNull0(PooledByteBufAllocator allocator, int capacity)
    {
        ByteBuf buffer = allocator.HeapBuffer(capacity);
        Assert.NotNull(buffer.Alloc());
        Assert.True(buffer.Release());
        Assert.NotNull(buffer.Alloc());
    }

    [Fact]
    public void TestFreePoolChunk()
    {
        int chunkSize = 16 * 1024 * 1024;
        using PooledByteBufAllocator allocator = new PooledByteBufAllocator(true, 1, 0, 8192, 11, 0, 0, 0);
        ByteBuf buffer = allocator.HeapBuffer(chunkSize);
        IReadOnlyList<IPoolArenaMetric> arenas = allocator.Metric.HeapArenas;
        // CLR: xUnit2013 requires Assert.Single for the original one-element size check.
        Assert.Single(arenas);
        IReadOnlyList<IPoolChunkListMetric> lists = arenas[0].ChunkLists;
        Assert.Equal(6, lists.Count);

        Assert.False(lists[0].GetEnumerator().MoveNext());
        Assert.False(lists[1].GetEnumerator().MoveNext());
        Assert.False(lists[2].GetEnumerator().MoveNext());
        Assert.False(lists[3].GetEnumerator().MoveNext());
        Assert.False(lists[4].GetEnumerator().MoveNext());

        // Must end up in the 6th PoolChunkList
        Assert.True(lists[5].GetEnumerator().MoveNext());
        Assert.True(buffer.Release());

        // Should be completely removed and so all PoolChunkLists must be empty
        Assert.False(lists[0].GetEnumerator().MoveNext());
        Assert.False(lists[1].GetEnumerator().MoveNext());
        Assert.False(lists[2].GetEnumerator().MoveNext());
        Assert.False(lists[3].GetEnumerator().MoveNext());
        Assert.False(lists[4].GetEnumerator().MoveNext());
        Assert.False(lists[5].GetEnumerator().MoveNext());
    }

    [Fact]
    public void TestCollapse()
    {
        int pageSize = 8192;
        //no cache
        using PooledByteBufAllocator pool = new PooledByteBufAllocator(true, 1, 1, 8192, 9, 0, 0, 0);
        IByteBufAllocator allocator = pool;

        ByteBuf b1 = allocator.Buffer(pageSize * 4);
        ByteBuf b2 = allocator.Buffer(pageSize * 5);
        ByteBuf b3 = allocator.Buffer(pageSize * 6);

        b2.Release();
        b3.Release();

        ByteBuf b4 = allocator.Buffer(pageSize * 10);

        PooledByteBuf b = UnwrapIfNeeded(b4);

        //b2 and b3 are collapsed, b4 should start at offset 4
        Assert.Equal(4, PoolChunk.RunOffset(b.Allocation.Handle));
        Assert.Equal(10, PoolChunk.RunPages(b.Allocation.Handle));

        b1.Release();
        b4.Release();

        //all ByteBuf are collapsed, b5 should start at offset 0
        ByteBuf b5 = allocator.Buffer(pageSize * 20);
        b = UnwrapIfNeeded(b5);

        Assert.Equal(0, PoolChunk.RunOffset(b.Allocation.Handle));
        Assert.Equal(20, PoolChunk.RunPages(b.Allocation.Handle));

        b5.Release();
    }

    [Fact]
    public void TestAllocateSmallOffset()
    {
        int pageSize = 8192;
        using PooledByteBufAllocator pool = new PooledByteBufAllocator(true, 1, 1, 8192, 9, 0, 0, 0);
        IByteBufAllocator allocator = pool;

        int size = pageSize * 5;

        ByteBuf[] bufs = new ByteBuf[10];
        for (int i = 0; i < 10; i++)
        {
            bufs[i] = allocator.Buffer(size);
        }

        for (int i = 0; i < 5; i++)
        {
            bufs[i].Release();
        }

        //make sure we always allocate runs with small offset
        for (int i = 0; i < 5; i++)
        {
            ByteBuf buf = allocator.Buffer(size);
            PooledByteBuf unwrapedBuf = UnwrapIfNeeded(buf);
            Assert.Equal(PoolChunk.RunOffset(unwrapedBuf.Allocation.Handle), i * 5);
            bufs[i] = buf;
        }

        //release at reverse order
        for (int i = 10 - 1; i >= 5; i--)
        {
            bufs[i].Release();
        }

        for (int i = 5; i < 10; i++)
        {
            ByteBuf buf = allocator.Buffer(size);
            PooledByteBuf unwrapedBuf = UnwrapIfNeeded(buf);
            Assert.Equal(PoolChunk.RunOffset(unwrapedBuf.Allocation.Handle), i * 5);
            bufs[i] = buf;
        }

        for (int i = 0; i < 10; i++)
        {
            bufs[i].Release();
        }
    }

    // CLR: @Timeout(value = 20, threadMode = SEPARATE_THREAD) becomes a 20-second WaitAsync on a separate task.
    [Fact]
    public async Task TestThreadCacheDestroyedByThreadCleaner()
    {
        await Task.Run(() => TestThreadCacheDestroyed(false), TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
    }

    // CLR: @Timeout(value = 20, threadMode = SEPARATE_THREAD) becomes a 20-second WaitAsync on a separate task.
    [Fact]
    public async Task TestThreadCacheDestroyedAfterExitRun()
    {
        await Task.Run(() => TestThreadCacheDestroyed(true), TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
    }

    private static void TestThreadCacheDestroyed(bool useRunnable)
    {
        int numArenas = 11;
        using PooledByteBufAllocator allocator =
            new PooledByteBufAllocator(numArenas, numArenas, 8192, 1);

        bool threadCachesCreated = true;

        Action task = () =>
        {
            // CLR: the public HeapBuffer factory replaces the protected newHeapBuffer access.
            ByteBuf buf = allocator.HeapBuffer(1024, 1024);
            for (int i = 0; i < buf.Capacity; i++)
            {
                buf.WriteByte(0);
            }

            // Make sure that thread caches are actually created,
            // so that down below we are not testing for zero
            // thread caches without any of them ever having been initialized.
            if (allocator.Metric.NumThreadLocalCaches == 0)
            {
                Volatile.Write(ref threadCachesCreated, false);
            }

            buf.Release();
        };

        for (int i = 0; i < numArenas; i++)
        {
            FastThreadLocalThread thread;
            if (useRunnable)
            {
                thread = new FastThreadLocalThread(task);
                Assert.True(FastThreadLocalThread.WillCleanupFastThreadLocals(thread.Thread));
            }
            else
            {
                thread = new RunOverridingFastThreadLocalThread(task);
                Assert.False(FastThreadLocalThread.WillCleanupFastThreadLocals(thread.Thread));
            }
            thread.Thread.Start();
            thread.Thread.Join();
        }

        // Wait for the ThreadDeathWatcher to have destroyed all thread caches
        while (allocator.Metric.NumThreadLocalCaches > 0)
        {
            // Signal we want to have a GC run to ensure we can process our ThreadCleanerReference
            GC.Collect();
            Thread.Sleep(TimeSpan.FromMilliseconds(100));
        }

        Assert.True(Volatile.Read(ref threadCachesCreated));
    }

    // CLR: the anonymous FastThreadLocalThread subclass that overrides run(); a CLR Thread cannot be subclassed
    // (src/Netty.NET.Common/Concurrent/FastThreadLocalThread.cs).
    private sealed class RunOverridingFastThreadLocalThread(Action task) : FastThreadLocalThread
    {
        public override void Run()
        {
            task();
        }
    }

    // CLR: @Timeout(30) becomes a 30-second WaitAsync on a separate task.
    [Fact]
    public async Task TestNumThreadCachesWithNoDirectArenas()
    {
        await Task.Run(() =>
        {
            int numHeapArenas = 1;
            using PooledByteBufAllocator allocator =
                new PooledByteBufAllocator(numHeapArenas, 0, 8192, 1);

            ThreadCache tcache0 = CreateNewThreadCache(allocator, false);
            Assert.Equal(1, allocator.Metric.NumThreadLocalCaches);

            ThreadCache tcache1 = CreateNewThreadCache(allocator, false);
            Assert.Equal(2, allocator.Metric.NumThreadLocalCaches);

            tcache0.Destroy();
            Assert.Equal(1, allocator.Metric.NumThreadLocalCaches);

            tcache1.Destroy();
            Assert.Equal(0, allocator.Metric.NumThreadLocalCaches);
        }, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }

    // CLR: @Timeout(30) becomes a 30-second WaitAsync on a separate task.
    [Fact]
    public async Task TestNumThreadCachesAccountForDirectAndHeapArenas()
    {
        await Task.Run(() =>
        {
            int numArenas = 1;
            using PooledByteBufAllocator allocator =
                    new PooledByteBufAllocator(numArenas, numArenas, 8192, 1);

            ThreadCache tcache0 = CreateNewThreadCache(allocator, false);
            Assert.Equal(1, allocator.Metric.NumThreadLocalCaches);

            ThreadCache tcache1 = CreateNewThreadCache(allocator, true);
            Assert.Equal(2, allocator.Metric.NumThreadLocalCaches);

            tcache0.Destroy();
            Assert.Equal(1, allocator.Metric.NumThreadLocalCaches);

            tcache1.Destroy();
            Assert.Equal(0, allocator.Metric.NumThreadLocalCaches);
        }, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }

    // CLR: @Timeout(30) becomes a 30-second WaitAsync on a separate task.
    [Fact]
    public async Task TestThreadCacheToArenaMappings()
    {
        await Task.Run(() =>
        {
            int numArenas = 2;
            using PooledByteBufAllocator allocator =
                new PooledByteBufAllocator(numArenas, numArenas, 8192, 1);

            ThreadCache tcache0 = CreateNewThreadCache(allocator, false);
            ThreadCache tcache1 = CreateNewThreadCache(allocator, false);
            Assert.Equal(2, allocator.Metric.NumThreadLocalCaches);
            Assert.Equal(1, allocator.Metric.HeapArenas[0].NumThreadCaches);
            Assert.Equal(1, allocator.Metric.HeapArenas[1].NumThreadCaches);
            Assert.Equal(1, allocator.Metric.DirectArenas[0].NumThreadCaches);
            Assert.Equal(1, allocator.Metric.DirectArenas[0].NumThreadCaches);

            tcache1.Destroy();

            Assert.Equal(1, allocator.Metric.NumThreadLocalCaches);
            Assert.Equal(1, allocator.Metric.HeapArenas[0].NumThreadCaches);
            Assert.Equal(0, allocator.Metric.HeapArenas[1].NumThreadCaches);
            Assert.Equal(1, allocator.Metric.DirectArenas[0].NumThreadCaches);
            Assert.Equal(0, allocator.Metric.DirectArenas[1].NumThreadCaches);

            ThreadCache tcache2 = CreateNewThreadCache(allocator, false);
            Assert.Equal(2, allocator.Metric.NumThreadLocalCaches);
            Assert.Equal(1, allocator.Metric.HeapArenas[0].NumThreadCaches);
            Assert.Equal(1, allocator.Metric.HeapArenas[1].NumThreadCaches);
            Assert.Equal(1, allocator.Metric.DirectArenas[0].NumThreadCaches);
            Assert.Equal(1, allocator.Metric.DirectArenas[1].NumThreadCaches);

            tcache0.Destroy();
            Assert.Equal(1, allocator.Metric.NumThreadLocalCaches);

            tcache2.Destroy();
            Assert.Equal(0, allocator.Metric.NumThreadLocalCaches);
            Assert.Equal(0, allocator.Metric.HeapArenas[0].NumThreadCaches);
            Assert.Equal(0, allocator.Metric.HeapArenas[1].NumThreadCaches);
            Assert.Equal(0, allocator.Metric.DirectArenas[0].NumThreadCaches);
            Assert.Equal(0, allocator.Metric.DirectArenas[1].NumThreadCaches);
        }, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }

    private static ThreadCache CreateNewThreadCache(PooledByteBufAllocator allocator, bool direct)
    {
        CountdownEvent latch = new CountdownEvent(1);
        CountdownEvent cacheLatch = new CountdownEvent(1);
        // CLR: a TaskCompletionSource stands in for the FutureTask that reports the runnable's outcome.
        TaskCompletionSource task = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        FastThreadLocalThread t = new FastThreadLocalThread(() =>
        {
            try
            {
                ByteBuf buf;

                // CLR: the public factories replace the protected newDirectBuffer/newHeapBuffer access.
                if (direct)
                {
                    buf = allocator.DirectBuffer(1024, 1024);
                }
                else
                {
                    buf = allocator.HeapBuffer(1024, 1024);
                }

                // Countdown the latch after we allocated a buffer. At this point the cache must exists.
                cacheLatch.Signal();

                buf.WriteZero(buf.Capacity);

                latch.Wait();

                buf.Release();

                FastThreadLocal.RemoveAll();
                task.SetResult();
            }
            catch (Exception e)
            {
                task.SetException(e);
                if (cacheLatch.CurrentCount > 0)
                {
                    cacheLatch.Signal();
                }
            }
        });
        t.Thread.Start();

        // Wait until we allocated a buffer and so be sure the thread was started and the cache exists.
        cacheLatch.Wait();

        return new DelegateThreadCache(() =>
        {
            latch.Signal();
            task.Task.GetAwaiter().GetResult();
            t.Thread.Join();
        });
    }

    private interface ThreadCache
    {
        void Destroy();
    }

    // CLR: the anonymous ThreadCache implementation.
    private sealed class DelegateThreadCache(Action destroy) : ThreadCache
    {
        public void Destroy()
        {
            destroy();
        }
    }

    [Fact]
    public void TestConcurrentUsage()
    {
        long runningTime = TimeSpan.FromMilliseconds(SystemPropertyUtil.GetLong(
                "io.netty.buffer.PooledByteBufAllocatorTest.testConcurrentUsageTime", 15000)).Ticks;

        // We use no caches and only one arena to maximize the chance of hitting the race-condition we
        // had before.
        using PooledByteBufAllocator pool = new PooledByteBufAllocator(true, 0, 1, 8192, 9, 0, 0, 0);
        IByteBufAllocator allocator = pool;
        List<AllocationThread> threads = new List<AllocationThread>();
        try
        {
            for (int i = 0; i < 64; i++)
            {
                AllocationThread thread = new AllocationThread(allocator);
                thread.Start();
                threads.Add(thread);
            }

            long start = Stopwatch.GetTimestamp();
            while (!IsExpired(start, runningTime))
            {
                CheckForErrors(threads);
                Thread.Sleep(100);
            }
        }
        finally
        {
            // First mark all AllocationThreads to complete their work and then wait until these are complete
            // and rethrow if there was any error.
            foreach (AllocationThread t in threads)
            {
                t.MarkAsFinished();
            }

            foreach (AllocationThread t in threads)
            {
                t.JoinAndCheckForError();
            }
        }
    }

    // CLR: Stopwatch timestamps replace System.nanoTime(); the expiry is kept in TimeSpan ticks.
    private static bool IsExpired(long start, long expireTime)
    {
        return Stopwatch.GetElapsedTime(start).Ticks > expireTime;
    }

    private static void CheckForErrors(List<AllocationThread> threads)
    {
        foreach (AllocationThread t in threads)
        {
            if (t.IsFinished())
            {
                t.CheckForError();
            }
        }
    }

    // CLR: Thread is sealed, so the allocation thread owns a Thread instead of extending it.
    private sealed class AllocationThread
    {

        private static readonly int[] ALLOCATION_SIZES = new int[16 * 1024];
        static AllocationThread()
        {
            for (int i = 0; i < ALLOCATION_SIZES.Length; i++)
            {
                ALLOCATION_SIZES[i] = i;
            }
        }

        private readonly ConcurrentQueue<ByteBuf> buffers = new ConcurrentQueue<ByteBuf>();
        private readonly IByteBufAllocator allocator;
        private object finish;
        private readonly Thread thread;

        internal AllocationThread(IByteBufAllocator allocator)
        {
            this.allocator = allocator;
            thread = new Thread(Run);
        }

        internal void Start()
        {
            thread.Start();
        }

        private void Run()
        {
            try
            {
                int idx = 0;
                while (Volatile.Read(ref finish) == null)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        int len = ALLOCATION_SIZES[Math.Abs(idx++ % ALLOCATION_SIZES.Length)];
                        ByteBuf buf = allocator.DirectBuffer(len, int.MaxValue);
                        Assert.Equal(len, buf.WritableBytes);
                        while (buf.IsWritable())
                        {
                            buf.WriteByte(i);
                        }

                        buffers.Enqueue(buf);
                    }
                    ReleaseBuffersAndCheckContent();
                }
            }
            catch (Exception cause)
            {
                Volatile.Write(ref finish, cause);
            }
            finally
            {
                ReleaseBuffersAndCheckContent();
            }
        }

        private void ReleaseBuffersAndCheckContent()
        {
            int i = 0;
            while (!buffers.IsEmpty)
            {
                buffers.TryDequeue(out ByteBuf buf);
                while (buf.IsReadable())
                {
                    Assert.Equal(i, buf.ReadByte());
                }
                buf.Release();
                i++;
            }
        }

        internal bool IsFinished()
        {
            return Volatile.Read(ref finish) != null;
        }

        internal void MarkAsFinished()
        {
            Interlocked.CompareExchange(ref finish, true, null);
        }

        internal void JoinAndCheckForError()
        {
            try
            {
                // Mark as finish if not already done but ensure we not override the previous set error.
                thread.Join();
            }
            finally
            {
                ReleaseBuffersAndCheckContent();
            }
            CheckForError();
        }

        internal void CheckForError()
        {
            object obj = Volatile.Read(ref finish);
            if (obj is Exception e)
            {
                ExceptionDispatchInfo.Capture(e).Throw();
            }
        }
    }

    private static PooledByteBuf UnwrapIfNeeded(ByteBuf buf)
    {
        return (PooledByteBuf) (buf is PooledByteBuf ? buf : buf.Unwrap());
    }

    [Fact]
    public void TestCacheWorksForNormalAllocations()
    {
        using PooledByteBufAllocator allocator =
                new PooledByteBufAllocator(true, 0, 1,
                        PooledByteBufAllocator.DefaultPageSize(), PooledByteBufAllocator.DefaultMaxOrder(),
                        128, 128, true);
        // CLR: DEFAULT_MAX_CACHED_BUFFER_CAPACITY is a private per-allocator snapshot of the same
        // io.netty.allocator.maxCachedBufferCapacity default (src/Netty.NET.Buffer/PooledByteBufAllocator.cs).
        int maxCachedBufferCapacity = (int)typeof(PooledByteBufAllocator)
                .GetField("_maxCachedBufferCapacity", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(allocator)!;
        ByteBuf buffer = allocator.DirectBuffer(maxCachedBufferCapacity);
        Assert.Equal(1, allocator.Metric.DirectArenas[0].NumNormalAllocations);
        buffer.Release();

        buffer = allocator.DirectBuffer(maxCachedBufferCapacity);
        // Should come out of the cache so the count should not be incremented
        Assert.Equal(1, allocator.Metric.DirectArenas[0].NumNormalAllocations);
        buffer.Release();

        // Should be allocated without cache and also not put back in a cache.
        buffer = allocator.DirectBuffer(maxCachedBufferCapacity + 1);
        Assert.Equal(2, allocator.Metric.DirectArenas[0].NumNormalAllocations);
        buffer.Release();

        buffer = allocator.DirectBuffer(maxCachedBufferCapacity + 1);
        Assert.Equal(3, allocator.Metric.DirectArenas[0].NumNormalAllocations);
        buffer.Release();
    }

    [Fact]
    public void TestNormalPoolSubpageRelease()
    {
        // 16 < elemSize <= 7168 or 8192 < elemSize <= 28672, 1 < subpage.maxNumElems <= 256
        // 7168 <= elemSize <= 8192, subpage.maxNumElems == 1
        int elemSize = 8192;
        int length = 1024;
        ByteBuf[] byteBufs = new ByteBuf[length];
        using PooledByteBufAllocator allocator = new PooledByteBufAllocator(false, 32, 32, 8192, 11, 256, 64, false, 0);

        for (int i = 0; i < length; i++)
        {
            byteBufs[i] = allocator.HeapBuffer(elemSize, elemSize);
        }
        PoolChunk chunk = UnwrapIfNeeded(byteBufs[0]).Allocation.Chunk;

        int beforeFreeBytes = chunk.FreeBytes;
        for (int i = 0; i < length; i++)
        {
            byteBufs[i].Release();
        }
        int afterFreeBytes = chunk.FreeBytes;

        Assert.True(beforeFreeBytes < afterFreeBytes);
    }

    [Fact]
    public override void TestUsedDirectMemory()
    {
        for (int power = 0; power < 8; power++)
        {
            int initialCapacity = 1024 << power;
            TestUsedDirectMemory0(initialCapacity);
        }
    }

    // CLR: xUnit1024 forbids overloading the TestUsedDirectMemory test name.
    private void TestUsedDirectMemory0(int initialCapacity)
    {
        PooledByteBufAllocator allocator = NewAllocator(true);
        IByteBufAllocatorMetric metric = allocator.Metric;
        Assert.Equal(0, metric.UsedDirectMemory);
        Assert.Equal(0, allocator.PinnedDirectMemory);
        ByteBuf buffer = allocator.DirectBuffer(initialCapacity, 4 * initialCapacity);
        int capacity = buffer.Capacity;
        Assert.Equal(ExpectedUsedMemory(allocator, capacity), metric.UsedDirectMemory);
        Assert.InRange(allocator.PinnedDirectMemory, capacity, metric.UsedDirectMemory);

        // Double the size of the buffer
        buffer.Capacity = capacity << 1;
        capacity = buffer.Capacity;
        AssertEquals(ExpectedUsedMemory(allocator, capacity), metric.UsedDirectMemory, buffer.ToString());
        Assert.InRange(allocator.PinnedDirectMemory, capacity, metric.UsedDirectMemory);

        buffer.Release();
        Assert.Equal(ExpectedUsedMemoryAfterRelease(allocator, capacity), metric.UsedDirectMemory);
        Assert.InRange(allocator.PinnedDirectMemory, 0, metric.UsedDirectMemory);
        TrimCaches(allocator);
        Assert.Equal(0, allocator.PinnedDirectMemory);

        int[] capacities = new int[30];
        // CLR: an unseeded System.Random replaces the unseeded SplittableRandom.
        Random rng = new Random();
        for (int i = 0; i < capacities.Length; i++)
        {
            capacities[i] = initialCapacity / 4 + rng.Next(8 * initialCapacity);
        }
        ByteBuf[] bufs = new ByteBuf[capacities.Length];
        for (int i = 0; i < 20; i++)
        {
            bufs[i] = allocator.DirectBuffer(capacities[i], 2 * capacities[i]);
        }
        for (int i = 0; i < 10; i++)
        {
            bufs[i].Release();
        }
        for (int i = 20; i < 30; i++)
        {
            bufs[i] = allocator.DirectBuffer(capacities[i], 2 * capacities[i]);
        }
        for (int i = 0; i < 10; i++)
        {
            bufs[i] = allocator.DirectBuffer(capacities[i], 2 * capacities[i]);
        }
        for (int i = 0; i < 30; i++)
        {
            bufs[i].Release();
        }
        TrimCaches(allocator);
        Assert.Equal(0, allocator.PinnedDirectMemory);
    }

    [Fact]
    public override void TestUsedHeapMemory()
    {
        for (int power = 0; power < 8; power++)
        {
            int initialCapacity = 1024 << power;
            TestUsedHeapMemory0(initialCapacity);
        }
    }

    // CLR: xUnit1024 forbids overloading the TestUsedHeapMemory test name.
    private void TestUsedHeapMemory0(int initialCapacity)
    {
        PooledByteBufAllocator allocator = NewAllocator(true);
        IByteBufAllocatorMetric metric = allocator.Metric;

        Assert.Equal(0, metric.UsedHeapMemory);
        Assert.Equal(0, allocator.PinnedDirectMemory);
        ByteBuf buffer = allocator.HeapBuffer(initialCapacity, 4 * initialCapacity);
        int capacity = buffer.Capacity;
        Assert.Equal(ExpectedUsedMemory(allocator, capacity), metric.UsedHeapMemory);
        Assert.InRange(allocator.PinnedHeapMemory, capacity, metric.UsedHeapMemory);

        // Double the size of the buffer
        buffer.Capacity = capacity << 1;
        capacity = buffer.Capacity;
        Assert.Equal(ExpectedUsedMemory(allocator, capacity), metric.UsedHeapMemory);
        Assert.InRange(allocator.PinnedHeapMemory, capacity, metric.UsedHeapMemory);

        buffer.Release();
        Assert.Equal(ExpectedUsedMemoryAfterRelease(allocator, capacity), metric.UsedHeapMemory);
        Assert.InRange(allocator.PinnedHeapMemory, 0, metric.UsedHeapMemory);
        TrimCaches(allocator);
        Assert.Equal(0, allocator.PinnedHeapMemory);

        int[] capacities = new int[30];
        // CLR: an unseeded System.Random replaces the unseeded SplittableRandom.
        Random rng = new Random();
        for (int i = 0; i < capacities.Length; i++)
        {
            capacities[i] = initialCapacity / 4 + rng.Next(8 * initialCapacity);
        }
        ByteBuf[] bufs = new ByteBuf[capacities.Length];
        for (int i = 0; i < 20; i++)
        {
            bufs[i] = allocator.HeapBuffer(capacities[i], 2 * capacities[i]);
        }
        for (int i = 0; i < 10; i++)
        {
            bufs[i].Release();
        }
        for (int i = 20; i < 30; i++)
        {
            bufs[i] = allocator.HeapBuffer(capacities[i], 2 * capacities[i]);
        }
        for (int i = 0; i < 10; i++)
        {
            bufs[i] = allocator.HeapBuffer(capacities[i], 2 * capacities[i]);
        }
        for (int i = 0; i < 30; i++)
        {
            bufs[i].Release();
        }
        TrimCaches(allocator);
        Assert.Equal(0, allocator.PinnedDirectMemory);
    }

    [Fact]
    public void PinnedMemoryMustReflectBuffersInUseWithThreadLocalCaching()
    {
        PinnedMemoryMustReflectBuffersInUse(true);
    }

    [Fact]
    public void PinnedMemoryMustReflectBuffersInUseWithoutThreadLocalCaching()
    {
        PinnedMemoryMustReflectBuffersInUse(false);
    }

    private static void PinnedMemoryMustReflectBuffersInUse(bool useThreadLocalCaching)
    {
        int smallCacheSize;
        int normalCacheSize;
        if (useThreadLocalCaching)
        {
            smallCacheSize = PooledByteBufAllocator.DefaultSmallCacheSize();
            normalCacheSize = PooledByteBufAllocator.DefaultNormalCacheSize();
        }
        else
        {
            smallCacheSize = 0;
            normalCacheSize = 0;
        }
        int directMemoryCacheAlignment = 0;
        using PooledByteBufAllocator alloc = new PooledByteBufAllocator(
                PooledByteBufAllocator.DefaultPreferDirect(),
                1,
                1,
                PooledByteBufAllocator.DefaultPageSize(),
                PooledByteBufAllocator.DefaultMaxOrder(),
                smallCacheSize,
                normalCacheSize,
                useThreadLocalCaching,
                directMemoryCacheAlignment);
        PooledByteBufAllocatorMetric metric = alloc.Metric;
        long capSum = 0;

        for (long index = 0; index < 10000; index++)
        {
            // CLR: Random.Shared replaces ThreadLocalRandom.current().
            Random rnd = Random.Shared;
            int bufCount = rnd.Next(1, 100);
            List<ByteBuf> buffers = new List<ByteBuf>(bufCount);

            if (index % 2 == 0)
            {
                // ensure that we allocate a small buffer
                for (int i = 0; i < bufCount; i++)
                {
                    ByteBuf buf = alloc.DirectBuffer(rnd.Next(8, 128));
                    buffers.Add(buf);
                    capSum += buf.Capacity;
                }
            }
            else
            {
                // allocate a larger buffer
                for (int i = 0; i < bufCount; i++)
                {
                    ByteBuf buf = alloc.DirectBuffer(rnd.Next(1024, 1024 * 100));
                    buffers.Add(buf);
                    capSum += buf.Capacity;
                }
            }

            if (index % 100 == 0)
            {
                long used = UsedMemory(metric.DirectArenas);
                long pinned = alloc.PinnedDirectMemory;
                Assert.True(capSum <= pinned);
                Assert.True(pinned <= used);
            }

            foreach (ByteBuf buffer in buffers)
            {
                buffer.Release();
            }
            capSum = 0;
            // After releasing all buffers, pinned memory must be zero
            Assert.Equal(0, alloc.PinnedDirectMemory);
        }
    }

    /**
     * Returns an estimate of bytes used by currently in-use buffers
     */
    private static long UsedMemory(IReadOnlyList<IPoolArenaMetric> arenas)
    {
        long totalUsed = 0;
        foreach (IPoolArenaMetric arenaMetrics in arenas)
        {
            foreach (IPoolChunkListMetric arenaMetric in arenaMetrics.ChunkLists)
            {
                foreach (IPoolChunkMetric chunkMetric in arenaMetric)
                {
                    // chunkMetric.chunkSize() returns maximum of bytes that can be served out of the chunk
                    // and chunkMetric.freeBytes() returns the bytes that are not yet allocated by in-use buffers
                    totalUsed += chunkMetric.ChunkSize - chunkMetric.FreeBytes;
                }
            }
        }
        return totalUsed;
    }

    [Fact]
    public void TestCapacityChangeDoesntThrowAssertionError()
    {
        IByteBufAllocator allocator = NewAllocator(true);
        List<ByteBuf> buffers = new List<ByteBuf>();
        try
        {
            for (int i = 0; i < 31; i++)
            {
                buffers.Add(allocator.HeapBuffer());
            }

            ByteBuf buf = allocator.HeapBuffer();
            buffers.Add(buf);
            // CLR: Java assert statements are Debug.Assert here, and a failed Debug.Assert terminates the test host
            // instead of throwing AssertionError (https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.debug.assert?view=net-10.0).
            // An exception escaping a CLR thread would also terminate the process, so every exception is captured
            // and rethrown on the test thread.
            Exception assertionRef = null;
            ThreadStart capacityChangeTask = () =>
            {
                try
                {
                    buf.Capacity = 512;
                }
                catch (Exception e)
                {
                    Interlocked.CompareExchange(ref assertionRef, e, null);
                }
            };
            Thread thread1 = new Thread(capacityChangeTask);
            Thread thread2 = new Thread(capacityChangeTask);

            thread1.Start();
            thread2.Start();

            thread1.Join();
            thread2.Join();

            buffers.Add(allocator.HeapBuffer());
            buffers.Add(allocator.HeapBuffer());

            Exception error = Volatile.Read(ref assertionRef);
            if (error != null)
            {
                ExceptionDispatchInfo.Capture(error).Throw();
            }
        }
        finally
        {
            foreach (ByteBuf buffer in buffers)
            {
                buffer.Release();
            }
        }
    }
}
#pragma warning restore CS0618

// CLR: supplemental pooled-allocator contracts that are not part of the original fixture; they run once instead of
// once per concrete PooledByteBufAllocatorTest/AlignedPooledByteBufAllocatorTest instance.
public class PooledByteBufAllocatorClrContractTest
{
    // Like the source fixtures' new PooledByteBufAllocator(true, 1, 1, 8192, 9, 0, 0, 0): one arena per kind and no
    // thread caches.
    private static PooledByteBufAllocator NewAllocator(bool direct = false, int alignment = 0,
        NativeMemoryAllocator native = null)
    {
        return new(direct, 1, 1, pageSize: 8192, maxOrder: 4,
            directMemoryCacheAlignment: alignment, nativeAllocator: native, smallCacheSize: 0, normalCacheSize: 0);
    }

    // leastUsedArena binds the first thread to arenas[0].
    private static IPoolArenaMetric Arena(PooledByteBufAllocator a, bool direct)
    {
        return (direct ? a.Metric.DirectArenas : a.Metric.HeapArenas)[0];
    }

    private static long Pinned(PooledByteBufAllocator a, bool direct)
    {
        return direct ? a.PinnedDirectMemory : a.PinnedHeapMemory;
    }

    [Theory]
    [InlineData(false, false, false)] [InlineData(false, false, true)]
    [InlineData(false, true, false)] [InlineData(false, true, true)]
    [InlineData(true, false, false)] [InlineData(true, false, true)]
    [InlineData(true, true, false)] [InlineData(true, true, true)]
    public void PublicFactoriesPreserveStoragePolicyProvenanceAndDisabledPoolFallback(bool preferDirect, bool poolHeap, bool poolDirect)
    {
        var native = new NativeMemoryAllocator();
        using var a = new PooledByteBufAllocator(preferDirect, poolHeap, poolDirect, maxOrder: 4, nativeAllocator: native);
        Assert.Same(a.Metric, ((IByteBufAllocatorMetricProvider)a).Metric);
        Assert.Equal(poolDirect, a.IsDirectBufferPooled);
        // The convenience pool flags select the DEFAULT_NUM_*_ARENA counts the source's shorter constructors pass.
        Assert.Equal(poolHeap ? PooledByteBufAllocator.DefaultNumHeapArena() : 0, a.Metric.NumHeapArenas);
        Assert.Equal(poolDirect ? PooledByteBufAllocator.DefaultNumDirectArena() : 0, a.Metric.NumDirectArenas);
        var factories = new Func<ByteBuf>[] { () => a.Buffer(), () => a.HeapBuffer(), () => a.DirectBuffer(), () => a.IoBuffer() };
        for (int i = 0; i < factories.Length; ++i)
        {
            ByteBuf b = factories[i]();
            bool direct = i == 0 ? preferDirect : i >= 2;
            try
            {
                Assert.Equal(direct, b.IsDirect);
                Assert.Same(a, b.Alloc());
                Assert.Equal(256, b.Capacity);
                Assert.Equal(int.MaxValue, b.MaxCapacity);
                Assert.Equal(0, b.ReaderIndex);
                Assert.Equal(0, b.WriterIndex);
                Assert.Equal(1, b.RefCnt);
                Assert.True(b.IsContiguous);
                Assert.Equal(direct ? poolDirect : poolHeap, UnwrapLeak(b) is PooledByteBuf);
                b.WriteLong(0x0102030405060708);
                Assert.Equal(0x0102030405060708, b.ReadLong());
            }
            finally
            {
                b.Release();
            }
        }
        Assert.Equal(poolHeap ? 131072 : -1, a.Metric.UsedHeapMemory);
        Assert.Equal(poolDirect ? 131072 : -1, a.Metric.UsedDirectMemory);
        Assert.Equal(poolHeap ? 0 : -1, a.PinnedHeapMemory);
        Assert.Equal(poolDirect ? 0 : -1, a.PinnedDirectMemory);
        a.Dispose();
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(0)] [InlineData(64)]
    public void OriginalArenaMetricsWithoutCacheCountEveryReservationAndReturn(int alignment)
    {
        using var a = NewAllocator(alignment: alignment);
        for (int i = 0; i < 100; ++i)
        {
            Assert.True(a.HeapBuffer().Release());
            Assert.True(a.DirectBuffer().Release());
        }
        foreach (var arena in a.Metric.HeapArenas.Concat(a.Metric.DirectArenas))
        {
            Assert.Equal(100, arena.NumAllocations);
            Assert.Equal(100, arena.NumDeallocations);
            Assert.Equal(0, arena.NumActiveAllocations);
            Assert.Equal(100, arena.NumSmallAllocations);
            Assert.Equal(100, arena.NumSmallDeallocations);
            Assert.Equal(0, arena.NumNormalAllocations);
            Assert.Equal(0, arena.NumHugeAllocations);
            Assert.Equal(1, arena.NumChunkAllocations);
            Assert.Equal(0, arena.NumChunkDeallocations);
            Assert.Equal(1, arena.NumActiveChunks);
            Assert.Equal(a.Metric.ChunkSize, arena.NumActiveBytes);
            Assert.Equal(1, arena.NumThreadCaches);
        }
        Assert.Equal(0, a.Metric.SmallCacheSize);
        Assert.Equal(0, a.Metric.NormalCacheSize);
        Assert.Equal(1, a.Metric.NumThreadLocalCaches);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalSmallSubpageAndChunkListMetricsArePublicReadonlySnapshots(bool direct)
    {
        using var a = NewAllocator(direct);
        IPoolArenaMetric arena = Arena(a, direct);
        Assert.Equal(6, arena.NumChunkLists);
        Assert.Equal(new[] { 1, 1, 25, 50, 75, 100 }, arena.ChunkLists.Select(l => l.MinUsage));
        Assert.Equal(new[] { 25, 50, 75, 100, 100, 100 }, arena.ChunkLists.Select(l => l.MaxUsage));
        Assert.Empty(arena.SmallSubpages);
        ByteBuf b = a.Buffer(500);
        var snapshot = arena.SmallSubpages;
        try
        {
            IPoolSubpageMetric subpage = Assert.Single(snapshot);
            Assert.Equal(1, subpage.MaxNumElements - subpage.NumAvailable);
            Assert.Equal(512, subpage.ElementSize);
            Assert.Throws<NotSupportedException>(() => ((IList<IPoolSubpageMetric>)snapshot).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<IPoolArenaMetric>)a.Metric.HeapArenas).Clear());
            Assert.Equal(arena.SizeIdx2Size(arena.Size2SizeIdx(500)), arena.NormalizeSize(500));
            Assert.Equal(arena.SizeIdx2Size(0), arena.SizeIdx2SizeCompute(0));
            Assert.Equal(arena.PageIdx2Size(3), arena.PageIdx2SizeCompute(3));
            Assert.Equal(arena.Pages2PageIdx(4), arena.Pages2PageIdxFloor(4));
        }
        finally
        {
            b.Release();
        }
        Assert.Equal(snapshot[0].MaxNumElements, snapshot[0].NumAvailable);
        a.Dispose();
        Assert.Empty(arena.SmallSubpages);
#pragma warning disable CS0618
        Assert.Equal(0, arena.NumTinySubpages);
        Assert.Empty(arena.TinySubpages);
        Assert.Equal(0, arena.NumTinyAllocations);
        Assert.Equal(0, arena.NumTinyDeallocations);
        Assert.Equal(0, arena.NumActiveTinyAllocations);
        Assert.Equal(0, a.Metric.TinyCacheSize);
#pragma warning restore CS0618
    }

    [Theory]
    [InlineData(false, 1024)] [InlineData(true, 1024)]
    [InlineData(false, 2048)] [InlineData(true, 2048)]
    [InlineData(false, 4096)] [InlineData(true, 4096)]
    [InlineData(false, 8192)] [InlineData(true, 8192)]
    [InlineData(false, 16384)] [InlineData(true, 16384)]
    [InlineData(false, 32768)] [InlineData(true, 32768)]
    [InlineData(false, 65536)] [InlineData(true, 65536)]
    [InlineData(false, 131072)] [InlineData(true, 131072)]
    public void OriginalUsedAndPinnedMemoryAcrossResizeAndFinalRelease(bool direct, int initial)
    {
        using var a = new PooledByteBufAllocator(direct);
        IPoolArenaMetric arena = Arena(a, direct);
        Assert.Equal(0, arena.NumActiveBytes);
        Assert.Equal(0, Pinned(a, direct));
        ByteBuf b = a.Buffer(initial, initial * 4);
        try
        {
            Assert.Equal(a.Metric.ChunkSize, arena.NumActiveBytes);
            Assert.InRange(Pinned(a, direct), initial, arena.NumActiveBytes);
            // Double the size of the buffer
            b.Capacity = initial * 2;
            Assert.Equal(a.Metric.ChunkSize, arena.NumActiveBytes);
            Assert.InRange(Pinned(a, direct), initial * 2, arena.NumActiveBytes);
            b.Retain();
            Assert.Equal(1, arena.NumActiveAllocations);
            Assert.False(b.Release());
        }
        finally
        {
            b.Release();
        }
        // This is the case as allocations will start in qInit and chunks in qInit will never be released until
        // these are moved to q000.
        // See https://www.bsdcan.org/2006/papers/jemalloc.pdf
        Assert.Equal(a.Metric.ChunkSize, arena.NumActiveBytes);
        Assert.Equal(0, Pinned(a, direct));
        Assert.Equal(0, arena.NumActiveAllocations);
        Assert.Equal(arena.NumAllocations, arena.NumDeallocations);
        a.Dispose();
        Assert.Equal(0, arena.NumActiveBytes);
        Assert.Equal(0, arena.NumActiveChunks);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OriginalWholeChunkReleaseRetiresStorageAndPreservesCreator(bool direct)
    {
        using var a = NewAllocator(direct);
        IPoolArenaMetric arena = Arena(a, direct);
        ByteBuf b = a.Buffer(a.Metric.ChunkSize);
        Assert.Same(a, b.Alloc());
        // Must end up in the 6th PoolChunkList
        Assert.Single(arena.ChunkLists[5]);
        Assert.Equal(1, arena.NumNormalAllocations);
        Assert.Equal(1, arena.NumActiveChunks);
        b.Release();
        Assert.Same(a, b.Alloc());
        // Should be completely removed and so all PoolChunkLists must be empty
        Assert.All(arena.ChunkLists, list => Assert.Empty(list));
        Assert.Equal(1, arena.NumChunkDeallocations);
        Assert.Equal(0, arena.NumActiveChunks);
        Assert.Equal(0, arena.NumActiveBytes);
        Assert.Equal(0, Pinned(a, direct));
        var retry = a.Buffer(a.Metric.ChunkSize);
        try
        {
            Assert.Equal(2, arena.NumChunkAllocations);
            Assert.Equal(1, arena.NumActiveChunks);
        }
        finally
        {
            retry.Release();
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(64)] [InlineData(8192)]
    public void AlignedHugeMetricsCountNormalizedReservationWithoutHeadroom(int alignment)
    {
        var native = new NativeMemoryAllocator();
        using var a = NewAllocator(true, alignment, native);
        IPoolArenaMetric arena = Arena(a, true);
        int requested = a.Metric.ChunkSize + 1;
        ByteBuf b = a.DirectBuffer(requested, requested);
        int reserved = alignment == 0 ? requested : arena.NormalizeSize(requested);
        try
        {
            Assert.Equal(requested, b.Capacity);
            Assert.Equal(requested, b.MaxCapacity);
            Assert.Equal(reserved, arena.NumActiveBytes);
            Assert.Equal(reserved, a.PinnedDirectMemory);
            Assert.Equal(reserved + alignment, native.ReservedBytes);
            Assert.Equal(1, arena.NumHugeAllocations);
            Assert.Equal(1, arena.NumActiveHugeAllocations);
            Assert.Equal(0, arena.NumChunkAllocations);
            Assert.Equal(0, arena.NumActiveChunks);
        }
        finally
        {
            b.Release();
        }
        Assert.Equal(1, arena.NumHugeDeallocations);
        Assert.Equal(0, arena.NumActiveBytes);
        Assert.Equal(0, native.ReservedBytes);
        Assert.Equal(0, a.PinnedDirectMemory);
    }

    [Theory]
    [InlineData(0)] [InlineData(64)]
    public void FailedNativeGrowthAndRetryDoNotChargeUnpublishedReservations(int alignment)
    {
        const int chunk = 131072;
        var native = new NativeMemoryAllocator(chunk + alignment);
        using var a = NewAllocator(true, alignment, native);
        IPoolArenaMetric arena = Arena(a, true);
        ByteBuf b = a.DirectBuffer(chunk, chunk * 2).WriteInt(42);
        Assert.Throws<OutOfDirectMemoryError>(() => b.Capacity = chunk + 1);
        Assert.Equal(42, b.GetInt(0));
        Assert.Equal(1, arena.NumAllocations);
        Assert.Equal(0, arena.NumDeallocations);
        Assert.Equal(chunk, arena.NumActiveBytes);
        Assert.Equal(chunk, a.PinnedDirectMemory);
        Assert.Equal(1, arena.NumChunkAllocations);
        b.Release();
        Assert.Equal(0, native.ReservedBytes);
        ByteBuf retry = a.DirectBuffer(48);
        try
        {
            Assert.Equal(2, arena.NumAllocations);
            Assert.Equal(2, arena.NumChunkAllocations);
        }
        finally
        {
            retry.Release();
        }
    }

    [Theory]
    [InlineData(false, 48)] [InlineData(true, 48)]
    [InlineData(false, 131072)] [InlineData(true, 131072)]
    [InlineData(false, 131073)] [InlineData(true, 131073)]
    public void ShutdownMetricsKeepActiveReservationsUntilFinalInternalLease(bool direct, int size)
    {
        var native = new NativeMemoryAllocator();
        using var a = NewAllocator(direct, native: native);
        IPoolArenaMetric arena = Arena(a, direct);
        ByteBuf b = a.Buffer(size).WriteInt(42);
        long pinned = Pinned(a, direct);
        Memory<byte> memory = b.AsMemory(0, 4);
        using (var lease = b.AcquireReadLease())
        {
            a.Dispose();
            a.Dispose();
            Assert.Throws<ObjectDisposedException>(() => a.Buffer(1));
            Assert.Equal(1, arena.NumActiveAllocations);
            Assert.True(arena.NumActiveBytes >= size);
            Assert.All(arena.ChunkLists, list => Assert.Empty(list));
            b.Release();
            Assert.Equal(pinned, Pinned(a, direct));
            Assert.Equal(1, arena.NumActiveAllocations);
            Assert.Equal(new byte[] { 0, 0, 0, 42 }, memory.Span.ToArray());
        }
        Assert.Equal(0, arena.NumActiveBytes);
        Assert.Equal(0, Pinned(a, direct));
        Assert.Equal(0, arena.NumActiveAllocations);
        Assert.Equal(0, arena.NumActiveChunks);
        Assert.Equal(arena.NumAllocations, arena.NumDeallocations);
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void RuntimePinDoesNotRetainTheAllocatorReservation()
    {
        var native = new NativeMemoryAllocator();
        using var a = NewAllocator(true, native: native);
        ByteBuf b = a.DirectBuffer(131072).WriteInt(42);
        ReadOnlySpan<byte> captured = b.AsReadOnlySpan(0, 4);
        using (var pin = b.AsMemory(0, 4).Pin())
        {
            b.Release(); Assert.Equal(0, a.PinnedDirectMemory); Assert.Equal(0, a.Metric.UsedDirectMemory);
            Assert.Equal(0, native.ReservedBytes); Assert.Equal(42, captured[3]);
        }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CopiesViewsAndCompositePaddingUsePublicAllocatorAndCountOnlyReservations(bool direct)
    {
        using var a = NewAllocator(direct);
        var arena = Arena(a, direct);
        ByteBuf root = a.Buffer(48).WriteLong(42);
        ByteBuf view = root.RetainedSlice(0, 8), copy = view.Copy();
        try
        {
            Assert.Same(a, copy.Alloc());
            Assert.Equal(direct, copy.IsDirect);
            Assert.IsAssignableFrom<PooledByteBuf>(UnwrapLeak(copy));
            Assert.Equal(2, arena.NumActiveAllocations);
            using var compositeLease = root.AcquireReadLease();
            Assert.Equal(2, arena.NumActiveAllocations);
            Assert.False(root.Release());
            Assert.True(view.Release());
        }
        finally
        {
            copy.Release();
            if (root.RefCnt != 0)
            {
                root.Release(root.RefCnt);
            }
        }
        CompositeByteBuf composite = a.CompositeBuffer(1);
        try
        {
            composite.Capacity = 100;
            composite.WriteLong(99);
            Assert.Equal(direct, composite.IsDirect);
            Assert.Same(a, composite.Component(0).Alloc());
            Assert.Equal(1, arena.NumActiveAllocations);
        }
        finally
        {
            composite.Release();
        }
        Assert.Equal(0, arena.NumActiveAllocations);
        Assert.Equal(0, Pinned(a, direct));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UnpooledFallbackRemainsIndependentlyOwnedAfterFactoryShutdown(bool direct)
    {
        var native = new NativeMemoryAllocator();
        using var a = new PooledByteBufAllocator(direct, false, false, nativeAllocator: native);
        ByteBuf b = a.Buffer(8, 32).WriteInt(42);
        a.Dispose();
        try
        {
            Assert.Throws<ObjectDisposedException>(() => a.Buffer(1));
            b.Capacity = 16;
            Assert.Equal(42, b.GetInt(0));
            Assert.Equal(-1, a.Metric.UsedHeapMemory);
            Assert.Equal(-1, a.Metric.UsedDirectMemory);
            Assert.Same(a.HeapBuffer(0, 0), a.DirectBuffer(0, 0));
            CompositeByteBuf empty = a.CompositeBuffer();
            Assert.Equal(0, empty.Capacity);
            empty.Release();
        }
        finally
        {
            b.Release();
        }
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(0, 4, 0)] [InlineData(1024, 4, 0)] [InlineData(4097, 4, 0)]
    [InlineData(8192, -1, 0)] [InlineData(8192, 15, 0)] [InlineData(131072, 14, 0)]
    [InlineData(8192, 4, -1)] [InlineData(8192, 4, 3)] [InlineData(int.MaxValue, 4, 64)]
    public void InvalidGeometryAndAlignmentFailBeforeNativeReservation(int pageSize, int maxOrder, int alignment)
    {
        var native = new NativeMemoryAllocator();
        Assert.ThrowsAny<ArgumentException>(() => new PooledByteBufAllocator(pageSize: pageSize, maxOrder: maxOrder,
            directMemoryCacheAlignment: alignment, nativeAllocator: native));
        Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void OriginalPageAlignmentPromotionAndClrDefaultChoices()
    {
        var native = new NativeMemoryAllocator();
        using var a = new PooledByteBufAllocator(pageSize: 4096, maxOrder: 4, directMemoryCacheAlignment: 8192, nativeAllocator: native);
        Assert.Equal(131072, a.Metric.ChunkSize);
        Assert.True(PooledByteBufAllocator.IsDirectMemoryCacheAlignmentSupported);
        ByteBuf b = a.Buffer(0, 1);
        Assert.False(b.IsDirect);
        b.Release();
        ByteBuf d = PooledByteBufAllocator.Default.Buffer(1);
        Assert.Equal(ByteBufUtil.PreferDirect, d.IsDirect);
        d.Release();
        Assert.Same(PooledByteBufAllocator.Default, PooledByteBufAllocator.Default);
        Assert.Same(ByteBufUtil.DefaultAllocator, IByteBufAllocator.Default);
        Assert.Equal(0, native.ReservedBytes);
        Assert.Contains("smallCacheSize: " + PooledByteBufAllocator.DefaultSmallCacheSize() + "; normalCacheSize: " +
            PooledByteBufAllocator.DefaultNormalCacheSize() + "; numThreadLocalCaches: 1; chunkSize: 131072", a.Metric.ToString());
    }

    [Fact]
    public void MaximumGeometryReservesNoBackingBeforeAllocation()
    {
        var native = new NativeMemoryAllocator(0);
        using var a = new PooledByteBufAllocator(pageSize: 65536, maxOrder: 14, nativeAllocator: native);
        Assert.Equal(1 << 30, a.Metric.ChunkSize);
        Assert.Equal(0, a.Metric.UsedHeapMemory);
        Assert.Equal(0, a.Metric.UsedDirectMemory);
        Assert.Equal(0, native.ReservedBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ConcurrentReservationsKeepExactCountersThroughShutdown(bool direct)
    {
        var native = new NativeMemoryAllocator();
        using var a = NewAllocator(direct, native: native);
        var arena = Arena(a, direct);
        var token = TestContext.Current.CancellationToken;
        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (int i = 0; i < 128; ++i)
            {
                ByteBuf b = a.Buffer(new[] { 48, 32768, 131073 }[i % 3]);
                try
                {
                    b.WriteInt(worker);
                    Assert.Equal(worker, b.GetInt(0));
                }
                finally
                {
                    b.Release();
                }
            }
        }, token)));
        Assert.Equal(1024, arena.NumAllocations);
        Assert.Equal(1024, arena.NumDeallocations);
        Assert.Equal(0, arena.NumActiveAllocations);
        Assert.Equal(0, Pinned(a, direct));
        a.Dispose();
        Assert.Equal(0, arena.NumActiveBytes);
        Assert.Equal(0, arena.NumActiveChunks);
        Assert.Equal(arena.NumChunkAllocations, arena.NumChunkDeallocations);
        Assert.Equal(0, native.ReservedBytes);
    }
}
