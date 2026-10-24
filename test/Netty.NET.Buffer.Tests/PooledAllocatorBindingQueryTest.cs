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
using System.Runtime.ExceptionServices;
using System.Threading;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class PooledAllocatorBindingQueryTest
{
    public static IEnumerable<object[]> Cases()
    {
        for (int topology = 0; topology < 4; ++topology)
        foreach (bool direct in new[] { false, true })
        foreach (bool cached in new[] { false, true })
        for (int trigger = 0; trigger < 3; ++trigger) yield return [topology, direct, cached, trigger];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void PresenceQueryIsPhysicalThreadScopedAndNeverInitializesOrTrimsBindings(int topology, bool direct, bool cached, int trigger)
    {
        var native = new NativeMemoryAllocator();
        using var allocator = new PooledByteBufAllocator(false, topology is 0 or 2 ? 1 : 0, topology is 0 or 3 ? 1 : 0,
            pageSize: 4096, maxOrder: 1, nativeAllocator: native, smallCacheSize: cached ? 2 : 0, normalCacheSize: cached ? 2 : 0);
        ByteBuf owner = null;
        try
        {
            for (int i = 0; i < 3; ++i) Assert.False(allocator.HasCurrentThreadBinding);
            Assert.Equal(0, allocator.Metric.NumThreadLocalCaches);
            owner = trigger == 0 ? direct ? allocator.DirectBuffer(64, 128) : allocator.HeapBuffer(64, 128)
                : trigger == 1 ? direct ? allocator.DirectBuffer(0, 0) : allocator.HeapBuffer(0, 0)
                : direct ? allocator.CompositeDirectBuffer(2) : allocator.CompositeHeapBuffer(2);
            bool expected = trigger == 0 && topology != 1;
            Assert.Equal(expected, allocator.HasCurrentThreadBinding);
            long pinnedHeap = allocator.PinnedHeapMemory, pinnedDirect = allocator.PinnedDirectMemory;
            Exception workerFailure = null;
            var worker = new Thread(() =>
            {
                ByteBuf allocation = null;
                try
                {
                    Assert.False(allocator.HasCurrentThreadBinding);
                    allocation = direct ? allocator.DirectBuffer(64, 128) : allocator.HeapBuffer(64, 128);
                    Assert.Equal(topology != 1, allocator.HasCurrentThreadBinding);
                    Assert.Equal(topology != 1, allocator.ReleaseCurrentThreadBinding());
                    Assert.False(allocator.HasCurrentThreadBinding);
                }
                catch (Exception failure) { workerFailure = failure; }
                finally { allocation?.Release(); allocator.ReleaseCurrentThreadBinding(); }
            }) { IsBackground = true };
            worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(10)));
            if (workerFailure != null) ExceptionDispatchInfo.Capture(workerFailure).Throw();
            for (int i = 0; i < 3; ++i) Assert.Equal(expected, allocator.HasCurrentThreadBinding);
            Assert.Equal(expected ? 1 : 0, allocator.Metric.NumThreadLocalCaches);
            Assert.Equal(pinnedHeap, allocator.PinnedHeapMemory); Assert.Equal(pinnedDirect, allocator.PinnedDirectMemory);
            Assert.Equal(expected, allocator.ReleaseCurrentThreadBinding()); Assert.False(allocator.HasCurrentThreadBinding);
            if (trigger == 0) { owner.SetByte(0, 0x5A); Assert.Equal(0x5A, owner.GetByte(0)); }
            owner.Release(); owner = null;
            allocator.Dispose(); Assert.False(allocator.HasCurrentThreadBinding);
            Assert.Equal(0, allocator.Metric.NumThreadLocalCaches);
        }
        finally { owner?.Release(); allocator.Dispose(); Assert.Equal(0, native.ReservedBytes); }
    }
}
