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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer.Tests;

public class AdaptiveThreadLocalAllocatorTest
{
    private static AdaptiveByteBufAllocator Allocator(bool nonEventLoop = true, bool low = false, NativeMemoryAllocator native = null)
        => new(false, low, native, 2, 4, 1, 4, nonEventLoop, 4, 4 * 131072, 2 * 131072);
    private static void OnThread(Action action, bool fast = false)
    {
        Exception error = null;
        Action run = () => { try { if (fast) FastThreadLocalThread.RunWithFastThreadLocal(action); else action(); } catch (Exception e) { error = e; } };
        var thread = new Thread(() => run()); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15))); Assert.Null(error);
    }

    [Theory]
    [InlineData(false, false, false)] [InlineData(false, false, true)]
    [InlineData(false, true, false)] [InlineData(false, true, true)]
    [InlineData(true, false, false)] [InlineData(true, false, true)]
    [InlineData(true, true, false)] [InlineData(true, true, true)]
    public void PhysicalCleanupAndExecutorOrExplicitOptionControlEligibility(bool fast, bool mapping, bool nonEventLoop)
    {
        using var a = Allocator(nonEventLoop);
        OnThread(() =>
        {
            Action allocate = () =>
            {
                var b = a.HeapBuffer(7, 100);
                try
                {
                    bool local = fast && (mapping || nonEventLoop);
                    Assert.Equal(local ? 1 : 0, a.HeapPool.LocalBindingCount);
                    Assert.Equal(local, a.HeapPool.CurrentThreadGroup(0) != null);
                }
                finally { b.Release(); }
            };
            if (mapping) ThreadExecutorMap.Apply(allocate, ImmediateEventExecutor.INSTANCE)(); else allocate();
        }, fast);
        Assert.Equal(0, a.HeapPool.LocalBindingCount);
        Assert.Equal(fast && (mapping || nonEventLoop) ? 0 : 131072, a.UsedHeapMemory);
    }

    [Fact]
    public void LowMemoryAndBuddyAllocationsUseSharedMagazines()
    {
        using var low = Allocator(low: true); using var normal = Allocator();
        OnThread(() =>
        {
            var small = low.HeapBuffer(7); var large = normal.HeapBuffer(32768);
            try
            {
                Assert.Equal(0, low.HeapPool.LocalBindingCount);
                Assert.NotNull(normal.HeapPool.CurrentThreadGroup(0));
                Assert.Equal(262144, normal.UsedHeapMemory);
            }
            finally { small.Release(); large.Release(); }
        }, true);
        Assert.Equal(131072, low.UsedHeapMemory); Assert.Equal(262144, normal.UsedHeapMemory);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FastThreadExitReleasesCachesWhileLiveBufferAndPinSurvive(bool direct)
    {
        var native = new NativeMemoryAllocator(); using var a = Allocator(native: native);
        ByteBuf live = null; System.Buffers.MemoryHandle pin = default; Exception error = null;
        // Thread-local magazines require FastThreadLocalThread
        // (allocate() checks currentThreadWillCleanupFastThreadLocals)
        var thread = new FastThreadLocalThread(() =>
        {
            try
            {
                live = direct ? a.DirectBuffer(7, 100) : a.HeapBuffer(7, 100); live.SetInt(0, 12345);
                pin = live.AsMemory(0, 4).Pin();
                for (int i = 0; i < 1300; i++)
                { var b = direct ? a.DirectBuffer(256, 256) : a.HeapBuffer(256, 256); b.Release(); }
            }
            catch (Exception e) { error = e; }
        });
        thread.Thread.Start(); Assert.True(thread.Thread.Join(TimeSpan.FromSeconds(15))); Assert.Null(error);
        Assert.Equal(0, a.HeapPool.LocalBindingCount + a.DirectPool.LocalBindingCount);
        Assert.Equal(131072, a.UsedHeapMemory + a.UsedDirectMemory); Assert.Equal(12345, live.GetInt(0));
        live.Release(); Assert.Equal(131072, a.UsedHeapMemory + a.UsedDirectMemory);
        pin.Dispose(); Assert.Equal(0, a.UsedHeapMemory + a.UsedDirectMemory); Assert.Equal(0, native.ReservedBytes);
    }

    [Fact]
    public void ScopeFailureCleansUpAndNextScopeCreatesNewGroups()
    {
        using var a = Allocator(); AdaptiveMagazineGroup previous = null;
        OnThread(() =>
        {
            Assert.Throws<InvalidOperationException>(() => FastThreadLocalThread.RunWithFastThreadLocal(() =>
            {
                var b = a.HeapBuffer(7); b.Release(); previous = a.HeapPool.CurrentThreadGroup(0);
                throw new InvalidOperationException("scope failure");
            }));
            Assert.True(previous.IsFreed); Assert.Equal(0, a.UsedHeapMemory);
            FastThreadLocalThread.RunWithFastThreadLocal(() =>
            {
                var b = a.HeapBuffer(7); b.Release(); Assert.NotSame(previous, a.HeapPool.CurrentThreadGroup(0));
            });
        });
        Assert.Equal(0, a.UsedHeapMemory); Assert.Equal(0, a.HeapPool.LocalBindingCount);
    }

    [Fact]
    public void TaskWorkerDoesNotInheritPhysicalThreadCacheOrExecutorMapping()
    {
        using var a = Allocator();
        OnThread(() => ThreadExecutorMap.Apply(() =>
        {
            var b = a.HeapBuffer(7); var owner = a.HeapPool.CurrentThreadGroup(0); Assert.NotNull(owner);
            try
            {
                Task.Run(() =>
                {
                    Assert.False(FastThreadLocalThread.CurrentThreadWillCleanupFastThreadLocals());
                    Assert.Null(ThreadExecutorMap.CurrentExecutor()); var other = a.HeapBuffer(7);
                    try { Assert.Null(a.HeapPool.CurrentThreadGroup(0)); Assert.Equal(262144, a.UsedHeapMemory); }
                    finally { other.Release(); }
                }, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
                Assert.Same(owner, a.HeapPool.CurrentThreadGroup(0));
            }
            finally { b.Release(); }
        }, ImmediateEventExecutor.INSTANCE)(), true);
        Assert.Equal(131072, a.UsedHeapMemory);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OffThreadDisposeRacesOwnerReturnsWithoutLosingSegments(bool direct)
    {
        for (int repeat = 0; repeat < 40; repeat++)
        {
            var native = new NativeMemoryAllocator(); using var a = Allocator(native: native);
            using var ready = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            Exception error = null; ByteBuf held = null; System.Buffers.MemoryHandle pin = default;
            var owner = new Thread(() =>
            {
                try
                {
                    FastThreadLocalThread.RunWithFastThreadLocal(() =>
                    {
                        var live = new List<ByteBuf>();
                        for (int i = 0; i < 1025; i++) live.Add(direct ? a.DirectBuffer(256, 256) : a.HeapBuffer(256, 256));
                        held = live[0]; held.SetInt(0, 54321); pin = held.AsMemory(0, 4).Pin();
                        ready.Set(); release.Wait();
                        for (int i = 1; i < live.Count; i++) live[i].Release();
                    });
                }
                catch (Exception e) { error = e; ready.Set(); }
            });
            owner.Start(); Assert.True(ready.Wait(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));
            try { release.Set(); a.Dispose(); }
            finally { release.Set(); Assert.True(owner.Join(TimeSpan.FromSeconds(15))); }
            Assert.Null(error); Assert.Equal(54321, held.GetInt(0));
            Assert.Equal(131072, a.UsedHeapMemory + a.UsedDirectMemory);
            held.Release(); pin.Dispose(); Assert.Equal(0, a.UsedHeapMemory + a.UsedDirectMemory);
            Assert.Equal(0, native.ReservedBytes); Assert.Equal(0, a.HeapPool.LocalBindingCount + a.DirectPool.LocalBindingCount);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BurstPurgeRetainsBoundedLocalChunksAndScopeExitReclaimsAll(bool direct)
    {
        using var a = new AdaptiveByteBufAllocator(false, false, null, 2, 4, 1, 4,
            true, 1, 4 * 131072, 2 * 131072);
        OnThread(() =>
        {
            var live = new List<ByteBuf>();
            for (int i = 0; i < 12 * 512; i++) live.Add(direct ? a.DirectBuffer(256, 256) : a.HeapBuffer(256, 256));
            foreach (var b in live) b.Release();
            live.Clear();
            long before = a.UsedHeapMemory + a.UsedDirectMemory;
            for (int poll = 0; poll < 40; poll++)
            {
                for (int i = 0; i < 512; i++) live.Add(direct ? a.DirectBuffer(256, 256) : a.HeapBuffer(256, 256));
                foreach (var b in live) b.Release();
                live.Clear();
            }
            long after = a.UsedHeapMemory + a.UsedDirectMemory;
            Assert.True(after < before, $"Before purge: {before}, after purge: {after}");
            Assert.InRange(after, 131072, 4 * 131072);
        }, true);
        Assert.Equal(0, a.UsedHeapMemory + a.UsedDirectMemory);
    }
}
