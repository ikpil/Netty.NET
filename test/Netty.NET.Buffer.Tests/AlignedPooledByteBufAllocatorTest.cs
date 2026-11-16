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
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;

namespace Netty.NET.Buffer.Tests;

public class AlignedPooledByteBufAllocatorTest : PooledByteBufAllocatorTest
{
    protected override PooledByteBufAllocator NewAllocator(bool preferDirect)
    {
        Assert.SkipUnless(PooledByteBufAllocator.IsDirectMemoryCacheAlignmentSupported,
                "PooledByteBufAllocator.isDirectMemoryCacheAlignmentSupported()");
        int directMemoryCacheAlignment = 1;
        return Track(new PooledByteBufAllocator(
                preferDirect,
                PooledByteBufAllocator.DefaultNumHeapArena(),
                PooledByteBufAllocator.DefaultNumDirectArena(),
                PooledByteBufAllocator.DefaultPageSize(),
                11,
                PooledByteBufAllocator.DefaultSmallCacheSize(),
                64,
                PooledByteBufAllocator.DefaultUseCacheForAllThreads(),
                directMemoryCacheAlignment));
    }

    // https://github.com/netty/netty/issues/11955
    [Fact]
    public void TestCorrectElementSize()
    {
        Assert.SkipUnless(PooledByteBufAllocator.IsDirectMemoryCacheAlignmentSupported,
                "PooledByteBufAllocator.isDirectMemoryCacheAlignmentSupported()");
        IByteBufAllocator allocator = Track(new PooledByteBufAllocator(
                true,
                PooledByteBufAllocator.DefaultNumHeapArena(),
                PooledByteBufAllocator.DefaultNumDirectArena(),
                PooledByteBufAllocator.DefaultPageSize(),
                11,
                PooledByteBufAllocator.DefaultSmallCacheSize(),
                64,
                PooledByteBufAllocator.DefaultUseCacheForAllThreads(),
                64));

        ByteBuf a = allocator.DirectBuffer(0, 16384);
        ByteBuf b = allocator.DirectBuffer(0, 16384);
        a.Capacity = 16;
        Assert.Equal(16, a.Capacity);
        b.Capacity = 16;
        Assert.Equal(16, b.Capacity);
        a.Capacity = 17;
        Assert.Equal(17, a.Capacity);
        b.Capacity = 18;
        Assert.Equal(18, b.Capacity);
        Assert.True(a.Release());
        Assert.True(b.Release());
    }

    [Fact]
    public void TestDirectSubpageReleaseLock()
    {
        Assert.SkipUnless(PooledByteBufAllocator.IsDirectMemoryCacheAlignmentSupported,
                "PooledByteBufAllocator.isDirectMemoryCacheAlignmentSupported()");
        int initialCapacity = 0;
        int directMemoryCacheAlignment = 32;
        PooledByteBufAllocator allocator = Track(new PooledByteBufAllocator(
                true,
                0,
                1,
                PooledByteBufAllocator.DefaultPageSize(),
                PooledByteBufAllocator.DefaultMaxOrder(),
                0,
                0,
                false,
                directMemoryCacheAlignment));

        ByteBuf wrapper = allocator.DirectBuffer(initialCapacity, 16);
        PooledByteBuf unwrapped = PooledByteBuf(wrapper);
        // Get the smallSubpagePools[] array in arena.
        // CLR: the arena keeps its subpage heads in a private field; reflection stands in for the Java test's
        // package-private access (src/Netty.NET.Buffer/PoolArena.cs).
        PoolSubpage[] smallSubpagePools = (PoolSubpage[])typeof(PoolArena)
                .GetField("_smallSubpagePools", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(unwrapped.Allocation.Arena)!;
        PoolSubpage head = null;
        foreach (PoolSubpage subpage in smallSubpagePools)
        {
            if (subpage.Next != subpage)
            {
                // Find the head subpage which the byteBuf belongs to.
                head = subpage;
                break;
            }
        }
        Assert.NotNull(head);
        Thread t1 = new Thread(() =>
        {
            // Because the head subpage was already locked in the main thread, so this should hang and wait.
            wrapper.Release();
        });
        t1.IsBackground = true;
        // Intentionally lock the head subpage in main thread.
        // CLR: the head lock is the Monitor on PoolSubpage.SyncRoot (src/Netty.NET.Buffer/PoolSubpage.cs). Monitor has
        // no hasQueuedThread query, so a WaitSleepJoin thread state stands for "queued on the head lock"
        // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.threadstate?view=net-10.0).
        Monitor.Enter(head.SyncRoot);
        try
        {
            t1.Start();
            long start = Stopwatch.GetTimestamp();
            while ((t1.ThreadState & System.Threading.ThreadState.WaitSleepJoin) == 0)
            {
                if (Stopwatch.GetElapsedTime(start) > TimeSpan.FromSeconds(3))
                {
                    break;
                }
            }
            Assert.True((t1.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0,
                    "The t1 thread should still be waiting for the head lock.");
        }
        finally
        {
            Monitor.Exit(head.SyncRoot);
        }
        t1.Join();
    }

    private static PooledByteBuf PooledByteBuf(ByteBuf buffer)
    {
        // might need to unwrap if swapped (LE) and/or leak-aware-wrapped
        while (!(buffer is PooledByteBuf))
        {
            buffer = buffer.Unwrap();
        }
        return (PooledByteBuf) buffer;
    }
}
