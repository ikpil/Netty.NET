/*
 * Copyright 2013 The Netty Project
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

public class NestedLeakAwareDerivedOwnershipTest
{
    private sealed class Tracker(ByteBuf expected) : IResourceLeakTracker<ByteBuf>
    {
        internal int Closes, Records;
        public void Record() => ++Records;
        public void Record(object hint) => ++Records;
        public bool Close(ByteBuf value) { Assert.Same(expected, value); ++Closes; return true; }
    }
    public static IEnumerable<object[]> Cases()
    {
        for (int provider = 0; provider < 6; ++provider)
        for (int depth = 2; depth <= 3; ++depth)
        for (int mask = 0; mask < 1 << depth; ++mask)
        for (int factory = 0; factory < 4; ++factory)
        foreach (bool parentFirst in new[] { false, true }) yield return [provider, depth, mask, factory, parentFirst];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void RetainedChildrenPreserveEveryNestedParentTrackerUntilItsActualFinalRelease(int provider, int depth, int mask, int factory, bool parentFirst)
    {
        var native = new NativeMemoryAllocator();
        bool pooled = provider is 2 or 3, direct = (provider & 1) != 0;
        IByteBufAllocator allocator = pooled ? new PooledByteBufAllocator(false, maxOrder: 4, nativeAllocator: native)
            : new UnpooledByteBufAllocator(false, true, native);
        ByteBuf Data(int count)
        {
            ByteBuf b = direct ? allocator.DirectBuffer(count, 64) : allocator.HeapBuffer(count, 64);
            for (int i = 0; i < count; ++i) b.WriteByte(i + 1);
            return b;
        }
        ByteBuf root = provider >= 4 ? new CompositeByteBuf(allocator, direct, 16, Data(8), Data(24)) : Data(32);
        root.SetIndex(2, 24); ByteBuf parent = root;
        var trackers = new List<Tracker>();
        for (int i = 0; i < depth; ++i)
        {
            var tracker = new Tracker(parent); trackers.Add(tracker);
            parent = (mask & 1 << i) != 0 ? new AdvancedLeakAwareByteBuf(parent, tracker) : new SimpleLeakAwareByteBuf(parent, tracker);
        }
        ByteBuf child = null; bool childReleased = false, parentReleased = false;
        try
        {
            child = factory switch
            {
                0 => parent.RetainedSlice(3, 8), 1 => parent.RetainedSlice(),
                2 => parent.RetainedDuplicate(), _ => parent.ReadRetainedSlice(8)
            };
            Assert.Equal(2, root.ReferenceCount); Assert.Equal(pooled ? 1 : 2, child.ReferenceCount);
            Assert.All(trackers, t => Assert.Equal(0, t.Closes));
            IResourceLeakTracker<ByteBuf> childTracker = AllocatorLeakDetectionTest.Tracker(child);
            if (pooled) Assert.All(trackers, t => Assert.NotSame(t, childTracker));
            else Assert.Same(trackers[^1], childTracker);
            int[] records = trackers.ConvertAll(t => t.Records).ToArray();
            byte expected = child.GetByte(child.ReaderIndex);
            if (pooled) Assert.Equal(records, trackers.ConvertAll(t => t.Records).ToArray());
            if (parentFirst)
            {
                Assert.False(parent.Release()); parentReleased = true;
                Assert.Equal(1, root.ReferenceCount); Assert.Equal(1, child.ReferenceCount);
                Assert.All(trackers, t => Assert.Equal(0, t.Closes));
                records = trackers.ConvertAll(t => t.Records).ToArray();
                Assert.Equal(expected, child.GetByte(child.ReaderIndex));
                if (pooled) Assert.Equal(records, trackers.ConvertAll(t => t.Records).ToArray());
                Assert.True(child.Release()); childReleased = true;
            }
            else
            {
                Assert.Equal(pooled, child.Release()); childReleased = true;
                Assert.Equal(1, root.ReferenceCount); Assert.Equal(pooled ? 0 : 1, child.ReferenceCount);
                Assert.All(trackers, t => Assert.Equal(0, t.Closes));
                Assert.Equal(factory == 3 ? 10 : 2, root.ReaderIndex);
                Assert.True(parent.Release()); parentReleased = true;
            }
            Assert.Equal(0, root.ReferenceCount); Assert.Equal(0, child.ReferenceCount);
            Assert.All(trackers, t => Assert.Equal(1, t.Closes));
        }
        finally
        {
            if (!childReleased && child is { ReferenceCount: > 0 }) child.Release();
            if (!parentReleased && parent.ReferenceCount > 0) parent.Release();
            (allocator as IDisposable)?.Dispose(); Assert.Equal(0, native.ReservedBytes);
        }
    }
}
